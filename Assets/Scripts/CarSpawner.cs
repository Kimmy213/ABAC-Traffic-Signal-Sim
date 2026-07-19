using UnityEngine;
using System.Collections;
using System.Collections.Generic;

public class CarSpawner : MonoBehaviour
{
    
    [Header("Prefab & Spawn Point")]
    public GameObject carPrefab;
    public Transform spawnPoint;
    public Transform stopLineTarget;   // stop line ของเลนนี้
    public TrafficLight assignedLight; // ไฟที่ควบคุมเลนนี้

    [Header("Rotation (ให้รถหันหน้าถูกทิศ)")]
    public float spawnRotationY = 0f;  // ตั้งตาม fastwork ทิศทางแต่ละเลน
    
    [Header("Lane Identity")]
    public string laneName = "A";   // ตั้งใน Inspector ให้ตรงกับ Spawner แต่ละตัว (A/B/C)

    [Header("Arrival Rate")]
    public float meanArrivalInterval = 4f;  // วินาทีเฉลี่ยระหว่างรถแต่ละคัน

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

    public void ResetSpawnCount()
    {
        totalSpawned = 0;
    }
    public void SetArrivalRate(float meanInterval)
    {
        meanArrivalInterval = meanInterval;
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
        StartCoroutine(SpawnLoop());
    }

    IEnumerator SpawnLoop()
    {
        while (true)
        {
            // สุ่มเวลาห่างของรถแต่ละคัน แบบ exponential distribution
            // (จำลอง Poisson process ที่ใช้กันทั่วไปในงานจราจร)
            float waitTime = -Mathf.Log(1f - Random.value) * meanArrivalInterval;
            yield return new WaitForSeconds(waitTime);

            SpawnCar();
        }
    }

    void SpawnCar()
    {
        GameObject newCar = Instantiate(carPrefab, spawnPoint.position, Quaternion.Euler(0, spawnRotationY, 0));
        spawnedCars.Add(newCar); 
        totalSpawned++;
        CarAgent agent = newCar.GetComponent<CarAgent>();

        agent.driverType = RandomDriverType();
        agent.stopLineTarget = stopLineTarget;
        agent.assignedLane = laneName;   // ← เพิ่มบรรทัดนี้

        if (assignedLight != null)
        {
            assignedLight.RegisterCar(agent);
        }

        StartCoroutine(DespawnWhenFar(newCar, agent));
    }

    CarAgent.DriverType RandomDriverType()
    {
        float roll = Random.Range(0f, 100f);
        if (roll < aggressivePercent) return CarAgent.DriverType.Aggressive;
        if (roll < aggressivePercent + normalPercent) return CarAgent.DriverType.Normal;
        return CarAgent.DriverType.Cautious;
    }

    IEnumerator DespawnWhenFar(GameObject car, CarAgent agent)
    {
        while (car != null)
        {
            float dist = Vector3.Distance(spawnPoint.position, car.transform.position);
            if (dist >= despawnDistance)
            {
                if (assignedLight != null) assignedLight.UnregisterCar(agent);
                Destroy(car);
                yield break;
            }
            yield return new WaitForSeconds(0.5f); // เช็คทุกครึ่งวิ ไม่ต้องเช็คทุกเฟรม (ประหยัด performance)
        }
    }

    
}