"""test_soda.py — check soda.py against the worked example in SODA_SPEC_kimmy.md.

The spec states the expected output for one full decision, so it is the best
available test: if this passes, the implementation matches what Kimmy specified
rather than what I assumed.
"""
import soda

HEADS = ["A", "A2", "B", "Bx", "CB"]
ok = True


def check(name, got, want):
    global ok
    good = got == want
    ok &= good
    print(f"  {'PASS' if good else 'FAIL'}  {name:<34} got {got!r}"
          + ("" if good else f"   want {want!r}"))


print("Spec §4 worked example")
print("  state: queueStart A=12 B=38 Bx=8 CB=inf | count A=9 B=4 Bx=6 CB=0\n")

s = soda.Soda(HEADS)
s.count = {"A": 9, "A2": 0, "B": 4, "Bx": 6, "CB": 0}
s.last_count = dict(s.count)
inf = float("inf")
s.queue_start = {"A": 12.0, "A2": inf, "B": 38.0, "Bx": 8.0, "CB": inf}
s.green = {"A", "A2"}

d = s.decide(45.0)
check("Q1 no starvation (37.0 < 42.75)", d.reason, "LONGEST_WAIT")
check("Q2 leader is the longest wait", d.leader, "Bx")
check("Q3 group = {Bx, B}", sorted(d.group), ["B", "Bx"])
check("Q4 G = max(16.8, 12.2)", round(d.green, 1), 16.8)
check("cut = {A, A2}", sorted(d.cut), ["A", "A2"])

print("\nPhase end at t=66.8: count(Bx)=2, count(B)=0")
s.snapshot(66.8, {"A": 9, "A2": 0, "B": 0, "Bx": 2, "CB": 0})
s.end_phase(66.8)
check("queueStart[Bx] reset to phase end", s.queue_start["Bx"], 66.8)
check("queueStart[B] released to inf", s.queue_start["B"], inf)
check("queueStart[A] untouched", s.queue_start["A"], 12.0)

print("\nOther spec clauses")
check("green_for(2) floors at 8.0", soda.green_for(2), 8.0)
check("green_for(19) caps at 45.0", soda.green_for(19), 45.0)
check("green_for(10) = 26.1", round(soda.green_for(10), 1), 26.1)
check("A/Bx conflict", soda.conflicts("A", "Bx"), True)
check("B/Bx do not conflict", soda.conflicts("B", "Bx"), False)
check("gap-out is 2 snapshots", soda.GAP_OUT_SNAPSHOTS, 2)

# Starvation must outrank a longer-waiting but not-yet-starving head.
s2 = soda.Soda(HEADS)
s2.count = {"A": 5, "A2": 0, "B": 2, "Bx": 1, "CB": 0}
s2.last_count = dict(s2.count)
s2.queue_start = {"A": 50.0, "A2": inf, "B": 40.0, "Bx": 5.0, "CB": inf}
d2 = s2.decide(50.0)
check("starvation fires at >=42.75 s", d2.reason, "STARVATION")
check("starving head leads", d2.leader, "Bx")
check("starvation still opens a green", d2.green > 0, True)

# The counting window was REMOVED in v1.1 (baseline §6.3). A thin leader must
# now open green immediately — COLLECT already asked the same question with the
# same number, so waiting again made one vehicle wait 11 s.
s3 = soda.Soda(HEADS)
s3.count = {"A": 0, "A2": 0, "B": 0, "Bx": 1, "CB": 0}
s3.last_count = dict(s3.count)
s3.queue_start = {"A": inf, "A2": inf, "B": inf, "Bx": 10.0, "CB": inf}
d3 = s3.decide(20.0)
check("thin leader opens green at once", d3 is not None and d3.leader, "Bx")
check("no counting window constant remains",
      hasattr(soda, "COUNTING_WINDOW_SEC"), False)
check("no MIN_LEADER_COUNT remains", hasattr(soda, "MIN_LEADER_COUNT"), False)

print("\n" + ("ALL PASS" if ok else "FAILURES ABOVE"))
raise SystemExit(0 if ok else 1)
