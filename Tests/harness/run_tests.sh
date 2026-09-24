#!/bin/bash
# ============================================================================
#  ABAC TrafficSim regression suites - run outside Unity with mcs/mono
#
#  Run with:  bash Tests/harness/run_tests.sh        (from the repo root or anywhere)
#  Requires:  mono + mcs     macOS: brew install mono     Ubuntu: apt install mono-devel
#
#  Why the stubs: code in Assets/Scripts refers to UnityEngine, which will not compile outside
#  Unity. UnityStub.cs and friends fake just enough of those types to compile and run pure logic.
#
#  -define:UNITY_EDITOR is required when compiling Assets/Editor. Without it the whole file is
#     cut away by #if and "it compiles" becomes a false pass.
#
#  QueueClockTest must not be compiled with a second copy of the stubs, or UnityEngine.Time
#     becomes a different static from the one inside gamecode.dll and the test reads the wrong clock.
# ============================================================================
set -e
cd "$(dirname "$0")"                  # Tests/harness
ROOT="../.."                          # repo root
OUT="$(pwd)/_build"
mkdir -p "$OUT"
STUBS="UnityStub.cs UnityGuiStub.cs UnityEditorStub.cs UnityThemeStub.cs"
SCRIPTS="$ROOT/Assets/Scripts/*.cs"
EDITOR="$ROOT/Assets/Editor/*.cs"
SCENE="Assets/Scenes/SampleScene.unity"   # relative to ROOT, where the tests are run from
FAIL=0

echo "=== compile: Assets/Scripts only ==="
mcs -target:library -out:"$OUT/gamecode.dll" $SCRIPTS $STUBS 2>&1 | grep -E ": error" && FAIL=1
echo "=== compile: Scripts + Editor (real UNITY_EDITOR) ==="
mcs -define:UNITY_EDITOR -target:library -out:"$OUT/all_ed.dll" $SCRIPTS $EDITOR $STUBS 2>&1 | grep -E ": error" && FAIL=1

# Every test runs from the repo root because some of them read source files by Assets/Scripts/... paths.
run () {  # run <exe> <src> <suite name> <flags> <dll> [args]
  mcs $4 -target:exe -out:"$OUT/$1.exe" $2 $STUBS -r:"$OUT/$5" 2>&1 | grep -E ": error" \
    && { echo "COMPILE FAIL $3"; FAIL=1; return; }
  echo "=== $3 ==="
  ( cd "$ROOT" && mono "$OUT/$1.exe" $6 2>&1 | grep -E "FAIL|PASSED" ) || true
  ( cd "$ROOT" && mono "$OUT/$1.exe" $6 > /dev/null 2>&1 ) || FAIL=1
}

# Tests that construct UnityEngine objects themselves (Transform/GameObject) must be compiled
# Tests that construct UnityEngine objects themselves (Transform/GameObject) must be compiled
# *without* a second copy of the stubs, or the types come from two assemblies and cannot be
# assigned across (same reason as QueueClockTest).
run_nostub () {  # run_nostub <exe> <src> <suite name> <dll>
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
run_nostub t11 Compare101Test.cs Compare101Test gamecode.dll

# Compares ".unity against .cs", not ".cs against .cs" - the one that actually catches scene-serialization bugs
run t7 ../SceneAuditTest.cs   SceneAuditTest ""                     gamecode.dll "$SCENE"

# SODA: queueStart reset / gap-out 2 snapshots / no tracker left behind / tie-break / single clock
run t8 ../SodaTest.cs         SodaTest       ""                     gamecode.dll

# Queue clock of a head that stays green across phases (bug C1, 11 Sep 2026)
# No $STUBS here: Time must be the same static as the one inside gamecode.dll
mcs -target:exe -out:"$OUT/t9.exe" ../QueueClockTest.cs -r:"$OUT/gamecode.dll" 2>&1 | grep -E ": error" \
  && { echo "COMPILE FAIL QueueClockTest"; FAIL=1; }
echo "=== QueueClockTest ==="
( cd "$ROOT" && mono "$OUT/t9.exe" 2>&1 | grep -E "FAIL|PASSED" ) || true
( cd "$ROOT" && mono "$OUT/t9.exe" > /dev/null 2>&1 ) || FAIL=1

echo
[ $FAIL -eq 0 ] && echo "########## ALL SUITES PASSED ##########" || echo "########## SOME SUITES FAILED ##########"
exit $FAIL
