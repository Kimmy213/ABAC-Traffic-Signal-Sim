using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Build your own test case at runtime and run it immediately — no Inspector, no recompile.
///
/// Everything the preset library can express is editable here: arrival mode, per-lane demand,
/// driver-behaviour mix, run length and RNG seed. The result is an ordinary <see cref="TestCase"/>,
/// so it flows through exactly the same code path as T1-T13 and can be saved back into the
/// control panel's list.
///
/// Opened from the control panel; drawn as its own window so it never overlaps the other HUDs.
/// </summary>
public class CustomTestCase : MonoBehaviour
{
    [Header("References (found automatically if left empty)")]
    public IntersectionConfig config;
    public SimulationControlPanel controlPanel;

    [Header("State")]
    public bool showPanel = false;

    [Tooltip("Hotkey that opens and closes this panel")]
    public KeyCode toggleKey = KeyCode.N;

    // ---------- the case being edited ----------
    private string caseName = "My test";
    private ArrivalMode mode = ArrivalMode.Scripted;
    private float duration = 180f;

    // Scripted: cars per lane and the gap between them
    private readonly Dictionary<string, int> carCount = new Dictionary<string, int>();
    private readonly Dictionary<string, float> spacing = new Dictionary<string, float>();
    private readonly Dictionary<string, float> startAt = new Dictionary<string, float>();

    // Poisson: mean seconds between arrivals per lane
    private readonly Dictionary<string, float> interval = new Dictionary<string, float>();

    // Driver mix, applied to every spawner (must sum to <= 100)
    private float pctAggressive = 25f;
    private float pctNormal = 60f;

    private Vector2 scroll;
    private Rect win = new Rect(340f, 60f, 430f, 560f);
    private bool initialised = false;
    private string status = "";

    void Awake()
    {
        if (config == null) config = FindFirstObjectByType<IntersectionConfig>();
        if (controlPanel == null) controlPanel = FindFirstObjectByType<SimulationControlPanel>();
    }

    void EnsureDefaults()
    {
        if (initialised || config == null) return;
        config.EnsureBuilt();
        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null) continue;
            string n = lane.laneName;
            if (!carCount.ContainsKey(n)) carCount[n] = 8;
            if (!spacing.ContainsKey(n))  spacing[n]  = 2f;
            if (!startAt.ContainsKey(n))  startAt[n]  = 0f;
            if (!interval.ContainsKey(n)) interval[n] = 7f;
        }
        initialised = true;
    }

    /// <summary>Turn the current settings into a real TestCase.</summary>
    public TestCase Build()
    {
        EnsureDefaults();
        TestCase tc = new TestCase
        {
            name = string.IsNullOrEmpty(caseName) ? "Custom" : caseName,
            description = "Custom test case built at runtime.\n" + Summary(),
            mode = mode,
            suggestedDuration = duration,
        };

        if (config == null) return tc;

        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null) continue;
            string n = lane.laneName;

            if (mode == ArrivalMode.Poisson)
            {
                // A very large interval is how this project expresses "lane closed" — a rate of
                // zero would divide by zero inside CarSpawner, so the closed case is a huge mean.
                tc.rates.Add(new LaneArrivalRate(n, Mathf.Max(0.2f, interval[n])));
            }
            else if (carCount[n] > 0)
            {
                tc.schedules.Add(new LaneSchedule(n,
                    new ScheduledBurst(startAt[n], carCount[n], Mathf.Max(0.2f, spacing[n]))));
            }
        }
        return tc;
    }

    string Summary()
    {
        if (config == null) return "";
        var parts = new List<string>();
        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null) continue;
            string n = lane.laneName;
            if (mode == ArrivalMode.Poisson) parts.Add($"{n}:{interval[n]:0.#}s");
            else if (carCount[n] > 0) parts.Add($"{n}:{carCount[n]}");
        }
        return string.Join("  ", parts);
    }

    /// <summary>Apply the driver-behaviour mix to every spawner in the scene.</summary>
    void ApplyDriverMix()
    {
        if (config == null) return;
        float agg = Mathf.Clamp(pctAggressive, 0f, 100f);
        float nor = Mathf.Clamp(pctNormal, 0f, 100f - agg);
        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null || lane.spawner == null) continue;
            lane.spawner.aggressivePercent = agg;
            lane.spawner.normalPercent = nor;
        }
    }

    // ===================== UI =====================

    void OnGUI()
    {
        Event e = Event.current;
        if (e != null && e.type == EventType.KeyDown && e.keyCode == toggleKey)
        {
            showPanel = !showPanel;
            e.Use();
        }
        if (!showPanel || !Application.isPlaying) return;

        EnsureDefaults();
        UITheme.Begin();
        win = GUI.Window(9318, win, DrawWindow, "");
        UITheme.End();
    }

    void DrawWindow(int id)
    {
        float w = win.width - 24f;
        GUILayout.BeginArea(new Rect(12f, 10f, w, win.height - 20f));

        GUILayout.BeginHorizontal();
        GUILayout.Label("CUSTOM TEST CASE", UITheme.Title, GUILayout.Height(20));
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Close", GUILayout.Width(60))) showPanel = false;
        GUILayout.EndHorizontal();

        UITheme.Rule(GUILayoutUtility.GetRect(w, 1f));
        GUILayout.Space(6f);

        // ---- name + mode + duration ----
        GUILayout.BeginHorizontal();
        GUILayout.Label("Name", UITheme.Body, GUILayout.Width(70));
        caseName = GUILayout.TextField(caseName);
        GUILayout.EndHorizontal();

        GUILayout.Space(4f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Arrivals", UITheme.Body, GUILayout.Width(70));
        if (GUILayout.Toggle(mode == ArrivalMode.Scripted, "  Scripted (exact counts)", GUILayout.Width(170)))
            mode = ArrivalMode.Scripted;
        if (GUILayout.Toggle(mode == ArrivalMode.Poisson, "  Poisson (random)"))
            mode = ArrivalMode.Poisson;
        GUILayout.EndHorizontal();

        GUILayout.BeginHorizontal();
        GUILayout.Label("Duration", UITheme.Body, GUILayout.Width(70));
        duration = Mathf.Round(GUILayout.HorizontalSlider(duration, 30f, 900f));
        GUILayout.Label($"{duration:F0} s", UITheme.Value, GUILayout.Width(60));
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        UITheme.Rule(GUILayoutUtility.GetRect(w, 1f));
        GUILayout.Space(6f);

        // ---- per-lane demand ----
        GUILayout.Label(mode == ArrivalMode.Scripted
            ? "PER-LANE TRAFFIC  (cars · gap · start)"
            : "PER-LANE TRAFFIC  (mean seconds between cars)", UITheme.H2);
        GUILayout.Space(2f);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(232f));
        if (config != null)
        {
            foreach (ApproachLane lane in config.lanes)
            {
                if (lane == null) continue;
                DrawLaneRow(lane.laneName, lane.isSignalized);
            }
        }
        GUILayout.EndScrollView();

        GUILayout.Space(6f);
        UITheme.Rule(GUILayoutUtility.GetRect(w, 1f));
        GUILayout.Space(6f);

        // ---- driver mix ----
        GUILayout.Label("DRIVER BEHAVIOUR MIX", UITheme.H2);
        pctAggressive = DrawPercent("Aggressive", pctAggressive, 100f);
        pctNormal     = DrawPercent("Normal",     pctNormal, 100f - pctAggressive);
        float cautious = Mathf.Max(0f, 100f - pctAggressive - pctNormal);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Cautious", UITheme.Body, GUILayout.Width(84));
        UITheme.Bar(GUILayoutUtility.GetRect(120f, 8f), cautious / 100f, UITheme.Neutral);
        GUILayout.Label($"{cautious:F0}%", UITheme.Value, GUILayout.Width(46));
        GUILayout.EndHorizontal();

        GUILayout.Space(10f);

        // ---- actions ----
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("▶  Run this now", GUILayout.Height(28)))
        {
            ApplyDriverMix();
            TestCase tc = Build();
            if (controlPanel != null)
            {
                controlPanel.RunCustomCase(tc);
                status = $"Running \"{tc.name}\"";
            }
            else status = "SimulationControlPanel not found";
        }
        if (GUILayout.Button("Add to list", GUILayout.Height(28), GUILayout.Width(110)))
        {
            if (controlPanel != null)
            {
                controlPanel.testCases.Add(Build());
                status = "Added to the test case list";
            }
        }
        GUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(status))
        {
            GUILayout.Space(4f);
            GUILayout.Label(status, UITheme.Dim);
        }

        GUILayout.EndArea();
        GUI.DragWindow(new Rect(0, 0, win.width, 26f));
    }

    void DrawLaneRow(string lane, bool signalised)
    {
        GUILayout.BeginHorizontal();

        Rect dot = GUILayoutUtility.GetRect(10f, 10f, GUILayout.Width(10));
        UITheme.Dot(new Rect(dot.x, dot.y + 4f, 8f, 8f), UITheme.LaneColor(lane));

        GUILayout.Label(lane + (signalised ? "" : " *"), UITheme.Mono, GUILayout.Width(42));

        if (mode == ArrivalMode.Scripted)
        {
            carCount[lane] = Mathf.RoundToInt(
                GUILayout.HorizontalSlider(carCount[lane], 0f, 60f, GUILayout.Width(96)));
            GUILayout.Label(carCount[lane].ToString(), UITheme.Value, GUILayout.Width(30));

            spacing[lane] = Mathf.Round(
                GUILayout.HorizontalSlider(spacing[lane], 0.5f, 10f, GUILayout.Width(70)) * 10f) / 10f;
            GUILayout.Label($"{spacing[lane]:0.#}s", UITheme.Body, GUILayout.Width(38));

            startAt[lane] = Mathf.Round(
                GUILayout.HorizontalSlider(startAt[lane], 0f, 180f, GUILayout.Width(60)));
            GUILayout.Label($"t{startAt[lane]:F0}", UITheme.Dim, GUILayout.Width(36));
        }
        else
        {
            interval[lane] = Mathf.Round(
                GUILayout.HorizontalSlider(interval[lane], 1f, 60f, GUILayout.Width(190)) * 10f) / 10f;
            GUILayout.Label($"{interval[lane]:0.#}s", UITheme.Value, GUILayout.Width(46));
            // veh/hr is the unit the field measurements are quoted in, so show both
            GUILayout.Label($"{3600f / Mathf.Max(0.2f, interval[lane]):F0}/hr", UITheme.Dim, GUILayout.Width(58));
        }

        GUILayout.EndHorizontal();
    }

    float DrawPercent(string label, float value, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, UITheme.Body, GUILayout.Width(84));
        value = Mathf.Round(GUILayout.HorizontalSlider(value, 0f, max, GUILayout.Width(120)));
        GUILayout.Label($"{value:F0}%", UITheme.Value, GUILayout.Width(46));
        GUILayout.EndHorizontal();
        return value;
    }
}
