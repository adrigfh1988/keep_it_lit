using UnityEngine;

/// <summary>
/// F107 C: what a DESCENT item cache can hold. Plain static data and rules, like ShopCatalogue: the weighted roll (made once, at placement,
/// from the +24 stream), the "does it fit in the player's hands right now" test, and the grant itself. Items are the shop's own
/// (<see cref="ShopItem"/>), so names and effects are the ones the player already knows; nothing is bought.
/// </summary>
public static class DescentLoot
{
    /// <summary>One row of the loot table. `NextLevel` items go to PlayerInventory.PendingModifiers and apply from the next level on.</summary>
    private struct Entry
    {
        public ShopItem Item;
        public int Weight;
        public bool NextLevel;

        public Entry(ShopItem item, int weight, bool nextLevel)
        {
            Item = item;
            Weight = weight;
            NextLevel = nextLevel;
        }
    }

    // Order matters: when the rolled item does not fit, the cache gives the next row that does.
    private static readonly Entry[] Table =
    {
        new Entry(ShopItem.SpareBattery, 25, false),
        new Entry(ShopItem.StarCompass, 15, false),
        new Entry(ShopItem.BottlePack, 15, false),
        new Entry(ShopItem.FocusLens, 10, false),
        new Entry(ShopItem.SecondWind, 4, false),
        new Entry(ShopItem.LongFuse, 8, true),
        new Entry(ShopItem.StaminaTonic, 8, true),
        new Entry(ShopItem.QuietShoes, 8, true),
        new Entry(ShopItem.ExtraLamps, 5, true),
        new Entry(ShopItem.FuseMap, 5, true),
    };

    /// <summary>A re-rolled Second Wind is rolled again at most this many times before the cache settles for a Spare Battery.</summary>
    private const int MaxRerolls = 5;
    /// <summary>A Second Wind turns up at most once per this many depths.</summary>
    private const int SecondWindSpacing = 5;
    private const int BottlesGiven = 3;
    private const float FocusLensRange = 1.35f;
    private const float FocusLensDrain = 1f;

    /// <summary>True for the items that only apply from the next level (the cache's subtitle says so).</summary>
    public static bool IsNextLevel(ShopItem item)
    {
        foreach (Entry entry in Table)
        {
            if (entry.Item == item) return entry.NextLevel;
        }
        return false;
    }

    /// <summary>
    /// The weighted roll for a cache at `depth`. A Second Wind that is not allowed (one is held, or one was found fewer than five depths ago) is
    /// drawn again from the same stream, at most <see cref="MaxRerolls"/> times, then it falls back to a Spare Battery - deterministic for a seed.
    /// </summary>
    public static ShopItem Roll(System.Random rng, int depth)
    {
        int total = 0;
        foreach (Entry entry in Table) total += entry.Weight;

        for (int attempt = 0; attempt <= MaxRerolls; attempt++)
        {
            int pick = rng.Next(total);
            foreach (Entry entry in Table)
            {
                pick -= entry.Weight;
                if (pick >= 0) continue;
                if (entry.Item != ShopItem.SecondWind || CanGrantSecondWind(depth)) return entry.Item;
                break; // a Second Wind that is not allowed: roll again
            }
        }

        return ShopItem.SpareBattery;
    }

    /// <summary>F110: percent of DESCENT levels with a Floor Plan in one of their caches. Decided by a seed hash, not by the +24 stream.</summary>
    public const int FloorPlanPercent = 40;

    /// <summary>F110: whether the level at `depth` of the maze built from `seed` hides a Floor Plan (a pure function: no rng draw, same answer on a retry).</summary>
    public static bool LevelHasFloorPlan(int seed, int depth)
    {
        int hash = RunRules.HashSeedFloor(seed ^ 0x464C504E, depth); // "FLPN"
        return (hash & 0x7FFFFFFF) % 100 < FloorPlanPercent;
    }

    /// <summary>A Second Wind only while none is held and the last one is at least five depths back.</summary>
    public static bool CanGrantSecondWind(int depth) =>
        PlayerInventory.Count(ShopItem.SecondWind) == 0 && depth - DescentRun.LastSecondWindDepth >= SecondWindSpacing;

    /// <summary>Whether `item` can be given right now. `throws` and `flashlight` may be null (an incomplete scene): the items that need them then never fit.</summary>
    public static bool Fits(ShopItem item, ThrowController throws, Flashlight flashlight, int depth)
    {
        switch (item)
        {
            case ShopItem.SpareBattery:
            case ShopItem.StarCompass:
                return PlayerInventory.CanHoldMore(item);
            case ShopItem.SecondWind:
                return CanGrantSecondWind(depth);
            case ShopItem.FloorPlan:
                return PlayerInventory.Count(ShopItem.FloorPlan) == 0; // F110: a held plan is unused - the cap is 1
            case ShopItem.BottlePack:
                return throws != null && !throws.IsFull;
            case ShopItem.FocusLens:
                return flashlight != null && !PlayerInventory.HasActive(ShopItem.FocusLens);
            default:
                return !PlayerInventory.HasPending(item);
        }
    }

    /// <summary>The rolled item if it fits, otherwise the next row of the table that does. False when nothing fits (hands full).</summary>
    public static bool TryResolve(ShopItem rolled, ThrowController throws, Flashlight flashlight, int depth, out ShopItem result)
    {
        if (rolled == ShopItem.FloorPlan && Fits(rolled, throws, flashlight, depth))
        {
            result = rolled;
            return true;
        }

        int start = 0;
        for (int i = 0; i < Table.Length; i++)
        {
            if (Table[i].Item == rolled) { start = i; break; }
        }

        for (int step = 0; step < Table.Length; step++)
        {
            ShopItem candidate = Table[(start + step) % Table.Length].Item;
            if (!Fits(candidate, throws, flashlight, depth)) continue;
            result = candidate;
            return true;
        }

        result = rolled;
        return false;
    }

    /// <summary>Gives `item`. "Now" items work mid-level; "next level" items wait in PendingModifiers for GameFlow.NextFloor's AdvanceFloor.</summary>
    public static void Grant(ShopItem item, ThrowController throws, Flashlight flashlight, int depth)
    {
        switch (item)
        {
            case ShopItem.SpareBattery:
            case ShopItem.StarCompass:
                PlayerInventory.Grant(item);
                break;
            case ShopItem.FloorPlan:
                PlayerInventory.Grant(item); // held, used on a floor of the player's choosing (FloorMapUi.UsePlan)
                break;
            case ShopItem.SecondWind:
                PlayerInventory.Grant(item);
                DescentRun.LastSecondWindDepth = depth;
                break;
            case ShopItem.BottlePack:
                for (int i = 0; i < BottlesGiven && throws != null && throws.TryPickUp(ThrowableItem.Kind.Bottle); i++) { }
                break;
            case ShopItem.FocusLens:
                // Active, not pending: this level only (AdvanceFloor clears ActiveModifiers); the beam is widened now, as MazeGenerator does at floor start.
                PlayerInventory.ActiveModifiers.Add(ShopItem.FocusLens);
                if (flashlight != null)
                {
                    flashlight.SetRangeMultiplier(FocusLensRange);
                    flashlight.SetFocusedDrainMultiplier(FocusLensDrain);
                }
                break;
            default:
                PlayerInventory.PendingModifiers.Add(item);
                break;
        }
    }
}
