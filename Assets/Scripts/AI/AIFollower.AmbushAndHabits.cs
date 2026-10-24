using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

public partial class AIFollower
{
    private const float HideMatchRadius = 0.5f;
    private const float PeekEyeLinger = 0.6f;

    // ---------------------------------------------------------------- F105 C6: what it has learned

    /// <summary>F105 C6: how well it has learned a habit this campaign (0 with the learningHunter flag off).</summary>
    private int Learned(HunterMemory.Habit habit) => _terror.learningHunter ? HunterMemory.Level(habit) : 0;

    private float PeekChanceScale => peekChanceScale + lockerPeekChancePerLevel * Learned(HunterMemory.Habit.Lockers);
    private float StandOutsideSeconds => standOutsideSeconds + lockerStandOutsidePerLevel * Learned(HunterMemory.Habit.Lockers);
    private float HidingSpotSearchRadius => hidingSpotSearchRadius + lockerSearchRadiusPerLevel * Learned(HunterMemory.Habit.Lockers);
    private int MaxPeeksPerFloor => Learned(HunterMemory.Habit.Lockers) >= lockerSecondPeekLevel ? 2 : 1;

    /// <summary>F105 C6 layer 1: the player was heard (center + neighbours) or seen (center only) at `at`.</summary>
    private void AddCellHeat(Vector3 at, float center, float neighbour)
    {
        if (!_terror.learningHunter || _maze == null || !_maze.TryWorldToCell(at, out Vector2Int cell)) return;

        _cellHeat.AddWithNeighbours(cell.x, cell.y, center, neighbour, Time.time);
    }

    /// <summary>
    /// F105 C6 layer 1: the wander point whose cell is warmest (heat minus distance / heatDistanceFalloff), or -1 if none is
    /// warm enough to be worth a detour. The point it is standing at is skipped.
    /// </summary>
    private int WarmestWanderPoint()
    {
        if (_maze == null) return -1;

        int best = -1;
        float bestScore = float.MinValue;
        for (int i = 0; i < _wanderPoints.Count; i++)
        {
            if (i == _wanderIndex || !_maze.TryWorldToCell(_wanderPoints[i], out Vector2Int cell)) continue;

            float heat = _cellHeat.HeatAt(cell.x, cell.y, Time.time);
            if (heat < minWanderHeat) continue;

            float score = heat - FlatDirection(_wanderPoints[i] - transform.position).magnitude / heatDistanceFalloff;
            if (score <= bestScore) continue;
            bestScore = score;
            best = i;
        }

        return best;
    }

    /// <summary>
    /// F105 C6 layer 2 (Throws): a thrown item landed at `impact`. It goes through HearNoise like any one-off sound, but a hunter
    /// that has learned the bottles (level 1+) ignores a throw heard within throwIgnoreWindow of the last one it acted on, and at
    /// levels 2-3 an investigated throw sometimes searches the player's own position instead. Returns true if it was heard.
    /// </summary>
    public bool HearThrow(Vector3 impact, float radius)
    {
        int level = Learned(HunterMemory.Habit.Throws);
        if (level >= 1 && Time.time - _lastThrowHeardTime < throwIgnoreWindow)
        {
            _terror.Log($"Hunter ignores a second throw (level {level})", this);
            return false;
        }

        if (!HearNoise(impact, radius)) return false;

        _lastThrowHeardTime = Time.time;
        float seesThrough = level >= 3 ? throwSeesThroughLevel3 : level == 2 ? throwSeesThroughLevel2 : 0f;
        if (target != null && Random.value < seesThrough)
        {
            ForceLastKnownPosition(target.position, false);
            _terror.Log($"Hunter sees through the throw (level {level}) and searches the player", this);
        }
        return true;
    }

    // ---------------------------------------------------------------- F104 B3: locker heat

    /// <summary>F104 B3: how hot a hiding spot is now - its stored heat cooled linearly by the time since the last event.</summary>
    private float HeatOf(int i)
    {
        return Mathf.Max(0f, _hidingHeat[i] - (Time.time - _hidingHeatAt[i]) / heatDecaySeconds);
    }

    /// <summary>F104 B3: the player was heard or seen at `at`, so every hiding spot within heatRadius of it gets hotter.</summary>
    private void AddHeatNear(Vector3 at)
    {
        if (!_terror.sanctuaryDecay) return;

        for (int i = 0; i < _hidingSpots.Count; i++)
        {
            if (Vector3.Distance(_hidingSpots[i], at) > heatRadius) continue;
            _hidingHeat[i] = Mathf.Min(1f, HeatOf(i) + heatPerEvent);
            _hidingHeatAt[i] = Time.time;
        }
    }

    /// <summary>
    /// F104 B3: queues the hot hiding spots within hotSpotSearchRadius of the last known position that the
    /// near-spot pass did not already queue, nearest first, starting at queue index `insertAt`.
    /// </summary>
    private void InsertHotSpots(int insertAt)
    {
        if (!_terror.sanctuaryDecay) return;

        _hotSpotBuffer.Clear();
        for (int i = 0; i < _hidingSpots.Count; i++)
        {
            Vector3 spot = _hidingSpots[i];
            if (HeatOf(i) < hotThreshold || Vector3.Distance(spot, _lastKnownPosition) > hotSpotSearchRadius) continue;
            if (!_searchQueue.Contains(spot)) _hotSpotBuffer.Add(spot);
        }

        _hotSpotBuffer.Sort((a, b) => Vector3.Distance(a, _lastKnownPosition).CompareTo(Vector3.Distance(b, _lastKnownPosition)));
        _searchQueue.InsertRange(insertAt, _hotSpotBuffer);
    }

    /// <summary>F104 B3: called when a search dwell begins. At a hot hiding spot it stands outside for longer.</summary>
    private float BeginHotDwell()
    {
        _hotDwellSpot = -1;
        if (!_terror.sanctuaryDecay || _searchQueue.Count == 0) return 0f;

        int spot = _hidingSpots.IndexOf(_searchQueue[0]);
        if (spot < 0 || HeatOf(spot) < hotThreshold) return 0f;

        _hotDwellSpot = spot;
        _terror.Log("Stands outside locker", this);
        return StandOutsideSeconds;
    }

    /// <summary>
    /// F104 B3: the stand-outside dwell has ended at hot spot `spot`. If the player is hidden in that very
    /// locker, and the dice and the once-per-floor limit allow, it peeks.
    /// </summary>
    private bool TryPeek(int spot)
    {
        _hotDwellSpot = -1;
        if (_peeksThisFloor >= MaxPeeksPerFloor || _stealth == null || !_stealth.Hidden) return false;

        IHidingSpot hiding = _stealth.CurrentHidingSpot;
        if (hiding == null || Vector3.Distance(hiding.FrontPosition, _hidingSpots[spot]) > HideMatchRadius) return false;
        if (Random.value >= HeatOf(spot) * PeekChanceScale) return false;

        _peeksThisFloor++;
        hiding.Peek(peekSeconds);
        _presence?.RevealEyes(peekSeconds + PeekEyeLinger);
        _searchDwellTimer = peekSeconds;
        _terror.Log("Peeks into locker", this);
        return true;
    }

    // ---------------------------------------------------------------- F45: the ambush

    /// <summary>
    /// Works out which live star the player will reach first (shortest NavMesh path from the player,
    /// among stars not on cooldown that have at least one ambush cell), then heads for the ambush cell
    /// nearest the hunter itself - never one the player is standing in or right next to.
    /// </summary>
    private bool TryEnterAmbush()
    {
        if (IsBackingOff) return false;

        int bestStarIndex = -1;
        float bestStarLength = float.MaxValue;
        Vector3 bestStarPosition = default;

        for (int i = 0; i < _patrolTargets.Count; i++)
        {
            Transform star = _patrolTargets[i];
            if (star == null) continue;
            if (_ambushCooldownUntil.TryGetValue(i, out float until) && until > Time.time) continue;
            if (_maze.AmbushCellsFor(i).Count == 0) continue;
            if (!TryPathLength(target.position, star.position, out float length)) continue;

            if (length < bestStarLength)
            {
                bestStarLength = length;
                bestStarIndex = i;
                bestStarPosition = star.position;
            }
        }

        if (bestStarIndex < 0) return false;

        IReadOnlyList<Vector3> cells = _maze.AmbushCellsFor(bestStarIndex);
        Vector3 chosenCell = default;
        bool haveCell = false;
        float bestCellLength = float.MaxValue;

        for (int i = 0; i < cells.Count; i++)
        {
            Vector3 cell = cells[i];
            if (FlatDirection(cell - target.position).magnitude <= ambushPlayerExclusionRadius) continue;
            if (!TryPathLength(transform.position, cell, out float length)) continue;

            if (length < bestCellLength)
            {
                bestCellLength = length;
                chosenCell = cell;
                haveCell = true;
            }
        }

        if (!haveCell) return false;

        _ambushStarIndex = bestStarIndex;
        _ambushStarPosition = bestStarPosition;
        _ambushArrived = false;
        _ambushWaitTimer = 0f;
        _state = State.Ambush;
        _agent.isStopped = false;
        _agent.stoppingDistance = wanderStoppingDistance;
        SetDestinationNear(chosenCell);
        AmbushStateChanged?.Invoke(true);
        return true;
    }

    private void TickAmbush()
    {
        if (!_ambushArrived)
        {
            _agent.isStopped = false;
            MoveSpeedTowards(moveSpeed);
            UpdateRotation(CurrentHeading());

            if (ReachedDestination())
            {
                _ambushArrived = true;
                _agent.isStopped = true;
                _agent.velocity = Vector3.zero;
                _presence?.Hold(EffectiveAmbushWait + 1f);
                _presence?.SetFaceMode(AIPresence.FaceMode.Lurking);
            }
            return;
        }

        _ambushWaitTimer += Time.deltaTime;
        UpdateRotation(FlatDirection(_ambushStarPosition - transform.position));

        if (_ambushWaitTimer >= EffectiveAmbushWait)
        {
            LeaveAmbush();
            _state = State.Wander;
            _wanderRepickCooldown = 0f;
        }
    }

    /// <summary>
    /// Ends the current ambush and starts its repeat cooldown. Idempotent (a no-op once already left).
    /// </summary>
    private void LeaveAmbush(bool endedOnPickup = false)
    {
        if (_ambushStarIndex < 0) return;

        _ambushCooldownUntil[_ambushStarIndex] = Time.time + ambushRepeatCooldown;
        _ambushStarIndex = -1;
        _ambushArrived = false;
        _agent.isStopped = false;
        _presence?.SetFaceMode(AIPresence.FaceMode.Normal);
        _presence?.ReleaseHold();
        LastAmbushEndedOnPickup = endedOnPickup;
        LastAmbushEndTime = Time.time;
        AmbushStateChanged?.Invoke(false);
    }

    /// <summary>
    /// F45: if the hunter is lying in wait for the star at `at`, the ambush ends right here -
    /// unconditionally, not gated by hearing.
    /// </summary>
    private bool TryEndAmbushForStar(Vector3 at)
    {
        if (_state != State.Ambush) return false;
        if (Vector3.Distance(at, _ambushStarPosition) > ambushStarMatchRadius) return false;

        _lastKnownPosition = at;
        LeaveAmbush(true);
        EnterSearch();
        return true;
    }

    private void HandleStarTakenAt(Vector3 at)
    {
        TryEndAmbushForStar(at);
    }
}
