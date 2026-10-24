using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// F95: switches the Asylum hub's Screen Space Ambient Occlusion on while the hub is in play. SSAO is a URP renderer feature, which is
/// a property of the renderer asset (shared by every scene), so it lives in the asset saved INACTIVE and this turns it on only for the
/// hub: the mazes, the shop and the Intake render exactly as before.
///
/// It sits on the hub's Lights group, which AsylumHub.SetContentActive switches with the rest of the content, so OnEnable / OnDisable
/// are exactly "the hub appeared / went away" and no AsylumHub change is needed. OnDestroy (a hub destroyed mid-visit, Play ending)
/// switches it off as well so the Editor never keeps the asset active. Skipped on mobile / touch-first devices (the cost is fill-rate).
/// Added and wired by AsylumHubBuilder.LookRepair.
/// </summary>
public class HubRenderingLook : MonoBehaviour
{
    [Tooltip("The SSAO renderer feature (a sub-asset of the URP renderer). Left inactive in the asset; this component turns it on for the hub.")]
    public ScriptableRendererFeature ambientOcclusion;

    [Tooltip("F96: the hub-only darkening volume (vignette, exposure, desaturation); enabled with the hub, off everywhere else.")]
    public Volume darkVolume;

    [Tooltip("The hub fog's soft particles fade where they meet floors and walls, which needs the camera depth texture (off in the URP " +
             "asset): the main camera asks for it while the hub is in play, and goes back to the asset's setting after.")]
    public bool depthTextureForFog = true;

    /// <summary>True while this component has the feature switched on.</summary>
    public static bool IsOn { get; private set; }

    private UniversalAdditionalCameraData _depthCamera;
    private CameraOverrideOption _depthBefore;

    private ColorAdjustments _darkExposure;
    private float _darkExposureBase;
    private readonly System.Collections.Generic.Dictionary<Light, LightShadows> _shadowed = new System.Collections.Generic.Dictionary<Light, LightShadows>();

    private void OnEnable() { Apply(true); GameSettings.Changed += ApplySettings; ApplySettings(); }
    private void OnDisable() { GameSettings.Changed -= ApplySettings; RestoreShadows(); Apply(false); }
    private void OnDestroy() { GameSettings.Changed -= ApplySettings; Apply(false); }

    /// <summary>
    /// S2: the three settings the hub owns. Brightness offsets the dark volume's absolute post-exposure (the maze's HorrorAtmosphere offsets its own
    /// clone the same way, so the setting moves both consistently); Ambient occlusion switches the SSAO feature; Shadows Off drops the hub's few
    /// shadow-casting lights (remembered and restored, so Low / High read as the authored shadows).
    /// </summary>
    private void ApplySettings()
    {
        if (darkVolume != null)
        {
            if (_darkExposure == null && darkVolume.profile != null && darkVolume.profile.TryGet(out _darkExposure)) _darkExposureBase = _darkExposure.postExposure.value;
            if (_darkExposure != null) _darkExposure.postExposure.value = _darkExposureBase + GameSettings.Brightness;
        }

        if (ambientOcclusion != null && !(Application.isMobilePlatform || MobilePerformance.IsMobileProfile))
        {
            bool want = GameSettings.AmbientOcclusion && isActiveAndEnabled;
            if (ambientOcclusion.isActive != want) ambientOcclusion.SetActive(want);
            IsOn = want;
        }

        if (GameSettings.Shadows == GameSettings.ShadowTier.Off)
        {
            AsylumHub hub = GetComponentInParent<AsylumHub>(true);
            foreach (Light light in (hub != null ? hub.transform : transform).GetComponentsInChildren<Light>(true))
            {
                if (light.shadows == LightShadows.None || _shadowed.ContainsKey(light)) continue;
                _shadowed[light] = light.shadows;
                light.shadows = LightShadows.None;
            }
        }
        else RestoreShadows();
    }

    private void RestoreShadows()
    {
        foreach (var pair in _shadowed) if (pair.Key != null) pair.Key.shadows = pair.Value;
        _shadowed.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOn = false;

    private void Apply(bool on)
    {
        if (darkVolume != null) darkVolume.enabled = on;
        DepthForFog(on);
        if (ambientOcclusion == null) return;
        if (on && (Application.isMobilePlatform || MobilePerformance.IsMobileProfile)) return;
        bool wanted = on && GameSettings.AmbientOcclusion; // S2: the Ambient occlusion setting
        if (ambientOcclusion.isActive != wanted) ambientOcclusion.SetActive(wanted);
        IsOn = wanted;
    }

    private void DepthForFog(bool on)
    {
        if (on && depthTextureForFog && Application.isPlaying && _depthCamera == null)
        {
            Camera cam = Camera.main;
            if (cam == null || !cam.TryGetComponent(out _depthCamera)) return;
            _depthBefore = _depthCamera.requiresDepthOption;
            _depthCamera.requiresDepthOption = CameraOverrideOption.On;
        }
        else if (!on && _depthCamera != null)
        {
            _depthCamera.requiresDepthOption = _depthBefore;
            _depthCamera = null;
        }
    }
}
