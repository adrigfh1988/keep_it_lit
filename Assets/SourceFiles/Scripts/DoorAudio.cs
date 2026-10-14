using UnityEngine;

/// <summary>
/// Procedural clunk/rattle/click clips for doors and buttons (F72), same construction technique as
/// MazeGenerator.BuildShardChimeClip - a short noise/tone mix under an exponential decay envelope. No
/// new audio assets needed.
/// </summary>
public static class DoorAudio
{
    /// <summary>Testing only: when on, the asylum hub logs each sound event it makes ("[HubAudio] time what").</summary>
    public static bool LogEvents;
    public static void Note(string what) { if (LogEvents) Debug.Log("[HubAudio] " + Time.time.ToString("F1") + " " + what); }

    /// <summary>A low tone/noise thud - used for the shutter's slide and the hunter's slam (louder, lower, noisier).</summary>
    public static AudioClip BuildClunkClip(float duration, float baseFrequency, float noiseAmount)
    {
        const int sampleRate = 44100;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random rng = new System.Random(1289);

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t / Mathf.Max(0.02f, duration * 0.35f));
            envelope *= Mathf.Clamp01(t / 0.004f); // kills the attack click

            float tone = Mathf.Sin(2f * Mathf.PI * baseFrequency * t);
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            samples[i] = Mathf.Lerp(tone, noise, noiseAmount) * envelope;
        }

        Normalise(samples, 0.85f);
        AudioClip clip = AudioClip.Create("DoorClunk", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// A knuckle or wrench on a metal pipe: a short noise tick, then inharmonic partials that ring for a fraction of a second
    /// (the low ones longest). Reads as metal at any distance, unlike BuildClunkClip's single low tone.
    /// </summary>
    public static AudioClip BuildPipeClankClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.55f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        float[] freq = { 412f, 667f, 1183f, 1794f, 2611f };
        float[] amp = { 1.0f, 0.75f, 0.5f, 0.32f, 0.2f };
        float[] decay = { 0.2f, 0.15f, 0.1f, 0.07f, 0.05f };
        System.Random rng = new System.Random(4407);

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float s = 0f;
            for (int p = 0; p < freq.Length; p++) s += amp[p] * Mathf.Exp(-t / decay[p]) * Mathf.Sin(2f * Mathf.PI * freq[p] * t);
            float tick = (float)(rng.NextDouble() * 2.0 - 1.0) * Mathf.Exp(-t / 0.004f) * 1.4f;
            samples[i] = (s + tick) * Mathf.Clamp01(t / 0.0008f);
        }

        Normalise(samples, 0.85f);
        AudioClip clip = AudioClip.Create("PipeClank", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// A hinge or floorboard creak: a wobbling saw tone chopped by an irregular stick-slip pattern, softened by a one-pole low-pass.
    /// `baseFrequency` ~120-260 Hz; `seed` varies the wobble so two creaks are not identical.
    /// </summary>
    public static AudioClip BuildCreakClip(float duration, float baseFrequency, int seed = 1)
    {
        const int sampleRate = 22050;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random rng = new System.Random(seed * 7919 + 13);
        float wobbleA = 0.9f + (float)rng.NextDouble() * 1.2f, wobbleB = 2.5f + (float)rng.NextDouble() * 2f, slipRate = 30f + (float)rng.NextDouble() * 25f;
        float phase = 0f, low = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float f = baseFrequency * (1f + 0.25f * Mathf.Sin(2f * Mathf.PI * wobbleA * t) + 0.12f * Mathf.Sin(2f * Mathf.PI * wobbleB * t));
            phase += f / sampleRate;
            float saw = 2f * (phase - Mathf.Floor(phase)) - 1f;
            float slip = 0.5f + 0.5f * Mathf.Sin(2f * Mathf.PI * (slipRate + 14f * Mathf.Sin(2f * Mathf.PI * 0.8f * t)) * t);
            slip *= slip;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            float env = Mathf.Pow(Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / duration)), 0.7f);
            float raw = (saw * 0.6f + noise * 0.25f) * slip * env;
            low += (raw - low) * 0.3f;
            samples[i] = low;
        }

        Normalise(samples, 0.7f);
        AudioClip clip = AudioClip.Create("Creak", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>
    /// F112: cold air out of an opened doorway - low-passed white noise, a 0.12 s attack and an exponential fade, the filter opening a little
    /// over the first 0.4 s (a rising tilt). Deliberately a soft whoosh and not a creak: the hinge creak was removed from the door walk on purpose.
    /// </summary>
    public static AudioClip BuildDraftClip()
    {
        const int sampleRate = 22050;
        const float duration = 1.4f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random rng = new System.Random(2112);
        float low = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float alpha = Mathf.Lerp(0.02f, 0.06f, Mathf.Clamp01(t / 0.4f));
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += (noise - low) * alpha;
            float envelope = t < 0.12f ? t / 0.12f : Mathf.Exp(-(t - 0.12f) / 0.4f);
            samples[i] = low * envelope;
        }

        Normalise(samples, 0.5f);
        AudioClip clip = AudioClip.Create("DoorDraft", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }


    /// <summary>
    /// F114: rummaging through a cabinet or drawer - four short dull clunks at irregular 0.1-0.25 s spacing mixed into one 0.9 s clip, each a
    /// decaying low tone (300-700 Hz) with a bit of noise. Deterministic seed.
    /// </summary>
    public static AudioClip BuildRummageClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.9f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random rng = new System.Random(114);

        float start = 0.02f;
        for (int hit = 0; hit < 4; hit++)
        {
            float frequency = 300f + (float)rng.NextDouble() * 400f;
            int first = Mathf.CeilToInt(start * sampleRate);
            for (int i = first; i < sampleCount; i++)
            {
                float t = (i - first) / (float)sampleRate;
                if (t > 0.12f) break;
                float envelope = Mathf.Exp(-t / 0.03f) * Mathf.Clamp01(t / 0.002f);
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                samples[i] += Mathf.Lerp(Mathf.Sin(2f * Mathf.PI * frequency * t), noise, 0.7f) * envelope * 0.7f;
            }
            start += 0.1f + (float)rng.NextDouble() * 0.15f;
        }

        Normalise(samples, 0.8f);
        AudioClip clip = AudioClip.Create("Rummage", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>F114: a chair dragged on the floor - 0.4 s of band-passed noise around 900 Hz with a 30 Hz stick-slip amplitude wobble.</summary>
    public static AudioClip BuildScrapeClip()
    {
        const int sampleRate = 22050;
        const float duration = 0.4f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random rng = new System.Random(1141);
        float lowA = 0f, lowB = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            lowA += (noise - lowA) * 0.35f; // low-pass ~ 1.8 kHz
            lowB += (noise - lowB) * 0.06f; // low-pass ~ 300 Hz, subtracted: a crude band-pass
            float band = lowA - lowB;
            float stick = 0.35f + 0.65f * Mathf.Abs(Mathf.Sin(Mathf.PI * 30f * t + 0.8f * Mathf.Sin(2f * Mathf.PI * 4f * t)));
            float envelope = Mathf.Clamp01(t / 0.03f) * Mathf.Clamp01((duration - t) / 0.12f);
            samples[i] = band * stick * envelope;
        }

        Normalise(samples, 0.7f);
        AudioClip clip = AudioClip.Create("ChairScrape", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>F114: the hiss of a television with no signal - 2 s of high-passed white noise, faded in and out at the ends so it loops without a click.</summary>
    public static AudioClip BuildStaticHissClip()
    {
        const int sampleRate = 22050;
        const float duration = 2f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];
        System.Random rng = new System.Random(1142);
        float low = 0f;

        for (int i = 0; i < sampleCount; i++)
        {
            float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
            low += (noise - low) * 0.12f;
            samples[i] = noise - low; // high-pass: the noise minus its low-passed self
        }

        int fade = sampleRate / 20;
        for (int i = 0; i < fade; i++)
        {
            float g = i / (float)fade;
            samples[i] *= g;
            samples[sampleCount - 1 - i] *= g;
        }

        Normalise(samples, 0.6f);
        AudioClip clip = AudioClip.Create("TvStaticHiss", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    /// <summary>A copy of `source` through a one-pole low-pass (`alpha` 0.02 = very dull, 1 = untouched): a sound heard through walls.</summary>
    public static AudioClip Muffle(AudioClip source, float alpha)
    {
        float[] data = new float[source.samples * source.channels];
        source.GetData(data, 0);
        float low = 0f;
        for (int i = 0; i < data.Length; i++) { low += (data[i] - low) * alpha; data[i] = low; }
        Normalise(data, 0.85f);
        AudioClip clip = AudioClip.Create(source.name + "Muffled", source.samples, source.channels, source.frequency, false);
        clip.SetData(data, 0);
        return clip;
    }

    /// <summary>A tiny high click - the button press.</summary>
    public static AudioClip BuildClickClip()
    {
        const int sampleRate = 44100;
        const float duration = 0.06f;
        int sampleCount = Mathf.CeilToInt(sampleRate * duration);
        float[] samples = new float[sampleCount];

        for (int i = 0; i < sampleCount; i++)
        {
            float t = i / (float)sampleRate;
            float envelope = Mathf.Exp(-t / 0.015f);
            samples[i] = Mathf.Sin(2f * Mathf.PI * 1800f * t) * envelope;
        }

        Normalise(samples, 0.7f);
        AudioClip clip = AudioClip.Create("ButtonClick", sampleCount, 1, sampleRate, false);
        clip.SetData(samples, 0);
        return clip;
    }

    private static void Normalise(float[] samples, float targetPeak)
    {
        float peak = 0f;
        for (int i = 0; i < samples.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(samples[i]));
        if (peak <= 0.0001f) return;

        float gain = targetPeak / peak;
        for (int i = 0; i < samples.Length; i++) samples[i] *= gain;
    }
}
