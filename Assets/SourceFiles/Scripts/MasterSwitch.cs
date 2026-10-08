using UnityEngine;

/// <summary>
/// F93 (E10): the master switch behind the Director's desk, revealed by file 105. Hold E for 3 s to pull it (ending B, LIGHTS OUT). A clone of
/// the kit's fuse box stripped of its own logic; HubFinale does the ending.
/// </summary>
public class MasterSwitch : MonoBehaviour, IInteractable
{
    private HubFinale _owner;

    public void Bind(HubFinale owner) { _owner = owner; }

    string IInteractable.Prompt => "PULL THE MASTER SWITCH · LIGHTS OUT";
    float IInteractable.HoldSeconds => 3f;
    bool IInteractable.CanInteract => HubState.IsFinale && _owner != null && !_owner.Finished && HubState.HasTruth;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_owner != null) _owner.PullSwitch();
    }

    void IInteractable.OnFocus(bool focused) { }
}
