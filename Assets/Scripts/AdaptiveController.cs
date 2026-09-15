using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SODA (patched) — Sampled-Observation Demand-Adaptive signal control
///
/// ===== Why this was rewritten =====
/// The previous controller relied on two things that cannot actually be deployed:
///   1) continuous per-vehicle tracking (needs ByteTrack/DeepSORT behind YOLO) - hardware we cannot afford
///   2) rest-in-green (holding green while idle) - the real signal goes all-red when idle
/// Running a batch on that would measure a controller that cannot be installed.
///
/// ===== Three things changed, nothing else =====
///   Perception : continuously tracked ROI with per-vehicle IDs -> a snapshot every 2 s, count only, no IDs
///   When idle  : rest-in-green                                 -> ALL-RED on every head, plus Eyes
///   Leader     : argmin arrival time of the first vehicle       -> argmin queueStart[h], a lane-level timestamp
///
/// Untouched: conflict matrix / concurrent grouping / A+A2 forced pairing / the G formula /
///            clearance that spares lanes still needing green / anti-starvation / max continuous green
///
/// ===== The entire state is eight numbers =====
/// Four signal heads x { queueStart, lastCount }. No vehicle IDs, no frame-to-frame matching,
/// no list that grows with the number of vehicles. That is where the tracker genuinely disappears.
/// </summary>
public class AdaptiveController : MonoBehaviour
{
    [Header("Intersection")]
    public IntersectionConfig config;

    // ===================== Eyes (sampled observation) =====================

    [Header("Eyes — Sampled Observation")]
    [Tooltip("Seconds between snapshots - counting only, no tracker required")]
    // 14 Sep 2026: 2 -> 1 s (Itim), which tracks vehicles more accurately and fires gap-out sooner.
    // Side effect: gapOutSnapshots stays at 2, so the confirmation window shrinks from 4 s to 2 s.
    //    That was Itim's decision made knowingly, not something that slipped through.
    // This value is also serialized in the scene; changing it here alone has no effect - edit SampleScene.unity too.
    public float snapshotInterval = 1f;
    [Tooltip("Ceiling on the collection window, not a fixed length - an early exit can end it sooner")]
    public float collectWindow = 7f;
    [Tooltip("Early exit (a): stop waiting once this many heads have vehicles")]
    public int earlyExitHeadCount = 2;
    [Tooltip("Early exit (b): stop waiting once any head has this many vehicles")]
    public int earlyExitDemand = 3;

    // ===================== Green time (carried over unchanged) =====================

    [Header("Timing Formula (HCM-based)")]
    public float gMin = 8f;
    public float gMax = 45f;
    public float yellowDuration = 3f;
    public float allRedDuration = 2f;
    public float startupLostTime = MEASURED_STARTUP_LOST_TIME;
    public float saturationHeadway = MEASURED_SATURATION_HEADWAY;

    // ===== Measured reference values - the single source of truth for the whole project =====
    // Unity serialises the values of a scene component into SampleScene.unity and always overrides
    //    the script defaults on load, so editing the default in the .cs alone has no effect on a
    //    component already in the scene. Three layers guard against that: (1) the consts here,
    //    (2) EnforceMeasuredParameters() in Awake, and (3) ABACSceneBuilder calling
    //    LoadMeasuredPreset() on every scene build.
    public const float MEASURED_STARTUP_LOST_TIME  = 2.95f;   // measured over 39 samples (HCM default = 2.0)
    public const float MEASURED_SATURATION_HEADWAY = 2.31f;   // measured, sd 0.81 (HCM default = 2.0)
    // The counting window (once 4 s) is gone. It asked the same question as COLLECT using the same
    //    threshold (< 3 vehicles) and then waited all over again.
    //    Full reasoning at Q2 below.

    [Tooltip("true = always enforce the measured values; a drifted scene value is corrected automatically with an error.\n" +
             "Turn off only for a deliberate sensitivity analysis, such as comparing HCM 2.0 against the measured value.")]
    public bool useMeasuredParameters = true;

    [Tooltip("Longest a single group may hold green before the phase is forced to end (standard max green)")]
    public float maxContinuousGreen = 90f;

    [Header("Concurrent Phase")]
    [Tooltip("true = combine non-conflicting lanes into one green (recommended) / false = one lane at a time")]
    public bool enableConcurrentPhases = true;
    [Tooltip("Minimum vehicles a head needs before it can join the group")]
    public int minDemandToJoinGroup = 1;

    [Header("Gap-out")]
    public bool enableGapOut = true;
    [Tooltip("How many consecutive snapshots must read count = 0 before the phase ends.\n" +
             "2 snapshots = 4 s. One is not enough: a single missed detection would end a phase with vehicles still waiting.")]
    public int gapOutSnapshots = 2;

    [Header("Anti-Starvation (longest acceptable wait)")]
    public float maxWaitTime = 45f;
    [Tooltip("Minimum green before the phase may be cut short, so it is never too short to discharge")]
    public float minGreenBeforeInterrupt = 20f;
    [Tooltip("Fraction of maxWaitTime at which a head is forced to be the next phase (the emergency exit)")]
    public float starvationSelectThreshold = 0.95f;
    [Tooltip("Fraction of maxWaitTime at which the current phase is ended early")]
    public float starvationInterruptThreshold = 0.90f;

    [Header("Debug Display")]
    public bool showDebugGUI = true;
    public bool verboseLog = false;

    [Header("Decision Inspector")]
    [Tooltip("Separate panel on the right showing which step the algorithm is on, what is queued, and which options it weighed")]
    public bool showDecisionPanel = true;
    [Tooltip("Key that toggles the decision-trace panel")]
    public KeyCode decisionPanelKey = KeyCode.D;

    // ===================== Signal head =====================

    /// <summary>
    /// One head = one physical signal lamp, not one lane.
    /// H_A controls both A (inner) and A2 (outer), because they genuinely are the same lamp,
    /// so the service queue holds at most 4 entries, not 7.
    /// </summary>
    public class SignalHead
    {
        public string name;
        /// <summary>Position in ORIGINAL_SEQUENCE - decides ties when queueStart is exactly equal</summary>
        public int priority = int.MaxValue;
        public List<int> signals = new List<int>();   // signal indices sharing one TrafficLight
        public List<ROIZone> rois = new List<ROIZone>();

        /// <summary>When this head went from empty to occupied (Infinity = empty)</summary>
        public float queueStart = Mathf.Infinity;
        /// <summary>Count from the previous snapshot - used only to detect the rising edge</summary>
        public int lastCount = 0;
    }

    /// <summary>
    /// The junction's original phase order, used to break ties when queueStart is equal.
    ///
    /// Why it is needed: every head is updated in the same Snapshot() call, so two heads whose vehicles
    /// arrive in the same 2 s window get bit-identical queueStart values. Ties are routine, not rare.
    /// They used to be settled by the incidental order the variables happened to be declared in, which
    /// was written down nowhere, and List&lt;T&gt;.Sort is not guaranteed stable, so the result was undefined.
    ///
    /// This tie-break decides more than who leads - it decides who is in the group.
    ///    With leader = B and A tied with Bx: A first gives {B,A,A2}, Bx first gives {B,Bx}.
    ///    And because A conflicts with Bx, favouring A systematically excludes Bx.
    ///    TieBreaksApplied / TieBreaksExcludingBx count this so the size of the bias can be measured.
    /// </summary>
    public static readonly string[] ORIGINAL_SEQUENCE = { "H_A", "H_B", "H_Bx", "H_CB" };

    public enum EyeState { AllRedIdle, Collect, Serve }

    // ===================== runtime =====================

    private List<SignalHead> heads = new List<SignalHead>();
    private List<int> activeGroup = new List<int>();
    private EyeState state = EyeState.AllRedIdle;
    private float phaseTimeRemaining = 0f;
    /// <summary>Seconds left in the current green - only set inside GreenPhaseWithInterrupts</summary>
    private float greenTimeRemaining = 0f;
    private float continuousGreenTimer = 0f;
    private bool forcePhaseChange = false;
    private string statusText = "Idle";

    private int groupZeroStreak = 0;          // consecutive snapshots with the current group empty (gap-out)

    /// <summary>
    /// Increments by one on every snapshot - the single clock for the entire algorithm.
    /// Every decision waits on this index to move; no loop runs a competing clock of its own.
    /// (COLLECT used to check every 1 s while the data updated every 2 s, so half its checks reread
    ///  stale numbers and it added a random 0-1 s lag between data arriving and being seen, for nothing.)
    /// </summary>
    private int snapshotIndex = 0;
    public int SnapshotIndex => snapshotIndex;

    // ===== Measuring idle green per phase (no behaviour change yet, just evidence) =====
    // Gap-out currently fires only when every lane in the group is empty, so a lane that empties first
    // sits on green waiting for the others. Resolution equals snapshotInterval (1 s) - what a real detector sees.
    private Dictionary<int, float> laneEmptyAt = new Dictionary<int, float>();
    private float phaseStartTime = 0f;
    private string lastEndReason = "completed";
    private Coroutine snapshotLoop = null;

    // ===== Statistics for the log =====
    // ===== Decision trace (for the Decision Inspector panel) =====
    /// <summary>One decision step - records both the options weighed and the verdict</summary>
    public class TraceStep
    {
        public string id;                                  // Q1..Q4
        public string title;
        public List<string> options = new List<string>();  // the options considered
        public string verdict = "";                        // one-line outcome
        public bool resolved = false;
    }

    private TraceStep[] trace = null;
    private List<string> queueView = new List<string>();
    private string activeStepId = "";
    private int phaseCounter = 0;

    private int collectCount = 0;
    private float collectTotalDuration = 0f;
    private int skippedHeadTotal = 0;
    private int queueStartResets = 0;
    private int tieBreaksApplied = 0;
    private int tieBreaksExcludingBx = 0;

    public float SnapshotInterval   => snapshotInterval;
    public int   CollectCount       => collectCount;
    public float CollectTotalTime   => collectTotalDuration;
    public int   SkippedHeadTotal   => skippedHeadTotal;
    public int   QueueStartResets   => queueStartResets;
    /// <summary>Phases where two or more heads had exactly equal queueStart (the tie-break ran)</summary>
    public int   TieBreaksApplied     => tieBreaksApplied;
    /// <summary>Of those, how many excluded H_Bx from the group in favour of a head tied with it</summary>
    public int   TieBreaksExcludingBx => tieBreaksExcludingBx;
    public EyeState CurrentState    => state;
    /// <summary>
    /// Seconds left in this phase's green - for display only.
    /// Never use phaseTimeRemaining for this: CountdownPhase overwrites it with the yellow and
    ///    all-red times, so a head green across phases would show another head's clearance as its own green.
    /// It is 0 throughout the decision and clearance stages, because the new phase's G is not yet computed.
    /// </summary>
    public float GreenTimeRemaining => greenTimeRemaining;
    /// <summary>All signal heads - exposed so tests can inspect internal state</summary>
    public List<SignalHead> Heads   => heads;
    /// <summary>Vehicle count for one head (public so tests can call it)</summary>
    public int CountOfHead(SignalHead h) => CountOf(h);

    // ===================== Lifecycle =====================

    void Awake()
    {
        EnforceMeasuredParameters();

        if (config == null) config = FindFirstObjectByType<IntersectionConfig>();
        if (config == null)
        {
            Debug.LogError("[AdaptiveController] no IntersectionConfig found in the scene");
            return;
        }
        config.EnsureBuilt();
        BuildHeads();
    }

    /// <summary>
    /// Groups signal indices that share the same TrafficLight object into one head.
    /// (A and A2 deliberately point at the same component, so they merge automatically with no hard-coding.)
    /// </summary>
    /// <summary>public so tests can build heads without relying on Unity calling Awake</summary>
    public void BuildHeads()
    {
        heads.Clear();
        for (int s = 0; s < config.SignalCount; s++)
        {
            TrafficLight light = config.SignalLight(s);
            if (light == null) continue;

            SignalHead found = null;
            foreach (SignalHead h in heads)
            {
                if (h.signals.Count > 0 && config.SignalLight(h.signals[0]) == light) { found = h; break; }
            }
            if (found == null)
            {
                found = new SignalHead { name = "H_" + config.SignalName(s) };
                heads.Add(found);
            }
            found.signals.Add(s);

            ROIZone roi = config.SignalRoi(s);
            if (roi != null && !found.rois.Contains(roi)) found.rois.Add(roi);
        }

        // Original order, stated explicitly rather than falling out of iteration order
        foreach (SignalHead h in heads)
        {
            h.priority = System.Array.IndexOf(ORIGINAL_SEQUENCE, h.name);
            if (h.priority < 0)
            {
                h.priority = ORIGINAL_SEQUENCE.Length + heads.IndexOf(h);
                Debug.LogWarning($"[SODA] {h.name} is not in ORIGINAL_SEQUENCE - tie-breaking will be undefined");
            }
        }

        if (verboseLog)
        {
            foreach (SignalHead h in heads)
            {
                string lanes = "";
                foreach (int s in h.signals) lanes += config.SignalName(s) + " ";
                Debug.Log($"[SODA] {h.name} (priority {h.priority}) controls lanes: {lanes}({h.rois.Count} ROI)");
            }
        }
    }

    public void ForceStop()
    {
        StopAllCoroutines();
        snapshotLoop = null;
        activeGroup.Clear();
        continuousGreenTimer = 0f;
        greenTimeRemaining = 0f;

        // 11 Sep 2026: clear the SODA counters here too.
        //    ScenarioManager calls ForceStop() before each Fixed run, but this was not cleared,
        //    so the Fixed rows in summary_all_runs.csv carried the previous Adaptive run's values
        //    wholesale (checked against the 216-run batch: 107 of 108 rows matched exactly).
        collectCount = 0; collectTotalDuration = 0f;
        skippedHeadTotal = 0; queueStartResets = 0;
        tieBreaksApplied = 0; tieBreaksExcludingBx = 0;
        phaseCounter = 0;
        SetAllRed();
        state = EyeState.AllRedIdle;
        statusText = "Stopped";
    }

    public void RestartCycle()
    {
        StopAllCoroutines();
        activeGroup.Clear();
        continuousGreenTimer = 0f;
        collectCount = 0; collectTotalDuration = 0f;
        skippedHeadTotal = 0; queueStartResets = 0;
        tieBreaksApplied = 0; tieBreaksExcludingBx = 0;
        foreach (SignalHead h in heads) { h.queueStart = Mathf.Infinity; h.lastCount = 0; }
        SetAllRed();
        snapshotLoop = StartCoroutine(SnapshotLoop());
        StartCoroutine(RunSoda());
    }

    void Start()
    {
        if (config == null) return;
        SetAllRed();
        snapshotLoop = StartCoroutine(SnapshotLoop());
        StartCoroutine(RunSoda());
    }

    // ===================== Measured-parameter guard =====================

    [ContextMenu("Load Measured Parameters (2.95 / 2.31)")]
    public void LoadMeasuredPreset()
    {
        startupLostTime   = MEASURED_STARTUP_LOST_TIME;
        saturationHeadway = MEASURED_SATURATION_HEADWAY;
    }

    /// <summary>
    /// Checks the values serialized in the scene against the measured ones, correcting them and
    /// raising a loud error if they differ, so a 216-run batch cannot quietly finish on textbook values.
    /// </summary>
    public bool EnforceMeasuredParameters()
    {
        if (!useMeasuredParameters) return false;

        bool drifted = Mathf.Abs(startupLostTime   - MEASURED_STARTUP_LOST_TIME)  > 0.001f
                    || Mathf.Abs(saturationHeadway - MEASURED_SATURATION_HEADWAY) > 0.001f;
        if (!drifted) return false;

        float oldL = startupLostTime, oldH = saturationHeadway;
        startupLostTime   = MEASURED_STARTUP_LOST_TIME;
        saturationHeadway = MEASURED_SATURATION_HEADWAY;

        Debug.LogError(
            $"[AdaptiveController] the values stored in the scene do not match the measured ones - corrected automatically\n" +
            $"  startupLostTime   {oldL} -> {MEASURED_STARTUP_LOST_TIME}\n" +
            $"  saturationHeadway {oldH} -> {MEASURED_SATURATION_HEADWAY}\n" +
            $"  This correction is in memory only. Open the AdaptiveController Inspector, press " +
            $"Load Measured Parameters, then Save Scene (Ctrl+S), or the next run drifts again.");
        return true;
    }

    // ===================== Decision trace =====================

    /// <summary>
    /// Records the options the algorithm can see before it decides: the queue, the Q1 threshold,
    /// and the Q2 candidates. Read-only and changes no state, so a test can call what ServeOnePhase calls.
    /// </summary>
    public void TraceDecisionInputs()
    {
        TraceBegin();
        TraceQueue();

        float starveLimit = maxWaitTime * starvationSelectThreshold;
        TraceOption("Q1", $"threshold|{starveLimit:F1}s|= {starvationSelectThreshold:F2} x {maxWaitTime:F0}");
        foreach (SignalHead h in heads)
        {
            if (h.lastCount == 0) continue;
            float w = WaitOf(h);
            TraceOption("Q1", $"{h.name}|{w:F1}s|{(w >= starveLimit ? "OVER" : "ok")}");
        }

        foreach (SignalHead h in heads)
        {
            if (h.lastCount == 0) { TraceOption("Q2", $"{h.name}|—|empty, not eligible"); continue; }
            TraceOption("Q2", $"{h.name}|queued {WaitOf(h):F1}s|{h.lastCount} cars");
        }
    }

    /// <summary>Decision steps of the most recent phase - readable by the Inspector panel and by tests</summary>
    public TraceStep[] DecisionTrace { get { EnsureTrace(); return trace; } }
    /// <summary>The queue as the algorithm sees it, ordered by queueStart</summary>
    public List<string> QueueView => queueView;


    void EnsureTrace()
    {
        if (trace != null) return;
        trace = new TraceStep[]
        {
            new TraceStep { id = "Q1", title = "STARVATION CHECK" },
            new TraceStep { id = "Q2", title = "LEADER  (argmin queueStart)" },
            new TraceStep { id = "Q3", title = "CONCURRENT GROUP" },
            new TraceStep { id = "Q4", title = "GREEN TIME" },
        };
    }

    TraceStep Step(string id)
    {
        EnsureTrace();
        foreach (TraceStep t in trace) if (t.id == id) return t;
        return trace[0];
    }

    void TraceBegin()
    {
        // 11 Sep 2026: phaseCounter must advance before the early return.
        //    showDecisionPanel is off for every batch run, so the phase number stayed at 0 for the
        //    whole file (which is why PhaseIndex is 0 throughout the 216-run batch phases_*.csv).
        phaseCounter++;
        if (!showDecisionPanel) return;
        EnsureTrace();
        foreach (TraceStep t in trace) { t.options.Clear(); t.verdict = ""; t.resolved = false; }
        queueView.Clear();
        activeStepId = "";
    }

    void TraceEnter(string id)
    {
        if (!showDecisionPanel) return;
        activeStepId = id;
    }

    void TraceOption(string id, string line)
    {
        if (!showDecisionPanel) return;
        TraceStep t = Step(id);
        if (t.options.Count < 8) t.options.Add(line);
    }

    void TraceVerdict(string id, string verdict)
    {
        if (!showDecisionPanel) return;
        TraceStep t = Step(id);
        t.verdict = verdict;
        t.resolved = true;
    }

    /// <summary>Snapshot of the current queue, oldest queueStart first = the order the algorithm sees</summary>
    void TraceQueue()
    {
        if (!showDecisionPanel) return;
        queueView.Clear();

        List<SignalHead> sorted = new List<SignalHead>(heads);
        sorted.Sort((a, b) => a.queueStart.CompareTo(b.queueStart));

        int rank = 0;
        foreach (SignalHead h in sorted)
        {
            if (h.lastCount > 0)
            {
                rank++;
                queueView.Add($"{rank}. {h.name}|{h.lastCount} cars|waited {WaitOf(h):F1}s");
            }
            else queueView.Add($"–  {h.name}|0 cars|SKIPPED this round");
        }
    }

    // ===================== Eyes: the snapshot =====================

    /// <summary>
    /// Vehicle count for one head = the sum over the ROIs it controls.
    /// Only GetSampledCount() may be used - the one API a camera without a tracker can really provide.
    ///    GetOldestArrivalTime / GetLongestWaitTime / GetCarsInZone must never be called anywhere in this file.
    /// </summary>
    int CountOf(SignalHead h)
    {
        int n = 0;
        foreach (ROIZone roi in h.rois) if (roi != null) n += roi.GetSampledCount();
        return n;
    }

    IEnumerator SnapshotLoop()
    {
        while (true)
        {
            Snapshot();
            yield return new WaitForSeconds(snapshotInterval);
        }
    }

    /// <summary>
    /// One snapshot: update queueStart and lastCount on every head, and advance the gap-out streak.
    /// This is everything the controller sees - four numbers every 2 seconds.
    /// </summary>
    public void Snapshot()
    {
        snapshotIndex++;

        foreach (SignalHead h in heads)
        {
            int c = CountOf(h);

            // Rising edge: empty -> occupied means this lane starts its wait clock
            if (h.lastCount == 0 && c > 0) h.queueStart = Time.time;
            // Clear it the moment the lane empties
            if (c == 0) h.queueStart = Mathf.Infinity;

            h.lastCount = c;
        }

        // ---- Record when each lane in the group emptied (first time only) ----
        if (activeGroup.Count > 0)
        {
            foreach (int sig in activeGroup)
            {
                if (laneEmptyAt.ContainsKey(sig)) continue;
                if (DemandOf(sig) == 0) laneEmptyAt[sig] = Time.time - phaseStartTime;
            }
        }

        // ---- Gap-out has to be confirmed over 2 consecutive snapshots ----
        if (activeGroup.Count > 0)
        {
            if (GroupIsEmpty(activeGroup)) groupZeroStreak++;
            else groupZeroStreak = 0;
        }
        else groupZeroStreak = 0;
    }

    float WaitOf(SignalHead h)
    {
        if (float.IsInfinity(h.queueStart)) return 0f;
        return Time.time - h.queueStart;
    }

    // ===================== Main loop =====================

    /// <summary>Waits for the next snapshot - every decision hangs off this one beat</summary>
    IEnumerator WaitForNextSnapshot()
    {
        int seen = snapshotIndex;
        while (snapshotIndex == seen) yield return null;
    }

    /// <summary>Waits for the next snapshot, but no later than a deadline (used for the COLLECT ceiling)</summary>
    IEnumerator WaitForNextSnapshotOrDeadline(float deadline)
    {
        int seen = snapshotIndex;
        while (snapshotIndex == seen && Time.time < deadline) yield return null;
    }

    IEnumerator RunSoda()
    {
        while (true)
        {
            yield return null;   // keeps the loop from spinning within a single frame

            // ---------- ALL-RED IDLE ----------
            // The real signal goes all-red when idle; there is no rest-in-green
            state = EyeState.AllRedIdle;
            statusText = "ALL-RED — waiting for demand";
            if (activeGroup.Count > 0)
            {
                yield return StartCoroutine(CloseGroup(activeGroup));
            }

            // Evaluate exactly once per snapshot: no faster and no slower than the data
            while (!AnyDemand()) yield return StartCoroutine(WaitForNextSnapshot());

            // ---------- COLLECT ----------
            yield return StartCoroutine(CollectPhase());

            // ---------- SERVE (loops until the junction is empty) ----------
            state = EyeState.Serve;
            while (AnyDemand())
            {
                yield return StartCoroutine(ServeOnePhase());
                if (!AnyDemand()) break;
            }
        }
    }

    /// <summary>
    /// Collection window - 7 s is a ceiling, not a fixed length.
    /// It exits early if (a) vehicles appear at 2 or more heads, or (b) any head reaches 3 vehicles.
    /// Always waiting the full window would be worse than the old controller with rest-in-green.
    /// </summary>
    IEnumerator CollectPhase()
    {
        state = EyeState.Collect;
        float t0 = Time.time;
        float deadline = t0 + collectWindow;

        // Early-exit conditions are evaluated only after a snapshot, because they are decisions from data.
        // The 7 s ceiling is a pure timeout that looks at no data, so it can cut in at exactly 7 s.
        while (Time.time < deadline)
        {
            int withCars = 0, best = 0;
            foreach (SignalHead h in heads)
            {
                if (h.lastCount > 0) withCars++;
                if (h.lastCount > best) best = h.lastCount;
            }
            if (withCars >= earlyExitHeadCount || best >= earlyExitDemand) break;

            statusText = $"COLLECT {(deadline - Time.time):F1}s";
            yield return StartCoroutine(WaitForNextSnapshotOrDeadline(deadline));
        }

        collectCount++;
        collectTotalDuration += Time.time - t0;
    }

    IEnumerator ServeOnePhase()
    {
        // Take a snapshot at the moment of decision, so the numbers used are never up to 2 s stale.
        // Still counting only; it just takes the reading when needed instead of waiting for the next tick.
        // (Equivalent to D_green in the SODA specification, which recounts before every green.)
        Snapshot();
        greenTimeRemaining = 0f;   // this phase's G is not known yet, and clearance comes first

        // ===== End of the *previous* phase - this must always come before Q1/Q2/Q3 =====
        // Section 9 of SODA_MASTER and end_phase() in soda.py both put the reset before decide().
        // At this point activeGroup is still the group that just finished (nextGroup overwrites it later).
        //
        // Never move this below the decision - it was there until 14 Sep 2026 - because queueStart
        //    keeps running while a head is green, so a head that had just been served entered Q1/Q2
        //    with a clock that included its own service time. The results were:
        //      - it could win Q2 again the very next phase, having just been served
        //      - with a green longer than 42.75 s (D >= 18) it met the Q1 threshold and triggered a
        //        starvation override for *itself*, while the genuinely starved head lost the tie-break.
        //    Measured on batch v2: 30 of 11,670 phases had GreenGiven >= 42.75 (all ABACEvent).
        ResetQueueStartFor(activeGroup);

        TraceDecisionInputs();

        // ===== Q1: is anyone close to starving? =====
        TraceEnter("Q1");
        SignalHead leader = FindStarvingHead(starvationSelectThreshold, null);
        if (leader != null)
        {
            TraceVerdict("Q1", $"OVERRIDE -> {leader.name} (waited {WaitOf(leader):F1}s)");
            TraceVerdict("Q2", "skipped — starvation takes priority");
            statusText = $"STARVATION OVERRIDE — {leader.name}";
            Debug.Log($"[SODA] STARVATION: {leader.name} waited {WaitOf(leader):F1}s, forcing green.");
            if (DataLogger.Instance != null) DataLogger.Instance.LogStarvationEvent(leader.name);
        }
        else
        {
            TraceVerdict("Q1", "none starving — fall through to FCFS");

            // ===== Q2: who has waited longest? =====
            TraceEnter("Q2");
            leader = ChooseLeaderByQueueStart(null);
            if (leader == null) { TraceVerdict("Q2", "no head has demand"); yield break; }

            // The counting window is gone - COLLECT is now the only demand accumulator in the system.
            //    Why it was removed:
            //    1) It asked the same question with the same threshold: earlyExitDemand and
            //       demandThresholdSkipWait were both 3, so a lane with fewer than 3 made COLLECT wait
            //       the full 7 s ceiling and *then* wait another 4 s for the same reason - punished
            //       twice (S1: 11 s of waiting for one vehicle).
            //    2) At peak it quietly extended the previous group's green by 4 s, because that group
            //       was still green while it waited, so phases ran longer than the logged G.
            TraceVerdict("Q2", $"LEADER -> {leader.name} (oldest queue, {WaitOf(leader):F1}s)");
        }
        if (forcePhaseChange)
        {
            SignalHead other = ChooseLeaderByQueueStart(activeGroup);
            if (other != null) leader = other;
            forcePhaseChange = false;
        }

        // ===== Q3: who else can go at the same time? =====
        TraceEnter("Q3");
        List<int> nextGroup = BuildCompatibleGroup(leader.signals[0]);
        TraceVerdict("Q3", $"GROUP -> {{{config.DescribeGroup(nextGroup)}}}");
        CountTieBreaks(nextGroup);

        // Heads with count = 0 are skipped for the whole round - the single largest source of the win over Fixed
        foreach (SignalHead h in heads)
            if (h.lastCount == 0 && !ContainsAny(nextGroup, h.signals)) skippedHeadTotal++;

        // ===== Clearance: only drop the lanes that must turn red =====
        // Captured before activeGroup is overwritten - used to reset the clock of heads that just had green
        List<int> dropping = Except(activeGroup, nextGroup);
        if (dropping.Count > 0)
        {
            statusText = $"Yellow — {config.DescribeGroup(dropping)}";
            SetGroupState(dropping, TrafficLight.LightState.Yellow);
            yield return StartCoroutine(CountdownPhase(yellowDuration));

            statusText = "All-Red clearance";
            SetGroupState(dropping, TrafficLight.LightState.Red);
            activeGroup = Except(activeGroup, dropping);
            // The clock reset for heads that just had green already happened at the top of
            // ServeOnePhase and covers both dropping and continuing heads at once. Do not add it
            // back here: that would add the clearance time to the timestamp and diverge from soda.py,
            yield return StartCoroutine(CountdownPhase(allRedDuration));
        }

        // ===== Turn green =====
        List<int> turningOn = Except(nextGroup, activeGroup);
        if (turningOn.Count > 0 || dropping.Count > 0 || continuousGreenTimer >= maxContinuousGreen)
            continuousGreenTimer = 0f;
        SetGroupState(turningOn, TrafficLight.LightState.Green);
        activeGroup = new List<int>(nextGroup);

        // 11 Sep 2026: heads that *continue* in the group must have their clock reset too, not only
        //    the ones dropped. Previously only dropping heads were reset, so a head that never left
        //    the group held a stale queueStart indefinitely and won both Q1 (FindStarvingHead) and
        //    Q2 (ChooseLeaderByQueueStart) forever - the genuinely starved head never got a turn.
        //    Evidence: phases_Adaptive_EveningPeak_Rep17 t=1104.6 to 1470.2 (365.5 s), H_B in the
        //    group for 24 consecutive phases, H_CB never served, CB vehicles waiting up to 344 s
        //    (maxWaitTime = 45). The correct meaning of queueStart is "how long since this head last
        //    had green". [test QueueClockTest] - the reset now lives at the top of ServeOnePhase.

        groupZeroStreak = 0;
        laneEmptyAt.Clear();
        phaseStartTime = Time.time;
        lastEndReason = "completed";

        // ===== Q4: how long a green? (recounted fresh as the phase begins) =====
        TraceEnter("Q4");
        float rawMax = gMin;
        string critical = "(floor)";
        foreach (int sig in activeGroup)
        {
            int d = DemandOf(sig);
            float need = startupLostTime + d * saturationHeadway;
            TraceOption("Q4", $"{config.SignalName(sig)}|{d} cars|{startupLostTime:F2}+{d}x{saturationHeadway:F2} = {need:F1}s");
            if (d > 0 && need > rawMax) { rawMax = need; critical = config.SignalName(sig); }
        }
        float greenTime = CalculateGreenDuration(activeGroup);
        string clampNote = greenTime >= gMax - 0.01f ? $" (clamped at gMax {gMax:F0})"
                         : greenTime <= gMin + 0.01f ? $" (clamped at gMin {gMin:F0})" : "";
        TraceVerdict("Q4", $"G = {greenTime:F1}s{clampNote} — critical lane {critical}");
        TraceEnter("");

        if (verboseLog || turningOn.Count > 0)
        {
            Debug.Log($"[SODA] Phase {config.DescribeGroup(activeGroup)} selected. " +
                      $"Demand={DescribeDemand(activeGroup)}, GreenTime={greenTime:F1}s");
        }

        statusText = $"Green — {config.DescribeGroup(activeGroup)}";
        yield return StartCoroutine(GreenPhaseWithInterrupts(activeGroup, greenTime));
        greenTimeRemaining = 0f;   // covers every exit from GreenPhaseWithInterrupts, gap-out and early cuts included
        LogPhaseTrace(greenTime);
    }

    /// <summary>
    /// Counts how often the tie-break actually ran, and how often it excluded H_Bx.
    ///
    /// Pure measurement that touches no decision - called once the group has been built.
    /// Why Bx is counted separately: A conflicts with Bx, so a tie-break favouring A (higher priority)
    /// systematically excludes Bx, and Bx is the lane at v/c = 1.51 that this project is about.
    /// The bias is conservative in direction (it makes adaptive look worse for Bx than it is),
    /// but the size has to be known, not only the direction.
    /// </summary>
    void CountTieBreaks(List<int> group)
    {
        // Are there two or more heads with vehicles and exactly equal queueStart?
        bool tied = false;
        for (int i = 0; i < heads.Count && !tied; i++)
        {
            if (heads[i].lastCount == 0) continue;
            for (int j = i + 1; j < heads.Count; j++)
            {
                if (heads[j].lastCount == 0) continue;
                if (heads[i].queueStart == heads[j].queueStart) { tied = true; break; }
            }
        }
        if (!tied) return;
        tieBreaksApplied++;

        // Bx has vehicles but did not join, even though a group member shares its queueStart
        SignalHead bx = null;
        foreach (SignalHead h in heads) if (h.name == "H_Bx") { bx = h; break; }
        if (bx == null || bx.lastCount == 0) return;
        if (ContainsAny(group, bx.signals)) return;

        foreach (SignalHead h in heads)
        {
            if (h == bx || h.lastCount == 0) continue;
            if (!ContainsAny(group, h.signals)) continue;
            if (h.queueStart == bx.queueStart)
            {
                tieBreaksExcludingBx++;
                if (verboseLog)
                    Debug.Log($"[SODA] tie-break excluded H_Bx in favour of {h.name} " +
                              $"(equal queueStart at {bx.queueStart:F1})");
                return;
            }
        }
    }

    /// <summary>Turns the currently green group red (used when returning to ALL-RED IDLE)</summary>
    IEnumerator CloseGroup(List<int> group)
    {
        List<int> closing = new List<int>(group);
        SetGroupState(closing, TrafficLight.LightState.Yellow);
        yield return StartCoroutine(CountdownPhase(yellowDuration));
        SetGroupState(closing, TrafficLight.LightState.Red);
        activeGroup.Clear();
        ResetQueueStartFor(closing);
        yield return StartCoroutine(CountdownPhase(allRedDuration));
    }

    /// <summary>
    /// The heart of the algorithm: when a green ends with vehicles still waiting, the wait clock restarts.
    ///
    /// Without this line a lane that never empties (Bx at v/c = 1.51) would hold a stale queueStart
    /// forever, win argmin every time, and no other lane would ever get a green.
    /// The bug does not show in light-traffic tests, because every lane empties periodically and
    /// queueStart resets itself. It breaks immediately at MorningPeak, hence a dedicated QueueStartTest.
    /// </summary>
    public void ResetQueueStartFor(List<int> signals)
    {
        foreach (SignalHead h in heads)
        {
            if (!ContainsAny(signals, h.signals)) continue;
            if (CountOf(h) > 0)
            {
                h.queueStart = Time.time;
                queueStartResets++;
            }
            else h.queueStart = Mathf.Infinity;
        }
    }

    // ===================== Green phase =====================

    IEnumerator GreenPhaseWithInterrupts(List<int> group, float maxGreenTime)
    {
        float elapsed = 0f;

        while (elapsed < maxGreenTime)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            continuousGreenTimer += dt;
            phaseTimeRemaining = maxGreenTime - elapsed;
            greenTimeRemaining = maxGreenTime - elapsed;

            // ---- Max green ----
            if (continuousGreenTimer >= maxContinuousGreen && AnyDemandOutside(group))
            {
                Debug.Log($"[SODA] Max green {maxContinuousGreen}s reached on " +
                          $"{config.DescribeGroup(group)} — forcing phase change");
                forcePhaseChange = true;
                lastEndReason = "max-green";
                yield break;
            }

            // ---- Gap-out: needs gapOutSnapshots consecutive empty readings ----
            if (enableGapOut && groupZeroStreak >= gapOutSnapshots)
            {
                if (verboseLog)
                    Debug.Log($"[SODA] {config.DescribeGroup(group)} gap-out at {elapsed:F1}s " +
                              $"({groupZeroStreak} snapshots empty)");
                lastEndReason = "gap-out";
                yield break;
            }

            // ---- Anti-starvation ----
            if (elapsed >= minGreenBeforeInterrupt)
            {
                SignalHead starving = FindStarvingHead(starvationInterruptThreshold, group);
                if (starving != null)
                {
                    Debug.Log($"[SODA] Cutting {config.DescribeGroup(group)} short at {elapsed:F1}s " +
                              $"to prevent {starving.name} starvation (waited {WaitOf(starving):F1}s)");
                    if (DataLogger.Instance != null)
                        DataLogger.Instance.LogStarvationEvent(starving.name);
                    lastEndReason = "starvation";
                    yield break;
                }
            }

            yield return null;
        }
    }

    /// <summary>
    /// Summarises the phase that just ended into the log, in particular when each lane emptied.
    ///
    /// idleLaneSeconds = sum over the group of (phase end time - the time that lane emptied)
    /// = time a lane held green with nothing left to discharge, which a per-lane gap-out could recover.
    /// No behaviour change yet - the figures are collected to decide after the batch has run.
    /// </summary>
    void LogPhaseTrace(float greenGiven)
    {
        float used = Time.time - phaseStartTime;

        string parts = "";
        float idle = 0f;
        foreach (int sig in activeGroup)
        {
            string name = config.SignalName(sig);
            if (laneEmptyAt.ContainsKey(sig))
            {
                float t = laneEmptyAt[sig];
                parts += $"{name}={t:F1};";
                float wasted = used - t;
                if (wasted > 0f) idle += wasted;
            }
            else parts += $"{name}=nan;";
        }
        if (parts.EndsWith(";")) parts = parts.Substring(0, parts.Length - 1);

        if (DataLogger.Instance != null)
        {
            DataLogger.Instance.LogPhase(phaseCounter, phaseStartTime,
                                         config.DescribeGroup(activeGroup).Replace(",", "+"),
                                         greenGiven, used, lastEndReason, parts, idle);
        }
    }

    IEnumerator CountdownPhase(float duration)
    {
        float t = duration;
        while (t > 0f)
        {
            phaseTimeRemaining = t;
            t -= Time.deltaTime;
            yield return null;
        }
        phaseTimeRemaining = 0f;
    }

    // ===================== Decision helpers =====================

    /// <summary>The head past the threshold that has waited longest - heads currently green are excluded</summary>
    public SignalHead FindStarvingHead(float thresholdRatio, List<int> excludeSignals)
    {
        float threshold = maxWaitTime * thresholdRatio;
        SignalHead worst = null;

        foreach (SignalHead h in heads)
        {
            if (excludeSignals != null && ContainsAny(excludeSignals, h.signals)) continue;
            if (h.lastCount == 0) continue;
            if (WaitOf(h) < threshold) continue;

            // Longest wait = oldest queueStart; ties fall back to the original phase order A->B->Bx->CB.
            // Uses the same comparator as Q2/Q3 (SODA_BASELINE section 8.3 - it used >= before, which
            // let the last head win a tie, the opposite of Option A and inconsistent with soda.py).
            if (worst == null || CompareHeads(h, worst) < 0) worst = h;
        }
        return worst;
    }

    /// <summary>
    /// Picks the head whose queue started forming earliest - fairness decides who gets the green.
    /// (Vehicle count decides only how long. Ordering by count as well would make H_A win every time
    ///  and H_Bx, at v/c = 1.51, would never get a green at all.)
    /// </summary>
    public SignalHead ChooseLeaderByQueueStart(List<int> bannedSignals)
    {
        SignalHead best = null;

        foreach (SignalHead h in heads)
        {
            if (h.lastCount == 0) continue;
            if (bannedSignals != null && ContainsAny(bannedSignals, h.signals)) continue;
            if (best == null || CompareHeads(h, best) < 0) best = h;
        }
        return best;
    }

    /// <summary>
    /// Total order: older queueStart first, and exactly equal values fall back to the original phase order.
    /// Being a total order, it always gives the same result without depending on the sort being stable.
    /// </summary>
    public static int CompareHeads(SignalHead a, SignalHead b)
    {
        int c = a.queueStart.CompareTo(b.queueStart);
        if (c != 0) return c;
        return a.priority.CompareTo(b.priority);
    }

    bool AnyDemand()
    {
        foreach (SignalHead h in heads) if (h.lastCount > 0) return true;
        return false;
    }

    bool AnyDemandOutside(List<int> group)
    {
        foreach (SignalHead h in heads)
        {
            if (ContainsAny(group, h.signals)) continue;
            if (h.lastCount > 0) return true;
        }
        return false;
    }

    bool GroupIsEmpty(List<int> group)
    {
        foreach (int s in group)
        {
            ROIZone roi = config.SignalRoi(s);
            if (roi != null && roi.GetSampledCount() > 0) return false;
        }
        return true;
    }

    int DemandOf(int signalIndex)
    {
        ROIZone roi = config.SignalRoi(signalIndex);
        return roi != null ? roi.GetSampledCount() : 0;
    }

    static bool ContainsAny(List<int> haystack, List<int> needles)
    {
        foreach (int n in needles) if (haystack.Contains(n)) return true;
        return false;
    }

    // ===================== Group building (carried over unchanged) =====================

    List<int> BuildCompatibleGroup(int leader)
    {
        List<int> group = new List<int> { leader };
        TraceOption("Q3", $"leader|{config.SignalName(leader)}|seeds the group");
        if (!enableConcurrentPhases) { AddPhysicallyLinkedSignals(group); return group; }

        // Order the remaining heads by oldest queueStart first, not by vehicle count
        List<SignalHead> candidates = new List<SignalHead>();
        foreach (SignalHead h in heads)
        {
            if (h.signals.Contains(leader)) continue;
            if (h.lastCount < minDemandToJoinGroup) continue;
            candidates.Add(h);
        }
        candidates.Sort(CompareHeads);

        foreach (SignalHead h in candidates)
        {
            bool joined = false;
            foreach (int s in h.signals)
                if (config.IsCompatibleWithGroup(s, group)) { group.Add(s); joined = true; }

            if (joined) TraceOption("Q3", $"+ {h.name}|{h.lastCount} cars|no conflict — joins");
            else        TraceOption("Q3", $"x {h.name}|{h.lastCount} cars|conflicts with group");
        }

        int before = group.Count;
        AddPhysicallyLinkedSignals(group);
        if (group.Count > before)
            TraceOption("Q3", $"+ A2|forced|same physical signal head as A");
        return group;
    }

    /// <summary>Adds lanes sharing a TrafficLight component with an existing group member (A2 follows A)</summary>
    void AddPhysicallyLinkedSignals(List<int> group)
    {
        bool added = true;
        while (added)
        {
            added = false;
            for (int s = 0; s < config.SignalCount; s++)
            {
                if (group.Contains(s)) continue;
                TrafficLight light = config.SignalLight(s);
                if (light == null) continue;

                foreach (int member in group)
                {
                    if (config.SignalLight(member) == light) { group.Add(s); added = true; break; }
                }
            }
        }
    }

    /// <summary>
    /// HCM formula: G = startupLostTime + D x saturationHeadway.
    /// A phase opening several lanes is sized by the longest queue, since all of them discharge in parallel.
    /// </summary>
    public float CalculateGreenDuration(List<int> group)
    {
        float maxNeeded = gMin;
        foreach (int s in group)
        {
            float d = DemandOf(s);
            if (d <= 0f) continue;
            float needed = startupLostTime + d * saturationHeadway;
            if (needed > maxNeeded) maxNeeded = needed;
        }
        return Mathf.Clamp(maxNeeded, gMin, gMax);
    }

    string DescribeDemand(List<int> group)
    {
        string[] parts = new string[group.Count];
        for (int i = 0; i < group.Count; i++)
            parts[i] = $"{config.SignalName(group[i])}={DemandOf(group[i])}";
        return string.Join(", ", parts);
    }

    static List<int> Except(List<int> source, List<int> remove)
    {
        List<int> result = new List<int>();
        foreach (int v in source) if (!remove.Contains(v)) result.Add(v);
        return result;
    }

    void SetAllRed()
    {
        if (config == null) return;
        for (int s = 0; s < config.SignalCount; s++)
        {
            TrafficLight light = config.SignalLight(s);
            if (light != null) light.SetState(TrafficLight.LightState.Red);
        }
    }

    void SetGroupState(List<int> group, TrafficLight.LightState st)
    {
        foreach (int s in group)
        {
            TrafficLight light = config.SignalLight(s);
            if (light != null) light.SetState(st);
        }
    }

    // ===================== Debug GUI =====================

    void OnGUI()
    {
        if (!Application.isPlaying || config == null) return;

        // Always read the key *before* any early return.
        //    Returning above this line would leave nothing listening for the key once D has hidden the
        //    panel, making it impossible to bring back. (Pure IMGUI, so it reads Event.current like CameraDirector.)
        Event ev = Event.current;
        if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == decisionPanelKey)
        {
            showDecisionPanel = !showDecisionPanel;
            ev.Use();                                  // stops anything else consuming the same key
        }

        // The Decision Inspector panel must not be tied to showDebugGUI.
        //    SimulationControlPanel.hideLegacyDebugGUI turns showDebugGUI off at Start to reduce clutter,
        //    so checking only showDebugGUI would mean the decision panel never appears at all.
        if (!showDebugGUI && !showDecisionPanel) return;

        UITheme.Begin();

        const float W = 288f;
        float x = Screen.width - W - 12f;

        // The trace panel hangs off the left edge of the status panel, or the screen edge if that is hidden
        float traceAnchor = showDebugGUI ? x : Screen.width - 12f;

        if (!showDebugGUI)
        {
            if (showDecisionPanel) DrawDecisionPanel(traceAnchor);
            UITheme.End();
            return;
        }

        Rect head = new Rect(x, 12f, W, 58f);
        GUI.Box(head, GUIContent.none);
        GUI.Label(new Rect(x + 12f, head.y + 6f, W - 24f, 18f), "SODA CONTROLLER", UITheme.Title);
        GUI.Label(new Rect(x + 12f, head.y + 24f, W - 24f, 15f), statusText, UITheme.Dim);
        GUI.Label(new Rect(x + 12f, head.y + 39f, W - 24f, 15f),
                  $"{state}  ·  snapshot {snapshotInterval:F0}s  ·  resets {queueStartResets}", UITheme.Dim);

        float y = head.y + head.height + 6f;
        const float CH = 44f;

        foreach (SignalHead h in heads)
        {
            TrafficLight light = config.SignalLight(h.signals[0]);
            TrafficLight.LightState st = light != null ? light.currentState : TrafficLight.LightState.Red;
            bool active = ContainsAny(activeGroup, h.signals);

            Rect card = new Rect(x, y, W, CH);
            GUI.Box(card, GUIContent.none);

            UITheme.Dot(new Rect(card.x + 10f, card.y + 8f, 10f, 10f), UITheme.SignalColor(st));
            GUI.Label(new Rect(card.x + 26f, card.y + 4f, 90f, 17f), h.name, UITheme.Title);
            UITheme.Pill(new Rect(card.x + W - 78f, card.y + 5f, 66f, 16f),
                         st.ToString().ToUpper(), UITheme.SignalColor(st));

            if (active)
            {
                float t = gMax > 0.01f ? phaseTimeRemaining / gMax : 0f;
                UITheme.Bar(new Rect(card.x + 26f, card.y + 26f, W - 120f, 5f), t, UITheme.SigGreen);
                GUI.Label(new Rect(card.x + W - 90f, card.y + 22f, 78f, 15f),
                          $"{phaseTimeRemaining:F1}s · {h.lastCount} cars", UITheme.Right);
            }
            else
            {
                float wait = WaitOf(h);
                float t = maxWaitTime > 0.01f ? wait / maxWaitTime : 0f;
                Color c = t >= 1f ? UITheme.SigRed : t >= 0.6f ? UITheme.SigYellow : UITheme.Neutral;
                UITheme.Bar(new Rect(card.x + 26f, card.y + 26f, W - 120f, 5f), t, c);
                GUI.Label(new Rect(card.x + W - 90f, card.y + 22f, 78f, 15f),
                          $"wait {wait:F0}/{maxWaitTime:F0}s", UITheme.Right);
            }

            y += CH + 4f;
        }

        if (showDecisionPanel) DrawDecisionPanel(x);

        UITheme.End();
    }

    // ===================== Decision Inspector =====================

    /// <summary>One trace row - the format "key|value|note", drawn as three columns</summary>
    static void TraceRow(Rect r, string packed, Color keyColor, GUIStyle noteStyle)
    {
        string[] p = packed.Split('|');
        GUIStyle ks = new GUIStyle(UITheme.Dim); ks.normal.textColor = keyColor;

        GUI.Label(new Rect(r.x, r.y, 74f, r.height), p.Length > 0 ? p[0] : "", ks);
        if (p.Length > 1)
            GUI.Label(new Rect(r.x + 76f, r.y, 74f, r.height), p[1], UITheme.Dim);
        if (p.Length > 2)
            GUI.Label(new Rect(r.x + 154f, r.y, r.width - 154f, r.height), p[2], noteStyle);
    }

    void DrawDecisionPanel(float statusX)
    {
        const float W = 344f;
        const float RH = 15f;      // height of one row
        float x = statusX - W - 8f;
        if (x < 8f) x = 8f;

        // ---- Compute the height first, so the frame always fits the content ----
        float h = 8f + 18f + 15f + 8f;                     // title + status line + separator
        h += 16f + queueView.Count * RH + 8f;              // queue block
        if (trace != null)
            foreach (TraceStep t in trace)
                h += 17f + t.options.Count * RH + (string.IsNullOrEmpty(t.verdict) ? 0f : RH + 2f) + 6f;
        h += 6f;

        Rect panel = new Rect(x, 12f, W, h);
        GUI.Box(panel, GUIContent.none);

        float cx = panel.x + 12f, cw = W - 24f;
        float y = panel.y + 8f;

        GUI.Label(new Rect(cx, y, cw, 18f), "SODA DECISION TRACE", UITheme.Title);
        y += 18f;
        GUI.Label(new Rect(cx, y, cw, RH),
                  $"phase #{phaseCounter}  ·  {state}  ·  snapshot {snapshotInterval:F1}s  ·  [{decisionPanelKey}] hide",
                  UITheme.Dim);
        y += RH + 4f;
        UITheme.Rule(new Rect(cx, y, cw, 1f));
        y += 5f;

        // ---- Queue: what the algorithm sees, ordered by queueStart ----
        GUI.Label(new Rect(cx, y, cw, 16f), "QUEUE — ordered by queueStart", UITheme.H2);
        y += 16f;
        foreach (string row in queueView)
        {
            bool skipped = row.Contains("SKIPPED");
            TraceRow(new Rect(cx, y, cw, RH), row,
                     skipped ? UITheme.Neutral : UITheme.TextHi,
                     skipped ? UITheme.Dim : UITheme.Dim);
            y += RH;
        }
        y += 6f;

        // ---- Q1..Q4 ----
        if (trace == null) return;
        foreach (TraceStep t in trace)
        {
            bool isActive = t.id == activeStepId;
            Color head = isActive ? UITheme.Accent : t.resolved ? UITheme.TextMid : UITheme.TextDim;

            GUIStyle hs = new GUIStyle(UITheme.H2); hs.normal.textColor = head;
            GUI.Label(new Rect(cx, y, cw, 16f), $"{t.id}   {t.title}", hs);
            if (isActive) UITheme.Dot(new Rect(cx + cw - 9f, y + 4f, 7f, 7f), UITheme.Accent);
            y += 17f;

            foreach (string opt in t.options)
            {
                TraceRow(new Rect(cx + 6f, y, cw - 6f, RH), opt, UITheme.TextMid, UITheme.Dim);
                y += RH;
            }

            if (!string.IsNullOrEmpty(t.verdict))
            {
                GUIStyle vs = new GUIStyle(UITheme.Dim);
                vs.normal.textColor = t.resolved ? UITheme.SigGreen : UITheme.TextDim;
                GUI.Label(new Rect(cx + 6f, y, cw - 6f, RH), "-> " + t.verdict, vs);
                y += RH + 2f;
            }
            y += 6f;
        }
    }
}
