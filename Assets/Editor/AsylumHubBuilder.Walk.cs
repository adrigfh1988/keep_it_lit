using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The walk graph. The pack FBX meshes are not readable, so a temporary editor NavMesh cannot be baked from them; instead
/// this probes the physics scene directly: a 0.25 m grid, every upward-facing surface a capsule (radius 0.28, height 1.8)
/// can stand on, joined to the neighbours within a 0.32 m step (the player's CharacterController climbs 0.25).
/// Flood-fills from a point, so "complete path" means "the player can really walk it".
/// </summary>
public static partial class AsylumHubBuilder
{
    private sealed class WalkGrid
    {
        private const float Cell = 0.25f;
        private const float StepTolerance = 0.32f;
        private const float Radius = 0.28f;
        private const float Height = 1.8f;

        private readonly float _minX, _minZ;
        private readonly int _nx, _nz;
        private readonly List<Vector3> _pos = new List<Vector3>();
        private readonly List<int>[] _columns;

        public int Count => _pos.Count;
        public float Y(int node) => _pos[node].y;
        public Vector3 Pos(int node) => _pos[node];

        public WalkGrid(Bounds area)
        {
            _minX = area.min.x;
            _minZ = area.min.z;
            _nx = Mathf.CeilToInt(area.size.x / Cell);
            _nz = Mathf.CeilToInt(area.size.z / Cell);
            _columns = new List<int>[_nx * _nz];
            Physics.SyncTransforms();

            float top = area.max.y;
            for (int i = 0; i < _nx; i++)
            {
                for (int j = 0; j < _nz; j++)
                {
                    Vector3 origin = new Vector3(_minX + (i + 0.5f) * Cell, top, _minZ + (j + 0.5f) * Cell);
                    RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, area.size.y, ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Length == 0) continue;

                    List<int> column = null;
                    foreach (RaycastHit h in hits)
                    {
                        if (h.normal.y < 0.6f) continue; // walls and undersides
                        Vector3 p = h.point;
                        Vector3 a = p + Vector3.up * (Radius + 0.27f); // like the CharacterController: obstacles under the step offset (0.25) are climbed, not blocked
                        Vector3 b = p + Vector3.up * (Height - Radius);
                        if (Physics.CheckCapsule(a, b, Radius, ~0, QueryTriggerInteraction.Ignore)) continue;
                        if (column == null) column = _columns[i * _nz + j] = new List<int>(2);
                        column.Add(_pos.Count);
                        _pos.Add(p);
                    }
                }
            }
        }

        /// <summary>The node nearest to `p` (same column neighbourhood, within 0.7 m vertically), or -1.</summary>
        public int Nearest(Vector3 p)
        {
            int ci = Mathf.FloorToInt((p.x - _minX) / Cell), cj = Mathf.FloorToInt((p.z - _minZ) / Cell);
            int best = -1;
            float bestD = float.MaxValue;
            for (int r = 0; r <= 6 && best < 0; r++)
            {
                for (int di = -r; di <= r; di++)
                {
                    for (int dj = -r; dj <= r; dj++)
                    {
                        int i = ci + di, j = cj + dj;
                        if (i < 0 || j < 0 || i >= _nx || j >= _nz) continue;
                        List<int> col = _columns[i * _nz + j];
                        if (col == null) continue;
                        foreach (int n in col)
                        {
                            if (Mathf.Abs(_pos[n].y - p.y) > 0.7f) continue;
                            float d = (_pos[n] - p).sqrMagnitude;
                            if (d < bestD) { bestD = d; best = n; }
                        }
                    }
                }
            }
            return best;
        }

        /// <summary>Breadth-first from `start`. Fills parent (-1 = unreached, itself = start) and returns steps per node.</summary>
        public int[] Flood(int start, out int[] parent)
        {
            int[] steps = new int[_pos.Count];
            parent = new int[_pos.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = -1;
            Queue<int> queue = new Queue<int>();
            parent[start] = start;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int n = queue.Dequeue();
                Vector3 p = _pos[n];
                int ci = Mathf.FloorToInt((p.x - _minX) / Cell), cj = Mathf.FloorToInt((p.z - _minZ) / Cell);
                for (int di = -1; di <= 1; di++)
                {
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        if (di == 0 && dj == 0) continue;
                        int i = ci + di, j = cj + dj;
                        if (i < 0 || j < 0 || i >= _nx || j >= _nz) continue;
                        List<int> col = _columns[i * _nz + j];
                        if (col == null) continue;
                        foreach (int m in col)
                        {
                            if (parent[m] >= 0 || Mathf.Abs(_pos[m].y - p.y) > StepTolerance) continue;
                            parent[m] = n;
                            steps[m] = steps[n] + 1;
                            queue.Enqueue(m);
                        }
                    }
                }
            }
            return steps;
        }

        public Vector3[] PathTo(int target, int[] parent)
        {
            List<Vector3> path = new List<Vector3>();
            int n = target;
            while (parent[n] != n) { path.Add(_pos[n]); n = parent[n]; }
            path.Add(_pos[n]);
            path.Reverse();
            return path.ToArray();
        }

        public static float Length(Vector3[] path)
        {
            float l = 0f;
            for (int i = 1; i < path.Length; i++) l += Vector3.Distance(path[i - 1], path[i]);
            return l;
        }
    }

    /// <summary>Dev diagnostics: how far the flood from `from` reaches, by storey, with every door leaf passable.</summary>
    public static string DebugWalk(AsylumHub hub, Vector3 from, bool doorsPassable = true)
    {
        _building = hub.building;
        List<PackDoor> doors = FindPackDoors();
        if (doorsPassable) SetLeafColliders(doors, false);
        try
        {
            WalkGrid grid = BuildWalkGrid(hub);
            int s = grid.Nearest(from);
            if (s < 0) return "no start node near " + from;
            grid.Flood(s, out int[] parent);
            int[] band = new int[3];
            float maxY = -99f, minY = 99f; Vector3 lo = Vector3.one * 1e9f, hi = Vector3.one * -1e9f;
            for (int i = 0; i < parent.Length; i++)
            {
                if (parent[i] < 0) continue;
                float y = grid.Y(i);
                band[y < 3f ? 0 : y < 7f ? 1 : 2]++;
                maxY = Mathf.Max(maxY, y); minY = Mathf.Min(minY, y); lo = Vector3.Min(lo, grid.Pos(i)); hi = Vector3.Max(hi, grid.Pos(i));
            }
            return $"nodes {grid.Count}; reached basement {band[0]} ground {band[1]} upper {band[2]} (y {minY:F1}..{maxY:F1}) x {lo.x:F0}..{hi.x:F0} z {lo.z:F0}..{hi.z:F0}";
        }
        finally { SetLeafColliders(doors, true); }
    }

    /// <summary>Walkable polylines from the main entrance to each wing arrival, doors as they are now (dev / Play tests).</summary>
    public static List<Vector3[]> WalkRoutes(AsylumHub hub) { _building = hub.building; return RoutePaths(hub); }

    private static WalkGrid BuildWalkGrid(AsylumHub hub)
    {
        return new WalkGrid(new Bounds(hub.transform.position + new Vector3(0f, 2f, -8.5f), new Vector3(70f, 18f, 56f)));
    }
}
