# GEMINI.md

This file provides project and global guidance to Gemini / Antigravity when working in this repository (migrated from `CLAUDE.md`, global `~/.claude/CLAUDE.md`, `~/.claude/settings.json`, and Claude project memories).

## Global & Workflow Rules

### 1. Unity: Use the Unity CLI, Never MCP
When working with Unity projects, always drive the Editor through the **Unity CLI** (`unity` command, with the project's `com.unity.pipeline` package), never through an MCP server — Unity deprecated the in-Editor MCP server of the AI Assistant package (`com.unity.ai.assistant`) in favour of the CLI (`https://docs.unity.com/en-us/unity-cli/replace-mcp-server-unity-cli`). Do not call `mcp__unity-mcp__*` tools even if available.
- **Check connection first:** `unity status` (state `ready`), `unity pipeline list` (Safe Mode shows here).
- **Run C#:** `unity command eval --code '<statement block>'` or `unity command eval_file --file <path>`; recompile with `unity recompile` (or see the `unity-pipeline` and `unity-compile-check` skills). The code is a statement block, not a file: no `using` directives, fully qualify every type (`UnityEngine.Object`, `UnityEditor.AssetDatabase`).
- **Long-running work** (mass reimports, bakes, builds): add `--detach` and follow with `unity job status|wait <id>`, or raise `--timeout`. The Editor's main thread must pick up a command within ~5 s, so a busy Editor can report a failure while the work still runs — check `Logs/Editor.log` / state before retrying.
- **Output & caller flags:** Use `--format json` when parsing output, and `--caller plugin --skill <skill>` when a skill asked for the call.
- **Play-mode frame advancement:** Bring the Unity window to the foreground (or run `unity command set_autotick --enable true`) when testing in Play mode, or frames do not advance.

### 2. Planning, Subagents, Commits & Shared Assets
- **Plans go in `plannings/`:** Always save planning `.md` files to `plannings/<topic>-plan.md` at the project root (outside `Assets/`, so Unity never imports them and no `.meta` files are generated).
- **Plan first, then implement via subagent for feature tiers:** When implementing a feature tier, first write a detailed implementation plan in `plannings/tier<N>-<topic>-plan.md` (verified facts with line numbers, numbered Decisions, architecture table, per-feature code, composition-root wiring, implementation order, test checklist), then delegate implementation to a subagent briefed with the plan path, `GEMINI.md`, and the `unity-compile-check` skill, and review/compile-check the diff afterwards.
- **No `Co-Authored-By` trailer:** Never add `Co-Authored-By` or AI attribution trailers to git commit messages or PR descriptions.
- **Author inspectable static world content in the scene, not in runtime code:** For static world content the user wants to inspect in Unity, generate real GameObjects in the scene via an `Assets/Editor` menu item (`KEEP IT LIT > Build > ...`) with serialized references on the runtime component rather than spawning primitives in `Awake`.
- **Hand-editable rooms are prefabs:** Authored rooms the user tweaks by hand (`Assets/Prefabs/IntakeRoom.prefab`, `Assets/Prefabs/ShopRoom.prefab` + `ShopSalesman.prefab`, `Assets/Prefabs/AsylumHub.prefab`) have the prefab as their source of truth. The builder only generates a first draft (`BuildSilently()` only places the prefab; regeneration is a confirmed reset; `Check Project` audits the hand-edited prefab). Edit the prefab rather than regenerating.
- **Verify room lighting in Play mode, not Edit mode:** Edit-mode camera renders are lit by the Editor's skybox ambient, hiding inverted triangle winding or wrong normals (Unity front faces wind clockwise seen from outside). Always verify mesh/material builder changes in Play mode (`~0.012` ambient + point lights/torch; see the `unity-smoke-test` skill).
- **Shared Mixamo clip library:** Lives outside the project at `D:\Unity\essentials\Assets\Mixamo`. When a feature needs a clip, **copy** (never move) it into `Assets/SourceFiles/Animation/Mixamo/` and set its rig to Humanoid.
- **3D Character & Prop Generation (`neural-3d-asset-pipeline` skill):** Always generate 3D assets 100% locally (never paid external 3D APIs) using local `Hunyuan3D-2mv` / `TripoSG` / `TRELLIS` + Blender 5.2 Cycles GPU. For humanoid characters seen up close, NEVER rely on a single full-body neural mesh: always use a **2-pass reconstruction** (Pass A: full-body `<Character>_Body` at `4096×4096`; Pass B: dedicated full-frame `1K` close-up orthographic head turnaround $\rightarrow$ `25K–28K`-triangle `<Character>_Head` sub-mesh at `2048×2048`) plus separate 3D sub-meshes (`<Character>_Apron`, `<Character>_Items`, `<Character>_Shards`) with per-submesh URP materials.

---

## Project

**KEEP IT LIT** (renamed from LIGHTS OUT on 11 Oct 2026; "LIGHTS OUT" survives only in-world, as the name of Ending B and a hunter line) — a first-person horror maze game. Unity 6 (editor `6000.6.2f1`), URP. It started life as the "Unity Essentials" / Get Started tutorial (a third-person robot collecting stars); the tutorial content has been removed, but the robot and Starter Assets controller are still the player. You are dropped into a dark procedural maze with a flashlight, collect 5 stars while something hunts you, and then have 60 seconds (or 120 seconds on exit-gate floors) to escape.

Git repository (branch `main`). There are no assembly definitions under `Assets/`, so all runtime game code compiles into `Assembly-CSharp` and Editor code into `Assembly-CSharp-Editor`.

## Commands

Development happens in the Unity Editor (Rider / Visual Studio / VS Code attach via `.vscode/launch.json`). The generated `*.csproj`, `*.sln`, `Library/`, `Temp/`, `obj/` and `Logs/` are Unity-generated — do not edit them.

- **Open Editor CLI:** Project has `com.unity.pipeline` (0.7.0-exp.1) and `com.unity.ai.assistant` 2.19 (≥ 2.13, no CLI conflict). Fastest compile check against a running Editor is `unity recompile` or `grep "error CS" Logs/Editor.log` (see `unity-compile-check` skill for offline Roslyn `csc.dll` compilation when `Temp/UnityLockfile` is held).
- **Batch-mode compile check / build (Editor closed):**
  ```
  "C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe" -batchmode -quit -projectPath "D:\Unity\essentials\keep_it_lit" -logFile -
  ```
- **Build settings:** `Assets/Scenes/Boot.unity` (scene 0, waits for localization tables) then `Assets/Scenes/GetStarted_Scene.unity`; Play from `GetStarted_Scene` in the Editor.
- **Tests (F118):** EditMode tests live in `Assets/Editor/Tests/` (`Assembly-CSharp-Editor`, namespace `KeepItLit.Tests`; categories `Fast` = `SaveDataTests`, pure/instant, and `Maze` = `MazeReachabilityTests`, which enters Play and loads 40 story mazes + 15 DESCENT levels, checking every star/key/fuse/button/gate with grid + NavMesh reachability legs of `MazeGenerator.CheckReachability`).
  - **Editor open:** **KEEP IT LIT > Debug > Run Tests (Fast) / (All)** or `unity command eval --code 'KeepItLit.Tests.TestMenu.RunAll();'` and read `Logs/TestResults.txt` (requires a CLEAN open scene; `unity test` refuses while an Editor holds the project).
  - **Dirty scene manual driver:** `unity command eval --code 'KeepItLit.Tests.MazeTestDriver.StartManual(false);'` (story) / `(true)` (DESCENT) runs the maze loop on an Editor update pump and writes `Logs/MazeTest-story.txt` / `-descent.txt`.
  - **Batch mode (Editor closed):** `Unity.exe -batchmode -projectPath <project> -runTests -testPlatform EditMode -testCategory "Fast;Maze" -testResults Logs\TestResults.xml -logFile -` (no `-quit`, no `-nographics`); fallback: `-executeMethod KeepItLit.Tests.TestMenu.RunAllFromCommandLine`. Trim the matrix in `MazeTestMatrix` for a quick run.
- **Benchmark (F118):** `keep_it_lit.exe -benchmark -benchmark-quit -benchmark-seconds 10 -benchmark-out C:\temp\bench.csv -logFile bench.log` (also `-benchmark-presets low,medium,high`, `-benchmark-stations maze1,maze5,hub,descent12`) walks a fixed route per station × preset and writes avg / 1 % low frame time rows; in Editor: **KEEP IT LIT > Debug > Run Benchmark (Play)** (`PerfBenchmark.cs`).

## The One Thing to Understand First

**Almost nothing is authored in the scene.** `MazeGenerator.Awake` (`[DefaultExecutionOrder(-100)]`) builds the whole game at runtime: walls, floor, ceiling, the NavMesh bake, stars, the first-person rig, the flashlight and every director component (each `AddComponent`-ed onto the `MazeGenerator` GameObject and wired with `Configure`/`Bind`). The authored exceptions are galleries the maze clones from and hand-editable rooms, all built by **KEEP IT LIT > Build > ...** menu items:
- **Hand-editable prefabs** (edit in Prefab Mode; builders only *place* them; "regenerate" menu items are confirmed resets): `Assets/Prefabs/ShopRoom.prefab` (+ nested `ShopSalesman.prefab`), `IntakeRoom.prefab`, `AsylumHub.prefab`.
- **Galleries the maze clones** (scene objects): `InteractableKit` (every interactable/entity template: doors, buttons, fuses, keys, throwables, notes, the Stalker, the Listener, item caches...) and `FloorThemes` (one `FloorTheme` row per floor). A missing slot logs an error naming its menu item and skips that feature.

Full notes on these rooms and galleries: `docs/systems.md` (first section).

**Do not look for Inspector wiring or prefabs to explain runtime procedural behaviour, and do not add procedural setup to the scene.** Read `MazeGenerator.SetUpAtmosphere` (near the end of the file) — that method is the composition root. New systems get created and configured there.

Execution order matters and is set explicitly:

| Order | Component | Why |
|-------|-----------|-----|
| −100 | `MazeGenerator` | builds everything before anything else's `Awake` |
| −90 | `FirstPersonRig` | camera/flashlight exist before directors look for them |
| −85 | `TensionDirector` | its counter is created before the title screen, so it draws *underneath* it |
| −84 | `PlayerHud` | builds the resource HUD before `TouchControls` sits above it |
| −83 | `TouchControls` (F76) | above the HUD, below the (lazily-built) menu panels; drives `TouchInput.Poll()` every frame |
| −80 | `MainMenu` | freezes the clock (`Time.timeScale = 0`) until the player presses Start |
| −50 | `HorrorAtmosphere` | ambient/fog/skybox blackout after the world exists |

## Run Lifecycle

0. **F122:** START GAME first opens the NEW ADMISSION screen (`NewCampaignScreen`: preset, assists, kit, Oaths); BEGIN runs `CampaignRules.Begin`, and a choice that changes how floor 1 is built reloads the scene once (`GameFlow.BeginCampaignOnLoad`), plain STANDARD does not. Then:
   **START GAME (F86)** opens the **Intake** (`IntakeRoom`, a short authored tutorial room) instead of the rules screen: `MainMenu.BeginNewCampaign` → `CloseMenus()` → `IntakeRoom.Begin()`, `GameFlow.IsInIntake = true`, the run is NOT active (hunter held in `AIFollower.Update`, everything else gated on `IsRunActive`). Nine steps (look, move, torch, focus, battery, read, throw, hide, star + hatch), one goal at a time on a top-centre **objective card** (`ObjectiveCard.cs`, shared with the hub's `HubObjectiveCard`) (OBJECTIVE label + `n / 9`, goal in caps, key-cap chips via `TouchInput.Key`, tick then fade) plus a bobbing amber **goal arrow** over the target and a pulsing glow on pickups (`IntakeRoom.goalArrow`/`goalGlow`, authored by the builder; one shared light). Walking into the hatch or clicking SKIP TUTORIAL ends it: blackout → maze start cell → `IsInIntake = false` → `GameFlow.BeginRun()`. **F92:** with a usable Asylum hub, the Intake hands over to it instead (arrive at main entrance, walk to open wing door, press E, then blackout → maze start → `BeginRun`); the fallback shop room's door and CONTINUE from a floor-start save likewise reopen each new floor in the hub (`GameFlow.ArriveInHub`). Retry and Restart skip it. CONTINUE, Retry and NEXT FLOOR go through `SkipMenuOnLoad` → `StartGame`. With no `IntakeRoom` built, START GAME falls back to the old rules screen (also reachable from title CONTROLS button).
1. `MainMenu` opens with `Time.timeScale = 0`; `StartGame` unfreezes and calls `GameFlow.BeginRun()` (sets `GameFlow.RunStartTime` and `GameFlow.IsRunActive = true`).
2. `Pickup.OnTriggerEnter` (Player tag) → static `Pickup.OnCoinCollected` → `GameManager` decrements and raises `GameManager.ProgressChanged(collected, total)`.
3. Last star → `GameManager.AllStarsCollected`. **If anything is subscribed, that listener takes over**; only with no listener does `GameManager` call `GameOutcome.FloorCleared` directly. `MazeEscape` and `TensionDirector` both subscribe.
4. `MazeEscape` opens a glowing hatch at least 12 m away, starts a 60 s countdown, and builds a landing platform underneath it. **F111:** on a gate floor (50 % seeded roll from floor 3: one gate; final floor and DESCENT boss: two) there is no hatch: exit gates built into the outer wall from the start of the floor get power, the countdown is 120 s, and the player holds E on a gate's lever (progress never goes down) then walks out (`MazeEscape.ExitThroughGate`, same `FloorCleared` path).
5. Ending, always through `GameOutcome` (single-shot: `IsOver` / `IsEnding` guard every entry point):
   - **Floors 1–4 hatch/gate:** `MazeEscape.DropThroughHatch()` → `GameOutcome.FloorCleared()`. **F94/F116:** with a hub that `CanVisitShop` (`AsylumHub.ShopInHub`), the ledger is shown over black, CONTINUE → `GameFlow.NextFloor()` → hub at the cleared wing's arrival point, and the shop is **THE COMMISSARY** (`HubShop`, ground-floor storeroom, open from floor 2 on; shut/padlocked on floor 1 and finale). Without a usable hub (or legacy shop saves), falls back to teleporting into `ShopRoom` (`GameFlow.IsInShop = true` until walking through the door → `GameFlow.NextFloor()`).
   - **Floor 5 hatch/gate:** (F93/F126) `GameOutcome.FinaleSequence`: back to the Director's office in the asylum, ending A (main doors), B (master switch, only with all case files), or C (sitting at the 6th chair of the Last Supper, `HubSupperSixthSeat`, only with all case files) through `GameOutcome.CompleteFinale`; without a usable hub, `GameOutcome.Win()`.
   - **Timer expires:** `GameOutcome.Lose(LoseReason.OutOfTime)`.
   - **Hunter catches you:** `AIFollower.PlayerCaught` → **F120:** first catch on a floor is a `GameOutcome.TryGrab` shove (torch knocked away, hunter stunned 3 s, limp 10 s; not on a locker bust) before any Second Wind; holding a Second Wind triggers `GameOutcome.SecondWindSequence` (cut to black, hunter warped away and stunned, player freed); otherwise `GameOutcome.Lose(LoseReason.Caught)` → `CaptureSequence` coroutine → end screen with CCTV death replay (`DeathReplayRecorder/View`).

## Systems

**Full notes for every system are in `docs/systems.md`** (one bullet per system, matching the headings below). Read the relevant bullet before changing a system and keep `docs/systems.md` updated:

- **World** — `MazeGenerator` (+ partials `.Loop/.Layout/.Lure/.Descent/.Entities/.Gates/.ShapeIdentity`): seeded maze, NavMesh bake, stars, dressing passes (`MazeDressing`), doors/vaults (F72), fuses and objectives (F73/F77 `FloorProfile.Objectives` flags), stealth tools/lore/run rules (F74), the composition root. RNG streams are `usedSeed + n`; **the next free offset is +29** (+28 = F125 shape avenues, +27 = F123 wing nightmares, +26 = F111 exit gates).
- **Player** — `FirstPersonRig` (F126: forensic lens FOV compression when focusing torch or hiding), `Flashlight` (wide/focused beam, recharge, `IsDead` not permanent, `IsDropped` F120), `ThirdPersonController` (`FirstPerson = true`), `PlayerStealthState`, `PlayerInteractor` (every `IInteractable`), `ThrowController`, `KeyRing`, `PlayerVoice`/`MicInput` (F120 opt-in microphone).
- **Hunter** — `AIFollower` (sight/hearing/search/ambush, capture needs sight), body built by Build > Hunter Body: **Sci-Fi Cyborg by default** (`HunterBodyBuilder.Cyborg.cs`, Build > Hunter Body Look switches to Orderly robot). **F120:** `HunterGrab`. **F126:** institutional tape-echo voice (`HunterPhraseBook`).
- **The Stalker (Stan)** — `Stalker.cs`: moves only unlit, never kills, drains the torch; `StalkerVoice`/`HunterVoice` speech bubbles.
- **Door breakers** — `DoorBreakers` registry; `MazeDoor` self-detects agents (no `NavMeshObstacle`).
- **Dread layer / escalation / relocation / phantoms** — `AIPresence`, `HorrorAudioDirector`, `HorrorAtmosphere` (F126: URP halation `Bloom` + `SplitToning`), `TensionDirector`, `DreadDirector`, `PhantomDirector`, `LightPool`.
- **The Intake (F86)** — `IntakeRoom` tutorial room (prefab), `IsInIntake` reader split.
- **The Asylum hub (F92–F101, F112–F117, F121, F126)** — `AsylumHub` (prefab) between floors: wing doors, the Commissary (`HubShop`), story layer (F93: files, patients, decay, set pieces, finale; F126: `RecordsBoard.TryCorroborate` scrubs highest `HunterMemory` habit once 2+ files read, playable finale epilogue beats in `HubFinale`/`HubExitDoor`), look (F95), scarier pass (F96), NPCs + dialogue + missions (F98, Timmy robots in custom skins), wayfinding (F100), props/vignettes (F101), room finds + scares (F114: `HubCache`, `HubRoomScare` + 7 subclasses, `CctvOnly` layer), Director's suite (F115: `AsylumHubBuilder.DirectorSuite*`), empty rooms (F117: `HubSupper` + F126 `HubSupperSixthSeat` Ending C `FinaleEnding.Seated`, `HubMeal`/`HubMealDish` with `HubState.MealFloor`, `HubLesson`), hub breach (F121: `HubBreach`, `HubBreachSetPiece`, `HubState.BreachDone`, `AsylumHubBuilder.Breach`).
- **World signs (F109)** — `WorldSignBuilder` (+ `AsylumHubBuilder.Signs.cs` room plates): real sign backings, lit text, six fonts in `Assets/SourceFiles/Fonts/`.
- **Maze terror (F103–F106)** — `TerrorSettings` switches: unreliable cues (Tier A), dread meter, held breath, sanctuary decay, shadow/peripheral phantoms, world shifts (Tier B), learning hunter `HunterMemory`/`HabitTracker` with lesson card, final floor, penumbra Stan, rooms/gallery/squeeze, loop corridor puzzle (`debugForceLoop`, Debug > Go To Loop Entrance).
- **Wing nightmares (F123)** — each story floor is its wing patient's nightmare (1 Hilde: late shutter door chalked 41; 2 Tomas: pipe knocks, ~1 in 5 lying unless HONEST CUES; 3 Elias: morgue drawer banks, drawer 5 holds his toe tag, one drawer is YOURS; 4 Weiss: blue ampoules that numb fear; 5 Director: posted house rules + PA that reports you), `WingNightmareDirector` + `WingNightmares`, never in DESCENT.
- **The floor map (F106/F110)** — `FloorMapUi` + `MazeMapData`: **only exists once a Floor Plan item is used** (held consumable, cap 1; hold M / View or pause MAP button), shows only walked cells for that floor (never stars, keys, fuses, hatch or hunter; loop reward reveals full layout).
- **DESCENT (F107, F124, F125)** — endless mode: `GameFlow.IsDescent`, `DescentRun`, `FloorProfile.ForDescent` (F124: monotonic playable cell scaling + non-repeating `FloorShape` void footprints from level 2+: `Ring`, `TwinWings`, `Cross`, `LChasm`; `_void` mask + `_startCell` in `MazeGenerator`; F125: `MazeGenerator.ShapeIdentity.cs` (+28) directional carve bias & avenues, barred iron-grille void windows + abyssal shaft mist/lighting, sector star & bridge door placement, zone ceiling heights & lamp tints, shape-aware hunter patrol in `AIFollower`), item caches (`DescentItem`/`DescentLoot`, +24; 40 % of levels hold a Floor Plan), entity roster (`EntityRoster`, +25: Listener, Tenant, living bodies). Testing: `debugStartInMaze` + `debugDescent` + `debugFloor` + `debugFloorShape` + `debugFullMap`.
- **Settings (S2)** — `GameSettings`, `SettingsMenu` (F118: `ReduceFlashing` + `FlashSafety`, pattern P2 in `docs/systems.md`); **audio routing rule**: music sources call `GameSettings.RouteMusic`, use `GameAudio.ClipAtPoint`.
- **Localization (S9)** — every player-visible string goes through `L10n.T("table.key")`; text lives in `Assets/Localization/Source/*.csv` (EN + es/fr/de), imported by **KEEP IT LIT > Localization > Import CSVs**.
- **Contracts (F87)** — `ContractState`, `ContractTracker`, shop contract board.
- **Economy / shop** — `ShopCatalogue` (12 cards; Floor Plan replaced retired HUD Tint), `PlayerWallet`, `PlayerInventory`, `ShopMenu`, `ConsumableController`, `LoreArchive`.
- **Flow / shell / saves** — `GameFlow`, `PlayerLock`, `MainMenu`, `PauseMenu`, `GameOutcome` (single-shot, `partial`), `RuntimeUi`, `SaveSystem` (whole floors only, one CONTINUE; F118: `persistentDataPath/save.json` + `.bak` on desktop, PlayerPrefs on WebGL, migrated by `SaveMigrations`, unreadable save set aside as `save.unreadable.json`). **F120:** `DeathReplayRecorder/View`.
- **Touch (F76)** — `TouchInput`, `TouchControls`, `MobilePerformance`.
- **Input (F119)** — `GameInput` (`KeepItLitControls` asset, `Pressed/Held/Released(GameAction.X)` OR'ed with `TouchInput`, `LastDevice`), `InputGlyphs`, `MenuFocus`, `RebindPanel` (Settings > CONTROLS > REMAP CONTROLS, saved as `GameSettings.InputOverrides`).
- **Difficulty and progression (F122)** — `CampaignRules` (presets / assists / Oaths; `UnreliableCues` is F123 flag), `FloorGrade`, `PatientRecord`, `NewCampaignScreen`, `PatientRecordMenu`; plain STANDARD is byte-identical (`StandardBaselineTests`).
- **Recorded audio (F97)** — `GameAudioLibrary` / `GameAudio`; generated sounds are fallbacks.

## Editor Menu (`KEEP IT LIT`)

| Path | What it is |
|------|-----------|
| **Check Project** | Read-only report: each piece OK / FIX, with the menu item that fixes it. Run this first when unsure (`Assets/Editor/ProjectSetup.cs`). |
| **Build Missing Pieces** | Runs only the builders whose piece is *missing*, in dependency order. Never rebuilds something that exists. |
| **Build > …** | One builder per scene piece (Floor Themes, Interactables, Phrase Books, Shop Room (regenerate), Intake Room (regenerate), Asylum Hub (regenerate), Hunter Body, Re-skin Templates > …). |
| **Debug > …** | Dev helpers: Delete Save, Print Maze Map (`MazeGenerator.DebugMap()` → clipboard & `Logs/MazeMap.txt`), Run Tests, Run Benchmark, Go To Loop Entrance. |

Tooling rules:
- **Builders:** Put under `KEEP IT LIT/Build/` (priority 100+), provide a dialog-free `BuildSilently()`, and **register as a `Piece` in `ProjectSetup.Pieces()`** with `Problem` and `Build` delegates in dependency order.
- **One-off migrations:** Prefer running automatically from dependent builders or flagging in `Check Project`. If placed under `KEEP IT LIT/Migrations/`, delete once applied to the scene and committed.
- **Hand-designed rooms (`IntakeRoom`, `ShopRoom`, `AsylumHub`):** Prefab is the source of truth; `BuildSilently()` only places it; edit the prefab directly.
- Never auto-save the scene from a builder; mark it dirty and tell the user to save (Ctrl+S). Keep menu paths in sync with log messages (`KEEP IT LIT > Build > X`).

## Essential Gotchas (Full details in `.agents/rules/gotchas-and-assets.md`)

- **Statics reset:** Every static campaign/run state (`GameFlow`, `GameOutcome.IsOver`, `PlayerWallet`, `PlayerInventory`, `HubState.MealFloor`, `DoorBreakers`, `LightPool`) must reset via `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` and in `GameFlow.ReturnToMenu`.
- **Pickup types:** `ShardPickup` and `KeyPickup` are NOT `Pickup` (which counts stars); `ObjectiveToken` (F73) has its own tally in `GameManager`.
- **WallLamp state:** `SetPowered(false)` (unpowered) is independent of `IsDead` (permanently killed).
- **Static events:** Always unsubscribe in `OnDestroy`/`OnDisable`.
- **Clock & Cursor:** Use `Time.time` (pauses at `timeScale = 0`), never `Time.unscaledTime` for run time. Call `PlayerLock.SetCursorFree(true)` *every frame* while a menu is open, and freeze via `PlayerLock.Freeze`.
- **Capture & Doors:** Capture requires `GameFlow.IsRunActive && _hasSight`. `MazeDoor` has no `NavMeshObstacle`; door-breakers must self-detect via `DoorBreakers`.
- **Input (F119):** Never read `Keyboard.current` / `Gamepad.current` / `Mouse.current` directly — always use `GameInput` and `InputGlyphs.Key(GameAction.X, touchLabel)`, and call `MenuFocus.Keep(root, firstButton)` every frame on active menus.
- **One MonoBehaviour per file**, matching the filename.
- **SaveData (F118):** Additive zero-safe fields need no version bump; anything else requires `SaveSystem.Version++` + `SaveMigrations` + fixture test. Never rename/remove fields; tests must use `SaveSystem.DirectoryOverride`.
- **Flicker (F118):** Check `FlashSafety.On` (cap at ≤3 Hz when enabled).
