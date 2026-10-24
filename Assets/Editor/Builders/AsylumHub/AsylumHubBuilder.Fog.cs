using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Hub fog: low drifting fog banks in every room and hall of the asylum, added to the hand-edited hub prefab without regenerating it
/// (build-missing-only, like the look pass). One particle system per room box (the same boxes as the reflection probes, so the long halls
/// get several), under Dressing/Fog so it switches with the hub content. The sprites are the "Fog Particles" pack's smoke sheet on a
/// lit, soft URP particle material (the pack's own materials are unlit Sprites/Default and would glow white in the dark): the fog is dark
/// until the torch or a lamp lights it, and fades where it meets the floor (HubRenderingLook asks the camera for the depth texture).
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string FogGroupName = "Fog";
    private const string FogMaterialPath = "Assets/SourceFiles/Materials/Asylum/HubFog.mat";
    private const string FogTexturePath = "Assets/Fog Particles/Texture/Smoke Sprite Sheet.png";

    /// <summary>Particles alive per m² of floor, and the cap per room (overdraw is the cost, on the web build most of all).</summary>
    private const float FogPerSquareMetre = 0.24f;
    private const int FogMaxPerRoom = 72;
    /// <summary>Flat walkable floor (m²) at one height before it counts as a storey the fog lies on.</summary>
    private const float FogLevelArea = 25f;

    public static Material FogMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(FogMaterialPath);
        if (mat != null) return mat;
        Texture2D tex = AssetDatabase.LoadAssetAtPath<Texture2D>(FogTexturePath);
        if (tex == null) return null;
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Simple Lit");
        if (shader == null) return null;
        mat = new Material(shader) { name = "HubFog" };
        mat.SetTexture("_BaseMap", tex);
        mat.SetColor("_BaseColor", new Color(0.7f, 0.74f, 0.8f, 0.7f));
        // a faint glow of its own: the light the haze scatters, so it reads as fog (not darkness) away from the lamps
        mat.SetColor("_EmissionColor", new Color(0.07f, 0.077f, 0.09f));
        mat.EnableKeyword("_EMISSION");
        // transparent, alpha blended, no depth write
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.SetFloat("_ReceiveShadows", 0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.EnableKeyword("_RECEIVE_SHADOWS_OFF");
        // soft particles: fade over 1.2 m where a sprite meets a floor or wall
        mat.SetFloat("_SoftParticlesEnabled", 1f);
        mat.SetFloat("_SoftParticlesNearFadeDistance", 0f);
        mat.SetFloat("_SoftParticlesFarFadeDistance", 1.2f);
        mat.SetVector("_SoftParticleFadeParams", new Vector4(0f, 1f / 1.2f, 0f, 0f));
        mat.EnableKeyword("_SOFTPARTICLES_ON");
        // camera fading: a sprite thins out as you walk into it instead of filling the screen
        mat.SetFloat("_CameraFadingEnabled", 1f);
        mat.SetFloat("_CameraNearFadeDistance", 0.6f);
        mat.SetFloat("_CameraFarFadeDistance", 2.6f);
        mat.SetVector("_CameraFadeParams", new Vector4(0.6f, 1f / (2.6f - 0.6f), 0f, 0f));
        mat.EnableKeyword("_FADING_ON");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetOverrideTag("RenderType", "Transparent");
        AssetDatabase.CreateAsset(mat, FogMaterialPath);
        return mat;
    }

    private static Transform FogGroup(AsylumHub hub, bool create)
    {
        if (hub.dressing == null) return null;
        Transform group = hub.dressing.Find(FogGroupName);
        if (group == null && create) group = Group(hub.dressing, FogGroupName);
        return group;
    }

    private const string FogMeshFolder = "Assets/SourceFiles/Meshes/AsylumFog";

    /// <summary>
    /// The hub's walkable floor (its baked NavMesh), cut per room: each triangle goes to the smallest room box that holds its centre, so a
    /// room claims its own floor before the hall wrapped around it does, and nothing lands in walls, furniture or between storeys.
    /// Rooms with no floor of their own are left out.
    /// </summary>
    private static Dictionary<ProbeDef, List<Vector3>> FogFloors(AsylumHub hub)
    {
        var boxes = ProbeDefs.Select(d => (def: d, box: ProbeBounds(hub.building, d)))
                             .Where(p => p.box.size != Vector3.zero)
                             .OrderBy(p => p.box.size.x * p.box.size.z).ToList();
        var floors = new Dictionary<ProbeDef, List<Vector3>>();
        UnityEngine.AI.NavMeshTriangulation nav = UnityEngine.AI.NavMesh.CalculateTriangulation();

        // Only the storey floors: the NavMesh also covers stages, stairs and the tops of some furniture, and fog spawned there floats at
        // window height. A storey is a height (0.2 m bins) holding at least FogLevelArea m² of flat walkable floor.
        var levelArea = new Dictionary<int, float>();
        for (int i = 0; i + 2 < nav.indices.Length; i += 3)
        {
            Vector3 a = nav.vertices[nav.indices[i]], b = nav.vertices[nav.indices[i + 1]], c = nav.vertices[nav.indices[i + 2]];
            int bin = Mathf.RoundToInt((a.y + b.y + c.y) / 3f / 0.2f);
            levelArea.TryGetValue(bin, out float sum);
            levelArea[bin] = sum + Vector3.Cross(b - a, c - a).magnitude * 0.5f;
        }
        List<float> levels = levelArea.Where(kv => kv.Value >= FogLevelArea).Select(kv => kv.Key * 0.2f).ToList();

        for (int i = 0; i + 2 < nav.indices.Length; i += 3)
        {
            Vector3 a = nav.vertices[nav.indices[i]], b = nav.vertices[nav.indices[i + 1]], c = nav.vertices[nav.indices[i + 2]];
            Vector3 centre = (a + b + c) / 3f;
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            if (Mathf.Abs(normal.y) < 0.95f) continue; // a stair or ramp
            if (!levels.Any(y => Mathf.Abs(centre.y - y) <= 0.25f)) continue; // a stage, a table top, a landing
            foreach (var p in boxes)
            {
                if (!p.box.Contains(centre)) continue;
                if (!floors.TryGetValue(p.def, out List<Vector3> tris)) floors[p.def] = tris = new List<Vector3>();
                tris.Add(a); tris.Add(b); tris.Add(c);
                break;
            }
        }
        return floors;
    }

    /// <summary>The rooms that have floor of their own and no fog yet (by name, Fog_&lt;probe name&gt;).</summary>
    private static List<ProbeDef> MissingFog(AsylumHub hub, Dictionary<ProbeDef, List<Vector3>> floors)
    {
        Transform group = FogGroup(hub, false);
        return floors.Keys.Where(d => group == null || group.Find("Fog_" + d.Name) == null).ToList();
    }

    private static float FloorArea(List<Vector3> tris)
    {
        float area = 0f;
        for (int i = 0; i + 2 < tris.Count; i += 3) area += Vector3.Cross(tris[i + 1] - tris[i], tris[i + 2] - tris[i]).magnitude * 0.5f;
        return area;
    }

    private static void BuildFogEmitter(Transform group, List<Vector3> tris, string name, Material mat)
    {
        Bounds floorBounds = new Bounds(tris[0], Vector3.zero);
        foreach (Vector3 v in tris) floorBounds.Encapsulate(v);
        Vector3 origin = new Vector3(floorBounds.center.x, floorBounds.min.y, floorBounds.center.z);

        // the room's walkable floor as the emitter's shape (a saved asset, so the prefab keeps it)
        if (!AssetDatabase.IsValidFolder(FogMeshFolder)) AssetDatabase.CreateFolder("Assets/SourceFiles/Meshes", "AsylumFog");
        string meshPath = $"{FogMeshFolder}/Fog_{name}.asset";
        Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        bool created = mesh == null;
        if (created) mesh = new Mesh();
        mesh.Clear();
        mesh.name = "Fog_" + name;
        mesh.SetVertices(tris.Select(v => v - origin).ToList());
        mesh.SetTriangles(Enumerable.Range(0, tris.Count).ToArray(), 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        if (created) AssetDatabase.CreateAsset(mesh, meshPath); else EditorUtility.SetDirty(mesh);

        GameObject go = new GameObject("Fog_" + name);
        go.transform.SetParent(group, false);
        go.transform.SetPositionAndRotation(origin, Quaternion.identity);

        int alive = Mathf.Clamp(Mathf.RoundToInt(FloorArea(tris) * FogPerSquareMetre), 4, FogMaxPerRoom);
        ParticleSystem ps = ConfigureFogParticles(go, alive, mat);
        ParticleSystem.ShapeModule shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Mesh;
        shape.meshShapeType = ParticleSystemMeshShapeType.Triangle;
        shape.mesh = mesh;
    }

    /// <summary>
    /// The ground-fog look, shared by the hub rooms and the maze template (InteractableKitBuilder's fogBank): wide flat banks that drift
    /// sideways at knee height, `alive` of them at a time. The shape is a flat Box at 0.45 m; the hub swaps it for its room's floor mesh,
    /// the maze sizes the box to each chunk of cells.
    /// </summary>
    public static ParticleSystem ConfigureFogParticles(GameObject go, int alive, Material mat)
    {
        ParticleSystem ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        const float lifeMin = 14f, lifeMax = 20f;

        ParticleSystem.MainModule main = ps.main;
        main.duration = 10f;
        main.loop = true;
        main.prewarm = true;
        main.playOnAwake = true;
        main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
        main.startSpeed = 0f; // any upward speed over a 14-20 s life lifts a bank to window height
        // wide, flat banks (a tall round puff reads as smoke from a fire), barely tilted
        main.startSize3D = true;
        main.startSizeX = new ParticleSystem.MinMaxCurve(3f, 5f);
        main.startSizeY = new ParticleSystem.MinMaxCurve(0.9f, 1.4f);
        main.startSizeZ = 1f;
        main.startRotation = new ParticleSystem.MinMaxCurve(-0.06f, 0.06f);
        main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 1f, 1f, 1f), new Color(1f, 1f, 1f, 0.55f));
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.scalingMode = ParticleSystemScalingMode.Hierarchy;
        main.maxParticles = alive + 4;
        main.cullingMode = ParticleSystemCullingMode.Pause; // off screen: nothing simulates

        ParticleSystem.EmissionModule emission = ps.emission;
        emission.rateOverTime = alive / ((lifeMin + lifeMax) * 0.5f);

        ParticleSystem.ShapeModule shape = ps.shape;
        shape.enabled = true;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(4f, 0f, 4f);
        shape.position = new Vector3(0f, 0.45f, 0f); // on the floor: the lower part sinks in and the soft edge fades it out
        shape.randomPositionAmount = 0.15f;

        ParticleSystem.NoiseModule noise = ps.noise;
        noise.enabled = true;
        noise.separateAxes = true; // drift sideways, not up
        noise.strengthX = 0.12f;
        noise.strengthY = 0.008f;
        noise.strengthZ = 0.12f;
        noise.frequency = 0.08f;
        noise.scrollSpeed = 0.04f;
        noise.quality = ParticleSystemNoiseQuality.Low;

        ParticleSystem.ColorOverLifetimeModule colour = ps.colorOverLifetime;
        colour.enabled = true;
        Gradient fade = new Gradient();
        fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                     new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.25f), new GradientAlphaKey(1f, 0.65f), new GradientAlphaKey(0f, 1f) });
        colour.color = fade;

        // the pack's sheet is 2 x 2 different puffs: each particle keeps one at random
        ParticleSystem.TextureSheetAnimationModule sheet = ps.textureSheetAnimation;
        sheet.enabled = true;
        sheet.numTilesX = 2;
        sheet.numTilesY = 2;
        sheet.startFrame = new ParticleSystem.MinMaxCurve(0f, 3.99f);
        sheet.frameOverTime = new ParticleSystem.MinMaxCurve(0f);
        sheet.cycleCount = 1;

        ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.sortMode = ParticleSystemSortMode.Distance;
        renderer.maxParticleSize = 3f;
        renderer.sharedMaterial = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
        renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        return ps;
    }

    // ---------------------------------------------------------------- ProjectSetup piece

    /// <summary>ProjectSetup piece: null when there is no hub (that piece reports it), the pack is not imported, or every room has its fog.</summary>
    public static string FogProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null || hub.building == null || hub.dressing == null) return null;
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(FogTexturePath) == null) return null;
        Dictionary<ProbeDef, List<Vector3>> floors = FogFloors(hub);
        if (floors.Count == 0) return null; // the hub NavMesh is not loaded: nothing to compare against
        List<ProbeDef> missing = MissingFog(hub, floors);
        if (missing.Count > 0) return $"{missing.Count} rooms have no fog ({string.Join(", ", missing.Take(4).Select(d => d.Name))}{(missing.Count > 4 ? ", ..." : "")})";
        HubRenderingLook look = hub.lights != null ? hub.lights.GetComponent<HubRenderingLook>() : null;
        if (look != null && !look.depthTextureForFog) return "HubRenderingLook does not ask for the depth texture the fog's soft edges need";
        return null;
    }

    /// <summary>ProjectSetup's fix: adds the fog of every room that has none to the scene hub and applies it to the prefab. Marks the scene dirty; the caller saves.</summary>
    public static void FogRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null || hub.building == null || hub.dressing == null) { Debug.LogError("Hub fog: there is no hub with Building / Dressing groups in the scene (Build Missing Pieces places it first)."); return; }
        Material mat = FogMaterial();
        if (mat == null) { Debug.LogError($"Hub fog: the Fog Particles pack is not imported ({FogTexturePath} is missing)."); return; }

        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        Dictionary<ProbeDef, List<Vector3>> floors = FogFloors(hub);
        if (floors.Count == 0) { Debug.LogError("Hub fog: the hub NavMesh is not loaded, so there is no floor to put fog on (bake it: HubNavMesh.Bake)."); if (!wasActive) hub.SetContentActive(false); return; }
        Transform group = FogGroup(hub, true);
        List<string> added = new List<string>();
        foreach (ProbeDef def in MissingFog(hub, floors))
        {
            BuildFogEmitter(group, floors[def], def.Name, mat);
            added.Add(def.Name);
        }
        HubRenderingLook look = hub.lights != null ? hub.lights.GetComponent<HubRenderingLook>() : null;
        if (look != null && !look.depthTextureForFog) { look.depthTextureForFog = true; EditorUtility.SetDirty(look); }
        Debug.Log($"Hub fog: added {added.Count} emitters ({string.Join(", ", added)}).");

        if (!wasActive) hub.SetContentActive(false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        ApplyHubToPrefab(hub);
    }
}
