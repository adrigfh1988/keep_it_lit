using System.Text;
using UnityEngine;

/// <summary>
/// F122: the campaign's difficulty layer. A preset (STORY / STANDARD / NIGHTMARE), individual assists and stackable Oaths, written into the knobs
/// that already exist (<see cref="FloorProfile"/> numbers, a clone of <see cref="TerrorSettings"/>, the torch's drain and recharge, the Stalker's first
/// floor, the Second Wind cap, F120's grab and F121's hub breach). Campaign-scoped statics: chosen on the new-campaign screen
/// (<see cref="Begin"/>), saved with the checkpoints (<see cref="CaptureTo"/>/<see cref="RestoreFrom"/>), cleared by <see cref="ResetCampaign"/>
/// (GameFlow.ReturnToMenu, DescentRun.Begin, SubsystemRegistration).
///
/// HARD RULE: STANDARD with no assists, no Oaths and the standard-issue kit is byte-for-byte the game as it was before F122 - every method below
/// returns its input untouched (same reference for the terror settings) and every derived value is exactly 1 / false / none.
/// KeepItLit.Tests.StandardBaselineTests proves it against Assets/Editor/Tests/Fixtures/standard-baseline.txt; re-run it after touching any knob.
/// A DESCENT stays outside presets and Oaths: every reader behaves as STANDARD while <c>GameFlow.IsDescent</c>.
///
/// <b>F123 hook: <see cref="UnreliableCues"/></b> (true = the lying cues are on) is the single flag for "unreliable cues". The HONEST CUES assist
/// turns it off; wing nightmares' <c>lyingKnocks</c> reads it.
/// </summary>
public static class CampaignRules
{
    public static DifficultyPreset Preset { get; private set; } = DifficultyPreset.Standard;
    public static Assist Assists { get; private set; } = Assist.None;
    public static Oath Oaths { get; private set; } = Oath.None;
    public static StartingKit Kit { get; private set; } = StartingKit.StandardIssue;

    /// <summary>A record-affecting assist was switched on at some point this campaign (saved): the campaign stays ASSISTED.</summary>
    public static bool AssistedSticky { get; private set; }

    /// <summary>Set by MazeGenerator.ApplyFloorProfile every build: the <see cref="Signature"/> the current scene was built with. Never saved.</summary>
    public static string BuiltSignature = "";

    private static bool Off => GameFlow.IsDescent;

    // ---------------------------------------------------------------- derived (all exactly 1 / false / none under plain STANDARD)

    public static bool Has(Assist a) => !Off && (Assists & a) != 0;
    public static bool Has(Oath o) => !Off && (Oaths & o) != 0;

    public static bool IsNightmare => !Off && Preset == DifficultyPreset.Nightmare;

    /// <summary>True for STORY, any record-affecting assist, or a campaign that ever had one on. Such a campaign still unlocks things but sets no bests or Oath records and gets no shard multiplier.</summary>
    public static bool IsAssisted => !Off && (Preset == DifficultyPreset.Story || (Assists & OathCatalogue.RecordAffectingAssists) != 0 || AssistedSticky);

    /// <summary>True when nothing at all is changed (the STANDARD baseline).</summary>
    public static bool IsPlain => Off || (Preset == DifficultyPreset.Standard && Assists == Assist.None && Oaths == Oath.None);

    /// <summary>"OATH 3": the sum of the chosen Oaths' weights (+2 on NIGHTMARE).</summary>
    public static int OathRank => Off ? 0 : OathCatalogue.RankOf(Oaths) + (Preset == DifficultyPreset.Nightmare ? 2 : 0);

    /// <summary>1 + the Oaths' bonuses (+25 % on NIGHTMARE); capped at 1 while assisted.</summary>
    public static float ShardMultiplier
    {
        get
        {
            if (Off || IsAssisted) return 1f;
            float m = 1f + OathCatalogue.ShardBonusOf(Oaths) + (Preset == DifficultyPreset.Nightmare ? 0.25f : 0f);
            return Mathf.Approximately(m, 1f) ? 1f : m;
        }
    }

    public static bool SecondWindAllowed => !Has(Oath.NoSecondChances);
    public static bool LockersWelded => Has(Oath.WeldedLockers);

    /// <summary>First story floor with Stan: 4, or 2 with STAN WAKES EARLY.</summary>
    public static int StalkerFromFloor => Has(Oath.StanWakesEarly) ? 2 : EntityRoster.StalkerFrom;

    /// <summary>F123 reads this: true = the lying cues are on (today's game). False only with the HONEST CUES assist, and never in a descent.</summary>
    public static bool UnreliableCues => Off || (Assists & Assist.HonestCues) == 0;

    /// <summary>Torch drain multiplier (NIGHTMARE x1.2, GENTLE TORCH x0.7).</summary>
    public static float DrainScale => (IsNightmare ? 1.2f : 1f) * (Has(Assist.GentleTorch) ? 0.7f : 1f);

    /// <summary>Torch recharge multiplier (NIGHTMARE x0.8, GENTLE TORCH x1.5).</summary>
    public static float RechargeScale => (IsNightmare ? 0.8f : 1f) * (Has(Assist.GentleTorch) ? 1.5f : 1f);

    /// <summary>RunRules bits (1 &lt;&lt; (int)Rule) the Oaths force on every floor: OLD BATTERY = WeakBattery, IT HEARS EVERYTHING = KeenEars.</summary>
    public static int ForcedRuleMask
    {
        get
        {
            int mask = 0;
            if (Has(Oath.OldBattery)) mask |= 1 << (int)RunRules.Rule.WeakBattery;
            if (Has(Oath.HearsEverything)) mask |= 1 << (int)RunRules.Rule.KeenEars;
            return mask;
        }
    }

    // ---------------------------------------------------------------- the knobs

    /// <summary>
    /// Derives the two external switches the rules drive: F121's <c>HubBreach.Mode</c> (THE DOORS DON'T HOLD = every visit) and F120's
    /// <c>HunterGrab.DisabledByRules</c> (NO SECOND CHANCES). Neither is reset by GameFlow.ReturnToMenu on its own where F122 owns it, so MazeGenerator.Awake
    /// calls this every build (covers Begin, Retry, NEXT FLOOR and CONTINUE).
    /// </summary>
    public static void ApplyRunSwitches()
    {
        HubBreach.Mode = Has(Oath.DoorsDontHold) && OathCatalogue.HubBreachSupported ? HubBreach.BreachMode.EveryVisit : HubBreach.BreachMode.OncePerCampaign;
        HunterGrab.DisabledByRules = Has(Oath.NoSecondChances);
    }

    /// <summary>Mutates the floor's profile: preset, then assists, then Oaths (fixed order, so the result is deterministic). A no-op when plain.</summary>
    public static void ApplyToProfile(FloorProfile p)
    {
        if (p == null || IsPlain) return;

        float time = 1f;
        if (IsNightmare)
        {
            time *= 0.85f;
            p.HearingScale *= 1.15f;
            p.ViewDistance *= 1.1f;
            p.ChaseCommitScale *= 0.75f;
            p.HunterEyesLitAtRest = false;
        }

        if (Has(Assist.LongerTimer)) time *= 1.5f;
        if (Has(Assist.SlowHunter))
        {
            p.ChaseCommitScale *= 1.75f;
            p.ChaseWalkFraction *= 0.85f;
            p.RunSpeed *= 0.9f;
            p.HearingScale *= 0.85f;
        }
        if (Has(Assist.HonestCues)) p.HunterEyesLitAtRest = true;
        if (Has(Assist.BrighterHalls))
        {
            p.Ambient = ScaleColour(p.Ambient, 1.8f);
            p.FogDensity *= 0.8f;
            p.LampIntensity *= 1.25f;
        }

        if (Has(Oath.NoWaiting))
        {
            p.ChaseWalkFraction = Mathf.Max(p.ChaseWalkFraction, 1.1f);
            p.ChaseCommitScale *= 0.75f;
            p.HunterEyesLitAtRest = false;
        }
        if (Has(Oath.FailingGrid))
        {
            p.Ambient = ScaleColour(p.Ambient, 0.6f);
            p.LampIntensity *= 0.75f;
            p.CellsPerLamp = Mathf.CeilToInt(p.CellsPerLamp * 1.25f);
        }
        if (Has(Oath.ShortFuse)) time *= 0.75f;

        if (!Mathf.Approximately(time, 1f))
        {
            p.EscapeSeconds = Mathf.Max(30f, p.EscapeSeconds * time);
            p.GateEscapeSeconds = Mathf.Max(30f, p.GateEscapeSeconds * time);
        }
    }

    private static Color ScaleColour(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, c.a);

    /// <summary>
    /// The terror switches for this build. Returns <paramref name="t"/> ITSELF (same reference) when nothing applies; otherwise a clone with the
    /// assists / Oaths / Stethoscope applied. Never mutates the serialized instance.
    /// </summary>
    public static TerrorSettings ApplyTerror(TerrorSettings t)
    {
        if (t == null) return t;
        bool honest = Has(Assist.HonestCues);
        bool steady = Has(Assist.SteadyTorch);
        bool noReplay = Has(Assist.NoReplay);
        bool noMercy = Has(Oath.NoSecondChances);
        bool stethoscope = PlayerInventory.HasActive(ShopItem.Stethoscope);
        if (!honest && !steady && !noReplay && !noMercy && !stethoscope) return t;

        TerrorSettings c = t.Clone();
        if (honest)
        {
            c.contaminatedPhantoms = false;
            c.unpredictableTelegraphs = false;
            c.torchFailures = false;
            c.wrongSounds = false;
            c.varyingSpottedSting = false;
            c.rationedEyes = false;
            c.lyingKnocks = false; // F123: the floor-2 pipe knocks tell the truth
        }
        if (steady) c.torchSway = false;
        if (noReplay) c.deathReplay = false;
        if (noMercy) c.survivableGrab = false;
        if (stethoscope) c.muffledHunter = false;
        return c;
    }

    // ---------------------------------------------------------------- identity

    /// <summary>What decides whether the scene built at the title already matches the choice: only what changes how floor 1 is BUILT. A kit only counts when it changes the build (RUNNER's perks).</summary>
    public static string Signature() => ((int)Preset) + "|" + ((int)Assists) + "|" + ((int)Oaths) + "|" + (Kit == StartingKit.Runner ? 1 : 0);

    public static string PresetName(DifficultyPreset preset) => L10n.T("ui.preset." + OathCatalogue.PresetSlug(preset) + ".name");

    /// <summary>The localized tag for the ledger / end screen: "STANDARD", "NIGHTMARE · OATH 5", "STANDARD · OATH 3 · ASSISTED".</summary>
    public static string Label()
    {
        StringBuilder sb = new StringBuilder(PresetName(Preset));
        if (OathRank > 0) sb.Append(" · ").Append(L10n.T("ui.campaign.tag.oath", ("rank", OathRank)));
        if (IsAssisted && Preset != DifficultyPreset.Story) sb.Append(" · ").Append(L10n.T("ui.campaign.tag.assisted"));
        return sb.ToString();
    }

    // ---------------------------------------------------------------- the payout

    /// <summary>D9: adds the OATH line (the multiplier's share of the base payout) when <see cref="ShardMultiplier"/> exceeds 1. Call right after ComputeFloorClear. A no-op for plain STANDARD.</summary>
    public static void AppendOathLine(Payout payout)
    {
        float mult = ShardMultiplier;
        if (payout == null || mult <= 1f) return;
        int amount = Mathf.RoundToInt(payout.Total * (mult - 1f));
        payout.Lines.Add(new PayoutLine
        {
            Label = L10n.T("ui.ledger.oath", ("rank", OathRank), ("mult", mult.ToString("0.00", L10n.Culture))),
            Amount = amount,
            Kind = PayoutKind.Oath
        });
        payout.Total += amount;
    }

    // ---------------------------------------------------------------- lifecycle

    /// <summary>
    /// The new-campaign screen's BEGIN: sets the choice, grants the kit and the unlocked torch colours, records the campaign as started. Does NOT reload -
    /// the caller compares <see cref="Signature"/> with <see cref="BuiltSignature"/> to see whether floor 1 has to be rebuilt.
    /// </summary>
    public static void Begin(DifficultyPreset preset, Assist assists, Oath oaths, StartingKit kit, int torchColour)
    {
        Preset = preset;
        Assists = assists & OathCatalogue.AllAssists;
        Oaths = IsOathChoiceLegal(preset, Assists) ? oaths & OathCatalogue.AllOaths : Oath.None;
        Kit = kit;
        AssistedSticky = false;
        FloorGrade.ResetCampaign();
        ApplyRunSwitches();

        // Torch colours the Patient Record unlocked are never sold: they are simply owned from the start.
        for (int i = ShopCatalogue.ShopTorchSwatches + 1; i < ShopCatalogue.TorchColours.Length; i++)
        {
            if (PatientRecord.IsTorchColourUnlocked(i)) PlayerInventory.GrantTorchColour(i);
        }
        PlayerInventory.SelectTorchColour(torchColour);

        switch (Kit)
        {
            case StartingKit.Scavenger:
                PlayerInventory.Grant(ShopItem.SpareBattery);
                PlayerInventory.Grant(ShopItem.StarCompass);
                break;
            case StartingKit.Runner:
                PlayerInventory.ActiveModifiers.Add(ShopItem.StaminaTonic);
                PlayerInventory.ActiveModifiers.Add(ShopItem.QuietShoes);
                break;
            case StartingKit.Cartographer:
                PlayerInventory.Grant(ShopItem.FloorPlan);
                break;
        }

        PatientRecord.NoteCampaignStarted(preset, Assists, Oaths, kit, torchColour);
        PatientRecord.ResetShardBaseline(PlayerWallet.TotalEarned);
    }

    /// <summary>Oaths are for a STANDARD or NIGHTMARE campaign with no record-affecting assist on.</summary>
    public static bool IsOathChoiceLegal(DifficultyPreset preset, Assist assists) =>
        preset != DifficultyPreset.Story && (assists & OathCatalogue.RecordAffectingAssists) == 0;

    /// <summary>The pause menu's ASSISTS tab (D15): changes apply at the next maze build; a record-affecting one makes the campaign ASSISTED for good.</summary>
    public static void SetAssists(Assist assists)
    {
        assists &= OathCatalogue.AllAssists;
        if ((assists & OathCatalogue.RecordAffectingAssists & ~Assists) != 0) AssistedSticky = true;
        Assists = assists;
        ApplyRunSwitches();
    }

    /// <summary>Test / tool hook: sets the choice outright with no grants and no record.</summary>
    public static void SetForTest(DifficultyPreset preset, Assist assists, Oath oaths, StartingKit kit = StartingKit.StandardIssue, bool sticky = false)
    {
        Preset = preset;
        Assists = assists;
        Oaths = oaths;
        Kit = kit;
        AssistedSticky = sticky;
        ApplyRunSwitches();
    }

    public static void ResetCampaign()
    {
        Preset = DifficultyPreset.Standard;
        Assists = Assist.None;
        Oaths = Oath.None;
        Kit = StartingKit.StandardIssue;
        AssistedSticky = false;
        BuiltSignature = "";
        FloorGrade.ResetCampaign();
        ApplyRunSwitches();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => ResetCampaign();

    /// <summary>F83: the choice goes into every checkpoint. Zero is STANDARD, so an older save needs no migration.</summary>
    public static void CaptureTo(SaveData data)
    {
        data.campaignPreset = (int)Preset;
        data.campaignAssists = (int)Assists;
        data.campaignOaths = (int)Oaths;
        data.campaignKit = (int)Kit;
        data.campaignAssistedSticky = AssistedSticky;
        FloorGrade.CaptureTo(data);
    }

    public static void RestoreFrom(SaveData data)
    {
        ResetCampaign();
        Preset = System.Enum.IsDefined(typeof(DifficultyPreset), data.campaignPreset) ? (DifficultyPreset)data.campaignPreset : DifficultyPreset.Standard;
        Assists = (Assist)data.campaignAssists & OathCatalogue.AllAssists;
        Oaths = (Oath)data.campaignOaths & OathCatalogue.AllOaths;
        Kit = System.Enum.IsDefined(typeof(StartingKit), data.campaignKit) ? (StartingKit)data.campaignKit : StartingKit.StandardIssue;
        AssistedSticky = data.campaignAssistedSticky;
        FloorGrade.RestoreFrom(data);
        ApplyRunSwitches();
    }
}
