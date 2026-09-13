"""
soda.py — the SODA decision layer, on its own.

Sampled-Observation Demand-Adaptive control. Spec: SODA_SPEC_kimmy.md (Kimmy,
final version). This file is the controller ONLY — no video, no detector, no
Unity. It takes counts in and returns a phase decision. That separation is
deliberate: it makes the logic unit-testable against the spec's worked example,
and it gives Kimmy a reference implementation to check the C# against.

WHAT IT CAN SEE
    count[h] for each signal head, refreshed every SAMPLE_SEC seconds.
    Four to six integers. Nothing else — no vehicle IDs, no arrival times, no
    speeds. Every other quantity below is derived from those integers.

THE ONE IDEA
    Time decides WHO gets green (fairness).  queueStart
    Count decides HOW LONG they get it (efficiency).  count
    Swap them and either the small lane starves or the big lane never clears.
"""

from dataclasses import dataclass, field
from typing import Dict, List, Optional, Set

# ── measured at this junction, not HCM defaults (39 samples) ────────────────
STARTUP_LOST_TIME = 2.95      # l1, s — green starts before the first car moves
SATURATION_HEADWAY = 2.31     # h, s/veh — time per vehicle across the stop line
G_MIN, G_MAX = 8.0, 45.0

SAMPLE_SEC = 2.0              # the detector reports this often
YELLOW, ALL_RED = 3.0, 2.0

# maxWaitTime (45 s, LOS D) is the ONE setting. Both starvation thresholds are
# derived from it as ratios, exactly as the C# does (baseline §8.10 coupling a):
# hard-coding 42.75 / 40.5 agreed with Unity only while maxWaitTime stayed 45, and
# would have diverged silently the moment anyone edited it on either side.
MAX_WAIT_SEC = 45.0
STARVATION_RATIO = 0.95       # Q1 — fires slightly BEFORE the limit, so the
                              # guarantee is met rather than detected afterwards
ANTI_STARVE_RATIO = 0.90      # interrupt — a little earlier again, so a cut
                              # green has time to clear before the limit
STARVATION_SEC = STARVATION_RATIO * MAX_WAIT_SEC         # 42.75

# ── COLLECT — the only demand accumulator (v1.1 §5) ─────────────────────────
# A ceiling, not a target. More traffic exits EARLIER, never later: a burst of
# four vehicles on one head leaves at 0 s elapsed, a single vehicle waits 7 s.
COLLECT_WINDOW_SEC = 7.0
EARLY_EXIT_HEAD_COUNT = 2     # >= 2 heads have vehicles -> go now
EARLY_EXIT_DEMAND = 3         # any head has >= 3 vehicles -> go now

# The v1.0 counting window (wait 4 s if the leader had < 3 vehicles) was REMOVED
# in v1.1. It asked the same question as EARLY_EXIT_DEMAND with the same number,
# so a thin leader paid the full 7 s COLLECT and then another 4 s for an
# identical reason — one vehicle waited 11 s. It also ran off the snapshot clock
# and silently extended the outgoing green. See SODA_v1.1_AS_BUILT.md §7.

# Gap-out: the group must READ ZERO on GAP_OUT_SNAPSHOTS consecutive snapshots.
# It fires on the SECOND consecutive zero reading — 4 s after the last non-zero
# reading, 2 s after the first zero. One zero is not enough: a single missed
# detection must not end a phase.
#
# Counted in READINGS, not elapsed seconds. Measuring 4 s from the first zero
# would fire on the third zero instead (baseline Appendix A, D2 — this
# implementation had that bug until 10 Sep 2026). The 4 s is derived from
# 2 x snapshotInterval and is not independently settable.
GAP_OUT_SNAPSHOTS = 2
GAP_OUT_SEC = GAP_OUT_SNAPSHOTS * SAMPLE_SEC        # 4.0 s, for reporting only

ANTI_STARVE_MIN_GREEN = 20.0  # only interrupt a green that has run this long
ANTI_STARVE_OTHER_WAIT = ANTI_STARVE_RATIO * MAX_WAIT_SEC   # 40.5
MAX_CONTINUOUS_GREEN = 90.0

# A and A2 share one physical signal head, so they are always in the same group.
FORCED_TOGETHER = [("A", "A2")]

CONFLICTS = {
    frozenset(("A", "Bx")), frozenset(("A", "CB")),
    frozenset(("A2", "Bx")), frozenset(("A2", "CB")),
    frozenset(("B", "CB")), frozenset(("Bx", "CB")),
}


def conflicts(a: str, b: str) -> bool:
    return frozenset((a, b)) in CONFLICTS


def green_for(demand: int) -> float:
    """Q4. Green time for a single head with `demand` vehicles queued."""
    return min(max(STARTUP_LOST_TIME + demand * SATURATION_HEADWAY,
                   G_MIN), G_MAX)


@dataclass
class Decision:
    t: float
    group: Set[str]
    green: float                  # provisional until open_green() re-reads
    cut: Set[str]                 # heads dropping from green — these pay the
    leader: str                   # yellow + all-red, the ones staying do not
    reason: str                   # STARVATION | LONGEST_WAIT
    counts: Dict[str, int] = field(default_factory=dict)
    opened_at: Optional[float] = None

    def __str__(self):
        g = "+".join(sorted(self.group))
        c = "+".join(sorted(self.cut)) or "—"
        return (f"t={self.t:6.1f}  green {g:<10} {self.green:5.1f}s   "
                f"cut {c:<8} leader {self.leader:<3} ({self.reason})")


class Soda:
    """The controller. Feed it snapshots; ask it for a decision when a phase ends."""

    def __init__(self, heads: List[str], order: Optional[List[str]] = None,
                 gap_out_sec: float = GAP_OUT_SEC):
        self.heads = list(heads)
        # Tie-break order. Agreed with Kimmy: ties resolve by "Original", the
        # sequence the junction's existing fixed-time signal uses.
        self.order = list(order or heads)
        self.gap_out_sec = gap_out_sec

        self.count: Dict[str, int] = {h: 0 for h in heads}
        self.last_count: Dict[str, int] = {h: 0 for h in heads}
        # inf means "empty, not in the running". Never 0 — 0 would mean
        # "has been waiting since the start of time" and would win every round.
        self.queue_start: Dict[str, float] = {h: float("inf") for h in heads}

        self.green: Set[str] = set()
        self.green_since: Optional[float] = None
        self.empty_since: Optional[float] = None
        self.zero_streak = 0        # consecutive all-zero readings of the group
        self.starvation_events = 0
        self.log: List[Decision] = []

        # Round state. ALL_RED_IDLE -> COLLECT -> SERVE -> ALL_RED_IDLE
        self.phase = "ALL_RED_IDLE"
        self.collect_started: Optional[float] = None
        # The COLLECT ceiling is the ONE event not gated on a snapshot. It is a
        # timeout, not a decision from data, so it fires at exactly 7.0 s;
        # deferring it to the next snapshot would make it 8 s and contradict the
        # spec. Drivers must step to this time as well as to snapshot times.
        self.collect_deadline: Optional[float] = None
        self.collect_count = 0
        self.collect_duration = 0.0
        self.snapshot_index = 0

    # ── input ───────────────────────────────────────────────────────────────
    def snapshot(self, t: float, counts: Dict[str, int]) -> None:
        """One detector reading. Call every SAMPLE_SEC.

        Every head is read every time, including heads already known to hold
        traffic. v1.0 stopped sampling detected heads to save compute; v1.1
        cannot, because gap-out and the empty->infinity rule both need every
        head's count continuously.
        """
        self.snapshot_index += 1
        for h in self.heads:
            c = int(counts.get(h, 0))
            if self.last_count[h] == 0 and c > 0:
                self.queue_start[h] = t          # a queue begins forming
            elif c == 0:
                self.queue_start[h] = float("inf")
            self.last_count[h] = c
            self.count[h] = c

        # Gap-out tracking: the whole green group must read empty, and stay
        # empty for a confirmation period. One empty snapshot is not enough —
        # a missed detection would end the phase early.
        if self.green:
            if all(self.count[h] == 0 for h in self.green):
                self.zero_streak += 1
                if self.empty_since is None:
                    self.empty_since = t
            else:
                self.zero_streak = 0
                self.empty_since = None

    # ── the four questions ──────────────────────────────────────────────────
    def _waiting(self, h: str) -> float:
        qs = self.queue_start[h]
        return 0.0 if qs == float("inf") else self._now - qs

    def _pick_leader(self, t: float):
        """Q1 then Q2. Returns (head, reason) or (None, None)."""
        self._now = t
        active = [h for h in self.heads if self.count[h] > 0]
        if not active:
            return None, None

        # Q1 — is anyone close to starving? Fairness outranks efficiency, so
        # this is asked first and short-circuits Q2.
        starving = [h for h in active if self._waiting(h) >= STARVATION_SEC]
        if starving:
            # Longest wait leads. On an exact tie, the SAME rule as Q2: earliest
            # in ORIGINAL_SEQUENCE. The C# used >= here, which makes the LAST
            # head win — the opposite direction to its own Q2 tie-break. Two
            # contradictory tie-break rules in one controller cannot be
            # explained in the report; one can. (Baseline §8.3.)
            starving.sort(key=lambda h: (-self._waiting(h), self.order.index(h)))
            return starving[0], "STARVATION"

        # Q2 — who has waited longest? NOT who has the most vehicles. Choosing
        # by count would hand every phase to the busiest approach and the lane
        # that actually has a problem (Bx, v/c 1.51) would never get green.
        active.sort(key=lambda h: (self.queue_start[h], self.order.index(h)))
        return active[0], "LONGEST_WAIT"

    def _build_group(self, leader: str) -> Set[str]:
        """Q3 — who else can run at the same time?"""
        group = {leader}
        others = [h for h in self.heads
                  if h not in group and self.count[h] >= 1]
        others.sort(key=lambda h: (self.queue_start[h], self.order.index(h)))
        for h in others:
            if all(not conflicts(h, g) for g in group):
                group.add(h)
        # A and A2 share a head, so one implies the other regardless of count.
        for a, b in FORCED_TOGETHER:
            if a in group and b in self.heads:
                group.add(b)
            if b in group and a in self.heads:
                group.add(a)
        return group

    # ── the round ───────────────────────────────────────────────────────────
    def has_demand(self) -> bool:
        return any(self.count[h] > 0 for h in self.heads)

    def collect_step(self, t: float) -> Optional[str]:
        """Advance ALL_RED_IDLE / COLLECT. Returns a reason when SERVE begins.

        Call once per snapshot, before decide(). Returns None while still
        waiting; a string ("EARLY_HEADS" / "EARLY_DEMAND" / "CEILING") on the
        snapshot that ends COLLECT.
        """
        if self.phase == "ALL_RED_IDLE":
            if not self.has_demand():
                return None
            self.phase = "COLLECT"
            self.collect_started = t
            self.collect_deadline = t + COLLECT_WINDOW_SEC
            self.collect_count += 1
            # fall through: a burst can satisfy an early exit on this same
            # snapshot, giving CollectDuration = 0.0

        if self.phase != "COLLECT":
            return None

        occupied = sum(1 for h in self.heads if self.count[h] > 0)
        biggest = max(self.count[h] for h in self.heads)
        reason = None
        if occupied >= EARLY_EXIT_HEAD_COUNT:
            reason = "EARLY_HEADS"
        elif biggest >= EARLY_EXIT_DEMAND:
            reason = "EARLY_DEMAND"
        elif t >= self.collect_deadline:
            reason = "CEILING"
        if reason:
            self.collect_duration = t - self.collect_started
            self.phase = "SERVE"
            self.collect_deadline = None
        return reason

    def close_round(self, t: float) -> None:
        """No demand left anywhere — drop to all-red and wait."""
        self.phase = "ALL_RED_IDLE"
        self.green = set()
        self.green_since = None
        self.empty_since = None
        self.zero_streak = 0

    # ── output ──────────────────────────────────────────────────────────────
    def decide(self, t: float) -> Optional[Decision]:
        """Called when a phase ends. None means 'hold, ask again shortly'."""
        leader, reason = self._pick_leader(t)
        if leader is None:
            return None                      # nothing anywhere; stay as we are

        group = self._build_group(leader)
        demand = max(self.count[h] for h in group)
        g = green_for(demand)

        # Only heads leaving green pay yellow + all-red. Heads green in both the
        # old and new group run straight through — 5 s saved every time the
        # groups overlap, which fixed-time signalling can never do.
        cut = self.green - group

        d = Decision(t=t, group=group, green=g, cut=cut, leader=leader,
                     reason=reason, counts=dict(self.count))
        if reason == "STARVATION":
            self.starvation_events += 1

        self.green = set(group)
        self.green_since = t + (YELLOW + ALL_RED if cut else 0.0)
        self.empty_since = None
        self.zero_streak = 0
        self.log.append(d)
        return d

    def open_green(self, t: float) -> Optional[Decision]:
        """Re-read the counts as the green actually opens, and reset G.

        The group is chosen at the decision, but G is computed from a FRESH
        count once clearance is over — up to 5 s later (3 s amber + 2 s
        all-red). Using the decision-time count would size the green from a
        number that is already stale by a whole clearance, and at peak that is
        two or three vehicles.
        """
        if not self.log:
            return None
        d = self.log[-1]
        demand = max((self.count[h] for h in d.group), default=0)
        d.green = green_for(demand)
        d.counts = dict(self.count)
        d.opened_at = t
        return d

    def end_phase(self, t: float) -> None:
        """Green for the current group is over. Re-arm their queue clocks.

        Without this, a head that is never empty (Bx runs at v/c 1.51) keeps its
        original queueStart for ever and wins every subsequent round.
        """
        for h in self.green:
            self.queue_start[h] = t if self.count[h] > 0 else float("inf")
        self.empty_since = None
        self.zero_streak = 0

    # ── early termination ───────────────────────────────────────────────────
    def should_end_early(self, t: float, green_end: float):
        """Three ways a green ends before its allotted time. Returns a reason."""
        if not self.green or self.green_since is None:
            return None
        elapsed = t - self.green_since

        if self.zero_streak >= GAP_OUT_SNAPSHOTS:
            return "GAP_OUT"

        outside = [h for h in self.heads
                   if h not in self.green and self.count[h] > 0]
        if elapsed >= ANTI_STARVE_MIN_GREEN and outside:
            self._now = t
            if max(self._waiting(h) for h in outside) >= ANTI_STARVE_OTHER_WAIT:
                return "ANTI_STARVATION"

        if elapsed >= MAX_CONTINUOUS_GREEN and outside:
            return "MAX_GREEN"
        return None
