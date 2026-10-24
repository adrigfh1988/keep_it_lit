using UnityEngine;

public partial class AIFollower
{
    private const float ScanSweepDegrees = 60f;
    private const float SightHeatInterval = 2f;

    private void UpdatePerception()
    {
        _sightTimer -= Time.deltaTime;
        if (_sightTimer > 0f)
        {
            return;
        }
        _sightTimer = sightCheckInterval;

        _hasSight = CanSeeTarget();
        if (_hasSight && Time.time >= _nextSightHeatTime)
        {
            _nextSightHeatTime = Time.time + SightHeatInterval;
            AddHeatNear(target.position);
            AddCellHeat(target.position, cellHeatSeen, 0f);
        }
        TrackSightSince();
        UpdateTorchBeamState();
        CheckTorchBeamNoise();
    }

    /// <summary>F104 B2: starts or resets the unbroken-sight clock that IsCommitted reads, and logs the commitment once per chase.</summary>
    private void TrackSightSince()
    {
        if (!_hasSight)
        {
            _sightSince = -1f;
            _commitLogged = false;
            return;
        }

        if (_sightSince < 0f) _sightSince = Time.time;
        if (_commitLogged || !IsCommitted) return;

        _commitLogged = true;
        _terror.Log($"Chase committed at {(_hunting ? _playerSprintSpeed * escapeCommitFraction : _playerWalkSpeed * _chaseWalkFraction):0.0#} m/s", this);
    }

    /// <summary>
    /// F103 A3: refreshes IsInTorchBeam - the lit torch's own (blended, F71) cone from its mount, within its
    /// range, with a plain LOS check so it cannot fire through a wall. Split out of the F73 noise check so
    /// AIPresence's eye glint reads the same fact the hunter's own beam awareness does.
    /// </summary>
    private void UpdateTorchBeamState()
    {
        IsInTorchBeam = false;
        if (_torch == null || target == null || !_torch.IsBeamLit) return;

        Vector3 eyePoint = transform.position + Vector3.up * eyeHeight;
        Vector3 beamOrigin = _torch.BeamOrigin;
        Vector3 toHunter = eyePoint - beamOrigin;
        float distance = toHunter.magnitude;
        if (distance < 0.0001f || distance >= _torch.Range) return;
        if (Vector3.Angle(_torch.BeamForward, toHunter) >= _torch.OuterAngle * 0.5f) return;
        if (!HasLineOfSightIgnoring(beamOrigin, eyePoint, target)) return;

        IsInTorchBeam = true;
        _torchBeamDistance = distance;
    }

    /// <summary>
    /// F73 (B3.1): the hunter notices the player's torch beam landing on it, even without full sight.
    /// When patrolling (Wander or Ambush), being hit by the flashlight triggers its short rage foot-stomp
    /// tantrum (2-3s) before sprinting in the player's direction.
    /// </summary>
    private void CheckTorchBeamNoise()
    {
        if (!IsInTorchBeam || _torch == null) return;
        if (_torchBeamDistance >= _torch.Range * 0.85f) return;

        if ((_state == State.Wander || _state == State.Ambush) && Time.time >= _nextTantrumTime && Time.time >= _tantrumUntil)
        {
            TriggerTorchTantrum();
            return;
        }

        if (_hasSight || Time.time < _torchNoiseCooldownUntil) return;

        _torchNoiseCooldownUntil = Time.time + torchBeamNoiseCooldown;
        HearNoiseInternal(target.position, torchBeamNoiseRadius);
    }

    /// <summary>
    /// Listens for the player. Deliberately routes to Search and never to Chase: a heard sprint
    /// promoting the follower to Chase would give it run speed and a live-tracked destination through
    /// a wall, which reads as cheating and makes sprinting a death sentence rather than a risk.
    /// </summary>
    private void UpdateHearing()
    {
        if (_stealth == null) return;

        _hearingTimer -= Time.deltaTime;
        if (_hearingTimer > 0f) return;
        _hearingTimer = hearingInterval;

        float noiseRadius = _stealth.NoiseRadius;
        if (noiseRadius <= 0.01f) return;
        if (_playerController != null && _playerController.IsSprinting)
            noiseRadius *= 1f + sprintNoisePerLevel * Learned(HunterMemory.Habit.Sprint);
        float hearingScale = EffectiveHearingScale * (_state == State.Ambush ? ambushHearingBoost : 1f);
        if (!CanHear(target.position, noiseRadius * hearingScale)) return;

        AddHeatNear(target.position);
        AddCellHeat(target.position, cellHeatHeard, cellHeatHeardNeighbour);
        _lastKnownPosition = target.position;
        if (_state != State.Chase && _state != State.Captured)
        {
            State previous = _state;
            EnterSearch();
            ReactToNoise(previous);
        }
    }

    /// <summary>
    /// Path-distance hearing test shared by the player's footsteps and one-off noises (a star being
    /// taken). In a maze, sound should travel down corridors, or creeping around a corner buys the
    /// player nothing. Both ends are snapped to the mesh first.
    /// </summary>
    private bool CanHear(Vector3 at, float radius)
    {
        return Perception.TryGetNavPathLength(transform.position, at, radius, out _, 2f);
    }

    /// <summary>
    /// A one-off sound at a point (a star being taken). Same rules as footsteps: heard along corridors,
    /// scaled by the floor's hearing, and it can only ever start a search - never a chase. Returns true
    /// if it was heard, so the caller can say so (a subtitle).
    /// </summary>
    public bool HearNoise(Vector3 at, float radius) => HearNoise(at, radius, NoiseSource.World);

    /// <summary>F120: the same noise with a source tag (the microphone is NoiseSource.Voice). LastHeardSource is set only when the hunter reacts.</summary>
    public bool HearNoise(Vector3 at, float radius, NoiseSource source)
    {
        MazeNoise.Emit(at, radius);
        bool heard = HearNoiseInternal(at, radius);
        if (heard) LastHeardSource = source;
        return heard;
    }

    /// <summary>F107 D2: the hunter's half of HearNoise, without the MazeNoise broadcast. The torch-beam check calls this directly.</summary>
    private bool HearNoiseInternal(Vector3 at, float radius)
    {
        if (IsDormantForSoloEntity || _agent == null || !_agent.isOnNavMesh || _state == State.Captured || GameFlow.IsInIntake || GameFlow.IsInHub) return false;

        if (TryEndAmbushForStar(at)) return true;

        if (!CanHear(at, radius * EffectiveHearingScale)) return false;

        AddHeatNear(at);
        AddCellHeat(at, cellHeatHeard, cellHeatHeardNeighbour);
        _lastKnownPosition = at;
        if (_state != State.Chase)
        {
            State previous = _state;
            EnterSearch();
            ReactToNoise(previous);
        }
        return true;
    }

    /// <summary>
    /// NavMesh path length between two points, snapping both onto the mesh first. Used by the F45 ambush picks.
    /// </summary>
    private bool TryPathLength(Vector3 from, Vector3 to, out float length)
    {
        return Perception.TryGetNavPathLength(from, to, float.MaxValue, out length, navMeshSearchRadius);
    }

    private bool CanSeeTarget()
    {
        Vector3 eye = transform.position + Vector3.up * eyeHeight;
        Vector3 chest = target.position + Vector3.up * targetHeight;

        float view = viewDistance * (_stealth != null ? _stealth.VisibilityMultiplier : 1f);
        if (_stealth != null && !_stealth.FlashlightOn) view *= 1f + darkViewPerLevel * Learned(HunterMemory.Habit.Dark);

        Vector3 flat = FlatDirection(chest - eye);
        if (flat.sqrMagnitude > view * view)
        {
            return false;
        }
        if (flat.sqrMagnitude < 0.0001f)
        {
            return true;
        }

        Vector3 facing = FlatDirection(Quaternion.AngleAxis(_scanYawOffset, Vector3.up) * ModelForward());
        if (Vector3.Angle(facing, flat) > viewAngle * 0.5f)
        {
            return false;
        }

        return HasLineOfSight(eye, chest);
    }

    /// <summary>
    /// F50 decision 8: a slow ±60 degree sine sweep (one full cycle over the dwell), active only while
    /// standing still at a search spot with no look clip to show it (bodySweepsDuringSearch), and never
    /// at a hiding spot front.
    /// </summary>
    private float ComputeScanYawOffset()
    {
        if (_state != State.Search || _searchDwellTimer <= 0f || bodySweepsDuringSearch || AtHidingSpotFront())
        {
            return 0f;
        }

        float elapsed = Mathf.Clamp(searchDwellTime - _searchDwellTimer, 0f, searchDwellTime);
        float t = searchDwellTime > 0.0001f ? elapsed / searchDwellTime : 0f;
        return Mathf.Sin(t * Mathf.PI * 2f) * ScanSweepDegrees;
    }

    /// <summary>True while the search sweep's current front-of-queue spot is a hiding spot (a locker front).</summary>
    private bool AtHidingSpotFront()
    {
        return _searchQueue.Count > 0 && _hidingSpots.Contains(_searchQueue[0]);
    }

    /// <summary>
    /// The raycast half of CanSeeTarget. The maze walls sit on the same layer as the player, so the
    /// blocker is identified by its hierarchy rather than by a layer mask.
    /// </summary>
    private bool HasLineOfSight(Vector3 from, Vector3 to)
    {
        return Perception.HasClearLine(from, to, transform, target);
    }

    /// <summary>
    /// F73: the reverse of HasLineOfSight above - cast from `from` (the player's camera) to `to` (a
    /// point on the hunter), ignoring `ignoreNear`'s own colliders and the hunter's own colliders.
    /// </summary>
    private bool HasLineOfSightIgnoring(Vector3 from, Vector3 to, Transform ignoreNear)
    {
        return Perception.HasClearLine(from, to, ignoreNear, transform);
    }

    /// <summary>F104 B1: true if the point (at chest height) is inside the camera's view with nothing solid between, ignoring the player and this hunter.</summary>
    private bool IsSeenByCamera(Vector3 point)
    {
        Camera cam = Camera.main;
        if (cam == null) return false;

        Vector3 chest = point + Vector3.up * eyeHeight * 0.75f;
        Vector3 viewport = cam.WorldToViewportPoint(chest);
        if (viewport.z <= 0f || viewport.x < 0f || viewport.x > 1f || viewport.y < 0f || viewport.y > 1f) return false;

        return Perception.HasClearLine(cam.transform.position, chest, transform, target, Physics.DefaultRaycastLayers);
    }
}
