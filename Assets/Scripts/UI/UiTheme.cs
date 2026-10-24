using UnityEngine;

/// <summary>
/// Shared procedural UI palette and corner-anchoring layout helpers used across menus, HUD, and outcome screens.
/// </summary>
public static class UiTheme
{
    public static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    public static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);
    public static readonly Color Accent = new Color(0.86f, 0.12f, 0.09f);
    public static readonly Color Dim = new Color(0.78f, 0.78f, 0.8f);
    public static readonly Color Grey = new Color(0.75f, 0.75f, 0.78f);
    public static readonly Color DarkGrey = new Color(0.55f, 0.55f, 0.58f);
    public static readonly Color Amber = new Color(1f, 0.78f, 0.45f);
    public static readonly Color Green = new Color(0.25f, 1f, 0.7f);
    public static readonly Color Red = new Color(0.9f, 0.15f, 0.1f);
    public static readonly Color ShardCyan = new Color(0.7f, 0.9f, 1f);
    public static readonly Color BackdropOpaque = new Color(0.02f, 0.02f, 0.03f, 1f);
    public static readonly Color BackdropDim = new Color(0f, 0f, 0.0f, 0.75f);
    public static readonly Color CardBg = new Color(0.08f, 0.08f, 0.10f, 0.96f);
    public static readonly Color ConfirmIdle = new Color(0.08f, 0.32f, 0.24f, 0.95f);
    public static readonly Color ConfirmHover = new Color(0.15f, 0.55f, 0.42f, 1f);

    /// <summary>Anchors and pivots <paramref name="rect"/> at <paramref name="anchor"/> with the given position and size.</summary>
    public static void PlaceCorner(RectTransform rect, Vector2 anchor, Vector2 position, Vector2 size)
    {
        if (rect == null) return;
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = anchor;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    public static void PlaceTopLeft(RectTransform rect, Vector2 offset, Vector2 size) =>
        PlaceCorner(rect, new Vector2(0f, 1f), offset, size);

    public static void PlaceTopRight(RectTransform rect, Vector2 offset, Vector2 size) =>
        PlaceCorner(rect, new Vector2(1f, 1f), offset, size);

    public static void PlaceBottomLeft(RectTransform rect, Vector2 offset, Vector2 size) =>
        PlaceCorner(rect, new Vector2(0f, 0f), offset, size);

    public static void PlaceBottomRight(RectTransform rect, Vector2 offset, Vector2 size) =>
        PlaceCorner(rect, new Vector2(1f, 0f), offset, size);
}
