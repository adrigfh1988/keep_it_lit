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
public class PhantomDirector : MonoBehaviour
{
    private enum PhantomType
    {
        Silhouette,
        Footsteps,
        LampBlink,
        Whisper,
        WrongSound,
        Shadow,
        Peripheral
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
        _body = null; // destroyed with the phantoms below; rebuilt on the next use
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
        return type == PhantomType.Footsteps || type == PhantomType.Whisper || type == PhantomType.WrongSound;
    }

    /// <summary>F104 B3: a locker stops being a refuge. Once the player has been hidden for lockerQuietSeconds, sounds may reach them there; figures never do.</summary>
    private bool HiddenAllowsAudio(PhantomType type)
    {
        return _terror.sanctuaryDecay && IsAudioType(type) && Time.time - _stealth.HiddenSince >= lockerQuietSeconds;
    }

    private bool PassesGate(PhantomType type)
    {
        if (_maze == null || _player == null || _follower == null) return false;
        if (BailedOut()) return false;
        if (_follower.IsChasing) return false;
        // F103 A2: a sound may come from a hunter that is close; a figure may not. Otherwise every phantom proves the hunter is far away.
        float exclusion = IsAudioType(type) && _terror.contaminatedPhantoms ? audioHunterExclusionDistance : hunterExclusionDistance;
        if (_follower.FlatDistanceToTarget <= exclusion) return false;
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
        float peripheral = _terror.peripheralPhantoms ? peripheralWeight : 0f;
        float total = silhouette + footstepsWeight + lampBlinkWeight + whisperWeight + wrongSound + shadow + peripheral;

        float r = Random.value * total;
        if (r < silhouette) return PhantomType.Silhouette;
        r -= silhouette;
        if (r < footstepsWeight) return PhantomType.Footsteps;
        r -= footstepsWeight;
        if (r < lampBlinkWeight) return PhantomType.LampBlink;
        r -= lampBlinkWeight;
        if (r < whisperWeight) return PhantomType.Whisper;
        r -= whisperWeight;
        if (r < wrongSound) return PhantomType.WrongSound;
        r -= wrongSound;
        return r < shadow ? PhantomType.Shadow : PhantomType.Peripheral;
    }

    // ---------------------------------------------------------------- silhouette

    private bool TrySpawnSilhouette()
    {
        if (_camera == null || _maze == null) return false;

        IReadOnlyList<Vector3> cells = _maze.CellCenters;
        List<Vector3> candidates = new List<Vector3>();

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 cell = cells[i];
            float distance = Vector3.Distance(_camera.transform.position, cell);
            if (distance < silhouetteMinDistance || distance > silhouetteMaxDistance) continue;

            Vector3 viewport = _camera.WorldToViewportPoint(cell);
            if (viewport.z <= 0f) continue;
            if (viewport.x < 0.15f || viewport.x > 0.85f || viewport.y < 0.15f || viewport.y > 0.85f) continue;

            if (_follower != null && Vector3.Distance(_follower.transform.position, cell) < silhouetteHunterExclusion) continue;

            if (!IsRayClear(_camera.transform.position, cell + Vector3.up * 1.2f)) continue;

            candidates.Add(cell);
        }

        if (candidates.Count == 0) return false;

        SpawnSilhouette(candidates[Random.Range(0, candidates.Count)]);
        return true;
    }

    private void SpawnSilhouette(Vector3 position)
    {
        GameObject root = new GameObject("Phantom_Silhouette");
        root.transform.SetParent(_phantomsRoot.transform, false);
        root.transform.position = position;

        GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "Body";
        Collider capsuleCollider = capsule.GetComponent<Collider>();
        if (capsuleCollider != null)
        {
            capsuleCollider.enabled = false;
            Destroy(capsuleCollider);
        }
        capsule.transform.SetParent(root.transform, false);
        capsule.transform.localPosition = new Vector3(0f, 0.9f, 0f);
        capsule.transform.localScale = new Vector3(0.5f, 0.9f, 0.5f);

        Renderer bodyRenderer = capsule.GetComponent<Renderer>();
        if (bodyRenderer != null)
        {
            if (_silhouetteMaterial != null) bodyRenderer.sharedMaterial = _silhouetteMaterial;
            bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;
        }

        if (_eyeMaterial != null)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject eye = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                eye.name = "Eye";
                Collider eyeCollider = eye.GetComponent<Collider>();
                if (eyeCollider != null)
                {
                    eyeCollider.enabled = false;
                    Destroy(eyeCollider);
                }
                eye.transform.SetParent(root.transform, false);
                eye.transform.localPosition = new Vector3(side * 0.1f, 1.55f, 0.2f);
                eye.transform.localScale = Vector3.one * 0.06f;

                Renderer eyeRenderer = eye.GetComponent<Renderer>();
                eyeRenderer.sharedMaterial = _eyeMaterial;
                eyeRenderer.shadowCastingMode = ShadowCastingMode.Off;
                eyeRenderer.receiveShadows = false;
            }
        }

        StartCoroutine(SilhouetteRoutine(root, position));
    }

    private IEnumerator SilhouetteRoutine(GameObject root, Vector3 position)
    {
        float lifetime = Random.Range(silhouetteLifetimeMin, silhouetteLifetimeMax);
        float t = 0f;
        bool bailed = false;

        while (t < lifetime)
        {
            if (BailedOut())
            {
                bailed = true;
                break;
            }

            // Vanish the instant the torch beam actually reaches it, whatever the random lifetime says.
            if (IsInTorchBeam(position)) break; // F103 A4: tested from the torch mount, not the eye

            t += Time.deltaTime;
            yield return null;
        }

        if (root != null) Destroy(root);
        if (!bailed) MaybeKillLampNear(position);
    }

    /// <summary>F103 A2: a silhouette sometimes leaves a trace - the nearest lamp where it stood dies, so the dark it came from stays.</summary>
    private void MaybeKillLampNear(Vector3 position)
    {
        if (!_terror.contaminatedPhantoms || _maze == null || Random.value >= silhouetteLampKillChance) return;

        IReadOnlyList<WallLamp> lamps = _maze.Lamps;
        WallLamp nearest = null;
        float nearestDistance = silhouetteLampKillRadius;
        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp == null || lamp.IsDead || !lamp.IsPowered || lamp.IsLockerLamp) continue;
            if (_maze.IsNearLiveStar(lamp.transform.position)) continue;

            float distance = Vector3.Distance(lamp.transform.position, position);
            if (distance <= nearestDistance)
            {
                nearestDistance = distance;
                nearest = lamp;
            }
        }

        if (nearest == null) return;

        nearest.Kill(silhouetteLampFadeSeconds);
        _terror.Log("Silhouette killed a lamp", this);
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

    // ---------------------------------------------------------------- F104 B7/B8: figure phantoms

    /// <summary>
    /// The shared figure, built on first use from the hunter's HunterBody. Null (logged once) when the hunter has none; then the
    /// figure phantoms simply do not fire. Also null while another figure phantom is using it.
    /// </summary>
    private PhantomBody TryGetBody()
    {
        if (_body == null && _follower != null)
        {
            _body = PhantomBody.TryBuild(_follower, _phantomsRoot.transform);
            if (_body == null && !_bodyMissingLogged)
            {
                _bodyMissingLogged = true;
                Debug.LogWarning("PhantomDirector: the hunter has no HunterBody child, so shadow and peripheral phantoms are off.", this);
            }
        }

        return _body != null && !_body.InUse ? _body : null;
    }

    /// <summary>True while a shadow phantom could be staged: flag on, shadows on, the beam lit (a shadow needs a light to cast it).</summary>
    private bool ShadowPhantomPossible()
    {
        return _terror.shadowPhantoms && GameSettings.Shadows != GameSettings.ShadowTier.Off
            && _flashlight != null && _flashlight.IsBeamLit;
    }

    /// <summary>
    /// B7: a person's shadow crosses the lit wall and nobody is there. The hunter's body stands between the torch and the wall,
    /// rendered ShadowsOnly, and walks across the beam. Wall found at 4-10 m or nothing happens (the scheduler retries).
    /// </summary>
    private bool TrySpawnShadow()
    {
        if (!ShadowPhantomPossible() || _camera == null) return false;

        Vector3 origin = _flashlight.BeamOrigin;
        Vector3 forward = _flashlight.BeamForward;
        if (!TryFindBeamWall(origin, forward, out RaycastHit wall)) return false;

        Vector3 toWall = wall.point - origin;
        toWall.y = 0f;
        if (toWall.sqrMagnitude < 1e-4f) return false;

        PhantomBody body = TryGetBody();
        if (body == null) return false;

        float stand = Random.Range(shadowStandFraction.x, shadowStandFraction.y);
        Vector3 center = origin + toWall * stand;
        center.y = _player.position.y;
        Vector3 across = Vector3.Cross(Vector3.up, toWall.normalized) * (Random.value < 0.5f ? 1f : -1f);
        Vector3 start = center - across * shadowWalkHalfWidth;
        Vector3 end = center + across * shadowWalkHalfWidth;

        body.Show(start, across);
        body.SetShadowMode(ShadowCastingMode.ShadowsOnly);
        body.Walk();
        StartCoroutine(ShadowRoutine(body, start, end));
        _terror.Log("Shadow phantom", this);
        return true;
    }

    /// <summary>The nearest wall (not floor or ceiling) the torch beam hits at shadowWallDistance, ignoring triggers and the player and hunter.</summary>
    private bool TryFindBeamWall(Vector3 origin, Vector3 forward, out RaycastHit wall)
    {
        wall = default;
        int count = Physics.RaycastNonAlloc(origin, forward, _rayHits, shadowWallDistance.y, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            Transform hit = _rayHits[i].transform;
            if (IsPartOf(hit, _player) || (_follower != null && IsPartOf(hit, _follower.transform))) continue;
            if (_rayHits[i].distance < nearest)
            {
                nearest = _rayHits[i].distance;
                wall = _rayHits[i];
            }
        }

        return nearest >= shadowWallDistance.x && nearest <= shadowWallDistance.y && Mathf.Abs(wall.normal.y) < shadowWallMaxNormalY;
    }

    private IEnumerator ShadowRoutine(PhantomBody body, Vector3 start, Vector3 end)
    {
        for (float t = 0f; t < shadowWalkSeconds; t += Time.deltaTime)
        {
            if (BailedOut() || _follower.IsChasing) break;

            body.Root.transform.position = Vector3.Lerp(start, end, t / shadowWalkSeconds);
            yield return null;
        }

        body.Hide();
    }

    /// <summary>
    /// B8: a dark figure at the very edge of the screen, gone the moment you look. A cell 6-14 m away with a clear line to it,
    /// standing in the outer strip of the view on either side.
    /// </summary>
    private bool TrySpawnPeripheral()
    {
        if (!_terror.peripheralPhantoms || _camera == null || _maze == null || _silhouetteMaterial == null) return false;

        Vector3 camPos = _camera.transform.position;
        IReadOnlyList<Vector3> cells = _maze.CellCenters;
        _figureCells.Clear();
        for (int i = 0; i < cells.Count; i++)
        {
            float distance = Vector3.Distance(camPos, cells[i]);
            if (distance < peripheralDistance.x || distance > peripheralDistance.y) continue;

            Vector3 viewport = _camera.WorldToViewportPoint(cells[i] + Vector3.up * figureTestHeight);
            if (viewport.z <= 0f || !InEdgeStrip(viewport.x)) continue;
            if (!IsRayClear(camPos, cells[i] + Vector3.up * 1.2f)) continue;

            _figureCells.Add(cells[i]);
        }

        if (_figureCells.Count == 0) return false;

        PhantomBody body = TryGetBody();
        if (body == null) return false;

        Vector3 position = _figureCells[Random.Range(0, _figureCells.Count)];
        Vector3 facing = camPos - position;
        facing.y = 0f;

        body.Show(position, facing);
        body.SetMaterial(_silhouetteMaterial);
        body.SetShadowMode(ShadowCastingMode.On);
        body.Freeze(AIFollower.Pose.Lurk);
        StartCoroutine(PeripheralRoutine(body, position + Vector3.up * figureTestHeight));
        _terror.Log("Peripheral phantom", this);
        return true;
    }

    private bool InEdgeStrip(float viewportX)
    {
        return (viewportX >= peripheralEdge.x && viewportX <= peripheralEdge.y)
            || (viewportX >= 1f - peripheralEdge.y && viewportX <= 1f - peripheralEdge.x);
    }

    /// <summary>Removes the peripheral figure the instant it would be looked at: it drifts toward the centre of the view, the camera turns toward it fast, or the beam reaches it.</summary>
    private IEnumerator PeripheralRoutine(PhantomBody body, Vector3 testPoint)
    {
        float previousYaw = _camera.transform.eulerAngles.y;
        for (float t = 0f; t < peripheralLifetime; t += Time.deltaTime)
        {
            if (BailedOut() || _follower.IsChasing || IsInTorchBeam(testPoint)) break;

            Vector3 viewport = _camera.WorldToViewportPoint(testPoint);
            if (viewport.z <= 0f || (viewport.x > peripheralVanishRange.x && viewport.x < peripheralVanishRange.y)) break;

            float yaw = _camera.transform.eulerAngles.y;
            float turnRate = Mathf.DeltaAngle(previousYaw, yaw) / Mathf.Max(Time.deltaTime, 1e-4f);
            previousYaw = yaw;
            if (turnRate * (viewport.x > 0.5f ? 1f : -1f) > peripheralTurnRate) break;

            yield return null;
        }

        body.Hide();
    }

    /// <summary>True when the lit torch beam (from its mount, F103 A4) reaches `point`, by the same angle and range fractions the silhouette vanishes at.</summary>
    private bool IsInTorchBeam(Vector3 point)
    {
        if (_flashlight == null || !_flashlight.IsBeamLit) return false;

        Vector3 dir = point - _flashlight.BeamOrigin;
        return dir.magnitude < _flashlight.Range * silhouetteBeamRangeFraction
            && Vector3.Angle(_flashlight.BeamForward, dir) < _flashlight.OuterAngle * silhouetteBeamAngleFraction;
    }

    // ---------------------------------------------------------------- shared helpers

    private bool IsRayClear(Vector3 from, Vector3 to)
    {
        Vector3 diff = to - from;
        float distance = diff.magnitude;
        if (distance < 0.01f) return true;

        int count = Physics.RaycastNonAlloc(from, diff / distance, _rayHits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = _rayHits[i].transform;
            if (_player != null && IsPartOf(hit, _player)) continue;
            if (_follower != null && IsPartOf(hit, _follower.transform)) continue;
            return false;
        }

        return true;
    }

    private static bool IsPartOf(Transform candidate, Transform root)
    {
        while (candidate != null)
        {
            if (candidate == root) return true;
            candidate = candidate.parent;
        }

        return false;
    }

    private static Material BuildSilhouetteMaterial(Material source)
    {
        if (source == null) return null;

        Material copy = new Material(source) { name = "Phantom_Silhouette" };
        copy.DisableKeyword("_EMISSION");
        copy.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;
        Color tint = new Color(0.02f, 0.02f, 0.025f);
        if (copy.HasProperty("_EmissionColor")) copy.SetColor("_EmissionColor", Color.black);
        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", tint);
        if (copy.HasProperty("_Color")) copy.SetColor("_Color", tint);

        return copy;
    }

    private static Material BuildEyeMaterial(Material source)
    {
        if (source == null) return null;

        Material copy = new Material(source) { name = "Phantom_Eyes" };
        copy.EnableKeyword("_EMISSION");
        Color eyeColor = new Color(0.8f, 0.86f, 1f);
        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", eyeColor);
        if (copy.HasProperty("_Color")) copy.SetColor("_Color", eyeColor);
        // F103 A2: cold white, not red - red stays the real hunter's. A phantom should read as wrong, not as it.
        copy.SetColor("_EmissionColor", eyeColor * 1.5f);

        return copy;
    }
}
