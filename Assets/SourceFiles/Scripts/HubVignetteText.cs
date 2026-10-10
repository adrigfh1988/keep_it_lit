using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// F101: every line the hub's story vignettes say (examine subtitles and the few short readables), by key and floor. ALL CAPS, one line
/// under 100 characters (readables may run to two short lines). Some lines are computed from the campaign: the admissions ledger grows by
/// one entry per floor and shows a 7 once file 105 has been read; the bay card gains a second line then; the night-shift rota gains a second
/// hand from floor 3. Nothing here is saved and nothing pays out - negative note ids are for readables only (LoreArchive ignores them).
/// S9: the text itself lives in the Lore table (lore.vignette.*); this class only picks the variant (floor / truth) and, for the admissions
/// ledger, composes the entries through a Smart String.
/// </summary>
public static class HubVignetteText
{
    public const int BayCardId = -5;

    /// <summary>The Lore-table key of `key` on `floor` / `truth`; null for an unknown key. The admissions ledger is composed, not looked up.</summary>
    private static string TableKey(string key, int floor, bool truth)
    {
        switch (key)
        {
            case "bay.card": return truth ? "lore.vignette.bay_card_truth" : "lore.vignette.bay_card";
            case "evac": return "lore.vignette.evac";
            case "tally": return "lore.vignette.tally";
            case "rota": return floor >= 3 ? "lore.vignette.rota_late" : "lore.vignette.rota";
            case "room4": return "lore.vignette.room4";
            case "stanbay": return "lore.vignette.stanbay";
            case "pipe": return floor >= 4 ? "lore.vignette.pipe_late" : "lore.vignette.pipe_early";
            case "lower": return "lore.vignette.lower";
            case "barricade": return floor >= 4 ? "lore.vignette.barricade_late" : "lore.vignette.barricade_early";
            case "chalkhouse": return floor >= 4 ? "lore.vignette.chalkhouse_late" : "lore.vignette.chalkhouse_early";
        }
        return null;
    }

    /// <summary>The text for `key` on `floor` (1..5; the finale counts as 5). Empty for an unknown key.</summary>
    public static string Get(string key, int floor, bool truth)
    {
        floor = floor < 1 ? 1 : floor > 5 ? 5 : floor;
        if (key == "admissions")
        {
            int entries = 13 + floor;
            string mark = truth ? "7" : "#";
            int shown = System.Math.Min(entries, 5);
            string entry = L10n.T("lore.vignette.admissions.entry", ("mark", mark));
            string list = string.Join(", ", Enumerable.Repeat(entry, shown));
            return entries > 5
                ? L10n.T("lore.vignette.admissions_more", ("list", list), ("n", entries - 5))
                : L10n.T("lore.vignette.admissions", ("list", list));
        }
        string tableKey = TableKey(key, floor, truth);
        return tableKey != null ? L10n.T(tableKey) : "";
    }

    private static readonly string[] Keys =
    {
        "bay.card", "evac", "tally", "admissions", "rota", "room4", "stanbay", "pipe", "lower", "barricade", "chalkhouse"
    };

    /// <summary>Every table key a vignette can show, over every floor and truth state.</summary>
    private static IEnumerable<string> AllTableKeys()
    {
        var seen = new HashSet<string>();
        foreach (string key in Keys)
        {
            if (key == "admissions")
            {
                foreach (string k in new[] { "lore.vignette.admissions", "lore.vignette.admissions_more" }) if (seen.Add(k)) yield return k;
                continue;
            }
            for (int floor = 1; floor <= 5; floor++)
            {
                foreach (bool truth in new[] { false, true })
                {
                    string k = TableKey(key, floor, truth);
                    if (k != null && seen.Add(k)) yield return k;
                }
            }
        }
    }

    /// <summary>
    /// The longest single line over every key, floor and truth state (the audit keeps it under 100 in English, 120 elsewhere). `localeCode`
    /// null = the current language; "en" / "es" / "fr" / "de" read that language's raw table text (a key that language has not translated is
    /// skipped). The admissions ledger is measured with five entries and a two-digit "more" count substituted for its tokens.
    /// </summary>
    public static int LongestLine(string localeCode = null)
    {
        int longest = 0;
        if (localeCode == null)
        {
            foreach (string key in Keys)
                for (int floor = 1; floor <= 5; floor++)
                    foreach (bool truth in new[] { false, true })
                        foreach (string line in Get(key, floor, truth).Split('\n')) if (line.Length > longest) longest = line.Length;
            return longest;
        }

        string entryRaw = L10n.TIn(localeCode, "lore.vignette.admissions.entry") ?? "";
        string entry = entryRaw.Replace("{mark}", "#");
        string fiveEntries = string.Join(", ", Enumerable.Repeat(entry, 5));
        foreach (string tableKey in AllTableKeys())
        {
            string text = L10n.TIn(localeCode, tableKey);
            if (text == null) continue;
            text = text.Replace("{list}", fiveEntries).Replace("{n}", "13");
            foreach (string line in text.Split('\n')) if (line.Length > longest) longest = line.Length;
        }
        return longest;
    }

    private static readonly Regex Token = new Regex(@"\{[^}]*\}");

    /// <summary>True when every English source line is upper-case (the house style). Other languages are not checked here (plan D16).</summary>
    public static bool AllCaps()
    {
        foreach (string tableKey in AllTableKeys())
        {
            string text = L10n.TIn("en", tableKey);
            if (text == null) return false;
            text = Token.Replace(text, "");
            if (text != text.ToUpperInvariant()) return false;
        }
        return true;
    }

    public static IEnumerable<string> AllKeys => Keys;
}
