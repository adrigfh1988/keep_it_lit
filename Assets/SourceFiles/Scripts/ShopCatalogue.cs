using UnityEngine;

/// <summary>Every purchasable thing in the shop. Kept as an enum rather than a string, so a typo fails at compile time.</summary>
public enum ShopItem
{
    SpareBattery,
    StarCompass,
    SecondWind,
    LongFuse,
    QuietShoes,
    ExtraLamps,
    StaminaTonic,
    TorchColour,
    HudTint,
    BottlePack,
    FocusLens,
    FuseMap,
    FloorPlan,
    // F122: earned through the Patient Record (ShopItemDef.Requires); appended after FloorPlan, saves store ints.
    Dynamo,
    ArchivistLamp,
    Stethoscope
}

/// <summary>What kind of thing a ShopItem is - drives how ShopMenu draws its card and how PlayerInventory holds it.</summary>
public enum ItemKind
{
    Consumable,
    SecondWind,
    FloorModifier,
    Cosmetic
}

/// <summary>Plain data for one shop entry. No behaviour: PlayerWallet/PlayerInventory decide what buying one does.</summary>
public sealed class ShopItemDef
{
    public ShopItem Id;
    public ItemKind Kind;
    /// <summary>S9: key stem in the Shop table (shop.item.&lt;Slug&gt;.name / .blurb).</summary>
    public string Slug;
    public int BasePrice;
    public int Cap;

    /// <summary>F122: the Patient Record milestone that puts this card on the shelf (null = always sold).</summary>
    public Unlock? Requires;

    /// <summary>The translated item name (Title Case in English).</summary>
    public string Name => L10n.T("shop.item." + Slug + ".name");

    // F119: the bound key, gamepad button or (touch keeps the keyboard label, as before) key shown in the blurb.
    private string BlurbKey()
    {
        GameAction a = Id == ShopItem.SpareBattery ? GameAction.Item1 : Id == ShopItem.FloorPlan ? GameAction.Map : GameAction.Item2;
        return InputGlyphs.Key(a, InputGlyphs.KeyboardLabel(a));
    }

    /// <summary>The translated description. {key} is the bound key, gamepad button or touch word that uses the item (F119).</summary>
    public string Blurb => L10n.T("shop.item." + Slug + ".blurb", ("key", BlurbKey()));
}

/// <summary>
/// Item definitions, prices and cosmetic palettes. Plain static data, exactly like FloorProfile - no
/// ScriptableObjects, no prefabs. PlayerWallet and PlayerInventory are the only things that mutate state.
/// </summary>
public static class ShopCatalogue
{
    /// <summary>Price grows 20% per shop floor past the first, so the late game is roomier but never buys everything.</summary>
    public const float PriceGrowthPerFloor = 0.2f;

    public static readonly ShopItemDef[] Items =
    {
        new ShopItemDef { Id = ShopItem.SpareBattery, Kind = ItemKind.Consumable, Slug = "spare_battery",
            BasePrice = 30, Cap = 3 },
        new ShopItemDef { Id = ShopItem.StarCompass, Kind = ItemKind.Consumable, Slug = "star_compass",
            BasePrice = 25, Cap = 3 },
        new ShopItemDef { Id = ShopItem.SecondWind, Kind = ItemKind.SecondWind, Slug = "second_wind",
            BasePrice = 120, Cap = 1 },
        new ShopItemDef { Id = ShopItem.LongFuse, Kind = ItemKind.FloorModifier, Slug = "long_fuse",
            BasePrice = 35, Cap = 1 },
        new ShopItemDef { Id = ShopItem.QuietShoes, Kind = ItemKind.FloorModifier, Slug = "quiet_shoes",
            BasePrice = 40, Cap = 1 },
        new ShopItemDef { Id = ShopItem.ExtraLamps, Kind = ItemKind.FloorModifier, Slug = "extra_lamps",
            BasePrice = 35, Cap = 1 },
        new ShopItemDef { Id = ShopItem.StaminaTonic, Kind = ItemKind.FloorModifier, Slug = "stamina_tonic",
            BasePrice = 30, Cap = 1 },
        new ShopItemDef { Id = ShopItem.TorchColour, Kind = ItemKind.Cosmetic, Slug = "torch_colour",
            BasePrice = 50, Cap = 0 },
        // F110: the Floor Plan took the retired HUD Tint card's slot (the enum value HudTint stays, saves store ints).
        new ShopItemDef { Id = ShopItem.FloorPlan, Kind = ItemKind.Consumable, Slug = "floor_plan",
            BasePrice = 50, Cap = 1 },

        // F75 shop depth.
        new ShopItemDef { Id = ShopItem.BottlePack, Kind = ItemKind.FloorModifier, Slug = "bottle_pack",
            BasePrice = 40, Cap = 1 },
        new ShopItemDef { Id = ShopItem.FocusLens, Kind = ItemKind.FloorModifier, Slug = "focus_lens",
            BasePrice = 35, Cap = 1 },
        new ShopItemDef { Id = ShopItem.FuseMap, Kind = ItemKind.FloorModifier, Slug = "fuse_map",
            BasePrice = 25, Cap = 1 },
    };

    /// <summary>F122: cards earned through the Patient Record. Kept apart from <see cref="Items"/> so the base shelf (and everything that counts it) is untouched; <see cref="Available"/> joins them.</summary>
    public static readonly ShopItemDef[] Unlockable =
    {
        new ShopItemDef { Id = ShopItem.Dynamo, Kind = ItemKind.FloorModifier, Slug = "dynamo", BasePrice = 35, Cap = 1, Requires = Unlock.CardDynamo },
        new ShopItemDef { Id = ShopItem.ArchivistLamp, Kind = ItemKind.FloorModifier, Slug = "archivist_lamp", BasePrice = 30, Cap = 1, Requires = Unlock.CardArchivist },
        new ShopItemDef { Id = ShopItem.Stethoscope, Kind = ItemKind.FloorModifier, Slug = "stethoscope", BasePrice = 30, Cap = 1, Requires = Unlock.CardStethoscope },
    };

    /// <summary>F122: the swatches on the TORCH COLOUR card (palette entries 1..3). Entries past them are earned, never sold.</summary>
    public const int ShopTorchSwatches = 3;

    /// <summary>F122: the shelf as the shop shows it - the 12 base cards, then each unlocked extra card (at most 16).</summary>
    public static System.Collections.Generic.List<ShopItemDef> Available()
    {
        System.Collections.Generic.List<ShopItemDef> list = new System.Collections.Generic.List<ShopItemDef>(Items);
        foreach (ShopItemDef def in Unlockable)
        {
            if (def.Requires == null || PatientRecord.IsUnlocked(def.Requires.Value)) list.Add(def);
        }
        if (list.Count > 16) list.RemoveRange(16, list.Count - 16);
        return list;
    }

    public static ShopItemDef Get(ShopItem id)
    {
        foreach (ShopItemDef def in Items)
        {
            if (def.Id == id) return def;
        }
        foreach (ShopItemDef def in Unlockable)
        {
            if (def.Id == id) return def;
        }
        return null;
    }

    /// <summary>shopFloor is the floor just cleared - the room is visited before GameFlow.NextFloor.</summary>
    public static int PriceFor(ShopItemDef def, int shopFloor)
    {
        if (def == null) return 0;
        return Mathf.RoundToInt(def.BasePrice * (1f + PriceGrowthPerFloor * Mathf.Max(0, shopFloor - 1)));
    }

    /// <summary>
    /// F100: the lowest price (at the prices of `shopFloor`, the floor just cleared) among the things the player could still buy right now,
    /// ignoring the wallet; -1 when there is nothing left to buy. A held-out consumable, a held Second Wind and a modifier already
    /// bought for the next floor do not count; a cosmetic counts while a palette entry is still unowned.
    /// </summary>
    public static int CheapestOpenPrice(int shopFloor)
    {
        int best = -1;
        foreach (ShopItemDef def in Available()) // F122: the unlocked extra cards count too
        {
            bool open;
            switch (def.Kind)
            {
                case ItemKind.Consumable: open = PlayerInventory.CanHoldMore(def.Id); break;
                case ItemKind.SecondWind: open = PlayerInventory.Count(def.Id) == 0; break;
                case ItemKind.FloorModifier: open = !PlayerInventory.HasPending(def.Id); break;
                default:
                {
                    bool torch = def.Id == ShopItem.TorchColour;
                    open = false;
                    int count = ShopTorchSwatches + 1; // F122: the earned colours past the swatches are never sold
                    for (int i = 1; i < count && !open; i++) open = !PlayerInventory.OwnsTorchColour(i);
                    break;
                }
            }
            if (!open) continue;
            int price = PriceFor(def, shopFloor);
            if (best < 0 || price < best) best = price;
        }
        return best;
    }

    /// <summary>S9: the translated palette entry name (ALL CAPS in the table). The tuple Name below stays the English id.</summary>
    public static string PaletteName(bool torch, int index) => L10n.T((torch ? "shop.palette.torch." : "shop.palette.hud.") + index);

    // Index 0 is the default look and is never for sale - GrantTorchColour/GrantHudTint start owning it.
    public static readonly (string Name, Color Colour)[] TorchColours =
    {
        ("Warm",   new Color(1f, 0.95f, 0.86f)),   // Flashlight's beamColor default
        ("Cold",   new Color(0.78f, 0.88f, 1f)),
        ("Ember",  new Color(1f, 0.72f, 0.45f)),
        ("Violet", new Color(0.82f, 0.68f, 1f)),
        // F122: earned through the Patient Record, never sold; owned from the start of a campaign (CampaignRules.Begin) and chosen on the new-campaign screen.
        ("Bone",      new Color(0.93f, 0.93f, 0.88f)),
        ("WardGreen", new Color(0.72f, 1f, 0.80f)),
        ("OathRed",   new Color(1f, 0.55f, 0.50f)),
    };

    public static readonly (string Name, Color Colour)[] HudTints =
    {
        ("Bone",     new Color(0.86f, 0.86f, 0.9f)),  // PlayerHud.CalmColor / TensionDirector.calmColor default
        ("Phosphor", new Color(0.62f, 1f, 0.7f)),
        ("Amber",    new Color(1f, 0.78f, 0.45f)),
        ("Ice",      new Color(0.7f, 0.9f, 1f)),
    };
}
