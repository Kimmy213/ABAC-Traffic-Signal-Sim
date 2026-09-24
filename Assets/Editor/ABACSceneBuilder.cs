#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

/// <summary>
/// Builds or updates the ABAC scene: 4 signals, 7 lanes (A2 shares A's signal and Ax's lane).
/// Menu: ABAC > Build 4-Phase Scene
///
/// Idempotent - press it again and existing objects are reused rather than duplicated.
/// Supports Undo (Ctrl+Z), but commit to git first anyway.
///
/// ===== Coordinates taken from the real site =====
/// Bangna-Trad Road runs along X (centreline z = 0.88).
/// The ABAC entrance is to the +Z side (north).
/// A  = eastbound (+X), on the northern half (left-hand traffic).
/// B  = westbound (-X), on the southern half.
/// ABAC is on A's left  -> Ax turns left, free.
/// ABAC is on B's right -> Bx turns right and needs a signal (it cuts the A stream).
/// Leaving ABAC heading south: left = east = merges into lane A, free -> CA
///                             right = west = crosses lane A to merge into B -> CB (signalised)
/// </summary>
public static class ABACSceneBuilder
{
    // ---------- geometry ----------
    public const float ROAD_Z   = 0.88f;    // centreline of the east-west road
    public const float ABAC_X   = -0.52f;   // centreline of the ABAC access road

    public const float Z_AX = 6.5f;    // left turn into ABAC (outermost on the north side)
    public const float Z_A  = 3.0f;    // eastbound through lane
    public const float Z_BX = -1.2f;   // right turn into ABAC (next to the centreline, south side)
    public const float Z_B  = -4.5f;   // westbound through lane

    public const float X_AX_IN = -4.0f;   // ABAC inbound lane fed by Ax
    public const float X_BX_IN = -1.8f;   // ABAC inbound lane fed by Bx
    public const float X_CB    = 1.5f;    // ABAC outbound lane turning right (west)
    public const float X_CA    = 3.8f;    // ABAC outbound lane turning left (east)

    public const float STOP_A_X = -10.5f;
    // Stop point of the outer lane (Ax) - must be exactly the same as the inner lane (A) stop line.
    // It used to be -11.0 against A at -10.5, and the 0.5 m offset showed in game as two staggered
    // white lines. In reality one approach has one stop line, so it is tied to STOP_A_X (a test locks this).
    public const float AX_STOP_X = STOP_A_X;
    // Where Ax begins its turn - the same point as the stop line (cars stop there, then turn out)
    public const float AX_TURN_X = AX_STOP_X;

    // ===== Where A2 merges into the shared lane (late merge) =====
    // Itim counted from the clip: all 3 through vehicles entering the shared lane changed with about
    // five car lengths left before the turn. Five car lengths ~ 5 x (4 m + 2 m gap) = 30 m, so 20 m is
    // used as the point where the merge completes, with edging starting 22 m before that.
    //
    // This value matters more than it looks: it is what makes A2 cut in at the head of the queue rather
    //    than spread through it. Merging from far back gives frequent mild blocking, which is wrong -
    //    the real pattern is rare but severe. The shared lane is about 5 car lengths, roughly 32 m
    //    (Itim confirmed, SP1_HANDOFF_v2 section 5.6), and the A2 lane change has to happen entirely
    //    within it, not before the shared lane exists.
    public const float SHARED_LANE_LENGTH = 32f;
    public const float A2_MERGE_X    = STOP_A_X - 20f;      // merge completes 20 m before the stop line
    public const float A2_MERGE_DIST = SHARED_LANE_LENGTH - 20f;   // = 12 m, so edging starts right at the shared lane edge
    public const float STOP_B_X = 9.5f;
    public const float STOP_C_Z = 10.9f;

    // ===== Shoulder-passing mechanism - disabled (reasons below) =====
    //
    // The eastbound approach has only two rows: inner (A) and outer (Ax shared with A2).
    //    There is no shoulder for a third row - confirmed on site by Kimmy.
    //
    // The modelling reason matters more than the visual one: the A2_SHARE value of 18% was
    // back-calculated by Itim from an observed 40-60% blocking rate using a pure car-following model,
    // so 18% with no passing already reproduces the observed blocking. Adding 65% passing on top would
    // discount the same effect twice, dropping blocking to ~14-21%, far below what was measured.
    //
    // The mechanism is still in the code (CarAgent.canBypassCrossLaneBlocker / bypassLateralOffset) in
    // case separate data ever justifies it - but if re-enabled, the offset must always exceed
    // lookAheadAngle, or cars drive through each other as in bug round 6 (Test6 enforces this).
    public const float AX_BYPASS_OFFSET_Z = 0f;
    public const float AX_BYPASS_CHANCE   = 0f;
    // The shared-lane ROI is back to the normal width, since there is no shoulder row to cover
    public const float ROI_WIDTH_SHARED = ROI_WIDTH;

    public const float SPAWN_FAR   = 200f;
    public const float ROI_LEN     = 100f;
    public const float ROI_WIDTH   = 3f;
    public const float ROAD_WIDTH  = 18f;   // width of Bangna-Trad Road in the scene (checks the shoulder stays on the road)
    // Car width, computed from the real model car_low_poly.fbx: 879.6 wide : 1636.9 long in model units,
    // so scaled to 4.00 m long per CarAgent.carLength it is 879.6/1636.9*4 = 2.15 m wide.
    public const float CAR_WIDTH   = 2.15f;
    public const float CAR_Y       = 0.5f;
    public const float DESPAWN     = 300f;

    static Transform tLights, tStops, tRois, tSpawners, tSpawnPts, tWaypoints, tEnv;

    // =====================================================================
    [MenuItem("ABAC/Build 4-Phase Scene", false, 0)]
    public static void BuildScene()
    {
        if (!EditorUtility.DisplayDialog("ABAC Scene Builder",
            "This will create or update the objects for the 4-signal, 7-lane junction in the current scene (adding lane A2, shared with Ax).\n\n" +
            "- existing objects with matching names are reused, not duplicated\n" +
            "- the old lane C is renamed CB\n" +
            "- lanes A and B move to make room for the turning lanes\n" +
            "- Fixed preset = 107s cycle from the measured values (A 40 / B+Bx 21 / CB 31 + intergreen 15)\n" +
            "- A2 and Ax share one ROI (the old ROI_A2 is deleted)\n" +
            "- the eastbound approach has only two rows (A inner / Ax+A2 outer) - shoulder passing is off\n" +
            "- Ax gets its own stop line and waits while Bx is green (it cannot turn in)\n\n" +
            "Committing to git first is recommended. Continue?",
            "Build", "Cancel")) return;

        // Let Unity import any files just copied in (PNG textures, FBX models) before using them
        AssetDatabase.Refresh();

        var scene = EditorSceneManager.GetActiveScene();

        tEnv       = GetOrCreateGroup("Environment");
        tLights    = GetOrCreateGroup("TrafficLights");
        tStops     = GetOrCreateGroup("StopLines");
        tRois      = GetOrCreateGroup("ROIZones");
        tSpawners  = GetOrCreateGroup("Spawners");
        tSpawnPts  = GetOrCreateGroup("SpawnPoints");
        tWaypoints = GetOrCreateGroup("Waypoints");

        BuildRoads();

        // ---------- Four traffic signals ----------
        var lightA  = BuildLight("Light_A",  "A",  new Vector3(-11.5f, 1.9f,  9.5f));
        var lightB  = BuildLight("Light_B",  "B",  new Vector3( 10.5f, 1.9f, -8.0f));
        var lightBx = BuildLight("Light_Bx", "Bx", new Vector3( 13.5f, 1.9f, -8.0f));
        var lightCB = BuildLight("Light_CB", "CB", new Vector3(  7.0f, 1.9f, 12.0f));

        // ---------- Stop lines ----------
        var stopA  = BuildStopLine("StopLine_A",  new Vector3(STOP_A_X, 0f, Z_A),  true);
        var stopB  = BuildStopLine("StopLine_B",  new Vector3(STOP_B_X, 0f, Z_B),  true);
        var stopBx = BuildStopLine("StopLine_Bx", new Vector3(STOP_B_X, 0f, Z_BX), true);
        var stopCB = BuildStopLine("StopLine_CB", new Vector3(X_CB, 0f, STOP_C_Z), false);
        // The outer lane stop line is now a real stop line for Ax, not just decoration.
        // Ax has to stop here while Bx is green (it cannot turn in), so its queue stays in the outer
        // lane and can go on blocking A2. A2 still uses stopA (x=-10.5) as its stopLineTarget - the
        // 0.5 m difference has no practical effect, since cars run parallel to X (see signedDistToStopPoint).
        DestroyIfExists("StopLine_A2_Visual");   // the old decorative line, no longer used
        var stopAx = BuildStopLine("StopLine_Ax", new Vector3(AX_STOP_X, 0f, Z_AX), true);

        // ---------- ROI (100 m back from the stop line) ----------
        // A2 and Ax use one and the same ROI, because they really are one physical lane.
        //    A real camera cannot tell which vehicle will go straight and which will turn into the campus.
        //    (There used to be two overlapping ROIs, handing the controller information a real detector
        //    could never have.) Wait statistics still separate by movement, via ROIZone.LogVehicle.
        var roiA  = BuildROI("ROI_A",  "A",  0, new Vector3(STOP_A_X - ROI_LEN/2f, 1.5f, Z_A),  true);
        // ROI for the outer lane, covering both Ax and A2 (one physical lane), centred at z = Z_AX + offset/2.
        // The offset is 0 now, so it is an ordinary 3 m wide ROI at z=6.5 like the others.
        var roiAx = BuildROI("ROI_Ax", "Ax", 1,
            new Vector3(STOP_A_X - ROI_LEN/2f, 1.5f, Z_AX + AX_BYPASS_OFFSET_Z/2f), true, ROI_WIDTH_SHARED);
        var roiB  = BuildROI("ROI_B",  "B",  2, new Vector3(STOP_B_X + ROI_LEN/2f, 1.5f, Z_B),  true);
        var roiBx = BuildROI("ROI_Bx", "Bx", 3, new Vector3(STOP_B_X + ROI_LEN/2f, 1.5f, Z_BX), true);
        var roiCB = BuildROI("ROI_CB", "CB", 4, new Vector3(X_CB, 1.5f, STOP_C_Z + ROI_LEN/2f), false);
        var roiCA = BuildROI("ROI_CA", "CA", 5, new Vector3(X_CA, 1.5f, STOP_C_Z + ROI_LEN/2f), false);
        DestroyIfExists("ROI_A2");   // no longer used - merged into ROI_Ax (leaving it would double-count)
        roiAx.alsoCountLanes = new[] { "A2" };
        EditorUtility.SetDirty(roiAx);

        // ---------- Waypoints for the turning lanes ----------
        // Placed along a 7 m radius arc (0 / 30 / 60 / 90 degrees of the turn), then a long straight.
        // Verified by numerical simulation that every path stays inside its own lane.
        //
        var wpAx = BuildWaypoints("Ax", new[] {
            new Vector3(AX_TURN_X, CAR_Y, Z_AX),   // start of the turn (= the Ax stop line)
            new Vector3( -7.50f, CAR_Y,  7.44f),   // 30°
            new Vector3( -4.94f, CAR_Y, 10.00f),   // 60°
            new Vector3( -4.00f, CAR_Y, 13.50f),   // 90 degrees, into the ABAC inbound lane
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
            new Vector3( -5.50f, CAR_Y, -4.50f),   // into lane B
            new Vector3(-90.00f, CAR_Y, -4.50f),
        });
        // CA ends in lane Ax (z=6.5), the leftmost lane of the eastbound side and the one nearest the
        // ABAC exit, which matches real road rules (merge into the nearest lane first) and means CA
        // never crosses A's path, so it can correctly be a free lane.
        var wpCA = BuildWaypoints("CA", new[] {
            new Vector3(  3.80f, CAR_Y, 13.50f),   // start of the turn
            new Vector3(  4.74f, CAR_Y, 10.00f),   // 30°
            new Vector3(  7.30f, CAR_Y,  7.44f),   // 60°
            new Vector3( 10.80f, CAR_Y,  6.50f),   // 90 degrees, into lane Ax
            new Vector3( 90.00f, CAR_Y,  6.50f),
        });

        // ---------- spawner ----------
        // Car colours by lane group A / B / C, so it is obvious at a glance where a car came from.
        // Real car paint is far glossier than a default material, so smoothness and metallic are set
        Material matA = GetOrCreateMaterial("Car_LaneA", new Color(0.23f, 0.51f, 0.96f), 0.78f, null, 1f, 1f, 0.35f);
        Material matB = GetOrCreateMaterial("Car_LaneB", new Color(0.96f, 0.62f, 0.07f), 0.78f, null, 1f, 1f, 0.35f);
        Material matC = GetOrCreateMaterial("Car_LaneC", new Color(0.13f, 0.77f, 0.37f), 0.78f, null, 1f, 1f, 0.35f);

        var spA  = BuildSpawner("A",  new Vector3(-SPAWN_FAR, CAR_Y, Z_A),   90f, stopA,  lightA,  null, matA);
        // Ax is no longer a fully free lane: on site it cannot turn into the campus while Bx is green
        //    (Bx turns in as well and sweeps across the entrance), so it is tied to the Bx signal, inverted:
        //    Bx green or yellow = Ax red, Bx red = Ax may go.
        //    Ax still has isSignalized = false, because it has no phase of its own and consumes no green time.
        var spAx = BuildSpawner("Ax", new Vector3(-SPAWN_FAR, CAR_Y, Z_AX),  90f, stopAx, lightBx, wpAx, matA);
        spAx.lightIsInverted = true;
        // Shoulder passing is off (both values 0) - full reasoning at the AX_BYPASS_OFFSET_Z constant above.
        // In short: the approach has only two rows and no shoulder, and the 18% figure is already calibrated.
        spAx.laneBypassChance    = AX_BYPASS_CHANCE;
        spAx.bypassLateralOffset = new Vector3(0f, 0f, AX_BYPASS_OFFSET_Z);
        var spB  = BuildSpawner("B",  new Vector3( SPAWN_FAR, CAR_Y, Z_B),  -90f, stopB,  lightB,  null, matB);
        var spBx = BuildSpawner("Bx", new Vector3( SPAWN_FAR, CAR_Y, Z_BX), -90f, stopBx, lightBx, wpBx, matB);
        var spCB = BuildSpawner("CB", new Vector3(X_CB, CAR_Y, SPAWN_FAR),  180f, stopCB, lightCB, wpCB, matC);
        var spCA = BuildSpawner("CA", new Vector3(X_CA, CAR_Y, SPAWN_FAR),  180f, null,   null,    wpCA, matC);
        // A2 = through traffic using the outer lane (shared with Ax). Its spawn point is in the inner lane, 20 m behind A.
        // It used to be only 5 m back, less than a car length plus margin (4 m + 2 m), leaving almost no
        //    room, so Poisson releasing on two lanes at once immediately overlapped cars.
        //    It is 20 m (five car lengths) now, with CarSpawner.spawnClearRadius as a second guard.
        // No waypoints (it runs straight like A). stopLineTarget and assignedLight are exactly the same
        // objects as A's - see the comment at BuildStopLine("StopLine_A2_Visual", ...).
        // A2 spawns in the inner lane (Z_A) like ordinary through traffic and changes into the shared
        //    lane near the stop line, rather than spawning in it - see the A2_MERGE_X constant.
        var wpA2 = BuildWaypoints("A2", new[] {
            new Vector3(A2_MERGE_X, CAR_Y, Z_AX),    // where the merge into the shared lane completes
            new Vector3(SPAWN_FAR,  CAR_Y, Z_AX),    // then straight on in the shared lane
        });
        var spA2 = BuildSpawner("A2", new Vector3(-SPAWN_FAR - 20f, CAR_Y, Z_A), 90f, stopA, lightA, wpA2, matA);
        spA2.steerBeforeStopLine = true;
        spA2.steerStartDistance  = A2_MERGE_DIST;

        // ---------- Car model ----------
        // Rebuilds Car.prefab from the real 3D model every time and hands it to every spawner.
        // (With no model file the function returns the existing prefab rather than failing.)
        GameObject carPrefab = CarPrefabBuilder.Rebuild(matA);
        if (carPrefab != null)
        {
            foreach (var sp in new[] { spA, spA2, spAx, spB, spBx, spCB, spCA })
            {
                if (sp == null) continue;
                Undo.RecordObject(sp, "ABAC Build");
                sp.carPrefab = carPrefab;
                EditorUtility.SetDirty(sp);
            }
        }

        BuildLaneMarkings();

        // ---------- IntersectionConfig ----------
        var cfgGO = GetOrCreate("_Intersection", null, PrimitiveType.Cube, false);
        var cfg = GetOrAdd<IntersectionConfig>(cfgGO);
        Undo.RecordObject(cfg, "ABAC Build");
        cfg.LoadABACPreset();
        Wire(cfg, "A",  lightA,  roiA,  spA);
        // Deliberately uses the same TrafficLight object as "A" and the same ROI as "Ax" (shared lane)
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
        // This must be called the way FixedController.LoadABACPreset() does, or the values serialized
        //    in the scene (the old 1 / 2.5 / 8, for instance) persist even after the .cs defaults change.
        adaptC.LoadMeasuredPreset();
        EditorUtility.SetDirty(adaptC);

        var logger = GetOrAdd<DataLogger>(gc);

        var sm = GetOrAdd<ScenarioManager>(gc);
        Undo.RecordObject(sm, "ABAC Build");
        // Same reason as AdaptiveController: without this call the stale scene values (reps=1, speed=2,
        //    share=0.18) persist, giving a batch of 10 runs instead of 270 and a retired lane share.
        sm.LoadBatchPreset();
        sm.config = cfg;
        sm.fixedController = fixedC;
        sm.adaptiveController = adaptC;
        sm.dataLogger = logger;
        sm.LoadABACScenarioPreset();
        EditorUtility.SetDirty(sm);

        // ---------- Control Panel (runtime UI) ----------
        var panel = GetOrAdd<SimulationControlPanel>(gc);
        Undo.RecordObject(panel, "ABAC Build");
        panel.config = cfg;
        panel.fixedController = fixedC;
        panel.adaptiveController = adaptC;
        panel.dataLogger = logger;
        panel.scenarioManager = sm;
        panel.LoadDefaultTestCases();
        EditorUtility.SetDirty(panel);

        // ---------- Custom test case builder (press N at runtime to open) ----------
        var custom = GetOrAdd<CustomTestCase>(gc);
        Undo.RecordObject(custom, "ABAC Build");
        custom.config = cfg;
        custom.controlPanel = panel;
        EditorUtility.SetDirty(custom);

        // ---------- Camera shots ----------
        BuildCameraRig(gc);

        // ---------- Batch Run Panel (right-hand UI, appears when a batch starts) ----------
        var batchPanel = GetOrAdd<BatchRunPanel>(gc);
        Undo.RecordObject(batchPanel, "ABAC Build");
        batchPanel.scenarioManager = sm;
        EditorUtility.SetDirty(batchPanel);

        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("<b>[ABACSceneBuilder] scene built</b>\n" +
                  $"- 7 lanes / 4 signals (A2 shares A) / cycle {fixedC.GetCycleLength():F0}s\n" +
                  $"- Control Panel with {panel.testCases.Count} test cases (visible as soon as you press Play)\n" +
                  "- Batch Run Panel wired up - it appears on the right when a batch starts\n" +
                  "- 8 camera shots with a button bar along the bottom ([ ] changes shot, V follows a car, C returns to overview)\n" +
                  "- run ABAC > Validate Scene to check everything\n" +
                  "- remember to save the scene with Ctrl+S");

        cfg.ValidateConfiguration();
        Selection.activeGameObject = cfgGO;
    }

    // =====================================================================
    [MenuItem("ABAC/Validate Scene", false, 1)]
    public static void ValidateScene()
    {
        var cfg = Object.FindFirstObjectByType<IntersectionConfig>();
        if (cfg == null) { Debug.LogError("[ABAC] no IntersectionConfig in the scene - run Build first"); return; }
        cfg.ValidateConfiguration();

        var fc = Object.FindFirstObjectByType<FixedController>();
        if (fc != null) Debug.Log($"[ABAC] Fixed cycle length = {fc.GetCycleLength():F0}s");

        int problems = 0;
        foreach (var lane in cfg.lanes)
        {
            if (lane?.spawner == null) continue;
            var sp = lane.spawner;
            if (sp.carPrefab == null) { Debug.LogError($"[ABAC] spawner {lane.laneName} has no carPrefab"); problems++; }
            if (sp.spawnPoint == null) { Debug.LogError($"[ABAC] spawner {lane.laneName} has no spawnPoint"); problems++; }
            // A2 has waypoints too, but for a lane change rather than a turn - it needs them either way
            bool turning = lane.laneName != "A" && lane.laneName != "B";
            if (turning && (sp.pathWaypoints == null || sp.pathWaypoints.Length == 0))
            { Debug.LogWarning($"[ABAC] spawner {lane.laneName} is a turning lane but has no waypoints"); }
            if (lane.isSignalized && sp.stopLineTarget == null)
            { Debug.LogError($"[ABAC] signalised lane {lane.laneName} must have a stopLineTarget"); problems++; }
            if (lane.isSignalized && sp.lightIsInverted)
            { Debug.LogError($"[ABAC] lane {lane.laneName} has its own signal and must not set lightIsInverted " +
                             "(that is only for a yielding lane with no phase of its own)"); problems++; }

            if (!lane.isSignalized)
            {
                if (sp.lightIsInverted)
                {
                    // A yielding lane such as Ax: no phase of its own, but it must stop while the other stream is green
                    if (sp.stopLineTarget == null)
                    { Debug.LogError($"[ABAC] yielding lane {lane.laneName} needs a stopLineTarget to wait at"); problems++; }
                    if (sp.assignedLight == null)
                    { Debug.LogError($"[ABAC] yielding lane {lane.laneName} needs an assignedLight (the signal it yields to)"); problems++; }
                    else Debug.Log($"[ABAC] lane {lane.laneName} yields - it stops whenever signal '{sp.assignedLight.laneName}' is not red");
                }
                else
                {
                    if (sp.stopLineTarget != null)
                    { Debug.LogError($"[ABAC] free lane {lane.laneName} must not have a stopLineTarget (cars would wait forever)"); problems++; }
                    if (sp.assignedLight != null)
                    { Debug.LogError($"[ABAC] free lane {lane.laneName} must not have an assignedLight"); problems++; }
                }
            }
        }

        // ===== Check ROIs shared between lanes =====
        foreach (var lane in cfg.lanes)
        {
            if (lane?.roi == null) continue;
            if (!lane.roi.Accepts(lane.laneName))
            {
                Debug.LogError($"[ABAC] lane {lane.laneName} points at ROI '{lane.roi.name}' but that ROI does not count this lane " +
                               $"(laneName='{lane.roi.laneName}', alsoCountLanes has no '{lane.laneName}') - demand will always read 0");
                problems++;
            }
        }

        // Warn if two ROIs count the same lane, which would record its statistics twice
        var allRois = Object.FindObjectsByType<ROIZone>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var lane in cfg.lanes)
        {
            if (lane == null) continue;
            int n = 0;
            foreach (var r in allRois) if (r != null && r.Accepts(lane.laneName)) n++;
            if (n > 1)
            {
                Debug.LogError($"[ABAC] lane {lane.laneName} is counted by {n} ROIs - its wait time will be recorded {n} times " +
                               "(remove the extra ROIs from the scene)");
                problems++;
            }
        }
        Debug.Log(problems == 0 ? "[ABAC] all spawner checks passed" : $"[ABAC] found {problems} spawner problem(s)");
    }

    // =====================================================================
    // helpers
    // =====================================================================

    static void Wire(IntersectionConfig cfg, string lane, TrafficLight light, ROIZone roi, CarSpawner sp)
    {
        var l = cfg.FindLane(lane);
        if (l == null) { Debug.LogError($"[ABAC] the preset has no lane {lane}"); return; }
        l.trafficLight = light;
        l.roi = roi;
        l.spawner = sp;
    }

    /// <summary>Removes a retired object (idempotent - fine if it does not exist)</summary>
    static void DestroyIfExists(string name)
    {
        var go = FindByName(name);
        if (go != null)
        {
            Debug.Log($"[ABAC] removed the retired '{name}' from the scene");
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

    /// <summary>Finds a GameObject by name anywhere in the scene, including inactive ones</summary>
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

    // ---------- camera ----------

    /// <summary>
    /// Sets up a camera shot for every approach and exit, computed from the lane coordinates in this file,
    /// so moving a lane or stop line and pressing Build moves the cameras with it.
    ///
    /// Placement principle: stand back from the stop line along the lane, high enough to see over the
    /// car roofs, tilted slightly down, so one frame shows both the waiting queue and the junction.
    /// </summary>
    static void BuildCameraRig(GameObject gc)
    {
        var dir = GetOrAdd<CameraDirector>(gc);
        Undo.RecordObject(dir, "ABAC Build");

        Camera cam = null;
        var camGO = FindByName("Main Camera");
        if (camGO != null) cam = camGO.GetComponent<Camera>();
        if (cam == null) cam = Object.FindFirstObjectByType<Camera>();
        dir.targetCamera = cam;

        dir.shots = MakeShots();
        dir.showBar = true;
        EditorUtility.SetDirty(dir);

        if (cam == null)
            Debug.LogWarning("[ABAC] no camera in the scene - CameraDirector will find Camera.main itself on Play");
    }

    /// <summary>
    /// The camera shot table, kept as pure static data so tests can check the angles without a scene.
    ///
    /// The idea: every shot sits at the centre of the T-junction and only the heading and height change,
    ///    like one PTZ camera on a mast in the middle, turning to look at each approach.
    ///
    ///    Besides looking tidy, this matches what the project actually proposes - the drone is only a
    ///    data-collection tool, and the system to be installed is a camera on a mast at the junction
    ///    (see SP1_REPORT_MATERIAL, System scope). These are the views the real camera would have.
    ///
    /// yaw -90 = facing west (the A/Ax approach coming towards us)
    /// yaw  90 = facing east (the B/Bx approach)
    /// yaw   0 = facing north (the campus exit, CB/CA)
    /// </summary>
    public static List<CameraDirector.Shot> MakeShots()
    {
        float jx = ABAC_X, jz = ROAD_Z;          // centre of the T-junction
        float zMidA = (Z_A + Z_AX) / 2f;         // midway between the inner and outer westbound-approach lanes
        float zMidB = (Z_B + Z_BX) / 2f;         // midway on the eastbound approach
        float xMidC = (X_CB + X_CA) / 2f;        // midway between the two campus exit lanes

        const float MAST = 8.5f;                 // mast height for approach views (clearly above car roofs)
        const float LOW  = 6.0f;                 // single-lane views sit lower, so the queue reads as a long row

        return new List<CameraDirector.Shot>
        {
            // ---- Overview: straight down on the junction, every queue visible at once ----
            new CameraDirector.Shot {
                label = "Overview", lane = "", canFollow = false, fov = 55f,
                position = new Vector3(jx, 48f, jz), euler = new Vector3(90f, 0f, 0f) },


            // ---- Facing west: the A + Ax approach coming towards the camera ----
            new CameraDirector.Shot {
                label = "Approach A / Ax", lane = "A", fov = 62f,
                position = new Vector3(jx, MAST, zMidA), euler = new Vector3(10f, -90f, 0f) },

            // ---- Shared lane close-up: where A2 waits at the red and traps Ax behind it ----
            new CameraDirector.Shot {
                label = "Shared Lane", lane = "Ax", fov = 55f, followBack = 9f, followUp = 4f,
                position = new Vector3(jx, LOW, Z_AX), euler = new Vector3(8f, -90f, 0f) },

            // ---- Facing east: the B + Bx approach ----
            new CameraDirector.Shot {
                label = "Approach B / Bx", lane = "B", fov = 62f,
                position = new Vector3(jx, MAST, zMidB), euler = new Vector3(10f, 90f, 0f) },

            // ---- Bx close-up: the real bottleneck at v/c 1.51, queue reaching the next junction in 5.7 min ----
            new CameraDirector.Shot {
                label = "Bx Bottleneck", lane = "Bx", fov = 55f, followBack = 9f, followUp = 4f,
                position = new Vector3(jx, LOW, Z_BX), euler = new Vector3(8f, 90f, 0f) },

            // ---- Facing north: the campus exits CB + CA ----
            new CameraDirector.Shot {
                label = "ABAC Exit", lane = "CB", fov = 62f, followBack = 9f, followUp = 4f,
                position = new Vector3(xMidC, MAST, jz), euler = new Vector3(10f, 0f, 0f) },
        };
    }

    // ---------- roads ----------
    // Size of one texture tile on the road surface, in metres - smaller means a denser pattern
    const float TILE_ROAD   = 8f;
    const float TILE_GROUND = 10f;

    static void BuildRoads()
    {
        // Asphalt: matte, low reflectance, not metallic
        Material road = GetOrCreateMaterial("Road_Mat", new Color(0.72f, 0.72f, 0.74f), 0.12f,
                                            "Asphalt", 400f / TILE_ROAD, ROAD_WIDTH / TILE_ROAD);
        Material roadAbac = GetOrCreateMaterial("Road_ABAC_Mat", new Color(0.72f, 0.72f, 0.74f), 0.12f,
                                            "Asphalt", 14f / TILE_ROAD, 200f / TILE_ROAD);
        Material grass = GetOrCreateMaterial("Ground_Mat", new Color(0.85f, 0.90f, 0.85f), 0.05f,
                                            "Grass", 600f / TILE_GROUND, 600f / TILE_GROUND);

        var ew = GetOrCreate("Road_EastWest", tEnv, PrimitiveType.Cube, true);
        Undo.RecordObject(ew.transform, "ABAC Build");
        ew.transform.position   = new Vector3(ABAC_X, -0.05f, ROAD_Z);
        ew.transform.localScale = new Vector3(400f, 0.1f, ROAD_WIDTH);   // widened from 8 to 18 to fit the turning lanes
        SetMaterial(ew, road);
        StripCollider(ew);

        var ab = GetOrCreate("Road_ABAC", tEnv, PrimitiveType.Cube, true);
        Undo.RecordObject(ab.transform, "ABAC Build");
        ab.transform.position   = new Vector3(ABAC_X, -0.05f, 100f);
        ab.transform.localScale = new Vector3(14f, 0.1f, 200f);   // widened from 8 to 14 (2 lanes in + 2 out)
        SetMaterial(ab, roadAbac);
        StripCollider(ab);

        var ground = FindByName("Ground");
        if (ground != null)
        {
            Undo.RecordObject(ground.transform, "ABAC Build");
            ground.transform.localScale = new Vector3(60f, 1f, 60f);
            SetMaterial(ground, grass);
        }

        // Buildings and guard huts: rendered concrete with windows
        Material bld = GetOrCreateMaterial("Building", new Color(0.90f, 0.88f, 0.85f), 0.15f,
                                           "Building", 1f, 1f);
        SetMaterial(FindByName("ABAC_Building_Left"),  bld);
        SetMaterial(FindByName("ABAC_Building_Right"), bld);

        // Two guard huts, moved outwards to clear the widened road
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
        // The old lane C is renamed CB
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
        // White bars across the lane: A/B run along Z, CB runs along X
        go.transform.localScale = alongX ? new Vector3(0.3f, 0.05f, ROI_WIDTH)
                                         : new Vector3(ROI_WIDTH, 0.05f, 0.3f);
        StripCollider(go);
        return go.transform;
    }

    // ---------- ROI ----------
    static ROIZone BuildROI(string goName, string lane, int guiSlot, Vector3 pos, bool alongX,
                           float width = ROI_WIDTH)
    {
        // The old ROI_C (laneName "C") becomes CB
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

        // Hide the box but keep the yellow gizmo visible in the Scene view
        var rend = go.GetComponent<Renderer>();
        if (rend != null) { Undo.RecordObject(rend, "ABAC Build"); rend.enabled = false; }

        // The old version had two colliders (trigger + solid); one trigger is enough
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
        // Some old objects had corrupted names, so they are found by the component laneName instead
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

    /// <summary>Assigns a material to an object (skipped if either is missing)</summary>
    static void SetMaterial(GameObject go, Material m)
    {
        if (go == null || m == null) return;
        var r = go.GetComponent<Renderer>();
        if (r == null) return;
        Undo.RecordObject(r, "ABAC Build");
        r.sharedMaterial = m;
        EditorUtility.SetDirty(r);
    }

    // ---------- textures ----------

    /// <summary>Loads a texture from Assets/Textures/ (pre-made tileable PNG files)</summary>
    static Texture2D Tex(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        string path = $"Assets/Textures/{name}.png";
        Texture2D t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (t == null)
            Debug.LogWarning($"[ABAC] texture {path} not found - falling back to a flat colour " +
                             "(if the file was just copied in, run Assets > Refresh and build again)");
        return t;
    }

    static void ConfigureMaterial(Material m, Color color, float smoothness,
                                  string textureName, float tileX, float tileY, float metallic)
    {
        m.color = color;
        if (m.HasProperty("_BaseColor"))  m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", smoothness);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", smoothness);
        if (m.HasProperty("_Metallic"))   m.SetFloat("_Metallic", metallic);

        Texture2D t = Tex(textureName);
        if (t != null)
        {
            if (m.HasProperty("_MainTex"))  m.SetTexture("_MainTex", t);
            if (m.HasProperty("_BaseMap"))  m.SetTexture("_BaseMap", t);
            m.mainTexture = t;
            m.mainTextureScale = new Vector2(tileX, tileY);
        }
    }

    // ---------- materials ----------

    /// <summary>Loads a material from Assets/Materials, creating and saving one if it does not exist</summary>
    /// <summary>
    /// Creates a material, or updates an existing one to match what the builder specifies.
    ///
    /// The old version returned as soon as it found the file, so a material created earlier could never
    ///    pick up a texture even after a rebuild. It now writes the values every time.
    /// </summary>
    static Material GetOrCreateMaterial(string name, Color color, float smoothness = 0.3f,
                                        string textureName = null, float tileX = 1f, float tileY = 1f,
                                        float metallic = 0f)
    {
        string path = $"Assets/Materials/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null)
        {
            ConfigureMaterial(m, color, smoothness, textureName, tileX, tileY, metallic);
            EditorUtility.SetDirty(m);
            return m;
        }

        Shader sh = Shader.Find("Standard");
        if (sh == null) sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null)
        {
            Debug.LogWarning($"[ABAC] shader not found, skipping creation of {name}");
            return null;
        }

        m = new Material(sh);
        ConfigureMaterial(m, color, smoothness, textureName, tileX, tileY, metallic);

        AssetDatabase.CreateAsset(m, path);
        AssetDatabase.SaveAssets();
        Debug.Log($"[ABAC] created material: {path}");
        return m;
    }

    // ---------- lane markings ----------

    /// <summary>
    /// White lane markings as on a real road:
    ///   - solid  = road centreline (no crossing) and the edge lines
    ///   - dashed = between lanes running the same way (lane changes allowed)
    /// The junction area is left unmarked, as it is in reality.
    /// </summary>
    static void BuildLaneMarkings()
    {
        var root = GetOrCreate("LaneMarkings", null, PrimitiveType.Cube, false).transform;
        // Clear the old markings first, so pressing the button again does not stack them
        for (int i = root.childCount - 1; i >= 0; i--)
            Undo.DestroyObjectImmediate(root.GetChild(i).gameObject);

        Material white = GetOrCreateMaterial("LaneMarking_White", new Color(0.95f, 0.95f, 0.92f), 0.1f);

        const float MARK_Y = 0.02f;   // floats just above the road surface to avoid z-fighting
        const float W = 0.22f;        // line width
        const float EXT = 90f;        // how far from the junction the lines run

        // Junction box - no lines are drawn across this area
        float jxMin = -8.2f, jxMax = 7.2f;      // width of the ABAC road
        float jzMax = 10.2f;                    // north edge of the main road = where the ABAC road markings start

        // ===== Main road (along X) =====
        SolidLineX(root, white, "Center_EW",  ROAD_Z,   -EXT, EXT, W * 1.3f, MARK_Y, jxMin, jxMax);
        SolidLineX(root, white, "Edge_North", 9.60f,    -EXT, EXT, W,        MARK_Y, jxMin, jxMax);
        SolidLineX(root, white, "Edge_South", -7.85f,   -EXT, EXT, W,        MARK_Y, jxMin, jxMax);
        DashLineX (root, white, "Dash_Ax_A",  (Z_AX + Z_A) / 2f,  -EXT, EXT, W, MARK_Y, jxMin, jxMax);
        DashLineX (root, white, "Dash_Bx_B",  (Z_BX + Z_B) / 2f,  -EXT, EXT, W, MARK_Y, jxMin, jxMax);

        // ===== ABAC access road (along Z, north of the junction) =====
        float zStart = jzMax, zEnd = 95f;
        SolidLineZ(root, white, "Center_ABAC", (X_BX_IN + X_CB) / 2f, zStart, zEnd, W * 1.3f, MARK_Y);
        SolidLineZ(root, white, "Edge_ABAC_W", -7.10f,                zStart, zEnd, W,        MARK_Y);
        SolidLineZ(root, white, "Edge_ABAC_E",  6.05f,                zStart, zEnd, W,        MARK_Y);
        DashLineZ (root, white, "Dash_ABAC_In",  (X_AX_IN + X_BX_IN) / 2f, zStart, zEnd, W, MARK_Y);
        DashLineZ (root, white, "Dash_ABAC_Out", (X_CB + X_CA) / 2f,       zStart, zEnd, W, MARK_Y);

        Debug.Log($"[ABAC] drew {root.childCount} lane-marking pieces");
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
        // Split into two sections, leaving the junction clear
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
            if (cx > skipMin - DASH && cx < skipMax + DASH) continue;   // skip the junction
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
        if (m == null) Debug.LogWarning($"[ABAC] material not found: {path}");
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
