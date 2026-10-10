using UnityEngine;

/// <summary>
/// F93: one folder slot on the records board. Visible only once its case file (100 + slotIndex) has been read; E on a visible one
/// reopens the text in the reading panel. Deliberately NOT a LoreNote: it never raises LoreNote.Read, never marks anything read
/// and pays no bonus. The collider sits on this object (PlayerInteractor probes colliders for IInteractable in parents).
/// </summary>
public class RecordsSlot : MonoBehaviour, IInteractable
{
    [Tooltip("0..4 -> case file 100..104.")]
    public int slotIndex;
    [Tooltip("The folder model, shown only when the file has been read.")]
    public GameObject folder;

    public int FileId => PatientFiles.FirstId + slotIndex;
    public bool Unlocked => LoreArchive.HasRead(FileId);

    /// <summary>Show or hide the folder (and its collider) to match the archive.</summary>
    public void Refresh()
    {
        bool on = Unlocked;
        if (folder != null && folder.activeSelf != on) folder.SetActive(on);
        Collider c = GetComponent<Collider>();
        if (c != null) c.enabled = on;
    }

    private static LoreReadingUi Reader => AsylumHub.Active != null ? AsylumHub.Active.Reader : null;

    string IInteractable.Prompt => Reader != null && Reader.IsOpen ? L10n.T("hud.prompt.close") : L10n.T("hub.records.read_file", ("label", PatientFiles.Label(FileId)));
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => Unlocked && Reader != null;

    void IInteractable.Interact(PlayerInteractor who)
    {
        LoreReadingUi reader = Reader;
        if (reader == null || !Unlocked) return;
        if (reader.IsOpen) { reader.Close(); return; }
        reader.Show(PatientFiles.Text(FileId), who);
        GameAudio.PlayAt(GameAudio.Library.paper, transform.position, 0.6f); // F97 (recorded clip only)
    }

    void IInteractable.OnFocus(bool focused) { }
}
