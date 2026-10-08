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

        // ---- dressing (phase D)
        int props = 0;
        if (hub.dressing != null) { foreach (Transform room in hub.dressing) { if (room.name != "Audio" && room.name != "Particles") props += room.childCount; } }
        sb.AppendLine($"  dressing props: {props} {(props > 0 ? "OK" : "NONE (the room was never dressed)")}");

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
            if (door.arrivalPoint != null && !ClearanceFree(door.arrivalPoint)) blocked.Add($"floor {door.floor} by {LastBlocker}");
        }
        if (hub.shopDoor != null && hub.shopDoor.arrivalPoint != null && !ClearanceFree(hub.shopDoor.arrivalPoint)) blocked.Add($"shop by {LastBlocker}");
        if (hub.shopDoor != null && hub.shopDoor.leafHinge != null)
        {
            foreach (Transform t in hub.shopDoor.leafHinge.GetComponentsInChildren<Transform>(true))
            {
                if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0) { staticLeaves++; break; }
            }
        }
        int wired = hub.wingDoors.Count(d => d != null);
        sb.AppendLine($"  wing doors: {wired} / 5 {(wired == 5 ? "OK" : "MISSING")}");
        sb.AppendLine($"  static moving leaves: {staticLeaves} {(staticLeaves == 0 ? "OK" : "FIX")}");
        sb.AppendLine(blocked.Count == 0 ? "  door clearance: OK" : "  door clearance blocked at: " + string.Join(", ", blocked));

        // ---- F93 story layer
        sb.Append(HubNavMesh.Audit(hub));
        List<string> storyMissing = StoryMissing(hub);
        sb.AppendLine(storyMissing.Count == 0 ? "  story layer (patients, records board, morgue, navmesh): OK" : "  story layer missing: " + string.Join(", ", storyMissing));

        // ---- walk graph
        sb.Append(AuditWalkGraph(hub));
        return sb.ToString();
    }

    /// <summary>The 1.5 x 2.5 x 1.5 m box in front of a wing door has nothing solid in it.</summary>
    public static string LastBlocker;

    private static bool ClearanceFree(Transform arrival)
    {
        Vector3 forward = arrival.forward;
        Vector3 centre = arrival.position - forward * 0.625f + Vector3.up * 1.3f;
        Collider[] hits = Physics.OverlapBox(centre, new Vector3(0.75f, 1.15f, 0.625f), arrival.rotation, ~0, QueryTriggerInteraction.Ignore);
        foreach (Collider c in hits)
        {
            if (c.GetComponentInParent<WingDoor>() != null || c.GetComponentInParent<ShopDoor>() != null) continue; // the door own leaf / frame
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
        List<(string, Vector3)> nodes = new List<(string, Vector3)> { ("entrance", hub.mainEntrance.position) };
        foreach (WingDoor d in hub.wingDoors) nodes.Add(($"wing {d.floor}", d.arrivalPoint.position));

        bool all = true;
        int[] ids = nodes.Select(n => grid.Nearest(n.Item2)).ToArray();
        sb.AppendLine($"  walk nodes: {grid.Count}");
        for (int i = 1; i < nodes.Count; i++)
        {
            all &= PathLine(grid, nodes, ids, 0, i, sb);
            if (i >= 2) all &= PathLine(grid, nodes, ids, i - 1, i, sb);
        }

        // The floor-5 key run: the Director's key must be reachable (the records room's pack door once sealed it). Measured from 1.5 m in
        // front of the hook, at floor height, so the nearest walk node is inside the room and not in the hall behind the board's wall.
        HubKey key = hub.life != null ? hub.life.GetComponentInChildren<HubKey>(true) : null;
        if (key != null)
        {
            Vector3 front = key.transform.position + key.transform.forward * 1.5f;
            front.y = hub.mainEntrance.position.y;
            nodes.Add(("director's key", front));
            int[] keyIds = nodes.Select(n => grid.Nearest(n.Item2)).ToArray();
            all &= PathLine(grid, nodes, keyIds, 0, nodes.Count - 1, sb);
        }
        all &= AuditNpcReach(hub, grid, ids[0], sb);
        sb.AppendLine($"  walk graph: {(all ? "complete OK" : "BROKEN")}");
        return sb.ToString();
    }

    /// <summary>
    /// F98: every NPC can be talked to and every mission item picked up. A standing spot 1.0 - 1.9 m from each (a lying NPC counts from
    /// the middle of its bed) must be on the walk graph and reachable from the main entrance.
    /// </summary>
    private static bool AuditNpcReach(AsylumHub hub, WalkGrid grid, int entranceNode, StringBuilder sb)
    {
        if (hub.life == null || entranceNode < 0) return true;
        grid.Flood(entranceNode, out int[] parent);
        bool ok = true;
        int checkedCount = 0;

        bool Reachable(Vector3 centre, float floorY, out float metres)
        {
            metres = 0f;
            // A standing spot 1.0 - 1.8 m away (the interactor reaches 2.4 m from eye height) that is on the graph and reachable.
            foreach (float radius in new[] { 1.0f, 1.3f, 1.6f, 1.9f })
            {
                for (int i = 0; i < 16; i++)
                {
                    float a = i * Mathf.PI * 2f / 16f;
                    Vector3 p = centre + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * radius;
                    p.y = floorY;
                    int node = grid.Nearest(p);
                    if (node < 0 || (grid.Pos(node) - p).sqrMagnitude > 0.3f * 0.3f || parent[node] < 0) continue;
                    metres = WalkGrid.Length(grid.PathTo(node, parent));
                    return true;
                }
            }
            return false;
        }

        foreach (HubNpc npc in hub.life.GetComponentsInChildren<HubNpc>(true))
        {
            Vector3 c = npc.anchor != null ? npc.anchor.position : npc.transform.position;
            float floorY = c.y; foreach (float f in new[] { 0.07f, 1.15f, 5.0f, 8.82f }) if (f <= c.y + 0.6f && c.y - f < 1.7f) floorY = f; // the storey floor under the NPC (a raycast would land on its collider)
            if (npc.lying) c = c - npc.anchor.forward * 0.95f;
            checkedCount++;
            if (Reachable(c, floorY, out float m)) sb.AppendLine($"  path npc {npc.name}: {m:F0} m OK");
            else { sb.AppendLine($"  path npc {npc.name}: no walkable spot within reach"); ok = false; }
        }
        foreach (HubMissionItem item in hub.life.GetComponentsInChildren<HubMissionItem>(true))
        {
            Vector3 c = item.transform.position;
            // Items sit on tables, so the storey floor under them is the nearest known floor at or below.
            float lowest = c.y;
            foreach (float f in new[] { 0.07f, 1.15f, 5.0f, 8.82f }) if (f <= c.y + 0.05f && c.y - f < 1.7f) lowest = f; // ascending: the highest floor within 1.7 m below wins
            checkedCount++;
            if (Reachable(c, lowest, out float m)) sb.AppendLine($"  path item {item.transform.parent.name}/{item.name}: {m:F0} m OK");
            else { sb.AppendLine($"  path item {item.transform.parent.name}/{item.name}: no walkable spot within reach"); ok = false; }
        }
        sb.AppendLine($"  npcs + items reachable ({checkedCount} checked), longest dialogue line {HubDialogue.LongestLine()} characters (limit 100): {(ok && HubDialogue.LongestLine() <= 100 ? "OK" : "BROKEN")}");
        return ok && HubDialogue.LongestLine() <= 100;
    }

    private static bool PathLine(WalkGrid grid, List<(string, Vector3)> nodes, int[] ids, int from, int to, StringBuilder sb)
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
