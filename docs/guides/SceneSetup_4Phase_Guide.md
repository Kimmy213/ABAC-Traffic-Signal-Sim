# Setting up the 4-phase scene - ABAC Traffic Simulation

**Unity 6 (6000.5.4f1), Built-In RP** - updated 20 August 2026

> **Shortcut:** to avoid placing everything by hand, skip to section 3 and run `ABAC > Build 4-Phase Scene`. One click does it.
> Sections 4-6 describe what the builder does, for checking or adjusting by hand.

---

## 1. The real junction layout (confirmed against the scene coordinates)

**Corrected from the old progress doc:** the ABAC entrance is to the **north (+Z)**, not the south.
Reading the real coordinates in `SampleScene.unity`: `Road_ABAC` runs from z=0 to z=200 and `SpawnPoint_C`
sits at z=200. So "east" and "west" were swapped in the old document, but **all the lane names and which
lanes have signals were correct**.

```
                        North (+Z)
                            |
                     +-------------+
                     |    ABAC     |
                     +------+------+
                 in  ^^     |     vv  out
           x=-4.0  x=-1.8   |   x=+1.5  x=+3.8
                    \   \   |   /   /
  ==========================+==========================  z=+6.5  Ax ->
  ==========================+==========================  z=+3.0  A  ->  (east, +X)
  --------------------------+--------------------------  z=+0.88 centreline
  ==========================+==========================  z=-1.2  Bx <-
  ==========================+==========================  z=-4.5  B  <-  (west, -X)
```

Thailand drives on the **left**, so A (eastbound) is on the northern half and B (westbound) on the southern half.

| Lane | Movement | Signal? | Why |
|---|---|---|---|
| **A** | eastbound (+X), straight through | yes | |
| **Ax** | splits from A, **turns left** into ABAC | no, free | ABAC is on A's left, so it turns into the nearest lane and crosses nobody |
| **B** | westbound (-X), straight through | yes | |
| **Bx** | splits from B, **turns right** into ABAC | yes | it has to cross the A stream |
| **CB** | leaves ABAC **turning right** to merge into B | yes | it must cross lane A to reach lane B |
| **CA** | leaves ABAC **turning left** to merge into A | no, free | merges into the nearest lane, crossing nobody |

### Conflict table (must never be green together)

|  | A | B | Bx | CB |
|---|---|---|---|---|
| **A** | - | compatible | conflict | conflict |
| **B** | compatible | - | compatible | conflict |
| **Bx** | conflict | compatible | - | conflict |
| **CB** | conflict | conflict | conflict | - |

**A point for the defence:** the system has **4 signals but 3 phases**, because A and B are opposing
through movements that do not cross and should be green together under HCM/ITE practice. Splitting them
into 4 phases would waste inter-green time and make the Fixed baseline weaker than reality - an unfair
comparison.

---

## 2. What changed from the old version

| Before | Now |
|---|---|
| the controller held `lightA/lightB/lightC` directly | everything is read from a single **`IntersectionConfig`** |
| 3 lanes A/B/C | 6 lanes **A, Ax, B, Bx, CB, CA** (4 signalised) |
| one phase = one lane | one phase = **several lanes at once**, if they do not conflict |
| no rule against collisions | a **conflict matrix** is enforced, with an immediate error on a bad setup |
| cars only drove straight | cars **turn along waypoints** |
| an 8 m road (2 lanes) | an 18 m road (4 lanes) plus a 14 m ABAC road (4 lanes) |

**New files:** `Assets/Scripts/IntersectionConfig.cs`, `Assets/Editor/ABACSceneBuilder.cs`, `Analysis/analyze_results.py`
**Rewritten:** `FixedController.cs`, `AdaptiveController.cs`, `ScenarioManager.cs`, `CarAgent.cs`, `CarSpawner.cs`, `ROIZone.cs`, `TrafficLight.cs`, `DataLogger.cs`

On first opening Unity after the change, **the Inspector references on the old controllers will be gone**
because fields were renamed. That is expected; the builder rewires everything.

---

## 3. The shortcut - let the builder do it (recommended)

1. **Commit to git first** (important - the builder rewrites the real scene)
2. Open Unity and wait for compilation with no red errors
3. Open `SampleScene`
4. Top menu -> **`ABAC` > `Build 4-Phase Scene`** -> press "Build"
5. The Console should show
   ```
   [IntersectionConfig] all checks passed - 6 lanes (4 signalised), 4 conflict pairs
   [ABACSceneBuilder] scene built
   ```
6. **Ctrl+S** to save the scene
7. Menu -> **`ABAC` > `Validate Scene`** to check again

**What the builder does:**

- widens the E-W road to 18 m and the ABAC road to 14 m, moving the guard huts clear of the road
- creates or moves `Light_A/B/Bx/CB` (renaming `Light_C` to `Light_CB` automatically)
- creates `StopLine_A/B/Bx/CB`
- creates ROIs for all 6 lanes, 100 m back from the stop line, with non-overlapping `guiSlot` values
- **removes the non-trigger collider from each ROI** (there used to be two stacked)
- creates 3 waypoints per turning lane (Ax, Bx, CB, CA)
- creates and wires spawners for all 6 lanes, finding existing ones by `laneName` rather than object name (some old names had corrupted characters)
- wires up `IntersectionConfig`, `FixedController` (120 s preset), `AdaptiveController` and `ScenarioManager` (5-scenario preset)

It is safe to press again - nothing is duplicated - and **Ctrl+Z undoes it**.

---

## 4. The coordinates the builder uses (for adjusting by hand)

### Lanes on the main road (along Z)
| Lane | z | spawn | rotationY |
|---|---|---|---|
| Ax | +6.5 | (-200, 0.5, 6.5) | 90 |
| A | +3.0 | (-200, 0.5, 3.0) | 90 |
| Bx | -1.2 | (+200, 0.5, -1.2) | -90 |
| B | -4.5 | (+200, 0.5, -4.5) | -90 |

### Lanes on the ABAC road (along X)
| Lane | x | spawn | rotationY |
|---|---|---|---|
| inbound from Ax | -4.0 | - | - |
| inbound from Bx | -1.8 | - | - |
| CB (outbound) | +1.5 | (1.5, 0.5, 200) | 180 |
| CA (outbound) | +3.8 | (3.8, 0.5, 200) | 180 |

### Stop lines
`StopLine_A` x=-10.5 - `StopLine_B` / `StopLine_Bx` x=+9.5 - `StopLine_CB` z=+10.9

### Waypoints (y = 0.5 throughout)
| Lane | WP1 | WP2 | WP3 |
|---|---|---|---|
| Ax | (-8.0, 6.5) | (-4.0, 14.0) | (-4.0, 90) |
| Bx | (2.0, 0.0) | (-1.8, 14.0) | (-1.8, 90) |
| CB | (1.5, 4.0) | (-6.0, -4.5) | (-90, -4.5) |
| CA | (3.8, 14.0) | (9.0, 3.0) | (90, 3.0) |

**The placement principle:** WP1 must sit **directly ahead of the car**, so it does not start drifting at
spawn; WP2 is the turn point; WP3 is a distant endpoint. A car always drives straight until past the stop
line before turning. The lanes without a stop line (Ax and CA) begin turning at spawn, which is why WP1
must be straight ahead of them.

---

## 5. Two hard rules for the free lanes (Ax, CA)

1. **Never set `Stop Line Target`** - the car will brake, stop and never move again, because it has no signal to turn green.
2. **Never set `Assigned Light`.**

`ABAC > Validate Scene` checks both and raises a red error if either is set.

> A free lane still needs an **ROI**, because we measure its waiting time to prove it really is near zero -
> a good sanity check for the report.

---

## 6. Controller settings

### FixedController - the 120 s preset

| Phase | green lanes | green |
|---|---|---|
| P1 MainThrough | A + B | 45 s |
| P2 EastRightTurn | B + Bx | 30 s |
| P3 ABACExit | CB | 30 s |

Yellow 3 s + all-red 2 s, three times = 15 s, giving exactly **120 s** (verifiable from the
`Print Cycle Length` context menu).

The verified timeline:
```
t=  0s +45s   A=G B=G Bx=R CB=R
t= 45s + 3s   A=Y B=G Bx=R CB=R   <- A goes yellow while B stays green, no flicker
t= 48s + 2s   A=R B=G Bx=R CB=R
t= 50s +30s   A=R B=G Bx=G CB=R   <- Bx joins after the all-red
t= 80s + 3s   A=R B=Y Bx=Y CB=R
t= 83s + 2s   A=R B=R Bx=R CB=R
t= 85s +30s   A=R B=R Bx=R CB=G
t=115s + 3s   A=R B=R Bx=R CB=Y
t=118s + 2s   A=R B=R Bx=R CB=R
t=120s        back to P1
```

### AdaptiveController - recommended values

| Field | Value | Note |
|---|---|---|
| G Min / G Max | 8 / 45 | unchanged |
| Startup Lost Time | 3 | HCM (the scene had 1; the builder does not touch it, so fix it by hand) |
| Saturation Headway | 2.5 | HCM |
| Max Continuous Green | 90 | new - stops one group holding green indefinitely |
| Enable Concurrent Phases | yes | new - allows A+B together |
| Counting Window | 4 | (the scene had 8) |
| Gap Out Delay | 3 | |
| Max Wait Time | 45 | |
| Min Green Before Interrupt | 20 | |

### ScenarioManager - settings for the real run

`Run Duration` = **1800** - `Repetitions Per Scenario` = **27** - `Warmup Duration` = **120** - `Simulation Speed` = **10** (see section 8)

| Scenario | A | Ax | B | Bx | CB | CA |
|---|---|---|---|---|---|---|
| MorningPeak | high | **high** | medium | **high** | veryLow | veryLow |
| EveningPeak | medium | low | high | low | **high** | **high** |
| MiddayNormal | low | low | low | low | low | low |
| ABACEvent | medium | **veryHigh** | medium | **veryHigh** | high | high |
| Night | veryLow on all six lanes | | | | | |

---

## 7. Manual test checklist (always before a real run)

Set `Repetitions Per Scenario = 1`, `Run Duration = 60`, `Warmup Duration = 10`, `Simulation Speed = 1`, then press Play.

- [ ] no red errors in the Console
- [ ] `ABAC > Validate Scene` passes entirely
- [ ] A and B do go green **together** (confirming concurrent phases)
- [ ] CB is **never** green alongside A, B or Bx - the most important check
- [ ] Bx is **never** green alongside A
- [ ] Ax vehicles really do turn left into ABAC rather than driving through
- [ ] Bx vehicles wait for the signal and then turn right into ABAC
- [ ] CB and CA leave ABAC into the correct side (CB into lane B, CA into lane A)
- [ ] no car drives in circles (circling means a waypoint is too far away or `Steer Rate` too low)
- [ ] all 6 ROI boxes appear and do not overlap
- [ ] waiting time for **Ax and CA is about 0**
- [ ] switching Fixed to Adaptive leaves no two groups stuck green at once
- [ ] both CSVs appear in `SimulationResults/`

---

## 8. Before launching 270 runs

### 8.1 Delete the old CSVs first
`summary_all_runs.csv` is appended to, so old test data mixes in.

> `DataLogger` now checks the header and, if the format differs, moves the old file to
> `summary_all_runs_OLD_<timestamp>.csv` automatically.
> (This project's own file really did have the problem - 18 of 22 rows mixing 7-column and 8-column forms.)

### 8.2 What warm-up is (worth writing up in the report)
Every run used to start on an empty road, so the first 10-20 vehicles never waited for a signal, pulling
the mean down and favouring Fixed. Traffic now runs for 120 seconds before the log is cleared and counting
begins, giving purely **steady-state** data - the standard approach in discrete-event simulation.

### 8.3 Validate timeScale (examiners like asking about this)
Total simulated time = 270 x (1800+120) = **144 simulated hours**.

| Time Scale | real time if the machine keeps up |
|---|---|
| 10x | about 14.4 h |
| 20x | about 7.2 h |
| 50x | about 2.9 h |

At high timeScale each frame's `Time.deltaTime` grows, so braking and acceleration in `CarAgent` drift.

**How to check:** run the same scenario on the same seed at 1x / 10x / 20x and compare `AvgWaitTime`. More
than about 5% apart means it is too fast and should be reduced. Do it once and record the result for the report.

### 8.4 Other points
- close the Scene view in Unity and leave only the Game view (much faster)
- stop the machine sleeping and run overnight
- commit to git before pressing run

---

## 9. Analysing the results

```bash
pip install pandas scipy matplotlib
cd Analysis
python analyze_results.py            # reads ../SimulationResults automatically
```

Produces, in `SimulationResults/analysis_output/`:

| File | Contents |
|---|---|
| `results_summary.txt` | every figure, plus a **ready-made sentence for the abstract** |
| `paired_table.csv` | the paired Fixed vs Adaptive table |
| `01_overall_comparison.png` | overall comparison plus boxplot |
| `02_by_scenario.png` | by scenario, with significance stars |
| `03_by_lane.png` | by lane (check that Ax and CA are near 0) |
| `04_paired_differences.png` | distribution of the paired differences |
| `05_throughput.png` | vehicles cleared |

**The script covers:** paired t-test, Wilcoxon (in case the data are not normal), Cohen's d, 95% CI,
Shapiro-Wilk for the assumption, and breakdowns by scenario and by lane.

**A caution about starvation when writing the report:**
`StarvationEvents` counts "how often the anti-starvation mechanism fired", not "how often a lane was
starved". Above 0 means the mechanism did its job, not that the system failed.
To claim *zero starvation*, use `MaxWaitTime` and show that no run exceeded the threshold - the script
reports that already.

---

## 10. Troubleshooting

| Symptom | Cause |
|---|---|
| the ROI always counts 0 | the Box Collider has no `Is Trigger`, or `laneName` differs between the ROI and the spawner |
| cars drive through instead of turning | `Path Waypoints` not set, or in the wrong order |
| cars drive in circles | `Steer Rate` too low, or a waypoint placed too far off the car's line |
| **free-lane cars sit forever** | a `Stop Line Target` was set on Ax or CA (never do this - see section 5) |
| cars drift off line at spawn | WP1 on a free lane is not directly ahead of the car |
| conflicting greens | a conflict pair is missing - run `Validate Configuration` |
| `NullReferenceException` on Play | `Config` on the controller is empty, or a lane has no ROI assigned |
| the light stays green and never changes | no vehicles in any other lane (rest-in-green is the correct behaviour) |
| Unity hangs on Play | the recursion bug, fixed in the new code. If it recurs, check for more than one `IntersectionConfig` |
| pandas cannot read the CSV | the file mixes schemas - the script skips them, but delete the old file |
| charts show boxes instead of letters | all chart text is English now; if it recurs, someone changed the text |

---

## 11. What comes next

1. Done - the 4-phase code, the builder and the analysis script
2. To do - run `ABAC > Build 4-Phase Scene` and work through the checklist in section 7
3. To do - validate timeScale (section 8.3) and record the result for the report
4. To do - delete the old CSVs, then run 270 runs overnight
5. To do - `python analyze_results.py` and put the charts and figures into the report
6. To do, time permitting - turn off `Enable Concurrent Phases` and run a comparison, giving a three-system ablation study
7. Exhibition 25 Sep - defence 14-16 Oct
