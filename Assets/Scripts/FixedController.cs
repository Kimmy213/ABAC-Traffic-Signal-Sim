using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Baseline controller: วนเฟสตายตัวตามลำดับที่กำหนดไว้ ไม่สนใจ demand จริง
/// รองรับ "เฟสที่เปิดหลายเลนพร้อมกัน" (เช่น A+B กระแสตรงสวนทาง)
/// อ้างอิงรอบสัญญาณ 120 วินาที/รอบ ตาม DOH Thailand (2019)
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
        Debug.Log($"[FixedController] Cycle length = {GetCycleLength():F0}s (เป้าหมาย 120s ตาม DOH Thailand 2019)");
    }

    /// <summary>
    /// แผนเฟสตายตัวของแยก ABAC — 3 เฟส คุม 4 ไฟ รวม 120 วินาที/รอบ
    ///   P1 A+B   45s  (กระแสตรงสวนทาง วิ่งพร้อมกันได้)
    ///   P2 B+Bx  30s  (ขาตะวันออกตรง + เลี้ยวขวาเข้า ABAC แบบ protected)
    ///   P3 CB    30s  (ระบายรถออกจาก ABAC)
    ///   inter-green 3 ครั้ง x (3s เหลือง + 2s แดงทั้งหมด) = 15s
    ///   รวม 45+30+30+15 = 120s
    /// </summary>
    [ContextMenu("Load ABAC 4-Phase Preset (120s cycle)")]
    public void LoadABACPreset()
    {
        phases = new List<FixedPhase>
        {
            new FixedPhase { phaseName = "P1 MainThrough",  greenLanes = new[] { "A", "B" },  greenDuration = 45f },
            new FixedPhase { phaseName = "P2 EastRightTurn", greenLanes = new[] { "B", "Bx" }, greenDuration = 30f },
            new FixedPhase { phaseName = "P3 ABACExit",      greenLanes = new[] { "CB" },      greenDuration = 30f },
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

        float w = 260;
        float x = Screen.width - w - 10;
        GUI.Box(new Rect(x, 10, w, 40), "");
        GUI.Label(new Rect(x + 8, 13, w - 16, 20), "FIXED CONTROLLER");
        GUI.Label(new Rect(x + 8, 30, w - 16, 20), statusText);
    }
}
