using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// รันคิวรถ "ตัวจริง" (CarAgent) นอก Unity แล้ววัดว่าคิวระบายออกเร็วแค่ไหน
/// เทียบกับค่าที่วัดจากคลิป: saturation headway 2.31 s/คัน (§2.4 ค่า [measured])
///
/// เทสนี้เกิดจากที่ Badin สังเกตเห็นในซีนวันที่ 15 ก.ย. ว่า "รถออกพร้อมกันทีเดียวหลายคัน"
/// วัดแล้วพบว่าโมเดลระบายคิวที่ 0.72 s/คัน = เร็วกว่าของจริง 3.2 เท่า จากสองสาเหตุ
///   1) ไม่มีคลื่นออกตัว — คันหลังจับเวลาตอบสนองจาก "ไฟเขียว" ซึ่งทุกคันเห็นพร้อมกัน
///   2) ระยะห่างตามคันหน้าเป็น 2 ม. คงที่ ไม่โตตามความเร็ว — วิ่งชิดกันที่ 15 m/s ได้
public class HeadwayTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo StepM   = typeof(CarAgent).GetMethod("Step", NP);
    static readonly MethodInfo EnableM = typeof(CarAgent).GetMethod("OnEnable", NP);
    static readonly MethodInfo DisableM= typeof(CarAgent).GetMethod("OnDisable", NP);
    static readonly MethodInfo ProfM   = typeof(CarAgent).GetMethod("SetProfileParameters", NP);

    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }
    static void Set(CarAgent c, string f, object v) => typeof(CarAgent).GetField(f, NP).SetValue(c, v);

    /// คิวยาว n คัน จอดติดไฟแดงอยู่แล้ว แล้วไฟเขียวที่ t = 0
    /// คืนเวลาที่หน้ารถแต่ละคันข้ามเส้นหยุด
    static float[] DischargeQueue(CarAgent.DriverType[] mix, float timeHeadway, float horizon = 90f)
    {
        int n = mix.Length;
        var stopLine = new Transform { position = Vector3.zero, forward = new Vector3(0, 0, 1) };
        var cars = new List<CarAgent>();
        float carLen = 4f, minGap = 2f, pitch = carLen + minGap;

        for (int i = 0; i < n; i++)
        {
            var c = new CarAgent();
            c.transform = new Transform {
                position = new Vector3(0, 0, -(CarAgent.REST_FRONT_MARGIN + carLen * 0.5f) - i * pitch),
                forward  = new Vector3(0, 0, 1) };
            c.gameObject = new GameObject("car" + i);
            c.driverType = mix[i];
            c.desiredTimeHeadway = timeHeadway;
            c.startAtRest = true;
            c.stopLineTarget = stopLine;
            ProfM.Invoke(c, null);
            Set(c, "lightInitialized", true);
            Set(c, "lightIsGreen", true);       // ไฟเขียวตั้งแต่ t = 0
            EnableM.Invoke(c, null);
            cars.Add(c);
        }

        var crossed = new float[n];
        for (int i = 0; i < n; i++) crossed[i] = -1f;

        float dt = CarAgent.MAX_STEP, t = 0f;
        while (t < horizon)
        {
            foreach (var c in cars) { Set(c, "stepDt", dt); StepM.Invoke(c, null); }
            t += dt;
            for (int i = 0; i < n; i++)
                if (crossed[i] < 0f && cars[i].transform.position.z + carLen * 0.5f >= 0f)
                    crossed[i] = t;
        }
        foreach (var c in cars) DisableM.Invoke(c, null);   // ถอนออกจาก allCars ไม่ให้รั่วไปเคสถัดไป
        return crossed;
    }

    static float MeanHeadway(float[] crossed, int skipFirst = 1)
    {
        float sum = 0f; int n = 0;
        for (int i = skipFirst + 1; i < crossed.Length; i++)
        {
            if (crossed[i] < 0f || crossed[i - 1] < 0f) continue;
            sum += crossed[i] - crossed[i - 1]; n++;
        }
        return n == 0 ? float.NaN : sum / n;
    }

    public static int Main()
    {
        // สัดส่วนคนขับตามที่ CarSpawner ใช้จริง (25 aggressive / 60 normal / 15 cautious)
        var mix = new CarAgent.DriverType[] {
            CarAgent.DriverType.Normal,  CarAgent.DriverType.Aggressive, CarAgent.DriverType.Normal,
            CarAgent.DriverType.Cautious,CarAgent.DriverType.Normal,     CarAgent.DriverType.Normal,
            CarAgent.DriverType.Aggressive, CarAgent.DriverType.Normal,  CarAgent.DriverType.Cautious,
            CarAgent.DriverType.Normal };

        Console.WriteLine("--- คิว 10 คันระบายออกหลังไฟเขียว ---");
        var crossed = DischargeQueue(mix, CarAgent.MEASURED_TIME_HEADWAY);
        for (int i = 0; i < crossed.Length; i++)
        {
            string h = i == 0 ? "    -" : (crossed[i] - crossed[i - 1]).ToString("F2");
            Console.WriteLine($"    คันที่ {i + 1,2}  {mix[i],-10}  ข้ามเส้นที่ {crossed[i],6:F2} s   headway {h}");
        }

        float mean = MeanHeadway(crossed);
        Console.WriteLine($"\n    headway เฉลี่ย (ตัดคันที่ 1-2) = {mean:F2} s/คัน | วัดจากคลิป 2.31");

        // ===== 1) ต้องระบายออกที่อัตราที่วัดจริง =====
        Check(Math.Abs(mean - 2.31f) <= 0.15f,
              $"saturation headway {mean:F2} s/คัน อยู่ในช่วง 2.31 +/- 0.15 ที่วัดจากคลิป");

        // ===== 2) ห้ามมีคันไหนออกพร้อมกัน — นี่คืออาการที่เห็นในซีน =====
        float tightest = float.MaxValue;
        for (int i = 1; i < crossed.Length; i++)
            tightest = Math.Min(tightest, crossed[i] - crossed[i - 1]);
        Console.WriteLine($"    headway แคบที่สุด = {tightest:F2} s");
        Check(tightest >= 1.0f, $"ไม่มีคู่ไหนออกห่างกันต่ำกว่า 1 วิ (แคบสุด {tightest:F2} s)");

        // ===== 3) คลื่นออกตัวต้องไหลถอยหลัง ไม่ใช่ทั้งแถวพร้อมกัน =====
        bool monotone = true;
        for (int i = 1; i < crossed.Length; i++)
            if (crossed[i] <= crossed[i - 1]) monotone = false;
        Check(monotone, "รถข้ามเส้นเรียงตามลำดับคิว ไม่มีแซงกัน");

        // ===== 4) ค่า T ต้องเป็นตัวที่คุมอัตราระบายจริง (กันใครไปแก้แล้วไม่รู้ตัว) =====
        Console.WriteLine("\n--- ความไวต่อ desiredTimeHeadway ---");
        float hLow  = MeanHeadway(DischargeQueue(mix, 1.0f));
        float hHigh = MeanHeadway(DischargeQueue(mix, 2.8f));
        Console.WriteLine($"    T=1.0 -> {hLow:F2} s/คัน | T=1.9 -> {mean:F2} | T=2.8 -> {hHigh:F2}");
        Check(hLow < mean && mean < hHigh, "headway โตตาม T อย่างเป็นเอกภาพ");
        Check(Math.Abs(CarAgent.MEASURED_TIME_HEADWAY - 1.9f) < 1e-6f,
              "ค่าคงที่ T = 1.9 s ตัวที่ปรับให้ตรงกับ 2.31");

        // ===== 5) ถ้าไม่มีคลื่นออกตัว คิวจะพุ่งออกเป็นก้อน — ยืนยันว่ากลไกยังอยู่ =====
        var f = typeof(CarAgent).GetField("startupTimer", NP);
        Check(f != null, "CarAgent ยังมีตัวจับเวลาคลื่นออกตัว (startupTimer)");
        Check(CarAgent.LEADER_MOVING_SPEED > CarAgent.QUEUE_SPEED_EPS,
              "เกณฑ์ 'คันหน้าเริ่มเคลื่อน' ต้องสูงกว่าเกณฑ์ 'ถือว่าจอด' ไม่งั้นปล่อยตัวเองทันที");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
