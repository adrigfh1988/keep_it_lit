using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F93 (E4): the thing that walks the asylum - a black Adam with two small red eyes, animated with the Mixamo zombie clips. Floor 5
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
    private int _animState;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => _agentType = int.MinValue;

    private void Awake() => EnsureAgent();

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

    /// <summary>The chase set piece: it rises (zombie StandUp clip), screams, and hunts the player.</summary>
    public void StartRise(float seconds, System.Action onReset)
    {
        EnsureAgent();
        _onReset = onReset;
        _hasRoute = false;
        CurrentMode = Mode.Rising;
        _stateUntil = Time.time + seconds;
        PlayAnim(StandUp, 0.05f);
    }

    public void Stop()
    {
        CurrentMode = Mode.Idle;
        if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
        PlayAnim(Idle, 0.2f);
    }

    public void ResetCatches() => Catches = 0;

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
                bool sees = CanSee() || CanHear();
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

        UpdateAnimation();
        Footsteps();
    }

    private void Spot()
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub != null && hub.Player != null) _lastKnown = hub.Player.position;
        _lastSeen = Time.time;
        CurrentMode = Mode.Spotted;
        _stateUntil = Time.time + screamSeconds;
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
        if (animator == null || !animator.isActiveAndEnabled || _animState == state) return;
        _animState = state;
        animator.CrossFadeInFixedTime(state, fade);
    }

    private void UpdateAnimation()
    {
        if (animator == null) return;
        float speed = _agent.velocity.magnitude;
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
            _audio.PlayOneShot(_step, CurrentMode == Mode.Chase ? 1f : 0.8f);
        }
    }

    private void OnDisable()
    {
        CurrentMode = Mode.Idle;
        _animState = 0;
    }
}
