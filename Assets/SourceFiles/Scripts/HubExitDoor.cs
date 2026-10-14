using System.Collections;
using UnityEngine;

/// <summary>
/// F93 (E10): the asylum's main doors, the way out in ending A (DISCHARGED). E at them: the player is frozen and the shared door walk plays
/// (F112: <see cref="DoorPassage"/> on this object - the pack's MainDoor leaf gets its hinge from the builder, the leaf swings toward the hall,
/// the opening is a grey-white daylight void and the screen irises closed onto it in the same cold grey-white), then the ending completes.
/// Only offered in the finale.
/// </summary>
public class HubExitDoor : MonoBehaviour, IInteractable
{
    [Tooltip("The pack's MainDoor mesh (AsylumHub/Building/.../MainDoor). The builder hangs it under a hinge (DoorPassage.hinges[0]).")]
    [SerializeField] private Transform leaf;

    private HubFinale _owner;

    public void Bind(HubFinale owner) { _owner = owner; }

    string IInteractable.Prompt => L10n.T("hub.exit.prompt");
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => HubState.IsFinale && _owner != null && !_owner.Finished;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_owner != null) _owner.Discharge();
    }

    void IInteractable.OnFocus(bool focused) { }

    /// <summary>The walk-out. `done` runs at the end (GameOutcome.CompleteFinale).</summary>
    public IEnumerator WalkOut(System.Action done)
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.Player == null) { done?.Invoke(); yield break; }
        hub.SetScripted(true);
        if (hub.Controller != null) hub.Controller.MovementLocked = true;
        if (hub.Hud != null) { hub.Hud.SetPrompt(null); hub.Hud.SetFocusPrompt(null); }

        Vector3 doorway = leaf != null ? leaf.position : transform.position;
        if (!GameAudio.PlayAt(GameAudio.Library.exitDoorCreak, doorway, 1f)) GameAudio.ClipAtPoint(DoorAudio.BuildCreakClip(1.4f, 120f, 4), doorway, 1f); // F97: the ending's own door sound

        yield return hub.PassThrough(transform); // returns at full cold grey-white, the leaf open
        yield return new WaitForSecondsRealtime(0.5f);
        done?.Invoke();
    }
}
