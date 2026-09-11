// stub เพิ่มเติมสำหรับ UITheme.cs — ใช้ตอน compile-check นอก Unity เท่านั้น
using System;
namespace UnityEngine
{
    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float a, float b, float c, float d) { x=a; y=b; z=c; w=d; }
        public static Vector4 zero => new Vector4(0,0,0,0);
        public static Vector4 one  => new Vector4(1,1,1,1);
        public static Vector4 operator *(Vector4 v, float f) => v;
        public static Vector4 operator *(float f, Vector4 v) => v;
    }

    public class RectOffset
    {
        public int left, right, top, bottom;
        public RectOffset() { }
        public RectOffset(int l, int r, int t, int b) { left=l; right=r; top=t; bottom=b; }
    }

    [Flags] public enum HideFlags { None=0, HideAndDontSave=61, DontSave=4 }
    public enum TextClipping { Overflow, Clip }
    public enum ScaleMode { StretchToFill, ScaleAndCrop, ScaleToFit }
    public enum TextureFormat { RGBA32, ARGB32 }
    public enum FilterMode { Point, Bilinear, Trilinear }
    public enum TextureWrapMode { Repeat, Clamp }

    public struct Bounds
    {
        public Vector3 center, size, min, max;
        public void Encapsulate(Bounds b) { }
    }

    public class Mesh : Object
    {
        public int subMeshCount;
        public Vector3[] vertices = new Vector3[0];
        public uint GetIndexCount(int i) => 0;
        public int[] GetTriangles(int i) => new int[0];
    }

    public class MeshFilter : Component { public Mesh sharedMesh; public Mesh mesh; }
    public class Light : Component { }
    public class Camera : Component { public static Camera main => null; public float fieldOfView; }

    public class GUIContent
    {
        public static GUIContent none = new GUIContent();
        public GUIContent() { }
        public GUIContent(string s) { }
    }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off, On, TwoSided, ShadowsOnly }
    public enum BlendMode { Zero, One, SrcAlpha, OneMinusSrcAlpha }
}
