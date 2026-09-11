using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// ตรวจ "ความสอดคล้องของตัวเลข" ระหว่างพารามิเตอร์พฤติกรรมรถ (CarAgent/CarSpawner)
/// กับพิกัดจริงในซีน (ABACSceneBuilder) — เป็นเทสที่รันนอก Unity ได้เพราะเป็นเลขคณิตล้วน
/// ครอบคลุมบั๊ก 2 ตัวที่เจอจากภาพซีนจริง: "Ax จอดไกล" และ "รถซ้อนกัน"
public class GeometryTest
{
    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    // อ่าน field private ของ CarAgent หลังตั้ง driver profile แล้ว
    static float Profile(CarAgent.DriverType type, string field)
    {
        var car = new CarAgent { driverType = type };
        var m = typeof(CarAgent).GetMethod("SetProfileParameters",
                    BindingFlags.NonPublic | BindingFlags.Instance);
        m.Invoke(car, null);
        var f = typeof(CarAgent).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
        return (float)f.GetValue(car);
    }

    public static int Main()
    {
        var car = new CarAgent();
        var spawner = new CarSpawner();

        float lat        = car.lookAheadAngle;          // ระยะข้างที่ยังนับว่าเลนเดียวกัน
        float look       = car.lookAheadDistance;       // เพดานระยะมองหน้า
        float carLen     = car.carLength;
        float minGap     = car.minGap;
        float clearR     = spawner.spawnClearRadius;

        float offset     = ABACSceneBuilder.AX_BYPASS_OFFSET_Z;
        float zAx        = ABACSceneBuilder.Z_AX;
        float zA         = ABACSceneBuilder.Z_A;
        float roadZ      = ABACSceneBuilder.ROAD_Z;
        float roadW      = ABACSceneBuilder.ROAD_WIDTH;
        float carW       = ABACSceneBuilder.CAR_WIDTH;
        float roiShared  = ABACSceneBuilder.ROI_WIDTH_SHARED;
        float stopAX     = ABACSceneBuilder.STOP_A_X;

        Console.WriteLine($"lookAheadAngle={lat}  lookAheadDistance={look}  carLength={carLen}  " +
                          $"minGap={minGap}  spawnClearRadius={clearR}  bypassOffset={offset}");
        Console.WriteLine();

        // ===== 1) ขาเข้าฝั่งตะวันออกต้องมี "2 แถว" เท่านั้น =====
        Console.WriteLine("--- 1) ขาเข้าฝั่งตะวันออก: A (เลนใน) + Ax/A2 (เลนนอก) = 2 แถว ---");
        float chance = ABACSceneBuilder.AX_BYPASS_CHANCE;
        var rows = new SortedSet<float> { zA, zAx };
        if (chance > 0f && offset != 0f) rows.Add(zAx + offset);
        Check(rows.Count == 2,
            $"จำนวนแถวรถบนขาเข้าฝั่งตะวันออก = {rows.Count} แถว (z = {string.Join(", ", rows)}) ต้องเป็น 2 พอดี");
        Check(!(chance > 0f && offset == 0f),
            "ห้ามเปิดโอกาสแซง (chance>0) โดยไม่มีระยะเยื้อง (offset=0) — จะกลายเป็นรถวิ่งทะลุกันเหมือนบั๊กรอบที่ 6");

        // ===== 1.1) ถ้าวันไหนเปิดกลไกแซงกลับมา กฎพวกนี้ต้องยังจริง =====
        Console.WriteLine("\n--- 1.1) กฎที่ต้องเป็นจริงเสมอ 'ถ้า' เปิดกลไกแซงกลับมาใช้ ---");
        if (chance > 0f)
        {
            Check(offset > lat,
                $"ระยะเยื้อง ({offset} ม.) > lookAheadAngle ({lat} ม.) -> รถที่แซงไม่เห็นคิว จึงไม่เบรกตาม");
            Check(offset - carW > 0f,
                $"ระยะเฉียดข้างตอนแซง = {offset - carW:F2} ม. > 0 -> ไม่ซ้อนทับกัน");
            float northEdgeOn = roadZ + roadW / 2f;
            Check(zAx + offset + carW / 2f <= northEdgeOn,
                "ขอบนอกของรถที่แซงยังอยู่ในผิวถนน");
        }
        else
        {
            Check(offset == 0f,
                $"ปิดกลไกแซงอยู่ (chance={chance}) -> ระยะเยื้องต้องเป็น 0 ด้วย (ได้ {offset}) ไม่งั้นเหลือแถวลอย ๆ ในซีน");
            Console.WriteLine("  SKIP  กฎระยะเยื้อง/ขอบถนน (กลไกแซงปิดอยู่ — จะถูกตรวจอัตโนมัติถ้าเปิดกลับมา)");
        }

        // ===== 2) ROI ของเลนนอกต้องครอบทุกแถวที่รถวิ่งจริง =====
        Console.WriteLine("\n--- 2) ROI_Ax ต้องครอบทุกแถวที่รถเลนนอกวิ่งจริง ---");
        float roiCenter = zAx + offset / 2f;
        float roiLo = roiCenter - roiShared / 2f, roiHi = roiCenter + roiShared / 2f;
        Check(roiLo <= zAx && zAx <= roiHi,
            $"ROI ครอบเลนปกติ z={zAx} (ช่วง {roiLo:F2}..{roiHi:F2})");
        Check(roiLo <= zAx + offset && zAx + offset <= roiHi,
            $"ROI ครอบทุกแถวของเลนนอก รวม z={zAx + offset:F2} -> ไม่มีรถหลุดการนับเวลารอ (ตัวชี้วัดหลักของงาน)");
        Check(!(roiLo <= zA && zA <= roiHi),
            $"ROI ของเลนนอกต้อง *ไม่* ครอบเลนใน z={zA} (ช่วง {roiLo:F2}..{roiHi:F2}) — ไม่งั้นเผลอนับรถ A ปนเข้ามา");

        // ===== 3) เลนข้าง ๆ ต้องไม่กวนกัน =====
        Console.WriteLine("\n--- 3) เลนคนละเลนต้องไม่เบรกให้กัน ---");
        float laneGap = Math.Abs(zAx - zA);
        Check(laneGap > lat,
            $"ระยะห่างเลน A กับเลนแชร์ = {laneGap} ม. > lookAheadAngle ({lat}) -> A ไม่บล็อก Ax/A2 (ตรงตามโมเดล)");
        Check(Math.Abs((zAx + offset) - zA) > lat,
            "ทุกแถวของเลนนอกยังห่างจากเลน A พอที่จะไม่กวนกัน");

        // ===== 4) แก้บั๊ก "Ax จอดไกล": เพดานระยะมองต้องพอสำหรับคนขับที่เบรกช้าที่สุด =====
        Console.WriteLine("\n--- 4) เพดานระยะมอง (แก้บั๊ก Ax จอดไกล) ---");
        foreach (CarAgent.DriverType t in new[] { CarAgent.DriverType.Aggressive,
                                                  CarAgent.DriverType.Normal,
                                                  CarAgent.DriverType.Cautious })
        {
            float v = Profile(t, "approachSpeed");
            float d = Profile(t, "decelRate");
            float brake = v * v / (2f * d);
            Check(look >= brake * 1.15f,
                $"{t}: ระยะเบรก {brake:F1} ม. — เพดานมอง {look} ม. เผื่อไว้ {look / brake:F2} เท่า");
        }

        // ===== 5) คิวที่บล็อกจริงต้องอยู่ในระยะมองเสมอ =====
        Console.WriteLine("\n--- 5) คิวที่บล็อก Ax ต้องอยู่ 'ก่อน' จุดหักเลี้ยว จึงยังตรวจเจอ ---");
        // A2 จอดโดยกันชนหน้าแตะเส้นหยุด -> ท้ายรถอยู่ที่ stopAX - carLen
        float a2Rear = stopAX - carLen;
        float axTurnX = -11.0f;   // waypoint แรกของ Ax
        Check(a2Rear < axTurnX,
            $"ท้ายรถ A2 ที่จอด (x={a2Rear:F1}) อยู่ก่อนจุดหักเลี้ยวของ Ax (x={axTurnX:F1}) " +
            "-> อยู่ในช่วงที่ระยะมองครอบถึงเสมอ การบล็อกยังทำงานเหมือนเดิม");

        // ===== 5.1) เลน Ax เป็น "เลนให้ทาง" — ตรวจตำแหน่งเส้นหยุด =====
        Console.WriteLine("\n--- 5.1) เส้นหยุดของ Ax (หยุดตอน Bx เขียว) ---");
        float axStop = ABACSceneBuilder.AX_STOP_X;
        float axTurn = ABACSceneBuilder.AX_TURN_X;
        Check(axStop <= axTurn + 0.001f,
            $"เส้นหยุด Ax (x={axStop}) ไม่เลยจุดหักเลี้ยว (x={axTurn}) -> รถไม่เริ่มเลี้ยวก่อนแล้วไปจอดขวางกลางแยก");
        // คิวต้องค้างอยู่ในเลนนอก ไม่ใช่หลุดเข้าไปในแยก (ตามที่เลือกไว้: "หยุดก่อนเลี้ยว คาอยู่ในเลนนอก")
        Check(axStop <= stopAX + 0.001f,
            $"เส้นหยุด Ax (x={axStop}) อยู่ก่อนหรือเสมอกับเส้นหยุด A2 (x={stopAX}) -> คิวสองกระแสต่อกันในเลนนอกเดียวกัน");
        Check(Math.Abs(axStop - stopAX) <= carLen,
            $"เส้นหยุด Ax กับ A2 ห่างกัน {Math.Abs(axStop - stopAX):F1} ม. (<= ความยาวรถ) -> ถือเป็นหัวคิวเดียวกัน ไม่มีช่องว่างประหลาด");

        // ===== 5.2) late merge ของ A2 =====
        Console.WriteLine("\n--- 5.2) A2 ต้อง merge เข้าเลนแชร์แบบ 'ช้า' (แทรกหัวคิว) ---");
        float mergeX = ABACSceneBuilder.A2_MERGE_X;
        float mergeDist = ABACSceneBuilder.A2_MERGE_DIST;
        float carSlot = carLen + minGap;                       // 1 ช่วงรถ = ตัวรถ + ระยะเว้น

        Check(mergeX < stopAX,
            $"จุด merge (x={mergeX}) อยู่ก่อนเส้นหยุดของ A2 (x={stopAX}) -> เปลี่ยนเลนเสร็จก่อนถึงเส้นหยุด");

        float slotsBeforeStop = (stopAX - mergeX) / carSlot;
        Check(slotsBeforeStop <= 6f,
            $"merge เสร็จก่อนเส้นหยุด {stopAX - mergeX:F0} ม. = {slotsBeforeStop:F1} ช่วงรถ " +
            "(Itim สังเกตว่า ~5 ช่วงรถ -> ต้องไม่เกิน 6 ไม่งั้นไม่ใช่การแทรกหัวคิวแล้ว)");
        Check(slotsBeforeStop >= 2f,
            $"แต่ก็ต้องไม่ชิดเส้นหยุดเกินไป ({slotsBeforeStop:F1} ช่วงรถ) ไม่งั้นเบียดเข้าไม่ทัน");

        // ระยะทั้งหมดที่ใช้เปลี่ยนเลน ต้องสั้นพอที่จะเรียกว่า late merge
        float totalMergeSpan = mergeDist + (stopAX - mergeX);
        Check(totalMergeSpan / carSlot <= 12f,
            $"ช่วงที่ใช้เปลี่ยนเลนทั้งหมด {totalMergeSpan:F0} ม. = {totalMergeSpan / carSlot:F1} ช่วงรถ " +
            "(ถ้ายาวกว่านี้ = กระจายกลางคิว ไม่ใช่แทรกหัวคิว)");

        // มุมเบนตอนเปลี่ยนเลน ต้องไม่ชันจนดูเหมือนหักเลี้ยว
        double mergeAngle = Math.Atan2(Math.Abs(zAx - zA), mergeDist) * 180.0 / Math.PI;
        Check(mergeAngle < 45.0,
            $"มุมเบนตอนเปลี่ยนเลน {mergeAngle:F0}° (< 45° = ถือเป็นการเปลี่ยนเลน ไม่ใช่การเลี้ยว " +
            "-> CarAgent.PathTurnsAway() จะไม่ไปตัดระยะมองของ A2 ทิ้ง)");

        // ===== 6) จุด spawn ต้องไม่ทับกัน =====
        Console.WriteLine("\n--- 6) จุด spawn ของ A2 กับ Ax (เลนกายภาพเดียวกัน) ---");
        float spawnSep = 20f;   // -SPAWN_FAR-20 เทียบกับ -SPAWN_FAR
        Check(spawnSep >= clearR + carLen,
            $"ระยะห่างจุด spawn A2/Ax = {spawnSep} ม. >= รัศมีเช็คที่ว่าง ({clearR}) + ความยาวรถ ({carLen})");
        Check(clearR >= carLen + minGap,
            $"รัศมีเช็คที่ว่าง ({clearR}) >= ความยาวรถ + ระยะห่างขั้นต่ำ ({carLen + minGap}) -> ไม่ปล่อยรถทับคันที่จอดอยู่");

        // ===== 7) กล้องทุกมุมต้องตั้งอยู่ "กลางแยก" แล้วหันไปดูแต่ละขา =====
        //
        // ⚠️ invariant ชุดนี้เปลี่ยนไปจากเดิมโดยตั้งใจ: ของเดิมกล้องยืนไกลแล้วเล็งเข้าหาแยก
        //    จึงเทสว่า "จุดที่เล็งลงพื้นต้องอยู่ใกล้แยก" — ตอนนี้กล้องอยู่กลางแยกแล้วหัน *ออก* ไปดูแต่ละขา
        //    เกณฑ์จึงกลายเป็น "ตัวกล้องต้องอยู่กลางแยก" + "เล็งไปตามแนวเลนนั้นจริง ๆ ไม่ใช่เลนข้าง ๆ"
        Console.WriteLine("\n--- 7) กล้องตั้งกลางแยก หันไปดูทีละขา ---");
        var shots = ABACSceneBuilder.MakeShots();
        Check(shots.Count >= 6, $"มีมุมกล้องให้เลือก {shots.Count} มุม");

        float jx = ABACSceneBuilder.ABAC_X, jz = ABACSceneBuilder.ROAD_Z;
        var seenLabels = new HashSet<string>();

        // เลน -> (แกนที่เลนวางตัว, พิกัดกึ่งกลางเลน, ทิศที่รถวิ่งเข้ามาหาแยก)
        //   'z' = เลนวางตัวตามแกน X ตำแหน่งเลนบอกด้วยค่า z / upstream = ด้าน X ที่รถมาจาก
        //   'x' = เลนวางตัวตามแกน Z ตำแหน่งเลนบอกด้วยค่า x / upstream = ด้าน Z ที่รถมาจาก
        var laneAxis = new Dictionary<string, char>();
        var laneAt   = new Dictionary<string, float>();
        var upstream = new Dictionary<string, float>();
        Action<string,char,float,float> lane = (n, ax, at, up) =>
            { laneAxis[n] = ax; laneAt[n] = at; upstream[n] = up; };
        lane("A",  'z', ABACSceneBuilder.Z_A,  -1f);
        lane("A2", 'z', ABACSceneBuilder.Z_AX, -1f);
        lane("Ax", 'z', ABACSceneBuilder.Z_AX, -1f);
        lane("B",  'z', ABACSceneBuilder.Z_B,  +1f);
        lane("Bx", 'z', ABACSceneBuilder.Z_BX, +1f);
        lane("CB", 'x', ABACSceneBuilder.X_CB, +1f);
        lane("CA", 'x', ABACSceneBuilder.X_CA, +1f);

        foreach (var sh in shots)
        {
            Check(seenLabels.Add(sh.label), $"ชื่อมุม '{sh.label}' ไม่ซ้ำกับมุมอื่น");

            // ---- ตัวกล้องต้องอยู่กลางแยก (เสาเดียว หมุนไปมา) ----
            double offJ = Math.Sqrt(Math.Pow(sh.position.x - jx, 2) + Math.Pow(sh.position.z - jz, 2));
            Check(offJ <= 8.0,
                $"'{sh.label}': ตัวกล้องห่างกลางแยก {offJ:F1} ม. (<= 8 = ถือว่าอยู่บนเสาต้นเดียวกลางแยก)");
            Check(sh.position.y >= 4f,
                $"'{sh.label}': กล้องสูง {sh.position.y:F1} ม. (ต้องพ้นหลังคารถ)");
            Check(sh.euler.x > 0f && sh.euler.x <= 90f,
                $"'{sh.label}': มุมก้ม {sh.euler.x:F0}° (ต้องก้มลง ไม่เงยขึ้นฟ้า)");
            Check(sh.fov >= 30f && sh.fov <= 75f, $"'{sh.label}': FOV {sh.fov:F0}");

            // ---- จุดที่เล็งลงพื้น ----
            double pitch = sh.euler.x * Math.PI / 180.0;
            double yaw   = sh.euler.y * Math.PI / 180.0;
            double fx = Math.Sin(yaw) * Math.Cos(pitch);
            double fz = Math.Cos(yaw) * Math.Cos(pitch);
            double t2 = sh.position.y / Math.Sin(pitch);
            double gx = sh.position.x + fx * t2;
            double gz = sh.position.z + fz * t2;

            if (string.IsNullOrEmpty(sh.lane))
            {
                // มุมภาพรวม/กลางแยก — ต้องเล็งลงมาที่ตัวแยกเอง
                double d = Math.Sqrt((gx - jx) * (gx - jx) + (gz - jz) * (gz - jz));
                Check(d <= 25.0, $"'{sh.label}': มุมภาพรวมต้องเล็งลงที่แยก (ห่าง {d:F1} ม.)");
                continue;
            }

            Check(sh.canFollow, $"'{sh.label}': ผูกกับเลน {sh.lane} จึงต้องเปิดโหมดตามรถได้");
            Check(laneAxis.ContainsKey(sh.lane), $"'{sh.label}': รู้จักเลน {sh.lane}");
            if (!laneAxis.ContainsKey(sh.lane)) continue;

            // เล็งไปตามแนวเลนนั้น ไม่ใช่เลนข้าง ๆ  (ระยะห่างด้านข้างจากกึ่งกลางเลน)
            double sideOff = laneAxis[sh.lane] == 'z'
                ? Math.Abs(gz - laneAt[sh.lane])
                : Math.Abs(gx - laneAt[sh.lane]);
            Check(sideOff <= 4.0,
                $"'{sh.label}': เล็งเบนจากกึ่งกลางเลน {sh.lane} ออกไป {sideOff:F1} ม. " +
                "(<= 4 = ยังอยู่ในเลนนั้น ไม่ไปโผล่เลนข้าง ๆ)");

            // ต้องหันไป "ทางที่รถวิ่งเข้ามา" ไม่ใช่หันหลังให้คิว
            double along = laneAxis[sh.lane] == 'z' ? (gx - jx) : (gz - jz);
            Check(along * upstream[sh.lane] >= 10.0,
                $"'{sh.label}': หันไปทางต้นทางของเลน {sh.lane} และมองไกลพอ ({Math.Abs(along):F0} ม. " +
                "จากแยก) = เห็นคิวที่วิ่งเข้ามาหา ไม่ใช่หันหลังให้");
        }

        // ===== 8) ROI tracking outline must not reach the vehicle behind =====
        // Regression: the outline used to be a solid box scaled 1.12x the car, and the boxes of
        // queued vehicles blended into one another. It is now an outline sized to the car's own
        // bounds + a tiny clearance pad, which must stay well inside the resting queue pitch.
        {
            var agent = new CarAgent();
            double roiCarLen = agent.carLength;
            double roiMinGap = agent.minGap;
            double boxLen   = CarPrefabBuilder.TargetLength + CarPrefabBuilder.RoiBoxPadding;
            double pitch    = roiCarLen + roiMinGap;              // centre-to-centre spacing at rest
            Console.WriteLine($"\n--- ROI outline ---\n    car {carLen:F2}m  minGap {minGap:F2}m  " +
                              $"pitch {pitch:F2}m  outline {boxLen:F2}m  bar {CarPrefabBuilder.RoiEdgeThickness:F2}m");

            Check(Math.Abs(CarPrefabBuilder.TargetLength - roiCarLen) < 0.001,
                  $"CarPrefabBuilder.TargetLength ตรงกับ CarAgent.carLength ({roiCarLen:F2} ม.)");
            Check(boxLen < pitch,
                  $"กล่อง ROI สั้นกว่าระยะห่างหัวถึงหัวตอนจอด จึงไม่ทับคันหลัง ({boxLen:F2} < {pitch:F2} ม.)");
            Check(pitch - boxLen >= 1.0,
                  $"เหลือช่องว่างระหว่างกล่องอย่างน้อย 1 ม. ({pitch - boxLen:F2} ม.)");
            Check(CarPrefabBuilder.RoiBoxPadding > 0.0 && CarPrefabBuilder.RoiBoxPadding < 0.3,
                  $"pad ของกล่องเล็กพอที่จะไม่โผล่พ้นตัวรถ แต่มากพอไม่ z-fight ({CarPrefabBuilder.RoiBoxPadding:F2} ม.)");
            Check(CarPrefabBuilder.RoiEdgeThickness > 0.02 && CarPrefabBuilder.RoiEdgeThickness < 0.25,
                  $"เส้นขอบหนาพอมองเห็นจากกล้องมุมสูง แต่ไม่กลายเป็นกล่องทึบ ({CarPrefabBuilder.RoiEdgeThickness:F2} ม.)");
            // an outline is only an outline while the bars are a small fraction of the face
            Check(CarPrefabBuilder.RoiEdgeThickness < ABACSceneBuilder.CAR_WIDTH * 0.1,
                  "เส้นขอบบางกว่า 10% ของความกว้างรถ (ยังเป็นเส้น ไม่ใช่ก้อน)");
        }

        // ===== 9) Scene-serialization drift guard =====
        // Itim จับได้ว่า SampleScene.unity เก็บ 1 / 2.5 / 8 ไว้ ทับค่า 2.95 / 2.31 / 4 ในโค้ด
        // เพราะ Unity serialize ค่าของ component ที่วางในซีนแล้ว "ทับ" ค่า default ของสคริปต์เสมอ
        {
            Console.WriteLine("\n--- measured-parameter guard ---");

            Check(Math.Abs(AdaptiveController.MEASURED_STARTUP_LOST_TIME  - 2.95) < 1e-6,
                  "ค่าคงที่ startup lost time = 2.95 s ตามที่วัดจริง");
            Check(Math.Abs(AdaptiveController.MEASURED_SATURATION_HEADWAY - 2.31) < 1e-6,
                  "ค่าคงที่ saturation headway = 2.31 s/คัน ตามที่วัดจริง");
            var probe = new AdaptiveController();
            Check(Math.Abs(probe.collectWindow - 7.0) < 1e-6 && probe.earlyExitDemand == 3,
                  "COLLECT เป็นตัวสะสมดีมานด์ตัวเดียว (เพดาน 7 วิ / early exit 3 คัน)");

            // ค่าตำราต้องไม่ใช่ค่าที่ใช้ — ถ้าเผลอกลับไปใช้ HCM/proposal เมื่อไหร่ ต้องแตกตรงนี้
            Check(Math.Abs(AdaptiveController.MEASURED_SATURATION_HEADWAY - 2.5) > 0.01,
                  "ไม่ใช่ 2.5 (ค่าที่ proposal สมมติไว้ตอนแรก — §5.1.1 แทนที่ไปแล้ว)");
            Check(Math.Abs(AdaptiveController.MEASURED_STARTUP_LOST_TIME - 2.0) > 0.01,
                  "ไม่ใช่ 2.0 (ค่า default ของ HCM)");
            Check(Math.Abs(AdaptiveController.MEASURED_STARTUP_LOST_TIME - 1.0) > 0.01,
                  "ไม่ใช่ 1.0 (ค่าที่ค้างอยู่ในซีน ไม่ใช่ทั้งค่าที่วัดและค่า HCM)");

            // LoadMeasuredPreset ต้องล้างค่าที่เพี้ยนได้ครบทั้งสามฟิลด์
            var ac = new AdaptiveController();
            ac.startupLostTime = 1f; ac.saturationHeadway = 2.5f;
            ac.LoadMeasuredPreset();
            Check(Math.Abs(ac.startupLostTime - 2.95f) < 1e-6
               && Math.Abs(ac.saturationHeadway - 2.31f) < 1e-6,
                  "LoadMeasuredPreset() คืนค่าจากค่าที่ค้างในซีน (1 / 2.5)");

            // Enforce ต้องจับ drift ได้ และต้องรายงานว่าได้แก้จริง
            var ac2 = new AdaptiveController();
            ac2.startupLostTime = 1f; ac2.saturationHeadway = 2.5f;
            bool corrected = ac2.EnforceMeasuredParameters();
            Check(corrected, "EnforceMeasuredParameters() รายงานว่าเจอค่าเพี้ยนและแก้แล้ว");
            Check(Math.Abs(ac2.startupLostTime - 2.95f) < 1e-6
               && Math.Abs(ac2.saturationHeadway - 2.31f) < 1e-6,
                  "ค่าถูกแก้เป็นค่าที่วัดจริงหลัง Enforce");

            // ซีนที่ถูกต้องอยู่แล้วต้องไม่ถูกแตะ (ไม่มี false positive)
            var ac3 = new AdaptiveController();
            Check(!ac3.EnforceMeasuredParameters(), "ซีนที่ค่าถูกอยู่แล้ว Enforce ต้องไม่รายงานว่าแก้");

            // sensitivity run ต้องปิด guard ได้ ไม่งั้นทดลอง 13-23% ไม่ได้
            var ac4 = new AdaptiveController();
            ac4.useMeasuredParameters = false;
            ac4.saturationHeadway = 2.0f;
            Check(!ac4.EnforceMeasuredParameters() && Math.Abs(ac4.saturationHeadway - 2.0f) < 1e-6,
                  "ปิด useMeasuredParameters แล้วตั้งค่าเองได้ (sensitivity analysis ยังทำได้)");

            // ค่า default ของ field ต้องผูกกับ const ไม่ใช่พิมพ์เลขซ้ำ
            var ac5 = new AdaptiveController();
            Check(Math.Abs(ac5.startupLostTime   - AdaptiveController.MEASURED_STARTUP_LOST_TIME)  < 1e-6
               && Math.Abs(ac5.saturationHeadway - AdaptiveController.MEASURED_SATURATION_HEADWAY) < 1e-6
               && Math.Abs(ac5.collectWindow - 7f) < 1e-6,
                  "ค่า default ของ field ผูกกับ const (แก้ที่เดียวไม่หลุด)");
        }

        // ===== 10) Batch parameters that the scene had overridden =====
        {
            Console.WriteLine("\n--- batch preset guard ---");

            Check(ScenarioManager.BATCH_REPETITIONS == 27, "reps ต่อ scenario = 27");
            Check(Math.Abs(ScenarioManager.BATCH_SIM_SPEED - 10f) < 1e-6, "simulation speed = 10x (ปลอดภัยเพราะ CarAgent sub-step ≤ 0.02 วิ)");
            Check(Math.Abs(ScenarioManager.BATCH_RUN_DURATION - 1800f) < 1e-6, "run duration = 1800 s");
            Check(Math.Abs(ScenarioManager.BATCH_WARMUP - 120f) < 1e-6, "warmup = 120 s");

            // ⭐ invariant ข้ามไฟล์ที่ควรจะจับบั๊กนี้ได้ตั้งแต่แรก:
            //    batch (ScenarioManager) กับ test case T1-T13 (TestCaseLibrary) ต้องใช้สัดส่วนเดียวกัน
            Check(Math.Abs(ScenarioManager.MEASURED_A2_SHARE - TestCasePresets.A2_SHARE) < 1e-6,
                  $"a2Share ของ batch = ของ test case ({ScenarioManager.MEASURED_A2_SHARE:F3})");
            Check(Math.Abs(ScenarioManager.MEASURED_A2_SHARE - 0.054f) < 1e-6,
                  "a2Share = 5.4% (นับตรง, SP1_HANDOFF_v2 §5)");
            Check(Math.Abs(ScenarioManager.MEASURED_A2_SHARE - 0.18f) > 0.01f,
                  "ไม่ใช่ 0.18 (ค่า back-calculate เก่าที่ซีนค้างไว้)");

            var sm = new ScenarioManager();
            sm.repetitionsPerScenario = 1; sm.simulationSpeed = 2f; sm.a2Share = 0.18f;
            sm.LoadBatchPreset();
            Check(sm.repetitionsPerScenario == 27 && Math.Abs(sm.simulationSpeed - 10f) < 1e-6
               && Math.Abs(sm.a2Share - 0.054f) < 1e-6,
                  "LoadBatchPreset() ล้างค่าที่ค้างในซีน (1 / 2 / 0.18) ได้ครบ");

            var sm2 = new ScenarioManager();
            sm2.a2Share = 0.18f;
            Check(sm2.EnforceMeasuredShare(), "EnforceMeasuredShare() จับ a2Share = 0.18 ได้");
            Check(Math.Abs(sm2.a2Share - 0.054f) < 1e-6, "แก้ a2Share เป็น 0.054 แล้ว");
            Check(!new ScenarioManager().EnforceMeasuredShare(), "ค่าถูกอยู่แล้วต้องไม่รายงานว่าแก้");

            // จำนวน run ที่คาดหวัง — ซีนที่ค้าง reps=1 จะได้แค่ 10 runs
            var smp = new ScenarioManager();
            smp.LoadABACScenarioPreset();
            int scen = smp.scenarios.Count;
            Console.WriteLine($"    {scen} scenarios x {ScenarioManager.BATCH_REPETITIONS} reps x 2 controllers " +
                              $"= {scen * ScenarioManager.BATCH_REPETITIONS * 2} runs");
            Check(scen == 4, "preset มี 4 scenarios (Night ตัดออก)");
            bool hasNight = false;
            foreach (var sc in smp.scenarios) if (sc.name == "Night") hasNight = true;
            Check(!hasNight, "ไม่มี Night ใน batch");
            Check(scen * ScenarioManager.BATCH_REPETITIONS * 2 == 216, "4 scenarios -> 216 runs");

            // sub-stepping: ความละเอียดการเคลื่อนที่ต้องไม่ขึ้นกับ timeScale
            Console.WriteLine($"    CarAgent.MAX_STEP = {CarAgent.MAX_STEP}s");
            Check(Math.Abs(CarAgent.MAX_STEP - 0.02f) < 1e-6, "ก้าวสูงสุด 0.02 วิ = Unity fixed timestep");
            Check(CarAgent.SubStepsFor(1f/60f) == 1,            "1x @60fps  -> 1 ก้าว");
            Check(CarAgent.SubStepsFor(5f/60f) == 5,            "5x @60fps  -> 5 ก้าว (0.0833 s)");
            Check(CarAgent.SubStepsFor(10f/60f) == 9,           "10x @60fps -> 9 ก้าว (0.167 s) — เดิมเป็นก้าวเดียว 2.3 ม.");
            Check(CarAgent.SubStepsFor(0.3333f) == 17,          "ที่ Unity cap 0.333 s -> 17 ก้าว");
            Check(CarAgent.SubStepsFor(5f) == 50,               "เฟรมค้างนานผิดปกติ -> ไม่เกิน 50 ก้าว");
            Check(CarAgent.SubStepsFor(0f) == 0,                "dt = 0 -> ไม่ก้าว");
        }

        // ===== 11) Shared-lane length (Itim ยืนยัน 32 m = ~5 คันรถ, SP1_HANDOFF_v2 §5.6) =====
        {
            Console.WriteLine("\n--- shared lane length ---");
            double mergeEnd   = ABACSceneBuilder.STOP_A_X - ABACSceneBuilder.A2_MERGE_X;   // ระยะจากเส้นหยุดถึงจุด merge เสร็จ
            double steerStart = mergeEnd + ABACSceneBuilder.A2_MERGE_DIST;                  // ระยะที่เริ่มหักเลน
            Console.WriteLine($"    shared lane {ABACSceneBuilder.SHARED_LANE_LENGTH:F0} m | " +
                              $"เริ่มหักที่ {steerStart:F0} m | merge เสร็จที่ {mergeEnd:F0} m ก่อนเส้นหยุด");

            Check(Math.Abs(ABACSceneBuilder.SHARED_LANE_LENGTH - 32.0) < 0.01,
                  "ความยาวเลนแชร์ = 32 ม. ตามที่วัดจริง");
            Check(steerStart <= ABACSceneBuilder.SHARED_LANE_LENGTH + 0.01,
                  $"A2 เริ่มเปลี่ยนเลน 'ภายใน' เลนแชร์ ({steerStart:F0} <= 32 ม.)");
            Check(mergeEnd > 0 && mergeEnd < steerStart,
                  "merge เสร็จก่อนถึงเส้นหยุด และหลังจากจุดเริ่มหัก");
            // ยังต้อง late พอที่จะไปแทรกหัวคิว ไม่ใช่ต่อท้าย
            Check(steerStart <= 6.0 * (4.0 + 2.0) + 0.01,
                  "การเปลี่ยนเลนเกิดในช่วง <= 6 ช่วงรถสุดท้าย (ยังเป็น late merge)");
        }

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
