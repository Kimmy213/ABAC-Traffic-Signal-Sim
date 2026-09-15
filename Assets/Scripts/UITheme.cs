using UnityEngine;

/// <summary>
/// Shared theme for every IMGUI panel in the project.
///
/// How to use it: wrap a panel OnGUI in UITheme.Begin() ... UITheme.End().
/// Every existing GUI.Box / GUI.Label / GUI.Button line then picks up the theme immediately,
/// with no change at the call site, because Begin() swaps GUI.skin and End() puts it back.
///
/// All textures are built in memory on first use, so no asset files can go missing,
/// and everything is null-checked in case memory is reclaimed on a play-mode switch.
/// </summary>
public static class UITheme
{
    // ===================== Palette =====================
    public static readonly Color BgPanel    = new Color(0.086f, 0.094f, 0.118f, 0.960f);
    public static readonly Color BgRaised   = new Color(0.137f, 0.149f, 0.180f, 0.980f);
    public static readonly Color BgSunken   = new Color(0.055f, 0.060f, 0.075f, 0.980f);
    public static readonly Color Line       = new Color(1f, 1f, 1f, 0.10f);

    public static readonly Color TextHi     = new Color(0.94f, 0.95f, 0.97f, 1f);
    public static readonly Color TextMid    = new Color(0.72f, 0.75f, 0.80f, 1f);
    public static readonly Color TextDim    = new Color(0.50f, 0.54f, 0.60f, 1f);

    public static readonly Color Accent     = new Color(0.31f, 0.60f, 1.00f, 1f);   // blue - primary buttons
    public static readonly Color AccentDark = new Color(0.18f, 0.38f, 0.72f, 1f);

    // Signal state colours - the same set in the scene and the UI, to avoid confusion
    public static readonly Color SigGreen   = new Color(0.24f, 0.80f, 0.42f, 1f);
    public static readonly Color SigYellow  = new Color(0.98f, 0.75f, 0.18f, 1f);
    public static readonly Color SigRed     = new Color(0.93f, 0.33f, 0.31f, 1f);
    public static readonly Color Neutral    = new Color(0.45f, 0.49f, 0.56f, 1f);

    // Lane group colours - match the car colours in the scene (A=blue B=orange C=green)
    public static readonly Color LaneA      = new Color(0.23f, 0.51f, 0.96f, 1f);
    public static readonly Color LaneB      = new Color(0.96f, 0.62f, 0.07f, 1f);
    public static readonly Color LaneC      = new Color(0.13f, 0.77f, 0.37f, 1f);

    // ===================== Standard sizes =====================
    public const int Pad = 10;
    public const int RowH = 20;
    public const int Radius = 6;

    // ===================== Internal resources =====================
    static GUISkin skin;
    static GUISkin prevSkin;
    static Texture2D texPanel, texRaised, texSunken, texAccent, texAccentHover, texWhite, texLine;

    public static GUIStyle Title, H2, Body, Dim, Value, Mono, PillText, Right;

    static bool Alive(Texture2D t) { return t != null; }

    /// <summary>Builds or repairs the theme resources - safe to call repeatedly, cheap once built.</summary>
    public static void Ensure()
    {
        if (skin != null && Alive(texPanel) && Alive(texAccent) && Alive(texWhite)) return;

        texWhite       = Solid(Color.white);
        texLine        = Solid(Line);
        texPanel       = Rounded(Radius, BgPanel,  new Color(1f, 1f, 1f, 0.13f));
        texRaised      = Rounded(Radius, BgRaised, new Color(1f, 1f, 1f, 0.10f));
        texSunken      = Rounded(Radius, BgSunken, new Color(0f, 0f, 0f, 0.35f));
        texAccent      = Rounded(Radius, Accent,   new Color(1f, 1f, 1f, 0.22f));
        texAccentHover = Rounded(Radius, Lighten(Accent, 0.10f), new Color(1f, 1f, 1f, 0.32f));

        skin = Object.Instantiate(GUI.skin);
        skin.name = "ABAC UI Theme";
        skin.hideFlags = HideFlags.HideAndDontSave;

        RectOffset b = new RectOffset(Radius, Radius, Radius, Radius);

        skin.box.normal.background = texPanel;
        skin.box.border = b;
        skin.box.padding = new RectOffset(Pad, Pad, 8, 8);
        skin.box.normal.textColor = TextHi;
        skin.box.alignment = TextAnchor.UpperLeft;
        skin.box.fontSize = 12;

        skin.window.normal.background = texPanel;
        skin.window.onNormal.background = texPanel;
        skin.window.border = b;
        skin.window.padding = new RectOffset(Pad, Pad, Pad, Pad);
        skin.window.normal.textColor = TextHi;

        skin.label.normal.textColor = TextMid;
        skin.label.fontSize = 12;
        skin.label.padding = new RectOffset(2, 2, 1, 1);

        StyleButton(skin.button, texRaised, texAccentHover, texAccent, b);
        if (skin.GetStyle("Button") != null) { }

        skin.toggle.normal.textColor = TextMid;
        skin.toggle.onNormal.textColor = TextHi;

        skin.textField.normal.background = texSunken;
        skin.textField.focused.background = texSunken;
        skin.textField.border = b;
        skin.textField.padding = new RectOffset(6, 6, 3, 3);
        skin.textField.normal.textColor = TextHi;

        skin.horizontalSlider.normal.background = texSunken;
        skin.horizontalSlider.border = b;

        Title    = New(15, FontStyle.Bold,   TextHi,  TextAnchor.MiddleLeft);
        H2       = New(12, FontStyle.Bold,   TextMid, TextAnchor.MiddleLeft);
        Body     = New(12, FontStyle.Normal, TextMid, TextAnchor.MiddleLeft);
        Dim      = New(11, FontStyle.Normal, TextDim, TextAnchor.MiddleLeft);
        Value    = New(12, FontStyle.Bold,   TextHi,  TextAnchor.MiddleRight);
        Mono     = New(12, FontStyle.Normal, TextHi,  TextAnchor.MiddleLeft);
        PillText = New(11, FontStyle.Bold,   Color.white, TextAnchor.MiddleCenter);
        Right    = New(12, FontStyle.Normal, TextMid, TextAnchor.MiddleRight);
    }

    static void StyleButton(GUIStyle s, Texture2D normal, Texture2D hover, Texture2D active, RectOffset b)
    {
        s.normal.background = normal;
        s.hover.background = hover;
        s.active.background = active;
        s.focused.background = normal;
        s.onNormal.background = active;
        s.border = b;
        s.padding = new RectOffset(10, 10, 4, 4);
        s.margin = new RectOffset(2, 2, 2, 2);
        s.normal.textColor = TextHi;
        s.hover.textColor = Color.white;
        s.active.textColor = Color.white;
        s.fontSize = 12;
        s.alignment = TextAnchor.MiddleCenter;
    }

    static GUIStyle New(int size, FontStyle fs, Color c, TextAnchor a)
    {
        GUIStyle s = new GUIStyle();
        s.fontSize = size; s.fontStyle = fs; s.alignment = a;
        s.normal.textColor = c;
        s.padding = new RectOffset(2, 2, 0, 0);
        s.clipping = TextClipping.Clip;
        return s;
    }

    /// <summary>Starts drawing with this theme - must always be paired with End().</summary>
    public static void Begin()
    {
        Ensure();
        prevSkin = GUI.skin;
        GUI.skin = skin;
    }

    public static void End()
    {
        GUI.skin = prevSkin;
    }

    // ===================== Ready-made widgets =====================

    /// <summary>Panel box with a title - returns the Rect of the inner area to lay content into.</summary>
    public static Rect Panel(Rect r, string title)
    {
        Ensure();
        GUI.Box(r, GUIContent.none);
        Rect inner = new Rect(r.x + Pad, r.y + 8, r.width - Pad * 2, r.height - 16);
        if (!string.IsNullOrEmpty(title))
        {
            GUI.Label(new Rect(inner.x, inner.y, inner.width, 20), title, Title);
            Rule(new Rect(inner.x, inner.y + 22, inner.width, 1));
            inner.y += 29; inner.height -= 29;
        }
        return inner;
    }

    /// <summary>Thin separator line</summary>
    public static void Rule(Rect r)
    {
        Ensure();
        Color old = GUI.color; GUI.color = Line;
        GUI.DrawTexture(r, texWhite);
        GUI.color = old;
    }

    /// <summary>Capsule status badge, e.g. GREEN / RED / RUNNING</summary>
    public static void Pill(Rect r, string text, Color c)
    {
        Ensure();
        Color old = GUI.color;
        GUI.color = c;
        GUI.DrawTexture(r, texAccent, ScaleMode.StretchToFill, true, 0f, Color.white,
                        Vector4.zero, Vector4.one * Radius);
        GUI.color = old;
        GUI.Label(r, text, PillText);
    }

    /// <summary>Round signal-state dot</summary>
    public static void Dot(Rect r, Color c)
    {
        Ensure();
        Color old = GUI.color; GUI.color = c;
        GUI.DrawTexture(r, texWhite, ScaleMode.StretchToFill, true, 0f, Color.white,
                        Vector4.zero, Vector4.one * (r.height * 0.5f));
        GUI.color = old;
    }

    /// <summary>Progress bar / meter (t = 0..1)</summary>
    public static void Bar(Rect r, float t, Color c)
    {
        Ensure();
        Color old = GUI.color;
        GUI.color = BgSunken;
        GUI.DrawTexture(r, texWhite, ScaleMode.StretchToFill, true, 0f, Color.white,
                        Vector4.zero, Vector4.one * (r.height * 0.5f));
        t = Mathf.Clamp01(t);
        if (t > 0.001f)
        {
            GUI.color = c;
            Rect f = new Rect(r.x, r.y, Mathf.Max(r.height, r.width * t), r.height);
            GUI.DrawTexture(f, texWhite, ScaleMode.StretchToFill, true, 0f, Color.white,
                            Vector4.zero, Vector4.one * (r.height * 0.5f));
        }
        GUI.color = old;
    }

    /// <summary>A "name .......... value" row - keeps every panel looking the same</summary>
    public static void Row(Rect r, string key, string val)
    {
        Ensure();
        GUI.Label(new Rect(r.x, r.y, r.width * 0.58f, r.height), key, Body);
        GUI.Label(new Rect(r.x + r.width * 0.58f, r.y, r.width * 0.42f, r.height), val, Value);
    }

    /// <summary>Colour for a signal state</summary>
    public static Color SignalColor(TrafficLight.LightState s)
    {
        if (s == TrafficLight.LightState.Green)  return SigGreen;
        if (s == TrafficLight.LightState.Yellow) return SigYellow;
        return SigRed;
    }

    /// <summary>Lane colour, taken from the first letter of the lane name</summary>
    public static Color LaneColor(string lane)
    {
        if (string.IsNullOrEmpty(lane)) return Neutral;
        char c = lane[0];
        if (c == 'A') return LaneA;
        if (c == 'B') return LaneB;
        if (c == 'C') return LaneC;
        return Neutral;
    }

    // ===================== Texture building =====================

    static Texture2D Solid(Color c)
    {
        Texture2D t = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        t.SetPixel(0, 0, c);
        t.Apply();
        t.hideFlags = HideFlags.HideAndDontSave;
        return t;
    }

    /// <summary>Rounded rectangle with a 1px border, for use as a 9-slice background</summary>
    static Texture2D Rounded(int radius, Color fill, Color border)
    {
        int size = radius * 2 + 4;
        Texture2D t = new Texture2D(size, size, TextureFormat.RGBA32, false);
        t.hideFlags = HideFlags.HideAndDontSave;
        t.filterMode = FilterMode.Bilinear;
        t.wrapMode = TextureWrapMode.Clamp;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float d = CornerDistance(x, y, size, radius);
                // d < 0 = inside the shape, near 0 = the edge (feathered to avoid jaggies)
                float inside = Mathf.Clamp01(0.5f - d);
                float edge = Mathf.Clamp01(1f - Mathf.Abs(d + 0.5f) * 1.4f);
                Color c = Color.Lerp(fill, border, edge * border.a);
                c.a = fill.a * inside + border.a * edge * inside;
                if (inside <= 0f) c.a = 0f;
                t.SetPixel(x, y, c);
            }
        }
        t.Apply();
        return t;
    }

    /// <summary>Distance from the edge of the rounded rect (negative = inside)</summary>
    static float CornerDistance(int x, int y, int size, int radius)
    {
        float cx = Mathf.Min(x + 0.5f, size - x - 0.5f);
        float cy = Mathf.Min(y + 0.5f, size - y - 0.5f);
        float dx = Mathf.Max(0f, radius - cx);
        float dy = Mathf.Max(0f, radius - cy);
        return Mathf.Sqrt(dx * dx + dy * dy) - 0.5f;
    }

    static Color Lighten(Color c, float amt)
    {
        return new Color(Mathf.Clamp01(c.r + amt), Mathf.Clamp01(c.g + amt), Mathf.Clamp01(c.b + amt), c.a);
    }
}
