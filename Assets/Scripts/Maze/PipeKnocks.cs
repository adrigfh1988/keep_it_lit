using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F123 floor 2 (Tomas Reiner, the stoker: "Two knocks: all clear. Three knocks: hide... since the Boiler Room was sealed, the pipes knock back"):
/// every 30-50 s the pipes in the walls nearby knock - twice when the hunter is far, three times when it is near. A real proximity cue, and per
/// the Tier A rules it sometimes lies (Bruno has heard "three knocks, then two"): the first group of the floor is always honest, never two lies in a
/// row, and the lies are off entirely with <see cref="TerrorSettings.lyingKnocks"/> or F122's <see cref="CampaignRules.UnreliableCues"/> = false.
/// The count carries the information, never the direction (the sound is never placed toward the hunter). Every group is also a subtitle.
/// Ticked by <see cref="WingNightmareDirector"/>, which hosts the coroutine; the tunables show in its Inspector.
/// </summary>
[System.Serializable]
public class PipeKnocks
{
    [Tooltip("Seconds after the run starts before the first group (min, max)")]
    [SerializeField] private Vector2 firstDelay = new Vector2(20f, 30f);
    [Tooltip("Seconds between groups (min, max)")]
    [SerializeField] private Vector2 interval = new Vector2(30f, 50f);
    [Tooltip("Seconds before trying again when a group was skipped")]
    [SerializeField, Min(0.5f)] private float retry = 5f;
    [Tooltip("The hunter counts as near at this many maze steps or fewer")]
    [SerializeField, Min(1)] private int nearSteps = 5;
    [Tooltip("Chance that a group (after the first, and never twice in a row) swaps its count")]
    [SerializeField, Range(0f, 1f)] private float lieChance = 0.25f;
    [SerializeField, Min(0.1f)] private float tapGap = 0.5f;
    [SerializeField, Min(0.5f)] private float subtitleSeconds = 2.5f;
    [Tooltip("The pipes run high (m above the floor)")]
    [SerializeField, Min(0.5f)] private float pipeHeight = 2.4f;
    [SerializeField, Range(0f, 1f)] private float volume = 0.9f;

    /// <summary>One played group, for the log and for tests.</summary>
    public struct Group
    {
        public int Shown, Truth;
        public bool Lie;
        public int HunterSteps;
    }

    private MazeGenerator _maze;
    private Transform _player;
    private AIFollower _hunter;
    private HorrorAudioDirector _audio;
    private PlayerHud _hud;
    private TerrorSettings _terror = new TerrorSettings();
    private MonoBehaviour _host;
    private AudioSource _source;
    private AudioClip _fallback;

    private float _next = -1f;
    private bool _playing;
    private bool _lastWasLie;
    public readonly List<Group> History = new List<Group>();

    public bool IsPlaying => _playing;

    public void Bind(MonoBehaviour host, MazeGenerator maze, Transform player, AIFollower hunter, HorrorAudioDirector audio, PlayerHud hud, TerrorSettings terror)
    {
        _host = host;
        _maze = maze;
        _player = player;
        _hunter = hunter;
        _audio = audio;
        _hud = hud;
        _terror = terror ?? new TerrorSettings();
        _next = Time.time + Random.Range(firstDelay.x, firstDelay.y);
        History.Clear();
        _lastWasLie = false;

        if (_source == null)
        {
            GameObject go = new GameObject("PipeKnock");
            go.transform.SetParent(host.transform, false);
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.spatialBlend = 0.6f;
            _source.rolloffMode = AudioRolloffMode.Linear;
            _source.minDistance = 2f;
            _source.maxDistance = 20f;
            GameSettings.RouteEffect(_source);
        }
    }

    /// <summary>One director tick while the run is live.</summary>
    public void Tick()
    {
        if (_maze == null || _player == null || _playing || !_terror.pipeKnocks || Time.time < _next) return;

        if (!CanKnockNow())
        {
            _next = Time.time + retry;
            return;
        }

        _next = Time.time + Random.Range(interval.x, interval.y);
        Knock(0);
    }

    /// <summary>Test hook: knocks now. count 0 decides as usual (the truth, or a lie by the usual rules); 2 or 3 forces that count (shown as a lie when it differs from the truth).</summary>
    public void DebugKnockNow(int count = 0)
    {
        if (_maze == null || _player == null || _playing) return;
        Knock(count);
    }

    private bool CanKnockNow()
    {
        if (_hunter != null && (_hunter.IsChasing || _hunter.IsCaptured)) return false;
        if (_audio != null && _audio.IsDucked) return false; // the held breath and the loop corridor stay silent
        if (_maze.Loop != null && _maze.Loop.IsInside) return false;
        return true;
    }

    private void Knock(int forced)
    {
        int steps = HunterSteps();
        bool near = steps >= 0 && steps <= nearSteps;
        int truth = near ? 3 : 2;

        int shown = truth;
        if (forced == 2 || forced == 3)
        {
            shown = forced;
        }
        else if (_terror.lyingKnocks && CampaignRules.UnreliableCues && History.Count > 0 && !_lastWasLie && Random.value < lieChance)
        {
            shown = truth == 3 ? 2 : 3;
        }
        // Assists can switch the lies off at any time: a forced count is a test, everything else is truthful then.
        if (forced == 0 && !(_terror.lyingKnocks && CampaignRules.UnreliableCues)) shown = truth;

        bool lie = shown != truth;
        _lastWasLie = lie;
        History.Add(new Group { Shown = shown, Truth = truth, Lie = lie, HunterSteps = steps });
        _terror.Log($"Pipe knocks {shown} (truth {truth}{(lie ? ", LIE" : "")}) hunter {(steps >= 0 ? steps + " steps" : "unknown")}", _host);

        _host.StartCoroutine(Play(shown));
    }

    /// <summary>Maze steps from the player to the hunter's cell, -1 when either is off the grid.</summary>
    private int HunterSteps()
    {
        if (_hunter == null) return -1;
        int[,] steps = _maze.StepsFrom(_player.position);
        if (steps == null || !_maze.TryWorldToCell(_hunter.transform.position, out Vector2Int cell)) return -1;
        return steps[cell.x, cell.y];
    }

    private IEnumerator Play(int count)
    {
        _playing = true;
        _source.transform.position = PickSource();
        if (_fallback == null) _fallback = DoorAudio.BuildPipeClankClip();

        for (int i = 0; i < count; i++)
        {
            AudioSlot slot = GameAudio.Library.pipeClank;
            AudioClip clip = slot.Pick();
            _source.clip = clip != null ? clip : _fallback;
            _source.pitch = slot.PickPitch();
            _source.volume = Mathf.Clamp01(volume * (clip != null ? slot.volume : 1f));
            _source.Play();
            yield return new WaitForSeconds(tapGap);
        }

        _hud?.ShowSubtitle(L10n.T(count == 3 ? "sub.nightmare.tomas.knocks_three" : "sub.nightmare.tomas.knocks_two"), subtitleSeconds);
        _playing = false;
    }

    /// <summary>A point on a wall of the player's cell, or of an open neighbour, high up where the pipes run, and never in the hunter's direction.</summary>
    private Vector3 PickSource()
    {
        Vector3 player = _player.position;
        Vector3 toHunter = _hunter != null ? _hunter.transform.position - player : Vector3.zero;
        toHunter.y = 0f;
        Vector3 fallback = player + Vector3.up * pipeHeight;

        if (!_maze.TryWorldToCell(player, out Vector2Int cell)) return fallback;

        int first = Random.Range(0, 4);
        for (int attempt = 0; attempt < 8; attempt++)
        {
            int dir = (first + attempt) % 4;
            Vector2Int from = cell;
            if (!_maze.IsClosedWall(cell, dir))
            {
                // An open side: knock on a wall of the neighbouring cell instead.
                from = MazeGenerator.NeighbourOf(cell, dir);
                if (from.x < 0 || from.y < 0 || from.x >= _maze.GridWidth || from.y >= _maze.GridHeight) continue;
                int wall = -1;
                int start = Random.Range(0, 4);
                for (int k = 0; k < 4; k++)
                {
                    int d = (start + k) % 4;
                    if (_maze.IsClosedWall(from, d)) { wall = d; break; }
                }
                if (wall < 0) continue;
                dir = wall;
            }

            Vector3 point = _maze.CellCenter(from.x, from.y) + MazeGenerator.DirVector(dir) * (_maze.CellSize * 0.5f) + Vector3.up * pipeHeight;
            Vector3 toPoint = point - player;
            toPoint.y = 0f;
            if (toHunter.sqrMagnitude > 1f && toPoint.sqrMagnitude > 0.01f && Vector3.Angle(toPoint, toHunter) < 50f) continue;
            return point;
        }

        return fallback;
    }
}
