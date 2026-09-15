using System;
using System.Collections.Generic;
using UnityEngine;

/// Checks how batch results are aggregated (ScenarioAggregate + GetOverall)
public class BatchMathTest
{
    static int fail = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) fail++;
    }
    static bool Near(float a, float b, float eps = 0.01f) => Math.Abs(a - b) < eps;

    public static int Main()
    {
        Console.WriteLine("--- ScenarioAggregate ---");

        var agg = new ScenarioManager.ScenarioAggregate { name = "MorningPeak" };
        Check(!agg.HasBoth, "empty -> HasBoth = false");
        Check(agg.FixedAvgWait < 0, "no data -> returns -1, not 0 (guards against dividing by zero)");
        Check(Near(agg.ImprovementPercent, 0f), "no data -> improvement = 0, no blow-up");

        agg.fixedWait.AddRange(new[] { 30f, 34f, 32f });
        Check(!agg.HasBoth, "Fixed only -> HasBoth still false");
        Check(Near(agg.FixedAvgWait, 32f), $"Fixed mean = {agg.FixedAvgWait:F2} (expected 32)");

        agg.adaptiveWait.AddRange(new[] { 24f, 26f, 25f });
        Check(agg.HasBoth, "both present -> HasBoth = true");
        Check(Near(agg.AdaptiveAvgWait, 25f), $"Adaptive mean = {agg.AdaptiveAvgWait:F2} (expected 25)");
        Check(Near(agg.ImprovementPercent, 21.875f),
              $"improvement = {agg.ImprovementPercent:F3}% (expected (32-25)/32 = 21.875)");
        Check(agg.PairCount == 3, $"PairCount = {agg.PairCount} (expected 3)");

        // Unequal counts -> PairCount takes the smaller
        agg.fixedWait.Add(40f);
        Check(agg.PairCount == 3, $"Fixed 4 / Adaptive 3 -> PairCount = {agg.PairCount} (expected 3)");

        Console.WriteLine("\n--- direction of the difference ---");
        var worse = new ScenarioManager.ScenarioAggregate { name = "X" };
        worse.fixedWait.Add(20f);
        worse.adaptiveWait.Add(25f);
        Check(worse.ImprovementPercent < 0f,
              $"Adaptive worse -> improvement is negative ({worse.ImprovementPercent:F1}%)");

        var same = new ScenarioManager.ScenarioAggregate { name = "Y" };
        same.fixedWait.Add(10f); same.adaptiveWait.Add(10f);
        Check(Near(same.ImprovementPercent, 0f), "equal -> 0%");

        var zero = new ScenarioManager.ScenarioAggregate { name = "Z" };
        zero.fixedWait.Add(0f); zero.adaptiveWait.Add(0f);
        Check(Near(zero.ImprovementPercent, 0f), "Fixed = 0 (nothing waiting) -> no division by zero");

        Console.WriteLine("\n--- GetOverall (mean across scenarios) ---");
        var sm = new ScenarioManager();

        float f, a, p;
        sm.GetOverall(out f, out a, out p);
        Check(f < 0f && Near(p, 0f), "no scenarios yet -> returns -1 / 0 without crashing");

        var s1 = new ScenarioManager.ScenarioAggregate { name = "S1" };
        s1.fixedWait.Add(40f); s1.adaptiveWait.Add(30f);      // 25% better
        var s2 = new ScenarioManager.ScenarioAggregate { name = "S2" };
        s2.fixedWait.Add(20f); s2.adaptiveWait.Add(10f);      // 50% better
        var s3 = new ScenarioManager.ScenarioAggregate { name = "S3_incomplete" };
        s3.fixedWait.Add(99f);                                 // no adaptive data yet
        sm.Results.Add(s1); sm.Results.Add(s2); sm.Results.Add(s3);

        sm.GetOverall(out f, out a, out p);
        Check(Near(f, 30f), $"Fixed overall = {f:F2} (expected (40+20)/2 = 30, excluding the incomplete scenario)");
        Check(Near(a, 20f), $"Adaptive overall = {a:F2} (expected (30+10)/2 = 20)");
        Check(Near(p, 33.333f), $"overall improvement = {p:F2}% (expected (30-20)/30 = 33.33)");

        Console.WriteLine("\n--- Progress ---");
        Check(Near(sm.Progress01, 0f), "not started -> progress 0, no division by zero");
        Check(sm.EstimatedRemaining < 0f, "not running -> ETA = -1 (cannot be estimated yet)");
        Check(sm.Results.Count == 3, "every scenario was recorded");

        Console.WriteLine(fail == 0 ? "\nALL CHECKS PASSED" : $"\n{fail} CHECK(S) FAILED");
        return fail;
    }
}
