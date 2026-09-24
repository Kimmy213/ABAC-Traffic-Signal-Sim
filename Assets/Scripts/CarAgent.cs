using UnityEngine;

public class CarAgent : MonoBehaviour
{
    public enum DriverType { Aggressive, Normal, Cautious }

    [Header("Driver Profile")]
    public DriverType driverType = DriverType.Normal;

    [Header("Car Dimensions")]
    public float carLength = 4f;

    private float approachSpeed;
    private float stopOvershoot;
    private float reactionDelay;
    private float acceleration;
    private float decelRate;
    private float emergencyDecelMultiplier;  // how much harder an emergency stop is than a normal one
    private float dilemmaZoneFactor;         // higher = more willing to run an amber

    [Header("Body Paint (the part that gets the lane colour)")]
    [Tooltip("Renderer for the car body - set automatically when the prefab is built by ABAC > Rebuild Car Prefab.\n" +
             "Never paint every Renderer in the car, or the wheels, glass and lights get the lane colour too.")]
    public Renderer bodyRenderer;

    [Tooltip("Material slot of the body within bodyRenderer. The model has several slots " +
             "(body / glass / tyres / headlights / tail lights); only the body slot may be painted.")]
    public int bodyMaterialIndex = 0;

    [Header("Lane Identity")]
    public string assignedLane = "";   // set by CarSpawner at spawn
    [Header("Runtime State")]
    public float currentSpeed = 0f;
    public bool isStopped = false;

    // ===== Standing queue placement =====
    // Normally a car spawns at the top of the lane and drives in, so SetProfileParameters sets
    // currentSpeed = approachSpeed. Cars placed by CarSpawner.PlaceStandingQueue are already stopped
    // at a red from second 0; spawning them at cruising speed makes the whole row skid across the stop
    // line on a red (a Cautious driver at 13.89 m/s with 3 m/s^2 slides 32 m). This flag makes
    // SetProfileParameters start at zero and treat the car as stopped, so reactionDelay still applies.
    [HideInInspector] public bool startAtRest = false;

    /// <summary>How far before the stop line the nose of the first queued car must sit, in metres.
    /// Must exceed the most negative stopOvershoot (Cautious = -1 m) plus the -0.3 threshold in
    /// ComputeDesiredSpeed, or a Cautious car counts as hasCleared on frame one and runs the red.</summary>
    public const float REST_FRONT_MARGIN = 1f;
    private bool hasCleared = false;
    private bool committedThrough = false;   // true = already decided to run the amber; will not brake now

    [Header("Car Following (keeps cars from overlapping)")]
    public float minGap = 2f;
    [Tooltip("Maximum lateral distance still counted as the same lane, in metres. Lanes in the scene are " +
             "3.5 m apart, so this must be under half of 3.5 or cars in different lanes brake for each other.")]
    public float lookAheadAngle = 1.5f;
    [Tooltip("Time gap a driver tries to keep from the car ahead, in seconds.\n" +
             "Actual gap = minGap + desiredTimeHeadway x speed. The old model kept only a constant minGap " +
             "at any speed, so cars could travel 2 m apart at 15 m/s = 0.4 s headway.\n\n" +
             "This sets the saturation headway of the whole junction, tuned so the result matches the " +
             "2.31 s/veh measured from the clip (section 2.4). HeadwayTest enforces it - re-run the tests if you change it.")]
    public float desiredTimeHeadway = MEASURED_TIME_HEADWAY;

    /// <summary>Value that makes queue discharge come out at the measured 2.31 s/veh (enforced by HeadwayTest)</summary>
    public const float MEASURED_TIME_HEADWAY = 1.9f;

    [Tooltip("How far ahead to look for another car, in metres. Never set this below the braking " +
             "distance of a Cautious driver (~33 m), or a car sees the queue too late to stop.")]
    public float lookAheadDistance = 50f;

    [Header("Shared Lane Bypass (only for lanes physically shared, e.g. Ax/A2)")]
    [Tooltip("true = this car may use the shoulder to pass a blocking car in the shared lane. Drawn once " +
             "at spawn by CarSpawner from laneBypassChance, modelling what Itim observed in reality " +
             "(cars edging right, motorcycles filtering, security waving people through).\n\n" +
             "The old implementation simply ignored cars from other lanes in GetCarAhead(), which was wrong: " +
             "cars drove straight through stopped ones and visibly overlapped. It now offsets the car onto " +
             "the shoulder by bypassLateralOffset and lets ordinary car-following handle the real distances.")]
    public bool canBypassCrossLaneBlocker = false;

    [Tooltip("Lateral offset onto the shoulder, in world space, while passing a queue in a shared lane. Set " +
             "by CarSpawner. Applies only before the turn begins; after that the car returns to its normal path.")]
    public Vector3 bypassLateralOffset = Vector3.zero;

    private static System.Collections.Generic.List<CarAgent> allCars
        = new System.Collections.Generic.List<CarAgent>();

    [Header("Stop Line (set at spawn)")]
    public Transform stopLineTarget;
    private float signedDistToStopPoint;

    [Header("Turning Path (set at spawn - empty = drive straight, as before)")]
    [Tooltip("Guide points past the stop line, used by the turning lanes Ax / Bx / CB / CA")]
    public Transform[] pathWaypoints;
    [Tooltip("Steering rate in degrees per second")]
    public float steerRate = 55f;
    [Tooltip("Target turning radius in metres - smaller is tighter, but the car has to slow more")]
    public float turnRadius = 7f;
    [Tooltip("How many degrees of turn before the car starts slowing")]
    public float turnSlowAngle = 12f;
    [Tooltip("Slowest speed while turning, in m/s, so cars do not crawl to a standstill")]
    public float minTurnSpeed = 3f;
    [Tooltip("Extra margin beyond the real braking distance, in metres, before the turn point")]
    public float turnBrakeMargin = 4f;
    [Tooltip("Coming this close to a waypoint counts as having reached it")]
    public float waypointReachRadius = 2f;

    [Header("Late Merge (lane change before the stop line - used by A2)")]
    [Tooltip("true = may turn or change lane before passing the stop line.\n" +
             "Normally a car with a stop line drives straight until past it, so signedDistToStopPoint stays valid.\n" +
             "A2 has to merge into the shared lane before the stop line. The lateral offset does not affect " +
             "braking distance, because the cars run parallel to X and signedDistToStopPoint uses only the X difference.")]
    public bool steerBeforeStopLine = false;

    [Tooltip("Do not start the lane change until the first waypoint is closer than this, in metres. 0 = from spawn.\n\n" +
             "This is the heart of the late merge: Itim counted from the clip that all 3 through vehicles " +
             "entering the shared lane changed with only about five car lengths left before the turn, " +
             "inserting right at the head of the queue. Merging from further back would spread them through " +
             "the queue: the same mean, but quite different dynamics from the real rare-but-severe pattern.")]
    public float steerStartDistance = 0f;

    private int waypointIndex = 0;
    /// <summary>true = finished the assigned path (CarSpawner uses this to decide when to remove the car)</summary>
    public bool reachedEndOfPath { get; private set; }

    [Header("Traffic Light (set at spawn - leave empty for a free-flow lane)")]
    [Tooltip("The car reads the signal state from this every frame, as a fallback if its registration is lost")]
    public TrafficLight assignedLight;

    [Tooltip("true = a yielding lane. The referenced signal belongs to the *other* stream, not to us.\n" +
             "Their green or yellow means we must stop; their red means we may go.\n\n" +
             "Used by lane Ax: normally free-flowing, but in reality it cannot turn into the campus while " +
             "Bx is green, because Bx turns in as well and sweeps across the entrance, so Ax is tied to the " +
             "Bx signal, inverted. Ax still has isSignalized = false in IntersectionConfig, because it has " +
             "no phase of its own, no allocated green time, and no place in the conflict matrix.")]
    public bool lightIsInverted = false;

    private bool lightIsGreen = false;
    private float reactionTimer = 0f;
    private TrafficLight.LightState lastSeenLight = TrafficLight.LightState.Red;
    private bool lightInitialized = false;

    // ===== Startup wave =====
    private float startupTimer = 0f;
    /// <summary>Slower than this counts as standing in the queue, in m/s</summary>
    public const float QUEUE_SPEED_EPS = 0.1f;
    /// <summary>The car ahead must exceed this before a follower starts its reaction timer</summary>
    public const float LEADER_MOVING_SPEED = 0.5f;

    void OnEnable()  { allCars.Add(this); }
    void OnDisable() { allCars.Remove(this); }

    void Start()
    {
        SetProfileParameters();
    }

    void SetProfileParameters()
    {
        switch (driverType)
        {
            case DriverType.Aggressive:
                approachSpeed = 16.67f;
                stopOvershoot = 0.5f; reactionDelay = 0.2f; acceleration = 5f;
                decelRate = 22f;
                emergencyDecelMultiplier = 1.3f;  // already brakes hard, so not much more
                dilemmaZoneFactor = 1.6f;         // readily runs an amber
                break;
            case DriverType.Normal:
                approachSpeed = 15.28f;
                stopOvershoot = 0f; reactionDelay = 0.8f; acceleration = 3f;
                decelRate = 9f;
                emergencyDecelMultiplier = 1.5f;
                dilemmaZoneFactor = 1.1f;
                break;
            case DriverType.Cautious:
                approachSpeed = 13.89f;
                stopOvershoot = -1f; reactionDelay = 1.5f; acceleration = 2.5f;
                decelRate = 3f;
                emergencyDecelMultiplier = 1.8f;  // normally gentle, but brakes much harder when startled
                dilemmaZoneFactor = 0.7f;         // only runs an amber when genuinely unable to stop
                break;
        }
        currentSpeed = startAtRest ? 0f : approachSpeed;
        if (startAtRest) { isStopped = true; reactionTimer = 0f; }
    }

    Vector3 GetFrontPosition()
    {
        return transform.position + transform.forward * (carLength / 2f);
    }

    Vector3 GetStopPoint()
    {
        return stopLineTarget.position + transform.forward * stopOvershoot;
    }

    float GetRequiredBrakeDistance()
    {
        return (approachSpeed * approachSpeed) / (2f * decelRate);
    }

    // ===== Sub-stepping: motion resolution must not depend on timeScale =====
    // It used to integrate once per frame with Time.deltaTime, which is real frame time x timeScale.
    // At 10x and 60 fps one frame is 0.167 s, so a car at 14 m/s moves 2.3 m per step and braking and
    // stop-line decisions were evaluated only every 2.3 m, overshooting lanes and stop lines (clearly
    // visible at 10x, barely at 2x). Each frame is now split into steps of at most MAX_STEP (= the Unity
    // fixed timestep), so at 1x or 10x cars decide every 0.02 simulated seconds alike.
    public const float MAX_STEP = 0.02f;
    const int MAX_SUBSTEPS = 50;            // guards against a spiral of death on a very slow frame (Unity caps deltaTime at 0.333 s)
    private float stepDt = 0f;

    void Update()
    {
        float remaining = Time.deltaTime;
        int steps = 0;
        while (remaining > 0f && steps < MAX_SUBSTEPS)
        {
            stepDt = Mathf.Min(remaining, MAX_STEP);
            Step();
            remaining -= stepDt;
            steps++;
        }
    }

    /// <summary>One simulation step, stepDt seconds long</summary>
    void Step()
    {
        SyncLightState();

        if (stopLineTarget != null && !hasCleared)
        {
            Vector3 frontPos = GetFrontPosition();
            Vector3 stopPoint = GetStopPoint();
            signedDistToStopPoint = Vector3.Dot(stopPoint - frontPos, transform.forward);
        }

        DecideSpeed();
        Steer();
        MoveForward();
    }

    /// <summary>How many steps one frame is split into - public static so tests can check it without Unity</summary>
    public static int SubStepsFor(float frameDt, float maxStep = MAX_STEP)
    {
        if (frameDt <= 0f) return 0;
        int n = Mathf.CeilToInt(frameDt / maxStep);
        return Mathf.Min(n, MAX_SUBSTEPS);
    }

    // ===== Read the signal state every frame =====
    // This used to rely on TrafficLight.NotifyCars() alone. If a registration was lost (for instance to
    // ClearRegistrations after spawn) the car never learned the light had gone green and sat forever.
    // Push and poll now work together; whichever arrives first gives the same result.
    void SyncLightState()
    {
        if (assignedLight == null) return;

        TrafficLight.LightState st = assignedLight.currentState;
        if (!lightInitialized || st != lastSeenLight)
        {
            OnLightStateChanged(st);
        }
    }

    // ===== Follow the waypoints once past the stop line =====
    // Before the stop line the car always drives straight, so signedDistToStopPoint and the ROI behave as before.
    void Steer()
    {
        if (pathWaypoints == null || pathWaypoints.Length == 0) return;
        if (stopLineTarget != null && !hasCleared && !steerBeforeStopLine) return;
        if (reachedEndOfPath) return;

        // skip waypoints that are null or have been destroyed
        while (waypointIndex < pathWaypoints.Length && pathWaypoints[waypointIndex] == null)
            waypointIndex++;

        if (waypointIndex >= pathWaypoints.Length)
        {
            reachedEndOfPath = true;
            return;
        }

        Vector3 toTarget = CurrentWaypointTarget() - transform.position;
        toTarget.y = 0f;

        // ===== Late merge: while still far away, stay in the current lane rather than edging across =====
        if (steerStartDistance > 0f && waypointIndex == 0
            && Vector3.Dot(toTarget, transform.forward) > steerStartDistance) return;

        // A waypoint already behind the car counts as reached.
        // Guards against a car turning so wide it never enters the radius and circles forever.
        bool reached = toTarget.magnitude <= waypointReachRadius
                       || Vector3.Dot(toTarget, transform.forward) < 0f;

        if (reached)
        {
            waypointIndex++;
            return;
        }

        Quaternion desired = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, desired, steerRate * stepDt);
    }

    float ComputeDesiredSpeed()
    {
        if (hasCleared || stopLineTarget == null)
        {
            return Mathf.MoveTowards(currentSpeed, approachSpeed, acceleration * stepDt);
        }

        if (signedDistToStopPoint < -0.3f)
        {
            hasCleared = true;
            isStopped = false;
            return Mathf.MoveTowards(currentSpeed, approachSpeed, acceleration * stepDt);
        }

        float brakeDistance = GetRequiredBrakeDistance();

        if (signedDistToStopPoint <= brakeDistance)
        {
            if (!lightIsGreen)
            {
                // ===== No snapping to 0 any more: follow decelRate continuously, which gives a natural slide =====
                float speed = currentSpeed - decelRate * stepDt;
                speed = Mathf.Max(0f, speed);
                if (speed <= 0.05f) isStopped = true;
                return speed;
            }
            else
            {
                if (isStopped)
                {
                    reactionTimer += stepDt;
                    if (reactionTimer >= reactionDelay)
                    {
                        isStopped = false;
                    }
                    else
                    {
                        return 0f;
                    }
                }
                return Mathf.MoveTowards(currentSpeed, approachSpeed, acceleration * stepDt);
            }
        }
        else
        {
            isStopped = false;
            reactionTimer = 0f;
            return approachSpeed;
        }
    }

    // ===== Fastest speed that still keeps the car inside the intended turning circle =====
    // Turning radius R = v / omega, where omega is the steering rate.
    // To hold the configured R, v must be capped at omega x R.
    // Without this a car at 15 m/s sweeps a turn wider than 25 m and leaves its lane.
    float TurnSpeedLimit()
    {
        if (pathWaypoints == null || pathWaypoints.Length == 0) return Mathf.Infinity;
        if (reachedEndOfPath) return Mathf.Infinity;
        if (stopLineTarget != null && !hasCleared) return Mathf.Infinity;  // before the stop line, drive normally
        if (waypointIndex >= pathWaypoints.Length) return Mathf.Infinity;

        Transform wp = pathWaypoints[waypointIndex];
        if (wp == null) return Mathf.Infinity;

        Vector3 wpPos = CurrentWaypointTarget();
        Vector3 toTarget = wpPos - transform.position;
        toTarget.y = 0f;
        if (toTarget.magnitude < 0.5f) return Mathf.Infinity;

        float omega = steerRate * Mathf.Deg2Rad;            // radians per second
        if (omega <= 0.001f) return Mathf.Infinity;
        float cornerSpeed = Mathf.Max(minTurnSpeed, omega * turnRadius);

        // 1) currently turning
        float angle = Vector3.Angle(transform.forward, toTarget);
        if (angle >= turnSlowAngle) return cornerSpeed;

        // 2) look ahead to the next waypoint - if the next section turns sharply and is close, slow now.
        //    This is essential: without it a car enters the curve at the full 15 m/s and sweeps wide
        //    into the neighbouring lane before it can slow down.
        // Distance needed to brake from the current v down to cornerSpeed at this profile's decelRate.
        // A constant will not do, because a Cautious driver brakes seven times more gently than an
        //    Aggressive one (decelRate 3 vs 22); a fixed value would send careful drivers in too fast.
        float brakeDist = currentSpeed > cornerSpeed
            ? (currentSpeed * currentSpeed - cornerSpeed * cornerSpeed) / (2f * Mathf.Max(0.1f, decelRate))
            : 0f;

        if (waypointIndex + 1 < pathWaypoints.Length
            && pathWaypoints[waypointIndex + 1] != null
            && toTarget.magnitude <= brakeDist + turnBrakeMargin)
        {
            Vector3 nextSeg = pathWaypoints[waypointIndex + 1].position - wpPos;
            nextSeg.y = 0f;
            if (nextSeg.magnitude > 0.1f && Vector3.Angle(toTarget, nextSeg) >= turnSlowAngle)
                return cornerSpeed;
        }

        return Mathf.Infinity;   // straight ahead, full speed is fine
    }

    void DecideSpeed()
    {
        float desiredSpeed = ComputeDesiredSpeed();

        // ---- slow down before the curve ----
        float turnCap = TurnSpeedLimit();
        if (desiredSpeed > turnCap)
        {
        // Ease down at decelRate rather than cutting speed instantly, so it looks natural
            float eased = Mathf.Max(turnCap, currentSpeed - decelRate * stepDt);
            desiredSpeed = Mathf.Min(desiredSpeed, eased);
        }

        float gapAhead;
        CarAgent carAhead = GetCarAhead(out gapAhead);

        // ===== Startup wave: a follower waits to see the car ahead move, not for the light =====
        // In reality a queue does not launch as one block. The third driver does not move on green,
        // but when the second car moves, so the launch wave travels backwards car by car.
        //
        // This used to time reactionDelay from the green signal, which every driver receives at once.
        // Worse, a car further from the line than its braking distance never entered that branch at all
        // (ComputeDesiredSpeed's else branch sets isStopped = false and asks for approachSpeed),
        // leaving only car-following to hold it, so the queue discharged as a single block.
        // HeadwayTest measured 0.72 s/veh against the 2.31 s/veh measured from the real clip.
        if (carAhead != null && currentSpeed <= QUEUE_SPEED_EPS)
        {
            if (carAhead.currentSpeed > LEADER_MOVING_SPEED) startupTimer += stepDt;
            else startupTimer = 0f;                      // the car ahead has not moved; do not start counting

            if (startupTimer < reactionDelay)
            {
                isStopped = true;
                currentSpeed = 0f;
                return;
            }
        }
        else if (currentSpeed > QUEUE_SPEED_EPS)
        {
            startupTimer = 0f;                            // already moving; nothing to wait for
        }

        if (carAhead != null)
        {
            float leadSpeed = carAhead.currentSpeed;
            float cushion = gapAhead - minGap;

            if (cushion <= 0f)
            {
                // ===== Emergency braking: harder than normal but not an instant snap, so it slides a little =====
                float emergencySpeed = currentSpeed - (decelRate * emergencyDecelMultiplier) * stepDt;
                emergencySpeed = Mathf.Max(0f, emergencySpeed);
                desiredSpeed = Mathf.Min(desiredSpeed, emergencySpeed);
                if (emergencySpeed <= 0.1f) isStopped = true;
            }
            else
            {
                float maxSafeSpeed = Mathf.Sqrt(Mathf.Max(0f, leadSpeed * leadSpeed + 2f * decelRate * cushion));
                desiredSpeed = Mathf.Min(desiredSpeed, maxSafeSpeed);

                // ===== The gap has to grow with speed, not stay at a constant 2 m =====
                // maxSafeSpeed above only asks "could I stop if the car ahead stopped dead", and with
                // decelRate of 9-22 m/s^2 that happily allows 2 m at 15 m/s = 0.4 s headway, which is impossible.
                // Real drivers keep a time gap: gap = minGap + T x v.
                // Inverted into a speed cap: v <= (gap - minGap) / T.
                if (desiredTimeHeadway > 0.01f)
                    desiredSpeed = Mathf.Min(desiredSpeed, cushion / desiredTimeHeadway);
            }
        }

        currentSpeed = desiredSpeed;
    }

    CarAgent GetCarAhead(out float gap)
    {
        CarAgent closest = null;
        float closestGap = Mathf.Infinity;
        Vector3 frontPos = GetFrontPosition();

        // Cars in other lanes are never skipped any more. The old version let a car drawn to pass drive
        //    straight through a stopped one (visibly overlapping). Passing is now done by genuinely
        //    offsetting the position and letting the lateralDist test below filter on real distance.
        float maxLook = LookAheadLimit();

        foreach (CarAgent other in allCars)
        {
            if (other == this) continue;

            Vector3 toOther = other.transform.position - frontPos;
            float forwardDist = Vector3.Dot(toOther, transform.forward);
            if (forwardDist <= 0f) continue;
            if (forwardDist > maxLook) continue;

            float lateralDist = Vector3.Cross(transform.forward, toOther).magnitude;
            if (lateralDist > lookAheadAngle) continue;

            float g = forwardDist - (other.carLength / 2f);
            if (g < closestGap)
            {
                closestGap = g;
                closest = other;
            }
        }
        gap = closestGap;
        return closest;
    }

    /// <summary>
    /// Target for the current waypoint - normally the waypoint itself. The exception is a car using the
    /// shoulder to pass a queue, while approaching the first waypoint and before the turn begins: it aims
    /// at a point offset by bypassLateralOffset so it really does travel on the shoulder. At the next
    /// waypoint the turn starts and the normal path resumes, merging it back into the lane automatically.
    /// </summary>
    Vector3 CurrentWaypointTarget()
    {
        Vector3 p = pathWaypoints[waypointIndex].position;
        if (waypointIndex == 0 && canBypassCrossLaneBlocker) p += bypassLateralOffset;
        return p;
    }

    /// <summary>
    /// How far ahead the car may look for another vehicle.
    ///
    /// This was once a serious bug: the old version looked infinitely far along the straight ahead of
    /// the car. But a turning lane (Ax) veers away before reaching the car it can see. Ax runs at z=6.5
    /// heading +X, and its straight line shot across the junction to a CA vehicle merging into z=6.5 on
    /// the east side, 40-90 m away, even though Ax turns left at x=-11 and could never reach it.
    /// The result was Ax braking and stopping far short of the junction for no reason.
    ///
    /// There are now two limits: (1) the normal look-ahead cap, and (2) if the path turns past the current
    /// waypoint, the car can only see as far as that waypoint - anything beyond the turn is irrelevant.
    /// (A genuinely blocking queue is always before the turn, so nothing is missed.)
    /// </summary>
    float LookAheadLimit()
    {
        float limit = lookAheadDistance;

        if (pathWaypoints == null || pathWaypoints.Length == 0) return limit;
        if (reachedEndOfPath) return limit;
        // There is deliberately no "not yet at the stop line" guard here. Even before the stop line, a car in
        //    a turning lane knows it veers off at the first waypoint - this was the "Ax stops far short" bug.
        if (waypointIndex >= pathWaypoints.Length) return limit;
        if (pathWaypoints[waypointIndex] == null) return limit;
        if (waypointIndex + 1 >= pathWaypoints.Length) return limit;
        if (pathWaypoints[waypointIndex + 1] == null) return limit;

        Vector3 wpPos = CurrentWaypointTarget();
        Vector3 toWp = wpPos - GetFrontPosition();
        toWp.y = 0f;

        float along = Vector3.Dot(toWp, transform.forward);
        if (along <= 0f) return limit;

        // Only clip the view for lanes that genuinely turn away (Ax/Bx/CB/CA), never for one that merely
        //    changes lane (A2), which continues straight - clipping there would hide the queue beyond and
        //    cause a rear-end collision. Decided by how far the end of the path deviates from the heading.
        if (!PathTurnsAway()) return limit;

        Vector3 nextSeg = pathWaypoints[waypointIndex + 1].position - wpPos;
        nextSeg.y = 0f;
        if (nextSeg.magnitude > 0.1f && toWp.magnitude > 0.1f
            && Vector3.Angle(toWp, nextSeg) >= turnSlowAngle)
        {
            return Mathf.Min(limit, along + waypointReachRadius);
        }
        return limit;
    }

    /// <summary>
    /// Does the path ahead genuinely turn away, or does it merely change lane and continue straight?
    ///
    /// Judged by the direction of the last section of the path against the car's current heading:
    ///   Ax turning left into the campus -> the end heads +Z, about 90 degrees from the car's +X = turns away
    ///   A2 changing lane then going on  -> the end heads +X, the same as the car, about 0 degrees = does not
    /// </summary>
    bool PathTurnsAway()
    {
        int last = pathWaypoints.Length - 1;
        while (last > waypointIndex && pathWaypoints[last] == null) last--;
        if (last <= waypointIndex) return false;

        Vector3 endDir = pathWaypoints[last].position - pathWaypoints[waypointIndex].position;
        endDir.y = 0f;
        if (endDir.magnitude < 0.1f) return false;
        return Vector3.Angle(transform.forward, endDir) >= 45f;
    }

    /// <summary>
    /// Is any car within this radius? CarSpawner calls this before releasing a car, to avoid dropping one on
    /// top of a car stopped at the spawn point (a queue backed up that far, or two spawners sharing a lane).
    /// </summary>
    /// <summary>
    /// The car in this lane nearest a reference point - CameraDirector uses it to choose which car to follow
    /// (it passes the static camera position for that shot, so the chosen car is already in frame).
    /// </summary>
    public static CarAgent NearestInLane(string lane, Vector3 point)
    {
        CarAgent best = null;
        float bestD = Mathf.Infinity;
        foreach (CarAgent c in allCars)
        {
            if (c == null || c.assignedLane != lane) continue;
            float d = Vector3.Distance(c.transform.position, point);
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    public static bool IsSpaceOccupied(Vector3 pos, float radius)
    {
        foreach (CarAgent c in allCars)
        {
            if (c == null) continue;
            if (Vector3.Distance(c.transform.position, pos) < radius) return true;
        }
        return false;
    }

    void MoveForward()
    {
        transform.Translate(Vector3.forward * currentSpeed * stepDt);
    }

    // ===== Called by TrafficLight when the signal changes =====
    public void OnLightStateChanged(TrafficLight.LightState state)
    {
        lastSeenLight = state;
        lightInitialized = true;

        if (hasCleared) return; // already past; the light no longer matters

        // ===== Yielding lane: read the other stream's signal inverted =====
        // Their yellow still means we cannot go, because their traffic has not cleared the entrance.
        // No dilemma zone here: a driver giving way does not accelerate to beat the light the way one
        // facing their own amber does. If the car is too close to stop, ComputeDesiredSpeed lets it slide
        // past and counts it as hasCleared on its own - realistic, and nothing extra to write.
        if (lightIsInverted)
        {
            lightIsGreen = (state == TrafficLight.LightState.Red);
            return;
        }

        if (state == TrafficLight.LightState.Green)
        {
            lightIsGreen = true;
        }
        else if (state == TrafficLight.LightState.Yellow)
        {
            // ===== Dilemma zone: go or stop, decided the moment the light turns amber =====
            if (!committedThrough && stopLineTarget != null)
            {
                float requiredStop = GetRequiredBrakeDistance();
                float dilemmaThreshold = requiredStop * dilemmaZoneFactor;

                if (signedDistToStopPoint <= dilemmaThreshold)
                {
                    committedThrough = true;  // too close to stop safely for this profile, so carry on through
                }
            }
            lightIsGreen = committedThrough;
        }
        else // Red
        {
            if (!committedThrough)
            {
                lightIsGreen = false;
            }
            // Once committedThrough is set, a red is ignored too, because the car committed at amber
        }
    }
}