/// <summary>
/// F93: the six case files of the asylum story. Ids 100..104 are found in the floor 1..5 mazes (100 + floor - 1) and 105 is the
/// Director's own file in the finale. They are ordinary LoreNote ids, so LoreArchive records them (and saves them) like any note.
/// No state. S9: the texts live in the Lore table, verbatim from plannings/asylum-story-plan.md.
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

    /// <summary>S9: the file's text, from the Lore table (lore.file.&lt;id&gt;.text; a diegetic document). Empty for an unknown id.</summary>
    public static string Text(int id) => IsFile(id) ? L10n.T("lore.file." + id + ".text") : "";

    /// <summary>A short label for the records board: "P3 · HILDE BRANDT", "SUBJECT A", ... (lore.file.&lt;id&gt;.label).</summary>
    public static string Label(int id) => IsFile(id) ? L10n.T("lore.file." + id + ".label") : "";
}
