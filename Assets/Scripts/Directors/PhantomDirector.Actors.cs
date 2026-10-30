using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Random = UnityEngine.Random;

public partial class PhantomDirector
{
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
        if (TrySpawnDouble(position)) return;

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

    // ---------------------------------------------------------------- F105 C8: your double

    /// <summary>
    /// F105 C8: from doubleFromFloor on, a silhouette is sometimes the player's own robot instead - frozen in its idle pose, facing them,
    /// lit by a small spot at its head, and gone the moment the beam reaches it. False (the plain silhouette goes ahead) when the
    /// flag is off, the roll fails, Stan is close on a Stalker floor, or the figure cannot be built.
    /// </summary>
    private bool TrySpawnDouble(Vector3 position)
    {
        if (!_terror.doubles || GameFlow.CurrentFloor < doubleFromFloor || _camera == null || Random.value >= doubleChance) return false;
        if (StalkerNear(position)) return false;

        PhantomBody body = TryGetDoubleBody();
        if (body == null) return false;

        Vector3 camPos = _camera.transform.position;
        Vector3 facing = camPos - position;
        facing.y = 0f;

        body.Show(position, facing);
        body.Freeze(AIFollower.Pose.Idle);
        body.AimSpot(camPos);
        StartCoroutine(DoubleRoutine(body, position + Vector3.up * figureTestHeight));
        _terror.Log("Double phantom", this);
        return true;
    }

    /// <summary>True while the floor's Stalker is out and within doubleStalkerExclusion of the spot or of the player.</summary>
    private bool StalkerNear(Vector3 position)
    {
        Stalker stalker = _maze != null ? _maze.Stalker : null;
        if (stalker == null || !stalker.gameObject.activeInHierarchy || stalker.IsHiddenAfterTouch) return false;

        Vector3 stan = stalker.transform.position;
        return FlatDistance(stan, position) < doubleStalkerExclusion || FlatDistance(stan, _player.position) < doubleStalkerExclusion;
    }

    private static float FlatDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y;
        return Vector3.Distance(a, b);
    }

    /// <summary>The double's figure, built on first use from the Stalker template with its head spot. Null (logged once) when the kit has no template, and while the double is already standing.</summary>
    private PhantomBody TryGetDoubleBody()
    {
        if (_doubleBody == null && _maze != null)
        {
            _doubleBody = PhantomBody.TryBuild(_maze.StalkerTemplate, _phantomsRoot.transform);
            _doubleBody?.AddHeadSpot(doubleSpotRange, doubleSpotIntensity, doubleSpotAngle, doubleSpotColor);
            if (_doubleBody == null && !_doubleMissingLogged)
            {
                _doubleMissingLogged = true;
                Debug.LogWarning("PhantomDirector: the InteractableKit has no Stalker template, so the double phantom is off.", this);
            }
        }

        return _doubleBody != null && !_doubleBody.InUse ? _doubleBody : null;
    }

    /// <summary>The double stands for its lifetime, and goes the instant the torch beam reaches it, a chase starts, or the run ends.</summary>
    private IEnumerator DoubleRoutine(PhantomBody body, Vector3 testPoint)
    {
        float lifetime = Random.Range(doubleLifetime.x, doubleLifetime.y);
        for (float t = 0f; t < lifetime; t += Time.deltaTime)
        {
            if (BailedOut() || _follower.IsChasing || IsInTorchBeam(testPoint)) break;

            yield return null;
        }

        body.Hide();
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

    /// <summary>A copy of `source` in near-black with no emission: the shape of a figure and nothing of its colour. F106: the loop corridor's Visitor wears it too, so it is internal.</summary>
    internal static Material BuildSilhouetteMaterial(Material source)
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

    // ---------------------------------------------------------------- F128: P.T.-style attached presence

    /// <summary>
    /// F128: tethers an unseen presence behind the player's back for 8-15 s. It walks in lockstep behind the player,
    /// lands a half-beat late step and neck breath when stopping, projects a real shadow forward when passing in front
    /// of a powered WallLamp, freezes for a split-second peripheral glimpse on a fast whip-turn, and triggers a non-lethal
    /// startle if the player backs into it or stays at critical dread too long.
    /// </summary>
    private bool TryStartAttachedPresence()
    {
        if (!_terror.attachedPresence || _attachedActive || _camera == null || _player == null || _maze == null) return false;
        if (_footstepClips == null || _footstepClips.Length == 0) return false;

        PhantomBody body = null;
        if (_terror.doubles && GameFlow.CurrentFloor >= doubleFromFloor && Random.value < doubleChance && !StalkerNear(_player.position))
        {
            body = TryGetDoubleBody();
        }
        if (body == null) body = TryGetBody();

        // Fall back to the legacy audio-only footsteps routine if the shared figure body is busy or unavailable.
        if (body == null) return TryStartFootsteps();

        GameObject holder = new GameObject("Phantom_AttachedPresence");
        holder.transform.SetParent(_phantomsRoot.transform, false);

        AudioSource source = holder.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = footstepMinDistance;
        source.maxDistance = footstepMaxDistance;
        source.pitch = footstepPitch;

        bool honest = RollHonestFootsteps();
        bool hunterPitched = honest || (_terror.contaminatedPhantoms && Random.value < hunterPitchedChance);

        Vector3 camForwardFlat = FlatForward(_camera.transform);
        Vector3 initialPos = _player.position - camForwardFlat * attachedDistanceBehind;
        body.Show(initialPos, camForwardFlat);
        body.SetShadowMode(ShadowCastingMode.ShadowsOnly);
        body.Freeze(AIFollower.Pose.Lurk);

        _attachedActive = true;
        StartCoroutine(AttachedPresenceRoutine(body, holder, source, hunterPitched, honest));
        _terror.Log("Attached presence tethered", this);
        return true;
    }

    private IEnumerator AttachedPresenceRoutine(PhantomBody body, GameObject holder, AudioSource source, bool hunterPitched, bool honest)
    {
        float duration = Random.Range(attachedDurationMin, attachedDurationMax);
        float elapsed = 0f;
        float distanceSinceStep = footstepStrideLength * 0.5f;
        Vector3 lastPlayerPos = _player.position;
        float prevYaw = _camera.transform.eulerAngles.y;
        float fastTurnAccum = 0f;
        float fastTurnSign = 0f;
        float backpedalDistance = 0f;
        float highDreadTimer = 0f;
        float timeSinceStopped = 0f;
        bool isWalking = false;
        bool lateStepPending = false;
        bool neckBreathPending = false;

        while (elapsed < duration)
        {
            if (BailedOut() || _camera == null || _player == null || _follower == null
                || _follower.IsChasing || _follower.FlatDistanceToTarget < 4.5f
                || (_stealth != null && _stealth.Hidden)
                || (_maze.Loop != null && _maze.Loop.IsInside))
            {
                CleanupAttachedPresence(body, holder);
                yield break;
            }

            float dt = Mathf.Max(Time.deltaTime, 1e-4f);
            Vector3 camForwardFlat = FlatForward(_camera.transform);
            Vector3 backDir = -camForwardFlat;

            // 1. Slow turn orbit vs. fast whip-turn peripheral glimpse
            float currentYaw = _camera.transform.eulerAngles.y;
            float dyaw = Mathf.DeltaAngle(prevYaw, currentYaw);
            prevYaw = currentYaw;
            float yawSpeed = Mathf.Abs(dyaw) / dt;

            if (yawSpeed >= whipTurnSpeedThreshold)
            {
                float sign = Mathf.Sign(dyaw);
                if (fastTurnSign != 0f && sign != fastTurnSign) fastTurnAccum = 0f;
                fastTurnSign = sign;
                fastTurnAccum += Mathf.Abs(dyaw);
            }
            else
            {
                fastTurnAccum = Mathf.Max(0f, fastTurnAccum - 90f * dt);
                if (fastTurnAccum <= 0f) fastTurnSign = 0f;
            }

            if (fastTurnAccum >= whipTurnMinAngle && _terror.peripheralPhantoms && _silhouetteMaterial != null)
            {
                yield return WhipGlimpseRoutine(body, holder, fastTurnSign, honest);
                yield break;
            }

            // 2. Player movement tracking: lockstep footsteps, half-beat late step, neck breath & back-pedal detection
            Vector3 playerPos = _player.position;
            Vector3 move = playerPos - lastPlayerPos;
            move.y = 0f;
            lastPlayerPos = playerPos;
            float moveDist = move.magnitude;
            bool movingNow = moveDist > 0.002f && (_stealth == null || _stealth.NoiseRadius > 0f);

            if (movingNow)
            {
                Vector3 moveDir = move / moveDist;
                float backDot = Vector3.Dot(moveDir, backDir);
                if (backDot > 0.35f)
                {
                    backpedalDistance += Vector3.Dot(move, backDir);
                }
                else
                {
                    backpedalDistance = Mathf.Max(0f, backpedalDistance - moveDist * 0.5f);
                }

                distanceSinceStep += moveDist;
                if (!isWalking)
                {
                    isWalking = true;
                    body.Show(body.Root.transform.position, camForwardFlat);
                    body.SetShadowMode(ShadowCastingMode.ShadowsOnly);
                    body.Walk();
                }

                timeSinceStopped = 0f;
                neckBreathPending = false;

                if (distanceSinceStep >= footstepStrideLength)
                {
                    distanceSinceStep = 0f;
                    lateStepPending = true;
                    if (holder != null)
                    {
                        Vector3 stepPos = _camera.transform.position + backDir * 1.6f;
                        stepPos.y = playerPos.y;
                        holder.transform.position = stepPos;
                    }
                    PlayFootstep(source, hunterPitched);
                }
            }
            else
            {
                timeSinceStopped += dt;
                if (isWalking)
                {
                    isWalking = false;
                    body.Freeze(AIFollower.Pose.Lurk);
                }

                if (lateStepPending && timeSinceStopped >= footstepLateStepDelay)
                {
                    lateStepPending = false;
                    neckBreathPending = true;
                    if (holder != null)
                    {
                        Vector3 stepPos = _camera.transform.position + backDir * 1.15f;
                        stepPos.y = playerPos.y;
                        holder.transform.position = stepPos;
                    }
                    PlayFootstep(source, hunterPitched);
                }

                if (neckBreathPending && timeSinceStopped >= footstepLateStepDelay + neckBreathDelay)
                {
                    neckBreathPending = false;
                    PlayNeckBreath(_camera.transform.position, camForwardFlat, _camera.transform.right);
                }
            }

            // 3. Hybrid escalation check: walking backward into it or sustaining critical dread
            if (_meter != null && _meter.IsActive && _meter.Value >= highDreadStartleThreshold)
            {
                highDreadTimer += dt;
            }
            else
            {
                highDreadTimer = Mathf.Max(0f, highDreadTimer - dt * 0.5f);
            }

            if (backpedalDistance >= backpedalBumpDistance || highDreadTimer >= highDreadStartleSeconds)
            {
                yield return TriggerAttachedStartle(body, holder, source, backpedalDistance >= backpedalBumpDistance);
                yield break;
            }

            // 4. Blind-spot positioning & rear WallLamp forward shadow projection
            Vector3 anchor = playerPos + backDir * attachedDistanceBehind;
            UpdateShadowLamp(playerPos, camForwardFlat, ref anchor);
            anchor.y = playerPos.y;

            body.Root.transform.SetPositionAndRotation(anchor, Quaternion.LookRotation(camForwardFlat, Vector3.up));
            body.SetShadowMode(ShadowCastingMode.ShadowsOnly);

            elapsed += dt;
            yield return null;
        }

        // Natural expiry: land any pending half-beat step before detaching
        if (lateStepPending && !BailedOut() && (_follower == null || !_follower.IsChasing))
        {
            float wait = Mathf.Max(0f, footstepLateStepDelay - timeSinceStopped);
            for (float t = 0f; t < wait; t += Time.deltaTime)
            {
                if (BailedOut() || (_follower != null && _follower.IsChasing))
                {
                    CleanupAttachedPresence(body, holder);
                    yield break;
                }
                yield return null;
            }

            if (_camera != null && _player != null && holder != null)
            {
                Vector3 backDir = -FlatForward(_camera.transform);
                Vector3 stepPos = _camera.transform.position + backDir * 1.15f;
                stepPos.y = _player.position.y;
                holder.transform.position = stepPos;
                PlayFootstep(source, hunterPitched);
            }
        }

        if (honest)
        {
            bool relocated = _dread != null && _dread.TryRelocateOnTrail(honestRelocateDistance);
            _terror.Log($"Honest attached presence -> relocated: {relocated}", this);
        }

        ClearShadowLamp();
        body.Hide();
        _attachedActive = false;

        yield return new WaitForSeconds(0.8f);
        if (holder != null) Destroy(holder);
    }

    private IEnumerator WhipGlimpseRoutine(PhantomBody body, GameObject holder, float turnSign, bool honest)
    {
        ClearShadowLamp();

        Vector3 playerPos = _player.position;
        Vector3 camForwardFlat = FlatForward(_camera.transform);
        Vector3 camRightFlat = Vector3.Cross(Vector3.up, camForwardFlat).normalized;
        Vector3 sideDir = camRightFlat * (turnSign >= 0f ? 1f : -1f);
        Vector3 glimpseDir = (-camForwardFlat * 0.55f + sideDir * 0.85f).normalized;
        Vector3 glimpsePos = FindClearOffset(playerPos, glimpseDir, whipGlimpseDistance);

        Vector3 facing = playerPos - glimpsePos;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-4f) facing = camForwardFlat;

        body.Show(glimpsePos, facing);
        if (_silhouetteMaterial != null) body.SetMaterial(_silhouetteMaterial);
        body.SetShadowMode(ShadowCastingMode.On);
        body.Freeze(AIFollower.Pose.Lurk);
        _terror.Log("Attached presence whip-turn glimpse", this);

        Vector3 testPoint = glimpsePos + Vector3.up * figureTestHeight;

        // Wait briefly for the whip-turn to bring the frozen figure into the viewport
        for (float wait = 0f; wait < 0.25f; wait += Time.deltaTime)
        {
            if (BailedOut() || _camera == null || (_follower != null && _follower.IsChasing) || IsInTorchBeam(testPoint)) break;
            Vector3 vp = _camera.WorldToViewportPoint(testPoint);
            if (vp.z > 0f && vp.x >= 0.03f && vp.x <= 0.97f) break;
            yield return null;
        }

        // Hold for a split-second peripheral/rear glimpse before vanishing cleanly
        for (float hold = 0f; hold < whipGlimpseDuration; hold += Time.deltaTime)
        {
            if (BailedOut() || (_follower != null && _follower.IsChasing) || IsInTorchBeam(testPoint)) break;
            yield return null;
        }

        body.Hide();
        _attachedActive = false;

        if (!BailedOut())
        {
            MaybeKillLampNear(glimpsePos);
            if (honest)
            {
                bool relocated = _dread != null && _dread.TryRelocateOnTrail(honestRelocateDistance);
                _terror.Log($"Honest attached presence -> relocated: {relocated}", this);
            }
        }

        if (holder != null) Destroy(holder);
    }

    private IEnumerator TriggerAttachedStartle(PhantomBody body, GameObject holder, AudioSource source, bool fromBackpedal)
    {
        ClearShadowLamp();

        Vector3 playerPos = _player.position;
        Vector3 camForwardFlat = FlatForward(_camera.transform);
        Vector3 camRightFlat = Vector3.Cross(Vector3.up, camForwardFlat).normalized;
        float side = Random.value < 0.5f ? -1f : 1f;
        Vector3 startleDir = (-camForwardFlat * 0.65f + camRightFlat * (side * 0.75f)).normalized;
        Vector3 startlePos = FindClearOffset(playerPos, startleDir, 0.95f);

        Vector3 facing = playerPos - startlePos;
        facing.y = 0f;
        if (facing.sqrMagnitude < 1e-4f) facing = camForwardFlat;

        body.Show(startlePos, facing);
        if (_silhouetteMaterial != null) body.SetMaterial(_silhouetteMaterial);
        body.SetShadowMode(ShadowCastingMode.On);
        body.Freeze(AIFollower.Pose.Lurk);

        if (holder != null) holder.transform.position = startlePos;
        PlayFootstep(source, true);
        PlayNeckBreath(_camera.transform.position, camForwardFlat, camRightFlat, 1.35f);

        _meter?.Spike();
        if (_flashlight != null && _flashlight.IsOn)
        {
            if (_terror.torchFailures) _flashlight.Blackout(startleBlackoutSeconds);
            else _flashlight.Stutter(1.1f);
        }

        _terror.Log(fromBackpedal ? "Attached presence startle (back-pedal bump)" : "Attached presence startle (high dread)", this);

        for (float t = 0f; t < 0.18f; t += Time.deltaTime)
        {
            if (BailedOut() || (_follower != null && _follower.IsChasing)) break;
            yield return null;
        }

        body.Hide();
        _attachedActive = false;

        yield return new WaitForSeconds(0.6f);
        if (holder != null) Destroy(holder);
    }

    /// <summary>
    /// F128: finds the nearest powered WallLamp behind the player within attachedLampShadowRadius, temporarily enables
    /// soft shadows on it, and biases the unseen body's anchor toward the line from the lamp to the floor ahead of the player.
    /// </summary>
    private void UpdateShadowLamp(Vector3 playerPos, Vector3 camForwardFlat, ref Vector3 anchor)
    {
        if (_maze == null || GameSettings.Shadows == GameSettings.ShadowTier.Off)
        {
            ClearShadowLamp();
            return;
        }

        IReadOnlyList<WallLamp> lamps = _maze.Lamps;
        WallLamp best = null;
        float bestDist = attachedLampShadowRadius;
        Vector3 playerChest = playerPos + Vector3.up * 1.2f;

        for (int i = 0; i < lamps.Count; i++)
        {
            WallLamp lamp = lamps[i];
            if (lamp == null || lamp.IsDead || !lamp.IsPowered || lamp.CurrentIntensity01 <= 0.05f) continue;

            Vector3 lampPos = lamp.transform.position;
            float dist = Vector3.Distance(lampPos, playerPos);
            if (dist > bestDist || dist < 0.8f) continue;

            Vector3 lampToPlayer = playerPos - lampPos;
            lampToPlayer.y = 0f;
            if (lampToPlayer.sqrMagnitude < 0.01f) continue;

            // Lamp must be behind the player so its light projects the shadow forward ahead of the player's feet
            if (Vector3.Dot(camForwardFlat, lampToPlayer.normalized) <= 0.15f) continue;
            if (!IsRayClear(lampPos, playerChest)) continue;

            bestDist = dist;
            best = lamp;
        }

        if (best != _shadowLamp)
        {
            if (_shadowLamp != null) _shadowLamp.SetShadowCasting(false);
            _shadowLamp = best;
            if (_shadowLamp != null) _shadowLamp.SetShadowCasting(true);
        }

        if (_shadowLamp != null)
        {
            Vector3 lampFlat = _shadowLamp.transform.position;
            lampFlat.y = playerPos.y;
            Vector3 forwardFloor = playerPos + camForwardFlat * attachedLampShadowForwardAim;
            Vector3 rayToAim = forwardFloor - lampFlat;
            rayToAim.y = 0f;
            float rayLen = rayToAim.magnitude;
            if (rayLen > 0.1f)
            {
                Vector3 rayDir = rayToAim / rayLen;
                float backFromAim = Mathf.Min(rayLen * 0.7f, attachedDistanceBehind + attachedLampShadowForwardAim);
                Vector3 shadowCasterPos = forwardFloor - rayDir * backFromAim;
                Vector3 targetAnchor = Vector3.Lerp(anchor, shadowCasterPos, attachedLampBias);
                Vector3 fromPlayer = targetAnchor - playerPos;
                fromPlayer.y = 0f;
                if (fromPlayer.sqrMagnitude > 0.01f)
                {
                    anchor = FindClearOffset(playerPos, fromPlayer.normalized, fromPlayer.magnitude);
                }
            }
        }
    }

    private void ClearShadowLamp()
    {
        if (_shadowLamp != null)
        {
            _shadowLamp.SetShadowCasting(false);
            _shadowLamp = null;
        }
    }

    private void CleanupAttachedPresence(PhantomBody body, GameObject holder)
    {
        ClearShadowLamp();
        body?.Hide();
        _attachedActive = false;
        if (holder != null) Destroy(holder);
    }

    private void PlayNeckBreath(Vector3 camPos, Vector3 camForwardFlat, Vector3 camRight, float volumeScale = 1f)
    {
        float earSide = Random.value < 0.5f ? -0.35f : 0.35f;
        Vector3 pos = camPos - camForwardFlat * neckBreathDistance + camRight * earSide;

        GameAudioLibrary library = GameAudio.Library;
        AudioSlot slot = library != null && library.lockerBreath.Has
            ? library.lockerBreath
            : (library != null ? library.phantomWhisper : null);

        if (slot != null && slot.Has)
        {
            AudioSource s = GameAudio.SpawnAt(slot, pos, neckBreathVolume * volumeScale, 0.8f, 4.5f);
            if (s != null && slot.lowPassHz <= 0f)
            {
                s.gameObject.AddComponent<AudioLowPassFilter>().cutoffFrequency = 1600f;
            }
            return;
        }

        _audio?.PlayWhisper(earSide < 0f ? -0.85f : 0.85f);
    }

    private Vector3 FindClearOffset(Vector3 origin, Vector3 flatDir, float desiredDistance)
    {
        Vector3 chestOrigin = origin + Vector3.up * 1.1f;
        int count = Physics.RaycastNonAlloc(chestOrigin, flatDir, _rayHits, desiredDistance + 0.35f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = desiredDistance;
        for (int i = 0; i < count; i++)
        {
            Transform hit = _rayHits[i].transform;
            if ((_player != null && IsPartOf(hit, _player)) || (_follower != null && IsPartOf(hit, _follower.transform))) continue;
            float safeDist = Mathf.Max(0.55f, _rayHits[i].distance - 0.35f);
            if (safeDist < nearest) nearest = safeDist;
        }

        Vector3 result = origin + flatDir * nearest;
        result.y = origin.y;
        return result;
    }

    private static Vector3 FlatForward(Transform t)
    {
        Vector3 fwd = t.forward;
        fwd.y = 0f;
        if (fwd.sqrMagnitude < 1e-4f) fwd = t.right;
        fwd.y = 0f;
        return fwd.sqrMagnitude > 1e-4f ? fwd.normalized : Vector3.forward;
    }
}
