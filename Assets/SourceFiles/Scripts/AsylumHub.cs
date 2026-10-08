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

    [Header("Shop door (F94, optional - not part of IsComplete)")]
    [Tooltip("AsylumHub/Wings/ShopDoor: the way into the shop room between floors. A hub without it still works; clearing a maze then goes to the shop directly, as before.")]
    public ShopDoor shopDoor;

    [Header("Points (always active)")]
    public Transform points;
    public Transform mainEntrance;

    [Header("Wings")]
    [Tooltip("Index 0 = floor 1.")]
    public WingDoor[] wingDoors = new WingDoor[0];

    [Header("Atmosphere while the player is inside (D9)")]
    public Color hubAmbient = new Color(0.035f, 0.035f, 0.04f);
    public float hubFogDensity = 0.03f;
    public Color hubFogColor = new Color(0.01f, 0.012f, 0.02f);

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

    /// <summary>F94: the hub can host the shop - usable, with a complete SHOP door, and the scene's shop room handed over. Deliberately not part of <see cref="IsComplete"/>.</summary>
    public bool CanVisitShop => CanEnter && shopDoor != null && shopDoor.IsComplete && _shop != null && _shop.IsComplete;

    /// <summary>F94: true when clearing floors 1-4 goes to the hub (and its SHOP door) instead of straight into the shop. The one place that decides it.</summary>
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

    private ShopRoom _shop;
    private int _mazeSeed;

    private Vector3 _returnPosition;
    private float _returnYaw;
    private Coroutine _hint;

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
    public void Configure(Transform player, Camera camera, PlayerHud hud, Flashlight flashlight, AudioClip music = null, LoreReadingUi reader = null, AudioClip urgent = null, ShopRoom shop = null, int mazeSeed = 0)
    {
        _shop = shop;
        _mazeSeed = mazeSeed;
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
        if (lights != null) lights.gameObject.SetActive(active);
        if (wings != null) wings.gameObject.SetActive(active);
        if (life != null) life.gameObject.SetActive(active);
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
    private IEnumerator EnterRoutine(Transform arrival, int floor, bool announce = true)
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
        if (shopDoor != null) shopDoor.Refresh();

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
            if (_hud != null) _hud.ShowSubtitle("THE DIRECTOR'S OFFICE", 4f);
            yield break;
        }
        WingDoor next = wingDoors[Mathf.Clamp(floor, 1, wingDoors.Length) - 1];
        // F94: coming back from the shop is not an arrival - no line, no fresh hint loop beyond the usual one.
        if (_hud != null && next != null && announce) _hud.ShowSubtitle(ArrivalText(next, floor), 5f);
        if (_hint != null) StopCoroutine(_hint);
        _hint = StartCoroutine(HintLoop(next, floor));
    }

    private static string ArrivalText(WingDoor door, int floor)
    {
        return $"FLOOR {floor} · THE {door.wingName} DOOR IS OPEN · {door.levelName}";
    }

    private IEnumerator HintLoop(WingDoor door, int floor)
    {
        while (!Leaving)
        {
            yield return new WaitForSeconds(hintEverySeconds);
            if (Leaving || door == null) yield break;
            if (_hud != null && Time.timeScale > 0f) _hud.ShowSubtitle($"{door.wingName} · {door.roomName} · {door.levelName}", 4f);
        }
    }

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
        if (_hint != null) { StopCoroutine(_hint); _hint = null; }

        Transform[] hinges = door.leafHingeB != null ? new[] { door.leafHinge, door.leafHingeB } : new[] { door.leafHinge };
        Quaternion[] closed = new Quaternion[hinges.Length];
        for (int i = 0; i < hinges.Length; i++) closed[i] = hinges[i].localRotation;
        yield return DoorWalk(door.transform, hinges);

        // ---- at black: the hub goes away and floor n begins exactly as it always did
        OnLeaveHub?.Invoke();
        for (int i = 0; i < hinges.Length; i++) hinges[i].localRotation = closed[i];
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

        yield return Fade(1f, 0f, 0.7f);
        if (_blackoutRoot != null) _blackoutRoot.SetActive(false);
    }

    /// <summary>
    /// The walk through a door, shared by the wing doors and the SHOP door: freeze, swing the leaf (both leaves of a double door) away
    /// from the player, ease up to the doorway looking through it and fade to black as they reach it. Returns at full black with the
    /// player still frozen and the leaves open; the caller decides what comes next and closes the leaves.
    /// </summary>
    private IEnumerator DoorWalk(Transform door, Transform[] hinges)
    {
        PlayerLock.Freeze(_player, true);
        if (_hud != null) { _hud.SetPrompt(null); _hud.SetFocusPrompt(null); }

        // Into the door = the way the arrival point does not face.
        Vector3 into = -door.forward;
        into.y = 0f;
        into = into.sqrMagnitude > 0.0001f ? into.normalized : Vector3.forward;
        Vector3 doorway = new Vector3(door.position.x, _player.position.y, door.position.z);
        Vector3 start = _player.position;
        Vector3 threshold = doorway - into * 0.9f;
        Vector3 stop = doorway - into * Mathf.Max(doorStopDistance, 0.3f); // the camera near plane is 0.2 m: stay far enough back for the door void to be in front of it
        Vector3 Look(Vector3 from) => from + into * 4f + Vector3.up * 1.4f;

        // Silent on purpose: the synthesized hinge creak that used to play here read as a weird noise on every way into a maze.

        // The leaves open away from the player, so a leaf never sweeps through them.
        Quaternion[] closed = new Quaternion[hinges.Length];
        Quaternion[] opened = new Quaternion[hinges.Length];
        for (int i = 0; i < hinges.Length; i++)
        {
            closed[i] = hinges[i].localRotation;
            opened[i] = closed[i] * Quaternion.Euler(0f, SwingSign(hinges[i], into) * doorOpenAngle, 0f);
        }

        float t = 0f;
        while (t < doorOpenSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / doorOpenSeconds));
            for (int i = 0; i < hinges.Length; i++) hinges[i].localRotation = Quaternion.Slerp(closed[i], opened[i], k);
            MovePlayer(Vector3.Lerp(start, threshold, k));
            if (_controller != null) _controller.LookAt(Look(_player.position), 1f - Mathf.Exp(-8f * Time.deltaTime));
            yield return null;
        }
        for (int i = 0; i < hinges.Length; i++) hinges[i].localRotation = opened[i];

        EnsureBlackout();
        SetBlackout(0f);
        t = 0f;
        while (t < doorWalkSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / doorWalkSeconds);
            MovePlayer(Vector3.Lerp(threshold, stop, 1f - (1f - k) * (1f - k)));
            if (_controller != null) _controller.LookAt(Look(_player.position), 1f - Mathf.Exp(-8f * Time.deltaTime));
            SetBlackout(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((k - 0.1f) / 0.9f)));
            yield return null;
        }
        SetBlackout(1f);
    }

    // ---------------------------------------------------------------- the SHOP door (F94)

    /// <summary>
    /// E at an open SHOP door: the same walk-in as a wing door, but at black the player lands in the shop room (lit, no run, no torch)
    /// instead of a maze. The shop's own door brings them back through <see cref="ReturnFromShop"/>. Refused while a hub pursuer is after
    /// the player - the shop is not an escape hatch.
    /// </summary>
    public void EnterShop(ShopDoor door)
    {
        if (door == null || Leaving || IsTransitioning || !CanVisitShop || !door.IsOpen) return;
        if (PursuerAfterPlayer())
        {
            if (_hud != null) _hud.ShowSubtitle("NOT NOW", 1.6f);
            return;
        }
        StartCoroutine(ShopWalkInRoutine(door));
    }

    private bool PursuerAfterPlayer()
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

    private IEnumerator ShopWalkInRoutine(ShopDoor door)
    {
        Leaving = true;
        IsTransitioning = true;
        if (_hint != null) { StopCoroutine(_hint); _hint = null; }

        Transform[] hinges = door.leafHingeB != null ? new[] { door.leafHinge, door.leafHingeB } : new[] { door.leafHinge };
        Quaternion[] closed = new Quaternion[hinges.Length];
        for (int i = 0; i < hinges.Length; i++) closed[i] = hinges[i].localRotation;
        yield return DoorWalk(door.transform, hinges);

        // ---- at black: the hub goes away and the shop room takes over. _returnPosition/_returnYaw (the maze start) stay as they were.
        OnLeaveHub?.Invoke();
        for (int i = 0; i < hinges.Length; i++) hinges[i].localRotation = closed[i];
        RestoreAtmosphere();
        SetContentActive(false);

        GameFlow.ShopFromHub = true; // before ArrivePlayer: the door sign and the shop's floor reads depend on it
        _shop.ArrivePlayer(_player);
        // IsInShop before IsInHub clears: never a frame that is neither.
        GameFlow.IsInShop = true;
        GameFlow.IsInHub = false;
        if (_flashlight != null) { _flashlight.SetOn(false); _flashlight.InputEnabled = false; }
        _shop.Open();
        if (_controller != null) _controller.MovementLocked = false;
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
        Leaving = false;
        IsTransitioning = false;

        yield return Fade(1f, 0f, 0.7f);
        if (_blackoutRoot != null) _blackoutRoot.SetActive(false);
    }

    /// <summary>
    /// ShopRoom's exit when the shop was reached from the hub: back into the hospital, arriving at the SHOP door. The caller destroys its
    /// own blackout panel after this (the hub makes its own black synchronously before its first yield).
    /// </summary>
    public void ReturnFromShop()
    {
        GameFlow.ShopFromHub = false;
        if (shopDoor == null) { GameFlow.NextFloor(); return; }
        StartCoroutine(EnterRoutine(shopDoor.arrivalPoint, GameFlow.CurrentFloor, false)); // sets IsInHub synchronously
        GameFlow.IsInShop = false;
    }

    /// <summary>+1 or -1: the sign of the swing that carries the leaf's centre further along `into` (away from the player).</summary>
    private static float SwingSign(Transform hinge, Vector3 into)
    {
        Renderer[] renderers = hinge.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return 1f;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        Vector3 offset = b.center - hinge.position;
        float plus = Vector3.Dot(Quaternion.AngleAxis(90f, hinge.up) * offset - offset, into);
        float minus = Vector3.Dot(Quaternion.AngleAxis(-90f, hinge.up) * offset - offset, into);
        return plus >= minus ? 1f : -1f;
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
        RenderSettings.ambientLight = hubAmbient;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = hubFogDensity;
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
    public bool BeginCatch(System.Action reset, string line = "You wake at the door.")
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
        else AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.9f, 55f, 0.9f), _player.position, 1f);
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
