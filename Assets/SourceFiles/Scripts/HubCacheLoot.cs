using UnityEngine;

/// <summary>
/// F114: which of the seven hub room finds (HubCache) have been searched this campaign. A bitmask by cache id (0..6), saved at the floor
/// checkpoints through SaveData.hubCachesSearched (missing in an older save = 0 = nothing searched, so no Version bump), cleared with the
/// other campaign statics (GameFlow.ReturnToMenu, DescentRun, SubsystemRegistration). Known gap, accepted: saves are whole floors, so
/// searching and then quitting before entering the wing gives the cache back on CONTINUE (the shards with it - the wallet is floor-start too).
/// </summary>
public static class HubCaches
{
    public const int Count = 7;

    /// <summary>Bit n = cache n has been searched.</summary>
    public static int SearchedMask { get; private set; }

    public static bool IsSearched(int id) => id >= 0 && id < Count && (SearchedMask & (1 << id)) != 0;

    public static void MarkSearched(int id)
    {
        if (id >= 0 && id < Count) SearchedMask |= 1 << id;
    }

    public static void ResetCampaign() => SearchedMask = 0;

    public static void CaptureTo(SaveData data) => data.hubCachesSearched = SearchedMask;

    public static void RestoreFrom(SaveData data) => SearchedMask = data.hubCachesSearched & ((1 << Count) - 1);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();
}

/// <summary>
/// F114: what a hub room find can hold. Plain static rules, like DescentLoot: a weighted roll (UnityEngine.Random, made when the search
/// completes), the "does it fit in the player's hands right now" test and the grant. Items are the shop's own (<see cref="ShopItem"/>).
/// A rolled reward that does not fit becomes shards - a cache is never empty. FocusLens is never given (it is "this level only" and the hub
/// is not a level). "Next floor" items go to PlayerInventory.PendingModifiers, which AsylumHub's wing walk-in already banks.
/// </summary>
public static class HubCacheLoot
{
    public enum Kind { Shards, Item, Bottles, NextFloorItem }

    /// <summary>What a search gave: the kind, the shop item it names (unused for shards) and the shard amount.</summary>
    public struct Reward
    {
        public Kind Kind;
        public ShopItem Item;
        public int Shards;
    }

    private struct Entry
    {
        public Kind Kind;
        public ShopItem Item;
        public int Weight;

        public Entry(Kind kind, ShopItem item, int weight)
        {
            Kind = kind;
            Item = item;
            Weight = weight;
        }
    }

    // Shards are the fallback, so they are row 0; the others follow in the order of the plan's table.
    private static readonly Entry[] Table =
    {
        new Entry(Kind.Shards, ShopItem.SpareBattery, 45),
        new Entry(Kind.Item, ShopItem.SpareBattery, 18),
        new Entry(Kind.Item, ShopItem.StarCompass, 10),
        new Entry(Kind.Bottles, ShopItem.BottlePack, 8),
        new Entry(Kind.NextFloorItem, ShopItem.QuietShoes, 6),
        new Entry(Kind.NextFloorItem, ShopItem.StaminaTonic, 6),
        new Entry(Kind.Item, ShopItem.SecondWind, 4),
        new Entry(Kind.Item, ShopItem.FloorPlan, 3),
    };

    /// <summary>Bottles a bottle find gives (fewer if the hands fill up).</summary>
    public const int BottlesGiven = 2;

    /// <summary>The shard find on `floor` (1..5): 12 on floor 1 up to 28 on floor 5.</summary>
    public static int ShardsFor(int floor) => 8 + 4 * Mathf.Clamp(floor, 1, 5);

    /// <summary>The weighted roll for `floor`; a row that does not fit right now becomes shards. `throws` may be null (then bottles never fit).</summary>
    public static Reward Roll(int floor, ThrowController throws)
    {
        int total = 0;
        foreach (Entry entry in Table) total += entry.Weight;

        int pick = Random.Range(0, total);
        Entry chosen = Table[0];
        foreach (Entry entry in Table)
        {
            pick -= entry.Weight;
            if (pick >= 0) continue;
            chosen = entry;
            break;
        }

        if (chosen.Kind != Kind.Shards && !Fits(chosen, throws)) chosen = Table[0];
        return new Reward { Kind = chosen.Kind, Item = chosen.Item, Shards = chosen.Kind == Kind.Shards ? ShardsFor(floor) : 0 };
    }

    private static bool Fits(Entry entry, ThrowController throws)
    {
        switch (entry.Kind)
        {
            case Kind.Item:
                return PlayerInventory.CanHoldMore(entry.Item);
            case Kind.Bottles:
                return throws != null && !throws.IsFull;
            case Kind.NextFloorItem:
                return !PlayerInventory.HasPending(entry.Item) && !PlayerInventory.HasActive(entry.Item);
            default:
                return true;
        }
    }

    /// <summary>Gives the reward. Held items through PlayerInventory.Grant, bottles into the hands, perks into PendingModifiers, shards into the wallet.</summary>
    public static void Grant(Reward reward, ThrowController throws)
    {
        switch (reward.Kind)
        {
            case Kind.Shards:
                PlayerWallet.AwardBonus(reward.Shards);
                break;
            case Kind.Item:
                PlayerInventory.Grant(reward.Item);
                break;
            case Kind.Bottles:
                for (int i = 0; i < BottlesGiven && throws != null && throws.TryPickUp(ThrowableItem.Kind.Bottle); i++) { }
                break;
            case Kind.NextFloorItem:
                PlayerInventory.PendingModifiers.Add(reward.Item);
                break;
        }
    }

    /// <summary>The subtitle for a reward (Hub table: hub.cache.found / found_next / found_shards).</summary>
    public static string Line(Reward reward)
    {
        if (reward.Kind == Kind.Shards) return L10n.T("hub.cache.found_shards", ("n", reward.Shards), ("currency", PlayerWallet.CurrencyName));
        string name = ShopCatalogue.Get(reward.Item)?.Name ?? reward.Item.ToString();
        return L10n.T(reward.Kind == Kind.NextFloorItem ? "hub.cache.found_next" : "hub.cache.found", ("item", name));
    }
}
