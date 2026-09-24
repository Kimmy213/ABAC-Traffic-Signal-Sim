using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// Runs a queue of real CarAgent instances outside Unity and measures how fast it discharges,
/// against the value measured from the clip: saturation headway 2.31 s/veh (section 2.4, [measured]).
///
/// This test exists because Badin noticed in the scene on 15 Sep that "several cars pull away at once".
/// Measuring it showed the model discharging at 0.72 s/veh, 3.2 times faster than reality, for two reasons:
///   1) no startup wave - followers timed their reaction from the green signal, which everyone sees at once
///   2) the gap to the car ahead was a constant 2 m and did not grow with speed, so cars ran nose-to-tail at 15 m/s
public class HeadwayTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo StepM   = typeof(CarAgent).GetMethod("Step", NP);
    static readonly MethodInfo EnableM = typeof(CarAgent).GetMethod("OnEnable", NP);
    static readonly MethodInfo DisableM= typeof(CarAgent).GetMethod("OnDisable", NP);
    static readonly MethodInfo ProfM   = typeof(CarAgent).GetMethod("SetProfileParameters", NP);

    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }
    static void Set(CarAgent c, string f, object v) => typeof(CarAgent).GetField(f, NP).SetValue(c, v);

    /// A queue of n cars already stopped at a red, with the light turning green at t = 0.
    /// Returns the time each car's front crosses the stop line.
    static float[] DischargeQueue(CarAgent.DriverType[] mix, float timeHeadway, float horizon = 90f)
    {
        int n = mix.Length;
        var stopLine = new Transform { position = Vector3.zero, forward = new Vector3(0, 0, 1) };
        var cars = new List<CarAgent>();
        float carLen = 4f, minGap = 2f, pitch = carLen + minGap;

        for (int i = 0; i < n; i++)
        {
            var c = new CarAgent();
            c.transform = new Transform {
                position = new Vector3(0, 0, -(CarAgent.REST_FRONT_MARGIN + carLen * 0.5f) - i * pitch),
                forward  = new Vector3(0, 0, 1) };
            c.gameObject = new GameObject("car" + i);
            c.driverType = mix[i];
            c.desiredTimeHeadway = timeHeadway;
            c.startAtRest = true;
            c.stopLineTarget = stopLine;
            ProfM.Invoke(c, null);
            Set(c, "lightInitialized", true);
            Set(c, "lightIsGreen", true);       // green from t = 0
            EnableM.Invoke(c, null);
            cars.Add(c);
        }

        var crossed = new float[n];
        for (int i = 0; i < n; i++) crossed[i] = -1f;

        float dt = CarAgent.MAX_STEP, t = 0f;
        while (t < horizon)
        {
            foreach (var c in cars) { Set(c, "stepDt", dt); StepM.Invoke(c, null); }
            t += dt;
            for (int i = 0; i < n; i++)
                if (crossed[i] < 0f && cars[i].transform.position.z + carLen * 0.5f >= 0f)
                    crossed[i] = t;
        }
        foreach (var c in cars) DisableM.Invoke(c, null);   // remove from allCars so nothing leaks into the next case
        return crossed;
    }

    static float MeanHeadway(float[] crossed, int skipFirst = 1)
    {
        float sum = 0f; int n = 0;
        for (int i = skipFirst + 1; i < crossed.Length; i++)
        {
            if (crossed[i] < 0f || crossed[i - 1] < 0f) continue;
            sum += crossed[i] - crossed[i - 1]; n++;
        }
        return n == 0 ? float.NaN : sum / n;
    }

    public static int Main()
    {
        // The driver mix CarSpawner actually uses (25 aggressive / 60 normal / 15 cautious)
        var mix = new CarAgent.DriverType[] {
            CarAgent.DriverType.Normal,  CarAgent.DriverType.Aggressive, CarAgent.DriverType.Normal,
            CarAgent.DriverType.Cautious,CarAgent.DriverType.Normal,     CarAgent.DriverType.Normal,
            CarAgent.DriverType.Aggressive, CarAgent.DriverType.Normal,  CarAgent.DriverType.Cautious,
            CarAgent.DriverType.Normal };

        Console.WriteLine("--- a queue of 10 discharging after green ---");
        var crossed = DischargeQueue(mix, CarAgent.MEASURED_TIME_HEADWAY);
        for (int i = 0; i < crossed.Length; i++)
        {
            string h = i == 0 ? "    -" : (crossed[i] - crossed[i - 1]).ToString("F2");
            Console.WriteLine($"    car {i + 1,2}  {mix[i],-10}  crosses at {crossed[i],6:F2} s   headway {h}");
        }

        float mean = MeanHeadway(crossed);
        Console.WriteLine($"\n    mean headway (cars 1-2 excluded) = {mean:F2} s/veh | measured from the clip 2.31");

        // ===== 1) It must discharge at the measured rate =====
        Check(Math.Abs(mean - 2.31f) <= 0.15f,
              $"saturation headway {mean:F2} s/veh is within 2.31 +/- 0.15 as measured from the clip");

        // ===== 2) No two cars may leave together - that was the symptom seen in the scene =====
        float tightest = float.MaxValue;
        for (int i = 1; i < crossed.Length; i++)
            tightest = Math.Min(tightest, crossed[i] - crossed[i - 1]);
        Console.WriteLine($"    tightest headway = {tightest:F2} s");
        Check(tightest >= 1.0f, $"no pair leaves less than 1 s apart (tightest {tightest:F2} s)");

        // ===== 3) The startup wave must travel backwards, not release the whole row at once =====
        bool monotone = true;
        for (int i = 1; i < crossed.Length; i++)
            if (crossed[i] <= crossed[i - 1]) monotone = false;
        Check(monotone, "cars cross in queue order, with no overtaking");

        // ===== 4) T must be what actually controls the discharge rate (so a change cannot slip by) =====
        Console.WriteLine("\n--- sensitivity to desiredTimeHeadway ---");
        float hLow  = MeanHeadway(DischargeQueue(mix, 1.0f));
        float hHigh = MeanHeadway(DischargeQueue(mix, 2.8f));
        Console.WriteLine($"    T=1.0 -> {hLow:F2} s/veh | T=1.9 -> {mean:F2} | T=2.8 -> {hHigh:F2}");
        Check(hLow < mean && mean < hHigh, "headway grows monotonically with T");
        Check(Math.Abs(CarAgent.MEASURED_TIME_HEADWAY - 1.9f) < 1e-6f,
              "the constant T = 1.9 s, the value tuned to match 2.31");

        // ===== 5) Without the startup wave the queue launches as a block - confirm the mechanism is still there =====
        var f = typeof(CarAgent).GetField("startupTimer", NP);
        Check(f != null, "CarAgent still has the startup wave timer (startupTimer)");
        Check(CarAgent.LEADER_MOVING_SPEED > CarAgent.QUEUE_SPEED_EPS,
              "the 'car ahead is moving' threshold must exceed the 'counts as stopped' one, or a car releases itself");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
