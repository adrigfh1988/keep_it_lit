using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F117: A HOT MEAL. The first draft of AsylumHub/Meal, written into the hand-edited hub prefab (<see cref="ApplyHubToPrefab"/>): a dark tray with
/// three dishes (broth, black bread, cold tea) on the kitchen table that faces the kitchen's door (Building/1stFloor/KitchenA, the table at
/// (78.88, -48.42)), steam over the broth, a small warm light. Whatever lay on that table top is removed, each by name and logged (the two
/// HubMissionItems stand on the neighbouring tables and are never touched). <see cref="HubMeal"/> / <see cref="HubMealDish"/> do the rest at runtime.
/// Build-missing-only; <see cref="RebuildMeal"/> is the development reset.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string MealGroupName = "Meal";
    private const string AlchDishes = "Assets/BK_AlchemistHouse/Prefabs/Items/Dishes/";
    private static readonly Vector3 MealTableAt = new Vector3(78.88f, 5.03f, -48.42f);
    private const string MealTrayMaterial = "Assets/SourceFiles/Materials/Asylum/MealTray.mat";
    private const string MealBreadMaterial = "Assets/SourceFiles/Materials/Asylum/MealBread.mat";
    private const string SteamMaterialPath = "Assets/SourceFiles/Materials/Asylum/HubSteam.mat";

    /// <summary>ProjectSetup piece: null when there is no hub (that piece reports it) or the kitchen's meal is complete.</summary>
    public static string MealProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null;
        if (hub.meal == null || !hub.meal.IsComplete) return "missing: AsylumHub/Meal (the three dishes on the kitchen table)";
        return null;
    }

    /// <summary>ProjectSetup's build: the kitchen's tray and dishes, applied to the prefab. Marks the scene dirty; the caller saves.</summary>
    public static void BuildMeal() => BuildMealCore(false);

    /// <summary>Development only: tears AsylumHub/Meal down and builds it again (props already removed stay removed). Not a menu item.</summary>
    public static void RebuildMeal() => BuildMealCore(true);

    private static void BuildMealCore(bool force)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Asylum meal: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        if (hub.building == null || hub.dressing == null) { Debug.LogError("Asylum meal: the hub has no Building / Dressing group."); return; }
        if (!force && MealProblem() == null) { Debug.Log("Asylum meal: the dishes are already there."); return; }

        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        StringBuilder log = new StringBuilder();

        Renderer tableR = NearestRenderer("TableKitchen", MealTableAt + Vector3.up * 0.4f, 0.7f);
        if (tableR == null) { Debug.LogError("Asylum meal: the kitchen table at " + MealTableAt + " is gone."); if (!wasActive) hub.SetContentActive(false); return; }
        Bounds table = tableR.bounds;
        float top = table.max.y;

        // ---- 1. what lies on the table top goes (through the prefab asset: an instance may not delete its prefab's children)
        List<Transform> gone = new List<Transform>();
        Transform oldGroup = hub.transform.Find(MealGroupName);
        if (oldGroup != null) { gone.Add(oldGroup); log.AppendLine("removed " + MealGroupName + " (rebuilt)"); }
        foreach (Transform room in new[] { hub.building.Find("1stFloor/KitchenA"), hub.dressing.Find("KitchenA") })
        {
            if (room == null) continue;
            foreach (Transform t in room)
            {
                if (t == tableR.transform || t.GetComponentInParent<HubMissionItem>() != null || t.GetComponentInChildren<HubMissionItem>(true) != null) continue;
                Renderer[] rs = t.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer)).ToArray();
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds; foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                bool onTop = b.min.y > top - 0.06f && b.min.y < top + 0.1f && b.center.x > table.min.x + 0.02f && b.center.x < table.max.x - 0.02f && b.center.z > table.min.z && b.center.z < table.max.z;
                if (!onTop) continue;
                gone.Add(t);
                log.AppendLine($"removed {AnimationUtility.CalculateTransformPath(t, hub.transform)} at {t.position:F2} (lay on the serving table)");
            }
        }
        // mission items near the table are only reported, never touched
        foreach (HubMissionItem item in hub.GetComponentsInChildren<HubMissionItem>(true))
        {
            if (Vector3.Distance(item.transform.position, MealTableAt) < 2.6f) log.AppendLine($"kept mission item {item.name} at {item.transform.position:F2}");
        }
        if (gone.Count > 0)
        {
            RemoveFromHub(hub, gone);
            hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
            hub.SetContentActive(true);
            _building = hub.building;
            tableR = NearestRenderer("TableKitchen", MealTableAt + Vector3.up * 0.4f, 0.7f);
            Physics.SyncTransforms();
        }

        // ---- 2. the group, the tray, the dishes
        GameObject groupGo = new GameObject(MealGroupName);
        groupGo.transform.SetParent(hub.transform, false);
        groupGo.transform.position = new Vector3(MealTableAt.x, top, MealTableAt.z);
        groupGo.transform.rotation = Quaternion.identity;
        HubMeal meal = groupGo.AddComponent<HubMeal>();
        Transform g = groupGo.transform;

        EnsureFolderPath(MaterialFolder);
        GameObject tray = GameObject.CreatePrimitive(PrimitiveType.Cube);
        tray.name = "Tray";
        Object.DestroyImmediate(tray.GetComponent<BoxCollider>());
        tray.transform.SetParent(g, false);
        tray.transform.localScale = new Vector3(1.62f, 0.022f, 0.58f);
        tray.transform.position = new Vector3(MealTableAt.x, top + 0.011f, MealTableAt.z);
        tray.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("MealTray", new Color(0.16f, 0.16f, 0.17f), 0.45f);
        float trayTop = top + 0.022f;

        float[] xs = { -0.5f, 0f, 0.5f };
        HubMealDish[] dishes = new HubMealDish[3];
        List<ParticleSystem> steam = new List<ParticleSystem>();
        for (int i = 0; i < 3; i++)
        {
            HubMealDish.Dish kind = (HubMealDish.Dish)i;
            Vector3 at = new Vector3(MealTableAt.x + xs[i], trayTop, MealTableAt.z);
            GameObject dishGo = new GameObject(kind.ToString());
            dishGo.transform.SetParent(g, false);
            dishGo.transform.position = at;
            GameObject fullGo = new GameObject("Full"), emptyGo = new GameObject("Empty");
            fullGo.transform.SetParent(dishGo.transform, false);
            emptyGo.transform.SetParent(dishGo.transform, false);
            fullGo.transform.position = at;
            emptyGo.transform.position = at;

            switch (kind)
            {
                case HubMealDish.Dish.Broth:
                    MealProp(emptyGo.transform, "ClutterBowl02", at, 0f);
                    MealProp(fullGo.transform, "ClutterBowl03Ing05", at, 0f, ingredient: true);
                    break;
                case HubMealDish.Dish.Bread:
                    MealProp(emptyGo.transform, "ClutterPlate01", at, 0f);
                    MealProp(fullGo.transform, "ClutterPlate01", at, 0f);
                    BreadLoaf(fullGo.transform, new Vector3(at.x - 0.035f, at.y + 0.012f, at.z + 0.01f), 18f);
                    BreadLoaf(fullGo.transform, new Vector3(at.x + 0.04f, at.y + 0.012f, at.z - 0.025f), -24f);
                    break;
                default:
                    MealProp(emptyGo.transform, "ClutterGlass01", at + new Vector3(0.08f, 0f, -0.05f), 0f);
                    MealProp(fullGo.transform, "ClutterPichet01", at + new Vector3(-0.03f, 0f, 0.04f), 200f);
                    MealProp(fullGo.transform, "ClutterGlass01", at + new Vector3(0.1f, 0f, -0.06f), 0f);
                    break;
            }

            BoxCollider box = dishGo.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 0.1f, 0f);
            box.size = new Vector3(0.36f, 0.26f, 0.34f);
            HubMealDish dish = dishGo.AddComponent<HubMealDish>();
            SerializedObject so = new SerializedObject(dish);
            so.FindProperty("dish").enumValueIndex = i;
            so.FindProperty("meal").objectReferenceValue = meal;
            so.FindProperty("full").objectReferenceValue = fullGo;
            so.FindProperty("empty").objectReferenceValue = emptyGo;
            so.ApplyModifiedPropertiesWithoutUndo();
            dishes[i] = dish;

            if (kind == HubMealDish.Dish.Broth) steam.Add(BuildSteam(fullGo.transform, at + Vector3.up * 0.1f));
        }
        meal.dishes = dishes;
        meal.steam = steam.ToArray();

        // a small warm light low over the tray: it is the one thing in the kitchen that looks cared for
        GameObject lightGo = new GameObject("MealLight");
        lightGo.transform.SetParent(g, false);
        lightGo.transform.position = new Vector3(MealTableAt.x, top + 0.95f, MealTableAt.z - 0.35f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.74f, 0.44f);
        light.intensity = 0.9f;
        light.range = 2.6f;
        light.shadows = LightShadows.None;
        light.lightmapBakeType = LightmapBakeType.Realtime;
        meal.light = light;

        hub.meal = meal;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hub);
        MakeMovable(g); // nothing here is batched with the room
        meal.Refresh();
        log.AppendLine("tray, 3 dishes, steam and MealLight built under AsylumHub/Meal on the table top y " + top.ToString("F3"));
        log.AppendLine("navmesh rebake: " + HubNavMesh.Bake(hub));
        log.AppendLine(Audit(hub.transform));

        if (!wasActive) hub.SetContentActive(false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        ApplyHubToPrefab(hub);
        Debug.Log("Asylum meal (the kitchen's hot meal):\n" + log.ToString().Trim());
    }

    /// <summary>One Alchemist dish prefab (kept linked), its bounds' bottom centre on `at` and turned to `yaw`.</summary>
    private static GameObject MealProp(Transform parent, string prefabName, Vector3 at, float yaw, bool ingredient = false)
    {
        string path = (ingredient ? "Assets/BK_AlchemistHouse/Prefabs/Items/Ingredients/" : AlchDishes) + prefabName + ".prefab";
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null) { Debug.LogError("Asylum meal: " + path + " is missing."); return null; }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.Rotate(0f, yaw, 0f, Space.World); // the pack prefab keeps its own import rotation: only turn it about the world Y axis
        Bounds b = RendBounds(go);
        go.transform.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
        StripPropPhysics(go); // the dish's trigger is the only collider; a pack prop carries a Rigidbody that would fall through the floor in Play
        return go;
    }

    /// <summary>Removes the colliders and rigidbodies of a pack prop (the instance keeps its link; the removals are overrides). A Rigidbody left on a decorative prop falls through the floor in Play.</summary>
    private static void StripPropPhysics(GameObject go)
    {
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
    }

    /// <summary>A slab of black bread: a scaled cube (no collider) in a dark crust material, lying on the plate.</summary>
    private static void BreadLoaf(Transform parent, Vector3 at, float yaw)
    {
        GameObject slice = GameObject.CreatePrimitive(PrimitiveType.Cube);
        slice.name = "BreadSlice";
        Object.DestroyImmediate(slice.GetComponent<BoxCollider>());
        slice.transform.SetParent(parent, false);
        slice.transform.localScale = new Vector3(0.12f, 0.024f, 0.075f);
        slice.transform.SetPositionAndRotation(at + Vector3.up * 0.012f, Quaternion.Euler(0f, yaw, 0f));
        slice.GetComponent<Renderer>().sharedMaterial = LoadOrCreateMaterial("MealBread", new Color(0.085f, 0.055f, 0.04f), 0.15f);
    }

    /// <summary>A few slow, small puffs rising from `at`: the hub's soft fog sprite with the camera fade pulled in close. Off until the meal is on offer.</summary>
    private static ParticleSystem BuildSteam(Transform parent, Vector3 at)
    {
        GameObject go = new GameObject("Steam");
        go.transform.SetParent(parent, false);
        go.transform.position = at;
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        ParticleSystem.MainModule main = ps.main;
        main.duration = 5f;
        main.loop = true;
        main.playOnAwake = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(1.6f, 2.4f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(0.12f, 0.2f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.16f, 0.26f);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, 6.28f);
        main.startColor = new Color(1f, 1f, 1f, 0.9f);
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 24;
        main.cullingMode = ParticleSystemCullingMode.Pause;

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = 8f;

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Circle;
        shape.radius = 0.06f;
        shape.rotation = new Vector3(-90f, 0f, 0f); // up

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.strength = 0.05f;
        noise.frequency = 0.8f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
        colour.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(0.7f, 0.25f), new GradientAlphaKey(0f, 1f) });
        colour.color = fade;

        ParticleSystem.SizeOverLifetimeModule size = ps.sizeOverLifetime;
        size.enabled = true;
        size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));

        ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.numTilesX = 2;
        sheet.numTilesY = 2;
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sharedMaterial = SteamMaterial();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return ps;
    }

    /// <summary>The hub fog material with camera fading and soft particles pulled in to a few centimetres (steam is seen from arm's length).</summary>
    private static Material SteamMaterial()
    {
        Material m = AssetDatabase.LoadAssetAtPath<Material>(SteamMaterialPath);
        if (m != null) return m;
        Material fog = FogMaterial();
        if (fog == null) return null;
        m = new Material(fog) { name = "HubSteam" };
        m.SetColor("_BaseColor", new Color(0.9f, 0.9f, 0.92f, 0.85f));
        m.SetColor("_EmissionColor", new Color(0.16f, 0.16f, 0.18f)); // a little glow of its own: steam seen in a dark room
        m.SetFloat("_SoftParticlesFarFadeDistance", 0.15f);
        m.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 0.15f, 0f, 0f));
        m.SetFloat("_CameraNearFadeDistance", 0.1f);
        m.SetFloat("_CameraFarFadeDistance", 0.5f);
        m.SetVector("_CameraFadeParams", new Vector4(0.1f, 1f / 0.4f, 0f, 0f));
        AssetDatabase.CreateAsset(m, SteamMaterialPath);
        return m;
    }
}
