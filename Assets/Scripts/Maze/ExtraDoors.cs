using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// F123 floor 1 (Hilde Brandt, "always one more door than yesterday"): some time into the floor, out of sight and behind the player, a Manual shutter
/// door drops into a corridor they have already walked, chalked 41 (the second, 42). It is a real door (MazeGenerator.TryAddLateDoor): it never locks,
/// E opens it, the hunter bashes through it, it shows on the floor map. Ticked by <see cref="WingNightmareDirector"/>; the tunables show in its Inspector.
/// Never fires once the escape has started, while the hunter chases, or in the super-easy mode (no doors there at all).
/// </summary>
[System.Serializable]
public class ExtraDoors
{
    [Tooltip("Run seconds before the first door may appear")]
    [SerializeField, Min(0f)] private float firstAfter = 75f;
    [Tooltip("Cells the player must have visited before a door may appear")]
    [SerializeField, Min(1)] private int minVisited = 10;
    [Tooltip("Seconds between two doors")]
    [SerializeField, Min(0f)] private float gap = 120f;
    [SerializeField, Range(0, 4)] private int max = 2;
    [Tooltip("Both cells of the passage must have been walked at least this long ago (s)")]
    [SerializeField, Min(0f)] private float minAge = 20f;
    [Tooltip("The door is more than this many cells (flat) from the player")]
    [SerializeField, Min(0f)] private float minCells = 3f;
    [Tooltip("The hunter is at least this many cell sizes from the door")]
    [SerializeField, Min(0f)] private float hunterClearCells = 1.5f;
    [Tooltip("Seconds between attempts")]
    [SerializeField, Min(0.25f)] private float recheck = 2f;
    [Tooltip("The player has 'seen' the door within this distance (m)")]
    [SerializeField, Min(1f)] private float seenDistance = 12f;
    [SerializeField, Min(0.5f)] private float seenSubtitleSeconds = 3f;

    private MazeGenerator _maze;
    private Transform _player;
    private AIFollower _hunter;
    private WorldShiftDirector _shift;
    private PlayerHud _hud;
    private TerrorSettings _terror = new TerrorSettings();

    private readonly List<MazeDoor> _doors = new List<MazeDoor>();
    private readonly List<bool> _seen = new List<bool>();
    private readonly List<Candidate> _candidates = new List<Candidate>();
    private readonly HashSet<(Vector2Int, int)> _tried = new HashSet<(Vector2Int, int)>();
    private float _nextTry;
    private float _nextSeenCheck;
    private float _lastSpawn = -9999f;

    private struct Candidate
    {
        public Vector2Int Cell;
        public int Dir;
        public float Recency;
    }

    public int Count => _doors.Count;
    public IReadOnlyList<MazeDoor> Doors => _doors;

    public void Bind(MazeGenerator maze, Transform player, AIFollower hunter, WorldShiftDirector shift, PlayerHud hud, TerrorSettings terror)
    {
        _maze = maze;
        _player = player;
        _hunter = hunter;
        _shift = shift;
        _hud = hud;
        _terror = terror ?? new TerrorSettings();
    }

    /// <summary>One director tick while the run is live. `escapeStarted` = every star and objective is in (the hatch is open or opening).</summary>
    public void Tick(bool escapeStarted)
    {
        if (_maze == null || _player == null || _shift == null) return;

        TickSeen();

        if (!_terror.extraDoors || escapeStarted || _doors.Count >= max || Time.time < _nextTry) return;
        _nextTry = Time.time + recheck;

        float since = _doors.Count == 0 ? Time.time - GameFlow.RunStartTime - firstAfter : Time.time - _lastSpawn - gap;
        if (since < 0f || _shift.VisitedCells.Count < minVisited) return;
        if (_hunter != null && (_hunter.IsChasing || _hunter.IsCaptured)) return;

        TrySpawn(ignoreSight: false);
    }

    /// <summary>Test hook: places the next door now, ignoring the timers (and the sight rule too with `ignoreSight`). Still needs a passage the maze accepts.</summary>
    public bool DebugFire(bool ignoreSight)
    {
        if (_maze == null || _player == null || _shift == null) return false;
        return TrySpawn(ignoreSight);
    }

    /// <summary>Test/diagnostic: why no door is being placed right now (run time, visited cells, and for each candidate which rule refused it).</summary>
    public string Explain()
    {
        if (_maze == null || _shift == null) return "not bound";
        StringBuilder sb = new StringBuilder();
        sb.Append($"runTime {Time.time - GameFlow.RunStartTime:0}s visited {_shift.VisitedCells.Count}/{minVisited} doors {_doors.Count}/{max} extraDoors flag {_terror.extraDoors} hunterChasing {(_hunter != null && _hunter.IsChasing)} ");
        BuildCandidates(false);
        sb.Append($"candidates(aged) {_candidates.Count}: ");
        Vector3 player = _player.position; player.y = 0f;
        int shown = 0;
        foreach (Candidate c in _candidates)
        {
            if (shown++ >= 6) break;
            Vector3 centre = _maze.PassageCenter(c.Cell, c.Dir);
            Vector3 flat = centre; flat.y = 0f;
            string why = Vector3.Distance(flat, player) <= minCells * _maze.CellSize ? "near-player" : !_shift.IsUnobserved(centre, null) ? "observed" : "ok";
            sb.Append($"[{c.Cell}/{c.Dir} {why}] ");
        }
        return sb.ToString();
    }

    private bool TrySpawn(bool ignoreSight)
    {
        if (_doors.Count >= 4) return false;
        BuildCandidates(ignoreSight);
        _candidates.Sort((a, b) => b.Recency.CompareTo(a.Recency)); // most recently walked first: the corridor they just left

        Vector3 player = _player.position;
        player.y = 0f;
        foreach (Candidate c in _candidates)
        {
            Vector3 centre = _maze.PassageCenter(c.Cell, c.Dir);
            Vector3 flat = centre;
            flat.y = 0f;
            if (!ignoreSight && Vector3.Distance(flat, player) <= minCells * _maze.CellSize) continue;
            if (_hunter != null)
            {
                Vector3 h = _hunter.transform.position;
                h.y = 0f;
                if (Vector3.Distance(flat, h) < hunterClearCells * _maze.CellSize) continue;
            }
            if (!ignoreSight && !_shift.IsUnobserved(centre, null)) continue;

            string chalk = (41 + _doors.Count).ToString();
            if (!_maze.TryAddLateDoor(c.Cell, c.Dir, chalk, out MazeDoor door)) continue;

            _doors.Add(door);
            _seen.Add(false);
            _lastSpawn = Time.time;
            _shift.TryGetVisitedAt(c.Cell, out float visitedAt);
            _terror.Log($"Extra door {chalk} at ({centre.x:0.#},{centre.z:0.#}) dir {c.Dir} (visited {Time.time - visitedAt:0} s ago)", door);
            if (!GameAudio.PlayAt(GameAudio.Library.shutterSlam, centre, 0.9f)) GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.4f, 55f, 0.9f), centre, 0.9f);
            return true;
        }

        return false;
    }

    /// <summary>Every open passage between two cells the player walked at least `minAge` ago, once each, keyed by the later visit.</summary>
    private void BuildCandidates(bool ignoreAge)
    {
        _candidates.Clear();
        _tried.Clear();
        IReadOnlyList<Vector2Int> visited = _shift.VisitedCells;
        for (int i = 0; i < visited.Count; i++)
        {
            Vector2Int cell = visited[i];
            if (!_shift.TryGetVisitedAt(cell, out float timeA)) continue;
            for (int dir = 0; dir < 4; dir++)
            {
                if (_maze.IsClosedWall(cell, dir)) continue;
                Vector2Int other = MazeGenerator.NeighbourOf(cell, dir);
                if (!_shift.TryGetVisitedAt(other, out float timeB)) continue;
                if (!ignoreAge && (Time.time - timeA < minAge || Time.time - timeB < minAge)) continue;

                // Normalise to (cell, dir 0|1) so both ends of the passage give the same key.
                (Vector2Int, int) key = dir <= 1 ? (cell, dir) : (other, dir - 2);
                if (!_tried.Add(key)) continue;
                _candidates.Add(new Candidate { Cell = key.Item1, Dir = key.Item2, Recency = Mathf.Max(timeA, timeB) });
            }
        }
    }

    private void TickSeen()
    {
        if (_doors.Count == 0 || Time.time < _nextSeenCheck) return;
        _nextSeenCheck = Time.time + 0.25f;
        for (int i = 0; i < _doors.Count; i++)
        {
            if (_seen[i] || _doors[i] == null) continue;
            if (!_shift.IsInSight(_doors[i].transform.position, seenDistance, _doors[i].transform)) continue;
            _seen[i] = true;
            string key = "sub.nightmare.hilde.seen." + Mathf.Min(i, 1);
            _hud?.ShowSubtitle(L10n.T(key), seenSubtitleSeconds);
        }
    }
}
