using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// Compares when cars leave lane A in the sim against when they leave it in the real clip, over one green.
///
/// Ground truth from DJI_20260824154642_0021_D, window 1:02-2:49 (head A turns green at 62.3 s).
/// Measured with a virtual detector at the stop line, x=587 px, cars only (a blob must cover the line for 0.30 s).
/// Motorcycles are excluded entirely, as agreed on 15 Sep.
///
/// The sim side uses the real CarAgent, stepping 0.02 s exactly as in Unity; only the geometry is a single straight lane.
public class Compare101Test
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo StepM  = typeof(CarAgent).GetMethod("Step", NP);
    static readonly MethodInfo EnM    = typeof(CarAgent).GetMethod("OnEnable", NP);
    static readonly MethodInfo DisM   = typeof(CarAgent).GetMethod("OnDisable", NP);
    static readonly MethodInfo ProfM  = typeof(CarAgent).GetMethod("SetProfileParameters", NP);
    static void Set(CarAgent c, string f, object v) => typeof(CarAgent).GetField(f, NP).SetValue(c, v);

    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    // When each front bumper crosses the stop line, measured from head A turning green (62.3 s of the clip)
    static readonly float[] VIDEO = { 4.90f, 8.90f, 11.13f, 14.77f, 17.40f,
                                      20.00f, 23.20f, 26.13f, 29.70f, 39.10f };

    // The 25/60/15 driver mix, spread out the way CarSpawner would draw it
    static readonly CarAgent.DriverType[] MIX = {
        CarAgent.DriverType.Normal, CarAgent.DriverType.Aggressive, CarAgent.DriverType.Normal,
        CarAgent.DriverType.Normal, CarAgent.DriverType.Cautious,   CarAgent.DriverType.Normal,
        CarAgent.DriverType.Aggressive, CarAgent.DriverType.Normal, CarAgent.DriverType.Normal,
        CarAgent.DriverType.Normal };

    const float CAR_LEN = 4f, MIN_GAP = 2f, SPAWN_X = -189.5f;
    const float GREEN_END = 40f;          // A + A2 + B green 0-40 per the real stage table
    const float YELLOW_END = 43f;

    /// <summary>Returns when each car's front crosses the stop line (the line is at z = 0)</summary>
    static List<float> RunLaneA(int queued, float[] spawnTimes, float timeHeadway)
    {
        var sl = new Transform { position = Vector3.zero, forward = new Vector3(0, 0, 1) };
        var cars = new List<CarAgent>();
        var pending = new List<KeyValuePair<float, int>>();
        int idx = 0;

        for (int i = 0; i < queued; i++)
        {
            var c = new CarAgent();
            c.transform = new Transform {
                position = new Vector3(0, 0, -(CarAgent.REST_FRONT_MARGIN + CAR_LEN * 0.5f) - i * (CAR_LEN + MIN_GAP)),
                forward  = new Vector3(0, 0, 1) };
            c.gameObject = new GameObject("q" + i);
            c.driverType = MIX[idx % MIX.Length]; idx++;
            c.desiredTimeHeadway = timeHeadway;
            c.startAtRest = true; c.stopLineTarget = sl;
            ProfM.Invoke(c, null);
            Set(c, "lightInitialized", true); Set(c, "lightIsGreen", false);
            EnM.Invoke(c, null); cars.Add(c);
        }
        foreach (float st in spawnTimes) pending.Add(new KeyValuePair<float, int>(st, idx++));

        var crossed = new List<float>();
        var seen = new HashSet<CarAgent>();
        float dt = CarAgent.MAX_STEP, t = 0f;

        while (t < 60f)
        {
            // release cars that are due
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Key > t) continue;
                var c = new CarAgent();
                c.transform = new Transform { position = new Vector3(0, 0, SPAWN_X), forward = new Vector3(0, 0, 1) };
                c.gameObject = new GameObject("s");
                c.driverType = MIX[pending[i].Value % MIX.Length];
                c.desiredTimeHeadway = timeHeadway;
                c.stopLineTarget = sl;
                ProfM.Invoke(c, null);
                Set(c, "lightInitialized", true);
                EnM.Invoke(c, null); cars.Add(c);
                pending.RemoveAt(i);
            }

            bool green = t < GREEN_END;
            foreach (var c in cars) Set(c, "lightIsGreen", green);

            foreach (var c in cars) { Set(c, "stepDt", dt); StepM.Invoke(c, null); }
            t += dt;

            foreach (var c in cars)
                if (!seen.Contains(c) && c.transform.position.z + CAR_LEN * 0.5f >= 0f)
                { seen.Add(c); crossed.Add(t); }
        }
        foreach (var c in cars) DisM.Invoke(c, null);
        crossed.Sort();
        return crossed;
    }

    public static int Main()
    {
        // Must match TestCasePresets.Compare101() lane A exactly: 4 queued + 6 arriving
        float tau = TestCasePresets.SPAWN_TO_STOPLINE_SEC;
        float[] arrive = { 17.40f, 20.00f, 23.20f, 26.13f, 29.70f, 39.10f };
        for (int i = 0; i < arrive.Length; i++) arrive[i] = Math.Max(0f, arrive[i] - tau);
        var sim = RunLaneA(4, arrive, CarAgent.MEASURED_TIME_HEADWAY);

        Console.WriteLine("--- lane A: sim vs clip (seconds from green) ---");
        Console.WriteLine("  car     clip      sim    diff");
        int n = Math.Min(sim.Count, VIDEO.Length);
        float sumAbs = 0f, worst = 0f;
        for (int i = 0; i < n; i++)
        {
            float d = sim[i] - VIDEO[i];
            sumAbs += Math.Abs(d); worst = Math.Max(worst, Math.Abs(d));
            Console.WriteLine($"  {i + 1,3}   {VIDEO[i],7:F2}  {sim[i],7:F2}  {d,7:F2}");
        }
        Console.WriteLine($"\n  discharged during green: clip {VIDEO.Length} cars - sim {sim.Count} cars");
        Console.WriteLine($"  mean error {sumAbs / n:F2} s - worst {worst:F2} s");

        // Car 5 onwards are all vehicles that drove in, so they measure how well arrivals match the clip
        float arrErr = 0f, arrWorst = 0f;
        for (int i = 4; i < n; i++)
        {
            float d = Math.Abs(sim[i] - VIDEO[i]);
            arrErr += d; arrWorst = Math.Max(arrWorst, d);
        }
        arrErr /= (n - 4);
        Console.WriteLine($"  arriving cars only (5-10): mean error {arrErr:F2} s - worst {arrWorst:F2} s");

        Check(sim.Count == VIDEO.Length, $"discharged all {VIDEO.Length} cars within the 40 s green, as in the clip");
        Check(sim[sim.Count - 1] < GREEN_END, "the last car crosses before the green ends, as in the clip");
        Check(Math.Abs(sim[sim.Count - 1] - VIDEO[VIDEO.Length - 1]) < 0.3f,
              "the last car (the only one arriving on a clear lane) matches the clip within 0.3 s");
        Check(arrWorst < 2.0f, $"every arriving car is within 2 s (worst {arrWorst:F2})");
        Check(sumAbs / n < 3.0f, $"mean error across the lane is under 3 s (currently {sumAbs / n:F2})");

        // ===== The remaining error is confined to the first four cars, and is explained =====
        // In the clip a cluster of motorcycles stands on the crossing ahead of every car and leaves at +1.80 s.
        // The first car cannot move until +5.10 s, so it waited 3.30 s for the motorcycles to clear.
        // It was agreed on 15 Sep not to model motorcycles, so the initial queue always leaves earlier than the clip.
        //
        // Compare101Test measured the alternatives (T = 1.9, the 39-sample calibration untouched):
        //     9 queued (the hand count)     -> mean error 5.75 s
        //     4 queued (what the camera sees) -> 2.10 s   <-- in use
        //     2 queued (best fit)             -> 1.14 s   contradicts the footage, so not used
        for (int i = 0; i < 4 && i < n; i++)
            Check(sim[i] <= VIDEO[i] + 0.01f,
                  $"queued car {i + 1} leaves no later than in the clip (diff {sim[i] - VIDEO[i]:F2} s, from the excluded motorcycles)");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
