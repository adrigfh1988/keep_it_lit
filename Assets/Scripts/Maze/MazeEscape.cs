using System.Collections;
using StarterAssets;
using TMPro;
using UnityEngine;

/// <summary>
/// The endgame. Collecting the last star does not win: the way out opens and the player has a countdown to reach it. On a hatch floor a hatch opens
/// somewhere in the maze (one minute, or more on the early floors); dropping through it wins the floor. On a gate floor (F111) the maze's exit gates
/// get power instead (two minutes) and one has to be opened by holding E on its lever and walked out of. Running out of time loses.
/// </summary>
public partial class MazeEscape : MonoBehaviour
{
    [Header("Timing")]
    [Tooltip("Seconds to find the hatch once it opens")]
    [SerializeField] private float escapeSeconds = 60f;

    [Header("Hatch")]
    [SerializeField] private Color hatchColor = new Color(0.25f, 1f, 0.7f);
    [Tooltip("How close the player has to get to drop through")]
    [SerializeField] private float hatchTriggerRadius = 1.1f;
    [SerializeField] private float hatchLightRange = 12f;
    [SerializeField] private float hatchLightIntensity = 5f;
    [Tooltip("Cells this far or closer to the player when the hatch opens are skipped, so it is never a free win")]
    [SerializeField] private float minimumDistanceFromPlayer = 12f;

    [Header("Exit gates (F111)")]
    [Tooltip("The countdown on a gate floor (s), whatever the floor; the profile's EscapeSeconds is ignored there")]
    [SerializeField] private float gateEscapeSeconds = 120f;

    [Header("Drop")]
    [Tooltip("How far below the maze floor the landing platform sits")]
    [SerializeField] private float dropDepth = 4f;
    [Tooltip("How far below the maze floor the player starts the drop. Must clear the 0.2 m floor slab, or the capsule spawns inside it and gets shoved.")]
    [SerializeField] private float dropStartDepth = 2.5f;
    [SerializeField] private float platformSize = 8f;

    [Header("UI")]
    [SerializeField] private Color timerColor = Color.red;
    [SerializeField] private int timerFontSize = 64;

    [Header("Wayfinding")]
    [SerializeField] private float pingInterval = 2f;
    [SerializeField] private float pingVolume = 0.7f;
    [Tooltip("Must exceed the maze diagonal (~47 m for 11x11 at 3 m) so the ping is audible from anywhere")]
    [SerializeField] private float pingMaxDistance = 60f;
    [SerializeField] private float chevronRadius = 70f;

    [Header("F104 B4 - escape hunt")]
    [Tooltip("The hatch is placed this far from the player by walking path (m, min/max)")]
    [SerializeField] private Vector2 hatchPathMetres = new Vector2(25f, 40f);
    [Tooltip("...and at least this far (flat) from the hunter (m)")]
    [SerializeField] private float hatchMinHunterDistance = 15f;
    [Tooltip("Scale applied to the path window, once, when no cell fits it: min x this, max x its inverse")]
    [SerializeField] private float hatchWindowWiden = 0.75f;
    [Tooltip("While the escape runs and the hunter is not chasing, it is pointed at the player this often (s)")]
    [SerializeField] private float hunterPingInterval = 15f;
    [Tooltip("The chevron disappears for the last this-many seconds; the ping stays (s)")]
    [SerializeField] private float chevronHideLastSeconds = 20f;
    [Tooltip("The lamp wave travels outward from the player at this speed (m/s)")]
    [SerializeField] private float lampWaveSpeed = 6f;
    [Tooltip("Chance each lamp in the wave dies (not while the hatch is padlocked)")]
    [Range(0f, 1f)]
    [SerializeField] private float lampWaveKillFraction = 0.3f;
    [Tooltip("Lamps this close to the hatch are left alone (m), so the way out stays lit")]
    [SerializeField] private float lampWaveHatchClearance = 6f;
    [Tooltip("How long each lamp in the wave goes dark before it returns or dies (s)")]
    [SerializeField] private float lampWaveBlinkSeconds = 0.3f;
    [Tooltip("Fade of a lamp the wave kills (s)")]
    [SerializeField] private float lampWaveKillFade = 1.5f;
    [Tooltip("The alarm at the hatch sounds this long, then fades (s)")]
    [SerializeField] private float alarmSeconds = 20f;
    [SerializeField] private float alarmVolume = 0.6f;
    [Tooltip("How far the alarm carries (m)")]
    [SerializeField] private float alarmMaxDistance = 35f;
    [Tooltip("Seconds the alarm takes to fade out (s)")]
    [SerializeField] private float alarmFadeSeconds = 2f;

    private MazeGenerator _maze;
    private Transform _player;
    private GameOutcome _outcome;
    private Material _emissiveSource;
    private PlayerStealthState _stealth;
    private AIFollower _hunter;
    private TerrorSettings _terror = new TerrorSettings();
    // F104 B4: when the next hunter ping is due, the alarm's object (so Stop can silence it), and the reused hatch-candidate list.
    private float _nextHunterPing;
    private bool _huntActive;
    private readonly System.Collections.Generic.List<GameObject> _alarmObjects = new System.Collections.Generic.List<GameObject>();
    private readonly System.Collections.Generic.List<Vector3> _hatchCandidates = new System.Collections.Generic.List<Vector3>();

    // F77 Key Hunt: the padlock. A KeyHunt floor's hatch/lift refuses to complete while _keys is
    // incomplete - see BindKeyRing/HandleKeysChanged/ApplyLockLook.
    private KeyRing _keys;
    private PlayerHud _hud;
    private bool _locked;
    private float _lockedNudgeTimer;
    private Light _exitLight;
    private Material _exitGlow;
    // F80: the trapdoor's two leaves (null on the primitive fallback and on a Lift). Shut while padlocked,
    // ajar once the hatch can be used, thrown open on the drop.
    private Transform _lidL;
    private Transform _lidR;
    private float _lidAngle;
    private Coroutine _lidRoutine;
    private const float LidAjarAngle = 16f;
    private const float LidOpenAngle = 100f;
    // F80: the walk-in. Stepping on the open trapdoor takes the controls, throws the leaves back, eases
    // the player over the opening looking down, then lowers them through as the blackout comes up.
    private bool _entering;
    private const float HatchApproachSeconds = 0.75f;
    private const float HatchDescendSeconds = 0.45f;
    private AudioClip _padlockClip;
    private Transform _pingAnchor;
    private static readonly Color LockedColor = new Color(0.9f, 0.15f, 0.1f);

    private bool _running;
    private bool _finished;
    private float _timeLeft;
    private Vector3 _hatchPosition;

    // F111: on a gate floor (MazeGenerator.HasExitGates) the exit is the maze's exit gates, not a hatch: no hatch cell, no landing platform,
    // a fixed countdown, completed by walking out of an open gate (ExitGate.PlayerReached).
    private readonly System.Collections.Generic.List<ExitGate> _gates = new System.Collections.Generic.List<ExitGate>();
    private bool _gateMode;
    private bool _gatesHooked;
    private string _exitKind = "hatch";
    private float _gateLeverSeconds = 10f;

    private TextMeshProUGUI _timerText;
    private TextMeshProUGUI _chevronText;
    private AudioSource _ping;
    private AudioClip _pingClip;
    private float _pingTimer;
    private float _chevronPulse;

    /// <summary>
    /// Where the way out is: the hatch, or on a gate floor the wayfinding exit - the nearest gate to the player (flat), or the nearest one that is
    /// opening or open once there is one. Callers: GameOutcome's out-of-time distance, the reachability check (hatch floors only), the debug map (hatch floors only).
    /// </summary>
    public Vector3 HatchPosition => _gateMode ? NearestExit(_player != null ? _player.position : _hatchPosition) : _hatchPosition;

    /// <summary>True once the escape has begun (the hatch is open or the gates have power).</summary>
    public bool HatchOpen { get; private set; }

    /// <summary>Seconds left on the countdown. Valid even after Stop() - Stop() hides the display, it does not zero the field.</summary>
    public float TimeLeft => Mathf.Max(0f, _timeLeft);

    /// <summary>F122: the value the countdown started from (0 before the hatch / gates open). The floor grade's time margin is measured against it.</summary>
    public float CountdownSeconds { get; private set; }

    /// <summary>F36 Second Wind: holds the countdown without the permanent Stop(), so it can resume.</summary>
    public bool Frozen { get; set; }

    /// <summary>F107 D2: true during the hatch walk-in (freeze, leaves open, the lowering), before GameOutcome.IsEnding flips. Entities never kill then.</summary>
    public bool IsEntering => _entering;

    /// <summary>Per-floor countdown. Read when the hatch opens, so it can be set any time before that.</summary>
    public void SetEscapeSeconds(float seconds) { escapeSeconds = Mathf.Max(10f, seconds); }

    /// <summary>F111: the profile's lever time (10 s, 14 s on the final floor and the DESCENT boss). Passed on to the gates.</summary>
    public void SetGateLeverSeconds(float seconds)
    {
        _gateLeverSeconds = Mathf.Max(1f, seconds);
        foreach (ExitGate gate in _gates) gate.SetLeverSeconds(_gateLeverSeconds);
    }

    /// <summary>F111: the gate floor's countdown from the profile (120 s, +10 with the Long Fuse). Read when the escape opens.</summary>
    public void SetGateEscapeSeconds(float seconds) { gateEscapeSeconds = Mathf.Max(10f, seconds); }

    /// <summary>F111: this floor's exit gates (MazeGenerator.BuildExitGates). Subscribes to them; OnDisable unsubscribes.</summary>
    public void SetGates(System.Collections.Generic.IReadOnlyList<ExitGate> gates)
    {
        HookGates(false);
        _gates.Clear();
        if (gates != null) foreach (ExitGate gate in gates) if (gate != null) _gates.Add(gate);
        _gateMode = _gates.Count > 0;
        foreach (ExitGate gate in _gates) gate.SetLeverSeconds(_gateLeverSeconds);
        if (isActiveAndEnabled) HookGates(true);
    }

    public void Configure(MazeGenerator maze, Transform player, Material emissiveSource, GameOutcome outcome, AIFollower hunter)
    {
        _maze = maze;
        _player = player;
        _emissiveSource = emissiveSource;
        _outcome = outcome;
        _hunter = hunter;
        _stealth = player != null ? player.GetComponent<PlayerStealthState>() : null;
    }

    /// <summary>Freeze the countdown and hide it. Idempotent; called by GameOutcome when any ending begins.</summary>
    public void Stop()
    {
        _running = false;
        _finished = true;
        if (_timerText != null) _timerText.gameObject.SetActive(false);
        if (_ping != null) _ping.Stop();
        EndAlarm();
        foreach (ExitGate gate in _gates) gate.Silence();
    }

    /// <summary>F77: the player's KeyRing (an instance component, built alongside it) and the HUD, for the padlock's subtitles. Unsubscribes any previous ring first - CLAUDE.md's every-subscriber-unsubscribes rule.</summary>
    public void BindKeyRing(KeyRing ring, PlayerHud hud)
    {
        if (_keys != null) _keys.Changed -= HandleKeysChanged;
        _keys = ring;
        _hud = hud;
        if (_keys != null) _keys.Changed += HandleKeysChanged;
    }

    private void OnEnable()
    {
        GameManager.AllStarsCollected += BeginEscape;
        HookGates(true);
    }

    private void OnDisable()
    {
        GameManager.AllStarsCollected -= BeginEscape;
        if (_keys != null) _keys.Changed -= HandleKeysChanged;
        HookGates(false);
    }

    /// <summary>F111: subscribes to (or releases) the gates' events. Idempotent, so OnEnable, OnDisable and SetGates can all call it.</summary>
    private void HookGates(bool on)
    {
        if (on == _gatesHooked) return;
        _gatesHooked = on;
        foreach (ExitGate gate in _gates)
        {
            if (on)
            {
                gate.Opened += HandleGateOpened;
                gate.PlayerReached += HandleGateReached;
            }
            else
            {
                gate.Opened -= HandleGateOpened;
                gate.PlayerReached -= HandleGateReached;
            }
        }
    }

    /// <summary>KeyRing.Changed: once every key is held, the padlock clears and the hatch/gates resume.</summary>
    private void HandleKeysChanged()
    {
        if (!_locked || _keys == null || !_keys.IsComplete) return;
        _locked = false;
        ApplyLockLook();
        _hud?.ShowSubtitle(L10n.T(_gateMode ? "sub.escape.gates_unlocked" : "sub.escape.hatch_unlocked"), 2.5f);
    }

    private void BeginEscape()
    {
        if (_running || _finished || _maze == null || _player == null || GameOutcome.IsOver) return;

        // F77: locked for the whole escape if this is a KeyHunt floor and keys are still missing.
        _locked = _keys != null && !_keys.IsComplete;

        if (_gateMode)
        {
            // F111: the exit gates were built with the maze (MazeGenerator.HasExitGates decided, with the old +17 roll). Power them; no hatch, no platform.
            _exitKind = "gates";
            HatchOpen = true;
            _hatchPosition = NearestExit(_player.position);
            foreach (ExitGate gate in _gates)
            {
                gate.Bind(this, _hunter, _player, _keys);
                gate.Power(_locked);
            }

            // F111: with two gates, a hunter that has lost the player patrols between them (one gate: its normal patrol).
            if (_hunter != null && _gates.Count >= 2)
            {
                var gatePoints = new System.Collections.Generic.List<Vector3>(_gates.Count);
                foreach (ExitGate gate in _gates) gatePoints.Add(gate.Position);
                _hunter.SetGatePatrol(gatePoints);
            }

            BuildPingSource(_hatchPosition);
            _timeLeft = gateEscapeSeconds;
            CountdownSeconds = _timeLeft; // F122
            BuildTimerText();
            ApplyLockLook();
            _hud?.ShowSubtitle(L10n.T("sub.escape.gates_powered"), 2.5f);
        }
        else
        {
            _hatchPosition = ChooseHatchCell();
            HatchOpen = true;
            BuildHatch(_hatchPosition);
            BuildLandingPlatform(_hatchPosition);
            _timeLeft = escapeSeconds;
            CountdownSeconds = _timeLeft; // F122
            BuildTimerText();
            ApplyLockLook();
        }

        _running = true;

        if (_terror.escapeHunt && !_maze.SuperEasyMode) BeginEscapeHunt();

        Debug.Log($"MazeEscape: {_exitKind} {(_gateMode ? "powered" : "open")}{(_gateMode ? $" ({_gates.Count})" : $" at {_hatchPosition}")}. {_timeLeft:0} seconds.", this);
    }

    /// <summary>ExitGate.Opened: say so, and let the ping and chevron move on to the open gate right away.</summary>
    private void HandleGateOpened(ExitGate gate)
    {
        _hud?.ShowSubtitle(L10n.T("sub.escape.gate_open"), 2f);
        _pingTimer = 0f;
    }

    /// <summary>ExitGate.PlayerReached (every frame the player is in an open gate's stub): starts the walk-out once.</summary>
    private void HandleGateReached(ExitGate gate)
    {
        if (!_running || _finished || Frozen || _entering || GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding)) return;
        if (_stealth != null && _stealth.Hidden) return;
        StartCoroutine(ExitThroughGate(gate));
    }

    /// <summary>The wayfinding exit on a gate floor: the nearest gate that is opening or open, else the nearest gate, by flat distance from `from`.</summary>
    private Vector3 NearestExit(Vector3 from)
    {
        ExitGate best = null;
        float bestDistance = float.MaxValue;
        for (int pass = 0; pass < 2 && best == null; pass++)
        {
            foreach (ExitGate gate in _gates)
            {
                bool open = gate.State == ExitGate.GateState.Opening || gate.State == ExitGate.GateState.Open;
                if (pass == 0 && !open) continue;
                float distance = FlatDistance(gate.Position, from);
                if (distance >= bestDistance) continue;
                bestDistance = distance;
                best = gate;
            }
        }

        return best != null ? best.Position : _hatchPosition;
    }

    /// <summary>True when `point` is within lampWaveHatchClearance of the hatch, or of either gate.</summary>
    private bool IsNearExit(Vector3 point)
    {
        if (!_gateMode) return FlatDistance(point, _hatchPosition) <= lampWaveHatchClearance;
        foreach (ExitGate gate in _gates) if (FlatDistance(point, gate.Position) <= lampWaveHatchClearance) return true;
        return false;
    }

    private void Update()
    {
        if (!_running || _finished || Frozen || _entering) return;

        float dt = Time.deltaTime;
        _timeLeft -= dt;

        if (_timerText != null)
        {
            float shown = Mathf.Max(0f, _timeLeft);
            _timerText.text = FormatTime(shown);

            // Pulse once a second under ten, so the last stretch reads without having to look at it
            if (shown <= 10f)
            {
                float pulse = 0.55f + 0.45f * Mathf.Abs(Mathf.Sin(shown * Mathf.PI));
                _timerText.color = new Color(timerColor.r, timerColor.g, timerColor.b, pulse);
            }
        }

        UpdatePing(dt);
        UpdateChevron(dt);
        UpdateHunterPing();

        if (_timeLeft <= 0f)
        {
            _outcome.Lose(LoseReason.OutOfTime);
            Stop();
            return;
        }

        // Impossible in practice (the hatch spawns >= 12 m from the player and a locker cannot move
        // them there), but guarded anyway: a hatch drop must never fire while inside a locker.
        if (_stealth != null && _stealth.Hidden) return;

        // F111: a gate floor is completed by ExitGate.PlayerReached (HandleGateReached), not by a trigger here.
        if (_gateMode) return;

        Vector3 toHatch = _player.position - _hatchPosition;
        toHatch.y = 0f;
        if (toHatch.sqrMagnitude <= hatchTriggerRadius * hatchTriggerRadius)
        {
            if (_locked) { NudgeLocked(dt); return; }
            // The primitive fallback has no leaves to open, so it keeps the instant drop.
            if (_lidL != null || _lidR != null) StartCoroutine(EnterHatch());
            else CompleteEscape();
        }
    }

    /// <summary>F77: walking onto a locked hatch does not drop the player - a padlock clunk and a reminder subtitle, at most every 2 s.</summary>
    private void NudgeLocked(float dt)
    {
        _lockedNudgeTimer -= dt;
        if (_lockedNudgeTimer > 0f) return;
        _lockedNudgeTimer = 2f;

        _hud?.ShowSubtitle(L10n.T("hud.keys_locked", ("h", _keys.Held), ("t", _keys.Total)), 1.8f);
        if (_padlockClip == null) _padlockClip = DoorAudio.BuildClunkClip(0.18f, 160f, 0.35f);
        if (!GameAudio.PlayAt(GameAudio.Library.lockedRattle, _hatchPosition, 0.7f)) GameAudio.ClipAtPoint(_padlockClip, _hatchPosition, 0.7f); // F97
    }

    internal static string FormatTime(float seconds)
    {
        return $"{Mathf.FloorToInt(seconds / 60f)}:{Mathf.FloorToInt(seconds % 60f):00}";
    }

    /// <summary>F104 B4: which Tier B features run, and the [Terror] log. escapeHunt is read when the hatch opens.</summary>
    public void ApplyTerror(TerrorSettings terror)
    {
        _terror = terror ?? new TerrorSettings();
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }

    /// <summary>F104 B4: starts the parts of the hunt that run on their own - the first ping, the lamp wave from the player and the alarm at the hatch.</summary>
    private void BeginEscapeHunt()
    {
        _huntActive = true;
        _hunter?.ForceLastKnownPosition(_player.position, reactAsHeard: false);
        _nextHunterPing = Time.time + hunterPingInterval;
        StartCoroutine(LampWaveRoutine());
        StartCoroutine(AlarmRoutine());
    }

    /// <summary>Every hunterPingInterval seconds, while the hunter is not already chasing, it is pointed at the spot the player is in (no IT HEARD YOU beat). Hatch floors only.</summary>
    private void UpdateHunterPing()
    {
        // Only once BeginEscapeHunt ran (not in super easy mode, and not with the flag off when the hatch opened).
        // F111: never on a gate floor - its 120 s made the reveals relentless; the levers' noise is what gives the player away there.
        if (_gateMode || !_huntActive || !_terror.escapeHunt || _hunter == null || Time.time < _nextHunterPing) return;

        _nextHunterPing = Time.time + hunterPingInterval;
        if (!_hunter.IsChasing) _hunter.ForceLastKnownPosition(_player.position, reactAsHeard: false);
    }

    private struct LampKill
    {
        public WallLamp Lamp;
        public float At;
    }

    /// <summary>
    /// A ripple of dark moving outward from the player at lampWaveSpeed: each live, powered, non-locker lamp farther than
    /// lampWaveHatchClearance from the hatch blinks as it passes, and some die. While the hatch is padlocked nothing dies - the
    /// player still has keys to find in those corridors.
    /// </summary>
    private IEnumerator LampWaveRoutine()
    {
        System.Collections.Generic.List<LampKill> kills = new System.Collections.Generic.List<LampKill>();
        Vector3 origin = _player.position;
        bool mayKill = !_locked;

        foreach (WallLamp lamp in _maze.Lamps)
        {
            if (lamp == null || lamp.IsLockerLamp || lamp.IsDead || !lamp.IsPowered) continue;
            if (IsNearExit(lamp.transform.position)) continue;

            float delay = FlatDistance(lamp.transform.position, origin) / Mathf.Max(0.01f, lampWaveSpeed);
            lamp.Blink(lampWaveBlinkSeconds, delay);
            if (mayKill && Random.value < lampWaveKillFraction) kills.Add(new LampKill { Lamp = lamp, At = delay + lampWaveBlinkSeconds });
        }

        kills.Sort((a, b) => a.At.CompareTo(b.At));
        float t = 0f;
        for (int i = 0; i < kills.Count; i++)
        {
            while (t < kills[i].At)
            {
                if (_finished) yield break;
                t += Time.deltaTime;
                yield return null;
            }

            if (kills[i].Lamp != null) kills[i].Lamp.Kill(lampWaveKillFade);
        }
    }

    /// <summary>The alarm at the hatch: a 3D loop for alarmSeconds, then a fade. It is diegetic and doubles as wayfinding. An empty slot is silent.</summary>
    private IEnumerator AlarmRoutine()
    {
        AudioSlot slot = GameAudio.Library.escapeAlarm;
        AudioClip clip = slot.Pick();
        if (clip == null) yield break;

        // One source per exit: the hatch, or each gate.
        float volume = alarmVolume * slot.volume;
        System.Collections.Generic.List<AudioSource> sources = new System.Collections.Generic.List<AudioSource>();
        if (_gateMode) foreach (ExitGate gate in _gates) sources.Add(AddAlarmSource(gate.Position, clip, volume));
        else sources.Add(AddAlarmSource(_hatchPosition, clip, volume));

        float t = 0f;
        while (t < alarmSeconds && !_finished)
        {
            t += Time.deltaTime;
            yield return null;
        }

        float fade = 0f;
        while (fade < alarmFadeSeconds && !_finished)
        {
            fade += Time.deltaTime;
            foreach (AudioSource source in sources) if (source != null) source.volume = Mathf.Lerp(volume, 0f, fade / Mathf.Max(0.0001f, alarmFadeSeconds));
            yield return null;
        }

        EndAlarm();
    }

    private AudioSource AddAlarmSource(Vector3 position, AudioClip clip, float volume)
    {
        GameObject holder = new GameObject("EscapeAlarm");
        holder.transform.position = position;
        _alarmObjects.Add(holder);
        AudioSource source = holder.AddComponent<AudioSource>();
        source.clip = clip;
        source.loop = true;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = 2f;
        source.maxDistance = alarmMaxDistance;
        source.volume = volume;
        GameSettings.RouteEffect(source);
        source.Play();
        return source;
    }

    private void EndAlarm()
    {
        foreach (GameObject holder in _alarmObjects) if (holder != null) Destroy(holder);
        _alarmObjects.Clear();
    }

    // ---------------------------------------------------------------- outcomes

    /// <summary>
    /// The trapdoor walk-in (F80). Ends through the same FloorCleared/Stop pair as CompleteEscape; the only
    /// difference is that the player is lowered through the opening instead of being placed under it.
    /// Bails out untouched if an ending (a capture) starts while the player is still stepping across.
    /// </summary>
    private IEnumerator EnterHatch()
    {
        _entering = true;
        PlayerLock.Freeze(_player, true);
        SetLidAngle(LidOpenAngle, 0.4f);
        if (!GameAudio.PlayAt(GameAudio.Library.hatchOpen, _hatchPosition, 0.8f)) GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.3f, 70f, 0.6f), _hatchPosition, 0.8f); // F97

        ThirdPersonController controller = _player.GetComponent<ThirdPersonController>();
        CharacterController characterController = _player.GetComponent<CharacterController>();

        Vector3 start = _player.position;
        Vector3 over = new Vector3(_hatchPosition.x, start.y, _hatchPosition.z);
        Vector3 lookPoint = _hatchPosition + Vector3.down * 2f;

        float t = 0f;
        while (t < HatchApproachSeconds)
        {
            if (GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding))
            {
                _entering = false;
                yield break;
            }

            t += Time.deltaTime;
            MovePlayer(characterController, Vector3.Lerp(start, over, Mathf.SmoothStep(0f, 1f, t / HatchApproachSeconds)));
            if (controller != null) controller.LookAt(lookPoint, 1f - Mathf.Exp(-7f * Time.deltaTime));
            yield return null;
        }

        // The blackout starts here and covers the camera passing through the floor.
        if (controller != null) controller.MovementLocked = true;
        _outcome.FloorCleared();

        t = 0f;
        while (t < HatchDescendSeconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / HatchDescendSeconds);
            MovePlayer(characterController, over + Vector3.down * (dropStartDepth * k * k));
            yield return null;
        }

        Stop();
    }

    /// <summary>
    /// F111: the walk out of an open gate. Freezes the player, eases them a further 1.5 m down the stub looking down the stairwell, then the blackout
    /// (FloorCleared) and Stop, exactly the order of EnterHatch. Bails out untouched if an ending (a capture) starts while the player is still stepping across.
    /// </summary>
    private IEnumerator ExitThroughGate(ExitGate gate)
    {
        _entering = true;
        PlayerLock.Freeze(_player, true);

        ThirdPersonController controller = _player.GetComponent<ThirdPersonController>();
        CharacterController characterController = _player.GetComponent<CharacterController>();

        Vector3 start = _player.position;
        Vector3 over = gate.ExitEasePoint(start.y);
        Vector3 lookPoint = gate.StairLookPoint;

        float t = 0f;
        while (t < HatchApproachSeconds)
        {
            if (GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding))
            {
                _entering = false;
                yield break;
            }

            t += Time.deltaTime;
            MovePlayer(characterController, Vector3.Lerp(start, over, Mathf.SmoothStep(0f, 1f, t / HatchApproachSeconds)));
            if (controller != null) controller.LookAt(lookPoint, 1f - Mathf.Exp(-7f * Time.deltaTime));
            yield return null;
        }

        // The blackout starts here and covers the last steps toward the stairs.
        if (controller != null) controller.MovementLocked = true;
        _outcome.FloorCleared();

        Vector3 end = over + (over - start).normalized * 0.6f;
        t = 0f;
        while (t < HatchDescendSeconds)
        {
            t += Time.deltaTime;
            MovePlayer(characterController, Vector3.Lerp(over, end, Mathf.Clamp01(t / HatchDescendSeconds)));
            yield return null;
        }

        Stop();
    }

    /// <summary>A CharacterController overrides transform writes unless it is switched off around them.</summary>
    private void MovePlayer(CharacterController characterController, Vector3 position)
    {
        if (characterController != null) characterController.enabled = false;
        _player.position = position;
        if (characterController != null) characterController.enabled = true;
    }

    private void CompleteEscape()
    {
        DropThroughHatch();
        _outcome.FloorCleared();
        Stop();
    }

    /// <summary>
    /// Hatch geometry, so it lives here rather than in GameOutcome: the player is placed below the
    /// floor over the landing platform.
    /// </summary>
    private void DropThroughHatch()
    {
        SetLidAngle(LidOpenAngle, 0.25f);

        ThirdPersonController controller = _player.GetComponent<ThirdPersonController>();
        CharacterController characterController = _player.GetComponent<CharacterController>();

        // Locked, not disabled: gravity keeps running, so the player drops the last stretch onto the
        // platform under their own weight instead of being teleported onto it.
        if (controller != null) controller.MovementLocked = true;

        if (characterController != null) characterController.enabled = false;
        _player.position = new Vector3(_hatchPosition.x, _hatchPosition.y - dropStartDepth, _hatchPosition.z);
        if (characterController != null) characterController.enabled = true;
    }
}
