using System.Collections.Generic;
using System.Text;
using StarterAssets;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F105 C2 and C3, the maze side: a pillared room and a long gallery (their own +21 stream), and the squeeze (+22) - a stretch
/// of corridor narrowed between two inserts of the theme's own wall. They change the topology or the geometry before the bake,
/// from Generate on, so every placement pass after them sees the final layout. With a flag off nothing here draws from its stream
/// and the maze is the one it always was; floor 1 never gets any of it; a floor without a theme (the primitive fallback) gets none.
/// Also the DebugMap extras: the r / g / q marks and a reachability check that treats locked doors as walls.
/// </summary>
public partial class MazeGenerator
{
    [Header("F105 C2 - rooms and galleries")]
    [Tooltip("The first floor with a pillared room: a block of cells with every interior wall opened")]
    [SerializeField, Min(1)] private int roomFromFloor = 2;
    [Tooltip("The room is 3x3 cells when the grid is at least this many cells on both sides, otherwise 2x2")]
    [SerializeField, Min(2)] private int largeRoomGrid = 10;
    [Tooltip("The first floor with a gallery: a long straight run of open passages with a lamp at its far end")]
    [SerializeField, Min(1)] private int galleryFromFloor = 3;
    [Tooltip("A gallery is at least this many cells long. A floor whose longest straight run is shorter gets one carved")]
    [SerializeField, Min(3)] private int galleryMinCells = 5;
    [Tooltip("Ground fog over a chunk that holds gallery cells, as a fraction of the normal density (and so emission)")]
    [SerializeField, Range(0.1f, 1f)] private float galleryFogScale = 0.5f;

    [Header("F105 C3 - the squeeze")]
    [Tooltip("The first floor with a squeeze: two corridor cells narrowed to a gap the player fits and the hunter does not")]
    [SerializeField, Min(1)] private int squeezeFromFloor = 2;
    [Tooltip("The gap between the two inserts (m). Clamped at build time into the window the player's CharacterController and the bake's agent radius leave open; no window means no squeeze")]
    [SerializeField, Min(0.3f)] private float squeezeGap = 0.88f;
    [Tooltip("The gap must exceed the player's CharacterController diameter by at least this (m)")]
    [SerializeField, Min(0f)] private float squeezePlayerMargin = 0.2f;
    [Tooltip("Slack kept from both ends of the window (m), so voxel rounding in the bake cannot let the hunter through or pinch the player")]
    [SerializeField, Min(0f)] private float squeezeWindowSlack = 0.04f;
    [Tooltip("Ceiling height inside the squeeze (m)")]
    [SerializeField, Min(1.8f)] private float squeezeCeiling = 2.2f;
    [Tooltip("Walking speed inside the squeeze, as a fraction (sprint is blocked there too)")]
    [SerializeField, Range(0.2f, 1f)] private float squeezeSpeedScale = 0.6f;
    [Tooltip("Torch range inside the squeeze, as a fraction")]
    [SerializeField, Range(0.2f, 1f)] private float squeezeTorchScale = 0.7f;
    [Tooltip("Volume of the breathing heard inside the squeeze")]
    [SerializeField, Range(0f, 1f)] private float squeezeBreathVolume = 0.35f;

    private readonly HashSet<Vector2Int> _roomCells = new HashSet<Vector2Int>();
    /// <summary>The grid vertices inside the room, where no wall meets and so BuildPillars would put nothing.</summary>
    private readonly List<Vector2Int> _roomPillarVertices = new List<Vector2Int>();
    /// <summary>The gallery's cells in order along its axis (empty when there is none).</summary>
    private readonly List<Vector2Int> _galleryRun = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> _galleryCells = new HashSet<Vector2Int>();
    /// <summary>The squeeze's two cells, in order along its axis (empty when there is none).</summary>
    private readonly List<Vector2Int> _squeezeCells = new List<Vector2Int>();
    private float _squeezeGapUsed;
    private SqueezeZone _squeezeZone;

    private struct WallSnapshot
    {
        public bool[,] North, East, South, West;
    }

    // ---------------------------------------------------------------- C2: rooms and galleries (+21)

    /// <summary>
    /// F105 C2, called from Generate once the maze is braided and its distances exist. A pillared room from roomFromFloor, a gallery from
    /// galleryFromFloor. Each is tried in a seeded order and kept only if the hunter's spawn (the farthest cell, which opening walls can
    /// move) does not land inside it; otherwise the walls are put back and the next candidate is tried. Leaves _distance valid.
    /// </summary>
    private void BuildRoomsAndGallery(int usedSeed)
    {
        _roomCells.Clear();
        _roomPillarVertices.Clear();
        _galleryRun.Clear();
        _galleryCells.Clear();
        if (!terror.roomsAndGalleries || _theme == null || _profile == null || _profile.Floor < roomFromFloor) return;

        System.Random rng = new System.Random(usedSeed + 21);
        CarveRoom(rng);
        if (_profile.Floor >= galleryFromFloor) FindOrCarveGallery(rng);
    }

    /// <summary>Kept dead ends and their mouths: cells the layout passes leave alone, so A10's bait keeps its shape.</summary>
    private HashSet<Vector2Int> DeadEndCells()
    {
        HashSet<Vector2Int> cells = new HashSet<Vector2Int>();
        foreach (Vector2Int deadEnd in _keptDeadEnds)
        {
            cells.Add(deadEnd);
            cells.Add(MouthOf(deadEnd));
        }

        return cells;
    }

    private WallSnapshot TakeWallSnapshot()
    {
        return new WallSnapshot
        {
            North = (bool[,])_wallN.Clone(), East = (bool[,])_wallE.Clone(),
            South = (bool[,])_wallS.Clone(), West = (bool[,])_wallW.Clone()
        };
    }

    private void RestoreWalls(WallSnapshot snapshot)
    {
        System.Array.Copy(snapshot.North, _wallN, snapshot.North.Length);
        System.Array.Copy(snapshot.East, _wallE, snapshot.East.Length);
        System.Array.Copy(snapshot.South, _wallS, snapshot.South.Length);
        System.Array.Copy(snapshot.West, _wallW, snapshot.West.Length);
    }

    private void CarveRoom(System.Random rng)
    {
        int size = width >= largeRoomGrid && height >= largeRoomGrid ? 3 : 2;
        HashSet<Vector2Int> avoid = DeadEndCells();

        List<Vector2Int> anchors = new List<Vector2Int>();
        for (int x = 0; x + size <= width; x++)
        {
            for (int z = 0; z + size <= height; z++)
            {
                if (BlockIsFree(new Vector2Int(x, z), size, avoid)) anchors.Add(new Vector2Int(x, z));
            }
        }

        Shuffle(anchors, rng);
        WallSnapshot before = TakeWallSnapshot();
        foreach (Vector2Int anchor in anchors)
        {
            OpenBlock(anchor, size);
            ComputeDistances();
            if (!InBlock(FarthestCell(), anchor, size))
            {
                for (int x = anchor.x; x < anchor.x + size; x++)
                {
                    for (int z = anchor.y; z < anchor.y + size; z++) _roomCells.Add(new Vector2Int(x, z));
                }

                for (int vx = anchor.x + 1; vx < anchor.x + size; vx++)
                {
                    for (int vz = anchor.y + 1; vz < anchor.y + size; vz++) _roomPillarVertices.Add(new Vector2Int(vx, vz));
                }

                terror.Log($"Room {size}x{size} at {anchor}", this);
                return;
            }

            RestoreWalls(before);
        }

        ComputeDistances();
        terror.Log("No room fits on this floor", this);
    }

    /// <summary>A block is free when it does not touch the start (Chebyshev distance 2 or more) and holds none of the avoided cells.</summary>
    private static bool BlockIsFree(Vector2Int anchor, int size, HashSet<Vector2Int> avoid)
    {
        for (int x = anchor.x; x < anchor.x + size; x++)
        {
            for (int z = anchor.y; z < anchor.y + size; z++)
            {
                if (Mathf.Max(x, z) < 2 || avoid.Contains(new Vector2Int(x, z))) return false;
            }
        }

        return true;
    }

    private static bool InBlock(Vector2Int cell, Vector2Int anchor, int size)
    {
        return cell.x >= anchor.x && cell.x < anchor.x + size && cell.y >= anchor.y && cell.y < anchor.y + size;
    }

    private void OpenBlock(Vector2Int anchor, int size)
    {
        for (int x = anchor.x; x < anchor.x + size; x++)
        {
            for (int z = anchor.y; z < anchor.y + size; z++)
            {
                if (x + 1 < anchor.x + size) OpenWall(new Vector2Int(x, z), 1);
                if (z + 1 < anchor.y + size) OpenWall(new Vector2Int(x, z), 0);
            }
        }
    }

    /// <summary>The longest straight run of open passages if it is long enough; otherwise the cheapest line of galleryMinCells cells to open up.</summary>
    private void FindOrCarveGallery(System.Random rng)
    {
        HashSet<Vector2Int> blocked = DeadEndCells();
        blocked.UnionWith(_roomCells);
        blocked.Add(Vector2Int.zero);
        blocked.Add(FarthestCell());

        List<Vector2Int> run = LongestStraightRun(blocked, rng);
        if (run.Count < galleryMinCells && !TryCarveGallery(blocked, rng, out run)) return;

        _galleryRun.AddRange(run);
        _galleryCells.UnionWith(run);
        terror.Log($"Gallery of {run.Count} cells from {run[0]} to {run[run.Count - 1]}", this);
    }

    /// <summary>The longest line of cells joined by open passages along one axis, none of them blocked. Ties are broken by `rng`.</summary>
    private List<Vector2Int> LongestStraightRun(HashSet<Vector2Int> blocked, System.Random rng)
    {
        List<List<Vector2Int>> best = new List<List<Vector2Int>>();
        int bestLength = 0;

        void Consider(List<Vector2Int> run)
        {
            if (run.Count == 0 || run.Count < bestLength) return;
            if (run.Count > bestLength) best.Clear();
            bestLength = run.Count;
            best.Add(run);
        }

        for (int axis = 0; axis < 2; axis++)
        {
            int dir = axis == 0 ? 1 : 0; // along +X (east) or +Z (north)
            int lines = axis == 0 ? height : width;
            int cellsPerLine = axis == 0 ? width : height;
            for (int line = 0; line < lines; line++)
            {
                List<Vector2Int> run = new List<Vector2Int>();
                for (int i = 0; i < cellsPerLine; i++)
                {
                    Vector2Int cell = axis == 0 ? new Vector2Int(i, line) : new Vector2Int(line, i);
                    bool joined = run.Count > 0 && !IsWallClosed(run[run.Count - 1].x, run[run.Count - 1].y, dir);
                    if (blocked.Contains(cell) || !joined)
                    {
                        Consider(run);
                        run = new List<Vector2Int>();
                    }

                    if (!blocked.Contains(cell)) run.Add(cell);
                }

                Consider(run);
            }
        }

        return best.Count > 0 ? best[rng.Next(best.Count)] : new List<Vector2Int>();
    }

    /// <summary>
    /// Opens the walls along the line of galleryMinCells cells that has the fewest closed ones (ties by `rng`). Opening walls never cuts
    /// anything off; a line is kept only if the hunter's spawn stays out of it, else its walls are put back and the next is tried.
    /// </summary>
    private bool TryCarveGallery(HashSet<Vector2Int> blocked, System.Random rng, out List<Vector2Int> run)
    {
        run = null;
        List<(int closed, int tie, Vector2Int anchor, int axis)> windows = new List<(int, int, Vector2Int, int)>();
        for (int axis = 0; axis < 2; axis++)
        {
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    Vector2Int anchor = new Vector2Int(x, z);
                    if (!WindowFits(anchor, axis) || WindowTouches(anchor, axis, blocked)) continue;

                    windows.Add((ClosedWallsInWindow(anchor, axis), rng.Next(), anchor, axis));
                }
            }
        }

        windows.Sort((a, b) => a.closed != b.closed ? a.closed.CompareTo(b.closed) : a.tie.CompareTo(b.tie));
        WallSnapshot before = TakeWallSnapshot();
        foreach ((int closed, int tie, Vector2Int anchor, int axis) window in windows)
        {
            List<Vector2Int> cells = WindowCells(window.anchor, window.axis);
            for (int i = 0; i + 1 < cells.Count; i++) OpenWall(cells[i], window.axis == 0 ? 1 : 0);

            ComputeDistances();
            Vector2Int spawn = FarthestCell();
            if (!cells.Contains(spawn) && !_roomCells.Contains(spawn))
            {
                run = cells;
                return true;
            }

            RestoreWalls(before);
        }

        ComputeDistances();
        terror.Log("No gallery fits on this floor", this);
        return false;
    }

    private bool WindowFits(Vector2Int anchor, int axis)
    {
        return axis == 0 ? anchor.x + galleryMinCells <= width : anchor.y + galleryMinCells <= height;
    }

    private List<Vector2Int> WindowCells(Vector2Int anchor, int axis)
    {
        List<Vector2Int> cells = new List<Vector2Int>(galleryMinCells);
        for (int i = 0; i < galleryMinCells; i++) cells.Add(axis == 0 ? new Vector2Int(anchor.x + i, anchor.y) : new Vector2Int(anchor.x, anchor.y + i));
        return cells;
    }

    private bool WindowTouches(Vector2Int anchor, int axis, HashSet<Vector2Int> blocked)
    {
        foreach (Vector2Int cell in WindowCells(anchor, axis))
        {
            if (blocked.Contains(cell)) return true;
        }

        return false;
    }

    private int ClosedWallsInWindow(Vector2Int anchor, int axis)
    {
        List<Vector2Int> cells = WindowCells(anchor, axis);
        int closed = 0;
        for (int i = 0; i + 1 < cells.Count; i++)
        {
            if (IsWallClosed(cells[i].x, cells[i].y, axis == 0 ? 1 : 0)) closed++;
        }

        return closed;
    }

    /// <summary>Pillar at every room vertex, from BuildPillars: a room's interior has no wall to meet, so the vertex rule would give it nothing.</summary>
    private void BuildRoomPillars()
    {
        foreach (Vector2Int vertex in _roomPillarVertices) SpawnPillar(vertex.x, vertex.y);
    }

    /// <summary>
    /// A lamp on the gallery's far end - the end farther from the start - on the cap wall facing down the run, so it glows at the end of the
    /// long view. Skipped when that end already has a lamp. Built after every other lamp, so it takes the +2 stream's last draws and
    /// moves none of the earlier ones.
    /// </summary>
    private void BuildGalleryLamp(Material sharedMaterial, System.Random rng)
    {
        if (_galleryRun.Count == 0) return;

        Vector2Int first = _galleryRun[0];
        Vector2Int last = _galleryRun[_galleryRun.Count - 1];
        bool farIsLast = _distance[last.x, last.y] >= _distance[first.x, first.y];
        Vector2Int end = farIsLast ? last : first;
        if (_lampCells.Contains(end)) return;

        int axisDir = first.y == last.y ? 1 : 0; // east or north
        int outward = farIsLast ? axisDir : (axisDir + 2) % 4;
        for (int turn = 0; turn < 4; turn++)
        {
            int dir = (outward + turn) % 4;
            if (!IsWallClosed(end.x, end.y, dir)) continue;

            BuildWallLamp(end, false, sharedMaterial, rng, DirectionVector(dir));
            terror.Log($"Gallery lamp at {end}", this);
            return;
        }
    }

    private static Vector3 DirectionVector(int dir)
    {
        switch (dir)
        {
            case 0: return Vector3.forward;
            case 1: return Vector3.right;
            case 2: return Vector3.back;
            default: return Vector3.left;
        }
    }

    /// <summary>The ground fog of a chunk: thinner over the gallery. Nothing for a maze without one.</summary>
    private float FogScaleForChunk(int firstX, int firstZ, int countX, int countZ)
    {
        foreach (Vector2Int cell in _galleryRun)
        {
            if (cell.x >= firstX && cell.x < firstX + countX && cell.y >= firstZ && cell.y < firstZ + countZ) return galleryFogScale;
        }

        return 1f;
    }

    // ---------------------------------------------------------------- C3: the squeeze (+22)

    /// <summary>
    /// F105 C3, called from Generate after the layout is final. Picks two adjacent straight corridor cells (+22) that are not the start, the
    /// hunter's spawn, a room, a gallery or a bait dead end, and that are not the only way to any other cell - the hunter cannot use a
    /// squeeze, so nothing may depend on one. Stars, lockers, keys and switches are kept out of it by the passes that place them.
    /// </summary>
    private void ChooseSqueeze(int usedSeed)
    {
        _squeezeCells.Clear();
        _squeezeGapUsed = 0f;
        if (!terror.squeezes || _theme == null || _profile == null || _profile.Floor < squeezeFromFloor) return;
        if (_theme.Wall == null || !TryResolveSqueezeGap(out float gap)) return;

        System.Random rng = new System.Random(usedSeed + 22);
        HashSet<Vector2Int> avoid = DeadEndCells();
        avoid.UnionWith(_roomCells);
        avoid.UnionWith(_galleryCells);
        avoid.Add(Vector2Int.zero);
        avoid.Add(FarthestCell());

        List<(Vector2Int a, Vector2Int b)> pairs = new List<(Vector2Int, Vector2Int)>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int a = new Vector2Int(x, z);
                Vector2Int north = new Vector2Int(x, z + 1);
                Vector2Int east = new Vector2Int(x + 1, z);
                if (SqueezePairFits(a, north, true, avoid)) pairs.Add((a, north));
                if (SqueezePairFits(a, east, false, avoid)) pairs.Add((a, east));
            }
        }

        for (int i = pairs.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pairs[i], pairs[j]) = (pairs[j], pairs[i]);
        }

        foreach ((Vector2Int a, Vector2Int b) pair in pairs)
        {
            if (ReachableCellsAvoiding(Vector2Int.zero, pair.a, pair.b).Count != width * height - 2) continue;

            _squeezeCells.Add(pair.a);
            _squeezeCells.Add(pair.b);
            _squeezeGapUsed = gap;
            terror.Log($"Squeeze at ({pair.a}, {pair.b}), gap {gap:0.00} m", this);
            return;
        }

        terror.Log("No squeeze fits on this floor", this);
    }

    private bool SqueezePairFits(Vector2Int a, Vector2Int b, bool northSouth, HashSet<Vector2Int> avoid)
    {
        if (b.x >= width || b.y >= height || avoid.Contains(a) || avoid.Contains(b)) return false;
        return IsStraight(a.x, a.y, northSouth) && IsStraight(b.x, b.y, northSouth);
    }

    /// <summary>True when the cell is a plain corridor along the axis: open at both ends of it, walled on both sides.</summary>
    private bool IsStraight(int x, int z, bool northSouth)
    {
        return northSouth
            ? !_wallN[x, z] && !_wallS[x, z] && _wallE[x, z] && _wallW[x, z]
            : !_wallE[x, z] && !_wallW[x, z] && _wallN[x, z] && _wallS[x, z];
    }

    /// <summary>
    /// The gap that fits the player and not the hunter: wider than the player's CharacterController diameter plus squeezePlayerMargin, narrower
    /// than twice the radius the bake erodes walls by. squeezeGap is clamped into that window (less squeezeWindowSlack at each end); no window,
    /// or no player or surface to read, means no squeeze (logged).
    /// </summary>
    private bool TryResolveSqueezeGap(out float gap)
    {
        gap = 0f;
        Transform player = FindPlayer();
        CharacterController body = player != null ? player.GetComponent<CharacterController>() : null;
        NavMeshSurface surface = ResolveNavMeshSurface();
        if (body == null || surface == null)
        {
            terror.Log("No squeeze: the player's CharacterController or the NavMeshSurface could not be read", this);
            return false;
        }

        float playerDiameter = 2f * body.radius * Mathf.Max(player.lossyScale.x, player.lossyScale.z);
        float low = playerDiameter + squeezePlayerMargin + squeezeWindowSlack;
        float high = 2f * NavMesh.GetSettingsByID(surface.agentTypeID).agentRadius - squeezeWindowSlack;
        if (low >= high)
        {
            terror.Log($"No squeeze: no gap fits the player ({low:0.00} m up) and not the hunter ({high:0.00} m down)", this);
            return false;
        }

        gap = Mathf.Clamp(squeezeGap, low, high);
        return true;
    }

    /// <summary>
    /// Pre-bake, from Awake after BuildSetPieces: reserves the squeeze's cells (so props, decals, keys, switches and doors keep out, like a
    /// set piece's) and builds the two inserts and the zone. The inserts are the theme's wall piece, scaled to fill the inset on each side
    /// of the corridor, so they carve the bake and block the hunter; the ceiling over the cells is lowered later, in BuildCeiling.
    /// </summary>
    private void BuildSqueeze()
    {
        if (_squeezeCells.Count == 0) return;

        foreach (Vector2Int cell in _squeezeCells) _setPieceCells.Add(cell);

        Vector2Int a = _squeezeCells[0];
        Vector2Int b = _squeezeCells[1];
        bool northSouth = a.x == b.x;
        Vector3 middle = (CellCenter(a.x, a.y) + CellCenter(b.x, b.y)) * 0.5f;
        float halfWidth = (cellSize - wallThickness) * 0.5f;
        float thickness = halfWidth - _squeezeGapUsed * 0.5f;
        float centreOffset = halfWidth - thickness * 0.5f;
        Quaternion rotation = northSouth ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
        Vector3 side = northSouth ? Vector3.right : Vector3.forward;

        SpawnThemedWall($"Squeeze_{a.x}_{a.y}_A", middle - side * centreOffset, rotation, 2f * cellSize, squeezeCeiling, thickness);
        SpawnThemedWall($"Squeeze_{a.x}_{a.y}_B", middle + side * centreOffset, rotation, 2f * cellSize, squeezeCeiling, thickness);

        GameObject zone = new GameObject($"SqueezeZone_{a.x}_{a.y}");
        zone.transform.SetParent(_mazeRoot, false);
        _squeezeZone = zone.AddComponent<SqueezeZone>();
        // A little short of the cells' far edges, so the effects begin once the player is really between the inserts.
        float hall = cellSize - wallThickness;
        Vector3 extent = northSouth ? new Vector3(hall, 4f, 2f * cellSize - 1f) : new Vector3(2f * cellSize - 1f, 4f, hall);
        _squeezeZone.Define(new Bounds(middle + Vector3.up * 1.5f, extent));
    }

    /// <summary>Hands the squeeze zone the player's controller, stamina and torch, once SetUpAtmosphere has them.</summary>
    private void BindSqueeze(Transform player, ThirdPersonController controller, PlayerStamina stamina, Flashlight flashlight)
    {
        if (_squeezeZone != null) _squeezeZone.Bind(player, controller, stamina, flashlight, squeezeSpeedScale, squeezeTorchScale, squeezeBreathVolume);
    }

    /// <summary>The ceiling height over a cell: lowered over the squeeze, the wall height anywhere else.</summary>
    private float CeilingHeightAt(int x, int z)
    {
        return _squeezeCells.Contains(new Vector2Int(x, z)) ? squeezeCeiling : wallHeight;
    }

    // ---------------------------------------------------------------- debug map extras

    /// <summary>Marks the room (r), gallery (g) and squeeze (q) cells that carry no other mark, so they never hide a star, key or button.</summary>
    private void AddLayoutMarks(string[,] marks)
    {
        foreach (Vector2Int cell in _roomCells) if (string.IsNullOrEmpty(marks[cell.x, cell.y])) marks[cell.x, cell.y] = "r";
        foreach (Vector2Int cell in _galleryCells) if (string.IsNullOrEmpty(marks[cell.x, cell.y])) marks[cell.x, cell.y] = "g";
        foreach (Vector2Int cell in _squeezeCells) if (string.IsNullOrEmpty(marks[cell.x, cell.y])) marks[cell.x, cell.y] = "q";
    }

    /// <summary>Legend lines for the layout extras and the lure star, then the reachability check.</summary>
    private void AppendLayoutLegend(StringBuilder sb)
    {
        sb.Append("r room   g gallery   q squeeze (marked only on a cell with no other mark)\n");
        if (_roomCells.Count > 0) sb.Append($"room: {string.Join(" ", _roomCells)}\n");
        if (_galleryRun.Count > 0) sb.Append($"gallery: {string.Join(" ", _galleryRun)}\n");
        if (_squeezeCells.Count > 0) sb.Append($"squeeze: {_squeezeCells[0]} {_squeezeCells[1]}, gap {_squeezeGapUsed:0.00} m\n");
        if (LureStar != null && TryWorldToCell(LureStar.position, out Vector2Int lureCell)) sb.Append($"lure star (dried blood under it): {lureCell}\n");

        AppendReachability(sb);
    }

    /// <summary>
    /// A completability check for the map: a flood fill from the start with every locked door a wall; any reached button then unlocks its door
    /// (and the chain behind it) and the fill runs again, until nothing changes. Reports every star, key, fuse box and button, and an open
    /// hatch, still out of reach - the squeeze counts as open, since the player fits it.
    /// </summary>
    private void AppendReachability(StringBuilder sb)
    {
        HashSet<(Vector2Int, int)> locked = new HashSet<(Vector2Int, int)>();
        foreach (MazeDoor door in _doors)
        {
            if (door != null && !door.IsUnlocked) LockPassage(locked, door, true);
        }

        HashSet<Vector2Int> reached = FloodFrom(Vector2Int.zero, locked);
        int withoutButtons = reached.Count;
        for (bool changed = true; changed;)
        {
            changed = false;
            foreach (WallButton button in _buttons)
            {
                if (button == null || button.Door == null || !TryWorldToCell(button.transform.position, out Vector2Int cell) || !reached.Contains(cell)) continue;

                int hops = 0;
                for (MazeDoor door = button.Door; door != null && hops++ <= _doors.Count; door = door.LinkedDoor) changed |= LockPassage(locked, door, false);
            }

            if (changed) reached = FloodFrom(Vector2Int.zero, locked);
        }

        List<string> missing = new List<string>();
        int targets = 0;
        void Check(Vector3 world, string label)
        {
            targets++;
            if (!TryWorldToCell(world, out Vector2Int cell) || !reached.Contains(cell)) missing.Add($"{label} {cell}");
        }

        foreach (Transform star in _stars) if (star != null) Check(star.position, "star");
        foreach (KeyPickup key in _keys) if (key != null && !key.IsTaken) Check(key.transform.position, "key");
        foreach (FuseBox box in _fuseBoxes) if (box != null) Check(box.transform.position, "fuse box");
        foreach (WallButton button in _buttons) if (button != null) Check(button.transform.position, "button");
        MazeEscape escape = FindAnyObjectByType<MazeEscape>();
        if (HasExitGates) foreach (Vector2Int gateCell in GateCells) Check(CellCenter(gateCell.x, gateCell.y), "exit gate"); // F111: always built, so always checked
        else if (escape != null && escape.HatchOpen) Check(escape.HatchPosition, "hatch");

        sb.Append($"reachability (locked doors are walls until their button is reached): {withoutButtons} of {width * height} cells before any button, {reached.Count} after; ");
        sb.Append(missing.Count == 0 ? $"all {targets} targets reachable\n" : $"UNREACHABLE: {string.Join(", ", missing)}\n");
    }

    /// <summary>Adds (lock) or removes (unlock) both directions of a door's passage. Returns true if the set changed.</summary>
    private bool LockPassage(HashSet<(Vector2Int, int)> locked, MazeDoor door, bool lockIt)
    {
        if (!_doorPassages.TryGetValue(door, out Passage p)) return false;

        bool changed;
        if (lockIt)
        {
            changed = locked.Add((p.CellA, p.Dir));
            changed |= locked.Add((p.CellB, (p.Dir + 2) % 4));
        }
        else
        {
            changed = locked.Remove((p.CellA, p.Dir));
            changed |= locked.Remove((p.CellB, (p.Dir + 2) % 4));
        }

        return changed;
    }

    private HashSet<Vector2Int> FloodFrom(Vector2Int from, HashSet<(Vector2Int, int)> locked)
    {
        HashSet<Vector2Int> visited = new HashSet<Vector2Int> { from };
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(from);
        while (queue.Count > 0)
        {
            Vector2Int cell = queue.Dequeue();
            for (int dir = 0; dir < 4; dir++)
            {
                if (IsWallClosed(cell.x, cell.y, dir) || locked.Contains((cell, dir))) continue;

                Vector2Int next = Neighbour(cell, dir);
                if (next.x < 0 || next.x >= width || next.y < 0 || next.y >= height || !visited.Add(next)) continue;

                queue.Enqueue(next);
            }
        }

        return visited;
    }
}
