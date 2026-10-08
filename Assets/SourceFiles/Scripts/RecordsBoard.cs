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
        Refresh();
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        LoreNote.Read -= HandleRead;
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
        if (header != null) header.text = $"CASE FILES  {_shown} / {PatientFiles.MazeFileCount}";
        foreach (RecordsSlot slot in slots)
        {
            if (slot != null) slot.Refresh();
        }
    }
}
