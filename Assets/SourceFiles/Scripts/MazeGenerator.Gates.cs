using System.Collections.Generic;
using System.Text;
using StarterAssets;
using UnityEngine;

/// <summary>
/// F111, the maze side of the exit gates (the runtime halves are <see cref="ExitGate"/> and <see cref="GateLever"/>; MazeEscape powers them). A gate floor
/// (the 50 % seeded roll from floor 3 - the old roll's +17 draw, unchanged - or the final floor and the DESCENT boss level) has one gate, or two on the
/// final floor and the boss, set into the maze's outer wall from the start of the floor. They are chosen in Generate right after the loop (own +26
/// stream) so every placement pass sees their cells reserved; the wall segment is only missing geometry (the wall arrays stay closed, as for the loop's
/// opening), and the gate, the themed pieces that seal the rest of the segment and the stub corridor outside are built after the NavMesh bake, outside
/// the bake volume, so the hunter never paths into them. A floor that is not a gate floor draws nothing from +26 and is the maze it was.
/// </summary>
public partial class MazeGenerator
{
    private const int GateSalt = 26;
    private const int GateRollSalt = 17; // the exit-kind roll MazeEscape used to make, kept so a seed keeps its variant
    private const float GateFrameWidth = 3.5f;
    private const float GateFrameHeight = 3.2f;
    private const float GateProbeFloorGap = 0.1f;
    private static readonly Color GateLitColor = new Color(0.25f, 1f, 0.7f);

    [Header("F111 - exit gates")]
    [Tooltip("A gate's cell is at least this many steps (x + z) from the start cell")]
    [SerializeField, Min(0)] private int gateMinStartSteps = 4;

    private struct GateSlot
    {
        public Vector2Int Cell;
        public int Dir;
    }

    private bool _gatesActive;
    private readonly List<GateSlot> _gateSlots = new List<GateSlot>();
    private readonly List<ExitGate> _exitGates = new List<ExitGate>();

    /// <summary>True when this floor's exit is its exit gates (the escape builds no hatch).</summary>
    public bool HasExitGates => _gatesActive;

    /// <summary>The built gates (empty until BuildExitGates has run, and on a hatch floor).</summary>
    public IReadOnlyList<ExitGate> ExitGates => _exitGates;

    /// <summary>The cells the gates are set into, for the reachability check.</summary>
    public IEnumerable<Vector2Int> GateCells
    {
        get { foreach (GateSlot slot in _gateSlots) yield return slot.Cell; }
    }

    private bool IsGateCell(Vector2Int cell)
    {
        if (!_gatesActive) return false;
        foreach (GateSlot slot in _gateSlots) if (slot.Cell == cell) return true;
        return false;
    }

    /// <summary>True when the outer wall of cell (x, z) on side `dir` is a gate's, so the wall is not built there.</summary>
    private bool IsGateOpening(int x, int z, int dir)
    {
        if (!_gatesActive) return false;
        foreach (GateSlot slot in _gateSlots) if (slot.Cell.x == x && slot.Cell.y == z && slot.Dir == dir) return true;
        return false;
    }

    // ---------------------------------------------------------------- choosing the gates (+26)

    /// <summary>
    /// Called from Generate right after ChooseLoop. Decides whether this is a gate floor (never in super easy mode), then walks the maze's edge cells in a
    /// seeded order for gate A: not the start, the hunter's spawn, a room, a gallery, a squeeze, a bait dead end or the loop entrance, not on the loop
    /// opening's side, not within gateMinStartSteps of the start, and with room outside for the stub. A second gate goes on a different side, as far
    /// from the first as the maze allows. Anything that cannot be satisfied (including a missing kit slot or theme) leaves the floor on its hatch -
    /// decided here, before the walls are built, so the wall never has a hole without a gate to fill it.
    /// </summary>
    private void ChooseGates(int usedSeed)
    {
        _gatesActive = false;
        _gateSlots.Clear();
        _exitGates.Clear();
        if (_profile == null || superEasyMode) return;

        // The +17 draw, kept identical (tied to the seed alone, floors 3-5) so a retry keeps its variant.
        bool rolled = GameFlow.CurrentFloor >= 3 && new System.Random(usedSeed + GateRollSalt).NextDouble() < 0.5;
        if (!(_profile.ForceGates || rolled || debugForceGates)) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include); // as every kit user does; Generate runs before the first of them
        if (_theme == null || interactableKit == null || interactableKit.EscapeGate == null)
        {
            Debug.LogError("MazeGenerator: this floor should end in exit gates but the InteractableKit has no EscapeGate (or the floor has no theme). Run KEEP IT LIT > Build > Interactables, then save the scene. The floor keeps its hatch.", this);
            return;
        }

        int wanted = Mathf.Clamp(_profile.GateCount, 1, 2);
        System.Random rng = new System.Random(usedSeed + GateSalt);

        HashSet<Vector2Int> avoid = DeadEndCells();
        avoid.UnionWith(_roomCells);
        avoid.UnionWith(_galleryCells);
        avoid.UnionWith(_squeezeCells);
        avoid.Add(_startCell);
        avoid.Add(FarthestCell());
        if (_loopActive) avoid.Add(_loopEntrance);

        List<GateSlot> edges = new List<GateSlot>();
        for (int x = 0; x < width; x++)
        {
            edges.Add(new GateSlot { Cell = new Vector2Int(x, 0), Dir = 2 });
            edges.Add(new GateSlot { Cell = new Vector2Int(x, height - 1), Dir = 0 });
        }

        for (int z = 0; z < height; z++)
        {
            edges.Add(new GateSlot { Cell = new Vector2Int(0, z), Dir = 3 });
            edges.Add(new GateSlot { Cell = new Vector2Int(width - 1, z), Dir = 1 });
        }

        for (int i = edges.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (edges[i], edges[j]) = (edges[j], edges[i]);
        }

        List<GateSlot> valid = new List<GateSlot>();
        foreach (GateSlot edge in edges)
        {
            if (IsVoid(edge.Cell) || avoid.Contains(edge.Cell) || !IsWallClosed(edge.Cell.x, edge.Cell.y, edge.Dir)) continue;
            if (Mathf.Abs(edge.Cell.x - _startCell.x) + Mathf.Abs(edge.Cell.y - _startCell.y) < gateMinStartSteps) continue;
            if (_loopActive && edge.Dir == _loopOut) continue; // never on the loop opening's side of the maze
            if (!GateHasRoom(edge.Cell, edge.Dir)) continue;
            valid.Add(edge);
        }

        if (valid.Count == 0)
        {
            Debug.LogWarning("MazeGenerator: no edge cell has room for an exit gate; the floor keeps its hatch.", this);
            return;
        }

        GateSlot a = valid[0];
        _gateSlots.Add(a);

        if (wanted == 2)
        {
            bool found = false;
            GateSlot best = default;
            int bestDistance = -1;
            foreach (GateSlot candidate in valid)
            {
                if (candidate.Dir == a.Dir) continue;
                int distance = Mathf.Abs(candidate.Cell.x - a.Cell.x) + Mathf.Abs(candidate.Cell.y - a.Cell.y);
                if (distance <= bestDistance) continue;
                bestDistance = distance;
                best = candidate;
                found = true;
            }

            if (!found)
            {
                _gateSlots.Clear();
                Debug.LogWarning("MazeGenerator: no second exit gate fits on another side of the maze; the floor keeps its hatch.", this);
                return;
            }

            _gateSlots.Add(best);
        }

        _gatesActive = true;
        foreach (GateSlot slot in _gateSlots) terror.Log($"Exit gate at {slot.Cell}, outward {slot.Dir}", this);
    }

    /// <summary>True when nothing in the scene stands where the gate's stub corridor would (one cell deep, outside the maze), and the loop corridor will not either.</summary>
    private bool GateHasRoom(Vector2Int cell, int dir)
    {
        Vector2Int stub = Neighbour(cell, dir);
        Vector3 centre = CellCenter(stub.x, stub.y);
        float half = cellSize * 0.5f + wallThickness;
        Bounds footprint = new Bounds(new Vector3(centre.x, FloorTop + wallHeight * 0.5f, centre.z), new Vector3(half * 2f, wallHeight, half * 2f));
        if (_loopActive && LoopBounds(LoopChain(_loopEntrance, _loopOut, _loopTurn)).Intersects(footprint)) return false;

        Vector3 halfExtents = new Vector3(half, wallHeight * 0.5f - GateProbeFloorGap, half);
        Vector3 probe = new Vector3(centre.x, FloorTop + GateProbeFloorGap + halfExtents.y, centre.z);
        return !Physics.CheckBox(probe, halfExtents, Quaternion.identity, ~0, QueryTriggerInteraction.Ignore);
    }

    /// <summary>Pre-bake, next to ReserveLoopEntrance: a gate's cell takes no props or decals.</summary>
    private void ReserveGates()
    {
        foreach (GateSlot slot in _gateSlots) _setPieceCells.Add(slot.Cell);
    }

    // ---------------------------------------------------------------- the gates (post-bake)

    /// <summary>
    /// Post-bake: for each gate, a clone of InteractableKit.escapeGate in the wall's place, themed wall filling the rest of the segment beside the
    /// frame and above it, and the stub corridor outside (a floor and ceiling tile, three walls and the outer pillars), all under one `ExitGates` group.
    /// </summary>
    private void BuildExitGates()
    {
        if (!_gatesActive) return;

        Transform root = Group("ExitGates");
        float leverSeconds = _profile != null ? _profile.GateLeverSeconds : 10f;
        float segment = cellSize + wallThickness;
        float side = (segment - GateFrameWidth) * 0.5f;

        for (int i = 0; i < _gateSlots.Count; i++)
        {
            GateSlot slot = _gateSlots[i];
            Vector3 wall = BoundaryWallPosition(slot.Cell, slot.Dir);
            Vector3 inward = -DirVector(slot.Dir);
            Quaternion rotation = BoundaryWallRotation(slot.Dir);

            GameObject model = Instantiate(interactableKit.EscapeGate, wall, Quaternion.LookRotation(inward, Vector3.up), root);
            model.SetActive(true);
            model.name = $"ExitGate_{i}";

            // The rest of the segment: a themed wall each side of the frame, and a header over it, so the posts, shutter and these seal it.
            if (side > 0.01f)
            {
                Vector3 right = model.transform.right;
                float offset = GateFrameWidth * 0.5f + side * 0.5f;
                SpawnThemedWall($"GateFill_{i}_L", wall - right * offset, rotation, side, wallHeight, wallThickness, root);
                SpawnThemedWall($"GateFill_{i}_R", wall + right * offset, rotation, side, wallHeight, wallThickness, root);
            }

            if (wallHeight > GateFrameHeight + 0.05f)
            {
                SpawnThemedWall($"GateHeader_{i}", wall + Vector3.up * GateFrameHeight, rotation, GateFrameWidth, wallHeight - GateFrameHeight, wallThickness, root);
            }

            BuildGateStub(slot, i, root);

            ExitGate gate = model.AddComponent<ExitGate>();
            gate.Define(leverSeconds, GateLitColor);
            Transform leverBox = gate.LeverBox;
            if (leverBox != null) leverBox.gameObject.AddComponent<GateLever>().Bind(gate);
            else Debug.LogError("MazeGenerator: the InteractableKit's EscapeGate has no LeverBox child. Run KEEP IT LIT > Build > Re-skin Templates, or delete the escapeGate slot's object and run Build > Interactables.", model);
            _exitGates.Add(gate);
        }

        terror.Log($"Built {_exitGates.Count} exit gate(s)", this);
    }

    /// <summary>One cell of themed floor and ceiling outside the gate's wall, a wall on each of its three other sides and a pillar at each of its outer corners.</summary>
    private void BuildGateStub(GateSlot slot, int index, Transform root)
    {
        Vector2Int stub = Neighbour(slot.Cell, slot.Dir);
        Vector3 centre = CellCenter(stub.x, stub.y);

        GameObject floor = Instantiate(_theme.FloorTile, centre, Quaternion.identity, root);
        floor.SetActive(true);
        floor.name = $"GateFloor_{index}";
        floor.transform.localScale = new Vector3(cellSize / 4.5f, 1f, cellSize / 4.5f);

        GameObject ceiling = Instantiate(_theme.CeilingTile, centre + Vector3.up * wallHeight, Quaternion.identity, root);
        ceiling.SetActive(true);
        ceiling.name = $"GateCeiling_{index}";
        ceiling.transform.localScale = new Vector3(cellSize / 4.5f, 1f, cellSize / 4.5f);

        int back = (slot.Dir + 2) % 4; // the side that is the maze's own wall, where the gate stands
        for (int dir = 0; dir < 4; dir++)
        {
            if (dir == back) continue;
            Vector3Int edge = LoopEdgeKey(stub, dir);
            bool horizontal = edge.z == 0;
            SpawnThemedWall($"GateWall_{index}_{dir}", LoopEdgePosition(edge), horizontal ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f), cellSize + wallThickness, wallHeight, wallThickness, root);
        }

        for (int dx = 0; dx <= 1; dx++)
        {
            for (int dz = 0; dz <= 1; dz++)
            {
                int vx = stub.x + dx;
                int vz = stub.y + dz;
                bool onMazeGrid = vx >= 0 && vx <= width && vz >= 0 && vz <= height;
                if (!onMazeGrid) SpawnPillar(vx, vz, root);
            }
        }
    }

    // ---------------------------------------------------------------- debug extras

    /// <summary>One line per gate for the debug map's legend (the G glyph itself is drawn on the wall by DebugWallGlyph).</summary>
    private void AppendGateLegend(StringBuilder sb)
    {
        if (!_gatesActive)
        {
            sb.Append("G exit gate: none on this floor\n");
            return;
        }

        for (int i = 0; i < _gateSlots.Count; i++)
        {
            string state = i < _exitGates.Count ? $"{_exitGates[i].State}, lever {_exitGates[i].Progress01 * 100f:0}%" : "not built";
            sb.Append($"G exit gate {i}: in the {EdgeNames[_gateSlots[i].Dir]} wall of {_gateSlots[i].Cell} (the wall is closed in the maze's own data); {state}\n");
        }
    }

    /// <summary>
    /// Testing (KEEP IT LIT &gt; Debug &gt; Go To Exit Gate): puts the player 2 m inside the first gate that is not yet open, facing it. Returns what happened.
    /// </summary>
    public string DebugGoToExitGate()
    {
        if (!_gatesActive || _exitGates.Count == 0) return "This floor has no exit gates. Tick Debug Force Gates on the MazeGenerator (a themed floor) and start again.";
        ThirdPersonController player = FindAnyObjectByType<ThirdPersonController>();
        if (player == null) return "No player found.";

        ExitGate gate = _exitGates[0];
        foreach (ExitGate candidate in _exitGates)
        {
            if (candidate.State != ExitGate.GateState.Open) { gate = candidate; break; }
        }

        Vector3 facing = -gate.transform.forward;
        Vector3 spot = gate.WallPosition + gate.transform.forward * 2f;
        CharacterController controller = player.GetComponent<CharacterController>();
        if (controller != null) controller.enabled = false;
        player.transform.position = new Vector3(spot.x, player.transform.position.y, spot.z);
        if (controller != null) controller.enabled = true;
        Camera view = Camera.main;
        float currentYaw = view != null ? view.transform.eulerAngles.y : player.transform.eulerAngles.y;
        player.AddYaw(Mathf.DeltaAngle(currentYaw, Quaternion.LookRotation(facing).eulerAngles.y));
        return $"At exit gate {_exitGates.IndexOf(gate)} ({gate.State}), facing it.";
    }
}
