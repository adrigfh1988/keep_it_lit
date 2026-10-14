using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F115 (G10), part 2: the furnishing, the missing check and the build entry. See <c>AsylumHubBuilder.DirectorSuite.cs</c> for the wall.
/// Everything is positioned in world metres (the floor is y 8.82) and is idempotent: the old dressing and anything of F115's own is removed first.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const float DeskCx = 108.9f, DeskCz = -58.6f;
    private const float LockerMinFromDirector = 3.5f;
    private static readonly Vector3 DeskScale = new Vector3(1.25f, 1f, 1.2f); // wider and deeper, not taller (it is 0.80 m high)
    private static bool _suiteChanged;

    private static Transform PackProp(AsylumHub hub, string room, string name) => hub.building.Find("2ndFloor/" + room + "/" + name);

    private static Transform SuiteDesk(AsylumHub hub) => PackProp(hub, "Office", "TableOffice (1)");

    private static Bounds SuiteBounds(Transform t)
    {
        bool was = t.gameObject.activeSelf;
        if (!was) t.gameObject.SetActive(true);
        Bounds b = RendBounds(t.gameObject);
        if (!was) t.gameObject.SetActive(false);
        return b;
    }

    /// <summary>Where the Director stands: behind his desk (north of it), west of the chair, half a metre off the desk's back edge.</summary>
    internal static Vector3 DirectorSpotFor(Bounds desk, float floorY) => new Vector3(desk.center.x - 0.65f, floorY, desk.max.z + 0.5f);

    /// <summary>The finale arrival: on the visitor side, 2.5 m from the desk's front edge (the desk is to the north; yaw 0 faces it).</summary>
    internal static Vector3 DeskPointFor(Bounds desk, float floorY) => new Vector3(desk.center.x, floorY + 0.05f, desk.min.z - 2.5f);

    /// <summary>Moves `t` (rotating about its bounds centre to the absolute yaw) so its bounds centre sits at (cx, cz); the height is left alone.</summary>
    private static void PlaceAt(Transform t, float cx, float cz, float yaw)
    {
        if (t == null) return;
        Bounds b = SuiteBounds(t);
        float turn = Mathf.DeltaAngle(t.eulerAngles.y, yaw);
        if (Mathf.Abs(turn) > 0.01f) t.RotateAround(b.center, Vector3.up, turn);
        b = SuiteBounds(t);
        t.position += new Vector3(cx - b.center.x, 0f, cz - b.center.z);
        PrefabUtility.RecordPrefabInstancePropertyModifications(t);
    }

    private static void HideProp(Transform t)
    {
        if (t == null) return;
        t.gameObject.SetActive(false);
        PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);
    }

    /// <summary>Deletes scene objects; a child of the hub prefab asset goes through the asset (an instance may not delete its prefab's children).</summary>
    private static void RemoveFromHub(AsylumHub hub, IEnumerable<Transform> targets)
    {
        List<string> paths = new List<string>();
        foreach (Transform t in targets.ToList())
        {
            if (t == null) continue;
            if (!PrefabUtility.IsPartOfPrefabInstance(t.gameObject) || PrefabUtility.IsAddedGameObjectOverride(t.gameObject)) { Object.DestroyImmediate(t.gameObject); continue; }
            paths.Add(AnimationUtility.CalculateTransformPath(t, hub.transform));
        }
        if (paths.Count == 0) return;
        EditHubPrefab(root =>
        {
            foreach (string p in paths)
            {
                Transform t = root.transform.Find(p);
                if (t != null) Object.DestroyImmediate(t.gameObject);
            }
        });
    }

    // ---------------------------------------------------------------- missing check

    private static List<string> G10Missing(AsylumHub hub)
    {
        List<string> missing = new List<string>();
        if (hub.life == null || hub.building == null) return missing;
        if (!SuiteShellDone(hub)) missing.Add("F115 wall opening (Building/2ndFloor/" + PartitionName + ")");
        Transform office = hub.dressing != null ? hub.dressing.Find("Office") : null;
        if (office == null || office.Find(SuiteMarker) == null) missing.Add("Dressing/Office/" + SuiteMarker);
        else if (office.GetComponentsInChildren<Transform>(true).Any(t => t.name.StartsWith("FilmProjector"))) missing.Add("old office dressing still in Dressing/Office");

        Transform deskT = SuiteDesk(hub);
        if (deskT == null) return missing; // not this hub's office: nothing to compare against
        Bounds desk = SuiteBounds(deskT);
        Transform finale = hub.life.Find("Finale");
        Transform director = finale != null ? finale.Find("Director") : null;
        if (director != null)
        {
            Animator body = director.GetComponentInChildren<Animator>(true);
            if (body != null)
            {
                Vector3 p = body.transform.position, want = DirectorSpotFor(desk, FloorLevel);
                if (Vector3.Distance(new Vector3(p.x, 0f, p.z), new Vector3(want.x, 0f, want.z)) > 1f) missing.Add("Life/Finale/Director is not behind the desk");
            }
        }
        Transform dp = hub.points != null ? hub.points.Find("Director_Desk") : null;
        if (dp != null)
        {
            Vector3 want = DeskPointFor(desk, FloorLevel);
            if (Vector3.Distance(new Vector3(dp.position.x, 0f, dp.position.z), new Vector3(want.x, 0f, want.z)) > 0.6f) missing.Add("points/Director_Desk is not in front of the desk");
        }
        Transform fill = hub.lights != null ? hub.lights.Find("Fill_Office") : null;
        if (fill != null && Vector2.Distance(new Vector2(fill.position.x, fill.position.z), new Vector2(desk.center.x, desk.center.z)) > 2.5f) missing.Add("Lights/Fill_Office is not over the desk");
        Transform locker = hub.life.Find("Lockers/Locker_Upper");
        if (locker != null && locker.position.x > 103.3f && locker.position.x < 117.6f && locker.position.z < -57.0f && locker.position.z > -63.8f && Mathf.Abs(locker.position.y - FloorLevel) < 1f)
            missing.Add("Life/Lockers/Locker_Upper is still inside the office");
        else if (locker != null)
        {
            Vector3 dir = DirectorSpotFor(desk, FloorLevel);
            if (Vector2.Distance(new Vector2(locker.position.x, locker.position.z), new Vector2(dir.x, dir.z)) < LockerMinFromDirector) missing.Add("Life/Lockers/Locker_Upper is within " + LockerMinFromDirector + " m of the Director");
        }
        return missing;
    }

    private static void AddG10(AsylumHub hub, StringBuilder report)
    {
        if (hub.life == null || G10Missing(hub).Count == 0) return;
        _building = hub.building;
        Physics.SyncTransforms();
        if (!SuiteShellDone(hub)) report.AppendLine(OpenSuiteWall(hub));
        Physics.SyncTransforms();
        report.AppendLine(SuiteFurnish(hub));
        _suiteChanged = true;
    }

    /// <summary>Dev: re-run just the furnishing (idempotent), for iterating on the layout. Not applied to the prefab.</summary>
    public static string DevSuiteFurnish()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return "no hub";
        _building = hub.building;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        return SuiteFurnish(hub);
    }

    /// <summary>Dev: opens the wall only.</summary>
    public static string DevOpenSuiteWall()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return "no hub";
        _building = hub.building;
        _suiteRebuildMeshes = true;
        try { return OpenSuiteWall(hub); }
        finally { _suiteRebuildMeshes = false; }
    }

    // ---------------------------------------------------------------- the furnishing

    private static string SuiteFurnish(AsylumHub hub)
    {
        StringBuilder log = new StringBuilder();
        Transform office = hub.dressing.Find("Office");
        if (office == null) office = Group(hub.dressing, "Office");

        // 1. the old dressing goes (the F101 marker and the brass plate stay); anything of this feature that is already placed goes too
        RemoveFromHub(hub, office.Cast<Transform>().Where(c => !c.name.StartsWith("F101_Office_Done") && !c.name.StartsWith("RoomSign")));
        office = hub.dressing.Find("Office");
        Transform finale = hub.life.Find("Finale");
        if (finale != null && finale.Find("Director") != null) RemoveFromHub(hub, new[] { finale.Find("Director") });
        Physics.SyncTransforms();

        // 2. the pack props: the desk first (everything else is measured from it)
        Transform deskT = SuiteDesk(hub);
        if (deskT == null) return "F115: TableOffice (1) not found";
        Vector3 parentScale = deskT.parent.lossyScale; // the shells are imported at 0.2, so the pack props carry a local scale of 5
        deskT.localScale = new Vector3(DeskScale.x / parentScale.x, DeskScale.y / parentScale.y, DeskScale.z / parentScale.z);
        PlaceAt(deskT, DeskCx, DeskCz, 0f);
        Bounds desk = SuiteBounds(deskT);
        log.AppendLine($"desk {desk.min:F2}..{desk.max:F2}");

        PlaceAt(PackProp(hub, "Office", "Cupboard"), 116.45f, -59.45f, 90f);   // the wardrobe moves to the lounge's east wall
        PlaceAt(PackProp(hub, "Office", "Sofa (1)"), 114.9f, -60.25f, 0f);       // the south lane (z -62.6..-61.0) stays free: it is the way to the fridge find
        PlaceAt(PackProp(hub, "Office", "TableSmall"), 114.9f, -59.0f, 0f);
        foreach (string n in new[] { "TableSmall (5)", "TV_Retro (1)", "Paper (8)", "Paper (9)" }) HideProp(PackProp(hub, "Office", n));
        Transform antlers = PackProp(hub, "Office", "Antlers"), antlers2 = PackProp(hub, "Office", "Antlers_2");
        PlaceAt(antlers, SuiteBounds(antlers).center.x, -57.42f, 90f);
        PlaceAt(antlers2, SuiteBounds(antlers2).center.x, -63.085f, 90f);
        Transform ceiling = PackProp(hub, "Office", "SingleLight (5)");
        if (ceiling != null) PlaceAt(ceiling, DeskCx, DeskCz - 1.0f, ceiling.eulerAngles.y);
        foreach (string n in new[] { "KitchenShelf", "Tap (5)", "Kettle", "Mug", "Mug (1)", "TableWhite (1)", "ChairLeather (2)", "ChairLeather (3)" }) HideProp(PackProp(hub, "Kitchen", n));
        PlaceAt(PackProp(hub, "Kitchen", "Heater (5)"), 114.16f, -63.52f, 0f);
        PlaceAt(PackProp(hub, "Kitchen", "Sofa_Chair (1)"), 113.4f, -59.2f, 90f);

        // 3. what is ours
        DressRoomCtx r = new DressRoomCtx { def = Def("Office"), group = office, rng = new System.Random(StableHash("F115")) };
        r.childStart = office.childCount;
        Placed host = new Placed { go = deskT.gameObject, key = "TableOffice (1)", center = desk.center, size = desk.size, yaw = 0f };

        // the desk top, seen from the visitor side: the monitor's back toward the visitors (its screen faces the Director's chair to the north), keyboard, notebook,
        // pencil holder, the family photo turned to the Director, a stack of the pack's papers, the pack Lamp, a mug
        On(r, host, "PC_Monitor", 0.20f, -0.10f, 180f);
        On(r, host, "Keyboard", 0.20f, 0.53f, 180f, 1.1f);
        On(r, host, "m:notebook", -0.25f, 0.30f, 20f);
        On(r, host, "m:pencil_holder", -0.80f, 0.55f, 0f, 1.4f);
        On(r, host, "m:photo_frame", -0.55f, -0.50f, 10f, 1.3f);
        Transform lamp = PackProp(hub, "Office", "Lamp");
        if (lamp != null) PlaceAt(lamp, DeskCx + 0.75f, DeskCz + 0.25f, 57f);
        Transform mug = PackProp(hub, "Office", "Mug (2)");
        if (mug != null) { PlaceAt(mug, DeskCx + 0.60f, DeskCz - 0.25f, 345f); mug.position = new Vector3(mug.position.x, desk.max.y, mug.position.z); }
        foreach (string n in new[] { "Paper", "Paper (1)", "Paper (2)" })
        {
            Transform p = PackProp(hub, "Office", n);
            if (p == null) continue;
            float k = n == "Paper" ? 0f : n == "Paper (1)" ? 1f : 2f;
            PlaceAt(p, DeskCx - 0.12f - 0.05f * k, DeskCz - 0.18f - 0.03f * k, 170f + k * 23f);
            p.position = new Vector3(p.position.x, desk.max.y + 0.004f * (k + 1f), p.position.z);
        }

        Put(r, "Carpet", new Vector3(DeskCx, 0f, -59.6f), 0f, 1f, null, false);
        Put(r, "ChairOffice", new Vector3(109.62f, 0f, -57.62f), 20f, 1f, null, false, true);
        Put(r, "ChairLeather", new Vector3(108.1f, 0f, -60.25f), 180f, 1f, null, false, true);
        Put(r, "ChairLeather", new Vector3(109.7f, 0f, -60.25f), 180f, 1f, null, false, true);
        Put(r, "alch:Alch_Cupboard1", new Vector3(110.70f, 0f, -57.42f), 270f, 1f, null, false, true);
        Put(r, "alch:Alch_Cupboard2", new Vector3(110.70f, 0f, -63.085f), 270f, 1f, null, false, true);
        Put(r, "CaseMetallic", new Vector3(103.86f, 0f, -57.8f), 90f, 1f, null, false, true);
        Put(r, "alch:Alch_GlobeStand", new Vector3(107.3f, 0f, -63.3f), 0f, 1f, null, false, true);
        Put(r, "alch:Alch_Painting1", new Vector3(107.26f, 0f, -63.62f), 0f, 1f, null, false, false, 1.4f);
        // a dresser with a few bottles under the south window's side of the room, books on the bookcases (shelves measured at 0.21 / 0.66 / 1.08 / 1.50 m)
        Put(r, "alch:Alch_Drawer1", new Vector3(104.25f, 0f, -63.30f), 0f, 1f, null, false, true);
        Put(r, "alch:Alch_BottlesB", new Vector3(104.25f, 0f, -63.40f), 15f, 1f, null, false, false, 1.05f);
        Put(r, "alch:Alch_BookRowB", new Vector3(110.70f, 0f, -57.42f), 270f, 1f, null, false, false, 1.08f);
        Put(r, "alch:Alch_BookPileA", new Vector3(110.70f, 0f, -57.42f), 270f, 1f, null, false, false, 0.66f);
        Put(r, "alch:Alch_BookPileA", new Vector3(110.70f, 0f, -63.085f), 90f, 1f, null, false, false, 1.5f);
        Put(r, "Paper", new Vector3(107.2f, 0f, -61.4f), 100f, 1f, null, false);
        Put(r, "Paper", new Vector3(110.3f, 0f, -60.6f), 235f, 1f, null, false);
        Put(r, "Paper", new Vector3(107.55f, 0f, -58.3f), 185f, 1f, null, false);
        Decal(r, "BloodSplat", new Vector3(107.0f, 0f, -59.9f), 0.9f, 20f);

        // the lounge
        Put(r, "Carpet", new Vector3(114.9f, 0f, -59.6f), 0f, 1f, null, false);
        Transform tableT = PackProp(hub, "Office", "TableSmall");
        Bounds tb = SuiteBounds(tableT);
        Placed table = new Placed { go = tableT.gameObject, key = "TableSmall", center = tb.center, size = tb.size, yaw = 0f };
        On(r, table, "alch:Alch_BottlesA", 0.2f, 0f, 20f);
        On(r, table, "Mug", -0.5f, 0.2f, 60f);
        Put(r, "alch:Alch_Cupboard1", new Vector3(116.42f, 0f, -57.45f), 90f, 1f, null, false, true);
        Put(r, "alch:Alch_Cupboard2", new Vector3(112.50f, 0f, -63.085f), 90f, 1f, null, false, true);
        Put(r, "alch:Alch_Painting2", new Vector3(115.9f, 0f, -63.62f), 0f, 1f, null, false, false, 1.4f);
        Put(r, "alch:Alch_Painting2", new Vector3(112.25f, 0f, -57.4f), 90f, 0.9f, null, false, false, 1.2f);
        Put(r, "alch:Alch_BookPileA", new Vector3(116.42f, 0f, -57.45f), 90f, 1f, null, false, false, 1.08f);

        Transform marker = new GameObject(SuiteMarker).transform;
        marker.SetParent(office, false);
        int added = FinishDressCtx(r, "F115_");
        log.AppendLine($"F115 furnishing: {added} new props under Dressing/Office");

        // 4. the light over the desk (the finale keeps the nearest light to Director_Desk) and a quiet one for the lounge (the kitchen had none of its own)
        Transform fill = hub.lights != null ? hub.lights.Find("Fill_Office") : null;
        if (fill != null) { fill.position = new Vector3(DeskCx, fill.position.y, DeskCz - 1.0f); PrefabUtility.RecordPrefabInstancePropertyModifications(fill); }
        if (hub.lights != null && hub.lights.Find("Fill_Lounge") == null)
        {
            Light lounge = AddFill(hub, "Fill_Lounge", new Vector3(114.9f, FloorLevel + 3.0f, -60.2f), Sodium, 1.1f, 8f);
            log.AppendLine("Fill_Lounge added at " + lounge.transform.position);
        }

        // 5. the finale: desk point, Director, drawer trigger, master switch (moved in place: HubFinale holds references to them)
        log.AppendLine(SuiteFinale(hub, desk));

        // 6. the locker leaves the office; the third ampoule stays where it is, the second goes on the desk
        log.AppendLine(SuiteMoveLocker(hub, desk));
        Transform ampoule = hub.life.Find("Missions/Floor4/Item_Ampoule2");
        if (ampoule != null)
        {
            ampoule.position = new Vector3(DeskCx - 0.6f, desk.max.y, DeskCz - 0.3f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(ampoule);
            log.AppendLine("Item_Ampoule2 on the desk at " + ampoule.position);
        }
        else log.AppendLine("Item_Ampoule2 not found (Life/Missions/Floor4)");
        Physics.SyncTransforms();
        return log.ToString();
    }

    private static string SuiteFinale(AsylumHub hub, Bounds desk)
    {
        StringBuilder log = new StringBuilder();
        Transform point = hub.points != null ? hub.points.Find("Director_Desk") : null;
        if (point != null)
        {
            point.position = DeskPointFor(desk, FloorLevel);
            point.rotation = Quaternion.identity; // yaw 0 faces north, at the desk
            PrefabUtility.RecordPrefabInstancePropertyModifications(point);
        }
        Transform finale = hub.life.Find("Finale");
        if (finale == null) return "F115: Life/Finale is missing; nothing to re-fit";
        if (finale.Find("Director") == null) log.AppendLine(BuildDirectorMemory(hub, finale));

        Transform drawer = finale.Find("Desk");
        if (drawer != null)
        {
            drawer.position = new Vector3(desk.center.x, desk.max.y + 0.15f, desk.center.z - 0.35f);
            BoxCollider dc = drawer.GetComponent<BoxCollider>();
            if (dc != null) dc.size = new Vector3(desk.size.x + 0.4f, 1.0f, desk.size.z + 0.9f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(drawer);
        }
        Transform sw = finale.Find("MasterSwitch");
        if (sw != null)
        {
            bool wasActive = sw.gameObject.activeSelf;
            sw.gameObject.SetActive(true);
            Vector3 from = new Vector3(desk.center.x, desk.max.y + 1.2f, desk.center.z);
            Vector3 wallPoint = Physics.Raycast(from, Vector3.forward, out RaycastHit wall, 3.5f, ~0, QueryTriggerInteraction.Ignore) ? wall.point : from + Vector3.forward * 1.8f;
            Bounds sb = RendBounds(sw.gameObject);
            sw.position += new Vector3(from.x - sb.center.x, from.y - sb.center.y, (wallPoint.z - 0.01f - sb.size.z * 0.5f) - sb.center.z);
            sw.gameObject.SetActive(wasActive);
            PrefabUtility.RecordPrefabInstancePropertyModifications(sw);
            log.AppendLine($"MasterSwitch on the wall behind the desk at {SuiteBounds(sw).center:F2} (wall z {wallPoint.z:F3}, hit {(wall.collider != null ? wall.collider.name : "none")})");
        }
        Transform director = finale.Find("Director");
        Animator body = director != null ? director.GetComponentInChildren<Animator>(true) : null;
        log.AppendLine($"desk {desk.center:F2}, Director at {(body != null ? body.transform.position : Vector3.zero):F2}, Director_Desk at {(point != null ? point.position : Vector3.zero):F2}");
        return log.ToString();
    }

    /// <summary>Moves Locker_Upper out of the office into the upper hall, back to the office's north wall between the office door and the lounge door.</summary>
    private static string SuiteMoveLocker(AsylumHub hub, Bounds desk)
    {
        Transform locker = hub.life.Find("Lockers/Locker_Upper");
        if (locker == null) return "F115: Life/Lockers/Locker_Upper not found";
        Vector3 director = DirectorSpotFor(desk, FloorLevel);
        // The locker's own colliders must not answer the probes below (a re-run would find the locker itself and stand 0.6 m off the wall).
        List<Collider> own = locker.GetComponentsInChildren<Collider>(true).Where(c => c.enabled).ToList();
        foreach (Collider c in own) c.enabled = false;
        Physics.SyncTransforms();
        try { return MoveLockerToHall(hub, locker, director); }
        finally { foreach (Collider c in own) c.enabled = true; Physics.SyncTransforms(); }
    }

    private static string MoveLockerToHall(AsylumHub hub, Transform locker, Vector3 director)
    {
        // The upper hall's south wall (the office's and the lounge's north wall seen from the hall). The first choices are east of the lounge door; a locker
        // right behind the desk's wall would stand 0.9 m from the Director through the plaster, which is what the user complained about.
        foreach (float x in new[] { 115.0f, 116.0f, 117.0f, 103.5f, 114.5f })
        {
            if (Vector2.Distance(new Vector2(x, -56.74f), new Vector2(director.x, director.z)) < LockerMinFromDirector) continue;
            if (!Physics.Raycast(new Vector3(x, FloorLevel + 1.0f, -55.2f), Vector3.back, out RaycastHit hit, 2.5f, ~0, QueryTriggerInteraction.Ignore) || hit.normal.z < 0.96f) continue;
            if (!Physics.Raycast(new Vector3(x, FloorLevel + 0.6f, hit.point.z + 0.4f), Vector3.down, out RaycastHit floor, 1.2f, ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(floor.point.y - FloorLevel) > 0.1f) continue;
            Quaternion rot = Quaternion.LookRotation(Vector3.forward);
            if (Physics.CheckBox(new Vector3(x, FloorLevel + 1.0f, hit.point.z + 0.4f), new Vector3(0.45f, 0.85f, 0.38f), rot, ~0, QueryTriggerInteraction.Ignore)) continue;
            if (Physics.CheckBox(new Vector3(x, FloorLevel + 1.0f, hit.point.z + 1.3f), new Vector3(0.45f, 0.9f, 0.4f), rot, ~0, QueryTriggerInteraction.Ignore)) continue;
            locker.position = new Vector3(x, floor.point.y, hit.point.z);
            locker.rotation = rot;
            PrefabUtility.RecordPrefabInstancePropertyModifications(locker);
            return $"Locker_Upper moved to the hall at {locker.position:F2}, facing north";
        }
        return "F115: no free spot in the hall for Locker_Upper (it stays in the office)";
    }
}
