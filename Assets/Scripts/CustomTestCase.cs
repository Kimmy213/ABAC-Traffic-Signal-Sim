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

    // ---------- แจกรถจากยอดรวม แทนที่จะกรอกทีละเลน ----------
    /// <summary>true = กรอกยอดรวมครั้งเดียวแล้วแบ่งเป็น % ต่อเลน · false = กรอกจำนวนทีละเลนแบบเดิม</summary>
    private bool splitByShare = true;
    private int totalCars = 40;
    private readonly Dictionary<string, float> sharePct = new Dictionary<string, float>();

    /// <summary>ช่องว่างระหว่างรถในเลนเดียวกัน — คงที่ หรือสุ่มในช่วง</summary>
    private enum GapMode { Constant, RandomRange }
    private GapMode gapMode = GapMode.Constant;
    private float gapConstant = 2f;      // ทุก ๆ กี่วินาที
    private float gapMin = 1f;           // สุ่มช่วงต่ำสุด
    private float gapMax = 5f;           // สุ่มช่วงสูงสุด
    private int   gapSeed = 1;           // ให้ผลซ้ำได้

    // Poisson: mean seconds between arrivals per lane
    private readonly Dictionary<string, float> interval = new Dictionary<string, float>();

    // Driver mix, applied to every spawner (must sum to <= 100)
    private float pctAggressive = 25f;
    private float pctNormal = 60f;

    private Vector2 scroll;
    private Rect win = new Rect(340f, 60f, 440f, 690f);
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
            if (!sharePct.ContainsKey(n)) sharePct[n] = lane.isSignalized ? 20f : 0f;
        }
        initialised = true;
    }

    /// <summary>
    /// แปลง % ต่อเลน เป็นจำนวนคันจริง โดยให้ผลรวมเท่ากับ totalCars เป๊ะ
    ///
    /// ใช้วิธี largest remainder: ปัดลงก่อนทุกเลน แล้วแจกเศษที่เหลือให้เลนที่มีทศนิยมมากสุดตามลำดับ
    /// ปัดแบบธรรมดาจะทำให้ผลรวมคลาดจากยอดที่ผู้ใช้ตั้งไว้ ซึ่งสับสนเวลาอ่านผล
    /// </summary>
    Dictionary<string, int> ResolveCounts()
    {
        var result = new Dictionary<string, int>();
        if (config == null) return result;

        if (!splitByShare)
        {
            foreach (ApproachLane lane in config.lanes)
                if (lane != null) result[lane.laneName] = carCount[lane.laneName];
            return result;
        }

        float sum = 0f;
        foreach (ApproachLane lane in config.lanes)
            if (lane != null) sum += Mathf.Max(0f, sharePct[lane.laneName]);

        if (sum <= 0.0001f)
        {
            foreach (ApproachLane lane in config.lanes)
                if (lane != null) result[lane.laneName] = 0;
            return result;
        }

        var frac = new List<KeyValuePair<string, float>>();
        int assigned = 0;
        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null) continue;
            string n = lane.laneName;
            float exact = totalCars * Mathf.Max(0f, sharePct[n]) / sum;
            int floor = Mathf.FloorToInt(exact);
            result[n] = floor;
            assigned += floor;
            frac.Add(new KeyValuePair<string, float>(n, exact - floor));
        }

        frac.Sort((a, b) => b.Value.CompareTo(a.Value));
        for (int i = 0; assigned < totalCars && i < frac.Count; i++, assigned++)
            result[frac[i].Key]++;

        return result;
    }

    /// <summary>ยอดรวมที่จะได้จริงหลังปัดเศษ — ใช้แสดงในแผงให้เห็นก่อนกดรัน</summary>
    int ResolvedTotal()
    {
        int t = 0;
        foreach (var kv in ResolveCounts()) t += kv.Value;
        return t;
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

        tc.userCreated = true;
        if (config == null) return tc;

        Dictionary<string, int> counts = ResolveCounts();

        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null) continue;
            string n = lane.laneName;

            if (mode == ArrivalMode.Poisson)
            {
                // A very large interval is how this project expresses "lane closed" — a rate of
                // zero would divide by zero inside CarSpawner, so the closed case is a huge mean.
                tc.rates.Add(new LaneArrivalRate(n, Mathf.Max(0.2f, interval[n])));
                continue;
            }

            int cars = counts.ContainsKey(n) ? counts[n] : 0;
            if (cars <= 0) continue;

            if (gapMode == GapMode.Constant)
            {
                // ช่องว่างคงที่ = burst เดียวปล่อย N คัน ห่างกันคันละ gapConstant
                tc.schedules.Add(new LaneSchedule(n,
                    new ScheduledBurst(startAt[n], cars, Mathf.Max(0.2f, gapConstant))));
            }
            else
            {
                // สุ่มช่วง = หนึ่ง burst ต่อหนึ่งคัน เวลาสะสมไปเรื่อย ๆ
                // ทำแบบนี้แทนที่จะเพิ่ม ArrivalMode ใหม่ เพราะไม่ต้องแตะ CarSpawner เลย
                // และเดินผ่านเส้นทาง Scripted เดิมที่มีเทสคุมอยู่แล้ว
                float lo = Mathf.Min(gapMin, gapMax), hi = Mathf.Max(gapMin, gapMax);
                lo = Mathf.Max(0.2f, lo); hi = Mathf.Max(lo, hi);

                // seed ผูกกับชื่อเลนด้วย StableHash เพื่อให้แต่ละเลนสุ่มคนละชุด
                // แต่ยังได้ผลเดิมทุกครั้งที่ใช้ seed เดิม — ไม่แตะ Random ของ Unity
                var rng = new System.Random(gapSeed * 7919 + IntersectionConfig.StableHash(n));

                var bursts = new List<ScheduledBurst>();
                float t = startAt[n];
                for (int i = 0; i < cars; i++)
                {
                    bursts.Add(new ScheduledBurst(t, 1, 0f));
                    t += lo + (float)rng.NextDouble() * (hi - lo);
                }
                tc.schedules.Add(new LaneSchedule(n, bursts.ToArray()));
            }
        }
        return tc;
    }

    string Summary()
    {
        if (config == null) return "";
        var parts = new List<string>();
        Dictionary<string, int> counts = ResolveCounts();
        foreach (ApproachLane lane in config.lanes)
        {
            if (lane == null) continue;
            string n = lane.laneName;
            if (mode == ArrivalMode.Poisson) { parts.Add($"{n}:{interval[n]:0.#}s"); continue; }
            int c = counts.ContainsKey(n) ? counts[n] : 0;
            if (c > 0) parts.Add($"{n}:{c}");
        }
        string gap = mode == ArrivalMode.Poisson ? ""
                   : gapMode == GapMode.Constant ? $"  · ห่างคงที่ {gapConstant:0.#}s"
                   : $"  · ห่างสุ่ม {Mathf.Min(gapMin,gapMax):0.#}-{Mathf.Max(gapMin,gapMax):0.#}s (seed {gapSeed})";
        return string.Join("  ", parts) + gap;
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

        // ---- how the cars are shared out, and how far apart they arrive ----
        if (mode == ArrivalMode.Scripted)
        {
            GUILayout.Label("HOW MANY CARS", UITheme.H2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(splitByShare, "  ยอดรวม + แบ่ง %", GUILayout.Width(150))) splitByShare = true;
            if (GUILayout.Toggle(!splitByShare, "  กรอกทีละเลน")) splitByShare = false;
            GUILayout.EndHorizontal();

            if (splitByShare)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("รถทั้งหมด", UITheme.Body, GUILayout.Width(70));
                totalCars = Mathf.RoundToInt(
                    GUILayout.HorizontalSlider(totalCars, 0f, 300f, GUILayout.Width(180)));
                GUILayout.Label($"{totalCars} คัน", UITheme.Value, GUILayout.Width(66));
                GUILayout.EndHorizontal();

                float sum = 0f;
                if (config != null)
                    foreach (ApproachLane lane in config.lanes)
                        if (lane != null) sum += Mathf.Max(0f, sharePct[lane.laneName]);

                GUILayout.Label(sum <= 0.0001f
                    ? "ยังไม่ได้ใส่ % ให้เลนไหนเลย — จะไม่มีรถเกิดขึ้น"
                    : $"รวม % ที่ใส่ = {sum:F0}  ·  ไม่ต้องรวมได้ 100 ระบบหารตามสัดส่วนให้  ·  แจกจริง {ResolvedTotal()} คัน",
                    UITheme.Dim);
            }

            GUILayout.Space(8f);
            GUILayout.Label("รถมาห่างกันแค่ไหน", UITheme.H2);
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(gapMode == GapMode.Constant, "  คงที่", GUILayout.Width(110)))
                gapMode = GapMode.Constant;
            if (GUILayout.Toggle(gapMode == GapMode.RandomRange, "  สุ่มในช่วง"))
                gapMode = GapMode.RandomRange;
            GUILayout.EndHorizontal();

            if (gapMode == GapMode.Constant)
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("ทุก ๆ", UITheme.Body, GUILayout.Width(70));
                gapConstant = Mathf.Round(
                    GUILayout.HorizontalSlider(gapConstant, 0.2f, 10f, GUILayout.Width(180)) * 10f) / 10f;
                GUILayout.Label($"{gapConstant:0.#} s", UITheme.Value, GUILayout.Width(66));
                GUILayout.EndHorizontal();
                GUILayout.Label("รถทุกคันในเลนเดียวกันมาห่างเท่ากันหมด ผลซ้ำได้ทุกครั้ง", UITheme.Dim);
            }
            else
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label("ต่ำสุด", UITheme.Body, GUILayout.Width(70));
                gapMin = Mathf.Round(
                    GUILayout.HorizontalSlider(gapMin, 0.2f, 10f, GUILayout.Width(180)) * 10f) / 10f;
                GUILayout.Label($"{gapMin:0.#} s", UITheme.Value, GUILayout.Width(66));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("สูงสุด", UITheme.Body, GUILayout.Width(70));
                gapMax = Mathf.Round(
                    GUILayout.HorizontalSlider(gapMax, 0.2f, 20f, GUILayout.Width(180)) * 10f) / 10f;
                GUILayout.Label($"{gapMax:0.#} s", UITheme.Value, GUILayout.Width(66));
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                GUILayout.Label("seed", UITheme.Body, GUILayout.Width(70));
                gapSeed = Mathf.RoundToInt(
                    GUILayout.HorizontalSlider(gapSeed, 1f, 99f, GUILayout.Width(180)));
                GUILayout.Label($"{gapSeed}", UITheme.Value, GUILayout.Width(66));
                GUILayout.EndHorizontal();

                GUILayout.Label($"คันถัดไปมาในอีก {Mathf.Min(gapMin,gapMax):0.#}-{Mathf.Max(gapMin,gapMax):0.#} วินาที สุ่มทีละคัน " +
                                "· seed เดิมได้ลำดับเดิมทุกครั้ง", UITheme.Dim);
            }

            GUILayout.Space(8f);
            UITheme.Rule(GUILayoutUtility.GetRect(w, 1f));
            GUILayout.Space(6f);
        }

        // ---- per-lane demand ----
        GUILayout.Label(mode == ArrivalMode.Scripted
            ? (splitByShare ? "PER-LANE TRAFFIC  (ส่วนแบ่ง % · เวลาเริ่ม)"
                            : "PER-LANE TRAFFIC  (cars · start)")
            : "PER-LANE TRAFFIC  (mean seconds between cars)", UITheme.H2);
        GUILayout.Space(2f);

        scroll = GUILayout.BeginScrollView(scroll, GUILayout.Height(190f));
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

        if (mode == ArrivalMode.Scripted && splitByShare)
        {
            sharePct[lane] = Mathf.Round(
                GUILayout.HorizontalSlider(sharePct[lane], 0f, 100f, GUILayout.Width(110)));
            GUILayout.Label($"{sharePct[lane]:F0}%", UITheme.Value, GUILayout.Width(40));

            Dictionary<string, int> c = ResolveCounts();
            int got = c.ContainsKey(lane) ? c[lane] : 0;
            GUILayout.Label($"= {got} คัน", UITheme.Body, GUILayout.Width(56));

            startAt[lane] = Mathf.Round(
                GUILayout.HorizontalSlider(startAt[lane], 0f, 180f, GUILayout.Width(52)));
            GUILayout.Label($"t{startAt[lane]:F0}", UITheme.Dim, GUILayout.Width(34));
        }
        else if (mode == ArrivalMode.Scripted)
        {
            carCount[lane] = Mathf.RoundToInt(
                GUILayout.HorizontalSlider(carCount[lane], 0f, 60f, GUILayout.Width(126)));
            GUILayout.Label(carCount[lane].ToString(), UITheme.Value, GUILayout.Width(34));

            startAt[lane] = Mathf.Round(
                GUILayout.HorizontalSlider(startAt[lane], 0f, 180f, GUILayout.Width(80)));
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
