using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase C: light, life and polish. Fill lights for the rooms the pack never lit (the wing rooms and the big day room), emission
/// dimmed on fixtures whose light was culled, a flicker on about one route light in four, the ambience emitters and a few
/// dust emitters in the halls. Called from Generate after the wings are in.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string DustPrefab = "Assets/SourceFiles/Themes/1_Ward/Prefabs/Ward_Dust.prefab";

    private static void BuildAmbienceAndDust(AsylumHub hub)
    {
        AddRoomFills(hub);
        DimUnlitFixtures(hub);
        AddFlickers(hub);
        BuildAmbience(hub);
        BuildDust(hub);
    }

    // ---------------------------------------------------------------- fill lights

    private static Light AddFill(AsylumHub hub, string name, Vector3 world, Color color, float intensity, float range)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(hub.lights, false);
        go.transform.position = world;
        Light l = go.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = color;
        l.intensity = intensity;
        l.range = range;
        l.shadows = LightShadows.None;
        l.lightmapBakeType = LightmapBakeType.Realtime;
        return l;
    }

    /// <summary>The pack lit its halls and left the wing rooms (Surgery, Office) and the middle of the huge day room dark.</summary>
    private static void AddRoomFills(AsylumHub hub)
    {
        foreach ((string room, Color color, float intensity, float range, float dx, float dz) fill in new[]
        {
            ("Surgery", Clinical, 1.1f, 9f, 0f, 0f),
            ("Office", Sodium, 1.4f, 9f, 0f, 0f),
            ("DayRoom", Sodium, 2.4f, 11f, 0f, -4f),
            ("DayRoom", Sodium, 2.4f, 11f, 0f, 4f),
        })
        {
            Bounds b = RoomBounds(_building, fill.room, 0f);
            if (b.size == Vector3.zero) continue;
            float floorY = FloorHeight(fill.room, b);
            AddFill(hub, "Fill_" + fill.room, new Vector3(b.center.x + fill.dx, floorY + 3.0f, b.center.z + fill.dz), fill.color, fill.intensity, fill.range);
        }
    }

    // ---------------------------------------------------------------- fixtures without a light

    private static bool IsEmissive(Material m)
    {
        return m != null && m.HasProperty("_EmissionColor") && m.IsKeywordEnabled("_EMISSION") && m.GetColor("_EmissionColor").maxColorComponent > 0.05f;
    }

    /// <summary>
    /// D7: a lamp fixture whose light was culled must not glow as if it were lit. Every emissive renderer with no enabled light
    /// within 2 m gets a copy of its material with the emission cut to 30 % (copies saved under Materials/Asylum/Dim).
    /// </summary>
    private static void DimUnlitFixtures(AsylumHub hub)
    {
        EnsureFolderPath(MaterialFolder + "/Dim");
        List<Vector3> lights = hub.GetComponentsInChildren<Light>(true).Where(l => l.enabled).Select(l => l.transform.position).ToList();
        Dictionary<Material, Material> dimmed = new Dictionary<Material, Material>();
        int count = 0;

        foreach (Renderer r in hub.building.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer) continue;
            Material[] mats = r.sharedMaterials;
            if (!mats.Any(IsEmissive)) continue;
            Vector3 c = r.bounds.center;
            if (lights.Any(p => Vector3.Distance(p, c) < 2f)) continue;

            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (!IsEmissive(mats[i])) continue;
                if (!dimmed.TryGetValue(mats[i], out Material copy))
                {
                    string path = $"{MaterialFolder}/Dim/{mats[i].name}_Dim.mat";
                    copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                    if (copy == null)
                    {
                        copy = new Material(mats[i]) { name = mats[i].name + "_Dim" };
                        copy.SetColor("_EmissionColor", mats[i].GetColor("_EmissionColor") * 0.3f);
                        AssetDatabase.CreateAsset(copy, path);
                    }
                    dimmed[mats[i]] = copy;
                }
                mats[i] = copy;
                changed = true;
            }
            if (changed) { r.sharedMaterials = mats; count++; }
        }
        Debug.Log($"Asylum fixtures: {count} unlit emissive fixtures dimmed.");
    }

    // ---------------------------------------------------------------- flicker

    private static readonly string[] FlickerRooms = { "HallDownstair", "HallUpstair", "HallUnderground", "DayRoom", "Morgue", "MachineRoom" };

    private static void AddFlickers(AsylumHub hub)
    {
        // About one route light in four, picked deterministically by position so a rebuild flickers the same lamps.
        List<Light> candidates = hub.building.GetComponentsInChildren<Light>(true)
            .Where(l => l.enabled && l.shadows == LightShadows.None && FlickerRooms.Contains(GroupKey(_building, l.transform)))
            .OrderBy(l => Mathf.RoundToInt(l.transform.position.x * 2f)).ThenBy(l => l.transform.position.z).ThenBy(l => l.transform.position.y)
            .ToList();

        List<Renderer> emissive = hub.building.GetComponentsInChildren<Renderer>(true).Where(r => !(r is ParticleSystemRenderer) && r.sharedMaterials.Any(IsEmissive)).ToList();
        int count = 0;
        for (int i = 1; i < candidates.Count; i += 4)
        {
            Light l = candidates[i];
            Renderer fixture = emissive.OrderBy(r => Vector3.Distance(r.bounds.center, l.transform.position)).FirstOrDefault(r => Vector3.Distance(r.bounds.center, l.transform.position) < 1.6f);
            AudioSource buzz = l.gameObject.AddComponent<AudioSource>();
            buzz.playOnAwake = false;
            HubLampFlicker flicker = l.gameObject.AddComponent<HubLampFlicker>();
            flicker.Configure(l, fixture, buzz);
            count++;
        }
        Debug.Log($"Asylum flicker: {count} of {candidates.Count} route lights.");
    }

    // ---------------------------------------------------------------- ambience

    private static void BuildAmbience(AsylumHub hub)
    {
        Transform audio = Group(hub.dressing, "Audio");
        List<AsylumAmbience.Emitter> emitters = new List<AsylumAmbience.Emitter>();

        void Emit(string name, AsylumAmbience.EmitterKind kind, Vector3 world)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(audio, false);
            go.transform.position = world;
            AudioSource source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            emitters.Add(new AsylumAmbience.Emitter { source = source, kind = kind });
        }

        Bounds machine = RoomBounds(_building, "MachineRoom", 0f);
        if (machine.size != Vector3.zero) Emit("MachineHum", AsylumAmbience.EmitterKind.MachineHum, new Vector3(machine.center.x, FloorHeight("MachineRoom", machine) + 1.5f, machine.center.z));

        Bounds toilets = RoomBounds(_building, "ToiletsManA", 0f);
        if (toilets.size != Vector3.zero) Emit("TapDrip", AsylumAmbience.EmitterKind.Drip, new Vector3(toilets.center.x, FloorHeight("ToiletsManA", toilets) + 1.0f, toilets.center.z));

        Transform shaft = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "Elevator_Wall");
        if (shaft != null)
        {
            Renderer sr = shaft.GetComponentInChildren<Renderer>();
            if (sr != null) Emit("LiftShaftWind", AsylumAmbience.EmitterKind.ShaftWind, new Vector3(sr.bounds.center.x, HubPosition.y + 0.5f, sr.bounds.center.z));
        }

        // Far-off noises come from rooms the player will not be in.
        List<Transform> points = new List<Transform>();
        foreach (string room in new[] { "DiningRoom", "KitchenA", "Bedroom", "Classroom", "SeclusionRoomA", "ToiletsWoman", "Stock" })
        {
            Bounds b = RoomBounds(_building, room, 0f);
            if (b.size == Vector3.zero) continue;
            points.Add(Point(hub.points, "Ambient_" + room, new Vector3(b.center.x, b.center.y, b.center.z), 0f));
        }

        AsylumAmbience ambience = hub.gameObject.AddComponent<AsylumAmbience>();
        ambience.Configure(hub, emitters.ToArray(), points.ToArray());
    }

    // ---------------------------------------------------------------- dust

    private static void BuildDust(AsylumHub hub)
    {
        GameObject dust = AssetDatabase.LoadAssetAtPath<GameObject>(DustPrefab);
        if (dust == null) { Debug.LogWarning($"Asylum dust: {DustPrefab} is missing, skipped."); return; }

        Transform group = Group(hub.dressing, "Particles");
        // name, centre (world), size
        (string, Vector3, Vector3)[] volumes =
        {
            ("DustEntrance", new Vector3(100f, 6.8f, -58f), new Vector3(10f, 3.2f, 8f)),
            ("DustHallWest", new Vector3(84f, 6.8f, -55f), new Vector3(14f, 3.2f, 6f)),
            ("DustHallEast", new Vector3(116f, 6.8f, -55f), new Vector3(14f, 3.2f, 6f)),
            ("DustStairs", new Vector3(100f, 9f, -42f), new Vector3(12f, 6f, 8f)),
            ("DustUpperHall", new Vector3(100f, 10.4f, -54f), new Vector3(40f, 3f, 6f)),
            ("DustBasement", new Vector3(118f, 2.8f, -60f), new Vector3(8f, 2.6f, 18f)),
        };
        foreach ((string name, Vector3 centre, Vector3 size) v in volumes)
        {
            GameObject d = (GameObject)PrefabUtility.InstantiatePrefab(dust, group);
            PrefabUtility.UnpackPrefabInstance(d, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            d.name = v.name;
            foreach (MonoBehaviour mb in d.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(mb); // DustMotes would re-parent it onto the camera
            d.transform.position = v.centre;
            ParticleSystem ps = d.GetComponent<ParticleSystem>();
            if (ps == null) continue;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = v.size;
            var emission = ps.emission;
            emission.rateOverTime = Mathf.Clamp(v.size.x * v.size.y * v.size.z * 0.03f, 5f, 14f);
            var main = ps.main;
            main.prewarm = true;
            main.maxParticles = 160;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
        }
    }
}
