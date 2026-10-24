using TMPro;
using UnityEngine;

/// <summary>
/// F93 (E7): the records board in OfficeA (AsylumHub/Life/RecordsBoard). A header "CASE FILES  n / 5" and five folder slots; slot i
/// is visible iff LoreArchive.HasRead(100 + i). Refreshes when it switches on (every hub visit), on AsylumHub.OnEnterHub, on
/// LoreNote.Read, and by polling the count while active (cheap), so debug-marked files show too.
/// </summary>
public class RecordsBoard : MonoBehaviour
{
    public TMP_Text header;
    public RecordsSlot[] slots = new RecordsSlot[0];

    private int _shown = -1;

    private void OnEnable()
    {
        AsylumHub.OnEnterHub += HandleEnter;
        LoreNote.Read += HandleRead;
        L10n.Changed += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        LoreNote.Read -= HandleRead;
        L10n.Changed -= Refresh;
    }

    private void HandleEnter(int floor) => Refresh();
    private void HandleRead(int id) => Refresh();

    private void Update()
    {
        if (HubState.FilesRead != _shown) Refresh();
    }

    public void Refresh()
    {
        _shown = HubState.FilesRead;
        if (header != null)
        {
            if (!header.enableAutoSizing) RuntimeUi.AutoFit(header, 0.6f); // D14
            header.text = L10n.T("hud.records.header", ("n", _shown), ("total", PatientFiles.MazeFileCount));
        }
        foreach (RecordsSlot slot in slots)
        {
            if (slot != null) slot.Refresh();
        }
    }

    /// <summary>
    /// F126: once 2+ case files are read, inspecting the RecordsBoard corroborates the files, scrubs one level off the
    /// hunter's highest learned habit, refreshes the classroom lesson board, and notifies the player.
    /// </summary>
    public static bool TryCorroborate(PlayerHud hud)
    {
        if (HunterMemory.Corroborated || HubState.FilesRead < 2) return false;
        HunterMemory.Corroborated = true;

        bool erasedAny = HunterMemory.EraseHighestHabit(out HunterMemory.Habit erased);
        HubLesson lesson = FindFirstObjectByType<HubLesson>();
        if (lesson != null) lesson.Refresh();

        AsylumHub hub = AsylumHub.Active;
        Vector3 soundPos = hub != null && hub.Player != null ? hub.Player.position : Vector3.zero;
        GameAudio.PlayAt(GameAudio.Library.paper, soundPos, 0.7f);
        GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.24f, 165f, 0.7f), soundPos, 0.75f);

        if (hud == null && hub != null) hud = hub.Hud;
        if (hud != null)
        {
            string line = erasedAny
                ? L10n.T("hub.records.corroborated", ("habit", HunterMemory.NameText(erased)))
                : L10n.T("hub.records.corroborated_clean");
            hud.ShowSubtitle(line, 4.2f);
        }
        return true;
    }
}
