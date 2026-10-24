using UnityEngine;
using UnityEngine.AI;

public partial class AIFollower
{
    /// <summary>Body speed (m/s) above which the run clip replaces the walk.</summary>
    private float WalkToRunSpeed()
    {
        return EntityGaitAnimator.ComputeHunterWalkToRunSpeed(locomotionWalkSpeed, locomotionRunSpeed, maxWalkPlayback, maxLocomotionPlayback);
    }

    /// <summary>F72: a closed MazeDoor bashed open and held the hunter here for `seconds`.</summary>
    public void Stall(float seconds)
    {
        _stallUntil = Mathf.Max(_stallUntil, Time.time + seconds);
    }

    /// <summary>
    /// F75: the Stalker's touch forces the hunter to "know" the player is exactly here, on top of the
    /// HearNoise it also calls.
    /// </summary>
    public void ForceLastKnownPosition(Vector3 position, bool reactAsHeard = true)
    {
        if (IsDormantForSoloEntity || _state == State.Captured) return;
        _lastKnownPosition = position;
        if (_state != State.Chase)
        {
            State previous = _state;
            EnterSearch();
            if (reactAsHeard) ReactToNoise(previous);
        }
    }

    /// <summary>F104 B1: true while the DreadMeter's backoff runs.</summary>
    public bool IsBackingOff => Time.time < _backoffUntil;

    /// <summary>
    /// F104 B1: lets the player breathe. The hunter drops whatever it was doing and goes to wander far away for
    /// <paramref name="seconds"/> - warped there if neither end is in the camera's sight, walking otherwise.
    /// </summary>
    public bool BackOff(float seconds, Vector3 farPoint)
    {
        if (IsDormantForSoloEntity || _agent == null || _state == State.Chase || _state == State.Captured || _hunting || Time.time < _stunnedUntil) return false;

        if (_state == State.Ambush) LeaveAmbush();
        _agent.isStopped = false;
        _state = State.Wander;
        _backoffUntil = Time.time + seconds;
        CancelHeardYouBeat();

        bool unseen = !IsSeenByCamera(transform.position) && !IsSeenByCamera(farPoint);
        if (unseen && NavMesh.SamplePosition(farPoint, out NavMeshHit hit, 3f, NavMesh.AllAreas) && _agent.Warp(hit.position))
        {
            _wanderIndex = -1;
            _repathTimer = 0f;
            return true;
        }

        _wanderIndex = _wanderPoints.Count > 0 ? 0 : -1;
        _wanderRepickCooldown = 0.25f;
        SetDestinationNear(farPoint);
        return true;
    }

    /// <summary>
    /// F103 A7: something it heard (not saw) pulled it off a patrol or out of an ambush - it stops dead, its servos whine,
    /// the game says IT HEARD YOU, then it rushes the spot it heard for rushSeconds.
    /// </summary>
    private void ReactToNoise(State previous)
    {
        if (!_terror.hunterListens || (previous != State.Wander && previous != State.Ambush) || Time.time < _nextListenTime) return;

        _listenUntil = Time.time + listenPauseSeconds;
        _rushUntil = _listenUntil + rushSeconds;
        _nextListenTime = Time.time + listenCooldown;
        LastHeardYouTime = Time.time;
        _presence?.PlayAlert();
        HeardYou?.Invoke();
        _terror.Log($"Hunter heard you -> rush {rushSeconds:0.#}s at {_rushSpeed:0.0} m/s to {_lastKnownPosition}", this);
    }

    /// <summary>F103 A7: true during the burst after the listening pause (or after the flashlight tantrum).</summary>
    private bool IsRushing => Time.time >= _listenUntil && Time.time >= _tantrumUntil && Time.time < _rushUntil;

    private void CancelHeardYouBeat()
    {
        _listenUntil = -1f;
        _tantrumUntil = -1f;
        _rushUntil = -1f;
    }

    /// <summary>Triggers the short rage foot-stomp tantrum (2-3s) before sprinting toward the player's position.</summary>
    public void TriggerTorchTantrum()
    {
        if (_state == State.Captured || _agent == null || !_agent.isOnNavMesh) return;
        if (_state == State.Ambush) LeaveAmbush();

        _tantrumUntil = Time.time + tantrumDuration;
        _listenUntil = -1f;
        _rushUntil = _tantrumUntil + rushSeconds;
        _nextTantrumTime = Time.time + tantrumCooldown;
        _torchNoiseCooldownUntil = Time.time + torchBeamNoiseCooldown;

        if (target != null)
        {
            _lastKnownPosition = target.position;
            AddHeatNear(target.position);
            AddCellHeat(target.position, cellHeatHeard, cellHeatHeardNeighbour);
        }

        if (_state != State.Chase)
        {
            EnterSearch();
        }

        _presence?.RevealEyes(tantrumDuration + rushSeconds);
        _presence?.PlayAlert();
        HeardYou?.Invoke();
        _terror.Log($"Hunter flashlight tantrum ({tantrumDuration:0.00}s) -> sprint {rushSeconds:0.#}s at {_rushSpeed:0.0} m/s", this);
    }

    // ---------------------------------------------------------------- movement helpers

    private void SetDestinationNear(Vector3 position)
    {
        if (NavMesh.SamplePosition(position, out NavMeshHit hit, navMeshSearchRadius, NavMesh.AllAreas))
        {
            _agent.SetDestination(hit.position);
        }
        else
        {
            _agent.SetDestination(position);
        }
    }

    private void MoveSpeedTowards(float desiredSpeed)
    {
        _agent.speed = Mathf.MoveTowards(_agent.speed, desiredSpeed, speedRampRate * Time.deltaTime);
    }

    /// <summary>
    /// F23 relocation primitive: warp onto the maze near `position`, then search toward `towards` as
    /// though it just heard something there. Refuses mid-chase or captured.
    /// </summary>
    public bool RelocateTo(Vector3 position, Vector3 towards)
    {
        if (IsDormantForSoloEntity || _agent == null || _state == State.Captured || _state == State.Chase) return false;
        if (!NavMesh.SamplePosition(position, out NavMeshHit hit, 1.5f, NavMesh.AllAreas)) return false;
        if (!_agent.Warp(hit.position)) return false;

        _hasSight = false;
        _sightTimer = 0f;
        _repathTimer = 0f;
        _lastKnownPosition = towards;
        CancelHeardYouBeat();
        _backoffUntil = -1f;
        EnterSearch();
        return true;
    }

    /// <summary>
    /// F36 Second Wind: the hunter lets go. Leaves Captured, warps to `warpTo` and stands stunned for `stunSeconds`, then wanders.
    /// </summary>
    public bool Release(Vector3 warpTo, float stunSeconds)
    {
        if (_agent == null || _state != State.Captured) return false;
        if (NavMesh.SamplePosition(warpTo, out NavMeshHit hit, 3f, NavMesh.AllAreas)) _agent.Warp(hit.position);
        CancelHeardYouBeat();
        _backoffUntil = -1f;

        _state = State.Wander;
        _wanderIndex = -1;
        _bustingHidingSpot = false;
        _hasSight = false;
        _sightTimer = 0f;
        _lastKnownPosition = transform.position;
        _stunnedUntil = Time.time + stunSeconds;
        _agent.isStopped = true;
        _agent.velocity = Vector3.zero;
        _presence?.SetFaceMode(AIPresence.FaceMode.Normal);
        _presence?.ReleaseHold();
        ChaseStateChanged?.Invoke(false);
        return true;
    }

    private Vector3 ModelForward()
    {
        return FlatDirection(Quaternion.AngleAxis(_modelYawOffset, Vector3.up) * transform.forward);
    }

    private Vector3 CurrentHeading()
    {
        Vector3 velocity = FlatDirection(_agent.velocity);
        if (velocity.sqrMagnitude > 0.0001f)
        {
            return velocity;
        }

        return _agent.hasPath ? FlatDirection(_agent.steeringTarget - transform.position) : Vector3.zero;
    }

    private void UpdateRotation(Vector3 lookDirection)
    {
        if (lookDirection.sqrMagnitude < 0.0001f)
        {
            _turnRaw = 0f;
            return;
        }

        float rate = (_state == State.Chase && _hasSight)
            ? turnSpeed
            : (_agent.velocity.sqrMagnitude > 0.01f ? turnSpeedMoving : turnSpeedStationary);

        Quaternion before = transform.rotation;
        Quaternion desired = Quaternion.LookRotation(lookDirection.normalized, Vector3.up) * Quaternion.Euler(0f, -_modelYawOffset, 0f);
        transform.rotation = Quaternion.RotateTowards(before, desired, rate * Time.deltaTime);

        float dt = Time.deltaTime;
        if (dt > 1e-5f)
        {
            float signedYaw = Vector3.SignedAngle(
                FlatDirection(before * Vector3.forward),
                FlatDirection(transform.rotation * Vector3.forward),
                Vector3.up);
            _turnRaw = Mathf.Clamp(signedYaw / dt / 180f, -1f, 1f);
        }
        else
        {
            _turnRaw = 0f;
        }
    }

    private void UpdateAnimator()
    {
        Pose pose = Pose.Idle;
        if (Time.time < _stunnedUntil)
        {
            pose = Pose.Stunned;
        }
        else if (Time.time < _tantrumUntil)
        {
            pose = Pose.Tantrum;
        }
        else if (_state == State.Ambush && _ambushArrived)
        {
            pose = Pose.Lurk;
        }
        else if (_state == State.Search && _searchDwellTimer > 0f && !AtHidingSpotFront())
        {
            pose = Pose.LookAround;
        }
        CurrentPose = pose;

        if (animator == null)
        {
            return;
        }

        float speed = FlatDirection(_agent.velocity).magnitude;
        _animationBlend = Mathf.Lerp(_animationBlend, speed, Time.deltaTime * animationBlendRate);
        if (_animationBlend < 0.01f)
        {
            _animationBlend = 0f;
        }

        _turnBlend = Mathf.Lerp(_turnBlend, _turnRaw, Time.deltaTime * animationBlendRate);
        if (Mathf.Abs(_turnBlend) < 0.005f)
        {
            _turnBlend = 0f;
        }
        _turnRaw = 0f;

        float v = _animationBlend;
        float switchSpeed = WalkToRunSpeed();
        float targetGait;
        if (v < 0.05f) targetGait = 0f;
        else if (_gaitRunning ? v > switchSpeed * 0.85f : v > switchSpeed) { targetGait = 2f; _gaitRunning = true; }
        else { targetGait = 1f; _gaitRunning = false; }
        _gait = Mathf.MoveTowards(_gait, targetGait, Time.deltaTime * 4f);
        float clipSpeed = _gaitRunning ? locomotionRunSpeed : locomotionWalkSpeed;
        float playback = v < 0.05f || clipSpeed < 0.05f ? 1f : Mathf.Clamp(v / clipSpeed, MinLocomotionPlayback, maxLocomotionPlayback);
        animator.SetFloat(AnimSpeed, _gait);
        AnimPlayback = playback;
        animator.SetFloat(AnimMotionSpeed, playback);
        animator.SetBool(AnimGrounded, true);

        if (_hasPoseParam) animator.SetInteger(AnimPose, (int)pose);
        if (_hasTurnParam) animator.SetFloat(AnimTurn, _turnBlend);
    }

    private void PlaceOnNavMesh(Vector3 around, float radius)
    {
        if (NavMesh.SamplePosition(around, out NavMeshHit hit, radius, NavMesh.AllAreas))
        {
            _agent.Warp(hit.position);
        }
    }

    private static Vector3 FlatDirection(Vector3 v)
    {
        v.y = 0f;
        return v;
    }
}
