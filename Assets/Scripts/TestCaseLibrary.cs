using UnityEngine;
using System.Collections.Generic;

/// <summary>How vehicles are released into the system</summary>
public enum ArrivalMode
{
    /// <summary>Poisson random arrivals - used for the 135-scenario statistical experiment</summary>
    Poisson,
    /// <summary>Hand-written schedule per lane - identical every run, for demos and logic checks</summary>
    Scripted
}

/// <summary>
/// "Release N cars at second X, Y seconds apart"
/// Times are measured from the start of the run.
/// </summary>
[System.Serializable]
public class ScheduledBurst
{
    [Tooltip("Second at which the burst starts, measured from the start of the run")]
    public float atTime = 0f;

    [Tooltip("How many cars to release")]
    public int carCount = 1;

    [Tooltip("Seconds between consecutive cars")]
    public float spacing = 2f;

    [Tooltip("true = draw a driver profile from the configured mix / false = use the profile set below")]
    public bool randomDriver = true;

    [Tooltip("Used when randomDriver = false")]
    public CarAgent.DriverType driverType = CarAgent.DriverType.Normal;

    public ScheduledBurst() { }
    public ScheduledBurst(float t, int n, float sp)
    {
        atTime = t; carCount = n; spacing = sp;
    }
    public ScheduledBurst(float t, int n, float sp, CarAgent.DriverType type)
    {
        atTime = t; carCount = n; spacing = sp;
        randomDriver = false; driverType = type;
    }

    /// <summary>Second at which the last car of this burst is released</summary>
    public float EndTime => atTime + Mathf.Max(0, carCount - 1) * spacing;
}

/// <summary>Release schedule for one lane</summary>
[System.Serializable]
public class LaneSchedule
{
    public string laneName = "A";
    public List<ScheduledBurst> bursts = new List<ScheduledBurst>();

    /// <summary>
    /// Cars already standing at the stop line at second 0 of the run.
    ///
    /// Different from ScheduledBurst(0, n, gap), which releases cars at the spawn point and
    /// waits for them to drive in, so the queue takes several seconds to form. This places them
    /// directly where they would come to rest. Needed for cases built from a clip that starts
    /// with vehicles already waiting at a red light.
    /// </summary>
    public int initialQueue = 0;

    public LaneSchedule() { }
    public LaneSchedule(string lane, params ScheduledBurst[] items)
    {
        laneName = lane;
        bursts = new List<ScheduledBurst>(items);
    }

    public int TotalCars
    {
        get
        {
            int total = 0;
            foreach (var b in bursts) total += b.carCount;
            return total;
        }
    }
}

/// <summary>Poisson arrival rate for one lane</summary>
[System.Serializable]
public class LaneArrivalRate
{
    public string laneName = "A";
    [Tooltip("Mean seconds between cars (smaller = heavier traffic)")]
    public float meanInterval = 7f;

    public LaneArrivalRate() { }
    public LaneArrivalRate(string lane, float interval)
    {
        laneName = lane; meanInterval = interval;
    }
}

/// <summary>
/// One test case, selectable from the Control Panel at runtime
/// </summary>
[System.Serializable]
public class TestCase
{
    public string name = "Test";

    [TextArea(2, 5)]
    [Tooltip("What this case checks - shown on the Control Panel")]
    public string description = "";

    public ArrivalMode mode = ArrivalMode.Scripted;

    [Tooltip("Suggested run length in seconds - 0 = unlimited")]
    public float suggestedDuration = 180f;

    /// <summary>
    /// true = created by the user at runtime through the CustomTestCase panel.
    /// Decides whether the delete button appears - the built-in cases T1-T13 cannot be
    /// deleted because the documents and the test suites refer to them by name.
    /// </summary>
    public bool userCreated = false;

    [Tooltip("Used when mode = Poisson")]
    public List<LaneArrivalRate> rates = new List<LaneArrivalRate>();

    [Tooltip("Used when mode = Scripted")]
    public List<LaneSchedule> schedules = new List<LaneSchedule>();

    public float GetInterval(string lane, float fallback)
    {
        foreach (var r in rates)
            if (r != null && r.laneName == lane) return r.meanInterval;
        return fallback;
    }

    public List<ScheduledBurst> GetSchedule(string lane)
    {
        foreach (var s in schedules)
            if (s != null && s.laneName == lane) return s.bursts;
        return new List<ScheduledBurst>();
    }

    /// <summary>Cars already queued at the start of the run for this lane - 0 if unset</summary>
    public int GetInitialQueue(string lane)
    {
        foreach (var s in schedules)
            if (s != null && s.laneName == lane) return Mathf.Max(0, s.initialQueue);
        return 0;
    }

    /// <summary>Total cars this case will release (Scripted only)</summary>
    public int TotalScriptedCars
    {
        get
        {
            int total = 0;
            foreach (var s in schedules) if (s != null) total += s.TotalCars;
            return total;
        }
    }

    /// <summary>One-line summary of cars per lane, shown on the panel</summary>
    public string ShortSummary()
    {
        if (mode == ArrivalMode.Poisson)
        {
            var parts = new List<string>();
            foreach (var r in rates) if (r != null) parts.Add($"{r.laneName}:{r.meanInterval:0.#}s");
            return "Poisson  " + string.Join("  ", parts);
        }
        else
        {
            var parts = new List<string>();
            foreach (var s in schedules) if (s != null && s.TotalCars > 0) parts.Add($"{s.laneName}:{s.TotalCars}");
            return $"Scripted {TotalScriptedCars} cars  ({string.Join("  ", parts)})";
        }
    }
}

/// <summary>
/// Built-in test cases.
///
/// T1-T7  = Scripted (deterministic) - identical every run, for demos and logic checks
/// T8-T12 = Poisson - the same set used for the 135-scenario statistics
/// </summary>
public static class TestCasePresets
{
    // Standard Poisson rate buckets
    const float VERY_HIGH = 1.5f, HIGH = 2.5f, MEDIUM = 4f, LOW = 7f, VERY_LOW = 15f;
    // A genuinely closed lane (confirmed by the security staff), not merely a quiet one.
    // Uses a very large mean interval rather than zero: CarSpawner derives the Poisson rate by
    // dividing by the interval, so a literal 0 would produce an unbounded rate instead.
    const float CLOSED = 999999f;

    // Share of through traffic that uses the outer lane (A2, shared with Ax).
    //
    // History: guessed 50/50 -> back-calculated 18% -> now 5.4% from a direct count (SP1_HANDOFF_v2 section 5).
    // Itim counted clip A_morning: 56 through vehicles, 53 in the inner lane, only 3 in the shared lane.
    // This is counted directly rather than inferred from a blocking rate, so it is far more reliable.
    //
    // Read together with the late-merge behaviour: those 3 vehicles changed lane with only about
    // five car lengths left before the turn, inserting at the head of the queue. One of them stops
    // every turning vehicle behind it for the rest of the red. The result is "rare but nearly
    // worst-case each time", not "frequent but mild". Both give the same mean and completely
    // different queue dynamics - see the note in ABACSceneBuilder.
    public const float A2_SHARE = 0.054f;   // must always equal ScenarioManager.MEASURED_A2_SHARE

    public static List<TestCase> BuildDefault()
    {
        var list = new List<TestCase>();

        // ---------- T1 ----------
        list.Add(new TestCase
        {
            name = "T1 \u00B7 Single lane, 10 cars",
            description = "10 cars   A 10",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 120f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A", new ScheduledBurst(0f, 10, 1.5f)),
            }
        });

        // ---------- T2 ----------
        list.Add(new TestCase
        {
            name = "T2 \u00B7 All lanes equal",
            description = "56 cars   A 8 \u00B7 A2 8 \u00B7 Ax 8 \u00B7 B 8 \u00B7 Bx 8 \u00B7 CB 8 \u00B7 CA 8",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 180f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A",  new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("A2", new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("Ax", new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("B",  new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("Bx", new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("CB", new ScheduledBurst(0f, 8, 2f)),
                new LaneSchedule("CA", new ScheduledBurst(0f, 8, 2f)),
            }
        });

        // ---------- T3 ----------
        list.Add(new TestCase
        {
            name = "T3 \u00B7 Bx vs CB conflict",
            description = "30 cars   A 6 \u00B7 Bx 12 \u00B7 CB 12",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 180f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("Bx", new ScheduledBurst(0f, 12, 1.5f)),
                new LaneSchedule("CB", new ScheduledBurst(0f, 12, 1.5f)),
                new LaneSchedule("A",  new ScheduledBurst(0f,  6, 3f)),
            }
        });

        // ---------- T4 ----------
        list.Add(new TestCase
        {
            name = "T4 \u00B7 Starvation test",
            description = "81 cars   A 40 \u00B7 B 40 \u00B7 CB 1",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 180f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A",  new ScheduledBurst(0f, 40, 2f)),
                new LaneSchedule("B",  new ScheduledBurst(0f, 40, 2f)),
                new LaneSchedule("CB", new ScheduledBurst(5f,  1, 1f)),
            }
        });

        // ---------- T5 ----------
        list.Add(new TestCase
        {
            name = "T5 \u00B7 Campus event in/out",
            description = "100 cars   A 10 \u00B7 Ax 20 \u00B7 B 10 \u00B7 Bx 20 \u00B7 CB 20 \u00B7 CA 20",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 300f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("Ax", new ScheduledBurst(0f, 20, 1.2f)),
                new LaneSchedule("Bx", new ScheduledBurst(0f, 20, 1.2f)),
                new LaneSchedule("A",  new ScheduledBurst(0f, 10, 4f)),
                new LaneSchedule("B",  new ScheduledBurst(0f, 10, 4f)),
                new LaneSchedule("CB", new ScheduledBurst(90f, 20, 1.2f)),
                new LaneSchedule("CA", new ScheduledBurst(90f, 20, 1.2f)),
            }
        });

        // ---------- T6 ----------
        list.Add(new TestCase
        {
            name = "T6 \u00B7 Unsignalised lanes (Ax, CA)",
            description = "30 cars   Ax 15 \u00B7 CA 15",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 120f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("Ax", new ScheduledBurst(0f, 15, 2f)),
                new LaneSchedule("CA", new ScheduledBurst(0f, 15, 2f)),
            }
        });

        // ---------- T7 ----------
        list.Add(new TestCase
        {
            name = "T7 \u00B7 Driver profiles",
            description = "18 cars   A 12 \u00B7 B 6",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 150f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A",
                    new ScheduledBurst(0f,  6, 2.5f, CarAgent.DriverType.Aggressive),
                    new ScheduledBurst(40f, 6, 2.5f, CarAgent.DriverType.Cautious)),
                new LaneSchedule("B", new ScheduledBurst(0f, 6, 5f)),
            }
        });

        // ---------- T13 ----------
        list.Add(new TestCase
        {
            name = "T13 · Shared Lane Blocking Demo",
            description = "85 cars   A2 30 \u00B7 Ax 40 \u00B7 B 15",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 300f,
            schedules = new List<LaneSchedule> {
                new LaneSchedule("A2", new ScheduledBurst(0f, 30, 4f)),
                new LaneSchedule("Ax", new ScheduledBurst(2f, 40, 3f)),
                new LaneSchedule("B",  new ScheduledBurst(0f, 15, 5f)),
            }
        });

        // ---------- T8-T12 : Poisson (the same set used for the real experiment) ----------
        // T8/T10 use rates Itim measured from drone clips (5 min, 13 and 22 Jul 2026) instead of guesses.
        // CB/CA have no measurement yet, so they keep the old buckets (see Questions_for_Itim.md item 2).
        // T9/T11/T12 have no measurement at all - those sessions were never filmed - and are assumptions.
        //
        // The A parameter passed to Poisson() is the combined through-traffic rate before the lane
        // split; the function halves it between A and A2 below (standard thinning of a Poisson
        // process: half the vehicles means double the mean interval).
        list.Add(Poisson("T8 · MorningPeak",
            18.8f, 5.1f, 9.0f, 9.6f, CLOSED, CLOSED));
        list.Add(Poisson("T9 · EveningPeak",
            MEDIUM, LOW, HIGH, LOW, 8.4f, 13.4f));
        list.Add(Poisson("T10 · MiddayNormal",
            29.0f, 9.7f, 20.5f, 21.4f, LOW, LOW));
        // ABACEvent rescaled 18 Sep 2026 - see the note in ScenarioManager.LoadABACScenarioPreset().
        // The old VERY_HIGH rates put lane Bx at v/c 9.6 against the calibrated vehicle model and
        // gridlocked the junction outright; these keep Bx the bottleneck at 1.80 without killing it.
        list.Add(Poisson("T11 · ABACEvent",
            6.5f, 3.2f, 4.0f, 8.0f, 8.0f, 2.5f));
        list.Add(Poisson("T12 · Night",
            VERY_LOW, VERY_LOW, VERY_LOW, VERY_LOW, VERY_LOW, VERY_LOW));

        // ---------- Compare-101 : one real cycle from the overhead clip ----------
        list.Add(Compare101());

        return list;
    }

    /// <summary>
    /// Compare-101 - one real signal cycle from the overhead clip, used to compare what the
    /// real signal did against what SODA would have done at the same second.
    ///
    /// Source: DJI_20260824154642_0021_D.MP4, 24 Aug 2026, window 1:02-2:49 of the clip
    ///         = seconds 62-169, exactly one cycle (measured period 107.17 s).
    ///         Head A turns green at 62.3 s, which is the start of the window.
    ///
    /// Real signal during this cycle (from signal_timeline.json cycle 0, relative to window start):
    ///     A + A2 + B   green    0.3 -  40.3
    ///     B  + Bx      green   45.3 -  65.3
    ///     CB           green   70.3 - 101.3
    /// </summary>

    /// <summary>Free-flow travel time from the spawn point to the stop line, in seconds.
    ///
    /// Important: times read off the clip are "when the front bumper crosses the stop line"
    /// (counting rule 1 in HANDOFF_tally_overhead), but ScheduledBurst.atTime is "when the car
    /// is released at the spawn point". The two are a whole lane length apart, so using clip
    /// times directly makes every car reach the line this much later than it really did.
    ///
    /// Distance = SPAWN_FAR 200 m - |STOP_A_X| 10.5 m = 189.5 m (B/Bx and CB/CA differ by under 1 m).
    /// Approach speed weighted by the 25/60/15 driver mix
    /// = 0.25 x 16.67 + 0.60 x 15.28 + 0.15 x 13.89 = 15.42 m/s.
    /// 189.5 / 15.42 = 12.29 s. GeometryTest section 13 ties this to the real scene coordinates.</summary>
    public const float SPAWN_TO_STOPLINE_SEC = 12.29f;

    /// <summary>Converts a stop-line crossing time from the clip into a spawn time.</summary>
    static float Spawn(float stopLineTime) => Mathf.Max(0f, stopLineTime - SPAWN_TO_STOPLINE_SEC);

    static TestCase Compare101()
    {
        // Case time runs from 1:02 of the clip, so t = (clip mm:ss) - 62 seconds.
        TestCase tc = new TestCase
        {
            name = "Compare-101 \u00B7 Real cycle from clip",
            mode = ArrivalMode.Scripted,
            suggestedDuration = 107f,      // one full cycle, measured 107.17 s
            description =
              "36 cars   A 10 \u00B7 Ax 3 \u00B7 B 7 \u00B7 Bx 4 \u00B7 CB 9 \u00B7 CA 3",
        };

        // ---- A : 4 queued, 6 more arriving (10 in total, matching the count) ----
        //
        // Changed from "9 queued + 1 arrival" after measuring the clip on 15 Sep.
        //
        // At 1:02, when head A turns green, the camera shows only 4 cars standing in lane A and
        // the last of them is flush against the left edge of frame, so the queue runs off-camera
        // and the count of 9 cannot be confirmed. The other 6 use stop-line crossing times taken
        // from a virtual detector at x=587 px, cars only, motorcycles excluded (a blob has to
        // occupy the line for at least 0.30 s to count).
        //
        // Why it had to change: 9 standing cars at the calibrated 2.31 s headway would empty the
        // queue by second 20, but the clip takes until 29.7, so they were not all standing to
        // begin with. Compare101Test measures it: 9 queued gives 5.75 s mean error, 4 gives 2.10 s.
        // Fitting 2 would give 1.14 s but contradicts the 4 cars visibly standing, so it is not used.
        tc.schedules.Add(new LaneSchedule("A",
            new ScheduledBurst(Spawn(17.40f), 1, 0f),
            new ScheduledBurst(Spawn(20.00f), 1, 0f),
            new ScheduledBurst(Spawn(23.20f), 1, 0f),
            new ScheduledBurst(Spawn(26.13f), 1, 0f),
            new ScheduledBurst(Spawn(29.70f), 1, 0f),
            new ScheduledBurst(Spawn(39.10f), 1, 0f)) { initialQueue = 4 });

        // ---- Ax : 1 queued, arrivals at t27 and t39 ----
        tc.schedules.Add(new LaneSchedule("Ax",
            new ScheduledBurst(Spawn(27f), 1, 0f),
            new ScheduledBurst(Spawn(39f), 1, 0f)) { initialQueue = 1 });

        // ---- B : 4 queued, arrivals at t36, t59, t60 ----
        tc.schedules.Add(new LaneSchedule("B",
            new ScheduledBurst(Spawn(36f), 1, 0f),
            new ScheduledBurst(Spawn(59f), 1, 0f),
            new ScheduledBurst(Spawn(60f), 1, 0f)) { initialQueue = 4 });

        // ---- Bx : 2 queued, arrivals at t53 and t55 (both land while Bx is green, 45.3-65.3) ----
        tc.schedules.Add(new LaneSchedule("Bx",
            new ScheduledBurst(Spawn(53f), 1, 0f),
            new ScheduledBurst(Spawn(55f), 1, 0f)) { initialQueue = 2 });

        // ---- CB : nothing queued, all 9 arrive during the cycle ----
        // The last 5 arrive in the same second (2:14) and are spread 0.8 s apart,
        // since they cannot all be released from one point at once.
        tc.schedules.Add(new LaneSchedule("CB",
            new ScheduledBurst(Spawn(16f), 1, 0f),
            new ScheduledBurst(Spawn(27f), 1, 0f),
            new ScheduledBurst(Spawn(32f), 1, 0f),
            new ScheduledBurst(Spawn(49f), 1, 0f),
            new ScheduledBurst(Spawn(72f), 5, 0.8f)));

        // ---- CA : nothing queued, arrivals at t39, t43, t72 ----
        tc.schedules.Add(new LaneSchedule("CA",
            new ScheduledBurst(Spawn(39f), 1, 0f),
            new ScheduledBurst(Spawn(43f), 1, 0f),
            new ScheduledBurst(Spawn(72f), 1, 0f)));

        return tc;
    }

    /// <summary>Run length of the Poisson cases, in seconds - also what the expected counts are based on.</summary>
    const float RUN_SECONDS = 300f;

    /// <summary>One line of expected vehicle counts over the run, so a Poisson case reads like a scripted one.</summary>
    static string RateSummary(float seconds, float a, float a2, float ax, float b, float bx, float cb, float ca)
    {
        string[] names = { "A", "A2", "Ax", "B", "Bx", "CB", "CA" };
        float[] intervals = { a, a2, ax, b, bx, cb, ca };
        var parts = new List<string>();
        int total = 0;
        for (int i = 0; i < names.Length; i++)
        {
            int n = intervals[i] > 0f ? Mathf.RoundToInt(seconds / intervals[i]) : 0;
            total += n;
            parts.Add($"{names[i]} {n}");
        }
        return $"~{total} cars in {seconds:0}s   " + string.Join(" \u00B7 ", parts);
    }

    static TestCase Poisson(string name,
                            float A, float Ax, float B, float Bx, float CB, float CA)
    {
        // The A argument is the combined through-traffic rate, before the shared lane splits it.
        // It is divided between A (inner) and A2 (outer) by A2_SHARE - not 50/50; see the comment
        // on A2_SHARE above. Thinning a Poisson process: each lane's rate is the total times its
        // share, so its mean interval is the total interval divided by that share.
        float aInner = A / (1f - A2_SHARE);   // inner lane takes most of the traffic -> shorter interval
        float aOuter = A / A2_SHARE;          // outer/shared lane takes far fewer -> much longer interval

        return new TestCase
        {
            name = name,
            description = RateSummary(RUN_SECONDS, aInner, aOuter, Ax, B, Bx, CB, CA),
            mode = ArrivalMode.Poisson,
            suggestedDuration = RUN_SECONDS,
            rates = new List<LaneArrivalRate> {
                new LaneArrivalRate("A",  aInner),
                new LaneArrivalRate("A2", aOuter),
                new LaneArrivalRate("Ax", Ax),
                new LaneArrivalRate("B",  B),
                new LaneArrivalRate("Bx", Bx),
                new LaneArrivalRate("CB", CB),
                new LaneArrivalRate("CA", CA),
            }
        };
    }
}
