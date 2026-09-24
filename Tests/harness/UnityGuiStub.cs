// Extra stubs for compile-checking the IMGUI part of the Control Panel.
using System;

namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 zero => new Vector2(0, 0);
    }

    public enum FontStyle { Normal, Bold, Italic, BoldAndItalic }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum EventType { KeyDown, KeyUp, MouseDown, MouseUp, Repaint, Layout, Used }
    public enum KeyCode { None, Space, R, H, F, G, V, C, N, B, D, Tab, LeftBracket, RightBracket, Alpha1, Alpha2, Alpha3, Alpha4, Alpha5 }

    public class Event
    {
        public Vector2 mousePosition;
        public static Event current;
        public EventType type;
        public KeyCode keyCode;
        public void Use() { }
    }

    public class GUIStyleState { public Color textColor; public Texture2D background; }
    public class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public int fontSize;
        public FontStyle fontStyle;
        public TextAnchor alignment;
        public bool wordWrap;
        public GUIStyleState normal = new GUIStyleState();
        public GUIStyleState hover = new GUIStyleState();
        public GUIStyleState active = new GUIStyleState();
        public GUIStyleState focused = new GUIStyleState();
        public GUIStyleState onNormal = new GUIStyleState();
        public RectOffset border = new RectOffset();
        public RectOffset padding = new RectOffset();
        public RectOffset margin = new RectOffset();
        public TextClipping clipping;
    }
    public class GUISkin : Object
    {
        public GUIStyle label = new GUIStyle();
        public GUIStyle button = new GUIStyle();
        public GUIStyle box = new GUIStyle();
        public GUIStyle window = new GUIStyle();
        public GUIStyle toggle = new GUIStyle();
        public GUIStyle textField = new GUIStyle();
        public GUIStyle horizontalSlider = new GUIStyle();
        public GUIStyle GetStyle(string n) => null;
    }
    public class GUILayoutOption { }

    public static class GUIUtility
    {
        public static void ScaleAroundPivot(Vector2 scale, Vector2 pivot) { }
    }

    public static class GUILayout
    {
        public static void Label(string t) { }
        public static void Label(string t, GUIStyle s, params GUILayoutOption[] o) { }
        public static bool Button(string t, GUIStyle s, params GUILayoutOption[] o) => false;
        public static bool Button(string t, params GUILayoutOption[] o) => false;
        public static void Box(string t, params GUILayoutOption[] o) { }
        public static string TextField(string t, params GUILayoutOption[] o) => t;
        public static bool Toggle(bool v, string t, params GUILayoutOption[] o) => v;
        public static float HorizontalSlider(float v, float a, float b, params GUILayoutOption[] o) => v;
        public static void Space(float p) { }
        public static void FlexibleSpace() { }
        public static void BeginHorizontal(params GUILayoutOption[] o) { }
        public static void EndHorizontal() { }
        public static void BeginVertical(params GUILayoutOption[] o) { }
        public static void BeginArea(Rect r) { }
        public static void EndArea() { }
        public static void EndVertical() { }
        public static Vector2 BeginScrollView(Vector2 p, params GUILayoutOption[] o) => p;
        public static void EndScrollView() { }
        public static GUILayoutOption Width(float w) => null;
        public static GUILayoutOption Height(float h) => null;
    }

    public class TextAreaAttribute : Attribute
    {
        public TextAreaAttribute(int min, int max) { }
    }
}

namespace UnityEngine
{
    public class Texture : Object { }
    public class Texture2D : Texture
    {
        public Texture2D() { }
        public Texture2D(int w, int h) { }
        public Texture2D(int w, int h, TextureFormat f, bool mip) { }
        public HideFlags hideFlags;
        public FilterMode filterMode;
        public TextureWrapMode wrapMode;
        public void SetPixel(int x, int y, Color c) { }
        public void Apply() { }
    }
    public static class GUILayoutUtility
    {
        public static Rect GetRect(float w, float h) => new Rect(0, 0, w, h);
        public static Rect GetRect(float w, float h, params GUILayoutOption[] o) => new Rect(0, 0, w, h);
    }
}
