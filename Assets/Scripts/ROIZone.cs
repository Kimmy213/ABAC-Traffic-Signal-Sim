using UnityEngine;
using System.Collections.Generic;

public class ROIZone : MonoBehaviour
{
    public void ClearAll()
    {
        carsInZone.Clear();
    }
    [Header("Lane Identity")]
    public string laneName = "A";

    [Header("Debug Display")]
    public bool showDebugGUI = true;
    [Tooltip("ลำดับช่องที่จะแสดงบนจอ (0 = บนสุด) ตั้งไม่ให้ซ้ำกันระหว่างเลน")]
    public int guiSlot = 0;

    private Dictionary<CarAgent, float> carsInZone = new Dictionary<CarAgent, float>();

    void OnTriggerEnter(Collider other)
    {
        CarAgent car = other.GetComponent<CarAgent>();
        if (car == null) return;
        if (car.assignedLane != laneName) return;   // ← เพิ่มบรรทัดนี้ ไม่ใช่เลนตัวเอง ไม่นับ

        if (!carsInZone.ContainsKey(car))
        {
            carsInZone.Add(car, Time.time);
        }
    }

    void OnTriggerExit(Collider other)
    {
        CarAgent car = other.GetComponent<CarAgent>();
        if (car == null) return;

        if (carsInZone.ContainsKey(car))
        {
            // ===== รายงานเวลาที่รอ ก่อนจะลบออกจาก dictionary =====
            float waitTime = Time.time - carsInZone[car];
            if (DataLogger.Instance != null)
            {
                DataLogger.Instance.LogVehicle(laneName, waitTime, car.driverType.ToString());
            }

            carsInZone.Remove(car);
        }
    }

    public int GetVehicleCount()
    {
        return carsInZone.Count;
    }

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

    public float GetLongestWaitTime()
    {
        float oldest = GetOldestArrivalTime();
        if (oldest == Mathf.Infinity) return 0f;
        return Time.time - oldest;
    }

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
        string label = $"Lane {laneName}: {GetVehicleCount()} cars";
        UnityEditor.Handles.Label(labelPos, label);
#endif
    }

    void OnGUI()
    {
        if (!showDebugGUI || !Application.isPlaying) return;

        float yPos = 10 + guiSlot * 55;

        GUI.Box(new Rect(10, yPos, 230, 48), "");
        GUI.Label(new Rect(18, yPos + 4, 214, 20), $"Lane {laneName} — ROI Count");
        GUI.Label(new Rect(18, yPos + 24, 214, 20), $"Cars: {GetVehicleCount()}   Wait: {GetLongestWaitTime():F1}s");
    }
}