using System;
using TMPro;
using UnityEngine;

/// <summary>
/// S9 (plan section 5): a runtime-UI label that follows the selected language. Holds a string key (plus optional named Smart args, or a
/// provider for args that change) and re-sets its sibling TMP_Text whenever it is enabled and on <see cref="L10n.Changed"/>. Built-once
/// menu text (titles, buttons, static HUD labels) binds itself with <c>RuntimeUi.Bind</c> instead of every menu hand-rolling a Relabel().
/// A label under an inactive panel refreshes when the panel is next shown (OnEnable), so no language change is ever missed.
/// </summary>
public class LocalizedLabel : MonoBehaviour
{
    private TMP_Text _text;
    private string _key;
    private (string name, object value)[] _args;
    private Func<(string name, object value)[]> _argProvider;

    public string Key => _key;

    public void Set(TMP_Text text, string key, (string name, object value)[] args = null, Func<(string name, object value)[]> argProvider = null)
    {
        _text = text;
        _key = key;
        _args = args;
        _argProvider = argProvider;
        Refresh();
    }

    /// <summary>Change which key the label shows (and apply it now).</summary>
    public void SetKey(string key, (string name, object value)[] args = null)
    {
        _key = key;
        _args = args;
        Refresh();
    }

    public void Refresh()
    {
        if (_text == null || string.IsNullOrEmpty(_key)) return;
        var args = _argProvider != null ? _argProvider() : _args;
        _text.text = args != null && args.Length > 0 ? L10n.T(_key, args) : L10n.T(_key);
    }

    private void OnEnable()
    {
        L10n.Changed += Refresh;
        Refresh();
    }

    private void OnDisable() => L10n.Changed -= Refresh;
}
