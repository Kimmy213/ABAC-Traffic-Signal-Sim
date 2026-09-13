# Adaptive Traffic Signal Control System
## A Camera-Based, Algorithm-Driven Approach for T-Intersections
### Case Study: ABAC University Entrance, Bang Na, Samut Prakan

**CSX3010 Senior Project 1 · Project 26008**

Warachai Aranchot (6610996) · Ratchanon Puwapattarapokin (6610909) · Badin Bangsen (6611108)

---

> **Status of this draft.** Chapters 1-5, 7 and 8 are complete, and Chapter 6
> describes the controller as implemented (SODA v1.1). Sections 6.6-6.8 were
> rewritten from the completed 216-run batch on 12 September 2026 and now carry
> results; the ⚠ passages they previously held are resolved. **The validation
> described in §6.10 has not been performed** (§7.13), and it is the one
> outstanding item in this chapter.

---

# 1 · Introduction

## 1.1 Background

Adaptive traffic signal control is not a new idea. Fixed-cycle signals allocate
green by schedule rather than by demand, and that inefficiency has been
recognised since Webster's work on signal settings in 1958. SCATS, developed in
Sydney, and SCOOT, developed at the UK's Transport and Road Research Laboratory,
both adjust cycle length and green splits from measured demand and run in
hundreds of cities. More recently, Google and DeepMind have used aggregated GPS
flow data to optimise signals across whole urban networks, including Bangkok.

None of these can be applied to the intersection studied here.

SCATS and SCOOT depend on inductive loop detectors embedded in the road surface.
Installing them means cutting the carriageway, which a university has no
authority to do and which a single entrance cannot justify economically.
City-wide GPS aggregation works at a resolution that cannot see this junction at
all. It optimises corridors and networks, not one university gate where the
dominant flow is a left turn into a car park.

This project therefore targets a specific gap: **an adaptive controller for a
single isolated junction that needs no change to the road surface and senses
demand from a camera that can be mounted on an existing pole.**

### The site is not a generic arterial intersection

Soi ABAC dead-ends about 9 kilometres beyond the junction. It is a residential
road, not a highway, and it carries no through traffic in the usual sense:
vehicles on the two main approaches are going to the university, to homes along
the soi, or to the canal-road connection. A reader who sees the name
"Bang Na-Trat" will expect highway volumes and may think the figures in
Chapter 5 are implausibly low. They are correct for a junction whose traffic is
mostly university access.

## 1.2 Problem statement

The project proposal, written before fieldwork began, stated that the
intersection "runs on a fixed 30-second cycle per phase." Fieldwork showed that
this was wrong.

| Source | Cycle length | Basis |
|---|---|---|
| Project proposal | 30 s per phase | verbal, second-hand |
| Simulation's original model | 120 s | assumed |
| Derived from a guard's verbal report | ~115 s | interview |
| **Measured** | **107.0 s** | three independent methods |

The measured plan gives 40 s of green to movement A, 65.7 s to movement B, 20 s
to Bx and 31 s to CB, in a cycle of about 107 seconds. Three independent methods
(a phone stopwatch, the countdown display on the signal, and automated extraction
of lamp colour from video sampled at 2 Hz) agree to within 3%. §2.2 gives the
full derivation.

The baseline matters because a comparison between an adaptive controller and a
fixed-time plan only means something if the fixed-time plan is real. Had the
project used the assumed 120-second cycle, it would have compared its controller
against a baseline it had invented.

With the measurements of Chapter 5, the problem can be stated exactly:

> At the ABAC entrance, the fixed signal plan allocates green time almost
> inversely to demand. Both movements serving the university are at or beyond
> capacity, while both through movements hold more than three times the green
> time they require. One movement is oversaturated to the point of queueing back
> into an adjacent intersection within approximately six minutes of peak
> operation.

## 1.3 Objectives

The system has three modules with defined interfaces, so that each can be built
and tested on its own and joined through a documented data format.

| Module | Responsibility | Input | Output |
|---|---|---|---|
| **A · Vehicle detection** | Ratchanon P. · Warachai A. | drone video | per-movement vehicle counts (JSON) |
| **B · Adaptive control** | Ratchanon P. · Warachai A. | counts | phase order and green durations |
| **C · Simulation and evaluation** | Badin | control decisions | delay, queue length, throughput |

The project's objectives are:

1. To measure, rather than assume, the demand and signal timing at the study site.
2. To build a camera-based vehicle counter accurate enough that its output can be
   used directly as simulation input.
3. To design an adaptive control algorithm that sizes green time from measured
   queues and serves movements in the order their queues formed.
4. To evaluate that algorithm against the measured fixed-time baseline in
   simulation, and to compare simulated behaviour against behaviour observed on
   site.

## 1.4 Scope and delimitations

Five boundaries apply to the results.

The drone is an instrument, not the deployment sensor. It was the available way
to get an overhead view of a junction the project does not own. A deployed system
assumes a **fixed mast- or pole-mounted camera** at a similar height and angle.
This is stated early because a reader who assumes permanent drone operation would
reasonably conclude that the system is impractical.

The drone films one approach at a time, so the whole intersection never appears
in one frame. That is enough for measuring arrival rates offline, which is all
this project needs, and it is a further argument for a fixed camera in any
deployment.

The system is scoped to dry daylight conditions. The dataset contains no
wet-weather imagery, for the reasons given in Chapter 7, and night operation is
out of scope.

Obstruction beyond the junction is out of scope. The controller sizes green from
the demand waiting at the stop line and cannot tell whether the road past the
junction can accept the vehicles it releases. An incident or a standing queue
downstream would have it granting green to a movement that cannot move. Any
demand-responsive signal has this failure mode. It is not addressed here because
no sensor in this project sees past the intersection, and adding one is separate
work.

This is the opposite direction to the spillback discussed in §7.7. That section
covers this junction's queue reaching a neighbouring one, which is measured and
reported. This boundary covers a problem elsewhere preventing this junction from
discharging, which is not modelled.

The detector counts arrivals, not departures. A vehicle is counted once, when its
tracked centre first enters a region and it has clearly moved (§4.1). Nothing in
the pipeline confirms that a vehicle released on green actually cleared the
junction. Checking that would need a second detection zone past the stop line and
a way to match vehicles between the two zones. The capacity analysis in Chapter 5
does not need it, because saturation headway was measured directly from footage
(§2.4) rather than inferred from throughput. **Wherever this report gives a number
of vehicles discharged, it is computed from the measured headway, not observed
vehicle by vehicle.**

---

# 2 · Site Description and Field Measurement

The measurements in this chapter come from several site visits in July and
August 2026, and everything that follows rests on them. Where a measurement
replaced an assumption, both are shown.

## 2.1 Site geometry and movement definitions

The ABAC entrance is a true T-intersection. The Bang Na-Trat service road runs
east-west as the main road, and the university entrance forms the stem to the
south.

The report uses six movements throughout.

| Movement | Description | Control |
|---|---|---|
| **A** | eastbound through | signalised |
| **Ax** | eastbound **left turn** into ABAC | **unsignalised, free flow** |
| **B** | westbound through | signalised |
| **Bx** | westbound **right turn** into ABAC | signalised |
| **CB** | exits ABAC, turns west onto the main road | signalised |
| **CA** | exits ABAC, turns east onto the main road | **unsignalised, free flow** |

Two of the six, `Ax` and `CA`, have no signal. Both are left turns that cross no
conflicting stream, so normal traffic engineering practice would not signalise
them. Chapter 5 shows that both are still obstructed in practice, and that this
is the intersection's characteristic failure.

### Queue storage

Only one approach is limited by road length.

The A and C approaches are long enough that their queues never run out of road,
so signal capacity, not geometry, limits those movements.

The B approach is bounded by an adjacent four-way intersection about 78 m
upstream. Once the queue fills that distance it spills into the second junction
and delays traffic that never meant to use the T-intersection. Two independent
measurements agree on the distance: site observation puts it at about 78 m, and
the longest Bx queue counted from footage is 12 vehicles, which at about 6.5 m
each is also about 78 m.

The queue fills the available road exactly. Bx therefore already runs at the
edge of spillback in normal peak conditions, and any further growth in demand
turns a local delay problem into a network one.

## 2.2 The measured signal plan

Signal timing was measured on site on 23 August 2026 by three methods, each
applied independently. A phone stopwatch lapped successive phase changes. The
countdown display, which exists only on the C head, was read directly and used to
derive the phases that have no display. Automated video analysis sampled the
colour of the signal lamp at 2 Hz from a 4 minute 52 second ground-level
recording, giving two complete cycles before the camera drifted.

| Head | Green | Amber | Red | Implied cycle |
|---|---|---|---|---|
| **A** | 40 s | 3 s | 63 s | 106 s |
| **B** | 65 s | 3 s | 35 s | 103 s |
| **Bx** | 20 s | 3 s | 84 s | 107 s |
| **CB** | 31 s | 3 s | 75 s | 109 s |

The four timings give 103-110 s, with a mean of **107.0 s**, which matches the
automated video measurement of the CB head exactly. Hand timing carries an
uncertainty of ±3-4 s, so this is as tight as the method allows.

The resulting phase structure is:

```
t =    0 – 40    A green  +  B green
t =   40 – 43    A amber      (B remains green)
t =   43 – 45    all-red
t =   45 – 65    Bx green  +  B green
t =   65 – 68    B amber  +  Bx amber
t =   68 – 70    all-red
t =   70 – 101   CB green
t =  101 – 104   CB amber
t =  104 – 106   all-red
```

Two features of this plan matter.

Movement Bx has no stage of its own. It joins movement B about 45 seconds into
the cycle, part-way through B's green. This was observed on site and confirms the
conflict matrix used in the control design: A runs with B, B runs with Bx, and CB
runs alone.

The signal is fixed-time, not vehicle-actuated. Two consecutive red phases on the
CB head measured 73.5 s and 73.5 s, identical at the resolution of the method,
where an actuated controller would vary with demand. The comparison in Chapter 6
is therefore against a fixed-time plan, not against a system that already
responds in part.

## 2.3 Operating regime: the flashing-amber finding

Interviews with security staff established the signal's daily schedule, which was
then checked against every recorded session.

| Period | Regime |
|---|---|
| 05:00-09:00 | normal red / green |
| 09:00-15:00 | **flashing amber** |
| 15:00-18:00 | normal red / green |
| 18:00-05:00 | **flashing amber** |

The signal controls the junction for 7 hours in every 24, about 29% of the
day. For the other 17 hours drivers negotiate the junction among themselves.

The schedule matched all four sessions recorded before it was known: the two
morning sessions were predicted to fall under normal signalling and did, and the
two midday sessions were predicted to fall under flashing amber and did.

### Why this motivates the project

Flashing amber presumably exists because fixed-time signalling wastes capacity
when demand is low: a full cycle at 03:00 delays vehicles that have no
conflicting traffic to wait for. The operator's answer is to switch off enforced
right-of-way altogether, which trades safety for efficiency at a junction where
accidents have already happened.

An adaptive controller removes that trade-off. It serves only the demand it
measures, so light traffic costs little delay, and there is no efficiency reason
to fall back to flashing amber at any hour.

The guard interviewed for this project raised the same point himself. Asked
whether he had anything to add, he said, unprompted, that he **dislikes the
flashing-amber mode and would prefer full red/green signalling to run 24 hours**,
for safety. A frontline operator asked for what this project proposes, and the
measurements explain why the current signal cannot provide it.

## 2.4 Locally measured traffic parameters

Capacity analysis needs a saturation flow rate. Instead of adopting published
defaults, it was measured at the junction from footage of five green phases that
began with standing queues, giving 39 saturation-headway samples.

| Parameter | Measured here | HCM 7th ed. default |
|---|---|---|
| Saturation headway | **2.31 s/veh** (sd 0.81) | 2.00 |
| Saturation flow | **1558 veh/hr/lane** | 1800 |
| Startup lost time | **2.95 s per cycle** | 2.00 |

Startup lost time is about 1.5 times the published default. That is plausible at
a junction where motorcycles filter to the front of the queue during red and must
clear the stop line before the cars behind them can move. It is treated as a
finding about local conditions rather than a nuisance parameter, because §5.4
shows that it changes the project's central result.

## 2.5 Interview evidence

A security guard stationed at the front gate was interviewed during fieldwork.
The interview was audio-recorded and a transcript kept. Five findings bear on the
analysis.

Weekday volumes differ considerably. Tuesday, Wednesday and Thursday were
described as full-strength days, Monday as about 70% and Friday as 40-50%. This is
used to describe sessions, never as a correction factor (see Chapter 7).

The Lane C exit is closed every day between 08:30 and 09:00, for 15 to 30
minutes, which forces outbound traffic onto another route. The stated reasons are
congestion and a history of accidents. §5.3 shows that this window falls exactly
on the independently measured arrival peak.

University policy requires taxis to stage across three lanes on the A approach,
including the emergency lane, while they wait to be called in. They are
stationary vehicles, not traffic, and would inflate counts on approach A if they
were counted. §4.1 describes how they are excluded.

Available exit capacity changes by time of day. An alternative outflow route
opens between 15:30 and 17:00, so the number of exit paths is not constant, which
a fixed-topology simulation does not represent.

People, not the signal, manage the junction. The guard described personally
waving left-turning vehicles into the university because straight-through
vehicles stop and block the turn lane, and estimated that he does so 40-60% of the
time. §5.6 measures the mechanism that makes this necessary.

It follows that **any "before" baseline at this junction measures the guards'
performance as much as the signal's.** The intersection currently works partly
because people make it work.

---

# 3 · Module A: Vehicle Detection

## 3.1 Data collection

Footage was collected by drone on three approaches at three times of day between
July and August 2026, giving about 6.6 hours of video in 20 session folders.
Counting sessions were flown about 6 m above the carriageway, and a separate
survey flight at 90 m captured the junction geometry and network context.

All footage is **1920 × 1080 at 29.97 fps**, encoded in HEVC. This was checked
directly in every collection folder rather than assumed, and it feeds into the
resolution argument in §3.4.

A battery lasts 20-25 minutes per flight, so each session exists as two or more
video files rather than one continuous recording. Each flight is treated as its
own session folder, which turns out to matter for the reasons in §3.3.

## 3.2 Annotation

Frames were extracted every 3.5 seconds and annotated in Roboflow by two
annotators. The final dataset has **6,585 annotated images** containing **57,260
labelled object instances**.

### The class taxonomy was reduced from five classes to three

The first dataset version used five classes: car, motorcycle, truck, bus and
special type. Evaluating that model showed that confusion between truck and car
dominated the confusion matrix.

The cause is a resolution limit, not careless annotation. From 6 m overhead, the
open bed that distinguishes a pickup truck from a saloon car covers a few pixels.
Human annotators disagree on these cases, and more labelling cannot resolve a
distinction the sensor does not record.

The classes were therefore merged to follow the **passenger car unit grouping
that the capacity analysis uses**:

| Class | PCU weight | Instances | Share |
|---|---|---|---|
| car | 1.0 | 44,622 | 77.9% |
| motorcycle | 0.33 | 11,982 | 20.9% |
| heavy | 2.0 | 656 | 1.1% |

The analysis never needed to tell a truck from a bus. It needed PCU-weighted
flow, so the model's output was aligned with that.

The heavy class is still under-represented at 1.1% of instances, although that is
four times the 160 instances in the first dataset version. §4.4 shows the effect
on session-level counting, and §4.5 shows that the small sample is not what makes
the class unreportable.

## 3.3 Dataset splitting by session

This is the most consequential decision in the dataset design.

Frames are extracted every 3.5 seconds from a stationary drone, so consecutive
frames are nearly identical: the same background, the same parked vehicles, and
often the same moving vehicles a few metres further on.

A random train/validation split puts near-duplicate images in both sets. The
model is then validated on images it has effectively already seen, and the
resulting mAP measures memorisation rather than generalisation. It will be high,
and it will mean nothing.

The split was therefore made **by session folder**, with every folder entirely in
one split.

| Split | Sessions | Images |
|---|---|---|
| Train | 12 | 4,075 (62%) |
| Validation | 4 | 1,406 (21%) |
| Test | 4 | 1,104 (17%) |

Every approach and every time of day appears in the training set. The four
validation and four test sessions are whole flights the model never saw, recorded
on different days in different light.

The split is slightly test-heavy compared with a conventional 70/20/10, because
whole sessions cannot be subdivided without reintroducing leakage. A clean 17%
test set is worth more than a contaminated 10% one.

Because the split was assigned by hand across 20 session folders using filename
filters, a script checked it before training. The script maps every exported
filename back to its session and fails if any session appears in more than one
split. Twenty manual filter operations are twenty chances for a substring to
over-match, and that error would be invisible in the interface and undetectable
in the final metric.

## 3.4 Preprocessing and augmentation

Images are auto-oriented and resized to fit within 1280 × 1280 with the aspect
ratio preserved, so a 1920 × 1080 source becomes 1280 × 720. The square canvas is
never filled, which is expected.

Resizing to the more common 640 px would be a **3× downscale** and would shrink a
distant motorcycle from about 20 pixels to about 7, below the size at which YOLOv8
detects reliably. Motorcycles are 21% of all instances in this dataset, so that
loss is not acceptable. Training at 1280 is only a **1.5× downscale** from native
and discards very little.

"Fit within" was chosen over "stretch to" because YOLO letterboxes rather than
stretches at inference, so a model trained on stretched images would see
differently shaped images at inference.

Augmentation used a 2× multiplier, producing 8,150 training images from 4,075
source images.

| Augmentation | Setting | Rationale |
|---|---|---|
| Flip | horizontal + vertical | free variety; an overhead view has no canonical orientation |
| Rotation | ±5° | matches real between-flight heading variation on a stabilised gimbal |
| Brightness | ±20% | real variance across morning, midday and evening sessions |
| Exposure | ±10% | largely redundant with brightness; kept at low cost |
| Blur | 0.2 px | negligible; daylight footage has a fast shutter |
| Noise | 0.5% | reduced, see below |

Augmentation was reduced from the first dataset version, which used a 3×
multiplier, rotation ±15% and noise 1.01%. Augmentation substitutes for real data,
and the second dataset has 2.3 times as much real data, covering three
approaches, three times of day and two months of lighting. Real variance is better
than synthetic variance.

Noise was reduced furthest because it is the augmentation most likely to do harm
here. When the target is only a few pixels across, pixel-scale noise risks
teaching the model that speckle and distant motorcycles are the same kind of
object.

Shear, heavy geometric distortion, large hue shifts, greyscale conversion and
automatic contrast adjustment were all excluded. Augmentation should mirror
variance that will actually occur at this installation. Extremes that never occur
only use up model capacity.

## 3.5 Training

The model is trained through a four-stage transfer ladder:

```
COCO  →  VisDrone  →  ABAC v1  →  ABAC v2
```

Stage one is the standard COCO-pretrained YOLOv8s. Stage two fine-tunes on
VisDrone, a large aerial-imagery dataset, to adapt the backbone to overhead views
and small objects. Stage three fine-tunes on the first ABAC dataset, and stage
four on the final dataset described above.

| Parameter | Value |
|---|---|
| Architecture | YOLOv8s (11.1 M parameters, 28.6 GFLOPs) |
| Image size | 1280 |
| Batch size | 8 |
| Epochs | 30 |
| Optimiser | AdamW, lr 0.001429 (auto-selected) |
| Early stopping | patience 15 |

The architecture comes with the checkpoint: loading the stage-three weights fixes
it regardless of configuration. YOLOv8s was kept because it produced the counting
accuracy reported in §4.2, and switching architecture late in the project would
have traded a validated result for an unvalidated one.

Because the class count fell from five to three, the detection head was partly
rebuilt. Ultralytics matched the `car` and `motorcycle` head rows by name against
the stage-three weights and kept them; only the `heavy` row was initialised from
scratch. All 355 backbone tensors transferred.

## 3.6 Detection results

Training ran for 30 epochs in 4.07 hours on an NVIDIA T4. Evaluation uses the
**test** split: four complete sessions (A_midday_2, B_midday_2,
B_evening_aug14_3, C_midday_2) that neither the model nor the validation loop saw
at any point, 1,104 images containing 5,417 labelled instances.

| | mAP@50 | mAP@50-95 |
|---|---|---|
| **Overall (test)** | **0.883** | 0.500 |
| Overall (validation) | 0.899 | 0.520 |

Validation and test agree to within 1.6%, so the model did not overfit to the
validation set during early stopping.

### Per class

Metrics are reported per class so that the under-represented heavy class stays
visible instead of disappearing into a headline figure.

| Class | Test instances | mAP@50 | mAP@50-95 | Precision | Recall |
|---|---|---|---|---|---|
| **car** | 4,066 | **0.972** | 0.608 | 0.937 | 0.923 |
| **motorcycle** | 1,281 | **0.911** | 0.438 | 0.929 | 0.854 |
| **heavy** | 70 | 0.766 | 0.456 | 0.771 | 0.714 |

Motorcycle detection improved the most. It is the hardest class in this dataset:
the smallest objects, 21% of all instances, and the worst affected by low evening
light. Precision of 0.929 with recall of 0.854 means the model finds motorcycles
reliably without generating many false positives.

Heavy vehicle detection improved but is still the weakest class, at recall 0.714,
so about 29% of heavy vehicles are missed. Two cautions apply. The test split has
only **70 heavy instances across 69 images**, so the metric carries wide
confidence intervals in either direction. And mAP is not the measurement that
decides whether the class is usable; §4.5 addresses that directly.

The low mAP@50-95 for motorcycles (0.438) is expected and does not matter for this
application. mAP@50-95 averages performance over increasingly strict
intersection-over-union thresholds, so it penalises loosely drawn boxes. On
objects a few dozen pixels across, a box two pixels off fails a strict IoU test
while still marking the right vehicle in the right place. §4.2 explains why this
does not affect counting.

### Comparison with the first model

| | v1 | v2 |
|---|---|---|
| mAP@50 | 0.764 | **0.883** |
| Images | 2,826 | 6,585 |
| Classes | 5 | 3 |
| Sessions | 8 | 20 |

> Read this comparison with care, because the two figures were measured on
> different data. The v2 evaluation set contains Lane C and evening footage that
> did not exist when v1 was trained, so v2 scored higher on a harder test. The
> improvement is real and substantial, but the difference of 0.119 should not be
> treated as a precisely calibrated quantity.

### Inference speed

Measured on the T4 during evaluation: **10.4 ms inference per image at 1280 px**,
plus 0.5 ms preprocessing and 3.4 ms postprocessing, about **70 frames per second
end to end.**

This supports the deployment argument in §1.4. A system running from a fixed
pole-mounted camera must process video at least as fast as it arrives, and on GPU
hardware of this class it easily does. It also bounds the hardware: real-time
operation needs nothing more capable than a T4.

### Training behaviour and the choice of epoch count

All three training losses fall smoothly across the 30 epochs. The validation
losses do not behave the same way:

- `val/cls_loss` falls throughout, from 0.640 to 0.573
- `val/box_loss` reaches a minimum near epoch 14, then **rises** from 1.44 to 1.485
- `val/dfl_loss` follows the same pattern
- `mAP@50` levels off from about epoch 16 at 0.88-0.90

This is the start of localisation overfitting. The model keeps improving at
deciding *what* an object is while getting slightly worse at placing a box around
it on unseen data: classification generalises, and precise box regression starts
to memorise.

The practical effect is small, for the reason in §4.2. The counter tests whether a
tracked centre crossed a polygon, not whether a box meets a strict IoU, so box
tightness is not what this application depends on.

The checkpoint used throughout this report is `best.pt`, saved at the epoch of
peak validation mAP (about epoch 18), not the final epoch. Thirty epochs were more
than this dataset needed, and 20 would have reached the same result in two-thirds
of the time. The divergence between validation losses and validation mAP is the
signal a practitioner should watch for, and it was present here.

## 3.7 Error analysis

The confusion matrix says more than the aggregate metrics, because it shows that
the **kind** of error changed between v1 and v2, not just the amount.

Validation set, 14,493 instances:

| | Predicted car | Predicted heavy | Predicted moto | Missed |
|---|---|---|---|---|
| **True car** (11,236) | **10,695** | 7 | 6 | 528 |
| **True heavy** (152) | 8 | **128** | 9 | 7 |
| **True motorcycle** (3,105) | 8 | 0 | **2,767** | 330 |
| **False positives** | 905 | 25 | 525 | — |

### The truck/car confusion is gone

**Cross-class misclassification accounts for 38 instances out of 14,493, or
0.26%.**

This was v1's main failure and the reason the five-class taxonomy was dropped. A
heavy vehicle is now classified correctly 84.2% of the time, as a car in 5.3% of
cases and as a motorcycle in 5.9%, and missed entirely in 4.6%. The class merge in
§3.2 did what it was meant to do.

### Detection ratios by class

| Class | True | Predicted | Difference | Implied factor |
|---|---|---|---|---|
| car | 11,236 | 11,616 | +3.4% | 0.967× |
| **heavy** | **152** | **160** | **+5.3%** | **0.950×** |
| motorcycle | 3,105 | 3,307 | +6.5% | 0.939× |
| **All** | 14,493 | 15,083 | +4.1% | **0.961×** |

The heavy class has flipped. Under v1 it was under-detected by about 48% and
needed a correction factor of 1.84× that varied by 57% between sessions, which
made it unusable. Under v2 it is over-detected by 5.3%, and all three class
factors fall within a 2.9% band of one another.

> This table is an indicator, not the conclusion. The ratios are per-instance
> detection rates on individually annotated frames. The correction factors in
> §4.4 are session-level *counting* factors, which also depend on tracking across
> frames, ROI gating and the displacement filter. Whether heavy vehicles can be
> reported separately is settled in §4.5, not here. What this table does show is
> that the underlying obstacle, systematic misclassification of heavy vehicles as
> cars, has gone.

### The remaining errors are of a type the counter absorbs

| Error type | Instances | Share |
|---|---|---|
| Cross-class misclassification | 38 | **0.26%** |
| Missed detections | 865 | 5.97% |
| False positives on background | 1,455 | 10.04% |

This difference matters more than the totals. A misclassification is permanent: a
heavy vehicle counted as a car stays wrong in the output, and nothing downstream
can recover it. A missed detection or a false positive is *transient*, and the
counting pipeline in §4.1 is built to absorb both:

- A vehicle missed in one frame keeps its ByteTrack identity and is counted when
  it is detected again in the next.
- A false positive that does not form a stable track across frames never reaches
  the count.
- Anything that does not travel 60 px is discarded regardless.

v2 therefore trades classification errors, which the counter cannot fix, for
detection errors, which it mostly can. **That is why counting accuracy should
improve by more than the 0.119 gain in mAP suggests.**

---

# 4 · Vehicle Counting and Validation

Detection alone is not a traffic measurement. Turning per-frame detections into
vehicle counts needs tracking, spatial gating, and decisions about what counts as a
vehicle. This chapter describes those decisions and validates the result against
manual ground truth.

## 4.1 Counter design

The counter (`detect_vehicles.py`) runs the detector on each frame, links
detections across frames into tracked objects, and counts a tracked object once
when it crosses a defined region of interest.

Five design decisions determine its accuracy, and each was adopted after a specific
failure was observed.

### Persistent-identity tracking

The first version deduplicated detections by distance: a detection within 30 pixels
of one already counted was suppressed. **This over-counted by about a factor of
thirty**, because a vehicle moving across the frame kept leaving the 30-pixel radius
and was counted again each time.

The counter now uses **ByteTrack**. A vehicle gets an identity on first detection and
keeps it across frames, and the counter records the identity in a set and counts it
once, which removes that failure entirely.

### Video-time counting windows

Counting windows were first advanced by wall-clock time. At the roughly 4 frames per
second the detector manages on the hardware used, a nominal 7-second window covered
about 0.43 seconds of video. Windows now advance by **video timestamp**, so a
7-second window covers 7 seconds of recorded traffic whatever the processing speed.

### Class names read from the model

The counter originally held a hardcoded three-class name map written for an earlier
model. When the model was retrained with five classes, the map silently mislabelled
classes and dropped one entirely. Class names are now read from `model.names` at load
time, so the counter cannot disagree with the weights it is using.

### Displacement filtering for staged vehicles

Taxis staged across three lanes on approach A (§2.5) are stationary, and counting
them as traffic would inflate approach A.

A tracked object is counted only once it has moved **60 pixels** from where it was
first seen. Staged taxis never move and are never counted. A motorcycle filtering
past is counted, and a car that has driven into a queue has already moved far enough
to qualify.

The policy was learned in the interview, its effect on measurement was anticipated,
and the filter was written before the counts were taken, so this data-quality problem
was caught through fieldwork before it could affect the results.

### Confidence threshold

The detection confidence threshold is **0.20**, taken from the peak of the model's F1
curve at 0.205 rather than adopted as a default.

### What the failures had in common

Each decision above replaced something that had failed, and **none of those failures
showed up in the model's own metrics.** Every one produced plausible-looking output,
and all five were caught only by comparing against hand counts. One was a class-name
table that silently sent every heavy vehicle into the car count while the model scored
mAP@50 of 0.766 on that class; Appendix H describes all five.

That is the main reason for the manual counting programme in §4.2. Besides giving
the report an accuracy figure, it is the only check in this project that can detect
this whole category of failure, which automated evaluation cannot see. The
counter now prints its complete class mapping at startup and warns on any unmapped
name, so that particular failure cannot recur silently.

## 4.2 Validation against manual ground truth

The counter was run over the same footage and the same 15-minute window as a manual
count, and the two were compared directly. The region of interest spans the
carriageway, so vehicles making both the through and turning movements cross it, and
the automated total is compared with the manual total for both movements together.

| Class | Manual | Detector | Difference | Accuracy |
|---|---|---|---|---|
| **Total vehicles** | 298 | 303 | +5 | **101.7%** |
| cars + heavy | 226 | 232 | +6 | 102.7% |
| motorcycle | 72 | 71 | −1 | 98.6% |
| car | 189 | 225 | +36 | 119.0% |
| heavy | 37 | 7 | −30 | **18.9%** |
| **PCU-weighted flow** | 286.8 | 262.4 | −24.4 | 91.5% |

**Vehicle counting accuracy is within 1.7% of manual ground truth.**

The `heavy` and PCU rows look alarming and are explained in §4.5: the manual tally and
the model are not counting the same class. Manual `heavy` means pickup and larger,
while the v2 taxonomy maps `truck` to `car`. The PCU row is low for the same reason
and in the expected direction, because manual PCU weights those pickups at 2.0 when
they belong at 1.0, so the manual figure is the overstated one. Neither row shows a
detection failure, and §3.7 shows that heavy is detected at the same rate as every
other class.

### Why counting accuracy exceeds what mAP would predict

The model's mAP@50 of 0.764 might suggest a counter accurate to about
three-quarters. It is accurate to 1.7%, because the two metrics measure different
things.

mAP penalises a bounding box with 0.45 intersection-over-union against the ground
truth instead of 0.50, even when both boxes mark the same vehicle in the same place.
The counter applies no such test. It asks only whether the centre of a tracked object
crossed a polygon, so **a model that draws slightly loose boxes still counts
correctly.**

Counting accuracy is the metric this project depends on and the one a traffic engineer
would ask for. mAP is reported because it is the standard computer-vision metric and
allows comparison with published work, but it is not the figure of merit here.

## 4.3 The one systematic error

Thirty heavy vehicles are missing from the detector count and thirty-six extra cars
appear. The sizes match, which points to heavy vehicles being filed as cars rather
than missed.

The two errors almost cancel, which is why the total is within 1.7% while the
individual class counts are not. §4.5 shows across all five sessions that this is a
**definitional** mismatch, not the resolution limit described in §3.2: the vehicles
are detected, and filed under the class the v2 taxonomy gives them.

## 4.4 Calibration across sessions

To process the remaining footage automatically, per-class correction factors were
computed as the ratio of manual to automated counts. Under the final model they cover
**five sessions across three approaches and three times of day**, one session, one
approach and one lighting condition more than the first model was calibrated against.

| Group | `A_morn` | `A_mid` | `B_morn` | `B_mid` | `C_eve` | Mean | Spread | Usable |
|---|---|---|---|---|---|---|---|---|
| **cars + heavy** | 0.974× | 0.969× | 0.890× | 0.887× | 0.956× | **0.935×** | **9.4%** | **yes** |
| total | 0.983× | 0.948× | 0.894× | 0.853× | 0.925× | 0.921× | 14.1% | marginal |
| motorcycle | 1.014× | 0.905× | 0.908× | 0.792× | 0.867× | 0.897× | 24.7% | marginal |
| car | 0.840× | 0.742× | 0.729× | 0.621× | 0.891× | 0.765× | 35.4% | no |
| **heavy** | 5.29× | — | 5.12× | 13.50× | 2.57× | **6.62×** | **165%** | **no** |

Spread is (max − min) / mean. The `A_midday` heavy factor is undefined rather than
large: the detector returned zero heavy vehicles against a manual count of 29, so the
mean and spread for that row are taken over the four defined sessions.

The group the analysis depends on improved. `cars + heavy` moved from 0.917× at
12% spread under the first model to **0.935× at 9.4%** under the final one, while the
calibration set grew from four sessions on two approaches to five on three. A factor
that tightens as the conditions it is measured under widen behaves like a property of
the detector rather than of the sample.

Only `cars + heavy` is stable enough to extrapolate, and it is the figure the
simulation needs. The simulation contains no motorcycles, because motorcycles filter
to the front of queues and take up almost no queue space, so feeding total counts into
a car-only model would overstate queue length.

Heavy vehicles are still not reported separately from detector output. On this
evidence alone the heavy row looks worse than under the first model: its spread rose
from 57% to 165%, and one session cannot be expressed as a factor at all. §4.5 shows
the reason: the detector and the manual tally are not counting the same set of
vehicles, which is a different problem with a different consequence.

The over-count is not caused by the tracker re-acquiring vehicles that stand in
queues, because the session with no queues shows the worst over-count. It falls as
traffic volume rises (r = −0.69), strictly within each approach, but the B approach
sits about ten points above A at comparable volume, so volume alone does not order the
five sessions. Appendix D.1 gives the table and the rejected hypothesis, and §4.7
identifies a mechanism.

The practical consequence is that **the detector is most accurate when traffic is
heavy**, from +1.7% at 298 vehicles to +17.2% at 128. That suits the capacity
analysis, which depends on peak conditions, but light-traffic counts carry noticeably
more error, and how much depends on the approach as well as the volume.

## 4.5 The heavy class: a definitional mismatch, not a detection failure

§3.6 and §4.4 both leave the same question here: now that the final model is
evaluated, can heavy vehicles be reported separately at their PCU weight of 2.0?
**They cannot, but not for the reason the first model's results suggested.**

Under the final model the session-level heavy factor got worse (§4.4) while
per-instance detection of the same class got better (§3.7). Those two results cannot
describe the same failure. Three lines of evidence, set out in Appendix D.2, show what
is happening:

- On annotated frames, heavy is detected as well as the other classes, at an implied
  factor of 0.950×, and cross-class confusion is 0.26%.
- Summed across the five calibration sessions, the detector is short **128 heavy
  vehicles** and carries **181 extra cars**, 141% of the deficit. The vehicles are
  detected; they are grouped differently.
- The detector's heavy share (2.0%) agrees with its own training annotations (1.1%)
  and disagrees with the manual tally (13.6%) by an order of magnitude.

### The cause is in the taxonomy

§3.2 merged the classes and mapped **`truck` → `car`**, because from 6 m overhead a
pickup's open bed covers a few pixels. The manual tallies were transcribed under the
**older five-class definition, in which a pickup counted as heavy.** The two sources
use one word for two different classes:

| | `heavy` means |
|---|---|
| Manual tally | pickup and larger |
| Model, v2 taxonomy | bus, lorry and larger |

Pickups are common in this traffic, which is why the gap is a factor of five to
thirteen rather than a few percent. **This is a bookkeeping mismatch between two
definitions, not a measurement failure.**

### What this changes, and what it does not

Vehicle-count results are unaffected. The capacity analysis in Chapter 5, including
the arrival rates, discharge figures, v/c table and the central Bx = 1.51 finding, is
built from **vehicle counts, not PCU**, and a pickup counts as one vehicle under either
definition.

`cars + heavy` is unaffected, which is why it is the stable group. A pickup filed
as `car` and a pickup filed as `heavy` both land inside `cars + heavy`, so its spread is
9.4% while the heavy row's is 165%.

PCU-weighted flow computed from the manual tallies is overstated, because it
weights pickups at 2.0 when they belong at 1.0. The overstatement runs from 5% to 25%
by session and **13.7% overall**, 1,060.0 PCU as tallied against 932.0 corrected
(Appendix D.2).

Any table in this report with a `heavy` column taken from the manual tallies must
state that manual `heavy` means *pickup and larger*. Without that note the column
invites a PCU calculation that is wrong by the margins above.

### Evening validation

All four of the first model's calibration sessions were recorded in daylight. The
`C_evening` session extends the set to dusk, and under the final model its
`cars + heavy` factor is **0.956× against a daytime mean of 0.930×, a difference of
2.8%**, down from the 6% gap the first model showed. Automated processing of the
evening footage is validated.

### What would settle the heavy question

Re-tallying one session by hand under the **v2 three-class definition**, with pickups
recorded as `car`, would turn the argument above into a direct measurement. If the
heavy factor then lands near the 0.950× that §3.7 predicts, heavy can be reported
separately at PCU 2.0 and this limitation withdrawn. **This is recommended before any
PCU-weighted flow figure is published.** It is one session of manual work, not a new
collection.

## 4.6 Platform drift

The drone holds position by GPS and visual odometry, not by a mechanical mount. The
ROI is drawn on one frame and applied to every later frame, so any drift of the
platform moves the scene underneath a stationary polygon.

The project's original drift estimator reported **56-223 px**, up to 27% of ROI
width. Re-measured with scenery landmarks, each matched independently against the
first frame, **real drift over 900 s is 9-92 px**; the original estimator overstated
it by two to five times on every session. It accumulated error across about 27,000
frame-to-frame estimates, and it fitted a similarity where the actual motion is a
homography, which explains 89-99% of it. `A_morning` cannot be measured this way at
all, because the repeating stripes of its central barrier defeat template matching.

That wrong motion model is why drift compensation (`--stabilise`) was rejected. Across
the five sessions it moved the mean correction factor to 1.002× but raised the spread
from 9.4% to **33.0%**, and the change in each session did not follow its drift: the
steadiest session, `C_evening`, lost a fifth of its vehicles. A correction factor that
cannot be predicted for the next session is not a usable correction.

The effect of drift on counts was then measured directly. `A_morning` was counted
twice, identically except that the ROI was **displaced 50 px across the lanes**, the
direction that does most damage and about the size of the drift actually measured.

| | `cars + heavy` | Factor |
|---|---|---|
| ROI as drawn | 232 | 0.974× |
| ROI displaced 50 px | **232** | 0.974× |

The count did not change over 900 s. Of the 128 seven-second counting windows, the
interval the controller reads, 102 were identical, 26 differed by exactly one vehicle,
and none by two or more. This is one session, and the least demanding one available:
its ROI is 859 px wide, so 50 px is 6% of the width. It shows that drift of the
measured size does not change counts on a wide approach ROI, not that counts are immune
to drift in general.

Drift would matter in the far field, where the ROIs narrow towards the vanishing point
and the platform can move further than the road is wide. §4.7 shows that region is
unreliable for counting on separate grounds. **Drift compensation remains available
behind `--stabilise` but is not used for any count in this report**, and reported
figures come from the fixed ROI throughout. Appendix I gives the full measurement.

## 4.7 ROI depth: a diagnosed over-count that is not corrected

Every correction factor in §4.4 is **below 1.0**: the detector counts more vehicles
than the hand tally, never fewer. §4.4 links this to volume without a mechanism.

The mechanism is distance. On `B_morning`, **62% of all detections come from the far
third of the polygon, where the median vehicle is 58 px tall against 192 px in the
near section.** ByteTrack has least to associate on there, so a track that breaks is
re-acquired under a new identity and each fragment is counted again. This fits the sign
of the error, which the rejected re-acquisition hypothesis did not, because it needs no
queue, only distance.

To test it, all five sessions were recounted with the ROI clipped where it narrows to
half its widest, keeping 61-66% of its depth. On the four free-flowing sessions the
over-count almost disappeared (mean 0.996× at 4.1% spread, with `B_midday` detecting
exactly 86 vehicles against 86 counted). `C_evening` went the other way, to 1.081×, an
8% under-count, which pushed the five-session spread to 10.5%, slightly worse than the
unclipped 9.4%.

`C_evening` has a **standing queue** at the university gate, with about half its
vehicles stopped *above* the cut. The full polygon counts vehicles **arriving** at the
back of the queue, while the clipped one counts vehicles **discharging** past a line
downstream of it. On a free-flowing approach those are the same number; on a queued one
they differ by the change in queue length over the window.

The clipping is not adopted, and no figure elsewhere in this report changes. A
correction that fails on one session in five, for a reason that depends on where queues
form, is not a correction, the same standard §4.5 applies to the heavy class and §4.6 to
stabilisation. What is established is the mechanism: the over-count now has a measured
cause of measured size, in a nameable part of the frame. The rule that would work is to
clip the far field where vehicles cannot be resolved but never through a queue, because
the counting line must sit upstream of where vehicles stop. That needs per-session
judgement about where queues form and is not applied here. Appendix D.3 gives the
per-session figures and the two explanations that were tested and rejected first.

---

# 5 · Results: Demand, Capacity and the Central Finding

This chapter reports what was measured at the ABAC T-intersection: how much
traffic arrives on each movement, how much green time each movement receives,
and what follows when the two are compared. Every figure was obtained on site or
from footage recorded there. Where a value had been assumed before it was
measured, both are shown, because in several cases the difference is itself a
result.

## 5.1 Method

Vehicle counts come from two independent routes, which are used to check each
other.

Manual counts were taken from drone footage with a purpose-built tallying
application (`tally.html`). Counts were taken in **15-minute bins**, the Highway
Capacity Manual's standard aggregation interval, split by movement and by lane,
with motorcycles tallied separately from cars and heavy vehicles. A vehicle is
counted when it **crosses the stop line**. That definition matters at this site:
university policy requires taxis to queue across three lanes on the A approach
while waiting to be called in, and those vehicles never cross the stop line, so
the rule excludes them without any special case.

Automated counts use the YOLOv8 detector from Chapter 3, combined with ByteTrack
for persistent vehicle identity as described in Chapter 4. The automated counter
was validated against the manual counts before it was used on the remaining
footage.

Where capacity is assessed, flow is reported in **passenger car units**, using the
conventional weights of 1.0 for cars, 0.33 for motorcycles and 2.0 for heavy
vehicles. Raw vehicle counts are shown alongside because the two tell different
stories here: movements B and A carry almost identical raw volumes (67 and 66
vehicles in comparable bins), but B imposes a much heavier load because 31% of its
traffic is heavy goods vehicles against A's 20%.

### 5.1.1 Locally measured capacity parameters

The saturation headway and startup lost time in §2.4 were measured from
`B_MORNING_DJI05` with a purpose-built lap-timing application (`headway.html`),
over five green phases that began with standing queues, giving 39
saturation-headway samples: 2.31 s/veh against the HCM default of 2.00, and 2.95 s
of startup lost time against 2.00.

The two differences pull in opposite directions. A saturation headway of 2.31 s
rather than the 2.50 s originally assumed makes discharge about 8% faster, which
raises capacity. But 2.95 s of lost time comes out of every green phase, and on a
20-second phase that is **15% of the available time**, which lowers capacity. **On
short phases the second effect dominates**, so the net picture is worse than the
original assumptions gave. §5.4 quantifies this.

## 5.2 Demand concentrates on the movement the signal does not control

The most consistent pattern in the data is the ratio between through traffic and
traffic turning into the university.

| Session | Day | Through | Turn into ABAC | Ratio |
|---|---|---|---|---|
| A_morning, 08:40 | Monday | A = 66 | **Ax = 232** | **3.52×** |
| A_midday, 11:37 | Wednesday | A = 38 | **Ax = 143** | **3.76×** |
| B_morning, 08:46 | Tuesday | B = 133 | Bx = 120 | 0.90× |
| B_midday, 11:50 | Wednesday | B = 67 | Bx = 61 | 0.91× |

On the A approach, **turning traffic outnumbers through traffic by about 3.5 to
1.** On the B approach the two are about equal. Both ratios hold to within 7%
while total volume doubles between the morning and midday sessions, and on the B
approach the morning volumes are each almost exactly twice the midday ones.

Four bins, two approaches and two times of day give two stable ratios, which makes
them properties of the intersection rather than of the sampling.

### The geometry explains the asymmetry

Both approaches lead to the same destination, so the difference is one of cost,
not demand. `Ax` is a left turn into ABAC that crosses no other movement and has
no signal, so a driver making it never has to stop. `Bx` is a right turn that must
cross A's through traffic and is therefore signalised. With two routes to the same
place and one much cheaper, drivers come from the east and take the free left.

### The dominant movement is the unsignalised one

That turning traffic dominates this junction is unremarkable. The specific
finding is that **the dominant movement has no signal.**

An adaptive signal controller has **no direct authority over `Ax`.** The movement
carrying the largest share of demand cannot be given more green time because it
has no signal to give it to. The only lever is indirect: clear the signalised
movement that physically blocks it, faster.

This limits what any signal-based intervention can achieve, and §5.7 shows that
the same pattern occurs at the other end of the junction.

### It matches the interview evidence

The security guard interviewed during fieldwork described personally waving
left-turning vehicles into the university, because straight-through vehicles stop
and block the turn lane, and estimated that he does so 40-60% of the time. The
measurement explains both why that intervention is needed and why it falls to a
person: the busiest movement at this junction has one lane and no signal, and it
is blocked whenever the through movement queues.

The interview and the measured data point independently to the same bottleneck.

## 5.3 The morning peak is later than the collection guide assumed

Running the calibrated automated counter over all eight session videos (2,299
vehicles in about 2.5 hours of footage) produced a demand profile across the day.
Two flights were recorded on each approach, the second starting shortly after the
first ended.

| Approach | First flight | Second flight | Change |
|---|---|---|---|
| A | 830 veh/hr (08:40-09:05) | 426 veh/hr (09:11-09:36) | **−49%** |
| B | 791 veh/hr (08:46-09:06) | 303 veh/hr (09:13-09:28) | **−62%** |

Both approaches roughly halve within minutes of 09:00. Counts also rise steadily
*within* the first flight (131, then 171, then 175 vehicles per 450 seconds), so
demand climbs towards 09:00 and then collapses.

The morning peak at this site is therefore about 08:15-09:00, not the 07:00-08:30
assumed in the data collection guide. Class start time drives it rather than a
general commuting pattern, which fits a junction whose traffic is largely
university access on a road that dead-ends about 9 km further on.

For this study, it means the first-flight measurements do capture real peak
conditions, which the capacity analysis below depends on.

It also explains something observed independently. The university closes the Lane
C exit for 15-30 minutes between 08:30 and 09:00 each morning, and that window
falls **exactly on the measured arrival peak.** The closure targets the moment
inbound demand surges rather than applying a blanket policy, which fits
everything else observed about how this junction is managed.

## 5.4 The central result: Bx is starved while A is over-served

This section compares supply with demand for the two conflicting movements on the
A and B approaches. Every input is measured: arrival rates from §5.2, signal timing
from Chapter 2, and saturation flow from §2.4.

### Movement Bx, the signalised right turn into the university

| | |
|---|---|
| Queueing arrivals per cycle | **11.1 vehicles** |
| Green time given | **20.0 s** |
| Usable green after 2.95 s startup lost time | 17.1 s |
| Discharge achieved | **7.4 vehicles** |
| Green time **needed** | **28.6 s** |
| Deficit | **8.6 s** |
| **Volume-to-capacity ratio** | **1.51** |

A v/c ratio above 1.0 means demand exceeds capacity, so the queue never settles.
It grows until something outside the signal relieves it, and here it grows by
**3.7 vehicles every cycle.**

The B approach has about **12 vehicles of storage** before the queue reaches the
adjacent four-way intersection some 78 m upstream. At 3.7 vehicles per cycle on a
107-second cycle, that storage is used up in **5.7 minutes**. The queue then blocks
a second junction and delays traffic that never meant to use the T-intersection.

This matches the field observation that the Bx queue "almost" reaches the
four-way: it overruns on a roughly six-minute clock and is partly relieved by
vehicles using the emergency lane and by guards waving traffic through.

### Movement A, the through movement it conflicts with

| | |
|---|---|
| Arrivals per cycle | 8.1 vehicles across 2 lanes |
| Green time **needed** (including lost time) | **12.3 s** |
| Green time given | **40 s** |
| Surplus | **27.7 s** |
| **Volume-to-capacity ratio** | **0.25** |

Movement A holds more than three times the green time it requires, while the
movement it conflicts with is oversaturated and spilling back into a neighbouring
junction.

### The remedy, quantified

Because A and Bx conflict, green time can move between them without changing the
cycle length or the phase structure.

| Green shifted A → Bx | Resulting Bx v/c | Outcome |
|---|---|---|
| 5 s | 1.17 | queue still grows |
| 8 s | 1.03 | queue still grows |
| **10 s** | **0.95** | **queue clears** |
| 12 s | 0.88 | clears comfortably |

Transferring ten seconds of green from A to Bx resolves the oversaturation. A
keeps 30 seconds against a requirement of 12.3 seconds, so it stays comfortably
under-saturated at a v/c of about 0.34.

### The effect of measuring the parameters locally

Before startup lost time was measured, the required transfer was calculated at
**8 seconds**, using the HCM default of 2.00 s instead of the measured 2.95 s.

| Method | Discharge per cycle | Bx v/c |
|---|---|---|
| Original assumptions (2.50 s/veh, no lost time) | 8.0 | 1.39 |
| Measured headway only | 8.7 | 1.28 |
| **Measured headway and measured lost time** | **7.4** | **1.51** |

Measuring the parameters locally made the diagnosis **worse** and moved the remedy
from 8 seconds to 10. A correction that makes one's own result less comfortable is
more credible than one that improves it, and it shows why the local measurement
was worth doing.

## 5.5 The exit movements: Lane C

The two movements leaving the university were measured from footage recorded at
the gate on Monday 24 August, 16:16-16:41, over a 15-minute bin. From inside the
gate the turn direction is visible, so the split between the two exit movements is
**measured rather than apportioned**.

| Movement | Car | Moto | Heavy | Total | Interval (cars + heavy) |
|---|---|---|---|---|---|
| **CB**: exits ABAC turning west, signalised | 98 | 52 | 9 | **159** | **8.4 s** |
| **CA**: exits ABAC turning east, free flow | 58 | 33 | 9 | **100** | **13.4 s** |

The split is 61:39 in favour of CB, and motorcycles make up 33% of both movements.

### CB sits exactly at capacity

| | |
|---|---|
| Queueing arrivals per cycle | 12.6 vehicles |
| Green time given | 31 s |
| Usable green after startup lost time | 28.1 s |
| Discharge achieved | 12.1 vehicles |
| Green time **needed** | **32.1 s** |
| **Volume-to-capacity ratio** | **1.04** |

CB runs at its capacity: it queues on most cycles and clears just in time. Unlike
Bx it does not build an unbounded queue, but it has no margin, and any rise in
demand or disruption to discharge pushes it into oversaturation.

### First check of the detector on evening footage

This session was also the first chance to test the counter's correction factors in
**evening** conditions. All earlier validation had used daytime footage, and at the
time of this comparison the detector had never been trained on evening imagery.

| Group | Manual | Detector | Evening factor | Daytime mean |
|---|---|---|---|---|
| **cars + heavy** | 174 | 182 | **0.956×** | 0.930× |
| total | 259 | 280 | 0.925× | 0.920× |
| motorcycle | 85 | 98 | 0.867× | 0.905× |
| heavy | 18 | 7 | 2.57× | 7.97× |

The `cars + heavy` factor holds: 0.956 against a daytime mean of 0.930, a
difference of **2.8%**. That is what makes automated processing of the remaining
evening footage defensible.

Motorcycles drift only from 0.905 to 0.867, a change of 4.1%, in the same direction
as the daytime spread and within it. Under the first model the drift was from 0.91
to 0.76, which was put down to motorcycles in low evening light being the hardest
objects in the dataset. The final model largely removes that effect, so the case
for reporting `cars + heavy` instead of total counts now rests on §4.4 and §4.5,
not on evening light.

The heavy row is not a light-level result. 2.57× against a daytime 7.97× reflects
the definitional mismatch established in §4.5; the evening session simply contains
a smaller share of pickups.

## 5.6 The shared lane: how blocking occurs

The A approach has two lanes. The inner lane, beside the median, is marked for
straight-through traffic only. The outer lane is shared between straight-through
traffic and the left turn into the university.

A driver turning into ABAC has no choice of lane, while a driver going straight can
use either. That gives the failure mechanism: `Ax` is unsignalised, so turning
drivers never need to stop, but a straight-through car waiting at red in the shared
lane blocks every turning vehicle behind it, holding them at a signal that does not
apply to them.

How often straight-through drivers choose the shared lane was measured from
`A_morning` footage. Only straight-through cars were counted. Turning vehicles were
left out because they have no choice, and motorcycles because they filter around
stopped traffic and so neither block nor get blocked.

| | Count | Share |
|---|---|---|
| Straight-through cars observed | 56 | |
| Inner lane | 53 | 94.6% |
| **Shared lane** | **3** | **5.4%** |

| Source | Shared-lane share |
|---|---|
| Simulation's original working assumption | 50% |
| Inferred from the "blocked 40-60%" field impression | 13-23% |
| **Measured** | **5.4%** |

Drivers avoid the shared lane far more than either estimate supposed: about
nineteen in twenty stay beside the median.

### Late merging

**All three shared-lane vehicles stayed in the inner lane until about five
car-lengths before the turn, then merged late.**

A vehicle that merges late enters at the **head** of the queue, so a single car
stops every turning vehicle behind it for the rest of the red phase. These events
are **rare, but each has close to the maximum possible impact.**

### Reconciling 5.4% with a 40-60% impression

The measured 5.4% seems at first to contradict the reported blocking frequency. It
does not; the question had been framed wrongly.

A Poisson model of arrivals across the whole red phase is the wrong frame, because
**only one straight-through car in the shared lane is needed** to cause blocking,
and such cars merge at the front. The right question is simply whether *any*
straight-through car chooses the shared lane in a given cycle.

At about 5.4 straight-through cars per cycle:

```
P(at least one in the shared lane) = 1 − (1 − 0.054)^5.4  =  26% of cycles
observed directly                  = 3 blockers in 10.3 cycles  =  29%
impression from daily driving      = 40–60%
```

A computed 26% and an observed 29% against a 40-60% impression are the same order of
magnitude, and an impression formed while driving will naturally over-weight
memorable events. The mechanism holds.

### What this means for modelling

This point is easy to get wrong, and it matters for Chapter 6. A model that spreads
straight-through traffic evenly across the shared lane produces **frequent small
delays.** The reality is **infrequent, severe blocking events.**

The two give the same average delay and very different queue dynamics, and it is
the severe events, not the average, that push the Bx queue back towards the
adjacent four-way intersection.

## 5.7 The same failure appears at both ends of the junction

The blocking in §5.6 is not peculiar to the A approach. The same arrangement, an
unsignalised movement sharing a lane with a signalised one, occurs at the exit.
CA was seen blocked seven times during the 15-minute Lane C bin, which at a
107-second cycle is 8.4 cycles.

| | Free-flow movement | Blocked by | Frequency |
|---|---|---|---|
| **Entering** | `Ax`, the left turn in | `A` queueing in the shared lane | **26-29%** of cycles |
| **Exiting** | `CA`, the left turn out | `CB` queueing in the shared lane | **83%** of cycles |

In both cases a movement that **never needs to stop** is held at a signal that does
not apply to it, because it shares a lane with a movement that does.

The exit case is far more severe, and the numbers show why: CB runs at a v/c of 1.04
and queues on nearly every cycle, whereas only 5.4% of A's through traffic enters
the shared lane at all.

Vehicles bound for CA were also seen waiting and then **turning two at a time once
CB receives green**, using the width of the exit to get around the queue, an
informal adaptation by drivers to a constraint the layout imposes.

### What this means for the recommendation

Because it happens wherever an unsignalised movement shares a lane with a
signalised one, **this is the intersection's characteristic failure mode**, not a
quirk of one approach.

It also sharpens the limit identified in §5.2. An adaptive controller cannot help
`Ax` or `CA` directly, because neither has a signal; it can only help them by
clearing the blocking movement faster.

Extra green for CB is therefore not only for CB's own demand. **It is the only
available lever for unblocking CA**, which carries a further 100 vehicles per 15
minutes with no signal of its own.

## 5.8 Every signalised movement, measured

The table summarises the capacity position of all four signalised movements under
the measured signal plan and the locally measured saturation flow.

| Movement | Green given | Green needed | v/c | Status |
|---|---|---|---|---|
| **A** | 40 s | 12.3 s | **0.25** | under-used: over three times the green it needs |
| **B** | 65 s | — | **0.25** | under-used |
| **Bx** | 20 s | 28.6 s | **1.51** | **oversaturated**: queue spills toward the four-way |
| **CB** | 31 s | 32.1 s | **1.04** | **at capacity**: queues on most cycles |

> **Both movements serving the university are at or beyond capacity.
> Both through movements hold more than three times the green time they require.**
>
> Every figure in this table was measured on site.

That statement is the whole argument of this project, and §5.4 shows that a
ten-second reallocation between two conflicting movements is enough to resolve the
worse of the two.

The figures were tested down to movement level on the B approach. `B` and `Bx`
occupy separate physical lanes, so each can have its own detection region, and a
split run over the same 900 s reproduces the hand-counted division of **52:48
exactly**, at a combined correction factor identical to the single-region factor
for that session (§6.9). Wherever movements are physically separable, the detector
is validated at *movement* level and not only at approach level. `A` and `Ax` share
one lane (§5.6) and cannot be separated at any effort, so their split rests on the
manual tally alone.

## 5.9 Visual confirmation: the result in a single frame

Footage `DATA_LANE_B_MORNING/DJI_20260714084627_0005_D.MP4` at **01:24** shows the
whole finding without any analysis:

- Bx queued seven vehicles deep in the median-side lane, all waiting to turn
  into the university
- B completely empty in the adjacent outer through lane, on the same approach,
  under the same green

Two movements share one carriageway and one signal stage; one is saturated and the
other unused, as a v/c of 1.51 against 0.25 predicts.

The same thing showed up while counting: **queues rarely form in both lanes at
once**, because the two movements are loaded so differently.

![**Figure 5.1.** `B_morning` at 01:24. `Bx` queued seven deep in the median-side lane; the outer through lane for `B`, on the same approach and under the same green, is empty.](figures/bx_queue_t84s.jpg)

This frame is the headline figure of the report. It makes the argument before any
number is presented.

### Not a single instant

A single frame invites the objection that it caught an unlucky moment, so the
frames either side were checked.

![**Figure 5.2.** The same approach at 01:18 and 01:30. The imbalance holds across the phase rather than appearing for an instant.](figures/bx_queue_t78s.jpg)

![](figures/bx_queue_t90s.jpg)

Across twelve seconds spanning the phase, the median-side lane stays loaded and the
outer lane stays empty. **The imbalance is the steady state of this approach, not an
accident of when the shutter fell.**

## 5.10 Summary

The measurements in this chapter establish four things.

Demand concentrates on movements the signal does not control. On the A
approach, traffic turning into the university outnumbers through traffic 3.5 to 1,
and that turn has no signal.

The signal allocates green time almost inversely to demand. Both through
movements hold more than three times the green they need, while both movements
serving the university are at or beyond capacity.

The intersection has a characteristic failure mode: an unsignalised movement
blocked by a signalised one sharing its lane. It occurs at both the entry and the
exit, in 26-29% and 82% of cycles respectively.

The main fault can be corrected within the existing signal plan. Moving ten
seconds of green from movement A to movement Bx clears the oversaturated queue
without changing the cycle length, the phase structure or the physical layout.

An adaptive controller should find that last result without being told. Chapter 6
examines whether it does.

---

# 6 · Modules B and C: Adaptive Control and Simulation

Sections 6.1 to 6.5 describe the control algorithm, SODA (Sampled-Observation
Demand-Adaptive control), written by Ratchanon P. and Warachai A. Sections 6.6 to 6.8
describe the simulation environment, the test scenarios and experiment batch, and the
statistical analysis and results, and are Badin's. Section 6.9 describes the interface between the two modules and is shared,
and §6.10 sets out how the simulation is to be validated against the site.

## 6.1 Controller architecture

The controller does not rotate through phases on a timer. Every 2 seconds it reads one
number for each signal head, the count of vehicles in that head's region of interest,
and nothing else: no vehicle identities, arrival times or speeds. Every decision it
makes is derived from those counts.

When no head has demand, every signal is red and the controller waits. Demand starts a
round, and each round passes through three states:

```
ALL_RED_IDLE  →  COLLECT  →  SERVE  →  back to ALL_RED_IDLE when no demand remains
```

## 6.2 The two independent rules

The controller keeps two questions apart: **which heads are served next**, and **for
how long**.

Rule 1: order by how long a queue has existed. Each signal head has a queue clock,
`queueStart`, set at the reading on which the head went from empty to occupied and
cleared whenever it reads empty. The next green goes to the head whose queue formed
earliest, however many vehicles it holds. Ties are broken by the junction's existing
phase order, A, B, Bx, CB.

Rule 2: duration from the length of the queue. Once the heads are chosen, the green
is sized from how many vehicles are waiting, using the capacity relationship below.

Keeping the two rules separate matters. A controller that ordered movements by demand
alone would starve a lightly loaded approach indefinitely, and one that gave every
movement equal time would repeat the fixed-cycle inefficiency the project exists to
address.

### The proposal's formula, tested against measured demand

The project proposal sized each movement's green by its share of total demand:

```
G(i) = G_min + (G_max − G_min) × D(i) / ΣD
```

with `G_min = 8 s` and `G_max = 45 s`, both chosen before any measurement existed.
Tested against the arrivals per cycle established in §5.4 and §5.5 (A 8.1 vehicles
across two lanes, Bx 11.1 across one, CB 12.6 across one), the formula requires the
design to change:

| Movement | Demand share | Formula allocates | Green required | Shortfall |
|---|---|---|---|---|
| A | 0.255 | 17.4 s | 12.3 s | over-served by 5.1 s |
| **Bx** | 0.349 | **20.9 s** | **28.6 s** | **short by 7.7 s** |
| CB | 0.396 | 22.7 s | 32.1 s | short by 9.4 s |

The existing fixed-time plan gives Bx 20.0 s. The proposed formula would give it
20.9 s. It would reproduce almost exactly the failure the project exists to correct,
over-serving the under-loaded through movement and starving the oversaturated turn.
This was the most useful thing the measurement programme revealed about the design.

The formula allocates by **demand share alone** and knows nothing about how many lanes
serve a movement or how quickly a standing queue discharges. A carries 8.1 vehicles
across **two** lanes, 4.05 per lane, while Bx carries 11.1 across **one**. Share-based
allocation cannot tell these apart, so it gives A 17.4 s to clear four vehicles per lane
and Bx 20.9 s to clear eleven. Tuning the bounds does not fix this: lowering `G_min`
makes Bx worse, and `G_max` would have to rise to 70 s, an implausibly long green for a
single-lane turn, before Bx got enough (Appendix F).

### The implemented rule: capacity-based allocation

The problem is the form of the equation, not its parameters. Green time has to be
allocated by **capacity**, not by demand share. `AdaptiveController.cs` therefore uses
the standard capacity relationship with the two values measured at this junction in
§2.4:

> **G_raw = ℓ₁ + D × h**
>
> **G = clamp( G_raw , g_min , g_max )**
>
> where **ℓ₁ = 2.95 s** is the measured startup lost time and **h = 2.31 s** the
> measured saturation headway.

A movement needs enough green to absorb its own startup lost time, plus the time for
its queue to discharge at the measured saturation rate.

| Movement | Rule gives | Requirement from Chapter 5 |
|---|---|---|
| A (2 lanes, 4.05 per lane) | 12.3 s | 12.3 s |
| Bx (1 lane, 11.1) | 28.6 s | 28.6 s |
| CB (1 lane, 12.6) | 32.1 s | 32.1 s |

The rule reproduces all three figures from Chapter 5 exactly, which is expected, since
Chapter 5 derives them from the same measured parameters.

#### Concurrent phases use the critical lane

When a phase serves several heads at once (A, A2 and B run together in stage 1), their
queues discharge **in parallel**, not one after another. The phase ends when the
*longest* queue has cleared, so the green is sized to the **critical lane**:

> **G = clamp( max over lanes in group ( ℓ₁ + Dᵢ × h ) , g_min , g_max )**

Taking the maximum instead of dividing total demand by the number of lanes matters
whenever a queue is unevenly spread, which at this junction is normal: §5.6 shows that
only 5.4% of straight-through traffic uses the shared outer lane on approach A, so the
two lanes of that approach carry very different loads, and an even split would
under-size the phase.

The count `D` is read when the green actually opens, after any amber and all-red, not at
the moment of the decision. A count taken 5 s earlier would already be out of date by
two or three vehicles at peak. The C# implementation is listed in Appendix F.

#### The inverse relation

Read backwards, the same equation gives the number of vehicles a green of a given
length can discharge, which the evaluation uses to compare adaptive allocation with the
fixed plan:

> **N = ( G − ℓ₁ ) / h = ( G − 2.95 ) / 2.31**

Applied to the measured fixed-time plan:

| Fixed phase | Green | Vehicles discharged |
|---|---|---|
| P1 · A + A2 + B | 40 s | 16.0 |
| **P2 · B + Bx** | **20 s** | **7.4** |
| P3 · CB | 31 s | 12.1 |

This is the arithmetic behind the central result. Bx discharges 7.4 vehicles per
cycle against 11.1 arriving, which is the v/c of 1.51 in §5.4, derived here from the
signal plan rather than from the demand side.

### The role of g_min and g_max

`g_min` and `g_max` stay at 8 s and 45 s, but in this formulation they are **safety and
stability bounds, not the allocation mechanism**. `g_min` guarantees a minimum green that
is safe for drivers and pedestrians, and `g_max` stops one saturated movement from
monopolising the cycle when its queue has no end.

The bounds bind only at the extremes. **The formula is linear from D = 3 to 18
vehicles**, which covers the whole demand range measured at this junction. Below three
vehicles the minimum green applies, and above eighteen the lane holds more than one
phase can clear, so the remaining queue carries over to the next cycle (the full table
is in Appendix F). At 8 s, the measured startup lost time of 2.95 s takes **37% of the
minimum green** before any vehicle crosses the stop line. Here that lost time appears as
an explicit term instead of being absorbed silently into the allocation.

This analysis was possible only because demand, saturation headway and startup lost time
were measured rather than assumed. A controller built on the share formula and validated
against invented arrival rates would have *seemed* to work: green time would visibly
respond to demand, the simulation would show it responding, and the defect would have
reached the results unchallenged. **The field measurement exposed the fault, not the
simulation.** The two halves of the project reached the same correction independently,
from the capacity analysis in Chapter 5 and from the controller implementation, which is
the best available evidence that the revision is right.

## 6.3 Phase termination: the calculated green is a ceiling

The calculated G is an upper bound, not the time actually served. Three conditions
can end a green early.

> **G_effective = min( G , t_gap-out , t_starvation , t_max-green )**

| Mechanism | Ends the green when | Parameter |
|---|---|---|
| **Gap-out** | every head in the group reads zero on 2 consecutive readings | fires 4 s after the last non-zero reading |
| **Anti-starvation interrupt** | green has run ≥ 20 s **and** a head outside the group has waited ≥ 0.90 × max wait | 20 s / 40.5 s |
| **Max continuous green** | the same group has held green continuously while traffic waits outside it | 90 s |

Gap-out counts readings, not seconds, so a single missed detection cannot end a phase.
The 20-second floor before an interrupt stops a phase being cut so short that it fails
to discharge a useful queue: 20 s clears (20 − 2.95) / 2.31 = **7.4 vehicles**.

## 6.4 Decision sequence

![**Figure 6.1.** One worked example of the controller. Top: three decisions across four signal heads, with amber and all-red paid only by heads that stop. Middle: the four questions asked at the first decision. Bottom: the queue clock decides which head goes, and the count decides for how long.](figures/fig_soda_explained.png)

The green-time formula is one step of the controller's round, not the whole of it.

Figure 6.1 draws the distinction that is easiest to lose. The count `D` is the only
value that enters the equation, and it decides *how long*. The queue clocks and waiting
times never appear in the formula: they decide *which* heads are served and in what
order. A description of this controller that gives only the equation has described the
timing and left out the scheduling.

**1 · All-red idle.** With no demand on any head, every signal is red and the controller
waits.

**2 · Collect.** When any head becomes occupied, the controller waits up to 7 s for demand
to build before deciding. The window is a ceiling, not a fixed wait: it ends at once when
two or more heads are occupied, or when any head holds three or more vehicles. More
traffic means an earlier decision, and only a lone vehicle at an otherwise empty junction
waits the full 7 s. The ceiling fires at exactly 7.0 s rather than at the next reading.

**3 · Select a leader.**

- Starvation is checked first. Any head that has waited ≥ 0.95 × max wait (42.75 s)
  leads immediately, longest wait first, and the event is logged.
- Otherwise the head whose queue clock started earliest leads (Rule 1), not the one
  holding the most vehicles. Choosing by volume would starve light movements
  indefinitely, such as the shared-lane traffic at this junction.

In both cases an exact tie goes to the head that comes first in the phase order A, B, Bx,
CB.

**4 · Build the concurrent group.** Starting from the leader, other occupied heads are
added in order of their queue clocks, provided each conflicts with nothing already in the
group. Heads that share a physical signal (A and A2) are always grouped together whatever
their counts, because otherwise the controller could not see vehicles arriving into a
lane that is already green.

**5 · Clearance.** Amber 3 s, then all-red 2 s, **applied only to heads that must turn
red.** A head that is green and stays in the new group keeps its green without
interruption.

This is a structural advantage over fixed-time operation. The fixed plan pays the full
5-second intergreen at every stage change without exception. The adaptive controller pays
it only for heads that actually change state, and saves 5 s every time consecutive groups
overlap.

**6 · Serve green**, with G computed by §6.2 from a fresh count once clearance is over,
and the early-termination checks of §6.3 running throughout.

**7 · End the phase.** Every head that was served has its queue clock reset: to the
phase-end time if it still holds vehicles, or cleared if it is empty. The controller then
returns to step 3, or to all-red idle if no head has demand.

Step 7 is what makes a saturated junction take turns instead of serving one head for
ever. Without it, a head that never empties (Bx runs at v/c 1.51) would keep its
original queue clock indefinitely and win every decision. The simulation first applied
the reset only to heads dropping to red, so a head that stayed green across phases kept
its clock; §7.12 describes that fault and its effect.

### Starting from cold

`AdaptiveController.Start()` sets **every signal to red**, which is the all-red idle state
of step 1, and the first head to register a vehicle starts the first round. The starvation
override cannot fire, because nothing has waited yet.

Starting from all-red is deliberate. A controller that starts by guessing a green,
the last one used or a fixed default, gives right of way to an approach it has not yet
observed. The cost falls on the first vehicle of the day, which is seen at the next
2-second reading and, if it is alone, waits out the COLLECT window. That vehicle is by
definition not queued behind anyone.

## 6.5 Conflict matrix and phase compatibility

The controller may serve only movements that do not conflict. The compatibility groups
are:

| Group | Movements served together |
|---|---|
| 1 | A + B |
| 2 | B + Bx |
| 3 | CB alone |

This matrix was designed before fieldwork and has since been **confirmed three times
independently** by watching the existing signal (§2.2): A was seen running with B, Bx
joining B part-way through B's green, and CB running alone. Confirmation from a working
installation is stronger validation than a design assumption.

## 6.6 Simulation environment

The simulation is built in **Unity 6 (6000.5.4f1)** on the Built-In Render Pipeline,
with the behaviour written as C# `MonoBehaviour` components.

**It is a behavioural digital twin, not a visual one.** Vehicles are kinematic agents
carrying car-following, queue discipline, a stop line and a signal state. They are not
Unity `NavMeshAgent`s: pathfinding is not the modelled problem, and a navigation-mesh
agent would introduce avoidance behaviour that a queue at a stop line does not exhibit.
The junction geometry is built to scale from the field measurements in Chapter 2.

**Every object in the scene is generated by one Editor script** (`ABACSceneBuilder`)
from named constants. Nothing is positioned by hand, so a correction from field data is
applied by changing one number and regenerating, rather than by editing a scene file.

Driver behaviour is drawn per vehicle from a three-way mix — **aggressive 25%, normal
60%, cautious 15%** (the last computed as the remainder, so the three always sum to
100). The types differ in following distance and reaction, which produces a spread of
waiting times within a single lane rather than a single deterministic value.

**Seven lanes are modelled**, including `A2`, the straight-through traffic using the
shared outer lane. `A2` is driven by the same signal head as `A` and shares one region
of interest with `Ax`, which is what a single real detector on that lane would see.
`Ax` is constrained without holding a phase of its own through `lightIsInverted`, by
which it reads `Bx`'s signal inverted: `Bx` green or amber means `Ax` red, because both
turn into the university from opposite directions. Together these implement the
shared-lane blocking established in §5.6.

### The two controllers are run against identical traffic

Each lane holds its own seeded `System.Random`, never Unity's global generator, and
every random draw happens at vehicle-arrival time and is never conditioned on traffic
state. Seeds are derived through an FNV-1a stable hash, so they survive across sessions
and machines:

> `seed = StableHash(scenario) + repetition × 31`, with each lane offset by
> `StableHash(laneName)`

The Fixed and Adaptive runs of the same (scenario, repetition) therefore see the same
vehicles arriving at the same instants, and **any difference in the result is
attributable to the signal logic alone.** This is what licenses the paired analysis in
§6.8, and it was verified directly: across the six timing-validation runs of §6.7, every
run reported `TotalSpawned = 859`, identical to the vehicle.

### Motion is integrated below the frame, not per frame

Batch runs are executed at ten times real time so that 216 runs fit in one overnight
session. An early version integrated vehicle motion once per frame with
`Time.deltaTime`, which at 10× and 60 fps makes each step 0.167 s — about 2.3 m for a
14 m/s vehicle, enough to carry a car through a stop line between two consecutive
positions.

Motion is now **sub-stepped**: each frame is divided into steps of at most
`CarAgent.MAX_STEP = 0.02 s`, so at 10× a single frame is resolved in nine steps of
about 0.26 m. Positional resolution therefore does not depend on the playback rate.
§6.7 reports the measurement that confirms this end to end.

### The scene is audited against the source

Unity serialises the public fields of a component into the scene file when the component
is placed, and on load those stored values **override the defaults written in the `.cs`
file**. Editing a constant in source therefore has no effect on a component already in
the scene. This is not hypothetical: the scene silently held a startup lost time of 1 s
and a saturation headway of 2.5 s while the source carried the measured 2.95 and 2.31,
so the simulation was running on superseded parameters.

An Editor command, `ABAC ▸ Verify Scene Against Code`, now builds a fresh component from
the source defaults and compares **every scalar field** against the one in the scene by
reflection, listing any that differ. It runs against the real `.unity` file rather than
against the source twice, which is the only form of the check that can detect this class
of fault. It is run before every batch, and it is the check that confirmed the project
transferred correctly when it was moved to a second machine.

### Test coverage

The control logic is tested outside Unity. Nine C# suites compile `Assets/Scripts`
against a minimal stub of the Unity API and run the logic directly, covering the conflict
matrix, the fixed-cycle timing, the batch arithmetic, the shared lane, the geometry and
measured constants, the scene audit above, the nine SODA clauses of §6.2 to §6.4, and the
queue-clock reset of step 7. A second compile pass includes the Editor scripts under
`UNITY_EDITOR` so that they cannot be excluded from the build by a preprocessor directive
and appear to pass.

Four Python suites run against `soda.py`, the reference implementation of the same
algorithm written independently by Ratchanon P. for Module B. All thirteen suites pass.

**The two implementations are checked against each other, not only against their own
tests.** A machine-readable trace file fixes the expected leader, concurrent group and
green duration at every decision point of five worked scenarios; `soda.py` reproduces all
seven phases exactly, green times agreeing to two decimal places, and the C# is checked
against the same trace. The two agree on all fourteen constants and on all six conflict
pairs. Two implementations in different languages, written by different authors, that
make the same decision from the same input are stronger evidence that the specification
is implemented than either one passing its own tests.

## 6.7 Test scenarios and the experiment batch

Scenarios fall into two kinds, and the distinction matters for how each may be read.

**Scripted scenarios (T1–T7)** release a fixed schedule of vehicles at fixed times. They
are deterministic and repeat exactly, so they demonstrate a specific mechanism rather
than produce statistics:

| | Scenario | Demonstrates |
|---|---|---|
| T1 | single lane, 10 vehicles | baseline discharge |
| T2 | all lanes equal | symmetric allocation |
| T3 | `Bx` against `CB` | the conflicting-movement case |
| T4 | starvation test | the anti-starvation interrupt |
| T5 | ABAC entry and exit | the site's dominant pattern |
| T6 | free movements only | `Ax` and `CA` in isolation |
| T7 | driver-behaviour comparison | sensitivity to the driver mix |

**Poisson scenarios** draw arrivals randomly at a specified mean interval per movement
and are the ones used for statistics. Where a movement has no measured rate, one of five
synthetic demand levels is used: very high 1.5 s, high 2.5 s, medium 4 s, low 7 s, very
low 15 s.

### Which scenarios carry measured demand, and which do not

This must be stated per scenario rather than for the batch as a whole, because the
answer differs.

| Scenario | A | Ax | B | Bx | CB | CA |
|---|---|---|---|---|---|---|
| **T8 · MorningPeak** | 18.8 M | 5.1 M | 9.0 M | 9.6 M | closed | closed |
| **T10 · MiddayNormal** | 29.0 M | 9.7 M | 20.5 M | 21.4 M | 10.1 M | 13.2 M |
| **T9 · EveningPeak** | medium | low | high | low | 8.4 M | 13.4 M |
| **T11 · ABACEvent** | medium | very high | medium | very high | high | high |

`M` marks a measured interval from §6.9; the remainder are synthetic levels.

**MorningPeak and MiddayNormal are measured throughout**, and results from them are
statements about this junction. **EveningPeak carries measured exit rates only**, its four
entry movements being synthetic. **ABACEvent is synthetic throughout**: it is a stress
case representing a campus event, for which no field measurement exists, and it should be
read as a statement about controller behaviour under heavy load rather than about this
site on any particular day.

Two further qualifications apply. **MiddayNormal is a counterfactual**: the real signal
flashes amber between 09:00 and 15:00 (§2.3), so the scenario compares two controllers
over a period in which neither is actually running. And MorningPeak closes `CB` and `CA`,
matching the daily exit closure described in §2.5 and §5.3.

**T12 Night was removed from the batch.** All of its rates were synthetic levels, and
night operation is outside the project's scope (§1.4). It remains available as a manual
test case.

### The batch

| | |
|---|---|
| Scenarios | 4 |
| Repetitions per scenario | 27 |
| Controllers | Fixed, Adaptive |
| **Runs** | **4 × 27 × 2 = 216** |
| Per run | 120 s warm-up, discarded, then 1,800 s measured |
| Playback rate | 10× real time |
| Wall clock | 11 h 31 m |

Twenty-seven repetitions give 27 pairs per scenario and 26 degrees of freedom for the
per-scenario tests. The warm-up is discarded so that each run is measured from a junction
already carrying traffic rather than from an empty one; statistics are reset at the end of
it.

**A batch is run to completion on one machine.** Sub-stepping makes positional resolution
independent of the playback rate, but the sub-step sequence still depends on the frame
times a particular machine produces, so runs are statistically equivalent across machines
rather than identical.

### Validating the playback rate

Running at 10× is only legitimate if it does not change the result. One scenario
(MorningPeak, repetition 1, the same seed throughout) was run at 1×, 5× and 10×:

| Playback | Fixed mean wait | Adaptive mean wait | Wall clock |
|---|---:|---:|---:|
| 1× | 22.23 s | 12.90 s | 64 min |
| 5× | 22.28 s | 12.73 s | 13 min |
| 10× | 22.33 s | 13.31 s | 6 min |

All six runs reported `TotalSpawned = 859`, so the traffic was identical and only frame
timing differed. The fixed-time controller, whose decisions do not depend on demand,
reproduces to within **0.45%** across a tenfold change in rate; this is the cleanest
available test of the sub-stepping, and it passes.

The adaptive controller varies by 3.18% between 1× and 10×. That figure is an upper
bound rather than a measured effect of the playback rate: when the batch itself re-ran the
same seed at 10×, it returned 12.90 s, so the controller's variation between two runs at
one rate is the same size as its variation across rates. The two cannot be separated,
which is the stronger result — **playback at 10× introduces no error beyond the
frame-timing variation already present at 1×.** The mechanism is expected: the controller
reads a count every 2 s, and a vehicle a few centimetres either side of a detection
boundary at the sampling instant changes that count by one.

### The batch was run twice

A first 216-run batch was completed and analysed, and its tail analysis exposed a defect
in the simulation rather than in the algorithm: the queue-clock reset of step 7 (§6.4) was
applied only to heads dropping to red, so a head that stayed green across consecutive
phases kept its original clock and won every subsequent decision. §7.12 describes the
fault and its effect.

The defect was corrected, three measurement faults in the logging were fixed alongside it,
and **the whole batch was repeated**. All results reported below are from the second
batch. The first is retained only as the "before" half of the comparison in §7.12 and is
never cited as a result.

## 6.8 Statistical analysis and results

### The comparison is paired, not independent

Because both controllers see identical arrivals within a scenario and repetition (§6.6),
each Fixed run has exactly one Adaptive counterpart that experienced the same traffic. The
quantity of interest is the per-pair difference, not the difference of two independent
group means. This is a substantially stronger design than sampling each controller
separately, and it is the reason the arrival generator is deliberately not conditioned on
traffic state.

### What is recorded

Three files are written per run:

```
detailed_<controller>_<scenario>_<timestamp>.csv     one row per vehicle
  ControllerType, Scenario, Timestamp, Lane, DriverType, WaitTime

phases_<controller>_<scenario>_<timestamp>.csv       one row per signal phase
  ControllerType, Scenario, PhaseIndex, StartTime, Group, GreenGiven,
  GreenUsed, EndReason, LaneEmptyAt, IdleLaneSeconds

summary_all_runs.csv                                 one row per run
  ControllerType, Scenario, RunTimestamp, TotalSpawned, VehiclesCleared,
  AvgWaitTime, MaxWaitTime, StarvationEvents, StartupLostTime,
  SaturationHeadway, SnapshotInterval, CollectCount, CollectDuration,
  SkippedHeads, QueueStartResets, IdleLaneSeconds, TieBreaksApplied,
  TieBreaksExcludingBx
```

`Lane` records the vehicle's **assigned** movement rather than the detection zone it
passed through, so `Ax` and `A2` remain separable in the results even though they share
one region of interest (§6.6). The per-phase file records what the controller did and why,
which is what makes the diagnosis in §7.12 possible from the output rather than by
re-running.

Completeness was checked before analysis: 216 summary rows, 216 per-vehicle files, 108
per-phase files, 108 runs per controller, 54 per scenario, and no duplicated
(controller, scenario, repetition).

### Which test, and why

Waiting-time distributions are right-skewed and bounded below at zero, so normality
cannot be assumed. It was tested rather than assumed: the Shapiro-Wilk test on the paired
differences rejects normality (p = 5.1 × 10⁻¹⁰).

Three quantities are therefore reported together. The **paired t-test** gives the headline
comparison and is robust at n = 108 by the central limit theorem. The **Wilcoxon
signed-rank test** is reported alongside as the distribution-free check, and agrees
throughout. **Cohen's d and a 95% confidence interval** are reported so that effect size
is visible and not only significance — with 108 pairs, a difference can be significant
and negligible at the same time, and the confidence interval is what shows it is not.

### Mean waiting time

| | Fixed | Adaptive | Reduction | p | Cohen's d |
|---|---:|---:|---:|---:|---:|
| **All scenarios** (108 pairs) | **40.76 s** | **31.60 s** | **22.5%** | 7.8 × 10⁻²³ | 1.21 |
| MorningPeak | 22.81 | 12.89 | 43.5% | 7.0 × 10⁻²⁹ | 11.01 |
| MiddayNormal | 22.69 | 14.40 | 36.5% | 1.3 × 10⁻²⁶ | 8.99 |
| ABACEvent | 92.27 | 77.29 | 16.2% | 2.0 × 10⁻⁶ | 1.17 |
| EveningPeak | 25.28 | 21.81 | 13.7% | 4.0 × 10⁻²¹ | 5.48 |

Overall: paired t(107) = 12.59, 95% CI of the difference [7.72, 10.61] s; Wilcoxon
p = 2.6 × 10⁻¹⁸. **The adaptive controller reduces mean waiting time in all four
scenarios, and in every case the effect is large as well as significant.**

The two scenarios built entirely from measured demand are also the two with the largest
improvement, at 43.5% and 36.5%.

### Worst case and throughput

An adaptive controller that improves the mean while lengthening the tail has not solved
the problem this project set out to solve, and Chapter 5 shows the site's failure is a tail
problem. Both quantities move the same way:

| | Fixed | Adaptive | Change | p |
|---|---:|---:|---:|---:|
| Maximum waiting time | 151.27 s | 111.01 s | **−26.6%** | 1.8 × 10⁻²⁶ |
| Vehicles cleared per run | 1,201.5 | 1,244.1 | **+3.5%** | 1.6 × 10⁻⁶ |

Throughput must be read beside delay rather than on its own, because the two can move
together for an uninteresting reason: a controller that clears more vehicles releases the
queues that block the spawn points, so it also admits more. The point of the pair is that
neither was traded for the other.

### By movement: the controller finds the Chapter 5 result

§5.10 ended by asking whether an adaptive controller would discover the ten-second
reallocation of §5.4 without being told. It was not told: it has no access to the capacity
analysis, only to a count per head every 2 s.

| Movement | Fixed | Adaptive | Reduction | Signal |
|---|---:|---:|---:|---|
| **Bx** | 77.63 s | 54.04 s | **30.4%** | signalised |
| **B** | 29.13 s | 20.26 s | **30.4%** | signalised |
| A2 | 17.59 s | 15.26 s | 13.2% | signalised (with A) |
| CB | 69.45 s | 61.18 s | 11.9% | signalised |
| Ax | 19.93 s | 18.77 s | 5.8% | **unsignalised** |
| A | 51.68 s | 49.22 s | 4.7% | signalised |
| CA | 10.16 s | 11.98 s | **−17.9%** | **unsignalised** |

**`Bx` receives the largest improvement of any movement.** That is the movement Chapter 5
identified as the failure of this junction, oversaturated at v/c 1.51 and spilling back
towards the adjacent four-way. The controller reaches it from demand alone.

The two movements that gain least are `Ax` and `CA`, and `CA` is the only movement that
gets worse. Both are **unsignalised**. This is the limit set out in §5.2 and §5.7
appearing in the results: a signal controller has no authority over a movement with no
signal, and can help it only indirectly by clearing the movement that blocks its lane
faster. The 1.8 s by which `CA` worsens is small in absolute terms, but its sign is the
finding, and it is consistent across the batch rather than incidental.

That both free-flow movements show substantial waiting time at all — 19.93 s and 10.16 s
under the fixed plan, where a movement with no signal should show close to zero — is the
shared-lane blocking of §5.6 and §5.7 reproducing itself in the model without being
scripted. This is suggestive rather than confirmatory: §6.10 sets out the validation that
would establish it, and that validation has not yet been performed (§7.13).

### Starvation events

The anti-starvation interrupt fired 7,887 times across the 108 adaptive runs, and never
under the fixed plan, which has no such mechanism.

**This count is not a failure rate.** It records the mechanism of §6.3 *working* — cutting
a green short so that a waiting head is served. A count of zero would mean either that no
head ever approached the limit or that the mechanism was not functioning, and the two could
not be distinguished from the count alone. The quantity that answers the fairness question
is the maximum waiting time, reported above and examined in §7.12.

Two cautions apply to this figure. The counter **increments twice per event**, so the
number of distinct interruptions is approximately 3,940, not 7,887. And the guarantee it
enforces is stated in lane occupancy rather than in vehicle arrival time: 64 of the 108
adaptive runs still record a maximum per-vehicle wait above 60 s against a 45 s setting.
§7.12 sets out why these are different quantities and why the gap cannot be closed with a
sampled detector.

### Idle green time

The per-phase log records, for every phase, how long each lane in the green group had
already been empty when the phase ended. Summed over a run this averages **752.6 s** for
the adaptive controller, and it is the clearest remaining inefficiency in the design:
gap-out ends a phase only when *every* lane in the group reads empty, so a lane that
clears early holds green while it waits for the others.

Decomposed per phase, only **46.0 s per run (6.1%)** could be recovered by ending the phase
as each lane latches empty; **49.9%** falls in phases where at least one lane never empties
at all, which no end-condition rule can shorten. Recovering that part requires dropping an
individual signal head to red part-way through a phase, with its own amber and all-red
clearance. That is a change to the algorithm rather than to its parameters, and it is
recorded as future work in §8.3 rather than implemented here.

### Summary of the comparison

| Question | Answer |
|---|---|
| Better on average? | Yes — 22.5% less mean delay, large effect, all four scenarios |
| Better for the worst-served driver? | Yes — 26.6% lower maximum wait |
| At the cost of throughput? | No — 3.5% more vehicles cleared |
| Does it find the Chapter 5 problem? | Yes — `Bx` improves most of any movement, from demand alone |
| Does it help the unsignalised movements? | Barely, and `CA` slightly worse — as §5.2 and §5.7 predict |

## 6.9 Interface between Modules A and B

Module A supplies Module B with measured arrival rates per movement. The parameters below
are the measured values, which replaced the invented rates used during early development.

Arrival rates are expressed as a **mean interval in seconds**, not as a rate. Feeding a
rate where an interval is expected inverts the demand, and the output still looks
plausible.

```csharp
// MEASURED — manual tally, cars + heavy, 15-minute HCM bin
// order: (A, Ax, B, Bx, CB, CA)
Poisson("T8 · MorningPeak",   "measured", 18.8f, 5.1f,  9.0f,  9.6f,  —,    —  )
Poisson("T9 · EveningPeak",   "measured",   —,     —,     —,     —,  8.4f, 13.4f)
Poisson("T10 · MiddayNormal", "measured", 29.0f, 9.7f, 20.5f, 21.4f,  —,    —  )
```

These are the manual tally figures, not detector output. Every figure reconstructs from
the tally files in one step, and **Chapter 5 is built on the same figures**, so feeding the
simulation a different set would leave Chapters 5 and 6 describing two different junctions
while claiming to describe one. The detector could not supply the A/Ax split in any case,
because A and Ax share one physical lane (§5.6). B and Bx can be separated, and a split run
over the same 900 s reproduces the hand-counted division **52:48 exactly**, at a combined
correction factor of 0.890x, identical to the single-region factor for that session. **The
central result does not depend on the choice**: a detector-derived set agrees with the
manual one on `Bx` to within 0.3% (9.57 against 9.60). Appendix B gives the rejected
detector-derived set and the full reasoning.

`CB = 8.4` and `CA = 13.4` are evening figures. The morning session contains no CB or CA
measurement, because the drone recorded one approach at a time. Midday CB and CA have since
been measured by running the detector with one region per movement on the midday Lane C
footage: **CB 10.1 s, CA 13.2 s**. The morning figures do not exist and cannot be recovered,
so any morning scenario must either omit the exit movements or state that their rates are
assumed.

A and Ax were recorded on a Monday, which the interviewed guard describes as a
low-volume day. No correction is applied, because the "×0.7" factor is hearsay and was
never measured, so 18.8 and 5.1 are below the true peak and are reported as measured. B and
Bx were recorded on a Tuesday and are at full peak.

Motorcycles are excluded from these figures. The simulation models cars only, and
motorcycles filter to the front of queues while taking up almost no queue space, so
including them would overstate queue length (§7.6).

Three further parameters established in Chapter 5 must be represented:

| Parameter | Measured value | §|
|---|---|---|
| Shared-lane usage by through traffic | **5.4%**, merging late at the queue head | 5.6 |
| B approach storage before spillback | **12 vehicles / 78 m** | 2.1 |
| Saturation headway and startup lost time | **2.31 s/veh**, **2.95 s/cycle** | 2.4 |

## 6.10 Validation of the simulation against observed behaviour

This comparison is what makes the two halves of the project one piece of work.

The simulation's realism is currently asserted rather than demonstrated. The validation
procedure is to feed the measured arrival rates and the measured fixed-time signal plan
into the simulator, run the baseline, and compare the resulting queues with those observed
directly in footage.

| Observable | Measured on site | Simulated |
|---|---|---|
| Maximum Bx queue | 12 vehicles → **≈9 cars** (see below) | *pending* |
| CB queue | approximately 6 vehicles → ≈5 cars | *pending* |
| CA blocked | 82% of cycles | *pending* |
| Ax blocked | 26-29% of cycles | *pending* |
| Time for Bx queue to overrun storage | approximately 5.7 minutes | *pending* |

### Queue counts must be converted before they are compared

The simulation contains no motorcycles (§7.6), so a simulated queue is a count of cars,
while the queues observed in footage were counted as vehicles of all types. Comparing one
with the other unconverted would make the model appear to under-predict by about a fifth.

During the B_morning bin, movement Bx comprised 86 cars, 26 motorcycles and 8 heavy
vehicles. Motorcycles are **22%** of the movement, so `cars + heavy` is 78.3% of the total.

| | Vehicles | Cars + heavy |
|---|---|---|
| Observed maximum Bx queue | 12 | **≈ 9.4** |
| Implied spacing over 78 m of storage | 6.50 m per vehicle | **8.30 m per car** |

The converted figure is also the more physically plausible one. 6.5 m per vehicle is short
for a queued car, while 8.3 m per car, with motorcycles filtered into the gaps, is close to
the conventional 7-8 m queue spacing for passenger vehicles. The conversion produces the
figure that matches the geometry.

**The simulation should be compared against about 9 queued cars, not 12.**

If the simulator reproduces the Bx spillback without being told to, that is direct evidence
that the model is faithful, and the improvement then attributed to the adaptive controller
means something. Without this comparison, the improvement is a number produced by a model
that nobody has checked against reality.

---

# 7 · Limitations

Each limitation below states the constraint, its effect on the work, and what was
done about it.

## 7.1 Environmental: no wet-weather data

Data collection fell during Thailand's rainy season. It rained on about three days
in seven, and drones cannot fly in rain, which ruled out close to half of all
available collection days. Wind affected almost every remaining day; consumer
drones become unstable in wind, and that degrades framing between and within
flights.

As a result, collection stretched over weeks rather than days, and **the dataset
contains no wet-weather imagery at all.** A system deployed at this junction would
see rain often. One documented case: the Lane C evening session planned for Friday
21 August 2026 was rained off and moved to the following Monday.

This is a physical constraint, not an oversight. The weather that would produce
rain imagery is the weather that grounds the aircraft, and augmentation cannot
synthesise rain convincingly enough to substitute. The model is therefore scoped to
dry conditions, and its wet-weather performance is stated as unvalidated. A fixed
mast-mounted camera, the intended deployment sensor, could collect this data
without the flight restriction, which makes it a natural next phase rather than
something this project could have done.

## 7.2 The A approach was never measured at true peak

The A_morning session was recorded on a **Monday**, which the interview described
as a low-volume weekday, so it understates peak demand on movements A and Ax.

No correction factor is applied anywhere in this project. The ×0.7 weekday
multiplier exists only as an interviewee's verbal estimate. It was never measured,
and putting a guess beneath a headline figure is worse than acknowledging the gap.

A and Ax are therefore reported as measured on a low-volume weekday. True peak
demand on the A approach is unmeasured and is expected to be higher.

Only the A approach is affected. B_morning was recorded on a Tuesday and is a real
peak-day measurement.

The structural finding survives regardless. The Ax:A ratio of about 3.5:1 holds on
a Monday morning and on a Wednesday midday, across a doubling of total volume, so
only the absolute magnitude at peak is missing.

## 7.3 The heavy class is defined differently by the tally and the model

Per-session correction factors for `heavy` vary by 165% (§4.4), but the detector is
not failing: on annotated frames the class is detected at 0.950×, within a 2.9% band
of car and motorcycle (§3.7). §4.5 gives the cause. The manual tallies count a
pickup as `heavy`, while the v2 taxonomy maps `truck` to `car`, so the two sources
use one word for two different classes. Across the five sessions, 141% of the
"missing" heavy vehicles reappear as cars (Appendix D).

Heavy vehicle counts are therefore still not reported separately from detector
output. `cars + heavy` is reported instead, and the mismatch cannot affect it. The
consequence is confined to PCU: flow computed from the manual tallies is overstated
by 5-25% (13.7% overall), because pickups are weighted at 2.0 instead of 1.0.
**Vehicle-count results, including the v/c table and the central Bx finding, are
unaffected.** Re-tallying one session under the three-class definition would remove
this limitation.

## 7.4 The detector is least accurate in light traffic

Over-counting ranges from +1.7% at 298 vehicles to +17.2% at 128 (§4.4), although
the order is strict only within an approach. Peak measurements, which the capacity
analysis depends on, are the most reliable, while off-peak figures carry noticeably
more error.

## 7.5 Saturation headway was measured on one movement only

The values ℓ₁ = 2.95 s and h = 2.31 s, which underpin both the capacity analysis in
Chapter 5 and the controller in §6.2, were measured from **five green phases on the B
approach during the morning peak**, on a through movement.

They are applied to every movement, including the turns Ax, Bx and CB. Turning
traffic should in theory discharge more slowly than through traffic, because
vehicles slow down for the turn. If so, the true headway on Bx and CB is above
2.31 s, and both their green requirements and their v/c ratios are **understated**
in this report.

The error is therefore conservative. Correcting it would make Bx and CB look worse,
not better, and would strengthen the central finding. Measuring headway separately
on a turning movement is a simple addition to any future collection session.

## 7.6 The simulation contains no motorcycles

Motorcycles are **21% of all annotated instances** and between 22% and 35% of
individual movements (§5.2, §5.5). The simulation does not represent them: it
spawns cars only, driven by the `cars + heavy` arrival rates handed over in §6.9.

This is a deliberate modelling decision, and the reasoning is given here because it
will be questioned.

Motorcycles do not lengthen queues. They filter to the front of stationary queues
and take up almost no storage. A car-following model that spawned them as separate
queueing agents would produce queues much longer than those observed.

They do use up discharge time, and that effect is represented through the measured
saturation headway. h = 2.31 s exceeds the HCM default of 2.00 s because motorcycles
that have filtered to the head of the queue must clear the stop line before the cars
behind them can move. **The parameter was measured on real mixed traffic, so the
delay motorcycles cause is already inside it.** Modelling motorcycles as agents *and*
using a headway measured on mixed traffic would count the same effect twice.

Two real limitations follow.

First, the treatment assumes the motorcycle share is about the same across
movements. It is not (22% of Bx against 35% of Ax at midday), so a movement with an
unusually high motorcycle share has its discharge time slightly understated.

Second, and more important for §6.10, **simulated queue lengths are counts of cars,
whereas the queues observed in footage were counted as vehicles.** Any comparison
must use one definition, so the observed maximum Bx queue of 12 vehicles has to be
restated in `cars + heavy` terms before it is set against a simulated figure.

## 7.7 The adjacent four-way intersection is not modelled

Bx queues into a second junction 78 m upstream during peak operation. That junction
is outside the simulation, so **the delay attributed to Bx is a lower bound**: the
model captures neither the delay imposed on traffic at the four-way nor any feedback
from congestion there.

## 7.8 Network topology changes by time of day

The Lane C exit closes for 15-30 minutes each morning between 08:30 and 09:00, and an
alternative outflow route opens between 15:30 and 17:00. **The number of exit paths
is not constant**, which a fixed-topology model does not represent. Each scenario
must either enable the relevant route explicitly or declare the variation out of
scope.

## 7.9 Manual guard intervention is not quantified

By the operator's own estimate, guards wave left-turning vehicles into the
university about 40-60% of the time, but this was never converted into a rate.
Identifying an intervention reliably from drone footage proved impractical: at the
available altitude and angle, an active wave-through cannot consistently be told
apart from a guard simply standing near the median.

The model therefore **understates** real throughput on `Ax`. In reality some blocked
vehicles are released by a guard or escape through the emergency lane, while a pure
car-following model holds the whole queue. **Reported `Ax` delay should be read as an
upper bound.**

The qualitative finding stands and matters more: **the intersection currently works
partly because people intervene by hand.**

## 7.10 The dominant movement is unsignalised

`Ax` carries the largest share of demand at this junction and has no signal, so **an
adaptive controller has no direct authority over it.** The same is true of `CA` at
the exit.

The junction was measured well enough to show that the main bottleneck sits partly
outside the control system, and that the only lever is indirect: clearing the
signalised movement that blocks it, faster.

## 7.11 Platform drift is measured, uncorrected, and shown not to matter

The drone drifts by **9-92 px** over a 900 s session (§4.6). Compensation was
implemented but rejected on evidence: across all five sessions it triples the spread
of the correction factors, and its effect does not scale with the drift it is meant
to remove, because the apparent motion is a homography and the compensation fits a
similarity (Appendix I). Displacing the ROI 50 px across the lanes on `A_morning`
changes the 900 s count by **zero vehicles**, and no 7 s window by more than one.

Two limits remain. The displacement test covers one session, the one with the widest
ROI, where 50 px is only 6% of its width. And `A_morning`'s own drift cannot be
measured reliably: its repeating striped barrier defeats patch matching, so only
25-29% of landmarks agree. That is a property of the site and would affect any drift
estimator deployed there.

## 7.12 The fairness guarantee is stated in lane time, but delay is measured per vehicle

The controller's anti-starvation rule promotes any signal head that has waited
**42.75 s**, which is 0.95 × the 45 s `maxWaitTime` set from Level of Service D. The
guarantee is expressed in `queueStart`, the moment a head last went from **empty to
occupied**.

That is not the moment any particular vehicle arrived. A head that never empties does
not re-arm the clock from arrivals; it is re-armed only when the head is served. A
vehicle that joins an already-occupied lane inherits a clock that may have started
long before it did, and the guarantee says nothing about how long *it* has been there.

The reported dependent variable is the other quantity: per-vehicle waiting time,
logged from the simulator's ground truth as each vehicle leaves its region. On a lane
that never empties the two can diverge without limit.

This has been measured. In the first 216-run batch the `EveningPeak` scenario
produced a maximum per-vehicle wait of **344 s against a 45 s limit**, and every one
of its 27 adaptive runs exceeded the fixed-time plan's worst case of 103.9 s. Most of
that gap came from a separate fault in the simulation: the reset was applied only to
heads leaving green, so a head that stayed green across phases kept its original
clock indefinitely.

The fault was corrected and the full 216-run batch repeated. In the second batch
`EveningPeak`'s adaptive maximum wait fell from a range of **126.5-344.4 s** to
**79.0-98.0 s**, and its worst adaptive run now beats the fixed-time plan's worst
(103.9 s).

The correction narrowed the gap without closing it. Across all four scenarios,
**64 of the 108 adaptive runs** still recorded a maximum per-vehicle wait above 60 s
against the 45 s setting, because the underlying quantity is still lane occupancy
rather than vehicle arrival.
<!-- Second-batch figures as reported by Badin on 12 Sep 2026 (SODA_BASELINE v1.6).
     Re-check 79.0-98.0, 103.9 and 64/108 against the batch v2
     summary_all_runs.csv when it arrives. -->

Closing the gap properly needs per-vehicle arrival timestamps, which a sampled camera
detector does not provide (§6.4). The controller guarantees that no *lane* is ignored
for long. It does not guarantee that any *vehicle* is served within a bound.

## 7.13 The simulation is not yet validated against observed data

The comparison described in §6.10 has not been performed, so the simulator's realism
is still asserted rather than demonstrated.

---

# 8 · Conclusions and Future Work

## 8.1 What was established

The signal plan at the ABAC entrance allocates green time almost inversely to
demand. Both movements serving the university, Bx entering and CB exiting, are at
or beyond capacity, at v/c ratios of 1.51 and 1.04. Both through movements hold more
than three times the green time they require, at v/c 0.25.

The fault can be corrected within the existing signal plan. Transferring ten
seconds of green from movement A to movement Bx brings Bx to a v/c of 0.95 and clears
the queue, while movement A keeps more than twice the green it needs. The cycle
length, phase structure and physical layout are unchanged.

The intersection has a characteristic failure mode. Wherever an unsignalised
movement shares a lane with a signalised one, the unsignalised movement is held by a
signal that does not apply to it. This affects 26-29% of cycles at the entry and 82%
at the exit.

A camera-based counter is accurate enough to serve as the measurement
instrument. Counting accuracy is within 1.3% of manual ground truth and
PCU-weighted flow within 2.7%, which is enough for the counts to be used directly as
simulation input.

Every capacity parameter in this analysis was measured locally rather than taken
from published defaults, and in one case the local value made the diagnosis
materially worse: including the measured startup lost time of 2.95 s raised the Bx
v/c from 1.28 to 1.51 and moved the required remedy from eight seconds to ten.

## 8.2 Recommendation

The immediate operational recommendation is the ten-second reallocation in §5.4. It
needs no capital expenditure, no change to the junction's physical layout and no new
equipment.

The longer-term recommendation is geometric, and signal timing cannot deliver it.
Shared-lane volume-to-capacity is only 0.56, yet blocking still occurs, which shows
that the problem on `Ax` is road geometry, not capacity. A dedicated left-turn lane
would resolve it directly, where signal timing can only mitigate it.

## 8.3 Future work

- Wet-weather data collection from a fixed mast-mounted camera, which is not
  subject to the flight restrictions that made this impossible here.
- True peak measurement on the A approach, on a Tuesday, Wednesday or Thursday
  between 08:15 and 09:00, the window this project established as the real peak,
  rather than the 07:00-08:30 originally assumed.
- Quantifying guard intervention, which would turn the largest unmodelled term in
  the analysis from a qualitative observation into a rate.
- Modelling the adjacent four-way intersection, which would turn the Bx delay
  figure from a lower bound into an estimate.
- Edge-hardware feasibility. Evaluating a lightweight model variant for both
  accuracy and inference speed on hardware realistic for a pole mount would show
  whether the system can run at the junction rather than on a workstation. A reader
  will ask this, and it is not yet answered.

### Per-head gap-out

A green ends early only when the *whole* group reads empty. When two heads share a
green and one clears first, that head holds green with nothing to serve until the
other finishes, and any head that conflicts only with the emptied one waits for
nothing. Across the 108 adaptive runs of the second batch this idle green time
(`IdleLaneSeconds`, summed across heads and across roughly 120 phases per run)
averaged **752.6 s per run**, against 784.4 s in the first batch. The waste is real,
not a by-product of the corrected fault.

Broken down phase by phase, most of it cannot be recovered cheaply:

| Scenario | Idle green per run | Recoverable by changing the end condition | In phases where a head never empties |
|---|---|---|---|
| MorningPeak | 1,223.8 s | 87.8 s (7.2 %) | 508.7 s (41.6 %) |
| MiddayNormal | 750.1 s | 58.8 s (7.8 %) | 172.7 s (23.0 %) |
| EveningPeak | 694.0 s | 37.2 s (5.4 %) | 477.1 s (68.7 %) |
| ABACEvent | 342.6 s | 0.0 s (0.0 %) | 342.6 s (100.0 %) |
| **All** | **752.6 s** | **46.0 s (6.1 %)** | **375.3 s (49.9 %)** |

The cheap change is to end the phase once every head in the group has read empty on
two consecutive readings, even if not at the same time. It is about fifteen lines,
costs no extra clearance, and recovers **46.0 s per run**, and nothing at all in
`ABACEvent`. Half the idle time sits in phases where at least one head never empties,
and no end condition can shorten those phases. Recovering that time means dropping a
single head to red mid-phase, with its own amber and all-red, a change to the running
group, a fresh conflict check and a revision to the specification. That was not a
change to make untested with less than two weeks to the exhibition, so it is recorded
here rather than implemented. It would work per signal head, not per lane: A and A2
share one physical head, and both the simulation and the Python reference controller
already model them that way.

---

# 9 · References

Department of Highways, Thailand (2019). *Traffic Engineering Design Standard.*
Ministry of Transport, Bangkok.

Du, D. et al. (2019). *VisDrone-DET2019: The Vision Meets Drone Object Detection
in Image Challenge Results.* ICCV Workshops.

Google Thailand / DeepMind (2023). *Using AI to reduce traffic congestion in
cities.* Google Blog.

Hunt, P.B., Robertson, D.I., Bretherton, R.D. and Royle, M.C. (1982). *SCOOT — A
Traffic Responsive Method of Coordinating Signals.* TRRL Laboratory Report 1014.

Jocher, G., Chaurasia, A. and Qiu, J. (2023). *Ultralytics YOLOv8.*
github.com/ultralytics/ultralytics

Lowrie, P.R. (1990). *SCATS: The Sydney Coordinated Adaptive Traffic System.*
Roads and Traffic Authority of New South Wales.

Transportation Research Board (2022). *Highway Capacity Manual, 7th Edition.*
National Academies of Sciences, Engineering, and Medicine.

Webster, F.V. (1958). *Traffic Signal Settings.* Road Research Technical Paper
No. 39, HMSO, London.

Zhang, Y. et al. (2022). *ByteTrack: Multi-Object Tracking by Associating Every
Detection Box.* ECCV.

---

# Appendices

**A.** Full signal timing measurements and cross-checks

**B.** Complete arrival rate tables, all eight sessions

**C.** Dataset configuration, split assignment and augmentation settings

**D.** Detector calibration, all sessions

**E.** Security guard interview transcript

**F.** Measurement tooling: `tally.html`, `headway.html`, `measure.html`,
`detect_vehicles.py`

**G.** Training environment and reproducibility notes

**H.** Development log: failures encountered and their resolution

**I.** Platform drift measurement

## Appendix B · Arrival rates

### B.1 Why the simulation uses the manual figures rather than detector output

A second set of rates was considered: A = 19.7, Ax = 5.6, B = 8.7, derived by applying the
§4.4 correction factor to detector totals and splitting the result by movement. **That set
was rejected, for three reasons.**

It is not an independent measurement. The detector cannot separate `A` from `Ax`: every
ROI file for the A approach has a single polygon covering both movements, so the split has
to come from the manual tally regardless.

`B` and `Bx` are a different case and have since been measured separately. They occupy
distinct physical lanes, so one region per movement is possible, and a split run over the
same 900 s reproduces the hand-counted division **52:48 exactly**, at a combined correction
factor of 0.890x, identical to the single-region factor for that session. The manual split
is therefore corroborated rather than just asserted. `A` and `Ax` cannot be split at any
effort, because they share one physical lane (§5.6). Applying a correction factor whose
spread is 9.4% (§4.4) and then splitting by a manually derived ratio adds two layers of
error to a number the tally already gives directly.

**Every manual figure reconstructs from the tally files in one step:**

| Movement | cars + heavy in 900 s | 900 ÷ count | Used |
|---|---|---|---|
| A | 35 + 13 = 48 | 18.75 s | **18.8** |
| Ax | 154 + 24 = 178 | 5.06 s | **5.1** |
| B | 67 + 33 = 100 | 9.00 s | **9.0** |
| Bx | 86 + 8 = 94 | 9.57 s | **9.6** |

And Chapter 5 is built on the same figures. The v/c table comes from the manual tallies,
so feeding the simulation a different set would leave Chapter 5 and Chapter 6 describing two
different junctions while claiming to describe one.

The central result is insensitive to the choice. The two sets agree on `Bx` to within
0.3% (9.57 against 9.60), and the project's conclusion that `Bx` is oversaturated rests on
`Bx` alone. The choice had to be settled before the batch ran, but it does not move the
finding.

The sets diverge most at midday. `B_midday` is the session with the largest detector
over-count (+17.2%, §4.4), and the detector-derived midday figures of 15.9 s and 17.6 s,
against the tally's 20.5 s and 21.4 s, would have loaded the simulation about 25% heavier
than the traffic actually observed.

## Appendix D · Detector calibration

### D.1 Over-count against traffic volume

One explanation considered for the over-count in §4.4 was tracker re-acquisition. ByteTrack
retires a track after a fixed number of frames without a confident detection, so a vehicle
standing in a queue for a long time could be dropped and re-acquired under a new identity,
and counted twice.

The data rule this out. The `B_midday` session contains no queues at all and shows the
*worst* over-count.

What correlates instead is **traffic volume**:

| Session | Approach | Manual total | Over-count |
|---|---|---|---|
| `B_midday` | B | 128 | +17.2% |
| `A_midday` | A | 181 | +5.5% |
| `B_morning` | B | 253 | +11.9% |
| `C_evening` | C | 259 | +8.1% |
| `A_morning` | A | 298 | +1.7% |

The correlation is negative but not clean (r = −0.69). **Within an approach it is strict**:
on A the over-count falls from 5.5% to 1.7% as volume rises from 181 to 298, and on B from
17.2% to 11.9% as it rises from 128 to 253. But B sits about ten points above A at comparable
volume, so volume alone does not order the five sessions. The present data do not explain the
offset between approaches. The B approach is filmed at a shallower angle, which is a
plausible but untested cause.

This is weaker than the first model's four-session version of the same table, which was
monotonic. The monotonic ordering did not survive adding a third approach, and reporting it as
if it had would claim more than five points can support.

The rejected hypothesis is recorded alongside the accepted one because a plausible mechanism
that the data contradict is worth knowing about, and reporting only the surviving explanation
would hide how the conclusion was reached.

### D.2 The heavy class: evidence for the definitional mismatch

Before the final model was evaluated, the expectation was that quadrupling the heavy sample
would either stabilise the class or trace the problem to sensor resolution. Neither happened.
The session-level factor got *worse* (§4.4) while per-instance detection of the same class got
*better* (§3.7), and those two results cannot both describe the same failure.

Per-instance detection of heavy is as good as the other classes. On individually annotated
frames (§3.7), heavy is over-detected by 5.3%, an implied factor of 0.950×, within a 2.9% band
of car (0.967×) and motorcycle (0.939×). Cross-class misclassification across the whole
validation set is 38 instances in 14,493, or 0.26%. A class detected at the same rate as every
other class is not one the model is failing to find.

The missing heavy vehicles reappear as cars, with some to spare. Summed across all five
calibration sessions, the detector is short **128 heavy vehicles** relative to the manual tally
and carries **181 extra cars**, **141% of the deficit.** The vehicles are being detected but
grouped differently. The surplus beyond 100% is the ordinary false-positive rate quantified in
§4.4, which is why the ratio is above one rather than exactly one.

**The detector agrees with its own training set, not with the tally.**

| Source | Heavy share of all vehicles |
|---|---|
| Manual tally, five sessions | **13.6%** (152 of 1,119) |
| Annotation set, v2 (§3.2) | **1.1%** (656 of 57,260) |
| Detector, five sessions | **2.0%** (24 of 1,207) |

A detector that was failing at a class would disagree with the annotation set about how common
that class is. This one agrees with it to within about one percentage point and disagrees with
the manual tally by an order of magnitude. **The disagreement is between the tally and the
annotation set, and the detector is only reporting it.**

The mismatch was created by a modelling decision that §3.2 defends on its own merits and that
§3.7 shows succeeded.

**PCU overstatement by session:**

| Session | PCU as tallied | PCU corrected | Overstated by |
|---|---|---|---|
| `A_morning` | 286.8 | 256.8 | 11.7% |
| `A_midday` | 171.8 | 142.8 | 20.3% |
| `B_morning` | 254.5 | 221.5 | 14.9% |
| `B_midday` | 126.9 | 101.9 | 24.5% |
| `C_evening` | 220.1 | 209.1 | 5.3% |
| **All five** | **1,060.0** | **932.0** | **13.7%** |

"Corrected" reassigns each session's excess manual heavy count to `car` at PCU 1.0.

### D.3 ROI depth: detections by image row and the clipping test

Single-frame inference over 29 frames per clip, every detection inside the ROI, binned by image
row:

| `B_morning`, image row | n | Median box height | Median confidence |
|---|---|---|---|
| 0-200 (far) | 39 | **41 px** | 0.64 |
| 200-300 | 39 | 58 px | 0.70 |
| 300-400 | 28 | 88 px | 0.72 |
| 400-452 | 10 | 115 px | 0.76 |
| 452-600 | 26 | 143 px | 0.77 |
| 600-800 | 25 | 214 px | 0.77 |
| 800-1080 (near) | 21 | **280 px** | 0.71 |

All five sessions were recounted with the ROI clipped at the height where it narrows to half its
widest, keeping 61-66% of its depth.

| Arm | `A_morn` | `A_mid` | `B_morn` | `B_mid` | `C_eve` | Mean | **Spread** |
|---|---|---|---|---|---|---|---|
| Full depth (reported) | 0.974× | 0.969× | 0.890× | 0.887× | 0.956× | 0.935× | **9.4%** |
| Clipped | 0.991× | 1.016× | 0.975× | 1.000× | **1.081×** | 1.013× | **10.5%** |

On the four free-flowing approaches the result is clear: mean 0.996× at 4.1% spread, with the two
worst sessions moving from 0.890× and 0.887× to 0.975× and 1.000×. `B_midday` lands on exactly 86
detected against 86 counted, and a pair of clipped movement polygons for the same session, drawn
by hand in the calibration tool rather than by the rule above, gives 0.989×: two differently built
clipped ROIs agreeing to within one vehicle.

`C_evening` breaks it, overshooting to 1.081×, an 8% *under*-count, and pushing the spread to
10.5%, slightly worse than the baseline.

Two explanations were tested and rejected before the cause was found. The first was that
`C_evening`'s ROI never reaches the small-vehicle regime, so a relative clip over-cuts it. The
reverse is true: it is the most distant framing of the five, 32 px in the clipped region and 87 px
in the region kept, so its *good* region is as hard as the others' *bad* one. The second was a
window-boundary effect: clipping moves the counting line downstream, so a vehicle inside the manual
0-900 s window can still be in transit when the window closes. Recounting to 960 s recovered three
vehicles, not thirteen, so that hypothesis was rejected too.

The cause is the **standing queue** at the university gate, with about half the vehicles present
stopped *above* the cut (§4.7). Extending the window cannot recover vehicles that are still
stationary, which is why the 960 s test found nothing.

## Appendix F · Controller implementation

### F.1 Green-time calculation

The implementation in `AdaptiveController.cs`:

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

### F.2 Why the proposal's share formula could not be tuned

`G_min` cannot be tuned around the failure in §6.2. **Lowering it makes Bx worse**, because for any
movement whose share is below 50% the floor removed is larger than the extra range gained:

| `G_min` | Bx receives |
|---|---|
| 8 s | 20.9 s |
| 6 s | 19.6 s |
| 4 s | 18.3 s |
| 2 s | 17.0 s |

Raising `G_max` does help, but crudely: `G_max = 70 s` would be needed to bring Bx to 29.6 s, which is
an implausibly long green for a single-lane turn and would distort allocation under every other
demand pattern.

### F.3 Where the bounds bind

| Demand D | G_raw | G after clamp |
|---:|---:|---|
| 0-2 | 2.95-7.57 | **8.00** (floor) |
| 3 | 9.88 | 9.88 |
| 8 | 21.43 | 21.43 |
| 15 | 37.60 | 37.60 |
| 18 | 44.53 | 44.53 |
| 19+ | 46.84+ | **45.00** (ceiling) |

## Appendix H · Development log

### H.1 Counter failures caught by comparison with hand counts

| Failure | Symptom | Cause |
|---|---|---|
| Spatial deduplication | counts roughly 30× too high | proximity is not identity |
| Wall-clock windows | a 7 s window held 0.43 s of traffic | inference runs below real time |
| Hardcoded class table | one class silently dropped | model gained classes, indices shifted |
| **Unmapped class name** | **heavy = 0 while its mAP was 0.766** | see below |
| `--conf` never applied | threshold experiments were inert | constant read instead of argument |

The fourth is the clearest case. During the final calibration run the detector reported
`car 229, motorcycle 71, heavy 0` for a window whose hand count was `car 189, motorcycle 72, heavy 37`:
no heavy vehicles found at all.

The model was not at fault. It had scored mAP@50 of 0.766 on the heavy class in the test evaluation
of §3.6. The fault was in the table mapping model class names to reporting groups, which had been
written for the five-class taxonomy (`car`, `motorcycle`, `truck`, `bus`, `special type`) and was
never updated when the merge described in §3.2 renamed the class to `heavy`. A dictionary lookup
defaulting to `"car"` therefore sent every heavy vehicle into the car count.

The arithmetic showed it at once: **the car count exceeded the manual figure by 40, against a manual
heavy count of 37.**

No training metric could have revealed this. mAP, precision and recall are all computed within the
model's own frame of reference, and this fault was in the pipeline around the model, in naming rather
than detection. The output was well-formed JSON with plausible totals and no errors raised. It was
caught by comparison with hand counts, and only by that, as were the other four.

The fix went beyond the mapping itself. The counter now prints its complete class mapping at startup
and warns on any unmapped name, so the same failure cannot recur silently:

```
[INFO] Classes  : car->car, heavy->heavy, motorcycle->moto
```

## Appendix I · Platform drift measurement

### I.1 The original estimator

Every drift figure before the re-measurement came from one estimator, `DriftTracker`, which has two
faults that push in the same direction.

It accumulates. It estimates motion between consecutive frames and composes the result about
27,000 times per session. Each step is plausible, so nothing is ever rejected (the tracker reports
`0 estimates rejected` on every run), but the composed total random-walks away from the truth.
`max_shift` then reports the maximum of that walk, which is the upper envelope of a random walk and
inflated by construction.

It fits the wrong model. It estimates a translation plus similarity, and the apparent motion in
this footage is neither.

### I.2 Re-measurement

Drift was re-measured with an instrument that has neither fault: scenery landmarks tracked by
normalised cross-correlation of 80 × 80 patches, each matched **independently against frame 0**. There
is no chain to accumulate, and one bad landmark cannot drag the others.

| Session | `DriftTracker` | Re-measured | Landmark agreement |
|---|---|---|---|
| `A_morning` | **223 px** | 44-59 px | **25-29%, not reliable** |
| `B_midday` | 205 px | 52-80 px | 42-53% |
| `B_morning` | 164 px | 47-92 px | 70-87% |
| `A_midday` | 107 px | 45-53 px | 88-89% |
| `C_evening` | 56 px | 9-13 px | 93% |

`DriftTracker` overstates by two to five times on every session. Real platform drift over 900 s
is **9-92 px**.

`A_morning` cannot be measured this way at all, and that is a property of the site rather than the
aircraft. Masking the carriageway out of feature tracking leaves grass, power lines and a central
barrier of regularly repeating stripes and posts, and template matching on a periodic structure locks
onto the wrong period. No drift figure should be quoted for that session.

### I.3 Why compensation failed: the model, not the tuning

Fitting successively richer models to the tracked landmarks shows what kind of motion this is:

| Session | motion | translation | similarity | **homography** |
|---|---|---|---|---|
| `A_midday` @900 s | 45.3 px | 10.6 px | 3.6 px | **0.7 px** |
| `B_morning` @900 s | 46.5 px | 22.1 px | 18.6 px | **1.8 px** |
| `B_midday` @900 s | 91.4 px | 41.2 px | 46.8 px | **10.4 px** |
| `C_evening` @300 s | 13.0 px | 2.9 px | 1.7 px | **1.2 px** |

Figures are the median residual each model leaves. A **homography** explains 89-99% of the motion,
because the camera is changing its view of a road plane. The similarity that `drift.py` assumes leaves
3-47 px, and a warp fitted with the wrong model does not simply under-correct: it moves the polygon
somewhere the road never was. `--stabilise` was warping with a model that cannot represent the motion.

| | Mean factor | **Spread** |
|---|---|---|
| **Fixed ROI (reported)** | 0.935× | **9.4%** |
| Stabilised ROI | 1.002× | **33.0%** |

The stabilised mean sits closer to 1.000, which on its own looks like an improvement. But the spread
more than triples, and a correction factor that cannot be predicted for the next session is not a
correction, the same standard this report applies to the heavy class in §4.5.

The change also does not follow the drift:

| Session | Re-measured drift | Change in `cars + heavy` |
|---|---|---|
| `B_midday` | **80 px** (largest) | **+1.0%** |
| `B_morning` | 47 px | −5.5% |
| `A_midday` | 45 px | −3.9% |
| `A_morning` | 44 px (not reliable) | 0.0% |
| `C_evening` | **9 px** (smallest) | **−20.9%** |

The session that drifts least has the largest change in count, and the session that loses a fifth of
its vehicles is the steadiest of the five. `C_evening` loses 38 of 182 vehicles while the tracker
reports zero rejected estimates.

### I.4 Where drift would matter

A 50 px displacement costs nothing near the camera (§4.6) but would cost a great deal far from it, for
a geometric reason. These ROIs were drawn to the vanishing point, and there the approach is only tens of
pixels wide:

| Session | Drift | ROI width at far edge | Drift as share of width |
|---|---|---|---|
| `B_midday` | 80 px | 55 px | **145%** |
| `B_morning` | 47 px | 33 px | **143%** |
| `A_midday` | 45 px | 90 px | 50% |
| `A_morning` | 44 px | 90 px | 49% |
| `C_evening` | 9 px | 52 px | 17% |

At the far edge of the polygon the platform moves further than the whole road is wide. At the near edge
the same drift is 6-26% of a **single lane**. The drift problem lives almost entirely in the far third of
the ROI, which §4.7 shows is unusable for counting on separate grounds.

### I.5 How the decision was reached

An initial A/B test on `B_morning` alone showed the factor moving from 0.890× to 0.942×, closer to unity,
a 5.5% change against a 3% decision threshold, which read as evidence for adopting compensation.
**Extending the test from one session to five reversed the conclusion.** The single-session result was not
wrong about `B_morning`, but it was not enough to decide the question, and a threshold applied to one
sample gave a confident answer in the wrong direction.

The original 56-223 px figure was never checked against an independent instrument, because it was
plausible and because `0 estimates rejected` looked like a clean result. **It stood because nothing
disagreed with it, not because anything confirmed it.**

