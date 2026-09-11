using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// ตรวจ shared-lane (A2) โดยเฉพาะ — ใช้การ wiring แบบเดียวกับที่ ABACSceneBuilder ทำจริง
/// (A2.trafficLight == A.trafficLight คือ instance เดียวกันเป๊ะ ไม่ใช่แค่ค่าเท่ากัน)
/// แล้วเรียก AdaptiveController.BuildCompatibleGroup() ตัวจริงผ่าน reflection
/// เพื่อพิสูจน์ว่า closure "physically-linked signal" ทำงานถูกต้อง
/// </summary>
public class SharedLaneTest
{
    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    static FieldInfo PrivateField(Type t, string name) =>
        t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

    static MethodInfo PrivateMethod(Type t, string name) =>
        t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>ยัดรถปลอมเข้า ROI ตรง ๆ ผ่าน reflection (หนี OnTriggerEnter ที่ใช้ physics จริงไม่ได้ใน stub)</summary>
    static void SeedDemand(ROIZone roi, int count, float arrivalTime)
    {
        var dict = (Dictionary<CarAgent, float>)PrivateField(typeof(ROIZone), "carsInZone").GetValue(roi);
        dict.Clear();
        for (int i = 0; i < count; i++)
        {
            var car = new CarAgent();
            dict[car] = arrivalTime;
        }
    }

    public static int Main()
    {
        Time.time = 100f; // "ตอนนี้" ของการจำลอง — ใช้คำนวณ wait time

        var cfg = new IntersectionConfig();
        cfg.LoadABACPreset();

        // ===== wiring แบบเดียวกับ ABACSceneBuilder จริง: A กับ A2 ใช้ TrafficLight instance เดียวกัน =====
        var lightA  = new TrafficLight { laneName = "A" };
        var lightB  = new TrafficLight { laneName = "B" };
        var lightBx = new TrafficLight { laneName = "Bx" };
        var lightCB = new TrafficLight { laneName = "CB" };

        var roiA  = new ROIZone { laneName = "A" };
        var roiA2 = new ROIZone { laneName = "A2" };
        var roiB  = new ROIZone { laneName = "B" };
        var roiBx = new ROIZone { laneName = "Bx" };
        var roiCB = new ROIZone { laneName = "CB" };

        cfg.FindLane("A").trafficLight  = lightA;   cfg.FindLane("A").roi  = roiA;
        cfg.FindLane("A2").trafficLight = lightA;   cfg.FindLane("A2").roi = roiA2;  // ← instance เดียวกับ A โดยตั้งใจ
        cfg.FindLane("B").trafficLight  = lightB;   cfg.FindLane("B").roi  = roiB;
        cfg.FindLane("Bx").trafficLight = lightBx;  cfg.FindLane("Bx").roi = roiBx;
        cfg.FindLane("CB").trafficLight = lightCB;  cfg.FindLane("CB").roi = roiCB;
        cfg.Build();

        Check(cfg.SignalLight(cfg.SignalIndexOf("A")) == cfg.SignalLight(cfg.SignalIndexOf("A2")),
              "A และ A2 อ้างถึง TrafficLight component เดียวกันจริง ๆ (reference เท่ากัน ไม่ใช่แค่ค่า)");

        var adaptive = new AdaptiveController { config = cfg };
        // เรียก Awake() ตรง ๆ ผ่าน reflection (ปกติ Unity engine เป็นคนเรียกให้)
        PrivateMethod(typeof(AdaptiveController), "Awake").Invoke(adaptive, null);

        var buildGroup = PrivateMethod(typeof(AdaptiveController), "BuildCompatibleGroup");
        // SODA อ่านจำนวนรถจาก snapshot ไม่ใช่จาก ROI สด ๆ จึงต้องถ่าย snapshot ก่อนทุกครั้ง
        // เหมือนที่ ServeOnePhase ทำจริงตอนตัดสินใจ
        Func<int, List<int>> Group = leader =>
        {
            adaptive.Snapshot();
            return (List<int>)buildGroup.Invoke(adaptive, new object[] { leader });
        };

        int iA  = cfg.SignalIndexOf("A");
        int iA2 = cfg.SignalIndexOf("A2");
        int iB  = cfg.SignalIndexOf("B");
        int iBx = cfg.SignalIndexOf("Bx");
        int iCB = cfg.SignalIndexOf("CB");

        Console.WriteLine("\n--- กรณี A2 demand = 0 (ไม่มีรถ A2 เลยตอนนี้) แต่ leader = A ---");
        SeedDemand(roiA2, 0, 0f);
        SeedDemand(roiB, 0, 0f);
        var g1 = Group(iA);
        Check(g1.Contains(iA) && g1.Contains(iA2) && g1.Count == 2,
              $"leader=A, A2 demand=0 -> group ต้องมี A2 ด้วยอยู่ดี (ไฟดวงเดียวกัน) = {{{string.Join(",", g1)}}}");

        Console.WriteLine("\n--- กรณีเดียวกัน แต่สลับ leader = A2 ---");
        var g2 = Group(iA2);
        Check(g2.Contains(iA) && g2.Contains(iA2) && g2.Count == 2,
              $"leader=A2 -> closure ต้องดึง A เข้ามาด้วยเหมือนกัน (สมมาตร) = {{{string.Join(",", g2)}}}");

        Console.WriteLine("\n--- กรณี B มี demand จริง ต้องเข้าร่วมกลุ่มด้วย (ไม่ conflict กับ A/A2) ---");
        SeedDemand(roiB, 3, 90f); // มาตั้งแต่ t=90, wait = 100-90 = 10s
        var g3 = Group(iA);
        Check(g3.Contains(iA) && g3.Contains(iA2) && g3.Contains(iB) && g3.Count == 3,
              $"leader=A, B มีรถรอ -> กลุ่ม = {{{string.Join(",", g3)}}} ต้องมีครบ A, A2, B");

        Console.WriteLine("\n--- Bx และ CB conflict กับ A2 (mirror ของ A) ต้องไม่มีวันเข้ากลุ่ม แม้จะมี demand ล้น ---");
        SeedDemand(roiBx, 10, 50f); // รอนานมาก demand เยอะมาก
        var g4 = Group(iA);
        Check(!g4.Contains(iBx), $"leader=A, Bx demand สูง -> Bx ยังต้องไม่เข้ากลุ่ม (conflict) = {{{string.Join(",", g4)}}}");

        // ============ ROI ที่ใช้ร่วมกันหลาย movement ============
        Console.WriteLine("\n--- ROI เดียวครอบทั้ง A2 และ Ax (แบบที่ detector จริงเห็น) ---");
        var shared = new ROIZone { laneName = "Ax", alsoCountLanes = new[] { "A2" } };
        Check(shared.Accepts("Ax"),  "ROI ร่วมรับเลน Ax");
        Check(shared.Accepts("A2"),  "ROI ร่วมรับเลน A2");
        Check(!shared.Accepts("A"),  "ROI ร่วมต้องไม่รับเลน A (คนละเลนกายภาพ ห่าง 3.5 ม.)");
        Check(!shared.Accepts("Bx"), "ROI ร่วมต้องไม่รับเลน Bx");
        Check(shared.DisplayName == "Ax+A2", $"ชื่อที่โชว์บนจอบอกครบทั้งสอง movement (ได้ '{shared.DisplayName}')");

        // ยัดรถปนกันสองเลนเข้า ROI เดียว แล้วดูว่าแยกนับได้ไหม
        var dict = (Dictionary<CarAgent, float>)PrivateField(typeof(ROIZone), "carsInZone").GetValue(shared);
        dict.Clear();
        for (int i = 0; i < 7; i++) dict[new CarAgent { assignedLane = "Ax" }] = 10f;
        for (int i = 0; i < 2; i++) dict[new CarAgent { assignedLane = "A2" }] = 10f;
        Check(shared.GetVehicleCount() == 9,
              $"GetVehicleCount() = จำนวนรถในเลนนอกทั้งหมด 9 คัน (สิ่งที่ detector จริงเห็น) ได้ {shared.GetVehicleCount()}");
        Check(shared.GetVehicleCount("Ax") == 7 && shared.GetVehicleCount("A2") == 2,
              $"แยกนับราย movement ได้สำหรับการตรวจสอบ (Ax={shared.GetVehicleCount("Ax")}, A2={shared.GetVehicleCount("A2")})");

        // ============ เลนให้ทาง: ไฟกลับด้าน ============
        Console.WriteLine("\n--- Ax เป็นเลนให้ทาง: Bx เขียว/เหลือง = Ax หยุด, Bx แดง = Ax ไปได้ ---");
        var greenField = PrivateField(typeof(CarAgent), "lightIsGreen");
        Func<TrafficLight.LightState, bool, bool> CanGo = (state, inverted) =>
        {
            var c = new CarAgent { lightIsInverted = inverted };
            c.OnLightStateChanged(state);
            return (bool)greenField.GetValue(c);
        };

        Check(!CanGo(TrafficLight.LightState.Green,  true),  "Bx เขียว  -> Ax ไปไม่ได้");
        Check(!CanGo(TrafficLight.LightState.Yellow, true),  "Bx เหลือง -> Ax ยังไปไม่ได้ (รถ Bx ยังเคลียร์ปากทางไม่หมด)");
        Check( CanGo(TrafficLight.LightState.Red,    true),  "Bx แดง    -> Ax ไปได้ตามปกติ");

        // เลนปกติต้องไม่โดนกระทบ
        Check( CanGo(TrafficLight.LightState.Green, false), "เลนปกติ: ไฟเขียวของตัวเอง -> ไปได้ (ไม่โดน inversion กระทบ)");
        Check(!CanGo(TrafficLight.LightState.Red,   false), "เลนปกติ: ไฟแดงของตัวเอง  -> หยุด");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
