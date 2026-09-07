using UnityEngine;
using System.Collections.Generic;

/// <summary>วิธีปล่อยรถเข้าระบบ</summary>
public enum ArrivalMode
{
    /// <summary>สุ่มแบบ Poisson — ใช้กับการทดลองเชิงสถิติ 135 scenarios</summary>
    Poisson,
    /// <summary>กำหนดเองว่าเลนไหนปล่อยกี่คันตอนไหน — ผลเหมือนกันเป๊ะทุกครั้ง เหมาะกับการเดโมและตรวจ logic</summary>
    Scripted
}

/// <summary>
/// "ปล่อยรถ N คัน ที่วินาทีที่ X ห่างกันคันละ Y วินาที"
/// หน่วยเวลานับจากตอนเริ่ม run
/// </summary>
[System.Serializable]
public class ScheduledBurst
{
    [Tooltip("เริ่มปล่อยที่วินาทีที่เท่าไหร่ (นับจากเริ่ม run)")]
    public float atTime = 0f;

    [Tooltip("ปล่อยกี่คัน")]
    public int carCount = 1;

    [Tooltip("รถแต่ละคันห่างกันกี่วินาที")]
    public float spacing = 2f;

    [Tooltip("true = สุ่ม driver profile ตามสัดส่วน / false = ใช้ profile ที่กำหนดด้านล่าง")]
    public bool randomDriver = true;

    [Tooltip("ใช้เมื่อ randomDriver = false")]
    public CarAgent.DriverType driverType = CarAgent.DriverType.Normal;

    public ScheduledBurst() { }
    public ScheduledBurst(float t, int n, float sp)
    {
        atTime = t; carCount = n; spacing = sp;
    }
    public ScheduledBurst(float t, int n, float sp, CarAgent.DriverType type)
    {
        atTime = t; carCount = n; spacing = sp;
        randomDriver = false; driverType = type;
    }

    /// <summary>วินาทีที่รถคันสุดท้ายของ burst นี้ออก</summary>
    public float EndTime => atTime + Mathf.Max(0, carCount - 1) * spacing;
}

/// <summary>ตารางปล่อยรถของเลนหนึ่ง</summary>
[System.Serializable]
public class LaneSchedule
{
    public string laneName = "A";
    public List<ScheduledBurst> bursts = new List<ScheduledBurst>();

    public LaneSchedule() { }
    public LaneSchedule(string lane, params ScheduledBurst[] items)
    {
        laneName = lane;
        bursts = new List<ScheduledBurst>(items);
    }

    public int TotalCars
    {
        get
        {
            int total = 0;
            foreach (var b in bursts) total += b.carCount;
            return total;
        }
    }
}

/// <summary>อัตราการมาถึงแบบ Poisson ของเลนหนึ่ง</summary>
[System.Serializable]
public class LaneArrivalRate
{
    public string laneName = "A";
    [Tooltip("วินาทีเฉลี่ยระหว่างรถแต่ละคัน (ยิ่งน้อย = รถยิ่งเยอะ)")]
    public float meanInterval = 7f;

    public LaneArrivalRate() { }
    public LaneArrivalRate(string lane, float interval)
    {
        laneName = lane; meanInterval = interval;
    }
}

/// <summary>
/// ชุดทดสอบหนึ่งชุด — เลือกได้จาก Control Panel ตอน runtime
/// </summary>
[System.Serializable]
public class TestCase
{
    public string name = "Test";

    [TextArea(2, 5)]
    [Tooltip("อธิบายว่าเทสนี้ตรวจอะไร — แสดงบน Control Panel")]
    public string description = "";

    public ArrivalMode mode = ArrivalMode.Scripted;

    [Tooltip("ความยาว run ที่แนะนำ (วินาที) — 0 = ไม่จำกัด รันไปเรื่อย ๆ")]
    public float suggestedDuration = 180f;

    [Tooltip("ใช้เมื่อ mode = Poisson")]
    public List<LaneArrivalRate> rates = new List<LaneArrivalRate>();

    [Tooltip("ใช้เมื่อ mode = Scripted")]
    public List<LaneSchedule> schedules = new List<LaneSchedule>();

    public float GetInterval(string lane, float fallback)
    {
        foreach (var r in rates)
            if (r != null && r.laneName == lane) return r.meanInterval;
        return fallback;
    }

    public List<ScheduledBurst> GetSchedule(string lane)
    {
        foreach (var s in schedules)
            if (s != null && s.laneName == lane) return s.bursts;
        return new List<ScheduledBurst>();
    }

    /// <summary>จำนวนรถทั้งหมดที่จะถูกปล่อย (เฉพาะ Scripted)</summary>
    public int TotalScriptedCars
    {
        get
        {
            int total = 0;
            foreach (var s in schedules) if (s != null) total += s.TotalCars;
            return total;
        }
    }

    /// <summary>สรุปสั้น ๆ ว่าเลนไหนปล่อยกี่คัน — โชว์บน panel</summary>
    public string ShortSummary()
    {
        if (mode == ArrivalMode.Poisson)
        {
            var parts = new List<string>();
            foreach (var r in rates) if (r != null) parts.Add($"{r.laneName}:{r.meanInterval:0.#}s");
            return "Poisson  " + string.Join("  ", parts);
        }
        else
        {
            var parts = new List<string>();
            foreach (var s in schedules) if (s != null && s.TotalCars > 0) parts.Add($"{s.laneName}:{s.TotalCars}");
            return $"Scripted {TotalScriptedCars} cars  ({string.Join("  ", parts)})";
        }
    }
}

/// <summary>
/// ชุดทดสอบสำเร็จรูป
///
/// T1-T7 = Scripted (deterministic) — ผลเหมือนเดิมเป๊ะทุกครั้งที่กด ใช้เดโมและตรวจ logic
/// T8-T12 = Poisson — ชุดเดียวกับที่ใช้รันสถิติ 135 scenarios
/// </summary>
public static class TestCasePresets
{
    // อัตรา Poisson มาตรฐาน
    const float VERY_HIGH = 1.5f, HIGH = 2.5f, MEDIUM = 4f, LOW = 7f, VERY_LOW = 15f;
    // เลน "ปิด" จริง (ยืนยันจาก รปภ.) — ไม่ใช่แค่รถน้อย ใช้ mean interval ใหญ่มาก ๆ แทนศูนย์
    // เพราะ Poisson ใน CarSpawner หาร rate จาก interval, ตั้ง 0 ตรง ๆ จะกลายเป็น rate ไม่จำกัด (พังคนละทาง)
    const float CLOSED = 999999f;

    // สัดส่วนรถตรงที่เข้าเลนนอก (A2 — เลนแชร์กับ Ax) เทียบกับรถตรงทั้งหมด
    //
    // ประวัติค่า: เดา 50/50 → back-calculate ได้ 18% → **ตอนนี้ 5.4% จากการนับตรง** (SP1_HANDOFF_v2 §5)
    // Itim นับจากคลิป A_morning: รถตรงทั้งหมด 56 คัน อยู่เลนใน 53 คัน เข้าเลนแชร์แค่ **3 คัน**
    // นี่คือค่าที่นับมาตรง ๆ ไม่ใช่ค่าที่อนุมานย้อนจากอัตราการบล็อกอีกต่อไป จึงเชื่อถือได้กว่าเดิมมาก
    //
    // ⚠️ ตัวเลขนี้ต้องอ่านคู่กับพฤติกรรม "merge ช้า" เสมอ — รถ 3 คันนั้นเปลี่ยนเลขเข้ามาตอนเหลือระยะ
    //    แค่ ~5 ช่วงรถก่อนถึงจุดเลี้ยว = แทรกเข้า "หัวคิว" พอดี คันเดียวหยุดรถเลี้ยวทั้งแถวที่ตามมาทั้งช่วงไฟแดง
    //    ผลคือ "เกิดไม่บ่อยแต่รุนแรงเกือบสูงสุดทุกครั้ง" ไม่ใช่ "บ่อย ๆ แต่หน่วงนิดเดียว"
    //    ทั้งสองแบบให้ค่าเฉลี่ยเท่ากันแต่ queue dynamics คนละเรื่อง — ดูหมายเหตุใน ABACSceneBuilder
    public const float A2_SHARE = 0.054f;   // ต้องเท่ากับ ScenarioManager.MEASURED_A2_SHARE เสมอ

    public static List<TestCase> BuildDefault()
    {
        var list = new List<TestCase>();

        // ---------- T1 ----------
        list.Add(new TestCase
        {
            name = "T1 \u00B7 Single lane, 10 cars",
            description = "Release 10 cars on lane A only, 1.5 s apart.\n" +
                          "Baseline check: cars queue in a neat line without overlapping, then discharge on green.",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 120f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A", new ScheduledBurst(0f, 10, 1.5f)),
            }
        });

        // ---------- T2 ----------
        list.Add(new TestCase
        {
            name = "T2 \u00B7 All lanes equal",
            description = "All 7 lanes release 8 cars at once, 2 s apart (including the A2 shared lane).\n" +
                          "Fairness check: every signalised lane must get green; none may be starved.\n" +
                          "Free lane CA should show a wait near zero. Ax will NOT be zero \u2014 by design \u2014 for two reasons:\n" +
                          "(1) A2 stops at the red light in the same lane ahead of it, (2) Ax itself must yield while Bx is green.",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 180f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A",  new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("A2", new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("Ax", new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("B",  new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("Bx", new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("CB", new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("CA", new ScheduledBurst(0f, 8, 2f)),
            }
        });

        // ---------- T3 ----------
        list.Add(new TestCase
        {
            name = "T3 \u00B7 Bx vs CB conflict",
            description = "Heavy load on Bx and CB at the same time, plus some traffic on A.\n" +
                          "Key check: Bx and CB must never be green together. Easy to see during a demo \u2014\n" +
                          "the controller alternates between them.",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 180f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("Bx", new ScheduledBurst(0f, 12, 1.5f)),
                new LaneSchedule("CB", new ScheduledBurst(0f, 12, 1.5f)),
                new LaneSchedule("A",  new ScheduledBurst(0f,  6, 3f)),
            }
        });

        // ---------- T4 ----------
        list.Add(new TestCase
        {
            name = "T4 \u00B7 Starvation test",
            description = "A and B run continuously with 40 cars each, while CB gets a single car at t = 5 s.\n" +
                          "Question: does that one CB car ever get green, and how long does it wait?\n" +
                          "This is the test that proves anti-starvation works \u2014 examiners like to ask about it.",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 180f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A",  new ScheduledBurst(0f, 40, 2f)),
                new LaneSchedule("B",  new ScheduledBurst(0f, 40, 2f)),
                new LaneSchedule("CB", new ScheduledBurst(5f,  1, 1f)),
            }
        });

        // ---------- T5 ----------
        list.Add(new TestCase
        {
            name = "T5 \u00B7 Campus event in/out",
            description = "Heavy inbound first (Ax, Bx), then at t = 90 s everyone leaves at once (CB, CA).\n" +
                          "Models a graduation-day surge \u2014 checks that the controller adapts to a reversal in direction.",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 300f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("Ax", new ScheduledBurst(0f, 20, 1.2f)),
                new LaneSchedule("Bx", new ScheduledBurst(0f, 20, 1.2f)),
                new LaneSchedule("A",  new ScheduledBurst(0f, 10, 4f)),
                new LaneSchedule("B",  new ScheduledBurst(0f, 10, 4f)),
                new LaneSchedule("CB", new ScheduledBurst(90f, 20, 1.2f)),
                new LaneSchedule("CA", new ScheduledBurst(90f, 20, 1.2f)),
            }
        });

        // ---------- T6 ----------
        list.Add(new TestCase
        {
            name = "T6 \u00B7 Unsignalised lanes (Ax, CA)",
            description = "Only Ax and CA release vehicles. No A2 present to block anything.\n" +
                          "CA is genuinely free \u2014 its wait must stay near zero, otherwise stopLineTarget is misconfigured.\n" +
                          "Ax is no longer truly free (it yields while Bx is green), which makes this the sharpest\n" +
                          "Fixed-vs-Adaptive comparison in the set:\n" +
                          "   \u2022 Fixed \u2192 Bx gets green every cycle even with zero Bx traffic, so Ax stops for nothing.\n" +
                          "   \u2022 Adaptive \u2192 no demand on Bx means no green, so Ax barely waits at all.",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 120f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("Ax", new ScheduledBurst(0f, 15, 2f)),
                new LaneSchedule("CA", new ScheduledBurst(0f, 15, 2f)),
            }
        });

        // ---------- T7 ----------
        list.Add(new TestCase
        {
            name = "T7 \u00B7 Driver profiles",
            description = "Lane A releases 6 aggressive drivers, then 6 cautious drivers at t = 40 s.\n" +
                          "Compares braking distance, reaction time and amber-running between profiles.",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 150f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A",
                    new ScheduledBurst(0f,  6, 2.5f, CarAgent.DriverType.Aggressive),
                    new ScheduledBurst(40f, 6, 2.5f, CarAgent.DriverType.Cautious)),
                new LaneSchedule("B", new ScheduledBurst(0f, 6, 5f)),
            }
        });

        // ---------- T13 ----------
        list.Add(new TestCase
        {
            name = "T13 · Shared Lane Blocking Demo",
            description = "Direct demonstration of the shared-lane mechanism: A2 (straight, outer lane) and\n" +
                          "Ax (turning into campus) occupy the same physical lane.\n" +
                          "A2 waits at the red light like any straight vehicle \u2014 and every Ax car behind it is trapped,\n" +
                          "even though no signal applies to Ax at all.\n" +
                          "When Bx turns green, Ax must stop at its own line too, so the Ax queue blocks A2 in return.\n" +
                          "Watch the Ax+A2 row in the HUD (one shared ROI = what a real detector sees).",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 300f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A2", new ScheduledBurst(0f, 30, 4f)),
                new LaneSchedule("Ax", new ScheduledBurst(2f, 40, 3f)),
                new LaneSchedule("B",  new ScheduledBurst(0f, 15, 5f)),
            }
        });

        // ---------- T8-T12 : Poisson (ชุดเดียวกับการทดลองจริง) ----------
        // T8/T10 ใช้ค่าที่ Itim วัดจริงจากคลิปโดรน (5 นาที, 13/22 ก.ค. 2569) แทนค่าเดา
        // ส่วน CB/CA ยังไม่มีข้อมูลวัด — ใช้ bucket เดิมไปก่อน (ดู Questions_for_Itim.md ข้อ 2)
        // T9/T11/T12 ยังไม่มีข้อมูลวัดเลย (ไม่ใช่ session ที่ Itim ถ่าย) — เป็นสมมติฐานล้วน ๆ
        //
        // พารามิเตอร์ A ที่ส่งเข้า Poisson() คือ "อัตรารถตรงรวม" ก่อนแบ่งเลน — ฟังก์ชันจะหาร 50/50
        // ให้ A กับ A2 เองข้างล่าง (มาตรฐานการ thin Poisson process: แบ่งครึ่งคัน = mean interval คูณ 2)
        list.Add(Poisson("T8 · MorningPeak",  "คนเข้ามหาลัยเยอะ — A/Ax/B/Bx เป็นค่าวัดจริงจาก Itim (13 ก.ค. 08:40-09:05, ปรับแก้ตัวเลขรอบ 24 ส.ค. หลังรัน detector ครบ 8 คลิป)\n" +
            "CB/CA = ปิด — แก้แล้ว: เลน C ขาออกปิดแค่ 08:30-09:00 (~15-30 นาที) ไม่ใช่ทั้งเช้า แต่พอดีตรงกับช่วง peak นี้เป๊ะ (08:15-09:00) จึงยังใช้ CB≈CA≈0 ได้",
            18.8f, 5.1f, 9.0f, 9.6f, CLOSED, CLOSED));
        list.Add(Poisson("T9 · EveningPeak",  "คนออกจากมหาลัยเยอะ — ⭐ CB/CA เป็นค่าวัดจริงแล้ว (SP1_HANDOFF_v2 §4 ช่วงเย็น)\n" +
            "CB 159 คัน = 8.4s/คัน, CA 100 คัน = 13.4s/คัน (สัดส่วน CB:CA = 61:39)\n" +
            "ส่วน A/Ax/B/Bx ตอนเย็นยังไม่มีข้อมูลวัด ใช้ bucket ประมาณไปก่อน",
            MEDIUM, LOW, HIGH, LOW, 8.4f, 13.4f));
        list.Add(Poisson("T10 · MiddayNormal", "จราจรปกติทุกทิศ — A/Ax/B/Bx เป็นค่าวัดจริงจาก Itim (22 ก.ค. 11:37-12:00, ปรับแก้ตัวเลขรอบ 24 ส.ค.), CB/CA ยังเป็นค่าเดา (รอฟุตเทจเลน C วันจันทร์)\n" +
            "⚠️ ยืนยันแล้วจากวิดีโอ: 09:00-15:00 ไฟกระพริบเหลืองจริง ไม่ใช่ fixed-time — ช่วงที่ถ่าย midday (11:37-12:00) อยู่ในช่วงกระพริบพอดี\n" +
            "แปลว่า T10 เทียบกับ 'Fixed' เป็นการเทียบเชิงสมมติ (ถ้าแยกนี้ใช้ fixed-time ตลอดวัน) ไม่ใช่สภาพจริงที่เกิดขึ้นตอนเที่ยง — ต้องระบุ caveat นี้ในรายงานให้ชัด",
            29.0f, 9.7f, 20.5f, 21.4f, LOW, LOW));
        list.Add(Poisson("T11 · ABACEvent",   "งานใหญ่ เข้าออกหนักพร้อมกัน — สมมติฐานทั้งหมด",
            MEDIUM, VERY_HIGH, MEDIUM, VERY_HIGH, HIGH, HIGH));
        list.Add(Poisson("T12 · Night",       "เบาบางทุกทิศ — สมมติฐานทั้งหมด",
            VERY_LOW, VERY_LOW, VERY_LOW, VERY_LOW, VERY_LOW, VERY_LOW));

        return list;
    }

    static TestCase Poisson(string name, string desc,
                            float A, float Ax, float B, float Bx, float CB, float CA)
    {
        // A ที่รับเข้ามาคือ "อัตรารถตรงรวม" (ก่อนมี shared lane) — แบ่งให้ A (เลนใน) กับ A2 (เลนนอก)
        // ตามสัดส่วน A2_SHARE (18% เลนนอก / 82% เลนใน — ไม่ใช่ 50/50 แล้ว ดูคอมเมนต์ที่ A2_SHARE ด้านบน)
        // thinning ของ Poisson process: rate ของแต่ละเลน = rate รวม x สัดส่วน -> mean interval = interval รวม / สัดส่วน
        float aInner = A / (1f - A2_SHARE);   // เลนใน (A) ได้รถเยอะกว่า -> interval สั้นกว่าเดิม
        float aOuter = A / A2_SHARE;          // เลนนอก/แชร์ (A2) ได้รถน้อยกว่ามาก -> interval ยาวกว่าเดิมเยอะ

        return new TestCase
        {
            name = name,
            description = desc + "\n(สุ่มแบบ Poisson — เปลี่ยน seed แล้วผลจะต่างกัน ใช้สำหรับสถิติ ไม่ใช่เดโม)",
            mode = ArrivalMode.Poisson,
            suggestedDuration = 300f,
            rates = new List<LaneArrivalRate> {
                new LaneArrivalRate("A",  aInner),
                new LaneArrivalRate("A2", aOuter),
                new LaneArrivalRate("Ax", Ax),
                new LaneArrivalRate("B",  B),
                new LaneArrivalRate("Bx", Bx),
                new LaneArrivalRate("CB", CB),
                new LaneArrivalRate("CA", CA),
            }
        };
    }
}
