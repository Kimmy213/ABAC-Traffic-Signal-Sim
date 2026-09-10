# SODA v1.1 — as-built specification (FROZEN)

> ⚠️ **SUPERSEDED — 10 September 2026** by **`SODA_BASELINE.md`**, which contains this document's content with §8 closed, the gap-out wording made exact (fires on the second consecutive zero reading), the starvation tie-break aligned, and S1–S5 corrected. Kept for history only.

**Sampled-Observation Demand-Adaptive signal control**
ABAC T-intersection · `Assets/Scripts/AdaptiveController.cs`
Frozen 10 September 2026 · supersedes `SODA_Algorithm_Specification.md` §5–§9
Cross-check reference trace: `soda_reference_trace.json`

> **Status: FROZEN — cleared to run.** This document describes what the code does, line for line, not what was designed. §8 was the last open item; it was decided with Ratchnon on 10 September 2026 and is now implemented. Nothing below changes before the 270-run batch.

---

## 1. What changed from the v1.0 spec, and why

| v1.0 spec | v1.1 as-built | Reason |
|---|---|---|
| Rules A/B/C/D + selection tree | **Removed.** Ordering is `argmin queueStart` | Rule B never fires on this junction's demand profile (3 of 4 heads at 11.1–12.7 veh/round). C and D need `t_first`, which the SERVE loop does not produce. |
| Continuous per-vehicle tracking | **Snapshot counts only** | A tracker (ByteTrack/DeepSORT) is the hardware cost; YOLO detection at 0.5 Hz is not. |
| Rest in green when empty | **All-red when empty** | The real installation drops every head to red. |
| Continuous Mode / mode transition | **Removed** | With no per-vehicle timestamps there is only one mode. |
| Counting window (4 s) | **Removed** | See §7. |
| Tie-break unspecified | **Original sequence A → B → Bx → CB** | See §8. |
| `t_first` per head | **`queueStart` per head** | Lane-level timestamp; no vehicle identity required. |

Unchanged from v1.0: conflict matrix · concurrent grouping · A/A2 forced pairing · the green formula · clearance that does not cut lanes staying green · anti-starvation · max continuous green · Ax inverted light · late merge · seeded per-lane RNG.

---

## 2. State — eight numbers

Four signal heads. **H_A** (controls A *and* A2 — one physical head) · **H_B** · **H_Bx** · **H_CB**.
Ax and CA have no head and never enter the service queue.

Each head stores exactly two values:

```
queueStart[h]   when this head went empty -> occupied   (∞ = empty)
lastCount[h]    vehicle count at the previous snapshot
```

No vehicle IDs. No cross-frame matching. No list whose length grows with traffic. **This is where the tracker was removed.**

---

## 3. One clock

Every decision in the algorithm fires immediately after a snapshot and at no other time.

```
SnapshotLoop:  every snapshotInterval (2 s)  →  Snapshot()  →  snapshotIndex++
Everything else: wait until snapshotIndex changes, then evaluate
```

The single exception is the COLLECT ceiling (§5), which is a timeout, not a decision from data.

`Snapshot()` reads **all four heads, every time** — including heads already known to have traffic. (v1.0 stopped sampling detected heads as a compute saving; v1.1 cannot, because gap-out and the empty→∞ rule need every head's count continuously.)

```
for each head h:
    c = count(h)                            # sum of that head's ROIs
    if lastCount[h] == 0 and c > 0:  queueStart[h] = now      # rising edge
    if c == 0:                       queueStart[h] = ∞        # cleared
    lastCount[h] = c
```

---

## 4. ALL_RED_IDLE

Every head red. Snapshot every 2 s. On the first snapshot where any head has `lastCount > 0`, go to COLLECT.

---

## 5. COLLECT — the only demand accumulator

Ceiling `collectWindow` = **7 s**. Evaluated after each snapshot; exits early on either condition:

| # | Condition | Parameter |
|---|---|---|
| (a) | ≥ 2 heads have vehicles | `earlyExitHeadCount` = 2 |
| (b) | any head has ≥ 3 vehicles | `earlyExitDemand` = 3 |

**7 s is a ceiling, not a target.** More traffic means an *earlier* exit, never a later one. A burst of 4 vehicles on one head exits at 0 s elapsed; a single vehicle waits the full 7 s.

Recorded per run as `CollectCount` and `CollectDuration`.

---

## 6. SERVE — loops until the junction is empty

```
Snapshot()                                    # decision snapshot

Q1  starvation?
      any head with vehicles waiting ≥ 0.95 × 45 = 42.75 s
        → that head leads, log a StarvationEvent, skip Q2

Q2  leader = argmin queueStart      over heads with lastCount > 0
      TIME decides who gets green            ← fairness

Q3  group  = {leader}
      remaining heads, ordered by queueStart, join if they conflict
      with no current member
      A2 forced in whenever A is present (same physical head)
      heads with lastCount = 0 are skipped for the whole round
      possible groups: {A, A2, B} · {B, Bx} · singletons

    clearance: yellow 3 s + all-red 2 s applied ONLY to heads
               that must turn red. Heads staying green are not cut.

Q4  fresh count, then
      G = clamp( max over group of (2.95 + D × 2.31), 8, 45 )
      COUNT decides how long                 ← efficiency

    green, with three interrupts:
      gap-out         every lane in the group empty for 2 consecutive
                      snapshots (4 s)
      anti-starvation green ≥ 20 s AND some head outside has waited
                      ≥ 0.90 × 45 = 40.5 s
      max green       same group green 90 s AND demand exists outside

phase end:
      for each head just served:
          still has vehicles → queueStart = now      ← 🛑 critical
          empty              → queueStart = ∞

      any demand left → SERVE again (no COLLECT)
      none            → close group (yellow 3 + all-red 2) → ALL_RED_IDLE
```

**Why the reset is critical.** Without it a head that never empties — Bx runs at v/c = 1.51 — keeps its original `queueStart` forever, wins `argmin` every time, and locks every other head out permanently. The bug is invisible in light-traffic tests because heads empty periodically and `queueStart` clears itself. `QueueStartTest` covers it: 10 consecutive phases with no head ever emptying must still serve all four.

**Consequence for the report.** At peak the junction never empties, so `queueStart` is set by the reset rule rather than by arrival. The resulting behaviour is **round-robin weighted by demand**, not first-come-first-served by true arrival time. Do not describe it as FCFS. `QueueStartResets` in the summary CSV measures how much of a run was in this regime.

---

## 7. Why the counting window was removed

v1.0 had a second accumulator: if the chosen leader had fewer than `demandThresholdSkipWait` = 3 vehicles, wait 4 s and re-pick.

1. **Same question, same number, asked twice.** `earlyExitDemand` and `demandThresholdSkipWait` were both 3. A head with fewer than 3 vehicles therefore (i) prevents COLLECT from exiting early, so it runs the full 7 s, and then (ii) triggers another 4 s wait for the identical reason. One vehicle waited 11 s.
2. **It silently extended the previous green.** The wait happened before clearance, so the outgoing group stayed green through it — a phase could run 4 s past its computed `G`, and `GreenUsed` in the phase log never saw it.
3. **It was the last timer off the snapshot clock**, re-picking a leader from data up to 2 s stale at an arbitrary offset.

**Blast radius on the batch: near zero.** It only fired when the leader had < 3 vehicles, which does not happen in MorningPeak, EveningPeak or ABACEvent. It fired mainly in Night (out of scope) and MiddayNormal (a counterfactual — the real signal flashes amber 09:00–15:00). Removing it changes the headline results very little while removing a mechanism that could not be justified in the report.

Reverting is one commit if the team disagrees.

---

## 8. ✅ RESOLVED — the tie-break is the intersection's own phase order

**Decision: Option A, agreed with Ratchnon, 10 September 2026.** Implemented; the leader's behaviour is unchanged.

Because every head is updated inside the same `Snapshot()` call, two heads whose first vehicles arrive in the same 2 s window receive **bit-identical** `queueStart`. Ties are not rare — the sampling design makes them common. The same happens at phase end, where heads served together are reset together.

Before this decision the tie was settled by the order the lanes happened to be declared in, which was written down nowhere, and `List<T>.Sort` is documented as unstable — so Q3's outcome was not guaranteed by the language contract. That was a defect, not a matter of style.

```csharp
public static readonly string[] ORIGINAL_SEQUENCE = { "H_A", "H_B", "H_Bx", "H_CB" };

static int CompareHeads(a, b) {
    int c = a.queueStart.CompareTo(b.queueStart);
    return c != 0 ? c : a.priority.CompareTo(b.priority);
}
```

A total order, so both sites — the Q2 leader choice and the Q3 candidate ordering — give the same answer every time, independent of sort stability.

> Ties in queue-start time — a frequent artefact of 2-second sampling — are broken by the intersection's existing phase order, A → B → Bx → CB.

### The bias, and how it is measured

The tie-break favours **H_A**, which is the busiest head (26.7 veh/round). Because A conflicts with Bx, favouring A **systematically excludes Bx from the group** — and Bx, at v/c = 1.51, is the movement this project exists to study.

Verified, not theoretical:

```
leader = B, A and Bx tied → group = { B, A, A2 }   (Bx excluded)
                            had Bx been ordered first:
                            group = { B, Bx }      (A excluded)
```

**The direction of the bias is conservative** — it makes the adaptive controller look *worse* than it is for Bx, so a positive result stays trustworthy. What cannot be defended is not knowing its size. Two passive counters, added at Ratchnon's request, measure it without touching any decision:

| Column | Meaning |
|---|---|
| `TieBreaksApplied` | phases in which two or more occupied heads shared an identical `queueStart` |
| `TieBreaksExcludingBx` | of those, phases where Bx had vehicles, was left out of the group, and a head that tied with it was in |

If the ratio is small, one sentence in the limitations section covers it. If it is large, the report carries a measured figure instead of an assurance.

---

## 9. Parameters — complete

| Parameter | Value | Type |
|---|---:|---|
| `snapshotInterval` | 2 s | tuned |
| `collectWindow` | 7 s (ceiling) | tuned |
| `earlyExitHeadCount` | 2 heads | tuned |
| `earlyExitDemand` | 3 veh | tuned |
| `startupLostTime` ℓ₁ | **2.95 s** | **measured** — 39 samples, 5 saturated greens |
| `saturationHeadway` h | **2.31 s/veh** (sd 0.81) | **measured** — s = 3600/2.31 = 1,558 veh/hr/lane |
| `gMin` / `gMax` | 8 s / 45 s | practice |
| `yellowDuration` / `allRedDuration` | 3 s / 2 s | observed on site |
| `gapOutSnapshots` | 2 (= 4 s) | derived from `snapshotInterval` |
| `maxContinuousGreen` | 90 s | practice |
| `maxWaitTime` | 45 s | tuned (LOS D) |
| `minGreenBeforeInterrupt` | 20 s (≈ 7.4 veh) | tuned |
| `starvationSelectThreshold` | 0.95 (= 42.75 s) | tuned |
| `starvationInterruptThreshold` | 0.90 (= 40.5 s) | tuned |
| `ORIGINAL_SEQUENCE` | A → B → Bx → CB | fixed — matches the real signal's phase order |

Only three timing numbers exist in the algorithm: **2 s** (snapshot), **7 s** (COLLECT ceiling), **4 s** (gap-out = 2 snapshots) — plus 3 s + 2 s of intergreen.

---

## 10. Logging

`summary_all_runs.csv` — one row per run:

```
ControllerType, Scenario, RunTimestamp, TotalSpawned, VehiclesCleared,
AvgWaitTime, MaxWaitTime, StarvationEvents,
StartupLostTime, SaturationHeadway,          ← proves each row's calibration
SnapshotInterval, CollectCount, CollectDuration,
SkippedHeads, QueueStartResets, IdleLaneSeconds,
TieBreaksApplied, TieBreaksExcludingBx
```

`phases_<controller>_<scenario>_<ts>.csv` — one row per green phase:

```
PhaseIndex, StartTime, Group, GreenGiven, GreenUsed,
EndReason, LaneEmptyAt, IdleLaneSeconds
```

`LaneEmptyAt` records, per lane, the second within the phase at which it first read zero (`nan` = never), at 2 s resolution — what the detector could actually see, not ground truth.

**What this measures.** Gap-out currently requires *every* lane in the group to be empty, so a lane that clears early sits on green until the rest finish. Group {B = 10, Bx = 3}: G = 26.05 s, Bx empties at 9.88 s, so Bx holds green empty for **16.17 s** — and A conflicts only with Bx, not with B, so A waits that whole time for nothing. `IdleLaneSeconds` sums this across a run.

Decision rule after the batch: if `IdleLaneSeconds` exceeds ~5 % of the 1,680 s measured window (≈ 84 s per run) on a regular basis, change gap-out to per-lane — drop a lane from the group when it has been empty for 2 snapshots, and end the phase only when the group is empty. Otherwise report it as future work with the measured figure attached.

**Not changed for the batch.** The measurement is passive; the fix would alter results and must not land mid-experiment.

---

## 11. Test coverage

Nine suites, all green (`run_tests.sh`):

| Suite | Covers |
|---|---|
| ConflictTest | conflict matrix, test-case library |
| FixedCycleTest | the 106 s baseline, no conflicting greens across 2 cycles |
| BatchMathTest | batch arithmetic |
| EtaTest | run-time estimation |
| SharedLaneTest | A/A2 shared light, shared ROI, inverted light |
| GeometryTest | 90+ scene invariants, camera rig, 32 m shared lane, ROI outline |
| **SceneAuditTest** | **`SampleScene.unity` vs the code** — reflects over every serialized scalar field |
| **SodaTest** | queueStart reset / gap-out 2-snapshot rule / no tracker API / decision panel / idle-lane measurement / one clock / one accumulator / tie-break total order + counters |

`SceneAuditTest` exists because Unity writes component values into the scene file and those override the script defaults. Six fields were found stale that way, including `repetitionsPerScenario: 1` — which would have produced 10 runs instead of 270.

---

## 12. Limitations to state in the report

1. Arrival order is quantised to 2 s; heads arriving closer than that are tied and settled by the phase order A → B → Bx → CB (§8). This favours H_A over H_Bx; the bias is conservative and its size is measured by `TieBreaksExcludingBx`.
2. At peak, order comes from the reset rule, not true arrival — round-robin weighted by demand.
3. Vehicle count can be up to 2 s stale; at the peak arrival rate (~0.2 veh/s) that is ≈ 0.4 veh ≈ 0.9 s of green. Not significant, but stated.
4. Gap-out is 1 s slower than v1.0 (4 s vs 3 s) — the price of confirming across two snapshots so a single missed detection cannot end a phase early.
5. Detection is assumed perfect in simulation; measured accuracy is 98.3 %. A miss shifts `queueStart` by a full sampling interval, so it hurts ordering more than counting.
6. Lanes in a group that clear early hold green until the group clears (§10).
7. Each approach is assumed independent in `ScenarioManager`; the scenarios are assembled from measurements taken on different approaches at different times.
8. ℓ₁ and h were measured on lane B in the morning and applied to every lane, including turning movements.
9. No motorcycle agents, no PCU weighting, no U-turns; the adjacent four-way is not modelled, so Bx delay is a lower bound.
10. T10 MiddayNormal is a counterfactual — the real signal flashes amber 09:00–15:00.
