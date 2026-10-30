using System.Collections;
using UnityEngine;

/// <summary>
/// Animates the skeletal bones of a rigged bathtub-with-bodies set piece (Bathtub_Bodies_HP) or hanging body
/// (Deadbody_Hanging_HP) when the player approaches or shines their flashlight on it, plus subtle eerie breathing/twitching.
/// Attached automatically by FloorThemeBuilder.Pack.cs onto Pack_BloodyBath and Pack_HangingBody variants.
/// </summary>
public class HorrorPropAnimator : MonoBehaviour
{
    public enum PropKind { HangingBody, BathtubBodies }

    [SerializeField] private PropKind kind;
    [SerializeField] private Transform torsoBone;
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform secondaryBone;
    [SerializeField] private Transform armBone;

    [Header("Scare & Twitch Settings")]
    [SerializeField] private float scareTriggerRadius = 2.6f;
    [SerializeField] private float idleTwitchIntervalMin = 7f;
    [SerializeField] private float idleTwitchIntervalMax = 16f;

    private Transform _player;
    private Flashlight _torch;
    private Quaternion _torsoRest;
    private Quaternion _headRest;
    private Quaternion _secondaryRest;
    private Quaternion _armRest;
    private bool _scared;
    private bool _animating;
    private float _nextTwitchTime;
    private float _breathSeed;
    private AudioSource _activeSource;
    private AudioClip _fallbackCreakClip;
    private readonly float[] _audioSamples = new float[256];

    public void BindBones(PropKind propKind, Transform torso, Transform head, Transform secondary, Transform arm)
    {
        kind = propKind;
        torsoBone = torso;
        headBone = head;
        secondaryBone = secondary;
        armBone = arm;
    }

    private void Awake()
    {
        CacheBonesIfNeeded();
        if (torsoBone != null) _torsoRest = torsoBone.localRotation;
        if (headBone != null) _headRest = headBone.localRotation;
        if (secondaryBone != null) _secondaryRest = secondaryBone.localRotation;
        if (armBone != null) _armRest = armBone.localRotation;
        _breathSeed = Random.Range(0f, 100f);
        _nextTwitchTime = Time.time + Random.Range(idleTwitchIntervalMin, idleTwitchIntervalMax);
        PreloadPropAudio();
    }

    private void OnDisable()
    {
        _animating = false;
        if (_activeSource != null)
        {
            Destroy(_activeSource.gameObject);
            _activeSource = null;
        }
        ResetBonesToRest();
    }

    private void OnDestroy()
    {
        if (_fallbackCreakClip != null)
        {
            Destroy(_fallbackCreakClip);
            _fallbackCreakClip = null;
        }
    }

    /// <summary>
    /// Preloads the prop's audio clips into memory on Awake so background loading (loadInBackground on >3s clips)
    /// never delays the sound relative to the bone movement.
    /// </summary>
    private void PreloadPropAudio()
    {
        if (GameAudio.Library == null) return;
        AudioSlot slot = kind == PropKind.BathtubBodies ? GameAudio.Library.farDrag : GameAudio.Library.floorCreak;
        if (slot == null || slot.clips == null) return;
        for (int i = 0; i < slot.clips.Length; i++)
        {
            AudioClip clip = slot.clips[i];
            if (clip != null && clip.loadState == AudioDataLoadState.Unloaded)
                clip.LoadAudioData();
        }
    }

    private void ResetBonesToRest()
    {
        if (torsoBone != null) torsoBone.localRotation = _torsoRest;
        if (headBone != null) headBone.localRotation = _headRest;
        if (secondaryBone != null) secondaryBone.localRotation = _secondaryRest;
        if (armBone != null) armBone.localRotation = _armRest;
    }

    private void CacheBonesIfNeeded()
    {
        if (torsoBone != null || headBone != null) return;
        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            switch (t.name)
            {
                case "Torso":
                    kind = PropKind.HangingBody;
                    torsoBone = t;
                    break;
                case "Head":
                    headBone = t;
                    break;
                case "Legs":
                    secondaryBone = t;
                    break;
                case "SeatedBody_Torso":
                    kind = PropKind.BathtubBodies;
                    torsoBone = t;
                    break;
                case "SeatedBody_Head":
                    headBone = t;
                    break;
                case "SlumpedBody_Head":
                    secondaryBone = t;
                    break;
                case "SlumpedBody_Arm":
                    armBone = t;
                    break;
            }
        }
    }

    private void Update()
    {
        if ((!GameFlow.IsRunActive && !GameFlow.IsInHub && !GameFlow.IsInIntake) || GameOutcome.IsOver || Time.timeScale <= 0f) return;
        if (torsoBone == null && headBone == null) return;

        if (_player == null)
        {
            GameObject pObj = GameObject.FindGameObjectWithTag("Player");
            if (pObj != null)
            {
                _player = pObj.transform;
                _torch = pObj.GetComponentInChildren<Flashlight>(true);
            }
            if (_player == null) return;
        }

        if (_animating) return;

        Vector3 toPlayer = _player.position - GetSoundPosition();
        toPlayer.y = 0f;
        float dist = toPlayer.magnitude;

        // Sudden close-range scare lurch / convulsion once per run when the player approaches!
        if (!_scared && dist <= scareTriggerRadius)
        {
            _scared = true;
            _nextTwitchTime = Time.time + Random.Range(idleTwitchIntervalMin, idleTwitchIntervalMax);
            StartCoroutine(ScareLurchRoutine());
            return;
        }

        // Occasional creak-synced sway / twitch while the player is nearby
        if (Time.time >= _nextTwitchTime && dist <= 11f)
        {
            _nextTwitchTime = Time.time + Random.Range(idleTwitchIntervalMin, idleTwitchIntervalMax);
            StartCoroutine(TwitchRoutine());
            return;
        }

        // Hanging bodies stay still while silent so they never move without their creak sound;
        // bathtub bodies keep a barely perceptible uneasy breathing micro-motion.
        if (kind == PropKind.BathtubBodies)
        {
            float t = Time.time * 1.4f + _breathSeed;
            float s1 = Mathf.Sin(t) * 0.6f;
            float s2 = Mathf.Cos(t * 0.75f) * 0.8f;
            if (torsoBone != null) torsoBone.localRotation = _torsoRest * Quaternion.Euler(s1, 0f, s1 * 0.5f);
            if (headBone != null) headBone.localRotation = _headRest * Quaternion.Euler(s2, s1 * 0.8f, 0f);
            if (armBone != null) armBone.localRotation = _armRest * Quaternion.Euler(s1 * 0.9f, 0f, 0f);
        }
    }

    private Vector3 GetSoundPosition()
    {
        if (torsoBone != null) return torsoBone.position;
        if (headBone != null) return headBone.position;
        return kind == PropKind.HangingBody
            ? transform.position + Vector3.down * 1.4f
            : transform.position + Vector3.up * 0.9f;
    }

    private AudioSource SpawnSyncedAudio(bool isScareLurch, out float duration, out float fadeOut)
    {
        duration = isScareLurch
            ? Random.Range(1.85f, 2.35f)
            : Random.Range(0.78f, 1.15f);
        fadeOut = isScareLurch ? 0.38f : 0.28f;

        float volume = isScareLurch ? 0.78f : 0.42f;
        float pitch = isScareLurch ? Random.Range(0.90f, 1.06f) : Random.Range(0.92f, 1.08f);
        float maxDist = isScareLurch ? 11f : 8.5f;
        Vector3 soundPos = GetSoundPosition();

        AudioSource src = null;
        if (GameAudio.Library != null)
        {
            AudioSlot slot = kind == PropKind.BathtubBodies ? GameAudio.Library.farDrag : GameAudio.Library.floorCreak;
            src = GameAudio.SpawnAt(slot, soundPos, volume, pitch, maxDist);
        }

        if (src != null && src.clip != null)
        {
            // Skip the ~0.26s silent lead-in of the recorded creak/drag files and jump straight to an active creak phrase
            float clipLen = src.clip.length;
            float playWindow = duration * Mathf.Max(0.5f, src.pitch);
            float startOffset = PickActiveCreakOffset(src.clip.name, isScareLurch, clipLen, playWindow);
            if (startOffset > 0f && startOffset < clipLen - 0.15f)
                src.time = startOffset;

            OneShotFade fade = src.GetComponent<OneShotFade>();
            if (fade != null) fade.Begin(src, duration, fadeOut);
            return src;
        }

        if (kind == PropKind.HangingBody)
        {
            if (_fallbackCreakClip != null) Destroy(_fallbackCreakClip);
            _fallbackCreakClip = DoorAudio.BuildCreakClip(duration, Random.Range(145f, 195f), Random.Range(1, 10000));
            GameObject go = new GameObject("OneShot_HangingCreak");
            go.transform.position = soundPos;
            src = go.AddComponent<AudioSource>();
            src.clip = _fallbackCreakClip;
            src.playOnAwake = false;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 1.5f;
            src.maxDistance = maxDist;
            src.volume = volume;
            src.pitch = pitch;
            GameSettings.RouteEffect(src);
            src.Play();
            go.AddComponent<OneShotFade>().Begin(src, duration, fadeOut);
        }

        return src;
    }

    private static float PickActiveCreakOffset(string clipName, bool isScareLurch, float clipLen, float neededSeconds)
    {
        if (!string.IsNullOrEmpty(clipName))
        {
            if (clipName.IndexOf("woodenbed_creaks_04", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Measured active creak phrase onsets in ucns_woodenbed_creaks_04.wav (0.0-0.26s is dead air)
                float[] scareOnsets = { 0.28f, 2.46f, 4.62f };
                float[] twitchOnsets = { 0.28f, 0.86f, 1.76f, 2.46f, 4.62f };
                float[] pool = isScareLurch ? scareOnsets : twitchOnsets;
                float pick = pool[Random.Range(0, pool.Length)];
                if (pick + neededSeconds <= clipLen - 0.05f) return pick;
                return 0.28f;
            }

            if (clipName.IndexOf("woodencupboard_01_squeaks_07", System.StringComparison.OrdinalIgnoreCase) >= 0)
            {
                // Measured active creak phrase onsets in ucns_woodencupboard_01_squeaks_07.wav (0.0-0.26s is dead air)
                float[] scareOnsets = { 0.28f, 1.32f };
                float[] twitchOnsets = { 0.28f, 1.32f, 2.35f };
                float[] pool = isScareLurch ? scareOnsets : twitchOnsets;
                float pick = pool[Random.Range(0, pool.Length)];
                if (pick + neededSeconds <= clipLen - 0.05f) return pick;
                return 0.28f;
            }
        }

        // Default: skip 0.25s of leading silence if the clip is long enough
        return clipLen > neededSeconds + 0.35f ? 0.25f : 0f;
    }

    private float SampleLiveAudioIntensity(AudioSource src, float baseVolume, float normalizedTime, float remainingSeconds, float fadeOut)
    {
        float fadeWindow = fadeOut > 0.01f ? Mathf.Clamp01(remainingSeconds / fadeOut) : 1f;
        float attackWindow = Mathf.Clamp01(normalizedTime / 0.06f);
        float fallback = Mathf.Sin(Mathf.PI * Mathf.Clamp01(normalizedTime)) * (0.65f + 0.35f * Mathf.Sin(normalizedTime * 18f));
        fallback = Mathf.Clamp01(fallback) * attackWindow * fadeWindow;

        if (src == null || !src.isPlaying) return 0f;

        src.GetOutputData(_audioSamples, 0);
        float sumSq = 0f;
        for (int i = 0; i < _audioSamples.Length; i++)
        {
            float s = _audioSamples[i];
            sumSq += s * s;
        }
        float rms = Mathf.Sqrt(sumSq / _audioSamples.Length);
        float unattenuatedRms = rms / Mathf.Max(0.08f, baseVolume);

        // Recorded creaks sit around 0.015 - 0.065 RMS; map into [0..1] so pauses in the clip drop toward 0 and peaks drive full strain
        float live = Mathf.Clamp01((unattenuatedRms - 0.003f) / 0.036f) * fadeWindow;
        return rms > 0.0001f ? live : fallback;
    }

    private IEnumerator ScareLurchRoutine()
    {
        return RunAudioSyncedMotion(isScareLurch: true);
    }

    private IEnumerator TwitchRoutine()
    {
        return RunAudioSyncedMotion(isScareLurch: false);
    }

    private IEnumerator RunAudioSyncedMotion(bool isScareLurch)
    {
        _animating = true;
        _activeSource = SpawnSyncedAudio(isScareLurch, out float duration, out float fadeOut);
        float baseVolume = _activeSource != null ? Mathf.Max(0.08f, _activeSource.volume) : 0.5f;

        float ampScale = isScareLurch ? 1f : 0.45f;
        float dirPitch = Random.value < 0.5f ? -1f : 1f;
        float dirRoll = Random.value < 0.5f ? -1f : 1f;
        float dirYaw = Random.value < 0.5f ? -1f : 1f;
        float headDirY = Random.value < 0.5f ? -1f : 1f;
        float headDirZ = Random.value < 0.5f ? -1f : 1f;

        float swingPhase = 0f;
        float smoothedEnv = 0f;
        float prevRawEnv = 0f;
        float slipImpulse = isScareLurch ? 0.65f : 0.3f;

        for (float el = 0f; el < duration; el += Time.deltaTime)
        {
            if (_activeSource != null && !_activeSource.isPlaying && el > 0.15f)
                break;

            if (_activeSource != null)
                _activeSource.transform.position = GetSoundPosition();

            float norm = Mathf.Clamp01(el / Mathf.Max(0.05f, duration));
            float remaining = Mathf.Max(0f, duration - el);
            float rawEnv = SampleLiveAudioIntensity(_activeSource, baseVolume, norm, remaining, fadeOut);

            // Rising edges in the creak audio trigger stick-slip impulses on the rope/bones
            float risingEdge = Mathf.Max(0f, rawEnv - prevRawEnv);
            prevRawEnv = rawEnv;
            slipImpulse = Mathf.MoveTowards(slipImpulse, 0f, Time.deltaTime * 4.5f);
            if (risingEdge > 0.08f)
                slipImpulse = Mathf.Max(slipImpulse, Mathf.Clamp01(risingEdge * 2.8f));

            float rate = rawEnv > smoothedEnv ? 22f : 7.5f;
            smoothedEnv = Mathf.MoveTowards(smoothedEnv, rawEnv, Time.deltaTime * rate);

            float fadeFactor = fadeOut > 0.01f ? Mathf.Clamp01(remaining / fadeOut) : 1f;
            float motionEnv = Mathf.Clamp01(smoothedEnv * 0.85f + slipImpulse * 0.45f) * fadeFactor;

            // Advance pendulum phase faster while the rope is actively creaking, hesitating during silent gaps in the clip
            swingPhase += Time.deltaTime * (2.1f + motionEnv * 4.8f);

            float sTorso = Mathf.Sin(swingPhase);
            float cTorso = Mathf.Cos(swingPhase * 0.75f);
            float sHead = Mathf.Sin(swingPhase - 0.55f);
            float sLegs = Mathf.Sin(swingPhase - 0.9f);

            if (kind == PropKind.HangingBody)
            {
                // Pendulum swing + stick-slip strain on the rope
                float tPitch = (sTorso * 12f + motionEnv * 7f) * dirPitch * ampScale;
                float tYaw = (cTorso * 15f + slipImpulse * 9f) * dirYaw * ampScale;
                float tRoll = (Mathf.Sin(swingPhase * 1.15f) * 11f + motionEnv * 5f) * dirRoll * ampScale;

                float hPitch = (sHead * 16f - motionEnv * 11f) * dirPitch * ampScale;
                float hYaw = (Mathf.Sin(swingPhase * 1.4f + 0.4f) * 18f + slipImpulse * 12f) * headDirY * ampScale;
                float hRoll = (motionEnv * 14f + sHead * 9f) * headDirZ * ampScale;

                // Legs lag behind the torso swing like a hanging double-pendulum
                float lPitch = (sLegs * 14f - slipImpulse * 8f) * dirPitch * ampScale;
                float lYaw = (sLegs * 8f) * dirYaw * ampScale;
                float lRoll = (Mathf.Cos(swingPhase - 0.8f) * 10f) * dirRoll * ampScale;

                if (torsoBone != null) torsoBone.localRotation = _torsoRest * Quaternion.Euler(tPitch, tYaw, tRoll);
                if (headBone != null) headBone.localRotation = _headRest * Quaternion.Euler(hPitch, hYaw, hRoll);
                if (secondaryBone != null) secondaryBone.localRotation = _secondaryRest * Quaternion.Euler(lPitch, lYaw, lRoll);
            }
            else
            {
                // BathtubBodies: convulsive shift synced to the drag sound
                float tPitch = (sTorso * 14f + motionEnv * 8f) * dirPitch * ampScale;
                float tYaw = (cTorso * 12f) * dirYaw * ampScale;
                float tRoll = (sTorso * 12f) * dirRoll * ampScale;

                float hPitch = (sHead * 20f + slipImpulse * 10f) * dirPitch * ampScale;
                float hYaw = (Mathf.Sin(swingPhase * 1.5f) * 22f) * headDirY * ampScale;
                float hRoll = (motionEnv * 15f) * headDirZ * ampScale;

                float aPitch = -(motionEnv * 20f + Mathf.Abs(sTorso) * 10f) * ampScale;
                float aRoll = (sLegs * 12f) * dirRoll * ampScale;

                if (torsoBone != null) torsoBone.localRotation = _torsoRest * Quaternion.Euler(tPitch, tYaw, tRoll);
                if (headBone != null) headBone.localRotation = _headRest * Quaternion.Euler(hPitch, hYaw, hRoll);
                if (secondaryBone != null) secondaryBone.localRotation = _secondaryRest * Quaternion.Euler(hPitch * 0.7f, -hYaw * 0.6f, hRoll * 0.7f);
                if (armBone != null) armBone.localRotation = _armRest * Quaternion.Euler(aPitch, 0f, aRoll);
            }

            yield return null;
        }

        // Smoothly settle any remaining angle back to exact rest over 0.18s
        Quaternion tf = torsoBone != null ? torsoBone.localRotation : Quaternion.identity;
        Quaternion hf = headBone != null ? headBone.localRotation : Quaternion.identity;
        Quaternion sf = secondaryBone != null ? secondaryBone.localRotation : Quaternion.identity;
        Quaternion af = armBone != null ? armBone.localRotation : Quaternion.identity;
        for (float el = 0f; el < 0.18f; el += Time.deltaTime)
        {
            float k = el / 0.18f;
            if (torsoBone != null) torsoBone.localRotation = Quaternion.Slerp(tf, _torsoRest, k);
            if (headBone != null) headBone.localRotation = Quaternion.Slerp(hf, _headRest, k);
            if (secondaryBone != null) secondaryBone.localRotation = Quaternion.Slerp(sf, _secondaryRest, k);
            if (armBone != null) armBone.localRotation = Quaternion.Slerp(af, _armRest, k);
            yield return null;
        }

        ResetBonesToRest();
        _activeSource = null;
        _animating = false;
    }
}
