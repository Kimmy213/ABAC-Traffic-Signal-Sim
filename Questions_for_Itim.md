# Questions for Itim - data the simulation module still lacks

**From:** Badin (simulation module)
**To:** Itim (field measurement module)
**Date:** 23 Aug 2026
**Reference:** `SP1_HANDOFF_for_Badin.md`

---

## Short version first

The handoff closed a great many gaps. Lanes A / Ax / B / Bx now have real values for both morning and
midday. What remains is **six empty cells** that block running the full experiment. They are ordered by
importance below.

With just the **two P0 items** I can run immediately; the rest raise the quality.

---

## P0 - required, or nothing can run

### 1. The share of through traffic using the inner lane vs the outer lane (the one shared with Ax)

**Why it matters:** this single variable determines the headline result of the whole project.

I am about to add a new movement, `A2`: vehicles going **straight** that nonetheless use the **outer**
lane, the same one as Ax. An `A2` vehicle stops at the red, so the `Ax` vehicles arriving behind it are
trapped, even though Ax never has to wait for a signal at all.

Get this share wrong and every Ax delay figure is wrong.

**What I need:**

| Wanted | Unit |
|---|---|
| **Through** vehicles using the inner lane (next to the median) | count or % |
| **Through** vehicles using the outer lane (shared with those turning into the campus) | count or % |

**Fastest way to measure it:** open the drone clip, take the same five-minute window already counted, and
for vehicles that **go straight through the junction**, note which lane they queued in. Nothing has to be
recounted from scratch.

Tell me too if the outer lane is wide enough for two parallel rows (motorcycles filtering on the left),
because that needs a different model.

**If there is no time:** I will use 50/50 for now and run a sensitivity at 30/70 and 70/30, but the report
then has to say "assumed", which is much weaker than "measured".

---

### 2. Arrival rates for CB and CA

**Why it matters:** they are 2 of the model's 6 movements and are currently pure guesswork.

- `CB` = leaving the campus **turning towards B** (signalised, with its own 31 s phase)
- `CA` = leaving the campus **turning towards A** (free-flowing, no signal)

**What I need:** just the mean headway in seconds, in exactly the format you sent last time. **Still
waiting on Monday's data** (you said you would film lane C at midday and in the evening - see
reply_to_badin_4.md item 6).

| | morning | midday | evening |
|---|---|---|---|
| CB (s/veh) | closed (confirmed) | ? | ? |
| CA (s/veh) | closed (confirmed) | ? | ? |

**Update (reply_to_badin_4.md):** the lane C exit really does close, but **only 08:30-09:00, about 15-30
minutes**, not all morning as previously stated. That happens to coincide exactly with the measured peak
(08:15-09:00), so the MorningPeak scenario can still use CB and CA at roughly 0. Simulating the wider
morning (before and after 08:30-09:00) would need CB/CA opened at rates we do not have yet - Monday's data.

---

## P1 - would make the report much stronger

### 3. Signal timing for the remaining phases - answered (reply_to_badin_4.md, 24 Aug)

Measured: **CB = 40 s green / 70 s red, so a cycle of about 115 s.** Very useful; the model was updated
accordingly.

What is still missing is the other phases in the same cycle. Standing in the same spot for another three
or four cycles would complete it:

| Phase | green (s) | yellow (s) | all-red (s) |
|---|---|---|---|
| Direction A (from the campus front, heading east) | | | |
| Direction B (opposite A) | | | |
| Bx (right turn from B) | | | |
| CB (leaving the campus) | **40** | | |
| **Whole cycle** | | | **~115?** |

**An interesting detail:** my model guessed Bx at **30 s** with nothing to go on, and you inferred **about
30 s** from queue behaviour. They agree by coincidence. If a direct measurement gives about 30 again, that
goes into the report as independent validation of the baseline.

---

### 3.1 Update 24 Aug - I timed the signal myself and my numbers differ from yours; please check

I (Badin/Kimmy) went and timed the real signal, because only lane C has a countdown display (A, B and Bx
have none, so they have to be timed by hand). I got:

| Lane | green | yellow | red |
|---|---|---|---|
| C (=CB) | **31 s** | **3 s** | **75 s** (C cycle total 109 s) |
| A | about **50 s** | | |
| B | about **90-95 s** (not at all confident, no display) | | |
| Bx | about **20 s** | | |

Checking it against itself, something does not add up: when C had 25 s of red left (nearly green), I
counted B as still having 45 s of green. If true, B and C would overlap green by about 20 s, which must not
happen (B and CB cross each other). I believe my B figure is simply miscounted for lack of a display,
rather than the real signal overlapping - but please confirm.

More worrying: **lane C itself, which has a real countdown display and should be the most reliable, I
counted at 31 s green, while your earlier drone measurement gave 40 s** - nearly 25% apart. The yellow at
3 s matches exactly (reassuring about my counting), but the green and the cycle clearly differ (my 109 s
against the measured 115 s).

**Answered (24 Aug):** Itim re-analysed the video I shot and got A=40s / B=65s / Bx=20-23s / CB=31s green,
with CB red = 73.5s (two passes giving identical figures, confirming it is genuinely fixed-time, not
actuated). Very close to my own timing. `FixedController.cs` is now 40/21/31 (107 s including intergreen),
cross-checked against the computed red (107-31-3 = 73 s) against the 73.5 s measured on video - 0.5 s apart.
Closed.

---

### 4. Confirming the flashing amber - answered (reply_to_badin_4.md, 24 Aug)

**Confirmed:** the signal only operates 05:00-09:00 and 15:00-18:00, 7 hours out of 24, or 29% of the day.
The rest of the time it flashes amber, consistent across all 4 clips filmed. This is as significant as
feared. It directly affects the scope of MiddayNormal (T10), because the clip used to measure its arrival
rates (11:37-12:00) falls squarely in the flashing period. We have to decide how to report T10 (see the
question to the user further down the main chat). No further answer needed from Itim on this.

---

### 5. Vehicle type mix per lane (for PCU)

Known so far: **Ax is 39% motorcycles.**

The other lanes are missing. The model weights by PCU (car 1.0 / motorcycle 0.33 / heavy 2.0), and applying
39% to every lane would distort capacity.

| Lane | car % | motorcycle % | heavy (large pickup / bus / lorry) % |
|---|---|---|---|
| A (through) | | | |
| Ax (into the campus) | | **39** | |
| B (through) | | | |
| Bx (right turn) | | | |
| CB | | | |
| CA | | | |

It does not need to be accurate to 1%; +/-5% is fine.

---

## P2 - if there is time left over

### 6. The security staff directing traffic - can it become a number?

The handoff says they wave left-turning traffic through **40-60% of the time**, which is too wide a range
to model.

Even ten minutes with a stopwatch would give something usable:

- **how many times** they wave in 10 minutes
- **how many vehicles** go through each time
- what colour that direction's signal is when they wave (red, or already green)

With those I can model it as intermittent extra capacity. Without them I will write it up as a limitation:
"manual direction by staff is not modelled".

---

### 7. The real saturation headway (discharge rate)

The model currently uses the HCM standard of **2.0 s/veh** with **2.0 s** startup lost time. Those are
American figures; Thai traffic with motorcycles filtering is usually **lower**.

**How to measure it (about 10 minutes):** when the light turns green on a long queue, note the second at
which vehicles 4, 5, 6, 7 and 8 cross the stop line. The mean gap between them is the saturation headway.

Three or four cycles is enough, on whichever lane has the longest queue - probably Bx.

**Why it is worth it:** this number affects **every result** in the report. If the real figure is 1.6 s
rather than 2.0 s, actual capacity is 25% higher than the model assumes - and it is exactly the sort of
number examiners ask the source of.

---

### 8. Two or three physical distances

Known so far: **Bx to the next junction is about 78 m, holding about 12 vehicles.**

What else would help (Google Maps is fine; no need to measure on site):

- the length of the shared A/Ax lane, from the stop line back to where vehicles begin to separate (**this sets how many Ax vehicles can be trapped**)
- lane width (about 3.0 m or about 3.5 m)
- the distance from the campus gate to the CB stop line

---

## What I can confirm back (answering section 11 of the handoff)

| Question | Answer |
|---|---|
| **1.** Can Ax be blocked by the A queue? | **Yes.** Car detection uses real distance, not lane membership, so putting two streams at the same coordinates makes them block each other. It is being built; only item 1 above is missing. |
| **2.** Is the shared lane a limitation? | **It need not be.** It can be modelled properly. |
| **3.** Opening and closing routes per scenario | **Yes.** Setting the rate to 0 closes a route, used for lane C during 08:30-09:00 (corrected from "all morning"). Soi Pa Kook opens **15:30-17:00** (corrected from 16:00) and is not yet wired into any scenario (out of scope for now). |
| **4.** The green split the model uses | Updated 24 Aug to a **107 s** cycle: A+A2+B 40 / B+Bx 21 / CB 31 / 15 s of yellow and all-red, from Kimmy's field timing confirmed by Itim on video. |
| **5.** Do we model the next junction? | **No**, out of scope, but the report will carry a caveat that the model assumes Bx discharges freely while the real one suffers spillback, so **the Bx delay figures are a lower bound; reality is worse**. |

---

## What I derived from Itim's data

**The shared lane is not full - it is blocked.**

| | A (shared lane) | Ax | total | v/c |
|---|---|---|---|---|
| morning | 96 veh/hr | 712 veh/hr | 808 | **0.56** |
| midday | 62 veh/hr | 372 veh/hr | 434 | **0.30** |

Both v/c figures are low, so **the road copes easily**. The problem is that a handful of A vehicles waiting
at a red obstruct hundreds of Ax vehicles that have no reason to wait at all.

- in the morning, the chance an Ax vehicle arrives to find an A vehicle blocking it is **86%**
- on average about **12.8** Ax vehicles are trapped behind the queue

**This is a blocking problem, not a capacity problem** - and it is why item 1 is the most important thing
in this document.

**Bx is the real bottleneck.**

Bx receives **12.0 vehicles per cycle**, exactly the storage available to the next junction (12 vehicles in
78 m), so **v/c = 1.00 with no margin at all**. Meanwhile A genuinely needs about 11 s of green and gets 45.

**Moving about 5 s of green from A to Bx brings Bx's v/c down to 0.86.** That is the project's policy
proposal, and it is something the adaptive controller **ought to work out for itself** without being told.
If it does, that proves adaptive wins.

---

## Reply

Answer whichever item you like first; there is no need to wait for a complete set.
**Items 1 and 2** are all I need to run the full experiment.
