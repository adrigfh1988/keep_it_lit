using UnityEngine;

/// <summary>
/// F116: one of the Commissary's two E targets (the counter and the contract board), found by PlayerInteractor's camera cast like every other
/// interactable in the hub - so E never double-fires with whatever else is in view. The collider on this object (or a child) is what the cast
/// hits. Interacting asks <see cref="HubShop"/>, which opens the shop menu or the contract panel unless a hub pursuer is after the player.
/// </summary>
public class HubShopService : MonoBehaviour, IInteractable
{
    public enum Kind { Counter, Contracts }

    public Kind kind = Kind.Counter;
    public HubShop shop;

    public string Prompt => L10n.T(kind == Kind.Counter ? "hud.prompt.talk" : "hud.prompt.contracts");

    public bool CanInteract => shop != null && shop.IsOpen && GameFlow.IsInHub && !shop.MenuOpen;
    public float HoldSeconds => 0f;
    public void OnFocus(bool focused) { }

    public void Interact(PlayerInteractor who)
    {
        if (shop != null) shop.OpenService(kind);
    }
}
