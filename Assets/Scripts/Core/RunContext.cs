using System.Collections.Generic;
using StarterAssets;
using UnityEngine;

/// <summary>
/// Read-only composition bundle assembled by <see cref="MazeGenerator.SetUpAtmosphere"/>
/// and handed to directors so new run systems don't widen 12-parameter <c>Configure</c>
/// signatures across the codebase.
/// </summary>
public sealed class RunContext
{
    // World & floor identity
    public MazeGenerator Maze { get; set; }
    public FloorProfile Profile { get; set; }
    public FloorTheme Theme { get; set; }
    public TerrorSettings Terror { get; set; }
    public int Seed { get; set; }

    // Player & rig
    public Transform Player { get; set; }
    public Transform Camera { get; set; }
    public FirstPersonRig Rig { get; set; }
    public Flashlight Torch { get; set; }
    public PlayerStamina Stamina { get; set; }
    public PlayerStealthState Stealth { get; set; }
    public ThirdPersonController Controller { get; set; }
    public PlayerInteractor Interactor { get; set; }
    public ThrowController ThrowController { get; set; }
    public KeyRing KeyRing { get; set; }
    public ConsumableController Consumables { get; set; }

    // Entities & world collections
    public AIFollower Hunter { get; set; }
    public AIPresence Presence { get; set; }
    public IReadOnlyList<WallLamp> Lamps { get; set; }
    public IReadOnlyList<Locker> Lockers { get; set; }
    public IReadOnlyList<MazeDoor> Doors { get; set; }

    // Presentation & UI
    public PlayerHud Hud { get; set; }
    public HorrorAtmosphere Atmosphere { get; set; }
    public HorrorAudioDirector Audio { get; set; }
    public MazeAmbience Ambience { get; set; }
    public FloorMapUi FloorMap { get; set; }
    public LoreReadingUi LoreReader { get; set; }
    public ShopMenu ShopMenu { get; set; }
    public MainMenu Menu { get; set; }
    public PauseMenu Pause { get; set; }

    // Directors & run state
    public TensionDirector Tension { get; set; }
    public DreadDirector Dread { get; set; }
    public PhantomDirector Phantoms { get; set; }
    public DreadMeter DreadMeter { get; set; }
    public WorldShiftDirector WorldShift { get; set; }
    public HabitTracker Habits { get; set; }
    public HunterGrab Grab { get; set; }
    public DeathReplayRecorder Replay { get; set; }
    public MazeEscape Escape { get; set; }
    public GameOutcome Outcome { get; set; }
    public ContractTracker Contracts { get; set; }
    public PlayerVoice Voice { get; set; }
    public FloorGradeTracker GradeTracker { get; set; }

    // Authored rooms
    public ShopRoom Shop { get; set; }
    public IntakeRoom Intake { get; set; }
    public AsylumHub Asylum { get; set; }
}
