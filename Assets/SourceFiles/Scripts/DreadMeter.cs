using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F104 B1: one hidden 0..1 fear value for the whole run. Six inputs feed it (how close the hunter is by path, whether
/// it is chasing in sight, a noise it just heard, time spent in the dark, exertion, a Stan touch); a floor that rises with
/// star progress keeps it from ever resting at zero late in a floor. The heartbeat, the screen effects, the phantom pacing
/// and the hunter's threat dial all read it, so a sprint through the dark makes the heart race even with the hunter
/// nowhere near.
///
/// It also forces a backoff (Alien: Isolation): held near the top for long enough while the hunter is not on you, the
/// hunter is sent away for a stretch, so tension arrives in waves instead of a staircase.
///
/// Reads only. It never moves the player, never touches GameOutcome, and ticks only while a run is live.
/// </summary>
public class DreadMeter : MonoBehaviour
{
    [Header("Inputs")]
    [Tooltip("Weight of the hunter's path proximity")]
    [SerializeField] private float proximityWeight = 0.45f;
    [Tooltip("Weight of the hunter chasing with sight")]
    [SerializeField] private float sightWeight = 0.3f;
    [Tooltip("Weight of the pulse after a heard noise (the A7 IT HEARD YOU beat)")]
    [SerializeField] private float heardWeight = 0.2f;
    [Tooltip("Weight of time spent with no light on the player")]
    [SerializeField] private float darknessWeight = 0.15f;
    [Tooltip("Weight of sprinting down the stamina bar, or being exhausted")]
    [SerializeField] private float exertionWeight = 0.1f;
    [Tooltip("Weight of the pulse after the Stalker touches the player")]
    [SerializeField] private float stanWeight = 0.25f;

    [Header("F104 B1 - proximity")]
    [Tooltip("Path distance at or below which proximity is full (m)")]
    [SerializeField] private float proximityNear = 4f;
    [Tooltip("Path distance at or above which proximity is zero (m)")]
    [SerializeField] private float proximityFar = 25f;
    [Tooltip("Seconds between path measurements to the hunter (s)")]
    [SerializeField] private float pathInterval = 0.5f;
    [Tooltip("How far a position may be from the NavMesh and still be measured (m)")]
    [SerializeField] private float navSampleRadius = 3f;

    [Header("F104 B1 - pulses and dark")]
    [Tooltip("Seconds a heard-noise pulse takes to fade (s)")]
    [SerializeField] private float heardPulseSeconds = 6f;
    [Tooltip("Seconds a Stan-touch pulse takes to fade (s)")]
    [SerializeField] private float stanPulseSeconds = 8f;
    [Tooltip("Seconds in the dark for the darkness input to reach full (s)")]
    [SerializeField] private float darkRampSeconds = 20f;
    [Tooltip("Lamp exposure below which the player counts as in the dark")]
    [SerializeField] private float darkLampExposure = 0.1f;

    [Header("F104 B1 - integration")]
    [Tooltip("How much of star progress is kept as a floor under the value")]
    [SerializeField] private float progressFloorWeight = 0.3f;
    [Tooltip("Value gained per second toward a higher goal")]
    [SerializeField] private float riseRate = 0.5f;
    [Tooltip("Value lost per second toward a lower goal")]
    [SerializeField] private float fallRate = 0.15f;
    [Tooltip("Fall rate multiplier while standing in lamplight")]
    [SerializeField] private float litFallMultiplier = 2f;
    [Tooltip("Lamp exposure above which the player counts as in lamplight")]
    [SerializeField] private float litLampExposure = 0.3f;
    [Tooltip("Time constant of the eased copy readers use (s)")]
    [SerializeField] private float smoothingSeconds = 1f;

    [Header("F104 B1 - backoff")]
    [Tooltip("Value at or above which the backoff clock runs")]
    [SerializeField] private float backoffThreshold = 0.85f;
    [Tooltip("Seconds the value must stay above the threshold, without a break, before the hunter backs off (s)")]
    [SerializeField] private float backoffAfterSeconds = 6f;
    [Tooltip("How long the hunter stays away (s, min/max)")]
    [SerializeField] private Vector2 backoffSeconds = new Vector2(20f, 40f);
    [Tooltip("Minimum seconds between two backoffs (s)")]
    [SerializeField] private float backoffCooldown = 90f;

    [Header("F104 B1 - debug overlay")]
    [Tooltip("Font size of the DREAD overlay shown while TerrorSettings.logEvents is on")]
    [SerializeField] private float overlayFontSize = 24f;

    private AIFollower _follower;
    private Transform _player;
    private PlayerStealthState _stealth;
    private PlayerStamina _stamina;
    private Flashlight _flashlight;
    private MazeGenerator _maze;
    private GameOutcome _outcome;
    private TerrorSettings _terror = new TerrorSettings();
    private bool _configured;
    private bool _subscribed;

    private float _progress;
    private float _darkTime;
    private float _heardAt = -9999f;
    private float _stanAt = -9999f;
    private float _proximity;
    private float _nextPathTime;
    private NavMeshPath _path;

    private float _highTime;
    private float _backoffUntil = -1f;
    private float _nextBackoffTime;

    private TextMeshProUGUI _overlay;
    private int _overlayHundredths = -1;
    private bool _overlayBackoff;

    /// <summary>The hidden fear value, 0..1.</summary>
    public float Value { get; private set; }

    /// <summary>Value eased over smoothingSeconds. What the screen, the heart and the threat dial read, so nothing steps.</summary>
    public float Smoothed { get; private set; }

    /// <summary>True while a forced backoff is running: the hunter has been sent away and phantoms crowd in.</summary>
    public bool InBackoff => Time.time < _backoffUntil;

    /// <summary>True when the flag is on and the meter has been configured. Readers fall back to their old behaviour otherwise.</summary>
    public bool IsActive => _terror.dreadMeter && _configured;

    /// <summary>Wired by MazeGenerator.SetUpAtmosphere once everything it reads exists.</summary>
    public void Configure(AIFollower follower, Transform player, PlayerStealthState stealth, PlayerStamina stamina, Flashlight flashlight, MazeGenerator maze, GameOutcome outcome)
    {
        Unsubscribe();
        _follower = follower;
        _player = player;
        _stealth = stealth;
        _stamina = stamina;
        _flashlight = flashlight;
        _maze = maze;
        _outcome = outcome;
        _configured = follower != null && player != null && stealth != null && stamina != null && flashlight != null && maze != null;
        if (_path == null) _path = new NavMeshPath();
        Subscribe();
    }

    /// <summary>Which Tier B features run, and the [Terror] log.</summary>
    public void ApplyTerror(TerrorSettings terror)
    {
        _terror = terror ?? new TerrorSettings();
    }

    private void OnEnable() => Subscribe();

    private void OnDisable() => Unsubscribe();

    // The statics and the follower's event are subscribed together and dropped together; Configure runs after OnEnable.
    private void Subscribe()
    {
        if (_subscribed || !_configured) return;
        _subscribed = true;
        GameManager.ProgressChanged += HandleProgress;
        Stalker.PlayerTouched += HandleStanTouch;
        _follower.HeardYou += HandleHeardYou;
    }

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        GameManager.ProgressChanged -= HandleProgress;
        Stalker.PlayerTouched -= HandleStanTouch;
        if (_follower != null) _follower.HeardYou -= HandleHeardYou;
    }

    private void HandleProgress(int collected, int total) => _progress = total > 0 ? collected / (float)total : 0f;

    private void HandleHeardYou() => _heardAt = Time.time;

    private void HandleStanTouch() => _stanAt = Time.time;

    private void Update()
    {
        bool live = IsActive && GameFlow.IsRunActive && !GameOutcome.IsOver && (_outcome == null || !_outcome.IsEnding);
        UpdateOverlay(live);
        if (!live) return;

        float dt = Time.deltaTime;
        UpdateProximity();
        UpdateDarkTime(dt);

        // Hidden: the value holds. Hiding is the one place the player cannot be doing anything about their fear.
        if (!_stealth.Hidden)
        {
            float goal = Mathf.Max(Mathf.Clamp01(SumInputs()), _progress * progressFloorWeight);
            float rate = goal > Value ? riseRate : fallRate * (_stealth.LampExposure > litLampExposure ? litFallMultiplier : 1f);
            Value = Mathf.MoveTowards(Value, goal, dt * rate);
        }

        Smoothed = Mathf.Lerp(Smoothed, Value, 1f - Mathf.Exp(-dt / Mathf.Max(0.0001f, smoothingSeconds)));
        TickBackoff(dt);
    }

    /// <summary>The weighted sum of the six inputs, before clamping and the progress floor.</summary>
    private float SumInputs()
    {
        float sight = _follower.HasSight && _follower.IsChasing ? 1f : 0f;
        float exertion = _stamina.Exhausted ? 1f : (_stamina.IsSprinting ? 1f - _stamina.Fraction : 0f);

        return proximityWeight * _proximity
            + sightWeight * sight
            + heardWeight * Pulse(_heardAt, heardPulseSeconds)
            + darknessWeight * Mathf.Clamp01(_darkTime / Mathf.Max(0.0001f, darkRampSeconds))
            + exertionWeight * exertion
            + stanWeight * Pulse(_stanAt, stanPulseSeconds);
    }

    /// <summary>1 at the moment of the event, falling linearly to 0 after `seconds`.</summary>
    private static float Pulse(float at, float seconds) => Mathf.Clamp01(1f - (Time.time - at) / Mathf.Max(0.0001f, seconds));

    /// <summary>Rises while there is no torch beam and no lamplight on the player, falls twice as fast otherwise.</summary>
    private void UpdateDarkTime(float dt)
    {
        bool dark = !_flashlight.IsBeamLit && _stealth.LampExposure < darkLampExposure;
        _darkTime = Mathf.Clamp(_darkTime + (dark ? dt : -2f * dt), 0f, darkRampSeconds);
    }

    /// <summary>Re-measures the NavMesh path length to the hunter on its interval; no path (or an unreachable end) counts as 0.</summary>
    private void UpdateProximity()
    {
        if (Time.time < _nextPathTime) return;
        _nextPathTime = Time.time + pathInterval;

        _proximity = 0f;
        if (!NavMesh.SamplePosition(_player.position, out NavMeshHit from, navSampleRadius, NavMesh.AllAreas)) return;
        if (!NavMesh.SamplePosition(_follower.transform.position, out NavMeshHit to, navSampleRadius, NavMesh.AllAreas)) return;
        if (!NavMesh.CalculatePath(from.position, to.position, NavMesh.AllAreas, _path) || _path.status != NavMeshPathStatus.PathComplete) return;

        _proximity = 1f - Mathf.InverseLerp(proximityNear, proximityFar, PathLength(_path));
    }

    private static float PathLength(NavMeshPath path)
    {
        Vector3[] corners = path.corners;
        float length = 0f;
        for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
        return length;
    }

    /// <summary>
    /// Counts continuous time above backoffThreshold with the hunter neither chasing
    /// nor hunting; once it passes backoffAfterSeconds the hunter is sent away. The clock resets on any dip or chase.
    /// </summary>
    private void TickBackoff(float dt)
    {
        if (Value < backoffThreshold || _follower.IsChasing || _follower.IsHunting || _follower.IsCaptured)
        {
            _highTime = 0f;
            return;
        }

        _highTime += dt;
        if (_highTime < backoffAfterSeconds || Time.time < _nextBackoffTime) return;

        float seconds = Random.Range(backoffSeconds.x, backoffSeconds.y);
        if (!_follower.BackOff(seconds, _maze.FarthestCellCenterFrom(_player.position))) return;

        _backoffUntil = Time.time + seconds;
        _nextBackoffTime = Time.time + backoffCooldown;
        _highTime = 0f;
        _terror.Log($"Backoff {seconds:0}s", this);
    }

    // ---------------------------------------------------------------- debug overlay

    /// <summary>
    /// Dev overlay (top-right, not localized on purpose: it only exists while TerrorSettings.logEvents is on). The text is
    /// rebuilt only when the value's hundredths or the backoff flag change, so it does not allocate every frame.
    /// </summary>
    private void UpdateOverlay(bool live)
    {
        bool show = live && _terror.logEvents;
        if (_overlay == null)
        {
            if (!show) return;
            BuildOverlay();
            if (_overlay == null) return;
        }

        if (_overlay.gameObject.activeSelf != show) _overlay.gameObject.SetActive(show);
        if (!show) return;

        int hundredths = Mathf.RoundToInt(Value * 100f);
        bool backoff = InBackoff;
        if (hundredths == _overlayHundredths && backoff == _overlayBackoff) return;

        _overlayHundredths = hundredths;
        _overlayBackoff = backoff;
        _overlay.text = "DREAD " + (hundredths / 100f).ToString("0.00") + (backoff ? "  BACKOFF" : string.Empty);
    }

    private void BuildOverlay()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        // Top-left holds the counter, floor label and shards; top-centre the objective card; top-right is free.
        _overlay = RuntimeUi.CreateText(canvas.transform, "DreadOverlay", string.Empty, overlayFontSize, new Color(1f, 0.45f, 0.35f));
        _overlay.alignment = TextAlignmentOptions.TopRight;
        _overlay.raycastTarget = false;
        RectTransform rect = _overlay.rectTransform;
        rect.anchorMin = new Vector2(1f, 1f);
        rect.anchorMax = new Vector2(1f, 1f);
        rect.pivot = new Vector2(1f, 1f);
        rect.anchoredPosition = new Vector2(-30f, -30f);
        rect.sizeDelta = new Vector2(420f, 40f);
    }

    private void OnDestroy()
    {
        if (_overlay != null) Destroy(_overlay.gameObject);
    }
}
