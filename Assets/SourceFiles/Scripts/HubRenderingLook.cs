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

    /// <summary>True while this component has the feature switched on.</summary>
    public static bool IsOn { get; private set; }

    private void OnEnable() => Apply(true);
    private void OnDisable() => Apply(false);
    private void OnDestroy() => Apply(false);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => IsOn = false;

    private void Apply(bool on)
    {
        if (darkVolume != null) darkVolume.enabled = on;
        if (ambientOcclusion == null) return;
        if (on && (Application.isMobilePlatform || MobilePerformance.IsMobileProfile)) return;
        if (ambientOcclusion.isActive != on) ambientOcclusion.SetActive(on);
        IsOn = on;
    }
}
