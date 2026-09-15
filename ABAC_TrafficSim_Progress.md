# ABAC Traffic Signal Simulation - Project Status

**Last updated:** 2 September 2026 (round 9 - SP1_HANDOFF_v2: 106 s cycle, measured saturation and startup values, shared lane 5.4% plus late merge)
**Repository:** github.com/Kimmy213/ABAC-Traffic-Signal-Sim (private)
**Engine:** Unity 6 (6000.5.4f1), Built-In Render Pipeline

---

## Round 9 (2 Sep) - updated to SP1_HANDOFF_v2 and SP1_REPORT_MATERIAL

Itim sent two documents that **supersede the original handoff and replies #1-#4**. Every parameter in them
is measured; none is guessed.

### Values changed immediately

1. **Bx green 21 -> 20 s, cycle 107 -> 106 s.** Itim supplied the full stage table, which matches our preset
   at every point (`0-40 A+B` / `45-65 Bx+B` / `70-101 CB`), and specified 20 s because a stopwatch gave
   exactly 20.0. The +/-3 s spread does not change the conclusion: Bx is over capacity throughout
   (v/c 1.21 at 23 s, **1.51** at 20 s).
2. **Saturation headway 2.5 -> 2.31 s/veh and startup lost time 3.0 -> 2.95 s.** These are measured at this
   junction (5 fully queued greens, 39 samples), no longer HCM defaults. This is a selling point at the
   defence: examiners ask where numbers come from, and the answer is "we measured them here". Saturation
   flow is 1,558 veh/hr/lane against HCM's 1,800, and the startup lost time is 1.5 times HCM - reasonable,
   because motorcycles filter to the head of the queue and have to clear first.
   **It costs 2.95 s of every phase, which is 15% of a Bx phase lasting only 20 s.**
3. **New arrival rates for every lane** - morning A=18.8 / Ax=5.1 / B=9.0 / Bx=9.6; midday A=29.0 / Ax=9.7 / B=20.5 / Bx=21.4.
4. **EveningPeak now has real CB/CA values** (8.4 / 13.4 s), previously pure guesswork. CB:CA = 61:39.
5. **A2_SHARE 18% -> 5.4%** - from a value *inferred backwards* from a blocking rate to one **counted
   directly** (clip A_morning: 56 through vehicles, 53 in the inner lane, only 3 in the shared lane). Far
   more reliable.

### A contradiction in the documents - needs Itim to confirm

The example code in section 4 ends the `T8 - MorningPeak` line with `8.4f, 13.4f`, but the table just above
it states that those two CB/CA values were measured **in the evening**, and section 6 item 8 repeats that
if MorningPeak means 08:15-09:00 then CB and CA are still about 0.

**Decision:** keep MorningPeak as `CLOSED` and put 8.4/13.4 into EveningPeak. To be checked with Itim.

### Late merge - the main work of this round

Itim warned outright that this is "the detail people miss most easily", and he is right.

Previously A2 spawned in the shared lane 220 m out and drove the whole way, so vehicles spread **through
the queue**, giving "frequent but mild blocking". In reality those 3 vehicles change lane with only about
five car lengths left, cutting in **at the head of the queue**, where one of them stops every turning
vehicle behind it for the whole red - "**rare, but nearly worst-case every time**".

The mean is the same; the queue dynamics are entirely different - and it is the severe events that push the
queue back to the next junction.

**Important:** cutting A2_SHARE to 5.4% *without* the late merge would make the model show **far less
blocking than reality** (the old 18% happened to compensate). The two changes must be made together; never
leave it half done.

**What was done:**

- A2 now **spawns in the inner lane (z=3.0)** like ordinary through traffic, not in the shared lane
- added `CarAgent.steerBeforeStopLine`, allowing a lane change before the stop line (normally forbidden). This is safe because the cars run parallel to X, so a lateral offset does not affect `signedDistToStopPoint`, which uses only the X difference
- added `CarAgent.steerStartDistance` - **no edging across until the remaining distance falls below a threshold**. This is the heart of the late merge, set so the merge completes **20 m before the stop line = 3.3 car slots**, over a total lane-change span of 42 m = 7 car slots

**A bug that had to be prevented at the same time:** `LookAheadLimit()`, added in round 6, clips the
look-ahead at a waypoint when the path turns. Left alone, A2 could not see the queue beyond the lane-change
point and would rear-end it. `PathTurnsAway()` now distinguishes **"turning away" (Ax into the campus, about
90 degrees) from "changing lane and continuing straight" (A2, about 9 degrees)**, and clips only the former.

### The emergency lane - still disabled (decided)

It was disabled in round 8 to avoid double-counting against the 18% that had been inferred from the blocking
rate. **That reason no longer applies**, because 5.4% is a direct count. But the decision is to keep two rows
as observed on site and **write it up as a limitation** that the model overstates the blocking. Itim had
already offered this as an option: "consider 60-70% escape, **or document the simplification**".

### Not done (deliberately deferred)

**CA is blocked by CB for 82% of the cycle**, worse than the inbound side at 26-29%. CA and CB are currently
different physical lanes in the scene (x=3.8 and x=1.5) and so never block each other. Modelling it needs
them sharing a lane the way A2 and Ax do.
Itim points out that this reframes the proposal too: **giving CB more green does not only help CB, it is the
only way to unblock CA**, which carries another 100 vehicles per 15 minutes and has no signal of its own.

**Test results:** all 6 suites pass. Five geometry checks were added for the late merge (the merge point is
before the stop line, completes within 6 car slots, is not closer than 2, the whole span is under 12 slots,
and the deviation angle is under 45 degrees so the look-ahead is not clipped).

### The scene must be rebuilt

A2's spawn point moved lane and there is a new waypoint set, so code changes alone are not enough.
When running T13, check that **A2 vehicles travel in the inner lane and only cut into the shared lane near
the stop line**, rather than running the shared lane the whole way.

---

## Round 8 (25 Aug) - the eastbound approach must have exactly two rows, so shoulder passing is off

Kimmy reported from a screenshot that **"lane A should only have two rows, A and Ax"** - correct, and a side
effect of my round 6 change.

Back then I fixed the "overlapping cars" bug by moving an Ax vehicle drawn to pass **2.3 m onto the
shoulder** at z=8.8, which turned the eastbound approach into **three rows** (z = 3.0 / 6.5 / 8.8) where
reality has two.

### Passing is now disabled - and the modelling reason is stronger than the visual one

While fixing it I found a bigger problem: **the passing mechanism and A2_SHARE = 18% discount the same
effect twice.**

Where 18% came from: not a direct count. Itim **back-calculated it from the observed 40-60% blocking rate**
using a pure car-following model. So **18% with no passing already reproduces the observed 40-60%**.

Adding a 65% passing chance on top drops the blocking rate to roughly 14-21%, **far below what was
measured**, so the model would understate the Ax problem - the opposite of what the project sets out to show.

The visual argument (two rows) and the statistical one (double-counting) point the same way, so passing is
off: `AX_BYPASS_CHANCE = 0` and `AX_BYPASS_OFFSET_Z = 0`.

**The code remains** (`CarAgent.canBypassCrossLaneBlocker` / `bypassLateralOffset` /
`CarSpawner.laneBypassChance`) in case we want it later, if Itim provides separate data on filtering and on
the security staff waving vehicles through, which is not already folded into the 18%.

### Consequences

- `ROI_Ax` is back to the normal 3 m width at z=6.5 (no need for 8 m covering the shoulder), and a new test enforces that the outer lane ROI must **not** cover the inner lane at z=3.0, so A vehicles are not counted into it
- Ax is once again blocked by A2 100% of the time by pure car-following physics, which matches the assumption behind the 18%

### New tests to stop this recurring

- **the eastbound approach must count exactly 2 rows** - reopening the offset by accident turns the test red immediately
- **`chance > 0` with `offset = 0` is forbidden** - that pair is exactly the round 6 "cars drive through each other" bug, now locked out
- **if passing is ever re-enabled**, the old rules (offset > lookAheadAngle, no overlap, stays on the carriageway) are checked automatically; while it is off they are skipped with a visible SKIP saying why

**Test results:** all 6 suites pass. GeometryTest confirms `2 rows (z = 3, 6.5)`.

### The scene must be rebuilt

Run `ABAC > Build 4-Phase Scene`, then `ABAC > Validate Scene`, then Ctrl+S, and confirm the eastbound
approach has exactly two rows of vehicles.

---

## Round 7 (25 Aug) - acting on Kimmy's field observations: a shared ROI, and Ax yielding to Bx

Kimmy reported two things from the site that the model did not have. Both change the results.

### 1. A2 and Ax must use the same ROI

**Why:** they are **one physical lane**. A real camera cannot tell which vehicle will go straight and which
will turn into the campus. Having two overlapping ROIs fed the controller information a real detector could
never have (and in round 6 I had even given the two ROIs different widths, 8 m and 3 m).

**The fix:**

- `ROIZone` gains `alsoCountLanes`, so one ROI can cover several movements (`ROI_Ax` covers both `Ax` and `A2`)
- `ROI_A2` is deleted from the scene (leaving it would count and log everything twice); the builder does this automatically
- `GetVehicleCount()` returns the total in the outer lane = **what a real detector sees**, so the adaptive controller sees the A phase's demand including Ax vehicles that do not need a green at all. **That is the correct behaviour** of a real detection system and is worth writing up in the report as shared-lane detection ambiguity
- **Wait statistics still separate by movement**, because `LogVehicle` now uses `car.assignedLane` rather than `roi.laneName`. Without that change the Ax and A2 waits would mix beyond separating - and the Ax wait is the headline measure of the whole project
- added `GetVehicleCount(lane)` for inspection and debugging by movement (a real detector cannot do this)

### 2. Ax cannot turn into the campus while Bx is green

**Why:** Bx also turns right into the campus, sweeping across the entrance, so Ax has to yield. The old
model had Ax 100% free, never stopping for a signal.

**The fix - a new "yield lane" concept:**

- `CarAgent.lightIsInverted` / `CarSpawner.lightIsInverted` read the *other* stream's signal inverted (their green or yellow means we stop; their red means we may go). `Spawner_Ax` is tied to `Light_Bx` this way
- a real stop line for Ax: **`StopLine_Ax` at x=-11.0**, exactly the point where the turn begins (replacing the decorative `StopLine_A2_Visual`, which the builder deletes). This position keeps **the queue inside the outer lane** as Kimmy confirmed, rather than obstructing the middle of the junction
- a Bx yellow still means Ax cannot go (Bx traffic has not cleared the entrance), and no dilemma zone applies, because a driver giving way does not accelerate to beat the light the way one facing their own amber does
- **every vehicle stops, 100%**, as Kimmy confirmed (no random filtering as in the shoulder mechanism)
- `Ax` still has `isSignalized = false`, because it **has no phase of its own and consumes nobody's green time**, and it stays out of the conflict matrix. It merely yields; it is not signalised

### A new trade-off that has to go in the report

Itim proposed **moving 8 s of green from A to Bx** to fix Bx at v/c 1.21-1.39 (the queue overflows in 7-11
minutes). But now **a longer Bx green directly means Ax is blocked longer**.

On a 107 s cycle: Bx green of 21 s stops Ax about 22% of the time; raising it to 29 s makes that about 27% -
on top of the blocking Ax already suffers from A2 in other phases.

**This is a genuine trade-off the project has to analyse** - fixing Bx makes Ax worse; it is not "move the
green and everyone wins". It is exactly the sort of balance an adaptive controller should find better than a
fixed one, and if it does, that demonstrates its value clearly.

### Blocking now works both ways

Before: A2 waits at the red and blocks Ax, one direction only.
Now: **Ax waits while Bx is green, its queue stands in the outer lane, and it blocks A2 in return.**
The outer lane can jam from both ends, which matches the site observations better.

### Tests added

- `Test5.cs` gains 12 checks: the shared ROI accepts the right lanes and rejects the wrong ones, counts 9 vehicles in total while still separating by movement (Ax=7, A2=2), and the inverted signal mapping is complete (Bx green / yellow / red, plus an ordinary lane being unaffected)
- `Test6.cs` gains 3 checks: the Ax stop line is not past the turn point, is at or before the A2 stop line, and the two are no more than a car length apart
- `ABACSceneBuilder.ValidateScene()` and `IntersectionConfig.ValidateConfiguration()` updated for yield lanes and shared ROIs, with **new checks**: a warning if a lane is counted by more than one ROI (its wait would be logged twice), and if a lane points at an ROI that does not count it (demand would always read 0)

**Test results:** all 6 suites pass.

### The scene must be rebuilt again

This round the builder **deletes** `ROI_A2` and `StopLine_A2_Visual` and **creates** `StopLine_Ax`, wiring
`Spawner_Ax` to `Light_Bx` inverted. Code changes alone are not enough.

Run `ABAC > Build 4-Phase Scene`, then `ABAC > Validate Scene` (which must print "lane Ax yields - it stops
whenever signal 'Bx' is not red"), then Ctrl+S.

Then run **T6** as the sharpest comparison available: only Ax and CA vehicles, with no Bx traffic at all.
Fixed still gives Bx a green every cycle, so Ax is stopped for nothing; Adaptive should not, so Ax barely
waits.

---

## Round 6 (25 Aug) - vehicle behaviour bugs found by watching the real scene

Kimmy sent screenshots of the running scene with two symptoms: **"Ax stops far short"** and **"cars
overlap"**. Tracing the code found a real and separate cause for each. Both are fixed.

### Bug 1 - "Ax stops far short": car-following looked infinitely far ahead, even while about to turn off

`CarAgent.GetCarAhead()` looked for the car ahead **along an unbounded straight line**, with only a 1.5 m
lateral filter. But Ax is a turning lane: it veers left from x=-11 and does not continue straight.

So an Ax vehicle at z=6.5 heading +X had a sight line straight across the junction to a **CA vehicle merging
into the same z=6.5 lane on the east side** (merge point about x=10.8, continuing to x=90), 40-90 m away,
which it could never actually meet. Those CA vehicles had just come out of the curve and were slow (about
6.7 m/s), so the Ax vehicle braked for them and came to rest far short of the junction.

**Fix:** added `CarAgent.LookAheadLimit()`, capping the view two ways:

1. a normal ceiling of `lookAheadDistance = 50 m` (tests confirm this is 1.55 times the braking distance of the slowest-braking profile, Cautious at 32.2 m)
2. if the path **turns** past the current waypoint, the view stops at that waypoint

A genuinely blocking queue is always *before* the turn point (the rear of a stopped A2 vehicle at x=-14.5 is
before Ax's turn at x=-11), so shared-lane blocking still works exactly as before - there is a test
specifically for this.

### Bug 2 - "cars overlap": two causes

**2.1 The shoulder-passing mechanism added in round 4 really did make cars drive through each other.**

It worked by "if this vehicle is drawn to pass, do not treat vehicles from other lanes as blockers"
(`if (canBypass && other.assignedLane != assignedLane) continue;`). But the vehicle **stayed on the lane
centreline**, so it drove straight through the stopped ones - visibly overlapping. (Round 4 already noted
that this mechanism could not be checked by the out-of-Unity harness and had to be watched by eye. Watching
it showed exactly this.)

**Fix:** replace "an exception to collision" with **a genuine offset onto the shoulder**.

- added `bypassLateralOffset`: an Ax vehicle drawn to pass (65%) spawns and drives at **z=8.8, offset by 2.3 m**, for as long as it is passing the queue, then merges back as it begins to turn
- **removed** the `other.assignedLane != assignedLane` condition from `GetCarAhead()` entirely. One set of physics with no exceptions: a passing vehicle does not see the queue because it is **genuinely 2.3 m away, more than 1.5 m**, not because of a special rule
- a side benefit: **you can now see with your own eyes whether passing works** (vehicles on the shoulder) rather than it being an invisible rule

**2.2 Vehicles were released on top of ones already stopped at the spawn point.**

`CarSpawner.SpawnCar()` instantiated immediately **without checking whether the space was free**, and A2's
spawn point was only **5 m** from Ax's while the cars are 4 m long - almost no room. Poisson releasing on two
nearby lanes overlapped them at once (and again whenever a queue backed up to the spawn point).

**Fix:**

- added `CarAgent.IsSpaceOccupied()` with `spawnClearRadius = 6 m`, checked before every release
- a vehicle that cannot be released is **not discarded** but held in a `pending` queue and released later. (Discarding would silently cut demand exactly when traffic is heaviest, making results look better than they are.) A `delayedArrivals` counter allows this to be checked
- **important for the paired t-test:** the bypass draw moved to the moment of "arrival" rather than "release", so the draw order does not depend on traffic conditions and Fixed and Adaptive still see exactly the same vehicles even when congestion differs
- A2's spawn point moved from 5 m to **20 m** (five car slots)

### A consequence

**`ROI_Ax` had to widen** from 3 m to **8 m** (centred at z=7.65), or the 65% of vehicles passing on the
shoulder fall outside the ROI and **their wait is never counted**. Lane Ax is the headline measure of the
whole project; missing this would distort everything.

### New tests, and a discovery about the harness

- added **`Test6.cs` (GeometryTest)**, checking consistency between vehicle behaviour parameters and the real scene coordinates (pure arithmetic, so it runs outside Unity). 15 checks, including offset > lookAheadAngle, a passing vehicle staying on the carriageway, the ROI covering the shoulder, the look-ahead ceiling exceeding every profile's braking distance, and spawn points not overlapping.
  **This test immediately caught a bug:** the offset was first set to 2.4 m, putting the car's edge 2 cm past the carriageway, so it was reduced to 2.3 m.
- **The "Editor compiles" claim in every previous round was a false pass.** `ABACSceneBuilder.cs` is wrapped entirely in `#if UNITY_EDITOR`, but the mcs command never passed `-define:UNITY_EDITOR`, so the whole file was cut away and "it compiles" checked nothing. Added `run_tests.sh`, which sets the define correctly and runs every suite in one command. (Checked retrospectively: the Editor code did compile; it had simply never been checked.)

**Test results:** all 6 suites pass (ConflictTest / FixedCycleTest / BatchMathTest / EtaTest / SharedLaneTest / GeometryTest).

### To do on the machine before continuing

**Run `ABAC > Build 4-Phase Scene` again and press Ctrl+S.** It is genuinely required this time, because the
changed values live in the scene rather than the code: the position and size of `ROI_Ax`, A2's spawn point,
and `bypassLateralOffset` on `Spawner_Ax`.

Then run **T13** and check by eye that (1) some Ax vehicles really do pass the queue on the shoulder,
(2) no cars overlap any more, and (3) Ax vehicles that do not pass queue up close behind, rather than
stopping far from the junction.

### Still to improve (found while tracing, not yet fixed - needs a decision)

**CA merges into lane A2 with neither a signal nor any yielding behaviour.** CA's path ends at (x=90,
z=6.5), **the same lane A2 runs straight through**, with the merge at about x=10.8. At that moment A2 has
green and is travelling at about 15 m/s, while CA is a free lane with no signal to wait for and **no
give-way logic**. All that stops them is car-following, which only notices at 1.5 m lateral separation - too
late to brake, so near misses and overlaps are possible at the merge on the east side of the junction.

In reality traffic leaving the campus must give way to the main road. Modelling that needs
**gap acceptance** (CA waits for a large enough gap before pulling out), which is new behaviour that
**changes the CA and A2 results**, so it is not done yet, pending a decision.

---

## Round 5 (24 Aug) - updated to reply_to_badin_4.md

Itim compared Kimmy's own field timing against the video, re-analysed it, sent a full set of arrival rates
(8 clips), and corrected mistakes from the previous round:

1. **Fixed timing in `FixedController.cs` updated to confirmed measurements** - P1(A+A2+B)=40s / P2(B+Bx)=21s / P3(CB)=31s, intergreen 15s, **107 s per cycle** (previously an estimated 115 s). Confirmed by a closing figure: the CB red computed from this table (107-31-3=73s) matches the 73.5 s Itim measured on video, 0.5 s apart - much more reliable than the previous round.
2. **Confirmed the signal really is fixed-time, not actuated** - Itim measured C's red at 73.5 s, identical across two consecutive cycles on video. An actuated signal would vary with traffic. This validates that modelling it with FixedController is the right type.
3. **A significant new finding: the signal only operates 05:00-09:00 and 15:00-18:00, 29% of the day.** The rest of the time it flashes amber, confirmed in all 4 clips. **This directly affects T10 MiddayNormal**, because the clip used to measure its arrival rates (11:37-12:00) falls in the flashing period, so the Fixed vs Adaptive comparison for T10 becomes hypothetical ("if this junction ran fixed-time at midday too") rather than a real situation.
   **Decided 24 Aug (Kimmy's choice):** keep T10 as Fixed vs Adaptive, but state the caveat clearly in the report's analysis - that it is a hypothetical comparison for midday, not what actually happens. No "no control" mode and no dropping T10. The caveat comments already in `TestCaseLibrary.cs` and `ScenarioManager.cs` match this, so no code change is needed.
4. **Corrected mistakes from the previous round** - the lane C exit closes only **08:30-09:00, about 15-30 minutes**, not all morning (though that coincides with the real peak, so it is still valid for MorningPeak), and Soi Pa Kook opens **15:30-17:00**, not 16:00 (not yet wired into any scenario - out of scope for now).
5. **The real peak is 08:15-09:00 and ends abruptly** (not 07:00-08:30 as first assumed), with traffic falling 49-62% within minutes of 09:00 - a class-start peak, not an ordinary commute.
6. **Arrival rates from all 8 clips, detector validated at 98.3%**, using car+heavy x0.92 (stable, varying only 12%). `TestCaseLibrary.cs` (T8/T10) and `ScenarioManager.cs` (MorningPeak/MiddayNormal) updated: MorningPeak A=19.7/Ax=5.6/B=8.7/Bx=9.6, MiddayNormal A=30.3/Ax=8.5/B=15.9/Bx=17.6 (CB/CA still awaiting Monday's data).
7. **The proposal to move 8 s of green from A to Bx** (taking Bx's v/c from 1.21-1.39 down to 0.90-0.99) is deliberately **not** applied to the Fixed preset, because Fixed has to preserve the real system's problem for Adaptive to be compared against. Making the preset smarter would defeat the experiment. It is kept as a discussion point for the report and a target for Adaptive to find on its own (noted in `FixedController.LoadABACPreset()`).
8. **Tested:** Scripts and Editor both compile, and all 5 regression suites pass (`Test.cs` / `Test2.cs` / `Test3.cs` / `Test4.cs` / `Test5.cs`). `GetCycleLength()` computes exactly 107 s as configured.

**Still outstanding, as before:** `ABAC > Build 4-Phase Scene` has not been re-run on the machine since this
round's numeric changes. (No scene objects are affected - only timing and arrival rate values in the
scripts - but Unity still has to be opened to check that `FixedController` and `ScenarioManager` read the new
values, or to reload the presets from the context menu.) Waiting on Itim for: CB/CA arrival rates while open
(Monday), guard-wave statistics (item 6), saturation headway (item 7) and the remaining distances (item 8).

---

## Update 24 Aug - checking the signal timing Kimmy measured on site

Kimmy sent figures from timing the real signal (only lane C has a countdown display). Here is the check:

**Internal consistency first, using C's display as the reference clock, since it is the most accurate:**
- between the two snapshots given, C's red counted down from 75 s to 25 s (50 s elapsed), matching A having just completed 50 s of green and turned red. That part is consistent.
- but B contradicts itself: with 25 s of C's red left, Kimmy counted B as having 45 s of green remaining, which would mean B and C (=CB) overlap green by about 20 s, contradicting the (B x CB) conflict pair. **Conclusion: the B figure is almost certainly miscounted by hand without a display, rather than the real signal overlapping.**
- Ax and Cx (=CA) show "no light" in every snapshot, matching the model's assumption that they are free lanes with no signal of their own - a good reverse confirmation, not a bug.
- Bx goes green alongside B towards the end (once A is red), matching the P1 to P2 phase structure already in the code (B continues, Bx joins later) - confirming the phase structure is right.

**Against the current code** (`FixedController.cs`: 36/24/40 plus 15 s intergreen = 115 s, based on Itim's earlier 40 s green / 70 s red):
- C's yellow: Kimmy counted 3 s, exactly matching the code, so the counting method is trustworthy
- C's green: Kimmy 31 s against Itim's earlier 40 s, about 22% apart
- C's cycle: Kimmy 109 s against the code's 115 s
- A green: Kimmy about 50 s against the code's 36-39 s
- B green: Kimmy about 90-95 s against the code's 65 s (the largest difference, but also the least confident, with no display)
- Bx green: Kimmy about 20 s against the code's 24 s (close)

**Decision:** do not change `FixedController.cs` yet. Two sources (Itim from the drone clip, Kimmy timing
live) disagree on C's green, and a third data point is needed. The question has been added to
`Questions_for_Itim.md` section 3.1, asking Itim to check whether the clips cover the same period, to count C
against the countdown display directly once more, and to answer the full A/B/Bx phases from the original item 3.

---

## Round 4 (23 Aug, continued) - updated to reply_to_badin_3.md

Itim answered part of `Questions_for_Itim.md` (items 1, 2 and 5) and proposed the emergency lane. All of it
is now in the model:

1. **The A/A2 split changes from 50/50 to 18/82.** Itim back-calculated from the observed blocking rate (40-60% of the time) that the outer lane (A2) really carries only about 18% of through traffic, with a 13-23% sensitivity range - not half. It lives in the constant `A2_SHARE = 0.18f` in `TestCaseLibrary.cs` and the `a2Share` field in `ScenarioManager.cs`: one place, easy to change once directly counted.
2. **CB/CA in the morning are genuinely closed (confirmed by the security staff)**, not merely quiet. Added the constant `CLOSED = 999999f` (a mean interval so large that essentially nothing arrives in a 30-minute run), replacing `VERY_LOW` in MorningPeak.
3. **Added the emergency/shoulder lane mechanism**, following Itim's feedback that pure car-following overstates the blocking (in reality vehicles edge right, motorcycles filter and the security staff wave people through, so it is never 100%). `canBypassCrossLaneBlocker` in `CarAgent.cs`: each Ax vehicle draws at spawn, from the spawner's own RNG to preserve reproducibility, with a 65% chance (the midpoint of the 60-70% proposed) of ignoring A2 as a blocker while still following the other Ax vehicles in its own queue. Configured in `ABACSceneBuilder.cs` (`spAx.laneBypassChance = 0.65f`).
4. **PCU and vehicle mix per lane** (Itim's item 5) - the data is complete for A/Ax/B/Bx, but **not wired into Unity**, because the sim does not distinguish vehicle types physically (every vehicle is the same `CarAgent`). For now it is only useful for weighted capacity calculations in the report and in `analyze_results.py`. Making physical behaviour differ by vehicle type is a new feature whose scope needs discussing first.
5. **Detector accuracy confirmed at 98.3%** against ground truth (despite an mAP of only 76% - a different metric). Good news: it means the next round of data from the remaining clips will be less noisy, especially lane A, which was +/-28% on only 31-48 vehicles.

**Tested:** everything compiles and all 5 regression suites pass (`Test.cs` had its invariant changed from
"A==A2" to "A2/A ratio = 4.556" for the new split, plus a check that T8's CB/CA really are closed).

**A limitation, stated plainly:** the bypass mechanism changes car-following and geometry, which the
out-of-Unity compile-check harness cannot test, because it has no real physics (`Vector3.Dot` and `Cross` are
stubs returning 0). Unity has to be opened and T13 watched by eye to see whether the 65% of Ax vehicles
really do pass.

**Still outstanding, as before:** `ABAC > Build 4-Phase Scene` has not been run on the machine, and we are
waiting on Itim for CB/CA arrival rates while open, phase timing for A/B/Bx and distances (items 3, the rest
of 2, and 8) before running the real 270-run batch.

---

## Round 3 (23 Aug) - what was done

The request: "bring the sim up to date, then check everything again for anything wrong".

1. **Added lane A2, the shared lane - the most important change.** It models the fact that some through traffic uses the outer lane together with Ax (turning into the campus). A2 uses **exactly the same `TrafficLight` component as A** (not merely the same settings), so it always stops at the red with A. And because `CarAgent.GetCarAhead()` filters by **physical distance** rather than lane name, an Ax vehicle following an A2 vehicle in the same lane is trapped automatically, with no extra blocking logic. This is what lets the model **reproduce ABAC's real problem for the first time** - previously Ax flowed freely in the model, which did not match the drone footage.
2. **Corrected the Fixed timing to Itim's measurements** - CB green 30 -> 40 s, cycle 120 -> 115 s (P1 and P2 scaled by their existing ratio, since there is no per-phase measurement yet - see the note in `FixedController.LoadABACPreset()`).
3. **Real arrival rates instead of guesses** for MorningPeak and MiddayNormal (A/Ax/B/Bx). CB/CA remain guesses pending Itim's answer to `Questions_for_Itim.md`.
4. **Fixed a structural bug in `AdaptiveController`** - added `AddPhysicallyLinkedSignals()`, guaranteeing that lanes sharing one lamp (A and A2) are always grouped green together, even when the other lane's demand is 0 at the moment the leader is chosen. Without it, gap-out and anti-starvation cannot see the other lane's demand even though the real signal is already green for it.
5. **Added test case T13**, which demonstrates the blocking mechanism directly (against T6, which has no A2, as the baseline).
6. **Wrote a new automated suite (`Test5.cs` / `SharedLaneTest`)** confirming by reflection that A and A2 really reference the same TrafficLight instance, and that `BuildCompatibleGroup()` groups correctly in every case (leader=A, leader=A2, with and without demand, and conflicting lanes never joining).
7. **All 5 regression suites pass** (`Test.cs` ConflictTest, `Test2.cs` FixedCycleTest, `Test3.cs` BatchMathTest, `Test4.cs` EtaTest, `Test5.cs` SharedLaneTest), with Scripts and Editor both compiling under the mcs stub.

### Not done / blocked

- **`ABAC > Build 4-Phase Scene` has not been run on the machine.** The code creates `Spawner_A2`, `ROI_A2` and `StopLine_A2_Visual` automatically (idempotent, safe to repeat), but Unity has to be opened and the menu item pressed before the objects exist in the scene.
- **Do not start the 270-run batch yet.** The CB/CA arrival rates and the inner/outer split of through traffic (A vs A2) are still guesses at 50/50, pending Itim's answer to `Questions_for_Itim.md` (sent 23 Aug).
- The flashing-amber mode Itim observed outside the peaks is not modelled. If confirmed, the midday Fixed vs Adaptive comparison will not match reality (it would have to be "adaptive vs no control"). That is a significant decision to take before the real run.

---

## 1. Project overview

A digital twin of the T-junction at the ABAC Suvarnabhumi campus entrance on Bangna-Trad Road (Sor Por 5003),
built to compare:

- **Fixed-cycle controller** - a rigid **115 s cycle** (the baseline, adjusted from the DOH 120 s to the measured value)
- **Adaptive controller** - FCFS plus an HCM-based clearance time formula plus concurrent phases, adjusting green time to real demand

The goal: run **135 scenarios x 2 controllers = 270 runs**, collect waiting times, and compare with a paired
t-test.

**The main hypothesis being tested (new this round):** Adaptive may make `Ax` worse than Fixed, because Ax is
unsignalised and the controller cannot see its demand directly. If Adaptive allocates A's green from A's own
demand, which is usually far lower than Ax's, then A2 gets less green too (same lamp), the red is longer, the
shared-lane queue builds, and Ax is blocked harder than under Fixed, which gives a constant green regardless
of demand. That is a valuable finding either way it turns out.

---

## 2. System architecture (after the refactor)

### 2.1 The full junction - 7 lanes, 4 signals (5 signal indices, since A shares with A2), 3 phases

The ABAC entrance is to the **north (+Z)**, confirmed from the real coordinates in `SampleScene.unity`.

| Lane | Movement | Signal? | Crosses |
|---|---|---|---|
| **A** | eastbound (+X), straight - inner lane | yes | - |
| **A2** (new) | eastbound (+X), straight - outer lane, shared with Ax, on A's signal | yes (shares A's) | the same as A |
| **Ax** | splits from A, turns left into ABAC - same lane as A2 | no, free | nothing, but is blocked by A2 waiting at the red |
| **B** | westbound (-X), straight - southern half | yes | - |
| **Bx** | splits from B, turns right into ABAC | yes | A, A2 |
| **CB** | leaves ABAC turning right to merge into B | yes | A, A2, B |
| **CA** | leaves ABAC turning left to merge into A/A2 | no, free | nothing |

**Conflict matrix (never green together):** A x Bx, A x CB, A2 x Bx, A2 x CB, B x CB, Bx x CB
**Always allowed together:** A+A2 (one lamp, with no ConflictPair between them), A+B (opposing through movements), B+Bx (same approach)

> **A point for the defence:** A2 is not "a seventh independent lane". It is the mechanism that models
> **shared-lane blocking**, the real bottleneck observed in the drone footage. It is not a capacity problem -
> the shared lane's v/c is only 0.30-0.56 - but a handful of A vehicles waiting at a red obstruct hundreds of
> Ax vehicles that never need to wait. The calculation is in `Reply_to_Itim_2.md`.

### 2.2 The Fixed phase plan (115 s cycle, adjusted from DOH's 120 s by the measured CB value)

| Phase | green lanes | green | note |
|---|---|---|---|
| P1 MainThrough | A + A2 + B | 36 s | A2 is always green and red with A (one lamp) |
| P2 EastRightTurn | B + Bx | 24 s | B continues green without going yellow |
| P3 ABACExit | CB | 40 s | measured by Itim (green 40 s / red 70 s) |

Yellow 3 s + all-red 2 s, three times = 15 s, giving exactly **36+24+40+15 = 115 s**.

**P1 and P2 (36 s / 24 s) are estimates**, scaled from the original 45:30 ratio to fit the time left after
CB (40 s) and intergreen (15 s). There is no per-phase measurement for A/B/Bx yet; these numbers must be
revised once Itim answers item 3 of `Questions_for_Itim.md`.

The verified timeline (from `Test2.cs`):
```
t=  0s +36s   A=G A2=G B=G Bx=R CB=R
t= 36s + 3s   A=Y A2=Y B=G Bx=R CB=R    <- A and A2 go yellow together while B stays green
t= 39s + 2s   A=R A2=R B=G Bx=R CB=R
t= 41s +24s   A=R A2=R B=G Bx=G CB=R    <- Bx joins after the all-red
t= 65s + 3s   A=R A2=R B=Y Bx=Y CB=R
t= 68s + 2s   A=R A2=R B=R Bx=R CB=R
t= 70s +40s   A=R A2=R B=R Bx=R CB=G
t=110s + 3s   A=R A2=R B=R Bx=R CB=Y
t=113s + 2s   A=R A2=R B=R Bx=R CB=R
t=115s        (back to P1)
```

---

## 3. What is finished

### 3.1 Scripts

| File | Purpose | Status |
|---|---|---|
| `IntersectionConfig.cs` | central junction data: 7 lanes, conflict matrix, validation - A2 added this round | done |
| `CarAgent.cs` | vehicle behaviour: 3 driver profiles, real braking physics, car-following (filtered by physical distance rather than lane name, which is what lets A2 and Ax block each other for free), waypoint steering | done |
| `TrafficLight.cs` | holds the signal state, notifies registered vehicles immediately, auto-cleanup | done (unchanged this round) |
| `FixedController.cs` | multi-lane phase list - A2 added to P1, cycle 120 -> 115 s this round | done |
| `AdaptiveController.cs` | FCFS, concurrent phases, the HCM formula - `AddPhysicallyLinkedSignals()` added this round | done |
| `CarSpawner.cs` | Poisson process, per-lane RNG | done (unchanged this round) |
| `ROIZone.cs` | vehicle detection with a lane-identity filter, FCFS timing | done (unchanged this round) |
| `DataLogger.cs` | per-vehicle waiting times and starvation events, CSV export | done (unchanged this round) |
| `ScenarioManager.cs` | generic N-lane batch runner - real MorningPeak/MiddayNormal values and A2 added this round | done |
| `TestCaseLibrary.cs` | 13 ready-made test cases - T13 added and T8/T10 given real values this round | done |
| `SimulationControlPanel.cs` | control UI: play/pause/stop, Fixed/Adaptive switching, test case selection, a live scrollable lane table, clearance-time comparison | done (unchanged this round - it uses `config.lanes` generically, so A2 works automatically) |
| `BatchRunPanel.cs` | right-hand batch UI: progress bar, ETA, Fixed vs Adaptive table per scenario, summary on completion | done (unchanged this round - equally generic) |
| `Assets/Editor/ABACSceneBuilder.cs` | the `ABAC > Build 4-Phase Scene` / `Validate Scene` menu - now also creates `Spawner_A2` / `ROI_A2` / `StopLine_A2_Visual` | done |
| `Analysis/analyze_results.py` | paired t-test, Wilcoxon, Cohen's d, 95% CI, Shapiro-Wilk, 5 charts | done (unchanged this round) |

### 3.2 How the shared lane (A2) is implemented

**The problem:** Ax (turning into the campus) previously had its own lane and flowed freely, contradicting
the drone footage, which shows Ax vehicles trapped behind through traffic waiting at the red in the same
shared lane.

**The solution, requiring no new blocking logic at all:**

1. A new `ApproachLane` called `A2`, with `isSignalized = true` and `trafficLight` pointing at **the same TrafficLight component as A** (set in `ABACSceneBuilder.Wire(cfg, "A2", lightA, roiA2, spA2)`), so it turns green and red with A automatically with no synchronisation logic.
2. `Spawner_A2` sits at the same coordinates as `Spawner_Ax`, offset 5 m back so the two spawners do not overlap at t=0. It has no waypoints (it runs straight like A) and uses the same `stopLineTarget` transform as `StopLine_A` (valid because the cars run parallel to X, so braking distance depends only on the X difference and the stop line's Z does not matter).
3. `CarAgent.GetCarAhead()` filters by **lateral distance** rather than lane name, so with A2 and Ax at the same z, the existing car-following traps Ax behind an A2 vehicle waiting at the red, automatically.
4. `AdaptiveController.AddPhysicallyLinkedSignals()` (new) - after building the concurrent group from demand, always add any lane sharing a `TrafficLight` component with a group member, whether or not it has demand. This stops gap-out and anti-starvation missing A2's demand while the real signal is already green for it.

**An unconfirmed assumption:** the inner (A) vs outer (A2) split of through traffic is 50/50 for now (the
`Poisson()` helper in `TestCaseLibrary.cs` and `MakeScenario()` in `ScenarioManager.cs` double the mean
interval for both, keeping the total through volume the same). Asked as item 1 of `Questions_for_Itim.md`.

### 3.3 Bugs fixed this round (23 Aug)

- **A false positive in `ABACSceneBuilder.ValidateScene()`** - the check `turning = laneName != "A" && laneName != "B"` wrongly warned that A2 "is a turning lane with no waypoints", when A2 deliberately runs straight. Fixed by also excluding "A2".
- **AdaptiveController could cut A2's green short** - if A2 became the leader while A's demand was 0, the group would be just {A2}, excluding A, so gap-out and anti-starvation could not see A vehicles arriving mid-phase even though the signal was already green for them (shared lamp). Fixed with `AddPhysicallyLinkedSignals()`. An edge case found in design, not in production, but guarded against.
- **T2, "all lanes equal", was no longer complete** - it had 6 lanes while the system now has 7, so A2 had to be added or the case would not test fairness across every lane.

### 3.4 Bugs fixed in earlier rounds (still fixed; see git log for detail)

- infinite recursion in `IntersectionConfig.Build()`, the Adaptive busy loop, mixed schemas in `summary_all_runs.csv`, "the light is green but cars do not move" (the two-pass reset), a double SpawnLoop, a too-wide turn arc, two stacked ROI BoxColliders. Full detail is in the git commit history and earlier sessions.

### 3.5 Verification done this round

- Scripts (12 files) and Editor (1 file) both compile under mcs with the UnityEngine/UnityEditor stubs
- `Test.cs` (ConflictTest) - 7 lanes, 5 signal indices, A2's conflict matrix correctly mirrors A's, A x A2 compatible, a greedy group from leader=A gives {A, A2, B}, and every Poisson case has A's interval exactly equal to A2's (the invariant of the 50/50 split)
- `Test2.cs` (FixedCycleTest) - runs the real `RunCycle()` for two full cycles at exactly 115 s, sees A and A2 green together, and never once has conflicting lanes holding the junction together
- `Test3.cs` (BatchMathTest) - unaffected by this round's changes, still passing
- `Test4.cs` (EtaTest) - unaffected by this round's changes, still passing
- `Test5.cs` (SharedLaneTest), **new** - built with reflection: confirms A2.trafficLight is the same instance as A.trafficLight, and that the real `BuildCompatibleGroup()` is correct in every case (leader=A pulls A2 in even at demand 0, leader=A2 symmetrically pulls A in, B joins when it has demand, and Bx/CB never join however high their demand)

---

## 4. Outstanding work

### 4.1 Rebuild the scene in Unity - highest priority
Run **`ABAC > Build 4-Phase Scene`** again (idempotent - nothing is lost, it simply adds `Spawner_A2` /
`ROI_A2` / `StopLine_A2_Visual`), then **`ABAC > Validate Scene`**, then re-run T1-T7 and T13. T13 is the
clearest demonstration of shared-lane blocking.

### 4.2 Waiting on Itim - blocks the real batch
`Questions_for_Itim.md` was sent on 23 Aug, split into P0 (required), P1 and P2. In brief:
- P0-1: the inner (A) vs outer (A2) split of through traffic - currently 50/50
- P0-2: arrival rates for CB and CA - currently guesses
- P1-3: signal timing for phases A/B/Bx (CB=40 s is known; the others are not)
- P1-4: confirmation of the flashing amber outside the peaks

### 4.3 Validate timeScale before the real run (still outstanding from the previous round)
Run the same scenario on the same seed at 1x / 10x / 20x and compare `AvgWaitTime`. More than about 5% apart
means it is too fast.

### 4.4 The real 270-run batch - do not start until 4.2 is resolved
- `repetitionsPerScenario = 27`, `runDuration = 1800`, `warmupDuration = 120`
- total simulated time about 144 hours, so about 14.4 hours at 10x
- delete the old `summary_all_runs.csv` first (it is appended to) and commit to git before running

### 4.5 Analysis
- a paired t-test comparing Fixed and Adaptive (waiting time, starvation events)
- **the main focus is Ax's waiting time** - whether Adaptive makes it better or worse than Fixed (this round's main hypothesis)
- per-lane analysis from `detailed_*.csv`

### 4.6 Decisions needed before the real run
- if Itim confirms the flashing amber outside the peaks, decide whether to (a) not model it and state it as a limitation, or (b) add a "no control" mode to FixedController to simulate the flashing. The latter is extra work and needs a team discussion.

### 4.7 Other items outstanding from the original proposal
- YOLOv8 tracking (real vehicle detection from camera footage) - Itim is working on it (see the `counts_A_morning.jsonl` he sent)
- confirming ABAC filming permission

---

## 5. Timeline

- **Senior Project Exhibition:** 25 September 2026 (about 4.5 weeks away)
- **Final defence:** 14-16 October 2026

---

## 6. Technical references

- **HCM (Highway Capacity Manual):** the formula `G = startupLostTime + D x saturationHeadway`, and the principle of concurrent non-conflicting movements
- **DOH Thailand (2019):** the 120 s/cycle default, now partly replaced by the measured value from the drone footage (115 s)
- **Poisson process** for vehicle arrivals, thinned 50/50 to split A and A2
- **Field measurement (Itim):** YOLOv8 vehicle counting from drone footage - see `Reply_to_Itim_2.md` and `Questions_for_Itim.md`

---

## 7. Related guides

- `SceneSetup_4Phase_Guide.md` - step-by-step scene setup, checklist and troubleshooting
- `Git_Guide.md` - pushing and pulling with git
- `Reply_to_Itim.md`, `Reply_to_Itim_2.md` - answers to Itim's questions on arrival rates and the shared lane
- `Questions_for_Itim.md` - questions still awaiting Itim's answers (P0/P1/P2)

---

*Updated from the development session of 23 August 2026*
