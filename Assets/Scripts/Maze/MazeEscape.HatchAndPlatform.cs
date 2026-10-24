using System.Collections;
using UnityEngine;

public partial class MazeEscape
{
    private void SetLidAngle(float degrees, float seconds)
    {
        if (_lidL == null && _lidR == null) return;
        if (_lidRoutine != null) StopCoroutine(_lidRoutine);
        _lidRoutine = StartCoroutine(SwingLid(degrees, seconds));
    }

    private IEnumerator SwingLid(float target, float seconds)
    {
        float start = _lidAngle;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            _lidAngle = Mathf.SmoothStep(start, target, Mathf.Clamp01(t / seconds));
            ApplyLidAngle();
            yield return null;
        }
        _lidAngle = target;
        ApplyLidAngle();
        _lidRoutine = null;
    }

    /// <summary>Each leaf lies flat at 0 and lifts its free (centre) edge about its hinge's local Z.</summary>
    private void ApplyLidAngle()
    {
        if (_lidL != null) _lidL.localRotation = Quaternion.Euler(0f, 0f, _lidAngle);
        if (_lidR != null) _lidR.localRotation = Quaternion.Euler(0f, 0f, -_lidAngle);
    }

    /// <summary>
    /// F104 B4: a hatch the player can reach inside the window by walking path (not straight line) and that is not on top of the
    /// hunter. Tries the window as configured, then once widened; false means the caller falls back to its old rule.
    /// </summary>
    private bool TryChooseHuntedHatchCell(out Vector3 chosen)
    {
        chosen = default;
        int[,] steps = _maze.StepsFrom(_player.position);
        if (steps == null) return false;

        for (int pass = 0; pass < 2; pass++)
        {
            float min = pass == 0 ? hatchPathMetres.x : hatchPathMetres.x * hatchWindowWiden;
            float max = pass == 0 ? hatchPathMetres.y : hatchPathMetres.y / hatchWindowWiden;

            _hatchCandidates.Clear();
            foreach (Vector3 cell in _maze.CellCenters)
            {
                if (!_maze.TryWorldToCell(cell, out Vector2Int index) || steps[index.x, index.y] < 0) continue;

                float path = steps[index.x, index.y] * _maze.CellSize;
                if (path < min || path > max) continue;
                if (_hunter != null && FlatDistance(cell, _hunter.transform.position) < hatchMinHunterDistance) continue;
                if (HasCeilingPropAt(cell)) continue; // F107 A

                _hatchCandidates.Add(cell);
            }

            if (_hatchCandidates.Count == 0) continue;

            chosen = _hatchCandidates[Random.Range(0, _hatchCandidates.Count)];
            _maze.TryWorldToCell(chosen, out Vector2Int picked);
            float hunterDistance = _hunter != null ? FlatDistance(chosen, _hunter.transform.position) : -1f;
            _terror.Log($"Hatch at {steps[picked.x, picked.y]} steps ({steps[picked.x, picked.y] * _maze.CellSize:0} m), hunter {hunterDistance:0} m", this);
            return true;
        }

        return false;
    }

    /// <summary>F107 A: the player is eased into the hatch looking down, so a hanging body over it is wrong.</summary>
    private bool HasCeilingPropAt(Vector3 cellCenter) => _maze.TryWorldToCell(cellCenter, out Vector2Int index) && _maze.HasCeilingProp(index);

    // ---------------------------------------------------------------- placement

    private Vector3 ChooseHatchCell()
    {
        // Debug (MazeGenerator.superEasyMode): the stars are packed round the start, so the hatch opens in the start cell itself -
        // a step or two from wherever the last star was (never in a star cell, so never underfoot).
        if (_maze.SuperEasyMode) return _maze.CellCenter(0, 0);

        // F104 B4: a hatch a walk of 25-40 m away that is not next to the hunter (falls through to the old rule when nothing fits).
        if (_terror.escapeHunt && TryChooseHuntedHatchCell(out Vector3 hunted)) return hunted;

        var cells = _maze.CellCenters;
        var candidates = new System.Collections.Generic.List<Vector3>();

        foreach (Vector3 cell in cells)
        {
            if (Vector3.Distance(cell, _player.position) >= minimumDistanceFromPlayer && !HasCeilingPropAt(cell)) // F107 A
            {
                candidates.Add(cell);
            }
        }

        // A very small maze may have nowhere far enough; anywhere but underfoot will do then
        if (candidates.Count == 0)
        {
            return cells[Random.Range(0, cells.Count)];
        }

        return candidates[Random.Range(0, candidates.Count)];
    }

    private void BuildHatch(Vector3 position)
    {
        GameObject root = new GameObject("EscapeHatch");
        root.transform.position = position;

        // A glowing panel set into the floor. Emissive geometry rather than light alone, so it is
        // visible from down a corridor rather than only lighting the wall next to it.
        // F80: the authored trapdoor (InteractableKit.escapeHatch) when there is one - its 'Glow' slab is
        // that same emissive panel, showing round and between the two wooden leaves.
        InteractableKit kit = FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        GameObject template = kit != null ? kit.EscapeHatch : null;
        GameObject panel = null;
        if (template != null)
        {
            GameObject model = Instantiate(template, root.transform);
            model.name = "Trapdoor";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.SetActive(true);

            Transform glow = model.transform.Find("Glow");
            if (glow != null && glow.GetComponent<MeshRenderer>() != null) panel = glow.gameObject;
            _lidL = model.transform.Find("LidL");
            _lidR = model.transform.Find("LidR");
        }

        if (panel == null)
        {
            panel = GameObject.CreatePrimitive(PrimitiveType.Cube);
            panel.name = "HatchPanel";
            Destroy(panel.GetComponent<Collider>());
            panel.transform.SetParent(root.transform, false);
            panel.transform.localPosition = new Vector3(0f, 0.03f, 0f);
            panel.transform.localScale = new Vector3(1.8f, 0.06f, 1.8f);
        }

        if (_emissiveSource != null)
        {
            Material material = new Material(_emissiveSource) { name = "HatchGlow" };
            material.EnableKeyword("_EMISSION");
            material.SetColor("_BaseColor", hatchColor);
            material.SetColor("_EmissionColor", hatchColor * 3f);
            panel.GetComponent<MeshRenderer>().sharedMaterial = material;
            _exitGlow = material;
        }

        // Shadows off: the flashlight is the only shadow caster, so the atlas stays undivided.
        GameObject lightHolder = new GameObject("HatchLight");
        lightHolder.transform.SetParent(root.transform, false);
        lightHolder.transform.localPosition = new Vector3(0f, 1.5f, 0f);

        Light light = lightHolder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = hatchColor;
        light.range = hatchLightRange;
        light.intensity = hatchLightIntensity;
        light.shadows = LightShadows.None;
        _exitLight = light;

        BuildPingSource(position);
    }

    /// <summary>F77: the ping's AudioSource lives on its own unparented GameObject (not the hatch light holder), so UpdatePing can move it onto the current wayfinding target (the hatch, or the nearest missing key while locked) right before each PlayOneShot.</summary>
    private void BuildPingSource(Vector3 position)
    {
        GameObject pingGo = new GameObject("EscapePing");
        pingGo.transform.position = position;
        _pingAnchor = pingGo.transform;

        _pingClip = BuildPingClip();
        _ping = pingGo.AddComponent<AudioSource>();
        _ping.playOnAwake = false;
        _ping.loop = false;
        _ping.spatialBlend = 1f;
        _ping.rolloffMode = AudioRolloffMode.Linear;
        _ping.minDistance = 3f;
        _ping.maxDistance = pingMaxDistance;
        _ping.dopplerLevel = 0f;
        _pingTimer = pingInterval;
    }

    /// <summary>
    /// A short descending tone, so the player can tell "getting warmer" from panning and pitch alone.
    /// Built from scratch, the same way HorrorAudioDirector builds its heartbeat.
    /// </summary>
    private static AudioClip BuildPingClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.35f;
        const float decay = 0.12f;
        const float freqStart = 880f;
        const float freqEnd = 660f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float sweepPerSecond = (freqEnd - freqStart) / duration;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            // Integrated frequency (a proper linear chirp), or the phase jumps between samples
            float phase = 2f * Mathf.PI * (freqStart * t + 0.5f * sweepPerSecond * t * t);

            float envelope = Mathf.Exp(-t / decay);
            envelope *= Mathf.Clamp01(t / 0.003f); // 3 ms fade-in kills the attack click

            samples[i] = Mathf.Sin(phase) * envelope;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.8f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("HatchPing", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private void BuildLandingPlatform(Vector3 hatchPosition)
    {
        GameObject platform = GameObject.CreatePrimitive(PrimitiveType.Cube);
        platform.name = "EscapePlatform";
        platform.transform.position = new Vector3(
            hatchPosition.x,
            hatchPosition.y - dropDepth - 0.1f,
            hatchPosition.z);
        platform.transform.localScale = new Vector3(platformSize, 0.2f, platformSize);

        // Unparented: the platform is scaled 8x, and a child would inherit that
        GameObject lightHolder = new GameObject("PlatformLight");
        lightHolder.transform.position = platform.transform.position + Vector3.up * 3f;

        Light light = lightHolder.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = hatchColor;
        light.range = 14f;
        light.intensity = 4f;
        light.shadows = LightShadows.None;
    }
}
