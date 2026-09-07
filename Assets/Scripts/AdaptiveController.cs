using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Adaptive controller: FCFS เลือกเลนที่มีรถรอนานที่สุดเป็น "leader"
/// แล้วรวมเลนอื่นที่ไม่ conflict เข้าเป็นกลุ่มเดียวกัน (concurrent phase)
/// เวลาไฟเขียวคำนวณด้วยสูตร HCM: G = startupLostTime + D x saturationHeadway
/// พร้อม gap-out, counting window และ anti-starvation
/// </summary>
public class AdaptiveController : MonoBehaviour
{
    [Header("Intersection")]
    public IntersectionConfig config;

    [Header("Timing Formula (HCM-based)")]
    public float gMin = 8f;
    public float gMax = 45f;
    public float yellowDuration = 3f;
    public float allRedDuration = 2f;
    // ⭐ วัดจริงที่แยกนี้ ไม่ใช่ค่า default ของ HCM (จาก 5 เฟสเขียวที่มีคิวเต็ม, 39 ตัวอย่าง, B_morning)
    //    startup lost time 2.95s = 1.5 เท่าของ HCM (2.0) — สมเหตุสมผลเพราะมอไซค์แทรกมาอยู่หัวแถว
    //    ต้องรอให้เคลียร์ก่อนรถยนต์ถึงจะออกได้
    //    ⚠️ ตัวนี้กระทบมากกว่าที่คิด: 2.95s ถูกกินไปจาก "ทุก" เฟสเขียว = 15% ของเฟส Bx ที่ยาว 20s
    //    saturation headway 2.31s/คัน (sd 0.81) = 1561 คัน/ชม./เลน (HCM ให้ 1800)
    //    รถที่ระบายได้ต่อเฟส = (green - 2.95) / 2.31
    public float startupLostTime = MEASURED_STARTUP_LOST_TIME;
    public float saturationHeadway = MEASURED_SATURATION_HEADWAY;

    // ===== ค่าอ้างอิงที่วัดได้จริง — แหล่งความจริงแหล่งเดียวของทั้งโปรเจกต์ =====
    // ⚠️ Unity เก็บค่าของ component ที่วางไว้ในซีนลง SampleScene.unity (serialize)
    //    แล้ว "ทับ" ค่า default ในสคริปต์เสมอตอนโหลดซีน
    //    => แก้ค่า default ในไฟล์ .cs เพียงอย่างเดียว ไม่มีผลกับซีนที่มี component อยู่แล้ว
    //    นี่คือสาเหตุที่ซีนเคยค้างอยู่ที่ 1 / 2.5 / 8 ทั้งที่โค้ดเขียน 2.95 / 2.31 / 4
    //    ป้องกันด้วย 3 ชั้น: (1) const ตรงนี้ (2) EnforceMeasuredParameters() ตอน Awake
    //    (3) ABACSceneBuilder เรียก LoadMeasuredPreset() ทุกครั้งที่ build ซีน
    public const float MEASURED_STARTUP_LOST_TIME  = 2.95f;   // วัดจริง 39 ตัวอย่าง (HCM default = 2.0)
    public const float MEASURED_SATURATION_HEADWAY = 2.31f;   // วัดจริง sd 0.81 (HCM default = 2.0)
    public const float DEFAULT_COUNTING_WINDOW     = 4f;

    [Tooltip("true = บังคับใช้ค่าที่วัดจริงเสมอ ถ้าค่าในซีนเพี้ยนจะแก้ให้อัตโนมัติพร้อมขึ้น error\n" +
             "ปิดเฉพาะตอนตั้งใจทำ sensitivity analysis (เช่นเทียบ HCM 2.0 กับค่าที่วัดได้)")]
    public bool useMeasuredParameters = true;

    [Tooltip("เขียวต่อเนื่องกลุ่มเดิมได้นานสุดกี่วินาที ก่อนบังคับปล่อยเฟส (max green ตามมาตรฐาน)")]
    public float maxContinuousGreen = 90f;

    [Header("Concurrent Phase")]
    [Tooltip("true = รวมเลนที่ไม่ conflict ให้เขียวพร้อมกัน (แนะนำ) / false = ปล่อยทีละเลน")]
    public bool enableConcurrentPhases = true;
    [Tooltip("เลนที่จะถูกรวมเข้ากลุ่มต้องมีรถอย่างน้อยกี่คัน")]
    public int minDemandToJoinGroup = 1;

    [Header("Counting Window")]
    public float countingWindow = DEFAULT_COUNTING_WINDOW;
    public int demandThresholdSkipWait = 3;

    [Header("Gap-out")]
    public bool enableGapOut = true;
    public float gapOutDelay = 3f;

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

    // ===== runtime =====
    private List<int> activeGroup = new List<int>();
    private float phaseTimeRemaining = 0f;
    private float continuousGreenTimer = 0f;
    private bool forcePhaseChange = false;
    private string statusText = "Idle";

    /// <summary>
    /// ตั้งค่าพารามิเตอร์เวลาทั้งหมดกลับไปเป็นชุดที่วัดได้จริง
    /// ABACSceneBuilder เรียกตัวนี้ทุกครั้งที่ build ซีน (แบบเดียวกับที่ FixedController.LoadABACPreset ทำ)
    /// </summary>
    [ContextMenu("Load Measured Parameters (2.95 / 2.31 / 4)")]
    public void LoadMeasuredPreset()
    {
        startupLostTime   = MEASURED_STARTUP_LOST_TIME;
        saturationHeadway = MEASURED_SATURATION_HEADWAY;
        countingWindow    = DEFAULT_COUNTING_WINDOW;
    }

    /// <summary>
    /// ตรวจว่าค่าที่ซีน serialize ไว้ตรงกับค่าที่วัดจริงหรือไม่ ถ้าไม่ตรงจะแก้ให้แล้วขึ้น error ดัง ๆ
    /// เพื่อไม่ให้ batch 270 รอบวิ่งจนจบด้วยค่าตำราโดยไม่มีใครรู้
    /// คืนค่า true ถ้ามีการแก้เกิดขึ้น (แปลว่าซีนเพี้ยนอยู่ ต้องไป save ซีนใหม่)
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
    }

    public void ForceStop()
    {
        StopAllCoroutines();
        activeGroup.Clear();
        continuousGreenTimer = 0f;
        SetAllRed();
        statusText = "Stopped";
    }

    public void RestartCycle()
    {
        StopAllCoroutines();
        activeGroup.Clear();
        continuousGreenTimer = 0f;
        SetAllRed();
        StartCoroutine(RunAdaptiveCycle());
    }

    void Start()
    {
        if (config == null) return;
        SetAllRed();
        StartCoroutine(RunAdaptiveCycle());
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

    void SetGroupState(List<int> group, TrafficLight.LightState state)
    {
        foreach (int s in group)
        {
            TrafficLight light = config.SignalLight(s);
            if (light != null) light.SetState(state);
        }
    }

    // ===================== Main loop =====================

    IEnumerator RunAdaptiveCycle()
    {
        while (true)
        {
            // กันลูปหมุนฟรีในเฟรมเดียว (เช่นตอนโดน max-green ตัดติด ๆ กัน)
            yield return null;

            // ---------- 1. รอจนกว่าจะมี demand ที่ไหนสักแห่ง ----------
            statusText = "Waiting for demand...";
            while (!AnyDemand())
            {
                // ไม่มีรถเลย -> ปล่อยเลนที่เขียวอยู่ให้เขียวต่อ (rest in green ไม่มีใครเสียหาย)
                yield return new WaitForSeconds(0.5f);
            }

            // ถ้าเพิ่งโดน max-green ตัด ให้ห้ามเลือก leader จากกลุ่มเดิม
            List<int> banned = forcePhaseChange ? new List<int>(activeGroup) : null;

            // ---------- 2. เลือก leader lane ----------
            int leader = FindStarvingLane(starvationSelectThreshold, activeGroup);
            bool starvationDriven = leader != -1;

            if (starvationDriven)
            {
                statusText = $"STARVATION OVERRIDE — {config.SignalName(leader)}";
                Debug.Log($"[Adaptive] STARVATION: Lane {config.SignalName(leader)} waited too long, forcing green.");
                if (DataLogger.Instance != null) DataLogger.Instance.LogStarvationEvent(config.SignalName(leader));
            }
            else
            {
                leader = ChooseLeaderFCFS(banned);
                if (leader == -1) leader = ChooseLeaderFCFS(null);
                if (leader == -1) continue;

                int demand = DemandOf(leader);
                if (demand < demandThresholdSkipWait)
                {
                    // ---------- 3. Counting window: รอสะสมดีมานด์อีกนิด ----------
                    statusText = $"Counting window ({countingWindow}s)...";
                    yield return new WaitForSeconds(countingWindow);

                    leader = FindStarvingLane(starvationSelectThreshold, activeGroup);
                    if (leader == -1) leader = ChooseLeaderFCFS(banned);
                    if (leader == -1) leader = ChooseLeaderFCFS(null);
                    if (leader == -1) continue;
                }
                else
                {
                    statusText = $"Demand clear ({demand} cars) — skip wait";
                }
            }
            forcePhaseChange = false;

            // ---------- 4. สร้างกลุ่มเลนที่เขียวพร้อมกันได้ ----------
            List<int> nextGroup = BuildCompatibleGroup(leader);

            // ---------- 5. ช่วง clearance: ตัดเฉพาะเลนที่ต้องเปลี่ยนเป็นแดง ----------
            List<int> dropping = Except(activeGroup, nextGroup);
            if (dropping.Count > 0)
            {
                statusText = $"Yellow — {config.DescribeGroup(dropping)}";
                SetGroupState(dropping, TrafficLight.LightState.Yellow);
                yield return StartCoroutine(CountdownPhase(yellowDuration));

                statusText = "All-Red clearance";
                SetGroupState(dropping, TrafficLight.LightState.Red);
                activeGroup = Except(activeGroup, dropping);
                yield return StartCoroutine(CountdownPhase(allRedDuration));
            }

            // ---------- 6. เปิดเขียวเลนใหม่ ----------
            List<int> turningOn = Except(nextGroup, activeGroup);
            // มีเลนเปลี่ยนสถานะ = เริ่มเฟสใหม่ -> รีเซ็ตตัวจับ max green
            // ถ้ากลุ่มเดิมเป๊ะ ๆ แต่ timer เต็มแล้ว ก็ต้องรีเซ็ตด้วย ไม่งั้นจะวนตัดไม่จบ
            if (turningOn.Count > 0 || dropping.Count > 0 || continuousGreenTimer >= maxContinuousGreen)
                continuousGreenTimer = 0f;
            SetGroupState(turningOn, TrafficLight.LightState.Green);
            activeGroup = new List<int>(nextGroup);

            float greenTime = CalculateGreenDuration(activeGroup);

            if (verboseLog || turningOn.Count > 0)
            {
                Debug.Log($"[Adaptive] Phase {config.DescribeGroup(activeGroup)} selected. " +
                          $"Demand={DescribeDemand(activeGroup)}, GreenTime={greenTime:F1}s");
            }

            statusText = $"Green — {config.DescribeGroup(activeGroup)}";
            yield return StartCoroutine(GreenPhaseWithInterrupts(activeGroup, greenTime));
        }
    }

    // ===================== Group building =====================

    /// <summary>
    /// เริ่มจาก leader แล้วไล่เพิ่มเลนที่ (ก) มีรถรอ (ข) ไม่ conflict กับทุกตัวในกลุ่ม
    /// เรียงตามลำดับ FCFS (รถที่รอนานกว่าได้สิทธิ์ก่อน) เพื่อให้ยังเป็นระบบ first-come-first-served
    /// </summary>
    List<int> BuildCompatibleGroup(int leader)
    {
        List<int> group = new List<int> { leader };
        if (!enableConcurrentPhases) return group;

        // เรียง candidate ตามเวลาที่รถคันแรกเข้ามารอ (เก่าสุดก่อน)
        List<int> candidates = new List<int>();
        for (int s = 0; s < config.SignalCount; s++)
        {
            if (s == leader) continue;
            if (DemandOf(s) < minDemandToJoinGroup) continue;
            candidates.Add(s);
        }
        candidates.Sort((a, b) => OldestArrivalOf(a).CompareTo(OldestArrivalOf(b)));

        foreach (int c in candidates)
        {
            if (config.IsCompatibleWithGroup(c, group)) group.Add(c);
        }

        // ===== เลนที่ใช้ TrafficLight component เดียวกันจริง ๆ (เช่น A/A2 เลนแชร์) ต้องอยู่กลุ่มเดียวกันเสมอ =====
        // ไม่ว่าตอนสร้างกลุ่มด้านบนจะมี demand พอให้ถูกเลือกเข้ากลุ่มหรือไม่ก็ตาม
        // เพราะไฟของมันซิงค์กันอัตโนมัติอยู่แล้ว (คนละ signal index แต่ชี้ TrafficLight object เดียวกัน)
        // ถ้าไม่บังคับรวม: สมมติ A2 เป็น leader แต่ A มี 0 คันตอนนั้น -> group = {A2} เท่านั้น
        // แล้วพอมีรถ A มาใหม่ระหว่างเฟส gap-out/anti-starvation จะมองไม่เห็น demand ของ A เลย
        // (เพราะ GroupIsEmpty/starvation เช็คเฉพาะสมาชิกใน group) ทั้งที่ไฟจริงเขียวให้ A อยู่แล้ว
        AddPhysicallyLinkedSignals(group);

        return group;
    }

    /// <summary>เติมเลนอื่นที่ใช้ TrafficLight component ตัวเดียวกับสมาชิกที่มีอยู่แล้วเข้ากลุ่ม (fixed point)</summary>
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
                    if (config.SignalLight(member) == light)
                    {
                        group.Add(s);
                        added = true;
                        break;
                    }
                }
            }
        }
    }

    // ===================== Green phase =====================

    IEnumerator GreenPhaseWithInterrupts(List<int> group, float maxGreenTime)
    {
        float elapsed = 0f;
        float emptyTimer = 0f;

        while (elapsed < maxGreenTime)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            continuousGreenTimer += dt;
            phaseTimeRemaining = maxGreenTime - elapsed;

            // ---- Max green: กันเลนเดิมเขียวยาวไม่จบ ----
            if (continuousGreenTimer >= maxContinuousGreen && AnyDemandOutside(group))
            {
                Debug.Log($"[Adaptive] Max green {maxContinuousGreen}s reached on " +
                          $"{config.DescribeGroup(group)} — forcing phase change");
                forcePhaseChange = true;
                yield break;
            }

            // ---- Gap-out: ทุกเลนในกลุ่มว่างพร้อมกันนานพอ ----
            if (enableGapOut)
            {
                if (GroupIsEmpty(group))
                {
                    emptyTimer += dt;
                    if (emptyTimer >= gapOutDelay)
                    {
                        if (verboseLog)
                            Debug.Log($"[Adaptive] {config.DescribeGroup(group)} gap-out at {elapsed:F1}s");
                        yield break;
                    }
                }
                else
                {
                    emptyTimer = 0f;
                }
            }

            // ---- Anti-starvation: ตัดเฟสสั้นถ้ามีเลนอื่นรอนานเกิน ----
            if (elapsed >= minGreenBeforeInterrupt)
            {
                int starving = FindStarvingLane(starvationInterruptThreshold, group);
                if (starving != -1)
                {
                    float otherWait = OldestWaitOf(starving);
                    Debug.Log($"[Adaptive] Cutting {config.DescribeGroup(group)} short at {elapsed:F1}s " +
                              $"(after min {minGreenBeforeInterrupt}s served) to prevent Lane " +
                              $"{config.SignalName(starving)} starvation (waited {otherWait:F1}s)");
                    if (DataLogger.Instance != null)
                        DataLogger.Instance.LogStarvationEvent(config.SignalName(starving));
                    yield break;
                }
            }

            yield return null;
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

    int DemandOf(int signalIndex)
    {
        ROIZone roi = config.SignalRoi(signalIndex);
        return roi != null ? roi.GetVehicleCount() : 0;
    }

    float OldestArrivalOf(int signalIndex)
    {
        ROIZone roi = config.SignalRoi(signalIndex);
        return roi != null ? roi.GetOldestArrivalTime() : Mathf.Infinity;
    }

    float OldestWaitOf(int signalIndex)
    {
        ROIZone roi = config.SignalRoi(signalIndex);
        return roi != null ? roi.GetLongestWaitTime() : 0f;
    }

    bool AnyDemand()
    {
        for (int s = 0; s < config.SignalCount; s++)
        {
            if (DemandOf(s) > 0) return true;
        }
        return false;
    }

    bool AnyDemandOutside(List<int> group)
    {
        for (int s = 0; s < config.SignalCount; s++)
        {
            if (group.Contains(s)) continue;
            if (DemandOf(s) > 0) return true;
        }
        return false;
    }

    bool GroupIsEmpty(List<int> group)
    {
        foreach (int s in group)
        {
            if (DemandOf(s) > 0) return false;
        }
        return true;
    }

    /// <summary>เลนที่มีรถรอนานที่สุดและเกิน threshold — ไม่นับเลนที่กำลังเขียวอยู่</summary>
    int FindStarvingLane(float thresholdRatio, List<int> excludeGroup)
    {
        float threshold = maxWaitTime * thresholdRatio;
        int worst = -1;
        float worstWait = threshold;

        for (int s = 0; s < config.SignalCount; s++)
        {
            if (excludeGroup != null && excludeGroup.Contains(s)) continue;
            if (DemandOf(s) == 0) continue;

            float wait = OldestWaitOf(s);
            if (wait >= worstWait)
            {
                worstWait = wait;
                worst = s;
            }
        }
        return worst;
    }

    /// <summary>FCFS: เลนที่รถคันแรกเข้ามารอนานที่สุด (banned = เลนที่ห้ามเลือกรอบนี้)</summary>
    int ChooseLeaderFCFS(List<int> banned = null)
    {
        int best = -1;
        float oldest = Mathf.Infinity;

        for (int s = 0; s < config.SignalCount; s++)
        {
            if (DemandOf(s) == 0) continue;
            if (banned != null && banned.Contains(s)) continue;

            float arrival = OldestArrivalOf(s);
            if (arrival < oldest)
            {
                oldest = arrival;
                best = s;
            }
        }
        return best;
    }

    /// <summary>
    /// สูตร HCM: G = startupLostTime + D x saturationHeadway
    /// สำหรับเฟสที่เปิดหลายเลน ใช้เลนที่มีคิวยาวสุดเป็นตัวกำหนด (critical lane)
    /// เพราะทุกเลนในกลุ่มระบายขนานกัน
    /// </summary>
    float CalculateGreenDuration(List<int> group)
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
        foreach (int v in source)
        {
            if (!remove.Contains(v)) result.Add(v);
        }
        return result;
    }

    // ===================== Debug GUI =====================

    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying || config == null) return;

        UITheme.Begin();

        const float W = 288f;
        float x = Screen.width - W - 12f;

        // ---- แถบหัว ----
        Rect head = new Rect(x, 12f, W, 44f);
        GUI.Box(head, GUIContent.none);
        GUI.Label(new Rect(x + 12f, head.y + 6f, W - 24f, 18f), "ADAPTIVE CONTROLLER", UITheme.Title);
        GUI.Label(new Rect(x + 12f, head.y + 24f, W - 24f, 15f), statusText, UITheme.Dim);

        // ---- การ์ดต่อสัญญาณไฟ ----
        int n = config.SignalCount;
        float y = head.y + head.height + 6f;
        const float CH = 44f;

        for (int s = 0; s < n; s++)
        {
            TrafficLight light = config.SignalLight(s);
            TrafficLight.LightState state = light != null ? light.currentState : TrafficLight.LightState.Red;
            bool active = activeGroup.Contains(s);
            string lane = config.SignalName(s);

            Rect card = new Rect(x, y, W, CH);
            GUI.Box(card, GUIContent.none);

            // แถบสีเลนด้านซ้าย + จุดสถานะไฟ
            UITheme.Dot(new Rect(card.x + 10f, card.y + 8f, 10f, 10f), UITheme.SignalColor(state));
            GUI.Label(new Rect(card.x + 26f, card.y + 4f, 90f, 17f), lane, UITheme.Title);

            UITheme.Pill(new Rect(card.x + W - 78f, card.y + 5f, 66f, 16f),
                         state.ToString().ToUpper(), UITheme.SignalColor(state));

            if (active)
            {
                // กำลังเขียว: โชว์เวลาที่เหลือเป็นแถบ เทียบกับ gMax
                float t = gMax > 0.01f ? phaseTimeRemaining / gMax : 0f;
                UITheme.Bar(new Rect(card.x + 26f, card.y + 26f, W - 120f, 5f), t, UITheme.SigGreen);
                GUI.Label(new Rect(card.x + W - 90f, card.y + 22f, 78f, 15f),
                          $"{phaseTimeRemaining:F1}s \u00B7 {DemandOf(s)} cars", UITheme.Right);
            }
            else
            {
                // รออยู่: แถบยิ่งเต็ม = ยิ่งใกล้ถูกบังคับให้เขียว (anti-starvation)
                float wait = OldestWaitOf(s);
                float t = maxWaitTime > 0.01f ? wait / maxWaitTime : 0f;
                Color c = t >= 1f ? UITheme.SigRed : t >= 0.6f ? UITheme.SigYellow : UITheme.Neutral;
                UITheme.Bar(new Rect(card.x + 26f, card.y + 26f, W - 120f, 5f), t, c);
                GUI.Label(new Rect(card.x + W - 90f, card.y + 22f, 78f, 15f),
                          $"wait {wait:F0}/{maxWaitTime:F0}s", UITheme.Right);
            }

            y += CH + 4f;
        }

        UITheme.End();
    }

    Color GetStateColor(TrafficLight.LightState state)
    {
        switch (state)
        {
            case TrafficLight.LightState.Green: return Color.green;
            case TrafficLight.LightState.Yellow: return Color.yellow;
            default: return Color.red;
        }
    }
}
