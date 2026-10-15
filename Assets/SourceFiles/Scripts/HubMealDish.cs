using UnityEngine;

/// <summary>
/// F117: one dish of the kitchen's hot meal (broth, black bread or cold tea) - an <see cref="IInteractable"/> found by PlayerInteractor's camera
/// cast like every other hub interactable; the trigger collider on this object is what the cast hits. <see cref="HubMeal"/> decides what is on
/// offer and gives the buff; this component shows the full dish while the meal is on offer and the empty (dusty) one otherwise, and answers
/// HANDS FULL / ALREADY HAD SOME for a dish that cannot be taken. Refused while a hub pursuer is after the player or a transition runs.
/// </summary>
public class HubMealDish : MonoBehaviour, IInteractable
{
    public enum Dish { Broth, Bread, Tea }

    [SerializeField] private Dish dish = Dish.Broth;
    [SerializeField] private HubMeal meal;
    [Tooltip("The dish with its food (shown while the meal is on offer).")]
    [SerializeField] private GameObject full;
    [Tooltip("The same dish empty and dusty (shown on floor 1, in the finale and once the floor's meal is eaten).")]
    [SerializeField] private GameObject empty;

    private bool _onOffer;

    public Dish Kind => dish;
    public bool OnOffer => _onOffer;

    /// <summary>The key part of this dish in the Hub table: broth, bread, tea.</summary>
    public static string Key(Dish d) => d.ToString().ToLowerInvariant();

    private static AsylumHub Hub => AsylumHub.Active;

    /// <summary>Shows the full or the empty dish; the prompt is only offered while the meal is on offer.</summary>
    public void SetOnOffer(bool onOffer)
    {
        _onOffer = onOffer;
        if (full != null) full.SetActive(onOffer);
        if (empty != null) empty.SetActive(!onOffer);
    }

    private static bool Busy()
    {
        AsylumHub hub = Hub;
        return hub == null || hub.IsTransitioning || hub.Leaving || hub.PursuerAfterPlayer();
    }

    string IInteractable.Prompt
    {
        get
        {
            switch (HubMeal.BlockedFor(dish))
            {
                case HubMeal.Blocked.HandsFull: return L10n.T("hub.meal.full");
                case HubMeal.Blocked.AlreadyHad: return L10n.T("hub.meal.had");
                default: return L10n.T("hub.meal." + Key(dish) + ".prompt");
            }
        }
    }

    float IInteractable.HoldSeconds => 0f;

    bool IInteractable.CanInteract => _onOffer && meal != null && GameFlow.IsInHub && Hub != null && !Busy() && HubMeal.IsOnOffer;

    void IInteractable.Interact(PlayerInteractor who)
    {
        AsylumHub hub = Hub;
        if (hub == null || meal == null || !_onOffer || Busy()) return;
        switch (HubMeal.BlockedFor(dish))
        {
            case HubMeal.Blocked.HandsFull:
                if (hub.Hud != null) hub.Hud.ShowSubtitle(L10n.T("hub.meal.full_line"), 2.4f);
                return;
            case HubMeal.Blocked.AlreadyHad:
                if (hub.Hud != null) hub.Hud.ShowSubtitle(L10n.T("hub.meal.had_line"), 2.4f);
                return;
        }
        meal.Take(dish);
    }

    void IInteractable.OnFocus(bool focused) { }
}
