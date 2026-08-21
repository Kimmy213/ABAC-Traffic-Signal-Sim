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
    const string SUMMARY_HEADER =
        "ControllerType,Scenario,RunTimestamp,TotalSpawned,VehiclesCleared,AvgWaitTime,MaxWaitTime,StarvationEvents";

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

        sb.AppendLine($"{controllerType},{scenarioName},{System.DateTime.Now:yyyy-MM-dd HH:mm:ss}," +
                       $"{totalSpawnedCount},{GetVehicleCount()},{GetAverageWaitTime():F2},{GetMaxWaitTime():F2},{GetStarvationCount()}");

        File.AppendAllText(path, sb.ToString());
        Debug.Log($"[DataLogger] Appended summary to: {path}");
    }

    [ContextMenu("Reset Run Data")]
    public void ResetRun()
    {
        records.Clear();
        starvationEventCount = 0;
        runStartTime = Time.time;
        Debug.Log("[DataLogger] Run data reset.");
    }

    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying) return;

        float boxWidth = 250;
        GUI.Box(new Rect(10, Screen.height - 160, boxWidth, 150), "");
        GUI.Label(new Rect(20, Screen.height - 155, boxWidth - 20, 20), $"[{controllerType}] {scenarioName}");
        GUI.Label(new Rect(20, Screen.height - 135, boxWidth - 20, 20), $"Total spawned: {totalSpawnedCount}");
        GUI.Label(new Rect(20, Screen.height - 115, boxWidth - 20, 20), $"Vehicles cleared: {GetVehicleCount()}");
        GUI.Label(new Rect(20, Screen.height - 95, boxWidth - 20, 20), $"Avg wait: {GetAverageWaitTime():F1}s");
        GUI.Label(new Rect(20, Screen.height - 75, boxWidth - 20, 20), $"Max wait: {GetMaxWaitTime():F1}s");
        GUI.Label(new Rect(20, Screen.height - 55, boxWidth - 20, 20), $"Starvation events: {GetStarvationCount()}");

        if (GUI.Button(new Rect(20, Screen.height - 30, 110, 22), "Export CSV"))
        {
            ExportDetailedCSV();
            ExportSummaryCSV();
        }
        if (GUI.Button(new Rect(140, Screen.height - 30, 110, 22), "Reset Run"))
        {
            ResetRun();
        }
    }
}