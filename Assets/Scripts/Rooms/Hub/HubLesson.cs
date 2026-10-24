using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// F117: THE LESSON. The asylum's classroom blackboard shows, in chalk, what the hunter has learned about the player (F105
/// <see cref="HunterMemory"/>), rewritten every time the hub switches on (AsylumHub.EnterRoutine calls <see cref="Refresh"/> at black), so it
/// is up to date after every floor. Nothing learned yet: OBSERVE THE PATIENT. From floor 3 the board admits IT IS LEARNING; in the finale THE
/// LESSON IS OVER. The pause menu's lessons panel lists the same data; this is the in-world version.
///
/// Lives on AsylumHub/Lesson (a hand-editable part of the hub prefab; AsylumHub switches it with the other content). The builder made the chalk
/// text (laid flat on the board's middle panel), a faint smudge of old writing behind it and the E target; this component only fills the text.
/// Implements <see cref="IInteractable"/> itself: the collider that PlayerInteractor's cast hits is on a child.
/// </summary>
public class HubLesson : MonoBehaviour, IInteractable
{
    [Header("The board")]
    [Tooltip("The chalk text on the blackboard.")]
    public TMP_Text chalk;
    [Tooltip("A faint ghost of earlier writing behind the chalk (optional).")]
    public TMP_Text smudge;
    [Tooltip("The cold light on the board. HubDecay never puts it out: the lesson stays readable on every floor.")]
    public Light light;

    private static AsylumHub Hub => AsylumHub.Active;

    /// <summary>True once the builder has made the text.</summary>
    public bool IsComplete => chalk != null;

    private void OnEnable() => L10n.Changed += Refresh;

    private void OnDisable() => L10n.Changed -= Refresh;

    /// <summary>How many habits the hunter has learned at all.</summary>
    public static int LessonCount()
    {
        int n = 0;
        foreach (HunterMemory.Habit h in System.Enum.GetValues(typeof(HunterMemory.Habit))) if (HunterMemory.Level(h) > 0) n++;
        return n;
    }

    /// <summary>The chalk tally for a level: | || ||| (the fourth stroke never comes: the levels stop at three).</summary>
    public static string Tally(int level) => new string('|', Mathf.Clamp(level, 0, HunterMemory.MaxLevel));

    /// <summary>Builds the board's text from the current floor, finale flag and the hunter's memory.</summary>
    public string BuildText()
    {
        StringBuilder sb = new StringBuilder();
        int floor = HubState.IsFinale ? 5 : HubState.Floor;
        sb.Append("<size=125%>").Append(L10n.T("hub.lesson.title")).Append("</size>\n");
        sb.Append("<size=70%>").Append(L10n.T("hub.lesson.patient", ("n", floor))).Append("</size>\n");

        int lines = 0;
        foreach (HunterMemory.Habit h in System.Enum.GetValues(typeof(HunterMemory.Habit)))
        {
            int level = HunterMemory.Level(h);
            if (level <= 0) continue;
            sb.Append(L10n.T("hub.lesson.habit." + h.ToString().ToLowerInvariant(), ("tally", Tally(level)))).Append('\n');
            lines++;
        }
        if (lines == 0) sb.Append(L10n.T("hub.lesson.none")).Append('\n');

        if (HubState.IsFinale) sb.Append("<u>").Append(L10n.T("hub.lesson.over")).Append("</u>");
        else if (floor >= 3) sb.Append("<u>").Append(L10n.T("hub.lesson.learning")).Append("</u>");
        return sb.ToString().TrimEnd('\n');
    }

    /// <summary>Rewrites the board. Called by AsylumHub.EnterRoutine (every time the hub switches on) and on a language change.</summary>
    public void Refresh()
    {
        if (chalk == null) return;
        string text = BuildText();
        chalk.text = text;
        if (smudge != null) smudge.text = text;
    }

    // ---------------------------------------------------------------- E on the board

    private static bool Busy()
    {
        AsylumHub hub = Hub;
        return hub == null || hub.IsTransitioning || hub.Leaving || hub.PursuerAfterPlayer();
    }

    string IInteractable.Prompt => L10n.T("hub.lesson.prompt");

    float IInteractable.HoldSeconds => 0f;

    bool IInteractable.CanInteract => GameFlow.IsInHub && Hub != null && Hub.Hud != null && !Busy();

    void IInteractable.Interact(PlayerInteractor who)
    {
        AsylumHub hub = Hub;
        if (hub == null || hub.Hud == null || Busy()) return;
        int n = LessonCount();
        string line = HubState.IsFinale ? L10n.T("hub.lesson.examine_over")
            : n == 0 ? L10n.T("hub.lesson.examine_none")
            : L10n.T("hub.lesson.examine", ("n", n));
        hub.Hud.ShowSubtitle(line, 4.5f);
    }

    void IInteractable.OnFocus(bool focused) { }
}
