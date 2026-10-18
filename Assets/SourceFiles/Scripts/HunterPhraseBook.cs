using UnityEngine;

/// <summary>
/// F82: the pool of lines the hunter says in speech bubbles, one array per situation
/// (HunterVoice picks from them with a per-category shuffle bag). Edit the asset
/// (Assets/SourceFiles/Data/HunterPhrases.asset, made by KEEP IT LIT &gt; Build &gt; Phrase Books) in the
/// Inspector; "Reset" from the component menu restores the defaults. An empty category is legal and just
/// means it says nothing for that event. The token {floor} is replaced with the current floor number.
/// </summary>
[CreateAssetMenu(menuName = "KEEP IT LIT/Hunter Phrase Book")]
public class HunterPhraseBook : ScriptableObject
{
    public enum Category { Spotted, Chasing, Lost, Searching, Ambush, Locker, Door, Caught }

    [Tooltip("It sees you after a while without seeing you.")]
    [TextArea(1, 2)] public string[] spotted;
    [Tooltip("A long chase with you in sight.")]
    [TextArea(1, 2)] public string[] chasing;
    [Tooltip("It lost you mid-chase and starts searching.")]
    [TextArea(1, 2)] public string[] lost;
    [Tooltip("Muttered while searching nearby without seeing you.")]
    [TextArea(1, 2)] public string[] searching;
    [Tooltip("Shouted: it springs out of an ambush with you in sight.")]
    [TextArea(1, 2)] public string[] ambush;
    [Tooltip("You are hiding in a locker and it is close.")]
    [TextArea(1, 2)] public string[] locker;
    [Tooltip("It just bashed a shutter door open.")]
    [TextArea(1, 2)] public string[] door;
    [Tooltip("Shouted: it caught you (plays into the capture blackout).")]
    [TextArea(1, 2)] public string[] caught;

    public string[] Get(Category category)
    {
        switch (category)
        {
            case Category.Spotted: return spotted;
            case Category.Chasing: return chasing;
            case Category.Lost: return lost;
            case Category.Searching: return searching;
            case Category.Ambush: return ambush;
            case Category.Locker: return locker;
            case Category.Door: return door;
            default: return caught;
        }
    }

    /// <summary>An in-memory book with the default lines - HunterVoice's fallback when the asset is not assigned.</summary>
    public static HunterPhraseBook CreateDefault()
    {
        HunterPhraseBook book = CreateInstance<HunterPhraseBook>();
        book.FillDefaults();
        return book;
    }

    /// <summary>F126: spliced magnetic-tape echoes of asylum staff orders, Procedure 1, and Patient 7's prior admissions.</summary>
    public void FillDefaults()
    {
        spotted = new[]
        {
            "patient seven... out of bed.",
            "lamp on in the corridor.",
            "do not run in the ward.",
            "returning to intake.",
            "pulse elevated. hold still.",
            "unauthorized light source.",
            "we see you, seven.",
        };
        chasing = new[]
        {
            "running only tires the heart.",
            "procedure one... does not stop.",
            "remain where you are.",
            "fourteen admissions. same corridor.",
            "the stars only lead back down.",
            "orderlies to the lower wing.",
        };
        lost = new[]
        {
            "subject stepped out of the beam.",
            "the ward is a closed system.",
            "we know every door down here.",
            "holding breath... noted.",
        };
        searching = new[]
        {
            "bed check... room empty.",
            "step forward for the count.",
            "respiration audible in the dark.",
            "patient keeps drawing stars...",
            "procedure one: removal of fear.",
            "floor {floor}. night rounds.",
        };
        ambush = new[]
        {
            "HOLD FOR TREATMENT.",
            "WARD LOCKDOWN.",
            "STEP INTO THE LIGHT.",
            "PATIENT SEVEN.",
        };
        locker = new[]
        {
            "locker latch warm.",
            "breathing behind the tin.",
            "open the cabinet, seven.",
            "it remembers which one.",
            "iron holds the heat.",
        };
        door = new[]
        {
            "obstruction in the corridor.",
            "clear the shutter.",
            "forty-one doors...",
            "ward access forced.",
        };
        caught = new[]
        {
            "READMITTED.",
            "PROCEDURE COMPLETE.",
            "LIGHTS OUT.",
            "BED READY IN INTAKE.",
        };
    }

    private void Reset() => FillDefaults();
}
