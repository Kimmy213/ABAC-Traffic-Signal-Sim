using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// หน้าจอควบคุมการจำลองตอน runtime
///
///   • เล่น / หยุดชั่วคราว / หยุด / เริ่มใหม่
///   • เลือกชุดทดสอบ (test case) — มีทั้งแบบกำหนดเองที่ผลนิ่งทุกครั้ง และแบบสุ่ม Poisson
///   • สลับระบบสัญญาณไฟ Fixed ↔ Adaptive ได้ทันที
///   • ปรับความเร็วจำลอง, ดูสถิติสด, export CSV
///
/// วิธีติดตั้ง: แปะสคริปต์นี้ลงบน GameController (หรือ object ไหนก็ได้) แล้วลาก reference ให้ครบ
/// หรือกด ABAC ▸ Build 4-Phase Scene ให้ builder ต่อให้อัตโนมัติ
/// </summary>
public class SimulationControlPanel : MonoBehaviour
{
    public enum ControllerKind { Fixed, Adaptive }
    public enum RunState { Idle, Running, Paused, Finished }

    [Header("References (เว้นว่างได้ ระบบจะหาเองในซีน)")]
    public IntersectionConfig config;
    public FixedController fixedController;
    public AdaptiveController adaptiveController;
    public DataLogger dataLogger;
    public ScenarioManager scenarioManager;

    [Header("Test Cases")]
    [Tooltip("กด context menu 'Load Default Test Cases' ถ้ายังว่าง")]
    public List<TestCase> testCases = new List<TestCase>();
    public int selectedTestCase = 0;

    [Header("Startup")]
    [Tooltip("เริ่มจำลองทันทีที่กด Play")]
    public bool autoStartOnPlay = true;
    public ControllerKind startController = ControllerKind.Adaptive;
    [Tooltip("seed สำหรับโหมด Poisson — เลขเดียวกันให้ผลเหมือนกัน")]
    public int randomSeed = 12345;

    [Header("Panel")]
    public bool showPanel = true;
    [Tooltip("ขยาย UI สำหรับจอความละเอียดสูง (1 = ปกติ, 1.5 = ใหญ่ขึ้น 50%)")]
    [Range(0.7f, 2.5f)] public float uiScale = 1f;
    [Tooltip("ซ่อน debug GUI เดิมของ ROI/Controller ตอนเปิด panel นี้")]
    public bool hideLegacyDebugGUI = true;

    // ===================== runtime state =====================
    private RunState state = RunState.Idle;
    private ControllerKind activeController;
    private float runElapsed = 0f;
    private float runLimit = 0f;          // 0 = ไม่จำกัด
    private float speedBeforePause = 1f;
    private float simSpeed = 1f;
    private string toast = "";
    private float toastUntil = 0f;

    private Rect windowRect = new Rect(12, 12, 350, 620);
    private Vector2 testScroll;
    private Vector2 mainScroll;
    private Vector2 laneScroll;

    // ===== เวลาระบายรถทั้งหมด (โหมด Scripted) =====
    private float clearanceTime = -1f;              // -1 = ยังไม่จบ
    private string clearanceLabel = "";
    private float lastFixedClearance = -1f;
    private float lastAdaptiveClearance = -1f;
    private string comparisonTestCase = "";
    private bool showTestList = true;
    private bool showLaneTable = true;

    // styles (สร้างครั้งเดียวตอน OnGUI ครั้งแรก)
    private bool stylesReady = false;
    private GUIStyle sTitle, sHeader, sSmall, sMono, sBtn, sBtnOn, sDesc;

    // ===================== lifecycle =====================

    void Awake()
    {
        if (config == null) config = FindFirstObjectByType<IntersectionConfig>();
        if (fixedController == null) fixedController = FindFirstObjectByType<FixedController>();
        if (adaptiveController == null) adaptiveController = FindFirstObjectByType<AdaptiveController>();
        if (dataLogger == null) dataLogger = FindFirstObjectByType<DataLogger>();
        if (scenarioManager == null) scenarioManager = FindFirstObjectByType<ScenarioManager>();

        if (testCases == null || testCases.Count == 0) testCases = TestCasePresets.BuildDefault();
        if (config != null) config.EnsureBuilt();
    }

    void Start()
    {
        if (hideLegacyDebugGUI) SetLegacyGUI(false);

        if (autoStartOnPlay) StartRun();
        else
        {
            StopRun();
            Toast("กด ▶ Start เพื่อเริ่ม");
        }
    }

    void Update()
    {
        if (state != RunState.Running) return;

        runElapsed += Time.deltaTime;

        // ===== หยุดเวลาเมื่อรถหมดถนน =====
        // เฉพาะโหมด Scripted ที่รู้จำนวนรถแน่นอน — ได้ตัวเลข "เวลาระบายรถทั้งหมด"
        // ที่เอาไปเทียบ Fixed vs Adaptive ได้ตรง ๆ เพราะรถชุดเดียวกันเป๊ะ
        if (config != null && clearanceTime < 0f
            && config.AllScheduledSpawned() && config.GetActiveCarCount() == 0)
        {
            clearanceTime = runElapsed;
            RecordClearance(clearanceTime);
            state = RunState.Finished;
            Time.timeScale = 0f;
            Toast($"รถหมดถนนแล้ว — ใช้เวลา {clearanceTime:F1} วินาที");
            return;
        }

        if (runLimit > 0f && runElapsed >= runLimit)
        {
            state = RunState.Finished;
            Time.timeScale = 0f;
            Toast($"ครบเวลา {runElapsed:F0} วินาที (ยังมีรถเหลือบนถนน)");
        }
    }

    void OnDisable()
    {
        // กัน timeScale ค้างเป็น 0 ตอนออกจาก Play mode
        Time.timeScale = 1f;
    }

    // ===================== run control =====================

    public TestCase CurrentTestCase
    {
        get
        {
            if (testCases == null || testCases.Count == 0) return null;
            int i = Mathf.Clamp(selectedTestCase, 0, testCases.Count - 1);
            return testCases[i];
        }
    }

    /// <summary>เริ่ม run ใหม่ด้วย test case + controller ที่เลือกอยู่</summary>
    public void StartRun()
    {
        if (config == null)
        {
            Toast("ไม่พบ IntersectionConfig");
            return;
        }
        if (scenarioManager != null && scenarioManager.IsBatchRunning)
        {
            Toast("batch run กำลังทำงาน — หยุดก่อน");
            return;
        }

        TestCase tc = CurrentTestCase;

        // ---- ตั้งค่ารูปแบบการปล่อยรถ ----
        config.ApplyTestCase(tc);

        // ---- ล้างถนน + ตั้ง seed ----
        int seed = (tc != null && tc.mode == ArrivalMode.Poisson) ? randomSeed : 20260820;
        Random.InitState(seed);
        config.ResetAllLanes(seed);

        // ---- รีเซ็ตสถิติ ----
        if (dataLogger != null)
        {
            dataLogger.ResetRun();
            dataLogger.scenarioName = tc != null ? tc.name : "Manual";
        }

        // ---- ตั้งระบบสัญญาณไฟ ----
        ApplyController(startController, silent: true);

        runElapsed = 0f;
        clearanceTime = -1f;
        runLimit = tc != null ? tc.suggestedDuration : 0f;
        state = RunState.Running;
        Time.timeScale = simSpeed;

        Toast(tc != null ? $"เริ่ม: {tc.name}" : "เริ่มจำลอง");
    }

    void RecordClearance(float t)
    {
        TestCase tc = CurrentTestCase;
        string tcName = tc != null ? tc.name : "-";

        // เปลี่ยนชุดทดสอบเมื่อไหร่ ล้างผลเทียบเก่าทิ้ง จะได้ไม่เทียบข้ามชุด
        if (comparisonTestCase != tcName)
        {
            comparisonTestCase = tcName;
            lastFixedClearance = -1f;
            lastAdaptiveClearance = -1f;
        }

        if (activeController == ControllerKind.Fixed) lastFixedClearance = t;
        else lastAdaptiveClearance = t;

        clearanceLabel = $"{tcName} · {activeController}";
        Debug.Log($"[ControlPanel] ระบายรถหมดใน {t:F2} วินาที  ({clearanceLabel})");
    }

    public void PauseRun()
    {
        if (state != RunState.Running) return;
        speedBeforePause = simSpeed;
        Time.timeScale = 0f;
        state = RunState.Paused;
        Toast("หยุดชั่วคราว");
    }

    public void ResumeRun()
    {
        if (state != RunState.Paused && state != RunState.Finished) return;
        simSpeed = Mathf.Max(0.1f, speedBeforePause);
        Time.timeScale = simSpeed;
        if (state == RunState.Finished) runLimit = 0f;   // เล่นต่อแบบไม่จำกัดเวลา
        state = RunState.Running;
        Toast("เล่นต่อ");
    }

    public void TogglePause()
    {
        if (state == RunState.Running) PauseRun();
        else if (state == RunState.Paused || state == RunState.Finished) ResumeRun();
        else StartRun();
    }

    /// <summary>หยุดสนิท ล้างรถออกจากถนน ไฟทุกดวงเป็นแดง</summary>
    public void StopRun()
    {
        if (fixedController != null) fixedController.ForceStop();
        if (adaptiveController != null) adaptiveController.ForceStop();
        if (config != null) config.ClearAllCars();

        state = RunState.Idle;
        runElapsed = 0f;
        clearanceTime = -1f;
        Time.timeScale = 0f;
        Toast("หยุดแล้ว — ถนนโล่ง");
    }

    public void RestartRun()
    {
        StopRun();
        StartRun();
    }

    /// <summary>สลับระบบสัญญาณไฟกลางคัน โดยไม่ล้างรถบนถนน</summary>
    public void ApplyController(ControllerKind kind, bool silent = false)
    {
        if (fixedController == null || adaptiveController == null) return;

        // ต้องหยุด coroutine ทั้งคู่ก่อนเสมอ ไม่งั้นไฟเขียวอาจค้างพร้อมกัน 2 กลุ่ม
        fixedController.ForceStop();
        adaptiveController.ForceStop();

        bool useFixed = (kind == ControllerKind.Fixed);
        fixedController.enabled = useFixed;
        adaptiveController.enabled = !useFixed;

        if (useFixed) fixedController.RestartCycle();
        else adaptiveController.RestartCycle();

        activeController = kind;
        startController = kind;

        if (dataLogger != null)
            dataLogger.controllerType = useFixed ? DataLogger.ControllerType.Fixed
                                                 : DataLogger.ControllerType.Adaptive;

        if (!silent) Toast($"สลับเป็น {kind}");
    }

    public void SetSpeed(float s)
    {
        simSpeed = Mathf.Clamp(s, 0.1f, 50f);
        if (state == RunState.Running) Time.timeScale = simSpeed;
    }

    void SetLegacyGUI(bool on)
    {
        if (config != null) config.SetRoiDebugGUI(on);
        if (adaptiveController != null) adaptiveController.showDebugGUI = on;
        if (fixedController != null) fixedController.showDebugGUI = on;
        if (dataLogger != null) dataLogger.showDebugGUI = on;
    }

    void Toast(string msg)
    {
        toast = msg;
        toastUntil = Time.unscaledTime + 3f;
    }

    [ContextMenu("Load Default Test Cases")]
    public void LoadDefaultTestCases()
    {
        testCases = TestCasePresets.BuildDefault();
        Debug.Log($"[ControlPanel] โหลด test case {testCases.Count} ชุด");
    }

    // ===================== GUI =====================

    void BuildStyles()
    {
        sTitle = new GUIStyle(GUI.skin.label)
        { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };

        sHeader = new GUIStyle(GUI.skin.label)
        { fontSize = 11, fontStyle = FontStyle.Bold };
        sHeader.normal.textColor = new Color(0.62f, 0.78f, 1f);

        sSmall = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };
        sSmall.normal.textColor = new Color(0.75f, 0.75f, 0.78f);

        sDesc = new GUIStyle(GUI.skin.label) { fontSize = 10, wordWrap = true };
        sDesc.normal.textColor = new Color(0.85f, 0.85f, 0.7f);

        sMono = new GUIStyle(GUI.skin.label) { fontSize = 11 };

        sBtn = new GUIStyle(GUI.skin.button) { fontSize = 11 };
        sBtnOn = new GUIStyle(GUI.skin.button) { fontSize = 11, fontStyle = FontStyle.Bold };
        sBtnOn.normal.textColor = Color.white;

        stylesReady = true;
    }

    void OnGUI()
    {
        HandleHotkeys();

        if (!showPanel)
        {
            GUI.Label(new Rect(10, 10, 300, 20), "กด H เพื่อเปิดแผงควบคุม");
            return;
        }

        if (!stylesReady) BuildStyles();

        Matrix4x4 old = GUI.matrix;
        if (!Mathf.Approximately(uiScale, 1f))
            GUIUtility.ScaleAroundPivot(new Vector2(uiScale, uiScale), Vector2.zero);

        // ไม่ให้หน้าต่างสูงเกินจอ (คิดรวม uiScale ด้วย)
        float maxH = Screen.height / Mathf.Max(0.1f, uiScale) - 24f;
        windowRect.height = Mathf.Min(windowRect.height, maxH);
        if (windowRect.height < 260f) windowRect.height = Mathf.Min(260f, maxH);

        windowRect = GUI.Window(9317, windowRect, DrawWindow, "");

        GUI.matrix = old;
    }

    void HandleHotkeys()
    {
        // ใช้ Event ของ IMGUI แทน Input class เพราะทำงานได้ทั้ง Input System เก่าและใหม่
        Event e = Event.current;
        if (e == null || e.type != EventType.KeyDown) return;

        switch (e.keyCode)
        {
            case KeyCode.Space: TogglePause(); e.Use(); break;
            case KeyCode.R:     RestartRun();  e.Use(); break;
            case KeyCode.H:     showPanel = !showPanel; e.Use(); break;
            case KeyCode.F:     ApplyController(ControllerKind.Fixed);    e.Use(); break;
            case KeyCode.G:     ApplyController(ControllerKind.Adaptive); e.Use(); break;
            case KeyCode.Alpha1: SetSpeed(1f);  e.Use(); break;
            case KeyCode.Alpha2: SetSpeed(2f);  e.Use(); break;
            case KeyCode.Alpha3: SetSpeed(5f);  e.Use(); break;
            case KeyCode.Alpha4: SetSpeed(10f); e.Use(); break;
            case KeyCode.Alpha5: SetSpeed(20f); e.Use(); break;
        }
    }

    void DrawWindow(int id)
    {
        GUILayout.Space(2);

        // ---------- header (อยู่นอก scroll ค้างไว้ด้านบนเสมอ) ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label("ABAC TRAFFIC SIM — CONTROL", sTitle);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("×", sBtn, GUILayout.Width(24))) showPanel = false;
        GUILayout.EndHorizontal();

        DrawStatusLine();
        Divider();

        // ---------- เนื้อหาที่เหลือเลื่อนขึ้นลงได้ ----------
        mainScroll = GUILayout.BeginScrollView(mainScroll);

        // ---------- run control ----------
        GUILayout.Label("การควบคุม", sHeader);
        GUILayout.BeginHorizontal();
        if (Btn(state == RunState.Running ? "⏸  หยุดชั่วคราว" : "▶  เล่น", false))
            TogglePause();
        if (Btn("⏹  หยุด", false, 70)) StopRun();
        if (Btn("↻  เริ่มใหม่", false, 82)) RestartRun();
        GUILayout.EndHorizontal();

        // ---------- speed ----------
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"ความเร็ว  {simSpeed:0.#}x", sMono, GUILayout.Width(105));
        float ns = GUILayout.HorizontalSlider(simSpeed, 0.25f, 20f);
        if (!Mathf.Approximately(ns, simSpeed)) SetSpeed(ns);
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        foreach (float sp in new[] { 0.5f, 1f, 2f, 5f, 10f, 20f })
        {
            if (Btn($"{sp:0.#}x", Mathf.Approximately(simSpeed, sp), 44)) SetSpeed(sp);
        }
        GUILayout.EndHorizontal();

        Divider();

        // ---------- controller ----------
        GUILayout.Label("ระบบสัญญาณไฟ", sHeader);
        GUILayout.BeginHorizontal();
        if (Btn("Fixed  (F)", activeController == ControllerKind.Fixed))
            ApplyController(ControllerKind.Fixed);
        if (Btn("Adaptive  (G)", activeController == ControllerKind.Adaptive))
            ApplyController(ControllerKind.Adaptive);
        GUILayout.EndHorizontal();

        if (activeController == ControllerKind.Fixed && fixedController != null)
            GUILayout.Label($"วนตายตัว · รอบละ {fixedController.GetCycleLength():F0} วินาที", sSmall);
        else if (adaptiveController != null)
            GUILayout.Label($"ปรับตามดีมานด์ · เขียว {adaptiveController.gMin:F0}-{adaptiveController.gMax:F0} วินาที" +
                            (adaptiveController.enableConcurrentPhases ? " · รวมเลนได้" : " · ทีละเลน"), sSmall);

        Divider();

        // ---------- test case ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label("ชุดทดสอบ", sHeader);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(showTestList ? "ย่อ" : "ขยาย", sBtn, GUILayout.Width(46)))
            showTestList = !showTestList;
        GUILayout.EndHorizontal();

        if (testCases == null || testCases.Count == 0)
        {
            GUILayout.Label("ยังไม่มี test case", sSmall);
            if (Btn("โหลดชุดมาตรฐาน", false)) LoadDefaultTestCases();
        }
        else
        {
            TestCase tc = CurrentTestCase;

            if (showTestList)
            {
                testScroll = GUILayout.BeginScrollView(testScroll, GUILayout.Height(122));
                for (int i = 0; i < testCases.Count; i++)
                {
                    if (testCases[i] == null) continue;
                    if (Btn(testCases[i].name, i == selectedTestCase))
                    {
                        selectedTestCase = i;
                        Toast("เลือกแล้ว — กด ↻ เริ่มใหม่ เพื่อใช้งาน");
                    }
                }
                GUILayout.EndScrollView();
            }

            if (tc != null)
            {
                GUILayout.Label(tc.ShortSummary(), sSmall);
                GUILayout.Label(tc.description, sDesc);
                if (tc.mode == ArrivalMode.Poisson)
                {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label("seed", sMono, GUILayout.Width(34));
                    string sv = GUILayout.TextField(randomSeed.ToString(), GUILayout.Width(72));
                    int parsed;
                    if (int.TryParse(sv, out parsed)) randomSeed = parsed;
                    if (Btn("สุ่มใหม่", false, 60)) randomSeed = Random.Range(1, 999999);
                    GUILayout.EndHorizontal();
                }
                if (Btn("▶  ใช้ชุดนี้แล้วเริ่มใหม่", false)) RestartRun();
            }
        }

        Divider();

        // ---------- live lane table ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label("สถานะแต่ละเลน", sHeader);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(showLaneTable ? "ย่อ" : "ขยาย", sBtn, GUILayout.Width(46)))
            showLaneTable = !showLaneTable;
        GUILayout.EndHorizontal();

        if (showLaneTable) DrawLaneTable();

        Divider();
        DrawClearance();
        Divider();
        DrawStats();
        Divider();

        // ---------- actions ----------
        GUILayout.BeginHorizontal();
        if (Btn("บันทึก CSV", false)) ExportNow();
        if (Btn("ล้างสถิติ", false)) { if (dataLogger != null) { dataLogger.ResetRun(); Toast("ล้างสถิติแล้ว"); } }
        GUILayout.EndHorizontal();

        if (scenarioManager != null)
        {
            GUILayout.BeginHorizontal();
            if (scenarioManager.IsBatchRunning)
            {
                if (Btn("■ หยุด batch run", true)) scenarioManager.StopBatchRun();
            }
            else
            {
                if (Btn("เริ่ม batch run 270 รอบ", false))
                {
                    Time.timeScale = 1f;
                    state = RunState.Idle;
                    scenarioManager.StartBatchRun();
                    Toast("เริ่ม batch run");
                }
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(3);
        GUILayout.Label("คีย์ลัด · Space เล่น/หยุด · R เริ่มใหม่ · F/G สลับระบบ · 1-5 ความเร็ว · H ซ่อนแผง", sSmall);

        GUILayout.EndScrollView();

        // ---------- toast ----------
        if (Time.unscaledTime < toastUntil && !string.IsNullOrEmpty(toast))
        {
            Color oc = GUI.color;
            GUI.color = new Color(1f, 0.9f, 0.4f);
            GUILayout.Label("► " + toast, sSmall);
            GUI.color = oc;
        }

        GUI.DragWindow(new Rect(0, 0, 10000, 22));
    }

    void DrawStatusLine()
    {
        string label;
        Color c;
        switch (state)
        {
            case RunState.Running:  label = "กำลังจำลอง"; c = new Color(.45f, .85f, .45f); break;
            case RunState.Paused:   label = "หยุดชั่วคราว"; c = new Color(1f, .8f, .3f); break;
            case RunState.Finished: label = "จบ run แล้ว";  c = new Color(.5f, .75f, 1f); break;
            default:                label = "หยุด";        c = new Color(.9f, .45f, .45f); break;
        }

        GUILayout.BeginHorizontal();
        Color oc = GUI.color;
        GUI.color = c;
        GUILayout.Label("●", sMono, GUILayout.Width(14));
        GUI.color = oc;

        GUILayout.Label(label, sMono, GUILayout.Width(88));
        GUILayout.FlexibleSpace();

        string t = runLimit > 0f ? $"{runElapsed:F0} / {runLimit:F0} วิ" : $"{runElapsed:F0} วิ";
        GUILayout.Label(t, sMono);
        GUILayout.EndHorizontal();
    }

    void DrawLaneTable()
    {
        if (config == null) return;

        GUILayout.BeginHorizontal();
        GUILayout.Label("เลน", sSmall, GUILayout.Width(38));
        GUILayout.Label("ไฟ", sSmall, GUILayout.Width(52));
        GUILayout.Label("คิว", sSmall, GUILayout.Width(38));
        GUILayout.Label("รอนานสุด", sSmall);
        GUILayout.EndHorizontal();

        // สูงพอสำหรับ 6 เลน ถ้าเพิ่มเลนทีหลังจะเลื่อนดูได้
        laneScroll = GUILayout.BeginScrollView(laneScroll, GUILayout.Height(112));

        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null) continue;

            int queue = lane.roi != null ? lane.roi.GetVehicleCount() : 0;
            float wait = lane.roi != null ? lane.roi.GetLongestWaitTime() : 0f;

            GUILayout.BeginHorizontal();
            GUILayout.Label(lane.laneName, sMono, GUILayout.Width(38));

            Color oc = GUI.color;
            if (!lane.isSignalized)
            {
                GUI.color = new Color(.6f, .6f, .65f);
                GUILayout.Label("ฟรี", sMono, GUILayout.Width(52));
            }
            else
            {
                TrafficLight.LightState st = lane.trafficLight != null
                    ? lane.trafficLight.currentState : TrafficLight.LightState.Red;
                GUI.color = st == TrafficLight.LightState.Green ? new Color(.35f, .9f, .35f)
                          : st == TrafficLight.LightState.Yellow ? new Color(1f, .85f, .2f)
                          : new Color(.95f, .4f, .4f);
                GUILayout.Label("███", sMono, GUILayout.Width(52));
            }
            GUI.color = oc;

            GUILayout.Label(queue.ToString(), sMono, GUILayout.Width(38));

            // ไฮไลต์แดงถ้ารอนานผิดปกติ
            if (adaptiveController != null && wait > adaptiveController.maxWaitTime * 0.8f)
                GUI.color = new Color(1f, .5f, .4f);
            GUILayout.Label($"{wait:F1} วิ", sMono);
            GUI.color = oc;

            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();
    }

    void DrawClearance()
    {
        GUILayout.Label("เวลาระบายรถทั้งหมด", sHeader);

        if (clearanceTime >= 0f)
        {
            Color oc = GUI.color;
            GUI.color = new Color(.5f, 1f, .6f);
            GUILayout.Label($"✓  {clearanceTime:F2} วินาที   ({clearanceLabel})", sMono);
            GUI.color = oc;
        }
        else if (config != null && config.AllScheduledSpawned())
        {
            GUILayout.Label($"ปล่อยรถครบแล้ว · เหลือบนถนน {config.GetActiveCarCount()} คัน", sSmall);
        }
        else
        {
            GUILayout.Label("ยังปล่อยรถไม่ครบ (โหมด Poisson จะไม่มีวันจบ)", sSmall);
        }

        // ---- ตารางเทียบ Fixed vs Adaptive ของชุดทดสอบเดียวกัน ----
        if (lastFixedClearance >= 0f || lastAdaptiveClearance >= 0f)
        {
            GUILayout.Space(2);
            GUILayout.Label($"เทียบชุด: {comparisonTestCase}", sSmall);

            Stat("  Fixed",    lastFixedClearance    >= 0f ? $"{lastFixedClearance:F2} วิ"    : "ยังไม่ได้รัน");
            Stat("  Adaptive", lastAdaptiveClearance >= 0f ? $"{lastAdaptiveClearance:F2} วิ" : "ยังไม่ได้รัน");

            if (lastFixedClearance > 0f && lastAdaptiveClearance > 0f)
            {
                float diff = lastFixedClearance - lastAdaptiveClearance;
                float pct = diff / lastFixedClearance * 100f;
                Color oc = GUI.color;
                GUI.color = diff > 0f ? new Color(.5f, 1f, .6f) : new Color(1f, .6f, .5f);
                string who = diff > 0f ? "Adaptive เร็วกว่า" : "Fixed เร็วกว่า";
                GUILayout.Label($"  → {who} {Mathf.Abs(diff):F2} วิ ({Mathf.Abs(pct):F1}%)", sMono);
                GUI.color = oc;
            }
            else
            {
                GUILayout.Label("  สลับระบบแล้วกด ↻ เพื่อรันอีกฝั่ง", sSmall);
            }
        }
    }

    void DrawStats()
    {
        GUILayout.Label("สถิติ run นี้", sHeader);
        if (dataLogger == null)
        {
            GUILayout.Label("ไม่พบ DataLogger", sSmall);
            return;
        }

        int spawned = config != null ? config.GetTotalSpawned() : 0;
        int cleared = dataLogger.GetVehicleCount();

        Stat("รถที่ปล่อยแล้ว", spawned.ToString());
        Stat("รถที่ผ่านแยกไป", cleared.ToString());
        Stat("เวลารอเฉลี่ย", $"{dataLogger.GetAverageWaitTime():F2} วิ");
        Stat("เวลารอสูงสุด", $"{dataLogger.GetMaxWaitTime():F2} วิ");
        Stat("กลไกกันอดทำงาน", $"{dataLogger.GetStarvationCount()} ครั้ง");
    }

    void Stat(string k, string v)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(k, sMono, GUILayout.Width(150));
        GUILayout.FlexibleSpace();
        GUILayout.Label(v, sMono);
        GUILayout.EndHorizontal();
    }

    void ExportNow()
    {
        if (dataLogger == null) { Toast("ไม่พบ DataLogger"); return; }
        if (config != null) dataLogger.SetTotalSpawned(config.GetTotalSpawned());
        dataLogger.ExportDetailedCSV();
        dataLogger.ExportSummaryCSV();
        Toast("บันทึกลง SimulationResults แล้ว");
    }

    // ---------- widgets ----------

    bool Btn(string label, bool active, float width = 0f)
    {
        Color ob = GUI.backgroundColor;
        if (active) GUI.backgroundColor = new Color(.35f, .65f, 1f);

        bool clicked = width > 0f
            ? GUILayout.Button(label, active ? sBtnOn : sBtn, GUILayout.Width(width))
            : GUILayout.Button(label, active ? sBtnOn : sBtn);

        GUI.backgroundColor = ob;
        return clicked;
    }

    void Divider()
    {
        GUILayout.Space(5);
        Color oc = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.18f);
        GUILayout.Box("", GUILayout.Height(1));
        GUI.color = oc;
        GUILayout.Space(3);
    }
}
