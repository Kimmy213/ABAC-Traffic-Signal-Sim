using System;
using System.Reflection;
using UnityEngine;

/// จำลองเวลาเดินจริง แล้วตรวจว่า ETA ลดลงตลอด ไม่เพิ่มขึ้น
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

    /// <summary>เดินเวลาไปข้างหน้า จำลองว่าแต่ละ run ใช้เวลาจริง realPerRun วินาที</summary>
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
        Console.WriteLine($"\n  [{title}]  ทฤษฎี {theoretical:F1} วิ/run · จริง {realPerRun:F1} วิ/run · {totalRuns} runs");

        float prevEta = float.MaxValue;
        float prevProgress = -1f;
        bool monotonic = true, progressOk = true;
        int finished = 0;
        int bumpsAfterFinish = 0;
        float lastPrint = -999f;

        for (float t = 0f; t <= realPerRun * totalRuns + 1f; t += 0.5f)
        {
            Time.realtimeSinceStartup = t;

            // ถึงเวลา run ถัดไปจบหรือยัง
            int shouldBeFinished = (int)(t / realPerRun);
            if (shouldBeFinished > finished && shouldBeFinished <= totalRuns)
            {
                finished = shouldBeFinished;
                Set(sm, "finishedRuns", finished);
                Set(sm, "elapsedAtLastFinish", finished * realPerRun);
                Set(sm, "currentRunStartRealtime", finished * realPerRun);
                prevEta = float.MaxValue;   // อนุญาตให้ประเมินใหม่ตอน run จบ
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

        Check(monotonic, $"{title}: ETA ไม่เพิ่มขึ้นระหว่าง run เดียวกัน");
        Check(progressOk, $"{title}: หลอดความคืบหน้าเดินหน้าอย่างเดียว ไม่ถอยหลัง");
        return monotonic && progressOk;
    }

    public static int Main()
    {
        Console.WriteLine("=== ตรวจ ETA / Progress ===");

        // 1) ตรงตามที่ประเมิน
        RunScenario("ตรงเวลา", runDur: 60f, warmup: 0f, speed: 2f,
                    totalRuns: 10, realPerRun: 30f, expectMonotonic: true);

        // 2) เครื่องตามไม่ทัน run ช้ากว่าที่คิด 2 เท่า (เคสที่เจอในภาพ)
        RunScenario("เครื่องช้ากว่าที่ประเมิน", runDur: 60f, warmup: 0f, speed: 2f,
                    totalRuns: 10, realPerRun: 60f, expectMonotonic: true);

        // 3) เครื่องเร็วกว่าที่คิด
        RunScenario("เครื่องเร็วกว่าที่ประเมิน", runDur: 60f, warmup: 0f, speed: 2f,
                    totalRuns: 10, realPerRun: 18f, expectMonotonic: true);

        // 4) ของจริง 270 runs
        RunScenario("270 runs ของจริง", runDur: 1800f, warmup: 120f, speed: 10f,
                    totalRuns: 270, realPerRun: 200f, expectMonotonic: true);

        Console.WriteLine("\n=== ตรวจเคสขอบ ===");
        var sm = new ScenarioManager();
        Check(sm.EstimatedRemaining < 0f, "ยังไม่เริ่ม batch -> ETA = -1");
        Check(Mathf.Approximately(sm.Progress01, 0f), "ยังไม่เริ่ม -> progress = 0 ไม่หารศูนย์");
        Check(Mathf.Approximately(sm.CurrentRunFraction01, 0f), "ยังไม่เริ่ม -> fraction = 0");

        Console.WriteLine(fail == 0 ? "\nALL CHECKS PASSED" : $"\n{fail} CHECK(S) FAILED");
        return fail;
    }
}
