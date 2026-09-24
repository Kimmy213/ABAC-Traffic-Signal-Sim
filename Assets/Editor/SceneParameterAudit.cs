#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

/// <summary>
/// Menu: ABAC > Verify Scene Against Code
///
/// Why this exists (Ratchanon spotted it):
///   Unity writes the value of every public field into SampleScene.unity when a component is
///   placed in the scene, and on load those values always override the defaults in the .cs file.
///   So editing the script alone has no effect on a component already in the scene.
///   It once left the scene stuck on startupLostTime=1 / saturationHeadway=2.5 / countingWindow=8
///   and repetitionsPerScenario=1 (a batch of 10 runs instead of 270) / simulationSpeed=2 / a2Share=0.18.
///
/// How the check works: create one throwaway component (= the defaults the code intends), then
/// compare every scalar field against the one in the scene by reflection.
/// That covers all fields, not only the known ones, so the next drift gets caught too.
/// </summary>
public static class SceneParameterAudit
{
    /// <summary>Fields allowed to differ from the default on purpose (not drift)</summary>
    static readonly HashSet<string> Allowed = new HashSet<string>
    {
        "showDebugGUI", "verboseLog", "showPanel", "controllerType", "scenarioName",
    };

    [MenuItem("ABAC/Verify Scene Against Code", false, 3)]
    public static void Verify()
    {
        var report = new StringBuilder();
        int drift = 0;

        drift += AuditType<AdaptiveController>(report);
        drift += AuditType<ScenarioManager>(report);
        drift += AuditType<FixedController>(report);

        string head = drift == 0
            ? "Scene matches the code on every field"
            : $"{drift} field(s) in the scene override the code";

        string body = report.Length == 0 ? "Nothing out of place" : report.ToString();

        if (drift == 0) Debug.Log($"[SceneParameterAudit] {head}\n{body}");
        else            Debug.LogError($"[SceneParameterAudit] {head}\n{body}");

        EditorUtility.DisplayDialog("Verify Scene Against Code",
            head + "\n\n" + body +
            (drift > 0
                ? "\n\nFix: select GameController in the Hierarchy, right-click the component header, " +
                  "choose Load Measured Parameters / Load Batch Preset, then Ctrl+S.\n" +
                  "Or run ABAC > Build 4-Phase Scene, which does all of it for you."
                : ""),
            "OK");
    }

    static int AuditType<T>(StringBuilder report) where T : MonoBehaviour
    {
        T live = Object.FindFirstObjectByType<T>();
        if (live == null)
        {
            report.AppendLine($"{typeof(T).Name}: not found in the scene");
            return 0;
        }

        // Throwaway component used to read the defaults the code intends (a MonoBehaviour cannot be newed)
        GameObject probe = new GameObject("~AuditProbe") { hideFlags = HideFlags.HideAndDontSave };
        T fresh = probe.AddComponent<T>();

        int drift = 0;
        try
        {
            foreach (FieldInfo f in typeof(T).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (Allowed.Contains(f.Name)) continue;

                object a = f.GetValue(live), b = f.GetValue(fresh);
                if (a == null || b == null) continue;

                bool same;
                if      (a is float) same = Mathf.Abs((float)a - (float)b) < 0.0005f;
                else if (a is int)   same = (int)a == (int)b;
                else if (a is bool)  same = (bool)a == (bool)b;
                else continue;                                  // reference / list - skip

                if (!same)
                {
                    drift++;
                    report.AppendLine($"{typeof(T).Name}.{f.Name}: scene = {a}  but code = {b}");
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(probe);
        }

        if (drift == 0) report.AppendLine($"{typeof(T).Name}: ✓");
        return drift;
    }
}
#endif
