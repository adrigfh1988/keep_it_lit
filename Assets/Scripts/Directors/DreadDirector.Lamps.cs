using System.Collections.Generic;
using UnityEngine;

public partial class DreadDirector
{
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
        if (!_terror.torchFailures || BailedOut() || _flashlight == null || _player == null || _follower == null || _follower.IsDormantForSoloEntity) return false;
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
