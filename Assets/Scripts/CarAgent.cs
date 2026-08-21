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
    private float emergencyDecelMultiplier;  // ← เบรกฉุกเฉินแรงกว่าปกติกี่เท่า
    private float dilemmaZoneFactor;         // ← ยิ่งสูง ยิ่งฝ่าไฟเหลืองง่าย

    [Header("Lane Identity")]
    public string assignedLane = "";   // ตั้งค่าโดย CarSpawner ตอน spawn
    [Header("Runtime State")]
    public float currentSpeed = 0f;
    public bool isStopped = false;
    private bool hasCleared = false;
    private bool committedThrough = false;   // true = ตัดสินใจฝ่าไฟเหลืองไปแล้ว ไม่หันกลับมาเบรก

    [Header("Car Following (กันรถซ้อนกัน)")]
    public float minGap = 2f;
    public float lookAheadAngle = 1.5f;

    private static System.Collections.Generic.List<CarAgent> allCars
        = new System.Collections.Generic.List<CarAgent>();

    [Header("Stop Line (ตั้งค่าตอน Spawn)")]
    public Transform stopLineTarget;
    private float signedDistToStopPoint;

    [Header("Turning Path (ตั้งค่าตอน Spawn — เว้นว่าง = วิ่งตรงเหมือนเดิม)")]
    [Tooltip("จุดนำทางหลังผ่านเส้นหยุด ใช้กับเลนเลี้ยว Ax / Bx / CB / CA")]
    public Transform[] pathWaypoints;
    [Tooltip("อัตราการหมุนพวงมาลัย องศา/วินาที")]
    public float steerRate = 55f;
    [Tooltip("รัศมีวงเลี้ยวเป้าหมาย (เมตร) — ยิ่งเล็กวงยิ่งแคบ แต่รถต้องชะลอมากขึ้น")]
    public float turnRadius = 7f;
    [Tooltip("เลี้ยวเกินกี่องศาถึงจะเริ่มชะลอ")]
    public float turnSlowAngle = 12f;
    [Tooltip("ช้าสุดตอนเลี้ยว (เมตร/วินาที) กันรถคลานจนแทบไม่ขยับ")]
    public float minTurnSpeed = 3f;
    [Tooltip("ระยะเผื่อเพิ่มจากระยะเบรกจริง (เมตร) ก่อนถึงจุดหักเลี้ยว")]
    public float turnBrakeMargin = 4f;
    [Tooltip("เข้าใกล้ waypoint เท่านี้ถือว่าถึงแล้ว")]
    public float waypointReachRadius = 2f;

    private int waypointIndex = 0;
    /// <summary>true = วิ่งจนสุดเส้นทางที่กำหนดแล้ว (CarSpawner ใช้ตัดสินใจลบรถทิ้ง)</summary>
    public bool reachedEndOfPath { get; private set; }

    [Header("Traffic Light (ตั้งค่าตอน Spawn — เว้นว่างถ้าเป็นเลนไหลอิสระ)")]
    [Tooltip("รถอ่านสถานะไฟจากตัวนี้เองทุกเฟรม เป็นตัวสำรองกรณีทะเบียนใน TrafficLight หลุด")]
    public TrafficLight assignedLight;

    private bool lightIsGreen = false;
    private float reactionTimer = 0f;
    private TrafficLight.LightState lastSeenLight = TrafficLight.LightState.Red;
    private bool lightInitialized = false;

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
                emergencyDecelMultiplier = 1.3f;  // เบรกแรงอยู่แล้ว เพิ่มไม่มาก
                dilemmaZoneFactor = 1.6f;         // ฝ่าไฟเหลืองง่าย
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
                emergencyDecelMultiplier = 1.8f;  // ปกติเบรกเบา ตกใจแล้วเบรกแรงขึ้นเยอะ
                dilemmaZoneFactor = 0.7f;         // ฝ่าเฉพาะหยุดไม่ทันจริง ๆ
                break;
        }
        currentSpeed = approachSpeed;
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

    void Update()
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

    // ===== อ่านสถานะไฟเองทุกเฟรม =====
    // เดิมพึ่ง TrafficLight.NotifyCars() อย่างเดียว ถ้าทะเบียนหลุด (เช่นโดน ClearRegistrations
    // หลัง spawn) รถจะไม่มีทางรู้ว่าไฟเขียวแล้ว จอดแช่ตลอดกาล
    // ตอนนี้ push กับ poll ทำงานคู่กัน อันไหนมาก่อนก็ได้ ผลเหมือนกัน
    void SyncLightState()
    {
        if (assignedLight == null) return;

        TrafficLight.LightState st = assignedLight.currentState;
        if (!lightInitialized || st != lastSeenLight)
        {
            OnLightStateChanged(st);
        }
    }

    // ===== เลี้ยวตาม waypoint หลังผ่านเส้นหยุดแล้ว =====
    // ก่อนถึงเส้นหยุดยังวิ่งตรงเสมอ เพื่อให้ signedDistToStopPoint และ ROI ทำงานเหมือนเดิม
    void Steer()
    {
        if (pathWaypoints == null || pathWaypoints.Length == 0) return;
        if (stopLineTarget != null && !hasCleared) return;
        if (reachedEndOfPath) return;

        // ข้าม waypoint ที่เป็น null หรือถูกลบไปแล้ว
        while (waypointIndex < pathWaypoints.Length && pathWaypoints[waypointIndex] == null)
            waypointIndex++;

        if (waypointIndex >= pathWaypoints.Length)
        {
            reachedEndOfPath = true;
            return;
        }

        Vector3 toTarget = pathWaypoints[waypointIndex].position - transform.position;
        toTarget.y = 0f;

        // ผ่านไปแล้ว (อยู่ข้างหลังรถ) ก็นับว่าถึงเหมือนกัน
        // กันเคสรถเลี้ยวกว้างเกินจนไม่เคยเข้าใกล้ waypoint ในรัศมี แล้ววนเป็นวงกลมไม่จบ
        bool reached = toTarget.magnitude <= waypointReachRadius
                       || Vector3.Dot(toTarget, transform.forward) < 0f;

        if (reached)
        {
            waypointIndex++;
            return;
        }

        Quaternion desired = Quaternion.LookRotation(toTarget.normalized, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation, desired, steerRate * Time.deltaTime);
    }

    float ComputeDesiredSpeed()
    {
        if (hasCleared || stopLineTarget == null)
        {
            return Mathf.MoveTowards(currentSpeed, approachSpeed, acceleration * Time.deltaTime);
        }

        if (signedDistToStopPoint < -0.3f)
        {
            hasCleared = true;
            isStopped = false;
            return Mathf.MoveTowards(currentSpeed, approachSpeed, acceleration * Time.deltaTime);
        }

        float brakeDistance = GetRequiredBrakeDistance();

        if (signedDistToStopPoint <= brakeDistance)
        {
            if (!lightIsGreen)
            {
                // ===== ไม่มี snap เป็น 0 อีกแล้ว → ปล่อยไล่ตาม decelRate ต่อเนื่อง = เกิดไถลธรรมชาติ =====
                float speed = currentSpeed - decelRate * Time.deltaTime;
                speed = Mathf.Max(0f, speed);
                if (speed <= 0.05f) isStopped = true;
                return speed;
            }
            else
            {
                if (isStopped)
                {
                    reactionTimer += Time.deltaTime;
                    if (reactionTimer >= reactionDelay)
                    {
                        isStopped = false;
                    }
                    else
                    {
                        return 0f;
                    }
                }
                return Mathf.MoveTowards(currentSpeed, approachSpeed, acceleration * Time.deltaTime);
            }
        }
        else
        {
            isStopped = false;
            reactionTimer = 0f;
            return approachSpeed;
        }
    }

    // ===== ความเร็วสูงสุดที่ยังเลี้ยวอยู่ในวงที่กำหนดได้ =====
    // รัศมีวงเลี้ยว R = v / ω  (ω = อัตราหมุนพวงมาลัย)
    // ถ้าอยากได้วงแคบ R ตามที่ตั้งไว้ ก็ต้องจำกัด v ≤ ω × R
    // ไม่มีข้อนี้ รถวิ่ง 15 m/s จะกวาดวงเลี้ยวกว้างเกิน 25 เมตร → หลุดออกนอกเลนไปทับเลนอื่น
    float TurnSpeedLimit()
    {
        if (pathWaypoints == null || pathWaypoints.Length == 0) return Mathf.Infinity;
        if (reachedEndOfPath) return Mathf.Infinity;
        if (stopLineTarget != null && !hasCleared) return Mathf.Infinity;  // ยังไม่ถึงเส้นหยุด วิ่งปกติ
        if (waypointIndex >= pathWaypoints.Length) return Mathf.Infinity;

        Transform wp = pathWaypoints[waypointIndex];
        if (wp == null) return Mathf.Infinity;

        Vector3 toTarget = wp.position - transform.position;
        toTarget.y = 0f;
        if (toTarget.magnitude < 0.5f) return Mathf.Infinity;

        float omega = steerRate * Mathf.Deg2Rad;            // เรเดียน/วินาที
        if (omega <= 0.001f) return Mathf.Infinity;
        float cornerSpeed = Mathf.Max(minTurnSpeed, omega * turnRadius);

        // 1) กำลังหักเลี้ยวอยู่ตอนนี้
        float angle = Vector3.Angle(transform.forward, toTarget);
        if (angle >= turnSlowAngle) return cornerSpeed;

        // 2) มองล่วงหน้าไป waypoint ถัดไป — ถ้าช่วงหน้าหักแรงและใกล้จะถึงแล้ว ให้เริ่มชะลอตั้งแต่ตอนนี้
        //    ⚠️ ขาดข้อนี้ไม่ได้: รถจะพุ่งเข้าโค้งด้วยความเร็วเต็ม 15 m/s แล้วกวาดวงกว้าง
        //    ออกไปทับเลนข้าง ๆ ก่อนที่จะทันชะลอ
        // ระยะที่ต้องใช้เบรกจาก v ปัจจุบัน ลงมาเหลือ cornerSpeed ตาม decelRate ของ profile นี้
        // ⚠️ ใช้ค่าคงที่ไม่ได้ เพราะคนขับแบบ Cautious เบรกช้ากว่า Aggressive ถึง 7 เท่า
        //    (decelRate 3 vs 22) ถ้าตั้งตายตัวคนขับระวังจะเข้าโค้งเร็วเกินแล้วกวาดออกนอกเลน
        float brakeDist = currentSpeed > cornerSpeed
            ? (currentSpeed * currentSpeed - cornerSpeed * cornerSpeed) / (2f * Mathf.Max(0.1f, decelRate))
            : 0f;

        if (waypointIndex + 1 < pathWaypoints.Length
            && pathWaypoints[waypointIndex + 1] != null
            && toTarget.magnitude <= brakeDist + turnBrakeMargin)
        {
            Vector3 nextSeg = pathWaypoints[waypointIndex + 1].position - wp.position;
            nextSeg.y = 0f;
            if (nextSeg.magnitude > 0.1f && Vector3.Angle(toTarget, nextSeg) >= turnSlowAngle)
                return cornerSpeed;
        }

        return Mathf.Infinity;   // ทางตรง วิ่งเต็มสปีดได้
    }

    void DecideSpeed()
    {
        float desiredSpeed = ComputeDesiredSpeed();

        // ---- ชะลอก่อนเข้าโค้ง ----
        float turnCap = TurnSpeedLimit();
        if (desiredSpeed > turnCap)
        {
            // ค่อย ๆ ลดตาม decelRate ไม่ใช่ตัดความเร็วทันที จะได้ดูเป็นธรรมชาติ
            float eased = Mathf.Max(turnCap, currentSpeed - decelRate * Time.deltaTime);
            desiredSpeed = Mathf.Min(desiredSpeed, eased);
        }

        float gapAhead;
        CarAgent carAhead = GetCarAhead(out gapAhead);

        if (carAhead != null)
        {
            float leadSpeed = carAhead.currentSpeed;
            float cushion = gapAhead - minGap;

            if (cushion <= 0f)
            {
                // ===== เบรกฉุกเฉิน: แรงกว่าปกติแต่ยังไม่ใช่ snap ทันที → ไถลนิดหน่อยตามนิสัย =====
                float emergencySpeed = currentSpeed - (decelRate * emergencyDecelMultiplier) * Time.deltaTime;
                emergencySpeed = Mathf.Max(0f, emergencySpeed);
                desiredSpeed = Mathf.Min(desiredSpeed, emergencySpeed);
                if (emergencySpeed <= 0.1f) isStopped = true;
            }
            else
            {
                float maxSafeSpeed = Mathf.Sqrt(Mathf.Max(0f, leadSpeed * leadSpeed + 2f * decelRate * cushion));
                desiredSpeed = Mathf.Min(desiredSpeed, maxSafeSpeed);
            }
        }

        currentSpeed = desiredSpeed;
    }

    CarAgent GetCarAhead(out float gap)
    {
        CarAgent closest = null;
        float closestGap = Mathf.Infinity;
        Vector3 frontPos = GetFrontPosition();

        foreach (CarAgent other in allCars)
        {
            if (other == this) continue;

            Vector3 toOther = other.transform.position - frontPos;
            float forwardDist = Vector3.Dot(toOther, transform.forward);
            if (forwardDist <= 0f) continue;

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

    void MoveForward()
    {
        transform.Translate(Vector3.forward * currentSpeed * Time.deltaTime);
    }

    // ===== เรียกจาก TrafficLight ตอนไฟเปลี่ยนสถานะ =====
    public void OnLightStateChanged(TrafficLight.LightState state)
    {
        lastSeenLight = state;
        lightInitialized = true;

        if (hasCleared) return; // ผ่านไปแล้ว ไม่สนไฟอีก

        if (state == TrafficLight.LightState.Green)
        {
            lightIsGreen = true;
        }
        else if (state == TrafficLight.LightState.Yellow)
        {
            // ===== Dilemma Zone: ตัดสินใจฝ่าหรือหยุด ตอนไฟเพิ่งเปลี่ยนเป็นเหลือง =====
            if (!committedThrough && stopLineTarget != null)
            {
                float requiredStop = GetRequiredBrakeDistance();
                float dilemmaThreshold = requiredStop * dilemmaZoneFactor;

                if (signedDistToStopPoint <= dilemmaThreshold)
                {
                    committedThrough = true;  // ใกล้เกินจะหยุดปลอดภัย (ตามนิสัย) → ฝ่าไปเลย
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
            // ถ้า committedThrough แล้ว ไฟแดงก็ไม่สนใจอีก เพราะฝ่าไปตั้งแต่ไฟเหลืองแล้ว
        }
    }
}