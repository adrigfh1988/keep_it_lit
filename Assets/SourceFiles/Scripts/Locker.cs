using System.Collections;
using StarterAssets;
using UnityEngine;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>Anything the player can hide inside. Implemented by Locker; AIFollower talks only to this.</summary>
public interface IHidingSpot
{
    Vector3 FrontPosition { get; }

    /// <summary>The hunter busts the player out: swing the door open and drop the hiding state.</summary>
    void Expose();

    /// <summary>F104 B3: the hunter checks inside: the door creaks partly open for `seconds`, then shuts. The player stays hidden.</summary>
    void Peek(float seconds);
}

/// <summary>
/// A hiding spot built by MazeGenerator.BuildLockers. Handles its own E-to-enter/leave input like
/// Flashlight handles F, teleports the player inside, locks movement and clamps the look to the slit.
/// </summary>
public class Locker : MonoBehaviour, IHidingSpot
{
    [SerializeField] private float yawRange = 35f;
    [SerializeField] private float pitchRange = 25f;
    [SerializeField] private float doorOpenDegrees = 110f;
    [SerializeField] private float doorOpenSeconds = 0.25f;

    [Header("Themed prefab wiring")]
    [Tooltip("The door's hinge pivot. Opened by rotating local Y by doorOpenDegrees, so the hinge edge must sit at local (0,0,0) of this transform. Body keeps its own collider; total locker depth must stay <= 0.7 m or the 0.5 m player clips through the cell centre.")]
    [SerializeField] private Transform door;
    [Tooltip("Where the player stands once hidden. Keep at least 0.3 m clear of the body's inside faces - the CharacterController is 0.5 m wide and would otherwise clip out.")]
    [SerializeField] private Transform insideAnchor;
    [Tooltip("Where the player stands when they leave, roughly 1 m in front of the door.")]
    [SerializeField] private Transform frontAnchor;

    [Header("F107 D3 - the Tenant")]
    [Tooltip("Seconds the door takes to burst open when something shoves the player out")]
    [SerializeField] private float burstOpenSeconds = 0.08f;
    [Tooltip("Seconds the door hangs open before it slams shut again")]
    [SerializeField] private float burstHoldSeconds = 0.35f;

    [Header("F104 B3 - sanctuary decay")]
    [Tooltip("Seconds hidden before the locker stops being safe: louder breathing, then knocks and scrapes outside")]
    [SerializeField] private float quietSeconds = 15f;
    [Tooltip("Seconds between one presence beat (a knock or a scrape) and the next while hidden (x = min, y = max)")]
    [SerializeField] private Vector2 presenceInterval = new Vector2(12f, 25f);
    [Tooltip("How far the door creaks open when the hunter peeks in (degrees)")]
    [SerializeField] private float peekDegrees = 35f;
    [Tooltip("Seconds the door takes to open to the peek angle, and to shut again")]
    [SerializeField] private float peekOpenSeconds = 0.6f;
    [Tooltip("Breath volume (before the clip's own volume) once the ramp is complete; it starts at 0.3")]
    [SerializeField] private float breathVolumeMax = 0.55f;
    [Tooltip("Seconds, after quietSeconds, over which the breathing swells to breathVolumeMax")]
    [SerializeField] private float breathRampSeconds = 30f;
    [Tooltip("Degrees the door twitches on a knock")]
    [SerializeField] private float joltDegrees = 2f;
    [Tooltip("Seconds a knock's door twitch lasts, out and back")]
    [SerializeField] private float joltSeconds = 0.1f;
    [Tooltip("How far from the locker's front a scrape on a nearby wall is heard (m)")]
    [SerializeField] private float scrapeDistance = 2.5f;
    [Tooltip("Low-pass cutoff of the scrape: it comes through a wall (Hz)")]
    [SerializeField] private float scrapeLowPassHz = 1200f;

    private const float BaseBreathVolume = 0.3f;
    private const float KnockVolume = 0.5f;
    private const float ScrapeVolume = 0.4f;

    public Transform Door => door;
    public Transform InsideAnchor => insideAnchor;
    public Transform FrontAnchor => frontAnchor;

    private Vector3 _insidePosition;
    private float _facingYaw;
    private Transform _door;

    private Transform _player;
    private PlayerStealthState _stealth;
    private Flashlight _flashlight;
    private PlayerHud _hud;
    private PlayerInteractor _interactor;
    private ThirdPersonController _controller;

    private TerrorSettings _terror = new TerrorSettings();
    private AIFollower _hunter;
    private Quaternion _doorClosed = Quaternion.identity;
    private Coroutine _presence;
    private Coroutine _doorMotion;
    private bool _peeking;

    private bool _playerInTrigger;
    private bool _exposed;
    private float _savedBottom;
    private float _savedTop;

    public Vector3 FrontPosition { get; private set; }
    public bool Occupied { get; private set; }

    /// <summary>F107 D3: set when something already lives in this locker. E asks it instead of entering; the player is never hidden here.</summary>
    public LockerTenant Tenant { get; set; }

    /// <summary>Called by MazeGenerator.BuildLockers right after the geometry is built.</summary>
    public void Configure(Vector3 insidePosition, Vector3 frontPosition, float facingYaw, Transform door)
    {
        _insidePosition = insidePosition;
        FrontPosition = frontPosition;
        _facingYaw = facingYaw;
        _door = door;
        if (door != null) _doorClosed = door.localRotation;
    }

    /// <summary>F104 B3: which sanctuary-decay features run, and the [Terror] log.</summary>
    public void ApplyTerror(TerrorSettings terror)
    {
        _terror = terror ?? new TerrorSettings();
    }

    /// <summary>F104 B3: the hunter, so presence beats never sound mid-chase (the chase is its own scare).</summary>
    public void BindHunter(AIFollower hunter)
    {
        _hunter = hunter;
    }

    /// <summary>Called by MazeGenerator.SetUpAtmosphere once the player and HUD exist.</summary>
    public void Bind(Transform player, PlayerStealthState stealth, Flashlight flashlight, PlayerHud hud, PlayerInteractor interactor)
    {
        _player = player;
        _stealth = stealth;
        _flashlight = flashlight;
        _hud = hud;
        _interactor = interactor;
        _controller = player != null ? player.GetComponent<ThirdPersonController>() : null;
    }

    private void Update()
    {
        if (_player == null || _stealth == null || _controller == null) return;

#if ENABLE_INPUT_SYSTEM
        bool pressed = (Keyboard.current != null && Keyboard.current.eKey.wasPressedThisFrame)
                    || (Gamepad.current != null && Gamepad.current.buttonWest.wasPressedThisFrame);
#else
        bool pressed = false;
#endif
        pressed |= TouchInput.Pressed(TouchInput.TouchAction.Interact);

        if (!pressed || !(GameFlow.IsRunActive || GameFlow.IsInIntake || GameFlow.IsInHub) || GameOutcome.IsOver) return;

        // F72 decision D12: a closer/more specific interactable (door, button, vault loot) wins the
        // press over hiding. Leaving a locker you are already inside always works regardless.
        if (!Occupied && _interactor != null && _interactor.HasFocus) return;

        if (Occupied) Leave();
        else if (_playerInTrigger)
        {
            if (Tenant != null) Tenant.Attempt(); // F107 D3: something is already in here
            else Enter();
        }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInTrigger = true;
        if (!Occupied && _hud != null) RefreshPrompt();
    }

    private void OnTriggerExit(Collider other)
    {
        if (!IsPlayer(other)) return;
        _playerInTrigger = false;
        if (!Occupied && _hud != null) _hud.SetPrompt(null);
    }

    // Two objects carry the Player tag - a static root and the child that actually moves - so the
    // CharacterController is what tells them apart, the same rule AIFollower.FindTarget uses.
    private static bool IsPlayer(Collider other)
    {
        return other.CompareTag("Player") && other.GetComponent<CharacterController>() != null;
    }

    private void Enter()
    {
        _stealth.SetHidden(this);
        Occupied = true;
        _exposed = false;

        if (_flashlight != null)
        {
            _flashlight.ForceOff();
            _flashlight.InputEnabled = false;
        }

        CharacterController cc = _player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        _player.position = _insidePosition;
        _player.rotation = Quaternion.Euler(0f, _facingYaw, 0f);
        if (cc != null) cc.enabled = true;

        _controller.MovementLocked = true;
        // Snap the view to look out through the slit before the clamp locks it there.
        _controller.LookAt(FrontPosition + Vector3.up * 1.6f, 1f);
        _controller.SetYawClamp(_facingYaw, yawRange);
        _savedBottom = _controller.BottomClamp;
        _savedTop = _controller.TopClamp;
        _controller.BottomClamp = -pitchRange;
        _controller.TopClamp = pitchRange;

        if (_hud != null) _hud.SetPromptKey("E", "hud.prompt.leave");
        GameAudio.PlayAt(GameAudio.Library.lockerDoor, transform.position, 0.7f); // F97: the door pulled shut (recorded clip only)
        StartBreathing();
        StartPresence(); // F104 B3
    }

    // F97: breathing inside the locker - a recorded loop-with-gaps at low volume, stopped on exit or when the hunter opens the door.
    private Coroutine _breathing;
    private AudioSource _breathSource;

    private void StartBreathing()
    {
        if (!GameAudio.Library.lockerBreath.Has) return;
        StopBreathing();
        _breathing = StartCoroutine(BreathRoutine());
    }

    private void StopBreathing()
    {
        if (_breathing != null) StopCoroutine(_breathing);
        _breathing = null;
        if (_breathSource != null) { Destroy(_breathSource.gameObject); _breathSource = null; }
    }

    private IEnumerator BreathRoutine()
    {
        AudioSlot slot = GameAudio.Library.lockerBreath;
        GameObject go = new GameObject("LockerBreath");
        go.transform.SetParent(transform, false);
        _breathSource = go.AddComponent<AudioSource>();
        _breathSource.playOnAwake = false;
        _breathSource.spatialBlend = 0f;
        yield return new WaitForSeconds(0.6f);
        while (Occupied)
        {
            AudioClip clip = slot.Pick();
            if (clip == null) yield break;
            _breathSource.clip = clip;
            _breathSource.volume = Mathf.Clamp01(BreathVolume() * slot.volume);
            _breathSource.pitch = slot.PickPitch();
            _breathSource.Play();
            yield return new WaitForSeconds(clip.length / _breathSource.pitch + Random.Range(0.8f, 2.5f));
        }
    }

    private void Leave()
    {
        StopBreathing();
        StopPresence(); // F104 B3
        GameAudio.PlayAt(GameAudio.Library.lockerDoor, transform.position, 0.7f); // F97
        CharacterController cc = _player.GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;
        _player.position = FrontPosition;
        _player.rotation = Quaternion.Euler(0f, _facingYaw, 0f);
        if (cc != null) cc.enabled = true;

        _controller.MovementLocked = false;
        _controller.ClearYawClamp();
        _controller.BottomClamp = _savedBottom;
        _controller.TopClamp = _savedTop;

        // Torch stays off - the player chooses whether to switch it back on.
        if (_flashlight != null) _flashlight.InputEnabled = true;
        _stealth.SetHidden(null);
        Occupied = false;

        if (_hud != null)
        {
            if (_playerInTrigger) _hud.SetPromptKey("E", "hud.prompt.hide");
            else _hud.SetPrompt(null);
        }
    }

    /// <summary>
    /// The hunter watched the player climb in and has now arrived: swing the door open and hand
    /// control back so the capture sequence's camera ease can turn onto its eyes. Harmless if called
    /// twice - EnterCaptured only fires PlayerCaught once, but this guards regardless.
    /// </summary>
    public void Expose()
    {
        if (_exposed) return;
        _exposed = true;
        StopBreathing(); // F97
        StopPresence(); // F104 B3
        // A peek or knock may be mid-swing; the bust opens the door itself from wherever it is.
        if (_doorMotion != null) StopCoroutine(_doorMotion);
        _doorMotion = null;
        _peeking = false;
        GameAudio.PlayAt(GameAudio.Library.lockerDoor, transform.position, 0.9f);

        if (_door != null) StartCoroutine(OpenDoor());

        if (_controller != null)
        {
            _controller.ClearYawClamp();
            _controller.BottomClamp = _savedBottom;
            _controller.TopClamp = _savedTop;
        }

        if (_stealth != null)
        {
            _stealth.SetHidden(null);
            _stealth.LastExposed = true;
        }
    }

    /// <summary>Second Wind after a locker bust: Expose() left the player inside with movement locked. Full exit.</summary>
    public void ForceLeave()
    {
        if (Occupied) Leave();
    }

    // ---------------------------------------------------------------- F107 D3: the Tenant

    /// <summary>F107 D3: the trigger prompt for standing at this locker: "hide", or the tenant's "it holds the door" once it has shoved the player out.</summary>
    public void RefreshPrompt()
    {
        if (_hud == null) return;
        _hud.SetPromptKey("E", Tenant != null && Tenant.Held ? "hud.tenant.held" : "hud.prompt.hide");
    }

    /// <summary>F107 D3: the door bursts open and slams shut again, fast (a tenant shoving the player out). Ignored while someone is hidden inside.</summary>
    public void Burst()
    {
        if (_door == null || Occupied) return;

        if (_doorMotion != null) StopCoroutine(_doorMotion);
        _doorMotion = StartCoroutine(BurstRoutine());
    }

    private IEnumerator BurstRoutine()
    {
        yield return TurnDoor(_doorClosed * Quaternion.Euler(0f, doorOpenDegrees, 0f), burstOpenSeconds);
        yield return new WaitForSeconds(burstHoldSeconds);
        yield return TurnDoor(_doorClosed, burstOpenSeconds * 1.5f);
        _doorMotion = null;
    }

    // ---------------------------------------------------------------- F104 B3: sanctuary decay

    /// <summary>Breath volume for the next breath: it swells from the base level to breathVolumeMax once the player has stayed hidden past quietSeconds.</summary>
    private float BreathVolume()
    {
        if (!_terror.sanctuaryDecay || _stealth == null) return BaseBreathVolume;

        float t = Mathf.Clamp01((Time.time - _stealth.HiddenSince - quietSeconds) / breathRampSeconds);
        return Mathf.Lerp(BaseBreathVolume, breathVolumeMax, t);
    }

    private void StartPresence()
    {
        StopPresence();
        if (_terror.sanctuaryDecay) _presence = StartCoroutine(PresenceRoutine());
    }

    private void StopPresence()
    {
        if (_presence != null) StopCoroutine(_presence);
        _presence = null;
    }

    /// <summary>Hiding stops being safe: after quietSeconds, something knocks or scrapes outside every so often, until the player leaves. Silent mid-chase.</summary>
    private IEnumerator PresenceRoutine()
    {
        yield return new WaitForSeconds(quietSeconds);
        while (Occupied)
        {
            yield return new WaitForSeconds(Random.Range(presenceInterval.x, presenceInterval.y));
            if (!Occupied) yield break;
            if (_hunter != null && _hunter.IsChasing) continue;

            if (Random.value < 0.5f) Knock();
            else Scrape();
        }
    }

    /// <summary>A knock on this locker's metal, with a small twitch of the door.</summary>
    private void Knock()
    {
        _terror.Log("Locker presence: knock", this);
        GameAudio.PlayAt(GameAudio.Library.pipeClank, FrontPosition + Vector3.up * 1.2f, KnockVolume);
        if (_door != null && !_peeking)
        {
            if (_doorMotion != null) StopCoroutine(_doorMotion);
            _doorMotion = StartCoroutine(JoltRoutine());
        }
    }

    /// <summary>A scrape on a wall somewhere nearby, muffled as if through the wall.</summary>
    private void Scrape()
    {
        _terror.Log("Locker presence: scrape", this);
        Vector2 offset = Random.insideUnitCircle.normalized * scrapeDistance;
        AudioSlot slot = GameAudio.Library.farDrag;
        AudioSource scrape = GameAudio.SpawnAt(slot, FrontPosition + new Vector3(offset.x, 1f, offset.y), ScrapeVolume);
        // A slot with its own low-pass already got a filter from GameAudio (and AudioLowPassFilter allows only one per object).
        if (scrape != null && slot.lowPassHz <= 0f) scrape.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = scrapeLowPassHz;
    }

    private IEnumerator JoltRoutine()
    {
        Quaternion jolted = _doorClosed * Quaternion.Euler(0f, joltDegrees, 0f);
        yield return TurnDoor(jolted, joltSeconds * 0.5f);
        yield return TurnDoor(_doorClosed, joltSeconds * 0.5f);
        _doorMotion = null;
    }

    /// <summary>
    /// F104 B3: the hunter looks in. The door creaks open to peekDegrees, stays there for `seconds`, and shuts again.
    /// The player stays hidden throughout; ignored when nobody is inside or a peek is already running.
    /// </summary>
    public void Peek(float seconds)
    {
        if (!Occupied || _peeking || _door == null || _exposed) return;

        if (_doorMotion != null) StopCoroutine(_doorMotion);
        _doorMotion = StartCoroutine(PeekRoutine(seconds));
    }

    private IEnumerator PeekRoutine(float seconds)
    {
        _peeking = true;
        GameAudio.PlayAt(GameAudio.Library.lockerDoor, transform.position, 0.6f);
        yield return TurnDoor(_doorClosed * Quaternion.Euler(0f, peekDegrees, 0f), peekOpenSeconds);
        yield return new WaitForSeconds(seconds);
        yield return TurnDoor(_doorClosed, peekOpenSeconds);
        GameAudio.PlayAt(GameAudio.Library.lockerDoor, transform.position, 0.5f);
        _peeking = false;
        _doorMotion = null;
    }

    /// <summary>Swings the door to `target` over `seconds`, from wherever it is now.</summary>
    private IEnumerator TurnDoor(Quaternion target, float seconds)
    {
        Quaternion start = _door.localRotation;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            _door.localRotation = Quaternion.Slerp(start, target, t / seconds);
            yield return null;
        }

        _door.localRotation = target;
    }

    private IEnumerator OpenDoor()
    {
        Quaternion start = _door.localRotation;
        Quaternion end = start * Quaternion.Euler(0f, doorOpenDegrees, 0f);
        float t = 0f;

        while (t < doorOpenSeconds)
        {
            t += Time.deltaTime;
            _door.localRotation = Quaternion.Slerp(start, end, Mathf.Clamp01(t / doorOpenSeconds));
            yield return null;
        }

        _door.localRotation = end;
    }
}
