using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// KEEP IT LIT > Build > Asylum Hub (regenerate). F92: drafts the asylum the player walks through between mazes from the
/// Karlheinz Asylum pack's finished building (Assets/Abandoned_Asylum/Prefabs/Asylum.prefab, fully unpacked), fixes it for
/// this project (URP materials, realtime lights within budget, trimmed colliders, static flags) and saves the result as a
/// hand-editable prefab, Assets/Prefabs/AsylumHub.prefab - from then on the prefab is the source of truth, exactly like
/// the Intake. BuildSilently() only places it when the scene lacks the hub; the menu item is a confirmed reset.
///
/// Placed at (100, 5, -50): the empty south-east quadrant, clear of the maze, the shop, the Intake and the galleries
/// and outside the maze's NavMesh volume. The ground storey floor sits at y 5 like every other room. Spec:
/// plannings/asylum-hub-plan.md.
/// </summary>
public static partial class AsylumHubBuilder
{
    public const string PrefabPath = "Assets/Prefabs/AsylumHub.prefab";
    private const string PackPrefabPath = "Assets/Abandoned_Asylum/Prefabs/Asylum.prefab";
    private const string RootName = "AsylumHub";

    private static readonly Vector3 HubPosition = new Vector3(100f, 5f, -50f);

    /// <summary>Realtime lights allowed in the whole hub (the Audit enforces it), and how many may cast shadows.</summary>
    public const int LightCap = 56;
    public const int ShadowCap = 6;
    // Pack lights are culled to this many, leaving room for the wing-door beacons and the builder's own lights.
    private const int PackLightTarget = 44;

    private static readonly Color Sodium = new Color(1f, 0.82f, 0.62f);
    private static readonly Color Clinical = new Color(0.75f, 0.9f, 1f);

    private static Transform _building;

    // ---------------------------------------------------------------- ProjectSetup entry points

    /// <summary>Null when the scene has the hub prefab with every part the component needs.</summary>
    public static string Problem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null
                ? "missing from the scene (Build Missing Pieces places the saved prefab)"
                : "missing (the run skips the asylum and goes straight into the mazes)";
        }
        if (!hub.IsComplete) return "incomplete";
        if (!PrefabUtility.IsPartOfPrefabInstance(hub.gameObject)) return $"not a prefab yet (Build Missing Pieces saves it as {PrefabPath})";
        return null;
    }

    /// <summary>The layout checks on the hub as it is now - hand edits included. Report only.</summary>
    public static string LayoutProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        return hub == null ? null : AuditProblems(hub.transform);
    }

    /// <summary>ProjectSetup's fix: place the saved prefab when the hub is missing, or save a plain scene hub as the prefab.</summary>
    public static void Repair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) BuildSilently();
        else if (!PrefabUtility.IsPartOfPrefabInstance(hub.gameObject)) SaveAsPrefab(hub.gameObject);
    }

    /// <summary>
    /// Generates the hub from scratch and saves it over the prefab - a reset, not the way to change the hub. To change it,
    /// open Assets/Prefabs/AsylumHub.prefab and edit it by hand.
    /// </summary>
    [MenuItem("KEEP IT LIT/Build/Asylum Hub (regenerate)", priority = 106)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Build Asylum Hub", "Open the game scene first.", "OK");
            return;
        }

        bool hasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        if ((hasPrefab || Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include) != null) &&
            !EditorUtility.DisplayDialog("Regenerate Asylum Hub",
                $"This throws away the current hub and regenerates it from code, overwriting {PrefabPath}.\n\n" +
                "Every change you made to it by hand will be lost. To change the hub, edit the prefab instead.",
                "Regenerate", "Cancel"))
        {
            return;
        }

        Generate();
    }

    /// <summary>
    /// No dialogs, never overwrites hand work: when the prefab exists and the hub is missing, it places the prefab; it
    /// only generates from code when there is no prefab yet.
    /// </summary>
    public static void BuildSilently()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("Build Asylum Hub: open the game scene first.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            if (Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include) == null)
            {
                InstantiatePrefab();
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return;
        }

        Generate();
    }

    /// <summary>Regenerates without the confirmation dialog (scripted rebuilds and tests). Overwrites the prefab.</summary>
    public static void RegenerateSilently()
    {
        Generate();
    }

    // ---------------------------------------------------------------- generation

    /// <summary>
    /// Three pack renderers (the seclusion cells' floor and "facing" shells) ship with an empty material slot, which Unity fills
    /// with the pink Built-in default. They get the morgue's tiles instead.
    /// </summary>
    private static void FixMissingMaterials(Transform building)
    {
        Material floor = FindPackMaterial("Tiles_7");
        Material facing = FindPackMaterial("Tiles_10");
        foreach (Renderer r in building.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != null && mats[i].name != "Default-Material") continue;
                Material replacement = r.name.EndsWith("_floor") ? floor : facing;
                if (replacement == null) continue;
                mats[i] = replacement;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    private static Material FindPackMaterial(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets(name + " t:Material", new[] { AsylumMaterialUpgrade.AsylumFolder }))
        {
            Material m = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));
            if (m != null && m.name == name) return m;
        }
        return null;
    }

    private static void Generate()
    {
        Scene scene = SceneManager.GetActiveScene();
        AsylumHub existing = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);
        GameObject stale;
        while ((stale = GameObject.Find(RootName)) != null) Undo.DestroyObjectImmediate(stale);

        GameObject pack = AssetDatabase.LoadAssetAtPath<GameObject>(PackPrefabPath);
        if (pack == null)
        {
            Debug.LogError($"Build Asylum Hub: {PackPrefabPath} is missing - import the Karlheinz Asylum pack (Assets/Abandoned_Asylum) first.");
            return;
        }

        if (AsylumMaterialUpgrade.NeedsConversion()) AsylumMaterialUpgrade.Convert();

        // ---- root and building
        GameObject rootGo = new GameObject(RootName);
        Undo.RegisterCreatedObjectUndo(rootGo, "Build Asylum Hub");
        rootGo.transform.position = HubPosition;

        GameObject building = (GameObject)PrefabUtility.InstantiatePrefab(pack);
        PrefabUtility.UnpackPrefabInstance(building, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        building.name = "Building";
        building.transform.SetParent(rootGo.transform, false);
        building.transform.localPosition = Vector3.zero;
        building.transform.localRotation = Quaternion.identity;
        _building = building.transform;
        FixMissingMaterials(_building);
        Physics.SyncTransforms();

        AsylumHub hub = rootGo.AddComponent<AsylumHub>();
        hub.building = _building;
        hub.dressing = Group(rootGo.transform, "Dressing");
        hub.lights = Group(rootGo.transform, "Lights");
        hub.points = Group(rootGo.transform, "Points");
        hub.wings = Group(rootGo.transform, "Wings");

        // ---- the pack building made fit for this project
        TrimColliders(_building);
        Physics.SyncTransforms();
        Transform entrance = BuildPoints(hub);
        FixStairs(_building);
        ProcessLights(_building, entrance.position);
        MarkStatic(_building);

        BuildWings(hub);
        OpenRouteDoors(hub);
        BuildAmbienceAndDust(hub);
        Dress(hub);
        WorldSignBuilder.RestyleHubInstance(hub, false, false); // F109: restyle the signs; the room plates come from Build Missing Pieces once the NavMesh exists

        EditorSceneManager.MarkSceneDirty(scene);
        SaveAsPrefab(rootGo);
        AsylumMaterialUpgrade.CapTextures(new[] { PrefabPath });
        Debug.Log($"Asylum hub built. Save the scene (Ctrl+S) to keep it.\n{Audit(rootGo.transform)}", rootGo);
    }

    // ---------------------------------------------------------------- colliders (D8)

    private static readonly string[] ArchitectureTokens = { "Wall", "Floor", "Stair", "Ceiling", "Frame", "Pillar", "Ground", "Dome" };

    private static void TrimColliders(Transform building)
    {
        int stripped = 0, kept = 0;
        foreach (Collider c in building.GetComponentsInChildren<Collider>(true))
        {
            if (c.GetComponentInParent<Rigidbody>() != null) Object.DestroyImmediate(c.GetComponentInParent<Rigidbody>());

            Transform t = c.transform;
            bool inDecals = false;
            for (Transform p = t; p != null && p != building; p = p.parent)
            {
                if (p.name == "Decals_All") { inDecals = true; break; }
            }

            Vector3 size = c.bounds.size;
            float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
            bool architecture = longest > 2.5f || ArchitectureTokens.Any(token => t.name.Contains(token) || (t.parent != null && t.parent.name.Contains(token)));

            if (inDecals || (!architecture && longest < 0.35f))
            {
                Object.DestroyImmediate(c);
                stripped++;
            }
            else
            {
                kept++;
            }
        }
        Debug.Log($"Asylum colliders: kept {kept}, stripped {stripped}.");
    }

    // ---------------------------------------------------------------- statics (D10)

    private static void MarkStatic(Transform building)
    {
        // Not BatchingStatic: the content starts inactive and static batching of inactive objects is unreliable.
        const StaticEditorFlags flags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic |
                                        StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
        foreach (Transform t in building.GetComponentsInChildren<Transform>(true))
        {
            bool visual = t.GetComponent<Renderer>() != null || t.GetComponent<Collider>() != null;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, visual ? flags : 0);
        }
    }

    // ---------------------------------------------------------------- lights (D7)

    private static readonly string[] ClinicalRooms = { "Surgery", "ExamRoom", "Morgue", "MachineRoom", "SeclusionRoomA", "SeclusionRoomB", "Bathroom", "ToiletsMan", "ToiletsWoman", "ToiletsManA", "ToiletsWomanA", "KitchenA", "Kitchen" };

    private static void ProcessLights(Transform building, Vector3 entrance)
    {
        List<Light> all = building.GetComponentsInChildren<Light>(true).ToList();
        int before = all.Count;
        foreach (Light l in all)
        {
            l.lightmapBakeType = LightmapBakeType.Realtime;
            l.shadows = LightShadows.None;
            l.enabled = true;
        }

        // Per room group: keep at most `cap` lights, chosen to spread out (strongest first, then each next one the farthest from the
        // chosen set, storeys weighted apart so an upstairs lamp never stands in for one below it). Accent lights (the pack's loose
        // PointLights_All) with a tiny range are fixture glows and go. Route rooms keep more; rooms nobody walks through keep one.
        Dictionary<string, List<Light>> groups = new Dictionary<string, List<Light>>();
        foreach (Light l in all)
        {
            string key = GroupKey(building, l.transform);
            if (key == "PointLights_All" && l.range < 3f) { Object.DestroyImmediate(l); continue; }
            if (!groups.TryGetValue(key, out List<Light> list)) groups[key] = list = new List<Light>();
            list.Add(l);
        }

        List<Light> kept = new List<Light>();
        foreach (KeyValuePair<string, List<Light>> group in groups)
        {
            int cap = LightCapFor(group.Key);
            List<Light> chosen = new List<Light>();
            List<Light> pool = group.Value.OrderByDescending(l => l.intensity * l.range).ToList();
            while (chosen.Count < cap && pool.Count > 0)
            {
                Light pick = chosen.Count == 0
                    ? pool[0]
                    : pool.OrderByDescending(l => chosen.Min(c => SpreadDistance(c, l))).First();
                chosen.Add(pick);
                pool.Remove(pick);
            }
            foreach (Light dropped in pool) Object.DestroyImmediate(dropped);
            kept.AddRange(chosen);
        }

        // Colour, a floor on strength (the pack baked values were meant for lightmaps), and range.
        foreach (Light l in kept)
        {
            string key = GroupKey(building, l.transform);
            bool clinical = ClinicalRooms.Any(room => RoomBounds(building, room, 1.0f).Contains(l.transform.position));
            l.type = LightType.Point;
            l.color = clinical ? Clinical : Sodium;
            l.intensity = Mathf.Clamp(l.intensity, 1f, 1.3f) * HubLightIntensity; // the pack morgue lamp was 2.6: the tiles went neon
            l.range = Mathf.Clamp(l.range * 1.25f, HubLightRange, 12f);
            l.renderMode = LightRenderMode.Auto;
        }

        // Six shadow casters: the entrance hall, the main stairs, the day room, the morgue, the machine room, the upper hall.
        Vector3 hubOrigin = HubPosition;
        (string, Vector3)[] spots =
        {
            ("entrance hall", entrance + Vector3.up * 3f),
            ("main stairs", hubOrigin + new Vector3(0f, 3.5f, 4f)),
            ("day room", RoomBounds(building, "DayRoom", 0f).center),
            ("morgue", RoomBounds(building, "Morgue", 0f).center),
            ("machine room", RoomBounds(building, "MachineRoom", 0f).center),
            ("upper hall", RoomBounds(building, "HallUpstair", 0f).center),
        };
        HashSet<Light> casters = new HashSet<Light>();
        foreach ((string, Vector3) spot in spots)
        {
            Light pick = kept.Where(k => !casters.Contains(k)).OrderBy(k => Vector3.Distance(k.transform.position, spot.Item2)).FirstOrDefault();
            if (pick == null) continue;
            pick.shadows = LightShadows.Soft;
            casters.Add(pick);
        }

        Debug.Log($"Asylum lights: {before} in the pack, {kept.Count} kept (realtime), {casters.Count} cast shadows.");
    }

    // Tuned by eye in Play (screenshots): the pack values were baked-lightmap numbers, far too weak for realtime in a dark hub.
    private const float HubLightIntensity = 2.8f;
    private const float HubLightRange = 7f;

    /// <summary>The room a pack light belongs to: its top-level room object under a storey group, or PointLights_All.</summary>
    private static string GroupKey(Transform building, Transform light)
    {
        Transform t = light;
        while (t.parent != null && t.parent.parent != building && t.parent != building) t = t.parent;
        return t.parent != null && t.parent.name == "PointLights_All" ? "PointLights_All" : t.name;
    }

    private static int LightCapFor(string room)
    {
        switch (room)
        {
            case "HallDownstair": return 10;
            case "HallUpstair": return 8;
            case "HallUnderground": return 4;
            case "DayRoom":
            case "Morgue":
            case "MachineRoom": return 2;
            case "PointLights_All": return 6;
            // Rooms the routes never cross may stay near-black (the torch finds them).
            case "ToiletsManA":
            case "ToiletsWomanA":
            case "ToiletsMan":
            case "ToiletsWoman":
            case "KitchenStock":
            case "Bathroom": return 0;
            default: return 1;
        }
    }

    /// <summary>Distance between two lights with the vertical axis weighted, so different storeys count as far apart.</summary>
    private static float SpreadDistance(Light a, Light b)
    {
        Vector3 d = a.transform.position - b.transform.position;
        d.y *= 3f;
        return d.magnitude;
    }

    /// <summary>Union of the renderer bounds of the first object called `name` under the building, grown by `margin`.</summary>
    private static Bounds RoomBounds(Transform building, string name, float margin)
    {
        Transform room = building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);
        if (room == null) return new Bounds(Vector3.one * -9999f, Vector3.zero);
        Renderer[] renderers = room.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return new Bounds(room.position, Vector3.zero);
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        b.Expand(margin * 2f);
        return b;
    }

    // ---------------------------------------------------------------- helpers

    private static Transform Group(Transform parent, string name)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    private static Transform Point(Transform parent, string name, Vector3 world, float yaw)
    {
        Transform t = Group(parent, name);
        t.position = world;
        t.rotation = Quaternion.Euler(0f, yaw, 0f);
        return t;
    }

    private static void EnsureFolderPath(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        EnsureFolderPath(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
