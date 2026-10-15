# ABAC Code Tour

A set of HTML pages that walk through the 19 C# files of the Unity simulation: one overview page showing
how the files work together, then one page per file. Each file page goes from the whole file down to its
methods and every variable, with the real code, the value saved in the scene, and what changing it does.

## Open it

Open `index.html` in a browser. Everything is local (no network, no CDN), so it works offline on the
presentation machine. Keys: `←` `→` previous / next section, `[` `]` previous / next file, `h` overview.

## Rebuild it

The pages are generated. Edit the fragments in `generator/pages/`, then run from the repository root:

```bash
python3 docs/code_tour/generator/build.py
```

Needs Python 3 with PyYAML. The build expands four tags, so nothing quoted from the code is typed by hand:

| Tag | Becomes |
|---|---|
| `<excerpt file=... lines=... expect=.../>` | the real source lines with their line numbers; the build fails if `expect` is not on the first line |
| `<vartable for="Class">` | one row per field with type, code default and the value saved in `SampleScene.unity` (or `Car.prefab`) |
| `<sv .../>`, `<dv .../>` | one saved value or code default, inline |
| `<filemap/>`, `<roles/>`, `<conflictmatrix/>`, `<scenariotable/>`, `<palette/>` | figures and tables read from the code or the scene |

`python3 docs/code_tour/generator/build.py --dist DIR` also writes a copy for publishing as an Artifact.

## How it was checked

Every build runs these checks and prints any failure:

- every excerpt starts at the line it claims (a moved line fails the build);
- every documented field exists in the code, and every public field of every documented class has a row;
- every link and `#anchor` between pages resolves.

In the browser, on 15 Oct 2026: every page loaded with no script errors and every figure started; no page
scrolls sideways at 400 px; light and dark themes checked.

## Where the numbers come from

- The code and `Assets/Scenes/SampleScene.unity`, read by `generator/extract.py` at build time.
- `docs/soda/SODA_BASELINE.md` and `docs/unity/Unity_BASELINE.md`.
- Batch v5 (`SimulationResults/batch_v5/`). Shares of phase end reasons and SODA counters on the
  AdaptiveController page were computed from its `phases_Adaptive_*.csv` and `summary_all_runs.csv`.
- The queue-discharge data on the CarAgent page comes from running the real `CarAgent` code outside Unity
  with `generator/measure_headway.cs` (instructions in the file). It reproduces `HeadwayTest`'s 2.28 s/veh.

Interactive figures that re-implement a rule (SODA's Q1-Q4, the green-time formula, gap-out, `StableHash`,
the largest-remainder split) port the formula from the file named next to them. `StableHash` was checked
against the compiled C#. The arrival dots on the CarSpawner page and the zone counts on the ROIZone page
are illustrations: the rule is the code's, the random numbers are not from a real run.
