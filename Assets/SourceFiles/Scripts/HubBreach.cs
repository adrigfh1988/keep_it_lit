using UnityEngine;

/// <summary>
/// F121: the static switchboard of the hub breach (spec <c>plannings/f121-hub-breach-plan.md</c>) - the one time the asylum stops being safe.
/// On the floor-5 visit, taking the Director's key cuts the power, the Clerk and the patient vanish, the Commissary shuts and THE hunter comes
/// up out of the Surgery wing door (<see cref="HubBreachSetPiece"/>). Plain static class: the component logic lives in the set piece, this holds
/// the mode (F122 drives it), the run flags the other hub systems read, and the campaign "has it happened" test.
/// </summary>
public static class HubBreach
{
    public enum BreachMode { Off, OncePerCampaign, EveryVisit }

    /// <summary>The only floor with a breach (a constant: moving it earlier collides with the floor 1-4 mission locks and set pieces).</summary>
    public const int Floor = 5;

    /// <summary>F122's switch (an Oath sets it before the hub is entered). Default OncePerCampaign. Deliberately NOT reset by GameFlow.ReturnToMenu: a choice made on the title screen has to survive into the campaign; it is reset at SubsystemRegistration only.</summary>
    public static BreachMode Mode = BreachMode.OncePerCampaign;

    /// <summary>Session only (never saved): a breach death's Retry / New Maze re-enters the hub straight into the breach (the key is already held). Cleared when the breach is survived and by ReturnToMenu.</summary>
    public static bool Retrying;

    /// <summary>True between the power cut and <see cref="HubBreachSetPiece"/>'s end. Read by HubNpc / HubState.PatientPresent / the scares.</summary>
    public static bool InProgress { get; internal set; }

    /// <summary>The Clerk and the patient are gone while the breach runs.</summary>
    public static bool NpcsGone => InProgress;

    /// <summary>Should the floor-5 visit of this scene run (or arm) a breach?</summary>
    public static bool ShouldRun(int floor)
    {
        if (Mode == BreachMode.Off || floor != Floor || GameFlow.IsDescent || HubState.IsFinale) return false;
        return Mode == BreachMode.EveryVisit || !HubState.BreachDone;
    }

    /// <summary>KeyRunSetPiece reads it: a Retry after a breach death skips the key run (the key is already held, the door open).</summary>
    public static bool SkipKeyRun => Retrying && ShouldRun(HubState.Floor);

    /// <summary>HubShop.OpenFor reads it: the Commissary is shut from the power cut on (and from the arrival of a Retry).</summary>
    public static bool ShopShut => InProgress || SkipKeyRun;

    /// <summary>GameFlow.ReturnToMenu: a new campaign. Mode is left alone (see <see cref="Mode"/>).</summary>
    public static void ResetCampaign()
    {
        Retrying = false;
        InProgress = false;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Mode = BreachMode.OncePerCampaign;
        Retrying = false;
        InProgress = false;
    }
}
