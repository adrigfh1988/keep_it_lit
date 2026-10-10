using System.Collections;
using UnityEngine;

/// <summary>
/// F107 D3: the Tenant. One locker per level is already occupied: try to hide in it and the door bursts open, you are shoved back out with a
/// slam the hunter hears (and every listening entity, through MazeNoise), and the locker keeps its door shut for the rest of the level. The only
/// warning is audio: a faint, irregular breath, knock or scratch, audible only within a few metres, so you have to listen before you hide.
/// Added by MazeGenerator.BuildEntities to the chosen Locker's GameObject (the Locker stays generic: it only asks its Tenant when E is pressed).
/// Not a killer. Spec: plannings/descent-mode-spec.md D3.
/// </summary>
public class LockerTenant : MazeEntity
{
    [Header("F107 D3 - The tell")]
    [Tooltip("The tell is audible within this distance of the locker (m)")]
    [SerializeField, Min(1f)] private float tellRange = 4f;
    [Tooltip("Seconds between one tell and the next (x = min, y = max)")]
    [SerializeField] private Vector2 tellGap = new Vector2(4f, 9f);
    [Tooltip("When the player walks up to the locker, the next tell comes within this many seconds at the latest")]
    [SerializeField, Min(0.5f)] private float firstTellMax = 3f;
    [Tooltip("Low-pass cutoff of the tell when its audio slot has none of its own (Hz): it differs from the player's own breathing")]
    [SerializeField, Min(200f)] private float tellLowPassHz = 900f;

    [Header("F107 D3 - The shove")]
    [Tooltip("How far the player is pushed back along the locker's facing (m). Moved with the CharacterController, so walls stop it")]
    [SerializeField, Min(0f)] private float pushDistance = 1.5f;
    [SerializeField, Min(0.05f)] private float pushSeconds = 0.25f;
    [Tooltip("The slam is a noise of this radius (m): the hunter and the Listener come to look")]
    [SerializeField, Min(0f)] private float slamNoiseRadius = 30f;
    [SerializeField, Min(0f)] private float shakeSeconds = 0.3f;
    [Tooltip("Peak view jitter of the shove (degrees, before the Camera shake setting)")]
    [SerializeField, Min(0f)] private float shakeDegrees = 1.5f;

    private Locker _locker;
    private bool _configured;
    private bool _held;
    private bool _wasNear;
    private float _nextTell;
    private CharacterController _characterController;

    public override EntityKind Kind => EntityKind.Tenant;

    public override char DebugGlyph => 't';

    /// <summary>True once it has shoved the player out: the prompt reads "it holds the door" and E does nothing.</summary>
    public bool Held => _held;

    /// <summary>How far the last shove actually moved the player (m, flat), for the smoke test.</summary>
    public float LastShoveDistance { get; private set; }

    /// <summary>The locker this tenant lives in.</summary>
    public Locker Locker => _locker;

    public override void Configure(MazeGenerator maze, Transform player, AIFollower hunter)
    {
        BindSystems(maze, player, hunter);
        _locker = GetComponent<Locker>();
        if (_locker == null)
        {
            Debug.LogWarning("LockerTenant: needs a Locker on the same GameObject - it stays inert.", this);
            return;
        }

        _locker.Tenant = this;
        _characterController = player != null ? player.GetComponent<CharacterController>() : null;
        _nextTell = Time.time + Random.Range(tellGap.x, tellGap.y);
        _configured = true;
    }

    private void OnDestroy()
    {
        if (_locker != null && _locker.Tenant == this) _locker.Tenant = null;
    }

    /// <summary>Called by Locker when E is pressed in its trigger: the first time it shoves the player out, afterwards it does nothing.</summary>
    public void Attempt()
    {
        if (!_configured || _held || !CanAct) return;
        Shove();
    }

    private void Update()
    {
        if (!_configured || !CanAct) return;

        bool near = FlatDistanceToPlayer(_locker.transform.position) <= tellRange;
        if (near && !_wasNear && _nextTell - Time.time > firstTellMax) _nextTell = Time.time + Random.Range(1.5f, firstTellMax);
        _wasNear = near;

        if (Time.time < _nextTell) return;

        _nextTell = Time.time + Random.Range(tellGap.x, tellGap.y);
        if (near) PlayTell();
    }

    // ---------------------------------------------------------------- the tell

    /// <summary>One faint sound from inside the locker: a breath (40 %), a knock (30 %) or a nail scratch (30 %), low-passed and quiet.</summary>
    private void PlayTell()
    {
        GameAudioLibrary lib = GameAudio.Library;
        float roll = Random.value;
        AudioSlot slot;
        float volume;
        float pitch;
        string what;
        if (roll < 0.4f) { slot = lib.lockerBreath; volume = 0.5f; pitch = 0.8f; what = "breath"; }
        else if (roll < 0.7f) { slot = lib.pipeClank; volume = 0.35f; pitch = 1.1f; what = "knock"; }
        else { slot = lib.farDrag; volume = 0.4f; pitch = 1.3f; what = "scratch"; }

        Vector3 at = _locker.transform.position + Vector3.up * 1.2f;
        AudioSource source = GameAudio.SpawnAt(slot, at, volume, pitch, tellRange);
        if (source == null) return;

        // A slot with its own low-pass already got a filter from GameAudio (and AudioLowPassFilter allows only one per object).
        if (slot.lowPassHz <= 0f) source.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = tellLowPassHz;
        LogEvent($"tell: {what}");
    }

    // ---------------------------------------------------------------- the shove

    private void Shove()
    {
        _held = true;
        Vector3 at = _locker.transform.position + Vector3.up;
        _locker.Burst();
        _locker.RefreshPrompt();

        GameAudioLibrary lib = GameAudio.Library;
        PlayFirst(at, 1f, 0.85f, lib.shutterSlam, lib.farSlam);
        PlayFirst(at, 0.8f, 1f, lib.lockerDoor);
        Hunter?.HearNoise(at, slamNoiseRadius); // MazeNoise carries it on to every listening entity
        PulseDread();

        StartCoroutine(ShoveRoutine());
        StartCoroutine(ShakeView(shakeSeconds, shakeDegrees));
        LogEvent($"shove: HearNoise({slamNoiseRadius:0})");
    }

    /// <summary>Pushes the player back along the locker's facing with the CharacterController (it sweeps, so a wall stops the push short).</summary>
    private IEnumerator ShoveRoutine()
    {
        Vector3 direction = _locker.transform.forward;
        direction.y = 0f;
        direction.Normalize();

        bool wasLocked = Controller != null && Controller.MovementLocked;
        if (Controller != null) Controller.MovementLocked = true; // their own movement would fight the push
        Vector3 start = Player.position;

        float speed = pushDistance / pushSeconds;
        for (float t = 0f; t < pushSeconds; t += Time.deltaTime)
        {
            if (_characterController == null || !_characterController.enabled) break;
            _characterController.Move(direction * (speed * Time.deltaTime));
            yield return null;
        }

        if (Controller != null && !wasLocked && CanAct) Controller.MovementLocked = false;

        Vector3 moved = Player.position - start;
        moved.y = 0f;
        LastShoveDistance = moved.magnitude;
        LogEvent($"shove: pushed {LastShoveDistance:0.00} m");
    }
}
