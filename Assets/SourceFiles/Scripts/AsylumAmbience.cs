using System;
using StarterAssets;
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

    private AudioSource _toneSource, _musicSource, _urgentSource;
    private float _eventsPerMinute;      // F93 decay: 0 = the old 25-60 s spacing
    private int _decayFloor = 1;
    private float _urgencyTarget, _urgency;
    private AudioClip _urgentClip;
    private static AudioClip _scream;
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

    /// <summary>F93 (decay): how many far-off noises per minute (0 = the default slow spacing) and the floor, which adds footsteps overhead (2+) and a far scream (4+).</summary>
    public void SetDecay(float eventsPerMinute, int floor)
    {
        _eventsPerMinute = eventsPerMinute;
        _decayFloor = floor;
        _nextNoise = Mathf.Min(_nextNoise, Time.time + 6f);
    }

    /// <summary>F93 (ending B): every hub sound fades out now and stays out until the next hub entry.</summary>
    public void Mute() { _muted = true; fadeSeconds = Mathf.Min(fadeSeconds, 0.35f); }
    private bool _muted;
    public void Unmute() { _muted = false; }

    /// <summary>F93 (chase set piece): the escape track under the base music. `target` 0..1 is eased in/out.</summary>
    public void SetUrgent(AudioClip clip) { _urgentClip = clip; }
    public void SetUrgency(float target, float seconds = 1.5f) { _urgencyTarget = Mathf.Clamp01(target); _urgencySeconds = Mathf.Max(0.05f, seconds); }
    private float _urgencySeconds = 1.5f;

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

        if (_noises == null || _noises.Length == 0 || _noises[0] == null) // Unity-null: runtime clips are destroyed when Play stops (domain reload may be off)
        {
            _noises = new[]
            {
                DoorAudio.Muffle(DoorAudio.BuildClunkClip(0.5f, 120f, 0.7f), 0.1f), // a door shutting somewhere deep in the building (dulled by walls)
                DoorAudio.BuildPipeClankClip(),                                  // a pipe clanking in the walls
                DoorAudio.BuildCreakClip(1.2f, 140f, 11),                        // the building settling
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
        bool active = hub != null && hub.ContentActive && !hub.Leaving && !_muted; // F92: fades out with the screen during the walk through a wing door
        _fade = Mathf.MoveTowards(_fade, active ? 1f : 0f, Time.unscaledDeltaTime / Mathf.Max(0.05f, fadeSeconds));

        if (active && !_ready) { Init(); _nextNoise = Time.time + UnityEngine.Random.Range(8f, 20f); }
        if (!_ready) return;

        _toneSource.volume = toneVolume * _fade;
        _urgency = Mathf.MoveTowards(_urgency, _urgencyTarget, Time.unscaledDeltaTime / _urgencySeconds);
        _musicSource.volume = musicVolume * _fade * (1f - 0.85f * _urgency);
        if (_urgentClip != null)
        {
            if (_urgentSource == null) { _urgentSource = gameObject.AddComponent<AudioSource>(); _urgentSource.playOnAwake = false; _urgentSource.loop = true; _urgentSource.spatialBlend = 0f; }
            _urgentSource.clip = _urgentClip;
            _urgentSource.volume = 0.3f * _urgency * _fade;
            SetPlaying(_urgentSource, _urgency > 0.001f && _fade > 0.001f);
        }
        SetPlaying(_toneSource, _fade > 0.001f);
        SetPlaying(_musicSource, _fade > 0.001f && _musicSource.clip != null);

        foreach (Emitter e in emitters)
        {
            if (e.source == null || !e.source.gameObject.activeInHierarchy) continue;
            if (active && !e.source.isPlaying) e.source.Play();
        }

        if (active && Time.time >= _nextNoise && soundPoints != null && soundPoints.Length > 0)
        {
            _nextNoise = Time.time + (_eventsPerMinute > 0.01f ? UnityEngine.Random.Range(0.6f, 1.4f) * 60f / _eventsPerMinute : UnityEngine.Random.Range(secondsBetweenNoises.x, secondsBetweenNoises.y));
            Transform point = soundPoints[UnityEngine.Random.Range(0, soundPoints.Length)];
            if (point != null) PlayFarEvent(point.position);
        }
    }

    /// <summary>One far-off event at `position`: a door/pipe/thud, from floor 2 also footsteps overhead, from floor 4 a scream.</summary>
    private void PlayFarEvent(Vector3 position)
    {
        DoorAudio.Note("far event at " + position);
        float roll = UnityEngine.Random.value;
        if (_decayFloor >= 4 && roll < 0.25f)
        {
            if (_scream == null) _scream = BuildScream();
            AudioSource.PlayClipAtPoint(_scream, position, 0.45f);
        }
        else if (_decayFloor >= 2 && roll < 0.55f)
        {
            StartCoroutine(FootstepsOverhead(position + Vector3.up * 3.5f));
        }
        else
        {
            int pick = UnityEngine.Random.Range(0, _noises.Length);
            DoorAudio.Note("far noise #" + pick);
            AudioSource.PlayClipAtPoint(_noises[pick], position, UnityEngine.Random.Range(0.3f, 0.5f));
        }
    }

    /// <summary>
    /// Someone walking in the room above: the player's own footstep clips, low-passed (a ceiling in the way) and pitched down a little, alternating feet,
    /// irregular timing with the odd hesitation, a floorboard creak now and then, travelling across and fading as they walk away.
    /// </summary>
    private System.Collections.IEnumerator FootstepsOverhead(Vector3 at)
    {
        DoorAudio.Note("footsteps overhead");
        if (_stepClips == null || _stepClips.Length == 0)
        {
            ThirdPersonController tpc = UnityEngine.Object.FindAnyObjectByType<ThirdPersonController>();
            _stepClips = tpc != null && tpc.FootstepAudioClips != null && tpc.FootstepAudioClips.Length > 0 ? tpc.FootstepAudioClips : new[] { DoorAudio.BuildClunkClip(0.12f, 90f, 0.5f) };
        }
        if (_creaks == null || _creaks.Length == 0) _creaks = new[] { DoorAudio.BuildCreakClip(0.45f, 150f, 3), DoorAudio.BuildCreakClip(0.6f, 190f, 5), DoorAudio.BuildCreakClip(0.35f, 230f, 8) };

        GameObject stepsGo = new GameObject("OverheadSteps");
        _stepWalkers.Add(stepsGo);
        stepsGo.transform.SetParent(transform, false);
        AudioSource src = stepsGo.AddComponent<AudioSource>();
        src.playOnAwake = false;
        src.spatialBlend = 1f;
        src.rolloffMode = AudioRolloffMode.Linear;
        src.minDistance = 3f;
        src.maxDistance = 20f;
        AudioLowPassFilter lp = stepsGo.AddComponent<AudioLowPassFilter>();
        lp.cutoffFrequency = 650f;

        float angle = UnityEngine.Random.value * Mathf.PI * 2f;
        Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
        int count = UnityEngine.Random.Range(8, 13);
        for (int i = 0; i < count; i++)
        {
            float k = i / (float)(count - 1);
            if (stepsGo == null) yield break; // the hub switched off under us
            stepsGo.transform.position = at + dir * (-3.5f + 7f * k);
            src.pitch = (i % 2 == 0 ? 0.78f : 0.86f) * UnityEngine.Random.Range(0.95f, 1.05f);
            float vol = Mathf.Lerp(0.9f, 0.25f, k) * (i % 2 == 0 ? 1f : 0.85f);
            AudioClip stepClip = _stepClips.Length > 0 ? _stepClips[UnityEngine.Random.Range(0, 1000) % _stepClips.Length] : null;
            if (stepClip != null) src.PlayOneShot(stepClip, vol);
            if (UnityEngine.Random.value < 0.22f)
            {
                src.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
                src.PlayOneShot(_creaks[UnityEngine.Random.Range(0, 1000) % _creaks.Length], vol * 0.8f);
            }
            float wait = 0.62f + UnityEngine.Random.Range(-0.1f, 0.16f);
            if (UnityEngine.Random.value < 0.15f) wait += UnityEngine.Random.Range(0.5f, 0.9f); // a hesitation
            yield return new WaitForSeconds(wait);
        }
        yield return new WaitForSeconds(1.5f);
        if (stepsGo != null) Destroy(stepsGo);
        _stepWalkers.Remove(stepsGo);
    }

    [System.NonSerialized] private AudioClip[] _stepClips, _creaks;
    private readonly System.Collections.Generic.List<GameObject> _stepWalkers = new System.Collections.Generic.List<GameObject>();

    private void OnDisable()
    {
        foreach (GameObject g in _stepWalkers) if (g != null) Destroy(g);
        _stepWalkers.Clear();
    }

    private static AudioClip BuildScream()
    {
        int n = Mathf.RoundToInt(1.4f * SampleRate);
        float[] data = new float[n];
        float phase = 0f;
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)SampleRate;
            float env = Mathf.Sin(Mathf.PI * Mathf.Clamp01(t / 1.4f));
            float freq = 620f + 520f * Mathf.Sin(Mathf.PI * t / 1.4f) + 35f * Mathf.Sin(2f * Mathf.PI * 7f * t);
            phase += 2f * Mathf.PI * freq / SampleRate;
            data[i] = env * (0.45f * Mathf.Sin(phase) + 0.2f * Mathf.Sin(2f * phase) + 0.12f * Noise(i)) * 0.7f;
        }
        AudioClip clip = AudioClip.Create("AsylumFarScream", n, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
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
