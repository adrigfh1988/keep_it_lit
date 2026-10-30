using System;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Shared speech-bubble builder, 3D voice <see cref="AudioSource"/>, and frantic chase-music crossfader
/// collaborator for humanoid pursuers (<see cref="DarkBruno"/> and <see cref="NightNurse"/>).
/// </summary>
public sealed class EntityVoiceAndMusic
{
    public SpeechBubble Bubble { get; private set; }
    public AudioSource VoiceSource { get; private set; }
    public AudioSource ChaseMusicSource { get; private set; }
    public AudioLowPassFilter ChaseLowPass { get; private set; }
    public float SpeakUntil { get; set; } = -999f;

    private float _smoothedIntensity01;

    /// <summary>True while a voice clip or timed speech bubble is actively speaking.</summary>
    public bool IsSpeaking => Time.time < SpeakUntil;

    /// <summary>Creates the world-space <see cref="SpeechBubble"/> on the HUD canvas using <paramref name="palette"/>.</summary>
    public SpeechBubble BuildSpeechBubble(PlayerHud hud, SpeechBubble.Palette palette, string objectName, float maxDistance = 18f)
    {
        if (hud == null) return null;
        Canvas canvas = hud.GetComponentInParent<Canvas>();
        if (canvas == null) return null;

        Bubble = SpeechBubble.Create(canvas, maxDistance, palette);
        if (Bubble != null && !string.IsNullOrEmpty(objectName))
        {
            Bubble.gameObject.name = objectName;
        }
        return Bubble;
    }

    /// <summary>Creates the 3D voice <see cref="AudioSource"/> (optional) and looping frantic chase music <see cref="AudioSource"/>.</summary>
    public void BuildAudioSources(
        Transform owner,
        float eyeHeight,
        float chestHeight,
        AudioClip franticChaseClip,
        string voiceObjectName,
        string musicObjectName,
        bool createVoiceSource = true)
    {
        if (owner == null) return;

        if (createVoiceSource && VoiceSource == null && !string.IsNullOrEmpty(voiceObjectName))
        {
            GameObject voiceGo = new GameObject(voiceObjectName);
            voiceGo.transform.SetParent(owner, false);
            voiceGo.transform.localPosition = new Vector3(0f, eyeHeight, 0f);
            VoiceSource = voiceGo.AddComponent<AudioSource>();
            VoiceSource.playOnAwake = false;
            VoiceSource.loop = false;
            VoiceSource.spatialBlend = 0.82f;
            VoiceSource.rolloffMode = AudioRolloffMode.Linear;
            VoiceSource.minDistance = 2.2f;
            VoiceSource.maxDistance = 20f;
            GameSettings.RouteEffect(VoiceSource);
        }

        if (ChaseMusicSource == null && !string.IsNullOrEmpty(musicObjectName))
        {
            GameObject musicGo = new GameObject(musicObjectName);
            musicGo.transform.SetParent(owner, false);
            musicGo.transform.localPosition = new Vector3(0f, chestHeight, 0f);
            ChaseMusicSource = musicGo.AddComponent<AudioSource>();
            ChaseMusicSource.playOnAwake = false;
            ChaseMusicSource.loop = true;
            ChaseMusicSource.spatialBlend = 0.35f;
            ChaseMusicSource.rolloffMode = AudioRolloffMode.Linear;
            ChaseMusicSource.minDistance = 4f;
            ChaseMusicSource.maxDistance = 28f;
            ChaseMusicSource.volume = 0f;
            AudioClip loopClip = franticChaseClip != null ? franticChaseClip : GameAudio.Library.chaseMusic.First();
            ChaseMusicSource.clip = loopClip;
            GameSettings.RouteMusic(ChaseMusicSource);
        }
    }

    /// <summary>Fades the frantic chase music in during active rush/charge/pin states and pauses it when silent.</summary>
    public void UpdateChaseMusic(bool activeChase, AudioClip franticChaseClip, float maxVolume)
    {
        if (ChaseMusicSource == null) return;
        if (ChaseMusicSource.clip == null)
        {
            AudioClip loopClip = franticChaseClip != null ? franticChaseClip : GameAudio.Library.chaseMusic.First();
            if (loopClip == null) return;
            ChaseMusicSource.clip = loopClip;
        }

        float targetVol = activeChase ? maxVolume : 0f;
        float fadeRate = activeChase ? 2.4f : 0.65f;
        ChaseMusicSource.volume = Mathf.MoveTowards(ChaseMusicSource.volume, targetVol, fadeRate * Time.deltaTime);

        if (ChaseMusicSource.volume > 0.005f)
        {
            if (!ChaseMusicSource.isPlaying) ChaseMusicSource.Play();
        }
        else if (ChaseMusicSource.isPlaying)
        {
            ChaseMusicSource.Pause();
        }
    }

    /// <summary>
    /// Gradually modulates chase music intensity, low-pass cutoff, and heartbeat tempo/pitch
    /// based on proximity/threat (<paramref name="targetIntensity01"/> in 0..1) and line-of-sight occlusion.
    /// </summary>
    public void UpdateProximityChaseMusic(
        float targetIntensity01,
        AudioClip franticChaseClip,
        float maxVolume,
        float riseRate = 0.42f,
        float fallRate = 0.32f,
        bool hasLineOfSight = true,
        float minPitch = 0.93f,
        float maxPitch = 1.06f)
    {
        if (ChaseMusicSource == null) return;
        if (ChaseMusicSource.clip == null)
        {
            AudioClip loopClip = franticChaseClip != null ? franticChaseClip : GameAudio.Library.chaseMusic.First();
            if (loopClip == null) return;
            ChaseMusicSource.clip = loopClip;
        }

        if (ChaseLowPass == null)
        {
            ChaseLowPass = ChaseMusicSource.GetComponent<AudioLowPassFilter>();
            if (ChaseLowPass == null)
            {
                ChaseLowPass = ChaseMusicSource.gameObject.AddComponent<AudioLowPassFilter>();
                ChaseLowPass.cutoffFrequency = 520f;
                ChaseLowPass.lowpassResonanceQ = 1.0f;
            }
        }

        targetIntensity01 = Mathf.Clamp01(targetIntensity01);
        float rate = targetIntensity01 > _smoothedIntensity01 ? riseRate : fallRate;
        _smoothedIntensity01 = Mathf.MoveTowards(_smoothedIntensity01, targetIntensity01, rate * Time.deltaTime);

        ChaseMusicSource.volume = _smoothedIntensity01 * maxVolume;
        ChaseMusicSource.pitch = Mathf.Lerp(minPitch, maxPitch, _smoothedIntensity01);

        float maxCutoff = hasLineOfSight ? 18500f : 3400f;
        float targetCutoff = Mathf.Lerp(460f, maxCutoff, _smoothedIntensity01 * _smoothedIntensity01);
        ChaseLowPass.cutoffFrequency = Mathf.MoveTowards(ChaseLowPass.cutoffFrequency, targetCutoff, 9500f * Time.deltaTime);

        if (ChaseMusicSource.volume > 0.005f)
        {
            if (!ChaseMusicSource.isPlaying) ChaseMusicSource.Play();
        }
        else if (ChaseMusicSource.isPlaying)
        {
            ChaseMusicSource.Pause();
        }
    }

    /// <summary>Picks a line from <paramref name="pool"/>, optionally plays its matching voice clip, and displays the speech bubble.</summary>
    public void Speak(
        float flatDistanceToPlayer,
        string[] pool,
        AudioClip[] clips,
        SpeechBubble.Style style,
        Func<Vector3> bubbleAnchor,
        float shoutMaxRange = 26f,
        float normalMaxRange = 18f)
    {
        if (pool == null || pool.Length == 0) return;
        bool isShout = style == SpeechBubble.Style.Shout;
        float maxSpeakRange = isShout ? shoutMaxRange : normalMaxRange;
        if (flatDistanceToPlayer > maxSpeakRange) return;

        int idx = Random.Range(0, pool.Length);
        string line = pool[idx];

        AudioClip clip = (clips != null && idx >= 0 && idx < clips.Length) ? clips[idx] : null;
        if (clip != null && VoiceSource != null)
        {
            VoiceSource.Stop();
            VoiceSource.spatialBlend = isShout ? 0.38f : 0.80f;
            VoiceSource.maxDistance = maxSpeakRange;
            VoiceSource.pitch = isShout ? Random.Range(0.96f, 1.03f) : Random.Range(0.97f, 1.03f);
            VoiceSource.volume = isShout ? 0.98f : 0.86f;
            VoiceSource.clip = clip;
            VoiceSource.Play();
            SpeakUntil = Time.time + clip.length;
        }
        else
        {
            SpeakUntil = Time.time + 2.6f;
        }

        if (Bubble != null && bubbleAnchor != null)
        {
            Bubble.Show(line, style, bubbleAnchor, glitch: isShout);
        }
    }

    /// <summary>Destroys the instantiated speech bubble GameObject on entity cleanup.</summary>
    public void DestroyBubble()
    {
        if (Bubble != null)
        {
            UnityEngine.Object.Destroy(Bubble.gameObject);
            Bubble = null;
        }
    }
}
