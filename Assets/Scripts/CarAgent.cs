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

    private bool lightIsGreen = false;
    private float reactionTimer = 0f;

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
        if (stopLineTarget != null && !hasCleared)
        {
            Vector3 frontPos = GetFrontPosition();
            Vector3 stopPoint = GetStopPoint();
            signedDistToStopPoint = Vector3.Dot(stopPoint - frontPos, transform.forward);
        }

        DecideSpeed();
        MoveForward();
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

    void DecideSpeed()
    {
        float desiredSpeed = ComputeDesiredSpeed();

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