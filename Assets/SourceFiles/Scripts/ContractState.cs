using UnityEngine;

/// <summary>F87: one optional side job the salesman can offer for the next floor.</summary>
public struct ContractDef
{
    /// <summary>S9: the key stem in the Shop table (shop.contract.&lt;Slug&gt;.name / .text).</summary>
    public string Slug;
    public int BaseReward;

    /// <summary>The translated contract name. Carries no final period (plan section 6); a sentence that shows it adds one.</summary>
    public string Name => L10n.T("shop.contract." + Slug + ".name");
    public string Text => L10n.T("shop.contract." + Slug + ".text");
}

/// <summary>
/// F87 Contracts: the campaign-scoped part. Which contracts the shop is offering, and which one (at most) the player
/// has taken and for which floor. Statics, reset at SubsystemRegistration and in GameFlow.ReturnToMenu (the
/// static-reset Gotcha); SaveSystem snapshots them (CaptureTo/RestoreFrom), so a shop resume shows the same two offers
/// and a resumed floor keeps its contract. The per-floor checking lives in ContractTracker (an instance component on the
/// player). A contract never gates the hatch and never touches GameManager's counting, like ShardPickup and KeyPickup.
///
/// Ids are indexes into <see cref="Defs"/>: append only, never reorder (they are stored in saves).
/// </summary>
public static class ContractState
{
    public const int NoHide = 0, Untouched = 1, DarkWalk = 2, Scavenger = 3, Reader = 4, Quick = 5, Distract = 6, Silent = 7;

    /// <summary>F122: the three contracts earned through the Patient Record (ids 8-10, append only). Offered only once unlocked; the base eight are rolled exactly as before.</summary>
    public const int Unseen = 8, HandsToYourself = 9, CutItClose = 10;

    /// <summary>CUT IT CLOSE: the floor must be cleared with at most this many seconds left on the countdown.</summary>
    public const float CutItCloseSeconds = 20f;

    /// <summary>Seconds with the torch off while moving that DarkWalk needs.</summary>
    public const float DarkWalkSeconds = 60f;
    /// <summary>Fraction of the floor's shards Scavenger needs.</summary>
    public const float ScavengerFraction = 0.6f;
    /// <summary>Hunter investigations Distract needs.</summary>
    public const int DistractCount = 2;

    private static readonly ContractDef[] Defs =
    {
        new ContractDef { Slug = "never_hide", BaseReward = 40 },
        new ContractDef { Slug = "not_a_scratch", BaseReward = 50 },
        new ContractDef { Slug = "walk_in_dark", BaseReward = 35 },
        new ContractDef { Slug = "take_what_glitters", BaseReward = 30 },
        new ContractDef { Slug = "read_it_all", BaseReward = 25 },
        new ContractDef { Slug = "dont_dawdle", BaseReward = 45 },
        new ContractDef { Slug = "make_noise", BaseReward = 40 },
        new ContractDef { Slug = "light_feet", BaseReward = 35 },
        new ContractDef { Slug = "unseen", BaseReward = 45 },
        new ContractDef { Slug = "hands_to_yourself", BaseReward = 30 },
        new ContractDef { Slug = "cut_it_close", BaseReward = 40 },
    };

    public static int Count => Defs.Length;

    /// <summary>The id of the contract taken for <see cref="ActiveFloor"/>, or -1 for none.</summary>
    public static int ActiveId { get; private set; } = -1;

    /// <summary>The floor the taken contract applies to (the one after the shop that offered it).</summary>
    public static int ActiveFloor { get; private set; }

    /// <summary>The two ids the shop board shows, or -1 when there is no offer (no shop visit yet).</summary>
    public static int OfferA { get; private set; } = -1;
    public static int OfferB { get; private set; } = -1;

    /// <summary>The Subtitles KEY (S9) of a line for the salesman to say next time the shop opens (paid / failed), consumed once. Transient: not saved.</summary>
    public static string SalesmanLine;

    public static bool HasOffers => OfferA >= 0 && OfferB >= 0;
    public static bool HasActive => ActiveId >= 0;

    public static ContractDef Def(int id) => Defs[Mathf.Clamp(id, 0, Defs.Length - 1)];

    /// <summary>Minutes DON'T DAWDLE allows on a floor: 6 on floor 2 up to 9 on floor 5 (the shop only follows floors 1-4).</summary>
    public static float QuickSeconds(int floor) => (4 + Mathf.Max(1, floor)) * 60f;

    /// <summary>The base reward scaled gently with floor (x(1 + 0.15 * (floor - 1))), rounded to 5.</summary>
    public static int Reward(int id, int floor)
    {
        float scaled = Def(id).BaseReward * (1f + 0.15f * Mathf.Max(0, floor - 1));
        return Mathf.Max(5, Mathf.RoundToInt(scaled / 5f) * 5);
    }

    /// <summary>
    /// Picks two distinct offers from a hash of (the seed of the floor just cleared, that floor). Deterministic, so
    /// rolling twice for the same floor shows the same pair. Cancels nothing already taken.
    /// </summary>
    public static void RollOffers(int usedSeed, int floor)
    {
        int h = unchecked(usedSeed * 73856093 ^ floor * 19349663 ^ 0x2F6B1D);
        System.Random rng = new System.Random(h);
        // F122: drawn from the offer pool - the eight base contracts plus the Patient Record's unlocked ones. With no unlocks pool[i] == i, so the draws (and the pair) are identical to before.
        int[] pool = OfferPool();
        int a = rng.Next(pool.Length);
        int b = rng.Next(pool.Length - 1);
        OfferA = pool[a];
        OfferB = pool[b >= a ? b + 1 : b];
    }

    /// <summary>F122: the ids a shop may offer - 0..7 always, then each id the Patient Record has unlocked, in id order.</summary>
    public static int[] OfferPool()
    {
        System.Collections.Generic.List<int> pool = new System.Collections.Generic.List<int>();
        for (int i = 0; i < Defs.Length; i++)
        {
            if (i < BaseCount || PatientRecord.IsContractUnlocked(i)) pool.Add(i);
        }
        return pool.ToArray();
    }

    /// <summary>The contracts that were always offered (ids 0..7).</summary>
    public const int BaseCount = 8;

    /// <summary>Takes an offered contract for the given floor. One at most: replaces any earlier take.</summary>
    public static void Take(int id, int forFloor)
    {
        if (id < 0 || id >= Defs.Length) return;
        ActiveId = id;
        ActiveFloor = forFloor;
    }

    /// <summary>The contract is over (paid, failed or declined).</summary>
    public static void ClearActive()
    {
        ActiveId = -1;
        ActiveFloor = 0;
    }

    public static void ClearOffers()
    {
        OfferA = -1;
        OfferB = -1;
    }

    public static string TakeSalesmanLine()
    {
        string line = SalesmanLine;
        SalesmanLine = null;
        return line;
    }

    public static void ResetCampaign()
    {
        ClearActive();
        ClearOffers();
        SalesmanLine = null;
    }

    /// <summary>F83: ids are stored as id + 1 so a save written before F87 (fields missing, read as 0) means "none".</summary>
    public static void CaptureTo(SaveData data)
    {
        data.contractActivePlus1 = ActiveId + 1;
        data.contractFloor = ActiveFloor;
        data.contractOfferAPlus1 = OfferA + 1;
        data.contractOfferBPlus1 = OfferB + 1;
    }

    public static void RestoreFrom(SaveData data)
    {
        ResetCampaign();
        int active = data.contractActivePlus1 - 1;
        if (active >= 0 && active < Defs.Length)
        {
            ActiveId = active;
            ActiveFloor = data.contractFloor;
        }
        int a = data.contractOfferAPlus1 - 1;
        int b = data.contractOfferBPlus1 - 1;
        if (a >= 0 && a < Defs.Length && b >= 0 && b < Defs.Length)
        {
            OfferA = a;
            OfferB = b;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();
}
