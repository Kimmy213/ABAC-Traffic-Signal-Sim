using UnityEngine;

/// <summary>
/// Green tracking box drawn around a vehicle while it sits inside a detection zone (ROI).
///
/// This is the visual answer to "what does the detector actually see?" — the same question the
/// computer-vision half of the project answers from drone footage. Turning it on during a demo
/// shows, frame by frame, which vehicles are being counted and which are outside the zone.
///
/// Toggled globally by <see cref="ShowAll"/> (button on the camera bar, or the V-key row in the UI),
/// and per-vehicle by ROIZone as cars enter and leave.
/// </summary>
public class RoiHighlight : MonoBehaviour
{
    /// <summary>Global switch — off by default so a normal run looks clean.</summary>
    public static bool ShowAll = false;

    [Tooltip("The wireframe outline, created automatically by ABAC > Rebuild Car Prefab")]
    public GameObject boxRoot;

    [Tooltip("Legacy single-renderer box — only set on prefabs built before the outline change")]
    public Renderer box;

    private int insideCount = 0;

    void OnEnable()  { Refresh(); }

    /// <summary>Called by ROIZone when this vehicle enters a detection zone.</summary>
    public void EnterZone()
    {
        insideCount++;
        Refresh();
    }

    /// <summary>Called by ROIZone when this vehicle leaves a detection zone.</summary>
    public void ExitZone()
    {
        insideCount--;
        if (insideCount < 0) insideCount = 0;
        Refresh();
    }

    void Update()
    {
        // The global switch can flip at any time, so keep the outline in sync.
        // Cheap: one bool compare per vehicle per frame.
        Refresh();
    }

    void Refresh()
    {
        bool want = ShowAll && insideCount > 0;
        if (boxRoot != null && boxRoot.activeSelf != want) boxRoot.SetActive(want);
        if (box != null && box.enabled != want) box.enabled = want;
    }
}
