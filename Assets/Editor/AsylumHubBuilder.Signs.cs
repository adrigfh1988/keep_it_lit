using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F109 D6: the name plate at the entrance of every named hub room. Lives in this partial because the room rectangles (RoomDef) are private to
/// AsylumHubBuilder; the plate look comes from WorldSignBuilder.Style. Doorways are found from data (the Hub NavMesh), never by hand:
/// walk the room rectangle in 0.25 m steps, keep the places where a point just inside and a point just outside are both on the NavMesh with a
/// clear segment between them, merge neighbours into one doorway, then raycast the wall face outside it (above the opening if there is a lintel,
/// else beside it). Build-missing-only: a room that already has a RoomSign_&lt;Room&gt;_n under Dressing/&lt;Room&gt; is left alone.
/// </summary>
public static partial class AsylumHubBuilder
{
    private sealed class PlateSpec
    {
        public string room, text, key;
        public bool brass, battered, deadLetter;
        public string ghostText, ghostKey, shiftKey;
    }

    private static readonly PlateSpec[] PlateSpecs =
    {
        new PlateSpec { room = "DayRoom", text = "DAY ROOM", key = "world.room.DayRoom", ghostText = "WARD 4", ghostKey = "world.ghost.ward4" },
        new PlateSpec { room = "MachineRoom", text = "MACHINE ROOM", key = "world.room.MachineRoom" },
        new PlateSpec { room = "Morgue", text = "COLD STORAGE", key = "world.room.Morgue", battered = true },
        new PlateSpec { room = "Surgery", text = "OPERATING THEATRE", key = "world.room.Surgery", battered = true },
        new PlateSpec { room = "Office", text = "DIRECTOR'S OFFICE", key = "world.room.Office", brass = true },
        new PlateSpec { room = "OfficeA", text = "RECORDS ROOM", key = "world.room.OfficeA" },
        new PlateSpec { room = "Gatehouse", text = "NURSE STATION", key = "world.room.Gatehouse" },
        new PlateSpec { room = "GuardRoom", text = "GUARD ROOM", key = "world.room.GuardRoom" },
        new PlateSpec { room = "VisitRoom", text = "VISITING ROOM", key = "world.room.VisitRoom" },
        new PlateSpec { room = "Stock", text = "STORE ROOM", key = "world.room.Stock" },
        new PlateSpec { room = "KitchenStock", text = "PANTRY", key = "world.room.KitchenStock" },
        new PlateSpec { room = "DiningRoom", text = "DINING HALL", key = "world.room.DiningRoom" },
        new PlateSpec { room = "KitchenA", text = "KITCHEN", key = "world.room.KitchenA" },
        new PlateSpec { room = "Classroom", text = "CLASSROOM", key = "world.room.Classroom" },
        new PlateSpec { room = "Bathroom", text = "WASHROOM", key = "world.room.Bathroom" },
        new PlateSpec { room = "ExamRoom", text = "EXAM ROOM", key = "world.room.ExamRoom" },
        new PlateSpec { room = "Bedroom", text = "BEDROOM", key = "world.room.Bedroom", shiftKey = "world.room.alt.Bedroom" },
        new PlateSpec { room = "SeclusionRoomA", text = "SECLUSION 1", key = "world.room.SeclusionRoomA", battered = true, deadLetter = true },
        new PlateSpec { room = "SeclusionRoomB", text = "SECLUSION 2", key = "world.room.SeclusionRoomB", battered = true },
    };

    private sealed class Doorway
    {
        public Vector3 center;   // on the NavMesh, outside, in front of the opening (world)
        public Vector3 inward;   // horizontal unit vector pointing into the room
        public float width;
    }

    /// <summary>The room names that get a plate, for ProjectSetup's check.</summary>
    public static string PlatesProblem(AsylumHub hub)
    {
        if (hub == null) return null;
        Transform dressing = hub.transform.Find("Dressing");
        int missing = 0;
        foreach (PlateSpec s in PlateSpecs)
        {
            Transform g = dressing != null ? dressing.Find(s.room) : null;
            bool has = false;
            if (g != null) foreach (Transform c in g) if (c.name.StartsWith("RoomSign_" + s.room + "_")) { has = true; break; }
            if (!has && !_plateSkipLog.Contains(s.room)) missing++;
        }
        return missing > 0 ? missing + " room(s) without a name plate" : null;
    }

    private static readonly HashSet<string> _plateSkipLog = new HashSet<string>();

    /// <summary>Places the missing room plates. Needs the hub content active and the Hub NavMesh loaded. Returns the report.</summary>
    public static string AddRoomPlates(AsylumHub hub)
    {
        var report = new StringBuilder();
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        int agent = HubNavMesh.FindAgentType();
        if (agent == -1) { report.AppendLine("room plates: no Hub NavMesh agent type, nothing placed"); return report.ToString(); }
        Transform dressing = hub.transform.Find("Dressing");
        List<Vector3> plaques = new List<Vector3>();
        foreach (WingDoor w in hub.GetComponentsInChildren<WingDoor>(true)) if (w.plaque != null) plaques.Add(w.plaque.transform.position);
        foreach (ShopDoor s in hub.GetComponentsInChildren<ShopDoor>(true)) if (s.plaque != null) plaques.Add(s.plaque.transform.position);

        int placedTotal = 0;
        foreach (PlateSpec spec in PlateSpecs)
        {
            RoomDef def = PropRoom(spec.room);
            Transform group = dressing.Find(spec.room);
            if (group == null) { GameObject g = new GameObject(spec.room); g.transform.SetParent(dressing, false); group = g.transform; }
            bool has = false;
            foreach (Transform c in group) if (c.name.StartsWith("RoomSign_" + spec.room + "_")) { has = true; break; }
            if (has) { report.AppendLine($"{spec.room}: already has a plate"); continue; }

            List<Doorway> doors = FindDoorways(def, agent);
            List<Doorway> loose = new List<Doorway>(doors);
            doors.RemoveAll(w => !WallFlanked(hub, w, def.floorY));
            if (doors.Count == 0) doors = FrameDoorways(hub, def);
            if (doors.Count == 0) { loose.RemoveAll(w => w.width > 1.5f); if (loose.Count > 0) { doors = new List<Doorway> { loose[0] }; report.AppendLine($"{spec.room}: using an unconfirmed narrow NavMesh doorway (no wall-flanked one, no door frame)"); } }
            int placed = 0;
            var why = new StringBuilder();
            foreach (Doorway d in doors)
            {
                if (placed >= 3) break;
                bool nearPlaque = false;
                foreach (Vector3 p in plaques) if (new Vector2(p.x - d.center.x, p.z - d.center.z).magnitude < 2.2f && Mathf.Abs(p.y - d.center.y) < 3f) nearPlaque = true;
                if (nearPlaque) { why.Append($" [skip: wing plaque within 2 m at ({d.center.x:0.0},{d.center.z:0.0})]"); continue; }
                if (!FindWallFace(hub, d, def.floorY, out Vector3 point, out Vector3 normal, out string how)) { why.Append($" [no wall face at ({d.center.x:0.0},{d.center.z:0.0}): {how}]"); continue; }
                PlacePlate(hub, group, spec, placed, point, normal);
                report.AppendLine($"{spec.room}: plate {placed} {how} at ({point.x:0.0},{point.y:0.0},{point.z:0.0}), doorway width {d.width:0.0}");
                placed++;
            }
            placedTotal += placed;
            if (placed == 0) { report.AppendLine($"{spec.room}: NO plate - {doors.Count} doorway(s) found{why}"); _plateSkipLog.Add(spec.room); }
            else if (why.Length > 0) report.AppendLine($"{spec.room}: also{why}");
        }
        report.Insert(0, $"room plates placed: {placedTotal}\n");
        if (!wasActive) hub.SetContentActive(false);
        EditorUtility.SetDirty(hub);
        return report.ToString();
    }

    private static List<Doorway> FindDoorways(RoomDef d, int agentType)
    {
        var filter = new NavMeshQueryFilter { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
        var result = new List<Doorway>();
        // four sides: (fixed coordinate, inward direction, runs along X?)
        var sides = new (Vector3 start, Vector3 along, float length, Vector3 inward)[]
        {
            (new Vector3(d.minX, d.floorY, d.minZ), Vector3.right, d.maxX - d.minX, Vector3.forward),
            (new Vector3(d.minX, d.floorY, d.maxZ), Vector3.right, d.maxX - d.minX, Vector3.back),
            (new Vector3(d.minX, d.floorY, d.minZ), Vector3.forward, d.maxZ - d.minZ, Vector3.right),
            (new Vector3(d.maxX, d.floorY, d.minZ), Vector3.forward, d.maxZ - d.minZ, Vector3.left),
        };
        float[] inOffsets = { 0.2f, 0.5f, 0.8f, 1.2f };
        float[] outOffsets = { 0.4f, 0.8f, 1.3f, 2.0f };
        foreach (var side in sides)
        {
            var hits = new List<(float t, Vector3 outPoint)>();
            for (float t = 0.25f; t < side.length - 0.2f; t += 0.25f)
            {
                Vector3 edge = side.start + side.along * t;
                bool ok = false; Vector3 outPoint = default;
                foreach (float io in inOffsets)
                {
                    if (!NavMesh.SamplePosition(edge + side.inward * io, out NavMeshHit a, 0.3f, filter) || Mathf.Abs(a.position.y - d.floorY) > 0.6f) continue;
                    foreach (float oo in outOffsets)
                    {
                        if (!NavMesh.SamplePosition(edge - side.inward * oo, out NavMeshHit b, 0.3f, filter) || Mathf.Abs(b.position.y - d.floorY) > 0.6f) continue;
                        if (NavMesh.Raycast(a.position, b.position, out NavMeshHit _, filter)) continue;
                        ok = true; outPoint = b.position; break;
                    }
                    if (ok) break;
                }
                if (ok) hits.Add((t, outPoint));
            }
            // merge consecutive samples into doorways
            int i = 0;
            while (i < hits.Count)
            {
                int j = i;
                while (j + 1 < hits.Count && hits[j + 1].t - hits[j].t <= 0.55f) j++;
                float width = hits[j].t - hits[i].t + 0.25f;
                if (width >= 0.7f && width <= 4.5f)
                {
                    float mid = (hits[i].t + hits[j].t) * 0.5f;
                    Vector3 c = side.start + side.along * mid - side.inward * 1.0f;
                    if (NavMesh.SamplePosition(c, out NavMeshHit onNav, 0.6f, filter)) c = onNav.position;
                    result.Add(new Doorway { center = c, inward = side.inward, width = width });
                }
                i = j + 1;
            }
        }
        return result;
    }

    private static bool WallHit(AsylumHub hub, Vector3 origin, Vector3 inward, out RaycastHit hit)
    {
        RaycastHit[] all = Physics.RaycastAll(origin, inward, 2.6f, ~0, QueryTriggerInteraction.Ignore);
        System.Array.Sort(all, (x, y) => x.distance.CompareTo(y.distance));
        foreach (RaycastHit h in all)
        {
            if (h.collider.transform.IsChildOf(hub.transform) == false) continue;
            if (Vector3.Dot(h.normal, -inward) < 0.8f) continue; // a face looking back out at the corridor
            hit = h;
            return true;
        }
        hit = default;
        return false;
    }

    private static bool FindWallFace(AsylumHub hub, Doorway d, float floorY, out Vector3 point, out Vector3 normal, out string how)
    {
        point = default; normal = default;
        Vector3 inward = d.inward;
        Vector3 lateral = Vector3.Cross(Vector3.up, inward).normalized;
        Vector3 from = d.center - inward * 0.2f;
        // 1. above the opening
        float yAbove = floorY + 2.45f;
        Vector3 o = new Vector3(from.x, yAbove, from.z);
        if (WallHit(hub, o, inward, out RaycastHit h1) && h1.distance < 2.2f)
        {
            bool continuous = true;
            foreach (float dy in new[] { -0.2f, 0.2f })
            {
                if (!WallHit(hub, o + Vector3.up * dy, inward, out RaycastHit hh) || Mathf.Abs(hh.distance - h1.distance) > 0.12f) continuous = false;
            }
            foreach (float dl in new[] { -0.5f, 0.5f })
            {
                if (!WallHit(hub, o + lateral * dl, inward, out RaycastHit hh) || Mathf.Abs(hh.distance - h1.distance) > 0.12f) continuous = false;
            }
            bool ceilingClear = !Physics.Raycast(h1.point + h1.normal * 0.05f, Vector3.up, 0.3f, ~0, QueryTriggerInteraction.Ignore);
            if (continuous && ceilingClear) { point = h1.point; normal = h1.normal; how = "above"; return true; }
        }
        // 2. beside the opening, at 1.6 m, on either jamb
        foreach (float side in new[] { 1f, -1f })
        {
            Vector3 o2 = new Vector3(from.x, floorY + 1.6f, from.z) + lateral * side * (d.width * 0.5f + 0.6f);
            if (!WallHit(hub, o2, inward, out RaycastHit h2) || h2.distance > 2.2f) continue;
            bool continuous = true;
            foreach (float dl in new[] { -0.4f, 0.4f })
                if (!WallHit(hub, o2 + lateral * dl, inward, out RaycastHit hh) || Mathf.Abs(hh.distance - h2.distance) > 0.12f) continuous = false;
            foreach (float dy in new[] { -0.2f, 0.2f })
                if (!WallHit(hub, o2 + Vector3.up * dy, inward, out RaycastHit hh) || Mathf.Abs(hh.distance - h2.distance) > 0.12f) continuous = false;
            if (!continuous) continue;
            point = h2.point; normal = h2.normal; how = "beside"; return true;
        }
        how = "no continuous wall above or beside the opening";
        return false;
    }

    private static void PlacePlate(AsylumHub hub, Transform group, PlateSpec spec, int index, Vector3 point, Vector3 normal)
    {
        GameObject go = new GameObject("RoomSign_" + spec.room + "_" + index);
        go.transform.SetParent(group, true);
        go.transform.position = point + normal * 0.012f;
        go.transform.rotation = Quaternion.LookRotation(-normal, Vector3.up);
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        tmp.text = spec.text;
        tmp.fontSize = 1.0f;
        tmp.characterSpacing = 3f;
        WorldSignBuilder.SetLocalizedKey(go, spec.key);
        var o = new WorldSignBuilder.SignOptions { deadLetter = spec.deadLetter, battered = spec.battered, ghostText = spec.ghostText, ghostKey = spec.ghostKey };
        if (spec.battered) o.tilt = (index % 2 == 0 ? 1f : -1f) * 2.5f;
        else o.noMissingScrew = true;
        if (spec.brass)
        {
            o.darkInset = true;
            WorldSignBuilder.Style(tmp, WorldSignBuilder.SignStyle.BrassPlaque, o);
        }
        else WorldSignBuilder.Style(tmp, WorldSignBuilder.SignStyle.EnamelPlate, o);
        if (!string.IsNullOrEmpty(spec.shiftKey))
        {
            SignShift shift = go.AddComponent<SignShift>();
            SerializedObject so = new SerializedObject(shift);
            so.FindProperty("altKey").stringValue = spec.shiftKey;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}

public static partial class AsylumHubBuilder
{
    /// <summary>
    /// Fallback for a room whose doorways are not on the NavMesh (a closed door leaf blocks it): the pack Door_Frame objects that stand on the room
    /// rectangle's border, with the inward direction taken from the nearest side.
    /// </summary>
    private static List<Doorway> FrameDoorways(AsylumHub hub, RoomDef d)
    {
        var result = new List<Doorway>();
        foreach (Transform t in hub.building.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("Door_Frame")) continue;
            Vector3 p = t.position;
            if (Mathf.Abs(p.y - d.floorY) > 1.6f) continue;
            // distance to each side, inside the band
            float dSouth = Mathf.Abs(p.z - d.minZ), dNorth = Mathf.Abs(p.z - d.maxZ), dWest = Mathf.Abs(p.x - d.minX), dEast = Mathf.Abs(p.x - d.maxX);
            bool alongX = p.x >= d.minX - 0.5f && p.x <= d.maxX + 0.5f, alongZ = p.z >= d.minZ - 0.5f && p.z <= d.maxZ + 0.5f;
            float best = 9f; Vector3 inward = Vector3.zero;
            if (alongX && dSouth < best) { best = dSouth; inward = Vector3.forward; }
            if (alongX && dNorth < best) { best = dNorth; inward = Vector3.back; }
            if (alongZ && dWest < best) { best = dWest; inward = Vector3.right; }
            if (alongZ && dEast < best) { best = dEast; inward = Vector3.left; }
            if (best > 1.2f || inward == Vector3.zero) continue;
            bool dup = false;
            foreach (Doorway e in result) if (new Vector2(e.center.x - p.x, e.center.z - p.z).magnitude < 0.8f) dup = true;
            if (dup) continue;
            // snap the along-axis coordinate to the frame, the across-axis one to the rectangle edge, then step outside
            Vector3 c = p; c.y = d.floorY;
            result.Add(new Doorway { center = c - inward * (best < 0.3f ? 1.0f : 1.0f), inward = inward, width = 1.1f });
        }
        return result;
    }
}

public static partial class AsylumHubBuilder
{
    /// <summary>
    /// A real doorway is a gap in one wall plane: the wall is hit on both sides of it at the same distance, and the middle is open. A room rectangle
    /// that merely lies inside a bigger open space also gives two NavMesh points with a clear segment between them, but has no wall at its ends.
    /// </summary>
    private static bool WallFlanked(AsylumHub hub, Doorway d, float floorY)
    {
        Vector3 lateral = Vector3.Cross(Vector3.up, d.inward).normalized;
        Vector3 from = d.center - d.inward * 0.3f;
        from.y = floorY + 1.4f;
        float off = d.width * 0.5f + 0.4f;
        if (!WallHit(hub, from + lateral * off, d.inward, out RaycastHit a) || !WallHit(hub, from - lateral * off, d.inward, out RaycastHit b)) return false;
        if (Mathf.Abs(a.distance - b.distance) > 0.25f) return false;
        float wall = Mathf.Min(a.distance, b.distance);
        if (WallHit(hub, from, d.inward, out RaycastHit c) && Mathf.Abs(c.distance - wall) < 0.15f) return false; // the middle is wall too
        return true;
    }
}
