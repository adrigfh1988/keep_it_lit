using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// F94: the SHOP door in the Asylum hub, added to the (hand-edited) hub prefab without regenerating it - build-missing-only, like the
/// story layer. It is a wing-door twin (<see cref="BuildDoorShell"/>) on a free wall of the entrance hall that needs no props moved:
/// candidate walls with anything in the clearance box are skipped, so the user's dressing is left alone.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string ShopDoorName = "ShopDoor";

    /// <summary>Rooms tried for the SHOP door, in order.</summary>
    private static readonly string[] ShopDoorRooms = { "Entrance", "HallDownstair", "OfficeA" };

    /// <summary>ProjectSetup piece: null when there is no hub (that piece reports it) or the hub has a complete SHOP door.</summary>
    public static string ShopDoorProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null;
        if (hub.shopDoor == null || !hub.shopDoor.IsComplete) return "missing: Wings/" + ShopDoorName;
        if (hub.ContentActive && hub.building != null)
        {
            _building = hub.building;
            if (!ShopDoorPlacementOk(hub.shopDoor)) return "Wings/" + ShopDoorName + " overlaps a pack door, frame or arch (Build Missing Pieces moves it)";
        }
        return null;
    }

    private static readonly string[] DoorLikeTokens = { "Door", "Frame", "Arch" };

    /// <summary>The existing SHOP door's own spot passes <see cref="ClearsDoorLike"/>.</summary>
    private static bool ShopDoorPlacementOk(ShopDoor door)
    {
        Physics.SyncTransforms();
        WallSpot here = new WallSpot { Point = door.transform.position, Normal = door.transform.forward, Props = new List<Collider>() };
        return ClearsDoorLike(here, door.transform);
    }

    /// <summary>
    /// True when the door + plaque volume at `spot` (about 2.2 m wide x 3.0 m tall, 0.4 m deep off the wall, grown by 0.4 m) touches no
    /// pack renderer named Door / Frame / Arch (collider-less frames included) and no wing door or SHOP door. `ignore` is the door being moved.
    /// </summary>
    private static bool ClearsDoorLike(WallSpot spot, Transform ignore)
    {
        Vector3 across = Mathf.Abs(spot.Normal.z) > 0.5f ? Vector3.right : Vector3.forward;
        Vector3 size = across * 2.2f + spot.Normal * 0.4f + Vector3.up * 3.0f;
        Bounds volume = new Bounds(spot.Point + spot.Normal * 0.1f + Vector3.up * 1.5f, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)));
        volume.Expand(0.8f); // 0.4 m each side

        foreach (Renderer r in _building.GetComponentsInChildren<Renderer>(true))
        {
            if (r == null || r is ParticleSystemRenderer || r.GetComponent<TMPro.TMP_Text>() != null || !r.bounds.Intersects(volume)) continue;
            if (ignore != null && r.transform.IsChildOf(ignore)) continue;
            for (Transform t = r.transform; t != null && t != _building; t = t.parent)
            {
                if (DoorLikeTokens.Any(token => t.name.Contains(token))) return false;
            }
        }

        AsylumHub hub = _building.GetComponentInParent<AsylumHub>();
        if (hub == null) return true;
        List<Transform> others = new List<Transform>();
        foreach (WingDoor wing in hub.wingDoors) { if (wing != null) others.Add(wing.transform); }
        if (hub.shopDoor != null) others.Add(hub.shopDoor.transform);
        foreach (Transform other in others)
        {
            if (other == ignore) continue;
            foreach (Renderer r in other.GetComponentsInChildren<Renderer>(true))
            {
                if (r.bounds.Intersects(volume)) return false;
            }
        }
        return true;
    }

    /// <summary>ProjectSetup's fix: adds the SHOP door to the scene hub and applies it to the prefab. Marks the scene dirty; the caller saves.</summary>
    public static void AddShopDoor()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Asylum shop door: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        if (hub.wings == null || hub.building == null) { Debug.LogError("Asylum shop door: the hub has no Wings / Building group."); return; }

        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();

        // An existing, well-placed door is left alone; a complete one that overlaps a pack door/frame/arch is moved (not rebuilt).
        ShopDoor existing = hub.shopDoor != null && hub.shopDoor.IsComplete ? hub.shopDoor : null;
        if (existing != null && ShopDoorPlacementOk(existing))
        {
            Debug.Log("Asylum shop door: already there.");
            if (!wasActive) hub.SetContentActive(false);
            return;
        }

        // A half-built earlier attempt would double up.
        if (existing == null)
        {
            Transform stale = hub.wings.Find(ShopDoorName);
            if (stale != null) Object.DestroyImmediate(stale.gameObject);
        }

        EnsureFolderPath(MaterialFolder);
        Material plate = LoadOrCreateMaterial("Asylum_Plate", new Color(0.09f, 0.075f, 0.06f), 0.25f);

        ShopDoor door = null;
        foreach (string room in ShopDoorRooms)
        {
            Bounds bounds = RoomBounds(_building, room, 0f);
            if (bounds.size == Vector3.zero) continue;

            float floorY = FloorHeight(room, bounds);
            string roomName = room;
            // Only a wall that needs no prop moved (nothing with a collider, nothing hanging in the door's space) and that
            // does not run into a pack door, frame or arch or another hub door.
            Transform ignore = existing != null ? existing.transform : null;
            if (!FindWallSpot(bounds, floorY, RoomEntrances(room), out WallSpot spot,
                    s => s.Props.Count == 0 && HangingProps(roomName, s).Count == 0 && ClearsDoorLike(s, ignore)))
            {
                Debug.Log($"Asylum shop door: no clear wall in {room}.");
                continue;
            }

            if (existing != null)
            {
                existing.transform.position = new Vector3(spot.Point.x, floorY, spot.Point.z);
                existing.transform.rotation = Quaternion.LookRotation(spot.Normal, Vector3.up);
                door = existing;
                Debug.Log($"Asylum shop door: moved to {room} at {spot.Point}, facing {spot.Normal}; no props removed.");
            }
            else
            {
                door = BuildShopDoor(hub, spot, floorY, plate);
                Debug.Log($"Asylum shop door: placed in {room} at {spot.Point}, facing {spot.Normal}; no props removed.");
            }
            break;
        }

        if (door == null)
        {
            Debug.LogError("Asylum shop door: no prop-free wall found in " + string.Join(", ", ShopDoorRooms) + ". Nothing was added.");
            if (!wasActive) hub.SetContentActive(false);
            return;
        }

        hub.shopDoor = door;
        PrefabUtility.RecordPrefabInstancePropertyModifications(hub); // a direct field write on a prefab instance is otherwise lost at the next domain reload

        // The pack door's own colliders are solid; the hub NavMesh is baked from colliders.
        bool solid = door.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger);
        if (solid) Debug.Log("Asylum shop door navmesh rebake: " + HubNavMesh.Bake(hub));

        Debug.Log(Audit(hub.transform));

        if (!wasActive) hub.SetContentActive(false);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        ApplyHubToPrefab(hub);
    }

    private static ShopDoor BuildShopDoor(AsylumHub hub, WallSpot spot, float floorY, Material plate)
    {
        DoorShell shell = BuildDoorShell(hub, ShopDoorName, "Door_V3", false, "SHOP\n<size=60%>THE SALESMAN  ·  OPEN</size>", spot, floorY, plate);
        ShopDoor door = shell.Root.AddComponent<ShopDoor>();
        door.plaque = shell.Text;
        door.beacon = shell.Beacon;
        door.leafHinge = shell.HingeA;
        door.leafHingeB = shell.HingeB;
        door.arrivalPoint = shell.Arrival;
        door.Refresh();
        shell.Beacon.enabled = true; // the saved prefab keeps the beacon on (Refresh switches it off while closed); the audit counts it
        return door;
    }
}
