using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F104 B9/B10: the maze quietly stops being the maze the player memorised. Both effects only ever touch dressing
/// (MazeGenerator.Shiftables) and only ever act out of sight:
///  - B9 displacement: now and then an unobserved prop that the player passed a while ago switches to its next look-alike model
///    ("the corpse lay the other way"). Change blindness makes most players doubt themselves rather than notice.
///  - B10 lying landmarks: once per floor, from floor 2, a set piece the player has seen appears a second time on a wall they have
///    also walked past ("I turn left at the bloody bath" - and now there are two baths). The copy is visual only.
/// Never moves or alters a gameplay object (stars, keys, doors, lockers, fuses, pickups are not Shiftables), draws nothing from a maze rng
/// (UnityEngine.Random only) and acts only while a run is live and the hunter is not chasing.
///
/// No execution-order attribute: everything arrives through Configure, after MazeGenerator built the world.
/// </summary>
public class WorldShiftDirector : MonoBehaviour
{
    [Header("F104 B9 - displacement")]
    [Tooltip("Seconds between displacements")]
    [SerializeField] private float displacementInterval = 90f;
    [Tooltip("Seconds before trying again when nothing was eligible")]
    [SerializeField] private float displacementRetry = 10f;
    [Tooltip("A prop's cell must have been visited at least this long ago (s), so the player has had time to remember it")]
    [SerializeField] private float displacementMinSeenAge = 30f;

    [Header("F104 B10 - lying landmarks")]
    [Tooltip("Floor from which a twin may appear")]
    [SerializeField] private int twinFromFloor = 2;
    [Tooltip("Set-piece cells the player must have visited before a twin may appear")]
    [SerializeField] private int twinVisitedSetPieces = 2;
    [Tooltip("Seconds that must pass after the first set piece was visited (s)")]
    [SerializeField] private float twinDelay = 60f;
    [Tooltip("The twin's cell is at least this many cells (Chebyshev) from its source")]
    [SerializeField] private int twinMinCellDistance = 3;
    [Tooltip("Random visited cells tried per check when looking for a spot for the twin")]
    [SerializeField] private int twinAttemptsPerCheck = 8;

    [Header("F127 - unobserved wall shifts")]
    [Tooltip("Seconds into the run before the first shifting passage pair may swap")]
    [SerializeField] private float wallShiftMinRunSeconds = 45f;
    [Tooltip("Seconds between wall shifts when multiple pairs exist")]
    [SerializeField] private float wallShiftInterval = 65f;
    [Tooltip("Seconds before trying again when no pair is currently unobserved")]
    [SerializeField] private float wallShiftRetry = 8f;

    [Header("Observation")]
    [Tooltip("Seconds between recording which cell the player is in, and between B10 checks")]
    [SerializeField] private float sampleSeconds = 0.5f;
    [Tooltip("Something this close to the player (in cells) is never changed, seen or not")]
    [SerializeField] private float safeDistanceCells = 2f;
    [Tooltip("Heights above the point of the two sight lines that must both be blocked for it to count as unobserved (m)")]
    [SerializeField] private Vector2 sightLineHeights = new Vector2(1.5f, 0.3f);
    [Tooltip("Sight lines stop this short of the point, so the wall it hangs on does not block its own line (m)")]
    [SerializeField] private float sightLineStopShort = 0.1f;

    private MazeGenerator _maze;
    private Transform _player;
    private Camera _camera;
    private AIFollower _follower;
    private GameOutcome _outcome;
    private TerrorSettings _terror = new TerrorSettings();

    private readonly Dictionary<Vector2Int, float> _visitedAt = new Dictionary<Vector2Int, float>();
    private readonly List<Vector2Int> _visitedCells = new List<Vector2Int>();
    private readonly List<int> _visitedSetPieces = new List<int>();
    private readonly RaycastHit[] _hits = new RaycastHit[16];

    private float _nextSample;
    private float _nextDisplacement;
    private float _nextWallShift;
    private bool _twinPlaced;

    /// <summary>Called by MazeGenerator.SetUpAtmosphere once the world, the camera and the hunter exist.</summary>
    public void Configure(MazeGenerator maze, Transform player, Camera camera, AIFollower follower, GameOutcome outcome)
    {
        _maze = maze;
        _player = player;
        _camera = camera;
        _follower = follower;
        _outcome = outcome;
        _nextDisplacement = Time.time + displacementInterval;
        _nextWallShift = Time.time + wallShiftMinRunSeconds;
    }

    /// <summary>F104: which Tier B features run, and the [Terror] log.</summary>
    public void ApplyTerror(TerrorSettings terror)
    {
        _terror = terror ?? new TerrorSettings();
    }

    private bool IsLive()
    {
        return GameFlow.IsRunActive && !GameOutcome.IsOver && (_outcome == null || !_outcome.IsEnding);
    }

    private void Update()
    {
        if (_maze == null || _player == null || _camera == null || !IsLive()) return;

        if (Time.time >= _nextSample)
        {
            _nextSample = Time.time + sampleSeconds;
            RecordVisit();
            if (_terror.lyingLandmarks) TryPlaceTwin();
        }

        if (_terror.displacement && Time.time >= _nextDisplacement) TryDisplace();
        if (_terror.wallShifts && Time.time >= _nextWallShift) TryShiftWall();
    }

    private void TryShiftWall()
    {
        if (Time.time - GameFlow.RunStartTime < wallShiftMinRunSeconds || HunterChasing())
        {
            _nextWallShift = Time.time + wallShiftRetry;
            return;
        }

        if (_maze.TryTriggerWallShift(this, _player.position))
        {
            _nextWallShift = Time.time + wallShiftInterval;
        }
        else
        {
            _nextWallShift = Time.time + wallShiftRetry;
        }
    }

    private void RecordVisit()
    {
        if (!_maze.TryWorldToCell(_player.position, out Vector2Int cell) || _visitedAt.ContainsKey(cell)) return;

        _visitedAt[cell] = Time.time;
        _visitedCells.Add(cell);
    }

    private bool HunterChasing()
    {
        return _follower != null && _follower.IsChasing;
    }

    // ---------------------------------------------------------------- observation

    /// <summary>
    /// True when the player cannot be looking at `point`: it is outside the camera's view, or both a high and a low sight line to it
    /// are blocked - and it is more than a couple of cells away, so nothing changes where it could be heard or glimpsed. Hits on the
    /// player, the hunter and `ignore` (the thing itself) do not block.
    /// </summary>
    public bool IsUnobserved(Vector3 point, Transform ignore)
    {
        if (Vector3.Distance(_player.position, point) <= safeDistanceCells * _maze.CellSize) return false;

        Vector3 viewport = _camera.WorldToViewportPoint(point + Vector3.up * sightLineHeights.x);
        bool inView = viewport.z > 0f && viewport.x >= 0f && viewport.x <= 1f && viewport.y >= 0f && viewport.y <= 1f;
        if (!inView) return true;

        return IsBlocked(point + Vector3.up * sightLineHeights.x, ignore) && IsBlocked(point + Vector3.up * sightLineHeights.y, ignore);
    }

    /// <summary>F123: true when `point` is inside the camera's view, within `maxDistance` and the 1.5 m sight line to it is not blocked (hits on `ignore`, the thing itself, do not block): the player can see it right now.</summary>
    public bool IsInSight(Vector3 point, float maxDistance, Transform ignore = null)
    {
        if (_camera == null || _player == null) return false;
        if (Vector3.Distance(_camera.transform.position, point) > maxDistance) return false;
        Vector3 target = point + Vector3.up * sightLineHeights.x;
        Vector3 viewport = _camera.WorldToViewportPoint(target);
        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) return false;
        return !IsBlocked(target, ignore);
    }

    /// <summary>F123: when (Time.time) the player first stood in `cell` this floor, false if never.</summary>
    public bool TryGetVisitedAt(Vector2Int cell, out float time) => _visitedAt.TryGetValue(cell, out time);

    /// <summary>F123: every cell the player has stood in, in the order first visited.</summary>
    public IReadOnlyList<Vector2Int> VisitedCells => _visitedCells;

    private bool IsBlocked(Vector3 to, Transform ignore)
    {
        Vector3 from = _camera.transform.position;
        Vector3 diff = to - from;
        float distance = diff.magnitude - sightLineStopShort;
        if (distance <= 0f) return false;

        int count = Physics.RaycastNonAlloc(from, diff.normalized, _hits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = _hits[i].transform;
            if (IsPartOf(hit, _player) || (_follower != null && IsPartOf(hit, _follower.transform)) || (ignore != null && IsPartOf(hit, ignore))) continue;
            return true;
        }

        return false;
    }

    private static bool IsPartOf(Transform candidate, Transform root)
    {
        while (candidate != null)
        {
            if (candidate == root) return true;
            candidate = candidate.parent;
        }

        return false;
    }

    // ---------------------------------------------------------------- B9: displacement

    private void TryDisplace()
    {
        if (HunterChasing())
        {
            _nextDisplacement = Time.time + displacementRetry;
            return;
        }

        IReadOnlyList<ShiftablePlacement> shiftables = _maze.Shiftables;
        int eligible = 0;
        int chosen = -1;
        for (int i = 0; i < shiftables.Count; i++)
        {
            ShiftablePlacement placement = shiftables[i];
            if (placement.Variants == null || placement.Variants.Count < 2 || placement.Root == null || !placement.Root.gameObject.activeInHierarchy) continue;
            if (placement.Root.GetComponent<LivingBody>() != null) continue; // F107 D4: a living body (or its decoy) must keep the figure it was built around
            if (!_visitedAt.TryGetValue(placement.Cell, out float visitedAt) || Time.time - visitedAt < displacementMinSeenAge) continue;
            if (!IsUnobserved(placement.Root.position, placement.Root)) continue;

            // Reservoir pick: uniform over the eligible ones without a scratch list.
            eligible++;
            if (Random.Range(0, eligible) == 0) chosen = i;
        }

        if (chosen < 0)
        {
            _nextDisplacement = Time.time + displacementRetry;
            return;
        }

        shiftables[chosen].Variants.ApplyNext();
        _terror.Log("Displaced " + shiftables[chosen].Root.name, this);
        _nextDisplacement = Time.time + displacementInterval;
    }

    // ---------------------------------------------------------------- B10: landmarks that lie

    /// <summary>Once per floor, from floor 2: copies a visited set piece onto a free wall of another visited cell the player cannot see.</summary>
    private void TryPlaceTwin()
    {
        if (_twinPlaced || GameFlow.CurrentFloor < twinFromFloor || HunterChasing()) return;

        // The visited set pieces, and when the first of them was visited.
        IReadOnlyList<ShiftablePlacement> shiftables = _maze.Shiftables;
        _visitedSetPieces.Clear();
        float firstVisit = float.MaxValue;
        for (int i = 0; i < shiftables.Count; i++)
        {
            if (!shiftables[i].IsSetPiece || shiftables[i].Root == null || !_visitedAt.TryGetValue(shiftables[i].Cell, out float visitedAt)) continue;

            _visitedSetPieces.Add(i);
            firstVisit = Mathf.Min(firstVisit, visitedAt);
        }

        if (_visitedSetPieces.Count < twinVisitedSetPieces || Time.time - firstVisit < twinDelay) return;

        ShiftablePlacement source = shiftables[_visitedSetPieces[Random.Range(0, _visitedSetPieces.Count)]];
        for (int attempt = 0; attempt < twinAttemptsPerCheck; attempt++)
        {
            Vector2Int cell = _visitedCells[Random.Range(0, _visitedCells.Count)];
            if (Mathf.Max(Mathf.Abs(cell.x - source.Cell.x), Mathf.Abs(cell.y - source.Cell.y)) < twinMinCellDistance) continue;
            if (TryTwinInCell(source, cell)) return;
        }
    }

    /// <summary>Tries the cell's four walls in a random order; the first free, unobserved one gets the twin.</summary>
    private bool TryTwinInCell(ShiftablePlacement source, Vector2Int cell)
    {
        int firstDir = Random.Range(0, 4);
        for (int i = 0; i < 4; i++)
        {
            int dir = (firstDir + i) % 4;
            if (!_maze.IsFreeWall(cell, dir)) continue;

            Vector3 wallPoint = _maze.CellCenter(cell.x, cell.y) + MazeGenerator.DirVector(dir) * (_maze.CellSize * 0.5f);
            if (!IsUnobserved(wallPoint, null)) continue;
            if (!_maze.TryPlaceTwin(source, cell, dir, out Transform twin)) continue;

            _twinPlaced = true;
            _terror.Log($"Twin of {source.Root.name} at {cell}", twin);
            return true;
        }

        return false;
    }
}

