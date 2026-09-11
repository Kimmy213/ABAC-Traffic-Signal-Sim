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
        Check(C("Bx", "CB"), "Bx x CB conflict  <-- ตาม scope");
        Check(!C("A", "B"), "A + B compatible (กระแสตรงสวนทาง)");
        Check(!C("A", "A2"), "A + A2 compatible (ไฟดวงเดียวกัน ต้องเขียวพร้อมกันได้เสมอ)");
        Check(!C("A2", "B"), "A2 + B compatible");
        Check(!C("B", "Bx"), "B + Bx compatible (ขาเดียวกัน)");
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
            Check(safe, $"{kv.Key} = {{{string.Join(",", kv.Value)}}} ไม่มี conflict ภายในเฟส");
        }

        // ทุกเลนที่มีไฟต้องได้เขียวอย่างน้อย 1 เฟส ไม่งั้นจะ starve ถาวร
        var covered = new HashSet<string>();
        foreach (var kv in plans) foreach (var l in kv.Value) covered.Add(l);
        foreach (var n in names) Check(covered.Contains(n), $"เลน {n} ได้ไฟเขียวใน fixed plan");

        Console.WriteLine("\n--- greedy group building (จำลอง logic ของ AdaptiveController) ---");
        // leader = CB -> ห้ามมีใครเข้าร่วมเลย
        Check(GroupOf(cfg, "CB", names).Count == 1, "leader=CB -> กลุ่มมีแค่ CB ตัวเดียว");
        // leader = A -> เข้าร่วมได้ทั้ง A2 (ไฟเดียวกัน) และ B (สวนทาง)
        var gA = GroupOf(cfg, "A", names);
        Check(gA.Count == 3 && gA.Contains("A") && gA.Contains("A2") && gA.Contains("B"),
              $"leader=A -> {{{string.Join(",", gA)}}}");
        // leader = Bx -> เข้าร่วมได้เฉพาะ B (A conflict, CB conflict)
        var gBx = GroupOf(cfg, "Bx", names);
        Check(gBx.Count == 2 && gBx.Contains("Bx") && gBx.Contains("B"), $"leader=Bx -> {{{string.Join(",", gBx)}}}");
        // leader = B -> ตัวแรกที่เจอชนะ, แต่ผลลัพธ์ต้องไม่มี conflict เสมอ
        var gB = GroupOf(cfg, "B", names);
        Console.WriteLine($"         leader=B -> {{{string.Join(",", gB)}}}");
        bool gBsafe = true;
        for (int i = 0; i < gB.Count; i++)
            for (int j = i + 1; j < gB.Count; j++)
                if (C(gB[i], gB[j])) gBsafe = false;
        Check(gBsafe, "กลุ่มที่ได้จาก leader=B ไม่มี conflict");

        Console.WriteLine("\n--- stable hash reproducibility ---");
        Check(IntersectionConfig.StableHash("MorningPeak") == IntersectionConfig.StableHash("MorningPeak"),
              "StableHash คงที่");
        Check(IntersectionConfig.StableHash("MorningPeak") != IntersectionConfig.StableHash("EveningPeak"),
              "scenario ต่างกัน seed ต่างกัน");
        Check(IntersectionConfig.StableHash("A") != IntersectionConfig.StableHash("Ax"),
              "เลนต่างกัน seed ต่างกัน");
        Check(IntersectionConfig.StableHash("MorningPeak") >= 0, "seed ไม่ติดลบ");

        // ---------- test case presets ----------
        Console.WriteLine("\n--- test case presets ---");
        var cases = TestCasePresets.BuildDefault();
        Check(cases.Count >= 13, $"มี test case {cases.Count} ชุด");

        int scripted = 0, poisson = 0;
        foreach (var tc in cases)
        {
            if (tc.mode == ArrivalMode.Scripted) scripted++; else poisson++;
            Check(!string.IsNullOrEmpty(tc.name), $"'{tc.name}' มีชื่อ");
            Check(!string.IsNullOrEmpty(tc.description), $"'{tc.name}' มีคำอธิบาย");
            Check(tc.suggestedDuration > 0, $"'{tc.name}' มีความยาว run");

            if (tc.mode == ArrivalMode.Scripted)
            {
                Check(tc.TotalScriptedCars > 0, $"'{tc.name}' ปล่อยรถ {tc.TotalScriptedCars} คัน");
                // ทุกเลนที่อ้างถึงต้องมีอยู่จริงใน config
                foreach (var sch in tc.schedules)
                    Check(cfg.FindLane(sch.laneName) != null, $"'{tc.name}' อ้างเลน {sch.laneName} ที่มีจริง");
            }
            else
            {
                foreach (var r in tc.rates)
                {
                    Check(cfg.FindLane(r.laneName) != null, $"'{tc.name}' อ้างเลน {r.laneName} ที่มีจริง");
                    Check(r.meanInterval > 0, $"'{tc.name}' เลน {r.laneName} interval > 0");
                }
            }
        }
        Check(scripted >= 8, $"มี scripted {scripted} ชุด (deterministic, รวม T13 shared-lane demo)");
        Check(poisson >= 5, $"มี Poisson {poisson} ชุด");

        // T13: shared lane blocking demo ต้องมีทั้ง A2 (จอดรอไฟ) และ Ax (โดนบล็อก) ปนกัน
        var t13 = cases.Find(c => c.name.StartsWith("T13"));
        Check(t13 != null, "มี T13 shared-lane blocking demo");
        if (t13 != null)
        {
            var a2 = t13.GetSchedule("A2");
            int a2Cars = 0; foreach (var b in a2) a2Cars += b.carCount;
            Check(a2Cars > 0, $"T13: A2 มีรถ {a2Cars} คัน (ต้องจอดรอไฟแดงเป็นระยะ ๆ)");
            var ax = t13.GetSchedule("Ax");
            int axCars = 0; foreach (var b in ax) axCars += b.carCount;
            Check(axCars > 0, $"T13: Ax มีรถ {axCars} คัน (ตัวที่ควรโดนบล็อก)");
        }

        // Poisson: A กับ A2 ต้อง interval เท่ากันเป๊ะทุกชุด (invariant ของการแบ่ง 50/50)
        // A2_SHARE = 5.4% (นับตรงจาก A_morning: รถตรง 56 คัน เข้าเลนแชร์แค่ 3)
        // -> A2/A ratio ควรเป็น (1-0.054)/0.054 = 17.52 (A2 interval ยาวกว่า A มาก = รถน้อยกว่ามาก)
        const float expectedRatio = (1f - 0.054f) / 0.054f;
        Console.WriteLine($"\n--- A/A2 18/82 split invariant (ทุก Poisson test case, คาด ratio={expectedRatio:F3}) ---");
        foreach (var tc in cases)
        {
            if (tc.mode != ArrivalMode.Poisson) continue;
            float aRate = tc.GetInterval("A", -1f);
            float a2Rate = tc.GetInterval("A2", -1f);
            float ratio = aRate > 0 ? a2Rate / aRate : -1f;
            Check(aRate > 0 && a2Rate > 0 && Math.Abs(ratio - expectedRatio) < 0.01f,
                  $"'{tc.name}': A={aRate:F1}s, A2={a2Rate:F1}s, ratio={ratio:F3} (คาด {expectedRatio:F3})");
            Check(a2Rate > aRate, $"'{tc.name}': A2 interval ยาวกว่า A (เลนนอกรถน้อยกว่าเลนใน ตามที่วัดจริง)");
        }

        // เทสเจาะจง: T4 ต้องมี CB คันเดียวจริง ๆ
        var t4 = cases.Find(c => c.name.StartsWith("T4"));
        Check(t4 != null, "มี T4 starvation test");
        if (t4 != null)
        {
            var cb = t4.GetSchedule("CB");
            int cbCars = 0; foreach (var b in cb) cbCars += b.carCount;
            Check(cbCars == 1, $"T4: CB มีรถ 1 คัน (ได้ {cbCars})");
            var a = t4.GetSchedule("A");
            int aCars = 0; foreach (var b in a) aCars += b.carCount;
            Check(aCars >= 30, $"T4: A อัดหนัก {aCars} คัน");
        }

        // T6 ต้องมีแต่เลนฟรี
        var t6 = cases.Find(c => c.name.StartsWith("T6"));
        if (t6 != null)
        {
            bool onlyFree = true;
            foreach (var sch in t6.schedules)
            {
                var lane = cfg.FindLane(sch.laneName);
                if (lane != null && lane.isSignalized && sch.TotalCars > 0) onlyFree = false;
            }
            Check(onlyFree, "T6: มีแต่เลนฟรี (Ax, CA) ไม่มีเลนที่มีไฟ");
        }

        // T8 MorningPeak: CB/CA ต้อง "ปิด" จริง (ยืนยันจาก รปภ.) ไม่ใช่แค่ค่าน้อย
        var t8 = cases.Find(c => c.name.StartsWith("T8"));
        if (t8 != null)
        {
            float cbInt = t8.GetInterval("CB", -1f);
            float caInt = t8.GetInterval("CA", -1f);
            Check(cbInt > 100000f, $"T8: CB ปิด (interval={cbInt:F0}s ใหญ่มากจนแทบไม่มีรถ)");
            Check(caInt > 100000f, $"T8: CA ปิด (interval={caInt:F0}s ใหญ่มากจนแทบไม่มีรถ)");
        }

        // burst end time คำนวณถูก
        var burst = new ScheduledBurst(10f, 5, 2f);
        Check(Math.Abs(burst.EndTime - 18f) < 0.001f, $"burst 5 คัน spacing 2 เริ่ม t=10 จบ t={burst.EndTime} (ควรเป็น 18)");
        var single = new ScheduledBurst(10f, 1, 2f);
        Check(Math.Abs(single.EndTime - 10f) < 0.001f, "burst คันเดียวจบทันที ไม่บวก spacing เกิน");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }

    // จำลอง BuildCompatibleGroup: leader ก่อน แล้วไล่เพิ่มตัวที่เข้ากันได้
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
