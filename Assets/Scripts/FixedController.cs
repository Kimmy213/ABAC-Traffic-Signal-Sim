using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Baseline controller: วนเฟสตายตัวตามลำดับที่กำหนดไว้ ไม่สนใจ demand จริง
/// รองรับ "เฟสที่เปิดหลายเลนพร้อมกัน" (เช่น A+B กระแสตรงสวนทาง)
/// เดิมอ้างอิงรอบสัญญาณ 120 วินาที/รอบ ตาม DOH Thailand (2019) — ปัจจุบันปรับเป็น 106s
/// ตามที่ Kimmy จับเวลาจริงหน้างาน + Itim วิเคราะห์วิดีโอยืนยัน (A=40s/B=65s/Bx=20-23s/CB=31s แดง 73.5s)
/// ดูรายละเอียดที่ LoadABACPreset() — ยืนยันแล้วว่าไฟเป็น fixed-time จริง ไม่ใช่ actuated (ดู reply_to_badin_4.md ข้อ 3)
/// </summary>
public class FixedController : MonoBehaviour
{
    [System.Serializable]
    public class FixedPhase
    {
        public string phaseName = "Phase";
        [Tooltip("ชื่อเลนที่ได้ไฟเขียวในเฟสนี้ (ต้องไม่ conflict กันเอง)")]
        public string[] greenLanes;
        public float greenDuration = 30f;
    }

    [Header("Intersection")]
    public IntersectionConfig config;

    [Header("Fixed Phase Plan (วนตามลำดับนี้)")]
    public List<FixedPhase> phases = new List<FixedPhase>();

    [Header("Inter-green Timing (ITE)")]
    public float yellowDuration = 3f;
    public float allRedDuration = 2f;

    [Header("Debug")]
    public bool showDebugGUI = false;
    public bool verboseLog = false;

    // runtime
    private List<int>[] phaseLaneIndices;   // แปลงชื่อเลน -> signal index ไว้ล่วงหน้า
    private List<int> currentGreen = new List<int>();
    private int currentPhase = 0;
    private string statusText = "Idle";

    void Awake()
    {
        if (config == null) config = FindFirstObjectByType<IntersectionConfig>();
        if (config == null)
        {
            Debug.LogError("[FixedController] ไม่พบ IntersectionConfig ในซีน");
            return;
        }
        config.EnsureBuilt();
        BuildPhaseCache();
    }

    void BuildPhaseCache()
    {
        phaseLaneIndices = new List<int>[phases.Count];

        for (int p = 0; p < phases.Count; p++)
        {
            List<int> indices = new List<int>();
            FixedPhase phase = phases[p];

            if (phase.greenLanes != null)
            {
                foreach (string name in phase.greenLanes)
                {
                    int s = config.SignalIndexOf(name);
                    if (s == -1)
                    {
                        Debug.LogError($"[FixedController] เฟส '{phase.phaseName}' อ้างถึงเลน '{name}' " +
                                       $"ที่ไม่มีอยู่ใน IntersectionConfig (หรือไม่ใช่เลนที่มีไฟ)");
                        continue;
                    }
                    if (!indices.Contains(s)) indices.Add(s);
                }
            }

            // ===== ตรวจ conflict ภายในเฟสเดียวกัน — กันตั้งค่าผิดแล้วรถชนกลางแยก =====
            for (int i = 0; i < indices.Count; i++)
            {
                for (int j = i + 1; j < indices.Count; j++)
                {
                    if (config.AreConflicting(indices[i], indices[j]))
                    {
                        Debug.LogError($"[FixedController] เฟส '{phase.phaseName}' เปิด " +
                                       $"{config.SignalName(indices[i])} กับ {config.SignalName(indices[j])} " +
                                       $"พร้อมกัน แต่สองเลนนี้ conflict กัน — ตัด {config.SignalName(indices[j])} ออก");
                        indices.RemoveAt(j);
                        j--;
                    }
                }
            }

            phaseLaneIndices[p] = indices;
        }
    }

    public void ForceStop()
    {
        StopAllCoroutines();
        currentGreen.Clear();
        SetAllRed();
        statusText = "Stopped";
    }

    public void RestartCycle()
    {
        StopAllCoroutines();
        currentPhase = 0;
        currentGreen.Clear();
        BuildPhaseCache();       // เผื่อมีการแก้ค่าใน Inspector ระหว่าง batch
        SetAllRed();
        StartCoroutine(RunCycle());
    }

    void Start()
    {
        if (config == null) return;
        SetAllRed();
        StartCoroutine(RunCycle());
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

    void SetLanes(List<int> indices, TrafficLight.LightState state)
    {
        foreach (int s in indices)
        {
            TrafficLight light = config.SignalLight(s);
            if (light != null) light.SetState(state);
        }
    }

    IEnumerator RunCycle()
    {
        if (phases.Count == 0)
        {
            Debug.LogError("[FixedController] ยังไม่ได้ตั้ง phase plan — กด 'Load ABAC 4-Phase Preset' ที่ context menu ก่อน");
            yield break;
        }

        while (true)
        {
            List<int> greenNow = phaseLaneIndices[currentPhase];
            FixedPhase phase = phases[currentPhase];

            // ===== เปิดเขียวเฉพาะเลนที่ยังไม่เขียว (เลนที่ต่อเนื่องจากเฟสก่อนไม่ต้องกระพริบ) =====
            List<int> turningOn = Except(greenNow, currentGreen);
            SetLanes(turningOn, TrafficLight.LightState.Green);
            currentGreen = new List<int>(greenNow);

            statusText = $"{phase.phaseName} — GREEN {config.DescribeGroup(greenNow)}";
            if (verboseLog)
                Debug.Log($"[Fixed] {phase.phaseName}: green {config.DescribeGroup(greenNow)} for {phase.greenDuration}s");

            yield return new WaitForSeconds(phase.greenDuration);

            // ===== หาว่าเฟสถัดไปยังต้องเขียวเลนไหนบ้าง เลนที่ต้อง "ตัด" เท่านั้นที่ขึ้นเหลือง =====
            int nextPhase = (currentPhase + 1) % phases.Count;
            List<int> greenNext = phaseLaneIndices[nextPhase];
            List<int> dropping = Except(greenNow, greenNext);

            if (dropping.Count > 0)
            {
                statusText = $"{phase.phaseName} — YELLOW {config.DescribeGroup(dropping)}";
                SetLanes(dropping, TrafficLight.LightState.Yellow);
                yield return new WaitForSeconds(yellowDuration);

                statusText = "ALL-RED clearance";
                SetLanes(dropping, TrafficLight.LightState.Red);
                currentGreen = Except(currentGreen, dropping);
                yield return new WaitForSeconds(allRedDuration);
            }

            currentPhase = nextPhase;
        }
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

    /// <summary>ความยาวรอบสัญญาณรวม (วินาที) — ใช้ตรวจว่าตรง 120s ตาม DOH หรือยัง</summary>
    public float GetCycleLength()
    {
        float total = 0f;
        for (int p = 0; p < phases.Count; p++)
        {
            total += phases[p].greenDuration;

            int next = (p + 1) % phases.Count;
            List<int> a = phaseLaneIndices != null && phaseLaneIndices[p] != null ? phaseLaneIndices[p] : new List<int>();
            List<int> b = phaseLaneIndices != null && phaseLaneIndices[next] != null ? phaseLaneIndices[next] : new List<int>();
            if (Except(a, b).Count > 0) total += yellowDuration + allRedDuration;
        }
        return total;
    }

    [ContextMenu("Print Cycle Length")]
    void PrintCycleLength()
    {
        if (config != null) { config.EnsureBuilt(); BuildPhaseCache(); }
        Debug.Log($"[FixedController] Cycle length = {GetCycleLength():F0}s (เป้าหมาย 106s ตาม SP1_HANDOFF_v2)");
    }

    /// <summary>
    /// แผนเฟสตายตัวของแยก ABAC — 3 เฟส คุม 5 ไฟ (A ใช้ TrafficLight ร่วมกับ A2) รวม 107 วินาที/รอบ
    ///   P1 A+A2+B  40s  (กระแสตรงสวนทาง + เลนแชร์นอก A2 วิ่งพร้อมกันได้ — A2 ใช้ไฟดวงเดียวกับ A)
    ///   P2 B+Bx    20s  (ขาตะวันออกตรง + เลี้ยวขวาเข้า ABAC แบบ protected — ⭐ นี่คือคอขวดจริง ดูหมายเหตุด้านล่าง)
    ///   P3 CB      31s  (ระบายรถออกจาก ABAC)
    ///   inter-green 3 ครั้ง x (3s เหลือง + 2s แดงทั้งหมด) = 15s
    ///   รวม 40+20+31+15 = 106s
    ///
    ///   ⭐ ค่าชุดนี้มาจาก Kimmy จับเวลาไฟจริงหน้างาน (A=50/B=90-95/Bx=20/CB=31/3/75 ที่นับสด — เชื่อถือ CB
    ///   ได้สุดเพราะมีจอนับถอยหลัง) แล้ว Itim เอาไปเทียบกับวิดีโอที่ Kimmy ถ่ายมา วิเคราะห์ซ้ำได้ A=40s / B=65s /
    ///   Bx=20-23s / CB=31s เขียว, แดง C=73.5s (วัดซ้ำ 2 รอบได้เท่ากันเป๊ะ → ยืนยันเป็น fixed-time จริง ไม่ใช่
    ///   actuated)
    ///
    ///   ⭐ อัปเดตตาม SP1_HANDOFF_v2: Itim ให้ตารางสเตจเต็มมาแล้ว และสั่งให้ **ใช้ Bx = 20s** (ไม่ใช่ 21 หรือ 23)
    ///   เพราะค่าจับด้วยนาฬิกาได้ 20.0s พอดี ส่วน 23s มาจากการจับอีกวิธี — ช่วงคลาด ±3s นี้ไม่เปลี่ยนข้อสรุป
    ///   (Bx oversaturated ทั้งช่วง: v/c 1.21 ที่ 23s, 1.51 ที่ 20s) ตารางสเตจจริงที่ตรงกับ preset นี้เป๊ะ ๆ:
    ///     t=  0-40  A เขียว + B เขียว     |  t= 40-43 A เหลือง (B ยังเขียว)  |  t= 43-45 all-red
    ///     t= 45-65  Bx เขียว + B เขียว    |  t= 65-68 B+Bx เหลืองพร้อมกัน    |  t= 68-70 all-red
    ///     t= 70-101 CB เขียว              |  t=101-104 CB เหลือง             |  t=104-106 all-red
    ///
    ///   ⚠️ v/c จากค่าชุดนี้ (handoff v2): Bx = **1.51** (ล้น คิวโต 3.7 คัน/รอบ กินที่จอด 78m หมดใน 5.7 นาที
    ///   แล้วไปบล็อกสี่แยกถัดไป), CB = **1.04** (เต็มพอดี), A และ B = **0.25** ทั้งคู่ (ได้เขียวเกินที่ต้องใช้ 4 เท่า)
    ///   — ข้อเสนอ "ย้ายเขียว **10s** จาก A ไป Bx" (Bx → v/c 0.95) เป็น**นโยบายที่ควร
    ///   เขียนในรายงาน/ให้ Adaptive หาเจอเอง** ไม่ได้เอามาแก้ preset นี้ตรง ๆ เพราะ Fixed ต้องจำลองปัญหาจริงของ
    ///   ระบบเดิมไว้ ถ้าแก้ preset ให้ฉลาดขึ้นเอง จะเสียจุดประสงค์ของการเทียบ Fixed vs Adaptive ไป
    /// </summary>
    [ContextMenu("Load ABAC 4-Phase Preset (106s cycle)")]
    public void LoadABACPreset()
    {
        phases = new List<FixedPhase>
        {
            new FixedPhase { phaseName = "P1 MainThrough",  greenLanes = new[] { "A", "A2", "B" },  greenDuration = 40f },
            new FixedPhase { phaseName = "P2 EastRightTurn", greenLanes = new[] { "B", "Bx" }, greenDuration = 20f },
            new FixedPhase { phaseName = "P3 ABACExit",      greenLanes = new[] { "CB" },      greenDuration = 31f },
        };
        yellowDuration = 3f;
        allRedDuration = 2f;

        if (config == null) config = FindFirstObjectByType<IntersectionConfig>();
        if (config != null) { config.EnsureBuilt(); BuildPhaseCache(); }

        Debug.Log($"[FixedController] โหลด preset 3 เฟส / 4 ไฟ แล้ว — cycle = {GetCycleLength():F0}s");
    }

    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying) return;

        UITheme.Begin();

        const float w = 288f;
        float x = Screen.width - w - 12f;
        Rect card = new Rect(x, 12f, w, 62f);
        GUI.Box(card, GUIContent.none);

        GUI.Label(new Rect(x + 12f, card.y + 8f, w - 100f, 18f), "FIXED CONTROLLER", UITheme.Title);
        UITheme.Pill(new Rect(x + w - 86f, card.y + 9f, 74f, 17f),
                     $"{GetCycleLength():F0}s cycle", UITheme.AccentDark);
        UITheme.Rule(new Rect(x + 12f, card.y + 31f, w - 24f, 1f));
        GUI.Label(new Rect(x + 12f, card.y + 37f, w - 24f, 18f), statusText, UITheme.Body);

        UITheme.End();
    }
}
