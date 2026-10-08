using System;
using UnityEngine;

/// <summary>
/// F92 (D15): the sound of the asylum. A quiet looping room tone, the floor's base music at a whisper (handed in through
/// <see cref="SetMusic"/>, faded in and out with the hub), positional emitters placed by AsylumHubBuilder (the machine room's hum,
/// a dripping tap in the toilets, the lift shaft's wind) and, every 25-60 s, a far-off noise from one of the sound points.
/// Everything is synthesised on first use, nothing is a project asset, and it only runs while the hub content is switched on
/// (the building, which carries the emitters, is inactive otherwise). It never alerts the hunter - no HearNoise.
/// </summary>
public class AsylumAmbience : MonoBehaviour
{
    public enum EmitterKind { MachineHum, Drip, ShaftWind }

    [Serializable]
    public struct Emitter
    {
        public AudioSource source;
        public EmitterKind kind;
    }

    [SerializeField] private AsylumHub hub;
    [SerializeField] private Emitter[] emitters = new Emitter[0];
    [Tooltip("Where the occasional far-off noises come from.")]
    [SerializeField] private Transform[] soundPoints = new Transform[0];

    [Header("Levels")]
    [SerializeField, Range(0f, 0.3f)] private float toneVolume = 0.05f;
    [SerializeField, Range(0f, 0.3f)] private float musicVolume = 0.08f;
    [SerializeField] private float fadeSeconds = 1.2f;
    [SerializeField] private Vector2 secondsBetweenNoises = new Vector2(25f, 60f);

    private static AudioClip _buzz, _tone, _machine, _drip, _wind;
    private static AudioClip[] _noises;

    private AudioSource _toneSource, _musicSource;
    private float _fade;          // 0..1, follows the hub content switch
    private float _nextNoise;
    private bool _ready;

    /// <summary>A short mains buzz, shared by every flickering lamp.</summary>
    public static AudioClip BuzzClip => _buzz != null ? _buzz : _buzz = BuildLoop("AsylumBuzz", 1f, (t, i) =>
        0.5f * Mathf.Sin(2f * Mathf.PI * 100f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 200f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * 300f * t) + 0.05f * Noise(i));

    public void Configure(AsylumHub owner, Emitter[] positional, Transform[] noisePoints)
    {
        hub = owner;
        emitters = positional;
        soundPoints = noisePoints;
    }

    /// <summary>The floor's base music, played at a whisper while the player is in the asylum (null = none). Takes effect at once.</summary>
    public void SetMusic(AudioClip clip)
    {
        EnsureSources();
        if (_musicSource.clip == clip) return;
        _musicSource.clip = clip;
        _musicSource.loop = true;
    }

    private void EnsureSources()
    {
        if (_toneSource != null) return;
        _toneSource = gameObject.AddComponent<AudioSource>();
        _toneSource.playOnAwake = false;
        _toneSource.loop = true;
        _toneSource.spatialBlend = 0f;
        _toneSource.volume = 0f;
        _musicSource = gameObject.AddComponent<AudioSource>();
        _musicSource.playOnAwake = false;
        _musicSource.loop = true;
        _musicSource.spatialBlend = 0f;
        _musicSource.volume = 0f;
    }

    private void Init()
    {
        _ready = true;
        EnsureSources();
        _tone = _tone != null ? _tone : BuildLoop("AsylumTone", 2f, (t, i) =>
            0.5f * Mathf.Sin(2f * Mathf.PI * 50f * t) + 0.25f * Mathf.Sin(2f * Mathf.PI * 100f * t) + 0.1f * Mathf.Sin(2f * Mathf.PI * 150f * t) + 0.03f * Noise(i));
        _toneSource.clip = _tone;

        _machine = _machine != null ? _machine : BuildLoop("AsylumMachine", 4f, (t, i) =>
        {
            float swell = 0.75f + 0.25f * Mathf.Sin(2f * Mathf.PI * 0.5f * t);
            return swell * (0.5f * Mathf.Sin(2f * Mathf.PI * 40f * t) + 0.3f * Mathf.Sin(2f * Mathf.PI * 80f * t) + 0.15f * Mathf.Sin(2f * Mathf.PI * 120f * t)) + 0.03f * Noise(i);
        });
        _drip = _drip != null ? _drip : BuildDripLoop();
        _wind = _wind != null ? _wind : BuildWindLoop();

        if (_noises == null)
        {
            _noises = new[]
            {
                DoorAudio.BuildClunkClip(0.5f, 120f, 0.7f),   // a door shutting somewhere deep in the building
                DoorAudio.BuildClunkClip(0.16f, 480f, 0.3f),  // pipes knocking
                DoorAudio.BuildClunkClip(0.7f, 65f, 0.8f),    // something heavy, far off
                DoorAudio.BuildClunkClip(0.07f, 2200f, 0.3f), // a drip in a stairwell
            };
        }

        foreach (Emitter e in emitters)
        {
            if (e.source == null) continue;
            e.source.clip = e.kind == EmitterKind.MachineHum ? _machine : e.kind == EmitterKind.Drip ? _drip : _wind;
            e.source.loop = true;
            e.source.spatialBlend = 1f;
            e.source.rolloffMode = AudioRolloffMode.Linear;
            e.source.minDistance = 1.5f;
            e.source.maxDistance = e.kind == EmitterKind.Drip ? 7f : 12f;
            e.source.volume = e.kind == EmitterKind.MachineHum ? 0.22f : e.kind == EmitterKind.Drip ? 0.5f : 0.3f;
            e.source.playOnAwake = false;
        }
    }

    private void Update()
    {
        if (!Application.isPlaying) return;
        bool active = hub != null && hub.ContentActive && !hub.Leaving; // F92: fades out with the screen during the walk through a wing door
        _fade = Mathf.MoveTowards(_fade, active ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeSeconds));

        if (active && !_ready) { Init(); _nextNoise = Time.time + UnityEngine.Random.Range(8f, 20f); }
        if (!_ready) return;

        _toneSource.volume = toneVolume * _fade;
        _musicSource.volume = musicVolume * _fade;
        SetPlaying(_toneSource, _fade > 0.001f);
        SetPlaying(_musicSource, _fade > 0.001f && _musicSource.clip != null);

        foreach (Emitter e in emitters)
        {
            if (e.source == null || !e.source.gameObject.activeInHierarchy) continue;
            if (active && !e.source.isPlaying) e.source.Play();
        }

        if (active && Time.time >= _nextNoise && soundPoints != null && soundPoints.Length > 0)
        {
            _nextNoise = Time.time + UnityEngine.Random.Range(secondsBetweenNoises.x, secondsBetweenNoises.y);
            Transform point = soundPoints[UnityEngine.Random.Range(0, soundPoints.Length)];
            if (point != null) AudioSource.PlayClipAtPoint(_noises[UnityEngine.Random.Range(0, _noises.Length)], point.position, UnityEngine.Random.Range(0.3f, 0.5f));
        }
    }

    private static void SetPlaying(AudioSource source, bool on)
    {
        if (on && !source.isPlaying) source.Play();
        else if (!on && source.isPlaying) source.Stop();
    }

    // ---------------------------------------------------------------- synthesis

    private const int SampleRate = 22050;

    private static float Noise(int i)
    {
        // Cheap deterministic hash noise in -1..1.
        uint x = (uint)i * 2654435761u;
        x ^= x >> 15; x *= 2246822519u; x ^= x >> 13;
        return (x & 0xFFFF) / 32767.5f - 1f;
    }

    /// <summary>A mono loop whose samples come from `f(seconds, index)`; the tones in the callers use whole cycles so the loop is seamless.</summary>
    private static AudioClip BuildLoop(string name, float seconds, Func<float, int, float> f)
    {
        int n = Mathf.RoundToInt(seconds * SampleRate);
        float[] data = new float[n];
        for (int i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)SampleRate, i), -1f, 1f);
        AudioClip clip = AudioClip.Create(name, n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip BuildDripLoop()
    {
        const float seconds = 4f;
        int n = Mathf.RoundToInt(seconds * SampleRate);
        float[] data = new float[n];
        foreach (float at in new[] { 0.6f, 2.9f })
        {
            int start = Mathf.RoundToInt(at * SampleRate);
            int length = Mathf.RoundToInt(0.12f * SampleRate);
            for (int i = 0; i < length && start + i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-t * 55f);
                float freq = 2600f - 5500f * t;
                data[start + i] += 0.6f * env * Mathf.Sin(2f * Mathf.PI * freq * t);
            }
        }
        AudioClip clip = AudioClip.Create("AsylumDrip", n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private static AudioClip BuildWindLoop()
    {
        const float seconds = 8f;
        int n = Mathf.RoundToInt(seconds * SampleRate);
        float[] data = new float[n];
        float low = 0f;
        for (int i = 0; i < n; i++)
        {
            low += (Noise(i) - low) * 0.035f; // one-pole low-pass: a soft rumble of air
            float swell = 0.55f + 0.45f * Mathf.Sin(2f * Mathf.PI * 0.25f * (i / (float)SampleRate));
            data[i] = low * swell * 2.4f;
        }
        // Cross-fade the cut-off tail into the start, so the loop continues from where it ends without a click.
        int fade = Mathf.RoundToInt(0.4f * SampleRate);
        float[] looped = new float[n - fade];
        Array.Copy(data, looped, looped.Length);
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            looped[i] = data[i] * k + data[n - fade + i] * (1f - k);
        }
        AudioClip clip = AudioClip.Create("AsylumWind", looped.Length, 1, SampleRate, false);
        clip.SetData(looped, 0);
        return clip;
    }
}
