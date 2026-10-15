using UnityEngine;

/// <summary>
/// F117: A HOT MEAL. On the asylum kitchen's serving table stand three dishes - broth, black bread, cold tea - and from the second floor on
/// the player may take ONE of them per hub visit (floors 2..5, never in the finale): a small buff for the next floor, free. Broth = the
/// Stamina Tonic floor modifier, bread = a Spare Battery (only if the hands are not full), tea = Quiet Shoes (both modifiers go to
/// <see cref="PlayerInventory.PendingModifiers"/>, which AsylumHub's wing walk-in banks exactly like a Commissary purchase). A dish whose
/// buff the player already has (bought in the Commissary or found) answers ALREADY HAD SOME and cannot be taken.
///
/// Once per floor across reloads: the wing entry rebuilds the maze (a scene reload), so the "taken" flag lives in <see cref="HubState.MealFloor"/>
/// (a campaign static, saved in SaveData.hubMealFloor), never on this component. Taking a meal writes the floor-start checkpoint, like a
/// Commissary purchase. Floor 1 (the first hub visit, before any maze) and the finale show empty, dusty dishes.
///
/// Lives on AsylumHub/Meal (a hand-editable part of the hub prefab; AsylumHub switches it with the other content and calls <see cref="Refresh"/>
/// at black on every arrival). The builder made the dishes, tray and steam; this component only switches their state.
/// </summary>
public class HubMeal : MonoBehaviour
{
    public enum Blocked { None, HandsFull, AlreadyHad }

    [Tooltip("Broth, black bread, cold tea (in HubMealDish.Dish order).")]
    public HubMealDish[] dishes = new HubMealDish[0];
    [Tooltip("Steam rising from the dishes while they are on offer.")]
    public ParticleSystem[] steam = new ParticleSystem[0];
    [Tooltip("The warm light over the tray. HubDecay never blinks it.")]
    public Light light;

    private static AudioClip s_clink;

    private static AsylumHub Hub => AsylumHub.Active;

    /// <summary>True once the builder has made the three dishes.</summary>
    public bool IsComplete => dishes != null && dishes.Length == 3 && System.Array.IndexOf(dishes, null) < 0;

    /// <summary>The floor the meal is for: the floor whose wing is open (HubState.Floor).</summary>
    public static int MealFloor => HubState.Floor;

    /// <summary>A meal may be taken now: floors 2..5, not the finale, and not already eaten for this floor.</summary>
    public static bool IsOnOffer => !HubState.IsFinale && MealFloor >= 2 && HubState.MealFloor != MealFloor;

    /// <summary>Why a dish cannot be taken right now (None = it can).</summary>
    public static Blocked BlockedFor(HubMealDish.Dish dish)
    {
        switch (dish)
        {
            case HubMealDish.Dish.Bread: return PlayerInventory.CanHoldMore(ShopItem.SpareBattery) ? Blocked.None : Blocked.HandsFull;
            case HubMealDish.Dish.Broth: return Has(ShopItem.StaminaTonic) ? Blocked.AlreadyHad : Blocked.None;
            default: return Has(ShopItem.QuietShoes) ? Blocked.AlreadyHad : Blocked.None;
        }
    }

    private static bool Has(ShopItem modifier) => PlayerInventory.HasPending(modifier) || PlayerInventory.HasActive(modifier);

    /// <summary>Puts the dishes and the steam in the state of the current floor. Called by AsylumHub.EnterRoutine at black, and after a dish is taken.</summary>
    public void Refresh()
    {
        bool offer = IsOnOffer;
        foreach (HubMealDish d in dishes) if (d != null) d.SetOnOffer(offer);
        foreach (ParticleSystem s in steam)
        {
            if (s == null) continue;
            if (offer && !s.isPlaying) s.Play();
            else if (!offer && s.isPlaying) s.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }
    }

    /// <summary>E on a dish that can be taken: gives its buff, marks the floor's meal eaten, saves, says so.</summary>
    internal void Take(HubMealDish.Dish dish)
    {
        AsylumHub hub = Hub;
        if (hub == null || !IsOnOffer || BlockedFor(dish) != Blocked.None) return;

        switch (dish)
        {
            case HubMealDish.Dish.Broth: PlayerInventory.PendingModifiers.Add(ShopItem.StaminaTonic); break;
            case HubMealDish.Dish.Bread: PlayerInventory.Grant(ShopItem.SpareBattery); break;
            default: PlayerInventory.PendingModifiers.Add(ShopItem.QuietShoes); break;
        }
        HubState.MealFloor = MealFloor;
        SaveSystem.WriteCheckpoint(GameFlow.CurrentFloor, false); // the same floor-start save a Commissary purchase writes

        if (hub.Hud != null) hub.Hud.ShowSubtitle(L10n.T("hub.meal." + HubMealDish.Key(dish) + ".taken"), 4f);
        Vector3 at = dishes[(int)dish] != null ? dishes[(int)dish].transform.position : transform.position;
        if (s_clink == null) s_clink = DoorAudio.BuildClunkClip(0.08f, 900f, 0.2f);
        GameAudio.ClipAtPoint(s_clink, at, 0.7f); // a spoon on the rim
        Refresh();
    }

    private void OnDestroy()
    {
        if (s_clink != null) { Destroy(s_clink); s_clink = null; }
    }
}
