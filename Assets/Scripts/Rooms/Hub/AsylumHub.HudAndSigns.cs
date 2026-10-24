using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public partial class AsylumHub
{
    private bool _shopLineShown, _shopVisited;
    private Color _overlayColor = Color.black;

    /// <summary>
    /// F100: the readmission slip and the arrival both offer the shop only when it is worth the walk: the Commissary will be open for the next
    /// floor and the wallet holds at least the cheapest thing that can still be bought (at the prices of `clearedFloor`, the floor just cleared).
    /// </summary>
    public static bool ShopWorthAVisit(int clearedFloor)
    {
        if (!ShopInHub || clearedFloor < 1 || !HubShop.OpenFor(clearedFloor + 1)) return false;
        int cheapest = ShopCatalogue.CheapestOpenPrice(clearedFloor);
        return cheapest >= 0 && PlayerWallet.Shards >= cheapest;
    }

    /// <summary>F100: plays after the line on screen has finished (HUD queue).</summary>
    public void QueueSubtitleLine(string text, float seconds) { if (_hud != null) _hud.QueueSubtitle(text, seconds); }

    /// <summary>F100/F116: the player has been inside the Commissary - the arrival line that mentions it is not repeated.</summary>
    public void MarkShopVisited() { _shopVisited = true; }

    public void SubtitleLine(string text, float seconds) { if (_hud != null) _hud.ShowSubtitle(text, seconds); }

    // ---------------------------------------------------------------- atmosphere (D9)

    private void CacheAtmosphere()
    {
        if (_hasCachedAtmosphere) return;
        _hasCachedAtmosphere = true;
        _ambientMode = RenderSettings.ambientMode;
        _ambientLight = RenderSettings.ambientLight;
        _fog = RenderSettings.fog;
        _fogMode = RenderSettings.fogMode;
        _fogDensity = RenderSettings.fogDensity;
        _fogColor = RenderSettings.fogColor;
    }

    private void ApplyHubAtmosphere()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        Color amb = hubAmbient;
        float max = Mathf.Max(amb.r, amb.g, amb.b);
        if (max > 0.0001f && max < 0.15f)
        {
            float s = 0.15f / max;
            amb = new Color(amb.r * s, amb.g * s, amb.b * s, 1f);
        }
        else if (max <= 0.0001f)
        {
            amb = new Color(0.15f, 0.15f, 0.18f, 1f);
        }
        RenderSettings.ambientLight = amb;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.ExponentialSquared;
        RenderSettings.fogDensity = Mathf.Min(hubFogDensity, 0.018f);
        RenderSettings.fogColor = hubFogColor;
    }

    private void RestoreAtmosphere()
    {
        if (!_hasCachedAtmosphere) return;
        _hasCachedAtmosphere = false;
        RenderSettings.ambientMode = _ambientMode;
        RenderSettings.ambientLight = _ambientLight;
        RenderSettings.fog = _fog;
        RenderSettings.fogMode = _fogMode;
        RenderSettings.fogDensity = _fogDensity;
        RenderSettings.fogColor = _fogColor;
    }

    // ---------------------------------------------------------------- blackout / overlay

    /// <summary>Fades the story blackout to `alpha` over `seconds` (unscaled). Used by set pieces for hard cuts.</summary>
    public IEnumerator BlackoutTo(float alpha, float seconds)
    {
        EnsureBlackout();
        float from = _blackout != null ? _blackout.color.a : 0f;
        yield return Fade(from, alpha, seconds);
        if (alpha <= 0.001f && _blackoutRoot != null && !IsTransitioning) _blackoutRoot.SetActive(false);
    }

    private void EnsureBlackout()
    {
        if (_blackoutRoot != null)
        {
            _blackoutRoot.SetActive(true);
            _blackoutRoot.transform.SetAsLastSibling();
            return;
        }
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;
        GameObject panel = RuntimeUi.CreatePanel(canvas.transform, "AsylumBlackout", Color.black);
        _blackoutRoot = panel;
        _blackout = panel.GetComponent<Image>();
        _blackout.raycastTarget = false;
        panel.transform.SetAsLastSibling();
    }

    private void SetBlackout(float alpha)
    {
        if (_blackout != null) _blackout.color = new Color(_overlayColor.r, _overlayColor.g, _overlayColor.b, alpha);
    }

    /// <summary>The finale's walk-out fades to a cold grey-white instead of black. `colour` stays until the next call.</summary>
    public IEnumerator OverlayTo(Color colour, float alpha, float seconds)
    {
        EnsureBlackout();
        _overlayColor = colour;
        float from = _blackout != null ? _blackout.color.a : 0f;
        yield return Fade(from, alpha, seconds);
    }

    private IEnumerator Fade(float from, float to, float seconds)
    {
        if (_blackout == null) yield break;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            SetBlackout(Mathf.Lerp(from, to, Mathf.Clamp01(t / seconds)));
            yield return null;
        }
        SetBlackout(to);
    }
}
