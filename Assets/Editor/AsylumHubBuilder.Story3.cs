using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F93 G5: the set pieces between the wings - the lift (floor 2), the flood (floor 3) and the chase (floor 4). Added to the existing hub
/// by <see cref="AddG5"/> (called from AddStoryLayer), build-missing-only: each piece exists once its named child under AsylumHub/Life does.
/// The pack's lift car, shaft doors and stairs are probed by name and bounds, never by assumed pivots.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string WaterMatPath = "Assets/SourceFiles/Materials/Asylum/Asylum_Water.mat";
    private const string WaterNormalPath = "Assets/SourceFiles/Textures/Asylum/WaterNormal.png";
    private const string PackModels = "Assets/Abandoned_Asylum/Models/";

    private static List<string> G5Missing(Transform life)
    {
        List<string> missing = new List<string>();
        foreach (string n in new[] { "LiftSet", "FloodSet", "ChaseSet" })
        {
            if (life == null || life.Find(n) == null) missing.Add("Life/" + n);
        }
        return missing;
    }

    private static void AddG5(AsylumHub hub, Transform life, Transform pursuer, System.Text.StringBuilder report)
    {
        Physics.SyncTransforms();
        if (life.Find("LiftSet") == null) BuildLiftSet(hub, life, report);
        if (life.Find("FloodSet") == null) BuildFloodSet(hub, life, report);
        if (life.Find("ChaseSet") == null && pursuer != null) BuildChaseSet(hub, life, pursuer, report);
    }

    private static Transform FindBuilding(string name) => _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    // ---------------------------------------------------------------- lift

    /// <summary>A wire-mesh panel model fitted to width x height (thin side along the local Z), seated on the floor at `position`.</summary>
    private static GameObject Panel(Transform parent, string name, Vector3 position, float yaw, float width, float height)
    {
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(PackModels + "Window_Barrier.fbx");
        if (model == null) { Debug.LogError("Asylum story layer: Window_Barrier.fbx is missing; the gate has no mesh."); return new GameObject(name); }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(model, parent);
        go.name = name;
        StripPhysics(go);
        go.transform.rotation = Quaternion.identity;
        go.transform.localScale = Vector3.one;
        Bounds b = RendBounds(go);
        bool thinX = b.size.x < b.size.z;
        if (thinX) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
        b = RendBounds(go);
        Vector3 scale = new Vector3(width / Mathf.Max(0.01f, b.size.x), height / Mathf.Max(0.01f, b.size.y), 1f);
        go.transform.localScale = Vector3.Scale(go.transform.localScale, scale);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
        b = RendBounds(go);
        go.transform.position += position - new Vector3(b.center.x, b.min.y, b.center.z);
        return go;
    }

    private static void BuildLiftSet(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        Transform car = FindBuilding("Elevator");
        Transform neon = FindBuilding("NeonLift");
        Transform upper = FindBuilding("Elevator_Door");
        Transform lower = FindBuilding("Elevator_Door (1)");
        if (car == null || upper == null || lower == null)
        {
            Debug.LogError("Asylum story layer: the pack lift (Elevator, Elevator_Door, Elevator_Door (1)) was not found; no lift set piece.");
            return;
        }

        Transform set = Group(life, "LiftSet");
        float groundY = hub.mainEntrance.position.y - 0.07f;

        // The closed shaft door at ground level: a clone of the upper-floor door.
        GameObject ground = Object.Instantiate(upper.gameObject, set);
        ground.name = "GroundShaftDoor";
        ground.transform.position = new Vector3(upper.position.x, groundY - 0.05f, upper.position.z);
        ground.transform.rotation = upper.rotation;
        ground.transform.localScale = upper.lossyScale;

        // The call point in front of it.
        GameObject call = new GameObject("CallPoint");
        call.transform.SetParent(set, false);
        call.transform.position = new Vector3(ground.transform.position.x, groundY + 1.1f, ground.transform.position.z - 0.65f);
        BoxCollider cb = call.AddComponent<BoxCollider>();
        cb.isTrigger = true;
        cb.size = new Vector3(1.8f, 2.3f, 1.3f);
        LiftPoint callPoint = call.AddComponent<LiftPoint>();
        callPoint.role = LiftPoint.Role.Call;

        // The point to force the stuck basement doors (inside the car, in front of them).
        GameObject pryGo = new GameObject("PryPoint");
        pryGo.transform.SetParent(set, false);
        pryGo.transform.position = new Vector3(lower.position.x, lower.position.y + 1.2f, lower.position.z - 0.55f);
        BoxCollider pb = pryGo.AddComponent<BoxCollider>();
        pb.isTrigger = true;
        pb.size = new Vector3(1.8f, 2.3f, 1.4f);
        LiftPoint pryPoint = pryGo.AddComponent<LiftPoint>();
        pryPoint.role = LiftPoint.Role.Pry;

        // The red emergency light inside the car (off; under Life so HubDecay leaves it alone; the piece moves it with the car).
        GameObject redGo = new GameObject("RedLight");
        redGo.transform.SetParent(set, false);
        redGo.transform.position = new Vector3(car.position.x, groundY + 2.1f, car.position.z + 0.2f);
        Light red = redGo.AddComponent<Light>();
        red.type = LightType.Point;
        red.color = new Color(1f, 0.08f, 0.04f);
        red.intensity = 1.8f;
        red.range = 5f;
        red.shadows = LightShadows.None;
        red.enabled = false;

        // The gate across the mouth of the basement stairs (floor 2) and its toppled variant (floor 3+).
        Vector3 mouth = new Vector3(117.35f, groundY, -56.7f);
        GameObject gate = new GameObject("Gate");
        gate.transform.SetParent(set, false);
        gate.transform.position = mouth;
        for (int i = 0; i < 3; i++) Panel(gate.transform, "Panel" + i, mouth + new Vector3(-0.85f + i * 0.85f, 0f, 0f), 0f, 0.86f, 2.1f);
        GameObject pipe = AssetDatabase.LoadAssetAtPath<GameObject>(PackModels + "Pipe.fbx");
        if (pipe != null)
        {
            foreach (float y in new[] { 0.55f, 1.55f })
            {
                GameObject bar = (GameObject)PrefabUtility.InstantiatePrefab(pipe, gate.transform);
                bar.name = "Bar";
                StripPhysics(bar);
                bar.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                Bounds pbnd = RendBounds(bar);
                bar.transform.localScale = new Vector3(1f, 2.6f / Mathf.Max(0.01f, pbnd.size.x), 1f) * 0.5f;
                bar.transform.localScale = new Vector3(bar.transform.localScale.x, bar.transform.localScale.y, bar.transform.localScale.z);
                pbnd = RendBounds(bar);
                bar.transform.position += new Vector3(mouth.x, groundY + y, mouth.z + 0.06f) - pbnd.center;
            }
        }
        BoxCollider gc = gate.AddComponent<BoxCollider>();
        gc.center = new Vector3(0f, 1.1f, 0f);
        gc.size = new Vector3(2.7f, 2.2f, 0.3f);
        LiftPoint gatePoint = gate.AddComponent<LiftPoint>();
        gatePoint.role = LiftPoint.Role.Gate;

        GameObject broken = new GameObject("GateBroken");
        broken.transform.SetParent(set, false);
        broken.transform.position = mouth;
        Panel(broken.transform, "Fallen", mouth + new Vector3(-0.2f, 0.02f, 0.55f), 20f, 0.86f, 2.1f).transform.rotation *= Quaternion.Euler(90f, 0f, 0f);
        Panel(broken.transform, "Leaning", mouth + new Vector3(1.0f, 0f, 0.1f), 0f, 0.86f, 2.1f).transform.rotation *= Quaternion.Euler(0f, 0f, 25f);
        // the fallen one: reseat on the floor after the tipping
        foreach (Transform t in broken.transform)
        {
            Bounds tb = RendBounds(t.gameObject);
            t.position += new Vector3(0f, groundY - tb.min.y + 0.01f, 0f);
        }

        LiftSetPiece piece = set.gameObject.AddComponent<LiftSetPiece>();
        piece.floor = 2;
        SerializedObject so = new SerializedObject(piece);
        so.FindProperty("car").objectReferenceValue = car;
        SerializedProperty parts = so.FindProperty("carParts");
        parts.arraySize = neon != null ? 1 : 0;
        if (neon != null) parts.GetArrayElementAtIndex(0).objectReferenceValue = neon;
        so.FindProperty("groundDoor").objectReferenceValue = ground.transform;
        so.FindProperty("basementDoor").objectReferenceValue = lower;
        so.FindProperty("gate").objectReferenceValue = gate;
        so.FindProperty("gateBroken").objectReferenceValue = broken;
        so.FindProperty("redLight").objectReferenceValue = red;
        so.FindProperty("callPoint").objectReferenceValue = callPoint;
        so.FindProperty("pryPoint").objectReferenceValue = pryPoint;
        so.ApplyModifiedPropertiesWithoutUndo();
        foreach (LiftPoint p in new[] { callPoint, pryPoint, gatePoint })
        {
            SerializedObject ps = new SerializedObject(p);
            ps.FindProperty("owner").objectReferenceValue = piece;
            ps.ApplyModifiedPropertiesWithoutUndo();
        }

        gate.SetActive(false);
        broken.SetActive(false);
        report.AppendLine($"LiftSet: car {car.position}, shaft door at ground {ground.transform.position}, gate at {mouth}");
    }

    // ---------------------------------------------------------------- flood

    private static Material EnsureWaterMaterial()
    {
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(WaterMatPath);
        if (existing != null) return existing;
        EnsureFolderPath("Assets/SourceFiles/Textures/Asylum");

        // A small tileable ripple normal map (sum of periodic waves), saved once.
        Texture2D normal = AssetDatabase.LoadAssetAtPath<Texture2D>(WaterNormalPath);
        if (normal == null)
        {
            const int n = 128;
            float[,] h = new float[n, n];
            (int fx, int fy, float amp, float ph)[] waves = { (3, 2, 1f, 0.3f), (5, 7, 0.6f, 1.1f), (9, 4, 0.4f, 2.0f), (2, 11, 0.5f, 0.7f), (13, 9, 0.25f, 4f) };
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                    foreach (var w in waves) h[x, y] += w.amp * Mathf.Sin(2f * Mathf.PI * (w.fx * x + w.fy * y) / n + w.ph);
            Texture2D tex = new Texture2D(n, n, TextureFormat.RGBA32, false);
            for (int y = 0; y < n; y++)
                for (int x = 0; x < n; x++)
                {
                    float dx = h[(x + 1) % n, y] - h[(x + n - 1) % n, y], dy = h[x, (y + 1) % n] - h[x, (y + n - 1) % n];
                    Vector3 v = new Vector3(-dx * 0.18f, -dy * 0.18f, 1f).normalized;
                    tex.SetPixel(x, y, new Color(v.x * 0.5f + 0.5f, v.y * 0.5f + 0.5f, v.z * 0.5f + 0.5f, 1f));
                }
            System.IO.File.WriteAllBytes(WaterNormalPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(WaterNormalPath);
            TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(WaterNormalPath);
            imp.textureType = TextureImporterType.NormalMap;
            imp.wrapMode = TextureWrapMode.Repeat;
            imp.mipmapEnabled = true;
            imp.SaveAndReimport();
            normal = AssetDatabase.LoadAssetAtPath<Texture2D>(WaterNormalPath);
        }

        Material m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Asylum_Water" };
        m.SetFloat("_Surface", 1f);
        m.SetFloat("_Blend", 0f);
        m.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        m.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_SrcBlendAlpha", (float)UnityEngine.Rendering.BlendMode.One);
        m.SetFloat("_DstBlendAlpha", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        m.SetFloat("_ZWrite", 0f);
        m.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        m.SetOverrideTag("RenderType", "Transparent");
        // Project gotcha: URP re-validates to premultiplied alpha unless this is 0.
        if (m.HasProperty("_BlendModePreserveSpecular")) m.SetFloat("_BlendModePreserveSpecular", 0f);
        m.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        m.SetColor("_BaseColor", new Color(0.045f, 0.075f, 0.08f, 0.985f));
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", 0.85f); // documented exception to the hub's 0.35 clamp: the torch should glint on the water
        m.EnableKeyword("_NORMALMAP");
        m.SetTexture("_BumpMap", normal);
        m.SetFloat("_BumpScale", 0.6f);
        AssetDatabase.CreateAsset(m, WaterMatPath);
        return m;
    }

    private static void BuildFloodSet(AsylumHub hub, Transform life, System.Text.StringBuilder report)
    {
        Bounds hall = RoomBounds(_building, "HallUnderground", 0f);
        if (hall.size == Vector3.zero) { Debug.LogError("Asylum story layer: HallUnderground was not found; no flood set piece."); return; }
        float floorY = FloorHeight("HallUnderground", hall);
        float waterY = floorY + 0.25f;
        Transform set = Group(life, "FloodSet");

        // The zone: the hall's footprint, floor to head height.
        GameObject zoneGo = new GameObject("Zone");
        zoneGo.transform.SetParent(set, false);
        zoneGo.transform.position = new Vector3(hall.center.x, floorY + 1.0f, hall.center.z);
        BoxCollider zone = zoneGo.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(hall.size.x, 2.4f, hall.size.z);

        // The water: one quad over the hall.
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "Water";
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.transform.SetParent(set, false);
        quad.transform.position = new Vector3(hall.center.x, waterY, hall.center.z);
        quad.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        quad.transform.localScale = new Vector3(hall.size.x, hall.size.z, 1f);
        Renderer wr = quad.GetComponent<Renderer>();
        wr.sharedMaterial = EnsureWaterMaterial();
        wr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        wr.receiveShadows = false;

        // Floating things: paper, cans and a sheeted body, scattered along the route.
        Transform group = Group(set, "Floaters");
        GameObject paper = AssetDatabase.LoadAssetAtPath<GameObject>(PackPaper);
        GameObject can = AssetDatabase.LoadAssetAtPath<GameObject>(PackModels + "CanOld.fbx");
        GameObject body = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/DeadBody LITE/Prefabs/Body_Only/DeadBody (2).prefab");
        Material sheet = AssetDatabase.LoadAssetAtPath<Material>("Assets/SourceFiles/Materials/Asylum/Asylum_Sheet.mat");
        (GameObject prefab, Vector2 xz, float yaw, float scale)[] items =
        {
            (paper, new Vector2(115.2f, -53.1f), 20f, 1.8f), (paper, new Vector2(117.6f, -53.9f), 140f, 1.8f), (paper, new Vector2(120.2f, -54.2f), 75f, 1.8f),
            (paper, new Vector2(122.6f, -54.6f), 200f, 1.8f), (paper, new Vector2(118.5f, -56.8f), 310f, 1.8f),
            (can, new Vector2(116.4f, -54.0f), 0f, 1.3f), (can, new Vector2(121.4f, -55.2f), 90f, 1.3f),
            (body, new Vector2(119.0f, -60.5f), 100f, 1f),
        };
        int made = 0;
        foreach (var item in items)
        {
            if (item.prefab == null) continue;
            Vector3 p = new Vector3(item.xz.x, waterY, item.xz.y);
            if (Physics.CheckSphere(p + Vector3.up * 0.25f, 0.28f, ~0, QueryTriggerInteraction.Ignore) && item.prefab != body) continue; // not inside a wall or a prop
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(item.prefab, group);
            go.name = item.prefab.name + "_Float";
            StripPhysics(go);
            go.transform.rotation = Quaternion.Euler(0f, item.yaw, 0f);
            go.transform.localScale *= item.scale;
            if (item.prefab == body && sheet != null)
            {
                foreach (Renderer r in go.GetComponentsInChildren<Renderer>(true)) { Material[] ms = new Material[r.sharedMaterials.Length]; for (int i = 0; i < ms.Length; i++) ms[i] = sheet; r.sharedMaterials = ms; }
                go.transform.localScale = new Vector3(go.transform.localScale.x * 1.25f, go.transform.localScale.y * 1.6f, go.transform.localScale.z * 1.25f);
            }
            Bounds b = RendBounds(go);
            go.transform.position += new Vector3(p.x - b.center.x, waterY - b.min.y - (item.prefab == body ? 0.12f : 0.01f), p.z - b.center.z);
            go.SetActive(false);
            made++;
        }

        FloodSetPiece piece = set.gameObject.AddComponent<FloodSetPiece>();
        piece.floor = 3;
        SerializedObject so = new SerializedObject(piece);
        so.FindProperty("water").objectReferenceValue = quad.transform;
        so.FindProperty("zone").objectReferenceValue = zone;
        SerializedProperty arr = so.FindProperty("floaters");
        arr.arraySize = group.childCount;
        for (int i = 0; i < group.childCount; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = group.GetChild(i);
        so.ApplyModifiedPropertiesWithoutUndo();
        quad.SetActive(false);
        report.AppendLine($"FloodSet: water at y {waterY} over {hall.size.x:F1} x {hall.size.z:F1} m, {made} floaters");
    }

    // ---------------------------------------------------------------- chase

    private static void BuildChaseSet(AsylumHub hub, Transform life, Transform pursuer, System.Text.StringBuilder report)
    {
        Transform set = Group(life, "ChaseSet");
        float groundY = hub.mainEntrance.position.y;

        GameObject top = new GameObject("TopZone");
        top.transform.SetParent(set, false);
        top.transform.position = new Vector3(117.35f, groundY + 1.1f, -55.5f);
        BoxCollider tz = top.AddComponent<BoxCollider>();
        tz.isTrigger = true;
        tz.size = new Vector3(3.2f, 2.4f, 2.2f);

        Bounds surgery = RoomBounds(_building, "Surgery", 0f);
        GameObject end = new GameObject("EndZone");
        end.transform.SetParent(set, false);
        end.transform.position = new Vector3(surgery.center.x, surgery.min.y + 1.4f, surgery.center.z);
        BoxCollider ez = end.AddComponent<BoxCollider>();
        ez.isTrigger = true;
        ez.size = new Vector3(Mathf.Max(2f, surgery.size.x - 1.8f), 2.8f, Mathf.Max(2f, surgery.size.z - 1.6f));

        Vector3 riseWanted = new Vector3(117.2f, 1.2f, -64.6f);
        Vector3 rise = SampleHub(riseWanted, 3f, out Vector3 onMesh) ? onMesh : riseWanted;
        Transform riseSpot = Point(set, "RiseSpot", rise, 0f); // faces +Z, up the stairs

        ChaseSetPiece piece = set.gameObject.AddComponent<ChaseSetPiece>();
        piece.floor = 4;
        SerializedObject so = new SerializedObject(piece);
        so.FindProperty("pursuer").objectReferenceValue = pursuer.GetComponent<HubStalker>();
        so.FindProperty("riseSpot").objectReferenceValue = riseSpot;
        so.FindProperty("topZone").objectReferenceValue = tz;
        so.FindProperty("endZone").objectReferenceValue = ez;
        so.ApplyModifiedPropertiesWithoutUndo();
        report.AppendLine($"ChaseSet: rise spot {rise}, top zone {top.transform.position}, surgery zone {end.transform.position}");
    }
}
