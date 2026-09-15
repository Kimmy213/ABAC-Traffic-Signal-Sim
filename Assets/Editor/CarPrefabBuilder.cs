#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Builds Assets/Prefabs/Car.prefab from the real 3D model (Assets/Models/car_low_poly.fbx).
/// Menu: ABAC > Rebuild Car Prefab (ABACSceneBuilder also calls it during Build Scene).
///
/// Everything is computed from the model at build time rather than hard-coded, so swapping the
/// model file and pressing the menu item again just works:
///   - strips the cameras, lights and nulls Blender adds (otherwise every car carries a Light and Camera)
///   - scales it uniformly to 4 m long, matching CarAgent.carLength
///   - points the nose along +Z, Unity's forward axis, deciding which end is which from the red tail lights
///   - places it so the wheels touch the ground when spawned at y = CAR_Y
/// </summary>
public static class CarPrefabBuilder
{
    public const string FbxPath    = "Assets/Models/car_low_poly.fbx";
    public const string PrefabPath = "Assets/Prefabs/Car.prefab";

    /// <summary>Target car length in metres - must match CarAgent.carLength or queue spacing goes wrong</summary>
    public const float TargetLength = 4f;

    /// <summary>Thickness of one bar of the ROI tracking outline (m).</summary>
    public const float RoiEdgeThickness = 0.09f;

    /// <summary>
    /// How far the outline sits outside the vehicle's own bounds (m, total across the box).
    /// Just enough that the bars clear the bodywork instead of z-fighting it — deliberately tiny,
    /// because the outline of a queued vehicle must never reach the vehicle behind it
    /// (bumper-to-bumper spacing at rest is CarAgent.minGap).
    /// </summary>
    public const float RoiBoxPadding = 0.06f;

    [MenuItem("ABAC/Rebuild Car Prefab", false, 2)]
    public static void RebuildMenu()
    {
        GameObject p = Rebuild(null);
        if (p != null) Selection.activeObject = p;
    }

    public static GameObject Rebuild(Material defaultBodyMaterial)
    {
        GameObject fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
        if (fbx == null)
        {
            Debug.LogWarning($"[ABAC] no car model at {FbxPath} - skipping the prefab rebuild " +
                             "(the existing one still works). If the file was just copied in, run Assets > Refresh first.");
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        // ---------- Assemble a temporary instance in the scene ----------
        GameObject root = new GameObject("Car");
        GameObject model = Object.Instantiate(fbx);
        model.name = "Model";
        model.transform.SetParent(root.transform, false);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.identity;
        model.transform.localScale = Vector3.one;

        int stripped = StripNonCarParts(model);

        List<Renderer> rends = new List<Renderer>(model.GetComponentsInChildren<Renderer>());
        rends.RemoveAll(r => r == null || r.GetComponent<MeshFilter>() == null);
        if (rends.Count == 0)
        {
            Debug.LogError("[ABAC] the car model has no MeshRenderer at all - aborting");
            Object.DestroyImmediate(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        }

        // ---------- 1) Align the car's long axis with Z ----------
        Bounds b = Combined(rends);
        if (b.size.x > b.size.z)
        {
            model.transform.localRotation = Quaternion.Euler(0f, 90f, 0f) * model.transform.localRotation;
            b = Combined(rends);
        }

        // ---------- 2) Find the body part and its material slot ----------
        Renderer body = LargestRenderer(rends);
        int paintSlot = BiggestSubmesh(body);
        int tailSlot  = ReddestSlot(body);

        // ---------- 3) The nose must point +Z (tail lights on the -Z side) ----------
        string facing = "could not tell (no red tail lights found) - keeping the model's own orientation";
        if (tailSlot >= 0)
        {
            Vector3 tail = SubmeshCentroid(body, tailSlot);
            if (tail.z > b.center.z)   // tail lights on the +Z side means the car faces backwards; turn it
            {
                model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f) * model.transform.localRotation;
                b = Combined(rends);
                facing = "rotated 180 degrees (tail lights were on the +Z side, facing backwards)";
            }
            else facing = "no rotation needed (tail lights already on the -Z side)";
        }

        // ---------- 4) Scale to 4 m long ----------
        float scale = b.size.z > 0.0001f ? TargetLength / b.size.z : 1f;
        model.transform.localScale = Vector3.one * scale;
        b = Combined(rends);

        // ---------- 5) Centre it and put the wheels on the ground ----------
        // Spawned at y = CAR_Y (0.5), the wheels must sit at y = 0, so the underside is at local y = -0.5
        Vector3 fix = model.transform.position - b.center;
        fix.y = model.transform.position.y - b.min.y - ABACSceneBuilder.CAR_Y;
        model.transform.localPosition = fix;
        b = Combined(rends);

        // ---------- 6) Components the system needs ----------
        BoxCollider col = root.AddComponent<BoxCollider>();
        col.size = b.size;
        col.center = b.center - root.transform.position;
        col.isTrigger = false;

        Rigidbody rb = root.AddComponent<Rigidbody>();
        rb.isKinematic = true;      // driven by transform, not by physics
        rb.useGravity = false;
        // A Rigidbody is required or ROIZone's OnTriggerEnter never fires and nothing gets counted

        // ---------- 6.1) green tracking box shown while the vehicle is inside a detection zone ----------
        // Drawn as a WIREFRAME outline, not a solid translucent block.
        // A solid box is a volume: with the alpha blending it needs, you see straight through the box
        // of the car in front into the box of the car behind, and a queue reads as one continuous
        // green mass instead of N vehicles. An outline made of thin bars has almost no area to blend,
        // so every vehicle in a stopped queue stays individually countable — which is the whole point
        // of the overlay, and also what a real detector draws.
        GameObject boxGO = BuildWireBox(root.transform, b.center - root.transform.position, b.size);
        boxGO.SetActive(false);                           // off until the car actually enters a zone

        RoiHighlight hl = root.AddComponent<RoiHighlight>();
        hl.boxRoot = boxGO;

        CarAgent agent = root.AddComponent<CarAgent>();
        agent.carLength = TargetLength;
        agent.bodyRenderer = body;
        agent.bodyMaterialIndex = paintSlot;

        if (defaultBodyMaterial != null && body != null && paintSlot >= 0)
        {
            Material[] mats = body.sharedMaterials;
            if (paintSlot < mats.Length) { mats[paintSlot] = defaultBodyMaterial; body.sharedMaterials = mats; }
        }

        // ---------- 7) Save as a prefab ----------
        System.IO.Directory.CreateDirectory(Application.dataPath + "/Prefabs");
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();

        Debug.Log($"<b>[ABAC] built Car.prefab from the real model</b>\n" +
                  $"- stripped {stripped} Blender extras (cameras / lights / nulls)\n" +
                  $"- size after scaling: {b.size.z:F2} long x {b.size.x:F2} wide x {b.size.y:F2} high m (scale {scale:F4})\n" +
                  $"- facing: {facing}\n" +
                  $"- body = '{(body != null ? body.name : "-")}' material slot {paintSlot} " +
                  $"(tail lights = slot {tailSlot}), so only that slot is painted per lane\n" +
                  $"- wheels touch the ground when spawned at y = {ABACSceneBuilder.CAR_Y}");

        return saved;
    }

    // ===================== helpers =====================

    /// <summary>
    /// Builds the ROI tracking outline: a 12-bar wireframe cuboid centred at <paramref name="center"/>
    /// (local space) around a body of the given size. Sized from the vehicle's real bounds plus
    /// <see cref="RoiBoxPadding"/> only, so two outlines can never touch in a stopped queue.
    /// </summary>
    static GameObject BuildWireBox(Transform parent, Vector3 center, Vector3 size)
    {
        GameObject rootGO = new GameObject("RoiBox");
        rootGO.transform.SetParent(parent, false);
        rootGO.transform.localPosition = center;

        Vector3 outer = size + Vector3.one * RoiBoxPadding;
        Material mat = UnlitMaterial("Roi_Highlight", new Color(0.16f, 0.98f, 0.42f, 1f));
        float t  = RoiEdgeThickness;
        float hx = outer.x / 2f, hy = outer.y / 2f, hz = outer.z / 2f;

        // 12 edges of a cuboid = 4 bars along each axis.
        for (int i = 0; i < 4; i++)
        {
            float sy = (i < 2 ? -1f : 1f) * hy;
            float sz = (i % 2 == 0 ? -1f : 1f) * hz;
            AddBar(rootGO.transform, mat, new Vector3(0f, sy, sz), new Vector3(outer.x, t, t));
        }
        for (int i = 0; i < 4; i++)
        {
            float sx = (i < 2 ? -1f : 1f) * hx;
            float sz = (i % 2 == 0 ? -1f : 1f) * hz;
            AddBar(rootGO.transform, mat, new Vector3(sx, 0f, sz), new Vector3(t, outer.y, t));
        }
        for (int i = 0; i < 4; i++)
        {
            float sx = (i < 2 ? -1f : 1f) * hx;
            float sy = (i % 2 == 0 ? -1f : 1f) * hy;
            AddBar(rootGO.transform, mat, new Vector3(sx, sy, 0f), new Vector3(t, t, outer.z));
        }
        return rootGO;
    }

    /// <summary>One bar of the wireframe outline.</summary>
    static void AddBar(Transform parent, Material mat, Vector3 pos, Vector3 scale)
    {
        GameObject bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        bar.name = "Edge";
        bar.transform.SetParent(parent, false);
        bar.transform.localPosition = pos;
        bar.transform.localScale = scale;

        Collider c = bar.GetComponent<Collider>();
        if (c != null) Object.DestroyImmediate(c);        // must not disturb the ROI trigger

        Renderer r = bar.GetComponent<Renderer>();
        if (r != null)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }
    }

    /// <summary>
    /// Flat unlit material for the tracking outline. Unlit keeps the green constant whatever the sun
    /// angle, so the outline stays legible in the low-angle junction camera shots as well as overhead.
    /// Opaque on purpose — the old translucent box is exactly what made queued boxes blend together.
    /// </summary>
    static Material UnlitMaterial(string name, Color color)
    {
        string path = $"Assets/Materials/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        bool isNew = m == null;

        Shader sh = Shader.Find("Unlit/Color");
        if (sh == null) sh = Shader.Find("Standard");
        if (isNew)
        {
            if (sh == null) return null;
            m = new Material(sh);
        }
        else if (sh != null && m.shader != sh) m.shader = sh;   // upgrade the old transparent asset

        m.color = color;
        if (m.HasProperty("_Color"))     m.SetColor("_Color", color);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_EmissionColor"))
        {
            m.EnableKeyword("_EMISSION");
            m.SetColor("_EmissionColor", color);
        }
        m.renderQueue = -1;                                     // whatever the shader asks for

        if (isNew)
        {
            System.IO.Directory.CreateDirectory(Application.dataPath + "/Materials");
            AssetDatabase.CreateAsset(m, path);
            AssetDatabase.SaveAssets();
        }
        else EditorUtility.SetDirty(m);
        return m;
    }

    /// <summary>Strips the cameras, lights and mesh-less objects Blender adds to the file</summary>
    static int StripNonCarParts(GameObject model)
    {
        int n = 0;
        foreach (Light l in model.GetComponentsInChildren<Light>(true))
        {
            if (l != null) { Object.DestroyImmediate(l.gameObject); n++; }
        }
        foreach (Camera c in model.GetComponentsInChildren<Camera>(true))
        {
            if (c != null) { Object.DestroyImmediate(c.gameObject); n++; }
        }
        return n;
    }

    static Bounds Combined(List<Renderer> rends)
    {
        Bounds b = rends[0].bounds;
        for (int i = 1; i < rends.Count; i++) b.Encapsulate(rends[i].bounds);
        return b;
    }

    /// <summary>The largest part is the body (wheels and glass are always smaller)</summary>
    static Renderer LargestRenderer(List<Renderer> rends)
    {
        Renderer best = rends[0];
        float bestV = -1f;
        foreach (Renderer r in rends)
        {
            Vector3 s = r.bounds.size;
            float v = s.x * s.y * s.z;
            if (v > bestV) { bestV = v; best = r; }
        }
        return best;
    }

    /// <summary>The material slot covering the most area in that part is the body paint</summary>
    static int BiggestSubmesh(Renderer r)
    {
        Mesh m = MeshOf(r);
        if (m == null || m.subMeshCount <= 1) return 0;
        int best = 0, bestTris = -1;
        for (int i = 0; i < m.subMeshCount; i++)
        {
            int t = (int)m.GetIndexCount(i);
            if (t > bestTris) { bestTris = t; best = i; }
        }
        return best;
    }

    /// <summary>The reddest material slot is the tail lights, which tells us which end is the rear</summary>
    static int ReddestSlot(Renderer r)
    {
        Material[] mats = r != null ? r.sharedMaterials : null;
        if (mats == null) return -1;
        int best = -1; float bestScore = 0.25f;   // must be clearly red, otherwise treat it as not found
        for (int i = 0; i < mats.Length; i++)
        {
            if (mats[i] == null || !mats[i].HasProperty("_Color")) continue;
            Color c = mats[i].color;
            float score = c.r - Mathf.Max(c.g, c.b);
            if (score > bestScore) { bestScore = score; best = i; }
        }
        return best;
    }

    /// <summary>World-space centre of one material slot</summary>
    static Vector3 SubmeshCentroid(Renderer r, int slot)
    {
        Mesh m = MeshOf(r);
        if (m == null || slot < 0 || slot >= m.subMeshCount) return r.bounds.center;

        int[] tris = m.GetTriangles(slot);
        Vector3[] verts = m.vertices;
        if (tris.Length == 0) return r.bounds.center;

        Vector3 sum = Vector3.zero;
        for (int i = 0; i < tris.Length; i++) sum += verts[tris[i]];
        Vector3 local = sum / tris.Length;
        return r.transform.TransformPoint(local);
    }

    static Mesh MeshOf(Renderer r)
    {
        if (r == null) return null;
        MeshFilter mf = r.GetComponent<MeshFilter>();
        return mf != null ? mf.sharedMesh : null;
    }
}
#endif
