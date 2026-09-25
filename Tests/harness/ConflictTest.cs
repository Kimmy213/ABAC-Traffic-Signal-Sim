using System;
using System.Collections.Generic;
using UnityEngine;

public class ConflictTest
{
    static int failures = 0;

    static void Check(bool cond, string label)
    {
        Console.WriteLine((cond ? "  PASS  " : "  FAIL  ") + label);
        if (!cond) failures++;
    }

    public static int Main()
    {
        var cfg = new IntersectionConfig();
        cfg.LoadABACPreset();

        Console.WriteLine($"lanes={cfg.lanes.Count}  signalized={cfg.SignalCount}");
        Check(cfg.lanes.Count == 7, "7 lanes total (A, A2, Ax, B, Bx, CB, CA)");
        Check(cfg.SignalCount == 5, "5 signalized lanes (A, A2, B, Bx, CB)");
        Check(cfg.SignalIndexOf("Ax") == -1, "Ax is free-flow -> no signal index");
        Check(cfg.SignalIndexOf("CA") == -1, "CA is free-flow -> no signal index");
        Check(cfg.SignalIndexOf("A2") != -1, "A2 IS signalized -> has a signal index");

        Func<string, string, bool> C = (a, b) =>
            cfg.AreConflicting(cfg.SignalIndexOf(a), cfg.SignalIndexOf(b));

        Console.WriteLine("\n--- conflict matrix ---");
        Check(C("A", "Bx"), "A x Bx conflict");
        Check(C("A", "CB"), "A x CB conflict");
        Check(C("A2", "Bx"), "A2 x Bx conflict (mirrors A)");
        Check(C("A2", "CB"), "A2 x CB conflict (mirrors A)");
        Check(C("B", "CB"), "B x CB conflict");
        Check(C("Bx", "CB"), "Bx x CB conflict  <-- per the scope");
        Check(!C("A", "B"), "A + B compatible (opposing through movements)");
        Check(!C("A", "A2"), "A + A2 compatible (same lamp, so they must always be able to run together)");
        Check(!C("A2", "B"), "A2 + B compatible");
        Check(!C("B", "Bx"), "B + Bx compatible (same approach)");
        Check(!C("A", "A"), "self is never a conflict");

        Console.WriteLine("\n--- symmetry ---");
        bool sym = true;
        string[] names = { "A", "A2", "B", "Bx", "CB" };
        foreach (var a in names)
            foreach (var b in names)
                if (C(a, b) != C(b, a)) sym = false;
        Check(sym, "matrix is symmetric");

        Console.WriteLine("\n--- fixed phase plan safety ---");
        var plans = new Dictionary<string, string[]>
        {
            { "P1 MainThrough",   new[] { "A", "A2", "B" } },
            { "P2 EastRightTurn", new[] { "B", "Bx" } },
            { "P3 ABACExit",      new[] { "CB" } },
        };
        foreach (var kv in plans)
        {
            bool safe = true;
            for (int i = 0; i < kv.Value.Length; i++)
                for (int j = i + 1; j < kv.Value.Length; j++)
                    if (C(kv.Value[i], kv.Value[j])) safe = false;
            Check(safe, $"{kv.Key} = {{{string.Join(",", kv.Value)}}} has no conflict within the phase");
        }

        // Every signalised lane must get green in at least one phase, or it starves permanently
        var covered = new HashSet<string>();
        foreach (var kv in plans) foreach (var l in kv.Value) covered.Add(l);
        foreach (var n in names) Check(covered.Contains(n), $"lane {n} gets a green in the fixed plan");

        Console.WriteLine("\n--- greedy group building (mirrors AdaptiveController logic) ---");
        // leader = CB -> nobody may join
        Check(GroupOf(cfg, "CB", names).Count == 1, "leader=CB -> the group is CB alone");
        // leader = A -> both A2 (same lamp) and B (opposing) may join
        var gA = GroupOf(cfg, "A", names);
        Check(gA.Count == 3 && gA.Contains("A") && gA.Contains("A2") && gA.Contains("B"),
              $"leader=A -> {{{string.Join(",", gA)}}}");
        // leader = Bx -> only B may join (A conflicts, CB conflicts)
        var gBx = GroupOf(cfg, "Bx", names);
        Check(gBx.Count == 2 && gBx.Contains("Bx") && gBx.Contains("B"), $"leader=Bx -> {{{string.Join(",", gBx)}}}");
        // leader = B -> first match wins, but the result must never contain a conflict
        var gB = GroupOf(cfg, "B", names);
        Console.WriteLine($"         leader=B -> {{{string.Join(",", gB)}}}");
        bool gBsafe = true;
        for (int i = 0; i < gB.Count; i++)
            for (int j = i + 1; j < gB.Count; j++)
                if (C(gB[i], gB[j])) gBsafe = false;
        Check(gBsafe, "the group from leader=B contains no conflict");

        Console.WriteLine("\n--- stable hash reproducibility ---");
        Check(IntersectionConfig.StableHash("MorningPeak") == IntersectionConfig.StableHash("MorningPeak"),
              "StableHash is stable");
        Check(IntersectionConfig.StableHash("MorningPeak") != IntersectionConfig.StableHash("EveningPeak"),
              "different scenarios give different seeds");
        Check(IntersectionConfig.StableHash("A") != IntersectionConfig.StableHash("Ax"),
              "different lanes give different seeds");
        Check(IntersectionConfig.StableHash("MorningPeak") >= 0, "seeds are never negative");

        // ---------- test case presets ----------
        Console.WriteLine("\n--- test case presets ---");
        var cases = TestCasePresets.BuildDefault();
        Check(cases.Count >= 13, $"{cases.Count} test cases present");

        int scripted = 0, poisson = 0;
        foreach (var tc in cases)
        {
            if (tc.mode == ArrivalMode.Scripted) scripted++; else poisson++;
            Check(!string.IsNullOrEmpty(tc.name), $"'{tc.name}' has a name");
            Check(!string.IsNullOrEmpty(tc.description), $"'{tc.name}' has a description");
            if (tc.name == "ForeverTest")
                Check(tc.suggestedDuration == 0f && tc.mode == ArrivalMode.Poisson, "'ForeverTest' is Poisson with no time limit");
            else
                Check(tc.suggestedDuration > 0, $"'{tc.name}' has a run length");

            if (tc.mode == ArrivalMode.Scripted)
            {
                Check(tc.TotalScriptedCars > 0, $"'{tc.name}' releases {tc.TotalScriptedCars} cars");
                // Every lane referenced must exist in the config
                foreach (var sch in tc.schedules)
                    Check(cfg.FindLane(sch.laneName) != null, $"'{tc.name}' refers to the existing lane {sch.laneName}");
            }
            else
            {
                foreach (var r in tc.rates)
                {
                    Check(cfg.FindLane(r.laneName) != null, $"'{tc.name}' refers to the existing lane {r.laneName}");
                    Check(r.meanInterval > 0, $"'{tc.name}' lane {r.laneName} interval > 0");
                }
            }
        }
        Check(scripted >= 8, $"{scripted} scripted cases (deterministic, T13 shared-lane demo included)");
        Check(poisson >= 5, $"{poisson} Poisson cases");

        // T13: the shared-lane blocking demo needs both A2 (waiting at the red) and Ax (getting blocked)
        var t13 = cases.Find(c => c.name.StartsWith("T13"));
        Check(t13 != null, "T13 shared-lane blocking demo present");
        if (t13 != null)
        {
            var a2 = t13.GetSchedule("A2");
            int a2Cars = 0; foreach (var b in a2) a2Cars += b.carCount;
            Check(a2Cars > 0, $"T13: A2 has {a2Cars} cars (they must wait at the red periodically)");
            var ax = t13.GetSchedule("Ax");
            int axCars = 0; foreach (var b in ax) axCars += b.carCount;
            Check(axCars > 0, $"T13: Ax has {axCars} cars (the ones that should get blocked)");
        }

        // Poisson: A and A2 intervals must follow the split exactly in every case
        // A2_SHARE = 5.4% (direct count from A_morning: 56 through vehicles, only 3 in the shared lane)
        // -> the A2/A ratio should be (1-0.054)/0.054 = 17.52 (a much longer A2 interval = far fewer cars)
        const float expectedRatio = (1f - 0.054f) / 0.054f;
        Console.WriteLine($"\n--- A/A2 split invariant (every Poisson case, expected ratio={expectedRatio:F3}) ---");
        foreach (var tc in cases)
        {
            if (tc.mode != ArrivalMode.Poisson) continue;
            float aRate = tc.GetInterval("A", -1f);
            float a2Rate = tc.GetInterval("A2", -1f);
            float ratio = aRate > 0 ? a2Rate / aRate : -1f;
            Check(aRate > 0 && a2Rate > 0 && Math.Abs(ratio - expectedRatio) < 0.01f,
                  $"'{tc.name}': A={aRate:F1}s, A2={a2Rate:F1}s, ratio={ratio:F3} (expected {expectedRatio:F3})");
            Check(a2Rate > aRate, $"'{tc.name}': the A2 interval is longer than A (fewer cars in the outer lane, as measured)");
        }

        // Specific checks: T4 must really have exactly one CB vehicle
        var t4 = cases.Find(c => c.name.StartsWith("T4"));
        Check(t4 != null, "T4 starvation test present");
        if (t4 != null)
        {
            var cb = t4.GetSchedule("CB");
            int cbCars = 0; foreach (var b in cb) cbCars += b.carCount;
            Check(cbCars == 1, $"T4: CB has 1 car (got {cbCars})");
            var a = t4.GetSchedule("A");
            int aCars = 0; foreach (var b in a) aCars += b.carCount;
            Check(aCars >= 30, $"T4: A is loaded with {aCars} cars");
        }

        // T6 must contain only free lanes
        var t6 = cases.Find(c => c.name.StartsWith("T6"));
        if (t6 != null)
        {
            bool onlyFree = true;
            foreach (var sch in t6.schedules)
            {
                var lane = cfg.FindLane(sch.laneName);
                if (lane != null && lane.isSignalized && sch.TotalCars > 0) onlyFree = false;
            }
            Check(onlyFree, "T6: free lanes only (Ax, CA), no signalised lane");
        }

        // T8 MorningPeak: CB/CA must be genuinely closed (confirmed by security), not merely quiet
        var t8 = cases.Find(c => c.name.StartsWith("T8"));
        if (t8 != null)
        {
            float cbInt = t8.GetInterval("CB", -1f);
            float caInt = t8.GetInterval("CA", -1f);
            Check(cbInt > 100000f, $"T8: CB closed (interval={cbInt:F0}s, so large that almost nothing arrives)");
            Check(caInt > 100000f, $"T8: CA closed (interval={caInt:F0}s, so large that almost nothing arrives)");
        }

        // Burst end time is computed correctly
        var burst = new ScheduledBurst(10f, 5, 2f);
        Check(Math.Abs(burst.EndTime - 18f) < 0.001f, $"a burst of 5 with spacing 2 starting at t=10 ends at t={burst.EndTime} (expected 18)");
        var single = new ScheduledBurst(10f, 1, 2f);
        Check(Math.Abs(single.EndTime - 10f) < 0.001f, "a single-car burst ends immediately, with no spacing added");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }

    // Mirrors BuildCompatibleGroup: the leader first, then add whatever is compatible
    static List<string> GroupOf(IntersectionConfig cfg, string leader, string[] all)
    {
        var group = new List<int> { cfg.SignalIndexOf(leader) };
        foreach (var c in all)
        {
            if (c == leader) continue;
            int idx = cfg.SignalIndexOf(c);
            if (cfg.IsCompatibleWithGroup(idx, group)) group.Add(idx);
        }
        var outp = new List<string>();
        foreach (var g in group) outp.Add(cfg.SignalName(g));
        return outp;
    }
}
