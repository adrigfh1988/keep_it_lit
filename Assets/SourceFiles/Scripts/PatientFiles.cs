/// <summary>
/// F93: the six case files of the asylum story. Ids 100..104 are found in the floor 1..5 mazes (100 + floor - 1) and 105 is the
/// Director's own file in the finale. They are ordinary LoreNote ids, so LoreArchive records them (and saves them) like any note.
/// Static text only, no state. Texts are verbatim from plannings/asylum-story-plan.md.
/// </summary>
public static class PatientFiles
{
    public const int FirstId = 100;
    /// <summary>The five maze files, 100..104.</summary>
    public const int MazeFileCount = 5;
    /// <summary>The Director's file: read only in the finale.</summary>
    public const int DirectorId = 105;

    public static bool IsFile(int id) => id >= FirstId && id <= DirectorId;

    /// <summary>The note id of the file hidden in floor `floor`'s maze.</summary>
    public static int IdForFloor(int floor) => FirstId + floor - 1;

    public static string Text(int id)
    {
        switch (id)
        {
            case 100: return "CASE FILE · PATIENT 3 · HILDE BRANDT\n\nAdmitted for counting. Counts the doors on Ward A, aloud, all night. Insists there is always one more door than yesterday.\n\nStaff note: she is right. Maintenance cannot explain the new door on Ward A. Keep her away from it.";
            case 101: return "CASE FILE · PATIENT 11 · TOMAS REINER\n\nFormer stoker. Hears footsteps in the pipes and answers them. Two knocks: all clear. Three knocks: hide.\n\nStaff note: since the Boiler Room was sealed, the pipes knock back. Nobody on staff knocks three times.";
            case 102: return "CASE FILE · PATIENT 5 · ELIAS VOGT\n\nSleepwalker. Found every morning at the morgue doors, eyes open, holding his own wristband. Says drawer five \"has his name on\".\n\nStaff note: drawer five does have his name on. Nobody wrote it. Transfer to Surgery approved.";
            case 103: return "CASE FILE · SUBJECT A\n\nProcedure 1: removal of fear. Successful. The subject no longer sleeps, no longer speaks, no longer stops.\n\nReleased to the lower levels to keep them quiet. Staff note: the lower levels are quiet now. Everyone down there is very, very quiet.";
            case 104: return "DIRECTOR KARLHEINZ · PRIVATE\n\nThe service units keep the lights. Subject A keeps the dark. The stars show the patients the way down, and the way down brings them back to Intake. A closed system.\n\nIt needs one more patient to close. The file is on my desk.";
            case 105: return "CASE FILE · PATIENT 7\n\nAdmitted: fourteen times. Draws stars on the walls. Reaches the bottom every time, holding the stars, and walks back in through Intake asking where the beds are.\n\nTreatment: none effective.\n\nDirector's note: there is a switch behind this desk. It turns everything off. The units, the dark, the subject, the Asylum. I never had the courage. Patient 7 might.";
            default: return "";
        }
    }

    /// <summary>A short label for the records board: "HILDE BRANDT", "SUBJECT A", ...</summary>
    public static string Label(int id)
    {
        switch (id)
        {
            case 100: return "P3 · HILDE BRANDT";
            case 101: return "P11 · TOMAS REINER";
            case 102: return "P5 · ELIAS VOGT";
            case 103: return "SUBJECT A";
            case 104: return "DIRECTOR · PRIVATE";
            case 105: return "PATIENT 7";
            default: return "";
        }
    }
}
