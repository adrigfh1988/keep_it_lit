using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F85 pack dressing: the unused corners of the art packs as maze dressing (plannings/pack-dressing-plan.md).
/// Kept in its own partial file like FloorThemeBuilder.Alchemist.cs. Two halves:
///   - FurnitureDefs: the Alchemist House's big furniture as standing/hung wall props and a floor cluster. They are
///     AlchDefs, so they ride the F79 build/load/cache path and the AlchemistSet flags in ThemeSets.
///   - PackDef (DeadBody LITE + Socket Pack): composed set pieces (beds, tubs), a standing shroud wall prop, hanging
///     bodies as ceiling props and the Socket Pack's vents / sockets / fuse boxes as hung wall props. Prefabs land in
///     Themes/Common/Prefabs/PackDressing; look-alike models are children behind a ModelVariants (picked by a hash of
///     the cell at runtime, never an rng draw).
/// The full-rebuild builders include these natively (BuildPackProps / BuildPackSetPieces);
/// PackDressing.Apply appends them to an existing scene's gallery rows without rebuilding them.
/// Tier 129 adds the locally generated maze scare props (Gen_*, plannings/tier129-maze-scare-assets-plan.md): their
/// models live in Assets/SourceFiles/Models/MazeProps/&lt;Asset&gt;/ (imported by MazePropImport) and each def requires
/// its own asset folder + model prefab, so a prop that has not been generated yet is simply not expected.
/// </summary>
public static partial class FloorThemeBuilder
{
    internal const string PackFolder = CommonRoot + "/Prefabs/PackDressing";
    private const string DeadBodyRoot = "Assets/DeadBody LITE/Prefabs/";
    private const string SocketRoot = "Assets/Socket Pack/Prefabs/";
    /// <summary>Tier 129: one folder per generated asset (model, maps, URP material, model-only prefab); decals in Scrawl/.</summary>
    internal const string MazePropsRoot = "Assets/SourceFiles/Models/MazeProps";
    internal const string ScrawlFolder = MazePropsRoot + "/Scrawl";

    /// <summary>PropPiece's wall-prop depth rule (keep &lt;= 0.7 m, like a locker) and the gap generated models keep off the wall plane.</summary>
    private const float WallPropMaxDepth = 0.7f, GenWallGap = 0.03f;
    /// <summary>PropPiece's Floor rule: low clutter, no collider.</summary>
    private const float FloorPropMaxHeight = 0.35f, FloorPropMaxDepth = 0.45f;
    /// <summary>The most a generated model may be shrunk to meet a depth rule (the F79 furniture's 15 % rule); past it the variant is left out.</summary>
    private const float GenMinFit = 0.85f;

    // Theme bits for PackDef.Themes: one per gallery row (Ward, Boiler Deck, Crypt, Lab, Hollow).
    internal const int PackWard = 1, PackBoiler = 2, PackCrypt = 4, PackLab = 8, PackHollow = 16;

    internal static int PackBitForRow(int rowIndex) => 1 << rowIndex;

    /// <summary>The ceiling height the hanging bodies are built for (floor 3, the lowest); HangingBodyFit re-cuts the rope at spawn for floor 5 (floors 3 and 5 share the Crypt row, 3.2 m and 4.0 m).</summary>
    private const float CryptCeiling = 3.2f;
    /// <summary>Hanging feet this far off the floor: head height, so the player walks into a body, not under it.</summary>
    private const float HangFeetClearance = 0.4f;

    /// <summary>Set = SetPiece; the rest are PropPiece mount kinds (Floor and WallDecal since tier 129).</summary>
    private enum PackKind { Set, Wall, Ceiling, Floor, WallDecal }

    private struct PackBuild
    {
        public GameObject Root;
        public float Width, Depth;
    }

    private sealed class PackDef
    {
        public string Name;
        public int Themes;
        public PackKind Kind;
        public float Weight = 1f;
        /// <summary>PropPiece/SetPiece.enabledInMaze. False = built and shown in the gallery row, never spawned (Pack_StandingShroud, 7 Oct 2026: reads as the hunter in the dark; kept for a future idea).</summary>
        public bool Enabled = true;
        public bool AllowLowHang;
        public string RequiredFolder;
        /// <summary>Tier 129, optional: an asset (path with extension) that must also exist - a generated prop's model prefab or a decal's texture - so a half-imported folder is not expected yet.</summary>
        public string RequiredAsset;
        /// <summary>WallDecal only: PropPiece.decalSizeRange (the root's uniform scale range, the quad's longest side).</summary>
        public Vector2 DecalSize = new Vector2(0.6f, 1.2f);
        /// <summary>WallDecal only: PropPiece.randomRoll. Off keeps the decal upright (an arrow, a number, a drawing).</summary>
        public bool RandomRoll = true;
        public Func<PackBuild> Compose;
    }

    /// <summary>Tier 129: one generated model variant - the model-only prefab path (no extension) and an extra yaw applied before fitting.</summary>
    private struct GenModel
    {
        public string Path;
        public float Yaw;
    }

    private static Dictionary<string, GameObject> _packPrefabs;
    private static bool _overwritePack;

    // ---------------------------------------------------------------- Alchemist furniture

    private static List<AlchDef> FurnitureDefs()
    {
        const string F = "Furniture/", Bt = "Items/Bottles/", O = "Items/Organic/";
        const PropPiece.MountKind Wall = PropPiece.MountKind.Wall;

        AlchDef Stand(string name, AlchemistSet set, string path, float weight, bool flip = false)
        {
            return new AlchDef { Name = name, Set = set, Mount = Wall, Depth = 0.7f, Weight = weight, RootBox = true, Compose = () => ComposeFurniture(name, 0.68f, path, flip) };
        }

        return new List<AlchDef>
        {
            Stand("Alch_Cupboard1", AlchemistSet.Cabinets, F + "WoodCupboard01", 0.7f),
            Stand("Alch_Cupboard2", AlchemistSet.Cabinets, F + "WoodCupboard02", 0.7f),
            Stand("Alch_Cupboard3", AlchemistSet.Cabinets, F + "WoodCupboard03", 0.7f, true),
            Stand("Alch_Drawer1", AlchemistSet.Cabinets, F + "Drawer01", 0.6f),
            Stand("Alch_Drawer2", AlchemistSet.Cabinets, F + "Drawer02", 0.6f),
            Stand("Alch_Drawer3", AlchemistSet.Cabinets, F + "Drawer03", 0.6f),
            Stand("Alch_WoodDrawer", AlchemistSet.Cabinets, F + "WoodDrawer01", 0.6f, true),
            Stand("Alch_LongTable", AlchemistSet.Tables, F + "Table01", 0.6f),
            Stand("Alch_Workbench", AlchemistSet.Workshop, F + "Workbench", 0.7f),
            Stand("Alch_Sink", AlchemistSet.Sink, F + "TableSink", 0.6f),
            Stand("Alch_Stove", AlchemistSet.Stove, F + "Stove", 0.6f),
            // Alembic02 (0.81 m deep, 18 % shrink to fit) is left out: past the 15 % rule.
            new AlchDef
            {
                Name = "Alch_WoodShelf", Set = AlchemistSet.Shelves, Mount = Wall, Depth = 0.36f, Weight = 0.7f,
                Compose = () =>
                {
                    GameObject root = new GameObject("Alch_WoodShelf");
                    GameObject shelf = HangFlat(root, F + "WoodShelf01", 0f, 1.35f, 0f);
                    if (shelf != null)
                    {
                        float top = AlchemistPack.RenderBounds(shelf).max.y;
                        AlchemistPack.Place(root.transform, Bt + "Bottle02", new Vector3(-0.55f, top, 0.17f), Quaternion.identity);
                        AlchemistPack.Place(root.transform, Bt + "Bottle04", new Vector3(0.05f, top, 0.15f), Quaternion.Euler(0f, 40f, 0f));
                        AlchemistPack.Place(root.transform, Bt + "Flask01", new Vector3(0.5f, top, 0.17f), Quaternion.identity);
                    }
                    return root;
                }
            },
            new AlchDef
            {
                Name = "Alch_Babies", Set = AlchemistSet.Nursery, Mount = PropPiece.MountKind.Floor, Depth = 0.4f, Weight = 0.6f,
                Compose = () => Compose("Alch_Babies", 0.4f, 0.35f,
                    new[]
                    {
                        P(O + "Baby01", -0.42f, 0.2f, 25f, 90f), P(O + "Baby02", 0f, 0.26f, -40f, 90f), P(O + "Baby03", 0.42f, 0.2f, 70f, 90f)
                    })
            },
        };
    }

    /// <summary>
    /// One piece of big furniture, turned so its shallower floor side runs into the corridor, bottom-centre on the
    /// origin. Unlike Compose it may shrink the piece by at most 15 % to fit the depth budget; anything deeper is
    /// left out (null, logged) rather than squashed.
    /// </summary>
    private static GameObject ComposeFurniture(string name, float depth, string path, bool flipFront)
    {
        GameObject root = new GameObject(name);
        GameObject go = AlchemistPack.Spawn(root.transform, path);
        if (go == null) { UnityEngine.Object.DestroyImmediate(root); return null; }

        Vector3 s = AlchemistPack.RenderBounds(go).size;
        float turn = s.x < s.z ? 90f : 0f;
        if (flipFront) turn += 180f;
        go.transform.rotation = Quaternion.Euler(0f, turn, 0f) * go.transform.rotation;

        Bounds b = AlchemistPack.RenderBounds(go);
        go.transform.position += -new Vector3(b.center.x, b.min.y, b.center.z);

        float fit = (depth - 0.02f) / Mathf.Max(0.0001f, b.size.z);
        if (fit < 0.85f)
        {
            Debug.Log($"FloorThemeBuilder: {name} left out - it is {b.size.z:0.00} m deep and would need shrinking to {fit:P0} to fit a {depth:0.00} m wall prop.");
            UnityEngine.Object.DestroyImmediate(root);
            return null;
        }

        Settle(root, depth, 0f);
        return root;
    }

    // ---------------------------------------------------------------- DeadBody LITE + Socket Pack

    private static List<PackDef> PackDefs()
    {
        string[] bedBodies = { "Bed_&_Body/Bed_Body (1)", "Bed_&_Body/Bed_Body (2)", "Bed_&_Body/Bed_Body (3)", "Bed_&_Body/Bed_Body (4)", "Bed_&_Body/Bed_Body (5)", "Bed_&_Body/Bed_Body (6)" };
        string[] beds = { "Bed_Only/Bed_Rusty (1)", "Bed_Only/Bed_Rusty (2)", "Bed_Only/Bed_Rusty (3)", "Bed_Only/Bed_Rusty (4)", "Bed_Only/Bed_Rusty (5)" };
        string[] tubs = { "Assets/SourceFiles/Models/HorrorProps/Bathtub_Bodies_HP" };
        string[] stands = { "Hanging_Body/Standing/Deadbody_Stand (1)", "Hanging_Body/Standing/Deadbody_Stand (2)", "Hanging_Body/Standing/Deadbody_Stand (3)" };
        string[] hangs =
        {
            "Assets/SourceFiles/Models/HorrorProps/Deadbody_Hanging_HP"
        };
        string[] vents = { "Air_Vent_01", "Air_Vent_02", "Air_Vent_03", "Air_Vent_04", "Air_Vent_05" };
        string[] sockets = { "Socket_01", "Socket_02", "Socket_03", "Socket_04", "Socket_05", "Socket_06" };
        string[] fuses = { "Fuse_Box_02", "Fuse_Box_03" };

        const string dead = "Assets/DeadBody LITE", socket = "Assets/Socket Pack";

        List<PackDef> defs = new List<PackDef>
        {
            new PackDef { Name = "Pack_MorgueBed", Themes = PackWard | PackLab, Kind = PackKind.Set, Weight = 1f, RequiredFolder = dead, Compose = () => ComposeLying("Pack_MorgueBed", DeadBodyRoot, bedBodies, 90f) },
            new PackDef { Name = "Pack_EmptyBed", Themes = PackWard | PackCrypt | PackHollow, Kind = PackKind.Set, Weight = 0.8f, RequiredFolder = dead, Compose = () => ComposeLying("Pack_EmptyBed", DeadBodyRoot, beds, 90f) },
            new PackDef { Name = "Pack_BloodyBath", Themes = PackWard | PackCrypt | PackHollow, Kind = PackKind.Set, Weight = 0.8f, RequiredFolder = dead, Compose = () => ComposeLying("Pack_BloodyBath", DeadBodyRoot, tubs, 0f) },
            new PackDef { Name = "Pack_StandingShroud", Themes = PackCrypt | PackHollow, Kind = PackKind.Wall, Weight = 0.5f, Enabled = false, RequiredFolder = dead, Compose = () => ComposeStanding("Pack_StandingShroud", stands) },
            new PackDef { Name = "Pack_HangingBody", Themes = PackCrypt | PackHollow, Kind = PackKind.Ceiling, Weight = 1f, AllowLowHang = true, RequiredFolder = dead, Compose = () => ComposeHanging("Pack_HangingBody", hangs, CryptCeiling) },
            new PackDef { Name = "Pack_AirVent", Themes = PackWard | PackBoiler | PackLab, Kind = PackKind.Wall, Weight = 0.7f, RequiredFolder = socket, Compose = () => ComposeHungModels("Pack_AirVent", SocketRoot, vents, 2.6f, 2f, false) },
            new PackDef { Name = "Pack_Socket", Themes = PackWard | PackBoiler | PackLab, Kind = PackKind.Wall, Weight = 0.6f, RequiredFolder = socket, Compose = () => ComposeHungModels("Pack_Socket", SocketRoot, sockets, 0.45f, 2.5f, false) },
            new PackDef { Name = "Pack_FuseBox", Themes = PackBoiler | PackLab, Kind = PackKind.Wall, Weight = 0.5f, RequiredFolder = socket, Compose = () => ComposeHungModels("Pack_FuseBox", SocketRoot, fuses, 1.4f, 2f, true) },
        };
        defs.AddRange(GeneratedPackDefs());
        return defs;
    }

    // ---------------------------------------------------------------- tier 129: generated maze scare props

    /// <summary>A generated asset's model-only prefab path, no extension (MazeProps/&lt;asset&gt;/&lt;stem&gt;).</summary>
    private static string GenPath(string asset, string stem = null) => $"{MazePropsRoot}/{asset}/{stem ?? asset}";

    private static GenModel GM(string path, float yaw = 0f) => new GenModel { Path = path, Yaw = yaw };

    /// <summary>
    /// The tier 129 set (plan section 4). Each def requires its own asset folder and its first model prefab (or, for a
    /// decal, its texture), so a prop the 3D stage has not delivered yet is never expected by PackDressing.NeedsUpgrade.
    /// Facing convention: the 3D stage exports the turnaround's front view facing Unity +Z (into the corridor);
    /// the yaws below are relative to that.
    /// </summary>
    private static List<PackDef> GeneratedPackDefs()
    {
        const int playable = PackWard | PackBoiler | PackCrypt | PackLab; // never Hollow

        PackDef Gen(string name, string asset, int themes, PackKind kind, float weight, Func<PackBuild> compose, string firstStem = null)
        {
            return new PackDef
            {
                Name = name, Themes = themes, Kind = kind, Weight = weight, RequiredFolder = $"{MazePropsRoot}/{asset}",
                RequiredAsset = $"{GenPath(asset, firstStem)}.prefab", Compose = compose
            };
        }

        PackDef Scrawl(string name, Vector2 size, bool roll)
        {
            return new PackDef
            {
                Name = $"Gen_Scrawl_{name}", Themes = playable, Kind = PackKind.WallDecal, Weight = 0.4f, RequiredFolder = ScrawlFolder,
                RequiredAsset = $"{ScrawlFolder}/Scrawl_{name}.png", DecalSize = size, RandomRoll = roll,
                Compose = () => ComposeScrawl($"Gen_Scrawl_{name}", $"Scrawl_{name}")
            };
        }

        string wheelchair = GenPath("Wheelchair"), mannequinA = GenPath("Mannequin", "MannequinA");
        return new List<PackDef>
        {
            // Wheelchair: facing the corridor / facing the wall; turnToFit puts it side-on if it is deeper than 0.7 m that way.
            Gen("Gen_Wheelchair", "Wheelchair", PackWard | PackLab, PackKind.Wall, 0.6f,
                () => ComposeWallModels("Gen_Wheelchair", new[] { GM(wheelchair, 0f), GM(wheelchair, 180f) }, 0f, true)),
            Gen("Gen_RestraintBed", "RestraintBed", PackWard | PackLab, PackKind.Set, 0.8f,
                () => ComposeGenSet("Gen_RestraintBed", new[] { GM(GenPath("RestraintBed")) }, null)),
            // Hung from its own wall hook: model top at 2.0 m (hook at ~1.95).
            Gen("Gen_Straitjacket", "Straitjacket", PackWard, PackKind.Wall, 0.6f,
                () => ComposeWallModels("Gen_Straitjacket", new[] { GM(GenPath("Straitjacket"), 180f) }, 2.0f, false)),
            // Static emissive hatch (D8): no light (PackSpawn strips lights), no flicker.
            Gen("Gen_Furnace", "Furnace", PackBoiler, PackKind.Wall, 0.5f,
                () => ComposeWallModels("Gen_Furnace", new[] { GM(GenPath("Furnace"), 180f) }, 0f, false)),
            Gen("Gen_MeatHooks", "MeatHooks", PackBoiler, PackKind.Ceiling, 0.6f,
                () => ComposeCeilingModels("Gen_MeatHooks", new[] { GenPath("MeatHooks") }, "sack")),
            Gen("Gen_BodyBag", "BodyBag", PackCrypt | PackLab, PackKind.Floor, 0.7f,
                () => ComposeFloorLying("Gen_BodyBag", new[] { GenPath("BodyBag") })),
            // The lid is its own mesh (a child of the model prefab, slid ~25 cm aside), kept as part of the set piece.
            Gen("Gen_Coffin", "Coffin", PackCrypt, PackKind.Set, 0.7f,
                () => ComposeGenSet("Gen_Coffin", new[] { GM(GenPath("Coffin")) }, "lid")),
            // Plan name LobotomyChair; the 3D stage delivers it as TreatmentChair.
            Gen("Gen_LobotomyChair", "TreatmentChair", PackLab, PackKind.Wall, 0.6f,
                () => ComposeWallModels("Gen_LobotomyChair", new[] { GM(GenPath("TreatmentChair"), 180f) }, 0f, false)),
            // D4: the mannequins DO spawn. A standing, B slumped, C reaching, D = A turned to face the wall.
            Gen("Gen_Mannequin", "Mannequin", playable, PackKind.Wall, 0.5f,
                () => ComposeWallModels("Gen_Mannequin", new[] { GM(mannequinA, 180f), GM(GenPath("Mannequin", "MannequinB"), 180f), GM(GenPath("Mannequin", "MannequinC"), 180f), GM(mannequinA, 0f) }, 0f, false),
                "MannequinA"),

            // D5: non-verbal scrawls. Roll off for anything with an up (a number, an arrow, a drawing, a tally grid, gouges).
            Scrawl("Tally", new Vector2(0.6f, 1.0f), true),
            Scrawl("41", new Vector2(0.5f, 0.8f), false),
            Scrawl("Figure", new Vector2(0.7f, 1.1f), false),
            Scrawl("Eye", new Vector2(0.5f, 0.8f), true),
            Scrawl("Arrow", new Vector2(0.6f, 1.0f), false),
            Scrawl("Gouges", new Vector2(0.6f, 1.0f), false),
            Scrawl("TallyGrid", new Vector2(0.9f, 1.4f), false),
        };
    }

    private static bool PackAvailable(PackDef def) =>
        AssetDatabase.IsValidFolder(def.RequiredFolder) && (def.RequiredAsset == null || AssetDatabase.LoadMainAssetAtPath(def.RequiredAsset) != null);

    // ---- public build / load ----

    /// <summary>Wall and ceiling PropPieces of the DeadBody LITE / Socket Pack set for one theme bit (PackWard ... PackHollow). Built once, loaded afterwards.</summary>
    internal static PropPiece[] BuildPackProps(int themeBit)
    {
        List<PropPiece> result = new List<PropPiece>();
        foreach (GameObject prefab in BuildPackPrefabs(themeBit, set: false))
        {
            PropPiece prop = prefab.GetComponent<PropPiece>();
            if (prop != null) result.Add(prop);
        }
        return result.ToArray();
    }

    /// <summary>SetPieces of the DeadBody LITE set for one theme bit.</summary>
    internal static SetPiece[] BuildPackSetPieces(int themeBit)
    {
        List<SetPiece> result = new List<SetPiece>();
        foreach (GameObject prefab in BuildPackPrefabs(themeBit, set: true))
        {
            SetPiece piece = prefab.GetComponent<SetPiece>();
            if (piece != null) result.Add(piece);
        }
        return result.ToArray();
    }

    /// <summary>Names of every F85 prefab a theme should list, for PackDressing.NeedsUpgrade. Only pieces whose pack is in the project.</summary>
    internal static List<string> ExpectedPackNames(int themeBit, bool sets)
    {
        List<string> names = new List<string>();
        foreach (PackDef def in PackDefs())
        {
            if ((def.Themes & themeBit) == 0 || !PackAvailable(def) || (def.Kind == PackKind.Set) != sets) continue;
            names.Add(def.Name);
        }
        if (!sets)
        {
            int row = 0;
            while ((1 << row) != themeBit) row++;
            AlchemistSet wanted = ThemeSets[row] & AlchemistSet.FurnitureAll;
            foreach (AlchDef def in AlchemistDefs())
            {
                if ((def.Set & wanted) != 0 && AssetDatabase.IsValidFolder("Assets/BK_AlchemistHouse")) names.Add(def.Name);
            }
        }
        return names;
    }

    private static List<GameObject> BuildPackPrefabs(int themeBit, bool set)
    {
        if (_packPrefabs == null) _packPrefabs = new Dictionary<string, GameObject>();

        List<GameObject> result = new List<GameObject>();
        foreach (PackDef def in PackDefs())
        {
            if ((def.Themes & themeBit) == 0 || (def.Kind == PackKind.Set) != set || !PackAvailable(def)) continue;
            if (_packPrefabs.TryGetValue(def.Name, out GameObject cached) && cached != null) { result.Add(cached); continue; }

            GameObject prefab = LoadOrBuildPack(def);
            if (prefab == null) continue;
            _packPrefabs[def.Name] = prefab;
            result.Add(prefab);
        }
        return result;
    }

    private static GameObject LoadOrBuildPack(PackDef def)
    {
        string path = $"{PackFolder}/{def.Name}.prefab";
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null && !_overwritePack) return existing;

        PackBuild build = def.Compose();
        if (build.Root == null)
        {
            if (def.RequiredAsset != null) Debug.LogError($"FloorThemeBuilder: {def.Name} could not be composed from {def.RequiredFolder} (see the log above) - no prefab saved.");
            return null;
        }
        GameObject root = build.Root;
        root.name = def.Name;

        if (def.Kind == PackKind.Set)
        {
            if (build.Depth > 0.9f) Debug.LogWarning($"FloorThemeBuilder: {def.Name} intrudes {build.Depth:0.00} m, past SetPiece's 0.9 m rule.");
            SetPiece piece = root.AddComponent<SetPiece>();
            SerializedObject so = new SerializedObject(piece);
            so.FindProperty("width").floatValue = build.Width;
            so.FindProperty("depth").floatValue = build.Depth;
            so.FindProperty("weight").floatValue = def.Weight;
            so.FindProperty("enabledInMaze").boolValue = def.Enabled;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
        else
        {
            PropPiece.MountKind mount = def.Kind == PackKind.Ceiling ? PropPiece.MountKind.Ceiling
                : def.Kind == PackKind.Floor ? PropPiece.MountKind.Floor
                : def.Kind == PackKind.WallDecal ? PropPiece.MountKind.WallDecal
                : PropPiece.MountKind.Wall;
            PropPiece prop = root.AddComponent<PropPiece>();
            SerializedObject so = new SerializedObject(prop);
            so.FindProperty("mount").enumValueIndex = (int)mount;
            so.FindProperty("depth").floatValue = build.Depth;
            so.FindProperty("weight").floatValue = def.Weight;
            so.FindProperty("enabledInMaze").boolValue = def.Enabled;
            so.FindProperty("allowLowHang").boolValue = def.AllowLowHang;
            if (def.Kind == PackKind.WallDecal)
            {
                so.FindProperty("decalSizeRange").vector2Value = def.DecalSize;
                so.FindProperty("randomRoll").boolValue = def.RandomRoll;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        EnsureFolder(PackFolder);
        GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, path); // in place when it exists, so GUIDs (and scene references) survive
        UnityEngine.Object.DestroyImmediate(root);
        return saved;
    }

    /// <summary>
    /// No-dialog entry point for unity-mcp / scripts: builds (or loads) every pack prefab. With overwrite each is
    /// recomposed and saved over its file (GUIDs kept), the one exception to "hand edits survive".
    /// </summary>
    public static int BuildAllPackDressing(bool overwrite = false)
    {
        _packPrefabs = null;
        _alchemistProps = null;
        _overwritePack = overwrite;
        _overwriteExisting = overwrite;
        try
        {
            int count = BuildAlchemistProps(AlchemistSet.FurnitureAll).Length;
            for (int bit = PackWard; bit <= PackHollow; bit <<= 1)
            {
                count += BuildPackProps(bit).Length + BuildPackSetPieces(bit).Length;
            }
            return count;
        }
        finally
        {
            _overwritePack = false;
            _overwriteExisting = false;
        }
    }

    /// <summary>
    /// Tier 129, no dialogs (MazePropImport, `unity command eval`): recomposes every pack def whose RequiredFolder is
    /// `requiredFolder` (one generated asset's folder, or ScrawlFolder for the decals) and saves it over its prefab,
    /// GUIDs kept. Defs whose asset is not there yet are skipped. Returns the names written. Does not touch the scene;
    /// PackDressing.Apply lists any new prefab in its gallery rows.
    /// </summary>
    internal static List<string> RebuildGeneratedPacks(string requiredFolder)
    {
        List<string> built = new List<string>();
        if (_packPrefabs == null) _packPrefabs = new Dictionary<string, GameObject>();
        _overwritePack = true;
        try
        {
            foreach (PackDef def in PackDefs())
            {
                if (def.RequiredFolder != requiredFolder || !PackAvailable(def)) continue;
                GameObject prefab = LoadOrBuildPack(def);
                if (prefab == null) { _packPrefabs.Remove(def.Name); continue; }
                _packPrefabs[def.Name] = prefab;
                built.Add(def.Name);
            }
        }
        finally
        {
            _overwritePack = false;
        }
        return built;
    }

    // ---------------------------------------------------------------- spawning + pack quirks

    /// <summary>
    /// A pack prefab instance, unpacked and stripped for maze dressing: the pack's ExecuteInEditMode scripts
    /// (BathTub, DeadBodyHanging) are applied once and removed, then rigidbodies / colliders / lights go and any
    /// Built-in material is swapped for a URP copy. Null (logged) if the asset is missing.
    /// </summary>
    private static GameObject PackSpawn(Transform parent, string assetPath)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (prefab == null)
        {
            Debug.LogError($"FloorThemeBuilder: pack prefab not found at {assetPath}.");
            return null;
        }

        GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
        PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        go.transform.localPosition = Vector3.zero;
        go.transform.localRotation = Quaternion.identity;

        foreach (MonoBehaviour script in go.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (script == null) continue;
            string type = script.GetType().Name;
            if (type == "BathTub") ApplyBathTubState(script);
            if (type == "BathTub" || type == "DeadBodyHanging") UnityEngine.Object.DestroyImmediate(script);
        }

        AlchemistPack.Strip(go, true);
        PackMaterials.ConvertHierarchy(go);
        return go;
    }

    /// <summary>The pack's BathTub script switches the Blood and Body_n children from its serialized flags every frame; do it once, here.</summary>
    private static void ApplyBathTubState(MonoBehaviour tub)
    {
        SerializedObject so = new SerializedObject(tub);
        void Toggle(string flag, string target)
        {
            SerializedProperty f = so.FindProperty(flag);
            SerializedProperty t = so.FindProperty(target);
            GameObject go = t != null ? t.objectReferenceValue as GameObject : null;
            if (f != null && go != null) go.SetActive(f.boolValue);
        }
        Toggle("Blood", "BloodObj");
        Toggle("DeadBody_1", "Body_1");
        Toggle("DeadBody_2", "Body_2");
        Toggle("DeadBody_3", "Body_3");
    }

    private static void AttachVariants(GameObject root, List<GameObject> options)
    {
        ModelVariants variants = root.AddComponent<ModelVariants>();
        SerializedObject so = new SerializedObject(variants);
        SerializedProperty prop = so.FindProperty("options");
        prop.arraySize = options.Count;
        for (int i = 0; i < options.Count; i++) prop.GetArrayElementAtIndex(i).objectReferenceValue = options[i];
        so.ApplyModifiedPropertiesWithoutUndo();
        for (int i = 0; i < options.Count; i++) options[i].SetActive(i == 0);
    }

    private static void FitBox(GameObject holder, Bounds worldBounds, float shrink = 0f)
    {
        BoxCollider box = holder.AddComponent<BoxCollider>();
        box.center = worldBounds.center; // holders sit at the world origin
        box.size = worldBounds.size - new Vector3(shrink, 0f, shrink);
    }

    // ---- lying pieces: beds and tubs, the SetPiece shape (pivot on the floor at the wall face, +Z into the corridor) ----

    /// <summary>
    /// One variant per model under a holder, each turned by yaw (a bed lies along Z, so 90 puts its long side along the
    /// wall), bottom-centred on x = 0 and pushed back so its wall side sits 3 cm off the wall plane, with one box
    /// collider fitted to its renderer bounds. Width/Depth are the largest variant's footprint.
    /// </summary>
    private static PackBuild ComposeLying(string name, string folder, string[] models, float yaw)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float width = 0f, depth = 0f;

        for (int i = 0; i < models.Length; i++)
        {
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            string path = models[i].StartsWith("Assets/") ? $"{models[i]}.prefab" : $"{folder}{models[i]}.prefab";
            GameObject go = PackSpawn(holder.transform, path);
            if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * go.transform.rotation;
            Bounds b = AlchemistPack.RenderBounds(go);
            go.transform.position += new Vector3(-b.center.x, -b.min.y, 0.03f - b.min.z);

            b = AlchemistPack.RenderBounds(go);
            FitBox(holder, b);
            Transform seatTorso = FindDeep(go.transform, "SeatedBody_Torso");
            if (seatTorso != null)
            {
                HorrorPropAnimator anim = holder.AddComponent<HorrorPropAnimator>();
                anim.BindBones(
                    HorrorPropAnimator.PropKind.BathtubBodies,
                    seatTorso,
                    FindDeep(go.transform, "SeatedBody_Head"),
                    FindDeep(go.transform, "SlumpedBody_Head"),
                    FindDeep(go.transform, "SlumpedBody_Arm"));
            }
            width = Mathf.Max(width, b.size.x);
            depth = Mathf.Max(depth, b.max.z);
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Width = width + 0.1f, Depth = depth };
    }

    // ---- standing shroud: a wall prop (pivot on the floor at the wall face) ----

    /// <summary>Models are DeadBody LITE prefab names, or full "Assets/..." paths (no extension) like ComposeLying.</summary>
    private static PackBuild ComposeStanding(string name, string[] models)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float depth = 0f;

        for (int i = 0; i < models.Length; i++)
        {
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            string path = models[i].StartsWith("Assets/") ? $"{models[i]}.prefab" : $"{DeadBodyRoot}{models[i]}.prefab";
            GameObject go = PackSpawn(holder.transform, path);
            if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            Bounds b = AlchemistPack.RenderBounds(go);
            go.transform.position += new Vector3(-b.center.x, -b.min.y, 0.03f - b.min.z);

            b = AlchemistPack.RenderBounds(go);
            FitBox(holder, b);
            depth = Mathf.Max(depth, b.max.z);
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Depth = depth };
    }

    // ---- hanging bodies: a ceiling prop (pivot at the ceiling underside, hanging along -Y) ----

    /// <summary>
    /// The pack's DeadBodyHanging script just sets its rope bone's local Y; here that is solved once so the rope's top
    /// reaches the ceiling underside (y = 0, tucked 5 cm in) while the feet hang HangFeetClearance off the floor of a
    /// room `ceiling` m tall. The rope top is read from a baked skinned mesh at two rope lengths (it is linear in the
    /// length). Skinned renderers update off-screen, since their stored bounds know nothing of the stretched rope.
    /// </summary>
    private static PackBuild ComposeHanging(string name, string[] models, float ceiling)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float depth = ceiling - HangFeetClearance; // ceiling underside to the soles

        for (int i = 0; i < models.Length; i++)
        {
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            string path = models[i].StartsWith("Assets/") ? $"{models[i]}.prefab" : $"{DeadBodyRoot}{models[i]}.prefab";
            GameObject go = PackSpawn(holder.transform, path);
            Transform rope = go != null ? FindDeep(go.transform, "Rope_Length") : null;
            if (go == null || rope == null)
            {
                Debug.LogError($"FloorThemeBuilder: {models[i]} has no Rope_Length bone - skipped.");
                UnityEngine.Object.DestroyImmediate(holder);
                continue;
            }

            float l1 = 1.5f, l2 = 2.5f;
            rope.localPosition = new Vector3(0f, l1, 0f);
            Bounds b1 = BakedBounds(go);
            rope.localPosition = new Vector3(0f, l2, 0f);
            Bounds b2 = BakedBounds(go);
            float slope = (b2.max.y - b1.max.y) / (l2 - l1);
            float feet = b1.min.y;
            float length = l1 + (feet + depth - b1.max.y) / Mathf.Max(0.01f, slope);
            rope.localPosition = new Vector3(0f, length, 0f);

            Bounds b = BakedBounds(go);
            go.transform.position += new Vector3(0f, 0.05f - b.max.y, 0f);
            foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true)) smr.updateWhenOffscreen = true;

            // Only the body is solid (the cone and head, not the rope above or the thread-thin tail below).
            b = BakedBounds(go);
            BoxCollider box = holder.AddComponent<BoxCollider>();
            box.center = new Vector3(b.center.x, b.min.y + 1.1f, b.center.z);
            box.size = new Vector3(0.55f, 1.3f, Mathf.Max(0.5f, b.size.z * 0.8f));

            HangingBodyFit fit = holder.AddComponent<HangingBodyFit>();
            SerializedObject fitSo = new SerializedObject(fit);
            fitSo.FindProperty("rope").objectReferenceValue = rope;
            fitSo.FindProperty("body").objectReferenceValue = go.transform;
            fitSo.FindProperty("box").objectReferenceValue = box;
            fitSo.FindProperty("ropeLength0").floatValue = length;
            fitSo.FindProperty("bodyY0").floatValue = go.transform.localPosition.y;
            fitSo.FindProperty("boxY0").floatValue = box.center.y;
            fitSo.FindProperty("slope").floatValue = slope;
            fitSo.FindProperty("depth0").floatValue = depth;
            fitSo.FindProperty("feetClearance").floatValue = HangFeetClearance;
            fitSo.ApplyModifiedPropertiesWithoutUndo();

            Transform hbTorso = FindDeep(go.transform, "Torso");
            if (hbTorso != null)
            {
                HorrorPropAnimator anim = holder.AddComponent<HorrorPropAnimator>();
                anim.BindBones(
                    HorrorPropAnimator.PropKind.HangingBody,
                    hbTorso,
                    FindDeep(go.transform, "Head"),
                    FindDeep(go.transform, "Legs"),
                    null);
            }
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Depth = depth };
    }

    private static Bounds BakedBounds(GameObject go)
    {
        bool any = false;
        Bounds result = new Bounds(go.transform.position, Vector3.zero);
        foreach (SkinnedMeshRenderer smr in go.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            UnityEngine.Mesh baked = new UnityEngine.Mesh();
            smr.BakeMesh(baked);
            foreach (Vector3 v in baked.vertices)
            {
                Vector3 w = smr.transform.TransformPoint(v);
                if (!any) { result = new Bounds(w, Vector3.zero); any = true; }
                else result.Encapsulate(w);
            }
            UnityEngine.Object.DestroyImmediate(baked);
        }
        return result;
    }

    private static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform child in t)
        {
            Transform found = FindDeep(child, name);
            if (found != null) return found;
        }
        return null;
    }

    // ---- hung Socket Pack models: a wall prop with no collider ----

    /// <summary>
    /// Vents, sockets and fuse boxes: each model scaled up (the pack is life-size and tiny from down a corridor),
    /// thin axis turned to Z with its back 1 cm off the wall, centred at `height` on x = 0. A fuse box's glass door
    /// is hidden (its shader is not URP), same as the interactable fuse box.
    /// </summary>
    private static PackBuild ComposeHungModels(string name, string folder, string[] models, float height, float scale, bool hideDoor)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float depth = 0f;

        for (int i = 0; i < models.Length; i++)
        {
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            GameObject go = PackSpawn(holder.transform, $"{folder}{models[i]}.prefab");
            if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            go.transform.localScale *= scale;
            if (hideDoor)
            {
                Transform door = FindDeep(go.transform, "Fuse_Box_Door");
                if (door != null) door.gameObject.SetActive(false);
            }

            Bounds b = AlchemistPack.RenderBounds(go);
            if (b.size.x < b.size.z) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * go.transform.rotation;
            b = AlchemistPack.RenderBounds(go);
            go.transform.position += new Vector3(-b.center.x, height - b.center.y, 0.01f - b.min.z);

            depth = Mathf.Max(depth, AlchemistPack.RenderBounds(go).max.z);
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Depth = depth };
    }

    // ---------------------------------------------------------------- tier 129 composers (generated models + scrawls)

    /// <summary>A generated model-only prefab (MazePropImport) under holder, or null (logged, not an error) when that variant has not been imported yet.</summary>
    private static GameObject GenSpawn(Transform holder, string path)
    {
        string prefabPath = $"{path}.prefab";
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
        {
            Debug.Log($"FloorThemeBuilder: {prefabPath} is not imported yet - variant left out.");
            return null;
        }
        return PackSpawn(holder, prefabPath);
    }

    /// <summary>Uniform shrink so go's render depth (Z) fits budget, at most down to GenMinFit. False (logged) if it would need more.</summary>
    private static bool FitGenDepth(string label, GameObject go, float budget)
    {
        float d = AlchemistPack.RenderBounds(go).size.z;
        if (d <= budget) return true;
        float fit = budget / Mathf.Max(0.0001f, d);
        if (fit < GenMinFit)
        {
            Debug.LogWarning($"FloorThemeBuilder: {label} left out - it is {d:0.00} m deep and would need shrinking to {fit:P0} to fit {budget:0.00} m.");
            return false;
        }
        go.transform.localScale *= fit;
        Debug.Log($"FloorThemeBuilder: {label} shrunk to {fit:P0} to fit {budget:0.00} m deep.");
        return true;
    }

    private static Transform FindDeepContaining(Transform t, string part)
    {
        if (t.name.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0) return t;
        foreach (Transform child in t)
        {
            Transform found = FindDeepContaining(child, part);
            if (found != null) return found;
        }
        return null;
    }

    // ---- generated wall props: wheelchair, straitjacket, furnace, treatment chair, mannequins ----

    /// <summary>
    /// Each variant turned by its yaw and fitted to the 0.7 m wall-prop rule: turned side-on first when turnToFit and
    /// that is shallower, then shrunk by at most 15 %, otherwise left out (logged). Then centred on x = 0 with its back
    /// GenWallGap off the wall, standing on the floor - or hung with its top at topHeight when that is above 0 (never
    /// below the floor) - with one box collider fitted to its render bounds.
    /// </summary>
    private static PackBuild ComposeWallModels(string name, GenModel[] models, float topHeight, bool turnToFit)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float depth = 0f, budget = WallPropMaxDepth - GenWallGap;

        for (int i = 0; i < models.Length; i++)
        {
            string label = $"{name}/Variant{i + 1}";
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            GameObject go = GenSpawn(holder.transform, models[i].Path);
            if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            go.transform.rotation = Quaternion.Euler(0f, models[i].Yaw, 0f) * go.transform.rotation;
            Bounds b = AlchemistPack.RenderBounds(go);
            if (turnToFit && b.size.z > budget && b.size.x < b.size.z)
            {
                go.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * go.transform.rotation;
                Debug.Log($"FloorThemeBuilder: {label} turned side-on ({b.size.z:0.00} m deep facing out, {b.size.x:0.00} m side-on).");
            }
            if (!FitGenDepth(label, go, budget)) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            b = AlchemistPack.RenderBounds(go);
            float lift = topHeight > 0f ? Mathf.Max(topHeight - b.max.y, -b.min.y) : -b.min.y;
            go.transform.position += new Vector3(-b.center.x, lift, GenWallGap - b.min.z);

            b = AlchemistPack.RenderBounds(go);
            FitBox(holder, b);
            depth = Mathf.Max(depth, b.max.z);
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Depth = depth };
    }

    // ---- generated set pieces: restraint bed, coffin ----

    /// <summary>
    /// Like ComposeLying, but the long side is found rather than assumed: each variant is turned so its longer floor axis
    /// runs along the wall (local X) whichever way the 3D stage exported it (the DeadBody beds lie along Z and need yaw
    /// 90; a model reconstructed from a long-side front view already lies along X). requiredChild is part of a child's
    /// name the piece must keep (the coffin's separate "lid" mesh) - warned about when missing. One box collider per
    /// variant fitted to its render bounds, lid included.
    /// </summary>
    private static PackBuild ComposeGenSet(string name, GenModel[] models, string requiredChild)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float width = 0f, depth = 0f;

        for (int i = 0; i < models.Length; i++)
        {
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            GameObject go = GenSpawn(holder.transform, models[i].Path);
            if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            go.transform.rotation = Quaternion.Euler(0f, models[i].Yaw, 0f) * go.transform.rotation;
            Bounds b = AlchemistPack.RenderBounds(go);
            if (b.size.z > b.size.x) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * go.transform.rotation;
            if (requiredChild != null && FindDeepContaining(go.transform, requiredChild) == null)
            {
                Debug.LogWarning($"FloorThemeBuilder: {name}/Variant{i + 1} has no child named *{requiredChild}* - is it missing from the export?");
            }

            b = AlchemistPack.RenderBounds(go);
            go.transform.position += new Vector3(-b.center.x, -b.min.y, GenWallGap - b.min.z);

            b = AlchemistPack.RenderBounds(go);
            FitBox(holder, b);
            width = Mathf.Max(width, b.size.x);
            depth = Mathf.Max(depth, b.max.z);
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Width = width + 0.1f, Depth = depth };
    }

    // ---- generated ceiling props: meat hooks ----

    /// <summary>
    /// Pivot at the ceiling underside: each model centred on x/z with its top 5 cm into the ceiling, hanging along -Y;
    /// Depth is the drop. No collider, except one box fitted to the first child whose name contains solidChild (the
    /// sack), when given and found. MazeGenerator.BuildCeilingProps skips the prop under a ceiling lower than Depth + 2.2 m.
    /// </summary>
    private static PackBuild ComposeCeilingModels(string name, string[] models, string solidChild)
    {
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float depth = 0f;

        for (int i = 0; i < models.Length; i++)
        {
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            GameObject go = GenSpawn(holder.transform, models[i]);
            if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            Bounds b = AlchemistPack.RenderBounds(go);
            go.transform.position += new Vector3(-b.center.x, 0.05f - b.max.y, -b.center.z);

            b = AlchemistPack.RenderBounds(go);
            depth = Mathf.Max(depth, -b.min.y);
            Transform solid = solidChild != null ? FindDeepContaining(go.transform, solidChild) : null;
            if (solid != null && solid != go.transform) FitBox(holder, AlchemistPack.RenderBounds(solid.gameObject));
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        if (depth > 1.8f) Debug.LogWarning($"FloorThemeBuilder: {name} drops {depth:0.00} m - it only spawns under ceilings of {depth + 2.2f:0.0} m or more (Boiler Deck is 4.0 m).");
        AttachVariants(root, options);
        return new PackBuild { Root = root, Depth = depth };
    }

    // ---- generated floor props: body bag ----

    /// <summary>
    /// PropPiece's Floor shape: long side along the wall, bottom on the floor, back 2 cm off the wall, no collider (the
    /// player walks through it). Must stay within 0.35 m high and 0.45 m deep: shrunk by at most 15 % to fit, otherwise
    /// left out (logged).
    /// </summary>
    private static PackBuild ComposeFloorLying(string name, string[] models)
    {
        const float gap = 0.02f;
        GameObject root = new GameObject(name);
        List<GameObject> options = new List<GameObject>();
        float depth = 0f;

        for (int i = 0; i < models.Length; i++)
        {
            string label = $"{name}/Variant{i + 1}";
            GameObject holder = new GameObject($"Variant{i + 1}");
            holder.transform.SetParent(root.transform, false);
            GameObject go = GenSpawn(holder.transform, models[i]);
            if (go == null) { UnityEngine.Object.DestroyImmediate(holder); continue; }

            Bounds b = AlchemistPack.RenderBounds(go);
            if (b.size.z > b.size.x) go.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * go.transform.rotation;

            b = AlchemistPack.RenderBounds(go);
            float fit = Mathf.Min(1f, FloorPropMaxHeight / Mathf.Max(0.0001f, b.size.y), (FloorPropMaxDepth - gap) / Mathf.Max(0.0001f, b.size.z));
            if (fit < GenMinFit)
            {
                Debug.LogWarning($"FloorThemeBuilder: {label} left out - {b.size.y:0.00} m high x {b.size.z:0.00} m deep is past the floor-prop rule ({FloorPropMaxHeight} x {FloorPropMaxDepth} m) by more than 15 %.");
                UnityEngine.Object.DestroyImmediate(holder);
                continue;
            }
            if (fit < 1f)
            {
                go.transform.localScale *= fit;
                Debug.Log($"FloorThemeBuilder: {label} shrunk to {fit:P0} to fit the floor-prop rule.");
            }

            b = AlchemistPack.RenderBounds(go);
            go.transform.position += new Vector3(-b.center.x, -b.min.y, gap - b.min.z);
            depth = Mathf.Max(depth, AlchemistPack.RenderBounds(go).max.z);
            options.Add(holder);
        }

        if (options.Count == 0) { UnityEngine.Object.DestroyImmediate(root); return default; }
        AttachVariants(root, options);
        return new PackBuild { Root = root, Depth = depth };
    }

    // ---- scrawl decals (D5) ----

    /// <summary>
    /// One alpha-clipped quad on the F70 decal material recipe (LoadOrCreateDecalMat, white tint - the colour is in the
    /// keyed texture), shaped to the texture's aspect so DecalSizeRange is its longest side (the DecalVariants rule).
    /// The texture's import settings are made decal-ready here, so the python keying step
    /// (plannings/concepts/maze_props_b1/decals/key_scrawls.py) only has to write the PNG.
    /// </summary>
    private static PackBuild ComposeScrawl(string name, string textureName)
    {
        string path = $"{ScrawlFolder}/{textureName}.png";
        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer == null)
        {
            Debug.LogError($"FloorThemeBuilder: {path} not found - run plannings/concepts/maze_props_b1/decals/key_scrawls.py.");
            return default;
        }
        EnsureScrawlImport(importer);
        importer.GetSourceTextureWidthAndHeight(out int w, out int h);
        float aspect = h > 0 ? (float)w / h : 1f;

        Material material = LoadOrCreateDecalMat(ScrawlFolder, textureName, AssetDatabase.LoadAssetAtPath<Texture2D>(path), Color.white, 0.1f);
        GameObject root = new GameObject(name);
        GameObject quad = CreateDecalQuad(root.transform, "Quad", material, false, Vector3.zero, Quaternion.identity, 1f);
        quad.transform.localScale = aspect >= 1f ? new Vector3(1f, 1f / aspect, 1f) : new Vector3(aspect, 1f, 1f);
        return new PackBuild { Root = root };
    }

    /// <summary>sRGB colour + alpha, clamped, mips that keep the clipped coverage at the material's 0.4 cutoff, no power-of-two stretch, 1024 max.</summary>
    private static void EnsureScrawlImport(TextureImporter importer)
    {
        if (importer.textureType == TextureImporterType.Default && importer.sRGBTexture && importer.alphaIsTransparency
            && importer.alphaSource == TextureImporterAlphaSource.FromInput && importer.wrapMode == TextureWrapMode.Clamp
            && importer.mipmapEnabled && importer.mipMapsPreserveCoverage && Mathf.Approximately(importer.alphaTestReferenceValue, 0.4f)
            && importer.npotScale == TextureImporterNPOTScale.None && importer.maxTextureSize == 1024) return;

        importer.textureType = TextureImporterType.Default;
        importer.sRGBTexture = true;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = true;
        importer.wrapMode = TextureWrapMode.Clamp;
        importer.mipmapEnabled = true;
        importer.mipMapsPreserveCoverage = true;
        importer.alphaTestReferenceValue = 0.4f;
        importer.npotScale = TextureImporterNPOTScale.None;
        importer.maxTextureSize = 1024;
        importer.SaveAndReimport();
    }
}
