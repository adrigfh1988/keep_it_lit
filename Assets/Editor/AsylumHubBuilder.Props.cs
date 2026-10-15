using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

/// <summary>
/// F101: the hub's lived-in-then-abandoned props - the statue bay (phase A), the three halls (B), story vignettes with examine spots (C) and
/// the rooms (D) - added to the EXISTING, hand-edited hub prefab non-destructively and never regenerated (Dress() is never called). Every
/// piece is one marker: Dressing/&lt;Room&gt;/F101_&lt;Piece&gt;_Done for static dressing, Life/Vignettes/&lt;Name&gt; for floor-dependent scenes.
/// <see cref="PropsRepair"/> only builds a piece whose marker is missing, so a prop the owner moves or deletes in Prefab Mode is never put
/// back. Props are placed by renderer bounds with the F92/F98 helpers (Put / On / Hang / FindArea), rejected on any collider or on the
/// walk lanes (<see cref="ComputeKeepOutF101"/>: the routes, the pursuer's patrol and chase lanes, the echo route, mission items, NPCs, the lift,
/// both stair feet), with no new Light, no primitive decor and no random stream outside the room's own System.Random. Spec: plannings/hub-props-plan.md.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string PropsTag = "F101_";
    private const string VignettesName = "Vignettes";

    private static System.Text.StringBuilder _pr = new System.Text.StringBuilder();
    private static readonly HashSet<string> PropsTouchedRooms = new HashSet<string>();

    private sealed class PropsPiece
    {
        public string name; public char phase;
        public string room;   // Dressing/<room> for static dressing; null for a vignette
        public System.Action<AsylumHub> build;
        public string Prefix => PropsTag + name + "_";
        public string Marker => PropsTag + name + "_Done";
    }

    // ---------------------------------------------------------------- the pieces, in build order

    private static List<PropsPiece> PropsPieces()
    {
        List<PropsPiece> list = new List<PropsPiece>();
        list.Add(new PropsPiece { name = "Bay", phase = 'A', room = "HallDownstair", build = BuildBay });
        PropsPiecesHalls(list);
        PropsPiecesVignettes(list);
        PropsPiecesRooms(list);
        return list;
    }

    static partial void PropsPiecesHalls(List<PropsPiece> list);
    static partial void PropsPiecesVignettes(List<PropsPiece> list);
    static partial void PropsPiecesRooms(List<PropsPiece> list);

    private static bool PropsDone(AsylumHub hub, PropsPiece p)
    {
        if (p.room == null)
        {
            Transform vig = hub.life != null ? hub.life.Find(VignettesName) : null;
            return vig != null && vig.Find(p.name) != null;
        }
        Transform room = hub.dressing != null ? hub.dressing.Find(p.room) : null;
        return room != null && room.Find(p.Marker) != null;
    }

    private static List<PropsPiece> PropsMissing(AsylumHub hub) => PropsPieces().Where(p => !PropsDone(hub, p)).ToList();

    // ---------------------------------------------------------------- ProjectSetup entry points

    /// <summary>What F101 still lacks in the scene hub (null = complete, or no hub at all).</summary>
    public static string PropsProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null; // the "Asylum hub" piece reports that
        List<string> problems = new List<string>();
        List<PropsPiece> missing = PropsMissing(hub);
        if (missing.Count > 0) problems.Add("missing: " + string.Join(", ", missing.Select(p => p.name)));
        if (HubVignetteText.LongestLine("en") > 100) problems.Add("a vignette line is over 100 characters");
        if (!HubVignetteText.AllCaps()) problems.Add("a vignette line is not ALL CAPS");
        return problems.Count == 0 ? null : string.Join("; ", problems);
    }

    /// <summary>ProjectSetup's fix: adds every missing piece to the scene hub, rebakes, refits fog and probes and applies the additions to the prefab. Marks the scene dirty; the caller saves.</summary>
    public static void PropsRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Hall props: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        if (hub.life == null || hub.dressing == null) { Debug.LogError("Hall props: the hub has no Life / Dressing group - run Build Missing Pieces (story layer) first."); return; }
        AddProps(hub);
    }

    /// <summary>Builds every missing piece of the given phases ("A", "AB", ...), then rebakes, refits fog and probes, audits and applies. Returns the report.</summary>
    public static string AddProps(AsylumHub hub, string phases = "ABCD")
    {
        List<PropsPiece> todo = PropsMissing(hub).Where(p => phases.IndexOf(p.phase) >= 0).ToList();
        if (todo.Count == 0) { Debug.Log("Hall props (F101): nothing to add."); return "nothing to add"; }
        bool wasActive = hub.ContentActive;
        string placed = PropsPlace(hub, todo);
        string finish = PropsFinish(hub, wasActive);
        string text = placed + "\n" + finish;
        Debug.Log("Hall props (F101):\n" + text);
        return text;
    }

    // ---------------------------------------------------------------- dev entry points (evals: place without finishing, finish, reset)

    /// <summary>Dev: builds the missing pieces of the phases without rebake / apply. Pair with <see cref="DevPropsFinish"/>.</summary>
    public static string DevPropsPlace(string phases)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return "no hub";
        List<PropsPiece> todo = PropsMissing(hub).Where(p => phases.IndexOf(p.phase) >= 0).ToList();
        if (todo.Count == 0) return "nothing to add";
        string text = PropsPlace(hub, todo);
        PropsEndPoses(hub);
        return text;
    }

    /// <summary>Dev: rebake, fog, probes, audit and prefab apply after DevPropsPlace.</summary>
    public static string DevPropsFinish()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        return hub == null ? "no hub" : PropsFinish(hub, true);
    }

    /// <summary>Dev: removes pieces by name (their marker, their props and, for a vignette, its Life group) so they can be built again. Not applied to the prefab.</summary>
    public static string DevPropsReset(string names)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return "no hub";
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        foreach (PropsPiece p in PropsPieces())
        {
            if (names != "*" && !names.Split(',').Contains(p.name)) continue;
            int removed = 0;
            if (p.room == null)
            {
                Transform vig = hub.life != null ? hub.life.Find(VignettesName) : null;
                Transform t = vig != null ? vig.Find(p.name) : null;
                if (t != null) { Object.DestroyImmediate(t.gameObject); removed++; }
                // the vignette's static dressing (anything named after it) lives in Dressing/<room>/F101_<name>_*
            }
            foreach (Transform room in hub.dressing)
            {
                for (int i = room.childCount - 1; i >= 0; i--)
                {
                    Transform c = room.GetChild(i);
                    if (c.name.StartsWith(p.Prefix)) { Object.DestroyImmediate(c.gameObject); removed++; }
                }
            }
            sb.AppendLine($"{p.name}: {removed} removed");
        }
        return sb.ToString();
    }

    // ---------------------------------------------------------------- the run

    private static string PropsPlace(AsylumHub hub, List<PropsPiece> todo)
    {
        _pr = new System.Text.StringBuilder();
        PropsTouchedRooms.Clear();
        DressLog = "";
        PropsBegin(hub);
        foreach (PropsPiece p in todo)
        {
            int before = CountF101(hub);
            p.build(hub);
            if (p.room != null) PropsTouchedRooms.Add(p.room);
            _pr.AppendLine($"== piece {p.name} ({p.phase}) done: {CountF101(hub) - before} F101 objects");
        }
        if (DressLog.Length > 0) _pr.AppendLine("dress log: " + DressLog.Trim());
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        return _pr.ToString();
    }

    private static int CountF101(AsylumHub hub)
    {
        int n = 0;
        if (hub.dressing != null) foreach (Transform room in hub.dressing) foreach (Transform t in room) if (t.name.StartsWith(PropsTag)) n++;
        Transform vig = hub.life != null ? hub.life.Find(VignettesName) : null;
        if (vig != null) foreach (Transform v in vig) n += v.GetComponentsInChildren<Transform>(true).Length;
        return n;
    }

    private static void PropsBegin(AsylumHub hub)
    {
        _building = hub.building;
        hub.SetContentActive(true);
        SetPosesActive(hub, true); // the union of every vignette pose counts as solid while anything else is placed
        Physics.SyncTransforms();
        ComputeKeepOutF101(hub);
        _sheet = AssetDatabase.LoadAssetAtPath<Material>(SheetMaterialPath);
        EnsureFolderPath("Assets/SourceFiles/Materials/Asylum");
    }

    private static void SetPosesActive(AsylumHub hub, bool on)
    {
        Transform vig = hub.life != null ? hub.life.Find(VignettesName) : null;
        if (vig == null) return;
        foreach (HubVignette v in vig.GetComponentsInChildren<HubVignette>(true)) v.SetAllActive(on);
        Physics.SyncTransforms();
    }

    private static void PropsEndPoses(AsylumHub hub)
    {
        SetPosesActive(hub, false);
    }

    /// <summary>Rebake the Hub NavMesh with every pose solid, audit, put the poses away, refit fog and probes, apply to the prefab.</summary>
    private static string PropsFinish(AsylumHub hub, bool wasActive)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        _building = hub.building;
        hub.SetContentActive(true);
        SetPosesActive(hub, true);

        // 1. the NavMesh is baked from physics colliders: new big props reshape the pursuer's world
        sb.AppendLine("navmesh rebake: " + HubNavMesh.Bake(hub));

        // 2. audit with every pose solid (the strictest layout)
        sb.AppendLine(AuditF101(hub));
        SetPosesActive(hub, false);

        // 3. fog: the emitters follow the new floor triangulation (FogRepair only adds missing ones, so drop the touched rooms' first)
        Transform fog = FogGroup(hub, false);
        if (fog != null)
        {
            int dropped = 0;
            for (int i = fog.childCount - 1; i >= 0; i--)
            {
                string n = fog.GetChild(i).name;
                if (PropsTouchedRooms.Any(room => n.StartsWith("Fog_" + room))) { Object.DestroyImmediate(fog.GetChild(i).gameObject); dropped++; }
            }
            sb.AppendLine($"fog emitters dropped for the refit: {dropped}");
        }
        FogRepair();

        // 4. probes (a static a domain reload clears: set and used in this one call)
        ForceProbeRebuild = true;
        try { LookRepair(); } finally { ForceProbeRebuild = false; }

        // 5. apply (additions only), then put the scene back as it was
        hub.SetContentActive(wasActive);
        ApplyHubToPrefab(hub);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        sb.AppendLine("applied to " + PrefabPath);
        return sb.ToString();
    }

    // ---------------------------------------------------------------- audit

    /// <summary>The standard hub audit plus the F101 lines (every line ends in OK when it passes).</summary>
    public static string AuditF101(AsylumHub hub)
    {
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        sb.AppendLine(Audit(hub.transform)); // includes HubNavMesh.Audit, the walk graph and AuditNpcReach

        // every pursuer / chase / echo point is still on the Hub NavMesh
        int type = HubNavMesh.FindAgentType();
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = type, areaMask = NavMesh.AllAreas };
        List<string> off = new List<string>();
        int checkedPoints = 0;
        foreach (string group in new[] { "PatrolRoute", "EchoRoute", "ChaseSet" })
        {
            Transform g = hub.life != null ? hub.life.Find(group) : null;
            if (g == null) continue;
            foreach (Transform t in g)
            {
                if (group == "ChaseSet" && t.name != "TopZone" && t.name != "EndZone" && t.name != "RiseSpot") continue;
                checkedPoints++;
                Vector3 p = t.position;
                // the hand-placed points sit at their floor height (not always exactly on the mesh)
                if (!NavMesh.SamplePosition(p, out NavMeshHit hit, group == "ChaseSet" && t.name != "RiseSpot" ? 1.6f : 0.5f, filter)) off.Add($"{group}/{t.name} {p:F1}");
            }
        }
        sb.AppendLine($"  pursuer patrol / echo / chase points on the NavMesh ({checkedPoints} checked): {(off.Count == 0 ? "OK" : "OFF: " + string.Join(", ", off))}");

        // S9 (plan D16): English blocks at 100 and must be ALL CAPS; ES / FR / DE are measured against 120 and only reported.
        int line = HubVignetteText.LongestLine("en");
        sb.AppendLine($"  vignette lines: longest EN {line} characters (limit 100), all caps (EN source): {(line <= 100 && HubVignetteText.AllCaps() ? "OK" : "BROKEN")}");
        foreach (string code in new[] { "es", "fr", "de" })
        {
            int longest = HubVignetteText.LongestLine(code);
            if (longest > 0) sb.AppendLine($"    vignette {code}: longest line {longest} characters (limit 120, report only){(longest > 120 ? " - OVER" : "")}");
        }

        // a hung or standing F101 prop must not intrude on the walk lanes (the lane test is the same one placement used, margin 0.3 m)
        int intruders = 0;
        List<string> names = new List<string>();
        if (hub.dressing != null)
        {
            foreach (Transform room in hub.dressing)
            {
                foreach (Transform t in room)
                {
                    if (!t.name.StartsWith(PropsTag) || t.name.EndsWith("_Done")) continue;
                    BoxCollider bc = t.GetComponentInChildren<BoxCollider>();
                    if (bc == null) continue;
                    Vector3 c = bc.bounds.center; Vector3 ext = bc.bounds.extents;
                    if (KeepOutHit(c, ext, 0f, RoomFloorY(c.y), 0.3f)) { intruders++; if (names.Count < 6) names.Add(t.name); }
                }
            }
        }
        sb.AppendLine($"  F101 solid props off the walk lanes: {(intruders == 0 ? "OK" : intruders + " too close: " + string.Join(", ", names))}");
        sb.AppendLine($"  F101 objects in Dressing: {CountF101Dressing(hub)} OK");
        return sb.ToString();
    }

    private static int CountF101Dressing(AsylumHub hub)
    {
        int n = 0;
        if (hub.dressing != null) foreach (Transform room in hub.dressing) foreach (Transform t in room) if (t.name.StartsWith(PropsTag) && !t.name.EndsWith("_Done")) n++;
        return n;
    }

    private static float RoomFloorY(float y)
    {
        float best = 0.07f;
        foreach (float f in new[] { 0.07f, 1.15f, 5.0f, 8.82f }) if (f <= y + 0.7f) best = f;
        return best;
    }

    // ---------------------------------------------------------------- keep-out lanes

    /// <summary>
    /// The F92 keep-out (entrance -> wing routes, wing arrivals, entrance) plus F101's: the shop door's route, the pursuer's patrol loop, the
    /// floor-4 chase lane, the Stan echo route, mission items, NPC spots, the lift / gate / lockers and both stair feet. Lanes are Hub-NavMesh paths
    /// sampled every half metre; Put keeps 0.85 m clear of every point (so a lane is at least 1.7 m wide).
    /// </summary>
    private static void ComputeKeepOutF101(AsylumHub hub)
    {
        ComputeKeepOut(hub);
        int type = HubNavMesh.FindAgentType();
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = type, areaMask = NavMesh.AllAreas };
        int lanePoints = 0;

        void Add(Vector3 p) { _keepOut.Add(p); lanePoints++; }
        void Lane(Vector3 a, Vector3 b)
        {
            if (type == -1) return;
            if (!NavMesh.SamplePosition(a, out NavMeshHit ha, 2f, filter) || !NavMesh.SamplePosition(b, out NavMeshHit hb, 2f, filter)) return;
            NavMeshPath path = new NavMeshPath();
            if (!NavMesh.CalculatePath(ha.position, hb.position, filter, path) || path.corners.Length < 2) return;
            Vector3 last = path.corners[0];
            Add(last);
            for (int i = 1; i < path.corners.Length; i++)
            {
                Vector3 from = path.corners[i - 1], to = path.corners[i];
                float len = Vector3.Distance(from, to);
                for (float s = 0.5f; s <= len; s += 0.5f) Add(Vector3.Lerp(from, to, s / len));
                Add(to);
            }
        }

        if (hub.life != null)
        {
            Transform patrol = hub.life.Find("PatrolRoute");
            if (patrol != null && patrol.childCount > 1)
            {
                for (int i = 0; i < patrol.childCount; i++) Lane(patrol.GetChild(i).position, patrol.GetChild((i + 1) % patrol.childCount).position);
            }
            Transform echo = hub.life.Find("EchoRoute");
            if (echo != null) for (int i = 1; i < echo.childCount; i++) Lane(echo.GetChild(i - 1).position, echo.GetChild(i).position);
            Transform chase = hub.life.Find("ChaseSet");
            if (chase != null)
            {
                Transform rise = chase.Find("RiseSpot"), top = chase.Find("TopZone"), end = chase.Find("EndZone");
                if (rise != null && top != null) Lane(rise.position, top.position);
                if (top != null && end != null) Lane(top.position, end.position);
            }
            foreach (HubMissionItem item in hub.life.GetComponentsInChildren<HubMissionItem>(true)) Add(item.transform.position);
            foreach (HubNpc npc in hub.life.GetComponentsInChildren<HubNpc>(true)) Add(npc.anchor != null ? npc.anchor.position : npc.transform.position);
            Transform lift = hub.life.Find("LiftSet");
            if (lift != null) foreach (string n in new[] { "GroundShaftDoor", "CallPoint", "PryPoint", "Gate" }) { Transform t = lift.Find(n); if (t != null) Add(t.position); }
            Transform lockers = hub.life.Find("Lockers");
            if (lockers != null) foreach (Transform locker in lockers) { Transform front = locker.Find("FrontAnchor"); if (front != null) Add(front.position); }
            foreach (HubPatient patient in hub.life.GetComponentsInChildren<HubPatient>(true)) if (patient.body != null) Add(patient.body.position);
        }
        if (hub.shop != null && hub.shop.doorHinge != null && hub.shop.roomZone != null && hub.mainEntrance != null)
        {
            // F116: the hall side of the Commissary door, 1.5 m out from the wall (away from the room)
            Vector3 away = ShopHallDirection(hub.shop);
            Bounds shopFrame = ShopFrameBounds(hub.shop);
            Vector3 hallSide = new Vector3(shopFrame.center.x, ShopFloorY, shopFrame.center.z) + away * 1.5f;
            Add(hallSide);
            Lane(hub.mainEntrance.position, hallSide);
        }
        // every pack door: the approach on both sides (a 2.4 m deep, door-wide strip) stays open, so no room is walled off from its hall by furniture
        if (hub.building != null)
        {
            int doors = 0;
            foreach (Transform t in hub.building.GetComponentsInChildren<Transform>(true))
            {
                if (!(t.name.StartsWith("Door_Frame") || t.name.StartsWith("DoorD_Frame"))) continue;
                Renderer rr = t.GetComponentInChildren<Renderer>(true);
                if (rr == null) continue;
                Bounds b = rr.bounds;
                bool thinX = b.size.x < b.size.z; // the door's thin axis is its normal
                for (float s = -2.4f; s <= 2.4f; s += 0.6f)
                    for (float w = -0.5f; w <= 0.5f; w += 0.5f)
                    {
                        Vector3 p = thinX ? new Vector3(b.center.x + s, b.min.y + 0.1f, b.center.z + w * (b.size.z * 0.5f)) : new Vector3(b.center.x + w * (b.size.x * 0.5f), b.min.y + 0.1f, b.center.z + s);
                        _keepOut.Add(p); lanePoints++;
                    }
                doors++;
            }
            _pr.AppendLine($"door approaches kept open: {doors} doors");
        }
        // both stair feet (the routes up are already lanes; the feet themselves stay open for the chase and the way up)
        foreach (string n in new[] { "Stairs_Left", "Stairs_Right" })
        {
            Transform s = FindBuildingNear(n, hub.mainEntrance.position);
            if (s == null) continue;
            Bounds b = RendBounds(s.gameObject);
            float z = b.min.z + 0.4f;
            for (float x = b.min.x; x <= b.max.x; x += 0.5f) { Add(new Vector3(x, b.min.y + 0.1f, z)); Add(new Vector3(x, b.min.y + 0.1f, z - 0.9f)); }
        }
        _pr.AppendLine($"keep-out points: {_keepOut.Count} ({lanePoints} added by F101)");
    }

    /// <summary>The building transform named `name` whose renderer bounds are closest to `near` (several pieces share names).</summary>
    private static Transform FindBuildingNear(string name, Vector3 near)
    {
        Transform best = null; float bestD = float.MaxValue;
        foreach (Transform t in _building.GetComponentsInChildren<Transform>(true))
        {
            if (t.name != name) continue;
            Renderer[] rs = t.GetComponentsInChildren<Renderer>(true);
            if (rs.Length == 0) continue;
            float d = (RendBounds(t.gameObject).center - near).sqrMagnitude;
            if (d < bestD) { bestD = d; best = t; }
        }
        return best;
    }

    // ---------------------------------------------------------------- placement helpers

    /// <summary>Counts the placement keys the pack lacks (a missing prefab shows in DressLog).</summary>
    private static DressRoomCtx PropsCtx(AsylumHub hub, string room, string piece) => NewDressCtx(hub, Def(room), PropsTag + piece + "_");

    /// <summary>Names the new children F101_Piece_*, flags them static like the building (no batching) and drops the marker.</summary>
    private static int PropsFinishCtx(DressRoomCtx r, PropsPiece p)
    {
        int n = FinishDressCtx(r, p.Prefix);
        int trimmed = 0;
        for (int i = r.childStart; i < r.group.childCount; i++) trimmed += TrimShadows(r.group.GetChild(i));
        GameObject marker = new GameObject(p.Marker);
        marker.transform.SetParent(r.group, false);
        _pr.AppendLine($"   {p.name}/{r.def.name}: {r.placed} placed, {r.rejected} rejected" + (r.rejects.Count > 0 ? "; " + string.Join(" | ", r.rejects.Where(x => !x.StartsWith("star")).Take(14)) : ""));
        return n;
    }

    /// <summary>
    /// A wall-mounted prop (or a decal quad: `decal`) on the first building wall met from `origin` along the horizontal direction `dir`, centred
    /// `height` metres above the room's floor, facing into the room (+Z of the prefab at yawRel 0). The back touches the wall (plus `inset`); no
    /// collider; `scale` multiplies it. Returns null (nothing left behind) when the ray meets no building wall.
    /// </summary>
    private static Placed Hang(DressRoomCtx r, string key, Vector3 origin, Vector3 dir, float height, float yawRel = 0f, float scale = 1f, float inset = 0.02f, bool decal = false, float reach = 6f)
    {
        dir.y = 0f; dir.Normalize();
        Vector3 o = new Vector3(origin.x, r.def.floorY + height, origin.z);
        if (!Physics.Raycast(o, dir, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore))
        {
            r.rejected++; r.rejects.Add($"hang {key} at {o:F1}: no wall within {reach} m"); return null;
        }
        if (_building == null || !hit.collider.transform.IsChildOf(_building) || Mathf.Abs(hit.normal.y) > 0.25f)
        {
            r.rejected++; r.rejects.Add($"hang {key} at {o:F1}: first hit is {hit.collider.name}"); return null;
        }
        Vector3 n = hit.normal; n.y = 0f; n.Normalize();
        GameObject go = Spawn(key, r.group);
        if (go == null) return null;
        float yaw = Quaternion.LookRotation(n).eulerAngles.y + yawRel;
        if (decal)
        {
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            go.transform.localScale = Vector3.one * scale;
            go.transform.position = hit.point + n * inset + Vector3.up * (o.y - hit.point.y);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
            r.placed++;
            r.perKey["decal:" + key] = r.perKey.TryGetValue("decal:" + key, out int d0) ? d0 + 1 : 1;
            return new Placed { go = go, key = key, center = go.transform.position, size = Vector3.one * scale, yaw = yaw };
        }
        float front = FrontYaw.TryGetValue(key, out float f) ? f : 0f;
        Quaternion assetRot = AssetDatabase.LoadAssetAtPath<GameObject>(PropPath(key)).transform.rotation;
        go.transform.rotation = Quaternion.Euler(0f, yaw + front, 0f) * assetRot;
        if (!Mathf.Approximately(scale, 1f)) go.transform.localScale *= scale;
        Bounds b = RendBounds(go);
        float extN = Mathf.Abs(n.x) * b.extents.x + Mathf.Abs(n.z) * b.extents.z;
        Vector3 target = new Vector3(hit.point.x, o.y, hit.point.z) + n * (extN + inset);
        go.transform.position += target - b.center;
        b = RendBounds(go);
        foreach (Renderer rr in go.GetComponentsInChildren<Renderer>(true)) rr.shadowCastingMode = ShadowCastingMode.Off;
        r.placed++;
        r.perKey[key] = r.perKey.TryGetValue(key, out int n0) ? n0 + 1 : 1;
        Placed p = new Placed { go = go, key = key, center = b.center, size = b.size, yaw = yaw };
        r.all.Add(p);
        return p;
    }

    /// <summary>A wall frame (+Z into the room, origin on the wall) found by a ray from `from` along the horizontal `dir`. Null when nothing flat is hit.</summary>
    private static Frame WallFrameFrom(Vector3 from, Vector3 dir, float reach = 8f)
    {
        dir.y = 0f; dir.Normalize();
        if (!Physics.Raycast(from, dir, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore)) return null;
        if (_building == null || !hit.collider.transform.IsChildOf(_building) || Mathf.Abs(hit.normal.y) > 0.25f) return null;
        Vector3 n = hit.normal; n.y = 0f; n.Normalize();
        return new Frame { pos = new Vector3(hit.point.x, 0f, hit.point.z), yaw = Quaternion.LookRotation(n).eulerAngles.y };
    }

    /// <summary>True when a `halfW` x `halfH` patch around the ray's hit on a building wall is one flat plane (four probes), and the wall is plain (no window, door, frame, arch, lift, gate, stair).</summary>
    private static bool FlatWallAt(Vector3 origin, Vector3 dir, float halfW, float halfH, out Vector3 point, out Vector3 normal, out string why, float reach = 8f)
    {
        point = default; normal = default; why = null;
        dir.y = 0f; dir.Normalize();
        if (!Physics.Raycast(origin, dir, out RaycastHit hit, reach, ~0, QueryTriggerInteraction.Ignore)) { why = "no wall"; return false; }
        if (_building == null || !hit.collider.transform.IsChildOf(_building)) { why = "first hit is " + hit.collider.name; return false; }
        if (Mathf.Abs(hit.normal.y) > 0.25f) { why = "not vertical"; return false; }
        string[] skip = { "Window", "Door", "Frame", "Arch", "Elevator", "Lift", "Gate", "Stair", "Suspension", "Lamp" };
        for (Transform t = hit.collider.transform; t != null && t != _building; t = t.parent)
            foreach (string s in skip) if (t.name.Contains(s)) { why = "a " + s; return false; }
        Vector3 n = hit.normal; n.y = 0f; n.Normalize();
        Vector3 across = Vector3.Cross(Vector3.up, n);
        for (int i = 0; i < 4; i++)
        {
            Vector3 off = across * (i < 2 ? halfW : -halfW) + Vector3.up * (i % 2 == 0 ? halfH : -halfH);
            if (!Physics.Raycast(hit.point + n * 0.4f + off, -n, out RaycastHit probe, 0.7f, ~0, QueryTriggerInteraction.Ignore)) { why = "a gap"; return false; }
            if (!probe.collider.transform.IsChildOf(_building) || Mathf.Abs(probe.distance - 0.4f) > 0.08f) { why = "not flat"; return false; }
        }
        point = hit.point; normal = n;
        return true;
    }

    // ---------------------------------------------------------------- stars (the chalk sheet, one quad mesh per quadrant)

    private const string StarMeshFolder = "Assets/SourceFiles/Meshes/Hub";
    private const string StarFreshMaterialPath = "Assets/SourceFiles/Materials/Asylum/HubChalkStarFresh.mat";
    private const string WallPaintMaterialPath = "Assets/SourceFiles/Materials/Asylum/HubWallPaint.mat";

    /// <summary>The four quad meshes (one per quadrant of ChalkStars.png; the same UVs HubStarTrail's property blocks pick), saved as assets so a prefab can hold them.</summary>
    private static Mesh[] EnsureStarQuads()
    {
        EnsureFolderPath(StarMeshFolder);
        Mesh[] meshes = new Mesh[4];
        for (int v = 0; v < 4; v++)
        {
            string path = $"{StarMeshFolder}/StarQuad_{v}.asset";
            Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (m == null)
            {
                m = new Mesh { name = $"StarQuad_{v}" };
                Vector2 off = new Vector2((v & 1) * 0.5f, (v >> 1) * 0.5f);
                m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
                m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
                m.uv = new[] { off, off + new Vector2(0.5f, 0f), off + new Vector2(0f, 0.5f), off + new Vector2(0.5f, 0.5f) };
                m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
                m.RecalculateBounds();
                AssetDatabase.CreateAsset(m, path);
            }
            meshes[v] = m;
        }
        return meshes;
    }

    private static Material EnsureStarFreshMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(StarFreshMaterialPath);
        if (mat != null) return mat;
        Material source = EnsureStarMaterial(EnsureStarTexture());
        mat = new Material(source) { name = "HubChalkStarFresh" };
        mat.SetColor("_BaseColor", new Color(1f, 1f, 0.96f, 1f));
        mat.SetColor("_EmissionColor", new Color(0.92f, 1.15f, 0.96f));
        AssetDatabase.CreateAsset(mat, StarFreshMaterialPath);
        return mat;
    }

    /// <summary>A flat, rounded grey patch with soft edges: the paint someone rolled over the chalk. Transparent, no emission.</summary>
    private static Material EnsureWallPaintMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(WallPaintMaterialPath);
        if (mat != null) return mat;
        Material source = EnsureStarMaterial(EnsureStarTexture());
        mat = new Material(source) { name = "HubWallPaint" };
        Texture2D mask = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/SourceFiles/Textures/Decals/PuddleMask.png");
        mat.SetTexture("_BaseMap", mask);
        mat.SetTextureScale("_BaseMap", Vector2.one);
        mat.SetTextureOffset("_BaseMap", Vector2.zero);
        mat.SetColor("_BaseColor", new Color(0.30f, 0.31f, 0.31f, 0.9f));
        mat.SetTexture("_EmissionMap", null);
        mat.SetColor("_EmissionColor", Color.black);
        mat.DisableKeyword("_EMISSION");
        mat.SetFloat("_Smoothness", 0.05f);
        AssetDatabase.CreateAsset(mat, WallPaintMaterialPath);
        return mat;
    }

    /// <summary>A chalk star (variant 0..3 of the sheet) or a paint patch on a wall: facing the room, `size` m across, rolled `roll` degrees, under `parent`.</summary>
    private static GameObject WallQuad(Transform parent, string name, Mesh mesh, Material material, Vector3 point, Vector3 normal, float size, float roll, float aspect = 1f, float lift = 0.02f)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(point + normal * lift, Quaternion.LookRotation(-normal, Vector3.up) * Quaternion.Euler(0f, 0f, roll));
        go.transform.localScale = new Vector3(size * aspect, size, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        MeshRenderer mr = go.AddComponent<MeshRenderer>();
        mr.sharedMaterial = material;
        mr.shadowCastingMode = ShadowCastingMode.Off;
        mr.receiveShadows = false;
        mr.lightProbeUsage = LightProbeUsage.Off;
        mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
        return go;
    }

    /// <summary>Statically flags every visual under `t` like the building (never batching) - Dressing props only; Life objects stay unflagged.</summary>
    private static void FlagStatic(Transform t)
    {
        const StaticEditorFlags flags = StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic;
        foreach (Transform c in t.GetComponentsInChildren<Transform>(true))
        {
            bool visual = c.GetComponent<Renderer>() != null || c.GetComponent<Collider>() != null;
            GameObjectUtility.SetStaticEditorFlags(c.gameObject, visual ? flags : 0);
        }
    }

    /// <summary>Clears the static flags of everything under `t` (vignette poses move with floors; the creak chair moves).</summary>
    private static void ClearStatic(Transform t)
    {
        foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(c.gameObject, 0);
    }

    // ---------------------------------------------------------------- vignette plumbing

    private static Transform VignetteRoot(AsylumHub hub)
    {
        return hub.life.Find(VignettesName) ?? Group(hub.life, VignettesName);
    }

    /// <summary>A pose object under Life/Vignettes/&lt;name&gt; (created with its HubVignette on first use).</summary>
    private static Transform NewPose(Transform vignette, string poseName)
    {
        Transform pose = Group(vignette, poseName);
        return pose;
    }

    /// <summary>A placement context whose props land under `pose` (a Life object): same checks, no cell scan unless asked.</summary>
    private static DressRoomCtx PoseCtx(Transform pose, string room, string tag, bool scan = false)
    {
        DressRoomCtx r = new DressRoomCtx { def = Def(room), group = pose, rng = new System.Random(StableHash(tag + room)) };
        r.childStart = 0;
        if (scan) ScanCells(r);
        return r;
    }

    /// <summary>Wires the pose list of `vignette` (serialized private field) and marks it.</summary>
    private static void WirePoses(HubVignette vignette, List<(int min, int max, GameObject root)> poses)
    {
        SerializedObject so = new SerializedObject(vignette);
        SerializedProperty arr = so.FindProperty("poses");
        arr.arraySize = poses.Count;
        for (int i = 0; i < poses.Count; i++)
        {
            SerializedProperty e = arr.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("minFloor").intValue = poses[i].min;
            e.FindPropertyRelative("maxFloor").intValue = poses[i].max;
            e.FindPropertyRelative("root").objectReferenceValue = poses[i].root;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(vignette);
    }

    /// <summary>A small trigger box (the interactor's spherecast collects triggers) with a HubExamine on `host`, wired by serialized fields.</summary>
    private static HubExamine MakeExamine(GameObject host, string key, string prompt, bool readable, int noteId, HubExamine.Sound sound, Vector3 worldCentre, Vector3 size, HubPatient knocker = null)
    {
        BoxCollider box = host.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.center = host.transform.InverseTransformPoint(worldCentre);
        Vector3 s = host.transform.lossyScale;
        box.size = new Vector3(size.x / Mathf.Max(0.0001f, s.x), size.y / Mathf.Max(0.0001f, s.y), size.z / Mathf.Max(0.0001f, s.z));
        HubExamine ex = host.AddComponent<HubExamine>();
        SerializedObject so = new SerializedObject(ex);
        so.FindProperty("key").stringValue = key;
        so.FindProperty("prompt").stringValue = prompt;
        so.FindProperty("readable").boolValue = readable;
        so.FindProperty("noteId").intValue = noteId;
        so.FindProperty("sound").enumValueIndex = (int)sound;
        so.FindProperty("knocker").objectReferenceValue = knocker;
        so.ApplyModifiedPropertiesWithoutUndo();
        return ex;
    }
}

public static partial class AsylumHubBuilder
{
    /// <summary>
    /// Small clutter (lower than 0.6 m and under 1 m across) does not cast shadows: the torch's shadow pass would draw every mug and sheet of
    /// paper twice, and nobody sees their shadows. Keeps the F101 additions inside the +15 % draw-call budget (plan 2.5).
    /// </summary>
    private static int TrimShadows(Transform root)
    {
        int n = 0;
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            if (r is ParticleSystemRenderer || r.shadowCastingMode == ShadowCastingMode.Off) continue;
            Vector3 s = r.bounds.size;
            if (s.y < 0.6f && Mathf.Max(s.x, s.z) < 1.0f) { r.shadowCastingMode = ShadowCastingMode.Off; n++; }
        }
        return n;
    }
}
