# verify_traces.py — drive soda.py through every decision point of SODA_BASELINE.md §5.
# Run from the folder that contains soda.py:  python3 verify_traces.py
# Lines marked "bypassing window" exist only because soda.py still carries the counting window (D1).
"""Drive soda.py through the decision points of S1-S5 and the spec worked example.
soda.py has no COLLECT phase, so only Q1-Q4 decisions and end_phase resets are checked here."""
import soda
inf = float("inf")
HEADS = ["A", "A2", "B", "Bx", "CB"]
ORDER = ["A", "A2", "B", "Bx", "CB"]     # A -> B -> Bx -> CB (A2 rides with A)

def mk(counts, qs, green=set()):
    s = soda.Soda(HEADS, order=ORDER)
    s.count = {h: counts.get(h, 0) for h in HEADS}
    s.last_count = dict(s.count)
    s.queue_start = {h: qs.get(h, inf) for h in HEADS}
    s.green = set(green)
    return s

def show(tag, d):
    print(f"  {tag}: leader={d.leader} group={sorted(d.group)} G={d.green:.2f} cut={sorted(d.cut)} reason={d.reason}")

print("== S1: CB=1 at t=11.0 (post-COLLECT) ==")
s = mk({"CB": 1}, {"CB": 4.0})
d = s.decide(11.0)
print("  soda.py decide ->", d, "   <- None means counting-window hold (not in C#)")
s.waited_once = True; s.waiting_until = None   # bypass soda.py's counting window
d = s.decide(11.0); show("bypassing window", d)

print("== S2: A=1 (qs 2.0), B=1 (qs 4.0) at t=4.0 ==")
s = mk({"A": 1, "B": 1}, {"A": 2.0, "B": 4.0}); s.waited_once = True
d = s.decide(4.0); show("S2", d)

print("== S3: Bx=4 (qs 2.0) at t=2.0 ==")
s = mk({"Bx": 4}, {"Bx": 2.0})
d = s.decide(2.0); show("S3", d)

print("== S4 phase 1: all tied at 0.0, counts A=12 B=5 Bx=9 CB=7 ==")
s = mk({"A": 12, "B": 5, "Bx": 9, "CB": 7}, {"A": 0.0, "B": 0.0, "Bx": 0.0, "CB": 0.0})
d = s.decide(0.0); show("P1", d)
g1 = d.green
# phase end at g1: A and B still occupied
s.snapshot(g1, {"A": 12, "B": 5, "Bx": 9, "CB": 7}); s.end_phase(g1)
print(f"  after P1 end: qs = { {h: s.queue_start[h] for h in HEADS} }")
print("== S4 phase 2 decision at t=30.67 ==")
d = s.decide(g1); show("P2", d)
# anti-starvation check during P2: green starts at g1+5 = 35.67 ; at elapsed 20 (t=55.67) CB waited 55.67
t = 35.67 + 20.0
print(f"  should_end_early at t={t:.2f} (elapsed 20): {s.should_end_early(t, 35.67 + d.green)}")
s.snapshot(t, {"A": 12, "B": 4, "Bx": 9, "CB": 7}); s.end_phase(t)
print("== S4 phase 3 decision at t=55.67 ==")
d = s.decide(t); show("P3", d)

print("== S5: B=10, Bx=3 tied at 0.0 ==")
s = mk({"B": 10, "Bx": 3}, {"B": 0.0, "Bx": 0.0})
d = s.decide(0.0); show("S5", d)
print(f"  Bx empties at 2.95+3*2.31 = {2.95+3*2.31:.2f}; idle = {d.green - (2.95+3*2.31):.2f}")

print("== gap-out semantics ==")
s = mk({"B": 3}, {"B": 0.0}, green={"B"}); s.green_since = 0.0
for t, c in [(2.0, 3), (4.0, 0), (6.0, 0), (8.0, 0)]:
    s.snapshot(t, {"B": c})
    print(f"  t={t}: count={c} empty_since={s.empty_since} -> {s.should_end_early(t, 30.0)}")
