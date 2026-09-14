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

    [Header("References (leave empty — found automatically in the scene)")]
    public IntersectionConfig config;
    public FixedController fixedController;
    public AdaptiveController adaptiveController;
    public DataLogger dataLogger;
    public ScenarioManager scenarioManager;

    [Header("Test Cases")]
    [Tooltip("Use the context menu 'Load Default Test Cases' if this is empty")]
    public List<TestCase> testCases = new List<TestCase>();
    public int selectedTestCase = 0;

    [Header("Startup")]
    [Tooltip("Start the run as soon as Play is pressed")]
    public bool autoStartOnPlay = true;
    public ControllerKind startController = ControllerKind.Adaptive;
    [Tooltip("Seed for Poisson mode — the same number reproduces the same traffic")]
    public int randomSeed = 12345;

    [Header("Panel")]
    public bool showPanel = true;
    [Tooltip("UI scale for high-DPI displays (1 = normal, 1.5 = 50% larger)")]
    [Range(0.7f, 2.5f)] public float uiScale = 1f;
    [Tooltip("Hide the per-lane and controller HUDs while this panel is open")]
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
            Toast("Press \u25B6 Play to start");
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
            Toast($"Road cleared in {clearanceTime:F1} s");
            return;
        }

        if (runLimit > 0f && runElapsed >= runLimit)
        {
            state = RunState.Finished;
            Time.timeScale = 0f;
            Toast($"Time limit reached at {runElapsed:F0} s (vehicles still on the road)");
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
            Toast("IntersectionConfig not found");
            return;
        }
        if (scenarioManager != null && scenarioManager.IsBatchRunning)
        {
            Toast("A batch run is in progress — stop it first");
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

        Toast(tc != null ? $"Running: {tc.name}" : "Run started");
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
        Debug.Log($"[ControlPanel] Road cleared in {t:F2} s  ({clearanceLabel})");
    }

    public void PauseRun()
    {
        if (state != RunState.Running) return;
        speedBeforePause = simSpeed;
        Time.timeScale = 0f;
        state = RunState.Paused;
        Toast("Paused");
    }

    public void ResumeRun()
    {
        if (state != RunState.Paused && state != RunState.Finished) return;
        simSpeed = Mathf.Max(0.1f, speedBeforePause);
        Time.timeScale = simSpeed;
        if (state == RunState.Finished) runLimit = 0f;   // เล่นต่อแบบไม่จำกัดเวลา
        state = RunState.Running;
        Toast("Resumed");
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
        Toast("Stopped — road cleared");
    }

    public void RestartRun()
    {
        StopRun();
        StartRun();
    }

    /// <summary>
    /// Run a test case that was built at runtime (see CustomTestCase).
    ///
    /// The case is appended to the list and selected, so it goes through exactly the same
    /// path as the built-in presets — no separate code path to keep in sync, and the case
    /// stays in the list afterwards so it can be re-run or compared against.
    /// </summary>
    public void RunCustomCase(TestCase tc)
    {
        if (tc == null) return;

        // Replace an earlier custom case with the same name rather than piling up duplicates
        int existing = testCases.FindIndex(t => t != null && t.name == tc.name);
        if (existing >= 0) testCases[existing] = tc;
        else testCases.Add(tc);

        selectedTestCase = existing >= 0 ? existing : testCases.Count - 1;
        RestartRun();
        Toast($"Running: {tc.name}");
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

        if (!silent) Toast($"Switched to {kind}");
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
        Debug.Log($"[ControlPanel] Loaded {testCases.Count} test cases");
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

        // ธีมกลาง: ครอบไว้ทั้งก้อน แล้ว GUI.Box/Label/Button ทุกบรรทัดในแผงนี้
        // จะได้หน้าตาชุดเดียวกับแผงอื่นทันที โดยไม่ต้องแก้ทีละจุด (ดู UITheme.Begin)
        UITheme.Begin();

        if (!showPanel)
        {
            Rect hint = new Rect(12f, 12f, 216f, 32f);
            GUI.Box(hint, GUIContent.none);
            GUI.Label(new Rect(hint.x + 12f, hint.y + 8f, hint.width - 24f, 16f),
                      "Press  H  to open the control panel", UITheme.Body);
            UITheme.End();
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
        UITheme.End();
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
        GUILayout.Label("RUN CONTROL", sHeader);
        GUILayout.BeginHorizontal();
        if (Btn(state == RunState.Running ? "\u23F8  Pause" : "\u25B6  Play", false))
            TogglePause();
        if (Btn("\u23F9  Stop", false, 70)) StopRun();
        if (Btn("\u21BB  Restart", false, 88)) RestartRun();
        GUILayout.EndHorizontal();

        // ---------- speed ----------
        GUILayout.Space(4);
        GUILayout.BeginHorizontal();
        GUILayout.Label($"Speed  {simSpeed:0.#}x", sMono, GUILayout.Width(105));
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
        GUILayout.Label("SIGNAL CONTROLLER", sHeader);
        GUILayout.BeginHorizontal();
        if (Btn("Fixed  (F)", activeController == ControllerKind.Fixed))
            ApplyController(ControllerKind.Fixed);
        if (Btn("Adaptive  (G)", activeController == ControllerKind.Adaptive))
            ApplyController(ControllerKind.Adaptive);
        GUILayout.EndHorizontal();

        if (activeController == ControllerKind.Fixed && fixedController != null)
            GUILayout.Label($"Fixed-time \u00B7 {fixedController.GetCycleLength():F0} s cycle", sSmall);
        else if (adaptiveController != null)
            GUILayout.Label($"Demand-responsive \u00B7 green {adaptiveController.gMin:F0}-{adaptiveController.gMax:F0} s" +
                            (adaptiveController.enableConcurrentPhases ? " \u00B7 concurrent phases" : " \u00B7 one lane at a time"), sSmall);

        // ปุ่มเปิด/ปิดแผงขั้นตอนการคิดของอัลกอริทึม (แผงแยกด้านขวา)
        if (adaptiveController != null)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("Decision trace  (D)", sSmall);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button(adaptiveController.showDecisionPanel ? "Hide" : "Show", sBtn, GUILayout.Width(52)))
                adaptiveController.showDecisionPanel = !adaptiveController.showDecisionPanel;
            GUILayout.EndHorizontal();
        }

        Divider();

        // ---------- test case ----------
        // ⚠️ ระหว่าง batch ไม่วาดบล็อกนี้เลย
        //    Unity crash เมื่อ 10 ก.ย. 15:51 เกิดใน IMGUI layout ของหน้าต่างนี้ตอน Play:
        //      GUI.CallWindowDelegate -> GUILayoutUtility.Layout -> GUIScrollGroup.CalcWidth
        //        -> GUIWordWrapSizer.CalcWidth -> GUIStyle.CalcMinMaxWidth  [native crash]
        //    = scroll view ซ้อน scroll view ที่ข้างในมี label แบบ wordWrap (tc.description, sDesc)
        //    ยังพิสูจน์ไม่ได้ว่านี่คือต้นเหตุแน่นอน แต่ batch รันยาว ~23 ชม.แบบไม่มีคนเฝ้า
        //    จึงไม่ควรวาดโครงสร้างที่อยู่ใน stack ของ crash ทิ้งไว้ทั้งคืน
        //    (ไม่กระทบผลการทดลอง — เป็น GUI ล้วน BatchRunPanel ยังโชว์ความคืบหน้าตามปกติ)
        bool batchRunning = scenarioManager != null && scenarioManager.IsBatchRunning;

        if (batchRunning)
        {
            GUILayout.Label($"BATCH RUNNING — {scenarioManager.CompletedRuns}/{scenarioManager.TotalRuns}", sHeader);
            GUILayout.Label("test-case list and lane table hidden while the batch runs", sMono);
            GUILayout.EndScrollView();                       // ปิด mainScroll ให้ตรงกับทางออกปกติ
            GUI.DragWindow(new Rect(0, 0, 10000, 22));
            return;
        }

        GUILayout.BeginHorizontal();
        GUILayout.Label("TEST CASES", sHeader);
        GUILayout.FlexibleSpace();

        // ⚠️ ลิสต์นี้ถูก serialize ไว้ในซีนด้วย และ Awake จะสร้างจากโค้ดก็ต่อเมื่อลิสต์ว่างเท่านั้น
        //    เคสใหม่ที่เพิ่มใน TestCasePresets จึงไม่โผล่เองจนกว่าจะกดปุ่มนี้
        //    (กับดักเดียวกับค่าพารามิเตอร์ที่ซีนทับซอร์ส — ดู ABAC ▸ Verify Scene Against Code)
        if (GUILayout.Button("Reload", sBtn, GUILayout.Width(58)))
        {
            LoadDefaultTestCases();
            selectedTestCase = 0;
            Toast($"โหลดจากโค้ดใหม่ {testCases.Count} เคส");
        }
        if (GUILayout.Button(showTestList ? "Hide" : "Show", sBtn, GUILayout.Width(52)))
            showTestList = !showTestList;
        GUILayout.EndHorizontal();

        if (testCases == null || testCases.Count == 0)
        {
            GUILayout.Label("No test cases loaded", sSmall);
            if (Btn("Load default set", false)) LoadDefaultTestCases();
        }
        else
        {
            TestCase tc = CurrentTestCase;

            if (showTestList)
            {
                testScroll = GUILayout.BeginScrollView(testScroll, GUILayout.Height(122));
                int deleteAt = -1;
                for (int i = 0; i < testCases.Count; i++)
                {
                    if (testCases[i] == null) continue;

                    // เคสที่ผู้ใช้สร้างเองมีปุ่มลบ · T1-T13 ไม่มี เพราะรายงานกับเทสอ้างถึงมัน
                    bool own = testCases[i].userCreated;
                    if (own) GUILayout.BeginHorizontal();

                    if (Btn(testCases[i].name, i == selectedTestCase))
                    {
                        selectedTestCase = i;
                        Toast("Selected — press Restart to apply");
                    }

                    if (own)
                    {
                        if (GUILayout.Button("×", sBtn, GUILayout.Width(22))) deleteAt = i;
                        GUILayout.EndHorizontal();
                    }
                }

                if (deleteAt >= 0)
                {
                    string gone = testCases[deleteAt].name;
                    testCases.RemoveAt(deleteAt);
                    if (selectedTestCase >= testCases.Count) selectedTestCase = testCases.Count - 1;
                    if (selectedTestCase < 0) selectedTestCase = 0;
                    Toast($"ลบ \"{gone}\" แล้ว");
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
                    if (Btn("Reroll", false, 62)) randomSeed = Random.Range(1, 999999);
                    GUILayout.EndHorizontal();
                }
                if (Btn("\u25B6  Apply and restart", false)) RestartRun();
            }
        }

        Divider();

        // ---------- live lane table ----------
        GUILayout.BeginHorizontal();
        GUILayout.Label("LANE STATUS", sHeader);
        GUILayout.FlexibleSpace();
        if (GUILayout.Button(showLaneTable ? "Hide" : "Show", sBtn, GUILayout.Width(52)))
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
        if (Btn("Export CSV", false)) ExportNow();
        if (Btn("Clear stats", false)) { if (dataLogger != null) { dataLogger.ResetRun(); Toast("Statistics cleared"); } }
        GUILayout.EndHorizontal();

        if (scenarioManager != null)
        {
            GUILayout.BeginHorizontal();
            if (scenarioManager.IsBatchRunning)
            {
                if (Btn("\u25A0  Stop batch run", true)) scenarioManager.StopBatchRun();
            }
            else
            {
                if (Btn($"Start batch run ({(scenarioManager != null ? scenarioManager.scenarios.Count * scenarioManager.repetitionsPerScenario * 2 : 0)} runs)", false))
                {
                    Time.timeScale = 1f;
                    state = RunState.Idle;
                    scenarioManager.StartBatchRun();
                    Toast("Batch run started");
                }
            }
            GUILayout.EndHorizontal();
        }

        GUILayout.Space(3);
        GUILayout.Label("Keys \u00B7 Space play/pause \u00B7 R restart \u00B7 F/G controller \u00B7 1-5 speed \u00B7 H panel \u00B7 [ ] camera", sSmall);

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
            case RunState.Running:  label = "RUNNING"; c = new Color(.45f, .85f, .45f); break;
            case RunState.Paused:   label = "PAUSED";  c = new Color(1f, .8f, .3f); break;
            case RunState.Finished: label = "FINISHED"; c = new Color(.5f, .75f, 1f); break;
            default:                label = "IDLE";     c = new Color(.9f, .45f, .45f); break;
        }

        GUILayout.BeginHorizontal();
        Color oc = GUI.color;
        GUI.color = c;
        GUILayout.Label("●", sMono, GUILayout.Width(14));
        GUI.color = oc;

        GUILayout.Label(label, sMono, GUILayout.Width(88));
        GUILayout.FlexibleSpace();

        string t = runLimit > 0f ? $"{runElapsed:F0} / {runLimit:F0} s" : $"{runElapsed:F0} s";
        GUILayout.Label(t, sMono);
        GUILayout.EndHorizontal();
    }

    // ความกว้างคอลัมน์ของตารางเลน — หัวตารางกับแถวข้อมูลต้องใช้ชุดเดียวกัน
    const float W_LANE = 32f, W_SIGNAL = 78f, W_QUEUE = 32f, W_OLDEST = 56f;

    void DrawLaneTable()
    {
        if (config == null) return;

        GUILayout.BeginHorizontal();
        // ⚠️ ความกว้างสี่ค่านี้ต้องตรงกับแถวข้อมูลด้านล่างเป๊ะ ไม่งั้นคอลัมน์จะเลื่อนจากหัวตาราง
        //    รวม 198 จากพื้นที่ใน scroll view ~318 (แผงกว้าง 350 หัก padding กับ scrollbar)
        //    คอลัมน์สุดท้ายกินที่เหลือ ~120 พอสำหรับ "42.8s H_Bx" โดยไม่ตัดบรรทัด
        GUILayout.Label("Lane", sSmall, GUILayout.Width(W_LANE));
        GUILayout.Label("Signal", sSmall, GUILayout.Width(W_SIGNAL));
        GUILayout.Label("Queue", sSmall, GUILayout.Width(W_QUEUE));
        GUILayout.Label("Oldest car", sSmall, GUILayout.Width(W_OLDEST));
        GUILayout.Label("Queue clock", sSmall);
        GUILayout.EndHorizontal();

        // SODA รันอยู่จริงไหม — ใช้ทั้งคอลัมน์นับถอยหลังและคอลัมน์นาฬิกาคิว
        bool sodaLive = adaptiveController != null && adaptiveController.enabled;

        // สูงพอสำหรับ 6 เลน ถ้าเพิ่มเลนทีหลังจะเลื่อนดูได้
        laneScroll = GUILayout.BeginScrollView(laneScroll, GUILayout.Height(112));

        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null) continue;

            int queue = lane.roi != null ? lane.roi.GetVehicleCount() : 0;
            float wait = lane.roi != null ? lane.roi.GetLongestWaitTime() : 0f;

            GUILayout.BeginHorizontal();
            GUILayout.Label(lane.laneName, sMono, GUILayout.Width(W_LANE));

            Color oc = GUI.color;
            if (!lane.isSignalized)
            {
                GUI.color = new Color(.6f, .6f, .65f);
                GUILayout.Label("free", sMono, GUILayout.Width(W_SIGNAL));
            }
            else
            {
                TrafficLight.LightState st = lane.trafficLight != null
                    ? lane.trafficLight.currentState : TrafficLight.LightState.Red;
                GUI.color = st == TrafficLight.LightState.Green ? new Color(.35f, .9f, .35f)
                          : st == TrafficLight.LightState.Yellow ? new Color(1f, .85f, .2f)
                          : new Color(.95f, .4f, .4f);

                // นับถอยหลังของไฟเขียว — ใช้ GreenTimeRemaining ไม่ใช่ PhaseTimeRemaining
                // เพราะตัวหลังถูกเขียนทับด้วยเวลาเหลือง/แดงของ clearance ทำให้หัวที่เขียวต่อเนื่อง
                // ข้ามเฟสแสดงเวลา clearance ของหัวอื่นว่าเป็นเวลาเขียวของตัวเอง
                string left = "";
                if (sodaLive && st == TrafficLight.LightState.Green)
                {
                    float r = adaptiveController.GreenTimeRemaining;
                    if (r > 0.05f) left = $" {r:F1}s";
                }
                GUILayout.Label("███" + left, sMono, GUILayout.Width(W_SIGNAL));
            }
            GUI.color = oc;

            GUILayout.Label(queue.ToString(), sMono, GUILayout.Width(W_QUEUE));

            // ---- รถคันที่เก่าที่สุดในเลน (ground truth ของซิม) ----
            // ⚠️ ไม่ใช่ตัวเลขที่ controller ใช้ตัดสินใจ ดูคอลัมน์ถัดไป
            if (adaptiveController != null && wait > adaptiveController.maxWaitTime * 0.8f)
                GUI.color = new Color(1f, .5f, .4f);
            GUILayout.Label($"{wait:F1} s", sMono, GUILayout.Width(W_OLDEST));
            GUI.color = oc;

            // ---- นาฬิกาคิวของหัวไฟ = ตัวเลขจริงที่ Q1/Q2 ใช้ ----
            // ต่างจากคอลัมน์ซ้ายเพราะเป็นของ "เลน" ไม่ใช่ของ "รถ" และรีเซ็ตทุกครั้งที่ได้เสิร์ฟ
            // เลนที่ไม่เคยว่างจะสะสมนาฬิกาเก่าไว้ ทั้งที่รถในเลนอาจเพิ่งมาถึง (SODA_BASELINE §8.11 note)
            // sodaLive: ถ้า SODA ไม่ได้รันอยู่ นาฬิกาจะค้างที่ค่าเดิม (ForceStop ฆ่า SnapshotLoop
            // แต่ไม่ล้าง queueStart) แล้ว Time.time - queueStart จะไต่ขึ้นไม่หยุด จนขึ้นแดงว่าอดตาย
            // ทั้งที่ตัวคุมไม่ได้ทำงาน — จึงต้องไม่แสดงเลย
            AdaptiveController.SignalHead head = sodaLive ? HeadForLane(lane) : null;
            if (!lane.isSignalized || head == null)
            {
                GUI.color = new Color(.6f, .6f, .65f);
                GUILayout.Label("—", sMono);
            }
            else if (float.IsInfinity(head.queueStart))
            {
                GUI.color = new Color(.6f, .6f, .65f);
                GUILayout.Label("empty", sMono);
            }
            else
            {
                float cw = Time.time - head.queueStart;
                if (cw >= adaptiveController.maxWaitTime * adaptiveController.starvationSelectThreshold)
                    GUI.color = new Color(1f, .5f, .4f);       // เข้าเกณฑ์ Q1 แล้ว
                GUILayout.Label($"{cw:F1}s {head.name}", sMono);
            }
            GUI.color = oc;

            GUILayout.EndHorizontal();
        }

        GUILayout.EndScrollView();

        GUILayout.Label("Oldest car = รถคันเก่าสุดในเลน (ground truth)  ·  " +
                        "Queue clock = นาฬิกาของหัวไฟที่ Q1/Q2 ใช้จริง", sSmall);
    }

    /// <summary>
    /// หา SignalHead ที่คุมเลนนี้ โดยจับคู่จาก ROI
    /// A2 กับ Ax ใช้ ROI ร่วมกัน จึงได้ H_A ทั้งคู่ — Ax ถูกกรองออกด้วย isSignalized ก่อนหน้าแล้ว
    /// </summary>
    AdaptiveController.SignalHead HeadForLane(ApproachLane lane)
    {
        if (adaptiveController == null || lane == null || lane.roi == null) return null;
        foreach (AdaptiveController.SignalHead h in adaptiveController.Heads)
            if (h != null && h.rois != null && h.rois.Contains(lane.roi)) return h;
        return null;
    }

    void DrawClearance()
    {
        GUILayout.Label("TOTAL CLEARANCE TIME", sHeader);

        if (clearanceTime >= 0f)
        {
            Color oc = GUI.color;
            GUI.color = new Color(.5f, 1f, .6f);
            GUILayout.Label($"\u2713  {clearanceTime:F2} s   ({clearanceLabel})", sMono);
            GUI.color = oc;
        }
        else if (config != null && config.AllScheduledSpawned())
        {
            GUILayout.Label($"All vehicles released \u00B7 {config.GetActiveCarCount()} still on the road", sSmall);
        }
        else
        {
            GUILayout.Label("Still releasing vehicles (Poisson mode never ends)", sSmall);
        }

        // ---- ตารางเทียบ Fixed vs Adaptive ของชุดทดสอบเดียวกัน ----
        if (lastFixedClearance >= 0f || lastAdaptiveClearance >= 0f)
        {
            GUILayout.Space(2);
            GUILayout.Label($"Comparing: {comparisonTestCase}", sSmall);

            Stat("  Fixed",    lastFixedClearance    >= 0f ? $"{lastFixedClearance:F2} s"    : "not run yet");
            Stat("  Adaptive", lastAdaptiveClearance >= 0f ? $"{lastAdaptiveClearance:F2} s" : "not run yet");

            if (lastFixedClearance > 0f && lastAdaptiveClearance > 0f)
            {
                float diff = lastFixedClearance - lastAdaptiveClearance;
                float pct = diff / lastFixedClearance * 100f;
                Color oc = GUI.color;
                GUI.color = diff > 0f ? new Color(.5f, 1f, .6f) : new Color(1f, .6f, .5f);
                string who = diff > 0f ? "Adaptive faster by" : "Fixed faster by";
                GUILayout.Label($"  \u2192 {who} {Mathf.Abs(diff):F2} s ({Mathf.Abs(pct):F1}%)", sMono);
                GUI.color = oc;
            }
            else
            {
                GUILayout.Label("  Switch controller and restart to run the other side", sSmall);
            }
        }
    }

    void DrawStats()
    {
        GUILayout.Label("RUN STATISTICS", sHeader);
        if (dataLogger == null)
        {
            GUILayout.Label("DataLogger not found", sSmall);
            return;
        }

        int spawned = config != null ? config.GetTotalSpawned() : 0;
        int cleared = dataLogger.GetVehicleCount();

        Stat("Vehicles released", spawned.ToString());
        Stat("Vehicles cleared", cleared.ToString());
        Stat("Average wait", $"{dataLogger.GetAverageWaitTime():F2} s");
        Stat("Maximum wait", $"{dataLogger.GetMaxWaitTime():F2} s");
        Stat("Starvation events", dataLogger.GetStarvationCount().ToString());
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
        if (dataLogger == null) { Toast("DataLogger not found"); return; }
        if (config != null) dataLogger.SetTotalSpawned(config.GetTotalSpawned());
        dataLogger.ExportDetailedCSV();
        dataLogger.ExportSummaryCSV();
        dataLogger.ExportPhaseCSV();
        Toast("Saved to SimulationResults");
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
