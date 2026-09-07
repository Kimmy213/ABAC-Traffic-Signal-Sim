#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

/// <summary>
/// เมนู: ABAC ▸ Verify Scene Against Code
///
/// ทำไมต้องมี (Ratchnon ชี้ถูก):
///   Unity เขียนค่าของทุก public field ลง SampleScene.unity ตอนที่ component ถูกวางลงซีน
///   แล้วตอนโหลดซีน ค่าพวกนั้นจะ "ทับ" ค่า default ที่เขียนไว้ในไฟล์ .cs เสมอ
///   => แก้สคริปต์อย่างเดียวไม่มีผลกับ component ที่อยู่ในซีนแล้ว
///   เคยทำให้ซีนค้างที่ startupLostTime=1 / saturationHeadway=2.5 / countingWindow=8
///   และ repetitionsPerScenario=1 (batch ได้ 10 runs แทน 270) / simulationSpeed=2 / a2Share=0.18
///
/// วิธีตรวจ: สร้าง component ชั่วคราวขึ้นมาหนึ่งตัว (= ค่า default ที่โค้ดตั้งใจ)
/// แล้วเทียบ "ทุกฟิลด์ scalar" กับตัวที่อยู่ในซีนจริงด้วย reflection
/// จึงครอบทุกฟิลด์ ไม่ใช่แค่ตัวที่รู้จัก — drift ครั้งหน้าก็จะโดนจับด้วย
/// </summary>
public static class SceneParameterAudit
{
    /// <summary>ฟิลด์ที่ตั้งใจให้ต่างจาก default ได้ (ไม่ใช่ drift)</summary>
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
            ? "✓ ซีนตรงกับโค้ดทุกฟิลด์"
            : $"✗ พบ {drift} ฟิลด์ที่ซีนทับค่าของโค้ดไว้";

        string body = report.Length == 0 ? "ไม่มีอะไรผิดปกติ" : report.ToString();

        if (drift == 0) Debug.Log($"[SceneParameterAudit] {head}\n{body}");
        else            Debug.LogError($"[SceneParameterAudit] {head}\n{body}");

        EditorUtility.DisplayDialog("Verify Scene Against Code",
            head + "\n\n" + body +
            (drift > 0
                ? "\n\nวิธีแก้: เลือก GameController ใน Hierarchy → คลิกขวาที่หัว component " +
                  "→ Load Measured Parameters / Load Batch Preset → แล้ว Ctrl+S\n" +
                  "หรือกด ABAC ▸ Build 4-Phase Scene ซึ่งเรียกให้เองทั้งหมด"
                : ""),
            "OK");
    }

    static int AuditType<T>(StringBuilder report) where T : MonoBehaviour
    {
        T live = Object.FindFirstObjectByType<T>();
        if (live == null)
        {
            report.AppendLine($"{typeof(T).Name}: ไม่พบในซีน");
            return 0;
        }

        // component ชั่วคราวเพื่ออ่านค่า default ที่โค้ดตั้งใจ (MonoBehaviour สร้างด้วย new ไม่ได้)
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
                else continue;                                  // reference / list — ข้าม

                if (!same)
                {
                    drift++;
                    report.AppendLine($"{typeof(T).Name}.{f.Name}: ซีน = {a}  แต่โค้ด = {b}");
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
