using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// รัน batch experiment: 5 scenario x 27 repetition x 2 controller = 270 runs
/// ใช้ seed เดียวกันคู่ Fixed/Adaptive เพื่อให้ paired t-test valid
/// อ่านรายชื่อเลนจาก IntersectionConfig จึงรองรับทั้ง 3 เลนเดิมและ 7 เลนเต็มระบบ (รวม A2 shared lane)
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

    /// <summary>ผลสะสมของ scenario หนึ่ง แยก Fixed / Adaptive</summary>
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

        /// <summary>เปอร์เซ็นต์ที่ Adaptive ลดเวลารอได้ (บวก = Adaptive ดีกว่า)</summary>
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
    [Tooltip("ความยาวของแต่ละ run เป็นวินาทีจำลอง (1800 = 30 นาที)")]
    public float runDuration = 1800f;
    [Tooltip("จำนวนรอบต่อ scenario (27 x 5 scenario = 135 scenarios)")]
    public int repetitionsPerScenario = 27;
    [Tooltip("เร่งความเร็ว simulation ระหว่าง batch run (1 = ปกติ, 10 = เร็ว 10 เท่า)")]
    public float simulationSpeed = 10f;
    [Tooltip("เวลาอุ่นเครื่องก่อนเริ่มเก็บข้อมูล (วินาทีจำลอง) — กันช่วงถนนว่างตอนเริ่ม run ทำให้ค่าเพี้ยน")]
    public float warmupDuration = 120f;

    [Header("Performance (ปิด GUI ระหว่าง batch เพื่อความเร็ว)")]
    public bool disableGUIDuringBatch = true;

    [Header("Arrival Interval Mapping (วินาทีเฉลี่ยระหว่างรถ)")]
    public float veryHigh = 1.5f;
    public float high = 2.5f;
    public float medium = 4f;
    public float low = 7f;
    public float veryLow = 15f;
    [Tooltip("เลน 'ปิด' จริง (ยืนยันจาก รปภ.) — mean interval ใหญ่มาก ๆ แทนศูนย์ (Poisson หาร rate จาก interval)")]
    public float closed = 999999f;

    [Tooltip("สัดส่วนรถตรงที่เข้าเลนนอก A2 (เลนแชร์กับ Ax) เทียบกับรถตรงทั้งหมด\n" +
             "5.4% = ค่านับตรงจากคลิป A_morning (รถตรง 56 คัน เข้าเลนใน 53 เข้าเลนแชร์ 3) ตาม SP1_HANDOFF_v2\n" +
             "แทนค่าเดิม 18% ที่ได้จากการอนุมานย้อนจากอัตราการบล็อก")]
    [Range(0.01f, 0.99f)] public float a2Share = 0.054f;

    [Header("Scenario Table (กด Load ABAC Scenario Preset ถ้ายังว่าง)")]
    public List<ScenarioDef> scenarios = new List<ScenarioDef>();

    private bool isRunning = false;
    /// <summary>true = batch run กำลังทำงานอยู่ (Control Panel ใช้เช็คก่อนแตะ timeScale)</summary>
    public bool IsBatchRunning => isRunning;
    // ⚠️ ต้องแยก 2 ตัว: startedRuns นับตอน "เริ่ม" run, finishedRuns นับตอน "จบ" run
    // ถ้าใช้ตัวเดียวแล้วเอาไปหาร ETA จะได้ค่าเพี้ยน (ดูคอมเมนต์ที่ MeasuredRealPerRun)
    private int startedRuns = 0;
    private int finishedRuns = 0;
    private int totalRuns;
    private string currentLabel = "";
    private float currentRunStartRealtime = 0f;
    private float elapsedAtLastFinish = 0f;

    // ===================== ข้อมูลให้ BatchRunPanel อ่าน =====================
    private List<ScenarioAggregate> results = new List<ScenarioAggregate>();
    private float batchStartRealtime = 0f;
    private float batchEndRealtime = 0f;
    private bool batchFinished = false;
    private string currentScenarioName = "";
    private int currentRep = 0;
    private DataLogger.ControllerType currentController = DataLogger.ControllerType.Fixed;

    public List<ScenarioAggregate> Results => results;
    /// <summary>จำนวน run ที่ "จบแล้วจริง ๆ"</summary>
    public int CompletedRuns => finishedRuns;
    /// <summary>จำนวน run ที่เริ่มไปแล้ว (รวมตัวที่กำลังรันอยู่)</summary>
    public int StartedRuns => startedRuns;
    public int TotalRuns => totalRuns;
    public string CurrentScenarioName => currentScenarioName;
    public int CurrentRep => currentRep;
    public DataLogger.ControllerType CurrentController => currentController;
    public bool BatchFinished => batchFinished;

    /// <summary>
    /// เวลาจริงที่ใช้ต่อ 1 run
    ///
    /// ถ้ามี run จบแล้ว → เฉลี่ยจากของจริง (แม่นสุด)
    /// ถ้ายังไม่มี → ใช้ค่าทางทฤษฎี: เวลาจำลองต่อ run ÷ timeScale
    ///
    /// ห้ามเอาเวลาที่ใช้ไปกับ run ปัจจุบันมาเป็นตัวประมาณ เพราะมันโตขึ้นเรื่อย ๆ
    /// จะทำให้ ETA เพิ่มแทนที่จะลด — ปล่อยให้ CurrentRunFraction01 ตันที่ 1 แทน
    /// ตัวเลขจะ "ค้าง" ไว้จนกว่า run แรกจบ แล้วค่อยประเมินใหม่จากของจริง
    /// </summary>
    float MeasuredRealPerRun
    {
        get
        {
            if (finishedRuns >= 1) return elapsedAtLastFinish / finishedRuns;
            return (warmupDuration + runDuration) / Mathf.Max(0.01f, simulationSpeed);
        }
    }

    /// <summary>ความคืบหน้าของ run ที่กำลังรันอยู่ (0-1)</summary>
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

    // นับ run ที่จบแล้ว + เศษของ run ปัจจุบัน → หลอดเดินนุ่ม ไม่กระตุกทีละก้อน
    public float Progress01 =>
        totalRuns > 0 ? Mathf.Clamp01((finishedRuns + CurrentRunFraction01) / totalRuns) : 0f;

    /// <summary>เวลาจริงที่ใช้ไปแล้ว (วินาที)</summary>
    public float ElapsedRealtime =>
        isRunning ? Time.realtimeSinceStartup - batchStartRealtime
                  : (batchFinished ? batchEndRealtime - batchStartRealtime : 0f);

    /// <summary>
    /// ประมาณเวลาที่เหลือ (วินาที)
    ///
    /// สูตรเดิมคือ elapsed / completedRuns × runs ที่เหลือ ซึ่งผิด
    /// เพราะ completedRuns นับตอน "เริ่ม" run ไม่ใช่ตอนจบ
    /// ระหว่างที่ run แรกยังรันอยู่ ตัวหารค้างที่ 1 แต่ตัวตั้ง (elapsed) โตขึ้นเรื่อย ๆ
    /// → เวลาที่เหลือเลยเพิ่มขึ้นแทนที่จะลดลง
    ///
    /// ตอนนี้หารด้วย run ที่จบจริง แล้วลบเศษของ run ปัจจุบันออก → ค่าลดลงตลอด
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

    /// <summary>ค่าเฉลี่ยรวมทุก scenario (ถ่วงน้ำหนักเท่ากันทุก scenario)</summary>
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

    void Awake()
    {
        if (config == null) config = FindFirstObjectByType<IntersectionConfig>();
        if (config != null) config.EnsureBuilt();
    }

    // ===================== Batch control =====================

    [ContextMenu("Start Batch Run")]
    public void StartBatchRun()
    {
        if (isRunning)
        {
            Debug.LogWarning("[ScenarioManager] Already running!");
            return;
        }
        if (config == null)
        {
            Debug.LogError("[ScenarioManager] ยังไม่ได้ลาก IntersectionConfig");
            return;
        }
        if (scenarios.Count == 0)
        {
            Debug.LogError("[ScenarioManager] ยังไม่ได้ตั้ง scenario — กด 'Load ABAC Scenario Preset' ก่อน");
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
        // ผลที่รันไปแล้วยังใช้ดูได้ ถือว่า "จบแบบไม่ครบ"
        batchFinished = results.Count > 0;
        Debug.Log("[ScenarioManager] Batch run stopped manually.");
    }

    /// <summary>ปิดหน้าสรุป (BatchRunPanel เรียก)</summary>
    public void DismissSummary()
    {
        batchFinished = false;
    }

    void LogFinalSummary()
    {
        float f, a, pct;
        GetOverall(out f, out a, out pct);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("═══════ สรุปผล Batch Run ═══════");
        sb.AppendLine($"{"Scenario",-16}{"Fixed",10}{"Adaptive",11}{"ลด%",9}");
        foreach (var r in results)
        {
            if (!r.HasBoth) continue;
            sb.AppendLine($"{r.name,-16}{r.FixedAvgWait,10:F2}{r.AdaptiveAvgWait,11:F2}{r.ImprovementPercent,8:F1}%");
        }
        sb.AppendLine($"{"รวมทุก scenario",-16}{f,10:F2}{a,11:F2}{pct,8:F1}%");
        Debug.Log(sb.ToString());
    }

    [ContextMenu("Print Estimated Runtime")]
    public void PrintEstimatedRuntime()
    {
        int runs = scenarios.Count * repetitionsPerScenario * 2;
        float simSeconds = runs * (runDuration + warmupDuration);
        float realSeconds = simSeconds / Mathf.Max(0.01f, simulationSpeed);
        Debug.Log($"[ScenarioManager] {runs} runs x {runDuration + warmupDuration:F0}s = " +
                  $"{simSeconds / 3600f:F1} ชม.จำลอง → ที่ {simulationSpeed}x ใช้เวลาจริงราว " +
                  $"{realSeconds / 3600f:F1} ชม. (ถ้าเครื่องตามทัน)");
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
        // ===== seed เดียวกันสำหรับคู่ Fixed/Adaptive → เจอ traffic ชุดเดียวกัน paired t-test จึง valid =====
        int seed = IntersectionConfig.StableHash(scenario.name) + repetition * 31;
        Random.InitState(seed);

        ApplyArrivalRates(scenario);
        config.ResetAllLanes(seed);
        dataLogger.ResetRun();

        // ===== หยุด coroutine ของทั้ง 2 controller ก่อนเสมอ กันไฟเขียวพร้อมกันแบบผิดกฎ =====
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

        // ===== Warm-up: ปล่อยรถวิ่งให้ระบบเข้าสู่สภาวะปกติก่อน แล้วค่อยล้างข้อมูลเริ่มนับจริง =====
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

        finishedRuns++;
        elapsedAtLastFinish = ElapsedRealtime;

        Debug.Log($"[ScenarioManager] ✓ Completed {dataLogger.scenarioName} [{ctype}]. " +
                  $"Vehicles={dataLogger.GetVehicleCount()}, AvgWait={dataLogger.GetAverageWaitTime():F1}s, " +
                  $"Starvation={dataLogger.GetStarvationCount()}");
    }

    // ===================== Lane helpers (generic ตาม IntersectionConfig) =====================

    void ApplyArrivalRates(ScenarioDef scenario)
    {
        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null || lane.spawner == null) continue;
            float interval = scenario.GetInterval(lane.laneName, low);
            lane.spawner.UsePoisson(interval);   // batch run ใช้ Poisson เสมอ
        }
    }

    List<bool> DisableAllGUI()
    {
        List<bool> saved = new List<bool> { dataLogger.showDebugGUI, adaptiveController.showDebugGUI, fixedController.showDebugGUI };
        dataLogger.showDebugGUI = false;
        adaptiveController.showDebugGUI = false;
        fixedController.showDebugGUI = false;

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

        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null || lane.roi == null) continue;
            if (i < saved.Count) lane.roi.showDebugGUI = saved[i++];
        }
    }

    // ===================== Preset =====================

    /// <summary>
    /// 5 scenario ครอบคลุมรูปแบบจราจรหน้า ABAC
    ///   MorningPeak  — คนเข้ามหาลัยเยอะ (Ax, Bx หนัก / ขาออก CB, CA เบา)
    ///   EveningPeak  — คนออกจากมหาลัยเยอะ (CB, CA หนัก / ขาเข้าเบา)
    ///   MiddayNormal — จราจรปกติทุกทิศ
    ///   ABACEvent    — งานรับปริญญา/กิจกรรมใหญ่ เข้าออกหนักพร้อมกัน
    ///   Night        — เบาบางทุกทิศ
    /// </summary>
    // ⚠️ ค่า MorningPeak / MiddayNormal ใช้ตัวเลขวัดจริงจาก Itim (detector ผ่านครบ 8 คลิป, ปรับแก้รอบ 24 ส.ค.)
    // ที่เหลือ (EveningPeak, ABACEvent, Night) ยังเป็นสมมติฐานจาก bucket — ยังไม่มีข้อมูลวัด
    // MorningPeak CB/CA = closed — แก้แล้ว: เลน C ปิดแค่ 08:30-09:00 (~15-30 นาที) ไม่ใช่ทั้งเช้า แต่ตรงกับ
    //   ช่วง peak จริง (08:15-09:00, จบทันทีหลัง 09:00 ไม่ใช่ 07:00-08:30 ตามที่เดาไว้แต่แรก) จึงยังใช้ closed ได้
    // MiddayNormal และ scenario อื่น ๆ CB/CA ยังเป็นสมมติฐาน (รอฟุตเทจเลน C วันจันทร์จาก Itim)
    // ⚠️ ยืนยันแล้ว: 09:00-15:00 ไฟกระพริบเหลืองจริง (ไม่ใช่ fixed-time) — MiddayNormal (11:37-12:00) อยู่ในช่วงนี้
    //   ผล Fixed vs Adaptive ของ MiddayNormal จึงเป็นการเทียบเชิงสมมติ ไม่ใช่สภาพจริงตอนเที่ยง ต้องระบุ caveat ในรายงาน
    [ContextMenu("Load ABAC Scenario Preset (5 scenarios x 7 lanes)")]
    public void LoadABACScenarioPreset()
    {
        scenarios = new List<ScenarioDef>
        {
            MakeScenario("MorningPeak",  A: 18.8f, Ax: 5.1f,   B: 9.0f,  Bx: 9.6f,     CB: closed,  CA: closed),
            MakeScenario("EveningPeak",  A: medium,  Ax: low,      B: high,   Bx: low,      CB: 8.4f,    CA: 13.4f),
            MakeScenario("MiddayNormal", A: 29.0f, Ax: 9.7f,   B: 20.5f, Bx: 21.4f,    CB: low,     CA: low),
            MakeScenario("ABACEvent",    A: medium,  Ax: veryHigh, B: medium, Bx: veryHigh, CB: high,    CA: high),
            MakeScenario("Night",        A: veryLow, Ax: veryLow,  B: veryLow, Bx: veryLow, CB: veryLow, CA: veryLow),
        };

        Debug.Log($"[ScenarioManager] โหลด preset {scenarios.Count} scenarios แล้ว → " +
                  $"{scenarios.Count * repetitionsPerScenario} scenarios รวม " +
                  $"{scenarios.Count * repetitionsPerScenario * 2} runs");
    }

    /// <summary>
    /// A ที่รับเข้ามาคือ "อัตรารถตรงรวม" (ก่อนมี shared lane) — แบ่งให้ A (เลนใน) กับ A2 (เลนนอก)
    /// เหมือนกับ TestCaseLibrary.Poisson() เป๊ะ ๆ ตามสัดส่วน a2Share (ค่าเริ่มต้น 18% ไม่ใช่ 50/50 แล้ว)
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
