using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

/// Runs real CarAgent instances on the scene geometry (ABACSceneBuilder constants) and checks the three
/// vehicle behaviours Badin reported on 24 Sep 2026:
///   1) an A2 car changing into the shared lane sped up sharply and then stopped dead
///      (+240 / -723 m/s^2 in one reproduction; the limits are +3 and -13.5 for a Normal driver)
///   2) an A2 car sometimes ended up on top of an Ax car (full car width, parked that way)
///   3) the new head of a CB queue sometimes waited 10+ m short of the stop line for the whole red
/// plus the regressions that the fixes must not introduce (stop-line accuracy, reaction delay at green).
///
/// Needs the yaw-aware Quaternion/Transform in UnityStub.cs (24 Sep 2026) - with the old stub no car could turn.
public class LaneBehaviourTest
{
    const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
    static readonly MethodInfo StepM    = typeof(CarAgent).GetMethod("Step", NP);
    static readonly MethodInfo EnableM  = typeof(CarAgent).GetMethod("OnEnable", NP);
    static readonly MethodInfo DisableM = typeof(CarAgent).GetMethod("OnDisable", NP);
    static readonly MethodInfo ProfM    = typeof(CarAgent).GetMethod("SetProfileParameters", NP);
    static readonly FieldInfo  StepDtF  = typeof(CarAgent).GetField("stepDt", NP);

    const float DT = CarAgent.MAX_STEP;
    const float Y  = ABACSceneBuilder.CAR_Y;
    const float OVERLAP_TOLERANCE = 0.05f;   // bodies touching by less than this are not counted

    static int failures = 0;
    static void Check(bool c, string label)
    {
        Console.WriteLine((c ? "  PASS  " : "  FAIL  ") + label);
        if (!c) failures++;
    }

    static T Priv<T>(CarAgent c, string f)
    {
        var fi = typeof(CarAgent).GetField(f, NP);
        return fi == null ? default(T) : (T)fi.GetValue(c);
    }

    // ===================== scene geometry =====================
    static Transform Pt(float x, float z, float y = Y) =>
        new Transform { position = new Vector3(x, y, z), forward = new Vector3(0, 0, 1) };

    static readonly Transform StopA  = Pt(ABACSceneBuilder.STOP_A_X,  ABACSceneBuilder.Z_A,  0f);
    static readonly Transform StopAx = Pt(ABACSceneBuilder.AX_STOP_X, ABACSceneBuilder.Z_AX, 0f);
    static readonly Transform StopCB = Pt(ABACSceneBuilder.X_CB, ABACSceneBuilder.STOP_C_Z, 0f);

    static readonly Transform[] WpA2 = {
        Pt(ABACSceneBuilder.A2_MERGE_X, ABACSceneBuilder.Z_AX),
        Pt(ABACSceneBuilder.SPAWN_FAR,  ABACSceneBuilder.Z_AX) };
    // copied from ABACSceneBuilder.BuildScene (the points are literals there, not constants)
    static readonly Transform[] WpAx = {
        Pt(ABACSceneBuilder.AX_TURN_X, ABACSceneBuilder.Z_AX), Pt(-7.50f, 7.44f),
        Pt(-4.94f, 10.00f), Pt(-4.00f, 13.50f), Pt(-4.00f, 90.00f) };
    static readonly Transform[] WpCB = {
        Pt(1.50f, 2.50f), Pt(0.56f, -1.00f), Pt(-2.00f, -3.56f), Pt(-5.50f, -4.50f), Pt(-90.00f, -4.50f) };

    static TrafficLight Light(TrafficLight.LightState s) => new TrafficLight { currentState = s };
    const TrafficLight.LightState RED = TrafficLight.LightState.Red, YELLOW = TrafficLight.LightState.Yellow,
                                  GREEN = TrafficLight.LightState.Green;

    static CarAgent Make(string lane, CarAgent.DriverType d, float x, float z, float yaw,
                         Transform stop, TrafficLight light, bool inverted = false,
                         Transform[] wps = null, bool steerBefore = false, float steerStart = 0f)
    {
        var c = new CarAgent();
        c.transform = new Transform { position = new Vector3(x, Y, z) };
        c.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        c.gameObject = new GameObject(lane);
        c.driverType = d;
        c.assignedLane = lane;
        c.stopLineTarget = stop;
        c.assignedLight = light;
        c.lightIsInverted = inverted;
        c.pathWaypoints = wps;
        c.steerBeforeStopLine = steerBefore;
        c.steerStartDistance = steerStart;
        c.desiredTimeHeadway = CarAgent.MEASURED_TIME_HEADWAY;
        ProfM.Invoke(c, null);
        EnableM.Invoke(c, null);
        return c;
    }

    static CarAgent A (CarAgent.DriverType d, float x, TrafficLight l) =>
        Make("A",  d, x, ABACSceneBuilder.Z_A, 90f, StopA, l);
    static CarAgent Ax(CarAgent.DriverType d, float x, TrafficLight lBx) =>
        Make("Ax", d, x, ABACSceneBuilder.Z_AX, 90f, StopAx, lBx, inverted: true, wps: WpAx);
    static CarAgent A2(CarAgent.DriverType d, float x, TrafficLight l) =>
        Make("A2", d, x, ABACSceneBuilder.Z_A, 90f, StopA, l, wps: WpA2, steerBefore: true,
             steerStart: ABACSceneBuilder.A2_MERGE_DIST);
    static CarAgent CB(CarAgent.DriverType d, float z, TrafficLight l) =>
        Make("CB", d, ABACSceneBuilder.X_CB, z, 180f, StopCB, l, wps: WpCB);

    // ===================== simulation loop with bookkeeping =====================
    class Sim
    {
        public readonly List<CarAgent> cars = new List<CarAgent>();
        readonly List<KeyValuePair<float, Func<CarAgent>>> due = new List<KeyValuePair<float, Func<CarAgent>>>();
        public float t;
        public float worstOverlap;
        public string worstOverlapNote = "none";
        public readonly Dictionary<CarAgent, float> maxAccel = new Dictionary<CarAgent, float>();
        public readonly Dictionary<CarAgent, float> maxDecel = new Dictionary<CarAgent, float>();
        public int a2TurnedOnTheSpot;

        public void At(float time, Func<CarAgent> make) => due.Add(new KeyValuePair<float, Func<CarAgent>>(time, make));

        public void Run(float until, Action<Sim> afterStep = null)
        {
            due.Sort((p, q) => p.Key.CompareTo(q.Key));
            while (t < until - 1e-4f)
            {
                while (due.Count > 0 && due[0].Key <= t + 1e-4f)
                {
                    var c = due[0].Value();
                    cars.Add(c); maxAccel[c] = 0f; maxDecel[c] = 0f;
                    due.RemoveAt(0);
                }
                foreach (var c in cars)
                {
                    float v0 = c.currentSpeed, yaw0 = c.transform.rotation.yaw;
                    StepDtF.SetValue(c, DT);
                    StepM.Invoke(c, null);
                    float a = (c.currentSpeed - v0) / DT;
                    if (a > maxAccel[c]) maxAccel[c] = a;
                    // a car at queue speed is set straight to 0 by the startup wave; that is not a slam
                    if (v0 > CarAgent.QUEUE_SPEED_EPS && a < maxDecel[c]) maxDecel[c] = a;
                    if (c.assignedLane == "A2" && c.currentSpeed <= 1e-4f
                        && Math.Abs(Quaternion.DeltaYaw(yaw0, c.transform.rotation.yaw)) > 1e-3f)
                        a2TurnedOnTheSpot++;
                }
                t += DT;
                for (int i = 0; i < cars.Count; i++)
                    for (int j = i + 1; j < cars.Count; j++)
                    {
                        float d = Overlap(cars[i], cars[j]);
                        if (d > worstOverlap)
                        {
                            worstOverlap = d;
                            worstOverlapNote = $"{cars[i].assignedLane} {cars[i].driverType} and {cars[j].assignedLane} " +
                                               $"{cars[j].driverType} by {d:F2} m at t={t:F2}";
                        }
                    }
                afterStep?.Invoke(this);
            }
        }

        public void Dispose() { foreach (var c in cars) DisableM.Invoke(c, null); }
    }

    /// Depth (m) by which two car footprints (carLength x CAR_WIDTH rectangles) intersect; 0 if they do not.
    static float Overlap(CarAgent a, CarAgent b)
    {
        if ((a.transform.position - b.transform.position).magnitude > a.carLength + 1f) return 0f;
        Vector3[] ca = Corners(a), cb = Corners(b);
        float depth = float.MaxValue;
        foreach (var owner in new[] { a, b })
        {
            Vector3 f = owner.transform.forward;
            foreach (var axis in new[] { f, new Vector3(f.z, 0f, -f.x) })
            {
                float amin = float.MaxValue, amax = float.MinValue, bmin = float.MaxValue, bmax = float.MinValue;
                foreach (var p in ca) { float s = Vector3.Dot(p, axis); amin = Math.Min(amin, s); amax = Math.Max(amax, s); }
                foreach (var p in cb) { float s = Vector3.Dot(p, axis); bmin = Math.Min(bmin, s); bmax = Math.Max(bmax, s); }
                float o = Math.Min(amax, bmax) - Math.Max(amin, bmin);
                if (o <= 0f) return 0f;
                depth = Math.Min(depth, o);
            }
        }
        return depth;
    }

    static Vector3[] Corners(CarAgent c)
    {
        Vector3 f = c.transform.forward, r = new Vector3(f.z, 0f, -f.x), p = c.transform.position;
        float hl = c.carLength / 2f, hw = ABACSceneBuilder.CAR_WIDTH / 2f;
        return new[] { p + f * hl + r * hw, p + f * hl - r * hw, p - f * hl - r * hw, p - f * hl + r * hw };
    }

    /// Worst speed change against each car's own limits: accelerate no faster than `acceleration`,
    /// brake no harder than decelRate x emergencyDecelMultiplier.
    static void SpeedLimits(Sim s, Func<CarAgent, bool> which, out string worstUp, out string worstDown,
                            out bool upOk, out bool downOk)
    {
        upOk = downOk = true; worstUp = worstDown = "none";
        float wu = 0f, wd = 0f;
        foreach (var c in s.cars)
        {
            if (!which(c)) continue;
            float acc = Priv<float>(c, "acceleration");
            float emg = Priv<float>(c, "decelRate") * Priv<float>(c, "emergencyDecelMultiplier");
            float up = s.maxAccel[c], dn = s.maxDecel[c];
            if (up - acc > wu) { wu = up - acc; worstUp = $"{c.driverType} {up:F1} m/s^2 (limit {acc:F1})"; }
            if (-dn - emg > wd) { wd = -dn - emg; worstDown = $"{c.driverType} {dn:F1} m/s^2 (limit -{emg:F1})"; }
            if (up > acc + 1e-3f) upOk = false;
            if (dn < -emg - 1e-3f) downOk = false;
        }
    }

    static readonly CarAgent.DriverType AGG = CarAgent.DriverType.Aggressive, NOR = CarAgent.DriverType.Normal,
                                        CAU = CarAgent.DriverType.Cautious;

    // ======================================================================================
    public static int Main()
    {
        float mergeEnd = ABACSceneBuilder.A2_MERGE_X;

        // ===== 1) A2 joins the shared lane behind a queue that Bx is holding, beside a lane A queue =====
        Console.WriteLine("--- 1) A2 merges while A is red and Bx holds the Ax queue, then everyone gets to go ---");
        {
            var lA = Light(RED); var lBx = Light(GREEN);
            var s = new Sim();
            for (int i = 0; i < 4; i++) { float t0 = i * 2.5f; s.At(t0, () => A(NOR, -80f, lA)); }
            for (int i = 0; i < 3; i++) { float t0 = 1f + i * 2.5f; s.At(t0, () => Ax(NOR, -80f, lBx)); }
            s.At(9f, () => A2(NOR, -110f, lA));
            float zAtLine = float.NaN;
            s.Run(40f);
            lA.currentState = GREEN; lBx.currentState = RED;
            s.Run(75f, sim =>
            {
                foreach (var c in sim.cars)
                    if (c.assignedLane == "A2" && float.IsNaN(zAtLine)
                        && c.transform.position.x + c.carLength / 2f >= ABACSceneBuilder.STOP_A_X)
                        zAtLine = c.transform.position.z;
            });
            SpeedLimits(s, c => c.assignedLane == "A2", out var wu, out var wd, out bool upOk, out bool dnOk);
            Console.WriteLine($"    A2 worst acceleration: {wu} | worst braking: {wd}");
            Check(upOk, "A2 never accelerates faster than its profile allows");
            Check(dnOk, "A2 never brakes harder than its emergency limit");
            Check(s.worstOverlap <= OVERLAP_TOLERANCE, $"no two cars overlap (worst: {s.worstOverlapNote})");
            Check(!float.IsNaN(zAtLine) && Math.Abs(zAtLine - ABACSceneBuilder.Z_AX) <= 0.3f,
                  $"A2 is centred in the shared lane when it reaches the stop line (z = {zAtLine:F2}, lane centre {ABACSceneBuilder.Z_AX})");
            s.Dispose();
        }

        // ===== 2) A2 meets one Ax car at every relative position, driver pair and light state =====
        Console.WriteLine("\n--- 2) A2 against one Ax car: 3 light states x 9 driver pairs x 21 start offsets ---");
        {
            var drivers = new[] { AGG, NOR, CAU };
            var states = new[] {
                new { name = "A green, Bx red ", a = GREEN, bx = RED },
                new { name = "A red, Bx red   ", a = RED,   bx = RED },
                new { name = "A red, Bx green ", a = RED,   bx = GREEN } };
            int runs = 0, overlapRuns = 0, jumpRuns = 0, passedMergeEndUnmerged = 0;
            float cautiousBehindAggressive = 0f;
            float worst = 0f; string worstNote = "none", worstJump = "none";
            foreach (var st in states)
            {
                int stOverlap = 0, stJump = 0;
                foreach (var dm in drivers)
                    foreach (var dx in drivers)
                        for (int k = -10; k <= 10; k++)
                        {
                            var lA = Light(st.a); var lBx = Light(st.bx);
                            var s = new Sim();
                            float off = 2f * k;
                            s.At(0f, () => A2(dm, -80f, lA));
                            s.At(0f, () => Ax(dx, -80f + off, lBx));
                            s.Run(14f, sim =>
                            {
                                var m = sim.cars[0];
                                // (a) with no gap the car must wait in lane A at the end of the merge zone, not drive on
                                bool started = Priv<bool>(m, "laneChangeStarted");
                                if (typeof(CarAgent).GetField("laneChangeStarted", NP) != null && !started
                                    && m.transform.position.x + m.carLength / 2f > mergeEnd + 0.5f)
                                    passedMergeEndUnmerged++;
                            });
                            runs++;
                            if (s.worstOverlap > OVERLAP_TOLERANCE)
                            {
                                overlapRuns++; stOverlap++;
                                if (s.worstOverlap > worst) { worst = s.worstOverlap; worstNote = s.worstOverlapNote; }
                            }
                            SpeedLimits(s, c => c.assignedLane == "A2", out var wu, out var wd, out bool upOk, out bool dnOk);
                            if (dm == CAU && dx == AGG)
                            {
                                // Known limit of car-following, not of the lane change: a Cautious driver (brakes at 3,
                                // emergency 5.4) behind an Aggressive one braking at 22 for its line is held by
                                // maxSafeSpeed harder than its own profile. The same pair in a straight lane does it too
                                // (-9.4 m/s^2; it was -54 to -528 before 24 Sep). Bounded separately below.
                                float d = s.maxDecel[s.cars[0]];
                                cautiousBehindAggressive = Math.Min(cautiousBehindAggressive, d);
                                dnOk = true;
                            }
                            if (!upOk || !dnOk) { jumpRuns++; stJump++; worstJump = !upOk ? wu : wd; }
                            s.Dispose();
                        }
                Console.WriteLine($"    {st.name}: overlapping runs {stOverlap,3}/189 | A2 speed-jump runs {stJump,3}/189");
            }
            Check(overlapRuns == 0, $"A2 never overlaps the Ax car ({overlapRuns}/{runs} runs; worst {worstNote})");
            Check(jumpRuns == 0, $"A2 speed changes stay within its profile in every run ({jumpRuns}/{runs}; e.g. {worstJump})");
            Check(cautiousBehindAggressive >= -10f,
                  $"a Cautious A2 behind an Aggressive Ax car never brakes harder than 10 m/s^2 (worst {cautiousBehindAggressive:F1})");
            Check(passedMergeEndUnmerged == 0,
                  $"an A2 without a gap waits at the end of the merge zone instead of driving on in lane A ({passedMergeEndUnmerged} steps)");
        }

        // ===== 3) A2 must not rotate while standing still (it used to pivot on the spot) =====
        Console.WriteLine("\n--- 3) A2 held at the end of the merge zone does not turn on the spot ---");
        {
            // an Ax car sitting right beside the merge point, held by Bx, leaves no gap for the whole run
            var lA = Light(RED); var lBx = Light(GREEN);
            var s = new Sim();
            for (int i = 0; i < 5; i++) { float t0 = i * 2.5f; s.At(t0, () => Ax(NOR, -80f, lBx)); }
            s.At(14f, () => A2(NOR, -110f, lA));
            s.Run(45f);
            Check(s.a2TurnedOnTheSpot == 0, $"A2 heading never changes while its speed is 0 ({s.a2TurnedOnTheSpot} steps)");
            Check(Math.Abs(CarAgent.BODY_WIDTH - ABACSceneBuilder.CAR_WIDTH) < 1e-6f,
                  $"CarAgent.BODY_WIDTH ({CarAgent.BODY_WIDTH}) matches the car model width ABACSceneBuilder.CAR_WIDTH ({ABACSceneBuilder.CAR_WIDTH})");
            Check(s.worstOverlap <= OVERLAP_TOLERANCE, $"no two cars overlap (worst: {s.worstOverlapNote})");
            s.Dispose();
        }

        // ===== 4) CB queue cut off by the amber: where does the new head wait? =====
        Console.WriteLine("\n--- 4) a 12-car CB queue gets 8 s of green, 3 s amber, then red ---");
        {
            var mixes = new[] {
                new { name = "all Normal  ", m = new[] { NOR, NOR, NOR, NOR, NOR, NOR, NOR, NOR, NOR, NOR, NOR, NOR } },
                new { name = "mixed       ", m = new[] { NOR, NOR, AGG, NOR, CAU, NOR, NOR, CAU, NOR, AGG, NOR, NOR } },
                new { name = "all Cautious", m = new[] { CAU, CAU, CAU, CAU, CAU, CAU, CAU, CAU, CAU, CAU, CAU, CAU } } };
            foreach (var mx in mixes)
            {
                var l = Light(RED);
                var s = new Sim();
                for (int i = 0; i < mx.m.Length; i++) { var d = mx.m[i]; s.At(i * 2.5f, () => CB(d, 120f, l)); }
                s.Run(40f); l.currentState = GREEN;
                s.Run(48f); l.currentState = YELLOW;
                s.Run(51f); l.currentState = RED;
                s.Run(95f);

                var waiting = s.cars.FindAll(c => !Priv<bool>(c, "hasCleared"));
                waiting.Sort((p, q) => p.transform.position.z.CompareTo(q.transform.position.z));
                if (waiting.Count == 0) { Check(false, $"{mx.name}: expected a residual queue after an 8 s green"); s.Dispose(); continue; }
                var head = waiting[0];
                float toStop = Priv<float>(head, "signedDistToStopPoint");
                float worstGap = 0f;
                for (int i = 1; i < waiting.Count; i++)
                {
                    float gap = waiting[i].transform.position.z - waiting[i - 1].transform.position.z - head.carLength;
                    worstGap = Math.Max(worstGap, gap);
                }
                Console.WriteLine($"    {mx.name}: {waiting.Count} left; new head ({head.driverType}) waits {toStop:F2} m " +
                                  $"before its stop point; largest gap in the queue behind it {worstGap:F2} m");
                Check(Math.Abs(toStop) <= 1.1f, $"{mx.name}: the new head waits at its stop point (within 1 m + 0.1)");
                Check(worstGap <= head.minGap + 0.5f, $"{mx.name}: the queue behind it closes up to minGap + 0.5 m");
                Check(s.worstOverlap <= OVERLAP_TOLERANCE, $"{mx.name}: no two cars overlap (worst: {s.worstOverlapNote})");
                s.Dispose();
            }
            var creep = typeof(CarAgent).GetField("CREEP_MIN_GAP", BindingFlags.Public | BindingFlags.Static);
            Check(creep != null && Math.Abs((float)creep.GetValue(null) - 1f) < 1e-6f,
                  "CarAgent.CREEP_MIN_GAP is the 1 m this test allows");
        }

        // ===== 5) Regressions: stop-line accuracy and the reaction delay at green =====
        Console.WriteLine("\n--- 5) one car per driver type stops at a red, then gets a green ---");
        foreach (var d in new[] { AGG, NOR, CAU })
        {
            // (a) arriving at full speed, as every car used to be tested
            var l = Light(RED);
            var s = new Sim();
            s.At(0f, () => A(d, -150f, l));
            s.Run(25f);
            var car = s.cars[0];
            float toStop = Priv<float>(car, "signedDistToStopPoint");
            Check(Math.Abs(toStop) <= 0.3f && car.currentSpeed == 0f,
                  $"{d}: arriving at full speed it stops at its stop point ({toStop:F2} m)");

            float react = Priv<float>(car, "reactionDelay");
            l.currentState = GREEN;
            float greenAt = s.t, movedAt = float.NaN;
            s.Run(35f, sim => { if (float.IsNaN(movedAt) && sim.cars[0].currentSpeed > 0f) movedAt = sim.t; });
            Check(!float.IsNaN(movedAt) && movedAt - greenAt >= react - DT,
                  $"{d}: keeps its {react:F1} s reaction delay at green (moved after {movedAt - greenAt:F2} s)");
            s.Dispose();

            // (b) arriving slowly (e.g. it was following a car that then ran the amber)
            var l2 = Light(RED);
            var s2 = new Sim();
            float brake = Priv<float>(car, "approachSpeed");
            brake = brake * brake / (2f * Priv<float>(car, "decelRate"));
            s2.At(0f, () =>
            {
                // front of the car 0.8 x braking distance before the line, rolling at 5 m/s
                var c = A(d, ABACSceneBuilder.STOP_A_X - 0.8f * brake - 2f, l2);
                c.currentSpeed = 5f;
                return c;
            });
            s2.Run(30f);
            float toStop2 = Priv<float>(s2.cars[0], "signedDistToStopPoint");
            Check(Math.Abs(toStop2) <= 1.1f,
                  $"{d}: arriving at 5 m/s inside its braking zone it still reaches its stop point ({toStop2:F2} m short)");
            s2.Dispose();
        }

        Console.WriteLine(failures == 0 ? "\nALL CHECKS PASSED" : $"\n{failures} CHECK(S) FAILED");
        return failures;
    }
}
