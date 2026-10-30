using System;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Scripting.APIUpdating;

/// <summary>
/// S9 (D6): first link of the startup-locale chain - the language saved by the LANGUAGE setting. Returns null when nothing is saved (or the
/// saved code is not an available locale), so the chain continues with SystemLocaleSelector and finally English.
/// </summary>
[Serializable]
[MovedFrom(true, sourceNamespace: null, sourceAssembly: "Assembly-CSharp")]
public class SavedLocaleSelector : IStartupLocaleSelector
{
    public Locale GetStartupLocale(ILocalesProvider availableLocales)
    {
        string code = GameSettings.Language;
        if (string.IsNullOrEmpty(code)) return null;
        return availableLocales.GetLocale(code);
    }
}
