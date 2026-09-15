using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CarSpawner : MonoBehaviour
{
    
    [Header("Prefab & Spawn Point")]
    public GameObject carPrefab;
    public Transform spawnPoint;
    public Transform stopLineTarget;   // stop line for this lane (may be empty for free-flow lanes Ax/CA)
    public TrafficLight assignedLight; // signal controlling this lane (may be empty for free-flow lanes)
    [Tooltip("true = a yielding lane. assignedLight belongs to the *other* stream and is read inverted:\n" +
             "if they are not red, we stop. Used by Ax, which cannot turn in while Bx is green. See CarAgent.lightIsInverted")]
    public bool lightIsInverted = false;

    [Header("Rotation (so cars face the right way)")]
    public float spawnRotationY = 0f;  // set per lane direction

    [Header("Turning Path (empty = drive straight)")]
    [Tooltip("Empty GameObjects placed along the turn, in the order the car should pass them")]
    public Transform[] pathWaypoints;
    [Tooltip("Steering rate in degrees per second")]
    public float steerRate = 55f;
    [Tooltip("Target turning radius in metres - caps speed in the turn; smaller means tighter but slower")]
    public float turnRadius = 7f;
    [Tooltip("Allow lane changes and turns before the stop line (used by A2 merging into the shared lane) - see CarAgent.steerBeforeStopLine")]
    public bool steerBeforeStopLine = false;
    [Tooltip("Distance at which the lane change starts, in metres. 0 = from spawn - see CarAgent.steerStartDistance")]
    public float steerStartDistance = 0f;

    [Header("Appearance")]
    [Tooltip("Material for this lane's cars - distinguishes lane groups A / B / C (empty = keep the prefab colour)")]
    public Material carMaterial;

    [Header("Lane Identity")]
    public string laneName = "A";   // must match ROIZone.laneName and IntersectionConfig (A/Ax/B/Bx/CB/CA)

    [Header("Arrival Mode")]
    [Tooltip("Poisson = random at a given rate / Scripted = follow a fixed schedule (identical every run)")]
    public ArrivalMode arrivalMode = ArrivalMode.Poisson;

    [Header("Arrival Rate (used when mode = Poisson)")]
    public float meanArrivalInterval = 4f;  // mean seconds between cars

    [Header("Schedule (used when mode = Scripted)")]
    [Tooltip("How many cars and when - set from the Control Panel, or filled in by hand")]
    public List<ScheduledBurst> schedule = new List<ScheduledBurst>();

    [Header("Driver Profile Distribution (%)")]
    [Range(0, 100)] public float aggressivePercent = 25f;
    [Range(0, 100)] public float normalPercent = 60f;
    // cautious = 100 - aggressive - normal (computed automatically)

    [Header("Shared Lane Bypass (only for lanes physically sharing space with another, e.g. Ax)")]
    [Tooltip("Chance this car can pass a blocking car from the other lane using the shoulder (0 = never, blocked 100% as before).\n" +
             "Currently disabled (set to 0 in ABACSceneBuilder) for two reasons:\n" +
             "1) the eastbound approach has only two rows (A inner / Ax+A2 outer) and no shoulder for a third\n" +
             "2) the A2_SHARE value of 18% was back-calculated by Itim from a real 40-60% blocking rate using " +
             "a pure car-following model, so adding passing on top would discount the same effect twice.\n\n" +
             "If it is ever re-enabled, bypassLateralOffset must always exceed CarAgent.lookAheadAngle, " +
             "or cars drive through each other (Test6 enforces this). Drawn once per car at spawn, not per frame.")]
    [Range(0f, 1f)] public float laneBypassChance = 0f;

    [Tooltip("Lateral offset onto the shoulder (world space) for a car drawn to pass the queue - passed to CarAgent.\n" +
             "Must be comfortably larger than CarAgent.lookAheadAngle (1.5 m), or the car still sees the queue and brakes, " +
             "but not so wide it leaves the carriageway (road is 18 m, the north edge is z=9.88, lane Ax is z=6.5)")]
    public Vector3 bypassLateralOffset = Vector3.zero;

    [Header("Spawn Safety (avoids dropping a car on top of a stopped one)")]
    [Tooltip("If any car is within this radius of the spawn point, the new car waits in a queue instead.\n" +
             "Should be >= car length (4 m) plus margin. Happens when a queue backs up to the spawn point, " +
             "or when two spawners share a physical lane (A2 and Ax are both at z=6.5).")]
    public float spawnClearRadius = 6f;

    [Tooltip("How often to retry releasing cars held in the queue, in seconds")]
    public float pendingRetryInterval = 0.25f;

    [Header("Cleanup")]
    public float despawnDistance = 80f; // distance from the spawn point at which a car is removed
    // ===== Added for Batch Run =====
    private List<GameObject> spawnedCars = new List<GameObject>();
    // ===== Total cars spawned (never reset automatically) =====
    public int totalSpawned = 0;

    // ===== Per-lane RNG =====
    // A separate System.Random per spawner rather than the global UnityEngine.Random, because the
    // global one interleaves draws between the six spawners according to frame timing, so Fixed and
    // Adaptive would not see exactly the same set of vehicles and the paired t-test would weaken.
    private System.Random rng = new System.Random(12345);

    // ===== Cars that have arrived but cannot be placed because the spawn point is occupied =====
    // Never discard a car that cannot be released: that silently cuts demand exactly when traffic is
    //    heaviest (the worse the jam, the more are dropped, making results look better than they are).
    private struct PendingArrival
    {
        public CarAgent.DriverType driver;
        public bool bypass;
    }
    private Queue<PendingArrival> pending = new Queue<PendingArrival>();

    /// <summary>Cars that have arrived but cannot be placed yet (spawn point occupied)</summary>
    public int PendingSpawnCount => pending.Count;
    /// <summary>How many arrivals had to wait - a high value means the spawn point is too close to the junction</summary>
    public int delayedArrivals = 0;

    public void SetSeed(int seed)
    {
        rng = new System.Random(seed);
    }

    public void ResetSpawnCount()
    {
        totalSpawned = 0;
        delayedArrivals = 0;
    }
    public void SetArrivalRate(float meanInterval)
    {
        meanArrivalInterval = meanInterval;
    }

    /// <summary>Switch to random Poisson arrivals</summary>
    public void UsePoisson(float meanInterval)
    {
        standingQueue = 0;   // Poisson mode has no initial queue
        arrivalMode = ArrivalMode.Poisson;
        meanArrivalInterval = meanInterval;
    }

    /// <summary>Switch to the scheduled (deterministic) mode</summary>
    /// <summary>
    /// Cars to be placed already standing at the start of the run - set from a TestCase via ApplyTestCase.
    /// Placed in RestartSpawning() before the schedule begins.
    /// </summary>
    private int standingQueue = 0;
    public void SetStandingQueue(int n) { standingQueue = Mathf.Max(0, n); }

    public void UseSchedule(List<ScheduledBurst> bursts)
    {
        arrivalMode = ArrivalMode.Scripted;
        schedule = bursts != null ? new List<ScheduledBurst>(bursts) : new List<ScheduledBurst>();
        schedule.Sort((a, b) => a.atTime.CompareTo(b.atTime));
    }

    /// <summary>Total cars in the schedule (Scripted mode)</summary>
    public int ScheduledCarCount
    {
        get
        {
            int n = 0;
            foreach (var b in schedule) if (b != null) n += b.carCount;
            return n;
        }
    }

    // Start() ordering between MonoBehaviours is not guaranteed. If the Control Panel calls
    // RestartSpawning() before CarSpawner.Start() runs, two SpawnLoops stack up and twice as many cars appear.
    private bool spawnStarted = false;

    /// <summary>Clears old cars and restarts the spawn loop from the beginning</summary>
    public void RestartSpawning()
    {
        spawnStarted = true;
        StopAllCoroutines();   // stops both SpawnLoop and any pending DespawnWhenFar
        ClearAllCars();

        // The initial queue must always be placed before StartCoroutine: SpawnLoop runs its body
        // immediately up to the first yield, so a burst at atTime = 0 releases a car right there,
        // and that car would end up in front of the queue we are about to place, reversing the order.
        if (standingQueue > 0) PlaceStandingQueue(standingQueue);

        StartCoroutine(SpawnLoop());
        StartCoroutine(PendingReleaseLoop());
    }

    public void ClearAllCars()
    {
        foreach (GameObject car in spawnedCars)
        {
            if (car != null)
            {
                CarAgent agent = car.GetComponent<CarAgent>();
                if (agent != null && assignedLight != null) assignedLight.UnregisterCar(agent);
                Destroy(car);
            }
        }
        spawnedCars.Clear();
        pending.Clear();
    }
    void Start()
    {
        if (spawnStarted) return;   // the Control Panel already started it; do not start twice
        spawnStarted = true;
        StartCoroutine(SpawnLoop());
        StartCoroutine(PendingReleaseLoop());
    }

    /// <summary>Releases queued cars as soon as the spawn point frees up</summary>
    IEnumerator PendingReleaseLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(Mathf.Max(0.05f, pendingRetryInterval));
            if (pending.Count > 0) TryReleasePending();
        }
    }

    IEnumerator SpawnLoop()
    {
        if (arrivalMode == ArrivalMode.Scripted)
            yield return StartCoroutine(ScriptedSpawnLoop());
        else
            yield return StartCoroutine(PoissonSpawnLoop());
    }

    // ===== Scheduled release - identical every run =====
    IEnumerator ScriptedSpawnLoop()
    {
        float elapsed = 0f;

        for (int i = 0; i < schedule.Count; i++)
        {
            ScheduledBurst burst = schedule[i];
            if (burst == null || burst.carCount <= 0) continue;

            // wait until this burst is due
            if (burst.atTime > elapsed)
            {
                float wait = burst.atTime - elapsed;
                yield return new WaitForSeconds(wait);
                elapsed += wait;
            }

            for (int c = 0; c < burst.carCount; c++)
            {
                SpawnCar(burst.randomDriver ? RandomDriverType() : burst.driverType);

                if (c < burst.carCount - 1 && burst.spacing > 0f)
                {
                    yield return new WaitForSeconds(burst.spacing);
                    elapsed += burst.spacing;
                }
            }
        }
        // schedule exhausted; stay idle and release nothing further
    }

    IEnumerator PoissonSpawnLoop()
    {
        while (true)
        {
            // Draw the gap between cars from an exponential distribution
            // (the usual Poisson process used in traffic work)
            float u = (float)rng.NextDouble();
            float waitTime = -Mathf.Log(1f - Mathf.Clamp(u, 0f, 0.999999f)) * meanArrivalInterval;
            yield return new WaitForSeconds(waitTime);

            SpawnCar(RandomDriverType());
        }
    }

    /// <summary>
    /// One vehicle arrives. Always draw every random value first, then decide whether it can be placed.
    ///
    /// The order of draws must not depend on traffic conditions, or Fixed and Adaptive see different
    ///    vehicle sets as soon as congestion differs, and the paired t-test becomes invalid. That is why
    ///    the bypass draw happens here, on arrival, rather than at the moment of release.
    /// </summary>
    void SpawnCar(CarAgent.DriverType driver)
    {
        bool bypass = laneBypassChance > 0f && rng.NextDouble() < laneBypassChance;

        pending.Enqueue(new PendingArrival { driver = driver, bypass = bypass });
        int before = pending.Count;
        TryReleasePending();
        if (pending.Count >= before) delayedArrivals++;   // could not be released; had to queue
    }

    /// <summary>The actual release point - a car drawn to use the shoulder is offset from the start</summary>
    Vector3 SpawnPositionFor(bool bypass)
    {
        return bypass ? spawnPoint.position + bypassLateralOffset : spawnPoint.position;
    }

    /// <summary>Releases as many queued cars as the spawn point allows, in arrival order</summary>
    void TryReleasePending()
    {
        while (pending.Count > 0)
        {
            PendingArrival next = pending.Peek();
            Vector3 pos = SpawnPositionFor(next.bypass);
            if (CarAgent.IsSpaceOccupied(pos, spawnClearRadius)) return;   // still blocked, try next time
            pending.Dequeue();
            PlaceCar(next.driver, next.bypass, pos);
        }
    }

    /// <summary>
    /// Places count cars standing nose-to-tail at the stop line, as if the red had been on for a while.
    ///
    /// Spacing uses carLength + minGap, the distance at which car-following settles. Any closer and the
    /// cars back away from each other on the first frame; any further and they creep forward.
    ///
    /// Free-flow lanes (Ax / CA) have no stopLineTarget, so they are placed at the spawn point and drive in.
    /// That is less exact, but those lanes have no signal to wait at, so the queue is not what the case needs.
    /// </summary>
    public int PlaceStandingQueue(int count)
    {
        if (count <= 0 || carPrefab == null || spawnPoint == null) return 0;

        // Direction this car faces - rotation about Y only, matching what PlaceCar uses at Instantiate
        float yaw = spawnRotationY * Mathf.Deg2Rad;
        Vector3 fwd = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));

        CarAgent proto = carPrefab.GetComponent<CarAgent>();
        float len = proto != null ? proto.carLength : 4f;
        float gap = proto != null ? proto.minGap    : 2f;
        float pitch = len + gap;

        // Head of queue: the nose stops REST_FRONT_MARGIN metres before the line, so the centre sits
        // half a car further back. The nose must not touch the line exactly, because a Cautious driver
        // has stopOvershoot = -1 m, which would make signedDistToStopPoint -1 on the first frame,
        // below the -0.3 threshold, setting hasCleared = true and driving straight through the red.
        Vector3 head = stopLineTarget != null
                     ? stopLineTarget.position - fwd * (len * 0.5f + CarAgent.REST_FRONT_MARGIN)
                     : spawnPoint.position;

        if (stopLineTarget == null)
            Debug.LogWarning($"[CarSpawner:{laneName}] no stopLineTarget - placing the initial queue at the spawn point instead");

        for (int i = 0; i < count; i++)
        {
            Vector3 pos = head - fwd * (i * pitch);
            pos.y = spawnPoint.position.y;      // keep road level; ignore the stop line transform's y
            PlaceCar(RandomDriverType(), false, pos, atRest: true);
        }
        return count;
    }

    void PlaceCar(CarAgent.DriverType driver, bool bypass, Vector3 pos, bool atRest = false)
    {
        GameObject newCar = Instantiate(carPrefab, pos, Quaternion.Euler(0, spawnRotationY, 0));
        spawnedCars.Add(newCar);
        totalSpawned++;
        CarAgent agent = newCar.GetComponent<CarAgent>();

        // Must be set before CarAgent.Start(), which Unity calls at the end of this frame, not at Instantiate
        agent.startAtRest = atRest;
        agent.driverType = driver;
        agent.stopLineTarget = stopLineTarget;
        agent.assignedLane = laneName;
        agent.pathWaypoints = pathWaypoints;
        agent.steerRate = steerRate;
        agent.turnRadius = turnRadius;
        agent.steerBeforeStopLine = steerBeforeStopLine;
        agent.steerStartDistance = steerStartDistance;
        agent.assignedLight = assignedLight;
        agent.lightIsInverted = lightIsInverted;
        agent.canBypassCrossLaneBlocker = bypass;
        agent.bypassLateralOffset = bypassLateralOffset;

        ApplyCarMaterial(newCar, agent);

        if (assignedLight != null)
        {
            assignedLight.RegisterCar(agent);
        }

        StartCoroutine(DespawnWhenFar(newCar, agent));
    }

    // Uses three pre-built sharedMaterials rather than .material, because .material clones a new
    // material per car and a long run then stutters.
    //
    // The old version painted every Renderer in the car, which worked when a car was one box,
    //    but with a real model whose wheels, glass and lights are separate parts it painted the lot.
    //    It now paints only the body material slot (CarAgent.bodyRenderer / bodyMaterialIndex),
    //    which ABAC > Rebuild Car Prefab finds automatically as the part with the largest surface.
    void ApplyCarMaterial(GameObject car, CarAgent agent)
    {
        if (carMaterial == null || car == null) return;

        Renderer target = agent != null ? agent.bodyRenderer : null;
        if (target == null)
        {
            // fallback: the old single-box prefab, which can still be painted whole
            Renderer r = car.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = carMaterial;
            return;
        }

        int slot = agent.bodyMaterialIndex;
        Material[] mats = target.sharedMaterials;
        if (slot < 0 || slot >= mats.Length) slot = 0;
        if (mats.Length == 0) return;
        mats[slot] = carMaterial;
        target.sharedMaterials = mats;   // the array has to be assigned back, or nothing changes
    }

    /// <summary>Cars from this lane still on the road</summary>
    public int ActiveCarCount
    {
        get
        {
            int n = 0;
            for (int i = spawnedCars.Count - 1; i >= 0; i--)
            {
                if (spawnedCars[i] == null) spawnedCars.RemoveAt(i);
                else n++;
            }
            return n;
        }
    }

    /// <summary>Scripted mode: has the whole schedule been released? (Poisson mode never finishes)</summary>
    public bool AllScheduledSpawned
    {
        get
        {
            if (arrivalMode != ArrivalMode.Scripted) return false;
            return totalSpawned >= ScheduledCarCount;
        }
    }

    CarAgent.DriverType RandomDriverType()
    {
        float roll = (float)rng.NextDouble() * 100f;
        if (roll < aggressivePercent) return CarAgent.DriverType.Aggressive;
        if (roll < aggressivePercent + normalPercent) return CarAgent.DriverType.Normal;
        return CarAgent.DriverType.Cautious;
    }

    IEnumerator DespawnWhenFar(GameObject car, CarAgent agent)
    {
        while (car != null)
        {
            float dist = Vector3.Distance(spawnPoint.position, car.transform.position);
            // A turning lane can loop back near the spawn point, so check "finished its path" as well as distance
            bool finishedPath = agent != null && agent.reachedEndOfPath;

            if (dist >= despawnDistance || finishedPath)
            {
                if (assignedLight != null) assignedLight.UnregisterCar(agent);
                spawnedCars.Remove(car);
                Destroy(car);
                yield break;
            }
            yield return new WaitForSeconds(0.5f); // twice a second is enough; no need to check every frame
        }
    }

    
}