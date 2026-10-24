using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared full-screen overlay fader used by <see cref="GameOutcome"/>, <see cref="IntakeRoom"/>, and <see cref="AsylumHub"/>.
/// </summary>
public static class ScreenFader
{
    /// <summary>Creates a full-screen non-raycast overlay <see cref="Image"/> as the last sibling of <paramref name="parent"/> (or the root canvas).</summary>
    public static Image CreateOverlay(string name, Color initialColor, Transform parent = null)
    {
        if (parent == null)
        {
            Canvas canvas = RuntimeUi.ResolveCanvas();
            if (canvas == null) return null;
            parent = canvas.transform;
        }

        GameObject panel = RuntimeUi.CreatePanel(parent, name, initialColor);
        Image img = panel.GetComponent<Image>();
        if (img != null) img.raycastTarget = false;
        panel.transform.SetAsLastSibling();
        return img;
    }

    /// <summary>Fades <paramref name="image"/> alpha from <paramref name="from"/> to <paramref name="to"/> over <paramref name="seconds"/>.</summary>
    public static IEnumerator Fade(Image image, float from, float to, float seconds, Transform alsoFade = null, bool unscaled = false, Color? baseColor = null)
    {
        CanvasGroup group = null;
        if (alsoFade != null)
        {
            group = alsoFade.GetComponent<CanvasGroup>();
            if (group == null) group = alsoFade.gameObject.AddComponent<CanvasGroup>();
        }

        Color rgb = baseColor ?? (image != null ? new Color(image.color.r, image.color.g, image.color.b, 1f) : Color.black);
        float duration = Mathf.Max(0.0001f, seconds);
        float t = 0f;
        while (t < seconds)
        {
            t += unscaled ? Time.unscaledDeltaTime : Time.deltaTime;
            float k = Mathf.Clamp01(t / duration);
            if (image != null) image.color = new Color(rgb.r, rgb.g, rgb.b, Mathf.Lerp(from, to, k));
            if (group != null) group.alpha = 1f - k;
            yield return null;
        }

        if (image != null) image.color = new Color(rgb.r, rgb.g, rgb.b, to);
        if (group != null) group.alpha = 0f;
    }
}
