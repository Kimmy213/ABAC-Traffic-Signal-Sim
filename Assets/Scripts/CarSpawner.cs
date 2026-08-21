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

    [Header("Rotation (ให้รถหันหน้าถูกทิศ)")]
    public float spawnRotationY = 0f;  // ตั้งตามทิศทางแต่ละเลน

    [Header("Turning Path (เว้นว่าง = วิ่งตรง)")]
    [Tooltip("ลาก Empty GameObject ที่วางไว้ตามแนวเลี้ยว เรียงตามลำดับที่รถต้องวิ่งผ่าน")]
    public Transform[] pathWaypoints;
    [Tooltip("อัตราการหมุนพวงมาลัย องศา/วินาที")]
    public float steerRate = 55f;
    [Tooltip("รัศมีวงเลี้ยวเป้าหมาย (เมตร) — ใช้จำกัดความเร็วตอนเลี้ยว ยิ่งเล็กยิ่งเลี้ยวแคบแต่ต้องช้าลง")]
    public float turnRadius = 7f;

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

    public void SetSeed(int seed)
    {
        rng = new System.Random(seed);
    }

    public void ResetSpawnCount()
    {
        totalSpawned = 0;
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
    }
    void Start()
    {
        if (spawnStarted) return;   // Control Panel เริ่มให้แล้ว ไม่ต้องเริ่มซ้ำ
        spawnStarted = true;
        StartCoroutine(SpawnLoop());
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

    void SpawnCar(CarAgent.DriverType driver)
    {
        GameObject newCar = Instantiate(carPrefab, spawnPoint.position, Quaternion.Euler(0, spawnRotationY, 0));
        spawnedCars.Add(newCar); 
        totalSpawned++;
        CarAgent agent = newCar.GetComponent<CarAgent>();

        agent.driverType = driver;
        agent.stopLineTarget = stopLineTarget;
        agent.assignedLane = laneName;
        agent.pathWaypoints = pathWaypoints;
        agent.steerRate = steerRate;
        agent.turnRadius = turnRadius;
        agent.assignedLight = assignedLight;

        ApplyCarMaterial(newCar);

        if (assignedLight != null)
        {
            assignedLight.RegisterCar(agent);
        }

        StartCoroutine(DespawnWhenFar(newCar, agent));
    }

    // ใช้ sharedMaterial ที่สร้างไว้ล่วงหน้า 3 ตัว ไม่ใช่ .material
    // เพราะ .material จะโคลน material ใหม่ต่อรถ 1 คัน รันยาว ๆ แล้วหน่วง
    void ApplyCarMaterial(GameObject car)
    {
        if (carMaterial == null || car == null) return;
        foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
        {
            if (r != null) r.sharedMaterial = carMaterial;
        }
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