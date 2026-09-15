using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// เทียบ "รถออกจากเลน A ในสิม" กับ "รถออกจากเลน A ในคลิปจริง" ตลอดไฟเขียวรอบเดียวกัน
///
/// ความจริงจากคลิป DJI_20260824154642_0021_D หน้าต่าง 1:02-2:49 (หัว A เขียวที่ 62.3 s)
/// วัดด้วย virtual detector ที่เส้นหยุด x=587 px นับเฉพาะรถยนต์ (คราบที่ทับเส้น >= 0.30 s)
/// ตัดมอเตอร์ไซค์ออกทั้งหมดตามที่ตกลงกัน 15 ก.ย.
///
/// สิมฝั่งนี้ใช้ CarAgent ตัวจริง เดินทีละ 0.02 s เหมือนใน Unity เป๊ะ ต่างกันแค่เป็นเลนเดียวตรง ๆ
public class Compare101Test
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo StepM  = typeof(CarAgent).GetMethod("Step", NP);
    static readonly MethodInfo EnM    = typeof(CarAgent).GetMethod("OnEnable", NP);
    static readonly MethodInfo DisM   = typeof(CarAgent).GetMethod("OnDisable", NP);
    static readonly MethodInfo ProfM  = typeof(CarAgent).GetMethod("SetProfileParameters", NP);
    static void Set(CarAgent c, string f, object v) => typeof(CarAgent).GetField(f, NP).SetValue(c, v);

    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    // เวลาที่หน้ารถข้ามเส้นหยุด นับจากหัว A เขียว (62.3 s ของคลิป)
    static readonly float[] VIDEO = { 4.90f, 8.90f, 11.13f, 14.77f, 17.40f,
                                      20.00f, 23.20f, 26.13f, 29.70f, 39.10f };

    // สัดส่วนคนขับ 25/60/15 เรียงแบบกระจายให้เหมือนที่ CarSpawner สุ่มได้จริง
    static readonly CarAgent.DriverType[] MIX = {
        CarAgent.DriverType.Normal, CarAgent.DriverType.Aggressive, CarAgent.DriverType.Normal,
        CarAgent.DriverType.Normal, CarAgent.DriverType.Cautious,   CarAgent.DriverType.Normal,
        CarAgent.DriverType.Aggressive, CarAgent.DriverType.Normal, CarAgent.DriverType.Normal,
        CarAgent.DriverType.Normal };

    const float CAR_LEN = 4f, MIN_GAP = 2f, SPAWN_X = -189.5f;
    const float GREEN_END = 40f;          // A + A2 + B เขียว 0-40 ตามตารางสเตจจริง
    const float YELLOW_END = 43f;

    /// คืนเวลาที่หน้ารถแต่ละคันข้ามเส้นหยุด (เส้นหยุดอยู่ที่ z = 0)
    static List<float> RunLaneA(int queued, float[] spawnTimes, float timeHeadway)
    {
        var sl = new Transform { position = Vector3.zero, forward = new Vector3(0, 0, 1) };
        var cars = new List<CarAgent>();
        var pending = new List<KeyValuePair<float, int>>();
        int idx = 0;

        for (int i = 0; i < queued; i++)
        {
            var c = new CarAgent();
            c.transform = new Transform {
                position = new Vector3(0, 0, -(CarAgent.REST_FRONT_MARGIN + CAR_LEN * 0.5f) - i * (CAR_LEN + MIN_GAP)),
                forward  = new Vector3(0, 0, 1) };
            c.gameObject = new GameObject("q" + i);
            c.driverType = MIX[idx % MIX.Length]; idx++;
            c.desiredTimeHeadway = timeHeadway;
            c.startAtRest = true; c.stopLineTarget = sl;
            ProfM.Invoke(c, null);
            Set(c, "lightInitialized", true); Set(c, "lightIsGreen", false);
            EnM.Invoke(c, null); cars.Add(c);
        }
        foreach (float st in spawnTimes) pending.Add(new KeyValuePair<float, int>(st, idx++));

        var crossed = new List<float>();
        var seen = new HashSet<CarAgent>();
        float dt = CarAgent.MAX_STEP, t = 0f;

        while (t < 60f)
        {
            // ปล่อยรถที่ถึงคิวแล้ว
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Key > t) continue;
                var c = new CarAgent();
                c.transform = new Transform { position = new Vector3(0, 0, SPAWN_X), forward = new Vector3(0, 0, 1) };
                c.gameObject = new GameObject("s");
                c.driverType = MIX[pending[i].Value % MIX.Length];
                c.desiredTimeHeadway = timeHeadway;
                c.stopLineTarget = sl;
                ProfM.Invoke(c, null);
                Set(c, "lightInitialized", true);
                EnM.Invoke(c, null); cars.Add(c);
                pending.RemoveAt(i);
            }

            bool green = t < GREEN_END;
            foreach (var c in cars) Set(c, "lightIsGreen", green);

            foreach (var c in cars) { Set(c, "stepDt", dt); StepM.Invoke(c, null); }
            t += dt;

            foreach (var c in cars)
                if (!seen.Contains(c) && c.transform.position.z + CAR_LEN * 0.5f >= 0f)
                { seen.Add(c); crossed.Add(t); }
        }
        foreach (var c in cars) DisM.Invoke(c, null);
        crossed.Sort();
        return crossed;
    }

    public static int Main()
    {
        // ต้องตรงกับ TestCasePresets.Compare101() เลน A เป๊ะ: จอดรอ 4 + ทยอยมา 6
        float tau = TestCasePresets.SPAWN_TO_STOPLINE_SEC;
        float[] arrive = { 17.40f, 20.00f, 23.20f, 26.13f, 29.70f, 39.10f };
        for (int i = 0; i < arrive.Length; i++) arrive[i] = Math.Max(0f, arrive[i] - tau);
        var sim = RunLaneA(4, arrive, CarAgent.MEASURED_TIME_HEADWAY);

        Console.WriteLine("--- เลน A: สิม vs คลิป (วินาทีนับจากไฟเขียว) ---");
        Console.WriteLine("  คัน     คลิป      สิม     ต่าง");
        int n = Math.Min(sim.Count, VIDEO.Length);
        float sumAbs = 0f, worst = 0f;
        for (int i = 0; i < n; i++)
        {
            float d = sim[i] - VIDEO[i];
            sumAbs += Math.Abs(d); worst = Math.Max(worst, Math.Abs(d));
            Console.WriteLine($"  {i + 1,3}   {VIDEO[i],7:F2}  {sim[i],7:F2}  {d,7:F2}");
        }
        Console.WriteLine($"\n  ปล่อยได้ในไฟเขียว: คลิป {VIDEO.Length} คัน · สิม {sim.Count} คัน");
        Console.WriteLine($"  ค่าคลาดเฉลี่ย {sumAbs / n:F2} s · มากสุด {worst:F2} s");

        // คันที่ 5 เป็นต้นไปคือรถที่ 'ขับเข้ามา' ทั้งหมด จึงเป็นตัววัดว่าการมาของรถตรงคลิปแค่ไหน
        float arrErr = 0f, arrWorst = 0f;
        for (int i = 4; i < n; i++)
        {
            float d = Math.Abs(sim[i] - VIDEO[i]);
            arrErr += d; arrWorst = Math.Max(arrWorst, d);
        }
        arrErr /= (n - 4);
        Console.WriteLine($"  เฉพาะรถที่ขับเข้ามา (คันที่ 5-10): คลาดเฉลี่ย {arrErr:F2} s · มากสุด {arrWorst:F2} s");

        Check(sim.Count == VIDEO.Length, $"ปล่อยรถได้ครบ {VIDEO.Length} คันในไฟเขียว 40 s เท่าคลิป");
        Check(sim[sim.Count - 1] < GREEN_END, "คันสุดท้ายข้ามเส้นก่อนไฟเขียวหมด เหมือนในคลิป");
        Check(Math.Abs(sim[sim.Count - 1] - VIDEO[VIDEO.Length - 1]) < 0.3f,
              "คันสุดท้าย (คันเดียวที่ขับมาตอนเลนโล่ง) ตรงกับคลิปในระยะ 0.3 s");
        Check(arrWorst < 2.0f, $"รถที่ขับเข้ามาทุกคันคลาดไม่เกิน 2 s (มากสุด {arrWorst:F2})");
        Check(sumAbs / n < 3.0f, $"คลาดเฉลี่ยทั้งเลนไม่เกิน 3 s (ตอนนี้ {sumAbs / n:F2})");

        // ===== ส่วนต่างที่เหลืออยู่ที่ 4 คันแรกเท่านั้น และอธิบายได้ =====
        // ในคลิปมีมอเตอร์ไซค์กองอยู่บนทางม้าลาย *หน้ารถยนต์ทุกคัน* ออกตัวที่ +1.80 s
        // รถคันแรกถึงขยับได้ที่ +5.10 s คือรอมอไซค์พ้นไป 3.30 s
        // ตกลงกันวันที่ 15 ก.ย. ว่าไม่จำลองมอเตอร์ไซค์ คิวตั้งต้นจึงออกเร็วกว่าคลิปเสมอ
        //
        // Compare101Test เคยวัดทางเลือกอื่นไว้ (T = 1.9 ไม่แตะค่าที่ calibrate จาก 39 ตัวอย่าง):
        //     จอด 9 (ยอดที่นับมือ) -> คลาดเฉลี่ย 5.75 s
        //     จอด 4 (เท่าที่กล้องเห็น) -> 2.10 s   <-- ที่ใช้อยู่
        //     จอด 2 (ฟิตดีที่สุด)      -> 1.14 s   ขัดกับภาพ จึงไม่เอา
        for (int i = 0; i < 4 && i < n; i++)
            Check(sim[i] <= VIDEO[i] + 0.01f,
                  $"คันที่ {i + 1} ในคิวตั้งต้นออกไม่ช้ากว่าคลิป (ต่าง {sim[i] - VIDEO[i]:F2} s จากมอไซค์ที่ตัดออก)");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
