# Bug report C1 - Unity did not follow the specification for resetting `queueStart` at the end of a phase

| | |
|---|---|
| **Number** | C1 (with C2 and C3, which are measurement-only) |
| **Date** | 11 September 2026 |
| **Reported by** | Kimmy (Badin) |
| **Type** | **an implementation bug on the Unity side** - the C# does not match the frozen specification. Not an algorithm change. |
| **Does the specification change?** | **No.** `SODA_BASELINE.md` section 4 is already correct. |
| **Does `soda.py` change?** | **No.** Itim's `end_phase()` is already correct. |
| **Does section 9.1 require a vote?** | **No**, because no rule changes; the code is merely brought into line with the existing rule. |
| **Does the batch need re-running?** | **Yes**, per section 9.3, because the simulated behaviour changes. Batch v1 can no longer be cited. |
| **Code status** | **Merged 11 Sep 2026.** All 9 test suites pass. |
| **Reviewed by Itim** | Yes, with no objections, and he reproduced the cause himself with `test_queueclock.py` against `soda.py`. |

---

## 0. The shortest possible summary

Section 4 of `SODA_BASELINE.md` says:

```
phase end   for each served head: lastCount>0 -> queueStart = now, else infinity
```

**"for each served head"** means every head that has just had green. The Unity code applied it only to
heads **dropping out of the group and turning red**. A head that continued across phases therefore held
a stale `queueStart` indefinitely, so the system believed the head that was permanently green had waited
longest, and it won both Q1 and Q2 over and over. The genuinely starved head never got a turn.

What is striking is that **the baseline predicted this exactly**, on the next line of section 4:

> **The reset at phase end is load-bearing.** Without it, a head that never empties keeps its original `queueStart` forever and wins Q2 every time - every other head is locked out.

We wrote the rule correctly, explained it correctly and even had a test - but the code was incomplete, and
the existing test (`SodaTest` section 1) only exercised the case where the head drops to red, which the
code already handled. So it passed throughout.

---

## 1. Batch v1 results (context)

216 runs (4 scenarios x 27 reps x 2 controllers), finished 11 Sep at 14:23, taking 11 h 31 min at
`simulationSpeed = 10`.

Mean wait per vehicle in seconds, paired on the same seed, n = 27 per scenario.

| Scenario | Fixed | Adaptive | difference | t | Adaptive wins |
|---|---|---|---|---|---|
| MorningPeak | 22.77 +/- 0.97 | 14.11 +/- 0.61 | +8.66 (38.0%) | +38.2 | 27/27 |
| MiddayNormal | 22.66 +/- 0.89 | 14.78 +/- 0.75 | +7.88 (34.8%) | +41.8 | 27/27 |
| ABACEvent | 93.35 +/- 6.45 | 67.26 +/- 14.13 | +26.10 (28.0%) | +8.3 | 25/27 |
| EveningPeak | 25.40 +/- 0.72 | 25.95 +/- 1.57 | -0.55 (-2.2%) | -1.8 **not significant** | 11/27 |
| **Overall** | **41.05** | **30.52** | **+25.6%** | | |

**Never write that EveningPeak "lost".** t = -1.8 against a critical value of 2.056 at df = 26 does not
reach significance. The correct wording is "the means do not differ significantly". Write "lost" and an
examiner who checks the arithmetic will catch it (Itim's warning, 11 Sep).

The means look good, but the **tail** of the distribution reveals the problem. All lanes, all scenarios,
27 reps:

| | Fixed p95 / max | Adaptive p95 / max |
|---|---|---|
| Midday, all lanes | worst 88.2 / 94.2 | worst 49.4 / **85.8** - better on every measure |
| Morning Bx | 89.2 / 100.4 | 58.7 / **131.8** - worse |
| Evening CB | 78.5 / 84.2 | 148.0 / **344.4** - far worse |
| Evening Bx | 90.8 / 103.9 | 123.4 / **264.7** - far worse |
| Evening A | 70.5 / 78.6 | 78.9 / **192.8** - far worse |

Fixed has a clear tail ceiling of about 100 seconds on every lane, because a 106 second cycle guarantees
every lane its green. Adaptive's tail runs out to 344 seconds, with `maxWaitTime` set to 45. The
anti-starvation mechanism should have prevented exactly this.

---

## 2. Evidence from the real log

In `phases_Adaptive_EveningPeak_Rep17_20260911_062549.csv`, from t = 1104.6 to t = 1470.2 there are
**365.5 seconds in which H_CB never once got a green**, with vehicles waiting throughout.

```
 start   given   used  group           end reason
1104.6   21.43  20.00  CB              starvation     <- CB's last green
1129.7   37.60  19.98  A+B+A2          starvation
1149.6    9.88   9.91  A+B+A2          completed
1159.5   12.19  12.19  A+B+A2          completed
1171.7    9.88   9.89  A+B+A2          completed
1181.6   12.19  12.20  A+B+A2          completed
1193.8    9.88   9.92  A+B+A2          completed
1203.8    9.88   9.89  A+B+A2          completed
1213.6   14.50   5.97  A+B+A2          max-green
1224.7   39.91  19.95  Bx+B            starvation
1244.6    9.88   9.89  B+Bx            completed
1259.6   30.67  19.94  B+A+A2          starvation
1284.6   14.50  14.55  B+Bx            completed
1304.2   23.74  19.98  B+A+A2          starvation
1324.2   12.19  12.25  B+A+A2          completed
1341.5   12.19  12.25  B+Bx            completed
1358.8   19.12  19.16  B+A+A2          completed
1378.0   16.81  16.81  B+A+A2          completed
1399.9   26.05  19.92  B+Bx            starvation
1424.9   28.36  19.95  B+A+A2          starvation
1444.9   12.19  12.27  B+A+A2          completed
1457.1    8.00   7.97  B+A+A2          completed
1470.2   42.22  20.13  CB              starvation     <- 365.5 seconds later
```

Three things to notice:

1. **H_B is in the green group for 24 consecutive phases and never leaves it** across those 365 seconds.
2. Seven phases end with reason `starvation`, so the mechanism fired - but not once did it pick CB.
3. The CB vehicles stuck there clear at about t = 1473-1483, having waited between 280 and **344 seconds**.

Confirmation from another angle: Rep17 has 99 phases but logged **137** `StarvationEvents`, more than the
phase count. (The first draft said 145, which was wrong - 145 is the *median across all 27 EveningPeak
runs*, a different quantity. Itim caught it on 11 Sep.) Per baseline note L13 this counter double-counts
each event (interrupt plus override), so 137 is about **68 real events in 99 phases**, which is still very
high, because H_B met the "starving" condition on nearly every decision while being the head that was
green. The `StarvationEvents` column in batch v1 therefore does not measure real starvation.

### The main evidence - all 27 runs, not Rep17 alone

Rep17 at 344 seconds is a single case that an examiner could dismiss as an outlier. Look at the whole
scenario instead (Itim's suggestion).

| EveningPeak - `MaxWaitTime` per run | min | median | max |
|---|---:|---:|---:|
| Adaptive | **126.5** | 188.3 | 344.4 |
| Fixed | 92.7 | 96.3 | **103.9** |

**Adaptive's best run (126.5 s) is worse than Fixed's worst (103.9 s) - it exceeds Fixed's ceiling in all
27 runs out of 27.** That is not arguable, and not an outlier.

Lane CB across all 27 runs: mean 61.7 against Fixed's 37.7, p95 148.0 against 78.5, max 344.4 against
84.2. Fixed wins decisively on the tail, because a 106 second cycle always guarantees CB its turn.

---

## 3. The cause in the code

In `Assets/Scripts/AdaptiveController.cs`, inside `ServeOnePhase` at the clearance stage:

```csharp
List<int> dropping = Except(activeGroup, nextGroup);
if (dropping.Count > 0)
{
    ...
    activeGroup = Except(activeGroup, dropping);
    ResetQueueStartFor(dropping);        // <-- only the heads turning red
    ...
}
```

`ResetQueueStartFor` is called with `dropping` alone. A head in both `activeGroup` and `nextGroup` - one
that keeps green for another phase - is not in `dropping` and is therefore never reset.

The consequence: for as long as a head stays in the green group, its `queueStart` remains frozen, and

- `WaitOf(h) = Time.time - h.queueStart` grows without limit
- `FindStarvingHead` (Q1) picks the oldest `queueStart`, so it picks this head every time
- `ChooseLeaderByQueueStart` (Q2) picks the oldest `queueStart`, so it picks this head every time
- `BuildCompatibleGroup` (Q3) builds the group around that leader, putting the head in the group again, and back to the first point

It is a self-locking loop: a head that wins the green once under saturated traffic keeps it until its own
lane empties.

### Three-way comparison - who was wrong

| Source | What it says | Correct? |
|---|---|---|
| `SODA_BASELINE.md` section 4 | `phase end - for each served head: lastCount>0 -> queueStart = now, else infinity` | Yes |
| Itim's `soda.py` `end_phase()` | `for h in self.green: self.queue_start[h] = t if self.count[h] > 0 else inf`, iterating the whole green group | Yes |
| Unity's `AdaptiveController.cs` | `ResetQueueStartFor(dropping)`, iterating only the heads turning red | **No** |

`soda.py` even documents the symptom in its docstring:

```python
def end_phase(self, t: float) -> None:
    """Green for the current group is over. Re-arm their queue clocks.

    Without this, a head that is never empty (Bx runs at v/c 1.51) keeps its
    original queueStart for ever and wins every subsequent round.
    """
    for h in self.green:
        self.queue_start[h] = t if self.count[h] > 0 else float("inf")
```

So Itim understood it correctly from the start, and the Python reference needs no change here. Unity alone
was wrong.

**Why `verify_traces.py` did not catch it:** the S1-S5 traces contain no case where the same head stays in
the green group across several phases without ever emptying. That needs a long saturated stretch like
EveningPeak before it shows.

### Why Midday and Morning were unaffected

`ResetQueueStartFor` sets `queueStart = Infinity` when a lane is empty, and `Snapshot()` sets it again when
the first vehicle arrives. Under unsaturated demand a head empties periodically and the clock clears
itself. The symptom appears only when a head **always has vehicles and never empties**, which is exactly
H_B's state during EveningPeak.

That explains why MiddayNormal beat Fixed on both mean and tail (max 85.8 against 94.2) while EveningPeak
did not - and why manual testing never showed it.

---

## 4. The fix (Unity only)

Reset the clock for heads continuing in the group, after turning green for the new phase.

```csharp
// captured before activeGroup is overwritten
List<int> servedLastPhase = new List<int>(activeGroup);
List<int> dropping = Except(activeGroup, nextGroup);
...
SetGroupState(turningOn, TrafficLight.LightState.Green);
activeGroup = new List<int>(nextGroup);

ResetContinuingHeads(servedLastPhase, dropping);   // the added line
```

```csharp
public void ResetContinuingHeads(List<int> servedLastPhase, List<int> dropping)
{
    ResetQueueStartFor(Except(servedLastPhase, dropping));
}
```

It is a public method so that the test can call the same sequence production does, rather than reproducing
the logic itself.

After the fix the code matches section 4 of the baseline and `end_phase()` in `soda.py` exactly. No new
rule is introduced.

**A gap in the old test, worth recording.** `SodaTest` section 1 really does test that "a head which never
empties must not lock out the others", but it creates the situation by dropping that head to red every
phase - the path the code already handled. The case where a head **stays in the group and never goes red**
was never tested. `QueueClockTest` closes that gap.

---

## 5. The test that confirms it

A new suite, `QueueClockTest` (`Test9.cs`), reproduces the group sequence from the log above: B in the
group every phase, CB never chosen, every lane holding 4 vehicles and never empty.

**Before the fix - 4 failures**

```
      H_A   queueStart=   1100  system thinks it waited    0s
      H_B   queueStart=    900  system thinks it waited  200s     <- permanently green, yet believed to have waited longest
      H_Bx  queueStart=   1080  system thinks it waited   20s
      H_CB  queueStart=   1000  system thinks it waited  100s     <- genuinely starved
  FAIL  a head whose green has just ended must have a queueStart no older than that phase's start
  FAIL  the genuinely starved head (CB) must have an older queue than the one just served (B)
      Q1 FindStarvingHead      -> H_B
  FAIL  Q1 must point at H_CB, not the permanently green H_B
      Q2 ChooseLeaderByQueueStart -> H_B
  FAIL  Q2 must choose H_CB as the next phase leader
```

**After the fix - all pass**

```
      H_A   queueStart=   1100  system thinks it waited    0s
      H_B   queueStart=   1100  system thinks it waited    0s
      H_Bx  queueStart=   1080  system thinks it waited   20s
      H_CB  queueStart=   1000  system thinks it waited  100s
  PASS  a head whose green has just ended must have a queueStart no older than that phase's start
  PASS  the genuinely starved head (CB) must have an older queue than the one just served (B)
      Q1 FindStarvingHead      -> H_CB
      Q2 ChooseLeaderByQueueStart -> H_CB
  PASS  ServeOnePhase still calls ResetContinuingHeads(servedLastPhase, dropping)
  PASS  phaseCounter++ comes before the early return
```

All 8 existing suites still pass after the fix; no regressions.

```
ConflictTest - FixedCycleTest - BatchMathTest - EtaTest - SharedLaneTest
GeometryTest - SceneAuditTest - SodaTest - QueueClockTest
########## ALL SUITES PASSED ##########
```

There is also a guard against regression: the test scans the source of `ServeOnePhase` to confirm it still
calls `ResetContinuingHeads`, so deleting it fails the test immediately - the same approach `SodaTest`
section 3 uses to scan for leftover tracker APIs.

---

## 6. Two further findings, measurement-only

### C2 - Fixed rows in `summary_all_runs.csv` carried the previous Adaptive run's numbers

`ScenarioManager.ApplyController` calls `adaptiveController.ForceStop()` before a Fixed run, but
`ForceStop()` did not clear the counters. They were cleared only in `RestartCycle()`, which is not called
for a Fixed run.

Checked: **107 of 108 Fixed rows have `CollectCount`, `CollectDuration`, `SkippedHeads`,
`QueueStartResets`, `TieBreaksApplied` and `TieBreaksExcludingBx` exactly equal to the Adaptive row above
them** (the first row is 0, since nothing had run before it).

It does not affect `AvgWaitTime`, `MaxWaitTime`, `TotalSpawned`, `VehiclesCleared`, `StarvationEvents` or
`IdleLaneSeconds`, which are the figures actually compared.

Fixed by clearing the counters in `ForceStop()` as well.

### C3 - `PhaseIndex` is 0 throughout every `phases_*.csv`

`phaseCounter++` sat inside `TraceBegin()` **after** the line `if (!showDecisionPanel) return;`. The
decision panel is off for every batch run, so the counter never advanced.

Fixed by moving `phaseCounter++` above the early return.

---

## 7. What to expect after the fix, and what is still unknown

Expected:

- the EveningPeak tail should shrink considerably; CB should not exceed about 100 seconds, because Q1 sees it within 42.75 seconds of it starting to wait
- EveningPeak should turn from -2.2% to positive
- `StarvationEvents` should fall to something close to reality

Unknown, stated plainly:

- **the MorningPeak and MiddayNormal means may drop somewhat**, because more fairness usually costs throughput. The 38% and 34.8% figures may not hold.
- the headline 25.6% will change; only a fresh run will say by how much

If the numbers fall a little but the tail disappears, that is the better result to defend. "The mean
improved but somebody waited 344 seconds" is a question the examiners will certainly ask and is hard to
answer; "the mean improved slightly less, and nobody waited over 100 seconds" is not.

---

## 7b. Reviewed by Itim - 11 September 2026

The cause is confirmed three ways: we read the code the same way, section 4 of the specification admits
only one reading, and **he proved it by running it**. He wrote `test_queueclock.py` for `soda.py`,
simulating permanent saturation with 6 vehicles held on every head and no lane ever emptying, over 10
phases.

```
leaders : A Bx CB A Bx CB A Bx CB A
served  : A 4, B 7, Bx 3, CB 3
```

B is the head that stays in the group across phases, the counterpart of Unity's H_B, but its `queueStart`
is reset to 30.0 at the end of the phase - precisely the line Unity was missing. The result is no lock-out;
every head gets its turn.

He re-ran all our figures and matched every cell: 38.0 / 34.8 / 28.0 / -2.2, and verified C2 at 107 rows
matching and 0 differing.

Three things he asked to be corrected before this goes into the report, all now corrected in this document:
Rep17's `StarvationEvents` figure (137, not 145), the word "lost" for EveningPeak (it must be "not
significantly different"), and changing the main evidence from Rep17 alone to all 27 runs.

**What he asked us to watch after the fix:** Q3 orders candidates by `queueStart`, and once the continuing
heads are reset they become the "newest" and are tried last when the group is built. That may make the
group change more often and pay the 5 second clearance more frequently. Compare **phases per run** and the
**`GreenUsed / GreenGiven`** ratio between v1 and v2. A large rise in phase count is where the lost
throughput went. Recorded as section 8.9 in the baseline.

---

## 8. Status of Itim's side (8.2 and 8.5 closed)

**8.2 closed.** The counting window is removed. Gap-out counts zero *readings* rather than seconds, firing
on the second zero reading, 4 s after the last non-zero one. COLLECT is implemented with both early exits.
The starvation tie now matches Q2. S1-S5 agree line for line, including when green opens and closes, not
just leader / group / G.

He also fixed something outside my D1-D3 list: **section 4.4 says G must re-read the count when green
opens**, but the old `soda.py` computed it from the count at decision time, which is 5 seconds stale
(3 yellow + 2 all-red) - 2 or 3 vehicles at peak. **I checked the C# and it was already correct:**
`CalculateGreenDuration` is called after `SetGroupState(...Green)` and `DemandOf` reads the ROI live at
that moment, not a stale `lastCount`.

**8.5 closed.** Headway from 39 samples in `DJI_20260714084627_0005_D.MP4`, Tuesday 14 Jul 2026, window
08:46-09:06. Detector 298/303 from `DJI_20260713084049_0002_D.MP4`, Monday 13 Jul 2026, window 08:40-09:05.
DJI filenames encode YYYYMMDDHHMMSS and `tally_*.json` records the date in its `source` field, so nobody
has to remember. Both are now in section 4.4 and section 7 L5 of the baseline.

**Still outstanding (8.10).** Itim's latest `soda.py`, `test_soda.py` and `test_queueclock.py` are not yet
in the repo, so the C#/Python pair for the C1 case is only half present. `Tests/verify_traces.py` has had
the `waited_once` / `waiting_until` lines removed as he asked, but has not been run against the new
`soda.py`.

---

## 9. Current status

C1, C2 and C3 are merged into `AdaptiveController.cs` as of 11 Sep. `SODA_BASELINE.md` is now v1.5, closing
sections 8.2, 8.5, 8.7 and 8.8 and opening 8.9 and 8.10. The next step is batch v2, 216 runs.

Every batch v1 file is kept in `SimulationResults/` as the "before" half of a before/after comparison,
which is valuable for the report because it shows how much the anti-starvation mechanism actually does
when it works against when it does not - but it must not be cited as a result.

---

## Appendix - related files

| File | Purpose |
|---|---|
| `SimulationResults/summary_all_runs.csv` | 216 rows, all the headline figures |
| `SimulationResults/phases_Adaptive_EveningPeak_Rep17_*.csv` | the log quoted in section 2 |
| `SimulationResults/detailed_*_EveningPeak_Rep*.csv` | per-vehicle waits, used for p95 and max by lane |
| `SimulationResults/batch_summary_20260911_142347.txt` | the summary ScenarioManager writes itself |
| `Tests/QueueClockTest.cs` | the new test, not yet committed |
