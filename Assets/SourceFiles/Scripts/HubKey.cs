using UnityEngine;

/// <summary>
/// F93 (S4): the Director's key, a brass key on the records board in OfficeA (a clone of InteractableKit.keyPickup with its KeyPickup
/// stripped - it is not a maze key and never touches KeyRing). Only offered on floor 5, while the key run is on. Taking it unlocks the
/// Director's wing door for the rest of the visit, even after a catch.
/// </summary>
public class HubKey : MonoBehaviour, IInteractable
{
    private KeyRunSetPiece _owner;
    private AudioClip _clink;

    public void Bind(KeyRunSetPiece owner) { _owner = owner; }

    string IInteractable.Prompt => "TAKE THE DIRECTOR'S KEY";
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => _owner != null && _owner.Running && !_owner.KeyHeld;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_owner == null || _owner.KeyHeld) return;
        if (_clink == null) _clink = DoorAudio.BuildClunkClip(0.12f, 900f, 0.2f);
        AudioSource.PlayClipAtPoint(_clink, transform.position, 0.7f);
        _owner.TakeKey();
    }

    void IInteractable.OnFocus(bool focused) { }
}
