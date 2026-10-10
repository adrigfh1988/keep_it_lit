using TMPro;
using UnityEngine;

/// <summary>
/// S9 (plan D11): puts a World-table string on a static world-space sign (a TMP text on a hand-edited prefab: the Intake's HOLDING, the hub's
/// records-board labels, the shop's CONTRACTS ...). It sets its sibling <see cref="TMP_Text"/> from <c>L10n.T(key)</c> on enable and on every
/// language change; the English the prefab already carries stays as the fallback when the key is missing.
/// Fit: a longer translation must not spill past the sign. The first time it runs it remembers the sign's authored font size and how large
/// the authored English text measures; every later text is shrunk (down to 60 %) until it measures no larger than that. TMP's own auto-size
/// is deliberately NOT used: several signs (the records-board labels) are authored with text larger than their rect and rely on Overflow,
/// and auto-size would shrink them to the rect even in English.
/// Added to the signs by <c>LocalizationSignsBuilder</c> (ProjectSetup piece "Localized world signs"), never by hand-built code.
/// </summary>
[DisallowMultipleComponent]
public class LocalizedText : MonoBehaviour
{
    private const float MinScale = 0.6f;
    private const float Step = 0.05f;

    [Tooltip("World-table key (world.*). Empty or unknown = the text the prefab already has stays.")]
    [SerializeField] private string key = "";

    private TMP_Text _text;
    private bool _captured;
    private float _baseSize;
    private Vector2 _basePreferred;

    public string Key
    {
        get => key;
        set { key = value ?? ""; Apply(); }
    }

    private void OnEnable()
    {
        Apply();
        L10n.Changed += Apply;
    }

    private void OnDisable() => L10n.Changed -= Apply;

    /// <summary>Re-sets the sibling text from the table and fits it to the sign.</summary>
    public void Apply()
    {
        if (string.IsNullOrEmpty(key) || !L10n.Has(key)) return;
        if (_text == null) _text = GetComponent<TMP_Text>();
        if (_text == null) return;
        Capture();
        string text = L10n.T(key);
        _text.text = text;
        Fit(text);
    }

    private void Capture()
    {
        if (_captured) return;
        _captured = true;
        _baseSize = _text.fontSize;
        _basePreferred = Measure(_text.text);
    }

    private Vector2 Measure(string s)
    {
        bool wrap = _text.textWrappingMode != TextWrappingModes.NoWrap;
        float width = wrap ? Mathf.Max(0.01f, _text.rectTransform.rect.width) : float.PositiveInfinity;
        return _text.GetPreferredValues(s, width, float.PositiveInfinity);
    }

    private void Fit(string text)
    {
        float scale = 1f;
        _text.fontSize = _baseSize;
        while (scale > MinScale + 0.001f)
        {
            Vector2 p = Measure(text);
            if (p.x <= _basePreferred.x * 1.02f + 0.0001f && p.y <= _basePreferred.y * 1.02f + 0.0001f) break;
            scale -= Step;
            _text.fontSize = _baseSize * scale;
        }
    }
}
