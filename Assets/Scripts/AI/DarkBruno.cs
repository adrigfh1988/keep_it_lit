using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Cook Bruno ("The Butcher" / Dark Bruno), a non-lethal territorial maze entity (<see cref="EntityKind.Butcher"/>).
/// Implements "The Chopping Block" with a 2-minute room rotation:
/// - Claims 3-5 Chopping Block stations across the maze's carved rooms (and fallback junctions/dead-ends).
/// - Stays at his current station in <see cref="Mode.Chopping"/> for <see cref="stationRotateSeconds"/> (120 s = 2 minutes),
///   hacking rhythmically onto his wooden butcher block with his meat cleaver (audible 14 m away through walls).
/// - Every 2 minutes, mutters a rotation line and transitions to <see cref="Mode.Rotating"/>, marching along the NavMesh
///   (clanging his belt-hung soup ladle) to the next Chopping Block station.
/// - While chopping, his vision is narrowed to a front cone (<see cref="chopSightHalfAngle"/>) so the player can sneak behind
///   his back or crouch past room baffles, or lure him out of the room with a thrown bottle/can (<see cref="Mode.Investigate"/>).
/// - Intruding openly into his station, sprinting/making noise near him, or staring at his face (<see cref="stareGraceSeconds"/>,
///   "I'd sooner not be looked at") triggers <see cref="Mode.Warn"/> -> <see cref="Mode.Roar"/> -> <see cref="Mode.Charge"/>.
/// - Registered in <see cref="DoorBreakers"/> with <c>instantCleave: true</c> so he cleaves closed <see cref="MazeDoor"/> shutters
///   open in 0.08 s with zero stall. Catching an unhidden player executes a non-lethal 2.4 s Butcher Pin followed by a 12 s
///   cooldown rotation to the next station.
/// </summary>
public class DarkBruno : MazeEntity
{
    /// <summary>Current behaviour state of Dark Bruno. Public so smoke tests and DebugMap can inspect it.</summary>
    public enum Mode { Chopping, Rotating, Investigate, Warn, Roar, Charge, Pin, Cooldown }

    /// <summary>
    /// Campaign-scoped flag: true once Dark Bruno has appeared on a floor in this campaign (or DESCENT run).
    /// Saved in <see cref="SaveData.darkBrunoSeen"/>.
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
        if (d != null) d.darkBrunoSeen = SeenThisCampaign;
    }

    public static void RestoreFrom(SaveData d)
    {
        SeenThisCampaign = d != null && d.darkBrunoSeen;
    }

    [Header("The Chopping Block & 2-Minute Rotation")]
    [Tooltip("Seconds Bruno works at one Chopping Block station before rotating to the next room (120 s = 2 minutes).")]
    [SerializeField, Min(10f)] private float stationRotateSeconds = 120f;
    [Tooltip("Interval (s) between cleaver hacks onto the chopping block while in Mode.Chopping.")]
    [SerializeField, Min(0.4f)] private float chopInterval = 1.35f;
    [Tooltip("Audible range (m) of the cleaver thwack on the chopping block.")]
    [SerializeField, Min(4f)] private float chopAudibleRange = 14f;
    [Tooltip("Duration (s) Bruno inspects a thrown bottle/can distraction before returning to his station.")]
    [SerializeField, Min(2f)] private float investigateSeconds = 9f;

    [Header("Movement & Speeds")]
    [Tooltip("Heavy butcher walk speed when rotating between stations or investigating (m/s).")]
    [SerializeField, Min(0.5f)] private float patrolSpeed = 1.95f;
    [Tooltip("High-speed Butcher Charge speed when an intruder or starer triggers him (m/s).")]
    [SerializeField, Min(1f)] private float chargeSpeed = 4.75f;
    [Tooltip("Post-pin cooldown walk speed while rotating to the next station (m/s).")]
    [SerializeField, Min(0.5f)] private float cooldownSpeed = 2.25f;
    [Tooltip("Initial grace after the run starts before Bruno reacts to the player (s).")]
    [SerializeField, Min(0f)] private float runStartGraceSeconds = 10f;

    [Header("Detection: Kitchen Intrusion, Stare & Noise")]
    [Tooltip("Half-angle (deg) of Bruno's narrow front vision cone while focused on his chopping block.")]
    [SerializeField, Range(20f, 85f)] private float chopSightHalfAngle = 42f;
    [Tooltip("Half-angle (deg) of Bruno's front vision cone while walking between stations.")]
    [SerializeField, Range(25f, 90f)] private float rotateSightHalfAngle = 58f;
    [Tooltip("Distance (m) within which walking upright in front of Bruno at his station triggers an intrusion warning.")]
    [SerializeField, Min(2f)] private float roomIntrusionRadius = 6.0f;
    [Tooltip("Maximum distance (m) at which staring at Bruno's face ('I'd sooner not be looked at') builds stare exposure.")]
    [SerializeField, Min(3f)] private float stareDetectRange = 12f;
    [Tooltip("Seconds of continuous staring at Bruno before he snaps into a Roar and Charge.")]
    [SerializeField, Min(0.2f)] private float stareGraceSeconds = 1.1f;
    [Tooltip("Radius (m) within which sprinting, jumping, or stepping on noisy surfaces alerts Bruno.")]
    [SerializeField, Min(3f)] private float noiseDisciplineRange = 9.5f;
    [Tooltip("Maximum distance (m) at which a thrown bottle/can lures Bruno out of his room to investigate.")]
    [SerializeField, Min(4f)] private float throwLureRange = 15f;
    [Tooltip("Player noise radius threshold that counts as sprinting/running (walk is ~5 m, sprint is ~15 m).")]
    [SerializeField, Min(5f)] private float sprintNoiseThreshold = 9.5f;

    [Header("Warn, Roar, Charge & Butcher Pin")]
    [Tooltip("Duration (s) of the Warn pause before escalating if the player stays exposed.")]
    [SerializeField, Min(0.2f)] private float warnDurationSeconds = 0.65f;
    [Tooltip("Roar telegraph wind-up duration before the Butcher Charge begins (s).")]
    [SerializeField, Min(0.15f)] private float roarWindupSeconds = 0.48f;
    [Tooltip("Maximum duration of a Butcher Charge before he breaks off and rotates to the next station (s).")]
    [SerializeField, Min(1.5f)] private float chargeMaxSeconds = 6.0f;
    [Tooltip("Flat distance (m) within which a Charge catches and pins an unhidden player.")]
    [SerializeField, Min(0.5f)] private float pinRadius = 1.20f;
    [Tooltip("Duration of the non-lethal Butcher Pin (s).")]
    [SerializeField, Min(0.5f)] private float pinSeconds = 2.4f;
    [Tooltip("Post-pin cooldown during which Bruno rotates to his next station and ignores the player (s).")]
    [SerializeField, Min(3f)] private float postPinCooldownSeconds = 12f;
    [Tooltip("Radius (m) of the alert sent to the Hunter when Bruno roars or pins the player.")]
    [SerializeField, Min(5f)] private float hunterAlertRadius = 35f;
    [Tooltip("Minimum seconds between Hunter alerts so Bruno never spams the Hunter continuously.")]
    [SerializeField, Min(5f)] private float hunterAlertCooldown = 20f;

    [Header("Ladle Clank Audio")]
    [Tooltip("Audible range (m) of Bruno's belt-hung iron soup ladle clanking while walking/charging.")]
    [SerializeField, Min(3f)] private float ladleClankRange = 14f;
    [Tooltip("Interval (s) between ladle clanks while rotating between rooms.")]
    [SerializeField, Min(0.3f)] private float patrolClankInterval = 1.65f;
    [Tooltip("Interval (s) between ladle clanks while charging.")]
    [SerializeField, Min(0.15f)] private float chargeClankInterval = 0.40f;

    [Header("Voice Lines & Frantic Chase Music (Assets/SourceFiles/Audio/Generated/Voices/Bruno)")]
    [SerializeField] private AudioClip[] chopMutterClips = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] rotateClips = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] investigateClips = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] warnClips = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] roarIntrusionClips = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] roarStareClips = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip[] pinClips = System.Array.Empty<AudioClip>();
    [SerializeField] private AudioClip franticChaseClip;
    [SerializeField, Range(0f, 1f)] private float franticChaseMaxVolume = 0.42f;

    [Header("Animator & Locomotion (Hunter-Matched, Animation/Descent/DarkBruno.controller)")]
    [SerializeField] private string locomotionState = "Locomotion";
    [SerializeField] private string patrolState = "Patrol";
    [SerializeField] private string chopState = "Chop";
    [SerializeField] private string warnState = "Warn";
    [SerializeField] private string roarState = "Roar";
    [SerializeField] private string chargeState = "Rush";
    [SerializeField] private string pinState = "Pin";
    [Tooltip("Ground speed (m/s) the walk and run clips cover at normal playback, measured from stance foot travel by InteractableKitBuilder.")]
    [SerializeField] private float locomotionWalkSpeed = 2.16f;
    [SerializeField] private float locomotionRunSpeed = 5.92f;
    [SerializeField] private float maxLocomotionPlayback = 2f;
    [SerializeField] private float maxWalkPlayback = 1.75f;
    [SerializeField] private float animationBlendRate = 6f;
    [SerializeField] private float acceleration = 6f;
    [SerializeField] private float speedRampRate = 14f;
    [SerializeField] private float turnSpeedCharge = 540f;
    [SerializeField] private float turnSpeedMoving = 200f;
    [SerializeField] private float turnSpeedStationary = 140f;

    private const float ChestHeight = 1.32f;
    private const float EyeHeight = 1.72f;
    private const float PinMaxVerticalDelta = 1.6f;
    private const float ArriveDistance = 0.80f;
    private const float RepathInterval = 0.25f;
    private const float SnapRadius = 2.2f;
    private const float StateFadeSeconds = 0.18f;
    private const float MaxGroundOffset = 0.20f;

    private static readonly string[] ChopMutterLines =
    {
        "Thick bone on this one... hold still on the slab.",
        "Six portions for the ward. One for the pass.",
        "Salt on the sill... keep the flies off the cut."
    };

    private static readonly string[] RotateLines =
    {
        "Done with this cut. Next table.",
        "Meat's gone cold here. Moving the block.",
        "Time to check the other pass."
    };

    private static readonly string[] InvestigateLines =
    {
        "Who dropped a pan on my floor?!",
        "Rats in the pantry again...",
        "Don't touch the dishes!"
    };

    private static readonly string[] WarnLines =
    {
        "Who's in my kitchen?! Step back from the block!",
        "I said don't look at me!",
        "Keep your eyes off the cleaver, boy!"
    };

    private static readonly string[] RoarIntrusionLines =
    {
        "OUT OF MY KITCHEN!",
        "FRESH MEAT ON THE PASS!",
        "NO STEALING FROM THE BLOCK!"
    };

    private static readonly string[] RoarStareLines =
    {
        "I TOLD YOU NOT TO LOOK AT ME!",
        "STOP STARING AT MY HANDS!",
        "EYES OFF THE BUTCHER!"
    };

    private static readonly string[] PinLines =
    {
        "Hold still on the block! Time to dress this cut!",
        "Right through the bone! Back in the pot!",
        "Caught you by the pass! Let's weigh what's left!"
    };

    private NavMeshAgent _agent;
    private Animator _animator;
    private CookBrunoPhysics _physics;
    private Renderer[] _renderers;
    private Transform _body;
    private Transform _head;
    private readonly EntityGaitAnimator _locomotion = new EntityGaitAnimator();
    private readonly EntityVoiceAndMusic _voiceMusic = new EntityVoiceAndMusic();
    private bool _configured;
    private bool _subscribed;

    private Mode _mode = Mode.Chopping;
    private readonly List<Vector3> _stations = new List<Vector3>();
    private readonly List<Vector3> _stationBlockLookTargets = new List<Vector3>();
    private readonly List<Transform> _spawnedBlocks = new List<Transform>();
    private int _stationIndex;
    private Vector3 _target;
    private bool _roarFromStare;

    private float _nextRotationAt;
    private float _chopTimer;
    private bool _chopStruckThisCycle;
    private float _stareExposure;
    private float _stateUntil;
    private float _dormantUntil;
    private float _nextRepathAt;
    private float _nextClankAt;
    private float _nextMutterAt;
    private float _nextHunterAlertAt = -999f;
    private float _nextWarnLineAt = -999f;

    public override EntityKind Kind => EntityKind.Butcher;

    public override bool IsKiller => true;

    public override char DebugGlyph => 'c';

    /// <summary>Current behaviour state of Dark Bruno.</summary>
    public Mode CurrentMode => _mode;

    /// <summary>Index of the Chopping Block station Bruno currently claims or is rotating toward.</summary>
    public int CurrentStationIndex => _stationIndex;

    /// <summary>Total number of Chopping Block stations Bruno rotates between on this floor.</summary>
    public int StationCount => _stations.Count;

    /// <summary>Seconds remaining before Bruno rotates to his next Chopping Block station.</summary>
    public float TimeUntilNextRotation => Mathf.Max(0f, _nextRotationAt - Time.time);

    /// <summary>World-space chest point tested against the player's flashlight cone and line-of-sight.</summary>
    public Vector3 ChestPosition => transform.position + Vector3.up * ChestHeight;

    /// <summary>World-space head position for <see cref="MazeEntity.PinPlayer"/> and speech bubbles.</summary>
    public Vector3 HeadPosition => _head != null ? _head.position : transform.position + Vector3.up * EyeHeight;

    public override void Configure(MazeGenerator maze, Transform player, AIFollower hunter)
    {
        BindSystems(maze, player, hunter);
        SeenThisCampaign = true;

        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();
        _physics = GetComponentInChildren<CookBrunoPhysics>();
        if (_agent == null || _animator == null)
        {
            Debug.LogWarning("DarkBruno: needs a NavMeshAgent and an Animator - staying inert.", this);
            return;
        }

        _body = _animator.transform;
        _animator.fireEvents = false;
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (_animator.isHuman) _head = _animator.GetBoneTransform(HumanBodyBones.Head);
        if (_head == null) _head = EntityGaitAnimator.FindDeepChild(_body, "mixamorig:Head");

        EntityGaitAnimator.SyncCosmeticsFromHub(_body, _animator, HubNpc.Who.Bruno, "Body_CookBruno");

        _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in _renderers)
        {
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        if (_physics != null)
        {
            _physics.autoSpeakInDialogue = false;
            _physics.BindBones();
        }

        BuildChoppingStations();
        BuildSpeechBubble();
        BuildAudioSources();

        _agent.speed = patrolSpeed;
        _agent.acceleration = acceleration;
        _agent.updateRotation = false;
        _locomotion.Bind(_animator);

        // InstantCleave = true: Dark Bruno cleaves through closed shutters in 0.08 s with zero stall.
        DoorBreakers.Register(transform, _agent, silent: false, stall: null, instantCleave: true);
        _configured = true;
        Subscribe();

        _stationIndex = 0;
        if (_stations.Count > 0)
        {
            _agent.Warp(_stations[0]);
            _target = _stations[0];
        }
        _nextRotationAt = Time.time + stationRotateSeconds;
        _nextMutterAt = Time.time + Random.Range(9f, 18f);
        _nextClankAt = Time.time + Random.Range(0.5f, patrolClankInterval);
        SetMode(Mode.Chopping, force: true);
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
        for (int i = 0; i < _spawnedBlocks.Count; i++)
        {
            if (_spawnedBlocks[i] != null) Destroy(_spawnedBlocks[i].gameObject);
        }
        _spawnedBlocks.Clear();
    }

    private void Subscribe()
    {
        if (_subscribed || !_configured) return;
        _subscribed = true;
        MazeNoise.Emitted += HandleMazeNoise;
    }

    /// <summary>
    /// Immediately advances Dark Bruno to his next Chopping Block station and starts <see cref="Mode.Rotating"/>.
    /// Called automatically every <see cref="stationRotateSeconds"/> (120 s), after a Pin cooldown, or from debug/test evals.
    /// </summary>
    public void ForceRotateToNextStation()
    {
        if (!_configured || _stations.Count <= 1) return;
        _stationIndex = (_stationIndex + 1) % _stations.Count;
        _target = _stations[_stationIndex];
        _nextRotationAt = Time.time + stationRotateSeconds;
        Speak(RotateLines, SpeechBubble.Style.Normal);
        SetMode(Mode.Rotating, force: true);
    }

    public override void WarpAway(float dormantSeconds)
    {
        if (!_configured || Maze == null || Player == null) return;

        _dormantUntil = Time.time + dormantSeconds;
        int bestIdx = _stationIndex;
        float bestDist = -1f;
        for (int i = 0; i < _stations.Count; i++)
        {
            float d = Vector3.Distance(_stations[i], Player.position);
            if (d > bestDist)
            {
                bestDist = d;
                bestIdx = i;
            }
        }
        _stationIndex = bestIdx;
        Vector3 spot = _stations.Count > 0 ? _stations[_stationIndex] : Maze.FarthestCellCenterFrom(Player.position);
        if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 3f, NavMesh.AllAreas)) spot = hit.position;
        _agent.Warp(spot);
        _target = spot;
        _stareExposure = 0f;
        _nextRotationAt = Time.time + stationRotateSeconds;
        SetMode(Mode.Chopping, force: true);
        LogEvent($"warped away to station {_stationIndex} ({spot}) for {dormantSeconds:0} s");
    }

    private void Update()
    {
        if (!_configured || !_agent.isOnNavMesh) return;

        // Keep animating the lethal Butcher catch/cleaver strike through the 1.4 s CaptureSequence until GameOutcome.IsOver.
        if (_mode == Mode.Pin && !GameOutcome.IsOver)
        {
            TickPin();
            UpdateLocomotionAndMouth();
            UpdateChaseMusic();
            return;
        }

        if (!CanAct || Time.time < _dormantUntil)
        {
            _agent.isStopped = true;
            _locomotion.PauseToIdle(locomotionState, patrolState, chargeState, warnState);
            _physics?.SetSpeaking(false);
            _physics?.SetChopping(false);
            UpdateChaseMusic();
            return;
        }

        bool inStartGrace = Time.time - GameFlow.RunStartTime < runStartGraceSeconds;

        switch (_mode)
        {
            case Mode.Chopping:
                TickChopping(inStartGrace);
                break;
            case Mode.Rotating:
                TickRotating(inStartGrace);
                break;
            case Mode.Investigate:
                TickInvestigate(inStartGrace);
                break;
            case Mode.Warn:
                TickWarn();
                break;
            case Mode.Roar:
                TickRoar();
                break;
            case Mode.Charge:
                TickCharge();
                break;
            case Mode.Pin:
                TickPin();
                break;
            case Mode.Cooldown:
                TickCooldown();
                break;
        }

        UpdateLocomotionAndMouth();
        UpdateLadleClank();
        UpdateChaseMusic();
        if (!inStartGrace && Time.time >= _dormantUntil) TryKillPlayer();
    }

    private void LateUpdate()
    {
        if (!_configured || _body == null) return;
        bool lookAtPlayer = _mode == Mode.Warn || _mode == Mode.Roar || _mode == Mode.Charge || _mode == Mode.Pin
                            || (_mode == Mode.Rotating && FlatDistanceToPlayer(transform.position) <= 6.5f);
        bool fastTrack = _mode == Mode.Warn || _mode == Mode.Roar || _mode == Mode.Charge;
        _locomotion.UpdateBodyGroundAndHeadTrack(transform, _body, _head, Maze, Player, lookAtPlayer, fastTrack, MaxGroundOffset);
    }

    // ---------------------------------------------------------------- Mode Ticks

    private void TickChopping(bool inStartGrace)
    {
        _agent.isStopped = true;
        if (_stationIndex >= 0 && _stationIndex < _stationBlockLookTargets.Count)
        {
            UpdateRotation(_stationBlockLookTargets[_stationIndex] - transform.position);
        }

        // Advance rhythmic cleaver chopping cycle and play 3D thwack at strike bottom (phase >= 0.72)
        float prevPhase = _chopTimer / Mathf.Max(0.25f, chopInterval);
        _chopTimer += Time.deltaTime;
        if (_chopTimer >= chopInterval)
        {
            _chopTimer -= chopInterval;
            _chopStruckThisCycle = false;
        }
        float normPhase = _chopTimer / Mathf.Max(0.25f, chopInterval);
        _physics?.SetChopping(true, normPhase);

        if (!_chopStruckThisCycle && normPhase >= 0.72f && prevPhase < 0.72f)
        {
            _chopStruckThisCycle = true;
            PlayChopThwack();
        }

        if (Time.time >= _nextMutterAt)
        {
            _nextMutterAt = Time.time + Random.Range(14f, 24f);
            Speak(ChopMutterLines, SpeechBubble.Style.Normal);
        }

        // 2-Minute (120 s) Station Rotation check
        if (_stations.Count > 1 && Time.time >= _nextRotationAt)
        {
            ForceRotateToNextStation();
            return;
        }

        if (inStartGrace) return;

        if (CheckStareViolation())
        {
            StartRoar(fromStare: true);
            return;
        }

        if (CheckNoiseViolation(out Vector3 noiseSpot))
        {
            _target = noiseSpot;
            StartRoar(fromStare: false);
            return;
        }

        if (CheckStationIntrusion())
        {
            SetMode(Mode.Warn);
        }
    }

    private void TickRotating(bool inStartGrace)
    {
        _agent.isStopped = false;
        _physics?.SetChopping(false);
        MoveSpeedTowards(patrolSpeed);
        UpdateRotation(CurrentHeading());

        if (Time.time >= _nextRepathAt)
        {
            GoTo(_target);
        }

        if (!_agent.pathPending && (_agent.remainingDistance <= ArriveDistance || !_agent.hasPath || _agent.pathStatus == NavMeshPathStatus.PathInvalid))
        {
            _nextRotationAt = Time.time + stationRotateSeconds;
            SetMode(Mode.Chopping);
            return;
        }

        if (inStartGrace) return;

        if (CheckStareViolation())
        {
            StartRoar(fromStare: true);
            return;
        }

        if (CheckNoiseViolation(out Vector3 noiseSpot))
        {
            _target = noiseSpot;
            StartRoar(fromStare: false);
            return;
        }

        if (CheckRotationSightIntrusion())
        {
            SetMode(Mode.Warn);
        }
    }

    private void TickInvestigate(bool inStartGrace)
    {
        _agent.isStopped = false;
        _physics?.SetChopping(false);
        MoveSpeedTowards(patrolSpeed);

        if (!_agent.pathPending && _agent.remainingDistance <= ArriveDistance)
        {
            _agent.isStopped = true;
            UpdateRotation( new Vector3(Mathf.Sin(Time.time * 1.8f), 0f, Mathf.Cos(Time.time * 1.8f)) );
        }
        else
        {
            UpdateRotation(CurrentHeading());
        }

        if (!inStartGrace)
        {
            if (CheckStareViolation())
            {
                StartRoar(fromStare: true);
                return;
            }
            if (CheckNoiseViolation(out Vector3 noiseSpot))
            {
                _target = noiseSpot;
                StartRoar(fromStare: false);
                return;
            }
            if (CheckRotationSightIntrusion())
            {
                SetMode(Mode.Warn);
                return;
            }
        }

        if (Time.time >= _stateUntil)
        {
            // Return to current station (or rotate if the 2-minute timer elapsed)
            if (_stations.Count > 1 && Time.time >= _nextRotationAt)
            {
                ForceRotateToNextStation();
            }
            else if (_stationIndex >= 0 && _stationIndex < _stations.Count)
            {
                _target = _stations[_stationIndex];
                SetMode(Mode.Rotating);
            }
            else
            {
                SetMode(Mode.Chopping);
            }
        }
    }

    private void TickWarn()
    {
        _agent.isStopped = true;
        _physics?.SetChopping(false);
        if (Player != null) UpdateRotation(Player.position - transform.position);

        if (CheckStareViolation())
        {
            StartRoar(fromStare: true);
            return;
        }

        if (CheckNoiseViolation(out Vector3 noiseSpot))
        {
            _target = noiseSpot;
            StartRoar(fromStare: false);
            return;
        }

        if (Time.time >= _stateUntil)
        {
            // If the player is still standing openly in front of him, escalate to Roar -> Charge!
            if (CheckStationIntrusion() || CheckRotationSightIntrusion())
            {
                StartRoar(fromStare: false);
            }
            else
            {
                _stareExposure = 0f;
                SetMode(Mode.Chopping);
            }
        }
    }

    private void TickRoar()
    {
        _agent.isStopped = true;
        _physics?.SetChopping(true, 0.35f); // Cleaver raised high during roar wind-up
        if (Player != null) UpdateRotation(Player.position - transform.position);

        if (Time.time >= _stateUntil)
        {
            SetMode(Mode.Charge);
        }
    }

    private void TickCharge()
    {
        _agent.isStopped = false;
        _physics?.SetChopping(false);
        MoveSpeedTowards(chargeSpeed);
        UpdateRotation(CurrentHeading());

        if (Player != null && (Stealth == null || !Stealth.Hidden))
        {
            float dist = FlatDistanceToPlayer(transform.position);
            if (dist <= 15f && HasClearLine(HeadPosition, Player.position + Vector3.up * 1.1f, null))
            {
                _target = Player.position;
            }

            if (Time.time >= _nextRepathAt)
            {
                GoTo(_target);
            }

            float dy = Mathf.Abs(Player.position.y - transform.position.y);
            if (dist <= pinRadius && dy <= PinMaxVerticalDelta && HasClearLine(HeadPosition, Player.position + Vector3.up * 0.9f, null))
            {
                ExecuteButcherKill();
                return;
            }
        }

        bool arrived = !_agent.pathPending && _agent.remainingDistance <= ArriveDistance;
        if (Time.time >= _stateUntil || (Stealth != null && Stealth.Hidden && arrived))
        {
            SetMode(Mode.Cooldown);
        }
    }

    private void TickPin()
    {
        _agent.isStopped = true;
        if (Player != null) UpdateRotation(Player.position - transform.position);

        // Fast overhead execution cleaver hack cycle (0.52 s per swing -> 2 full overhead cleaver strikes during the 1.4 s capture sequence)
        const float killChopPeriod = 0.52f;
        float prevPhase = (_chopTimer % killChopPeriod) / killChopPeriod;
        _chopTimer += Time.deltaTime;
        float normPhase = (_chopTimer % killChopPeriod) / killChopPeriod;
        if (normPhase < prevPhase) _chopStruckThisCycle = false;

        _physics?.SetChopping(true, normPhase, 1.45f);
        if (!_chopStruckThisCycle && normPhase >= 0.68f && prevPhase < 0.68f)
        {
            _chopStruckThisCycle = true;
            PlayChopThwack();
            StartCoroutine(ShakeView(0.28f, 7f));
        }

        if (CanAct && Time.time >= _stateUntil)
        {
            SetMode(Mode.Cooldown);
        }
    }

    private void TickCooldown()
    {
        _agent.isStopped = false;
        _physics?.SetChopping(false);
        MoveSpeedTowards(cooldownSpeed);
        UpdateRotation(CurrentHeading());

        if (Time.time >= _nextRepathAt)
        {
            GoTo(_target);
        }

        if (!_agent.pathPending && (_agent.remainingDistance <= ArriveDistance || !_agent.hasPath))
        {
            _nextRotationAt = Time.time + stationRotateSeconds;
            SetMode(Mode.Chopping);
            return;
        }

        if (Time.time >= _stateUntil)
        {
            SetMode(Mode.Rotating);
        }
    }

    // ---------------------------------------------------------------- Rule Checks & Transitions

    private bool CheckStareViolation()
    {
        if (Player == null || (Stealth != null && Stealth.Hidden))
        {
            _stareExposure = Mathf.Max(0f, _stareExposure - Time.deltaTime * 1.5f);
            return false;
        }

        float dist = FlatDistanceToPlayer(transform.position);
        if (dist > stareDetectRange)
        {
            _stareExposure = Mathf.Max(0f, _stareExposure - Time.deltaTime * 1.5f);
            return false;
        }

        bool playerLookingAtBruno = IsInViewCone(HeadPosition, 22f, stareDetectRange) || IsInViewCone(ChestPosition, 22f, stareDetectRange);
        bool headLit = Perception.IsIlluminatedByTorch(Torch, HeadPosition, transform);
        bool illuminatedOrClose = dist <= 7.5f || headLit || Perception.IsIlluminatedByTorch(Torch, ChestPosition, transform);

        if (playerLookingAtBruno && illuminatedOrClose && HasClearLine(HeadPosition, ViewCamera != null ? ViewCamera.position : Player.position + Vector3.up * 1.5f, null))
        {
            float rate = (Torch != null && Torch.IsFocused && headLit) ? 1.85f : 1.0f;
            _stareExposure += Time.deltaTime * rate;
            if (_stareExposure >= stareGraceSeconds)
            {
                _target = Player.position;
                return true;
            }
        }
        else
        {
            _stareExposure = Mathf.Max(0f, _stareExposure - Time.deltaTime * 1.25f);
        }

        return false;
    }

    private bool CheckStationIntrusion()
    {
        if (Player == null || (Stealth != null && Stealth.Hidden)) return false;

        float dist = FlatDistanceToPlayer(transform.position);
        if (dist > roomIntrusionRadius) return false;

        bool quietStealth = Stealth != null && !Stealth.FlashlightOn && Stealth.NoiseRadius < 2.5f;
        Vector3 toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        float angle = toPlayer.sqrMagnitude > 0.001f && forward.sqrMagnitude > 0.001f
            ? Vector3.Angle(forward, toPlayer)
            : 0f;

        // Sneaking behind Bruno's back while he chops on the block is safe unless you touch him (< 1.45 m)
        if (angle > chopSightHalfAngle && (quietStealth || dist > 2.1f)) return false;
        if (quietStealth && dist > 3.2f) return false;

        if (!HasClearLine(HeadPosition, Player.position + Vector3.up * 1.0f, null)) return false;

        _target = Player.position;
        return true;
    }

    private bool CheckRotationSightIntrusion()
    {
        if (Player == null || (Stealth != null && Stealth.Hidden)) return false;

        float dist = FlatDistanceToPlayer(transform.position);
        float range = roomIntrusionRadius + 2.5f;
        if (dist > range) return false;

        Vector3 toPlayer = Player.position - transform.position;
        toPlayer.y = 0f;
        Vector3 forward = transform.forward;
        forward.y = 0f;
        float angle = toPlayer.sqrMagnitude > 0.001f && forward.sqrMagnitude > 0.001f
            ? Vector3.Angle(forward, toPlayer)
            : 0f;

        if (angle > rotateSightHalfAngle) return false;
        if (Stealth != null && !Stealth.FlashlightOn && Stealth.NoiseRadius < 2.5f && dist > 4.0f) return false;
        if (!HasClearLine(HeadPosition, Player.position + Vector3.up * 1.0f, null)) return false;

        _target = Player.position;
        return true;
    }

    private bool CheckNoiseViolation(out Vector3 violationPosition)
    {
        violationPosition = Player != null ? Player.position : transform.position;
        if (Player == null || Stealth == null || Stealth.Hidden) return false;

        float dist = FlatDistanceToPlayer(transform.position);
        if (dist > noiseDisciplineRange) return false;

        bool sprinting = Stealth.NoiseRadius >= sprintNoiseThreshold;
        bool jumping = Controller != null && !Controller.Grounded;
        bool noisyFloor = Stealth.SurfaceNoiseMultiplier > 1.05f && Stealth.NoiseRadius > 1.5f;
        if (!sprinting && !jumping && !noisyFloor) return false;

        bool hasLine = HasClearLine(ChestPosition, Player.position + Vector3.up * 1.0f, null);
        if (hasLine && dist <= noiseDisciplineRange) return true;
        if (dist <= noiseDisciplineRange * 0.72f) return true;

        return false;
    }

    private void HandleMazeNoise(Vector3 position, float radius)
    {
        if (!_configured || !CanAct || Time.time < _dormantUntil) return;
        if (Time.time - GameFlow.RunStartTime < runStartGraceSeconds) return;
        if (_mode == Mode.Roar || _mode == Mode.Charge || _mode == Mode.Pin || _mode == Mode.Cooldown) return;

        Vector3 d = position - transform.position;
        d.y = 0f;
        float dist = d.magnitude;
        if (dist < 1.5f) return; // ignore Bruno's own door-cleave or roar noise

        bool farFromPlayer = true;
        if (Player != null)
        {
            Vector3 dp = position - Player.position;
            dp.y = 0f;
            farFromPlayer = dp.magnitude > 2.0f;
        }

        // Thrown bottle/can or distant distraction lures Bruno out of his room to inspect the mess ("Who dropped a pan on my floor?!")
        if (farFromPlayer && dist <= Mathf.Max(throwLureRange, radius))
        {
            _target = position;
            GoTo(_target);
            Speak(InvestigateLines, SpeechBubble.Style.Normal);
            SetMode(Mode.Investigate);
            return;
        }

        if (dist <= Mathf.Max(noiseDisciplineRange, radius * 0.75f))
        {
            _target = position;
            StartRoar(fromStare: false);
        }
    }

    private void StartRoar(bool fromStare)
    {
        _roarFromStare = fromStare;
        if (Player != null && !fromStare && _target == Vector3.zero) _target = Player.position;
        if (Player != null && fromStare) _target = Player.position;

        PulseDread();
        AlertHunter(transform.position);
        Speak(fromStare ? RoarStareLines : RoarIntrusionLines, SpeechBubble.Style.Shout);
        PlayFirst(HeadPosition, 0.92f, 0.78f, GameAudio.Library.farScream, GameAudio.Library.spottedSting);
        SetMode(Mode.Roar);
    }

    private void TryKillPlayer()
    {
        if (_mode == Mode.Pin || _mode == Mode.Cooldown) return;
        if (Player == null || Stealth == null || Stealth.Hidden || Outcome == null || !CanAct) return;

        Vector3 d = Player.position - transform.position;
        if (Mathf.Abs(d.y) > PinMaxVerticalDelta) return;
        d.y = 0f;

        float killDist = _mode == Mode.Charge ? pinRadius : Mathf.Min(0.95f, pinRadius * 0.8f);
        if (d.magnitude > killDist) return;
        if (!HasClearLine(HeadPosition, Player.position + Vector3.up * 0.9f, null)) return;

        ExecuteButcherKill();
    }

    private void ExecuteButcherKill()
    {
        if (Outcome == null || !CanAct) return;

        // Frame Dark Bruno squarely at 1.22 m in front of the player so the camera sees his face, grabbing hand, and cleaver slash cleanly
        if (Player != null)
        {
            Vector3 fromPlayer = transform.position - Player.position;
            fromPlayer.y = 0f;
            Vector3 dir = fromPlayer.sqrMagnitude > 0.001f ? fromPlayer.normalized : Player.forward;
            Vector3 idealSpot = Player.position + dir * 1.22f;
            idealSpot.y = transform.position.y;
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
                if (NavMesh.SamplePosition(idealSpot, out NavMeshHit hit, 0.8f, NavMesh.AllAreas))
                {
                    _agent.Warp(hit.position);
                }
            }
            transform.rotation = Quaternion.LookRotation(-dir, Vector3.up);
        }

        _chopTimer = 0f;
        _chopStruckThisCycle = false;
        SetMode(Mode.Pin, force: true);
        _physics?.SetChopping(true, 0.15f, 1.45f);
        StartCoroutine(ShakeView(0.55f, 9f));
        PulseDread();
        Speak(PinLines, SpeechBubble.Style.Shout);
        PlayFirst(transform.position, 0.95f, 0.82f, GameAudio.Library.shutterSlam, GameAudio.Library.caught);
        LogEvent($"caught and killed the player in {_mode}");

        // Aim the player's capture look-at at upper-chest/neck height (1.42 m) so both his snarling face and cleaver swing fill the frame
        Vector3 captureLookTarget = transform.position + Vector3.up * 1.42f;
        Outcome.CaughtByEntity(captureLookTarget, () => WarpAway(postPinCooldownSeconds), EntityKind.Butcher);
    }

    private void AlertHunter(Vector3 pos)
    {
        if (Hunter == null || Time.time < _nextHunterAlertAt) return;
        _nextHunterAlertAt = Time.time + hunterAlertCooldown;
        Hunter.HearNoise(pos, hunterAlertRadius);
    }

    private void SetMode(Mode mode, bool force = false)
    {
        if (!force && _mode == mode) return;
        Mode prev = _mode;
        _mode = mode;

        switch (mode)
        {
            case Mode.Chopping:
                _agent.isStopped = true;
                _agent.ResetPath();
                _chopTimer = 0f;
                _chopStruckThisCycle = false;
                _stareExposure = 0f;
                PlayAnim(_animator != null && _animator.HasState(0, Animator.StringToHash(chopState)) ? chopState : (_locomotion.HasLocomotionState ? locomotionState : warnState));
                break;

            case Mode.Rotating:
                _agent.isStopped = false;
                if (_stationIndex >= 0 && _stationIndex < _stations.Count)
                {
                    _target = _stations[_stationIndex];
                    GoTo(_target);
                }
                PlayAnim(_locomotion.HasLocomotionState ? locomotionState : patrolState);
                break;

            case Mode.Investigate:
                _agent.isStopped = false;
                _stateUntil = Time.time + investigateSeconds;
                GoTo(_target);
                PlayAnim(_locomotion.HasLocomotionState ? locomotionState : patrolState);
                break;

            case Mode.Warn:
                _agent.isStopped = true;
                _agent.ResetPath();
                _stateUntil = Time.time + warnDurationSeconds;
                if (Time.time >= _nextWarnLineAt)
                {
                    _nextWarnLineAt = Time.time + 3.0f;
                    Speak(WarnLines, SpeechBubble.Style.Normal);
                    PlayFirst(transform.position, 0.72f, 0.85f, GameAudio.Library.shutterRoll, GameAudio.Library.keyJingle);
                }
                PlayAnim(warnState);
                break;

            case Mode.Roar:
                _agent.isStopped = true;
                _agent.ResetPath();
                _stateUntil = Time.time + roarWindupSeconds;
                PlayAnim(roarState);
                break;

            case Mode.Charge:
                _agent.isStopped = false;
                _agent.speed = Mathf.Max(_agent.speed, patrolSpeed);
                _stateUntil = Time.time + chargeMaxSeconds;
                GoTo(_target);
                PlayAnim(_locomotion.HasLocomotionState ? locomotionState : chargeState);
                break;

            case Mode.Pin:
                _agent.isStopped = true;
                _agent.ResetPath();
                _stateUntil = Time.time + pinSeconds;
                PlayAnim(pinState);
                break;

            case Mode.Cooldown:
                _agent.isStopped = false;
                _stateUntil = Time.time + postPinCooldownSeconds;
                // Advance to next station immediately on cooldown so Bruno never camps the pinned player
                if (_stations.Count > 1)
                {
                    _stationIndex = (_stationIndex + 1) % _stations.Count;
                    _target = _stations[_stationIndex];
                }
                else if (Maze != null && Player != null)
                {
                    _target = Maze.FarthestCellCenterFrom(Player.position);
                }
                _nextRotationAt = Time.time + stationRotateSeconds;
                GoTo(_target);
                PlayAnim(_locomotion.HasLocomotionState ? locomotionState : patrolState);
                break;
        }

        if (prev != mode) LogEvent($"{prev} -> {mode} (station {_stationIndex + 1}/{Mathf.Max(1, _stations.Count)})");
    }

    // ---------------------------------------------------------------- Chopping Stations & Visual Blocks

    private void BuildChoppingStations()
    {
        _stations.Clear();
        _stationBlockLookTargets.Clear();
        if (Maze != null)
        {
            Maze.CollectButcherStations(_stations);
        }
        if (_stations.Count == 0)
        {
            _stations.Add(transform.position);
        }

        // Spawn a small non-blocking visual Butcher Block beside each station so the player can recognize Bruno's chopping stations
        Material woodMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
        {
            color = new Color(0.26f, 0.17f, 0.11f)
        };
        if (woodMat.HasProperty("_BaseColor")) woodMat.SetColor("_BaseColor", new Color(0.26f, 0.17f, 0.11f));
        if (woodMat.HasProperty("_Smoothness")) woodMat.SetFloat("_Smoothness", 0.22f);

        Material bloodMat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"))
        {
            color = new Color(0.32f, 0.05f, 0.04f)
        };
        if (bloodMat.HasProperty("_BaseColor")) bloodMat.SetColor("_BaseColor", new Color(0.32f, 0.05f, 0.04f));
        if (bloodMat.HasProperty("_Smoothness")) bloodMat.SetFloat("_Smoothness", 0.55f);

        for (int i = 0; i < _stations.Count; i++)
        {
            Vector3 stationPos = _stations[i];
            Vector3 offsetDir = Quaternion.Euler(0f, (i * 90f) % 360f, 0f) * Vector3.forward;
            Vector3 blockPos = stationPos + offsetDir * 0.62f;
            blockPos.y = Maze != null ? Maze.FloorTopY : stationPos.y;
            _stationBlockLookTargets.Add(blockPos);

            GameObject blockRoot = new GameObject($"ButcherChoppingBlock_{i}");
            if (Maze != null) blockRoot.transform.SetParent(transform.parent, true);
            blockRoot.transform.position = blockPos;
            blockRoot.transform.rotation = Quaternion.LookRotation(-offsetDir, Vector3.up);

            GameObject stump = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stump.name = "Stump";
            stump.transform.SetParent(blockRoot.transform, false);
            stump.transform.localPosition = new Vector3(0f, 0.42f, 0f);
            stump.transform.localScale = new Vector3(0.56f, 0.42f, 0.56f);
            Collider c1 = stump.GetComponent<Collider>();
            if (c1 != null) Destroy(c1);
            stump.GetComponent<MeshRenderer>().sharedMaterial = woodMat;

            GameObject stain = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            stain.name = "BloodTop";
            stain.transform.SetParent(blockRoot.transform, false);
            stain.transform.localPosition = new Vector3(0f, 0.842f, 0f);
            stain.transform.localScale = new Vector3(0.46f, 0.005f, 0.46f);
            Collider c2 = stain.GetComponent<Collider>();
            if (c2 != null) Destroy(c2);
            stain.GetComponent<MeshRenderer>().sharedMaterial = bloodMat;

            _spawnedBlocks.Add(blockRoot.transform);
        }
    }

    private void GoTo(Vector3 point) => EntityGaitAnimator.GoTo(_agent, point, SnapRadius, RepathInterval, ref _nextRepathAt);

    private void MoveSpeedTowards(float targetSpeed) => EntityGaitAnimator.MoveSpeedTowards(_agent, targetSpeed, speedRampRate);

    private Vector3 CurrentHeading() => EntityGaitAnimator.CurrentHeading(_agent, transform);

    private void UpdateRotation(Vector3 desiredDirection)
    {
        _locomotion.UpdateRotation(transform, _agent, desiredDirection, _mode == Mode.Charge, turnSpeedCharge, turnSpeedMoving, turnSpeedStationary);
    }

    // ---------------------------------------------------------------- Locomotion, Audio & Speech Bubble

    private void UpdateLocomotionAndMouth()
    {
        if (_locomotion.StepPursuerGait(
            _agent,
            _mode == Mode.Charge,
            locomotionWalkSpeed,
            locomotionRunSpeed,
            maxWalkPlayback,
            maxLocomotionPlayback,
            chargeSpeed,
            animationBlendRate,
            out bool moving,
            out float v,
            out float walkSpeed,
            out float runSpeed,
            out float playback))
        {
            switch (_mode)
            {
                case Mode.Chopping:
                    string chopTarget = _animator.HasState(0, Animator.StringToHash(chopState)) ? chopState
                        : _locomotion.HasLocomotionState ? locomotionState
                        : warnState;
                    _locomotion.PlayActionState(chopTarget, 1f, patrolState, chargeState, warnState, roarState, pinState, chopState);
                    break;

                case Mode.Rotating:
                case Mode.Investigate:
                case Mode.Cooldown:
                    _locomotion.PlayPatrolLocomotion(moving, v, walkSpeed, playback, maxLocomotionPlayback, locomotionState, patrolState, warnState, chargeState);
                    break;

                case Mode.Charge:
                    _locomotion.PlayChargeLocomotion(v, runSpeed, playback, maxLocomotionPlayback, locomotionState, patrolState, chargeState, warnState);
                    break;

                case Mode.Warn:
                    _locomotion.PlayActionState(warnState, 1f, patrolState, chargeState, warnState, roarState, pinState, chopState);
                    break;

                case Mode.Roar:
                    _locomotion.PlayActionState(roarState, 1f, patrolState, chargeState, warnState, roarState, pinState, chopState);
                    break;

                case Mode.Pin:
                    _locomotion.PlayActionState(pinState, 1.35f, patrolState, chargeState, warnState, roarState, pinState, chopState);
                    break;
            }
        }

        if (_physics != null)
        {
            if (_mode == Mode.Roar || _mode == Mode.Pin)
            {
                _physics.SetSpeaking(false);
                _physics.SetMouthOpen(0.95f, 0.50f, 0.25f);
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
        _locomotion.PlayState(state, patrolState, chargeState, warnState, roarState, pinState, chopState, StateFadeSeconds);
    }

    private void PlayChopThwack()
    {
        if (FlatDistanceToPlayer(transform.position) > chopAudibleRange * 1.35f) return;

        AudioSlot slot = GameAudio.Library.liftThud.Has ? GameAudio.Library.liftThud
            : GameAudio.Library.shutterSlam.Has ? GameAudio.Library.shutterSlam
            : GameAudio.Library.timmyStep;
        if (!slot.Has) return;

        AudioSource src = GameAudio.SpawnAt(slot, transform.position + Vector3.up * 1.0f, 0.68f, Random.Range(0.76f, 0.88f), chopAudibleRange);
        if (src != null) src.transform.SetParent(transform, true);
    }

    private void UpdateLadleClank()
    {
        if (_mode != Mode.Rotating && _mode != Mode.Investigate && _mode != Mode.Charge && _mode != Mode.Cooldown) return;
        if (Time.time < _nextClankAt) return;

        float interval = _mode == Mode.Charge ? chargeClankInterval : patrolClankInterval;
        _nextClankAt = Time.time + interval * Random.Range(0.88f, 1.12f);

        if (FlatDistanceToPlayer(transform.position) > ladleClankRange * 1.35f) return;

        AudioSlot slot = GameAudio.Library.pipeClank.Has ? GameAudio.Library.pipeClank
            : GameAudio.Library.keyJingle.Has ? GameAudio.Library.keyJingle
            : GameAudio.Library.timmyStep;
        if (!slot.Has) return;

        float vol = _mode == Mode.Charge ? 0.82f : 0.54f;
        float pitch = _mode == Mode.Charge ? 0.86f : 0.72f;
        AudioSource src = GameAudio.SpawnAt(slot, transform.position + Vector3.up * 0.92f, vol, pitch, ladleClankRange);
        if (src != null) src.transform.SetParent(transform, true);
    }

    private void BuildSpeechBubble()
    {
        SpeechBubble.Palette butcherPalette = new SpeechBubble.Palette
        {
            Fill = new Color(0.90f, 0.85f, 0.74f, 0.92f),
            Outline = new Color(0.24f, 0.14f, 0.08f, 1f),
            Text = new Color(0.18f, 0.11f, 0.07f, 1f),
            Shout = new Color(0.76f, 0.10f, 0.06f, 1f)
        };
        _voiceMusic.BuildSpeechBubble(Hud, butcherPalette, "DarkBrunoSpeechBubble");
    }

    private void BuildAudioSources()
    {
        _voiceMusic.BuildAudioSources(transform, EyeHeight, ChestHeight, franticChaseClip, "ButcherVoiceAudio", "ButcherFranticChaseMusic", createVoiceSource: true);
    }

    private void UpdateChaseMusic()
    {
        bool activeChase = (_mode == Mode.Roar || _mode == Mode.Charge || _mode == Mode.Pin)
                           && !GameOutcome.IsOver
                           && (CanAct || _mode == Mode.Pin);
        _voiceMusic.UpdateChaseMusic(activeChase, franticChaseClip, franticChaseMaxVolume);
    }

    private AudioClip[] ResolveVoiceClips(string[] pool)
    {
        if (ReferenceEquals(pool, ChopMutterLines)) return chopMutterClips;
        if (ReferenceEquals(pool, RotateLines)) return rotateClips;
        if (ReferenceEquals(pool, InvestigateLines)) return investigateClips;
        if (ReferenceEquals(pool, WarnLines)) return warnClips;
        if (ReferenceEquals(pool, RoarIntrusionLines)) return roarIntrusionClips;
        if (ReferenceEquals(pool, RoarStareLines)) return roarStareClips;
        if (ReferenceEquals(pool, PinLines)) return pinClips;
        return null;
    }

    private void Speak(string[] pool, SpeechBubble.Style style)
    {
        _voiceMusic.Speak(
            FlatDistanceToPlayer(transform.position),
            pool,
            ResolveVoiceClips(pool),
            style,
            () => HeadPosition + Vector3.up * 0.42f,
            shoutMaxRange: 26f,
            normalMaxRange: 18f);
    }
}
