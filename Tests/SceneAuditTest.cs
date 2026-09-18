using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// Checks that the values stored in SampleScene.unity match the values the code intends.
///
/// Why this test exists (Ratchanon spotted it):
///   The old test compared .cs against .cs (ScenarioManager vs TestCasePresets), which could never
///   catch this bug, because the mismatch is between .cs and .unity. Unity serialises a component's
///   values when it is placed in the scene, and those always override the script defaults.
///
/// How it works: create a fresh instance of the component (= the defaults the code intends) and compare
/// it against every field the scene serialised, by reflection - covering all fields, not just the known few.
/// Fields allowed to differ (references, runtime options) are listed in ALLOWED below.
/// </summary>
public class SceneAuditTest
{
    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    // Fields the scene may legitimately set differently from the default (not drift)
    static readonly Dictionary<string, HashSet<string>> ALLOWED = new Dictionary<string, HashSet<string>>
    {
        { "AdaptiveController", new HashSet<string> { "showDebugGUI", "verboseLog" } },
        { "ScenarioManager",    new HashSet<string> { "scenarios" } },
        { "FixedController",    new HashSet<string> { "showDebugGUI", "verboseLog", "phases" } },
    };

    /// <summary>Extracts one component block from the scene as field -> raw value (top-level scalars only)</summary>
    static Dictionary<string, string> ParseBlock(string[] lines, string className)
    {
        var result = new Dictionary<string, string>();
        int start = -1;
        for (int i = 0; i < lines.Length; i++)
        {
            if (lines[i].Trim() == $"m_EditorClassIdentifier: Assembly-CSharp::{className}") { start = i + 1; break; }
        }
        if (start < 0) return null;

        for (int i = start; i < lines.Length; i++)
        {
            string line = lines[i];
            if (line.StartsWith("---")) break;                 // end of the object
            Match m = Regex.Match(line, @"^  ([A-Za-z_][A-Za-z0-9_]*): ?(.*)$");
            if (!m.Success) continue;                          // a child line, indented deeper - skip
            result[m.Groups[1].Value] = m.Groups[2].Value.Trim();
        }
        return result;
    }

    static bool TryScalar(string raw, out double val)
    {
        val = 0;
        if (string.IsNullOrEmpty(raw)) return false;
        if (raw.StartsWith("{")) return false;                 // reference / struct
        return double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out val);
    }

    /// <summary>Compares every scalar field of the component in the scene against the code default</summary>
    static void AuditComponent(string[] lines, object fresh, string className)
    {
        var scene = ParseBlock(lines, className);
        Console.WriteLine($"\n--- {className} ---");
        if (scene == null) { Check(false, $"{className} not found in the scene"); return; }

        var allowed = ALLOWED.ContainsKey(className) ? ALLOWED[className] : new HashSet<string>();
        int compared = 0, drifted = 0;

        foreach (FieldInfo f in fresh.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (allowed.Contains(f.Name)) continue;
            if (!scene.ContainsKey(f.Name)) continue;          // a new field the scene has not saved yet - it uses the default
            double sceneVal;
            if (!TryScalar(scene[f.Name], out sceneVal)) continue;

            double codeVal;
            object cur = f.GetValue(fresh);
            if      (cur is float) codeVal = (float)cur;
            else if (cur is int)   codeVal = (int)cur;
            else if (cur is bool)  codeVal = ((bool)cur) ? 1 : 0;
            else continue;

            compared++;
            bool ok = Math.Abs(sceneVal - codeVal) < 0.0005;
            if (!ok)
            {
                drifted++;
                Check(false, $"{className}.{f.Name}: scene = {sceneVal} but the code intends {codeVal}");
            }
        }

        Console.WriteLine($"    compared {compared} fields, {drifted} mismatched");
        Check(drifted == 0, $"{className}: every field the scene serialised matches the code default");
    }

    static void Exact(Dictionary<string, string> blk, string field, double want, string note)
    {
        string raw = blk != null && blk.ContainsKey(field) ? blk[field] : "(not in the scene)";
        double got;
        bool ok = TryScalar(raw, out got) && Math.Abs(got - want) < 0.0005;
        Check(ok, $"{field} = {want} in the scene file (read {raw}) - {note}");
    }

    public static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "Scenes/SampleScene.unity";
        if (!File.Exists(path))
        {
            Console.WriteLine($"scene file not found: {path}");
            return 1;
        }
        string[] lines = File.ReadAllLines(path);
        Console.WriteLine($"checking {path} ({lines.Length} lines)");

        AuditComponent(lines, new AdaptiveController(), "AdaptiveController");
        AuditComponent(lines, new ScenarioManager(),    "ScenarioManager");
        AuditComponent(lines, new FixedController(),    "FixedController");

        // ===== The 6 values that actually drifted - read straight from the scene file, not via reflection =====
        Console.WriteLine("\n--- values that have drifted before (read directly from .unity) ---");
        var ad = ParseBlock(lines, "AdaptiveController");
        var sm = ParseBlock(lines, "ScenarioManager");

        Exact(ad, "startupLostTime",        2.95,  "the measured value, not the 1 that was once stuck");
        Exact(ad, "saturationHeadway",      2.31,  "the measured value, not the proposal's 2.5");
        Exact(sm, "repetitionsPerScenario", 27,    "270 runs, not 10");
        Exact(sm, "simulationSpeed",        10,    "10x after sub-stepping (Kimmy, 10 Sep)");
        Exact(sm, "a2Share",                0.054, "the direct count from SP1_HANDOFF_v2 s5, not the back-calculated 0.18");

        // The scene values must also match the consts in the code (in case a const changes and the scene does not)
        Exact(ad, "startupLostTime",   AdaptiveController.MEASURED_STARTUP_LOST_TIME,  "matches the const in the code");
        Exact(ad, "saturationHeadway", AdaptiveController.MEASURED_SATURATION_HEADWAY, "matches the const in the code");
        Exact(sm, "a2Share",           ScenarioManager.MEASURED_A2_SHARE,              "matches the const in the code");
        Exact(sm, "repetitionsPerScenario", ScenarioManager.BATCH_REPETITIONS,         "matches the const in the code");

        // ===== The batch scenario list the scene serialised (ALLOWED skips "scenarios" in reflection, so check it here) =====
        Console.WriteLine("\n--- scenario list in the scene ---");
        {
            var names = new List<string>();
            bool inSm = false, inList = false;
            string midCB = null, midCA = null, cur = null;
            var evt = new Dictionary<string, string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string ln = lines[i];
                if (ln.Trim() == "m_EditorClassIdentifier: Assembly-CSharp::ScenarioManager") inSm = true;
                else if (ln.StartsWith("---")) { inSm = false; inList = false; }
                if (!inSm) continue;
                if (ln.StartsWith("  scenarios:")) { inList = true; continue; }
                if (inList && Regex.IsMatch(ln, @"^  [A-Za-z]")) inList = false;
                if (!inList) continue;
                if (ln.StartsWith("  - name: ")) { cur = ln.Substring(10).Trim(); names.Add(cur); }
                if (ln.StartsWith("    - laneName: ") && i + 1 < lines.Length)
                {
                    string lane = ln.Substring(16).Trim();
                    string val = lines[i + 1].Trim().Replace("meanInterval:", "").Trim();
                    if (cur == "MiddayNormal" && lane == "CB") midCB = val;
                    if (cur == "MiddayNormal" && lane == "CA") midCA = val;
                    if (cur == "ABACEvent") evt[lane] = val;
                }
            }
            Console.WriteLine("    " + string.Join(", ", names.ToArray()));
            Check(names.Count == 4, $"the scene has 4 scenarios ({names.Count})");
            Check(!names.Contains("Night"), "Night is not in the scene (dropped 10 Sep)");
            Check(names.Contains("MorningPeak") && names.Contains("EveningPeak")
               && names.Contains("MiddayNormal") && names.Contains("ABACEvent"), "all 4 required scenarios present");
            Check(midCB == "10.1", $"MiddayNormal CB = 10.1 (measured by Itim), not 7 (read {midCB})");
            Check(midCA == "13.2", $"MiddayNormal CA = 13.2 (measured by Itim), not 7 (read {midCA})");

            // ===== ABACEvent was rescaled on 18 Sep 2026 =====
            // The old rates (Ax and Bx at 1.5 s) put lane Bx at v/c 9.6 against the calibrated vehicle
            // model, and 10 of 54 runs in batch v3 gridlocked outright. The scene stores its own copy of
            // this table and overrides the code, so editing ScenarioManager.cs alone changes nothing:
            // press "Load ABAC Scenario Preset" in the Inspector, then save the scene.
            // If this check fails, that is the step that has not been done.
            var wantEvt = new Dictionary<string, string> {
                { "A",  "6.871" },   // MakeScenario splits the 6.5 combined through rate by a2Share:
                { "A2", "120.37" },  // inner = 6.5 / (1 - 0.054) = 6.871, outer = 6.5 / 0.054 = 120.37
                { "Ax", "3.2" }, { "B", "4" }, { "Bx", "8" }, { "CB", "8" }, { "CA", "2.5" },
            };
            Console.WriteLine("    ABACEvent in the scene: " +
                (evt.Count == 0 ? "(not found)" : string.Join(", ", evt.Select(kv => kv.Key + "=" + kv.Value).ToArray())));
            foreach (var kv in wantEvt)
            {
                string got = evt.ContainsKey(kv.Key) ? evt[kv.Key] : "(missing)";
                bool ok = got != "(missing)" && Math.Abs(double.Parse(got) - double.Parse(kv.Value)) < 0.05;
                Check(ok, $"ABACEvent {kv.Key} = {kv.Value} in the scene (read {got})"
                        + (ok ? "" : "  -> press Load ABAC Scenario Preset in the Inspector, then save the scene"));
            }
        }

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
