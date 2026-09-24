using UnityEngine;
using System.Collections.Generic;

public class TrafficLight : MonoBehaviour
{
    public enum LightState { Red, Yellow, Green }

    [Header("Identity (match IntersectionConfig so logs are easy to read)")]
    public string laneName = "A";

    [Header("Current State")]
    public LightState currentState = LightState.Red;

    [Header("Materials (assign in the Inspector)")]
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
        // Iterate backwards so destroyed cars can be removed from the list safely.
        for (int i = assignedCars.Count - 1; i >= 0; i--)
        {
            if (assignedCars[i] == null) { assignedCars.RemoveAt(i); continue; }
            assignedCars[i].OnLightStateChanged(currentState);
        }
    }

    /// <summary>Clears every registered car - called at the start of each run in a batch.</summary>
    public void ClearRegistrations()
    {
        assignedCars.Clear();
    }

    public void RegisterCar(CarAgent car)
    {
        if (!assignedCars.Contains(car)) assignedCars.Add(car);
        // Push the current state to a car as soon as it registers, rather than making it wait for the next change.
        car.OnLightStateChanged(currentState);
    }

    public void UnregisterCar(CarAgent car)
    {
        assignedCars.Remove(car);
    }

    // ===== Manual testing (right-click the component while in Play mode) =====
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