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
}
