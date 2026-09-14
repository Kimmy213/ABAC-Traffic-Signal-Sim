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

    [Header("Body Paint (ชิ้นที่จะถูกทาสีตามเลน)")]
    [Tooltip("Renderer ของตัวถังรถ — ตั้งให้อัตโนมัติตอนสร้าง prefab โดย ABAC ▸ Rebuild Car Prefab\n" +
             "⚠️ ห้ามทาสีทุก Renderer ในรถ ไม่งั้นล้อ กระจก และไฟหน้า/ท้าย จะโดนทาสีเลนไปด้วยหมดคัน")]
    public Renderer bodyRenderer;

    [Tooltip("ช่องวัสดุ (material slot) ของตัวถังใน bodyRenderer — โมเดลรถมีหลายช่อง " +
             "(สีตัวถัง / กระจก / ยาง / ไฟหน้า / ไฟท้าย) ต้องทาแค่ช่องสีตัวถังช่องเดียว")]
    public int bodyMaterialIndex = 0;

    [Header("Lane Identity")]
    public string assignedLane = "";   // ตั้งค่าโดย CarSpawner ตอน spawn
    [Header("Runtime State")]
    public float currentSpeed = 0f;
    public bool isStopped = false;

    // ===== วางคิวเริ่มต้น (standing queue) =====
    // ปกติรถเกิดที่ต้นเลนแล้ววิ่งเข้ามา SetProfileParameters จึงตั้ง currentSpeed = approachSpeed
    // แต่รถที่ CarSpawner.PlaceStandingQueue วางไว้ "จอดติดไฟแดงอยู่แล้ว" ตั้งแต่วินาทีที่ 0
    // ถ้าปล่อยให้เกิดมาด้วยความเร็ววิ่ง มันจะไถลเลยเส้นหยุดไปทั้งแถวทั้งที่ไฟยังแดง
    // (Cautious decel 3 m/s^2 ที่ 13.89 m/s = ไถลไป 32 ม.) ธงนี้ให้ SetProfileParameters
    // เริ่มที่ความเร็ว 0 และถือว่าหยุดอยู่แล้ว เพื่อให้ reactionDelay ตอนไฟเขียวทำงานตามปกติ
    [HideInInspector] public bool startAtRest = false;

    /// <summary>ระยะที่ "หน้ารถ" คันแรกของคิวต้องอยู่ก่อนเส้นหยุด (เมตร)
    /// ต้องมากกว่า |stopOvershoot| ที่ติดลบมากที่สุด (Cautious = -1 ม.) บวกเกณฑ์ -0.3 ใน
    /// ComputeDesiredSpeed ไม่งั้นรถ Cautious จะถูกนับว่า hasCleared ตั้งแต่เฟรมแรกแล้วฝ่าไฟแดงออกไป</summary>
    public const float REST_FRONT_MARGIN = 1f;
    private bool hasCleared = false;
    private bool committedThrough = false;   // true = ตัดสินใจฝ่าไฟเหลืองไปแล้ว ไม่หันกลับมาเบรก

    [Header("Car Following (กันรถซ้อนกัน)")]
    public float minGap = 2f;
    [Tooltip("ระยะห่างด้านข้างสูงสุดที่ยังนับว่า 'อยู่เลนเดียวกัน' (เมตร) — ระยะห่างระหว่างเลนในซีนคือ 3.5 ม. " +
             "ค่านี้จึงต้องน้อยกว่าครึ่งหนึ่งของ 3.5 เพื่อไม่ให้รถคนละเลนเบรกให้กันโดยไม่จำเป็น")]
    public float lookAheadAngle = 1.5f;
    [Tooltip("มองหารถคันหน้าไกลสุดกี่เมตร — ⚠️ ห้ามตั้งต่ำกว่าระยะเบรกของคนขับแบบ Cautious (~33 ม.) " +
             "ไม่งั้นรถจะเห็นคิวช้าเกินจนเบรกไม่ทัน")]
    public float lookAheadDistance = 50f;

    [Header("Shared Lane Bypass (เฉพาะเลนที่แชร์ทางกายภาพ เช่น Ax/A2)")]
    [Tooltip("true = คันนี้ใช้ 'ไหล่ทาง/เลนฉุกเฉิน' แซงคิวที่จอดขวางในเลนแชร์ได้ — สุ่มครั้งเดียวตอน spawn โดย " +
             "CarSpawner ตาม laneBypassChance จำลองพฤติกรรมจริงที่ Itim สังเกต (รถขยับขวา/มอไซค์แทรก/รปภ.โบก) " +
             "ที่ทำให้การบล็อกไม่ใช่ 100%\n\n" +
             "⚠️ เดิมทำโดย 'ข้ามไม่นับรถคนละเลนเป็นตัวบล็อก' ใน GetCarAhead() ซึ่งผิด — รถวิ่งทะลุผ่านคันที่จอดอยู่ " +
             "ตรง ๆ (เห็นเป็นรถซ้อนกันในซีน) ตอนนี้เปลี่ยนเป็นการ 'เยื้องออกไหล่ทางจริง ๆ' ตาม bypassLateralOffset " +
             "แล้วปล่อยให้ car-following ตามระยะห่างจริงจัดการเอง จึงไม่มีทางซ้อนทับกันอีก")]
    public bool canBypassCrossLaneBlocker = false;

    [Tooltip("ระยะเยื้องออกไหล่ทาง (world space) ตอนแซงคิวในเลนแชร์ — ตั้งโดย CarSpawner " +
             "ใช้เฉพาะช่วงวิ่งเข้าหา waypoint แรก (ก่อนเริ่มหักเลี้ยว) จากนั้นกลับเข้าเลนปกติเอง")]
    public Vector3 bypassLateralOffset = Vector3.zero;

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

    [Header("Late Merge (เปลี่ยนเลนก่อนถึงเส้นหยุด — ใช้กับ A2)")]
    [Tooltip("true = เลี้ยว/เปลี่ยนเลนได้ทั้งที่ยังไม่ผ่านเส้นหยุด\n" +
             "ปกติรถที่มีเส้นหยุดจะวิ่งตรงจนผ่านเส้นก่อนถึงเริ่มเลี้ยว (กันค่า signedDistToStopPoint เพี้ยน)\n" +
             "แต่ A2 ต้อง 'เปลี่ยนเลน' เข้าเลนแชร์ก่อนถึงเส้นหยุด — การเยื้องด้านข้างไม่กระทบระยะเบรก " +
             "เพราะรถวิ่งขนานแกน X ล้วน signedDistToStopPoint คิดจากผลต่าง X อย่างเดียว")]
    public bool steerBeforeStopLine = false;

    [Tooltip("ยังไม่เริ่มเปลี่ยนเลนจนกว่าจะเหลือระยะถึง waypoint แรกน้อยกว่านี้ (เมตร) — 0 = เปลี่ยนตั้งแต่ spawn\n\n" +
             "⭐ นี่คือหัวใจของ 'late merge': Itim นับจากคลิปว่ารถตรงที่เข้าเลนแชร์ทั้ง 3 คัน เปลี่ยนเลนเข้ามา" +
             "ตอนเหลือระยะแค่ ~5 ช่วงรถก่อนถึงจุดเลี้ยว = แทรกเข้า 'หัวคิว' พอดี\n" +
             "ถ้าปล่อยให้เปลี่ยนเลนตั้งแต่ไกล รถจะกระจายอยู่กลางคิว เกิดเป็น 'หน่วงนิดเดียวแต่บ่อย' " +
             "ซึ่งค่าเฉลี่ยเท่ากันแต่ queue dynamics ผิดไปคนละเรื่องกับของจริงที่ 'นาน ๆ ครั้งแต่รุนแรงเกือบสูงสุด'")]
    public float steerStartDistance = 0f;

    private int waypointIndex = 0;
    /// <summary>true = วิ่งจนสุดเส้นทางที่กำหนดแล้ว (CarSpawner ใช้ตัดสินใจลบรถทิ้ง)</summary>
    public bool reachedEndOfPath { get; private set; }

    [Header("Traffic Light (ตั้งค่าตอน Spawn — เว้นว่างถ้าเป็นเลนไหลอิสระ)")]
    [Tooltip("รถอ่านสถานะไฟจากตัวนี้เองทุกเฟรม เป็นตัวสำรองกรณีทะเบียนใน TrafficLight หลุด")]
    public TrafficLight assignedLight;

    [Tooltip("true = เลน 'ให้ทาง' (yield) — ไฟที่อ้างถึงเป็นไฟของ*อีกกระแสหนึ่ง* ไม่ใช่ไฟของเราเอง\n" +
             "อีกฝ่ายเขียว/เหลือง = เราต้องหยุด, อีกฝ่ายแดง = เราไปได้\n\n" +
             "ใช้กับเลน Ax: ปกติเป็นเลนฟรีไม่ต้องรอไฟ แต่หน้างานจริงเลี้ยวเข้ามอไม่ได้ตอน Bx เขียว " +
             "(Bx เลี้ยวขวาเข้ามอเหมือนกัน กวาดผ่านปากทางเข้า) จึงผูก Ax ไว้กับไฟ Bx แบบกลับด้าน\n" +
             "⚠️ Ax ยังคง isSignalized = false ใน IntersectionConfig เพราะมันไม่มีเฟสของตัวเอง " +
             "ไม่ได้รับการจัดสรรเวลาเขียว และไม่เข้าไปอยู่ใน conflict matrix — เป็นแค่การให้ทางเท่านั้น")]
    public bool lightIsInverted = false;

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

    // ===== sub-stepping: ความละเอียดของการเคลื่อนที่ต้องไม่ขึ้นกับ timeScale =====
    // เดิม integrate ครั้งเดียวต่อเฟรมด้วย Time.deltaTime ซึ่ง = เวลาจริงต่อเฟรม x timeScale
    // ที่ 10x / 60 fps หนึ่งเฟรม = 0.167 วิ -> รถ 14 m/s ขยับทีละ 2.3 ม. การตัดสินใจเบรก/เส้นหยุด
    // ถูกประเมินแค่ทุก 2.3 ม. จึงเลยเลน เลยเส้นหยุด (สังเกตได้จริงที่ 10x, 2x แทบไม่เป็น)
    // ตอนนี้ซอยเวลาของแต่ละเฟรมเป็นก้าวละไม่เกิน MAX_STEP (= Unity fixed timestep) เสมอ
    // -> ที่ 1x หรือ 10x รถตัดสินใจทุก 0.02 วิจำลองเท่ากัน ผลจึงไม่ขึ้นกับความเร็วที่รัน
    public const float MAX_STEP = 0.02f;
    const int MAX_SUBSTEPS = 50;            // กัน spiral of death ถ้าเฟรมช้าผิดปกติ (Unity cap deltaTime ที่ 0.333 s อยู่แล้ว)
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

    /// <summary>หนึ่งก้าวของการจำลอง ยาว stepDt วินาที</summary>
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

    /// <summary>จำนวนก้าวที่หนึ่งเฟรมจะถูกซอย — public static เพื่อให้เทสตรวจได้โดยไม่ต้องมี Unity</summary>
    public static int SubStepsFor(float frameDt, float maxStep = MAX_STEP)
    {
        if (frameDt <= 0f) return 0;
        int n = Mathf.CeilToInt(frameDt / maxStep);
        return Mathf.Min(n, MAX_SUBSTEPS);
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
        if (stopLineTarget != null && !hasCleared && !steerBeforeStopLine) return;
        if (reachedEndOfPath) return;

        // ข้าม waypoint ที่เป็น null หรือถูกลบไปแล้ว
        while (waypointIndex < pathWaypoints.Length && pathWaypoints[waypointIndex] == null)
            waypointIndex++;

        if (waypointIndex >= pathWaypoints.Length)
        {
            reachedEndOfPath = true;
            return;
        }

        Vector3 toTarget = CurrentWaypointTarget() - transform.position;
        toTarget.y = 0f;

        // ===== late merge: ยังไกลอยู่ก็วิ่งตรงในเลนเดิมไปก่อน ยังไม่ต้องเบียดเข้าเลนใหม่ =====
        if (steerStartDistance > 0f && waypointIndex == 0
            && Vector3.Dot(toTarget, transform.forward) > steerStartDistance) return;

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
                // ===== ไม่มี snap เป็น 0 อีกแล้ว → ปล่อยไล่ตาม decelRate ต่อเนื่อง = เกิดไถลธรรมชาติ =====
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

        Vector3 wpPos = CurrentWaypointTarget();
        Vector3 toTarget = wpPos - transform.position;
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
            Vector3 nextSeg = pathWaypoints[waypointIndex + 1].position - wpPos;
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
            float eased = Mathf.Max(turnCap, currentSpeed - decelRate * stepDt);
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
                float emergencySpeed = currentSpeed - (decelRate * emergencyDecelMultiplier) * stepDt;
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

        // ⚠️ ไม่มีการ "ข้ามรถคนละเลน" อีกแล้ว (ของเดิมทำให้รถที่สุ่มได้แซงวิ่งทะลุผ่านรถที่จอดอยู่จริง ๆ
        //    = เห็นเป็นรถซ้อนทับกันในซีน) การแซงไหล่ทางตอนนี้ทำด้วยการเยื้องตำแหน่งจริงแทน (bypassLateralOffset)
        //    แล้วปล่อยให้เงื่อนไข lateralDist ข้างล่างคัดออกเองตามระยะห่างจริง — ฟิสิกส์เดียว ไม่มีข้อยกเว้น
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
    /// จุดหมายของ waypoint ปัจจุบัน — ปกติคือตัว waypoint เอง
    /// ยกเว้นรถที่ "ใช้ไหล่ทางแซงคิว" ในช่วงวิ่งเข้าหา waypoint แรก (ยังไม่เริ่มหักเลี้ยว)
    /// จะเล็งไปยังจุดที่เยื้องออกไปตาม bypassLateralOffset เพื่อให้วิ่งอยู่บนไหล่ทางจริง ๆ ตลอดช่วงที่แซง
    /// พอเข้า waypoint ถัดไป (เริ่มเลี้ยว) ก็กลับมาใช้เส้นทางปกติ = merge กลับเข้าเลนเองโดยอัตโนมัติ
    /// </summary>
    Vector3 CurrentWaypointTarget()
    {
        Vector3 p = pathWaypoints[waypointIndex].position;
        if (waypointIndex == 0 && canBypassCrossLaneBlocker) p += bypassLateralOffset;
        return p;
    }

    /// <summary>
    /// มองหารถคันหน้าได้ไกลสุดกี่เมตร
    ///
    /// ⚠️ จุดนี้เคยเป็นบั๊กใหญ่: ของเดิมมองไกล "ไม่จำกัด" ตามแนวเส้นตรงหน้ารถ
    /// แต่เลนเลี้ยว (Ax) จะหักออกไปทางอื่นก่อนถึงรถที่มองเห็น เช่น Ax วิ่งอยู่ z=6.5 มุ่ง +X
    /// แนวเส้นตรงของมันพุ่งทะลุสี่แยกไปเจอรถ CA ที่ merge เข้าเลน z=6.5 ฝั่งตะวันออก (ห่าง 40-90 ม.)
    /// ทั้งที่ตัวเองจะเลี้ยวซ้ายออกไปตั้งแต่ x=-11 ไม่มีทางไปชนกันเลย
    /// ผลคือรถ Ax เบรกและ "จอดไกล" จากสี่แยกโดยไม่มีเหตุผล
    ///
    /// ตอนนี้จำกัด 2 ชั้น: (1) เพดานระยะมองปกติ (2) ถ้าเลย waypoint ปัจจุบันไปแล้วเส้นทางหักเลี้ยว
    /// ก็มองได้แค่ถึง waypoint นั้น — รถที่อยู่เลยจุดหักไปไม่เกี่ยวกับเราแล้ว
    /// (คิวที่บล็อกจริงอยู่ "ก่อน" จุดหักเสมอ จึงยังตรวจเจอครบเหมือนเดิม)
    /// </summary>
    float LookAheadLimit()
    {
        float limit = lookAheadDistance;

        if (pathWaypoints == null || pathWaypoints.Length == 0) return limit;
        if (reachedEndOfPath) return limit;
        // ⚠️ ไม่มี guard "ยังไม่ถึงเส้นหยุด" ตรงนี้โดยตั้งใจ — ต่อให้ยังไม่ถึงเส้นหยุด รถเลนเลี้ยวก็รู้อยู่แล้วว่า
        //    ตัวเองจะหักออกที่ waypoint แรก จึงไม่ควรเบรกให้รถที่อยู่เลยจุดนั้นไป (นี่คือบั๊ก "Ax จอดไกล")
        if (waypointIndex >= pathWaypoints.Length) return limit;
        if (pathWaypoints[waypointIndex] == null) return limit;
        if (waypointIndex + 1 >= pathWaypoints.Length) return limit;
        if (pathWaypoints[waypointIndex + 1] == null) return limit;

        Vector3 wpPos = CurrentWaypointTarget();
        Vector3 toWp = wpPos - GetFrontPosition();
        toWp.y = 0f;

        float along = Vector3.Dot(toWp, transform.forward);
        if (along <= 0f) return limit;

        // ⚠️ ตัดระยะมองแค่ "เลนที่หักออกไปจริง ๆ" เท่านั้น (Ax/Bx/CB/CA) ห้ามตัดให้เลนเปลี่ยนเลน (A2)
        //    A2 เปลี่ยนเลนแล้ววิ่งตรงต่อ ถ้าไปตัดระยะมองที่จุดเปลี่ยนเลน มันจะมองไม่เห็นคิวที่รออยู่
        //    เลยจุดนั้นแล้วพุ่งชนท้าย — เช็คด้วย "ปลายทางของเส้นทางเบนจากหัวรถไปกี่องศา"
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
    /// เส้นทางข้างหน้า "หักออกไปคนละทาง" หรือแค่ "เปลี่ยนเลนแล้ววิ่งตรงต่อ"
    ///
    /// ดูจากทิศของช่วงสุดท้ายของเส้นทางเทียบกับหัวรถตอนนี้:
    ///   Ax เลี้ยวซ้ายเข้ามอ  -> ปลายทางมุ่ง +Z ต่างจากหัวรถ (+X) ~90°  = หักออก
    ///   A2 เปลี่ยนเลนแล้วตรง -> ปลายทางมุ่ง +X เท่าหัวรถ ~0°           = ไม่ได้หักออก
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
    /// มีรถคันไหนอยู่ในรัศมีนี้ไหม — CarSpawner เรียกก่อนปล่อยรถ กันปล่อยทับคันที่จอดค้างอยู่ตรงจุด spawn
    /// (เกิดจริงเมื่อคิวยาวถอยมาถึงจุด spawn หรือเมื่อสอง spawner ใช้เลนกายภาพเดียวกัน เช่น A2 กับ Ax)
    /// </summary>
    /// <summary>
    /// รถของเลนนี้ที่อยู่ใกล้จุดอ้างอิงที่สุด — CameraDirector ใช้เลือกคันที่จะเกาะ
    /// (ส่งตำแหน่งกล้องนิ่งของมุมนั้นมาเป็นจุดอ้างอิง จะได้เกาะคันที่อยู่ในกรอบภาพอยู่แล้ว)
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

    // ===== เรียกจาก TrafficLight ตอนไฟเปลี่ยนสถานะ =====
    public void OnLightStateChanged(TrafficLight.LightState state)
    {
        lastSeenLight = state;
        lightInitialized = true;

        if (hasCleared) return; // ผ่านไปแล้ว ไม่สนไฟอีก

        // ===== เลนให้ทาง (yield): อ่านไฟของอีกกระแสแบบกลับด้าน =====
        // เหลืองของอีกฝ่ายก็ยังถือว่าเราไปไม่ได้ เพราะรถเขายังเคลียร์ปากทางไม่หมด
        // ไม่ใช้ dilemma zone ที่นี่ — คนที่ให้ทางไม่ได้ "เร่งฝ่า" เหมือนคนเจอไฟเหลืองของตัวเอง
        // ถ้าใกล้เส้นเกินกว่าจะหยุดทัน ComputeDesiredSpeed จะไถเลยเส้นแล้วนับเป็น hasCleared ไปเอง
        // (พฤติกรรมนี้สมจริง = หยุดไม่ทันก็ต้องไปต่อ) ไม่ต้องเขียนเพิ่ม
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