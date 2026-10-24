/// <summary>
/// F123: the mapping between the five wings and the five patients whose nightmare each maze is, plus the per-wing text lookups. No state, so no reset.
///   floor 1 WARD A         Hilde Brandt (P3)   the extra door
///   floor 2 BOILER ROOM    Tomas Reiner (P11)  the pipes knock
///   floor 3 COLD STORAGE   Elias Vogt   (P5)   the drawers
///   floor 4 SURGERY        Dr. Weiss           the ampoules
///   floor 5 DIRECTOR       the Director        the house rules
/// Every nightmare keys on the FLOOR (floor 5 wears the Crypt look of floor 3) and never in a DESCENT, which is an endless mode outside the story.
/// </summary>
public static class WingNightmares
{
    /// <summary>True on a story floor 1-5: the only place a wing nightmare, a per-wing line or a wing banner exists.</summary>
    public static bool IsStoryFloor => !GameFlow.IsDescent && GameFlow.CurrentFloor >= 1 && GameFlow.CurrentFloor <= FloorProfile.FinalFloor;

    /// <summary>The floor to key a nightmare on: <paramref name="floor"/> on a story floor, 0 in a DESCENT or out of range.</summary>
    public static int StoryFloor(int floor) => !GameFlow.IsDescent && floor >= 1 && floor <= FloorProfile.FinalFloor ? floor : 0;

    /// <summary>The Clerk's typed slip subtitle for the cleared floor; the generic line in a DESCENT or when the key is missing.</summary>
    public static string SlipSubtitle(int clearedFloor)
    {
        string key = "ui.ledger.slip_subtitle." + clearedFloor;
        return IsStoryFloor && L10n.Has(key) ? L10n.T(key) : L10n.T("ui.ledger.slip_subtitle");
    }

    /// <summary>The second end-screen line after a loss on this floor, or null (a DESCENT, a missing key).</summary>
    public static string LossLine(int floor)
    {
        string key = "ui.end.wing." + floor;
        return StoryFloor(floor) != 0 && L10n.Has(key) ? L10n.T(key) : null;
    }

    /// <summary>The wing's name for the floor banner ("WARD A"; floor 5 "DIRECTOR'S OFFICE"), or null (a DESCENT, a missing key).</summary>
    public static string BannerName(int floor)
    {
        if (StoryFloor(floor) == 0) return null;
        string key = floor == FloorProfile.FinalFloor ? "hub.wing." + floor + ".room" : "hub.wing." + floor + ".name";
        return L10n.Has(key) ? L10n.T(key) : null;
    }

    /// <summary>
    /// How the player's own number is written everywhere: "7" once the truth is known, else the redaction block when the UI font has it, else "#".
    /// The readmission slip, the PA subtitles and the drawer tag all go through this, so they always show the same mark.
    /// </summary>
    public static string PatientMark()
    {
        if (HubState.HasTruth) return "7";
        TMPro.TMP_FontAsset font = RuntimeUi.ResolveFont();
        return font != null && font.HasCharacter('▇', true, true) ? "▇" : "#";
    }
}
