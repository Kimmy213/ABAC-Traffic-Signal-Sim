using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// Runs the real FixedController.RunCycle() and checks the safety invariant at every instant
public class FixedCycleTest
{
    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    public static int Main()
    {
        var cfg = new IntersectionConfig();
        cfg.LoadABACPreset();

        // Attach a fake TrafficLight to each signalised lane
        var lights = new Dictionary<string, TrafficLight>();
        foreach (var lane in cfg.lanes)
        {
            if (!lane.isSignalized) continue;
            var tl = new TrafficLight { laneName = lane.laneName };
            lane.trafficLight = tl;
            lights[lane.laneName] = tl;
        }
        cfg.Build();

        var fc = new FixedController { config = cfg };
        fc.LoadABACPreset();

        Console.WriteLine($"cycle length = {fc.GetCycleLength():F0}s");
        Check(Math.Abs(fc.GetCycleLength() - 106f) < 0.01f, "cycle = 106s per the stage table in SP1_HANDOFF_v2 (A=40 / B+Bx=20 / CB=31 + intergreen 15)");

        // ===== Step through the real RunCycle =====
        fc.RestartCycle();
        IEnumerator cycle = fc.captured;
        Check(cycle != null, "RunCycle started");

        string[] sig = { "A", "A2", "B", "Bx", "CB" };
        Func<string, string, bool> C = (a, b) =>
            cfg.AreConflicting(cfg.SignalIndexOf(a), cfg.SignalIndexOf(b));

        float t = 0f;
        int violations = 0;
        int steps = 0;
        var timeline = new List<string>();
        var greenSeen = new HashSet<string>();
        bool sawAplusB = false;
        bool sawAplusA2 = false;
        bool sawBplusBx = false;

        // 2 full cycles = 212 seconds (106s each)
        while (cycle.MoveNext() && t < 240f && steps < 200)
        {
            steps++;

            // ---- check the invariant in the current state ----
            var notRed = new List<string>();
            var green = new List<string>();
            foreach (var n in sig)
            {
                if (lights[n].currentState != TrafficLight.LightState.Red) notRed.Add(n);
                if (lights[n].currentState == TrafficLight.LightState.Green) { green.Add(n); greenSeen.Add(n); }
            }

            // Hard rule: conflicting lanes must never both be non-red
            // (yellow still has traffic moving, so it still holds the junction)
            for (int i = 0; i < notRed.Count; i++)
                for (int j = i + 1; j < notRed.Count; j++)
                    if (C(notRed[i], notRed[j]))
                    {
                        violations++;
                        Console.WriteLine($"    !! t={t,6:F0}s  {notRed[i]}({lights[notRed[i]].currentState}) " +
                                          $"+ {notRed[j]}({lights[notRed[j]].currentState}) hold the junction together");
                    }

            if (green.Contains("A") && green.Contains("B")) sawAplusB = true;
            if (green.Contains("A") && green.Contains("A2")) sawAplusA2 = true;
            if (green.Contains("B") && green.Contains("Bx")) sawBplusBx = true;

            var wait = cycle.Current as WaitForSeconds;
            float dur = wait != null ? wait.seconds : 0f;
            if (t < 125f)
            {
                string states = "";
                foreach (var n in sig) states += $"{n}={lights[n].currentState.ToString()[0]} ";
                timeline.Add($"    t={t,6:F0}s  +{dur,4:F0}s   {states}");
            }
            t += dur;
        }

        Console.WriteLine("\n--- first cycle timeline (106s) ---");
        foreach (var line in timeline) Console.WriteLine(line);

        Console.WriteLine();
        Check(violations == 0, $"no conflicting lanes ever held the junction together across 2 cycles ({steps} state changes)");
        Check(sawAplusB, "saw A + B green together (concurrent phase works)");
        Check(sawAplusA2, "saw A + A2 green together (the shared lane is in the same phase)");
        Check(sawBplusBx, "saw B + Bx green together (protected right turn)");
        foreach (var n in sig) Check(greenSeen.Contains(n), $"lane {n} did get a green during the cycle");
        Check(t >= 240f || steps >= 200, "the cycle kept running without stalling");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
