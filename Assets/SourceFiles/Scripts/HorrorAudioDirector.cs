using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player-facing dread cues: a heartbeat that quickens as the hunter closes, a sting when it spots
/// you, and a low drone underneath everything.
///
/// The heartbeat uses straight-line distance rather than path distance on purpose (F104 B1: with a DreadMeter bound it is combined with the meter). The AI reasons about
/// the maze topologically because it has to walk it; the player feels space, so a heartbeat that spikes
/// when the thing is one wall away - even if that is thirty metres of corridor - is the correct lie.
/// </summary>
public class HorrorAudioDirector : MonoBehaviour
{
    [Header("Heartbeat")]
    [Tooltip("Distance at which the heartbeat becomes audible")]
    [SerializeField] private float heartbeatRange = 18f;
    [Tooltip("Seconds between beats at the edge of the range")]
    [SerializeField] private float slowestInterval = 1.2f;
    [Tooltip("Seconds between beats when it is right on top of you")]
    [SerializeField] private float fastestInterval = 0.45f;
    [Tooltip("Volume when the hunter is right on top of you")]
    [SerializeField] private float heartbeatVolume = 1f;
    [Tooltip("Volume at the very edge of the range - audible, but only just")]
    [SerializeField] private float minHeartbeatVolume = 0.12f;
    [Tooltip("Above 1 holds the heartbeat quiet until it is genuinely close, then ramps hard. 1 = straight line.")]
    [SerializeField] private float proximityFalloff = 2f;

    [Header("F104 B1 - dread heartbeat")]
    [Tooltip("Time constant of the easing on the distance term, so a hunter stepping into range does not jump the heart (s)")]
    [SerializeField] private float heartbeatDistanceLag = 3f;
    [Tooltip("The hunter's distance term is weighted by this when a DreadMeter drives the heart")]
    [SerializeField] private float heartbeatDistanceWeight = 0.6f;
    [Tooltip("Cap on the meter's own contribution: the heart races from a sprint or the dark, but never as hard as a hunter on top of you")]
    [SerializeField] private float heartbeatSelfCap = 0.75f;
    [Tooltip("Combined closeness above which the heart beats at all. The meter's progress floor alone must never start it, or it would beat for the whole second half of a floor.")]
    [SerializeField] private float heartbeatThreshold = 0.2f;

    [Header("Sting")]
    [SerializeField] private AudioClip stingClip;
    [SerializeField] private float stingPitch = 0.55f;
    [SerializeField] private float stingVolume = 0.8f;
    [Tooltip("The clip to hand may be a minutes-long loop, so the sting is cut off after this many seconds")]
    [SerializeField] private float stingDuration = 1.5f;

    [Header("Spotted")]
    [Tooltip("Played whole when the hunter starts a chase (Violin Stabs). Empty = the synthesised sting above, cut to Sting Duration.")]
    [SerializeField] private AudioClip spottedClip;
    [SerializeField] private float spottedVolume = 0.6f;
    [Tooltip("Seconds before the spotted cue may play again - the hunter can lose and regain a chase in a few seconds")]
    [SerializeField] private float spottedCooldown = 10f;
    [Tooltip("F103 A9: chance a chase starts with no sting at all, from silentSpottedFromFloor on")]
    [Range(0f, 1f)]
    [SerializeField] private float silentSpottedChance = 0.15f;
    [Tooltip("F103 A9: first floor on which a chase may start in silence (floors 1-2 always sting, so the cue is learned first)")]
    [SerializeField] private int silentSpottedFromFloor = 3;

    [Header("Drone")]
    [SerializeField] private AudioClip droneClip;
    [SerializeField] private float dronePitch = 0.6f;
    [SerializeField] private float droneVolume = 0.1f;
    [Tooltip("Drone volume when the bed is a composed base-music track (Bind's droneIsMusic). Set by ear for the PSX pack, whose loops are ~1.4x louder than Music_Suspenseful.")]
    [SerializeField] private float musicVolume = 0.15f;
    [Tooltip("The base music's volume at full progress (it swells, never re-pitches)")]
    [SerializeField] private float musicVolumeAtPeak = 0.3f;

    [Header("Escalation")]
    [Tooltip("How much louder the drone gets at full progress")]
    [SerializeField] private float droneVolumeAtPeak = 0.3f;
    [Tooltip("The drone creeps upward in pitch as things get worse")]
    [SerializeField] private float dronePitchAtPeak = 0.72f;
    [Tooltip("The heartbeat starts carrying from further away as progress climbs")]
    [SerializeField] private float heartbeatRangeAtPeak = 28f;

    [Header("Dread cues")]
    [Tooltip("Volume of the F23 relocation cue (the low sweep-and-whump)")]
    [SerializeField] private float relocationCueVolume = 0.8f;
    [Tooltip("Volume of the F26 whisper phantom")]
    [SerializeField] private float whisperVolume = 0.18f;

    [Header("Panic")]
    [Tooltip("Played on a loop once the hatch opens")]
    [SerializeField] private AudioClip panicClip;
    [Tooltip("The escape track (The Urge to Kill). 0.5 puts it ~2.5x the base music at its peak, a clear lift when the hatch opens; its peaks (0.92) stay under clipping. 0.2 was too quiet in play (7 Oct 2026).")]
    [SerializeField] private float panicVolume = 0.5f;
    [Tooltip("1 for a track written as horror (The Urge to Kill, since 7 Oct 2026). The old Music_Exciting placeholder needed 0.78: at native pitch it read as a victory fanfare.")]
    [SerializeField] private float panicPitch = 1f;

    [Header("F104 B5 - silence windows")]
    [Tooltip("How long the floor music plays before it leaves (s, min/max)")]
    [SerializeField] private Vector2 musicSeconds = new Vector2(60f, 120f);
    [Tooltip("How long a room-tone stretch lasts before it ends or drops into dead air (s, min/max)")]
    [SerializeField] private Vector2 roomToneSeconds = new Vector2(30f, 60f);
    [Tooltip("How long dead air lasts (s, min/max). The maze's own wind continues through it.")]
    [SerializeField] private Vector2 deadAirSeconds = new Vector2(10f, 20f);
    [Tooltip("Chance a finished room-tone stretch drops on into dead air")]
    [Range(0f, 1f)]
    [SerializeField] private float deadAirChance = 0.3f;
    [Tooltip("Seconds the music and the room tone take to trade places (s)")]
    [SerializeField] private float bedCrossfadeSeconds = 4f;
    [Tooltip("Room-tone loop volume")]
    [SerializeField] private float roomToneVolume = 0.35f;

    [Header("F104 B4 - escape hunt")]
    [Tooltip("With the escape hunt on, the panic track waits this long after the hatch opens; the floor music carries the first seconds (s)")]
    [SerializeField] private float panicMusicDelay = 20f;
    [Tooltip("The panic track's gain when the hunter is right on top of the player, so its footsteps cut through")]
    [Range(0f, 1f)]
    [SerializeField] private float threatDuckGain = 0.4f;
    [Tooltip("Flat distance to the hunter at which the panic track starts to duck (m)")]
    [SerializeField] private float threatDuckRange = 15f;
    [Tooltip("Seconds the threat duck takes to follow the hunter (s)")]
    [SerializeField] private float threatDuckEaseSeconds = 0.3f;

    private Transform _player;
    private Transform _hunter;
    private AIFollower _follower;

    private AudioSource _oneShots;
    private AudioSource _sting;
    private float _nextSpottedTime;
    // F103 A9: spottedClip plus the library's alternatives, each with its volume; one is picked per chase, never the same twice running.
    private readonly List<AudioClip> _spottedPool = new List<AudioClip>();
    private readonly List<float> _spottedVolumes = new List<float>();
    private int _lastSpottedIndex = -1;
    private TerrorSettings _terror = new TerrorSettings();
    private bool _bedWaitingForRun;
    private AudioSource _drone;
    private AudioSource _whisper;
    private AudioClip _heartbeat;
    private AudioClip _relocationCue;
    private AudioClip _whisperClip;
    private float _beatTimer;
    private float _heartbeatMutedUntil = -1f; // F123 floor 4: an ampoule silences the heart
    private float _intensity;
    private bool _panicking;
    private Coroutine _holdBreathRoutine;
    // F104 B1: the meter that makes the heart race, and the eased hunter-distance term it is combined with.
    private DreadMeter _dread;
    private float _distanceTerm;
    // F104 B5: what the bed wants (SetIntensity / BeginPanic), and the independent gains that scale it. ApplyBedVolume multiplies them
    // every frame, so a breath hold, a duck, a silence window and the threat duck can overlap without undoing each other.
    private enum BedState { Music, RoomTone, DeadAir }
    private float _bedVolume;
    private float _breathGain = 1f;
    private float _duckGain = 1f;
    private float _duckTarget = 1f;
    private float _duckRate;
    private float _musicGain = 1f;
    private float _roomToneGain;
    private float _threatDuckGain = 1f;
    private AudioSource _roomTone;
    private float _roomToneLevel;
    private BedState _bedState = BedState.Music;
    private float _bedStateEndsAt = -1f;
    private Coroutine _panicDelayRoutine;
    private bool _escapeBegun;

    /// <summary>F123 floor 4: no heartbeat for `seconds` (an ampoule), whether or not the dread meter is active.</summary>
    public void SuppressHeartbeat(float seconds) => _heartbeatMutedUntil = Mathf.Max(_heartbeatMutedUntil, Time.time + Mathf.Max(0f, seconds));

    /// <summary>F123: true while the bed is ducked (the held breath, the loop corridor): the pipe knocks stay silent then.</summary>
    public bool IsDucked => _duckTarget < 0.99f;

    /// <summary>
    /// Wired by MazeGenerator, which already knows about both actors. droneIsMusic: the bed is a composed track (the
    /// floor's base music) rather than a clip slowed into a drone - it plays at its own pitch, and escalation only
    /// makes it louder, because pitching music up and down detunes it.
    /// </summary>
    public void Bind(Transform player, AIFollower follower, AudioClip sting, AudioClip drone, AudioClip panic, bool droneIsMusic = false, AudioClip spotted = null)
    {
        if (spotted != null) spottedClip = spotted;
        if (droneIsMusic)
        {
            dronePitch = 1f;
            dronePitchAtPeak = 1f;
            droneVolume = musicVolume;
            droneVolumeAtPeak = musicVolumeAtPeak;
        }

        _player = player;
        _follower = follower;
        if (follower != null) _hunter = follower.transform;
        // The hunter's own synthesised sting always wins over whatever clip MazeGenerator hands over
        stingClip = AngelAudio.BuildSting();
        stingPitch = 1f;
        stingDuration = 2.5f;
        if (drone != null) droneClip = drone;
        if (panic != null) panicClip = panic;
    }

    /// <summary>F104 B1: raised each time a heartbeat plays. HorrorAtmosphere pulses the vignette on it.</summary>
    public event System.Action HeartBeat;

    /// <summary>F104 B1: the DreadMeter that makes the heart race. Null (or an inactive meter) leaves the heartbeat purely distance-driven, exactly as before.</summary>
    public void BindDread(DreadMeter meter)
    {
        _dread = meter;
    }

    /// <summary>0 = nothing collected yet, 1 = every star taken. Drives everything that swells.</summary>
    public void SetIntensity(float normalized)
    {
        _intensity = Mathf.Clamp01(normalized);

        // Once the panic track is on the drone source, leave its pitch and volume alone
        if (_drone != null && !_panicking)
        {
            _bedVolume = Mathf.Lerp(droneVolume, droneVolumeAtPeak, _intensity);
            _drone.pitch = Mathf.Lerp(dronePitch, dronePitchAtPeak, _intensity);
        }
    }

    /// <summary>
    /// The hatch is open and the hunter is loose. Bring in the panic loop. F104 B4: with the escape hunt on, the floor's music
    /// (forced back in if a silence window had taken it) carries the first panicMusicDelay seconds and the track arrives after.
    /// </summary>
    public void BeginPanic()
    {
        if (panicClip == null || _drone == null) return;

        _escapeBegun = true;
        EnterBedState(BedState.Music);
        if (!_terror.escapeHunt || panicMusicDelay <= 0f)
        {
            StartPanicTrack();
            return;
        }

        if (_panicDelayRoutine == null) _panicDelayRoutine = StartCoroutine(PanicAfterDelay());
    }

    private IEnumerator PanicAfterDelay()
    {
        float t = 0f;
        while (t < panicMusicDelay)
        {
            // Disabling this component does not stop a running coroutine, so check every frame (see HoldBreathRoutine).
            if (!enabled || GameOutcome.IsOver)
            {
                _panicDelayRoutine = null;
                yield break;
            }

            t += Time.deltaTime;
            yield return null;
        }

        _panicDelayRoutine = null;
        StartPanicTrack();
    }

    /// <summary>Reuses the drone source: the panic track replaces the bed rather than piling on top of it.</summary>
    private void StartPanicTrack()
    {
        _panicking = true;
        _drone.clip = panicClip;
        _drone.pitch = panicPitch;
        _bedVolume = panicVolume;
        ApplyBedVolume();
        _drone.Play();
    }

    /// <summary>
    /// Stop everything this director is playing. Disabling the component is not enough on its own -
    /// the AudioSources keep running, which would leave the panic track playing over the win screen.
    /// </summary>
    public void Silence()
    {
        if (_drone != null) _drone.Stop();
        if (_roomTone != null) _roomTone.Stop();
        if (_sting != null) _sting.Stop();
        if (_oneShots != null) _oneShots.Stop();
        if (_whisper != null) _whisper.Stop();

        if (_holdBreathRoutine != null)
        {
            StopCoroutine(_holdBreathRoutine);
            _holdBreathRoutine = null;
        }
        if (_panicDelayRoutine != null)
        {
            StopCoroutine(_panicDelayRoutine);
            _panicDelayRoutine = null;
        }
    }

    private void Awake()
    {
        _oneShots = gameObject.AddComponent<AudioSource>();
        _oneShots.playOnAwake = false;
        _oneShots.spatialBlend = 0f;

        // Its own source: sharing with the heartbeat means the next beat's pitch reset yanks a
        // still-playing sting halfway through.
        _sting = gameObject.AddComponent<AudioSource>();
        _sting.playOnAwake = false;
        _sting.spatialBlend = 0f;

        _drone = gameObject.AddComponent<AudioSource>();
        _drone.playOnAwake = false;
        _drone.loop = true;
        _drone.spatialBlend = 0f;
        GameSettings.RouteMusic(_drone); // S2: the base music bed and the panic track share this source

        // F104 B5: the silence windows' room tone. Ambience, not music, so it follows the Effects slider.
        _roomTone = gameObject.AddComponent<AudioSource>();
        _roomTone.playOnAwake = false;
        _roomTone.loop = true;
        _roomTone.spatialBlend = 0f;
        GameSettings.RouteEffect(_roomTone);

        // There used to be a fourth source here, a synthesised "tension bed" (two detuned 55 Hz tones
        // beating against each other, swelling with star progress). Removed 4 Oct 2026: it read as a
        // buzz that got louder before the last star. Escalation is now the drone's volume/pitch and the
        // heartbeat's range alone.

        // Its own 2D source: PlayWhisper drives panStereo directly, which would yank a concurrent
        // heartbeat sideways if they shared a source.
        _whisper = gameObject.AddComponent<AudioSource>();
        _whisper.playOnAwake = false;
        _whisper.spatialBlend = 0f;

        _heartbeat = BuildHeartbeatClip();
    }

    private void OnEnable()
    {
        Pickup.OnCollectedAt += HandleStarTaken; // F104 B5: taking a star brings the music back
        if (_follower != null)
        {
            _follower.ChaseStateChanged += HandleChaseStateChanged;
        }
    }

    private void Start()
    {
        // Bind runs before OnEnable when the component is added at runtime, but subscribing twice is
        // harmless to guard against and missing the subscription is not.
        if (_follower != null)
        {
            _follower.ChaseStateChanged -= HandleChaseStateChanged;
            _follower.ChaseStateChanged += HandleChaseStateChanged;
        }

        BuildSpottedPool();
        PickRoomTone();

        if (droneClip != null)
        {
            _drone.clip = droneClip;
            _drone.pitch = dronePitch;
            _bedVolume = droneVolume;
            ApplyBedVolume();
            // Behind the title screen the menu has its own music: the bed waits for the run (see Update).
            if (GameFlow.IsRunActive) _drone.Play();
            else _bedWaitingForRun = true;
        }
    }

    private void OnDisable()
    {
        Pickup.OnCollectedAt -= HandleStarTaken;
        if (_follower != null)
        {
            _follower.ChaseStateChanged -= HandleChaseStateChanged;
        }
    }

    private void Update()
    {
        if (_bedWaitingForRun && GameFlow.IsRunActive)
        {
            _bedWaitingForRun = false;
            if (_drone != null && !_panicking) _drone.Play();
        }
        // F104: the bed's gains (silence windows, held-breath duck, threat duck) are advanced and applied every frame.
        TickBed();
        TickDuck();
        TickThreatDuck();
        ApplyBedVolume();

        if (_player == null || _hunter == null) return;

        if (Time.time < _heartbeatMutedUntil)
        {
            _beatTimer = 0f; // F123
            return;
        }

        // The heartbeat carries further as the maze empties out
        float range = Mathf.Lerp(heartbeatRange, heartbeatRangeAtPeak, _intensity);

        float distance = Vector3.Distance(_player.position, _hunter.position);
        float closeness;
        if (_dread != null && _dread.IsActive)
        {
            // F104 B1: a near hunter still reads near, but a sprint or the dark also make the heart race (the Psychologist's max).
            float rawDistance = distance > range ? 0f : 1f - Mathf.Clamp01(distance / range);
            _distanceTerm = Mathf.Lerp(_distanceTerm, rawDistance, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, heartbeatDistanceLag)));
            closeness = Mathf.Max(_distanceTerm * heartbeatDistanceWeight, Mathf.Min(_dread.Smoothed, heartbeatSelfCap));
            if (closeness <= heartbeatThreshold)
            {
                _beatTimer = 0f;
                return;
            }

            closeness = Mathf.InverseLerp(heartbeatThreshold, 1f, closeness);
        }
        else
        {
            if (distance > range)
            {
                _beatTimer = 0f;
                return;
            }

            closeness = 1f - Mathf.Clamp01(distance / range);
        }

        float interval = Mathf.Lerp(slowestInterval, fastestInterval, closeness);

        _beatTimer -= Time.deltaTime;
        if (_beatTimer > 0f) return;

        _beatTimer = interval;
        // One-shots on a variable interval rather than a looping clip with its pitch modulated:
        // pitching a loop couples tempo to pitch and the heartbeat turns into a chipmunk.
        _oneShots.pitch = 1f;

        // Curved rather than straight: a linear ramp spends most of the range around half volume,
        // which reads as a constant thud. This stays faint until it is close, then climbs fast.
        float loudness = Mathf.Pow(closeness, proximityFalloff);
        float volume = Mathf.Lerp(minHeartbeatVolume, heartbeatVolume, loudness);
        AudioSlot slot = GameAudio.Library.heartbeat;
        AudioClip recorded = slot.Pick();
        if (recorded != null) _oneShots.PlayOneShot(recorded, volume * slot.volume);
        else _oneShots.PlayOneShot(_heartbeat, volume);
        HeartBeat?.Invoke();
    }

    // ---------------------------------------------------------------- F104 B5: the bed's gains and silence windows

    /// <summary>
    /// The one place the bed's volume is written: what SetIntensity / BeginPanic want, times every independent gain. With every
    /// Tier B feature off all the gains are 1, so this reduces to the old direct writes.
    /// </summary>
    private void ApplyBedVolume()
    {
        if (_drone != null) _drone.volume = _bedVolume * _breathGain * _duckGain * _musicGain * _threatDuckGain;
        if (_roomTone != null) _roomTone.volume = _roomToneLevel * _roomToneGain * _duckGain;
    }

    /// <summary>
    /// F104 B11: eases the whole bed (music and room tone) to `gain` over `seconds`, e.g. 0 for a held breath and back to 1.
    /// Independent of the silence windows and the breath hold, which scale the same volume.
    /// </summary>
    public void Duck(float gain, float seconds)
    {
        _duckTarget = Mathf.Clamp01(gain);
        _duckRate = seconds > 0.0001f ? Mathf.Abs(_duckTarget - _duckGain) / seconds : float.MaxValue;
    }

    private void TickDuck()
    {
        _duckGain = Mathf.MoveTowards(_duckGain, _duckTarget, _duckRate * Time.deltaTime);
    }

    /// <summary>True while the floor's music is what the bed is playing (not a silence window), so a held breath has something to take away.</summary>
    public bool IsBedMusic => _bedState == BedState.Music;

    /// <summary>F104 B5: a room-tone loop for this floor, chosen once. No clip = no silence windows (taking the music away for nothing would just be a bug).</summary>
    private void PickRoomTone()
    {
        AudioSlot slot = GameAudio.Library.mazeRoomTone;
        AudioClip clip = slot.Pick();
        if (clip == null || _roomTone == null) return;

        _roomTone.clip = clip;
        _roomToneLevel = roomToneVolume * slot.volume;
    }

    /// <summary>The state machine: music for a while, then room tone, sometimes dead air; only an event brings the music back.</summary>
    private void TickBed()
    {
        bool windowsLive = _roomTone != null && _roomTone.clip != null && _terror.silenceWindows && !_bedWaitingForRun && !_escapeBegun
            && GameFlow.IsRunActive && !GameOutcome.IsOver;
        if (windowsLive)
        {
            // The first music stretch starts with the run, not with the scene load behind the title screen.
            if (_bedStateEndsAt < 0f) _bedStateEndsAt = RollBedEnd(BedState.Music);
            else if (Time.time >= _bedStateEndsAt) AdvanceBedState();
        }
        else if (!_terror.silenceWindows && _bedState != BedState.Music)
        {
            // Switched off mid-window (Inspector A/B test): bring the music straight back rather than strand it at zero.
            EnterBedState(BedState.Music);
        }

        float step = Time.deltaTime / Mathf.Max(0.0001f, bedCrossfadeSeconds);
        _musicGain = Mathf.MoveTowards(_musicGain, _bedState == BedState.Music ? 1f : 0f, step);
        _roomToneGain = Mathf.MoveTowards(_roomToneGain, _bedState == BedState.RoomTone ? 1f : 0f, step);

        if (_roomTone == null || _roomTone.clip == null) return;
        if (_roomToneGain > 0f && !_roomTone.isPlaying && !GameOutcome.IsOver) _roomTone.Play();
        else if (_roomToneGain <= 0f && _roomTone.isPlaying) _roomTone.Pause();
    }

    /// <summary>Called when the current state's timer has run out. Music -> room tone; room tone -> dead air or another stretch; dead air -> room tone.</summary>
    private void AdvanceBedState()
    {
        switch (_bedState)
        {
            case BedState.Music:
                EnterBedState(BedState.RoomTone);
                break;
            case BedState.RoomTone:
                EnterBedState(Random.value < deadAirChance ? BedState.DeadAir : BedState.RoomTone);
                break;
            default:
                EnterBedState(BedState.RoomTone);
                break;
        }
    }

    private void EnterBedState(BedState state)
    {
        bool changed = state != _bedState;
        _bedState = state;
        _bedStateEndsAt = RollBedEnd(state);
        if (changed) _terror.Log("Bed: " + state, this);
    }

    /// <summary>The Time.time a stretch of `state` ends, rolled from its range.</summary>
    private float RollBedEnd(BedState state)
    {
        Vector2 range = state == BedState.Music ? musicSeconds : state == BedState.RoomTone ? roomToneSeconds : deadAirSeconds;
        return Time.time + Random.Range(range.x, range.y);
    }

    /// <summary>A chase or a star brings the music back; nothing timed ever does, so its return cannot be predicted.</summary>
    private void ReturnMusic()
    {
        if (_bedState != BedState.Music) EnterBedState(BedState.Music);
    }

    private void HandleStarTaken(Vector3 at) => ReturnMusic();

    /// <summary>F104 B4: while the panic track plays, the hunter's nearness ducks it so the footsteps cut through. Untouched (1) otherwise and with the flag off.</summary>
    private void TickThreatDuck()
    {
        float target = 1f;
        if (_terror.escapeHunt && _panicking && _player != null && _hunter != null)
        {
            Vector3 offset = _player.position - _hunter.position;
            offset.y = 0f;
            float closeness = 1f - Mathf.Clamp01(offset.magnitude / Mathf.Max(0.0001f, threatDuckRange));
            target = Mathf.Lerp(1f, threatDuckGain, closeness);
        }

        if (Mathf.Approximately(_threatDuckGain, target)) return;
        _threatDuckGain = Mathf.Lerp(_threatDuckGain, target, 1f - Mathf.Exp(-Time.deltaTime / Mathf.Max(0.0001f, threatDuckEaseSeconds)));
    }

    /// <summary>F103: which Tier A features run, and the [Terror] log.</summary>
    public void ApplyTerror(TerrorSettings terror)
    {
        _terror = terror ?? new TerrorSettings();
    }

    /// <summary>F103 A9: the spotted cue plus every non-null clip of the library's spottedSting slot, built once (the slot's own loudness trim applies to its clips).</summary>
    private void BuildSpottedPool()
    {
        _spottedPool.Clear();
        _spottedVolumes.Clear();

        if (spottedClip != null)
        {
            _spottedPool.Add(spottedClip);
            _spottedVolumes.Add(spottedVolume);
        }

        AudioSlot slot = GameAudio.Library.spottedSting;
        for (int i = 0; i < slot.clips.Length; i++)
        {
            if (slot.clips[i] == null) continue;
            _spottedPool.Add(slot.clips[i]);
            _spottedVolumes.Add(spottedVolume * slot.volume);
        }
    }

    /// <summary>A pool index other than the one used last time, while the pool has more than one entry.</summary>
    private int PickSpottedIndex()
    {
        int index = Random.Range(0, _spottedPool.Count);
        if (_spottedPool.Count > 1 && index == _lastSpottedIndex) index = (index + 1 + Random.Range(0, _spottedPool.Count - 1)) % _spottedPool.Count;

        _lastSpottedIndex = index;
        return index;
    }

    private void HandleChaseStateChanged(bool chasing)
    {
        if (!chasing) return;

        ReturnMusic(); // F104 B5: a chase brings the music back

        // A real "spotted" cue plays whole, at its own pitch, no more than once per spottedCooldown -
        // the hunter can drop and regain a chase within seconds, and a stab on every flicker turns into noise.
        bool varied = _terror.varyingSpottedSting && _spottedPool.Count > 0;
        if (varied || spottedClip != null)
        {
            if (Time.time < _nextSpottedTime) return;
            _nextSpottedTime = Time.time + spottedCooldown;

            AudioClip spotted = spottedClip;
            float volume = spottedVolume;
            if (varied)
            {
                // F103 A9: from floor 3 some chases start in silence, so the sting's absence stops meaning "not chased".
                if (GameFlow.CurrentFloor >= silentSpottedFromFloor && Random.value < silentSpottedChance)
                {
                    _terror.Log("Silent chase start", this);
                    return;
                }

                int index = PickSpottedIndex();
                spotted = _spottedPool[index];
                volume = _spottedVolumes[index];
                _terror.Log("Spotted sting: " + spotted.name, this);
            }

            _sting.clip = spotted;
            _sting.pitch = 1f;
            _sting.volume = volume;
            _sting.Play();
            // Same source as the capture sting, which schedules a cut-off - replace it with this clip's own end.
            _sting.SetScheduledEndTime(AudioSettings.dspTime + spotted.length + 0.1);
            return;
        }

        if (stingClip == null) return;

        _sting.clip = stingClip;
        _sting.pitch = stingPitch;
        _sting.volume = stingVolume;
        _sting.Play();
        // The clip may well be a long ambience loop rather than a stinger, so bound it rather than
        // trusting its length.
        _sting.SetScheduledEndTime(AudioSettings.dspTime + stingDuration);
    }

    /// <summary>The sting for being caught: louder and lower than the chase one, with the bed cut out under it.</summary>
    public void PlayCaptureSting()
    {
        if (_drone != null) _drone.Stop();
        if (_roomTone != null) _roomTone.Stop();
        AudioSlot recorded = GameAudio.Library.captureSting; // F97: the recorded stinger when the slot is filled
        if (_sting != null && recorded.Has)
        {
            _sting.clip = recorded.Pick();
            _sting.pitch = 1f;
            _sting.volume = Mathf.Clamp01(recorded.volume);
            _sting.Play();
            _sting.SetScheduledEndTime(AudioSettings.dspTime + Mathf.Min(_sting.clip.length, 5.5f));
            return;
        }
        if (stingClip == null || _sting == null) return;

        _sting.clip = stingClip;
        _sting.pitch = stingPitch * 0.8f;
        _sting.volume = 1f;
        _sting.Play();
        _sting.SetScheduledEndTime(AudioSettings.dspTime + 2.5f);
    }

    /// <summary>After PlayCaptureSting stopped the bed for a Second Wind: bring the drone back at its current intensity.</summary>
    public void ResumeBed()
    {
        if (_drone != null && _drone.clip != null && !_drone.isPlaying) _drone.Play();
        SetIntensity(_intensity);
    }

    /// <summary>F23 telegraph: skips the next heartbeat by holding it silent a little longer than usual.</summary>
    public void SkipBeat()
    {
        _beatTimer = Mathf.Max(_beatTimer, 1.6f);
    }

    /// <summary>F23 telegraph: a low sweep with a "whump" landing, synthesised once and cached.</summary>
    public void PlayRelocationCue()
    {
        if (GameAudio.Play2D(GameAudio.Library.relocationCue, relocationCueVolume)) return; // F97
        if (_relocationCue == null) _relocationCue = BuildRelocationCueClip();
        if (_oneShots == null) return;

        _oneShots.pitch = 1f;
        _oneShots.PlayOneShot(_relocationCue, relocationCueVolume);
    }

    /// <summary>F26 phantom: a breathy whisper panned hard to one ear. pan is -1 (left) to 1 (right).</summary>
    public void PlayWhisper(float pan)
    {
        if (GameAudio.Play2D(GameAudio.Library.phantomWhisper, whisperVolume, 1f, pan)) return; // F97
        if (_whisperClip == null) _whisperClip = BuildWhisperClip();
        if (_whisper == null) return;

        _whisper.panStereo = Mathf.Clamp(pan, -1f, 1f);
        _whisper.volume = whisperVolume;
        _whisper.clip = _whisperClip;
        _whisper.Play();
    }

    /// <summary>
    /// F27's last-star beat: the drone drops to silence, held, then restored over the
    /// final 0.6 s. Restores toward whatever SetIntensity/BeginPanic would want right now, not a value
    /// captured at the start, so a panic transition landing mid-hold is not undone by the restore.
    /// </summary>
    public void HoldBreath(float seconds)
    {
        if (_holdBreathRoutine != null) StopCoroutine(_holdBreathRoutine);
        _holdBreathRoutine = StartCoroutine(HoldBreathRoutine(Mathf.Max(0.1f, seconds)));
    }

    private IEnumerator HoldBreathRoutine(float seconds)
    {
        float restoreSeconds = Mathf.Min(0.6f, seconds);
        float silentSeconds = seconds - restoreSeconds;

        float t = 0f;
        while (t < silentSeconds)
        {
            // Disabling this component (GameOutcome.EndTheHunt) does not stop a running coroutine, only
            // its Update - so this loop has to check for itself, every frame, not just at the start.
            if (!enabled || GameOutcome.IsOver) yield break;

            _breathGain = 0f;
            t += Time.deltaTime;
            yield return null;
        }

        t = 0f;
        while (t < restoreSeconds)
        {
            if (!enabled || GameOutcome.IsOver) yield break;

            // A gain on top of whatever the bed wants right now (ApplyBedVolume), so a panic transition landing mid-hold is not undone.
            _breathGain = Mathf.Clamp01(t / restoreSeconds);
            t += Time.deltaTime;
            yield return null;
        }

        if (!enabled || GameOutcome.IsOver) yield break;

        _breathGain = 1f;
        _holdBreathRoutine = null;
    }

    /// <summary>
    /// A two-thump heartbeat built from scratch, because the project has no horror source material.
    /// </summary>
    private static AudioClip BuildHeartbeatClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.45f;
        const float dubDelay = 0.20f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        AddThump(samples, sampleRate, 0f, 55f, 0.045f, 1.0f);
        AddThump(samples, sampleRate, dubDelay, 48f, 0.035f, 0.8f);

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.8f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("Heartbeat", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>A descending sine sweep with a noise "whump" at the end - the sound of it landing behind you.</summary>
    private static AudioClip BuildRelocationCueClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.9f;
        const float freqStart = 140f;
        const float freqEnd = 38f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float sweepPerSecond = (freqEnd - freqStart) / duration;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            // Integrated frequency (a proper linear chirp), same technique as MazeEscape's ping.
            float phase = 2f * Mathf.PI * (freqStart * t + 0.5f * sweepPerSecond * t * t);
            // 1/t-shaped envelope: loud immediately, trails off rather than swelling in.
            float envelope = 1f / (1f + t * 6f);
            samples[i] = Mathf.Sin(phase) * envelope;
        }

        // 12 ms noise transient at the very end - the "whump" of something arriving.
        int transientSamples = Mathf.RoundToInt(0.012f * sampleRate);
        int transientStart = Mathf.Max(0, sampleCount - transientSamples);
        System.Random noise = new System.Random(4242);
        for (int i = transientStart; i < sampleCount; i++)
        {
            float t = (i - transientStart) / (float)transientSamples;
            samples[i] += (float)(noise.NextDouble() * 2.0 - 1.0) * 0.6f * (1f - t);
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.85f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("RelocationCue", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>Filtered white noise with a rise-fall envelope and a slow tremolo - reads as a breath, not a word.</summary>
    private static AudioClip BuildWhisperClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.7f;
        const float lowPass = 0.08f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random noise = new System.Random(9001);
        float y = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float x = (float)(noise.NextDouble() * 2.0 - 1.0);
            y += lowPass * (x - y);

            float envelope = Mathf.Sin(Mathf.PI * t / duration);
            envelope *= envelope;
            float tremolo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 2f * t);

            samples[i] = y * envelope * tremolo;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.9f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("Whisper", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static void AddThump(float[] samples, int sampleRate, float startSeconds, float frequency, float decay, float amplitude)
    {
        int start = Mathf.RoundToInt(startSeconds * sampleRate);
        System.Random noise = new System.Random(unchecked((int)(frequency * 1000f)));

        for (int i = start; i < samples.Length; i++)
        {
            float t = (i - start) / (float)sampleRate;
            float envelope = Mathf.Exp(-t / decay);
            if (envelope < 0.0005f) break;

            // 3 ms fade-in kills the click that a hard attack on a sine produces
            envelope *= Mathf.Clamp01(t / 0.003f);

            float value = Mathf.Sin(2f * Mathf.PI * frequency * t);
            // A second harmonic, and a brief noise transient over the first few milliseconds. Without
            // these the 55 Hz fundamental is simply inaudible on laptop speakers.
            value += 0.35f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * t);
            if (t < 0.008f)
            {
                value += 0.3f * (float)(noise.NextDouble() * 2.0 - 1.0) * (1f - t / 0.008f);
            }

            samples[i] += value * envelope * amplitude;
        }
    }
}
