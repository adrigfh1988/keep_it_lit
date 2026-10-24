using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F116: THE COMMISSARY. The shop stops being a separate room: the hub's ground-floor storeroom (Building/1stFloor/Stock) is redecorated into it.
/// This is the first draft of AsylumHub/Shop, written into the hand-edited hub prefab (<see cref="ApplyHubToPrefab"/>); from then on the prefab is
/// the source of truth, so <see cref="BuildShop"/> is build-missing-only (it does nothing once the Commissary is complete) and
/// <see cref="RebuildShop"/> is the confirmed reset (development only, no menu item).
///
/// Layout (world metres; the hub sits at (100, 5, -50) with identity rotation; the room's interior is x 120.55..132.13, z -51.05..-44.18, floor top
/// y 5.03, ceiling 8.61; its only doorway is Door_V1 (8) at x 122.5 in the south wall, a pack door that opens outward into the hall):
///   - the counter runs east-west across the room at z -48.2 (x 125.5..128.5), the salesman behind it (north) with the pack's own shelves as his
///     stock; the old alchemist shop's counter tables and goods are cloned from ShopRoom (the Alchemist props the hub already uses everywhere),
///   - the contract board is on the south wall, east of the doorway; the SHARDS ACCEPTED brass sign hangs over the counter, a COMMISSARY name
///     board over the salesman, a rug lies in front of the counter,
///   - the room's lamp (Point light (48)), the oil lamp on the counter and one fill light by the door are the room's warm light; the door plaque in the
///     hall says COMMISSARY and OPEN / CLOSED.
/// What was in the way is removed, each by name, in <see cref="RemoveFromHub"/>'s prefab round trip and logged.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string ShopGroupName = "Shop";
    private const string ShopSalesmanPrefab = "Assets/Prefabs/ShopSalesman.prefab";
    private const string StockRoomPath = "1stFloor/Stock";
    private const string StockDoorPath = "1stFloor/Stock/Door_V1 (8)";

    private const float ShopFloorY = 5.03f;
    private static readonly Vector3 ShopRoomCentre = new Vector3(126.34f, ShopFloorY, -47.615f);
    private static readonly Vector3 ShopCounterAt = new Vector3(127.0f, ShopFloorY, -48.2f);
    private static readonly Vector3 ShopBoardAt = new Vector3(124.6f, ShopFloorY, -50.96f);

    // Pack props (under Building/1stFloor/Stock) that stand where the counter and the customers' floor go, or are stacked on such a prop.
    private static readonly string[] ShopRemoveBuilding =
    {
        "BigShelf_Stock", "BigShelf_Stock (1)", "BigShelf_Stock (2)", // two shelves in the south row and the toppled pile in the south-east corner
        "SmallMetalicCase",                                            // the green case at the pile
        "Box_V1 (8)", "Box_V1 (9)", "Box_V2 (3)",                      // boxes on those shelves
        "CanOld (4)", "CanOld (5)", "CanOld (6)", "CanOld (7)", "CanOld (8)", "CanOld (9)", "CanOld (10)", // the cans on the middle south shelf
    };

    // Hub dressing (under Dressing/Stock) in the way.
    private static readonly string[] ShopRemoveDressing =
    {
        "F101_Stock_BigShelf",  // the freestanding 2.4 m shelf in the middle of the room
        "RoomSign_Stock_0",     // the STORE ROOM plate: the Commissary has its own plaque over the door
    };

    // ---------------------------------------------------------------- ProjectSetup piece

    /// <summary>ProjectSetup piece: null when there is no hub (that piece reports it), or the hub has a complete Commissary and no old SHOP door.</summary>
    public static string ShopProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null;
        if (hub.shop == null || !hub.shop.IsComplete) return "missing: AsylumHub/Shop (the Commissary)";
        if (hub.wings != null && hub.wings.Find("ShopDoor") != null) return "the old Wings/ShopDoor is still in the hub (Build Missing Pieces removes it)";
        return null;
    }

    /// <summary>ProjectSetup's build: adds the Commissary to the scene hub and applies it to the prefab. Marks the scene dirty; the caller saves.</summary>
    public static void BuildShop() => BuildShopCore(false);

    /// <summary>Development only: tears AsylumHub/Shop down and builds it again (props already removed stay removed). Not a menu item.</summary>
    public static void RebuildShop() => BuildShopCore(true);

    private static void BuildShopCore(bool force)
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Asylum shop: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        if (hub.building == null || hub.wings == null) { Debug.LogError("Asylum shop: the hub has no Building / Wings group."); return; }
        if (!force && ShopProblem() == null) { Debug.Log("Asylum shop: the Commissary is already there."); return; }

        ShopRoom oldShop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        GameObject salesmanPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(ShopSalesmanPrefab);
        if (oldShop == null || !oldShop.IsComplete || salesmanPrefab == null)
        {
            Debug.LogError("Asylum shop: the Commissary is dressed with the old shop's counter, board and signs and with ShopSalesman.prefab; run Build Missing Pieces for the shop room first.");
            return;
        }

        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();
        StringBuilder log = new StringBuilder();

        // ---- 1. out with what is in the way (through the prefab asset: an instance may not delete its prefab's children)
        List<Transform> gone = new List<Transform>();
        Transform stock = hub.building.Find(StockRoomPath);
        if (stock == null) { Debug.LogError("Asylum shop: Building/" + StockRoomPath + " not found."); if (!wasActive) hub.SetContentActive(false); return; }

        Transform oldGroup = hub.transform.Find(ShopGroupName);
        if (oldGroup != null) { gone.Add(oldGroup); log.AppendLine("removed " + ShopGroupName + " (rebuilt)"); }
        Transform oldDoor = hub.wings.Find("ShopDoor");
        if (oldDoor != null) { gone.Add(oldDoor); log.AppendLine("removed Wings/ShopDoor (the F94 SHOP door; the Commissary is walked into)"); }
        foreach (string n in ShopRemoveBuilding)
        {
            Transform t = stock.Find(n);
            if (t == null) continue;
            gone.Add(t);
            log.AppendLine($"removed Building/{StockRoomPath}/{n} at {t.position:F2}");
        }
        foreach (string n in ShopRemoveDressing)
        {
            Transform t = hub.dressing != null ? hub.dressing.Find("Stock/" + n) : null;
            if (t == null) continue;
            gone.Add(t);
            log.AppendLine($"removed Dressing/Stock/{n} at {t.position:F2}");
        }
        if (gone.Count > 0)
        {
            RemoveFromHub(hub, gone);
            hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
            hub.SetContentActive(true);
            _building = hub.building;
            stock = hub.building.Find(StockRoomPath);
            Physics.SyncTransforms();
        }

        // ---- 1b. the freestanding case just behind the counter's west end moves 0.35 m south so it meets the counter and nobody walks round to the salesman
        Transform caseT = FindNear(hub.dressing != null ? hub.dressing.Find("Stock") : null, "F101_Stock_CaseMetallic", new Vector3(124.90f, 5.00f, -46.93f), 0.25f);
        if (caseT != null)
        {
            caseT.position += new Vector3(0f, 0f, -0.35f);
            PrefabUtility.RecordPrefabInstancePropertyModifications(caseT);
            log.AppendLine($"moved Dressing/Stock/F101_Stock_CaseMetallic from z -46.93 to {caseT.position.z:F2} (closes the gap between it and the counter)");
        }

        // ---- 2. the Commissary group
        GameObject shopGo = new GameObject(ShopGroupName);
        shopGo.transform.SetParent(hub.transform, false);
        shopGo.transform.position = ShopRoomCentre;
        shopGo.transform.rotation = Quaternion.identity;
        Transform shop = shopGo.transform;
        HubShop hubShop = shopGo.AddComponent<HubShop>();

        Transform src = oldShop.transform;

        // The counter rig: tables, blocker and goods cloned from the old shop (their group roots sit at the old room's origin, the counter's
        // centre at +4 m in z), the salesman behind it facing the customers.
        GameObject counterGo = new GameObject("Counter");
        counterGo.transform.SetParent(shop, false);
        counterGo.transform.position = ShopCounterAt;
        counterGo.transform.rotation = Quaternion.identity;
        CloneInto(src.Find("Counter"), counterGo.transform, new Vector3(0f, 0f, -4f));
        CloneInto(src.Find("CounterTop"), counterGo.transform, new Vector3(0f, 0f, -4f));

        Transform salesman = ((GameObject)PrefabUtility.InstantiatePrefab(salesmanPrefab, counterGo.transform)).transform;
        salesman.name = "Salesman";
        salesman.localPosition = new Vector3(0f, 0f, 1.1f);
        salesman.localRotation = Quaternion.Euler(0f, 180f, 0f);
        ShopkeeperAnimator keeper = salesman.GetComponent<ShopkeeperAnimator>();
        Animator body = salesman.GetComponentInChildren<Animator>();
        Transform head = body != null && body.isHuman ? body.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) Debug.LogWarning("Asylum shop: the salesman has no Humanoid Head bone, so his head will not follow the player.", salesman);

        // The counter turns north at its east end (a third table, turned 90 degrees) so nobody walks round behind the salesman; the west side is
        // closed by moving the freestanding case over (below).
        Transform tableSrc = counterGo.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name.StartsWith("WoodTableSquare01b"));
        if (tableSrc != null)
        {
            GameObject ret = Object.Instantiate(tableSrc.gameObject, counterGo.transform, false);
            ret.name = "CounterReturn";
            // the pack table keeps its own import rotation: turn it about the world Y axis, then stand its bounds on the floor at the spot
            Vector3 retAt = new Vector3(ShopCounterAt.x + 1.3f, ShopFloorY - 0.02f, ShopCounterAt.z + 0.95f); // overlaps the counter's east end by 0.2 m (2 cm lower, so the tops do not z-fight); its north end stops at the shelf
            ret.transform.Rotate(0f, 90f, 0f, Space.World);
            Bounds rb = RendBounds(ret);
            ret.transform.position += new Vector3(retAt.x - rb.center.x, retAt.y - rb.min.y, retAt.z - rb.center.z);
            GameObject retBlock = new GameObject("CounterReturnBlocker");
            retBlock.transform.SetParent(counterGo.transform, false);
            retBlock.transform.position = new Vector3(retAt.x, ShopFloorY + 0.45f, retAt.z);
            BoxCollider retBox = retBlock.AddComponent<BoxCollider>();
            retBox.size = new Vector3(0.8f, 0.9f, 1.49f);
        }

        HubShopService counter = counterGo.AddComponent<HubShopService>();
        counter.kind = HubShopService.Kind.Counter;
        counter.shop = hubShop;
        AddTrigger(counterGo.transform, "TalkZone", new Vector3(ShopCounterAt.x, ShopFloorY + 1.2f, ShopCounterAt.z - 0.9f), new Vector3(3.8f, 2.4f, 1.3f));

        // The contract board on the south wall, east of the doorway; its sign above it.
        GameObject boardGo = new GameObject("ContractBoard");
        boardGo.transform.SetParent(shop, false);
        boardGo.transform.position = ShopBoardAt;
        boardGo.transform.rotation = Quaternion.identity;
        CloneInto(src.Find("ContractBoard"), boardGo.transform, new Vector3(3f, 0f, 5.91f));
        Transform sign = CloneInto(src.Find("ContractSign"), boardGo.transform, Vector3.zero);
        if (sign != null) sign.localPosition = new Vector3(0f, 2.5f, -0.01f);
        BoxCollider boardBox = boardGo.AddComponent<BoxCollider>();
        boardBox.center = new Vector3(0f, 1.7f, 0.02f);
        boardBox.size = new Vector3(2.1f, 1.2f, 0.2f);
        HubShopService contracts = boardGo.AddComponent<HubShopService>();
        contracts.kind = HubShopService.Kind.Contracts;
        contracts.shop = hubShop;
        AddTrigger(boardGo.transform, "TalkZone", new Vector3(ShopBoardAt.x, ShopFloorY + 1.2f, ShopBoardAt.z + 0.7f), new Vector3(2.6f, 2.4f, 1.2f));

        // The two signs over the counter: SHARDS ACCEPTED (brass, hung from the beams) and the COMMISSARY name board (wood) above the salesman.
        TextMeshPro counterSign = null;
        Transform shardsSign = CloneInto(src.Find("ShopSign"), shop, Vector3.zero);
        if (shardsSign != null)
        {
            shardsSign.rotation = src.Find("ShopSign").rotation;
            shardsSign.position = new Vector3(ShopCounterAt.x, 7.4f, ShopCounterAt.z);
            shardsSign.localScale = Vector3.one * 0.8f; // the old sign is 3.9 m wide; the counter is 3.0
            counterSign = shardsSign.GetComponent<TextMeshPro>();
        }
        Transform nameBoard = CloneInto(src.Find("ShopNameBoard"), shop, Vector3.zero);
        if (nameBoard != null)
        {
            nameBoard.rotation = src.Find("ShopNameBoard").rotation;
            nameBoard.position = new Vector3(ShopCounterAt.x, 7.95f, ShopCounterAt.z - 1.8f); // over the customers' floor, in front of the rug
            nameBoard.localScale = Vector3.one * 0.9f;
            TextMeshPro t = nameBoard.GetComponent<TextMeshPro>();
            if (t != null) t.text = "COMMISSARY";
            WorldSignBuilder.SetLocalizedKey(nameBoard.gameObject, "world.shop.commissary");
        }

        FixHubTransparency(counterGo.transform);

        // A rug in front of the counter (the old shop's), warming the grey floor.
        Transform rugSrc = FindDeep(src, "Carpet01");
        if (rugSrc != null)
        {
            Transform rug = CloneInto(rugSrc, shop, Vector3.zero);
            rug.rotation = rugSrc.rotation;
            rug.position = new Vector3(ShopCounterAt.x, ShopFloorY + 0.01f, ShopCounterAt.z - 1.6f);
        }

        // ---- 3. light: the room's own lamp, the oil lamp on the counter, one fill by the door
        List<Light> lamps = new List<Light>();
        Transform lampFixture = hub.building.Find("PointLights_All/Point light (48)");
        if (lampFixture != null)
        {
            Light l = lampFixture.GetComponent<Light>();
            if (l != null) { l.color = Palette[Area.Ground].Color; l.intensity = 2.2f; lamps.Add(l); } // already the Ground palette's warm tungsten (F95); only a little brighter
        }
        else Debug.LogWarning("Asylum shop: Building/PointLights_All/Point light (48) is gone; the room has only the oil lamp and the door light.");
        foreach (Light l in counterGo.GetComponentsInChildren<Light>(true)) { l.shadows = LightShadows.None; lamps.Add(l); }
        GameObject fillGo = new GameObject("DoorLight");
        fillGo.transform.SetParent(shop, false);
        fillGo.transform.position = new Vector3(123.9f, 7.4f, -49.7f);
        Light fill = fillGo.AddComponent<Light>();
        fill.type = LightType.Point;
        fill.color = new Color(1f, 0.72f, 0.42f);
        fill.intensity = 1.5f;
        fill.range = 5.5f;
        fill.shadows = LightShadows.None;
        fill.lightmapBakeType = LightmapBakeType.Realtime;
        lamps.Add(fill);

        // ---- 4. the door: the leaf swings shut/open, a solid blocker and the E target while shut, a plaque on the hall side
        Transform stockDoor = hub.building.Find(StockDoorPath);
        Transform hinge = stockDoor != null ? stockDoor.Find("DoorHinge") : null;
        if (hinge == null) { Debug.LogError("Asylum shop: " + StockDoorPath + "/DoorHinge not found."); if (!wasActive) hub.SetContentActive(false); return; }
        MakeMovable(hinge); // the leaf was lightmap/occluder static; it moves now
        const float openYaw = 265f; // the pack's authored pose: the leaf stands open, swung out into the hall (a re-run must not read it from a shut door)
        float closedYaw = MeasureClosedYaw(hinge, stockDoor, openYaw);

        GameObject lockGo = new GameObject("DoorLock");
        lockGo.transform.SetParent(shop, false);
        Vector3 frame = RendBounds(stockDoor.Find("Door_Frame").gameObject).center;
        lockGo.transform.position = new Vector3(frame.x, ShopFloorY, frame.z);
        BoxCollider blocker = lockGo.AddComponent<BoxCollider>();
        blocker.center = new Vector3(0f, 1.1f, 0f);
        blocker.size = new Vector3(1.15f, 2.2f, 0.3f);
        blocker.enabled = false; // the saved state is "open"; HubShop.Refresh closes it
        HubShopDoor doorLock = lockGo.AddComponent<HubShopDoor>();
        SerializedObject lockSo = new SerializedObject(doorLock);
        lockSo.FindProperty("blocker").objectReferenceValue = blocker;
        lockSo.ApplyModifiedPropertiesWithoutUndo();

        TextMeshPro plaque = BuildShopPlaque(shop, frame, hub);
        // the hall is dark: a small warm light in front of the plaque so it can be read from across the hall (on only while the shop is open)
        GameObject plaqueLightGo = new GameObject("PlaqueLight");
        plaqueLightGo.transform.SetParent(shop, false);
        plaqueLightGo.transform.position = new Vector3(frame.x, 7.3f, frame.z - 1.3f);
        Light plaqueLight = plaqueLightGo.AddComponent<Light>();
        plaqueLight.type = LightType.Point;
        plaqueLight.color = new Color(1f, 0.72f, 0.42f);
        plaqueLight.intensity = 1.0f;
        plaqueLight.range = 3.5f;
        plaqueLight.shadows = LightShadows.None;
        plaqueLight.lightmapBakeType = LightmapBakeType.Realtime;
        lamps.Add(plaqueLight);

        // ---- 5. wire HubShop, zone, hub
        GameObject zoneGo = new GameObject("RoomZone");
        zoneGo.transform.SetParent(shop, false);
        zoneGo.transform.position = new Vector3(126.34f, ShopFloorY + 1.0f, -47.615f);
        zoneGo.layer = 2; // Ignore Raycast
        BoxCollider zone = zoneGo.AddComponent<BoxCollider>();
        zone.isTrigger = true;
        zone.size = new Vector3(11.58f, 3.0f, 6.87f); // feet from floor - 0.5 to floor + 2.5: the classroom above never counts

        hubShop.roomZone = zone;
        hubShop.counter = counter;
        hubShop.board = contracts;
        hubShop.headPivot = head;
        hubShop.shopkeeper = keeper;
        hubShop.counterSign = counterSign;
        hubShop.doorPlaque = plaque;
        hubShop.doorHinge = hinge;
        hubShop.openYaw = openYaw;
        hubShop.closedYaw = closedYaw;
        hubShop.doorLock = doorLock;
        hubShop.lamps = lamps.ToArray();
        hub.shop = hubShop;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hub);
        MakeMovable(shop.Find("Counter")); // the clones were static where they came from; nothing here is batched with the room
        MakeMovable(shop.Find("ContractBoard"));

        // ---- 6. the chalk star that marks an open shop (HubStarTrail), now on the hall wall beside the doorway
        string star = ReplaceShopStar(hub);
        if (star != null) log.AppendLine(star);

        // ---- 7. the hub NavMesh (the counter adds colliders), audit, save
        log.AppendLine("navmesh rebake: " + HubNavMesh.Bake(hub));
        hubShop.Apply(true); // the saved state is the open look: lamps on, door open, blocker off, plaque OPEN
        log.AppendLine(Audit(hub.transform));

        if (!wasActive) hub.SetContentActive(false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        ApplyHubToPrefab(hub);
        Debug.Log("Asylum shop (the Commissary):\n" + log.ToString().Trim());
    }

    // ---------------------------------------------------------------- pieces

    /// <summary>A copy of `source` under `parent` at the local `offset`, same local rotation, active.</summary>
    private static Transform CloneInto(Transform source, Transform parent, Vector3 offset)
    {
        if (source == null) { Debug.LogWarning("Asylum shop: a prop of the old shop room is missing (parent " + parent.name + ")."); return null; }
        GameObject copy = Object.Instantiate(source.gameObject, parent, false);
        copy.name = source.name;
        copy.SetActive(true);
        copy.transform.localPosition = offset;
        return copy.transform;
    }

    /// <summary>
    /// The hub audit wants every transparent material to have _BlendModePreserveSpecular off. The old shop's glassware (the lab rig on the counter) uses the
    /// Alchemist pack's shared glass materials, which the shop room itself needs untouched: the clones get hub copies with the flag off (smoothness kept).
    /// </summary>
    private static void FixHubTransparency(Transform root)
    {
        EnsureFolderPath(MaterialFolder);
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || !m.HasProperty("_Surface") || m.GetFloat("_Surface") < 0.5f || !m.HasProperty("_BlendModePreserveSpecular") || m.GetFloat("_BlendModePreserveSpecular") == 0f) continue;
                string path = MaterialFolder + "/" + m.name + "_Hub.mat";
                Material copy = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (copy == null)
                {
                    copy = new Material(m) { name = m.name + "_Hub" };
                    copy.SetFloat("_BlendModePreserveSpecular", 0f);
                    copy.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                    AssetDatabase.CreateAsset(copy, path);
                }
                mats[i] = copy;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    /// <summary>The unit vector from the Commissary door out into the hall: the door frame's thin axis, pointing away from the room.</summary>
    internal static Vector3 ShopHallDirection(HubShop shop)
    {
        Bounds fb = ShopFrameBounds(shop);
        Vector3 d = fb.center - shop.roomZone.bounds.center;
        d.y = 0f;
        return fb.size.x < fb.size.z ? new Vector3(Mathf.Sign(d.x), 0f, 0f) : new Vector3(0f, 0f, Mathf.Sign(d.z));
    }

    /// <summary>The pack door frame of the Commissary doorway (the hinge's sibling Door_Frame), or a small box at the hinge.</summary>
    internal static Bounds ShopFrameBounds(HubShop shop)
    {
        Transform root = shop.doorHinge.parent;
        Transform frame = root != null ? root.Find("Door_Frame") : null;
        return frame != null ? RendBounds(frame.gameObject) : new Bounds(shop.doorHinge.position, Vector3.one * 0.2f);
    }

    /// <summary>The child of `parent` called `name` whose position is within `radius` (flat) of `at`.</summary>
    private static Transform FindNear(Transform parent, string name, Vector3 at, float radius)
    {
        if (parent == null) return null;
        foreach (Transform t in parent)
        {
            if (t.name != name) continue;
            Vector3 d = t.position - at; d.y = 0f;
            if (d.magnitude <= radius) return t;
        }
        return null;
    }

    private static Transform FindDeep(Transform root, string name) => root.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == name);

    private static BoxCollider AddTrigger(Transform parent, string name, Vector3 worldCentre, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = worldCentre;
        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        return box;
    }

    /// <summary>
    /// The hinge's local Y when the leaf lies in the frame: of the two quarter turns from the open yaw, the one that puts the leaf's centre on the
    /// frame's centre. Measured, so a hand-edited door still works.
    /// </summary>
    private static float MeasureClosedYaw(Transform hinge, Transform doorRoot, float openYaw)
    {
        Bounds frame = RendBounds(doorRoot.Find("Door_Frame").gameObject);
        Vector3 e = hinge.localEulerAngles;
        float best = openYaw + 90f, bestDist = float.MaxValue;
        foreach (float candidate in new[] { openYaw + 90f, openYaw - 90f })
        {
            hinge.localEulerAngles = new Vector3(e.x, candidate, e.z);
            Physics.SyncTransforms();
            Bounds leaf = RendBounds(hinge.gameObject);
            float d = Vector2.Distance(new Vector2(leaf.center.x, leaf.center.z), new Vector2(frame.center.x, frame.center.z));
            if (d < bestDist) { bestDist = d; best = candidate; }
        }
        hinge.localEulerAngles = new Vector3(e.x, openYaw, e.z);
        Physics.SyncTransforms();
        return Mathf.Repeat(best, 360f);
    }

    /// <summary>The COMMISSARY / OPEN plaque on the hall side of the wall above the doorway (the F109 brass plaque, the hub's door plaque style).</summary>
    private static TextMeshPro BuildShopPlaque(Transform shop, Vector3 frameCentre, AsylumHub hub)
    {
        WorldSignBuilder.EnsureFonts();
        float wallZ = frameCentre.z - 0.1f;
        Vector3 from = new Vector3(frameCentre.x, 7.85f, frameCentre.z - 1.5f);
        if (Physics.Raycast(from, Vector3.forward, out RaycastHit hit, 3f, ~0, QueryTriggerInteraction.Ignore)) wallZ = hit.point.z;

        GameObject go = new GameObject("DoorPlaque");
        go.transform.SetParent(shop, false);
        go.transform.position = new Vector3(frameCentre.x, 7.85f, wallZ);
        go.transform.rotation = Quaternion.identity; // text faces -z, into the hall; the backing is behind it
        TextMeshPro text = go.AddComponent<TextMeshPro>();
        text.text = "COMMISSARY\n<size=60%>OPEN</size>";
        text.fontSize = 1.9f;
        text.alignment = TextAlignmentOptions.Center;
        WorldSignBuilder.Style(text, WorldSignBuilder.SignStyle.BrassPlaque, new WorldSignBuilder.SignOptions { darkInset = true, fixedInner = new Vector2(2.1f, 0.68f), fontSize = 1.9f, standoff = 0.05f });
        return text;
    }

    /// <summary>Gives HubStarTrail a shop star on the hall wall beside the doorway (the old one hung on the removed SHOP door's leaf).</summary>
    private static string ReplaceShopStar(AsylumHub hub)
    {
        Transform way = hub.life != null ? hub.life.Find(WayfindingName) : null;
        HubStarTrail trail = way != null ? way.GetComponent<HubStarTrail>() : null;
        if (trail == null) return "shop star: no HubStarTrail yet (Build Missing Pieces adds it, and its star, later)";
        SerializedObject so = new SerializedObject(trail);
        SerializedProperty pool = so.FindProperty("pool");
        Material material = null;
        for (int i = 0; i < pool.arraySize && material == null; i++)
        {
            Renderer r = pool.GetArrayElementAtIndex(i).objectReferenceValue as Renderer;
            if (r != null) material = r.sharedMaterial;
        }
        if (material == null) material = AssetDatabase.LoadAssetAtPath<Material>(StarMaterialPath);
        if (material == null) return "shop star: the star material is missing";

        Transform old = hub.shop.transform.Find("ShopStar");
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Renderer star = PlaceShopStar(hub, material);
        so.FindProperty("shopStar").objectReferenceValue = star;
        so.ApplyModifiedPropertiesWithoutUndo();
        PrefabUtility.RecordPrefabInstancePropertyModifications(trail);
        return "shop star: on the hall wall at " + star.transform.position.ToString("F2");
    }
}
