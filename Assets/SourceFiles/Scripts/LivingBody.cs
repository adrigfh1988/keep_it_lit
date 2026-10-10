using System.Collections;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F107 D4: a body that is not dead. No new art: it is one of the maze's existing hanging bodies (Pack_HangingBody ceiling props) or floor
/// corpses (the FallenRunner set piece's DeadBody LITE bodies), with this component added by MazeGenerator.BuildEntities.
/// It pins the player and screams; the hunter, called by the noise, does the rest. It never kills.
///
/// Hanging: drops on you if you pass under it unlit (the torch beam not on it and no lamp on it). Shining the torch up at it is always safe.
/// Floor: crawls toward you in jerky slides, but only while the torch beam is on it (the inverse of Stan); the first move is a subtle tell
/// followed by a pause so you can look away; at grabRadius it grabs your ankle. After it has acted it lies still for good.
/// Decoy: once any living body has acted in this descent (DescentRun.BodiesWoke) every level also gives one or two other bodies a harmless twitch
/// about once a minute while unobserved, so the player can never tell again which one is real. Spec: plannings/descent-mode-spec.md D4.
/// </summary>
public class LivingBody : MazeEntity
{
    /// <summary>Which kind of body this is.</summary>
    public enum BodyMode { Hanging, Floor }

    [Header("F107 D4 - Both modes")]
    [Tooltip("Seconds the player is pinned after a drop or a grab (the camera is pulled onto the body)")]
    [SerializeField, Min(0.1f)] private float pinSeconds = 1.6f;
    [Tooltip("The scream is a noise of this radius (m): the hunter comes to look")]
    [SerializeField, Min(0f)] private float noiseRadius = 30f;
    [Tooltip("A lamp lighting the body at least this much counts as lit (LightPool exposure)")]
    [SerializeField, Range(0f, 1f)] private float litExposure = 0.25f;
    [Tooltip("Peak view jitter when it acts (degrees, before the Camera shake setting)")]
    [SerializeField, Min(0f)] private float shakeDegrees = 1.2f;

    [Header("F107 D4 - Hanging")]
    [Tooltip("The drop triggers when the player passes within this flat distance of the body (m)")]
    [SerializeField, Min(0.2f)] private float triggerRadius = 1.2f;
    [Tooltip("Seconds the body takes to fall to the floor")]
    [SerializeField, Min(0.05f)] private float fallSeconds = 0.3f;
    [Tooltip("The rope bone's length after the snap (m): a short stub")]
    [SerializeField, Min(0f)] private float snappedRopeLength = 0.3f;
    [Tooltip("How far the body's pivot rests above the floor once it has fallen (m)")]
    [SerializeField, Min(0f)] private float restHeight = 0.25f;

    [Header("F107 D4 - Floor corpse")]
    [Tooltip("Length of one jerky slide (m, x = min, y = max)")]
    [SerializeField] private Vector2 stepLength = new Vector2(0.4f, 0.8f);
    [Tooltip("Seconds between slides while lit (x = min, y = max). With the default lengths: about 1 m/s on average")]
    [SerializeField] private Vector2 stepInterval = new Vector2(0.4f, 0.8f);
    [Tooltip("A slide takes this long (s)")]
    [SerializeField, Min(0.02f)] private float slideSeconds = 0.12f;
    [Tooltip("Within this flat distance of an unhidden player it grabs their ankle (m)")]
    [SerializeField, Min(0.2f)] private float grabRadius = 1f;
    [Tooltip("After the first-move tell it stays still this long (s), so the player can notice and look away")]
    [SerializeField, Min(0f)] private float tellPauseSeconds = 1.5f;
    [Tooltip("The first-move tell shifts the body this far (m)")]
    [SerializeField, Min(0f)] private float tellTwitchMeters = 0.02f;

    [Header("F107 D4 - Decoy twitch")]
    [Tooltip("Seconds between two harmless twitches (x = min, y = max)")]
    [SerializeField] private Vector2 twitchInterval = new Vector2(45f, 75f);
    [Tooltip("A hanging decoy turns by this many degrees")]
    [SerializeField, Min(0f)] private float twitchDegrees = 3f;
    [Tooltip("A floor decoy shifts by this many metres")]
    [SerializeField, Min(0f)] private float twitchMeters = 0.02f;
    [Tooltip("A decoy twitches only while it is outside the player's view: the half-angle of the view cone tested (degrees)")]
    [SerializeField, Range(10f, 90f)] private float observedHalfAngle = 50f;

    private const float PathSampleRadius = 1.5f;
    private const float DecoyRetrySeconds = 3f;
    private const float DecoyObservedDistance = 40f;
    private const float CreakDistance = 8f;

    private BodyMode _mode;
    private bool _decoy;
    private bool _setUp;
    private bool _configured;
    private bool _done;
    private bool _stepping;
    private bool _told;
    private float _noMoveUntil;
    private float _nextStep;
    private float _nextTwitch;

    private HangingBodyFit _fit;
    private Transform _bodyNode;
    private Renderer _bodyRenderer;
    // Created on first use: NavMeshPath's constructor is not allowed in a field initializer (Unity returns an unusable object; Stalker does the same).
    private NavMeshPath _path;

    public override EntityKind Kind => EntityKind.LivingBody;

    public override char DebugGlyph => _decoy ? 'd' : 'b';

    /// <summary>Hanging or Floor.</summary>
    public BodyMode Mode => _mode;

    /// <summary>True for a harmless look-alike (never pins, never drops).</summary>
    public bool IsDecoy => _decoy;

    /// <summary>True once it has acted (dropped or grabbed): it lies still for good.</summary>
    public bool HasActed => _done;

    /// <summary>The total distance a floor corpse has crawled (m), for the smoke test.</summary>
    public float CrawledMeters { get; private set; }

    /// <summary>
    /// Called by MazeGenerator.BuildEntities right after adding the component: which kind of body this prop is and whether it is the real one
    /// or a decoy. Finds the figure that is actually showing (a prop with look-alike models has exactly one active). False if there is none.
    /// </summary>
    public bool Setup(BodyMode mode, bool decoy)
    {
        _mode = mode;
        _decoy = decoy;

        if (mode == BodyMode.Hanging)
        {
            foreach (HangingBodyFit fit in GetComponentsInChildren<HangingBodyFit>(false))
            {
                _fit = fit;
                break;
            }

            _bodyNode = _fit != null ? _fit.Body : null;
        }
        else
        {
            Transform bodies = transform.Find("Bodies");
            if (bodies != null)
            {
                foreach (Transform child in bodies)
                {
                    if (!child.gameObject.activeInHierarchy) continue;
                    _bodyNode = child;
                    break;
                }
            }
        }

        _bodyRenderer = _bodyNode != null ? _bodyNode.GetComponentInChildren<Renderer>() : null;
        _setUp = _bodyNode != null && _bodyRenderer != null;
        return _setUp;
    }

    public override void Configure(MazeGenerator maze, Transform player, AIFollower hunter)
    {
        BindSystems(maze, player, hunter);
        if (!_setUp)
        {
            Debug.LogWarning("LivingBody: no figure found to animate - it stays inert.", this);
            return;
        }

        _nextTwitch = Time.time + Random.Range(twitchInterval.x * 0.3f, twitchInterval.y);
        _configured = true;
    }

    /// <summary>Where the figure's torso is, from its renderer's bounds (the hanging pose and the lying pose both centre there).</summary>
    private Vector3 BodyPoint => _bodyRenderer != null ? _bodyRenderer.bounds.center : transform.position;

    private void Update()
    {
        if (!_configured || _done || !CanAct) return;

        if (_decoy) TickDecoy();
        else if (_mode == BodyMode.Hanging) TickHanging();
        else TickFloor();
    }

    private bool IsLit(Vector3 point)
    {
        if (Torch != null && Torch.IlluminatesPoint(point, transform)) return true;
        return LightPool.ExposureAt(point) >= litExposure;
    }

    // ---------------------------------------------------------------- hanging

    private void TickHanging()
    {
        if (Stealth == null || Stealth.Hidden) return;

        Vector3 point = BodyPoint;
        Vector3 under = point - Player.position;
        under.y = 0f;
        if (under.magnitude > triggerRadius) return;

        // Lit, it never drops: shining the torch up at a hanging body is always the safe answer.
        if (IsLit(point)) return;
        StartCoroutine(DropRoutine());
    }

    private IEnumerator DropRoutine()
    {
        Act();
        _done = true; // it lies still for good afterwards
        Transform body = _bodyNode;
        Vector3 startPosition = body.position;
        Quaternion startRotation = body.rotation;

        Vector3 toPlayer = Player.position - startPosition;
        toPlayer.y = 0f;
        Vector3 fall = toPlayer.sqrMagnitude > 0.01f ? toPlayer.normalized : transform.forward;
        Vector3 endPosition = new Vector3(startPosition.x, Player.position.y + restHeight, startPosition.z);
        Quaternion endRotation = Quaternion.AngleAxis(80f, Vector3.Cross(Vector3.down, fall)) * startRotation;

        // The rope snaps: it is cut down to a stub and the collider that filled the hanging space goes.
        Vector3 rope = _fit.Rope != null ? _fit.Rope.localPosition : Vector3.zero;
        if (_fit.Rope != null) _fit.Rope.localPosition = new Vector3(rope.x, snappedRopeLength, rope.z);
        if (_fit.Box != null) _fit.Box.enabled = false;

        Vector3 lookAt = BodyPoint;
        LogEvent($"drop: pinned for {pinSeconds:0.0} s");
        StartCoroutine(PinPlayer(lookAt, pinSeconds));

        for (float t = 0f; t < fallSeconds; t += Time.deltaTime)
        {
            float k = t / fallSeconds;
            k *= k; // gravity: slow then fast
            body.SetPositionAndRotation(Vector3.Lerp(startPosition, endPosition, k), Quaternion.Slerp(startRotation, endRotation, k));
            yield return null;
        }

        body.SetPositionAndRotation(endPosition, endRotation);
        Impact(body.position);
    }

    // ---------------------------------------------------------------- floor corpse

    private void TickFloor()
    {
        if (_stepping || Stealth == null) return;

        // It moves only while the torch beam is on it, and stops dead the moment the beam leaves.
        Vector3 point = BodyPoint;
        if (Torch == null || !Torch.IlluminatesPoint(point, transform)) return;

        if (!_told)
        {
            _told = true;
            StartCoroutine(TellRoutine());
            return;
        }

        if (Time.time < _noMoveUntil || Time.time < _nextStep) return;

        if (!Stealth.Hidden && FlatDistanceToPlayer(_bodyNode.position) <= grabRadius)
        {
            Grab();
            return;
        }

        StartCoroutine(StepRoutine());
    }

    /// <summary>The first time the beam finds it: one drag and a 2 cm twitch, then stillness, so a careful player can notice and look away.</summary>
    private IEnumerator TellRoutine()
    {
        Act();
        _noMoveUntil = Time.time + tellPauseSeconds;
        PlayFirst(_bodyNode.position, 0.6f, 0.9f, GameAudio.Library.farDrag);
        LogEvent("tell: one drag and a twitch");
        yield return SlideTo(_bodyNode.position + _bodyNode.forward * tellTwitchMeters, slideSeconds, count: false);
    }

    /// <summary>One jerky slide toward the player along the NavMesh, with a wet drag sound.</summary>
    private IEnumerator StepRoutine()
    {
        _stepping = true;
        try
        {
            Vector3 from = _bodyNode.position;
            if (TryNextStepTarget(from, out Vector3 target))
            {
                Vector3 heading = target - from;
                heading.y = 0f;
                if (heading.sqrMagnitude > 0.0001f)
                {
                    // Head-first: the DeadBody LITE skeleton runs from the feet at +Z to the head at -Z.
                    _bodyNode.rotation = Quaternion.LookRotation(-heading.normalized, Vector3.up);
                }

                PlayFirst(from, 0.55f, Random.Range(0.85f, 1.1f), GameAudio.Library.farDrag);
                yield return SlideTo(target, slideSeconds, count: true);
                LogEvent($"step to {target.x:0.00},{target.z:0.00}, crawled {CrawledMeters:0.00} m");
            }
        }
        finally
        {
            _nextStep = Time.time + Random.Range(stepInterval.x, stepInterval.y);
            _stepping = false; // never left set, even if a step throws
        }
    }

    private IEnumerator SlideTo(Vector3 target, float seconds, bool count)
    {
        Vector3 start = _bodyNode.position;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            _bodyNode.position = Vector3.Lerp(start, target, t / seconds);
            yield return null;
        }

        _bodyNode.position = target;
        if (count) CrawledMeters += Vector3.Distance(start, target);
    }

    /// <summary>The next point of a slide: a step along the NavMesh path to the player (the lying body is carved out of the mesh, so the start is snapped first).</summary>
    private bool TryNextStepTarget(Vector3 from, out Vector3 target)
    {
        target = from;
        if (_path == null) _path = new NavMeshPath();
        if (!NavMesh.SamplePosition(from, out NavMeshHit start, PathSampleRadius, NavMesh.AllAreas)) return false;
        if (!NavMesh.SamplePosition(Player.position, out NavMeshHit end, PathSampleRadius, NavMesh.AllAreas)) return false;
        if (!NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, _path) || _path.corners.Length < 2) return false;

        Vector3[] corners = _path.corners;
        Vector3 corner = corners[1];
        int i = 1;
        while (i < corners.Length - 1 && (corner - start.position).sqrMagnitude < 0.04f) corner = corners[++i];

        Vector3 direction = corner - start.position;
        direction.y = 0f;
        float length = Mathf.Min(Random.Range(stepLength.x, stepLength.y), direction.magnitude);
        if (length < 0.02f) return false;

        Vector3 point = from + direction.normalized * length;
        if (!Maze.AllowsLivingBodyAt(point)) return false; // never into a locker, squeeze or loop cell
        target = point;
        return true;
    }

    private void Grab()
    {
        StartCoroutine(GrabRoutine());
    }

    private IEnumerator GrabRoutine()
    {
        Act();
        _done = true;
        Vector3 at = _bodyNode.position;
        LogEvent($"grab: pinned for {pinSeconds:0.0} s");
        StartCoroutine(PinPlayer(BodyPoint, pinSeconds));
        Impact(at);
        yield break;
    }

    // ---------------------------------------------------------------- shared

    /// <summary>The first thing a real body does (its tell, its drop, its grab) flags the descent: from here on, decoys twitch in every level.</summary>
    private void Act()
    {
        DescentRun.BodiesWoke = true;
    }

    /// <summary>The scream, the heavy sound, the noise the hunter comes to, the shake.</summary>
    private void Impact(Vector3 at)
    {
        GameAudioLibrary lib = GameAudio.Library;
        if (_mode == BodyMode.Hanging) PlayFirst(at, 1f, 0.9f, lib.liftThud, lib.liftBang, lib.shutterSlam);
        PlayFirst(at + Vector3.up * 0.5f, 1f, 1f, lib.farScream, lib.caught);
        Hunter?.HearNoise(at, noiseRadius);
        PulseDread();
        StartCoroutine(ShakeView(0.3f, shakeDegrees));
    }

    // ---------------------------------------------------------------- decoy

    /// <summary>A harmless twitch about once a minute, only while the player cannot see it and the beam is not on it. Never pins, never makes noise beyond a faint creak.</summary>
    private void TickDecoy()
    {
        if (!DescentRun.BodiesWoke || Time.time < _nextTwitch) return;

        Vector3 point = BodyPoint;
        bool observed = IsInViewCone(point, observedHalfAngle, DecoyObservedDistance) || (Torch != null && Torch.IlluminatesPoint(point, transform));
        if (observed)
        {
            _nextTwitch = Time.time + DecoyRetrySeconds;
            return;
        }

        _nextTwitch = Time.time + Random.Range(twitchInterval.x, twitchInterval.y);
        StartCoroutine(TwitchRoutine());
    }

    private IEnumerator TwitchRoutine()
    {
        LogEvent("decoy twitch");
        if (FlatDistanceToPlayer(_bodyNode.position) <= CreakDistance) GameAudio.PlayAt(GameAudio.Library.floorCreak, _bodyNode.position, 0.15f, 1f, CreakDistance);

        if (_mode == BodyMode.Hanging)
        {
            Quaternion start = _bodyNode.rotation;
            Quaternion end = Quaternion.AngleAxis(twitchDegrees, Vector3.up) * start;
            for (float t = 0f; t < 0.5f; t += Time.deltaTime)
            {
                _bodyNode.rotation = Quaternion.Slerp(start, end, t / 0.5f);
                yield return null;
            }

            _bodyNode.rotation = end;
        }
        else
        {
            yield return SlideTo(_bodyNode.position + _bodyNode.forward * twitchMeters, slideSeconds, count: false);
        }
    }
}
