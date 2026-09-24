using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// QueueClockTest - a defect found after the 216-run batch (11 Sep 2026).
///
/// Symptom in the field (phases_Adaptive_EveningPeak_Rep17, t=1104.6 -> t=1470.2):
///   H_B stayed in the green group for 24 consecutive phases and never left it.
///   H_CB had vehicles waiting the whole time and got no green at all for 365.5 seconds.
///   The worst CB vehicle waited 344 seconds, against maxWaitTime = 45.
///   That run had 99 phases but logged 145 StarvationEvents.
///
/// Cause: ServeOnePhase reset queueStart only for heads dropping out of the group, so a head that
///        continued across phases held a stale queueStart indefinitely and won both
///        Q1 (FindStarvingHead) and Q2 (ChooseLeaderByQueueStart) forever.
///
/// The invariant: queueStart means "how long since this head last had a green", so a head whose
/// green has just ended must have a queueStart no older than the start of that phase.
///
/// Compile: mcs -target:exe -out:test9.exe Test9.cs -r:gamecode.dll
///          (never add a second copy of the stubs, or UnityEngine.Time differs from the one in the dll)
/// </summary>
public class QueueClockTest
{
    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    static AdaptiveController MakeController()
    {
        var cfg = new IntersectionConfig();
        cfg.LoadABACPreset();
        var lightA = new TrafficLight { laneName = "A" };
        foreach (var lane in cfg.lanes)
        {
            if (!lane.isSignalized) continue;
            lane.trafficLight = (lane.laneName == "A" || lane.laneName == "A2")
                              ? lightA : new TrafficLight { laneName = lane.laneName };
            // Every lane keeps 4 vehicles throughout, an EveningPeak state where nothing ever empties
            lane.roi = new ROIZone { laneName = lane.laneName, simulatedCountOverride = 4 };
        }
        cfg.Build();
        var ac = new AdaptiveController { config = cfg };
        ac.BuildHeads();
        return ac;
    }

    static AdaptiveController.SignalHead Head(AdaptiveController ac, string name)
    {
        foreach (var h in ac.Heads) if (h.name == name) return h;
        return null;
    }

    static List<int> Sig(AdaptiveController ac, params string[] lanes)
    {
        var l = new List<int>();
        foreach (var n in lanes)
        {
            int i = ac.config.SignalIndexOf(n);
            if (i >= 0 && !l.Contains(i)) l.Add(i);
        }
        return l;
    }

    static List<int> Except(List<int> src, List<int> rm)
    {
        var m = typeof(AdaptiveController).GetMethod("Except",
                    BindingFlags.NonPublic | BindingFlags.Static);
        return (List<int>)m.Invoke(null, new object[] { src, rm });
    }

    public static int Main()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("=== QueueClockTest: the clock of a head that stays green ===");

        var ac = MakeController();
        var hB  = Head(ac, "H_B");
        var hCB = Head(ac, "H_CB");

        // Starting point t=1000: CB has just finished its green (queueStart reset to 1000),
        //                     while B has been green since before that and never left the group, so its clock is stuck at 900
        Time.time = 1000f;
        foreach (var h in ac.Heads) { h.lastCount = 4; h.queueStart = 1000f; }
        hB.queueStart = 900f;

        // The group sequence that actually occurred in the log: B in every group, CB never chosen
        string[][] sequence = {
            new[] { "Bx", "B" },
            new[] { "A", "B", "A2" },
            new[] { "B", "Bx" },
            new[] { "A", "B", "A2" },
            new[] { "B", "Bx" },
        };

        var active = Sig(ac, "Bx", "B");
        float lastPhaseStart = Time.time;

        foreach (var g in sequence)
        {
            lastPhaseStart = Time.time;
            Time.time += 20f;                 // 20 s per phase, the value the real log shows starvation cutting at
            // Same order as ServeOnePhase: end the previous phase, then decide the new group
            ac.ResetQueueStartFor(active);
            active = Sig(ac, g);
        }

        float now = Time.time;
            Console.WriteLine($"    after {sequence.Length} phases, t={now:F0} (B green every phase, CB never)");
        foreach (var h in ac.Heads)
        {
            string w = float.IsInfinity(h.queueStart) ? "-" : $"{now - h.queueStart:F0}s";
            Console.WriteLine($"      {h.name,-5} queueStart={h.queueStart,7:F0}  the system thinks it waited {w,5}");
        }

        Check(hB.queueStart >= lastPhaseStart,
              "a head whose green has just ended must have a queueStart no older than that phase's start");

        Check(hCB.queueStart < hB.queueStart,
              "the genuinely starved head (CB) must have an older queue than the one just served (B)");

        var starving = ac.FindStarvingHead(0.95f, null);
        Console.WriteLine($"      Q1 FindStarvingHead      -> {(starving == null ? "null" : starving.name)}");
        Check(starving == hCB, "Q1 must point at H_CB, not the permanently green H_B");

        var leader = ac.ChooseLeaderByQueueStart(null);
        Console.WriteLine($"      Q2 ChooseLeaderByQueueStart -> {(leader == null ? "null" : leader.name)}");
        Check(leader == hCB, "Q2 must choose H_CB as the next phase leader");

        // ================================================================
        // Reset order against the decision - the path the old test never walked
        // ================================================================

        Console.WriteLine("\n--- Case 1: a head just served must not win Q2 again ---");
        var ac3 = MakeController();
        var a3  = Head(ac3, "H_A");  var b3  = Head(ac3, "H_B");
        var bx3 = Head(ac3, "H_Bx"); var cb3 = Head(ac3, "H_CB");

        // t=6 : A and CB get their first vehicle in the same snapshot -> exactly equal queueStart
        Time.time = 6f;
        a3.lastCount = 1;  a3.queueStart = 6f;
        cb3.lastCount = 1; cb3.queueStart = 6f;
        b3.lastCount = 0;  b3.queueStart  = float.PositiveInfinity;
        bx3.lastCount = 0; bx3.queueStart = float.PositiveInfinity;
        Check(ac3.ChooseLeaderByQueueStart(null) == a3,
              "phase 1: a tie -> A wins on ORIGINAL_SEQUENCE");

        // t=8 : B and Bx start getting vehicles
        Time.time = 8f;
        b3.lastCount = 5;  b3.queueStart  = 8f;
        bx3.lastCount = 5; bx3.queueStart = 8f;

        // t=14 : phase 1 (8 s of green) ends -> phase-end reset of {A, A2} before the decision
        Time.time = 14f;
        ac3.ResetQueueStartFor(Sig(ac3, "A", "A2"));
        var lead2 = ac3.ChooseLeaderByQueueStart(null);
        Console.WriteLine($"      queueStart: A={a3.queueStart:F1} B={b3.queueStart:F1} " +
                          $"Bx={bx3.queueStart:F1} CB={cb3.queueStart:F1}");
        Console.WriteLine($"      Q2 -> {(lead2 == null ? "null" : lead2.name)}");
        Check(lead2 == cb3,
              "phase 2 must go to CB (the oldest remaining queue), not to A, which just had 8 s of green");

        Console.WriteLine("\n--- Case 2: a long green must not push the head itself over the Q1 threshold ---");
        var ac4 = MakeController();
        var a4  = Head(ac4, "H_A");  var bx4 = Head(ac4, "H_Bx"); var cb4 = Head(ac4, "H_CB");
        Time.time = 0f;
        foreach (var h in ac4.Heads) { h.lastCount = 4; h.queueStart = 0f; }
        bx4.lastCount = 0; bx4.queueStart = float.PositiveInfinity;   // Bx empty, leaving CB as the genuinely starved one

        // A+A2+B get 44.53 s of green (D = 18, exactly at the ceiling) and still have vehicles left
        Time.time = 44.53f;
        Check(ac4.FindStarvingHead(0.95f, null) == a4,
              "before the reset: A meets the Q1 threshold using time it spent green itself - the symptom of the bug");
        ac4.ResetQueueStartFor(Sig(ac4, "A", "A2", "B"));
        var st4 = ac4.FindStarvingHead(0.95f, null);
            Console.WriteLine($"      Q1 after the phase-end reset -> {(st4 == null ? "null" : st4.name)}");
        Check(st4 == cb4,
              "after the reset: Q1 must point at CB, which really waited 44.53 s on red, not at A, which was green for it");

        // ---- The counters must be cleared when switching to the Fixed controller ----
        Console.WriteLine("\n--- ForceStop must clear the SODA counters ---");
        var ac2 = MakeController();
        var f = typeof(AdaptiveController).GetField("queueStartResets",
                    BindingFlags.NonPublic | BindingFlags.Instance);
        var f2 = typeof(AdaptiveController).GetField("skippedHeadTotal",
                    BindingFlags.NonPublic | BindingFlags.Instance);
        f.SetValue(ac2, 50); f2.SetValue(ac2, 225);
        Console.WriteLine($"      before ForceStop: QueueStartResets={ac2.QueueStartResets} SkippedHeadTotal={ac2.SkippedHeadTotal}");
        ac2.ForceStop();
        Console.WriteLine($"      after ForceStop:  QueueStartResets={ac2.QueueStartResets} SkippedHeadTotal={ac2.SkippedHeadTotal}");
        Check(ac2.QueueStartResets == 0 && ac2.SkippedHeadTotal == 0,
              "ForceStop clears the counters, so Fixed rows do not carry the previous Adaptive run's values");

        // ---- Keep it from disappearing from production: ServeOnePhase must really call the reset ----
        string src = System.IO.File.ReadAllText("Assets/Scripts/AdaptiveController.cs");
        int serve = src.IndexOf("IEnumerator ServeOnePhase");
        int after = serve >= 0 ? src.IndexOf("IEnumerator GreenPhaseWithInterrupts", serve) : -1;
        string body = (serve >= 0 && after > serve) ? src.Substring(serve, after - serve) : "";
        int iReset = body.IndexOf("ResetQueueStartFor(activeGroup)");
        int iQ1    = body.IndexOf("FindStarvingHead");
        int iQ2    = body.IndexOf("ChooseLeaderByQueueStart");
        Check(iReset >= 0, "ServeOnePhase calls ResetQueueStartFor(activeGroup) as the phase-end reset");
        Check(iReset >= 0 && iQ1 > iReset, "the phase-end reset comes *before* Q1 (FindStarvingHead)");
        Check(iReset >= 0 && iQ2 > iReset, "the phase-end reset comes *before* Q2 (ChooseLeaderByQueueStart)");
        Check(!body.Contains("ResetContinuingHeads"),
              "no split reset in two places may remain (it is one call at the top of the phase now)");

        int tb = src.IndexOf("void TraceBegin");
        int tbEnd = tb >= 0 ? src.IndexOf("void TraceEnter", tb) : -1;
        string tbBody = (tb >= 0 && tbEnd > tb) ? src.Substring(tb, tbEnd - tb) : "";
        Check(tbBody.IndexOf("phaseCounter++") >= 0 &&
              tbBody.IndexOf("phaseCounter++") < tbBody.IndexOf("if (!showDecisionPanel) return;"),
              "phaseCounter++ comes before the early return, so PhaseIndex advances even with the decision panel off");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
