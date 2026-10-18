using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Tier 3's orchestrator for two things that both key off the same progress number: F23's relocation
/// (the hunter appearing behind you, telegraphed, along your own trail) and F27's reaction to the star
/// count (lamp ripples/deaths, the world darkening, the last-star breath). Relocations are deliberately
/// wordless since F103 A1; only facts the player cannot perceive (lights dying, the ambush) get a subtitle,
/// via PlayerHud.ShowSubtitle.
///
/// No execution-order attribute: everything it needs (PlayerHud, the maze, the actors) is built earlier
/// in MazeGenerator.SetUpAtmosphere and handed in through Configure. Added at runtime, so OnEnable runs
/// during AddComponent, before Configure - every handler below null-checks accordingly.
/// </summary>
public class DreadDirector : MonoBehaviour
{
    private struct TrailSample
    {
        public Vector3 Position;
        public float Time;
    }

    [Header("Relocation - trigger")]
    [Tooltip("Progress (0..1) at which relocation may start")]
    [SerializeField] private float startProgress = 0.4f;
    [Tooltip("Seconds between relocation checks while a star has armed one")]
    [SerializeField] private float checkInterval = 0.5f;
    [Tooltip("Taking a star arms one relocation for this many seconds. It fires the first moment the rules allow inside the window, then disarms; nothing relocates outside a window, so every relocation is an answer to a pickup.")]
    [SerializeField] private float armedWindowSeconds = 10f;
    [Tooltip("Hard minimum seconds between two relocations, so two stars grabbed back to back cannot chain them")]
    [SerializeField] private float hardMinimumCooldown = 15f;
    [Tooltip("Seconds since the last chase ended before a relocation may be considered again")]
    [SerializeField] private float chaseCooldown = 8f;
    [Tooltip("The player has to have moved at least this far within the stillness window - never onto someone standing still reading the HUD")]
    [SerializeField] private float movementThreshold = 1f;
    [SerializeField] private float stillnessWindow = 3f;
    [Tooltip("The hunter has to be at least this much farther than the relocation distance, or it would be a nudge rather than a jump")]
    [SerializeField] private float jumpMargin = 5f;

    [Header("Relocation - distance")]
    [Tooltip("Relocation distance along the trail at startProgress (m)")]
    [SerializeField] private float farDistance = 16f;
    [Tooltip("...at full progress (m). Overridden per floor by FloorProfile.RelocateNearDistance when a profile is bound.")]
    [SerializeField] private float nearDistance = 7f;
    [Tooltip("Never relocate closer than this, whatever progress says (m)")]
    [SerializeField] private float minDistance = 6f;

    [Header("Relocation - trail")]
    [SerializeField] private float sampleInterval = 0.5f;
    [SerializeField] private int trailCapacity = 64;
    [Tooltip("A jump further than this between samples is a teleport (locker, hatch drop, respawn), not travel - dropped so the trail never runs through the inside of a locker")]
    [SerializeField] private float trailJumpThreshold = 2.5f;

    [Header("Relocation - candidate spot")]
    [SerializeField] private float navSampleRadius = 1.5f;
    [SerializeField] private float lockerExclusionRadius = 2f;
    [Tooltip("Reject a spot this close to the hunter's current position - nothing happens if it is already there")]
    [SerializeField] private float hunterExclusionRadius = 3f;
    [Tooltip("How far back along the trail, past the chosen point, to also try if it fails validation")]
    [SerializeField] private int candidateFallbackCount = 4;

    [Header("Relocation - telegraph timing (seconds from trigger)")]
    [SerializeField] private float humHoldSeconds = 1.4f;
    [SerializeField] private float stutterAt = 0.15f;
    [SerializeField] private float stutterSeconds = 0.9f;
    [SerializeField] private float cueAt = 0.30f;
    [SerializeField] private float warpAt = 1.10f;

    [Header("Subtitles")]
    [SerializeField] private float subtitleSeconds = 2.6f;
    // S9: the C# defaults below are the English fallbacks; the lines players see come from the Subtitles table (sub.dread.*) via Loc().
    // F103 A1: the relocation distance bands and "it knows" were cut - a threat that has been named is a bounded threat.
    [SerializeField] private string lineLightsDying = "The lights are dying.";
    [Tooltip("F45: shown once per campaign, the first time an ambush ends because the ambushed star was actually taken")]
    [SerializeField] private string lineAmbushed = "It was waiting.";

    [Header("F27 - reacting to the count")]
    [SerializeField] private float flickerWaveSpeed = 12f;
    [SerializeField] private float flickerDuration = 0.25f;
    [SerializeField] private float firstDeathProgress = 0.5f;
    [SerializeField] private float secondDeathProgress = 0.75f;
    // F71: halved from 0.2 - the new density (FloorProfile.CellsPerLamp 5..8, was 2..3) means far fewer
    // lamps exist to begin with, so the old fraction emptied a floor almost completely by wave 2.
    [Tooltip("Fraction of the ORIGINAL live, non-locker lamp count killed at each death wave")]
    [SerializeField] private float deathWaveFraction = 0.1f;
    [SerializeField] private float deathFadeSeconds = 1.5f;
    [SerializeField] private float firstDarkenFraction = 0.15f;
    [SerializeField] private float secondDarkenFraction = 0.25f;
    [SerializeField] private float darkenSeconds = 2f;
    [SerializeField] private float eyeEmissionAtSecondDeath = 6f;
    [SerializeField] private float breathSeconds = 1.6f;

    [Header("F103 A5 - unpredictability")]
    [Tooltip("Each telegraph beat is scaled by a fresh roll in 1 +- this")]
    [Range(0f, 0.9f)]
    [SerializeField] private float timingJitter = 0.5f;
    [Tooltip("Chance a telegraph warps nothing at the end (the star is spent, no cooldown starts)")]
    [Range(0f, 1f)]
    [SerializeField] private float feintChance = 0.33f;
    [Tooltip("Chance a relocation late in the floor skips the telegraph entirely and just happens")]
    [Range(0f, 1f)]
    [SerializeField] private float silentWarpChance = 0.2f;
    [Tooltip("Progress (0..1) from which a silent warp is possible")]
    [SerializeField] private float silentWarpFromProgress = 0.75f;
    [Tooltip("A silent warp only lands at least this far from the player (m)")]
    [SerializeField] private float silentWarpMinDistance = 10f;

    [Header("F103 A3 - eyes")]
    [Tooltip("After a warp the hunter's eyes burn for this long, so a player who turns fast sees them (s)")]
    [SerializeField] private float eyeRevealAfterWarpSeconds = 1.5f;

    [Header("F103 A7 - heard you")]
    [Tooltip("How long the world text IT HEARD YOU. stays up (s)")]
    [SerializeField] private float heardYouSubtitleSeconds = 2f;

    [Header("F103 A6 - torch failure")]
    [Tooltip("Most scripted torch failures per floor")]
    [SerializeField] private int maxTorchFailuresPerFloor = 2;
    [Tooltip("Minimum seconds between two torch failures")]
    [SerializeField] private float torchFailureSpacing = 30f;
    [Tooltip("How long the beam stays dark, min..max (s)")]
    [SerializeField] private Vector2 torchFailureSeconds = new Vector2(1.2f, 2.5f);
    [Tooltip("First sighting: the hunter has to be at least this far for the torch to fail (m); closer, the sighting is scary enough")]
    [SerializeField] private float sightingFailureMinHunterDistance = 8f;
    [Tooltip("First sighting: seconds after the chase starts before the torch fails")]
    [SerializeField] private float sightingFailureDelay = 0.25f;
    [Tooltip("No failure while the hunter is chasing and closer than this (m): the chase is already the threat")]
    [SerializeField] private float chaseGuardDistance = 6f;
    [Tooltip("No failure while the Stalker is within this of the player (m): a scripted blackout must never release a Stan the player is holding frozen")]
    [SerializeField] private float stalkerGuardDistance = 10f;
    [Tooltip("Dead end: the hunter has to be within this of the player, not chasing (m)")]
    [SerializeField] private float deadEndFailureHunterDistance = 15f;
    [Tooltip("Lamps within this of the player blink along with the torch (m)")]
    [SerializeField] private float failureLampRadius = 10f;
    [Tooltip("Seconds between checks for the player standing in a dead end")]
    [SerializeField] private float deadEndCheckInterval = 0.5f;

    [Header("F104 B11 - held breath")]
    [Tooltip("The held breath fires once per floor at a progress rolled in this range")]
    [SerializeField] private Vector2 heldBreathProgress = new Vector2(0.35f, 0.85f);
    [Tooltip("How long the silence holds, min..max (s)")]
    [SerializeField] private Vector2 heldBreathSeconds = new Vector2(15f, 25f);
    [Tooltip("Seconds the bed and the wind take to fall away (s)")]
    [SerializeField] private float heldBreathFadeIn = 2f;
    [Tooltip("Seconds they take to return at the end (s)")]
    [SerializeField] private float heldBreathFadeOut = 3f;
    [Tooltip("Seconds they take to return when a chase or the end of the run cuts the hold short (s)")]
    [SerializeField] private float heldBreathAbortFade = 0.5f;
    [Tooltip("Chance that midway through the hold one lamp at the end of the corridor dies")]
    [Range(0f, 1f)]
    [SerializeField] private float heldBreathLampChance = 0.33f;
    [Tooltip("The dread meter must be below this for the hold to start; a player already terrified does not need the lull")]
    [SerializeField] private float heldBreathMaxDread = 0.5f;
    [Tooltip("The lamp that dies is at most this far from the camera (m)")]
    [SerializeField] private float heldBreathLampRange = 18f;
    [Tooltip("...and within this angle of where the player is looking (degrees, flat)")]
    [SerializeField] private float heldBreathLampAngle = 30f;
    [Tooltip("Fade of the dying lamp (s)")]
    [SerializeField] private float heldBreathLampFade = 1.2f;
    [Tooltip("Seconds between checks for the held breath's conditions")]
    [SerializeField] private float heldBreathCheckInterval = 0.5f;
    [Tooltip("How far in front of a wall lamp the line-of-sight ray ends (m)")]
    [SerializeField] private float lampSurfaceInset = 0.3f;

    [Header("F105 C5 - lure star")]
    [Tooltip("The hunter is put this far back along the player's trail when the lure star is taken (m)")]
    [SerializeField] private float lureRelocateDistance = 9f;
    [Tooltip("Lamps within this of the lure star blink when it is taken (m)")]
    [SerializeField] private float lureLampRadius = 8f;
    [Tooltip("Chance that a lamp in that ring dies instead of only blinking")]
    [Range(0f, 1f)]
    [SerializeField] private float lureLampDeathChance = 0.5f;
    [SerializeField] private float lureBlinkSeconds = 0.6f;
    [Tooltip("A hunter lying in wait for the lure within this of it (m) goes straight to a chase instead of a search")]
    [SerializeField] private float lureAmbushRadius = 6f;

    // F103 A5: the jittered beats keep these minimum gaps, so the stutter, the cue and the warp stay in order and apart.
    private const float MinCueGapSeconds = 0.05f;
    private const float MinWarpGapSeconds = 0.3f;
    private const float HumHoldTailSeconds = 0.3f;
    // F45: an ambush that ended on a pickup this recently is the one this pickup ended (TensionDirector's HearNoise runs before this handler).
    private const float AmbushJustEndedSeconds = 0.5f;

    private static string Loc(string key, string fallback) => L10n.Has(key) ? L10n.T(key) : fallback;

    private MazeGenerator _maze;
    private Transform _player;
    private Camera _camera;
    private AIFollower _follower;
    private AIPresence _presence;
    private HorrorAudioDirector _audio;
    private HorrorAtmosphere _atmosphere;
    private Flashlight _flashlight;
    private PlayerStealthState _stealth;
    private PlayerHud _hud;
    private GameOutcome _outcome;
    // F104 B11 / B1: the ambience that ducks with the bed, the meter that gates the hold, and the once-per-floor state.
    private MazeAmbience _ambience;
    private DreadMeter _meter;
    private float _heldBreathAt;
    private float _nextHeldBreathCheck;
    private bool _heldBreathDone;

    private readonly List<TrailSample> _trail = new List<TrailSample>();
    private Vector3 _lastRawSample;
    private bool _hasLastRaw;
    private Vector3 _lastAcceptedPosition;
    private bool _hasAccepted;

    private readonly RaycastHit[] _losHits = new RaycastHit[16];

    private float _sampleTimer;
    private float _checkTimer;
    // Time.time until which a star-taken relocation is armed; -1 = disarmed. Time.time stands still under
    // pause, so a window paused halfway resumes with its remainder intact.
    private float _armedUntil = -1f;
    private float _lastRelocationTime = -9999f;
    private float _lastChaseEndTime = -9999f;
    private bool _isTelegraphing;

    private int _collected;
    private int _total;
    private bool _firstDeathsFired;
    private bool _secondDeathsFired;
    private int _originalLiveLampCount = -1;
    private System.Random _lampRng;

    private TerrorSettings _terror = new TerrorSettings();
    // F103 A6
    private int _torchFailures;
    private float _lastTorchFailureTime = -9999f;
    private bool _sawFirstChase;
    private float _deadEndTimer;
    private Vector3 _lastDeadEndCell;
    private bool _hasLastDeadEndCell;

    // F45: shown once per campaign. With Enter Play Mode > Reload Domain disabled this static would
    // otherwise survive between presses of Play, same as GameFlow's own statics - reset alongside them.
    private static bool s_ambushSubtitleShown;

    /// <summary>True while a relocation telegraph is playing out. PhantomDirector skips a phantom while this is true - a telegraph is already one.</summary>
    public bool IsTelegraphing => _isTelegraphing;

    /// <summary>F45's once-per-campaign subtitle flag. Cleared by GameFlow.ReturnToMenu - the economy and dread beats both live for one campaign only.</summary>
    public static void ResetCampaign()
    {
        s_ambushSubtitleShown = false;
    }

    /// <summary>F83: writes the once-per-campaign subtitle flag into a save.</summary>
    public static void CaptureTo(SaveData data)
    {
        data.ambushSubtitleShown = s_ambushSubtitleShown;
    }

    /// <summary>F83: restores the once-per-campaign subtitle flag from a save.</summary>
    public static void RestoreFrom(SaveData data)
    {
        s_ambushSubtitleShown = data.ambushSubtitleShown;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_ambushSubtitleShown = false;
    }

    private float Progress => _total > 0 ? _collected / (float)_total : 0f;

    public void Configure(MazeGenerator maze, Transform player, Camera camera, AIFollower follower, AIPresence presence,
        HorrorAudioDirector audio, HorrorAtmosphere atmosphere, Flashlight flashlight, PlayerStealthState stealth,
        PlayerHud hud, GameOutcome outcome, FloorProfile profile)
    {
        _maze = maze;
        _player = player;
        _camera = camera;
        _presence = presence;
        _audio = audio;
        _atmosphere = atmosphere;
        _flashlight = flashlight;
        _stealth = stealth;
        _hud = hud;
        _outcome = outcome;

        // Configure runs after OnEnable when this is added at runtime, so unsubscribe first - harmless
        // if it was never subscribed, and required so a re-Configure cannot double-subscribe.
        if (_follower != null)
        {
            _follower.ChaseStateChanged -= HandleChaseStateChanged;
            _follower.AmbushStateChanged -= HandleAmbushStateChanged;
            _follower.HeardYou -= HandleHeardYou;
        }
        _follower = follower;
        if (_follower != null)
        {
            _follower.ChaseStateChanged += HandleChaseStateChanged;
            _follower.AmbushStateChanged += HandleAmbushStateChanged;
            _follower.HeardYou += HandleHeardYou;
        }

        if (profile != null)
        {
            nearDistance = profile.RelocateNearDistance;
        }

        _heldBreathAt = Random.Range(heldBreathProgress.x, heldBreathProgress.y); // F104 B11: runtime randomness, never a maze stream
    }

    /// <summary>F103: which Tier A features run, and the [Terror] log.</summary>
    public void ApplyTerror(TerrorSettings terror)
    {
        _terror = terror ?? new TerrorSettings();
    }

    /// <summary>F74 Restless run rule: scales hardMinimumCooldown so relocations can chain tighter. Called at most once per floor (MazeGenerator.SetUpAtmosphere), so multiplying in place is safe.</summary>
    public void SetHardMinimumCooldownMultiplier(float multiplier)
    {
        hardMinimumCooldown *= Mathf.Max(0.01f, multiplier);
    }

    private void OnEnable()
    {
        GameManager.ProgressChanged += HandleProgress;
        GameManager.AllStarsCollected += HandleAllStarsCollected;
        Pickup.OnCollectedAt += HandleStarTaken;
    }

    private void OnDisable()
    {
        GameManager.ProgressChanged -= HandleProgress;
        GameManager.AllStarsCollected -= HandleAllStarsCollected;
        Pickup.OnCollectedAt -= HandleStarTaken;
        if (_follower != null)
        {
            _follower.ChaseStateChanged -= HandleChaseStateChanged;
            _follower.AmbushStateChanged -= HandleAmbushStateChanged;
            _follower.HeardYou -= HandleHeardYou;
        }

        StopAllCoroutines();
        _isTelegraphing = false;
    }

    private void Update()
    {
        if (_maze == null || _player == null) return;

        _sampleTimer -= Time.deltaTime;
        if (_sampleTimer <= 0f)
        {
            _sampleTimer = sampleInterval;
            SampleTrail();
        }

        // Only a star arms a relocation. Outside a window there is nothing to check, so the hunter never
        // just "shows up" behind a player who is quietly exploring.
        if (!_isTelegraphing && Time.time <= _armedUntil)
        {
            _checkTimer -= Time.deltaTime;
            if (_checkTimer <= 0f)
            {
                _checkTimer = checkInterval;
                TryConsiderRelocation();
            }
        }

        TickHeldBreath();

        _deadEndTimer -= Time.deltaTime;
        if (_deadEndTimer <= 0f)
        {
            _deadEndTimer = deadEndCheckInterval;
            CheckDeadEndFailure();
        }
    }

    /// <summary>F104 B11: the maze's environmental one-shots, ducked together with the bed during the held breath.</summary>
    public void BindAmbience(MazeAmbience ambience)
    {
        _ambience = ambience;
    }

    /// <summary>F104 B11: the DreadMeter that keeps the held breath away from a player who is already terrified. Optional.</summary>
    public void BindDread(DreadMeter meter)
    {
        _meter = meter;
    }

    // ---------------------------------------------------------------- F104 B11: held breath

    /// <summary>
    /// Once per floor, once progress passes the rolled point and the moment is calm (music playing, nothing chasing or hunting,
    /// no telegraph, not hidden, dread low), everything goes quiet for a while - and usually nothing happens.
    /// </summary>
    private void TickHeldBreath()
    {
        if (_heldBreathDone || !_terror.heldBreath || _audio == null || Time.time < _nextHeldBreathCheck) return;
        _nextHeldBreathCheck = Time.time + heldBreathCheckInterval;

        if (Progress < _heldBreathAt || !_audio.IsBedMusic) return;
        if (!GameFlow.IsRunActive || BailedOut() || _isTelegraphing) return;
        if (_follower == null || _follower.IsChasing || _follower.IsHunting || _follower.IsCaptured) return;
        if (_stealth != null && _stealth.Hidden) return;
        if (_meter != null && _meter.IsActive && _meter.Smoothed >= heldBreathMaxDread) return;

        _heldBreathDone = true;
        StartCoroutine(HeldBreathRoutine());
    }

    private IEnumerator HeldBreathRoutine()
    {
        float seconds = Random.Range(heldBreathSeconds.x, heldBreathSeconds.y);
        _terror.Log($"Held breath {seconds:0}s", this);
        DuckBed(0f, heldBreathFadeIn);

        float t = 0f;
        bool lampRolled = false;
        while (t < seconds)
        {
            if (BailedOut() || _follower == null || _follower.IsChasing)
            {
                DuckBed(1f, heldBreathAbortFade);
                yield break;
            }

            if (!lampRolled && t >= seconds * 0.5f)
            {
                lampRolled = true;
                if (Random.value < heldBreathLampChance) KillLampAhead();
            }

            t += Time.deltaTime;
            yield return null;
        }

        DuckBed(1f, heldBreathFadeOut);
    }

    private void DuckBed(float gain, float seconds)
    {
        _audio.Duck(gain, seconds);
        if (_ambience != null) _ambience.SetDuck(gain, seconds);
    }

    /// <summary>
    /// Kills the nearest live, powered, non-locker lamp within range that the player is looking toward with a clear line to it and
    /// that is not guarding a live star: a light at the end of the corridor going out in the silence.
    /// </summary>
    private void KillLampAhead()
    {
        if (_maze == null || _camera == null) return;

        Vector3 camPos = _camera.transform.position;
        Vector3 flatForward = _camera.transform.forward;
        flatForward.y = 0f;
        flatForward.Normalize();

        IReadOnlyList<WallLamp> lamps = _maze.Lamps;
        WallLamp best = null;
        float bestDistance = heldBreathLampRange;
        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp == null || lamp.IsLockerLamp || lamp.IsDead || !lamp.IsPowered) continue;

            Vector3 toLamp = lamp.transform.position - camPos;
            float distance = toLamp.magnitude;
            if (distance >= bestDistance) continue;

            Vector3 flatToLamp = toLamp;
            flatToLamp.y = 0f;
            if (Vector3.Angle(flatForward, flatToLamp) > heldBreathLampAngle) continue;
            if (_maze.IsNearLiveStar(lamp.transform.position)) continue;
            // Aim just in front of the fixture: a wall lamp sits ON a wall, whose collider would always block the ray.
            if (IsRayBlocked(camPos, lamp.transform.position - toLamp / distance * lampSurfaceInset)) continue;

            best = lamp;
            bestDistance = distance;
        }

        if (best == null) return;
        best.Kill(heldBreathLampFade);
        _terror.Log("Held breath: a lamp ahead died", this);
    }

    // ---------------------------------------------------------------- F23: trail

    private void SampleTrail()
    {
        // Lockers stay trustworthy: never record the inside of one, or the trail would offer it up
        // as a relocation spot.
        if (_stealth != null && _stealth.Hidden) return;

        Vector3 pos = _player.position;

        if (_hasAccepted && Vector3.Distance(_lastAcceptedPosition, pos) > trailJumpThreshold)
        {
            // A teleport (locker, hatch drop, respawn). If the very next sample settles near here too,
            // treat this as the new baseline instead of refusing every sample forever.
            if (_hasLastRaw && Vector3.Distance(_lastRawSample, pos) <= trailJumpThreshold)
            {
                _hasAccepted = false;
            }
            else
            {
                _lastRawSample = pos;
                _hasLastRaw = true;
                return;
            }
        }

        _lastRawSample = pos;
        _hasLastRaw = true;
        _lastAcceptedPosition = pos;
        _hasAccepted = true;

        _trail.Add(new TrailSample { Position = pos, Time = Time.time });
        if (_trail.Count > trailCapacity) _trail.RemoveAt(0);
    }

    /// <summary>Has the player covered at least movementThreshold metres within the last stillnessWindow seconds?</summary>
    private bool PlayerMovedRecently()
    {
        if (_trail.Count == 0) return false;

        Vector3 latest = _trail[_trail.Count - 1].Position;
        float cutoff = Time.time - stillnessWindow;

        for (int i = _trail.Count - 1; i >= 0; i--)
        {
            if (_trail[i].Time < cutoff) break;
            if (Vector3.Distance(_trail[i].Position, latest) >= movementThreshold) return true;
        }

        return false;
    }

    /// <summary>Walks the trail backwards accumulating segment length until it reaches distance, then tries that entry and a few older ones.</summary>
    private bool TryFindRelocationSpot(float distance, out Vector3 spot)
    {
        spot = default;
        if (_trail.Count < 2) return false;

        float accumulated = 0f;
        int startIndex = -1;
        for (int i = _trail.Count - 1; i > 0; i--)
        {
            accumulated += Vector3.Distance(_trail[i].Position, _trail[i - 1].Position);
            if (accumulated >= distance)
            {
                startIndex = i - 1;
                break;
            }
        }

        // The trail does not reach back far enough yet - no candidate, rather than quietly settling
        // for the oldest sample and mislabelling a near relocation as the far band.
        if (startIndex < 0) return false;

        for (int offset = 0; offset <= candidateFallbackCount; offset++)
        {
            int index = startIndex - offset;
            if (index < 0) break;

            if (ValidateCandidateFull(_trail[index].Position, out Vector3 navPosition))
            {
                spot = navPosition;
                return true;
            }
        }

        return false;
    }

    private bool ValidateCandidateFull(Vector3 rawPosition, out Vector3 navPosition)
    {
        navPosition = rawPosition;
        if (!NavMesh.SamplePosition(rawPosition, out NavMeshHit hit, navSampleRadius, NavMesh.AllAreas)) return false;
        navPosition = hit.position;

        if (IsInsideLockerCell(navPosition)) return false;

        return ValidateCandidateVisibilityAndDistance(navPosition);
    }

    /// <summary>23.3 steps 3-5, reused as-is for the re-validation at the warp instant.</summary>
    private bool ValidateCandidateVisibilityAndDistance(Vector3 navPosition)
    {
        if (!IsHiddenFromCamera(navPosition)) return false;

        if (FlatDistance(_player.position, navPosition) < minDistance) return false;

        if (_follower != null && Vector3.Distance(_follower.transform.position, navPosition) < hunterExclusionRadius) return false;

        return true;
    }

    private bool IsInsideLockerCell(Vector3 position)
    {
        if (_maze == null) return false;

        IReadOnlyList<Locker> lockers = _maze.Lockers;
        for (int i = 0; i < lockers.Count; i++)
        {
            if (lockers[i] != null && Vector3.Distance(lockers[i].FrontPosition, position) <= lockerExclusionRadius) return true;
        }

        return false;
    }

    /// <summary>
    /// True if the point is an acceptable relocation spot: both eye- and feet-height rays from the
    /// camera are blocked, or the point is genuinely behind the camera and outside its view frustum.
    /// Blocked-and-behind is the common case; blocked-only is accepted; a visible point is rejected.
    /// </summary>
    private bool IsHiddenFromCamera(Vector3 point)
    {
        if (_camera == null) return true;

        Vector3 camPos = _camera.transform.position;
        bool eyesBlocked = IsRayBlocked(camPos, point + Vector3.up * 1.5f);
        bool feetBlocked = IsRayBlocked(camPos, point + Vector3.up * 0.3f);
        if (eyesBlocked && feetBlocked) return true;

        Vector3 viewport = _camera.WorldToViewportPoint(point);
        bool outsideFrustum = viewport.z < 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f;

        Vector3 flatDir = point - camPos;
        flatDir.y = 0f;
        Vector3 flatForward = _camera.transform.forward;
        flatForward.y = 0f;
        bool genuinelyBehind = flatDir.sqrMagnitude > 0.0001f && flatForward.sqrMagnitude > 0.0001f
            && Vector3.Dot(flatForward.normalized, flatDir.normalized) < -0.2f;

        return outsideFrustum && genuinelyBehind;
    }

    private bool IsRayBlocked(Vector3 from, Vector3 to)
    {
        Vector3 diff = to - from;
        float distance = diff.magnitude;
        if (distance < 0.01f) return false;

        int count = Physics.RaycastNonAlloc(from, diff / distance, _losHits, distance, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = _losHits[i].transform;
            if (_player != null && IsPartOf(hit, _player)) continue;
            if (_follower != null && IsPartOf(hit, _follower.transform)) continue;
            return true;
        }

        return false;
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

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        Vector3 d = a - b;
        d.y = 0f;
        return d.magnitude;
    }

    // ---------------------------------------------------------------- F23: trigger + telegraph

    private void TryConsiderRelocation()
    {
        if (Time.time - _lastRelocationTime < hardMinimumCooldown) return;

        float distance = ComputeRelocationDistance(Progress);
        if (!PassesCoreRules(distance)) return;
        if (!TryFindRelocationSpot(distance, out Vector3 spot)) return;

        // F103 A5: late in a floor it is sometimes just there - no stutter, no cue, no hold.
        if (ShouldWarpSilently(spot))
        {
            if (CommitRelocation(spot, distance)) _terror.Log("Silent warp", this);
            return;
        }

        StartCoroutine(RelocateRoutine(spot, distance));
    }

    /// <summary>F103 A5: a rare telegraph-free warp, only late in the floor and only a good way off (a warp next to the player would just be a jump scare).</summary>
    private bool ShouldWarpSilently(Vector3 spot)
    {
        return _terror.unpredictableTelegraphs
            && Progress >= silentWarpFromProgress
            && FlatDistance(_player.position, spot) >= silentWarpMinDistance
            && Random.value < silentWarpChance;
    }

    private float ComputeRelocationDistance(float progress)
    {
        float t = Mathf.InverseLerp(startProgress, 1f, progress);
        return Mathf.Max(minDistance, Mathf.Lerp(farDistance, nearDistance, t));
    }

    /// <summary>
    /// Trigger rules 1-5 of 23.2 (6: the star-armed window, and 7: candidate search, are checked separately).
    /// F103 A2: <paramref name="requireProgress"/> false skips only the startProgress test, for TryRelocateOnTrail's honest phantoms;
    /// the hatch hunt, chase, ambush, hidden and distance rules still hold.
    /// </summary>
    private bool PassesCoreRules(float distance, bool requireProgress = true)
    {
        if (_maze == null || _player == null || _follower == null) return false;
        if (!GameFlow.IsRunActive || GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding)) return false;

        if (requireProgress && Progress < startProgress) return false;
        if (_follower.IsHunting) return false;
        if (_follower.IsChasing || _follower.IsCaptured) return false;
        // F45: an ambush is a beat of its own - it should not be interrupted by a relocation telegraph.
        if (_follower.IsAmbushing) return false;
        // F104 B1: a DreadMeter backoff must not be undone by a warp back onto the player's trail.
        if (_follower.IsBackingOff) return false;
        if (Time.time - _lastChaseEndTime < chaseCooldown) return false;
        if (_stealth != null && _stealth.Hidden) return false;
        if (!PlayerMovedRecently()) return false;
        if (_follower.FlatDistanceToTarget <= distance + jumpMargin) return false;

        return true;
    }

    /// <summary>
    /// Re-validates rules 1-5 and the spot with fresh positions, then warps the hunter and does the bookkeeping: stamps the
    /// cooldown, disarms (one relocation per star) and lights its eyes for a moment. Failing is not an error - a telegraph
    /// that cannot warp simply reads as a phantom. Shared by the telegraph, the silent warp and TryRelocateOnTrail.
    /// </summary>
    private bool CommitRelocation(Vector3 spot, float distance, bool requireProgress = true)
    {
        if (!PassesCoreRules(distance, requireProgress) || !ValidateCandidateVisibilityAndDistance(spot)) return false;
        if (!_follower.RelocateTo(spot, _player.position)) return false;

        _lastRelocationTime = Time.time;
        _armedUntil = -1f;
        if (_presence != null) _presence.RevealEyes(eyeRevealAfterWarpSeconds);
        return true;
    }

    /// <summary>
    /// F103 A2: an untelegraphed relocation onto the player's trail, for PhantomDirector's "honest" footsteps. Same rules and
    /// cooldowns as a star-armed relocation, minus the armed window and the startProgress gate - without that, an honest phantom
    /// would do nothing before 40 % progress, which is exactly when players learn that "phantom" means "safe".
    /// </summary>
    public bool TryRelocateOnTrail(float distance)
    {
        if (_isTelegraphing || Time.time - _lastRelocationTime < hardMinimumCooldown) return false;
        if (!PassesCoreRules(distance, requireProgress: false) || !TryFindRelocationSpot(distance, out Vector3 spot)) return false;

        return CommitRelocation(spot, distance, requireProgress: false);
    }

    /// <summary>A beat's offset from the trigger, scaled by a fresh roll in 1 +- jitter. Zero jitter returns the beat unchanged.</summary>
    private static float Jittered(float seconds, float jitter)
    {
        return seconds * Random.Range(1f - jitter, 1f + jitter);
    }

    private IEnumerator RelocateRoutine(Vector3 spot, float distance)
    {
        _isTelegraphing = true;

        // F103 A5: each beat gets its own roll, so the ritual never lands on the same count twice; a feint warps nothing.
        // With the flag off the jitter is zero and there is no feint: exactly the serialized timings.
        bool varied = _terror.unpredictableTelegraphs;
        float jitter = varied ? timingJitter : 0f;
        float stutter = Jittered(stutterAt, jitter);
        float cue = Mathf.Max(stutter + MinCueGapSeconds, Jittered(cueAt, jitter));
        float warp = Mathf.Max(cue + MinWarpGapSeconds, Jittered(warpAt, jitter));
        bool feint = varied && Random.value < feintChance;

        if (_presence != null) _presence.Hold(Mathf.Max(humHoldSeconds, warp + HumHoldTailSeconds));
        if (_audio != null) _audio.SkipBeat();

        yield return WaitSeconds(stutter);
        if (BailedOut()) { _isTelegraphing = false; yield break; }
        if (_flashlight != null) _flashlight.Stutter(stutterSeconds);

        yield return WaitSeconds(cue - stutter);
        if (BailedOut()) { _isTelegraphing = false; yield break; }
        if (_audio != null) _audio.PlayRelocationCue();

        yield return WaitSeconds(warp - cue);
        if (BailedOut())
        {
            _isTelegraphing = false;
            yield break;
        }

        if (feint)
        {
            // This star is spent, but no cooldown is stamped: the next one can still relocate.
            _armedUntil = -1f;
            _terror.Log("Feint telegraph", this);
        }
        else
        {
            CommitRelocation(spot, distance);
        }

        _isTelegraphing = false;
    }

    private bool BailedOut()
    {
        return GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding) || !GameFlow.IsRunActive;
    }

    private IEnumerator WaitSeconds(float seconds)
    {
        float t = 0f;
        while (t < seconds)
        {
            if (BailedOut()) yield break;
            t += Time.deltaTime;
            yield return null;
        }
    }

    private void HandleChaseStateChanged(bool chasing)
    {
        if (!chasing)
        {
            _lastChaseEndTime = Time.time;
            return;
        }

        // F103 A6: the first sighting of the floor, from far enough that the failure is a surprise rather than a pile-on.
        if (_sawFirstChase) return;
        _sawFirstChase = true;
        if (_follower != null && _follower.FlatDistanceToTarget >= sightingFailureMinHunterDistance)
        {
            StartCoroutine(SightingFailureRoutine());
        }
    }

    /// <summary>F103 A7: the game, not the hunter, says it - a fact the player caused and cannot see. The rush follows the listening pause.</summary>
    private void HandleHeardYou()
    {
        if (_hud != null) _hud.ShowSubtitle(L10n.T("sub.hunter.heard_you"), heardYouSubtitleSeconds);
    }

    // ---------------------------------------------------------------- F103 A6: scripted torch failures

    private IEnumerator SightingFailureRoutine()
    {
        yield return WaitSeconds(sightingFailureDelay);
        TryTorchFailure(RandomFailureSeconds(), "first sighting");
    }

    private float RandomFailureSeconds()
    {
        return Random.Range(torchFailureSeconds.x, torchFailureSeconds.y);
    }

    /// <summary>F103 A6: the player is standing in a dead end with the hunter close but not chasing - the torch dies on them there.</summary>
    private void CheckDeadEndFailure()
    {
        if (!_terror.torchFailures || _maze == null || _player == null || _follower == null) return;
        if (_follower.IsChasing || _follower.FlatDistanceToTarget > deadEndFailureHunterDistance) return;
        if (!_maze.IsDeadEndAt(_player.position, out Vector3 cell)) return;
        if (_hasLastDeadEndCell && (cell - _lastDeadEndCell).sqrMagnitude < 0.01f) return;

        // Remembered only if it fired, so a refused attempt (spacing, cap) can still fire on the next check.
        if (!TryTorchFailure(RandomFailureSeconds(), "dead end")) return;
        _lastDeadEndCell = cell;
        _hasLastDeadEndCell = true;
    }

    /// <summary>Is the floor's Stalker out and close to the player? Stan moving during a blackout is correct, but a blackout must never release one the player is holding frozen.</summary>
    private bool StalkerNearPlayer()
    {
        Stalker stalker = _maze != null ? _maze.Stalker : null;
        if (stalker == null || !stalker.gameObject.activeInHierarchy || stalker.IsHiddenAfterTouch) return false;
        return FlatDistance(stalker.transform.position, _player.position) < stalkerGuardDistance;
    }

    /// <summary>
    /// F103 A6: cuts the torch for `seconds` (and blinks the lamps around the player) if every fairness rule allows it.
    /// Capped per floor and spaced out, so each failure lands as a story beat rather than a bug.
    /// </summary>
    private bool TryTorchFailure(float seconds, string reason)
    {
        if (!_terror.torchFailures || BailedOut() || _flashlight == null || _player == null || _follower == null) return false;
        if (_torchFailures >= maxTorchFailuresPerFloor || Time.time - _lastTorchFailureTime < torchFailureSpacing) return false;
        if (!_flashlight.IsBeamLit || _isTelegraphing) return false;
        if (_stealth != null && _stealth.Hidden) return false;
        if (_follower.IsChasing && _follower.FlatDistanceToTarget < chaseGuardDistance) return false;
        if (StalkerNearPlayer()) return false;

        _flashlight.Blackout(seconds);
        BlinkLampsAround(_player.position, failureLampRadius, seconds);
        _torchFailures++;
        _lastTorchFailureTime = Time.time;
        _terror.Log($"TorchFailure ({reason}) {seconds:0.0}s", this);
        return true;
    }

    private void BlinkLampsAround(Vector3 centre, float radius, float seconds)
    {
        if (_maze == null) return;

        IReadOnlyList<WallLamp> lamps = _maze.Lamps;
        float radiusSqr = radius * radius;
        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp == null || lamp.IsDead || !lamp.IsPowered) continue;
            if ((lamp.transform.position - centre).sqrMagnitude <= radiusSqr) lamp.Blink(seconds);
        }
    }


    /// <summary>F45: the first time an ambush ends because its star was actually taken, show the subtitle - once per campaign.</summary>
    private void HandleAmbushStateChanged(bool ambushing)
    {
        if (ambushing || s_ambushSubtitleShown || _follower == null || !_follower.LastAmbushEndedOnPickup) return;

        s_ambushSubtitleShown = true;
        if (_hud != null) _hud.ShowSubtitle(Loc("sub.dread.ambushed", lineAmbushed), subtitleSeconds);
    }

    // ---------------------------------------------------------------- F27: reacting to the count

    private void HandleProgress(int collected, int total)
    {
        _collected = collected;
        _total = total;

        float progress = Progress;

        if (!_firstDeathsFired && progress >= firstDeathProgress)
        {
            _firstDeathsFired = true;
            KillLampWave();
            if (_atmosphere != null) _atmosphere.Darken(firstDarkenFraction, darkenSeconds);
            if (_hud != null) _hud.ShowSubtitle(Loc("sub.dread.lights_dying", lineLightsDying), subtitleSeconds);
        }

        if (!_secondDeathsFired && progress >= secondDeathProgress)
        {
            _secondDeathsFired = true;
            KillLampWave();
            if (_atmosphere != null) _atmosphere.Darken(secondDarkenFraction, darkenSeconds);
            if (_presence != null) _presence.SetEyeEmission(eyeEmissionAtSecondDeath);
        }
    }

    /// <summary>Raised before ProgressChanged for the same star - drives the flicker wave and arms one relocation.</summary>
    private void HandleStarTaken(Vector3 at)
    {
        if (_maze != null)
        {
            IReadOnlyList<WallLamp> lamps = _maze.Lamps;
            for (int i = 0; i < lamps.Count; i++)
            {
                WallLamp lamp = lamps[i];
                // F73: an unpowered lamp (Blackout floor, before its fuse) is already dark - a blink
                // would be invisible, and Update skips its own flicker tick while unpowered anyway.
                if (lamp == null || lamp.IsDead || !lamp.IsPowered) continue;

                float delay = Vector3.Distance(lamp.transform.position, at) / flickerWaveSpeed;
                lamp.Blink(flickerDuration, delay);
            }
        }

        // F105 C5: the lure answers at once instead of arming a relocation - and before the ambush test below, since
        // a lure taken under an ambush is exactly the case that turns into a chase.
        if (_terror.lureStars && _maze != null && _maze.IsLure(at))
        {
            HandleLureTaken(at);
            return;
        }

        // F45: the hunter is already stepping out toward this exact pickup (TensionDirector's own
        // Pickup.OnCollectedAt subscription - added earlier, so it runs first - just called HearNoise,
        // which ends a matching ambush synchronously and stamps LastAmbushEndTime). Warping it elsewhere
        // would waste that beat, so this pickup arms no relocation.
        if (_follower != null && Time.time - _follower.LastAmbushEndTime < AmbushJustEndedSeconds) return;

        // Arm one relocation (23.2 rule 6). The rules (progress, not chasing, not hidden, not hunting, a
        // valid spot) are still checked every tick inside the window; if they never pass, the window
        // simply closes and this star produced no relocation.
        _armedUntil = Time.time + armedWindowSeconds;
        _checkTimer = 0f;
    }

    // ---------------------------------------------------------------- F105 C5: the lure star

    /// <summary>
    /// F105 C5: the lure star was taken. A hunter lying in wait for it goes straight to a chase (sight is still needed to capture);
    /// otherwise it is put back along the player's trail, as an honest phantom would. Either way the lamps around the star blink
    /// and some die. Nothing is announced: the trap is in the blood under the star and the dark that follows.
    /// </summary>
    private void HandleLureTaken(Vector3 at)
    {
        bool chasing = TryLureChase(at);
        bool relocated = !chasing && TryRelocateOnTrail(lureRelocateDistance);
        int dead = KillLampsAroundLure(at);
        _terror.Log($"Lure star taken (chase: {chasing}, relocated: {relocated}, lamps killed: {dead})", this);
    }

    private bool TryLureChase(Vector3 at)
    {
        if (_follower == null || _player == null || BailedOut()) return false;

        bool lyingInWait = _follower.IsAmbushing
            || (_follower.LastAmbushEndedOnPickup && Time.time - _follower.LastAmbushEndTime < AmbushJustEndedSeconds);
        return lyingInWait && FlatDistance(_follower.transform.position, at) <= lureAmbushRadius && _follower.StartLureChase(_player.position);
    }

    /// <summary>Blinks every live lamp within lureLampRadius of the star and kills about half of the non-locker ones that no other live star depends on. Returns how many died.</summary>
    private int KillLampsAroundLure(Vector3 at)
    {
        if (_maze == null) return 0;

        IReadOnlyList<WallLamp> lamps = _maze.Lamps;
        float radiusSqr = lureLampRadius * lureLampRadius;
        int dead = 0;
        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp == null || lamp.IsDead || !lamp.IsPowered) continue;
            if ((lamp.transform.position - at).sqrMagnitude > radiusSqr) continue;

            lamp.Blink(lureBlinkSeconds);
            if (lamp.IsLockerLamp || Random.value >= lureLampDeathChance || _maze.IsNearLiveStar(lamp.transform.position, _maze.LureStar)) continue;

            lamp.Kill(deathFadeSeconds);
            dead++;
        }

        return dead;
    }

    private void HandleAllStarsCollected()
    {
        if (_maze != null)
        {
            IReadOnlyList<WallLamp> lamps = _maze.Lamps;
            for (int i = 0; i < lamps.Count; i++)
            {
                WallLamp lamp = lamps[i];
                if (lamp != null && !lamp.IsDead && lamp.IsPowered) lamp.Blink(breathSeconds);
            }
        }

        if (_audio != null) _audio.HoldBreath(breathSeconds);
        if (_presence != null) _presence.Hold(breathSeconds);
        // F103 A6: replaces the cut "it knows" subtitle - the torch dies for the same breath the lamps hold.
        TryTorchFailure(breathSeconds, "last star");
    }

    /// <summary>
    /// Kills ceil(originalLiveNonLockerLamps * deathWaveFraction) lamps, chosen by one RNG seeded from
    /// the maze's seed and reused across both waves - so a same-seed retry kills the same lamps at the
    /// same star both times, and wave 2 does not repeat wave 1's picks.
    /// </summary>
    private void KillLampWave()
    {
        if (_maze == null) return;
        IReadOnlyList<WallLamp> lamps = _maze.Lamps;

        if (_originalLiveLampCount < 0)
        {
            int count = 0;
            for (int i = 0; i < lamps.Count; i++)
            {
                if (lamps[i] != null && !lamps[i].IsLockerLamp) count++;
            }
            _originalLiveLampCount = count;
        }

        if (_lampRng == null) _lampRng = new System.Random(_maze.UsedSeed + 7);

        List<WallLamp> candidates = new List<WallLamp>();
        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            // F71: a lamp within 1 cell of a live star is exempt, same reasoning as the locker-lamp
            // exemption above it - a star you cannot find is not a reward, it is a bug.
            // F73: an unpowered lamp (Blackout floor, before its fuse) has nothing to kill.
            if (lamp != null && !lamp.IsLockerLamp && !lamp.IsDead && lamp.IsPowered && !_maze.IsNearLiveStar(lamp.transform.position)) candidates.Add(lamp);
        }

        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = _lampRng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        int killCount = Mathf.Max(1, Mathf.CeilToInt(_originalLiveLampCount * deathWaveFraction));
        for (int i = 0; i < killCount && i < candidates.Count; i++)
        {
            candidates[i].Kill(deathFadeSeconds);
        }
    }
}
