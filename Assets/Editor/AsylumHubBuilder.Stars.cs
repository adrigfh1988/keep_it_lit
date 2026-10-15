using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

/// <summary>
/// F100 (layer 2): Patient 7's chalk stars, added to the hand-edited hub prefab without regenerating it (build-missing-only). A drawn
/// 2 x 2 sheet of rough five-point stars with a short tail along +X, one transparent URP Lit material with a faint emission of its own
/// (no Light anywhere), a pool of inactive wall quads under Life/Wayfinding/Stars and one star on the hall wall beside the Commissary door, all wired into a
/// <see cref="HubStarTrail"/>. Spec: plannings/hub-wayfinding-plan.md.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string StarTexturePath = "Assets/SourceFiles/Textures/Decals/ChalkStars.png";
    private const string StarMaterialPath = "Assets/SourceFiles/Materials/Asylum/HubChalkStar.mat";
    private const string WayfindingName = "Wayfinding";
    private const int StarPoolSize = 48; // 32 ran dry on floor 4 (every 3 m, some doubled)

    // ---------------------------------------------------------------- ProjectSetup piece

    /// <summary>Null when there is no hub (that piece reports it) or the star trail is all there.</summary>
    public static string StarsProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null;
        List<string> missing = new List<string>();
        if (AssetDatabase.LoadAssetAtPath<Texture2D>(StarTexturePath) == null) missing.Add("ChalkStars.png");
        if (AssetDatabase.LoadAssetAtPath<Material>(StarMaterialPath) == null) missing.Add("HubChalkStar.mat");
        Transform way = hub.life != null ? hub.life.Find(WayfindingName) : null;
        HubStarTrail trail = way != null ? way.GetComponent<HubStarTrail>() : null;
        if (hub.life == null) missing.Add("Life");
        else if (trail == null) missing.Add("Life/Wayfinding (HubStarTrail)");
        else if (!trail.IsComplete) missing.Add("the star pool or the Commissary door star");
        return missing.Count == 0 ? null : "missing: " + string.Join(", ", missing);
    }

    /// <summary>ProjectSetup's fix. Marks the scene dirty; the caller saves.</summary>
    public static void StarsRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Hub stars: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        if (hub.life == null) { Debug.LogError("Hub stars: the hub has no Life group - run Build Missing Pieces (story layer) first."); return; }

        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        Material material = EnsureStarMaterial(EnsureStarTexture());

        Transform way = hub.life.Find(WayfindingName) ?? Group(hub.life, WayfindingName);
        HubStarTrail trail = way.GetComponent<HubStarTrail>() ?? way.gameObject.AddComponent<HubStarTrail>();

        Transform stars = way.Find("Stars") ?? Group(way, "Stars");
        List<Renderer> pool = new List<Renderer>();
        for (int i = 0; i < StarPoolSize; i++)
        {
            Transform existing = stars.Find($"Star_{i:00}");
            Renderer r = existing != null ? existing.GetComponent<Renderer>() : MakeQuad(stars, $"Star_{i:00}", material);
            pool.Add(r);
        }

        Renderer shop = null;
        if (hub.shop != null && hub.shop.IsComplete)
        {
            Transform found = hub.shop.transform.Find("ShopStar");
            shop = found != null ? found.GetComponent<Renderer>() : PlaceShopStar(hub, material);
        }
        else Debug.LogWarning("Hub stars: the hub has no Commissary yet; its star is added on the next run once it exists.");

        SerializedObject so = new SerializedObject(trail);
        SerializedProperty arr = so.FindProperty("pool");
        arr.arraySize = pool.Count;
        for (int i = 0; i < pool.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = pool[i];
        so.FindProperty("shopStar").objectReferenceValue = shop;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(trail);

        Debug.Log($"Hub stars: pool of {pool.Count}, Commissary door star {(shop != null ? "on the wall at " + shop.transform.position.ToString("F2") : "MISSING")}.");
        if (!wasActive) hub.SetContentActive(false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        ApplyHubToPrefab(hub);
    }

    private static Renderer MakeQuad(Transform parent, string name, Material material)
    {
        GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = name;
        quad.transform.SetParent(parent, false);
        quad.transform.localScale = Vector3.one * 0.3f;
        Object.DestroyImmediate(quad.GetComponent<Collider>());
        MeshRenderer r = quad.GetComponent<MeshRenderer>();
        r.sharedMaterial = material;
        r.shadowCastingMode = ShadowCastingMode.Off;
        r.receiveShadows = false;
        r.lightProbeUsage = LightProbeUsage.Off;
        r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        quad.SetActive(false);
        return r;
    }

    /// <summary>One star on the hall wall beside the Commissary door (1.4 m up): found by a ray from the hall to the wall, parented to AsylumHub/Shop so it exists with the hub content.</summary>
    internal static Renderer PlaceShopStar(AsylumHub hub, Material material)
    {
        HubShop shop = hub.shop;
        Vector3 away = ShopHallDirection(shop); // into the hall
        Vector3 across = Vector3.Cross(Vector3.up, away); // the door's width axis
        Bounds frameB = ShopFrameBounds(shop);
        Vector3 doorCentre = new Vector3(frameB.center.x, ShopFloorY, frameB.center.z);
        Vector3 point = doorCentre + Vector3.up * 1.4f; Vector3 normal = away;
        // 1.4 m to one side of the door (east first, then west), 1.4 m up; whichever the wall is flat and clear on
        foreach (float side in new[] { -1.4f, 1.4f })
        {
            Vector3 from = doorCentre + across * side + away * 1.5f + Vector3.up * 1.4f;
            if (Physics.Raycast(from, -away, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore) && Mathf.Abs(hit.normal.y) < 0.2f)
            {
                point = hit.point; normal = hit.normal; normal.y = 0f; normal.Normalize();
                break;
            }
        }
        Renderer r = MakeQuad(shop.transform, "ShopStar", material);
        r.transform.SetPositionAndRotation(point + normal * 0.03f, Quaternion.LookRotation(-normal, Vector3.up));
        r.transform.localScale = Vector3.one * 0.3f;
        r.gameObject.SetActive(true); // HubStarTrail switches it with the shop
        return r;
    }

    // ---------------------------------------------------------------- texture and material

    private static Texture2D EnsureStarTexture()
    {
        Texture2D existing = AssetDatabase.LoadAssetAtPath<Texture2D>(StarTexturePath);
        if (existing != null) return existing;
        EnsureFolderPath("Assets/SourceFiles/Textures/Decals");

        const int cell = 256, size = cell * 2;
        Color32[] pixels = new Color32[size * size];
        for (int variant = 0; variant < 4; variant++) DrawChalkStar(pixels, size, (variant & 1) * cell, (variant >> 1) * cell, cell, new System.Random(1007 + variant * 31));
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(pixels);
        tex.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(StarTexturePath), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(StarTexturePath);

        TextureImporter importer = (TextureImporter)AssetImporter.GetAtPath(StarTexturePath);
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true; // every cell keeps 24 px of empty border, so the lower mips never bleed one star into the next
        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.maxTextureSize = 512;
        importer.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(StarTexturePath);
    }

    /// <summary>One rough chalk star: five jittered strokes of a single pentagram line, a short tail stroke along +X, grainy and broken.</summary>
    private static void DrawChalkStar(Color32[] pixels, int size, int ox, int oy, int cell, System.Random rng)
    {
        float cx = ox + cell * 0.40f, cy = oy + cell * 0.5f;
        float radius = cell * 0.27f;
        Vector2[] v = new Vector2[5];
        for (int k = 0; k < 5; k++)
        {
            float a = (90f + 72f * k + (float)(rng.NextDouble() * 10 - 5)) * Mathf.Deg2Rad;
            float r = radius * (0.9f + 0.2f * (float)rng.NextDouble());
            v[k] = new Vector2(cx + Mathf.Cos(a) * r, cy + Mathf.Sin(a) * r);
        }
        // the pentagram is drawn with one line: 0 -> 2 -> 4 -> 1 -> 3 -> 0
        int[] order = { 0, 2, 4, 1, 3, 0 };
        List<(Vector2 a, Vector2 b)> segments = new List<(Vector2, Vector2)>();
        for (int i = 0; i < 5; i++) segments.Add((v[order[i]], v[order[i + 1]]));
        // the tail leaves the last vertex of the stroke, to the right
        Vector2 tailStart = v[0];
        segments.Add((tailStart, tailStart + new Vector2(cell * 0.28f, (float)(rng.NextDouble() * 14 - 7))));

        float width = 6.5f + (float)rng.NextDouble() * 2f;
        float seed = (float)rng.NextDouble() * 100f;
        int pad = 24;
        for (int y = oy + pad; y < oy + cell - pad; y++)
        for (int x = ox + pad; x < ox + cell - pad; x++)
        {
            float d = float.MaxValue;
            Vector2 p = new Vector2(x, y);
            foreach ((Vector2 a, Vector2 b) in segments)
            {
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                d = Mathf.Min(d, Vector2.Distance(p, a + ab * t));
            }
            // a wobbling stroke width, chalky grain, and a few gaps where the chalk skipped
            float wobble = 0.75f + 0.5f * Mathf.PerlinNoise(x * 0.07f + seed, y * 0.07f);
            float edge = 1f - Mathf.Clamp01((d - width * wobble * 0.5f) / 2.2f);
            if (edge <= 0f) continue;
            float grain = 0.55f + 0.45f * Mathf.PerlinNoise(x * 0.55f + seed, y * 0.55f);
            float gap = Mathf.PerlinNoise(x * 0.045f + seed * 2f, y * 0.045f) < 0.2f ? 0.35f : 1f;
            byte alpha = (byte)Mathf.RoundToInt(Mathf.Clamp01(edge * grain * gap) * 255f);
            pixels[y * size + x] = new Color32(255, 255, 255, alpha);
        }
    }

    private static Material EnsureStarMaterial(Texture2D texture)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(StarMaterialPath);
        if (mat != null) return mat;
        EnsureFolderPath("Assets/SourceFiles/Materials/Asylum");
        mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "HubChalkStar" };
        mat.SetTexture("_BaseMap", texture);
        mat.SetColor("_BaseColor", new Color(0.96f, 0.95f, 0.88f, 1f));
        mat.SetFloat("_Smoothness", 0.1f);
        mat.SetFloat("_Metallic", 0f);
        // the glow: the same sheet as the emission map, tinted pale green (HubStarTrail scales it per star through a property block)
        mat.EnableKeyword("_EMISSION");
        mat.SetTexture("_EmissionMap", texture);
        mat.SetColor("_EmissionColor", new Color(0.52f, 0.66f, 0.55f));
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;

        // the decal rules of this project: transparent, alpha-blended, no depth write, and _BlendModePreserveSpecular off (else URP
        // re-validates to premultiplied alpha and the torch's specular lights the whole quad as a pale rectangle)
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        if (mat.HasProperty("_BlendModePreserveSpecular")) mat.SetFloat("_BlendModePreserveSpecular", 0f);
        mat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        mat.SetFloat("_AlphaClip", 0f);
        mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
        mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
        if (mat.HasProperty("_SrcBlendAlpha")) mat.SetFloat("_SrcBlendAlpha", (float)BlendMode.One);
        if (mat.HasProperty("_DstBlendAlpha")) mat.SetFloat("_DstBlendAlpha", (float)BlendMode.OneMinusSrcAlpha);
        mat.SetFloat("_ZWrite", 0f);
        mat.DisableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetShaderPassEnabled("DepthOnly", false);
        mat.SetShaderPassEnabled("ShadowCaster", false);
        mat.renderQueue = (int)RenderQueue.Transparent;
        AssetDatabase.CreateAsset(mat, StarMaterialPath);
        return mat;
    }
}
