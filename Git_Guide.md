# Using Git on the ABAC Traffic Sim project

**Current repo state** (checked against `.git/config`)

| | |
|---|---|
| Remote | `https://github.com/Kimmy213/ABAC-Traffic-Signal-Sim.git` |
| Branch | `main` (already tracking `origin/main`) |
| Folder | `C:\Users\USER\Desktop\ProjectSim\frist_try\ABAC_TrafficSim` |

Everything is already configured. No `git init` or `git remote add` needed - **you can push straight away**.

---

## 1. The normal routine (four commands is all you need)

Open **Terminal / PowerShell** (or the VS Code terminal) and:

```bash
cd C:\Users\USER\Desktop\ProjectSim\frist_try\ABAC_TrafficSim

git status          # 1. see what changed
git add .           # 2. select every changed file
git commit -m "a message saying what you did"    # 3. save one point in history
git push            # 4. send it to GitHub
```

**The mental model:**

```
edited files       ->  git add   ->   staging   ->  git commit  ->  local history  ->  git push  ->  GitHub
(working dir)                       (ready to go)                  (local repo)                    (remote)
```

- `add` = choose what goes into this round
- `commit` = stamp a save point on your machine (still offline)
- `push` = send every save point to GitHub

Committing alone does not reach GitHub. You have to push as well.

---

## 2. Commits for this round of work

A lot has just changed. Splitting it into several commits makes it far easier to read back.

```bash
# the 4-phase system
git add Assets/Scripts/IntersectionConfig.cs Assets/Scripts/FixedController.cs Assets/Scripts/AdaptiveController.cs
git commit -m "Expand to 4-phase system with conflict matrix

- Add IntersectionConfig as single source of truth for lanes + conflicts
- FixedController: phase list with concurrent lanes, carry-over green
- AdaptiveController: FCFS leader + conflict-free group building
- Fix infinite recursion in Build() that would hang Unity on Play"

# vehicle behaviour
git add Assets/Scripts/CarAgent.cs Assets/Scripts/CarSpawner.cs Assets/Scripts/ROIZone.cs Assets/Scripts/TrafficLight.cs
git commit -m "Add turning lanes, per-lane RNG, and car colors

- Waypoint steering with physics-based turn speed limit
- Per-lane System.Random so Fixed/Adaptive see identical traffic
- Fix cars not moving on green when light registration was cleared"

# tooling
git add Assets/Editor/ Assets/Scripts/SimulationControlPanel.cs Assets/Scripts/BatchRunPanel.cs Assets/Scripts/TestCaseLibrary.cs
git commit -m "Add scene builder, control panel, and batch run UI"

# everything else
git add .
git commit -m "Update scene, materials, docs, and analysis script"

git push
```

**If you want it simpler**, one commit is fine:

```bash
git add .
git commit -m "Expand to 4-phase system: 6 lanes, conflict matrix, control panel, batch UI, analysis script"
git push
```

---

## 3. About SimulationResults - read before pushing

`.gitignore` currently **does not exclude** the `SimulationResults/` folder.

There are about 41 old test files there now, and a real 270-run batch will produce **540 or more**. Left
alone, the repo gets cluttered.

**Recommended:** delete the old test files first. They are from the 3-phase era and no longer usable, and
`summary_all_runs.csv` has mixed schemas in it.

```bash
# remove all old test results
rmdir /s /q SimulationResults          # Windows CMD
# or
Remove-Item -Recurse -Force SimulationResults   # PowerShell
```

Then decide whether real results belong in git:

**Option A - keep the real results in git (recommended for a senior project).**
Raw data is the evidence behind the research, the examiners may ask to see it, and it survives a dead
laptop. No `.gitignore` change needed; just push as usual after a run.

**Option B - keep only the summary files.**
If you are worried about repo size, add this to the end of `.gitignore`:

```gitignore
# ===== Simulation Output =====
SimulationResults/detailed_*.csv
SimulationResults/summary_all_runs_OLD_*.csv
```

That keeps `summary_all_runs.csv`, `batch_summary_*.txt` and the `analysis_output/` folder.

> CSVs are plain text and very small; 540 of them should come to under 20 MB. **I lean towards A.**

---

## 4. The first push will ask you to log in

GitHub **stopped accepting passwords in 2021**, so your ordinary GitHub password will not work.

**Easiest on Windows:** when you push, a *Git Credential Manager* window appears. Click
**"Sign in with your browser"**, log in there, and you are done - it remembers you afterwards.

**If no window appears**, create a Personal Access Token yourself:

1. Go to https://github.com/settings/tokens
2. **Generate new token (classic)**
3. Tick the **`repo`** scope; nothing else is needed
4. Set an expiry (90 days covers the October defence)
5. **Copy the token immediately** - you cannot see it again once the page closes
6. When push asks for a username enter `Kimmy213`; when it asks for a password **enter the token**, not your password

---

## 5. Three things to check before every push

```bash
git status
```

- [ ] **no `Library/` or `Temp/` in the list** - if they appear, `.gitignore` is broken; do not add
- [ ] **every new file has its `.meta` alongside** - Unity needs them, or references break on another machine
- [ ] **`Assets/Scenes/SampleScene.unity` is in the list** if you ran Build Scene and pressed Ctrl+S

---

## 6. Other commands worth knowing

```bash
git log --oneline -10        # the last 10 commits
git diff                     # what you changed (not yet added)
git diff --stat              # a summary: which files, how many lines
git add Assets/Scripts/CarAgent.cs    # add one file only
git restore <file>           # discard changes to that file (not yet added)
git restore --staged <file>  # take it out of staging (keeping the edit)
```

---

## 7. What to do when you hit an error

| Error | What it means | Fix |
|---|---|---|
| `Updates were rejected because the remote contains work that you do not have locally` | someone else (or your other machine) pushed first | `git pull`, then `git push` again |
| `Please tell me who you are` | name and email not set | `git config --global user.name "name"` and `git config --global user.email "email"` |
| `Authentication failed` | you entered a password instead of a token | see section 4 |
| `nothing to commit, working tree clean` | nothing changed (or you forgot `git add`) | normal, not an error |
| `fatal: not a git repository` | you are in the wrong folder | `cd` into the project folder first |
| a strange screen full of `~` | it opened vim for the commit message | press `Esc`, type `:wq`, press Enter |

---

## 8. Moving between Windows and Mac

This already works. The routine is:

```bash
# on the machine you just finished on
git add . && git commit -m "..." && git push

# on the other machine, before starting work every time
git pull
```

**Always `git pull` before you start editing.** Forget it, edit the same file on both machines, and you get
a merge conflict that is awkward to resolve - especially in a `.unity` scene file, which is almost
impossible to merge by hand.

---

## 9. Habits worth forming

- **Commit before pressing `ABAC > Build 4-Phase Scene` every time.** The builder rewrites the real scene, so if you dislike the result you can undo with `git restore Assets/Scenes/SampleScene.unity`.
- **Commit before starting a 270-run batch.** Then you know exactly which version of the code produced those results, which matters for the credibility of the research.
- **Many small commits beat one large one.** You can roll back to exactly the right point.
- **Write a message that means something.** `"fix bug"` helps nobody; `"Fix cars not moving on green light"` helps a lot.

### A tip for the defence

Before the examination, tag the version used to collect the real data:

```bash
git tag -a v1.0-final-data -m "Version used for the 270-run experiment"
git push origin v1.0-final-data
```

If the examiners ask which code produced these results, the answer is the `v1.0-final-data` tag.
