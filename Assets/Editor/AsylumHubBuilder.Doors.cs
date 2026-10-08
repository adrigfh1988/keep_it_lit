using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// B4: the pack's own doors. Every door on a route from the main entrance to a wing is swung open (95 degrees) so the
/// building is explorable; of the rest about half stay closed, a few are left ajar (25 degrees) and the others stand open.
/// Doors never move at runtime, so their leaves may stay static - they are simply left alone by the static pass for now.
/// </summary>
public static partial class AsylumHubBuilder
{
    private sealed class PackDoor
    {
        public Transform Root;
        public List<Transform> Leaves = new List<Transform>();   // leaf objects, left/single first
        public bool Double;
    }

    private static List<PackDoor> FindPackDoors()
    {
        List<PackDoor> doors = new List<PackDoor>();
        foreach (Transform t in _building.GetComponentsInChildren<Transform>(true))
        {
            if (!(t.name.StartsWith("Door_V") || t.name.StartsWith("DoorD_V"))) continue;
            PackDoor d = new PackDoor { Root = t, Double = t.name.StartsWith("DoorD_V") };
            foreach (Transform child in t)
            {
                string n = child.name;
                if (n.EndsWith("Frame") || n.Contains("Window")) continue;
                if (child.GetComponent<Renderer>() == null) continue;
                if (d.Double ? (n.Contains("Left") || n.Contains("Right")) : n.StartsWith("Door")) d.Leaves.Add(child);
            }
            // Left leaves before right ones.
            d.Leaves = d.Leaves.OrderBy(l => l.GetComponent<Renderer>().bounds.center.x).ToList();
            if (d.Leaves.Count > 0) doors.Add(d);
        }
        return doors;
    }

    /// <summary>Leaf colliders on or off, so the temporary NavMesh can plan through every door.</summary>
    private static void SetLeafColliders(List<PackDoor> doors, bool on)
    {
        foreach (PackDoor d in doors)
        {
            foreach (Transform leaf in d.Leaves)
            {
                foreach (Collider c in leaf.GetComponentsInChildren<Collider>(true)) c.enabled = on;
            }
            foreach (Transform t in d.Root)
            {
                if (t.name.Contains("Window")) foreach (Collider c in t.GetComponentsInChildren<Collider>(true)) c.enabled = on;
            }
        }
        Physics.SyncTransforms();
    }

    private static void OpenRouteDoors(AsylumHub hub)
    {
        List<PackDoor> doors = FindPackDoors();
        SetLeafColliders(doors, false);
        List<Vector3[]> routes;
        try { routes = RoutePaths(hub); }
        finally { SetLeafColliders(doors, true); }

        int opened = 0, ajar = 0, closed = 0;
        foreach (PackDoor d in doors)
        {
            Vector3 centre = DoorCentre(d);
            bool onRoute = routes.Any(path => DistanceToPath(centre, path) < 1.1f);

            // Deterministic spread for the others, from the door position.
            int h = Mathf.Abs(Mathf.RoundToInt(centre.x * 7.13f + centre.z * 3.71f + centre.y * 1.9f)) % 8;
            float angle;
            if (onRoute) angle = 95f;
            else if (h < 4) angle = 0f;       // about half stay closed
            else if (h < 6) angle = 25f;      // a couple ajar
            else angle = 95f;

            if (angle <= 0f) { closed++; continue; }
            if (angle < 90f) ajar++; else opened++;
            SwingDoor(d, angle);
        }
        LastDiagnostics += $"Asylum doors: {opened} open, {ajar} ajar, {closed} closed ({routes.Count} routes of {hub.wingDoors.Length}).\n";
    }

    /// <summary>Middle of the whole door (frame and leaves), not of one leaf, so a double door counts as on the route when the path goes through its middle.</summary>
    private static Vector3 DoorCentre(PackDoor d)
    {
        Renderer[] rs = d.Root.GetComponentsInChildren<Renderer>(true);
        Bounds b = rs[0].bounds;
        foreach (Renderer r in rs) b.Encapsulate(r.bounds);
        return b.center;
    }

    private static void SwingDoor(PackDoor door, float angle)
    {
        // Swing toward the side with more free room.
        Vector3 axis = door.Root.forward;
        Vector3 centre = DoorCentre(door);
        float front = FreeDistance(centre, axis), back = FreeDistance(centre, -axis);
        float sign = front >= back ? 1f : -1f;

        for (int i = 0; i < door.Leaves.Count; i++)
        {
            Transform leaf = door.Leaves[i];
            bool rightLeaf = door.Double && door.Root.InverseTransformPoint(leaf.GetComponent<Renderer>().bounds.center).x > 0f;
            Transform hinge = rightLeaf ? HingeAtMaxEdge(door.Root, leaf) : ShopRoom.CreateDoorHinge(door.Root, leaf);
            if (hinge == null) continue;
            hinge.name = rightLeaf ? "DoorHingeB" : "DoorHinge";

            // The window panes of the double-door variants belong to their leaf.
            foreach (Transform t in door.Root)
            {
                if (!t.name.Contains("Window")) continue;
                bool second = t.name.EndsWith("2");
                if (second == rightLeaf) t.SetParent(hinge, true);
            }

            // The left leaf hinges on its min-x edge and the right on its max-x edge: opposite signs open both toward the same side.
            float yaw = sign * angle * (rightLeaf ? 1f : -1f);
            hinge.localRotation = Quaternion.Euler(0f, yaw, 0f) * hinge.localRotation;
        }
    }

    private static float FreeDistance(Vector3 from, Vector3 dir)
    {
        return Physics.Raycast(from, dir, out RaycastHit hit, 2f, ~0, QueryTriggerInteraction.Ignore) ? hit.distance : 2f;
    }

    private static float DistanceToPath(Vector3 p, Vector3[] corners)
    {
        float best = float.MaxValue;
        for (int i = 1; i < corners.Length; i++)
        {
            Vector3 a = corners[i - 1], b = corners[i];
            // Flatten the vertical axis lightly so a door on the same storey matches and one a storey away does not.
            Vector3 ab = b - a;
            float t = ab.sqrMagnitude < 1e-6f ? 0f : Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
            Vector3 c = a + ab * t;
            Vector3 d = p - c;
            d.y = Mathf.Max(0f, Mathf.Abs(d.y) - 1.2f) * 3f; // a door is centred 1 m above the path
            best = Mathf.Min(best, d.magnitude);
        }
        return best;
    }
}
