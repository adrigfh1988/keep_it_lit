# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

**KEEP IT LIT** (renamed from LIGHTS OUT on 11 Oct 2026; "LIGHTS OUT" survives only in-world, as the name of Ending B and a hunter line) — a first-person horror maze game. Unity 6 (editor `6000.6.2f1`), URP. It started life as the "Unity Essentials" / Get Started tutorial (a third-person robot collecting stars); the tutorial content has been removed, but the robot and Starter Assets controller are still the player. The game is now: you are dropped into a dark procedural maze with a flashlight, you collect 5 stars while something hunts you, and then you have 60 seconds to find the escape hatch.

Git repository (branch `main`). There is no test suite and no assembly definitions under `Assets/`, so all game code compiles into `Assembly-CSharp`.

## Commands

Development happens in the Unity Editor (Rider / Visual Studio / VS Code attach via `.vscode/launch.json`). The generated `*.csproj`, `*.sln`, `Library/`, `Temp/`, `obj/` and `Logs/` are Unity-generated — do not edit them.

**Drive the open Editor with the Unity CLI, never MCP** (Unity deprecated the AI Assistant package's in-Editor MCP server; don't call
`mcp__unity-mcp__*` tools). The project has `com.unity.pipeline` (0.7.0-exp.1) and `com.unity.ai.assistant` 2.19 (≥ 2.13, so no
conflict with the CLI). `unity status` → `ready`; `unity command eval --code '...'` / `unity command eval_file --file x.cs` (a
statement block: no `using`, fully qualified types); `unity recompile` reports compile errors; long jobs (mass reimports, bakes,
builds) with `--detach` + `unity job wait <id>` — a busy main thread fails the call after ~5 s while the work may still run (the
F99 texture crunch did), so check `Logs/Editor.log` before retrying.

Batch-mode compile check / build:

```
"C:\Program Files\Unity\Hub\Editor\6000.6.2f1\Editor\Unity.exe" -batchmode -quit -projectPath "D:\Unity\essentials\keep_it_lit" -logFile -
```

Compile errors from the running Editor also land in `Logs/Editor.log` — `grep "error CS" Logs/Editor.log` is the fastest way to check the last compile without launching a second Unity instance (one is usually already running and holds the project lock).

Build settings: `Assets/Scenes/Boot.unity` (scene 0, waits for the localization tables) then `Assets/Scenes/GetStarted_Scene.unity`; Play from `GetStarted_Scene` in the Editor as before. No tests exist; if added, run with `-runTests -testPlatform EditMode|PlayMode` (and `-testFilter <name>`).

## The one thing to understand first

**Almost nothing is authored in the scene.** `MazeGenerator.Awake` (`[DefaultExecutionOrder(-100)]`) builds the whole game at runtime: walls, floor, ceiling, the NavMesh bake, stars, the first-person rig, the flashlight and every director component (each `AddComponent`-ed onto the `MazeGenerator` GameObject and wired with `Configure`/`Bind`). The authored exceptions are galleries the maze clones from and hand-editable rooms, all built by **KEEP IT LIT > Build > ...** menu items:
- **Hand-editable prefabs** (edit them in Prefab Mode; builders only *place* them; "regenerate" menu items are confirmed resets): `Assets/Prefabs/ShopRoom.prefab` (+ nested `ShopSalesman.prefab`), `IntakeRoom.prefab`, `AsylumHub.prefab`.
- **Galleries the maze clones** (scene objects): `InteractableKit` (every interactable/entity template: doors, buttons, fuses, keys, throwables, notes, the Stalker, the Listener, item caches...) and `FloorThemes` (one `FloorTheme` row per floor). A missing slot logs an error naming its menu item and skips that feature.

Full notes on these rooms and galleries: `docs/systems.md` (first section).

So: **do not look for Inspector wiring or prefabs to explain behaviour, and do not add setup to the scene.** Read `MazeGenerator.SetUpAtmosphere` (near the end of the file) — that method is the composition root. New systems get created and configured there.

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

## Run lifecycle

0. **START GAME (F86)** opens the **Intake** (`IntakeRoom`, a short authored tutorial room) instead of the rules screen: `MainMenu.BeginNewCampaign` → `CloseMenus()` → `IntakeRoom.Begin()`, `GameFlow.IsInIntake = true`, the run is NOT active (hunter held in `AIFollower.Update`, everything else gated on `IsRunActive`). Nine steps (look, move, torch, focus, battery, read, throw, hide, star + hatch), one goal at a time on a top-centre **objective card** (`ObjectiveCard.cs`, shared with the hub's `HubObjectiveCard`) (OBJECTIVE label + `n / 9`, the goal in caps, key-cap chips via `TouchInput.Key` so touch shows button names, a tick then fade between goals) plus a bobbing amber **goal arrow** over the target and a pulsing glow on pickups (`IntakeRoom.goalArrow`/`goalGlow`, authored by the builder; one shared light); the hatch walk-in or the pause menu's SKIP TUTORIAL ends it: blackout → back to the maze start cell → `IsInIntake = false` → `GameFlow.BeginRun()`, i.e. step 1 below. **F92:** with a usable Asylum hub the Intake hands over to it instead (arrive at the main entrance, walk to the open wing door, E, then the same blackout -> maze start -> `BeginRun`); the fallback shop room's door and CONTINUE from a floor-start save likewise reopen each new floor in the hub (`GameFlow.ArriveInHub`). Retry and Restart skip it. CONTINUE, Retry and NEXT FLOOR never enter it (they go through `SkipMenuOnLoad` → `StartGame`). With no `IntakeRoom` built START GAME falls back to the old rules screen (now also reachable from the title's CONTROLS button, as a reference sheet).
1. `MainMenu` opens with `Time.timeScale = 0`; `StartGame` unfreezes and calls `GameFlow.BeginRun()` (sets `GameFlow.RunStartTime` and `GameFlow.IsRunActive = true`).
2. `Pickup.OnTriggerEnter` (Player tag) → static `Pickup.OnCoinCollected` → `GameManager` decrements and raises `GameManager.ProgressChanged(collected, total)`.
3. Last star → `GameManager.AllStarsCollected`. **If anything is subscribed, that listener takes over**; only with no listener does `GameManager` call `GameOutcome.FloorCleared` directly. `MazeEscape` and `TensionDirector` both subscribe.
4. `MazeEscape` opens a glowing hatch at least 12 m away, starts a 60 s countdown, and builds a landing platform underneath it. **F111:** on a gate floor (the old 50 % seeded roll from floor 3: one gate; the final floor and the DESCENT boss: two) there is no hatch: the exit gates built into the outer wall from the start of the floor get power, the countdown is always 120 s, and the player holds E on a gate's lever (progress never goes down) then walks out of the open gate (`MazeEscape.ExitThroughGate`, same `FloorCleared` path).
5. Ending, always through `GameOutcome`:
   - reach the hatch (or an open gate) on floors 1–4 → `MazeEscape.DropThroughHatch()` then `GameOutcome.FloorCleared()`: blackout during the fall, teleport into `ShopRoom`, a payout ledger, CONTINUE → `GameFlow.IsInShop = true` until the player walks through the door (E) → `GameFlow.NextFloor()`. **F94/F116:** with a hub that `CanVisitShop` (`AsylumHub.ShopInHub`) the player is not teleported to the shop: the ledger is shown over black, CONTINUE → `GameFlow.NextFloor()` → the hub at the cleared wing's arrival point, and the shop is a room of the asylum, **THE COMMISSARY** (the ground-floor storeroom, `HubShop`): the player walks in through its real door, no fade, no teleport (open from floor 2 on; shut and padlocked on floor 1 and in the finale). The old far-away `ShopRoom` flow above stays as the fallback (no usable hub) and for legacy shop saves.
   - reach the hatch (or an open gate) on floor 5 → (F93) `GameOutcome.FinaleSequence`: back to the Director's office in the asylum, ending A (the main doors) or B (the master switch, only with all case files) through `GameOutcome.CompleteFinale`; with no usable hub still the old `GameOutcome.Win()` — the ending, with the campaign total.
   - timer expires → `GameOutcome.Lose(LoseReason.OutOfTime)`
   - hunter catches you → `AIFollower.PlayerCaught` → holding a Second Wind, `GameOutcome.SecondWindSequence` (cut to black, hunter warped away and stunned, player freed) instead; otherwise `GameOutcome.Lose(LoseReason.Caught)` → `CaptureSequence` coroutine (camera eases onto its eyes, torch flickers out, blackout) → end screen.

`GameOutcome` is single-shot: `IsOver` / `IsEnding` guard every entry point, so a hatch drop landing mid-capture is ignored.

## Systems

**The full notes for every system are in `docs/systems.md`** (one bullet per system, the same headings as below). Read the relevant bullet before changing a system; keep it up to date there, not here. This is the index:

- **World** — `MazeGenerator` (+ partials `.Loop/.Layout/.Lure/.Descent/.Entities/.Gates`): seeded maze, NavMesh bake, stars, dressing passes (`MazeDressing`), doors/vaults (F72), fuses and objectives (F73/F77 `FloorProfile.Objectives` flags), stealth tools/lore/run rules (F74), the composition root. RNG streams are `usedSeed + n`; **the next free offset is +27** (+26 = the F111 exit gates).
- **Player** — `FirstPersonRig`, `Flashlight` (wide/focused beam, recharge, `IsDead` not permanent), `ThirdPersonController` (`FirstPerson = true`), `PlayerStealthState`, `PlayerInteractor` (every `IInteractable`), `ThrowController`, `KeyRing`.
- **Hunter** — `AIFollower` (sight/hearing/search/ambush, capture needs sight), body built by Build > Hunter Body: **the Sci-Fi Cyborg by default** (`HunterBodyBuilder.Cyborg.cs`, Build > Hunter Body Look switches to the Orderly robot).
- **The Stalker (Stan)** — `Stalker.cs`: moves only unlit, never kills, drains the torch; `StalkerVoice`/`HunterVoice` speech bubbles.
- **Door breakers** — `DoorBreakers` registry; `MazeDoor` self-detects agents (no NavMeshObstacle).
- **Dread layer / escalation / relocation / phantoms** — `AIPresence`, `HorrorAudioDirector`, `HorrorAtmosphere`, `TensionDirector`, `DreadDirector`, `PhantomDirector`, `LightPool`.
- **The Intake (F86)** — `IntakeRoom` tutorial room (prefab), `IsInIntake` reader split.
- **The Asylum hub (F92-F101, door passages F112, lift chase F113)** — `AsylumHub` (prefab) between floors: wing doors, the Commissary (F94 shop door, F116 the shop as the storeroom room `HubShop`), story layer (F93: files, patients, decay, set pieces, finale), look (F95), scarier pass (F96), NPCs + dialogue + missions (F98, all six NPCs and the patients are Timmy robots in their own skins), wayfinding (F100), props and vignettes (F101), room finds + scares (F114: `HubCache`, `HubRoomScare` and its seven subclasses, the `CctvOnly` layer), Director's suite (F115: the office opened onto a private lounge, the Director behind his desk, `AsylumHubBuilder.DirectorSuite*`).
- **World signs (F109)** — `WorldSignBuilder` (+ `AsylumHubBuilder.Signs.cs` room plates): real sign backings, lit text, six fonts in `Assets/SourceFiles/Fonts/`.
- **Maze terror (F103-F106)** — `TerrorSettings` switches: unreliable cues (Tier A), dread meter, held breath, sanctuary decay, shadow/peripheral phantoms, world shifts (Tier B), learning hunter `HunterMemory`/`HabitTracker` with the lesson card, final floor, penumbra Stan, rooms/gallery/squeeze, the loop corridor and its puzzle (rules notice, chalk count, hanging verdict signs; `debugForceLoop`, Debug > Go To Loop Entrance).
- **The floor map (F106/F110)** — `FloorMapUi` + `MazeMapData`: **only exists once a Floor Plan item is used** (held consumable, cap 1; hold M / View or the pause MAP button), then shows only walked cells for that floor; never stars, keys, fuses, the hatch or the hunter; the loop reward reveals the whole layout.
- **DESCENT (F107)** — endless mode: `GameFlow.IsDescent`, `DescentRun`, `FloorProfile.ForDescent`, item caches (`DescentItem`/`DescentLoot`, +24; 40 % of levels hold a Floor Plan), entity roster (`EntityRoster`, +25): the Listener, the Tenant, living bodies. Testing: `debugStartInMaze` + `debugDescent` + `debugFloor`.
- **Settings (S2)** — `GameSettings`, `SettingsMenu`; **audio routing rule**: music sources call `GameSettings.RouteMusic`, use `GameAudio.ClipAtPoint`.
- **Localization (S9)** — every player-visible string goes through `L10n.T("table.key")`; text lives in `Assets/Localization/Source/*.csv` (EN + es/fr/de), imported by KEEP IT LIT > Localization > Import CSVs.
- **Contracts (F87)** — `ContractState`, `ContractTracker`, the shop's contract board.
- **Economy / shop** — `ShopCatalogue` (12 cards; the Floor Plan replaced HUD Tint, retired), `PlayerWallet`, `PlayerInventory`, `ShopMenu`, `ConsumableController`, `LoreArchive`.
- **Flow / shell / saves** — `GameFlow`, `PlayerLock`, `MainMenu`, `PauseMenu`, `GameOutcome` (single-shot, `partial`), `RuntimeUi`, `SaveSystem` (whole floors only, one CONTINUE).
- **Touch (F76)** — `TouchInput` facade, `TouchControls`, `MobilePerformance`.
- **Recorded audio (F97)** — `GameAudioLibrary` / `GameAudio`; generated sounds are fallbacks.

## Editor menu (KEEP IT LIT)

Every authored scene piece is generated by an editor menu item. The menu is organised so nobody has to remember what was run:

| Path | What it is |
|------|-----------|
| **Check Project** | Read-only report: each piece OK / FIX, with the menu item that fixes it. Run this first when unsure. (`Assets/Editor/ProjectSetup.cs`) |
| **Build Missing Pieces** | Runs only the builders whose piece is *missing*, in dependency order. Never rebuilds something that exists. |
| **Build > …** | One builder per scene piece (Floor Themes, Interactables, Phrase Books, Shop Room (regenerate), Intake Room (regenerate), Asylum Hub (regenerate), Hunter Body). Re-running one deliberately rebuilds that piece (for the Intake and the shop that overwrites their hand-edited prefabs). **Build > Re-skin Templates > …** empties specific `InteractableKit` slots and rebuilds just those. |
| **Debug > …** | Dev helpers that touch no content: Delete Save, and Print Maze Map (Play only: `MazeGenerator.DebugMap()` draws walls, doors, stars, keys, buttons, start, player and hunter as ASCII from the `MazeMapData` snapshot of `MazeGenerator.BuildMapData()` (F106: the same snapshot feeds `FloorMapUi`), copied to the clipboard and `Logs/MazeMap.txt`; also on the component's context menu). |

Rules when adding or changing editor tooling:

- **A builder is the recipe for a scene piece — keep it** and put it under `KEEP IT LIT/Build/` (priority 100+). Give it a dialog-free `BuildSilently()` entry point (or make `Build()` dialog-free when the piece is missing), and **register it as a `Piece` in `ProjectSetup.Pieces()`** with a `Problem` check (missing/incomplete) and a `Build` action, in dependency order. A builder that isn't in `ProjectSetup` will be forgotten.
- **A one-off migration** (convert materials, patch existing scene data to a new format, clean up imports) **is not a permanent menu item.** Prefer running it automatically from the builder(s) that depend on it (as `AlchemistMaterialUpgrade` and `DarknessRelight` now are), or add a `Problem` check so Check Project flags it. If it really must be a menu item, put it under `KEEP IT LIT/Migrations/`, and **delete it (the menu item, and any code nothing else calls) once it has been applied to the scene and committed** — git history keeps it. Before deleting, confirm in the scene YAML that it was applied, and check no builder still calls into the file.
- **Exception — hand-designed rooms are prefabs.** When the user wants to change a room by hand (the Intake since 8 Oct 2026, the shop since 10 Oct 2026), the builder generates a first draft and saves it as a prefab; from then on the prefab is the source of truth, `BuildSilently()` only places it, regeneration is a confirmed reset, and a report-only `ProjectSetup` piece runs the room's audit on the hand-edited result. Don't put a hand change into builder code instead — edit the prefab.
- Never auto-save the scene from a builder; mark it dirty and tell the user to save (Ctrl+S).
- Menu paths are referenced in log/`LogError` messages and comments as `KEEP IT LIT > Build > X` (`&gt;` in XML docs) — keep them in sync when renaming.

## Gotchas that bite

- **Statics must survive a scene reload and must not survive Play mode.** `GameFlow` and `GameOutcome.IsOver` both have a `[RuntimeInitializeOnLoadMethod(SubsystemRegistration)]` reset, because with *Enter Play Mode > Reload Domain* disabled a stale `SkipMenuOnLoad` would skip the title screen on the next run. `GameFlow.IsInShop`, `PlayerWallet` and `PlayerInventory` all have the same reset, and are also cleared in `GameFlow.ReturnToMenu` — the economy lives for one campaign only. Any new static run state needs the same treatment.
- **`ShardPickup` is deliberately not a `Pickup`.** `GameManager.Start` counts `Pickup` objects as stars; a shard sharing that type would corrupt the star count and could fire `AllStarsCollected` early. **`ObjectiveToken` (F73) is the same idea for non-star objectives** (fuse boxes) — `GameManager` counts it alongside `Pickup`s in a second tally, so it gates `AllStarsCollected` too without touching the star count itself. **`KeyPickup` (F77) is neither** — a key never gates the hatch *opening* (it still opens on the last star/fuse exactly as before), only its *use*; the lock lives entirely in `MazeEscape`, not `GameManager`.
- **A `WallLamp.SetPowered(false)` lamp is not `IsDead`.** They're independent: unpowered just means dark-and-not-ticking-its-flicker right now (a Blackout floor before its fuse), `IsDead` means permanently killed (F27). `DreadDirector`'s kill waves/breath-blink and `PhantomDirector`'s lamp-blink phantom all check `IsPowered` too, or they'd waste a blink on a lamp that's already invisible. `Kill()` on an unpowered lamp is allowed and just waits — the fade plays once the lamp is powered back on.
- **Every static-event subscriber unsubscribes** in `OnDestroy`/`OnDisable` (`GameManager`, `MazeEscape`, `TensionDirector`, `HorrorAudioDirector`, `GameOutcome`). Restart is a plain `SceneManager.LoadScene`, so a missed unsubscribe leaks across reloads. Keep the pattern. `DoorBreakers` (F75) follows the same registry pattern as `LightPool`: scene-scoped, `SubsystemRegistration`-reset, and every registrant (`AIFollower.OnDisable`, `Stalker.OnDestroy`) unregisters itself.
- **`Time.time` is the run clock** and stands still at `timeScale = 0`, which is why run time is net of menu and pause. Don't swap it for `Time.unscaledTime`.
- **The cursor fight.** `StarterAssetsInputs` re-locks the cursor on every focus change, so anything showing a menu calls `PlayerLock.SetCursorFree(true)` *every frame*, not once. Freezing the player is three flags (`MovementLocked`, `LockCameraPosition`, `cursorLocked`) — always go through `PlayerLock.Freeze`.
- **Capture needs sight, not just distance.** A bare radius test captures through a wall (two actors either side of a 0.5 m wall are ~1.5 m apart), and `Update` still ticks at `timeScale = 0` behind the title screen — hence the `GameFlow.IsRunActive && _hasSight` gate in `AIFollower`.
- **`GameManager` counts stars at `Start`.** Anything spawning or removing stars after that desyncs the count.
- **Input System only** (`activeInputHandler: 1`). Keep the `#if ENABLE_INPUT_SYSTEM` guards in `ThirdPersonController` / `StarterAssetsInputs`. Note `Flashlight` and `PauseMenu` read `Keyboard.current` / `Gamepad.current` directly and bypass the action map, so those bindings are not remappable yet — as of F71 that now includes `Mouse.current.rightButton` / `Gamepad.current.leftTrigger` for the focused beam.
- **`Flashlight.IsDead` is no longer permanent (F71).** It recharges while off and can clear itself mid-run, so don't cache "the torch is dead" past one frame — re-read `IsDead`/`Charge` each time, the way `PlayerHud.UpdateBattery` already does.
- **A door is built post-bake with a collider and deliberately no `NavMeshObstacle` (F72).** The navmesh is already baked by the time `MazeDoor` exists, so a shutter can be any width the corridor needs — but it also means a `NavMeshAgent` has zero physics response to its collider and will silently clip straight through a closed door unless the door notices and opens itself (`MazeDoor.CheckHunterBash` + `AIFollower.Stall`). Any future door-breaker (F75's Stalker) needs the same self-detection; there's no NavMeshObstacle carve to lean on.
- **E has two owners now (F72).** `PlayerInteractor` (camera spherecast, `IInteractable`) is the default; `Locker` is the one holdout with its own E handling, and only because it predates this slice — new hideable/interactable things should implement `IInteractable`, not copy `Locker`'s pattern. `PlayerHud.SetFocusPrompt` always wins over `SetPrompt` while set.
- **New visible models are `InteractableKit` templates, not primitives at runtime.** Built once by **KEEP IT LIT > Build > Interactables** (`Assets/Editor/InteractableKitBuilder.cs`, build-missing-only by default so later slices can add their own slots safely); `MazeGenerator`'s various `Build*` passes clone them and log an error (naming the menu item) with no fallback if the kit or a slot is missing.
- **One MonoBehaviour per file, named after the file.** A component class declared inside another script's file (F86's `IntakeTarget` lived in `IntakeRoom.cs`) still works when added from the Editor, but the scene then embeds a stand-in `MonoScript` (`grep "^MonoScript:" Assets/Scenes/GetStarted_Scene.unity` finds it) and every reference to that component comes back **null after a runtime scene load** (Retry / next floor / back to menu) and may not load at all in a player build. Fixed 8 Oct 2026 by moving it to `IntakeTarget.cs` and re-pointing the component's `m_Script`. Plain (non-component) helper classes can share a file.
- Keep `.meta` files alongside assets when moving or renaming; scenes and prefabs are text-serialized YAML, so hand edits must preserve GUID references.
- **Any new direct key-reader must also OR in the `TouchInput` facade (F76).** The pattern that bit the first seven readers before `TouchInput` existed - a bare `Keyboard.current`/`Gamepad.current` check with no touch equivalent - silently strands tablet players on that one action. `TouchInput.Pressed`/`Held`/`Released` cost nothing to add next to the existing check even if you never wire up a button for it. Also: `TouchInput.Pressed`/`Released` intentionally lag `SetHeld` by one frame (see the Touch/tablet bullet above) - don't "simplify" that to same-frame, it exists to make every reader see the edge exactly once regardless of execution order.
- **Feature-specific gotchas** (the escape hatch template, `MazeDoor._open`, F74/F77/F87/F92/F93 statics and agent type, pack dressing F79/F85, surface textures and primitive cleanup F88-F90, recorded audio F97, saves and `ShopItem` ints F83) are in `docs/gotchas-features.md` - read the relevant one before touching that feature.

## Planning docs

`plannings/` sits outside `Assets/`, so Unity never imports it:

- `full-game-feature-plan.md` — the road to a full game, Tiers 1–5 (F1–F21).
- `tier1-core-loop-plan.md` — the detailed spec for capture, restart, pause and end screens.

**As of 20 Sep 2026 Tier 1 is being implemented.** `GameFlow`, `PlayerLock`, `GameOutcome`, `PauseMenu` and the `Captured` state all exist. If something in this file disagrees with the code, the code is newer — check `MazeGenerator.SetUpAtmosphere` first.

## Non-game assets

- The Unity Essentials tutorial itself is gone: `Assets/Tutorials/`, the `com.unity.learn.iet-framework` package, `UpdateCollectibleCount` and the collectibles UI prefab, and `GameManager`'s win panel are deleted. `Assets/SourceFiles/Models|Animation|Materials|...` are content assets the game still uses.
- The one-off *Remove Tutorial Leftovers* migration has been run and deleted (it is in git history): the tutorial props in the scene, `RespawnPlayer` on the `PlayerRobot` prefabs and `MotionAudioController` are gone. The Moving_Platform / Wall_Light prefabs survived because the scene still has instances of them (Stairs has none); **KEEP IT LIT > Check Project** lists them.
- `Background_Ambient_Sci-Fi` (scene prefab) is **not** tutorial clutter: `MazeGenerator.TakeOverSceneAmbience` uses its looping clip as the hunter's hum.
- Render settings: URP with `PC_RPAsset`/`Mobile_RPAsset` in `Assets/SourceFiles/Settings/`.
- The player is still the third-person `PlayerRobot.prefab` (two copies) with `FirstPerson = true`; the hunter wears a stripped instance of it too (F102, the "Orderly" skin), built by **KEEP IT LIT > Build > Hunter Body**.
- **The Adam Character Pack is gone (11 Oct 2026, licence).** `Assets/UnityTechnologies/` was deleted; every body it supplied (hub patients Hilde/Elias, the morgue body, the Intake silhouette, the old NPC/pursuer bodies in the builders) is a stripped `PlayerRobot.prefab` (`InteractableKitBuilder.InstantiateRobotNode`); its URP pipeline/renderer/volume assets live in `Assets/SourceFiles/Settings/URP/`. Do not import it again; new characters are Timmy robots (painted with `HunterSkinBuilder`) or Mixamo bodies. It is still in git history.
- `Assets/Maze/` keeps only what the wall-button template still uses: `Models/Switch.fbx`, `Prefabs/Switch.prefab`, `Materials/PixPal_URP.mat` (+ its shader graph and textures) and the pack notice, referenced by `InteractableKit/WallButton` and `InteractableKitBuilder.MountKitSwitch`. The rest of the Maze Modular Puzzle Kit (and the F48 kit-maze test path it served) was removed 10 Oct 2026; the `Kit_*` decal/dust prefabs under `Themes/Common/Prefabs` are shared (hub `blood:` keys) and stay.
