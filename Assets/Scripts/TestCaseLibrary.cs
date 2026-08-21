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
            description = "ทั้ง 6 เลนปล่อย 8 คันพร้อมกัน ห่างกัน 2 วิ\n" +
                          "ตรวจความเป็นธรรม: ทุกเลนต้องได้ไฟเขียว ไม่มีเลนไหนถูกทิ้ง\n" +
                          "และเลนฟรี Ax/CA ต้องมีเวลารอใกล้ 0",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 180f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A",  new ScheduledBurst(0f, 8, 2f)),
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
            name = "T6 · เลนฟรีอย่างเดียว",
            description = "ปล่อยเฉพาะ Ax และ CA (สองเลนที่ไม่มีไฟ) ไม่มีรถเลนอื่นเลย\n" +
                          "⭐ Sanity check: เวลารอต้องใกล้ 0 และรถต้องเลี้ยวได้ไม่ติดอะไร\n" +
                          "ถ้าเลนฟรีมีเวลารอเยอะ = ตั้ง stopLineTarget ผิด",
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

        // ---------- T8-T12 : Poisson (ชุดเดียวกับการทดลองจริง) ----------
        list.Add(Poisson("T8 · MorningPeak",  "คนเข้ามหาลัยเยอะ — ชุดเดียวกับที่ใช้รันสถิติ",
            HIGH, HIGH, MEDIUM, HIGH, VERY_LOW, VERY_LOW));
        list.Add(Poisson("T9 · EveningPeak",  "คนออกจากมหาลัยเยอะ",
            MEDIUM, LOW, HIGH, LOW, HIGH, HIGH));
        list.Add(Poisson("T10 · MiddayNormal", "จราจรปกติทุกทิศ",
            LOW, LOW, LOW, LOW, LOW, LOW));
        list.Add(Poisson("T11 · ABACEvent",   "งานใหญ่ เข้าออกหนักพร้อมกัน",
            MEDIUM, VERY_HIGH, MEDIUM, VERY_HIGH, HIGH, HIGH));
        list.Add(Poisson("T12 · Night",       "เบาบางทุกทิศ",
            VERY_LOW, VERY_LOW, VERY_LOW, VERY_LOW, VERY_LOW, VERY_LOW));

        return list;
    }

    static TestCase Poisson(string name, string desc,
                            float A, float Ax, float B, float Bx, float CB, float CA)
    {
        return new TestCase
        {
            name = name,
            description = desc + "\n(สุ่มแบบ Poisson — เปลี่ยน seed แล้วผลจะต่างกัน ใช้สำหรับสถิติ ไม่ใช่เดโม)",
            mode = ArrivalMode.Poisson,
            suggestedDuration = 300f,
            rates = new List<LaneArrivalRate> {
                new LaneArrivalRate("A",  A),
                new LaneArrivalRate("Ax", Ax),
                new LaneArrivalRate("B",  B),
                new LaneArrivalRate("Bx", Bx),
                new LaneArrivalRate("CB", CB),
                new LaneArrivalRate("CA", CA),
            }
        };
    }
}
