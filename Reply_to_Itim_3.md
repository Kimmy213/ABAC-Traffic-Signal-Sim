# Reply to reply_to_badin_3

Everything that could be applied straight away is now in the model and all tests pass. Here is what was done.

---

## Item 1 - the 50/50 split is now 18/82

Changed directly in the code. It used to be `aSplit = A * 2f` (an even split); it is now

```
aInner (inner lane, A)  = A / (1 - 0.18)  = A / 0.82
aOuter (outer lane, A2) = A / 0.18
```

The outer lane now gets far fewer vehicles (its interval is about 5.6 times longer), matching the
back-calculated figure. It lives in one constant, `A2_SHARE = 0.18f`, with a comment citing where it
came from, so a single edit updates it if a direct count gives something else.

The 13% / 23% sensitivity run is not implemented yet - only the structure is in place, behind that one
variable. It will be run once we have A and A2 counted separately from the clip.

---

## Item 2 - CB/CA genuinely closed in the morning: applied

MorningPeak used `VERY_LOW` (still a trickle of vehicles). It now uses a new constant, `CLOSED`, whose
interval is large enough that essentially nothing arrives during a 30-minute run. A comment records
that this was confirmed by the security staff rather than assumed.

Waiting on Monday's lane C footage for the real rate while it is open (evening and the other scenarios).

---

## The emergency lane - implemented, not just written up as a limitation

Agreed that pure car-following overstates the blocking, so there is now a real mechanism:

- each Ax vehicle has a **65%** chance at spawn (the midpoint of the 60-70% suggested) of being marked
  as able to pass along the emergency lane
- a marked vehicle **does not treat a stopped A2 car as a blocker**, but still follows the Ax vehicles
  ahead of it normally, so the Ax queue itself does not vanish
- the draw uses the spawner's own RNG, separate from Unity's, so the same seed reproduces exactly
  between Fixed and Adaptive - which matters for the paired t-test

It is currently a global 65% (every Ax vehicle has the same chance). If there is evidence it depends on
something - time of day, driver type, queue length at that moment - say so and it can be made dynamic.
For now it is the simplification that was proposed.

**A limitation, stated plainly:** this geometry and physics logic cannot be checked by automated test,
because the harness used here (a compile check outside Unity) is a stub with no real physics. It has to
be watched by eye in Unity while running T13, to see which Ax vehicles actually pass.

---

## Item 5 - PCU and vehicle mix: useful, but not wired into Unity

The simulation does not distinguish cars, motorcycles and heavy vehicles physically at all; every
vehicle is the same `CarAgent`, and the colours mark lane groups A/B/C, not vehicle types. The PCU
figures are very useful for **writing the report** and for computing weighted capacity by hand or in
Python (analyze_results.py), but they do not yet affect simulation behaviour.

Making motorcycles faster or able to filter, or heavy vehicles take more queue space, would be a new
feature of some size. Say if you want it; for now it seems outside the scope of this round.

The "Ax motorcycles 39%" figure mentioned earlier has been corrected to 28% as advised (it is only a
note in the document, not in the code).

---

## Good news - the detector is validated

98.7% against ground truth is a very good figure, and it is now clear that the 76% mAP does not reflect
counting accuracy (a different metric). That distinction can go straight into the report, ready for the
examiners asking how counting can be accurate with such a low mAP.

Waiting on more bins from the remaining clips. With more data the noise on A (+/-28%) should fall, and
T8/T10 can be re-run to match more closely.

---

## On running three cases (Fixed / Adaptive blind to Ax / Adaptive aware of Ax)

Agreed that this is a good finding. The model currently covers only the first two cases (Fixed vs
Adaptive, which cannot see Ax because Ax has no signal). The third case, Adaptive with Ax awareness, is
not done: it needs the Ax ROI fed into AdaptiveController's decision. That is separate work, kept as the
next step once the middle case is confirmed to lose to Fixed. If it does not lose, the third case is
unnecessary.

---

## Where things stand

T8 (MorningPeak) is ready to run with the updated values: A/Ax/B/Bx measured, CB/CA genuinely closed,
the shared lane at 18/82 and the emergency lane at 65%. What is still missing is the CB/CA arrival rate
while open (evening and afternoon) and the phase timing for A/B/Bx, to complete the remaining
scenarios. No hurry - Monday's footage is soon enough.
