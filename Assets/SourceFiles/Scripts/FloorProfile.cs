using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Every number that makes a floor harder, in one place. Difficulty here is not Easy/Medium/Hard, it
/// is how far into the game the player is: floor 1 is a gentle walk in a small, lit maze, and the
/// final floor keeps the pre-floors gameplay tuning for the hunter's speed, senses and timers. Its look
/// is a separate concern, assigned by <see cref="FloorThemes"/> below, and the ambush bias (F45) is new
/// behaviour on every floor, floor 5 included.
///
/// Each gameplay value is a pair, "first floor" and "final floor", blended by <see cref="Blend"/>. To make
/// the early game kinder or the late game crueller, change the pair - nothing else needs to know.
/// The footprint (grid, corridor pitch, wall height) is a per-floor table, not a blend (F40 slice A).
/// Plain data, no MonoBehaviour and no statics: MazeGenerator builds one and hands it out.
/// </summary>
public class FloorProfile
{
    public const int FinalFloor = 5;

    /// <summary>
    /// A floor's grid size and corridor scale - a "kind" of floor, not a point on a first/final slope
    /// (F40 slice A). Voids (L-shapes, rings) are a later slice; this is rectangles and scale only.
    /// </summary>
    private struct Footprint
    {
        public int Width, Height;
        public float CellSize, WallHeight;

        public Footprint(int width, int height, float cellSize, float wallHeight)
        {
            Width = width;
            Height = height;
            CellSize = cellSize;
            WallHeight = wallHeight;
        }
    }

    /// <summary>
    /// One row per floor (index 0 is floor 1). Not a (first, final) pair like the gameplay fields below -
    /// each floor is its own hand-picked shape. Cell counts: 49, 60, 81, 96, 121.
    /// </summary>
    private static readonly Footprint[] Footprints =
    {
        new Footprint(7, 7, 4.5f, 4.0f),   // floor 1 - as now
        new Footprint(10, 6, 4.5f, 4.0f),  // floor 2 - long east-west halls; the maze has a far side
        new Footprint(9, 9, 4.5f, 3.2f),   // floor 3 - low ceiling; the torch lights it and the sound is close
        new Footprint(12, 8, 5.5f, 6.0f),  // floor 4 - wide, tall halls; the beam is a small cone in a big dark space
        new Footprint(11, 11, 4.5f, 4.0f), // floor 5 - as now
    };

    /// <summary>
    /// Names the theme numbers <see cref="FloorThemes"/> assigns from - one row per "type of floor",
    /// matching a row built by KEEP IT LIT &gt; Build &gt; Floor Themes (<c>Assets/Editor/FloorThemeBuilder.cs</c>,
    /// its <c>rowNames</c> array). Add a new type here (and a matching builder/row) to grow the list;
    /// nothing else needs to change.
    /// </summary>
    public static class Theme
    {
        public const int Ward = 1;
        public const int BoilerDeck = 2;
        public const int Crypt = 3;
        public const int Lab = 4;
        public const int Hollow = 5;
    }

    /// <summary>
    /// Floor -&gt; theme number, one entry per floor (index 0 is floor 1). A floor's theme does not have
    /// to match its own number: reassign an entry to give that floor a different type's look - e.g.
    /// <c>FloorThemes[0] = Theme.Hollow</c> gives floor 1 the Hollow's aesthetic. Adding a floor to the
    /// game means adding an entry here (and bumping <see cref="FinalFloor"/> and the gallery row count).
    /// </summary>
    private static readonly int[] FloorThemes =
    {
        Theme.Ward,       // floor 1
        Theme.BoilerDeck, // floor 2
        Theme.Crypt,      // floor 3
        Theme.Lab,        // floor 4
        Theme.Crypt,      // floor 5 - borrowed: the Hollow's purple runes read murky, not scary, and swallowed the flashlight
    };

    /// <summary>Theme number for a 1-based floor, from <see cref="FloorThemes"/>. Falls back to the floor's own number if the list doesn't cover it.</summary>
    private static int ThemeFor(int floor)
    {
        int index = floor - 1;
        return index >= 0 && index < FloorThemes.Length ? FloorThemes[index] : floor;
    }

    // F72: doors, buttons and vaults are per-floor tables, like Footprints, not a first/final blend -
    // each floor's mix of "how many things gate progress" is hand-picked. Floor 1's one manual door
    // teaches the mechanic before anything is locked behind it.
    private static readonly int[] ManualDoorsTable = { 1, 2, 2, 3, 3 };
    private static readonly int[] SecurityDoorsTable = { 0, 2, 1, 3, 3 }; // F105: the final floor has as many security doors as floor 4
    private static readonly int[] VaultsTable = { 0, 1, 1, 1, 1 };

    /// <summary>
    /// F77: what a floor is actually about, beyond "collect the stars", as a flag set - every floor from
    /// FirstRolledFloor on rolls MechanicsPerFloor of these three, a pure function of (UsedSeed, floor)
    /// so a Retry keeps its pair (see RollObjectives). Stars are no longer a flag: they are always
    /// present, so None means "no extra mechanic" (floor 1, the teaching floor). Lockdown means the F72
    /// door/button table plus one Timed door. Blackout means the lights start off and FuseCount fuse
    /// boxes have to be found to bring them back. KeyHunt means KeyCount brass keys have to be found
    /// before the hatch/lift will complete (see MazeEscape's padlock).
    /// </summary>
    [System.Flags]
    public enum Objective { None = 0, Lockdown = 1, Blackout = 2, KeyHunt = 4 }

    /// <summary>F77 user decision 4 Oct 2026, "only 2 objectives per floor": how many of the three mechanics a rolled floor gets.</summary>
    public const int MechanicsPerFloor = 2;
    /// <summary>First floor that rolls mechanics; floor 1 stays stars-only as the teaching floor.</summary>
    public const int FirstRolledFloor = 2;

    /// <summary>
    /// F105 F5: the final floor always plays keys + a timed lockdown on top of its stars (and always ends in the lift,
    /// see <see cref="ForceLift"/>), so it is the hardest floor; it is not rolled. Floor 4's roll never lands on this pair.
    /// </summary>
    public const Objective FinalFloorObjectives = Objective.KeyHunt | Objective.Lockdown;

    private static int TableFor(int[] table, int floor, int fallback)
    {
        int index = floor - 1;
        return index >= 0 && index < table.Length ? table[index] : fallback;
    }

    /// <summary>
    /// F77: picks this floor's mechanics pair, a pure function of (usedSeed, floor) salted apart from
    /// RunRules' own roll ("KEYS" = 0x4B455953), so the two never correlate. Builds every MechanicsPerFloor-
    /// sized combination of the pool from a bitmask loop (not hard-coded), shuffles it with that seed, and
    /// picks the first entry that is not the pair the previous floor rolled (GameFlow.RolledObjectives),
    /// so two consecutive floors are never identical. Writes the result back to RolledObjectives[floor].
    /// </summary>
    /// <summary>Every MechanicsPerFloor-sized combination of the three mechanics (built from a bitmask loop, not hard-coded), shuffled by a System.Random seeded with `hash`.</summary>
    private static List<Objective> ShuffledMechanicPairs(int hash)
    {
        Objective[] singles = { Objective.Lockdown, Objective.Blackout, Objective.KeyHunt };
        List<Objective> pairs = new List<Objective>();
        int comboCount = 1 << singles.Length;
        for (int mask = 1; mask < comboCount; mask++)
        {
            int popcount = 0;
            Objective combo = Objective.None;
            for (int bit = 0; bit < singles.Length; bit++)
            {
                if ((mask & (1 << bit)) == 0) continue;
                popcount++;
                combo |= singles[bit];
            }
            if (popcount == MechanicsPerFloor) pairs.Add(combo);
        }

        System.Random rng = new System.Random(hash);
        for (int i = pairs.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (pairs[i], pairs[j]) = (pairs[j], pairs[i]);
        }

        return pairs;
    }

    private static Objective RollObjectives(int floor, int usedSeed)
    {
        if (floor < FirstRolledFloor) return Objective.None;

        // F105 F5: the final floor is fixed, not rolled; keep RolledObjectives consistent for the next lookup.
        if (floor == FinalFloor)
        {
            if (floor < GameFlow.RolledObjectives.Length) GameFlow.RolledObjectives[floor] = (int)FinalFloorObjectives;
            return FinalFloorObjectives;
        }

        List<Objective> pairs = ShuffledMechanicPairs(RunRules.HashSeedFloor(usedSeed ^ unchecked((int)0x4B455953), floor));

        int[] rolled = GameFlow.RolledObjectives;
        int previous = floor - 1 >= 1 && floor - 1 < rolled.Length ? rolled[floor - 1] : 0;

        // F105 F5: the floor before the final one never rolls the final floor's own pair, so the two never play the same.
        bool beforeFinal = floor == FinalFloor - 1;

        Objective chosen = pairs[0];
        for (int i = 0; i < pairs.Count; i++)
        {
            if ((int)pairs[i] == previous) continue;
            if (beforeFinal && pairs[i] == FinalFloorObjectives) continue;
            chosen = pairs[i];
            break;
        }

        if (floor < rolled.Length) rolled[floor] = (int)chosen;
        return chosen;
    }

    public int Floor { get; private set; }

    /// <summary>0 on the first floor, 1 on the final one.</summary>
    public float Progress { get; private set; }

    /// <summary>Which FloorThemeSet row the maze clones. Set from <see cref="FloorThemes"/> in For() - the themes' looks are authored in the scene; this is only the floor-to-theme mapping.</summary>
    public int ThemeIndex;

    // ---- world
    public int Width, Height;
    public float CellSize;
    public float WallHeight;
    public int StarCount;
    public int LockerCount;
    public int CellsPerLamp;
    public float LampIntensity;
    public float LampRange;
    /// <summary>Glowing shards hidden in the maze's nooks (F31). Never set by For(); Quiet Shoes touches NoiseScale, not this.</summary>
    public int ShardCount;

    // ---- light
    public Color Ambient;
    public float FogDensity;

    // ---- the player
    public float SprintSeconds;
    public float EscapeSeconds;

    // ---- the hunter
    public float DormantSpeed;
    public float WalkSpeed;
    public float RunSpeed;
    public float ViewDistance;
    public float ViewAngle;
    public float HearingScale;
    public float LoseSightTime;
    public float PatrolBias;
    public float SearchBudget;
    /// <summary>F104 B2: the hunter's committed-chase speed as a fraction of the player's walk speed, before the hatch.</summary>
    public float ChaseWalkFraction;

    // ---- dread
    /// <summary>Seconds between relocations (F23). Also floored at a hard 20 s minimum in DreadDirector.</summary>
    /// <summary>How close a relocation may appear along the player's trail at full progress (m).</summary>
    public float RelocateNearDistance;
    /// <summary>Mean seconds between phantoms (F26).</summary>
    public float PhantomInterval;

    /// <summary>Footstep noise multiplier. Never set here - only PlayerInventory.ApplyActiveModifiers (Quiet Shoes) touches it.</summary>
    public float NoiseScale = 1f;

    // ---- ambush (F45)
    // ---- doors, buttons and vaults (F72)
    public int ManualDoors;
    public int SecurityDoors;
    public int Vaults;
    public bool HasTimedDoor;

    // ---- objective (F73, flags since F77)
    public Objective Objectives;
    /// <summary>True when `o` is one of this floor's rolled (or forced) mechanics.</summary>
    public bool Has(Objective o) => (Objectives & o) != 0;
    /// <summary>Blackout only; 0 on every other floor.</summary>
    public int FuseCount;
    /// <summary>KeyHunt only; 0 on every other floor.</summary>
    public int KeyCount;

    // ---- the escape (F105 F5)
    /// <summary>F105 F5: the final floor always ends in the lift, never a hatch (MazeEscape still rolls +17 for the other floors).</summary>
    public bool ForceLift;
    /// <summary>F105 F5: seconds standing in the lift ring to complete the escape - 8 everywhere, 12 on the final floor.</summary>
    public float LiftFillSeconds = 8f;

    /// <summary>Chance a wander leg becomes an ambush. Read raw - see AIFollower.EffectiveAmbushBias.</summary>
    public float AmbushBias;
    /// <summary>Maximum time lying in wait.</summary>
    public float AmbushWaitSeconds;
    /// <summary>Hearing multiplier while lying in wait.</summary>
    public float AmbushHearingBoost;

    // ---- hunter temperament (F74, B3.2)
    /// <summary>One line for the floor intro banner, e.g. "It hunts by sound here." Empty on a floor with no temperament twist.</summary>
    public string TemperamentKey = "";

    /// <summary>S9: the translated line (empty when the floor has no temperament twist). The key is a Shop-table key.</summary>
    public string TemperamentText => string.IsNullOrEmpty(TemperamentKey) ? "" : L10n.T(TemperamentKey);


    // ---- rationed eyes (F103 A3)
    /// <summary>True on floor 1 only: the hunter's eyes stay lit while it patrols, so the teaching floor still shows what they mean. Later floors ration them (AIPresence).</summary>
    public bool HunterEyesLitAtRest;

    // ---- bait dead ends (F103 A10)
    /// <summary>Fraction of the maze's dead ends that survive braiding (0 on floor 1). Only short pockets stay - see MazeGenerator.maxDeadEndDepth.</summary>
    public float DeadEndKeep;
    /// <summary>How many stars prefer a kept dead end (0 on floor 1).</summary>
    public int DeadEndBaitStars;

    /// <summary>
    /// Base profile, no mechanics rolled (Objectives = None). F77 decision 3: only PlayerWallet.
    /// ComputeConsolation should use this overload - the consolation cap is deliberately on the base
    /// star count, not a KeyHunt/Blackout-trimmed one (slightly generous on a trimmed floor; harmless).
    /// Everything else should go through the seeded overload below.
    /// </summary>
    public static FloorProfile For(int floor) => For(floor, 0, Objective.None);

    /// <summary>
    /// F77: usedSeed makes the mechanics roll a pure function of (seed, floor), so a Retry keeps its
    /// floor's pair. `forced` overrides the roll entirely (MazeGenerator's debugObjectives) - pass null to
    /// roll normally, or Objective.None explicitly to roll nothing (see the seedless For() above).
    /// </summary>
    public static FloorProfile For(int floor, int usedSeed, Objective? forced = null) => Build(floor, usedSeed, forced, true);

    /// <summary>The story profile for a floor. `storyTemperament` off (F107 DESCENT) leaves the hunter's blended values unmultiplied and the banner line empty - ForDescent picks its own.</summary>
    private static FloorProfile Build(int floor, int usedSeed, Objective? forced, bool storyTemperament)
    {
        floor = Mathf.Clamp(floor, 1, FinalFloor);
        float t = FinalFloor > 1 ? (floor - 1) / (float)(FinalFloor - 1) : 1f;

        FloorProfile p = new FloorProfile { Floor = floor, Progress = t, ThemeIndex = ThemeFor(floor) };

        // Pairs are (first floor, final floor). The final-floor values are the shipped tuning, except the
        // stare/ambush block below, which is new on every floor.
        // Footprint is a per-floor table (decision 1), not a blend - but keep the old blend as a
        // fallback so a floor missing from the table (impossible with FinalFloor = 5) still builds.
        int footprintIndex = floor - 1;
        if (footprintIndex >= 0 && footprintIndex < Footprints.Length)
        {
            Footprint footprint = Footprints[footprintIndex];
            p.Width = footprint.Width;
            p.Height = footprint.Height;
            p.CellSize = footprint.CellSize;
            p.WallHeight = footprint.WallHeight;
        }
        else
        {
            p.Width = Blend(7, 11, t);
            p.Height = p.Width;
            p.CellSize = 4.5f;
            p.WallHeight = 4.0f;
        }
        p.StarCount = Blend(2, 5, t);
        p.LockerCount = Blend(5, 10, t);

        // F77: every floor from FirstRolledFloor on rolls MechanicsPerFloor of {Lockdown, Blackout,
        // KeyHunt} from the seed (RollObjectives), or `forced` overrides it (MazeGenerator debug field).
        // Blackout and KeyHunt each trade some of the floor's stars for their own objective - the
        // lights/keys themselves are the point, so the full star count on top would run long past D1's
        // per-floor time budget. One Max(2, ...) after both trims, not one per branch.
        p.Objectives = forced ?? RollObjectives(floor, usedSeed);
        if (p.Has(Objective.Blackout))
        {
            p.FuseCount = 3;
            p.StarCount -= 2;
        }
        if (p.Has(Objective.KeyHunt))
        {
            p.KeyCount = floor <= 3 ? 2 : 3;
            p.StarCount -= 1;
        }
        // F105 F5: the final floor therefore plays 5 - 1 (KeyHunt) = 4 stars, 3 keys, and Lockdown's timed door.
        p.StarCount = Mathf.Max(2, p.StarCount);
        // F71 darkness pass: lamps are sparser, dimmer and shorter-range than the old tuning, so floor 1
        // has real pockets of dark between lamp pools (the torch is optional there) and floor 5 is
        // unreadable without it. Ambient/fog follow the same curve. See
        // plannings/darkness-and-interactivity-spec.md, decision D2 and slice F71.
        p.CellsPerLamp = Blend(5, 8, t);
        p.LampIntensity = Blend(1.0f, 0.7f, t);
        p.LampRange = Blend(5.0f, 4.0f, t);

        float ambient = Blend(0.015f, 0.006f, t);
        p.Ambient = new Color(ambient, ambient, ambient * 1.25f);
        p.FogDensity = Blend(0.07f, 0.12f, t);

        // 3x slower drain than the original 14 -> 8 s tuning (bar-flavor unchanged, just lasts longer).
        p.SprintSeconds = Blend(42f, 24f, t);
        p.EscapeSeconds = Blend(120f, 60f, t);

        // The hunter's walking pace on floor 1 is below the player's own walk (2.0 m/s), and it does
        // not really run even once the hatch opens: 1.8 m/s "running" is a brisk walk.
        // 10 Oct 2026: the later floors were slowed (walk 3.5 -> 2.4, run 4.5 -> 3.4 at floor 5) so the
        // hunter moves near the speed its recorded clips cover (zombie run 3.0 m/s) instead of gliding;
        // the player still has to sprint (5.3 m/s) to get away, never just walk (2.0 m/s).
        p.DormantSpeed = Blend(0.4f, 0.6f, t); // the hunter never moves this slowly: AIFollower starts its speed at one star's progress
        p.WalkSpeed = Blend(1.4f, 2.4f, t);
        p.RunSpeed = Blend(1.8f, 3.4f, t);
        p.ViewDistance = Blend(9f, 18f, t);
        p.ViewAngle = Blend(80f, 110f, t);
        p.HearingScale = Blend(0.4f, 1f, t);
        p.LoseSightTime = Blend(1.2f, 3f, t);
        p.PatrolBias = Blend(0.2f, 0.6f, t);
        p.SearchBudget = Blend(9f, 18f, t);
        p.ChaseWalkFraction = floor <= 1 ? 0.9f : 1.1f;

        p.RelocateNearDistance = Blend(11f, 7f, t);
        p.PhantomInterval = Blend(45f, 18f, t);

        p.ShardCount = Blend(4, 8, t);

        p.ManualDoors = TableFor(ManualDoorsTable, floor, 1);
        p.SecurityDoors = TableFor(SecurityDoorsTable, floor, 0);
        p.Vaults = TableFor(VaultsTable, floor, 0);
        // F73/F77: folded into Objectives - Lockdown means the door/button table above plus one Timed door.
        p.HasTimedDoor = p.Has(Objective.Lockdown);

        p.AmbushBias = Blend(0.35f, 0.75f, t);
        p.AmbushWaitSeconds = Blend(40f, 90f, t);
        p.AmbushHearingBoost = Blend(1.5f, 1.5f, t);

        // F74 hunter temperament (B3.2): per-floor multipliers layered on top of the blend above, plus
        // the banner's one-line flavour text. Every other floor is untouched (implicit x1, no line).
        if (storyTemperament) ApplyTemperament(p, floor == 3 ? Temperament.Listens : floor == 4 ? Temperament.Sprints : floor == FinalFloor ? Temperament.Final : Temperament.None);

        // F105 F5: the final floor always ends in the lift, and the lift takes longer to fill.
        p.ForceLift = floor == FinalFloor;
        p.LiftFillSeconds = p.ForceLift ? 12f : 8f;

        // F75, decision D7: the Stalker appears on the last two floors only - by then the hunter alone
        // has had time to establish what "the one thing chasing you" feels like.
        // F107 D1: which entities a floor gets is EntityRoster's call now (it was p.HasStalker = floor >= 4).

        // F103 A3: floor 1 is the teaching floor - the eyes stay lit there.
        p.HunterEyesLitAtRest = floor == 1;

        // F103 A10: floor 1 is the teaching floor and stays fully braided; from floor 2 some dead ends survive, and a star or two waits in one.
        p.DeadEndKeep = floor <= 1 ? 0f : Blend(0.15f, 0.35f, t);
        p.DeadEndBaitStars = floor <= 1 ? 0 : floor <= 3 ? 1 : 2;

        return p;
    }

    // ---------------------------------------------------------------- hunter temperament

    private enum Temperament { None, Listens, Sprints, Final }

    /// <summary>F74 temperament multipliers on top of the first/final blend, plus the banner's one-line flavour text (shared by the story floors and F107's levels).</summary>
    private static void ApplyTemperament(FloorProfile p, Temperament temperament)
    {
        switch (temperament)
        {
            case Temperament.Listens:
                p.ViewDistance *= 0.6f;
                p.HearingScale *= 1.5f;
                p.TemperamentKey = "shop.temperament.listens";
                break;
            case Temperament.Sprints:
                p.RunSpeed *= 1.15f;
                p.LoseSightTime *= 0.6f;
                p.TemperamentKey = "shop.temperament.sprints";
                break;
            case Temperament.Final:
                // F105 F5: "it will not let you leave" - a final-floor hunter hunts, it doesn't wait.
                // RunSpeed 3.4 x 1.2 = 4.08, still under the chase sprint cap.
                p.RunSpeed *= 1.2f;
                p.HearingScale *= 1.25f;
                p.LoseSightTime *= 1.3f;
                p.AmbushBias = Mathf.Max(0f, p.AmbushBias - 0.15f);
                p.TemperamentKey = "shop.temperament.final";
                break;
            default:
                p.TemperamentKey = "";
                break;
        }
    }

    // ---------------------------------------------------------------- F107 DESCENT

    /// <summary>F107 B3: the themes a descent cycles through, in depth order. The Hollow row never plays (its index would be 5; FloorThemeSet.ForFloor must never be handed it).</summary>
    private static readonly int[] DescentThemeCycle = { Theme.Ward, Theme.BoilerDeck, Theme.Crypt, Theme.Lab };

    /// <summary>F107 B3: stars + keys + fuses the player has to find on this level. The maze grid is sized from it.</summary>
    public int RequiredItems => StarCount + KeyCount + FuseCount;

    private const float DescentRunSpeedCap = 4.3f;   // the player sprints at 5.3 m/s; the hunter must stay beatable
    private const float DescentMinAmbient = 0.004f;
    private const int DescentCellsPerLampCap = 11;
    private const float DescentFogCap = 0.16f;
    private const float DescentPhantomFloor = 12f;
    private const float DescentChaseWalkCap = 1.15f;
    private const int DescentBossEvery = 5;
    private const int DescentSalt = 0x44455343; // "DESC"

    /// <summary>
    /// F107 B3: the profile of one DESCENT level. Starts from the story profile of floor min(depth, FinalFloor) (so everything keyed to a story
    /// floor number, such as the footprint tables and the Terror tiers, behaves as on that floor) and overrides what a descent decides itself:
    /// the item counts, the grid (sized from the items), the cycling theme, the shard count (none: there is no shop) and, past depth 5, a set of
    /// capped difficulty curves. `forced` (MazeGenerator.debugObjectives) overrides the objective roll exactly as in the story.
    /// </summary>
    public static FloorProfile ForDescent(int depth, int usedSeed, Objective? forced = null)
    {
        depth = Mathf.Max(1, depth);
        Objective objectives = forced ?? RollDescentObjectives(depth, usedSeed);
        FloorProfile p = Build(Mathf.Min(depth, FinalFloor), usedSeed, objectives, false);

        // What has to be found. The story's "Blackout costs 2 stars / KeyHunt costs 1" trims do not apply.
        p.StarCount = Mathf.Min(3 + depth / 3, 8);
        p.KeyCount = p.Has(Objective.KeyHunt) ? Mathf.Min(2 + depth / 6, 4) : 0;
        p.FuseCount = p.Has(Objective.Blackout) ? Mathf.Min(3 + depth / 10, 4) : 0;

        // Look and footprint: the theme cycles, and the corridor scale comes from the story floor that wears that theme.
        int cycle = (depth - 1) % DescentThemeCycle.Length;
        p.ThemeIndex = DescentThemeCycle[cycle];
        Footprint footprint = Footprints[DescentFootprintIndex(cycle, depth)];
        p.CellSize = footprint.CellSize;
        p.WallHeight = footprint.WallHeight;
        DescentGrid(p.RequiredItems, usedSeed, out p.Width, out p.Height);

        int cells = p.Width * p.Height;
        int lockers = Mathf.Max(5, Mathf.RoundToInt(cells * 10f / 121f)); // the story's floor-5 lockers per cell
        if (depth >= 15) lockers = Mathf.Max(5, lockers - (depth - 10) / 5);
        p.LockerCount = lockers;

        p.ShardCount = 0;

        // Every fifth level is a boss level: the final floor's temperament and its lift.
        bool boss = depth % DescentBossEvery == 0;
        p.ForceLift = boss;
        p.LiftFillSeconds = boss ? 12f : 8f;
        // Depths 1-5 keep the story's temperaments (a gentle start); deeper levels pick one from the seed.
        Temperament temperament = boss ? Temperament.Final
            : depth <= FinalFloor ? (depth == 3 ? Temperament.Listens : depth == 4 ? Temperament.Sprints : Temperament.None)
            : DescentTemperament(depth, usedSeed);
        ApplyTemperament(p, temperament);

        ApplyDescentCurves(p, depth, cells);
        return p;
    }

    /// <summary>The Footprints row a level of this theme-cycle slot uses: Ward floor 1, Boiler floor 2, Crypt floor 3 or 5 (alternating each time the Crypt comes round), Lab floor 4.</summary>
    private static int DescentFootprintIndex(int cycle, int depth)
    {
        switch (DescentThemeCycle[cycle])
        {
            case Theme.Ward: return 0;
            case Theme.BoilerDeck: return 1;
            case Theme.Crypt: return ((depth - 3) / DescentThemeCycle.Length) % 2 == 0 ? 2 : 4;
            default: return 3;
        }
    }

    private static Temperament DescentTemperament(int depth, int usedSeed)
    {
        int pick = ((RunRules.HashSeedFloor(usedSeed ^ unchecked((int)0x54454D50), depth) % 3) + 3) % 3; // "TEMP"
        return pick == 1 ? Temperament.Listens : pick == 2 ? Temperament.Sprints : Temperament.None;
    }

    /// <summary>
    /// F107 B3: the grid for `required` items, the user's rule: cells = clamp(18 + 7 * required, 42, 196), a seeded aspect of 1.0-1.35, then each
    /// side clamped to 6..16 by 6..13. Depth 1 (3 items) gives 7x6; depth 12 (15 items) 12x10.
    /// </summary>
    public static void DescentGrid(int required, int seed, out int width, out int height)
    {
        int cells = Mathf.Clamp(18 + 7 * required, 42, 196);
        int hash = RunRules.HashSeedFloor(seed ^ unchecked((int)0x41535043), 0); // "ASPC"
        float aspect = 1f + 0.35f * ((hash & 0x7FFFFFFF) / (float)int.MaxValue);
        width = Mathf.Clamp(Mathf.RoundToInt(Mathf.Sqrt(cells * aspect)), 6, 16);
        height = Mathf.Clamp(Mathf.RoundToInt(cells / (float)width), 6, 13);
    }

    /// <summary>
    /// Depth 1 stars only; 2-9 two mechanics rolled like the story but never the pair DescentRun.PreviousObjectives holds; 10+ all three.
    /// Writes the result back to DescentRun.PreviousObjectives (a forced value from the Inspector never reaches here).
    /// </summary>
    private static Objective RollDescentObjectives(int depth, int usedSeed)
    {
        Objective chosen;
        if (depth <= 1) chosen = Objective.None;
        else if (depth >= 10) chosen = Objective.Lockdown | Objective.Blackout | Objective.KeyHunt;
        else
        {
            List<Objective> pairs = ShuffledMechanicPairs(RunRules.HashSeedFloor(usedSeed ^ DescentSalt, depth));
            chosen = pairs[0];
            for (int i = 0; i < pairs.Count; i++)
            {
                if ((int)pairs[i] == DescentRun.PreviousObjectives) continue;
                chosen = pairs[i];
                break;
            }
        }

        DescentRun.PreviousObjectives = (int)chosen;
        return chosen;
    }

    /// <summary>
    /// Past depth 5 every term follows c = 1 - exp(-(depth - 5) / 8): 0 at depth 5, 0.47 at 10, 0.85 at 20. A saturating curve, never a Lerp
    /// extrapolated past 1, and each result is capped.
    /// </summary>
    private static void ApplyDescentCurves(FloorProfile p, int depth, int cells)
    {
        int past = Mathf.Max(0, depth - FinalFloor);
        float c = 1f - Mathf.Exp(-past / 8f);

        p.RunSpeed = Mathf.Min(DescentRunSpeedCap, p.RunSpeed * (1f + 0.25f * c));
        p.HearingScale *= 1f + 0.5f * c;
        p.ViewDistance *= 1f + 0.25f * c;
        p.ChaseWalkFraction = Mathf.Min(DescentChaseWalkCap, p.ChaseWalkFraction + 0.05f * c);
        p.CellsPerLamp = Mathf.Min(DescentCellsPerLampCap, Mathf.RoundToInt(p.CellsPerLamp + 4f * c));
        p.FogDensity = Mathf.Min(DescentFogCap, p.FogDensity + 0.04f * c);

        float scale = 1f - 0.4f * c;
        Color a = p.Ambient;
        p.Ambient = new Color(Mathf.Max(a.r * scale, DescentMinAmbient), Mathf.Max(a.g * scale, DescentMinAmbient), Mathf.Max(a.b * scale, DescentMinAmbient * 1.25f), 1f);

        if (past > 0)
        {
            float escape = Mathf.Clamp(25f + 0.3f * cells, 45f, 90f) - Mathf.Max(0, depth - 10);
            p.EscapeSeconds = Mathf.Max(40f, escape);
            p.PhantomInterval = Mathf.Max(DescentPhantomFloor, p.PhantomInterval - past);
            p.DeadEndKeep = Mathf.Lerp(p.DeadEndKeep, 0.5f, Mathf.Clamp01(past / 15f));
        }
    }

    public override string ToString()
    {
        return $"floor {Floor}/{FinalFloor}: {Width}x{Height}, cell {CellSize:0.0} wall {WallHeight:0.0}, {StarCount} stars, {LockerCount} lockers, " +
               $"hunter walk {WalkSpeed:0.0} run {RunSpeed:0.0} commits {ChaseWalkFraction:0.0#}x walk, sees {ViewDistance:0}m, escape {EscapeSeconds:0}s, {ShardCount} shards, " +
               $"ambush {AmbushBias:0.00}, objectives {Objectives}, dead ends kept {DeadEndKeep:0.00} (bait stars {DeadEndBaitStars})";
    }

    private static float Blend(float first, float final, float t) => Mathf.Lerp(first, final, t);

    private static int Blend(int first, int final, float t) => Mathf.RoundToInt(Mathf.Lerp(first, final, t));
}
