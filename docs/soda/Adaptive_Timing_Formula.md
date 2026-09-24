# Green time formula for the Adaptive Controller

> **SUPERSEDED - 10 September 2026.** The decision logic described in this file is the pre-SODA controller (rest in green, per-vehicle arrival times, counting window, 3 s gap-out). The reference is now **`SODA_BASELINE.md`**. Numbers that are still current (l1 = 2.95 s, h = 2.31 s/veh, G = clamp(2.95 + D x 2.31, 8, 45), the 106 s fixed cycle, the conflict matrix) are unchanged; everything about *how the controller decides* is not.

**ABAC Smart Traffic Light - Digital Twin (T-intersection, ABAC Bang Na front gate)**
Source: `Assets/Scripts/AdaptiveController.cs`
Document version: 2 Sep 2026

---

## 1. The main formula (HCM-based green time)

Green time is not set in advance. It is recomputed at the start of every phase from the number of
vehicles detected in the ROI at that second.

$$
G_{\text{raw}} = \ell_1 + D \times h
$$

$$
G = \mathrm{clamp}(G_{\text{raw}},\; g_{\min},\; g_{\max})
$$

| Symbol | Meaning | Value | Source |
|---|---|---|---|
| $\ell_1$ | startup lost time - time wasted after green before the first vehicle moves | **2.95 s** | **measured at this junction** (5 fully queued greens, 39 samples, approach B in the morning) |
| $h$ | saturation headway - mean time between vehicles crossing the stop line from a full queue | **2.31 s/veh** (sd 0.81) | **measured at this junction** |
| $D$ | demand - vehicles in the critical lane's ROI when the phase begins | runtime | `ROIZone.GetVehicleCount()` |
| $g_{\min}$ | minimum green | 8 s | standard practice |
| $g_{\max}$ | maximum green per phase | 45 s | standard practice |

> **Important for the report:** $\ell_1 = 2.95$ s is **1.5 times** the HCM default of 2.0 s, and
> $h = 2.31$ s likewise exceeds the HCM 2.0 s, so the saturation flow at this junction is only
> $$s = \frac{3600}{h} = \frac{3600}{2.31} = 1{,}558 \text{ veh/hr/lane}$$
> against the HCM figure of 1,800 veh/hr/lane, **13% lower**. The main cause is motorcycles filtering to
> the head of the queue, which have to clear before the cars can move.
> **Using our own measurements instead of standard values is one of this project's main selling points.**

### 1.1 Several lanes green at once (concurrent phase)

When one phase opens several lanes (A + A2 + B, for instance), they all discharge **in parallel** rather
than one after another, so the green must suit the **longest queue** (the critical lane).

$$
G = \mathrm{clamp}\left(\max_{i \in \text{group}} \left(\ell_1 + D_i \times h\right),\; g_{\min},\; g_{\max}\right)
$$

The actual code:

```csharp
float CalculateGreenDuration(List<int> group)
{
    float maxNeeded = gMin;
    foreach (int s in group)
    {
        float d = DemandOf(s);
        if (d <= 0f) continue;
        float needed = startupLostTime + d * saturationHeadway;
        if (needed > maxNeeded) maxNeeded = needed;
    }
    return Mathf.Clamp(maxNeeded, gMin, gMax);
}
```

### 1.2 The inverse - vehicles discharged per phase

Used when analysing results and comparing against Fixed:

$$
N = \frac{G - \ell_1}{h} = \frac{G - 2.95}{2.31}
$$

### 1.3 Resulting values

| $D$ (vehicles in the ROI) | $G_{\text{raw}}$ | $G$ after clamping |
|---:|---:|---:|
| 0 | 2.95 | **8.00** (at $g_{\min}$) |
| 1 | 5.26 | **8.00** (at $g_{\min}$) |
| 2 | 7.57 | **8.00** (at $g_{\min}$) |
| 3 | 9.88 | 9.88 |
| 5 | 14.50 | 14.50 |
| 8 | 21.43 | 21.43 |
| 10 | 26.05 | 26.05 |
| 15 | 37.60 | 37.60 |
| 18 | 44.53 | 44.53 |
| 19 | 46.84 | **45.00** (at $g_{\max}$) |
| 25 | 60.70 | **45.00** (at $g_{\max}$) |

**The formula is genuinely linear over** $D = 3 \dots 18$ vehicles.
- $D \le 2.19$ -> pinned at the floor $g_{\min}$
- $D \ge 18.20$ -> pinned at the ceiling $g_{\max}$ (the lane is saturated and cannot clear in one phase)

---

## 2. The green actually given - $G$ is only a ceiling

This is where readers of the report usually go wrong: **the computed $G$ is a maximum, not necessarily
what is used.** A phase can be cut short by three conditions, so

$$
G_{\text{effective}} = \min\big(G,\; t_{\text{gap-out}},\; t_{\text{starvation}},\; t_{\text{max-green}}\big)
$$

| Mechanism | Condition for ending early | Parameter |
|---|---|---|
| **Gap-out** | every lane in the group empty (`D = 0` together) for `gapOutDelay` | 3 s |
| **Anti-starvation interrupt** | green has run $\ge$ `minGreenBeforeInterrupt` **and** another lane has waited $\ge 0.90 \times$ `maxWaitTime` | 20 s / $0.90 \times 45 = 40.5$ s |
| **Max continuous green** | the same group has been green for `maxContinuousGreen` **and** vehicles wait outside it | 90 s |

> `minGreenBeforeInterrupt = 20 s` stops a phase being cut so short it cannot discharge: 20 s clears
> $(20-2.95)/2.31 = 7.4$ vehicles.

---

## 3. The full decision sequence (one pass of `RunAdaptiveCycle`)

The $G$ formula is only **step 5**. The whole sequence is:

**1) Wait for demand.** With no vehicles anywhere, hold the current green (*rest in green* - nobody loses).

**2) Choose the leader lane.**

- **2a. Always check starvation first.** If a lane has waited $\ge 0.95 \times$ `maxWaitTime` $= 42.75$ s it becomes the leader immediately, and a starvation event is logged.
- **2b. Otherwise FCFS.** Choose the lane whose **first queued vehicle has waited longest**.
  $$\text{leader} = \arg\min_{s\,:\,D_s > 0} \; t_{\text{arrival, oldest}}(s)$$
  Note this is *first come, first served by arrival time*, not *the lane with the most vehicles*. That is deliberate: ordering by count would starve the small lanes.

**3) Counting window.** If the leader has fewer than `demandThresholdSkipWait = 3` vehicles, wait another `countingWindow = 4` s to accumulate demand, then pick the leader again. This avoids spending an 8 s green on one vehicle. With $\ge 3$ already present, skip the wait and go green.

**4) Build the group of lanes that can be green together (concurrent phase).**

- start from the leader
- order the remaining lanes (with at least `minDemandToJoinGroup = 1` vehicle) by oldest arrival first
- add them one at a time if they **conflict with nothing already in the group**, per the `IntersectionConfig` conflict matrix
- always force in any lane sharing the **same** `TrafficLight` component (A and A2 in the shared lane), whether or not it currently has vehicles - otherwise the system cannot see demand arriving mid-phase on a lane that already has green

**5) Clearance.** Yellow for `yellowDuration = 3` s and then all-red for `allRedDuration = 2` s, **only for the lanes that must turn red**. A lane already green and still in the new group **is not cut**, saving 5 s of intergreen whenever the groups overlap - a major advantage over Fixed, which always pays the 5 s.

**6) Turn green, compute $G$ by the formula in section 1, and enter the `GreenPhaseWithInterrupts` loop** described in section 2.

---

## 4. How this differs from Fixed

Fixed uses a rigid 106 s cycle (A+A2+B = 40 s, B+Bx = 20 s, CB = 31 s, 15 s of intergreen), so a lane gets
the same time whether 0 or 30 vehicles are waiting.

| Fixed phase | green | vehicles discharged $(G-2.95)/2.31$ |
|---|---:|---:|
| P1 A+A2+B | 40 s | 16.0 |
| P2 B+Bx | 20 s | 7.4 |
| P3 CB | 31 s | 12.1 |

These figures explain why Bx runs at $v/c = 1.51$: it can clear 7.4 vehicles per cycle but more than that
arrive. Adaptive gives Bx up to 45 s, or 18.2 vehicles, according to the real queue when it is needed.

**Three things Adaptive saves that Fixed cannot:**

1. **Idle green** - a lane with no vehicles is skipped, or gaps out in 3 s, instead of consuming its full quota.
2. **Intergreen** - lanes that still need green are not cut (step 5).
3. **Allocation by real queue** - the critical lane gets the time it needs, not what was set last year.

**The price:** no guaranteed cycle time, so drivers cannot predict it, and every lane needs a reliable
detector. That is why anti-starvation and max green are there as guardrails.

---

## 5. Every parameter (for the report appendix)

| Parameter | Value | Type | Source |
|---|---:|---|---|
| `startupLostTime` $\ell_1$ | 2.95 s | **measured** | 39 samples from 5 fully queued greens |
| `saturationHeadway` $h$ | 2.31 s/veh | **measured** | 39 samples, sd 0.81 |
| `gMin` | 8 s | practice | traffic engineering standard |
| `gMax` | 45 s | practice | traffic engineering standard |
| `yellowDuration` | 3 s | matches reality | field observation |
| `allRedDuration` | 2 s | matches reality | field observation |
| `maxContinuousGreen` | 90 s | practice | safety ceiling |
| `countingWindow` | 4 s | tuned | - |
| `demandThresholdSkipWait` | 3 vehicles | tuned | - |
| `gapOutDelay` | 3 s | practice | - |
| `maxWaitTime` | 45 s | tuned | LOS D criterion |
| `minGreenBeforeInterrupt` | 20 s | tuned | about 7.4 vehicles |
| `starvationSelectThreshold` | 0.95 | tuned | $= 42.75$ s |
| `starvationInterruptThreshold` | 0.90 | tuned | $= 40.5$ s |

---

## 6. Limitations to state in the report

1. **$D$ is read from the ROI only at the instant the phase begins**, not updated during it, so vehicles arriving after green starts are not counted in that round's $G$. This matches the limitation of a real detector measuring at the head of the queue.
2. **$h$ and $\ell_1$ were measured on approach B in the morning only** and are assumed to apply everywhere. It is not yet confirmed that the turning lanes (Ax, Bx, CB) share the headway of the through lanes; in theory theirs should be higher.
3. **No PCU weighting.** A motorcycle counts as one vehicle, the same as a car. Part of the effect is already absorbed into the measured $\ell_1$ and $h$, which came out above the standard values.
4. **No prediction.** It is purely reactive, using neither neighbouring junctions nor historical data.
