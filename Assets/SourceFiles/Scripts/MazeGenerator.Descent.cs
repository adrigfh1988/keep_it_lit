using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F107, the maze side of DESCENT (the endless mode): where a descent level lives, the Inspector entry point for testing one, and the item caches
/// found in the maze (stream +24). The profile (FloorProfile.ForDescent), the state (DescentRun) and the flow (GameFlow, GameOutcome) live elsewhere.
/// </summary>
public partial class MazeGenerator
{
    /// <summary>
    /// Far from every authored piece: the FloorThemes gallery sits at z = -6, the shop and Intake west of the default maze, the hub to the east.
    /// The largest descent grid (16 x 13 cells of 5.5 m) spans x -70..18 and z -400..-328, so it overlaps none of them.
    /// </summary>
    private static readonly Vector3 DescentOrigin = new Vector3(-70f, 4.98f, -400f);

    private const int ItemCacheBase = 2;
    private const int ItemCacheDepthDivisor = 4;
    private const int ItemCacheMax = 5;
    private const int ItemCacheSalt = 24; // the +24 stream

    [Header("F107 - DESCENT")]
    [Tooltip("With Debug Start In Maze, Debug Floor = n starts a DESCENT at depth n (the smoke-test entry point). No effect otherwise.")]
    [SerializeField] private bool debugDescent = false;
    [Tooltip("An item cache prefers a cell at least this many BFS steps from the start (dead ends first)")]
    [SerializeField, Min(0)] private int itemCacheMinStartSteps = 4;

    /// <summary>F107 C: every item cache built this run, late-bound for its HUD/hunter/torch references.</summary>
    private readonly List<DescentItem> _itemCaches = new List<DescentItem>();

    /// <summary>F107 C: this level's item caches (empty outside a descent).</summary>
    public IReadOnlyList<DescentItem> ItemCaches => _itemCaches;

    /// <summary>F107 B3: the Inspector's way into a descent at a chosen depth: begins a fresh run and sets its depth to debugFloor. Only on a fresh start (see Awake).</summary>
    private void BeginDebugDescent()
    {
        DescentRun.Begin();
        DescentRun.Depth = debugFloor;
        GameFlow.IsDescent = true;
        Debug.Log($"[Descent] debug start at depth {debugFloor}", this);
    }

    /// <summary>F107 B3: the per-level setup that has to precede the profile: the seed for the end screen, the story floor number the lookups use, and the maze's home.</summary>
    private void EnterDescentLevel(int usedSeed)
    {
        DescentRun.LevelSeed = usedSeed;
        GameFlow.CurrentFloor = Mathf.Min(DescentRun.Depth, FloorProfile.FinalFloor);
        origin = DescentOrigin;
        Debug.Log($"[Descent] level -{DescentRun.Depth}, seed {usedSeed}, origin {origin}", this);
    }

    /// <summary>F107 B3: whether this level may have the looping corridor. The story's rule is "from floor 3, once a campaign"; a descent allows it from depth 6, at most once every 8 depths.</summary>
    private bool LoopAllowedHere()
    {
        if (GameFlow.IsDescent) return DescentRun.Depth >= 6 && DescentRun.Depth - DescentRun.LastLoopDepth >= 8;
        return _profile.Floor >= loopFromFloor && !LoopCorridor.UsedThisCampaign;
    }

    /// <summary>F107 B2: the banner's record line, by this level's depth against the deepest level ever reached. Empty outside a descent and before any record exists.</summary>
    private static string DescentRecordLineText()
    {
        if (!GameFlow.IsDescent || !DescentRecords.HasRecord) return "";

        int best = DescentRecords.BestDepth;
        if (DescentRun.Depth < best) return L10n.T("hud.descent.deepest", ("best", best));
        return L10n.T(DescentRun.Depth == best ? "hud.descent.at_record" : "hud.descent.past_record");
    }

    // ---------------------------------------------------------------- item caches (F107 C)

    /// <summary>
    /// F107 C: min(2 + depth / 4, 5) item caches against free walls, post-bake after BuildKeys, +24. Dead ends first, then cells at least
    /// itemCacheMinStartSteps from the start; never the start, a star, locker, set piece, door/vault/squeeze/loop cell or a cell with a hanging
    /// body. If a vault exists, one cache is forced inside it. What each holds is a weighted roll from the same stream (<see cref="DescentLoot"/>).
    /// </summary>
    private void BuildItemCaches(System.Random rng)
    {
        _itemCaches.Clear();
        if (!GameFlow.IsDescent || _profile == null) return;

        if (interactableKit == null) interactableKit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (interactableKit == null || !interactableKit.HasItemCache)
        {
            Debug.LogError("MazeGenerator: no InteractableKit item cache in the scene - the descent has no caches. Run LIGHTS OUT > Build > Interactables in the Editor, then save the scene.", this);
            return;
        }

        int depth = DescentRun.Depth;
        int want = Mathf.Min(ItemCacheBase + depth / ItemCacheDepthDivisor, ItemCacheMax);
        HashSet<Vector2Int> used = new HashSet<Vector2Int>();

        // A vault keeps its battery cell and gets one cache on top.
        foreach (Vector2Int vaultCell in _vaultForcedShardCells)
        {
            if (_itemCaches.Count >= want) break;
            if (used.Contains(vaultCell)) continue;
            PlaceItemCache(vaultCell, depth, rng, used);
        }

        List<Vector2Int> deadEnds = new List<Vector2Int>();
        List<Vector2Int> others = new List<Vector2Int>();
        HashSet<Vector2Int> starCells = new HashSet<Vector2Int>(_starCells);
        HashSet<Vector2Int> lockerCells = new HashSet<Vector2Int>(_lockerCells);
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < height; z++)
            {
                Vector2Int cell = new Vector2Int(x, z);
                if (cell == Vector2Int.zero || used.Contains(cell)) continue;
                if (starCells.Contains(cell) || lockerCells.Contains(cell) || _setPieceCells.Contains(cell) || _doorUsedCells.Contains(cell)) continue;
                if (IsLayoutReserved(cell) || HasCeilingProp(cell) || !HasFreeWall(cell)) continue;

                if (WallCount(x, z) == 3) deadEnds.Add(cell);
                else if (_distance[x, z] >= itemCacheMinStartSteps) others.Add(cell);
            }
        }
        Shuffle(deadEnds, rng);
        Shuffle(others, rng);
        deadEnds.AddRange(others);

        foreach (Vector2Int cell in deadEnds)
        {
            if (_itemCaches.Count >= want) break;
            PlaceItemCache(cell, depth, rng, used);
        }

        if (_itemCaches.Count < want) Debug.LogWarning($"MazeGenerator: only {_itemCaches.Count} of {want} item caches fit on level -{depth}.", this);
        terror.Log($"Item caches: {string.Join(", ", _itemCaches.ConvertAll(c => $"{c.name}={c.Rolled}"))}", this);
    }

    /// <summary>Puts one cache on a free wall of `cell` (floor level, +Z into the cell) and rolls its contents. False if the cell has no free wall.</summary>
    private bool PlaceItemCache(Vector2Int cell, int depth, System.Random rng, HashSet<Vector2Int> used)
    {
        if (!TryChooseFreeWall(cell, rng, out int dir)) return false;

        Vector3 dirVec = DirVector(dir);
        float backFaceDistance = cellSize * 0.5f - wallThickness * 0.5f;
        Vector3 pos = CellCenter(cell.x, cell.y) + dirVec * (backFaceDistance - 0.03f);

        DescentItem cache = Instantiate(interactableKit.ItemCache, pos, Quaternion.LookRotation(-dirVec, Vector3.up), _mazeRoot);
        cache.gameObject.SetActive(true);
        cache.name = $"ItemCache_{cell.x}_{cell.y}";
        cache.Configure(DescentLoot.Roll(rng, depth), depth);

        if (!_propWalls.TryGetValue(cell, out List<Vector3> taken))
        {
            taken = new List<Vector3>();
            _propWalls[cell] = taken;
        }
        taken.Add(dirVec);
        used.Add(cell);
        _itemCaches.Add(cache);
        return true;
    }
}
