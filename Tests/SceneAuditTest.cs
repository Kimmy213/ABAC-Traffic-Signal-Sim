using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEngine;

/// <summary>
/// ตรวจว่า "ค่าที่ SampleScene.unity เก็บไว้" ตรงกับ "ค่าที่โค้ดตั้งใจ" หรือไม่
///
/// ทำไมต้องมีเทสต์นี้ (Ratchnon ชี้ถูก):
///   เทสต์เดิมเทียบ .cs กับ .cs (ScenarioManager vs TestCasePresets) ซึ่ง "จับบั๊กนี้ไม่ได้เลย"
///   เพราะตัวที่ไม่ตรงคือ .cs กับ .unity — Unity serialize ค่าของ component ตอนที่วางลงซีน
///   แล้วค่านั้นทับ default ในสคริปต์เสมอ แก้ .cs อย่างเดียวจึงไม่มีผล
///
/// วิธีตรวจ: สร้าง instance ใหม่ของ component (= ค่า default ที่โค้ดตั้งใจ) แล้วเทียบกับ
/// ทุกฟิลด์ที่ซีน serialize ไว้ ด้วย reflection — จึงครอบ "ทุกฟิลด์" ไม่ใช่แค่ 4-6 ตัวที่รู้จัก
/// ฟิลด์ที่ตั้งใจให้ต่างได้ (reference, ตัวเลือกตอนรัน) อยู่ใน ALLOWED ด้านล่าง
/// </summary>
public class SceneAuditTest
{
    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    // ฟิลด์ที่ซีนตั้งค่าต่างจาก default ได้ตามปกติ (ไม่ใช่ drift)
    static readonly Dictionary<string, HashSet<string>> ALLOWED = new Dictionary<string, HashSet<string>>
    {
        { "AdaptiveController", new HashSet<string> { "showDebugGUI", "verboseLog" } },
        { "ScenarioManager",    new HashSet<string> { "scenarios" } },
        { "FixedController",    new HashSet<string> { "showDebugGUI", "verboseLog", "phases" } },
    };

    /// <summary>ดึงบล็อกของ component หนึ่งตัวออกมาเป็น field -> ค่าดิบ (เฉพาะ scalar ระดับบนสุด)</summary>
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
            if (line.StartsWith("---")) break;                 // จบ object
            Match m = Regex.Match(line, @"^  ([A-Za-z_][A-Za-z0-9_]*): ?(.*)$");
            if (!m.Success) continue;                          // บรรทัดลูก (เยื้องลึกกว่า) ข้าม
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

    /// <summary>เทียบทุกฟิลด์ scalar ของ component ในซีน กับค่า default ที่โค้ดตั้งใจ</summary>
    static void AuditComponent(string[] lines, object fresh, string className)
    {
        var scene = ParseBlock(lines, className);
        Console.WriteLine($"\n--- {className} ---");
        if (scene == null) { Check(false, $"หา {className} ในซีนไม่เจอ"); return; }

        var allowed = ALLOWED.ContainsKey(className) ? ALLOWED[className] : new HashSet<string>();
        int compared = 0, drifted = 0;

        foreach (FieldInfo f in fresh.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
        {
            if (allowed.Contains(f.Name)) continue;
            if (!scene.ContainsKey(f.Name)) continue;          // ฟิลด์ใหม่ที่ซีนยังไม่ได้ save — ใช้ default อยู่แล้ว
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
                Check(false, $"{className}.{f.Name}: ซีน = {sceneVal} แต่โค้ดตั้งใจ {codeVal}");
            }
        }

        Console.WriteLine($"    เทียบ {compared} ฟิลด์ พบไม่ตรง {drifted}");
        Check(drifted == 0, $"{className}: ทุกฟิลด์ที่ซีน serialize ไว้ตรงกับ default ในโค้ด");
    }

    static void Exact(Dictionary<string, string> blk, string field, double want, string note)
    {
        string raw = blk != null && blk.ContainsKey(field) ? blk[field] : "(ไม่มีในซีน)";
        double got;
        bool ok = TryScalar(raw, out got) && Math.Abs(got - want) < 0.0005;
        Check(ok, $"{field} = {want} ในไฟล์ซีน (อ่านได้ {raw}) — {note}");
    }

    public static int Main(string[] args)
    {
        string path = args.Length > 0 ? args[0] : "Scenes/SampleScene.unity";
        if (!File.Exists(path))
        {
            Console.WriteLine($"หาไฟล์ซีนไม่เจอ: {path}");
            return 1;
        }
        string[] lines = File.ReadAllLines(path);
        Console.WriteLine($"ตรวจ {path} ({lines.Length} บรรทัด)");

        AuditComponent(lines, new AdaptiveController(), "AdaptiveController");
        AuditComponent(lines, new ScenarioManager(),    "ScenarioManager");
        AuditComponent(lines, new FixedController(),    "FixedController");

        // ===== เจาะเฉพาะ 6 ตัวที่เคยเพี้ยนจริง — ต้องอ่านตรง ๆ จากไฟล์ซีน ไม่ผ่าน reflection =====
        Console.WriteLine("\n--- ค่าที่เคยเพี้ยน (อ่านจาก .unity ตรง ๆ) ---");
        var ad = ParseBlock(lines, "AdaptiveController");
        var sm = ParseBlock(lines, "ScenarioManager");

        Exact(ad, "startupLostTime",        2.95,  "วัดจริง ไม่ใช่ 1 ที่เคยค้าง");
        Exact(ad, "saturationHeadway",      2.31,  "วัดจริง ไม่ใช่ 2.5 ของ proposal");
        Exact(sm, "repetitionsPerScenario", 27,    "270 runs ไม่ใช่ 10");
        Exact(sm, "simulationSpeed",        10,    "10x หลัง sub-stepping (Kimmy 10 ก.ย.)");
        Exact(sm, "a2Share",                0.054, "นับตรง SP1_HANDOFF_v2 s5 ไม่ใช่ 0.18 ที่ back-calculate");

        // ค่าที่ซีนถืออยู่ต้องตรงกับ const ในโค้ดด้วย (กันแก้ const แล้วลืมแก้ซีน)
        Exact(ad, "startupLostTime",   AdaptiveController.MEASURED_STARTUP_LOST_TIME,  "ตรงกับ const ในโค้ด");
        Exact(ad, "saturationHeadway", AdaptiveController.MEASURED_SATURATION_HEADWAY, "ตรงกับ const ในโค้ด");
        Exact(sm, "a2Share",           ScenarioManager.MEASURED_A2_SHARE,              "ตรงกับ const ในโค้ด");
        Exact(sm, "repetitionsPerScenario", ScenarioManager.BATCH_REPETITIONS,         "ตรงกับ const ในโค้ด");

        // ===== batch scenario list ที่ซีน serialize ไว้ (ALLOWED ข้าม "scenarios" ใน reflection จึงต้องเจาะเอง) =====
        Console.WriteLine("\n--- scenario list ในซีน ---");
        {
            var names = new List<string>();
            bool inSm = false, inList = false;
            string midCB = null, midCA = null, cur = null;
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
                if (cur == "MiddayNormal" && ln.StartsWith("    - laneName: ") && i + 1 < lines.Length)
                {
                    string lane = ln.Substring(16).Trim();
                    string val = lines[i + 1].Trim().Replace("meanInterval:", "").Trim();
                    if (lane == "CB") midCB = val;
                    if (lane == "CA") midCA = val;
                }
            }
            Console.WriteLine("    " + string.Join(", ", names.ToArray()));
            Check(names.Count == 4, $"ซีนมี 4 scenarios ({names.Count})");
            Check(!names.Contains("Night"), "ไม่มี Night ในซีน (ตัดออก 10 ก.ย.)");
            Check(names.Contains("MorningPeak") && names.Contains("EveningPeak")
               && names.Contains("MiddayNormal") && names.Contains("ABACEvent"), "ครบ 4 ตัวที่ต้องมี");
            Check(midCB == "10.1", $"MiddayNormal CB = 10.1 (วัดจริงจาก Itim) ไม่ใช่ 7 (อ่านได้ {midCB})");
            Check(midCA == "13.2", $"MiddayNormal CA = 13.2 (วัดจริงจาก Itim) ไม่ใช่ 7 (อ่านได้ {midCA})");
        }

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
