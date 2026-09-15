using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Runs the batch experiment: 4 scenarios x 27 repetitions x 2 controllers = 216 runs (Night dropped 10 Sep).
/// Fixed and Adaptive share a seed so the paired t-test is valid.
/// Lane names come from IntersectionConfig, so it works with the original 3 lanes and the full 7 (A2 included).
/// </summary>
public class ScenarioManager : MonoBehaviour
{
    [System.Serializable]
    public class ScenarioDef
    {
        public string name;
        public List<LaneArrivalRate> laneRates = new List<LaneArrivalRate>();

        public float GetInterval(string lane, float fallback)
        {
            foreach (LaneArrivalRate r in laneRates)
            {
                if (r != null && r.laneName == lane) return r.meanInterval;
            }
            return fallback;
        }
    }

    /// <summary>Accumulated results for one scenario, split by Fixed / Adaptive</summary>
    [System.Serializable]
    public class ScenarioAggregate
    {
        public string name;
        public List<float> fixedWait     = new List<float>();
        public List<float> adaptiveWait  = new List<float>();
        public List<float> fixedMax      = new List<float>();
        public List<float> adaptiveMax   = new List<float>();
        public List<int>   fixedCleared  = new List<int>();
        public List<int>   adaptiveCleared = new List<int>();
        public int fixedStarvation = 0;
        public int adaptiveStarvation = 0;

        public static float Mean(List<float> v)
        {
            if (v.Count == 0) return -1f;
            float s = 0f; foreach (float x in v) s += x; return s / v.Count;
        }
        public static float MeanI(List<int> v)
        {
            if (v.Count == 0) return -1f;
            float s = 0f; foreach (int x in v) s += x; return s / v.Count;
        }

        public float FixedAvgWait    => Mean(fixedWait);
        public float AdaptiveAvgWait => Mean(adaptiveWait);
        public float FixedMaxWait    => Mean(fixedMax);
        public float AdaptiveMaxWait => Mean(adaptiveMax);
        public float FixedThroughput    => MeanI(fixedCleared);
        public float AdaptiveThroughput => MeanI(adaptiveCleared);

        /// <summary>Percentage by which Adaptive cut the wait (positive = Adaptive better)</summary>
        public float ImprovementPercent
        {
            get
            {
                float f = FixedAvgWait, a = AdaptiveAvgWait;
                if (f <= 0f || a < 0f) return 0f;
                return (f - a) / f * 100f;
            }
        }

        public bool HasBoth => fixedWait.Count > 0 && adaptiveWait.Count > 0;
        public int PairCount => Mathf.Min(fixedWait.Count, adaptiveWait.Count);
    }

    [Header("References")]
    public IntersectionConfig config;
    public FixedController fixedController;
    public AdaptiveController adaptiveController;
    public DataLogger dataLogger;

    [Header("Run Settings")]
    [Tooltip("Length of each run in simulated seconds (1800 = 30 minutes)")]
    public float runDuration = BATCH_RUN_DURATION;
    [Tooltip("Repetitions per scenario (27 x 4 scenarios = 108 scenarios = 216 runs)")]
    public int repetitionsPerScenario = BATCH_REPETITIONS;
    [Tooltip("Simulation speed-up during a batch run (1 = real time, 10 = ten times faster)")]
    public float simulationSpeed = BATCH_SIM_SPEED;

    // ===== Batch defaults - the single source of truth =====
    // Unity serialises a scene component's values into SampleScene.unity and always overrides the
    //    script defaults. The scene was once stuck at repetitionsPerScenario = 1 (8 runs, not 216) and
    //    simulationSpeed = 2. Editing the .cs is not enough; ABACSceneBuilder must call LoadBatchPreset().
    public const int   BATCH_REPETITIONS  = 27;
    // 10x is safe now that CarAgent sub-steps at <= 0.02 s (before that cars overshot lanes at 10x,
    // stepping 2.3 m at a time). The only thing still tied to timeScale is ROI trigger timing, which is
    // frame-grained (~0.17 s at 10x/60fps), an undirected +/-0.17 s error per vehicle WaitTime - small
    // against a 2 s snapshot. Kimmy chose 10x on 10 Sep after sub-stepping landed (216 runs in about
    // 11.5 h instead of 23). The 1x/5x/10x validation on one seed (SODA_BASELINE 8.6) is still worth recording.
    public const float BATCH_SIM_SPEED    = 10f;
    public const float BATCH_RUN_DURATION = 1800f;
    public const float BATCH_WARMUP       = 120f;
    [Tooltip("Warm-up before data collection starts, in simulated seconds - stops the empty road at the start skewing results")]
    public float warmupDuration = BATCH_WARMUP;

    [Header("Performance (GUI is disabled during a batch for speed)")]
    public bool disableGUIDuringBatch = true;

    [Header("Arrival Interval Mapping (mean seconds between cars)")]
    public float veryHigh = 1.5f;
    public float high = 2.5f;
    public float medium = 4f;
    public float low = 7f;
    public float veryLow = 15f;
    [Tooltip("A genuinely closed lane (confirmed by security) - a very large mean interval instead of zero, since Poisson divides by the interval")]
    public float closed = 999999f;

    [Tooltip("Share of through traffic using the outer lane A2 (shared with Ax).\n" +
             "5.4% is a direct count from clip A_morning (56 through vehicles: 53 inner, 3 shared), per SP1_HANDOFF_v2.\n" +
             "It replaces the earlier 18%, which was inferred backwards from a blocking rate.")]
    [Range(0.01f, 0.99f)] public float a2Share = MEASURED_A2_SHARE;

    /// <summary>
    /// Share of through traffic using the outer lane (shared with Ax).
    /// History: guessed 50/50 -> back-calculated 18% -> now 5.4% from a direct count (SP1_HANDOFF_v2 section 5).
    /// Must always equal TestCasePresets.A2_SHARE, or the batch and cases T1-T13 use different splits.
    /// </summary>
    public const float MEASURED_A2_SHARE = 0.054f;

    [Header("Scenario Table (press Load ABAC Scenario Preset if empty)")]
    public List<ScenarioDef> scenarios = new List<ScenarioDef>();

    private bool isRunning = false;
    /// <summary>true = a batch run is in progress (the Control Panel checks this before touching timeScale)</summary>
    public bool IsBatchRunning => isRunning;
    // These must stay two separate counters: startedRuns counts when a run begins, finishedRuns when
    // it ends. Using one for the ETA division gives a wrong figure (see the comment on MeasuredRealPerRun).
    private int startedRuns = 0;
    private int finishedRuns = 0;
    private int totalRuns;
    private string currentLabel = "";
    private float currentRunStartRealtime = 0f;
    private float elapsedAtLastFinish = 0f;

    // ===================== Data for BatchRunPanel to read =====================
    private List<ScenarioAggregate> results = new List<ScenarioAggregate>();
    private float batchStartRealtime = 0f;
    private float batchEndRealtime = 0f;
    private bool batchFinished = false;
    private string currentScenarioName = "";
    private int currentRep = 0;
    private DataLogger.ControllerType currentController = DataLogger.ControllerType.Fixed;

    public List<ScenarioAggregate> Results => results;
    /// <summary>Runs that have actually finished</summary>
    public int CompletedRuns => finishedRuns;
    /// <summary>Runs started, including the one in progress</summary>
    public int StartedRuns => startedRuns;
    public int TotalRuns => totalRuns;
    public string CurrentScenarioName => currentScenarioName;
    public int CurrentRep => currentRep;
    public DataLogger.ControllerType CurrentController => currentController;
    public bool BatchFinished => batchFinished;

    /// <summary>
    /// Real time spent per run.
    ///
    /// Once a run has finished, average the real measurements (most accurate).
    /// Before that, use the theoretical figure: simulated seconds per run divided by timeScale.
    ///
    /// Never estimate from the time spent on the current run: that grows continuously, so the ETA
    /// would rise instead of fall. CurrentRunFraction01 is capped at 1 instead.
    /// The figure holds still until the first run finishes, then is re-estimated from measurement.
    /// </summary>
    float MeasuredRealPerRun
    {
        get
        {
            if (finishedRuns >= 1) return elapsedAtLastFinish / finishedRuns;
            return (warmupDuration + runDuration) / Mathf.Max(0.01f, simulationSpeed);
        }
    }

    /// <summary>Progress of the run currently in flight (0-1)</summary>
    public float CurrentRunFraction01
    {
        get
        {
            if (!isRunning) return 0f;
            float per = MeasuredRealPerRun;
            if (per <= 0.001f) return 0f;
            return Mathf.Clamp01((Time.realtimeSinceStartup - currentRunStartRealtime) / per);
        }
    }

    // Finished runs plus the fraction of the current one, so the bar moves smoothly rather than in jumps
    public float Progress01 =>
        totalRuns > 0 ? Mathf.Clamp01((finishedRuns + CurrentRunFraction01) / totalRuns) : 0f;

    /// <summary>Real time elapsed so far, in seconds</summary>
    public float ElapsedRealtime =>
        isRunning ? Time.realtimeSinceStartup - batchStartRealtime
                  : (batchFinished ? batchEndRealtime - batchStartRealtime : 0f);

    /// <summary>
    /// Estimated time remaining, in seconds.
    ///
    /// The old formula was elapsed / completedRuns x runs remaining, which was wrong because
    /// completedRuns counted runs *started*, not finished. While the first run was still going the
    /// divisor stayed at 1 while elapsed kept growing, so the estimate rose instead of falling.
    /// It now divides by runs actually finished and subtracts the current run fraction.
    ///
    /// The number therefore only ever decreases.
    /// </summary>
    public float EstimatedRemaining
    {
        get
        {
            if (!isRunning) return -1f;
            float per = MeasuredRealPerRun;
            float runsLeft = (totalRuns - finishedRuns) - CurrentRunFraction01;
            return Mathf.Max(0f, per * runsLeft);
        }
    }

    /// <summary>Mean across all scenarios (each scenario weighted equally)</summary>
    public void GetOverall(out float fixedWait, out float adaptiveWait, out float improvePct)
    {
        float fs = 0f, as_ = 0f; int n = 0;
        foreach (var r in results)
        {
            if (!r.HasBoth) continue;
            fs += r.FixedAvgWait; as_ += r.AdaptiveAvgWait; n++;
        }
        if (n == 0) { fixedWait = -1f; adaptiveWait = -1f; improvePct = 0f; return; }
        fixedWait = fs / n;
        adaptiveWait = as_ / n;
        improvePct = fixedWait > 0f ? (fixedWait - adaptiveWait) / fixedWait * 100f : 0f;
    }

    ScenarioAggregate GetAggregate(string name)
    {
        foreach (var r in results) if (r.name == name) return r;
        var fresh = new ScenarioAggregate { name = name };
        results.Add(fresh);
        return fresh;
    }

    void RecordResult(string scenarioName, DataLogger.ControllerType ctype)
    {
        var agg = GetAggregate(scenarioName);
        float wait = dataLogger.GetAverageWaitTime();
        float max  = dataLogger.GetMaxWaitTime();
        int cleared = dataLogger.GetVehicleCount();
        int starv = dataLogger.GetStarvationCount();

        if (ctype == DataLogger.ControllerType.Fixed)
        {
            agg.fixedWait.Add(wait); agg.fixedMax.Add(max);
            agg.fixedCleared.Add(cleared); agg.fixedStarvation += starv;
        }
        else
        {
            agg.adaptiveWait.Add(wait); agg.adaptiveMax.Add(max);
            agg.adaptiveCleared.Add(cleared); agg.adaptiveStarvation += starv;
        }
    }

    /// <summary>Resets the batch parameters to the standard set (ABACSceneBuilder calls this on every scene build)</summary>
    [ContextMenu("Load Batch Preset (27 reps / 10x / 1800s / 5.4%)")]
    public void LoadBatchPreset()
    {
        repetitionsPerScenario = BATCH_REPETITIONS;
        simulationSpeed        = BATCH_SIM_SPEED;
        runDuration            = BATCH_RUN_DURATION;
        warmupDuration         = BATCH_WARMUP;
        a2Share                = MEASURED_A2_SHARE;
    }

    /// <summary>
    /// A wrong a2Share makes the batch and cases T1-T13 use different splits, so results cannot be compared.
    /// It is therefore corrected and reported as an error, unlike repetitions, which may legitimately be
    /// lowered for a quick test. Returns true if it had to change anything, meaning the scene needs saving.
    /// </summary>
    public bool EnforceMeasuredShare()
    {
        if (Mathf.Abs(a2Share - MEASURED_A2_SHARE) <= 0.0001f) return false;

        float old = a2Share;
        a2Share = MEASURED_A2_SHARE;
        Debug.LogError(
            $"[ScenarioManager] the a2Share stored in the scene ({old:F3}) does not match the counted value " +
            $"({MEASURED_A2_SHARE:F3}) - corrected automatically.\n" +
            $"  0.18 is the old back-calculated value that SP1_HANDOFF_v2 section 5 replaced with a direct count.\n" +
            $"  Corrected in memory only: press Load Batch Preset in the Inspector, then Save Scene.");
        return true;
    }

    void Awake()
    {
        EnforceMeasuredShare();

        if (config == null) config = FindFirstObjectByType<IntersectionConfig>();
        if (config != null) config.EnsureBuilt();
    }

    // ===================== Batch control =====================

    [ContextMenu("Start Batch Run")]
    public void StartBatchRun()
    {
        EnforceMeasuredShare();
        if (repetitionsPerScenario != BATCH_REPETITIONS)
        {
            Debug.LogWarning(
                $"[ScenarioManager] repetitionsPerScenario = {repetitionsPerScenario} " +
                $"(the standard value is {BATCH_REPETITIONS}), which gives " +
                $"{scenarios.Count * repetitionsPerScenario * 2} runs rather than " +
                $"{scenarios.Count * BATCH_REPETITIONS * 2} runs — " +
                $"Is that intentional? If not, press Load Batch Preset in the Inspector first.");
        }

        if (isRunning)
        {
            Debug.LogWarning("[ScenarioManager] Already running!");
            return;
        }
        if (config == null)
        {
            Debug.LogError("[ScenarioManager] IntersectionConfig has not been assigned");
            return;
        }
        if (scenarios.Count == 0)
        {
            Debug.LogError("[ScenarioManager] no scenarios set - press 'Load ABAC Scenario Preset' first");
            return;
        }
        StartCoroutine(RunAllExperiments());
    }

    [ContextMenu("Stop Batch Run")]
    public void StopBatchRun()
    {
        StopAllCoroutines();
        Time.timeScale = 1f;
        isRunning = false;
        batchEndRealtime = Time.realtimeSinceStartup;
        // Results so far are still usable; treat it as finished but incomplete
        batchFinished = results.Count > 0;
        Debug.Log("[ScenarioManager] Batch run stopped manually.");
    }

    /// <summary>Closes the summary screen (called by BatchRunPanel)</summary>
    public void DismissSummary()
    {
        batchFinished = false;
    }

    void LogFinalSummary()
    {
        float f, a, pct;
        GetOverall(out f, out a, out pct);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("======= Batch Run Summary =======");
        sb.AppendLine($"{"Scenario",-16}{"Fixed",10}{"Adaptive",11}{"Cut %",9}");
        foreach (var r in results)
        {
            if (!r.HasBoth) continue;
            sb.AppendLine($"{r.name,-16}{r.FixedAvgWait,10:F2}{r.AdaptiveAvgWait,11:F2}{r.ImprovementPercent,8:F1}%");
        }
        sb.AppendLine($"{"All scenarios",-16}{f,10:F2}{a,11:F2}{pct,8:F1}%");
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Print Estimated Runtime")]
    public void PrintEstimatedRuntime()
    {
        int runs = scenarios.Count * repetitionsPerScenario * 2;
        float simSeconds = runs * (runDuration + warmupDuration);
        float realSeconds = simSeconds / Mathf.Max(0.01f, simulationSpeed);
        Debug.Log($"[ScenarioManager] {runs} runs x {runDuration + warmupDuration:F0}s = " +
                  $"{simSeconds / 3600f:F1} simulated hours -> at {simulationSpeed}x that is roughly " +
                  $"{realSeconds / 3600f:F1} real hours (if the machine keeps up)");
    }

    IEnumerator RunAllExperiments()
    {
        isRunning = true;
        batchFinished = false;
        startedRuns = 0;
        finishedRuns = 0;
        elapsedAtLastFinish = 0f;
        totalRuns = scenarios.Count * repetitionsPerScenario * 2;
        results.Clear();
        batchStartRealtime = Time.realtimeSinceStartup;
        currentRunStartRealtime = batchStartRealtime;

        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        Time.timeScale = simulationSpeed;

        List<bool> savedGUI = null;
        if (disableGUIDuringBatch) savedGUI = DisableAllGUI();

        for (int s = 0; s < scenarios.Count; s++)
        {
            for (int rep = 1; rep <= repetitionsPerScenario; rep++)
            {
                yield return StartCoroutine(RunSingleExperiment(scenarios[s], rep, DataLogger.ControllerType.Fixed));
                yield return StartCoroutine(RunSingleExperiment(scenarios[s], rep, DataLogger.ControllerType.Adaptive));
            }
        }

        Time.timeScale = 1f;
        QualitySettings.vSyncCount = 1;
        if (disableGUIDuringBatch && savedGUI != null) RestoreAllGUI(savedGUI);

        isRunning = false;
        batchFinished = true;
        batchEndRealtime = Time.realtimeSinceStartup;
        Debug.Log($"[ScenarioManager] ═══ ALL {totalRuns} RUNS COMPLETE ═══");
        LogFinalSummary();
    }

    IEnumerator RunSingleExperiment(ScenarioDef scenario, int repetition, DataLogger.ControllerType ctype)
    {
        // ===== Same seed for the Fixed/Adaptive pair, so both see the same traffic and the t-test is valid =====
        int seed = IntersectionConfig.StableHash(scenario.name) + repetition * 31;
        Random.InitState(seed);

        ApplyArrivalRates(scenario);
        config.ResetAllLanes(seed);
        dataLogger.ResetRun();

        // ===== Always stop both controllers' coroutines first, or two groups could hold green at once =====
        fixedController.ForceStop();
        adaptiveController.ForceStop();

        bool useFixed = (ctype == DataLogger.ControllerType.Fixed);
        fixedController.enabled = useFixed;
        adaptiveController.enabled = !useFixed;

        if (useFixed) fixedController.RestartCycle();
        else adaptiveController.RestartCycle();

        dataLogger.controllerType = ctype;
        dataLogger.scenarioName = $"{scenario.name}_Rep{repetition}";
        currentLabel = $"{dataLogger.scenarioName} [{ctype}] seed={seed}";
        currentScenarioName = scenario.name;
        currentRep = repetition;
        currentController = ctype;

        startedRuns++;
        currentRunStartRealtime = Time.realtimeSinceStartup;
        Debug.Log($"[ScenarioManager] ({startedRuns}/{totalRuns}) Running {currentLabel}...");

        // ===== Warm-up: let traffic reach a normal state, then clear the data and start measuring =====
        if (warmupDuration > 0f)
        {
            yield return new WaitForSeconds(warmupDuration);
            dataLogger.ResetRun();
            config.ResetSpawnCounts();
        }

        yield return new WaitForSeconds(runDuration);

        dataLogger.SetTotalSpawned(config.GetTotalSpawned());
        RecordResult(scenario.name, ctype);
        dataLogger.ExportDetailedCSV();
        dataLogger.ExportSummaryCSV();
        dataLogger.ExportPhaseCSV();

        finishedRuns++;
        elapsedAtLastFinish = ElapsedRealtime;

        Debug.Log($"[ScenarioManager] ✓ Completed {dataLogger.scenarioName} [{ctype}]. " +
                  $"Vehicles={dataLogger.GetVehicleCount()}, AvgWait={dataLogger.GetAverageWaitTime():F1}s, " +
                  $"Starvation={dataLogger.GetStarvationCount()}");
    }

    // ===================== Lane helpers (generic, driven by IntersectionConfig) =====================

    void ApplyArrivalRates(ScenarioDef scenario)
    {
        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null || lane.spawner == null) continue;
            float interval = scenario.GetInterval(lane.laneName, low);
            lane.spawner.UsePoisson(interval);   // a batch run always uses Poisson
        }
    }

    List<bool> DisableAllGUI()
    {
        List<bool> saved = new List<bool> { dataLogger.showDebugGUI, adaptiveController.showDebugGUI,
                                            fixedController.showDebugGUI, adaptiveController.showDecisionPanel };
        dataLogger.showDebugGUI = false;
        adaptiveController.showDebugGUI = false;
        fixedController.showDebugGUI = false;
        // The decision-trace panel must be off during a batch too - it builds a string every phase, 216 times over, with nobody watching
        adaptiveController.showDecisionPanel = false;

        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null || lane.roi == null) continue;
            saved.Add(lane.roi.showDebugGUI);
            lane.roi.showDebugGUI = false;
        }
        return saved;
    }

    void RestoreAllGUI(List<bool> saved)
    {
        int i = 0;
        dataLogger.showDebugGUI = saved[i++];
        adaptiveController.showDebugGUI = saved[i++];
        fixedController.showDebugGUI = saved[i++];
        adaptiveController.showDecisionPanel = saved[i++];

        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null || lane.roi == null) continue;
            if (i < saved.Count) lane.roi.showDebugGUI = saved[i++];
        }
    }

    // ===================== Preset =====================

    /// <summary>
    /// Five scenarios covering the traffic patterns outside ABAC:
    ///   MorningPeak  - heavy inbound (Ax, Bx busy / outbound CB, CA light)
    ///   EveningPeak  - heavy outbound (CB, CA busy / inbound light)
    ///   MiddayNormal - ordinary traffic on every approach
    ///   ABACEvent    - graduation or a large event, heavy in and out at once
    ///   Night        - light on every approach
    /// </summary>
    // MorningPeak and MiddayNormal use figures measured by Itim (detector run over all 8 clips, revised 24 Aug).
    // The rest (EveningPeak, ABACEvent, Night) are still bucket assumptions with no measurement behind them.
    // MorningPeak CB/CA = closed: lane C actually shuts only 08:30-09:00 (15-30 min), not all morning, but that
    //   coincides with the real peak (08:15-09:00, ending right after 09:00), so closed is still right here.
    // MiddayNormal CB/CA = 10.1 / 13.2 s measured by Itim (10 Sep) - previously left at low=7 despite having the figures.
    // The other scenarios (EveningPeak medium/low, ABACEvent, Night) are assumed levels, not measurements.
    // Confirmed: 09:00-15:00 the signal really does flash amber rather than run fixed-time, and MiddayNormal
    //   (11:37-12:00) falls in that window, so its Fixed vs Adaptive result is hypothetical. State the caveat in the report.
    [ContextMenu("Load ABAC Scenario Preset (4 scenarios x 7 lanes)")]
    public void LoadABACScenarioPreset()
    {
        scenarios = new List<ScenarioDef>
        {
            MakeScenario("MorningPeak",  A: 18.8f, Ax: 5.1f,   B: 9.0f,  Bx: 9.6f,     CB: closed,  CA: closed),
            MakeScenario("EveningPeak",  A: medium,  Ax: low,      B: high,   Bx: low,      CB: 8.4f,    CA: 13.4f),
            MakeScenario("MiddayNormal", A: 29.0f, Ax: 9.7f,   B: 20.5f, Bx: 21.4f,    CB: 10.1f,   CA: 13.2f),
            MakeScenario("ABACEvent",    A: medium,  Ax: veryHigh, B: medium, Bx: veryHigh, CB: high,    CA: high),
            // Night dropped from the batch (10 Sep, Kimmy's decision): its rates are pure assumption and it
            // is out of scope (there is no night-time problem to solve). Still in T12 of TestCaseLibrary for manual runs.
        };

        Debug.Log($"[ScenarioManager] loaded a preset of {scenarios.Count} scenarios -> " +
                  $"{scenarios.Count * repetitionsPerScenario} scenarios, " +
                  $"{scenarios.Count * repetitionsPerScenario * 2} runs");
    }

    /// <summary>
    /// The A argument is the combined through-traffic rate, before the shared lane splits it, divided
    /// between A (inner) and A2 (outer) exactly as TestCaseLibrary.Poisson() does, by a2Share.
    /// </summary>
    ScenarioDef MakeScenario(string name, float A, float Ax, float B, float Bx, float CB, float CA)
    {
        float aInner = A / (1f - a2Share);
        float aOuter = A / a2Share;
        return new ScenarioDef
        {
            name = name,
            laneRates = new List<LaneArrivalRate>
            {
                new LaneArrivalRate("A",  aInner),
                new LaneArrivalRate("A2", aOuter),
                new LaneArrivalRate("Ax", Ax),
                new LaneArrivalRate("B",  B),
                new LaneArrivalRate("Bx", Bx),
                new LaneArrivalRate("CB", CB),
                new LaneArrivalRate("CA", CA),
            }
        };
    }

    void OnGUI()
    {
        if (!isRunning) return;

        UITheme.Begin();

        const float W = 380f;
        Rect card = new Rect(Screen.width / 2f - W / 2f, 10f, W, 84f);
        GUI.Box(card, GUIContent.none);

        GUI.Label(new Rect(card.x + 12f, card.y + 8f, 200f, 18f), "BATCH RUN", UITheme.Title);
        UITheme.Pill(new Rect(card.x + W - 92f, card.y + 9f, 80f, 17f),
                     $"{Time.timeScale:0.#}x speed", UITheme.AccentDark);

        UITheme.Bar(new Rect(card.x + 12f, card.y + 32f, W - 24f, 8f), Progress01, UITheme.Accent);

        GUI.Label(new Rect(card.x + 12f, card.y + 44f, W * 0.5f, 16f),
                  $"{finishedRuns}/{totalRuns} runs  ({Progress01 * 100f:F0}%)", UITheme.Body);
        float rem = EstimatedRemaining;
        GUI.Label(new Rect(card.x + W * 0.5f - 12f, card.y + 44f, W * 0.5f, 16f),
                  rem >= 0f ? $"~{rem / 60f:F0} min left" : "", UITheme.Right);
        GUI.Label(new Rect(card.x + 12f, card.y + 62f, W - 24f, 16f), currentLabel, UITheme.Dim);

        UITheme.End();
    }
}
