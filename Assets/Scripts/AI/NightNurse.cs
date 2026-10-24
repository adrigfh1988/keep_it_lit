using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Sister Agnes ("The Night Nurse"), a non-lethal rule/harasser maze entity (<see cref="EntityKind.NightNurse"/>).
/// Combines:
/// - Concept 1 ("The Night Round" - Anti-Torch Curfew Patrol): walks a methodical round between lit WallLamps and corridor
///   junctions with a rhythmic brass key-ring jingle. Blind in the dark or ambient lamp light, but shining the flashlight
///   beam on her triggers a short warning grace followed by a curfew scream, a 3-second forced torch blackout
///   (<see cref="Flashlight.Blackout"/>), a 35 m Hunter alert, and a high-speed Triage Rush.
/// - Concept 5 ("The Triage Nurse" - No Running / No Noise in the Halls): sprinting, jumping, stepping on a noisy surface,
///   or triggering a <see cref="MazeNoise"/> within her discipline radius causes her to snap into a 4.6 m/s Triage Rush.
///   Catching an unhidden player executes a non-lethal 2.5 s Triage Pin (<see cref="MazeEntity.PinPlayer"/> +
///   <see cref="MazeEntity.ShakeView"/> + <see cref="MazeEntity.PulseDread"/> + 3 s torch blackout + Hunter alert),
///   followed by a 12 s post-pin retreat cooldown so she never chain-pins. Walking or crouching past her with the torch
///   off or aimed away is completely safe ("Good patient.").
/// </summary>
public class NightNurse : MazeEntity
{
    /// <summary>Current behaviour state. Public so smoke tests and DebugMap can inspect it.</summary>
    public enum Mode { Patrol, Warn, Scream, Rush, Pin, Cooldown }

    /// <summary>
    /// Campaign-scoped flag: true once Sister Agnes has been rolled/spawned on a floor in this campaign (or DESCENT run).
    /// Saved in <see cref="SaveData.nightNurseSeen"/> and checked by <see cref="EntityRoster.For"/> so she is guaranteed
    /// by Floor 4 / Depth 4 if Floors 2-3 missed their roll.
    /// </summary>
    public static bool SeenThisCampaign { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();

    public static void ResetCampaign()
    {
        SeenThisCampaign = false;
    }

    public static void CaptureTo(SaveData d)
    {
        if (d != null) d.nightNurseSeen = SeenThisCampaign;
    }

    public static void RestoreFrom(SaveData d)
    {
        SeenThisCampaign = d != null && d.nightNurseSeen;
    }

    [Header("Movement & Patrol")]
    [Tooltip("Methodical ward-round walk speed (m/s).")]
    [SerializeField, Min(0.5f)] private float patrolSpeed = 1.85f;
    [Tooltip("High-speed Triage Rush speed when a hall rule is broken (m/s).")]
    [SerializeField, Min(1f)] private float rushSpeed = 4.6f;
    [Tooltip("Post-pin retreat walk speed (m/s).")]
    [SerializeField, Min(0.5f)] private float cooldownSpeed = 2.2f;
    [Tooltip("Initial grace after the run starts before she enforces rules (s).")]
    [SerializeField, Min(0f)] private float runStartGraceSeconds = 10f;

    [Header("Concept 1 - Anti-Torch Curfew")]
    [Tooltip("Maximum distance (m) at which shining the flashlight beam on Agnes triggers her curfew warning.")]
    [SerializeField, Min(2f)] private float beamDetectRange = 15f;
    [Tooltip("Grace window (s) under the wide beam before she slaps the torch out and rushes.")]
    [SerializeField, Min(0.05f)] private float wideBeamGraceSeconds = 0.45f;
    [Tooltip("Grace window (s) under the focused beam before she slaps the torch out and rushes.")]
    [SerializeField, Min(0.05f)] private float focusedBeamGraceSeconds = 0.20f;
    [Tooltip("Seconds the player's flashlight is forced dark when Agnes slaps the beam out or pins the player.")]
    [SerializeField, Min(0.5f)] private float torchBlackoutSeconds = 3f;

    [Header("Concept 5 - Triage Nurse (No Running / No Noise)")]
    [Tooltip("Discipline radius (m) with clear line-of-sight where sprinting/jumping/noisy floors trigger an immediate Rush.")]
    [SerializeField, Min(2f)] private float disciplineSightRange = 12f;
    [Tooltip("Discipline radius (m) around a corner / through a doorway where sprinting or noisy surfaces trigger a Rush.")]
    [SerializeField, Min(2f)] private float disciplineCornerRange = 8f;
    [Tooltip("Maximum distance (m) at which a world MazeNoise event (shattered bottle, bell, etc.) triggers a Rush.")]
    [SerializeField, Min(2f)] private float worldNoiseRushRange = 11f;
    [Tooltip("Player noise radius threshold that counts as sprinting/running in the halls (walk is ~5 m, sprint is ~15 m).")]
    [SerializeField, Min(5f)] private float sprintNoiseThreshold = 9.5f;

    [Header("Rush, Pin & Cooldown")]
    [Tooltip("Scream telegraph wind-up duration before the Rush begins (s).")]
    [SerializeField, Min(0.1f)] private float screamWindupSeconds = 0.45f;
    [Tooltip("Maximum duration of a Triage Rush before she gives up and returns to patrol (s).")]
    [SerializeField, Min(1f)] private float rushMaxSeconds = 5.5f;
    [Tooltip("Flat distance (m) within which a Rush catches and pins an unhidden player.")]
    [SerializeField, Min(0.5f)] private float pinRadius = 1.15f;
    [Tooltip("Duration of the non-lethal Triage Pin (s).")]
    [SerializeField, Min(0.5f)] private float pinSeconds = 2.5f;
    [Tooltip("Post-pin retreat cooldown during which Agnes walks away and ignores the player (s).")]
    [SerializeField, Min(3f)] private float postPinCooldownSeconds = 12f;
    [Tooltip("Radius (m) of the alert sent to the Hunter when Agnes screams or pins the player.")]
    [SerializeField, Min(5f)] private float hunterAlertRadius = 35f;
    [Tooltip("Minimum seconds between Hunter alerts so Agnes never spams the Hunter continuously.")]
    [SerializeField, Min(5f)] private float hunterAlertCooldown = 20f;

    [Header("Audio & Voice")]
    [Tooltip("Audible range (m) of Agnes's rhythmic brass key-ring jingle.")]
    [SerializeField, Min(3f)] private float keyJingleRange = 14f;
    [Tooltip("Interval (s) between key-ring jingles while patrolling.")]
    [SerializeField, Min(0.3f)] private float patrolJingleInterval = 1.7f;
    [Tooltip("Interval (s) between key-ring jingles while rushing.")]
    [SerializeField, Min(0.15f)] private float rushJingleInterval = 0.42f;
    [Tooltip("Distance (m) within which walking/crouching past her with the torch off triggers a calm 'Good patient' line.")]
    [SerializeField, Min(1.5f)] private float politePassRange = 3.8f;
    [Tooltip("Cooldown (s) between calm 'Good patient' pass lines.")]
    [SerializeField, Min(4f)] private float politePassCooldown = 15f;
    [Tooltip("Looping frantic chase music track while Agnes is screaming, rushing or pinning the player (empty = GameAudio.Library.chaseMusic).")]
    [SerializeField] private AudioClip franticChaseClip;
    [SerializeField, Range(0f, 1f)] private float franticChaseMaxVolume = 0.42f;

    [Header("Animator & Locomotion (Hunter-Matched, Animation/Descent/NightNurse.controller)")]
    [SerializeField] private string locomotionState = "Locomotion";
    [SerializeField] private string patrolState = "Patrol";
    [SerializeField] private string warnState = "Warn";
    [SerializeField] private string screamState = "Scream";
    [SerializeField] private string rushState = "Rush";
    [SerializeField] private string pinState = "Pin";
    [Tooltip("Ground speed (m/s) the walk and run clips cover at normal playback, measured from stance foot travel by InteractableKitBuilder. The clip in use plays at body speed / this (MotionSpeed) so the feet keep up instead of sliding.")]
    [SerializeField] private float locomotionWalkSpeed = 1.94f;
    [SerializeField] private float locomotionRunSpeed = 5.33f;
    [Tooltip("Cap on how much faster than recorded a walk/run clip may play to keep up with the agent.")]
    [SerializeField] private float maxLocomotionPlayback = 2f;
    [Tooltip("The walk clip plays up to this much faster than recorded before the run clip takes over.")]
    [SerializeField] private float maxWalkPlayback = 1.75f;
    [SerializeField] private float animationBlendRate = 6f;
    [Tooltip("NavMeshAgent.acceleration - how hard she leans into a change of speed, matching AIFollower.")]
    [SerializeField] private float acceleration = 6f;
    [Tooltip("How fast MoveSpeedTowards ramps the agent's target speed (m/s^2), matching AIFollower.")]
    [SerializeField] private float speedRampRate = 14f;
    [Tooltip("Turn rate while rushing (deg/s), matching AIFollower.turnSpeed.")]
    [SerializeField] private float turnSpeedRush = 540f;
    [Tooltip("Turn rate while moving on patrol/cooldown (deg/s), matching AIFollower.turnSpeedMoving.")]
    [SerializeField] private float turnSpeedMoving = 200f;
    [Tooltip("Turn rate while stationary (deg/s), matching AIFollower.turnSpeedStationary.")]
    [SerializeField] private float turnSpeedStationary = 120f;

    private const float ChestHeight = 1.25f;
    private const float EyeHeight = 1.6f;
    private const float PinMaxVerticalDelta = 1.6f;
    private const float ArriveDistance = 0.75f;
    private const float RepathInterval = 0.25f;
    private const float SnapRadius = 2f;
    private const float StateFadeSeconds = 0.18f;

    private static readonly string[] WarnLines =
    {
        "Mind that lamp, dear.",
        "Curfew is six o'clock, patient.",
        "Put that light out at once!"
    };

    private static readonly string[] BeamShoutLines =
    {
        "LIGHTS OUT, PATIENT!",
        "NO TORCHES AFTER CURFEW!",
        "EYES DOWN, LIGHTS OUT!"
    };

    private static readonly string[] NoiseShoutLines =
    {
        "NO RUNNING IN THE HALLS!",
        "QUIET ON THE WARD!",
        "WALKING FEET, PATIENT!"
    };

    private static readonly string[] PinLines =
    {
        "Back to bed by six, dear! Orderly—this corridor!",
        "Hold still! Let me check your chart, patient!",
        "Caught out of bed! The Orderly will see to you!"
    };

    private static readonly string[] PolitePassLines =
    {
        "Wipe your feet, dear.",
        "Good patient. Keep to the dark.",
        "Quiet steps. Very good, dear."
    };

    private NavMeshAgent _agent;
    private Animator _animator;
    private NurseAgnesPhysics _physics;
    private Renderer[] _renderers;
    private Transform _body;
    private Transform _head;
    private readonly EntityGaitAnimator _locomotion = new EntityGaitAnimator();
    private readonly EntityVoiceAndMusic _voiceMusic = new EntityVoiceAndMusic();
    private bool _configured;
    private bool _subscribed;

    private Mode _mode = Mode.Patrol;
    private readonly List<Vector3> _patrolPoints = new List<Vector3>();
    private int _patrolIndex;
    private Vector3 _target;
    private bool _rushFromBeam;

    private float _beamExposure;
    private float _stateUntil;
    private float _dormantUntil;
    private float _nextRepathAt;
    private float _nextJingleAt;
    private float _nextHunterAlertAt = -999f;
    private float _nextPoliteLineAt = -999f;
    private float _nextWarnLineAt = -999f;

    private const float MaxGroundOffset = 0.20f;

    public override EntityKind Kind => EntityKind.NightNurse;

    public override bool IsKiller => false;

    public override char DebugGlyph => 'n';

    /// <summary>Current state of Sister Agnes.</summary>
    public Mode CurrentMode => _mode;

    /// <summary>World-space chest point tested against the player's flashlight cone.</summary>
    public Vector3 ChestPosition => transform.position + Vector3.up * ChestHeight;

    /// <summary>World-space head position for <see cref="MazeEntity.PinPlayer"/> and speech bubbles.</summary>
    public Vector3 HeadPosition => _head != null ? _head.position : transform.position + Vector3.up * EyeHeight;

    public override void Configure(MazeGenerator maze, Transform player, AIFollower hunter)
    {
        BindSystems(maze, player, hunter);
        SeenThisCampaign = true;

        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();
        _physics = GetComponentInChildren<NurseAgnesPhysics>();
        if (_agent == null || _animator == null)
        {
            Debug.LogWarning("NightNurse: needs a NavMeshAgent and an Animator - staying inert.", this);
            return;
        }

        _body = _animator.transform;
        _animator.fireEvents = false;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (_animator.isHuman) _head = _animator.GetBoneTransform(HumanBodyBones.Head);
        if (_head == null) _head = EntityGaitAnimator.FindDeepChild(_body, "mixamorig:Head");

        EntityGaitAnimator.SyncCosmeticsFromHub(_body, _animator, HubNpc.Who.Agnes, "Body_NurseAgnes");

        _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in _renderers)
        {
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        if (_physics != null) _physics.BindBones();

        BuildPatrolRoute();
        BuildSpeechBubble();
        BuildAudioSources();

        _agent.speed = patrolSpeed;
        _agent.acceleration = acceleration;
        _agent.updateRotation = false;
        _locomotion.Bind(_animator);

        DoorBreakers.Register(transform, _agent, true, null);
        _configured = true;
        Subscribe();
        SetMode(Mode.Patrol, force: true);
        _nextJingleAt = Time.time + Random.Range(0.4f, patrolJingleInterval);
    }

    private void OnEnable() => Subscribe();

    private void OnDisable()
    {
        if (!_subscribed) return;
        _subscribed = false;
        MazeNoise.Emitted -= HandleMazeNoise;
    }

    private void OnDestroy()
    {
        DoorBreakers.Unregister(transform);
        _voiceMusic.DestroyBubble();
    }

    private void Subscribe()
    {
        if (_subscribed || !_configured) return;
        _subscribed = true;
        MazeNoise.Emitted += HandleMazeNoise;
    }

    public override void WarpAway(float dormantSeconds)
    {
        if (!_configured || Maze == null || Player == null) return;

        _dormantUntil = Time.time + dormantSeconds;
        Vector3 spot = Maze.FarthestCellCenterFrom(Player.position);
        if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 3f, NavMesh.AllAreas)) spot = hit.position;
        _agent.Warp(spot);
        _target = spot;
        _beamExposure = 0f;
        SetMode(Mode.Patrol, force: true);
        LogEvent($"warped away to {spot} for {dormantSeconds:0} s");
    }

    private void Update()
    {
        if (!_configured || !_agent.isOnNavMesh) return;

        if (!CanAct || Time.time < _dormantUntil)
        {
            _agent.isStopped = true;
            _locomotion.PauseToIdle(locomotionState, patrolState, rushState, warnState);
            _physics?.SetSpeaking(false);
            UpdateChaseMusic();
            return;
        }

        bool inStartGrace = Time.time - GameFlow.RunStartTime < runStartGraceSeconds;

        switch (_mode)
        {
            case Mode.Patrol:
                TickPatrol(inStartGrace);
                break;
            case Mode.Warn:
                TickWarn();
                break;
            case Mode.Scream:
                TickScream();
                break;
            case Mode.Rush:
                TickRush();
                break;
            case Mode.Pin:
                TickPin();
                break;
            case Mode.Cooldown:
                TickCooldown();
                break;
        }

        UpdateLocomotionAndMouth();
        UpdateKeyJingle();
        UpdateChaseMusic();
    }

    private void LateUpdate()
    {
        if (!_configured || _body == null) return;
        bool lookAtPlayer = _mode == Mode.Warn || _mode == Mode.Scream || _mode == Mode.Rush || _mode == Mode.Pin
                            || (_mode == Mode.Patrol && FlatDistanceToPlayer(transform.position) <= 6f);
        bool fastTrack = _mode == Mode.Warn || _mode == Mode.Scream || _mode == Mode.Rush;
        _locomotion.UpdateBodyGroundAndHeadTrack(transform, _body, _head, Maze, Player, lookAtPlayer, fastTrack, MaxGroundOffset);
    }

    // ---------------------------------------------------------------- Mode ticks

    private void TickPatrol(bool inStartGrace)
    {
        _agent.isStopped = false;
        MoveSpeedTowards(patrolSpeed);
        UpdateRotation(CurrentHeading());
        _physics?.SetFingerCurl(0.1f, 0.1f);

        if (!_agent.pathPending && (_agent.remainingDistance <= ArriveDistance || !_agent.hasPath || _agent.pathStatus == NavMeshPathStatus.PathInvalid))
        {
            AdvancePatrolWaypoint();
        }

        if (inStartGrace) return;

        if (CheckNoiseViolation(out Vector3 noiseSpot))
        {
            StartRush(noiseSpot, fromBeam: false);
            return;
        }

        if (IsIlluminatedByPlayerTorch())
        {
            _beamExposure = Time.deltaTime;
            SetMode(Mode.Warn);
            return;
        }

        CheckPolitePass();
    }

    private void TickWarn()
    {
        _agent.isStopped = true;
        _physics?.SetFingerCurl(0.55f, 0.55f);
        if (Player != null) UpdateRotation(Player.position - transform.position);

        if (CheckNoiseViolation(out Vector3 noiseSpot))
        {
            StartRush(noiseSpot, fromBeam: false);
            return;
        }

        if (IsIlluminatedByPlayerTorch())
        {
            float focusBlend = Torch != null ? Torch.FocusBlend : 0f;
            float grace = Mathf.Lerp(wideBeamGraceSeconds, focusedBeamGraceSeconds, focusBlend);
            _beamExposure += Time.deltaTime;
            if (_beamExposure >= grace)
            {
                StartRush(Player != null ? Player.position : transform.position, fromBeam: true);
            }
            return;
        }

        _beamExposure = Mathf.MoveTowards(_beamExposure, 0f, Time.deltaTime * 2f);
        if (_beamExposure <= 0.001f)
        {
            SetMode(Mode.Patrol);
        }
    }

    private void TickScream()
    {
        _agent.isStopped = true;
        _physics?.SetFingerCurl(1f, 1f);
        if (Player != null) UpdateRotation(Player.position - transform.position);

        if (Time.time >= _stateUntil)
        {
            SetMode(Mode.Rush);
        }
    }

    private void TickRush()
    {
        _agent.isStopped = false;
        MoveSpeedTowards(rushSpeed);
        UpdateRotation(CurrentHeading());
        _physics?.SetFingerCurl(1f, 1f);

        if (Player != null && Stealth != null && !Stealth.Hidden)
        {
            Vector3 from = transform.position + Vector3.up * EyeHeight;
            Vector3 to = Player.position + Vector3.up * 1f;
            if (HasClearLine(from, to, null) || FlatDistanceToPlayer(transform.position) <= disciplineCornerRange)
            {
                _target = Player.position;
            }
        }

        if (Time.time >= _nextRepathAt)
        {
            GoTo(_target);
        }

        if (TryPinPlayer()) return;

        bool arrived = !_agent.pathPending && _agent.remainingDistance <= ArriveDistance * 0.6f;
        if (arrived || Time.time >= _stateUntil)
        {
            SetMode(Mode.Patrol);
        }
    }

    private void TickPin()
    {
        _agent.isStopped = true;
        _physics?.SetFingerCurl(1f, 1f);
        if (Player != null) UpdateRotation(Player.position - transform.position);

        if (Time.time >= _stateUntil)
        {
            SetMode(Mode.Cooldown);
        }
    }

    private void TickCooldown()
    {
        _agent.isStopped = false;
        MoveSpeedTowards(cooldownSpeed);
        UpdateRotation(CurrentHeading());
        _physics?.SetFingerCurl(0.15f, 0.15f);

        if (!_agent.pathPending && (_agent.remainingDistance <= ArriveDistance || !_agent.hasPath))
        {
            AdvancePatrolWaypoint();
        }

        if (Time.time >= _stateUntil)
        {
            SetMode(Mode.Patrol);
        }
    }

    // ---------------------------------------------------------------- Rule checks & transitions

    private bool IsIlluminatedByPlayerTorch()
    {
        if (Torch == null || Player == null || (Stealth != null && Stealth.Hidden)) return false;
        if (FlatDistanceToPlayer(transform.position) > beamDetectRange) return false;
        return Perception.IsIlluminatedByTorch(Torch, ChestPosition, transform)
            || Perception.IsIlluminatedByTorch(Torch, HeadPosition, transform);
    }

    private bool CheckNoiseViolation(out Vector3 violationPosition)
    {
        violationPosition = Player != null ? Player.position : transform.position;
        if (Player == null || Stealth == null || Stealth.Hidden) return false;

        float dist = FlatDistanceToPlayer(transform.position);
        if (dist > disciplineSightRange) return false;

        bool sprinting = Stealth.NoiseRadius >= sprintNoiseThreshold;
        bool jumping = Controller != null && !Controller.Grounded;
        bool noisyFloor = Stealth.SurfaceNoiseMultiplier > 1.05f && Stealth.NoiseRadius > 1.5f;
        if (!sprinting && !jumping && !noisyFloor) return false;

        if (dist <= disciplineCornerRange) return true;

        Vector3 from = transform.position + Vector3.up * EyeHeight;
        Vector3 to = Player.position + Vector3.up * 1f;
        return HasClearLine(from, to, null);
    }

    private void HandleMazeNoise(Vector3 position, float radius)
    {
        if (!_configured || !CanAct || Time.time < _dormantUntil) return;
        if (Time.time - GameFlow.RunStartTime < runStartGraceSeconds) return;
        if (_mode == Mode.Scream || _mode == Mode.Rush || _mode == Mode.Pin || _mode == Mode.Cooldown) return;

        Vector3 d = position - transform.position;
        d.y = 0f;
        float maxDist = Mathf.Min(worldNoiseRushRange, Mathf.Max(disciplineCornerRange, radius));
        if (d.magnitude <= maxDist)
        {
            StartRush(position, fromBeam: false);
        }
    }

    private void StartRush(Vector3 targetPosition, bool fromBeam)
    {
        _target = targetPosition;
        _rushFromBeam = fromBeam;
        _beamExposure = 0f;

        if (fromBeam && Torch != null)
        {
            Torch.Blackout(torchBlackoutSeconds);
            LogEvent($"curfew violation -> torch blackout ({torchBlackoutSeconds:0.0} s)");
        }

        PulseDread();
        AlertHunter(transform.position, hunterAlertRadius);
        PlayFirst(HeadPosition, 0.9f, 1.08f, GameAudio.Library.farScream, GameAudio.Library.keyJingle);
        Speak(fromBeam ? BeamShoutLines : NoiseShoutLines, SpeechBubble.Style.Shout);

        _stateUntil = Time.time + screamWindupSeconds;
        SetMode(Mode.Scream, force: true);
    }

    private bool TryPinPlayer()
    {
        if (Player == null || Stealth == null || Stealth.Hidden || !CanAct) return false;
        if (Controller != null && Controller.MovementLocked) return false;

        Vector3 d = Player.position - transform.position;
        if (Mathf.Abs(d.y) > PinMaxVerticalDelta) return false;
        d.y = 0f;
        if (d.magnitude > pinRadius) return false;

        _stateUntil = Time.time + pinSeconds;
        SetMode(Mode.Pin, force: true);

        Torch?.Blackout(torchBlackoutSeconds);
        PulseDread();
        AlertHunter(Player.position, hunterAlertRadius, force: true);
        PlayFirst(HeadPosition, 0.95f, 1f, GameAudio.Library.keyJingle, GameAudio.Library.caught);
        Speak(PinLines, SpeechBubble.Style.Shout);

        StartCoroutine(PinPlayer(HeadPosition, pinSeconds));
        StartCoroutine(ShakeView(0.45f, 7f));
        LogEvent($"pinned player for {pinSeconds:0.0} s");
        return true;
    }

    private void CheckPolitePass()
    {
        if (Player == null || Stealth == null || Stealth.Hidden) return;
        if (Time.time < _nextPoliteLineAt) return;

        float dist = FlatDistanceToPlayer(transform.position);
        if (dist > politePassRange) return;

        _nextPoliteLineAt = Time.time + politePassCooldown;
        Speak(PolitePassLines, SpeechBubble.Style.Normal);
        if (GameAudio.Library.whisperEcho.Has)
        {
            GameAudio.PlayAt(GameAudio.Library.whisperEcho, HeadPosition, 0.45f, 1.05f);
        }
        LogEvent("polite pass acknowledged");
    }

    private void AlertHunter(Vector3 position, float radius, bool force = false)
    {
        if (Hunter == null) return;
        if (!force && Time.time < _nextHunterAlertAt) return;
        _nextHunterAlertAt = Time.time + hunterAlertCooldown;
        Hunter.HearNoise(position, radius);
    }

    private void SetMode(Mode mode, bool force = false)
    {
        if (!force && _mode == mode) return;

        Mode prev = _mode;
        _mode = mode;

        switch (mode)
        {
            case Mode.Patrol:
                _agent.isStopped = false;
                if (_patrolPoints.Count > 0) GoTo(_patrolPoints[_patrolIndex % _patrolPoints.Count]);
                PlayAnim(_locomotion.HasLocomotionState ? locomotionState : patrolState);
                break;

            case Mode.Warn:
                _agent.isStopped = true;
                _agent.ResetPath();
                PlayAnim(warnState);
                if (Time.time >= _nextWarnLineAt)
                {
                    _nextWarnLineAt = Time.time + 4f;
                    Speak(WarnLines, SpeechBubble.Style.Normal);
                    if (GameAudio.Library.keyJingle.Has) GameAudio.PlayAt(GameAudio.Library.keyJingle, transform.position + Vector3.up, 0.7f, 1.15f);
                }
                break;

            case Mode.Scream:
                _agent.isStopped = true;
                _agent.ResetPath();
                PlayAnim(screamState);
                break;

            case Mode.Rush:
                _agent.isStopped = false;
                _stateUntil = Time.time + rushMaxSeconds;
                GoTo(_target);
                PlayAnim(_locomotion.HasLocomotionState ? locomotionState : rushState);
                break;

            case Mode.Pin:
                _agent.isStopped = true;
                _agent.ResetPath();
                PlayAnim(pinState);
                break;

            case Mode.Cooldown:
                _agent.isStopped = false;
                _stateUntil = Time.time + postPinCooldownSeconds;
                if (Maze != null && Player != null)
                {
                    _target = Maze.FarthestCellCenterFrom(Player.position);
                    GoTo(_target);
                }
                PlayAnim(_locomotion.HasLocomotionState ? locomotionState : patrolState);
                break;
        }

        if (prev != mode) LogEvent($"{prev} -> {mode}");
    }

    // ---------------------------------------------------------------- Patrol & Navigation

    private void BuildPatrolRoute()
    {
        _patrolPoints.Clear();
        if (Maze == null) return;

        if (Maze.Lamps != null)
        {
            for (int i = 0; i < Maze.Lamps.Count; i++)
            {
                WallLamp lamp = Maze.Lamps[i];
                if (lamp == null) continue;
                Vector3 pos = lamp.transform.position;
                pos.y = Maze.FloorTopY;
                if (NavMesh.SamplePosition(pos, out NavMeshHit hit, Maze.CellSize * 0.6f, NavMesh.AllAreas))
                {
                    _patrolPoints.Add(hit.position);
                }
            }
        }

        if (_patrolPoints.Count < 4 && Maze.CellCenters != null)
        {
            int step = Mathf.Max(1, Maze.CellCenters.Count / 8);
            for (int i = 0; i < Maze.CellCenters.Count; i += step)
            {
                _patrolPoints.Add(Maze.CellCenters[i]);
            }
        }

        if (_patrolPoints.Count == 0)
        {
            _patrolPoints.Add(transform.position);
        }

        _patrolIndex = 0;
    }

    private void AdvancePatrolWaypoint()
    {
        if (_patrolPoints.Count == 0) return;
        _patrolIndex = (_patrolIndex + 1) % _patrolPoints.Count;
        _target = _patrolPoints[_patrolIndex];
        GoTo(_target);
    }

    private void GoTo(Vector3 point) => EntityGaitAnimator.GoTo(_agent, point, SnapRadius, RepathInterval, ref _nextRepathAt);

    private void MoveSpeedTowards(float targetSpeed) => EntityGaitAnimator.MoveSpeedTowards(_agent, targetSpeed, speedRampRate);

    private Vector3 CurrentHeading() => EntityGaitAnimator.CurrentHeading(_agent, transform);

    private void UpdateRotation(Vector3 desiredDirection)
    {
        _locomotion.UpdateRotation(transform, _agent, desiredDirection, _mode == Mode.Rush, turnSpeedRush, turnSpeedMoving, turnSpeedStationary);
    }

    // ---------------------------------------------------------------- Audio, Speech Bubble & Cosmetics

    private void UpdateLocomotionAndMouth()
    {
        if (_locomotion.StepPursuerGait(
            _agent,
            _mode == Mode.Rush,
            locomotionWalkSpeed,
            locomotionRunSpeed,
            maxWalkPlayback,
            maxLocomotionPlayback,
            rushSpeed,
            animationBlendRate,
            out bool moving,
            out float v,
            out float walkSpeed,
            out float runSpeed,
            out float playback))
        {
            switch (_mode)
            {
                case Mode.Patrol:
                case Mode.Cooldown:
                    _locomotion.PlayPatrolLocomotion(moving, v, walkSpeed, playback, maxLocomotionPlayback, locomotionState, patrolState, warnState, rushState);
                    break;

                case Mode.Rush:
                    _locomotion.PlayChargeLocomotion(v, runSpeed, playback, maxLocomotionPlayback, locomotionState, patrolState, rushState, warnState);
                    break;

                case Mode.Warn:
                    _locomotion.PlayActionState(warnState, 1f, patrolState, rushState, warnState, screamState, pinState);
                    break;

                case Mode.Scream:
                    _locomotion.PlayActionState(screamState, 1f, patrolState, rushState, warnState, screamState, pinState);
                    break;

                case Mode.Pin:
                    _locomotion.PlayActionState(pinState, 1f, patrolState, rushState, warnState, screamState, pinState);
                    break;
            }
        }

        if (_physics != null)
        {
            if (_mode == Mode.Scream)
            {
                _physics.SetSpeaking(false);
                _physics.SetMouthOpen(0.92f, 0.45f, 0.20f);
            }
            else
            {
                bool speakingNow = _voiceMusic.IsSpeaking;
                _physics.SetSpeaking(speakingNow);
                if (!speakingNow && _physics.mouthOpen > 0.01f)
                {
                    _physics.SetMouthOpen(0f, 0f, 0f);
                }
            }
        }
    }

    private void PlayAnim(string state)
    {
        _locomotion.PlayState(state, patrolState, rushState, warnState, screamState, pinState, null, StateFadeSeconds);
    }

    private void UpdateKeyJingle()
    {
        if (_mode != Mode.Patrol && _mode != Mode.Rush && _mode != Mode.Cooldown) return;
        if (Time.time < _nextJingleAt) return;

        float interval = _mode == Mode.Rush ? rushJingleInterval : patrolJingleInterval;
        _nextJingleAt = Time.time + interval * Random.Range(0.88f, 1.12f);

        if (FlatDistanceToPlayer(transform.position) > keyJingleRange * 1.35f) return;

        AudioSlot slot = GameAudio.Library.keyJingle.Has ? GameAudio.Library.keyJingle : GameAudio.Library.timmyStep;
        if (!slot.Has) return;

        float vol = _mode == Mode.Rush ? 0.78f : 0.52f;
        float pitch = _mode == Mode.Rush ? 1.12f : 0.98f;
        AudioSource src = GameAudio.SpawnAt(slot, transform.position + Vector3.up * 0.95f, vol, pitch, keyJingleRange);
        if (src != null) src.transform.SetParent(transform, true);
    }

    private void BuildSpeechBubble()
    {
        SpeechBubble.Palette nursePalette = new SpeechBubble.Palette
        {
            Fill = new Color(0.92f, 0.91f, 0.88f, 0.92f),
            Outline = new Color(0.14f, 0.16f, 0.20f, 1f),
            Text = new Color(0.12f, 0.13f, 0.16f, 1f),
            Shout = new Color(0.72f, 0.08f, 0.08f, 1f)
        };
        _voiceMusic.BuildSpeechBubble(Hud, nursePalette, "NightNurseSpeechBubble");
    }

    private void BuildAudioSources()
    {
        _voiceMusic.BuildAudioSources(transform, EyeHeight, ChestHeight, franticChaseClip, null, "NurseFranticChaseMusic", createVoiceSource: false);
    }

    private void UpdateChaseMusic()
    {
        bool activeChase = (_mode == Mode.Scream || _mode == Mode.Rush || _mode == Mode.Pin)
                           && !GameOutcome.IsOver
                           && (CanAct || _mode == Mode.Pin);
        _voiceMusic.UpdateChaseMusic(activeChase, franticChaseClip, franticChaseMaxVolume);
    }

    private void Speak(string[] pool, SpeechBubble.Style style)
    {
        _voiceMusic.Speak(
            FlatDistanceToPlayer(transform.position),
            pool,
            null,
            style,
            () => HeadPosition + Vector3.up * 0.38f,
            shoutMaxRange: 18f,
            normalMaxRange: 18f);
    }
}
