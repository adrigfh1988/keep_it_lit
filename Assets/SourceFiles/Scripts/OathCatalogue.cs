using System;

/// <summary>F122: the three campaign presets. Saved as an int: 0 MUST stay Standard (an old save reads 0). Append only.</summary>
public enum DifficultyPreset { Standard = 0, Story = 1, Nightmare = 2 }

/// <summary>F122: individual assists, each a multiplier on an existing knob. Saved as a bitmask: append only, never reorder.</summary>
[Flags]
public enum Assist
{
    None = 0,
    LongerTimer = 1,
    GentleTorch = 2,
    SlowHunter = 4,
    HonestCues = 8,
    SteadyTorch = 16,
    BrighterHalls = 32,
    NoReplay = 64
}

/// <summary>F122: stackable chosen Oaths (NG+). Each one is harder and pays a shard bonus. Saved as a bitmask: append only, never reorder.</summary>
[Flags]
public enum Oath
{
    None = 0,
    OldBattery = 1,
    NoWaiting = 2,
    WeldedLockers = 4,
    StanWakesEarly = 8,
    NoSecondChances = 16,
    FailingGrid = 32,
    ShortFuse = 64,
    HearsEverything = 128,
    DoorsDontHold = 256
}

/// <summary>F122: what the player starts a campaign holding. Saved as an int. Append only.</summary>
public enum StartingKit { StandardIssue = 0, Scavenger = 1, Runner = 2, Cartographer = 3 }

/// <summary>F122: the data about presets, assists, Oaths and kits (names are L10n keys <c>ui.&lt;kind&gt;.&lt;slug&gt;.name/.desc</c>). Plain static data, no behaviour.</summary>
public static class OathCatalogue
{
    public sealed class OathDef
    {
        public Oath Id;
        public string Slug;
        /// <summary>Rank points the Oath adds.</summary>
        public int Weight;
        /// <summary>Added to the shard multiplier (0.10 = +10 %).</summary>
        public float ShardBonus;
    }

    public sealed class AssistDef
    {
        public Assist Id;
        public string Slug;
        /// <summary>True: turning it on marks the campaign ASSISTED (no bests, no Oath records, no shard multiplier). False: comfort only (steady torch, no replay).</summary>
        public bool AffectsRecords;
    }

    /// <summary>F121 landed: the Oath THE DOORS DON'T HOLD drives <c>HubBreach.Mode</c> (EveryVisit).</summary>
    public static bool HubBreachSupported = true;

    public static readonly OathDef[] Oaths =
    {
        new OathDef { Id = Oath.OldBattery, Slug = "old_battery", Weight = 1, ShardBonus = 0.10f },
        new OathDef { Id = Oath.NoWaiting, Slug = "does_not_wait", Weight = 1, ShardBonus = 0.15f },
        new OathDef { Id = Oath.WeldedLockers, Slug = "welded_lockers", Weight = 2, ShardBonus = 0.20f },
        new OathDef { Id = Oath.StanWakesEarly, Slug = "stan_early", Weight = 2, ShardBonus = 0.20f },
        new OathDef { Id = Oath.NoSecondChances, Slug = "no_second_chances", Weight = 2, ShardBonus = 0.20f },
        new OathDef { Id = Oath.FailingGrid, Slug = "failing_grid", Weight = 1, ShardBonus = 0.10f },
        new OathDef { Id = Oath.ShortFuse, Slug = "short_fuse", Weight = 1, ShardBonus = 0.15f },
        new OathDef { Id = Oath.HearsEverything, Slug = "hears_everything", Weight = 1, ShardBonus = 0.10f },
        new OathDef { Id = Oath.DoorsDontHold, Slug = "doors_dont_hold", Weight = 1, ShardBonus = 0.10f },
    };

    public static readonly AssistDef[] Assists =
    {
        new AssistDef { Id = Assist.LongerTimer, Slug = "longer_timer", AffectsRecords = true },
        new AssistDef { Id = Assist.GentleTorch, Slug = "gentle_torch", AffectsRecords = true },
        new AssistDef { Id = Assist.SlowHunter, Slug = "slow_hunter", AffectsRecords = true },
        new AssistDef { Id = Assist.HonestCues, Slug = "honest_cues", AffectsRecords = true },
        new AssistDef { Id = Assist.BrighterHalls, Slug = "brighter_halls", AffectsRecords = true },
        new AssistDef { Id = Assist.SteadyTorch, Slug = "steady_torch", AffectsRecords = false },
        new AssistDef { Id = Assist.NoReplay, Slug = "no_replay", AffectsRecords = false },
    };

    /// <summary>The assists that make a campaign ASSISTED.</summary>
    public static readonly Assist RecordAffectingAssists = Assist.LongerTimer | Assist.GentleTorch | Assist.SlowHunter | Assist.HonestCues | Assist.BrighterHalls;

    /// <summary>All assist bits the game knows (a save with an unknown bit has it dropped).</summary>
    public static readonly Assist AllAssists = Assist.LongerTimer | Assist.GentleTorch | Assist.SlowHunter | Assist.HonestCues | Assist.SteadyTorch | Assist.BrighterHalls | Assist.NoReplay;

    /// <summary>All Oath bits the game knows.</summary>
    public static readonly Oath AllOaths = Oath.OldBattery | Oath.NoWaiting | Oath.WeldedLockers | Oath.StanWakesEarly | Oath.NoSecondChances | Oath.FailingGrid | Oath.ShortFuse | Oath.HearsEverything | Oath.DoorsDontHold;

    /// <summary>The assists a preset switches on (STORY = all assists; the others none).</summary>
    public static Assist PresetAssists(DifficultyPreset preset)
    {
        return preset == DifficultyPreset.Story ? AllAssists : Assist.None;
    }

    /// <summary>The Oaths a preset switches on (NIGHTMARE = all available Oaths; the others none).</summary>
    public static Oath PresetOaths(DifficultyPreset preset)
    {
        if (preset != DifficultyPreset.Nightmare) return Oath.None;
        Oath oaths = AllOaths;
        if (!IsAvailable(Oath.DoorsDontHold)) oaths &= ~Oath.DoorsDontHold;
        return oaths;
    }

    public static string PresetSlug(DifficultyPreset preset)
    {
        switch (preset)
        {
            case DifficultyPreset.Story: return "story";
            case DifficultyPreset.Nightmare: return "nightmare";
            default: return "standard";
        }
    }

    public static string KitSlug(StartingKit kit)
    {
        switch (kit)
        {
            case StartingKit.Scavenger: return "scavenger";
            case StartingKit.Runner: return "runner";
            case StartingKit.Cartographer: return "cartographer";
            default: return "standard_issue";
        }
    }

    public static OathDef Def(Oath oath)
    {
        foreach (OathDef d in Oaths) if (d.Id == oath) return d;
        return null;
    }

    public static AssistDef Def(Assist assist)
    {
        foreach (AssistDef d in Assists) if (d.Id == assist) return d;
        return null;
    }

    /// <summary>Whether an Oath can be offered at all (THE DOORS DON'T HOLD needs the F121 hub breach).</summary>
    public static bool IsAvailable(Oath oath) => oath != Oath.DoorsDontHold || HubBreachSupported;

    public static int RankOf(Oath oaths)
    {
        int rank = 0;
        foreach (OathDef d in Oaths) if ((oaths & d.Id) != 0) rank += d.Weight;
        return rank;
    }

    public static float ShardBonusOf(Oath oaths)
    {
        float bonus = 0f;
        foreach (OathDef d in Oaths) if ((oaths & d.Id) != 0) bonus += d.ShardBonus;
        return bonus;
    }
}
