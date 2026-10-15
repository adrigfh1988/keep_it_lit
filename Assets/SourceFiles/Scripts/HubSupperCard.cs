using UnityEngine;

/// <summary>
/// F117: one name card at the Last Supper table - an <see cref="IInteractable"/> found by PlayerInteractor's camera cast (the trigger on this
/// object is what the cast hits). Offered only once the card has a name on it (the seat's case file has been read); E gives that patient's one
/// line. Not a LoreNote: it raises nothing, marks nothing read and pays nothing. Never offered while a hub pursuer is after the player or a hub
/// transition runs.
/// </summary>
public class HubSupperCard : MonoBehaviour, IInteractable
{
    [Tooltip("The seat this card belongs to, 1..5 (= the floor whose case file names it).")]
    [SerializeField, Range(1, 5)] private int seat = 1;
    [SerializeField] private HubSupper supper;

    public int Seat => seat;

    private static AsylumHub Hub => AsylumHub.Active;

    private static bool Busy()
    {
        AsylumHub hub = Hub;
        return hub == null || hub.IsTransitioning || hub.Leaving || hub.PursuerAfterPlayer();
    }

    string IInteractable.Prompt => L10n.T("hub.supper.card.prompt");

    float IInteractable.HoldSeconds => 0f;

    bool IInteractable.CanInteract => supper != null && supper.CardNamed(seat) && GameFlow.IsInHub && Hub != null && Hub.Hud != null && !Busy();

    void IInteractable.Interact(PlayerInteractor who)
    {
        AsylumHub hub = Hub;
        if (hub == null || hub.Hud == null || supper == null || !supper.CardNamed(seat) || Busy()) return;
        hub.Hud.ShowSubtitle(L10n.T("hub.supper.card." + seat), 4.5f);
        GameAudio.PlayAt(GameAudio.Library.paper, transform.position, 0.4f);
    }

    void IInteractable.OnFocus(bool focused) { }
}
