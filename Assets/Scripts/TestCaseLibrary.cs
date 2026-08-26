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
            return $"Scripted {TotalScriptedCars} คัน  ({string.Join("  ", parts)})";
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
    // เดิมเดา 50/50 — Itim back-calculate จากอัตราการบล็อกที่สังเกตจริง (Ax โดนบล็อก 40-60% ของเวลา) ได้ตัวเลขจริง
    // ประมาณ 13-23% กลาง ๆ ที่ 18% (ดู reply_to_badin_3.md ข้อ 1) — ใช้ 18% เป็นค่ากลาง, sensitivity ที่ 13%/23%
    const float A2_SHARE = 0.18f;

    public static List<TestCase> BuildDefault()
    {
        var list = new List<TestCase>();

        // ---------- T1 ----------
        list.Add(new TestCase
        {
            name = "T1 · เลนเดียว 10 คัน",
            description = "ปล่อยรถเฉพาะเลน A 10 คัน ห่างกัน 1.5 วิ\n" +
                          "ตรวจพื้นฐาน: รถจอดรอเป็นแถวสวย ๆ ไม่ซ้อนกัน แล้วระบายออกตอนไฟเขียว",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 120f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A", new ScheduledBurst(0f, 10, 1.5f)),
            }
        });

        // ---------- T2 ----------
        list.Add(new TestCase
        {
            name = "T2 · ทุกเลนเท่ากัน",
            description = "ทั้ง 7 เลนปล่อย 8 คันพร้อมกัน ห่างกัน 2 วิ (รวม A2 เลนแชร์ด้วย)\n" +
                          "ตรวจความเป็นธรรม: ทุกเลนต้องได้ไฟเขียว ไม่มีเลนไหนถูกทิ้ง\n" +
                          "และเลนฟรี CA ต้องมีเวลารอใกล้ 0 — ส่วน Ax ตอนนี้ไม่ใช่ 0 แล้ว (โดนตั้งใจ) ด้วย 2 เหตุ:\n" +
                          "(1) A2 จอดรอไฟแดงบล็อกอยู่ข้างหน้าในเลนเดียวกัน (2) Ax เองก็ต้องหยุดตอน Bx เขียว\n" +
                          "ดู T13 สำหรับกรณีที่ชัดกว่านี้",
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
            name = "T3 · Bx ปะทะ CB (conflict)",
            description = "อัด Bx และ CB พร้อมกันหนัก ๆ + A อีกนิดหน่อย\n" +
                          "⭐ ตรวจ conflict rule: Bx กับ CB ต้องไม่เขียวพร้อมกันเด็ดขาด\n" +
                          "เปิดดูตอนเดโมได้เลย เห็นชัดว่าระบบสลับให้ทีละฝั่ง",
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
            name = "T4 · ทดสอบกันอด (starvation)",
            description = "A และ B อัดต่อเนื่องยาว ๆ 40 คัน แต่ CB มีรถแค่ 1 คันที่วินาทีที่ 5\n" +
                          "⭐ คำถาม: รถคันเดียวของ CB จะได้ไฟเขียวไหม รอนานแค่ไหน\n" +
                          "นี่คือเทสที่พิสูจน์ว่า anti-starvation ทำงานจริง — กรรมการชอบถามข้อนี้",
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
            name = "T5 · งาน ABAC เข้า-ออก",
            description = "ช่วงแรกคนเข้ามหาลัยหนัก (Ax, Bx) แล้ววินาทีที่ 90 คนออกพร้อมกัน (CB, CA)\n" +
                          "จำลองงานรับปริญญา — ตรวจว่าระบบปรับตัวตามทิศทางที่เปลี่ยนได้ไหม",
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
            name = "T6 · เลนไม่มีเฟสของตัวเอง (Ax, CA)",
            description = "ปล่อยเฉพาะ Ax และ CA ไม่มีรถเลนอื่นเลย — ไม่มี A2 มาบล็อกด้วย\n" +
                          "⭐ CA เป็นเลนฟรีแท้ ๆ เวลารอต้องใกล้ 0 เสมอ ถ้าไม่ใกล้ 0 = ตั้ง stopLineTarget ผิด\n" +
                          "⭐ Ax ไม่ใช่เลนฟรีแท้แล้ว (ต้องหยุดตอน Bx เขียว) เทสนี้เลยกลายเป็นตัวเทียบ Fixed vs Adaptive ที่ตรงประเด็นมาก:\n" +
                          "   • Fixed → Bx ได้ไฟเขียวทุกรอบถึงไม่มีรถ Bx สักคัน = Ax โดนหยุดฟรี ๆ ทุกรอบ เวลารอ > 0 ชัดเจน\n" +
                          "   • Adaptive → ไม่มี demand ที่ Bx ก็ไม่ควรให้เขียว = Ax แทบไม่ต้องรอเลย\n" +
                          "   ถ้า Adaptive ทำข้อนี้ไม่ได้ แปลว่า logic gap-out/anti-starvation มีปัญหา",
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
            name = "T7 · เทียบนิสัยคนขับ",
            description = "เลน A ปล่อยคนขับดุ 6 คันก่อน แล้ววินาทีที่ 40 ปล่อยคนขับระวัง 6 คัน\n" +
                          "เทียบระยะเบรก เวลาตอบสนอง และการฝ่าไฟเหลืองของแต่ละ profile",
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
            description = "สาธิตกลไก shared lane โดยตรง: A2 (รถตรงเลนนอก) กับ Ax (เลี้ยวเข้ามอเลนนอก) ใช้เลนเดียวกัน\n" +
                          "⭐ A2 ต้องรอไฟแดงเหมือน A ปกติ — ตอน A2 จอดรอ รถ Ax ที่ตามมาข้างหลังจะติดขังไปด้วย\n" +
                          "⭐ และเมื่อ Bx เขียว Ax เองก็ต้องหยุดที่เส้นหยุดของมัน — คิว Ax จะไปขวาง A2 กลับด้วย (บล็อกสองทาง)\n" +
                          "ดูแถว Ax+A2 ใน UI (ROI เดียวกันแล้ว = จำนวนรถในเลนนอกทั้งหมดที่ detector จริงเห็น)\n" +
                          "เวลารอของ Ax ต้อง > 0 ชัดเจน — ปล่อยยาวพอให้ผ่านครบทั้ง 3 เฟสอย่างน้อย 1 รอบ\n" +
                          "และควรเห็นรถ Ax บางคัน (65%) แซงคิวไปทางไหล่ทางฝั่งเหนือด้วย",
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
            19.7f, 5.6f, 8.7f, 9.6f, CLOSED, CLOSED));
        list.Add(Poisson("T9 · EveningPeak",  "คนออกจากมหาลัยเยอะ — ยังไม่มีข้อมูลวัดจริง (สมมติฐานทั้งหมด)",
            MEDIUM, LOW, HIGH, LOW, HIGH, HIGH));
        list.Add(Poisson("T10 · MiddayNormal", "จราจรปกติทุกทิศ — A/Ax/B/Bx เป็นค่าวัดจริงจาก Itim (22 ก.ค. 11:37-12:00, ปรับแก้ตัวเลขรอบ 24 ส.ค.), CB/CA ยังเป็นค่าเดา (รอฟุตเทจเลน C วันจันทร์)\n" +
            "⚠️ ยืนยันแล้วจากวิดีโอ: 09:00-15:00 ไฟกระพริบเหลืองจริง ไม่ใช่ fixed-time — ช่วงที่ถ่าย midday (11:37-12:00) อยู่ในช่วงกระพริบพอดี\n" +
            "แปลว่า T10 เทียบกับ 'Fixed' เป็นการเทียบเชิงสมมติ (ถ้าแยกนี้ใช้ fixed-time ตลอดวัน) ไม่ใช่สภาพจริงที่เกิดขึ้นตอนเที่ยง — ต้องระบุ caveat นี้ในรายงานให้ชัด",
            30.3f, 8.5f, 15.9f, 17.6f, LOW, LOW));
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
