using UnityEngine;

/// <summary>
/// F93 (E10): the drawer of the Director's desk. With all five maze case files read it opens and shows the Director's own file (105, marked
/// read), which reveals the master switch on the wall; otherwise it answers "THE DRAWER IS LOCKED · CASE FILES n / 5". Only offered in the finale.
/// </summary>
public class HubFinaleDesk : MonoBehaviour, IInteractable
{
    private HubFinale _owner;

    public void Bind(HubFinale owner) { _owner = owner; }

    string IInteractable.Prompt => HubState.FilesRead >= PatientFiles.MazeFileCount ? "OPEN THE DRAWER" : "THE DRAWER";
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => HubState.IsFinale && _owner != null && !_owner.Finished;

    void IInteractable.Interact(PlayerInteractor who)
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || _owner == null) return;
        int read = HubState.FilesRead;
        if (read < PatientFiles.MazeFileCount)
        {
            if (!GameAudio.PlayAt(GameAudio.Library.lockedRattle, transform.position, 0.7f)) GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.18f, 160f, 0.35f), transform.position, 0.7f); // F97
            hub.SubtitleLine($"THE DRAWER IS LOCKED · CASE FILES {read} / {PatientFiles.MazeFileCount}", 3f);
            return;
        }

        if (!GameAudio.PlayAt(GameAudio.Library.deskDrawer, transform.position, 0.8f)) GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.3f, 120f, 0.5f), transform.position, 0.8f); // F97
        LoreArchive.MarkRead(PatientFiles.DirectorId);
        if (who == null && hub.Player != null) who = hub.Player.GetComponent<PlayerInteractor>();
        if (hub.Reader != null) hub.Reader.Show(PatientFiles.Text(PatientFiles.DirectorId), who);
        _owner.RevealSwitch();
    }

    void IInteractable.OnFocus(bool focused) { }
}
