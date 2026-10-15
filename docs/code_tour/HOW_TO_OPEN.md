# How to open the ABAC Code Tour

The code tour is a set of HTML pages that explains the 19 C# files of the Unity simulation. It is
interactive (animations, sliders), so it has to be opened in a web browser, not read on GitHub.

> Clicking `index.html` on GitHub shows the raw HTML source, not the page. Use one of the options below.

## Option 1: online link (no download)

**https://claude.ai/artifact/18vzZfUs8aAj38zRem3RRi**

This only opens once Kimmy has shared it with you from the page's **Share** menu. If it asks you to
sign in or says you have no access, ask Kimmy to share it, or use option 2.

## Option 2: download and open (works offline)

The repository is private, so you need to be added to it on GitHub first.

### Without git

1. Go to https://github.com/Kimmy213/ABAC-Traffic-Signal-Sim
2. Click **Code → Download ZIP** and unzip it.
3. Open the unzipped folder, go to `docs/code_tour`, and double-click `index.html`.

### With git, first time

```bash
git clone https://github.com/Kimmy213/ABAC-Traffic-Signal-Sim.git
```

Then open the first page.

macOS:

```bash
open ABAC-Traffic-Signal-Sim/docs/code_tour/index.html
```

Windows:

```bash
start ABAC-Traffic-Signal-Sim\docs\code_tour\index.html
```

Linux:

```bash
xdg-open ABAC-Traffic-Signal-Sim/docs/code_tour/index.html
```

The clone includes every batch of simulation results, so the download takes a while.

### With git, already cloned

Run these inside your copy of the repository:

```bash
git checkout main && git pull
```

```bash
open docs/code_tour/index.html
```

On Windows use `start docs\code_tour\index.html` instead of `open`.

## Moving around

| Key | Does |
|---|---|
| `←` `→` | previous / next section |
| `[` `]` | previous / next file |
| `h` | back to the overview |

Start from the overview page ("How the 19 files work together"), then follow the file list on the left.
The **Theme** button at the bottom of that list switches between light and dark.

## If something looks wrong

- **Text but no layout or diagrams:** the `assets` folder is missing. Open `index.html` from inside the
  full `docs/code_tour` folder, not a copy of the single file.
- **Old content:** run `git pull` on `main` again, then reload the page.
