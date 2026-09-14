#!/bin/bash
# ============================================================================
#  ชุด regression ของ ABAC TrafficSim — รันนอก Unity ด้วย mcs/mono
#
#  วิธีรัน:   bash Tests/harness/run_tests.sh        (จาก root ของ repo หรือที่ไหนก็ได้)
#  ต้องมี:    mono + mcs     macOS: brew install mono     Ubuntu: apt install mono-devel
#
#  ทำไมต้องมี stub: โค้ดใน Assets/Scripts อ้าง UnityEngine ซึ่งคอมไพล์นอก Unity ไม่ได้
#  UnityStub.cs และเพื่อนจึงจำลอง type ที่จำเป็นให้พอคอมไพล์และรันตรรกะล้วน ๆ ได้
#
#  ⚠️ ต้องมี -define:UNITY_EDITOR ตอนคอมไพล์ Assets/Editor ไม่งั้นไฟล์ทั้งไฟล์ถูก #if
#     ตัดทิ้ง แล้ว "คอมไพล์ผ่าน" จะกลายเป็นผลลวง
#
#  ⚠️ QueueClockTest ห้ามใส่ไฟล์ stub ซ้ำตอนคอมไพล์ ไม่งั้น UnityEngine.Time
#     จะกลายเป็นคนละ static กับตัวใน gamecode.dll แล้วเทสจะอ่านเวลาผิด
# ============================================================================
set -e
cd "$(dirname "$0")"                  # Tests/harness
ROOT="../.."                          # root ของ repo
OUT="$(pwd)/_build"
mkdir -p "$OUT"
STUBS="UnityStub.cs UnityGuiStub.cs UnityEditorStub.cs UnityThemeStub.cs"
SCRIPTS="$ROOT/Assets/Scripts/*.cs"
EDITOR="$ROOT/Assets/Editor/*.cs"
SCENE="Assets/Scenes/SampleScene.unity"   # relative to ROOT, เทสถูกรันจากที่นั่น
FAIL=0

echo "=== compile: Assets/Scripts เท่านั้น ==="
mcs -target:library -out:"$OUT/gamecode.dll" $SCRIPTS $STUBS 2>&1 | grep -E ": error" && FAIL=1
echo "=== compile: Scripts + Editor (UNITY_EDITOR จริง) ==="
mcs -define:UNITY_EDITOR -target:library -out:"$OUT/all_ed.dll" $SCRIPTS $EDITOR $STUBS 2>&1 | grep -E ": error" && FAIL=1

# เทสทุกตัวถูกรันจาก root ของ repo เพราะบางตัวอ่านซอร์สเองด้วย path แบบ Assets/Scripts/...
run () {  # run <exe> <src> <ชื่อชุด> <flags> <dll> [args]
  mcs $4 -target:exe -out:"$OUT/$1.exe" $2 $STUBS -r:"$OUT/$5" 2>&1 | grep -E ": error" \
    && { echo "COMPILE FAIL $3"; FAIL=1; return; }
  echo "=== $3 ==="
  ( cd "$ROOT" && mono "$OUT/$1.exe" $6 2>&1 | grep -E "FAIL|PASSED" ) || true
  ( cd "$ROOT" && mono "$OUT/$1.exe" $6 > /dev/null 2>&1 ) || FAIL=1
}

# เทสที่ "สร้าง" ออบเจกต์ของ UnityEngine เอง (Transform/GameObject) ต้องคอมไพล์โดย *ไม่* ใส่ stub ซ้ำ
# ไม่งั้นจะได้ type สองชุดจากคนละแอสเซมบลี แล้ว assign ข้ามกันไม่ได้ (เหตุผลเดียวกับ QueueClockTest)
run_nostub () {  # run_nostub <exe> <src> <ชื่อชุด> <dll>
  mcs -target:exe -out:"$OUT/$1.exe" $2 -r:"$OUT/$4" 2>&1 | grep -E ": error" \
    && { echo "COMPILE FAIL $3"; FAIL=1; return; }
  echo "=== $3 ==="
  ( cd "$ROOT" && mono "$OUT/$1.exe" 2>&1 | grep -E "FAIL|PASSED" ) || true
  ( cd "$ROOT" && mono "$OUT/$1.exe" > /dev/null 2>&1 ) || FAIL=1
}

run t1 ConflictTest.cs        ConflictTest   ""                     gamecode.dll
run t2 FixedCycleTest.cs      FixedCycleTest ""                     gamecode.dll
run t3 BatchMathTest.cs       BatchMathTest  ""                     gamecode.dll
run t4 EtaTest.cs             EtaTest        ""                     gamecode.dll
run t5 SharedLaneTest.cs      SharedLaneTest ""                     gamecode.dll
run t6 GeometryTest.cs        GeometryTest   "-define:UNITY_EDITOR" all_ed.dll
run_nostub t10 HeadwayTest.cs HeadwayTest    gamecode.dll

# เทียบ ".unity กับ .cs" ไม่ใช่ ".cs กับ .cs" — ตัวที่จับบั๊ก scene-serialization ได้จริง
run t7 ../SceneAuditTest.cs   SceneAuditTest ""                     gamecode.dll "$SCENE"

# SODA: queueStart reset / gap-out 2 snapshots / ไม่มี tracker หลงเหลือ / tie-break / single clock
run t8 ../SodaTest.cs         SodaTest       ""                     gamecode.dll

# นาฬิกาคิวของหัวที่เขียวต่อเนื่อง (บั๊ก C1, 11 ก.ย. 2026)
# ไม่ใส่ $STUBS เพราะ Time ต้องเป็นตัวเดียวกับใน gamecode.dll
mcs -target:exe -out:"$OUT/t9.exe" ../QueueClockTest.cs -r:"$OUT/gamecode.dll" 2>&1 | grep -E ": error" \
  && { echo "COMPILE FAIL QueueClockTest"; FAIL=1; }
echo "=== QueueClockTest ==="
( cd "$ROOT" && mono "$OUT/t9.exe" 2>&1 | grep -E "FAIL|PASSED" ) || true
( cd "$ROOT" && mono "$OUT/t9.exe" > /dev/null 2>&1 ) || FAIL=1

echo
[ $FAIL -eq 0 ] && echo "########## ALL SUITES PASSED ##########" || echo "########## SOME SUITES FAILED ##########"
exit $FAIL
