using UnityEngine;
using System.Collections;

public class AdaptiveController : MonoBehaviour
{
    [Header("Traffic Lights")]
    public TrafficLight lightA;
    public TrafficLight lightB;
    public TrafficLight lightC;

    [Header("ROI Zones (ใช้ข้อมูลตัดสินใจ)")]
    public ROIZone roiA;
    public ROIZone roiB;
    public ROIZone roiC;

    [Header("Timing Formula (HCM-based)")]
    public float gMin = 8f;
    public float gMax = 45f;
    public float yellowDuration = 3f;
    public float allRedDuration = 2f;
    public float startupLostTime = 3f;
    public float saturationHeadway = 2.5f;

    [Header("Counting Window")]
    public float countingWindow = 4f;
    public int demandThresholdSkipWait = 3;

    [Header("Gap-out")]
    public bool enableGapOut = true;
    public float gapOutDelay = 3f;

    [Header("Anti-Starvation (รอได้ไม่เกินกี่วิ)")]
    public float maxWaitTime = 45f;
    [Tooltip("ต้องเขียวอย่างน้อยเท่านี้ก่อนจะโดนตัดสั้นได้ (กันไฟสั้นเกินระบายไม่ทัน)")]
    public float minGreenBeforeInterrupt = 20f;
    [Tooltip("สัดส่วนของ maxWaitTime ที่จะบังคับเลือกเป็นเฟสถัดไป (ทางออกฉุกเฉิน)")]
    public float starvationSelectThreshold = 0.95f;
    [Tooltip("สัดส่วนของ maxWaitTime ที่จะตัดจบเฟสปัจจุบันก่อนเวลา")]
    public float starvationInterruptThreshold = 0.90f;

    [Header("Debug Display")]
    public bool showDebugGUI = true;

    private TrafficLight[] lights;
    private ROIZone[] rois;
    private string[] laneNames = { "A", "B", "C" };

    private int activeLaneIndex = -1;
    private TrafficLight.LightState[] currentPhaseState;
    private float phaseTimeRemaining = 0f;
    private string statusText = "Idle";

    // ===== Awake() ทำงานทันทีตอนโหลด ไม่ต้องรอ enable ต่างจาก Start() =====
    void Awake()
    {
        lights = new TrafficLight[] { lightA, lightB, lightC };
        rois = new ROIZone[] { roiA, roiB, roiC };
        currentPhaseState = new TrafficLight.LightState[] {
            TrafficLight.LightState.Red, TrafficLight.LightState.Red, TrafficLight.LightState.Red
        };
    }
    public void ForceStop()
    {
        StopAllCoroutines();
        activeLaneIndex = -1;
        SetAllRed();
    }
    public void RestartCycle()
    {
        StopAllCoroutines();
        activeLaneIndex = -1;
        SetAllRed();
        StartCoroutine(RunAdaptiveCycle());
    }

    void Start()
    {
        SetAllRed();
        StartCoroutine(RunAdaptiveCycle());
    }

    void SetAllRed()
    {
        for (int i = 0; i < lights.Length; i++)
        {
            if (lights[i] != null) lights[i].SetState(TrafficLight.LightState.Red);
            currentPhaseState[i] = TrafficLight.LightState.Red;
        }
    }

    IEnumerator RunAdaptiveCycle()
    {
        while (true)
        {
            statusText = "Waiting for any demand...";
            while (ChooseNextLaneFCFS() == -1)
            {
                yield return new WaitForSeconds(0.5f);
            }

            int starvingLane = FindStarvingLane(starvationSelectThreshold);
            int candidateLane = starvingLane != -1 ? starvingLane : ChooseNextLaneFCFS();
            int candidateDemand = rois[candidateLane].GetVehicleCount();

            if (starvingLane != -1)
            {
                statusText = $"STARVATION OVERRIDE — Lane {laneNames[starvingLane]} forced";
                Debug.Log($"[Adaptive] STARVATION: Lane {laneNames[starvingLane]} waited too long, forcing green.");
                if (DataLogger.Instance != null) DataLogger.Instance.LogStarvationEvent(laneNames[starvingLane]);
            }
            else if (candidateDemand < demandThresholdSkipWait)
            {
                statusText = $"Counting window ({countingWindow}s)...";
                yield return new WaitForSeconds(countingWindow);
            }
            else
            {
                statusText = $"Demand clear ({candidateDemand} cars) — skip wait";
            }

            int nextLane = FindStarvingLane(starvationSelectThreshold);
            if (nextLane == -1) nextLane = ChooseNextLaneFCFS();
            if (nextLane == -1) continue;

            activeLaneIndex = nextLane;
            float greenTime = CalculateGreenDuration(nextLane);

            Debug.Log($"[Adaptive] Lane {laneNames[nextLane]} selected. " +
                      $"Demand={rois[nextLane].GetVehicleCount()}, GreenTime={greenTime:F1}s");

            statusText = "Green";
            SetLaneState(nextLane, TrafficLight.LightState.Green);
            yield return StartCoroutine(GreenPhaseWithInterrupts(nextLane, greenTime));

            statusText = "Yellow";
            SetLaneState(nextLane, TrafficLight.LightState.Yellow);
            yield return StartCoroutine(CountdownPhase(yellowDuration));

            statusText = "All-Red";
            SetLaneState(nextLane, TrafficLight.LightState.Red);
            yield return StartCoroutine(CountdownPhase(allRedDuration));

            activeLaneIndex = -1;
        }
    }

    int FindStarvingLane(float thresholdRatio)
    {
        float threshold = maxWaitTime * thresholdRatio;

        for (int i = 0; i < rois.Length; i++)
        {
            if (rois[i] == null) continue;
            if (i == activeLaneIndex) continue;
            if (rois[i].GetVehicleCount() == 0) continue;

            float waitTime = rois[i].GetLongestWaitTime();
            if (waitTime >= threshold)
            {
                return i;
            }
        }
        return -1;
    }

    IEnumerator GreenPhaseWithInterrupts(int laneIndex, float maxGreenTime)
    {
        float elapsed = 0f;
        float emptyTimer = 0f;

        while (elapsed < maxGreenTime)
        {
            float dt = Time.deltaTime;
            elapsed += dt;
            phaseTimeRemaining = maxGreenTime - elapsed;

            if (enableGapOut)
            {
                int currentCount = rois[laneIndex].GetVehicleCount();
                if (currentCount == 0)
                {
                    emptyTimer += dt;
                    if (emptyTimer >= gapOutDelay)
                    {
                        Debug.Log($"[Adaptive] Lane {laneNames[laneIndex]} gap-out at {elapsed:F1}s");
                        yield break;
                    }
                }
                else
                {
                    emptyTimer = 0f;
                }
            }

            if (elapsed >= minGreenBeforeInterrupt)
            {
                int starvingOther = FindStarvingLane(starvationInterruptThreshold);
                if (starvingOther != -1 && starvingOther != laneIndex)
                {
                    float otherWait = rois[starvingOther].GetLongestWaitTime();
                    Debug.Log($"[Adaptive] Cutting Lane {laneNames[laneIndex]} short at {elapsed:F1}s " +
                              $"(after min {minGreenBeforeInterrupt}s served) to prevent Lane {laneNames[starvingOther]} " +
                              $"starvation (waited {otherWait:F1}s)");
                    if (DataLogger.Instance != null) DataLogger.Instance.LogStarvationEvent(laneNames[starvingOther]);
                    yield break;
                }
            }

            yield return null;
        }
    }

    IEnumerator CountdownPhase(float duration)
    {
        float t = duration;
        while (t > 0f)
        {
            phaseTimeRemaining = t;
            t -= Time.deltaTime;
            yield return null;
        }
        phaseTimeRemaining = 0f;
    }

    void SetLaneState(int laneIndex, TrafficLight.LightState state)
    {
        lights[laneIndex].SetState(state);
        currentPhaseState[laneIndex] = state;
    }

    int ChooseNextLaneFCFS()
    {
        int bestLane = -1;
        float oldestTime = Mathf.Infinity;

        for (int i = 0; i < rois.Length; i++)
        {
            if (rois[i] == null) continue;
            if (rois[i].GetVehicleCount() == 0) continue;

            float arrivalTime = rois[i].GetOldestArrivalTime();
            if (arrivalTime < oldestTime)
            {
                oldestTime = arrivalTime;
                bestLane = i;
            }
        }
        return bestLane;
    }

    float CalculateGreenDuration(int laneIndex)
    {
        float d_i = rois[laneIndex].GetVehicleCount();
        if (d_i <= 0f) return gMin;

        float neededTime = startupLostTime + d_i * saturationHeadway;
        return Mathf.Clamp(neededTime, gMin, gMax);
    }

    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying) return;

        float boxWidth = 230;
        float startX = Screen.width - boxWidth - 10;

        GUI.Box(new Rect(startX, 10, boxWidth, 25), "");
        GUI.Label(new Rect(startX + 10, 12, boxWidth - 20, 20), $"Status: {statusText}");

        for (int i = 0; i < 3; i++)
        {
            float yPos = 45 + i * 65;

            Color stateColor = GetStateColor(currentPhaseState[i]);
            string stateName = currentPhaseState[i].ToString().ToUpper();

            GUI.Box(new Rect(startX, yPos, boxWidth, 55), "");

            Color oldColor = GUI.color;
            GUI.color = stateColor;
            GUI.Box(new Rect(startX + 5, yPos + 5, 20, 20), "");
            GUI.color = oldColor;

            GUI.Label(new Rect(startX + 35, yPos + 5, 190, 20), $"Lane {laneNames[i]} — {stateName}");

            if (i == activeLaneIndex)
            {
                GUI.Label(new Rect(startX + 35, yPos + 25, 190, 20), $"Time left: {phaseTimeRemaining:F1}s");
            }
            else
            {
                float wait = rois[i] != null ? rois[i].GetLongestWaitTime() : 0f;
                GUI.Label(new Rect(startX + 35, yPos + 25, 190, 20), $"Waiting: {wait:F1}s / {maxWaitTime:F0}s");
            }
        }
    }

    Color GetStateColor(TrafficLight.LightState state)
    {
        switch (state)
        {
            case TrafficLight.LightState.Green: return Color.green;
            case TrafficLight.LightState.Yellow: return Color.yellow;
            default: return Color.red;
        }
    }
}