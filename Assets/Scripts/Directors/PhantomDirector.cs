using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

/// <summary>
/// F26: scheduled, weighted, gameplay-inert fakes - a silhouette that vanishes in the torch beam,
/// footsteps that stop a beat after you do, a lamp dying beside you, a whisper in one ear. None of them
/// carry a collider, raise a noise event, or touch the hunter's state; a fake must never mask a real cue
/// that is about to matter, which is why they refuse to fire near the hunter or during a relocation
/// telegraph (DreadDirector.IsTelegraphing).
///
/// F103 A2/A8: phantoms are contaminated so they stop proving the hunter is far away - audio ones may fire
/// closer to it, some footsteps are hunter-pitched, and an "honest" one ends by really relocating the
/// hunter (DreadDirector.TryRelocateOnTrail, the single exception to "never touches the hunter"). Wrong
/// sounds (the game's own sounds from somewhere unseen) never fake an objective and never raise a noise.
///
/// No execution-order attribute - everything it needs is handed in through Configure, after MazeGenerator
/// has built the world. Added at runtime, so OnEnable runs during AddComponent, before Configure.
/// </summary>
public partial class PhantomDirector : MonoBehaviour
{
    private enum PhantomType
    {
        Silhouette,
        Footsteps,
        LampBlink,
        Whisper,
        WrongSound,
        Shadow,
        Peripheral,
        AttachedPresence
    }

    [Header("Scheduling")]
    [Tooltip("Mean seconds between phantoms at full progress. Overridden per floor by FloorProfile.PhantomInterval when a profile is bound.")]
    [SerializeField] private float phantomInterval = 18f;
    [Tooltip("Minimum seconds between two phantoms, even if the roll comes up short")]
    [SerializeField] private float minSpacing = 8f;
    [Tooltip("No visual phantom fires while the hunter is this close or closer (m) - a fake must not mask something real. Audio phantoms use audioHunterExclusionDistance while contaminated phantoms are on.")]
    [SerializeField] private float hunterExclusionDistance = 12f;
    [Tooltip("F103 A2: audio phantoms (footsteps, whisper, wrong sounds) may fire this close to the hunter (m) - so a phantom no longer proves it is far away")]
    [SerializeField] private float audioHunterExclusionDistance = 6f;
    [Tooltip("A skipped check is retried after interval x this fraction, rather than waiting a full interval")]
    [SerializeField] private float failRetryFraction = 0.5f;

    [Header("F104 B1 - dread pacing")]
    [Tooltip("Interval multiplier during a DreadMeter backoff: phantoms crowd in while the hunter is away")]
    [SerializeField] private float backoffIntervalScale = 1f / 3f;
    [Tooltip("Dread above which phantoms ease off, so a terrified player is not piled on")]
    [SerializeField] private float crowdedDread = 0.8f;
    [Tooltip("Interval multiplier above crowdedDread")]
    [SerializeField] private float crowdedIntervalScale = 1.5f;

    [Header("Weights (relative, need not sum to anything in particular)")]
    [SerializeField] private float silhouetteWeight = 3f;
    [SerializeField] private float footstepsWeight = 3f;
    [SerializeField] private float lampBlinkWeight = 2f;
    [SerializeField] private float whisperWeight = 2f;
    [Tooltip("F103 A8: weight of the wrong-sound phantom (the game's own sounds from somewhere unseen)")]
    [SerializeField] private float wrongSoundWeight = 2f;
    [Tooltip("Multiplier on the silhouette's weight while the torch is off - then only the eyes are visible")]
    [SerializeField] private float silhouetteTorchOffMultiplier = 2f;

    [Header("Silhouette")]
    [SerializeField] private float silhouetteMinDistance = 10f;
    [SerializeField] private float silhouetteMaxDistance = 22f;
    [Tooltip("How close to the hunter a silhouette cell may not be (m)")]
    [SerializeField] private float silhouetteHunterExclusion = 4f;
    [SerializeField] private float silhouetteLifetimeMin = 0.8f;
    [SerializeField] private float silhouetteLifetimeMax = 1.6f;
    [Tooltip("Torch beam catches it inside this fraction of the beam's outer angle")]
    [SerializeField] private float silhouetteBeamAngleFraction = 0.45f;
    [Tooltip("...and inside this fraction of the beam's range")]
    [SerializeField] private float silhouetteBeamRangeFraction = 0.9f;

    [Header("Footsteps behind you")]
    [SerializeField] private float footstepDistanceBehind = 2.5f;
    [SerializeField] private float footstepStrideLength = 1.4f;
    [SerializeField] private float footstepPitch = 0.85f;
    [SerializeField] private float footstepVolume = 0.35f;
    [SerializeField] private float footstepMinDistance = 1f;
    [SerializeField] private float footstepMaxDistance = 12f;
    [SerializeField] private float footstepDurationMin = 4f;
    [SerializeField] private float footstepDurationMax = 7f;
    [Tooltip("How late the final step lands after the player stops (s) - that lateness is the beat")]
    [SerializeField] private float footstepLateStepDelay = 0.35f;

    [Header("F103 A2 - contaminated footsteps")]
    [Tooltip("Chance a footsteps phantom is pitched and weighted like the hunter's own")]
    [Range(0f, 1f)]
    [SerializeField] private float hunterPitchedChance = 0.3f;
    [Tooltip("Per-step pitch of a hunter-pitched phantom, min..max (AIPresence's own footsteps use 0.55-0.70)")]
    [SerializeField] private Vector2 hunterPitch = new Vector2(0.55f, 0.70f);
    [SerializeField] private float hunterStepVolume = 0.6f;
    [Tooltip("Chance a footsteps phantom is honest: it ends by really relocating the hunter onto the player's trail")]
    [Range(0f, 1f)]
    [SerializeField] private float honestChance = 0.15f;
    [Tooltip("An honest phantom needs the hunter to be patrolling at least this far away (m)")]
    [SerializeField] private float honestMinHunterDistance = 20f;
    [Tooltip("How far back along the trail the honest relocation puts the hunter (m)")]
    [SerializeField] private float honestRelocateDistance = 11f;

    [Header("Lamp beside you dies")]
    [SerializeField] private float lampBlinkRadius = 6f;
    [SerializeField] private float lampBlinkSeconds = 0.45f;

    [Header("F103 A2 - silhouettes leave a trace")]
    [Range(0f, 1f)]
    [SerializeField] private float silhouetteLampKillChance = 0.35f;
    [Tooltip("The nearest lamp within this of where the silhouette stood dies (m)")]
    [SerializeField] private float silhouetteLampKillRadius = 3.5f;
    [SerializeField] private float silhouetteLampFadeSeconds = 1.2f;

    [Header("F103 A8 - wrong sounds")]
    [Tooltip("A cell centre this far from the camera (min..max, m) may host a can, shutter or bottle sound")]
    [SerializeField] private Vector2 wrongSoundDistance = new Vector2(8f, 22f);
    [Tooltip("A real locker this far from the camera (min..max, m), with its front out of view, may slam")]
    [SerializeField] private Vector2 wrongSoundLockerDistance = new Vector2(6f, 25f);
    [SerializeField] private float wrongSoundVolume = 0.7f;
    [Tooltip("How far a wrong sound carries (m)")]
    [SerializeField] private float wrongSoundMaxDistance = 30f;
    [Tooltip("Low-pass cutoff for a wrong sound whose slot has none of its own, so it reads as heard through a wall (Hz)")]
    [SerializeField] private float wrongSoundLowPassHz = 2200f;
    [Tooltip("The torch click comes from this far behind the camera (m): someone else switching a torch on")]
    [SerializeField] private float torchClickBehindDistance = 0.6f;
    [SerializeField] private float torchClickVolume = 0.35f;

    [Header("F104 B3 - phantoms in the locker")]
    [Tooltip("Audio phantoms (footsteps, whispers, wrong sounds) may fire at a hidden player once they have been hidden this long (s). Visual ones stay blocked.")]
    [SerializeField] private float lockerQuietSeconds = 15f;

    [Header("F104 B7 - shadow phantom")]
    [Tooltip("Weight of the shadow phantom: a figure's shadow crosses the lit wall with no one there. Zero unless the torch is lit and shadows are on.")]
    [SerializeField] private float shadowWeight = 2f;
    [Tooltip("The torch beam must hit a wall this far away (min..max, m) for a shadow phantom to be staged")]
    [SerializeField] private Vector2 shadowWallDistance = new Vector2(4f, 10f);
    [Tooltip("The body stands this fraction of the way from the torch to the wall (min..max)")]
    [SerializeField] private Vector2 shadowStandFraction = new Vector2(0.45f, 0.6f);
    [Tooltip("It starts this far to one side of the beam and walks across to the other (m)")]
    [SerializeField] private float shadowWalkHalfWidth = 1f;
    [Tooltip("Seconds the walk across takes")]
    [SerializeField] private float shadowWalkSeconds = 1.2f;
    [Tooltip("A wall hit with a normal steeper than this (|y|) is floor or ceiling, not a wall")]
    [SerializeField] private float shadowWallMaxNormalY = 0.5f;

    [Header("F104 B8 - peripheral figure")]
    [Tooltip("Weight of the figure at the very edge of the view, gone when looked at")]
    [SerializeField] private float peripheralWeight = 2f;
    [Tooltip("A cell this far from the camera (min..max, m) may host the figure. Corridors rarely offer a visible cell this far off-axis, so keep the near end low or it will hardly ever fire.")]
    [SerializeField] private Vector2 peripheralDistance = new Vector2(4f, 14f);
    [Tooltip("Viewport x range of the outer edge strip on each side where it may stand (min..max, mirrored for the left)")]
    [SerializeField] private Vector2 peripheralEdge = new Vector2(0.82f, 0.97f);
    [Tooltip("It is removed the moment it drifts inside this viewport x range (min..max)")]
    [SerializeField] private Vector2 peripheralVanishRange = new Vector2(0.2f, 0.8f);
    [Tooltip("Camera turn rate toward its side that makes it vanish (deg/s)")]
    [SerializeField] private float peripheralTurnRate = 60f;
    [Tooltip("It never stays longer than this (s)")]
    [SerializeField] private float peripheralLifetime = 4f;
    [Tooltip("Height of the point on the body used for the viewport and torch tests (m)")]
    [SerializeField] private float figureTestHeight = 1f;

    [Header("F105 C8 - your double")]
    [Tooltip("The first floor on which a silhouette phantom may be the player's own robot instead")]
    [SerializeField, Min(1)] private int doubleFromFloor = 3;
    [Tooltip("Chance that a silhouette phantom is the double")]
    [Range(0f, 1f)]
    [SerializeField] private float doubleChance = 0.3f;
    [Tooltip("On the Stalker floors no double stands while Stan is this close to it or to the player (m): at close range it must never be mistaken for him")]
    [SerializeField] private float doubleStalkerExclusion = 15f;
    [Tooltip("How long the double stands, min..max (s); the torch beam reaching it ends it sooner")]
    [SerializeField] private Vector2 doubleLifetime = new Vector2(3f, 5f);
    [Tooltip("The spot light at its head that lights it, and the player, from where it stands")]
    [SerializeField] private float doubleSpotRange = 8f;
    [SerializeField] private float doubleSpotIntensity = 3f;
    [SerializeField] private float doubleSpotAngle = 40f;
    [SerializeField] private Color doubleSpotColor = new Color(1f, 0.93f, 0.8f);

    [Header("F128 - attached presence (P.T. tether)")]
    [Tooltip("Seconds the presence stays tethered behind the player's back (min..max)")]
    [SerializeField] private float attachedDurationMin = 8f;
    [SerializeField] private float attachedDurationMax = 15f;
    [Tooltip("Distance behind the player's back where the unseen figure walks and stands (m)")]
    [SerializeField] private float attachedDistanceBehind = 1.25f;
    [Tooltip("Radius within which a powered WallLamp behind the player casts the presence's shadow forward (m)")]
    [SerializeField] private float attachedLampShadowRadius = 6.5f;
    [Tooltip("How far ahead of the player's feet the rear lamp shadow is aimed (m)")]
    [SerializeField] private float attachedLampShadowForwardAim = 1.8f;
    [Tooltip("How strongly the unseen figure biases toward the lamp-to-forward-floor line (0..1)")]
    [Range(0f, 1f)]
    [SerializeField] private float attachedLampBias = 0.65f;
    [Tooltip("Camera yaw speed above which a turn counts as a whip-turn (deg/s)")]
    [SerializeField] private float whipTurnSpeedThreshold = 115f;
    [Tooltip("Cumulative fast-turn yaw required to catch the peripheral glimpse before it vanishes (deg)")]
    [SerializeField] private float whipTurnMinAngle = 35f;
    [Tooltip("Seconds the figure remains frozen in view during a whip-turn before vanishing (s)")]
    [SerializeField] private float whipGlimpseDuration = 0.15f;
    [Tooltip("Distance at which the whip-turn glimpse figure freezes (m)")]
    [SerializeField] private float whipGlimpseDistance = 1.65f;
    [Tooltip("Cumulative backward walking distance that bumps into the presence and triggers a startle (m)")]
    [SerializeField] private float backpedalBumpDistance = 0.75f;
    [Tooltip("DreadMeter value above which keeping the presence behind you too long triggers a startle")]
    [SerializeField] private float highDreadStartleThreshold = 0.82f;
    [Tooltip("Seconds at high dread before the attached presence triggers a close startle (s)")]
    [SerializeField] private float highDreadStartleSeconds = 5.5f;
    [Tooltip("Seconds the torch cuts out during an attached startle when torchFailures is enabled (s)")]
    [SerializeField] private float startleBlackoutSeconds = 0.85f;
    [Tooltip("Delay after the late footstep before the neck breath plays behind one ear (s)")]
    [SerializeField] private float neckBreathDelay = 0.45f;
    [Tooltip("Distance behind the player's ear for the close neck breath (m)")]
    [SerializeField] private float neckBreathDistance = 0.65f;
    [SerializeField] private float neckBreathVolume = 0.28f;

    private MazeGenerator _maze;
    private Transform _player;
    private Camera _camera;
    private AIFollower _follower;
    private HorrorAudioDirector _audio;
    private Flashlight _flashlight;
    private PlayerStealthState _stealth;
    private GameOutcome _outcome;
    private DreadDirector _dread;
    private TerrorSettings _terror = new TerrorSettings();
    private DreadMeter _meter; // F104 B1: paces the next phantom
    private AudioClip[] _footstepClips;

    private GameObject _phantomsRoot;
    private Material _silhouetteMaterial;
    private Material _eyeMaterial;
    private PhantomBody _body; // F104 B7/B8: the shared figure, built on first use
    private bool _bodyMissingLogged;
    private PhantomBody _doubleBody; // F105 C8: the player's own robot, built on first use
    private bool _doubleMissingLogged;
    private bool _attachedActive; // F128: true while an AttachedPresence routine is running
    private WallLamp _shadowLamp; // F128: the single rear WallLamp currently casting soft shadows for the attached presence

    private readonly RaycastHit[] _rayHits = new RaycastHit[16];

    private float _timer;
    private float _lastPhantomTime = -9999f;
    private int _collected;
    private int _total;

    private float Progress => _total > 0 ? _collected / (float)_total : 0f;

    private void Awake()
    {
        _phantomsRoot = new GameObject("Phantoms");
        _phantomsRoot.transform.SetParent(transform, false);
    }

    /// <summary>Configures <see cref="PhantomDirector"/> from the composition-root <see cref="RunContext"/>.</summary>
    public void Configure(RunContext ctx, Material wallMat, Material starMat, AudioClip[] footsteps)
    {
        if (ctx == null) return;
        Camera cam = ctx.Camera != null ? ctx.Camera.GetComponent<Camera>() : null;
        Configure(ctx.Maze, ctx.Player, cam, ctx.Hunter, ctx.Audio, ctx.Torch, ctx.Stealth,
            ctx.Outcome, ctx.Dread, wallMat, starMat, footsteps, ctx.Profile);
        if (ctx.Terror != null) ApplyTerror(ctx.Terror);
        if (ctx.DreadMeter != null) BindDread(ctx.DreadMeter);
    }

    /// <summary>Single-parameter overload for configuring from <see cref="RunContext"/>.</summary>
    public void Configure(RunContext ctx)
    {
        if (ctx == null) return;
        Configure(ctx, null, null, null);
    }

    public void Configure(MazeGenerator maze, Transform player, Camera camera, AIFollower follower, HorrorAudioDirector audio,
        Flashlight flashlight, PlayerStealthState stealth, GameOutcome outcome, DreadDirector dread,
        Material wallMat, Material starMat, AudioClip[] footsteps, FloorProfile profile)
    {
        _maze = maze;
        _player = player;
        _camera = camera;
        _follower = follower;
        _audio = audio;
        _flashlight = flashlight;
        _stealth = stealth;
        _outcome = outcome;
        _dread = dread;
        _footstepClips = footsteps;

        // Its own dark copies rather than MazeGenerator's runtime material list, so it owns their
        // lifetime and destroys them itself in OnDestroy.
        if (_silhouetteMaterial == null) _silhouetteMaterial = BuildSilhouetteMaterial(wallMat);
        if (_eyeMaterial == null) _eyeMaterial = BuildEyeMaterial(starMat);

        if (profile != null) phantomInterval = profile.PhantomInterval;

        _timer = ComputeInterval(Progress) * Random.Range(0.6f, 1.4f);
    }

    private void OnEnable()
    {
        GameManager.ProgressChanged += HandleProgress;
    }

    private void OnDisable()
    {
        GameManager.ProgressChanged -= HandleProgress;

        StopAllCoroutines();
        ClearShadowLamp();
        _attachedActive = false;
        _body = null; // destroyed with the phantoms below; rebuilt on the next use
        _doubleBody = null;
        if (_phantomsRoot != null)
        {
            for (int i = _phantomsRoot.transform.childCount - 1; i >= 0; i--)
            {
                Destroy(_phantomsRoot.transform.GetChild(i).gameObject);
            }
        }
    }

    private void OnDestroy()
    {
        if (_silhouetteMaterial != null) Destroy(_silhouetteMaterial);
        if (_eyeMaterial != null) Destroy(_eyeMaterial);
    }

    private void HandleProgress(int collected, int total)
    {
        _collected = collected;
        _total = total;
    }

    private void Update()
    {
        if (_maze == null || _player == null) return;

        _timer -= Time.deltaTime;
        if (_timer > 0f) return;

        bool fired = TryFirePhantom();
        _timer = fired
            ? ComputeInterval(Progress) * Random.Range(0.6f, 1.4f)
            : ComputeInterval(Progress) * failRetryFraction;
    }

    /// <summary>F104 B1: the DreadMeter that paces the phantoms - three times faster during a backoff, 1.5x slower above 0.8 dread.</summary>
    public void BindDread(DreadMeter meter)
    {
        _meter = meter;
    }

    private float ComputeInterval(float progress)
    {
        float interval = Mathf.Lerp(phantomInterval * 1.5f, phantomInterval, progress);
        if (_meter == null || !_meter.IsActive) return interval;

        return interval * (_meter.InBackoff ? backoffIntervalScale : _meter.Value > crowdedDread ? crowdedIntervalScale : 1f);
    }

    private bool BailedOut()
    {
        return GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding) || !GameFlow.IsRunActive;
    }

    /// <summary>F103: which Tier A features run, and the [Terror] log.</summary>
    public void ApplyTerror(TerrorSettings terror)
    {
        _terror = terror ?? new TerrorSettings();
    }

    private static bool IsAudioType(PhantomType type)
    {
        return type == PhantomType.Footsteps || type == PhantomType.Whisper || type == PhantomType.WrongSound || type == PhantomType.AttachedPresence;
    }

    /// <summary>F104 B3: a locker stops being a refuge. Once the player has been hidden for lockerQuietSeconds, sounds may reach them there; figures never do.</summary>
    private bool HiddenAllowsAudio(PhantomType type)
    {
        return _terror.sanctuaryDecay && IsAudioType(type) && type != PhantomType.AttachedPresence && Time.time - _stealth.HiddenSince >= lockerQuietSeconds;
    }

    private bool PassesGate(PhantomType type)
    {
        if (_maze == null || _player == null || _follower == null) return false;
        if (BailedOut()) return false;
        if (type == PhantomType.AttachedPresence && (_attachedActive || (_stealth != null && _stealth.Hidden))) return false;
        if (_maze.Loop != null && _maze.Loop.IsInside) return false; // F106: the looping corridor has its own horrors (the Visitor); no phantom fires inside it
        if (_follower.IsChasing) return false;
        // F103 A2: a sound may come from a hunter that is close; a figure may not. Otherwise every phantom proves the hunter is far away.
        float exclusion = IsAudioType(type) && _terror.contaminatedPhantoms ? audioHunterExclusionDistance : hunterExclusionDistance;
        if (_follower.FlatDistanceToTarget <= exclusion) return false;
        if (_maze.IsKillerEntityNear(_player.position, exclusion)) return false; // F107 D2: a Listener close by counts like the hunter
        if (_stealth != null && _stealth.Hidden && !HiddenAllowsAudio(type)) return false;
        if (Time.time - _lastPhantomTime < minSpacing) return false;
        if (_dread != null && _dread.IsTelegraphing) return false;

        return true;
    }

    private bool TryFirePhantom()
    {
        PhantomType type = PickType();
        if (!PassesGate(type)) return false;

        bool fired;
        switch (type)
        {
            case PhantomType.Silhouette:
                fired = TrySpawnSilhouette();
                break;
            case PhantomType.Footsteps:
                fired = TryStartFootsteps();
                break;
            case PhantomType.LampBlink:
                fired = TryBlinkLamp();
                break;
            case PhantomType.Whisper:
                fired = TryWhisper();
                break;
            case PhantomType.WrongSound:
                fired = TryWrongSound();
                break;
            case PhantomType.Shadow:
                fired = TrySpawnShadow();
                break;
            case PhantomType.AttachedPresence:
                fired = TryStartAttachedPresence();
                break;
            default:
                fired = TrySpawnPeripheral();
                break;
        }

        if (fired) _lastPhantomTime = Time.time;
        return fired;
    }

    private PhantomType PickType()
    {
        float silhouette = silhouetteWeight * (_flashlight != null && !_flashlight.IsOn ? silhouetteTorchOffMultiplier : 1f);
        float wrongSound = _terror.wrongSounds ? wrongSoundWeight : 0f;
        float shadow = ShadowPhantomPossible() ? shadowWeight : 0f;
        bool useAttached = _terror.attachedPresence && !_attachedActive;
        float footsteps = useAttached ? 0f : footstepsWeight;
        float peripheral = !useAttached && _terror.peripheralPhantoms ? peripheralWeight : 0f;
        float attached = useAttached ? footstepsWeight + (_terror.peripheralPhantoms ? peripheralWeight : 0f) : 0f;
        float total = silhouette + footsteps + lampBlinkWeight + whisperWeight + wrongSound + shadow + peripheral + attached;

        float r = Random.value * total;
        if (r < silhouette) return PhantomType.Silhouette;
        r -= silhouette;
        if (r < footsteps) return PhantomType.Footsteps;
        r -= footsteps;
        if (r < lampBlinkWeight) return PhantomType.LampBlink;
        r -= lampBlinkWeight;
        if (r < whisperWeight) return PhantomType.Whisper;
        r -= whisperWeight;
        if (r < wrongSound) return PhantomType.WrongSound;
        r -= wrongSound;
        if (r < shadow) return PhantomType.Shadow;
        r -= shadow;
        return r < peripheral ? PhantomType.Peripheral : PhantomType.AttachedPresence;
    }

    // ---------------------------------------------------------------- footsteps behind you

    private bool TryStartFootsteps()
    {
        if (_footstepClips == null || _footstepClips.Length == 0 || _camera == null || _player == null) return false;

        GameObject holder = new GameObject("Phantom_Footsteps");
        holder.transform.SetParent(_phantomsRoot.transform, false);

        AudioSource source = holder.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = footstepMinDistance;
        source.maxDistance = footstepMaxDistance;
        source.pitch = footstepPitch;

        // F103 A2: rolled once, here, and handed to the routine - two overlapping routines must not share state.
        bool honest = RollHonestFootsteps();
        bool hunterPitched = honest || (_terror.contaminatedPhantoms && Random.value < hunterPitchedChance);
        StartCoroutine(FootstepsRoutine(holder, source, hunterPitched, honest));
        return true;
    }

    private IEnumerator FootstepsRoutine(GameObject holder, AudioSource source, bool hunterPitched, bool honest)
    {
        float duration = Random.Range(footstepDurationMin, footstepDurationMax);
        // Offset half a stride so the first step does not land right on spawn.
        float distanceSinceStep = footstepStrideLength * 0.5f;
        Vector3 lastPlayerPos = _player.position;
        float t = 0f;

        while (t < duration)
        {
            if (BailedOut())
            {
                if (holder != null) Destroy(holder);
                yield break;
            }

            // Cut instantly, no late step: a chase starting is the real thing, and a phantom must
            // never be mistaken for it.
            if (_follower != null && _follower.IsChasing)
            {
                if (holder != null) Destroy(holder);
                yield break;
            }

            if (_camera != null)
            {
                Vector3 back = -_camera.transform.forward;
                back.y = 0f;
                if (back.sqrMagnitude < 0.0001f) back = -_camera.transform.right;
                back.Normalize();

                Vector3 pos = _camera.transform.position + back * footstepDistanceBehind;
                pos.y = _player.position.y;
                holder.transform.position = pos;
            }

            Vector3 playerPos = _player.position;
            distanceSinceStep += Vector3.Distance(playerPos, lastPlayerPos);
            lastPlayerPos = playerPos;

            if (distanceSinceStep >= footstepStrideLength)
            {
                distanceSinceStep = 0f;
                PlayFootstep(source, hunterPitched);
            }

            if (_stealth != null && _stealth.NoiseRadius <= 0f) break;

            t += Time.deltaTime;
            yield return null;
        }

        // Whether it ended because the player stopped or because the duration simply ran out, the
        // ending is the same: the beat that sells it as something that was following you.
        float lateT = 0f;
        while (lateT < footstepLateStepDelay)
        {
            if (BailedOut() || (_follower != null && _follower.IsChasing))
            {
                if (holder != null) Destroy(holder);
                yield break;
            }
            lateT += Time.deltaTime;
            yield return null;
        }

        PlayFootstep(source, hunterPitched);

        if (honest)
        {
            bool relocated = _dread != null && _dread.TryRelocateOnTrail(honestRelocateDistance);
            _terror.Log($"Honest footsteps -> relocated: {relocated}", this);
        }

        yield return new WaitForSeconds(1f);
        if (holder != null) Destroy(holder);
    }

    /// <summary>F103 A2: an honest phantom is rolled only when the hunter is patrolling far away and not hunting - then the sound it imitates is about to become true.</summary>
    private bool RollHonestFootsteps()
    {
        return _terror.contaminatedPhantoms
            && _follower != null && _follower.IsWandering && !_follower.IsHunting
            && _follower.FlatDistanceToTarget >= honestMinHunterDistance
            && Random.value < honestChance;
    }

    private void PlayFootstep(AudioSource source, bool hunterPitched)
    {
        if (_footstepClips == null || _footstepClips.Length == 0) return;

        // A hunter-pitched step re-pitches the shared source per step, like AIPresence does for the real thing.
        if (hunterPitched) source.pitch = Random.Range(hunterPitch.x, hunterPitch.y);
        source.PlayOneShot(_footstepClips[Random.Range(0, _footstepClips.Length)], hunterPitched ? hunterStepVolume : footstepVolume);
    }

    // ---------------------------------------------------------------- lamp beside you dies

    private bool TryBlinkLamp()
    {
        if (_maze == null || _player == null) return false;

        IReadOnlyList<WallLamp> lamps = _maze.Lamps;
        WallLamp nearest = null;
        float nearestDistance = lampBlinkRadius;

        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            // F73: an unpowered lamp (Blackout floor, before its fuse) is already dark.
            if (lamp == null || lamp.IsDead || !lamp.IsPowered) continue;

            float distance = Vector3.Distance(lamp.transform.position, _player.position);
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearest = lamp;
            }
        }

        if (nearest == null) return false;

        nearest.Blink(lampBlinkSeconds);
        return true;
    }

    // ---------------------------------------------------------------- whisper

    private bool TryWhisper()
    {
        if (_audio == null) return false;

        float pan = Random.value < 0.5f ? -0.9f : 0.9f;
        _audio.PlayWhisper(pan);
        return true;
    }

    // ---------------------------------------------------------------- F103 A8: wrong sounds

    /// <summary>One thing the wrong-sound phantom could play: a library slot (by field name, for the log), where, how loud.</summary>
    private struct WrongSoundOption
    {
        public string Name;
        public AudioSlot Slot;
        public Vector3 Position;
        public float Volume;
    }

    private readonly List<WrongSoundOption> _wrongSoundOptions = new List<WrongSoundOption>(8);
    private readonly List<Vector3> _occludedCells = new List<Vector3>(32);
    private readonly List<Vector3> _figureCells = new List<Vector3>(32); // F104 B8

    /// <summary>
    /// F103 A8: "someone else is here, using the things I use" - a locker door, a can, a shutter, a bottle or a torch click from
    /// somewhere out of sight. Never fakes an objective sound (star, hatch, key, shard, fuse) and never raises a noise event.
    /// </summary>
    private bool TryWrongSound()
    {
        if (!_terror.wrongSounds || _camera == null || _maze == null) return false;

        Vector3 camPos = _camera.transform.position;
        GameAudioLibrary library = GameAudio.Library;
        _wrongSoundOptions.Clear();

        AddLockerOption(camPos, library.lockerDoor);
        CollectOccludedCells(camPos);
        AddCellOption("canClatter", library.canClatter);
        AddCellOption("shutterSlam", library.shutterSlam);
        AddCellOption("bottleShatter", library.bottleShatter);
        if (library.torchClick.Has)
        {
            // Just behind the player: someone else switching a torch on.
            _wrongSoundOptions.Add(new WrongSoundOption
            {
                Name = "torchClick", Slot = library.torchClick,
                Position = camPos - _camera.transform.forward * torchClickBehindDistance, Volume = torchClickVolume
            });
        }

        if (_wrongSoundOptions.Count == 0) return false;

        WrongSoundOption pick = _wrongSoundOptions[Random.Range(0, _wrongSoundOptions.Count)];
        AudioSource source = GameAudio.SpawnAt(pick.Slot, pick.Position, pick.Volume, 1f, wrongSoundMaxDistance);
        if (source == null) return false;

        // Heard through a wall - except the torch click, which is right behind you.
        if (pick.Slot.lowPassHz <= 0f && pick.Name != "torchClick")
        {
            source.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = wrongSoundLowPassHz;
        }

        _terror.Log("Wrong sound: " + pick.Name, this);
        return true;
    }

    /// <summary>A random real locker in range whose front the camera cannot see.</summary>
    private void AddLockerOption(Vector3 camPos, AudioSlot slot)
    {
        if (!slot.Has) return;

        IReadOnlyList<Locker> lockers = _maze.Lockers;
        int seen = 0;
        Vector3 chosen = default;
        for (int i = 0; i < lockers.Count; i++)
        {
            if (lockers[i] == null) continue;

            Vector3 front = lockers[i].FrontPosition;
            float distance = Vector3.Distance(camPos, front);
            if (distance < wrongSoundLockerDistance.x || distance > wrongSoundLockerDistance.y) continue;
            if (IsRayClear(camPos, front + Vector3.up * 1.2f)) continue;

            // Reservoir pick: uniform over the matches without a scratch list.
            seen++;
            if (Random.Range(0, seen) == 0) chosen = front;
        }

        if (seen > 0)
        {
            _wrongSoundOptions.Add(new WrongSoundOption { Name = "lockerDoor", Slot = slot, Position = chosen, Volume = wrongSoundVolume });
        }
    }

    private void CollectOccludedCells(Vector3 camPos)
    {
        _occludedCells.Clear();
        IReadOnlyList<Vector3> cells = _maze.CellCenters;
        for (int i = 0; i < cells.Count; i++)
        {
            float distance = Vector3.Distance(camPos, cells[i]);
            if (distance < wrongSoundDistance.x || distance > wrongSoundDistance.y) continue;
            if (!IsRayClear(camPos, cells[i] + Vector3.up * 1.2f)) _occludedCells.Add(cells[i]);
        }
    }

    private void AddCellOption(string slotName, AudioSlot slot)
    {
        if (!slot.Has || _occludedCells.Count == 0) return;

        _wrongSoundOptions.Add(new WrongSoundOption
        {
            Name = slotName, Slot = slot, Volume = wrongSoundVolume,
            Position = _occludedCells[Random.Range(0, _occludedCells.Count)] + Vector3.up
        });
    }
}
