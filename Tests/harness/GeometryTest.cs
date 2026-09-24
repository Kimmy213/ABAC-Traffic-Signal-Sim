using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// Checks that the vehicle behaviour parameters (CarAgent/CarSpawner) are numerically consistent
/// with the real scene coordinates (ABACSceneBuilder). It runs outside Unity because it is pure arithmetic.
/// Covers two bugs found from screenshots of the real scene: "Ax stops far short" and "overlapping cars".
public class GeometryTest
{
    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    // Reads a private CarAgent field after the driver profile has been applied
    static float Profile(CarAgent.DriverType type, string field)
    {
        var car = new CarAgent { driverType = type };
        var m = typeof(CarAgent).GetMethod("SetProfileParameters",
                    BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(car, null);
        var f = typeof(CarAgent).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        return (float)f.GetValue(car);
    }

    public static int Main()
    {
        var car = new CarAgent();
        var spawner = new CarSpawner();

        float lat        = car.lookAheadAngle;          // lateral distance still counted as the same lane
        float look       = car.lookAheadDistance;       // look-ahead ceiling
        float carLen     = car.carLength;
        float minGap     = car.minGap;
        float clearR     = spawner.spawnClearRadius;

        float offset     = ABACSceneBuilder.AX_BYPASS_OFFSET_Z;
        float zAx        = ABACSceneBuilder.Z_AX;
        float zA         = ABACSceneBuilder.Z_A;
        float roadZ      = ABACSceneBuilder.ROAD_Z;
        float roadW      = ABACSceneBuilder.ROAD_WIDTH;
        float carW       = ABACSceneBuilder.CAR_WIDTH;
        float roiShared  = ABACSceneBuilder.ROI_WIDTH_SHARED;
        float stopAX     = ABACSceneBuilder.STOP_A_X;

        Console.WriteLine($"lookAheadAngle={lat}  lookAheadDistance={look}  carLength={carLen}  " +
                          $"minGap={minGap}  spawnClearRadius={clearR}  bypassOffset={offset}");
        Console.WriteLine();

        // ===== 1) The eastbound approach must have exactly two rows =====
        Console.WriteLine("--- 1) eastbound approach: A (inner) + Ax/A2 (outer) = 2 rows ---");
        float chance = ABACSceneBuilder.AX_BYPASS_CHANCE;
        var rows = new SortedSet<float> { zA, zAx };
        if (chance > 0f && offset != 0f) rows.Add(zAx + offset);
        Check(rows.Count == 2,
            $"rows of vehicles on the eastbound approach = {rows.Count} (z = {string.Join(", ", rows)}), must be exactly 2");
        Check(!(chance > 0f && offset == 0f),
            "never allow a passing chance (>0) with no lateral offset (=0) - cars would drive through each other, as in bug round 6");

        // ===== 1.1) Rules that must still hold if passing is ever re-enabled =====
        Console.WriteLine("\n--- 1.1) rules that must hold *if* the passing mechanism is turned back on ---");
        if (chance > 0f)
        {
            Check(offset > lat,
                $"offset ({offset} m) > lookAheadAngle ({lat} m) -> a passing car does not see the queue and will not brake for it");
            Check(offset - carW > 0f,
                $"side clearance while passing = {offset - carW:F2} m > 0 -> no overlap");
            float northEdgeOn = roadZ + roadW / 2f;
            Check(zAx + offset + carW / 2f <= northEdgeOn,
                "the outer edge of a passing car is still on the carriageway");
        }
        else
        {
            Check(offset == 0f,
                $"passing is off (chance={chance}) -> the offset must be 0 too (got {offset}), or a phantom row is left in the scene");
            Console.WriteLine("  SKIP  offset and carriageway rules (passing is off - they are checked automatically if it returns)");
        }

        // ===== 2) The outer lane ROI must cover every row vehicles actually use =====
        Console.WriteLine("\n--- 2) ROI_Ax must cover every row the outer lane uses ---");
        float roiCenter = zAx + offset / 2f;
        float roiLo = roiCenter - roiShared / 2f, roiHi = roiCenter + roiShared / 2f;
        Check(roiLo <= zAx && zAx <= roiHi,
            $"the ROI covers the normal lane z={zAx} (range {roiLo:F2}..{roiHi:F2})");
        Check(roiLo <= zAx + offset && zAx + offset <= roiHi,
            $"the ROI covers every outer row including z={zAx + offset:F2}, so no vehicle escapes the wait measurement");
        Check(!(roiLo <= zA && zA <= roiHi),
            $"the outer lane ROI must *not* cover the inner lane z={zA} (range {roiLo:F2}..{roiHi:F2}), or A vehicles get counted");

        // ===== 3) Neighbouring lanes must not interfere =====
        Console.WriteLine("\n--- 3) cars in different lanes must not brake for each other ---");
        float laneGap = Math.Abs(zAx - zA);
        Check(laneGap > lat,
            $"gap between lane A and the shared lane = {laneGap} m > lookAheadAngle ({lat}) -> A does not block Ax/A2, as the model intends");
        Check(Math.Abs((zAx + offset) - zA) > lat,
            "every outer row is still far enough from lane A not to interfere");

        // ===== 4) Fixing "Ax stops far short": the look-ahead must suit the slowest-braking driver =====
        Console.WriteLine("\n--- 4) look-ahead ceiling (fixes the Ax stops-far-short bug) ---");
        foreach (CarAgent.DriverType t in new[] { CarAgent.DriverType.Aggressive,
                                                  CarAgent.DriverType.Normal,
                                                  CarAgent.DriverType.Cautious })
        {
            float v = Profile(t, "approachSpeed");
            float d = Profile(t, "decelRate");
            float brake = v * v / (2f * d);
            Check(look >= brake * 1.15f,
                $"{t}: braking distance {brake:F1} m - look-ahead {look} m, a margin of {look / brake:F2}x");
        }

        // ===== 5) A genuinely blocking queue must always be within the look-ahead =====
        Console.WriteLine("\n--- 5) the queue blocking Ax must sit *before* the turn point, so it is still detected ---");
        // An A2 car stops with its front bumper on the stop line, so its rear is at stopAX - carLen
        float a2Rear = stopAX - carLen;
        float axTurnX = -11.0f;   // the first Ax waypoint
        Check(a2Rear < axTurnX,
            $"the rear of a stopped A2 car (x={a2Rear:F1}) is before the Ax turn point (x={axTurnX:F1}), " +
            "so it is always inside the look-ahead and blocking still works as before");

        // ===== 5.1) Ax is a yielding lane - check where its stop line sits =====
        Console.WriteLine("\n--- 5.1) the Ax stop line (where it waits while Bx is green) ---");
        float axStop = ABACSceneBuilder.AX_STOP_X;
        float axTurn = ABACSceneBuilder.AX_TURN_X;
        Check(axStop <= axTurn + 0.001f,
            $"the Ax stop line (x={axStop}) is not past the turn point (x={axTurn}), so cars do not begin turning and then block the junction");
        // The queue must stay in the outer lane rather than spill into the junction (the chosen behaviour: stop before turning)
        Check(axStop <= stopAX + 0.001f,
            $"the Ax stop line (x={axStop}) is at or before the A2 stop line (x={stopAX}), so the two queues join in one outer lane");
        Check(Math.Abs(axStop - stopAX) <= carLen,
            $"the Ax and A2 stop lines are {Math.Abs(axStop - stopAX):F1} m apart (<= one car length), so they are one queue head with no odd gap");

        // ===== 5.2) The A2 late merge =====
        Console.WriteLine("\n--- 5.2) A2 must merge into the shared lane late, cutting in at the head ---");
        float mergeX = ABACSceneBuilder.A2_MERGE_X;
        float mergeDist = ABACSceneBuilder.A2_MERGE_DIST;
        float carSlot = carLen + minGap;                       // one car slot = the car plus its gap

        Check(mergeX < stopAX,
            $"the merge point (x={mergeX}) is before the A2 stop line (x={stopAX}), so the lane change finishes before the line");

        float slotsBeforeStop = (stopAX - mergeX) / carSlot;
        Check(slotsBeforeStop <= 6f,
            $"the merge completes {stopAX - mergeX:F0} m before the stop line = {slotsBeforeStop:F1} car slots " +
            "(Itim observed about 5 slots, so it must not exceed 6 or it is no longer cutting in at the head)");
        Check(slotsBeforeStop >= 2f,
            $"but not so close to the line ({slotsBeforeStop:F1} slots) that there is no room to edge across");

        // The whole lane-change span must be short enough to count as a late merge
        float totalMergeSpan = mergeDist + (stopAX - mergeX);
        Check(totalMergeSpan / carSlot <= 12f,
            $"the whole lane-change span is {totalMergeSpan:F0} m = {totalMergeSpan / carSlot:F1} car slots " +
            "(any longer and it spreads through the queue instead of cutting in at the head)");

        // The deviation angle must not be so steep it reads as a turn
        double mergeAngle = Math.Atan2(Math.Abs(zAx - zA), mergeDist) * 180.0 / Math.PI;
        Check(mergeAngle < 45.0,
            $"lane-change deviation {mergeAngle:F0} degrees (< 45 counts as a lane change, not a turn, " +
            "so CarAgent.PathTurnsAway() will not clip A2 look-ahead)");

        // ===== 6) Spawn points must not overlap =====
        Console.WriteLine("\n--- 6) the A2 and Ax spawn points (one physical lane) ---");
        float spawnSep = 20f;   // -SPAWN_FAR-20 against -SPAWN_FAR
        Check(spawnSep >= clearR + carLen,
            $"spacing between the A2 and Ax spawn points = {spawnSep} m >= the clearance radius ({clearR}) + car length ({carLen})");
        Check(clearR >= carLen + minGap,
            $"the clearance radius ({clearR}) >= car length + minimum gap ({carLen + minGap}), so no car is dropped on a stopped one");

        // ===== 7) Every camera shot must sit at the junction centre, looking out at one approach =====
        //
        // This invariant changed deliberately: cameras used to stand far off and aim at the junction,
        //    so the test asked whether the aim point was near the junction. They now sit at the centre
        //    and look *out*, so the criterion is "the camera is at the centre" plus "it aims along that lane".
        Console.WriteLine("\n--- 7) cameras at the junction centre, each looking at one approach ---");
        var shots = ABACSceneBuilder.MakeShots();
        Check(shots.Count >= 6, $"{shots.Count} camera shots available");

        float jx = ABACSceneBuilder.ABAC_X, jz = ABACSceneBuilder.ROAD_Z;
        var seenLabels = new HashSet<string>();

        // lane -> (the axis the lane runs along, the lane centre coordinate, the side traffic arrives from)
        //   'z' = the lane runs along X, its position is given by z, upstream is the X side cars come from
        //   'x' = the lane runs along Z, its position is given by x, upstream is the Z side cars come from
        var laneAxis = new Dictionary<string, char>();
        var laneAt   = new Dictionary<string, float>();
        var upstream = new Dictionary<string, float>();
        Action<string,char,float,float> lane = (n, ax, at, up) =>
            { laneAxis[n] = ax; laneAt[n] = at; upstream[n] = up; };
        lane("A",  'z', ABACSceneBuilder.Z_A,  -1f);
        lane("A2", 'z', ABACSceneBuilder.Z_AX, -1f);
        lane("Ax", 'z', ABACSceneBuilder.Z_AX, -1f);
        lane("B",  'z', ABACSceneBuilder.Z_B,  +1f);
        lane("Bx", 'z', ABACSceneBuilder.Z_BX, +1f);
        lane("CB", 'x', ABACSceneBuilder.X_CB, +1f);
        lane("CA", 'x', ABACSceneBuilder.X_CA, +1f);

        foreach (var sh in shots)
        {
            Check(seenLabels.Add(sh.label), $"shot name '{sh.label}' is unique");

            // ---- The camera body must sit at the junction centre (one mast, rotating) ----
            double offJ = Math.Sqrt(Math.Pow(sh.position.x - jx, 2) + Math.Pow(sh.position.z - jz, 2));
            Check(offJ <= 8.0,
                $"'{sh.label}': the camera is {offJ:F1} m from the junction centre (<= 8 counts as the same mast)");
            Check(sh.position.y >= 4f,
                $"'{sh.label}': camera height {sh.position.y:F1} m (must clear the car roofs)");
            Check(sh.euler.x > 0f && sh.euler.x <= 90f,
                $"'{sh.label}': pitch {sh.euler.x:F0} degrees (must look down, not up at the sky)");
            Check(sh.fov >= 30f && sh.fov <= 75f, $"'{sh.label}': FOV {sh.fov:F0}");

            // ---- Where it aims on the ground ----
            double pitch = sh.euler.x * Math.PI / 180.0;
            double yaw   = sh.euler.y * Math.PI / 180.0;
            double fx = Math.Sin(yaw) * Math.Cos(pitch);
            double fz = Math.Cos(yaw) * Math.Cos(pitch);
            double t2 = sh.position.y / Math.Sin(pitch);
            double gx = sh.position.x + fx * t2;
            double gz = sh.position.z + fz * t2;

            if (string.IsNullOrEmpty(sh.lane))
            {
                // Overview or junction shots must aim at the junction itself
                double d = Math.Sqrt((gx - jx) * (gx - jx) + (gz - jz) * (gz - jz));
                Check(d <= 25.0, $"'{sh.label}': an overview shot must aim at the junction ({d:F1} m away)");
                continue;
            }

            Check(sh.canFollow, $"'{sh.label}': it is tied to lane {sh.lane}, so follow mode must be available");
            Check(laneAxis.ContainsKey(sh.lane), $"'{sh.label}': lane {sh.lane} is known");
            if (!laneAxis.ContainsKey(sh.lane)) continue;

            // It must aim along that lane, not the one beside it (lateral distance from the lane centre)
            double sideOff = laneAxis[sh.lane] == 'z'
                ? Math.Abs(gz - laneAt[sh.lane])
                : Math.Abs(gx - laneAt[sh.lane]);
            Check(sideOff <= 4.0,
                $"'{sh.label}': the aim point is {sideOff:F1} m off the centre of lane {sh.lane} " +
                "(<= 4 keeps it in that lane rather than the next one)");

            // It must face the direction traffic arrives from, not turn its back on the queue
            double along = laneAxis[sh.lane] == 'z' ? (gx - jx) : (gz - jz);
            Check(along * upstream[sh.lane] >= 10.0,
                $"'{sh.label}': faces upstream of lane {sh.lane} and looks far enough ({Math.Abs(along):F0} m " +
                "from the junction) to see the approaching queue rather than its back");
        }

        // ===== 8) ROI tracking outline must not reach the vehicle behind =====
        // Regression: the outline used to be a solid box scaled 1.12x the car, and the boxes of
        // queued vehicles blended into one another. It is now an outline sized to the car's own
        // bounds + a tiny clearance pad, which must stay well inside the resting queue pitch.
        {
            var agent = new CarAgent();
            double roiCarLen = agent.carLength;
            double roiMinGap = agent.minGap;
            double boxLen   = CarPrefabBuilder.TargetLength + CarPrefabBuilder.RoiBoxPadding;
            double pitch    = roiCarLen + roiMinGap;              // centre-to-centre spacing at rest
            Console.WriteLine($"\n--- ROI outline ---\n    car {carLen:F2}m  minGap {minGap:F2}m  " +
                              $"pitch {pitch:F2}m  outline {boxLen:F2}m  bar {CarPrefabBuilder.RoiEdgeThickness:F2}m");

            Check(Math.Abs(CarPrefabBuilder.TargetLength - roiCarLen) < 0.001,
                  $"CarPrefabBuilder.TargetLength matches CarAgent.carLength ({roiCarLen:F2} m)");
            Check(boxLen < pitch,
                  $"the ROI box is shorter than the head-to-head spacing of stopped cars, so it never overlaps the one behind ({boxLen:F2} < {pitch:F2} m)");
            Check(pitch - boxLen >= 1.0,
                  $"at least 1 m remains between boxes ({pitch - boxLen:F2} m)");
            Check(CarPrefabBuilder.RoiBoxPadding > 0.0 && CarPrefabBuilder.RoiBoxPadding < 0.3,
                  $"the box padding is small enough not to stick out past the car but large enough to avoid z-fighting ({CarPrefabBuilder.RoiBoxPadding:F2} m)");
            Check(CarPrefabBuilder.RoiEdgeThickness > 0.02 && CarPrefabBuilder.RoiEdgeThickness < 0.25,
                  $"the outline is thick enough to see from a high camera but not so thick it becomes a solid box ({CarPrefabBuilder.RoiEdgeThickness:F2} m)");
            // an outline is only an outline while the bars are a small fraction of the face
            Check(CarPrefabBuilder.RoiEdgeThickness < ABACSceneBuilder.CAR_WIDTH * 0.1,
                  "the outline is thinner than 10% of the car width (still a line, not a slab)");
        }

        // ===== 9) Scene-serialization drift guard =====
        // Itim found that SampleScene.unity stored 1 / 2.5 / 8, overriding the 2.95 / 2.31 / 4 in the code,
        // because Unity serialises a scene component's values and always overrides the script defaults.
        {
            Console.WriteLine("\n--- measured-parameter guard ---");

            Check(Math.Abs(AdaptiveController.MEASURED_STARTUP_LOST_TIME  - 2.95) < 1e-6,
                  "the startup lost time constant = 2.95 s, as measured");
            Check(Math.Abs(AdaptiveController.MEASURED_SATURATION_HEADWAY - 2.31) < 1e-6,
                  "the saturation headway constant = 2.31 s/veh, as measured");
            var probe = new AdaptiveController();
            Check(Math.Abs(probe.collectWindow - 7.0) < 1e-6 && probe.earlyExitDemand == 3,
                  "COLLECT is the only demand accumulator (7 s ceiling / early exit at 3 vehicles)");

            // The textbook values must not be in use - if anyone reverts to HCM or the proposal, this breaks
            Check(Math.Abs(AdaptiveController.MEASURED_SATURATION_HEADWAY - 2.5) > 0.01,
                  "not 2.5 (the value the proposal assumed initially - replaced in section 5.1.1)");
            Check(Math.Abs(AdaptiveController.MEASURED_STARTUP_LOST_TIME - 2.0) > 0.01,
                  "not 2.0 (the HCM default)");
            Check(Math.Abs(AdaptiveController.MEASURED_STARTUP_LOST_TIME - 1.0) > 0.01,
                  "not 1.0 (the value left stuck in the scene, neither measured nor HCM)");

            // LoadMeasuredPreset must clear all three drifted fields
            var ac = new AdaptiveController();
            ac.startupLostTime = 1f; ac.saturationHeadway = 2.5f;
            ac.LoadMeasuredPreset();
            Check(Math.Abs(ac.startupLostTime - 2.95f) < 1e-6
               && Math.Abs(ac.saturationHeadway - 2.31f) < 1e-6,
                  "LoadMeasuredPreset() restores the values from the scene-stuck ones (1 / 2.5)");

            // Enforce must catch the drift and report that it corrected it
            var ac2 = new AdaptiveController();
            ac2.startupLostTime = 1f; ac2.saturationHeadway = 2.5f;
            bool corrected = ac2.EnforceMeasuredParameters();
            Check(corrected, "EnforceMeasuredParameters() reports finding and correcting drifted values");
            Check(Math.Abs(ac2.startupLostTime - 2.95f) < 1e-6
               && Math.Abs(ac2.saturationHeadway - 2.31f) < 1e-6,
                  "the values are the measured ones after Enforce");

            // A correct scene must be left alone (no false positives)
            var ac3 = new AdaptiveController();
            Check(!ac3.EnforceMeasuredParameters(), "a scene already correct must not be reported as corrected");

            // A sensitivity run must be able to disable the guard, or the 13-23% experiment is impossible
            var ac4 = new AdaptiveController();
            ac4.useMeasuredParameters = false;
            ac4.saturationHeadway = 2.0f;
            Check(!ac4.EnforceMeasuredParameters() && Math.Abs(ac4.saturationHeadway - 2.0f) < 1e-6,
                  "with useMeasuredParameters off the values can be set by hand (sensitivity analysis still possible)");

            // The field defaults must be tied to the consts rather than retyping the numbers
            var ac5 = new AdaptiveController();
            Check(Math.Abs(ac5.startupLostTime   - AdaptiveController.MEASURED_STARTUP_LOST_TIME)  < 1e-6
               && Math.Abs(ac5.saturationHeadway - AdaptiveController.MEASURED_SATURATION_HEADWAY) < 1e-6
               && Math.Abs(ac5.collectWindow - 7f) < 1e-6,
                  "the field defaults are tied to the consts (one place to change)");
        }

        // ===== 10) Batch parameters that the scene had overridden =====
        {
            Console.WriteLine("\n--- batch preset guard ---");

            Check(ScenarioManager.BATCH_REPETITIONS == 27, "27 repetitions per scenario");
            Check(Math.Abs(ScenarioManager.BATCH_SIM_SPEED - 10f) < 1e-6, "simulation speed = 10x (safe because CarAgent sub-steps at <= 0.02 s)");
            Check(Math.Abs(ScenarioManager.BATCH_RUN_DURATION - 1800f) < 1e-6, "run duration = 1800 s");
            Check(Math.Abs(ScenarioManager.BATCH_WARMUP - 120f) < 1e-6, "warmup = 120 s");

            // The cross-file invariant that should have caught this bug in the first place:
            //    the batch (ScenarioManager) and cases T1-T13 (TestCaseLibrary) must use the same share
            Check(Math.Abs(ScenarioManager.MEASURED_A2_SHARE - TestCasePresets.A2_SHARE) < 1e-6,
                  $"the batch a2Share equals the test case one ({ScenarioManager.MEASURED_A2_SHARE:F3})");
            Check(Math.Abs(ScenarioManager.MEASURED_A2_SHARE - 0.054f) < 1e-6,
                  "a2Share = 5.4% (direct count, SP1_HANDOFF_v2 section 5)");
            Check(Math.Abs(ScenarioManager.MEASURED_A2_SHARE - 0.18f) > 0.01f,
                  "not 0.18 (the old back-calculated value the scene was stuck on)");

            var sm = new ScenarioManager();
            sm.repetitionsPerScenario = 1; sm.simulationSpeed = 2f; sm.a2Share = 0.18f;
            sm.LoadBatchPreset();
            Check(sm.repetitionsPerScenario == 27 && Math.Abs(sm.simulationSpeed - 10f) < 1e-6
               && Math.Abs(sm.a2Share - 0.054f) < 1e-6,
                  "LoadBatchPreset() clears every value the scene was stuck on (1 / 2 / 0.18)");

            var sm2 = new ScenarioManager();
            sm2.a2Share = 0.18f;
            Check(sm2.EnforceMeasuredShare(), "EnforceMeasuredShare() catches a2Share = 0.18");
            Check(Math.Abs(sm2.a2Share - 0.054f) < 1e-6, "a2Share corrected to 0.054");
            Check(!new ScenarioManager().EnforceMeasuredShare(), "an already-correct value must not be reported as corrected");

            // Expected run count - a scene stuck at reps=1 would give only 10 runs
            var smp = new ScenarioManager();
            smp.LoadABACScenarioPreset();
            int scen = smp.scenarios.Count;
            Console.WriteLine($"    {scen} scenarios x {ScenarioManager.BATCH_REPETITIONS} reps x 2 controllers " +
                              $"= {scen * ScenarioManager.BATCH_REPETITIONS * 2} runs");
            Check(scen == 4, "the preset has 4 scenarios (Night dropped)");
            bool hasNight = false;
            foreach (var sc in smp.scenarios) if (sc.name == "Night") hasNight = true;
            Check(!hasNight, "Night is not in the batch");
            Check(scen * ScenarioManager.BATCH_REPETITIONS * 2 == 216, "4 scenarios -> 216 runs");

            // Sub-stepping: motion resolution must not depend on timeScale
            Console.WriteLine($"    CarAgent.MAX_STEP = {CarAgent.MAX_STEP}s");
            Check(Math.Abs(CarAgent.MAX_STEP - 0.02f) < 1e-6, "maximum step 0.02 s = the Unity fixed timestep");
            Check(CarAgent.SubStepsFor(1f/60f) == 1,            "1x @60fps  -> 1 step");
            Check(CarAgent.SubStepsFor(5f/60f) == 5,            "5x @60fps  -> 5 steps (0.0833 s)");
            Check(CarAgent.SubStepsFor(10f/60f) == 9,           "10x @60fps -> 9 steps (0.167 s) - it used to be one step of 2.3 m");
            Check(CarAgent.SubStepsFor(0.3333f) == 17,          "at the Unity cap of 0.333 s -> 17 steps");
            Check(CarAgent.SubStepsFor(5f) == 50,               "an abnormally long frame -> no more than 50 steps");
            Check(CarAgent.SubStepsFor(0f) == 0,                "dt = 0 -> no steps");
        }

        // ===== 11) Shared-lane length (Itim confirmed 32 m = about 5 car lengths, SP1_HANDOFF_v2 section 5.6) =====
        {
            Console.WriteLine("\n--- shared lane length ---");
            double mergeEnd   = ABACSceneBuilder.STOP_A_X - ABACSceneBuilder.A2_MERGE_X;   // stop line to where the merge completes
            double steerStart = mergeEnd + ABACSceneBuilder.A2_MERGE_DIST;                  // where the lane change begins
            Console.WriteLine($"    shared lane {ABACSceneBuilder.SHARED_LANE_LENGTH:F0} m | " +
                              $"starts at {steerStart:F0} m | merge completes {mergeEnd:F0} m before the stop line");

            Check(Math.Abs(ABACSceneBuilder.SHARED_LANE_LENGTH - 32.0) < 0.01,
                  "shared lane length = 32 m, as measured");
            Check(steerStart <= ABACSceneBuilder.SHARED_LANE_LENGTH + 0.01,
                  $"A2 begins its lane change *within* the shared lane ({steerStart:F0} <= 32 m)");
            Check(mergeEnd > 0 && mergeEnd < steerStart,
                  "the merge completes before the stop line and after the start of the change");
            // It must still be late enough to cut in at the head rather than join the back
            Check(steerStart <= 6.0 * (4.0 + 2.0) + 0.01,
                  "the lane change happens within the last 6 car slots (still a late merge)");
        }

        // ===== 12) A standing queue must hold still and not skid across the line on a red =====
        //   Bug found from a screenshot on 15 Sep: Compare-101 starts in AllRedIdle but the whole row drove off.
        //   The cause was SetProfileParameters setting currentSpeed = approachSpeed on every new car.
        {
            Console.WriteLine("\n--- standing queue ---");
            float margin = CarAgent.REST_FRONT_MARGIN;

            foreach (CarAgent.DriverType t in Enum.GetValues(typeof(CarAgent.DriverType)))
            {
                float overshoot = Profile(t, "stopOvershoot");
                float approach  = Profile(t, "approachSpeed");
                float decel     = Profile(t, "decelRate");

                // signedDistToStopPoint of the head car once placed = margin + stopOvershoot
                float signed = margin + overshoot;
                Check(signed >= -0.3f + 0.01f,
                      $"{t}: the head of the queue does not count as past the line (signed {signed:F2} > -0.3)");

                // How far it would skid if it spawned at cruising speed - confirms why it must start at 0
                float skid = approach * approach / (2f * decel);
                Console.WriteLine($"    {t}: overshoot {overshoot:F1} m | without starting at 0 it would skid {skid:F1} m");
                Check(skid > margin,
                      $"{t}: a skid of {skid:F1} m > the {margin:F1} m margin, so startAtRest is genuinely needed");
            }

            var f = typeof(CarAgent).GetField("startAtRest");
            Check(f != null && f.FieldType == typeof(bool), "CarAgent has a public startAtRest flag");

            var atRest = new CarAgent { driverType = CarAgent.DriverType.Cautious, startAtRest = true };
            typeof(CarAgent).GetMethod("SetProfileParameters",
                BindingFlags.NonPublic | BindingFlags.Instance).Invoke(atRest, null);
            Check(atRest.currentSpeed == 0f, "startAtRest = true -> spawns at zero speed");
            Check(atRest.isStopped,          "startAtRest = true -> counts as stopped, so reactionDelay applies on green");

            var rolling = new CarAgent { driverType = CarAgent.DriverType.Cautious };
            typeof(CarAgent).GetMethod("SetProfileParameters",
                BindingFlags.NonPublic | BindingFlags.Instance).Invoke(rolling, null);
            Check(rolling.currentSpeed > 1f && !rolling.isStopped,
                  "a normally spawned car still drives in at approachSpeed");
        }

        // ===== 13) Travel time from the spawn point to the stop line - Compare-101 uses it to convert clip times =====
        //   Clip times are when the front bumper crosses the stop line; ScheduledBurst releases at the spawn point.
        //   If this value drifts, every clip-derived case is offset as a whole.
        {
            Console.WriteLine("\n--- spawn -> stop line ---");
            float distA  = ABACSceneBuilder.SPAWN_FAR - Math.Abs(ABACSceneBuilder.STOP_A_X);
            float distB  = ABACSceneBuilder.SPAWN_FAR - Math.Abs(ABACSceneBuilder.STOP_B_X);
            float distC  = ABACSceneBuilder.SPAWN_FAR - Math.Abs(ABACSceneBuilder.STOP_C_Z);

            var sp = new CarSpawner();
            float agg = sp.aggressivePercent / 100f, nor = sp.normalPercent / 100f;
            float cau = 1f - agg - nor;
            float vbar = agg * Profile(CarAgent.DriverType.Aggressive, "approachSpeed")
                       + nor * Profile(CarAgent.DriverType.Normal,     "approachSpeed")
                       + cau * Profile(CarAgent.DriverType.Cautious,   "approachSpeed");
            float expect = distA / vbar;
            Console.WriteLine($"    distance A {distA:F1} m | B {distB:F1} | C {distC:F1} | mean v {vbar:F2} m/s"
                            + $" -> {expect:F2} s   (value in use {TestCasePresets.SPAWN_TO_STOPLINE_SEC:F2})");

            Check(Math.Abs(TestCasePresets.SPAWN_TO_STOPLINE_SEC - expect) < 0.15f,
                  $"SPAWN_TO_STOPLINE_SEC matches the real scene coordinates ({expect:F2} s)");
            Check(Math.Abs(distA - distB) < 1.5f && Math.Abs(distA - distC) < 1.5f,
                  "every approach is within 1.5 m of the same distance, so one value serves all");
            Check(Math.Abs(agg + nor + cau - 1f) < 1e-5f && cau > 0f,
                  "the driver shares sum to 1 and Cautious is not negative");
        }

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
