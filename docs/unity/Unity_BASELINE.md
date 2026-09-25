# Unity_BASELINE — the reference for the simulated vehicles

**ABAC digital twin · how the cars in `SampleScene` drive**

| | |
|---|---|
| **Document version** | **v1.0** — 25 September 2026 (see Revision history below) |
| **Owner** | Kimmy (Module C — Unity simulation) |
| **Scope** | Vehicle behaviour: `CarAgent.cs` (car following, stopping at the line, turning, the A2 lane change) and the per-lane settings `CarSpawner` hands to each car |
| **Not in scope** | The signal controllers and their rules — see `docs/soda/SODA_BASELINE.md`. The fixed signal plan and scene phase data — not to be changed from this document. |
| **Companion tests** | `HeadwayTest`, `Compare101Test`, `LaneBehaviourTest`, `GeometryTest`, `SceneAuditTest` (all in `bash Tests/harness/run_tests.sh`) |

> **Why a separate document.** `SODA_BASELINE.md` is shared with the detection team and records what the controller decides. This file records the traffic both controllers are measured on. A change here changes the results of **both** Fixed and Adaptive, so a batch run before a change here does not describe the model after it.
>
> **How to read it.** Tags as in `SODA_BASELINE.md`: **[code]** file and function · **[test]** the test that fails if the claim is false · **[measured]** the measurement, its size and date · **[tuned]** the value and who chose it.

---

## Revision history

Every edit to this file gets a row here. The change log in §7 tracks the model itself.

| Version | Date | What changed | Sections touched | By |
|---|---|---|---|---|
| v1.0 | 25 Sep 2026 | First version. Records the vehicle model as it stands after the 24 Sep fixes to the A2 lane change and to stopping at a red, with the history of earlier vehicle-model changes gathered from git in §7. | all | Kimmy |

---

## 1. Driver profiles

Every car draws one profile at spawn: 25 % Aggressive, 60 % Normal, 15 % Cautious [code `CarSpawner.RandomDriverType`, scene values 25 / 60].

| Profile | approachSpeed (m/s) | acceleration (m/s²) | decelRate (m/s²) | emergency × | reactionDelay (s) | stopOvershoot (m) | dilemma factor |
|---|---|---|---|---|---|---|---|
| Aggressive | 16.67 | 5.0 | 22 | 1.3 | 0.2 | +0.5 | 1.6 |
| Normal | 15.28 | 3.0 | 9 | 1.5 | 0.8 | 0 | 1.1 |
| Cautious | 13.89 | 2.5 | 3 | 1.8 | 1.5 | −1.0 | 0.7 |

[code `CarAgent.SetProfileParameters`] [test `GeometryTest`]

Every car is 4.0 m long and 2.15 m wide [code `CarAgent.carLength`, `CarAgent.BODY_WIDTH`] [test `LaneBehaviourTest` §3 checks `BODY_WIDTH` = `ABACSceneBuilder.CAR_WIDTH`].

## 2. Time step

Each frame is split into steps of at most 0.02 s, so decisions do not depend on the simulation speed [code `CarAgent.Update`, `MAX_STEP`] [test `GeometryTest` §10].

## 3. Speed control

One step chooses the lowest of the limits below.

### 3.1 Free road

A car accelerates towards `approachSpeed` at its profile's `acceleration`, in every branch [code `ComputeDesiredSpeed`].
*Until 24 Sep, a car further from its stop line than its braking distance was set straight to `approachSpeed`, so whenever car following let go the speed jumped to full in one step.*

### 3.2 Car following

The car ahead is the nearest car ahead of the nose within `lookAheadAngle` = 1.5 m sideways and at most `lookAheadDistance` = 50 m (less for turning lanes, see §4.1) [code `GetCarAhead`, `LookAheadLimit`].

- **Safety limit, applied at once:** `maxSafeSpeed = sqrt(v_lead² + 2 · decelRate · cushion)`, where cushion = gap − `minGap` (2 m).
- **Emergency:** when cushion ≤ 0, brake at `decelRate × emergency`.
- **Comfort limit:** keep a time gap, `v ≤ cushion / desiredTimeHeadway`, with `desiredTimeHeadway` = 1.9 s [tuned, 15 Sep, to match the measured 2.31 s/veh]. Since 24 Sep this limit is approached at `decelRate` rather than applied in one step. It is a comfort target, and applying it at once turned every cut-in into an instant stop.
- **Startup wave:** a stopped car waits until the car ahead is moving, then its own `reactionDelay` [code `DecideSpeed`].

[test `HeadwayTest`: queue discharge 2.28 s/veh against 2.31 measured; unchanged by the 24 Sep changes] [test `Compare101Test`: mean error 2.10 s against the reference clip, unchanged]

### 3.3 Stopping at a red

The stop is closed loop [code `ApproachStopSpeed`]:

- The car is never faster than `sqrt(2 · decelRate · d)`, where d is the distance to its stop point. On or above that curve it brakes at `decelRate`; if even that is not enough it slides past the line, as it always did.
- Below the curve it keeps rolling. If it is slower than `CREEP_SPEED` (3 m/s) it rolls up at that speed.
- A car standing within `CREEP_MIN_GAP` (1 m) of its stop point stays there.
- A fresh stop resets the reaction timer, so every green starts the full `reactionDelay`.

*Until 24 Sep the stop was open loop: brake at `decelRate` from wherever the car entered its braking zone, a zone sized for arriving at `approachSpeed`. A car arriving slower stopped (approachSpeed² − v²) / (2 · decelRate) short, then never moved again until green. That is 11 m for a Normal driver at 6 m/s, 26 m for a Cautious one. CB showed it most, because a CB queue follows cars turning at 6.7 m/s, so the car left when the amber comes is slow.*

[test `LaneBehaviourTest` §4: a 12-car CB queue cut off by the amber; the new head now waits 0.00–0.04 m from its stop point, where it used to wait 10.6 m (all Normal), 18.8 m (mixed) and 27.5 m (all Cautious) back]
[test `LaneBehaviourTest` §5: a car arriving at 5 m/s inside its braking zone reaches its stop point for every profile, where it used to stop 5.0 / 9.0 / 20.6 m short. Full-speed arrivals still stop within 0.3 m, and each profile keeps its reaction delay at green.]

The amber decision is unchanged: a car within `braking distance × dilemma factor` of its line carries on [code `OnLightStateChanged`].

### 3.4 Yielding lane Ax

Ax has no phase. It reads the Bx signal inverted: Bx green or amber means Ax stops at its own line [code `lightIsInverted`] [test `SharedLaneTest`].

## 4. Steering

### 4.1 Turning lanes (Ax, Bx, CB, CA)

- A turning car follows its waypoints after its stop line, turning at `steerRate` = 55°/s.
- Its speed is capped at `steerRate × turnRadius` = 6.7 m/s through the turn [code `Steer`, `TurnSpeedLimit`].
- It looks no further than its turn point [code `LookAheadLimit`].

### 4.2 A2 lane change into the shared lane

A2 spawns in lane A and moves into the outer lane it shares with Ax. The merge point is waypoint 0 (x = −30.5), and the move starts within `steerStartDistance` = 12 m of it [code `ABACSceneBuilder.A2_MERGE_X`, `A2_MERGE_DIST`]. From 24 Sep it works in four stages [code `UpdateLaneChange`, `FindMergeBlocker`, `SteerLaneChange`]:

1. **Plan** from 12 m + 50 m before the merge point. Cars in the target lane that the driver could stop for comfortably count as the car ahead, measured along the lane, not the nose.
2. **Check for a gap.**
   - A car ahead in the target lane must be one we could stop behind at our `decelRate`, keeping `minGap`.
   - A car behind must be able to slow to our speed at its own `decelRate` and still have `MERGE_ACCEPT_TIME_GAP` = 1 s of its travel in hand.
   - Without a green, our speed counts as 0, because we are about to stop at the line and the car behind must be able to stop too.
3. **No gap:** stay in lane A and hold at the end of the merge zone (the decision of 24 Sep, option (a)). The car must reach the shared lane to be counted by `ROI_Ax`, the only detector that sees A2.
   - Inside the zone it asks to be let in. A car behind in the target lane that can stop comfortably treats it as already in the lane and keeps back.
   - A car that cannot stop comfortably drives on, and the next one lets it in.
4. **Gap:** commit and steer onto the lane.
   - The heading may change no faster than `speed / turnRadius`, so the car never turns on the spot.
   - It aims 8 m ahead on the target lane, so it settles onto the centre.
   - The move is complete within 0.25 m of the centre and 2° of the lane direction.

While a car is part-way across, cars in both lanes judge it by body overlap instead of `lookAheadAngle`: half a body width plus the other car's reach across the path at the angle between them [code `GetCarAhead`].

*Until 24 Sep A2 simply turned towards waypoint 0, without looking at the target lane.*
- *Its nose swung away from the lane A car ahead (speed jumped to full), then found the Ax queue close up (instant stop).*
- *An Ax car alongside was invisible to it, and it to the Ax car, until the bodies overlapped.*
- *A stopped A2 car could pivot where it stood.*

[test `LaneBehaviourTest` §1: A2 joining a queue held by Bx beside a lane A queue. Worst acceleration was +371 m/s² and worst braking −524 m/s², against limits of +3 and −13.5; it now stays within both. It reaches the stop line centred in the shared lane (z = 6.27).]
[test `LaneBehaviourTest` §2: A2 against one Ax car, 3 light states × 9 driver pairs × 21 starting offsets. Overlaps went from 115 of 567 runs (worst 2.15 m, a full car width) to 0. Speed jumps went from 292 of 567 runs to 0. With no gap, the car waits at the end of the merge zone.]
[test `LaneBehaviourTest` §3: A2 never turns while stopped.]

## 5. Known limits (open)

| # | Limit | Evidence | Why it is left |
|---|---|---|---|
| 5.1 | A Cautious driver (brakes at 3, emergency 5.4) behind an Aggressive one braking at 22 is held by `maxSafeSpeed` harder than its own profile. | Straight lane A, at a red: −6.2 to −9.4 m/s² now, −54 to −528 before 24 Sep. The A2 case is bounded at −10 in `LaneBehaviourTest` §2 (worst −5.1). | `maxSafeSpeed` is the physical safety limit; slowing it would let cars overlap. |
| 5.2 | A car spawned at full speed behind a queue that has backed up to the spawn point brakes hard at once. | Scratch stress runs, 25 Sep, ABACEvent and MorningPeak, 2 seeds each: every step braking harder than 100 m/s² was within 25 m of a spawn point (x = −200 or −220, off screen). The milder excess elsewhere is 5.1. | A spawner behaviour that existed before; not part of this change. |
| 5.3 | The turning lanes (§4.1) still turn at `steerRate` at any speed, so a car stopped part-way through a turn can rotate in place. | Code reading. Only the A2 lane change has the speed-limited heading. | Out of scope for the 24 Sep report; no visible case found. |
| 5.4 | An A2 car without a gap holds lane A behind it while it waits. | Longest wait 41 s in ABACEvent (Ax every 3.2 s), under 1 s in MorningPeak; see §6. | Intended (option (a)): the car must reach `ROI_Ax` to be counted. |

## 6. Effect on results

**Batch v4 (19 Sep) ran on the model before the 24 Sep changes.** Its figures describe that model. Re-running is a team decision; until then, cite v4 as produced on the vehicle model of 19 Sep.

Indication only: a scratch stress run, not in the repository, 24–25 Sep. It drove lanes A, A2 and Ax for 30 simulated minutes on the fixed 106 s cycle, 2 seeds each, with the old and new `CarAgent` [measured]. Times are the mean seconds from spawn to crossing the stop line, seed 1 / seed 2.

| Scenario | | Lane A | A2 | Ax | Overlapping pairs | Longest A2 wait for a gap |
|---|---|---|---|---|---|---|
| ABACEvent (A2 every 120 s, Ax every 3.2 s) | before | 53.6 / 44.5 | 50.9 / 46.1 | 41.7 / 29.7 | 45 / 30 (worst 2.15 m) | — |
| | after | 57.9 / 58.9 | 55.7 / 69.4 | 39.7 / 45.0 | 0 / 0 | 41 s |
| MorningPeak (A2 every 348 s, Ax every 5.1 s) | before | 30.6 / 27.4 | 17.3 / 31.5 | 19.4 / 20.0 | 19 / 4 (worst 1.92 m) | — |
| | after | 28.7 / 29.0 | 17.9 / 19.7 | 17.8 / 17.7 | 0 / 0 | 0.6 s |

Reading:
- The old model let A2 and Ax cars drive through each other, so it **understated** how much the shared lane blocks. Where Ax is heavy (ABACEvent), A2 now occupies the lane for real and waits for gaps, and lanes A and A2 slow.
- Where Ax is moderate (MorningPeak), nothing measurable changes except that the overlaps are gone.

## 7. Change procedure and change log

1. Any change to vehicle behaviour gets a new version and a Revision history row here, plus a change-log row below.
2. Run `bash Tests/harness/run_tests.sh`; all suites must pass. `HeadwayTest` must stay within 2.31 ± 0.15 s/veh, or the model no longer discharges at the measured rate.
3. A behaviour change makes every batch run before it describe a different model.
4. Values serialised in the scene or the car prefab (`CarSpawner` fields, `Car.prefab`) are changed through the Inspector, saved, and checked with `SceneAuditTest`; the scene overrides code defaults. The tunables added on 24 Sep are `const` in `CarAgent` so the scene cannot override them.

| Date | Change | Kind | Evidence |
|---|---|---|---|
| 2026-09-10 | Motion sub-stepped at ≤ 0.02 s, so results do not depend on simulation speed | behaviour (engine) | `GeometryTest` §10 |
| 2026-09-15 | Standing-queue cars placed at rest, so they hold the red instead of sliding across the line (`0bda935`) | behaviour | `GeometryTest` |
| 2026-09-15 | Queue discharge calibrated: startup wave plus `desiredTimeHeadway` = 1.9 s; 0.72 → 2.28 s/veh against 2.31 measured (`fca305c`) | behaviour | `HeadwayTest` |
| 2026-09-24 | Free-road acceleration limited to the profile's rate; comfort time gap approached at `decelRate`; red stop made closed loop, with roll-up and reaction-timer reset; A2 lane change rebuilt (planning, gap acceptance, hold at the end of the merge zone, being let in, speed-limited heading, body-overlap test while crossing) | behaviour — **after batch v4** | `LaneBehaviourTest` (new); `HeadwayTest` and `Compare101Test` unchanged |
