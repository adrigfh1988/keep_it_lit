using System.Collections.Generic;
using UnityEngine;

public partial class MazeGenerator
{
    /// <summary>Floor-level world centre of one cell.</summary>
    public Vector3 CellCenter(int x, int z)
    {
        return new Vector3(
            origin.x + (x + 0.5f) * cellSize,
            FloorTop,
            origin.z + (z + 0.5f) * cellSize);
    }

    /// <summary>The cell a world position falls in. False outside the maze (or before it has been generated).</summary>
    public bool TryWorldToCell(Vector3 worldPosition, out Vector2Int cell)
    {
        cell = new Vector2Int(
            Mathf.FloorToInt((worldPosition.x - origin.x) / cellSize),
            Mathf.FloorToInt((worldPosition.z - origin.z) / cellSize));
        return _wallN != null && cell.x >= 0 && cell.x < width && cell.y >= 0 && cell.y < height && !IsVoid(cell);
    }

    /// <summary>
    /// F103 A6: true if the position is in a cell with three logical walls (a dead end, an opened vault, a bait dead end;
    /// doors are ignored). <paramref name="cellCenter"/> is that cell's floor-level centre, so a caller can remember which one.
    /// </summary>
    public bool IsDeadEndAt(Vector3 worldPosition, out Vector3 cellCenter)
    {
        cellCenter = default;
        if (!TryWorldToCell(worldPosition, out Vector2Int cell) || WallCount(cell.x, cell.y) != 3) return false;

        cellCenter = CellCenter(cell.x, cell.y);
        return true;
    }

    // ---------------------------------------------------------------- generation

    private void Generate(int usedSeed)
    {
        _grid = new MazeGrid(width, height, cellSize, wallHeight, origin, FloorTop);
        _wallN = _grid.WallN;
        _wallE = _grid.WallE;
        _wallS = _grid.WallS;
        _wallW = _grid.WallW;
        _void = _grid.Void;
        _distance = _grid.Distance;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                _wallN[x, z] = true;
                _wallE[x, z] = true;
                _wallS[x, z] = true;
                _wallW[x, z] = true;
            }
        }

        // Its own RNG so the maze layout never perturbs UnityEngine.Random (used by the pickup VFX).
        System.Random rng = new System.Random(usedSeed);

        bool[,] visited = new bool[width, height];
        ApplyVoidMask(visited);
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        Vector2Int current = _startCell;
        visited[current.x, current.y] = true;
        stack.Push(current);

        // Recursive backtracker (iterative): always yields a perfect maze, so every cell is reachable.
        List<int> candidates = new List<int>(4);
        int lastDir = -1;
        while (stack.Count > 0)
        {
            current = stack.Peek();
            candidates.Clear();

            if (current.y + 1 < height && !visited[current.x, current.y + 1]) candidates.Add(0); // north
            if (current.x + 1 < width && !visited[current.x + 1, current.y]) candidates.Add(1);  // east
            if (current.y - 1 >= 0 && !visited[current.x, current.y - 1]) candidates.Add(2);     // south
            if (current.x - 1 >= 0 && !visited[current.x - 1, current.y]) candidates.Add(3);     // west

            if (candidates.Count == 0)
            {
                stack.Pop();
                lastDir = -1;
                continue;
            }

            int direction = PickShapeCarveDirection(current, candidates, lastDir, rng);
            lastDir = direction;
            Vector2Int next = current;
            switch (direction)
            {
                case 0:
                    next.y += 1;
                    _wallN[current.x, current.y] = false;
                    _wallS[next.x, next.y] = false;
                    break;
                case 1:
                    next.x += 1;
                    _wallE[current.x, current.y] = false;
                    _wallW[next.x, next.y] = false;
                    break;
                case 2:
                    next.y -= 1;
                    _wallS[current.x, current.y] = false;
                    _wallN[next.x, next.y] = false;
                    break;
                default:
                    next.x -= 1;
                    _wallW[current.x, current.y] = false;
                    _wallE[next.x, next.y] = false;
                    break;
            }

            visited[next.x, next.y] = true;
            stack.Push(next);
        }

        _keptDeadEnds.Clear();
        if (removeDeadEnds) RemoveDeadEnds(rng, new System.Random(usedSeed + 20));
        CarveShapeAvenues(usedSeed); // F125 D2 (+28): shape-defining avenues, no-op on Rectangle

        ComputeDistances();

        // F105 C2/C3: topology and geometry choices that must be final before any placement pass. +21 rooms/gallery, +22 squeeze;
        // both draw nothing (and the maze stays what it was) with their flag off, on floor 1 and on a floor without a theme.
        BuildRoomsAndGallery(usedSeed);
        ChooseSqueeze(usedSeed);
        ChooseLoop(usedSeed); // F105 C4 (+23): the loop's entrance on the maze edge, picked once rooms, gallery and squeeze are final
        ChooseGates(usedSeed); // F111 (+26): the exit gates, picked after the loop so they keep clear of its entrance and side
    }

    private void ApplyVoidMask(bool[,] visited)
    {
        if (_grid != null) _void = _grid.Void;
        else _void = new bool[width, height];
        for (int x = 0; x < width; x++)
            for (int z = 0; z < height; z++)
                _void[x, z] = false;

        _startCell = Vector2Int.zero;
        FloorProfile.FloorShape shape = _profile != null ? _profile.Shape : FloorProfile.FloorShape.Rectangle;

        switch (shape)
        {
            case FloorProfile.FloorShape.Ring:
            {
                int vw = Mathf.Clamp(width - 6, 2, 6);
                int vh = Mathf.Clamp(height - 6, 2, 5);
                int x0 = (width - vw) / 2;
                int z0 = (height - vh) / 2;
                for (int x = x0; x < x0 + vw; x++)
                    for (int z = z0; z < z0 + vh; z++)
                        _void[x, z] = true;
                _startCell = Vector2Int.zero;
                break;
            }
            case FloorProfile.FloorShape.TwinWings:
            {
                int vw = Mathf.Clamp(width - 6, 2, 4);
                int nh = Mathf.Clamp((height - 3) / 2, 2, 4);
                int x0 = (width - vw) / 2;
                for (int x = x0; x < x0 + vw; x++)
                {
                    for (int z = 0; z < nh; z++) _void[x, z] = true;
                    for (int z = height - nh; z < height; z++) _void[x, z] = true;
                }
                _startCell = Vector2Int.zero;
                break;
            }
            case FloorProfile.FloorShape.Cross:
            {
                int cw = Mathf.Clamp((width - 4) / 2, 2, 4);
                int ch = Mathf.Clamp((height - 4) / 2, 2, 3);
                for (int x = 0; x < width; x++)
                {
                    bool xCorner = x < cw || x >= width - cw;
                    if (!xCorner) continue;
                    for (int z = 0; z < height; z++)
                    {
                        if (z < ch || z >= height - ch)
                            _void[x, z] = true;
                    }
                }
                _startCell = new Vector2Int(width / 2, height / 2);
                _void[_startCell.x, _startCell.y] = false;
                break;
            }
            case FloorProfile.FloorShape.LChasm:
            {
                int cw = Mathf.Clamp(width / 2 - 1, 3, 6);
                int ch = Mathf.Clamp(height / 2 - 1, 3, 5);
                int variant = _profile != null && _profile.ShapeVariant >= 1 && _profile.ShapeVariant <= 3 ? _profile.ShapeVariant : 1;
                int xStart = (variant == 2) ? 0 : width - cw; // 1=NE, 2=NW, 3=SE
                int zStart = (variant == 3) ? 0 : height - ch;
                for (int x = xStart; x < xStart + cw; x++)
                    for (int z = zStart; z < zStart + ch; z++)
                        _void[x, z] = true;
                _startCell = Vector2Int.zero;
                _void[0, 0] = false;
                break;
            }
            default:
                _startCell = Vector2Int.zero;
                break;
        }

        _playableCellCount = 0;
        for (int x = 0; x < width; x++)
            for (int z = 0; z < height; z++)
            {
                if (_void[x, z]) visited[x, z] = true;
                else _playableCellCount++;
            }

        if (_grid != null)
        {
            _grid.StartCell = _startCell;
            _grid.PlayableCellCount = _playableCellCount;
        }
    }

    private int WallCount(int x, int z)
    {
        return (_wallN[x, z] ? 1 : 0) + (_wallE[x, z] ? 1 : 0) + (_wallS[x, z] ? 1 : 0) + (_wallW[x, z] ? 1 : 0);
    }

    /// <summary>
    /// Turns the perfect maze into a braided one: every cell with three walls gets one more wall
    /// knocked through, so there are no dead ends and the maze is full of loops. Prefers a wall whose
    /// far side is also a dead end, which fixes two at once and keeps the number of new loops low.
    /// Only interior walls are ever opened, so the outer boundary stays sealed.
    ///
    /// F103 A10: a fraction of the dead ends (FloorProfile.DeadEndKeep) survives, so the maze keeps its most
    /// frightening shape. The keep decisions use <paramref name="keepRng"/> (the +20 stream) and the loop draws from
    /// <paramref name="rng"/> (the carve stream) exactly as it always did: with nothing kept - floor 1, the toggle
    /// off - it draws the identical sequence, so a seed still produces a byte-identical maze.
    /// </summary>
    private void RemoveDeadEnds(System.Random rng, System.Random keepRng)
    {
        List<Vector2Int> deadEnds = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (IsVoid(x, z)) continue;
                if (WallCount(x, z) == 3) deadEnds.Add(new Vector2Int(x, z));
            }
        }

        Shuffle(deadEnds, rng);

        HashSet<Vector2Int> kept = ChooseDeadEndsToKeep(deadEnds, keepRng);

        List<int> options = new List<int>(4);
        foreach (Vector2Int cell in deadEnds)
        {
            if (kept.Contains(cell)) continue;

            // An earlier removal may already have opened this one up
            if (WallCount(cell.x, cell.y) != 3) continue;

            options.Clear();
            int preferred = -1;
            int intoKept = -1;
            for (int direction = 0; direction < 4; direction++)
            {
                if (!IsWallClosed(cell.x, cell.y, direction)) continue;

                Vector2Int neighbour = Neighbour(cell, direction);
                if (neighbour.x < 0 || neighbour.x >= width || neighbour.y < 0 || neighbour.y >= height || IsVoid(neighbour)) continue;
                // F103 A10: a kept dead end must not be opened from the other side either...
                if (kept.Contains(neighbour))
                {
                    if (intoKept < 0) intoKept = direction;
                    continue;
                }

                options.Add(direction);
                if (preferred < 0 && WallCount(neighbour.x, neighbour.y) == 3) preferred = direction;
            }

            // A dead end has three closed walls and at most two of them are on the boundary
            if (options.Count == 0)
            {
                // ...unless that is this cell's only way out: then the kept one gives way (TrimKeptDeadEnds drops it), rather than
                // leaving this cell a dead end of any depth that nothing tracks. No rng draw, so the nothing-kept sequence is unchanged.
                if (intoKept >= 0) OpenWall(cell, intoKept);
                continue;
            }

            int chosen = preferred >= 0 ? preferred : options[rng.Next(options.Count)];
            OpenWall(cell, chosen);
        }

        TrimKeptDeadEnds(kept, keepRng);
        if (_keptDeadEnds.Count > 0)
        {
            terror.Log($"Kept {_keptDeadEnds.Count} dead ends (cells {string.Join(", ", _keptDeadEnds)})", this);
        }
    }

    /// <summary>F103 A10: which of the dead ends survive braiding - a seeded fraction of those far enough from the start. Empty when the feature is off or the floor keeps none.</summary>
    private HashSet<Vector2Int> ChooseDeadEndsToKeep(List<Vector2Int> deadEnds, System.Random keepRng)
    {
        HashSet<Vector2Int> kept = new HashSet<Vector2Int>();
        float keep = AdjustShapeDeadEndKeep(terror.baitDeadEnds && _profile != null ? _profile.DeadEndKeep : 0f);
        if (keep <= 0f) return kept;

        List<Vector2Int> candidates = new List<Vector2Int>();
        foreach (Vector2Int cell in deadEnds)
        {
            if (Mathf.Abs(cell.x - _startCell.x) + Mathf.Abs(cell.y - _startCell.y) >= deadEndStartClearance) candidates.Add(cell);
        }

        Shuffle(candidates, keepRng);
        int count = Mathf.RoundToInt(candidates.Count * keep);
        for (int i = 0; i < count; i++) kept.Add(candidates[i]);

        return kept;
    }

    /// <summary>
    /// F103 A10: of the dead ends left standing, only short pockets stay. One deeper than maxDeadEndDepth is opened after all
    /// (a wall chosen with keepRng), and what survives goes into _keptDeadEnds. Processed in a fixed order, not the HashSet's.
    /// </summary>
    private void TrimKeptDeadEnds(HashSet<Vector2Int> kept, System.Random keepRng)
    {
        if (kept.Count == 0) return;

        List<Vector2Int> ordered = new List<Vector2Int>(kept);
        ordered.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));

        List<int> interiorWalls = new List<int>(4);
        foreach (Vector2Int cell in ordered)
        {
            if (IsVoid(cell)) continue;
            // Defensive: a neighbouring trim may already have opened this one.
            if (WallCount(cell.x, cell.y) != 3) continue;

            if (DeadEndDepth(cell) <= maxDeadEndDepth)
            {
                _keptDeadEnds.Add(cell);
                continue;
            }

            interiorWalls.Clear();
            for (int direction = 0; direction < 4; direction++)
            {
                Vector2Int neighbour = Neighbour(cell, direction);
                if (IsWallClosed(cell.x, cell.y, direction) && neighbour.x >= 0 && neighbour.x < width && neighbour.y >= 0 && neighbour.y < height && !IsVoid(neighbour))
                {
                    interiorWalls.Add(direction);
                }
            }

            if (interiorWalls.Count > 0) OpenWall(cell, interiorWalls[keepRng.Next(interiorWalls.Count)]);
        }

        // Opening a deep pocket can break into one already accepted above, so keep only cells that are still dead ends.
        _keptDeadEnds.RemoveAll(cell => WallCount(cell.x, cell.y) != 3);
    }

    /// <summary>Steps from a dead end to the first cell with three or more openings (a junction), walking through the 2-opening cells between. Capped at the cell count.</summary>
    private int DeadEndDepth(Vector2Int deadEnd)
    {
        Vector2Int previous = deadEnd;
        Vector2Int current = MouthOf(deadEnd);
        int steps = 1;

        while (steps < width * height && 4 - WallCount(current.x, current.y) == 2)
        {
            Vector2Int next = current;
            for (int direction = 0; direction < 4; direction++)
            {
                if (IsWallClosed(current.x, current.y, direction)) continue;
                Vector2Int candidate = Neighbour(current, direction);
                if (candidate != previous) next = candidate;
            }

            previous = current;
            current = next;
            steps++;
        }

        return steps;
    }

    /// <summary>F103 A10: the cell on the far side of a dead end's one open wall - where the player has to come back out.</summary>
    private Vector2Int MouthOf(Vector2Int deadEnd)
    {
        for (int direction = 0; direction < 4; direction++)
        {
            if (!IsWallClosed(deadEnd.x, deadEnd.y, direction)) return Neighbour(deadEnd, direction);
        }

        return deadEnd;
    }

    /// <summary>
    /// F103 A10: the first FloorProfile.DeadEndBaitStars kept dead ends become bait: SpawnStars puts a star at the end of each.
    /// Runs right after Generate (distances exist). Skips the hunter's cell and any whose mouth is the hunter's cell or the start;
    /// a mouth with no wall at all (an open junction) is skipped too, as no locker can stand there. Empty with superEasyMode.
    /// </summary>
    private void ChooseBaitDeadEnds()
    {
        _baitDeadEnds.Clear();
        int wanted = terror.baitDeadEnds && _profile != null && !superEasyMode ? _profile.DeadEndBaitStars : 0;
        if (wanted <= 0) return;

        Vector2Int aiCell = FarthestCell();
        foreach (Vector2Int cell in _keptDeadEnds)
        {
            if (_baitDeadEnds.Count >= wanted) break;
            if (cell == aiCell) continue;

            Vector2Int mouth = MouthOf(cell);
            if (mouth == aiCell || mouth == _startCell || WallCount(mouth.x, mouth.y) < 1) continue;
            _baitDeadEnds.Add(cell);
        }
    }

    // 0 = north, 1 = east, 2 = south, 3 = west, matching the carving code above
    private bool IsWallClosed(int x, int z, int direction)
    {
        switch (direction)
        {
            case 0: return _wallN[x, z];
            case 1: return _wallE[x, z];
            case 2: return _wallS[x, z];
            default: return _wallW[x, z];
        }
    }

    private static Vector2Int Neighbour(Vector2Int cell, int direction)
    {
        switch (direction)
        {
            case 0: return new Vector2Int(cell.x, cell.y + 1);
            case 1: return new Vector2Int(cell.x + 1, cell.y);
            case 2: return new Vector2Int(cell.x, cell.y - 1);
            default: return new Vector2Int(cell.x - 1, cell.y);
        }
    }

    private void OpenWall(Vector2Int cell, int direction)
    {
        Vector2Int other = Neighbour(cell, direction);
        switch (direction)
        {
            case 0: _wallN[cell.x, cell.y] = false; _wallS[other.x, other.y] = false; break;
            case 1: _wallE[cell.x, cell.y] = false; _wallW[other.x, other.y] = false; break;
            case 2: _wallS[cell.x, cell.y] = false; _wallN[other.x, other.y] = false; break;
            default: _wallW[cell.x, cell.y] = false; _wallE[other.x, other.y] = false; break;
        }
    }

    private void ComputeDistances()
    {
        if (_grid != null) _distance = _grid.Distance;
        if (_distance == null || _distance.GetLength(0) != width || _distance.GetLength(1) != height)
            _distance = new int[width, height];

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                _distance[x, z] = -1;
            }
        }

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        _distance[_startCell.x, _startCell.y] = 0;
        queue.Enqueue(_startCell);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int next = _distance[cell.x, cell.y] + 1;

            if (!_wallN[cell.x, cell.y]) TryVisit(cell.x, cell.y + 1, next, queue);
            if (!_wallE[cell.x, cell.y]) TryVisit(cell.x + 1, cell.y, next, queue);
            if (!_wallS[cell.x, cell.y]) TryVisit(cell.x, cell.y - 1, next, queue);
            if (!_wallW[cell.x, cell.y]) TryVisit(cell.x - 1, cell.y, next, queue);
        }

        if (_grid != null)
        {
            _grid.FarthestCell = FarthestCell();
            _grid.MaxDistance = _distance[_grid.FarthestCell.x, _grid.FarthestCell.y];
        }
    }

    private void TryVisit(int x, int z, int distance, Queue<Vector2Int> queue)
    {
        if (x < 0 || x >= width || z < 0 || z >= height || IsVoid(x, z)) return;
        if (_distance[x, z] >= 0) return;

        _distance[x, z] = distance;
        queue.Enqueue(new Vector2Int(x, z));
    }

    private void CacheCellCenters()
    {
        _cellCenters.Clear();
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                if (IsVoid(x, z)) continue;
                _cellCenters.Add(CellCenter(x, z));
            }
        }
        if (_grid != null) _grid.CacheCellCenters();
    }

    /// <summary>BFS over open passages from `from`, with `blocked` (if given) treated as closed regardless of what it actually is - the security-door test: does blocking this one passage actually cut the maze in two.</summary>
    private HashSet<Vector2Int> ReachableCells(Vector2Int from, Passage? blocked)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { from };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (blocked.HasValue && IsSamePassage(cell, dir, blocked.Value)) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        return visited;
    }

    /// <summary>Step distances from `from`, expanding only within `region` - used to keep a button at least 3 BFS steps from its door without wandering onto the locked side.</summary>
    private Dictionary<Vector2Int, int> BfsWithin(Vector2Int from, HashSet<Vector2Int> region)
    {
        Dictionary<Vector2Int, int> distance = new Dictionary<Vector2Int, int> { [from] = 0 };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            int next = distance[cell] + 1;
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (!region.Contains(n) || distance.ContainsKey(n)) continue;
                distance[n] = next;
                queue.Enqueue(n);
            }
        }
        return distance;
    }

    /// <summary>
    /// F104 B4: BFS step count from the cell containing <paramref name="worldPosition"/> to every cell over the logical walls (doors
    /// are ignored, as in ReachableCells), -1 for an unreachable cell. Null when the position is outside the maze. Indexed [x, z].
    /// </summary>
    public int[,] StepsFrom(Vector3 worldPosition)
    {
        if (!TryWorldToCell(worldPosition, out Vector2Int from)) return null;

        int[,] steps = new int[width, height];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++) steps[x, z] = -1;
        }

        steps[from.x, from.y] = 0;
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height || steps[n.x, n.y] >= 0) continue;
                steps[n.x, n.y] = steps[cell.x, cell.y] + 1;
                queue.Enqueue(n);
            }
        }

        return steps;
    }

    /// <summary>World direction of wall `dir` (0 north +Z, 1 east +X, 2 south, 3 west), the way every wall pass numbers them.</summary>
    public static Vector3 DirVector(int dir)
    {
        switch (dir)
        {
            case 0: return Vector3.forward;
            case 1: return Vector3.right;
            case 2: return Vector3.back;
            default: return Vector3.left;
        }
    }

    /// <summary>Cells reachable from `from` through open passages without ever entering `avoid`.</summary>
    private HashSet<Vector2Int> ReachableCellsAvoiding(Vector2Int from, Vector2Int avoid, Vector2Int? alsoAvoid = null)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { from };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);

        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir)) continue;
                Vector2Int n = Neighbour(cell, dir);
                if (n == avoid || n == alsoAvoid || n.x < 0 || n.x >= width || n.y < 0 || n.y >= height) continue;
                if (!visited.Add(n)) continue;
                queue.Enqueue(n);
            }
        }
        return visited;
    }

    private static void ShuffleInts(List<int> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>BFS from `origin`: first cell at >= `minSteps`, or null if the maze is smaller than that.</summary>
    private Vector2Int? FindCellAtLeastSteps(Vector2Int origin, int minSteps)
    {
        Dictionary<Vector2Int, int> dist = new Dictionary<Vector2Int, int> { [origin] = 0 };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(origin);

        while (queue.Count > 0)
        {
            Vector2Int c = queue.Dequeue();
            int d = dist[c];
            if (d >= minSteps) return c;
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(c.x, c.y, dir)) continue;
                Vector2Int n = Neighbour(c, dir);
                if (n.x < 0 || n.x >= width || n.y < 0 || n.y >= height || IsVoid(n)) continue;
                if (dist.ContainsKey(n)) continue;
                dist[n] = d + 1;
                queue.Enqueue(n);
            }
        }

        return null;
    }

    /// <summary>Cell centre farthest (flat) from a point. F36 warps the released hunter here.</summary>
    public Vector3 FarthestCellCenterFrom(Vector3 point)
    {
        Vector3 best = _cellCenters.Count > 0 ? _cellCenters[0] : point;
        float bestDistance = -1f;

        foreach (Vector3 cell in _cellCenters)
        {
            Vector3 flat = cell - point;
            flat.y = 0f;
            float distance = flat.sqrMagnitude;
            if (distance > bestDistance)
            {
                bestDistance = distance;
                best = cell;
            }
        }

        return best;
    }

    private Vector2Int FarthestCell()
    {
        Vector2Int best = new Vector2Int(width - 1, height - 1);
        int bestDistance = -1;

        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                if (_distance[x, z] > bestDistance)
                {
                    bestDistance = _distance[x, z];
                    best = new Vector2Int(x, z);
                }
            }
        }

        return best;
    }

    private static void Shuffle(List<Vector2Int> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
