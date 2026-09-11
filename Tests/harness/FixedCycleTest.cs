using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// รัน RunCycle() ของ FixedController จริง ๆ แล้วตรวจ safety invariant ทุกช่วงเวลา
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

        // ผูก TrafficLight ปลอมให้เลนที่มีไฟ
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
        Check(Math.Abs(fc.GetCycleLength() - 106f) < 0.01f, "cycle = 106s ตามตารางสเตจใน SP1_HANDOFF_v2 (A=40 / B+Bx=20 / CB=31 + intergreen 15)");

        // ===== ไล่ RunCycle จริง =====
        fc.RestartCycle();
        IEnumerator cycle = fc.captured;
        Check(cycle != null, "RunCycle เริ่มทำงาน");

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

        // 2 รอบเต็ม = 212 วินาที (รอบละ 106s)
        while (cycle.MoveNext() && t < 240f && steps < 200)
        {
            steps++;

            // ---- ตรวจ invariant ที่สถานะปัจจุบัน ----
            var notRed = new List<string>();
            var green = new List<string>();
            foreach (var n in sig)
            {
                if (lights[n].currentState != TrafficLight.LightState.Red) notRed.Add(n);
                if (lights[n].currentState == TrafficLight.LightState.Green) { green.Add(n); greenSeen.Add(n); }
            }

            // กฎเหล็ก: เลนที่ conflict กันต้องไม่ "ไม่แดง" พร้อมกัน
            // (เหลืองก็ยังมีรถวิ่งอยู่ ถือว่ายังครองแยก)
            for (int i = 0; i < notRed.Count; i++)
                for (int j = i + 1; j < notRed.Count; j++)
                    if (C(notRed[i], notRed[j]))
                    {
                        violations++;
                        Console.WriteLine($"    !! t={t,6:F0}s  {notRed[i]}({lights[notRed[i]].currentState}) " +
                                          $"+ {notRed[j]}({lights[notRed[j]].currentState}) ครองแยกพร้อมกัน");
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

        Console.WriteLine("\n--- timeline รอบแรก (106s) ---");
        foreach (var line in timeline) Console.WriteLine(line);

        Console.WriteLine();
        Check(violations == 0, $"ไม่มีเลน conflict ครองแยกพร้อมกันเลยตลอด 2 รอบ ({steps} state changes)");
        Check(sawAplusB, "เห็น A + B เขียวพร้อมกันจริง (concurrent phase ทำงาน)");
        Check(sawAplusA2, "เห็น A + A2 เขียวพร้อมกันจริง (shared lane อยู่ phase เดียวกัน)");
        Check(sawBplusBx, "เห็น B + Bx เขียวพร้อมกันจริง (protected right turn)");
        foreach (var n in sig) Check(greenSeen.Contains(n), $"เลน {n} ได้ไฟเขียวจริงในรอบ");
        Check(t >= 240f || steps >= 200, "วนต่อเนื่องไม่หยุดกลางคัน");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
