using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// SODA (patched) — Sampled-Observation Demand-Adaptive signal control
///
/// ===== ทำไมต้องเขียนใหม่ =====
/// controller เดิมพึ่งสองอย่างที่ "ติดตั้งจริงไม่ได้":
///   1) ติดตามรถรายคันต่อเนื่อง (ต้องมี ByteTrack/DeepSORT ต่อท้าย YOLO) — hardware ไม่ไหว
///   2) rest in green (ปล่อยไฟเขียวค้างตอนว่าง) — ไฟจริงปล่อยแดงทุกเลนตอนว่าง
/// ถ้ารัน batch ด้วยของเดิม = ได้ผลของ controller ที่ deploy ไม่ได้
///
/// ===== แก้ 3 จุด (ที่เหลือไม่แตะ) =====
///   การมองเห็น : ROI ติดตามต่อเนื่องมี ID รายคัน -> snapshot ทุก 2 วิ "นับอย่างเดียว ไม่มี ID"
///   ตอนว่าง    : rest in green                  -> ALL-RED ทุกเลน + Eyes
///   เลือก leader: argmin เวลารถคันแรกสุดมาถึง    -> argmin queueStart[h] (timestamp ระดับเลน)
///
/// ไม่แตะ: conflict matrix / concurrent grouping / A+A2 forced pairing / สูตร G /
///         clearance ที่ไม่ตัดเลนที่ยังต้องเขียว / anti-starvation / max continuous green
///
/// ===== state ทั้งหมดมีแค่ 8 ตัวเลข =====
/// หัวไฟ 4 ดวง x { queueStart, lastCount } — ไม่มี ID รถ ไม่มีการจับคู่ข้ามเฟรม
/// ไม่มี list ที่ยาวตามจำนวนรถ นี่คือจุดที่ tracker ถูกตัดออกจริง ๆ
/// </summary>
public class AdaptiveController : MonoBehaviour
{
    [Header("Intersection")]
    public IntersectionConfig config;

    // ===================== Eyes (sampled observation) =====================

    [Header("Eyes — Sampled Observation")]
    [Tooltip("ถ่าย snapshot ทุกกี่วินาที — นับรถอย่างเดียว ไม่ต้องมี tracker")]
    public float snapshotInterval = 2f;
    [Tooltip("เพดานของหน้าต่างเก็บข้อมูล ไม่ใช่ค่าคงที่ — ออกก่อนได้ด้วย early exit")]
    public float collectWindow = 7f;
    [Tooltip("Early exit (ก): พบรถกี่หัวไฟจึงเลิกรอ")]
    public int earlyExitHeadCount = 2;
    [Tooltip("Early exit (ข): หัวไฟใดมีรถถึงจำนวนนี้จึงเลิกรอ")]
    public int earlyExitDemand = 3;

    // ===================== Green time (carried over unchanged) =====================

    [Header("Timing Formula (HCM-based)")]
    public float gMin = 8f;
    public float gMax = 45f;
    public float yellowDuration = 3f;
    public float allRedDuration = 2f;
    public float startupLostTime = MEASURED_STARTUP_LOST_TIME;
    public float saturationHeadway = MEASURED_SATURATION_HEADWAY;

    // ===== ค่าอ้างอิงที่วัดได้จริง — แหล่งความจริงแหล่งเดียวของทั้งโปรเจกต์ =====
    // ⚠️ Unity เก็บค่าของ component ที่วางไว้ในซีนลง SampleScene.unity (serialize)
    //    แล้ว "ทับ" ค่า default ในสคริปต์เสมอตอนโหลดซีน
    //    => แก้ค่า default ในไฟล์ .cs เพียงอย่างเดียว ไม่มีผลกับซีนที่มี component อยู่แล้ว
    //    ป้องกันด้วย 3 ชั้น: (1) const ตรงนี้ (2) EnforceMeasuredParameters() ตอน Awake
    //    (3) ABACSceneBuilder เรียก LoadMeasuredPreset() ทุกครั้งที่ build ซีน
    public const float MEASURED_STARTUP_LOST_TIME  = 2.95f;   // วัดจริง 39 ตัวอย่าง (HCM default = 2.0)
    public const float MEASURED_SATURATION_HEADWAY = 2.31f;   // วัดจริง sd 0.81 (HCM default = 2.0)
    // ⛔ เลิกใช้ counting window แล้ว (เคยเป็น 4 วิ)
    //    มันถามคำถามเดียวกับ COLLECT ด้วยเกณฑ์ตัวเดียวกัน (< 3 คัน) แล้วรอซ้ำอีกรอบ
    //    รายละเอียดอยู่ที่ Q2 ด้านล่าง

    [Tooltip("true = บังคับใช้ค่าที่วัดจริงเสมอ ถ้าค่าในซีนเพี้ยนจะแก้ให้อัตโนมัติพร้อมขึ้น error\n" +
             "ปิดเฉพาะตอนตั้งใจทำ sensitivity analysis (เช่นเทียบ HCM 2.0 กับค่าที่วัดได้)")]
    public bool useMeasuredParameters = true;

    [Tooltip("เขียวต่อเนื่องกลุ่มเดิมได้นานสุดกี่วินาที ก่อนบังคับปล่อยเฟส (max green ตามมาตรฐาน)")]
    public float maxContinuousGreen = 90f;

    [Header("Concurrent Phase")]
    [Tooltip("true = รวมเลนที่ไม่ conflict ให้เขียวพร้อมกัน (แนะนำ) / false = ปล่อยทีละเลน")]
    public bool enableConcurrentPhases = true;
    [Tooltip("หัวไฟที่จะถูกรวมเข้ากลุ่มต้องมีรถอย่างน้อยกี่คัน")]
    public int minDemandToJoinGroup = 1;

    [Header("Gap-out")]
    public bool enableGapOut = true;
    [Tooltip("ต้องเห็น count = 0 ติดกันกี่ snapshot จึงตัดเฟส\n" +
             "2 snapshot = 4 วิ — ยืนยันครั้งเดียวไม่พอ ถ้า detect พลาดรอบเดียวจะตัดเฟสทั้งที่ยังมีรถ")]
    public int gapOutSnapshots = 2;

    [Header("Anti-Starvation (รอได้ไม่เกินกี่วิ)")]
    public float maxWaitTime = 45f;
    [Tooltip("ต้องเขียวอย่างน้อยเท่านี้ก่อนจะโดนตัดสั้นได้ (กันไฟสั้นเกินระบายไม่ทัน)")]
    public float minGreenBeforeInterrupt = 20f;
    [Tooltip("สัดส่วนของ maxWaitTime ที่จะบังคับเลือกเป็นเฟสถัดไป (ทางออกฉุกเฉิน)")]
    public float starvationSelectThreshold = 0.95f;
    [Tooltip("สัดส่วนของ maxWaitTime ที่จะตัดจบเฟสปัจจุบันก่อนเวลา")]
    public float starvationInterruptThreshold = 0.90f;

    [Header("Debug Display")]
    public bool showDebugGUI = true;
    public bool verboseLog = false;

    [Header("Decision Inspector")]
    [Tooltip("แผงแยกด้านขวา แสดงว่าอัลกอริทึมกำลังคิดขั้นไหน มีอะไรอยู่ในคิว และตัดสินใจจากตัวเลือกอะไรบ้าง")]
    public bool showDecisionPanel = true;
    [Tooltip("ปุ่มสลับเปิด/ปิดแผงขั้นตอนการคิด")]
    public KeyCode decisionPanelKey = KeyCode.D;

    // ===================== Signal head =====================

    /// <summary>
    /// หัวไฟหนึ่งดวง = สัญญาณไฟจริงหนึ่งดวง ไม่ใช่หนึ่งเลน
    /// H_A คุมทั้ง A (เลนใน) และ A2 (เลนนอก) เพราะเป็นไฟดวงเดียวกันจริง ๆ
    /// คิวบริการจึงมีสมาชิกได้มากสุด 4 ตัว ไม่ใช่ 7
    /// </summary>
    public class SignalHead
    {
        public string name;
        /// <summary>ตำแหน่งใน ORIGINAL_SEQUENCE — ใช้ตัดสินเมื่อ queueStart เท่ากันเป๊ะ</summary>
        public int priority = int.MaxValue;
        public List<int> signals = new List<int>();   // signal index ที่ใช้ TrafficLight ดวงเดียวกัน
        public List<ROIZone> rois = new List<ROIZone>();

        /// <summary>เวลาที่หัวไฟนี้เปลี่ยนจากว่าง -> มีรถ (Infinity = ว่าง)</summary>
        public float queueStart = Mathf.Infinity;
        /// <summary>จำนวนรถจาก snapshot ก่อนหน้า — ใช้ตรวจขอบขาขึ้นเท่านั้น</summary>
        public int lastCount = 0;
    }

    /// <summary>
    /// ลำดับเฟสเดิมของแยก — ใช้เป็น tie-break เมื่อ queueStart เสมอกัน
    ///
    /// ทำไมต้องมี: ทุกหัวไฟถูกอัปเดตใน Snapshot() ครั้งเดียวกัน สองหัวที่รถมาถึงในหน้าต่าง
    /// 2 วิเดียวกันจึงได้ queueStart "เท่ากันระดับบิต" — เสมอกันเป็นเรื่องปกติ ไม่ใช่เรื่องหายาก
    /// เดิมการเสมอถูกตัดสินด้วยลำดับที่บังเอิญประกาศตัวแปรไว้ (ไม่ได้เขียนไว้ที่ไหน) และ
    /// List&lt;T&gt;.Sort ก็ไม่การันตี stable ทำให้ผลไม่แน่นอนตามสัญญาของภาษา
    ///
    /// ⚠️ tie-break ตัวนี้ไม่ได้ตัดสินแค่ว่าใครนำ — มันตัดสินว่ากลุ่มประกอบด้วยใคร
    ///    leader = B แล้ว A กับ Bx เสมอกัน: A เข้าก่อน -> {B,A,A2} · Bx เข้าก่อน -> {B,Bx}
    ///    และเพราะ A ชนกับ Bx การเข้าข้าง A จึงตัด Bx ออกอย่างเป็นระบบ
    ///    นับไว้ที่ TieBreaksApplied / TieBreaksExcludingBx เพื่อวัดขนาดของ bias นี้
    /// </summary>
    public static readonly string[] ORIGINAL_SEQUENCE = { "H_A", "H_B", "H_Bx", "H_CB" };

    public enum EyeState { AllRedIdle, Collect, Serve }

    // ===================== runtime =====================

    private List<SignalHead> heads = new List<SignalHead>();
    private List<int> activeGroup = new List<int>();
    private EyeState state = EyeState.AllRedIdle;
    private float phaseTimeRemaining = 0f;
    private float continuousGreenTimer = 0f;
    private bool forcePhaseChange = false;
    private string statusText = "Idle";

    private int groupZeroStreak = 0;          // snapshot ติดกันที่กลุ่มปัจจุบันว่าง (gap-out)

    /// <summary>
    /// เดินขึ้นทีละ 1 ทุกครั้งที่ถ่าย snapshot — เป็น "นาฬิกาเดียว" ของอัลกอริทึมทั้งตัว
    /// ทุกการตัดสินใจรอ index ตัวนี้ขยับ ไม่มีลูปไหนตั้งนาฬิกาของตัวเองแข่ง
    /// (เดิม COLLECT เช็กทุก 1 วิ ทั้งที่ข้อมูลอัปเดตทุก 2 วิ = เช็กครึ่งนึงอ่านของเก่าซ้ำ
    ///  แถมยังหน่วงสุ่ม 0-1 วิ ระหว่าง "ข้อมูลมา" กับ "อัลกอริทึมเห็น" โดยไม่ได้อะไรเลย)
    /// </summary>
    private int snapshotIndex = 0;
    public int SnapshotIndex => snapshotIndex;

    // ===== วัด "เขียวเปล่า" ต่อเฟส (ยังไม่เปลี่ยนพฤติกรรม แค่เก็บหลักฐาน) =====
    // gap-out ปัจจุบันยิงเมื่อ "ทุกเลนในกลุ่มว่าง" เลนที่ว่างก่อนจึงติดเขียวรอเลนอื่นอยู่เฉย ๆ
    // ความละเอียดเท่ากับ snapshotInterval (2 วิ) ตามที่ detector จริงมองเห็น ไม่ใช่ ground truth
    private Dictionary<int, float> laneEmptyAt = new Dictionary<int, float>();
    private float phaseStartTime = 0f;
    private string lastEndReason = "completed";
    private Coroutine snapshotLoop = null;

    // ===== สถิติสำหรับ log =====
    // ===== บันทึกขั้นตอนการคิด (สำหรับแผง Decision Inspector) =====
    /// <summary>หนึ่งขั้นของการตัดสินใจ — เก็บทั้ง "ตัวเลือกที่พิจารณา" และ "ผลสรุป"</summary>
    public class TraceStep
    {
        public string id;                                  // Q1..Q4
        public string title;
        public List<string> options = new List<string>();  // ตัวเลือกที่เอามาชั่งน้ำหนัก
        public string verdict = "";                        // ผลสรุปหนึ่งบรรทัด
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
    /// <summary>เฟสที่มีหัวไฟตั้งแต่ 2 ดวงขึ้นไป queueStart เท่ากันเป๊ะ (tie-break ได้ทำงาน)</summary>
    public int   TieBreaksApplied     => tieBreaksApplied;
    /// <summary>ในจำนวนนั้น กี่เฟสที่ tie-break ตัด H_Bx ออกจากกลุ่ม แทนหัวไฟที่เสมอกับมัน</summary>
    public int   TieBreaksExcludingBx => tieBreaksExcludingBx;
    public EyeState CurrentState    => state;
    /// <summary>หัวไฟทั้งหมด — เปิดให้เทสตรวจสถานะภายในได้</summary>
    public List<SignalHead> Heads   => heads;
    /// <summary>นับรถของหัวไฟหนึ่งดวง (public เพื่อให้เทสเรียกได้)</summary>
    public int CountOfHead(SignalHead h) => CountOf(h);

    // ===================== Lifecycle =====================

    void Awake()
    {
        EnforceMeasuredParameters();

        if (config == null) config = FindFirstObjectByType<IntersectionConfig>();
        if (config == null)
        {
            Debug.LogError("[AdaptiveController] ไม่พบ IntersectionConfig ในซีน");
            return;
        }
        config.EnsureBuilt();
        BuildHeads();
    }

    /// <summary>
    /// จัดกลุ่ม signal index ที่ใช้ TrafficLight "object เดียวกัน" ให้เป็นหัวไฟดวงเดียว
    /// (A กับ A2 ชี้ component ตัวเดียวกันโดยตั้งใจ จึงรวมกันอัตโนมัติ ไม่ต้อง hard-code)
    /// </summary>
    /// <summary>public เพื่อให้เทสสร้าง head ได้โดยไม่ต้องพึ่ง Awake ของ Unity</summary>
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

        // ลำดับ Original — ประกาศไว้ชัด ๆ ไม่ใช่ผลพลอยได้จากลำดับที่วนเจอ
        foreach (SignalHead h in heads)
        {
            h.priority = System.Array.IndexOf(ORIGINAL_SEQUENCE, h.name);
            if (h.priority < 0)
            {
                h.priority = ORIGINAL_SEQUENCE.Length + heads.IndexOf(h);
                Debug.LogWarning($"[SODA] {h.name} ไม่อยู่ใน ORIGINAL_SEQUENCE — tie-break จะไม่แน่นอน");
            }
        }

        if (verboseLog)
        {
            foreach (SignalHead h in heads)
            {
                string lanes = "";
                foreach (int s in h.signals) lanes += config.SignalName(s) + " ";
                Debug.Log($"[SODA] {h.name} (priority {h.priority}) คุมเลน: {lanes}({h.rois.Count} ROI)");
            }
        }
    }

    public void ForceStop()
    {
        StopAllCoroutines();
        snapshotLoop = null;
        activeGroup.Clear();
        continuousGreenTimer = 0f;
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
    /// ตรวจว่าค่าที่ซีน serialize ไว้ตรงกับค่าที่วัดจริงหรือไม่ ถ้าไม่ตรงจะแก้ให้แล้วขึ้น error ดัง ๆ
    /// เพื่อไม่ให้ batch 270 รอบวิ่งจนจบด้วยค่าตำราโดยไม่มีใครรู้
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
            $"[AdaptiveController] ค่าที่ซีนเก็บไว้ไม่ตรงกับค่าที่วัดจริง — แก้ให้อัตโนมัติแล้ว\n" +
            $"  startupLostTime   {oldL} -> {MEASURED_STARTUP_LOST_TIME}\n" +
            $"  saturationHeadway {oldH} -> {MEASURED_SATURATION_HEADWAY}\n" +
            $"  ⚠️ การแก้นี้อยู่แค่ในหน่วยความจำ ให้เปิด Inspector ของ AdaptiveController " +
            $"กด Load Measured Parameters แล้ว Save Scene (Ctrl+S) ไม่งั้นรอบหน้าจะเพี้ยนอีก");
        return true;
    }

    // ===================== Decision trace =====================

    /// <summary>
    /// เก็บ "ตัวเลือกที่อัลกอริทึมเห็น" ก่อนตัดสินใจ — คิว + เกณฑ์ Q1 + ผู้เข้าชิง Q2
    /// อ่านอย่างเดียว ไม่เปลี่ยนสถานะใด ๆ จึงให้เทสเรียกตัวเดียวกับที่ ServeOnePhase เรียกได้
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

    /// <summary>ขั้นตอนการคิดของเฟสล่าสุด — ให้แผง Inspector และเทสอ่านได้</summary>
    public TraceStep[] DecisionTrace { get { EnsureTrace(); return trace; } }
    /// <summary>คิวที่อัลกอริทึมมองเห็น เรียงตาม queueStart</summary>
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
        if (!showDecisionPanel) return;
        EnsureTrace();
        phaseCounter++;
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

    /// <summary>ถ่ายภาพคิวปัจจุบัน เรียงตาม queueStart เก่าสุดก่อน = ลำดับที่อัลกอริทึมมองเห็น</summary>
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
    /// นับรถในหัวไฟหนึ่งดวง = ผลรวมของ ROI ที่มันคุม
    /// ⚠️ ใช้ GetSampledCount() เท่านั้น — API เดียวที่กล้องไม่มี tracker ทำได้จริง
    ///    ห้ามเรียก GetOldestArrivalTime / GetLongestWaitTime / GetCarsInZone จากที่ไหนก็ตามในไฟล์นี้
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
    /// หนึ่ง snapshot: อัปเดต queueStart/lastCount ของทุกหัวไฟ และนับ streak ของ gap-out
    /// นี่คือ "ทุกสิ่งที่ controller มองเห็น" — ตัวเลข 4 ตัวทุก 2 วินาที
    /// </summary>
    public void Snapshot()
    {
        snapshotIndex++;

        foreach (SignalHead h in heads)
        {
            int c = CountOf(h);

            // ขอบขาขึ้น: ว่าง -> มีรถ = เริ่มจับเวลารอของเลนนี้
            if (h.lastCount == 0 && c > 0) h.queueStart = Time.time;
            // ว่างเมื่อไหร่ ล้างทันที
            if (c == 0) h.queueStart = Mathf.Infinity;

            h.lastCount = c;
        }

        // ---- จดว่าแต่ละเลนในกลุ่มว่างเมื่อไหร่ (ครั้งแรกเท่านั้น) ----
        if (activeGroup.Count > 0)
        {
            foreach (int sig in activeGroup)
            {
                if (laneEmptyAt.ContainsKey(sig)) continue;
                if (DemandOf(sig) == 0) laneEmptyAt[sig] = Time.time - phaseStartTime;
            }
        }

        // ---- gap-out ต้องยืนยัน 2 snapshot ติดกัน ----
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

    /// <summary>รอจนกว่าจะมี snapshot ใหม่ — ทุกการตัดสินใจผูกกับจังหวะนี้จังหวะเดียว</summary>
    IEnumerator WaitForNextSnapshot()
    {
        int seen = snapshotIndex;
        while (snapshotIndex == seen) yield return null;
    }

    /// <summary>รอ snapshot ใหม่ แต่ไม่เกิน deadline (ใช้กับเพดานเวลาของ COLLECT)</summary>
    IEnumerator WaitForNextSnapshotOrDeadline(float deadline)
    {
        int seen = snapshotIndex;
        while (snapshotIndex == seen && Time.time < deadline) yield return null;
    }

    IEnumerator RunSoda()
    {
        while (true)
        {
            yield return null;   // กันลูปหมุนฟรีในเฟรมเดียว

            // ---------- ALL-RED IDLE ----------
            // ไฟจริงปล่อยแดงทุกเลนตอนว่าง ไม่มี rest-in-green
            state = EyeState.AllRedIdle;
            statusText = "ALL-RED — waiting for demand";
            if (activeGroup.Count > 0)
            {
                yield return StartCoroutine(CloseGroup(activeGroup));
            }

            // ประเมินทุก snapshot พอดี ไม่เร็วกว่าและไม่ช้ากว่าข้อมูลที่มี
            while (!AnyDemand()) yield return StartCoroutine(WaitForNextSnapshot());

            // ---------- COLLECT ----------
            yield return StartCoroutine(CollectPhase());

            // ---------- SERVE (วนจนแยกว่าง) ----------
            state = EyeState.Serve;
            while (AnyDemand())
            {
                yield return StartCoroutine(ServeOnePhase());
                if (!AnyDemand()) break;
            }
        }
    }

    /// <summary>
    /// หน้าต่างเก็บข้อมูล — 7 วิเป็นเพดาน ไม่ใช่ค่าคงที่
    /// ออกก่อนได้ถ้า (ก) พบรถ >= 2 หัวไฟ หรือ (ข) หัวไฟใดมีรถ >= 3 คัน
    /// ถ้าบังคับรอครบทุกครั้ง จะแย่กว่า controller เดิมที่มี rest-in-green
    /// </summary>
    IEnumerator CollectPhase()
    {
        state = EyeState.Collect;
        float t0 = Time.time;
        float deadline = t0 + collectWindow;

        // เงื่อนไข early exit ประเมิน "หลัง snapshot" เท่านั้น เพราะเป็นการตัดสินใจจากข้อมูล
        // ส่วนเพดาน 7 วิเป็น timeout ล้วน ๆ ไม่ได้ดูข้อมูล จึงตัดตรงวินาทีที่ 7 ได้เลย
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
        // ถ่าย snapshot ณ วินาทีที่ตัดสินใจ เพื่อไม่ให้ใช้ตัวเลขที่เก่าได้ถึง 2 วิ
        // ยังเป็นการ "นับอย่างเดียว" เหมือนเดิม แค่สั่งถ่ายตอนต้องใช้แทนที่จะรอ tick ถัดไป
        // (เทียบได้กับ D_green ในสเปก SODA ที่นับใหม่ก่อนเปิดไฟเขียวทุกเฟส)
        Snapshot();
        TraceDecisionInputs();

        // ===== Q1: มีใครใกล้อดตายไหม =====
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

            // ===== Q2: ใครรอนานที่สุด =====
            TraceEnter("Q2");
            leader = ChooseLeaderByQueueStart(null);
            if (leader == null) { TraceVerdict("Q2", "no head has demand"); yield break; }

            // ⛔ ไม่มี counting window แล้ว — COLLECT เป็นตัวสะสมดีมานด์ตัวเดียวของระบบ
            //    เหตุผลที่ตัด:
            //    1) ถามคำถามเดียวกันด้วยเกณฑ์เดียวกัน — earlyExitDemand กับ demandThresholdSkipWait
            //       เป็น 3 คันทั้งคู่ เลนที่มีน้อยกว่า 3 จึงทำให้ COLLECT รอจนครบเพดาน 7 วิ
            //       "แล้วยังโดนรออีก 4 วิ" ด้วยเหตุผลเดียวกันเป๊ะ = ลงโทษสองต่อ (S1: รอ 11 วิเพื่อรถ 1 คัน)
            //    2) ตอนพีคมันแอบยืดไฟเขียวของกลุ่มก่อนหน้าไปอีก 4 วิ เพราะกลุ่มเดิมยังเขียวอยู่
            //       ระหว่างรอ — เฟสจึงยาวเกิน G ที่คำนวณไว้โดยไม่มีใน log
            //    3) มันเป็น WaitForSeconds ตัวสุดท้ายที่ไม่ผูกกับนาฬิกา snapshot
            TraceVerdict("Q2", $"LEADER -> {leader.name} (oldest queue, {WaitOf(leader):F1}s)");
        }
        if (forcePhaseChange)
        {
            SignalHead other = ChooseLeaderByQueueStart(activeGroup);
            if (other != null) leader = other;
            forcePhaseChange = false;
        }

        // ===== Q3: ใครไปพร้อมกันได้อีก =====
        TraceEnter("Q3");
        List<int> nextGroup = BuildCompatibleGroup(leader.signals[0]);
        TraceVerdict("Q3", $"GROUP -> {{{config.DescribeGroup(nextGroup)}}}");
        CountTieBreaks(nextGroup);

        // หัวไฟที่ count = 0 ถูกข้ามทั้งรอบ — แหล่งที่มาใหญ่ที่สุดของการชนะ Fixed
        foreach (SignalHead h in heads)
            if (h.lastCount == 0 && !ContainsAny(nextGroup, h.signals)) skippedHeadTotal++;

        // ===== clearance: ตัดเฉพาะเลนที่ต้องเปลี่ยนเป็นแดง =====
        List<int> dropping = Except(activeGroup, nextGroup);
        if (dropping.Count > 0)
        {
            statusText = $"Yellow — {config.DescribeGroup(dropping)}";
            SetGroupState(dropping, TrafficLight.LightState.Yellow);
            yield return StartCoroutine(CountdownPhase(yellowDuration));

            statusText = "All-Red clearance";
            SetGroupState(dropping, TrafficLight.LightState.Red);
            activeGroup = Except(activeGroup, dropping);
            ResetQueueStartFor(dropping);
            yield return StartCoroutine(CountdownPhase(allRedDuration));
        }

        // ===== เปิดเขียว =====
        List<int> turningOn = Except(nextGroup, activeGroup);
        if (turningOn.Count > 0 || dropping.Count > 0 || continuousGreenTimer >= maxContinuousGreen)
            continuousGreenTimer = 0f;
        SetGroupState(turningOn, TrafficLight.LightState.Green);
        activeGroup = new List<int>(nextGroup);
        groupZeroStreak = 0;
        laneEmptyAt.Clear();
        phaseStartTime = Time.time;
        lastEndReason = "completed";

        // ===== Q4: ให้เขียวนานเท่าไหร่ (นับใหม่สด ๆ ตอนเฟสเริ่ม) =====
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
        LogPhaseTrace(greenTime);
    }

    /// <summary>
    /// นับว่า tie-break ได้ทำงานจริงกี่ครั้ง และกี่ครั้งที่มันตัด H_Bx ออก
    ///
    /// เป็นการวัดล้วน ๆ ไม่แตะการตัดสินใจใด ๆ — เรียกหลังกลุ่มถูกสร้างเสร็จแล้ว
    /// เหตุผลที่ต้องแยกนับ Bx: A ชนกับ Bx การ tie-break ที่เข้าข้าง A (priority สูงกว่า)
    /// จึงตัด Bx ออกจากกลุ่มอย่างเป็นระบบ และ Bx คือเลนที่ v/c = 1.51 ที่งานนี้ศึกษาอยู่
    /// ทิศทางของ bias เป็นแบบ conservative (ทำให้ adaptive ดูแย่กว่าจริงสำหรับ Bx)
    /// แต่ต้องรู้ขนาดของมัน ไม่ใช่รู้แค่ทิศทาง
    /// </summary>
    void CountTieBreaks(List<int> group)
    {
        // มีหัวไฟตั้งแต่ 2 ดวงที่มีรถและ queueStart เท่ากันเป๊ะไหม
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

        // Bx มีรถ แต่ไม่ได้เข้ากลุ่ม ทั้งที่มีสมาชิกในกลุ่มที่ queueStart เท่ากับมัน
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
                    Debug.Log($"[SODA] tie-break ตัด H_Bx ออก โดย {h.name} " +
                              $"(queueStart เท่ากันที่ {bx.queueStart:F1})");
                return;
            }
        }
    }

    /// <summary>ปิดกลุ่มที่เขียวอยู่ให้เป็นแดง (ใช้ตอนกลับเข้า ALL-RED IDLE)</summary>
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
    /// 🛑 หัวใจของอัลกอริทึม — ไฟเขียวจบแล้วถ้ายังมีรถเหลือ ต้องเริ่มจับเวลารอใหม่
    ///
    /// ถ้าลืมบรรทัดนี้: เลนที่ไม่เคยว่าง (Bx ที่ v/c = 1.51) จะถือ queueStart เก่าค้างตลอดกาล
    /// -> ชนะ argmin ทุกครั้ง -> เลนอื่นไม่มีวันได้ไฟ
    /// บั๊กนี้ไม่โผล่ตอนเทสรถน้อย เพราะทุกเลนว่างเป็นระยะแล้ว queueStart reset เอง
    /// แต่พังทันทีตอน MorningPeak — จึงมีเทสเฉพาะ (QueueStartTest)
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

            // ---- Max green ----
            if (continuousGreenTimer >= maxContinuousGreen && AnyDemandOutside(group))
            {
                Debug.Log($"[SODA] Max green {maxContinuousGreen}s reached on " +
                          $"{config.DescribeGroup(group)} — forcing phase change");
                forcePhaseChange = true;
                lastEndReason = "max-green";
                yield break;
            }

            // ---- Gap-out: ต้องเห็นว่างติดกัน gapOutSnapshots ครั้ง ----
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
    /// สรุปเฟสที่เพิ่งจบลง log — โดยเฉพาะ "เลนไหนว่างตั้งแต่วินาทีที่เท่าไหร่"
    ///
    /// idleLaneSeconds = ผลรวมของ (เวลาที่เฟสจบ − เวลาที่เลนนั้นว่าง) ทุกเลนในกลุ่ม
    /// = เวลาที่เลนติดเขียวทั้งที่ไม่มีรถแล้ว ซึ่งกู้คืนได้ถ้าเปลี่ยน gap-out เป็นรายเลน
    /// ยังไม่เปลี่ยนพฤติกรรมใด ๆ — เก็บตัวเลขไว้ตัดสินใจหลังรัน batch
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

    /// <summary>หัวไฟที่รอเกิน threshold และรอนานที่สุด — ไม่นับหัวที่กำลังเขียวอยู่</summary>
    public SignalHead FindStarvingHead(float thresholdRatio, List<int> excludeSignals)
    {
        float threshold = maxWaitTime * thresholdRatio;
        SignalHead worst = null;

        foreach (SignalHead h in heads)
        {
            if (excludeSignals != null && ContainsAny(excludeSignals, h.signals)) continue;
            if (h.lastCount == 0) continue;
            if (WaitOf(h) < threshold) continue;

            // รอนานสุด = queueStart เก่าสุด; เสมอกัน -> ลำดับเฟสเดิม A->B->Bx->CB
            // ใช้ comparator ตัวเดียวกับ Q2/Q3 (SODA_BASELINE §8.3 — เดิมใช้ >= ทำให้หัวท้ายสุดชนะ
            // เมื่อเสมอ ซึ่งกลับทิศกับ Option A และไม่ตรงกับ soda.py)
            if (worst == null || CompareHeads(h, worst) < 0) worst = h;
        }
        return worst;
    }

    /// <summary>
    /// เลือกหัวไฟที่คิวเริ่มก่อตัวเก่าสุด — เวลาตัดสิน "ใครได้ไฟ" เพื่อความเป็นธรรม
    /// (จำนวนรถตัดสินแค่ "ได้นานแค่ไหน" ถ้าเอาจำนวนมาจัดลำดับด้วย H_A จะชนะทุกครั้ง
    ///  แล้ว H_Bx ที่ v/c = 1.51 จะไม่มีวันได้ไฟ)
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
    /// ลำดับสมบูรณ์: queueStart เก่ากว่ามาก่อน ถ้าเท่ากันเป๊ะใช้ลำดับเฟสเดิมของแยก
    /// เป็น total order จึงให้ผลเดียวกันเสมอ ไม่ต้องพึ่งว่า sort จะ stable หรือไม่
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

        // เรียงหัวไฟที่เหลือตาม queueStart เก่าสุดก่อน (ไม่ใช่ตามจำนวนรถ)
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

    /// <summary>เติมเลนอื่นที่ใช้ TrafficLight component ตัวเดียวกับสมาชิกที่มีอยู่แล้วเข้ากลุ่ม (A2 ตาม A)</summary>
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
    /// สูตร HCM: G = startupLostTime + D x saturationHeadway
    /// เฟสที่เปิดหลายเลนใช้เลนที่คิวยาวสุดเป็นตัวกำหนด เพราะทุกเลนระบายขนานกัน
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

        // 🛑 อ่านปุ่ม "ก่อน" ทางออกด่วนเสมอ
        //    ถ้า return ก่อนถึงบรรทัดนี้ พอกด D ปิดแล้วจะไม่มีใครฟังปุ่มอีกเลย = เปิดกลับไม่ได้ตลอดกาล
        //    (โปรเจกต์นี้ใช้ IMGUI ล้วน จึงอ่านจาก Event.current เหมือน CameraDirector)
        Event ev = Event.current;
        if (ev != null && ev.type == EventType.KeyDown && ev.keyCode == decisionPanelKey)
        {
            showDecisionPanel = !showDecisionPanel;
            ev.Use();                                  // กันไม่ให้ตัวอื่นกินปุ่มเดียวกันซ้ำ
        }

        // ⚠️ ห้ามผูกแผง Decision Inspector ไว้กับ showDebugGUI
        //    SimulationControlPanel.hideLegacyDebugGUI ปิด showDebugGUI ทิ้งตั้งแต่ Start เพื่อลดความรก
        //    ถ้าเช็ก showDebugGUI อย่างเดียว แผงขั้นตอนการคิดจะไม่มีวันโผล่เลย
        if (!showDebugGUI && !showDecisionPanel) return;

        UITheme.Begin();

        const float W = 288f;
        float x = Screen.width - W - 12f;

        // แผง trace ยึดขอบซ้ายของแผงสถานะ ถ้าแผงสถานะถูกปิดก็ยึดขอบจอแทน
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

    /// <summary>หนึ่งแถวของ trace — รูปแบบ "คีย์|ค่ากลาง|หมายเหตุ" วาดเป็น 3 คอลัมน์</summary>
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
        const float RH = 15f;      // ความสูงหนึ่งแถว
        float x = statusX - W - 8f;
        if (x < 8f) x = 8f;

        // ---- คำนวณความสูงก่อน เพื่อให้กรอบพอดีเนื้อหาเสมอ ----
        float h = 8f + 18f + 15f + 8f;                     // หัวเรื่อง + บรรทัดสถานะ + เส้นคั่น
        h += 16f + queueView.Count * RH + 8f;              // บล็อกคิว
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

        // ---- คิว: สิ่งที่อัลกอริทึมมองเห็น เรียงตาม queueStart ----
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
