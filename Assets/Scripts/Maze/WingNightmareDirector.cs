using UnityEngine;

/// <summary>
/// F123: each maze is its wing patient's nightmare. One director per story floor (never in a DESCENT: floor 0 does nothing), added by
/// MazeGenerator.SetUpAtmosphere like WorldShiftDirector. It owns the runtime half of the floor's signature:
///   floor 1 Hilde  - <see cref="ExtraDoors"/>: a door that was not there, behind the player, chalked 41
///   floor 2 Tomas  - <see cref="PipeKnocks"/>: two knocks (all clear) or three (hide), about one group in four lying
///   floor 3 Elias  - the <see cref="MorgueDrawerBank"/>s act on their own once built (nothing to tick here)
///   floor 4 Weiss  - <see cref="TakeAmpoule"/>: the fear goes quiet (<see cref="DreadMeter.Numb"/>, no heartbeat, full stamina), then wears off
///   floor 5 the Director - <see cref="HouseRules"/>: the printed rules and the ceiling PA that reports the patient who breaks them
/// Floor-keyed, never theme-keyed (floor 5 wears the Crypt look). No statics, no maze rng stream (the placement half draws from MazeGenerator's +27).
/// Never subscribes to GameManager.AllStarsCollected (a listener there takes over the floor clear): it polls the star counts instead.
/// </summary>
public class WingNightmareDirector : MonoBehaviour
{
    [Header("F123 floor 1 - Hilde (the extra door)")]
    [SerializeField] private ExtraDoors extraDoors = new ExtraDoors();

    [Header("F123 floor 2 - Tomas (the pipes knock)")]
    [SerializeField] private PipeKnocks pipeKnocks = new PipeKnocks();

    [Header("F123 floor 4 - Dr. Weiss (the ampoules)")]
    [Tooltip("Seconds the fear stays quiet after an ampoule")]
    [SerializeField, Min(1f)] private float ampouleNumbSeconds = 20f;
    [Tooltip("Seconds the legs do not tire after an ampoule")]
    [SerializeField, Min(0f)] private float ampouleStaminaHoldSeconds = 8f;
    [SerializeField, Min(0.5f)] private float doseSubtitleSeconds = 3.5f;

    [Header("F123 floor 5 - the Director (the house rules)")]
    [SerializeField] private HouseRules houseRules = new HouseRules();

    private MazeGenerator _maze;
    private int _floor;
    private Transform _player;
    private AIFollower _hunter;
    private PlayerStamina _stamina;
    private PlayerHud _hud;
    private HorrorAudioDirector _audio;
    private DreadMeter _dread;
    private GameOutcome _outcome;
    private TerrorSettings _terror = new TerrorSettings();

    private int _ampoulesTaken;
    private float _numbUntil = -1f;
    private bool _wearOffPending;

    public int Floor => _floor;
    public ExtraDoors ExtraDoorsState => extraDoors;
    public PipeKnocks Knocks => pipeKnocks;
    public HouseRules Rules => houseRules;
    public int AmpoulesTaken => _ampoulesTaken;

    /// <summary>Configures the wing nightmare director from the composition-root <see cref="RunContext"/>.</summary>
    public void Configure(RunContext ctx)
    {
        if (ctx == null) return;
        Camera cam = ctx.Camera != null ? ctx.Camera.GetComponent<Camera>() : null;
        int floor = ctx.Profile != null ? ctx.Profile.Floor : 0;
        Configure(ctx.Maze, floor, ctx.Player, cam, ctx.Hunter, ctx.Stealth, ctx.Stamina,
            ctx.ThrowController, ctx.Hud, ctx.Audio, ctx.DreadMeter, ctx.WorldShift, ctx.Outcome);
        if (ctx.Terror != null) ApplyTerror(ctx.Terror);
    }

    /// <summary>`floor` is the story floor 1-5, or 0 for a DESCENT (the director then does nothing).</summary>
    public void Configure(MazeGenerator maze, int floor, Transform player, Camera camera, AIFollower hunter, PlayerStealthState stealth, PlayerStamina stamina,
        ThrowController throws, PlayerHud hud, HorrorAudioDirector audioDirector, DreadMeter dread, WorldShiftDirector worldShift, GameOutcome outcome)
    {
        _maze = maze;
        _floor = WingNightmares.StoryFloor(floor);
        _player = player;
        _hunter = hunter;
        _stamina = stamina;
        _hud = hud;
        _audio = audioDirector;
        _dread = dread;
        _outcome = outcome;

        houseRules.Unbind();
        extraDoors.Bind(maze, player, hunter, worldShift, hud, _terror);
        pipeKnocks.Bind(this, maze, player, hunter, audioDirector, hud, _terror);
        houseRules.Bind(this, player, hunter, stealth, stamina, throws, hud, _terror);
        if (_floor != 5) houseRules.Unbind();
    }

    /// <summary>Which F123 features run, and the [Terror] log. Re-binds the plain classes so they hold the same settings object.</summary>
    public void ApplyTerror(TerrorSettings terror)
    {
        _terror = terror ?? new TerrorSettings();
        if (_maze == null) return;
        extraDoors.Bind(_maze, _player, _hunter, FindAnyObjectByType<WorldShiftDirector>(), _hud, _terror);
        pipeKnocks.Bind(this, _maze, _player, _hunter, _audio, _hud, _terror);
        if (_floor == 5) houseRules.Bind(this, _player, _hunter, _player != null ? _player.GetComponent<PlayerStealthState>() : null, _stamina,
            FindAnyObjectByType<ThrowController>(), _hud, _terror);
    }

    private void OnDestroy() => houseRules.Unbind();

    private bool IsLive() => GameFlow.IsRunActive && !GameOutcome.IsOver && (_outcome == null || !_outcome.IsEnding);

    /// <summary>Polled, never subscribed to: every star and every objective is in (the escape has started or is about to).</summary>
    private static bool EscapeStarted() => GameManager.StarsTotal > 0 && GameManager.StarsCollected >= GameManager.StarsTotal && GameManager.TokensDone >= GameManager.TokensTotal;

    private void Update()
    {
        if (_floor == 0 || _maze == null || _player == null || !IsLive()) return;

        switch (_floor)
        {
            case 1:
                extraDoors.Tick(EscapeStarted());
                break;
            case 2:
                pipeKnocks.Tick();
                break;
            case 4:
                if (_wearOffPending && Time.time >= _numbUntil)
                {
                    _wearOffPending = false;
                    _hud?.ShowSubtitle(L10n.T("sub.nightmare.weiss.wears_off"), 3f);
                }
                break;
            case 5:
                houseRules.Tick(Time.deltaTime);
                break;
        }
    }

    /// <summary>Floor 4: the dose. Quiet (no hunter noise), then twenty seconds with no heartbeat, no dread and no tired legs; taking another extends the timer.</summary>
    public void TakeAmpoule(NightmareAmpoule ampoule)
    {
        if (_floor != 4) return;
        _ampoulesTaken++;

        Vector3 at = ampoule != null ? ampoule.transform.position : _player.position;
        if (!GameAudio.PlayAt(GameAudio.Library.glassStep, at, 0.35f)) GameAudio.ClipAtPoint(DoorAudio.BuildClickClip(), at, 0.35f);

        _numbUntil = Mathf.Max(_numbUntil, Time.time + ampouleNumbSeconds);
        _wearOffPending = true;
        _dread?.Numb(ampouleNumbSeconds);
        _audio?.SuppressHeartbeat(ampouleNumbSeconds);
        _stamina?.Restore(ampouleStaminaHoldSeconds);

        _hud?.ShowSubtitle(L10n.T("sub.nightmare.weiss.dose." + Mathf.Min(_ampoulesTaken - 1, 2)), doseSubtitleSeconds);
        _terror.Log($"Ampoule {_ampoulesTaken} taken: numb for {ampouleNumbSeconds:0} s", this);
    }

    /// <summary>Right-click the component in Play: fires this floor's signature now (floor 1 a door, floor 2 a knock group, floor 5 a breach of rule 1), logging what it did.</summary>
    [ContextMenu("Debug Fire")]
    public void DebugFire() => DebugFire(false);

    /// <summary>Like DebugFire; on floor 1 `ignoreSight` also ignores the unobserved / distance rules (still needs a passage the maze accepts). Returns what happened.</summary>
    public string DebugFire(bool ignoreSight)
    {
        string result;
        switch (_floor)
        {
            case 1:
                result = extraDoors.DebugFire(ignoreSight) ? "an extra door was placed" : "no passage accepted a door";
                break;
            case 2:
                pipeKnocks.DebugKnockNow();
                result = "a knock group was played";
                break;
            case 5:
                houseRules.DebugBreach(HouseRules.Rule.Run);
                result = "a breach of rule 1 was announced";
                break;
            default:
                result = _floor == 0 ? "no wing nightmare here (DESCENT)" : "nothing to fire on floor " + _floor;
                break;
        }
        Debug.Log("WingNightmareDirector.DebugFire: " + result, this);
        return result;
    }
}
