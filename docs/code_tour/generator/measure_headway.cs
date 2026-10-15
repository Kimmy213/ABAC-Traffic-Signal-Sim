// Produces the queue-discharge data on the CarAgent page (window.TOUR_DATA.headway in assets/data.js).
//
// It runs the real CarAgent code outside Unity exactly the way Tests/harness/HeadwayTest.cs does:
// ten cars stopped at a red, green at t = 0, 0.02 s steps, the 25/60/15 driver mix in HeadwayTest's order.
//
// Run from the repository root, after `bash Tests/harness/run_tests.sh` has built gamecode.dll:
//   mcs -target:exe -out:/tmp/measure_headway.exe docs/code_tour/generator/measure_headway.cs -r:Tests/harness/_build/gamecode.dll
//   cp Tests/harness/_build/gamecode.dll /tmp/ && mono /tmp/measure_headway.exe > /tmp/headway.json
// then paste the JSON over the `window.TOUR_DATA.headway = ...;` line in assets/data.js.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using UnityEngine;

public class MeasureHeadway
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo StepM    = typeof(CarAgent).GetMethod("Step", NP);
    static readonly MethodInfo EnableM  = typeof(CarAgent).GetMethod("OnEnable", NP);
    static readonly MethodInfo DisableM = typeof(CarAgent).GetMethod("OnDisable", NP);
    static readonly MethodInfo ProfM    = typeof(CarAgent).GetMethod("SetProfileParameters", NP);
    static void Set(CarAgent c, string f, object v) => typeof(CarAgent).GetField(f, NP).SetValue(c, v);
    static string F(float v, string fmt) => v.ToString(fmt, CultureInfo.InvariantCulture);

    static readonly CarAgent.DriverType[] Mix = {
        CarAgent.DriverType.Normal,  CarAgent.DriverType.Aggressive, CarAgent.DriverType.Normal,
        CarAgent.DriverType.Cautious,CarAgent.DriverType.Normal,     CarAgent.DriverType.Normal,
        CarAgent.DriverType.Aggressive, CarAgent.DriverType.Normal,  CarAgent.DriverType.Cautious,
        CarAgent.DriverType.Normal };

    /// Same set-up as HeadwayTest.DischargeQueue. Returns crossing times, and every 0.2 s the centre of every car.
    static float[] Run(float timeHeadway, float horizon, List<float[]> frames)
    {
        int n = Mix.Length;
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
            c.driverType = Mix[i];
            c.desiredTimeHeadway = timeHeadway;
            c.startAtRest = true;
            c.stopLineTarget = stopLine;
            ProfM.Invoke(c, null);
            Set(c, "lightInitialized", true);
            Set(c, "lightIsGreen", true);
            EnableM.Invoke(c, null);
            cars.Add(c);
        }
        var crossed = new float[n];
        for (int i = 0; i < n; i++) crossed[i] = -1f;
        float dt = CarAgent.MAX_STEP, t = 0f;
        int tick = 0;
        while (t < horizon)
        {
            foreach (var c in cars) { Set(c, "stepDt", dt); StepM.Invoke(c, null); }
            t += dt; tick++;
            for (int i = 0; i < n; i++)
                if (crossed[i] < 0f && cars[i].transform.position.z + carLen * 0.5f >= 0f) crossed[i] = t;
            if (frames != null && tick % 10 == 0)
            {
                var f = new float[n];
                for (int i = 0; i < n; i++) f[i] = cars[i].transform.position.z;
                frames.Add(f);
            }
        }
        foreach (var c in cars) DisableM.Invoke(c, null);
        return crossed;
    }

    static float MeanHeadway(float[] crossed)
    {
        float sum = 0f; int n = 0;
        for (int i = 2; i < crossed.Length; i++)
        {
            if (crossed[i] < 0f || crossed[i - 1] < 0f) continue;
            sum += crossed[i] - crossed[i - 1]; n++;
        }
        return n == 0 ? float.NaN : sum / n;
    }

    public static int Main()
    {
        var sb = new StringBuilder("{\"mix\":[");
        for (int i = 0; i < Mix.Length; i++) sb.Append((i > 0 ? "," : "") + "\"" + Mix[i] + "\"");
        sb.Append("],\"sweep\":[");
        for (int k = 0; k <= 30; k++)
        {
            float T = k * 0.1f;
            var c = Run(T, 90f, null);
            sb.Append((k > 0 ? "," : "") + "{\"T\":" + F(T, "0.0") + ",\"h\":" + F(MeanHeadway(c), "0.00") + ",\"cross\":[");
            for (int i = 0; i < c.Length; i++) sb.Append((i > 0 ? "," : "") + F(c[i], "0.00"));
            sb.Append("]}");
        }
        sb.Append("],\"traj\":{");
        float[] keep = { 1.0f, 1.5f, 1.9f, 2.4f, 2.8f };
        for (int k = 0; k < keep.Length; k++)
        {
            var frames = new List<float[]>();
            Run(keep[k], 32f, frames);
            sb.Append((k > 0 ? "," : "") + "\"" + F(keep[k], "0.0") + "\":[");
            for (int j = 0; j < frames.Count; j++)
            {
                sb.Append((j > 0 ? "," : "") + "[");
                for (int i = 0; i < frames[j].Length; i++) sb.Append((i > 0 ? "," : "") + F(frames[j][i], "0.0"));
                sb.Append("]");
            }
            sb.Append("]");
        }
        sb.Append("},\"trajDt\":0.2}");
        Console.WriteLine(sb.ToString());
        return 0;
    }
}
