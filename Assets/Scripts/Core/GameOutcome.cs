using UnityEngine;
using UnityEngine.AI;

public enum LoseReason
{
    Caught,
    OutOfTime
}

/// <summary>
/// Owns how a run ends: the capture sequence, silencing the world, and the end screen with its retry
/// buttons. Added at runtime by MazeGenerator. Single-shot - the first ending wins, and a hatch drop
/// or a timeout that lands mid-capture is ignored.
/// </summary>
public partial class GameOutcome : MonoBehaviour
{
    private static readonly Color Green = new Color(0.25f, 1f, 0.7f);
    private static readonly Color Red = new Color(0.9f, 0.15f, 0.1f);
    private static readonly Color Grey = new Color(0.75f, 0.75f, 0.78f);
    private static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    private static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);

    // Seconds into the capture sequence at which the torch flips. Ends off.
    private static readonly float[] FlickerTimes = { 0.5f, 0.56f, 0.62f, 0.68f, 0.74f, 0.80f, 0.9f };

    [Tooltip("How long the hunter stands stunned after Second Wind lets go (F36).")]
    [SerializeField] private float secondWindStunSeconds = 6f;

    private static bool _isOver;

    /// <summary>True once the run has ended. Static, so reset when the scene is torn down.</summary>
    public static bool IsOver
    {
        get => _isOver;
        private set
        {
            _isOver = value;
            GameFlow.SyncPhase();
        }
    }

    /// <summary>True while the capture sequence is playing.</summary>
    public bool IsEnding { get; private set; }

    /// <summary>The ways out of the finale (F93, F126 Ending C at the Last Supper table).</summary>
    public enum FinaleEnding { Discharged = 0, LightsOut = 1, Seated = 2 }

    private Transform _player;
    private AIFollower _follower;
    private HorrorAudioDirector _audio;
    private MazeEscape _escape;
    private Flashlight _flashlight;
    private int _seed;
    // F106: where CaptureSequence eases the camera when something other than the hunter caught the player (the loop corridor's Visitor).
    private Vector3 _captureLookAt;
    private bool _hasCaptureLookAt;
    private bool _caughtByHunter; // F120: set in HandleCaught right before a hunter death

    // F32/F37: the room between floors. May all stay null (escapeSequence off, or BindShop never called).
    private ShopRoom _shop;
    private MazeGenerator _maze;
    private PlayerHud _hud;
    private PlayerStealthState _stealth;
    private ContractTracker _contracts;
    private HabitTracker _habits;
    private bool _showingLedger;
    private GameObject _endRoot;          // F119: the win / lose / DESCENT end screen panel (MenuFocus keeps a gamepad on its buttons)
    private bool _continuePressed;
    private Color? _endingTitleColorOverride;

    // Kept only so ShowEndScreen can report the number after the fact.
    private Payout _floorPayout;
    private Payout _consolationPayout;

    private int _collected;
    private int _total;
    private float _runTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _isOver = false;
    }

    /// <summary>Follower, escape and flashlight may each be null; every use is guarded.</summary>
    public void Configure(Transform player, AIFollower follower, HorrorAudioDirector audio,
        MazeEscape escape, Flashlight flashlight, int seed)
    {
        _player = player;
        _audio = audio;
        _escape = escape;
        _flashlight = flashlight;
        _seed = seed;

        if (_follower != null) _follower.PlayerCaught -= HandleCaught;
        _follower = follower;
        if (_follower != null) _follower.PlayerCaught += HandleCaught;
    }

    /// <summary>Wired after escape and outcome.Configure, once the shop and stealth references exist.</summary>
    public void BindShop(ShopRoom shop, MazeGenerator maze, PlayerHud hud, PlayerStealthState stealth)
    {
        _shop = shop;
        _maze = maze;
        _hud = hud;
        _stealth = stealth;
    }

    /// <summary>F87: the floor's contract checker. May stay null; every use is guarded.</summary>
    public void BindContracts(ContractTracker contracts) => _contracts = contracts;

    /// <summary>F105 C6: the tracker FloorCleared asks to teach the hunter what this floor's play showed it.</summary>
    public void BindHabits(HabitTracker habits) => _habits = habits;

    private void Awake()
    {
        IsOver = false;
    }

    private void OnEnable()
    {
        GameManager.ProgressChanged += HandleProgress;
        if (_follower != null) _follower.PlayerCaught += HandleCaught;
    }

    private void OnDisable()
    {
        GameManager.ProgressChanged -= HandleProgress;
        if (_follower != null) _follower.PlayerCaught -= HandleCaught;
    }

    private void OnDestroy()
    {
        IsOver = false;
    }

    private void Update()
    {
        // Held every frame, for the same reason as the title screen: focus changes re-lock the cursor
        if (IsOver || _showingLedger) PlayerLock.SetCursorFree(true);
        PollLedgerInput();
        // F119: not the ledger (its CONTINUE is also read directly, so a pre-selected button would double-fire), and not while the DESCENT slip is still typing.
        if (IsOver && _endRoot != null && !_typing) MenuFocus.Keep(_endRoot, null);
    }

    private void HandleProgress(int collected, int total)
    {
        _collected = collected;
        _total = total;
    }

    private void HandleCaught()
    {
        if (IsOver || IsEnding) return;

        // F36 Second Wind: a release, not an invulnerability. Consumed automatically, once.
        if (PlayerInventory.Count(ShopItem.SecondWind) > 0 && _follower != null && _maze != null)
        {
            PlayerInventory.TryConsume(ShopItem.SecondWind);
            if (_grades != null) _grades.NoteSecondWind(); // F122
            StartCoroutine(SecondWindSequence(ReleaseHunter, "sub.second_wind.release"));
            return;
        }

        DescentRun.Cause = DescentCause.Hunter; // F107
        _caughtByHunter = true; // F120: the death replay is for the hunter only
        if (_replay != null) _replay.Freeze();
        Lose(LoseReason.Caught);
    }

    /// <summary>
    /// F106: the loop corridor's Visitor has the player. The same two outcomes as the hunter's catch, through the same single-shot guards: with a
    /// Second Wind it is consumed and the player is let go under a blackout (`onSecondWind` runs at full black and moves them out; the hunter is
    /// not touched); otherwise it is a real death, the camera easing onto `lookAt` (the Visitor's head) instead of the hunter's eyes.
    /// </summary>
    public void CaughtByLoopVisitor(Vector3 lookAt, System.Action onSecondWind)
    {
        CaughtBy(lookAt, onSecondWind, DescentCause.Visitor, EntityKind.None, "sub.loop.second_wind");
    }

    /// <summary>
    /// F107 D2: a maze entity that kills (the Listener) has the player. The same two outcomes as the hunter's catch, through the same single-shot
    /// guards: a held Second Wind is consumed and `onSecondWind` runs at full black (the entity warps itself away); otherwise a real death with
    /// the camera easing onto `lookAt`, and the descent end screen names `kind` as the cause.
    /// </summary>
    public void CaughtByEntity(Vector3 lookAt, System.Action onSecondWind, EntityKind kind)
    {
        CaughtBy(lookAt, onSecondWind, DescentCause.Entity, kind, "sub.loop.second_wind");
    }

    /// <summary>The shared body of <see cref="CaughtByLoopVisitor"/> and <see cref="CaughtByEntity"/>.</summary>
    private void CaughtBy(Vector3 lookAt, System.Action onSecondWind, DescentCause cause, EntityKind kind, string secondWindLineKey)
    {
        if (IsOver || IsEnding) return;

        if (PlayerInventory.TryConsume(ShopItem.SecondWind))
        {
            if (_grades != null) _grades.NoteSecondWind(); // F122
            StartCoroutine(SecondWindSequence(onSecondWind, secondWindLineKey));
            return;
        }

        _captureLookAt = lookAt;
        _hasCaptureLookAt = true;
        DescentRun.Cause = cause; // F107: read only by the descent end screen
        DescentRun.CauseEntity = kind;
        Lose(LoseReason.Caught);
    }

    /// <summary>The hunter's half of a Second Wind: warped to the far side of the maze and stunned. If it busted a locker, the player is still standing inside it with movement locked.</summary>
    private void ReleaseHunter()
    {
        _follower.Release(_maze.FarthestCellCenterFrom(_player.position), secondWindStunSeconds);

        // Locker.Leave() clears MovementLocked directly, so re-assert the freeze straight after -
        // the sequence is not done with the player yet.
        foreach (Locker locker in _maze.Lockers)
        {
            if (locker != null && locker.Occupied) locker.ForceLeave();
        }

        PlayerLock.Freeze(_player, true);
    }

    // ---------------------------------------------------------------- endings

    /// <summary>The hatch was reached. Below the final floor this is not an ending: black, teleport, ledger, shop.</summary>
    public void FloorCleared()
    {
        if (IsOver || IsEnding) return;

        // F105 C6: the floor was cleared, so what the player leaned on this floor is learned (before the shop checkpoint / the finale).
        EndGrabForTransition(); // F120: a dropped torch / limp must not follow the player into the hub or the shop
        if (_habits != null) _habits.CommitLearning();
        LoopCorridor.CommitFloor(); // F105 C4: a loop on this floor counts as the campaign's one

        // F107 B2: a descent never wins, never visits the shop or the finale and never saves: always the hatch drop, a typed slip, the next level.
        if (GameFlow.IsDescent)
        {
            StartCoroutine(DescentClearSequence());
            return;
        }

        if (GameFlow.CurrentFloor >= FloorProfile.FinalFloor || (_shop == null && !AsylumHub.ShopInHub))
        {
            // F93 (E10): the last floor ends in the Director's office when the asylum can host it; anything missing = the old plain win.
            if (GameFlow.CurrentFloor >= FloorProfile.FinalFloor && AsylumHub.Active != null && AsylumHub.Active.CanFinale)
            {
                StartCoroutine(FinaleSequence());
                return;
            }
            Win();
            return;
        }

        StartCoroutine(FloorClearSequence());
    }

    /// <summary>
    /// F83: CONTINUE on a save taken in the shop. The tail of FloorClearSequence without the ledger or the
    /// payout (already banked in the save): silence the hunt, torch off, teleport in, open the room, fade up.
    /// Called by MainMenu.Start with GameFlow.IsRunActive still false. No shop in the scene = start the next floor.
    /// </summary>
    public void EnterShopDirectly()
    {
        if (_shop == null)
        {
            GameFlow.NextFloor();
            return;
        }

        StartCoroutine(EnterShopRoutine());
    }

    /// <summary>One of the endings was reached in the asylum finale. Single-shot; deletes the save and shows the ending over the campaign total.</summary>
    public void CompleteFinale(FinaleEnding ending)
    {
        if (IsOver) return;
        IsOver = true;
        IsEnding = false;
        SaveSystem.Delete();
        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;
        bool a = ending == FinaleEnding.Discharged;
        NoteEndingReached(a); // F122
        if (ending == FinaleEnding.Seated)
        {
            _endingTitleColorOverride = new Color(1f, 0.72f, 0.25f, 1f);
            ShowEndScreen(true, LoseReason.Caught, false, L10n.T("ui.outcome.ending_c_title"), L10n.T("ui.outcome.ending_c_sub"));
            return;
        }
        string endKey = a ? "lore.ending.discharged" : "lore.ending.lights_out"; // S9: the two ending texts live in the Lore table
        ShowEndScreen(true, LoseReason.Caught, false, L10n.T(endKey + ".title"), L10n.T(endKey + ".body"));
    }

    public void Win()
    {
        if (IsOver || IsEnding) return;

        // F107: a descent has no ending to win; anything that reaches here is a floor clear.
        if (GameFlow.IsDescent)
        {
            FloorCleared();
            return;
        }

        IsOver = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;

        float secondsLeft = _escape != null ? _escape.TimeLeft : 0f;
        _floorPayout = CommitFloorRewards(secondsLeft);
        // F83: the campaign is over. Guarded on the final floor so the no-shop fallback (a plain Win on floors 1-4) keeps its save.
        if (GameFlow.CurrentFloor >= FloorProfile.FinalFloor) { SaveSystem.Delete(); NoteEndingReached(true); } // F122: the plain no-hub final Win is ending A

        EndTheHunt();
        if (_escape != null) _escape.Stop();
        if (_flashlight != null) _flashlight.InputEnabled = false;
        PlayerLock.Freeze(_player, true);

        ShowEndScreen(true, LoseReason.Caught, false);
    }

    public void Lose(LoseReason reason)
    {
        if (IsOver || IsEnding) return;
        CountDeath(); // F122: a death on this floor caps its grade (a hub death does not count)

        if (reason == LoseReason.Caught)
        {
            StartCoroutine(CaptureSequence());
            return;
        }

        IsOver = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        if (GameFlow.IsDescent) DescentRun.Cause = DescentCause.Timer; // F107

        BankConsolation();

        EndTheHunt();
        if (_escape != null) _escape.Stop();
        if (_flashlight != null) _flashlight.InputEnabled = false;
        PlayerLock.Freeze(_player, true);

        ShowEndScreen(false, reason, _flashlight != null && _flashlight.IsOn);
    }

    /// <summary>
    /// Computes the floor payout, scores the floor clear (F122), appends contract rewards (F87),
    /// optionally rolls the next floor's contract offers, deposits the payout, and commits shards.
    /// </summary>
    private Payout CommitFloorRewards(float secondsLeft, bool rollNextContractOffers = false)
    {
        Payout payout = PlayerWallet.ComputeFloorClear(GameFlow.CurrentFloor, _collected, secondsLeft);
        ScoreFloorClear(payout, secondsLeft);
        if (_contracts != null) _contracts.AppendTo(payout, secondsLeft);
        if (rollNextContractOffers) ContractState.RollOffers(_seed, GameFlow.CurrentFloor);
        PlayerWallet.Deposit(payout);
        PatientRecord.CommitShards();
        return payout;
    }

    /// <summary>
    /// Once the run is over the hunter stops mattering, so silence it. Without this the heartbeat keeps
    /// pounding over the end screen and the hum keeps looping.
    /// </summary>
    private void EndTheHunt()
    {
        foreach (HorrorAudioDirector director in FindObjectsByType<HorrorAudioDirector>(FindObjectsInactive.Exclude))
        {
            // Silence first: disabling the component stops its Update but leaves its AudioSources running
            director.Silence();
            // Disabling then runs its OnDisable, which unsubscribes it from the chase event
            director.enabled = false;
        }

        foreach (AIPresence presence in FindObjectsByType<AIPresence>(FindObjectsInactive.Exclude))
        {
            presence.Silence();
        }

        foreach (AIFollower follower in FindObjectsByType<AIFollower>(FindObjectsInactive.Exclude))
        {
            follower.enabled = false;

            NavMeshAgent agent = follower.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        }
    }
}
