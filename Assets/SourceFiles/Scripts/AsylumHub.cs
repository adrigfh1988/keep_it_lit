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

    public bool IsComplete =>
        building != null && points != null && mainEntrance != null && wings != null && lights != null && dressing != null &&
        wingDoors != null && wingDoors.Length == 5 && AllWingsWired();

    /// <summary>Complete and handed the player systems by MazeGenerator.SetUpAtmosphere: the run may go through it.</summary>
    public bool CanEnter => IsComplete && _configured;

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
    public void Configure(Transform player, Camera camera, PlayerHud hud, Flashlight flashlight, AudioClip music = null)
    {
        _player = player;
        _camera = camera;
        _hud = hud;
        _flashlight = flashlight;
        _controller = player != null ? player.GetComponent<ThirdPersonController>() : null;
        _characterController = player != null ? player.GetComponent<CharacterController>() : null;
        _ambience = GetComponentInChildren<AsylumAmbience>(true);
        if (_ambience != null) _ambience.SetMusic(music);
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

        int floor = Mathf.Clamp(GameFlow.CurrentFloor, 1, wingDoors.Length);
        // The door they came out of is the previous wing's; the very first arrival is the front door.
        Transform arrival = floor >= 2 && wingDoors[floor - 2] != null ? wingDoors[floor - 2].arrivalPoint : mainEntrance;
        StartCoroutine(EnterRoutine(arrival, floor));
    }

    private IEnumerator EnterRoutine(Transform arrival, int floor)
    {
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

        WingDoor next = wingDoors[Mathf.Clamp(floor, 1, wingDoors.Length) - 1];
        if (_hud != null && next != null) _hud.ShowSubtitle(ArrivalText(next, floor), 5f);
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
        if (door == null || Leaving || IsTransitioning || !CanEnter || door.State != WingDoor.DoorState.Open) return;
        StartCoroutine(WalkInRoutine(door));
    }

    private IEnumerator WalkInRoutine(WingDoor door)
    {
        Leaving = true;
        IsTransitioning = true;
        if (_hint != null) { StopCoroutine(_hint); _hint = null; }
        PlayerLock.Freeze(_player, true);
        if (_hud != null) { _hud.SetPrompt(null); _hud.SetFocusPrompt(null); }

        // Into the door = the way the arrival point does not face.
        Vector3 into = -door.transform.forward;
        into.y = 0f;
        into = into.sqrMagnitude > 0.0001f ? into.normalized : Vector3.forward;
        Vector3 doorway = new Vector3(door.transform.position.x, _player.position.y, door.transform.position.z);
        Vector3 start = _player.position;
        Vector3 threshold = doorway - into * 0.9f;
        Vector3 stop = doorway - into * doorStopDistance;
        Vector3 Look(Vector3 from) => from + into * 4f + Vector3.up * 1.4f;

        AudioSource.PlayClipAtPoint(DoorAudio.BuildClunkClip(0.35f, 60f, 0.5f), doorway, 0.8f);

        // The leaves open away from the player, so a leaf never sweeps through them.
        Transform[] hinges = door.leafHingeB != null ? new[] { door.leafHinge, door.leafHingeB } : new[] { door.leafHinge };
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

        // ---- at black: the hub goes away and floor n begins exactly as it always did
        for (int i = 0; i < hinges.Length; i++) hinges[i].localRotation = closed[i];
        RestoreAtmosphere();
        SetContentActive(false);
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

    private void SetBlackout(float alpha)
    {
        if (_blackout != null) _blackout.color = new Color(0f, 0f, 0f, alpha);
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
