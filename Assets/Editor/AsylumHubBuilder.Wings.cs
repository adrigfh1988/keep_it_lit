using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase B: the five wing doors. Each is a NEW pack door (Door_V3 single, DoorD_V1 double) set flush against a free
/// wall inside the room its maze belongs to, with a plaque, a beacon light, an arrival point 1.5 m in front and a hinge on
/// the leaf (made with ShopRoom.CreateDoorHinge, never static). The leaf is purely the walk-in visual: the wall is behind it.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string PackPrefabs = "Assets/Abandoned_Asylum/Prefabs/";
    private const string MaterialFolder = "Assets/SourceFiles/Materials/Asylum";

    private struct WingDef
    {
        public int Floor;
        public string Name, RoomLabel, Level, Room, DoorPrefab;
        public bool Double;
    }

    private static readonly WingDef[] Wings =
    {
        new WingDef { Floor = 1, Name = "WARD A", RoomLabel = "DAY ROOM", Level = "GROUND FLOOR", Room = "DayRoom", DoorPrefab = "Door_V3" },
        new WingDef { Floor = 2, Name = "BOILER ROOM", RoomLabel = "MACHINE ROOM", Level = "BASEMENT", Room = "MachineRoom", DoorPrefab = "Door_V3" },
        new WingDef { Floor = 3, Name = "MORGUE", RoomLabel = "COLD STORAGE", Level = "BASEMENT", Room = "Morgue", DoorPrefab = "DoorD_V1", Double = true },
        new WingDef { Floor = 4, Name = "SURGERY", RoomLabel = "OPERATING THEATRE", Level = "UPPER FLOOR", Room = "Surgery", DoorPrefab = "Door_V3" },
        new WingDef { Floor = 5, Name = "DIRECTOR", RoomLabel = "DIRECTOR'S OFFICE", Level = "UPPER FLOOR", Room = "Office", DoorPrefab = "DoorD_V1", Double = true },
    };

    private struct WallSpot
    {
        public Vector3 Point;     // on the wall face, floor height
        public Vector3 Normal;    // into the room
        public float Score;
        public List<Collider> Props;
    }

    private static void BuildWings(AsylumHub hub)
    {
        EnsureFolderPath(MaterialFolder);
        Material plate = LoadOrCreateMaterial("Asylum_Plate", new Color(0.09f, 0.075f, 0.06f), 0.25f);
        List<WingDoor> doors = new List<WingDoor>();

        foreach (WingDef def in Wings)
        {
            Bounds room = RoomBounds(_building, def.Room, 0f);
            if (room.size == Vector3.zero) { Debug.LogError($"Build Asylum Hub: room {def.Room} not found in the pack building."); doors.Add(null); continue; }

            float floorY = FloorHeight(def.Room, room);
            List<Vector3> entrances = RoomEntrances(def.Room);
            if (!FindWallSpot(room, floorY, entrances, out WallSpot spot))
            {
                Debug.LogError($"Build Asylum Hub: no free wall for the {def.Name} door in {def.Room}.");
                doors.Add(null);
                continue;
            }

            foreach (Collider prop in spot.Props)
            {
                Transform victim = PropRoot(prop.transform, def.Room);
                Debug.Log($"Asylum {def.Name} door: removed {victim.name} from the clearance box.", victim);
                Object.DestroyImmediate(victim.gameObject);
            }

            RemoveHangingProps(def, spot);

            doors.Add(BuildWingDoor(hub, def, spot, floorY, plate));
        }

        hub.wingDoors = doors.ToArray();
    }

    private static readonly string[] KeepNames = { "Window", "Wall", "Frame", "Door", "Pillar", "facing", "floor", "roof", "Stair", "Ceiling" };

    /// <summary>
    /// The collider test misses collider-less dressing (curtains, rails, posters): anything that is not architecture and whose
    /// renderer reaches into the door, its plaque or the space in front of it goes too. Windows stay - they are part of the wall.
    /// </summary>
    private static void RemoveHangingProps(WingDef def, WallSpot spot)
    {
        foreach (Transform victim in HangingProps(def.Room, spot))
        {
            if (victim == null) continue; // a parent prop went already
            Debug.Log($"Asylum {def.Name} door: removed hanging prop {victim.name}.");
            Object.DestroyImmediate(victim.gameObject);
        }
    }

    /// <summary>The collider-less dressing a door at `spot` in `roomName` would hang inside (what <see cref="RemoveHangingProps"/> deletes). Nothing is removed here.</summary>
    private static List<Transform> HangingProps(string roomName, WallSpot spot)
    {
        List<Transform> found = new List<Transform>();
        Transform room = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == roomName);
        if (room == null) return found;
        Vector3 across = Mathf.Abs(spot.Normal.z) > 0.5f ? Vector3.right : Vector3.forward;
        Vector3 centre = spot.Point + spot.Normal * 0.45f + Vector3.up * 1.5f;
        Vector3 size = across * 2.5f + spot.Normal * 1.0f + Vector3.up * 3.1f;
        Bounds clear = new Bounds(centre, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
        foreach (Renderer r in room.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || !r.bounds.Intersects(clear)) continue;
            Vector3 s = r.bounds.size;
            if (Mathf.Max(s.x, Mathf.Max(s.y, s.z)) > 3f) continue;
            string chain = "";
            for (Transform t = r.transform; t != null && t != room; t = t.parent) chain += t.name + "/";
            if (KeepNames.Any(k => chain.Contains(k))) continue;
            Transform victim = r.transform;
            while (victim.parent != null && victim.parent != room) victim = victim.parent;
            if (victim == room || found.Contains(victim)) continue;
            found.Add(victim);
        }
        return found;
    }

    /// <summary>What <see cref="BuildDoorShell"/> builds: the root plus the parts a door component needs wired.</summary>
    private struct DoorShell
    {
        public GameObject Root;
        public TextMeshPro Text;
        public Light Beacon;
        public Transform HingeA, HingeB, Arrival;
    }

    private static WingDoor BuildWingDoor(AsylumHub hub, WingDef def, WallSpot spot, float floorY, Material plate)
    {
        DoorShell shell = BuildDoorShell(hub, $"Wing_{def.Floor}_{def.Name.Replace(' ', '_')}", def.DoorPrefab, def.Double,
            $"{def.Name}\n<size=60%>{def.RoomLabel}</size>", spot, floorY, plate);

        WingDoor wing = shell.Root.AddComponent<WingDoor>();
        wing.floor = def.Floor;
        wing.wingName = def.Name;
        wing.roomName = def.RoomLabel;
        wing.levelName = def.Level;
        wing.plaque = shell.Text;
        wing.beacon = shell.Beacon;
        wing.leafHinge = shell.HingeA;
        wing.leafHingeB = shell.HingeB;
        wing.arrivalPoint = shell.Arrival;
        wing.Refresh();
        shell.Beacon.enabled = true; // the saved prefab keeps every beacon on (Refresh switches a sealed wing off); the audit counts them
        return wing;
    }

    /// <summary>
    /// The shared body of every hub door (wing doors, the SHOP door): the pack door with a hinge per leaf, a plaque plate and text, a
    /// beacon, an arrival point 1.5 m in front and the interaction trigger, flush against `spot`. The caller adds its component.
    /// </summary>
    private static DoorShell BuildDoorShell(AsylumHub hub, string rootName, string doorPrefab, bool isDouble, string plaqueText,
        WallSpot spot, float floorY, Material plate)
    {
        GameObject root = new GameObject(rootName);
        root.transform.SetParent(hub.wings, false);
        root.transform.position = new Vector3(spot.Point.x, floorY, spot.Point.z);
        root.transform.rotation = Quaternion.LookRotation(spot.Normal, Vector3.up);

        // The pack door: frame + leaf(s), pivot at floor level in the middle, thin along its local Z.
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PackPrefabs + doorPrefab + ".prefab");
        GameObject door = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(door, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        door.name = "Door";
        door.transform.SetParent(root.transform, false);
        door.transform.localPosition = new Vector3(0f, 0f, 0.1f);
        door.transform.localRotation = Quaternion.identity;
        door.transform.localScale = Vector3.one;

        Transform leafA, leafB = null;
        if (isDouble)
        {
            leafA = door.GetComponentsInChildren<Transform>(true).First(t => t.name.EndsWith("_Left") || t.name == "DoorD_Left");
            leafB = door.GetComponentsInChildren<Transform>(true).First(t => t.name.EndsWith("_Right") || t.name == "DoorD_Right");
        }
        else
        {
            leafA = door.GetComponentsInChildren<Transform>(true).First(t => t.name == "Door" && t != door.transform);
        }

        Transform hingeA = ShopRoom.CreateDoorHinge(door.transform, leafA);
        hingeA.name = "LeafHinge";
        Transform hingeB = null;
        if (leafB != null)
        {
            hingeB = HingeAtMaxEdge(door.transform, leafB);
            hingeB.name = "LeafHingeB";
        }

        // Plaque: a dark plate above the door with the state-coloured text (WingDoor.Refresh writes the text).
        GameObject plateGo = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plateGo.name = "PlaqueBack";
        plateGo.transform.SetParent(root.transform, false);
        plateGo.transform.localPosition = new Vector3(0f, 2.62f, 0.03f);
        plateGo.transform.localScale = new Vector3(2.2f, 0.62f, 0.05f);
        Object.DestroyImmediate(plateGo.GetComponent<Collider>());
        plateGo.GetComponent<Renderer>().sharedMaterial = plate;

        GameObject textGo = new GameObject("Plaque");
        textGo.transform.SetParent(root.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 2.62f, 0.062f);
        textGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // TextMeshPro reads from -Z
        TextMeshPro text = textGo.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) text.font = TMP_Settings.defaultFontAsset;
        text.fontSize = 1.55f;
        text.characterSpacing = 3f;
        text.alignment = TextAlignmentOptions.Center;
        text.rectTransform.sizeDelta = new Vector2(2.1f, 0.6f);
        text.text = plaqueText;

        // Beacon above the door, a little into the room.
        GameObject lightGo = new GameObject("Beacon");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 2.2f, 0.9f);
        Light beacon = lightGo.AddComponent<Light>();
        beacon.type = LightType.Point;
        beacon.color = Sodium;
        beacon.range = 5f;
        beacon.intensity = 1.4f;
        beacon.shadows = LightShadows.None;
        beacon.lightmapBakeType = LightmapBakeType.Realtime;

        // Arrival: 1.5 m in front of the door, facing away from it.
        GameObject arrival = new GameObject("Arrival");
        arrival.transform.SetParent(root.transform, false);
        arrival.transform.localPosition = new Vector3(0f, 0.05f, 1.5f);
        arrival.transform.localRotation = Quaternion.identity;

        // Interaction: a trigger box over the door (PlayerInteractor spherecasts for IInteractable colliders in Collide mode).
        BoxCollider trigger = root.AddComponent<BoxCollider>();
        trigger.isTrigger = true;
        trigger.center = new Vector3(0f, 1.1f, 0.35f);
        trigger.size = new Vector3(1.9f, 2.3f, 0.9f);

        MakeMovable(root.transform);
        return new DoorShell { Root = root, Text = text, Beacon = beacon, HingeA = hingeA, HingeB = hingeB, Arrival = arrival.transform };
    }

    // ---------------------------------------------------------------- wall search

    private static int _dbgNotFlat, _dbgBlocked;
    public static string LastDiagnostics;

    private static bool FindWallSpot(Bounds room, float floorY, List<Vector3> entrances, out WallSpot best, System.Func<WallSpot, bool> accept = null)
    {
        best = default;
        bool found = false;
        _dbgNotFlat = _dbgBlocked = 0;
        if (room.size.x > 0f && LastDiagnostics != null && LastDiagnostics.Length > 600) LastDiagnostics = "";
        Vector3[] normals = { Vector3.forward, Vector3.back, Vector3.right, Vector3.left };
        Physics.SyncTransforms();

        foreach (Vector3 n in normals)
        {
            bool tangentIsX = Mathf.Abs(n.z) > 0.5f;
            Vector3 tangent = tangentIsX ? Vector3.right : Vector3.forward;
            float half = tangentIsX ? room.extents.x : room.extents.z;
            float depthHalf = tangentIsX ? room.extents.z : room.extents.x;

            for (float s = -half + 1.1f; s <= half - 1.1f; s += 0.25f)
            {
                Vector3 probe = new Vector3(room.center.x, floorY, room.center.z) + tangent * s;
                if (!FlatWall(probe, n, tangent, floorY, depthHalf + 1.0f, out Vector3 wallPoint)) { _dbgNotFlat++; continue; }

                // Clearance in front: 1.5 x 2.5 x 1.5 m. Props in it are removed; architecture in it rejects the spot.
                Vector3 centre = wallPoint + n * 0.95f;
                centre.y = floorY + 1.4f;
                Vector3 halfExtents = tangentIsX ? new Vector3(0.75f, 1.15f, 0.75f) : new Vector3(0.75f, 1.15f, 0.75f);
                Collider[] hits = Physics.OverlapBox(centre, halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
                List<Collider> props = new List<Collider>();
                bool blocked = false;
                foreach (Collider c in hits)
                {
                    if (IsArchitecture(c)) { blocked = true; break; }
                    props.Add(c);
                }
                if (blocked) { _dbgBlocked++; continue; }

                float nearestEntrance = entrances.Count == 0 ? 8f : entrances.Min(e => Vector3.Distance(new Vector3(e.x, 0f, e.z), new Vector3(wallPoint.x, 0f, wallPoint.z)));
                float score = Mathf.Min(nearestEntrance, 7f) - 0.12f * Mathf.Abs(s) - 1.2f * props.Select(p => PropRoot(p.transform, "")).Distinct().Count();
                if (found && score <= best.Score) continue;

                WallSpot candidate = new WallSpot { Point = new Vector3(wallPoint.x, floorY, wallPoint.z), Normal = n, Score = score, Props = props };
                if (accept != null && !accept(candidate)) continue;

                found = true;
                best = candidate;
            }
        }
        if (!found) LastDiagnostics = ($"Asylum wall search {room.center} size {room.size} floorY {floorY}: not flat {_dbgNotFlat}, blocked {_dbgBlocked}");
        return found;
    }

    /// <summary>
    /// First finds the wall along the centre line (the first architecture hit facing into the room), then a 3 x 3 grid of
    /// short rays from just in front of it: all must hit architecture facing into the room within 0.14 m of one another
    /// (wainscot sits proud of the plaster). Returns the most inward plane, so the door frame rests against the wainscot.
    /// </summary>
    private static bool FlatWall(Vector3 probe, Vector3 n, Vector3 tangent, float floorY, float maxDistance, out Vector3 point)
    {
        point = default;
        Vector3 start = probe;
        start.y = floorY + 1.2f;
        RaycastHit[] all = Physics.RaycastAll(start, -n, maxDistance, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(all, (a, b) => a.distance.CompareTo(b.distance));
        RaycastHit? wall = null;
        foreach (RaycastHit h in all)
        {
            if (Vector3.Dot(h.normal, n) > 0.9f && IsArchitecture(h.collider)) { wall = h; break; }
        }
        if (wall == null) return false;

        Vector3 baseOrigin = wall.Value.point + n * 0.5f;
        float bestPlane = float.NegativeInfinity, worstPlane = float.PositiveInfinity;
        Vector3 bestPoint = default;
        foreach (float offset in new[] { -0.7f, 0f, 0.7f })
        {
            foreach (float height in new[] { 0.3f, 1.2f, 2.2f })
            {
                Vector3 origin = baseOrigin + tangent * offset;
                origin.y = floorY + height;
                if (!Physics.Raycast(origin, -n, out RaycastHit hit, 1.0f, ~0, QueryTriggerInteraction.Ignore)) return false;
                if (Vector3.Dot(hit.normal, n) < 0.95f || !IsArchitecture(hit.collider)) return false;
                float p = Vector3.Dot(hit.point, n);
                if (p > bestPlane) { bestPlane = p; bestPoint = hit.point; }
                worstPlane = Mathf.Min(worstPlane, p);
            }
        }
        if (bestPlane - worstPlane > 0.14f) return false;

        // The door sits at the sampled position along the wall, on the most inward plane.
        Vector3 along = Vector3.Project(wall.Value.point - bestPoint, n);
        point = new Vector3(wall.Value.point.x, floorY, wall.Value.point.z) - along;
        return true;
    }
    /// <summary>Shell, floors, stairs, frames, pillars and anything big: never removed, never built through.</summary>
    private static bool IsArchitecture(Collider c)
    {
        Vector3 size = c.bounds.size;
        if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) > 2.5f) return true;
        for (Transform t = c.transform; t != null && t != _building; t = t.parent)
        {
            if (ArchitectureTokens.Any(token => t.name.Contains(token)) || t.name.StartsWith("Door") || t.name.EndsWith("_facing")) return true;
        }
        return false;
    }

    /// <summary>The object to delete for a prop collider: the child of the room group that contains it.</summary>
    private static Transform PropRoot(Transform t, string room)
    {
        Transform r = t;
        while (r.parent != null && r.parent.name != room && r.parent != _building) r = r.parent;
        return r;
    }


    private static List<Vector3> RoomEntrances(string room)
    {
        Transform t = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(x => x.name == room);
        List<Vector3> list = new List<Vector3>();
        if (t == null) return list;
        foreach (Transform d in t.GetComponentsInChildren<Transform>(true))
        {
            if (!d.name.StartsWith("Door") || d.name.EndsWith("Frame")) continue;
            Renderer r = d.GetComponent<Renderer>();
            if (r != null) list.Add(r.bounds.center);
        }
        return list;
    }

    private static float FloorHeight(string room, Bounds b)
    {
        Transform floor = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == room + "_floor");
        Renderer r = floor != null ? floor.GetComponent<Renderer>() : null;
        if (r != null) return r.bounds.max.y;
        FloorAt(b.center, b.min.y + 3f, out float y);
        return y;
    }

    // ---------------------------------------------------------------- hinges

    private static Transform HingeAtMaxEdge(Transform room, Transform leaf)
    {
        Renderer[] renderers = leaf.GetComponentsInChildren<Renderer>(true);
        Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
        foreach (Renderer r in renderers)
        {
            Bounds b = r.bounds;
            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3((i & 1) == 0 ? b.min.x : b.max.x, (i & 2) == 0 ? b.min.y : b.max.y, (i & 4) == 0 ? b.min.z : b.max.z);
                Vector3 local = room.InverseTransformPoint(corner);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
        }
        GameObject hinge = new GameObject("DoorHinge");
        hinge.transform.SetParent(leaf.parent, false);
        hinge.transform.position = room.TransformPoint(new Vector3(max.x, 0.5f * (min.y + max.y), 0.5f * (min.z + max.z)));
        hinge.transform.rotation = room.rotation;
        leaf.SetParent(hinge.transform, true);
        return hinge.transform;
    }

    /// <summary>Clears every static flag under `root`: static batching would merge a leaf into a combined mesh at scene load and the hinge would turn nothing.</summary>
    private static void MakeMovable(Transform root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
    }

    private static Material LoadOrCreateMaterial(string name, Color color, float smoothness)
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (m != null) return m;
        m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
        m.SetColor("_BaseColor", color);
        m.SetFloat("_Smoothness", smoothness);
        m.SetFloat("_Metallic", 0f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }
}
