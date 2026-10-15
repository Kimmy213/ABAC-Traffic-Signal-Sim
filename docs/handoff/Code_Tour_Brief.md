# Brief: an HTML code tour of the Unity simulation

**For:** the Claude session that builds it · **Asked by:** Kimmy (Module C, Unity simulation) · **Written:** 15 Oct 2026

## 1. What this is for

The project defence (14-16 Oct 2026) is a purely technical presentation. Kimmy needs a set of HTML pages
that walk a technical audience through the code of the Unity digital twin:

- **one hub page** showing every file, what each one does, and how they work together;
- **one page per file**, which first shows the file as a whole and then zooms into its smaller parts;
- on every file page, **every variable explained**, and **what happens if you change it**;
- laid out cleanly, with **diagrams and animations** wherever a picture explains better than text.

**Use only facts that exist in the code and the docs. Do not invent anything** - no made-up numbers,
behaviours or results. If you cannot find a fact, say so on the page or leave it out.

Time is short. Build in the priority order in §8 so that a partial result is still usable on the day.

## 2. Source of truth

1. **Code: branch `sim/1s-with-reset-fix` (commit `4e78162` or later).** This worktree branch
   (`claude/html-code-documentation-7c0003`) was cut from `main`, which lacks the latest `CarAgent.cs`,
   `TestCaseLibrary.cs`, `docs/unity/Unity_BASELINE.md` and the batch v5 results. **First merge
   `sim/1s-with-reset-fix` into this branch**, then work from the merged tree. Quote line numbers from that tree.
2. **Scene values: `Assets/Scenes/SampleScene.unity`.** Unity stores every public field of every component
   in the scene, and those values override the defaults in the `.cs` files. A variable table that shows
   only the code default is wrong whenever the scene differs. Read the serialized value from the scene and
   show both. `Assets/Editor/SceneParameterAudit.cs` lists the fields allowed to differ on purpose.
3. **Behaviour documents (read before writing about a file):**
   - `docs/soda/SODA_BASELINE.md` - the authority on what `AdaptiveController` does, and every known defect.
   - `docs/unity/Unity_BASELINE.md` - the authority on vehicle behaviour (`CarAgent`, `CarSpawner`).
   - `docs/soda/Adaptive_Timing_Formula.md` - the green-time formula.
   - `docs/guides/SceneSetup_4Phase_Guide.md` - how the scene is built.
   - `docs/report/SP1_Report_DRAFT.md` - background and context.
4. **Results: batch v5 only** (`SimulationResults/batch_v5/` and its `analysis_output/`). Batch v2 (22.5%)
   and v4 are superseded; do not quote them as current. Headline v5 figures, Fixed to Adaptive:
   mean wait per run 42.03 to 33.62 s (-20.0%), max wait 204.2 to 119.3 s (-41.6%), Bx wait -53.9%,
   throughput +3.1% (not significant by t-test), 0 stalled runs. Re-read them from the analysis output
   rather than copying from here.

## 3. Scope

The 19 C# files of the Unity project, nothing else (no `Tests/`, `Analysis/`, `tools/`).
Suggested grouping for the hub (check it against the code and adjust if the code says otherwise):

| Layer | Files | Role, from each file's header comment |
|---|---|---|
| Junction model | `IntersectionConfig`, `TrafficLight` | Lanes, conflict matrix, signal heads. Controllers read lanes only from `IntersectionConfig`. |
| Traffic | `CarSpawner`, `CarAgent`, `TestCaseLibrary`, `CustomTestCase` | Arrivals (Poisson or scripted), driver profiles, car following, stopping, turning, the A2 lane change. |
| Sensing | `ROIZone`, `RoiHighlight` | What the detector can see: a count per snapshot, no IDs. The highlight box shows it on screen. |
| Control | `FixedController`, `AdaptiveController` | The real 106 s fixed-time plan, and SODA. |
| Experiment | `ScenarioManager`, `DataLogger` | 4 scenarios x 27 repetitions x 2 controllers = 216 paired runs; per-vehicle and per-phase logging. |
| Runtime UI | `SimulationControlPanel`, `BatchRunPanel`, `CameraDirector`, `UITheme` | Control panel, batch progress, camera shots, shared styling. |
| Editor tools | `ABACSceneBuilder`, `CarPrefabBuilder`, `SceneParameterAudit` | Build the scene and the car prefab from real coordinates; check the scene against the code. |

Junction facts from `ABACSceneBuilder.cs`: Bangna-Trad Road runs along X; ABAC is to the north (+Z);
7 movements (A, A2, Ax, B, Bx, CB, CA) on 4 signal heads; A2 shares A's signal and Ax's lane;
Ax and CA are free (unsignalised); Bx turns right into ABAC across the A stream; CB crosses A to merge into B.

## 4. Pages

### 4.1 Hub page (`index.html`)

- **Architecture map:** an interactive diagram of all 19 files grouped by layer. Each node links to its
  page. Hovering or tapping a node highlights the files it talks to, with the edge labelled by what passes
  between them (for example `ROIZone -> AdaptiveController: vehicle count per snapshot`). Build the edges
  from real references in the code (method calls, fields of that type, `FindObjectOfType`), not from
  names that only appear in comments.
- **"One second of simulation" animation:** a car is spawned by `CarSpawner`, drives (`CarAgent`), enters
  an ROI (`ROIZone`), the controller takes a snapshot and decides, `TrafficLight` changes, the car stops
  or goes, `DataLogger` records its wait. Show which file is active at each step.
- **Junction diagram:** top-down SVG of the T-junction with the 7 movements, 4 signal heads, the ROIs and
  the conflict pairs from `IntersectionConfig`. Reuse it on the file pages.
- **Lifecycle:** what runs in `Awake`, `Start`, `Update` and `FixedUpdate` across the files, and in what
  order a run starts, runs and ends (single run from the control panel, and a batch from `ScenarioManager`).
- **The scene-overrides-code rule**, as a small diagram: `.cs` default -> value saved in `SampleScene.unity`
  -> value at runtime. The audience must understand this before reading any variable table.

### 4.2 File page (one per file, e.g. `adaptive-controller.html`)

Same template on every page, from the whole file down to the details:

1. **Header:** file name, layer badge, one-sentence role, line count, links to the files it depends on
   and the files that depend on it.
2. **Big picture:** where the file sits (a small copy of the architecture map with this file and its
   neighbours highlighted); its responsibilities in three to five bullets; an outline of its classes and
   methods grouped by purpose, each linking to its deep-dive section.
3. **Deep dive, one section per method group:** what it does, why, the real code excerpt (short, with the
   file and line numbers), and a diagram or animation when the logic is a process, a state machine or a
   timeline. Explain the flow step by step.
4. **Variable reference table:** every public and serialized field, plus constants that matter
   (`MEASURED_*` and similar). Columns: name, type, unit, code default, scene value (flag it when it
   differs), meaning, **what happens if you change it**, and coupled values (for example
   `CarAgent.carLength` must match `CarPrefabBuilder`'s target length).
5. **"What if" panel:** for the most important variables, an interactive control (slider or input) that
   recomputes the formula from the code and shows the result. Only use formulas that are in the code.
6. **Known issues and decisions:** anything the baselines, handoffs or correspondence record about this
   file - for example the `queueStart` reset order, the 2 s to 1 s snapshot change, the discharge
   calibration to 2.31 s/veh, the A2 lane-change fix, the `SetDirty` context-menu issue. Cite the document.
7. **Previous / next file** navigation, and a link back to the hub.

### 4.3 "What happens if you change it" - how to write it

Every entry is one of three kinds, and the page must say which:

- **Measured:** a recorded result exists. Cite it. Examples to look for: `minGreenBeforeInterrupt = 20 s`
  is why Adaptive loses in EveningPeak (anti-starvation cuts about 92-94% of greens at that floor);
  `snapshotInterval` 2 s to 1 s halved the gap-out window; `desiredTimeHeadway = 1.9` brings queue
  discharge from 0.72 to 2.28 s/veh against the measured 2.31.
- **From the code:** you traced it through the code and it follows directly (for example, raising `gMax`
  lets one green run longer before it is cut). Say "follows from the code, not measured".
- **Unknown:** the effect depends on traffic and has not been run. Say so; do not guess a number.

Also note when a change would break something: a test in `Tests/harness/` that would fail, a coupled value
that must change with it, or a value the scene overrides.

## 5. Visuals and animations

Make them where they explain a mechanism, not as decoration. Good candidates, all built from the code:

| Topic | File | Visual |
|---|---|---|
| Fixed phase plan and inter-green | `FixedController` | Animated timeline of one cycle: phases, yellow, all-red; total cycle length computed from the values. |
| SODA decision loop | `AdaptiveController` | State diagram: collect window, early exit, leader choice (Q1-Q4 in the Decision Inspector), concurrent group, green time, gap-out, anti-starvation, clearance. Step through it. |
| Green time formula | `AdaptiveController` | Slider for vehicle count -> G from `CalculateGreenDuration`, clamped by `gMin` / `gMax`. |
| The eight-number state | `AdaptiveController` | 4 heads x {`queueStart`, `lastCount`} updating as snapshots arrive. |
| Car following and queue discharge | `CarAgent` | A queue leaving on green, showing headway and the startup wave. |
| Driver profiles | `CarAgent`, `CarSpawner` | The Aggressive / Normal / Cautious table as a chart (values from `Unity_BASELINE.md` §1). |
| Poisson vs scripted arrivals | `CarSpawner`, `TestCaseLibrary` | Arrival dots on a timeline for both modes. |
| Snapshot sensing | `ROIZone` | ROI box with cars passing; the count sampled once per `snapshotInterval`, no IDs. |
| Batch design | `ScenarioManager` | 4 x 27 x 2 grid, paired by seed. |
| Scene build | `ABACSceneBuilder` | Lane coordinates on the junction plan, and how camera shots are derived from them. |

Animations must have play / pause and step controls, so the presenter can stop on a frame while talking.
Respect `prefers-reduced-motion`.

## 6. Design and technical rules

- **English only**, in every file, including labels, alt text and comments (project rule).
- **Presentation-friendly:** large readable type, high contrast, works on a projector. Keyboard navigation
  between sections (arrow keys or PageUp/PageDown) and between pages.
- **Offline:** the local copy must open from disk with no network - no CDN scripts or web fonts. Write
  plain HTML, CSS and JavaScript, with inline SVG for diagrams. A small hand-written C# syntax highlighter
  is fine; a library is not.
- **Shared look:** one stylesheet and one script shared by all pages. Lane colours and signal colours used
  consistently across every diagram. `docs/overview_source/overview.html` has the project's existing
  palette (blue, go green, amber, stop red); stay close to it so the materials look like one set.
- **Light and dark** themes; usable at phone width with no horizontal scroll.
- **Do not modify any code, the scene, prefabs or project settings.** This task is documentation only.
  In particular never touch `FixedController.cs`, the scene's phase plan or cycle timing.

## 7. Deliverables

1. **Local files** in `docs/code_tour/`: `index.html`, one HTML page per file, `assets/style.css`,
   `assets/app.js`, and a short `docs/code_tour/README.md` saying how to open it and how it was checked.
   Add a line about it to the `docs/` table in the root `README.md`.
2. **A published Artifact** of the same pages (multi-file artifact, private by default). Load the
   `artifact-design` skill before publishing, and follow its title, theming and CDN rules. Give Kimmy the link.
3. **A commit** on this branch. No `Co-Authored-By: Claude` line (Kimmy does not want it).
   Ask before pushing or opening a PR.

## 8. Priority order

Finish and check each step before starting the next:

1. Shared stylesheet, script and page template; hub page with the architecture map and junction diagram.
2. `AdaptiveController` (the core of the presentation; it is about 1,300 lines, so give it in-page navigation).
3. `FixedController`, `CarAgent`, `ROIZone`, `IntersectionConfig`, `TrafficLight`.
4. `CarSpawner`, `ScenarioManager`, `DataLogger`, `TestCaseLibrary`.
5. The remaining runtime files, then the three Editor tools.
6. Publish the Artifact and commit.

## 9. Checks before calling it done

- Every variable in every table exists in the code with that name and default; check with a script, not by eye.
- Every scene value was read from `SampleScene.unity`, and every difference from the code default is flagged.
- Every line number points at the quoted code in the merged tree.
- Every number on the pages traces to the code, a baseline document or batch v5. List any you could not trace.
- Open every page in the browser pane: no console errors, every link works, animations play, pause and step,
  dark mode reads well, phone width has no horizontal scroll.
- `git diff` shows changes only under `docs/` and in `README.md`.
