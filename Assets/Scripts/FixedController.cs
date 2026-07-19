using UnityEngine;

public class FixedController : MonoBehaviour
{
    [Header("Traffic Lights (ลากใส่ใน Inspector)")]
    public TrafficLight lightA;
    public TrafficLight lightB;
    public TrafficLight lightC;

    [Header("Timing (วินาที) — อ้างอิง DOH Thailand (2019), 120s/cycle")]
    public float greenDuration = 35f;
    public float yellowDuration = 3f;
    public float allRedDuration = 2f;   // ช่วงไฟแดงทั้งหมด กันชนตอนเปลี่ยนเฟส

    private TrafficLight[] lights;
    private int currentIndex = 0;

    // ===== Awake() ทำงานทันทีตอนโหลด ไม่ต้องรอ enable ต่างจาก Start() =====
    void Awake()
    {
        lights = new TrafficLight[] { lightA, lightB, lightC };
    }
    public void ForceStop()
    {
        StopAllCoroutines();
        SetAllRed();
    }
    public void RestartCycle()
    {
        StopAllCoroutines();
        currentIndex = 0;
        SetAllRed();
        StartCoroutine(RunCycle());
    }

    void Start()
    {
        // เริ่มต้น: ทุกไฟแดงหมดก่อน
        SetAllRed();

        // เริ่ม loop สลับเฟส
        StartCoroutine(RunCycle());
    }

    void SetAllRed()
    {
        foreach (TrafficLight light in lights)
        {
            if (light != null) light.SetState(TrafficLight.LightState.Red);
        }
    }

    // Coroutine = เหมือน async generator ใน Python ที่ค้างรอได้โดยไม่บล็อกเฟรมอื่น
    System.Collections.IEnumerator RunCycle()
    {
        while (true)
        {
            TrafficLight active = lights[currentIndex];

            // 1. ไฟเขียว
            active.SetState(TrafficLight.LightState.Green);
            yield return new WaitForSeconds(greenDuration);

            // 2. ไฟเหลือง
            active.SetState(TrafficLight.LightState.Yellow);
            yield return new WaitForSeconds(yellowDuration);

            // 3. ไฟแดงทั้งหมด (all-red buffer)
            active.SetState(TrafficLight.LightState.Red);
            yield return new WaitForSeconds(allRedDuration);

            // 4. ไปเฟสถัดไป (วนกลับ A→B→C→A...)
            currentIndex = (currentIndex + 1) % lights.Length;
        }
    }
}