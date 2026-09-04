using UnityEngine;
using System.Collections.Generic;

public class ROIZone : MonoBehaviour
{
    public void ClearAll()
    {
        carsInZone.Clear();
    }
    [Header("Lane Identity")]
    [Tooltip("ชื่อเลนหลักของ ROI นี้ (ใช้แสดงผลและเป็นชื่อที่ IntersectionConfig อ้างถึง)")]
    public string laneName = "A";

    [Tooltip("เลนอื่นที่ใช้ ROI ตัวเดียวกันนี้ — สำหรับกรณีที่หลาย movement อยู่ใน 'เลนกายภาพเดียวกัน'\n\n" +
             "ใช้กับเลนนอกฝั่งขาตะวันออก: A2 (วิ่งตรง) กับ Ax (เลี้ยวเข้ามอ) อยู่เลนเดียวกันจริง ๆ\n" +
             "กล้อง/เครื่องตรวจจับตัวจริงแยกไม่ออกว่าคันไหนจะตรงคันไหนจะเลี้ยว จึงต้องเป็น ROI เดียว\n" +
             "GetVehicleCount() จึงคืน 'จำนวนรถในเลนนอกทั้งหมด' = สิ่งที่ detector จริงเห็น\n\n" +
             "⚠️ สถิติเวลารอยังแยกราย movement ได้ปกติ เพราะ LogVehicle ใช้ car.assignedLane ไม่ใช่ laneName")]
    public string[] alsoCountLanes = new string[0];

    /// <summary>เลนนี้ถูกนับใน ROI ตัวนี้ไหม</summary>
    public bool Accepts(string lane)
    {
        if (lane == laneName) return true;
        if (alsoCountLanes == null) return false;
        foreach (string n in alsoCountLanes) if (n == lane) return true;
        return false;
    }

    [Header("Debug Display")]
    public bool showDebugGUI = true;
    [Tooltip("ลำดับช่องที่จะแสดงบนจอ (0 = บนสุด) ตั้งไม่ให้ซ้ำกันระหว่างเลน")]
    public int guiSlot = 0;

    private Dictionary<CarAgent, float> carsInZone = new Dictionary<CarAgent, float>();

    void OnTriggerEnter(Collider other)
    {
        CarAgent car = other.GetComponent<CarAgent>();
        if (car == null) return;
        if (!Accepts(car.assignedLane)) return;   // ไม่ใช่เลนที่ ROI นี้รับผิดชอบ ไม่นับ

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
            // ===== รายงานเวลาที่รอ ก่อนจะลบออกจาก dictionary =====
            float waitTime = Time.time - carsInZone[car];
            if (DataLogger.Instance != null)
            {
                // ⚠️ ใช้ชื่อเลนของ "ตัวรถ" ไม่ใช่ของ ROI — ROI เดียวอาจครอบหลาย movement (A2 + Ax)
                //    ถ้าใช้ laneName สถิติเวลารอของ Ax กับ A2 จะปนกันแล้วแยกไม่ออกอีกเลย
                //    ซึ่งเวลารอของ Ax คือตัวชี้วัดหลักของงานทั้งหมด
                DataLogger.Instance.LogVehicle(car.assignedLane, waitTime, car.driverType.ToString());
            }

            carsInZone.Remove(car);
            RoiHighlight h = car.GetComponent<RoiHighlight>();
            if (h != null) h.ExitZone();
        }
    }

    /// <summary>ชื่อที่แสดงบนจอ — ถ้า ROI นี้ครอบหลายเลนก็โชว์ให้ครบ เช่น "Ax+A2"</summary>
    public string DisplayName
    {
        get
        {
            if (alsoCountLanes == null || alsoCountLanes.Length == 0) return laneName;
            return laneName + "+" + string.Join("+", alsoCountLanes);
        }
    }

    /// <summary>จำนวนรถทั้งหมดใน ROI = สิ่งที่ detector จริงเห็น (แยก movement ไม่ออก)</summary>
    public int GetVehicleCount()
    {
        return carsInZone.Count;
    }

    /// <summary>จำนวนรถเฉพาะ movement เดียว — ใช้ตรวจสอบ/ดีบักเท่านั้น detector จริงทำแบบนี้ไม่ได้</summary>
    public int GetVehicleCount(string lane)
    {
        int n = 0;
        foreach (var e in carsInZone) if (e.Key != null && e.Key.assignedLane == lane) n++;
        return n;
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
        string label = $"Lane {DisplayName}: {GetVehicleCount()} cars";
        UnityEditor.Handles.Label(labelPos, label);
#endif
    }

    // จำนวนรถที่ถือว่า "เต็มเลน" ใช้เทียบเป็นสัดส่วนของแถบมิเตอร์เท่านั้น
    const int BarFullCars = 15;
    // เวลารอที่ถือว่า "นานเกินรับได้" — เกินนี้แถบเป็นสีแดง (อ้างจาก maxWaitTime ของ Adaptive = 45s)
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

        // จุดสีประจำกลุ่มเลน + ชื่อเลน
        UITheme.Dot(new Rect(card.x + 10f, card.y + 11f, 9f, 9f), laneCol);
        GUI.Label(new Rect(card.x + 25f, card.y + 6f, 120f, 18f), DisplayName, UITheme.Title);

        // ตัวเลขคันรถชิดขวา อ่านง่ายสุดในจอเดียว
        GUI.Label(new Rect(card.x + W - 96f, card.y + 6f, 88f, 18f), cars.ToString(), UITheme.Value);

        // แถบมิเตอร์ความหนาแน่น + เวลารอ
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