using System.Collections.Generic;
using System.Linq;

/// <summary>
/// F101: every line the hub's story vignettes say (examine subtitles and the few short readables), by key and floor. ALL CAPS, one line
/// under 100 characters (readables may run to two short lines). Some lines are computed from the campaign: the admissions ledger grows by
/// one entry per floor and shows a 7 once file 105 has been read; the bay card gains a second line then; the night-shift rota gains a second
/// hand from floor 3. Nothing here is saved and nothing pays out - negative note ids are for readables only (LoreArchive ignores them).
/// </summary>
public static class HubVignetteText
{
    public const int BayCardId = -5;

    /// <summary>The text for `key` on `floor` (1..5; the finale counts as 5). Empty for an unknown key.</summary>
    public static string Get(string key, int floor, bool truth)
    {
        floor = floor < 1 ? 1 : floor > 5 ? 5 : floor;
        switch (key)
        {
            case "bay.card":
                return truth
                    ? "ADMITTED AGAIN. ASKED WHERE THE BEDS ARE. SLEPT BEHIND THE STATUE.\nIT IS YOUR HANDWRITING."
                    : "ADMITTED AGAIN. ASKED WHERE THE BEDS ARE. SLEPT BEHIND THE STATUE.";
            case "evac": return "EVACUATION ORDER · NIGHT OF THE 14TH · WARD A FIRST. DO NOT";
            case "tally": return "SOMEONE HAS BEEN COUNTING THE DOORS.";
            case "admissions":
            {
                int entries = 13 + floor;
                string mark = truth ? "7" : "#";
                return "ADMISSIONS: PATIENT " + string.Join(", PATIENT ", Enumerable.Repeat(mark, System.Math.Min(entries, 5))) + (entries > 5 ? ", AND " + (entries - 5) + " MORE" : "");
            }
            case "rota":
                return floor >= 3 ? "ROTA · OKAFOR · LIGHTS STAY ON.\nCOUNT AT SIX. COUNT AT SIX." : "ROTA · OKAFOR · LIGHTS STAY ON.";
            case "room4": return "ROOM 4. EMPTY. THE LAMP IS WARM.";
            case "stanbay": return "BAY LIGHTS: OFF. DO NOT LOOK IN.";
            case "pipe": return floor >= 4 ? "THREE DENTS IN THE PIPE. THE STEEL IS STILL WARM." : "TWO DENTS IN THE PIPE. THE STEEL IS STILL WARM.";
            case "lower": return "RELEASED TO LOWER LEVELS. SUBJECT A. DO NOT FOLLOW.";
            case "barricade": return floor >= 4 ? "THE TABLE HAS BEEN SHOVED. FROM THE OTHER SIDE." : "THEY BARRICADED THE DOOR. FROM THE INSIDE.";
            case "chalkhouse": return floor >= 4 ? "A CHALK HOUSE, SMEARED OUT BY A HAND." : "A CHALK HOUSE WITH MORE WINDOWS THAN THE REAL ONE.";
        }
        return "";
    }

    private static readonly string[] Keys =
    {
        "bay.card", "evac", "tally", "admissions", "rota", "room4", "stanbay", "pipe", "lower", "barricade", "chalkhouse"
    };

    /// <summary>The longest single line over every key, floor and truth state (the audit keeps it under 100).</summary>
    public static int LongestLine()
    {
        int longest = 0;
        foreach (string key in Keys)
        {
            for (int floor = 1; floor <= 5; floor++)
            {
                foreach (bool truth in new[] { false, true })
                {
                    foreach (string line in Get(key, floor, truth).Split('\n')) if (line.Length > longest) longest = line.Length;
                }
            }
        }
        return longest;
    }

    /// <summary>True when every line is upper-case (the house style).</summary>
    public static bool AllCaps()
    {
        foreach (string key in Keys)
        {
            for (int floor = 1; floor <= 5; floor++)
            {
                string text = Get(key, floor, true) + Get(key, floor, false);
                if (text != text.ToUpperInvariant()) return false;
            }
        }
        return true;
    }

    public static IEnumerable<string> AllKeys => Keys;
}
