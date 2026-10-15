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
    /// One green phase - recorded to measure the idle green that the current gap-out cannot see.
    ///
    /// Origin (Ratchanon): gap-out fires only when every lane in the group is empty, so if one lane
    /// empties first the group holds green waiting for the other. For {B=10, Bx=3}, G = 26.05 s but Bx
    /// is empty from ~9.88 s = 16.17 s idle green, during all of which Bx blocks A (A conflicts with Bx, not B).
    ///
    /// SkippedHeads cannot see this case, because the lane did have vehicles when the phase began.
    /// Only comparing greenGiven against when each lane actually emptied shows whether fixing it pays.
    /// </summary>
    private struct PhaseRecord
    {
        public int index;
        public float startTime;
        public string group;
        public float greenGiven;      // G as computed by the formula
        public float greenUsed;       // time actually used (shorter if an interrupt fired)
        public string endReason;
        public string laneEmptyAt;    // "B=25.1;Bx=9.9" - second each lane emptied (nan = never emptied)
        public float idleLaneSeconds; // total time a lane sat empty while still green = recoverable idle green
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

    /// <summary>Records one phase - AdaptiveController calls this when the green ends.</summary>
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

    /// <summary>Total "lane empty but still green" time over the run - answers whether fixing gap-out pays.</summary>
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

    // ===== Current header row of the summary CSV =====
    // If this line ever changes, the old file is archived automatically rather than appended to.
    // StartupLostTime / SaturationHeadway are the values actually in force for that run, not the
    // ones written in the code, so every result row can prove for itself whether it used the
    // measured constants (2.95 / 2.31) or the textbook ones.
    //
    //
    // The last four columns belong to SODA (sampled observation):
    //   SnapshotInterval  confirms this run really used 2 s (in case of a 1/2/4 s sensitivity study)
    //   CollectCount      how often COLLECT ran - expected near 0 at peak, since it stays in the SERVE loop
    //   SkippedHeads      heads with count = 0 that were skipped = time saved directly against Fixed
    //   QueueStartResets  a high value means a permanently saturated regime (order set by the reset rule)
    const string SUMMARY_HEADER =
        "ControllerType,Scenario,RunTimestamp,TotalSpawned,VehiclesCleared,AvgWaitTime,MaxWaitTime,StarvationEvents,StartupLostTime,SaturationHeadway,SnapshotInterval,CollectCount,CollectDuration,SkippedHeads,QueueStartResets,IdleLaneSeconds,TieBreaksApplied,TieBreaksExcludingBx";

    [ContextMenu("Export Summary CSV (Append)")]
    public void ExportSummaryCSV()
    {
        string folder = GetExportFolder();
        string path = Path.Combine(folder, "summary_all_runs.csv");

        // ===== Keep schemas from mixing in one file =====
        // Before: an existing file was appended to without checking its header. Adding or removing
        // a column later gave rows whose width did not match the header, and pandas could not read it.
        // Now a mismatched header archives the old file and starts a new one.
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
                Debug.LogWarning($"[DataLogger] could not read the existing header: {e.Message}");
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
                    Debug.LogWarning($"[DataLogger] the existing summary header does not match the current format " +
                                     $"- archived it as {Path.GetFileName(backup)} and started a new file");
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"[DataLogger] could not archive the old file ({e.Message}) - " +
                                   $"data may get mixed; delete summary_all_runs.csv yourself before running again");
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

        UITheme.Begin(scaled: true);

        const float W = 262f, H = 168f;
        Rect card = new Rect(12f, UITheme.Height - H - 12f, W, H);
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