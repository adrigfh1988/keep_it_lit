using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// F101 phase C: eleven story vignettes under AsylumHub/Life/Vignettes/&lt;Name&gt; (Life is never touched by HubDecay), each a HubVignette with
/// pose children that switch with the floor (the evacuation queue turns back on floor 3 and faces the player on 5, a barricaded table is
/// shoved on floor 4, a ward's ampoule tray is missing on floor 4, ...). Readables use negative ids (-2 evacuation order, -3 rota, -4 Stan's
/// bay, -6 the lower-level tag) and examine spots a HubExamine; nothing is saved or paid. Positions come from FreeSpot / FindWall around
/// anchors read from the hub itself (doors, patients, mission items), never from fixed coordinates.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string ChalkHousePath = "Assets/SourceFiles/Textures/Decals/ChalkHouse.png";
    private const string ChalkHouseMaterialPath = "Assets/SourceFiles/Materials/Asylum/HubChalkHouse.mat";
    private const string ThresholdMaterialPath = "Assets/SourceFiles/Materials/Asylum/HubThresholdGlow.mat";

    static partial void PropsPiecesVignettes(List<PropsPiece> list)
    {
        list.Add(new PropsPiece { name = "Evacuation", phase = 'C', build = BuildVEvacuation });
        list.Add(new PropsPiece { name = "Tally", phase = 'C', build = BuildVTally });
        list.Add(new PropsPiece { name = "Barricade", phase = 'C', build = BuildVBarricade });
        list.Add(new PropsPiece { name = "Reception", phase = 'C', build = BuildVReception });
        list.Add(new PropsPiece { name = "NightPost", phase = 'C', build = BuildVNightPost });
        list.Add(new PropsPiece { name = "Room4", phase = 'C', build = BuildVRoom4 });
        list.Add(new PropsPiece { name = "EliasRoute", phase = 'C', build = BuildVEliasRoute });
        list.Add(new PropsPiece { name = "StanBay", phase = 'C', build = BuildVStanBay });
        list.Add(new PropsPiece { name = "TomasCorner", phase = 'C', build = BuildVTomasCorner });
        list.Add(new PropsPiece { name = "MartaChalk", phase = 'C', build = BuildVMartaChalk });
        list.Add(new PropsPiece { name = "LowerLevel", phase = 'C', build = BuildVLowerLevel });
    }

    // ---------------------------------------------------------------- plumbing

    private sealed class VignetteBuild
    {
        public Transform root; public HubVignette hv; public PropsPiece piece;
        public List<(int, int, GameObject)> poses = new List<(int, int, GameObject)>();
        public Transform Pose(string name, int min, int max)
        {
            foreach (var p in poses) p.Item3.SetActive(false); // earlier poses of this vignette must not block the next one's placement
            Transform t = NewPose(root, name);
            poses.Add((min, max, t.gameObject));
            return t;
        }
    }

    private static VignetteBuild StartVignette(AsylumHub hub, string name)
    {
        PropsPiece piece = PropsPieces().First(p => p.name == name);
        DevPropsResetQuiet(hub, piece);
        Transform vig = VignetteRoot(hub);
        VignetteBuild v = new VignetteBuild { piece = piece, root = Group(vig, name) };
        v.hv = v.root.gameObject.AddComponent<HubVignette>();
        return v;
    }

    private static void EndVignette(VignetteBuild v)
    {
        foreach (Transform t in v.root) { ClearStatic(t); TrimShadows(t); }
        WirePoses(v.hv, v.poses);
        foreach (var p in v.poses) p.Item3.SetActive(true); // stay solid while later pieces are placed
        Physics.SyncTransforms();
        foreach (DressRoomCtx c in VCtxs) if (c.rejects.Count > 0) _pr.AppendLine($"   {v.piece.name} rejects: " + string.Join(" | ", c.rejects.Take(6)));
        VCtxs.Clear();
        _pr.AppendLine($"   {v.piece.name}: {v.poses.Count} poses, {v.root.GetComponentsInChildren<Renderer>(true).Length} renderers, {v.root.GetComponentsInChildren<HubExamine>(true).Length} examine");
    }

    private static readonly List<DressRoomCtx> VCtxs = new List<DressRoomCtx>();
    private static DressRoomCtx VCtx(VignetteBuild v, Transform pose, string room) { DressRoomCtx c = PoseCtx(pose, room, PropsTag + v.piece.name + "_"); VCtxs.Add(c); return c; }

    /// <summary>
    /// A free rectangle (2*halfX by 2*halfZ, rotated by `yaw`) on the floor nearest to `anchor` within `radius`, off the walk lanes, seated on
    /// floor everywhere; with `wallSide`, one touching a wall within 0.9 m. Null when there is none. Returns the centre (floor height).
    /// </summary>
    private static Vector3? FreeSpot(DressRoomCtx r, Vector3 anchor, float radius, float halfX, float halfZ, float yaw, bool wallSide = false)
    {
        Quaternion q = Quaternion.Euler(0f, yaw, 0f);
        float best = float.MaxValue; Vector3? bestSpot = null;
        for (float dx = -radius; dx <= radius; dx += 0.4f)
        {
            for (float dz = -radius; dz <= radius; dz += 0.4f)
            {
                float dist = Mathf.Sqrt(dx * dx + dz * dz);
                if (dist > radius || dist >= best) continue;
                Vector3 c = new Vector3(anchor.x + dx, r.def.floorY, anchor.z + dz);
                Vector3 half = new Vector3(halfX, 0.45f, halfZ);
                if (Physics.CheckBox(c + Vector3.up * 0.55f, half, q, ~0, QueryTriggerInteraction.Ignore)) continue;
                if (KeepOutHit(c, half, yaw, r.def.floorY, 0.85f)) continue;
                bool floor = true;
                foreach (Vector2 k in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1), Vector2.zero })
                {
                    Vector3 p = c + q * new Vector3(k.x * halfX, 0f, k.y * halfZ);
                    if (!Physics.Raycast(new Vector3(p.x, r.def.floorY + 0.6f, p.z), Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore)) { floor = false; break; }
                }
                if (!floor) continue;
                if (wallSide)
                {
                    bool wall = false;
                    foreach (Vector3 d in new[] { Vector3.right, Vector3.left, Vector3.forward, Vector3.back })
                    {
                        float reach = (Mathf.Abs(d.x) > 0.5f ? halfX : halfZ) + 0.9f;
                        if (Physics.Raycast(c + Vector3.up * 1f, d, out RaycastHit h, reach + 0.5f, ~0, QueryTriggerInteraction.Ignore) && h.collider.transform.IsChildOf(_building)) { wall = true; break; }
                    }
                    if (!wall) continue;
                }
                best = dist; bestSpot = c;
            }
        }
        return bestSpot;
    }

    /// <summary>World text (TextMeshPro) on a wall facing the room, readable from the front. `size` is the font size.</summary>
    private static TextMeshPro WallText(Transform parent, string name, string text, Vector3 point, Vector3 normal, float size, Color color, float roll = 0f, float width = 2f)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, true);
        go.transform.SetPositionAndRotation(point + normal * 0.03f, Quaternion.LookRotation(-normal, Vector3.up) * Quaternion.Euler(0f, 0f, roll));
        go.transform.rotation = go.transform.rotation * Quaternion.Euler(0f, 0f, 0f);
        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.fontSize = size;
        tmp.color = color;
        tmp.textWrappingMode = TextWrappingModes.NoWrap;
        tmp.rectTransform.sizeDelta = new Vector2(width, 0.8f);
        tmp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        return tmp;
    }

    private static readonly Color ChalkWhite = new Color(0.86f, 0.86f, 0.8f);

    private static Vector3 AnchorOf(AsylumHub hub, string where)
    {
        switch (where)
        {
            case "Tomas":
                foreach (HubPatient p in hub.life.GetComponentsInChildren<HubPatient>(true)) if (p.patient == HubState.Patient.Tomas && p.body != null) return p.body.position;
                break;
            case "MusicBox":
                foreach (HubMissionItem it in hub.life.GetComponentsInChildren<HubMissionItem>(true)) if (it.name.Contains("MusicBox")) return it.transform.position;
                break;
        }
        return hub.mainEntrance.position;
    }

    // ---------------------------------------------------------------- 1. interrupted evacuation (ground hall, east of the front doors)

    private static void BuildVEvacuation(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "Evacuation");
        Vector3 entrance = hub.mainEntrance.position;
        DressRoomCtx probe = VCtx(v, v.root, "HallDownstair");
        Vector3? spot = FreeSpot(probe, entrance + new Vector3(8.5f, 0f, 5.5f), 15f, 2.4f, 1.4f, 0f, false);
        if (spot == null) { _pr.AppendLine("EVAC: no free spot east of the entrance"); EndVignette(v); return; }
        Vector3 c = spot.Value;
        _pr.AppendLine($"evacuation queue at {c:F1}");
        // positions along x; faces the entrance (queued for the doors)
        float[] xs = { -2.0f, -1.0f, 0f, 1.0f };
        Vector3 Facing(Vector3 from, Vector3 to) { Vector3 d = to - from; d.y = 0f; return d; }

        Transform queue = v.Pose("Queue", 1, 2);
        DressRoomCtx rq = VCtx(v, queue, "HallDownstair");
        foreach (float x in xs) Put(rq, "WheelChair", c + new Vector3(x * 1.1f, 0f, 0f), Quaternion.LookRotation(Facing(c, entrance)).eulerAngles.y + Jit(rq, 5f));
        Put(rq, "SurgeryWheelBad", c + new Vector3(-0.3f, 0f, -1.25f), 90f + Jit(rq, 4f), 1f, null, true);

        Transform turned = v.Pose("Turned", 3, 4);
        DressRoomCtx rt = VCtx(v, turned, "HallDownstair");
        for (int i = 0; i < xs.Length; i++)
        {
            float yaw = Quaternion.LookRotation(Facing(c, entrance)).eulerAngles.y + (i == 1 || i == 2 ? 180f : 0f) + Jit(rt, 8f);
            Put(rt, "WheelChair", c + new Vector3(xs[i] * 1.1f, 0f, 0f), yaw);
        }
        Put(rt, "SurgeryWheelBad", c + new Vector3(-0.3f, 0f, -1.25f), 90f + Jit(rt, 4f), 1f, null, true);

        Transform emptyFacing = v.Pose("Facing", 5, 5);
        DressRoomCtx rf = VCtx(v, emptyFacing, "HallDownstair");
        foreach (float x in xs) { Vector3 wp = c + new Vector3(x * 1.1f, 0f, 0f); Put(rf, "WheelChair", wp, Quaternion.LookRotation(Facing(wp, new Vector3(entrance.x, wp.y, entrance.z + 14f))).eulerAngles.y + Jit(rf, 6f)); }

        // the order, on the floor in front of the queue (always)
        Transform always = v.Pose("Always", 1, 5);
        DressRoomCtx ra = VCtx(v, always, "HallDownstair");
        Placed board = Put(ra, "m:writing_board", c + new Vector3(0f, 0f, -1.6f), 20f, 1f, null, false);
        if (board != null)
        {
            board.go.name = "EvacuationOrder";
            MakeExamine(board.go, "evac", "READ", true, -2, HubExamine.Sound.Paper, board.center + Vector3.up * 0.1f, new Vector3(0.9f, 0.5f, 0.9f));
        }
        EndVignette(v);
    }

    // ---------------------------------------------------------------- 2. Hilde's tally (ground hall wall, by the day room)

    private static void BuildVTally(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "Tally");
        DressRoomCtx probe = VCtx(v, v.root, "HallDownstair");
        Frame f = FindWallEither(probe, new Vector3(119.5f, 0f, -54.5f), Vector3.forward, -1, 1.0f);
        if (f == null) f = FindWallEither(probe, new Vector3(116f, 0f, -54.5f), Vector3.forward, -1, 1.0f);
        if (f == null) { _pr.AppendLine("TALLY: no wall"); EndVignette(v); return; }
        Vector3 n = f.Rot * Vector3.forward;
        Vector3 p = f.W(0f, 0f) + Vector3.up * 1.45f;
        _pr.AppendLine($"tally wall at {p:F1}");
        for (int floor = 1; floor <= 5; floor++)
        {
            Transform pose = v.Pose("Floor" + floor, floor, floor);
            string numbers = "38  39  40  41" + string.Concat(Enumerable.Range(42, floor).Select(i => "  " + i));
            WallText(pose, "Tally", numbers, p, n, 2.6f, ChalkWhite, -2f + floor, 2.4f);
            WallText(pose, "TallyMarks", "||||  ||||  |||", p - Vector3.up * 0.4f + f.Rot * Vector3.right * 0.1f, n, 2.2f, new Color(0.8f, 0.8f, 0.74f, 0.9f), 1f, 2.4f);
        }
        Transform always = v.Pose("Always", 1, 5);
        GameObject host = new GameObject("TallyExamine");
        host.transform.SetParent(always, true);
        host.transform.position = p;
        MakeExamine(host, "tally", "EXAMINE", false, 0, HubExamine.Sound.None, p + n * 0.5f, new Vector3(2.6f, 1.2f, 1.2f));
        EndVignette(v);
    }

    // ---------------------------------------------------------------- 3. the staff barricade (the west end of the ground hall, off every lane)

    private static void BuildVBarricade(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "Barricade");
        DressRoomCtx probe = VCtx(v, v.root, "HallDownstair");
        Transform always = v.Pose("Always", 1, 5);
        DressRoomCtx ra = VCtx(v, always, "HallDownstair");
        Frame f = null;
        // the first plain stretch of hall wall (clear of every door approach and lane) that takes the two cupboards
        foreach ((float x, int side) in new[] { (83.5f, -1), (86.5f, -1), (79.8f, -1), (82f, +1), (95.5f, -1), (113f, -1), (117f, -1) })
        {
            Frame cand = FindWallEither(probe, new Vector3(x, 0f, -54.5f), Vector3.forward, side, 1.2f);
            if (cand == null) continue;
            if (InFrame(ra, cand, "Cupboard", -1.1f, 0.45f, 0f, 1f, true) != null) { f = cand; break; }
        }
        if (f == null) { _pr.AppendLine("BARRICADE: no wall took the cupboards"); EndVignette(v); return; }
        _pr.AppendLine($"barricade wall at {f.pos:F1}");
        InFrame(ra, f, "Cupboard", 0.2f, 0.45f, 0f, 1f, true);
        Decal(ra, "BloodSmear", f.W(1.0f, 0.9f), 1.4f, f.yaw + 80f);
        Placed tray = InFrame(ra, f, "TableSmall", 1.6f, 0.55f, 0f, 1f, true);
        On(ra, tray, "Mug", -0.3f, 0f);
        On(ra, tray, "Mug", 0.2f, 0.1f);
        On(ra, tray, "Kettle", 0.6f, -0.1f);

        Transform base1 = v.Pose("TableIn", 1, 3);
        DressRoomCtx rb = VCtx(v, base1, "HallDownstair");
        Placed t1 = InFrame(rb, f, "TableWhite", -0.5f, 1.2f, 90f, 1f, false);
        Transform shoved = v.Pose("TableShoved", 4, 5);
        DressRoomCtx rs = VCtx(v, shoved, "HallDownstair");
        Placed t2 = InFrame(rs, f, "TableWhite", -0.5f, 1.8f, 78f, 1f, false);
        Decal(rs, "DragMarks", f.W(-0.5f, 1.5f), 1.7f, f.yaw + 90f);
        Placed ex = t1 ?? t2;
        GameObject host = new GameObject("BarricadeExamine");
        host.transform.SetParent(always, true);
        host.transform.position = f.W(0f, 0.5f);
        MakeExamine(host, "barricade", "EXAMINE", false, 0, HubExamine.Sound.None, f.W(0f, 1.0f) + Vector3.up * 0.9f, new Vector3(3.2f, 1.6f, 2.4f));
        EndVignette(v);
    }

    // ---------------------------------------------------------------- 4. reception / admissions (the entrance, Gatehouse side)

    private static void BuildVReception(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "Reception");
        Transform always = v.Pose("Always", 1, 5);
        DressRoomCtx r = VCtx(v, always, "Entrance");
        float yawR = Mathf.Round(Quaternion.LookRotation(hub.mainEntrance.position - (hub.mainEntrance.position + new Vector3(-3.0f, 0f, 3.2f))).eulerAngles.y / 90f) * 90f;
        Vector3? spot = FreeSpot(r, hub.mainEntrance.position + new Vector3(-3.0f, 0f, 3.2f), 7f, 0.8f, 0.55f, yawR, true);
        if (spot == null) { _pr.AppendLine("RECEPTION: no free spot"); EndVignette(v); return; }
        Vector3 c = spot.Value;
        _pr.AppendLine($"reception desk at {c:F1}");
        float yaw = yawR;
        Placed desk = Put(r, "TableWhite", c, yaw, 1f, null, true);
        if (desk == null) { EndVignette(v); return; }
        On(r, desk, "Lamp", -0.5f, 0f, 20f);
        Placed ledger = On(r, desk, "alch:Alch_BookPileA", 0.2f, 0.1f, 15f, 1.1f, 0f);
        On(r, desk, "Mug", 0.65f, -0.3f);
        Put(r, "ChairOffice", c + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0f, 0f, -1.1f), yaw + 170f);
        if (ledger != null)
        {
            ledger.go.name = "AdmissionsLedger";
            MakeExamine(ledger.go, "admissions", "READ THE LEDGER", false, 0, HubExamine.Sound.Paper, ledger.center + Vector3.up * 0.2f, new Vector3(1.2f, 0.9f, 1.2f));
        }
        EndVignette(v);
    }

    // ---------------------------------------------------------------- 5. the night-shift post (upper hall, by the Bedroom door)

    private static void BuildVNightPost(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "NightPost");
        Transform always = v.Pose("Always", 1, 5);
        DressRoomCtx r = VCtx(v, always, "HallUpstair");
        Vector3? spot = FreeSpot(r, new Vector3(80.5f, r.def.floorY, -61.5f), 4f, 0.8f, 0.5f, 0f, true);
        if (spot == null) { _pr.AppendLine("NIGHTPOST: no free spot by the Bedroom door"); EndVignette(v); return; }
        Vector3 c = spot.Value;
        _pr.AppendLine($"night post at {c:F1}");
        Placed table = Put(r, "TableSmall", c, 0f, 1f, null, true);
        if (table == null) { EndVignette(v); return; }
        Put(r, "ChairWhite", c + new Vector3(0f, 0f, -0.9f), 180f + Jit(r, 10f));
        On(r, table, "Lamp", -0.5f, 0f, 30f);
        On(r, table, "Mug", 0.45f, 0.1f);
        Placed rota = On(r, table, "Paper", 0.05f, 0f, 25f, 1.8f, 0f);
        if (rota != null)
        {
            rota.go.name = "Rota";
            MakeExamine(rota.go, "rota", "READ", true, -3, HubExamine.Sound.Paper, rota.center + Vector3.up * 0.1f, new Vector3(1.0f, 0.6f, 1.0f));
        }
        EndVignette(v);
    }

    // ---------------------------------------------------------------- 6. Room 4 (Weiss's door: ajar, a warm threshold, a dropped tray)

    private static void BuildVRoom4(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "Room4");
        RoomDef exam = Def("ExamRoom");
        Transform always = v.Pose("Always", 1, 5);
        DressRoomCtx r = VCtx(v, always, "ExamRoom");
        // the threshold: a thin warm glow across the doorway (the door is at x 89.3, its sill at the room's hall edge)
        Vector3 doorway = new Vector3(89.3f, exam.floorY, exam.maxZ - 0.55f);
        Material glow = EnsureThresholdMaterial();
        GameObject strip = new GameObject("ThresholdGlow");
        strip.transform.SetParent(always, true);
        strip.transform.SetPositionAndRotation(doorway + Vector3.up * 0.02f, Quaternion.Euler(90f, 0f, 0f));
        strip.transform.localScale = new Vector3(1.05f, 0.14f, 1f);
        strip.AddComponent<MeshFilter>().sharedMesh = EnsureStarQuads()[0];
        MeshRenderer mr = strip.AddComponent<MeshRenderer>();
        mr.sharedMaterial = glow; mr.shadowCastingMode = ShadowCastingMode.Off; mr.receiveShadows = false;
        GameObject host = new GameObject("Room4Examine");
        host.transform.SetParent(always, true);
        host.transform.position = doorway;
        MakeExamine(host, "room4", "EXAMINE", false, 0, HubExamine.Sound.None, doorway + Vector3.up * 1.0f + Vector3.back * 0.3f, new Vector3(1.4f, 2.0f, 1.2f));

        // the dropped ampoule tray - NOT on floor 4, where the real ampoules are the mission items
        foreach ((string name, int a, int b) in new[] { ("TrayEarly", 1, 3), ("TrayLate", 5, 5) })
        {
            Transform pose = v.Pose(name, a, b);
            DressRoomCtx rp = VCtx(v, pose, "HallUpstair");
            Vector3 p = new Vector3(doorway.x - 0.2f, rp.def.floorY, doorway.z + 0.9f + (a == 5 ? 0.5f : 0f));
            Put(rp, "m:instrument_tray", p, 30f + a * 11f, 1f, null, false);
            Put(rp, "alch:Alch_Flasks", p + new Vector3(0.45f, 0f, 0.1f), 70f, 1f, null, false);
            Put(rp, "m:glass_jar", p + new Vector3(-0.35f, 0f, 0.3f), 10f, 1f, null, false);
        }
        EndVignette(v);
    }

    private static Material EnsureThresholdMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(ThresholdMaterialPath);
        if (mat != null) return mat;
        mat = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { name = "HubThresholdGlow" };
        mat.SetColor("_BaseColor", new Color(1f, 0.66f, 0.3f, 1f));
        AssetDatabase.CreateAsset(mat, ThresholdMaterialPath);
        return mat;
    }

    // ---------------------------------------------------------------- 7. Elias's route (Bedroom to the west stair head)

    private static void BuildVEliasRoute(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "EliasRoute");
        Transform early = v.Pose("Scuffs", 1, 3);
        DressRoomCtx r = VCtx(v, early, "HallUpstair");
        float z = -55.6f;
        for (int i = 0; i < 4; i++) Decal(r, i % 2 == 0 ? "DragMarks" : "BloodSmear", new Vector3(80.5f + i * 2.6f, 0f, z + Jit(r, 0.3f)), 1.2f + 0.2f * i, 90f + Jit(r, 8f));
        Transform late = v.Pose("TakenDown", 4, 5);
        DressRoomCtx rl = VCtx(v, late, "HallUpstair");
        Vector3? spot = FreeSpot(rl, new Vector3(87.5f, rl.def.floorY, -55.4f), 3.5f, 1.2f, 0.5f, 0f, true);
        if (spot != null)
        {
            Placed bed = Put(rl, "MorgueWheelBed", spot.Value, 0f, 1f, null, true);
            if (bed != null) Sheet(On(rl, bed, Body(rl), 0f, 0f, bed.size.x > bed.size.z ? 0f : 90f, 1f, 0.02f));
            _pr.AppendLine($"covered gurney at {spot.Value:F1}");
        }
        else _pr.AppendLine("ELIAS: no spot for the covered gurney");
        for (int i = 0; i < 3; i++) Decal(rl, "DragMarks", new Vector3(80.5f + i * 3.4f, 0f, z + Jit(rl, 0.3f)), 1.4f, 90f);
        EndVignette(v);
    }

    // ---------------------------------------------------------------- 8. Stan's bay (floor 4, the far east end of the upper hall)

    private static void BuildVStanBay(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "StanBay");
        Transform pose = v.Pose("Floor4", 4, 4);
        DressRoomCtx r = VCtx(v, pose, "HallUpstair");
        Frame f = null;
        foreach ((float x, int side) in new[] { (116.5f, +1), (116.5f, -1), (119.5f, -1), (121f, +1) })
        {
            Frame cand = FindWallEither(r, new Vector3(x, 0f, -54.5f), Vector3.forward, side, 1.4f);
            if (cand == null) continue;
            Placed test = InFrame(r, cand, "SmallMetalicCase", -0.8f, 0.45f, 180f, 1f, true);
            if (test != null) { Object.DestroyImmediate(test.go); Physics.SyncTransforms(); f = cand; break; }
        }
        if (f == null) { _pr.AppendLine("STANBAY: no wall"); EndVignette(v); return; }
        _pr.AppendLine($"Stan's bay at {f.pos:F1}");
        Placed first = null;
        for (int i = 0; i < 3; i++)
        {
            Placed crate = InFrame(r, f, "SmallMetalicCase", -0.8f + i * 0.95f, 0.45f, 180f + Jit(r, 5f), 1f, true);
            if (i == 0) first = crate;
            if (crate != null) WallTextOnProp(pose, crate, f, "SERVICE UNIT");
        }
        if (first != null)
        {
            GameObject host = new GameObject("StanBayTag");
            host.transform.SetParent(pose, true);
            host.transform.position = first.center;
            MakeExamine(host, "stanbay", "READ", true, -4, HubExamine.Sound.Paper, first.center + f.Rot * Vector3.forward * 0.4f, new Vector3(3.0f, 1.2f, 1.4f));
        }
        EndVignette(v);
    }

    private static void WallTextOnProp(Transform parent, Placed prop, Frame f, string text)
    {
        Vector3 n = f.Rot * Vector3.forward;
        Vector3 p = new Vector3(prop.center.x, prop.center.y + 0.05f, prop.center.z) + n * (Mathf.Abs(n.x) * prop.size.x * 0.5f + Mathf.Abs(n.z) * prop.size.z * 0.5f + 0.005f);
        WallText(parent, "Stencil", text, p, n, 1.0f, new Color(0.05f, 0.05f, 0.05f), 0f, 0.9f);
    }

    // ---------------------------------------------------------------- 9. Tomas's corner (the basement hall by the machine room)

    private static void BuildVTomasCorner(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "TomasCorner");
        Vector3 tomas = AnchorOf(hub, "Tomas");
        DressRoomCtx probe = VCtx(v, v.root, "MachineRoom");
        Frame f = FindWallEither(probe, new Vector3(tomas.x, 0f, tomas.z), Vector3.right, +1, 0.7f) ?? FindWallEither(probe, new Vector3(tomas.x, 0f, tomas.z), Vector3.forward, +1, 0.7f);
        if (f == null) { _pr.AppendLine("TOMAS: no wall near him"); EndVignette(v); return; }
        _pr.AppendLine($"Tomas at {tomas:F1}, wall at {f.pos:F1}");
        Vector3 n = f.Rot * Vector3.forward;
        Transform always = v.Pose("Always", 1, 5);
        DressRoomCtx ra = VCtx(v, always, "MachineRoom");
        Placed stool = InFrame(ra, f, "Table_Bed", 1.3f, 0.6f, 20f, 1f, false);
        On(ra, stool, "CanOld", 0f, 0f);
        for (int i = 0; i < 3; i++) HangAt(ra, f, "blood:Handprint", -0.5f + i * 0.28f, 1.15f + 0.08f * i, i * 12f, 0.3f, true);
        Placed pipe = InFrame(ra, f, "Pipe", -1.3f, 0.3f, 0f, 1f, true);
        HubPatient tomasPatient = hub.life.GetComponentsInChildren<HubPatient>(true).FirstOrDefault(p => p.patient == HubState.Patient.Tomas);
        GameObject host = new GameObject("PipeExamine");
        host.transform.SetParent(always, true);
        Vector3 pc = pipe != null ? pipe.center : f.W(-1.3f, 0.4f);
        host.transform.position = pc;
        MakeExamine(host, "pipe", "LISTEN", false, 0, HubExamine.Sound.Knock, pc + n * 0.5f, new Vector3(1.4f, 2.4f, 1.4f), tomasPatient);

        Transform twos = v.Pose("TalliesTwos", 1, 3);
        WallText(twos, "Tallies", "||  ||  ||  ||", f.W(0.9f, 0f) + Vector3.up * 1.0f, n, 2.0f, ChalkWhite, 3f, 1.8f);
        Transform threes = v.Pose("TalliesThrees", 4, 5);
        WallText(threes, "Tallies", "|||  |||  |||  |||", f.W(0.9f, 0f) + Vector3.up * 1.0f, n, 2.0f, ChalkWhite, -4f, 1.9f);
        WallText(threes, "TalliesScratch", "////  ////", f.W(0.7f, 0f) + Vector3.up * 0.55f, n, 2.0f, new Color(0.7f, 0.7f, 0.66f, 0.9f), 7f, 1.6f);
        EndVignette(v);
    }

    // ---------------------------------------------------------------- 10. Marta's chalk (a house with more windows than the real one)

    private static void BuildVMartaChalk(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "MartaChalk");
        Vector3 box = AnchorOf(hub, "MusicBox");
        DressRoomCtx probe = VCtx(v, v.root, "HallUnderground");
        Frame f = FindWallEither(probe, new Vector3(box.x, 0f, box.z + 1.2f), Vector3.right, -1, 0.6f) ?? FindWallEither(probe, new Vector3(box.x, 0f, box.z + 1.2f), Vector3.right, +1, 0.6f);
        if (f == null) { _pr.AppendLine("MARTA: no wall near the music box"); EndVignette(v); return; }
        _pr.AppendLine($"chalk house wall at {f.pos:F1}");
        Vector3 n = f.Rot * Vector3.forward;
        Vector3 p = f.pos + Vector3.up * 0.55f;
        Material house = EnsureChalkHouseMaterial();
        Mesh quad = EnsureStarQuads()[0];
        Mesh full = EnsureFullQuad();
        Transform drawn = v.Pose("Drawn", 1, 3);
        WallQuad(drawn, "ChalkHouse", full, house, p, n, 0.85f, 2f);
        Transform smeared = v.Pose("Smeared", 4, 5);
        WallQuad(smeared, "ChalkHouse", full, house, p, n, 0.85f, 2f);
        WallQuad(smeared, "Smear", quad, EnsureWallPaintMaterial(), p + Vector3.up * 0.05f, n, 0.95f, 40f, 1.25f, 0.035f);
        EndVignette(v);
    }

    private static Mesh EnsureFullQuad()
    {
        string path = StarMeshFolder + "/FullQuad.asset";
        Mesh m = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (m != null) return m;
        EnsureFolderPath(StarMeshFolder);
        m = new Mesh { name = "FullQuad" };
        m.vertices = new[] { new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f), new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f) };
        m.normals = new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back };
        m.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) };
        m.triangles = new[] { 0, 2, 1, 2, 3, 1 };
        m.RecalculateBounds();
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    /// <summary>A child's chalk drawing of a house: a body, a roof, a door and far too many windows, drawn into a PNG once.</summary>
    private static Material EnsureChalkHouseMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(ChalkHouseMaterialPath);
        if (mat != null) return mat;
        const int size = 256;
        Color32[] px = new Color32[size * size];
        System.Random rng = new System.Random(4401);
        List<(Vector2, Vector2)> segs = new List<(Vector2, Vector2)>();
        void Box(float x0, float y0, float x1, float y1) { segs.Add((new Vector2(x0, y0), new Vector2(x1, y0))); segs.Add((new Vector2(x1, y0), new Vector2(x1, y1))); segs.Add((new Vector2(x1, y1), new Vector2(x0, y1))); segs.Add((new Vector2(x0, y1), new Vector2(x0, y0))); }
        Box(50, 40, 206, 150);
        segs.Add((new Vector2(44, 150), new Vector2(128, 218))); segs.Add((new Vector2(128, 218), new Vector2(212, 150)));
        Box(112, 40, 144, 96);
        for (int i = 0; i < 6; i++) Box(62 + (i % 3) * 46, 104 + (i / 3) * 24 - (i / 3) * 0, 90 + (i % 3) * 46, 128 + (i / 3) * 0);
        Box(66, 60, 96, 88); Box(160, 60, 190, 88);
        float seed = (float)rng.NextDouble() * 100f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = float.MaxValue; Vector2 p = new Vector2(x, y);
            foreach (var (a, b) in segs) { Vector2 ab = b - a; float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude); d = Mathf.Min(d, Vector2.Distance(p, a + ab * t)); }
            float w = 3.2f * (0.8f + 0.4f * Mathf.PerlinNoise(x * 0.08f + seed, y * 0.08f));
            float edge = 1f - Mathf.Clamp01((d - w * 0.5f) / 1.6f);
            if (edge <= 0f) continue;
            float grain = 0.5f + 0.5f * Mathf.PerlinNoise(x * 0.6f + seed, y * 0.6f);
            px[y * size + x] = new Color32(255, 255, 255, (byte)(Mathf.Clamp01(edge * grain) * 255f));
        }
        EnsureFolderPath("Assets/SourceFiles/Textures/Decals");
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        tex.SetPixels32(px); tex.Apply();
        System.IO.File.WriteAllBytes(System.IO.Path.GetFullPath(ChalkHousePath), tex.EncodeToPNG());
        Object.DestroyImmediate(tex);
        AssetDatabase.ImportAsset(ChalkHousePath);
        TextureImporter imp = (TextureImporter)AssetImporter.GetAtPath(ChalkHousePath);
        imp.alphaIsTransparency = true; imp.wrapMode = TextureWrapMode.Clamp; imp.maxTextureSize = 256; imp.mipmapEnabled = true;
        imp.SaveAndReimport();
        Texture2D tx = AssetDatabase.LoadAssetAtPath<Texture2D>(ChalkHousePath);
        Material source = EnsureStarMaterial(EnsureStarTexture());
        mat = new Material(source) { name = "HubChalkHouse" };
        mat.SetTexture("_BaseMap", tx); mat.SetTexture("_EmissionMap", tx);
        mat.SetColor("_BaseColor", new Color(0.92f, 0.92f, 0.86f, 0.85f));
        mat.SetColor("_EmissionColor", new Color(0.12f, 0.15f, 0.13f));
        AssetDatabase.CreateAsset(mat, ChalkHouseMaterialPath);
        return mat;
    }

    // ---------------------------------------------------------------- 11. "Released to lower levels" (beside the basement stairs, off the chase lane)

    private static void BuildVLowerLevel(AsylumHub hub)
    {
        VignetteBuild v = StartVignette(hub, "LowerLevel");
        DressRoomCtx probe = VCtx(v, v.root, "HallUnderground");
        Frame f = FindWallEither(probe, new Vector3(121f, 0f, -67.5f), Vector3.right, +1, 0.9f) ?? FindWallEither(probe, new Vector3(121f, 0f, -64.5f), Vector3.right, +1, 0.9f);
        if (f == null) { _pr.AppendLine("LOWERLEVEL: no wall"); EndVignette(v); return; }
        _pr.AppendLine($"lower-level door wall at {f.pos:F1}");
        Vector3 n = f.Rot * Vector3.forward;
        foreach ((string name, int a, int b, float ajar) in new[] { ("Shut", 1, 3, 0f), ("Ajar", 4, 5, 24f) })
        {
            Transform pose = v.Pose(name, a, b);
            DressRoomCtx r = VCtx(v, pose, "HallUnderground");
            Placed door = InFrame(r, f, "Door_V3", 0f, 0.15f, 90f, 1f, true, false);
            if (door == null) continue;
            if (ajar > 0f)
            {
                Bounds b0 = RendBounds(door.go);
                Vector3 hinge = b0.center - f.Rot * Vector3.right * (b0.extents.x) + Vector3.zero;
                hinge.y = b0.center.y;
                door.go.transform.RotateAround(hinge, Vector3.up, ajar);
            }
            else
            {
                GameObject host = new GameObject("LowerLevelExamine");
                host.transform.SetParent(pose, true);
                host.transform.position = door.center;
                MakeExamine(host, "lower", "READ", true, -6, HubExamine.Sound.Paper, door.center + n * 0.5f, new Vector3(1.8f, 2.4f, 1.4f));
            }
            WallText(pose, "Stencil", "SUBJECT A\nLOWER LEVEL", f.W(0f, 0f) + Vector3.up * 2.45f, n, 1.6f, new Color(0.7f, 0.12f, 0.1f), 0f, 1.6f);
        }
        EndVignette(v);
    }
}
