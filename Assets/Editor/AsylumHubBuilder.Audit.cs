using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

public static partial class AsylumHubBuilder
{
    // The reserved box plus a 5 m margin (world).
    private static readonly Bounds AllowedBox = new Bounds(new Vector3(100f, 7f, -58.5f), new Vector3(74f, 18f, 57f));

    /// <summary>The plan assertions: materials, lights, bounds, wings wired, door clearance, and a complete walk graph. Every check line ends in OK when it passes.</summary>
    public static string Audit(Transform root)
    {
        StringBuilder sb = new StringBuilder("Asylum audit:\n");

        // ---- materials
        int nonUrp = 0, badTransparent = 0;
        HashSet<Material> seen = new HashSet<Material>();
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material m in r.sharedMaterials)
            {
                if (m == null || !seen.Add(m)) continue;
                if (m.shader == null || !m.shader.name.StartsWith("Universal Render Pipeline/")) nonUrp++;
                else if (m.HasProperty("_Surface") && m.GetFloat("_Surface") > 0.5f && m.HasProperty("_BlendModePreserveSpecular") && m.GetFloat("_BlendModePreserveSpecular") != 0f) badTransparent++;
            }
        }
        sb.AppendLine($"  materials: {seen.Count} distinct, {nonUrp} not URP {(nonUrp == 0 ? "OK" : "FIX (AsylumMaterialUpgrade)")}");
        sb.AppendLine($"  transparent with preserve-specular: {badTransparent} {(badTransparent == 0 ? "OK" : "FIX")}");

        // ---- lights
        Light[] lights = root.GetComponentsInChildren<Light>(true).Where(l => l.enabled).ToArray();
        int baked = lights.Count(l => l.lightmapBakeType != LightmapBakeType.Realtime);
        int shadowed = lights.Count(l => l.shadows != LightShadows.None);
        sb.AppendLine($"  baked lights: {baked} {(baked == 0 ? "OK" : "FIX")}");
        sb.AppendLine($"  lights: {lights.Length} (cap {LightCap}) {(lights.Length <= LightCap ? "OK" : "TOO MANY")}");
        sb.AppendLine($"  shadowed lights: {shadowed} (cap {ShadowCap}) {(shadowed <= ShadowCap ? "OK" : "TOO MANY")}");

        // ---- bounds
        Bounds b = new Bounds(root.position, Vector3.zero);
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>(true)) { if (!(r is ParticleSystemRenderer)) b.Encapsulate(r.bounds); }
        bool inside = AllowedBox.Contains(b.min) && AllowedBox.Contains(b.max);
        sb.AppendLine($"  bounds: {b.min} .. {b.max}");
        sb.AppendLine($"  inside the reserved box: {(inside ? "OK" : "OUTSIDE")}");

        AsylumHub hub = root.GetComponent<AsylumHub>();
        sb.AppendLine($"  complete: {(hub != null && hub.IsComplete ? "OK" : "NO")}");
        if (hub == null) return sb.ToString();

        // ---- wings
        int staticLeaves = 0;
        List<string> blocked = new List<string>();
        foreach (WingDoor door in hub.wingDoors)
        {
            if (door == null) continue;
            if (door.leafHinge != null)
            {
                foreach (Transform t in door.leafHinge.GetComponentsInChildren<Transform>(true))
                {
                    if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0) { staticLeaves++; break; }
                }
            }
            if (door.arrivalPoint != null && !ClearanceFree(door)) blocked.Add($"floor {door.floor}");
        }
        int wired = hub.wingDoors.Count(d => d != null);
        sb.AppendLine($"  wing doors: {wired} / 5 {(wired == 5 ? "OK" : "MISSING")}");
        sb.AppendLine($"  static moving leaves: {staticLeaves} {(staticLeaves == 0 ? "OK" : "FIX")}");
        sb.AppendLine(blocked.Count == 0 ? "  door clearance: OK" : "  door clearance blocked at: " + string.Join(", ", blocked));

        // ---- walk graph
        sb.Append(AuditWalkGraph(hub));
        return sb.ToString();
    }

    /// <summary>The 1.5 x 2.5 x 1.5 m box in front of a wing door has nothing solid in it.</summary>
    private static bool ClearanceFree(WingDoor door)
    {
        Vector3 forward = door.arrivalPoint.forward;
        Vector3 centre = door.arrivalPoint.position - forward * 0.75f + Vector3.up * 1.3f;
        Collider[] hits = Physics.OverlapBox(centre, new Vector3(0.75f, 1.2f, 0.75f), door.arrivalPoint.rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            if (c.GetComponentInParent<WingDoor>() != null) continue; // the door's own leaf / frame
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- walk graph

    private static NavMeshDataInstance AddTemporaryNavMesh(AsylumHub hub)
    {
        Physics.SyncTransforms();
        // A copy of the Humanoid agent type's settings: NavMesh.CreateSettings() would add a new agent type to ProjectSettings.
        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = 0.28f;
        settings.agentHeight = 1.8f;
        settings.agentClimb = 0.25f;
        settings.agentSlope = 45f;
        settings.minRegionArea = 0.2f;

        Bounds area = new Bounds(hub.transform.position + new Vector3(0f, 5f, 0f), new Vector3(90f, 30f, 80f));
        List<NavMeshBuildSource> sources = new List<NavMeshBuildSource>();
        NavMeshBuilder.CollectSources(area, ~0, NavMeshCollectGeometry.PhysicsColliders, 0, new List<NavMeshBuildMarkup>(), sources);
        NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, area, area.center, Quaternion.identity);
        return NavMesh.AddNavMeshData(data);
    }

    /// <summary>
    /// A temporary editor NavMesh over the hub colliders (agent radius 0.28, height 1.8, step 0.25, slope 45) and a
    /// complete path from the main entrance to every wing arrival point and from each wing to the next.
    /// </summary>
    private static string AuditWalkGraph(AsylumHub hub)
    {
        StringBuilder sb = new StringBuilder();
        if (hub.mainEntrance == null || hub.wingDoors.Any(d => d == null || d.arrivalPoint == null))
        {
            sb.AppendLine("  walk graph: parts missing, not checked");
            return sb.ToString();
        }

        NavMeshDataInstance instance = default;
        try
        {
            instance = AddTemporaryNavMesh(hub);

            List<(string, Transform)> nodes = new List<(string, Transform)> { ("entrance", hub.mainEntrance) };
            foreach (WingDoor d in hub.wingDoors) nodes.Add(($"wing {d.floor}", d.arrivalPoint));

            bool all = true;
            for (int i = 1; i < nodes.Count; i++)
            {
                sb.AppendLine(PathLine(nodes[0], nodes[i], out bool ok));
                all &= ok;
                if (i >= 2)
                {
                    sb.AppendLine(PathLine(nodes[i - 1], nodes[i], out ok));
                    all &= ok;
                }
            }
            sb.AppendLine($"  walk graph: {(all ? "complete OK" : "BROKEN")}");
        }
        finally
        {
            if (instance.valid) NavMesh.RemoveNavMeshData(instance);
        }
        return sb.ToString();
    }

    private static string PathLine((string, Transform) a, (string, Transform) b, out bool ok)
    {
        ok = false;
        if (!NavMesh.SamplePosition(a.Item2.position, out NavMeshHit ha, 1.5f, NavMesh.AllAreas)) return $"  path {a.Item1} -> {b.Item1}: start is off the walkable area";
        if (!NavMesh.SamplePosition(b.Item2.position, out NavMeshHit hb, 1.5f, NavMesh.AllAreas)) return $"  path {a.Item1} -> {b.Item1}: end is off the walkable area";
        NavMeshPath path = new NavMeshPath();
        if (!NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) || path.status != NavMeshPathStatus.PathComplete)
        {
            return $"  path {a.Item1} -> {b.Item1}: {path.status}";
        }
        float length = 0f;
        for (int i = 1; i < path.corners.Length; i++) length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
        ok = true;
        return $"  path {a.Item1} -> {b.Item1}: {length:F0} m OK";
    }

    /// <summary>The corner chains from the main entrance to each wing arrival, used to open the doors on a route.</summary>
    private static List<Vector3[]> RoutePaths(AsylumHub hub)
    {
        List<Vector3[]> routes = new List<Vector3[]>();
        NavMeshDataInstance instance = default;
        try
        {
            instance = AddTemporaryNavMesh(hub);
            if (!NavMesh.SamplePosition(hub.mainEntrance.position, out NavMeshHit ha, 1.5f, NavMesh.AllAreas)) return routes;
            foreach (WingDoor d in hub.wingDoors)
            {
                if (d == null || d.arrivalPoint == null) continue;
                if (!NavMesh.SamplePosition(d.arrivalPoint.position, out NavMeshHit hb, 1.5f, NavMesh.AllAreas)) continue;
                NavMeshPath path = new NavMeshPath();
                if (NavMesh.CalculatePath(ha.position, hb.position, NavMesh.AllAreas, path) && path.status == NavMeshPathStatus.PathComplete) routes.Add(path.corners);
            }
        }
        finally
        {
            if (instance.valid) NavMesh.RemoveNavMeshData(instance);
        }
        return routes;
    }
}
