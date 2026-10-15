"""Builds the ABAC code tour: docs/code_tour/*.html.

    python3 docs/code_tour/generator/build.py          # build, then run every check
    python3 docs/code_tour/generator/build.py --dist D # also write an Artifact-ready copy into D

Each page is a hand-written fragment in generator/pages/<slug>.html. This script wraps it in the shared
frame and expands four tags so that nothing quoted from the code is typed in by hand:

    <excerpt file="Assets/Scripts/X.cs" lines="10-20" expect="text on the first line" hl="12,13" caption="..."/>
        the real source lines, with their real line numbers. Fails the build if `expect` is not on
        the first line, so an excerpt cannot silently drift after the code changes.
    <vartable for="ClassName"> <v n="field" k="measured|code|unknown">meaning<eff>effect</eff><cpl>coupled</cpl></v> ... </vartable>
        one row per documented field, with type, unit-free code default, the value saved in
        SampleScene.unity (or Car.prefab) and a flag where the two differ. Fails if a field does not exist.
    <sv c="Class" f="field"/>   the scene value of one field, inline.
    <dv c="Class" f="field"/>   the code default of one field, inline.

Checks run after every build: excerpts, field names, coverage of every public field, links and anchors.
"""
import html
import os
import re
import shutil
import sys

sys.path.insert(0, os.path.dirname(__file__))
import extract  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.abspath(os.path.join(HERE, ".."))
ROOT = extract.ROOT

LAYERS = [
    ("junction", "Junction model", "--l-junction"),
    ("traffic", "Traffic", "--l-traffic"),
    ("sensing", "Sensing", "--l-sensing"),
    ("control", "Control", "--l-control"),
    ("experiment", "Experiment", "--l-experiment"),
    ("ui", "Runtime UI", "--l-ui"),
    ("editor", "Editor tools", "--l-editor"),
]

# slug, source file, layer, classes documented on the page, one-sentence role
PAGES = [
    ("intersection-config", "Assets/Scripts/IntersectionConfig.cs", "junction",
     ["IntersectionConfig", "ApproachLane", "ConflictPair"],
     "The junction as data: the seven lanes, which of them have a signal, and the six lane pairs that may never be green together."),
    ("traffic-light", "Assets/Scripts/TrafficLight.cs", "junction", ["TrafficLight"],
     "One signal lamp: holds red, yellow or green, repaints itself and tells every car registered to it."),
    ("car-spawner", "Assets/Scripts/CarSpawner.cs", "traffic", ["CarSpawner", "PendingArrival"],
     "Releases cars into one lane, at Poisson-random or scripted times, with a seeded random stream of its own."),
    ("car-agent", "Assets/Scripts/CarAgent.cs", "traffic", ["CarAgent"],
     "One vehicle: follows the car ahead, stops at a red, decides at amber, turns, and makes the A2 lane change."),
    ("test-case-library", "Assets/Scripts/TestCaseLibrary.cs", "traffic",
     ["ScheduledBurst", "LaneSchedule", "LaneArrivalRate", "TestCase", "TestCasePresets"],
     "The traffic patterns: the data types for a test case and the built-in cases T1-T13, Compare-101 and ForeverTest."),
    ("custom-test-case", "Assets/Scripts/CustomTestCase.cs", "traffic", ["CustomTestCase"],
     "A runtime window for building a test case without the Inspector, then running it through the normal path."),
    ("roi-zone", "Assets/Scripts/ROIZone.cs", "sensing", ["ROIZone"],
     "A detection zone: counts the cars inside it, which is all the adaptive controller is allowed to see."),
    ("roi-highlight", "Assets/Scripts/RoiHighlight.cs", "sensing", ["RoiHighlight"],
     "Draws the green tracking box around a car while it is inside a detection zone."),
    ("fixed-controller", "Assets/Scripts/FixedController.cs", "control", ["FixedController", "FixedPhase"],
     "The baseline: the junction's real 106 s fixed-time plan, cycled regardless of traffic."),
    ("adaptive-controller", "Assets/Scripts/AdaptiveController.cs", "control",
     ["AdaptiveController", "SignalHead", "TraceStep"],
     "SODA, the adaptive controller: reads four counts every second and decides who goes next and for how long."),
    ("scenario-manager", "Assets/Scripts/ScenarioManager.cs", "experiment",
     ["ScenarioManager", "ScenarioDef", "ScenarioAggregate"],
     "Runs the experiment: 4 scenarios x 27 repetitions x 2 controllers = 216 runs, paired by seed."),
    ("data-logger", "Assets/Scripts/DataLogger.cs", "experiment", ["DataLogger", "VehicleRecord", "PhaseRecord"],
     "Records every vehicle's wait and every adaptive phase, and writes the three CSV files a batch produces."),
    ("simulation-control-panel", "Assets/Scripts/SimulationControlPanel.cs", "ui", ["SimulationControlPanel"],
     "The on-screen control panel: play, pause, pick a test case, switch Fixed and Adaptive, watch the lanes."),
    ("batch-run-panel", "Assets/Scripts/BatchRunPanel.cs", "ui", ["BatchRunPanel"],
     "The batch progress screen and the end-of-batch summary; it reads ScenarioManager and controls nothing else."),
    ("camera-director", "Assets/Scripts/CameraDirector.cs", "ui", ["CameraDirector", "Shot"],
     "Preset camera shots of the junction, a follow-a-car mode and the shot bar along the bottom of the screen."),
    ("ui-theme", "Assets/Scripts/UITheme.cs", "ui", ["UITheme"],
     "The shared look of every on-screen panel: palette, text styles and small drawing helpers."),
    ("abac-scene-builder", "Assets/Editor/ABACSceneBuilder.cs", "editor", ["ABACSceneBuilder"],
     "Menu ABAC > Build 4-Phase Scene: builds the whole junction from coordinates taken on site and wires every component."),
    ("car-prefab-builder", "Assets/Editor/CarPrefabBuilder.cs", "editor", ["CarPrefabBuilder"],
     "Menu ABAC > Rebuild Car Prefab: turns the 3D car model into Car.prefab, scaled, oriented and wired."),
    ("scene-parameter-audit", "Assets/Editor/SceneParameterAudit.cs", "editor", ["SceneParameterAudit"],
     "Menu ABAC > Verify Scene Against Code: lists every field where the scene overrides the code."),
]

# Real references in the code (calls, fields of that type, FindFirstObjectByType). Comments do not count.
# (from, to, what passes, kind)  kind: run = simulation path, ui = display only, edit = editor tools
EDGES = [
    ("CarSpawner", "CarAgent", "creates cars and sets their lane, stop line, path and light", "run"),
    ("CarSpawner", "TrafficLight", "registers each new car with its lane's light", "run"),
    ("CarSpawner", "TestCaseLibrary", "uses ArrivalMode and ScheduledBurst", "run"),
    ("TrafficLight", "CarAgent", "pushes every state change to registered cars", "run"),
    ("CarAgent", "TrafficLight", "polls currentState every step", "run"),
    ("ROIZone", "CarAgent", "reads the lane and driver type of a car entering", "run"),
    ("ROIZone", "DataLogger", "LogVehicle: the wait of each car leaving the zone", "run"),
    ("ROIZone", "RoiHighlight", "EnterZone / ExitZone", "ui"),
    ("AdaptiveController", "ROIZone", "GetSampledCount: the vehicle count per snapshot", "run"),
    ("AdaptiveController", "IntersectionConfig", "signal heads, conflict test, lane names", "run"),
    ("AdaptiveController", "TrafficLight", "SetState red / yellow / green", "run"),
    ("AdaptiveController", "DataLogger", "LogPhase and LogStarvationEvent", "run"),
    ("FixedController", "IntersectionConfig", "lane names to signal indices, conflict check", "run"),
    ("FixedController", "TrafficLight", "SetState red / yellow / green", "run"),
    ("IntersectionConfig", "CarSpawner", "reset, seed and arrival pattern for every lane", "run"),
    ("IntersectionConfig", "ROIZone", "ClearAll, Accepts", "run"),
    ("IntersectionConfig", "TrafficLight", "ClearRegistrations", "run"),
    ("IntersectionConfig", "TestCaseLibrary", "ApplyTestCase reads a TestCase", "run"),
    ("ScenarioManager", "IntersectionConfig", "ResetAllLanes(seed), StableHash, lane list", "run"),
    ("ScenarioManager", "FixedController", "ForceStop, RestartCycle, enable / disable", "run"),
    ("ScenarioManager", "AdaptiveController", "ForceStop, RestartCycle, enable / disable", "run"),
    ("ScenarioManager", "DataLogger", "ResetRun, read results, export CSV", "run"),
    ("ScenarioManager", "CarSpawner", "UsePoisson(rate) per lane", "run"),
    ("ScenarioManager", "TestCaseLibrary", "LaneArrivalRate", "run"),
    ("DataLogger", "AdaptiveController", "reads SODA counters for the summary CSV", "run"),
    ("SimulationControlPanel", "IntersectionConfig", "ApplyTestCase, ResetAllLanes, lane table", "ui"),
    ("SimulationControlPanel", "FixedController", "switch controller", "ui"),
    ("SimulationControlPanel", "AdaptiveController", "switch controller, queue clocks", "ui"),
    ("SimulationControlPanel", "DataLogger", "statistics, export", "ui"),
    ("SimulationControlPanel", "ScenarioManager", "start / stop a batch", "ui"),
    ("SimulationControlPanel", "TestCaseLibrary", "TestCasePresets.BuildDefault", "ui"),
    ("CustomTestCase", "SimulationControlPanel", "RunCustomCase", "ui"),
    ("CustomTestCase", "IntersectionConfig", "lane list, StableHash", "ui"),
    ("CustomTestCase", "CarSpawner", "driver mix per spawner", "ui"),
    ("CustomTestCase", "TestCaseLibrary", "builds a TestCase", "ui"),
    ("BatchRunPanel", "ScenarioManager", "progress, ETA, results", "ui"),
    ("CameraDirector", "CarAgent", "NearestInLane for follow mode", "ui"),
    ("CameraDirector", "RoiHighlight", "ShowAll toggle", "ui"),
    ("ABACSceneBuilder", "IntersectionConfig", "LoadABACPreset and wiring", "edit"),
    ("ABACSceneBuilder", "FixedController", "LoadABACPreset", "edit"),
    ("ABACSceneBuilder", "AdaptiveController", "LoadMeasuredPreset", "edit"),
    ("ABACSceneBuilder", "ScenarioManager", "LoadBatchPreset, LoadABACScenarioPreset", "edit"),
    ("ABACSceneBuilder", "CarSpawner", "one spawner per lane", "edit"),
    ("ABACSceneBuilder", "ROIZone", "six detection zones", "edit"),
    ("ABACSceneBuilder", "TrafficLight", "four lights", "edit"),
    ("ABACSceneBuilder", "CameraDirector", "MakeShots", "edit"),
    ("ABACSceneBuilder", "SimulationControlPanel", "wiring, LoadDefaultTestCases", "edit"),
    ("ABACSceneBuilder", "CustomTestCase", "wiring", "edit"),
    ("ABACSceneBuilder", "BatchRunPanel", "wiring", "edit"),
    ("ABACSceneBuilder", "DataLogger", "adds the component", "edit"),
    ("ABACSceneBuilder", "CarPrefabBuilder", "Rebuild", "edit"),
    ("CarPrefabBuilder", "CarAgent", "carLength, bodyRenderer", "edit"),
    ("CarPrefabBuilder", "RoiHighlight", "boxRoot", "edit"),
    ("SceneParameterAudit", "AdaptiveController", "compares scene against code", "edit"),
    ("SceneParameterAudit", "ScenarioManager", "compares scene against code", "edit"),
    ("SceneParameterAudit", "FixedController", "compares scene against code", "edit"),
]
# UITheme is used for drawing by 9 files; shown on its own page rather than as 9 edges.

PREFAB = "Assets/Prefabs/Car.prefab"

errors, warnings = [], []


def page_of_file_class(name):
    for p in PAGES:
        if os.path.basename(p[1])[:-3] == name:
            return p
    return None


def slug_for(name):
    p = page_of_file_class(name)
    return p[0] + ".html" if p else None


# ---------------------------------------------------------------- values

def clean_default(d):
    if d is None:
        return None
    d = d.strip()
    m = re.fullmatch(r"(-?\d+(?:\.\d+)?)f", d)
    if m:
        return m.group(1)
    return d


def resolve_default(cls_fields, d, all_classes):
    """Turns `MEASURED_STARTUP_LOST_TIME` or `ABACSceneBuilder.CAR_WIDTH` into its literal."""
    seen = 0
    while d and re.fullmatch(r"[A-Za-z_][\w\.]*", d) and seen < 5:
        seen += 1
        if "." in d:
            c, f = d.rsplit(".", 1)
            src = all_classes.get(c, {}).get("fields", {})
        else:
            c, f, src = None, d, cls_fields
        if f in src and src[f]["default"] is not None:
            d = clean_default(src[f]["default"])
        else:
            break
    return d


def num(v):
    try:
        return float(v)
    except (TypeError, ValueError):
        return None


def fmt_scene(v):
    if isinstance(v, bool):
        return "1" if v else "0"
    if isinstance(v, (int, float)):
        s = repr(v)
        return s[:-2] if s.endswith(".0") else s
    if isinstance(v, dict):
        if "fileID" in v:
            return "none" if v.get("fileID") == 0 else "set"
        if set(v) >= {"x", "y", "z"}:
            return "(%s, %s, %s)" % (fmt_scene(v["x"]), fmt_scene(v["y"]), fmt_scene(v["z"]))
        return "{...}"
    if isinstance(v, list):
        return "list of %d" % len(v)
    if v is None:
        return "empty"
    return str(v)


def comparable_default(field, classes, owner):
    d = resolve_default(classes[owner]["fields"], clean_default(field["default"]), classes)
    if d in ("true", "false"):
        return "1" if d == "true" else "0"
    if d is not None and num(d) is not None:
        return fmt_scene(float(d))
    return None


def scene_cell(owner, field, classes, scene, prefab):
    """HTML for the 'value saved in the scene' column."""
    mod = field["modifier"]
    if mod in ("const", "static readonly", "static"):
        return '<span class="same">%s - not serialised</span>' % ("const" if mod == "const" else "static")
    rows = scene.get(owner)
    where = "scene"
    if not rows and owner in prefab:
        rows, where = prefab[owner], "Car.prefab"
    if not rows:
        return '<span class="same">not a scene component</span>'
    want = comparable_default(field, classes, owner)
    vals = []
    for r in rows:
        if field["name"] in r["values"]:
            vals.append((r["object"], r["values"][field["name"]]))
    if not vals:
        return '<span class="same">not serialised</span>'
    shown = [(o, fmt_scene(v)) for o, v in vals]
    uniq = sorted(set(s for _, s in shown))
    differs = lambda s: want is not None and num(s) is not None and abs(num(s) - num(want)) > 1e-6
    prefix = "" if where == "scene" else '<small>Car.prefab</small> '
    if len(uniq) == 1:
        s = uniq[0]
        cls = "diff" if differs(s) else ""
        n = "" if len(shown) == 1 else '<small>all %d</small> ' % len(shown)
        return prefix + n + '<span class="%s">%s</span>' % (cls, html.escape(s))
    parts = []
    for o, s in shown:
        label = re.sub(r"^(Spawner_|ROI_|Light_)", "", o)
        parts.append('%s <span class="%s">%s</span>' % (html.escape(label), "diff" if differs(s) else "", html.escape(s)))
    return prefix + "<br>".join(parts)


def default_cell(owner, field, classes):
    d = field["default"]
    if d is None:
        return '<span class="same">type default</span>'
    lit = clean_default(d)
    res = resolve_default(classes[owner]["fields"], lit, classes)
    txt = html.escape(lit if len(lit) < 34 else lit[:31] + "...")
    if res != lit and res is not None and len(res) < 20:
        txt += ' <span class="same">= %s</span>' % html.escape(res)
    return txt


# ---------------------------------------------------------------- tag expansion

def expand_excerpt(m):
    a = dict(re.findall(r'(\w+)="([^"]*)"', m.group(0)))
    path = os.path.join(ROOT, a["file"])
    lines = extract.read_lines(path)
    lo, hi = [int(x) for x in a["lines"].split("-")]
    if hi > len(lines) or lo < 1:
        errors.append("excerpt %s %s is out of range" % (a["file"], a["lines"]))
        return ""
    chunk = lines[lo - 1:hi]
    if "expect" in a and html.unescape(a["expect"]) not in chunk[0]:
        errors.append("excerpt %s:%d does not start with %r (found %r)" % (a["file"], lo, a["expect"], chunk[0].strip()))
    # strip the common indentation, so a method body does not start 8 spaces in
    ind = min((len(l) - len(l.lstrip()) for l in chunk if l.strip()), default=0)
    hl = set()
    for part in a.get("hl", "").split(","):
        part = part.strip()
        if "-" in part:
            x, y = part.split("-")
            hl.update(range(int(x), int(y) + 1))
        elif part:
            hl.add(int(part))
    body = "".join('<span class="ln%s" data-n="%d">%s</span>' % (" hl" if (lo + i) in hl else "", lo + i, html.escape(l[ind:]))
                   for i, l in enumerate(chunk))
    cap = a.get("caption", "")
    return ('<figure class="code"><figcaption><span>%s</span><span class="loc">%s : %d-%d</span></figcaption>'
            '<pre class="cs">%s</pre></figure>' % (cap, a["file"].split("/")[-1], lo, hi, body))


def expand_vartable(m, classes, scene, prefab, documented):
    owner = m.group(1)
    inner = m.group(2)
    if owner not in classes:
        errors.append("vartable for unknown class %s" % owner)
        return ""
    fields = classes[owner]["fields"]
    rows = []
    for item in re.finditer(r'<vgroup>(.*?)</vgroup>|<v n="(\w+)" k="(\w+)">(.*?)</v>', inner, re.S):
        if item.group(1) is not None:
            rows.append('<tr class="group-row"><td colspan="5">%s</td></tr>' % item.group(1))
            continue
        name, kind, body = item.group(2), item.group(3), item.group(4)
        if name not in fields:
            errors.append("%s.%s is documented but does not exist in the code" % (owner, name))
            continue
        if kind not in ("measured", "code", "unknown"):
            errors.append("%s.%s has kind %r" % (owner, name, kind))
        documented.add((owner, name))
        f = fields[name]
        meaning = re.split(r"<eff>|<cpl>", body)[0].strip()
        eff = re.search(r"<eff>(.*?)</eff>", body, re.S)
        cpl = re.search(r"<cpl>(.*?)</cpl>", body, re.S)
        kind_label = {"measured": "measured", "code": "from the code", "unknown": "unknown"}.get(kind, kind)
        eff_html = '<span class="kind %s">%s</span>%s' % (kind, kind_label, eff.group(1).strip() if eff else "")
        if cpl:
            eff_html += '<span class="cpl">%s</span>' % cpl.group(1).strip()
        typ = f["type"] + (" " + f["modifier"] if f["modifier"] else "")
        rows.append(
            '<tr id="v-%s-%s"><td class="vn"><a href="#v-%s-%s">%s</a><small>%s · line %d</small></td>'
            '<td class="val">%s</td><td class="val">%s</td><td>%s</td><td class="eff">%s</td></tr>'
            % (owner, name, owner, name, name, html.escape(typ), f["line"],
               default_cell(owner, f, classes), scene_cell(owner, f, classes, scene, prefab), meaning, eff_html))
    head = ('<div class="vt-wrap"><table class="vt"><colgroup><col class="c-f"><col class="c-d"><col class="c-s"><col class="c-m"><col class="c-e"></colgroup>'
            '<thead><tr><th>Field</th><th>Code default</th><th>Saved value</th>'
            '<th>What it means</th><th>What happens if you change it</th></tr></thead><tbody>')
    return head + "".join(rows) + "</tbody></table></div>"


def expand_inline(m, classes, scene, prefab, kind):
    a = dict(re.findall(r'(\w+)="([^"]*)"', m.group(0)))
    c, f = a["c"], a["f"]
    if c not in classes or f not in classes[c]["fields"]:
        errors.append("inline value %s.%s does not exist" % (c, f))
        return "?"
    field = classes[c]["fields"][f]
    if kind == "dv":
        return "<code>%s</code>" % html.escape(resolve_default(classes[c]["fields"], clean_default(field["default"]), classes) or "")
    rows = scene.get(c) or prefab.get(c) or []
    for r in rows:
        if f in r["values"] and ("o" not in a or r["object"] == a["o"]):
            return "<code>%s</code>" % html.escape(fmt_scene(r["values"][f]))
    errors.append("no saved value for %s.%s" % (c, f))
    return "?"


# ---------------------------------------------------------------- frame

def layer_info(lid):
    for l in LAYERS:
        if l[0] == lid:
            return l
    raise KeyError(lid)


def sidebar(current_slug, prefix=""):
    out = ['<a class="brand" href="%sindex.html"><b>ABAC Code Tour</b><span>Unity digital twin · 19 files</span></a>' % prefix,
           '<button class="menu-btn" id="menu-btn" type="button" aria-expanded="false">Files</button>',
           '<nav aria-label="Files">',
           '<ul><li><a href="%sindex.html"%s>How the files work together</a></li></ul>'
           % (prefix, ' aria-current="page"' if current_slug == "index" else "")]
    for lid, lname, var in LAYERS:
        out.append('<h4><i style="background:var(%s)"></i>%s</h4><ul>' % (var, lname))
        for p in PAGES:
            if p[2] != lid:
                continue
            name = os.path.basename(p[1])[:-3]
            lc = extract.line_count(p[1])
            cur = ' aria-current="page"' if p[0] == current_slug else ""
            out.append('<li><a href="%s%s.html"%s>%s<small>%d</small></a></li>' % (prefix, p[0], cur, name, lc))
        out.append("</ul>")
    out.append('</nav><div class="tools"><button id="theme-btn" type="button">Theme</button></div>')
    return "\n".join(out)


def deps_block(name):
    outs = [(t, lab) for (f, t, lab, k) in EDGES if f == name]
    ins = [(f, lab) for (f, t, lab, k) in EDGES if t == name]

    def links(items):
        if not items:
            return '<span class="muted">none</span>'
        seen, parts = set(), []
        for n, lab in items:
            if n in seen:
                continue
            seen.add(n)
            parts.append('<a href="%s" title="%s">%s</a>' % (slug_for(n), html.escape(lab), n))
        return " ".join(parts)
    return ('<div class="deps"><div><b>Uses</b>%s</div><div><b>Used by</b>%s</div></div>' % (links(outs), links(ins)))


PAGE_TMPL = """<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
<title>{title}</title>
<meta name="description" content="{desc}">
<link rel="stylesheet" href="assets/style.css?v={v_css}">
<script>try{{var t=localStorage.getItem("tour-theme");if(t==="light"||t==="dark")document.documentElement.setAttribute("data-theme",t)}}catch(e){{}}</script>
</head>
<body data-prev="{prev}" data-next="{next}" data-hub="index.html">
<div class="frame">
<aside class="side">
{sidebar}
</aside>
<main class="main" id="main">
{head}
{body}
{pager}
<p class="keys"><kbd>&larr;</kbd> <kbd>&rarr;</kbd> previous / next section &nbsp;·&nbsp; <kbd>[</kbd> <kbd>]</kbd> previous / next file &nbsp;·&nbsp; <kbd>h</kbd> overview</p>
</main>
</div>
<script src="assets/data.js?v={v_data}"></script>
<script src="assets/app.js?v={v_app}"></script>
<script src="assets/widgets.js?v={v_widgets}"></script>
</body>
</html>
"""


def file_header(p):
    slug, path, lid, classes_doc, role = p
    name = os.path.basename(path)[:-3]
    l = layer_info(lid)
    lc = extract.line_count(path)
    return ('<header class="file-head"><p class="path">%s</p><h1>%s</h1>'
            '<div class="badges"><span class="badge layer" style="background:var(%s)">%s</span>'
            '<span class="badge">%d lines</span><span class="badge">%s</span></div>'
            '<p class="lede">%s</p>%s<nav class="toc" id="toc" aria-label="On this page"><b>On this page</b></nav></header>'
            % (path, name, l[2], l[1], lc, "Editor only" if path.startswith("Assets/Editor") else "Runtime",
               role, deps_block(name)))


def pager(i):
    order = ["index"] + [p[0] for p in PAGES]
    names = {"index": "How the files work together"}
    names.update({p[0]: os.path.basename(p[1])[:-3] for p in PAGES})
    prev = order[i - 1] if i > 0 else None
    nxt = order[i + 1] if i + 1 < len(order) else None
    out = '<nav class="pager" aria-label="Files">'
    if prev:
        out += '<a class="prev" href="%s.html"><small>&larr; Previous</small><b>%s</b></a>' % (prev, names[prev])
    if nxt:
        out += '<a class="next" href="%s.html"><small>Next &rarr;</small><b>%s</b></a>' % (nxt, names[nxt])
    return out + "</nav>", (prev + ".html" if prev else ""), (nxt + ".html" if nxt else "")


def asset_version(name):
    """Short content hash, so a browser never shows a stale stylesheet or script after a rebuild."""
    import hashlib
    with open(os.path.join(OUT, "assets", name), "rb") as fh:
        return hashlib.sha1(fh.read()).hexdigest()[:8]


def render(slug, title, desc, head, body, idx):
    pg, prev, nxt = pager(idx)
    return PAGE_TMPL.format(title=html.escape(title), desc=html.escape(desc), prev=prev, next=nxt,
                            sidebar=sidebar(slug), head=head, body=body, pager=pg,
                            v_css=asset_version("style.css"), v_data=asset_version("data.js"),
                            v_app=asset_version("app.js"), v_widgets=asset_version("widgets.js"))


def filemap_tag():
    import json
    pages = {os.path.basename(p[1])[:-3]: p[0] + ".html" for p in PAGES}
    lines = {os.path.basename(p[1])[:-3]: extract.line_count(p[1]) for p in PAGES}
    edges = [[f, t, lab, k] for (f, t, lab, k) in EDGES]
    return ('<div data-widget="filemap" data-edges="%s" data-pages="%s" data-lines="%s"></div>'
            % tuple(html.escape(json.dumps(x), quote=True) for x in (edges, pages, lines)))


def fileindex_tag():
    out = ['<div class="outline">']
    for lid, lname, var in LAYERS:
        out.append('<section><h5 style="color:var(%s)">%s</h5><ul>' % (var, lname))
        for p in PAGES:
            if p[2] == lid:
                out.append('<li><a href="%s.html">%s</a> <span class="muted">%d</span></li>'
                           % (p[0], os.path.basename(p[1])[:-3], extract.line_count(p[1])))
        out.append("</ul></section>")
    out.append("</div>")
    return "".join(out)


def roles_tag():
    rows = []
    for lid, lname, var in LAYERS:
        rows.append('<tr class="group-row"><td colspan="3">%s</td></tr>' % lname)
        for p in PAGES:
            if p[2] == lid:
                rows.append('<tr><td class="vn"><a href="%s.html">%s</a></td><td class="val">%d</td><td>%s</td></tr>'
                            % (p[0], os.path.basename(p[1])[:-3], extract.line_count(p[1]), html.escape(p[4])))
    return ('<div class="vt-wrap"><table><thead><tr><th>File</th><th>Lines</th><th>What it does</th></tr></thead><tbody>'
            + "".join(rows) + "</tbody></table></div>")


def conflictmatrix_tag():
    """The conflict pairs, parsed from IntersectionConfig.LoadABACPreset rather than typed in."""
    import json
    src = "\n".join(extract.read_lines(os.path.join(ROOT, "Assets/Scripts/IntersectionConfig.cs")))
    pairs = re.findall(r'new ConflictPair \{ laneA = "(\w+)",\s*laneB = "(\w+)",\s*reason = "([^"]*)" \}', src)
    lanes = re.findall(r'new ApproachLane \{ laneName = "(\w+)",\s*isSignalized = true', src)
    if len(pairs) != 6 or len(lanes) != 5:
        errors.append("conflict matrix: expected 6 pairs and 5 signalised lanes, found %d and %d" % (len(pairs), len(lanes)))
    return ('<div data-widget="conflictMatrix" data-lanes="%s" data-pairs="%s"></div>'
            % (html.escape(json.dumps(lanes), quote=True), html.escape(json.dumps(pairs), quote=True)))


def scenariotable_tag(scene):
    """The batch's arrival rates, as saved in SampleScene.unity (what batch v5 ran)."""
    rows = scene["ScenarioManager"][0]["values"]["scenarios"]
    lanes = ["A", "A2", "Ax", "B", "Bx", "CB", "CA"]
    out = ['<div class="tw"><table class="plain"><thead><tr><th>Scenario</th>']
    out += ["<th>%s</th>" % l for l in lanes]
    out.append("</tr></thead><tbody>")
    for sc in rows:
        rate = {r["laneName"]: r["meanInterval"] for r in sc["laneRates"]}
        out.append('<tr><td class="vn">%s</td>' % html.escape(sc["name"]))
        for l in lanes:
            v = rate.get(l)
            txt = "closed" if v is not None and v >= 999999 else ("%.1f" % v if v is not None else "-")
            out.append('<td class="num">%s</td>' % txt)
        out.append("</tr>")
    out.append("</tbody></table></div>")
    return "".join(out)


def palette_tag():
    """UITheme's colours, parsed from the source."""
    import json
    src = "\n".join(extract.read_lines(os.path.join(ROOT, "Assets/Scripts/UITheme.cs")))
    out = []
    for name, args in re.findall(r"public static readonly Color (\w+)\s*=\s*new Color\(([^)]*)\);", src):
        v = [float(x.strip().rstrip("f")) for x in args.split(",")]
        r, g, b = (int(round(c * 255)) for c in v[:3])
        a = v[3] if len(v) > 3 else 1.0
        css = "rgba(%d,%d,%d,%.2f)" % (r, g, b, a)
        out.append([name, css, "#%02x%02x%02x" % (r, g, b) + ("" if a >= 0.999 else " · alpha %.2f" % a)])
    return '<div data-widget="palette" data-colors="%s"></div>' % html.escape(json.dumps(out), quote=True)


def expand(fragment, classes, scene, prefab, documented):
    if "<palette/>" in fragment:
        fragment = fragment.replace("<palette/>", palette_tag())
    if "<scenariotable/>" in fragment:
        fragment = fragment.replace("<scenariotable/>", scenariotable_tag(scene))
    if "<conflictmatrix/>" in fragment:
        fragment = fragment.replace("<conflictmatrix/>", conflictmatrix_tag())
    fragment = fragment.replace("<filemap/>", filemap_tag()).replace("<fileindex/>", fileindex_tag()).replace("<roles/>", roles_tag())
    fragment = re.sub(r"<excerpt\b[^>]*/>", expand_excerpt, fragment)
    fragment = re.sub(r'<vartable for="(\w+)">(.*?)</vartable>',
                      lambda m: expand_vartable(m, classes, scene, prefab, documented), fragment, flags=re.S)
    fragment = re.sub(r"<sv\b[^>]*/>", lambda m: expand_inline(m, classes, scene, prefab, "sv"), fragment)
    fragment = re.sub(r"<dv\b[^>]*/>", lambda m: expand_inline(m, classes, scene, prefab, "dv"), fragment)
    return fragment


def read_prefab():
    """CarAgent and RoiHighlight live on Car.prefab, not in the scene."""
    saved = extract.SCENE
    extract.SCENE = PREFAB
    try:
        return extract.extract_scene()
    finally:
        extract.SCENE = saved


def check_links():
    files = {f for f in os.listdir(OUT) if f.endswith(".html")}
    ids = {}
    for f in files:
        with open(os.path.join(OUT, f), encoding="utf-8") as fh:
            t = fh.read()
        ids[f] = set(re.findall(r'\bid="([^"]+)"', t))
    for f in files:
        with open(os.path.join(OUT, f), encoding="utf-8") as fh:
            t = fh.read()
        for href in re.findall(r'href="([^"]+)"', t):
            if href.startswith(("http://", "https://", "mailto:")) or href.startswith("assets/"):
                continue
            target, _, anchor = href.partition("#")
            target = target or f
            if target not in files:
                errors.append("%s links to missing page %s" % (f, href))
            elif anchor and anchor not in ids[target]:
                errors.append("%s links to missing anchor %s" % (f, href))


def main():
    classes = extract.extract_fields()
    scene = extract.extract_scene()
    prefab = read_prefab()
    documented = set()

    built = 0
    with open(os.path.join(HERE, "pages", "index.html"), encoding="utf-8") as fh:
        frag = expand(fh.read(), classes, scene, prefab, documented)
    out = render("index", "ABAC Code Tour",
                 "How the 19 C# files of the ABAC Unity digital twin work together, file by file.",
                 "", frag, 0)
    with open(os.path.join(OUT, "index.html"), "w", encoding="utf-8") as fh:
        fh.write(out)
    built += 1

    for i, p in enumerate(PAGES, start=1):
        src = os.path.join(HERE, "pages", p[0] + ".html")
        name = os.path.basename(p[1])[:-3]
        if not os.path.exists(src):
            warnings.append("page %s not written yet" % p[0])
            frag = '<p class="callout warn">This page is not written yet.</p>'
        else:
            with open(src, encoding="utf-8") as fh:
                frag = fh.read()
            # coverage is only checked for pages that exist
            for c in p[3]:
                for fname in classes.get(c, {}).get("fields", {}):
                    pass
        frag = expand(frag, classes, scene, prefab, documented)
        out = render(p[0], name + " · ABAC Code Tour", p[4], file_header(p), frag, i)
        with open(os.path.join(OUT, p[0] + ".html"), "w", encoding="utf-8") as fh:
            fh.write(out)
        built += 1

    # coverage: every public field of every class documented on a written page
    for p in PAGES:
        if not os.path.exists(os.path.join(HERE, "pages", p[0] + ".html")):
            continue
        for c in p[3]:
            for fname in classes.get(c, {}).get("fields", {}):
                if (c, fname) not in documented:
                    warnings.append("%s.%s (page %s) has no row in a variable table" % (c, fname, p[0]))

    check_links()
    print("built %d pages into %s" % (built, os.path.relpath(OUT, ROOT)))
    for w in warnings:
        print("  warning:", w)
    for e in errors:
        print("  ERROR:", e)

    if "--dist" in sys.argv:
        make_dist(sys.argv[sys.argv.index("--dist") + 1])
    return 1 if errors else 0


def make_dist(dist):
    """Artifact copy: the published page is wrapped in its own skeleton, so index.html loses its
    doctype, html, head and body tags there. Every other page is served as-is."""
    if os.path.isdir(dist):
        shutil.rmtree(dist)
    os.makedirs(os.path.join(dist, "assets"))
    for f in os.listdir(os.path.join(OUT, "assets")):
        shutil.copy(os.path.join(OUT, "assets", f), os.path.join(dist, "assets", f))
    for f in os.listdir(OUT):
        if f.endswith(".html"):
            shutil.copy(os.path.join(OUT, f), os.path.join(dist, f))
    with open(os.path.join(dist, "index.html"), encoding="utf-8") as fh:
        t = fh.read()
    head = re.search(r"<head>(.*?)</head>", t, re.S).group(1)
    head = re.sub(r'<meta charset="utf-8">\s*', "", head)
    head = re.sub(r'<meta name="viewport"[^>]*>\s*', "", head)
    body_tag = re.search(r"<body([^>]*)>", t).group(1)
    body = re.search(r"<body[^>]*>(.*)</body>", t, re.S).group(1)
    # the skeleton owns <body>, so the data attributes move onto a script that copies them across
    attrs = dict(re.findall(r'(data-[\w-]+)="([^"]*)"', body_tag))
    js = "".join('document.body.setAttribute("%s","%s");' % (k, v) for k, v in attrs.items())
    with open(os.path.join(dist, "index.html"), "w", encoding="utf-8") as fh:
        fh.write(head.strip() + "\n<script>" + js + "</script>\n" + body.strip() + "\n")
    print("artifact copy written to", dist)


if __name__ == "__main__":
    sys.exit(main())
