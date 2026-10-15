/* ABAC code tour - shared page behaviour: theme, keyboard navigation, table of contents,
   C# highlighting and the Player used by every animation. No libraries; works from file://. */
(function () {
  "use strict";

  // ---------------- theme ----------------
  var root = document.documentElement;
  function storedTheme() { try { return localStorage.getItem("tour-theme"); } catch (e) { return null; } }
  function storeTheme(v) { try { if (v) localStorage.setItem("tour-theme", v); else localStorage.removeItem("tour-theme"); } catch (e) {} }
  var t0 = storedTheme();
  if (t0 === "light" || t0 === "dark") root.setAttribute("data-theme", t0);

  function themeLabel() {
    var t = root.getAttribute("data-theme");
    return t === "dark" ? "Theme: dark" : t === "light" ? "Theme: light" : "Theme: system";
  }

  // ---------------- C# highlighter ----------------
  var KEYWORDS = "abstract as base bool break byte case catch char checked class const continue decimal default delegate do double else enum event explicit extern false finally fixed float for foreach goto if implicit in int interface internal is lock long namespace new null object operator out override params private protected public readonly ref return sbyte sealed short sizeof stackalloc static string struct switch this throw true try typeof uint ulong unchecked unsafe ushort using virtual void volatile while var yield get set value when".split(" ");
  var KW = {}; KEYWORDS.forEach(function (k) { KW[k] = 1; });
  var TYPES = /^(Vector3|Vector2|Vector4|Quaternion|Mathf|Time|Debug|GameObject|Transform|Renderer|Material|Color|Rect|GUI|GUILayout|GUIStyle|GUIContent|List|Dictionary|HashSet|Queue|IEnumerator|Coroutine|MonoBehaviour|KeyCode|Event|EventType|Application|Random|System|StringBuilder|File|Path|Directory|Screen|Camera|Collider|BoxCollider|Rigidbody|Mesh|MeshFilter|Shader|Texture2D|AssetDatabase|EditorUtility|Undo|Selection|PrefabUtility|EditorSceneManager|Object|FieldInfo|BindingFlags|WaitForSeconds|Light|Bounds|Matrix4x4|GUIUtility|QualitySettings|Gizmos|TextAnchor|FontStyle|RectOffset|GUISkin|HideFlags|[A-Z][A-Za-z0-9]*(Controller|Config|Agent|Spawner|Logger|Zone|Light|Panel|Director|Theme|Case|Lane|Pair|Head|Step|State|Mode|Burst|Schedule|Rate|Def|Aggregate|Kind|Type|Shot|Record|Arrival|Presets|Highlight|Builder|Audit|Phase))$/;

  function esc(s) { return s.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;"); }

  function highlightLine(line, state) {
    var out = "", i = 0, n = line.length;
    if (state.inBlock) {
      var end = line.indexOf("*/");
      if (end < 0) return '<span class="tk-com">' + esc(line) + "</span>";
      out += '<span class="tk-com">' + esc(line.slice(0, end + 2)) + "</span>";
      i = end + 2; state.inBlock = false;
    }
    while (i < n) {
      var c = line[i], rest = line.slice(i), m;
      if (rest.startsWith("//")) { out += '<span class="tk-com">' + esc(rest) + "</span>"; break; }
      if (rest.startsWith("/*")) {
        var e = rest.indexOf("*/", 2);
        if (e < 0) { out += '<span class="tk-com">' + esc(rest) + "</span>"; state.inBlock = true; break; }
        out += '<span class="tk-com">' + esc(rest.slice(0, e + 2)) + "</span>"; i += e + 2; continue;
      }
      if (c === '"' || (c === "$" && line[i + 1] === '"') || (c === "@" && line[i + 1] === '"')) {
        var j = i + (c === '"' ? 1 : 2);
        while (j < n && line[j] !== '"') { if (line[j] === "\\") j++; j++; }
        out += '<span class="tk-str">' + esc(line.slice(i, j + 1)) + "</span>"; i = j + 1; continue;
      }
      if (c === "'") {
        m = /^'(\\.|[^'])'/.exec(rest);
        if (m) { out += '<span class="tk-str">' + esc(m[0]) + "</span>"; i += m[0].length; continue; }
      }
      if (c === "[" && (m = /^\[(Header|Tooltip|Range|ContextMenu|MenuItem|HideInInspector|SerializeField|TextArea|System\.Serializable)\b/.exec(rest))) {
        out += '<span class="tk-attr">' + esc(m[0]) + "</span>"; i += m[0].length; continue;
      }
      if ((m = /^\d+(\.\d+)?f?\b|^0x[0-9A-Fa-f]+u?/.exec(rest)) && !/[A-Za-z_]/.test(line[i - 1] || "")) {
        out += '<span class="tk-num">' + m[0] + "</span>"; i += m[0].length; continue;
      }
      if ((m = /^[A-Za-z_]\w*/.exec(rest))) {
        var w = m[0];
        if (KW[w]) out += '<span class="tk-key">' + w + "</span>";
        else if (TYPES.test(w)) out += '<span class="tk-type">' + w + "</span>";
        else out += w;
        i += w.length; continue;
      }
      out += esc(c); i++;
    }
    return out;
  }

  function highlightAll() {
    document.querySelectorAll("pre.cs").forEach(function (pre) {
      if (pre.dataset.done) return;
      var state = { inBlock: false };
      pre.querySelectorAll(".ln").forEach(function (ln) { ln.innerHTML = highlightLine(ln.textContent, state); });
      pre.dataset.done = "1";
    });
  }

  // ---------------- table of contents ----------------
  function buildToc() {
    var holder = document.getElementById("toc");
    if (!holder) return;
    var hs = document.querySelectorAll(".main h2[id]");
    if (hs.length < 3) { holder.hidden = true; return; }
    var ol = document.createElement("ol");
    hs.forEach(function (h) {
      var li = document.createElement("li"), a = document.createElement("a");
      a.href = "#" + h.id; a.textContent = h.dataset.short || h.textContent.replace(/^\d+\s*/, "");
      li.appendChild(a); ol.appendChild(li);
    });
    holder.appendChild(ol);
  }

  // ---------------- keyboard ----------------
  function sections() { return Array.prototype.slice.call(document.querySelectorAll(".main h1, .main h2[id]")); }
  function jumpSection(dir) {
    var list = sections(), y = window.scrollY + 8, target = null;
    if (dir > 0) { for (var i = 0; i < list.length; i++) if (list[i].getBoundingClientRect().top + window.scrollY > y + 4) { target = list[i]; break; } }
    else { for (var k = list.length - 1; k >= 0; k--) if (list[k].getBoundingClientRect().top + window.scrollY < y - 4) { target = list[k]; break; } }
    if (target) target.scrollIntoView({ behavior: matchMedia("(prefers-reduced-motion: reduce)").matches ? "auto" : "smooth", block: "start" });
  }
  document.addEventListener("keydown", function (e) {
    var tag = (e.target && e.target.tagName) || "";
    if (/INPUT|SELECT|TEXTAREA|BUTTON/.test(tag) || e.metaKey || e.ctrlKey || e.altKey) return;
    var body = document.body;
    if (e.key === "ArrowRight" || e.key === "j") { jumpSection(1); e.preventDefault(); }
    else if (e.key === "ArrowLeft" || e.key === "k") { jumpSection(-1); e.preventDefault(); }
    else if (e.key === "]" && body.dataset.next) { location.href = body.dataset.next; }
    else if (e.key === "[" && body.dataset.prev) { location.href = body.dataset.prev; }
    else if (e.key === "h" && body.dataset.hub) { location.href = body.dataset.hub; }
  });

  // ---------------- Player: play / pause / step / reset for any animation ----------------
  // draw(t) renders the state at time t (seconds). step = seconds per Step press.
  function Player(host, opts) {
    var self = this;
    this.t = 0; this.duration = opts.duration; this.draw = opts.draw; this.speed = opts.speed || 1;
    this.step = opts.step || 1; this.playing = false; this.loop = opts.loop !== false;
    this.fmt = opts.format || function (t) { return "t = " + t.toFixed(1) + " s"; };
    var wrap = document.createElement("span"); wrap.className = "player";
    function mk(label, title, fn) { var b = document.createElement("button"); b.type = "button"; b.textContent = label; b.title = title; b.addEventListener("click", fn); wrap.appendChild(b); return b; }
    this.bPlay = mk("Play", "Play or pause", function () { self.toggle(); });
    mk("Step", "Advance one step", function () { self.pause(); self.seek(self.t + self.step); });
    mk("Back", "Go back one step", function () { self.pause(); self.seek(self.t - self.step); });
    mk("Reset", "Back to the start", function () { self.pause(); self.seek(0); });
    if (opts.speeds) {
      var sel = document.createElement("select"); sel.title = "Playback speed";
      opts.speeds.forEach(function (s) { var o = document.createElement("option"); o.value = s; o.textContent = s + "x"; if (s === self.speed) o.selected = true; sel.appendChild(o); });
      sel.addEventListener("change", function () { self.speed = parseFloat(sel.value); });
      wrap.appendChild(sel);
    }
    this.label = document.createElement("span"); this.label.className = "t"; wrap.appendChild(this.label);
    host.appendChild(wrap);
    this.seek(0);
  }
  Player.prototype.seek = function (t) {
    if (t < 0) t = 0;
    if (t > this.duration) t = this.loop ? 0 : this.duration;
    this.t = t; this.draw(t); this.label.textContent = this.fmt(t);
  };
  Player.prototype.toggle = function () { if (this.playing) this.pause(); else this.play(); };
  Player.prototype.pause = function () { this.playing = false; this.bPlay.textContent = "Play"; this.bPlay.setAttribute("aria-pressed", "false"); };
  Player.prototype.play = function () {
    var self = this;
    if (this.playing) return;
    if (this.t >= this.duration) this.t = 0;
    this.playing = true; this.bPlay.textContent = "Pause"; this.bPlay.setAttribute("aria-pressed", "true");
    var last = performance.now();
    function frame(now) {
      if (!self.playing) return;
      var dt = Math.min(0.1, (now - last) / 1000); last = now;
      var nt = self.t + dt * self.speed;
      if (nt >= self.duration) { if (self.loop) nt = 0; else { self.seek(self.duration); self.pause(); return; } }
      self.seek(nt);
      requestAnimationFrame(frame);
    }
    requestAnimationFrame(frame);
  };

  // ---------------- tiny SVG helper ----------------
  var NS = "http://www.w3.org/2000/svg";
  function S(tag, attrs, parent) {
    var el = document.createElementNS(NS, tag);
    if (attrs) for (var k in attrs) { if (k === "text") el.textContent = attrs[k]; else el.setAttribute(k, attrs[k]); }
    if (parent) parent.appendChild(el);
    return el;
  }
  function css(name) { return getComputedStyle(document.documentElement).getPropertyValue(name).trim(); }

  window.Tour = { Player: Player, S: S, css: css, highlightLine: highlightLine, widgets: {} };

  document.addEventListener("DOMContentLoaded", function () {
    var tb = document.getElementById("theme-btn");
    if (tb) {
      tb.textContent = themeLabel();
      tb.addEventListener("click", function () {
        var cur = root.getAttribute("data-theme");
        var next = cur === "light" ? "dark" : cur === "dark" ? null : (matchMedia("(prefers-color-scheme: dark)").matches ? "light" : "dark");
        if (next) root.setAttribute("data-theme", next); else root.removeAttribute("data-theme");
        storeTheme(next); tb.textContent = themeLabel();
        document.dispatchEvent(new Event("tour-theme"));
      });
    }
    var mb = document.getElementById("menu-btn");
    if (mb) mb.addEventListener("click", function () { var s = document.querySelector(".side"); s.classList.toggle("open"); mb.setAttribute("aria-expanded", s.classList.contains("open")); });
    highlightAll();
    buildToc();
    document.querySelectorAll("[data-widget]").forEach(function (el) {
      var fn = window.Tour.widgets[el.dataset.widget];
      if (fn) { try { fn(el); } catch (err) { el.insertAdjacentHTML("beforeend", '<p class="muted small">This figure could not start: ' + esc(String(err)) + "</p>"); } }
    });
  });
})();
