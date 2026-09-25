# ABAC Smart Traffic Light

An adaptive traffic signal for the ABAC university entrance on Bang Na, and a Unity digital twin that
tests it against the junction's real fixed-time plan.

CSX3010 Senior Project 1 · Project 26008 · Warachai Aranchot, Ratchanon Puwapattarapokin, Badin Bangsen

New here? Start with [`docs/ABAC_Smart_Traffic_Light_Overview.pdf`](docs/ABAC_Smart_Traffic_Light_Overview.pdf),
a 14-page plain-language overview of the whole project.

## Where things are

| Folder | What is in it |
|---|---|
| `Assets/` | The Unity project: scene, scripts, prefabs, materials. `Assets/Scripts/AdaptiveController.cs` is the SODA controller. |
| `Tests/` | Tests that run outside Unity, plus `soda.py`, the reference implementation of the algorithm. |
| `Analysis/` | `analyze_results.py`: paired t-tests and charts from a batch of simulation runs. |
| `SimulationResults/` | One folder per experiment batch. **`batch_v4/` is the current one**; v1-v3 are superseded. |
| `tools/` | `demo_soda.py`: type in arriving cars and watch the algorithm decide, step by step. |
| `docs/` | Everything written about the project - see below. |
| `Packages/`, `ProjectSettings/` | Unity configuration. |

`Library/`, `Logs/`, `Temp/` and `UserSettings/` are rebuilt by Unity and are not in git.

### docs/

| Folder | Contents |
|---|---|
| `docs/` | The plain-language overview PDF, and `overview_source/` to re-render it. |
| `docs/report/` | `SP1_Report_DRAFT.md`, the project report. |
| `docs/soda/` | The algorithm's technical record. **`SODA_BASELINE.md` is the authority** on what the controller does and every defect found; the others are earlier versions and the reference trace. |
| `docs/unity/` | The simulation's technical record. **`Unity_BASELINE.md` is the authority** on how the simulated vehicles behave (car following, stopping, turning, the A2 lane change) and every change to it. |
| `docs/guides/` | How to build the scene, and how to use git on this project. |
| `docs/handoff/` | Progress notes and handoffs written between sessions and between team members. |
| `docs/correspondence/` | Questions and replies exchanged with the detection team. |

## Common tasks

Run every test (needs `mono`; on macOS `brew install mono`):

```bash
bash Tests/harness/run_tests.sh
```

Watch the algorithm decide:

```bash
python3 tools/demo_soda.py
```

Analyse a batch of runs:

```bash
cd Analysis && python3 analyze_results.py ../SimulationResults/batch_v4
```

## Two things that will catch you out

**The scene overrides the code.** `SampleScene.unity` stores every public field of every component in
the scene, and those values win over the defaults in the `.cs` files. Changing a default in code does
nothing to a component already in the scene. Change it through the Inspector preset, check the title
bar shows `SampleScene*`, save, and run the tests - `SceneAuditTest` reads the scene file and fails if
it disagrees with the code.

**All files are English.** Code, comments, tooltips, test labels and documents.
