# ข้อเสนอแก้ไข C1 — นาฬิกา `queueStart` ของหัวที่ได้ไฟเขียวต่อเนื่อง

| | |
|---|---|
| **เลขที่** | C1 (พร้อม C2, C3 ที่เป็น measurement-only) |
| **วันที่** | 11 กันยายน 2026 |
| **ผู้เสนอ** | Kimmy (Badin) |
| **ต้องการจาก** | Itim (Ratchnon) — เห็นชอบหรือคัดค้าน ตาม `SODA_BASELINE.md` §9.1 |
| **ประเภท** | **behaviour change** → ตาม §9.3 ทำให้ผล batch ที่รันไปแล้วใช้ไม่ได้ ต้องรันใหม่ |
| **สถานะโค้ด** | แก้แล้วในเครื่องทดสอบ เทสผ่านครบ 9 ชุด **ยังไม่ merge เข้า repo** รอมติ |

---

## 0. สรุปให้สั้นที่สุด

`ServeOnePhase` รีเซ็ตนาฬิกา `queueStart` เฉพาะหัวที่ **หลุดออกจากกลุ่มไฟเขียว** หัวที่อยู่ในกลุ่มต่อเนื่องข้ามเฟสจึงถือค่า `queueStart` เก่าค้างไว้ไม่จำกัดเวลา ระบบเลยคิดว่าหัวที่กำลังได้ไฟเขียวอยู่ตลอดเวลาคือหัวที่ "รอนานที่สุด" แล้วมันชนะทั้ง Q1 และ Q2 ซ้ำไปเรื่อย ๆ หัวที่อดจริงไม่มีวันได้คิว

แก้โดยรีเซ็ตนาฬิกาให้ทุกหัวที่เพิ่งได้ไฟเขียวไป ไม่ใช่แค่หัวที่หลุด — กลับไปตรงกับความหมายที่ baseline เขียนไว้ตั้งแต่แรกว่า `queueStart` คือ "รอมานานแค่ไหนนับจากครั้งล่าสุดที่หัวนี้ได้ไฟเขียว"

---

## 1. ผล batch v1 ที่รันเสร็จแล้ว (บริบท)

216 รอบ (4 scenario × 27 rep × 2 controller) จบเมื่อ 11 ก.ย. 14:23 น. ใช้เวลา 11 ชม. 31 นาที ที่ `simulationSpeed = 10`

เวลารอเฉลี่ยต่อคัน หน่วยวินาที จับคู่ด้วย seed เดียวกัน n = 27 ต่อ scenario

| Scenario | Fixed | Adaptive | ผลต่าง | t | Adaptive ชนะ |
|---|---|---|---|---|---|
| MorningPeak | 22.77 ± 0.97 | 14.11 ± 0.61 | +8.66 (38.0 %) | +38.2 | 27/27 |
| MiddayNormal | 22.66 ± 0.89 | 14.78 ± 0.75 | +7.88 (34.8 %) | +41.8 | 27/27 |
| ABACEvent | 93.35 ± 6.45 | 67.26 ± 14.13 | +26.10 (28.0 %) | +8.3 | 25/27 |
| EveningPeak | 25.40 ± 0.72 | 25.95 ± 1.57 | −0.55 (−2.2 %) | −1.8 | 11/27 |
| **รวม** | **41.05** | **30.52** | **+25.6 %** | | |

ตัวเลขค่าเฉลี่ยดูดี แต่ดู **หาง** ของการกระจายแล้วเจอปัญหา ทุกเลน ทุก scenario รวม 27 rep

| | Fixed p95 / max | Adaptive p95 / max |
|---|---|---|
| Midday ทุกเลน | สูงสุด 88.2 / 94.2 | สูงสุด 49.4 / **85.8** ✅ ดีกว่าทุกด้าน |
| Morning Bx | 89.2 / 100.4 | 58.7 / **131.8** ⚠️ |
| Evening CB | 78.5 / 84.2 | 148.0 / **344.4** ❌ |
| Evening Bx | 90.8 / 103.9 | 123.4 / **264.7** ❌ |
| Evening A | 70.5 / 78.6 | 78.9 / **192.8** ❌ |

Fixed มีเพดานหางที่ชัดเจนราว 100 วินาทีทุกเลน เพราะรอบ 106 วินาทีการันตีว่าทุกเลนได้ไฟแน่นอน ส่วน Adaptive หางหลุดไปถึง 344 วินาที ทั้งที่ `maxWaitTime` ตั้งไว้ 45 วินาที กลไก anti-starvation ควรกันไม่ให้เกิดเรื่องแบบนี้

---

## 2. หลักฐานจาก log จริง

ไฟล์ `phases_Adaptive_EveningPeak_Rep17_20260911_062549.csv` ช่วง t = 1104.6 ถึง t = 1470.2 คือ **365.5 วินาทีที่ H_CB ไม่ได้ไฟเขียวเลยสักครั้ง** ทั้งที่มีรถรออยู่

```
 start   given   used  group           end reason
1104.6   21.43  20.00  CB              starvation     <- CB ได้ไฟครั้งสุดท้าย
1129.7   37.60  19.98  A+B+A2          starvation
1149.6    9.88   9.91  A+B+A2          completed
1159.5   12.19  12.19  A+B+A2          completed
1171.7    9.88   9.89  A+B+A2          completed
1181.6   12.19  12.20  A+B+A2          completed
1193.8    9.88   9.92  A+B+A2          completed
1203.8    9.88   9.89  A+B+A2          completed
1213.6   14.50   5.97  A+B+A2          max-green
1224.7   39.91  19.95  Bx+B            starvation
1244.6    9.88   9.89  B+Bx            completed
1259.6   30.67  19.94  B+A+A2          starvation
1284.6   14.50  14.55  B+Bx            completed
1304.2   23.74  19.98  B+A+A2          starvation
1324.2   12.19  12.25  B+A+A2          completed
1341.5   12.19  12.25  B+Bx            completed
1358.8   19.12  19.16  B+A+A2          completed
1378.0   16.81  16.81  B+A+A2          completed
1399.9   26.05  19.92  B+Bx            starvation
1424.9   28.36  19.95  B+A+A2          starvation
1444.9   12.19  12.27  B+A+A2          completed
1457.1    8.00   7.97  B+A+A2          completed
1470.2   42.22  20.13  CB              starvation     <- 365.5 วินาทีต่อมา
```

สังเกตสามอย่าง

1. **H_B อยู่ในกลุ่มไฟเขียวทุกเฟสติดกัน 24 เฟส ไม่เคยหลุดออกจากกลุ่มเลย** ตลอด 365 วินาทีนี้
2. มี 7 เฟสที่จบด้วยเหตุผล `starvation` แปลว่ากลไก anti-starvation ทำงาน แต่หัวที่มันเลือกให้ต่อไม่ใช่ CB สักครั้ง
3. รถ CB ที่ค้างอยู่เคลียร์ออกตอน t ≈ 1473–1483 โดยรอกันคนละ 280 ถึง **344 วินาที**

ตัวเลขยืนยันจากอีกทาง: run นี้มี 99 เฟส แต่ log `StarvationEvents` ไป **145 ครั้ง** มากกว่าจำนวนเฟส เพราะ H_B เข้าเงื่อนไข "กำลังอดอยาก" แทบทุกครั้งที่มีการตัดสินใจ ทั้งที่มันคือหัวที่ได้ไฟเขียวอยู่ คอลัมน์ `StarvationEvents` ของ batch v1 จึงไม่ได้วัด starvation จริง

---

## 3. สาเหตุในโค้ด

`Assets/Scripts/AdaptiveController.cs` ใน `ServeOnePhase` ตรงช่วง clearance

```csharp
List<int> dropping = Except(activeGroup, nextGroup);
if (dropping.Count > 0)
{
    ...
    activeGroup = Except(activeGroup, dropping);
    ResetQueueStartFor(dropping);        // <-- รีเซ็ตเฉพาะหัวที่กลายเป็นแดง
    ...
}
```

`ResetQueueStartFor` ถูกเรียกด้วย `dropping` เท่านั้น หัวที่อยู่ในทั้ง `activeGroup` และ `nextGroup` คือหัวที่ได้ไฟเขียวต่ออีกเฟส ไม่ได้อยู่ใน `dropping` จึงไม่เคยถูกรีเซ็ต

ผลลัพธ์: ตราบใดที่หัวหนึ่งยังอยู่ในกลุ่มไฟเขียวต่อเนื่อง `queueStart` ของมันจะค้างอยู่ที่ค่าเดิมไปเรื่อย ๆ แล้ว

- `WaitOf(h) = Time.time − h.queueStart` โตขึ้นเรื่อย ๆ ไม่มีขีดจำกัด
- `FindStarvingHead` (Q1) เลือกหัวที่ `queueStart` เก่าที่สุด → เลือกหัวนี้ตลอด
- `ChooseLeaderByQueueStart` (Q2) เลือกหัวที่ `queueStart` เก่าที่สุด → เลือกหัวนี้ตลอด
- `BuildCompatibleGroup` (Q3) สร้างกลุ่มจาก leader ตัวนี้ → หัวนี้อยู่ในกลุ่มอีกรอบ → วนกลับข้อแรก

เป็นวงจรที่ล็อกตัวเอง หัวที่ครองไฟเขียวได้หนึ่งครั้งในสภาพจราจรอิ่มตัวจะครองต่อไปจนกว่ารถในเลนมันจะหมด

### ทำไม Midday กับ Morning ไม่เป็น

`ResetQueueStartFor` ตั้ง `queueStart = Infinity` เมื่อเลนว่าง และ `Snapshot()` ตั้งค่าใหม่เมื่อรถคันแรกมาถึง ในสภาพที่ demand ไม่อิ่มตัว หัวไฟจะว่างเป็นระยะ นาฬิกาจึงถูกล้างโดยอัตโนมัติ อาการนี้โผล่เฉพาะตอนที่หัวไฟ**มีรถค้างตลอดเวลาไม่เคยว่าง** ซึ่งคือสภาพของ H_B ใน EveningPeak พอดี

นี่อธิบายว่าทำไม MiddayNormal ถึงชนะ Fixed ทั้งค่าเฉลี่ยและหาง (max 85.8 เทียบ 94.2) ในขณะที่ EveningPeak แพ้ — และทำไมเราไม่เห็นมันตอนเทสด้วยมือ

---

## 4. สิ่งที่เสนอให้แก้

เพิ่มการรีเซ็ตนาฬิกาให้หัวที่อยู่ในกลุ่มต่อเนื่อง หลังเปิดไฟเขียวของเฟสใหม่

```csharp
// เก็บไว้ก่อน activeGroup ถูกเขียนทับ
List<int> servedLastPhase = new List<int>(activeGroup);
List<int> dropping = Except(activeGroup, nextGroup);
...
SetGroupState(turningOn, TrafficLight.LightState.Green);
activeGroup = new List<int>(nextGroup);

ResetContinuingHeads(servedLastPhase, dropping);   // ⭐ เพิ่มบรรทัดนี้
```

```csharp
public void ResetContinuingHeads(List<int> servedLastPhase, List<int> dropping)
{
    ResetQueueStartFor(Except(servedLastPhase, dropping));
}
```

แยกเป็นเมธอดสาธารณะเพื่อให้เทสเรียกลำดับเดียวกับ production ได้จริง ไม่ใช่เทสจำลองตรรกะเอง

ความหมายของ `queueStart` หลังแก้ตรงกับที่ `SODA_BASELINE.md` §4 เขียนไว้แล้ว คือ **เวลาที่หัวนี้เริ่มรอ นับจากครั้งล่าสุดที่มันได้ไฟเขียว** ไม่ใช่ "นับจากครั้งล่าสุดที่มันกลายเป็นสีแดง"

---

## 5. เทสที่ยืนยัน

เพิ่มชุดใหม่ `QueueClockTest` (`Test9.cs`) จำลองลำดับกลุ่มจาก log จริงข้างบน คือ B อยู่ในกลุ่มทุกเฟส CB ไม่เคยถูกเลือก ทุกเลนมีรถค้าง 4 คันไม่เคยว่าง

**ก่อนแก้ — ตก 4 ข้อ**

```
      H_A   queueStart=   1100  ระบบคิดว่ารอ    0s
      H_B   queueStart=    900  ระบบคิดว่ารอ  200s     <- เขียวอยู่ตลอด แต่ระบบคิดว่ารอนานสุด
      H_Bx  queueStart=   1080  ระบบคิดว่ารอ   20s
      H_CB  queueStart=   1000  ระบบคิดว่ารอ  100s     <- อดจริง
  FAIL  หัวที่เพิ่งได้ไฟเขียวจบเฟส ต้องมี queueStart ไม่เก่ากว่าเวลาที่เฟสนั้นเริ่ม
  FAIL  หัวที่อดจริง (CB) ต้องมีคิวเก่ากว่าหัวที่เพิ่งได้เขียว (B)
      Q1 FindStarvingHead      -> H_B
  FAIL  Q1 ต้องชี้ไปที่ H_CB ไม่ใช่ H_B ที่เขียวอยู่ตลอด
      Q2 ChooseLeaderByQueueStart -> H_B
  FAIL  Q2 ต้องเลือก H_CB เป็น leader ของเฟสถัดไป
```

**หลังแก้ — ผ่านทั้งหมด**

```
      H_A   queueStart=   1100  ระบบคิดว่ารอ    0s
      H_B   queueStart=   1100  ระบบคิดว่ารอ    0s
      H_Bx  queueStart=   1080  ระบบคิดว่ารอ   20s
      H_CB  queueStart=   1000  ระบบคิดว่ารอ  100s
  PASS  หัวที่เพิ่งได้ไฟเขียวจบเฟส ต้องมี queueStart ไม่เก่ากว่าเวลาที่เฟสนั้นเริ่ม
  PASS  หัวที่อดจริง (CB) ต้องมีคิวเก่ากว่าหัวที่เพิ่งได้เขียว (B)
      Q1 FindStarvingHead      -> H_CB
      Q2 ChooseLeaderByQueueStart -> H_CB
  PASS  ServeOnePhase ยังเรียก ResetContinuingHeads(servedLastPhase, dropping) อยู่
  PASS  phaseCounter++ อยู่ก่อน early return
```

ชุดเทสเดิมทั้ง 8 ชุดผ่านหมดหลังแก้ ไม่มี regression

```
ConflictTest · FixedCycleTest · BatchMathTest · EtaTest · SharedLaneTest
GeometryTest · SceneAuditTest · SodaTest · QueueClockTest
########## ALL SUITES PASSED ##########
```

มีเทสกันถอยหลังด้วย คือสแกนซอร์สของ `ServeOnePhase` ว่ายังเรียก `ResetContinuingHeads` อยู่จริง ถ้าใครลบทิ้งเทสจะตกทันที แบบเดียวกับที่ `SodaTest` §3 สแกนว่าไม่มี API ของ tracker หลงเหลือ

---

## 6. อีกสองเรื่องที่เจอ เป็น measurement-only ไม่ต้องรันใหม่เพราะมัน

### C2 — แถว Fixed ใน `summary_all_runs.csv` แบกตัวเลขของ Adaptive รอบก่อนหน้า

`ScenarioManager.ApplyController` เรียก `adaptiveController.ForceStop()` ก่อนรันรอบ Fixed แต่ `ForceStop()` ไม่ได้ล้างตัวนับ ล้างเฉพาะใน `RestartCycle()` ซึ่งไม่ถูกเรียกตอนรัน Fixed

ตรวจแล้ว **107 จาก 108 แถว Fixed มีค่า `CollectCount`, `CollectDuration`, `SkippedHeads`, `QueueStartResets`, `TieBreaksApplied`, `TieBreaksExcludingBx` ตรงเป๊ะกับแถว Adaptive ที่อยู่เหนือมัน** (แถวแรกเป็น 0 เพราะยังไม่มีอะไรรันมาก่อน)

ไม่กระทบ `AvgWaitTime`, `MaxWaitTime`, `TotalSpawned`, `VehiclesCleared`, `StarvationEvents`, `IdleLaneSeconds` ซึ่งเป็นตัวเลขที่ใช้เปรียบเทียบจริง

แก้โดยล้างตัวนับใน `ForceStop()` ด้วย

### C3 — `PhaseIndex` ใน `phases_*.csv` เป็น 0 ทั้งไฟล์

`phaseCounter++` อยู่ใน `TraceBegin()` **หลัง** บรรทัด `if (!showDecisionPanel) return;` ตอนรัน batch เราปิด decision panel ทุกรอบ ตัวนับจึงไม่เคยเดิน

แก้โดยย้าย `phaseCounter++` ขึ้นไปก่อน early return

---

## 7. ผลที่คาดว่าจะเกิดหลังแก้ และสิ่งที่ยังไม่รู้

คาดว่า

- หางของ EveningPeak จะหดลงมาก CB ไม่ควรเกินราว 100 วินาที เพราะ Q1 จะเห็นมันภายใน 42.75 วินาทีหลังมันเริ่มรอ
- EveningPeak น่าจะพลิกจาก −2.2 % เป็นบวก
- `StarvationEvents` จะลดลงมาใกล้ความจริง

ยังไม่รู้และต้องยอมรับตรง ๆ

- **ค่าเฉลี่ยของ MorningPeak กับ MiddayNormal อาจลดลงบ้าง** เพราะการเพิ่มความเป็นธรรมมักแลกมาด้วย throughput ตัวเลข 38 % กับ 34.8 % อาจไม่คงอยู่เท่าเดิม
- ตัวเลขพาดหัว 25.6 % จะเปลี่ยน ต้องรันใหม่ถึงจะรู้

ถ้าตัวเลขลดลงบ้างแต่หางหายไป ผมคิดว่านั่นคือผลที่ดีกว่าสำหรับการ defend เพราะ "ค่าเฉลี่ยดีขึ้นแต่มีคนรอ 344 วินาที" เป็นจุดที่กรรมการถามแน่ ๆ และตอบยากกว่า "ค่าเฉลี่ยดีขึ้นน้อยลงหน่อยแต่ไม่มีใครรอเกิน 100 วินาที"

---

## 8. สิ่งที่ขอจากอิทิ่ม

1. **เห็นชอบหรือคัดค้าน C1** ถ้าคัดค้านช่วยบอกด้วยว่าตรงไหน ตาม §9.2 ต้องมีหลักฐานประกอบ ไม่ใช่ความเห็นอย่างเดียว
2. ถ้าเห็นชอบ **`soda.py` ต้องแก้ให้ตรงกัน** ด้วย ไม่งั้น `verify_traces.py` จะไม่ตรงอีก จุดที่ต้องดูคือตอนจบเฟสว่ารีเซ็ตนาฬิกาให้ใครบ้าง
3. ยืนยันว่าที่ค้างอยู่ตาม §8.2 (counting window, gap-out ยิงที่ค่าศูนย์ครั้งที่สอง, เพิ่ม COLLECT) ยังไม่ได้ทำใช่ไหม ถ้ายัง อยากให้ทำรอบเดียวพร้อม C1 เลย
4. §8.5 วันที่ถ่ายโดรนสำหรับ 39 ตัวอย่าง headway และ 298/303 ของ detector — ยังขาดอยู่ ต้องใส่ในส่วน methods ของรายงาน

เมื่อตกลงกันได้ ผมจะ merge เข้า repo อัปเดต `SODA_BASELINE.md` (§4, §8, change log, Appendix A ถ้า trace S1–S5 เปลี่ยน) แล้วรัน batch ใหม่ 216 รอบ ใช้เวลาราว 11.5 ชั่วโมง

ระหว่างรอ ผลของ batch v1 ยังเก็บไว้ครบทุกไฟล์ ใช้เทียบ before/after ได้

---

## ภาคผนวก — ไฟล์ที่เกี่ยวข้อง

| ไฟล์ | ใช้ทำอะไร |
|---|---|
| `SimulationResults/summary_all_runs.csv` | 216 แถว ตัวเลขหลักทั้งหมด |
| `SimulationResults/phases_Adaptive_EveningPeak_Rep17_*.csv` | log ที่ยกมาในข้อ 2 |
| `SimulationResults/detailed_*_EveningPeak_Rep*.csv` | เวลารอรายคัน ใช้คำนวณ p95/max รายเลน |
| `SimulationResults/batch_summary_20260911_142347.txt` | สรุปที่ ScenarioManager เขียนเอง |
| `Tests/QueueClockTest.cs` | เทสใหม่ ยังไม่ commit |
