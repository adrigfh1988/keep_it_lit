using System.Linq;
using UnityEngine;

public static partial class AsylumHubBuilder
{
    /// <summary>
    /// The points group: MainEntrance just inside the pack MainDoor, facing into the hall, plus the three empty hooks for
    /// the later patch (D17): Nurse_Station, Records_Room (OfficeA) and Director_Desk (Office). Returns MainEntrance.
    /// </summary>
    private static Transform BuildPoints(AsylumHub hub)
    {
        Transform door = _building.GetComponentsInChildren<Transform>(true).FirstOrDefault(t => t.name == "MainDoor");
        Vector3 doorCentre = door != null ? RendererCentre(door) : HubPosition + new Vector3(0f, 0f, -13.6f);
        Vector3 inside = Vector3.forward;

        // The hall side is the side that has a floor at the ground-storey level a step or two away.
        float best = float.MaxValue;
        foreach (Vector3 side in new[] { Vector3.forward, Vector3.back, Vector3.right, Vector3.left })
        {
            Vector3 probe = doorCentre + side * 1.6f;
            if (!FloorAt(probe, doorCentre.y + 1.5f, out float floorY)) continue;
            float score = Mathf.Abs(floorY - HubPosition.y);
            // Prefer the side where the hall keeps going: a second floor hit further in.
            if (FloorAt(doorCentre + side * 4f, doorCentre.y + 1.5f, out float farY)) score += Mathf.Abs(farY - HubPosition.y); else score += 5f;
            if (score < best) { best = score; inside = side; }
        }

        Vector3 spot = doorCentre + inside * 1.5f;
        FloorAt(spot, doorCentre.y + 1.5f, out float y);
        spot.y = y + 0.05f;
        Transform entrance = Point(hub.points, "MainEntrance", spot, Mathf.Atan2(inside.x, inside.z) * Mathf.Rad2Deg);
        hub.mainEntrance = entrance;

        Hook(hub, "Nurse_Station", "Gatehouse");
        Hook(hub, "Records_Room", "OfficeA");
        Hook(hub, "Director_Desk", "Office");
        return entrance;
    }

    private static void Hook(AsylumHub hub, string pointName, string room)
    {
        Bounds b = RoomBounds(_building, room, 0f);
        if (b.size == Vector3.zero) return;
        Vector3 p = b.center;
        FloorAt(p, b.min.y + 1.5f, out float y);
        Point(hub.points, pointName, new Vector3(p.x, y + 0.05f, p.z), 0f);
    }

    /// <summary>Highest collider surface under `from` within 4 m, ignoring nothing but triggers.</summary>
    private static bool FloorAt(Vector3 xz, float fromY, out float y)
    {
        y = 0f;
        if (Physics.Raycast(new Vector3(xz.x, fromY, xz.z), Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore))
        {
            y = hit.point.y;
            return true;
        }
        return false;
    }

    private static Vector3 RendererCentre(Transform t)
    {
        Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return t.position;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        return new Vector3(b.center.x, b.min.y, b.center.z);
    }
}
