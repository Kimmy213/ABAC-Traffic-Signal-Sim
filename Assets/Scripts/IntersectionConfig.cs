using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// ข้อมูลของ "ทางเข้า/ทิศทางการเคลื่อนที่" หนึ่งเส้นทาง (approach movement)
/// เช่น A = ตรงฝั่งตะวันตก, Bx = เลี้ยวขวาเข้า ABAC, CA = ออกจาก ABAC เลี้ยวซ้ายฟรี
/// </summary>
[System.Serializable]
public class ApproachLane
{
    [Tooltip("ชื่อเลน ต้องตรงกับ laneName ใน CarSpawner และ ROIZone เป๊ะ ๆ")]
    public string laneName = "A";

    [Tooltip("true = มีไฟจราจรคุม (A, B, Bx, CB) / false = ไหลอิสระไม่มีไฟ (Ax, CA)")]
    public bool isSignalized = true;

    [Tooltip("ปล่อยว่างได้ถ้า isSignalized = false")]
    public TrafficLight trafficLight;

    [Tooltip("ROI Zone ของเลนนี้ (ใช้ทั้งนับ demand และวัด waiting time)")]
    public ROIZone roi;

    [Tooltip("Spawner ของเลนนี้")]
    public CarSpawner spawner;

    [Tooltip("คำอธิบายสำหรับใส่ในรายงาน (ไม่มีผลกับ logic)")]
    public string movementDescription = "";
}

/// <summary>
/// คู่เลนที่ "ห้ามเขียวพร้อมกัน" เพราะเส้นทางตัดกัน
/// </summary>
[System.Serializable]
public class ConflictPair
{
    public string laneA;
    public string laneB;
    [Tooltip("เหตุผลของ conflict — ใช้อ้างอิงในรายงาน")]
    public string reason = "";
}

/// <summary>
/// ศูนย์กลางข้อมูลของแยก: รายชื่อเลนทั้งหมด + ตารางความขัดแย้ง (conflict matrix)
/// ทั้ง FixedController, AdaptiveController และ ScenarioManager อ่านจากที่นี่ที่เดียว
/// ทำให้เพิ่ม/ลดเลนได้โดยไม่ต้องแก้โค้ด controller
/// </summary>
public class IntersectionConfig : MonoBehaviour
{
    [Header("เลนทั้งหมดในแยก (signalized + free-flow)")]
    public List<ApproachLane> lanes = new List<ApproachLane>();

    [Header("คู่เลนที่ห้ามเขียวพร้อมกัน")]
    public List<ConflictPair> conflicts = new List<ConflictPair>();

    // ===== ข้อมูลที่ build ขึ้นมาตอน runtime =====
    private bool[,] conflictMatrix;     // index ในปริภูมิ "signal index" (เฉพาะเลนที่มีไฟ)
    private List<int> signalToLane = new List<int>();   // signalIndex -> index ใน lanes
    private bool isBuilt = false;

    void Awake()
    {
        Build();
    }

    /// <summary>
    /// เรียกได้ปลอดภัยหลายครั้ง — Controller ควรเรียกอันนี้ใน Awake() ของตัวเอง
    /// เพราะลำดับ Awake() ระหว่าง MonoBehaviour ไม่การันตี
    /// </summary>
    public void EnsureBuilt()
    {
        if (!isBuilt) Build();
    }

    public void Build()
    {
        signalToLane.Clear();

        for (int i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] == null) continue;
            if (lanes[i].isSignalized) signalToLane.Add(i);
        }

        int n = signalToLane.Count;
        conflictMatrix = new bool[n, n];

        // ต้องตั้ง isBuilt ตรงนี้ ก่อนวน conflicts
        // เพราะข้างล่างเรียก SignalIndexOf() ซึ่งเรียก EnsureBuilt() -> Build() วนไม่จบ
        isBuilt = true;

        // เลนเดียวกันไม่ conflict กับตัวเอง, ที่เหลือ default = ไม่ conflict
        foreach (ConflictPair pair in conflicts)
        {
            if (pair == null) continue;
            int a = SignalIndexOf(pair.laneA);
            int b = SignalIndexOf(pair.laneB);

            if (a == -1 || b == -1)
            {
                Debug.LogWarning($"[IntersectionConfig] Conflict pair ({pair.laneA}, {pair.laneB}) " +
                                 $"อ้างถึงเลนที่ไม่มีอยู่ หรือเลนนั้นไม่ใช่ signalized — ข้ามไป");
                continue;
            }
            if (a == b) continue;

            conflictMatrix[a, b] = true;
            conflictMatrix[b, a] = true;
        }
    }

    // ===================== Accessors =====================

    /// <summary>จำนวนเลนที่มีไฟจราจรคุม (ABAC = 4: A, B, Bx, CB)</summary>
    public int SignalCount
    {
        get { EnsureBuilt(); return signalToLane.Count; }
    }

    public ApproachLane GetSignalLane(int signalIndex)
    {
        EnsureBuilt();
        return lanes[signalToLane[signalIndex]];
    }

    public string SignalName(int signalIndex) => GetSignalLane(signalIndex).laneName;
    public TrafficLight SignalLight(int signalIndex) => GetSignalLane(signalIndex).trafficLight;
    public ROIZone SignalRoi(int signalIndex) => GetSignalLane(signalIndex).roi;

    /// <summary>แปลงชื่อเลน -> signal index (คืน -1 ถ้าไม่เจอ หรือเลนนั้นไม่มีไฟ)</summary>
    public int SignalIndexOf(string laneName)
    {
        EnsureBuilt();
        if (string.IsNullOrEmpty(laneName)) return -1;

        for (int s = 0; s < signalToLane.Count; s++)
        {
            if (lanes[signalToLane[s]].laneName == laneName) return s;
        }
        return -1;
    }

    /// <summary>หาเลน (รวม free-flow) จากชื่อ</summary>
    public ApproachLane FindLane(string laneName)
    {
        foreach (ApproachLane lane in lanes)
        {
            if (lane != null && lane.laneName == laneName) return lane;
        }
        return null;
    }

    /// <summary>true = เลนสองเส้นนี้เขียวพร้อมกันไม่ได้</summary>
    public bool AreConflicting(int signalA, int signalB)
    {
        EnsureBuilt();
        if (signalA == signalB) return false;
        if (signalA < 0 || signalB < 0) return false;
        if (signalA >= conflictMatrix.GetLength(0) || signalB >= conflictMatrix.GetLength(0)) return false;
        return conflictMatrix[signalA, signalB];
    }

    /// <summary>true = เลน candidate เขียวพร้อมกับทุกเลนในกลุ่มได้</summary>
    public bool IsCompatibleWithGroup(int candidate, List<int> group)
    {
        foreach (int member in group)
        {
            if (AreConflicting(candidate, member)) return false;
        }
        return true;
    }

    public string DescribeGroup(List<int> group)
    {
        if (group == null || group.Count == 0) return "(none)";
        string[] names = new string[group.Count];
        for (int i = 0; i < group.Count; i++) names[i] = SignalName(group[i]);
        return string.Join("+", names);
    }

    // ===================== Lane control (ใช้ร่วมกันระหว่าง ScenarioManager กับ Control Panel) =====================

    /// <summary>
    /// ล้างสถานะทุกเลนแล้วตั้ง seed ประจำเลน + เริ่ม spawn ใหม่
    /// seed แต่ละเลนคำนวณจาก runSeed + ชื่อเลน → Fixed กับ Adaptive ที่ seed เดียวกัน
    /// จะเจอลำดับรถเหมือนกันเป๊ะ (สำคัญกับ paired t-test)
    /// </summary>
    public void ResetAllLanes(int runSeed)
    {
        // ⚠️ ต้องแยกเป็น 2 รอบ ห้ามรวมเป็นลูปเดียว
        //
        // เหตุผล: RestartSpawning() เรียก StartCoroutine ซึ่งรัน body ทันทีจนถึง yield แรก
        // ในโหมด Scripted burst ที่ atTime = 0 จะ "spawn รถคันแรกทันที" ตรงนั้นเลย
        // แล้วรถคันนั้นจะไป RegisterCar กับไฟ
        // ถ้าเราเรียก ClearRegistrations() ทีหลังในลูปเดียวกัน = ลบทะเบียนรถคันนั้นทิ้ง
        // ผลคือรถคันแรกของทุกเลนไม่เคยได้รับแจ้งว่าไฟเขียว → จอดแช่ตลอดกาล
        // และรถที่ตามมาข้างหลังก็ติดตามไปด้วยทั้งแถว

        // ---- รอบที่ 1: ล้างของเก่าให้หมดก่อน ----
        foreach (ApproachLane lane in lanes)
        {
            if (lane == null) continue;
            if (lane.spawner != null) lane.spawner.ClearAllCars();
            if (lane.roi != null) lane.roi.ClearAll();
            if (lane.trafficLight != null) lane.trafficLight.ClearRegistrations();
        }

        // ---- รอบที่ 2: ตั้ง seed แล้วค่อยเริ่มปล่อยรถ ----
        foreach (ApproachLane lane in lanes)
        {
            if (lane == null || lane.spawner == null) continue;
            lane.spawner.SetSeed(runSeed + StableHash(lane.laneName));
            lane.spawner.ResetSpawnCount();
            lane.spawner.RestartSpawning();
        }
    }

    /// <summary>ล้างรถออกจากถนนทั้งหมด โดยไม่เริ่ม spawn ใหม่</summary>
    public void ClearAllCars()
    {
        foreach (ApproachLane lane in lanes)
        {
            if (lane == null) continue;
            if (lane.spawner != null) lane.spawner.ClearAllCars();
            if (lane.roi != null) lane.roi.ClearAll();
            if (lane.trafficLight != null) lane.trafficLight.ClearRegistrations();
        }
    }

    public int GetTotalSpawned()
    {
        int total = 0;
        foreach (ApproachLane lane in lanes)
            if (lane != null && lane.spawner != null) total += lane.spawner.totalSpawned;
        return total;
    }

    /// <summary>จำนวนรถที่ยังวิ่งอยู่บนถนนทั้งแยก</summary>
    public int GetActiveCarCount()
    {
        int total = 0;
        foreach (ApproachLane lane in lanes)
            if (lane != null && lane.spawner != null) total += lane.spawner.ActiveCarCount;
        return total;
    }

    /// <summary>true = ทุกเลนอยู่ในโหมด Scripted และปล่อยรถครบตามตารางหมดแล้ว</summary>
    public bool AllScheduledSpawned()
    {
        bool anyScripted = false;
        foreach (ApproachLane lane in lanes)
        {
            if (lane == null || lane.spawner == null) continue;
            if (lane.spawner.arrivalMode != ArrivalMode.Scripted) return false;
            anyScripted = true;
            if (!lane.spawner.AllScheduledSpawned) return false;
        }
        return anyScripted;
    }

    public void ResetSpawnCounts()
    {
        foreach (ApproachLane lane in lanes)
            if (lane != null && lane.spawner != null) lane.spawner.ResetSpawnCount();
    }

    /// <summary>ตั้งค่าทุกเลนตาม TestCase ที่เลือก (รองรับทั้ง Poisson และ Scripted)</summary>
    public void ApplyTestCase(TestCase testCase, float poissonFallback = 7f)
    {
        if (testCase == null) return;

        foreach (ApproachLane lane in lanes)
        {
            if (lane == null || lane.spawner == null) continue;

            if (testCase.mode == ArrivalMode.Poisson)
            {
                lane.spawner.UsePoisson(testCase.GetInterval(lane.laneName, poissonFallback));
            }
            else
            {
                lane.spawner.UseSchedule(testCase.GetSchedule(lane.laneName));
            }
        }
    }

    /// <summary>เปิด/ปิด debug GUI ของ ROI ทุกเลนพร้อมกัน</summary>
    public void SetRoiDebugGUI(bool on)
    {
        foreach (ApproachLane lane in lanes)
            if (lane != null && lane.roi != null) lane.roi.showDebugGUI = on;
    }

    /// <summary>
    /// FNV-1a — ต้องใช้แทน string.GetHashCode() เพราะของ .NET ไม่การันตีว่าเท่ากันข้าม session
    /// ถ้า hash ไม่คงที่ seed จะไม่ reproducible และ paired comparison จะพัง
    /// </summary>
    public static int StableHash(string text)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    // ===================== Validation =====================

    [ContextMenu("Validate Configuration")]
    public void ValidateConfiguration()
    {
        Build();

        int problems = 0;

        foreach (ApproachLane lane in lanes)
        {
            if (lane == null) { Debug.LogError("[IntersectionConfig] มีช่องเลนว่าง (null) ใน list"); problems++; continue; }

            if (string.IsNullOrEmpty(lane.laneName))
            {
                Debug.LogError("[IntersectionConfig] มีเลนที่ยังไม่ได้ตั้งชื่อ"); problems++;
            }
            if (lane.isSignalized && lane.trafficLight == null)
            {
                Debug.LogError($"[IntersectionConfig] เลน {lane.laneName} ตั้งเป็น signalized แต่ยังไม่ได้ลาก TrafficLight"); problems++;
            }
            if (lane.roi == null)
            {
                Debug.LogError($"[IntersectionConfig] เลน {lane.laneName} ยังไม่ได้ลาก ROIZone"); problems++;
            }
            else if (!lane.roi.Accepts(lane.laneName))
            {
                // ⚠️ ใช้ Accepts() ไม่ใช่เทียบ laneName ตรง ๆ เพราะ ROI ตัวเดียวรับผิดชอบได้หลายเลน
                //    (A2 กับ Ax อยู่เลนกายภาพเดียวกัน จึงใช้ ROI_Ax ร่วมกัน — ดู ROIZone.alsoCountLanes)
                Debug.LogError($"[IntersectionConfig] ROIZone '{lane.roi.name}' ของเลน {lane.laneName} ไม่ได้นับเลนนี้ " +
                               $"(laneName='{lane.roi.laneName}', alsoCountLanes ไม่มี '{lane.laneName}') → demand จะเป็น 0 ตลอด"); problems++;
            }
            if (lane.spawner == null)
            {
                Debug.LogWarning($"[IntersectionConfig] เลน {lane.laneName} ยังไม่ได้ลาก CarSpawner (จะไม่มีรถในเลนนี้)");
            }
            else if (lane.spawner.laneName != lane.laneName)
            {
                Debug.LogError($"[IntersectionConfig] CarSpawner ของเลน {lane.laneName} ตั้ง laneName เป็น '{lane.spawner.laneName}' ไม่ตรงกัน"); problems++;
            }
        }

        // เช็คชื่อซ้ำ
        HashSet<string> seen = new HashSet<string>();
        foreach (ApproachLane lane in lanes)
        {
            if (lane == null) continue;
            if (!seen.Add(lane.laneName))
            {
                Debug.LogError($"[IntersectionConfig] ชื่อเลน '{lane.laneName}' ซ้ำกัน"); problems++;
            }
        }

        if (problems == 0)
            Debug.Log($"[IntersectionConfig] ✓ ผ่านทั้งหมด — {lanes.Count} เลน ({SignalCount} เลนมีไฟ), {conflicts.Count} conflict pairs");
        else
            Debug.LogError($"[IntersectionConfig] พบปัญหา {problems} จุด แก้ก่อนกด Play");
    }

    /// <summary>
    /// เติมโครงสร้างเลน + conflict pair ของแยก ABAC ให้อัตโนมัติ
    /// (ยังต้องลาก TrafficLight / ROIZone / CarSpawner ใส่เองใน Inspector)
    /// </summary>
    [ContextMenu("Load ABAC T-Junction Preset (โครงสร้างเปล่า)")]
    public void LoadABACPreset()
    {
        lanes = new List<ApproachLane>
        {
            new ApproachLane { laneName = "A",  isSignalized = true,  movementDescription = "ถนนบางนา-ตราด ทิศทางไปตะวันออก (+X) วิ่งตรงผ่านแยก — เลนใน" },
            new ApproachLane { laneName = "A2", isSignalized = true,  movementDescription = "รถตรงที่ใช้เลนนอกร่วมกับ Ax (shared lane) — ใช้ไฟดวงเดียวกับ A เป๊ะ ๆ และ ROI ตัวเดียวกับ Ax เป๊ะ ๆ (อ้างถึง component เดียวกันทั้งคู่ ไม่ใช่แค่ตั้งค่าให้เหมือนกัน) จึงเขียว/แดงพร้อม A เสมอ และถูกนับรวมกับ Ax เหมือนที่ detector จริงเห็น — สัดส่วนรถตรงเข้าเลนนอก 18% (Itim back-calculate จากอัตราการบล็อกจริง, sensitivity 13-23%)" },
            new ApproachLane { laneName = "Ax", isSignalized = false, movementDescription = "แยกจาก A เลี้ยวซ้ายเข้า ABAC (ABAC อยู่ทางซ้ายของ A) — ไม่มีเฟส/เวลาเขียวของตัวเอง แต่ไม่ใช่เลนฟรี 100%: (1) วิ่งเลนเดียวกับ A2 จึงถูก A2 ที่จอดรอไฟแดงบล็อก (2) เลี้ยวเข้ามอไม่ได้ตอน Bx เขียว เพราะ Bx ก็เลี้ยวเข้ามอเหมือนกัน — ทำผ่าน CarSpawner.lightIsInverted ผูกกับไฟ Bx แบบกลับด้าน ไม่ได้อยู่ใน conflict matrix เพราะไม่ใช่เลนที่มีไฟ" },
            new ApproachLane { laneName = "B",  isSignalized = true,  movementDescription = "ถนนบางนา-ตราด ทิศทางไปตะวันตก (-X) วิ่งตรงผ่านแยก" },
            new ApproachLane { laneName = "Bx", isSignalized = true,  movementDescription = "แยกจาก B เลี้ยวขวาเข้า ABAC — ตัดกระแส A (ABAC อยู่ทางขวาของ B)" },
            new ApproachLane { laneName = "CB", isSignalized = true,  movementDescription = "ออกจาก ABAC เลี้ยวขวาไปทางตะวันตก merge เข้าเลน B — ต้องข้ามเลน A ก่อน" },
            new ApproachLane { laneName = "CA", isSignalized = false, movementDescription = "ออกจาก ABAC เลี้ยวซ้ายไปทางตะวันออก merge เข้าเลน A — ฟรี ไม่ต้องข้ามเลนใคร" },
        };

        conflicts = new List<ConflictPair>
        {
            new ConflictPair { laneA = "A",  laneB = "Bx", reason = "Bx เลี้ยวขวาเข้า ABAC ต้องตัดผ่านกระแสรถตรงของเลน A" },
            new ConflictPair { laneA = "A",  laneB = "CB", reason = "CB ต้องข้ามช่องจราจรของเลน A ก่อนไป merge เลน B" },
            new ConflictPair { laneA = "A2", laneB = "Bx", reason = "A2 ใช้ไฟเดียวกับ A — เลนนอกก็ถูก Bx ตัดผ่านเหมือนกับเลนใน" },
            new ConflictPair { laneA = "A2", laneB = "CB", reason = "A2 ใช้ไฟเดียวกับ A — CB ต้องข้ามเลนนอกก่อนไป merge เลน B เหมือนกับที่ต้องข้ามเลนใน" },
            new ConflictPair { laneA = "B",  laneB = "CB", reason = "CB merge เข้าช่องจราจรที่ B กำลังวิ่งผ่านพอดี" },
            new ConflictPair { laneA = "Bx", laneB = "CB", reason = "เส้นทางตัดกันกลางแยก (ตามที่ระบุใน scope)" },
        };

        // หมายเหตุ: A กับ B ไม่ conflict (กระแสตรงสวนทางกัน) -> เขียวพร้อมกันได้
        //          B กับ Bx ไม่ conflict (มาจากขาเดียวกัน)   -> เขียวพร้อมกันได้
        //          A กับ A2 ไม่ conflict โดยตั้งใจ (ไม่มี ConflictPair) -> ต้องเขียวพร้อมกันได้เสมอ
        //          เพราะจริง ๆ แล้วมันคือไฟดวงเดียวกัน ถ้าใครไป conflict กับ A ก็ต้อง conflict กับ A2 ด้วยเสมอ

        Build();
        Debug.Log("[IntersectionConfig] โหลด preset แยก ABAC แล้ว — เหลือลาก TrafficLight / ROIZone / CarSpawner ใส่แต่ละเลน");
    }
}
