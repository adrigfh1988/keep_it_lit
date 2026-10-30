using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Elias ("The Frost" / Subject #5): solo blackout predator for DESCENT level 5+.
///
/// Replaces the baseline Hunter (<see cref="AIFollower.IsDormantForSoloEntity"/>) when rolled.
/// Systematically harvests the maze's <see cref="WallLamp"/> grid across 4 blackout escalation stages,
/// projects a frost-cyan aura onto nearby lamps, drains the player's flashlight in proximity, recoils
/// from a sustained focused beam, tracks exhausted breathing, busts lockers when seen hiding, and
/// delivers a survivable Frost Shove on the first catch of a floor before executing a lethal capture.
/// </summary>
[RequireComponent(typeof(NavMeshAgent))]
public class Elias : MazeEntity
{
    private enum Mode
    {
        PatrolDark,
        HarvestLamp,
        InvestigateNoise,
        Search,
        Chase,
        BustLocker,
        ColdSurge,
        Stunned,
        Captured
    }

    private static readonly Color FrostTint = new Color(0.35f, 0.78f, 1.0f);

    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimMotionSpeed = Animator.StringToHash("MotionSpeed");
    private static readonly int AnimGrounded = Animator.StringToHash("Grounded");
    private static readonly int AnimPose = Animator.StringToHash("Pose");
    private static readonly int AnimReach = Animator.StringToHash("Reach");

    [Header("Animator & Locomotion (Animation/Descent/Elias.controller)")]
    [SerializeField] private string locomotionState = "Locomotion";
    [SerializeField] private string patrolState = "Patrol";
    [SerializeField] private string rushState = "Rush";
    [SerializeField] private string harvestState = "Harvest";
    [SerializeField] private string surgeState = "Surge";
    [SerializeField] private string recoilState = "Recoil";
    [SerializeField] private string shoveState = "Shove";
    [SerializeField] private string pinState = "Pin";
    [Tooltip("Ground speed (m/s) the walk and run clips cover at normal playback, measured from stance foot travel by InteractableKitBuilder.")]
    [SerializeField] private float locomotionWalkSpeed = 1.98f;
    [SerializeField] private float locomotionRunSpeed = 5.45f;
    [SerializeField] private float maxLocomotionPlayback = 2.0f;
    [SerializeField] private float maxWalkPlayback = 1.75f;
    [SerializeField] private float animationBlendRate = 6f;
    [SerializeField] private float acceleration = 7f;
    [SerializeField] private float speedRampRate = 14f;
    [SerializeField] private float turnSpeedChase = 520f;
    [SerializeField] private float turnSpeedMoving = 210f;
    [SerializeField] private float turnSpeedStationary = 150f;

    private const float EyeHeight = 1.68f;
    private const float MaxGroundOffset = 0.20f;
    private const float StateFadeSeconds = 0.16f;

    private static readonly string[] PatrolMutterLines =
    {
        "Drawer five... leave the door shut.",
        "Too warm in this corridor...",
        "I walk at night. I don't remember where.",
        "The drawers are cold. Mine is number five."
    };

    private static readonly string[] HarvestLampLines =
    {
        "Out. All of them out.",
        "The filaments burn my eyes...",
        "Better in the cold.",
        "No lights after lights-out."
    };

    private static readonly string[] BreathAndNoiseLines =
    {
        "I see your breath in the dark...",
        "Warm lungs... this way.",
        "Don't pant. It fogs the air.",
        "Who's shivering down here?"
    };

    private static readonly string[] SpottedShoutLines =
    {
        "PUT THAT TORCH OUT!",
        "BACK TO THE DRAWERS!",
        "THE LIGHT BURNS!",
        "WARM BLOOD IN THE HALL!"
    };

    private static readonly string[] ColdSurgeLines =
    {
        "FREEZE THE GRID!",
        "NO MORE WARMTH DOWN HERE!",
        "LIGHTS OUT IN COLD STORAGE!"
    };

    private static readonly string[] BeamRecoilLines =
    {
        "TURN IT OFF! TURN IT OFF!",
        "TOO BRIGHT—STOP!",
        "GET THAT BEAM OFF MY EYES!"
    };

    private static readonly string[] LostSightLines =
    {
        "You're still shivering somewhere...",
        "The cold will find you first.",
        "Hold your breath all you want..."
    };

    private static readonly string[] CaptureLines =
    {
        "DRAWER FIVE IS WAITING!",
        "HOLD STILL ON THE SLAB!",
        "COLD STORAGE FOR YOU!"
    };

    [Header("Blackout Stage Speeds & Time Thresholds")]
    [SerializeField] private float[] stageWalkSpeeds = { 3.0f, 3.35f, 3.75f, 4.15f };
    [SerializeField] private float[] stageChaseSpeeds = { 4.2f, 4.6f, 5.0f, 5.4f };
    [SerializeField] private float[] stageHarvestCooldowns = { 10f, 8f, 6f, 4.5f };
    [SerializeField] private float[] stageSurgeIntervals = { 30f, 24f, 18f, 13f };
    [SerializeField] private float[] stageTorchDrainMultipliers = { 1.40f, 1.70f, 2.05f, 2.45f };
    [Tooltip("Elapsed floor seconds at which Elias escalates to Stages 2, 3, and 4 even if no stars are collected.")]
    [SerializeField] private float stage2TimeSeconds = 55f;
    [SerializeField] private float stage3TimeSeconds = 110f;
    [SerializeField] private float stage4TimeSeconds = 165f;

    [Header("Frost Aura & Lamp Harvesting")]
    [SerializeField] private float frostAuraInnerRadius = 5f;
    [SerializeField] private float frostAuraOuterRadius = 14f;
    [SerializeField] private float lampKillFadeSeconds = 1.2f;
    [SerializeField] private float harvestReachDistance = 2.25f;

    [Header("Perception & Combat")]
    [SerializeField] private float viewAngle = 135f;
    [SerializeField] private float litSightRange = 20f;
    [SerializeField] private float lampSightRange = 13.5f;
    [SerializeField] private float darkSightRange = 9f;
    [SerializeField] private float closeSenseRange = 2.6f;
    [SerializeField] private float loseSightSeconds = 4.5f;
    [SerializeField] private float searchDurationSeconds = 7.5f;
    [SerializeField] private float captureRange = 1.45f;
    [SerializeField] private float frostDrainRadius = 10f;
    [SerializeField] private float focusStaggerHoldSeconds = 0.65f;
    [SerializeField] private float focusStaggerCooldownSeconds = 14f;
    [SerializeField] private float frostShoveStunSeconds = 3.2f;
    [SerializeField] private float frostLimpSeconds = 6.0f;
    [SerializeField] private float frostLimpSpeedScale = 0.62f;

    [Header("Audio Keys (GameAudioLibrary) & Chase Music")]
    [SerializeField] private string[] stageWaveSoundKeys = { "elias_stage_wave", "hunter_relocate_cue", "dread_lamp_pop" };
    [SerializeField] private string[] lampKillSoundKeys = { "elias_lamp_snuff", "dread_lamp_pop", "fuse_off" };
    [SerializeField] private string[] staggerSoundKeys = { "elias_stagger", "hunter_stun", "torch_click" };
    [SerializeField] private string[] shoveSoundKeys = { "elias_shove", "hunter_grab_shove", "hunter_catch" };
    [SerializeField] private string[] captureSoundKeys = { "elias_capture", "hunter_catch" };
    [Tooltip("Looping frantic frost-chase music track while Elias is chasing, surging, busting a locker, or capturing.")]
    [SerializeField] private AudioClip franticChaseClip;
    [SerializeField] private float franticChaseMaxVolume = 0.65f;
    [SerializeField] private float chaseMusicOuterDistance = 22f;
    [SerializeField] private float chaseMusicInnerDistance = 3.0f;
    [SerializeField] private float chaseMusicRiseRate = 0.42f;
    [SerializeField] private float chaseMusicFallRate = 0.32f;

    [Header("Voice Audio (Assets/SourceFiles/Audio/Generated/Voices/Elias)")]
    [SerializeField] private AudioClip[] patrolMutterClips;
    [SerializeField] private AudioClip[] harvestLampClips;
    [SerializeField] private AudioClip[] breathAndNoiseClips;
    [SerializeField] private AudioClip[] spottedShoutClips;
    [SerializeField] private AudioClip[] coldSurgeClips;
    [SerializeField] private AudioClip[] beamRecoilClips;
    [SerializeField] private AudioClip[] lostSightClips;
    [SerializeField] private AudioClip[] captureClips;

    /// <summary>Campaign-scoped flag saved in <see cref="SaveData.eliasSeen"/>.</summary>
    public static bool SeenThisCampaign { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        SeenThisCampaign = false;
    }

    public static void ResetCampaign() => SeenThisCampaign = false;

    public static void RestoreFromSave(bool seen) => SeenThisCampaign = seen;

    public static void CaptureTo(SaveData d)
    {
        if (d != null) d.eliasSeen = SeenThisCampaign;
    }

    public static void RestoreFrom(SaveData d)
    {
        SeenThisCampaign = d != null && d.eliasSeen;
    }

    public override EntityKind Kind => EntityKind.Elias;

    public override bool IsKiller => true;

    public override char DebugGlyph => 'f';

    /// <summary>1..4 blackout stage driven by objective progress and endgame hatch/gate opening.</summary>
    public int CurrentStage { get; private set; } = 1;

    public bool IsChasing => _mode == Mode.Chase || _mode == Mode.BustLocker;

    public bool IsSearching => _mode == Mode.Search || _mode == Mode.InvestigateNoise || _mode == Mode.ColdSurge;

    /// <summary>World-space head position for <see cref="MazeEntity.PinPlayer"/> and speech bubbles.</summary>
    public Vector3 HeadPosition => _head != null ? _head.position : transform.position + Vector3.up * EyeHeight;

    public override void Configure(MazeGenerator maze, Transform player, AIFollower hunter)
    {
        BindSystems(maze, player, hunter);
        Begin();
    }

    public override void WarpAway(float dormantSeconds)
    {
        if (Maze != null && Player != null && _agent != null && _agent.isOnNavMesh)
        {
            Vector3 far = Maze.FarthestCellCenterFrom(Player.position);
            if (NavMesh.SamplePosition(far, out NavMeshHit hit, 4f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
        }
        _mode = Mode.Stunned;
        _stunnedUntil = Time.time + Mathf.Max(2f, dormantSeconds);
        if (_agent != null && _agent.isOnNavMesh)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }
    }

    private NavMeshAgent _agent;
    private Animator _animator;
    private EliasPhysics _physics;
    private Transform _body;
    private Transform _head;
    private Renderer[] _renderers;
    private readonly EntityGaitAnimator _locomotion = new EntityGaitAnimator();
    private readonly EntityVoiceAndMusic _voiceMusic = new EntityVoiceAndMusic();
    private bool _hasPoseParam;
    private bool _hasReachParam;
    private PlayerStamina _stamina;

    private Mode _mode = Mode.PatrolDark;
    private readonly List<Vector3> _patrolPoints = new List<Vector3>();
    private int _patrolIndex = -1;

    private readonly HashSet<WallLamp> _tintedLamps = new HashSet<WallLamp>();
    private WallLamp _targetLamp;
    private IHidingSpot _targetHidingSpot;

    private Vector3 _lastKnownPlayerPos;
    private float _lastSeenPlayerTime = -999f;
    private float _lostSightTimer;
    private float _searchEndTime;
    private float _nextHarvestTime;
    private float _nextSurgeTime;
    private float _surgeEndTime;
    private float _stunnedUntil = -1f;
    private float _stallUntil = -1f;
    private float _harvestPoseUntil = -1f;
    private Vector3 _harvestLampPos;
    private float _shovePoseUntil = -1f;
    private float _beamRecoilUntil = -1f;
    private float _nextAuraTick;
    private float _nextBreathSenseTime;
    private float _nextFocusStaggerTime;
    private float _focusBeamExposure;
    private float _lastLampSubtitleTime = -999f;
    private float _nextPatrolMutterTime = -999f;
    private float _nextBreathNoiseLineTime = -999f;

    private bool _endgameHunting;
    private bool _runPrimed;
    private bool _shownFirstSightSubtitle;
    private bool _wasHidden;
    private bool _limpActive;
    private float _limpEndTime;

    private void Begin()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();
        _physics = GetComponentInChildren<EliasPhysics>();
        _stamina = Player != null ? Player.GetComponent<PlayerStamina>() : null;

        if (_animator != null)
        {
            _body = _animator.transform;
            _animator.fireEvents = false;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            if (_animator.isHuman) _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            if (_head == null) _head = EntityGaitAnimator.FindDeepChild(_body, "mixamorig:Head");

            EntityGaitAnimator.SyncCosmeticsFromHub(_body, _animator, null, "Body_EliasVogt");

            foreach (AnimatorControllerParameter param in _animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Int && param.name == "Pose") _hasPoseParam = true;
                else if (param.type == AnimatorControllerParameterType.Trigger && param.name == "Reach") _hasReachParam = true;
                else if (param.type == AnimatorControllerParameterType.Bool && param.name == "Grounded") _animator.SetBool(AnimGrounded, true);
            }
            _locomotion.Bind(_animator);
        }

        _renderers = GetComponentsInChildren<Renderer>(true);
        foreach (Renderer r in _renderers)
        {
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        _physics?.BindRig();
        BuildSpeechBubble();
        _voiceMusic.BuildAudioSources(transform, EyeHeight, 1.25f, franticChaseClip, "EliasVoiceAudio", "EliasChaseMusic", createVoiceSource: true);

        if (_agent != null)
        {
            _agent.acceleration = acceleration;
            _agent.updateRotation = false;
        }

        if (Maze != null && Maze.CellCenters != null)
        {
            for (int i = 0; i < Maze.CellCenters.Count; i++)
                _patrolPoints.Add(Maze.CellCenters[i]);
        }

        if (_agent != null && !_agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 12f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
        }

        CurrentStage = 1;
        _runPrimed = false;
        _nextHarvestTime = Time.time + 1.0f;
        _nextSurgeTime = Time.time + 18f;
        _nextPatrolMutterTime = Time.time + Random.Range(10f, 18f);

        GameManager.ProgressChanged += OnProgressChanged;
        GameManager.AllStarsCollected += OnAllStarsCollected;
        MazeNoise.Emitted += OnNoiseEmitted;

        if (_agent != null)
            DoorBreakers.Register(transform, _agent, silent: false, Stall);

        PickNextPatrolPoint();
    }

    private void OnEnable()
    {
        if (_agent != null)
            DoorBreakers.Register(transform, _agent, silent: false, Stall);
    }

    private void OnDisable()
    {
        DoorBreakers.Unregister(transform);
    }

    private void OnDestroy()
    {
        GameManager.ProgressChanged -= OnProgressChanged;
        GameManager.AllStarsCollected -= OnAllStarsCollected;
        MazeNoise.Emitted -= OnNoiseEmitted;
        DoorBreakers.Unregister(transform);
        ClearAllLampTints();
        EndFrostLimp();
        _voiceMusic.DestroyBubble();
    }

    public void Stall(float seconds)
    {
        _stallUntil = Mathf.Max(_stallUntil, Time.time + seconds);
    }

    private void Update()
    {
        UpdateFrostLimp();

        if (_mode == Mode.Captured && !GameOutcome.IsOver)
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
            if (Player != null) UpdateRotation(Player.position - transform.position);
            UpdateAnimator(0f);
            UpdateChaseMusic();
            return;
        }

        if (!CanAct)
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
            _locomotion.PauseToIdle(locomotionState, patrolState, rushState, recoilState);
            _physics?.SetPoseState(false, Vector3.zero, false, false, false, false);
            UpdateChaseMusic();
            return;
        }

        if (_mode == Mode.Captured)
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
            UpdateAnimator(0f);
            UpdateChaseMusic();
            return;
        }

        if (_agent == null || !_agent.isOnNavMesh)
        {
            if (_agent != null && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 12f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
            return;
        }

        if (!_runPrimed)
        {
            _runPrimed = true;
            _nextHarvestTime = Time.time + 0.5f;
            _nextSurgeTime = Time.time + 18f;
            WallLamp initialLamp = PickLampToHarvest();
            if (initialLamp != null)
            {
                _targetLamp = initialLamp;
                _mode = Mode.HarvestLamp;
                _agent.isStopped = false;
                _agent.SetDestination(_targetLamp.transform.position);
            }
            else
            {
                PickNextPatrolPoint();
            }
        }

        if (Time.time >= _nextAuraTick)
        {
            _nextAuraTick = Time.time + 0.15f;
            UpdateFrostAura();
        }

        if (_mode == Mode.Stunned)
        {
            if (Time.time < _stunnedUntil)
            {
                _agent.isStopped = true;
                if (Player != null) UpdateRotation(Player.position - transform.position);
                UpdateAnimator(0f);
                UpdateChaseMusic();
                return;
            }
            _mode = Mode.Search;
            _searchEndTime = Time.time + searchDurationSeconds;
            _agent.isStopped = false;
        }

        UpdateTimeEscalation();
        UpdateTorchInteraction();
        UpdatePerceptionAndBreath();
        UpdateStateMachine();

        if (Time.time < _stallUntil)
        {
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;
        }
        else if (!_agent.isStopped)
        {
            UpdateRotation(EntityGaitAnimator.CurrentHeading(_agent, transform));
        }

        UpdateAnimator(_agent.velocity.magnitude);
        UpdateChaseMusic();
    }

    private void UpdateChaseMusic()
    {
        if (!CanAct || _mode == Mode.Stunned || Player == null)
        {
            _voiceMusic.UpdateProximityChaseMusic(0f, franticChaseClip, franticChaseMaxVolume, chaseMusicRiseRate, chaseMusicFallRate, false);
            return;
        }

        float dist = FlatDistanceToPlayer(transform.position);
        float closeness01 = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(chaseMusicOuterDistance, chaseMusicInnerDistance, dist));
        float stageBoost = 0.03f * Mathf.Clamp(CurrentStage - 1, 0, 3);
        bool los = HasClearLine(transform.position + Vector3.up * EyeHeight, Player.position + Vector3.up * 1.45f, Player);

        float targetIntensity01;
        switch (_mode)
        {
            case Mode.Captured:
                targetIntensity01 = 1.0f;
                break;
            case Mode.Chase:
            case Mode.BustLocker:
                // Starts at 22% intensity at outer chase edge (22m) and builds gradually to 100% as Elias closes in (<= 3m)
                targetIntensity01 = Mathf.Clamp01(Mathf.Lerp(0.22f, 1.0f, closeness01) + stageBoost);
                break;
            case Mode.ColdSurge:
                targetIntensity01 = Mathf.Clamp01(Mathf.Lerp(0.16f, 0.85f, closeness01) + stageBoost);
                break;
            case Mode.Search:
            case Mode.InvestigateNoise:
                // Creeping muffled pulse when Elias is searching nearby (< 16m)
                targetIntensity01 = Mathf.SmoothStep(0f, 0.34f, Mathf.InverseLerp(16f, 3.5f, dist));
                break;
            default:
                // Faint sub-bass cryo-undertone when Elias silently patrols within 12m
                targetIntensity01 = Mathf.SmoothStep(0f, 0.18f, Mathf.InverseLerp(12f, 3.5f, dist));
                break;
        }

        _voiceMusic.UpdateProximityChaseMusic(
            targetIntensity01,
            franticChaseClip,
            franticChaseMaxVolume,
            chaseMusicRiseRate,
            chaseMusicFallRate,
            los);
    }

    private void LateUpdate()
    {
        if (_body == null) return;
        bool lookAtPlayer = _mode == Mode.Chase || _mode == Mode.BustLocker || _mode == Mode.Stunned || _mode == Mode.Captured
                            || (_mode == Mode.Search && FlatDistanceToPlayer(transform.position) <= 8f);
        bool fastTrack = _mode == Mode.Chase || _mode == Mode.BustLocker || _mode == Mode.Captured;
        _locomotion.UpdateBodyGroundAndHeadTrack(transform, _body, _head, Maze, Player, lookAtPlayer, fastTrack, MaxGroundOffset);
    }

    // ---------------------------------------------------------------- Blackout Escalation (Stages 1..4)

    private void UpdateTimeEscalation()
    {
        if (_endgameHunting || !GameFlow.IsRunActive) return;
        float elapsed = Mathf.Max(0f, Time.time - GameFlow.RunStartTime);
        int timeStage = 1;
        if (elapsed >= stage4TimeSeconds) timeStage = 4;
        else if (elapsed >= stage3TimeSeconds) timeStage = 3;
        else if (elapsed >= stage2TimeSeconds) timeStage = 2;

        if (timeStage > CurrentStage) SetStage(timeStage);
    }

    private void OnProgressChanged(int collected, int total)
    {
        if (_endgameHunting) return;
        float progress = total > 0 ? (float)collected / total : 0f;
        int stage = 1;
        if (progress >= 0.80f) stage = 4;
        else if (progress >= 0.50f) stage = 3;
        else if (progress >= 0.25f) stage = 2;

        SetStage(stage);
    }

    private void OnAllStarsCollected()
    {
        _endgameHunting = true;
        SetStage(4);
    }

    private void SetStage(int newStage)
    {
        newStage = Mathf.Clamp(newStage, 1, 4);
        if (newStage <= CurrentStage) return;

        CurrentStage = newStage;
        LogEvent($"Stage {CurrentStage}");

        float interval = GetSurgeInterval(CurrentStage);
        if (interval > 0f)
            _nextSurgeTime = Mathf.Min(_nextSurgeTime, Time.time + interval * 0.5f);

        _nextHarvestTime = Mathf.Min(_nextHarvestTime, Time.time + 2.5f);

        BlinkNearbyLamps(22f, FlashSafety.On ? 0.8f : 0.45f);
        PlayFirst(transform.position, 0.9f, 0.85f, GameAudio.Library.powerDown, GameAudio.Library.bulbPop);
        PulseDread();

        if (Hud != null)
        {
            string key = CurrentStage == 2 ? "sub.elias.stage2"
                       : CurrentStage == 3 ? "sub.elias.stage3"
                       : "sub.elias.stage4";
            Hud.ShowSubtitle(L10n.T(key), 3.2f);
        }
    }

    private float GetWalkSpeed(int stage)
    {
        int i = Mathf.Clamp(stage - 1, 0, stageWalkSpeeds.Length - 1);
        float s = stageWalkSpeeds[i];
        return Controller != null ? Mathf.Min(s, Controller.SprintSpeed * 0.98f) : s;
    }

    private float GetChaseSpeed(int stage)
    {
        int i = Mathf.Clamp(stage - 1, 0, stageChaseSpeeds.Length - 1);
        float s = stageChaseSpeeds[i];
        return Controller != null ? Mathf.Min(s, Controller.SprintSpeed * 0.99f) : s;
    }

    private float GetHarvestCooldown(int stage)
    {
        int i = Mathf.Clamp(stage - 1, 0, stageHarvestCooldowns.Length - 1);
        return stageHarvestCooldowns[i];
    }

    private float GetSurgeInterval(int stage)
    {
        int i = Mathf.Clamp(stage - 1, 0, stageSurgeIntervals.Length - 1);
        return stageSurgeIntervals[i];
    }

    private float GetTorchDrainMultiplier(int stage)
    {
        int i = Mathf.Clamp(stage - 1, 0, stageTorchDrainMultipliers.Length - 1);
        return stageTorchDrainMultipliers[i];
    }

    // ---------------------------------------------------------------- Live Frost Aura & Lamp Harvesting

    private void UpdateFrostAura()
    {
        if (Maze == null || Maze.Lamps == null) return;

        IReadOnlyList<WallLamp> lamps = Maze.Lamps;
        Vector3 pos = transform.position;

        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp == null) continue;

            if (lamp.IsDead || !lamp.IsPowered)
            {
                if (_tintedLamps.Remove(lamp))
                    lamp.ClearTint();
                continue;
            }

            float d = FlatDistance(pos, lamp.transform.position);
            if (d <= frostAuraOuterRadius)
            {
                float u = 1f - Mathf.Clamp01((d - frostAuraInnerRadius) / Mathf.Max(0.01f, frostAuraOuterRadius - frostAuraInnerRadius));
                lamp.SetTint(Color.Lerp(Color.white, FrostTint, u * 0.85f));
                _tintedLamps.Add(lamp);
            }
            else if (_tintedLamps.Remove(lamp))
            {
                lamp.ClearTint();
            }
        }
    }

    private void ClearAllLampTints()
    {
        foreach (WallLamp lamp in _tintedLamps)
        {
            if (lamp != null) lamp.ClearTint();
        }
        _tintedLamps.Clear();
    }

    private WallLamp PickLampToHarvest()
    {
        if (Maze == null || Maze.Lamps == null) return null;
        IReadOnlyList<WallLamp> lamps = Maze.Lamps;
        bool allowLocker = CurrentStage >= 3;

        WallLamp bestMidBand = null;
        float bestMidScore = float.MaxValue;
        WallLamp bestFallback = null;
        float bestFallbackDist = float.MaxValue;

        Vector3 myPos = transform.position;
        Vector3 playerPos = Player != null ? Player.position : myPos;

        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp == null || lamp.IsDead || !lamp.IsPowered) continue;
            if (lamp.IsLockerLamp && !allowLocker) continue;

            float dElias = FlatDistance(myPos, lamp.transform.position);
            float dPlayer = FlatDistance(playerPos, lamp.transform.position);

            if (dPlayer <= 38f && dElias < bestMidScore)
            {
                bestMidScore = dElias;
                bestMidBand = lamp;
            }

            if (dElias < bestFallbackDist)
            {
                bestFallbackDist = dElias;
                bestFallback = lamp;
            }
        }

        return bestMidBand != null ? bestMidBand : bestFallback;
    }

    private void BlinkNearbyLamps(float radius, float seconds)
    {
        if (Maze == null || Maze.Lamps == null) return;
        IReadOnlyList<WallLamp> lamps = Maze.Lamps;
        Vector3 pos = transform.position;
        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp == null || lamp.IsDead || !lamp.IsPowered) continue;
            if (FlatDistance(pos, lamp.transform.position) <= radius)
                lamp.Blink(seconds);
        }
    }

    // ---------------------------------------------------------------- Torch Frost Drain & Focused-Beam Stagger

    private void UpdateTorchInteraction()
    {
        if (Torch == null || !Torch.IsBeamLit)
        {
            _focusBeamExposure = Mathf.Max(0f, _focusBeamExposure - Time.deltaTime * 1.5f);
            return;
        }

        float distToPlayer = FlatDistanceToPlayer(transform.position);
        if (distToPlayer <= frostDrainRadius)
        {
            float extraMult = GetTorchDrainMultiplier(CurrentStage) - 1f;
            if (extraMult > 0f)
                Torch.Drain(extraMult * (1f / 75f) * Time.deltaTime);
        }

        Vector3 chest = transform.position + Vector3.up * 1.15f;
        bool focusedOnElias = Torch.IsFocused
            && Time.time >= _nextFocusStaggerTime
            && distToPlayer <= 9.5f
            && IsInViewCone(chest, 22f, 9.5f)
            && ViewCamera != null
            && HasClearLine(ViewCamera.position, chest, transform);

        if (focusedOnElias)
        {
            _focusBeamExposure += Time.deltaTime;
            if (_focusBeamExposure >= focusStaggerHoldSeconds)
            {
                _focusBeamExposure = 0f;
                _nextFocusStaggerTime = Time.time + focusStaggerCooldownSeconds;
                _beamRecoilUntil = Time.time + 1.35f;
                Stall(1.35f);
                PlayFirst(transform.position, 0.9f, 0.92f, GameAudio.Library.flickerCrackle, GameAudio.Library.bulbPop);
                Speak(BeamRecoilLines, SpeechBubble.Style.Shout);
                Hud?.ShowSubtitle(L10n.T("sub.elias.beam_recoil"), 2.2f);
                LogEvent("Focused-beam stagger");
            }
        }
        else
        {
            _focusBeamExposure = Mathf.Max(0f, _focusBeamExposure - Time.deltaTime * 1.5f);
        }
    }

    // ---------------------------------------------------------------- Perception, Hearing & Breath Sensing

    private void OnNoiseEmitted(Vector3 at, float radius)
    {
        if (!CanAct || _mode == Mode.Captured || _mode == Mode.Stunned) return;
        if (FlatDistance(at, transform.position) < 1.0f) return;

        float effectiveRadius = radius * (1f + 0.12f * (CurrentStage - 1));
        if (FlatDistance(at, transform.position) > effectiveRadius) return;

        _lastKnownPlayerPos = at;
        if (_mode != Mode.Chase && _mode != Mode.BustLocker)
        {
            _mode = Mode.InvestigateNoise;
            _agent.isStopped = false;
            _agent.SetDestination(_lastKnownPlayerPos);
            if (Time.time >= _nextBreathNoiseLineTime)
            {
                _nextBreathNoiseLineTime = Time.time + 6.0f;
                Speak(BreathAndNoiseLines, SpeechBubble.Style.Normal);
            }
        }
    }

    private void UpdatePerceptionAndBreath()
    {
        if (Player == null) return;

        // First-seen check in player's view cone
        if (!SeenThisCampaign || !_shownFirstSightSubtitle)
        {
            Vector3 chest = transform.position + Vector3.up * 1.2f;
            if (IsInViewCone(chest, 55f, 16f) && ViewCamera != null && HasClearLine(ViewCamera.position, chest, transform))
            {
                NoteFirstSeen();
            }
        }

        // Check if player just hid in a locker while Elias had direct line of sight
        bool hiddenNow = Stealth != null && Stealth.Hidden && Stealth.CurrentHidingSpot != null;
        if (hiddenNow)
        {
            if (!_wasHidden && _mode != Mode.BustLocker)
            {
                Vector3 eye = transform.position + Vector3.up * 1.6f;
                Vector3 lockerFront = Stealth.CurrentHidingSpot.FrontPosition + Vector3.up * 1.2f;
                bool sawHide = _mode == Mode.Chase
                    && (Time.time - _lastSeenPlayerTime) <= 0.15f
                    && HasClearLine(eye, lockerFront, transform);
                if (sawHide)
                {
                    _targetHidingSpot = Stealth.CurrentHidingSpot;
                    _mode = Mode.BustLocker;
                    _agent.isStopped = false;
                    _agent.SetDestination(_targetHidingSpot.FrontPosition);
                    Speak(CaptureLines, SpeechBubble.Style.Shout);
                    LogEvent("Busting locker");
                }
            }
            _wasHidden = true;
            return;
        }
        _wasHidden = false;

        bool canSee = CanSeePlayer();
        if (canSee)
        {
            _lastSeenPlayerTime = Time.time;
            _lastKnownPlayerPos = Player.position;
            _lostSightTimer = loseSightSeconds;
            NoteFirstSeen();

            if (_mode != Mode.Chase)
            {
                _mode = Mode.Chase;
                _agent.isStopped = false;
                PulseDread();
                Speak(SpottedShoutLines, SpeechBubble.Style.Shout);
                LogEvent("Chase started");
            }
            return;
        }

        // Exhausted cold-breath plume sensing
        if (_stamina != null && _stamina.Fraction < 0.35f
            && FlatDistanceToPlayer(transform.position) <= 16f
            && Time.time >= _nextBreathSenseTime)
        {
            _nextBreathSenseTime = Time.time + 6.0f;
            _lastKnownPlayerPos = Player.position;
            if (_mode != Mode.Chase && _mode != Mode.BustLocker)
            {
                _mode = Mode.InvestigateNoise;
                _agent.isStopped = false;
                _agent.SetDestination(_lastKnownPlayerPos);
                _nextBreathNoiseLineTime = Time.time + 6.0f;
                Speak(BreathAndNoiseLines, SpeechBubble.Style.Normal);
                Hud?.ShowSubtitle(L10n.T("sub.elias.hears_breath"), 2.5f);
            }
        }
    }

    private bool CanSeePlayer(bool ignoreHidden = false)
    {
        if (Player == null || (!ignoreHidden && Stealth != null && Stealth.Hidden)) return false;

        float dist = FlatDistanceToPlayer(transform.position);
        float maxRange = darkSightRange;
        if (Torch != null && Torch.IsBeamLit)
            maxRange = litSightRange;
        else if (Stealth != null && Stealth.LampExposure > 0.1f)
            maxRange = lampSightRange;
        else if (Stealth != null && Stealth.VisibilityMultiplier < 0.4f)
            maxRange = darkSightRange * 0.62f;

        if (dist > maxRange) return false;

        Vector3 eye = transform.position + Vector3.up * 1.6f;
        Vector3 targetChest = Player.position + Vector3.up * 1.2f;

        if (dist > closeSenseRange)
        {
            Vector3 flatToPlayer = targetChest - eye;
            flatToPlayer.y = 0f;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (flatToPlayer.sqrMagnitude > 0.001f && Vector3.Angle(forward, flatToPlayer) > viewAngle * 0.5f)
                return false;
        }

        return HasClearLine(eye, targetChest, transform);
    }

    private void NoteFirstSeen()
    {
        if (!SeenThisCampaign)
        {
            SeenThisCampaign = true;
            PatientRecord.NoteMet(CodexEntry.Elias);
        }
        if (!_shownFirstSightSubtitle)
        {
            _shownFirstSightSubtitle = true;
            Hud?.ShowSubtitle(L10n.T("sub.elias.first_sight"), 3.2f);
            PulseDread();
        }
    }

    // ---------------------------------------------------------------- State Machine

    private void UpdateStateMachine()
    {
        // Check periodic Cold Surge in Stage 2+ when not in active combat
        float surgeInterval = GetSurgeInterval(CurrentStage);
        if (surgeInterval > 0f
            && Time.time >= _nextSurgeTime
            && (_mode == Mode.PatrolDark || _mode == Mode.HarvestLamp || _mode == Mode.Search))
        {
            BeginColdSurge(surgeInterval);
            return;
        }

        switch (_mode)
        {
            case Mode.ColdSurge:
                _agent.isStopped = true;
                if (Time.time >= _surgeEndTime)
                {
                    _agent.isStopped = false;
                    _mode = Mode.PatrolDark;
                    PickNextPatrolPoint();
                }
                break;

            case Mode.BustLocker:
                UpdateBustLocker();
                break;

            case Mode.Chase:
                UpdateChase();
                break;

            case Mode.InvestigateNoise:
                _agent.speed = GetWalkSpeed(CurrentStage) * 1.12f;
                _agent.isStopped = false;
                if (!_agent.pathPending && _agent.remainingDistance <= 1.25f)
                {
                    _mode = Mode.Search;
                    _searchEndTime = Time.time + searchDurationSeconds;
                    PickSearchPointAround(_lastKnownPlayerPos);
                }
                break;

            case Mode.Search:
                _agent.speed = GetWalkSpeed(CurrentStage);
                _agent.isStopped = false;
                if (Time.time >= _searchEndTime)
                {
                    _mode = Mode.PatrolDark;
                    PickNextPatrolPoint();
                }
                else if (!_agent.pathPending && _agent.remainingDistance <= 1.1f)
                {
                    PickSearchPointAround(_lastKnownPlayerPos);
                }
                break;

            case Mode.HarvestLamp:
                UpdateHarvestLamp();
                break;

            case Mode.PatrolDark:
            default:
                UpdatePatrolDark();
                break;
        }
    }

    private void UpdatePatrolDark()
    {
        _agent.speed = GetWalkSpeed(CurrentStage);
        _agent.isStopped = false;

        if (Time.time >= _nextPatrolMutterTime)
        {
            _nextPatrolMutterTime = Time.time + Random.Range(14f, 24f);
            Speak(PatrolMutterLines, SpeechBubble.Style.Normal);
        }

        if (Time.time >= _nextHarvestTime)
        {
            WallLamp candidate = PickLampToHarvest();
            _nextHarvestTime = Time.time + GetHarvestCooldown(CurrentStage);
            if (candidate != null)
            {
                _targetLamp = candidate;
                _mode = Mode.HarvestLamp;
                _agent.SetDestination(_targetLamp.transform.position);
                return;
            }
        }
        else if (Time.time >= _nextHarvestTime - 4f && Maze != null && Maze.Lamps != null)
        {
            // Opportunistically harvest any lit lamp Elias walks right past
            IReadOnlyList<WallLamp> lamps = Maze.Lamps;
            bool allowLocker = CurrentStage >= 3;
            Vector3 myPos = transform.position;
            for (int i = 0; i < lamps.Count; i++)
            {
                WallLamp lamp = lamps[i];
                if (lamp == null || lamp.IsDead || !lamp.IsPowered) continue;
                if (lamp.IsLockerLamp && !allowLocker) continue;
                if (FlatDistance(myPos, lamp.transform.position) <= harvestReachDistance + 0.5f)
                {
                    _targetLamp = lamp;
                    _nextHarvestTime = Time.time + GetHarvestCooldown(CurrentStage);
                    _mode = Mode.HarvestLamp;
                    UpdateHarvestLamp();
                    return;
                }
            }
        }

        if (!_agent.hasPath || _agent.pathStatus == NavMeshPathStatus.PathInvalid || (!_agent.pathPending && _agent.remainingDistance <= 1.1f))
        {
            PickNextPatrolPoint();
        }
    }

    private void UpdateHarvestLamp()
    {
        if (_targetLamp == null || _targetLamp.IsDead || !_targetLamp.IsPowered)
        {
            _targetLamp = null;
            _mode = Mode.PatrolDark;
            PickNextPatrolPoint();
            return;
        }

        _agent.speed = GetWalkSpeed(CurrentStage);
        _agent.isStopped = false;

        float dist = FlatDistance(transform.position, _targetLamp.transform.position);
        bool arrived = dist <= harvestReachDistance || (!_agent.pathPending && _agent.remainingDistance <= 1.2f && dist <= 3.2f);
        if (!arrived) return;

        Vector3 look = _targetLamp.transform.position - transform.position;
        look.y = 0f;
        if (look.sqrMagnitude > 0.001f)
            transform.rotation = Quaternion.LookRotation(look.normalized);

        if (_animator != null && _hasReachParam)
            _animator.SetTrigger(AnimReach);

        _harvestLampPos = _targetLamp.transform.position;
        _harvestPoseUntil = Time.time + 0.90f;
        _targetLamp.Kill(lampKillFadeSeconds, allowLockerLamp: CurrentStage >= 3);
        PlayFirst(transform.position, 0.95f, 0.88f, GameAudio.Library.bulbPop, GameAudio.Library.powerDown);
        Speak(HarvestLampLines, SpeechBubble.Style.Normal);
        Stall(0.90f);

        if (FlatDistanceToPlayer(transform.position) <= 22f && Time.time - _lastLampSubtitleTime >= 10f)
        {
            _lastLampSubtitleTime = Time.time;
            Hud?.ShowSubtitle(L10n.T("sub.elias.lamp_snuffed"), 2.4f);
        }

        _targetLamp = null;
        _mode = Mode.PatrolDark;
        PickNextPatrolPoint();
    }

    private void BeginColdSurge(float interval)
    {
        _mode = Mode.ColdSurge;
        _surgeEndTime = Time.time + 1.1f;
        _nextSurgeTime = Time.time + interval;
        _agent.isStopped = true;

        int killQuota = Mathf.Max(1, CurrentStage);
        int killed = 0;
        bool allowLocker = CurrentStage >= 3;

        if (Maze != null && Maze.Lamps != null)
        {
            IReadOnlyList<WallLamp> lamps = Maze.Lamps;
            for (int i = 0; i < lamps.Count; i++)
            {
                WallLamp lamp = lamps[i];
                if (lamp == null || lamp.IsDead || !lamp.IsPowered) continue;
                float d = FlatDistance(transform.position, lamp.transform.position);
                if (d <= 16f && killed < killQuota && (!lamp.IsLockerLamp || allowLocker))
                {
                    lamp.Kill(lampKillFadeSeconds, allowLockerLamp: allowLocker);
                    killed++;
                }
                else if (d <= 24f)
                {
                    lamp.Blink(FlashSafety.On ? 0.8f : 0.45f);
                }
            }
        }

        if (Torch != null && Torch.IsBeamLit && FlatDistanceToPlayer(transform.position) <= 14f)
        {
            if (CurrentStage >= 3) Torch.Blackout(1.5f);
            else Torch.Stutter(0.45f);
        }

        PlayFirst(transform.position, 0.9f, 0.82f, GameAudio.Library.powerDown, GameAudio.Library.bulbPop);
        Speak(ColdSurgeLines, SpeechBubble.Style.Shout);
        LogEvent($"ColdSurge (killed {killed} lamps)");
    }

    private void UpdateChase()
    {
        _agent.speed = GetChaseSpeed(CurrentStage);
        _agent.isStopped = false;

        if (Player != null && (Time.time - _lastSeenPlayerTime) <= 0.25f)
        {
            _lastKnownPlayerPos = Player.position;
        }

        _agent.SetDestination(_lastKnownPlayerPos);

        // Check capture
        if (Player != null && !(Stealth != null && Stealth.Hidden))
        {
            float dist = FlatDistanceToPlayer(transform.position);
            if (dist <= captureRange && HasClearLine(transform.position + Vector3.up * 1.5f, Player.position + Vector3.up * 1.1f, transform))
            {
                HandlePlayerCaught(wasLockerBust: false);
                return;
            }
        }

        // Lost sight countdown
        if (Time.time - _lastSeenPlayerTime > 0.2f)
        {
            _lostSightTimer -= Time.deltaTime;
            if (_lostSightTimer <= 0f)
            {
                _mode = Mode.Search;
                _searchEndTime = Time.time + searchDurationSeconds;
                Speak(LostSightLines, SpeechBubble.Style.Normal);
                PickSearchPointAround(_lastKnownPlayerPos);
            }
        }
    }

    private void UpdateBustLocker()
    {
        if (_targetHidingSpot == null || Stealth == null || !Stealth.Hidden || Stealth.CurrentHidingSpot != _targetHidingSpot)
        {
            _targetHidingSpot = null;
            _mode = Mode.Chase;
            _lostSightTimer = loseSightSeconds;
            return;
        }

        _agent.speed = GetChaseSpeed(CurrentStage);
        _agent.isStopped = false;
        Vector3 front = _targetHidingSpot.FrontPosition;
        _agent.SetDestination(front);

        if (FlatDistance(transform.position, front) <= 1.45f)
        {
            IHidingSpot busted = _targetHidingSpot;
            _targetHidingSpot = null;
            busted.Expose();
            HandlePlayerCaught(wasLockerBust: true);
        }
    }

    private void HandlePlayerCaught(bool wasLockerBust)
    {
        // Survivable first-catch Frost Shove / slow-limp removed: catches go directly to Second Wind or lethal capture.
        _mode = Mode.Captured;
        _agent.isStopped = true;
        _agent.velocity = Vector3.zero;
        StartCoroutine(PinPlayer(transform.position + Vector3.up * 1.55f, 0.65f));
        StartCoroutine(ShakeView(0.55f, 0.45f));
        PlayFirst(transform.position, 1.0f, 0.85f, GameAudio.Library.caught, GameAudio.Library.caughtLayer);
        Speak(CaptureLines, SpeechBubble.Style.Shout);
        LogEvent(wasLockerBust ? "Lethal locker-bust capture" : "Lethal capture");
        Outcome?.CaughtByEntity(transform.position + Vector3.up * 1.55f, () => WarpAway(6f), EntityKind.Elias);
    }

    // ---------------------------------------------------------------- Frost Limp Helper

    private void BeginFrostLimp(float seconds)
    {
        if (!_limpActive)
        {
            _limpActive = true;
            if (Controller != null) Controller.SpeedMultiplier *= frostLimpSpeedScale;
            if (_stamina != null) _stamina.LimpBlocked = true;
        }
        _limpEndTime = Time.time + seconds;
    }

    private void UpdateFrostLimp()
    {
        if (_limpActive && Time.time >= _limpEndTime)
        {
            EndFrostLimp();
        }
    }

    private void EndFrostLimp()
    {
        if (!_limpActive) return;
        _limpActive = false;
        if (Controller != null && frostLimpSpeedScale > 0.001f)
            Controller.SpeedMultiplier /= frostLimpSpeedScale;
        if (_stamina != null)
            _stamina.LimpBlocked = false;
    }

    // ---------------------------------------------------------------- Navigation & Animation Helpers

    private void PickNextPatrolPoint()
    {
        if (_agent == null || !_agent.isOnNavMesh || _patrolPoints.Count == 0) return;

        // During endgame hatch/gate phase, patrol close to the player to keep blackout pressure high
        if (_endgameHunting && Player != null)
        {
            PickSearchPointAround(Player.position, 18f);
            return;
        }

        // Bias patrol points toward the player's general sector even from the start of the maze
        if (Player != null && Random.value < 0.65f)
        {
            PickSearchPointAround(Player.position, 24f);
            return;
        }

        _patrolIndex = (_patrolIndex + 1 + Random.Range(0, Mathf.Max(1, _patrolPoints.Count / 3))) % _patrolPoints.Count;
        _agent.SetDestination(_patrolPoints[_patrolIndex]);
    }

    private void PickSearchPointAround(Vector3 center, float radius = 10f)
    {
        if (_agent == null || !_agent.isOnNavMesh) return;
        for (int attempt = 0; attempt < 6; attempt++)
        {
            Vector2 offset = Random.insideUnitCircle * radius;
            Vector3 candidate = center + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3.5f, NavMesh.AllAreas))
            {
                _agent.SetDestination(hit.position);
                return;
            }
        }
        PickNextPatrolPoint();
    }

    private void UpdateRotation(Vector3 desiredDirection)
    {
        bool chasing = _mode == Mode.Chase || _mode == Mode.BustLocker;
        _locomotion.UpdateRotation(transform, _agent, desiredDirection, chasing, turnSpeedChase, turnSpeedMoving, turnSpeedStationary);
    }

    private void UpdateAnimator(float speed)
    {
        bool harvestingNow = Time.time < _harvestPoseUntil;
        Vector3 harvestPos = _harvestLampPos;
        bool recoilingNow = Time.time < _beamRecoilUntil
            || (_mode == Mode.Stunned && Time.time >= _shovePoseUntil);
        bool shovingNow = Time.time < _shovePoseUntil;
        bool surgingNow = _mode == Mode.ColdSurge || _mode == Mode.Captured;

        _physics?.SetPoseState(harvestingNow, harvestPos, harvestingNow, recoilingNow, surgingNow, shovingNow);

        if (_animator == null) return;

        bool chasing = _mode == Mode.Chase || _mode == Mode.BustLocker;
        if (_locomotion.StepPursuerGait(
            _agent,
            chasing,
            locomotionWalkSpeed,
            locomotionRunSpeed,
            maxWalkPlayback,
            maxLocomotionPlayback,
            GetChaseSpeed(CurrentStage),
            animationBlendRate,
            out bool moving,
            out float v,
            out float walkSpeed,
            out float runSpeed,
            out float playback))
        {
            if (_mode == Mode.Captured)
            {
                _locomotion.PlayActionState(pinState, 1.25f, patrolState, rushState, recoilState, surgeState, pinState, harvestState);
            }
            else if (shovingNow)
            {
                _locomotion.PlayActionState(shoveState, 1.15f, patrolState, rushState, recoilState, surgeState, pinState, shoveState);
            }
            else if (recoilingNow)
            {
                _locomotion.PlayActionState(recoilState, 1.0f, patrolState, rushState, recoilState, surgeState, pinState, harvestState);
            }
            else if (_mode == Mode.ColdSurge)
            {
                _locomotion.PlayActionState(surgeState, 1.0f, patrolState, rushState, recoilState, surgeState, pinState, harvestState);
            }
            else if (harvestingNow)
            {
                _locomotion.PlayActionState(harvestState, 1.15f, patrolState, rushState, recoilState, surgeState, pinState, harvestState);
            }
            else if (chasing)
            {
                _locomotion.PlayChargeLocomotion(v, runSpeed, playback, maxLocomotionPlayback, locomotionState, patrolState, rushState, recoilState);
            }
            else
            {
                _locomotion.PlayPatrolLocomotion(moving, v, walkSpeed, playback, maxLocomotionPlayback, locomotionState, patrolState, recoilState, rushState);
            }
        }
        else
        {
            float gait = speed < 0.15f ? 0f : (chasing ? 6f : 2f);
            _animator.SetFloat(AnimSpeed, gait);
            _animator.SetFloat(AnimMotionSpeed, 1f);
        }

        if (_hasPoseParam)
        {
            int pose = _mode == Mode.Search || _mode == Mode.ColdSurge ? 1 : 0;
            _animator.SetInteger(AnimPose, pose);
        }
    }

    private void BuildSpeechBubble()
    {
        SpeechBubble.Palette frostPalette = new SpeechBubble.Palette
        {
            Fill = new Color(0.05f, 0.09f, 0.13f, 0.92f),
            Outline = new Color(0.35f, 0.78f, 1.0f, 1.0f),
            Text = new Color(0.86f, 0.94f, 1.0f, 1.0f),
            Shout = new Color(0.45f, 0.88f, 1.0f, 1.0f)
        };
        _voiceMusic.BuildSpeechBubble(Hud, frostPalette, "EliasSpeechBubble");
    }

    private AudioClip[] ResolveVoiceClips(string[] pool)
    {
        if (ReferenceEquals(pool, PatrolMutterLines)) return patrolMutterClips;
        if (ReferenceEquals(pool, HarvestLampLines)) return harvestLampClips;
        if (ReferenceEquals(pool, BreathAndNoiseLines)) return breathAndNoiseClips;
        if (ReferenceEquals(pool, SpottedShoutLines)) return spottedShoutClips;
        if (ReferenceEquals(pool, ColdSurgeLines)) return coldSurgeClips;
        if (ReferenceEquals(pool, BeamRecoilLines)) return beamRecoilClips;
        if (ReferenceEquals(pool, LostSightLines)) return lostSightClips;
        if (ReferenceEquals(pool, CaptureLines)) return captureClips;
        return null;
    }

    private void Speak(string[] pool, SpeechBubble.Style style)
    {
        _voiceMusic.Speak(
            FlatDistanceToPlayer(transform.position),
            pool,
            ResolveVoiceClips(pool),
            style,
            () => HeadPosition + Vector3.up * 0.40f,
            shoutMaxRange: 24f,
            normalMaxRange: 18f);
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
