using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// เทส 3 ชุดของ SODA (patched) ตาม §8 ของบันทึกรอบที่ 10
///   1. queueStart ถูก reset ตอนไฟเขียวจบ -> เลนที่ไม่เคยว่างต้องไม่ lock out เลนอื่น
///   2. gap-out ต้องเห็นว่างติดกัน 2 snapshot ไม่ใช่ครั้งเดียว
///   3. AdaptiveController ต้องไม่เรียก API ที่ต้องมี tracker เหลืออยู่เลย
/// </summary>
public class SodaTest
{
    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    /// <summary>สร้างแยกจำลอง: 5 signal (A A2 B Bx CB) โดย A/A2 ใช้ไฟดวงเดียวกัน</summary>
    static AdaptiveController MakeController(out Dictionary<string, ROIZone> rois)
    {
        var cfg = new IntersectionConfig();
        cfg.LoadABACPreset();

        rois = new Dictionary<string, ROIZone>();
        var lightA = new TrafficLight { laneName = "A" };

        foreach (var lane in cfg.lanes)
        {
            if (!lane.isSignalized) continue;
            // A กับ A2 ต้องชี้ TrafficLight "object เดียวกัน" เหมือนในซีนจริง
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

        Console.WriteLine("--- signal heads ที่สร้างได้ ---");
        foreach (var h in ac.Heads)
        {
            string lanes = "";
            foreach (int s in h.signals) lanes += ac.config.SignalName(s) + " ";
            Console.WriteLine($"    {h.name}: {lanes.Trim()}");
        }
        Check(ac.Heads.Count == 4, $"แยกนี้มี 4 หัวไฟ ไม่ใช่ 5 เลนที่มีไฟ (ได้ {ac.Heads.Count})");

        var hA = HeadNamed(ac, "H_A");
        Check(hA != null && hA.signals.Count == 2, "H_A คุมทั้ง A และ A2 (ไฟดวงเดียวกัน)");

        // ===============================================================================
        // เทส 1 — queueStart reset: เลนที่ไม่เคยว่างต้องไม่ยึดสิทธิ์ตลอดกาล
        // ===============================================================================
        Console.WriteLine("\n=== เทส 1: queueStart reset (กัน lock-out ตอน MorningPeak) ===");
        {
            // จำลอง Bx v/c = 1.51 — ไม่เคยว่างเลย ส่วนหัวอื่นมีรถเรื่อย ๆ เช่นกัน
            foreach (var h in ac.Heads) { h.lastCount = 5; h.queueStart = 100f; }
            var hBx = HeadNamed(ac, "H_Bx");
            hBx.queueStart = 50f;                    // Bx เริ่มก่อนใครเพื่อน

            var served = new Dictionary<string, int>();
            foreach (var h in ac.Heads) served[h.name] = 0;

            float clock = 200f;
            for (int phase = 0; phase < 10; phase++)
            {
                var leader = ac.ChooseLeaderByQueueStart(null);
                if (leader == null) { Check(false, "ไม่มี leader ทั้งที่ทุกหัวมีรถ"); break; }
                served[leader.name]++;

                // ไฟเขียวจบ: ยังมีรถเหลือ -> ต้อง reset queueStart เป็นเวลาปัจจุบัน
                clock += 30f;
                leader.queueStart = clock;           // = ResetQueueStartFor เมื่อ count > 0
            }

            string dist = "";
            foreach (var kv in served) dist += $"{kv.Key}={kv.Value} ";
            Console.WriteLine($"    ได้ไฟกี่ครั้งใน 10 เฟส: {dist}");

            int zero = 0;
            foreach (var kv in served) if (kv.Value == 0) zero++;
            Check(zero == 0, "ทุกหัวไฟได้ไฟอย่างน้อย 1 ครั้งใน 10 เฟส (ไม่มีเลนไหนถูก lock out)");
            Check(served["H_Bx"] <= 4, $"Bx ไม่ผูกขาดคิว (ได้ {served["H_Bx"]}/10 เฟส)");
        }

        // เทส 1b — ถ้า "ลืม" reset จะพังจริงไหม (พิสูจน์ว่าเทสนี้จับของจริง)
        Console.WriteLine("\n--- 1b: จำลองว่าลืม reset — ต้องพังให้เห็น ---");
        {
            foreach (var h in ac.Heads) { h.lastCount = 5; h.queueStart = 100f; }
            HeadNamed(ac, "H_Bx").queueStart = 50f;

            var served = new Dictionary<string, int>();
            foreach (var h in ac.Heads) served[h.name] = 0;
            for (int phase = 0; phase < 10; phase++)
            {
                var leader = ac.ChooseLeaderByQueueStart(null);
                served[leader.name]++;
                // ← ไม่ reset queueStart เลย
            }
            Console.WriteLine($"    H_Bx ได้ {served["H_Bx"]}/10 เฟส (คาดว่ายึดทั้งหมด)");
            Check(served["H_Bx"] == 10, "ยืนยัน: ถ้าลืม reset เลนที่ไม่เคยว่างจะยึดคิวทุกเฟสจริง");
        }

        // เทส 1c — ResetQueueStartFor ตัวจริง: มีรถ -> ตั้งเวลาใหม่, ว่าง -> Infinity
        Console.WriteLine("\n--- 1c: ResetQueueStartFor ตัวจริง ---");
        {
            var hB = HeadNamed(ac, "H_B");
            rois["B"].simulatedCountOverride = 4;          // ไฟเขียวจบแล้วยังมีรถเหลือ
            hB.queueStart = 10f;
            ac.ResetQueueStartFor(hB.signals);
            Check(!float.IsInfinity(hB.queueStart)
               && Math.Abs(hB.queueStart - Time.time) < 0.001f
               && Math.Abs(hB.queueStart - 10f) > 0.001f,
                  $"ยังมีรถเหลือ -> queueStart ถูกตั้งใหม่เป็นเวลาปัจจุบัน (10 -> {hB.queueStart})");

            rois["B"].simulatedCountOverride = 0;          // ระบายหมดพอดี
            ac.ResetQueueStartFor(hB.signals);
            Check(float.IsInfinity(hB.queueStart), "ระบายหมด -> queueStart = Infinity (ว่าง)");

            Check(ac.QueueStartResets >= 1, $"นับจำนวน reset ไว้ log ได้ ({ac.QueueStartResets})");
        }

        // ===============================================================================
        // เทส 2 — gap-out ต้องยืนยัน 2 snapshot ติดกัน
        // ===============================================================================
        Console.WriteLine("\n=== เทส 2: gap-out ต้องเห็นว่าง 2 snapshot ติด ===");
        {
            var ac2 = MakeController(out rois);
            var hB = HeadNamed(ac2, "H_B");

            // ทำให้ H_B เป็นกลุ่มที่กำลังเขียวอยู่
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
            Console.WriteLine($"    ลำดับ [3, 0, 2] -> streak = {streakA}");
            Check(streakA < ac2.gapOutSnapshots,
                  "[3, 0, 2] ต้องไม่ตัดเฟส (ว่างแค่ snapshot เดียวแล้วมีรถอีก)");

            int streakB = RunSeq(new[] { 3, 0, 0 });
            Console.WriteLine($"    ลำดับ [3, 0, 0] -> streak = {streakB}");
            Check(streakB >= ac2.gapOutSnapshots, "[3, 0, 0] ต้องตัดเฟส (ว่างติดกัน 2 snapshot)");

            Check(ac2.gapOutSnapshots == 2, "ต้องยืนยัน 2 snapshot");
            Check(Math.Abs(ac2.gapOutSnapshots * ac2.snapshotInterval - 4f) < 0.01f,
                  $"= {ac2.gapOutSnapshots * ac2.snapshotInterval:F0} วินาที (เดิม 3 วิ — ช้าลง 1 วิเป็นราคาของการยืนยัน)");
        }

        // ===============================================================================
        // เทส 3 — ไม่มีการเรียก API ที่ต้องมี tracker เหลืออยู่
        // ===============================================================================
        Console.WriteLine("\n=== เทส 3: ตัด tracker ออกจริงไหม ===");
        {
            string src = File.Exists("Scripts/AdaptiveController.cs")
                       ? File.ReadAllText("Scripts/AdaptiveController.cs") : null;
            Check(src != null, "อ่านไฟล์ AdaptiveController.cs ได้");

            if (src != null)
            {
                // ตัดคอมเมนต์ออกก่อน — ในคอมเมนต์เขียนชื่อ API พวกนี้ไว้เตือนได้
                string code = Regex.Replace(src, @"//.*?$", "", RegexOptions.Multiline);
                code = Regex.Replace(code, @"/\*.*?\*/", "", RegexOptions.Singleline);

                string[] trackerApis = { "GetOldestArrivalTime", "GetLongestWaitTime", "GetCarsInZone" };
                foreach (string api in trackerApis)
                    Check(!code.Contains(api),
                          $"ไม่เรียก {api}() (ต้องมี ByteTrack/DeepSORT ถึงจะทำได้จริง)");

                Check(code.Contains("GetSampledCount"),
                      "ใช้ GetSampledCount() ซึ่งกล้องที่ไม่มี tracker ทำได้");
                Check(!code.Contains("GetVehicleCount"),
                      "ไม่เรียก GetVehicleCount() ปนมาด้วย (ให้เหลือทางเดียว)");

                // rest-in-green ต้องหายไป — ไฟจริงปล่อยแดงทุกเลนตอนว่าง
                Check(code.Contains("AllRedIdle"), "มีสถานะ ALL-RED IDLE แทน rest in green");
            }

            // DataLogger ยังต้องใช้ ground truth ต่อไป — ตัวแปรตามของการทดลอง ไม่ใช่ input ของ controller
            string roiSrc = File.Exists("Scripts/ROIZone.cs") ? File.ReadAllText("Scripts/ROIZone.cs") : "";
            Check(roiSrc.Contains("GetOldestArrivalTime"),
                  "ROIZone ยังเก็บ API ตัวติดตามไว้ (DataLogger ใช้วัดผล ไม่ใช่ controller ใช้ตัดสินใจ)");
            Check(roiSrc.Contains("car.assignedLane"),
                  "ยัง log ด้วย car.assignedLane ไม่ใช่ roi.laneName (Ax คือตัวชี้วัดหลัก)");
        }

        // ===============================================================================
        // ตรวจว่าสิ่งที่ห้ามแตะ ยังอยู่ครบ
        // ===============================================================================
        Console.WriteLine("\n=== ของเดิมที่ห้ามแตะ ===");
        {
            var ac3 = MakeController(out rois);
            Check(Math.Abs(ac3.startupLostTime - 2.95f) < 1e-6f
               && Math.Abs(ac3.saturationHeadway - 2.31f) < 1e-6f, "สูตร G ยังใช้ค่าที่วัดจริง");
            Check(Math.Abs(ac3.yellowDuration - 3f) < 1e-6f
               && Math.Abs(ac3.allRedDuration - 2f) < 1e-6f, "clearance 3 + 2 วิเท่าเดิม");
            Check(Math.Abs(ac3.maxContinuousGreen - 90f) < 1e-6f, "max continuous green 90 วิ");
            Check(Math.Abs(ac3.maxWaitTime - 45f) < 1e-6f
               && Math.Abs(ac3.starvationSelectThreshold - 0.95f) < 1e-6f
               && Math.Abs(ac3.starvationInterruptThreshold - 0.90f) < 1e-6f,
                  "anti-starvation 45 วิ / 0.95 / 0.90 เท่าเดิม");

            // สูตร G ต้องให้ผลเดิมเป๊ะ
            rois["B"].simulatedCountOverride = 4;
            var hB = HeadNamed(ac3, "H_B");
            float g = ac3.CalculateGreenDuration(hB.signals);
            Console.WriteLine($"    D = 4 -> G = {g:F2}s (คาด 12.19)");
            Check(Math.Abs(g - 12.19f) < 0.01f, "G(4) = 2.95 + 4 x 2.31 = 12.19 วิ เท่าเดิมทุกประการ");

            // starvation ต้องใช้ queueStart ไม่ใช่ arrival รายคัน
            foreach (var h in ac3.Heads) { h.lastCount = 0; h.queueStart = Mathf.Infinity; }
            hB.lastCount = 2;
            hB.queueStart = Time.time - 43f;      // เกิน 0.95 x 45 = 42.75
            Check(ac3.FindStarvingHead(0.95f, null) == hB, "จับ starvation จาก queueStart ได้");
            hB.queueStart = Time.time - 10f;
            Check(ac3.FindStarvingHead(0.95f, null) == null, "รอไม่นานพอ ต้องไม่ถือว่า starving");
        }

        // ===============================================================================
        // เทส 4 — Decision Inspector: แผงต้องสะท้อนสิ่งที่อัลกอริทึมเห็นจริง
        // ===============================================================================
        Console.WriteLine("\n=== เทส 4: แผงขั้นตอนการคิด ===");
        {
            var ac4 = MakeController(out rois);
            var hA4  = HeadNamed(ac4, "H_A");
            var hB4 = HeadNamed(ac4, "H_B");
            var hBx = HeadNamed(ac4, "H_Bx");
            var hCB = HeadNamed(ac4, "H_CB");

            // สร้างสถานการณ์: Bx รอนานสุด, A รองลงมา, B เพิ่งมา, CB ว่าง
            float now = Time.time;
            hBx.lastCount = 9;  hBx.queueStart = now - 18.3f;
            hA4.lastCount  = 14; hA4.queueStart  = now - 12.1f;
            hB4.lastCount = 3;  hB4.queueStart = now - 4.0f;
            hCB.lastCount = 0;  hCB.queueStart = Mathf.Infinity;

            ac4.TraceDecisionInputs();

            Console.WriteLine("    -- QUEUE ที่แผงจะโชว์ --");
            foreach (string row in ac4.QueueView) Console.WriteLine("       " + row.Replace("|", "  "));

            Check(ac4.QueueView.Count == 4, $"แผงโชว์ครบทั้ง 4 หัวไฟ (ได้ {ac4.QueueView.Count})");
            Check(ac4.QueueView[0].Contains("H_Bx"), "อันดับ 1 ของคิว = H_Bx (queueStart เก่าสุด)");
            Check(ac4.QueueView[1].Contains("H_A"),  "อันดับ 2 = H_A");
            Check(ac4.QueueView[2].Contains("H_B"),  "อันดับ 3 = H_B");
            Check(ac4.QueueView[3].Contains("H_CB") && ac4.QueueView[3].Contains("SKIPPED"),
                  "H_CB ว่าง -> โชว์ว่า SKIPPED ไม่ใช่หายไปเฉย ๆ");

            var q1 = ac4.DecisionTrace[0];
            var q2 = ac4.DecisionTrace[1];
            Console.WriteLine($"    -- Q1 มี {q1.options.Count} แถว, Q2 มี {q2.options.Count} แถว --");
            foreach (string o in q1.options) Console.WriteLine("       Q1  " + o.Replace("|", "  "));

            Check(q1.options.Count == 4, "Q1 โชว์เกณฑ์ + หัวไฟที่มีรถ 3 ดวง");
            Check(q1.options[0].Contains("42.8") || q1.options[0].Contains("42.7"),
                  "Q1 โชว์เกณฑ์ starvation 42.75 วิ ให้เห็นว่าเทียบกับอะไร");
            Check(q2.options.Count == 4, "Q2 โชว์ผู้เข้าชิงครบทั้ง 4 หัวไฟ");

            bool cbIneligible = false;
            foreach (string o in q2.options)
                if (o.Contains("H_CB") && o.Contains("not eligible")) cbIneligible = true;
            Check(cbIneligible, "Q2 บอกเหตุผลว่าทำไม H_CB ไม่ได้เข้าชิง (ว่าง)");

            // ลำดับที่แผงโชว์ ต้องตรงกับ leader ที่อัลกอริทึมเลือกจริง
            var chosen = ac4.ChooseLeaderByQueueStart(null);
            Check(chosen == hBx, "leader ที่เลือกจริง = อันดับ 1 ที่แผงโชว์ (แผงไม่โกหก)");

            // regression: SimulationControlPanel.hideLegacyDebugGUI ปิด showDebugGUI ตั้งแต่ Start
            // ถ้า OnGUI ยัง return เพราะ showDebugGUI อย่างเดียว แผงนี้จะไม่มีวันโผล่ (เคยพลาดมาแล้ว)
            string ctl = File.Exists("Scripts/AdaptiveController.cs")
                       ? File.ReadAllText("Scripts/AdaptiveController.cs") : "";
            Check(ctl.Contains("!showDebugGUI && !showDecisionPanel"),
                  "OnGUI ออกก็ต่อเมื่อปิดทั้งสองแผง — แผง trace ไม่ผูกกับ showDebugGUI");
            Check(!ctl.Contains("if (!showDebugGUI || !Application.isPlaying"),
                  "ไม่เหลือเงื่อนไขเดิมที่ปิดแผง trace ไปด้วย");

            string sm = File.Exists("Scripts/ScenarioManager.cs")
                      ? File.ReadAllText("Scripts/ScenarioManager.cs") : "";
            Check(sm.Contains("showDecisionPanel = false"),
                  "batch 270 รอบต้องปิดแผงนี้ (ไม่สร้าง string ทิ้งทุกเฟสโดยไม่มีใครดู)");

            string cp = File.Exists("Scripts/SimulationControlPanel.cs")
                      ? File.ReadAllText("Scripts/SimulationControlPanel.cs") : "";
            Check(cp.Contains("showDecisionPanel"),
                  "มีปุ่มเปิด/ปิดในแผงควบคุม ไม่ต้องจำ hotkey");

            // 🛑 ลำดับใน OnGUI: ต้องอ่าน hotkey ก่อน early-return ไม่งั้นกดปิดแล้วเปิดกลับไม่ได้
            int iKey  = ctl.IndexOf("ev.keyCode == decisionPanelKey");
            int iExit = ctl.IndexOf("if (!showDebugGUI && !showDecisionPanel) return;");
            Console.WriteLine($"    ตำแหน่งใน OnGUI: อ่านปุ่ม @{iKey}, ทางออกด่วน @{iExit}");
            Check(iKey > 0 && iExit > 0 && iKey < iExit,
                  "อ่าน hotkey ก่อน early-return (กด D ปิดแล้วต้องกดเปิดกลับได้)");
            Check(ctl.Contains("ev.Use()"), "consume event หลังสลับ กันปุ่มถูกกินซ้ำ");
            Check(!cp.Contains("activeController == ControllerKind.Adaptive && adaptiveController != null"),
                  "ปุ่มในแผงควบคุมไม่หายตาม controller ที่เลือก — มีทางกลับเสมอ");
        }

        // ===============================================================================
        // เทส 5 — เขียวเปล่าที่ gap-out ปัจจุบันมองไม่เห็น (Ratchnon)
        //   gap-out ยิงเมื่อ "ทุกเลนในกลุ่มว่าง" เลนที่ว่างก่อนจึงติดเขียวรอเลนอื่นอยู่เฉย ๆ
        //   เทสนี้ "ยืนยันว่าปัญหามีจริง" และวัดขนาดของมัน — ยังไม่ได้แก้พฤติกรรม
        //   ถ้าวันไหนเปลี่ยนเป็น gap-out รายเลน เทสนี้จะเป็นตัวชี้ว่าประหยัดได้เท่าไหร่
        // ===============================================================================
        Console.WriteLine("\n=== เทส 5: เขียวเปล่าในกลุ่ม {B, Bx} ===");
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
            Console.WriteLine($"    G = max = {G:F2}s  ->  Bx ว่างตั้งแต่ {gBx:F2}s = เขียวเปล่า {G - gBx:F2}s");

            Check(Math.Abs(G - 26.05f) < 0.02f, $"G ของกลุ่ม = 26.05s (critical lane = B)");
            Check(Math.Abs((G - gBx) - 16.17f) < 0.02f, "Bx ติดเขียวเปล่า 16.17 วิ");

            // ทำไมถึงแพงกว่าที่เห็น: Bx เป็นตัวเดียวที่บล็อก A
            int iA = cfg5.SignalIndexOf("A"), iB = cfg5.SignalIndexOf("B"), iBx = cfg5.SignalIndexOf("Bx");
            Check(cfg5.AreConflicting(iA, iBx), "A ชนกับ Bx");
            Check(!cfg5.AreConflicting(iA, iB), "A ไม่ชนกับ B");
            Console.WriteLine($"    => ตัด Bx เร็ว 16.17s แล้ว A เขียวพร้อม B ได้เลย (A รอสั้นลง 16.17s)");

            // gap-out ปัจจุบันช่วยไม่ได้จริงไหม — B ยังมีรถ กลุ่มจึงไม่เคยว่างพร้อมกัน
            FieldInfo fG = typeof(AdaptiveController)
                .GetField("activeGroup", BindingFlags.NonPublic | BindingFlags.Instance);
            FieldInfo fS = typeof(AdaptiveController)
                .GetField("groupZeroStreak", BindingFlags.NonPublic | BindingFlags.Instance);
            fG.SetValue(ac5, new List<int>(grp));
            fS.SetValue(ac5, 0);

            rois["Bx"].simulatedCountOverride = 0;      // Bx ระบายหมดแล้ว
            for (int i = 0; i < 5; i++) ac5.Snapshot(); // B ยังมี 10 คัน
            int streak = (int)fS.GetValue(ac5);
            Console.WriteLine($"    Bx ว่าง 5 snapshot ติด แต่ streak = {streak}");
            Check(streak == 0, "ยืนยัน: gap-out ปัจจุบันไม่ยิงเลย เพราะ B ยังไม่ว่าง (ปัญหามีจริง)");

            // การวัดต้องจับเคสนี้ได้ ไม่งั้นรัน batch ไปก็ไม่มีหลักฐาน
            var dl = new DataLogger();
            dl.LogPhase(1, 0f, "B+Bx", G, G, "completed", $"B=25.0;Bx={gBx:F1}", G - gBx);
            Check(Math.Abs(dl.GetIdleLaneSeconds() - 16.17f) < 0.05f,
                  $"phase log เก็บเขียวเปล่าไว้ได้ ({dl.GetIdleLaneSeconds():F2}s)");
            Check(dl.GetPhaseCount() == 1, "นับจำนวนเฟสได้");
        }

        // ===============================================================================
        // เทส 6 — นาฬิกาเดียว: ทุกการตัดสินใจผูกกับ snapshot ไม่มีลูปตั้งนาฬิกาแข่ง
        // ===============================================================================
        Console.WriteLine("\n=== เทส 6: นาฬิกาเดียว ===");
        {
            var ac6 = MakeController(out rois);
            int i0 = ac6.SnapshotIndex;
            ac6.Snapshot(); ac6.Snapshot(); ac6.Snapshot();
            Check(ac6.SnapshotIndex == i0 + 3, $"snapshotIndex เดินขึ้นทุกครั้งที่ถ่าย ({i0} -> {ac6.SnapshotIndex})");

            string ctl6 = File.Exists("Scripts/AdaptiveController.cs")
                        ? File.ReadAllText("Scripts/AdaptiveController.cs") : "";
            string code6 = Regex.Replace(ctl6, @"//.*?$", "", RegexOptions.Multiline);
            code6 = Regex.Replace(code6, @"/\*.*?\*/", "", RegexOptions.Singleline);

            Check(!code6.Contains("snapshotInterval * 0.5f"),
                  "ไม่เหลือลูปที่เช็กทุก 1 วิ ทั้งที่ข้อมูลมาทุก 2 วิ");
            Check(code6.Contains("WaitForNextSnapshot"),
                  "ลูปรอใช้ WaitForNextSnapshot — ประเมินพอดีครั้งเดียวต่อ snapshot");

            // SnapshotLoop ต้องเป็นที่เดียวที่ตั้งจังหวะเวลาไว้
            int ticks = Regex.Matches(code6, @"WaitForSeconds\(snapshotInterval").Count;
            Console.WriteLine($"    จุดที่ตั้งจังหวะจาก snapshotInterval = {ticks} จุด");
            Check(ticks == 1, "มีนาฬิกาเดียวในระบบ (SnapshotLoop) ไม่มีใครตั้งแข่ง");
        }

        // ===============================================================================
        // เทส 7 — มีตัวสะสมดีมานด์ตัวเดียว ไม่รอซ้ำสองต่อ
        // ===============================================================================
        Console.WriteLine("\n=== เทส 7: ตัวสะสมดีมานด์ตัวเดียว ===");
        {
            string ctl7 = File.Exists("Scripts/AdaptiveController.cs")
                        ? File.ReadAllText("Scripts/AdaptiveController.cs") : "";
            string code7 = Regex.Replace(ctl7, @"//.*?$", "", RegexOptions.Multiline);
            code7 = Regex.Replace(code7, @"/\*.*?\*/", "", RegexOptions.Singleline);

            Check(!code7.Contains("countingWindow"),
                  "ไม่เหลือ counting window (ถามคำถามเดียวกับ COLLECT ด้วยเกณฑ์เดียวกัน)");
            Check(!code7.Contains("demandThresholdSkipWait"),
                  "ไม่เหลือเกณฑ์ที่ซ้ำกับ earlyExitDemand");

            // WaitForSeconds ต้องเหลือที่เดียว = SnapshotLoop
            int waits = Regex.Matches(code7, @"WaitForSeconds\(").Count;
            Console.WriteLine($"    WaitForSeconds ทั้งไฟล์ = {waits} จุด");
            Check(waits == 1, "เหลือ WaitForSeconds จุดเดียว (SnapshotLoop) — ทุกอย่างอยู่บนนาฬิกาเดียว");

            var ac7 = MakeController(out rois);
            Check(ac7.earlyExitDemand == 3 && Math.Abs(ac7.collectWindow - 7f) < 1e-6f,
                  "COLLECT: เพดาน 7 วิ · early exit ที่ 3 คัน");

            // S1 สั้นลง 4 วิหลังตัด: 7 (collect) + 8 (G floor) + 5 (ปิด) = 20 นับจากที่เจอรถ
            float s1 = ac7.collectWindow + ac7.gMin + ac7.yellowDuration + ac7.allRedDuration;
            Console.WriteLine($"    S1 (รถ 1 คัน) นับจากวินาทีที่เห็นรถ = {s1:F1}s (เดิม {s1 + 4:F1}s)");
            Check(Math.Abs(s1 - 20f) < 0.01f, "รอบรถคันเดียวสั้นลงจาก 24 เหลือ 20 วิ");
        }

        // ===============================================================================
        // เทส 8 — Option A: tie-break = ลำดับเฟสเดิม A -> B -> Bx -> CB (Itim อนุมัติ)
        // ===============================================================================
        Console.WriteLine("\n=== เทส 8: tie-break แบบประกาศไว้ชัด ===");
        {
            var ac8 = MakeController(out rois);
            var cfg8 = ac8.config;

            Console.WriteLine("    priority: " + string.Join("  ",
                ac8.Heads.ConvertAll(h => $"{h.name}={h.priority}").ToArray()));
            Check(HeadNamed(ac8, "H_A").priority == 0 && HeadNamed(ac8, "H_B").priority == 1
               && HeadNamed(ac8, "H_Bx").priority == 2 && HeadNamed(ac8, "H_CB").priority == 3,
                  "priority มาจาก ORIGINAL_SEQUENCE ไม่ใช่ลำดับที่บังเอิญประกาศตัวแปร");

            // เสมอกันหมด -> ต้องได้ H_A เสมอ และต้องเหมือนเดิมทุกครั้งที่เรียก
            foreach (var h in ac8.Heads) { h.lastCount = 5; h.queueStart = 100f; }
            var l1 = ac8.ChooseLeaderByQueueStart(null);
            var l2 = ac8.ChooseLeaderByQueueStart(null);
            Check(l1 == HeadNamed(ac8, "H_A") && l1 == l2,
                  "เสมอกันหมด -> leader = H_A เสมอ (พฤติกรรมเดิม ไม่เปลี่ยน)");

            // queueStart ชนะ priority เสมอ
            HeadNamed(ac8, "H_CB").queueStart = 50f;
            Check(ac8.ChooseLeaderByQueueStart(null) == HeadNamed(ac8, "H_CB"),
                  "queueStart เก่ากว่าชนะ แม้ priority จะแย่ที่สุด");

            // comparator เป็น total order — sort ให้ผลเดียวกันไม่ว่าจะเรียงเข้ามาแบบไหน
            var fwd = new List<AdaptiveController.SignalHead>(ac8.Heads);
            var rev = new List<AdaptiveController.SignalHead>(ac8.Heads);
            rev.Reverse();
            foreach (var h in ac8.Heads) h.queueStart = 100f;
            fwd.Sort(AdaptiveController.CompareHeads);
            rev.Sort(AdaptiveController.CompareHeads);
            string a = string.Join(",", fwd.ConvertAll(h => h.name).ToArray());
            string b = string.Join(",", rev.ConvertAll(h => h.name).ToArray());
            Console.WriteLine($"    sort จากลำดับปกติ  = {a}");
            Console.WriteLine($"    sort จากลำดับกลับ = {b}");
            Check(a == b, "เสมอกันหมดแล้ว sort ยังให้ผลเดียวกัน (ไม่พึ่ง List.Sort ว่า stable)");
            Check(a == "H_A,H_B,H_Bx,H_CB", "เรียงตามลำดับเฟสเดิมของแยก");

            // counter: passive ล้วน
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
            Console.WriteLine($"    leader=B, A/Bx เสมอกัน -> group = {{ {gn.Trim()} }}");
            Console.WriteLine($"    TieBreaksApplied={ac9.TieBreaksApplied}  TieBreaksExcludingBx={ac9.TieBreaksExcludingBx}");

            Check(ac9.TieBreaksApplied == 1, "นับได้ว่ามี tie เกิดขึ้น 1 ครั้ง");
            Check(ac9.TieBreaksExcludingBx == 1,
                  "และนับได้ว่าครั้งนั้น tie-break ตัด H_Bx ออกจากกลุ่ม");

            // ไม่มี tie -> ต้องไม่นับ
            var acA = MakeController(out rois);
            var hs = acA.Heads;
            for (int i = 0; i < hs.Count; i++) { hs[i].lastCount = 3; hs[i].queueStart = 100f + i; }
            var gA = (List<int>)m9.Invoke(acA, new object[] { acA.config.SignalIndexOf("B") });
            c9.Invoke(acA, new object[] { gA });
            Check(acA.TieBreaksApplied == 0, "ไม่มีใครเสมอกัน -> counter ต้องไม่ขยับ");
        }

        // ===============================================================================
        // เทส 9 — starvation tie ต้องไปทิศเดียวกับ Option A (SODA_BASELINE §8.3, D3)
        // ===============================================================================
        Console.WriteLine("\n=== เทส 9: starvation tie-break ===");
        {
            var ac9b = MakeController(out rois);
            float now = Time.time;
            foreach (var h in ac9b.Heads) { h.lastCount = 0; h.queueStart = Mathf.Infinity; }
            var hA9  = HeadNamed(ac9b, "H_A");
            var hCB9 = HeadNamed(ac9b, "H_CB");
            hA9.lastCount = 2;  hA9.queueStart  = now - 50f;     // both over 42.75, bit-identical
            hCB9.lastCount = 2; hCB9.queueStart = now - 50f;

            var pick = ac9b.FindStarvingHead(0.95f, null);
            Console.WriteLine($"    A และ CB รอ 50 วิเท่ากันเป๊ะ -> starving leader = {pick.name}");
            Check(pick == hA9, "เสมอกัน -> H_A ชนะ (ลำดับเฟสเดิม) ไม่ใช่หัวท้ายสุดของ list");

            // รอนานกว่าต้องชนะ priority เสมอ
            hCB9.queueStart = now - 60f;
            Check(ac9b.FindStarvingHead(0.95f, null) == hCB9, "CB รอนานกว่า -> CB ชนะแม้ priority ต่ำกว่า");

            // ต่ำกว่าเกณฑ์ต้องไม่ถูกเลือก
            hA9.queueStart = now - 10f; hCB9.queueStart = now - 10f;
            Check(ac9b.FindStarvingHead(0.95f, null) == null, "ไม่มีใครถึง 42.75 -> null");

            // ยังคง exclude กลุ่มที่เขียวอยู่
            hCB9.queueStart = now - 50f;
            Check(ac9b.FindStarvingHead(0.90f, hCB9.signals) == null,
                  "หัวที่อยู่ในกลุ่มเขียวถูก exclude (anti-starvation ไม่ตัดตัวเอง)");
        }

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
