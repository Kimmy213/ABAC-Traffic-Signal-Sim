// Stub เพิ่มเติมสำหรับ compile-check ตัว Editor script เท่านั้น
using System;
using System.Collections.Generic;
using UnityEngine;

namespace UnityEngine
{
    public enum PrimitiveType { Cube, Cylinder, Sphere, Capsule, Plane, Quad }
    public enum FindObjectsInactive { Exclude, Include }
    public enum FindObjectsSortMode { None, InstanceID }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene { public GameObject[] GetRootGameObjects() => new GameObject[0]; }
}

namespace UnityEditor
{
    public class MenuItemAttribute : Attribute
    {
        public MenuItemAttribute(string s) { }
        public MenuItemAttribute(string s, bool v) { }
        public MenuItemAttribute(string s, bool v, int p) { }
    }

    public static class EditorUtility
    {
        public static bool DisplayDialog(string t, string m, string ok, string cancel) => true;
        public static bool DisplayDialog(string t, string m, string ok) => true;   // overload จริงของ Unity
        public static void SetDirty(UnityEngine.Object o) { }
    }

    public static class Undo
    {
        public static void RegisterCreatedObjectUndo(UnityEngine.Object o, string n) { }
        public static void RecordObject(UnityEngine.Object o, string n) { }
        public static T AddComponent<T>(GameObject g) where T : Component => default(T);
        public static void DestroyObjectImmediate(UnityEngine.Object o) { }
        public static void SetTransformParent(Transform t, Transform p, string n) { }
    }

    public static class Selection { public static GameObject activeGameObject; public static UnityEngine.Object activeObject; }

    public static class AssetDatabase
    {
        public static T LoadAssetAtPath<T>(string path) where T : UnityEngine.Object => null;
        public static void Refresh() { }
        public static void CreateAsset(UnityEngine.Object o, string path) { }
        public static void SaveAssets() { }
    }
}

namespace UnityEditor.SceneManagement
{
    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene GetActiveScene() => new UnityEngine.SceneManagement.Scene();
        public static void MarkSceneDirty(UnityEngine.SceneManagement.Scene s) { }
    }
}

namespace UnityEditor
{
    public static class Handles
    {
        public static void Label(UnityEngine.Vector3 pos, string text) { }
    }

    public static class PrefabUtility
    {
        public static GameObject SaveAsPrefabAsset(GameObject go, string path) => go;
        public static UnityEngine.Object InstantiatePrefab(UnityEngine.Object o) => o;
    }
}