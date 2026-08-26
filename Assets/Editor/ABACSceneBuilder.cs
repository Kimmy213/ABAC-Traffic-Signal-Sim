#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// สร้าง/อัปเดต Scene แยก ABAC แบบ 4 ไฟ 7 เลน ให้อัตโนมัติ (A2 ใช้ไฟร่วมกับ A — shared lane กับ Ax)
/// เมนู: ABAC ▸ Build 4-Phase Scene
///
/// ทำงานแบบ idempotent — กดซ้ำได้ ของเดิมที่มีอยู่จะถูกใช้ต่อ ไม่สร้างซ้ำ
/// รองรับ Undo (Ctrl+Z) แต่ควร commit git ก่อนกดอยู่ดี
///
/// ===== พิกัดอ้างอิงจากซีนจริง =====
/// ถนนบางนา-ตราด วางตามแกน X   (centerline z = 0.88)
/// ทางเข้า ABAC อยู่ทาง +Z (เหนือ)
/// A  = วิ่งไปทางตะวันออก (+X)  อยู่ครึ่งเหนือ  (ขับชิดซ้าย)
/// B  = วิ่งไปทางตะวันตก (-X)  อยู่ครึ่งใต้
/// ABAC อยู่ทางซ้ายของ A  -> Ax เลี้ยวซ้าย ฟรี
/// ABAC อยู่ทางขวาของ B  -> Bx เลี้ยวขวา ต้องมีไฟ (ตัดกระแส A)
/// ออกจาก ABAC มุ่งใต้: เลี้ยวซ้าย = ตะวันออก = merge เข้าเลน A ฟรี  -> CA
///                      เลี้ยวขวา = ตะวันตก = ข้ามเลน A ไป merge B   -> CB (ต้องมีไฟ)
/// </summary>
public static class ABACSceneBuilder
{
    // ---------- geometry ----------
    public const float ROAD_Z   = 0.88f;    // centerline ถนนตะวันออก-ตะวันตก
    public const float ABAC_X   = -0.52f;   // centerline ถนนเข้า ABAC

    public const float Z_AX = 6.5f;    // ช่องเลี้ยวซ้ายเข้า ABAC (นอกสุดฝั่งเหนือ)
    public const float Z_A  = 3.0f;    // ช่องวิ่งตรง ขาตะวันออก
    public const float Z_BX = -1.2f;   // ช่องเลี้ยวขวาเข้า ABAC (ชิด centerline ฝั่งใต้)
    public const float Z_B  = -4.5f;   // ช่องวิ่งตรง ขาตะวันตก

    const float X_AX_IN = -4.0f;   // เลนขาเข้า ABAC ที่รับจาก Ax
    const float X_BX_IN = -1.8f;   // เลนขาเข้า ABAC ที่รับจาก Bx
    const float X_CB    = 1.5f;    // เลนขาออก ABAC เลี้ยวขวา (ไปตะวันตก)
    const float X_CA    = 3.8f;    // เลนขาออก ABAC เลี้ยวซ้าย (ไปตะวันออก)

    public const float STOP_A_X = -10.5f;
    // จุดที่ Ax เริ่มหักเลี้ยว (= waypoint แรกของ Ax)
    public const float AX_TURN_X = -11.0f;
    // จุดหยุดของ Ax — ต้องอยู่ที่หรือก่อนจุดหักเลี้ยว ไม่งั้นรถจะเริ่มเลี้ยวก่อนแล้วค่อยหยุด = ไปจอดขวางกลางแยก
    // ตั้งไว้ที่จุดหักพอดี เพื่อให้คิวค้างอยู่ใน "เลนนอก" ตามที่สังเกตหน้างาน (แล้วไปขวาง A2 ต่อได้)
    public const float AX_STOP_X = AX_TURN_X;
    public const float STOP_B_X = 9.5f;
    public const float STOP_C_Z = 10.9f;

    // ===== กลไก "แซงไหล่ทาง" — ปิดใช้งานแล้ว (ดูเหตุผลด้านล่าง) =====
    //
    // ⚠️ ขาเข้าฝั่งตะวันออกมี 2 แถวเท่านั้น: เลนใน (A) กับเลนนอก (Ax ใช้ร่วมกับ A2)
    //    ไม่มีไหล่ทางให้แซงเป็นแถวที่ 3 — ยืนยันจากหน้างานโดย Kimmy
    //
    // เหตุผลเชิงโมเดลที่ปิดด้วย (สำคัญกว่าเรื่องภาพ): ค่า A2_SHARE = 18% ที่ใช้อยู่นั้น Itim
    // **back-calculate มาจากอัตราการบล็อกที่สังเกตจริง 40-60%** โดยใช้โมเดล car-following ล้วน ๆ
    // แปลว่า 18% + ไม่มีการแซง = ได้อัตราบล็อก 40-60% ตามที่สังเกตอยู่แล้ว
    // ถ้าใส่การแซง 65% ทับเข้าไปอีก = หักลบผลเดียวกันสองรอบ อัตราบล็อกจะเหลือแค่ ~14-21% ต่ำกว่าที่วัดได้จริงมาก
    //
    // โค้ดกลไกยังอยู่ครบ (CarAgent.canBypassCrossLaneBlocker / bypassLateralOffset) เผื่ออยาก
    // เปิดใช้ทีหลังเมื่อมีข้อมูลแยกต่างหาก — แต่ถ้าจะเปิด **ต้องตั้ง offset ให้มากกว่า lookAheadAngle เสมอ**
    // ไม่งั้นรถจะวิ่งทะลุผ่านกันเหมือนบั๊กรอบที่ 6 (Test6 บังคับกฎข้อนี้ไว้แล้ว)
    public const float AX_BYPASS_OFFSET_Z = 0f;
    public const float AX_BYPASS_CHANCE   = 0f;
    // ROI เลนแชร์กลับมาใช้ความกว้างปกติ เพราะไม่มีแถวไหล่ทางให้ครอบแล้ว
    public const float ROI_WIDTH_SHARED = ROI_WIDTH;

    public const float SPAWN_FAR   = 200f;
    public const float ROI_LEN     = 100f;
    public const float ROI_WIDTH   = 3f;
    public const float ROAD_WIDTH  = 18f;   // ความกว้างถนนบางนา-ตราดในซีน (ใช้ตรวจว่าไหล่ทางไม่หลุดขอบ)
    public const float CAR_WIDTH   = 2f;    // ความกว้างตัวรถโดยประมาณ (ใช้ตรวจระยะเฉียดกัน)
    public const float CAR_Y       = 0.5f;
    public const float DESPAWN     = 300f;

    static Transform tLights, tStops, tRois, tSpawners, tSpawnPts, tWaypoints, tEnv;

    // =====================================================================
    [MenuItem("ABAC/Build 4-Phase Scene", false, 0)]
    public static void BuildScene()
    {
        if (!EditorUtility.DisplayDialog("ABAC Scene Builder",
            "จะสร้าง/อัปเดต object ของแยก 4 ไฟ 7 เลน ในซีนปัจจุบัน (เพิ่มเลน A2 = shared lane กับ Ax)\n\n" +
            "• ของเดิมที่ชื่อตรงกันจะถูกใช้ต่อ ไม่สร้างซ้ำ\n" +
            "• เลน C เดิมจะถูกเปลี่ยนเป็น CB\n" +
            "• ตำแหน่งเลน A และ B จะถูกขยับเพื่อเปิดที่ให้ช่องเลี้ยว\n" +
            "• Fixed preset = cycle 107s ตามค่าที่วัดจริง (A 40 / B+Bx 21 / CB 31 + intergreen 15)\n" +
            "• ⭐ A2 กับ Ax รวมมาใช้ ROI เดียวกัน (ROI_A2 เดิมจะถูกลบ)\n" +
            "• ⭐ ขาเข้าฝั่งตะวันออกมี 2 แถวเท่านั้น (A เลนใน / Ax+A2 เลนนอก) — ปิดการแซงไหล่ทางแล้ว\n" +
            "• ⭐ Ax จะมีเส้นหยุดของตัวเอง และหยุดรอตอน Bx เขียว (เลี้ยวเข้ามอไม่ได้)\n\n" +
            "แนะนำให้ commit git ก่อน — ดำเนินการต่อไหม?",
            "สร้างเลย", "ยกเลิก")) return;

        var scene = EditorSceneManager.GetActiveScene();

        tEnv       = GetOrCreateGroup("Environment");
        tLights    = GetOrCreateGroup("TrafficLights");
        tStops     = GetOrCreateGroup("StopLines");
        tRois      = GetOrCreateGroup("ROIZones");
        tSpawners  = GetOrCreateGroup("Spawners");
        tSpawnPts  = GetOrCreateGroup("SpawnPoints");
        tWaypoints = GetOrCreateGroup("Waypoints");

        BuildRoads();

        // ---------- ไฟจราจร 4 ดวง ----------
        var lightA  = BuildLight("Light_A",  "A",  new Vector3(-11.5f, 1.9f,  9.5f));
        var lightB  = BuildLight("Light_B",  "B",  new Vector3( 10.5f, 1.9f, -8.0f));
        var lightBx = BuildLight("Light_Bx", "Bx", new Vector3( 13.5f, 1.9f, -8.0f));
        var lightCB = BuildLight("Light_CB", "CB", new Vector3(  7.0f, 1.9f, 12.0f));

        // ---------- เส้นหยุด ----------
        var stopA  = BuildStopLine("StopLine_A",  new Vector3(STOP_A_X, 0f, Z_A),  true);
        var stopB  = BuildStopLine("StopLine_B",  new Vector3(STOP_B_X, 0f, Z_B),  true);
        var stopBx = BuildStopLine("StopLine_Bx", new Vector3(STOP_B_X, 0f, Z_BX), true);
        var stopCB = BuildStopLine("StopLine_CB", new Vector3(X_CB, 0f, STOP_C_Z), false);
        // ⭐ เส้นหยุดของเลนนอก — ตอนนี้เป็นเส้นหยุด "จริง" ของ Ax แล้ว ไม่ใช่แค่เส้นตกแต่ง
        // Ax ต้องหยุดตรงนี้ตอน Bx เขียว (เลี้ยวเข้ามอไม่ได้) คิวจึงค้างอยู่ในเลนนอกและไปขวาง A2 ต่อได้
        // A2 ยังใช้ stopA (x=-10.5) เป็น stopLineTarget เหมือนเดิม — ต่างกัน 0.5 ม. ไม่มีผลในทางปฏิบัติ
        // เพราะรถวิ่งขนานแกน X ระยะเบรกคำนวณจากผลต่าง X อย่างเดียว (ดู CarAgent.signedDistToStopPoint)
        DestroyIfExists("StopLine_A2_Visual");   // เส้นตกแต่งเดิม เลิกใช้แล้ว
        var stopAx = BuildStopLine("StopLine_Ax", new Vector3(AX_STOP_X, 0f, Z_AX), true);

        // ---------- ROI (100 ม. จ่อเส้นหยุด) ----------
        // ⭐ A2 กับ Ax ใช้ ROI "ตัวเดียวกัน" — เพราะมันคือเลนกายภาพเดียวกันจริง ๆ
        //    กล้อง/เครื่องตรวจจับตัวจริงแยกไม่ออกว่าคันไหนจะวิ่งตรงคันไหนจะเลี้ยวเข้ามอ
        //    (ของเดิมแยกเป็น 2 ROI ซ้อนตำแหน่งกัน = ให้ข้อมูลที่ detector จริงไม่มีทางรู้ได้)
        //    สถิติเวลารอยังแยกราย movement ได้ เพราะ ROIZone.LogVehicle ใช้ car.assignedLane
        var roiA  = BuildROI("ROI_A",  "A",  0, new Vector3(STOP_A_X - ROI_LEN/2f, 1.5f, Z_A),  true);
        // ROI ของเลนนอก — ครอบทั้ง Ax และ A2 (เลนกายภาพเดียวกัน) จัดกลางที่ z = Z_AX + offset/2
        // ตอนนี้ offset = 0 จึงเป็น ROI ปกติที่ z=6.5 กว้าง 3 ม. เหมือนเลนอื่น
        var roiAx = BuildROI("ROI_Ax", "Ax", 1,
            new Vector3(STOP_A_X - ROI_LEN/2f, 1.5f, Z_AX + AX_BYPASS_OFFSET_Z/2f), true, ROI_WIDTH_SHARED);
        var roiB  = BuildROI("ROI_B",  "B",  2, new Vector3(STOP_B_X + ROI_LEN/2f, 1.5f, Z_B),  true);
        var roiBx = BuildROI("ROI_Bx", "Bx", 3, new Vector3(STOP_B_X + ROI_LEN/2f, 1.5f, Z_BX), true);
        var roiCB = BuildROI("ROI_CB", "CB", 4, new Vector3(X_CB, 1.5f, STOP_C_Z + ROI_LEN/2f), false);
        var roiCA = BuildROI("ROI_CA", "CA", 5, new Vector3(X_CA, 1.5f, STOP_C_Z + ROI_LEN/2f), false);
        DestroyIfExists("ROI_A2");   // เลิกใช้ — รวมเข้ากับ ROI_Ax แล้ว (ถ้าปล่อยไว้จะนับ/บันทึกซ้ำสองรอบ)
        roiAx.alsoCountLanes = new[] { "A2" };
        EditorUtility.SetDirty(roiAx);

        // ---------- waypoint เลนเลี้ยว ----------
        // ---------- waypoint เลนเลี้ยว ----------
        // วางตามส่วนโค้งรัศมี 7 ม. (0° / 30° / 60° / 90° ของวงเลี้ยว) แล้วต่อด้วยทางตรงไกล ๆ
        // ตรวจด้วยการจำลองเชิงตัวเลขแล้วว่าทุกเส้นทางอยู่ในเลนของตัวเอง ไม่ทับเลนอื่น
        var wpAx = BuildWaypoints("Ax", new[] {
            new Vector3(AX_TURN_X, CAR_Y, Z_AX),   // จุดเริ่มหักเลี้ยว (= เส้นหยุดของ Ax)
            new Vector3( -7.50f, CAR_Y,  7.44f),   // 30°
            new Vector3( -4.94f, CAR_Y, 10.00f),   // 60°
            new Vector3( -4.00f, CAR_Y, 13.50f),   // 90° เข้าเลนขาเข้า ABAC
            new Vector3( -4.00f, CAR_Y, 90.00f),
        });
        var wpBx = BuildWaypoints("Bx", new[] {
            new Vector3(  5.20f, CAR_Y, -1.20f),
            new Vector3(  1.70f, CAR_Y, -0.26f),
            new Vector3( -0.86f, CAR_Y,  2.30f),
            new Vector3( -1.80f, CAR_Y,  5.80f),
            new Vector3( -1.80f, CAR_Y, 90.00f),
        });
        var wpCB = BuildWaypoints("CB", new[] {
            new Vector3(  1.50f, CAR_Y,  2.50f),
            new Vector3(  0.56f, CAR_Y, -1.00f),
            new Vector3( -2.00f, CAR_Y, -3.56f),
            new Vector3( -5.50f, CAR_Y, -4.50f),   // เข้าเลน B
            new Vector3(-90.00f, CAR_Y, -4.50f),
        });
        // CA จบที่เลน Ax (z=6.5) ซึ่งเป็นเลนซ้ายสุดของฝั่งขาตะวันออก
        // = เลนที่ใกล้ทางออก ABAC ที่สุด ตรงตามกฎจราจรจริง (merge เข้าเลนใกล้ตัวก่อน)
        // และทำให้ CA ไม่ตัดผ่านเส้นทางของ A เลย → เป็นเลนฟรีได้อย่างถูกต้อง
        var wpCA = BuildWaypoints("CA", new[] {
            new Vector3(  3.80f, CAR_Y, 13.50f),   // จุดเริ่มหักเลี้ยว
            new Vector3(  4.74f, CAR_Y, 10.00f),   // 30°
            new Vector3(  7.30f, CAR_Y,  7.44f),   // 60°
            new Vector3( 10.80f, CAR_Y,  6.50f),   // 90° เข้าเลน Ax
            new Vector3( 90.00f, CAR_Y,  6.50f),
        });

        // ---------- spawner ----------
        // สีรถแยกตามกลุ่มเลนใหญ่ A / B / C — ดูแล้วรู้ทันทีว่ารถคันไหนมาจากไหน
        Material matA = GetOrCreateMaterial("Car_LaneA", new Color(0.23f, 0.51f, 0.96f));  // น้ำเงิน
        Material matB = GetOrCreateMaterial("Car_LaneB", new Color(0.96f, 0.62f, 0.07f));  // ส้ม
        Material matC = GetOrCreateMaterial("Car_LaneC", new Color(0.13f, 0.77f, 0.37f));  // เขียว

        var spA  = BuildSpawner("A",  new Vector3(-SPAWN_FAR, CAR_Y, Z_A),   90f, stopA,  lightA,  null, matA);
        // ⭐ Ax ไม่ใช่เลนฟรี 100% อีกแล้ว — หน้างานจริงเลี้ยวเข้ามอไม่ได้ตอน Bx เขียว
        //    (Bx ก็เลี้ยวขวาเข้ามอเหมือนกัน กวาดผ่านปากทางเข้า) จึงผูกกับไฟ Bx แบบ "กลับด้าน":
        //    Bx เขียว/เหลือง = Ax แดง, Bx แดง = Ax ไปได้ตามปกติ
        //    Ax ยังคง isSignalized = false เพราะไม่มีเฟสของตัวเอง ไม่กินเวลาเขียวของใคร
        var spAx = BuildSpawner("Ax", new Vector3(-SPAWN_FAR, CAR_Y, Z_AX),  90f, stopAx, lightBx, wpAx, matA);
        spAx.lightIsInverted = true;
        // กลไกแซงไหล่ทาง — ปิดอยู่ (ทั้งคู่เป็น 0) ดูเหตุผลเต็มที่ค่าคงที่ AX_BYPASS_OFFSET_Z ด้านบน
        // สรุปสั้น: ขาเข้ามี 2 แถวเท่านั้น ไม่มีไหล่ทาง + ค่า 18% ที่ใช้อยู่ calibrate จากอัตราบล็อกจริงมาแล้ว
        spAx.laneBypassChance    = AX_BYPASS_CHANCE;
        spAx.bypassLateralOffset = new Vector3(0f, 0f, AX_BYPASS_OFFSET_Z);
        var spB  = BuildSpawner("B",  new Vector3( SPAWN_FAR, CAR_Y, Z_B),  -90f, stopB,  lightB,  null, matB);
        var spBx = BuildSpawner("Bx", new Vector3( SPAWN_FAR, CAR_Y, Z_BX), -90f, stopBx, lightBx, wpBx, matB);
        var spCB = BuildSpawner("CB", new Vector3(X_CB, CAR_Y, SPAWN_FAR),  180f, stopCB, lightCB, wpCB, matC);
        var spCA = BuildSpawner("CA", new Vector3(X_CA, CAR_Y, SPAWN_FAR),  180f, null,   null,    wpCA, matC);
        // A2 = รถตรงในเลนนอก (เลนแชร์กับ Ax) — จุด spawn ถอยจาก Ax ไป 20 ม.
        // ⚠️ ของเดิมถอยแค่ 5 ม. ซึ่งสั้นกว่าความยาวรถ + ระยะเผื่อ (4 ม. + 2 ม.) แทบไม่มีที่ว่าง
        //    พอ Poisson ปล่อยสองเลนใกล้กันก็เกิดรถซ้อนทับกันทันที — ตอนนี้ถอยเป็น 20 ม. (5 ช่วงรถ)
        //    และมี CarSpawner.spawnClearRadius คอยกันซ้ำอีกชั้นหนึ่ง
        // ไม่มี waypoints (วิ่งตรงเหมือน A), stopLineTarget/
        // assignedLight ใช้ตัวเดียวกับ A เป๊ะ ๆ — ดูคอมเมนต์ที่ BuildStopLine("StopLine_A2_Visual", ...)
        var spA2 = BuildSpawner("A2", new Vector3(-SPAWN_FAR - 20f, CAR_Y, Z_AX), 90f, stopA, lightA, null, matA);

        BuildLaneMarkings();

        // ---------- IntersectionConfig ----------
        var cfgGO = GetOrCreate("_Intersection", null, PrimitiveType.Cube, false);
        var cfg = GetOrAdd<IntersectionConfig>(cfgGO);
        Undo.RecordObject(cfg, "ABAC Build");
        cfg.LoadABACPreset();
        Wire(cfg, "A",  lightA,  roiA,  spA);
        // ใช้ TrafficLight ตัวเดียวกับ "A" และ ROI ตัวเดียวกับ "Ax" โดยตั้งใจทั้งคู่ (shared lane)
        Wire(cfg, "A2", lightA,  roiAx, spA2);
        Wire(cfg, "Ax", null,    roiAx, spAx);
        Wire(cfg, "B",  lightB,  roiB,  spB);
        Wire(cfg, "Bx", lightBx, roiBx, spBx);
        Wire(cfg, "CB", lightCB, roiCB, spCB);
        Wire(cfg, "CA", null,    roiCA, spCA);
        cfg.Build();
        EditorUtility.SetDirty(cfg);

        // ---------- controllers ----------
        var gc = FindByName("GameController") ?? GetOrCreate("GameController", null, PrimitiveType.Cube, false);

        var fixedC = GetOrAdd<FixedController>(gc);
        Undo.RecordObject(fixedC, "ABAC Build");
        fixedC.config = cfg;
        fixedC.LoadABACPreset();
        EditorUtility.SetDirty(fixedC);

        var adaptC = GetOrAdd<AdaptiveController>(gc);
        Undo.RecordObject(adaptC, "ABAC Build");
        adaptC.config = cfg;
        EditorUtility.SetDirty(adaptC);

        var logger = GetOrAdd<DataLogger>(gc);

        var sm = GetOrAdd<ScenarioManager>(gc);
        Undo.RecordObject(sm, "ABAC Build");
        sm.config = cfg;
        sm.fixedController = fixedC;
        sm.adaptiveController = adaptC;
        sm.dataLogger = logger;
        sm.LoadABACScenarioPreset();
        EditorUtility.SetDirty(sm);

        // ---------- Control Panel (UI ตอน runtime) ----------
        var panel = GetOrAdd<SimulationControlPanel>(gc);
        Undo.RecordObject(panel, "ABAC Build");
        panel.config = cfg;
        panel.fixedController = fixedC;
        panel.adaptiveController = adaptC;
        panel.dataLogger = logger;
        panel.scenarioManager = sm;
        panel.LoadDefaultTestCases();
        EditorUtility.SetDirty(panel);

        // ---------- Batch Run Panel (UI ฝั่งขวา โผล่เองตอนกดเริ่ม batch) ----------
        var batchPanel = GetOrAdd<BatchRunPanel>(gc);
        Undo.RecordObject(batchPanel, "ABAC Build");
        batchPanel.scenarioManager = sm;
        EditorUtility.SetDirty(batchPanel);

        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("<b>[ABACSceneBuilder] สร้างซีนเสร็จแล้ว</b>\n" +
                  $"• 7 เลน / 4 ไฟ (A2 ใช้ร่วมกับ A) / cycle {fixedC.GetCycleLength():F0}s\n" +
                  $"• Control Panel พร้อม test case {panel.testCases.Count} ชุด (กด Play แล้วเห็นเลย)\n" +
                  "• Batch Run Panel ต่อแล้ว — กดเริ่ม batch จะโผล่ทางขวาเอง\n" +
                  "• กด ABAC ▸ Validate Scene เพื่อตรวจซ้ำ\n" +
                  "• อย่าลืม Ctrl+S บันทึกซีน");

        cfg.ValidateConfiguration();
        Selection.activeGameObject = cfgGO;
    }

    // =====================================================================
    [MenuItem("ABAC/Validate Scene", false, 1)]
    public static void ValidateScene()
    {
        var cfg = Object.FindFirstObjectByType<IntersectionConfig>();
        if (cfg == null) { Debug.LogError("[ABAC] ไม่พบ IntersectionConfig ในซีน — กด Build ก่อน"); return; }
        cfg.ValidateConfiguration();

        var fc = Object.FindFirstObjectByType<FixedController>();
        if (fc != null) Debug.Log($"[ABAC] Fixed cycle length = {fc.GetCycleLength():F0}s");

        int problems = 0;
        foreach (var lane in cfg.lanes)
        {
            if (lane?.spawner == null) continue;
            var sp = lane.spawner;
            if (sp.carPrefab == null) { Debug.LogError($"[ABAC] Spawner {lane.laneName} ไม่มี carPrefab"); problems++; }
            if (sp.spawnPoint == null) { Debug.LogError($"[ABAC] Spawner {lane.laneName} ไม่มี spawnPoint"); problems++; }
            bool turning = lane.laneName != "A" && lane.laneName != "A2" && lane.laneName != "B";
            if (turning && (sp.pathWaypoints == null || sp.pathWaypoints.Length == 0))
            { Debug.LogWarning($"[ABAC] Spawner {lane.laneName} เป็นเลนเลี้ยวแต่ไม่มี waypoint"); }
            if (lane.isSignalized && sp.stopLineTarget == null)
            { Debug.LogError($"[ABAC] เลนมีไฟ {lane.laneName} ต้องมี stopLineTarget"); problems++; }
            if (lane.isSignalized && sp.lightIsInverted)
            { Debug.LogError($"[ABAC] เลนที่มีไฟของตัวเอง {lane.laneName} ห้ามตั้ง lightIsInverted " +
                             "(ใช้ได้เฉพาะเลนให้ทางที่ไม่มีเฟสของตัวเอง)"); problems++; }

            if (!lane.isSignalized)
            {
                if (sp.lightIsInverted)
                {
                    // เลน "ให้ทาง" เช่น Ax — ไม่มีเฟสของตัวเอง แต่ต้องหยุดตอนอีกกระแสได้ไฟเขียว
                    if (sp.stopLineTarget == null)
                    { Debug.LogError($"[ABAC] เลนให้ทาง {lane.laneName} ต้องมี stopLineTarget (ไว้ให้หยุดรอ)"); problems++; }
                    if (sp.assignedLight == null)
                    { Debug.LogError($"[ABAC] เลนให้ทาง {lane.laneName} ต้องมี assignedLight (ไฟของกระแสที่เราต้องให้ทาง)"); problems++; }
                    else Debug.Log($"[ABAC] เลน {lane.laneName} = เลนให้ทาง → หยุดเมื่อไฟ '{sp.assignedLight.laneName}' ไม่แดง");
                }
                else
                {
                    if (sp.stopLineTarget != null)
                    { Debug.LogError($"[ABAC] เลนฟรี {lane.laneName} ห้ามมี stopLineTarget (รถจะจอดแช่ตลอดไป)"); problems++; }
                    if (sp.assignedLight != null)
                    { Debug.LogError($"[ABAC] เลนฟรี {lane.laneName} ห้ามมี assignedLight"); problems++; }
                }
            }
        }

        // ===== ตรวจ ROI ที่ใช้ร่วมกันหลายเลน =====
        foreach (var lane in cfg.lanes)
        {
            if (lane?.roi == null) continue;
            if (!lane.roi.Accepts(lane.laneName))
            {
                Debug.LogError($"[ABAC] เลน {lane.laneName} ชี้ไปที่ ROI '{lane.roi.name}' แต่ ROI นั้นไม่ได้นับเลนนี้ " +
                               $"(laneName='{lane.roi.laneName}', alsoCountLanes ไม่มี '{lane.laneName}') → demand จะเป็น 0 ตลอด");
                problems++;
            }
        }

        // เตือนถ้ามี ROI ซ้ำที่นับเลนเดียวกันสองที่ = บันทึกสถิติซ้ำ
        var allRois = Object.FindObjectsByType<ROIZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var lane in cfg.lanes)
        {
            if (lane == null) continue;
            int n = 0;
            foreach (var r in allRois) if (r != null && r.Accepts(lane.laneName)) n++;
            if (n > 1)
            {
                Debug.LogError($"[ABAC] เลน {lane.laneName} ถูกนับโดย ROI {n} ตัว → เวลารอจะถูกบันทึกซ้ำ {n} เท่า " +
                               "(ลบ ROI ที่เกินออกจากซีน)");
                problems++;
            }
        }
        Debug.Log(problems == 0 ? "[ABAC] ✓ ตรวจ spawner ผ่านทั้งหมด" : $"[ABAC] พบปัญหา spawner {problems} จุด");
    }

    // =====================================================================
    // helpers
    // =====================================================================

    static void Wire(IntersectionConfig cfg, string lane, TrafficLight light, ROIZone roi, CarSpawner sp)
    {
        var l = cfg.FindLane(lane);
        if (l == null) { Debug.LogError($"[ABAC] preset ไม่มีเลน {lane}"); return; }
        l.trafficLight = light;
        l.roi = roi;
        l.spawner = sp;
    }

    /// <summary>ลบ object เก่าที่เลิกใช้แล้ว (idempotent — ไม่มีอยู่ก็ไม่เป็นไร)</summary>
    static void DestroyIfExists(string name)
    {
        var go = FindByName(name);
        if (go != null)
        {
            Debug.Log($"[ABAC] ลบ '{name}' ที่เลิกใช้แล้วออกจากซีน");
            Undo.DestroyObjectImmediate(go);
        }
    }

    static Transform GetOrCreateGroup(string name)
    {
        var go = FindByName(name);
        if (go == null)
        {
            go = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(go, "ABAC Build");
        }
        go.transform.SetParent(null);
        return go.transform;
    }

    static GameObject GetOrCreate(string name, Transform parent, PrimitiveType prim, bool usePrimitive)
    {
        var go = FindByName(name);
        if (go == null)
        {
            go = usePrimitive ? GameObject.CreatePrimitive(prim) : new GameObject(name);
            go.name = name;
            Undo.RegisterCreatedObjectUndo(go, "ABAC Build");
        }
        if (parent != null && go.transform.parent != parent)
            Undo.SetTransformParent(go.transform, parent, "ABAC Build");
        return go;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        var c = go.GetComponent<T>();
        if (c == null) c = Undo.AddComponent<T>(go);
        return c;
    }

    /// <summary>ค้น GameObject ตามชื่อทั้งซีน รวม object ที่ inactive</summary>
    static GameObject FindByName(string name)
    {
        foreach (var root in EditorSceneManager.GetActiveScene().GetRootGameObjects())
        {
            if (root.name == name) return root;
            var found = FindRecursive(root.transform, name);
            if (found != null) return found;
        }
        return null;
    }

    static GameObject FindRecursive(Transform t, string name)
    {
        for (int i = 0; i < t.childCount; i++)
        {
            var c = t.GetChild(i);
            if (c.name == name) return c.gameObject;
            var found = FindRecursive(c, name);
            if (found != null) return found;
        }
        return null;
    }

    // ---------- roads ----------
    static void BuildRoads()
    {
        var ew = GetOrCreate("Road_EastWest", tEnv, PrimitiveType.Cube, true);
        Undo.RecordObject(ew.transform, "ABAC Build");
        ew.transform.position   = new Vector3(ABAC_X, -0.05f, ROAD_Z);
        ew.transform.localScale = new Vector3(400f, 0.1f, ROAD_WIDTH);   // ขยายจาก 8 -> 18 เพื่อให้มีช่องเลี้ยว
        ApplyMaterial(ew, "Assets/Materials/Road_Mat.mat");
        StripCollider(ew);

        var ab = GetOrCreate("Road_ABAC", tEnv, PrimitiveType.Cube, true);
        Undo.RecordObject(ab.transform, "ABAC Build");
        ab.transform.position   = new Vector3(ABAC_X, -0.05f, 100f);
        ab.transform.localScale = new Vector3(14f, 0.1f, 200f);   // ขยายจาก 8 -> 14 (2 เลนเข้า + 2 เลนออก)
        ApplyMaterial(ab, "Assets/Materials/Road_Mat.mat");
        StripCollider(ab);

        var ground = FindByName("Ground");
        if (ground != null)
        {
            Undo.RecordObject(ground.transform, "ABAC Build");
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
        }

        // ป้อมยาม 2 หลัง ขยับออกนอกถนนที่กว้างขึ้น
        MoveIfExists("ABAC_Building_Left",  new Vector3( 9.5f, 2.9f, 12f));
        MoveIfExists("ABAC_Building_Right", new Vector3(-10.5f, 2.9f, 12f));
    }

    static void MoveIfExists(string name, Vector3 pos)
    {
        var go = FindByName(name);
        if (go == null) return;
        Undo.RecordObject(go.transform, "ABAC Build");
        go.transform.position = pos;
    }

    // ---------- lights ----------
    static TrafficLight BuildLight(string goName, string lane, Vector3 pos)
    {
        // เลน C เดิม -> เปลี่ยนชื่อเป็น CB
        if (goName == "Light_CB")
        {
            var old = FindByName("Light_C");
            if (old != null && FindByName("Light_CB") == null) { Undo.RecordObject(old, "ABAC Build"); old.name = "Light_CB"; }
        }

        var go = GetOrCreate(goName, tLights, PrimitiveType.Cylinder, true);
        Undo.RecordObject(go.transform, "ABAC Build");
        go.transform.position   = pos;
        go.transform.localScale = new Vector3(0.5f, 1.5f, 0.5f);
        StripCollider(go);

        var tl = GetOrAdd<TrafficLight>(go);
        Undo.RecordObject(tl, "ABAC Build");
        tl.laneName = lane;
        if (tl.redMat    == null) tl.redMat    = LoadMat("Assets/Materials/Light_Red.mat");
        if (tl.yellowMat == null) tl.yellowMat = LoadMat("Assets/Materials/Light_Yellow.mat");
        if (tl.greenMat  == null) tl.greenMat  = LoadMat("Assets/Materials/Light_Green.mat");
        tl.assignedCars = new List<CarAgent>();
        EditorUtility.SetDirty(tl);
        return tl;
    }

    // ---------- stop lines ----------
    static Transform BuildStopLine(string goName, Vector3 pos, bool alongX)
    {
        if (goName == "StopLine_CB")
        {
            var old = FindByName("StopLine_C");
            if (old != null && FindByName("StopLine_CB") == null) { Undo.RecordObject(old, "ABAC Build"); old.name = "StopLine_CB"; }
        }

        var go = GetOrCreate(goName, tStops, PrimitiveType.Cube, true);
        Undo.RecordObject(go.transform, "ABAC Build");
        go.transform.position = pos;
        // แถบขาวขวางเลน: เลน A/B ขวางตามแกน Z, เลน CB ขวางตามแกน X
        go.transform.localScale = alongX ? new Vector3(0.3f, 0.05f, ROI_WIDTH)
                                         : new Vector3(ROI_WIDTH, 0.05f, 0.3f);
        StripCollider(go);
        return go.transform;
    }

    // ---------- ROI ----------
    static ROIZone BuildROI(string goName, string lane, int guiSlot, Vector3 pos, bool alongX,
                           float width = ROI_WIDTH)
    {
        // ROI_C เดิม (laneName "C") -> CB
        if (goName == "ROI_CB")
        {
            var old = FindByName("ROI_C");
            if (old != null && FindByName("ROI_CB") == null) { Undo.RecordObject(old, "ABAC Build"); old.name = "ROI_CB"; }
        }

        var go = GetOrCreate(goName, tRois, PrimitiveType.Cube, true);
        Undo.RecordObject(go.transform, "ABAC Build");
        go.transform.position = pos;
        go.transform.localScale = alongX ? new Vector3(ROI_LEN, 3f, width)
                                         : new Vector3(width, 3f, ROI_LEN);

        // ซ่อนกล่อง แต่ยังเห็น gizmo สีเหลืองใน Scene view
        var rend = go.GetComponent<Renderer>();
        if (rend != null) { Undo.RecordObject(rend, "ABAC Build"); rend.enabled = false; }

        // ⚠️ ของเดิมมี collider 2 ตัว (trigger + solid) — เหลือ trigger ตัวเดียวพอ
        var cols = go.GetComponents<BoxCollider>();
        for (int i = cols.Length - 1; i >= 1; i--) Undo.DestroyObjectImmediate(cols[i]);
        var col = go.GetComponent<BoxCollider>() ?? Undo.AddComponent<BoxCollider>(go);
        Undo.RecordObject(col, "ABAC Build");
        col.isTrigger = true;
        col.center = Vector3.zero;
        col.size = Vector3.one;

        var roi = GetOrAdd<ROIZone>(go);
        Undo.RecordObject(roi, "ABAC Build");
        roi.laneName = lane;
        roi.guiSlot = guiSlot;
        roi.showDebugGUI = true;
        EditorUtility.SetDirty(roi);
        return roi;
    }

    // ---------- waypoints ----------
    static Transform[] BuildWaypoints(string lane, Vector3[] points)
    {
        var parent = GetOrCreate("WP_" + lane, tWaypoints, PrimitiveType.Cube, false).transform;
        var result = new Transform[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            var go = GetOrCreate($"WP_{lane}_{i + 1}", parent, PrimitiveType.Cube, false);
            Undo.RecordObject(go.transform, "ABAC Build");
            go.transform.position = points[i];
            result[i] = go.transform;
        }
        return result;
    }

    // ---------- spawners ----------
    static CarSpawner BuildSpawner(string lane, Vector3 spawnPos, float rotY,
                                   Transform stopLine, TrafficLight light, Transform[] waypoints,
                                   Material carMat = null)
    {
        // ของเดิมชื่อเพี้ยน (มีอักขระไทยปน) — หาโดยดูจาก laneName ของ component แทนชื่อ object
        CarSpawner existing = null;
        foreach (var sp in Object.FindObjectsByType<CarSpawner>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (sp.laneName == lane || (lane == "CB" && sp.laneName == "C")) { existing = sp; break; }
        }

        GameObject go;
        if (existing != null) { go = existing.gameObject; go.name = "Spawner_" + lane; }
        else go = GetOrCreate("Spawner_" + lane, tSpawners, PrimitiveType.Cube, false);
        if (go.transform.parent != tSpawners) Undo.SetTransformParent(go.transform, tSpawners, "ABAC Build");

        var spawner = GetOrAdd<CarSpawner>(go);

        // spawn point
        var spName = "SpawnPoint_" + lane;
        var spGO = (spawner.spawnPoint != null) ? spawner.spawnPoint.gameObject
                                                : GetOrCreate(spName, tSpawnPts, PrimitiveType.Cube, false);
        spGO.name = spName;
        if (spGO.transform.parent != tSpawnPts) Undo.SetTransformParent(spGO.transform, tSpawnPts, "ABAC Build");
        Undo.RecordObject(spGO.transform, "ABAC Build");
        spGO.transform.position = spawnPos;

        Undo.RecordObject(spawner, "ABAC Build");
        spawner.laneName        = lane;
        spawner.spawnPoint      = spGO.transform;
        spawner.stopLineTarget  = stopLine;
        spawner.assignedLight   = light;
        spawner.spawnRotationY  = rotY;
        spawner.pathWaypoints   = waypoints;
        spawner.steerRate       = 55f;
        spawner.turnRadius      = 7f;
        spawner.despawnDistance = DESPAWN;
        if (carMat != null) spawner.carMaterial = carMat;
        if (spawner.carPrefab == null)
            spawner.carPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Car.prefab");
        EditorUtility.SetDirty(spawner);
        return spawner;
    }

    // ---------- materials ----------

    /// <summary>โหลด material จาก Assets/Materials ถ้าไม่มีก็สร้างใหม่แล้วบันทึกเป็นไฟล์</summary>
    static Material GetOrCreateMaterial(string name, Color color, float smoothness = 0.3f)
    {
        string path = $"Assets/Materials/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;

        Shader sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null)
        {
            Debug.LogWarning($"[ABAC] หา shader ไม่เจอ ข้ามการสร้าง {name}");
            return null;
        }

        m = new Material(sh);
        m.color = color;
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);

        AssetDatabase.CreateAsset(m, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ABAC] สร้าง material ใหม่: {path}");
        return m;
    }

    // ---------- lane markings ----------

    /// <summary>
    /// เส้นแบ่งเลนสีขาวแบบถนนจริง
    ///   • เส้นทึบ  = เส้นกึ่งกลางถนน (ห้ามข้าม) และเส้นขอบทาง
    ///   • เส้นประ = แบ่งเลนที่วิ่งทิศเดียวกัน (เปลี่ยนเลนได้)
    /// เว้นช่วงกลางแยกไว้ ไม่ลากทับ เหมือนแยกจริง
    /// </summary>
    static void BuildLaneMarkings()
    {
        var root = GetOrCreate("LaneMarkings", null, PrimitiveType.Cube, false).transform;
        // ล้างของเดิมก่อน กันซ้อนตอนกดปุ่มซ้ำ
        for (int i = root.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);

        Material white = GetOrCreateMaterial("LaneMarking_White", new Color(0.95f, 0.95f, 0.92f), 0.1f);

        const float MARK_Y = 0.02f;   // ลอยเหนือผิวถนนนิดหน่อย กัน z-fighting
        const float W = 0.22f;        // ความกว้างเส้น
        const float EXT = 90f;        // ลากไกลจากแยกเท่าไหร่

        // กรอบสี่แยก — ไม่ลากเส้นทับบริเวณนี้
        float jxMin = -8.2f, jxMax = 7.2f;      // ความกว้างถนน ABAC
        float jzMax = 10.2f;                    // ขอบเหนือของถนนหลัก = จุดเริ่มเส้นถนน ABAC

        // ===== ถนนหลัก (ลากตามแกน X) =====
        SolidLineX(root, white, "Center_EW",  ROAD_Z,   -EXT, EXT, W * 1.3f, MARK_Y, jxMin, jxMax);
        SolidLineX(root, white, "Edge_North", 9.60f,    -EXT, EXT, W,        MARK_Y, jxMin, jxMax);
        SolidLineX(root, white, "Edge_South", -7.85f,   -EXT, EXT, W,        MARK_Y, jxMin, jxMax);
        DashLineX (root, white, "Dash_Ax_A",  (Z_AX + Z_A) / 2f,  -EXT, EXT, W, MARK_Y, jxMin, jxMax);
        DashLineX (root, white, "Dash_Bx_B",  (Z_BX + Z_B) / 2f,  -EXT, EXT, W, MARK_Y, jxMin, jxMax);

        // ===== ถนนเข้า ABAC (ลากตามแกน Z ทางเหนือของแยก) =====
        float zStart = jzMax, zEnd = 95f;
        SolidLineZ(root, white, "Center_ABAC", (X_BX_IN + X_CB) / 2f, zStart, zEnd, W * 1.3f, MARK_Y);
        SolidLineZ(root, white, "Edge_ABAC_W", -7.10f,                zStart, zEnd, W,        MARK_Y);
        SolidLineZ(root, white, "Edge_ABAC_E",  6.05f,                zStart, zEnd, W,        MARK_Y);
        DashLineZ (root, white, "Dash_ABAC_In",  (X_AX_IN + X_BX_IN) / 2f, zStart, zEnd, W, MARK_Y);
        DashLineZ (root, white, "Dash_ABAC_Out", (X_CB + X_CA) / 2f,       zStart, zEnd, W, MARK_Y);

        Debug.Log($"[ABAC] วาดเส้นแบ่งเลน {root.childCount} ชิ้น");
    }

    static GameObject MarkPiece(Transform parent, Material mat, string name,
                                Vector3 pos, Vector3 scale)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        Undo.RegisterCreatedObjectUndo(go, "ABAC Build");
        go.transform.SetParent(parent);
        go.transform.position = pos;
        go.transform.localScale = scale;
        foreach (var c in go.GetComponents<Collider>()) Undo.DestroyObjectImmediate(c);
        var r = go.GetComponent<Renderer>();
        if (r != null && mat != null) r.sharedMaterial = mat;
        return go;
    }

    static void SolidLineX(Transform p, Material m, string n, float z,
                           float x0, float x1, float w, float y, float skipMin, float skipMax)
    {
        // ตัดเป็น 2 ท่อน เว้นช่วงกลางแยก
        if (x0 < skipMin) MarkPiece(p, m, n + "_L",
            new Vector3((x0 + skipMin) / 2f, y, z), new Vector3(skipMin - x0, 0.02f, w));
        if (x1 > skipMax) MarkPiece(p, m, n + "_R",
            new Vector3((skipMax + x1) / 2f, y, z), new Vector3(x1 - skipMax, 0.02f, w));
    }

    static void SolidLineZ(Transform p, Material m, string n, float x,
                           float z0, float z1, float w, float y)
    {
        MarkPiece(p, m, n, new Vector3(x, y, (z0 + z1) / 2f), new Vector3(w, 0.02f, z1 - z0));
    }

    static void DashLineX(Transform p, Material m, string n, float z,
                          float x0, float x1, float w, float y, float skipMin, float skipMax)
    {
        const float DASH = 4f, GAP = 4f;
        int i = 0;
        for (float x = x0; x < x1; x += DASH + GAP)
        {
            float cx = x + DASH / 2f;
            if (cx > skipMin - DASH && cx < skipMax + DASH) continue;   // ข้ามกลางแยก
            MarkPiece(p, m, $"{n}_{i++}", new Vector3(cx, y, z), new Vector3(DASH, 0.02f, w));
        }
    }

    static void DashLineZ(Transform p, Material m, string n, float x,
                          float z0, float z1, float w, float y)
    {
        const float DASH = 4f, GAP = 4f;
        int i = 0;
        for (float z = z0; z < z1; z += DASH + GAP)
        {
            MarkPiece(p, m, $"{n}_{i++}", new Vector3(x, y, z + DASH / 2f), new Vector3(w, 0.02f, DASH));
        }
    }

    // ---------- misc ----------
    static Material LoadMat(string path)
    {
        var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m == null) Debug.LogWarning($"[ABAC] ไม่พบ material: {path}");
        return m;
    }

    static void ApplyMaterial(GameObject go, string path)
    {
        var rend = go.GetComponent<Renderer>();
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (rend != null && mat != null) rend.sharedMaterial = mat;
    }

    static void StripCollider(GameObject go)
    {
        foreach (var c in go.GetComponents<Collider>()) Undo.DestroyObjectImmediate(c);
    }
}
#endif
