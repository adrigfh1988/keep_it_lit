using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F97: static facade for the recorded-audio library. <see cref="Library"/> is assigned by MazeGenerator.Awake (before anything plays) and is never
/// null - with no asset assigned every slot is empty, so every call site keeps its generated fallback. Play helpers return false when the slot has
/// no clip, so a call site reads <c>if (!GameAudio.PlayAt(slot, pos, 0.8f)) generated...</c> and never builds the fallback when it does not need it.
/// </summary>
public static class GameAudio
{
    /// <summary>Debug: log which slots resolved to library clips (one line per slot, once).</summary>
    public static bool LogResolution;

    private static GameAudioLibrary _library;
    private static GameAudioLibrary _empty;
    private static readonly HashSet<string> Logged = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _library = null;
        _empty = null;
        Logged.Clear();
    }

    /// <summary>The scene's library, or an empty one (never null).</summary>
    public static GameAudioLibrary Library
    {
        get
        {
            if (_library != null) return _library;
            if (_empty == null) _empty = ScriptableObject.CreateInstance<GameAudioLibrary>();
            return _empty;
        }
    }

    public static void Assign(GameAudioLibrary library)
    {
        _library = library;
        Logged.Clear();
        if (LogResolution && library != null) LogSlots(library);
    }

    private static void LogSlots(GameAudioLibrary lib)
    {
        var sb = new System.Text.StringBuilder("GameAudio: slots with library clips -");
        int filled = 0, empty = 0;
        foreach (System.Reflection.FieldInfo f in typeof(GameAudioLibrary).GetFields())
        {
            if (f.FieldType != typeof(AudioSlot)) continue;
            AudioSlot s = (AudioSlot)f.GetValue(lib);
            if (s != null && s.Has) { filled++; sb.Append(' ').Append(f.Name).Append('=').Append(s.clips.Length); }
            else { empty++; }
        }
        sb.Append(" | ").Append(filled).Append(" filled, ").Append(empty).Append(" on generated fallback");
        Debug.Log(sb.ToString());
    }

    /// <summary>Plays one of the slot's clips as a 3D one-shot at `position`. Returns false (nothing played) when the slot is empty.</summary>
    public static bool PlayAt(AudioSlot slot, Vector3 position, float volume = 1f, float pitch = 1f, float maxDistance = 0f)
    {
        return Spawn(slot, position, true, volume, pitch, maxDistance) != null;
    }

    /// <summary>Plays one of the slot's clips flat (no positioning). Returns false when the slot is empty.</summary>
    public static bool Play2D(AudioSlot slot, float volume = 1f, float pitch = 1f, float pan = 0f)
    {
        return Spawn(slot, Vector3.zero, false, volume, pitch, 0f, pan) != null;
    }

    /// <summary>Like <see cref="PlayAt"/> but returns the voice (null = empty slot) so the caller can follow it or parent it.</summary>
    public static AudioSource SpawnAt(AudioSlot slot, Vector3 position, float volume = 1f, float pitch = 1f, float maxDistance = 0f)
    {
        return Spawn(slot, position, true, volume, pitch, maxDistance);
    }

    private static AudioSource Spawn(AudioSlot slot, Vector3 position, bool spatial, float volume, float pitch, float maxDistance = 0f, float pan = 0f)
    {
        if (slot == null) return null;
        AudioClip clip = slot.Pick();
        if (clip == null) return null;

        GameObject go = new GameObject("OneShot_" + clip.name);
        go.transform.position = position;
        AudioSource src = go.AddComponent<AudioSource>();
        src.clip = clip;
        src.playOnAwake = false;
        src.loop = false;
        src.panStereo = Mathf.Clamp(pan, -1f, 1f);
        src.spatialBlend = spatial ? 1f : 0f;
        if (spatial && maxDistance > 0f) { src.rolloffMode = AudioRolloffMode.Linear; src.minDistance = 1.5f; src.maxDistance = maxDistance; } // short-range sounds only (a lamp zap); default rolloff otherwise
        src.volume = Mathf.Clamp01(volume * slot.volume);
        float p = Mathf.Max(0.1f, pitch * slot.PickPitch());
        src.pitch = p;
        if (slot.lowPassHz > 0f)
        {
            AudioLowPassFilter lp = go.AddComponent<AudioLowPassFilter>();
            lp.cutoffFrequency = slot.lowPassHz;
        }
        float offset = Mathf.Min(slot.startOffset, Mathf.Max(0f, clip.length - 0.1f));
        if (offset > 0f) src.time = offset;
        src.Play();

        float playable = clip.length - offset;
        bool cut = slot.maxSeconds > 0f && slot.maxSeconds < playable;
        float seconds = (cut ? slot.maxSeconds : playable) / p;
        go.AddComponent<OneShotFade>().Begin(src, seconds, cut ? Mathf.Min(0.35f, seconds * 0.4f) : 0f);

        if (LogResolution && Logged.Add(clip.name)) Debug.Log("GameAudio: played library clip " + clip.name);
        return src;
    }
}
