"""
test_soda_rounds.py — replay Kimmy's worked traces against soda.py.

SODA_v1.1_AS_BUILT.md carries five second-by-second traces produced by the C#
controller. They are the strongest test available: if this Python reference
reproduces them, the two implementations agree about the algorithm. If it does
not, one of them is wrong, and it is much cheaper to find out now.

Each scenario below is the trace as Kimmy wrote it, not as I would have written
it. Where his trace and this code disagree the test FAILS rather than adapting.
"""
import soda

HEADS = ["A", "A2", "B", "Bx", "CB"]
inf = float("inf")
ok = True


def check(name, got, want, tol=0.01):
    global ok
    good = (abs(got - want) <= tol) if isinstance(want, float) else got == want
    ok &= good
    print(f"  {'PASS' if good else 'FAIL'}  {name:<42} {got!r}"
          + ("" if good else f"   want {want!r}"))


def run(arrivals, until, heads=HEADS):
    """Feed snapshots every 2 s; return the controller and the first decision.

    arrivals: {time -> {head: count}} applied from that time onward.
    """
    c = soda.Soda(heads, order=["A", "B", "Bx", "CB"])
    state = {h: 0 for h in heads}
    serve_at = None
    exit_reason = None
    decision = None
    t = 0.0
    while t <= until:
        for at in sorted(arrivals):
            if at <= t:
                state.update(arrivals[at])
        c.snapshot(t, state)
        r = c.collect_step(t)
        if r and serve_at is None:
            serve_at, exit_reason = t, r
        if c.phase == "SERVE" and decision is None:
            decision = c.decide(t)

        # The COLLECT ceiling is the one event not on the snapshot clock. If it
        # falls before the next snapshot, step to it exactly — deferring would
        # turn 7 s into 8 s, which is the failure this test exists to catch.
        nxt = t + soda.SAMPLE_SEC
        dl = c.collect_deadline
        if dl is not None and dl < nxt:
            r = c.collect_step(dl)
            if r and serve_at is None:
                serve_at, exit_reason = dl, r
            if c.phase == "SERVE" and decision is None:
                decision = c.decide(dl)
        t = nxt
    return c, serve_at, exit_reason, decision


print("S1 — one vehicle at night, COLLECT runs the full 7 s")
# CB arrives 3.4 s, first seen at snapshot t=4.0
c, serve, why, d = run({4.0: {"CB": 1}}, 14.0)
check("COLLECT exits on the ceiling", why, "CEILING")
check("CollectDuration = 7.0", c.collect_duration, 7.0)
check("SERVE begins at t=11.0", serve, 11.0)
check("leader is CB", d.leader, "CB")
check("D=1 -> 5.26 clamps to gMin 8.0", d.green, 8.0)
check("group is CB alone", sorted(d.group), ["CB"])

print("\nS2 — two heads arriving in sequence, early exit (a)")
c, serve, why, d = run({2.0: {"A": 1}, 4.0: {"A": 1, "B": 1}}, 10.0)
check("exits because 2 heads have demand", why, "EARLY_HEADS")
check("CollectDuration = 2.0", c.collect_duration, 2.0)
check("SERVE begins at t=4.0", serve, 4.0)
check("leader is A — queueStart 2.0 beats B's 4.0", d.leader, "A")
check("B joins A (no conflict), A2 forced in", sorted(d.group), ["A", "A2", "B"])

print("\nS3 — burst on one head, early exit (b)")
c, serve, why, d = run({2.0: {"Bx": 4}}, 10.0)
check("exits because a head has >= 3", why, "EARLY_DEMAND")
check("CollectDuration = 0.0", c.collect_duration, 0.0)
check("SERVE begins on the same snapshot", serve, 2.0)
check("leader is Bx", d.leader, "Bx")
check("D=4 -> 2.95 + 4x2.31", d.green, 12.19)
check("group is Bx alone — B is empty", sorted(d.group), ["Bx"])

print("\nS4 — peak, everything tied at t=0")
c, serve, why, d = run({0.0: {"A": 6, "B": 3, "Bx": 4, "CB": 2}}, 6.0)
check("exits immediately, 4 heads", why, "EARLY_HEADS")
check("CollectDuration = 0.0", c.collect_duration, 0.0)
check("all tied -> leader by declared order = A", d.leader, "A")
check("group = A, A2, B", sorted(d.group), ["A", "A2", "B"])

print("\nQueue-start reset — the rule that stops Bx locking the junction")
c = soda.Soda(HEADS, order=["A", "B", "Bx", "CB"])
c.snapshot(0.0, {"A": 5, "A2": 0, "B": 0, "Bx": 9, "CB": 0})
c.phase = "SERVE"
d = c.decide(0.0)
c.snapshot(30.0, {"A": 5, "A2": 0, "B": 0, "Bx": 9, "CB": 0})
c.end_phase(30.0)
check("served head still occupied -> reset to now", c.queue_start[d.leader], 30.0)
# Bx conflicts with A, so it was NOT in the served group and must keep its
# original timestamp — that is what makes it win the next round.
check("unserved head keeps its original queueStart", c.queue_start["Bx"], 0.0)
check("Bx was indeed excluded from the group", "Bx" not in d.group, True)

print("\nS4 phases 2-3 — the correction Kimmy made on 10 Sep")
# Kimmy's first S4 ran phase 2 to its full 23.74 s and picked CB by Q2 at
# phase 3. Recomputed, anti-starvation cuts phase 2 at T=55.67 and CB is then
# selected by the Q1 override, not by Q2. This asserts the corrected version.
c = soda.Soda(HEADS, order=["A", "B", "Bx", "CB"])
c.count = {"A": 12, "A2": 0, "B": 5, "Bx": 9, "CB": 7}
c.last_count = dict(c.count)
c.queue_start = {"A": 0.0, "A2": float("inf"), "B": 0.0, "Bx": 0.0, "CB": 0.0}
c.phase = "SERVE"
p1 = c.decide(0.0)
check("P1 leader = A (all tied -> phase order)", p1.leader, "A")
check("P1 group", sorted(p1.group), ["A", "A2", "B"])
check("P1 G = 30.67 from A=12", p1.green, 30.67)

c.snapshot(30.67, {"A": 10, "A2": 0, "B": 4, "Bx": 9, "CB": 7})
c.end_phase(30.67)
p2 = c.decide(30.67)
check("P2 leader = Bx (Bx,CB tied at 0.0 -> phase order)", p2.leader, "Bx")
check("P2 group = B + Bx", sorted(p2.group), ["B", "Bx"])
check("P2 G = 23.74 from Bx=9", p2.green, 23.74)
check("P2 cuts A and A2", sorted(p2.cut), ["A", "A2"])

# green opens after 3 s amber + 2 s all-red; the interrupt is checked at
# elapsed 20 s, i.e. T = 35.67 + 20 = 55.67, when CB has waited 55.67 s.
check("anti-starvation cuts P2 at 55.67",
      c.should_end_early(55.67, 35.67 + p2.green), "ANTI_STARVATION")

c.snapshot(55.67, {"A": 10, "A2": 0, "B": 3, "Bx": 8, "CB": 7})
c.end_phase(55.67)
p3 = c.decide(55.67)
check("P3 selected by the Q1 override, not Q2", p3.reason, "STARVATION")
check("P3 leader = CB", p3.leader, "CB")
check("P3 group = CB alone", sorted(p3.group), ["CB"])
check("P3 G = 19.12 from CB=7", p3.green, 19.12)
check("P3 cuts B and Bx", sorted(p3.cut), ["B", "Bx"])

print("\nS5 — a lane that clears early holds green anyway")
c = soda.Soda(HEADS, order=["A", "B", "Bx", "CB"])
c.count = {"A": 0, "A2": 0, "B": 10, "Bx": 3, "CB": 0}
c.last_count = dict(c.count)
c.queue_start = {"A": inf, "A2": inf, "B": 0.0, "Bx": 0.0, "CB": inf}
c.phase = "SERVE"
d = c.decide(0.0)
check("leader = B (B,Bx tied -> phase order)", d.leader, "B")
check("group = B + Bx", sorted(d.group), ["B", "Bx"])
check("G = 26.05 from B=10, not from Bx=3", d.green, 26.05)
# Bx empties at 2.95 + 3 x 2.31 = 9.88, but B is occupied to the end, so the
# group never reads all-zero and gap-out cannot fire.
for t, b, bx in [(10.0, 6, 0), (12.0, 5, 0), (14.0, 4, 0), (16.0, 3, 0)]:
    c.snapshot(t, {"A": 0, "A2": 0, "B": b, "Bx": bx, "CB": 0})
check("gap-out never fires while B is occupied",
      c.should_end_early(16.0, 26.05), None)
check("idle green >= 16 s", round(26.05 - (2.95 + 3 * 2.31), 2), 16.17)

print("\nD2 — gap-out fires on the SECOND zero reading, not the third")
c = soda.Soda(["B"], order=["B"])
c.green = {"B"}
c.green_since = 0.0
fired = []
for t, n in [(2.0, 3), (4.0, 0), (6.0, 0)]:
    c.snapshot(t, {"B": n})
    fired.append(c.should_end_early(t, 30.0))
check("no gap-out on the first zero", fired[1], None)
check("gap-out on the second zero (4 s after last non-zero)", fired[2], "GAP_OUT")

print("\n" + ("ALL PASS" if ok else "FAILURES ABOVE"))
raise SystemExit(0 if ok else 1)
