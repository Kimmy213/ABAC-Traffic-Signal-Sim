using UnityEngine;
using System.Collections.Generic;
using System.IO;
using System.Text;

public class DataLogger : MonoBehaviour
{
    public enum ControllerType { Fixed, Adaptive }

    [Header("Run Identity")]
    public ControllerType controllerType = ControllerType.Adaptive;
    public string scenarioName = "Scenario1";

    [Header("Debug Display")]
    public bool showDebugGUI = true;

    public static DataLogger Instance;

    private struct VehicleRecord
    {
        public float waitTime;
        public string lane;
        public string driverType;
        public float timestamp;
    }

    /// <summary>
    /// หนึ่งเฟสไฟเขียว — บันทึกไว้เพื่อวัด "เขียวเปล่า" ที่ gap-out ปัจจุบันมองไม่เห็น
    ///
    /// ที่มา (Ratchnon): gap-out ยิงเมื่อ "ทุกเลนในกลุ่มว่าง" ถ้าเลนหนึ่งว่างก่อน มันจะติดเขียวเปล่ารอ
    /// อีกเลนจนหมด เช่นกลุ่ม {B=10, Bx=3} -> G = 26.05s แต่ Bx ว่างตั้งแต่ ~9.88s
    /// = เขียวเปล่า 16.17s และตลอดช่วงนั้น Bx บล็อก A อยู่ (A ชนกับ Bx แต่ไม่ชนกับ B)
    ///
    /// SkippedHeads มองเคสนี้ไม่เห็น เพราะเลนนั้น "มีรถ" ตอนเฟสเริ่ม
    /// ต้องเทียบ greenGiven กับเวลาที่แต่ละเลนว่างจริง จึงจะรู้ว่าคุ้มแก้ไหม
    /// </summary>
    private struct PhaseRecord
    {
        public int index;
        public float startTime;
        public string group;
        public float greenGiven;      // G ที่สูตรคำนวณให้
        public float greenUsed;       // เวลาที่ใช้จริง (อาจสั้นกว่าถ้าโดน interrupt)
        public string endReason;
        public string laneEmptyAt;    // "B=25.1;Bx=9.9" — วินาทีที่แต่ละเลนว่าง (nan = ไม่เคยว่าง)
        public float idleLaneSeconds; // ผลรวมเวลาที่เลนว่างแล้วยังติดเขียว = เขียวเปล่าที่กู้คืนได้
    }

    private List<PhaseRecord> phases = new List<PhaseRecord>();

    private List<VehicleRecord> records = new List<VehicleRecord>();
    private int starvationEventCount = 0;
    private float runStartTime;
    private int totalSpawnedCount = 0;

    public void SetTotalSpawned(int count)
    {
        totalSpawnedCount = count;
    }

    public int GetTotalSpawned() => totalSpawnedCount;
    void Awake()
    {
        Instance = this;
    }

    void Start()
    {
        runStartTime = Time.time;
    }

    public void LogVehicle(string lane, float waitTime, string driverType)
    {
        records.Add(new VehicleRecord
        {
            waitTime = waitTime,
            lane = lane,
            driverType = driverType,
            timestamp = Time.time - runStartTime
        });
    }

    /// <summary>บันทึกหนึ่งเฟส — AdaptiveController เรียกตอนไฟเขียวจบ</summary>
    public void LogPhase(int index, float startTime, string group, float greenGiven, float greenUsed,
                         string endReason, string laneEmptyAt, float idleLaneSeconds)
    {
        phases.Add(new PhaseRecord
        {
            index = index,
            startTime = startTime - runStartTime,
            group = group,
            greenGiven = greenGiven,
            greenUsed = greenUsed,
            endReason = endReason,
            laneEmptyAt = laneEmptyAt,
            idleLaneSeconds = idleLaneSeconds
        });
    }

    /// <summary>รวมเวลา "เลนว่างแล้วยังติดเขียว" ทั้ง run — ตัวเลขที่ตอบว่าคุ้มแก้ gap-out ไหม</summary>
    public float GetIdleLaneSeconds()
    {
        float t = 0f;
        foreach (PhaseRecord p in phases) t += p.idleLaneSeconds;
        return t;
    }

    public int GetPhaseCount() => phases.Count;

    public void LogStarvationEvent(string lane)
    {
        starvationEventCount++;
        Debug.Log($"[DataLogger] Starvation event #{starvationEventCount} on Lane {lane}");
    }

    public int GetVehicleCount() => records.Count;
    public int GetStarvationCount() => starvationEventCount;

    public float GetAverageWaitTime()
    {
        if (records.Count == 0) return 0f;
        float sum = 0f;
        foreach (var r in records) sum += r.waitTime;
        return sum / records.Count;
    }

    public float GetMaxWaitTime()
    {
        float max = 0f;
        foreach (var r in records) if (r.waitTime > max) max = r.waitTime;
        return max;
    }

    string GetExportFolder()
    {
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        string folder = Path.Combine(projectRoot, "SimulationResults");

        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }

        return folder;
    }

    [ContextMenu("Export Detailed CSV")]
    public void ExportDetailedCSV()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("ControllerType,Scenario,Timestamp,Lane,DriverType,WaitTime");

        foreach (var r in records)
        {
            sb.AppendLine($"{controllerType},{scenarioName},{r.timestamp:F2},{r.lane},{r.driverType},{r.waitTime:F2}");
        }

        string folder = GetExportFolder();
        string fileName = $"detailed_{controllerType}_{scenarioName}_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv";
        string path = Path.Combine(folder, fileName);
        File.WriteAllText(path, sb.ToString());

        Debug.Log($"[DataLogger] Exported {records.Count} records to: {path}");
    }

    // ===== หัวตารางปัจจุบันของ summary CSV =====
    // ⚠️ ถ้าแก้บรรทัดนี้เมื่อไหร่ ไฟล์เก่าจะถูกย้ายไปเก็บอัตโนมัติ ไม่ append ปนกัน
    // StartupLostTime / SaturationHeadway = ค่าที่ "ใช้จริงตอนรันรอบนั้น" ไม่ใช่ค่าที่เขียนไว้ในโค้ด
    // มีไว้เพื่อให้ทุกแถวของผลลัพธ์พิสูจน์ตัวเองได้ว่าใช้ค่าที่วัดจริง (2.95 / 2.31) หรือค่าตำรา
    // StartupLostTime / SaturationHeadway = ค่าที่ "ใช้จริงตอนรันรอบนั้น" ไม่ใช่ค่าที่เขียนไว้ในโค้ด
    // มีไว้เพื่อให้ทุกแถวของผลลัพธ์พิสูจน์ตัวเองได้ว่าใช้ค่าที่วัดจริง (2.95 / 2.31) หรือค่าตำรา
    //
    // สี่คอลัมน์ท้ายเป็นของ SODA (sampled observation):
    //   SnapshotInterval  ยืนยันว่า run นี้ใช้ 2 วิจริง (เผื่อทำ sensitivity 1/2/4 วิ)
    //   CollectCount      COLLECT ถูกเรียกกี่ครั้ง — คาดว่าตอนพีคเกือบ 0 เพราะอยู่ใน SERVE loop ตลอด
    //   SkippedHeads      หัวไฟที่ count = 0 แล้วถูกข้าม = เวลาที่ประหยัดตรง ๆ เทียบ Fixed
    //   QueueStartResets  สูงมาก = อยู่ใน saturated regime ตลอด (ลำดับถูกกำหนดโดยกฎ reset)
    const string SUMMARY_HEADER =
        "ControllerType,Scenario,RunTimestamp,TotalSpawned,VehiclesCleared,AvgWaitTime,MaxWaitTime,StarvationEvents,StartupLostTime,SaturationHeadway,SnapshotInterval,CollectCount,CollectDuration,SkippedHeads,QueueStartResets,IdleLaneSeconds,TieBreaksApplied,TieBreaksExcludingBx";

    [ContextMenu("Export Summary CSV (Append)")]
    public void ExportSummaryCSV()
    {
        string folder = GetExportFolder();
        string path = Path.Combine(folder, "summary_all_runs.csv");

        // ===== กันไฟล์สคีมาปนกัน =====
        // เดิม: ถ้าไฟล์มีอยู่แล้วจะ append เลยโดยไม่ดูหัวตาราง
        // พอมีการเพิ่ม/ลดคอลัมน์ทีหลัง แถวใหม่จะมีจำนวนคอลัมน์ไม่ตรงหัว -> pandas อ่านไม่ออก
        // ตอนนี้: ถ้าหัวตารางไม่ตรง ย้ายไฟล์เก่าไปเก็บไว้แล้วเริ่มไฟล์ใหม่
        bool needHeader = true;

        if (File.Exists(path))
        {
            string existingHeader = "";
            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    existingHeader = reader.ReadLine() ?? "";
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[DataLogger] อ่านหัวตารางเดิมไม่ได้: {e.Message}");
            }

            if (existingHeader.Trim() == SUMMARY_HEADER)
            {
                needHeader = false;
            }
            else
            {
                string backup = Path.Combine(folder,
                    $"summary_all_runs_OLD_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
                try
                {
                    File.Move(path, backup);
                    Debug.LogWarning($"[DataLogger] หัวตารางของ summary เดิมไม่ตรงกับรูปแบบปัจจุบัน " +
                                     $"— ย้ายไฟล์เก่าไปที่ {Path.GetFileName(backup)} แล้วเริ่มไฟล์ใหม่");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[DataLogger] ย้ายไฟล์เก่าไม่สำเร็จ ({e.Message}) — " +
                                   $"ข้อมูลอาจปนกัน ให้ลบ summary_all_runs.csv เองก่อนรันใหม่");
                    needHeader = false;
                }
            }
        }

        StringBuilder sb = new StringBuilder();
        if (needHeader) sb.AppendLine(SUMMARY_HEADER);

        AdaptiveController ac = FindFirstObjectByType<AdaptiveController>();
        string l1   = ac != null ? ac.startupLostTime.ToString("F2")   : "NA";
        string hh   = ac != null ? ac.saturationHeadway.ToString("F2") : "NA";
        string snap = ac != null ? ac.SnapshotInterval.ToString("F1")  : "NA";
        string cc   = ac != null ? ac.CollectCount.ToString()          : "NA";
        string cd   = ac != null ? ac.CollectTotalTime.ToString("F1")  : "NA";
        string sk   = ac != null ? ac.SkippedHeadTotal.ToString()      : "NA";
        string qr   = ac != null ? ac.QueueStartResets.ToString()      : "NA";
        string tb   = ac != null ? ac.TieBreaksApplied.ToString()      : "NA";
        string tbx  = ac != null ? ac.TieBreaksExcludingBx.ToString()  : "NA";

        sb.AppendLine($"{controllerType},{scenarioName},{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}," +
                       $"{totalSpawnedCount},{GetVehicleCount()},{GetAverageWaitTime():F2},{GetMaxWaitTime():F2},{GetStarvationCount()}," +
                       $"{l1},{hh},{snap},{cc},{cd},{sk},{qr},{GetIdleLaneSeconds():F1},{tb},{tbx}");

        File.AppendAllText(path, sb.ToString());
        Debug.Log($"[DataLogger] Appended summary to: {path}");
    }

    [ContextMenu("Export Phase Trace CSV")]
    public void ExportPhaseCSV()
    {
        if (phases.Count == 0) return;

        StringBuilder sb = new StringBuilder();
        sb.AppendLine("ControllerType,Scenario,PhaseIndex,StartTime,Group,GreenGiven,GreenUsed,EndReason,LaneEmptyAt,IdleLaneSeconds");
        foreach (PhaseRecord p in phases)
        {
            sb.AppendLine($"{controllerType},{scenarioName},{p.index},{p.startTime:F2},{p.group}," +
                          $"{p.greenGiven:F2},{p.greenUsed:F2},{p.endReason},{p.laneEmptyAt},{p.idleLaneSeconds:F2}");
        }

        string folder = GetExportFolder();
        string path = Path.Combine(folder,
            $"phases_{controllerType}_{scenarioName}_{System.DateTime.Now:yyyyMMdd_HHmmss}.csv");
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[DataLogger] Exported {phases.Count} phases to: {path} " +
                  $"(idle-lane total {GetIdleLaneSeconds():F1}s)");
    }

    [ContextMenu("Reset Run Data")]
    public void ResetRun()
    {
        phases.Clear();
        records.Clear();
        starvationEventCount = 0;
        runStartTime = Time.time;
        Debug.Log("[DataLogger] Run data reset.");
    }

    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying) return;

        UITheme.Begin();

        const float W = 262f, H = 168f;
        Rect card = new Rect(12f, Screen.height - H - 12f, W, H);
        GUI.Box(card, GUIContent.none);

        float x = card.x + 12f, w = W - 24f, y = card.y + 8f;

        GUI.Label(new Rect(x, y, w - 80f, 18f), "MEASUREMENTS", UITheme.Title);
        UITheme.Pill(new Rect(card.x + W - 84f, y + 1f, 72f, 17f), controllerType.ToString(),
                     controllerType == ControllerType.Adaptive ? UITheme.Accent : UITheme.Neutral);
        y += 22f;
        GUI.Label(new Rect(x, y, w, 15f), scenarioName, UITheme.Dim);
        y += 18f;
        UITheme.Rule(new Rect(x, y, w, 1f)); y += 6f;

        UITheme.Row(new Rect(x, y, w, 17f), "Released",      totalSpawnedCount.ToString());   y += 18f;
        UITheme.Row(new Rect(x, y, w, 17f), "Cleared",       GetVehicleCount().ToString());   y += 18f;
        UITheme.Row(new Rect(x, y, w, 17f), "Mean wait",     $"{GetAverageWaitTime():F1}s");  y += 18f;
        UITheme.Row(new Rect(x, y, w, 17f), "Max wait",      $"{GetMaxWaitTime():F1}s");      y += 18f;

        int starv = GetStarvationCount();
        GUI.Label(new Rect(x, y, w * 0.58f, 17f), "Starvation", UITheme.Body);
        GUIStyle sv = new GUIStyle(UITheme.Value);
        sv.normal.textColor = starv > 0 ? UITheme.SigRed : UITheme.SigGreen;
        GUI.Label(new Rect(x + w * 0.58f, y, w * 0.42f, 17f), starv.ToString(), sv);
        y += 22f;

        if (GUI.Button(new Rect(x, y, w * 0.5f - 3f, 22f), "Export CSV"))
        {
            ExportDetailedCSV();
            ExportSummaryCSV();
        }
        if (GUI.Button(new Rect(x + w * 0.5f + 3f, y, w * 0.5f - 3f, 22f), "Reset Run"))
        {
            ResetRun();
        }

        UITheme.End();
    }
}