using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Random = UnityEngine.Random;

public partial class AIFollower
{
    // ---------------------------------------------------------------- escalation (effective values)

    /// <summary>How long sight is forgiven after losing it. Lerps 50%..100% of the floor's value.</summary>
    private float EffectiveLoseSightTime => Mathf.Lerp(loseSightTime * 0.5f, loseSightTime, _threat);

    /// <summary>Hard cap on a single search sweep. Lerps 50%..100% of the floor's value.</summary>
    private float EffectiveSearchBudget => Mathf.Lerp(searchBudget * 0.5f, searchBudget, _threat);

    /// <summary>Chance of heading for a live star while wandering. Lerps 50%..100% of the floor's value.</summary>
    private float EffectivePatrolBias => Mathf.Lerp(patrolBias * 0.5f, patrolBias, _threat);

    /// <summary>How far a noise carries. Floor's hearing scale, further scaled 60%..100% by threat.</summary>
    private float EffectiveHearingScale => _hearingScale * Mathf.Lerp(0.6f, 1f, _threat) * (IsBackingOff ? backoffHearingScale : 1f);

    /// <summary>How many spots a search sweep checks. Lerps 50%..100% of the floor's value.</summary>
    private int EffectiveSearchPoints => Mathf.RoundToInt(Mathf.Lerp(searchPoints * 0.5f, searchPoints, _threat));

    /// <summary>Chance a wander leg heads for the cell nearest the player instead of a star or random cell. 0 at zero progress.</summary>
    private float HunchBias => IsBackingOff ? 0f : Mathf.Lerp(0f, hunchBiasAtPeak, _threat);

    /// <summary>Chance a wander leg becomes an ambush. Read raw.</summary>
    private float EffectiveAmbushBias => ambushBias;

    /// <summary>Maximum time lying in wait. Read raw.</summary>
    private float EffectiveAmbushWait => ambushWaitSeconds;

    private float SpeedThreat => Mathf.Max(_threat, _speedThreatFloor);

    /// <summary>
    /// How dangerous the follower currently is, from 0 (nothing collected, barely stirring) to 1.
    /// <paramref name="hunting"/> is the endgame: once the hatch is open it runs everywhere, whether
    /// it can see the player or not.
    /// </summary>
    public void SetThreatLevel(float normalized, bool hunting)
    {
        _threat = Mathf.Clamp01(normalized);
        _hunting = hunting;
    }

    /// <summary>10 Oct 2026: the hunter's pace starts where one collected star would put it (1 / stars on the floor).</summary>
    public void SetStarCount(int total) => _speedThreatFloor = total > 0 ? 1f / total : 0f;

    /// <summary>
    /// Sets how dangerous this floor's hunter is. Called by MazeGenerator before Awake, so it only
    /// writes fields. The speed cap against the player's sprint still applies afterwards in Start.
    /// </summary>
    public void ApplyDifficulty(FloorProfile profile)
    {
        if (profile == null) return;

        dormantSpeed = profile.DormantSpeed;
        moveSpeed = profile.WalkSpeed;
        runSpeed = profile.RunSpeed;
        viewDistance = profile.ViewDistance;
        viewAngle = profile.ViewAngle;
        _hearingScale = profile.HearingScale;
        loseSightTime = profile.LoseSightTime;
        patrolBias = profile.PatrolBias;
        searchBudget = profile.SearchBudget;
        _chaseWalkFraction = profile.ChaseWalkFraction;
        _chaseCommitScale = profile.ChaseCommitScale;

        ambushBias = profile.AmbushBias;
        ambushWaitSeconds = profile.AmbushWaitSeconds;
        ambushHearingBoost = profile.AmbushHearingBoost;
    }

    /// <summary>Speed while patrolling or searching.</summary>
    private float WanderSpeed()
    {
        return _hunting ? runSpeed : Mathf.Lerp(dormantSpeed, moveSpeed, SpeedThreat);
    }

    /// <summary>
    /// Speed while it can see the player. F104 B2: once it has held the player in sight for
    /// chaseCommitSeconds it commits to a chase it can win.
    /// </summary>
    private float ChaseSpeed()
    {
        if (_hunting)
        {
            return IsCommitted ? Mathf.Max(runSpeed, _playerSprintSpeed * escapeCommitFraction) : runSpeed;
        }

        float eased = Mathf.Lerp(dormantSpeed, moveSpeed, SpeedThreat);
        return IsCommitted ? Mathf.Max(eased, Mathf.Min(_playerWalkSpeed * _chaseWalkFraction, _speedCap)) : eased;
    }

    /// <summary>F104 B2: unbroken sight for chaseCommitSeconds.</summary>
    private bool IsCommitted => _terror.committedChase && _sightSince >= 0f && Time.time - _sightSince >= chaseCommitSeconds * _chaseCommitScale;

    /// <summary>
    /// F105 C5: the lure star was taken under this hunter's ambush - it goes straight to a chase on the player's position instead of a search.
    /// </summary>
    public bool StartLureChase(Vector3 playerPosition)
    {
        if (IsDormantForSoloEntity || _agent == null || !_agent.isOnNavMesh || _state == State.Captured || _state == State.Chase || GameFlow.IsInIntake || GameFlow.IsInHub) return false;

        if (_state == State.Ambush) LeaveAmbush();
        CancelHeardYouBeat();
        _lastKnownPosition = playerPosition;
        _lostSightTimer = EffectiveLoseSightTime;
        _listenUntil = -1f;
        _agent.isStopped = false;
        _state = State.Chase;
        ChaseStateChanged?.Invoke(true);
        return true;
    }

    private void UpdateChaseOnly()
    {
        _repathTimer -= Time.deltaTime;
        if (_repathTimer <= 0f)
        {
            _repathTimer = repathInterval;
            SetDestinationNear(target.position);

            if (Vector3.Distance(transform.position, target.position) > teleportDistance)
            {
                PlaceOnNavMesh(target.position, navMeshSearchRadius);
            }
        }

        float distance = FlatDirection(target.position - transform.position).magnitude;
        MoveSpeedTowards(distance > runDistance ? runSpeed : moveSpeed);
        UpdateRotation(FlatDirection(target.position - transform.position));
    }

    // ---------------------------------------------------------------- states

    private void UpdateStateMachine()
    {
        if (_state == State.Captured)
        {
            TickCaptured();
            return;
        }

        State previous = _state;

        bool hiddenNow = _stealth != null && _stealth.Hidden;
        if (hiddenNow && !_wasHidden && _state == State.Chase && _hasSight)
        {
            _bustingHidingSpot = true;
            _state = State.Chase;
        }
        if (!hiddenNow) _bustingHidingSpot = false;
        _wasHidden = hiddenNow;

        if (_hasSight)
        {
            _lostSightTimer = EffectiveLoseSightTime;
            _lastKnownPosition = target.position;

            if (_state != State.Chase)
            {
                if (_state == State.Ambush) LeaveAmbush();
                _state = State.Chase;
                _listenUntil = -1f;
            }
        }
        else if (_state == State.Chase && !_bustingHidingSpot)
        {
            _lostSightTimer -= Time.deltaTime;
            if (_lostSightTimer <= 0f)
            {
                EnterSearch();
            }
        }

        if (_state != previous && (_state == State.Chase || previous == State.Chase))
        {
            ChaseStateChanged?.Invoke(_state == State.Chase);
        }

        switch (_state)
        {
            case State.Chase:
                TickChase();
                break;
            case State.Search:
                TickSearch();
                break;
            case State.Ambush:
                TickAmbush();
                break;
            default:
                TickWander();
                break;
        }
    }

    private void TickChase()
    {
        _agent.stoppingDistance = Mathf.Min(stoppingDistance, captureRadius - 0.3f);
        _agent.isStopped = false;
        MoveSpeedTowards(IsRushing ? Mathf.Max(ChaseSpeed(), _rushSpeed) : ChaseSpeed());

        _repathTimer -= Time.deltaTime;
        if (_repathTimer <= 0f)
        {
            _repathTimer = repathInterval;
            SetDestinationNear(_bustingHidingSpot ? _lastKnownPosition : (_hasSight ? target.position : _lastKnownPosition));
        }

        if (_hasSight)
        {
            UpdateRotation(FlatDirection(target.position - transform.position));
        }
        else
        {
            UpdateRotation(CurrentHeading());
        }

        float radius = _bustingHidingSpot ? captureRadius + 0.8f : captureRadius;
        if (GameFlow.IsRunActive && (_hasSight || _bustingHidingSpot)
            && FlatDirection(target.position - transform.position).sqrMagnitude <= radius * radius)
        {
            if (_bustingHidingSpot && _stealth != null)
            {
                _stealth.CurrentHidingSpot?.Expose();
            }

            EnterCaptured();
        }
    }

    private void EnterCaptured()
    {
        LastCaptureWasBust = _bustingHidingSpot;
        _state = State.Captured;
        CancelHeardYouBeat();
        _backoffUntil = -1f;
        _bustingHidingSpot = false;
        _agent.isStopped = true;
        _agent.velocity = Vector3.zero;
        if (_hasReachParam) animator.SetTrigger(AnimReach);
        PlayerCaught?.Invoke();
    }

    private void TickCaptured()
    {
        UpdateRotation(FlatDirection(target.position - transform.position));
    }

    /// <summary>
    /// Queue up a handful of spots around the last known position.
    /// </summary>
    private void EnterSearch()
    {
        if (_state == State.Ambush) LeaveAmbush();

        _state = State.Search;
        _searchBudgetTimer = EffectiveSearchBudget;
        _searchDwellTimer = 0f;
        _hotDwellSpot = -1;
        _agent.isStopped = false;
        _searchQueue.Clear();

        foreach (Vector3 point in _wanderPoints)
        {
            if (Vector3.Distance(point, _lastKnownPosition) <= searchRadius)
            {
                _searchQueue.Add(point);
            }
        }

        for (int i = _searchQueue.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (_searchQueue[i], _searchQueue[j]) = (_searchQueue[j], _searchQueue[i]);
        }

        int effectiveSearchPoints = EffectiveSearchPoints;
        if (_searchQueue.Count > effectiveSearchPoints)
        {
            _searchQueue.RemoveRange(effectiveSearchPoints, _searchQueue.Count - effectiveSearchPoints);
        }

        _searchQueue.Insert(0, _lastKnownPosition);

        List<Vector3> nearbyHidingSpots = new List<Vector3>();
        foreach (Vector3 spot in _hidingSpots)
        {
            if (Vector3.Distance(spot, _lastKnownPosition) <= HidingSpotSearchRadius)
            {
                nearbyHidingSpots.Add(spot);
            }
        }
        nearbyHidingSpots.Sort((a, b) =>
            Vector3.Distance(a, _lastKnownPosition).CompareTo(Vector3.Distance(b, _lastKnownPosition)));
        for (int i = 0; i < nearbyHidingSpots.Count; i++)
        {
            _searchQueue.Insert(1 + i, nearbyHidingSpots[i]);
        }

        InsertHotSpots(1 + nearbyHidingSpots.Count);

        SetDestinationNear(_searchQueue[0]);
    }

    private void TickSearch()
    {
        _agent.stoppingDistance = wanderStoppingDistance;
        MoveSpeedTowards(IsRushing ? Mathf.Max(WanderSpeed(), _rushSpeed) : WanderSpeed());

        _searchBudgetTimer -= Time.deltaTime;
        if (_searchBudgetTimer <= 0f || _searchQueue.Count == 0)
        {
            _state = State.Wander;
            _wanderRepickCooldown = 0f;
            return;
        }

        if (_searchDwellTimer > 0f)
        {
            _searchDwellTimer -= Time.deltaTime;
            _agent.isStopped = true;

            if (AtHidingSpotFront())
            {
                UpdateRotation(FlatDirection(_searchQueue[0] - transform.position));
            }
            else if (bodySweepsDuringSearch)
            {
                UpdateRotation(Quaternion.AngleAxis(120f * Time.deltaTime, Vector3.up) * ModelForward());
            }

            if (_searchDwellTimer <= 0f && _hotDwellSpot >= 0 && TryPeek(_hotDwellSpot)) return;

            if (_searchDwellTimer <= 0f)
            {
                _agent.isStopped = false;
                _searchQueue.RemoveAt(0);
                if (_searchQueue.Count > 0) SetDestinationNear(_searchQueue[0]);
            }
            return;
        }

        _agent.isStopped = false;

        if (ReachedDestination())
        {
            _searchDwellTimer = searchDwellTime + BeginHotDwell();
            return;
        }

        UpdateRotation(CurrentHeading());
    }

    private void TickWander()
    {
        _agent.stoppingDistance = wanderStoppingDistance;
        _agent.isStopped = false;
        MoveSpeedTowards(WanderSpeed());

        _wanderRepickCooldown -= Time.deltaTime;
        if (_wanderIndex < 0 || (_wanderRepickCooldown <= 0f && ReachedDestination()))
        {
            PickWanderPoint();
        }

        UpdateRotation(CurrentHeading());
    }

    private void PickWanderPoint()
    {
        if (_wanderPoints.Count == 0)
        {
            return;
        }

        if (_hunting && _gatePatrol.Count >= 2)
        {
            int gateLeg = WanderPointNearestNextGate();
            if (gateLeg >= 0)
            {
                _wanderIndex = gateLeg;
                _wanderRepickCooldown = 0.25f;
                SetDestinationNear(_wanderPoints[gateLeg]);
                return;
            }
        }

        if (_maze != null && _patrolTargets != null && Random.value < EffectiveAmbushBias && TryEnterAmbush())
        {
            return;
        }

        int next = -1;
        if (Random.value < HunchBias)
        {
            next = NearestCellToTarget();
        }

        if (next < 0 && _patrolTargets != null && Random.value < EffectivePatrolBias)
        {
            next = NearestCellToLivePatrolTarget();
        }

        if (next < 0 && _terror.learningHunter && !IsBackingOff && Random.value < heatWanderBias * SpeedThreat)
        {
            next = WarmestWanderPoint();
            if (next >= 0) _terror.Log("Hunter heads for where you were noisy", this);
        }

        if (next < 0 && _maze != null)
        {
            next = _maze.PickShapeWanderCellIndex(transform.position, target != null ? target.position : transform.position, _wanderIndex, _wanderPoints);
        }

        if (next < 0)
        {
            next = Random.Range(0, _wanderPoints.Count);
        }

        if (_wanderPoints.Count > 1)
        {
            while (next == _wanderIndex)
            {
                next = Random.Range(0, _wanderPoints.Count);
            }
        }

        _wanderIndex = next;
        _wanderRepickCooldown = 0.25f;
        SetDestinationNear(_wanderPoints[next]);
    }

    private bool ReachedDestination()
    {
        if (_agent.pathPending)
        {
            return false;
        }

        if (_agent.pathStatus != NavMeshPathStatus.PathComplete)
        {
            return true;
        }

        if (!_agent.hasPath)
        {
            return true;
        }

        return _agent.remainingDistance <= _agent.stoppingDistance + 0.05f;
    }

    /// <summary>Index of the wander point closest to the nearest star still standing, or -1.</summary>
    private int NearestCellToLivePatrolTarget()
    {
        Transform best = null;
        float bestDistance = float.MaxValue;

        for (int i = 0; i < _patrolTargets.Count; i++)
        {
            Transform candidate = _patrolTargets[i];
            if (candidate == null) continue;

            float distance = Vector3.Distance(transform.position, candidate.position);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = candidate;
            }
        }

        if (best == null) return -1;

        int nearestCell = -1;
        float nearestCellDistance = float.MaxValue;
        for (int i = 0; i < _wanderPoints.Count; i++)
        {
            float distance = Vector3.Distance(_wanderPoints[i], best.position);
            if (distance < nearestCellDistance)
            {
                nearestCellDistance = distance;
                nearestCell = i;
            }
        }

        return nearestCell;
    }

    /// <summary>F111: index of the wander point nearest the closest gate it is not already standing at (within a cell), or -1.</summary>
    private int WanderPointNearestNextGate()
    {
        float atGate = _maze != null ? _maze.CellSize : 3f;
        int gate = -1;
        float gateDistance = float.MaxValue;
        for (int i = 0; i < _gatePatrol.Count; i++)
        {
            float distance = FlatDirection(_gatePatrol[i] - transform.position).magnitude;
            if (distance <= atGate || distance >= gateDistance) continue;
            gateDistance = distance;
            gate = i;
        }

        if (gate < 0) return -1;

        int nearest = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < _wanderPoints.Count; i++)
        {
            float distance = Vector3.Distance(_wanderPoints[i], _gatePatrol[gate]);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearest = i;
            }
        }

        if (nearest >= 0) _terror.Log($"Hunter patrols to exit gate {gate}", this);
        return nearest;
    }

    /// <summary>Index of the wander point closest to the player's actual position, or -1.</summary>
    private int NearestCellToTarget()
    {
        if (target == null) return -1;

        int nearestCell = -1;
        float nearestDistance = float.MaxValue;
        for (int i = 0; i < _wanderPoints.Count; i++)
        {
            float distance = Vector3.Distance(_wanderPoints[i], target.position);
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestCell = i;
            }
        }

        return nearestCell;
    }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
    /// <summary>F120 test hook: forces a capture of the player (a bust when `asBust`).</summary>
    [ContextMenu("Debug: capture the player")]
    public void DebugCapture() => DebugCapture(false);

    public void DebugCapture(bool asBust)
    {
        if (!GameFlow.IsRunActive || _state == State.Captured || _agent == null) return;
        _bustingHidingSpot = asBust;
        EnterCaptured();
    }
#endif
}
