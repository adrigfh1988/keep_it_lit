using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// F95 (spec plannings/asylum-look-plan.md): the Asylum's graphics pass, added to the hand-edited hub prefab without regenerating it -
/// build-missing-only, like the story layer. Four parts: a per-area light colour story (<see cref="ApplyPalette"/>), custom reflection
/// probes rendered by this builder (<see cref="EnsureProbes"/>), SSAO as a renderer feature that only <see cref="HubRenderingLook"/>
/// switches on (<see cref="EnsureSsao"/>), and aged surface materials (<see cref="AsylumLookUpgrade"/>). No Light is added, so the hub
/// audit budget (56 lights / 6 shadowed) is untouched.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string ProbesName = "Probes";
    private const string ProbeFolder = "Assets/SourceFiles/Textures/Asylum/Probes";
    private const string LampMaterialFolder = "Assets/SourceFiles/Materials/Asylum";
    private const int ProbeResolution = 128;

    /// <summary>Set by a dev call (RunCommand) to re-render every probe on the next LookRepair; never set by the menus.</summary>
    public static bool ForceProbeRebuild;

    // ---------------------------------------------------------------- palette (D3)

    private enum Area { Ward, Ground, Basement, Morgue, Surgery, Upper, Office }

    private struct Look
    {
        public Color Color;
        public float Intensity;
        public Look(string hex, float intensity)
        {
            ColorUtility.TryParseHtmlString(hex, out Color);
            Intensity = intensity;
        }
    }

    private static readonly Dictionary<Area, Look> Palette = new Dictionary<Area, Look>
    {
        { Area.Ward, new Look("#D6F2C8", 1f) },        // sickly fluorescent green-white
        { Area.Ground, new Look("#FFC98A", 0.85f) },   // dim warm tungsten
        { Area.Basement, new Look("#FF9A4A", 1f) },    // sodium orange
        { Area.Morgue, new Look("#B8D4FF", 1.1f) },    // cold blue-white
        { Area.Surgery, new Look("#F2F6FF", 1f) },     // hard white
        { Area.Upper, new Look("#C6CFDA", 0.8f) },     // desaturated grey-blue
        { Area.Office, new Look("#FFB46B", 0.75f) },   // dim amber
    };

    /// <summary>Room (top-level group under a storey, or a Fill_ light's suffix) to area. Rooms not listed keep their colour.</summary>
    private static readonly Dictionary<string, Area> RoomAreas = new Dictionary<string, Area>
    {
        { "DayRoom", Area.Ward }, { "Bedroom", Area.Ward }, { "Classroom", Area.Ward },
        { "Gatehouse", Area.Ground }, { "HallDownstair", Area.Ground }, { "GuardRoom", Area.Ground }, { "DiningRoom", Area.Ground }, { "Stock", Area.Ground },
        { "HallUnderground", Area.Basement }, { "MachineRoom", Area.Basement }, { "SeclusionRoomA", Area.Basement }, { "SeclusionRoomB", Area.Basement },
        { "Morgue", Area.Morgue },
        { "Surgery", Area.Surgery }, { "ExamRoom", Area.Surgery },
        { "HallUpstair", Area.Upper },
        { "Office", Area.Office }, { "OfficeA", Area.Office },
    };

    /// <summary>The area a hub light belongs to, or null (leave it alone: wing beacons, Life lights, unlisted rooms).</summary>
    private static Area? AreaOf(AsylumHub hub, Light light)
    {
        if (light.transform.IsChildOf(hub.building))
        {
            Transform t = light.transform;
            while (t.parent != null && t.parent.parent != hub.building && t.parent != hub.building) t = t.parent;
            string key = t.parent != null && t.parent.name == "PointLights_All" ? "PointLights_All" : t.name;
            if (key == "PointLights_All")
            {
                // The pack's loose accent lights: by height (the storeys are ~4 m apart, the basement floor is near y = 0).
                float y = light.transform.position.y - hub.transform.position.y;
                return y >= 4f ? Area.Upper : y >= 0f ? Area.Ground : Area.Basement;
            }
            return RoomAreas.TryGetValue(key, out Area area) ? area : (Area?)null;
        }
        if (hub.lights != null && light.transform.IsChildOf(hub.lights) && light.name.StartsWith("Fill_"))
        {
            return RoomAreas.TryGetValue(light.name.Substring("Fill_".Length), out Area fill) ? fill : (Area?)null;
        }
        return null;
    }

    private static IEnumerable<Light> PaletteLights(AsylumHub hub)
    {
        foreach (Light l in hub.GetComponentsInChildren<Light>(true))
        {
            if (hub.life != null && l.transform.IsChildOf(hub.life)) continue;      // the lift's red light, the key glint: leave alone
            if (hub.wings != null && l.transform.IsChildOf(hub.wings)) continue;    // wing / shop beacons keep their own signal colours
            yield return l;
        }
    }

    private static bool NearlyEqual(Color a, Color b) => Mathf.Abs(a.r - b.r) < 0.01f && Mathf.Abs(a.g - b.g) < 0.01f && Mathf.Abs(a.b - b.b) < 0.01f;

    private static int UnpaintedLights(AsylumHub hub)
    {
        int n = 0;
        foreach (Light l in PaletteLights(hub))
        {
            Area? area = AreaOf(hub, l);
            if (area.HasValue && !NearlyEqual(l.color, Palette[area.Value].Color)) n++;
        }
        return n;
    }

    /// <summary>Colours the lights by area and gives each shared emissive fixture a per-area material copy. Returns how many lights changed. Idempotent: a light already at its area's colour is left alone.</summary>
    private static int ApplyPalette(AsylumHub hub, out Dictionary<Area, int> perArea)
    {
        perArea = new Dictionary<Area, int>();
        EnsureFolderPath(LampMaterialFolder);
        int changed = 0;
        foreach (Light l in PaletteLights(hub).ToList())
        {
            Area? maybe = AreaOf(hub, l);
            if (!maybe.HasValue) continue;
            Area area = maybe.Value;
            Look look = Palette[area];
            perArea[area] = perArea.TryGetValue(area, out int n) ? n + 1 : 1;
            RecolourFixture(l, area, look);
            if (NearlyEqual(l.color, look.Color)) continue;
            Undo.RecordObject(l, "F95 palette");
            l.color = look.Color;
            l.intensity *= look.Intensity;
            EditorUtility.SetDirty(l);
            changed++;
        }
        return changed;
    }

    /// <summary>The fixture mesh next to a light (its parent object) keeps its shared emissive material; swap it for an area copy so the glass matches the light it throws.</summary>
    private static void RecolourFixture(Light light, Area area, Look look)
    {
        Transform fixture = light.transform.parent;
        if (fixture == null || fixture.GetComponent<AsylumHub>() != null) return;
        foreach (Renderer r in fixture.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool dirty = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || m.name.StartsWith("Lamp_") || !m.IsKeywordEnabled("_EMISSION") || !m.HasProperty("_EmissionColor")) continue;
                string baseName = m.name.Replace("_Dim", "");
                if (baseName != "NeonLamp" && baseName != "LampWall" && baseName != "SingleLight" && baseName != "Lamp") continue;
                string path = $"{LampMaterialFolder}/Lamp_{area}_{m.name}.mat";
                Material copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (copy == null)
                {
                    copy = new Material(m) { name = $"Lamp_{area}_{m.name}" };
                    Color emission = m.GetColor("_EmissionColor");
                    copy.SetColor("_EmissionColor", new Color(emission.r * look.Color.r, emission.g * look.Color.g, emission.b * look.Color.b, emission.a));
                    AssetDatabase.CreateAsset(copy, path);
                }
                mats[i] = copy;
                dirty = true;
            }
            if (dirty) { Undo.RecordObject(r, "F95 lamp material"); r.sharedMaterials = mats; EditorUtility.SetDirty(r); }
        }
    }

    // ---------------------------------------------------------------- reflection probes (D2)

    private struct ProbeDef
    {
        public string Name, Room;
        public int Part, Parts;   // a long hall is cut into `Parts` boxes along its longer axis
        public ProbeDef(string room, int part = 0, int parts = 1) { Room = room; Part = part; Parts = parts; Name = parts > 1 ? $"{room}_{part + 1}" : room; }
    }

    private static readonly ProbeDef[] ProbeDefs =
    {
        new ProbeDef("HallDownstair", 0, 3), new ProbeDef("HallDownstair", 1, 3), new ProbeDef("HallDownstair", 2, 3),
        new ProbeDef("HallUpstair", 0, 2), new ProbeDef("HallUpstair", 1, 2),
        new ProbeDef("HallUnderground"), new ProbeDef("MachineRoom"), new ProbeDef("Morgue"), new ProbeDef("DayRoom"),
        new ProbeDef("Surgery"), new ProbeDef("Office"), new ProbeDef("OfficeA"), new ProbeDef("Bedroom"), new ProbeDef("ExamRoom"),
        new ProbeDef("DiningRoom"), new ProbeDef("Classroom"), new ProbeDef("KitchenA"), new ProbeDef("Kitchen"),
    };

    private static Bounds ProbeBounds(Transform building, ProbeDef def)
    {
        Bounds b = RoomBounds(building, def.Room, 0f);
        if (def.Parts <= 1 || b.size == Vector3.zero) return b;
        bool alongX = b.size.x >= b.size.z;
        float length = alongX ? b.size.x : b.size.z;
        float min = alongX ? b.min.x : b.min.z;
        float lo = min + length * def.Part / def.Parts, hi = min + length * (def.Part + 1) / def.Parts;
        Vector3 size = alongX ? new Vector3(hi - lo, b.size.y, b.size.z) : new Vector3(b.size.x, b.size.y, hi - lo);
        Vector3 centre = alongX ? new Vector3((lo + hi) * 0.5f, b.center.y, b.center.z) : new Vector3(b.center.x, b.center.y, (lo + hi) * 0.5f);
        return new Bounds(centre, size);
    }

    private static Transform ProbeGroup(AsylumHub hub, bool create)
    {
        if (hub.lights == null) return null;
        Transform group = hub.lights.Find(ProbesName);
        if (group == null && create) group = Group(hub.lights, ProbesName);
        return group;
    }

    private static List<string> MissingProbes(AsylumHub hub)
    {
        Transform group = ProbeGroup(hub, false);
        List<string> missing = new List<string>();
        foreach (ProbeDef def in ProbeDefs)
        {
            if (hub.building != null && RoomBounds(hub.building, def.Room, 0f).size == Vector3.zero) continue; // the room does not exist in this hub
            Transform t = group != null ? group.Find("Probe_" + def.Name) : null;
            ReflectionProbe p = t != null ? t.GetComponent<ReflectionProbe>() : null;
            if (ForceProbeRebuild || p == null || p.customBakedTexture == null) missing.Add(def.Name);
        }
        return missing;
    }

    /// <summary>Adds (and renders) every missing room probe. Needs the hub content active. Returns the probes created.</summary>
    private static List<string> EnsureProbes(AsylumHub hub)
    {
        List<string> created = new List<string>();
        List<string> missing = MissingProbes(hub);
        if (missing.Count == 0) return created;
        EnsureFolderPath(ProbeFolder);
        Transform group = ProbeGroup(hub, true);

        // Render with the hub's own atmosphere, as the player sees it when entering, then put the scene's settings back.
        AmbientMode ambientMode = RenderSettings.ambientMode; Color ambient = RenderSettings.ambientLight;
        bool fog = RenderSettings.fog; FogMode fogMode = RenderSettings.fogMode; float fogDensity = RenderSettings.fogDensity; Color fogColor = RenderSettings.fogColor;
        RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = hub.hubAmbient;
        RenderSettings.fog = true; RenderSettings.fogMode = FogMode.ExponentialSquared; RenderSettings.fogDensity = hub.hubFogDensity; RenderSettings.fogColor = hub.hubFogColor;
        try
        {
            foreach (ProbeDef def in ProbeDefs)
            {
                if (!missing.Contains(def.Name)) continue;
                Bounds b = ProbeBounds(hub.building, def);
                Transform existing = group.Find("Probe_" + def.Name);
                GameObject go = existing != null ? existing.gameObject : new GameObject("Probe_" + def.Name);
                go.transform.SetParent(group, true);
                // Capture from about head height, inside the box.
                Vector3 position = new Vector3(b.center.x, Mathf.Min(b.min.y + 1.6f, b.center.y), b.center.z);
                go.transform.position = position;

                ReflectionProbe probe = go.GetComponent<ReflectionProbe>();
                if (probe == null) probe = go.AddComponent<ReflectionProbe>();
                probe.mode = ReflectionProbeMode.Custom;
                probe.refreshMode = ReflectionProbeRefreshMode.OnAwake;
                probe.boxProjection = true;
                probe.size = b.size;
                probe.center = b.center - position;
                probe.blendDistance = 1f;
                probe.resolution = ProbeResolution;
                probe.hdr = false;
                probe.intensity = 1f;
                probe.importance = Mathf.Clamp(Mathf.RoundToInt(b.size.x * b.size.y * b.size.z / 400f), 0, 20);
                probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
                probe.backgroundColor = hub.hubFogColor;
                probe.nearClipPlane = 0.05f;
                probe.farClipPlane = Mathf.Max(30f, b.size.magnitude);

                Cubemap cube = RenderProbeCubemap(position, hub.hubFogColor, probe.farClipPlane);
                cube.name = def.Name;
                string path = $"{ProbeFolder}/{def.Name}.asset";
                Cubemap existingCube = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                if (existingCube != null) { EditorUtility.CopySerialized(cube, existingCube); EditorUtility.SetDirty(existingCube); Object.DestroyImmediate(cube); cube = existingCube; }
                else AssetDatabase.CreateAsset(cube, path);
                probe.customBakedTexture = AssetDatabase.LoadAssetAtPath<Cubemap>(path);
                Color sample = cube.GetPixel(CubemapFace.PositiveZ, ProbeResolution / 2, ProbeResolution / 2);
                if (!(sample.maxColorComponent >= 0f && sample.maxColorComponent < 1000f)) Debug.LogError($"Asylum look: probe '{def.Name}' rendered garbage ({sample}).");
                EditorUtility.SetDirty(probe);
                created.Add(def.Name);
            }
        }
        finally
        {
            RenderSettings.ambientMode = ambientMode; RenderSettings.ambientLight = ambient;
            RenderSettings.fog = fog; RenderSettings.fogMode = fogMode; RenderSettings.fogDensity = fogDensity; RenderSettings.fogColor = fogColor;
        }
        AssetDatabase.SaveAssets();
        return created;
    }

    /// <summary>Renders the six faces into an 8-bit sRGB cubemap (with mips; the half-float path read back negative values in this Unity version) from `position`, through a throwaway camera.</summary>
    private static Cubemap RenderProbeCubemap(Vector3 position, Color background, float far)
    {
        GameObject go = new GameObject("F95ProbeCam") { hideFlags = HideFlags.HideAndDontSave };
        RenderTexture rt = new RenderTexture(ProbeResolution, ProbeResolution, 24, RenderTextureFormat.ARGB32)
        {
            dimension = TextureDimension.Cube, useMipMap = true, autoGenerateMips = true
        };
        try
        {
            Camera cam = go.AddComponent<Camera>();
            cam.transform.position = position;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = far;
            cam.allowHDR = false;
            cam.allowMSAA = false;
            UniversalAdditionalCameraData data = go.GetComponent<UniversalAdditionalCameraData>();
            if (data == null) data = go.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;
            cam.RenderToCubemap(rt, 63);

            Cubemap cube = new Cubemap(ProbeResolution, TextureFormat.RGBA32, true, false);
            // CopyTexture is GPU-only: an asset saved from it would serialise uninitialised CPU memory. Read each face back into the CPU copy.
            Texture2D face = new Texture2D(ProbeResolution, ProbeResolution, TextureFormat.RGBA32, false, false);
            RenderTexture flat = new RenderTexture(ProbeResolution, ProbeResolution, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            for (int i = 0; i < 6; i++)
            {
                Graphics.CopyTexture(rt, i, 0, flat, 0, 0);
                RenderTexture.active = flat;
                face.ReadPixels(new Rect(0, 0, ProbeResolution, ProbeResolution), 0, 0);
                cube.SetPixels(face.GetPixels(), (CubemapFace)i);
            }
            RenderTexture.active = previous;
            flat.Release();
            Object.DestroyImmediate(flat);
            Object.DestroyImmediate(face);
            cube.Apply(true); // mips by box filter (not a glossy convolution; fine for the low smoothness used here)
            return cube;
        }
        finally
        {
            rt.Release();
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(go);
        }
    }

    // ---------------------------------------------------------------- SSAO (D1)

    private static IEnumerable<UniversalRendererData> RendererDatasInUse()
    {
        HashSet<UniversalRendererData> seen = new HashSet<UniversalRendererData>();
        List<RenderPipelineAsset> assets = new List<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
        for (int i = 0; i < QualitySettings.names.Length; i++) assets.Add(QualitySettings.GetRenderPipelineAssetAt(i));
        foreach (UniversalRenderPipelineAsset asset in assets.OfType<UniversalRenderPipelineAsset>().Distinct())
        {
            SerializedProperty list = new SerializedObject(asset).FindProperty("m_RendererDataList");
            for (int i = 0; list != null && i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue is UniversalRendererData data && seen.Add(data)) yield return data;
            }
        }
    }

    private static ScreenSpaceAmbientOcclusion FindSsao(UniversalRendererData data) => data.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();

    /// <summary>The SSAO feature HubRenderingLook should drive (the first renderer data in use that has one).</summary>
    private static ScreenSpaceAmbientOcclusion HubSsao() => RendererDatasInUse().Select(FindSsao).FirstOrDefault(f => f != null);

    private static string SsaoProblem()
    {
        List<UniversalRendererData> datas = RendererDatasInUse().ToList();
        if (datas.Count == 0) return "no URP renderer data in use";
        foreach (UniversalRendererData data in datas)
        {
            ScreenSpaceAmbientOcclusion feature = FindSsao(data);
            if (feature == null) return $"renderer '{data.name}' has no Screen Space Ambient Occlusion feature";
            if (!Application.isPlaying && feature.isActive) return $"the SSAO feature on '{data.name}' is left active (it must be saved inactive; HubRenderingLook switches it on)";
        }
        return null;
    }

    private static void EnsureSsao(System.Text.StringBuilder report)
    {
        foreach (UniversalRendererData data in RendererDatasInUse().ToList())
        {
            ScreenSpaceAmbientOcclusion feature = FindSsao(data);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                feature.name = "ScreenSpaceAmbientOcclusion";
                AssetDatabase.AddObjectToAsset(feature, data);
                AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long id);

                SerializedObject so = new SerializedObject(data);
                SerializedProperty features = so.FindProperty("m_RendererFeatures");
                SerializedProperty map = so.FindProperty("m_RendererFeatureMap");
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
                map.arraySize++;
                map.GetArrayElementAtIndex(map.arraySize - 1).longValue = id;
                so.ApplyModifiedPropertiesWithoutUndo();
                report.AppendLine($"SSAO: added to renderer '{data.name}'.");
            }

            // Tuned by eye in Play (see the F95 notes): enough contact shadow in corners and under furniture, no black halos.
            SerializedObject fso = new SerializedObject(feature);
            SerializedProperty s = fso.FindProperty("m_Settings");
            SetEnum(s, "AOMethod", 1);          // InterleavedGradient
            SetBool(s, "Downsample", true);
            SetBool(s, "AfterOpaque", false);
            SetEnum(s, "Source", 1);            // DepthNormals
            SetEnum(s, "NormalSamples", 1);     // Medium
            SetFloat(s, "Intensity", SsaoIntensity);
            SetFloat(s, "DirectLightingStrength", SsaoDirectLighting);
            SetFloat(s, "Radius", SsaoRadius);
            SetEnum(s, "Samples", 1);           // Medium (8)
            SetEnum(s, "BlurQuality", 1);       // Medium
            SetFloat(s, "Falloff", 100f);
            fso.ApplyModifiedPropertiesWithoutUndo();

            feature.SetActive(false);
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(data);
        }
        AssetDatabase.SaveAssets();
    }

    private const float SsaoIntensity = 2.0f;
    private const float SsaoDirectLighting = 0.6f;
    private const float SsaoRadius = 0.45f;

    private static void SetEnum(SerializedProperty parent, string name, int value) { SerializedProperty p = parent.FindPropertyRelative(name); if (p != null) p.intValue = value; }
    private static void SetBool(SerializedProperty parent, string name, bool value) { SerializedProperty p = parent.FindPropertyRelative(name); if (p != null) p.boolValue = value; }
    private static void SetFloat(SerializedProperty parent, string name, float value) { SerializedProperty p = parent.FindPropertyRelative(name); if (p != null) p.floatValue = value; }

    // ---------------------------------------------------------------- dark volume (F96)

    private const string DarkVolumePath = "Assets/SourceFiles/Settings/AsylumHubVolume.asset";
    private const string DarkVolumeName = "DarkVolume";

    /// <summary>F96: the hub-only darkening volume (vignette, exposure, desaturation) and its profile asset; HubRenderingLook enables it with the hub. Idempotent (an existing profile's values are left alone).</summary>
    private static void EnsureDarkVolume(AsylumHub hub, HubRenderingLook look)
    {
        VolumeProfile profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(DarkVolumePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, DarkVolumePath);
            Vignette vignette = profile.Add<Vignette>(true);
            vignette.intensity.Override(0.45f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
            ColorAdjustments adjust = profile.Add<ColorAdjustments>(true);
            adjust.postExposure.Override(DarkVolumeExposure); // absolute: HorrorAtmosphere's runtime clone of the scene volume sits at -0.2
            adjust.saturation.Override(-10f);
            foreach (VolumeComponent c in profile.components) { AssetDatabase.AddObjectToAsset(c, profile); EditorUtility.SetDirty(c); }
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();
        }

        Transform t = hub.lights.Find(DarkVolumeName);
        if (t == null) { t = new GameObject(DarkVolumeName).transform; t.SetParent(hub.lights, false); }
        Volume volume = t.GetComponent<Volume>();
        if (volume == null) volume = t.gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = 10f; // above the scene's global volume (0)
        volume.weight = 1f;
        volume.sharedProfile = profile;
        volume.enabled = false; // HubRenderingLook switches it on with the hub
        look.darkVolume = volume;
        EditorUtility.SetDirty(volume);
    }

    private const float DarkVolumeExposure = -0.45f;

    // ---------------------------------------------------------------- pipeline flags

    private static bool ProbeFlagsOn(UniversalRenderPipelineAsset asset)
    {
        SerializedObject so = new SerializedObject(asset);
        return so.FindProperty("m_ReflectionProbeBlending").boolValue && so.FindProperty("m_ReflectionProbeBoxProjection").boolValue;
    }

    private static IEnumerable<UniversalRenderPipelineAsset> PipelinesInUse()
    {
        List<RenderPipelineAsset> assets = new List<RenderPipelineAsset> { GraphicsSettings.defaultRenderPipeline };
        for (int i = 0; i < QualitySettings.names.Length; i++) assets.Add(QualitySettings.GetRenderPipelineAssetAt(i));
        return assets.OfType<UniversalRenderPipelineAsset>().Distinct();
    }

    // ---------------------------------------------------------------- ProjectSetup piece

    /// <summary>ProjectSetup piece: null when there is no hub (that piece reports it) or the whole look is in place.</summary>
    public static string LookProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null || hub.building == null || hub.lights == null) return null;
        List<string> problems = new List<string>();

        int unpainted = UnpaintedLights(hub);
        if (unpainted > 0) problems.Add($"{unpainted} lights not on their area's colour");

        List<string> missing = MissingProbes(hub);
        if (missing.Count > 0) problems.Add($"{missing.Count} reflection probes missing ({string.Join(", ", missing.Take(4))}{(missing.Count > 4 ? ", ..." : "")})");
        if (PipelinesInUse().Any(p => !ProbeFlagsOn(p))) problems.Add("the URP asset has Reflection Probe Blending / Box Projection off");

        string ssao = SsaoProblem();
        if (ssao != null) problems.Add(ssao);
        HubRenderingLook look = hub.lights.GetComponent<HubRenderingLook>();
        if (look == null || look.ambientOcclusion == null) problems.Add("Lights has no HubRenderingLook wired to the SSAO feature");

        if (look != null && (look.darkVolume == null || look.darkVolume.sharedProfile == null)) problems.Add("Lights has no DarkVolume wired to HubRenderingLook");

        int unaged = AsylumLookUpgrade.UnagedCount();
        if (unaged > 0 || AsylumLookUpgrade.NeedsAging()) problems.Add($"{unaged} surface materials not aged");

        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    /// <summary>ProjectSetup's fix. Adds only what is missing to the scene hub, then applies it to the prefab. Marks the scene dirty; the caller saves.</summary>
    public static void LookRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Asylum look: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        if (hub.building == null || hub.lights == null) { Debug.LogError("Asylum look: the hub has no Building / Lights group."); return; }

        System.Text.StringBuilder report = new System.Text.StringBuilder("Asylum look (F95):\n");
        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();

        // 1) materials first (the probes render the aged surfaces)
        report.AppendLine($"materials aged: {AsylumLookUpgrade.Apply(hub.transform)}");

        // 2) light palette
        int painted = ApplyPalette(hub, out Dictionary<Area, int> perArea);
        report.AppendLine($"lights recoloured: {painted} (" + string.Join(", ", perArea.Select(kv => $"{kv.Key} {kv.Value}")) + ")");

        // 3) pipeline flags + probes
        foreach (UniversalRenderPipelineAsset asset in PipelinesInUse())
        {
            if (ProbeFlagsOn(asset)) continue;
            SerializedObject so = new SerializedObject(asset);
            so.FindProperty("m_ReflectionProbeBlending").boolValue = true;
            so.FindProperty("m_ReflectionProbeBoxProjection").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(asset);
            report.AppendLine($"URP asset '{asset.name}': reflection probe blending + box projection on.");
        }
        List<string> probes = EnsureProbes(hub);
        report.AppendLine($"probes rendered: {probes.Count}");

        // 4) SSAO feature (inactive) + the component that turns it on for the hub only
        EnsureSsao(report);
        HubRenderingLook look = hub.lights.GetComponent<HubRenderingLook>();
        if (look == null) look = hub.lights.gameObject.AddComponent<HubRenderingLook>();
        look.ambientOcclusion = HubSsao();
        EnsureDarkVolume(hub, look);
        EditorUtility.SetDirty(look);
        PrefabUtility.RecordPrefabInstancePropertyModifications(look);

        report.AppendLine(Audit(hub.transform));
        Debug.Log(report.ToString());

        if (!wasActive) hub.SetContentActive(false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        ApplyHubToPrefab(hub);
    }
}
