using UnityEngine;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Baseline controller: cycles through a fixed phase order and ignores actual demand.
/// Supports phases that open several lanes at once (e.g. A+B, opposing through movements).
/// Originally used the 120 s cycle from DOH Thailand (2019); now 106 s, from Kimmy timing the
/// real signal on site and Itim confirming it on video (A=40s / B=65s / Bx=20-23s / CB=31s, red 73.5s).
/// See LoadABACPreset() for detail - confirmed fixed-time, not actuated (reply_to_badin_4.md item 3).
/// </summary>
public class FixedController : MonoBehaviour
{
    [System.Serializable]
    public class FixedPhase
    {
        public string phaseName = "Phase";
        [Tooltip("Lanes that get green in this phase (they must not conflict with each other)")]
        public string[] greenLanes;
        public float greenDuration = 30f;
    }

    [Header("Intersection")]
    public IntersectionConfig config;

    [Header("Fixed Phase Plan (cycled in this order)")]
    public List<FixedPhase> phases = new List<FixedPhase>();

    [Header("Inter-green Timing (ITE)")]
    public float yellowDuration = 3f;
    public float allRedDuration = 2f;

    [Header("Debug")]
    public bool showDebugGUI = false;
    public bool verboseLog = false;

    // runtime
    private List<int>[] phaseLaneIndices;   // lane names resolved to signal indices ahead of time
    private List<int> currentGreen = new List<int>();
    private int currentPhase = 0;
    private string statusText = "Idle";

    void Awake()
    {
        if (config == null) config = FindFirstObjectByType<IntersectionConfig>();
        if (config == null)
        {
            Debug.LogError("[FixedController] no IntersectionConfig found in the scene");
            return;
        }
        config.EnsureBuilt();
        BuildPhaseCache();
    }

    void BuildPhaseCache()
    {
        phaseLaneIndices = new List<int>[phases.Count];

        for (int p = 0; p < phases.Count; p++)
        {
            List<int> indices = new List<int>();
            FixedPhase phase = phases[p];

            if (phase.greenLanes != null)
            {
                foreach (string name in phase.greenLanes)
                {
                    int s = config.SignalIndexOf(name);
                    if (s == -1)
                    {
                        Debug.LogError($"[FixedController] phase '{phase.phaseName}' refers to lane '{name}' " +
                                       $"which is not in IntersectionConfig (or is not a signalised lane)");
                        continue;
                    }
                    if (!indices.Contains(s)) indices.Add(s);
                }
            }

            // ===== Check for conflicts inside one phase - guards against a misconfiguration causing a collision =====
            for (int i = 0; i < indices.Count; i++)
            {
                for (int j = i + 1; j < indices.Count; j++)
                {
                    if (config.AreConflicting(indices[i], indices[j]))
                    {
                        Debug.LogError($"[FixedController] phase '{phase.phaseName}' opens " +
                                       $"{config.SignalName(indices[i])} and {config.SignalName(indices[j])} " +
                                       $"together, but those two conflict - dropping {config.SignalName(indices[j])}");
                        indices.RemoveAt(j);
                        j--;
                    }
                }
            }

            phaseLaneIndices[p] = indices;
        }
    }

    public void ForceStop()
    {
        StopAllCoroutines();
        currentGreen.Clear();
        SetAllRed();
        statusText = "Stopped";
    }

    public void RestartCycle()
    {
        StopAllCoroutines();
        currentPhase = 0;
        currentGreen.Clear();
        BuildPhaseCache();       // in case values were edited in the Inspector during a batch
        SetAllRed();
        StartCoroutine(RunCycle());
    }

    void Start()
    {
        if (config == null) return;
        SetAllRed();
        StartCoroutine(RunCycle());
    }

    void SetAllRed()
    {
        if (config == null) return;
        for (int s = 0; s < config.SignalCount; s++)
        {
            TrafficLight light = config.SignalLight(s);
            if (light != null) light.SetState(TrafficLight.LightState.Red);
        }
    }

    void SetLanes(List<int> indices, TrafficLight.LightState state)
    {
        foreach (int s in indices)
        {
            TrafficLight light = config.SignalLight(s);
            if (light != null) light.SetState(state);
        }
    }

    IEnumerator RunCycle()
    {
        if (phases.Count == 0)
        {
            Debug.LogError("[FixedController] no phase plan set - use 'Load ABAC 4-Phase Preset' from the context menu first");
            yield break;
        }

        while (true)
        {
            List<int> greenNow = phaseLaneIndices[currentPhase];
            FixedPhase phase = phases[currentPhase];

            // ===== Turn green only the lanes not already green (continuing lanes must not flicker) =====
            List<int> turningOn = Except(greenNow, currentGreen);
            SetLanes(turningOn, TrafficLight.LightState.Green);
            currentGreen = new List<int>(greenNow);

            statusText = $"{phase.phaseName} — GREEN {config.DescribeGroup(greenNow)}";
            if (verboseLog)
                Debug.Log($"[Fixed] {phase.phaseName}: green {config.DescribeGroup(greenNow)} for {phase.greenDuration}s");

            yield return new WaitForSeconds(phase.greenDuration);

            // ===== Work out which lanes the next phase still needs; only the dropped ones go yellow =====
            int nextPhase = (currentPhase + 1) % phases.Count;
            List<int> greenNext = phaseLaneIndices[nextPhase];
            List<int> dropping = Except(greenNow, greenNext);

            if (dropping.Count > 0)
            {
                statusText = $"{phase.phaseName} — YELLOW {config.DescribeGroup(dropping)}";
                SetLanes(dropping, TrafficLight.LightState.Yellow);
                yield return new WaitForSeconds(yellowDuration);

                statusText = "ALL-RED clearance";
                SetLanes(dropping, TrafficLight.LightState.Red);
                currentGreen = Except(currentGreen, dropping);
                yield return new WaitForSeconds(allRedDuration);
            }

            currentPhase = nextPhase;
        }
    }

    static List<int> Except(List<int> source, List<int> remove)
    {
        List<int> result = new List<int>();
        foreach (int v in source)
        {
            if (!remove.Contains(v)) result.Add(v);
        }
        return result;
    }

    /// <summary>Total cycle length in seconds - used to check it against the DOH 120 s figure.</summary>
    public float GetCycleLength()
    {
        float total = 0f;
        for (int p = 0; p < phases.Count; p++)
        {
            total += phases[p].greenDuration;

            int next = (p + 1) % phases.Count;
            List<int> a = phaseLaneIndices != null && phaseLaneIndices[p] != null ? phaseLaneIndices[p] : new List<int>();
            List<int> b = phaseLaneIndices != null && phaseLaneIndices[next] != null ? phaseLaneIndices[next] : new List<int>();
            if (Except(a, b).Count > 0) total += yellowDuration + allRedDuration;
        }
        return total;
    }

    [ContextMenu("Print Cycle Length")]
    void PrintCycleLength()
    {
        if (config != null) { config.EnsureBuilt(); BuildPhaseCache(); }
        Debug.Log($"[FixedController] Cycle length = {GetCycleLength():F0}s (target 106s per SP1_HANDOFF_v2)");
    }

    /// <summary>
    /// Fixed phase plan for ABAC - 3 phases driving 5 signals (A shares a TrafficLight with A2).
    ///   P1 A+A2+B  40s  (opposing through movements plus the shared outer lane A2, on A's signal head)
    ///   P2 B+Bx    20s  (eastbound through plus the protected right turn into ABAC - the real bottleneck)
    ///   P3 CB      31s  (clears traffic out of ABAC)
    ///   inter-green 3 times x (3s yellow + 2s all-red) = 15s
    ///   total 40+20+31+15 = 106s
    ///
    ///   These values come from Kimmy timing the real signal on site (A=50 / B=90-95 / Bx=20 / CB=31/3/75
    ///   counted live, CB the most reliable because it has a countdown display), which Itim then checked
    ///   against Kimmy's video and re-derived as A=40s / B=65s / Bx=20-23s / CB=31s green, C red 73.5s
    ///   actuated)
    ///
    ///   Updated per SP1_HANDOFF_v2: Itim supplied the full stage table and specified Bx = 20s, not 21 or 23,
    ///   because a stopwatch gave exactly 20.0s while 23s came from a different method. The +/-3s spread
    ///   does not change the conclusion (Bx is oversaturated throughout: v/c 1.21 at 23s, 1.51 at 20s).
    ///     t=  0-40  A green + B green   |  t= 40-43 A yellow (B still green) |  t= 43-45 all-red
    ///     t= 45-65  Bx green + B green  |  t= 65-68 B+Bx yellow together     |  t= 68-70 all-red
    ///     t= 70-101 CB green            |  t=101-104 CB yellow               |  t=104-106 all-red
    ///
    ///   v/c from this set (handoff v2): Bx = 1.51 (over capacity; the queue grows 3.7 veh per cycle and
    ///   fills the 78 m of storage in 5.7 minutes, then blocks the next junction), CB = 1.04 (exactly at
    ///   capacity), A and B = 0.25 each (four times the green they need). The proposal to move 10s of green
    ///   from A to Bx (taking Bx to v/c 0.95) is a policy for the report, or for Adaptive to find on its own.
    ///   It is deliberately not applied here: Fixed has to reproduce the real problem, or the comparison fails.
    /// </summary>
    [ContextMenu("Load ABAC 4-Phase Preset (106s cycle)")]
    public void LoadABACPreset()
    {
        phases = new List<FixedPhase>
        {
            new FixedPhase { phaseName = "P1 MainThrough",  greenLanes = new[] { "A", "A2", "B" },  greenDuration = 40f },
            new FixedPhase { phaseName = "P2 EastRightTurn", greenLanes = new[] { "B", "Bx" }, greenDuration = 20f },
            new FixedPhase { phaseName = "P3 ABACExit",      greenLanes = new[] { "CB" },      greenDuration = 31f },
        };
        yellowDuration = 3f;
        allRedDuration = 2f;

        if (config == null) config = FindFirstObjectByType<IntersectionConfig>();
        if (config != null) { config.EnsureBuilt(); BuildPhaseCache(); }

        Debug.Log($"[FixedController] loaded the 3-phase / 4-signal preset - cycle = {GetCycleLength():F0}s");
    }

    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying) return;

        UITheme.Begin();

        const float w = 288f;
        float x = Screen.width - w - 12f;
        Rect card = new Rect(x, 12f, w, 62f);
        GUI.Box(card, GUIContent.none);

        GUI.Label(new Rect(x + 12f, card.y + 8f, w - 100f, 18f), "FIXED CONTROLLER", UITheme.Title);
        UITheme.Pill(new Rect(x + w - 86f, card.y + 9f, 74f, 17f),
                     $"{GetCycleLength():F0}s cycle", UITheme.AccentDark);
        UITheme.Rule(new Rect(x + 12f, card.y + 31f, w - 24f, 1f));
        GUI.Label(new Rect(x + 12f, card.y + 37f, w - 24f, 18f), statusText, UITheme.Body);

        UITheme.End();
    }
}
