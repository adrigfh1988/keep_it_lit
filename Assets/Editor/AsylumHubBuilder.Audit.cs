using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

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
            if (r.GetComponent<TMPro.TMP_Text>() != null || r is ParticleSystemRenderer) continue; // text and particles have their own shaders
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
            if (door.arrivalPoint != null && !ClearanceFree(door)) blocked.Add($"floor {door.floor} by {LastBlocker}");
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
    public static string LastBlocker;

    private static bool ClearanceFree(WingDoor door)
    {
        Vector3 forward = door.arrivalPoint.forward;
        Vector3 centre = door.arrivalPoint.position - forward * 0.625f + Vector3.up * 1.3f;
        Collider[] hits = Physics.OverlapBox(centre, new Vector3(0.75f, 1.15f, 0.625f), door.arrivalPoint.rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            if (c.GetComponentInParent<WingDoor>() != null) continue; // the door own leaf / frame
            LastBlocker = c.name + " (" + c.transform.parent?.name + ")";
            return false;
        }
        return true;
    }

    // ---------------------------------------------------------------- walk graph

    /// <summary>
    /// Complete path (see WalkGrid) from the main entrance to every wing arrival point and from each wing to the next.
    /// Lines start with "path" and end in OK; the summary line says whether the whole graph is complete.
    /// </summary>
    private static string AuditWalkGraph(AsylumHub hub)
    {
        StringBuilder sb = new StringBuilder();
        if (hub.mainEntrance == null || hub.wingDoors.Any(d => d == null || d.arrivalPoint == null))
        {
            sb.AppendLine("  walk graph: parts missing, not checked");
            return sb.ToString();
        }

        WalkGrid grid = BuildWalkGrid(hub);
        List<(string, Transform)> nodes = new List<(string, Transform)> { ("entrance", hub.mainEntrance) };
        foreach (WingDoor d in hub.wingDoors) nodes.Add(($"wing {d.floor}", d.arrivalPoint));

        bool all = true;
        int[] ids = nodes.Select(n => grid.Nearest(n.Item2.position)).ToArray();
        sb.AppendLine($"  walk nodes: {grid.Count}");
        for (int i = 1; i < nodes.Count; i++)
        {
            all &= PathLine(grid, nodes, ids, 0, i, sb);
            if (i >= 2) all &= PathLine(grid, nodes, ids, i - 1, i, sb);
        }
        sb.AppendLine($"  walk graph: {(all ? "complete OK" : "BROKEN")}");
        return sb.ToString();
    }

    private static bool PathLine(WalkGrid grid, List<(string, Transform)> nodes, int[] ids, int from, int to, StringBuilder sb)
    {
        string label = $"{nodes[from].Item1} -> {nodes[to].Item1}";
        if (ids[from] < 0 || ids[to] < 0) { sb.AppendLine($"  path {label}: {(ids[from] < 0 ? "start" : "end")} is off the walkable area"); return false; }
        grid.Flood(ids[from], out int[] parent);
        if (parent[ids[to]] < 0) { sb.AppendLine($"  path {label}: no walkable route"); return false; }
        sb.AppendLine($"  path {label}: {WalkGrid.Length(grid.PathTo(ids[to], parent)):F0} m OK");
        return true;
    }

    /// <summary>The walkable polylines from the main entrance to each wing arrival, used to open the doors on a route.</summary>
    private static List<Vector3[]> RoutePaths(AsylumHub hub)
    {
        List<Vector3[]> routes = new List<Vector3[]>();
        WalkGrid grid = BuildWalkGrid(hub);
        int start = grid.Nearest(hub.mainEntrance.position);
        if (start < 0) return routes;
        grid.Flood(start, out int[] parent);
        foreach (WingDoor d in hub.wingDoors)
        {
            if (d == null || d.arrivalPoint == null) continue;
            int target = grid.Nearest(d.arrivalPoint.position);
            if (target >= 0 && parent[target] >= 0) routes.Add(grid.PathTo(target, parent));
        }
        return routes;
    }
}
