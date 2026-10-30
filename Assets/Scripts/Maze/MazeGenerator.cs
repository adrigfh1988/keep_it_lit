using System.Collections.Generic;
using Unity.AI.Navigation;
using UnityEngine;

/// <summary>
/// Builds a procedural maze at Play time, bakes a runtime NavMesh over it, then moves the player,
/// the AI follower and the collectible stars inside it.
///
/// Everything happens in Awake (execution order -100) because other components capture state in Start:
/// GameManager counts the Pickup objects, RespawnPlayer records the spawn position.
/// </summary>
[DefaultExecutionOrder(-100)]
public partial class MazeGenerator : MonoBehaviour
{
    [Header("Maze")]
    [Tooltip("Number of cells along X")]
    [SerializeField] private int width = 11;
    [Tooltip("Number of cells along Z")]
    [SerializeField] private int height = 11;
    [Tooltip("Corridor pitch in metres. The walkable corridor is cellSize - wallThickness. Raised at Awake if it would make the halls narrower than minCorridorWidth.")]
    [SerializeField] private float cellSize = 4.5f;
    [Tooltip("Narrowest walkable hall allowed (m). cellSize is forced up to minCorridorWidth + wallThickness if needed.")]
    [SerializeField] private float minCorridorWidth = 4f;
    [Tooltip("Open a wall at every dead end so the maze has loops and nowhere to get cornered")]
    [SerializeField] private bool removeDeadEnds = true;
    [SerializeField] private float wallThickness = 0.5f;
    [Tooltip("At 1.6 m eye height, 3 m walls read as low and leak the layout")]
    [SerializeField] private float wallHeight = 4f;
    [Tooltip("0 = a different maze every run (the seed used is logged)")]
    [SerializeField] private int seed = 0;
    [Tooltip("Bottom-left corner of the maze. Y is the floor top. Keep well clear of the tutorial props: the navmesh bake volume pads 2 m beyond the walls, and anything it catches becomes walkable.")]
    [SerializeField] private Vector3 origin = new Vector3(-70f, 4.98f, -70f);
    [Tooltip("A ceiling is what stops the unfoggable skybox showing above the walls")]
    [SerializeField] private bool buildCeiling = true;

    [Header("Materials")]
    [SerializeField] private Material wallMaterial;
    [SerializeField] private Material floorMaterial;
    [Tooltip("Both source materials are bright and the wall one is emissive, so the generator tints runtime copies rather than editing the assets the rest of the scene shares")]
    [SerializeField] private Color wallTint = new Color(0.22f, 0.22f, 0.25f);
    [SerializeField] private Color floorTint = new Color(0.10f, 0.10f, 0.12f);
    [SerializeField] private Color ceilingTint = new Color(0.06f, 0.06f, 0.07f);
    [Tooltip("The stock 0.477 throws a specular flare straight back down the flashlight beam")]
    [SerializeField] private float surfaceSmoothness = 0.15f;

    [Header("Stars")]
    [SerializeField] private GameObject starPrefab;
    [SerializeField] private int starCount = 5;
    [Tooltip("Height of a star above the maze floor")]
    [SerializeField] private float starHeight = 1f;
    [Tooltip("Hide the hand-placed stars of the original scene")]
    [SerializeField] private bool disableExistingPickups = true;
    [Tooltip("An emissive material glows but lights nothing. Without a light per star you walk straight past them in the dark.")]
    [SerializeField] private bool starLights = true;
    [SerializeField] private float starLightRange = 5f;
    [SerializeField] private float starLightIntensity = 1.5f;
    [Tooltip("Used only when useFloorProfile is off; otherwise FloorProfile.ShardCount wins")]
    [SerializeField] private int shardCount = 6;

    [Header("Atmosphere")]
    [SerializeField] private bool firstPerson = true;
    [SerializeField] private bool darkness = true;
    [Tooltip("Collecting the last star opens a hatch and starts a countdown instead of winning outright")]
    [SerializeField] private bool escapeSequence = true;
    [Tooltip("Title screen and rules screen, with the game held frozen until Start is pressed")]
    [SerializeField] private bool mainMenu = true;
    [Tooltip("Looping bed, e.g. SFX_AmbienceClose. Only used (pitched down into a drone) when Base Music is empty.")]
    [SerializeField] private AudioClip droneClip;
    [Tooltip("Base music: each floor loops one of these at its own pitch, picked from the seed (a Retry keeps it). Empty = the Drone Clip above.")]
    [SerializeField] private AudioClip[] baseMusic;
    [Tooltip("One-shot fired when the hunter spots you")]
    [SerializeField] private AudioClip stingClip;
    [Tooltip("Looping track for the escape, e.g. Music_Exciting")]
    [SerializeField] private AudioClip panicClip;
    [Tooltip("Played whole when the hunter spots you and starts a chase (Violin Stabs). Empty = the synthesised sting.")]
    [SerializeField] private AudioClip spottedClip;
    [Tooltip("Looping track under the title and rules screens (Discovering Rooms); fades out on START.")]
    [SerializeField] private AudioClip menuMusic;

    [Header("Decals")]
    [Tooltip("Multiplies the theme's wall/floor decal chances (capped at 0.9 per roll) and, above 1, allows one more decal per cell. 1 = the theme's own numbers.")]
    [SerializeField, Min(0f)] private float decalDensity = 2f;
    [Tooltip("Extra pick weight for decals with look-alike variants (the Blood decal pack splats/smears/pools) over grime, drips and the rest. 1 = the prefab's own weight.")]
    [SerializeField, Min(0f)] private float bloodWeight = 3f;

    [Header("Props (F85)")]
    [Tooltip("Multiplies the theme's wall / ceiling / floor prop chances (capped at 0.85 per roll, never below the theme's own number). 1 = the theme's own numbers. Only the +4 / +6 dressing streams shift.")]
    [SerializeField, Min(0f)] private float propDensity = 1.4f;
    [Tooltip("Extra set pieces on a themed floor, on top of the theme's own count. Set pieces only land in eligible cells, so this is a ceiling, not a guarantee.")]
    [SerializeField, Min(0)] private int setPieceBonus = 1;

    [Header("Actors")]
    [SerializeField] private NavMeshSurface navMeshSurface;
    [SerializeField] private AIFollower aiFollower;
    [Tooltip("Jumping out of the maze would trivialise it")]
    [SerializeField] private bool disablePlayerJump = true;

    public enum DebugFloorShape
    {
        Auto = -1,
        Rectangle = 0,
        Ring = 1,
        TwinWings = 2,
        Cross = 3,
        LChasm = 4
    }

    [Header("Debug — Start & Mode")]
    [Tooltip("For testing a maze: skip the title screen, the Intake and the hub and start the run straight away in Debug Floor's maze (floor 1 when Debug Floor is 0). Fresh start only; the shop, hub and finale debug starts take priority.")]
    [SerializeField] private bool debugStartInMaze = false;
    [Tooltip("With Debug Start In Maze, starts a DESCENT at depth Debug Floor (depth 1 if Debug Floor is 0). No effect otherwise.")]
    [SerializeField] private bool debugDescent = false;
    [Tooltip("For testing the Asylum hub: skip the title screen and the Intake and arrive in the hub as if the previous floor had just been cleared - the wings before Debug Floor are CLEARED, its wing is OPEN (and leads to that floor's maze), the hub has that floor's decay, patients and set piece. Debug Floor 0 = floor 1, at the front door. Fresh start only; ignored while Debug Start In Shop is on.")]
    [SerializeField] private bool debugStartInHub = false;
    [Tooltip("For testing: skip the title screen and open the floor as if the hatch had just been reached.")]
    [SerializeField] private bool debugStartInShop = false;
    [Tooltip("For testing the ending: skip the title screen and the Intake and arrive at the Director's desk in the hub, as if floor 5 had just been cleared (the finale). Takes priority over Debug Start In Hub; ignored while Debug Start In Shop is on. Fresh start only.")]
    [SerializeField] private bool debugStartInFinale = false;
    [Tooltip("With Debug Start In Finale: mark the five maze case files read first, so file 105 is in the desk drawer and ending B (the master switch) is possible. Off = ending A only.")]
    [SerializeField] private bool debugFinaleAllFiles = true;

    [Header("Debug — Floor, Shape & Mechanics")]
    [Tooltip("For testing: start a fresh run on this floor (1 to 5) without playing up to it. The game carries on from there - the shop door still leads to the next floor. 0 = start on floor 1. With Debug Start In Maze + Debug Descent: the DESCENT depth to start at (any n, e.g. 12 = LEVEL -12).")]
    [SerializeField] private int debugFloor = 0;
    [Tooltip("Editor convenience: Auto uses the profile's rolled shape; otherwise forces Rectangle, Ring (The Courtyard), TwinWings, Cross (The Panopticon), or LChasm (The Chasm).")]
    [SerializeField] private DebugFloorShape debugFloorShape = DebugFloorShape.Auto;
    [Tooltip("Editor convenience: Auto uses the floor's rolled morphology; otherwise forces Standard, CompactWarren, CavernousHalls, or ChambersAndChokepoints.")]
    [SerializeField] private DebugMazeMorphology debugMorphology = DebugMazeMorphology.Auto;
    [Tooltip("For testing: forces this floor's mechanics instead of rolling them. None = roll normally.")]
    [SerializeField] private FloorProfile.Objective debugObjectives = FloorProfile.Objective.None;
    [Tooltip("Testing: build the loop on any themed floor, skipping the first-floor rule, the once-per-campaign rule and the roll (the roll is still drawn, so the maze is unchanged). KEEP IT LIT > Debug > Go To Loop Entrance takes you there in Play")]
    [SerializeField] private bool debugForceLoop = false;
    [Tooltip("Testing: gates on any themed floor (one gate on floors below the final, as the profile says; the +17 roll is still drawn). KEEP IT LIT > Debug > Go To Exit Gate takes you there in Play")]
    [SerializeField] private bool debugForceGates = false;

    [Header("Debug — Entities & Cheats")]
    [Tooltip("For testing / getting through floors fast: every star, key, fuse box and the floor's case file go in the cells nearest the start, the hatch opens next to the player, and no shutters, security doors or vaults are built. The hunter still starts in the farthest cell.")]
    [SerializeField] private bool superEasyMode = false;
    [Tooltip("For testing the floor map: unlocks and reveals the entire floor map (all non-void cells, walls, doors and switches, just like clearing the loop corridor) and opens it automatically.")]
    [SerializeField] private bool debugFullMap = false;
    [Tooltip("Not None: forces exactly this entity set on any floor, in the story or a descent (the way to test one). None = the roster decides (EntityRoster). Restore to None before saving.")]
    [SerializeField] private EntityKind debugEntities = EntityKind.None;


    [Tooltip("For testing the shop: deposited into the wallet when the maze is built.")]
    [SerializeField] private int debugShards = 0;
    [Tooltip("F76: forces TouchInput.Active on regardless of Mode, so the on-screen controls show in the Editor without a touchscreen - click them with the mouse, or use the Device Simulator.")]
    [SerializeField] private bool debugForceTouchControls = false;
    [Tooltip("Log which library slots are filled and which clip each sound played (once per clip).")]
    [SerializeField] private bool logAudioResolution = false;

    [Header("Floors")]
    [Tooltip("Scale the maze, hunter, light and timers to how far into the game the player is (see FloorProfile). Off = use the values in this Inspector as they are.")]
    [SerializeField] private bool useFloorProfile = true;

    [Header("Lockers")]
    [SerializeField] private int lockerCount = 10;
    [Tooltip("Width across the corridor (m)")]
    [SerializeField] private float lockerWidth = 1.1f;
    [Tooltip("Depth out from the back wall (m). Keep <= 0.7 so the cell centre stays reachable for the 0.5 m agent.")]
    [SerializeField] private float lockerDepth = 0.6f;
    [SerializeField] private float lockerHeight = 2.1f;
    [SerializeField] private Color lockerTint = new Color(0.16f, 0.17f, 0.19f);

    [Header("Creature voices")]
    [Tooltip("F82 speech bubbles over the maze hunter and Stan. Off since 10 Oct 2026: they read as goofy; recorded voices (OrderlyPa) replace them.")]
    [SerializeField] private bool creatureSpeechBubbles = false;

    [Header("Wall lamps")]
    [SerializeField] private bool wallLamps = true;
    [Tooltip("Roughly one lamp per this many cells, plus one above every locker")]
    [SerializeField] private int cellsPerLamp = 5;
    [SerializeField] private float lampHeight = 2.6f;
    [SerializeField] private Color lampColor = new Color(1f, 0.72f, 0.42f);
    [SerializeField] private float lampRange = 9.5f;
    [SerializeField] private float lampIntensity = 3.2f;
    [Range(0f, 1f)]
    [SerializeField] private float faultyLampChance = 0.25f;

    [Header("Themes")]
    [Tooltip("Scene gallery built by KEEP IT LIT > Build > Floor Themes. Found automatically; assign only to override.")]
    [SerializeField] private FloorThemeSet themeSet;
    [Tooltip("Combine the static themed geometry (walls, pillars, floor, ceiling) after the navmesh bake. Off until profiled.")]
    [SerializeField] private bool staticBatchThemedGeometry = false;

    [Header("Interactables (F72)")]
    [Tooltip("Scene gallery built by KEEP IT LIT > Build > Interactables. Found automatically; assign only to override.")]
    [SerializeField] private InteractableKit interactableKit;

    [Header("Ground fog")]
    [Tooltip("Fog banks alive per m² of maze floor (the hub uses 0.24). 0 = no fog. Clones InteractableKit.fogBank per chunk of cells.")]
    [SerializeField, Min(0f)] private float fogDensity = 0.12f;
    [Tooltip("Cells per side of one fog emitter. Smaller chunks pause more of the fog off screen; more emitters cost more draw calls.")]
    [SerializeField, Min(1)] private int fogChunkCells = 4;
    [Tooltip("Cap on fog banks alive in the whole maze (overdraw is the cost, on the web build most of all); the density is scaled down to fit.")]
    [SerializeField, Min(0)] private int fogMaxParticles = 700;

    [Header("Audio (F97)")]
    [Tooltip("Recorded clips for the sounds that used to be generated. Built by KEEP IT LIT > Check Project > Build Missing Pieces (Audio library). Empty slots keep the generated sound.")]
    [SerializeField] private GameAudioLibrary audioLibrary;
    [Tooltip("S2: GameMixer.mixer (Master > Music, Effects; the three volumes exposed). Assigned by KEEP IT LIT > Build Missing Pieces. Empty = only the Master volume works.")]
    [SerializeField] private UnityEngine.Audio.AudioMixer gameMixer;

    [Header("F103 - Maze terror (Tier A)")]
    [Tooltip("One switch per Tier A terror feature, plus the [Terror] Console log. Spec: plannings/maze-terror-spec.md")]
    [SerializeField] private TerrorSettings terror = new TerrorSettings();

    [Header("F105 C9 - narrow beam")]
    [Tooltip("The floor's fog density is multiplied by this while TerrorSettings.narrowBeam is on, so a narrower beam also sees a little less far")]
    [SerializeField, Min(1f)] private float narrowBeamFogScale = 1.05f;

    [Header("Door buttons")]
    [Tooltip("A door's button goes on a wall the player can see from in front of the door: a cell in a straight open line from it, at most this many cells away (10 Oct 2026 - a button out of sight turned a Lockdown floor into a hunt for a switch). Only when no such wall is free does it go anywhere farther.")]
    [SerializeField, Min(1)] private int buttonSightCells = 4;

    [Header("F103 A10 - bait dead ends")]
    [Tooltip("A dead end deeper than this (cells from the junction it hangs off) is opened after all: only short pockets survive braiding")]
    [SerializeField, Min(1)] private int maxDeadEndDepth = 2;
    [Tooltip("A dead end must be at least this many steps (Manhattan, from the start cell) away to be kept - never a pocket at the spawn")]
    [SerializeField, Min(0)] private int deadEndStartClearance = 3;

    /// <summary>
    /// F70: one dressing source, two providers. Every prop pass (wall/ceiling/floor props, decals, set
    /// pieces, dust) reads this instead of _theme directly. Resolved once by ResolveDressing, right after ResolveTheme.
    /// </summary>
    private struct MazeDressing
    {
        public PropPiece[] Props;
        public SetPiece[] SetPieces;
        public DustMotes Dust;
        public float WallPropChance, CeilingPropChance, FloorPropChance, WallDecalChance, FloorDecalChance;
        public int MaxDecalsPerCell, SetPieceCount;
    }

    private MazeGrid _grid;

    /// <summary>Pure-C# grid topology and BFS model backing this maze.</summary>
    public MazeGrid Grid => _grid;

    // Wall flags per cell. The west/south walls are the ones actually built; the east wall of cell
    // (x, z) is the west wall of (x + 1, z), so carving a passage clears both sides.
    private bool[,] _wallN;
    private bool[,] _wallE;
    private bool[,] _wallS;
    private bool[,] _wallW;
    private bool[,] _void;
    private int _playableCellCount;
    private Vector2Int _startCell;

    private bool IsVoid(int x, int z) => _void != null && x >= 0 && x < width && z >= 0 && z < height && _void[x, z];
    private bool IsVoid(Vector2Int c) => IsVoid(c.x, c.y);

    // Step distance from the start cell through open passages, used to place the stars and the AI far away.
    private int[,] _distance;

    private Transform _mazeRoot;
    private readonly List<Vector3> _cellCenters = new List<Vector3>();
    private readonly List<Transform> _stars = new List<Transform>();
    /// <summary>Cells SpawnStars chose. Kept so SpawnShards can stay clear of them.</summary>
    private readonly List<Vector2Int> _starCells = new List<Vector2Int>();
    /// <summary>F45: cell centres two steps from star i, around a corner. Parallel to _stars/_starCells.</summary>
    private readonly List<List<Vector3>> _ambushCells = new List<List<Vector3>>();
    private readonly List<Material> _runtimeMaterials = new List<Material>();
    private readonly List<Vector2Int> _lockerCells = new List<Vector2Int>();
    // Direction from a locker cell's centre toward the wall its locker stands against
    private readonly Dictionary<Vector2Int, Vector3> _lockerWall = new Dictionary<Vector2Int, Vector3>();
    private readonly List<Locker> _lockers = new List<Locker>();
    private readonly List<WallLamp> _lamps = new List<WallLamp>();
    /// <summary>F73: the cell each entry of _lamps was built at, parallel to it - lets BuildFuses map a lamp to its sector without lamps needing to know their own cell.</summary>
    private readonly List<Vector2Int> _lampCells = new List<Vector2Int>();
    private FloorProfile _profile;
    private Material _wallMat;
    private Material _floorMat;
    private Material _ceilingMat;

    private FloorTheme _theme;
    private Transform _wallsGroup, _pillarsGroup, _floorGroup, _ceilingGroup, _lampsGroup, _lockersGroup, _propsGroup;
    /// <summary>F70: parents for set-piece and decal clones, created alongside the other groups in BuildGeometry.</summary>
    private Transform _setPiecesGroup, _decalsGroup;
    /// <summary>Wall directions already taken by a wall prop, per cell. Consulted by BuildCeilingProps (no double-dressing a cell) and SpawnShards (no shard clipping a prop).</summary>
    private readonly Dictionary<Vector2Int, List<Vector3>> _propWalls = new Dictionary<Vector2Int, List<Vector3>>();
    /// <summary>F70: resolved once by ResolveDressing. Every prop/decal/set-piece pass reads this, never _theme directly.</summary>
    private MazeDressing _dressing;
    /// <summary>F70: cells a set piece has reserved this run. Skipped by wall/ceiling/floor props and decals.</summary>
    private readonly HashSet<Vector2Int> _setPieceCells = new HashSet<Vector2Int>();
    /// <summary>F107 A: cells BuildCeilingProps hung something from. Read by SpawnStars and MazeEscape so nothing sits under a hanging body.</summary>
    private readonly HashSet<Vector2Int> _ceilingPropCells = new HashSet<Vector2Int>();
    private readonly List<ShiftablePlacement> _shiftables = new List<ShiftablePlacement>(); // F104 B9/B10: dressing worth quietly changing

    /// <summary>F72: every door/button/vault-loot instance built this run, kept so SetUpAtmosphere can late-bind the HUD/flashlight once they exist (same pattern as _lockers).</summary>
    private readonly List<MazeDoor> _doors = new List<MazeDoor>();
    private readonly List<WallButton> _buttons = new List<WallButton>();
    // Which passage each door sits in (CreateDoor), for DebugMap: a door's position is on the wall line, between two cells.
    private readonly Dictionary<MazeDoor, Passage> _doorPassages = new Dictionary<MazeDoor, Passage>();
    private readonly List<BatteryCellPickup> _batteryPickups = new List<BatteryCellPickup>();
    /// <summary>F72: cells SpawnShards must place a shard in (a vault's 2 forced shards), consumed and cleared by SpawnShards. Each entry is one shard - a vault with 2 shards appears twice.</summary>
    private readonly List<Vector2Int> _vaultForcedShardCells = new List<Vector2Int>();
    /// <summary>F72: every cell BuildDoors used for a door, button or vault this run - reused (not copied) by F73's BuildFuses so a fuse box never lands on one of them.</summary>
    private readonly HashSet<Vector2Int> _doorUsedCells = new HashSet<Vector2Int>();
    /// <summary>F73: every fuse box instance built this run.</summary>
    private readonly List<FuseBox> _fuseBoxes = new List<FuseBox>();

    /// <summary>F77: every key built this run (KeyHunt floors only), late-bound onto the player's KeyRing once it exists.</summary>
    private readonly List<KeyPickup> _keys = new List<KeyPickup>();

    /// <summary>F74: every lore note built this run, kept so SetUpAtmosphere can late-bind the reader/HUD once they exist (same pattern as _buttons).</summary>
    private readonly List<LoreNote> _loreNotes = new List<LoreNote>();
    /// <summary>F74: this floor's lantern (0 or 1), late-bound once the player/hunter/HUD exist.</summary>
    private readonly List<Lantern> _lanterns = new List<Lantern>();
    /// <summary>F74: every theme interactable built this run (call bell/steam valve/candles/terminal), late-bound for its HUD reference.</summary>
    private readonly List<ThemeInteractable> _themeInteractables = new List<ThemeInteractable>();
    /// <summary>F74: Lab terminals specifically - ConfigureTerminal needs the player camera, which does not exist yet when BuildThemeInteractables runs.</summary>
    private readonly List<ThemeInteractable> _pendingTerminals = new List<ThemeInteractable>();

    /// <summary>F75: this floor's stalker (whenever EntityRoster says so: story floors 4-5, a descent from depth 4), late-bound once the player/camera/flashlight/hunter/HUD/outcome exist. Null on every other floor.</summary>
    private Stalker _stalker;

    /// <summary>F103 A10: the dead ends that survived braiding on this floor (short pockets only), in generation order.</summary>
    private readonly List<Vector2Int> _keptDeadEnds = new List<Vector2Int>();
    /// <summary>F103 A10: the kept dead ends that hold a star (the first FloorProfile.DeadEndBaitStars usable ones). Locker cells avoid them, and a locker stands at each one's mouth.</summary>
    private readonly List<Vector2Int> _baitDeadEnds = new List<Vector2Int>();

    /// <summary>The theme this maze was built from, or null when the primitive fallback was used.</summary>
    public FloorTheme Theme => _theme;

    /// <summary>The spawned stars. Entries become null as they are collected.</summary>
    public IReadOnlyList<Transform> Stars => _stars;

    /// <summary>Every locker built for this maze.</summary>
    public IReadOnlyList<Locker> Lockers => _lockers;

    /// <summary>F104 B9/B10: every set piece, and every wall or ceiling prop with look-alike models, placed this floor. Dressing only - never a gameplay object.</summary>
    public IReadOnlyList<ShiftablePlacement> Shiftables => _shiftables;

    /// <summary>Every wall lamp built for this maze.</summary>
    public IReadOnlyList<WallLamp> Lamps => _lamps;

    /// <summary>Every fuse box built for this maze (F75 Fuse Map compass targeting).</summary>
    public IReadOnlyList<FuseBox> FuseBoxes => _fuseBoxes;

    /// <summary>Every wall button built for this maze (F75 Fuse Map compass targeting).</summary>
    public IReadOnlyList<WallButton> Buttons => _buttons;

    /// <summary>Every key built for this maze this run (F77 Key Hunt; ConsumableController's Fuse Map compass targeting).</summary>
    public IReadOnlyList<KeyPickup> Keys => _keys;

    /// <summary>Floor-level world centre of every cell, row-major (x + z * width).</summary>
    public IReadOnlyList<Vector3> CellCenters => _cellCenters;

    /// <summary>The seed this maze was actually built from, whether it came from the Inspector, a retry or the clock.</summary>
    public int UsedSeed { get; private set; }

    /// <summary>Cells along X (F105: the hunter's heat map is sized from it).</summary>
    public int GridWidth => width;

    /// <summary>Cells along Z (F105: the hunter's heat map is sized from it).</summary>
    public int GridHeight => height;

    /// <summary>World size of one cell (F71: DreadDirector uses this to keep a kill-wave away from a star).</summary>
    public float CellSize => cellSize;
    /// <summary>Debug: everything to collect is packed next to the start (see the Inspector tooltip). MazeEscape reads it to open the hatch next to the player.</summary>
    public bool SuperEasyMode => superEasyMode;
    /// <summary>Floor-level world centre of the maze's start cell (<c>(width/2, height/2)</c> on <c>Cross</c>, <c>(0, 0)</c> on other shapes).</summary>
    public Vector3 StartCellCenter => CellCenter(_startCell.x, _startCell.y);
    /// <summary>F124: the active floor footprint shape built this run.</summary>
    public FloorProfile.FloorShape ActiveShape => _profile != null ? _profile.Shape : FloorProfile.FloorShape.Rectangle;
    /// <summary>F124: non-void playable cells in the current maze.</summary>
    public int PlayableCellCount => _playableCellCount;
    /// <summary>F124: true if cell (x, z) is masked out as part of a non-rectangular void footprint.</summary>
    public bool IsVoidCell(int x, int z) => IsVoid(x, z);

    private float FloorTop => origin.y + 0.02f;

    /// <summary>F120: this floor's wall height (m), for the death replay camera.</summary>
    public float WallHeight => wallHeight;

    /// <summary>F120: world Y of the floor top.</summary>
    public float FloorTopY => FloorTop;

    /// <summary>F103 A6: this floor's Stalker (floors 4-5), or null. DreadDirector refuses a scripted torch failure while it is close.</summary>
    public Stalker Stalker => _stalker;

    /// <summary>F105 C8: the InteractableKit's Stalker template (the player's own robot body, in the Stalker's dark skin), or null. PhantomDirector builds the double's figure from its visuals.</summary>
    public Stalker StalkerTemplate => interactableKit != null ? interactableKit.Stalker : null;

    private void Awake()
    {
        GameAudio.LogResolution = logAudioResolution;
        GameAudio.Assign(audioLibrary); // F97: before anything plays, so every call site sees the recorded clips
        GameSettings.BindMixer(gameMixer); // S2: before any component creates an AudioSource, so music / effects are routed from the first sample

        if (width < 2 || height < 2)
        {
            Debug.LogError("MazeGenerator needs at least a 2x2 grid.", this);
            return;
        }

        // A fresh start is one the title screen will front; a reload from the shop door or a retry
        // arrives with SkipMenuOnLoad already set. Read before the debug toggle below sets it too.
        bool freshStart = !GameFlow.SkipMenuOnLoad;

        // Debug affordance: skip the title screen so a shop iteration does not cost a full floor.
        if (debugStartInShop) GameFlow.SkipMenuOnLoad = true;
        // Same for the hub: MainMenu.Start takes the ArriveInHub path (the one NextFloor uses) and AsylumHub.EnterFromMenu reads
        // GameFlow.CurrentFloor, which ApplyFloorProfile below sets from debugFloor on a fresh start.
        // The finale goes the same way: AsylumHub.EnterFromMenu sends a HubState.IsFinale arrival to the Director's desk.
        if ((debugStartInHub || debugStartInFinale) && !debugStartInShop && freshStart)
        {
            GameFlow.SkipMenuOnLoad = true;
            GameFlow.ArriveInHub = true;
            if (debugStartInFinale)
            {
                HubState.IsFinale = true;
                if (debugFinaleAllFiles)
                    for (int i = 0; i < PatientFiles.MazeFileCount; i++) LoreArchive.MarkRead(PatientFiles.FirstId + i);
            }
        }
        // And straight into the maze: the reload path Retry / Next Floor use (MainMenu.Start -> StartGame -> BeginRun), with
        // ArriveInHub left off so the hub is not entered; ApplyFloorProfile below still picks the floor from debugFloor.
        else if (debugStartInMaze && !debugStartInShop && freshStart)
        {
            GameFlow.SkipMenuOnLoad = true;
        }

        // Moved ahead of ApplyFloorProfile (F74) so RunRules.Resolve, called from inside it, can hash
        // the seed that is actually used rather than the Inspector's raw seed field - nothing between
        // here and the old call site reads seed/PendingSeed, so this is a pure reorder.
        int usedSeed = GameFlow.PendingSeed != 0 ? GameFlow.PendingSeed
            : seed != 0 ? seed : System.Environment.TickCount;
        GameFlow.PendingSeed = 0;
        UsedSeed = usedSeed;

        // F107 B3: the Inspector entry into a descent at depth debugFloor (fresh starts only, so a reload keeps the run going).
        if (debugDescent && debugStartInMaze && !debugStartInShop && freshStart && !GameFlow.IsDescent) BeginDebugDescent();

        // F122: the campaign rules. A CLONE of the serialized terror settings when an assist / Oath / card changes any (the same reference otherwise);
        // before ApplyFloorProfile so its narrowBeam read, and every ApplyTerror downstream, see the clone. Also derives HubBreach.Mode / HunterGrab.DisabledByRules.
        terror = CampaignRules.ApplyTerror(terror);
        CampaignRules.ApplyRunSwitches();

        ApplyFloorProfile(freshStart, usedSeed);
        ResolveTheme();

        ResolveDressing();
        ResolveEntitySet(usedSeed); // F107 D1: before the dressing passes record their living-body candidates

        // Saved scenes carry their own cellSize, so the width rule is enforced here rather than trusted
        float effectiveMinCorridor = (_profile != null && _profile.Morphology == FloorProfile.MazeMorphology.CompactWarren)
            ? Mathf.Min(minCorridorWidth, 3.5f)
            : minCorridorWidth;
        float minCellSize = effectiveMinCorridor + wallThickness;
        if (cellSize < minCellSize)
        {
            Debug.LogWarning($"MazeGenerator: cellSize {cellSize} gives halls {cellSize - wallThickness:0.0} m wide; raised to {minCellSize} for {effectiveMinCorridor} m halls.", this);
            cellSize = minCellSize;
        }

        Debug.Log($"MazeGenerator: building a {width}x{height} maze with seed {usedSeed}.", this);

        // A new attempt at the current floor. Must run before SpawnShards reads the per-floor cap.
        PlayerWallet.BeginAttempt();
        if (debugShards != 0) PlayerWallet.DebugDeposit(debugShards);

        Generate(usedSeed);
        ChooseBaitDeadEnds();
        CacheCellCenters();
        ChooseLockerCells(new System.Random(usedSeed + 3));
        BuildGeometry();
        BuildPillars();
        BuildLockers();

        // F70, pre-bake: set pieces reserve their cell (and record their wall in _propWalls) before wall
        // props run, so BuildWallProps/BuildCeilingProps skip them. +8 is a new offset, never read by
        // anything that existed before this feature.
        BuildSetPieces(new System.Random(usedSeed + 8));
        BuildSqueeze(); // F105 C3: after the set pieces' reset of _setPieceCells, which it adds its cells to; before the bake, so the inserts carve it
        ReserveLoopEntrance(); // F105 C4: after BuildSetPieces for the same reason
        ReserveGates(); // F111: the gates' cells likewise
        BuildMorgueDrawers(new System.Random(usedSeed + NightmareSalt)); // F123 floor 3: the drawer banks reserve their WALL here, before the props pick walls and before the bake carves them

        // +4 is the RNG offset props own (+0 carve, +1 stars, +2 lamps, +3 lockers, +5 shards). One
        // instance is threaded through both prop passes below so the stream stays continuous across the
        // navmesh bake between them - same seed and floor always puts props on the same walls.
        System.Random propRng = new System.Random(usedSeed + 4);
        BuildWallProps(propRng);

        // Bake before anything else is placed inside the volume: the surface collects render meshes on
        // every layer, so a star, a ceiling or a robot standing in the maze would be carved out of the
        // navmesh. Lockers and wall props are built above, before this line, so their bodies are carved
        // out too and the hunter paths around them instead of through them.
        System.Diagnostics.Stopwatch bakeWatch = GameFlow.IsDescent ? System.Diagnostics.Stopwatch.StartNew() : null; // F107: a descent grid can be 16 x 13, so its bake time is logged
        BuildRuntimeNavMesh();
        if (bakeWatch != null) Debug.Log($"[Descent] navmesh bake {bakeWatch.ElapsedMilliseconds} ms for {width}x{height}", this);

        BuildCeiling();
        BuildCeilingProps(propRng);
        BuildWallLamps();
        BuildLoopCorridor(); // F105 C4: post-bake and outside the volume, so the hunter's NavMesh never reaches it
        BuildExitGates(); // F111: post-bake too, outside the volume

        // F70, post-bake: flat, collider-less dressing - safe to add after the navmesh has already
        // been carved from the bodies built above.
        BuildFloorProps(new System.Random(usedSeed + 6));
        BuildDecals(new System.Random(usedSeed + 7));
        BuildDust();
        BuildFog(); // no rng draw: a chunk grid, the particle system randomises itself
        BuildVoidAtmosphere(); // F125 D3: abyssal shaft walls, pit floor, mist and uplight for internal void regions

        DisableExistingPickups();
        SpawnStars(usedSeed);
        ChooseLureStar(); // F105 C5: no rng draw, so nothing after it moves

        // F72, post-bake: doors/buttons/vaults, after the stars exist (security placement tests that
        // blocking a passage actually cuts a star off) and before the shards (a vault's forced shards
        // are subtracted from the normal pool, not added on top). +11 is a new offset (+15 for vault
        // loot); it never touches the existing carve/star/lamp/locker/prop/shard streams (+0..+10).
        BuildDoors(new System.Random(usedSeed + 11));

        // F73, post-bake, after BuildDoors: Blackout-floor fuse boxes and their lamp sectors. +12 is a
        // new offset, never read by anything that existed before this feature.
        BuildFuses(new System.Random(usedSeed + 12));

        // F77, post-bake, after BuildFuses (fuse walls reserved) and before +13 (so throwables/notes see
        // key walls). +18: +17 is the exit gates' roll (MazeGenerator.Gates).
        BuildKeys(new System.Random(usedSeed + 18));

        // F107 C, after BuildKeys: a DESCENT's item caches (+24). Draws nothing outside a descent.
        BuildItemCaches(new System.Random(usedSeed + ItemCacheSalt));

        // F74, post-bake, after BuildFuses so every free-wall/free-cell pass below sees _doorUsedCells
        // fully populated. Decision D15: +13 threaded through both throwable passes (one continuous
        // stream, same convention as propRng above), +14 threaded through notes and theme interactables,
        // +15 for the lantern (shares its numeric offset with BuildDoors' vault-loot stream, but as a
        // separate System.Random instance - see the spec's decision D15).
        System.Random throwRng = new System.Random(usedSeed + 13);
        BuildThrowables(throwRng);
        BuildNoisySurfaces(throwRng);

        System.Random noteRng = new System.Random(usedSeed + 14);
        if (!GameFlow.IsDescent) BuildLoreNotes(noteRng); // F107: a descent has no lore notes
        BuildThemeInteractables(noteRng);

        BuildLantern(new System.Random(usedSeed + 15));

        // F93, post-BuildLoreNotes: the floor's patient case file (+19, its own System.Random; +17 is the exit gates' roll, +18 BuildKeys).
        if (!GameFlow.IsDescent) BuildPatientFile(new System.Random(usedSeed + 19)); // F107: nor a case file

        // F123, post-BuildPatientFile (lamps, doors, fuses, keys and notes are final): floor 4's ampoules or floor 5's house rules (+27, the same stream as floor 3's drawers).
        BuildNightmarePostBake(usedSeed);

        SpawnShards(usedSeed);
        BuildShiftingPassages(usedSeed); // F127 D5 (+29): linked open/closed shutter pairs on loop passages
        PlacePlayer();
        PlaceAI();

        // F75, post-PlaceAI: the second creature (+16), whenever the entity set (EntityRoster) has the Stalker.
        BuildStalker(new System.Random(usedSeed + 16));

        // F107 D, post-BuildStalker: the Listener, the Tenant and the living bodies (+25). Draws nothing for a kind that is not in the level's set.
        BuildEntities(new System.Random(usedSeed + EntitySalt));

        SetUpAtmosphere();

        if (staticBatchThemedGeometry && _theme != null) CombineStaticGroups();
        // Whatever the artist sees in the gallery is exactly what was cloned, including unapplied
        // overrides - so once cloning is done the gallery itself has no further reason to be visible.
        // Also hidden when the fallback ran: an incomplete gallery must not leave five rows of point
        // lights and locker triggers live in the scene during a run.
        if (themeSet != null) themeSet.gameObject.SetActive(false);
        // F72: same reasoning - the InteractableKit gallery has nothing left to show once its templates
        // are cloned, and an incomplete InteractableKit must not leave a live MazeDoor/WallButton sitting in the scene.
        if (interactableKit != null) interactableKit.gameObject.SetActive(false);
    }

    /// <summary>
    /// Picks the FloorThemes gallery row this maze clones, or leaves _theme null to fall back to the
    /// primitive geometry the game shipped with before theming existed. The Editor cannot run the
    /// KEEP IT LIT &gt; Build &gt; Floor Themes menu item on this implementer's behalf (it does not hold the
    /// project lock), so until the user runs it and saves the scene, every maze uses this fallback.
    /// </summary>
    private void ResolveTheme()
    {
        _theme = null;
        if (_profile == null) return; // Inspector mode (useFloorProfile off): primitives, as before

        if (themeSet == null) themeSet = FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        if (themeSet == null)
        {
            Debug.LogError("MazeGenerator: no FloorThemes gallery in the scene, building the plain maze. Run KEEP IT LIT > Build > Floor Themes in the Editor, then save the scene.", this);
            return;
        }

        FloorTheme theme = themeSet.ForFloor(_profile.ThemeIndex);
        if (theme == null || !theme.IsComplete)
        {
            Debug.LogError($"MazeGenerator: FloorThemes has no complete theme for floor {_profile.ThemeIndex}, building the plain maze.", themeSet);
            return;
        }

        _theme = theme;
        Debug.Log($"MazeGenerator: theme '{theme.DisplayName}'.", this);
    }

    /// <summary>
    /// F70 decision 6: one dressing source. Called right after ResolveTheme. Themed floor -> _theme's fields.
    /// Anything else (primitive fallback) -> every field stays at its C# default (null/0), so
    /// BuildWallProps and friends' empty/zero checks make every new pass a no-op -
    /// the graceful-degradation rule in decision 14.
    /// </summary>
    private void ResolveDressing()
    {
        if (_theme != null)
        {
            _dressing = new MazeDressing
            {
                Props = _theme.Props,
                SetPieces = _theme.SetPieces,
                Dust = _theme.Dust,
                WallPropChance = DenserProps(_theme.WallPropChance),
                CeilingPropChance = DenserProps(_theme.CeilingPropChance),
                FloorPropChance = DenserProps(_theme.FloorPropChance),
                WallDecalChance = _theme.WallDecalChance,
                FloorDecalChance = _theme.FloorDecalChance,
                MaxDecalsPerCell = _theme.MaxDecalsPerCell,
                SetPieceCount = _theme.SetPieceCount > 0 ? _theme.SetPieceCount + setPieceBonus : 0
            };
        }
        else
        {
            _dressing = default;
        }
    }

    /// <summary>F85: a theme's prop chance scaled by propDensity, capped at 0.85 but never lowered below its own value.</summary>
    private float DenserProps(float chance)
    {
        return Mathf.Max(chance, Mathf.Min(0.85f, chance * propDensity));
    }

    /// <summary>F85: look-alike models under a prop (hanging bodies, shrouds, sockets, vents) picked by a hash of the cell - no rng draw, same trick as the set pieces.</summary>
    private static void ApplyModelVariants(Component clone, int x, int z, int salt)
    {
        if (clone.TryGetComponent(out ModelVariants models)) models.Apply((x * 73856093) ^ (z * 19349663) ^ salt);
    }

    /// <summary>
    /// F104 B9/B10: remembers a placed prop or set piece so WorldShiftDirector can change it later. Set pieces are always recorded (B10 copies
    /// them); a prop only when it has more than one look-alike model to switch between. Draws nothing from any rng.
    /// </summary>
    private void RecordShiftable(Component clone, Vector2Int cell, Vector3 wallDir, bool isSetPiece)
    {
        clone.TryGetComponent(out ModelVariants variants);
        if (!isSetPiece && (variants == null || variants.Count < 2)) return;

        _shiftables.Add(new ShiftablePlacement(clone.transform, variants, cell, wallDir, isSetPiece));
    }

    /// <summary>
    /// Picks this run's floor and overwrites the world-size and lamp fields with its values. The
    /// hunter, light and timers are handed their values later, where each is wired up. Runs first in
    /// Awake, before anything reads width, height, starCount, lockerCount or the lamp settings.
    /// </summary>
    private void ApplyFloorProfile(bool freshStart, int usedSeed)
    {
        _profile = null;
        CampaignRules.BuiltSignature = CampaignRules.Signature(); // F122: what this scene was built with (the new-campaign screen compares)

        // F107 B3: a descent level keeps its story-floor lookups in range by pinning CurrentFloor to min(depth, FinalFloor), and lives far from
        // every authored piece. Set before RunRules.Resolve so a level's rule hashes the real depth.
        bool descent = GameFlow.IsDescent;
        if (descent) EnterDescentLevel(usedSeed);

        // F74 decision D9: resolved before the useFloorProfile-off early return, so RunRules.Current is
        // never left stale (it is a pure function of usedSeed/floor, always safe to recompute even on
        // an Inspector-only test maze where nothing below reads it).
        RunRules.Resolve(usedSeed, descent ? DescentRun.Depth : GameFlow.CurrentFloor);

        if (!useFloorProfile) return;

        // A debug floor picks the floor a fresh start begins on, and is written back so the HUD, end
        // screens and Next Floor all agree with it. Only on a fresh start: a reload that came from the
        // shop door or a retry (SkipMenuOnLoad) must keep the floor the game set, or the shop's door
        // would lead straight back to the same floor forever.
        if (debugFloor > 0 && freshStart && !descent) GameFlow.CurrentFloor = Mathf.Clamp(debugFloor, 1, FloorProfile.FinalFloor);

        // F77: debugObjectives (Inspector) forces this floor's mechanics instead of rolling them from the
        // seed; None means "roll normally" (null forced, not Objective.None - that would mean "force no
        // mechanics", see FloorProfile.For's doc comment).
        FloorProfile.Objective? forcedObjectives = debugObjectives != FloorProfile.Objective.None ? debugObjectives : (FloorProfile.Objective?)null;
        _profile = descent
            ? FloorProfile.ForDescent(DescentRun.Depth, usedSeed, forcedObjectives)
            : FloorProfile.For(GameFlow.CurrentFloor, usedSeed, forcedObjectives);
        PlayerInventory.ApplyActiveModifiers(_profile);
        // F74: run rules (DyingGrid, KeenEars, HeavyFog, WeakBattery, Restless, GlassFloors, RichVeins)
        // layer on top of the shop's own modifiers - see RunRules.ApplyToProfile's doc comment for which
        // fields it touches and why that is enough (AIFollower.ApplyDifficulty and SpawnShards both read
        // the mutated profile later in this same Awake).
        RunRules.ApplyToProfile(_profile);
        CampaignRules.ApplyToProfile(_profile); // F122: preset, assists, Oaths (a no-op for plain STANDARD and in a descent)
        // F105 C9: runs before SetUpAtmosphere, so the flag is read here directly rather than through ApplyTerror.
        if (terror.narrowBeam) _profile.FogDensity *= narrowBeamFogScale;

        if (Application.isEditor && debugFloorShape != DebugFloorShape.Auto)
        {
            _profile.Shape = (FloorProfile.FloorShape)debugFloorShape;
            if (_profile.ShapeVariant == 0) _profile.ShapeVariant = 1 + ((RunRules.HashSeedFloor(usedSeed, 7919) & 0x7FFFFFFF) % 3);
            if (_profile.Shape != FloorProfile.FloorShape.Rectangle)
            {
                _profile.Width = Mathf.Max(_profile.Width, _profile.Shape == FloorProfile.FloorShape.Cross ? 10 : 9);
                _profile.Height = Mathf.Max(_profile.Height, _profile.Shape == FloorProfile.FloorShape.Cross ? 9 : 8);
            }
        }

        ResolveAndApplyMorphology(usedSeed); // F127 D1/D2: roll & apply spatial morphology on Floor 2+ / DESCENT Level 2+

        width = _profile.Width;
        height = _profile.Height;
        cellSize = _profile.CellSize;
        wallHeight = _profile.WallHeight;
        starCount = _profile.StarCount;
        lockerCount = _profile.LockerCount;
        cellsPerLamp = _profile.CellsPerLamp;
        lampIntensity = _profile.LampIntensity * 2.6f;
        lampRange = _profile.LampRange * 1.65f;

        // F40 slice A decision 5: lamps scale with wall height so a low ceiling (floor 3) doesn't hide
        // its lamps in the torch beam and a tall one (floor 4) doesn't leave them looking stranded low.
        // The serialized lampHeight (2.6) is read here as the 4 m baseline; ApplyFloorProfile runs once
        // per scene load and only writes this field here, so there is no compounding across floors.
        lampHeight = Mathf.Clamp(lampHeight * _profile.WallHeight / 4f, 2.3f, 3.4f);

        string modifiers = PlayerInventory.ActiveModifiers.Count > 0
            ? string.Join(", ", PlayerInventory.ActiveModifiers)
            : "none";
        Debug.Log($"MazeGenerator: {_profile}, modifiers: {modifiers}", this);
        if (!CampaignRules.IsPlain) Debug.Log($"MazeGenerator: campaign rules {CampaignRules.Signature()} (preset {CampaignRules.Preset}, assists {CampaignRules.Assists}, oaths {CampaignRules.Oaths}, kit {CampaignRules.Kit}), escape {_profile.EscapeSeconds:0.#} s / gates {_profile.GateEscapeSeconds:0.#} s, forced rules {RunRules.Current}/{CampaignRules.ForcedRuleMask}", this);
    }

    private void OnDestroy()
    {
        // Runtime material copies are not collected with the scene in the editor
        foreach (Material material in _runtimeMaterials)
        {
            if (material != null) Destroy(material);
        }
        _runtimeMaterials.Clear();

        // F73: LightPool is scene-scoped, not campaign-scoped (CLAUDE.md's static-reset rule) - cleared
        // here as well as by its own SubsystemRegistration reset, so a maze rebuild never carries stale
        // entries over from the floor just left.
        LightPool.Clear();
    }
}
