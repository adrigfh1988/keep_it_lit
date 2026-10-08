using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F92 phase D: the dressing layer. Props from the Karlheinz Asylum pack's loose prefabs, the Morgue Room PBR pack, the
/// DeadBody LITE bodies and the project's blood-decal prefabs are composed into little stories (a gurney abandoned in a
/// hall with a covered body on it, an overturned chair beside a wheelchair that faces the wall, an autopsy table with its
/// trolley and a pool under it) under AsylumHub/Dressing/&lt;Room&gt;/.
///
/// Every prop is placed by renderer bounds (pack pivots are unreliable), seated on the floor found by a raycast, and rejected
/// when its oriented box overlaps any collider or the corridor kept free along the walk routes. The result is saved into the
/// hub prefab like everything else - from then on it is hand-editable; <see cref="DressSilently"/> is only the first draft
/// (it clears Dressing/&lt;Room&gt; groups and redoes them).
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string AsylumDir = "Assets/Abandoned_Asylum/Prefabs/";
    private const string MorgueDir = "Assets/Morgue Room PBR/Prefabs/props/";
    private const string CommonDir = "Assets/SourceFiles/Themes/Common/Prefabs/";
    private const string SheetMaterialPath = "Assets/SourceFiles/Materials/Asylum/Asylum_Sheet.mat";

    public static string DressLog = "";

    // Native front fix-ups (degrees to add so a prop's front looks along +Z at yaw 0). Filled in by eye.
    private static readonly Dictionary<string, float> FrontYaw = new Dictionary<string, float>
    {
    };

    private sealed class RoomDef
    {
        public string name; public float floorY; public float minX, maxX, minZ, maxZ; public bool skip;
        public RoomDef(string n, float y, float x0, float x1, float z0, float z1, bool skip = false) { name = n; floorY = y; minX = x0; maxX = x1; minZ = z0; maxZ = z1; this.skip = skip; }
        public bool Contains(float x, float z, float y) => x >= minX && x <= maxX && z >= minZ && z <= maxZ && Mathf.Abs(y - floorY) < 0.2f;
    }

    // Most specific first; halls last. `skip` rooms are listed so a hall's rectangle does not dress them.
    private static readonly RoomDef[] DressRooms =
    {
        new RoomDef("Morgue", 1.15f, 124.8f, 131.3f, -57.2f, -46.6f),
        new RoomDef("MachineRoom", 0.07f, 106.4f, 114.2f, -56f, -45.2f),
        new RoomDef("Surgery", 8.82f, 93.3f, 102.4f, -63.8f, -56.9f),
        new RoomDef("Office", 8.82f, 103.3f, 111.6f, -63.8f, -56.0f),
        new RoomDef("DayRoom", 5.0f, 114.8f, 129.3f, -81.8f, -63.9f),
        new RoomDef("OfficeA", 5.0f, 83.9f, 92.9f, -63.8f, -56.9f),
        new RoomDef("Bedroom", 8.82f, 71.8f, 86.6f, -82f, -64f),
        new RoomDef("Gatehouse", 5f, 93.2f, 97.5f, -64f, -56.7f, true),
        new RoomDef("GuardRoom", 5f, 102.6f, 106.9f, -64f, -55.8f, true),
        new RoomDef("VisitRoom", 5f, 106.8f, 115.6f, -64f, -56.7f, true),
        new RoomDef("Stock", 5f, 120.4f, 132.3f, -52.1f, -43.8f, true),
        new RoomDef("KitchenStock", 5f, 70.2f, 74.8f, -63.9f, -51.1f, true),
        new RoomDef("DiningRoom", 5f, 70.8f, 85.4f, -82.2f, -63.6f, true),
        new RoomDef("KitchenA", 5f, 71f, 85f, -51.7f, -43.8f, true),
        new RoomDef("Classroom", 8.82f, 120.4f, 132.3f, -52.1f, -43.8f, true),
        new RoomDef("Bathroom", 8.82f, 70f, 84f, -51.2f, -43.8f, true),
        new RoomDef("ExamRoom", 8.82f, 83.7f, 93.1f, -64f, -56.3f, true),
        new RoomDef("Entrance", 5.0f, 92f, 108f, -62f, -52f),
        new RoomDef("HallUnderground", 1.15f, 113.3f, 124.6f, -71.3f, -50.8f),
        new RoomDef("HallDownstair", 5.0f, 74.9f, 124.4f, -67f, -35f),
        new RoomDef("HallUpstair", 8.82f, 74.9f, 124.4f, -64f, -35.2f),
    };

    private sealed class Cell { public Vector3 pos; public bool wall; public Vector3 n; public float dist; }

    private sealed class Frame
    {
        public Vector3 pos; public float yaw;
        public Quaternion Rot => Quaternion.Euler(0f, yaw, 0f);
        public Vector3 W(float lx, float lz) => pos + Rot * new Vector3(lx, 0f, lz);
    }

    private sealed class Placed
    {
        public GameObject go; public string key; public Vector3 center; public Vector3 size; public float yaw;
        public float TopY => center.y + size.y * 0.5f;
        public float LongAxisYaw => yaw + (size.x > size.z ? 90f : 0f);
    }

    private sealed class DressRoomCtx
    {
        public RoomDef def; public Transform group; public System.Random rng; public List<Cell> cells = new List<Cell>();
        public int placed, rejected; public List<string> rejects = new List<string>(); public Dictionary<string, int> perKey = new Dictionary<string, int>();
        public List<Placed> all = new List<Placed>();
        public List<Vector3> decalSpots = new List<Vector3>();
    }

    private static List<Vector3> _keepOut = new List<Vector3>();
    private static Material _sheet;
    private static int _decalCounter;

    // ---------------------------------------------------------------- entry

    /// <summary>Clears and redoes Dressing/&lt;Room&gt; for the scene hub, then applies the result to the prefab.</summary>
    public static string DressSilently()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return "no hub";
        string result = Dress(hub);
        if (PrefabUtility.IsPartOfPrefabInstance(hub.gameObject))
        {
            PrefabUtility.ApplyPrefabInstance(hub.gameObject, InteractionMode.AutomatedAction);
        }
        return result;
    }

    /// <summary>Dev: the draft without applying it to the prefab.</summary>
    public static string DressNoApply()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        return hub == null ? "no hub" : Dress(hub);
    }

    private static string Dress(AsylumHub hub)
    {
        _building = hub.building;
        System.Text.StringBuilder log = new System.Text.StringBuilder();
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);

        // 1. clear the old room groups
        for (int i = hub.dressing.childCount - 1; i >= 0; i--)
        {
            Transform c = hub.dressing.GetChild(i);
            if (c.name == "Audio" || c.name == "Particles") continue;
            Object.DestroyImmediate(c.gameObject);
        }
        Physics.SyncTransforms();

        // 2. the corridor the walk routes need
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
        log.AppendLine($"keep-out points: {_keepOut.Count}");

        EnsureFolderPath("Assets/SourceFiles/Materials/Asylum");
        _sheet = AssetDatabase.LoadAssetAtPath<Material>(SheetMaterialPath);
        if (_sheet == null)
        {
            _sheet = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            _sheet.name = "Asylum_Sheet";
            _sheet.SetColor("_BaseColor", new Color(0.72f, 0.73f, 0.72f));
            _sheet.SetFloat("_Smoothness", 0.12f);
            AssetDatabase.CreateAsset(_sheet, SheetMaterialPath);
        }
        _sheet.SetColor("_BaseColor", new Color(0.8f, 0.82f, 0.82f));
        EditorUtility.SetDirty(_sheet);

        _decalCounter = 0;
        int total = 0, rejected = 0;
        foreach (RoomDef def in DressRooms)
        {
            if (def.skip) continue;
            GameObject groupGo = new GameObject(def.name);
            groupGo.transform.SetParent(hub.dressing, false);
            DressRoomCtx r = new DressRoomCtx { def = def, group = groupGo.transform, rng = new System.Random(StableHash(def.name)) };
            ScanCells(r);
            DressRoomStory(r);
            total += r.placed; rejected += r.rejected;
            log.AppendLine($"{def.name}: {r.placed} placed, {r.rejected} rejected, {r.cells.Count} free cells ({r.cells.Count(c => c.wall)} at a wall)");
            foreach (string s in r.rejects.Take(12)) log.AppendLine("    reject " + s);
            if (r.placed == 0) Object.DestroyImmediate(groupGo);
        }

        // flags like the building: never batching (the content starts inactive)
        const StaticEditorFlags flags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
        foreach (Transform t in hub.dressing.GetComponentsInChildren<Transform>(true))
        {
            if (t == hub.dressing || t.name == "Audio" || t.name == "Particles" || t.GetComponentInParent<AudioSource>() != null) continue;
            if (t.IsChildOf(hub.dressing) && t.parent != null && (t.parent.name == "Audio" || t.parent.name == "Particles")) continue;
            bool visual = t.GetComponent<Renderer>() != null || t.GetComponent<Collider>() != null;
            if (t.GetComponent<ParticleSystem>() != null) continue;
            GameObjectUtility.SetStaticEditorFlags(t.gameObject, visual ? flags : 0);
        }

        Physics.SyncTransforms();
        log.AppendLine($"TOTAL props placed {total} (rejected {rejected})");
        hub.SetContentActive(wasActive || true);
        DressLog = log.ToString();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        return DressLog;
    }

    private static int StableHash(string s) { int h = 17; foreach (char c in s) h = h * 31 + c; return h; }

    // ---------------------------------------------------------------- floor cells

    private static void ScanCells(DressRoomCtx r)
    {
        RoomDef d = r.def;
        const float step = 0.5f;
        Physics.SyncTransforms();
        for (float x = d.minX; x <= d.maxX; x += step)
        {
            for (float z = d.minZ; z <= d.maxZ; z += step)
            {
                // a more specific room that comes earlier in the table owns the cell
                bool owned = true;
                foreach (RoomDef other in DressRooms)
                {
                    if (other == d) break;
                    if (other.Contains(x, z, d.floorY)) { owned = false; break; }
                }
                if (!owned) continue;

                if (!Physics.Raycast(new Vector3(x, d.floorY + 0.6f, z), Vector3.down, out RaycastHit hit, 1.0f, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (Mathf.Abs(hit.point.y - d.floorY) > 0.15f || hit.normal.y < 0.9f) continue;
                Vector3 p = new Vector3(x, hit.point.y, z);
                if (Physics.CheckBox(p + Vector3.up * 1.05f, new Vector3(0.24f, 0.8f, 0.24f), Quaternion.identity, ~0, QueryTriggerInteraction.Ignore)) continue;

                Cell c = new Cell { pos = p, dist = 99f };
                Vector3[] dirs = { Vector3.right, Vector3.left, Vector3.forward, Vector3.back };
                foreach (Vector3 dir in dirs)
                {
                    if (!Physics.Raycast(p + Vector3.up * 1.0f, dir, out RaycastHit wh, 1.0f, ~0, QueryTriggerInteraction.Ignore)) continue;
                    Vector3 size = wh.collider.bounds.size;
                    float longest = Mathf.Max(size.x, Mathf.Max(size.y, size.z));
                    if (longest < 2.0f || wh.distance >= c.dist) continue;
                    c.dist = wh.distance; c.n = -dir; c.wall = true;
                }
                r.cells.Add(c);
            }
        }
    }

    // ---------------------------------------------------------------- placement

    private static GameObject Spawn(string key, Transform parent)
    {
        GameObject asset = AssetDatabase.LoadAssetAtPath<GameObject>(PropPath(key));
        if (asset == null) { DressLog += "missing prefab " + key + "\n"; return null; }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(asset);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.SetParent(parent, true);
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
        go.name = System.IO.Path.GetFileNameWithoutExtension(PropPath(key));
        return go;
    }

    private static string PropPath(string key)
    {
        if (key.StartsWith("m:")) return MorgueDir + key.Substring(2) + ".prefab";
        if (key.StartsWith("pack:")) return CommonDir + "PackDressing/" + key.Substring(5) + ".prefab";
        if (key.StartsWith("blood:")) return CommonDir + "Kit_" + key.Substring(6) + ".prefab";
        if (key.StartsWith("body:")) return "Assets/DeadBody LITE/Prefabs/Body_Only/DeadBody (" + key.Substring(5) + ").prefab";
        if (key == "d:drain") return "Assets/Morgue Room PBR/Prefabs/decals/drain_decal.prefab";
        return AsylumDir + key + ".prefab";
    }

    private static Bounds RendBounds(GameObject go)
    {
        Renderer[] rs = go.GetComponentsInChildren<Renderer>(true).Where(x => !(x is ParticleSystemRenderer)).ToArray();
        if (rs.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
        Bounds b = rs[0].bounds;
        foreach (Renderer rr in rs) b.Encapsulate(rr.bounds);
        return b;
    }

    private static bool KeepOutHit(Vector3 center, Vector3 ext, float yaw, float floorY, float margin)
    {
        Quaternion inv = Quaternion.Euler(0f, -yaw, 0f);
        foreach (Vector3 k in _keepOut)
        {
            if (Mathf.Abs(k.y - floorY) > 2.5f) continue;
            Vector3 l = inv * (k - center);
            if (Mathf.Abs(l.x) < ext.x + margin && Mathf.Abs(l.z) < ext.z + margin) return true;
        }
        return false;
    }

    /// <summary>
    /// Places a prop. `wallN` (outward wall normal) makes it back onto the wall; `scale` multiplies the prop; `collide` adds one
    /// fitted BoxCollider (big or tall props only). Returns null (and destroys the prop) when it does not fit.
    /// </summary>
    private static Placed Put(DressRoomCtx r, string key, Vector3 xz, float yaw, float scale = 1f, Vector3? wallN = null, bool check = true, bool forceCollider = false, float lift = 0f, float gap = 0.03f)
    {
        GameObject go = Spawn(key, r.group);
        if (go == null) return null;
        float front = FrontYaw.TryGetValue(key, out float f) ? f : 0f;
        Quaternion assetRot = AssetDatabase.LoadAssetAtPath<GameObject>(PropPath(key)).transform.rotation;
        go.transform.rotation = Quaternion.Euler(0f, front, 0f) * assetRot;
        if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;

        Bounds b = RendBounds(go);
        go.transform.position += new Vector3(xz.x - b.center.x, r.def.floorY + lift - b.min.y, xz.z - b.center.z);
        b = RendBounds(go);
        Vector3 size = b.size;
        Vector3 center = b.center;
        if (Mathf.Abs(yaw) > 0.01f)
        {
            go.transform.RotateAround(center, Vector3.up, yaw);
        }
        Quaternion rot = Quaternion.Euler(0f, yaw, 0f);

        if (wallN.HasValue)
        {
            Vector3 n = wallN.Value;
            float hd = Mathf.Abs(Vector3.Dot(rot * Vector3.right, n)) * size.x * 0.5f + Mathf.Abs(Vector3.Dot(rot * Vector3.forward, n)) * size.z * 0.5f;
            Vector3 origin = new Vector3(center.x, r.def.floorY + Mathf.Clamp(size.y * 0.5f, 0.3f, 1.1f), center.z);
            if (Physics.Raycast(origin, -n, out RaycastHit wh, 2.2f, ~0, QueryTriggerInteraction.Ignore))
            {
                float shift = wh.distance - hd - gap;
                go.transform.position -= n * shift;
                center -= n * shift;
            }
        }

        Vector3 ext = size * 0.5f;
        if (check)
        {
            Vector3 shrink = new Vector3(Mathf.Min(0.05f, ext.x * 0.2f), 0f, Mathf.Min(0.05f, ext.z * 0.2f));
            Vector3 half = ext - shrink; half.y = Mathf.Max(0.02f, ext.y - 0.05f);
            Vector3 boxCenter = center + Vector3.up * 0.04f;
            Collider[] hits = Physics.OverlapBox(boxCenter, half, rot, ~0, QueryTriggerInteraction.Ignore);
            if (hits.Length > 0)
            {
                r.rejected++; r.rejects.Add($"{key} at {xz:F1}: hits {hits[0].name}/{hits[0].transform.parent?.name}");
                Object.DestroyImmediate(go);
                return null;
            }
            if (KeepOutHit(center, ext, yaw, r.def.floorY, 0.85f))
            {
                r.rejected++; r.rejects.Add($"{key} at {xz:F1}: on the walk corridor");
                Object.DestroyImmediate(go);
                return null;
            }
        }

        Placed p = new Placed { go = go, key = key, center = center, size = size, yaw = yaw };
        // size is the oriented box in the prop's yaw frame: rotation was applied about the vertical, bounds were measured before it
        if (forceCollider || ((size.x > 0.45f || size.z > 0.45f) && size.y > 0.25f))
        {
            GameObject col = new GameObject("Collider");
            col.transform.SetParent(go.transform, true);
            col.transform.position = center;
            col.transform.rotation = rot;
            BoxCollider bc = col.AddComponent<BoxCollider>();
            bc.size = size;
            Physics.SyncTransforms();
        }
        r.placed++;
        r.perKey[key] = r.perKey.TryGetValue(key, out int n0) ? n0 + 1 : 1;
        r.all.Add(p);
        return p;
    }

    /// <summary>Puts a small prop on top of a host prop (no collision test, no collider).</summary>
    private static Placed On(DressRoomCtx r, Placed host, string key, float u, float v, float yawRel = 0f, float scale = 1f, float topInset = 0f)
    {
        if (host == null) return null;
        Quaternion hr = Quaternion.Euler(0f, host.yaw, 0f);
        Vector3 xz = host.center + hr * new Vector3(u * host.size.x * 0.5f, 0f, v * host.size.z * 0.5f);
        GameObject go = Spawn(key, r.group);
        if (go == null) return null;
        float front = FrontYaw.TryGetValue(key, out float f) ? f : 0f;
        Quaternion assetRot = AssetDatabase.LoadAssetAtPath<GameObject>(PropPath(key)).transform.rotation;
        go.transform.rotation = Quaternion.Euler(0f, front + host.yaw + yawRel, 0f) * assetRot;
        if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
        Bounds b = RendBounds(go);
        go.transform.position += new Vector3(xz.x - b.center.x, host.TopY - topInset - b.min.y, xz.z - b.center.z);
        Placed p = new Placed { go = go, key = key, center = RendBounds(go).center, size = RendBounds(go).size, yaw = host.yaw + yawRel };
        r.placed++;
        r.perKey[key] = r.perKey.TryGetValue(key, out int n0) ? n0 + 1 : 1;
        return p;
    }

    private static void Sheet(Placed body, float thicken = 1.45f)
    {
        if (body == null) return;
        foreach (Renderer rr in body.go.GetComponentsInChildren<Renderer>(true)) rr.sharedMaterials = Enumerable.Repeat(_sheet, rr.sharedMaterials.Length).ToArray();
        Vector3 s = body.go.transform.localScale;
        body.go.transform.localScale = new Vector3(s.x * 1.12f, s.y * thicken, s.z * 1.02f);
        Vector3 pos = body.go.transform.position;
        Bounds b = RendBounds(body.go);
        body.go.transform.position = pos + new Vector3(0f, 0f, 0f) + Vector3.up * 0f;
        // grown upwards only: keep the bottom where it was
        body.size = b.size;
    }

    /// <summary>A flat blood / drain decal on the floor. `kind`: BloodPool, BloodSmear, BloodSplat.</summary>
    private static void Decal(DressRoomCtx r, string kind, Vector3 xz, float size, float yaw)
    {
        GameObject go = Spawn("blood:" + kind, r.group);
        if (go == null) return;
        go.transform.position = new Vector3(xz.x, r.def.floorY + 0.014f + (_decalCounter % 5) * 0.0006f, xz.z);
        go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        go.transform.localScale = Vector3.one * size;
        DecalVariants dv = go.GetComponentInChildren<DecalVariants>(true);
        if (dv != null) dv.Apply(StableHash(r.def.name) + _decalCounter * 7919);
        _decalCounter++;
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        r.placed++;
        r.perKey["decal:" + kind] = r.perKey.TryGetValue("decal:" + kind, out int n0) ? n0 + 1 : 1;
    }

    // ---------------------------------------------------------------- search helpers

    private static Cell RandomCell(DressRoomCtx r, bool wall, int skipNearWallCount = 0)
    {
        List<Cell> pool = r.cells.Where(c => c.wall == wall).ToList();
        if (pool.Count == 0) return null;
        return pool[r.rng.Next(pool.Count)];
    }

    /// <summary>
    /// Finds a free w (along the frame's X) by d (along its Z) area. wall = true: a frame whose +Z points into the room, its origin
    /// on the wall; wall = false: an open-floor frame whose origin is the area's centre-back.
    /// </summary>
    private static Frame FindArea(DressRoomCtx r, float w, float d, bool wall, int tries = 160)
    {
        int rb = 0, rk = 0, rf = 0;
        for (int i = 0; i < tries; i++)
        {
            Cell c = RandomCell(r, wall);
            if (c == null) return null;
            Frame f = new Frame();
            if (wall)
            {
                f.yaw = Quaternion.LookRotation(c.n).eulerAngles.y;
                f.pos = c.pos - c.n * c.dist;
            }
            else
            {
                f.yaw = 90f * r.rng.Next(4);
                f.pos = c.pos - Quaternion.Euler(0f, f.yaw, 0f) * new Vector3(0f, 0f, d * 0.5f);
            }
            Vector3 center = f.W(0f, d * 0.5f + 0.03f) + Vector3.up * (r.def.floorY + 1.0f - c.pos.y + c.pos.y - r.def.floorY);
            center.y = r.def.floorY + 1.05f;
            if (Physics.CheckBox(center, new Vector3(w * 0.5f - 0.05f, 0.8f, d * 0.5f - 0.05f), f.Rot, ~0, QueryTriggerInteraction.Ignore)) { rb++; continue; }
            if (KeepOutHit(f.W(0f, d * 0.5f), new Vector3(w * 0.5f, 1f, d * 0.5f), f.yaw, r.def.floorY, 0.85f)) { rk++; continue; }
            // the whole rectangle needs floor
            bool floorOk = true;
            for (float x = -w * 0.5f; x <= w * 0.5f && floorOk; x += 0.5f)
            {
                for (float z = 0.1f; z <= d && floorOk; z += 0.5f)
                {
                    Vector3 q = f.W(x, z);
                    floorOk = Physics.Raycast(new Vector3(q.x, r.def.floorY + 0.6f, q.z), Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore);
                }
            }
            if (!floorOk) { rf++; continue; }
            return f;
        }
        r.rejects.Add($"no area {w:F1}x{d:F1} ({(wall ? "wall" : "open")}) blocked {rb} corridor {rk} nofloor {rf}");
        return null;
    }

    /// <summary>Places `key` in a frame: local offset (lx, lz), yaw relative to the frame. The wall option backs it onto the wall.</summary>
    private static Placed InFrame(DressRoomCtx r, Frame f, string key, float lx, float lz, float yawRel, float scale = 1f, bool snapToWall = false, bool check = true, float lift = 0f)
    {
        if (f == null) return null;
        Vector3 w = f.W(lx, lz);
        Vector3? n = snapToWall ? (Vector3?)(f.Rot * Vector3.forward) : null;
        return Put(r, key, w, f.yaw + yawRel, scale, n, check, false, lift);
    }

    private static void DecalIn(DressRoomCtx r, Frame f, string kind, float lx, float lz, float size, float yawRel = 0f)
    {
        if (f == null) return;
        Decal(r, kind, f.W(lx, lz), size, f.yaw + yawRel);
    }

    private static string Pick(DressRoomCtx r, params string[] keys) => keys[r.rng.Next(keys.Length)];
    private static string Body(DressRoomCtx r) => "body:" + (1 + r.rng.Next(6));
    private static float Jit(DressRoomCtx r, float a) => (float)(r.rng.NextDouble() * 2.0 - 1.0) * a;

    /// <summary>Scatter: `count` copies of the keys on random free cells (wall cells or open floor), spaced at least `spacing` apart.</summary>
    private static void Scatter(DressRoomCtx r, int count, bool wall, float spacing, params string[] keys)
    {
        List<Vector3> done = new List<Vector3>();
        int attempts = 0, placed = 0;
        while (placed < count && attempts++ < count * 14)
        {
            Cell c = RandomCell(r, wall);
            if (c == null) return;
            if (done.Any(p => (p - c.pos).sqrMagnitude < spacing * spacing)) continue;
            string key = keys[r.rng.Next(keys.Length)];
            Placed p;
            if (wall)
            {
                float yaw = Quaternion.LookRotation(c.n).eulerAngles.y;
                p = Put(r, key, c.pos, yaw, 1f, c.n);
            }
            else
            {
                p = Put(r, key, c.pos, r.rng.Next(360));
            }
            if (p != null) { placed++; done.Add(c.pos); }
        }
    }

    private static void Litter(DressRoomCtx r, int count, params string[] keys)
    {
        int attempts = 0, placed = 0;
        while (placed < count && attempts++ < count * 6)
        {
            Cell c = RandomCell(r, r.rng.Next(3) == 0);
            if (c == null) return;
            Vector3 p = c.pos + new Vector3(Jit(r, 0.2f), 0f, Jit(r, 0.2f));
            if (KeepOutHit(p, new Vector3(0.1f, 1f, 0.1f), 0f, r.def.floorY, 0.05f) && r.rng.Next(3) != 0) { /* litter on the route is allowed, it does not block (no collider) */ }
            if (Put(r, Pick(r, keys), p, r.rng.Next(360), 1f, null, false) != null) placed++;
        }
    }

    private static void BloodSpots(DressRoomCtx r, int count, float minSize, float maxSize)
    {
        for (int i = 0; i < count; i++)
        {
            Cell c = RandomCell(r, r.rng.Next(3) == 0);
            if (c == null) return;
            string kind = Pick(r, "BloodPool", "BloodSmear", "BloodSplat", "BloodSmear");
            Decal(r, kind, c.pos + new Vector3(Jit(r, 0.2f), 0f, Jit(r, 0.2f)), minSize + (float)r.rng.NextDouble() * (maxSize - minSize), r.rng.Next(360));
        }
    }
}

public static partial class AsylumHubBuilder
{
    /// <summary>An existing pack object in this room as a host for small props (axis-aligned bounds, yaw 0). Null when absent.</summary>
    private static Placed Existing(DressRoomCtx r, string name)
    {
        Transform best = null;
        foreach (Transform t in _building.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != name) continue;
            Bounds b = RendBounds(t.gameObject);
            if (!r.def.Contains(b.center.x, b.center.z, r.def.floorY) && !(b.center.x >= r.def.minX && b.center.x <= r.def.maxX && b.center.z >= r.def.minZ && b.center.z <= r.def.maxZ && Mathf.Abs(b.min.y - r.def.floorY) < 0.6f)) continue;
            best = t; break;
        }
        if (best == null) { r.rejects.Add("no existing " + name); return null; }
        Bounds bb = RendBounds(best.gameObject);
        return new Placed { go = best.gameObject, key = name, center = bb.center, size = bb.size, yaw = 0f };
    }

    /// <summary>A free cell 'min..max' metres from a host, on the floor. Places `key` there facing the host (+Z of the prop towards it).</summary>
    private static Placed Beside(DressRoomCtx r, Placed host, string key, float min, float max, float scale = 1f, bool facing = true, bool collide = true)
    {
        if (host == null) return null;
        List<Cell> pool = r.cells.Where(c => { float d = Vector2.Distance(new Vector2(c.pos.x, c.pos.z), new Vector2(host.center.x, host.center.z)); return d >= min && d <= max; }).OrderBy(c => r.rng.Next()).Take(24).ToList();
        foreach (Cell c in pool)
        {
            Vector3 to = host.center - c.pos; to.y = 0f;
            float yaw = facing ? Quaternion.LookRotation(to).eulerAngles.y : r.rng.Next(360);
            Placed p = Put(r, key, c.pos, yaw, scale);
            if (p != null) return p;
        }
        return null;
    }
}
