/* ABAC code tour - figures and interactive panels.
   Every number drawn here comes from the C# code, the baseline documents or batch v5; where a figure
   re-implements a formula, the formula is the one in the file named next to it. */
(function () {
  "use strict";
  var T = window.Tour, S = T.S, W = T.widgets;

  function el(tag, attrs, parent, html) {
    var e = document.createElement(tag);
    if (attrs) for (var k in attrs) e.setAttribute(k, attrs[k]);
    if (html != null) e.innerHTML = html;
    if (parent) parent.appendChild(e);
    return e;
  }
  function controls(host) { return el("div", { class: "controls" }, host); }
  function slider(host, label, min, max, step, value, fmt, onInput) {
    var lab = el("label", null, host);
    lab.appendChild(document.createTextNode(label + " "));
    var id = "r" + Math.random().toString(36).slice(2, 8);
    var r = el("input", { type: "range", min: min, max: max, step: step, value: value, id: id }, lab);
    var o = el("output", { for: id }, lab);
    function upd() { o.textContent = fmt(parseFloat(r.value)); onInput(parseFloat(r.value)); }
    r.addEventListener("input", upd);
    o.textContent = fmt(value);
    return { input: r, set: function (v) { r.value = v; upd(); }, get: function () { return parseFloat(r.value); } };
  }
  function svgBox(host, w, h, label) {
    var c = el("div", { class: "canvas" }, host);
    var s = S("svg", { viewBox: "0 0 " + w + " " + h, width: w, role: "img", "aria-label": label || "" }, c);
    s.style.width = "100%"; s.style.maxWidth = w + "px"; s.style.height = "auto";
    return s;
  }
  function fx(v, n) { return (Math.round(v * Math.pow(10, n)) / Math.pow(10, n)).toFixed(n); }

  var LANE_GROUP = { A: "--lane-a", A2: "--lane-a", Ax: "--lane-a", B: "--lane-b", Bx: "--lane-b", CB: "--lane-c", CA: "--lane-c" };
  function laneColor(l) { return "var(" + (LANE_GROUP[l] || "--muted") + ")"; }

  // ================================================================ junction geometry
  // All coordinates are ABACSceneBuilder's constants and waypoint tables (x east, z north, metres).
  var G = {
    roadS: -8.12, roadN: 9.88, abacW: -7.52, abacE: 6.48,
    lanes: {
      A:  { dir: "east",  pts: [[-60, 3.0], [60, 3.0]], stop: [-10.5, 3.0], label: [-38, 3.0] },
      A2: { dir: "east",  pts: [[-60, 3.0], [-42.5, 3.0], [-30.5, 6.5], [60, 6.5]], stop: [-10.5, 6.5], label: [-25, 7.6] },
      Ax: { dir: "east",  pts: [[-60, 6.5], [-10.5, 6.5], [-7.5, 7.44], [-4.94, 10.0], [-4.0, 13.5], [-4.0, 40]], stop: [-10.5, 6.5], label: [-38, 6.5] },
      B:  { dir: "west",  pts: [[60, -4.5], [-60, -4.5]], stop: [9.5, -4.5], label: [38, -4.5] },
      Bx: { dir: "west",  pts: [[60, -1.2], [9.5, -1.2], [5.2, -1.2], [1.7, -0.26], [-0.86, 2.3], [-1.8, 5.8], [-1.8, 40]], stop: [9.5, -1.2], label: [38, -1.2] },
      CB: { dir: "south", pts: [[1.5, 40], [1.5, 10.9], [1.5, 2.5], [0.56, -1.0], [-2.0, -3.56], [-5.5, -4.5], [-60, -4.5]], stop: [1.5, 10.9], label: [-0.8, 21] },
      CA: { dir: "south", pts: [[3.8, 40], [3.8, 13.5], [4.74, 10.0], [7.3, 7.44], [10.8, 6.5], [60, 6.5]], stop: null, label: [7.2, 21] }
    },
    lights: { H_A: { pos: [-11.5, 9.5], lanes: ["A", "A2"], lab: [0, -14, "middle"] }, H_B: { pos: [10.5, -8.0], lanes: ["B"], lab: [-4, 24, "end"] },
              H_Bx: { pos: [13.5, -8.0], lanes: ["Bx"], lab: [4, 24, "start"] }, H_CB: { pos: [7.0, 12.0], lanes: ["CB"], lab: [14, 4, "start"] } },
    conflicts: [["A", "Bx"], ["A", "CB"], ["A2", "Bx"], ["A2", "CB"], ["B", "CB"], ["Bx", "CB"]],
    signalised: ["A", "A2", "B", "Bx", "CB"]
  };
  function conflicting(a, b) { return G.conflicts.some(function (p) { return (p[0] === a && p[1] === b) || (p[0] === b && p[1] === a); }); }
  T.G = G; T.conflicting = conflicting;

  // world -> svg
  function mkProj(x0, x1, z0, z1, scale) {
    return { x: function (x) { return (x - x0) * scale; }, y: function (z) { return (z1 - z) * scale; },
             w: (x1 - x0) * scale, h: (z1 - z0) * scale, s: scale };
  }
  function smoothPath(P, pts) {
    var p = pts.map(function (q) { return [P.x(q[0]), P.y(q[1])]; });
    var d = "M" + p[0][0].toFixed(1) + " " + p[0][1].toFixed(1);
    for (var i = 0; i < p.length - 1; i++) {
      var p0 = p[i - 1] || p[i], p1 = p[i], p2 = p[i + 1], p3 = p[i + 2] || p2;
      var c1 = [p1[0] + (p2[0] - p0[0]) / 6, p1[1] + (p2[1] - p0[1]) / 6];
      var c2 = [p2[0] - (p3[0] - p1[0]) / 6, p2[1] - (p3[1] - p1[1]) / 6];
      d += " C" + c1[0].toFixed(1) + " " + c1[1].toFixed(1) + " " + c2[0].toFixed(1) + " " + c2[1].toFixed(1) + " " + p2[0].toFixed(1) + " " + p2[1].toFixed(1);
    }
    return d;
  }

  // Draws the T-junction; returns handles so other figures can colour lanes and lights.
  function drawJunction(svg, opts) {
    opts = opts || {};
    var P = mkProj(opts.x0 || -42, opts.x1 || 42, opts.z0 || -11, opts.z1 || 24, opts.scale || 11.5);
    var g = S("g", null, svg);
    S("rect", { x: 0, y: 0, width: P.w, height: P.h, fill: "var(--grass)" }, g);
    // carriageways
    S("rect", { x: 0, y: P.y(G.roadN), width: P.w, height: (G.roadN - G.roadS) * P.s, fill: "var(--road)" }, g);
    S("rect", { x: P.x(G.abacW), y: 0, width: (G.abacE - G.abacW) * P.s, height: P.y(G.roadN), fill: "var(--road)" }, g);
    // markings: solid centrelines, dashed lane dividers (ABACSceneBuilder.BuildLaneMarkings)
    var mk = { stroke: "var(--road-line)", "stroke-width": 1.6, fill: "none" };
    function hline(z, x0, x1, dash) { S("line", Object.assign({ x1: P.x(x0), x2: P.x(x1), y1: P.y(z), y2: P.y(z), "stroke-dasharray": dash || "" }, mk), g); }
    function vline(x, z0, z1, dash) { S("line", Object.assign({ x1: P.x(x), x2: P.x(x), y1: P.y(z0), y2: P.y(z1), "stroke-dasharray": dash || "" }, mk), g); }
    hline(0.88, -60, -8.2); hline(0.88, 7.2, 60);
    hline(4.75, -60, -8.2, "32 32"); hline(4.75, 7.2, 60, "32 32");
    hline(-2.85, -60, -8.2, "32 32"); hline(-2.85, 7.2, 60, "32 32");
    vline(-0.15, 10.2, 40); vline(-2.9, 10.2, 40, "32 32"); vline(2.65, 10.2, 40, "32 32");
    // ROIs: 100 m back from each stop line (only the part inside the view is drawn)
    var roi = S("g", { class: "j-roi" }, g);
    function roiBox(x0, x1, z0, z1, name) {
      S("rect", { x: P.x(Math.min(x0, x1)), y: P.y(Math.max(z0, z1)), width: Math.abs(x1 - x0) * P.s, height: Math.abs(z1 - z0) * P.s,
                  fill: "var(--amber)", opacity: .16, stroke: "var(--amber)", "stroke-dasharray": "4 3" }, roi);
    }
    roiBox(-60, -10.5, 1.5, 4.5, "ROI_A"); roiBox(-60, -10.5, 5.0, 8.0, "ROI_Ax");
    roiBox(9.5, 60, -6.0, -3.0, "ROI_B"); roiBox(9.5, 60, -2.7, 0.3, "ROI_Bx");
    roiBox(0, 3, 10.9, 40, "ROI_CB"); roiBox(2.3, 5.3, 10.9, 40, "ROI_CA");
    roi.style.display = opts.roi === false ? "none" : "";
    // stop lines
    [[-10.5, 3.0, "x"], [-10.5, 6.5, "x"], [9.5, -4.5, "x"], [9.5, -1.2, "x"], [1.5, 10.9, "z"]].forEach(function (s) {
      if (s[2] === "x") S("line", { x1: P.x(s[0]), x2: P.x(s[0]), y1: P.y(s[1] + 1.5), y2: P.y(s[1] - 1.5), stroke: "var(--ink)", "stroke-width": 3 }, g);
      else S("line", { x1: P.x(s[0] - 1.5), x2: P.x(s[0] + 1.5), y1: P.y(s[1]), y2: P.y(s[1]), stroke: "var(--ink)", "stroke-width": 3 }, g);
    });
    // movements
    var lanes = {}, lg = S("g", null, g);
    Object.keys(G.lanes).forEach(function (name) {
      var L = G.lanes[name];
      var p = S("path", { d: smoothPath(P, L.pts), fill: "none", stroke: laneColor(name), "stroke-width": 3.2, "stroke-linecap": "round",
                          "marker-end": "url(#arrow-" + (opts.id || "j") + ")", opacity: .9 }, lg);
      if (name === "A2" || name === "CA" || name === "Ax") p.setAttribute("stroke-dasharray", name === "A2" ? "7 5" : "");
      var t = S("text", { x: P.x(L.label[0]), y: P.y(L.label[1]) - 6, "text-anchor": "middle", "font-size": 13, "font-weight": 700, text: name }, lg);
      t.style.fill = laneColor(name);
      lanes[name] = { path: p, label: t };
    });
    var defs = S("defs", null, svg);
    var m = S("marker", { id: "arrow-" + (opts.id || "j"), viewBox: "0 0 10 10", refX: 8, refY: 5, markerWidth: 5, markerHeight: 5, orient: "auto-start-reverse" }, defs);
    S("path", { d: "M0 0 L10 5 L0 10 z", fill: "var(--ink2)" }, m);
    // signal heads
    var lights = {};
    Object.keys(G.lights).forEach(function (h) {
      var L = G.lights[h], x = P.x(L.pos[0]), y = P.y(L.pos[1]);
      var gg = S("g", null, g);
      S("rect", { x: x - 9, y: y - 9, width: 18, height: 18, rx: 4, fill: "var(--ink)" }, gg);
      var c = S("circle", { cx: x, cy: y, r: 5.5, fill: "var(--stop)" }, gg);
      var tx = S("text", { x: x + L.lab[0], y: y + L.lab[1], "text-anchor": L.lab[2], "font-size": 11, "font-weight": 700, text: h }, gg);
      lights[h] = { dot: c, group: gg, text: tx };
    });
    return { P: P, lanes: lanes, lights: lights, roi: roi, root: g };
  }
  T.drawJunction = drawJunction;
  function lightColor(st) { return st === "G" ? "var(--go)" : st === "Y" ? "var(--amber)" : "var(--stop)"; }
  T.lightColor = lightColor;

  // ---------------- hub: junction with conflict explorer ----------------
  W.junction = function (host) {
    var svg = svgBox(host, 966, 403, "Top-down plan of the ABAC T-junction");
    var J = drawJunction(svg, { id: "hub" });
    var c = controls(host), info = el("div", { class: "readout" }, host);
    var NOTE = {
      A: "Eastbound through, inner lane. Head H_A. Conflicts with Bx and CB.",
      A2: "Eastbound through traffic that uses the outer lane shared with Ax (5.4% of through traffic). Same TrafficLight object as A and same ROI as Ax, so it is always green with A and counted with Ax.",
      Ax: "Left turn into ABAC. No phase of its own (isSignalized = false). It reads the Bx light inverted: Bx green or yellow means Ax stops at its line. It also queues behind A2 in the shared lane.",
      B: "Westbound through. Head H_B. Conflicts only with CB, so it can run with A or with Bx.",
      Bx: "Right turn into ABAC across the A stream. Head H_Bx. The bottleneck: v/c 1.51 under the fixed plan.",
      CB: "Leaving ABAC to the west: crosses lane A, merges into B. Head H_CB. Conflicts with every other signalised lane.",
      CA: "Leaving ABAC to the east, merging into the outer eastbound lane. Free: no signal, crosses nobody."
    };
    var cur = null;
    function show(name) {
      cur = cur === name ? null : name;
      Object.keys(J.lanes).forEach(function (n) {
        var L = J.lanes[n], col = laneColor(n), op = 1, w = 3.2;
        if (cur) {
          if (n === cur) { w = 5.5; }
          else if (G.signalised.indexOf(cur) >= 0 && G.signalised.indexOf(n) >= 0) { col = conflicting(cur, n) ? "var(--stop)" : "var(--go)"; w = 4; }
          else op = .25;
        }
        L.path.setAttribute("stroke", col); L.path.setAttribute("stroke-width", w); L.path.setAttribute("opacity", op);
      });
      btns.forEach(function (b) { b.setAttribute("aria-pressed", b.dataset.l === cur ? "true" : "false"); });
      if (!cur) { info.innerHTML = "Pick a lane. Signalised lanes it may run with turn <b>green</b>; lanes it must never run with turn <span style=\"color:var(--stop)\">red</span>."; return; }
      var ok = [], no = [];
      G.signalised.forEach(function (n) { if (n !== cur && G.signalised.indexOf(cur) >= 0) (conflicting(cur, n) ? no : ok).push(n); });
      info.innerHTML = "<b>" + cur + "</b>  " + NOTE[cur] + (G.signalised.indexOf(cur) >= 0 ? "\nMay run with: " + (ok.join(", ") || "none") + "    Never with: " + (no.join(", ") || "none") : "");
    }
    var btns = ["A", "A2", "Ax", "B", "Bx", "CB", "CA"].map(function (n) {
      var b = el("button", { type: "button", "data-l": n, "aria-pressed": "false" }, c, n);
      b.style.borderColor = laneColor(n);
      b.addEventListener("click", function () { show(n); });
      return b;
    });
    var rb = el("button", { type: "button", "aria-pressed": "true" }, c, "Detection zones");
    rb.addEventListener("click", function () { var on = J.roi.style.display === "none"; J.roi.style.display = on ? "" : "none"; rb.setAttribute("aria-pressed", on); });
    show(null);
  };

  // ================================================================ hub: file map
  var NODES = {
    CarSpawner: [20, 230, "traffic"], CarAgent: [20, 300, "traffic"], TestCaseLibrary: [20, 370, "traffic"], CustomTestCase: [20, 440, "traffic"],
    ROIZone: [235, 265, "sensing"], RoiHighlight: [235, 335, "sensing"],
    AdaptiveController: [450, 265, "control"], FixedController: [450, 335, "control"],
    IntersectionConfig: [665, 265, "junction"], TrafficLight: [665, 335, "junction"],
    SimulationControlPanel: [880, 230, "ui"], BatchRunPanel: [880, 300, "ui"], CameraDirector: [880, 370, "ui"], UITheme: [880, 440, "ui"],
    DataLogger: [235, 95, "experiment"], ScenarioManager: [450, 95, "experiment"],
    ABACSceneBuilder: [235, 580, "editor"], CarPrefabBuilder: [450, 580, "editor"], SceneParameterAudit: [665, 580, "editor"]
  };
  var LAYER_NAME = { junction: "Junction model", traffic: "Traffic", sensing: "Sensing", control: "Control", experiment: "Experiment", ui: "Runtime UI", editor: "Editor tools" };
  W.filemap = function (host) {
    var edges = JSON.parse(host.dataset.edges), pages = JSON.parse(host.dataset.pages), lines = JSON.parse(host.dataset.lines);
    var svg = svgBox(host, 1100, 650, "Map of the 19 files and the calls between them");
    var NW = 200, NH = 50;
    var defs = S("defs", null, svg);
    [["fm-arrow", "var(--muted)"], ["fm-arrow-on", "var(--accent)"]].forEach(function (a) {
      var mk = S("marker", { id: a[0], viewBox: "0 0 10 10", refX: 9, refY: 5, markerWidth: 6, markerHeight: 6, orient: "auto" }, defs);
      S("path", { d: "M0 0 L10 5 L0 10 z", fill: a[1] }, mk);
    });
    // layer titles
    var titles = { traffic: [20, 215], sensing: [235, 250], control: [450, 250], junction: [665, 250], ui: [880, 215], experiment: [235, 80], editor: [235, 565] };
    Object.keys(titles).forEach(function (k) {
      var t = S("text", { x: titles[k][0], y: titles[k][1], class: "map-layer-title", text: LAYER_NAME[k] }, svg);
      t.style.fill = "var(--l-" + k + ")";
    });
    function rectPoint(n, tx, ty) {
      var x = NODES[n][0] + NW / 2, y = NODES[n][1] + NH / 2, dx = tx - x, dy = ty - y;
      var sx = Math.abs(dx) > 1e-6 ? (NW / 2) / Math.abs(dx) : Infinity, sy = Math.abs(dy) > 1e-6 ? (NH / 2) / Math.abs(dy) : Infinity;
      var s = Math.min(sx, sy);
      return [x + dx * s, y + dy * s];
    }
    var eg = S("g", null, svg), ng = S("g", null, svg), lg = S("g", null, svg);
    var E = edges.map(function (e, i) {
      var a = NODES[e[0]], b = NODES[e[1]];
      var ac = [a[0] + NW / 2, a[1] + NH / 2], bc = [b[0] + NW / 2, b[1] + NH / 2];
      var p1 = rectPoint(e[0], bc[0], bc[1]), p2 = rectPoint(e[1], ac[0], ac[1]);
      var mx = (p1[0] + p2[0]) / 2, my = (p1[1] + p2[1]) / 2, dx = p2[0] - p1[0], dy = p2[1] - p1[1], len = Math.hypot(dx, dy) || 1;
      var bend = 26;   // always to the left of travel, so A->B and B->A separate
      var cx = mx - dy / len * bend, cy = my + dx / len * bend;
      var lx = .25 * p1[0] + .5 * cx + .25 * p2[0], ly = .25 * p1[1] + .5 * cy + .25 * p2[1];
      var path = S("path", { d: "M" + p1[0] + " " + p1[1] + " Q" + cx + " " + cy + " " + p2[0] + " " + p2[1], class: "map-edge", "marker-end": "url(#fm-arrow)" }, eg);
      var lab = S("text", { x: lx, y: ly + 3, "text-anchor": "middle", class: "map-label", text: e[2] }, lg);
      return { from: e[0], to: e[1], kind: e[3], path: path, label: lab };
    });
    var nodeEls = {};
    Object.keys(NODES).forEach(function (n) {
      var N = NODES[n];
      var a = S("a", { href: pages[n], class: "map-node", tabindex: 0 }, ng);
      S("title", { text: n + " - open its page" }, a);
      var r = S("rect", { x: N[0], y: N[1], width: NW, height: NH, rx: 7 }, a);
      S("rect", { x: N[0], y: N[1], width: 5, height: NH, rx: 2, fill: "var(--l-" + N[2] + ")", stroke: "none" }, a);
      S("text", { x: N[0] + 14, y: N[1] + 22, text: n }, a);
      S("text", { x: N[0] + 14, y: N[1] + 38, class: "sub", text: lines[n] + " lines" }, a);
      a.addEventListener("mouseenter", function () { focus(n); });
      a.addEventListener("focus", function () { focus(n); });
      a.addEventListener("mouseleave", function () { focus(null); });
      a.addEventListener("blur", function () { focus(null); });
      nodeEls[n] = a;
    });
    var kinds = { run: true, ui: false, edit: false };
    function visible(e) { return kinds[e.kind]; }
    function focus(n) {
      var linked = {};
      E.forEach(function (e) {
        var vis = visible(e) || (n && (e.from === n || e.to === n));
        var on = n && (e.from === n || e.to === n);
        e.path.style.display = vis ? "" : "none";
        e.path.setAttribute("class", "map-edge" + (on ? " on" : n ? " dim" : ""));
        e.path.setAttribute("marker-end", on ? "url(#fm-arrow-on)" : "url(#fm-arrow)");
        e.label.setAttribute("class", "map-label" + (on ? " on" : ""));
        if (on) { linked[e.from] = 1; linked[e.to] = 1; }
      });
      Object.keys(nodeEls).forEach(function (k) {
        nodeEls[k].setAttribute("class", "map-node" + (n === k ? " focus" : (n && !linked[k] ? " dim" : "")));
      });
    }
    var c = controls(host);
    el("span", { class: "muted" }, c, "Show calls:");
    [["run", "Simulation path"], ["ui", "Display and control"], ["edit", "Editor tools"]].forEach(function (k) {
      var b = el("button", { type: "button", "aria-pressed": kinds[k[0]] ? "true" : "false" }, c, k[1]);
      b.addEventListener("click", function () { kinds[k[0]] = !kinds[k[0]]; b.setAttribute("aria-pressed", kinds[k[0]]); focus(null); });
    });
    focus(null);
  };

  // ================================================================ hub: the control loop, stage by stage
  W.loop = function (host) {
    var svg = svgBox(host, 1000, 330, "One car through the control loop");
    var STAGES = [
      ["CarSpawner", "Spawn", "PoissonSpawnLoop draws an exponential gap from this lane's own seeded random stream, then PlaceCar instantiates the car and sets its lane, stop line, path and light, and RegisterCar adds it to the light's list."],
      ["CarAgent", "Drive", "Every frame is cut into steps of at most 0.02 s. Each step reads the light, follows the car ahead (gap = minGap + desiredTimeHeadway x speed) and brakes for the red on a closed-loop curve."],
      ["ROIZone", "Detect", "The car's collider enters the zone's trigger: OnTriggerEnter stores it with the time it arrived, and RoiHighlight draws the green box."],
      ["AdaptiveController", "Snapshot", "Every snapshotInterval (1 s) Snapshot() asks each head's zones for GetSampledCount(). 0 -> 1 is a rising edge, so queueStart = now. That is all the controller sees: a count, no IDs."],
      ["AdaptiveController", "Decide", "ServeOnePhase answers four questions: Q1 is anyone starving, Q2 who queued first, Q3 who can go with them, Q4 how long: G = clamp(2.95 + D x 2.31, 8, 45)."],
      ["TrafficLight", "Signal", "SetState(Green) repaints the lamp and calls OnLightStateChanged on every registered car. The car also polls currentState each step, so a lost registration cannot strand it."],
      ["CarAgent", "Go", "The first car waits its reactionDelay; each follower starts its own timer only when the car ahead moves, so the queue leaves as a wave, 2.28 s apart."],
      ["DataLogger", "Log", "Leaving the zone, OnTriggerExit hands LogVehicle the car's own lane and its time inside the zone. That wait is the number every result in the project is built from."]
    ];
    var P = mkProj(-60, 20, -6, 6, 10);
    var g = S("g", { transform: "translate(80,20)" }, svg);
    S("rect", { x: 0, y: 0, width: P.w, height: P.h, fill: "var(--road)", rx: 6 }, g);
    S("rect", { x: P.x(-50), y: P.y(1.5), width: 40 * P.s, height: 3 * P.s, fill: "var(--amber)", opacity: .18, stroke: "var(--amber)", "stroke-dasharray": "4 3" }, g);
    S("text", { x: P.x(-30), y: P.y(1.5) - 5, "text-anchor": "middle", "font-size": 11, text: "detection zone (ROI)" }, g);
    S("line", { x1: P.x(-10), x2: P.x(-10), y1: P.y(2), y2: P.y(-2), stroke: "var(--ink)", "stroke-width": 3 }, g);
    S("text", { x: P.x(-10), y: P.y(-2) + 14, "text-anchor": "middle", "font-size": 11, text: "stop line" }, g);
    S("text", { x: P.x(-58), y: P.y(-2) + 14, "font-size": 11, text: "spawn point" }, g);
    var lamp = S("circle", { cx: P.x(-8), cy: P.y(4.3), r: 7, fill: "var(--stop)" }, g);
    S("rect", { x: P.x(-8) - 10, y: P.y(4.3) - 10, width: 20, height: 20, rx: 4, fill: "none", stroke: "var(--ink)" }, g);
    var car = S("rect", { x: 0, y: P.y(1) , width: 4 * P.s, height: 2 * P.s, rx: 3, fill: "var(--lane-a)" }, g);
    var box = S("rect", { x: 0, y: P.y(1.3) , width: 4.6 * P.s, height: 2.6 * P.s, rx: 2, fill: "none", stroke: "var(--go)", "stroke-width": 2 }, g);
    var cam = S("text", { x: P.x(-30), y: P.y(-3.3), "text-anchor": "middle", "font-size": 12, "font-family": "var(--f-mono)", text: "" }, g);
    // pipeline row
    var row = S("g", { transform: "translate(20,180)" }, svg), cells = [];
    STAGES.forEach(function (s, i) {
      var x = i * 121;
      var r = S("rect", { x: x, y: 0, width: 113, height: 58, rx: 7, fill: "var(--paper)", stroke: "var(--rule)" }, row);
      S("text", { x: x + 56, y: 22, "text-anchor": "middle", "font-size": 13, "font-weight": 700, text: (i + 1) + " " + s[1] }, row);
      S("text", { x: x + 56, y: 41, "text-anchor": "middle", "font-size": 9.5, "font-family": "var(--f-mono)", text: s[0] }, row);
      if (i < STAGES.length - 1) S("path", { d: "M" + (x + 114) + " 29 l6 0", stroke: "var(--muted)" }, row);
      cells.push(r);
    });
    var cap = el("p", { class: "readout" }, host);
    var stageLen = 2;
    function carX(t) {
      // spawn at -58, decelerate to stop at -14 by t=6, wait till 10.5 (green at 10), then leave
      if (t < 2) return -58 + 4 * t;
      if (t < 6) { var u = (t - 2) / 4; return -50 + 36 * (1 - Math.pow(1 - u, 2)); }
      if (t < 12.8) return -14;
      var v = t - 12.8; return Math.min(30, -14 + 3 * v * v);
    }
    function draw(t) {
      var i = Math.min(STAGES.length - 1, Math.floor(t / stageLen));
      cells.forEach(function (r, k) { r.setAttribute("stroke", k === i ? "var(--accent)" : "var(--rule)"); r.setAttribute("stroke-width", k === i ? 3 : 1); r.setAttribute("fill", k === i ? "var(--accent-tint)" : "var(--paper)"); });
      var x = carX(t);
      car.setAttribute("x", P.x(x - 2)); box.setAttribute("x", P.x(x - 2.3));
      var inZone = x > -50 && x < -10 + 2 && t > 4 && t < 15;
      box.style.display = inZone ? "" : "none";
      var green = t >= 10;
      lamp.setAttribute("fill", green ? "var(--go)" : "var(--stop)");
      var snaps = t >= 6 ? Math.floor(t - 6) + 1 : 0;
      cam.textContent = t >= 6 && t < 10 ? "snapshot #" + snaps + "   count = 1   queueStart = 6.0 s" : t >= 10 && t < 12 ? "Q2 leader -> this head   G = clamp(2.95 + 1 x 2.31) = 8 s" : t >= 14 ? "LogVehicle(lane, wait in zone)" : "";
      cap.innerHTML = "<b>" + (i + 1) + ". " + STAGES[i][1] + "</b> · <code>" + STAGES[i][0] + "</code>\n" + STAGES[i][2];
    }
    new T.Player(controls(host), { duration: STAGES.length * stageLen - 0.01, step: stageLen, draw: draw, loop: true,
      format: function (t) { return "stage " + (Math.min(STAGES.length, Math.floor(t / stageLen) + 1)) + " / " + STAGES.length; } });
    host.appendChild(cap);
  };

  // ================================================================ AdaptiveController
  // Port of the decision rules in AdaptiveController.cs (Q1 FindStarvingHead, Q2 ChooseLeaderByQueueStart,
  // Q3 BuildCompatibleGroup + AddPhysicallyLinkedSignals, Q4 CalculateGreenDuration, CountTieBreaks).
  var HEADS = [
    { name: "H_A", priority: 0, signals: ["A", "A2"] }, { name: "H_B", priority: 1, signals: ["B"] },
    { name: "H_Bx", priority: 2, signals: ["Bx"] }, { name: "H_CB", priority: 3, signals: ["CB"] }
  ];
  var SIGNAL_ROI = { A: "ROI_A", A2: "ROI_Ax", B: "ROI_B", Bx: "ROI_Bx", CB: "ROI_CB" };
  var SIGNAL_ORDER = ["A", "A2", "B", "Bx", "CB"];   // IntersectionConfig signal indices
  function compareHeads(a, b) { return a.queueStart !== b.queueStart ? (a.queueStart < b.queueStart ? -1 : 1) : a.priority - b.priority; }
  function sodaDecide(state) {
    // state: { now, roi: {ROI_A..}, qs: {H_A..} (time or null), maxWait, sel, minJoin, l1, h, gMin, gMax }
    var heads = HEADS.map(function (h) {
      var c = 0; h.signals.forEach(function (s) { if (h.name !== "H_A" || s === "A") c += state.roi[SIGNAL_ROI[s]] || 0; });
      if (h.name === "H_A") c = (state.roi.ROI_A || 0) + (state.roi.ROI_Ax || 0);
      var qs = c > 0 && state.qs[h.name] != null ? state.qs[h.name] : Infinity;
      return { name: h.name, priority: h.priority, signals: h.signals, lastCount: c, queueStart: qs };
    });
    function wait(h) { return isFinite(h.queueStart) ? state.now - h.queueStart : 0; }
    var out = { lines: [] };
    var limit = state.maxWait * state.sel, leader = null;
    heads.forEach(function (h) { if (h.lastCount > 0 && wait(h) >= limit && (!leader || compareHeads(h, leader) < 0)) leader = h; });
    if (leader) { out.q1 = "OVERRIDE -> " + leader.name + " (waited " + fx(wait(leader), 1) + " s >= " + fx(limit, 2) + ")"; out.q2 = "skipped: starvation takes priority"; out.reason = "starvation"; }
    else {
      out.q1 = "none starving (threshold " + fx(limit, 2) + " s)";
      heads.forEach(function (h) { if (h.lastCount > 0 && (!leader || compareHeads(h, leader) < 0)) leader = h; });
      if (!leader) { out.q2 = "no head has demand: stay ALL-RED"; return out; }
      var tied = heads.filter(function (h) { return h.lastCount > 0 && h !== leader && h.queueStart === leader.queueStart; });
      out.q2 = "LEADER -> " + leader.name + " (oldest queue, " + fx(wait(leader), 1) + " s)" + (tied.length ? "  tie with " + tied.map(function (h) { return h.name; }).join(", ") + ", settled by A -> B -> Bx -> CB" : "");
    }
    var group = [leader.signals[0]], notes = [];
    function compatible(sig) { return group.every(function (m) { return !conflicting(sig, m); }); }
    var cands = heads.filter(function (h) { return h !== leader && h.lastCount >= state.minJoin; }).sort(compareHeads);
    cands.forEach(function (h) {
      var joined = false;
      h.signals.forEach(function (sg) { if (compatible(sg)) { group.push(sg); joined = true; } });
      notes.push((joined ? "+ " : "x ") + h.name + (joined ? " joins" : " conflicts with the group"));
    });
    if (group.indexOf("A") >= 0 && group.indexOf("A2") < 0) { group.push("A2"); notes.push("+ A2 forced: same light as A"); }
    group.sort(function (a, b) { return SIGNAL_ORDER.indexOf(a) - SIGNAL_ORDER.indexOf(b); });
    out.q3 = "GROUP -> {" + group.join(", ") + "}   " + notes.join("; ");
    var g = state.gMin, crit = "(floor)", parts = [];
    group.forEach(function (sg) {
      var d = state.roi[SIGNAL_ROI[sg]] || 0, need = state.l1 + d * state.h;
      parts.push(sg + "=" + d + (d > 0 ? " -> " + fx(need, 2) : ""));
      if (d > 0 && need > g) { g = need; crit = sg; }
    });
    var G0 = Math.min(Math.max(g, state.gMin), state.gMax);
    out.q4 = "G = " + fx(G0, 2) + " s  (" + parts.join(", ") + ")" + (G0 >= state.gMax - .01 ? "  clamped at gMax" : G0 <= state.gMin + .01 ? "  clamped at gMin" : "  critical lane " + crit);
    // CountTieBreaks
    var anyTie = heads.some(function (a, i) { return a.lastCount > 0 && heads.some(function (b, j) { return j > i && b.lastCount > 0 && a.queueStart === b.queueStart; }); });
    var bx = heads[2], excl = false;
    if (anyTie && bx.lastCount > 0 && group.indexOf("Bx") < 0)
      excl = heads.some(function (h) { return h !== bx && h.lastCount > 0 && h.signals.some(function (sg) { return group.indexOf(sg) >= 0; }) && h.queueStart === bx.queueStart; });
    out.ties = (anyTie ? "TieBreaksApplied +1" : "no tie") + (excl ? ", TieBreaksExcludingBx +1" : "");
    out.group = group; out.heads = heads; out.G = G0; out.leader = leader;
    var dark = {}; heads.forEach(function (h) { if (h.lastCount === 0 && !h.signals.some(function (sg) { return group.indexOf(sg) >= 0; })) dark[h.name] = 1; });
    out.skipped = Object.keys(dark);
    return out;
  }
  T.sodaDecide = sodaDecide;

  W.sodaPlayground = function (host) {
    var C = { l1: 2.95, h: 2.31, gMin: 8, gMax: 45, maxWait: 45, sel: 0.95, minJoin: 1 };
    var PRESETS = {
      "S2: two heads": { now: 4, roi: { ROI_A: 1, ROI_Ax: 0, ROI_B: 1, ROI_Bx: 0, ROI_CB: 0 }, qs: { H_A: 2, H_B: 4, H_Bx: null, H_CB: null } },
      "S4 phase 1": { now: 0, roi: { ROI_A: 12, ROI_Ax: 0, ROI_B: 5, ROI_Bx: 9, ROI_CB: 7 }, qs: { H_A: 0, H_B: 0, H_Bx: 0, H_CB: 0 } },
      "S4 phase 2": { now: 30.67, roi: { ROI_A: 10, ROI_Ax: 0, ROI_B: 4, ROI_Bx: 9, ROI_CB: 7 }, qs: { H_A: 30.67, H_B: 30.67, H_Bx: 0, H_CB: 0 } },
      "S4 phase 3": { now: 55.67, roi: { ROI_A: 10, ROI_Ax: 0, ROI_B: 3, ROI_Bx: 8, ROI_CB: 7 }, qs: { H_A: 30.67, H_B: 55.67, H_Bx: 55.67, H_CB: 0 } }
    };
    var form = el("div", { class: "cols" }, host), inputs = {};
    var rows = [["H_A", "ROI_A", "ROI_Ax"], ["H_B", "ROI_B"], ["H_Bx", "ROI_Bx"], ["H_CB", "ROI_CB"]];
    var tbl = el("div", { class: "tw" }, form);
    var t = el("table", { class: "plain" }, tbl);
    t.innerHTML = "<thead><tr><th>Head</th><th>Count in its zone(s)</th><th>queueStart (s)</th></tr></thead>";
    var tb = el("tbody", null, t);
    rows.forEach(function (r) {
      var tr = el("tr", null, tb);
      el("td", { class: "vn" }, tr, r[0]);
      var td = el("td", null, tr);
      r.slice(1).forEach(function (roi) {
        var lab = el("label", { style: "margin-right:.6rem;font-family:var(--f-mono);font-size:.78rem" }, td, roi + " ");
        inputs[roi] = el("input", { type: "number", min: 0, max: 40, step: 1, value: 0, style: "width:3.6rem" }, lab);
      });
      var td2 = el("td", null, tr);
      inputs["qs_" + r[0]] = el("input", { type: "number", step: 0.01, value: 0, style: "width:5.5rem" }, td2);
    });
    var side = el("div", null, form);
    var lab = el("label", { style: "display:block;font-family:var(--f-ui);font-size:.85rem;margin-bottom:.4rem" }, side, "Decision time (now, s) ");
    inputs.now = el("input", { type: "number", step: 0.01, value: 4, style: "width:6rem" }, lab);
    var pre = el("div", { class: "controls" }, side);
    el("span", { class: "muted" }, pre, "Load a documented case:");
    Object.keys(PRESETS).forEach(function (k) {
      var b = el("button", { type: "button" }, pre, k);
      b.addEventListener("click", function () { load(PRESETS[k]); });
    });
    el("p", { class: "small muted" }, side, "A head with count 0 has no queueStart (it is infinite in the code), whatever the box says. Constants are the code's: 2.95 / 2.31 / 8 / 45, maxWaitTime 45 × 0.95.");
    var out = el("div", { class: "readout" }, host);
    function load(p) {
      inputs.now.value = p.now;
      Object.keys(p.roi).forEach(function (k) { inputs[k].value = p.roi[k]; });
      Object.keys(p.qs).forEach(function (k) { inputs["qs_" + k].value = p.qs[k] == null ? "" : p.qs[k]; });
      run();
    }
    function run() {
      var st = Object.assign({}, C, { now: parseFloat(inputs.now.value) || 0, roi: {}, qs: {} });
      ["ROI_A", "ROI_Ax", "ROI_B", "ROI_Bx", "ROI_CB"].forEach(function (k) { st.roi[k] = Math.max(0, parseInt(inputs[k].value, 10) || 0); });
      HEADS.forEach(function (h) { var v = inputs["qs_" + h.name].value; st.qs[h.name] = v === "" ? null : parseFloat(v); });
      var r = sodaDecide(st);
      var q = r.heads ? r.heads.map(function (h) { return h.name + " " + h.lastCount + (h.lastCount === 1 ? " car" : " cars") + (isFinite(h.queueStart) ? ", waited " + fx(st.now - h.queueStart, 2) + " s" : ", empty"); }).join("   ") : "";
      out.innerHTML = (q ? "QUEUE  " + q + "\n" : "") + "<b>Q1</b> " + r.q1 + "\n<b>Q2</b> " + r.q2 + (r.q3 ? "\n<b>Q3</b> " + r.q3 + "\n<b>Q4</b> " + r.q4 + "\n" + r.ties + (r.skipped.length ? "    skipped (count 0): " + r.skipped.join(", ") : "") : "");
    }
    Object.keys(inputs).forEach(function (k) { inputs[k].addEventListener("input", run); });
    load(PRESETS["S2: two heads"]);
  };

  // ---------------- Q4 green time ----------------
  W.gformula = function (host) {
    var st = { D: 4, l1: 2.95, h: 2.31, gMin: 8, gMax: 45 };
    var svg = svgBox(host, 760, 330, "Green time G against queued vehicles D");
    var L = 60, R = 740, Tp = 20, B = 290, Dmax = 25, Gmax = 70;
    var sx = function (d) { return L + (R - L) * d / Dmax; }, sy = function (g) { return B - (B - Tp) * g / Gmax; };
    for (var g = 0; g <= Gmax; g += 10) { S("line", { x1: L, x2: R, y1: sy(g), y2: sy(g), stroke: "var(--rule)" }, svg); S("text", { x: L - 8, y: sy(g) + 4, "text-anchor": "end", "font-size": 11, text: g + " s" }, svg).style.fill = "var(--muted)"; }
    for (var d = 0; d <= Dmax; d += 5) S("text", { x: sx(d), y: B + 18, "text-anchor": "middle", "font-size": 11, text: d }, svg).style.fill = "var(--muted)";
    S("text", { x: (L + R) / 2, y: B + 36, "text-anchor": "middle", "font-size": 11, text: "D = vehicles counted in the busiest lane of the group at green-open" }, svg).style.fill = "var(--muted)";
    var raw = S("path", { fill: "none", stroke: "var(--muted)", "stroke-dasharray": "4 4", "stroke-width": 1.5 }, svg);
    var line = S("path", { fill: "none", stroke: "var(--accent)", "stroke-width": 3 }, svg);
    var codeLine = S("path", { fill: "none", stroke: "var(--ink2)", "stroke-width": 1, opacity: .5 }, svg);
    var dot = S("circle", { r: 6, fill: "var(--accent)" }, svg);
    var lab = S("text", { "font-size": 12, "font-weight": 700 }, svg);
    var c = controls(host), out = el("div", { class: "readout" }, host);
    function path(fn) { var p = ""; for (var d = 0; d <= Dmax; d += 0.25) p += (p ? " L" : "M") + sx(d).toFixed(1) + " " + sy(Math.min(Gmax, fn(d))).toFixed(1); return p; }
    function Gof(d, s) { var need = d > 0 ? s.l1 + d * s.h : 0; return Math.min(Math.max(Math.max(s.gMin, need), s.gMin), s.gMax); }
    function draw() {
      raw.setAttribute("d", path(function (d) { return st.l1 + d * st.h; }));
      line.setAttribute("d", path(function (d) { return Gof(d, st); }));
      codeLine.setAttribute("d", path(function (d) { return Gof(d, { l1: 2.95, h: 2.31, gMin: 8, gMax: 45 }); }));
      var G0 = Gof(st.D, st);
      dot.setAttribute("cx", sx(st.D)); dot.setAttribute("cy", sy(G0));
      lab.setAttribute("x", sx(st.D) + 10); lab.setAttribute("y", sy(G0) - 10); lab.textContent = "G = " + fx(G0, 2) + " s";
      var cap = (st.gMax - st.l1) / st.h;
      out.innerHTML = "need = " + fx(st.l1, 2) + " + " + st.D + " × " + fx(st.h, 2) + " = " + fx(st.l1 + st.D * st.h, 2) + " s   ->   <b>G = clamp(need, " + st.gMin + ", " + st.gMax + ") = " + fx(G0, 2) + " s</b>\n" +
        "Above D = " + fx(cap, 1) + " every extra car gets no extra green (gMax). Below D = " + Math.max(0, Math.ceil((st.gMin - st.l1) / st.h) - 1) + " the floor gMin decides.\n" +
        "Thin grey line: the code's values (2.95 / 2.31 / 8 / 45). Dashed: the formula before clamping.";
    }
    var sD = slider(c, "D", 0, 25, 1, st.D, function (v) { return v + " cars"; }, function (v) { st.D = v; draw(); });
    var sL = slider(c, "startupLostTime", 1, 4, 0.05, st.l1, function (v) { return fx(v, 2) + " s"; }, function (v) { st.l1 = v; draw(); });
    var sH = slider(c, "saturationHeadway", 1.5, 3, 0.01, st.h, function (v) { return fx(v, 2) + " s"; }, function (v) { st.h = v; draw(); });
    var sMin = slider(c, "gMin", 4, 15, 1, st.gMin, function (v) { return v + " s"; }, function (v) { st.gMin = v; draw(); });
    var sMax = slider(c, "gMax", 25, 70, 1, st.gMax, function (v) { return v + " s"; }, function (v) { st.gMax = v; draw(); });
    var b1 = el("button", { type: "button" }, c, "Code values");
    b1.addEventListener("click", function () { sL.set(2.95); sH.set(2.31); sMin.set(8); sMax.set(45); });
    var b2 = el("button", { type: "button" }, c, "HCM textbook 2.0 / 2.0");
    b2.addEventListener("click", function () { sL.set(2.0); sH.set(2.0); });
    draw();
  };

  // ---------------- gap-out ----------------
  W.gapout = function (host) {
    var readings = [3, 2, 1, 0, 2, 1, 0, 0, 1, 0, 0, 0];
    var st = { n: 2, dt: 1 };
    var wrap = el("div", { class: "canvas" }, host), cells = [];
    var row = el("div", { style: "display:grid;grid-template-columns:repeat(12,minmax(2.6rem,1fr));gap:.35rem;min-width:36rem" }, wrap);
    readings.forEach(function (v, i) {
      var b = el("button", { type: "button", title: "Click to change this reading", style: "font-family:var(--f-mono);font-size:1rem;padding:.5rem 0" }, row);
      b.addEventListener("click", function () { readings[i] = (readings[i] + 1) % 4; draw(); });
      cells.push(b);
    });
    var meta = el("div", { style: "display:grid;grid-template-columns:repeat(12,minmax(2.6rem,1fr));gap:.35rem;min-width:36rem;margin-top:.3rem;font-family:var(--f-mono);font-size:.72rem;color:var(--muted);text-align:center" }, wrap);
    var metas = readings.map(function () { return el("div", null, meta); });
    var c = controls(host), out = el("div", { class: "readout" }, host);
    slider(c, "gapOutSnapshots", 1, 4, 1, st.n, function (v) { return v + ""; }, function (v) { st.n = v; draw(); });
    slider(c, "snapshotInterval", 0.5, 2, 0.5, st.dt, function (v) { return v + " s"; }, function (v) { st.dt = v; draw(); });
    var p1 = el("button", { type: "button" }, c, "Test: 3, 0, 2"), p2 = el("button", { type: "button" }, c, "Test: 3, 0, 0");
    p1.addEventListener("click", function () { readings = [3, 0, 2, 2, 1, 1, 1, 1, 1, 1, 1, 1]; draw(); });
    p2.addEventListener("click", function () { readings = [3, 0, 0, 1, 1, 1, 1, 1, 1, 1, 1, 1]; draw(); });
    function draw() {
      var streak = 0, fired = -1, lastNZ = -1;
      readings.forEach(function (v, i) {
        if (fired < 0) { streak = v === 0 ? streak + 1 : 0; if (v > 0) lastNZ = i; if (streak >= st.n) fired = i; }
        cells[i].textContent = v;
        cells[i].style.background = fired >= 0 && i > fired ? "var(--sunk)" : i === fired ? "var(--amber)" : v === 0 ? "var(--panel)" : "var(--paper)";
        cells[i].style.opacity = fired >= 0 && i > fired ? .35 : 1;
        metas[i].textContent = "t=" + (i * st.dt).toFixed(1) + (fired < 0 || i <= fired ? " · " + streak : "");
      });
      out.innerHTML = fired >= 0
        ? "<b>Gap-out fires at reading " + (fired + 1) + "</b> (t = " + (fired * st.dt).toFixed(1) + " s), " + ((fired - lastNZ) * st.dt).toFixed(1) + " s after the last non-zero reading. The green ends there instead of running to G."
        : "No gap-out: the group never read zero " + st.n + " times in a row, so the green runs to G (or another interrupt).";
      out.innerHTML += "\nConfirmation window = " + st.n + " readings × " + st.dt + " s = " + (st.n * st.dt).toFixed(1) + " s. The small number under each cell is groupZeroStreak.";
    }
    draw();
  };

  // ---------------- anti-starvation ----------------
  W.starvation = function (host) {
    var st = { maxWait: 45, minGreen: 20, thr: 0.90, G: 30.67, w0: 20 };
    var svg = svgBox(host, 760, 150, "When anti-starvation cuts a green");
    var L = 40, R = 740, sx = function (t) { return L + (R - L) * t / 60; };
    for (var t = 0; t <= 60; t += 10) { S("text", { x: sx(t), y: 140, "text-anchor": "middle", "font-size": 11, text: t + " s" }, svg).style.fill = "var(--muted)"; S("line", { x1: sx(t), x2: sx(t), y1: 20, y2: 125, stroke: "var(--rule)" }, svg); }
    S("text", { x: L, y: 14, "font-size": 11, text: "current green (planned G)" }, svg);
    var gBar = S("rect", { x: L, y: 22, height: 26, rx: 3, fill: "var(--go)", opacity: .35 }, svg);
    var gUsed = S("rect", { x: L, y: 22, height: 26, rx: 3, fill: "var(--go)" }, svg);
    var floor = S("line", { y1: 18, y2: 125, stroke: "var(--ink)", "stroke-width": 2, "stroke-dasharray": "5 4" }, svg);
    var floorT = S("text", { y: 118, "font-size": 11, "font-weight": 700 }, svg);
    S("text", { x: L, y: 70, "font-size": 11, text: "another head's wait (it started the phase already waiting)" }, svg);
    var wBar = S("rect", { x: L, y: 78, height: 18, rx: 3, fill: "var(--stop)", opacity: .7 }, svg);
    var thrL = S("line", { y1: 74, y2: 100, stroke: "var(--stop)", "stroke-width": 2 }, svg);
    var c = controls(host), out = el("div", { class: "readout" }, host);
    function draw() {
      var limit = st.thr * st.maxWait;
      var reach = Math.max(0, limit - st.w0);                 // when the outside head crosses the limit
      var cut = Math.max(st.minGreen, reach);
      var ends = cut < st.G ? cut : st.G;
      gBar.setAttribute("width", sx(Math.min(60, st.G)) - L);
      gUsed.setAttribute("width", sx(Math.min(60, ends)) - L);
      floor.setAttribute("x1", sx(st.minGreen)); floor.setAttribute("x2", sx(st.minGreen));
      floorT.setAttribute("x", sx(st.minGreen) + 5); floorT.textContent = "minGreenBeforeInterrupt " + st.minGreen + " s";
      wBar.setAttribute("width", sx(Math.min(60, ends)) - L);
      thrL.setAttribute("x1", sx(Math.min(60, reach))); thrL.setAttribute("x2", sx(Math.min(60, reach)));
      out.innerHTML = "The outside head reaches " + fx(st.thr, 2) + " × " + st.maxWait + " = " + fx(limit, 1) + " s of waiting at t = " + fx(reach, 1) + " s into this green.\n" +
        (cut < st.G ? "<b>Cut at t = " + fx(cut, 1) + " s</b> (" + (reach <= st.minGreen ? "held until the 20 s floor" : "the moment it crossed the limit") + "), " + fx(st.G - cut, 1) + " s of planned green lost. EndReason = starvation."
                    : "<b>Not cut</b>: the green ends at G = " + fx(st.G, 1) + " s first.");
    }
    slider(c, "maxWaitTime", 30, 90, 1, st.maxWait, function (v) { return v + " s"; }, function (v) { st.maxWait = v; draw(); });
    slider(c, "minGreenBeforeInterrupt", 5, 40, 1, st.minGreen, function (v) { return v + " s"; }, function (v) { st.minGreen = v; draw(); });
    slider(c, "planned G", 8, 45, 0.5, st.G, function (v) { return v + " s"; }, function (v) { st.G = v; draw(); });
    slider(c, "outside wait at start", 0, 60, 1, st.w0, function (v) { return v + " s"; }, function (v) { st.w0 = v; draw(); });
    draw();
  };

  // ---------------- S1-S4 trace player ----------------
  var TRACES = {
    S1: { title: "S1 · one vehicle, quiet junction", steps: [
      { T: 0, s: "IDLE", c: [0, 0, 0, 0], qs: [null, null, null, null], L: "RRRR", n: "Snapshot: every head reads 0. All four heads red." },
      { T: 4, s: "COLLECT", c: [0, 0, 0, 1], qs: [null, null, null, 4], L: "RRRR", n: "CB reads 1: rising edge, queueStart[CB] = 4.0. Go to COLLECT, ceiling 4 + 7 = 11 s. Early exit? Heads with cars = 1 (< 2), most cars = 1 (< 3): no." },
      { T: 10, s: "COLLECT", c: [0, 0, 0, 1], qs: [null, null, null, 4], L: "RRRR", n: "Snapshots at 6, 8, 10 still read CB = 1. No early exit." },
      { T: 11, s: "SERVE", c: [0, 0, 0, 1], qs: [null, null, null, 4], L: "RRRG", n: "Ceiling reached: COLLECT ends (CollectDuration 7.0). Nothing was green, so no clearance.", q: ["wait 7.0 < 42.75: none", "eligible {CB} -> H_CB", "{CB}; the other three heads are skipped (SkippedHeads += 3)", "D = 1: 2.95 + 2.31 = 5.26 -> clamped to gMin: G = 8.00 s"] },
      { T: 19, s: "SERVE", c: [0, 0, 0, 0], qs: [null, null, null, null], L: "RRRY", n: "G elapsed: completed (GreenUsed 8.0). CB reads 0, so queueStart = ∞. No demand anywhere: close the group." },
      { T: 24, s: "IDLE", c: [0, 0, 0, 0], qs: [null, null, null, null], L: "RRRR", n: "Yellow 19-22, all-red 22-24, back to ALL_RED_IDLE. Round = 24 s for one vehicle: the cost of all-red idle instead of rest-in-green (limitation L7)." }]},
    S2: { title: "S2 · two heads within one window: early exit (a)", steps: [
      { T: 0, s: "IDLE", c: [0, 0, 0, 0], qs: [null, null, null, null], L: "RRRR", n: "All heads read 0." },
      { T: 2, s: "COLLECT", c: [1, 0, 0, 0], qs: [2, null, null, null], L: "RRRR", n: "A reads 1: queueStart[A] = 2.0. COLLECT, ceiling 9 s. Exit? no." },
      { T: 4, s: "SERVE", c: [1, 1, 0, 0], qs: [2, 4, null, null], L: "GGRR", n: "B reads 1: queueStart[B] = 4.0. Two heads have cars, so early exit (a): CollectDuration 2.0.", q: ["A waited 2.0, B 0.0: none", "A (2.0) is older than B (4.0) -> H_A", "B does not conflict with A: joins; A2 forced in -> {A, A2, B}", "A = 1 -> 5.26, B = 1 -> 5.26 -> G = 8.00 s (gMin)"] },
      { T: 12, s: "SERVE", c: [0, 0, 0, 0], qs: [null, null, null, null], L: "YYRR", n: "Readings 6:(1,1) 8:(1,1) 10:(1,0) 12:(0,0). G elapsed at 12: completed. All reset to ∞; close." },
      { T: 17, s: "IDLE", c: [0, 0, 0, 0], qs: [null, null, null, null], L: "RRRR", n: "All-red at 15, idle at 17. A led although B could have had more cars: it queued first." }]},
    S3: { title: "S3 · a burst on one head: early exit (b)", steps: [
      { T: 0, s: "IDLE", c: [0, 0, 0, 0], qs: [null, null, null, null], L: "RRRR", n: "All heads read 0." },
      { T: 2, s: "SERVE", c: [0, 0, 4, 0], qs: [null, null, 2, null], L: "RRGR", n: "Bx reads 4: queueStart[Bx] = 2.0. Most cars = 4 ≥ 3: early exit (b) immediately, CollectDuration 0.0. Ax is held at its line while Bx is green (it reads Bx inverted).", q: ["none", "H_Bx", "{Bx}; B reads 0 and is skipped", "2.95 + 4 × 2.31 = 12.19 s"] },
      { T: 14.19, s: "SERVE", c: [0, 0, 0, 0], qs: [null, null, null, null], L: "RRYR", n: "Readings 4:3 6:2 8:2 10:1 12:1 14:0. G elapsed at 14.19: completed; close." },
      { T: 19.19, s: "IDLE", c: [0, 0, 0, 0], qs: [null, null, null, null], L: "RRRR", n: "Red at 17.19 (Ax released), idle at 19.19. Four cars took a shorter round (19.19 s) than one car in S1 (24 s): the 7 s window is a ceiling, not a cost." }]},
    S4: { title: "S4 · morning peak: the round never closes", steps: [
      { T: 0, s: "COLLECT", c: [12, 5, 9, 7], qs: [0, 0, 0, 0], L: "RRRR", n: "Every head rises at once: all queueStart = 0.0. Early exit (a) at once." },
      { T: 0, s: "SERVE", c: [12, 5, 9, 7], qs: [0, 0, 0, 0], L: "GGRR", n: "Phase 1. TieBreaksApplied = 1. Bx had cars, was excluded and tied with A: TieBreaksExcludingBx = 1.", q: ["all waits 0: none", "all tied at 0.0 -> priority -> H_A", "B joins; Bx and CB conflict with A; A2 forced -> {A, A2, B}", "A = 12 -> 30.67, B = 5 -> 14.50 -> G = 30.67 s"] },
      { T: 20, s: "SERVE", c: [12, 5, 9, 7], qs: [0, 0, 0, 0], L: "GGRR", n: "Elapsed 20 s: anti-starvation may now look. Bx and CB have waited 20 s < 40.5 s: no cut." },
      { T: 30.67, s: "SERVE", c: [10, 4, 9, 7], qs: [30.67, 30.67, 0, 0], L: "GGRR", n: "G reached: completed. A and B still have cars, so their queueStart restarts at 30.67. Bx and CB keep 0.0. Demand remains: SERVE again, no COLLECT." },
      { T: 30.67, s: "SERVE", c: [10, 4, 9, 7], qs: [30.67, 30.67, 0, 0], L: "YGRR", n: "Phase 2. Only {A, A2} drops, so only H_A goes yellow; B stays green through the change.", q: ["Bx and CB waited 30.67 < 42.75: none", "Bx (0.0, priority 2) and CB (0.0, priority 3) tie -> H_Bx", "CB conflicts with Bx; A conflicts with Bx; B joins -> {B, Bx}", "computed at green-open (35.67): Bx = 9 -> 23.74, B = 4 -> 12.19 -> G = 23.74 s"] },
      { T: 35.67, s: "SERVE", c: [10, 4, 9, 7], qs: [30.67, 30.67, 0, 0], L: "RGGR", n: "Yellow 30.67-33.67, all-red to 35.67, then Bx green. Planned end 59.41." },
      { T: 55.67, s: "SERVE", c: [10, 3, 8, 7], qs: [30.67, 55.67, 55.67, 0], L: "RGGR", n: "Elapsed 20 s: CB has waited 55.67 s ≥ 40.5 s. CUT (EndReason = starvation, GreenUsed 20.0 of 23.74). StarvationEvents = 1." },
      { T: 55.67, s: "SERVE", c: [10, 3, 8, 7], qs: [30.67, 55.67, 55.67, 0], L: "RYYR", n: "Phase 3. StarvationEvents = 2: one episode is counted twice, the cut and the override (limitation L13).", q: ["CB waited 55.67 ≥ 42.75: STARVATION OVERRIDE -> H_CB", "skipped: starvation takes priority", "CB conflicts with every other head -> {CB}", "CB = 7 -> 2.95 + 7 × 2.31 = 19.12 s"] },
      { T: 60.67, s: "SERVE", c: [10, 3, 8, 7], qs: [30.67, 55.67, 55.67, 0], L: "RRRG", n: "Yellow 55.67-58.67, all-red to 60.67, CB green until 79.79. The round never returns to ALL_RED_IDLE." }]}
  };
  W.sodaTrace = function (host) {
    var bar = controls(host), cur = "S4";
    Object.keys(TRACES).forEach(function (k) {
      var b = el("button", { type: "button", "aria-pressed": k === cur ? "true" : "false", "data-k": k }, bar, k);
      b.addEventListener("click", function () { cur = k; bar.querySelectorAll("button[data-k]").forEach(function (x) { x.setAttribute("aria-pressed", x.dataset.k === k); }); player.duration = TRACES[k].steps.length - 0.001; player.pause(); player.seek(0); });
    });
    var title = el("p", { class: "eyebrow", style: "margin-top:.6rem" }, host);
    var svg = svgBox(host, 900, 250, "SODA state, four heads and the decision");
    // state machine
    var STN = [["IDLE", "ALL_RED_IDLE", 20], ["COLLECT", "COLLECT", 200], ["SERVE", "SERVE", 380]], stEls = {};
    STN.forEach(function (s, i) {
      var r = S("rect", { x: s[2], y: 14, width: 150, height: 34, rx: 17, fill: "var(--paper)", stroke: "var(--rule)", "stroke-width": 1.5 }, svg);
      S("text", { x: s[2] + 75, y: 36, "text-anchor": "middle", "font-size": 12.5, "font-weight": 700, "font-family": "var(--f-mono)", text: s[1] }, svg);
      if (i < 2) S("path", { d: "M" + (s[2] + 152) + " 31 l24 0 m-6 -5 l6 5 l-6 5", fill: "none", stroke: "var(--muted)", "stroke-width": 1.5 }, svg);
      stEls[s[0]] = r;
    });
    S("path", { d: "M455 48 q0 22 -40 22 q-40 0 -40 -18", fill: "none", stroke: "var(--muted)", "stroke-width": 1.5 }, svg);
    S("text", { x: 545, y: 72, "font-size": 11, text: "repeat while any head has cars" }, svg).style.fill = "var(--muted)";
    var tT = S("text", { x: 880, y: 36, "text-anchor": "end", "font-size": 22, "font-weight": 700, "font-family": "var(--f-mono)" }, svg);
    // head cards
    var hc = HEADS.map(function (h, i) {
      var x = 20 + i * 220, y = 96;
      S("rect", { x: x, y: y, width: 205, height: 140, rx: 8, fill: "var(--paper)", stroke: "var(--rule)" }, svg);
      var lamp = S("circle", { cx: x + 20, cy: y + 22, r: 9, fill: "var(--stop)" }, svg);
      S("text", { x: x + 38, y: y + 27, "font-size": 15, "font-weight": 700, "font-family": "var(--f-mono)", text: h.name }, svg);
      var cnt = S("text", { x: x + 190, y: y + 28, "text-anchor": "end", "font-size": 20, "font-weight": 700, "font-family": "var(--f-mono)" }, svg);
      S("text", { x: x + 14, y: y + 56, "font-size": 11, text: "queueStart" }, svg).style.fill = "var(--muted)";
      var qs = S("text", { x: x + 190, y: y + 56, "text-anchor": "end", "font-size": 13, "font-family": "var(--f-mono)" }, svg);
      S("text", { x: x + 14, y: y + 78, "font-size": 11, text: "wait = now - queueStart" }, svg).style.fill = "var(--muted)";
      var wt = S("text", { x: x + 190, y: y + 78, "text-anchor": "end", "font-size": 13, "font-family": "var(--f-mono)" }, svg);
      S("rect", { x: x + 14, y: y + 92, width: 177, height: 10, rx: 5, fill: "var(--sunk)" }, svg);
      var wb = S("rect", { x: x + 14, y: y + 92, width: 0, height: 10, rx: 5, fill: "var(--accent)" }, svg);
      var m1 = x + 14 + 177 * 40.5 / 60, m2 = x + 14 + 177 * 42.75 / 60;
      S("line", { x1: m1, x2: m1, y1: y + 88, y2: y + 106, stroke: "var(--amber)", "stroke-width": 2 }, svg);
      S("line", { x1: m2, x2: m2, y1: y + 88, y2: y + 106, stroke: "var(--stop)", "stroke-width": 2 }, svg);
      S("text", { x: x + 14, y: y + 124, "font-size": 10, text: "0" }, svg).style.fill = "var(--muted)";
      S("text", { x: m1 - 2, y: y + 124, "font-size": 10, "text-anchor": "end", text: "40.5 cut" }, svg).style.fill = "var(--amber)";
      S("text", { x: m2 + 2, y: y + 124, "font-size": 10, text: "42.75 lead" }, svg).style.fill = "var(--stop)";
      return { lamp: lamp, cnt: cnt, qs: qs, wt: wt, wb: wb };
    });
    var qbox = el("div", { class: "readout" }, host);
    function draw(t) {
      var tr = TRACES[cur], i = Math.min(tr.steps.length - 1, Math.floor(t)), s = tr.steps[i];
      title.textContent = tr.title + "  ·  step " + (i + 1) + " of " + tr.steps.length;
      Object.keys(stEls).forEach(function (k) { stEls[k].setAttribute("stroke", k === s.s ? "var(--accent)" : "var(--rule)"); stEls[k].setAttribute("fill", k === s.s ? "var(--accent-tint)" : "var(--paper)"); stEls[k].setAttribute("stroke-width", k === s.s ? 3 : 1.5); });
      tT.textContent = "T = " + s.T.toFixed(2).replace(/\.00$/, ".0");
      hc.forEach(function (h, k) {
        h.lamp.setAttribute("fill", lightColor(s.L[k]));
        h.cnt.textContent = s.c[k] + (s.c[k] === 1 ? " car" : " cars");
        h.qs.textContent = s.qs[k] == null ? "∞ (empty)" : s.qs[k].toFixed(2);
        var w = s.qs[k] == null ? 0 : s.T - s.qs[k];
        h.wt.textContent = fx(w, 2) + " s";
        h.wb.setAttribute("width", 177 * Math.min(1, w / 60));
        h.wb.setAttribute("fill", w >= 42.75 ? "var(--stop)" : w >= 40.5 ? "var(--amber)" : "var(--accent)");
      });
      qbox.innerHTML = s.n + (s.q ? "\n<b>Q1</b> " + s.q[0] + "\n<b>Q2</b> " + s.q[1] + "\n<b>Q3</b> " + s.q[2] + "\n<b>Q4</b> " + s.q[3] : "");
    }
    var player = new T.Player(controls(host), { duration: TRACES[cur].steps.length - 0.001, step: 1, speed: 0.4, draw: draw, loop: false,
      format: function () { return ""; } });
    host.appendChild(qbox);
  };

  // ================================================================ FixedController: one cycle
  // RunCycle: green for greenDuration; then only lanes not green in the next phase get yellow + all-red.
  W.fixedCycle = function (host) {
    var st = { p: [40, 20, 31], y: 3, r: 2 };
    var PH = [["P1 MainThrough", ["A", "A2", "B"]], ["P2 EastRightTurn", ["B", "Bx"]], ["P3 ABACExit", ["CB"]]];
    var ROWS = ["A", "A2", "B", "Bx", "CB", "Ax"];
    function timeline() {
      // returns per-lane list of [t0, t1, state] and the cycle length, mirroring RunCycle and GetCycleLength
      var segs = {}, t = 0, green = [];
      ROWS.forEach(function (r) { segs[r] = []; });
      for (var i = 0; i < 3; i++) {
        var lanes = PH[i][1], nxt = PH[(i + 1) % 3][1];
        lanes.forEach(function (l) { segs[l].push([t, t + st.p[i], "G"]); });
        t += st.p[i];
        var dropping = lanes.filter(function (l) { return nxt.indexOf(l) < 0; });
        var staying = lanes.filter(function (l) { return nxt.indexOf(l) >= 0; });
        if (dropping.length) {
          dropping.forEach(function (l) { segs[l].push([t, t + st.y, "Y"]); });
          staying.forEach(function (l) { segs[l].push([t, t + st.y + st.r, "G"]); });
          t += st.y + st.r;
        }
      }
      // Ax reads Bx inverted: Bx green or yellow means Ax must stop
      var bx = segs.Bx;
      return { segs: segs, cycle: t };
    }
    function stateAt(segs, lane, t) {
      if (lane === "Ax") { var b = stateAt(segs, "Bx", t); return b === "R" ? "G" : "R"; }
      for (var i = 0; i < segs[lane].length; i++) { var g = segs[lane][i]; if (t >= g[0] && t < g[1]) return g[2]; }
      return "R";
    }
    var svg = svgBox(host, 900, 270, "One cycle of the fixed plan");
    var L = 70, R = 880, top = 20, rh = 30;
    var gr = S("g", null, svg), head = S("line", { y1: 10, y2: top + ROWS.length * rh + 4, stroke: "var(--ink)", "stroke-width": 2 }, svg);
    var tl;
    function drawBars() {
      gr.innerHTML = ""; tl = timeline();
      var sx = function (t) { return L + (R - L) * t / tl.cycle; };
      ROWS.forEach(function (lane, i) {
        var y = top + i * rh;
        S("text", { x: L - 10, y: y + 19, "text-anchor": "end", "font-size": 13, "font-weight": 700, text: lane + (lane === "Ax" ? "*" : "") }, gr).style.fill = laneColor(lane);
        S("rect", { x: L, y: y + 4, width: R - L, height: 20, fill: "var(--stop)", opacity: .75, rx: 2 }, gr);
        var segs = lane === "Ax" ? null : tl.segs[lane];
        if (lane === "Ax") {
          // green wherever Bx is red
          var bxs = tl.segs.Bx.slice().sort(function (a, b) { return a[0] - b[0]; });
          var gaps = [], c = 0;
          bxs.forEach(function (g) { if (g[0] > c) gaps.push([c, g[0]]); c = Math.max(c, g[1]); });
          if (c < tl.cycle) gaps.push([c, tl.cycle]);
          gaps.forEach(function (g) { S("rect", { x: sx(g[0]), y: y + 4, width: sx(g[1]) - sx(g[0]), height: 20, fill: "var(--go)", rx: 2 }, gr); });
        } else segs.forEach(function (g) { S("rect", { x: sx(g[0]), y: y + 4, width: sx(g[1]) - sx(g[0]), height: 20, fill: g[2] === "G" ? "var(--go)" : "var(--amber)", rx: 2 }, gr); });
      });
      var t = 0, ty = top + ROWS.length * rh + 22;
      for (var i = 0; i < 3; i++) {
        S("text", { x: sx(t) + 3, y: ty, "font-size": 11, text: PH[i][0] + " " + st.p[i] + " s" }, gr).style.fill = "var(--muted)";
        t += st.p[i] + st.y + st.r;
      }
      S("text", { x: R, y: ty + 18, "text-anchor": "end", "font-size": 12, "font-weight": 700, text: "cycle = " + fx(tl.cycle, 0) + " s" }, gr);
      S("text", { x: L, y: ty + 18, "font-size": 11, text: "* Ax has no phase: it reads the Bx light inverted (CarSpawner.lightIsInverted)" }, gr).style.fill = "var(--muted)";
    }
    var jwrap = el("div", { style: "margin-top:.6rem" }, host);
    var jsvg = svgBox(jwrap, 966, 403, "The junction during the cycle");
    var J = drawJunction(jsvg, { id: "fx", roi: false });
    var out = el("div", { class: "readout" }, host);
    function draw(t) {
      var x = L + (R - L) * t / tl.cycle;
      head.setAttribute("x1", x); head.setAttribute("x2", x);
      var hs = { H_A: stateAt(tl.segs, "A", t), H_B: stateAt(tl.segs, "B", t), H_Bx: stateAt(tl.segs, "Bx", t), H_CB: stateAt(tl.segs, "CB", t) };
      Object.keys(hs).forEach(function (h) { J.lights[h].dot.setAttribute("fill", lightColor(hs[h])); });
      ["A", "A2", "B", "Bx", "CB", "Ax"].forEach(function (l) {
        var s0 = stateAt(tl.segs, l, t);
        J.lanes[l].path.setAttribute("opacity", s0 === "G" ? 1 : s0 === "Y" ? .7 : .18);
        J.lanes[l].path.setAttribute("stroke", s0 === "Y" ? "var(--amber)" : laneColor(l));
      });
      J.lanes.CA.path.setAttribute("opacity", 1);
      var on = ["A", "A2", "B", "Bx", "CB", "Ax"].filter(function (l) { return stateAt(tl.segs, l, t) === "G"; });
      out.innerHTML = "t = " + fx(t, 1) + " s   green: " + (on.join(", ") || "none (all-red)") + "    CA is always free.";
    }
    drawBars();
    var player = new T.Player(controls(host), { duration: tl.cycle, step: 1, speed: 6, speeds: [2, 6, 12], draw: draw });
    var c = controls(host);
    function upd() { drawBars(); player.duration = tl.cycle; player.seek(Math.min(player.t, tl.cycle)); }
    slider(c, "P1 green", 10, 60, 1, 40, function (v) { return v + " s"; }, function (v) { st.p[0] = v; upd(); });
    slider(c, "P2 green", 10, 40, 1, 20, function (v) { return v + " s"; }, function (v) { st.p[1] = v; upd(); });
    slider(c, "P3 green", 10, 50, 1, 31, function (v) { return v + " s"; }, function (v) { st.p[2] = v; upd(); });
    slider(c, "yellow", 2, 5, 0.5, 3, function (v) { return v + " s"; }, function (v) { st.y = v; upd(); });
    slider(c, "all-red", 0, 4, 0.5, 2, function (v) { return v + " s"; }, function (v) { st.r = v; upd(); });
    host.appendChild(out);
  };

  // ================================================================ CarAgent
  W.discharge = function (host) {
    var D = window.TOUR_DATA.headway, keys = Object.keys(D.traj), cur = "1.9";
    var svg = svgBox(host, 900, 170, "A queue of ten cars leaving on green");
    var x0 = 20, x1 = 880, zMin = -60, zMax = 30, sx = function (z) { return x0 + (x1 - x0) * (z - zMin) / (zMax - zMin); };
    S("rect", { x: x0, y: 40, width: x1 - x0, height: 46, fill: "var(--road)", rx: 4 }, svg);
    S("line", { x1: sx(0), x2: sx(0), y1: 34, y2: 92, stroke: "var(--ink)", "stroke-width": 3 }, svg);
    S("text", { x: sx(0), y: 26, "text-anchor": "middle", "font-size": 11, text: "stop line" }, svg);
    var lamp = S("circle", { cx: sx(0) + 18, cy: 26, r: 7, fill: "var(--go)" }, svg);
    var col = { Normal: "var(--lane-a)", Aggressive: "var(--stop)", Cautious: "var(--go)" };
    var cars = D.mix.map(function (m, i) {
      var r = S("rect", { y: 53, width: (x1 - x0) * 4 / (zMax - zMin), height: 20, rx: 4, fill: col[m] }, svg);
      var t = S("text", { y: 67, "text-anchor": "middle", "font-size": 10, "font-weight": 700, text: i + 1 }, svg); t.style.fill = "var(--paper)";
      return { r: r, t: t };
    });
    var lg = S("g", null, svg);
    [["Aggressive", "var(--stop)"], ["Normal", "var(--lane-a)"], ["Cautious", "var(--go)"]].forEach(function (k, i) {
      S("rect", { x: 20 + i * 110, y: 112, width: 12, height: 12, fill: k[1], rx: 2 }, lg);
      S("text", { x: 38 + i * 110, y: 122, "font-size": 11, text: k[0] }, lg);
    });
    var crossT = S("text", { x: 880, y: 122, "text-anchor": "end", "font-size": 12, "font-family": "var(--f-mono)" }, svg);
    var c = controls(host);
    el("span", { class: "muted" }, c, "desiredTimeHeadway:");
    var bs = keys.map(function (k) {
      var b = el("button", { type: "button", "aria-pressed": k === cur ? "true" : "false" }, c, k + " s");
      b.addEventListener("click", function () { cur = k; bs.forEach(function (x) { x.setAttribute("aria-pressed", x.textContent === k + " s"); }); player.seek(player.t); });
      return b;
    });
    function draw(t) {
      var fr = D.traj[cur], i = Math.min(fr.length - 1, Math.round(t / D.trajDt)), f = fr[i];
      var crossed = 0;
      f.forEach(function (z, k) {
        cars[k].r.setAttribute("x", sx(z - 2)); cars[k].t.setAttribute("x", sx(z));
        if (z + 2 >= 0) crossed++;
      });
      var sw = D.sweep.filter(function (r) { return r.T.toFixed(1) === cur; })[0];
      crossT.textContent = crossed + " of 10 across · mean headway " + sw.h.toFixed(2) + " s/veh";
    }
    var player = new T.Player(controls(host), { duration: 31.8, step: 1, speed: 2, speeds: [1, 2, 4], draw: draw });
    // headway against T
    var svg2 = svgBox(host, 900, 260, "Measured discharge headway against desiredTimeHeadway");
    var L = 60, R = 880, Tp = 15, B = 215, sxT = function (v) { return L + (R - L) * v / 3; }, sy = function (h) { return B - (B - Tp) * (h - 1.2) / (3.4 - 1.2); };
    for (var h = 1.4; h <= 3.31; h += 0.4) { S("line", { x1: L, x2: R, y1: sy(h), y2: sy(h), stroke: "var(--rule)" }, svg2); S("text", { x: L - 8, y: sy(h) + 4, "text-anchor": "end", "font-size": 11, text: h.toFixed(1) }, svg2).style.fill = "var(--muted)"; }
    for (var v = 0; v <= 3; v += 0.5) S("text", { x: sxT(v), y: B + 18, "text-anchor": "middle", "font-size": 11, text: v.toFixed(1) }, svg2).style.fill = "var(--muted)";
    S("text", { x: (L + R) / 2, y: B + 38, "text-anchor": "middle", "font-size": 11, text: "desiredTimeHeadway T (s)  ·  vertical: mean headway at the stop line (s/veh)" }, svg2).style.fill = "var(--muted)";
    S("rect", { x: L, y: sy(2.46), width: R - L, height: sy(2.16) - sy(2.46), fill: "var(--go)", opacity: .12 }, svg2);
    S("line", { x1: L, x2: R, y1: sy(2.31), y2: sy(2.31), stroke: "var(--go)", "stroke-dasharray": "6 4" }, svg2);
    S("text", { x: R - 4, y: sy(2.31) - 6, "text-anchor": "end", "font-size": 11, text: "measured 2.31 ± 0.15 (HeadwayTest band)" }, svg2).style.fill = "var(--go)";
    var d = ""; D.sweep.forEach(function (r, i) { d += (i ? " L" : "M") + sxT(r.T).toFixed(1) + " " + sy(r.h).toFixed(1); });
    S("path", { d: d, fill: "none", stroke: "var(--accent)", "stroke-width": 2.5 }, svg2);
    D.sweep.forEach(function (r) { S("circle", { cx: sxT(r.T), cy: sy(r.h), r: r.T.toFixed(1) === "1.9" ? 6 : 2.5, fill: "var(--accent)" }, svg2); });
    S("text", { x: sxT(1.9) + 10, y: sy(2.28) + 18, "font-size": 12, "font-weight": 700, text: "T = 1.9 -> 2.28" }, svg2);
  };

  W.dilemma = function (host) {
    var P = [["Aggressive", 16.67, 22, 1.6], ["Normal", 15.28, 9, 1.1], ["Cautious", 13.89, 3, 0.7]];
    var st = { d: 15 };
    var svg = svgBox(host, 900, 190, "Go or stop at amber, by driver profile");
    var L = 120, R = 880, sx = function (m) { return L + (R - L) * m / 40; };
    for (var m = 0; m <= 40; m += 5) { S("line", { x1: sx(m), x2: sx(m), y1: 10, y2: 150, stroke: "var(--rule)" }, svg); S("text", { x: sx(m), y: 168, "text-anchor": "middle", "font-size": 11, text: m + " m" }, svg).style.fill = "var(--muted)"; }
    S("text", { x: sx(20), y: 186, "text-anchor": "middle", "font-size": 11, text: "distance to the stop point when the light turns amber" }, svg).style.fill = "var(--muted)";
    var marks = [], rows = P.map(function (p, i) {
      var y = 20 + i * 45, brake = p[1] * p[1] / (2 * p[2]), thr = brake * p[3];
      S("text", { x: L - 10, y: y + 18, "text-anchor": "end", "font-size": 13, "font-weight": 700, text: p[0] }, svg);
      S("rect", { x: sx(0), y: y + 4, width: sx(Math.min(40, thr)) - sx(0), height: 22, fill: "var(--amber)", opacity: .35, rx: 3 }, svg);
      S("rect", { x: sx(Math.min(40, thr)), y: y + 4, width: sx(40) - sx(Math.min(40, thr)), height: 22, fill: "var(--stop)", opacity: .25, rx: 3 }, svg);
      S("text", { x: sx(Math.min(40, thr)) + 4, y: y + 19, "font-size": 11, text: "go within " + thr.toFixed(1) + " m | stop beyond" }, svg);
      var mk = S("circle", { cy: y + 15, r: 7, fill: "var(--ink)" }, svg);
      marks.push({ mk: mk, thr: thr, brake: brake, p: p });
    });
    var out = el("div", { class: "readout" }, host);
    function draw() {
      marks.forEach(function (m) { m.mk.setAttribute("cx", sx(st.d)); m.mk.setAttribute("fill", st.d <= m.thr ? "var(--amber)" : "var(--stop)"); });
      out.innerHTML = marks.map(function (m) {
        return m.p[0] + ": braking distance " + m.brake.toFixed(1) + " m × dilemma factor " + m.p[3] + " = " + m.thr.toFixed(1) + " m  ->  <b>" + (st.d <= m.thr ? "carries on through the amber" : "stops") + "</b>";
      }).join("\n");
    }
    slider(controls(host), "distance at amber", 0, 40, 0.5, st.d, function (v) { return v + " m"; }, function (v) { st.d = v; draw(); });
    host.appendChild(out);
    draw();
  };

  // ================================================================ ROIZone: count vs sampled count
  W.sampling = function (host) {
    // An illustration: arrival and departure times are made up for the drawing, the sampling rule is the code's.
    var CARS = [[0.4, 9.2], [1.6, 11.8], [2.1, 14.0], [5.3, 16.1], [6.0, 18.4], [9.7, 12.6], [10.2, 21.0], [13.8, 22.5]];
    var st = { dt: 1 };
    var svg = svgBox(host, 900, 220, "Continuous count against what a snapshot sees");
    var L = 50, R = 880, Tp = 20, B = 170, Tmax = 24, Cmax = 7;
    var sx = function (t) { return L + (R - L) * t / Tmax; }, sy = function (c) { return B - (B - Tp) * c / Cmax; };
    for (var c = 0; c <= Cmax; c++) { S("line", { x1: L, x2: R, y1: sy(c), y2: sy(c), stroke: "var(--rule)" }, svg); S("text", { x: L - 8, y: sy(c) + 4, "text-anchor": "end", "font-size": 11, text: c }, svg).style.fill = "var(--muted)"; }
    for (var t = 0; t <= Tmax; t += 4) S("text", { x: sx(t), y: B + 18, "text-anchor": "middle", "font-size": 11, text: t + " s" }, svg).style.fill = "var(--muted)";
    function count(t) { return CARS.filter(function (k) { return t >= k[0] && t < k[1]; }).length; }
    var d = "", prev = null;
    for (var t2 = 0; t2 <= Tmax; t2 += 0.05) { var cc = count(t2); d += (d ? " L" : "M") + sx(t2).toFixed(1) + " " + sy(prev == null ? cc : prev).toFixed(1) + " L" + sx(t2).toFixed(1) + " " + sy(cc).toFixed(1); prev = cc; }
    S("path", { d: d, fill: "none", stroke: "var(--muted)", "stroke-width": 1.5 }, svg);
    var dots = S("g", null, svg);
    S("text", { x: R, y: 14, "text-anchor": "end", "font-size": 11, text: "grey: cars in the zone at every instant (carsInZone.Count)   ·   blue: GetSampledCount() read by Snapshot()" }, svg).style.fill = "var(--muted)";
    var out = el("div", { class: "readout" }, host);
    function draw() {
      dots.innerHTML = "";
      var reads = [], last = 0, qs = null, rising = [];
      for (var t3 = 0; t3 <= Tmax + 1e-9; t3 += st.dt) {
        var v = count(t3); reads.push(v);
        S("circle", { cx: sx(t3), cy: sy(v), r: 4.5, fill: "var(--accent)" }, dots);
        if (last === 0 && v > 0) { qs = t3; rising.push(t3); }
        if (v === 0) qs = null;
        last = v;
      }
      out.innerHTML = "snapshotInterval = " + st.dt + " s: " + reads.length + " readings. The controller sees only the blue dots, never which car is which.\n" +
        "queueStart is set at the first non-zero reading: t = " + rising.map(function (x) { return x.toFixed(1); }).join(", ") + " s, the first car entered at 0.4 s.";
    }
    slider(controls(host), "snapshotInterval", 0.5, 4, 0.5, 1, function (v) { return v + " s"; }, function (v) { st.dt = v; draw(); });
    host.appendChild(out);
    draw();
  };

  // ================================================================ IntersectionConfig
  W.conflictMatrix = function (host) {
    var lanes = JSON.parse(host.dataset.lanes), pairs = JSON.parse(host.dataset.pairs);
    var why = {}; pairs.forEach(function (p) { why[p[0] + "|" + p[1]] = p[2]; why[p[1] + "|" + p[0]] = p[2]; });
    var t = el("table", { class: "plain", style: "width:auto;font-family:var(--f-mono)" }, el("div", { class: "tw" }, host));
    var out = el("div", { class: "readout" }, host);
    var hr = el("tr", null, el("thead", null, t)); el("th", null, hr, "");
    lanes.forEach(function (l) { var th = el("th", null, hr, l); th.style.color = laneColor(l); });
    var tb = el("tbody", null, t);
    lanes.forEach(function (a) {
      var tr = el("tr", null, tb), th = el("th", null, tr, a); th.style.color = laneColor(a);
      lanes.forEach(function (b) {
        var k = why[a + "|" + b], td = el("td", { style: "text-align:center;padding:.25rem" }, tr);
        var btn = el("button", { type: "button", style: "width:2.6rem;height:2.2rem;border-radius:5px;" + (a === b ? "background:var(--sunk);color:var(--muted)" : k ? "background:color-mix(in srgb,var(--stop) 22%,var(--paper));border-color:var(--stop)" : "background:color-mix(in srgb,var(--go) 16%,var(--paper));border-color:var(--go)") }, td, a === b ? "-" : k ? "✕" : "✓");
        btn.addEventListener("click", function () {
          out.innerHTML = a === b ? "A lane never conflicts with itself." :
            k ? "<b>" + a + " and " + b + " conflict.</b> Reason in the code: " + k :
            "<b>" + a + " and " + b + " may be green together.</b>" + ((a === "A" && b === "A2") || (a === "A2" && b === "A") ? " Deliberately no pair: they are one physical light, so they must always be green together." : a + b === "AB" || a + b === "BA" ? " Opposing through movements." : (a + b).indexOf("Bx") >= 0 && (a + b).replace("Bx", "") === "B" ? " Same approach." : "");
        });
      });
    });
    out.innerHTML = "Click a cell. ✕ = a ConflictPair exists (both directions are filled in by Build). ✓ = no pair, so the lanes may share a green.";
  };
  function stableHash(text) {
    var h = 2166136261 >>> 0;
    for (var i = 0; i < text.length; i++) { h = (h ^ text.charCodeAt(i)) >>> 0; h = Math.imul(h, 16777619) >>> 0; }
    return h & 0x7FFFFFFF;
  }
  T.stableHash = stableHash;
  W.seeds = function (host) {
    var c = controls(host);
    var lab = el("label", null, c, "Scenario ");
    var sel = el("select", null, lab);
    ["MorningPeak", "EveningPeak", "MiddayNormal", "ABACEvent"].forEach(function (n) { el("option", { value: n }, sel, n); });
    var rep = slider(c, "repetition", 1, 27, 1, 1, function (v) { return v + ""; }, function () { draw(); });
    var out = el("div", { class: "readout" }, host);
    function draw() {
      var hs = stableHash(sel.value), seed = (hs + rep.get() * 31) | 0;
      var lines = ["StableHash(\"" + sel.value + "\") = " + hs, "run seed = " + hs + " + " + rep.get() + " × 31 = <b>" + seed + "</b>   (the same for the Fixed and the Adaptive run)", ""];
      ["A", "A2", "Ax", "B", "Bx", "CB", "CA"].forEach(function (l) {
        var lh = stableHash(l);
        lines.push("lane " + (l + "  ").slice(0, 3) + " seed = " + seed + " + StableHash(\"" + l + "\") " + lh + " = " + ((seed + lh) | 0));
      });
      out.innerHTML = lines.join("\n");
    }
    sel.addEventListener("change", draw);
    draw();
  };

  // ================================================================ CarSpawner: arrivals
  // Poisson gaps use the code's formula, gap = -ln(1 - u) x meanArrivalInterval. The u values here come
  // from a small seeded generator in the page, not from .NET System.Random, so the dots are an
  // illustration of the rule, not the cars of a particular run.
  function lcg(seed) { var x = seed >>> 0; return function () { x = (Math.imul(x, 1664525) + 1013904223) >>> 0; return x / 4294967296; }; }
  W.arrivals = function (host) {
    var st = { mean: 4, seed: 7, agg: 25, nor: 60 };
    var svg = svgBox(host, 900, 200, "Poisson and scripted arrivals over two minutes");
    var L = 110, R = 880, H = 120, sx = function (t) { return L + (R - L) * t / H; };
    for (var t = 0; t <= H; t += 20) { S("line", { x1: sx(t), x2: sx(t), y1: 14, y2: 150, stroke: "var(--rule)" }, svg); S("text", { x: sx(t), y: 168, "text-anchor": "middle", "font-size": 11, text: t + " s" }, svg).style.fill = "var(--muted)"; }
    S("text", { x: L - 10, y: 50, "text-anchor": "end", "font-size": 13, "font-weight": 700, text: "Poisson" }, svg);
    S("text", { x: L - 10, y: 115, "text-anchor": "end", "font-size": 13, "font-weight": 700, text: "Scripted" }, svg);
    S("text", { x: L - 10, y: 130, "text-anchor": "end", "font-size": 10, text: "8 cars, 2 s apart" }, svg).style.fill = "var(--muted)";
    var g = S("g", null, svg);
    var out = el("div", { class: "readout" }, host);
    var COL = { Aggressive: "var(--stop)", Normal: "var(--lane-a)", Cautious: "var(--go)" };
    function draw() {
      g.innerHTML = "";
      var rnd = lcg(st.seed * 7919 + 1), t = 0, n = 0, counts = { Aggressive: 0, Normal: 0, Cautious: 0 };
      while (true) {
        var u = rnd(), gap = -Math.log(1 - Math.min(Math.max(u, 0), 0.999999)) * st.mean;
        t += gap; if (t > H) break;
        var roll = rnd() * 100, type = roll < st.agg ? "Aggressive" : roll < st.agg + st.nor ? "Normal" : "Cautious";
        counts[type]++; n++;
        S("circle", { cx: sx(t), cy: 45, r: 6, fill: COL[type] }, g);
      }
      for (var k = 0; k < 8; k++) S("circle", { cx: sx(10 + k * 2), cy: 112, r: 6, fill: "var(--lane-a)" }, g);
      out.innerHTML = "meanArrivalInterval " + st.mean + " s: expected " + (H / st.mean).toFixed(1) + " cars in " + H + " s, this draw gave " + n + ".  " +
        "Aggressive " + counts.Aggressive + " · Normal " + counts.Normal + " · Cautious " + counts.Cautious + "  (mix " + st.agg + " / " + st.nor + " / " + (100 - st.agg - st.nor) + ")";
    }
    var c = controls(host);
    slider(c, "meanArrivalInterval", 1, 20, 0.5, st.mean, function (v) { return v + " s"; }, function (v) { st.mean = v; draw(); });
    slider(c, "seed", 1, 50, 1, st.seed, function (v) { return v + ""; }, function (v) { st.seed = v; draw(); });
    slider(c, "aggressivePercent", 0, 100, 5, st.agg, function (v) { return v + "%"; }, function (v) { st.agg = Math.min(v, 100); if (st.agg + st.nor > 100) st.nor = 100 - st.agg; draw(); });
    host.appendChild(out);
    draw();
  };

  // ================================================================ ScenarioManager
  W.batchGrid = function (host) {
    var SC = ["MorningPeak", "EveningPeak", "MiddayNormal", "ABACEvent"], REPS = 27;
    var svg = svgBox(host, 900, 250, "The 216 runs of a batch, in the order they run");
    var L = 120, cw = 27, ch = 22, cells = [];
    SC.forEach(function (name, si) {
      var y = 20 + si * 56;
      S("text", { x: L - 10, y: y + 26, "text-anchor": "end", "font-size": 12.5, "font-weight": 700, text: name }, svg);
      for (var r = 0; r < REPS; r++) {
        var x = L + r * cw;
        var f = S("rect", { x: x, y: y, width: cw - 3, height: ch, rx: 3, fill: "var(--sunk)" }, svg);
        var a = S("rect", { x: x, y: y + ch + 2, width: cw - 3, height: ch, rx: 3, fill: "var(--sunk)" }, svg);
        cells.push({ el: f, kind: "F", s: name, r: r + 1 }, { el: a, kind: "A", s: name, r: r + 1 });
      }
    });
    S("text", { x: L, y: 245, "font-size": 11, text: "top square of each pair = Fixed, bottom = Adaptive · one column = one repetition, sharing one seed" }, svg).style.fill = "var(--muted)";
    var out = el("div", { class: "readout" }, host);
    function draw(t) {
      var done = Math.floor(t);
      cells.forEach(function (c, i) {
        c.el.setAttribute("fill", i < done ? (c.kind === "F" ? "var(--muted)" : "var(--accent)") : i === done ? "var(--amber)" : "var(--sunk)");
      });
      var cur = cells[Math.min(done, cells.length - 1)];
      var seed = (T.stableHash(cur.s) + cur.r * 31) | 0;
      var realH = (cells.length - done) * (120 + 1800) / 10 / 3600;
      out.innerHTML = "run " + Math.min(done + 1, 216) + " of 216: " + cur.s + " rep " + cur.r + " · " + (cur.kind === "F" ? "Fixed" : "Adaptive") + " · seed " + seed +
        "\nEach run: 120 s warm-up + 1,800 s measured = 1,920 simulated seconds, at 10× ≈ 3.2 real minutes if the machine keeps up.  Theoretical time left ≈ " + realH.toFixed(1) + " h (v5 took 11.5 h).";
    }
    new T.Player(controls(host), { duration: 216, step: 1, speed: 12, speeds: [4, 12, 40], draw: draw, loop: false, format: function (t) { return Math.floor(t) + " / 216"; } });
    host.appendChild(out);
  };
  W.a2split = function (host) {
    var st = { A: 18.8, share: 0.054 };
    var out = el("div", { class: "readout" }, host), c = controls(host);
    host.insertBefore(c, out);
    function draw() {
      var inner = st.A / (1 - st.share), outer = st.A / st.share;
      out.innerHTML = "combined through traffic: one car every " + st.A.toFixed(1) + " s (" + (3600 / st.A).toFixed(0) + " veh/h)\n" +
        "A  (inner)  = " + st.A.toFixed(1) + " / (1 − " + st.share.toFixed(3) + ") = <b>" + inner.toFixed(2) + " s</b>  (" + (3600 / inner).toFixed(0) + " veh/h)\n" +
        "A2 (outer)  = " + st.A.toFixed(1) + " / " + st.share.toFixed(3) + "       = <b>" + outer.toFixed(1) + " s</b>  (" + (3600 / outer).toFixed(1) + " veh/h)\n" +
        "Splitting a Poisson stream by a fixed share gives two Poisson streams whose rates add back to the total.";
    }
    slider(c, "combined A interval", 2, 30, 0.1, st.A, function (v) { return v.toFixed(1) + " s"; }, function (v) { st.A = v; draw(); });
    slider(c, "a2Share", 0.01, 0.5, 0.001, st.share, function (v) { return v.toFixed(3); }, function (v) { st.share = v; draw(); });
    var b = el("button", { type: "button" }, c, "Measured 0.054");
    b.addEventListener("click", function () { c.querySelectorAll("input")[1].value = 0.054; c.querySelectorAll("input")[1].dispatchEvent(new Event("input")); });
    var b2 = el("button", { type: "button" }, c, "Old back-calculated 0.18");
    b2.addEventListener("click", function () { c.querySelectorAll("input")[1].value = 0.18; c.querySelectorAll("input")[1].dispatchEvent(new Event("input")); });
    draw();
  };

  // ================================================================ TestCaseLibrary: Compare-101
  // Times copied from TestCasePresets.Compare101(): stop-line crossing times in the clip window; the
  // spawn time in the case is that minus SPAWN_TO_STOPLINE_SEC = 12.29 s (never below 0).
  W.compare101 = function (host) {
    var LANES = [
      ["A", 4, [17.40, 20.00, 23.20, 26.13, 29.70, 39.10]],
      ["Ax", 1, [27, 39]],
      ["B", 4, [36, 59, 60]],
      ["Bx", 2, [53, 55]],
      ["CB", 0, [16, 27, 32, 49, [59.71, 72], [60.51, 72], [61.31, 72], [62.11, 72], [62.91, 72]]],
      ["CA", 0, [39, 43, 72]]
    ];
    var SIG = { A: [[0.3, 40.3]], B: [[0.3, 40.3], [45.3, 65.3]], Bx: [[45.3, 65.3]], CB: [[70.3, 101.3]] };
    var svg = svgBox(host, 900, 300, "Compare-101: one real cycle from the overhead clip");
    var L = 120, R = 880, Tm = 107, sx = function (t) { return L + (R - L) * t / Tm; };
    for (var t = 0; t <= 100; t += 10) { S("line", { x1: sx(t), x2: sx(t), y1: 10, y2: 262, stroke: "var(--rule)" }, svg); S("text", { x: sx(t), y: 280, "text-anchor": "middle", "font-size": 11, text: t + " s" }, svg).style.fill = "var(--muted)"; }
    LANES.forEach(function (ln, i) {
      var y = 20 + i * 41;
      S("text", { x: L - 10, y: y + 15, "text-anchor": "end", "font-size": 13, "font-weight": 700, text: ln[0] }, svg).style.fill = laneColor(ln[0]);
      if (ln[1]) S("text", { x: L - 10, y: y + 29, "text-anchor": "end", "font-size": 10, text: ln[1] + " queued at 0" }, svg).style.fill = "var(--muted)";
      (SIG[ln[0]] || []).forEach(function (g) { S("rect", { x: sx(g[0]), y: y + 2, width: sx(g[1]) - sx(g[0]), height: 22, fill: "var(--go)", opacity: .18, rx: 3 }, svg); });
      ln[2].forEach(function (item) {
        // the CB burst of five: one burst at Spawn(72) with 0.8 s spacing, all crossing at 72 in the clip
        var cross = Array.isArray(item) ? item[1] : item, sp = Array.isArray(item) ? item[0] : Math.max(0, item - 12.29);
        S("line", { x1: sx(sp), x2: sx(cross), y1: y + 13, y2: y + 13, stroke: laneColor(ln[0]), "stroke-width": 1.2, opacity: .6 }, svg);
        S("circle", { cx: sx(sp), cy: y + 13, r: 3, fill: "none", stroke: laneColor(ln[0]) }, svg);
        S("circle", { cx: sx(cross), cy: y + 13, r: 5, fill: laneColor(ln[0]) }, svg);
      });
    });
    S("text", { x: L, y: 296, "font-size": 11, text: "filled dot: crossing time read off the clip · hollow dot: when the case releases the car (12.29 s earlier) · green band: the real signal's green" }, svg).style.fill = "var(--muted)";
  };

  // ================================================================ CustomTestCase: largest remainder
  // Port of CustomTestCase.ResolveCounts.
  W.largestRemainder = function (host) {
    var lanes = ["A", "A2", "Ax", "B", "Bx", "CB", "CA"];
    var share = { A: 20, A2: 0, Ax: 0, B: 20, Bx: 20, CB: 0, CA: 0 }, st = { total: 40 };
    var c = controls(host), out = el("div", { class: "readout" }, host);
    host.insertBefore(c, out);
    slider(c, "total cars", 0, 300, 1, st.total, function (v) { return v + ""; }, function (v) { st.total = v; draw(); });
    var c2 = controls(host); host.insertBefore(c2, out);
    lanes.forEach(function (l) {
      slider(c2, l, 0, 100, 1, share[l], function (v) { return v + "%"; }, function (v) { share[l] = v; draw(); });
    });
    function draw() {
      var sum = 0; lanes.forEach(function (l) { sum += Math.max(0, share[l]); });
      if (sum <= 0.0001) { out.innerHTML = "No lane has a share: no cars."; return; }
      var res = {}, frac = [], assigned = 0, plain = 0;
      lanes.forEach(function (l) {
        var exact = st.total * Math.max(0, share[l]) / sum, fl = Math.floor(exact);
        res[l] = fl; assigned += fl; frac.push([l, exact - fl, exact]); plain += Math.round(exact);
      });
      frac.sort(function (a, b) { return b[1] - a[1]; });
      for (var i = 0; assigned < st.total && i < frac.length; i++, assigned++) res[frac[i][0]]++;
      out.innerHTML = lanes.map(function (l) {
        var f = frac.filter(function (x) { return x[0] === l; })[0];
        return (l + "  ").slice(0, 3) + "  exact " + f[2].toFixed(2).padStart(6) + "  ->  " + res[l];
      }).join("\n") + "\n<b>total " + assigned + "</b> (asked " + st.total + ").  Plain rounding would give " + plain + ".";
    }
    draw();
  };

  // ================================================================ CameraDirector: the shot table
  // Positions and angles from ABACSceneBuilder.MakeShots().
  W.cameraShots = function (host) {
    var jx = -0.52, jz = 0.88;
    var SHOTS = [
      ["Overview", jx, jz, 48, 90, 0, 55], ["Approach A / Ax", jx, (3.0 + 6.5) / 2, 8.5, 10, -90, 62],
      ["Shared Lane", jx, 6.5, 6.0, 8, -90, 55], ["Approach B / Bx", jx, (-4.5 + -1.2) / 2, 8.5, 10, 90, 62],
      ["Bx Bottleneck", jx, -1.2, 6.0, 8, 90, 55], ["ABAC Exit", (1.5 + 3.8) / 2, jz, 8.5, 10, 0, 62]
    ];
    var svg = svgBox(host, 966, 403, "Camera positions over the junction");
    var J = drawJunction(svg, { id: "cam", roi: false }), P = J.P;
    var g = S("g", null, svg), cur = 1;
    var out = el("div", { class: "readout" }, host), c = controls(host);
    host.insertBefore(c, out);
    var bs = SHOTS.map(function (s, i) { var b = el("button", { type: "button" }, c, s[0]); b.addEventListener("click", function () { cur = i; draw(); }); return b; });
    function draw() {
      g.innerHTML = "";
      var s = SHOTS[cur], x = P.x(s[1]), y = P.y(s[2]);
      if (s[4] >= 89) {
        S("circle", { cx: x, cy: y, r: 120, fill: "var(--accent)", opacity: .08, stroke: "var(--accent)", "stroke-dasharray": "4 4" }, g);
      } else {
        // yaw: 0 = north (+z), 90 = east (+x), -90 = west; draw the field of view as a wedge
        var yaw = s[5] * Math.PI / 180, half = s[6] / 2 * Math.PI / 180, len = 330;
        var a1 = yaw - half, a2 = yaw + half;
        var p1 = [x + Math.sin(a1) * len, y - Math.cos(a1) * len], p2 = [x + Math.sin(a2) * len, y - Math.cos(a2) * len];
        S("path", { d: "M" + x + " " + y + " L" + p1[0] + " " + p1[1] + " L" + p2[0] + " " + p2[1] + " Z", fill: "var(--accent)", opacity: .14, stroke: "var(--accent)" }, g);
      }
      S("circle", { cx: x, cy: y, r: 7, fill: "var(--accent)", stroke: "var(--paper)", "stroke-width": 2 }, g);
      bs.forEach(function (b, i) { b.setAttribute("aria-pressed", i === cur ? "true" : "false"); });
      out.innerHTML = "<b>" + s[0] + "</b>   position (" + s[1].toFixed(2) + ", " + s[3] + ", " + s[2].toFixed(2) + ")   pitch " + s[4] + "°   yaw " + s[5] + "°   field of view " + s[6] + "°" +
        (s[4] >= 89 ? "\nStraight down from 48 m: every queue in one frame." : "\nOne mast at the centre of the junction, turning to face an approach, like the camera the project proposes to install.");
    }
    draw();
  };

  // ================================================================ UITheme: palette swatches
  W.palette = function (host) {
    var cols = JSON.parse(host.dataset.colors);
    var grid = el("div", { style: "display:grid;grid-template-columns:repeat(auto-fill,minmax(10.5rem,1fr));gap:.6rem" }, host);
    cols.forEach(function (c) {
      var card = el("div", { style: "border:1px solid var(--rule);border-radius:8px;overflow:hidden;background:var(--paper)" }, grid);
      el("div", { style: "height:2.6rem;background:" + c[1] + ";border-bottom:1px solid var(--rule)" }, card);
      el("div", { style: "padding:.35rem .55rem;font-family:var(--f-mono);font-size:.75rem" }, card, "<b>" + c[0] + "</b><br><span class=\"muted\">" + c[2] + "</span>");
    });
  };

  // ================================================================ shared: horizontal bar chart (two series)
  W.v5bars = function (host) {
    var d = window.TOUR_DATA.v5, rows = host.dataset.by === "lane" ? d.byLane.map(function (r) { return { name: r.lane, fixed: r.fixed, adaptive: r.adaptive }; }) : d.byScenario;
    var max = 0; rows.forEach(function (r) { max = Math.max(max, r.fixed, r.adaptive); });
    max = Math.ceil(max / 20) * 20;
    var W0 = 760, left = 120, right = 90, rowH = 46, H = rows.length * rowH + 50;
    var svg = svgBox(host, W0, H, "Mean wait, Fixed against Adaptive, batch v5");
    var sx = function (v) { return left + (W0 - left - right) * v / max; };
    for (var v = 0; v <= max; v += 20) {
      S("line", { x1: sx(v), x2: sx(v), y1: 10, y2: H - 30, stroke: "var(--rule)" }, svg);
      S("text", { x: sx(v), y: H - 12, "text-anchor": "middle", "font-size": 11, text: v + " s" }, svg).style.fill = "var(--muted)";
    }
    rows.forEach(function (r, i) {
      var y = 14 + i * rowH;
      S("text", { x: left - 10, y: y + 18, "text-anchor": "end", "font-size": 13, "font-weight": 600, text: r.name }, svg);
      S("rect", { x: left, y: y, width: sx(r.fixed) - left, height: 14, fill: "var(--muted)", rx: 2 }, svg);
      S("rect", { x: left, y: y + 17, width: sx(r.adaptive) - left, height: 14, fill: "var(--accent)", rx: 2 }, svg);
      S("text", { x: sx(r.fixed) + 5, y: y + 11, "font-size": 11, "font-family": "var(--f-mono)", text: r.fixed.toFixed(2) }, svg);
      S("text", { x: sx(r.adaptive) + 5, y: y + 28, "font-size": 11, "font-family": "var(--f-mono)", text: r.adaptive.toFixed(2) }, svg);
    });
    var lg = el("div", { class: "legend-kinds" }, host);
    lg.innerHTML = '<span><span style="display:inline-block;width:.8rem;height:.8rem;background:var(--muted);border-radius:2px;vertical-align:-1px"></span> Fixed</span>' +
                   '<span><span style="display:inline-block;width:.8rem;height:.8rem;background:var(--accent);border-radius:2px;vertical-align:-1px"></span> Adaptive (SODA)</span>' +
                   '<span class="muted">mean wait in the detection zone, seconds · 27 pairs per scenario</span>';
  };
})();
