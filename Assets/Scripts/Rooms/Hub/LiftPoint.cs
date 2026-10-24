using UnityEngine;

/// <summary>
/// F93 (S1): one of the three things the player presses E on around the asylum's lift - the call at the ground-floor shaft door, the
/// chained gate across the basement stairs (floor 2), and the stuck basement doors that have to be forced (hold E). The logic lives in
/// <see cref="LiftSetPiece"/>; this only reports what the piece says and forwards the press.
/// </summary>
public class LiftPoint : MonoBehaviour, IInteractable
{
    public enum Role { Call, Gate, Pry }

    public Role role;
    [SerializeField] private LiftSetPiece owner;

    public void Bind(LiftSetPiece piece) { owner = piece; }

    string IInteractable.Prompt => owner != null ? owner.PromptFor(role) : "";
    bool IInteractable.CanInteract => owner != null && owner.CanUse(role);
    float IInteractable.HoldSeconds => role == Role.Pry ? 1.5f : 0f;
    void IInteractable.Interact(PlayerInteractor who) { if (owner != null) owner.Use(role); }
    void IInteractable.OnFocus(bool focused) { }
}
