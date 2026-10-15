using UnityEngine;

/// <summary>
/// F93 (E2): the asylum story's state as a pure function of things that are already saved - GameFlow.CurrentFloor (the floor whose
/// wing is open) and LoreArchive (which case files were read) - plus the finale flag. No saved state of its own. The one static
/// is <see cref="IsFinale"/>, reset by SubsystemRegistration and by GameFlow.ReturnToMenu / ResetStatics like every run static.
/// </summary>
public static class HubState
{
    public enum Patient { Hilde = 0, Tomas = 1, Elias = 2 }

    /// <summary>True while the Director's-office finale (G6) is in force: the hub is the end-game stage, not a between-floors stop.</summary>
    public static bool IsFinale { get; set; }

    /// <summary>The floor whose wing is open: AsylumHub.debugFloor if set (Inspector, 0 = off), else GameFlow.CurrentFloor, clamped 1..5.</summary>
    public static int Floor
    {
        get
        {
            AsylumHub hub = AsylumHub.Active;
            int f = hub != null && hub.debugFloor > 0 ? hub.debugFloor : GameFlow.CurrentFloor;
            return Mathf.Clamp(f, 1, 5);
        }
    }

    // index 0 = floor 1.
    private static readonly float[] LightsOff = { 0f, 0.15f, 0.30f, 0.45f, 0.60f };
    private static readonly float[] Flicker = { 0.15f, 0.25f, 0.35f, 0.45f, 0.50f };
    private static readonly float[] Events = { 0f, 1f, 2f, 3f, 3f };

    /// <summary>Fraction of the hub's lights that are dead on `floor` (cumulative: the same lamps stay dead). The finale handles its own lights.</summary>
    public static float LightsOffFraction(int floor) => LightsOff[Mathf.Clamp(floor, 1, 5) - 1];
    public static float FlickerFraction(int floor) => Flicker[Mathf.Clamp(floor, 1, 5) - 1];
    public static float EventsPerMinute(int floor) => Events[Mathf.Clamp(floor, 1, 5) - 1];

    /// <summary>Hilde stands in the day room floors 1-4, Tomas in the basement hall floors 1-5, Elias lies in his bed floors 1-3; all three appear in the finale.</summary>
    public static bool PatientPresent(Patient patient, int floor, bool finale)
    {
        if (finale) return true;
        if (HubBreach.NpcsGone) return false; // F121: the breach emptied the asylum
        switch (patient)
        {
            case Patient.Hilde: return floor <= 4;
            case Patient.Tomas: return floor <= 5;
            default: return floor <= 3;
        }
    }

    /// <summary>Elias is gone from floor 4: his body lies on the morgue table instead.</summary>
    public static bool EliasIsDead(int floor, bool finale) => !finale && floor >= 4;

    public static bool PatientPresent(Patient patient) => PatientPresent(patient, Floor, IsFinale);

    /// <summary>How many of the five maze files (100..104) have been read.</summary>
    public static int FilesRead
    {
        get
        {
            int n = 0;
            for (int i = 0; i < PatientFiles.MazeFileCount; i++)
            {
                if (LoreArchive.HasRead(PatientFiles.FirstId + i)) n++;
            }
            return n;
        }
    }

    /// <summary>The Director's file (105) has been read: the master switch is known.</summary>
    public static bool HasTruth => LoreArchive.HasRead(PatientFiles.DirectorId);

    /// <summary>A stable (platform-independent) hash of a hierarchy path, for ordering lights so the dead ones accumulate floor by floor.</summary>
    public static int StableHash(string s)
    {
        unchecked
        {
            int h = (int)2166136261;
            for (int i = 0; i < s.Length; i++) h = (h ^ s[i]) * 16777619;
            return h;
        }
    }

    /// <summary>F117: the floor whose free hot meal (the kitchen's tray) has been eaten, 0 = none. Campaign-scoped like HubCaches: it must survive the scene reloads of a wing entry, Retry and CONTINUE, so it is saved (SaveData.hubMealFloor) and reset by ResetCampaign.</summary>
    public static int MealFloor { get; set; }

    /// <summary>F121: the hub breach has been survived this campaign (set when the player walks into the Director's door while it hunts; saved as SaveData.hubBreachDone). Campaign-scoped like MealFloor.</summary>
    public static bool BreachDone { get; set; }

    public static void ResetCampaign()
    {
        MealFloor = 0;
        BreachDone = false;
    }

    public static void CaptureTo(SaveData data)
    {
        data.hubMealFloor = MealFloor;
        data.hubBreachDone = BreachDone;
    }

    public static void RestoreFrom(SaveData data)
    {
        MealFloor = Mathf.Clamp(data.hubMealFloor, 0, FloorProfile.FinalFloor);
        BreachDone = data.hubBreachDone;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsFinale = false;
        MealFloor = 0;
        BreachDone = false;
    }
}
