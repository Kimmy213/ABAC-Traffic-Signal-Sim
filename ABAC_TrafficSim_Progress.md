# ABAC Traffic Signal Simulation — Project Status

**อัปเดตล่าสุด:** 20 สิงหาคม 2026 (รอบที่ 2 — หลังขยายเป็น 4-phase)
**Repository:** github.com/Kimmy213/ABAC-Traffic-Signal-Sim (Private)
**Engine:** Unity 6 (6000.5.4f1), Built-In Render Pipeline

---

## 1. ภาพรวมโปรเจค

ระบบจำลอง (digital twin) ของทางแยกรูปตัว T หน้าทางเข้า ABAC วิทยาเขตสุวรรณภูมิ บนถนนบางนา-ตราด (สป.5003) เพื่อเปรียบเทียบประสิทธิภาพระหว่าง:

- **Fixed-cycle controller** — ไฟจราจรแบบวนตายตัว 120s/cycle (baseline)
- **Adaptive controller** — FCFS + สูตร HCM-based clearance time + concurrent phase ปรับเวลาไฟเขียวตามดีมานด์จริง

เป้าหมาย: รัน **135 scenarios × 2 controllers = 270 runs** เก็บข้อมูล waiting time แล้วเปรียบเทียบด้วย paired t-test

---

## 2. สถาปัตยกรรมระบบ (หลัง refactor)

### 2.1 ผังแยกเต็มระบบ — 6 เลน, ไฟ 4 ดวง, 3 เฟส

⚠️ **แก้จาก doc รอบก่อน:** ทางเข้า ABAC อยู่ทาง **เหนือ (+Z)** ไม่ใช่ทางใต้ — ยืนยันจากพิกัดจริงใน `SampleScene.unity`
(`Road_ABAC` วางจาก z=0 ถึง z=200, `SpawnPoint_C` ที่ z=200) คำว่าตะวันออก/ตะวันตกใน doc เดิมจึงสลับกัน
แต่ **ชื่อเลนและการมี/ไม่มีไฟถูกต้องทั้งหมด** conflict matrix ไม่ต้องแก้

| เลน | การเคลื่อนที่ | มีไฟ? | ตัดกระแสใคร |
|---|---|---|---|
| **A** | มุ่งตะวันออก (+X) วิ่งตรง — อยู่ครึ่งเหนือ | ✅ | — |
| **Ax** | แยกจาก A เลี้ยวซ้ายเข้า ABAC | ❌ ฟรี | ไม่ตัดใคร (ABAC อยู่ทางซ้ายของ A) |
| **B** | มุ่งตะวันตก (−X) วิ่งตรง — อยู่ครึ่งใต้ | ✅ | — |
| **Bx** | แยกจาก B เลี้ยวขวาเข้า ABAC | ✅ | ตัด A |
| **CB** | ออกจาก ABAC เลี้ยวขวา ไป merge เลน B | ✅ | ตัด A และ B |
| **CA** | ออกจาก ABAC เลี้ยวซ้าย ไป merge เลน A | ❌ ฟรี | ไม่ต้องข้ามใคร |

**Conflict matrix (ห้ามเขียวพร้อมกัน):** A×Bx, A×CB, B×CB, Bx×CB
**เขียวพร้อมกันได้:** A+B (กระแสตรงสวนทาง), B+Bx (ขาเดียวกัน, protected right turn)

> **ประเด็นสำคัญสำหรับ defence:** ระบบมี "4 ไฟ แต่ 3 เฟส" ไม่ใช่ 4 เฟสแยกกัน เพราะกระแสที่ไม่ตัดกันควรได้เขียวพร้อมกันตามหลัก HCM/ITE — การแยก 4 เฟสจะเสียเวลาเปล่าและทำให้ baseline อ่อนเกินจริง (unfair comparison)

### 2.2 Fixed phase plan (120s/cycle ตาม DOH Thailand 2019)

| เฟส | เลนที่เขียว | Green | หมายเหตุ |
|---|---|---|---|
| P1 MainThrough | A + B | 45s | |
| P2 EastRightTurn | B + Bx | 30s | B เขียวต่อเนื่องไม่ขึ้นเหลือง |
| P3 ABACExit | CB | 30s | |

Yellow 3s + All-Red 2s × 3 = 15s → **รวม 120s พอดี**

Timeline จริงที่ verify แล้ว:
```
t=  0s +45s   A=G B=G Bx=R CB=R
t= 45s + 3s   A=Y B=G Bx=R CB=R    ← A ขึ้นเหลือง B ยังเขียวต่อ
t= 48s + 2s   A=R B=G Bx=R CB=R
t= 50s +30s   A=R B=G Bx=G CB=R    ← Bx เข้าร่วมหลัง all-red
t= 80s + 3s   A=R B=Y Bx=Y CB=R
t= 83s + 2s   A=R B=R Bx=R CB=R
t= 85s +30s   A=R B=R Bx=R CB=G
t=115s + 3s   A=R B=R Bx=R CB=Y
t=118s + 2s   A=R B=R Bx=R CB=R
t=120s        (วนกลับ P1)
```

---

## 3. สิ่งที่ทำเสร็จแล้ว

### 3.1 Scripts (9 ไฟล์)

| ไฟล์ | หน้าที่ | สถานะ |
|---|---|---|
| `IntersectionConfig.cs` ⭐ **ใหม่** | ศูนย์กลางข้อมูลแยก: รายชื่อเลนทั้งหมด + conflict matrix + validation (`Validate Configuration`) — controller ทุกตัวอ่านจากที่นี่ที่เดียว เพิ่ม/ลดเลนได้โดยไม่แก้โค้ด | ✅ |
| `CarAgent.cs` | พฤติกรรมรถ: 3 driver profile, เบรกฟิสิกส์จริง, car-following, Dilemma Zone + ⭐ **waypoint steering** สำหรับเลนเลี้ยว | ✅ |
| `TrafficLight.cs` | เก็บ state ไฟ, แจ้งรถที่ลงทะเบียนทันที + ⭐ auto-cleanup รถที่ถูกลบไปแล้ว | ✅ |
| `FixedController.cs` | ⭐ **rewrite** — phase list ที่แต่ละเฟสเปิดได้หลายเลน, carry-over green (เลนที่เขียวต่อไม่ต้องขึ้นเหลือง), ตรวจ conflict ในเฟสอัตโนมัติ | ✅ |
| `AdaptiveController.cs` | ⭐ **rewrite** — FCFS เลือก leader → รวมเลนที่ไม่ conflict เป็นกลุ่ม, HCM formula ใช้ critical lane, gap-out/counting window/anti-starvation ระดับกลุ่ม, max continuous green | ✅ |
| `CarSpawner.cs` | Poisson process + ⭐ **RNG แยกต่อเลน** (`System.Random`) แทน global Unity Random → paired comparison แข็งแรงขึ้นมาก | ✅ |
| `ROIZone.cs` | ตรวจจับรถด้วย lane-identity filter, จับเวลา FCFS + ⭐ `guiSlot` รองรับ 6 เลน | ✅ |
| `DataLogger.cs` | เก็บ waiting time รายคัน + starvation events, export 2 ไฟล์ CSV | ✅ (ไม่ได้แก้) |
| `ScenarioManager.cs` | ⭐ **rewrite** — อ่านเลนจาก config (generic N เลน), 5 scenario × 6 เลน, ⭐ **warm-up period**, ⭐ **StableHash** (FNV-1a) แทน `GetHashCode()` | ✅ |

### 3.2 การปรับปรุงเชิงวิธีวิจัยที่เพิ่มเข้ามา (ควรเขียนในรายงาน)

1. **Warm-up period (120s)** — เดิมทุก run เริ่มจากถนนว่าง ทำให้รถ 10-20 คันแรกไม่ต้องรอไฟเลย ค่า avg waiting time จึงต่ำกว่าจริงและเอนเอียงเข้าข้าง Fixed ตอนนี้ปล่อยรถวิ่ง 120s ก่อนแล้วค่อยล้าง log เริ่มนับจริง → ได้ข้อมูล **steady-state** ล้วน (วิธีมาตรฐานของ discrete-event simulation)

2. **RNG แยกต่อเลน** — เดิมใช้ `UnityEngine.Random` ที่เป็น global ทำให้ลำดับการสุ่มสลับกันไปมาระหว่าง 6 spawner ตามจังหวะเฟรม → Fixed กับ Adaptive เจอ "ชุดรถ" ไม่เหมือนกันเป๊ะ ตอนนี้แต่ละเลนมี `System.Random` ของตัวเอง seed จาก `runSeed + hash(laneName)` → **ลำดับเวลามาถึงและ driver profile เหมือนกันเป๊ะทุกเลน** paired t-test จึง valid จริง

3. **StableHash (FNV-1a)** — `string.GetHashCode()` ของ .NET ไม่การันตีว่าเท่ากันข้าม session/แพลตฟอร์ม ทำให้ seed ไม่ reproducible จริง เปลี่ยนมาคำนวณเองแล้ว

4. **Max continuous green (90s)** — กันเลนเดิมเขียวยาวไม่จบในกรณี demand สูงต่อเนื่อง (สอดคล้องกับ max green ในตัวควบคุมจริง)

5. **Carry-over green** — เลนที่เขียวต่อเนื่องข้ามเฟสไม่ต้องขึ้นเหลือง เป็นพฤติกรรมของสัญญาณไฟจริง และทำให้ Fixed baseline ไม่เสียเปรียบโดยไม่จำเป็น

### 3.2b เครื่องมือใหม่ (รอบเดียวกัน)

| ไฟล์ | หน้าที่ |
|---|---|
| `Assets/Editor/ABACSceneBuilder.cs` ⭐ | เมนู `ABAC ▸ Build 4-Phase Scene` — สร้าง/ต่อ GameObject ทั้งแยกให้อัตโนมัติใน 1 คลิก (idempotent + Ctrl+Z ได้) และ `ABAC ▸ Validate Scene` ตรวจความถูกต้อง |
| `Analysis/analyze_results.py` ⭐ | paired t-test + Wilcoxon + Cohen's d + 95% CI + Shapiro-Wilk, แยกราย scenario และรายเลน, ออกกราฟ 5 รูป + ประโยคสำเร็จรูปสำหรับ abstract |

### 3.3 บั๊กที่แก้ในรอบนี้

- 🔴 **Infinite recursion ใน `IntersectionConfig.Build()`** — `Build()` → `SignalIndexOf()` → `EnsureBuilt()` → `Build()` วนไม่จบ **จะทำให้ Unity ค้างตอนกด Play** (เจอด้วย automated test ก่อนถึงมือ Unity) แก้โดยตั้ง `isBuilt` ก่อนวน conflict list
- 🔴 **Adaptive busy-loop** — ตอนโดน max-green ตัด อาจวนเลือกกลุ่มเดิมซ้ำในเฟรมเดียวจนค้าง แก้ด้วย `forcePhaseChange` ban กลุ่มเดิม + `yield return null` กันลูปหมุนฟรี
- `CarSpawner.DespawnWhenFar` ไม่ลบ entry ออกจาก `spawnedCars` → list โตเรื่อย ๆ ข้าม run
- `TrafficLight.NotifyCars` วน list ที่มีรถถูก Destroy ไปแล้ว → เพิ่ม auto-cleanup
- `ROIZone.OnGUI` hardcode index A/B/C → 6 เลนจะทับกัน แก้เป็น `guiSlot`
- 🔴 **`summary_all_runs.csv` สคีมาปนกัน** — `DataLogger` เขียนแบบ append และเขียนหัวตารางเฉพาะตอนไฟล์ยังไม่มี พอมีการเพิ่มคอลัมน์ `TotalSpawned` ทีหลัง แถวใหม่เลยมี 8 คอลัมน์แต่หัวตารางบอก 7 → **pandas อ่านไฟล์ไม่ออกเลย** (ไฟล์จริงในโปรเจคมีปัญหานี้อยู่: 18 แถวเก่า + 22 แถวใหม่ปนกัน) แก้โดยให้ตรวจหัวตารางก่อน append ถ้าไม่ตรงจะย้ายไฟล์เก่าไปเป็น `summary_all_runs_OLD_<เวลา>.csv` แล้วเริ่มไฟล์ใหม่
- ROI zone มี BoxCollider ซ้อนกัน 2 ตัว (trigger + solid) — builder ลบตัวเกินให้
- `CarAgent` waypoint: ถ้ารถเลี้ยวกว้างจนไม่เคยเข้าใกล้ waypoint ในรัศมี จะวนเป็นวงกลมไม่จบ — เพิ่มเงื่อนไข "ผ่านไปแล้วก็นับว่าถึง"

### 3.4 บั๊กที่แก้ไปแล้วรอบก่อน (ยังคงอยู่)

- รถซ้อนทับกัน → car-following ด้วยสูตร `v² = v_lead² + 2a·gap`
- รถมาใหม่ตอนไฟเขียวแล้วไม่ยอมวิ่ง → `RegisterCar()` ส่งสถานะไฟปัจจุบันทันที
- ROI นับรถข้ามเลน → `assignedLane` filter
- `NullReferenceException` ตอนสลับ controller → ย้าย array init ไป `Awake()`
- ไฟเขียว 2 เลนพร้อมกันตอนสลับ controller → `ForceStop()` หยุด coroutine ทั้งคู่ก่อนสลับเสมอ

### 3.5 การตรวจสอบที่ทำแล้ว

- ✅ Compile ผ่านทั้ง 9 ไฟล์ (mcs + UnityEngine stub)
- ✅ Conflict matrix: symmetric, ครบทุกคู่ตาม scope, เลนฟรีไม่มี signal index
- ✅ Fixed phase plan: ไม่มี conflict ภายในเฟส, ทุกเลนได้เขียวอย่างน้อย 1 เฟส
- ✅ Greedy group building: ทุก leader ให้กลุ่มที่ conflict-free เสมอ
- ✅ **รัน `RunCycle()` จริง 2 รอบเต็ม (240s) — ไม่มีเลน conflict ครองแยกพร้อมกันเลยแม้แต่ครั้งเดียว** (นับช่วงเหลืองเป็น "ครองแยก" ด้วย)
- ✅ Cycle length = 120s พอดี
- ✅ StableHash reproducible และแยก scenario/lane ได้

---

## 4. สิ่งที่ยังไม่ได้ทำ / ค้างอยู่

### 4.1 ตั้ง Scene ใน Unity — Priority สูงสุด 🔥
โค้ดพร้อมแล้ว แต่ยังต้องสร้าง GameObject + ต่อ Inspector เอง
→ **ทำตาม `SceneSetup_4Phase_Guide.md` ในโฟลเดอร์โปรเจค** (มี checklist ครบ)

สิ่งที่ต้องสร้างใหม่: `Light_Bx`, `StopLine_Bx`, `ROI_Ax/Bx/CA`, `Spawner_Ax/Bx/CA`, waypoint 8 จุด, และ GameObject `_Intersection`
สิ่งที่ต้องเปลี่ยนชื่อ: `Light_C` → `Light_CB`, `ROI_C` → `ROI_CB`, `Spawner_C` → `Spawner_CB`

⚠️ Reference ใน Inspector ของ Controller เดิมจะหายหมด (field เปลี่ยนชื่อ) — ต้องต่อใหม่ตามคู่มือ

### 4.2 Validate timeScale ก่อนรันจริง
ที่ timeScale สูง `Time.deltaTime` ต่อเฟรมใหญ่ขึ้น → การเบรก/เร่งของ `CarAgent` คลาดเคลื่อน (integration error)

**ต้องทำ:** รัน scenario เดียวกัน seed เดียวกัน ที่ 1x / 10x / 20x แล้วเทียบ `AvgWaitTime` ถ้าต่างเกิน ~5% แปลว่าเร็วเกินไป
→ ทำครั้งเดียวแล้วบันทึกไว้ **เป็นหัวข้อ validation ที่กรรมการชอบถาม**

### 4.3 รันจริง 270 runs
- `repetitionsPerScenario = 27`, `runDuration = 1800`, `warmupDuration = 120`
- เวลาจำลองรวม = 270 × 1920s ≈ **144 ชม.จำลอง**
- ที่ 10x ≈ 14.4 ชม. / ที่ 20x ≈ 7.2 ชม. / ที่ 50x ≈ 2.9 ชม.
- ลบ `summary_all_runs.csv` เก่าก่อน (เป็นแบบ append) + commit git ก่อนรัน
- แนะนำรันข้ามคืน

### 4.4 วิเคราะห์ผลข้อมูล
- Paired t-test เปรียบเทียบ Fixed vs Adaptive (waiting time, starvation events)
- ตรวจ success criteria: zero starvation events, ลด waiting time ได้จริง
- แยกวิเคราะห์รายเลนจาก `detailed_*.csv` (คอลัมน์ `Lane`)
- ⭐ ตรวจว่า waiting time ของ Ax และ CA ≈ 0 — เป็นหลักฐานว่าโมเดลเลนฟรีถูกต้อง
- สรุปเป็นกราฟ/ตารางสำหรับรายงาน

### 4.5 การทดลองเสริมที่น่าทำ (ถ้ามีเวลา)
ปิด `Enable Concurrent Phases` แล้วรันเทียบ → ได้ **3 ระบบเทียบกัน**: Fixed / Adaptive แบบทีละเลน / Adaptive แบบรวมกลุ่ม
เห็นชัดว่าส่วนไหนของ Adaptive ช่วยลด waiting time ได้เท่าไหร่ (ablation study) — เพิ่มน้ำหนักงานวิจัยเยอะมาก

### 4.6 อื่น ๆ ที่ค้างจาก proposal เดิม
- YOLOv8 track (การตรวจจับรถจริงจากกล้อง) — ยังไม่ได้เริ่ม แยกจาก Unity track
- ยืนยัน ABAC filming permission
- เก็บข้อมูลเวลาไฟจริงหน้า ABAC มา calibrate ค่า Fixed baseline (ปัจจุบันใช้ค่าสมมติฐาน DOH 2019 — เป็น limitation ที่ต้องระบุในรายงาน)

---

## 5. Timeline

- **Senior Project Exhibition:** 25 กันยายน 2026 (เหลือ ~5 สัปดาห์)
- **Final Defence:** 14-16 ตุลาคม 2026
- Exhibition prep ควรเริ่ม 8 กันยายน 2026

**แผนที่แนะนำ:**

| ช่วง | งาน |
|---|---|
| 20-25 ส.ค. | ตั้ง scene 4-phase + ผ่าน checklist |
| 26-28 ส.ค. | timeScale validation + ทดสอบรันสั้น |
| 29 ส.ค. - 2 ก.ย. | รัน 270 runs จริง (ข้ามคืน) |
| 3-7 ก.ย. | วิเคราะห์ t-test + ทำกราฟ |
| 8 ก.ย. เป็นต้นไป | เตรียม exhibition + เขียนรายงาน |

---

## 6. ข้อมูลอ้างอิงทางเทคนิค

- **HCM (Highway Capacity Manual):** สูตร `G = startupLostTime + D × saturationHeadway`, หลักการ concurrent non-conflicting movement
- **DOH Thailand (2019):** ค่า default 120s/cycle สำหรับ Fixed baseline
- **ITE standard:** yellow/all-red interval (ปัจจุบันยังเป็นค่าคงที่ 3s/2s — ยังไม่ได้คำนวณตามความเร็วเข้าใกล้จริง)
- **Poisson process** สำหรับ vehicle arrival simulation
- **Seeded RNG แยกต่อเลน** สำหรับ reproducibility และ paired comparison
- **Warm-up period** ตามหลัก discrete-event simulation (ตัด transient state ออก)

---

## 7. ไฟล์คู่มือที่เกี่ยวข้อง

- `SceneSetup_4Phase_Guide.md` — คู่มือตั้ง scene step-by-step + checklist + troubleshooting

---

*อัปเดตจากเซสชันพัฒนา 20 สิงหาคม 2026*
