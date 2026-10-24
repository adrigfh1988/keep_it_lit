using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure-C# grid topology, coordinate conversion, wall state, and BFS reachability helper for the
/// procedural maze. Owned by <see cref="MazeGenerator"/> and exposed via <see cref="MazeGenerator.Grid"/>.
/// </summary>
public sealed class MazeGrid
{
    public struct WallSnapshot
    {
        public bool[,] N, E, S, W;
    }

    public int Width { get; private set; }
    public int Height { get; private set; }
    public float CellSize { get; set; }
    public float WallHeight { get; set; }
    public Vector3 Origin { get; set; }
    public float FloorTop { get; set; }

    public bool[,] WallN { get; private set; }
    public bool[,] WallE { get; private set; }
    public bool[,] WallS { get; private set; }
    public bool[,] WallW { get; private set; }
    public bool[,] Void { get; private set; }
    public int[,] Distance { get; private set; }

    public Vector2Int StartCell { get; set; }
    public Vector2Int FarthestCell { get; set; }
    public int MaxDistance { get; set; }
    public int PlayableCellCount { get; set; }

    private readonly List<Vector3> _cellCenters = new List<Vector3>();
    public IReadOnlyList<Vector3> CellCenters => _cellCenters;
    public List<Vector3> CellCentersMutable => _cellCenters;

    public MazeGrid(int width, int height, float cellSize, float wallHeight, Vector3 origin, float floorTop)
    {
        Reset(width, height, cellSize, wallHeight, origin, floorTop);
    }

    public void Reset(int width, int height, float cellSize, float wallHeight, Vector3 origin, float floorTop)
    {
        Width = width;
        Height = height;
        CellSize = cellSize;
        WallHeight = wallHeight;
        Origin = origin;
        FloorTop = floorTop;

        WallN = new bool[width, height];
        WallE = new bool[width, height];
        WallS = new bool[width, height];
        WallW = new bool[width, height];
        Void = new bool[width, height];
        Distance = new int[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int y = 0; y < height; y++)
            {
                WallN[x, y] = true;
                WallE[x, y] = true;
                WallS[x, y] = true;
                WallW[x, y] = true;
                Distance[x, y] = -1;
            }
        }

        StartCell = Vector2Int.zero;
        FarthestCell = Vector2Int.zero;
        MaxDistance = 0;
        PlayableCellCount = width * height;
        _cellCenters.Clear();
    }

    public bool InBounds(int x, int y) => x >= 0 && x < Width && y >= 0 && y < Height;

    public bool IsVoid(int x, int y) => !InBounds(x, y) || (Void != null && Void[x, y]);

    public int GetDistance(int x, int y) => InBounds(x, y) && Distance != null ? Distance[x, y] : -1;

    public Vector3 CellCenter(int x, int y)
    {
        return new Vector3(
            Origin.x + (x + 0.5f) * CellSize,
            FloorTop,
            Origin.z + (y + 0.5f) * CellSize);
    }

    public bool TryWorldToCell(Vector3 world, out int x, out int y)
    {
        x = Mathf.FloorToInt((world.x - Origin.x) / CellSize);
        y = Mathf.FloorToInt((world.z - Origin.z) / CellSize);
        return x >= 0 && x < Width && y >= 0 && y < Height && (Void == null || !Void[x, y]);
    }

    public bool IsDeadEndAt(Vector3 world, out Vector3 cellCenter)
    {
        cellCenter = default;
        if (WallN == null || !TryWorldToCell(world, out int x, out int y)) return false;

        cellCenter = CellCenter(x, y);
        return WallCount(x, y) == 3;
    }

    public int WallCount(int x, int y)
    {
        return (WallN[x, y] ? 1 : 0) + (WallE[x, y] ? 1 : 0) + (WallS[x, y] ? 1 : 0) + (WallW[x, y] ? 1 : 0);
    }

    public bool IsWallClosed(int x, int y, int dir)
    {
        switch (dir)
        {
            case 0: return WallN[x, y];
            case 1: return WallE[x, y];
            case 2: return WallS[x, y];
            default: return WallW[x, y];
        }
    }

    public bool Neighbour(int x, int y, int dir, out int nx, out int ny)
    {
        nx = x + (dir == 1 ? 1 : dir == 3 ? -1 : 0);
        ny = y + (dir == 0 ? 1 : dir == 2 ? -1 : 0);
        return nx >= 0 && nx < Width && ny >= 0 && ny < Height && (Void == null || !Void[nx, ny]);
    }

    public void OpenWall(int x, int y, int nx, int ny)
    {
        int dx = nx - x;
        int dy = ny - y;
        if (dx == 1) { WallE[x, y] = false; WallW[nx, ny] = false; }
        else if (dx == -1) { WallW[x, y] = false; WallE[nx, ny] = false; }
        else if (dy == 1) { WallN[x, y] = false; WallS[nx, ny] = false; }
        else if (dy == -1) { WallS[x, y] = false; WallN[nx, ny] = false; }
    }

    public void SetWall(int x, int y, int dir, bool closed)
    {
        if (!InBounds(x, y)) return;
        switch (dir)
        {
            case 0:
                WallN[x, y] = closed;
                if (InBounds(x, y + 1)) WallS[x, y + 1] = closed;
                break;
            case 1:
                WallE[x, y] = closed;
                if (InBounds(x + 1, y)) WallW[x + 1, y] = closed;
                break;
            case 2:
                WallS[x, y] = closed;
                if (InBounds(x, y - 1)) WallN[x, y - 1] = closed;
                break;
            default:
                WallW[x, y] = closed;
                if (InBounds(x - 1, y)) WallE[x - 1, y] = closed;
                break;
        }
    }

    public WallSnapshot TakeWallSnapshot()
    {
        return new WallSnapshot
        {
            N = (bool[,])WallN.Clone(),
            E = (bool[,])WallE.Clone(),
            S = (bool[,])WallS.Clone(),
            W = (bool[,])WallW.Clone(),
        };
    }

    public void RestoreWalls(WallSnapshot s)
    {
        Array.Copy(s.N, WallN, s.N.Length);
        Array.Copy(s.E, WallE, s.E.Length);
        Array.Copy(s.S, WallS, s.S.Length);
        Array.Copy(s.W, WallW, s.W.Length);
    }

    public void ComputeDistances(Vector2Int start)
    {
        for (int x = 0; x < Width; x++)
        {
            for (int y = 0; y < Height; y++)
            {
                Distance[x, y] = -1;
            }
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        Distance[start.x, start.y] = 0;
        queue.Enqueue(start);
        FarthestCell = start;
        MaxDistance = 0;

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            int next = Distance[c.x, c.y] + 1;
            if (!WallN[c.x, c.y]) TryVisit(queue, c.x, c.y + 1, next);
            if (!WallE[c.x, c.y]) TryVisit(queue, c.x + 1, c.y, next);
            if (!WallS[c.x, c.y]) TryVisit(queue, c.x, c.y - 1, next);
            if (!WallW[c.x, c.y]) TryVisit(queue, c.x - 1, c.y, next);
        }
    }

    private void TryVisit(Queue<Vector2Int> queue, int x, int y, int dist)
    {
        if (x < 0 || x >= Width || y < 0 || y >= Height) return;
        if (Void != null && Void[x, y]) return;
        if (Distance[x, y] >= 0) return;
        Distance[x, y] = dist;
        if (dist > MaxDistance)
        {
            MaxDistance = dist;
            FarthestCell = new Vector2Int(x, y);
        }
        queue.Enqueue(new Vector2Int(x, y));
    }

    public void CacheCellCenters()
    {
        _cellCenters.Clear();
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Void != null && Void[x, y]) continue;
                _cellCenters.Add(CellCenter(x, y));
            }
        }
    }

    public HashSet<Vector2Int> ReachableCells(Vector2Int start)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { start };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(c.x, c.y, dir)) continue;
                if (!Neighbour(c.x, c.y, dir, out int nx, out int ny)) continue;
                Vector2Int n = new Vector2Int(nx, ny);
                if (visited.Add(n)) queue.Enqueue(n);
            }
        }
        return visited;
    }

    public HashSet<Vector2Int> ReachableCellsAvoiding(Vector2Int start, HashSet<Vector2Int> forbidden)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
        if (forbidden.Contains(start)) return visited;
        visited.Add(start);
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(start);
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(c.x, c.y, dir)) continue;
                if (!Neighbour(c.x, c.y, dir, out int nx, out int ny)) continue;
                Vector2Int n = new Vector2Int(nx, ny);
                if (!forbidden.Contains(n) && visited.Add(n)) queue.Enqueue(n);
            }
        }
        return visited;
    }

    public HashSet<Vector2Int> BfsWithin(Vector2Int origin, int maxSteps)
    {
        Dictionary<Vector2Int, int> dist = new Dictionary<Vector2Int, int> { [origin] = 0 };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            int d = dist[c];
            if (d >= maxSteps) continue;
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(c.x, c.y, dir)) continue;
                if (!Neighbour(c.x, c.y, dir, out int nx, out int ny)) continue;
                Vector2Int n = new Vector2Int(nx, ny);
                if (dist.ContainsKey(n)) continue;
                dist[n] = d + 1;
                queue.Enqueue(n);
            }
        }
        return new HashSet<Vector2Int>(dist.Keys);
    }

    public int StepsFrom(Vector2Int origin, Vector2Int target)
    {
        if (origin == target) return 0;

        Dictionary<Vector2Int, int> dist = new Dictionary<Vector2Int, int> { [origin] = 0 };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(origin);
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            int next = dist[c] + 1;
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(c.x, c.y, dir)) continue;
                if (!Neighbour(c.x, c.y, dir, out int nx, out int ny)) continue;
                Vector2Int n = new Vector2Int(nx, ny);
                if (n == target) return next;
                if (dist.ContainsKey(n)) continue;
                dist[n] = next;
                queue.Enqueue(n);
            }
        }

        return int.MaxValue;
    }

    public Vector2Int FindCellAtLeastSteps(Vector2Int origin, int minSteps)
    {
        Dictionary<Vector2Int, int> dist = new Dictionary<Vector2Int, int> { [origin] = 0 };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(origin);
        Vector2Int farthest = origin;
        int maxDist = 0;

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            int d = dist[c];
            if (d >= minSteps) return c;
            if (d > maxDist) { maxDist = d; farthest = c; }
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(c.x, c.y, dir)) continue;
                if (!Neighbour(c.x, c.y, dir, out int nx, out int ny)) continue;
                Vector2Int n = new Vector2Int(nx, ny);
                if (dist.ContainsKey(n)) continue;
                dist[n] = d + 1;
                queue.Enqueue(n);
            }
        }

        return farthest;
    }

    public void BfsParents(Vector2Int from, Dictionary<Vector2Int, Vector2Int> parents)
    {
        parents.Clear();
        parents[from] = from;
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(c.x, c.y, dir)) continue;
                if (!Neighbour(c.x, c.y, dir, out int nx, out int ny)) continue;
                Vector2Int n = new Vector2Int(nx, ny);
                if (parents.ContainsKey(n)) continue;
                parents[n] = c;
                queue.Enqueue(n);
            }
        }
    }
}
