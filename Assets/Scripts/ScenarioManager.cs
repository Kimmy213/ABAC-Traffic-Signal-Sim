using UnityEngine;
using System.Collections;

public class ScenarioManager : MonoBehaviour
{
    [System.Serializable]
    public class ScenarioDef
    {
        public string name;
        public float laneA_interval;
        public float laneB_interval;
        public float laneC_interval;
    }

    [Header("References")]
    public CarSpawner spawnerA;
    public CarSpawner spawnerB;
    public CarSpawner spawnerC;
    public ROIZone roiA;
    public ROIZone roiB;
    public ROIZone roiC;
    public FixedController fixedController;
    public AdaptiveController adaptiveController;
    public DataLogger dataLogger;

    [Header("Run Settings")]
    public float runDuration = 1800f;  // 30 นาทีจำลอง
    public int repetitionsPerScenario = 27;
    [Tooltip("เร่งความเร็ว simulation ระหว่าง batch run (1 = ปกติ, 10 = เร็ว 10 เท่า)")]
    public float simulationSpeed = 10f;

    [Header("Performance (ปิด GUI ระหว่าง batch เพื่อความเร็ว)")]
    public bool disableGUIDuringBatch = true;

    [Header("Arrival Interval Mapping (วินาทีเฉลี่ยระหว่างรถ)")]
    public float veryHigh = 1.5f;
    public float high = 2.5f;
    public float medium = 4f;
    public float low = 7f;
    public float veryLow = 15f;

    private bool isRunning = false;
    private int completedRuns = 0;
    private int totalRuns;

    ScenarioDef[] GetScenarios()
    {
        return new ScenarioDef[]
        {
            new ScenarioDef{ name="MorningPeak",  laneA_interval=high,    laneB_interval=medium,  laneC_interval=low },
            new ScenarioDef{ name="EveningPeak",  laneA_interval=medium,  laneB_interval=high,    laneC_interval=high },
            new ScenarioDef{ name="MiddayNormal", laneA_interval=low,     laneB_interval=low,     laneC_interval=low },
            new ScenarioDef{ name="ABACEvent",    laneA_interval=low,     laneB_interval=low,     laneC_interval=veryHigh },
            new ScenarioDef{ name="Night",        laneA_interval=veryLow, laneB_interval=veryLow, laneC_interval=veryLow },
        };
    }

    [ContextMenu("Start Batch Run (All 135)")]
    public void StartBatchRun()
    {
        if (isRunning)
        {
            Debug.LogWarning("[ScenarioManager] Already running!");
            return;
        }
        StartCoroutine(RunAllExperiments());
    }

    [ContextMenu("Stop Batch Run")]
    public void StopBatchRun()
    {
        StopAllCoroutines();
        Time.timeScale = 1f;
        isRunning = false;
        Debug.Log("[ScenarioManager] Batch run stopped manually.");
    }

    IEnumerator RunAllExperiments()
    {
        isRunning = true;
        completedRuns = 0;
        ScenarioDef[] scenarios = GetScenarios();
        totalRuns = scenarios.Length * repetitionsPerScenario * 2;

        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 0;
        Time.timeScale = simulationSpeed;

        bool[] savedGUIStates = null;
        if (disableGUIDuringBatch)
        {
            savedGUIStates = new bool[] {
                dataLogger.showDebugGUI, roiA.showDebugGUI, roiB.showDebugGUI, roiC.showDebugGUI,
                adaptiveController.showDebugGUI
            };
            dataLogger.showDebugGUI = false;
            roiA.showDebugGUI = false;
            roiB.showDebugGUI = false;
            roiC.showDebugGUI = false;
            adaptiveController.showDebugGUI = false;
        }

        for (int s = 0; s < scenarios.Length; s++)
        {
            for (int rep = 1; rep <= repetitionsPerScenario; rep++)
            {
                yield return StartCoroutine(RunSingleExperiment(scenarios[s], rep, DataLogger.ControllerType.Fixed));
                yield return StartCoroutine(RunSingleExperiment(scenarios[s], rep, DataLogger.ControllerType.Adaptive));
            }
        }

        Time.timeScale = 1f;
        Application.targetFrameRate = -1;
        QualitySettings.vSyncCount = 1;

        if (disableGUIDuringBatch && savedGUIStates != null)
        {
            dataLogger.showDebugGUI = savedGUIStates[0];
            roiA.showDebugGUI = savedGUIStates[1];
            roiB.showDebugGUI = savedGUIStates[2];
            roiC.showDebugGUI = savedGUIStates[3];
            adaptiveController.showDebugGUI = savedGUIStates[4];
        }

        isRunning = false;
        Debug.Log($"[ScenarioManager] ═══ ALL {totalRuns} RUNS COMPLETE ═══");
    }

    IEnumerator RunSingleExperiment(ScenarioDef scenario, int repetition, DataLogger.ControllerType ctype)
    {
        int seed = scenario.name.GetHashCode() + repetition * 31;
        Random.InitState(seed);

        spawnerA.ClearAllCars();
        spawnerB.ClearAllCars();
        spawnerC.ClearAllCars();
        spawnerA.ResetSpawnCount();
        spawnerB.ResetSpawnCount();
        spawnerC.ResetSpawnCount();
        roiA.ClearAll();
        roiB.ClearAll();
        roiC.ClearAll();
        dataLogger.ResetRun();

        spawnerA.SetArrivalRate(scenario.laneA_interval);
        spawnerB.SetArrivalRate(scenario.laneB_interval);
        spawnerC.SetArrivalRate(scenario.laneC_interval);

        // ===== หยุด coroutine ของทั้ง 2 controller ก่อนเสมอ กันไฟเขียวพร้อมกัน =====
        fixedController.ForceStop();
        adaptiveController.ForceStop();

        bool useFixed = (ctype == DataLogger.ControllerType.Fixed);
        fixedController.enabled = useFixed;
        adaptiveController.enabled = !useFixed;

        if (useFixed) fixedController.RestartCycle();
        else adaptiveController.RestartCycle();

        dataLogger.controllerType = ctype;
        dataLogger.scenarioName = $"{scenario.name}_Rep{repetition}";

        completedRuns++;
        Debug.Log($"[ScenarioManager] ({completedRuns}/{totalRuns}) Running {dataLogger.scenarioName} [{ctype}]...");

        yield return new WaitForSeconds(runDuration);

        int totalSpawned = spawnerA.totalSpawned + spawnerB.totalSpawned + spawnerC.totalSpawned;
        dataLogger.SetTotalSpawned(totalSpawned);

        dataLogger.ExportDetailedCSV();
        dataLogger.ExportSummaryCSV();

        Debug.Log($"[ScenarioManager] ✓ Completed {dataLogger.scenarioName} [{ctype}]. " +
                  $"Vehicles={dataLogger.GetVehicleCount()}, AvgWait={dataLogger.GetAverageWaitTime():F1}s, " +
                  $"Starvation={dataLogger.GetStarvationCount()}");
    }

    void OnGUI()
    {
        if (!isRunning) return;

        GUI.Box(new Rect(Screen.width / 2 - 150, 10, 300, 60), "");
        GUI.Label(new Rect(Screen.width / 2 - 140, 15, 280, 20), $"Batch Run: {completedRuns}/{totalRuns}");
        GUI.Label(new Rect(Screen.width / 2 - 140, 35, 280, 20), $"Time Scale: {Time.timeScale}x");
    }
}