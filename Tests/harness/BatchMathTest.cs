using System;
using System.Collections.Generic;
using UnityEngine;

/// ตรวจการรวมผลของ batch (ScenarioAggregate + GetOverall)
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
        Check(!agg.HasBoth, "ว่างเปล่า -> HasBoth = false");
        Check(agg.FixedAvgWait < 0, "ยังไม่มีข้อมูล -> คืน -1 ไม่ใช่ 0 (กันหารศูนย์)");
        Check(Near(agg.ImprovementPercent, 0f), "ไม่มีข้อมูล -> improvement = 0 ไม่ระเบิด");

        agg.fixedWait.AddRange(new[] { 30f, 34f, 32f });
        Check(!agg.HasBoth, "มีแต่ Fixed -> ยัง HasBoth = false");
        Check(Near(agg.FixedAvgWait, 32f), $"Fixed mean = {agg.FixedAvgWait:F2} (ควร 32)");

        agg.adaptiveWait.AddRange(new[] { 24f, 26f, 25f });
        Check(agg.HasBoth, "ครบทั้งคู่ -> HasBoth = true");
        Check(Near(agg.AdaptiveAvgWait, 25f), $"Adaptive mean = {agg.AdaptiveAvgWait:F2} (ควร 25)");
        Check(Near(agg.ImprovementPercent, 21.875f),
              $"improvement = {agg.ImprovementPercent:F3}% (ควร (32-25)/32 = 21.875)");
        Check(agg.PairCount == 3, $"PairCount = {agg.PairCount} (ควร 3)");

        // จำนวนไม่เท่ากัน -> PairCount ใช้ตัวน้อย
        agg.fixedWait.Add(40f);
        Check(agg.PairCount == 3, $"Fixed 4 / Adaptive 3 -> PairCount = {agg.PairCount} (ควร 3)");

        Console.WriteLine("\n--- ทิศทางของผลต่าง ---");
        var worse = new ScenarioManager.ScenarioAggregate { name = "X" };
        worse.fixedWait.Add(20f);
        worse.adaptiveWait.Add(25f);
        Check(worse.ImprovementPercent < 0f,
              $"Adaptive แย่กว่า -> improvement ติดลบ ({worse.ImprovementPercent:F1}%)");

        var same = new ScenarioManager.ScenarioAggregate { name = "Y" };
        same.fixedWait.Add(10f); same.adaptiveWait.Add(10f);
        Check(Near(same.ImprovementPercent, 0f), "เท่ากัน -> 0%");

        var zero = new ScenarioManager.ScenarioAggregate { name = "Z" };
        zero.fixedWait.Add(0f); zero.adaptiveWait.Add(0f);
        Check(Near(zero.ImprovementPercent, 0f), "Fixed = 0 (ไม่มีรถรอ) -> ไม่หารศูนย์");

        Console.WriteLine("\n--- GetOverall (เฉลี่ยข้าม scenario) ---");
        var sm = new ScenarioManager();

        float f, a, p;
        sm.GetOverall(out f, out a, out p);
        Check(f < 0f && Near(p, 0f), "ยังไม่มี scenario -> คืน -1 / 0 ไม่ crash");

        var s1 = new ScenarioManager.ScenarioAggregate { name = "S1" };
        s1.fixedWait.Add(40f); s1.adaptiveWait.Add(30f);      // ลด 25%
        var s2 = new ScenarioManager.ScenarioAggregate { name = "S2" };
        s2.fixedWait.Add(20f); s2.adaptiveWait.Add(10f);      // ลด 50%
        var s3 = new ScenarioManager.ScenarioAggregate { name = "S3_incomplete" };
        s3.fixedWait.Add(99f);                                 // ยังไม่มี adaptive
        sm.Results.Add(s1); sm.Results.Add(s2); sm.Results.Add(s3);

        sm.GetOverall(out f, out a, out p);
        Check(Near(f, 30f), $"Fixed รวม = {f:F2} (ควร (40+20)/2 = 30, ไม่นับ scenario ที่ยังไม่ครบ)");
        Check(Near(a, 20f), $"Adaptive รวม = {a:F2} (ควร (30+10)/2 = 20)");
        Check(Near(p, 33.333f), $"improvement รวม = {p:F2}% (ควร (30-20)/30 = 33.33)");

        Console.WriteLine("\n--- Progress ---");
        Check(Near(sm.Progress01, 0f), "ยังไม่เริ่ม -> progress 0 ไม่หารศูนย์");
        Check(sm.EstimatedRemaining < 0f, "ยังไม่รัน -> ETA = -1 (ยังประเมินไม่ได้)");
        Check(sm.Results.Count == 3, "เก็บ scenario ครบ");

        Console.WriteLine(fail == 0 ? "\nALL CHECKS PASSED" : $"\n{fail} CHECK(S) FAILED");
        return fail;
    }
}
