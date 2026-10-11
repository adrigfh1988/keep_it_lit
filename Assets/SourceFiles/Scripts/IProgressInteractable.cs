/// <summary>
/// F111: an interactable filled by holding E whose progress is NOT reset by letting go, looking away or walking off (the exit gates' levers).
/// PlayerInteractor drives it instead of the HoldSeconds path: while it is focused and E is held it calls HoldTick, and it shows Prompt (with the
/// percentage once Progress01 is above zero) even while CanInteract is false, so a dead lever can say NO POWER. Interact is never called on one.
/// </summary>
public interface IProgressInteractable : IInteractable
{
    /// <summary>0..1, how far the hold has got. Never decreases.</summary>
    float Progress01 { get; }

    /// <summary>One frame of E held while this is focused and CanInteract. `dt` is the frame time.</summary>
    void HoldTick(float dt);
}
