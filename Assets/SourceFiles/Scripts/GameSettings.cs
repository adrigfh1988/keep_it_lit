using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// S2: the player's machine settings (display, graphics, audio, controls). A static, like <see cref="TouchInput"/>: PlayerPrefs under
/// <c>lightsout.settings.&lt;name&gt;</c>, reloaded in SubsystemRegistration so an Editor Play session starts from the saved values, applied once
/// in BeforeSceneLoad. NOT campaign state: not in SaveData, not cleared by GameFlow.ReturnToMenu, untouched by a scene reload.
///
/// Every setter clamps, stores, saves, applies the part that needs applying right now and raises <see cref="Changed"/>; the systems that
/// own the rest (HorrorAtmosphere/HubRenderingLook exposure, FogScale, Flashlight shadows, FirstPersonRig FOV, LiftSetPiece shake, HorrorAtmosphere dread effects,
/// ThirdPersonController look) either read the value when they need it or subscribe to <see cref="Changed"/>.
///
/// Audio: <see cref="BindMixer"/> (MazeGenerator.Awake, before anything plays) hands over GameMixer.mixer. Music sources call
/// <see cref="RouteMusic"/>, one-shots <see cref="RouteEffect"/>, and AudioRouter sweeps anything left unrouted into Effects. Without a
/// mixer Master falls back to AudioListener.volume and the Music / Effects sliders are hidden (<see cref="HasMixer"/>).
/// </summary>
public static class GameSettings
{
    public enum WindowModeKind { Fullscreen, Exclusive, Windowed }
    public enum QualityTier { Low, Medium, High }
    public enum ShadowTier { Off, Low, High }
    public enum FogTier { Off, Low, Normal }

    public const string MasterParam = "MasterVolume";
    public const string MusicParam = "MusicVolume";
    public const string EffectsParam = "EffectsVolume";
    public const string MusicGroupName = "Music";
    public const string EffectsGroupName = "Effects";

    public static readonly int[] FrameCaps = { 30, 60, 120, 144, 0 }; // 0 = unlimited
    public const float MinBrightness = -0.5f, MaxBrightness = 0.5f;
    public const float MinMouseSensitivity = 0.25f, MaxMouseSensitivity = 3f;
    public const float MinFov = 60f, MaxFov = 90f, DefaultFov = 70f;

    private const string Prefix = "lightsout.settings.";

    /// <summary>Raised after any setter or reset. Subscribers must unsubscribe in OnDisable / OnDestroy.</summary>
    public static event Action Changed;

    // ---------------------------------------------------------------- values

    private static WindowModeKind _windowMode;
    private static int _resW, _resH;                  // 0 = native
    private static bool _vSync;
    private static int _frameCap;                     // FrameCaps value
    private static float _brightness;
    private static QualityTier _quality;
    private static ShadowTier _shadows;
    private static bool _ambientOcclusion;
    private static FogTier _fog;
    private static float _master, _music, _effects;
    private static float _mouseSensitivity;
    private static bool _invertY;
    private static float _fov;
    private static float _cameraShake;
    private static float _effectsIntensity;

    public static WindowModeKind WindowMode { get => _windowMode; set { _windowMode = value; Store("WindowMode", (int)value); ApplyDisplay(); Raise(); } }
    public static bool VSync { get => _vSync; set { _vSync = value; Store("VSync", value ? 1 : 0); ApplyFrame(); Raise(); } }
    public static int FrameCap { get => _frameCap; set { _frameCap = Array.IndexOf(FrameCaps, value) >= 0 ? value : 0; Store("FrameCap", _frameCap); ApplyFrame(); Raise(); } }
    public static float Brightness { get => _brightness; set { _brightness = Mathf.Clamp(value, MinBrightness, MaxBrightness); Store("Brightness", _brightness); Raise(); } }
    public static QualityTier Quality { get => _quality; set { _quality = value; Store("Quality", (int)value); ApplyQuality(); Raise(); } }
    public static ShadowTier Shadows { get => _shadows; set { _shadows = value; Store("Shadows", (int)value); Raise(); } }
    public static bool AmbientOcclusion { get => _ambientOcclusion; set { _ambientOcclusion = value; Store("AmbientOcclusion", value ? 1 : 0); Raise(); } }
    public static FogTier Fog { get => _fog; set { _fog = value; Store("Fog", (int)value); Raise(); } }
    public static float MasterVolume { get => _master; set { _master = Mathf.Clamp01(value); Store("MasterVolume", _master); ApplyAudio(); Raise(); } }
    public static float MusicVolume { get => _music; set { _music = Mathf.Clamp01(value); Store("MusicVolume", _music); ApplyAudio(); Raise(); } }
    public static float EffectsVolume { get => _effects; set { _effects = Mathf.Clamp01(value); Store("EffectsVolume", _effects); ApplyAudio(); Raise(); } }
    public static float MouseSensitivity { get => _mouseSensitivity; set { _mouseSensitivity = Mathf.Clamp(value, MinMouseSensitivity, MaxMouseSensitivity); Store("MouseSensitivity", _mouseSensitivity); Raise(); } }
    public static bool InvertY { get => _invertY; set { _invertY = value; Store("InvertY", value ? 1 : 0); Raise(); } }
    public static float FieldOfView { get => _fov; set { _fov = Mathf.Clamp(value, MinFov, MaxFov); Store("FieldOfView", _fov); Raise(); } }
    /// <summary>S9: the chosen UI language code ("en", "es", "fr", "de"); empty = follow the system language. Not reset by any settings tab, not in SaveData.
    /// Persisting only: <see cref="L10n.SetLanguage"/> is what switches the locale.</summary>
    public static string Language { get => _language; set { _language = value ?? ""; Store("Language", _language); Raise(); } }
    private static string _language = "";
    public static float CameraShake { get => _cameraShake; set { _cameraShake = Mathf.Clamp01(value); Store("CameraShake", _cameraShake); Raise(); } }
    /// <summary>F104 B1: strength of the screen effects that grow with the DreadMeter (film grain, vignette, colour fringing, desaturation), 0..1.</summary>
    public static float EffectsIntensity { get => _effectsIntensity; set { _effectsIntensity = Mathf.Clamp01(value); Store("EffectsIntensity", _effectsIntensity); Raise(); } }

    /// <summary>The touch controls mode / look sensitivity stay stored by <see cref="TouchInput"/> (their existing prefs); these wrappers just raise Changed.</summary>
    public static TouchInput.Mode TouchControls { get => TouchInput.CurrentMode; set { TouchInput.SetMode(value); Raise(); } }
    public static float TouchLookSensitivity { get => TouchInput.LookSensitivity; set { TouchInput.SetLookSensitivity(value); Raise(); } }

    /// <summary>Fraction of the fog particles to keep: 0 off, 0.5 low, 1 normal.</summary>
    public static float FogFactor => _fog == FogTier.Off ? 0f : _fog == FogTier.Low ? 0.5f : 1f;

    /// <summary>Window mode, resolution and frame cap mean something only on a desktop player.</summary>
    public static bool DisplayAvailable => Application.platform != RuntimePlatform.WebGLPlayer && !Application.isMobilePlatform;

    /// <summary>The last display request made (the Editor logs it instead of calling Screen.SetResolution): "1920x1080 FullScreenWindow".</summary>
    public static string LastDisplayRequest { get; private set; } = "";

    // ---------------------------------------------------------------- load / defaults

    static GameSettings()
    {
        Load();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Changed = null;
        _mixer = null;
        _musicGroup = null;
        _effectsGroup = null;
        _hasMixer = false;
        MusicSources.Clear();
        _renderScaleBackups.Clear();
        Load();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void ApplyAtLoad()
    {
        Application.quitting -= RestoreAssets;
        Application.quitting += RestoreAssets;
        if (PlayerPrefs.HasKey(Prefix + "WindowMode") || PlayerPrefs.HasKey(Prefix + "ResW")) ApplyDisplay(); // never touch the Player Settings' window until the player has chosen
        ApplyFrame();
        ApplyQuality();
        ApplyAudio();
    }

    private static bool DefaultLowQuality => Application.isMobilePlatform || Application.platform == RuntimePlatform.WebGLPlayer;

    private static void Load()
    {
        _language = Str("Language", "");
        _windowMode = (WindowModeKind)Mathf.Clamp(Int("WindowMode", 0), 0, 2);
        _resW = Mathf.Max(0, Int("ResW", 0));
        _resH = Mathf.Max(0, Int("ResH", 0));
        _vSync = Int("VSync", 1) != 0;
        int cap = Int("FrameCap", 0);
        _frameCap = Array.IndexOf(FrameCaps, cap) >= 0 ? cap : 0;
        _brightness = Mathf.Clamp(Float("Brightness", 0f), MinBrightness, MaxBrightness);
        _quality = (QualityTier)Mathf.Clamp(Int("Quality", DefaultLowQuality ? 0 : 2), 0, 2);
        _shadows = (ShadowTier)Mathf.Clamp(Int("Shadows", 2), 0, 2);
        _ambientOcclusion = Int("AmbientOcclusion", Application.isMobilePlatform ? 0 : 1) != 0;
        _fog = (FogTier)Mathf.Clamp(Int("Fog", 2), 0, 2);
        _master = Mathf.Clamp01(Float("MasterVolume", 1f));
        _music = Mathf.Clamp01(Float("MusicVolume", 0.8f));
        _effects = Mathf.Clamp01(Float("EffectsVolume", 1f));
        _mouseSensitivity = Mathf.Clamp(Float("MouseSensitivity", 1f), MinMouseSensitivity, MaxMouseSensitivity);
        _invertY = Int("InvertY", 0) != 0;
        _fov = Mathf.Clamp(Float("FieldOfView", DefaultFov), MinFov, MaxFov);
        _cameraShake = Mathf.Clamp01(Float("CameraShake", 1f));
        _effectsIntensity = Mathf.Clamp01(Float("EffectsIntensity", 1f));
    }

    private static int Int(string name, int fallback)
    {
        try { return PlayerPrefs.GetInt(Prefix + name, fallback); } catch { return fallback; }
    }

    private static float Float(string name, float fallback)
    {
        try { return PlayerPrefs.GetFloat(Prefix + name, fallback); } catch { return fallback; }
    }

    private static void Store(string name, int value)
    {
        try { PlayerPrefs.SetInt(Prefix + name, value); PlayerPrefs.Save(); } catch { /* the in-memory value still applies this session */ }
    }

    private static void Store(string name, float value)
    {
        try { PlayerPrefs.SetFloat(Prefix + name, value); PlayerPrefs.Save(); } catch { /* the in-memory value still applies this session */ }
    }

    private static void Store(string name, string value)
    {
        try { PlayerPrefs.SetString(Prefix + name, value); PlayerPrefs.Save(); } catch { /* the in-memory value still applies this session */ }
    }

    private static string Str(string name, string fallback)
    {
        try { return PlayerPrefs.GetString(Prefix + name, fallback); } catch { return fallback; }
    }

    private static void Raise() => Changed?.Invoke();

    // ---------------------------------------------------------------- resets (one per settings tab)

    public static void ResetDisplay()
    {
        _windowMode = WindowModeKind.Fullscreen; Store("WindowMode", 0);
        _resW = 0; _resH = 0; Store("ResW", 0); Store("ResH", 0);
        _vSync = true; Store("VSync", 1);
        _frameCap = 0; Store("FrameCap", 0);
        ApplyDisplay();
        ApplyFrame();
        Raise();
    }

    public static void ResetGraphics()
    {
        _quality = DefaultLowQuality ? QualityTier.Low : QualityTier.High; Store("Quality", (int)_quality);
        _shadows = ShadowTier.High; Store("Shadows", 2);
        _ambientOcclusion = !Application.isMobilePlatform; Store("AmbientOcclusion", _ambientOcclusion ? 1 : 0);
        _fog = FogTier.Normal; Store("Fog", 2);
        _brightness = 0f; Store("Brightness", 0f);
        _fov = DefaultFov; Store("FieldOfView", _fov);
        _cameraShake = 1f; Store("CameraShake", 1f);
        _effectsIntensity = 1f; Store("EffectsIntensity", 1f);
        ApplyQuality();
        Raise();
    }

    public static void ResetAudio()
    {
        _master = 1f; Store("MasterVolume", 1f);
        _music = 0.8f; Store("MusicVolume", 0.8f);
        _effects = 1f; Store("EffectsVolume", 1f);
        ApplyAudio();
        Raise();
    }

    public static void ResetControls()
    {
        _mouseSensitivity = 1f; Store("MouseSensitivity", 1f);
        _invertY = false; Store("InvertY", 0);
        TouchInput.SetMode(TouchInput.Mode.Auto);
        TouchInput.SetLookSensitivity(TouchInput.DefaultLookSensitivity);
        Raise();
    }

    // ---------------------------------------------------------------- display

    /// <summary>Distinct width x height pairs, ascending, each at its highest refresh rate. Empty off desktop.</summary>
    public static List<Vector2Int> Resolutions()
    {
        var best = new Dictionary<Vector2Int, double>();
        foreach (Resolution r in Screen.resolutions)
        {
            Vector2Int key = new Vector2Int(r.width, r.height);
            double hz = r.refreshRateRatio.value;
            if (!best.TryGetValue(key, out double have) || hz > have) best[key] = hz;
        }
        var list = new List<Vector2Int>(best.Keys);
        list.Sort((a, b) => a.x != b.x ? a.x.CompareTo(b.x) : a.y.CompareTo(b.y));
        return list;
    }

    private static Vector2Int NativeResolution()
    {
        try
        {
            Display main = Display.main;
            if (main != null && main.systemWidth > 0) return new Vector2Int(main.systemWidth, main.systemHeight);
        }
        catch { /* fall through */ }
        Resolution current = Screen.currentResolution;
        return new Vector2Int(current.width, current.height);
    }

    /// <summary>Index into <see cref="Resolutions"/> of the chosen size (native when none is stored; the closest one below if the stored size is gone).</summary>
    public static int ResolutionIndex(List<Vector2Int> list)
    {
        if (list.Count == 0) return 0;
        Vector2Int want = _resW > 0 && _resH > 0 ? new Vector2Int(_resW, _resH) : NativeResolution();
        int pick = list.Count - 1;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] == want) return i;
            if (list[i].x * list[i].y <= want.x * want.y) pick = i;
        }
        return pick;
    }

    public static void SetResolution(Vector2Int size)
    {
        _resW = size.x; _resH = size.y;
        Store("ResW", size.x); Store("ResH", size.y);
        ApplyDisplay();
        Raise();
    }

    private static void ApplyDisplay()
    {
        if (!DisplayAvailable) return;
        FullScreenMode mode = _windowMode == WindowModeKind.Fullscreen ? FullScreenMode.FullScreenWindow
            : _windowMode == WindowModeKind.Exclusive ? FullScreenMode.ExclusiveFullScreen : FullScreenMode.Windowed;
        Vector2Int native = NativeResolution();
        int w = _resW > 0 ? _resW : native.x, h = _resH > 0 ? _resH : native.y;
        if (w <= 0 || h <= 0) return;
        LastDisplayRequest = $"{w}x{h} {mode}";
#if UNITY_EDITOR
        Debug.Log("GameSettings: display request (not applied in the Editor) " + LastDisplayRequest);
#else
        Screen.SetResolution(w, h, mode);
#endif
    }

    private static void ApplyFrame()
    {
        QualitySettings.vSyncCount = _vSync ? 1 : 0;
        if (Application.isMobilePlatform && !PlayerPrefs.HasKey(Prefix + "FrameCap")) return; // the platform's own cap stays until the player picks one
        Application.targetFrameRate = _frameCap > 0 ? _frameCap : (Application.platform == RuntimePlatform.WebGLPlayer && !_vSync ? 60 : -1);
    }

    // ---------------------------------------------------------------- quality

    private static readonly Dictionary<UniversalRenderPipelineAsset, float> _renderScaleBackups = new Dictionary<UniversalRenderPipelineAsset, float>();
    private static int _qualityLevelBackup = -1;

    private static int FindQualityLevel(string name)
    {
        string[] names = QualitySettings.names;
        for (int i = 0; i < names.Length; i++) if (names[i] == name) return i;
        return -1;
    }

    /// <summary>
    /// Low = the Mobile quality level (its own pipeline asset, render scale 0.8) where the platform has one - the Windows standalone player only
    /// lists PC, so there Low stays on PC at render scale 0.7. Medium / High = the PC level at render scale 0.85 / 1.0. The render scale is written
    /// on the live pipeline asset and put back by <see cref="RestoreAssets"/> when the player quits.
    /// </summary>
    private static void ApplyQuality()
    {
        if (_qualityLevelBackup < 0) _qualityLevelBackup = QualitySettings.GetQualityLevel();
        int mobile = FindQualityLevel("Mobile"), pc = FindQualityLevel("PC");
        int level = _quality == QualityTier.Low && mobile >= 0 ? mobile : pc >= 0 ? pc : QualitySettings.GetQualityLevel();
        if (QualitySettings.GetQualityLevel() != level) QualitySettings.SetQualityLevel(level, true);
        QualitySettings.vSyncCount = _vSync ? 1 : 0; // a level change reloads the level's own vSync

        bool ownLowAsset = _quality == QualityTier.Low && level == mobile && mobile >= 0;
        if (!ownLowAsset && !MobilePerformance.IsMobileProfile)
        {
            UniversalRenderPipelineAsset asset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (asset != null)
            {
                if (!_renderScaleBackups.ContainsKey(asset)) _renderScaleBackups[asset] = asset.renderScale;
                asset.renderScale = _quality == QualityTier.Low ? 0.7f : _quality == QualityTier.Medium ? 0.85f : 1f;
            }
        }
    }

    /// <summary>Leaving Play (or the player) puts back what the assets held: the render scale lives on a shared asset the Editor could otherwise save.</summary>
    private static void RestoreAssets()
    {
        foreach (KeyValuePair<UniversalRenderPipelineAsset, float> pair in _renderScaleBackups)
        {
            if (pair.Key != null) pair.Key.renderScale = pair.Value;
        }
        _renderScaleBackups.Clear();
        if (_qualityLevelBackup >= 0 && _qualityLevelBackup < QualitySettings.names.Length && QualitySettings.GetQualityLevel() != _qualityLevelBackup) QualitySettings.SetQualityLevel(_qualityLevelBackup, true);
        _qualityLevelBackup = -1;
    }

    // ---------------------------------------------------------------- audio

    private static AudioMixer _mixer;
    private static AudioMixerGroup _musicGroup, _effectsGroup;
    private static bool _hasMixer;
    private static readonly HashSet<AudioSource> MusicSources = new HashSet<AudioSource>();

    public static bool HasMixer => _hasMixer;
    public static AudioMixerGroup MusicGroup => _musicGroup;
    public static AudioMixerGroup EffectsGroup => _effectsGroup;

    /// <summary>Called first thing in MazeGenerator.Awake. A mixer without the Music / Effects groups or the three exposed parameters counts as no mixer.</summary>
    public static void BindMixer(AudioMixer mixer)
    {
        _mixer = null; _musicGroup = null; _effectsGroup = null; _hasMixer = false;
        if (mixer != null)
        {
            AudioMixerGroup[] music = mixer.FindMatchingGroups(MusicGroupName);
            AudioMixerGroup[] effects = mixer.FindMatchingGroups(EffectsGroupName);
            bool ok = music.Length > 0 && effects.Length > 0 &&
                      mixer.GetFloat(MasterParam, out _) && mixer.GetFloat(MusicParam, out _) && mixer.GetFloat(EffectsParam, out _);
            if (ok)
            {
                _mixer = mixer; _musicGroup = music[0]; _effectsGroup = effects[0]; _hasMixer = true;
            }
            else Debug.LogWarning("GameSettings: the mixer is missing the Music / Effects groups or the exposed volume parameters - run LIGHTS OUT > Build Missing Pieces. Falling back to AudioListener.volume.");
        }
        else Debug.LogWarning("GameSettings: MazeGenerator has no audio mixer assigned (LIGHTS OUT > Build Missing Pieces): only the Master volume works.");

        foreach (AudioSource s in MusicSources) if (s != null) s.outputAudioMixerGroup = _musicGroup;
        ApplyAudio();
    }

    /// <summary>Marks `source` as music: it is sent to the Music group (now, or as soon as a mixer is bound) and the Effects sweep leaves it alone.</summary>
    public static void RouteMusic(AudioSource source)
    {
        if (source == null) return;
        MusicSources.Add(source);
        source.outputAudioMixerGroup = _musicGroup;
    }

    /// <summary>Sends `source` to the Effects group (a no-op without a mixer).</summary>
    public static void RouteEffect(AudioSource source)
    {
        if (source != null && !MusicSources.Contains(source)) source.outputAudioMixerGroup = _effectsGroup;
    }

    /// <summary>Everything that still has no output group goes to Effects. AudioRouter calls this on a timer.</summary>
    public static void SweepToEffects()
    {
        MusicSources.RemoveWhere(s => s == null);
        if (_effectsGroup == null) return;
        foreach (AudioSource s in UnityEngine.Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include))
        {
            if (s.outputAudioMixerGroup == null && !MusicSources.Contains(s)) s.outputAudioMixerGroup = _effectsGroup;
        }
    }

    public static float ToDecibels(float volume) => 20f * Mathf.Log10(Mathf.Max(volume, 0.0001f));

    /// <summary>Pushes the three volumes into the mixer (Master into AudioListener.volume without one). Also re-run by AudioRouter once the mixer is live.</summary>
    public static void ApplyAudio()
    {
        if (_hasMixer && _mixer != null)
        {
            _mixer.SetFloat(MasterParam, ToDecibels(_master));
            _mixer.SetFloat(MusicParam, ToDecibels(_music));
            _mixer.SetFloat(EffectsParam, ToDecibels(_effects));
            AudioListener.volume = 1f;
        }
        else AudioListener.volume = _master;
    }

    /// <summary>The mixer's current value of an exposed parameter, in dB (test / debug read-back). NaN without a mixer.</summary>
    public static float MixerDecibels(string param)
    {
        return _mixer != null && _mixer.GetFloat(param, out float v) ? v : float.NaN;
    }
}
