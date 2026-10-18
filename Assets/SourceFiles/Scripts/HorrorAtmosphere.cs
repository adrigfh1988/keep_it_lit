using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Takes the lights out. Everything here is a play-mode RenderSettings change, so it reverts when you
/// stop - the scene's authored lighting is never touched.
/// </summary>
[DefaultExecutionOrder(-50)]
public class HorrorAtmosphere : MonoBehaviour
{
    [Header("Ambient")]
    [SerializeField] private Color ambientLight = new Color(0.18f, 0.18f, 0.22f);

    [Header("Fog")]
    [SerializeField] private bool enableFog = true;
    [SerializeField] private Color fogColor = new Color(0.025f, 0.028f, 0.038f);
    [Tooltip("Exponential-squared fog reaches near-total opacity around 3/density metres. 0.07 ~ 43 m, 0.12 ~ 25 m. Lower = you see further.")]
    [SerializeField] private float fogDensity = 0.020f;

    [Header("Lights")]
    [Tooltip("Intensity for the scene's directional light. Raise it at runtime to see the maze while tuning.")]
    [SerializeField] private float directionalLightIntensity = 0f;

    [Header("Post")]
    [Tooltip("The scene profile ships +0.2 exposure, which fights all of the above. This neutralises it on a runtime copy of the profile - the asset on disk is not touched.")]
    [SerializeField] private float postExposure = 0.85f;
    [Tooltip("F71: crushes mid-tones so the ambient floor reads as inky rather than grey, on the same runtime profile copy as postExposure above.")]
    [SerializeField] private float contrast = 0f;
    [Tooltip("F71: Neutral keeps the torch's hot core readable against the crushed shadows below - ACES' filmic roll-off reads muddier once everything is this dark.")]
    [SerializeField] private TonemappingMode tonemappingMode = TonemappingMode.Neutral;
    [Tooltip("F71: darkens the screen edges so the beam's small lit circle reads as a circle, not a rectangle")]
    [SerializeField] private float vignetteIntensity = 0.14f;
    [Tooltip("How gradual the vignette's falloff is - low is a hard-edged circle, high fades in gently")]
    [SerializeField] private float vignetteSmoothness = 0.4f;
    [Tooltip("Tint of the vignette - black reads as darkness, not a colour cast")]
    [SerializeField] private Color vignetteColor = Color.black;

    [Header("F104 B1 - dread")]
    [Tooltip("Vignette intensity at full dread (the base vignetteIntensity at zero)")]
    [SerializeField] private float dreadVignetteMax = 0.5f;
    [Tooltip("Film grain intensity at full dread")]
    [SerializeField] private float dreadGrainMax = 0.5f;
    [Tooltip("Chromatic aberration intensity at full dread. It only starts above dreadCaThreshold and is off on mobile.")]
    [SerializeField] private float dreadCaMax = 0.35f;
    [Tooltip("Dread level (after the screen-effects setting) above which colour fringing starts")]
    [SerializeField] private float dreadCaThreshold = 0.7f;
    [Tooltip("Colour-adjustment saturation at full dread (negative drains colour)")]
    [SerializeField] private float dreadDesaturation = -15f;
    [Tooltip("Extra vignette added on each heartbeat, decaying over heartbeatPulseSeconds")]
    [SerializeField] private float heartbeatPulseAmount = 0.05f;
    [SerializeField] private float heartbeatPulseSeconds = 0.25f;
    [Tooltip("Values are only written to the volume when they move by more than this")]
    [SerializeField] private float writeEpsilon = 0.002f;

    // Base values Darken lerps away from. Captured in Awake from the serialized defaults, then
    // overwritten by ApplyProfile if a floor profile is in use - either way Darken has a real base.
    private Color _baseAmbient;
    private float _baseFogDensity;
    private Coroutine _darkenRoutine;
    // S2: the runtime volume profile's colour adjustments, so the Brightness setting can offset postExposure live
    private readonly System.Collections.Generic.List<ColorAdjustments> _exposures = new System.Collections.Generic.List<ColorAdjustments>();
    // F104 B1: the runtime clones' dread-driven overrides, and what was last written to them (NaN = nothing yet).
    private readonly System.Collections.Generic.List<Vignette> _vignettes = new System.Collections.Generic.List<Vignette>();
    private readonly System.Collections.Generic.List<FilmGrain> _grains = new System.Collections.Generic.List<FilmGrain>();
    private readonly System.Collections.Generic.List<ChromaticAberration> _aberrations = new System.Collections.Generic.List<ChromaticAberration>();
    private DreadMeter _dread;
    private HorrorAudioDirector _audio;
    private float _pulse;
    private float _writtenVignette = float.NaN;
    private float _writtenGrain = float.NaN;
    private float _writtenAberration = float.NaN;
    private float _writtenSaturation = float.NaN;
    private float _baseSaturation;

    private void OnEnable()
    {
        GameSettings.Changed += ApplyBrightness;
        if (_audio != null) _audio.HeartBeat += HandleHeartBeat;
    }

    private void OnDisable()
    {
        GameSettings.Changed -= ApplyBrightness;
        if (_audio != null) _audio.HeartBeat -= HandleHeartBeat;
    }

    /// <summary>
    /// F104 B1: hands over the DreadMeter that tunnels the vignette, grows film grain and colour fringing and drains colour on the
    /// runtime profile clone, plus the director whose heartbeat pulses the vignette. Called before Start, so the grain and fringing
    /// overrides are added with the rest in NeutraliseExposure.
    /// </summary>
    public void BindDread(DreadMeter meter, HorrorAudioDirector audio)
    {
        if (_audio != null) _audio.HeartBeat -= HandleHeartBeat;
        _dread = meter;
        _audio = audio;
        if (_audio != null && isActiveAndEnabled) _audio.HeartBeat += HandleHeartBeat;
    }

    private void HandleHeartBeat() => _pulse = heartbeatPulseAmount;

    private void Update()
    {
        if (_dread == null || _vignettes.Count == 0) return;

        if (_pulse > 0f) _pulse = Mathf.MoveTowards(_pulse, 0f, Time.deltaTime * heartbeatPulseAmount / Mathf.Max(0.0001f, heartbeatPulseSeconds));

        // The effects setting scales the whole dread contribution, the heartbeat pulse included. No active meter = the base values.
        float strength = _dread.IsActive ? GameSettings.EffectsIntensity : 0f;
        float k = _dread.Smoothed * strength;

        float aberration = k > dreadCaThreshold && !MobilePerformance.IsMobileProfile
            ? Mathf.InverseLerp(dreadCaThreshold, 1f, k) * dreadCaMax
            : 0f;

        if (Changed(ref _writtenVignette, Mathf.Lerp(vignetteIntensity, dreadVignetteMax, k) + _pulse * strength))
        {
            foreach (Vignette vignette in _vignettes) vignette.intensity.value = _writtenVignette;
        }
        if (Changed(ref _writtenGrain, Mathf.Lerp(0f, dreadGrainMax, k)))
        {
            foreach (FilmGrain grain in _grains) grain.intensity.value = _writtenGrain;
        }
        if (Changed(ref _writtenAberration, aberration))
        {
            foreach (ChromaticAberration fringe in _aberrations) fringe.intensity.value = _writtenAberration;
        }
        if (Changed(ref _writtenSaturation, _baseSaturation + Mathf.Lerp(0f, dreadDesaturation, k)))
        {
            foreach (ColorAdjustments adjustments in _exposures) adjustments.saturation.value = _writtenSaturation;
        }
    }

    /// <summary>Stores `value` in `written` and returns true when it moved by more than writeEpsilon (or nothing was written yet).</summary>
    private bool Changed(ref float written, float value)
    {
        if (!float.IsNaN(written) && Mathf.Abs(value - written) <= writeEpsilon) return false;

        written = value;
        return true;
    }

    /// <summary>S2: Brightness is an EV offset on top of this component's postExposure, applied to the same runtime profile clone (never a second volume).</summary>
    private void ApplyBrightness()
    {
        foreach (ColorAdjustments adjustments in _exposures)
        {
            if (adjustments != null) adjustments.postExposure.value = postExposure + GameSettings.Brightness;
        }
    }

    /// <summary>Per-floor light level, keeping whatever fog colour is already serialized. Awake has already applied the defaults, so this re-applies. Used by the primitive fallback path, which has no theme colour to hand in.</summary>
    public void ApplyProfile(Color ambient, float fogDensity)
    {
        ApplyProfile(ambient, fogDensity, fogColor);
    }

    /// <summary>
    /// Per-floor light level and fog colour. Awake has already applied the defaults, so this re-applies.
    /// Also updates the camera's background colour when the camera already exists - SetUpAtmosphere runs
    /// after FirstPersonRig, so in practice it always does - so SealTheSkybox's job stays consistent with
    /// a themed fog colour instead of whatever the serialized default was.
    /// </summary>
    public void ApplyProfile(Color ambient, float fogDensity, Color fogColor)
    {
        ambientLight = ambient;
        this.fogDensity = fogDensity;
        this.fogColor = fogColor;
        _baseAmbient = ambient;
        _baseFogDensity = fogDensity;
        RenderSettings.ambientLight = ambient;
        if (enableFog)
        {
            RenderSettings.fogDensity = fogDensity;
            RenderSettings.fogColor = fogColor;
        }

        Camera camera = Camera.main;
        if (camera != null) camera.backgroundColor = fogColor;
    }

    /// <summary>
    /// F27: lerps ambient/fog toward a darker target over `seconds`. Absolute, not cumulative - a
    /// fraction of 0.35 then 0.6 is monotonic, since each lerps from the untouched base rather than
    /// from wherever the previous call left off.
    /// </summary>
    public void Darken(float fraction, float seconds)
    {
        fraction = Mathf.Clamp01(fraction);
        if (_darkenRoutine != null) StopCoroutine(_darkenRoutine);
        _darkenRoutine = StartCoroutine(DarkenRoutine(fraction, Mathf.Max(0.01f, seconds)));
    }

    private System.Collections.IEnumerator DarkenRoutine(float fraction, float seconds)
    {
        Color startAmbient = RenderSettings.ambientLight;
        float startFog = RenderSettings.fogDensity;

        Color targetAmbient = _baseAmbient * (1f - 0.5f * fraction);
        float targetFog = _baseFogDensity * (1f + 0.5f * fraction);

        float t = 0f;
        while (t < seconds)
        {
            if (GameOutcome.IsOver) yield break;

            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            RenderSettings.ambientLight = Color.Lerp(startAmbient, targetAmbient, k);
            if (enableFog) RenderSettings.fogDensity = Mathf.Lerp(startFog, targetFog, k);
            yield return null;
        }

        RenderSettings.ambientLight = targetAmbient;
        if (enableFog) RenderSettings.fogDensity = targetFog;
        _darkenRoutine = null;
    }

    private void Awake()
    {
        _baseAmbient = ambientLight;
        _baseFogDensity = fogDensity;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = ambientLight;

        // Without this the maze stays visibly grey no matter what else is off: the skybox reflection
        // cubemap keeps lighting the walls, which are glossy enough to show it.
        RenderSettings.defaultReflectionMode = DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.reflectionIntensity = 0f;

        if (enableFog)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogDensity = fogDensity;
        }

        foreach (Light light in FindObjectsByType<Light>(FindObjectsInactive.Exclude))
        {
            if (light.type == LightType.Directional)
            {
                light.intensity = directionalLightIntensity;
            }
        }
    }

    private void Start()
    {
        // Start, not Awake: the camera has been reparented by FirstPersonRig and the Volume has run
        // its own OnEnable by now.
        SealTheSkybox();
        NeutraliseExposure();
    }

    private void SealTheSkybox()
    {
        // Unity's fog never touches the skybox, so without this the world fades toward the fog colour
        // while the background stays a different one. The maze has a ceiling, but gaps and the moment
        // of spawning still show it.
        Camera camera = Camera.main;
        if (camera == null) return;

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = fogColor;
    }

    private void NeutraliseExposure()
    {
        postExposure = Mathf.Max(postExposure, 0.85f);
        contrast = Mathf.Min(contrast, 0f);
        vignetteIntensity = Mathf.Min(vignetteIntensity, 0.14f);

        foreach (Volume volume in FindObjectsByType<Volume>(FindObjectsInactive.Include))
        {
            // volume.profile (not sharedProfile) lazily clones the asset, so every edit below lives only
            // for this play session and leaves the .asset on disk clean.
            if (volume.profile == null) continue;

            if (volume.profile.TryGet(out ColorAdjustments colorAdjustments))
            {
                colorAdjustments.postExposure.overrideState = true;
                colorAdjustments.postExposure.value = postExposure + GameSettings.Brightness;
                _exposures.Add(colorAdjustments);
                colorAdjustments.contrast.overrideState = true;
                colorAdjustments.contrast.value = contrast;
                colorAdjustments.saturation.overrideState = true;
                if (_exposures.Count == 1) _baseSaturation = colorAdjustments.saturation.value;
            }

            // F71: Tonemapping/Vignette are not on the shipped profile at all (decision D13 - no asset
            // YAML edits), so Add them to this runtime clone the first time through.
            if (!volume.profile.TryGet(out Tonemapping tonemapping)) tonemapping = volume.profile.Add<Tonemapping>(true);
            tonemapping.mode.overrideState = true;
            tonemapping.mode.value = tonemappingMode;

            if (!volume.profile.TryGet(out Vignette vignette)) vignette = volume.profile.Add<Vignette>(true);
            vignette.intensity.overrideState = true;
            vignette.intensity.value = vignetteIntensity;
            vignette.smoothness.overrideState = true;
            vignette.smoothness.value = vignetteSmoothness;
            vignette.color.overrideState = true;
            vignette.color.value = vignetteColor;
            _vignettes.Add(vignette);

            // F104 B1: grain and fringing only exist when the dread meter drives them; Update writes their intensity.
            if (_dread != null)
            {
                if (!volume.profile.TryGet(out FilmGrain grain)) grain = volume.profile.Add<FilmGrain>(true);
                grain.type.overrideState = true;
                grain.type.value = FilmGrainLookup.Medium1;
                grain.intensity.overrideState = true;
                grain.intensity.value = 0f;
                _grains.Add(grain);

                if (!volume.profile.TryGet(out ChromaticAberration fringe)) fringe = volume.profile.Add<ChromaticAberration>(true);
                fringe.intensity.overrideState = true;
                fringe.intensity.value = 0f;
                _aberrations.Add(fringe);
            }

            // F126: URP Bloom halation on emissive bulbs/eyes (skipped on mobile) and institutional SplitToning color script.
            if (!MobilePerformance.IsMobileProfile)
            {
                if (!volume.profile.TryGet(out Bloom bloom)) bloom = volume.profile.Add<Bloom>(true);
                bloom.threshold.overrideState = true;
                bloom.threshold.value = 1.05f;
                bloom.intensity.overrideState = true;
                bloom.intensity.value = 0.42f * GameSettings.EffectsIntensity;
                bloom.scatter.overrideState = true;
                bloom.scatter.value = 0.65f;
                bloom.tint.overrideState = true;
                bloom.tint.value = new Color(1.0f, 0.93f, 0.82f);
            }

            if (!volume.profile.TryGet(out SplitToning splitToning)) splitToning = volume.profile.Add<SplitToning>(true);
            splitToning.shadows.overrideState = true;
            splitToning.shadows.value = new Color(0.38f, 0.45f, 0.56f);
            splitToning.highlights.overrideState = true;
            splitToning.highlights.value = new Color(1.0f, 0.90f, 0.72f);
            splitToning.balance.overrideState = true;
            splitToning.balance.value = -18f;
        }
    }
}
