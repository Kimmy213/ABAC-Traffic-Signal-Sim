using UnityEngine;
using System.Collections.Generic;

public class ROIZone : MonoBehaviour
{
    public void ClearAll()
    {
        carsInZone.Clear();
    }
    [Header("Lane Identity")]
    [Tooltip("Primary lane name for this ROI (used for display and referenced by IntersectionConfig)")]
    public string laneName = "A";

    [Tooltip("Other lanes sharing this same ROI - for when several movements occupy one physical lane.\n\n" +
             "Used on the outer eastbound lane: A2 (through) and Ax (turning into the campus) really do share it.\n" +
             "A real camera cannot tell which vehicle will go straight and which will turn, so it has to be one ROI.\n" +
             "GetVehicleCount() therefore returns the total in the outer lane = what a real detector sees.\n\n" +
             "Wait-time statistics still separate by movement, because LogVehicle uses car.assignedLane, not laneName")]
    public string[] alsoCountLanes = new string[0];

    /// <summary>Is this lane counted by this ROI?</summary>
    public bool Accepts(string lane)
    {
        if (lane == laneName) return true;
        if (alsoCountLanes == null) return false;
        foreach (string n in alsoCountLanes) if (n == lane) return true;
        return false;
    }

    [Header("Debug Display")]
    public bool showDebugGUI = true;
    [Tooltip("Display order on screen (0 = top). Keep these unique across lanes.")]
    public int guiSlot = 0;

    private Dictionary<CarAgent, float> carsInZone = new Dictionary<CarAgent, float>();

    void OnTriggerEnter(Collider other)
    {
        CarAgent car = other.GetComponent<CarAgent>();
        if (car == null) return;
        if (!Accepts(car.assignedLane)) return;   // not a lane this ROI covers

        if (!carsInZone.ContainsKey(car))
        {
            carsInZone.Add(car, Time.time);
            RoiHighlight h = car.GetComponent<RoiHighlight>();
            if (h != null) h.EnterZone();
        }
    }

    void OnTriggerExit(Collider other)
    {
        CarAgent car = other.GetComponent<CarAgent>();
        if (car == null) return;

        if (carsInZone.ContainsKey(car))
        {
            // ===== Report the wait before removing it from the dictionary =====
            float waitTime = Time.time - carsInZone[car];
            if (DataLogger.Instance != null)
            {
                // Use the car's own lane name, not the ROI's - one ROI can cover several movements (A2 + Ax).
                //    Using laneName would mix the Ax and A2 wait statistics beyond separating again,
                //    and the Ax wait is the headline measure of the whole project.
                DataLogger.Instance.LogVehicle(car.assignedLane, waitTime, car.driverType.ToString());
            }

            carsInZone.Remove(car);
            RoiHighlight h = car.GetComponent<RoiHighlight>();
            if (h != null) h.ExitZone();
        }
    }

    /// <summary>Display name - shows every lane if this ROI covers more than one, e.g. "Ax+A2"</summary>
    public string DisplayName
    {
        get
        {
            if (alsoCountLanes == null || alsoCountLanes.Length == 0) return laneName;
            return laneName + "+" + string.Join("+", alsoCountLanes);
        }
    }

    /// <summary>Total vehicles in the ROI = what a real detector sees (it cannot separate movements)</summary>
    public int GetVehicleCount()
    {
        return carsInZone.Count;
    }

    // ===================================================================================
    //  APIs the controller may use vs. APIs that are sim-only ground truth
    // ===================================================================================
    //  GetSampledCount() - the only number a YOLO camera with no tracker can actually produce:
    //     "count the vehicles in frame this second", with no identity and no frame-to-frame matching.
    //     AdaptiveController must call only this one.
    //
    //  🛑 GetOldestArrivalTime / GetLongestWaitTime / GetCarsInZone
    //     Those need ByteTrack/DeepSORT behind YOLO, which is hardware we cannot afford.
    //     They stay because DataLogger uses them to measure results, which are outputs, not controller inputs.
    //     A test (NoTrackerTest) scans that AdaptiveController never touches those three.
    // ===================================================================================

    /// <summary>
    /// Snapshot count - vehicles visible this second, no IDs and no history.
    /// This is everything the controller can see once the tracker is removed.
    /// </summary>
    public int GetSampledCount()
    {
        if (simulatedCountOverride >= 0) return simulatedCountOverride;
        return carsInZone.Count;
    }

    /// <summary>
    /// -1 = off (use the real vehicle count) - >= 0 = force the snapshot to return this value.
    /// For automated tests and manual scenario walkthroughs in the Editor only.
    /// Must always be -1 during a batch; a batch never touches this field.
    /// </summary>
    [Tooltip("Tests only: -1 = off, >= 0 = force the value the snapshot sees")]
    public int simulatedCountOverride = -1;

    /// <summary>Count for one movement only - for inspection and debugging; a real detector cannot do this.</summary>
    public int GetVehicleCount(string lane)
    {
        int n = 0;
        foreach (var e in carsInZone) if (e.Key != null && e.Key.assignedLane == lane) n++;
        return n;
    }

    /// <summary>Requires a tracker - sim-only ground truth, never for the controller.</summary>
    public float GetOldestArrivalTime()
    {
        if (carsInZone.Count == 0) return Mathf.Infinity;

        float oldest = Mathf.Infinity;
        foreach (var entry in carsInZone)
        {
            if (entry.Value < oldest) oldest = entry.Value;
        }
        return oldest;
    }

    /// <summary>Requires a tracker - sim-only ground truth, never for the controller.</summary>
    public float GetLongestWaitTime()
    {
        float oldest = GetOldestArrivalTime();
        if (oldest == Mathf.Infinity) return 0f;
        return Time.time - oldest;
    }

    /// <summary>Requires a tracker - sim-only ground truth, never for the controller.</summary>
    public List<CarAgent> GetCarsInZone()
    {
        return new List<CarAgent>(carsInZone.Keys);
    }

    void OnDrawGizmos()
    {
        BoxCollider box = GetComponent<BoxCollider>();
        if (box == null) return;

        Gizmos.color = new Color(1f, 1f, 0f, 0.25f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(box.center, box.size);

        Gizmos.color = Color.yellow;
        Gizmos.DrawWireCube(box.center, box.size);

#if UNITY_EDITOR
        Vector3 labelPos = transform.position + Vector3.up * 4f;
        string label = $"Lane {DisplayName}: {GetVehicleCount()} cars";
        UnityEditor.Handles.Label(labelPos, label);
#endif
    }

    // Vehicle count treated as "lane full" - only used to scale the meter bar
    const int BarFullCars = 15;
    // Wait treated as "longer than acceptable" - above this the bar turns red (from Adaptive maxWaitTime = 45 s)
    const float WaitCritical = 45f;

    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying) return;

        UITheme.Begin();

        const float W = 246f, H = 52f;
        Rect card = new Rect(12f, 12f + guiSlot * (H + 6f), W, H);
        GUI.Box(card, GUIContent.none);

        int cars = GetVehicleCount();
        float wait = GetLongestWaitTime();
        Color laneCol = UITheme.LaneColor(laneName);

        // Lane-group colour dot and lane name
        UITheme.Dot(new Rect(card.x + 10f, card.y + 11f, 9f, 9f), laneCol);
        GUI.Label(new Rect(card.x + 25f, card.y + 6f, 120f, 18f), DisplayName, UITheme.Title);

        // Vehicle count right-aligned, easiest to read in a single glance
        GUI.Label(new Rect(card.x + W - 96f, card.y + 6f, 88f, 18f), cars.ToString(), UITheme.Value);

        // Density meter bar and wait time
        UITheme.Bar(new Rect(card.x + 10f, card.y + 30f, W - 96f, 6f),
                    cars / (float)BarFullCars, laneCol);

        Color wc = wait >= WaitCritical ? UITheme.SigRed
                 : wait >= WaitCritical * 0.5f ? UITheme.SigYellow : UITheme.TextDim;
        GUIStyle ws = new GUIStyle(UITheme.Dim); ws.normal.textColor = wc;
        ws.alignment = TextAnchor.MiddleRight;
        GUI.Label(new Rect(card.x + W - 96f, card.y + 27f, 88f, 16f), $"wait {wait:F1}s", ws);

        UITheme.End();
    }
}