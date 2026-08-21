using UnityEngine;
using System.Collections.Generic;

public class TrafficLight : MonoBehaviour
{
    public enum LightState { Red, Yellow, Green }

    [Header("Identity (ใส่ให้ตรงกับ IntersectionConfig เพื่ออ่าน log ง่าย)")]
    public string laneName = "A";

    [Header("Current State")]
    public LightState currentState = LightState.Red;

    [Header("Materials (ลากใส่ใน Inspector)")]
    public Material redMat;
    public Material yellowMat;
    public Material greenMat;

    private Renderer rend;

    public List<CarAgent> assignedCars = new List<CarAgent>();

    void Start()
    {
        rend = GetComponent<Renderer>();
        UpdateVisual();
    }

    public void SetState(LightState newState)
    {
        currentState = newState;
        UpdateVisual();
        NotifyCars();
    }

    void UpdateVisual()
    {
        if (rend == null) return;

        switch (currentState)
        {
            case LightState.Red: rend.material = redMat; break;
            case LightState.Yellow: rend.material = yellowMat; break;
            case LightState.Green: rend.material = greenMat; break;
        }
    }

    void NotifyCars()
    {
        // วน backward เพื่อลบรถที่ถูก Destroy ไปแล้วออกจาก list ได้ปลอดภัย
        for (int i = assignedCars.Count - 1; i >= 0; i--)
        {
            if (assignedCars[i] == null) { assignedCars.RemoveAt(i); continue; }
            assignedCars[i].OnLightStateChanged(currentState);
        }
    }

    /// <summary>ล้างทะเบียนรถทั้งหมด — เรียกตอนเริ่ม run ใหม่ในแต่ละ batch</summary>
    public void ClearRegistrations()
    {
        assignedCars.Clear();
    }

    public void RegisterCar(CarAgent car)
    {
        if (!assignedCars.Contains(car)) assignedCars.Add(car);
        // ส่งสถานะไฟปัจจุบันให้รถที่เพิ่งมาลงทะเบียนทันที ไม่ต้องรอไฟเปลี่ยนรอบหน้า
        car.OnLightStateChanged(currentState);
    }

    public void UnregisterCar(CarAgent car)
    {
        assignedCars.Remove(car);
    }

    // ===== สำหรับทดสอบด้วยมือ (คลิกขวาที่ component ตอน Play) =====
    [ContextMenu("TEST: Set Green")]
    void TestSetGreen()
    {
        SetState(LightState.Green);
    }

    [ContextMenu("TEST: Set Red")]
    void TestSetRed()
    {
        SetState(LightState.Red);
    }

    [ContextMenu("TEST: Set Yellow")]
    void TestSetYellow()
    {
        SetState(LightState.Yellow);
    }
}