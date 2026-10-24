using UnityEngine;

/// <summary>
/// F120: the player's microphone as one smoothed loudness number, 0..1. Static, no MonoBehaviour: PlayerVoice asks for the device every frame
/// (<see cref="SetWanted"/>) and reads <see cref="Level"/>. Desktop only: everything that touches <c>Microphone</c> sits inside
/// <c>#if !UNITY_WEBGL &amp;&amp; !UNITY_ANDROID &amp;&amp; !UNITY_IOS</c>, so the WebGL / tablet builds never reference it (and
/// <see cref="PlatformSupported"/> is false there). The audio is never played back, stored or sent - the only thing kept is the RMS of the last
/// 1024 samples. The device is closed on the falling edge of SetWanted, on scene teardown (PlayerVoice.OnDestroy), on quitting and at
/// SubsystemRegistration.
/// </summary>
public static class MicInput
{
    public enum Status { Off, Listening, NoDevice, NoSignal, Unsupported }

    /// <summary>True only on a desktop player or the Editor with a desktop target.</summary>
    public static bool PlatformSupported
    {
        get
        {
#if !UNITY_WEBGL && !UNITY_ANDROID && !UNITY_IOS
            return !Application.isMobilePlatform;
#else
            return false;
#endif
        }
    }

    /// <summary>Test hook: when &gt;= 0, <see cref="Level"/> follows it and no device is opened. Reset at SubsystemRegistration.</summary>
    public static float DebugLevel = -1f;

    /// <summary>Whether the gameplay path can run at all (the debug level makes it testable with no microphone).</summary>
    public static bool Supported => PlatformSupported || DebugLevel >= 0f;

    /// <summary>Smoothed loudness 0..1 (fast attack, slow release, unscaled time).</summary>
    public static float Level { get; private set; }

    /// <summary>Unsmoothed loudness 0..1 of the last 1024 samples.</summary>
    public static float RawLevel { get; private set; }

    public static Status CurrentStatus { get; private set; } = Status.Off;

    private const int WindowSamples = 1024;
    private const float AttackSeconds = 0.03f;
    private const float ReleaseSeconds = 0.25f;
    private const float NoSignalSeconds = 3f;

    private static bool _wanted;
    private static float _silentFor;
    private static float _levelVelocity;

    /// <summary>The device names the system reports (empty off-platform).</summary>
    public static string[] Devices
    {
        get
        {
#if !UNITY_WEBGL && !UNITY_ANDROID && !UNITY_IOS
            if (!PlatformSupported) return System.Array.Empty<string>();
            try { return Microphone.devices ?? System.Array.Empty<string>(); } catch { return System.Array.Empty<string>(); }
#else
            return System.Array.Empty<string>();
#endif
        }
    }

    /// <summary>True while a real device is open.</summary>
    public static bool DeviceOpen
    {
        get
        {
#if !UNITY_WEBGL && !UNITY_ANDROID && !UNITY_IOS
            return _clip != null;
#else
            return false;
#endif
        }
    }

    /// <summary>Called every frame by PlayerVoice: opens the device on the rising edge, closes it on the falling edge.</summary>
    public static void SetWanted(bool wanted)
    {
        if (wanted == _wanted) return;
        _wanted = wanted;
        if (wanted)
        {
            _silentFor = 0f;
            Open();
        }
        else
        {
            Close();
            Level = 0f;
            RawLevel = 0f;
            _levelVelocity = 0f;
            CurrentStatus = Status.Off;
        }
    }

    /// <summary>Re-opens the device (a different one was chosen in Settings).</summary>
    public static void Restart()
    {
        if (!_wanted) return;
        Close();
        _silentFor = 0f;
        Open();
    }

    /// <summary>Reads the loudness once per frame (unscaled time, so a pause keeps the meter alive).</summary>
    public static void Poll(float unscaledDt)
    {
        if (!_wanted)
        {
            return;
        }

        if (DebugLevel >= 0f)
        {
            RawLevel = Mathf.Clamp01(DebugLevel);
            CurrentStatus = Status.Listening;
            Smooth(unscaledDt);
            return;
        }

#if !UNITY_WEBGL && !UNITY_ANDROID && !UNITY_IOS
        if (_clip == null)
        {
            CurrentStatus = PlatformSupported ? Status.NoDevice : Status.Unsupported;
            RawLevel = 0f;
            Smooth(unscaledDt);
            return;
        }

        float rms = ReadRms();
        float dB = 20f * Mathf.Log10(Mathf.Max(rms, 1e-5f));
        RawLevel = Mathf.InverseLerp(-60f, -10f, dB);

        // A blocked microphone (the Windows privacy switch) records exact digital silence.
        _silentFor = rms <= 0f ? _silentFor + unscaledDt : 0f;
        CurrentStatus = _silentFor >= NoSignalSeconds ? Status.NoSignal : Status.Listening;
        Smooth(unscaledDt);
#else
        CurrentStatus = Status.Unsupported;
#endif
    }

    private static void Smooth(float dt)
    {
        float time = RawLevel > Level ? AttackSeconds : ReleaseSeconds;
        Level = Mathf.SmoothDamp(Level, RawLevel, ref _levelVelocity, time, float.MaxValue, Mathf.Max(0.0001f, dt));
        Level = Mathf.Clamp01(Level);
    }

#if !UNITY_WEBGL && !UNITY_ANDROID && !UNITY_IOS
    private static AudioClip _clip;
    private static string _deviceName;
    private static float[] _samples; // the whole looping second, preallocated once per open
    private static bool _quitHooked;

    private static void Open()
    {
        if (DebugLevel >= 0f)
        {
            CurrentStatus = Status.Listening;
            return;
        }

        if (!PlatformSupported)
        {
            CurrentStatus = Status.Unsupported;
            return;
        }

        string[] devices = Devices;
        if (devices.Length == 0)
        {
            CurrentStatus = Status.NoDevice;
            return;
        }

        string wanted = GameSettings.MicDevice;
        string device = null; // null = the system default
        if (!string.IsNullOrEmpty(wanted) && System.Array.IndexOf(devices, wanted) >= 0) device = wanted;

        int frequency = 16000;
        try
        {
            Microphone.GetDeviceCaps(device, out int min, out int max);
            if (min != 0 || max != 0) frequency = Mathf.Clamp(frequency, min, max);
        }
        catch { /* keep 16 kHz */ }

        try
        {
            _clip = Microphone.Start(device, true, 1, frequency);
        }
        catch
        {
            _clip = null;
        }

        if (_clip == null)
        {
            CurrentStatus = Status.NoDevice;
            return;
        }

        _deviceName = device;
        _samples = new float[Mathf.Max(WindowSamples, _clip.samples)];
        CurrentStatus = Status.Listening;
        if (!_quitHooked)
        {
            _quitHooked = true;
            Application.quitting += Close;
        }
    }

    private static void Close()
    {
        if (_clip == null) return;
        try { Microphone.End(_deviceName); } catch { /* already gone */ }
        _clip = null;
        _samples = null;
    }

    private static float ReadRms()
    {
        int position;
        try { position = Microphone.GetPosition(_deviceName); } catch { return 0f; }
        if (position <= 0) return 0f; // not started yet

        int length = _clip.samples;
        if (_samples == null || _samples.Length < length) _samples = new float[length];
        if (!_clip.GetData(_samples, 0)) return 0f;

        int count = Mathf.Min(WindowSamples, length);
        double sum = 0.0;
        for (int i = 0; i < count; i++)
        {
            int index = position - 1 - i;
            if (index < 0) index += length;
            float s = _samples[index];
            sum += s * s;
        }
        return Mathf.Sqrt((float)(sum / count));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Close();
        _wanted = false;
        _silentFor = 0f;
        Level = 0f;
        RawLevel = 0f;
        _levelVelocity = 0f;
        CurrentStatus = Status.Off;
        DebugLevel = -1f;
    }
#else
    private static void Open() { CurrentStatus = Status.Unsupported; }
    private static void Close() { }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _wanted = false;
        Level = 0f;
        RawLevel = 0f;
        CurrentStatus = Status.Off;
        DebugLevel = -1f;
    }
#endif

    /// <summary>Closes the device and clears the wish (PlayerVoice.OnDestroy, scene teardown).</summary>
    public static void Shutdown()
    {
        _wanted = false;
        Close();
        Level = 0f;
        RawLevel = 0f;
        CurrentStatus = Status.Off;
    }
}
