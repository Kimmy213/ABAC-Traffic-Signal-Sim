# รายงานบั๊ก C1 — Unity ไม่ได้ทำตามสเปคเรื่องรีเซ็ต `queueStart` ตอนจบเฟส

| | |
|---|---|
| **เลขที่** | C1 (พร้อม C2, C3 ที่เป็น measurement-only) |
| **วันที่** | 11 กันยายน 2026 |
| **ผู้รายงาน** | Kimmy (Badin) |
| **ประเภท** | **implementation bug ฝั่ง Unity** — โค้ด C# ไม่ตรงกับสเปคที่ freeze ไว้ ไม่ใช่การเปลี่ยนอัลกอริทึม |
| **สเปคต้องแก้ไหม** | **ไม่ต้อง** `SODA_BASELINE.md` §4 เขียนถูกอยู่แล้ว |
| **`soda.py` ต้องแก้ไหม** | **ไม่ต้อง** `end_phase()` ของอิทิ่มทำถูกอยู่แล้ว |
| **ต้องโหวตตาม §9.1 ไหม** | **ไม่ต้อง** เพราะไม่มีการเปลี่ยนกฎ มีแต่ทำให้โค้ดตรงกับกฎเดิม |
| **ต้องรัน batch ใหม่ไหม** | **ต้อง** ตาม §9.3 เพราะพฤติกรรมที่จำลองออกมาเปลี่ยน ผล batch v1 จึงใช้อ้างอิงไม่ได้ |
| **สถานะโค้ด** | **merge แล้ว 11 ก.ย. 2026** เทสผ่านครบ 9 ชุด |
| **อิทิ่มทานแล้ว** | ✅ ไม่ติดอะไร และ reproduce สาเหตุเองด้วย `test_queueclock.py` ฝั่ง `soda.py` |

---

## 0. สรุปให้สั้นที่สุด

สเปคข้อ §4 ของ `SODA_BASELINE.md` เขียนไว้ว่า

```
phase end   for each served head: lastCount>0 → queueStart = now, else ∞
```

**"for each served head"** คือทุกหัวที่เพิ่งได้ไฟเขียวไป แต่โค้ด Unity ทำแค่กับหัวที่ **หลุดออกจากกลุ่มไปเป็นสีแดง** เท่านั้น หัวที่อยู่ในกลุ่มต่อเนื่องข้ามเฟสจึงถือ `queueStart` เก่าค้างไม่จำกัดเวลา ระบบเลยคิดว่าหัวที่กำลังได้ไฟเขียวอยู่ตลอดคือหัวที่ "รอนานที่สุด" แล้วมันชนะทั้ง Q1 และ Q2 ซ้ำไปเรื่อย ๆ หัวที่อดจริงไม่มีวันได้คิว

ที่น่าสนใจคือ **baseline ทำนายอาการนี้ไว้แล้วเป๊ะ ๆ** ใน §4 บรรทัดถัดมา

> **The reset at phase end is load-bearing.** Without it, a head that never empties keeps its original `queueStart` forever and wins Q2 every time — every other head is locked out.

เราเขียนกฎถูก อธิบายเหตุผลถูก เทสก็มี แต่โค้ดทำไม่ครบ แล้วเทสที่มีอยู่ (`SodaTest` §1) ดันทดสอบเฉพาะเคสหัวที่หลุดไปเป็นแดง ซึ่งเป็นเคสที่โค้ดทำถูกอยู่แล้ว จึงผ่านมาตลอด

---

## 1. ผล batch v1 ที่รันเสร็จแล้ว (บริบท)

216 รอบ (4 scenario × 27 rep × 2 controller) จบเมื่อ 11 ก.ย. 14:23 น. ใช้เวลา 11 ชม. 31 นาที ที่ `simulationSpeed = 10`

เวลารอเฉลี่ยต่อคัน หน่วยวินาที จับคู่ด้วย seed เดียวกัน n = 27 ต่อ scenario

| Scenario | Fixed | Adaptive | ผลต่าง | t | Adaptive ชนะ |
|---|---|---|---|---|---|
| MorningPeak | 22.77 ± 0.97 | 14.11 ± 0.61 | +8.66 (38.0 %) | +38.2 | 27/27 |
| MiddayNormal | 22.66 ± 0.89 | 14.78 ± 0.75 | +7.88 (34.8 %) | +41.8 | 27/27 |
| ABACEvent | 93.35 ± 6.45 | 67.26 ± 14.13 | +26.10 (28.0 %) | +8.3 | 25/27 |
| EveningPeak | 25.40 ± 0.72 | 25.95 ± 1.57 | −0.55 (−2.2 %) | −1.8 **ไม่มีนัยสำคัญ** | 11/27 |
| **รวม** | **41.05** | **30.52** | **+25.6 %** | | |

⚠️ **EveningPeak ห้ามเขียนว่า "แพ้"** t = −1.8 เทียบค่าวิกฤต 2.056 ที่ df = 26 → ไม่ถึงนัยสำคัญ ที่ถูกคือ "ค่าเฉลี่ยไม่ต่างกันอย่างมีนัยสำคัญ" ถ้าเขียนว่าแพ้แล้วกรรมการคำนวณเอง จะโดนจับได้ (อิทิ่มเตือน 11 ก.ย.)

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

ตัวเลขยืนยันจากอีกทาง: Rep17 มี 99 เฟส แต่ log `StarvationEvents` ไป **137 ครั้ง** มากกว่าจำนวนเฟส (ฉบับแรกเขียน 145 ซึ่งผิด — 145 คือ *มัธยฐานของ EveningPeak ทั้ง 27 รอบ* คนละตัวกัน อิทิ่มจับได้ 11 ก.ย.) และตาม L13 ของ baseline ตัวนับนี้นับซ้ำสองครั้งต่อเหตุการณ์ (interrupt + override) ดังนั้น 137 ครั้ง ≈ **68 เหตุการณ์จริงจาก 99 เฟส** ยังสูงมากอยู่ดี เพราะ H_B เข้าเงื่อนไข "กำลังอดอยาก" แทบทุกครั้งที่ตัดสินใจ ทั้งที่มันคือหัวที่ได้ไฟเขียวอยู่ คอลัมน์ `StarvationEvents` ของ batch v1 จึงไม่ได้วัด starvation จริง

### หลักฐานหลัก — ใช้ทั้ง 27 รอบ ไม่ใช่ Rep17 รอบเดียว

Rep17 ที่ 344 วินาทีเป็นเคสเดียว กรรมการค้านได้ว่าเป็น outlier ดูทั้ง scenario แทน (อิทิ่มเสนอ)

| EveningPeak · `MaxWaitTime` ต่อรอบ | ต่ำสุด | มัธยฐาน | สูงสุด |
|---|---:|---:|---:|
| Adaptive | **126.5** | 188.3 | 344.4 |
| Fixed | 92.7 | 96.3 | **103.9** |

**รอบที่ดีที่สุดของ Adaptive (126.5 วิ) ยังแย่กว่ารอบที่แย่ที่สุดของ Fixed (103.9 วิ) — เกินเพดานของ Fixed ครบ 27 จาก 27 รอบ** อันนี้เถียงไม่ได้ ไม่ใช่ outlier

เลน CB รวมทั้ง 27 รอบ: mean 61.7 vs Fixed 37.7 · p95 148.0 vs 78.5 · max 344.4 vs 84.2 — Fixed ชนะขาดที่หาง เพราะรอบ 106 วินาทีการันตี CB เสมอ

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

### เทียบสามทาง — ใครถูกใครผิด

| แหล่ง | เขียนว่าอะไร | ถูกไหม |
|---|---|---|
| `SODA_BASELINE.md` §4 | `phase end — for each served head: lastCount>0 → queueStart = now, else ∞` | ✅ ถูก |
| `soda.py` ของอิทิ่ม `end_phase()` | `for h in self.green: self.queue_start[h] = t if self.count[h] > 0 else inf` วนทั้งกลุ่มที่เขียวอยู่ | ✅ ถูก |
| `AdaptiveController.cs` ของ Unity | `ResetQueueStartFor(dropping)` วนเฉพาะหัวที่กลายเป็นแดง | ❌ **ผิด** |

`soda.py` ถึงกับเขียน docstring อธิบายอาการนี้ไว้แล้วด้วย

```python
def end_phase(self, t: float) -> None:
    """Green for the current group is over. Re-arm their queue clocks.

    Without this, a head that is never empty (Bx runs at v/c 1.51) keeps its
    original queueStart for ever and wins every subsequent round.
    """
    for h in self.green:
        self.queue_start[h] = t if self.count[h] > 0 else float("inf")
```

แปลว่าอิทิ่มเข้าใจถูกตั้งแต่แรก และ Python reference ไม่ต้องแก้อะไรเลยสำหรับเรื่องนี้ ผิดอยู่ที่ Unity ฝั่งเดียว

**ทำไม `verify_traces.py` ถึงไม่จับได้** เพราะ trace S1–S5 ที่เราเขียนไว้ไม่มีเคสที่หัวเดียวกันอยู่ในกลุ่มเขียวติดกันหลายเฟสโดยไม่เคยว่าง ต้องเป็นสภาพอิ่มตัวยาว ๆ แบบ EveningPeak ถึงจะโผล่

### ทำไม Midday กับ Morning ไม่เป็น

`ResetQueueStartFor` ตั้ง `queueStart = Infinity` เมื่อเลนว่าง และ `Snapshot()` ตั้งค่าใหม่เมื่อรถคันแรกมาถึง ในสภาพที่ demand ไม่อิ่มตัว หัวไฟจะว่างเป็นระยะ นาฬิกาจึงถูกล้างโดยอัตโนมัติ อาการนี้โผล่เฉพาะตอนที่หัวไฟ**มีรถค้างตลอดเวลาไม่เคยว่าง** ซึ่งคือสภาพของ H_B ใน EveningPeak พอดี

นี่อธิบายว่าทำไม MiddayNormal ถึงชนะ Fixed ทั้งค่าเฉลี่ยและหาง (max 85.8 เทียบ 94.2) ในขณะที่ EveningPeak แพ้ — และทำไมเราไม่เห็นมันตอนเทสด้วยมือ

---

## 4. สิ่งที่ต้องแก้ (ฝั่ง Unity เท่านั้น)

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

หลังแก้ โค้ดจะตรงกับ §4 ของ baseline และตรงกับ `end_phase()` ของ `soda.py` พอดี ไม่มีกฎใหม่เกิดขึ้น

**ช่องโหว่ของเทสเดิมที่ต้องบันทึกไว้** `SodaTest` §1 ทดสอบว่า "หัวที่ไม่เคยว่างต้องไม่ล็อกเลนอื่น" จริง แต่สร้างสถานการณ์ด้วยการให้หัวนั้นหลุดออกจากกลุ่มไปเป็นแดงทุกเฟส ซึ่งเป็นเส้นทางที่โค้ดทำถูกอยู่แล้ว เคสที่หัวเดิม **อยู่ในกลุ่มต่อเนื่องโดยไม่เคยเป็นแดง** ไม่เคยถูกทดสอบ `QueueClockTest` มาปิดช่องนี้

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

## 6. อีกสองเรื่องที่เจอ เป็น measurement-only

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

## 7b. อิทิ่มทานแล้ว — 11 ก.ย. 2026

ยืนยันสาเหตุตรงกันสามทาง: อ่านโค้ดตรงกัน · สเปค §4 อ่านได้ทางเดียว · และ **พิสูจน์ด้วยการรัน** เขาเขียน `test_queueclock.py` ให้ `soda.py` จำลองสภาพอิ่มตัวถาวร ทุกหัวมีรถค้าง 6 คัน ไม่มีเลนไหนว่างเลย 10 เฟส

```
leaders : A Bx CB A Bx CB A Bx CB A
served  : A 4, B 7, Bx 3, CB 3
```

B คือหัวที่อยู่ในกลุ่มข้ามเฟส เทียบได้กับ H_B ของฝั่ง Unity แต่ `queueStart` ถูกรีเซ็ตเป็น 30.0 ตอนจบเฟส ซึ่งคือบรรทัดที่ Unity ขาดพอดี ผลคือไม่มีการล็อกเอาต์ ทุกหัวได้คิว

เขารันตัวเลขของเราใหม่ทั้งหมด ตรงทุกช่อง 38.0 / 34.8 / 28.0 / −2.2 และตรวจ C2 ได้ 107 แถวตรง 0 แถวต่าง

สามจุดที่เขาให้แก้ก่อนเขียนลงรายงาน แก้ในเอกสารฉบับนี้แล้วทั้งหมด: เลข `StarvationEvents` ของ Rep17 (137 ไม่ใช่ 145) · คำว่า "แพ้" ของ EveningPeak (ต้องเป็น "ไม่ต่างอย่างมีนัยสำคัญ") · และเปลี่ยนหลักฐานหลักจาก Rep17 รอบเดียวเป็นทั้ง 27 รอบ

**สิ่งที่เขาบอกให้จับตาหลังแก้** Q3 เรียง candidate ตาม `queueStart` พอรีเซ็ตหัวที่ต่อเนื่องแล้ว หัวพวกนั้นจะกลายเป็น "ใหม่สุด" เลยถูกลองท้ายสุดตอนสร้างกลุ่ม อาจทำให้กลุ่มสลับบ่อยขึ้น = จ่าย clearance 5 วินาทีถี่ขึ้น ให้ดู **จำนวนเฟสต่อรอบ** กับอัตราส่วน **`GreenUsed / GreenGiven`** เทียบ v1 กับ v2 ถ้าจำนวนเฟสเพิ่มเยอะ นั่นคือที่มาของ throughput ที่หายไป — บันทึกเป็น §8.9 ใน baseline แล้ว

---

## 8. สถานะของงานฝั่งอิทิ่ม (§8.2, §8.5 ปิดแล้ว)

**§8.2 ปิดแล้ว** — counting window ลบแล้ว · gap-out นับเป็น "จำนวนครั้งที่อ่านได้ศูนย์" ไม่ใช่วินาที ยิงตอนอ่านศูนย์ครั้งที่ 2 = 4 วิหลังอ่านค่าไม่ศูนย์ครั้งสุดท้าย · COLLECT ใส่แล้วพร้อม early exit ทั้งสองข้อ · starvation tie ตรงกับ Q2 แล้ว · S1–S5 ตรงทุกบรรทัด รวมเวลาที่ green เปิดและจบ ไม่ใช่แค่ leader/group/G

เขายังแก้อีกจุดที่ไม่อยู่ในลิสต์ D1–D3 ของผม: **§4.4 บอกว่า G ต้องอ่าน count ใหม่ตอน green เปิด** แต่ `soda.py` เดิมคิดจาก count ตอนตัดสินใจ ซึ่งเก่ากว่า 5 วินาที (เหลือง 3 + all-red 2) ตอนพีคคือรถ 2–3 คัน — **ผมเช็กฝั่ง C# แล้ว ทำถูกอยู่แล้ว** `CalculateGreenDuration` ถูกเรียกหลัง `SetGroupState(…Green)` และ `DemandOf` อ่าน ROI สด ๆ ตอนนั้น ไม่ได้ใช้ `lastCount` เก่า

**§8.5 ปิดแล้ว** — headway 39 ตัวอย่าง `DJI_20260714084627_0005_D.MP4` อังคาร 14 ก.ค. 2026 หน้าต่าง 08:46–09:06 · detector 298/303 `DJI_20260713084049_0002_D.MP4` จันทร์ 13 ก.ค. 2026 หน้าต่าง 08:40–09:05 · ชื่อไฟล์ DJI เข้ารหัส YYYYMMDDHHMMSS และ `tally_*.json` เขียนวันไว้ในฟิลด์ `source` ไม่ต้องพึ่งความจำใคร → ใส่ใน §4.4 และ §7 L5 ของ baseline แล้ว

**ที่ยังค้าง (§8.10)** ไฟล์ `soda.py`, `test_soda.py`, `test_queueclock.py` ฉบับล่าสุดของอิทิ่มยังไม่ได้อยู่ในรีโป คู่ C#/Python ของเคส C1 จึงมีแค่ครึ่งเดียว `Tests/verify_traces.py` ลบบรรทัด `waited_once` / `waiting_until` ออกแล้วตามที่เขาบอก แต่ยังไม่ได้รันกับ `soda.py` ตัวใหม่

---

## 9. สถานะปัจจุบัน

C1 + C2 + C3 merge เข้า `AdaptiveController.cs` แล้ว 11 ก.ย. · `SODA_BASELINE.md` ขึ้นเป็น v1.5 ปิด §8.2 §8.5 §8.7 §8.8 เปิด §8.9 §8.10 · ขั้นต่อไปคือรัน batch v2 216 รอบ

ผล batch v1 ยังเก็บไว้ครบทุกไฟล์ใน `SimulationResults/` เป็นครึ่ง "before" ของการเทียบ before/after ซึ่งมีค่าสำหรับรายงาน เพราะแสดงให้เห็นว่ากลไก anti-starvation มีผลจริงแค่ไหนเมื่อมันทำงานกับตอนมันไม่ทำงาน — แต่ห้ามอ้างเป็นผลการทดลอง

---

## ภาคผนวก — ไฟล์ที่เกี่ยวข้อง

| ไฟล์ | ใช้ทำอะไร |
|---|---|
| `SimulationResults/summary_all_runs.csv` | 216 แถว ตัวเลขหลักทั้งหมด |
| `SimulationResults/phases_Adaptive_EveningPeak_Rep17_*.csv` | log ที่ยกมาในข้อ 2 |
| `SimulationResults/detailed_*_EveningPeak_Rep*.csv` | เวลารอรายคัน ใช้คำนวณ p95/max รายเลน |
| `SimulationResults/batch_summary_20260911_142347.txt` | สรุปที่ ScenarioManager เขียนเอง |
| `Tests/QueueClockTest.cs` | เทสใหม่ ยังไม่ commit |
