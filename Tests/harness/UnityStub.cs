// Minimal UnityEngine stub - only for syntax and type checking with mcs. Not the real thing.
using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 right => new Vector3(1, 0, 0);
        // 15 Sep 2026: every operator used to return a and Dot returned 0, which meant there was
        //    no vector arithmetic at all. No test could touch vehicle behaviour (car-following,
        //    distance to the stop line) even though both recent bugs - cars skidding across the
        //    line when a queue is placed, and the whole queue launching at once - lived there.
        //    It is real arithmetic now, so HeadwayTest can run a queue through the real CarAgent.
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public Vector3 normalized
        {
            get { float m = magnitude; return m < 1e-6f ? zero : new Vector3(x / m, y / m, z / m); }
        }
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(
            a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static float Angle(Vector3 a, Vector3 b)
        {
            float d = a.normalized.x * b.normalized.x + a.normalized.y * b.normalized.y
                    + a.normalized.z * b.normalized.z;
            return (float)(Math.Acos(Math.Max(-1.0, Math.Min(1.0, d))) * 180.0 / Math.PI);
        }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float b) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator *(float b, Vector3 a) => new Vector3(a.x * b, a.y * b, a.z * b);
        public static Vector3 operator /(Vector3 a, float b) => new Vector3(a.x / b, a.y / b, a.z / b);
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a + (b - a) * Mathf.Clamp01(t);
        public override string ToString() => $"({x:F2}, {y:F2}, {z:F2})";
    }

    // 24 Sep 2026: yaw only (degrees about +Y, Unity convention: 0 = +Z, 90 = +X). Every car drives on
    //    the flat XZ plane, so this is enough to run turning and lane changes through the real CarAgent.
    //    Before this, RotateTowards returned its input and no test could see a car change heading.
    public struct Quaternion
    {
        public float yaw;
        public static Quaternion Euler(float x, float y, float z) => new Quaternion { yaw = y };
        public static Quaternion Euler(Vector3 v) => new Quaternion { yaw = v.y };
        public static Quaternion LookRotation(Vector3 f, Vector3 u) =>
            new Quaternion { yaw = (float)(Math.Atan2(f.x, f.z) * 180.0 / Math.PI) };
        public static Quaternion RotateTowards(Quaternion a, Quaternion b, float maxDegrees)
        {
            float d = DeltaYaw(a.yaw, b.yaw);
            if (Math.Abs(d) <= maxDegrees) return b;
            return new Quaternion { yaw = a.yaw + Math.Sign(d) * maxDegrees };
        }
        /// <summary>Signed shortest turn from a to b, in degrees (-180, 180]</summary>
        public static float DeltaYaw(float a, float b)
        {
            float d = (b - a) % 360f;
            if (d > 180f) d -= 360f;
            if (d <= -180f) d += 360f;
            return d;
        }
        public static Quaternion identity => new Quaternion();
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => a;
        public static Quaternion operator *(Quaternion a, Quaternion b) => new Quaternion { yaw = a.yaw + b.yaw };
    }

    public struct Color
    {
        public static Color Lerp(Color a, Color b, float t) => a;
        public static Color white => new Color();
        public static Color green => new Color();
        public static Color yellow => new Color();
        public static Color red => new Color();
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r=r; this.g=g; this.b=b; this.a=a; }
        public Color(float r, float g, float b) { this.r=r; this.g=g; this.b=b; this.a=1f; }
    }

    public struct Rect { public float x, y, width, height; public Rect(float a, float b, float c, float d) { x=a; y=b; width=c; height=d; }
        public float xMax => x + width; public float yMax => y + height;
        public bool Contains(Vector2 p) => false; }
    public struct Matrix4x4 { }

    public static class Mathf
    {
        public static int CeilToInt(float f) => (int)System.Math.Ceiling(f);
        public static int FloorToInt(float f) => (int)System.Math.Floor(f);
        public const float Rad2Deg = 57.29578f;
        public static float Sin(float f) => (float)System.Math.Sin(f);
        public static float Cos(float f) => (float)System.Math.Cos(f);

        public static float Max(float a, float b, float c) => Max(Max(a,b),c);
        public static float Exp(float a) => (float)System.Math.Exp(a);
        public static float Round(float a) => (float)System.Math.Round(a);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public const float Infinity = float.PositiveInfinity;
        public const float Deg2Rad = 0.0174532924f;
        public static float Log(float f) => (float)System.Math.Log(f);
        public static float Sqrt(float f) => (float)System.Math.Sqrt(f);
        public static float Abs(float f) => f < 0 ? -f : f;
        public static float Clamp01(float v) => v < 0 ? 0 : (v > 1 ? 1 : v);
        public static int RoundToInt(float v) => (int)System.Math.Round(v);
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Clamp(float v, float a, float b) => v < a ? a : (v > b ? b : v);
        // Used to return a unchanged, so cars never accelerated. Together with Sqrt returning 0
        // that zeroed the whole car-following term, and no test could catch a queue-launch bug.
        public static float MoveTowards(float a, float b, float c)
        {
            if (System.Math.Abs(b - a) <= c) return b;
            return a + System.Math.Sign(b - a) * c;
        }
        public static bool Approximately(float a, float b) => System.Math.Abs(a - b) < 1e-5f;
        public static int Clamp(int v, int a, int b) => v < a ? a : (v > b ? b : v);
    }

    public class Object
    {
        public static void Destroy(Object o) { }
        public static void DestroyImmediate(Object o) { }
        public string name;
        public HideFlags hideFlags;
        public static T Instantiate<T>(T original) where T : Object => original;
        public static T FindFirstObjectByType<T>() where T : Object => null;
        public static T[] FindObjectsByType<T>(FindObjectsInactive a, FindObjectsSortMode b) where T : Object => new T[0];
    }
    public class Component : Object
    {
        public Transform transform;
        public GameObject gameObject;
        public T GetComponent<T>() => default(T);
        public T[] GetComponents<T>() => new T[0];
    }
    public class Renderer : Component
    {
        public Material material; public Material sharedMaterial;
        public Material[] sharedMaterials = new Material[0];
        public bool enabled; public Bounds bounds;
        public Rendering.ShadowCastingMode shadowCastingMode; public bool receiveShadows;
    }
    public class Shader : Object { public static Shader Find(string n) => null; }
    public class Material : Object
    {
        public Shader shader;

        public Material(Shader s) { }
        public Color color;
        public bool HasProperty(string n) => false;
        public void SetFloat(string n, float v) { }
        public void SetColor(string n, Color v) { }
        public void SetTexture(string n, Texture t) { }
        public void SetInt(string n, int v) { }
        public void EnableKeyword(string n) { }
        public void DisableKeyword(string n) { }
        public int renderQueue;
        public Texture mainTexture;
        public Vector2 mainTextureScale;
    }
    public class Collider : Component { }

    public class Rigidbody : Component { public bool isKinematic, useGravity; }
    public class BoxCollider : Collider { public Vector3 center, size; public bool isTrigger; }
    public class GameObject : Object
    {
        private bool _active = true;
        public bool activeSelf { get { return _active; } }
        public bool activeInHierarchy { get { return _active; } }
        public void SetActive(bool v) { _active = v; }

        public GameObject() { }
        public GameObject(string n) { name = n; }
        public Transform transform;
        public T GetComponent<T>() => default(T);
        public T[] GetComponents<T>() => new T[0];
        public T[] GetComponentsInChildren<T>() => new T[0];
        public T[] GetComponentsInChildren<T>(bool includeInactive) => new T[0];
        public T AddComponent<T>() where T : Component => (T)System.Activator.CreateInstance(typeof(T));
        public static GameObject CreatePrimitive(PrimitiveType t) => new GameObject();
    }
    public class Transform : Component
    {
        public Vector3 position, localScale;
        // forward and rotation are two views of one yaw, as in Unity: setting either one updates the other.
        private float yawDeg;
        public Quaternion rotation
        {
            get { return new Quaternion { yaw = yawDeg }; }
            set { yawDeg = value.yaw; }
        }
        public Vector3 forward
        {
            get
            {
                double r = yawDeg * Math.PI / 180.0;
                return new Vector3((float)Math.Sin(r), 0f, (float)Math.Cos(r));
            }
            set { yawDeg = (float)(Math.Atan2(value.x, value.z) * 180.0 / Math.PI); }
        }
        public Matrix4x4 localToWorldMatrix;
        public Transform parent;
        public int childCount => 0;
        public Transform GetChild(int i) => null;
        public Vector3 localPosition;
        public Quaternion localRotation;
        public void SetParent(Transform p) { }
        public void SetParent(Transform p, bool worldPositionStays) { }
        public Vector3 TransformPoint(Vector3 v) => v;
        // This stub only models motion along the object's own heading, which is all
        // CarAgent.MoveForward does: transform.Translate(Vector3.forward * v) in Unity
        // is position += rotation * (0,0,v).
        public void Translate(Vector3 v) { position += forward * v.z; }
    }

    public class Coroutine { }
    public class YieldInstruction { }
    public class WaitForSeconds : YieldInstruction
    {
        public float seconds;
        public WaitForSeconds(float s) { seconds = s; }
    }

    public class MonoBehaviour : Component
    {
        public bool enabled;
        public GameObject gameObject;
        // stub: keeps the enumerator so a test can step it manually
        public IEnumerator captured;
        public Coroutine StartCoroutine(IEnumerator r) { captured = r; return null; }
        public void StopAllCoroutines() { captured = null; }
        public static T FindFirstObjectByType<T>() => default(T);
        public static GameObject Instantiate(GameObject o, Vector3 p, Quaternion q) => o;
        public static void Destroy(Object o) { }
    }

    public static class Time { public static float time, deltaTime, timeScale, unscaledTime, unscaledDeltaTime, realtimeSinceStartup; }
    public static class Random
    {
        public static float value => 0f;
        public static float Range(float a, float b) => a;
        public static int Range(int a, int b) => a;
        public static void InitState(int seed) { }
    }
    public static class Debug
    {
        public static void Log(object o) { }
        public static void LogWarning(object o) { }
        public static void LogError(object o) { }
    }
    public static class Application { public static bool isPlaying; public static int targetFrameRate; public static string dataPath; }
    public static class QualitySettings { public static int vSyncCount; }
    public static class Screen { public static int width, height; }
    public static class GUI
    {
        public static Color color;
        public static Color backgroundColor;
        public static Matrix4x4 matrix;
        public static GUISkin skin = new GUISkin();
        public static void Box(Rect r, string s) { }
        public static void Box(Rect r, GUIContent c) { }
        public static void DrawTexture(Rect r, Texture t) { }
        public static void DrawTexture(Rect r, Texture t, ScaleMode m, bool alpha, float ratio,
                                       Color c, Vector4 borderW, Vector4 borderR) { }
        public static void Label(Rect r, string s) { }
        public static void Label(Rect r, string s, GUIStyle st) { }
        public static bool Button(Rect r, string s) => false;
        public static Color contentColor;
        public static Rect Window(int id, Rect r, System.Action<int> f, string title) => r;
        public static void DragWindow(Rect r) { }
    }
    public static class Gizmos
    {
        public static Color color;
        public static Matrix4x4 matrix;
        public static void DrawCube(Vector3 a, Vector3 b) { }
        public static void DrawWireCube(Vector3 a, Vector3 b) { }
    }

    public class HeaderAttribute : Attribute { public HeaderAttribute(string s) { } }
    public class TooltipAttribute : Attribute { public TooltipAttribute(string s) { } }
    public class HideInInspectorAttribute : Attribute { }
    public class RangeAttribute : Attribute { public RangeAttribute(float a, float b) { } }
    public class ContextMenuAttribute : Attribute { public ContextMenuAttribute(string s) { } }
    public class SerializeFieldAttribute : Attribute { }
}

namespace System { public class SerializableAttribute2 { } }
