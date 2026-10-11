using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// F98: the hospital's people and their jobs, added to the EXISTING hub non-destructively (the prefab is hand-edited, never regenerated).
/// <see cref="AddNpcs"/> only adds each piece when its named child is absent: Life/Npcs/{DialoguePanel, Agnes, Bruno, Josef, Marta, Weiss,
/// Clerk}, Life/Missions/Floor1..4 (a HubMission each, with its HubMissionItems, and one shared goal light), and ~45 vignette props under
/// Dressing/&lt;Room&gt;/F98_*. Everything is positioned by probing the existing hub (renderer bounds, capsule checks, the walk routes), never
/// by assumed pivots; <see cref="NpcRepair"/> then applies the instance to the prefab (additions only). Spec: plannings/hub-npcs-missions-plan.md.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string NpcsName = "Npcs";
    private const string MissionsName = "Missions";
    private const string NpcMaterialFolder = "Assets/SourceFiles/Materials/Asylum/Npcs";
    private const string TypingSource = "D:/Unity/essentials/Assets/Mixamo/Typing.fbx";
    private const string TypingProject = "Assets/SourceFiles/Animation/Mixamo/Typing.fbx";
    private const string CharMixamoDir = "Assets/SourceFiles/Characters/Mixamo/";
    private static readonly string[] NpcNames = { "Agnes", "Bruno", "Josef", "Marta", "Weiss", "Clerk" };

    // ---------------------------------------------------------------- ProjectSetup entry points

    /// <summary>What F98 still lacks in the scene hub (null = complete, or no hub at all).</summary>
    public static string NpcProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null; // the "Asylum hub" piece reports that
        List<string> missing = NpcMissing(hub);
        return missing.Count == 0 ? null : "missing: " + string.Join(", ", missing);
    }

    private static List<string> NpcMissing(AsylumHub hub)
    {
        List<string> missing = new List<string>();
        Transform life = hub.life;
        Transform npcs = life != null ? life.Find(NpcsName) : null;
        if (npcs == null) missing.Add("Life/Npcs");
        else
        {
            if (npcs.Find("DialoguePanel") == null) missing.Add("Life/Npcs/DialoguePanel");
            foreach (string n in NpcNames) if (npcs.Find(n) == null) missing.Add("Life/Npcs/" + n);
        }
        Transform missions = life != null ? life.Find(MissionsName) : null;
        for (int f = 1; f <= 4; f++) if (missions == null || missions.Find("Floor" + f) == null) missing.Add($"Life/Missions/Floor{f}");
        int props = CountF98Props(hub);
        if (props < 30) missing.Add($"F98 props ({props} of at least 30)");
        return missing;
    }

    private static int CountF98Props(AsylumHub hub)
    {
        int n = 0;
        if (hub.dressing == null) return 0;
        foreach (Transform room in hub.dressing)
        {
            foreach (Transform t in room) if (t.name.StartsWith("F98_")) n++;
        }
        return n;
    }

    /// <summary>ProjectSetup's fix: add what is missing to the scene hub and apply the additions to the prefab. Marks the scene dirty; the caller saves.</summary>
    public static void NpcRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Asylum NPCs: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        AddNpcs(hub);
        ApplyHubToPrefab(hub);
    }

    // ---------------------------------------------------------------- the layer

    public static string AddNpcs(AsylumHub hub)
    {
        System.Text.StringBuilder report = new System.Text.StringBuilder();
        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        EnsureFolderPath(NpcMaterialFolder);
        _routes = RoutePaths(hub);
        ComputeKeepOut(hub);

        if (hub.life == null)
        {
            Debug.LogError("Asylum NPCs: the hub has no Life group - run Build Missing Pieces (story layer) first.");
            return "no life group";
        }
        Transform life = hub.life;
        EnsureTypingClip(report);
        AnimatorController people = PeopleController();

        Transform npcs = life.Find(NpcsName) ?? Group(life, NpcsName);
        if (npcs.Find("DialoguePanel") == null) Group(npcs, "DialoguePanel").gameObject.AddComponent<DialoguePanel>();

        Dictionary<string, HubNpc> built = new Dictionary<string, HubNpc>();
        foreach (string n in NpcNames)
        {
            Transform existing = npcs.Find(n);
            if (existing != null) { built[n] = existing.GetComponent<HubNpc>(); continue; }
            HubNpc npc = BuildNpc(hub, npcs, n, people, report);
            if (npc != null) built[n] = npc;
        }

        Transform missions = life.Find(MissionsName) ?? Group(life, MissionsName);
        BuildMissions(hub, missions, built, report);

        BuildNpcProps(hub, built, report);
        report.AppendLine(OpenDoorsToNpcSpots(hub));

        // The hub NavMesh last: it is baked from the colliders in place (the NPCs and props added colliders).
        report.AppendLine("navmesh rebake: " + HubNavMesh.Bake(hub));

        if (!wasActive) hub.SetContentActive(false);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        string text = report.ToString().Trim();
        Debug.Log("Asylum NPCs + missions: " + (text.Length == 0 ? "nothing to add." : "\n" + text));
        return text;
    }

    private static void ComputeKeepOut(AsylumHub hub)
    {
        _keepOut = new List<Vector3>();
        HashSet<Vector2Int> seen = new HashSet<Vector2Int>();
        foreach (Vector3[] route in RoutePaths(hub))
        {
            foreach (Vector3 p in route)
            {
                Vector2Int k = new Vector2Int(Mathf.RoundToInt(p.x * 2f), Mathf.RoundToInt(p.z * 2f) + 100000 * Mathf.RoundToInt(p.y));
                if (seen.Add(k)) _keepOut.Add(p);
            }
        }
        foreach (WingDoor d in hub.wingDoors) { if (d != null && d.arrivalPoint != null) _keepOut.Add(d.arrivalPoint.position); }
        _keepOut.Add(hub.mainEntrance.position);
    }

    // ---------------------------------------------------------------- the Typing clip

    /// <summary>Copies Mixamo's Typing clip from the shared library into the project (never moves it) and imports it like the others (Humanoid, looping).</summary>
    private static void EnsureTypingClip(System.Text.StringBuilder report)
    {
        if (!System.IO.File.Exists(System.IO.Path.GetFullPath(TypingProject)))
        {
            if (!System.IO.File.Exists(TypingSource)) { report.AppendLine("Typing clip: source missing, the Clerk will stand idle"); return; }
            EnsureFolderPath(System.IO.Path.GetDirectoryName(TypingProject).Replace('\\', '/'));
            System.IO.File.Copy(TypingSource, System.IO.Path.GetFullPath(TypingProject));
            AssetDatabase.ImportAsset(TypingProject, ImportAssetOptions.ForceUpdate);
            report.AppendLine("Typing clip copied from the Mixamo library");
        }
        ModelImporter importer = AssetImporter.GetAtPath(TypingProject) as ModelImporter;
        if (importer == null) return;
        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Human) { importer.animationType = ModelImporterAnimationType.Human; dirty = true; }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        if (clips.Length > 0 && (!clips[0].loopTime || clips[0].name != "Typing"))
        {
            clips[0].name = "Typing";
            clips[0].loopTime = true;
            clips[0].keepOriginalOrientation = true;
            clips[0].keepOriginalPositionY = true;
            clips[0].keepOriginalPositionXZ = true;
            importer.clipAnimations = clips;
            dirty = true;
        }
        if (dirty) importer.SaveAndReimport();
    }

    // ---------------------------------------------------------------- bodies

    private sealed class NpcSpec
    {
        public string name;
        public HubNpc.Who who;
        public string display;
        public float scale = 1f;
        public string baseState = "Look", talkState = "Talking";
        public bool lying, turnsHead = true, seated, faceMe = true;
    }

    private static HubNpc BuildNpcHolder(Transform npcs, NpcSpec spec, AnimatorController people, Vector3 root, float yaw, Vector3 floorPoint, System.Text.StringBuilder report)
    {
        GameObject holderGo = new GameObject(spec.name);
        holderGo.transform.SetParent(npcs, false);
        HubNpc npc = holderGo.AddComponent<HubNpc>();
        npc.who = spec.who;
        npc.displayName = spec.display;
        npc.controller = people;
        npc.baseState = spec.baseState;
        npc.talkState = spec.talkState;
        npc.lying = spec.lying;
        npc.turnsHead = spec.turnsHead;
        npc.playerFacesMe = spec.faceMe;
        npc.anchor = Anchor(holderGo.transform, "Anchor", root, yaw);

        // Timmy in the NPC's own skin (the Adam Character Pack bodies were removed for licence reasons).
        Material skinMat = HunterSkinBuilder.EnsureNpcSkin(spec.who);
        Transform body = skinMat != null ? RobotBodyLike(holderGo.transform, null, skinMat) : null;
        if (body == null)
        {
            Debug.LogError($"Asylum NPCs: the robot body or skin could not be built, {spec.name} has no body.");
            return npc;
        }
        body.GetComponent<Animator>().runtimeAnimatorController = people;
        body.SetPositionAndRotation(root, Quaternion.Euler(0f, yaw, 0f));
        body.localScale = Vector3.one * spec.scale;
        npc.body = body;

        // The collider lives on its own child at floor level, so it never moves with the seated / lying body offsets.
        GameObject colGo = new GameObject("Collider");
        colGo.transform.SetParent(holderGo.transform, false);
        colGo.transform.SetPositionAndRotation(floorPoint, Quaternion.Euler(0f, yaw, 0f));
        if (spec.lying)
        {
            colGo.transform.position = floorPoint - Quaternion.Euler(0f, yaw, 0f) * Vector3.forward * 0.95f + Vector3.up * 0.35f;
            BoxCollider box = colGo.AddComponent<BoxCollider>();
            box.size = new Vector3(0.95f, 0.8f, 2.0f);
            box.isTrigger = true;
        }
        else
        {
            CapsuleCollider capsule = colGo.AddComponent<CapsuleCollider>();
            float h = spec.seated ? 1.3f * spec.scale : 1.8f * spec.scale;
            capsule.height = h;
            capsule.radius = 0.32f * Mathf.Max(0.8f, spec.scale);
            capsule.center = new Vector3(0f, h * 0.5f, 0f);
        }
        report.AppendLine($"{spec.name} ({spec.who}) at {root} yaw {yaw:F0}, {(spec.lying ? "lying" : spec.seated ? "seated" : "standing")}");
        return npc;
    }

    private static float GroundYAt(Vector3 p, float hintY)
    {
        return FloorAt(p, hintY + 1.5f, out float y) ? y : hintY;
    }

    private static Vector3 SpotOrFallback(string who, Vector3 around, float rMin, float rMax, float floorY, Vector3 lookAt, Vector3 fallback)
    {
        if (FreeSpotNear(around, rMin, rMax, floorY, lookAt, out Vector3 spot)) return spot;
        Debug.LogError($"Asylum NPCs: no free spot for {who} near {around}; using {fallback}.");
        return fallback;
    }

    private static HubNpc BuildNpc(AsylumHub hub, Transform npcs, string name, AnimatorController people, System.Text.StringBuilder report)
    {
        switch (name)
        {
            case "Agnes":
            {
                // The nurse station: the Gatehouse beside the entrance hall, standing between her desk and the door.
                float fy = GroundYAt(new Vector3(95f, 5f, -58f), 5f);
                Vector3 look = new Vector3(95.2f, fy, -56.8f);
                Vector3 spot = SpotOrFallback(name, new Vector3(94.7f, fy, -58.3f), 0f, 1.6f, fy, look, new Vector3(94.7f, fy, -58.3f));
                NpcSpec spec = new NpcSpec { name = name, who = HubNpc.Who.Agnes, display = HubDialogue.Agnes, baseState = "Look", talkState = "Talking" };
                return BuildNpcHolder(npcs, spec, people, spot, YawTo(spot, look), spot, report);
            }
            case "Bruno":
            {
                float fy = GroundYAt(new Vector3(76f, 5f, -48f), 5f);
                Vector3 table = new Vector3(78.9f, fy, -48.4f);
                Vector3 spot = SpotOrFallback(name, new Vector3(75.6f, fy, -48.6f), 0f, 2.4f, fy, table, new Vector3(75.6f, fy, -48.6f));
                NpcSpec spec = new NpcSpec { name = name, who = HubNpc.Who.Bruno, display = HubDialogue.Bruno, baseState = "Nervous", talkState = "Talking" };
                return BuildNpcHolder(npcs, spec, people, spot, YawTo(spot, table), spot, report);
            }
            case "Josef":
            {
                Renderer mattress = NearestRenderer("Mattress", new Vector3(74.87f, 9.26f, -75.35f), 0.6f);
                Renderer pillow = NearestRenderer("Pillow", new Vector3(74.15f, 9.37f, -75.34f), 0.6f);
                if (mattress == null)
                {
                    Debug.LogError("Asylum NPCs: no bed found for Josef in the Bedroom.");
                    return null;
                }
                Bounds mb = mattress.bounds;
                bool alongX = mb.size.x >= mb.size.z;
                Vector3 headDir = alongX ? new Vector3(Mathf.Sign((pillow != null ? pillow.bounds.center.x : mb.center.x - 1f) - mb.center.x), 0f, 0f)
                                         : new Vector3(0f, 0f, Mathf.Sign((pillow != null ? pillow.bounds.center.z : mb.center.z - 1f) - mb.center.z));
                float length = alongX ? mb.size.x : mb.size.z;
                Vector3 feet = new Vector3(mb.center.x, 0f, mb.center.z) - headDir * Mathf.Min(length * 0.5f - 0.1f, 0.95f);
                feet.y = mb.max.y + 0.13f;
                float yaw = Quaternion.LookRotation(-headDir, Vector3.up).eulerAngles.y;
                NpcSpec spec = new NpcSpec { name = name, who = HubNpc.Who.Josef, display = HubDialogue.Josef, baseState = "Lie", talkState = "", lying = true, turnsHead = false };
                return BuildNpcHolder(npcs, spec, people, feet, yaw, feet, report);
            }
            case "Marta":
            {
                // The classroom: a child-sized chair in front of the blackboard, facing it.
                RoomDef def = DressRooms.First(d => d.name == "Classroom");
                Vector3 board = new Vector3(131.6f, def.floorY, -47.7f);
                Vector3 spot = SpotOrFallback(name, new Vector3(128.6f, def.floorY, -47.6f), 0f, 2.2f, def.floorY, board, new Vector3(128.6f, def.floorY, -47.6f));
                float yaw = YawTo(spot, board);
                DressRoomCtx r = NewDressCtx(hub, def);
                Placed chair = Put(r, "ChairSchool", spot, yaw + ChairSchoolYawFix, 1f, null, false, false);
                FinishDressCtx(r);
                NpcSpec spec = new NpcSpec { name = name, who = HubNpc.Who.Marta, display = HubDialogue.Marta, scale = 0.82f, baseState = "SitIdle", talkState = "", seated = true };
                return BuildNpcHolder(npcs, spec, people, spot + Vector3.up * SeatLift, yaw, spot, report);
            }
            case "Weiss":
            {
                RoomDef def = DressRooms.First(d => d.name == "ExamRoom");
                Vector3 table = new Vector3(87.7f, def.floorY, -57.3f);
                Vector3 spot = SpotOrFallback(name, new Vector3(88.6f, def.floorY, -59.6f), 0f, 2.4f, def.floorY, table, new Vector3(88.6f, def.floorY, -59.6f));
                NpcSpec spec = new NpcSpec { name = name, who = HubNpc.Who.Weiss, display = HubDialogue.Weiss, baseState = "Nervous", talkState = "Talking" };
                return BuildNpcHolder(npcs, spec, people, spot, YawTo(spot, table), spot, report);
            }
            default: // Clerk
            {
                // Records: seated on the pack's own office chair at the pack's own desk against the east wall.
                Renderer chair = NearestRenderer("ChairOffice", new Vector3(91.7f, 5.4f, -62.6f), 1.2f);
                Renderer desk = NearestRenderer("TableOffice", new Vector3(92.6f, 5.4f, -62.8f), 1.2f);
                Vector3 seat = chair != null ? new Vector3(chair.bounds.center.x, 0f, chair.bounds.center.z) : new Vector3(91.7f, 0f, -62.6f);
                Vector3 deskC = desk != null ? new Vector3(desk.bounds.center.x, 0f, desk.bounds.center.z) : new Vector3(92.6f, 0f, -62.8f);
                float fy = FloorHeight("OfficeA", RoomBounds(_building, "OfficeA", 0f)); // not a raycast: it would land on the chair
                seat.y = fy;
                deskC.y = fy;
                NpcSpec spec = new NpcSpec { name = name, who = HubNpc.Who.Clerk, display = HubDialogue.Clerk, baseState = "Typing", talkState = "", seated = true, turnsHead = false };
                return BuildNpcHolder(npcs, spec, people, seat + Vector3.up * SeatLift, YawTo(seat, deskC), seat, report);
            }
        }
    }

    /// <summary>How far the Mixamo sitting clips need the body root raised above the floor to sit on a ~0.45 m seat (tuned by eye in Play).</summary>
    private const float SeatLift = 0.0f;
    /// <summary>Degrees added to the school chair's yaw so its backrest is behind the sitter (tuned by eye).</summary>
    private const float ChairSchoolYawFix = 180f;

    // ---------------------------------------------------------------- missions

    private static HubMissionItem MakeItem(Transform parent, string name, Vector3 topPoint, GameObject visualRoot, string prompt, string takenLine, HubMission mission)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = topPoint;
        if (visualRoot != null)
        {
            visualRoot.transform.SetParent(go.transform, true);
            Bounds b = RendBounds(visualRoot);
            visualRoot.transform.position += topPoint - new Vector3(b.center.x, b.min.y, b.center.z);
        }
        Bounds vb = visualRoot != null ? RendBounds(visualRoot) : new Bounds(topPoint + Vector3.up * 0.1f, Vector3.one * 0.2f);
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = go.transform.InverseTransformPoint(vb.center);
        box.size = Vector3.Max(vb.size + Vector3.one * 0.12f, new Vector3(0.4f, 0.4f, 0.4f));
        HubMissionItem item = go.AddComponent<HubMissionItem>();
        SerializedObject so = new SerializedObject(item);
        so.FindProperty("mission").objectReferenceValue = mission;
        so.FindProperty("prompt").stringValue = prompt;
        so.FindProperty("takenLine").stringValue = takenLine;
        so.ApplyModifiedPropertiesWithoutUndo();
        go.SetActive(false); // HubMission shows it while the mission is Active
        return item;
    }

    private static GameObject Visual(string key, string name, float scale = 1f)
    {
        GameObject root = new GameObject(name);
        GameObject go = Spawn(key, root.transform);
        if (go == null) return root;
        go.transform.localScale *= scale;
        return root;
    }

    private static void SeatOn(GameObject child, GameObject host, float dx = 0f, float dz = 0f)
    {
        if (child == null || host == null) return;
        Bounds hb = RendBounds(host), cb = RendBounds(child);
        child.transform.position += new Vector3(hb.center.x + dx - cb.center.x, hb.max.y - cb.min.y, hb.center.z + dz - cb.center.z);
    }

    /// <summary>The top of the first renderer whose name starts with one of `names` inside the room rectangle, nearest to `prefer`. Null = none.</summary>
    private static Vector3? TopOf(RoomDef def, string[] names, Vector3 prefer)
    {
        Renderer best = null;
        float bestD = float.MaxValue;
        foreach (Renderer r in _building.GetComponentsInChildren<Renderer>(true))
        {
            Vector3 c = r.bounds.center;
            if (c.x < def.minX || c.x > def.maxX || c.z < def.minZ || c.z > def.maxZ || Mathf.Abs(r.bounds.min.y - def.floorY) > 0.8f) continue;
            if (!names.Any(n => r.name.StartsWith(n))) continue;
            if (r.bounds.size.x < 0.5f && r.bounds.size.z < 0.5f) continue;
            float d = (new Vector3(c.x, 0f, c.z) - new Vector3(prefer.x, 0f, prefer.z)).sqrMagnitude;
            if (d < bestD) { bestD = d; best = r; }
        }
        if (best == null) return null;
        return new Vector3(best.bounds.center.x, best.bounds.max.y, best.bounds.center.z);
    }

    private static RoomDef Def(string room) => DressRooms.First(d => d.name == room);

    private static void BuildMissions(AsylumHub hub, Transform missions, Dictionary<string, HubNpc> npcs, System.Text.StringBuilder report)
    {
        // One goal light for all four missions: a small warm glow moved to whatever is left to find.
        Transform glowT = missions.Find("GoalGlow");
        if (glowT == null)
        {
            glowT = Group(missions, "GoalGlow");
            Light light = glowT.gameObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.82f, 0.5f);
            light.range = 3.2f;
            light.intensity = 0.6f;
            light.shadows = LightShadows.None;
            light.enabled = false; // HubMission switches it on while there is something to find
        }
        Light glow = glowT.GetComponent<Light>();

        for (int floor = 1; floor <= 4; floor++)
        {
            if (missions.Find("Floor" + floor) != null) continue;
            Transform group = Group(missions, "Floor" + floor);
            HubMission mission = group.gameObject.AddComponent<HubMission>();
            List<HubMissionItem> items = new List<HubMissionItem>();

            string title = "", locked = "", keyName = "", offered = "", active = "", fetched = "", progActive = "", progFetched = "";
            HubNpc giver = null, recipient = null;
            switch (floor)
            {
                case 1:
                {
                    title = "THE WARD KEYS"; locked = "LOCKED · SISTER AGNES HAS THE KEY · NURSE STATION"; keyName = "THE WARD A KEY";
                    offered = "TALK TO SISTER AGNES · NURSE STATION"; active = "FIND AGNES'S KEY RING · THE KITCHEN"; fetched = "BRING THE KEY RING BACK TO SISTER AGNES";
                    npcs.TryGetValue("Agnes", out giver);
                    Vector3 top = TopOf(Def("KitchenA"), new[] { "TableKitchen" }, new Vector3(82.9f, 5.8f, -48.4f)) ?? new Vector3(82.9f, 5.8f, -48.4f);
                    GameObject visual = BuildKeyRingVisual();
                    items.Add(MakeItem(group, "Item_KeyRing", top + new Vector3(0.3f, 0f, 0f), visual, "TAKE THE KEY RING", "KEY RING · TAKEN", mission));
                    break;
                }
                case 2:
                {
                    title = "THE TRAY"; locked = "LOCKED · BRUNO HAS THE KEY · KITCHEN"; keyName = "THE BOILER ROOM KEY";
                    offered = "TALK TO BRUNO · KITCHEN"; active = "TAKE JOSEF'S SUPPER TRAY FROM THE KITCHEN"; fetched = "TAKE THE TRAY UP TO JOSEF · BEDROOM"; progFetched = "CARRYING · JOSEF'S TRAY";
                    npcs.TryGetValue("Bruno", out giver);
                    npcs.TryGetValue("Josef", out recipient);
                    Vector3 top = TopOf(Def("KitchenA"), new[] { "TableKitchen" }, new Vector3(80.9f, 5.8f, -48.3f)) ?? new Vector3(80.9f, 5.8f, -48.3f);
                    items.Add(MakeItem(group, "Item_Tray", top + new Vector3(-0.2f, 0f, 0f), BuildTrayVisual(), "TAKE JOSEF'S TRAY", "CARRYING · JOSEF'S TRAY", mission));
                    break;
                }
                case 3:
                {
                    title = "THE MUSIC BOX"; locked = "LOCKED · MARTA HAS THE KEY · CLASSROOM"; keyName = "THE MORGUE KEY";
                    offered = "TALK TO MARTA · CLASSROOM"; active = "FIND MARTA'S MUSIC BOX · THE BASEMENT HALL"; fetched = "BRING THE MUSIC BOX BACK TO MARTA";
                    npcs.TryGetValue("Marta", out giver);
                    Vector3 spot = MusicBoxSpot(hub);
                    items.Add(MakeItem(group, "Item_MusicBox", spot, BuildMusicBoxVisual(), "TAKE THE MUSIC BOX", "MUSIC BOX · TAKEN", mission));
                    break;
                }
                default:
                {
                    title = "ROUNDS"; locked = "LOCKED · DR. WEISS HAS THE KEY · EXAM ROOM"; keyName = "THE SURGERY KEY";
                    offered = "TALK TO DR. WEISS · EXAM ROOM"; active = "FIND THE THREE AMPOULES · UPPER FLOOR"; fetched = "BRING THE AMPOULES TO DR. WEISS";
                    progActive = "AMPOULES  {n} / {total}"; progFetched = "AMPOULES  {n} / {total}";
                    npcs.TryGetValue("Weiss", out giver);
                    (string room, string[] names, Vector3 prefer)[] spots =
                    {
                        ("Surgery", new[] { "SurgeryTable", "TableWhite", "TableOffice", "Table" }, new Vector3(97.5f, 8.8f, -60.5f)),
                        ("Office", new[] { "TableOffice", "Table" }, new Vector3(107.5f, 8.8f, -60.5f)),
                        ("Bedroom", new[] { "Table_Bed", "Table" }, new Vector3(79f, 8.8f, -66f)),
                    };
                    for (int i = 0; i < spots.Length; i++)
                    {
                        Vector3? top = TopOf(Def(spots[i].room), spots[i].names, spots[i].prefer);
                        if (top == null) { Debug.LogError($"Asylum NPCs: no table for ampoule {i + 1} in the {spots[i].room}."); continue; }
                        items.Add(MakeItem(group, "Item_Ampoule" + (i + 1), top.Value + new Vector3(0.12f * (i - 1), 0f, 0.1f), BuildAmpouleVisual(i), "TAKE THE AMPOULE", "AMPOULE {n} / {total}", mission));
                    }
                    break;
                }
            }

            SerializedObject so = new SerializedObject(mission);
            so.FindProperty("floor").intValue = floor;
            so.FindProperty("title").stringValue = title;
            so.FindProperty("giver").objectReferenceValue = giver;
            so.FindProperty("recipient").objectReferenceValue = recipient;
            SerializedProperty arr = so.FindProperty("items");
            arr.arraySize = items.Count;
            for (int i = 0; i < items.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
            so.FindProperty("lockedLine").stringValue = locked;
            so.FindProperty("keyName").stringValue = keyName;
            so.FindProperty("objectiveOffered").stringValue = offered;
            so.FindProperty("objectiveActive").stringValue = active;
            so.FindProperty("objectiveFetched").stringValue = fetched;
            so.FindProperty("progressActive").stringValue = progActive;
            so.FindProperty("progressFetched").stringValue = progFetched;
            so.FindProperty("goalLight").objectReferenceValue = glow;
            so.ApplyModifiedPropertiesWithoutUndo();
            report.AppendLine($"Mission floor {floor} '{title}': {items.Count} item(s) at {string.Join(", ", items.Select(i => i.transform.position.ToString("F1")))}");
        }
    }

    // ---- item visuals (all pack models, no primitives)

    private static GameObject BuildKeyRingVisual()
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        GameObject root = new GameObject("KeyRing");
        if (kit != null && kit.KeyPickup != null)
        {
            KeyPickup clone = Object.Instantiate(kit.KeyPickup, root.transform);
            clone.gameObject.SetActive(true);
            clone.name = "Key";
            foreach (Component c in clone.GetComponentsInChildren<Component>(true).ToArray())
            {
                if (c is KeyPickup || c is Collider || c is Light || c is AudioSource) Object.DestroyImmediate(c);
            }
            Transform key = root.transform.Find("Key");
            if (key != null)
            {
                // The template is a key on a wall plaque with a hook; on a counter only the key, its tag and its ring stay.
                foreach (string part in new[] { "Plate", "Hook" })
                {
                    Transform t = key.Find(part);
                    if (t != null) Object.DestroyImmediate(t.gameObject);
                }
                key.localRotation = Quaternion.Euler(90f, 0f, 0f); // lying flat on the counter
                key.localScale *= 1.6f;
            }
        }
        else Debug.LogError("Asylum NPCs: the key template (LIGHTS OUT > Build > Interactables) is missing; the key ring has no model.");
        return root;
    }

    private static GameObject BuildTrayVisual()
    {
        GameObject root = new GameObject("Tray");
        GameObject tray = Spawn("m:instrument_tray", root.transform);
        if (tray != null)
        {
            tray.transform.localScale *= 1.25f;
            GameObject plate = Spawn("Plate", root.transform);
            SeatOn(plate, tray, 0.0f, 0.0f);
            GameObject mug = Spawn("Mug", root.transform);
            SeatOn(mug, tray, 0.14f, 0.1f);
        }
        return root;
    }

    private static GameObject BuildMusicBoxVisual()
    {
        GameObject root = Visual("path:Assets/BK_AlchemistHouse/Prefabs/Items/Misc/Trunk01.prefab", "MusicBox");
        Bounds b = RendBounds(root);
        if (b.size.x > 0.01f) root.transform.localScale *= 0.3f / Mathf.Max(b.size.x, b.size.z); // a hand-sized box
        return root;
    }

    private static GameObject BuildAmpouleVisual(int index)
    {
        string flask = new[] { "Flask03", "Flask05", "Flask07" }[index % 3];
        GameObject root = Visual($"path:Assets/BK_AlchemistHouse/Prefabs/Items/Bottles/{flask}.prefab", "Ampoule" + (index + 1));
        Bounds b = RendBounds(root);
        if (b.size.y > 0.01f) root.transform.localScale *= 0.2f / b.size.y;
        return root;
    }

    /// <summary>The basement hall's wall cell nearest to its middle: the music box lies on the floor by the wall.</summary>
    private static Vector3 MusicBoxSpot(AsylumHub hub)
    {
        RoomDef def = Def("HallUnderground");
        DressRoomCtx r = new DressRoomCtx { def = def, group = hub.dressing, rng = new System.Random(StableHash("F98musicbox")) };
        ScanCells(r);
        Vector3 middle = new Vector3((def.minX + def.maxX) * 0.5f, def.floorY, (def.minZ + def.maxZ) * 0.5f);
        Cell best = null;
        float bestD = float.MaxValue;
        foreach (Cell c in r.cells)
        {
            if (!c.wall || c.dist > 0.8f) continue;
            if (_routes.Any(route => DistanceToPath(c.pos, route) < 0.6f)) continue; // not right on the route
            float d = (new Vector3(c.pos.x, 0f, c.pos.z) - new Vector3(middle.x, 0f, middle.z)).sqrMagnitude;
            if (d < bestD) { bestD = d; best = c; }
        }
        if (best != null) return best.pos;
        Debug.LogWarning("Asylum NPCs: no wall cell off the route in the basement hall for the music box; using the hall middle.");
        return middle;
    }

    // ---------------------------------------------------------------- doors

    private static float StoreyFloor(float y)
    {
        float floor = y;
        foreach (float f in new[] { 0.07f, 1.15f, 5.0f, 8.82f }) if (f <= y + 0.6f && y - f < 1.7f) floor = f;
        return floor;
    }

    /// <summary>The pack closes about half of its non-route doors for good (they are never interactable), so an NPC or an item behind one cannot be reached.</summary>
    private static string OpenDoorsToNpcSpots(AsylumHub hub)
    {
        if (hub.life == null) return "";
        List<(string label, Vector3 centre, float floorY)> targets = new List<(string, Vector3, float)>();
        foreach (HubNpc npc in hub.life.GetComponentsInChildren<HubNpc>(true))
        {
            Vector3 c = npc.anchor != null ? npc.anchor.position : npc.transform.position;
            float floorY = StoreyFloor(c.y);
            if (npc.lying) c -= npc.anchor.forward * 0.95f;
            targets.Add((npc.name, c, floorY));
        }
        foreach (HubMissionItem item in hub.life.GetComponentsInChildren<HubMissionItem>(true))
        {
            targets.Add((item.name, item.transform.position, StoreyFloor(item.transform.position.y)));
        }

        List<PackDoor> doors = FindPackDoors();
        // The first draft left some doors ajar (25 degrees, on a hinge object): too narrow a gap to walk through. Find them too.
        List<(Transform hinge, Transform root)> ajar = new List<(Transform, Transform)>();
        foreach (Transform t in _building.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != "DoorHinge" && t.name != "DoorHingeB") continue;
            float yaw = Mathf.DeltaAngle(0f, t.localEulerAngles.y);
            if (Mathf.Abs(yaw) > 5f && Mathf.Abs(yaw) < 80f && t.parent != null) ajar.Add((t, t.parent));
        }
        List<Collider> ajarColliders = ajar.SelectMany(a => a.hinge.GetComponentsInChildren<Collider>(true)).Where(c => c.enabled).ToList();
        SetLeafColliders(doors, false); // every door passable: find the way, then see which closed or ajar doors it crosses
        foreach (Collider c in ajarColliders) c.enabled = false;
        Physics.SyncTransforms();
        WalkGrid grid = BuildWalkGrid(hub);
        foreach (Collider c in ajarColliders) c.enabled = true;
        SetLeafColliders(doors, true);
        int start = grid.Nearest(hub.mainEntrance.position);
        if (start < 0) return "doors: no walk graph";
        grid.Flood(start, out int[] parent);

        HashSet<PackDoor> toOpen = new HashSet<PackDoor>();
        HashSet<Transform> ajarToOpen = new HashSet<Transform>();
        foreach ((string label, Vector3 centre, float floorY) in targets)
        {
            Vector3[] path = null;
            foreach (float radius in new[] { 1.0f, 1.3f, 1.6f, 1.9f })
            {
                for (int i = 0; i < 16 && path == null; i++)
                {
                    float a = i * Mathf.PI * 2f / 16f;
                    Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    p.y = floorY;
                    int node = grid.Nearest(p);
                    if (node < 0 || (grid.Pos(node) - p).sqrMagnitude > 0.09f || parent[node] < 0) continue;
                    path = grid.PathTo(node, parent);
                }
                if (path != null) break;
            }
            if (path == null) continue;
            foreach ((Transform hinge, Transform root) in ajar)
            {
                Renderer[] rs = root.GetComponentsInChildren<Renderer>(true);
                if (rs.Length == 0) continue;
                Bounds b = rs[0].bounds;
                foreach (Renderer r in rs) b.Encapsulate(r.bounds);
                if (DistanceToPath(b.center, path) < 1.1f) ajarToOpen.Add(hinge);
            }
            foreach (PackDoor d in doors)
            {
                if (d.Root.Find(OpenedMarker) == null && IsClosedDoor(d) && DistanceToPath(DoorCentre(d), path) < 1.1f) toOpen.Add(d);
            }
        }
        foreach (PackDoor d in toOpen) SwingDoorInPlace(d, 95f);
        foreach (Transform hinge in ajarToOpen)
        {
            float yaw = Mathf.DeltaAngle(0f, hinge.localEulerAngles.y);
            hinge.localRotation = Quaternion.AngleAxis(Mathf.Sign(yaw) * 95f - yaw, Vector3.up) * hinge.localRotation; // swing the same way, all the way
        }
        Physics.SyncTransforms();
        return $"ajar doors opened wide: {ajarToOpen.Count}; closed doors opened: {toOpen.Count}" + (toOpen.Count > 0 ? " (" + string.Join(", ", toOpen.Select(d => d.Root.name + " at " + DoorCentre(d).ToString("F1"))) + ")" : "");
    }

    private const string OpenedMarker = "F98_Opened";

    /// <summary>
    /// A door is shut while its leaves lie across the opening: their extent along the door's own x axis is larger than along its z axis.
    /// (A leaf the user or the first draft already swung - with or without a hinge object - is wide in z and is left alone.)
    /// </summary>
    private static bool IsClosedDoor(PackDoor d)
    {
        foreach (Transform leaf in d.Leaves)
        {
            Renderer r = leaf.GetComponent<Renderer>();
            if (r == null) continue;
            Bounds b = r.bounds;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                Vector3 local = d.Root.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
            if (max.x - min.x <= max.z - min.z) return false;
        }
        return true;
    }

    /// <summary>
    /// SwingDoor without the hinge object: the hub is a prefab instance, and a prefab instance's transforms cannot be re-parented, so each
    /// leaf (and the window pane that belongs to it) is turned about its edge in place, and an empty marker child records that it was done.
    /// </summary>
    private static void SwingDoorInPlace(PackDoor door, float angle)
    {
        Vector3 centre = DoorCentre(door);
        float front = FreeDistance(centre, door.Root.forward), back = FreeDistance(centre, -door.Root.forward);
        float sign = front >= back ? 1f : -1f;

        for (int i = 0; i < door.Leaves.Count; i++)
        {
            Transform leaf = door.Leaves[i];
            Renderer lr = leaf.GetComponent<Renderer>();
            bool rightLeaf = door.Double && door.Root.InverseTransformPoint(lr.bounds.center).x > 0f;
            // The leaf's extent along the door's own x axis.
            float min = float.MaxValue, max = float.MinValue;
            Bounds b = lr.bounds;
            for (int c = 0; c < 8; c++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3((c & 1) == 0 ? -1f : 1f, (c & 2) == 0 ? -1f : 1f, (c & 4) == 0 ? -1f : 1f));
                float x = door.Root.InverseTransformPoint(corner).x;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
            }
            Vector3 local = door.Root.InverseTransformPoint(b.center);
            local.x = rightLeaf ? max : min;
            Vector3 hinge = door.Root.TransformPoint(local);
            float yaw = sign * angle * (rightLeaf ? 1f : -1f);

            leaf.RotateAround(hinge, door.Root.up, yaw);
            foreach (Transform t in door.Root)
            {
                if (!t.name.Contains("Window")) continue;
                bool second = t.name.EndsWith("2");
                if (second == rightLeaf) t.RotateAround(hinge, door.Root.up, yaw);
            }
        }
        GameObject marker = new GameObject(OpenedMarker);
        marker.transform.SetParent(door.Root, false);
    }

    // ---------------------------------------------------------------- props

    private static DressRoomCtx NewDressCtx(AsylumHub hub, RoomDef def, string tag = "F98")
    {
        Transform group = hub.dressing.Find(def.name);
        if (group == null)
        {
            GameObject go = new GameObject(def.name);
            go.transform.SetParent(hub.dressing, false);
            group = go.transform;
        }
        DressRoomCtx r = new DressRoomCtx { def = def, group = group, rng = new System.Random(StableHash(tag + def.name)) };
        r.childStart = group.childCount;
        ScanCells(r);
        return r;
    }

    /// <summary>Names the new children F98_*, gives visual props the building's static flags (no batching) and returns how many were added.</summary>
    private static int FinishDressCtx(DressRoomCtx r, string namePrefix = "F98_")
    {
        const StaticEditorFlags flags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
        int added = 0;
        for (int i = r.childStart; i < r.group.childCount; i++)
        {
            Transform t = r.group.GetChild(i);
            if (!t.name.StartsWith(namePrefix)) t.name = namePrefix + t.name;
            foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
            {
                bool visual = c.GetComponent<Renderer>() != null || c.GetComponent<Collider>() != null;
                GameObjectUtility.SetStaticEditorFlags(c.gameObject, visual ? flags : 0);
            }
            added++;
        }
        Physics.SyncTransforms();
        return added;
    }

    private static void BuildNpcProps(AsylumHub hub, Dictionary<string, HubNpc> npcs, System.Text.StringBuilder report)
    {
        if (CountF98Props(hub) >= 30) return;
        Physics.SyncTransforms();
        int total = 0;

        // ---- Sister Agnes: the nurse station desk, a ledger, cups, a kettle on the side table
        {
            DressRoomCtx r = NewDressCtx(hub, Def("Gatehouse"));
            Placed desk = Existing(r, "TableOffice (2)");
            On(r, desk, "m:notebook", 0.2f, 0.0f, 12f);
            On(r, desk, "Mug", -0.45f, 0.5f);
            On(r, desk, "m:photo_frame", 0.5f, -0.6f, 200f);
            On(r, desk, "Paper", -0.2f, -0.4f, 25f);
            On(r, desk, "m:pencil_holder", 0.55f, 0.5f);
            Placed white = Existing(r, "TableWhite (2)");
            On(r, white, "Kettle", -0.1f, 0.1f);
            On(r, white, "Mug", 0.4f, -0.3f);
            Put(r, "Box_V1", new Vector3(93.7f, r.def.floorY, -63.0f), 0f, 1f, null, true);
            total += FinishDressCtx(r);
        }

        // ---- Bruno: pots and plates on the pass, a chair, crates
        {
            DressRoomCtx r = NewDressCtx(hub, Def("KitchenA"));
            Placed t1 = Existing(r, "TableKitchen (2)");
            Placed t2 = Existing(r, "TableKitchen (1)");
            On(r, t1, "Pot", -0.5f, 0f);
            On(r, t1, "Plate", 0.1f, 0.1f);
            On(r, t1, "Mug", 0.55f, -0.3f);
            On(r, t2, "Kettle", -0.6f, 0.2f);
            On(r, t2, "Plate", 0.55f, 0.2f);
            Put(r, "ChairWhite", new Vector3(76.0f, r.def.floorY, -49.6f), 200f);
            Put(r, "CanOld", new Vector3(75.2f, r.def.floorY, -46.9f), 40f);
            Scatter(r, 2, true, 2.5f, "Box_V1", "Box_V2");
            total += FinishDressCtx(r);
        }

        // ---- Josef: a bedside table with a cup, his crutches, a wheelchair
        {
            DressRoomCtx r = NewDressCtx(hub, Def("Bedroom"));
            Vector3 bed = new Vector3(74.87f, r.def.floorY, -75.35f);
            Placed host = new Placed { center = bed, size = new Vector3(2.0f, 0.5f, 0.9f), yaw = 0f };
            Placed table = Beside(r, host, "Table_Bed", 1.3f, 2.4f, 1f, false);
            if (table != null) { On(r, table, "Mug", 0f, 0f); On(r, table, "m:notebook", 0.2f, 0.1f, 40f); }
            Beside(r, host, "Crutches", 1.0f, 2.8f, 1f, false, false);
            Beside(r, host, "WheelChair", 2.0f, 4.0f, 1f, false);
            Litter(r, 2, "Paper", "Plate_Broken");
            total += FinishDressCtx(r);
        }

        // ---- Marta: the babies she plays with, drawings, a toy box
        {
            DressRoomCtx r = NewDressCtx(hub, Def("Classroom"));
            Placed table = Existing(r, "TableSchool (1)");
            On(r, table, "Paper", 0f, 0.2f, 15f);
            On(r, table, "Paper", 0.1f, -0.4f, 80f);
            Vector3 chairSpot = npcs.TryGetValue("Marta", out HubNpc marta) && marta.anchor != null ? marta.anchor.position : new Vector3(128.6f, r.def.floorY, -47.6f);
            Placed host = new Placed { center = chairSpot, size = Vector3.one, yaw = 0f };
            Beside(r, host, "alch:Alch_Babies", 1.0f, 2.4f, 1f, false, false);
            Beside(r, host, "Box_V2", 1.2f, 3.2f, 1f, false);
            Litter(r, 3, "Paper");
            total += FinishDressCtx(r);
        }

        // ---- Dr. Weiss: the examination table with papers, a tray and bottles; a cabinet
        {
            DressRoomCtx r = NewDressCtx(hub, Def("ExamRoom"));
            Placed table = Existing(r, "TableWhite (2)") ?? Existing(r, "TableWhite");
            On(r, table, "m:instrument_tray", -0.45f, 0f, 90f);
            On(r, table, "Paper", 0.2f, 0.1f, 20f);
            On(r, table, "Paper", 0.45f, -0.3f, 70f);
            On(r, table, "m:notebook", 0.0f, -0.4f, 160f);
            On(r, table, "Mug", 0.6f, 0.4f);
            On(r, table, "alch:Alch_BottlesA", -0.1f, 0.3f);
            Scatter(r, 1, true, 3f, "m:cabinet");
            Litter(r, 2, "Paper");
            total += FinishDressCtx(r);
        }

        // ---- The Clerk: a keyboard and monitor, file boxes, paper everywhere
        {
            DressRoomCtx r = NewDressCtx(hub, Def("OfficeA"));
            Placed desk = Existing(r, "TableOffice (4)");
            On(r, desk, "Keyboard", 0f, 0.1f, 90f);
            On(r, desk, "PC_Monitor", 0.1f, -0.35f, 90f);
            On(r, desk, "Paper", 0.4f, 0.4f, 10f);
            On(r, desk, "m:notebook", -0.4f, 0.4f, 50f);
            Scatter(r, 3, true, 2.0f, "Box_V1", "Box_V2");
            Put(r, "m:trash_can", new Vector3(90.4f, r.def.floorY, -63.0f), 0f);
            Litter(r, 3, "Paper");
            total += FinishDressCtx(r);
        }

        // ---- the dining hall: a few plates and cups left on the tables
        {
            DressRoomCtx r = NewDressCtx(hub, Def("DiningRoom"));
            Litter(r, 3, "Plate", "Mug", "Plate_Broken");
            total += FinishDressCtx(r);
        }

        report.AppendLine($"F98 props: {total} placed");
    }
}
