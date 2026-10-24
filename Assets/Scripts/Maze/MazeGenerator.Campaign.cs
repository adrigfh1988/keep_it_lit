using UnityEngine;

/// <summary>
/// F122: the MazeGenerator side of the campaign rules (CampaignRules / the Patient Record unlocks). Kept in its own partial so the main file's diff stays small.
/// </summary>
public partial class MazeGenerator
{
    /// <summary>F122: this floor's profile after every modifier, rule and campaign layer (null with useFloorProfile off). The grade reads its par from it.</summary>
    public FloorProfile Profile => _profile;

    /// <summary>
    /// F122 WELDED LOCKERS: every maze locker is welded shut and the hunter is given no hiding spots to check. The lockers themselves, the bait-mouth ones and every
    /// rng draw stay exactly as they were (same maze); hub and Intake lockers are not in <c>_lockers</c>. Never in a descent (CampaignRules.LockersWelded).
    /// </summary>
    private void ApplyWeldedLockers(AIFollower hunter)
    {
        if (!CampaignRules.LockersWelded) return;
        foreach (Locker locker in _lockers)
        {
            if (locker != null) locker.Welded = true;
        }
        if (hunter != null) hunter.SetHidingSpots(System.Array.Empty<Vector3>());
    }

    /// <summary>F122 ARCHIVIST'S LAMP card: every lore note and patient file in the maze gets the Fuse Map's slow beacon.</summary>
    private void ApplyArchivistLamp()
    {
        if (!PlayerInventory.HasActive(ShopItem.ArchivistLamp)) return;
        foreach (LoreNote note in _loreNotes)
        {
            if (note != null) Beacon.Attach(note.transform, Vector3.up * 0.3f);
        }
    }

    /// <summary>F122: the Oath rank for the floor banner ("OATH 3"), appended to the rule line. Empty when there are no Oaths.</summary>
    private static string OathBannerText()
    {
        int rank = CampaignRules.OathRank;
        return rank > 0 ? L10n.T("sub.banner.oaths", ("rank", rank)) : "";
    }
}
