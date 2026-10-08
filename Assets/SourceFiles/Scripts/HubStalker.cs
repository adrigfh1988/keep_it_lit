using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F93 (E4): the thing that walks the asylum - Timmy, the player's own robot, dulled, with two small red eyes. On a Starter Assets
/// locomotion controller (Speed/MotionSpeed/Grounded) the Speed blend follows the agent's real velocity, so the feet match the ground,
/// and it turns smoothly toward where it is going; a controller with Mixamo-style states (Idle/Walk/Run/Scream/Attack/StandUp) still
/// works as before. Floor 5
/// patrols a route of waypoints the whole visit; floor 4's chase set piece raises it at the foot of the basement stairs and sends it
/// after the player. It never touches GameOutcome: a catch (about 1.1 m with sight) is a blackout, a sting and "You wake at the door."
/// at the hub arrival, after which it resets. It ignores a hidden player (a locker), sees 60 degrees / 12 m (18 m with the focused torch
/// on), hears sprinting within 8 m, loses sight after 4 s, searches the last seen spot for 6 s and goes back to patrolling. Chase speed is
/// the player's walk speed x 1.15 (a sprint escapes it) and drops 15 % after two catches in one visit. The body lives under
/// AsylumHub/Life/Pursuer (inactive by default); the NavMeshAgent uses the "Hub" agent type.
/// </summary>
public class HubStalker : MonoBehaviour
{
    public enum Mode { Idle, Patrol, Spotted, Chase, Search, Catching, Rising }

    [Header("Parts")]
    [SerializeField] private Animator animator;

    [Header("Senses")]
    public float viewRange = 12f;
    public float focusedViewRange = 18f;
    public float viewAngle = 60f;
    public float hearRange = 8f;
    public float catchDistance = 1.1f;
    public float loseSightSeconds = 4f;
    public float searchSeconds = 6f;

    [Header("Movement")]
    public float patrolSpeed = 1.6f;
    public float chaseMultiplier = 1.15f;
    public float screamSeconds = 1.5f;
    [Tooltip("How long a locomotion rig (no scream clip) stands and stares when it spots the player, before the chase.")]
    public float stareSeconds = 0.7f;
    [Tooltip("How fast the body turns toward where it is walking (exponential, per second). Higher = snappier.")]
    public float turnSharpness = 6f;

    [Tooltip("The chase never loses track of the player (the floor-4 stair chase); a hidden player still breaks it.")]
    public bool relentless;

    public Mode CurrentMode { get; private set; } = Mode.Idle;
    public int Catches { get; private set; }
    public bool IsActive => gameObject.activeInHierarchy && CurrentMode != Mode.Idle;

    private NavMeshAgent _agent;
    private AudioSource _audio;
    private AudioClip _step;
    private Transform[] _route = new Transform[0];
    private int _routeIndex;
    private float _stateUntil, _lastSeen, _nextPath, _stepDistance;
    private Vector3 _lastKnown;
    private bool _hasRoute;
    private System.Action _onReset;
    private static int _agentType = int.MinValue;

    private static readonly int Idle = Animator.StringToHash("Idle"), Walk = Animator.StringToHash("Walk"), Run = Animator.StringToHash("Run"),
        Scream = Animator.StringToHash("Scream"), Attack = Animator.StringToHash("Attack"), StandUp = Animator.StringToHash("StandUp");
    private static readonly int AnimSpeed = Animator.StringToHash("Speed"), AnimMotionSpeed = Animator.StringToHash("MotionSpeed"),
        AnimGrounded = Animator.StringToHash("Grounded");
    private int _animState;
    private bool _locomotion;
    private Renderer[] _eyes = new Renderer[0];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _agentType = int.MinValue;

    private void Awake()
    {
        DetectRig();
        EnsureAgent();
        if (!_locomotion) FitEyes(); // the robot is authored standing; only a lying-pose Mixamo body needs its eyes re-placed
    }

    /// <summary>A Starter Assets controller (a "Speed" float) is driven by parameters; anything else by named states.</summary>
    private void DetectRig()
    {
        _locomotion = false;
        if (animator == null || animator.runtimeAnimatorController == null) return;
        foreach (AnimatorControllerParameter p in animator.parameters)
            if (p.type == AnimatorControllerParameterType.Float && p.nameHash == AnimSpeed) _locomotion = true;
        if (_locomotion)
        {
            animator.SetBool(AnimGrounded, true); // Starter Assets defaults Grounded to false, which holds the legs in the fall pose
            animator.SetFloat(AnimMotionSpeed, 1f);
        }
        Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head != null)
        {
            Transform l = head.Find("EyeL"), r = head.Find("EyeR");
            _eyes = new[] { l != null ? l.GetComponent<Renderer>() : null, r != null ? r.GetComponent<Renderer>() : null };
        }
    }

    private void SetEyes(bool on)
    {
        foreach (Renderer r in _eyes) if (r != null) r.enabled = on;
    }

    /// <summary>
    /// The red eye beads sit under the head bone. Mixamo "Standing Up" models are saved in a lying pose, so their edit-time bone frames say nothing
    /// about where the face is: at the first activation the avatar is put in its own T-pose (controller off, Rebind) and the beads are placed
    /// 11 cm in front of and 8.5 cm above the head joint, 4 cm either side, along the body's forward - then the controller is given back.
    /// </summary>
    private void FitEyes()
    {
        if (animator == null || !animator.isHuman) return;
        Transform head = animator.GetBoneTransform(HumanBodyBones.Head);
        if (head == null) return;
        Transform left = head.Find("EyeL"), right = head.Find("EyeR");
        if (left == null && right == null) return;
        RuntimeAnimatorController controller = animator.runtimeAnimatorController;
        animator.runtimeAnimatorController = null;
        animator.Rebind();
        animator.Update(0f);
        Transform body = animator.transform;
        foreach ((Transform eye, float side) in new[] { (left, -1f), (right, 1f) })
        {
            if (eye == null) continue;
            eye.position = head.position + body.forward * 0.11f + body.up * 0.085f + body.right * (0.04f * side);
        }
        animator.runtimeAnimatorController = controller;
        animator.Rebind();
    }

    private void EnsureAgent()
    {
        if (_agent != null) return;
        _agent = GetComponent<NavMeshAgent>();
        if (_agent == null) _agent = gameObject.AddComponent<NavMeshAgent>();
        if (_agentType == int.MinValue)
        {
            _agentType = -1;
            for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
            {
                NavMeshBuildSettings s = NavMesh.GetSettingsByIndex(i);
                if (NavMesh.GetSettingsNameFromID(s.agentTypeID) == "Hub") _agentType = s.agentTypeID;
            }
        }
        if (_agentType != -1) _agent.agentTypeID = _agentType;
        else Debug.LogError("HubStalker: the \"Hub\" NavMesh agent type does not exist. Run LIGHTS OUT > Build Missing Pieces.", this);
        _agent.radius = 0.3f;
        _agent.height = 1.8f;
        _agent.acceleration = 12f;
        _agent.angularSpeed = 240f;
        _agent.updateRotation = false; // Turn() eases the body round instead of the agent snapping it
        _agent.stoppingDistance = 0.1f;
        _agent.autoBraking = false;
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        if (animator != null)
        {
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
        if (_audio == null)
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 2f;
            _audio.maxDistance = 22f;
            _step = DoorAudio.BuildClunkClip(0.14f, 85f, 0.55f);
        }
    }

    private float PlayerWalkSpeed
    {
        get
        {
            AsylumHub hub = AsylumHub.Active;
            return hub != null && hub.Controller != null ? hub.Controller.MoveSpeed : 2f;
        }
    }

    // ---------------------------------------------------------------- control

    /// <summary>Switches the creature on at `spawn` (snapped to the navmesh) doing nothing until told.</summary>
    public bool Place(Vector3 spawn)
    {
        gameObject.SetActive(true);
        EnsureAgent();
        if (!NavMesh.SamplePosition(spawn, out NavMeshHit hit, 3f, new NavMeshQueryFilter { agentTypeID = _agent.agentTypeID, areaMask = NavMesh.AllAreas }))
        {
            Debug.LogError($"HubStalker: {spawn} is not on the Hub navmesh.", this);
            return false;
        }
        _agent.enabled = false;
        transform.position = hit.position;
        _agent.enabled = true;
        _agent.Warp(hit.position);
        CurrentMode = Mode.Idle;
        _animState = 0;
        SetEyes(true);
        PlayAnim(Idle, 0.1f);
        return true;
    }

    /// <summary>Floor 5: walk the route forever, `startIndex` first. `onReset` (optional) runs after each catch instead of the default respawn.</summary>
    public void StartPatrol(Transform[] route, int startIndex, System.Action onReset = null)
    {
        EnsureAgent();
        _route = route ?? new Transform[0];
        _hasRoute = _route.Length > 0;
        _routeIndex = Mathf.Clamp(startIndex, 0, Mathf.Max(0, _route.Length - 1));
        _onReset = onReset;
        GoPatrol();
    }

    /// <summary>The chase set piece: it rises (zombie StandUp clip; a robot just stands dark until its eyes come on), screams, and hunts the player.</summary>
    public void StartRise(float seconds, System.Action onReset)
    {
        EnsureAgent();
        _onReset = onReset;
        _hasRoute = false;
        CurrentMode = Mode.Rising;
        _stateUntil = Time.time + seconds;
        if (_locomotion) SetEyes(false);
        PlayAnim(StandUp, 0.05f);
    }

    public void Stop()
    {
        CurrentMode = Mode.Idle;
        if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
        PlayAnim(Idle, 0.2f);
    }

    public void ResetCatches() => Catches = 0;

    /// <summary>Goes straight to the scream and the chase (a set piece wants it on the player now).</summary>
    public void ForceChase() { EnsureAgent(); if (_agent.isOnNavMesh) Spot(); }

    private void GoPatrol()
    {
        CurrentMode = Mode.Patrol;
        _agent.isStopped = false;
        _agent.speed = patrolSpeed;
        if (_hasRoute && _agent.isOnNavMesh) _agent.SetDestination(_route[_routeIndex].position);
    }

    private void GoChase()
    {
        CurrentMode = Mode.Chase;
        _agent.isStopped = false;
        _agent.speed = PlayerWalkSpeed * chaseMultiplier * (Catches >= 2 ? 0.85f : 1f);
        _lastSeen = Time.time;
    }

    // ---------------------------------------------------------------- perception

    private bool CanSee()
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.Player == null || hub.PlayerCamera == null) return false;
        if (hub.Stealth != null && hub.Stealth.Hidden) return false;
        Vector3 eye = transform.position + Vector3.up * 1.6f;
        Vector3 target = hub.Player.position + Vector3.up * 1.1f;
        Vector3 to = target - eye;
        if (Mathf.Abs(to.y) > 3.2f) return false;
        Flashlight torch = hub.Torch;
        float range = torch != null && torch.IsOn && torch.IsFocused ? focusedViewRange : viewRange;
        Vector3 flat = new Vector3(to.x, 0f, to.z);
        if (flat.magnitude > range) return false;
        if (Vector3.Angle(transform.forward, flat) > viewAngle * 0.5f && flat.magnitude > 1.6f) return false;
        return Clear(eye, target, hub.Player);
    }

    private bool CanHear()
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.Player == null || hub.Controller == null || !hub.Controller.IsSprinting) return false;
        if (hub.Stealth != null && hub.Stealth.Hidden) return false;
        Vector3 to = hub.Player.position - transform.position;
        return Mathf.Abs(to.y) < 3.2f && new Vector3(to.x, 0f, to.z).magnitude <= hearRange;
    }

    private static bool Clear(Vector3 from, Vector3 to, Transform player)
    {
        if (!Physics.Linecast(from, to, out RaycastHit hit, ~0, QueryTriggerInteraction.Ignore)) return true;
        return hit.collider.transform == player || hit.collider.transform.IsChildOf(player);
    }

    // ---------------------------------------------------------------- update

    private void Update()
    {
        if (_agent == null || !_agent.isOnNavMesh) return;
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.Player == null || !hub.ContentActive) return;

        bool paused = Time.timeScale <= 0f || hub.IsTransitioning || hub.Leaving;
        _agent.isStopped = paused || CurrentMode == Mode.Idle || CurrentMode == Mode.Spotted || CurrentMode == Mode.Catching || CurrentMode == Mode.Rising;
        if (paused) return;

        switch (CurrentMode)
        {
            case Mode.Patrol:
                if (CanSee() || CanHear()) { Spot(); break; }
                if (_hasRoute && !_agent.pathPending && _agent.remainingDistance <= 0.6f)
                {
                    _routeIndex = (_routeIndex + 1) % _route.Length;
                    _agent.SetDestination(_route[_routeIndex].position);
                }
                break;

            case Mode.Spotted:
                FaceTowards(hub.Player.position);
                if (Time.time >= _stateUntil) GoChase();
                break;

            case Mode.Rising:
                if (Time.time >= _stateUntil) Spot();
                break;

            case Mode.Chase:
                _agent.speed = PlayerWalkSpeed * chaseMultiplier * (Catches >= 2 ? 0.85f : 1f);
                bool hiddenNow = hub.Stealth != null && hub.Stealth.Hidden;
                bool sees = relentless ? !hiddenNow : CanSee() || CanHear();
                if (sees)
                {
                    _lastSeen = Time.time;
                    _lastKnown = hub.Player.position;
                }
                else if (Time.time - _lastSeen > loseSightSeconds)
                {
                    CurrentMode = Mode.Search;
                    _stateUntil = Time.time + searchSeconds;
                    _agent.SetDestination(_lastKnown);
                    _agent.speed = patrolSpeed;
                    break;
                }
                if (Time.time >= _nextPath)
                {
                    _nextPath = Time.time + 0.2f;
                    _agent.SetDestination(sees ? hub.Player.position : _lastKnown);
                }
                TryCatch(hub);
                break;

            case Mode.Search:
                if (CanSee() || CanHear()) { Spot(); break; }
                if (Time.time >= _stateUntil)
                {
                    if (_hasRoute) GoPatrol();
                    else CurrentMode = Mode.Idle;
                }
                break;
        }

        Turn();
        UpdateAnimation();
        Footsteps();
    }

    private void Spot()
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub != null && hub.Player != null) _lastKnown = hub.Player.position;
        _lastSeen = Time.time;
        CurrentMode = Mode.Spotted;
        _stateUntil = Time.time + (_locomotion ? stareSeconds : screamSeconds);
        SetEyes(true);
        GameAudio.PlayAt(GameAudio.Library.timmySpot, transform.position + Vector3.up * 1.4f, 1f); // F97: servo screech (recorded clip only)
        PlayAnim(Scream, 0.1f);
    }

    private void TryCatch(AsylumHub hub)
    {
        Vector3 to = hub.Player.position - transform.position;
        if (Mathf.Abs(to.y) > 1.4f || new Vector3(to.x, 0f, to.z).magnitude > catchDistance) return;
        if (hub.Stealth != null && hub.Stealth.Hidden) return;
        if (!Clear(transform.position + Vector3.up * 1.2f, hub.Player.position + Vector3.up * 1.1f, hub.Player)) return;
        if (!hub.BeginCatch(ResetAfterCatch)) return;
        CurrentMode = Mode.Catching;
        _agent.isStopped = true;
        FaceTowards(hub.Player.position);
        PlayAnim(Attack, 0.05f);
    }

    private void ResetAfterCatch()
    {
        Catches++;
        if (_onReset != null) { _onReset(); return; }
        // Default: far from the arrival, back to patrolling.
        AsylumHub hub = AsylumHub.Active;
        if (_hasRoute && hub != null)
        {
            int far = 0;
            float best = -1f;
            for (int i = 0; i < _route.Length; i++)
            {
                float d = (_route[i].position - hub.ArrivalPosition).sqrMagnitude;
                if (d > best) { best = d; far = i; }
            }
            _routeIndex = far;
            Place(_route[far].position);
            GoPatrol();
        }
        else Stop();
    }

    private void FaceTowards(Vector3 world)
    {
        Vector3 d = world - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), 360f * Time.deltaTime);
    }

    // ---------------------------------------------------------------- animation + sound

    private void PlayAnim(int state, float fade)
    {
        if (_locomotion || animator == null || !animator.isActiveAndEnabled || _animState == state) return; // a locomotion rig has no named clips
        _animState = state;
        animator.CrossFadeInFixedTime(state, fade);
    }

    /// <summary>While it walks, the body eases round toward where the agent wants to go (the agent itself never rotates it).</summary>
    private void Turn()
    {
        if (CurrentMode != Mode.Patrol && CurrentMode != Mode.Chase && CurrentMode != Mode.Search) return;
        Vector3 v = _agent.desiredVelocity;
        v.y = 0f;
        if (v.sqrMagnitude < 0.04f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(v), 1f - Mathf.Exp(-turnSharpness * Time.deltaTime));
    }

    private void UpdateAnimation()
    {
        if (animator == null) return;
        float speed = _agent.velocity.magnitude;
        if (_locomotion)
        {
            // Starter Assets' blend tree: Speed 0 idle, 2 walk, 6 run, in m/s - the agent's own speed keeps the feet on the ground.
            animator.speed = 1f;
            animator.SetBool(AnimGrounded, true);
            animator.SetFloat(AnimMotionSpeed, 1f);
            animator.SetFloat(AnimSpeed, _agent.isStopped ? 0f : speed, 0.12f, Time.deltaTime);
            return;
        }
        switch (CurrentMode)
        {
            case Mode.Patrol:
            case Mode.Search:
                PlayAnim(speed > 0.15f ? Walk : Idle, 0.25f);
                animator.speed = speed > 0.15f ? Mathf.Clamp(speed / 1.2f, 0.7f, 1.5f) : 1f;
                break;
            case Mode.Chase:
                PlayAnim(Run, 0.2f);
                animator.speed = Mathf.Clamp(speed / 3.0f, 0.8f, 1.5f);
                break;
            default:
                animator.speed = 1f;
                break;
        }
    }

    private void Footsteps()
    {
        if (_audio == null || _agent.velocity.sqrMagnitude < 0.1f) return;
        _stepDistance += _agent.velocity.magnitude * Time.deltaTime;
        float stride = CurrentMode == Mode.Chase ? 1.5f : 1.0f;
        if (_stepDistance >= stride)
        {
            _stepDistance = 0f;
            _audio.pitch = Random.Range(0.9f, 1.05f);
            DoorAudio.Note("HubStalker step");
            AudioSlot stepSlot = GameAudio.Library.timmyStep; // F97: heavy recorded steps, pitch-varied
            AudioClip stepClip = stepSlot.Pick();
            if (stepClip != null) { _audio.pitch *= stepSlot.PickPitch(); _audio.PlayOneShot(stepClip, (CurrentMode == Mode.Chase ? 1f : 0.8f) * stepSlot.volume); }
            else _audio.PlayOneShot(_step, CurrentMode == Mode.Chase ? 1f : 0.8f);
        }
    }

    private void OnDisable()
    {
        CurrentMode = Mode.Idle;
        _animState = 0;
    }
}
