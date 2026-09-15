using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// One approach movement into the junction,
/// e.g. A = westbound through, Bx = right turn into ABAC, CA = free left turn out of ABAC.
/// </summary>
[System.Serializable]
public class ApproachLane
{
    [Tooltip("Lane name - must match laneName in CarSpawner and ROIZone exactly")]
    public string laneName = "A";

    [Tooltip("true = controlled by a signal (A, B, Bx, CB) / false = free-flowing (Ax, CA)")]
    public bool isSignalized = true;

    [Tooltip("May be left empty when isSignalized = false")]
    public TrafficLight trafficLight;

    [Tooltip("ROI zone for this lane (used both to count demand and to measure waiting time)")]
    public ROIZone roi;

    [Tooltip("Spawner for this lane")]
    public CarSpawner spawner;

    [Tooltip("Description for the report (no effect on logic)")]
    public string movementDescription = "";
}

/// <summary>
/// A pair of lanes that must never be green together, because their paths cross
/// </summary>
[System.Serializable]
public class ConflictPair
{
    public string laneA;
    public string laneB;
    [Tooltip("Reason for the conflict - cited in the report")]
    public string reason = "";
}

/// <summary>
/// Central data for the junction: every lane plus the conflict matrix.
/// FixedController, AdaptiveController and ScenarioManager all read from here and nowhere else,
/// so lanes can be added or removed without touching any controller code.
/// </summary>
public class IntersectionConfig : MonoBehaviour
{
    [Header("Every lane in the junction (signalised + free-flow)")]
    public List<ApproachLane> lanes = new List<ApproachLane>();

    [Header("Lane pairs that must not be green together")]
    public List<ConflictPair> conflicts = new List<ConflictPair>();

    // ===== Data built at runtime =====
    private bool[,] conflictMatrix;     // indexed in signal-index space (signalised lanes only)
    private List<int> signalToLane = new List<int>();   // signalIndex -> index into lanes
    private bool isBuilt = false;

    void Awake()
    {
        Build();
    }

    /// <summary>
    /// Safe to call repeatedly - a controller should call this from its own Awake(),
    /// because Awake() ordering between MonoBehaviours is not guaranteed.
    /// </summary>
    public void EnsureBuilt()
    {
        if (!isBuilt) Build();
    }

    public void Build()
    {
        signalToLane.Clear();

        for (int i = 0; i < lanes.Count; i++)
        {
            if (lanes[i] == null) continue;
            if (lanes[i].isSignalized) signalToLane.Add(i);
        }

        int n = signalToLane.Count;
        conflictMatrix = new bool[n, n];

        // isBuilt must be set here, before the conflict loop, because the code below calls
        // SignalIndexOf(), which calls EnsureBuilt() -> Build() and would never terminate.
        isBuilt = true;

        // A lane never conflicts with itself; everything else defaults to no conflict
        foreach (ConflictPair pair in conflicts)
        {
            if (pair == null) continue;
            int a = SignalIndexOf(pair.laneA);
            int b = SignalIndexOf(pair.laneB);

            if (a == -1 || b == -1)
            {
                Debug.LogWarning($"[IntersectionConfig] Conflict pair ({pair.laneA}, {pair.laneB}) " +
                                 $"refers to a lane that does not exist or is not signalised - skipping");
                continue;
            }
            if (a == b) continue;

            conflictMatrix[a, b] = true;
            conflictMatrix[b, a] = true;
        }
    }

    // ===================== Accessors =====================

    /// <summary>Number of signal-controlled lanes (ABAC = 4: A, B, Bx, CB)</summary>
    public int SignalCount
    {
        get { EnsureBuilt(); return signalToLane.Count; }
    }

    public ApproachLane GetSignalLane(int signalIndex)
    {
        EnsureBuilt();
        return lanes[signalToLane[signalIndex]];
    }

    public string SignalName(int signalIndex) => GetSignalLane(signalIndex).laneName;
    public TrafficLight SignalLight(int signalIndex) => GetSignalLane(signalIndex).trafficLight;
    public ROIZone SignalRoi(int signalIndex) => GetSignalLane(signalIndex).roi;

    /// <summary>Lane name -> signal index (-1 if not found, or the lane has no signal)</summary>
    public int SignalIndexOf(string laneName)
    {
        EnsureBuilt();
        if (string.IsNullOrEmpty(laneName)) return -1;

        for (int s = 0; s < signalToLane.Count; s++)
        {
            if (lanes[signalToLane[s]].laneName == laneName) return s;
        }
        return -1;
    }

    /// <summary>Finds a lane by name, including free-flow lanes</summary>
    public ApproachLane FindLane(string laneName)
    {
        foreach (ApproachLane lane in lanes)
        {
            if (lane != null && lane.laneName == laneName) return lane;
        }
        return null;
    }

    /// <summary>true = these two lanes cannot be green at the same time</summary>
    public bool AreConflicting(int signalA, int signalB)
    {
        EnsureBuilt();
        if (signalA == signalB) return false;
        if (signalA < 0 || signalB < 0) return false;
        if (signalA >= conflictMatrix.GetLength(0) || signalB >= conflictMatrix.GetLength(0)) return false;
        return conflictMatrix[signalA, signalB];
    }

    /// <summary>true = the candidate lane can be green alongside every lane in the group</summary>
    public bool IsCompatibleWithGroup(int candidate, List<int> group)
    {
        foreach (int member in group)
        {
            if (AreConflicting(candidate, member)) return false;
        }
        return true;
    }

    public string DescribeGroup(List<int> group)
    {
        if (group == null || group.Count == 0) return "(none)";
        string[] names = new string[group.Count];
        for (int i = 0; i < group.Count; i++) names[i] = SignalName(group[i]);
        return string.Join("+", names);
    }

    // ===================== Lane control (shared by ScenarioManager and the Control Panel) =====================

    /// <summary>
    /// Clears every lane, sets its per-lane seed and restarts spawning.
    /// Each lane seed comes from runSeed plus the lane name, so Fixed and Adaptive on the same
    /// seed see exactly the same vehicle sequence - essential for the paired t-test.
    /// </summary>
    public void ResetAllLanes(int runSeed)
    {
        // These must be two separate passes and must not be merged into one loop.
        //
        // Reason: RestartSpawning() calls StartCoroutine, which runs the body immediately up to
        // the first yield. In Scripted mode a burst at atTime = 0 spawns its first car right there,
        // and that car registers itself with the signal.
        // Calling ClearRegistrations() later in the same loop would wipe that registration, so the
        // first car in every lane would never hear that the light went green and would sit forever,
        // with the whole queue behind it stuck too.

        // ---- pass 1: clear everything first ----
        foreach (ApproachLane lane in lanes)
        {
            if (lane == null) continue;
            if (lane.spawner != null) lane.spawner.ClearAllCars();
            if (lane.roi != null) lane.roi.ClearAll();
            if (lane.trafficLight != null) lane.trafficLight.ClearRegistrations();
        }

        // ---- pass 2: set the seed, then start releasing cars ----
        foreach (ApproachLane lane in lanes)
        {
            if (lane == null || lane.spawner == null) continue;
            lane.spawner.SetSeed(runSeed + StableHash(lane.laneName));
            lane.spawner.ResetSpawnCount();
            lane.spawner.RestartSpawning();
        }
    }

    /// <summary>Clears every vehicle from the road without restarting spawning</summary>
    public void ClearAllCars()
    {
        foreach (ApproachLane lane in lanes)
        {
            if (lane == null) continue;
            if (lane.spawner != null) lane.spawner.ClearAllCars();
            if (lane.roi != null) lane.roi.ClearAll();
            if (lane.trafficLight != null) lane.trafficLight.ClearRegistrations();
        }
    }

    public int GetTotalSpawned()
    {
        int total = 0;
        foreach (ApproachLane lane in lanes)
            if (lane != null && lane.spawner != null) total += lane.spawner.totalSpawned;
        return total;
    }

    /// <summary>Vehicles still on the road across the whole junction</summary>
    public int GetActiveCarCount()
    {
        int total = 0;
        foreach (ApproachLane lane in lanes)
            if (lane != null && lane.spawner != null) total += lane.spawner.ActiveCarCount;
        return total;
    }

    /// <summary>true = every lane is Scripted and has released its whole schedule</summary>
    public bool AllScheduledSpawned()
    {
        bool anyScripted = false;
        foreach (ApproachLane lane in lanes)
        {
            if (lane == null || lane.spawner == null) continue;
            if (lane.spawner.arrivalMode != ArrivalMode.Scripted) return false;
            anyScripted = true;
            if (!lane.spawner.AllScheduledSpawned) return false;
        }
        return anyScripted;
    }

    public void ResetSpawnCounts()
    {
        foreach (ApproachLane lane in lanes)
            if (lane != null && lane.spawner != null) lane.spawner.ResetSpawnCount();
    }

    /// <summary>Applies the selected TestCase to every lane (both Poisson and Scripted)</summary>
    public void ApplyTestCase(TestCase testCase, float poissonFallback = 7f)
    {
        if (testCase == null) return;

        foreach (ApproachLane lane in lanes)
        {
            if (lane == null || lane.spawner == null) continue;

            if (testCase.mode == ArrivalMode.Poisson)
            {
                lane.spawner.UsePoisson(testCase.GetInterval(lane.laneName, poissonFallback));
            }
            else
            {
                lane.spawner.UseSchedule(testCase.GetSchedule(lane.laneName));
                lane.spawner.SetStandingQueue(testCase.GetInitialQueue(lane.laneName));
            }
        }
    }

    /// <summary>Shows or hides the ROI debug GUI on every lane at once</summary>
    public void SetRoiDebugGUI(bool on)
    {
        foreach (ApproachLane lane in lanes)
            if (lane != null && lane.roi != null) lane.roi.showDebugGUI = on;
    }

    /// <summary>
    /// FNV-1a - needed instead of string.GetHashCode(), which .NET does not guarantee to be
    /// stable across sessions. An unstable hash makes seeds irreproducible and breaks the pairing.
    /// </summary>
    public static int StableHash(string text)
    {
        unchecked
        {
            uint hash = 2166136261u;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 16777619u;
            }
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    // ===================== Validation =====================

    [ContextMenu("Validate Configuration")]
    public void ValidateConfiguration()
    {
        Build();

        int problems = 0;

        foreach (ApproachLane lane in lanes)
        {
            if (lane == null) { Debug.LogError("[IntersectionConfig] there is an empty (null) lane slot in the list"); problems++; continue; }

            if (string.IsNullOrEmpty(lane.laneName))
            {
                Debug.LogError("[IntersectionConfig] a lane has no name"); problems++;
            }
            if (lane.isSignalized && lane.trafficLight == null)
            {
                Debug.LogError($"[IntersectionConfig] lane {lane.laneName} is signalised but has no TrafficLight assigned"); problems++;
            }
            if (lane.roi == null)
            {
                Debug.LogError($"[IntersectionConfig] lane {lane.laneName} has no ROIZone assigned"); problems++;
            }
            else if (!lane.roi.Accepts(lane.laneName))
            {
                // Uses Accepts() rather than comparing laneName directly, because one ROI can cover
                //    several lanes (A2 and Ax share a physical lane, so they share ROI_Ax - see ROIZone.alsoCountLanes)
                Debug.LogError($"[IntersectionConfig] ROIZone '{lane.roi.name}' on lane {lane.laneName} does not count this lane " +
                               $"(laneName='{lane.roi.laneName}', alsoCountLanes has no '{lane.laneName}') - demand will always read 0"); problems++;
            }
            if (lane.spawner == null)
            {
                Debug.LogWarning($"[IntersectionConfig] lane {lane.laneName} has no CarSpawner assigned (no vehicles will appear in it)");
            }
            else if (lane.spawner.laneName != lane.laneName)
            {
                Debug.LogError($"[IntersectionConfig] the CarSpawner on lane {lane.laneName} has laneName '{lane.spawner.laneName}', which does not match"); problems++;
            }
        }

        // Check for duplicate names
        HashSet<string> seen = new HashSet<string>();
        foreach (ApproachLane lane in lanes)
        {
            if (lane == null) continue;
            if (!seen.Add(lane.laneName))
            {
                Debug.LogError($"[IntersectionConfig] duplicate lane name '{lane.laneName}'"); problems++;
            }
        }

        if (problems == 0)
            Debug.Log($"[IntersectionConfig] all checks passed - {lanes.Count} lanes ({SignalCount} signalised), {conflicts.Count} conflict pairs");
        else
            Debug.LogError($"[IntersectionConfig] found {problems} problem(s) - fix them before pressing Play");
    }

    /// <summary>
    /// Fills in the ABAC lane structure and conflict pairs automatically.
    /// (TrafficLight / ROIZone / CarSpawner still have to be assigned in the Inspector.)
    /// </summary>
    [ContextMenu("Load ABAC T-Junction Preset (empty structure)")]
    public void LoadABACPreset()
    {
        lanes = new List<ApproachLane>
        {
            new ApproachLane { laneName = "A",  isSignalized = true,  movementDescription = "Bangna-Trad Road eastbound (+X), straight through the junction - inner lane" },
            new ApproachLane { laneName = "A2", isSignalized = true,  movementDescription = "Through traffic using the outer lane shared with Ax. Uses exactly the same signal head as A and exactly the same ROI as Ax - the same components, not merely identical settings - so it is always green and red with A, and is counted together with Ax just as a real detector would. 5.4% of through traffic uses the outer lane, from a direct count (SP1_HANDOFF_v2 section 5, replacing the earlier back-calculated 18%)." },
            new ApproachLane { laneName = "Ax", isSignalized = false, movementDescription = "Splits from A and turns left into ABAC (ABAC is on A's left). It has no phase or green time of its own, but it is not fully free either: (1) it shares a lane with A2 and is blocked by A2 waiting at the red, (2) it cannot turn in while Bx is green, because Bx turns into the campus as well. Handled through CarSpawner.lightIsInverted tied to the Bx signal, not through the conflict matrix, since it is not a signalised lane." },
            new ApproachLane { laneName = "B",  isSignalized = true,  movementDescription = "Bangna-Trad Road westbound (-X), straight through the junction" },
            new ApproachLane { laneName = "Bx", isSignalized = true,  movementDescription = "Splits from B and turns right into ABAC - crosses the A stream (ABAC is on B's right)" },
            new ApproachLane { laneName = "CB", isSignalized = true,  movementDescription = "Leaves ABAC turning right to the west, merging into lane B - must cross lane A first" },
            new ApproachLane { laneName = "CA", isSignalized = false, movementDescription = "Leaves ABAC turning left to the east, merging into lane A - free, crosses no one" },
        };

        conflicts = new List<ConflictPair>
        {
            new ConflictPair { laneA = "A",  laneB = "Bx", reason = "Bx turns right into ABAC across the A through movement" },
            new ConflictPair { laneA = "A",  laneB = "CB", reason = "CB has to cross lane A before merging into B" },
            new ConflictPair { laneA = "A2", laneB = "Bx", reason = "A2 shares A's signal - the outer lane is cut by Bx just as the inner one is" },
            new ConflictPair { laneA = "A2", laneB = "CB", reason = "A2 shares A's signal - CB crosses the outer lane as well as the inner one" },
            new ConflictPair { laneA = "B",  laneB = "CB", reason = "CB merges into the very lane B is running through" },
            new ConflictPair { laneA = "Bx", laneB = "CB", reason = "their paths cross in the middle of the junction (as stated in the scope)" },
        };

        // Note: A and B do not conflict (opposing through movements) -> they can be green together.
        //       B and Bx do not conflict (same approach)            -> they can be green together.
        //       A and A2 deliberately have no ConflictPair          -> they must always be green together,
        //       because they are physically the same signal head. Anything conflicting with A must conflict with A2.

        Build();
        Debug.Log("[IntersectionConfig] loaded the ABAC preset - now assign TrafficLight / ROIZone / CarSpawner on each lane");
    }
}
