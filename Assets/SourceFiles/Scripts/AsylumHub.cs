using System;
using System.Collections;
using StarterAssets;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

/// <summary>
/// F92: the Karlheinz Asylum the player walks through between mazes. The room is a hand-editable prefab
/// (Assets/Prefabs/AsylumHub.prefab, first drafted by AsylumHubBuilder); this component holds the parts the game needs to find,
/// switches the heavy content on and off (D10) and runs the flow (D13): arriving from the Intake or at the start of a floor,
/// and walking through the open wing door into that floor's maze.
///
/// It is not a run: while GameFlow.IsInHub is set IsRunActive stays false (the hunter is held, dread, phantoms, Stan and the
/// escape timer stay inert), the HUD counters are hidden and pausing works, but the interactor is on (the doors need it).
/// With no hub in the scene, or an incomplete one, <see cref="CanEnter"/> is false and every caller falls back to the old
/// straight-into-the-maze behaviour.
/// </summary>
public class AsylumHub : MonoBehaviour
{
    /// <summary>The hub in the scene, or null. Scene-scoped: cleared in OnDestroy.</summary>
    public static AsylumHub Active { get; private set; }

    /// <summary>Raised when the player arrives in the hub, with the floor they are about to enter (hook for the later "the hub gets less safe" idea). Subscribers unsubscribe in OnDestroy.</summary>
    public static event Action<int> OnEnterHub;

    [Header("Content (inactive except while the player is in the hub)")]
    [Tooltip("The pack building: architecture, props and its realtime lights.")]
    public Transform building;
    [Tooltip("Everything the builder adds on top of the building (props, signs, particles).")]
    public Transform dressing;
    [Tooltip("Lights the builder adds (wing-door beacons, entrance spill).")]
    public Transform lights;
    [Tooltip("The five wing doors.")]
    public Transform wings;

    [Header("Story layer (F93, optional - not part of IsComplete)")]
    [Tooltip("AsylumHub/Life: patients, records board, wristband, set pieces. Switched with the other content; the hub works without it.")]
    public Transform life;
    [Tooltip("Testing: > 0 forces HubState.Floor (and so the open wing and the patients) to this floor. 0 = off.")]
    public int debugFloor;

    [Header("Shop (F116, optional - not part of IsComplete)")]
    [Tooltip("AsylumHub/Shop: the Commissary, the shop as a room of the asylum (the ground-floor storeroom). A hub without it still works; clearing a maze then goes to the old shop room directly, as before.")]
    public HubShop shop;

    [Header("Empty rooms (F117, optional - not part of IsComplete)")]
    [Tooltip("AsylumHub/Lesson: the classroom blackboard, chalk text of what the hunter has learned.")]
    public HubLesson lesson;
    [Tooltip("AsylumHub/Meal: the kitchen serving table, one free dish per floor (F117).")]
    public HubMeal meal;
    [Tooltip("AsylumHub/Supper: the dining room's long table, THE LAST SUPPER (F117).")]
    public HubSupper supper;

    [Header("Points (always active)")]
    public Transform points;
    public Transform mainEntrance;

    [Header("Wings")]
    [Tooltip("Index 0 = floor 1.")]
    public WingDoor[] wingDoors = new WingDoor[0];

    [Header("Atmosphere while the player is inside (D9)")]
    public Color hubAmbient = new Color(0.15f, 0.15f, 0.18f);
    public float hubFogDensity = 0.016f;
    public Color hubFogColor = new Color(0.02f, 0.024f, 0.035f);

    [Header("Walk-in")]
    public float doorOpenAngle = 95f;
    public float doorOpenSeconds = 0.9f;
    public float doorWalkSeconds = 1.3f;
    [Tooltip("How far from the door plane the walk stops (the screen is black by then).")]
    public float doorStopDistance = 0.22f;
    public float fadeInSeconds = 1.4f;
    [Tooltip("The arrival hint repeats this often while the player has not entered a wing.")]
    public float hintEverySeconds = 40f;

    /// <summary>True while the building content is switched on.</summary>
    public bool ContentActive { get; private set; } = true;

    /// <summary>True from the moment a wing door is used until the maze takes over: the ambience fades out and pausing is off.</summary>
    public bool Leaving { get; private set; }

    /// <summary>True during the arrival and the walk-in (the player is held by a scripted move): PauseMenu does not open.</summary>
    public bool IsTransitioning { get; private set; }

    /// <summary>The hub's black overlay is up (a door walk, a catch, a scripted fade): HUD cards built after it would draw on top of it.</summary>
    public bool BlackoutVisible => _blackoutRoot != null && _blackoutRoot.activeInHierarchy && _blackout != null && _blackout.color.a > 0.01f;

    /// <summary>Raised right before the hub switches off for a maze (a wing door was used). Story pieces reset on it. Subscribers unsubscribe in OnDestroy.</summary>
    public static event Action OnLeaveHub;

    /// <summary>The player systems handed over by MazeGenerator.SetUpAtmosphere (null until then).</summary>
    public Transform Player => _player;
    public Camera PlayerCamera => _camera;
    public PlayerHud Hud => _hud;
    public Flashlight Torch => _flashlight;
    /// <summary>The reading panel the records board and the patient files use (F93).</summary>
    public LoreReadingUi Reader => _reader;
    public AsylumAmbience Ambience => _ambience;
    public PlayerStealthState Stealth => _stealth;
    public ThirdPersonController Controller => _controller;
    /// <summary>Where the last arrival put the player (a caught player wakes up here).</summary>
    public Vector3 ArrivalPosition { get; private set; }
    public float ArrivalYaw { get; private set; }

    public bool IsComplete =>
        building != null && points != null && mainEntrance != null && wings != null && lights != null && dressing != null &&
        wingDoors != null && wingDoors.Length == 5 && AllWingsWired();

    /// <summary>Complete and handed the player systems by MazeGenerator.SetUpAtmosphere: the run may go through it.</summary>
    public bool CanEnter => IsComplete && _configured;

    /// <summary>F94/F116: the hub can host the shop - usable, with a complete Commissary and the shop menu handed over. Deliberately not part of <see cref="IsComplete"/>.</summary>
    public bool CanVisitShop => CanEnter && shop != null && shop.IsComplete && _shopMenu != null;

    /// <summary>F94/F116: true when clearing floors 1-4 goes to the hub (and its Commissary) instead of straight into the old shop room. The one place that decides it.</summary>
    public static bool ShopInHub => Active != null && Active.CanVisitShop;

    private bool AllWingsWired()
    {
        foreach (WingDoor door in wingDoors)
        {
            if (door == null || !door.IsComplete) return false;
        }
        return true;
    }

    // Player systems, handed over by MazeGenerator.SetUpAtmosphere.
    private bool _configured;
    private Transform _player;
    private Camera _camera;
    private PlayerHud _hud;
    private Flashlight _flashlight;
    private ThirdPersonController _controller;
    private CharacterController _characterController;
    private AsylumAmbience _ambience;
    private LoreReadingUi _reader;
    private PlayerStealthState _stealth;
    private PlayerInteractor _interactor;

    private ShopMenu _shopMenu;
    private int _mazeSeed;

    private Vector3 _returnPosition;
    private float _returnYaw;

    private GameObject _blackoutRoot;
    private Image _blackout;

    // The atmosphere of the floor, kept while the hub's own values are in force (D9).
    private bool _hasCachedAtmosphere;
    private AmbientMode _ambientMode;
    private Color _ambientLight;
    private bool _fog;
    private FogMode _fogMode;
    private float _fogDensity;
    private Color _fogColor;

    private void Awake()
    {
        Active = this;
        // The hub is saved with its content active so it can be edited; at runtime it only exists while the player is in it.
        if (Application.isPlaying) SetContentActive(false);
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
        if (_blackoutRoot != null) Destroy(_blackoutRoot);
        // A scene reload gets its own RenderSettings, but a hub destroyed mid-visit must not leave the floor with the hub's fog.
        if (_hasCachedAtmosphere) RestoreAtmosphere();
    }

    /// <summary>Called by MazeGenerator.SetUpAtmosphere once the player systems exist. `music` is the floor's base music, played at a whisper in the hub.</summary>
    public void Configure(Transform player, Camera camera, PlayerHud hud, Flashlight flashlight, AudioClip music = null, LoreReadingUi reader = null, AudioClip urgent = null, ShopMenu shopMenu = null, int mazeSeed = 0)
    {
        _shopMenu = shopMenu;
        if (shop != null) shop.Configure(shopMenu);
        _mazeSeed = mazeSeed;
        if (GetComponent<HubObjectiveCard>() == null) gameObject.AddComponent<HubObjectiveCard>(); // the hub's objective card (built lazily)
        _reader = reader;
        _player = player;
        _camera = camera;
        _hud = hud;
        _flashlight = flashlight;
        _controller = player != null ? player.GetComponent<ThirdPersonController>() : null;
        _stealth = player != null ? player.GetComponent<PlayerStealthState>() : null;
        _interactor = player != null ? player.GetComponent<PlayerInteractor>() : null;
        _characterController = player != null ? player.GetComponent<CharacterController>() : null;
        _ambience = GetComponentInChildren<AsylumAmbience>(true);
        if (_ambience != null) _ambience.SetMusic(music);
        if (_ambience != null) _ambience.SetUrgent(urgent);
        BindLockers();
        _configured = player != null;
    }

    /// <summary>Shows or hides the building, the dressing, the added lights and the wing doors.</summary>
    public void SetContentActive(bool active)
    {
        ContentActive = active;
        if (building != null) building.gameObject.SetActive(active);
        if (dressing != null) dressing.gameObject.SetActive(active);
        if (active) ScaleFog();
        if (lights != null) lights.gameObject.SetActive(active);
        if (wings != null) wings.gameObject.SetActive(active);
        if (life != null) life.gameObject.SetActive(active);
        if (shop != null) shop.gameObject.SetActive(active); // F116: the Commissary is hub content too
        if (lesson != null) lesson.gameObject.SetActive(active); // F117
        if (meal != null) meal.gameObject.SetActive(active); // F117
        if (supper != null) supper.gameObject.SetActive(active); // F117
    }

    /// <summary>S2: every fog emitter under Dressing/Fog follows the Fog setting (a FogScale is added on first activation and applies on its own enable).</summary>
    private void ScaleFog()
    {
        Transform fog = dressing != null ? dressing.Find("Fog") : null;
        if (fog == null) return;
        foreach (ParticleSystem ps in fog.GetComponentsInChildren<ParticleSystem>(true))
        {
            if (ps.GetComponent<FogScale>() == null) ps.gameObject.AddComponent<FogScale>();
        }
    }

    // ---------------------------------------------------------------- entering

    /// <summary>
    /// IntakeRoom.LeaveRoutine, at black: the Intake hands the player over instead of dropping them into floor 1. `returnPosition` /
    /// `returnYaw` are the maze start the player came from; they are where the wing door will send them.
    /// </summary>
    public void EnterFromIntake(Vector3 returnPosition, float returnYaw)
    {
        if (!CanEnter) return;
        _returnPosition = returnPosition;
        _returnYaw = returnYaw;
        StartCoroutine(EnterRoutine(mainEntrance, 1));
    }

    /// <summary>MainMenu.Start with GameFlow.ArriveInHub: a floor starts here. The player stands where the maze would start; that is the way back.</summary>
    public void EnterFromMenu()
    {
        if (!CanEnter) return;
        _returnPosition = _player.position;
        _returnYaw = _player.eulerAngles.y;

        // MazeGenerator.debugStartInFinale sets HubState.IsFinale before this: arrive at the Director's desk instead of a wing.
        if (HubState.IsFinale)
        {
            if (CanFinale) { EnterFinale(); return; }
            Debug.LogError("AsylumHub: the finale piece (AsylumHub/Life/Finale) is missing or incomplete; entering the hub normally.", this);
            HubState.IsFinale = false;
        }

        int floor = Mathf.Clamp(GameFlow.CurrentFloor, 1, wingDoors.Length);
        // The door they came out of is the previous wing's; the very first arrival is the front door.
        Transform arrival = floor >= 2 && wingDoors[floor - 2] != null ? wingDoors[floor - 2].arrivalPoint : mainEntrance;
        StartCoroutine(EnterRoutine(arrival, floor));
    }


    // ---------------------------------------------------------------- debug entry points (F93 E11)

    /// <summary>
    /// Testing only (RunCommand in Play; never reachable from player UI): makes `floor` the current floor and runs the normal
    /// enter path at that floor's arrival point (the front door for floor 1). Call it on the title screen, in the Intake or while
    /// already in the hub - not during a maze run. Returns a one-line report.
    /// </summary>
    public static string DebugEnter(int floor)
    {
        AsylumHub h = Active;
        if (h == null || !h.CanEnter) return "no usable hub";
        if (h.IsTransitioning || h.Leaving) return "hub is busy";
        if (GameFlow.IsRunActive) return "a maze run is active";
        floor = Mathf.Clamp(floor, 1, 5);
        GameFlow.CurrentFloor = floor;
        HubState.IsFinale = false;
        if (!GameFlow.IsInHub) h.RememberReturn();
        Transform arrival = floor >= 2 && h.wingDoors[floor - 2] != null ? h.wingDoors[floor - 2].arrivalPoint : h.mainEntrance;
        h.StartCoroutine(h.EnterRoutine(arrival, floor));
        return $"entering the hub for floor {floor} at {arrival.name}";
    }

    /// <summary>
    /// Testing only: the finale stage (G6 finishes it). Sets HubState.IsFinale, floor 5, optionally marks case files 100..104 read
    /// (never 105 - that is read at the Director's desk), and enters at the Director_Desk point.
    /// </summary>
    public static string DebugFinale(bool allFiles)
    {
        AsylumHub h = Active;
        if (h == null || !h.CanEnter) return "no usable hub";
        if (h.IsTransitioning || h.Leaving) return "hub is busy";
        if (GameFlow.IsRunActive) return "a maze run is active";
        GameFlow.CurrentFloor = 5;
        HubState.IsFinale = true;
        if (allFiles)
        {
            for (int i = 0; i < PatientFiles.MazeFileCount; i++) LoreArchive.MarkRead(PatientFiles.FirstId + i);
        }
        if (!h.CanFinale) return "the finale piece (AsylumHub/Life/Finale) is missing or incomplete";
        h.EnterFinale();
        return $"finale: the Director's desk, files read {HubState.FilesRead}/5";
    }

    /// <summary>F93 (E10): true when the hub can host the Director's-office finale (complete, configured, with its HubFinale piece).</summary>
    public bool CanFinale => CanEnter && life != null && life.GetComponentInChildren<HubFinale>(true) != null && life.GetComponentInChildren<HubFinale>(true).IsComplete;

    /// <summary>GameOutcome.FinaleSequence, at black: clearing floor 5 brings the player back to the Director's desk. Single use per run (GameOutcome guards).</summary>
    public void EnterFinale()
    {
        if (!CanFinale) return;
        HubState.IsFinale = true;
        GameFlow.CurrentFloor = Mathf.Max(GameFlow.CurrentFloor, 5);
        if (!GameFlow.IsInHub) RememberReturn();
        Transform desk = points != null ? points.Find("Director_Desk") : null;
        StartCoroutine(EnterRoutine(desk != null ? desk : mainEntrance, 5));
    }

    private void RememberReturn()
    {
        if (_player == null) return;
        _returnPosition = _player.position;
        _returnYaw = _player.eulerAngles.y;
    }
    private IEnumerator EnterRoutine(Transform arrival, int floor)
    {
        _overlayColor = Color.black;
        if (_ambience != null) _ambience.Unmute();
        IsTransitioning = true;
        Leaving = false;
        // Set before the caller clears IsInIntake, so no frame is ever "neither".
        GameFlow.IsInHub = true;

        EnsureBlackout();
        SetBlackout(1f);
        CacheAtmosphere();
        ApplyHubAtmosphere();
        SetContentActive(true);
        foreach (WingDoor door in wingDoors) door.Refresh();
        if (shop != null) shop.Refresh();
        if (lesson != null) lesson.Refresh(); // F117
        if (meal != null) meal.Refresh(); // F117
        if (supper != null) supper.Refresh(); // F117

        ArrivalPosition = arrival.position;
        ArrivalYaw = arrival.eulerAngles.y;
        Teleport(arrival.position, arrival.eulerAngles.y);
        if (_controller != null)
        {
            _controller.MovementLocked = false;
            _controller.LockCameraPosition = false;
        }
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
        if (_hud != null) { _hud.SetPrompt(null); _hud.SetFocusPrompt(null); }
        if (_flashlight != null)
        {
            _flashlight.InputEnabled = true;
            _flashlight.SetOn(true);
        }

        OnEnterHub?.Invoke(floor);

        yield return Fade(1f, 0f, fadeInSeconds);
        IsTransitioning = false;
        if (_blackoutRoot != null) _blackoutRoot.SetActive(false);

        if (HubState.IsFinale)
        {
            if (_hud != null) _hud.ShowSubtitle(L10n.T("hub.finale.office"), 4f);
            yield break;
        }
        WingDoor next = wingDoors[Mathf.Clamp(floor, 1, wingDoors.Length) - 1];
        if (_hud != null && next != null)
        {
            // F100/F116: the Commissary gets its one mention when the slip would have offered it (the floor objective itself is shown on HubObjectiveCard).
            if (!_shopLineShown && !_shopVisited && ShopWorthAVisit(floor - 1))
            {
                _shopLineShown = true;
                string room = L10n.Has("hub.shopdoor.room") ? L10n.T("hub.shopdoor.room") : null; // where the door is
                _hud.QueueSubtitle(string.IsNullOrEmpty(room) ? L10n.T("hub.shopdoor.open_line") : L10n.T("hub.shopdoor.open_line_room", ("room", room)), 4f);
            }
        }
    }

    private bool _shopLineShown, _shopVisited;

    /// <summary>
    /// F100: the readmission slip and the arrival both offer the shop only when it is worth the walk: the Commissary will be open for the next
    /// floor and the wallet holds at least the cheapest thing that can still be bought (at the prices of `clearedFloor`, the floor just cleared).
    /// </summary>
    public static bool ShopWorthAVisit(int clearedFloor)
    {
        if (!ShopInHub || clearedFloor < 1 || !HubShop.OpenFor(clearedFloor + 1)) return false;
        int cheapest = ShopCatalogue.CheapestOpenPrice(clearedFloor);
        return cheapest >= 0 && PlayerWallet.Shards >= cheapest;
    }

    /// <summary>F100: the floor's mission has been finished (key handed over). False when the floor has none.</summary>
    public bool MissionDone(int floor)
    {
        HubMission m = HubMission.ForFloor(floor);
        return m != null && m.Current == HubMission.Stage.Done;
    }

    /// <summary>F100: plays after the line on screen has finished (HUD queue).</summary>
    public void QueueSubtitleLine(string text, float seconds) { if (_hud != null) _hud.QueueSubtitle(text, seconds); }

    // ---------------------------------------------------------------- the wing door

    /// <summary>
    /// The walk-in through an open wing door (modelled on ShopRoom.WalkOutRoutine): freeze, swing the leaf (both leaves of a
    /// double door) away from the player, ease up to the doorway looking through it, fade to black as they reach it, then at
    /// black switch the hub off, put the floor's atmosphere back, send them to the maze start and begin the run.
    /// </summary>
    public void EnterWing(WingDoor door)
    {
        if (door == null || Leaving || IsTransitioning || !CanEnter || door.State != WingDoor.DoorState.Open || door.StoryLocked) return;
        StartCoroutine(WalkInRoutine(door));
    }

    private IEnumerator WalkInRoutine(WingDoor door)
    {
        Leaving = true;
        IsTransitioning = true;

        DoorPassage passage = door.GetComponent<DoorPassage>();
        yield return DoorWalk(door.transform);

        // ---- at black: the hub goes away and floor n begins exactly as it always did
        OnLeaveHub?.Invoke();
        if (passage != null) passage.ResetClosed();
        RestoreAtmosphere();
        SetContentActive(false);

        // F94: perks bought in the hub shop apply to this floor, whose maze is already built without them: bank them and rebuild
        // the same maze (same seed, same rolled pair) so they take effect. IsInHub stays set until the reload takes over.
        if (PlayerInventory.PendingModifiers.Count > 0)
        {
            PlayerInventory.ActivatePending();
            SaveSystem.WriteCheckpoint(GameFlow.CurrentFloor, false);
            GameFlow.Restart(true, _mazeSeed, false);
            yield break;
        }

        Teleport(_returnPosition, _returnYaw);
        if (_flashlight != null) _flashlight.Refill();

        GameFlow.IsInHub = false;
        if (_controller != null) _controller.MovementLocked = false;
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
        Leaving = false;
        IsTransitioning = false;
        GameFlow.BeginRun(); // FloorIntroBanner, the hunter, the dread directors and the floor's music all wake on this

        yield return IrisOpen(0.8f);
        if (_blackoutRoot != null) _blackoutRoot.SetActive(false);
    }

    /// <summary>
    /// F112: the way back from a door walk - the black the walk ended on becomes a closed iris in the same frame and opens onto the
    /// destination. Falls back to a plain fade when the scene has no canvas for the iris.
    /// </summary>
    private IEnumerator IrisOpen(float seconds)
    {
        ScreenIris iris = ScreenIris.Show(Color.black);
        if (iris == null) { yield return Fade(1f, 0f, seconds); yield break; }
        iris.SetClosure(1f);
        _overlayColor = Color.black;
        SetBlackout(0f);
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            iris.SetClosure(1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        iris.Hide();
    }

    /// <summary>
    /// F112: the one walk through a door in the hub, shared by the wing doors and (through <see cref="PassThrough"/>) the finale's
    /// main doors. Stand back out of the leaves' sweep, latch, a held crack, the leaves swing TOWARD the hall with a cold draft, step up to
    /// the doorway and walk in while the screen irises closed onto it. Returns at full black with the player still frozen and the leaves
    /// open; the caller decides what comes next and closes them (<see cref="DoorPassage.ResetClosed"/>). A door without a complete
    /// DoorPassage logs an error and walks in without the swing (the player is never stranded).
    /// </summary>
    private IEnumerator DoorWalk(Transform door)
    {
        PlayerLock.Freeze(_player, true);
        if (_hud != null) { _hud.SetPrompt(null); _hud.SetFocusPrompt(null); }

        DoorPassage p = door.GetComponent<DoorPassage>();
        bool swing = p != null && p.IsComplete;
        if (!swing) Debug.LogError($"AsylumHub: {door.name} has no complete DoorPassage; walking in without the swing. Run KEEP IT LIT > Build Missing Pieces.", door);

        Vector3 into;
        Vector3 doorway;
        float standOff;
        if (swing)
        {
            p.ResetClosed();
            into = p.Into;
            doorway = p.Doorway(_player.position.y);
            standOff = p.StandOff;
            _overlayColor = p.passColour;
        }
        else
        {
            into = -door.forward;
            into.y = 0f;
            into = into.sqrMagnitude > 0.0001f ? into.normalized : Vector3.forward;
            doorway = new Vector3(door.position.x, _player.position.y, door.position.z);
            standOff = 1f;
            _overlayColor = Color.black;
        }
        Vector3 right = Vector3.Cross(Vector3.up, into);
        Vector3 start = _player.position;
        Vector3 stand = doorway - into * standOff;
        Vector3 threshold = doorway - into * 0.9f;
        Vector3 stop = doorway - into * Mathf.Max(doorStopDistance, 0.3f); // the camera near plane is 0.2 m: stay far enough back for the door void to be in front of it
        Vector3 Look(Vector3 from) => from + into * 4f + Vector3.up * 1.4f;
        void Face() { if (_controller != null) _controller.LookAt(Look(_player.position), 1f - Mathf.Exp(-8f * Time.deltaTime)); }

        // Silent on purpose: the synthesized hinge creak that used to play here read as a weird noise on every way into a maze.

        // 0. Stand back: out of the sweep of the leaves, on the door's axis.
        if (swing)
        {
            Vector3 flat = new Vector3(start.x - doorway.x, 0f, start.z - doorway.z);
            bool tooClose = flat.magnitude < standOff;
            bool offAxis = Mathf.Abs(Vector3.Dot(flat, right)) > 0.6f;
            if (tooClose || offAxis)
            {
                float t0 = 0f;
                while (t0 < 0.45f)
                {
                    t0 += Time.deltaTime;
                    MovePlayer(Vector3.Lerp(start, stand, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t0 / 0.45f))));
                    Face();
                    yield return null;
                }
                MovePlayer(stand);
            }
            else stand = start;
            threshold = new Vector3(threshold.x, stand.y, threshold.z);
            stop = new Vector3(stop.x, stand.y, stop.z);

            // 1. Latch and a held crack.
            DoorPassageFx.Latch(doorway + Vector3.up * 1f);
            float t1 = 0f;
            while (t1 < 0.3f)
            {
                t1 += Time.deltaTime;
                p.SetOpen(0.08f * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t1 / 0.1f)));
                Face();
                yield return null;
            }

            // 2. The swing, toward the hall, with a cold draft.
            DoorPassageFx.Draft(doorway + Vector3.up * 1.2f, p.draft);
            if (_flashlight != null) _flashlight.Stutter(0.35f);
            float t2 = 0f;
            while (t2 < doorOpenSeconds)
            {
                t2 += Time.deltaTime;
                p.SetOpen(Mathf.Lerp(0.08f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t2 / doorOpenSeconds))));
                Face();
                yield return null;
            }
            p.SetOpen(1f);
        }

        // 3. Step up to the threshold, looking through the door.
        Vector3 from3 = _player.position;
        float t3 = 0f;
        while (t3 < 0.6f)
        {
            t3 += Time.deltaTime;
            MovePlayer(Vector3.Lerp(from3, threshold, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t3 / 0.6f))));
            Face();
            yield return null;
        }

        // 4. Walk in while the iris closes on the doorway.
        EnsureBlackout();
        SetBlackout(0f);
        ScreenIris iris = ScreenIris.Show(_overlayColor);
        float t = 0f;
        while (t < doorWalkSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / doorWalkSeconds);
            MovePlayer(Vector3.Lerp(threshold, stop, 1f - (1f - k) * (1f - k)));
            Face();
            float closure = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.05f) / 0.95f));
            if (iris != null) iris.SetClosure(closure);
            else SetBlackout(closure);
            yield return null;
        }
        SetBlackout(1f);
        if (iris != null) iris.Hide();
    }

    /// <summary>F112: the finale's main doors use the hub's door walk too (HubExitDoor.WalkOut). Same contract: returns at full black, leaves open.</summary>
    public IEnumerator PassThrough(Transform door) => DoorWalk(door);

    // ---------------------------------------------------------------- the Commissary (F94, F116)

    /// <summary>F100/F116: the player has been inside the Commissary - the arrival line that mentions it is not repeated.</summary>
    public void MarkShopVisited() { _shopVisited = true; }

    /// <summary>True while a hub pursuer is spotting, chasing or catching the player (the Commissary counter and the dialogue panel refuse then).</summary>
    public bool PursuerAfterPlayer()
    {
        if (life == null) return false;
        foreach (HubStalker pursuer in life.GetComponentsInChildren<HubStalker>(true))
        {
            if (pursuer == null || !pursuer.isActiveAndEnabled) continue;
            HubStalker.Mode mode = pursuer.CurrentMode;
            if (mode == HubStalker.Mode.Spotted || mode == HubStalker.Mode.Chase || mode == HubStalker.Mode.Catching) return true;
        }
        return false;
    }

    // ---------------------------------------------------------------- atmosphere (D9)

    private void CacheAtmosphere()
    {
        if (_hasCachedAtmosphere) return;
        _hasCachedAtmosphere = true;
        _ambientMode = RenderSettings.ambientMode;
        _ambientLight = RenderSettings.ambientLight;
        _fog = RenderSettings.fog;
        _fogMode = RenderSettings.fogMode;
        _fogDensity = RenderSettings.fogDensity;
        _fogColor = RenderSettings.fogColor;
    }

    private void ApplyHubAtmosphere()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        Color amb = hubAmbient;
        float max = Mathf.Max(amb.r, amb.g, amb.b);
        if (max > 0.0001f && max < 0.15f)
        {
            float s = 0.15f / max;
            amb = new Color(amb.r * s, amb.g * s, amb.b * s, 1f);
        }
        else if (max <= 0.0001f)
        {
            amb = new Color(0.15f, 0.15f, 0.18f, 1f);
        }
        RenderSettings.ambientLight = amb;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = Mathf.Min(hubFogDensity, 0.018f);
        RenderSettings.fogColor = hubFogColor;
    }

    private void RestoreAtmosphere()
    {
        if (!_hasCachedAtmosphere) return;
        _hasCachedAtmosphere = false;
        RenderSettings.ambientMode = _ambientMode;
        RenderSettings.ambientLight = _ambientLight;
        RenderSettings.fog = _fog;
        RenderSettings.fogMode = _fogMode;
        RenderSettings.fogDensity = _fogDensity;
        RenderSettings.fogColor = _fogColor;
    }

    // ---------------------------------------------------------------- story helpers (F93 G4+)

    /// <summary>Binds every hub locker (AsylumHub/Life/Lockers, clones of the Ward locker) to the player systems, like MazeGenerator does for a maze's.</summary>
    private void BindLockers()
    {
        if (life == null || _player == null) return;
        foreach (Locker locker in life.GetComponentsInChildren<Locker>(true))
        {
            Vector3 inside = locker.InsideAnchor != null ? locker.InsideAnchor.position : locker.transform.TransformPoint(new Vector3(0f, 0f, 0.35f));
            Vector3 front = locker.FrontAnchor != null ? locker.FrontAnchor.position : locker.transform.TransformPoint(new Vector3(0f, 0f, 1.6f));
            locker.Configure(inside, front, locker.transform.eulerAngles.y, locker.Door);
            locker.Bind(_player, _stealth, _flashlight, _hud, _interactor);
        }
    }

    /// <summary>Moves the player (CharacterController off around the write) and turns the view.</summary>
    public void TeleportPlayer(Vector3 position, float yaw) => Teleport(position, yaw);

    /// <summary>Moves the player without touching the view (rides, scripted carries).</summary>
    public void MovePlayerTo(Vector3 position) => MovePlayer(position);

    /// <summary>A set piece is moving the player by script (the lift ride): pausing and catches are held off until it is switched off again.</summary>
    public void SetScripted(bool on) { IsTransitioning = on; }

    public void SubtitleLine(string text, float seconds) { if (_hud != null) _hud.ShowSubtitle(text, seconds); }

    /// <summary>Fades the story blackout to `alpha` over `seconds` (unscaled). Used by set pieces for hard cuts.</summary>
    public IEnumerator BlackoutTo(float alpha, float seconds)
    {
        EnsureBlackout();
        float from = _blackout != null ? _blackout.color.a : 0f;
        yield return Fade(from, alpha, seconds);
        if (alpha <= 0.001f && _blackoutRoot != null && !IsTransitioning) _blackoutRoot.SetActive(false);
    }

    /// <summary>
    /// A creature caught the player in the hub (never GameOutcome - the hub has no run to lose): black, a sting, the player wakes at the
    /// door they came through, `reset` puts the creature back, a line. False when something else is already moving the player.
    /// </summary>
    public bool BeginCatch(System.Action reset, string line = null)
    {
        if (IsTransitioning || Leaving || !ContentActive || GameFlow.IsRunActive) return false;
        StartCoroutine(CatchRoutine(reset, line));
        return true;
    }

    private IEnumerator CatchRoutine(System.Action reset, string line)
    {
        IsTransitioning = true;
        PlayerLock.Freeze(_player, true);
        if (_hud != null) { _hud.SetPrompt(null); _hud.SetFocusPrompt(null); }
        HorrorAudioDirector director = FindAnyObjectByType<HorrorAudioDirector>();
        if (GameAudio.Play2D(GameAudio.Library.caught, 1f)) GameAudio.Play2D(GameAudio.Library.caughtLayer, 0.8f); // F97: a hard hit, with a dark boom under it
        else if (director != null) director.PlayCaptureSting();
        else GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.9f, 55f, 0.9f), _player.position, 1f);
        EnsureBlackout();
        yield return Fade(_blackout != null ? _blackout.color.a : 0f, 1f, 0.25f);
        yield return new WaitForSecondsRealtime(0.8f);
        foreach (Locker locker in life != null ? life.GetComponentsInChildren<Locker>(true) : new Locker[0])
        {
            if (locker != null && locker.Occupied) locker.ForceLeave();
        }
        Teleport(ArrivalPosition, ArrivalYaw);
        reset?.Invoke();
        if (_flashlight != null) { _flashlight.InputEnabled = true; _flashlight.SetOn(true); }
        if (_controller != null) _controller.MovementLocked = false;
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
        if (line == null) line = L10n.T("hub.catch.wake"); // S9: the default line is translated when it is shown
        if (_hud != null && !string.IsNullOrEmpty(line)) _hud.ShowSubtitle(line, 3.2f);
        yield return Fade(1f, 0f, 0.9f);
        if (_blackoutRoot != null) _blackoutRoot.SetActive(false);
        IsTransitioning = false;
    }

    // ---------------------------------------------------------------- helpers

    private void Teleport(Vector3 position, float yaw)
    {
        if (_player == null) return;
        if (_characterController != null) _characterController.enabled = false;
        _player.position = position;
        _player.rotation = Quaternion.Euler(0f, yaw, 0f);
        if (_characterController != null) _characterController.enabled = true;
        if (_controller != null) _controller.ResetCameraRotation(yaw);
    }

    /// <summary>A CharacterController overrides transform writes unless it is switched off around them.</summary>
    private void MovePlayer(Vector3 position)
    {
        if (_characterController != null) _characterController.enabled = false;
        _player.position = position;
        if (_characterController != null) _characterController.enabled = true;
    }

    private void EnsureBlackout()
    {
        if (_blackoutRoot != null)
        {
            _blackoutRoot.SetActive(true);
            _blackoutRoot.transform.SetAsLastSibling();
            return;
        }
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;
        GameObject panel = RuntimeUi.CreatePanel(canvas.transform, "AsylumBlackout", Color.black);
        _blackoutRoot = panel;
        _blackout = panel.GetComponent<Image>();
        _blackout.raycastTarget = false;
        panel.transform.SetAsLastSibling();
    }

    private Color _overlayColor = Color.black;
    private void SetBlackout(float alpha)
    {
        if (_blackout != null) _blackout.color = new Color(_overlayColor.r, _overlayColor.g, _overlayColor.b, alpha);
    }

    /// <summary>The finale's walk-out fades to a cold grey-white instead of black. `colour` stays until the next call.</summary>
    public IEnumerator OverlayTo(Color colour, float alpha, float seconds)
    {
        EnsureBlackout();
        _overlayColor = colour;
        float from = _blackout != null ? _blackout.color.a : 0f;
        yield return Fade(from, alpha, seconds);
    }

    private IEnumerator Fade(float from, float to, float seconds)
    {
        if (_blackout == null) yield break;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            SetBlackout(Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        SetBlackout(to);
    }
}
