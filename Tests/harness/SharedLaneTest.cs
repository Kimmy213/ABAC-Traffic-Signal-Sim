using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// <summary>
/// Focused check on the shared lane (A2), wired exactly as ABACSceneBuilder wires it
/// (A2.trafficLight == A.trafficLight is the same instance, not merely an equal value),
/// then calling the real AdaptiveController.BuildCompatibleGroup() through reflection
/// to prove the "physically-linked signal" closure behaves correctly.
/// </summary>
public class SharedLaneTest
{
    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    static FieldInfo PrivateField(Type t, string name) =>
        t.GetField(name, BindingFlags.NonPublic | BindingFlags.Instance);

    static MethodInfo PrivateMethod(Type t, string name) =>
        t.GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance);

    /// <summary>Injects fake vehicles straight into an ROI by reflection (OnTriggerEnter needs real physics, unavailable in the stub)</summary>
    static void SeedDemand(ROIZone roi, int count, float arrivalTime)
    {
        var dict = (Dictionary<CarAgent, float>)PrivateField(typeof(ROIZone), "carsInZone").GetValue(roi);
        dict.Clear();
        for (int i = 0; i < count; i++)
        {
            var car = new CarAgent();
            dict[car] = arrivalTime;
        }
    }

    public static int Main()
    {
        Time.time = 100f; // the simulation "now", used to compute wait times

        var cfg = new IntersectionConfig();
        cfg.LoadABACPreset();

        // ===== Wired exactly as ABACSceneBuilder does: A and A2 use the same TrafficLight instance =====
        var lightA  = new TrafficLight { laneName = "A" };
        var lightB  = new TrafficLight { laneName = "B" };
        var lightBx = new TrafficLight { laneName = "Bx" };
        var lightCB = new TrafficLight { laneName = "CB" };

        var roiA  = new ROIZone { laneName = "A" };
        var roiA2 = new ROIZone { laneName = "A2" };
        var roiB  = new ROIZone { laneName = "B" };
        var roiBx = new ROIZone { laneName = "Bx" };
        var roiCB = new ROIZone { laneName = "CB" };

        cfg.FindLane("A").trafficLight  = lightA;   cfg.FindLane("A").roi  = roiA;
        cfg.FindLane("A2").trafficLight = lightA;   cfg.FindLane("A2").roi = roiA2;  // deliberately the same instance as A
        cfg.FindLane("B").trafficLight  = lightB;   cfg.FindLane("B").roi  = roiB;
        cfg.FindLane("Bx").trafficLight = lightBx;  cfg.FindLane("Bx").roi = roiBx;
        cfg.FindLane("CB").trafficLight = lightCB;  cfg.FindLane("CB").roi = roiCB;
        cfg.Build();

        Check(cfg.SignalLight(cfg.SignalIndexOf("A")) == cfg.SignalLight(cfg.SignalIndexOf("A2")),
              "A and A2 really do reference the same TrafficLight component (reference equality, not just value)");

        var adaptive = new AdaptiveController { config = cfg };
        // Call Awake() directly through reflection (normally the Unity engine does this)
        PrivateMethod(typeof(AdaptiveController), "Awake").Invoke(adaptive, null);

        var buildGroup = PrivateMethod(typeof(AdaptiveController), "BuildCompatibleGroup");
        // SODA reads counts from a snapshot rather than the live ROI, so a snapshot has to be taken first,
        // exactly as ServeOnePhase does when it decides.
        Func<int, List<int>> Group = leader =>
        {
            adaptive.Snapshot();
            return (List<int>)buildGroup.Invoke(adaptive, new object[] { leader });
        };

        int iA  = cfg.SignalIndexOf("A");
        int iA2 = cfg.SignalIndexOf("A2");
        int iB  = cfg.SignalIndexOf("B");
        int iBx = cfg.SignalIndexOf("Bx");
        int iCB = cfg.SignalIndexOf("CB");

        Console.WriteLine("\n--- Case: A2 demand = 0 (no A2 vehicles right now) but leader = A ---");
        SeedDemand(roiA2, 0, 0f);
        SeedDemand(roiB, 0, 0f);
        var g1 = Group(iA);
        Check(g1.Contains(iA) && g1.Contains(iA2) && g1.Count == 2,
              $"leader=A, A2 demand=0 -> the group must still include A2 (same lamp) = {{{string.Join(",", g1)}}}");

        Console.WriteLine("\n--- Same case with leader = A2 instead ---");
        var g2 = Group(iA2);
        Check(g2.Contains(iA) && g2.Contains(iA2) && g2.Count == 2,
              $"leader=A2 -> the closure must pull A in as well (symmetric) = {{{string.Join(",", g2)}}}");

        Console.WriteLine("\n--- B has real demand and must join (it does not conflict with A/A2) ---");
        SeedDemand(roiB, 3, 90f); // arrived at t=90, so wait = 100-90 = 10 s
        var g3 = Group(iA);
        Check(g3.Contains(iA) && g3.Contains(iA2) && g3.Contains(iB) && g3.Count == 3,
              $"leader=A with B waiting -> group = {{{string.Join(",", g3)}}} must contain A, A2 and B");

        Console.WriteLine("\n--- Bx and CB conflict with A2 (mirroring A) and must never join, however heavy their demand ---");
        SeedDemand(roiBx, 10, 50f); // a very long wait and very heavy demand
        var g4 = Group(iA);
        Check(!g4.Contains(iBx), $"leader=A with heavy Bx demand -> Bx still must not join (conflict) = {{{string.Join(",", g4)}}}");

        // ============ One ROI covering several movements ============
        Console.WriteLine("\n--- One ROI covering both A2 and Ax, as a real detector would see it ---");
        var shared = new ROIZone { laneName = "Ax", alsoCountLanes = new[] { "A2" } };
        Check(shared.Accepts("Ax"),  "the shared ROI accepts lane Ax");
        Check(shared.Accepts("A2"),  "the shared ROI accepts lane A2");
        Check(!shared.Accepts("A"),  "the shared ROI must not accept lane A (a different physical lane, 3.5 m away)");
        Check(!shared.Accepts("Bx"), "the shared ROI must not accept lane Bx");
        Check(shared.DisplayName == "Ax+A2", $"the on-screen name lists both movements (got '{shared.DisplayName}')");

        // Mix vehicles from both lanes into one ROI and check they can still be told apart
        var dict = (Dictionary<CarAgent, float>)PrivateField(typeof(ROIZone), "carsInZone").GetValue(shared);
        dict.Clear();
        for (int i = 0; i < 7; i++) dict[new CarAgent { assignedLane = "Ax" }] = 10f;
        for (int i = 0; i < 2; i++) dict[new CarAgent { assignedLane = "A2" }] = 10f;
        Check(shared.GetVehicleCount() == 9,
              $"GetVehicleCount() = all 9 vehicles in the outer lane, which is what a real detector sees (got {shared.GetVehicleCount()})");
        Check(shared.GetVehicleCount("Ax") == 7 && shared.GetVehicleCount("A2") == 2,
              $"per-movement counts are still available for inspection (Ax={shared.GetVehicleCount("Ax")}, A2={shared.GetVehicleCount("A2")})");

        // ============ Yielding lane: inverted signal ============
        Console.WriteLine("\n--- Ax yields: Bx green or yellow = Ax stops, Bx red = Ax may go ---");
        var greenField = PrivateField(typeof(CarAgent), "lightIsGreen");
        Func<TrafficLight.LightState, bool, bool> CanGo = (state, inverted) =>
        {
            var c = new CarAgent { lightIsInverted = inverted };
            c.OnLightStateChanged(state);
            return (bool)greenField.GetValue(c);
        };

        Check(!CanGo(TrafficLight.LightState.Green,  true),  "Bx green  -> Ax may not go");
        Check(!CanGo(TrafficLight.LightState.Yellow, true),  "Bx yellow -> Ax still may not go (Bx traffic has not cleared the entrance)");
        Check( CanGo(TrafficLight.LightState.Red,    true),  "Bx red    -> Ax may go as normal");

        // An ordinary lane must be unaffected
        Check( CanGo(TrafficLight.LightState.Green, false), "ordinary lane: its own green -> may go (inversion does not apply)");
        Check(!CanGo(TrafficLight.LightState.Red,   false), "ordinary lane: its own red  -> stops");

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
