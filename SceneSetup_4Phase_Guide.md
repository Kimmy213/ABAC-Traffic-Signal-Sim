# คู่มือตั้ง Scene 4-Phase — ABAC Traffic Simulation

**Unity 6 (6000.5.4f1), Built-In RP** · อัปเดต 20 สิงหาคม 2026

> **ทางลัด:** ถ้าไม่อยากวางมือทีละชิ้น ข้ามไปข้อ 3 → กด `ABAC ▸ Build 4-Phase Scene` จบใน 1 คลิก
> ข้อ 4-6 คือรายละเอียดว่า builder ทำอะไรให้บ้าง (ไว้ตรวจ/ปรับเอง)

---

## 1. ผังแยกจริง (ยืนยันจากพิกัดในซีนแล้ว)

⚠️ **แก้จาก progress doc เดิม:** ทางเข้า ABAC อยู่ทาง **เหนือ (+Z)** ไม่ใช่ทางใต้
ผมอ่านพิกัดจริงใน `SampleScene.unity` มาแล้ว — `Road_ABAC` วางจาก z=0 ถึง z=200 และ `SpawnPoint_C` อยู่ที่ z=200
คำว่า "ตะวันออก/ตะวันตก" ใน doc เดิมจึงสลับกัน แต่ **ชื่อเลนและการมี/ไม่มีไฟถูกต้องทั้งหมด**

```
                        ทิศเหนือ (+Z)
                            ↑
                     ┌─────────────┐
                     │    ABAC     │
                     └──────┬──────┘
              เข้า ↑↑        │        ↓↓ ออก
           x=-4.0  x=-1.8    │   x=+1.5  x=+3.8
                    ╲   ╲    │    ╱   ╱
  ═══════════════════════════╪═══════════════════════  z=+6.5  Ax →
  ═══════════════════════════╪═══════════════════════  z=+3.0  A  →  (ไปตะวันออก +X)
  ───────────────────────────┼───────────────────────  z=+0.88 centerline
  ═══════════════════════════╪═══════════════════════  z=−1.2  Bx ←
  ═══════════════════════════╪═══════════════════════  z=−4.5  B  ←  (ไปตะวันตก −X)
```

ประเทศไทยขับ **ชิดซ้าย** → A (มุ่งตะวันออก) อยู่ครึ่งเหนือ, B (มุ่งตะวันตก) อยู่ครึ่งใต้

| เลน | การเคลื่อนที่ | ไฟ? | เหตุผล |
|---|---|---|---|
| **A** | มุ่งตะวันออก (+X) วิ่งตรง | ✅ | |
| **Ax** | แยกจาก A **เลี้ยวซ้าย**เข้า ABAC | ❌ ฟรี | ABAC อยู่ทางซ้ายของ A — เลี้ยวเข้าเลนใกล้ตัว ไม่ตัดใคร |
| **B** | มุ่งตะวันตก (−X) วิ่งตรง | ✅ | |
| **Bx** | แยกจาก B **เลี้ยวขวา**เข้า ABAC | ✅ | ต้องตัดผ่านกระแส A |
| **CB** | ออกจาก ABAC **เลี้ยวขวา** ไป merge เลน B | ✅ | ต้องข้ามเลน A ก่อนถึงเลน B |
| **CA** | ออกจาก ABAC **เลี้ยวซ้าย** ไป merge เลน A | ❌ ฟรี | merge เลนใกล้ตัว ไม่ต้องข้ามใคร |

### ตาราง conflict (ห้ามเขียวพร้อมกัน)

|  | A | B | Bx | CB |
|---|---|---|---|---|
| **A** | — | ✅ ได้ | ❌ | ❌ |
| **B** | ✅ ได้ | — | ✅ ได้ | ❌ |
| **Bx** | ❌ | ✅ ได้ | — | ❌ |
| **CB** | ❌ | ❌ | ❌ | — |

**ประเด็นสำหรับ defence:** ระบบมี **4 ไฟ แต่ 3 เฟส** เพราะ A กับ B เป็นกระแสตรงสวนทาง ไม่ตัดกัน ควรได้เขียวพร้อมกันตามหลัก HCM/ITE
ถ้าแยกเป็น 4 เฟสจะเสียเวลา inter-green เกินจำเป็น และทำให้ Fixed baseline อ่อนกว่าความจริง = unfair comparison

---

## 2. สิ่งที่เปลี่ยนจากของเดิม

| เดิม | ใหม่ |
|---|---|
| Controller ถือ `lightA/lightB/lightC` ตรง ๆ | อ่านทุกอย่างจาก **`IntersectionConfig`** ตัวเดียว |
| 3 เลน A/B/C | 6 เลน **A, Ax, B, Bx, CB, CA** (มีไฟ 4) |
| 1 เฟส = 1 เลน | 1 เฟส = **หลายเลนพร้อมกันได้** ถ้าไม่ conflict |
| ไม่มีกฎห้ามชน | **conflict matrix** บังคับ ตั้งผิดขึ้น error ทันที |
| รถวิ่งตรงอย่างเดียว | **เลี้ยวตาม waypoint** ได้ |
| ถนน 8 ม. (2 เลน) | ถนนกว้าง 18 ม. (4 เลน) + ถนน ABAC 14 ม. (4 เลน) |

**ไฟล์ใหม่:** `Assets/Scripts/IntersectionConfig.cs`, `Assets/Editor/ABACSceneBuilder.cs`, `Analysis/analyze_results.py`
**เขียนทับ:** `FixedController.cs`, `AdaptiveController.cs`, `ScenarioManager.cs`, `CarAgent.cs`, `CarSpawner.cs`, `ROIZone.cs`, `TrafficLight.cs`, `DataLogger.cs`

⚠️ เปิด Unity ครั้งแรกหลังเปลี่ยนไฟล์ **reference ใน Inspector ของ controller เดิมจะหาย** (field เปลี่ยนชื่อ) — เป็นเรื่องปกติ builder ต่อให้ใหม่หมด

---

## 3. ทางลัด — ให้ builder สร้างให้ (แนะนำ)

1. **commit git ก่อน** (สำคัญ — builder แก้ซีนจริง)
2. เปิด Unity รอ compile จนไม่มี error แดง
3. เปิด `SampleScene`
4. เมนูบนสุด → **`ABAC` ▸ `Build 4-Phase Scene`** → กด "สร้างเลย"
5. ดู Console ต้องขึ้น
   ```
   [IntersectionConfig] ✓ ผ่านทั้งหมด — 6 เลน (4 เลนมีไฟ), 4 conflict pairs
   [ABACSceneBuilder] สร้างซีนเสร็จแล้ว
   ```
6. **Ctrl+S** บันทึกซีน
7. เมนู → **`ABAC` ▸ `Validate Scene`** ตรวจซ้ำอีกรอบ

**builder ทำอะไรให้บ้าง:**

- ขยายถนน E-W เป็น 18 ม. และถนน ABAC เป็น 14 ม. + ขยับป้อมยามออกนอกถนน
- สร้าง/ย้าย `Light_A/B/Bx/CB` (เปลี่ยนชื่อ `Light_C` → `Light_CB` อัตโนมัติ)
- สร้าง `StopLine_A/B/Bx/CB`
- สร้าง ROI ครบ 6 เลน ยาว 100 ม. จ่อเส้นหยุด พร้อมตั้ง `guiSlot` ไม่ให้ทับกัน
- **ลบ collider ตัวที่ไม่ใช่ trigger ออกจาก ROI** (ของเดิมมี 2 ตัวซ้อนกัน)
- สร้าง waypoint 3 จุดต่อเลนเลี้ยว (Ax, Bx, CB, CA)
- สร้าง/ต่อ spawner ครบ 6 เลน — หา spawner เดิมจาก `laneName` ไม่ใช่ชื่อ object (ของเดิมชื่อมีอักขระไทยปนอยู่)
- ต่อ `IntersectionConfig` + `FixedController` (preset 120s) + `AdaptiveController` + `ScenarioManager` (preset 5 scenarios)

กดซ้ำได้ ไม่สร้างของซ้ำ และ **Ctrl+Z ย้อนได้**

---

## 4. พิกัดที่ builder ใช้ (ไว้ปรับเอง)

### เลนบนถนนหลัก (แกน Z)
| เลน | z | spawn | rotationY |
|---|---|---|---|
| Ax | +6.5 | (−200, 0.5, 6.5) | 90 |
| A | +3.0 | (−200, 0.5, 3.0) | 90 |
| Bx | −1.2 | (+200, 0.5, −1.2) | −90 |
| B | −4.5 | (+200, 0.5, −4.5) | −90 |

### เลนบนถนน ABAC (แกน X)
| เลน | x | spawn | rotationY |
|---|---|---|---|
| ขาเข้าจาก Ax | −4.0 | — | — |
| ขาเข้าจาก Bx | −1.8 | — | — |
| CB (ออก) | +1.5 | (1.5, 0.5, 200) | 180 |
| CA (ออก) | +3.8 | (3.8, 0.5, 200) | 180 |

### เส้นหยุด
`StopLine_A` x=−10.5 · `StopLine_B` / `StopLine_Bx` x=+9.5 · `StopLine_CB` z=+10.9

### Waypoint (y = 0.5 ทุกจุด)
| เลน | WP1 | WP2 | WP3 |
|---|---|---|---|
| Ax | (−8.0, 6.5) | (−4.0, 14.0) | (−4.0, 90) |
| Bx | (2.0, 0.0) | (−1.8, 14.0) | (−1.8, 90) |
| CB | (1.5, 4.0) | (−6.0, −4.5) | (−90, −4.5) |
| CA | (3.8, 14.0) | (9.0, 3.0) | (90, 3.0) |

**หลักการวาง waypoint:**
WP1 ต้องอยู่ **ตรงหน้ารถพอดี** (ไม่ให้รถเริ่มเบี่ยงตั้งแต่ spawn) → WP2 คือจุดหักเลี้ยว → WP3 คือปลายทางไกล ๆ
รถจะวิ่งตรงจนผ่านเส้นหยุดก่อนเสมอ แล้วค่อยเริ่มเลี้ยว (เลนที่ไม่มีเส้นหยุดคือ Ax/CA จะเริ่มเลี้ยวตั้งแต่ spawn จึงต้องมี WP1 ตรงหน้า)

---

## 5. กฎเหล็ก 2 ข้อของเลนฟรี (Ax, CA)

1. **ห้ามใส่ `Stop Line Target`** — รถจะเบรกจอดแล้วไม่ไปต่อตลอดกาล (เพราะไม่มีไฟให้เขียว)
2. **ห้ามใส่ `Assigned Light`**

`ABAC ▸ Validate Scene` เช็คสองข้อนี้ให้อยู่แล้ว ถ้าผิดจะขึ้น error แดง

> เลนฟรียังต้องมี **ROI** อยู่ เพราะเราต้องวัด waiting time เพื่อพิสูจน์ว่าค่าใกล้ 0 จริง — เป็นหลักฐาน sanity check ที่ดีสำหรับรายงาน

---

## 6. ตั้งค่า Controller

### FixedController — preset 120s/cycle

| เฟส | เลนเขียว | Green |
|---|---|---|
| P1 MainThrough | A + B | 45s |
| P2 EastRightTurn | B + Bx | 30s |
| P3 ABACExit | CB | 30s |

Yellow 3s + All-Red 2s × 3 = 15s → **รวม 120s พอดี** (ตรวจได้ด้วย context menu `Print Cycle Length`)

Timeline จริงที่ verify มาแล้ว:
```
t=  0s +45s   A=G B=G Bx=R CB=R
t= 45s + 3s   A=Y B=G Bx=R CB=R   ← A ขึ้นเหลือง แต่ B เขียวต่อ ไม่กระพริบ
t= 48s + 2s   A=R B=G Bx=R CB=R
t= 50s +30s   A=R B=G Bx=G CB=R   ← Bx เข้าร่วมหลัง all-red
t= 80s + 3s   A=R B=Y Bx=Y CB=R
t= 83s + 2s   A=R B=R Bx=R CB=R
t= 85s +30s   A=R B=R Bx=R CB=G
t=115s + 3s   A=R B=R Bx=R CB=Y
t=118s + 2s   A=R B=R Bx=R CB=R
t=120s        วนกลับ P1
```

### AdaptiveController — ค่าที่แนะนำ

| ช่อง | ค่า | หมายเหตุ |
|---|---|---|
| G Min / G Max | 8 / 45 | เท่าเดิม |
| Startup Lost Time | 3 | HCM (⚠️ ของเดิมในซีนตั้งเป็น 1 — builder ไม่แตะ ให้แก้เอง) |
| Saturation Headway | 2.5 | HCM |
| Max Continuous Green | 90 | ⭐ ใหม่ กันเลนเดิมเขียวยาวไม่จบ |
| Enable Concurrent Phases | ✅ | ⭐ ใหม่ ให้รวม A+B ได้ |
| Counting Window | 4 | (⚠️ ของเดิมตั้ง 8) |
| Gap Out Delay | 3 | |
| Max Wait Time | 45 | |
| Min Green Before Interrupt | 20 | |

### ScenarioManager — ค่ารันจริง

`Run Duration` = **1800** · `Repetitions Per Scenario` = **27** · `Warmup Duration` = **120** · `Simulation Speed` = **10** (ดูข้อ 8)

| Scenario | A | Ax | B | Bx | CB | CA |
|---|---|---|---|---|---|---|
| MorningPeak | high | **high** | medium | **high** | veryLow | veryLow |
| EveningPeak | medium | low | high | low | **high** | **high** |
| MiddayNormal | low | low | low | low | low | low |
| ABACEvent | medium | **veryHigh** | medium | **veryHigh** | high | high |
| Night | veryLow | ×6 ทุกเลน | | | | |

---

## 7. Checklist ทดสอบมือ (ทำก่อนรันจริงเสมอ)

ตั้ง `Repetitions Per Scenario = 1`, `Run Duration = 60`, `Warmup Duration = 10`, `Simulation Speed = 1` แล้วกด Play

- [ ] Console ไม่มี error แดง
- [ ] `ABAC ▸ Validate Scene` ผ่านหมด
- [ ] ไฟ A กับ B เขียว**พร้อมกัน**ได้ (ยืนยัน concurrent phase)
- [ ] ไฟ CB **ไม่เคย**เขียวพร้อม A, B หรือ Bx เลยสักครั้ง ⭐ สำคัญสุด
- [ ] ไฟ Bx **ไม่เคย**เขียวพร้อม A
- [ ] รถ Ax เลี้ยวซ้ายเข้า ABAC จริง ไม่วิ่งทะลุ
- [ ] รถ Bx รอไฟแล้วเลี้ยวขวาเข้า ABAC จริง
- [ ] รถ CB/CA ออกจาก ABAC เข้าถนนถูกฝั่ง (CB→เลน B, CA→เลน A)
- [ ] รถไม่วิ่งวนเป็นวงกลม (ถ้าวน = waypoint ไกลเกิน/`Steer Rate` ต่ำเกิน)
- [ ] กล่อง ROI 6 กล่องขึ้นครบ ไม่ทับกัน
- [ ] waiting time ของ **Ax และ CA ≈ 0**
- [ ] สลับ Fixed ↔ Adaptive แล้วไม่มีไฟค้างเขียว 2 กลุ่มพร้อมกัน
- [ ] CSV ออกใน `SimulationResults/` ครบ 2 ไฟล์

---

## 8. ก่อนปล่อยรัน 270 runs

### 8.1 ลบ CSV เก่าก่อน ⭐
`summary_all_runs.csv` เขียนแบบ append — ข้อมูลทดสอบเก่าจะปนเข้าไปด้วย

> ตอนนี้ `DataLogger` ตรวจหัวตารางให้แล้ว ถ้ารูปแบบไม่ตรงจะย้ายไฟล์เก่าไปเป็น `summary_all_runs_OLD_<เวลา>.csv` อัตโนมัติ
> (ไฟล์เดิมของโปรเจคนี้มีปัญหานี้อยู่จริง — มีทั้งแถว 7 และ 8 คอลัมน์ปนกัน 18/22 แถว)

### 8.2 Warm-up คืออะไร (ควรเขียนในรายงาน)
เดิมทุก run เริ่มจากถนนว่าง → รถ 10-20 คันแรกไม่ต้องรอไฟเลย ทำให้ค่าเฉลี่ยต่ำกว่าจริงและเอนเอียงเข้าข้าง Fixed
ตอนนี้ปล่อยรถวิ่ง 120 วินาทีก่อน แล้วล้าง log เริ่มนับใหม่ → ได้ข้อมูล **steady-state** ล้วน (วิธีมาตรฐานของ discrete-event simulation)

### 8.3 Validate timeScale ⭐ กรรมการชอบถาม
เวลาจำลองรวม = 270 × (1800+120) = **144 ชม.จำลอง**

| Time Scale | เวลาจริง (ถ้าเครื่องตามทัน) |
|---|---|
| 10x | ~14.4 ชม. |
| 20x | ~7.2 ชม. |
| 50x | ~2.9 ชม. |

ที่ timeScale สูง `Time.deltaTime` ต่อเฟรมใหญ่ขึ้น → การเบรก/เร่งใน `CarAgent` คลาดเคลื่อน

**วิธีตรวจ:** รัน scenario เดียวกัน seed เดียวกัน ที่ 1x / 10x / 20x แล้วเทียบ `AvgWaitTime`
ต่างเกิน ~5% = เร็วเกินไป ให้ลดลง — ทำครั้งเดียวแล้วบันทึกไว้ใส่รายงาน

### 8.4 อื่น ๆ
- ปิด Scene view ใน Unity เหลือแต่ Game view (เร็วขึ้นเยอะ)
- ตั้งเครื่องไม่ให้ sleep แล้วรันข้ามคืน
- commit git ก่อนกดรัน

---

## 9. วิเคราะห์ผล

```bash
pip install pandas scipy matplotlib
cd Analysis
python analyze_results.py            # อ่านจาก ../SimulationResults อัตโนมัติ
```

ได้ไฟล์ใน `SimulationResults/analysis_output/`:

| ไฟล์ | เนื้อหา |
|---|---|
| `results_summary.txt` | ตัวเลขทั้งหมด + **ประโยคสำเร็จรูปสำหรับ abstract** |
| `paired_table.csv` | ตารางรายคู่ Fixed vs Adaptive |
| `01_overall_comparison.png` | เทียบภาพรวม + boxplot |
| `02_by_scenario.png` | แยก scenario พร้อมดาวนัยสำคัญ |
| `03_by_lane.png` | แยกรายเลน (ตรวจว่า Ax/CA ใกล้ 0) |
| `04_paired_differences.png` | การกระจายผลต่างรายคู่ |
| `05_throughput.png` | จำนวนรถที่ระบายได้ |

**สคริปต์ทำให้ครบ:** paired t-test, Wilcoxon (เผื่อข้อมูลไม่ปกติ), Cohen's d, 95% CI, Shapiro-Wilk ตรวจสมมติฐาน, แยกราย scenario และรายเลน

⚠️ **เรื่อง starvation ที่ต้องระวังตอนเขียนรายงาน:**
`StarvationEvents` ในระบบนี้นับ "จำนวนครั้งที่กลไกกันอดทำงาน" ไม่ใช่ "จำนวนครั้งที่เลนโดนอด"
ค่ามากกว่า 0 = กลไกทำหน้าที่ของมัน ไม่ใช่ระบบล้มเหลว
ถ้าจะเคลม *zero starvation* ให้ดูจาก `MaxWaitTime` ว่าไม่มี run ไหนเกิน threshold แทน (สคริปต์รายงานให้แล้ว)

---

## 10. แก้ปัญหา

| อาการ | สาเหตุ |
|---|---|
| ROI นับรถเป็น 0 ตลอด | Box Collider ไม่ได้ติ๊ก `Is Trigger` / `laneName` ไม่ตรงกันระหว่าง ROI กับ Spawner |
| รถวิ่งทะลุไม่เลี้ยว | ไม่ได้ใส่ `Path Waypoints` หรือเรียงลำดับผิด |
| รถวิ่งวนเป็นวงกลม | `Steer Rate` ต่ำเกิน หรือ WP วางไกลจากแนวรถเกินไป |
| **รถเลนฟรีจอดแช่ไม่ไปไหน** | ใส่ `Stop Line Target` ให้เลน Ax/CA (ห้ามใส่ — ดูข้อ 5) |
| รถเริ่มเบี่ยงตั้งแต่ spawn | WP1 ของเลนฟรีไม่ได้อยู่ตรงหน้ารถ |
| ไฟเขียวชนกัน | conflict pair ตกไป — กด `Validate Configuration` |
| `NullReferenceException` ตอน Play | `Config` ในตัว controller ยังว่าง หรือมีเลนที่ยังไม่ได้ลาก ROI |
| ไฟค้างเขียวไม่เปลี่ยน | ไม่มีรถในเลนอื่นเลย (rest-in-green = พฤติกรรมที่ถูกต้อง) |
| Unity ค้างตอนกด Play | (บั๊ก recursion แก้แล้วในโค้ดชุดใหม่) ถ้ายังเจอ เช็คว่ามี `IntersectionConfig` ซ้ำมากกว่า 1 ตัวไหม |
| pandas อ่าน CSV ไม่ออก | ไฟล์มีหลายสคีมาปนกัน — สคริปต์ข้ามให้เองแล้ว แต่ควรลบไฟล์เก่าทิ้ง |
| กราฟขึ้นสี่เหลี่ยมแทนตัวอักษร | ข้อความบนกราฟเป็นอังกฤษหมดแล้ว ถ้ายังเจอแปลว่าแก้ข้อความเป็นไทยเอง |

---

## 11. ลำดับงานถัดไป

1. ✅ โค้ด 4-phase + builder + สคริปต์วิเคราะห์ — เสร็จแล้ว
2. ⬜ กด `ABAC ▸ Build 4-Phase Scene` แล้วผ่าน checklist ข้อ 7
3. ⬜ Validate timeScale (ข้อ 8.3) บันทึกผลไว้ใส่รายงาน
4. ⬜ ลบ CSV เก่า → ปล่อยรัน 270 runs ข้ามคืน
5. ⬜ `python analyze_results.py` → เอากราฟกับตัวเลขใส่รายงาน
6. ⬜ (ถ้ามีเวลา) ปิด `Enable Concurrent Phases` รันเทียบ → ได้ ablation study 3 ระบบ
7. ⬜ Exhibition 25 ก.ย. · Defence 14-16 ต.ค.
