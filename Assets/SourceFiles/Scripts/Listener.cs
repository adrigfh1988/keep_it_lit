using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F107 D2: the Listener, the second killer. Blind: it ignores the torch and every lamp, and only ever moves toward sound. The opposite of
/// the hunter (sight) and of Stan (darkness), and a pair for the throwables and noisy floors the maze already has. Never named in the world;
/// the descent end screen calls its cause HEARD. It does not use AIFollower or AIPresence: it has its own NavMeshAgent on the maze surface's
/// default agent type and slips under shutters like Stan (a silent DoorBreakers entry).
///
/// Dormant (lying still, breathing) -> Drawn (crawls to the last heard spot) -> Sniff (4 s on the spot, then lies down where it is) and, when a
/// sound is within lungeRange, Lunge (a 0.6 s scream, then a fast run at the spot it heard - it does not track, so freezing makes it overshoot).
/// Sounds are the player's own noise radius (a hidden player makes none) and every MazeNoise event. Within killRadius of an unhidden player it
/// ends the run through GameOutcome.CaughtByEntity. Spec: plannings/descent-mode-spec.md D2.
/// </summary>
public class Listener : MazeEntity
{
    /// <summary>What it is doing. Public so the smoke test and DebugMap can read it.</summary>
    public enum Mode { Dormant, Drawn, Sniff, Lunge }

    [Header("F107 D2 - Hearing")]
    [Tooltip("Multiplies the player's own noise radius (walking 5 m, sprinting 15 m). World noises are not scaled")]
    [SerializeField, Min(0f)] private float hearingScale = 1f;
    [Tooltip("The player's noise radius is multiplied by this when a wall is between them and the Listener")]
    [SerializeField, Range(0f, 1f)] private float wallMuffle = 0.6f;
    [Tooltip("It neither wakes nor kills during this many seconds after the level's run starts (s)")]
    [SerializeField, Min(0f)] private float graceSeconds = 15f;

    [Header("F107 D2 - Movement")]
    [Tooltip("Crawl speed toward a heard spot (m/s). Slower than the player's walk")]
    [SerializeField, Min(0.1f)] private float crawlSpeed = 1.4f;
    [Tooltip("Run speed of a Lunge (m/s)")]
    [SerializeField, Min(0.1f)] private float lungeSpeed = 4.5f;
    [Tooltip("The scream before a Lunge (s). The player always gets this warning")]
    [SerializeField, Min(0.1f)] private float lungeScreamSeconds = 0.6f;
    [Tooltip("A Lunge ends after this long at the latest (s)")]
    [SerializeField, Min(0.1f)] private float lungeMaxSeconds = 2f;
    [Tooltip("A heard spot this close (m) while Drawn or Sniffing starts a Lunge")]
    [SerializeField, Min(0.1f)] private float lungeRange = 4f;
    [Tooltip("Minimum seconds between two Lunges")]
    [SerializeField, Min(0f)] private float lungeCooldown = 6f;
    [Tooltip("A Lunge aims this far past the heard spot (m), so a player who froze is overshot")]
    [SerializeField, Min(0f)] private float lungeOvershoot = 1.5f;
    [Tooltip("Seconds spent sniffing at the spot before it lies down where it is")]
    [SerializeField, Min(0.1f)] private float sniffSeconds = 4f;
    [Tooltip("It counts as arrived this close to the heard spot (m)")]
    [SerializeField, Min(0.1f)] private float arriveDistance = 0.6f;
    [Tooltip("Within this flat distance of an unhidden player it kills (m)")]
    [SerializeField, Min(0.1f)] private float killRadius = 1f;
    [Tooltip("The scream is a noise of this radius (m): the hunter comes to see")]
    [SerializeField, Min(0f)] private float screamNoiseRadius = 25f;

    [Header("F107 D2 - Audio")]
    [Tooltip("The breathing loop is audible within this distance (m) in every state")]
    [SerializeField, Min(1f)] private float breathAudibleRange = 10f;
    [SerializeField, Range(0f, 1f)] private float breathVolume = 0.45f;
    [Tooltip("Pitch of the borrowed locker-breath clip: low, so it is not the player's own breathing")]
    [SerializeField, Range(0.3f, 1.2f)] private float breathPitch = 0.65f;

    [Header("F107 D2 - Animator states (Animation/Descent/Listener.controller)")]
    [SerializeField] private string dormantState = "Dormant";
    [SerializeField] private string crawlState = "Crawl";
    [SerializeField] private string idleState = "Idle";
    [SerializeField] private string screamState = "Scream";
    [SerializeField] private string runState = "RunCrawl";

    private const float RepathInterval = 0.25f;
    private const float RepathDistance = 0.5f;
    private const float TurnDegreesPerSecond = 240f;
    private const float StateFadeSeconds = 0.2f;
    private const float SnapRadius = 2f;
    private const float KillHeight = 1.6f;
    private const float EyeHeight = 0.6f;
    private const float MaxGroundOffset = 1.2f;
    private const float GroundRate = 2.5f; // m/s the body is eased up or down to stay on the floor

    private NavMeshAgent _agent;
    private Animator _animator;
    private Transform _body;
    private Transform _head;
    private Renderer[] _renderers;
    private bool _configured;
    private bool _subscribed;

    private Mode _mode = Mode.Dormant;
    private int _currentStateHash;
    private Vector3 _target;
    private Vector3 _lungeTarget;
    private float _nextRepath;
    private float _sniffUntil;
    private float _screamUntil;
    private float _lungeEndAt;
    private float _nextLungeAt;
    private float _dormantUntil;
    private float _nextBreathAt;
    private bool _groundedOnce;

    public override EntityKind Kind => EntityKind.Listener;

    public override bool IsKiller => true;

    public override char DebugGlyph => 'l';

    /// <summary>What it is doing right now.</summary>
    public Mode CurrentMode => _mode;

    /// <summary>The last spot it heard something (where a Drawn or Sniffing Listener is headed).</summary>
    public Vector3 HeardPosition => _target;

    /// <summary>Where a kill looks: the head bone, never a child placed at build time (the rest pose of these bodies lies down).</summary>
    public Vector3 HeadPosition => _head != null ? _head.position : transform.position + Vector3.up * EyeHeight;

    public override void Configure(MazeGenerator maze, Transform player, AIFollower hunter)
    {
        BindSystems(maze, player, hunter);

        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();
        if (_agent == null || _animator == null)
        {
            Debug.LogWarning("Listener: needs a NavMeshAgent (BuildEntities adds it) and an Animator - it stays inert.", this);
            return;
        }

        _body = _animator.transform;
        _animator.fireEvents = false; // the clips carry no events for us
        _animator.applyRootMotion = false;
        _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        if (_animator.isHuman) _head = _animator.GetBoneTransform(HumanBodyBones.Head);

        // Bounds follow the animated pose only with this on; the lying pose is grounded from them once.
        _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in _renderers)
        {
            if (r is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        }

        _agent.speed = crawlSpeed;
        DoorBreakers.Register(transform, _agent, true, null); // silent: it slips under a shutter, like Stan
        _configured = true;
        Subscribe();
        _target = transform.position;
        SetMode(Mode.Dormant, force: true);
        _nextBreathAt = Time.time + Random.Range(0.5f, 2f);
    }

    private void OnEnable() => Subscribe();

    private void OnDisable()
    {
        if (!_subscribed) return;
        _subscribed = false;
        MazeNoise.Emitted -= HandleNoise;
    }

    private void OnDestroy() => DoorBreakers.Unregister(transform);

    private void Subscribe()
    {
        if (_subscribed || !_configured) return;
        _subscribed = true;
        MazeNoise.Emitted += HandleNoise;
    }

    /// <summary>Second Wind's release: far from the player, lying still, deaf for `dormantSeconds`.</summary>
    public override void WarpAway(float dormantSeconds)
    {
        if (!_configured) return;

        _dormantUntil = Time.time + dormantSeconds;
        Vector3 spot = Maze.FarthestCellCenterFrom(Player.position);
        if (NavMesh.SamplePosition(spot, out NavMeshHit hit, 3f, NavMesh.AllAreas)) spot = hit.position;
        _agent.Warp(spot);
        _target = spot;
        _nextLungeAt = Time.time + lungeCooldown;
        SetMode(Mode.Dormant, force: true);
        LogEvent($"warped away to {spot} for {dormantSeconds:0} s");
    }

    private void Update()
    {
        if (!_configured || !_agent.isOnNavMesh) return;

        if (!CanAct)
        {
            _agent.isStopped = true;
            return;
        }

        bool grace = Time.time - GameFlow.RunStartTime < graceSeconds;
        if (!grace && Time.time >= _dormantUntil) ListenForPlayer();

        switch (_mode)
        {
            case Mode.Drawn: TickDrawn(); break;
            case Mode.Sniff: TickSniff(); break;
            case Mode.Lunge: TickLunge(); break;
            default: _agent.isStopped = true; break;
        }

        UpdateBreath();
        if (!grace) TryKill();
    }

    // ---------------------------------------------------------------- hearing

    /// <summary>The player's own noise: their radius times hearingScale, shrunk by wallMuffle behind a wall. A hidden player makes none.</summary>
    private void ListenForPlayer()
    {
        if (Stealth == null || Stealth.Hidden) return;

        float radius = Stealth.NoiseRadius * hearingScale;
        if (radius <= 0.01f) return;

        float distance = FlatDistanceToPlayer(transform.position);
        if (distance > radius) return; // cheap out: the muffle can only shrink the radius further

        Vector3 from = transform.position + Vector3.up * EyeHeight;
        if (!HasClearLine(from, Player.position + Vector3.up, null)) radius *= wallMuffle;
        if (distance <= radius) Hear(Player.position);
    }

    private void HandleNoise(Vector3 position, float radius)
    {
        if (!_configured || !CanAct || Time.time < _dormantUntil) return;
        if (Time.time - GameFlow.RunStartTime < graceSeconds) return;

        Vector3 d = position - transform.position;
        d.y = 0f;
        if (d.magnitude <= radius) Hear(position);
    }

    /// <summary>It heard something at `at`: Drawn toward it, or - when it is close and the cooldown allows - a Lunge. A Lunge in progress ignores everything.</summary>
    private void Hear(Vector3 at)
    {
        if (_mode == Mode.Lunge) return;

        Vector3 d = at - transform.position;
        d.y = 0f;
        if ((_mode == Mode.Drawn || _mode == Mode.Sniff) && d.magnitude <= lungeRange && Time.time >= _nextLungeAt)
        {
            StartLunge(at);
            return;
        }

        _target = at;
        if (_mode != Mode.Drawn) SetMode(Mode.Drawn);
        else if ((at - _agent.destination).sqrMagnitude > RepathDistance * RepathDistance && Time.time >= _nextRepath) GoTo(at);
    }

    // ---------------------------------------------------------------- states

    private void TickDrawn()
    {
        _agent.isStopped = false;
        if (_agent.pathPending) return;

        Vector3 d = _target - transform.position;
        d.y = 0f;
        bool arrived = d.magnitude <= arriveDistance || (_agent.remainingDistance <= arriveDistance && _agent.hasPath)
                       || _agent.pathStatus == NavMeshPathStatus.PathInvalid;
        if (arrived) SetMode(Mode.Sniff);
    }

    private void TickSniff()
    {
        _agent.isStopped = true;
        FaceTowards(_target);
        if (Time.time >= _sniffUntil) SetMode(Mode.Dormant); // it does not go home: it lies down where it is
    }

    private void TickLunge()
    {
        if (Time.time < _screamUntil)
        {
            _agent.isStopped = true;
            FaceTowards(_lungeTarget);
            return;
        }

        if (_agent.isStopped)
        {
            // The scream is over: run at the spot it heard.
            _agent.isStopped = false;
            _agent.speed = lungeSpeed;
            GoTo(LungeDestination());
            Play(runState);
            LogEvent("Lunge: running");
        }

        bool arrived = !_agent.pathPending && _agent.remainingDistance <= arriveDistance * 0.5f;
        if (arrived || Time.time >= _lungeEndAt)
        {
            _nextLungeAt = Time.time + lungeCooldown;
            SetMode(Mode.Sniff);
        }
    }

    private void StartLunge(Vector3 at)
    {
        _lungeTarget = at;
        _target = at;
        _screamUntil = Time.time + lungeScreamSeconds;
        _lungeEndAt = _screamUntil + lungeMaxSeconds;
        SetMode(Mode.Lunge);
        _agent.isStopped = true; // TickLunge starts the run once the scream is over
        Play(screamState);

        PlayFirst(transform.position + Vector3.up, 1f, 1f, GameAudio.Library.farScream, GameAudio.Library.caught);
        PulseDread();
        Hunter?.HearNoise(transform.position, screamNoiseRadius); // MazeNoise: Lunge ignores it, so it never hears itself
        LogEvent($"Lunge: scream, target {at}");
    }

    private Vector3 LungeDestination()
    {
        Vector3 toTarget = _lungeTarget - transform.position;
        toTarget.y = 0f;
        Vector3 past = _lungeTarget + (toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward) * lungeOvershoot;
        return NavMesh.SamplePosition(past, out NavMeshHit hit, SnapRadius, NavMesh.AllAreas) ? hit.position : _lungeTarget;
    }

    private void SetMode(Mode mode, bool force = false)
    {
        if (!force && mode == _mode) return;

        Mode previous = _mode;
        _mode = mode;
        switch (mode)
        {
            case Mode.Dormant:
                _agent.isStopped = true;
                _agent.ResetPath();
                Play(dormantState);
                break;
            case Mode.Drawn:
                _agent.speed = crawlSpeed;
                _agent.isStopped = false;
                GoTo(_target);
                Play(crawlState);
                break;
            case Mode.Sniff:
                _agent.isStopped = true;
                _agent.ResetPath();
                _sniffUntil = Time.time + sniffSeconds;
                Play(idleState);
                break;
        }

        if (mode != Mode.Lunge) _agent.speed = crawlSpeed;
        if (previous != mode) LogEvent($"{previous} -> {mode}");
    }

    private void GoTo(Vector3 point)
    {
        _nextRepath = Time.time + RepathInterval;
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, SnapRadius, NavMesh.AllAreas)) _agent.SetDestination(hit.position);
        else _agent.SetDestination(point);
    }

    private void FaceTowards(Vector3 point)
    {
        Vector3 d = point - transform.position;
        d.y = 0f;
        if (d.sqrMagnitude < 0.01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(d), TurnDegreesPerSecond * Time.deltaTime);
    }

    // ---------------------------------------------------------------- the kill

    private void TryKill()
    {
        if (_mode == Mode.Dormant || Stealth == null || Stealth.Hidden || Outcome == null || Time.time < _dormantUntil) return;

        Vector3 d = Player.position - transform.position;
        if (Mathf.Abs(d.y) > KillHeight) return;
        d.y = 0f;
        if (d.magnitude > killRadius) return;

        LogEvent($"caught the player in {_mode}");
        Outcome.CaughtByEntity(HeadPosition, () => WarpAway(10f), EntityKind.Listener);
    }

    // ---------------------------------------------------------------- animation and sound

    private void Play(string state)
    {
        if (_animator == null || string.IsNullOrEmpty(state)) return;

        int hash = Animator.StringToHash(state);
        if (hash == _currentStateHash) return;

        _currentStateHash = hash;
        if (_animator.HasState(0, hash)) _animator.CrossFadeInFixedTime(hash, StateFadeSeconds);
    }

    /// <summary>
    /// Every clip sits at its own height above the agent's feet point (the Mixamo crawl lifts the body about half a metre, the lying clip
    /// sinks it), so after the Animator has posed the body each frame, the body is moved up or down until the lowest point of its renderers
    /// rests on the floor. Eased, so a crossfade between two clips never pops; capped at <see cref="MaxGroundOffset"/>.
    /// </summary>
    private void LateUpdate()
    {
        if (_body == null || _renderers == null) return;

        float lowest = float.PositiveInfinity;
        foreach (Renderer r in _renderers)
        {
            if (r != null && r.enabled && r is SkinnedMeshRenderer) lowest = Mathf.Min(lowest, r.bounds.min.y);
        }
        if (float.IsInfinity(lowest)) return;

        float parentScale = _body.parent != null ? Mathf.Max(0.01f, _body.parent.lossyScale.y) : 1f;
        float offset = _body.localPosition.y + (transform.position.y - lowest) / parentScale;
        offset = Mathf.Clamp(offset, -MaxGroundOffset, MaxGroundOffset);
        float y = _groundedOnce ? Mathf.MoveTowards(_body.localPosition.y, offset, GroundRate * Time.deltaTime) : offset;
        _groundedOnce = true;
        _body.localPosition = new Vector3(0f, y, 0f);
    }

    /// <summary>The breathing loop: the locker-breath recording, pitched down, within breathAudibleRange in every state.</summary>
    private void UpdateBreath()
    {
        if (Time.time < _nextBreathAt) return;

        AudioSlot slot = GameAudio.Library.lockerBreath.Has ? GameAudio.Library.lockerBreath : GameAudio.Library.floorCreak;
        _nextBreathAt = Time.time + 1f;
        if (!slot.Has || FlatDistanceToPlayer(transform.position) > breathAudibleRange * 1.5f) return;

        AudioSource source = GameAudio.SpawnAt(slot, transform.position + Vector3.up * 0.3f, breathVolume, breathPitch, breathAudibleRange);
        if (source == null) return;

        source.transform.SetParent(transform, true);
        float length = source.clip.length / Mathf.Max(0.1f, source.pitch);
        _nextBreathAt = Time.time + length + Random.Range(0.3f, 1f);
    }
}
