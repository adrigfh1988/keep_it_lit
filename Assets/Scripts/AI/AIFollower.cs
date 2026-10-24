using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.AI;
// System.Random would otherwise collide with UnityEngine.Random throughout this file
using Random = UnityEngine.Random;

[RequireComponent(typeof(NavMeshAgent))]
public partial class AIFollower : MonoBehaviour
{
    [SerializeField] private Transform target;

    [Header("Movement")]
    [Tooltip("Speed when close to the target (m/s)")]
    [SerializeField] private float moveSpeed = 3.5f;
    [Tooltip("Speed when far from the target (m/s). Clamped in Start to the player's sprint speed x maxSpeedFraction - it must never be able to out-sprint the player.")]
    [SerializeField] private float runSpeed = 4.5f;
    [Tooltip("Distance at which the follower switches from moveSpeed to runSpeed")]
    [SerializeField] private float runDistance = 4f;
    [Tooltip("The agent decelerates to a stop this far from the target. Kept inside captureRadius in TickChase, or it would park out of reach.")]
    [SerializeField] private float stoppingDistance = 0.9f;
    [Tooltip("NavMeshAgent.acceleration - how hard it leans into a change of speed. Split from speedRampRate (F50): this one only affects the agent's own steering, so slowing it down does not stretch the moveSpeed-to-runSpeed jump when the hatch opens.")]
    [SerializeField] private float acceleration = 6f;
    [Tooltip("How fast MoveSpeedTowards ramps the agent's target speed (m/s^2). Kept at the old acceleration value (14) so the hatch-opens jump to runSpeed still lands in about 0.3s - see the comment on ChaseSpeed.")]
    [SerializeField] private float speedRampRate = 14f;

    [Header("Following")]
    [Tooltip("How often the destination is refreshed (seconds)")]
    [SerializeField] private float repathInterval = 0.1f;
    [Tooltip("How far from the target to search for the closest walkable point (e.g. when the player is jumping)")]
    [SerializeField] private float navMeshSearchRadius = 6f;
    [Tooltip("If the follower falls farther than this from the target it is teleported next to it. Only used without wander points.")]
    [SerializeField] private float teleportDistance = 40f;

    [Header("Capture")]
    [Tooltip("Flat distance to the player's centre at which it has you. Must be larger than the chase stopping distance or it never fires.")]
    [SerializeField] private float captureRadius = 1.4f;

    [Header("Perception")]
    [Tooltip("How far the follower can see a fully lit player (m). Scaled down when their flashlight is off.")]
    [SerializeField] private float viewDistance = 18f;
    [Tooltip("Total width of the field of view (degrees)")]
    [SerializeField] private float viewAngle = 110f;
    [Tooltip("Height of the eyes above the follower's feet")]
    [SerializeField] private float eyeHeight = 1.5f;
    [Tooltip("Height of the point on the target that has to be visible")]
    [SerializeField] private float targetHeight = 1f;
    [Tooltip("How long the follower keeps chasing after losing sight (seconds)")]
    [SerializeField] private float loseSightTime = 3f;
    [Tooltip("Seconds between two line-of-sight tests")]
    [SerializeField] private float sightCheckInterval = 0.1f;
    [Tooltip("F73: seconds between two torch-beam noise triggers, so standing in the player's beam does not spam HearNoise every sight tick")]
    [SerializeField] private float torchBeamNoiseCooldown = 6f;
    [Tooltip("F73: HearNoise radius when the player's torch beam catches the hunter")]
    [SerializeField] private float torchBeamNoiseRadius = 40f;

    [Header("Escalation")]
    [Tooltip("Speed before a single star has been taken. Low enough to read as barely moving at all.")]
    [SerializeField] private float dormantSpeed = 0.7f;
    [Tooltip("Chance, at full progress, that a wander leg heads for the cell nearest the player instead of a star or a random cell. It reads as the thing knowing roughly where you are, without ever knowing exactly.")]
    [Range(0f, 1f)]
    [SerializeField] private float hunchBiasAtPeak = 0.35f;

    [Header("Hearing")]
    [Tooltip("Seconds between hearing checks")]
    [SerializeField] private float hearingInterval = 0.3f;

    [Header("F103 A7 - heard you")]
    [Tooltip("A noise that pulls it off a patrol or out of an ambush stops it dead for this long, listening, before it rushes (s)")]
    [SerializeField] private float listenPauseSeconds = 1.2f;
    [Tooltip("Minimum seconds between two heard-you beats, so a run of noises cannot chain them (s)")]
    [SerializeField] private float listenCooldown = 12f;
    [Tooltip("After the pause it sprints toward the spot it heard for this long (s)")]
    [SerializeField] private float rushSeconds = 3f;
    [Tooltip("Rush speed as a fraction of the player's sprint speed. Always clamped to the speed cap, so a sprinting player still gets away.")]
    [Range(0.1f, 0.99f)]
    [SerializeField] private float rushSprintFraction = 0.8f;

    [Header("F104 B2 - committed chase")]
    [Tooltip("Seconds of unbroken sight before the chase commits: the hunter then matches a walking player (and, in the escape, nearly a sprinting one) (s)")]
    [SerializeField] private float chaseCommitSeconds = 2f;
    [Tooltip("Committed speed during the escape as a fraction of the player's sprint. Deliberately above the speed cap but still under 1, so a sprinting player still gets away.")]
    [Range(0.5f, 0.99f)]
    [SerializeField] private float escapeCommitFraction = 0.95f;

    [Header("F104 B1 - backoff")]
    [Tooltip("Hearing scale multiplier while the DreadMeter has sent the hunter away. Sight still works: a backoff is not blindness.")]
    [SerializeField] private float backoffHearingScale = 0.5f;

    [Header("Wander")]
    [Tooltip("How close the follower has to get to a wander point before picking the next one")]
    [SerializeField] private float wanderStoppingDistance = 0.3f;
    [Tooltip("Chance of heading for a cell that still holds a star instead of anywhere. A hard preference would make it camp them.")]
    [Range(0f, 1f)]
    [SerializeField] private float patrolBias = 0.6f;

    [Header("Search")]
    [Tooltip("How far around the last known position to sweep (m)")]
    [SerializeField] private float searchRadius = 7.5f;
    [Tooltip("How many spots to check before giving up")]
    [SerializeField] private int searchPoints = 4;
    [Tooltip("Seconds spent looking around at each spot")]
    [SerializeField] private float searchDwellTime = 1f;
    [Tooltip("Hard limit on a single search, so repeated noises cannot trap it in a loop")]
    [SerializeField] private float searchBudget = 12f;

    [Header("Speed cap")]
    [Tooltip("Every speed is clamped to the player's sprint speed times this. It must never be able to out-sprint the player.")]
    [Range(0.5f, 0.99f)]
    [SerializeField] private float maxSpeedFraction = 0.86f;

    [Header("Hiding spots")]
    [Tooltip("How close a hiding spot has to be to the last known position to be checked during a search")]
    [SerializeField] private float hidingSpotSearchRadius = 6f;

    [Header("F104 B3 - locker heat")]
    [Tooltip("A heard or seen player heats every hiding spot within this radius of the event (m)")]
    [SerializeField] private float heatRadius = 6f;
    [Tooltip("Heat (0..1) one event adds to each spot in range")]
    [SerializeField] private float heatPerEvent = 0.35f;
    [Tooltip("Seconds for a full heat to cool to zero")]
    [SerializeField] private float heatDecaySeconds = 120f;
    [Tooltip("A spot at or above this heat is a hot spot: a search checks it even when it is farther out, and stands outside it")]
    [Range(0f, 1f)]
    [SerializeField] private float hotThreshold = 0.5f;
    [Tooltip("How far from the last known position a hot spot is still added to the search (m)")]
    [SerializeField] private float hotSpotSearchRadius = 15f;
    [Tooltip("Extra seconds it stands in front of a hot spot (s)")]
    [SerializeField] private float standOutsideSeconds = 4f;
    [Tooltip("How long it looks into the locker when it peeks, door ajar and eyes lit (s)")]
    [SerializeField] private float peekSeconds = 2.5f;
    [Tooltip("Peek chance = the spot's heat x this, once per floor, only when the player is hidden in that very spot")]
    [Range(0f, 1f)]
    [SerializeField] private float peekChanceScale = 0.6f;

    [Header("F105 C6 - where it was noisy")]
    [Tooltip("Seconds for a full cell heat to cool to zero")]
    [SerializeField] private float cellHeatDecaySeconds = 180f;
    [Tooltip("Heat added to the cell the player was heard in")]
    [SerializeField] private float cellHeatHeard = 0.4f;
    [Tooltip("Heat added to each of its four neighbours when the player is heard")]
    [SerializeField] private float cellHeatHeardNeighbour = 0.2f;
    [Tooltip("Heat added to the cell the player was seen in (at most every SightHeatInterval)")]
    [SerializeField] private float cellHeatSeen = 0.3f;
    [Tooltip("Chance of a wander leg heading for the warmest wander point, at full threat (scaled down by the threat dial)")]
    [Range(0f, 1f)]
    [SerializeField] private float heatWanderBias = 0.35f;
    [Tooltip("A wander point's score is its cell heat minus its distance divided by this (m); the warmest score wins")]
    [SerializeField] private float heatDistanceFalloff = 50f;
    [Tooltip("A cell colder than this is not worth a detour")]
    [SerializeField] private float minWanderHeat = 0.05f;

    [Header("F105 C6 - learned habits (per level, 0..3)")]
    [Tooltip("Lockers: added to the peek chance scale per level")]
    [SerializeField] private float lockerPeekChancePerLevel = 0.2f;
    [Tooltip("Lockers: extra seconds standing outside a hot locker per level")]
    [SerializeField] private float lockerStandOutsidePerLevel = 2f;
    [Tooltip("Lockers: extra metres on the radius within which a search checks hiding spots, per level")]
    [SerializeField] private float lockerSearchRadiusPerLevel = 2f;
    [Tooltip("Lockers: from this level it may peek twice a floor instead of once")]
    [SerializeField] private int lockerSecondPeekLevel = 2;
    [Tooltip("Throws: from level 1, a throw heard within this many seconds of the last heard one is ignored")]
    [SerializeField] private float throwIgnoreWindow = 20f;
    [Tooltip("Throws: chance an investigated throw searches the player's position instead of the impact, at levels 2 and 3")]
    [SerializeField] private float throwSeesThroughLevel2 = 0.3f;
    [SerializeField] private float throwSeesThroughLevel3 = 0.5f;
    [Tooltip("Dark: view distance is multiplied by 1 + this x level while the player's torch is off")]
    [SerializeField] private float darkViewPerLevel = 0.15f;
    [Tooltip("Sprint: the noise radius is multiplied by 1 + this x level while the player sprints")]
    [SerializeField] private float sprintNoisePerLevel = 0.15f;

    [Header("Ambush (F45)")]
    [Range(0f, 1f)] [SerializeField] private float ambushBias = 0.75f;
    [SerializeField] private float ambushWaitSeconds = 90f;
    [SerializeField] private float ambushHearingBoost = 1.5f;
    [Tooltip("Seconds before the same star may be ambushed again")]
    [SerializeField] private float ambushRepeatCooldown = 60f;
    [Tooltip("A pickup this close to the ambushed star counts as that star being taken (m)")]
    [SerializeField] private float ambushStarMatchRadius = 1.5f;
    [Tooltip("Never choose an ambush cell this close to the player (m) - roughly one cell width")]
    [SerializeField] private float ambushPlayerExclusionRadius = 4.5f;

    [Header("Rotation")]
    [Tooltip("Turn rate while chasing with sight (deg/s). Kept fast on purpose (F50): this is what holds a nearby player inside the sight cone, and dropping it would let a sprinting player two metres away out-turn it.")]
    [SerializeField] private float turnSpeed = 540f;
    [Tooltip("Turn rate while moving in any other state (deg/s). F50: slower than the chase-with-sight rate so ordinary turning reads as something with feet, not a turret.")]
    [SerializeField] private float turnSpeedMoving = 200f;
    [Tooltip("Turn rate while stationary - a search dwell without a look clip, facing a hiding spot, facing the ambushed star (deg/s).")]
    [SerializeField] private float turnSpeedStationary = 120f;

    [Header("Animation")]
    [SerializeField] private Animator animator;
    [SerializeField] private float animationBlendRate = 6f;
    [Tooltip("Ground speed (m/s) the walk and run clips cover at normal playback, measured from their root motion and written by KEEP IT LIT > Build > Hunter Body. The clip in use plays at body speed / this (MotionSpeed) so the feet keep up instead of gliding.")]
    [SerializeField] private float locomotionWalkSpeed = 1.6f;
    [SerializeField] private float locomotionRunSpeed = 4f;
    [Tooltip("Cap on how much faster than recorded a walk/run clip may play to keep up with the agent.")]
    [SerializeField] private float maxLocomotionPlayback = 2f;
    [Tooltip("F102: the walk clip plays up to this much faster than recorded before the run clip takes over (so a fast walk covers chases up to about walk clip speed x this; above it the run clip plays, slowed to match the ground it covers).")]
    [SerializeField] private float maxWalkPlayback = 1.75f;
    private const float MinLocomotionPlayback = 0.4f;
    private float _gait;
    private bool _gaitRunning;

    [Tooltip("Yaw (degrees) the visible mesh faces relative to its Animator node's +Z. 0 for the Timmy robot. A Generic rig whose model faces sideways in its file (ZombieSmooth.fbx faces +X) needs this so the sight cone, the turn-to-face and the eyes all use the face, not the node axis. Written by KEEP IT LIT > Build > Hunter Body.")]
    [SerializeField] private float modelFacingYaw = 0f;
    [Tooltip("Set by KEEP IT LIT > Build > Hunter Body: true when no 'look' clip resolved. While true, a search dwell keeps spinning the body in place (the only way to show a sweep with no clip); while false, the body stays still and HunterGaze's head sweep (plus the matching CanSeeTarget cone offset) does the work instead.")]
    [SerializeField] private bool bodySweepsDuringSearch = true;

    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimGrounded = Animator.StringToHash("Grounded");
    private static readonly int AnimMotionSpeed = Animator.StringToHash("MotionSpeed");
    private static readonly int AnimPose = Animator.StringToHash("Pose");
    private static readonly int AnimTurn = Animator.StringToHash("Turn");
    private static readonly int AnimReach = Animator.StringToHash("Reach");

    /// <summary>
    /// F50: the whole interface between the AI's state machine and the animator. Computed fresh every
    /// frame in UpdateAnimator and written as the Pose int parameter (when the controller has one) - the
    /// animator never sees State directly. Walk/Run are not poses at all: the Locomotion blend tree
    /// reads Speed (and Turn) for those. Reach is a one-shot trigger fired from EnterCaptured, not a
    /// Pose value - the pose stays whatever it already was (Idle, absent a stun) through a capture.
    /// </summary>
    public enum Pose
    {
        Idle,
        LookAround,
        Lurk,
        Stunned,
        Tantrum
    }

    private enum State
    {
        Chase,
        Wander,
        Search,
        Captured,
        Ambush
    }

    private NavMeshAgent _agent;
    private float _repathTimer;
    private float _animationBlend;
    private float _modelYawOffset;

    private bool _hasPoseParam;
    private bool _hasTurnParam;
    private bool _hasReachParam;

    private float _turnRaw;
    private float _turnBlend;
    private float _scanYawOffset;

    private readonly List<Vector3> _wanderPoints = new List<Vector3>();
    private readonly List<Vector3> _hidingSpots = new List<Vector3>();
    private bool _bustingHidingSpot;
    private bool _wasHidden;

    private State _state = State.Chase;
    private float _stunnedUntil = -1f;
    private float _stallUntil = -1f;
    private float _listenUntil = -1f;
    private float _rushUntil = -1f;
    private float _rushSpeed;
    private float _nextListenTime;

    [Header("Flashlight tantrum")]
    [Tooltip("Seconds the hunter stomps and slams its fists in rage when the player's torch beam hits it on patrol, before sprinting at the player.")]
    [SerializeField] private float tantrumDuration = 2.15f;
    [Tooltip("Minimum seconds between two flashlight tantrums.")]
    [SerializeField] private float tantrumCooldown = 12f;
    private float _tantrumUntil = -1f;
    private float _nextTantrumTime;

    private float _backoffUntil = -1f;
    private float _sightSince = -1f;
    private bool _commitLogged;
    private float _playerWalkSpeed;
    private float _playerSprintSpeed;
    private float _speedCap;
    private float _chaseWalkFraction = 1f;
    private float _chaseCommitScale = 1f;
    private TerrorSettings _terror = new TerrorSettings();

    private float[] _hidingHeat = new float[0];
    private float[] _hidingHeatAt = new float[0];
    private int _peeksThisFloor;
    private float _nextSightHeatTime;

    private readonly HunterHeatMap _cellHeat = new HunterHeatMap();
    private ThirdPersonController _playerController;
    private float _lastThrowHeardTime = -999f;
    private int _hotDwellSpot = -1;
    private readonly List<Vector3> _hotSpotBuffer = new List<Vector3>();
    private bool _hasSight;
    private float _sightTimer;
    private float _lostSightTimer;
    private Vector3 _lastKnownPosition;
    private int _wanderIndex = -1;
    private float _wanderRepickCooldown;

    private float _hearingTimer;
    private PlayerStealthState _stealth;

    private readonly List<Vector3> _searchQueue = new List<Vector3>();
    private float _searchDwellTimer;
    private float _searchBudgetTimer;

    private IReadOnlyList<Transform> _patrolTargets;
    private readonly List<Vector3> _gatePatrol = new List<Vector3>();

    private float _threat = 1f;
    private float _hearingScale = 1f;
    private bool _hunting;
    private float _speedThreatFloor;

    private MazeGenerator _maze;
    private AIPresence _presence;

    private Flashlight _torch;
    private float _torchNoiseCooldownUntil = -1f;
    private float _torchBeamDistance;

    private int _ambushStarIndex = -1;
    private Vector3 _ambushStarPosition;
    private float _ambushWaitTimer;
    private bool _ambushArrived;
    private readonly Dictionary<int, float> _ambushCooldownUntil = new Dictionary<int, float>();

    private bool _heldForIntake;

    /// <summary>Raised when the follower starts or stops actively chasing the player.</summary>
    public event Action<bool> ChaseStateChanged;

    /// <summary>Raised when the follower starts or stops lying in wait near a star the player is heading for.</summary>
    public event Action<bool> AmbushStateChanged;

    /// <summary>Raised once, when the follower reaches a player it can see.</summary>
    public event Action PlayerCaught;

    /// <summary>F103 A7: raised the moment a heard noise stops it dead (the "IT HEARD YOU" beat). DreadDirector shows the world text.</summary>
    public event Action HeardYou;

    /// <summary>F103 A7: Time.time of the last HeardYou beat, so TensionDirector can tell its own star-heard line was just superseded.</summary>
    public float LastHeardYouTime { get; private set; } = -9999f;

    /// <summary>Height of the eyes above the feet, for anything that wants to look at them.</summary>
    public float EyeHeight => eyeHeight;

    /// <summary>True while actively chasing a seen (or hiding-spot-busted) player.</summary>
    public bool IsChasing => _state == State.Chase;

    /// <summary>True once the follower has caught the player.</summary>
    public bool IsCaptured => _state == State.Captured;

    /// <summary>True while lying in wait near a star the player is heading toward.</summary>
    public bool IsAmbushing => _state == State.Ambush;

    /// <summary>Position of the star currently being ambushed. Only meaningful while IsAmbushing.</summary>
    public Vector3 AmbushStarPosition => _ambushStarPosition;

    /// <summary>True if the most recent ambush ended because the ambushed star was actually taken, rather than by being heard out of or a sighting.</summary>
    public bool LastAmbushEndedOnPickup { get; private set; }

    /// <summary>Time.time the most recent ambush ended, whatever the reason.</summary>
    public float LastAmbushEndTime { get; private set; } = -9999f;

    /// <summary>True once the hatch is open and the endgame chase is on (mirrors SetThreatLevel's hunting flag).</summary>
    public bool IsHunting => _hunting;

    /// <summary>F50: true while a line-of-sight check most recently succeeded.</summary>
    public bool HasSight => _hasSight;

    /// <summary>F103 A3: true while the lit torch beam is on the hunter (in cone, in range, with a clear line).</summary>
    public bool IsInTorchBeam { get; private set; }

    /// <summary>F103 A2: true while patrolling (not chasing, searching, ambushing or captured).</summary>
    public bool IsWandering => _state == State.Wander;

    /// <summary>F50: true while actively sweeping the area around a noise or a lost sighting.</summary>
    public bool IsSearching => _state == State.Search;

    /// <summary>F82: true while a bashed-open MazeDoor is holding it (Stall).</summary>
    public bool IsStalled => Time.time < _stallUntil;

    /// <summary>F50: the pose UpdateAnimator computed this frame.</summary>
    public Pose CurrentPose { get; private set; }

    /// <summary>F120: true while a Release stun is running (the grab).</summary>
    public bool IsStunned => Time.time < _stunnedUntil;

    /// <summary>F120: the capture that raised PlayerCaught was a locker bust (cornered).</summary>
    public bool LastCaptureWasBust { get; private set; }

    /// <summary>F120: which kind of noise last made the hunter react (World by default, Voice for the microphone).</summary>
    public NoiseSource LastHeardSource { get; private set; }

    /// <summary>F120: the body animator, for the death replay (null before Build Hunter Body has run).</summary>
    public Animator BodyAnimator => animator;

    /// <summary>F120: the Speed (gait 0/1/2) value last written to the animator.</summary>
    public float AnimGait => _gait;

    /// <summary>F120: the MotionSpeed (playback) value last written to the animator.</summary>
    public float AnimPlayback { get; private set; } = 1f;

    /// <summary>F50: the last position sight or hearing actually placed the player at.</summary>
    public Vector3 LastKnownPosition => _lastKnownPosition;

    /// <summary>F50: yaw offset (degrees) currently applied to the sight cone during a search dwell.</summary>
    public float ScanYawOffset => _scanYawOffset;

    /// <summary>F50: the tracked player transform.</summary>
    public Transform Target => target;

    /// <summary>Flat (Y-ignoring) distance to the target, or float.MaxValue with no target.</summary>
    public float FlatDistanceToTarget => IsDormantForSoloEntity ? float.MaxValue : (target != null ? FlatDirection(target.position - transform.position).magnitude : float.MaxValue);

    /// <summary>True on DESCENT floors where a solo boss predator (Elias) replaces the baseline Hunter.</summary>
    public bool IsDormantForSoloEntity { get; private set; }

    /// <summary>Parks or unparks the baseline Hunter for a solo-predator DESCENT floor.</summary>
    public void SetDormantForSoloEntity(bool dormant)
    {
        IsDormantForSoloEntity = dormant;
        if (dormant)
        {
            DoorBreakers.Unregister(transform);
            if (_agent != null)
            {
                if (_agent.isOnNavMesh) _agent.isStopped = true;
                _agent.enabled = false;
            }
            transform.position = new Vector3(0f, -500f, 0f);
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                r.enabled = false;
            foreach (AudioSource a in GetComponentsInChildren<AudioSource>(true))
                a.Stop();
        }
        else
        {
            if (_agent != null && !_agent.enabled) _agent.enabled = true;
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
                r.enabled = true;
        }
    }

    /// <summary>Corridor positions the follower patrols when it cannot see the player.</summary>
    public void SetWanderPoints(IEnumerable<Vector3> points)
    {
        _wanderPoints.Clear();
        if (points != null)
        {
            _wanderPoints.AddRange(points);
        }

        _state = _wanderPoints.Count > 0 ? State.Wander : State.Chase;
        _wanderIndex = -1;
    }

    /// <summary>Objects worth checking on while wandering. Entries may be destroyed at any time.</summary>
    public void SetPatrolTargets(IReadOnlyList<Transform> targets)
    {
        _patrolTargets = targets;
    }

    /// <summary>F111: the exit gates to patrol between once the escape is on (MazeEscape, two gates only).</summary>
    public void SetGatePatrol(IEnumerable<Vector3> gates)
    {
        _gatePatrol.Clear();
        if (gates != null) _gatePatrol.AddRange(gates);
    }

    /// <summary>Front-of-locker positions. Called from MazeGenerator.PlaceAI, before this component's Awake.</summary>
    public void SetHidingSpots(IEnumerable<Vector3> fronts)
    {
        _hidingSpots.Clear();
        if (fronts != null)
        {
            _hidingSpots.AddRange(fronts);
        }

        _hidingHeat = new float[_hidingSpots.Count];
        _hidingHeatAt = new float[_hidingSpots.Count];
    }

    /// <summary>F45: supplies AmbushCellsFor. Called from MazeGenerator.PlaceAI, before this component's Awake.</summary>
    public void SetAmbushProvider(MazeGenerator maze)
    {
        _maze = maze;
        if (maze != null) _cellHeat.Configure(maze.GridWidth, maze.GridHeight, cellHeatDecaySeconds);
    }

    /// <summary>F73: called once by MazeGenerator.SetUpAtmosphere, after the torch exists. May be null.</summary>
    public void BindTorch(Flashlight torch)
    {
        _torch = torch;
    }

    /// <summary>F103: which Tier A features run, and the [Terror] log.</summary>
    public void ApplyTerror(TerrorSettings terror)
    {
        _terror = terror ?? new TerrorSettings();
    }

    void Awake()
    {
        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = moveSpeed;
        _agent.acceleration = acceleration;
        _agent.stoppingDistance = stoppingDistance;
        _agent.updateRotation = false;

        if (animator == null)
        {
            animator = GetComponentInChildren<Animator>();
        }

        _modelYawOffset = ComputeModelYawOffset();
    }

    /// <summary>
    /// Yaw from this object's forward to the direction the visible mesh actually faces: the Animator
    /// node's own rotation plus modelFacingYaw for a rig whose mesh faces sideways inside that node.
    /// </summary>
    private float ComputeModelYawOffset()
    {
        Animator model = animator != null ? animator : GetComponentInChildren<Animator>();
        float nodeYaw = model != null
            ? Vector3.SignedAngle(FlatDirection(transform.forward), FlatDirection(model.transform.forward), Vector3.up)
            : 0f;
        return nodeYaw + modelFacingYaw;
    }

    /// <summary>World direction the visible mesh faces. Safe to call before Awake.</summary>
    public Vector3 VisualForward
    {
        get
        {
            Vector3 forward = FlatDirection(Quaternion.AngleAxis(ComputeModelYawOffset(), Vector3.up) * transform.forward);
            return forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
        }
    }

    void Start()
    {
        if (target == null)
        {
            FindTarget();
        }

        if (_stealth == null && target != null)
        {
            _stealth = target.GetComponent<PlayerStealthState>();
        }

        _presence = GetComponent<AIPresence>();

        ThirdPersonController player = target != null ? target.GetComponent<ThirdPersonController>() : null;
        _playerController = player;
        if (player != null)
        {
            float cap = player.SprintSpeed * maxSpeedFraction;
            _playerWalkSpeed = player.MoveSpeed;
            _playerSprintSpeed = player.SprintSpeed;
            _speedCap = cap;
            if (runSpeed > cap || moveSpeed > cap)
            {
                Debug.LogWarning($"AIFollower: speeds clamped to {cap:0.00} m/s (player sprint {player.SprintSpeed}).", this);
            }

            runSpeed = Mathf.Min(runSpeed, cap);
            moveSpeed = Mathf.Min(moveSpeed, cap);
            dormantSpeed = Mathf.Min(dormantSpeed, cap);
            _rushSpeed = Mathf.Min(player.SprintSpeed * rushSprintFraction, cap);
        }
        else
        {
            _rushSpeed = runSpeed;
            _speedCap = runSpeed;
        }

        if (!IsDormantForSoloEntity && !_agent.isOnNavMesh)
        {
            PlaceOnNavMesh(transform.position, 20f);
        }

        if (animator != null)
        {
            animator.SetBool(AnimGrounded, true);
            animator.SetFloat(AnimMotionSpeed, 1f);
            animator.fireEvents = false;

            foreach (AnimatorControllerParameter param in animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Int && param.name == "Pose") _hasPoseParam = true;
                else if (param.type == AnimatorControllerParameterType.Float && param.name == "Turn") _hasTurnParam = true;
                else if (param.type == AnimatorControllerParameterType.Trigger && param.name == "Reach") _hasReachParam = true;
            }
        }
    }

    private void OnEnable()
    {
        Pickup.OnCollectedAt += HandleStarTakenAt;
        if (!IsDormantForSoloEntity)
            DoorBreakers.Register(transform, _agent, false, Stall);
    }

    private void OnDisable()
    {
        Pickup.OnCollectedAt -= HandleStarTakenAt;
        DoorBreakers.Unregister(transform);
    }

    void Update()
    {
        if (IsDormantForSoloEntity) return;
        if (GameFlow.IsInIntake || GameFlow.IsInHub)
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
            _heldForIntake = true;
            return;
        }
        if (_heldForIntake)
        {
            _heldForIntake = false;
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = false;
        }

        if (target == null)
        {
            FindTarget();
            if (target == null)
            {
                return;
            }
        }

        if (!IsDormantForSoloEntity && !_agent.isOnNavMesh)
        {
            PlaceOnNavMesh(transform.position, 20f);
            UpdateRotation(FlatDirection(target.position - transform.position));
            UpdateAnimator();
            return;
        }

        if (Time.time < _stunnedUntil)
        {
            _agent.isStopped = true;
            UpdateAnimator();
            return;
        }

        _scanYawOffset = ComputeScanYawOffset();

        if (_wanderPoints.Count == 0)
        {
            UpdateChaseOnly();
        }
        else
        {
            UpdatePerception();
            UpdateHearing();
            UpdateStateMachine();
        }

        if (Time.time < _stallUntil || Time.time < _listenUntil || Time.time < _tantrumUntil)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
            if (Time.time < _tantrumUntil && target != null)
            {
                _lastKnownPosition = target.position;
                UpdateRotation(FlatDirection(target.position - transform.position));
            }
        }

        UpdateAnimator();
    }

    private void FindTarget()
    {
        GameObject[] players = GameObject.FindGameObjectsWithTag("Player");
        foreach (GameObject player in players)
        {
            if (player.GetComponent<CharacterController>() != null)
            {
                target = player.transform;
                _stealth = player.GetComponent<PlayerStealthState>();
                return;
            }
        }

        if (players.Length > 0)
        {
            target = players[0].transform;
            _stealth = target.GetComponent<PlayerStealthState>();
        }
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 facing = FlatDirection(Quaternion.AngleAxis(_modelYawOffset, Vector3.up) * transform.forward);
        if (facing.sqrMagnitude < 0.0001f)
        {
            facing = FlatDirection(transform.forward);
        }
        facing.Normalize();

        Gizmos.color = _hasSight ? Color.red : new Color(1f, 1f, 0f, 0.6f);
        Gizmos.DrawRay(eye, Quaternion.Euler(0f, -viewAngle * 0.5f, 0f) * facing * viewDistance);
        Gizmos.DrawRay(eye, Quaternion.Euler(0f, viewAngle * 0.5f, 0f) * facing * viewDistance);
        Gizmos.DrawRay(eye, facing * viewDistance);

        switch (_state)
        {
            case State.Captured:
                Gizmos.color = Color.red;
                break;
            case State.Ambush:
                Gizmos.color = new Color(0.5f, 0f, 0.6f, 0.7f);
                break;
            default:
                Gizmos.color = new Color(1f, 0.5f, 0f, 0.8f);
                break;
        }
        Vector3 feet = transform.position + Vector3.up * 0.05f;
        Vector3 previousPoint = feet + Vector3.right * captureRadius;
        for (int i = 1; i <= 32; i++)
        {
            float angle = i * Mathf.PI * 2f / 32f;
            Vector3 point = feet + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * captureRadius;
            Gizmos.DrawLine(previousPoint, point);
            previousPoint = point;
        }
    }
}
