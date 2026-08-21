# คู่มือใช้ Git กับโปรเจค ABAC Traffic Sim

**สถานะ repo ตอนนี้** (ตรวจจาก `.git/config` แล้ว)

| | |
|---|---|
| Remote | `https://github.com/Kimmy213/ABAC-Traffic-Signal-Sim.git` |
| Branch | `main` (track `origin/main` อยู่แล้ว) |
| โฟลเดอร์ | `C:\Users\USER\Desktop\ProjectSim\frist_try\ABAC_TrafficSim` |

ตั้งค่าครบแล้ว ไม่ต้อง `git init` หรือ `git remote add` ใหม่ — **push ได้เลย**

---

## 1. ขั้นตอนปกติ (จำแค่ 4 คำสั่งนี้พอ)

เปิด **Terminal / PowerShell** (หรือใช้ Terminal ใน VS Code ก็ได้) แล้ว:

```bash
cd C:\Users\USER\Desktop\ProjectSim\frist_try\ABAC_TrafficSim

git status          # 1. ดูว่ามีอะไรเปลี่ยนบ้าง
git add .           # 2. เลือกไฟล์ทั้งหมดที่เปลี่ยน
git commit -m "ข้อความอธิบายว่าทำอะไร"    # 3. บันทึกเป็น 1 จุด
git push            # 4. ส่งขึ้น GitHub
```

**คิดง่าย ๆ:**

```
ไฟล์ที่แก้         →  git add   →   staging   →  git commit  →  ประวัติในเครื่อง  →  git push  →  GitHub
(working dir)                      (เตรียมส่ง)                    (local repo)                    (remote)
```

- `add` = เลือกว่าจะเอาอะไรเข้ารอบนี้
- `commit` = ปั๊มเป็นจุดบันทึกในเครื่อง (ยังไม่ขึ้นเน็ต)
- `push` = ส่งจุดบันทึกทั้งหมดขึ้น GitHub

⚠️ commit อย่างเดียวยังไม่ขึ้น GitHub นะครับ ต้อง push ด้วย

---

## 2. commit สำหรับงานรอบนี้

งานที่เพิ่งทำไปเยอะมาก แนะนำแบ่งเป็นหลาย commit จะอ่านย้อนหลังง่ายกว่า

```bash
# ระบบ 4-phase
git add Assets/Scripts/IntersectionConfig.cs Assets/Scripts/FixedController.cs Assets/Scripts/AdaptiveController.cs
git commit -m "Expand to 4-phase system with conflict matrix

- Add IntersectionConfig as single source of truth for lanes + conflicts
- FixedController: phase list with concurrent lanes, carry-over green
- AdaptiveController: FCFS leader + conflict-free group building
- Fix infinite recursion in Build() that would hang Unity on Play"

# พฤติกรรมรถ
git add Assets/Scripts/CarAgent.cs Assets/Scripts/CarSpawner.cs Assets/Scripts/ROIZone.cs Assets/Scripts/TrafficLight.cs
git commit -m "Add turning lanes, per-lane RNG, and car colors

- Waypoint steering with physics-based turn speed limit
- Per-lane System.Random so Fixed/Adaptive see identical traffic
- Fix cars not moving on green when light registration was cleared"

# เครื่องมือ
git add Assets/Editor/ Assets/Scripts/SimulationControlPanel.cs Assets/Scripts/BatchRunPanel.cs Assets/Scripts/TestCaseLibrary.cs
git commit -m "Add scene builder, control panel, and batch run UI"

# ที่เหลือทั้งหมด
git add .
git commit -m "Update scene, materials, docs, and analysis script"

git push
```

**อยากง่ายกว่านั้น** ก็รวบเป็นก้อนเดียวได้:

```bash
git add .
git commit -m "Expand to 4-phase system: 6 lanes, conflict matrix, control panel, batch UI, analysis script"
git push
```

---

## 3. ⚠️ เรื่อง SimulationResults — อ่านก่อน push

`.gitignore` ตอนนี้ **ไม่ได้กัน** โฟลเดอร์ `SimulationResults/` ไว้

ตอนนี้มีไฟล์ทดสอบเก่าอยู่ ~41 ไฟล์ และหลังรันจริง 270 runs จะมี **540+ ไฟล์** ถ้าปล่อยไว้ repo จะรก

**แนะนำ:** ลบไฟล์ทดสอบเก่าทิ้งก่อน (มันเป็นข้อมูลจากตอนยังเป็น 3-phase ใช้ไม่ได้แล้ว และไฟล์ `summary_all_runs.csv` ก็มีสคีมาปนกันอยู่)

```bash
# ลบผลทดสอบเก่าทั้งหมด
rmdir /s /q SimulationResults          # Windows CMD
# หรือ
Remove-Item -Recurse -Force SimulationResults   # PowerShell
```

แล้วตัดสินใจว่าจะเก็บผลจริงลง git ไหม:

**ทางเลือก A — เก็บผลจริงไว้ใน git (แนะนำสำหรับ senior project)**
ข้อมูลดิบคือหลักฐานงานวิจัย กรรมการขอดูได้ และถ้าเครื่องพังก็ยังมี backup
ไม่ต้องแก้ `.gitignore` แค่ push ตามปกติหลังรันเสร็จ

**ทางเลือก B — เก็บแค่ไฟล์สรุป**
ถ้ากลัว repo ใหญ่เกิน เพิ่มบรรทัดนี้ท้าย `.gitignore`:

```gitignore
# ===== Simulation Output =====
SimulationResults/detailed_*.csv
SimulationResults/summary_all_runs_OLD_*.csv
```
แบบนี้จะเก็บแค่ `summary_all_runs.csv`, `batch_summary_*.txt` และโฟลเดอร์ `analysis_output/`

> ไฟล์ CSV เป็น text ล้วน ขนาดเล็กมาก 540 ไฟล์รวมกันน่าจะไม่ถึง 20 MB — **ผมเอน A มากกว่า**

---

## 4. ตอน push ครั้งแรกจะโดนถาม login

GitHub **เลิกรับรหัสผ่านตั้งแต่ปี 2021** ใส่รหัส GitHub ปกติจะไม่ผ่าน

**วิธีที่ง่ายที่สุดบน Windows:** พอ push แล้วมันจะเด้งหน้าต่าง *Git Credential Manager* ขึ้นมา → กด **"Sign in with your browser"** → login ในเบราว์เซอร์ → จบ ครั้งต่อไปจำให้เอง

**ถ้าไม่มีหน้าต่างเด้ง** ต้องสร้าง Personal Access Token เอง:

1. ไป https://github.com/settings/tokens
2. **Generate new token (classic)**
3. ติ๊ก scope **`repo`** อย่างเดียวพอ
4. ตั้งวันหมดอายุ (90 วัน ครอบคลุมถึง defence ตุลาคม)
5. **ก๊อป token เก็บไว้ทันที** — ปิดหน้าไปแล้วดูซ้ำไม่ได้
6. เวลา push ถาม username ใส่ `Kimmy213` / ถาม password **ใส่ token** (ไม่ใช่รหัสผ่าน)

---

## 5. ก่อน push ทุกครั้ง — เช็ค 3 อย่าง

```bash
git status
```

- [ ] **ไม่มี `Library/` หรือ `Temp/` โผล่ในลิสต์** — ถ้าโผล่แปลว่า `.gitignore` มีปัญหา อย่า add
- [ ] **มีไฟล์ `.meta` ตามมาด้วยทุกไฟล์ใหม่** — Unity ต้องใช้ ถ้าไม่มี reference จะหลุดตอนเปิดในเครื่องอื่น
- [ ] **`Assets/Scenes/SampleScene.unity` อยู่ในลิสต์** ถ้ากด Build Scene แล้ว Ctrl+S ไปแล้ว

---

## 6. คำสั่งที่ใช้บ่อยอีกชุด

```bash
git log --oneline -10        # ดูประวัติ 10 อันล่าสุด
git diff                     # ดูว่าแก้อะไรไปบ้าง (ที่ยังไม่ add)
git diff --stat              # ดูแบบสรุปว่าไฟล์ไหนเปลี่ยนกี่บรรทัด
git add Assets/Scripts/CarAgent.cs    # add แค่ไฟล์เดียว
git restore ไฟล์             # ยกเลิกการแก้ไฟล์นั้น (ยังไม่ได้ add)
git restore --staged ไฟล์    # เอาออกจาก staging (แต่ยังแก้อยู่)
```

---

## 7. เจอ error แล้วทำไง

| Error | แปลว่า | แก้ยังไง |
|---|---|---|
| `Updates were rejected because the remote contains work that you do not have locally` | มีคนอื่น (หรือเครื่องอื่นของเรา) push ไปก่อน | `git pull` แล้วค่อย `git push` ใหม่ |
| `Please tell me who you are` | ยังไม่ได้ตั้งชื่อ/อีเมล | `git config --global user.name "ชื่อ"` และ `git config --global user.email "อีเมล"` |
| `Authentication failed` | ใส่รหัสผ่านแทน token | ดูข้อ 4 |
| `nothing to commit, working tree clean` | ไม่มีอะไรเปลี่ยน (หรือลืม `git add`) | ปกติ ไม่ใช่ error |
| `fatal: not a git repository` | อยู่ผิดโฟลเดอร์ | `cd` เข้าโฟลเดอร์โปรเจคก่อน |
| ค้างที่หน้าจอแปลก ๆ มี `~` เต็มไปหมด | มันเปิด vim ให้พิมพ์ commit message | กด `Esc` แล้วพิมพ์ `:wq` กด Enter |

---

## 8. สลับเครื่อง Windows ↔ Mac

เคยทำได้แล้ว วิธีเดิม:

```bash
# เครื่องที่ทำงานเสร็จ
git add . && git commit -m "..." && git push

# อีกเครื่อง ก่อนเริ่มทำงานทุกครั้ง
git pull
```

⚠️ **อย่าลืม `git pull` ก่อนเริ่มแก้เสมอ** ถ้าลืมแล้วไปแก้ไฟล์เดียวกันทั้งสองเครื่อง จะเกิด merge conflict ที่แก้ยุ่งยาก โดยเฉพาะไฟล์ `.unity` (scene) ที่ merge ด้วยมือแทบไม่ได้เลย

---

## 9. นิสัยที่ควรติด

- **commit ก่อนกด `ABAC ▸ Build 4-Phase Scene` ทุกครั้ง** — builder แก้ scene จริง ถ้าผลไม่ถูกใจจะได้ย้อนได้ด้วย `git restore Assets/Scenes/SampleScene.unity`
- **commit ก่อนปล่อยรัน 270 runs** — จะได้รู้แน่ชัดว่าผลชุดนี้มาจากโค้ดเวอร์ชันไหน (สำคัญกับความน่าเชื่อถือของงานวิจัย)
- **commit ย่อย ๆ บ่อย ๆ ดีกว่าก้อนใหญ่ก้อนเดียว** — ย้อนกลับได้ตรงจุด
- **เขียน message ให้อ่านรู้เรื่อง** — `"fix bug"` ไม่ช่วยอะไร, `"Fix cars not moving on green light"` ช่วยเยอะ

### เคล็ดลับสำหรับ defence

ก่อนวันสอบ ให้ tag เวอร์ชันที่ใช้เก็บข้อมูลจริงไว้:

```bash
git tag -a v1.0-final-data -m "Version used for the 270-run experiment"
git push origin v1.0-final-data
```

ถ้ากรรมการถามว่า "ผลนี้มาจากโค้ดตัวไหน" ตอบได้ทันทีว่า tag `v1.0-final-data`
