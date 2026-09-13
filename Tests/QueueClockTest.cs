using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// QueueClockTest — ข้อบกพร่องที่พบหลัง batch 216 รอบ (11 ก.ย. 2026)
///
/// อาการจากสนามจริง (phases_Adaptive_EveningPeak_Rep17, t=1104.6 -> t=1470.2):
///   H_B อยู่ในกลุ่มไฟเขียวติดกัน 24 phase ไม่เคยหลุดออกจากกลุ่ม
///   H_CB มีรถรออยู่ตลอดแต่ไม่ได้ไฟเลย 365.5 วินาที
///   รถ CB คันที่แย่ที่สุดรอ 344 วินาที ทั้งที่ maxWaitTime = 45
///   run นั้นมี 99 phase แต่ log StarvationEvents ไป 145 ครั้ง
///
/// สาเหตุ: ServeOnePhase รีเซ็ต queueStart เฉพาะหัวที่ "หลุดออกจากกลุ่ม" (dropping)
///         หัวที่อยู่ต่อข้าม phase จึงถือ queueStart เก่าค้างไม่จำกัดเวลา
///         -> ชนะทั้ง Q1 (FindStarvingHead) และ Q2 (ChooseLeaderByQueueStart) ตลอดกาล
///
/// ค่าคงที่ที่ต้องเป็นจริง: queueStart = "รอมานานแค่ไหนนับจากครั้งล่าสุดที่หัวนี้ได้ไฟเขียว"
/// ดังนั้นหัวที่เพิ่งได้เขียวจบเฟส ต้องมี queueStart ไม่เก่ากว่าเวลาที่เฟสนั้นเริ่ม
///
/// คอมไพล์: mcs -target:exe -out:test9.exe Test9.cs -r:gamecode.dll
///          (ห้ามใส่ไฟล์ stub ซ้ำ ไม่งั้น UnityEngine.Time จะกลายเป็นคนละตัวกับใน dll)
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
            // ทุกเลนมีรถค้างอยู่ 4 คันตลอดการทดสอบ = สภาพ EveningPeak ที่ไม่มีใครว่าง
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
        Console.WriteLine("=== QueueClockTest: นาฬิกาของหัวที่เขียวต่อเนื่อง ===");

        var ac = MakeController();
        var hB  = Head(ac, "H_B");
        var hCB = Head(ac, "H_CB");

        // จุดตั้งต้น t=1000 : CB เพิ่งจบไฟเขียวของมันพอดี (queueStart รีเซ็ตเป็น 1000)
        //                     B เขียวมาตั้งแต่ก่อนหน้านั้น ไม่เคยหลุดกลุ่ม -> นาฬิกาค้างที่ 900
        Time.time = 1000f;
        foreach (var h in ac.Heads) { h.lastCount = 4; h.queueStart = 1000f; }
        hB.queueStart = 900f;

        // ลำดับกลุ่มที่เกิดขึ้นจริงใน log: B อยู่ในทุกกลุ่ม, CB ไม่เคยถูกเลือก
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
            Time.time += 20f;                 // เฟสละ 20 วิ = ค่าที่ log จริงโดน starvation ตัดพอดี
            // ลำดับเดียวกับ ServeOnePhase: phase end ของกลุ่มเดิม -> แล้วค่อยตัดสินใจกลุ่มใหม่
            ac.ResetQueueStartFor(active);
            active = Sig(ac, g);
        }

        float now = Time.time;
        Console.WriteLine($"    หลัง {sequence.Length} เฟส เวลา t={now:F0} (B เขียวทุกเฟส, CB ไม่ได้เลย)");
        foreach (var h in ac.Heads)
        {
            string w = float.IsInfinity(h.queueStart) ? "-" : $"{now - h.queueStart:F0}s";
            Console.WriteLine($"      {h.name,-5} queueStart={h.queueStart,7:F0}  ระบบคิดว่ารอ {w,5}");
        }

        Check(hB.queueStart >= lastPhaseStart,
              "หัวที่เพิ่งได้ไฟเขียวจบเฟส ต้องมี queueStart ไม่เก่ากว่าเวลาที่เฟสนั้นเริ่ม");

        Check(hCB.queueStart < hB.queueStart,
              "หัวที่อดจริง (CB) ต้องมีคิวเก่ากว่าหัวที่เพิ่งได้เขียว (B)");

        var starving = ac.FindStarvingHead(0.95f, null);
        Console.WriteLine($"      Q1 FindStarvingHead      -> {(starving == null ? "null" : starving.name)}");
        Check(starving == hCB, "Q1 ต้องชี้ไปที่ H_CB ไม่ใช่ H_B ที่เขียวอยู่ตลอด");

        var leader = ac.ChooseLeaderByQueueStart(null);
        Console.WriteLine($"      Q2 ChooseLeaderByQueueStart -> {(leader == null ? "null" : leader.name)}");
        Check(leader == hCB, "Q2 ต้องเลือก H_CB เป็น leader ของเฟสถัดไป");

        // ================================================================
        // ลำดับ reset เทียบกับการตัดสินใจ — เส้นทางที่เทสเดิมไม่เคยเดิน
        // ================================================================

        Console.WriteLine("\n--- เคส 1: หัวที่เพิ่งได้เขียว ต้องไม่ชนะ Q2 ซ้ำ ---");
        var ac3 = MakeController();
        var a3  = Head(ac3, "H_A");  var b3  = Head(ac3, "H_B");
        var bx3 = Head(ac3, "H_Bx"); var cb3 = Head(ac3, "H_CB");

        // t=6 : A กับ CB มีรถคันแรกใน snapshot เดียวกัน -> queueStart เท่ากันเป๊ะ
        Time.time = 6f;
        a3.lastCount = 1;  a3.queueStart = 6f;
        cb3.lastCount = 1; cb3.queueStart = 6f;
        b3.lastCount = 0;  b3.queueStart  = float.PositiveInfinity;
        bx3.lastCount = 0; bx3.queueStart = float.PositiveInfinity;
        Check(ac3.ChooseLeaderByQueueStart(null) == a3,
              "เฟส 1: เสมอกัน -> A ชนะด้วย ORIGINAL_SEQUENCE");

        // t=8 : B กับ Bx เริ่มมีรถ
        Time.time = 8f;
        b3.lastCount = 5;  b3.queueStart  = 8f;
        bx3.lastCount = 5; bx3.queueStart = 8f;

        // t=14 : เฟส 1 (เขียว 8 วิ) จบ -> phase end reset ของ {A, A2} ก่อนตัดสินใจ
        Time.time = 14f;
        ac3.ResetQueueStartFor(Sig(ac3, "A", "A2"));
        var lead2 = ac3.ChooseLeaderByQueueStart(null);
        Console.WriteLine($"      queueStart: A={a3.queueStart:F1} B={b3.queueStart:F1} " +
                          $"Bx={bx3.queueStart:F1} CB={cb3.queueStart:F1}");
        Console.WriteLine($"      Q2 -> {(lead2 == null ? "null" : lead2.name)}");
        Check(lead2 == cb3,
              "เฟส 2 ต้องได้ CB (คิวเก่าสุดที่เหลือ) ไม่ใช่ A ที่เพิ่งได้เขียวไป 8 วิ");

        Console.WriteLine("\n--- เคส 2: เขียวยาวต้องไม่ทำให้ตัวเองเข้าเกณฑ์ Q1 ---");
        var ac4 = MakeController();
        var a4  = Head(ac4, "H_A");  var bx4 = Head(ac4, "H_Bx"); var cb4 = Head(ac4, "H_CB");
        Time.time = 0f;
        foreach (var h in ac4.Heads) { h.lastCount = 4; h.queueStart = 0f; }
        bx4.lastCount = 0; bx4.queueStart = float.PositiveInfinity;   // Bx ว่าง เหลือ CB ที่อดจริง

        // A+A2+B ได้เขียว 44.53 วิ (D = 18 ชนเพดานพอดี) แล้วยังมีรถเหลือ
        Time.time = 44.53f;
        Check(ac4.FindStarvingHead(0.95f, null) == a4,
              "ก่อน reset: A เข้าเกณฑ์ Q1 ด้วยเวลาที่ตัวเองเป็นไฟเขียว = อาการของบั๊ก");
        ac4.ResetQueueStartFor(Sig(ac4, "A", "A2", "B"));
        var st4 = ac4.FindStarvingHead(0.95f, null);
        Console.WriteLine($"      Q1 หลัง phase-end reset -> {(st4 == null ? "null" : st4.name)}");
        Check(st4 == cb4,
              "หลัง reset: Q1 ต้องชี้ CB ที่รอไฟแดงจริง 44.53 วิ ไม่ใช่ A ที่เพิ่งได้เขียว 44.53 วิ");

        // ---- ตัวนับต้องถูกล้างเมื่อสลับไปคุม Fixed ----
        Console.WriteLine("\n--- ForceStop ต้องล้างตัวนับ SODA ---");
        var ac2 = MakeController();
        var f = typeof(AdaptiveController).GetField("queueStartResets",
                    BindingFlags.NonPublic | BindingFlags.Instance);
        var f2 = typeof(AdaptiveController).GetField("skippedHeadTotal",
                    BindingFlags.NonPublic | BindingFlags.Instance);
        f.SetValue(ac2, 50); f2.SetValue(ac2, 225);
        Console.WriteLine($"      ก่อน ForceStop: QueueStartResets={ac2.QueueStartResets} SkippedHeadTotal={ac2.SkippedHeadTotal}");
        ac2.ForceStop();
        Console.WriteLine($"      หลัง ForceStop: QueueStartResets={ac2.QueueStartResets} SkippedHeadTotal={ac2.SkippedHeadTotal}");
        Check(ac2.QueueStartResets == 0 && ac2.SkippedHeadTotal == 0,
              "ForceStop ล้างตัวนับ -> แถว Fixed ไม่แบกค่าของ Adaptive รอบก่อนหน้า");

        // ---- กันไม่ให้หายไปจาก production: ServeOnePhase ต้องเรียก ResetContinuingHeads จริง ----
        string src = System.IO.File.ReadAllText("Assets/Scripts/AdaptiveController.cs");
        int serve = src.IndexOf("IEnumerator ServeOnePhase");
        int after = serve >= 0 ? src.IndexOf("IEnumerator GreenPhaseWithInterrupts", serve) : -1;
        string body = (serve >= 0 && after > serve) ? src.Substring(serve, after - serve) : "";
        int iReset = body.IndexOf("ResetQueueStartFor(activeGroup)");
        int iQ1    = body.IndexOf("FindStarvingHead");
        int iQ2    = body.IndexOf("ChooseLeaderByQueueStart");
        Check(iReset >= 0, "ServeOnePhase เรียก ResetQueueStartFor(activeGroup) เป็น phase-end reset");
        Check(iReset >= 0 && iQ1 > iReset, "phase-end reset ต้องอยู่ *ก่อน* Q1 (FindStarvingHead)");
        Check(iReset >= 0 && iQ2 > iReset, "phase-end reset ต้องอยู่ *ก่อน* Q2 (ChooseLeaderByQueueStart)");
        Check(!body.Contains("ResetContinuingHeads"),
              "ต้องไม่เหลือการรีเซ็ตแบบแยกสองที่ (รวมเป็นครั้งเดียวที่ต้นเฟสแล้ว)");

        int tb = src.IndexOf("void TraceBegin");
        int tbEnd = tb >= 0 ? src.IndexOf("void TraceEnter", tb) : -1;
        string tbBody = (tb >= 0 && tbEnd > tb) ? src.Substring(tb, tbEnd - tb) : "";
        Check(tbBody.IndexOf("phaseCounter++") >= 0 &&
              tbBody.IndexOf("phaseCounter++") < tbBody.IndexOf("if (!showDecisionPanel) return;"),
              "phaseCounter++ อยู่ก่อน early return -> PhaseIndex เดินเลขแม้ปิด decision panel");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
