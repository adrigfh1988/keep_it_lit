using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F101 phase A: "Patient 7's wall". The dead space behind the entrance statue becomes where it all started: a chalk-star mural on the north
/// window wall (some stars painted over in grey), a nest on the floor that peeks past the statue from the front door, a half ring of
/// chairs facing the statue's back, a partial ring of furniture round the marble bar, one star on the back of the plinth, a readable
/// admission card (-5) and, from floor 4, an empty wheelchair that turns. One fresh star more per floor lives in the Bay vignette
/// (Life/Vignettes/Bay). Measured from the statue's and the marble bar's renderer bounds - never from the plan's coordinates.
/// </summary>
public static partial class AsylumHubBuilder
{
    private static void BuildBay(AsylumHub hub)
    {
        PropsPiece piece = PropsPieces().First(p => p.name == "Bay");
        DevPropsResetQuiet(hub, piece);

        Mesh[] quads = EnsureStarQuads();
        Material chalk = EnsureStarMaterial(EnsureStarTexture());
        Material fresh = EnsureStarFreshMaterial();
        Material paint = EnsureWallPaintMaterial();

        Vector3 entrance = hub.mainEntrance.position;
        Vector3 axisPoint = entrance + hub.mainEntrance.forward * 14.3f;
        Transform statueT = FindBuildingNear("Statue", axisPoint);
        Transform ledgeT = FindBuildingNear("Pillar", axisPoint + hub.mainEntrance.forward * 7f);
        if (statueT == null || ledgeT == null) { _pr.AppendLine("BAY: the statue or the marble bar was not found - nothing placed"); return; }
        Bounds sb = RendBounds(statueT.gameObject), lb = RendBounds(ledgeT.gameObject);
        RoomDef def = Def("HallDownstair");
        float floorY = def.floorY;
        _pr.AppendLine($"bay: statue {sb.center:F2} size {sb.size:F2}; marble bar {lb.center:F2} size {lb.size:F2}");

        DressRoomCtx r = NewDressCtx(hub, def, piece.Prefix);
        Vector3 forward = hub.mainEntrance.forward; forward.y = 0f; forward.Normalize();

        // ---- the north wall (the window wall) and the windows on it
        Frame wall = WallFrameFrom(new Vector3(lb.center.x, floorY + 1.5f, lb.max.z + 0.6f), Vector3.forward);
        if (wall == null) { _pr.AppendLine("BAY: no window wall found behind the marble bar"); return; }
        float wallZ = wall.pos.z;
        List<Bounds> windows = new List<Bounds>();
        foreach (Transform t in _building.GetComponentsInChildren<Transform>(true))
        {
            if (!t.name.StartsWith("WindowHall")) continue;
            Bounds wb = RendBounds(t.gameObject);
            if (Mathf.Abs(wb.center.z - wallZ) < 0.6f && Mathf.Abs(wb.center.x - lb.center.x) < 9f && Mathf.Abs(wb.min.y - (floorY + 1f)) < 0.8f) windows.Add(wb);
        }
        windows = windows.OrderBy(w => w.center.x).ToList();
        float x0 = windows.Count > 0 ? windows.First().min.x - 2.2f : lb.min.x - 2f;
        float x1 = windows.Count > 0 ? windows.Last().max.x + 2.2f : lb.max.x + 2f;
        _pr.AppendLine($"bay: window wall z {wallZ:F2}, {windows.Count} windows, mural x {x0:F1}..{x1:F1}");

        // ---- 1. the mural
        Transform muralGroup = Group(r.group, "Mural");
        List<(Vector3 p, float s)> stars = new List<(Vector3 p, float s)>();
        int made = 0, painted = 0;
        Vector3 wallN = Vector3.back; // into the bay (the wall faces south)
        bool TryStar(float x, float h, float size, bool force, out Vector3 point)
        {
            point = default;
            Vector3 origin = new Vector3(x, floorY + h, wallZ - 3.5f);
            if (!FlatWallAt(origin, Vector3.forward, size * 0.4f, size * 0.4f, out Vector3 pt, out Vector3 n, out string why, 6f)) { r.rejects.Add($"star at x {x:F1} h {h:F1}: {why}"); return false; }
            point = pt;
            if (!force) foreach (var s in stars) if ((s.p - pt).magnitude < 0.3f * (s.s + size)) { r.rejects.Add($"star at x {x:F1} h {h:F1}: crowded"); return false; }
            return true;
        }
        void Star(Vector3 point, float size, int variant, float roll, bool paintOver, Material mat, string name)
        {
            WallQuad(muralGroup, name, quads[variant], mat, point, wallN, size, roll);
            stars.Add((point, size));
            made++;
            if (paintOver && mat == chalk)
            {
                Vector3 shift = new Vector3(Jit(r, 0.12f), Jit(r, 0.1f), 0f);
                Material m = paint;
                WallQuad(muralGroup, name + "_Paint", quads[0], m, point + shift, wallN, size * (1.35f + (float)r.rng.NextDouble() * 0.25f), r.rng.Next(360), 1.1f, 0.035f);
                painted++;
            }
        }

        // the two big ones in the gaps between the windows (a gap is the wall between two window bounds)
        int bigMade = 0;
        for (int w = 0; w + 1 < windows.Count; w++)
        {
            float gapMin = windows[w].max.x, gapMax = windows[w + 1].min.x;
            float cx = (gapMin + gapMax) * 0.5f, size = Mathf.Min(1.5f, (gapMax - gapMin) - 0.35f);
            if (size < 0.7f) continue;
            if (TryStar(cx, 1.7f, size, true, out Vector3 pt)) { Star(pt, size, w % 4, w == 0 ? -8f : 11f, false, chalk, "MuralBig" + w); bigMade++; }
        }
        // the rest: scattered over the wall, windows excluded by the flat-wall test
        int attempts = 0;
        while (made - bigMade < 20 && attempts++ < 400)
        {
            float x = x0 + Mathf.Pow((float)r.rng.NextDouble(), 0.75f) * (x1 - x0);
            float h = 0.3f + (float)r.rng.NextDouble() * 2.75f;
            float size = 0.55f + (float)r.rng.NextDouble() * 0.75f;
            if (!TryStar(x, h, size, false, out Vector3 pt)) continue;
            Star(pt, size, r.rng.Next(4), Jit(r, 28f), made % 3 == 0, chalk, "Mural" + made);
        }
        _pr.AppendLine($"bay: mural {made} stars ({painted} painted over), {bigMade} big");

        // ---- 2. a big star on the marble bar's face towards the entrance
        if (FlatWallAt(new Vector3(lb.center.x, floorY + 0.55f, lb.min.z - 3f), Vector3.forward, 0.4f, 0.35f, out Vector3 barPt, out Vector3 barN, out string barWhy, 4f))
        {
            WallQuad(r.group, "BarStar", quads[2], chalk, barPt, barN, 0.95f, 6f);
            made++;
        }
        else r.rejects.Add("bar star: " + barWhy);

        // ---- 3. the discovery star on the back of the plinth (seen when rounding the statue to either stair)
        if (FlatWallAt(new Vector3(sb.center.x + 0.6f, floorY + 0.8f, sb.max.z + 2.2f), Vector3.back, 0.05f, 0.05f, out Vector3 plinthPt, out Vector3 plinthN, out string plinthWhy, 4f))
        {
            WallQuad(r.group, "PlinthStar", quads[1], chalk, plinthPt, plinthN, 0.5f, -14f);
            made++;
        }
        else r.rejects.Add("plinth star: " + plinthWhy);

        // ---- 4. the nest: a spot east of the marble bar from which the front door sees it past the statue's side
        Vector3 eye = new Vector3(entrance.x, floorY + 1.6f, entrance.z);
        Vector3 nestSpot = default; float nestYaw = 0f; bool nestVisible = false, nestFound = false;
        float bestScore = float.MaxValue;
        for (float x = lb.max.x - 1.0f; x <= lb.max.x + 5.5f; x += 0.25f)
        {
            for (float z = lb.min.z - 2.4f; z <= lb.max.z + 1.2f; z += 0.25f)
            {
                foreach (float yaw in new[] { 0f, 90f })
                {
                    Quaternion q = Quaternion.Euler(0f, yaw, 0f);
                    Vector3 half = new Vector3(0.62f, 0.3f, 1.2f);
                    Vector3 centre = new Vector3(x, floorY + 0.5f, z);
                    if (Physics.CheckBox(centre, half, q, ~0, QueryTriggerInteraction.Ignore)) continue;
                    if (KeepOutHit(centre, half, yaw, floorY, 0.85f)) continue;
                    // seated on the floor over its whole footprint
                    bool floorOk = true;
                    foreach (Vector3 c in new[] { new Vector3(-0.4f, 0, -1f), new Vector3(0.4f, 0, -1f), new Vector3(-0.4f, 0, 1f), new Vector3(0.4f, 0, 1f) })
                    {
                        Vector3 pc = centre + q * c;
                        if (!Physics.Raycast(new Vector3(pc.x, floorY + 0.6f, pc.z), Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore)) floorOk = false;
                    }
                    if (!floorOk) continue;
                    bool visible = !Physics.Linecast(eye, new Vector3(x, floorY + 0.45f, z), ~0, QueryTriggerInteraction.Ignore);
                    // nearest to the bar's east end, in front of it, wins; visible spots beat hidden ones
                    float score = (visible ? 0f : 100f) + Vector2.Distance(new Vector2(x, z), new Vector2(lb.max.x + 1.3f, lb.min.z - 0.9f));
                    if (score < bestScore) { bestScore = score; nestSpot = centre; nestYaw = yaw; nestVisible = visible; nestFound = true; }
                }
            }
        }
        Placed mattress = null;
        if (nestFound)
        {
            mattress = Put(r, "Mattress", new Vector3(nestSpot.x, floorY, nestSpot.z), nestYaw, 1f, null, true);
            _pr.AppendLine($"bay: nest at {nestSpot:F2} yaw {nestYaw}, seen from the front door: {(nestVisible ? "YES (line of sight clear)" : "NO (hidden)")}");
        }
        else _pr.AppendLine("BAY: no free spot for the nest");
        if (mattress != null)
        {
            On(r, mattress, "BedFeather", 0.05f, 0.45f, 12f, 0.55f, 0.01f);
            On(r, mattress, "Pillow", 0f, -0.78f, 6f, 1f, 0.01f);
            Vector3 right = Quaternion.Euler(0f, mattress.yaw, 0f) * Vector3.right, fwdM = Quaternion.Euler(0f, mattress.yaw, 0f) * Vector3.forward;
            Material glow = EnsureCandleGlowMaterial();
            Vector3 c1 = mattress.center + right * 0.85f + fwdM * 0.85f, c2 = mattress.center - right * 0.8f - fwdM * 0.35f, c3 = mattress.center + right * 0.7f - fwdM * 0.9f;
            foreach (Vector3 cp in new[] { c1, c2 })
            {
                Placed cand = Put(r, "alch:Alch_Candles", cp, r.rng.Next(360), 2.2f, null, false);
                if (cand != null) foreach (Renderer rr in cand.go.GetComponentsInChildren<Renderer>(true)) rr.sharedMaterials = Enumerable.Repeat(glow, rr.sharedMaterials.Length).ToArray();
            }
            Put(r, "Mug", c3, r.rng.Next(360), 1f, null, false);
        }

        // ---- 5. the congregation: chairs in a half ring facing the statue's back
        Vector3 back = new Vector3(sb.center.x, floorY, sb.max.z + 0.25f);
        float radius = 3.1f;
        int chairs = 0; List<Placed> chairPieces = new List<Placed>();
        foreach (float a in new[] { 28f, 52f, 76f, 104f, 128f, 152f })
        {
            Vector3 pos = back + new Vector3(Mathf.Cos(a * Mathf.Deg2Rad), 0f, Mathf.Sin(a * Mathf.Deg2Rad)) * (radius + Jit(r, 0.18f));
            Vector3 to = back - pos; to.y = 0f;
            Placed c = Put(r, "ChairWhite", pos, Quaternion.LookRotation(to).eulerAngles.y + Jit(r, 10f));
            if (c != null) { chairs++; chairPieces.Add(c); }
        }
        if (chairPieces.Count > 3) { Topple(r, chairPieces[2]); Topple(r, chairPieces[chairPieces.Count - 2], -1f); }
        _pr.AppendLine($"bay: {chairs} chairs in the half ring");

        // ---- 6. the partial ring round the marble bar: a bed stood on end, a cupboard and two wheelchairs, a gap towards the west stair foot
        Vector3 barC = new Vector3(lb.center.x, floorY, lb.center.z);
        Placed bed = PutFirst(r, "Bed", 90f, barC, new[] { new Vector2(-5.4f, 0.4f), new Vector2(-5.8f, -1.6f), new Vector2(-4.9f, 2.0f), new Vector2(-6.2f, 1.2f), new Vector2(-5.2f, -3.0f) });
        if (bed != null) StandOnEnd(r, bed);
        Placed cupboard = PutFirst(r, "Cupboard", -90f, barC, new[] { new Vector2(5.4f, 1.2f), new Vector2(5.8f, -0.4f), new Vector2(5.0f, 2.6f), new Vector2(6.2f, 2.0f), new Vector2(5.6f, 3.4f) });
        if (cupboard == null) PutFirst(r, "Cupboard", 180f, barC, new[] { new Vector2(-3.3f, 3.2f), new Vector2(3.3f, 3.2f), new Vector2(-4.4f, 3.2f), new Vector2(4.4f, 3.2f) });
        PutFirst(r, "WheelChair", 150f, barC, new[] { new Vector2(-2.6f, -2.2f), new Vector2(-3.4f, -1.2f), new Vector2(-3.0f, 0.9f), new Vector2(-4.2f, -2.0f) });
        PutFirst(r, "WheelChair", -160f, barC, new[] { new Vector2(2.7f, -2.0f), new Vector2(3.6f, -1.0f), new Vector2(3.2f, 1.2f), new Vector2(4.3f, -2.3f) });

        // ---- the grime where the congregation stood
        Decal(r, "BloodSmear", back + new Vector3(-1.2f, 0f, 1.5f), 1.5f, 70f);

        int dressed = PropsFinishCtx(r, piece);
        _pr.AppendLine($"bay: {dressed} Dressing children, {r.placed} placed, {r.rejected} rejected");

        // ======== the Life side: Life/Vignettes/Bay with its poses
        Transform vig = VignetteRoot(hub);
        Transform bay = Group(vig, piece.name);
        HubVignette hv = bay.gameObject.AddComponent<HubVignette>();
        List<(int, int, GameObject)> poses = new List<(int, int, GameObject)>();

        // always: the admission card on the mattress
        Transform always = NewPose(bay, "Always");
        DressRoomCtx ra = PoseCtx(always, "HallDownstair", "F101_BayLife_");
        if (mattress != null)
        {
            Placed card = On(ra, mattress, "Paper", -0.42f, -0.12f, 30f, 2.0f, 0.0f);
            if (card != null)
            {
                card.go.name = "AdmissionCard";
                MakeExamine(card.go, "bay.card", "READ", true, HubVignetteText.BayCardId, HubExamine.Sound.Paper, card.center + Vector3.up * 0.12f, new Vector3(0.8f, 0.5f, 0.8f));
            }
        }
        poses.Add((1, 5, always.gameObject));

        // one fresh star more per floor: floor n shows n of them, in the gaps the mural left
        List<Vector3> freshSpots = new List<Vector3>();
        int tries = 0;
        System.Random fr = new System.Random(StableHash("F101_BayFresh"));
        while (freshSpots.Count < 5 && tries++ < 200)
        {
            float x = x0 + 1.2f + (float)fr.NextDouble() * (x1 - x0 - 2.4f);
            float h = 0.6f + (float)fr.NextDouble() * 2.0f;
            Vector3 origin = new Vector3(x, floorY + h, wallZ - 3.5f);
            if (!FlatWallAt(origin, Vector3.forward, 0.22f, 0.22f, out Vector3 pt, out Vector3 n, out string why, 6f)) continue;
            if (freshSpots.Any(s => (s - pt).magnitude < 1.1f)) continue;
            freshSpots.Add(pt);
        }
        for (int i = 0; i < 5; i++)
        {
            Transform pose = NewPose(bay, "Fresh" + (i + 1));
            if (i < freshSpots.Count) WallQuad(pose, "FreshStar", quads[(i + 1) % 4], fresh, freshSpots[i], wallN, 0.5f + 0.05f * i, (i * 37) % 40 - 20f, 1f, 0.04f);
            ClearStatic(pose);
            poses.Add((i + 1, 5, pose.gameObject));
        }
        _pr.AppendLine($"bay: {freshSpots.Count} fresh-star spots");

        // floor 4+: the empty wheelchair that turns
        Transform scare = NewPose(bay, "Scare");
        DressRoomCtx rs = PoseCtx(scare, "HallDownstair", "F101_BayScare_");
        Placed chair = Put(rs, "WheelChair", new Vector3(barC.x + 0.7f, floorY, lb.max.z + 1.0f), 0f, 1f, null, true);
        if (chair == null) chair = Put(rs, "WheelChair", new Vector3(barC.x - 0.9f, floorY, lb.max.z + 1.1f), 0f, 1f, null, true);
        if (chair != null)
        {
            chair.go.name = "ScareChair";
            HubCreakScare cs = scare.gameObject.AddComponent<HubCreakScare>();
            SerializedObject so = new SerializedObject(cs);
            so.FindProperty("chair").objectReferenceValue = chair.go.transform;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else _pr.AppendLine("BAY: no room for the wheelchair behind the marble bar");
        poses.Add((4, 5, scare.gameObject));

        foreach (Transform t in bay) { ClearStatic(t); TrimShadows(t); }
        WirePoses(hv, poses);
        foreach (var p in poses) p.Item3.SetActive(true); // stay solid while later pieces are placed; PropsFinish / PropsEndPoses put them away
        Physics.SyncTransforms();
    }

    /// <summary>Turns a bed (or anything long) up onto its short end, leaning where it stood; reseated on the floor.</summary>
    private static void StandOnEnd(DressRoomCtx r, Placed p)
    {
        if (p == null) return;
        Vector3 axis = p.size.x > p.size.z ? Quaternion.Euler(0f, p.yaw, 0f) * Vector3.forward : Quaternion.Euler(0f, p.yaw, 0f) * Vector3.right;
        p.go.transform.RotateAround(p.center, axis, 90f);
        Bounds b = RendBounds(p.go);
        p.go.transform.position += Vector3.up * (r.def.floorY + 0.01f - b.min.y);
        b = RendBounds(p.go);
        p.center = b.center; p.size = b.size;
        Transform col = p.go.transform.Find("Collider");
        if (col != null) Object.DestroyImmediate(col.gameObject);
        GameObject c = new GameObject("Collider");
        c.transform.SetParent(p.go.transform, true);
        c.transform.position = b.center; c.transform.rotation = Quaternion.identity;
        c.AddComponent<BoxCollider>().size = b.size;
        Physics.SyncTransforms();
    }

    /// <summary>Removes what a half-finished earlier run of `piece` left (its Life group and its F101_ children), so the build is repeatable.</summary>
    private static void DevPropsResetQuiet(AsylumHub hub, PropsPiece piece)
    {
        Transform vig = hub.life != null ? hub.life.Find(VignettesName) : null;
        Transform t = vig != null ? vig.Find(piece.name) : null;
        if (t != null) Object.DestroyImmediate(t.gameObject);
        if (hub.dressing == null) return;
        foreach (Transform room in hub.dressing)
        {
            for (int i = room.childCount - 1; i >= 0; i--)
            {
                if (room.GetChild(i).name.StartsWith(piece.Prefix)) Object.DestroyImmediate(room.GetChild(i).gameObject);
            }
        }
        Physics.SyncTransforms();
    }
}

public static partial class AsylumHubBuilder
{
    /// <summary>Tries each offset (x, z from `origin`) in turn and keeps the first placement that fits.</summary>
    private static Placed PutFirst(DressRoomCtx r, string key, float yaw, Vector3 origin, IEnumerable<Vector2> offsets, float scale = 1f)
    {
        foreach (Vector2 o in offsets)
        {
            Placed p = Put(r, key, new Vector3(origin.x + o.x, r.def.floorY, origin.z + o.y), yaw + Jit(r, 8f), scale, null, true);
            if (p != null) return p;
        }
        return null;
    }
}

public static partial class AsylumHubBuilder
{
    private const string CandleGlowPath = "Assets/SourceFiles/Materials/Asylum/HubCandleGlow.mat";

    /// <summary>The pack's candle material with a warm emission of its own (no Light): one shared asset, so a few lit candles can be seen across the dark hall.</summary>
    private static Material EnsureCandleGlowMaterial()
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(CandleGlowPath);
        if (mat != null) return mat;
        GameObject candles = AssetDatabase.LoadAssetAtPath<GameObject>(CommonDir + "Alchemist/Alch_Candles.prefab");
        Material source = null;
        if (candles != null) foreach (Renderer rr in candles.GetComponentsInChildren<Renderer>(true)) { if (rr.sharedMaterial != null) { source = rr.sharedMaterial; break; } }
        mat = source != null ? new Material(source) : new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.name = "HubCandleGlow";
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(0.95f, 0.5f, 0.16f));
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        AssetDatabase.CreateAsset(mat, CandleGlowPath);
        return mat;
    }
}
