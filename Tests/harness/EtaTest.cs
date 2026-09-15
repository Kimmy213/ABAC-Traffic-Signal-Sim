using System;
using System.Reflection;
using UnityEngine;

/// Simulates the passage of real time and checks that the ETA only ever falls, never rises
public class EtaTest
{
    static int fail = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) fail++;
    }

    static FieldInfo F(string n) =>
        typeof(ScenarioManager).GetField(n, BindingFlags.NonPublic | BindingFlags.Instance);

    static void Set(ScenarioManager sm, string n, object v) => F(n).SetValue(sm, v);
    static object Get(ScenarioManager sm, string n) => F(n).GetValue(sm);

    /// <summary>Advances time, simulating each run taking realPerRun seconds of real time</summary>
    static bool RunScenario(string title, float runDur, float warmup, float speed,
                            int totalRuns, float realPerRun, bool expectMonotonic)
    {
        var sm = new ScenarioManager();
        sm.runDuration = runDur;
        sm.warmupDuration = warmup;
        sm.simulationSpeed = speed;

        Set(sm, "isRunning", true);
        Set(sm, "totalRuns", totalRuns);
        Set(sm, "finishedRuns", 0);
        Set(sm, "startedRuns", 1);
        Set(sm, "elapsedAtLastFinish", 0f);
        Set(sm, "batchStartRealtime", 0f);
        Set(sm, "currentRunStartRealtime", 0f);

        float theoretical = (warmup + runDur) / speed;
        Console.WriteLine($"\n  [{title}]  theory {theoretical:F1} s/run - actual {realPerRun:F1} s/run - {totalRuns} runs");

        float prevEta = float.MaxValue;
        float prevProgress = -1f;
        bool monotonic = true, progressOk = true;
        int finished = 0;
        int bumpsAfterFinish = 0;
        float lastPrint = -999f;

        for (float t = 0f; t <= realPerRun * totalRuns + 1f; t += 0.5f)
        {
            Time.realtimeSinceStartup = t;

            // has the next run finished?
            int shouldBeFinished = (int)(t / realPerRun);
            if (shouldBeFinished > finished && shouldBeFinished <= totalRuns)
            {
                finished = shouldBeFinished;
                Set(sm, "finishedRuns", finished);
                Set(sm, "elapsedAtLastFinish", finished * realPerRun);
                Set(sm, "currentRunStartRealtime", finished * realPerRun);
                prevEta = float.MaxValue;   // allow a fresh estimate when a run ends
                bumpsAfterFinish++;
                if (finished >= totalRuns) { Set(sm, "isRunning", false); break; }
            }

            float eta = sm.EstimatedRemaining;
            float pg = sm.Progress01;

            if (eta > prevEta + 0.01f) monotonic = false;
            if (pg < prevProgress - 0.001f) progressOk = false;
            prevEta = eta; prevProgress = pg;

            if (t - lastPrint >= realPerRun * Mathf.Max(1, totalRuns / 4))
            {
                Console.WriteLine($"      t={t,6:F0}s  finished={finished}  " +
                                  $"progress={pg * 100,5:F1}%  ETA={eta,7:F1}s");
                lastPrint = t;
            }
        }

        Check(monotonic, $"{title}: the ETA never rises within one run");
        Check(progressOk, $"{title}: the progress bar only moves forward");
        return monotonic && progressOk;
    }

    public static int Main()
    {
        Console.WriteLine("=== ETA / progress checks ===");

        // 1) exactly as estimated
        RunScenario("on time", runDur: 60f, warmup: 0f, speed: 2f,
                    totalRuns: 10, realPerRun: 30f, expectMonotonic: true);

        // 2) the machine cannot keep up and runs take twice as long (the case seen in the screenshot)
        RunScenario("slower than estimated", runDur: 60f, warmup: 0f, speed: 2f,
                    totalRuns: 10, realPerRun: 60f, expectMonotonic: true);

        // 3) the machine is faster than estimated
        RunScenario("faster than estimated", runDur: 60f, warmup: 0f, speed: 2f,
                    totalRuns: 10, realPerRun: 18f, expectMonotonic: true);

        // 4) the real 270-run batch
        RunScenario("270 runs, real settings", runDur: 1800f, warmup: 120f, speed: 10f,
                    totalRuns: 270, realPerRun: 200f, expectMonotonic: true);

        Console.WriteLine("\n=== edge cases ===");
        var sm = new ScenarioManager();
        Check(sm.EstimatedRemaining < 0f, "batch not started -> ETA = -1");
        Check(Mathf.Approximately(sm.Progress01, 0f), "not started -> progress = 0, no division by zero");
        Check(Mathf.Approximately(sm.CurrentRunFraction01, 0f), "not started -> fraction = 0");

        Console.WriteLine(fail == 0 ? "\nALL CHECKS PASSED" : $"\n{fail} CHECK(S) FAILED");
        return fail;
    }
}
