using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

/// <summary>
/// S9: the one door to the Unity Localization string tables. <c>L10n.T("ui.settings.title")</c> picks the table from the key's first
/// segment (ui. -> UI, hud. -> HUD ...), looks the key up in the selected locale, falls back to English, and finally to the key itself (one
/// warning per key) - the package's own "No translation found" text never reaches the screen. Smart String arguments are NAMED:
/// <c>L10n.T("hud.keys_locked", ("h", 1), ("t", 3))</c> for a "{h} / {t}" entry.
/// Switching language = <see cref="SetLanguage"/> (sets LocalizationSettings.SelectedLocale and remembers the choice in GameSettings);
/// <see cref="Changed"/> fires after the new locale's tables are ready. In the Editor and on Windows the localization system is initialised
/// lazily (blocking) if the Boot scene did not do it; on WebGL that is impossible, so the Boot scene is required there.
/// Spec: plannings/localization-plan.md.
/// </summary>
public static class L10n
{
    public static readonly string[] Codes = { "en", "es", "fr", "de" };
    /// <summary>Language names are always written in their own language and never translated.</summary>
    public static readonly string[] NativeNames = { "ENGLISH", "ESPAÑOL", "FRANÇAIS", "DEUTSCH" };

    private static readonly Dictionary<string, string> TableOf = new Dictionary<string, string>
    {
        { "ui", "UI" }, { "hud", "HUD" }, { "sub", "Subtitles" }, { "hub", "Hub" }, { "dlg", "Dialogue" },
        { "lore", "Lore" }, { "shop", "Shop" }, { "voice", "Voices" }, { "world", "World" }, { "intake", "Intake" },
    };

    private static readonly HashSet<string> Warned = new HashSet<string>();
    // Plain (argument-free) lookups are cached per locale: the HUD asks for the same labels every frame. Cleared on a language change.
    private static readonly Dictionary<string, string> Cache = new Dictionary<string, string>();
    private static bool _hooked;
    private static Action _changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (_hooked)
        {
            try { LocalizationSettings.SelectedLocaleChanged -= OnLocaleChanged; } catch { /* settings already gone */ }
        }
        _hooked = false;
        _changed = null;
        Warned.Clear();
        Cache.Clear();
    }

    /// <summary>Raised after the selected locale changed and its tables are loaded. Subscribers unsubscribe in OnDestroy / OnDisable.</summary>
    public static event Action Changed
    {
        add { Hook(); _changed += value; }
        remove { _changed -= value; }
    }

    private static void Hook()
    {
        if (_hooked) return;
        _hooked = true;
        LocalizationSettings.SelectedLocaleChanged += OnLocaleChanged;
    }

    private static void OnLocaleChanged(Locale locale)
    {
        Cache.Clear();
        var preload = LocalizationSettings.StringDatabase.PreloadOperation;
        if (!preload.IsDone)
        {
            if (Application.platform != RuntimePlatform.WebGLPlayer) preload.WaitForCompletion();
            else { preload.Completed += _ => _changed?.Invoke(); return; }
        }
        _changed?.Invoke();
    }

    /// <summary>True when the localization system is ready; blocks on initialisation where the platform allows it.</summary>
    public static bool Ensure()
    {
        var init = LocalizationSettings.InitializationOperation;
        if (init.IsDone) return true;
        if (Application.platform == RuntimePlatform.WebGLPlayer) return false;
        init.WaitForCompletion();
        return init.IsDone;
    }

    /// <summary>Code of the selected locale ("en", "es", "fr", "de"); "en" if the system is not ready.</summary>
    public static string Code
    {
        get
        {
            if (!Ensure()) return "en";
            Locale l = LocalizationSettings.SelectedLocale;
            return l != null ? l.Identifier.Code : "en";
        }
    }

    /// <summary>Culture of the selected locale (the decimal separator the player expects), never the OS culture.</summary>
    public static CultureInfo Culture
    {
        get
        {
            try
            {
                if (Ensure())
                {
                    Locale l = LocalizationSettings.SelectedLocale;
                    if (l != null && l.Identifier.CultureInfo != null) return l.Identifier.CultureInfo;
                }
            }
            catch { /* fall through */ }
            return CultureInfo.GetCultureInfo("en");
        }
    }

    /// <summary>Switches the language: remembers it and sets LocalizationSettings.SelectedLocale (the only language state there is).</summary>
    public static void SetLanguage(string code)
    {
        GameSettings.Language = code ?? "";
        if (!Ensure()) return;
        Locale target = LocalizationSettings.AvailableLocales.GetLocale(code);
        if (target != null && target != LocalizationSettings.SelectedLocale) LocalizationSettings.SelectedLocale = target;
    }

    /// <summary>Drops the cached plain lookups (LocalizationImporter calls it after regenerating the tables).</summary>
    public static void ClearCache() => Cache.Clear();

    public static string T(string key)
    {
        if (key != null && Cache.TryGetValue(key, out string cached)) return cached;
        string text = Lookup(key, null);
        // Only cache real hits (a miss returns the key and may resolve once the tables finish loading).
        if (key != null && text != key) Cache[key] = text;
        return text;
    }

    public static string T(string key, params (string name, object value)[] args)
    {
        if (args == null || args.Length == 0) return Lookup(key, null);
        var dict = new Dictionary<string, object>(args.Length);
        foreach (var a in args) dict[a.name] = a.value;
        return Lookup(key, dict);
    }

    public static bool Has(string key) => Find(key, false) != null || Find(key, true) != null;

    /// <summary>All keys prefix.0, prefix.1 ... that exist (stops at the first gap), already translated.</summary>
    public static string[] Pool(string prefix)
    {
        var list = new List<string>();
        for (int i = 0; ; i++)
        {
            string k = prefix + "." + i;
            if (!Has(k)) break;
            list.Add(T(k));
        }
        return list.ToArray();
    }

    /// <summary>Like <see cref="Pool(string)"/> with named Smart arguments (a pool line may contain {floor}).</summary>
    public static string[] Pool(string prefix, params (string name, object value)[] args)
    {
        var list = new List<string>();
        for (int i = 0; ; i++)
        {
            string k = prefix + "." + i;
            if (!Has(k)) break;
            list.Add(T(k, args));
        }
        return list.ToArray();
    }

    /// <summary>The RAW (unformatted) entry text of <paramref name="key"/> in the locale <paramref name="code"/> only - null when that locale has no
    /// translation. For editor audits that measure every language's text (plan D16); never for display.</summary>
    public static string TIn(string code, string key)
    {
        if (string.IsNullOrEmpty(key) || !Ensure()) return null;
        string table = TableFor(key);
        Locale locale = LocalizationSettings.AvailableLocales.GetLocale(code);
        if (table == null || locale == null) return null;
        try
        {
            StringTableEntry e = LocalizationSettings.StringDatabase.GetTableEntry(table, key, locale, FallbackBehavior.DontUseFallback).Entry;
            return e != null && !string.IsNullOrEmpty(e.Value) ? e.Value : null;
        }
        catch { return null; }
    }

    private static string TableFor(string key)
    {
        int dot = key.IndexOf('.');
        string head = dot > 0 ? key.Substring(0, dot) : key;
        return TableOf.TryGetValue(head, out string t) ? t : null;
    }

    private static StringTableEntry Find(string key, bool english)
    {
        if (string.IsNullOrEmpty(key) || !Ensure()) return null;
        string table = TableFor(key);
        if (table == null) return null;
        Locale locale = english ? LocalizationSettings.AvailableLocales.GetLocale("en") : LocalizationSettings.SelectedLocale;
        if (locale == null) return null;
        try
        {
            StringTableEntry e = LocalizationSettings.StringDatabase.GetTableEntry(table, key, locale, FallbackBehavior.DontUseFallback).Entry;
            return e != null && !string.IsNullOrEmpty(e.Value) ? e : null;
        }
        catch (Exception ex)
        {
            if (Warned.Add("ex:" + key)) Debug.LogWarning($"[L10n] lookup of '{key}' failed: {ex.Message}");
            return null;
        }
    }

    private static string Lookup(string key, Dictionary<string, object> args)
    {
        StringTableEntry e = Find(key, false) ?? Find(key, true);
        if (e == null)
        {
            if (!string.IsNullOrEmpty(key) && Warned.Add(key)) Debug.LogWarning($"[L10n] missing string key '{key}'");
            return key ?? "";
        }
        return args == null ? e.GetLocalizedString() : e.GetLocalizedString((IList<object>)new object[] { args });
    }
}
