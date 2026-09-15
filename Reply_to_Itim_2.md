# Reply to Itim - the A_morning / A_midday data

## 0. Checked the numbers; every one is right

| session | mv | total | interval, all vehicles | car+heavy | moto% |
|---|---|---|---|---|---|
| A_morning | A | 66 | 13.64s ok | 18.75s ok | 27% ok |
| A_morning | Ax | 232 | 3.88s ok | 5.06s ok | 23% ok |
| A_midday | A | 38 | 23.68s ok | 29.03s ok | 18% ok |
| A_midday | Ax | 143 | 6.29s ok | 9.68s ok | 35% ok |

Agreed on using **car+heavy**: the model has no motorcycles, so the total would give too long an interval.

Ax/(A+Ax) = **78% / 79%**, confirming what you said, and genuinely stable across times of day.

---

## 1. First though - the JSON and the text disagree on the number of lanes

- **JSON + CSV** say `"A": 3` and compute `per_lane_veh_hr` = 88 / 51 (dividing by 3)
- **Item 4 in the message** says **2 lanes** per side, and the table has `A lanes = 2`

That is a 50% difference in v/c:

| | 3 lanes | 2 lanes |
|---|---|---|
| A_morning | 88 veh/hr/ln | **132** veh/hr/ln |
| A_midday | 51 veh/hr/ln | **76** veh/hr/ln |

I believe **2 lanes** from the footage, but please fix it in the tool, or every future CSV is wrong.

---

## 2. Answer to (a) - can Ax be blocked?

**Not at present, and you have put your finger on the most important point in the whole project.**

In the current model Ax spawns at z=6.5 and A at z=3.0, so they are **physically different lanes and can
never meet**. Ax therefore flows freely at all times, which does not match reality.

**The good news: the mechanism already exists.**

`CarAgent.GetCarAhead()` finds the car ahead by **lateral distance under 1.5 metres** and does not filter
by `assignedLane`. So putting Ax and through traffic in the same lane makes **the blocking happen by
itself**, with no new logic.

---

## 3. Answer to (b) - it can be done, and should be

### The smallest change that models it properly

Add a seventh movement, **`A2`**: through traffic running in the shared lane.

| | lane (z) | stopLine | signal | behaviour |
|---|---|---|---|---|
| **A** | 3.0 (inner) | yes, StopLine_A | Light_A | straight on, stops at red |
| **A2** | 6.5 (shared) | yes, StopLine_A | Light_A | straight on, stops at red - **the blocker** |
| **Ax** | 6.5 (shared) | none | none | turns, free-flowing - **the blocked** |

A2 and Ax share a z, so car-following picks it up by itself: A2 waiting at the red holds up the whole Ax
queue behind it. ROIZone already filters by `laneName`, so the ROIs can overlap and still count separately.

### What I need from you

**Please count through traffic (A) separately by whether it is in the inner or the shared lane.**

We need to know how to split through traffic between A and A2; right now I have to guess 50/50. Drivers
going straight tend to move into the inner lane because they know the outer one gets blocked by turning
traffic, so the real split probably leans inward. This figure affects the result directly, because it is
literally "how many vehicles are there to block Ax".

---

## 4. Why this matters more than it looks

A rough calculation (120 s cycle, A green 45 s and red 75 s, through traffic split 50/50):

| session | through traffic in the shared lane | Ax | through vehicles arriving on red | chance of blocking | Ax stuck per cycle |
|---|---|---|---|---|---|
| A_morning | 96 veh/hr | 712 veh/hr | 2.0 vehicles | **86%** | about 13 |
| A_midday | 62 veh/hr | 372 veh/hr | 1.3 vehicles | **73%** | about 6 |

**And here is the key point:** the shared lane's v/c is only **0.56 in the morning and 0.30 at midday**.
It is **nowhere near capacity** - the problem is *blocking*, not *capacity*.

One through vehicle stopped at a red holds an entire row of turning vehicles for 75 seconds, even though
those turning vehicles have no signal and should not be waiting for anyone.
**This is why the security staff have to go out and wave traffic through**, and the data explains it clearly.

---

## 5. A warning, though - adaptive control may make Ax **worse**

I think this is the most important thing to settle before running for real.

Our adaptive controller allocates green **by the demand it can detect**, and it only counts signalised lanes.

- A has very little demand (about 192 veh/hr across both lanes)
- Ax has a great deal (712 veh/hr) but **has no signal, so adaptive cannot see it at all**

So adaptive gives A a shorter green (few vehicles) and therefore a longer red, the through queue in the
shared lane stands longer, and **Ax gets blocked harder than before**.

If that holds, the result will be that adaptive helps A/B/Bx/CB but **hurts Ax, which is 78% of this
approach** - and the overall figure might even lose to Fixed.

**That is not bad news.** If the simulation shows it, it is a good finding:

> adaptive control that sees only the signalised streams can make an unsignalised stream worse where they
> share a lane

and it opens the way to proposing a fix as a contribution of the project: let adaptive count the queue in
the shared lane including Ax. The Ax ROI already exists; it is simply not fed into the decision yet.

**The most honest conclusion is probably this:** signal timing can only ease this problem. The real fix is
**a separate left-turn lane**, which is physical rather than a matter of timing - and that belongs in the
recommendations section of the report.

---

## 6. On scaling Monday to Tue/Wed/Thu

The "x70% as the security staff said" figure is hearsay, not a measurement.

I would **not use it as a headline number**. If it is used, it must be labelled clearly as an estimate from
hearsay. Better still, **go and record a Tuesday, Wednesday or Thursday morning properly**, or the peak
figures at the heart of the project rest on hearsay, which the examiners will certainly ask about.

---

## 7. How precise the data is right now

| session | mv | car+heavy | +/-95% | interval | plausible range |
|---|---|---|---|---|---|
| A_morning | A | 48 | +/-28% | 18.8s | 14.6 - 26.1s |
| A_morning | Ax | 178 | +/-15% | 5.1s | 4.4 - 5.9s |
| A_midday | A | 31 | +/-35% | 29.0s | 21.5 - 44.8s |
| A_midday | Ax | 93 | +/-20% | 9.7s | 8.0 - 12.1s |

**Ax is precise enough to use** (+/-15%), but **A is still very noisy** (+/-28-35%) on only 31-48 vehicles.
Another two or three bins would help a lot, especially since A is what blocks Ax, so error in A feeds
straight through to the result.

---

## 8. What I need, in order of importance

1. **Through traffic (A) counted separately for the inner and shared lanes** - this blocks implementing the shared lane
2. **Settle whether A has 2 or 3 lanes** and fix it in the tool
3. **The real signal timing** (as arranged for Monday) - still near the top
4. A Tuesday, Wednesday or Thursday morning instead of multiplying by 0.7
5. More bins for A, to cut the noise
6. B/Bx and then CB/CA, as you are already doing

This data set is very good. It turns a model that merely looks plausible into one that explains why the
security staff have to go out and direct traffic, which is a different thing entirely.
