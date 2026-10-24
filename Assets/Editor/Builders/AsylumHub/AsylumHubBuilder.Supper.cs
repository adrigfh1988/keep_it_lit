using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F117: THE LAST SUPPER. The first draft of AsylumHub/Supper, written into the hand-edited hub prefab (<see cref="ApplyHubToPrefab"/>). The dining
/// room (Building/1stFloor/DiningRoom, interior x 70.98..85.2, z -63.9..-82, floor top y 5.03, its only door on the north wall at x 78.07) keeps
/// its scattered square tables and chairs, except down the room's middle: there the pack's own TableWhiteKitchen tables are pushed end to end into one
/// long refectory table (six sections, centred on the door's axis x 78.1, z -68.7..-76.98) under a white cloth, with three chairs a side (the pack's own
/// ChairWhite), a place setting at each, and the Director's high-backed chair at the head. Everything else standing inside the walkway rectangle round
/// the table (table, chairs, plates, mugs, pots and the hub dressing built from them) is removed, each by name, and logged.
/// Seats n = floors 1..5 (odd on the west side, even on the east); the sixth place is always empty. The seated patients are Timmy robots in the muted
/// "Lost" skin on a looping Sitting_Idle controller; the name cards, the finale candle and the room light are built here too. <see cref="HubSupper"/>
/// does the rest at runtime. Build-missing-only; <see cref="RebuildSupper"/> is the development reset (props already removed stay removed).
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string SupperGroupName = "Supper";
    private const string DiningRoomPath = "1stFloor/DiningRoom";
    private const string SupperControllerPath = HubAnimDir + "/HubSupper.controller";
    private const string AsylumPrefabs = "Assets/Abandoned_Asylum/Prefabs/";
    private const string AlchLights = "Assets/BK_AlchemistHouse/Prefabs/Items/Lights/";

    private const float SupperFloorY = 5.03f;
    private const float SupperX = 78.10f;           // the table's centre line: the dining room door's axis
    private const float SupperNorthZ = -68.70f;     // the table's door-side end
    private const int SupperSections = 6;
    private const float SupperChairDx = 0.86f;      // chair centre from the table's centre line (the table is 1.38 wide: 0.17 m of the seat is tucked under it)
    private static readonly float[] SupperRowZ = { -70.15f, -72.90f, -75.65f };

    /// <summary>Walkway rectangle: the table, its chairs and 1.2 m round them. Props of the room standing in it are removed (the table's reused parts excepted).</summary>
    private static readonly Rect SupperClearRect = Rect.MinMaxRect(75.5f, -78.9f, 80.7f, -67.4f);

    /// <summary>How high the sitting clip needs the body root above the floor for the hips to rest on a 0.5 m seat, and how far back (toward the chair back) it sits. Tuned by eye in renders.</summary>
    private const float SupperSeatLift = 0.0f;
    private const float SupperSeatBack = 0.05f;

    // ---------------------------------------------------------------- ProjectSetup piece

    /// <summary>ProjectSetup piece: null when there is no hub (that piece reports it) or the dining room's table is complete.</summary>
    public static string SupperProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null;
        if (hub.supper == null || !hub.supper.IsComplete) return "missing: AsylumHub/Supper (the long table, the seated patients and the name cards in the dining room)";
        return null;
    }

    /// <summary>ProjectSetup's build: the long table in the dining room, applied to the prefab. Marks the scene dirty; the caller saves.</summary>
    public static void BuildSupper() => BuildSupperCore(false);

    /// <summary>Development only: tears AsylumHub/Supper down and builds it again (props already removed or moved stay so). Not a menu item.</summary>
    public static void RebuildSupper() => BuildSupperCore(true);

    private static void BuildSupperCore(bool force)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Asylum supper: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        if (hub.building == null || hub.dressing == null) { Debug.LogError("Asylum supper: the hub has no Building / Dressing group."); return; }
        if (!force && SupperProblem() == null) { Debug.Log("Asylum supper: the long table is already there."); return; }

        GameObject officeChair = AssetDatabase.LoadAssetAtPath<GameObject>(AsylumPrefabs + "ChairOffice.prefab");
        if (officeChair == null) { Debug.LogError("Asylum supper: " + AsylumPrefabs + "ChairOffice.prefab is missing."); return; }

        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        StringBuilder log = new StringBuilder();

        Transform room = hub.building.Find(DiningRoomPath);
        if (room == null) { Debug.LogError("Asylum supper: Building/" + DiningRoomPath + " not found."); if (!wasActive) hub.SetContentActive(false); return; }

        // ---- 1. which pack tables and chairs become the long table; everything else in the walkway rectangle goes
        List<Transform> tableParts = PickSupperParts(room, "TableWhiteKitchen", SupperSections, 0.7f, 0.95f, SupperX, -72.8f);
        List<Transform> chairParts = PickSupperParts(room, "ChairWhite", 6, 1.0f, 0.95f, SupperX, -73.0f);
        HashSet<Transform> keep = new HashSet<Transform>(tableParts.Concat(chairParts));
        List<string> tablePaths = tableParts.Select(t => AnimationUtility.CalculateTransformPath(t, hub.transform)).ToList();
        List<string> chairPaths = chairParts.Select(t => AnimationUtility.CalculateTransformPath(t, hub.transform)).ToList();

        List<Transform> gone = new List<Transform>();
        Transform oldGroup = hub.transform.Find(SupperGroupName);
        if (oldGroup != null) { gone.Add(oldGroup); log.AppendLine("removed " + SupperGroupName + " (rebuilt)"); }
        foreach (Transform parent in new[] { room, hub.dressing.Find("DiningRoom") })
        {
            if (parent == null) continue;
            foreach (Transform t in parent)
            {
                if (keep.Contains(t) || !IsSupperProp(t.name)) continue;
                Bounds b = RendBounds(t.gameObject);
                if (b.size == Vector3.zero || b.min.y > SupperFloorY + 1.7f) continue;
                if (!SupperClearRect.Overlaps(Rect.MinMaxRect(b.min.x, b.min.z, b.max.x, b.max.z))) continue;
                gone.Add(t);
                log.AppendLine($"removed {AnimationUtility.CalculateTransformPath(t, hub.transform)} at {t.position:F2} (in the long table's walkway)");
            }
        }
        if (gone.Count > 0)
        {
            RemoveFromHub(hub, gone);
            hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
            hub.SetContentActive(true);
            _building = hub.building;
            room = hub.building.Find(DiningRoomPath);
            Physics.SyncTransforms();
        }
        tableParts = tablePaths.Select(p => hub.transform.Find(p)).Where(t => t != null).ToList();
        chairParts = chairPaths.Select(p => hub.transform.Find(p)).Where(t => t != null).ToList();

        // ---- 2. the group
        GameObject groupGo = new GameObject(SupperGroupName);
        groupGo.transform.SetParent(hub.transform, false);
        groupGo.transform.position = new Vector3(SupperX, SupperFloorY, (SupperNorthZ + SupperNorthZ - SupperSections * 1.38f) * 0.5f);
        groupGo.transform.rotation = Quaternion.identity;
        HubSupper supper = groupGo.AddComponent<HubSupper>();
        Transform g = groupGo.transform;
        Transform tablesGroup = Group(g, "Tables");
        Transform seatsGroup = Group(g, "Seats");

        // ---- 3. the table: six TableWhiteKitchen sections end to end (the pack's own, plus copies of one if the room had fewer)
        if (tableParts.Count == 0) { Debug.LogError("Asylum supper: no TableWhiteKitchen left in the dining room to build from."); if (!wasActive) hub.SetContentActive(false); return; }
        Transform template = tableParts[0];
        List<Transform> sections = new List<Transform>(tableParts);
        while (sections.Count < SupperSections)
        {
            GameObject copy = Object.Instantiate(template.gameObject, template.parent, false);
            copy.name = "TableWhiteKitchen_Supper_" + sections.Count;
            sections.Add(copy.transform);
            log.AppendLine("added " + copy.name + " (a copy of " + template.name + ", the room had fewer tables near the middle)");
        }
        float pitch = 1.38f;
        for (int i = 0; i < SupperSections; i++)
        {
            Transform t = sections[i];
            t.rotation = Quaternion.identity;
            t.localScale = Vector3.one;
            Bounds b0 = RendBounds(t.gameObject);
            pitch = b0.size.z; // the square top: its own length is the pitch
            float cz = SupperNorthZ - pitch * 0.5f - pitch * i;
            t.position += new Vector3(SupperX - b0.center.x, SupperFloorY - b0.min.y, cz - b0.center.z);
            log.AppendLine($"moved {AnimationUtility.CalculateTransformPath(t, hub.transform)} to the long table, section {i + 1} at {t.position:F2}");
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }
        float southZ = SupperNorthZ - pitch * SupperSections;
        Physics.SyncTransforms();
        float top = sections.Max(s => RendBounds(s.gameObject).max.y);
        float half = pitch * 0.5f;
        float tableLen = pitch * SupperSections;
        float midZ = (SupperNorthZ + southZ) * 0.5f;

        // a white cloth over the top (the sheet material), with a short skirt down both long sides
        Material sheet = AssetDatabase.LoadAssetAtPath<Material>("Assets/SourceFiles/Materials/Asylum/Asylum_Sheet.mat");
        GameObject cloth = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cloth.name = "Cloth";
        Object.DestroyImmediate(cloth.GetComponent<BoxCollider>());
        cloth.transform.SetParent(tablesGroup, false);
        cloth.transform.localScale = new Vector3(pitch + 0.24f, 0.012f, tableLen + 0.04f);
        cloth.transform.position = new Vector3(SupperX, top + 0.006f, midZ);
        cloth.GetComponent<Renderer>().sharedMaterial = sheet;
        foreach (float side in new[] { -1f, 1f })
        {
            GameObject skirt = GameObject.CreatePrimitive(PrimitiveType.Cube);
            skirt.name = side < 0 ? "ClothSkirtWest" : "ClothSkirtEast";
            Object.DestroyImmediate(skirt.GetComponent<BoxCollider>());
            skirt.transform.SetParent(tablesGroup, false);
            skirt.transform.localScale = new Vector3(0.012f, 0.30f, tableLen + 0.04f);
            skirt.transform.position = new Vector3(SupperX + side * (half + 0.12f), top - 0.15f, midZ);
            skirt.GetComponent<Renderer>().sharedMaterial = sheet;
        }

        // ---- 4. chairs: three a side (floors 1..5 and the empty sixth place), then the Director's at the head
        if (chairParts.Count == 0) { Debug.LogError("Asylum supper: no upright ChairWhite left in the dining room to seat anybody."); if (!wasActive) hub.SetContentActive(false); return; }
        List<Transform> chairs = new List<Transform>(chairParts);
        while (chairs.Count < 6)
        {
            GameObject copy = Object.Instantiate(chairParts[0].gameObject, chairParts[0].parent, false);
            copy.name = "ChairWhite_Supper_" + chairs.Count;
            chairs.Add(copy.transform);
            log.AppendLine("added " + copy.name + " (a copy of " + chairParts[0].name + ")");
        }
        Vector3[] seatAt = new Vector3[6];
        float[] seatOut = new float[6]; // -1 west, +1 east
        for (int i = 0; i < 6; i++)
        {
            int row = i / 2;
            float out_ = i % 2 == 0 ? -1f : 1f; // seat n = i + 1: odd = west, even = east
            seatOut[i] = out_;
            Vector3 want = new Vector3(SupperX + out_ * SupperChairDx, SupperFloorY, SupperRowZ[row]);
            seatAt[i] = SeatChair(chairs[i], want, new Vector3(out_, 0f, 0f));
            log.AppendLine($"moved {AnimationUtility.CalculateTransformPath(chairs[i], hub.transform)} to place {i + 1} at {chairs[i].position:F2} (back to the {(out_ < 0 ? "west" : "east")})");
            PrefabUtility.RecordPrefabInstancePropertyModifications(chairs[i]);
        }

        GameObject directorChair = (GameObject)PrefabUtility.InstantiatePrefab(officeChair, g);
        directorChair.name = "DirectorChair";
        SeatChair(directorChair.transform, new Vector3(SupperX, SupperFloorY, southZ - 0.20f), new Vector3(0f, 0f, -1f));
        StripPhysicsKeepColliders(directorChair);

        // ---- 5. place settings (a plate and a mug at each of the six places, one plate at the head)
        Transform settings = Group(g, "PlaceSettings");
        for (int i = 0; i < 6; i++)
        {
            Vector3 side = new Vector3(seatOut[i], 0f, 0f);
            Vector3 plateAt = new Vector3(SupperX + side.x * 0.44f, top, seatAt[i].z);
            PlaceProp(settings, AsylumPrefabs + "Plate.prefab", plateAt, i * 37f);
            PlaceProp(settings, AsylumPrefabs + "Mug.prefab", new Vector3(SupperX + side.x * 0.47f, top, seatAt[i].z - 0.34f), 90f + i * 21f);
        }
        PlaceProp(settings, AsylumPrefabs + "Plate.prefab", new Vector3(SupperX, top, southZ + 0.34f), 0f);

        // the candle in front of the Director's chair: unlit all the time but the finale
        GameObject candleGo = new GameObject("Candle");
        candleGo.transform.SetParent(g, false);
        candleGo.transform.position = new Vector3(SupperX, top, southZ + 0.72f);
        GameObject unlit = PlaceProp(candleGo.transform, AlchLights + "Candle01.prefab", new Vector3(SupperX, top, southZ + 0.72f), 0f);
        GameObject lit = PlaceProp(candleGo.transform, AlchLights + "Candle01b_on.prefab", new Vector3(SupperX, top, southZ + 0.72f), 0f);
        if (unlit != null) unlit.name = "Unlit";
        if (unlit != null) foreach (Light l in unlit.GetComponentsInChildren<Light>(true)) l.enabled = false;
        Light candleLight = null;
        if (lit != null)
        {
            lit.name = "Lit";
            candleLight = lit.GetComponentInChildren<Light>(true); // the pack candle's own light (it carries a VolumetricLight, so it cannot be removed): retuned below
            // the pack's VolumetricLight draws through command buffers, which do nothing in URP but log a warning each time its light switches on
            foreach (MonoBehaviour mb in lit.GetComponentsInChildren<MonoBehaviour>(true)) if (mb != null && mb.GetType().Name == "VolumetricLight") mb.enabled = false;
        }
        if (candleLight == null)
        {
            GameObject candleLightGo = new GameObject("CandleLight");
            candleLightGo.transform.SetParent(candleGo.transform, false);
            candleLightGo.transform.position = new Vector3(SupperX, top + 0.3f, southZ + 0.72f);
            candleLight = candleLightGo.AddComponent<Light>();
        }
        candleLight.type = LightType.Point;
        candleLight.color = new Color(1f, 0.68f, 0.34f);
        candleLight.intensity = 1.3f;
        candleLight.range = 4.5f;
        candleLight.shadows = LightShadows.None;
        candleLight.lightmapBakeType = LightmapBakeType.Realtime;
        candleLight.enabled = false; // the saved look is the non-finale one; HubSupper.Refresh lights it
        supper.candle = candleGo;
        supper.candleLight = candleLight;
        supper.directorChair = directorChair;

        // ---- 6. the seated patients and their name cards
        WorldSignBuilder.EnsureFonts();
        TMP_FontAsset ink = WorldSignBuilder.FontAsset(WorldSignBuilder.SignFont.Reenie);
        Material inkMat = WorldSignBuilder.TextMaterial(WorldSignBuilder.SignFont.Reenie);
        Material paper = LoadOrCreateMaterial("SupperCard", new Color(0.72f, 0.68f, 0.58f), 0.12f);
        Material skin = HunterSkinBuilder.EnsureNpcSkin("Lost");
        AnimatorController controller = EnsureController(SupperControllerPath, new[] { ("SitIdle", "Sitting_Idle") });
        List<HubSupper.Seat> seats = new List<HubSupper.Seat>();
        for (int n = 1; n <= 5; n++)
        {
            int i = n - 1;
            Transform holder = Group(seatsGroup, "Seat_" + n);
            Vector3 chairC = seatAt[i];
            Vector3 face = new Vector3(-seatOut[i], 0f, 0f);       // toward the table
            Vector3 bodyAt = chairC + new Vector3(seatOut[i] * SupperSeatBack, SupperSeatLift, 0f);
            float yaw = Mathf.Atan2(face.x, face.z) * Mathf.Rad2Deg;

            Transform body = skin != null ? RobotBodyLike(holder, null, skin) : null;
            Transform head = null;
            if (body == null) Debug.LogError("Asylum supper: the robot body or the Lost skin could not be built; seat " + n + " has no patient.");
            else
            {
                body.name = "Body_Lost";
                body.GetComponent<Animator>().runtimeAnimatorController = controller;
                body.SetPositionAndRotation(bodyAt, Quaternion.Euler(0f, yaw, 0f));
                Animator an = body.GetComponent<Animator>();
                head = an != null && an.isHuman ? an.GetBoneTransform(HumanBodyBones.Head) : null;
                if (head == null) Debug.LogWarning("Asylum supper: seat " + n + "'s body has no Humanoid Head bone: its head will not bow.");
            }

            TextMeshPro cardText = BuildNameCard(holder, n, new Vector3(SupperX + seatOut[i] * (half - 0.11f), top, seatAt[i].z + 0.52f), seatOut[i], ink, inkMat, paper, supper);
            seats.Add(new HubSupper.Seat { body = body, head = head, card = cardText });
        }
        supper.seats = seats.ToArray();

        // ---- 7. the room's light moves from the dead end of the room to hang low over the table, warm
        Transform lampT = hub.building.GetComponentsInChildren<Light>(true).Where(l => l.name == "Point light (28)" && l.transform.position.z < -64f && l.transform.position.z > -82f && l.transform.position.x > 71f && l.transform.position.x < 85f && l.transform.position.y < 9f).Select(l => l.transform).FirstOrDefault();
        if (lampT != null)
        {
            Light l = lampT.GetComponent<Light>();
            log.AppendLine($"moved the dining room's light {lampT.parent.name}/{lampT.name} from {lampT.position:F2} to over the table (range 9.5, palette colour)");
            lampT.position = new Vector3(SupperX, 7.0f, midZ);
            Area? paletteArea = AreaOf(hub, l);
            if (paletteArea.HasValue) l.color = Palette[paletteArea.Value].Color; // the F95 palette colour of its area, so Check Project keeps agreeing (warm tungsten on the ground floor)
            l.intensity = 2.3f;
            l.range = 9.5f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(lampT);
            PrefabUtility.RecordPrefabInstancePropertyModifications(l);
            supper.tableLight = l;
        }
        else log.AppendLine("the dining room's own light was not found; the room keeps the lights it has");

        // ---- 8. the room zone, wiring
        GameObject zoneGo = new GameObject("RoomZone");
        zoneGo.transform.SetParent(g, false);
        zoneGo.transform.position = new Vector3(78.1f, SupperFloorY + 1.0f, -73.05f);
        zoneGo.layer = 2; // Ignore Raycast
        BoxCollider zone = zoneGo.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(14.1f, 3.0f, 18.1f); // feet from floor - 0.5 to floor + 2.5: the room above never counts
        supper.roomZone = zone;

        hub.supper = supper;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hub);
        MakeMovable(settings);
        supper.Refresh();
        log.AppendLine($"long table: {SupperSections} sections {pitch:F2} m, z {SupperNorthZ:F2}..{southZ:F2}, top y {top:F3}; chairs {chairs.Count}; seats {seats.Count}");
        log.AppendLine("navmesh rebake: " + HubNavMesh.Bake(hub));
        log.AppendLine(Audit(hub.transform));

        if (!wasActive) hub.SetContentActive(false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        ApplyHubToPrefab(hub);
        Debug.Log("Asylum supper (the dining room's long table):\n" + log.ToString().Trim());
    }

    // ---------------------------------------------------------------- helpers

    private static bool IsSupperProp(string name) =>
        name.StartsWith("Table") || name.StartsWith("Chair") || name.StartsWith("Plate") || name.StartsWith("Mug") || name.StartsWith("Pot") ||
        name.StartsWith("F98_") || name.StartsWith("F101_");

    /// <summary>
    /// Up to `count` upright, floor-standing children of `room` called `prefix`..., nearest to (x, z) first, within the walkway rectangle widened by
    /// `margin`. `minHeight` filters out toppled and stacked ones; `maxLift` is how far above the floor their bottom may be.
    /// </summary>
    private static List<Transform> PickSupperParts(Transform room, string prefix, int count, float minHeight, float maxLift, float x, float z)
    {
        Rect wide = Rect.MinMaxRect(SupperClearRect.xMin - 0.5f, SupperClearRect.yMin - 0.5f, SupperClearRect.xMax + 0.5f, SupperClearRect.yMax + 0.5f);
        List<(Transform t, float d)> found = new List<(Transform, float)>();
        foreach (Transform t in room)
        {
            if (!t.name.StartsWith(prefix)) continue;
            Bounds b = RendBounds(t.gameObject);
            if (b.size.y < minHeight * (prefix.StartsWith("Chair") ? 1.0f : 0.8f)) continue;           // toppled
            if (b.min.y > SupperFloorY + 0.1f || b.min.y < SupperFloorY - 0.2f) continue;               // stacked on something
            if (!wide.Contains(new Vector2(b.center.x, b.center.z))) continue;
            found.Add((t, new Vector2(b.center.x - x, b.center.z - z).sqrMagnitude));
        }
        return found.OrderBy(f => f.d).Take(count).Select(f => f.t).ToList();
    }

    /// <summary>
    /// Stands a chair on the floor at `at` (its renderer bounds' centre), turned about the world Y axis so its back points along `backDir`.
    /// The back is found from the mesh itself (the tall part of the chair against its seat), so a pack chair that faces a different way still
    /// lands right. Returns the chair's final centre on the floor.
    /// </summary>
    private static Vector3 SeatChair(Transform chair, Vector3 at, Vector3 backDir)
    {
        chair.rotation = Quaternion.identity;
        Vector3 back = ChairBackDirection(chair.gameObject);
        float turn = Mathf.DeltaAngle(Mathf.Atan2(back.x, back.z) * Mathf.Rad2Deg, Mathf.Atan2(backDir.x, backDir.z) * Mathf.Rad2Deg);
        chair.Rotate(0f, turn, 0f, Space.World);
        Bounds b = RendBounds(chair.gameObject);
        chair.position += new Vector3(at.x - b.center.x, SupperFloorY - b.min.y, at.z - b.center.z);
        b = RendBounds(chair.gameObject);
        return new Vector3(b.center.x, SupperFloorY, b.center.z);
    }

    /// <summary>The horizontal unit vector from a chair's seat toward its back rest: the mean of the vertices in the top fifth of its height against the mean of the vertices at seat height.</summary>
    private static Vector3 ChairBackDirection(GameObject chair)
    {
        Bounds all = RendBounds(chair);
        float h = all.size.y;
        Vector3 hi = Vector3.zero, lo = Vector3.zero;
        int nh = 0, nl = 0;
        foreach (MeshFilter mf in chair.GetComponentsInChildren<MeshFilter>(true))
        {
            if (mf.sharedMesh == null) continue;
            Matrix4x4 m = mf.transform.localToWorldMatrix;
            foreach (Vector3 v in mf.sharedMesh.vertices)
            {
                Vector3 w = m.MultiplyPoint3x4(v);
                float y = (w.y - all.min.y) / h;
                if (y > 0.82f) { hi += w; nh++; }
                else if (y > 0.35f && y < 0.55f) { lo += w; nl++; }
            }
        }
        if (nh == 0 || nl == 0) return Vector3.forward;
        Vector3 d = hi / nh - lo / nl;
        d.y = 0f;
        return d.sqrMagnitude < 1e-6f ? Vector3.forward : d.normalized;
    }

    private static void StripPhysicsKeepColliders(GameObject go)
    {
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
    }

    /// <summary>One pack prefab (kept linked) with its bounds' bottom centre on `at`, turned `yaw` degrees about the world Y axis (the import rotation stays), no colliders.</summary>
    private static GameObject PlaceProp(Transform parent, string assetPath, Vector3 at, float yaw)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null) { Debug.LogError("Asylum supper: " + assetPath + " is missing."); return null; }
        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        go.transform.Rotate(0f, yaw, 0f, Space.World);
        Bounds b = RendBounds(go);
        go.transform.position += new Vector3(at.x - b.center.x, at.y - b.min.y, at.z - b.center.z);
        StripPropPhysics(go);
        return go;
    }

    /// <summary>
    /// A folded place card standing on the table at `at` (its base), lying back at 50 degrees and facing `outward` (the aisle side, where the player
    /// walks), with the handwritten name on it and a trigger the E cast hits. Blank until the seat's case file has been read.
    /// </summary>
    private static TextMeshPro BuildNameCard(Transform parent, int seat, Vector3 at, float outward, TMP_FontAsset font, Material inkMat, Material paper, HubSupper supper)
    {
        const float alpha = 50f * Mathf.Deg2Rad; // tilt of the face normal above horizontal-out: a flatter card faces the lamp over the table
        Vector3 outDir = new Vector3(outward, 0f, 0f);
        Vector3 normal = (outDir * Mathf.Cos(alpha) + Vector3.up * Mathf.Sin(alpha)).normalized;   // toward the viewer
        Vector3 up = (Vector3.up * Mathf.Cos(alpha) - outDir * Mathf.Sin(alpha)).normalized;       // up the card's face
        Quaternion rot = Quaternion.LookRotation(-normal, up);
        const float w = 0.40f, h = 0.17f;

        GameObject card = GameObject.CreatePrimitive(PrimitiveType.Cube);
        card.name = "NameCard_" + seat;
        Object.DestroyImmediate(card.GetComponent<BoxCollider>());
        card.transform.SetParent(parent, false);
        card.transform.localScale = new Vector3(w, h, 0.006f);
        card.transform.SetPositionAndRotation(at + up * (h * 0.5f) + normal * 0.003f, rot);
        card.GetComponent<Renderer>().sharedMaterial = paper;

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(parent, false); // a sibling: the card is scaled flat and would squash its child
        textGo.transform.SetPositionAndRotation(card.transform.position + normal * 0.0035f, rot);
        TextMeshPro t = textGo.AddComponent<TextMeshPro>();
        t.font = font;
        if (inkMat != null) t.fontSharedMaterial = inkMat;
        t.rectTransform.sizeDelta = new Vector2(w - 0.03f, h - 0.02f); // two short lines: P3 over HILDE BRANDT
        t.enableAutoSizing = true;
        t.fontSizeMin = 0.3f;
        t.fontSizeMax = 2.2f;
        t.textWrappingMode = TextWrappingModes.NoWrap;
        t.overflowMode = TextOverflowModes.Overflow;
        t.alignment = TextAlignmentOptions.Center;
        t.color = new Color(0.07f, 0.05f, 0.04f, 0.95f);
        t.raycastTarget = false;
        t.text = "P3\nHILDE BRANDT";

        // the E trigger: a generous box round the card (the cast collects triggers), on the card root so the component finds it
        BoxCollider box = card.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(1.6f, 2.4f, 60f); // in the card's own (scaled) space: ~0.64 x 0.41 x 0.36 m
        HubSupperCard cardTarget = card.AddComponent<HubSupperCard>();
        SerializedObject so = new SerializedObject(cardTarget);
        so.FindProperty("seat").intValue = seat;
        so.FindProperty("supper").objectReferenceValue = supper;
        so.ApplyModifiedPropertiesWithoutUndo();
        return t;
    }
}
