"""
demo_soda.py - watch the SODA algorithm decide, one step at a time.

Type which lanes get cars, in the order they arrive. The script runs the real
controller from Tests/soda.py and prints what it is thinking at every decision:
the four questions it asks, why each lane joins or does not, and how the green
time is worked out.

    python3 tools/demo_soda.py                     # type arrivals, or Enter for an example
    python3 tools/demo_soda.py Bx 2 A 8 B 4 CB 5   # arrivals straight from the command line
    python3 tools/demo_soda.py --slow              # pause between steps, for a live audience

Each arrival is "<lane> <cars>". Arrivals come one second apart in the order
given. Add "@<second>" to set the time yourself, e.g. "A 5@10".

Lanes with a traffic light: A, A2, B, Bx, CB.
Ax and CA have no light, so the algorithm does not control them.
"""
import os
import sys
import time
from collections import deque

sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Tests"))
import soda  # noqa: E402  - the reference controller; every decision below comes from it

soda.SAMPLE_SEC = 1.0          # the simulation reads the camera once a second
LANES = ["A", "A2", "B", "Bx", "CB"]
ORDER = ["A", "A2", "B", "Bx", "CB"]   # tie-break: the junction's normal phase order
CLEARANCE = soda.YELLOW + soda.ALL_RED  # amber + all-red paid by lanes that turn red
LIMIT = 900                             # safety stop, in seconds

SLOW = "--slow" in sys.argv
COLOR = sys.stdout.isatty() and "--no-color" not in sys.argv


def paint(text, code):
    return f"\033[{code}m{text}\033[0m" if COLOR else text


def green(t): return paint(t, "32;1")
def amber(t): return paint(t, "33;1")
def red(t):   return paint(t, "31;1")
def bold(t):  return paint(t, "1")
def dim(t):   return paint(t, "2")


def say(line=""):
    print(line)
    if SLOW and line.strip():
        time.sleep(0.35)


# ---------------------------------------------------------------- input

EXAMPLE = [("Bx", 2, None), ("A", 8, None), ("B", 4, None), ("CB", 5, None)]


def parse(tokens):
    """['Bx','3','A','8@5'] -> [('Bx',3,None), ('A',8,5)]"""
    out = []
    if len(tokens) % 2:
        raise ValueError("every lane needs a number of cars, e.g. 'A 5'")
    for lane, n in zip(tokens[::2], tokens[1::2]):
        at = None
        if "@" in n:
            n, at = n.split("@", 1)
            at = int(at)
        lane_key = next((l for l in LANES if l.lower() == lane.lower()), None)
        if lane_key is None:
            if lane.lower() in ("ax", "ca"):
                raise ValueError(f"{lane} has no traffic light, so the algorithm does not control it")
            raise ValueError(f"unknown lane '{lane}' - use one of {', '.join(LANES)}")
        cars = int(n)
        if not 1 <= cars <= 40:
            raise ValueError(f"{lane}: cars must be between 1 and 40")
        out.append((lane_key, cars, at))
    return out


def read_arrivals():
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    if args:
        return parse(args)
    print(bold("Enter arrivals in the order they arrive, one per line:  <lane> <cars>"))
    print(dim(f"  lanes: {', '.join(LANES)}   ·   add @<second> to set the time, e.g. 'A 5@10'"))
    print(dim("  press Enter on an empty line to run  ·  Enter straight away for an example"))
    got = []
    while True:
        try:
            line = input("  > ").strip()
        except EOFError:
            break
        if not line:
            break
        try:
            got += parse(line.split())
        except ValueError as e:
            print(red(f"    {e}"))
    return got or EXAMPLE


def schedule(arrivals):
    """Give every arrival a time: one second after the previous, unless set."""
    timed, t = [], 0
    for lane, cars, at in arrivals:
        if at is not None:
            t = at
        timed.append((t, lane, cars))
        t += 1
    return sorted(timed, key=lambda x: x[0])


# ---------------------------------------------------------------- narration

def explain(c, t, d):
    """Print the four questions for the decision soda just made.

    The reasoning is rebuilt from the same state soda read, then checked
    against soda's actual choice - so the explanation cannot drift from
    what the algorithm really did.
    """
    say()
    say(bold(f"  ── t={t:>3}s  DECISION " + "─" * 46))
    waiting = [h for h in LANES if c.count[h] > 0]
    if waiting:
        parts = [f"{h} {c.count[h]} car{'s' if c.count[h] != 1 else ''} "
                 f"{dim(f'(queue since t={c.queue_start[h]:.0f}s, waited {t - c.queue_start[h]:.0f}s)')}"
                 for h in sorted(waiting, key=lambda h: (c.queue_start[h], ORDER.index(h)))]
        say("  Waiting:  " + "\n            ".join(parts))

    # Q1
    starving = [h for h in waiting if t - c.queue_start[h] >= soda.STARVATION_SEC]
    if starving:
        say(f"  {bold('Q1')} Anyone close to starving (waited >= {soda.STARVATION_SEC:.2f}s)?  "
            + red("Yes: " + ", ".join(starving)) + f"  -> {bold(d.leader)} goes first")
    else:
        say(f"  {bold('Q1')} Anyone close to starving (waited >= {soda.STARVATION_SEC:.2f}s)?  No.")

    # Q2
    if not starving:
        ranked = sorted(waiting, key=lambda h: (c.queue_start[h], ORDER.index(h)))
        say(f"  {bold('Q2')} Whose queue started first?   "
            + "  ".join(f"{h} t={c.queue_start[h]:.0f}" for h in ranked)
            + f"   ->  {bold(ranked[0])} leads")
        if len(ranked) > 1 and c.queue_start[ranked[0]] == c.queue_start[ranked[1]]:
            say(dim(f"     (a tie - {ranked[0]} and {ranked[1]} were first seen in the same reading, "
                    "so the junction's normal order A, B, Bx, CB decides)"))
        elif len(ranked) > 1 and c.count[ranked[0]] < max(c.count[h] for h in ranked[1:]):
            say(dim(f"     (not the longest queue - {ranked[0]} was simply there first; "
                    "that is what stops a quiet lane waiting forever)"))
        assert ranked[0] == d.leader, "narration disagrees with soda's Q2"

    # Q3
    say(f"  {bold('Q3')} Who can go at the same time as {d.leader}?")
    group = {d.leader}
    others = sorted([h for h in LANES if h != d.leader and c.count[h] >= 1],
                    key=lambda h: (c.queue_start[h], ORDER.index(h)))
    for h in others:
        clash = [g for g in sorted(group) if soda.conflicts(h, g)]
        if clash:
            say(f"       {h:<3} {red('no')}  - clashes with {', '.join(clash)}")
        else:
            group.add(h)
            say(f"       {h:<3} {green('yes')} - crosses nobody already going")
    for a, b in soda.FORCED_TOGETHER:
        for x, y in ((a, b), (b, a)):
            if x in group and y not in group:
                group.add(y)
                say(f"       {y:<3} {green('yes')} - same physical light as {x}")
    for h in LANES:
        if h not in group and c.count[h] == 0 and h not in others:
            say(dim(f"       {h:<3} -    no cars"))
    assert group == d.group, "narration disagrees with soda's Q3"
    say(f"       -> green group: {bold(' + '.join(sorted(d.group, key=ORDER.index)))}")

    # Q4
    crit = max(d.group, key=lambda h: c.count[h])
    n = c.count[crit]
    raw = soda.STARTUP_LOST_TIME + n * soda.SATURATION_HEADWAY
    note = ""
    if raw < soda.G_MIN:
        note = dim(f"  (raised to the {soda.G_MIN:.0f}s minimum)")
    elif raw > soda.G_MAX:
        note = dim(f"  (capped at the {soda.G_MAX:.0f}s maximum)")
    say(f"  {bold('Q4')} How long?   longest queue in the group is {crit}, {n} car{'s' if n != 1 else ''}")
    say(f"       green = {soda.STARTUP_LOST_TIME} + {n} x {soda.SATURATION_HEADWAY} "
        f"= {raw:.1f}s  ->  {bold(f'{d.green:.1f}s')}{note}")


# ---------------------------------------------------------------- the run

def run(arrivals):
    timed = schedule(arrivals)
    c = soda.Soda(LANES, order=ORDER)
    queue = {h: deque() for h in LANES}          # arrival time of each waiting car
    waits = {h: [] for h in LANES}
    next_dep = {}                                 # lane -> when its next car crosses
    pending = list(timed)
    cur = None                                    # the decision being served
    open_at = green_end = None
    last_green = set()

    say()
    say(bold("SODA demo") + dim("  -  the real controller from Tests/soda.py, reading the camera every 1 s"))
    say(dim("Arrivals: " + "   ".join(f"t={t}s {lane}+{n}" for t, lane, n in timed)))

    t = 0
    while t <= LIMIT:
        # 1. cars arrive
        while pending and pending[0][0] <= t:
            _, lane, n = pending.pop(0)
            queue[lane].extend([t] * n)
            say(f"  t={t:>3}s  {amber(lane)} +{n} car{'s' if n != 1 else ''} arrive "
                f"{dim(f'({len(queue[lane])} waiting)')}")

        # 2. cars leave on green: first after the start-up loss, then one per headway
        if cur is not None and open_at is not None and t >= open_at:
            for lane in c.green:
                while queue[lane] and next_dep.get(lane, 1e9) <= t:
                    waits[lane].append(next_dep[lane] - queue[lane].popleft())
                    next_dep[lane] += soda.SATURATION_HEADWAY

        # 3. the camera reads every light
        c.snapshot(float(t), {h: len(queue[h]) for h in LANES})

        # 4. collect, if a round is starting
        if c.phase in ("ALL_RED_IDLE", "COLLECT"):
            was = c.phase
            why = c.collect_step(float(t))
            if was == "ALL_RED_IDLE" and c.phase == "COLLECT":
                say(f"  t={t:>3}s  {bold('COLLECT')} - waiting up to {soda.COLLECT_WINDOW_SEC:.0f}s to see who else comes")
            if why:
                reason = {"EARLY_HEADS": "two lights already have cars",
                          "EARLY_DEMAND": f"one light already has {soda.EARLY_EXIT_DEMAND}+ cars",
                          "CEILING": f"{soda.COLLECT_WINDOW_SEC:.0f}s are up"}[why]
                say(f"  t={t:>3}s  COLLECT ends early - {reason}" if why != "CEILING"
                    else f"  t={t:>3}s  COLLECT ends - {reason}")

        # 5. does the running green end now?  (only a green that is already open)
        if cur is not None and green_end is not None and t > open_at:
            ending = c.should_end_early(float(t), green_end)
            if ending is None and t >= green_end:
                ending = "TIME_UP"
            if ending:
                label = {"GAP_OUT": "gap-out, the group read empty twice",
                         "ANTI_STARVATION": f"anti-starvation, someone else waited {soda.ANTI_STARVE_OTHER_WAIT:.1f}s",
                         "MAX_GREEN": f"{soda.MAX_CONTINUOUS_GREEN:.0f}s maximum green",
                         "TIME_UP": "its green time is used up"}[ending]
                left = {h: len(queue[h]) for h in cur.group if queue[h]}
                tail = (", still waiting: " + ", ".join(f"{h} {n}" for h, n in left.items())) if left else ", all cleared"
                say(f"  t={t:>3}s  {red('green ends')} for {' + '.join(sorted(cur.group, key=ORDER.index))} "
                    f"- {label}{dim(tail)}")
                c.end_phase(float(t))
                last_green = set(cur.group)
                cur, green_end = None, None
                if not c.has_demand():
                    c.close_round(float(t))
                    last_green = set()
                    say(f"  t={t:>3}s  {red('ALL RED')} - nobody waiting")

        # 6. decide the next phase
        if cur is None and c.phase == "SERVE" and c.has_demand():
            cur = c.decide(float(t))
            explain(c, t, cur)
            if cur.cut:
                open_at = t + int(CLEARANCE)
                say(f"  t={t:>3}s  {amber('amber + all-red')} for "
                    f"{' + '.join(sorted(cur.cut, key=ORDER.index))} ({CLEARANCE:.0f}s) "
                    + dim("- only lanes turning red pay this"))
            else:
                open_at = t
                kept = cur.group & last_green
                if kept:
                    say(dim(f"         {' + '.join(sorted(kept, key=ORDER.index))} stays green - no amber needed"))

        # 7. the green opens, straight away or after its amber, with a fresh count
        if cur is not None and green_end is None and t == open_at:
            before = cur.green
            c.open_green(float(t))
            green_end = open_at + cur.green
            for lane in cur.group:
                if lane not in last_green:
                    next_dep[lane] = t + soda.STARTUP_LOST_TIME   # a standing queue pays the start-up loss
                elif next_dep.get(lane, 0) < t:
                    next_dep[lane] = float(t)                     # already moving: no fresh start-up loss
            if abs(cur.green - before) > 0.05:
                say(dim(f"         recounted as the light turned green: green is now {cur.green:.1f}s"))
            say(f"  t={t:>3}s  {green('GREEN')} {' + '.join(sorted(cur.group, key=ORDER.index))} "
                f"for {cur.green:.1f}s {dim(f'(until t={green_end:.1f}s)')}")

        if not pending and c.phase == "ALL_RED_IDLE" and not any(queue.values()):
            break
        t += 1

    summarise(t, timed, waits, queue)


def summarise(t, timed, waits, queue):
    say()
    say(bold("  ── Result " + "─" * 58))
    say(f"  {'lane':<5}{'cars':>6}{'first came':>12}{'avg wait':>11}{'longest':>10}")
    total, n = 0.0, 0
    for lane in LANES:
        cars = sum(k for _, l, k in timed if l == lane)
        if not cars:
            continue
        first = min(tt for tt, l, _ in timed if l == lane)
        w = waits[lane]
        avg = sum(w) / len(w) if w else float("nan")
        say(f"  {lane:<5}{cars:>6}{('t=' + str(first) + 's'):>12}{avg:>10.1f}s{(max(w) if w else 0):>9.1f}s"
            + (red(f"   {len(queue[lane])} never served") if queue[lane] else ""))
        total += sum(w)
        n += len(w)
    if n:
        say(f"  {'all':<5}{n:>6}{'':>12}{total / n:>10.1f}s")
    say(dim(f"  finished at t={t}s"))
    say()


if __name__ == "__main__":
    try:
        run(read_arrivals())
    except ValueError as e:
        print(red(f"error: {e}"))
        sys.exit(1)
