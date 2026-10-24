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

    [Header("Blackout Stage Speeds & Time Thresholds")]
    [SerializeField] private float[] stageWalkSpeeds = { 2.6f, 3.1f, 3.6f, 4.1f };
    [SerializeField] private float[] stageChaseSpeeds = { 3.8f, 4.4f, 4.9f, 5.35f };
    [SerializeField] private float[] stageHarvestCooldowns = { 28f, 18f, 11f, 6f };
    [SerializeField] private float[] stageSurgeIntervals = { 0f, 45f, 28f, 16f };
    [SerializeField] private float[] stageTorchDrainMultipliers = { 1.0f, 1.45f, 1.85f, 2.35f };
    [Tooltip("Elapsed floor seconds at which Elias escalates to Stages 2, 3, and 4 even if no stars are collected.")]
    [SerializeField] private float stage2TimeSeconds = 70f;
    [SerializeField] private float stage3TimeSeconds = 140f;
    [SerializeField] private float stage4TimeSeconds = 210f;

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

    [Header("Audio Keys (GameAudioLibrary)")]
    [SerializeField] private string[] stageWaveSoundKeys = { "elias_stage_wave", "hunter_relocate_cue", "dread_lamp_pop" };
    [SerializeField] private string[] lampKillSoundKeys = { "elias_lamp_snuff", "dread_lamp_pop", "fuse_off" };
    [SerializeField] private string[] staggerSoundKeys = { "elias_stagger", "hunter_stun", "torch_click" };
    [SerializeField] private string[] shoveSoundKeys = { "elias_shove", "hunter_grab_shove", "hunter_catch" };
    [SerializeField] private string[] captureSoundKeys = { "elias_capture", "hunter_catch" };

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
    private float _nextAuraTick;
    private float _nextBreathSenseTime;
    private float _nextFocusStaggerTime;
    private float _focusBeamExposure;
    private float _lastLampSubtitleTime = -999f;

    private bool _endgameHunting;
    private bool _shownFirstSightSubtitle;
    private bool _usedFrostShoveThisFloor;
    private bool _limpActive;
    private float _limpEndTime;

    private void Begin()
    {
        _agent = GetComponent<NavMeshAgent>();
        _animator = GetComponentInChildren<Animator>();
        _stamina = Player != null ? Player.GetComponent<PlayerStamina>() : null;

        if (_animator != null)
        {
            _animator.SetBool(AnimGrounded, true);
            _animator.SetFloat(AnimMotionSpeed, 1f);
            _animator.fireEvents = false;
            foreach (AnimatorControllerParameter param in _animator.parameters)
            {
                if (param.type == AnimatorControllerParameterType.Int && param.name == "Pose") _hasPoseParam = true;
                else if (param.type == AnimatorControllerParameterType.Trigger && param.name == "Reach") _hasReachParam = true;
            }
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
        _nextHarvestTime = Time.time + 10f;
        _nextSurgeTime = Time.time + 35f;

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
    }

    public void Stall(float seconds)
    {
        _stallUntil = Mathf.Max(_stallUntil, Time.time + seconds);
    }

    private void Update()
    {
        UpdateFrostLimp();

        if (!CanAct)
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
            UpdateAnimator(0f);
            return;
        }

        if (_mode == Mode.Captured)
        {
            if (_agent != null && _agent.isOnNavMesh) _agent.isStopped = true;
            UpdateAnimator(0f);
            return;
        }

        if (_agent == null || !_agent.isOnNavMesh)
        {
            if (_agent != null && NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 12f, NavMesh.AllAreas))
                _agent.Warp(hit.position);
            return;
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
                UpdateAnimator(0f);
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

        UpdateAnimator(_agent.velocity.magnitude);
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

            if (dPlayer >= 10f && dPlayer <= 38f && dElias < bestMidScore)
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
                Stall(1.35f);
                PlayFirst(transform.position, 0.9f, 0.92f, GameAudio.Library.flickerCrackle, GameAudio.Library.bulbPop);
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

        // Check if player just hid in a locker while Elias was watching or close behind
        if (Stealth != null && Stealth.Hidden && Stealth.CurrentHidingSpot != null)
        {
            bool sawHide = (Time.time - _lastSeenPlayerTime) <= 1.75f
                || (_mode == Mode.Chase && FlatDistanceToPlayer(transform.position) <= 8f);
            if (sawHide && _mode != Mode.BustLocker)
            {
                _targetHidingSpot = Stealth.CurrentHidingSpot;
                _mode = Mode.BustLocker;
                _agent.isStopped = false;
                _agent.SetDestination(_targetHidingSpot.FrontPosition);
                LogEvent("Busting locker");
            }
            return;
        }

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
                Hud?.ShowSubtitle(L10n.T("sub.elias.hears_breath"), 2.5f);
            }
        }
    }

    private bool CanSeePlayer()
    {
        if (Player == null || (Stealth != null && Stealth.Hidden)) return false;

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

        if (!_agent.pathPending && _agent.remainingDistance <= 1.1f)
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

        _targetLamp.Kill(lampKillFadeSeconds, allowLockerLamp: CurrentStage >= 3);
        PlayFirst(transform.position, 0.95f, 0.88f, GameAudio.Library.bulbPop, GameAudio.Library.powerDown);
        Stall(0.65f);

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

        int killQuota = Mathf.Max(1, CurrentStage - 1);
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
        if (!wasLockerBust && !_usedFrostShoveThisFloor)
        {
            _usedFrostShoveThisFloor = true;
            _mode = Mode.Stunned;
            _stunnedUntil = Time.time + frostShoveStunSeconds;
            _agent.isStopped = true;
            _agent.velocity = Vector3.zero;

            Torch?.Blackout(3.5f);
            BeginFrostLimp(frostLimpSeconds);
            StartCoroutine(ShakeView(0.45f, 0.35f));
            PlayFirst(transform.position, 1.0f, 0.9f, GameAudio.Library.caught, GameAudio.Library.bulbPop);
            Hud?.ShowSubtitle(L10n.T("sub.elias.frost_shove"), 3.0f);
            LogEvent("First-catch Frost Shove");
            return;
        }

        _mode = Mode.Captured;
        _agent.isStopped = true;
        _agent.velocity = Vector3.zero;
        StartCoroutine(PinPlayer(transform.position + Vector3.up * 1.55f, 0.65f));
        StartCoroutine(ShakeView(0.55f, 0.45f));
        PlayFirst(transform.position, 1.0f, 0.85f, GameAudio.Library.caught, GameAudio.Library.caughtLayer);
        LogEvent("Lethal capture");
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

        // During endgame hatch/gate phase, patrol closer to the player to keep blackout pressure high
        if (_endgameHunting && Player != null)
        {
            PickSearchPointAround(Player.position, 18f);
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

    private void UpdateAnimator(float speed)
    {
        if (_animator == null) return;
        float gait = speed < 0.15f ? 0f : (_mode == Mode.Chase || _mode == Mode.BustLocker ? 6f : 2f);
        _animator.SetFloat(AnimSpeed, gait);
        _animator.SetFloat(AnimMotionSpeed, 1f);
        if (_hasPoseParam)
        {
            int pose = _mode == Mode.Search || _mode == Mode.ColdSurge ? 1 : 0;
            _animator.SetInteger(AnimPose, pose);
        }
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        float dx = a.x - b.x;
        float dz = a.z - b.z;
        return Mathf.Sqrt(dx * dx + dz * dz);
    }
}
