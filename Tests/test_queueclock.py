"""
test_queueclock.py — the Python side of Kimmy's C1.

C1 is a Unity bug: ResetQueueStartFor is called only with `dropping`, so a head
that stays in the green group across phases never has its clock re-armed, wins
Q1 and Q2 for ever, and locks every other head out. His QueueClockTest proves it
in C#.

This proves the reference implementation does NOT have it — by simulation, not
by reading the code. Saturated demand, nothing ever empties, ten phases: every
head must be served.
"""
import soda

HEADS = ["A", "A2", "B", "Bx", "CB"]
ok = True


def check(name, got, want):
    global ok
    good = got == want
    ok &= good
    print(f"  {'PASS' if good else 'FAIL'}  {name:<46} {got!r}"
          + ("" if good else f"   want {want!r}"))


# Every head permanently occupied — the EveningPeak condition. Under C1 the head
# that wins the first phase keeps winning until its own lane drains, and here no
# lane ever drains.
STUCK = {"A": 6, "A2": 0, "B": 6, "Bx": 6, "CB": 6}

c = soda.Soda(HEADS, order=["A", "B", "Bx", "CB"])
c.snapshot(0.0, STUCK)
c.collect_step(0.0)

served, leaders, t = {h: 0 for h in HEADS}, [], 0.0
for _ in range(10):
    d = c.decide(t)
    assert d is not None, "controller stalled with demand present"
    leaders.append(d.leader)
    for h in d.group:
        served[h] += 1
    t += (soda.YELLOW + soda.ALL_RED if d.cut else 0.0) + d.green
    c.snapshot(t, STUCK)          # still saturated, nothing drained
    c.end_phase(t)

print("10 phases under permanent saturation")
print(f"  leaders  : {' '.join(leaders)}")
print(f"  served   : {dict(served)}\n")

check("H_B does not monopolise", leaders.count("B") < 10, True)
check("H_CB is served at least once", served["CB"] >= 1, True)
check("H_Bx is served at least once", served["Bx"] >= 1, True)
check("H_A is served at least once", served["A"] >= 1, True)
check("every signalised head served", all(served[h] >= 1 for h in
                                          ["A", "B", "Bx", "CB"]), True)

# The mechanism itself: a head that stays green across consecutive phases must
# still have its clock re-armed. This is the exact line Unity was missing.
c2 = soda.Soda(HEADS, order=["A", "B", "Bx", "CB"])
c2.snapshot(0.0, STUCK)
c2.collect_step(0.0)
d1 = c2.decide(0.0)
c2.snapshot(30.0, STUCK)
c2.end_phase(30.0)
d2 = c2.decide(30.0)
cont = set(d1.group) & set(d2.group)
print(f"\nheads green across both phases: {sorted(cont) or 'none'}")
for h in sorted(cont):
    check(f"{h} continued green -> clock re-armed", c2.queue_start[h], 30.0)
check("no head keeps a clock older than the phase it was served in",
      all(c2.queue_start[h] >= 30.0 for h in d1.group
          if c2.queue_start[h] != float("inf")), True)

print("\n" + ("ALL PASS" if ok else "FAILURES ABOVE"))
raise SystemExit(0 if ok else 1)
