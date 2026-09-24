using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Three SODA (patched) test groups, per section 8 of the round-10 notes:
///   1. queueStart is reset when a green ends, so a lane that never empties cannot lock the others out
///   2. gap-out must see two consecutive empty snapshots, not just one
///   3. AdaptiveController must have no remaining calls to tracker-dependent APIs
/// </summary>
public class SodaTest
{
    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    /// <summary>Builds a mock junction: 5 signals (A A2 B Bx CB) with A/A2 sharing one lamp</summary>
    static AdaptiveController MakeController(out Dictionary<string, ROIZone> rois)
    {
        var cfg = new IntersectionConfig();
        cfg.LoadABACPreset();

        rois = new Dictionary<string, ROIZone>();
        var lightA = new TrafficLight { laneName = "A" };

        foreach (var lane in cfg.lanes)
        {
            if (!lane.isSignalized) continue;
            // A and A2 must point at the same TrafficLight object, as they do in the real scene
            lane.trafficLight = (lane.laneName == "A" || lane.laneName == "A2")
                              ? lightA
                              : new TrafficLight { laneName = lane.laneName };

            var roi = new ROIZone { laneName = lane.laneName, simulatedCountOverride = 0 };
            lane.roi = roi;
            rois[lane.laneName] = roi;
        }
        cfg.Build();

        var ac = new AdaptiveController { config = cfg };
        ac.BuildHeads();
        return ac;
    }

    static AdaptiveController.SignalHead HeadNamed(AdaptiveController ac, string name)
    {
        foreach (var h in ac.Heads) if (h.name == name) return h;
        return null;
    }

    // ===================================================================================
    public static int Main()
    {
        Dictionary<string, ROIZone> rois;
        var ac = MakeController(out rois);

        Console.WriteLine("--- signal heads built ---");
        foreach (var h in ac.Heads)
        {
            string lanes = "";
            foreach (int s in h.signals) lanes += ac.config.SignalName(s) + " ";
            Console.WriteLine($"    {h.name}: {lanes.Trim()}");
        }
        Check(ac.Heads.Count == 4, $"this junction has 4 heads, not 5 signalised lanes (got {ac.Heads.Count})");

        var hA = HeadNamed(ac, "H_A");
        Check(hA != null && hA.signals.Count == 2, "H_A controls both A and A2 (one lamp)");

        // ===============================================================================
        // Test 1 - queueStart reset: a lane that never empties must not hold the right of way forever
        // ===============================================================================
        Console.WriteLine("\n=== Test 1: queueStart reset (prevents the MorningPeak lock-out) ===");
        {
            // Simulates Bx at v/c = 1.51, never empty, while the other heads also keep getting vehicles
            foreach (var h in ac.Heads) { h.lastCount = 5; h.queueStart = 100f; }
            var hBx = HeadNamed(ac, "H_Bx");
            hBx.queueStart = 50f;                    // Bx started before everyone else

            var served = new Dictionary<string, int>();
            foreach (var h in ac.Heads) served[h.name] = 0;

            float clock = 200f;
            for (int phase = 0; phase < 10; phase++)
            {
                var leader = ac.ChooseLeaderByQueueStart(null);
                if (leader == null) { Check(false, "no leader even though every head has vehicles"); break; }
                served[leader.name]++;

                // Green ends with vehicles still waiting -> queueStart must reset to now
                clock += 30f;
                leader.queueStart = clock;           // = ResetQueueStartFor when count > 0
            }

            string dist = "";
            foreach (var kv in served) dist += $"{kv.Key}={kv.Value} ";
            Console.WriteLine($"    greens received over 10 phases: {dist}");

            int zero = 0;
            foreach (var kv in served) if (kv.Value == 0) zero++;
            Check(zero == 0, "every head got at least one green in 10 phases (nobody was locked out)");
            Check(served["H_Bx"] <= 4, $"Bx does not monopolise the queue (got {served["H_Bx"]}/10 phases)");
        }

        // Test 1b - does forgetting the reset really break it? (proves this test catches the real thing)
        Console.WriteLine("\n--- 1b: simulate forgetting the reset - it must visibly break ---");
        {
            foreach (var h in ac.Heads) { h.lastCount = 5; h.queueStart = 100f; }
            HeadNamed(ac, "H_Bx").queueStart = 50f;

            var served = new Dictionary<string, int>();
            foreach (var h in ac.Heads) served[h.name] = 0;
            for (int phase = 0; phase < 10; phase++)
            {
                var leader = ac.ChooseLeaderByQueueStart(null);
                served[leader.name]++;
                // deliberately no queueStart reset
            }
            Console.WriteLine($"    H_Bx got {served["H_Bx"]}/10 phases (expected to take them all)");
            Check(served["H_Bx"] == 10, "confirmed: without the reset, a lane that never empties takes every phase");
        }

        // Test 1c - the real ResetQueueStartFor: vehicles left -> new timestamp, empty -> Infinity
        Console.WriteLine("\n--- 1c: the real ResetQueueStartFor ---");
        {
            var hB = HeadNamed(ac, "H_B");
            rois["B"].simulatedCountOverride = 4;          // green ended with vehicles still waiting
            hB.queueStart = 10f;
            ac.ResetQueueStartFor(hB.signals);
            Check(!float.IsInfinity(hB.queueStart)
               && Math.Abs(hB.queueStart - Time.time) < 0.001f
               && Math.Abs(hB.queueStart - 10f) > 0.001f,
                  $"vehicles left -> queueStart is reset to now (10 -> {hB.queueStart})");

            rois["B"].simulatedCountOverride = 0;          // discharged exactly
            ac.ResetQueueStartFor(hB.signals);
            Check(float.IsInfinity(hB.queueStart), "fully discharged -> queueStart = Infinity (empty)");

            Check(ac.QueueStartResets >= 1, $"resets are counted for the log ({ac.QueueStartResets})");
        }

        // ===============================================================================
        // Test 2 - gap-out must be confirmed over 2 consecutive snapshots
        // ===============================================================================
        Console.WriteLine("\n=== Test 2: gap-out needs two consecutive empty snapshots ===");
        {
            var ac2 = MakeController(out rois);
            var hB = HeadNamed(ac2, "H_B");

            // Make H_B the group currently holding green
            FieldInfo fGroup = typeof(AdaptiveController)
                .GetField("activeGroup", BindingFlags.NonPublic | BindingFlags.Instance);
            fGroup.SetValue(ac2, new List<int>(hB.signals));
            FieldInfo fStreak = typeof(AdaptiveController)
                .GetField("groupZeroStreak", BindingFlags.NonPublic | BindingFlags.Instance);

            Func<int[], int> RunSeq = counts =>
            {
                fStreak.SetValue(ac2, 0);
                foreach (int c in counts)
                {
                    rois["B"].simulatedCountOverride = c;
                    ac2.Snapshot();
                }
                return (int)fStreak.GetValue(ac2);
            };

            int streakA = RunSeq(new[] { 3, 0, 2 });
            Console.WriteLine($"    sequence [3, 0, 2] -> streak = {streakA}");
            Check(streakA < ac2.gapOutSnapshots,
                  "[3, 0, 2] must not end the phase (empty for one snapshot, then vehicles again)");

            int streakB = RunSeq(new[] { 3, 0, 0 });
            Console.WriteLine($"    sequence [3, 0, 0] -> streak = {streakB}");
            Check(streakB >= ac2.gapOutSnapshots, "[3, 0, 0] must end the phase (two consecutive empty snapshots)");

            Check(ac2.gapOutSnapshots == 2, "two snapshots of confirmation required");
            // The confirmation window is not set directly; it is always the product of these two.
            // 14 Sep 2026: snapshotInterval 2 -> 1 s, so the window shrank from 4 s to 2 s.
            //   Itim stated explicitly that he knew and accepted this ("Gap-out still counts 2 readings,
            //   so it now fires 2 s after a lane empties")
            // A shorter window means two consecutive missed readings end a phase more easily,
            //    and at 1 s two consecutive misses are more correlated than at 2 s (a car hidden
            //    behind a lorry stays hidden longer than a second) - watch EndReason = gap-out.
            float confirmWindow = ac2.gapOutSnapshots * ac2.snapshotInterval;
            Check(Math.Abs(confirmWindow - 2f) < 0.01f,
                  $"confirmation window = {confirmWindow:F0} seconds (2 readings x {ac2.snapshotInterval:F0} s)");
        }

        // ===============================================================================
        // Test 3 - no tracker-dependent API calls remain
        // ===============================================================================
        Console.WriteLine("\n=== Test 3: has the tracker really been removed? ===");
        {
            string src = File.Exists("Assets/Scripts/AdaptiveController.cs")
                       ? File.ReadAllText("Assets/Scripts/AdaptiveController.cs") : null;
            Check(src != null, "AdaptiveController.cs can be read");

            if (src != null)
            {
                // Strip comments first - the comments name these APIs deliberately, as warnings
                string code = Regex.Replace(src, @"//.*?$", "", RegexOptions.Multiline);
                code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);

                string[] trackerApis = { "GetOldestArrivalTime", "GetLongestWaitTime", "GetCarsInZone" };
                foreach (string api in trackerApis)
                    Check(!code.Contains(api),
                          $"does not call {api}() (that would require ByteTrack/DeepSORT)");

                Check(code.Contains("GetSampledCount"),
                      "uses GetSampledCount(), which a camera without a tracker can provide");
                Check(!code.Contains("GetVehicleCount"),
                      "does not also call GetVehicleCount() (keep it to one path)");

                // rest-in-green must be gone - the real signal goes all-red when idle
                Check(code.Contains("AllRedIdle"), "has an ALL-RED IDLE state instead of rest-in-green");
            }

            // DataLogger must keep using ground truth - it measures outcomes, not controller inputs
            string roiSrc = File.Exists("Assets/Scripts/ROIZone.cs") ? File.ReadAllText("Assets/Scripts/ROIZone.cs") : "";
            Check(roiSrc.Contains("GetOldestArrivalTime"),
                  "ROIZone keeps the tracking APIs (DataLogger measures with them; the controller does not decide with them)");
            Check(roiSrc.Contains("car.assignedLane"),
                  "still logs by car.assignedLane rather than roi.laneName (Ax is the headline measure)");
        }

        // ===============================================================================
        // Check that what must not be touched is all still there
        // ===============================================================================
        Console.WriteLine("\n=== Things that must not change ===");
        {
            var ac3 = MakeController(out rois);
            Check(Math.Abs(ac3.startupLostTime - 2.95f) < 1e-6f
               && Math.Abs(ac3.saturationHeadway - 2.31f) < 1e-6f, "the G formula still uses the measured values");
            Check(Math.Abs(ac3.yellowDuration - 3f) < 1e-6f
               && Math.Abs(ac3.allRedDuration - 2f) < 1e-6f, "clearance is still 3 + 2 seconds");
            Check(Math.Abs(ac3.maxContinuousGreen - 90f) < 1e-6f, "max continuous green is 90 seconds");
            Check(Math.Abs(ac3.maxWaitTime - 45f) < 1e-6f
               && Math.Abs(ac3.starvationSelectThreshold - 0.95f) < 1e-6f
               && Math.Abs(ac3.starvationInterruptThreshold - 0.90f) < 1e-6f,
                  "anti-starvation is still 45 s / 0.95 / 0.90");

            // The G formula must give exactly the same result
            rois["B"].simulatedCountOverride = 4;
            var hB = HeadNamed(ac3, "H_B");
            float g = ac3.CalculateGreenDuration(hB.signals);
            Console.WriteLine($"    D = 4 -> G = {g:F2}s (expected 12.19)");
            Check(Math.Abs(g - 12.19f) < 0.01f, "G(4) = 2.95 + 4 x 2.31 = 12.19 s, unchanged");

            // Starvation must use queueStart, not per-vehicle arrivals
            foreach (var h in ac3.Heads) { h.lastCount = 0; h.queueStart = Mathf.Infinity; }
            hB.lastCount = 2;
            hB.queueStart = Time.time - 43f;      // past 0.95 x 45 = 42.75
            Check(ac3.FindStarvingHead(0.95f, null) == hB, "starvation is detected from queueStart");
            hB.queueStart = Time.time - 10f;
            Check(ac3.FindStarvingHead(0.95f, null) == null, "not waited long enough - must not count as starving");
        }

        // ===============================================================================
        // Test 4 - Decision Inspector: the panel must reflect what the algorithm actually sees
        // ===============================================================================
        Console.WriteLine("\n=== Test 4: the decision trace panel ===");
        {
            var ac4 = MakeController(out rois);
            var hA4  = HeadNamed(ac4, "H_A");
            var hB4 = HeadNamed(ac4, "H_B");
            var hBx = HeadNamed(ac4, "H_Bx");
            var hCB = HeadNamed(ac4, "H_CB");

            // Set the scene: Bx has waited longest, A next, B just arrived, CB empty
            float now = Time.time;
            hBx.lastCount = 9;  hBx.queueStart = now - 18.3f;
            hA4.lastCount  = 14; hA4.queueStart  = now - 12.1f;
            hB4.lastCount = 3;  hB4.queueStart = now - 4.0f;
            hCB.lastCount = 0;  hCB.queueStart = Mathf.Infinity;

            ac4.TraceDecisionInputs();

            Console.WriteLine("    -- the QUEUE the panel will show --");
            foreach (string row in ac4.QueueView) Console.WriteLine("       " + row.Replace("|", "  "));

            Check(ac4.QueueView.Count == 4, $"the panel shows all 4 heads (got {ac4.QueueView.Count})");
            Check(ac4.QueueView[0].Contains("H_Bx"), "first in the queue = H_Bx (oldest queueStart)");
            Check(ac4.QueueView[1].Contains("H_A"),  "second = H_A");
            Check(ac4.QueueView[2].Contains("H_B"),  "third = H_B");
            Check(ac4.QueueView[3].Contains("H_CB") && ac4.QueueView[3].Contains("SKIPPED"),
                  "H_CB is empty -> shown as SKIPPED rather than silently missing");

            var q1 = ac4.DecisionTrace[0];
            var q2 = ac4.DecisionTrace[1];
            Console.WriteLine($"    -- Q1 has {q1.options.Count} rows, Q2 has {q2.options.Count} --");
            foreach (string o in q1.options) Console.WriteLine("       Q1  " + o.Replace("|", "  "));

            Check(q1.options.Count == 4, "Q1 shows the threshold plus the 3 heads with vehicles");
            Check(q1.options[0].Contains("42.8") || q1.options[0].Contains("42.7"),
                  "Q1 shows the 42.75 s starvation threshold, so the comparison is visible");
            Check(q2.options.Count == 4, "Q2 shows all 4 heads as candidates");

            bool cbIneligible = false;
            foreach (string o in q2.options)
                if (o.Contains("H_CB") && o.Contains("not eligible")) cbIneligible = true;
            Check(cbIneligible, "Q2 says why H_CB is not a candidate (empty)");

            // The order the panel shows must match the leader the algorithm actually picks
            var chosen = ac4.ChooseLeaderByQueueStart(null);
            Check(chosen == hBx, "the leader actually chosen = the panel's first entry (the panel does not lie)");

            // Regression: SimulationControlPanel.hideLegacyDebugGUI turns showDebugGUI off at Start,
            // so if OnGUI returned on showDebugGUI alone this panel could never appear (it happened once).
            string ctl = File.Exists("Assets/Scripts/AdaptiveController.cs")
                       ? File.ReadAllText("Assets/Scripts/AdaptiveController.cs") : "";
            Check(ctl.Contains("!showDebugGUI && !showDecisionPanel"),
                  "OnGUI only returns when both panels are off - the trace panel is not tied to showDebugGUI");
            Check(!ctl.Contains("if (!showDebugGUI || !Application.isPlaying"),
                  "the old condition that also hid the trace panel is gone");

            string sm = File.Exists("Assets/Scripts/ScenarioManager.cs")
                      ? File.ReadAllText("Assets/Scripts/ScenarioManager.cs") : "";
            Check(sm.Contains("showDecisionPanel = false"),
                  "a 270-run batch must disable this panel (no building strings every phase with nobody watching)");

            string cp = File.Exists("Assets/Scripts/SimulationControlPanel.cs")
                      ? File.ReadAllText("Assets/Scripts/SimulationControlPanel.cs") : "";
            Check(cp.Contains("showDecisionPanel"),
                  "there is a toggle button on the control panel, so no hotkey has to be remembered");

            // Order inside OnGUI: the hotkey must be read before the early return, or the panel cannot be reopened
            int iKey  = ctl.IndexOf("ev.keyCode == decisionPanelKey");
            int iExit = ctl.IndexOf("if (!showDebugGUI && !showDecisionPanel) return;");
            Console.WriteLine($"    positions in OnGUI: key read @{iKey}, early exit @{iExit}");
            Check(iKey > 0 && iExit > 0 && iKey < iExit,
                  "the hotkey is read before the early return (press D to hide, and you can bring it back)");
            Check(ctl.Contains("ev.Use()"), "the event is consumed after toggling, so the key is not eaten twice");
            Check(!cp.Contains("activeController == ControllerKind.Adaptive && adaptiveController != null"),
                  "the control panel button does not disappear with the selected controller - there is always a way back");
        }

        // ===============================================================================
        // Test 5 - idle green that the current gap-out cannot see (Ratchanon)
        //   Gap-out fires only when every lane in the group is empty, so a lane that empties first
        //   sits on green waiting for the others. This test confirms the problem is real and measures it;
        //   it does not change the behaviour. If gap-out ever becomes per-lane, this shows the saving.
        // ===============================================================================
        Console.WriteLine("\n=== Test 5: idle green in the group {B, Bx} ===");
        {
            var ac5 = MakeController(out rois);
            var cfg5 = ac5.config;

            rois["B"].simulatedCountOverride  = 10;
            rois["Bx"].simulatedCountOverride = 3;

            var grp = new List<int> { cfg5.SignalIndexOf("B"), cfg5.SignalIndexOf("Bx") };
            float gB  = 2.95f + 10f * 2.31f;
            float gBx = 2.95f +  3f * 2.31f;
            float G   = ac5.CalculateGreenDuration(grp);

            Console.WriteLine($"    B  10 cars -> {gB:F2}s");
            Console.WriteLine($"    Bx  3 cars -> {gBx:F2}s");
            Console.WriteLine($"    G = max = {G:F2}s  ->  Bx empty from {gBx:F2}s = {G - gBx:F2}s of idle green");

            Check(Math.Abs(G - 26.05f) < 0.02f, $"the group G = 26.05s (critical lane = B)");
            Check(Math.Abs((G - gBx) - 16.17f) < 0.02f, "Bx holds 16.17 s of idle green");

            // Why it costs more than it looks: Bx is the only thing blocking A
            int iA = cfg5.SignalIndexOf("A"), iB = cfg5.SignalIndexOf("B"), iBx = cfg5.SignalIndexOf("Bx");
            Check(cfg5.AreConflicting(iA, iBx), "A conflicts with Bx");
            Check(!cfg5.AreConflicting(iA, iB), "A does not conflict with B");
            Console.WriteLine($"    => dropping Bx 16.17s earlier lets A go green alongside B (A waits 16.17s less)");

            // Can the current gap-out help? No: B still has vehicles, so the group is never empty together
            FieldInfo fG = typeof(AdaptiveController)
                .GetField("activeGroup", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fS = typeof(AdaptiveController)
                .GetField("groupZeroStreak", BindingFlags.NonPublic | BindingFlags.Instance);
            fG.SetValue(ac5, new List<int>(grp));
            fS.SetValue(ac5, 0);

            rois["Bx"].simulatedCountOverride = 0;      // Bx has discharged
            for (int i = 0; i < 5; i++) ac5.Snapshot(); // B still has 10 vehicles
            int streak = (int)fS.GetValue(ac5);
            Console.WriteLine($"    Bx empty for 5 consecutive snapshots, yet streak = {streak}");
            Check(streak == 0, "confirmed: the current gap-out never fires, because B is not empty (the problem is real)");

            // The measurement must capture this case, or a batch run leaves no evidence
            var dl = new DataLogger();
            dl.LogPhase(1, 0f, "B+Bx", G, G, "completed", $"B=25.0;Bx={gBx:F1}", G - gBx);
            Check(Math.Abs(dl.GetIdleLaneSeconds() - 16.17f) < 0.05f,
                  $"the phase log records the idle green ({dl.GetIdleLaneSeconds():F2}s)");
            Check(dl.GetPhaseCount() == 1, "phases are counted");
        }

        // ===============================================================================
        // Test 6 - one clock: every decision hangs off a snapshot, with no competing loop timers
        // ===============================================================================
        Console.WriteLine("\n=== Test 6: a single clock ===");
        {
            var ac6 = MakeController(out rois);
            int i0 = ac6.SnapshotIndex;
            ac6.Snapshot(); ac6.Snapshot(); ac6.Snapshot();
            Check(ac6.SnapshotIndex == i0 + 3, $"snapshotIndex advances on every snapshot ({i0} -> {ac6.SnapshotIndex})");

            string ctl6 = File.Exists("Assets/Scripts/AdaptiveController.cs")
                        ? File.ReadAllText("Assets/Scripts/AdaptiveController.cs") : "";
            string code6 = Regex.Replace(ctl6, @"//.*?$", "", RegexOptions.Multiline);
            code6 = Regex.Replace(code6, @"/\*.*?\*/", "", RegexOptions.Singleline);

            Check(!code6.Contains("snapshotInterval * 0.5f"),
                  "no loop checks every 1 s while the data only updates every 2 s");
            Check(code6.Contains("WaitForNextSnapshot"),
                  "waiting loops use WaitForNextSnapshot - evaluated exactly once per snapshot");

            // SnapshotLoop must be the only place that sets a timing beat
            int ticks = Regex.Matches(code6, @"WaitForSeconds\(snapshotInterval").Count;
            Console.WriteLine($"    places that set a beat from snapshotInterval = {ticks}");
            Check(ticks == 1, "one clock in the system (SnapshotLoop), with nothing competing");
        }

        // ===============================================================================
        // Test 7 - one demand accumulator, with no double waiting
        // ===============================================================================
        Console.WriteLine("\n=== Test 7: a single demand accumulator ===");
        {
            string ctl7 = File.Exists("Assets/Scripts/AdaptiveController.cs")
                        ? File.ReadAllText("Assets/Scripts/AdaptiveController.cs") : "";
            string code7 = Regex.Replace(ctl7, @"//.*?$", "", RegexOptions.Multiline);
            code7 = Regex.Replace(code7, @"/\*.*?\*/", "", RegexOptions.Singleline);

            Check(!code7.Contains("countingWindow"),
                  "no counting window remains (it asked COLLECT's question with COLLECT's threshold)");
            Check(!code7.Contains("demandThresholdSkipWait"),
                  "no threshold duplicating earlyExitDemand remains");

            // WaitForSeconds must appear in one place only: SnapshotLoop
            int waits = Regex.Matches(code7, @"WaitForSeconds\(").Count;
            Console.WriteLine($"    WaitForSeconds in the whole file = {waits}");
            Check(waits == 1, "one WaitForSeconds left (SnapshotLoop) - everything is on one clock");

            var ac7 = MakeController(out rois);
            Check(ac7.earlyExitDemand == 3 && Math.Abs(ac7.collectWindow - 7f) < 1e-6f,
                  "COLLECT: 7 s ceiling, early exit at 3 vehicles");

            // S1 is 4 s shorter after the removal: 7 (collect) + 8 (G floor) + 5 (clearance) = 20 from first sighting
            float s1 = ac7.collectWindow + ac7.gMin + ac7.yellowDuration + ac7.allRedDuration;
            Console.WriteLine($"    S1 (one vehicle) from the moment it is seen = {s1:F1}s (was {s1 + 4:F1}s)");
            Check(Math.Abs(s1 - 20f) < 0.01f, "the single-vehicle cycle drops from 24 to 20 seconds");
        }

        // ===============================================================================
        // Test 8 - Option A: tie-break = the original phase order A -> B -> Bx -> CB (approved by Itim)
        // ===============================================================================
        Console.WriteLine("\n=== Test 8: an explicitly declared tie-break ===");
        {
            var ac8 = MakeController(out rois);
            var cfg8 = ac8.config;

            Console.WriteLine("    priority: " + string.Join("  ",
                ac8.Heads.ConvertAll(h => $"{h.name}={h.priority}").ToArray()));
            Check(HeadNamed(ac8, "H_A").priority == 0 && HeadNamed(ac8, "H_B").priority == 1
               && HeadNamed(ac8, "H_Bx").priority == 2 && HeadNamed(ac8, "H_CB").priority == 3,
                  "priority comes from ORIGINAL_SEQUENCE, not the incidental order of variable declarations");

            // All tied -> H_A must win, and identically on every call
            foreach (var h in ac8.Heads) { h.lastCount = 5; h.queueStart = 100f; }
            var l1 = ac8.ChooseLeaderByQueueStart(null);
            var l2 = ac8.ChooseLeaderByQueueStart(null);
            Check(l1 == HeadNamed(ac8, "H_A") && l1 == l2,
                  "all tied -> leader = H_A every time (unchanged behaviour)");

            // queueStart always beats priority
            HeadNamed(ac8, "H_CB").queueStart = 50f;
            Check(ac8.ChooseLeaderByQueueStart(null) == HeadNamed(ac8, "H_CB"),
                  "the older queueStart wins even with the worst priority");

            // The comparator is a total order - sorting gives the same result whatever the input order
            var fwd = new List<AdaptiveController.SignalHead>(ac8.Heads);
            var rev = new List<AdaptiveController.SignalHead>(ac8.Heads);
            rev.Reverse();
            foreach (var h in ac8.Heads) h.queueStart = 100f;
            fwd.Sort(AdaptiveController.CompareHeads);
            rev.Sort(AdaptiveController.CompareHeads);
            string a = string.Join(",", fwd.ConvertAll(h => h.name).ToArray());
            string b = string.Join(",", rev.ConvertAll(h => h.name).ToArray());
            Console.WriteLine($"    sorted from the normal order  = {a}");
            Console.WriteLine($"    sorted from the reverse order = {b}");
            Check(a == b, "with everything tied, the sort still gives one result (not relying on List.Sort being stable)");
            Check(a == "H_A,H_B,H_Bx,H_CB", "ordered by the junction's original phase order");

            // The counter is purely passive
            var ac9 = MakeController(out rois);
            foreach (var h in ac9.Heads) { h.lastCount = 4; h.queueStart = 100f; }
            HeadNamed(ac9, "H_CB").lastCount = 0;
            HeadNamed(ac9, "H_CB").queueStart = Mathf.Infinity;

            int iB9 = ac9.config.SignalIndexOf("B");
            var m9 = typeof(AdaptiveController).GetMethod("BuildCompatibleGroup",
                         BindingFlags.NonPublic | BindingFlags.Instance);
            var g9 = (List<int>)m9.Invoke(ac9, new object[] { iB9 });
            var c9 = typeof(AdaptiveController).GetMethod("CountTieBreaks",
                         BindingFlags.NonPublic | BindingFlags.Instance);
            c9.Invoke(ac9, new object[] { g9 });

            string gn = "";
            foreach (int sg in g9) gn += ac9.config.SignalName(sg) + " ";
            Console.WriteLine($"    leader=B, A/Bx tied -> group = {{ {gn.Trim()} }}");
            Console.WriteLine($"    TieBreaksApplied={ac9.TieBreaksApplied}  TieBreaksExcludingBx={ac9.TieBreaksExcludingBx}");

            Check(ac9.TieBreaksApplied == 1, "one tie was counted");
            Check(ac9.TieBreaksExcludingBx == 1,
                  "and that tie was counted as excluding H_Bx from the group");

            // No tie -> nothing counted
            var acA = MakeController(out rois);
            var hs = acA.Heads;
            for (int i = 0; i < hs.Count; i++) { hs[i].lastCount = 3; hs[i].queueStart = 100f + i; }
            var gA = (List<int>)m9.Invoke(acA, new object[] { acA.config.SignalIndexOf("B") });
            c9.Invoke(acA, new object[] { gA });
            Check(acA.TieBreaksApplied == 0, "nobody tied -> the counter must not move");
        }

        // ===============================================================================
        // Test 9 - a starvation tie must go the same way as Option A (SODA_BASELINE section 8.3, D3)
        // ===============================================================================
        Console.WriteLine("\n=== Test 9: the starvation tie-break ===");
        {
            var ac9b = MakeController(out rois);
            float now = Time.time;
            foreach (var h in ac9b.Heads) { h.lastCount = 0; h.queueStart = Mathf.Infinity; }
            var hA9  = HeadNamed(ac9b, "H_A");
            var hCB9 = HeadNamed(ac9b, "H_CB");
            hA9.lastCount = 2;  hA9.queueStart  = now - 50f;     // both over 42.75, bit-identical
            hCB9.lastCount = 2; hCB9.queueStart = now - 50f;

            var pick = ac9b.FindStarvingHead(0.95f, null);
            Console.WriteLine($"    A and CB have both waited exactly 50 s -> starving leader = {pick.name}");
            Check(pick == hA9, "tied -> H_A wins (the original phase order), not the last head in the list");

            // A longer wait must always beat priority
            hCB9.queueStart = now - 60f;
            Check(ac9b.FindStarvingHead(0.95f, null) == hCB9, "CB waited longer -> CB wins despite lower priority");

            // Below the threshold, nobody is selected
            hA9.queueStart = now - 10f; hCB9.queueStart = now - 10f;
            Check(ac9b.FindStarvingHead(0.95f, null) == null, "nobody reaches 42.75 -> null");

            // Heads in the green group are still excluded
            hCB9.queueStart = now - 50f;
            Check(ac9b.FindStarvingHead(0.90f, hCB9.signals) == null,
                  "a head in the green group is excluded (anti-starvation never cuts for itself)");
        }

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
