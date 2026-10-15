using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Preset camera shots for the ABAC intersection, plus a runtime button bar to switch between them.
///
/// Every shot is computed from the real scene coordinates by ABACSceneBuilder, not typed in by hand,
/// so moving a lane or a stop line and pressing Build moves the cameras with it.
///
/// Shortcuts:  [ ]  = previous/next shot,  V = toggle follow mode,  C = back to the overview
/// (avoids the keys SimulationControlPanel already uses: Space R H F G 1-5)
/// </summary>
public class CameraDirector : MonoBehaviour
{
    [System.Serializable]
    public class Shot
    {
        public string label = "Shot";
        [Tooltip("Lane this shot is aimed at - drives the button colour and follow mode (blank = not tied to a lane)")]
        public string lane = "";
        public Vector3 position;
        public Vector3 euler;
        public float fov = 60f;

        [Tooltip("Follow-mode camera offset: how far back and how high")]
        public float followBack = 9f;
        public float followUp = 4.5f;
        [Tooltip("false = this shot cannot follow a car (an overview, for example)")]
        public bool canFollow = true;
    }

    [Header("Camera")]
    public Camera targetCamera;

    [Header("Shots (filled in automatically by ABACSceneBuilder)")]
    public List<Shot> shots = new List<Shot>();

    [Header("Behaviour")]
    [Tooltip("Seconds taken to move to a new shot - 0 = cut immediately")]
    public float moveTime = 0.55f;
    [Tooltip("Follow mode: the camera tracks whichever car in that lane is nearest the stop line")]
    public bool followMode = false;
    [Tooltip("Delay before picking a new car, in seconds, so the camera does not jump between cars")]
    public float reacquireInterval = 0.4f;

    [Header("UI")]
    public bool showBar = true;

    [Header("Hotkeys")]
    public KeyCode keyNext = KeyCode.RightBracket;
    public KeyCode keyPrev = KeyCode.LeftBracket;
    public KeyCode keyFollow = KeyCode.V;
    public KeyCode keyOverview = KeyCode.C;
    [Tooltip("Toggles the green detection boxes drawn around vehicles inside a ROI")]
    public KeyCode keyRoiBoxes = KeyCode.B;

    private int index = 0;
    private CarAgent followed;
    private float nextReacquire = 0f;
    private bool snapNext = true;

    public int CurrentIndex => index;
    public Shot Current => (shots != null && index >= 0 && index < shots.Count) ? shots[index] : null;

    void Awake()
    {
        if (targetCamera == null) targetCamera = Camera.main;
    }

    void Start()
    {
        snapNext = true;   // jump straight to the first shot instead of sliding in from wherever the camera was
    }

    public void Select(int i)
    {
        if (shots == null || shots.Count == 0) return;
        index = Mathf.Clamp(i, 0, shots.Count - 1);
        followed = null;
        Shot s = Current;
        if (s != null && !s.canFollow) followMode = false;
    }

    public void Next() { if (shots.Count > 0) Select((index + 1) % shots.Count); }
    public void Prev() { if (shots.Count > 0) Select((index - 1 + shots.Count) % shots.Count); }

    void LateUpdate()
    {
        if (targetCamera == null) { targetCamera = Camera.main; if (targetCamera == null) return; }
        Shot s = Current;
        if (s == null) return;

        Vector3 wantPos;
        Quaternion wantRot;

        if (followMode && s.canFollow && !string.IsNullOrEmpty(s.lane))
        {
            if (Time.unscaledTime >= nextReacquire || followed == null)
            {
                followed = CarAgent.NearestInLane(s.lane, s.position);
                nextReacquire = Time.unscaledTime + reacquireInterval;
            }

            if (followed != null)
            {
                Transform t = followed.transform;
                // Sit behind and above the car, looking slightly ahead of it, so both the car and whatever blocks it are visible
                wantPos = t.position - t.forward * s.followBack + Vector3.up * s.followUp;
                wantRot = Quaternion.LookRotation((t.position + t.forward * 6f) - wantPos, Vector3.up);
            }
            else
            {
                // No car in that lane right now - hold the static shot rather than show a black frame
                wantPos = s.position;
                wantRot = Quaternion.Euler(s.euler);
            }
        }
        else
        {
            wantPos = s.position;
            wantRot = Quaternion.Euler(s.euler);
        }

        // Uses unscaledDeltaTime because a batch run pushes timeScale to 20x.
        //    With ordinary deltaTime the camera would snap into place and look jerky.
        float k = (snapNext || moveTime <= 0.001f)
                  ? 1f
                  : 1f - Mathf.Exp(-Time.unscaledDeltaTime / Mathf.Max(0.01f, moveTime) * 3f);

        Transform cam = targetCamera.transform;
        cam.position = Vector3.Lerp(cam.position, wantPos, k);
        cam.rotation = Quaternion.Slerp(cam.rotation, wantRot, k);
        targetCamera.fieldOfView = Mathf.Lerp(targetCamera.fieldOfView, s.fov, k);
        snapNext = false;
    }

    void OnGUI()
    {
        Event e = Event.current;
        if (e != null && e.type == EventType.KeyDown)
        {
            if (e.keyCode == keyNext)     { Next(); e.Use(); }
            else if (e.keyCode == keyPrev) { Prev(); e.Use(); }
            else if (e.keyCode == keyFollow)
            {
                Shot s = Current;
                if (s != null && s.canFollow && !string.IsNullOrEmpty(s.lane)) followMode = !followMode;
                e.Use();
            }
            else if (e.keyCode == keyOverview) { Select(0); e.Use(); }
            else if (e.keyCode == keyRoiBoxes) { RoiHighlight.ShowAll = !RoiHighlight.ShowAll; e.Use(); }
        }

        if (!showBar || !Application.isPlaying || shots == null || shots.Count == 0) return;

        UITheme.Begin(scaled: true);

        // ---- Shot button bar, centred along the bottom ----
        const float BW = 108f, BH = 26f, GAP = 4f;
        const float ROI_BTN_W = 116f;
        float barW = shots.Count * BW + (shots.Count - 1) * GAP + 24f + ROI_BTN_W + GAP;
        float barH = BH + 40f;
        Rect bar = new Rect(UITheme.Width / 2f - barW / 2f, UITheme.Height - barH - 12f, barW, barH);
        GUI.Box(bar, GUIContent.none);

        GUI.Label(new Rect(bar.x + 12f, bar.y + 6f, 200f, 16f), "CAMERA", UITheme.Title);

        Shot cur = Current;
        string hint = cur != null && cur.canFollow && !string.IsNullOrEmpty(cur.lane)
            ? (followMode ? "[ ] view   V: following" : "[ ] view   V: follow car")
            : "[ ] view   C: overview";
        GUI.Label(new Rect(bar.x + bar.width - 252f, bar.y + 7f, 240f, 15f), hint, UITheme.Right);

        float x = bar.x + 12f;
        float y = bar.y + 26f;
        for (int i = 0; i < shots.Count; i++)
        {
            Shot s = shots[i];
            bool active = (i == index);
            Rect r = new Rect(x, y, BW, BH);

            if (active)
            {
                Color lc = string.IsNullOrEmpty(s.lane) ? UITheme.Accent : UITheme.LaneColor(s.lane);
                UITheme.Pill(r, s.label, followMode && s.canFollow ? UITheme.SigGreen : lc);
            }
            else if (GUI.Button(r, s.label))
            {
                Select(i);
            }

            x += BW + GAP;
        }

        // ---- detection-box toggle: shows what the ROI actually "sees" ----
        Rect roiBtn = new Rect(x, y, ROI_BTN_W, BH);
        if (RoiHighlight.ShowAll)
        {
            UITheme.Pill(roiBtn, "B · ROI boxes ON", UITheme.SigGreen);
            if (Event.current != null && Event.current.type == EventType.MouseDown
                && roiBtn.Contains(Event.current.mousePosition))
            {
                RoiHighlight.ShowAll = false;
                Event.current.Use();
            }
        }
        else if (GUI.Button(roiBtn, "B · ROI boxes"))
        {
            RoiHighlight.ShowAll = true;
        }

        UITheme.End();
    }
}
