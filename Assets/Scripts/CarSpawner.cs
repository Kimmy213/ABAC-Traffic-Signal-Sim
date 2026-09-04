using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CarSpawner : MonoBehaviour
{
    
    [Header("Prefab & Spawn Point")]
    public GameObject carPrefab;
    public Transform spawnPoint;
    public Transform stopLineTarget;   // stop line ของเลนนี้ (เว้นว่างได้ถ้าเป็นเลนไหลอิสระ Ax/CA)
    public TrafficLight assignedLight; // ไฟที่ควบคุมเลนนี้ (เว้นว่างได้ถ้าเป็นเลนไหลอิสระ)
    [Tooltip("true = เลน 'ให้ทาง' — assignedLight เป็นไฟของอีกกระแส อ่านแบบกลับด้าน (เขา 'ไม่แดง' = เราหยุด)\n" +
             "ใช้กับ Ax ที่เลี้ยวเข้ามอไม่ได้ตอน Bx เขียว ดูรายละเอียดที่ CarAgent.lightIsInverted")]
    public bool lightIsInverted = false;

    [Header("Rotation (ให้รถหันหน้าถูกทิศ)")]
    public float spawnRotationY = 0f;  // ตั้งตามทิศทางแต่ละเลน

    [Header("Turning Path (เว้นว่าง = วิ่งตรง)")]
    [Tooltip("ลาก Empty GameObject ที่วางไว้ตามแนวเลี้ยว เรียงตามลำดับที่รถต้องวิ่งผ่าน")]
    public Transform[] pathWaypoints;
    [Tooltip("อัตราการหมุนพวงมาลัย องศา/วินาที")]
    public float steerRate = 55f;
    [Tooltip("รัศมีวงเลี้ยวเป้าหมาย (เมตร) — ใช้จำกัดความเร็วตอนเลี้ยว ยิ่งเล็กยิ่งเลี้ยวแคบแต่ต้องช้าลง")]
    public float turnRadius = 7f;
    [Tooltip("อนุญาตให้เปลี่ยนเลน/เลี้ยวก่อนถึงเส้นหยุด (ใช้กับ A2 ที่ต้อง merge เข้าเลนแชร์) — ดู CarAgent.steerBeforeStopLine")]
    public bool steerBeforeStopLine = false;
    [Tooltip("ระยะที่เริ่มเปลี่ยนเลน (เมตร) 0 = เปลี่ยนตั้งแต่ spawn — ดู CarAgent.steerStartDistance")]
    public float steerStartDistance = 0f;

    [Header("Appearance")]
    [Tooltip("วัสดุของรถเลนนี้ — ใช้แยกสีตามกลุ่มเลนใหญ่ A / B / C (เว้นว่าง = ใช้สีเดิมของ prefab)")]
    public Material carMaterial;

    [Header("Lane Identity")]
    public string laneName = "A";   // ต้องตรงกับ ROIZone.laneName และ IntersectionConfig (A/Ax/B/Bx/CB/CA)

    [Header("Arrival Mode")]
    [Tooltip("Poisson = สุ่มตามอัตรา / Scripted = ปล่อยตามตารางที่กำหนด (ผลเหมือนกันเป๊ะทุกครั้ง)")]
    public ArrivalMode arrivalMode = ArrivalMode.Poisson;

    [Header("Arrival Rate (ใช้เมื่อ mode = Poisson)")]
    public float meanArrivalInterval = 4f;  // วินาทีเฉลี่ยระหว่างรถแต่ละคัน

    [Header("Schedule (ใช้เมื่อ mode = Scripted)")]
    [Tooltip("ปล่อยรถกี่คัน ตอนไหน — ตั้งจาก Control Panel หรือใส่เองก็ได้")]
    public List<ScheduledBurst> schedule = new List<ScheduledBurst>();

    [Header("Driver Profile Distribution (%)")]
    [Range(0, 100)] public float aggressivePercent = 25f;
    [Range(0, 100)] public float normalPercent = 60f;
    // cautious = 100 - aggressive - normal (คำนวณอัตโนมัติ)

    [Header("Shared Lane Bypass (ตั้งค่าเฉพาะเลนที่แชร์ทางกายภาพกับเลนอื่น เช่น Ax)")]
    [Tooltip("โอกาสที่รถคันนี้จะ 'แซง' รถอีกเลนที่จอดขวางผ่านไหล่ทาง/เลนฉุกเฉินได้ (0 = ไม่มีทางแซง บล็อก 100% เหมือนเดิม)\n" +
             "⚠️ ปัจจุบัน 'ปิดอยู่' (ตั้งเป็น 0 ใน ABACSceneBuilder) ด้วย 2 เหตุผล:\n" +
             "1) ขาเข้าฝั่งตะวันออกมี 2 แถวเท่านั้น (A เลนใน / Ax+A2 เลนนอก) ไม่มีไหล่ทางให้แซงเป็นแถวที่ 3\n" +
             "2) ค่า A2_SHARE = 18% ที่ใช้อยู่ Itim back-calculate มาจากอัตราบล็อกจริง 40-60% โดยใช้โมเดล " +
             "car-following ล้วน ๆ อยู่แล้ว ถ้าใส่การแซงทับเข้าไปอีกจะเป็นการหักผลเดียวกันสองรอบ\n\n" +
             "ถ้าจะเปิดกลับมาใช้ **ต้องตั้ง bypassLateralOffset ให้มากกว่า CarAgent.lookAheadAngle เสมอ** " +
             "ไม่งั้นรถจะวิ่งทะลุผ่านกัน (Test6 บังคับกฎข้อนี้ไว้แล้ว) — สุ่มครั้งเดียวต่อคันตอน spawn ไม่ใช่ทุกเฟรม")]
    [Range(0f, 1f)] public float laneBypassChance = 0f;

    [Tooltip("ระยะเยื้องออกไหล่ทาง (world space) ของรถที่สุ่มได้ว่าจะแซงคิว — ส่งต่อให้ CarAgent\n" +
             "ต้องมากกว่า lookAheadAngle ของ CarAgent (1.5 ม.) พอสมควร ไม่งั้นรถจะยังมองเห็นคิวแล้วเบรกตาม " +
             "แต่ก็ต้องไม่กว้างจนหลุดขอบถนน (ถนนกว้าง 18 ม. ขอบเหนืออยู่ที่ z=9.88 เลน Ax อยู่ z=6.5)")]
    public Vector3 bypassLateralOffset = Vector3.zero;

    [Header("Spawn Safety (กันปล่อยรถทับคันที่จอดอยู่)")]
    [Tooltip("ถ้ามีรถคันไหนอยู่ในรัศมีนี้รอบจุด spawn จะยังไม่ปล่อยรถคันใหม่ แต่พักไว้ในคิวก่อน\n" +
             "ควร >= ความยาวรถ (4 ม.) + ระยะเผื่อ — เกิดจริงเมื่อคิวยาวถอยมาถึงจุด spawn " +
             "หรือเมื่อสอง spawner ใช้เลนกายภาพเดียวกัน (A2 กับ Ax อยู่เลน z=6.5 เหมือนกัน)")]
    public float spawnClearRadius = 6f;

    [Tooltip("ลองปล่อยรถที่ค้างคิวอยู่ทุกกี่วินาที")]
    public float pendingRetryInterval = 0.25f;

    [Header("Cleanup")]
    public float despawnDistance = 80f; // ระยะจาก spawn point ที่จะลบรถทิ้ง
    // ===== เพิ่มสำหรับ Batch Run =====
    private List<GameObject> spawnedCars = new List<GameObject>();
    // ===== นับจำนวนรถที่ spawn ทั้งหมด (ไม่ reset เองอัตโนมัติ) =====
    public int totalSpawned = 0;

    // ===== RNG ประจำเลน =====
    // ใช้ System.Random แยกต่อ spawner แทน UnityEngine.Random ที่เป็น global
    // เพราะถ้าใช้ global ลำดับการสุ่มจะสลับกันไปมาระหว่าง 6 spawner ตามจังหวะเฟรม
    // ทำให้ Fixed กับ Adaptive เจอ "ชุดรถ" ไม่เหมือนกันเป๊ะ → paired t-test อ่อนลง
    private System.Random rng = new System.Random(12345);

    // ===== คิวรถที่มาถึงแล้วแต่จุด spawn ยังไม่ว่าง =====
    // ⚠️ ห้าม "ทิ้ง" รถที่ปล่อยไม่ได้ เพราะจะกลายเป็นการลด demand ลงเงียบ ๆ ตอนที่รถติดที่สุดพอดี
    //    (ยิ่งติด ยิ่งทิ้งเยอะ = ผลออกมาดูดีเกินจริง) จึงพักไว้ในคิวแล้วปล่อยทีหลังแทน
    private struct PendingArrival
    {
        public CarAgent.DriverType driver;
        public bool bypass;
    }
    private Queue<PendingArrival> pending = new Queue<PendingArrival>();

    /// <summary>จำนวนรถที่มาถึงแล้วแต่ยังลงถนนไม่ได้ (จุด spawn ไม่ว่าง)</summary>
    public int PendingSpawnCount => pending.Count;
    /// <summary>นับจำนวนครั้งที่รถมาถึงแล้วต้องรอคิว — ถ้าค่านี้สูงแปลว่า spawn point อยู่ใกล้แยกเกินไป</summary>
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

    /// <summary>สลับไปโหมดสุ่ม Poisson</summary>
    public void UsePoisson(float meanInterval)
    {
        arrivalMode = ArrivalMode.Poisson;
        meanArrivalInterval = meanInterval;
    }

    /// <summary>สลับไปโหมดปล่อยตามตาราง (deterministic)</summary>
    public void UseSchedule(List<ScheduledBurst> bursts)
    {
        arrivalMode = ArrivalMode.Scripted;
        schedule = bursts != null ? new List<ScheduledBurst>(bursts) : new List<ScheduledBurst>();
        schedule.Sort((a, b) => a.atTime.CompareTo(b.atTime));
    }

    /// <summary>จำนวนรถทั้งหมดในตาราง (โหมด Scripted)</summary>
    public int ScheduledCarCount
    {
        get
        {
            int n = 0;
            foreach (var b in schedule) if (b != null) n += b.carCount;
            return n;
        }
    }

    // ลำดับ Start() ระหว่าง MonoBehaviour ไม่การันตี ถ้า Control Panel เรียก RestartSpawning()
    // ก่อนที่ CarSpawner.Start() จะทำงาน จะได้ SpawnLoop ซ้อนกัน 2 ตัว = รถออกมาเป็นสองเท่า
    private bool spawnStarted = false;

    /// <summary>ล้างรถเก่าแล้วเริ่ม spawn loop ใหม่ตั้งแต่ต้น</summary>
    public void RestartSpawning()
    {
        spawnStarted = true;
        StopAllCoroutines();   // หยุดทั้ง SpawnLoop และ DespawnWhenFar ที่ค้างอยู่
        ClearAllCars();
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
        if (spawnStarted) return;   // Control Panel เริ่มให้แล้ว ไม่ต้องเริ่มซ้ำ
        spawnStarted = true;
        StartCoroutine(SpawnLoop());
        StartCoroutine(PendingReleaseLoop());
    }

    /// <summary>คอยปล่อยรถที่ค้างคิวอยู่ทันทีที่จุด spawn ว่าง</summary>
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

    // ===== ปล่อยตามตารางที่กำหนด — ผลเหมือนกันเป๊ะทุกครั้ง =====
    IEnumerator ScriptedSpawnLoop()
    {
        float elapsed = 0f;

        for (int i = 0; i < schedule.Count; i++)
        {
            ScheduledBurst burst = schedule[i];
            if (burst == null || burst.carCount <= 0) continue;

            // รอจนถึงเวลาเริ่ม burst นี้
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
        // ปล่อยครบตามตารางแล้ว หยุดนิ่ง ๆ ไม่ปล่อยเพิ่ม
    }

    IEnumerator PoissonSpawnLoop()
    {
        while (true)
        {
            // สุ่มเวลาห่างของรถแต่ละคัน แบบ exponential distribution
            // (จำลอง Poisson process ที่ใช้กันทั่วไปในงานจราจร)
            float u = (float)rng.NextDouble();
            float waitTime = -Mathf.Log(1f - Mathf.Clamp(u, 0f, 0.999999f)) * meanArrivalInterval;
            yield return new WaitForSeconds(waitTime);

            SpawnCar(RandomDriverType());
        }
    }

    /// <summary>
    /// "รถมาถึง" 1 คัน — สุ่มค่าที่ต้องใช้ให้ครบก่อนเสมอ แล้วค่อยดูว่าลงถนนได้เลยไหม
    ///
    /// ⚠️ ลำดับการสุ่มต้องไม่ขึ้นกับสภาพจราจรเด็ดขาด ไม่งั้น Fixed กับ Adaptive จะเจอ "ชุดรถ" คนละชุด
    ///    ทันทีที่รถติดไม่เท่ากัน → paired t-test ใช้ไม่ได้ จึงสุ่ม bypass ตรงนี้ (ตอนมาถึง)
    ///    ไม่ใช่ตอนปล่อยจริง
    /// </summary>
    void SpawnCar(CarAgent.DriverType driver)
    {
        bool bypass = laneBypassChance > 0f && rng.NextDouble() < laneBypassChance;

        pending.Enqueue(new PendingArrival { driver = driver, bypass = bypass });
        int before = pending.Count;
        TryReleasePending();
        if (pending.Count >= before) delayedArrivals++;   // ปล่อยไม่ออก ต้องรอคิว
    }

    /// <summary>จุดปล่อยรถจริง — คันที่แซงไหล่ทางจะเยื้องออกไปตั้งแต่ต้น</summary>
    Vector3 SpawnPositionFor(bool bypass)
    {
        return bypass ? spawnPoint.position + bypassLateralOffset : spawnPoint.position;
    }

    /// <summary>ปล่อยรถที่ค้างคิวออกให้มากที่สุดเท่าที่จุด spawn ว่าง (เรียงตามลำดับที่มาถึง)</summary>
    void TryReleasePending()
    {
        while (pending.Count > 0)
        {
            PendingArrival next = pending.Peek();
            Vector3 pos = SpawnPositionFor(next.bypass);
            if (CarAgent.IsSpaceOccupied(pos, spawnClearRadius)) return;   // ยังไม่ว่าง รอรอบหน้า
            pending.Dequeue();
            PlaceCar(next.driver, next.bypass, pos);
        }
    }

    void PlaceCar(CarAgent.DriverType driver, bool bypass, Vector3 pos)
    {
        GameObject newCar = Instantiate(carPrefab, pos, Quaternion.Euler(0, spawnRotationY, 0));
        spawnedCars.Add(newCar);
        totalSpawned++;
        CarAgent agent = newCar.GetComponent<CarAgent>();

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

    // ใช้ sharedMaterial ที่สร้างไว้ล่วงหน้า 3 ตัว ไม่ใช่ .material
    // เพราะ .material จะโคลน material ใหม่ต่อรถ 1 คัน รันยาว ๆ แล้วหน่วง
    //
    // ⚠️ ของเดิมทาสีทับ "ทุก Renderer ในรถ" ซึ่งใช้ได้ตอนรถเป็นกล่องชิ้นเดียว
    //    แต่พอเปลี่ยนเป็นโมเดลจริงที่มีล้อ/กระจก/ไฟหน้า/ไฟท้ายแยกชิ้น จะกลายเป็นทาสีเลนทั้งคัน
    //    ตอนนี้ทาเฉพาะช่องวัสดุของตัวถัง (CarAgent.bodyRenderer / bodyMaterialIndex)
    //    ซึ่ง ABAC ▸ Rebuild Car Prefab หาให้อัตโนมัติจากชิ้นที่มีพื้นผิวมากที่สุด
    void ApplyCarMaterial(GameObject car, CarAgent agent)
    {
        if (carMaterial == null || car == null) return;

        Renderer target = agent != null ? agent.bodyRenderer : null;
        if (target == null)
        {
            // fallback: prefab เก่าที่เป็นกล่องชิ้นเดียว ยังทาทั้งคันได้เหมือนเดิม
            Renderer r = car.GetComponent<Renderer>();
            if (r != null) r.sharedMaterial = carMaterial;
            return;
        }

        int slot = agent.bodyMaterialIndex;
        Material[] mats = target.sharedMaterials;
        if (slot < 0 || slot >= mats.Length) slot = 0;
        if (mats.Length == 0) return;
        mats[slot] = carMaterial;
        target.sharedMaterials = mats;   // ต้อง assign array กลับ ไม่งั้นไม่มีผล
    }

    /// <summary>จำนวนรถของเลนนี้ที่ยังวิ่งอยู่บนถนน</summary>
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

    /// <summary>โหมด Scripted: ปล่อยรถครบตามตารางแล้วหรือยัง (โหมด Poisson จะไม่มีวันจบ)</summary>
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
            // เลนเลี้ยวอาจวนกลับมาใกล้ spawn point ได้ จึงเช็ค "วิ่งจบเส้นทาง" ควบคู่กับระยะทาง
            bool finishedPath = agent != null && agent.reachedEndOfPath;

            if (dist >= despawnDistance || finishedPath)
            {
                if (assignedLight != null) assignedLight.UnregisterCar(agent);
                spawnedCars.Remove(car);
                Destroy(car);
                yield break;
            }
            yield return new WaitForSeconds(0.5f); // เช็คทุกครึ่งวิ ไม่ต้องเช็คทุกเฟรม (ประหยัด performance)
        }
    }

    
}