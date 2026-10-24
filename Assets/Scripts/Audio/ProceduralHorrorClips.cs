using UnityEngine;

/// <summary>
/// Pure static PCM waveform synthesis for horror audio fallback clips (heartbeat, relocation
/// chirp, breath/whisper, and low-frequency thump primitives). Extracted from
/// <see cref="HorrorAudioDirector"/> so runtime mixing and ducking stay separate from raw DSP math.
/// </summary>
public static class ProceduralHorrorClips
{
    /// <summary>
    /// A two-thump heartbeat built from scratch, because the project has no horror source material.
    /// </summary>
    public static AudioClip BuildHeartbeatClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.45f;
        const float dubDelay = 0.20f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        AddThump(samples, sampleRate, 0f, 55f, 0.045f, 1.0f);
        AddThump(samples, sampleRate, dubDelay, 48f, 0.035f, 0.8f);

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.8f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("Heartbeat", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>A descending sine sweep with a noise "whump" at the end - the sound of it landing behind you.</summary>
    public static AudioClip BuildRelocationCueClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.9f;
        const float freqStart = 140f;
        const float freqEnd = 38f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float sweepPerSecond = (freqEnd - freqStart) / duration;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            // Integrated frequency (a proper linear chirp), same technique as MazeEscape's ping.
            float phase = 2f * Mathf.PI * (freqStart * t + 0.5f * sweepPerSecond * t * t);
            // 1/t-shaped envelope: loud immediately, trails off rather than swelling in.
            float envelope = 1f / (1f + t * 6f);
            samples[i] = Mathf.Sin(phase) * envelope;
        }

        // 12 ms noise transient at the very end - the "whump" of something arriving.
        int transientSamples = Mathf.RoundToInt(0.012f * sampleRate);
        int transientStart = Mathf.Max(0, sampleCount - transientSamples);
        System.Random noise = new System.Random(4242);
        for (int i = transientStart; i < sampleCount; i++)
        {
            float t = (i - transientStart) / (float)transientSamples;
            samples[i] += (float)(noise.NextDouble() * 2.0 - 1.0) * 0.6f * (1f - t);
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.85f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("RelocationCue", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>Filtered white noise with a rise-fall envelope and a slow tremolo - reads as a breath, not a word.</summary>
    public static AudioClip BuildWhisperClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.7f;
        const float lowPass = 0.08f;

        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random noise = new System.Random(9001);
        float y = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float x = (float)(noise.NextDouble() * 2.0 - 1.0);
            y += lowPass * (x - y);

            float envelope = Mathf.Sin(Mathf.PI * t / duration);
            envelope *= envelope;
            float tremolo = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 2f * t);

            samples[i] = y * envelope * tremolo;
        }

        float peak = 0f;
        for (int i = 0; i < sampleCount; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak > 0.0001f)
        {
            float gain = 0.9f / peak;
            for (int i = 0; i < sampleCount; i++) samples[i] *= gain;
        }

        AudioClip clip = AudioClip.Create("Whisper", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    public static void AddThump(float[] samples, int sampleRate, float startSeconds, float frequency, float decay, float amplitude)
    {
        int start = Mathf.RoundToInt(startSeconds * sampleRate);
        System.Random noise = new System.Random(unchecked((int)(frequency * 1000f)));

        for (int i = start; i < samples.Length; i++)
        {
            float t = (i - start) / (float)sampleRate;
            float envelope = Mathf.Exp(-t / decay);
            if (envelope < 0.0005f) break;

            // 3 ms fade-in kills the click that a hard attack on a sine produces
            envelope *= Mathf.Clamp01(t / 0.003f);

            float value = Mathf.Sin(2f * Mathf.PI * frequency * t);
            // A second harmonic, and a brief noise transient over the first few milliseconds. Without
            // these the 55 Hz fundamental is simply inaudible on laptop speakers.
            value += 0.35f * Mathf.Sin(2f * Mathf.PI * frequency * 2f * t);
            if (t < 0.008f)
            {
                value += 0.3f * (float)(noise.NextDouble() * 2.0 - 1.0) * (1f - t / 0.008f);
            }

            samples[i] += value * envelope * amplitude;
        }
    }

    // Convenience aliases matching the synthesis family names
    public static AudioClip MakeThumpClip() => BuildHeartbeatClip();
    public static AudioClip MakeRiserClip() => BuildRelocationCueClip();
    public static AudioClip MakeWhisperClip() => BuildWhisperClip();
    public static AudioClip MakeBreathClip() => BuildWhisperClip();
}
