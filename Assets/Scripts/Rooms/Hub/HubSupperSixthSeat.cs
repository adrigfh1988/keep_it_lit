using UnityEngine;

/// <summary>
/// F126: the sixth place setting at the Last Supper table in the dining room (<see cref="HubSupper"/>).
/// Attached dynamically by <see cref="HubSupper.Refresh"/> onto the sixth chair / place setting so no
/// prefab regeneration is required. On floors 1-4 examining the sixth place notes that the table is laid
/// for six; on floor 5 (before entering the final wing) it grants Patient 7's Communion blessing once;
/// in the finale holding E takes your seat at the table and triggers Ending C (<see cref="GameOutcome.FinaleEnding.Seated"/>).
/// </summary>
public class HubSupperSixthSeat : MonoBehaviour, IInteractable
{
    private HubSupper _supper;

    public void Bind(HubSupper supper)
    {
        _supper = supper;
    }

    string IInteractable.Prompt => L10n.T("hub.supper.sit_prompt");

    float IInteractable.HoldSeconds => HubState.IsFinale ? 1.6f : 0f;

    bool IInteractable.CanInteract => _supper != null && _supper.CanInteractSixthSeat;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_supper != null) _supper.InteractSixthSeat();
    }

    void IInteractable.OnFocus(bool focused) { }
}
