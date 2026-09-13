# SODA_BASELINE — the shared reference for the SODA signal controller

**Sampled-Observation Demand-Adaptive control · ABAC T-intersection, Bang Na–Trat front gate**

| | |
|---|---|
| **Document version** | **v1.7** — 12 September 2026 (see Revision history below) |
| **Owners** | Kimmy (Module B — Unity simulation, `AdaptiveController.cs`) · Itim / Ratchnon (Module A — detection, `soda.py`) |
| **Frozen spec date** | 10 September 2026 — §1–§7 and Appendix A have not changed since v1.0 |
| **Supersedes** | `SODA_SPEC_kimmy.md` (v1.0 design), `SODA_v1.1_AS_BUILT.md` (10 Sep), `SODA_Algorithm_Specification.md` |
| **Companion files** | `soda_reference_trace.json` (machine-readable S1–S5) · `Tests/verify_traces.py` (drives `soda.py` through every decision point below) |
| **Milestones** | Exhibition 25 Sep 2026 · Defence 14–16 Oct 2026 |

> **How to read this document.** Every behavioural claim carries a tag saying how to check it:
> **[code]** file and function · **[test]** the test that fails if the claim is false · **[measured]** the measurement, its sample size and date · **[tuned]** the value, who chose it, and on what grounds.
> A claim that could not be tagged was either removed or moved to §8 (open) or Appendix A (could not verify).

---

## Revision history

Every edit to this file gets a version number and a date here, before anything else changes. This table tracks **the document**; the change log in §9 tracks **the project** (code, algorithm, experiment inputs). An edit that changes nothing but wording still gets a row.

| Version | Date | What changed | Sections touched | By |
|---|---|---|---|---|
| v1.0 | 10 Sep 2026 | First frozen baseline. Reconciled four earlier documents; every claim tagged [code] / [test] / [measured] / [tuned]; traces S1–S5 verified against `soda.py`. | all | Kimmy |
| v1.1 | 10 Sep 2026 (evening) | Batch speed 5× → 10× after `CarAgent` sub-stepping removed the overrun that had forced 5×. Wall-clock estimate 23 h → 11.5 h. §8.6 left open: the 1× / 5× / 10× same-seed check is still owed. | Appendix C0, §8.6, §9 change log | Kimmy |
| v1.2 | 11 Sep 2026 | Batch v1 (216 runs) completed and analysed; results recorded. Opened §8.7 (defect found in the output) and §8.8 (two measurement defects). No spec change. | §8, §9 change log | Kimmy |
| v1.3 | 11 Sep 2026 | §8.7 re-classified after checking §4 against `soda.py`: the defect is a **Unity implementation bug**, not an algorithm change — §4 and `end_phase()` were both already correct, so no §9.1 vote applies. Wording of §8.7 and its change-log line corrected. | §8.7, §9 change log | Kimmy |
| v1.4 | 11 Sep 2026 | §8.1 closed. `origin/main` now carries SODA — the pre-SODA warning banner at the top, the §8.1 row, decision D7 and the file-status table all still said it did not. Verified from the repository's own refs: `refs/heads/main` = `refs/remotes/origin/main` = `2b893e5`, reached through `e65b7d8` (10 Sep, SODA + 10× batch) and two commits on 11 Sep, all authored by Kimmy213. | header banner, §8.1, Appendix A D7, Appendix B file table | Kimmy |
| v1.5 | 11 Sep 2026 | Itim reviewed C1 and raised no objection, and closed §8.2 and §8.5 on his side. Merged C1 + C2 + C3 into `AdaptiveController.cs`. Closed §8.2, §8.5, §8.7, §8.8. Corrected the Rep17 `StarvationEvents` figure (137, not 145 — 145 is the 27-run median) and the EveningPeak wording (no significant difference, not "loses"). Replaced the single-run evidence with the 27-run one. Added the field dates to §4.4 and §7 L5. Opened §8.9 (Q3 churn after the fix) and §8.10 (Itim's updated Python into the repo). | §4.4 and §7 L5 (dates only), §8, §9 change log, Appendix C0 | Kimmy, reviewed by Itim |
| v1.6 | 12 Sep 2026 | **Batch v2 complete** — 216 runs, 11 h 31 m, on the C1-corrected code; all four scenarios now significant and the results recorded in Appendix C0 and the §9 change log. Closed §8.6 (timescale), §8.9 (Q3 churn — it did not occur) and §8.10 (Itim's Python in the repository; all seven reference-trace phases reproduced exactly). §8.4 re-measured on v2 and decomposed into the part a cheap fix can recover and the part it cannot; the decision sits with Itim, deadline 15 Sep. Opened §8.11 (`StopBatchRun` does not restore what `RunAllExperiments` changed). Two defects corrected in `Analysis/analyze_results.py`. | §8, §9 change log, Appendix C0, Appendix D | Kimmy |
| v1.7 | 12 Sep 2026 (evening) | **§8.4 closed — per-head gap-out recorded as future work, agreed by Itim**, who accepted that the 84 s bar was never commensurate with `IdleLaneSeconds`. All three §8.10 couplings settled: Itim derived both starvation thresholds in `soda.py` from one `MAX_WAIT_SEC`, Kimmy documented the paired C1 reset at both C# call sites, and `test_soda_rounds.py` now asserts the D2 gap-out timing. Four Python suites in the repository, all green. Only §8.11 remains open. | §8.4, §8.10, §9 change log, Appendix D | Kimmy, agreed by Itim |

> **No rule, formula, threshold or measured constant in §1–§7 has changed since v1.0.** The only edits ever made inside §1–§7 are the measurement dates added to §4.4 and §7 L5 in v1.5. Everything else in every version touched the open-items register (§8), the change log (§9), Appendix C0, the repository-status notes, or wording.

---

> ✅ **`origin/main` carries SODA (closed 11 Sep 2026, v1.4).** Until 10 September this document opened with a warning that `origin/main` (`0fee0a3`) still held the pre-SODA controller — rest-in-green, per-vehicle tracker calls, counting window, `gapOutDelay = 3` — and that the file Itim had reviewed was that old one. That is no longer true. `e65b7d8` pushed SODA v1.1 and the 10× batch configuration; `main` and `origin/main` now both point at `2b893e5`. A fresh clone gets the controller this document describes. See §8.1 and Appendix A D7 for what the discrepancy was, since it is the reason several "code wins" resolutions read the way they do.

---

## 1. Scope

### 1.1 What SODA is

SODA is the project's existing adaptive controller made to run on **camera-realistic input**: counts sampled every 2 s, no vehicle identity, no tracker. Its decision logic — pick the head that has waited longest, group non-conflicting heads with it, give the group the HCM green time for its longest queue, cut early on gap-out / starvation / max-green — is the same logic the pre-SODA controller used. What changed is *what the controller is allowed to see*, and the three places where the old controller quietly depended on seeing more than a camera provides.

### 1.2 What SODA is NOT

- **Not a new decision algorithm.** The four ordering rules of v1.0 (FIFO / Small-First / Original / Mix) were removed (§6). What remains is one ordering: earliest queue start, ties broken by the junction's phase order.
- **Not a tracker.** It never knows which vehicle is which, when any vehicle arrived, or how fast anything moves. [code `ROIZone.GetSampledCount` — the only ROI method `AdaptiveController` calls] [test SodaTest §3 — source scan: `GetOldestArrivalTime`, `GetLongestWaitTime`, `GetCarsInZone`, `GetVehicleCount` absent from `AdaptiveController.cs`]
- **Not the measurement.** Vehicle waiting times reported in the results come from the simulator's ground truth via `ROIZone.OnTriggerExit → DataLogger.LogVehicle(car.assignedLane, …)`, which the controller never reads. The controller is constrained; the dependent variable is not. [code `ROIZone.cs/OnTriggerExit`] [test SodaTest §3 — `ROIZone` retains the tracker API and logs `car.assignedLane`]

---

## 2. What the controller can see

### 2.1 Signal heads, not lanes

The junction has seven movements and **four signal heads**. The unit of control is the head. [code `AdaptiveController.BuildHeads` — groups signal indices that reference the same `TrafficLight` object] [test SodaTest header — "แยกนี้มี 4 หัวไฟ", "H_A คุมทั้ง A และ A2"]

| Head | Signals | ROI(s) summed into its count | Note |
|---|---|---|---|
| **H_A** | A, A2 | `ROI_A` + `ROI_Ax` | One physical light. `ROI_Ax` is shared with the unsignalised left turn Ax, so H_A's count **includes Ax vehicles** that do not need H_A's green (§7 L4). |
| **H_B** | B | `ROI_B` | |
| **H_Bx** | Bx | `ROI_Bx` | The oversaturated movement, v/c = 1.51 under the fixed plan [measured — Itim, `SP1_HANDOFF_v2`, received 2 Sep 2026]. |
| **H_CB** | CB | `ROI_CB` | |

Ax and CA have no head and never enter the service queue. Ax is constrained indirectly: it reads Bx's light inverted (Bx green or amber = Ax stop) [code `CarAgent.OnLightStateChanged`, `lightIsInverted`] [test SharedLaneTest].

### 2.2 The eight numbers of state

Each head stores exactly two values. [code `AdaptiveController.SignalHead`]

```
queueStart[h]   time this head last went empty -> occupied   (∞ when empty)
lastCount[h]    vehicle count at the previous snapshot
```

Nothing else persists between snapshots. No vehicle IDs, no per-vehicle timestamps, no list whose length depends on traffic. [test SodaTest §3]

### 2.3 The snapshot

Every `snapshotInterval` = 2 s, all four heads are read — including heads already known to be occupied. [code `AdaptiveController.SnapshotLoop`, `Snapshot`] [test SodaTest §6 — exactly one `WaitForSeconds(snapshotInterval)` in the file]

```
snapshotIndex += 1
for each head h:
    c = Σ GetSampledCount() over h's ROIs
    if lastCount[h] == 0 and c > 0:   queueStart[h] = now     # rising edge
    if c == 0:                        queueStart[h] = ∞       # cleared
    lastCount[h] = c
```

A second snapshot is taken on demand at the instant a phase decision is made, so a decision never uses a reading older than the decision itself. [code `ServeOnePhase` — first statement is `Snapshot()`] [test SharedLaneTest — group construction is preceded by `Snapshot()`]

**Consequence.** Because all heads are stamped inside one call, two heads whose first vehicles arrive within the same 2 s window receive **bit-identical** `queueStart`. Ties are therefore common, not exceptional. §4.2 says how they are broken; §7 L1 sizes the effect.

---

## 3. The round

`T = 0` is the most recent entry into ALL_RED_IDLE. Snapshots fall at T = 0, 2, 4, … A round is ALL_RED_IDLE → COLLECT → SERVE (one or more phases) → back to ALL_RED_IDLE. Under saturated traffic the round never closes (S4).

### 3.1 One clock

Every data-driven decision is evaluated immediately after a snapshot and at no other time; the waiting loops block on `snapshotIndex` changing. [code `WaitForNextSnapshot`, `WaitForNextSnapshotOrDeadline`] [test SodaTest §6 — no `snapshotInterval * 0.5f` polling remains; exactly one timer in the file]

The one exception is the COLLECT ceiling, which is a timeout and can expire between snapshots.

### 3.2 ALL_RED_IDLE

All four heads red. On the first snapshot in which any `lastCount > 0`, go to COLLECT. [code `RunSoda`] The real installation drops to all-red when empty; the pre-SODA "rest in green" was removed for that reason (§6).

### 3.3 COLLECT — the only demand accumulator

Start a ceiling of `collectWindow` = 7 s. After each snapshot, exit early if either condition holds: [code `CollectPhase`]

| | Condition | Parameter |
|---|---|---|
| (a) | ≥ 2 heads have `lastCount > 0` | `earlyExitHeadCount = 2` |
| (b) | any head has `lastCount ≥ 3` | `earlyExitDemand = 3` |

Otherwise exit when the 7 s ceiling expires. The ceiling only shortens; nothing extends it. Per run, `CollectCount` and `CollectDuration` are logged. [test SodaTest §7 — `collectWindow == 7`, `earlyExitDemand == 3`]

### 3.4 SERVE — the four questions, once per phase

```
Snapshot()                                            # decision reading
Q1  starvation?      any head with lastCount>0 waiting ≥ 42.75 s → it leads, skip Q2
Q2  leader           argmin queueStart over heads with lastCount>0; ties → §4.2
Q3  group            leader + every head that conflicts with no member, tried in
                     (queueStart, priority) order; A2 forced in with A; heads with
                     lastCount = 0 are skipped for the whole phase
    clearance        yellow 3 s + all-red 2 s for heads leaving green only
Q4  green            G from a fresh count at green-open (§4.4)
    interrupts       gap-out · anti-starvation · max continuous green (§4.5)
phase end            for each served head: lastCount>0 → queueStart = now, else ∞
                     demand anywhere → SERVE again (no COLLECT) · none → close → IDLE
```

[code `ServeOnePhase`, `GreenPhaseWithInterrupts`, `ResetQueueStartFor`, `CloseGroup`]

**The reset at phase end is load-bearing.** Without it, a head that never empties keeps its original `queueStart` forever and wins Q2 every time — every other head is locked out. In a 10-phase simulation with no head ever emptying, the served counts were H_A = 3, H_B = 2, H_Bx = 3, H_CB = 2; with the reset removed, H_Bx took 10 of 10. [test SodaTest §1 and §1b]

**What this makes the peak-time behaviour.** When the junction never empties, `queueStart` is set by the reset rule, not by arrival, so service order is **round-robin weighted by demand**. It is not first-come-first-served by true arrival. `QueueStartResets` per run measures how long a run spent in this regime. Do not describe the controller as FCFS in the report.

---

## 4. The four questions in detail

### 4.1 Q1 — starvation override

A head with vehicles that has waited `now − queueStart ≥ starvationSelectThreshold × maxWaitTime = 0.95 × 45 = 42.75 s` leads the next phase and a `StarvationEvent` is logged. Among several, the longest wait leads. [code `FindStarvingHead(starvationSelectThreshold, null)`] [test SodaTest "ของเดิมที่ห้ามแตะ" — fires at 43 s, not at 10 s]

Among heads over the threshold the longest wait leads; on an exact tie the same `CompareHeads` order as Q2 applies (A → B → Bx → CB). Until 10 Sep the comparison was `>=`, which made the *last* declared head win a tie — the reverse of Option A and of `soda.py`; fixed the same day (change log). [code `FindStarvingHead` — `CompareHeads(h, worst) < 0`] [test SodaTest §9 — A and CB tied at 50 s → H_A; CB at 60 s beats A at 50 s; nobody over 42.75 → null]

### 4.2 Q2 — leader, and the tie-break

`leader = argmin queueStart` over heads with `lastCount > 0`. Time decides *who*; count is not consulted. If count decided, H_A (26.7 veh per 107 s round [measured — Itim, adopted arrival rates, `ABAC_TrafficSim_Progress_Round10.md` §3]) would lead every phase and H_Bx would never be served. [code `ChooseLeaderByQueueStart`]

**Tie-break — decided 10 Sep 2026 with Itim (Option A).** Ties in `queueStart` resolve by the junction's existing phase order:

```
ORIGINAL_SEQUENCE = { H_A, H_B, H_Bx, H_CB }
CompareHeads(a, b) = (a.queueStart, a.priority) vs (b.queueStart, b.priority)
```

The same comparison is used at Q2 and at Q3's candidate ordering. It is a total order, so the result does not depend on `List<T>.Sort` stability. [code `CompareHeads`, `SignalHead.priority` assigned from `ORIGINAL_SEQUENCE` in `BuildHeads`] [test SodaTest §8 — priorities 0/1/2/3; all-tied → H_A; sort from forward and reversed input gives the same order]

**Direction of the bias, and why it is tolerable.** A conflicts with Bx. When A and Bx tie as Q3 candidates, A is tried first and Bx is then rejected as conflicting — so the tie-break **systematically excludes H_Bx**, the movement the project studies. Verified: leader = B with A and Bx tied gives {B, A, A2}; had Bx been tried first it would give {B, Bx}. [test SodaTest §8] The bias makes the adaptive controller look *worse* for Bx than a neutral tie-break would, so it cannot manufacture a positive result. Its size is measured, not assumed: `TieBreaksApplied` (phases with any tie among occupied heads) and `TieBreaksExcludingBx` (of those, phases where Bx had vehicles, was excluded, and a group member tied with it). [code `CountTieBreaks`] [test SodaTest §8 — tied case counts (1, 1); untied case counts (0, 0)]

### 4.3 Q3 — the group

Start with the leader. Try each remaining head with `lastCount ≥ minDemandToJoinGroup` (= 1) in `CompareHeads` order; add it if it conflicts with no current member. Then force A2 in whenever A is present (same physical light). [code `BuildCompatibleGroup`, `AddPhysicallyLinkedSignals`] [test SharedLaneTest — A2 forced with A in both directions; B joins A; Bx never joins A]

Conflict matrix — six pairs: A↔Bx, A↔CB, A2↔Bx, A2↔CB, B↔CB, Bx↔CB. Non-conflicting by design: A↔B (opposing straight), B↔Bx (same approach), A↔A2 (one light). [code `IntersectionConfig.LoadABACPreset`] [test ConflictTest]

Feasible groups: {A, A2, B} · {B, Bx} · any singleton.

Clearance: heads leaving green get yellow 3 s then all-red 2 s; heads staying green are not cut. When the previous group was empty (coming from ALL_RED_IDLE) there is nothing to cut and no intergreen is paid. [code `ServeOnePhase` — `dropping = Except(activeGroup, nextGroup)`] [test FixedCycleTest establishes the same 3 + 2 s intergreen for the fixed baseline]

### 4.4 Q4 — green time

Read a fresh count at green-open (after clearance, not at decision time). Then: [code `CalculateGreenDuration`, called after `SetGroupState(…Green)`]

$$G = \operatorname{clamp}\Big(\max_{s \in \text{group}}\big(\ell_1 + D_s \cdot h\big),\; g_{\min},\; g_{\max}\Big)$$

The max is over *signals* in the group, so H_A contributes `max(ROI_A, ROI_Ax)`, not their sum. Parallel discharge is why it is a max and not a sum.

| Constant | Value | Provenance |
|---|---:|---|
| ℓ₁ startup lost time | **2.95 s** | [measured] Itim, 39 headway samples across 5 saturated greens, lane B, morning footage. **Source: `DJI_20260714084627_0005_D.MP4`, Tuesday 14 July 2026, window 08:46–09:06** (the DJI filename encodes YYYYMMDDHHMMSS; the date is also in the `source` field of the tally JSON). Handed over in `SP1_HANDOFF_v2` §5.1.1, 2 Sep 2026. HCM default 2.0 s. |
| h saturation headway | **2.31 s/veh** (sd 0.81) | [measured] same sample. Implies saturation flow 3600/2.31 = **1,558** veh/h/lane, 13 % below the HCM default of 1,800. (An earlier figure of 1,561 in the Round 9 progress log was an arithmetic error and has been corrected.) |
| g_min | 8 s | [tuned] Kimmy, v1.0 spec; traffic-engineering practice, no field basis |
| g_max | 45 s | [tuned] Kimmy, v1.0 spec; practice, no field basis |

[test SodaTest "ของเดิมที่ห้ามแตะ" — G(4) = 12.19] [test GeometryTest §9 — constants equal 2.95 / 2.31 and are not 1.0, 2.0 or 2.5] [test SceneAuditTest — the scene file carries 2.95 / 2.31, not the stale 1 / 2.5]

Response table (linear for D = 3 … 18; D_max at the cap is (45 − 2.95)/2.31 = 18.2):

| D | 0–2 | 3 | 4 | 5 | 6 | 7 | 9 | 10 | 12 | 15 | 18 | ≥ 19 |
|---|---|---|---|---|---|---|---|---|---|---|---|---|
| G (s) | **8.00** | 9.88 | 12.19 | 14.50 | 16.81 | 19.12 | 23.74 | 26.05 | 30.67 | 37.60 | 44.53 | **45.00** |

### 4.5 The three interrupts

G is a ceiling. The green ends at the first of: [code `GreenPhaseWithInterrupts`]

| Interrupt | Fires when | Parameters |
|---|---|---|
| **Gap-out** | the group has read zero on **two consecutive snapshots** — i.e. at the second zero reading, 2 s after the first, **4 s after the last non-zero reading** | `gapOutSnapshots = 2`. The 4 s is derived (2 × `snapshotInterval`); it is not an independently settable delay. The earlier "3 s or 4 s?" question was malformed. [test SodaTest §2 — readings [3,0,2] do not fire, [3,0,0] fire] |
| **Anti-starvation** | green has run ≥ `minGreenBeforeInterrupt` = 20 s **and** a head outside the group has waited ≥ 0.90 × 45 = 40.5 s | [tuned] Kimmy: 20 s ≈ 7.4 vehicles discharged, so an interrupted phase has still done useful work |
| **Max continuous green** | the same group has been green 90 s **and** a head outside has vehicles | [tuned] Kimmy, practice |

`EndReason` per phase is logged as `completed` / `gap-out` / `starvation` / `max-green`. [code `LogPhaseTrace`]

**Note on `StarvationEvents`.** One starving head produces **two** events: the interrupt that cuts the running phase, and the override that then selects it. Divide by two when reading the column as "episodes". [code both call `DataLogger.LogStarvationEvent`]

### 4.6 Parameter table — every value checked against `AdaptiveController.cs` (working tree, 10 Sep 2026)

| Parameter | Value in code | Kind |
|---|---:|---|
| `snapshotInterval` | 2 s | [tuned] Kimmy/Itim; YOLO detection at 0.5 Hz is cheap, a tracker is not (`Round10` §1.2) |
| `collectWindow` | 7 s | [tuned] Kimmy, v1.0 spec; no field basis |
| `earlyExitHeadCount` | 2 | [tuned] Kimmy, v1.0 spec §5 |
| `earlyExitDemand` | 3 | [tuned] Kimmy, v1.0 spec §5 |
| `startupLostTime` | 2.95 s | [measured] above |
| `saturationHeadway` | 2.31 s/veh | [measured] above |
| `gMin` / `gMax` | 8 / 45 s | [tuned] |
| `yellowDuration` / `allRedDuration` | 3 / 2 s | [measured] Itim's stage table for the existing signal, `SP1_HANDOFF_v2` |
| `gapOutSnapshots` | 2 | derived; see §4.5 |
| `maxContinuousGreen` | 90 s | [tuned] |
| `maxWaitTime` | 45 s | [tuned] Kimmy; LOS D criterion |
| `minGreenBeforeInterrupt` | 20 s | [tuned] |
| `starvationSelectThreshold` | 0.95 (→ 42.75 s) | [tuned] Kimmy; fire slightly before the limit rather than after |
| `starvationInterruptThreshold` | 0.90 (→ 40.5 s) | [tuned] |
| `minDemandToJoinGroup` | 1 | [tuned] |
| `enableConcurrentPhases` / `enableGapOut` | true / true | switches |
| `useMeasuredParameters` | true | guard: if the scene carries other values for ℓ₁ or h, `Awake` restores 2.95/2.31 and logs an error [code `EnforceMeasuredParameters`] [test GeometryTest §9] |
| `ORIGINAL_SEQUENCE` | H_A, H_B, H_Bx, H_CB | fixed; matches the existing signal's phase order |

Removed from the code and therefore **absent** from this table: `countingWindow`, `demandThresholdSkipWait`, `gapOutDelay` (§6). [test SodaTest §7]

---

## 5. Worked traces S1–S5 — acceptance tests

**Conventions.** Each trace fixes its *inputs* — the snapshot readings — and derives the controller's outputs. Vehicle dynamics are not part of the trace: where a reading is stated, it is stipulated, not simulated. T restarts at each entry into ALL_RED_IDLE. All decision points below were executed against `soda.py` with `Tests/verify_traces.py`; where `soda.py` cannot yet reproduce a line, the line says so.

**Correction to earlier drafts.** The versions of S1, S2 and S4 circulated on 8–10 Sep were wrong and are superseded here: S1 and S2 still contained the counting window (removed §6.3), and S4 omitted the anti-starvation interrupt that cuts phase 2 at T = 55.67 and the starvation override that follows. `soda.py` independently produces the corrected S4 (`verify_traces.py`: `ANTI_STARVATION` at 55.67, `STARVATION` leader CB).

### S1 · one vehicle, quiet junction

```
T= 0.0  snap  all 0                          ALL_RED_IDLE
T= 2.0  snap  all 0
   3.4  vehicle arrives H_CB                 not yet visible
T= 4.0  snap  CB=1  → queueStart[CB]=4.0     → COLLECT, deadline 11.0
              exit? heads=1 (<2), max=1 (<3) → no
T= 6.0  snap  CB=1  → no
T= 8.0  snap  CB=1  → no
T=10.0  snap  CB=1  → no
T=11.0  ceiling → COLLECT ends. CollectDuration = 7.0
T=11.0  SERVE  decision snap CB=1
        Q1  wait = 11.0 − 4.0 = 7.0 < 42.75         → none
        Q2  eligible {CB}                           → H_CB
        Q3  no other occupied head                  → {CB}; SkippedHeads += 3
        clearance: nothing was green                → 0 s
        Q4  D=1 → 2.95 + 1×2.31 = 5.26 → clamp      → G = 8.00
T=11.0  🟢 CB
        readings  12:1  14:1  16:1  18:0 (zero streak 1)
T=19.0  G elapsed → completed. GreenUsed 8.0
        reset: CB reads 0 → queueStart = ∞ ; no demand → close group
T=19.0  🟡 CB      T=22.0  🔴 all-red      T=24.0  ALL_RED_IDLE
Round = 24.0 s
```

Cost of serving one vehicle: 7 s COLLECT + 8 s floor green + 5 s closing = 20 s from first detection. That is the price of all-red idle instead of rest-in-green; it is stated as a limitation (§7 L7). Night traffic is outside the experiment's scope, so it does not touch the headline result.

`soda.py`: Q1–Q4 reproduce (`leader=CB, G=8.00`) **only after bypassing its counting window** — as shipped it returns `None` (hold) for a 1-vehicle leader. See Appendix A, D1.

### S2 · two heads within one window — early exit (a)

```
T= 0.0  snap  all 0
   1.2  vehicle H_A
T= 2.0  snap  A=1  → queueStart[A]=2.0       → COLLECT, deadline 9.0
              exit? heads=1, max=1            → no
   3.1  vehicle H_B
T= 4.0  snap  A=1 B=1 → queueStart[B]=4.0
              exit? heads=2 ≥ 2               → YES.  CollectDuration = 2.0
T= 4.0  SERVE  decision snap A=1 B=1
        Q1  A 2.0, B 0.0                            → none
        Q2  A (2.0) < B (4.0)                       → H_A
        Q3  B: A↔B no conflict → joins; A2 forced   → {A, A2, B}
        Q4  A=1 → 5.26; A2=0 skip; B=1 → 5.26 → max 5.26 → G = 8.00
T= 4.0  🟢 A A2 B
        readings  6:(1,1)  8:(1,1)  10:(1,0)  12:(0,0) streak 1
T=12.0  completed. reset all → ∞ ; close
T=15.0  🔴      T=17.0  ALL_RED_IDLE
Round = 17.0 s
```

Note the leader: A had 1 vehicle and B had 1, but even if B had more, A leads — it queued first. `soda.py` reproduces after the window bypass: `leader=A, group=[A,A2,B], G=8.00`.

### S3 · a burst on one head — early exit (b)

```
T= 0.0  snap  all 0
   0.5 1.1 1.6 1.8  four vehicles H_Bx
T= 2.0  snap  Bx=4 → queueStart[Bx]=2.0      → COLLECT
              exit? max=4 ≥ 3                 → YES immediately.  CollectDuration = 0.0
T= 2.0  SERVE  Q1 none · Q2 H_Bx · Q3 {Bx} (B reads 0, skipped)
        Q4  2.95 + 4×2.31 = 12.19
T= 2.0  🟢 Bx        Ax is held red for the whole green + yellow (inverted light)
        readings  4:3  6:2  8:2  10:1  12:1  14:0 streak 1
T=14.19 completed ; close
T=17.19 🔴  (Ax released)     T=19.19  ALL_RED_IDLE
Round = 19.19 s
```

More vehicles → shorter round (19.19 s) than one vehicle (24.0 s). The 7 s window is a ceiling, not a cost. `soda.py` reproduces: `leader=Bx, G=12.19`.

### S4 · morning peak — the round never closes

Snapshot readings are stipulated per phase as indicated.

```
T= 0.0  snap  A=12 B=5 Bx=9 CB=7 — all rising edges → queueStart all = 0.0
        → COLLECT; exit (a) at once.  CollectDuration = 0.0

── Phase 1, decision T=0.0 ─────────────────────────────────────────
  Q1  all waits 0                                  → none
  Q2  all queueStart 0.0 → TIE → priority          → H_A        TieBreaksApplied = 1
  Q3  candidates by (queueStart, priority): B(0,1) Bx(0,2) CB(0,3)
      B: no conflict with A → joins
      Bx: conflicts A       → rejected
      CB: conflicts A       → rejected
      A2 forced                                    → {A, A2, B}
      Bx occupied, excluded, A tied with it        → TieBreaksExcludingBx = 1
  clearance: none (from all-red)
  Q4  A=12 → 30.67 ; A2=0 ; B=5 → 14.50 → G = 30.67
  🟢 A A2 B   0.0 → 30.67
  anti-starvation at elapsed 20 (T=20): Bx, CB waited 20 < 40.5 → no
T=30.67 completed.  readings A=10 B=4 → queueStart[A]=queueStart[B]=30.67
        Bx, CB untouched at 0.0.  Demand remains → SERVE again.

── Phase 2, decision T=30.67 ──────────────────────────────────────
  Q1  Bx 30.67, CB 30.67 < 42.75                   → none
  Q2  Bx(0.0,2) vs CB(0.0,3) → TIE → priority      → H_Bx       TieBreaksApplied = 2
  Q3  candidates CB(0,3) A(30.67,0) B(30.67,1)
      CB conflicts Bx → no ; A conflicts Bx → no ; B joins   → {Bx, B}
  cut {A, A2}:  🟡 30.67–33.67   🔴 33.67–35.67
  Q4  at green-open, readings Bx=9 B=4 → 23.74, 12.19 → G = 23.74
  🟢 Bx B   35.67 → planned 59.41
  anti-starvation: first check at elapsed 20 = T=55.67
      CB outside, waited 55.67 − 0.0 = 55.67 ≥ 40.5 → CUT       EndReason = starvation
      GreenUsed = 20.0 (of 23.74)                                StarvationEvents = 1
T=55.67 readings Bx=8 B=3 → queueStart[Bx]=queueStart[B]=55.67

── Phase 3, decision T=55.67 ──────────────────────────────────────
  Q1  CB waited 55.67 ≥ 42.75 → STARVATION OVERRIDE → H_CB     StarvationEvents = 2
  Q3  CB conflicts everyone                        → {CB}
      Bx(55.67) = B(55.67) tie among occupied heads → TieBreaksApplied = 3
      (Bx excluded, but no group member ties with it → ExcludingBx stays 1)
  cut {Bx, B}:  🟡 55.67–58.67   🔴 58.67–60.67
  Q4  CB=7 → 2.95 + 7×2.31 = 19.12
  🟢 CB   60.67 → 79.79
… and on. T never resets; ALL_RED_IDLE is never re-entered.
```

Counters after three phases: `TieBreaksApplied = 3`, `TieBreaksExcludingBx = 1`, `StarvationEvents = 2` (one episode, counted twice — §4.5), `QueueStartResets = 4`.

`soda.py` reproduces every decision: P1 `leader=A, group={A,A2,B}, G=30.67`; P2 `leader=Bx, group={B,Bx}, G=23.74, cut={A,A2}`; `should_end_early(55.67) = ANTI_STARVATION`; P3 `leader=CB, reason=STARVATION, G=19.12, cut={B,Bx}`.

### S5 · the idle-green case (Itim, 10 Sep)

```
T= 0.0  SERVE  group {B, Bx}: readings B=10 Bx=3, both queueStart 0.0
  Q4  B → 26.05 ; Bx → 9.88 → G = 26.05
  🟢 B Bx   0.0 → 26.05
  discharge model: Bx's third vehicle crosses at 2.95 + 3×2.31 = 9.88
  readings Bx: 2:2 4:2 6:1 8:1 10:0 …     B: 10 → … → 24:1  26:0
  gap-out: never — B is occupied until the end, so the group never reads all-zero
  LaneEmptyAt: Bx = 10.0 (first zero reading, 2 s grid) ; B = nan
  IdleLaneSeconds = 26.05 − 10.0 = 16.05     (analytic 26.05 − 9.88 = 16.17)
T=26.05 completed
```

For 16.05 s Bx holds green with nothing to serve, and because A conflicts with Bx and not with B, A waits that entire time for nothing. Gap-out cannot help: it needs the *whole group* empty. The per-lane variant (drop a lane from the group after two zero readings; end the phase when the group is empty) is not implemented; `IdleLaneSeconds` over the batch decides whether it should be (§8.4). [code `LogPhaseTrace`] [test SodaTest §5 — streak stays 0 with Bx empty and B occupied; the log accumulates 16.17 when given the analytic value]

---

## 6. Removed since v1.0, and why

Negative results, recorded so they are not re-proposed.

### 6.1 Ordering rules A/B/C/D and the selection tree — removed

- **B (Small-First).** On the adopted arrival rates the four heads carry 26.7 / 11.9 / 11.1 / 12.7 vehicles per round; `GAP_SIGNIFICANT` (D_min ≤ 0.4 × median **and** median − D_min ≥ 4) fails on both tests (11.1 ≤ 4.9 is false; 1.2 ≥ 4 is false). It would fire only on Poisson noise, which is worse than never — the log would show a mechanism "working". [measured rates — Itim, `Round10` §3.1] No cross-head simultaneous observation exists to calibrate 0.40 / 4 / 2, and none can be collected before the deadline.
- **C (Original) and D (Mix)** need `t_first`, which the SERVE loop never produces. What survives of C is the tie-break in §4.2.
- **A (FIFO)** is not gone; it *is* Q2, without a label.
- `SODA_SPEC_kimmy.md` also specified 5 s idle sampling and "stop sampling a head once detected". Neither is in the code: idle sampling is 2 s, and every head is read every time because gap-out and the empty → ∞ rule need continuous counts. [test SodaTest §6]

### 6.2 Continuous Mode — removed

v1.0 §9 fell back to "FCFS from continuous ROI arrival timestamps" under saturation — exactly the capability §1 said no camera provides. With no per-vehicle timestamps there is one mode.

### 6.3 Counting window — removed

The pre-SODA controller waited 4 s and re-picked when the leader had fewer than `demandThresholdSkipWait` = 3 vehicles. Three reasons, in order of weight:

1. `earlyExitDemand` and `demandThresholdSkipWait` were both 3. A head with < 3 vehicles therefore kept COLLECT from exiting early (full 7 s), then triggered the 4 s wait for the identical reason. **One vehicle waited 11 s** before green.
2. The wait ran *before* clearance, so the outgoing group stayed green through it — a phase could exceed its computed G by 4 s and `GreenUsed` never saw it.
3. It was the last timer not on the snapshot clock.

Effect on the batch: the window fired only when the leader had < 3 vehicles, which does not occur in MorningPeak, EveningPeak or ABACEvent. [test SodaTest §7 — `countingWindow` and `demandThresholdSkipWait` absent; exactly one `WaitForSeconds` remains]

**`soda.py` still implements it** (`MIN_LEADER_COUNT`, `COUNTING_WINDOW_SEC`, `waited_once`) and `test_soda.py` tests it. That is the reference implementation being behind the baseline, not the code being wrong — see Appendix A, D1.

### 6.4 Rest in green — removed

The real installation drops every head to red when empty; the simulator now does the same. [code `RunSoda` — `CloseGroup` before `AllRedIdle`]

### 6.5 Per-vehicle tracking — removed

`GetOldestArrivalTime`, `GetLongestWaitTime`, `GetCarsInZone` require a tracker (ByteTrack/DeepSORT) behind the detector. They remain in `ROIZone` for the *measurement* and are never called by the controller. [test SodaTest §3]

---

## 7. Limitations — with the number attached

| # | Limitation | Size |
|---|---|---|
| L1 | Arrival order is quantised to 2 s; heads within one window tie and are settled by A → B → Bx → CB, which excludes Bx from groups it would otherwise join. | Direction: conservative for the Bx result. Size: `TieBreaksExcludingBx / phases` from the batch — not yet known. |
| L2 | Under saturation, service order comes from the reset rule, not true arrival: round-robin weighted by demand. | `QueueStartResets` per run — not yet known. |
| L3 | A count can be up to 2 s stale at the decision. | At the peak arrival rate (~0.2 veh/s) ≈ 0.4 veh ≈ 0.9 s of green. |
| L4 | H_A's count includes Ax vehicles via the shared `ROI_Ax`, inflating its demand. | Ax share of the outer lane not separately measured; A2 is 5.4 % of straight traffic [measured — Itim, `SP1_HANDOFF_v2` §5]. |
| L5 | Detection is assumed perfect. | Field accuracy 98.3 % (298/303) [measured — Itim, lane A morning, `DJI_20260713084049_0002_D.MP4`, Monday 13 July 2026, window 08:40–09:05]. A miss shifts `queueStart` by a full 2 s interval, so ordering suffers more than counting. |
| L6 | A lane that clears early holds green until the group clears. | S5: 16.05 s idle for a {B=10, Bx=3} group. Batch total in `IdleLaneSeconds`. |
| L7 | All-red idle costs a full COLLECT ceiling plus closing on light traffic. | S1: 20 s from first detection to idle for one vehicle. Outside the experiment's scenarios. |
| L8 | Gap-out is 2 s later than a single-reading rule. | Deliberate: one missed detection must not end a phase. |
| L9 | ℓ₁ and h were measured on one lane (B) in one period (morning) and applied to all, including turning movements. | 39 samples, sd(h) = 0.81 s. Turning-lane headway unmeasured. |
| L10 | Scenario arrival rates were measured per approach at different times and composed as if independent. | No cross-approach footage window exists [Itim, `Round10` §3.4]. |
| L11 | No motorcycle agents, no PCU weighting, no U-turns; the adjacent four-way is not modelled. | Bx delay is a lower bound. |
| L12 | T10 MiddayNormal is a counterfactual: the real signal flashes amber 09:00–15:00. | Retained deliberately, caveat stated. |
| L13 | `StarvationEvents` double-counts each episode (interrupt + override). | Divide by 2 when reporting episodes. |

---

## 8. Open questions — each with an owner and a deadline

| # | Question | Owner | Deadline | What closes it |
|---|---|---|---|---|
| ~~8.1~~ | ~~**The SODA v1.1 controller is uncommitted.** `origin/main` (`0fee0a3`) still carries the pre-SODA `AdaptiveController.cs`.~~ **Closed 11 Sep 2026.** Pushed as `e65b7d8` on 10 Sep together with the 10× batch configuration; `main` and `origin/main` both at `2b893e5` as of 11 Sep, all commits authored by Kimmy213. A fresh clone now gets SODA. | Kimmy | done | `grep ORIGINAL_SEQUENCE Assets/Scripts/AdaptiveController.cs` on a fresh pull returns the tie-break array — Itim's confirmation is still worth having, but the push itself is verified from the repository refs. |
| ~~8.2~~ | ~~`soda.py` is behind the baseline: counting window, gap-out one snapshot late, no COLLECT.~~ **Closed 11 Sep 2026 by Itim.** Counting window removed; gap-out now counts zero *readings* and fires on the second, i.e. 4 s after the last non-zero reading; COLLECT added with both early-exit conditions; starvation tie aligned with Q2 (first in `ORIGINAL_SEQUENCE`). He reports S1–S5 matching line for line including the times green opens and closes, not only leader/group/G. He also fixed a point outside D1–D3: §4.4 requires the count for G to be read at green-open, and `soda.py` had been using the count from decision time, 5 s (3 s yellow + 2 s all-red) earlier — 2–3 vehicles at peak. **The C# side was checked and already complies**: `CalculateGreenDuration` is called after `SetGroupState(…Green)` and `DemandOf` reads the ROI at that moment. | Itim | done | `verify_traces.py` green against the updated `soda.py` — blocked on §8.10. |
| ~~8.3~~ | ~~Starvation tie direction in C# is the reverse of the agreed Q2 tie-break.~~ **Closed 10 Sep:** `FindStarvingHead` now uses `CompareHeads`; behaviour change made before the batch per §9.3. `soda.py` already used this direction, so D3 is closed on both sides. | Kimmy | done | SodaTest §9 |
| ~~8.4~~ | ~~Per-lane gap-out (S5).~~ **Closed 12 Sep 2026 — recorded as future work, agreed by both.** The original bar compared incommensurate quantities: `IdleLaneSeconds` sums across lanes *and* across ~120 phases per run, while 84 s is 5 % of a single 1,680 s window, so "9× over" was never the right framing (Itim, 12 Sep: *"You're right that 84 s was never a fair bar for that metric"*). Measured on v2: per-run mean **752.6 s** against 784.4 s on v1, so **C1 was not inflating it**. Decomposed per phase across 108 Adaptive runs, only **46.0 s (6.1 %)** is recoverable by the cheap change — latch each lane's zero streak and end the phase when all are latched, ~15 lines with no extra clearance — which is *below* the 84 s bar it was meant to clear, and **0.0 s** in ABACEvent. **49.9 % sits in phases where at least one lane never empties at all**, which no end-condition rule can shorten: recovering it needs a head dropped to red mid-phase, with its own yellow + all-red, a mutated active group, conflict re-check, new tests, a §5 spec change and a matching `soda.py`. Not a same-merge change with 13 days to the exhibition. Granularity is settled either way — `FORCED_TOGETHER = [("A","A2")]` in `soda.py` and the shared H_A in the C# both make it **per-head**, not per-lane. **The report states it as future work with the decomposition above, never with the single 784 s figure.** | both | closed | — |
| ~~8.5~~ | ~~Field footage dates for the 39-sample headway measurement and the 298/303 detector validation are not recorded anywhere.~~ **Closed 11 Sep 2026 by Itim.** Headway: `DJI_20260714084627_0005_D.MP4`, Tuesday 14 July 2026, window 08:46–09:06, lane B morning. Detector: `DJI_20260713084049_0002_D.MP4`, Monday 13 July 2026, window 08:40–09:05, lane A morning. The DJI filename encodes YYYYMMDDHHMMSS and the `tally_*.json` files carry the date in their `source` field, so neither figure depends on anyone's memory. | Itim | done | Dates now in §4.4 and §7 L5. |
| ~~8.6~~ | ~~Re-validate timeScale after sub-stepping: same scenario and seed at 1× / 5× / 10×.~~ **Closed 12 Sep 2026.** MorningPeak rep 1, same seed, on the Mac: Adaptive **12.90 / 12.73 / 13.31 s** and Fixed **22.23 / 22.28 / 22.33 s** at 1× / 5× / 10×. 10× differs from 1× by 3.18 % (Adaptive) and 0.45 % (Fixed), inside the ~5 % bar, so 10× stands. **Read it as a bound, not as a measured timeScale effect:** batch v2 re-ran the same seed at 10× and returned 12.90 s, so the Adaptive spread across speeds (≤ 4.6 %) is no larger than its spread on repeated runs at one speed (3.2 %). The two cannot be separated — which is the stronger claim, because it says 10× adds no error beyond the frame-timing jitter already present. Fixed is the control and reproduces to 0.00 s across both speed and machine. Numbers in Appendix C0; raw files in `SimulationResults/timescale_check/`. | Kimmy | closed | — |
| ~~8.7~~ | ~~Batch v1's output exposed an implementation defect: `ServeOnePhase` resets `queueStart` only for `dropping`, so a head that stays in the green group keeps a frozen clock and wins Q1 and Q2 indefinitely.~~ **Closed 11 Sep 2026.** §4 (`for each served head`) and `soda.py`'s `end_phase` (`for h in self.green`) were both already right; only the C# was wrong. Fixed by `ResetContinuingHeads(servedLastPhase, dropping)` called after the green opens. Itim reproduced the diagnosis independently — he wrote `test_queueclock.py` against `soda.py`, drove 10 phases of permanent saturation (6 vehicles on every head, nothing ever empty) and got leaders `A Bx CB A Bx CB A Bx CB A`, served counts A 4 / B 7 / Bx 3 / CB 3, with B (the head that stays in the group across phases) reset to 30.0 at phase end and no lock-out. **Primary evidence for the report is the 27-run distribution, not the single Rep17 trace:** EveningPeak Adaptive `MaxWaitTime` ran min 126.5 / median 188.3 / max 344.4 s while Fixed never exceeded 103.9 s — Adaptive's *best* run was worse than Fixed's *worst*, 27 of 27. (Rep17 alone: 137 `StarvationEvents` over 99 phases; L13 says the counter double-counts, so ≈ 68 real episodes — still very high. 145 is the 27-run median, not a Rep17 figure.) | both | done | `QueueClockTest` + Itim's `test_queueclock.py`; batch v2. |
| ~~8.8~~ | ~~`ForceStop()` does not clear the SODA counters (107 of 108 Fixed rows carried the preceding Adaptive run's values); `phaseCounter++` sits after the `showDecisionPanel` early return so `PhaseIndex` is 0 throughout.~~ **Closed 11 Sep 2026**, both fixed, Itim independently confirmed the 107/108 figure with 0 rows differing. | Kimmy | done | `QueueClockTest` ForceStop section; `PhaseIndex` non-zero in batch v2's phase CSVs. |
| ~~8.9~~ | ~~Watch after the C1 fix (Itim): Q3 orders candidates by `queueStart`, so heads just reset become the newest and are tried last; groups may change more often and every change costs 5 s of clearance.~~ **Closed 12 Sep 2026 — it did not happen, and the effect ran the other way.** Phases per run *fell* in every scenario (MorningPeak 125.6 → 122.0, MiddayNormal 143.9 → 140.8, EveningPeak 108.7 → 89.9, ABACEvent 99.0 → 79.6) and `GreenUsed / GreenGiven` rose slightly in all four (0.931 → 0.936, 0.864 → 0.869, 0.694 → 0.696, 0.454 → 0.485). Fewer phases means *less* clearance paid, not more. 27 runs per scenario on each side, computed from the phase CSVs. | Kimmy | closed | — |
| ~~8.10~~ | ~~Itim's updated `soda.py`, `test_soda.py` and `test_queueclock.py` are not in the repository.~~ **Closed 12 Sep 2026.** All three committed under `Tests/` in `9ad7d3c` and all three green: `test_soda.py` 20/20, `test_queueclock.py` all pass, `verify_traces.py` clean. `soda.py` reproduces **all seven phases of S1–S5 in `soda_reference_trace.json` exactly**, green times to two decimal places. Constants agree three ways (trace JSON / `soda.py` / C#) on all 14, and the conflict matrix on all 6 pairs. C1 parity confirmed at code level: `end_phase` loops `self.green`; the C# reaches the same set through `ResetQueueStartFor(dropping)` + `ResetContinuingHeads(servedLastPhase, dropping)`, including the case where nothing drops. Three couplings were raised and all three are now settled (12 Sep). **(a) Fixed by Itim:** `soda.py` now derives both thresholds from a single `MAX_WAIT_SEC = 45.0` at ratios 0.95 / 0.90, exactly as the C# does, so editing the limit on either side moves both; bit-identical at 45 s and all suites still green. **(b) Documented by Kimmy:** the C# C1 reset is split across `ResetQueueStartFor(dropping)` and `ResetContinuingHeads(servedLastPhase, dropping)`; both call sites and the method doc now carry a warning that the two together are the rule and neither may be removed alone. **(c) Closed by Itim's `test_soda_rounds.py`,** which asserts the D2 gap-out timing — fires on the second zero reading, 4 s after the last non-zero — that `verify_traces.py` only printed. | both | closed | — |
| 8.11 | `StopBatchRun()` restores `Time.timeScale` but not `QualitySettings.vSyncCount`, `Application.targetFrameRate` or the GUI that `disableGUIDuringBatch` hid, so a manually stopped batch leaves the editor in batch configuration and Unity serialises `vSyncCount: 0` into `ProjectSettings/QualitySettings.asset`. Observed 12 Sep after the mis-configured 54-run batch was stopped. **No effect on any result** — `RunAllExperiments` sets `vSyncCount = 0` at the start of every batch, so v1, the §8.6 checks and v2 all ran under the same intended condition. Same family as C2: a stop path that does not undo what the start path did. | Kimmy | after the report, or before batch v3 if there is one | `StopBatchRun` restoring everything `RunAllExperiments` changed, with a test covering it; the asset back to `vSyncCount: 1`. |

---

## 9. Change procedure

This baseline is frozen for the 216-run batch, not forever. A frozen specification that turns out to be wrong and cannot be corrected is worse than no specification.

1. **Who.** Either owner may propose; both must agree. Disagreement is recorded in §8 with a deadline, not resolved by silence.
2. **What counts as evidence.** A proposal names the section, the current text, the replacement, and one of: a failing test with its output, a log column from a completed run with the value, a field measurement with sample size and date, or a code excerpt showing the document is wrong about the code. Opinion is not evidence.
3. **Behaviour vs. measurement.** A change that alters what the controller *does* (a rule, a threshold, a tie-break) invalidates every run made before it and must be dated relative to the batch: **before** it, or **after** it with a re-run. A change that only *records* more (a log column, a counter) may land at any time. §6.3 and §4.2 were behaviour changes made before the batch; `IdleLaneSeconds` and the tie counters were measurement changes.
4. **Where it lands.** Amend this file in place, **bump the document version and add a row to the Revision history at the top** (version, date, what changed, sections touched, who), add one line to the change log below if the project itself changed, and re-run `run_tests.sh` (8 suites + 2 compile passes) and `verify_traces.py`. Both must be green before the change is announced. A version number is never reused and never skipped, so any two copies of this file can be compared by their header alone.
5. **What never changes without a re-run.** ℓ₁, h, the conflict matrix, the seeded per-lane RNG, and the rule that the logger records `car.assignedLane`. These define the experiment; changing any of them starts a new one.

### Change log

| Date | Change | Kind | Evidence |
|---|---|---|---|
| 2026-09-08 | Sampled observation, all-red idle, `queueStart` per head replace tracker + rest-in-green | behaviour | `Round10` §1; SodaTest §1–§3 |
| 2026-09-10 | One clock: all decisions on the snapshot | behaviour (timing) | SodaTest §6 |
| 2026-09-10 | Counting window removed | behaviour | §6.3; SodaTest §7 |
| 2026-09-10 | Tie-break = Original sequence at Q2 and Q3 (Option A) | behaviour (Q3 only; Q2 unchanged) | §4.2; SodaTest §8 |
| 2026-09-10 | `IdleLaneSeconds`, `TieBreaksApplied`, `TieBreaksExcludingBx`, phase trace CSV | measurement | §4.5, Appendix C |
| 2026-09-10 | S4 trace corrected (anti-starvation cut at 55.67; override at phase 3); S1/S2 corrected for §6.3 | documentation | Appendix A, R4 |
| 2026-09-10 | Starvation tie-break aligned with Option A (`FindStarvingHead` → `CompareHeads`) | behaviour, before the batch | SodaTest §9; closes §8.3 / D3 |
| 2026-09-10 | `CarAgent` motion sub-stepped at ≤ 0.02 s so results do not depend on timeScale; batch speed 10× → 5× | simulation engine, before the batch | GeometryTest §10; Appendix C0 |
| 2026-09-11 | Batch v1 run and analysed (216 runs, 11 h 31 m). Headline: Fixed 41.05 s vs Adaptive 30.52 s (+25.6 %); Morning +38.0 % (t=+38.2), Midday +34.8 % (t=+41.8), ABACEvent +28.0 % (t=+8.3). EveningPeak t=−1.8 against a critical value of 2.056 at df=26: **the means do not differ significantly** — it is not a loss, and must not be written as one. Tail analysis found implementation defect C1 (Unity does not implement the §4 phase-end reset) — see §8.7. **Superseded by batch v2.** | documentation | `summary_all_runs.csv`; `SODA_CHANGE_PROPOSAL_C1.md` |
| 2026-09-10 (evening) | Batch speed 5× → 10× — sub-stepping removed the overrun that motivated 5×; `BATCH_SIM_SPEED`, scene `simulationSpeed`, SceneAuditTest all set to 10 | experiment configuration, before the batch (batch at 5× stopped at ~10 s, nothing kept) | GeometryTest §11; SceneAuditTest; Appendix C0; §8.6 still open |
| 2026-09-10 | Night removed from the batch (216 runs); MiddayNormal CB/CA set to measured 10.1 / 13.2 s | experiment inputs, before the batch | SceneAuditTest; GeometryTest §10 |
| 2026-09-11 | **C1** — `ResetContinuingHeads(servedLastPhase, dropping)` added to `ServeOnePhase`: heads that stay in the green group across phases now have `queueStart` reset like the ones that drop to red. Brings the code to what §4 always specified; no rule changed, so §9.1 did not apply. Reviewed by Itim, who reproduced the diagnosis independently against `soda.py`. | code corrected to match the frozen spec — invalidates batch v1 under §9.3 | `QueueClockTest`; Itim's `test_queueclock.py`; §8.7 |
| 2026-09-11 | **C2** — `ForceStop()` clears the SODA counters, so Fixed rows no longer carry the preceding Adaptive run's values. **C3** — `phaseCounter++` moved before the `showDecisionPanel` early return, so `PhaseIndex` is written during batch runs. | measurement | `QueueClockTest`; §8.8 |
| 2026-09-11 | Itim closed §8.2 (`soda.py`: counting window removed, gap-out on the second zero reading, COLLECT added, starvation tie aligned) and §8.5 (field dates). He also corrected `soda.py` to read the count for G at green-open per §4.4; the C# was checked and already complied. | reference implementation and documentation | §8.2; §4.4 |
| 2026-09-12 | Project moved to the Mac and rebuilt from the repository alone (Unity 6000.5.4f1). `ABAC ▸ Verify Scene Against Code` green on all three components; the nine C# suites green under mono 6.14.1 / arm64. No file was needed from outside git. | environment | Appendix D |
| 2026-09-12 | §8.6 closed — 1× / 5× / 10× same-seed validation run on the Mac, 10× confirmed. | experiment configuration | Appendix C0.2; `SimulationResults/timescale_check/` |
| 2026-09-12 | **Batch v2 run and analysed (216 runs, 11 h 31 m) on the C1-corrected code. These are the results; batch v1 is the "before" half only.** Overall Fixed 40.76 s vs Adaptive 31.60 s (−22.5 %, t(107) = 12.59, p = 7.8e-23, d = 1.21). All four scenarios significant: MorningPeak −43.5 %, MiddayNormal −36.5 %, ABACEvent −16.2 %, EveningPeak −13.7 %. EveningPeak, which in v1 showed no significant difference, is now the clearest demonstration of the C1 repair: Adaptive `MaxWaitTime` fell from a 126.5–344.4 s range to 79.0–98.0 s. Closes §8.9. | documentation | Appendix C0.1; `SimulationResults/analysis_output/` |
| 2026-09-12 | Two defects corrected in `Analysis/analyze_results.py`, neither touching any statistic. (1) The loader accepted only 7- or 8-column summary rows while `DataLogger` has written 18 since 10 Sep, so every row was silently skipped and the script could not read batch v1 either — the §5.1 figures quoted in `HANDOFF_2026-09-11.md` did not come from it. (2) The verdict line applied "lower is better" to every metric, so it reported Fixed as the significant winner on throughput when Adaptive in fact clears more. | analysis tooling | `Analysis/analyze_results.py`; Appendix C0.1 |
| 2026-09-12 | §8.10 closed — `soda.py`, `test_soda.py`, `test_queueclock.py` in `Tests/`, all green; `soda.py` reproduces all seven reference-trace phases exactly; constants agree three ways on all 14 and the conflict matrix on all 6 pairs. | reference implementation | §8.10 |
| 2026-09-12 | §8.4 re-measured on v2 and decomposed; §8.11 opened (`StopBatchRun` does not restore the globals `RunAllExperiments` set). Neither changes behaviour. | documentation | §8.4; §8.11 |
| 2026-09-12 (evening) | **§8.4 closed by agreement** — per-head gap-out is future work, reported with the per-phase decomposition rather than the single 784 s figure. Itim accepted that the 84 s threshold was not commensurate with the metric. No code changed. | documentation | §8.4 |
| 2026-09-12 (evening) | `soda.py` updated by Itim: both starvation thresholds now derived from a single `MAX_WAIT_SEC = 45.0` at ratios 0.95 / 0.90, matching the C#. Bit-identical at 45 s; `test_soda.py`, `test_soda_rounds.py`, `test_queueclock.py` and `verify_traces.py` all green and S1–S5 unchanged. Closes §8.10 coupling (a). | reference implementation | §8.10 |
| 2026-09-12 (evening) | `test_soda_rounds.py` added — replays S1–S5 as assertions and covers the D2 gap-out timing that `verify_traces.py` only printed. Closes §8.10 coupling (c). | test | Appendix D |
| 2026-09-12 (evening) | Comments added at both C# C1 reset call sites and on `ResetContinuingHeads`, stating that `ResetQueueStartFor(dropping)` and `ResetContinuingHeads(...)` together are the §4 rule and neither may be removed alone. Comment only, no behaviour change; all nine C# suites re-run green. Closes §8.10 coupling (b). | code documentation | `QueueClockTest`; §8.10 |

---

## Appendix A — Verification log

Everything below was recomputed from the constants in §4.6 and, where possible, executed against `soda.py` (Itim's, as uploaded 10 Sep) and the C# test suite.

### A.1 Numbers recomputed

| Item | Value | Method |
|---|---|---|
| G(1), G(4), G(5), G(7), G(9), G(10), G(12) | 8.00, 12.19, 14.50, 19.12, 23.74, 26.05, 30.67 | formula; `soda.green_for` agrees |
| Saturation flow | 3600 / 2.31 = 1,558.4 | arithmetic; 1,561 in Round 9 log was wrong, now corrected |
| 0.95 × 45, 0.90 × 45 | 42.75, 40.5 | arithmetic |
| S1 round | 4 + 7 + 8 + 5 = 24.0 s | rules §3 |
| S2 round | 4 + 8 + 5 = 17.0 s | rules §3 |
| S3 round | 2 + 12.19 + 5 = 19.19 s | rules §3 |
| S4 phase 2 cut | 35.67 + 20 = 55.67; CB wait 55.67 ≥ 40.5 | rules §4.5; `soda.should_end_early` → `ANTI_STARVATION` |
| S5 idle | analytic 16.17; logged 16.05 (2 s grid) | rules §4.5; SodaTest §5 |

### A.2 Where the source documents disagree — and which was wrong

| Ref | Disagreement | Resolution |
|---|---|---|
| **D1** | `soda.py` implements a counting window (`MIN_LEADER_COUNT = 3`, `COUNTING_WINDOW_SEC = 4.0`); C# and `SODA_v1.1_AS_BUILT.md` removed it. | **`soda.py` is behind.** As shipped it returns `None` for S1 and S2 until the window elapses. Verified by `verify_traces.py`. Owner Itim, §8.2. |
| **D2** | Gap-out timing. `soda.py` fires when `t − empty_since ≥ 4.0` with `empty_since` = first zero reading → fires at the **third** zero reading (6 s after the last non-zero). C# fires at the **second** zero reading (4 s after the last non-zero). Both sides read "2 snapshots = 4 s" from the as-built doc and implemented it differently; the doc was ambiguous. | **Code wins: second consecutive zero reading.** Verified: `soda.py` sequence [3,0,0,0] fires at t = 8.0; SodaTest §2 fires at the second zero. Wording fixed in §4.5. Owner Itim, §8.2. |
| **D3** | Starvation ties. `soda.py` sorts starving heads by (−wait, order) → first in order wins; C# used `>=` → last in order won. | **Resolved by amending the code** (the one case where the code was wrong against an agreed decision): C# now uses `CompareHeads`, matching `soda.py` and Option A. SodaTest §9. |
| **D4** | Head model. `soda.py` treats A2 as its own head (5 heads); C# has 4 heads with H_A = A + A2 and can never select "A2" alone as leader. | Code wins. For Q4 both take a per-signal max, so G agrees; only leader eligibility differs, and only when A reads 0 and A2 > 0. Not exercised by S1–S5. |
| **D5** | `SODA_SPEC_kimmy.md`: 5 s idle sampling; stop sampling detected heads; four ordering rules; counting window. | All superseded. §6.1, §6.3. The uploaded `AdaptiveController.cs` still matches this spec, which is how it was identified as pre-SODA. |
| **D6** | `SODA_v1.1_AS_BUILT.md` §8 says the tie-break is open; the code now implements Option A. | Document was behind the code by one day. Closed here, §4.2. |
| **D7** | The `AdaptiveController.cs` Itim reviewed (from `origin/main` at `0fee0a3`) had `gapOutDelay = 3f`, `countingWindow`, `GetOldestArrivalTime`, rest-in-green. | **It was not SODA**, which is why every "code wins" resolution above refers to the file containing `ORIGINAL_SEQUENCE`, `gapOutSnapshots = 2`, `collectWindow = 7`. **Historical since 11 Sep (v1.4):** that file is now what `origin/main` carries — §8.1 closed. The resolutions are unaffected; only the reason for the caveat has expired. |

### A.3 Where this author's own earlier drafts were wrong

| Ref | Error | Correction |
|---|---|---|
| R1 | S1 (8–10 Sep) included a 4 s counting window: round 28 s. | 24 s. |
| R2 | S2 included the window and let counts grow to A=3, B=2 during it: round 22.88 s, G 9.88. | 17 s, G 8.00. |
| R3 | S4 phase 1 stated T=0 readings A=6, B=3 but computed G from A=12, B=5. | T=0 readings are A=12, B=5, Bx=9, CB=7. |
| R4 | S4 phase 2 ran to its full 23.74 s and phase 3 chose CB by Q2. | Anti-starvation cuts phase 2 at 55.67 (CB has waited 55.67 ≥ 40.5); phase 3 selects CB by Q1 override. `soda.py` agrees. |
| R5 | S5 stated the log records 16.17. | The log records 16.05: `LaneEmptyAt` is the first *zero reading* (10.0), not the analytic 9.88. |
| R6 | `soda_reference_trace.json` (10 Sep) carried R1–R4. | Regenerated to match this document. |

### A.4 Could not verify

- The C# rules were checked by the 8-suite harness (`run_tests.sh`, all green on 10 Sep) and by reading the working-tree source; they were **not** executed in Unity for S1–S5 second by second. The traces are derived from the rules, then cross-checked against `soda.py` at every decision point. An end-to-end Unity replay of S1–S5 is not available.
- Whether a vehicle has left the ROI by a given snapshot depends on car-following and ROI geometry, not on the controller. Every reading in §5 is stipulated. A different reading changes `EndReason` at boundaries (e.g. S2 at T = 12.0, where G expiry and a second zero reading coincide).
- Field dates for the headway sample (39) and the detector validation (298/303) — §8.5.
- The frequency of the D3 starvation-tie case — no counter exists.

---

## Appendix B — Document reconciliation

| Document | Status | Use it for |
|---|---|---|
| `SODA_SPEC_kimmy.md` | superseded | the record of what v1.0 proposed and why each part was dropped (§6) |
| `SODA_Algorithm_Specification.md` | superseded | same |
| `SODA_v1.1_AS_BUILT.md` (10 Sep) | superseded by this file | nothing — its content is here with §8 closed and D2 clarified |
| `soda.py`, `test_soda.py` | reference implementation, **behind** on D1–D3 | decision-point checks via `verify_traces.py`; must be updated per §8.2 |
| `AdaptiveController.cs` (working tree = `origin/main`, `2b893e5`) | **the code** | every [code] tag above |
| `AdaptiveController.cs` (`0fee0a3`, superseded 10 Sep) | historical — pre-SODA | nothing; kept only to explain D5 and D7 |

---

## Appendix C0 — Batch configuration and results (as of 12 Sep 2026)

> **Batch v1** ran 11 Sep, 216 runs, 11 h 31 m, at the configuration below. It is superseded: it was produced by code carrying defect C1 (§8.7). Its files are kept in `SimulationResults/batch_v1/` as the "before" half of the before/after comparison and **must not be cited as results**.
>
> **Batch v2** ran 12 Sep on the corrected code, same configuration, 216 runs, 11 h 31 m, on the Mac. Integrity checked before analysis: 216 summary rows, 216 detailed CSVs, 108 phase CSVs, 108 Fixed / 108 Adaptive, 54 per scenario, no duplicate (controller, scenario, rep). **These are the results.** Files in `SimulationResults/`, analysis in `SimulationResults/analysis_output/`.
>
> Run the whole of a batch on one machine. Sub-stepping makes motion resolution independent of `timeScale`, but the sub-step sequence still depends on the frame times the machine happens to produce, so runs are statistically equivalent across machines rather than bit-identical.


| Item | Value | Provenance |
|---|---:|---|
| Scenarios | **4** — MorningPeak, EveningPeak, MiddayNormal, ABACEvent | Night removed 10 Sep by Kimmy: all its rates were placeholder levels and night traffic is outside the project's question. It remains as T12 in the manual test library. [code `ScenarioManager.LoadABACScenarioPreset`] [test GeometryTest §10 — preset count 4, no "Night"] [test SceneAuditTest — the scene's serialized list has 4 entries and no "Night"] |
| Repetitions per scenario | 27 | [code `BATCH_REPETITIONS`] |
| Controllers | Fixed, Adaptive (SODA) | |
| **Runs** | **4 × 27 × 2 = 216** | |
| Per run | 120 s warm-up (discarded) + 1,800 s measured | [code `BATCH_WARMUP`, `BATCH_RUN_DURATION`] |
| Pairing | `seed = StableHash(scenario) + rep × 31`; Fixed and Adaptive of the same (scenario, rep) share it | [code `RunSingleExperiment`] |
| `simulationSpeed` | **10×** (5× for a few hours on 10 Sep, then back to 10×) | Kimmy observed vehicles overrunning lanes and stop lines at 10×; 2× clean, 5× acceptable by eye. Root cause: `CarAgent` integrated once per frame with `Time.deltaTime`, so at 10× / 60 fps each step was 0.167 s ≈ 2.3 m for a 14 m/s vehicle. **Fixed 10 Sep by sub-stepping** — every frame is split into steps of at most `CarAgent.MAX_STEP` = 0.02 s, so decision resolution no longer depends on timeScale. [code `CarAgent.Update` / `Step` / `SubStepsFor`] [test GeometryTest §10 — 1×→1 step, 5×→5, 10×→9, cap 50] With sub-stepping in place Kimmy set the batch back to 10× (10 Sep, evening); at 10× / 60 fps each frame is split into 9 steps of ≤ 0.02 s (≈ 0.26 m per step at 14 m/s). The 1× / 5× / 10× same-seed check (§8.6) is still owed; if it diverges by more than ~5 % the batch stops and restarts at 5×. |
| MiddayNormal CB / CA | **10.1 s / 13.2 s** mean interval | [measured — Itim, 10 Sep]. Until 10 Sep the scenario still carried the placeholder `low` = 7 s, although `SIM_HANDOFF_v1` §9 claimed the measured values were "used" — that claim was wrong. [test SceneAuditTest — reads 10.1 / 13.2 from the scene] |
| Wall-clock estimate | 216 × 1,920 s = 414,720 s of simulation → **≈ 11.5 h at 10×** (≈ 23 h at 5×), plus Unity frame overhead — the on-screen estimate is the authority | arithmetic |

### C0.1 — Batch v2 results (12 Sep 2026)

Average waiting time, 108 paired runs, produced by `Analysis/analyze_results.py`.

| | Fixed | Adaptive | reduction | paired t | p | Cohen's d |
|---|---:|---:|---:|---:|---:|---:|
| **Overall** | 40.76 | 31.60 | **22.5 %** | t(107) = 12.59 | 7.8e-23 | 1.21 |
| MorningPeak | 22.81 | 12.89 | 43.5 % | — | 7.0e-29 | 11.01 |
| MiddayNormal | 22.69 | 14.40 | 36.5 % | — | 1.3e-26 | 8.99 |
| ABACEvent | 92.27 | 77.29 | 16.2 % | — | 2.0e-06 | 1.17 |
| EveningPeak | 25.28 | 21.81 | 13.7 % | — | 4.0e-21 | 5.48 |

95 % CI of the overall difference [7.72, 10.61]; Wilcoxon p = 2.6e-18. The differences are not normal (Shapiro-Wilk p = 5.1e-10), so quote Wilcoxon alongside the t-test. Maximum waiting time: Fixed 151.27 s vs Adaptive 111.01 s (−26.6 %, p = 1.8e-26). Throughput: Adaptive clears **more** — 1,244.1 vs 1,201.5 vehicles per run (p = 1.6e-06).

**What changed against v1, and why it is attributable to C1.** Fixed is the control: it contains no C1 code and reproduces within 1.2 % in every scenario across both the machine change and the batch (22.77→22.81, 22.66→22.69, 25.40→25.28, 93.35→92.27). Adaptive moved far outside that envelope.

| | v1 (pre-C1) | v2 (post-C1) |
|---|---|---|
| EveningPeak, Adaptive mean wait | 25.95 s | **21.81 s** |
| EveningPeak vs Fixed | −2.2 %, t = −1.8 — **not significant** | **+13.7 %**, p = 4.0e-21 |
| EveningPeak, Adaptive `MaxWaitTime` min / median / max | 126.5 / 188.3 / 344.4 | **79.0 / 86.2 / 98.0** |
| EveningPeak, Fixed `MaxWaitTime` min / median / max | 92.7 / 96.3 / 103.9 | 92.1 / 96.4 / 103.9 |

In v1 the best Adaptive run was worse than the worst Fixed run. In v2 the **worst** Adaptive run (98.0 s) beats the worst Fixed run (103.9 s) and the median leads by 10 s.

**Two caveats that must travel with these numbers.**

1. **ABACEvent cannot be compared across batches.** Adaptive spawned 1,501 vehicles in v1 and 1,378 in v2, because queues block the spawn points; the offered load differs, so the rise from 67.26 s to 77.29 s is not a regression that can be read directly. Within v2 Adaptive still wins on both axes — 77.29 s vs 92.27 s and 1,379 vs 1,221 cleared. Always report throughput beside delay for this scenario.
2. **`StarvationEvents` double-counts** (Appendix A L13). The analysis prints 7,887 across 108 Adaptive runs; halve it before quoting a number of events. It counts the anti-starvation mechanism *firing*, which is the mechanism working, not lanes being starved.

**The 45 s guarantee is a per-head bound, not a per-vehicle one.** `queueStart` records when a lane's queue formed or when it was last served — never when the oldest vehicle arrived — so on a lane that does not fully discharge, individual vehicles can wait past the threshold while the head's own clock is re-armed each phase. `MaxWaitTime` measures vehicles. In v2, **64 of 108 Adaptive runs exceed 60 s of `MaxWaitTime` against a 45 s setting**. C1 shrank this sharply (EveningPeak 344.4 → 98.0) but did not remove it, and the report must say so in one sentence rather than leave the reader to find it. Raised by Itim, 12 Sep.

### C0.2 — Timescale validation (§8.6, 12 Sep 2026)

MorningPeak, rep 1, `seed = 1879586351`, one Fixed and one Adaptive run at each speed, all on the Mac. `TotalSpawned` was 859 in all six runs, so the arrival stream is identical and only frame timing differs.

| speed | Fixed avg wait | vs 1× | Adaptive avg wait | vs 1× | wall clock |
|---|---:|---:|---:|---:|---:|
| 1× | 22.23 | — | 12.90 | — | 64 min |
| 5× | 22.28 | +0.22 % | 12.73 | −1.32 % | 13 min |
| 10× | 22.33 | +0.45 % | 13.31 | +3.18 % | 6 min |

Batch v2's own run of the same seed at 10× returned Fixed 22.33 (identical) and Adaptive 12.90. Fixed therefore reproduces exactly; Adaptive's repeat spread at one speed (3.2 %) is the same size as its spread across speeds (≤ 4.6 %), so the timeScale effect cannot be separated from frame-timing jitter and 10× adds nothing beyond it. **Do not write "10× deviates by 3.18 %"** — the same-speed rerun contradicts it.

---

## Appendix C — Logging schemas (exact, from `DataLogger.cs`)

`summary_all_runs.csv` — one row per run, appended:

```
ControllerType,Scenario,RunTimestamp,TotalSpawned,VehiclesCleared,AvgWaitTime,MaxWaitTime,
StarvationEvents,StartupLostTime,SaturationHeadway,SnapshotInterval,CollectCount,
CollectDuration,SkippedHeads,QueueStartResets,IdleLaneSeconds,TieBreaksApplied,TieBreaksExcludingBx
```

`phases_<controller>_<scenario>_<timestamp>.csv` — one row per green phase:

```
ControllerType,Scenario,PhaseIndex,StartTime,Group,GreenGiven,GreenUsed,EndReason,LaneEmptyAt,IdleLaneSeconds
```

`detailed_<controller>_<scenario>_<timestamp>.csv` — one row per vehicle (ground truth):

```
ControllerType,Scenario,Timestamp,Lane,DriverType,WaitTime
```

`StartupLostTime` and `SaturationHeadway` are written per row so every result line proves its own calibration. If the header of an existing `summary_all_runs.csv` differs from the current schema, the old file is renamed `summary_all_runs_OLD_<timestamp>.csv` rather than appended to. [code `DataLogger.ExportSummaryCSV`]

---

## Appendix D — Test suites (C#: `run_tests.sh`, 2 compile passes + 9 suites; Python: 4 suites — all green 12 Sep 2026 on macOS / mono 6.14.1 / arm64)

| Suite | File | Guards |
|---|---|---|
| ConflictTest | `Test.cs` | conflict matrix, test-case library, A/A2 5.4 % split |
| FixedCycleTest | `Test2.cs` | the 106 s fixed baseline; no conflicting greens over two cycles |
| BatchMathTest | `Test3.cs` | batch arithmetic |
| EtaTest | `Test4.cs` | run-time estimate |
| SharedLaneTest | `Test5.cs` | A/A2 one light; shared ROI; group construction after a snapshot |
| GeometryTest | `Test6.cs` | scene invariants; measured-parameter guard; 32 m shared lane; ROI outline |
| SceneAuditTest | `Tests/SceneAuditTest.cs` | `SampleScene.unity` vs code, every serialised scalar field |
| SodaTest | `Tests/SodaTest.cs` | §1 queueStart reset · §2 gap-out two readings · §3 no tracker API · §4 decision panel · §5 idle green · §6 one clock · §7 one accumulator · §8 tie-break and counters · §9 starvation tie direction |
