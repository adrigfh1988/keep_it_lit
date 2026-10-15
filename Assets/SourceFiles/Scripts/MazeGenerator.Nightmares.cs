using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// F123, the maze side of the wing nightmares (the runtime half is <see cref="WingNightmareDirector"/>, spec plannings/f123-wing-nightmares-plan.md).
/// Every pass keys on the FLOOR (floor 5 wears floor 3's Crypt look) and never runs in a DESCENT. One RNG stream, <c>usedSeed + 27</c>
/// (<see cref="NightmareSalt"/>): the floors are mutually exclusive, so floor 3's pre-bake drawer banks, floor 4's ampoules or floor 5's notices
/// are the only draws it ever makes; floors 1 and 2 are runtime-only and draw nothing from a maze stream.
///   floor 1  <see cref="TryAddLateDoor"/>: a Manual shutter door added post-bake, never by editing _wallN/E/S/W
///   floor 3  <see cref="BuildMorgueDrawers"/>: banks of four drawers on the end wall of the dead ends (pre-bake, the wall only is reserved)
///   floor 4  <see cref="BuildAmpoules"/>: blue-capped ampoules in cells no lamp reaches (post-bake)
///   floor 5  <see cref="BuildDirectorNotices"/>: the printed HOUSE RULES taped to three walls (post-bake)
/// </summary>
public partial class MazeGenerator
{
    private const int NightmareSalt = 27; // the +27 stream (F123)
    private const float NightmareFloorGap = 0.02f;

    [Header("F123 - wing nightmares")]
    [Tooltip("Floor 3: morgue drawer banks built at the end of dead ends (two at least when the maze has room)")]
    [SerializeField, Range(0, 5)] private int drawerBankCount = 3;
    [Tooltip("Floor 4: blue-capped ampoules lying where no lamp reaches")]
    [SerializeField, Range(0, 6)] private int ampouleCount = 3;
    [Tooltip("Floor 5: house-rules notices (the first on the start cell's wall)")]
    [SerializeField, Range(0, 5)] private int directorNoticeCount = 3;
    [Tooltip("The chalk numeral on the extra doors of floor 1 (TextMeshPro world text)")]
    [SerializeField, Min(1f)] private float extraDoorChalkSize = 8f;
    [SerializeField, Min(0.5f)] private float extraDoorChalkHeight = 1.7f;
    [Tooltip("Floor 5 notice: centre height above the floor (m) and the sheet and text sizes (m)")]
    [SerializeField, Min(0.5f)] private float houseRulesHeight = 1.45f;
    [SerializeField] private Vector2 houseRulesPaper = new Vector2(1f, 1.2f);
    [SerializeField] private Vector2 houseRulesText = new Vector2(0.9f, 1.1f);

    private readonly List<MorgueDrawerBank> _drawerBanks = new List<MorgueDrawerBank>();
    private readonly List<Vector2Int> _drawerBankCells = new List<Vector2Int>();
    private readonly List<NightmareAmpoule> _ampoules = new List<NightmareAmpoule>();
    private readonly List<Vector2Int> _ampouleCells = new List<Vector2Int>();
    private readonly List<GameObject> _directorNotices = new List<GameObject>();
    private readonly List<Vector2Int> _directorNoticeCells = new List<Vector2Int>();

    /// <summary>F123 floor 3: the banks built this floor (Awake order), late-bound by SetUpAtmosphere.</summary>
    public IReadOnlyList<MorgueDrawerBank> DrawerBanks => _drawerBanks;

    /// <summary>F123 floor 4: the ampoules built this floor.</summary>
    public IReadOnlyList<NightmareAmpoule> Ampoules => _ampoules;

    /// <summary>F123 floor 5: the notices built this floor.</summary>
    public IReadOnlyList<GameObject> DirectorNotices => _directorNotices;

    // ---------------------------------------------------------------- small public helpers the runtime classes use

    /// <summary>True when wall `dir` (0 N, 1 E, 2 S, 3 W) of `cell` is closed (a cell off the grid counts as closed).</summary>
    public bool IsClosedWall(Vector2Int cell, int dir)
    {
        if (cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height) return true;
        return IsWallClosed(cell.x, cell.y, dir);
    }

    /// <summary>The cell next to `cell` on side `dir` (may be off the grid).</summary>
    public static Vector2Int NeighbourOf(Vector2Int cell, int dir) => Neighbour(cell, dir);

    /// <summary>The floor-level middle of the opening between `cell` and its `dir` neighbour.</summary>
    public Vector3 PassageCenter(Vector2Int cell, int dir) => CellCenter(cell.x, cell.y) + DirVector(dir) * (cellSize * 0.5f);

    private InteractableKit EnsureNightmareKit()
    {
        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        return interactableKit;
    }

    private static void ShuffleWith<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            T t = list[i];
            list[i] = list[j];
            list[j] = t;
        }
    }

    private void ReserveWall(Vector2Int cell, int dir)
    {
        if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
        {
            taken = new List<Vector3>();
            _propWalls[cell] = taken;
        }
        Vector3 v = DirVector(dir);
        if (!taken.Contains(v)) taken.Add(v);
    }

    private bool WallReserved(Vector2Int cell, int dir) => _propWalls.TryGetValue(cell, out List<Vector3> taken) && taken.Contains(DirVector(dir));

    // ---------------------------------------------------------------- floor 3: the drawers (pre-bake)

    /// <summary>
    /// Floor 3 (Elias): up to drawerBankCount banks of four morgue drawers on the END wall of the kept dead ends (topped up from ordinary cells with a free wall
    /// when the maze has fewer than two). Only the WALL is reserved in _propWalls - never the cell - so an A10 bait star can still sit in the dead end in front
    /// of its bank. Not a Shiftable. The farthest bank is YOURS (the tag carries the player's number), the next is drawer FIVE (E. VOGT, his toe tag on the
    /// tray; case file 102 stays on its wall). Called after ReserveGates and before the props and the bake, so the cabinets carve the NavMesh like lockers.
    /// </summary>
    private void BuildMorgueDrawers(System.Random rng)
    {
        _drawerBanks.Clear();
        _drawerBankCells.Clear();
        int floor = _profile != null ? _profile.Floor : 0;
        if (WingNightmares.StoryFloor(floor) != 3 || !terror.morgueDrawers || drawerBankCount <= 0) return;

        InteractableKit kit = EnsureNightmareKit();
        if (kit == null || !kit.HasMorgueDrawerBank)
        {
            Debug.LogError("MazeGenerator: no InteractableKit morgue drawer bank in the scene - skipping floor 3's drawers. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        Vector2Int aiCell = FarthestCell();
        List<(Vector2Int cell, int dir)> pool = new List<(Vector2Int, int)>();
        foreach (Vector2Int cell in _keptDeadEnds)
        {
            if (cell == Vector2Int.zero || cell == aiCell || IsLayoutReserved(cell) || _lockerCells.Contains(cell) || _setPieceCells.Contains(cell)) continue;
            if (WallCount(cell.x, cell.y) != 3) continue;
            int open = -1;
            for (int d = 0; d < 4; d++) if (!IsWallClosed(cell.x, cell.y, d)) open = d;
            if (open < 0) continue;
            int end = (open + 2) % 4;
            if (!IsWallClosed(cell.x, cell.y, end) || WallReserved(cell, end)) continue;
            pool.Add((cell, end));
        }
        ShuffleWith(pool, rng);

        List<(Vector2Int cell, int dir)> chosen = new List<(Vector2Int, int)>();
        PickApart(pool, chosen, drawerBankCount);

        if (chosen.Count < 2)
        {
            int maxDistance = 0;
            for (int x = 0; x < width; x++) for (int z = 0; z < height; z++) maxDistance = Mathf.Max(maxDistance, _distance[x, z]);

            List<(Vector2Int cell, int dir)> fallback = new List<(Vector2Int, int)>();
            for (int x = 0; x < width; x++)
            {
                for (int z = 0; z < height; z++)
                {
                    Vector2Int cell = new Vector2Int(x, z);
                    if (cell == Vector2Int.zero || cell == aiCell || IsLayoutReserved(cell) || _lockerCells.Contains(cell) || _setPieceCells.Contains(cell)) continue;
                    if (WallCount(x, z) < 2 || _distance[x, z] < maxDistance * 0.25f) continue;

                    List<int> free = new List<int>();
                    for (int d = 0; d < 4; d++) if (IsWallClosed(x, z, d) && !WallReserved(cell, d)) free.Add(d);
                    if (free.Count == 0) continue;
                    fallback.Add((cell, free[0]));
                }
            }
            ShuffleWith(fallback, rng);
            // Keep what the dead ends gave; top up apart from it.
            List<(Vector2Int cell, int dir)> merged = new List<(Vector2Int, int)>(chosen);
            merged.AddRange(fallback);
            chosen.Clear();
            PickApart(merged, chosen, drawerBankCount);
            if (chosen.Count < 2) Debug.LogWarning($"MazeGenerator: only {chosen.Count} morgue drawer bank(s) fit on floor {floor}.", this);
        }

        // Farthest from the start first: the first is YOURS, the second is FIVE.
        chosen.Sort((a, b) => _distance[b.cell.x, b.cell.y].CompareTo(_distance[a.cell.x, a.cell.y]));
        bool haveYours = chosen.Count >= 2;
        HashSet<int> used = new HashSet<int>();
        string mark = WingNightmares.PatientMark();

        for (int i = 0; i < chosen.Count; i++)
        {
            (Vector2Int cell, int dir) = chosen[i];
            MorgueDrawerBank.Role role = haveYours ? (i == 0 ? MorgueDrawerBank.Role.Yours : i == 1 ? MorgueDrawerBank.Role.Five : MorgueDrawerBank.Role.None)
                : (i == 0 ? MorgueDrawerBank.Role.Five : MorgueDrawerBank.Role.None);
            if (!haveYours && i == 0) Debug.Log("MazeGenerator: only one morgue drawer bank fits, so there is no drawer with the player's number on this floor.", this);

            // The two roles open a bottom-row drawer (trays 2 and 3 are the lower row): below the player's eye, so what lies on the tray can be seen.
            int openTray = role != MorgueDrawerBank.Role.None ? 2 + rng.Next(2) : rng.Next(4);
            string[] tags = new string[4];
            used.Clear();
            for (int t = 0; t < 4; t++)
            {
                int n;
                do { n = rng.Next(1, 41); } while (n == 3 || n == 5 || n == 7 || n == 11 || !used.Add(n));
                tags[t] = n.ToString();
            }
            if (role == MorgueDrawerBank.Role.Five) tags[openTray] = L10n.T("world.nightmare.drawer.elias");
            if (role == MorgueDrawerBank.Role.Yours) tags[openTray] = L10n.T("world.nightmare.drawer.yours", ("mark", mark));

            Vector3 dirVec = DirVector(dir);
            Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * (cellSize * 0.5f - wallThickness * 0.5f);
            MorgueDrawerBank bank = Instantiate(kit.MorgueDrawerBank, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
            bank.gameObject.SetActive(true);
            bank.name = $"MorgueDrawers_{cell.x}_{cell.y}_{role}";
            bank.Configure(role, openTray, tags);
            ReserveWall(cell, dir); // the wall only: never _setPieceCells / _lockerCells, or A10's bait star in this dead end would vanish
            _drawerBanks.Add(bank);
            _drawerBankCells.Add(cell);
            terror.Log($"Morgue drawers {role} at {cell} wall {dir} (tray {openTray})", bank);
        }
    }

    /// <summary>Takes up to `want` from `pool` (in order) keeping every pair at least 3 cells apart (Chebyshev).</summary>
    private static void PickApart(List<(Vector2Int cell, int dir)> pool, List<(Vector2Int cell, int dir)> chosen, int want)
    {
        foreach ((Vector2Int cell, int dir) candidate in pool)
        {
            if (chosen.Count >= want) break;
            bool apart = true;
            foreach ((Vector2Int cell, int dir) other in chosen)
            {
                if (Mathf.Max(Mathf.Abs(candidate.cell.x - other.cell.x), Mathf.Abs(candidate.cell.y - other.cell.y)) < 3) { apart = false; break; }
            }
            if (apart) chosen.Add(candidate);
        }
    }

    // ---------------------------------------------------------------- floors 4 and 5 (post-bake)

    /// <summary>Called right after BuildPatientFile, once the lamps, doors, fuses, keys and notes are final. One stream, +27; draws nothing off floors 4 and 5.</summary>
    private void BuildNightmarePostBake(int usedSeed)
    {
        _ampoules.Clear();
        _ampouleCells.Clear();
        _directorNotices.Clear();
        _directorNoticeCells.Clear();
        int floor = _profile != null ? WingNightmares.StoryFloor(_profile.Floor) : 0;
        if (floor == 4) BuildAmpoules(new System.Random(usedSeed + NightmareSalt));
        else if (floor == 5) BuildDirectorNotices(new System.Random(usedSeed + NightmareSalt));
    }

    /// <summary>
    /// Floor 4 (Dr. Weiss: "They'll be wherever the light isn't"): ampouleCount ampoules in cells no lamp reaches (not a lamp cell and no open neighbour that
    /// is one - dark by construction, independent of a Blackout floor's power state), at least 3 BFS steps from the start and 3 cells apart. On the floor 0.35 m
    /// out from a closed wall; a trigger collider only.
    /// </summary>
    private void BuildAmpoules(System.Random rng)
    {
        if (!terror.ampoules || ampouleCount <= 0) return;
        InteractableKit kit = EnsureNightmareKit();
        if (kit == null || !kit.HasNightmareAmpoule)
        {
            Debug.LogError("MazeGenerator: no InteractableKit ampoule in the scene - skipping floor 4's ampoules. Run KEEP IT LIT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        HashSet<Vector2Int> lamps = new HashSet<Vector2Int>(_lampCells);
        List<Vector2Int> strict = new List<Vector2Int>();
        List<Vector2Int> relaxed = new List<Vector2Int>();
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == Vector2Int.zero || _distance[x, z] <= 3) continue;
                if (_starCells.Contains(cell) || _lockerCells.Contains(cell) || _setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell) || IsLayoutReserved(cell)) continue;
                if (lamps.Contains(cell)) continue;
                relaxed.Add(cell);
                bool nearLamp = false;
                for (int d = 0; d < 4 && !nearLamp; d++)
                {
                    if (!IsWallClosed(x, z, d) && lamps.Contains(Neighbour(cell, d))) nearLamp = true;
                }
                if (!nearLamp) strict.Add(cell);
            }
        }

        List<Vector2Int> pool = strict.Count >= ampouleCount ? strict : relaxed;
        ShuffleWith(pool, rng);
        List<Vector2Int> chosen = new List<Vector2Int>();
        foreach (Vector2Int cell in pool)
        {
            if (chosen.Count >= ampouleCount) break;
            bool apart = true;
            foreach (Vector2Int other in chosen)
            {
                if (Mathf.Max(Mathf.Abs(cell.x - other.x), Mathf.Abs(cell.y - other.y)) < 3) { apart = false; break; }
            }
            if (apart) chosen.Add(cell);
        }
        if (chosen.Count < ampouleCount) Debug.LogWarning($"MazeGenerator: only {chosen.Count} of {ampouleCount} ampoules fit on floor 4.", this);

        foreach (Vector2Int cell in chosen)
        {
            List<int> closed = new List<int>();
            for (int d = 0; d < 4; d++) if (IsWallClosed(cell.x, cell.y, d)) closed.Add(d);
            int wall = closed.Count > 0 ? closed[rng.Next(closed.Count)] : -1;
            Vector3 pos = wall >= 0 ? CellCenter(cell.x, cell.y) + DirVector(wall) * (cellSize * 0.5f - wallThickness * 0.5f - 0.35f) : CellCenter(cell.x, cell.y);
            float yaw = (float)(rng.NextDouble() * 360.0);

            NightmareAmpoule ampoule = Instantiate(kit.NightmareAmpoule, pos, Quaternion.Euler(0f, yaw, 0f), _mazeRoot);
            ampoule.gameObject.SetActive(true);
            ampoule.name = $"Ampoule_{cell.x}_{cell.y}";
            _ampoules.Add(ampoule);
            _ampouleCells.Add(cell);
        }
        terror.Log($"Ampoules at {string.Join(", ", _ampouleCells)}", this);
    }

    /// <summary>
    /// Floor 5 (the Director): the HOUSE RULES, printed and taped up - one on a free wall of the start cell (or of a neighbour open to it), the rest on free
    /// walls further in. Paper quad + text exactly like the loop corridor's notice (BuildPaperNotice).
    /// </summary>
    private void BuildDirectorNotices(System.Random rng)
    {
        if (!terror.houseRules || directorNoticeCount <= 0) return;

        Vector2Int startCell = Vector2Int.zero;
        bool placedFirst = false;
        for (int dir = 0; dir < 4 && !placedFirst; dir++)
        {
            if (!IsFreeWall(startCell, dir)) continue;
            placedFirst = PlaceHouseRules(startCell, dir);
        }
        for (int dir = 0; dir < 4 && !placedFirst; dir++)
        {
            if (IsWallClosed(startCell.x, startCell.y, dir)) continue;
            Vector2Int next = Neighbour(startCell, dir);
            if (next.x < 0 || next.y < 0 || next.x >= width || next.y >= height) continue;
            for (int wall = 0; wall < 4 && !placedFirst; wall++)
            {
                if (IsFreeWall(next, wall)) placedFirst = PlaceHouseRules(next, wall);
            }
        }

        // The rest: free walls, with the cells near the start pre-claimed so they land further in (the BuildPatientFile idiom).
        HashSet<Vector2Int> everything = new HashSet<Vector2Int>();
        for (int x = 0; x < width; x++) for (int z = 0; z < height; z++) everything.Add(new Vector2Int(x, z));
        HashSet<Vector2Int> localUsed = new HashSet<Vector2Int>();
        foreach (KeyValuePair<Vector2Int, int> kv in BfsWithin(Vector2Int.zero, everything))
        {
            if (kv.Value <= 4) localUsed.Add(kv.Key);
        }
        foreach (Vector2Int already in _directorNoticeCells) localUsed.Add(already);

        while (_directorNotices.Count < directorNoticeCount)
        {
            if (!TryPlaceOnFreeWall(rng, localUsed, out Vector2Int cell, out int dir))
            {
                Debug.LogWarning($"MazeGenerator: only {_directorNotices.Count} of {directorNoticeCount} house-rules notices fit on floor 5.", this);
                break;
            }
            PlaceHouseRules(cell, dir, alreadyReserved: true);
        }
        terror.Log($"House rules posted at {string.Join(", ", _directorNoticeCells)}", this);
    }

    private bool PlaceHouseRules(Vector2Int cell, int dir, bool alreadyReserved = false)
    {
        Vector3 towardWall = DirVector(dir);
        Vector3 position = CellCenter(cell.x, cell.y) + towardWall * (cellSize * 0.5f - wallThickness * 0.5f - LoopDecalWallGap) + Vector3.up * houseRulesHeight;
        Quaternion rotation = Quaternion.LookRotation(towardWall, Vector3.up) * Quaternion.Euler(0f, 0f, 1.5f);
        GameObject notice = BuildPaperNotice(position, rotation, _mazeRoot, L10n.T("world.nightmare.director.rules"), houseRulesPaper, houseRulesText);
        notice.name = $"HouseRules_{cell.x}_{cell.y}";
        if (!alreadyReserved) ReserveWall(cell, dir);
        _directorNotices.Add(notice);
        _directorNoticeCells.Add(cell);
        return true;
    }

    // ---------------------------------------------------------------- floor 1: the door that was not there (post-bake, at runtime)

    /// <summary>
    /// Floor 1 (Hilde): adds a Manual shutter door to the open passage between `cell` and its `dir` neighbour, chalked `chalk` on both faces. Refuses (false) for
    /// the super-easy mode, a closed wall, a passage that already has a door, the start cell, a star, locker, door or reserved cell, a gate or loop opening, or
    /// anything within two cells of another door. Normalised to (CellA, dir 0|1) so it matches what AllOpenPassages produces. Never touches _wallN/E/S/W (the BFS,
    /// the hatch and the NavMesh are unchanged); the door is post-bake like every door and never locks.
    /// </summary>
    public bool TryAddLateDoor(Vector2Int cell, int dir, string chalk, out MazeDoor door)
    {
        door = null;
        if (superEasyMode || dir < 0 || dir > 3 || cell.x < 0 || cell.y < 0 || cell.x >= width || cell.y >= height) return false;
        InteractableKit kit = EnsureNightmareKit();
        if (kit == null || kit.ShutterDoor == null || _mazeRoot == null) return false;
        if (IsWallClosed(cell.x, cell.y, dir)) return false;

        Vector2Int other = Neighbour(cell, dir);
        if (other.x < 0 || other.y < 0 || other.x >= width || other.y >= height) return false;
        Passage p = dir <= 1 ? new Passage { CellA = cell, Dir = dir, CellB = other } : new Passage { CellA = other, Dir = dir - 2, CellB = cell };

        foreach (Passage existing in _doorPassages.Values)
        {
            if (IsSamePassage(p.CellA, p.Dir, existing)) return false;
        }
        foreach (Vector2Int c in new[] { p.CellA, p.CellB })
        {
            if (c == Vector2Int.zero || _starCells.Contains(c) || _lockerCells.Contains(c) || _doorUsedCells.Contains(c) || IsLayoutReserved(c)) return false;
            for (int d = 0; d < 4; d++)
            {
                if (IsLoopOpening(c.x, c.y, d) || IsGateOpening(c.x, c.y, d)) return false;
            }
        }
        if (TooCloseToPlacedDoors(p.CellA, new List<Passage>(_doorPassages.Values), 3)) return false;

        door = CreateDoor(MazeDoor.Kind.Manual, p, p.CellA, p.CellB, FindPlayer());
        _doorUsedCells.Add(p.CellA);
        _doorUsedCells.Add(p.CellB);
        if (!string.IsNullOrEmpty(chalk)) AddChalkNumber(door, chalk);
        return true;
    }

    /// <summary>A chalk numeral on each face of the door's sliding panel (it rides up and down with it). World text, parented keeping its world scale.</summary>
    private void AddChalkNumber(MazeDoor door, string text)
    {
        Transform panel = door.Panel;
        if (panel == null) return;
        Renderer panelRenderer = panel.GetComponentInChildren<Renderer>();
        Bounds b = panelRenderer != null ? panelRenderer.bounds : new Bounds(door.transform.position + Vector3.up * 2f, Vector3.one);
        Vector3 normal = door.transform.forward;
        float half = Mathf.Abs(normal.x) * b.extents.x + Mathf.Abs(normal.z) * b.extents.z;

        TMP_FontAsset font = loopCounterFont != null ? loopCounterFont : RuntimeUi.ResolveFont();
        for (int side = -1; side <= 1; side += 2)
        {
            GameObject go = new GameObject(side > 0 ? "ChalkA" : "ChalkB");
            // A text mesh is seen from behind its forward, so forward points into the door.
            go.transform.SetPositionAndRotation(
                new Vector3(b.center.x, FloorTop + extraDoorChalkHeight, b.center.z) + normal * side * (half + 0.03f),
                Quaternion.LookRotation(-normal * side, Vector3.up));
            TextMeshPro t = go.AddComponent<TextMeshPro>();
            if (font != null) t.font = font;
            if (loopCounterFont != null && loopCounterTextMaterial != null) t.fontSharedMaterial = loopCounterTextMaterial;
            t.text = text;
            t.fontSize = extraDoorChalkSize;
            t.fontStyle = loopCounterFont != null ? FontStyles.Normal : FontStyles.Bold;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0.82f, 0.8f, 0.72f);
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(1f, 1f);
            go.transform.SetParent(panel, true); // keeps the world scale: the panel's own non-uniform scale is compensated
        }
    }

    // ---------------------------------------------------------------- DebugMap (m banks, a ampoules, h notices)

    private void AddNightmareMarks(string[,] marks)
    {
        AddNightmareMarks(marks, _drawerBankCells, "m");
        AddNightmareMarks(marks, _ampouleCells, "a");
        AddNightmareMarks(marks, _directorNoticeCells, "h");
    }

    private static void AddNightmareMarks(string[,] marks, List<Vector2Int> cells, string glyph)
    {
        foreach (Vector2Int c in cells)
        {
            if ((marks[c.x, c.y] ?? "").Length < 3) marks[c.x, c.y] += glyph;
        }
    }

    private void AppendNightmareLegend(StringBuilder sb)
    {
        if (_drawerBanks.Count > 0)
        {
            sb.Append("m drawer bank:");
            for (int i = 0; i < _drawerBanks.Count; i++)
            {
                MorgueDrawerBank bank = _drawerBanks[i];
                if (bank != null) sb.Append($" {_drawerBankCells[i]} {bank.BankRole}{(bank.IsOpened ? " (open)" : "")}");
            }
            sb.Append('\n');
        }
        if (_ampoules.Count > 0)
        {
            sb.Append("a ampoule:");
            for (int i = 0; i < _ampoules.Count; i++)
            {
                if (_ampoules[i] != null) sb.Append($" {_ampouleCells[i]}{(_ampoules[i].IsTaken ? " (taken)" : "")}");
            }
            sb.Append('\n');
        }
        if (_directorNotices.Count > 0) sb.Append("h house rules: ").Append(string.Join(" ", _directorNoticeCells)).Append('\n');
    }

    /// <summary>F123 SetUpAtmosphere hook: late-binds the banks and ampoules to what exists by now.</summary>
    private void BindNightmares(WingNightmareDirector director, PlayerHud hud, DreadMeter dread)
    {
        foreach (MorgueDrawerBank bank in _drawerBanks) bank?.Bind(hud, aiFollower, dread);
        foreach (NightmareAmpoule ampoule in _ampoules) ampoule?.Bind(director);
    }
}
