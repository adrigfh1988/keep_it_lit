using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The two top entries of the KEEP IT LIT menu, so nobody has to remember which builder was run when.
///
/// KEEP IT LIT &gt; Check Project reports, piece by piece, whether everything the game clones or reads out of the
/// scene is there and up to date, naming the menu item that fixes each gap. It changes nothing.
///
/// KEEP IT LIT &gt; Build Missing Pieces runs only the builders whose piece is missing, in dependency order
/// (Interactables before Phrase Books, which assigns Stan's book to the Stalker template). Pieces that already
/// exist are never rebuilt - rebuilding one on purpose is KEEP IT LIT &gt; Build &gt; that piece.
///
/// Every new builder adds a Piece here (see CLAUDE.md, "Editor menu").
/// </summary>
public static class ProjectSetup
{
    private sealed class Piece
    {
        public string Name;
        public string FixMenu;
        /// <summary>Null = present and up to date; otherwise what is wrong.</summary>
        public System.Func<string> Problem;
        /// <summary>Run by Build Missing Pieces when Problem is not null. Null = report only.</summary>
        public System.Action Build;
    }

    private static List<Piece> Pieces() => new List<Piece>
    {
        new Piece
        {
            Name = "Localization (S9: package, settings, 4 locales, string tables, Boot scene)",
            FixMenu = "Localization > Set Up Project (and Import CSVs)",
            Problem = () => LocalizationImporter.Problem(),
            Build = () => LocalizationImporter.EnsureProject()
        },
        new Piece
        {
            Name = "Localized world signs (S9: LocalizedText on the static signs of the hub, Intake and shop prefabs)",
            FixMenu = "Localization > Localize World Signs (adds components to the prefab assets only)",
            Problem = LocalizationSignsBuilder.Problem,
            Build = () => LocalizationSignsBuilder.BuildSilently()
        },
        new Piece
        {
            Name = "Alchemist House materials (URP)",
            FixMenu = "converted automatically by Build > Shop Room (regenerate) / Floor Themes",
            Problem = () => AlchemistMaterialUpgrade.NeedsConversion() ? "still on Built-in shaders (render magenta)" : null,
            Build = () => AlchemistMaterialUpgrade.Convert()
        },
        new Piece
        {
            Name = "Floor themes gallery",
            FixMenu = "Build > Floor Themes",
            Problem = () =>
            {
                FloorThemeSet set = Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
                if (set == null) return "missing";
                if (!set.IsComplete) return "a row is incomplete";
                return DarknessRelight.NeedsRelight() ? "a row is brighter than the F71 darkness pass" : null;
            },
            Build = () =>
            {
                if (Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include) == null) FloorThemeBuilder.BuildSilently();
                else DarknessRelight.Relight(); // present but too bright: the only gap fixable without a rebuild
            }
        },
        new Piece
        {
            Name = "Blood decals (Blood decal pack)",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes run)",
            Problem = () => BloodDecalUpgrade.NeedsUpgrade() ? "some blood decals still use the code-drawn placeholder textures" : null,
            Build = () => BloodDecalUpgrade.Apply()
        },
        new Piece
        {
            Name = "Set-piece decals",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes run)",
            Problem = () => SetPieceDecalFix.NeedsFix() ? "set-piece wall decals float 2 m off their wall, or blood quads are mis-shaped" : null,
            Build = () => SetPieceDecalFix.Apply()
        },
        new Piece
        {
            Name = "Corpses (DeadBody LITE)",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes run)",
            Problem = () => CorpseUpgrade.NeedsUpgrade() ? "the FallenRunner set piece still has the primitive capsule body" : null,
            Build = () => CorpseUpgrade.Apply()
        },
        new Piece
        {
            Name = "Theme surface textures (F88)",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes run)",
            Problem = () => ThemeSurfaceUpgrade.Problems(),
            Build = () => ThemeSurfaceUpgrade.Apply()
        },
        new Piece
        {
            Name = "Pack dressing (beds, baths, bodies, furniture, vents)",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes run)",
            Problem = () => PackDressing.NeedsUpgrade() ? "a gallery row lacks pieces from the DeadBody LITE / Socket Pack / Alchemist furniture set" : null,
            Build = () => PackDressing.Apply()
        },
        new Piece
        {
            Name = "Primitive dressing cleanup (F89)",
            FixMenu = "Build Missing Pieces (or any Build > Floor Themes run)",
            Problem = () => PrimitiveDressingCleanup.NeedsUpgrade() ? "untextured primitive props / set pieces (or their prefabs) are still in a gallery row" : null,
            Build = () => PrimitiveDressingCleanup.Apply()
        },
        new Piece
        {
            Name = "Interactables kit",
            FixMenu = "Build > Interactables (builds only the empty slots)",
            Problem = () =>
            {
                InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
                if (kit == null) return "missing";
                List<string> empty = EmptySlots(kit);
                return empty.Count == 0 ? null : "empty slot(s): " + string.Join(", ", empty);
            },
            Build = InteractableKitBuilder.Build
        },
        new Piece
        {
            Name = "Stalker body (Stan_Model.fbx)",
            FixMenu = "Build > Re-skin Templates > Stalker (Stan)",
            Problem = () =>
            {
                InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
                if (kit == null || kit.Stalker == null) return null; // reported by "Interactables kit"
                return InteractableKitBuilder.HasStanModelBody(kit.Stalker) ? null : "the Stalker template is still wearing the old PlayerRobot body";
            },
            Build = InteractableKitBuilder.RebuildStalkerTemplate
        },
        new Piece
        {
            Name = "Last untextured pieces (F90)",
            FixMenu = "Build Missing Pieces (or Build > Floor Themes)",
            Problem = () =>
            {
                List<string> bad = RemainingPrimitivesCleanup.Problems();
                return bad.Count == 0 ? null : string.Join("; ", bad);
            },
            Build = () => RemainingPrimitivesCleanup.Apply()
        },
        new Piece
        {
            Name = "Phrase books (Stan + hunter)",
            FixMenu = "Build > Phrase Books (Stan + Hunter)",
            Problem = PhraseBookProblem,
            Build = StanPhraseBookBuilder.Build
        },
        new Piece
        {
            Name = "Shop room",
            FixMenu = "Build Missing Pieces (places Assets/Prefabs/ShopRoom.prefab, or saves the scene room as it; Build > Shop Room (regenerate) is a reset)",
            Problem = ShopRoomBuilder.RoomProblem,
            Build = ShopRoomBuilder.Repair
        },
        new Piece
        {
            Name = "Shop door hinge",
            FixMenu = "Build Missing Pieces (adds the hinge, clears the leaf's Static flag)",
            Problem = ShopRoomBuilder.DoorHingeProblem,
            Build = ShopRoomBuilder.FixShopDoorHinge
        },
        new Piece
        {
            Name = "Shop contract board (F87)",
            FixMenu = "Build Missing Pieces (adds the board to the existing shop; a full Build > Shop Room (regenerate) builds it too)",
            Problem = ShopRoomBuilder.ContractBoardProblem,
            Build = () => ShopRoomBuilder.AddContractBoard()
        },
        new Piece
        {
            Name = "Shop salesman (Timmy)",
            FixMenu = "Build Missing Pieces (swaps the Guard for Timmy in the existing shop; a full Build > Shop Room (regenerate) builds him too)",
            Problem = ShopRoomBuilder.TimmySalesmanProblem,
            Build = () => ShopRoomBuilder.SwapSalesmanToTimmy()
        },
        new Piece
        {
            Name = "Intake room (F86 tutorial)",
            FixMenu = "Build Missing Pieces (places Assets/Prefabs/IntakeRoom.prefab, or saves the scene room as it)",
            Problem = IntakeRoomBuilder.Problem,
            Build = IntakeRoomBuilder.Repair
        },
        new Piece
        {
            Name = "Intake room layout (lights, walking paths, walls)",
            FixMenu = "edit Assets/Prefabs/IntakeRoom.prefab by hand to clear what is listed",
            Problem = IntakeRoomBuilder.LayoutProblem,
            Build = null
        },
        new Piece
        {
            Name = "Asylum materials (F92: URP, 1024 px textures)",
            FixMenu = "Build Missing Pieces (converts both packs in place)",
            Problem = () =>
            {
                if (!AssetDatabase.IsValidFolder(AsylumMaterialUpgrade.AsylumFolder)) return null; // pack not imported: nothing to convert
                if (AsylumMaterialUpgrade.NeedsConversion()) return $"{AsylumMaterialUpgrade.NonUrpCount()} materials still on Built-in shaders (magenta)";
                if (AssetDatabase.LoadAssetAtPath<GameObject>(AsylumHubBuilder.PrefabPath) != null && AsylumMaterialUpgrade.UncappedTextures(AsylumHubBuilder.PrefabPath) > 0) return "textures above 1024 px";
                return null;
            },
            Build = () =>
            {
                AsylumMaterialUpgrade.Convert();
                if (AssetDatabase.LoadAssetAtPath<GameObject>(AsylumHubBuilder.PrefabPath) != null) AsylumMaterialUpgrade.CapTextures(new[] { AsylumHubBuilder.PrefabPath });
            }
        },
        new Piece
        {
            Name = "Asylum hub (F92, between the mazes)",
            FixMenu = "Build Missing Pieces (places Assets/Prefabs/AsylumHub.prefab, or saves the scene hub as it)",
            Problem = AsylumHubBuilder.Problem,
            Build = AsylumHubBuilder.Repair
        },
        new Piece
        {
            Name = "Asylum story layer (F93: hub navmesh, patients, records board)",
            FixMenu = "Build Missing Pieces (adds only the missing pieces to the scene hub and applies them to the prefab)",
            Problem = AsylumHubBuilder.StoryProblem,
            Build = AsylumHubBuilder.StoryRepair
        },
        new Piece
        {
            Name = "Asylum shop (F116: the Commissary, the shop as a room of the hub)",
            FixMenu = "Build Missing Pieces (redecorates the hub storeroom into the Commissary and applies it to the prefab; the old shop door is removed)",
            Problem = AsylumHubBuilder.ShopProblem,
            Build = AsylumHubBuilder.BuildShop
        },
        new Piece
        {
            Name = "Asylum classroom board (F117: THE LESSON, chalk text of what the hunter has learned)",
            FixMenu = "Build Missing Pieces (writes the chalk text, light and E target on the classroom blackboard and applies them to the prefab)",
            Problem = AsylumHubBuilder.LessonProblem,
            Build = AsylumHubBuilder.BuildLesson
        },
        new Piece
        {
            Name = "Asylum kitchen meal (F117: A HOT MEAL, three free dishes on the serving table)",
            FixMenu = "Build Missing Pieces (places the tray and dishes on the kitchen table, removes what lay there, applies it to the prefab)",
            Problem = AsylumHubBuilder.MealProblem,
            Build = AsylumHubBuilder.BuildMeal
        },
        new Piece
        {
            Name = "Asylum dining room (F117: THE LAST SUPPER, the long table, seated patients and name cards)",
            FixMenu = "Build Missing Pieces (builds the long table in the dining room, removes what stood in its walkway, applies it to the prefab)",
            Problem = AsylumHubBuilder.SupperProblem,
            Build = AsylumHubBuilder.BuildSupper
        },
        new Piece
        {
            Name = "Asylum hub breach (F121: the hunter in the hub once, floor 5)",
            FixMenu = "Build Missing Pieces (adds Life/Breach and the breach hunter's body to the hub and applies them to the prefab)",
            Problem = AsylumHubBuilder.BreachProblem,
            Build = AsylumHubBuilder.BuildBreach
        },
        new Piece
        {
            Name = "Asylum look (F95: light palette, reflection probes, hub-only SSAO, aged materials)",
            FixMenu = "Build Missing Pieces (adds only what is missing to the scene hub and applies it to the prefab)",
            Problem = AsylumHubBuilder.LookProblem,
            Build = AsylumHubBuilder.LookRepair
        },
        new Piece
        {
            Name = "Asylum fog (low fog banks in every room, Fog Particles pack)",
            FixMenu = "Build Missing Pieces (adds the fog of rooms that have none to the scene hub and applies it to the prefab)",
            Problem = AsylumHubBuilder.FogProblem,
            Build = AsylumHubBuilder.FogRepair
        },
        new Piece
        {
            Name = "Asylum hub layout (lights, walking paths, doors)",
            FixMenu = "edit Assets/Prefabs/AsylumHub.prefab by hand to clear what is listed",
            Problem = AsylumHubBuilder.LayoutProblem,
            Build = null
        },
        new Piece
        {
            Name = "Audio library (F97: recorded clips in place of the generated sounds)",
            FixMenu = "Build Missing Pieces (creates GameAudioLibrary.asset, fills empty slots, applies import settings, assigns it to MazeGenerator)",
            Problem = GameAudioLibraryBuilder.Problem,
            Build = GameAudioLibraryBuilder.BuildSilently
        },
        new Piece
        {
            Name = "Audio mixer (S2: settings volumes - Master, Music, Effects)",
            FixMenu = "Build Missing Pieces (creates GameMixer.mixer with the Music / Effects groups and exposed volumes, assigns it to MazeGenerator)",
            Problem = GameMixerBuilder.Problem,
            Build = GameMixerBuilder.BuildSilently
        },
        new Piece
        {
            Name = "Asylum NPCs + missions (F98: six people, a dialogue panel, a mission per floor 1-4)",
            FixMenu = "Build Missing Pieces (adds only what is missing to the scene hub and applies it to the prefab)",
            Problem = AsylumHubBuilder.NpcProblem,
            Build = AsylumHubBuilder.NpcRepair
        },
        new Piece
        {
            Name = "Asylum NPC robot bodies (the six hub NPCs are Timmy robots, each in its own skin)",
            FixMenu = "Build Missing Pieces (swaps the remaining bodies in the scene hub and applies them to the prefab)",
            Problem = AsylumHubBuilder.NpcRobotProblem,
            Build = AsylumHubBuilder.NpcRobotRepair
        },
        new Piece
        {
            Name = "Asylum robot patients (Hilde, Elias and the morgue body are Timmy robots; the Adam Character Pack is gone)",
            FixMenu = "Build Missing Pieces (swaps the remaining bodies in the scene hub and applies them to the prefab)",
            Problem = AsylumHubBuilder.PatientRobotProblem,
            Build = AsylumHubBuilder.PatientRobotRepair
        },
        new Piece
        {
            Name = "Intake silhouette (the hide step's figure is a black Timmy robot; the Adam Character Pack is gone)",
            FixMenu = "Build Missing Pieces (swaps the body inside IntakeRoom.prefab)",
            Problem = IntakeRoomBuilder.SilhouetteProblem,
            Build = IntakeRoomBuilder.SilhouetteRepair
        },
        new Piece
        {
            Name = "Asylum wayfinding (F100: mission where-words, shop door room name)",
            FixMenu = "Build Missing Pieces (fills the missing words in the scene hub and applies them to the prefab)",
            Problem = AsylumHubBuilder.WayfindingProblem,
            Build = AsylumHubBuilder.WayfindingRepair
        },
        new Piece
        {
            Name = "Asylum wayfinding stars (F100: Patient 7's chalk stars on the way to the next key)",
            FixMenu = "Build Missing Pieces (adds the star pool and the Commissary door star to the scene hub and applies them to the prefab)",
            Problem = AsylumHubBuilder.StarsProblem,
            Build = AsylumHubBuilder.StarsRepair
        },
        new Piece
        {
            Name = "Asylum hall props (F101: the statue bay, the halls, story vignettes, the rooms)",
            FixMenu = "Build Missing Pieces (adds only the missing pieces to the scene hub, rebakes the NavMesh, refits fog and probes and applies it to the prefab)",
            Problem = AsylumHubBuilder.PropsProblem,
            Build = AsylumHubBuilder.PropsRepair
        },
        new Piece
        {
            Name = "Sign fonts (F109: six static TMP font assets + lit text materials)",
            FixMenu = "Build > World Signs (restyle missing)",
            Problem = () => WorldSignBuilder.FontsPresent() ? null : "a sign font asset or its lit text material is missing",
            Build = WorldSignBuilder.EnsureFonts
        },
        new Piece
        {
            Name = "Intake signs (F109: enamel, light box, stencil, wall paint, paper notice, room plates)",
            FixMenu = "Build > World Signs (restyle missing)",
            Problem = WorldSignBuilder.IntakeProblem,
            Build = () => WorldSignBuilder.RestyleIntake()
        },
        new Piece
        {
            Name = "Shop signs (F109: hanging name board, brass counter plaque, chalk door slate, contracts header)",
            FixMenu = "Build > World Signs (restyle missing)",
            Problem = WorldSignBuilder.ShopProblem,
            Build = () => WorldSignBuilder.RestyleShop()
        },
        new Piece
        {
            Name = "Hub signs + room plates (F109: brass wing plaques, stencils, a name plate at every room door)",
            FixMenu = "Build > World Signs (restyle missing) (edits the scene hub and applies it to the prefab)",
            Problem = WorldSignBuilder.HubProblem,
            Build = () => WorldSignBuilder.RestyleHub()
        },
        new Piece
        {
            Name = "Web texture budget (WebGL-only size caps + crunch on every build texture)",
            FixMenu = "Build Missing Pieces (WebTextureBudget.Apply; the first run crunches hundreds of textures and takes a while)",
            Problem = WebTextureBudget.Problem,
            Build = () => WebTextureBudget.Apply()
        },
        new Piece
        {
            Name = "Hunter body (Orderly robot, F102)",
            FixMenu = "Build > Hunter Body",
            Problem = () =>
            {
                AIFollower hunter = Object.FindAnyObjectByType<AIFollower>(FindObjectsInactive.Include);
                if (hunter == null) return "no AIFollower in the scene";
                return HunterBodyBuilder.HasOrderlyBody(hunter.transform) ? null : "the hunter is not wearing the Orderly robot body yet";
            },
            Build = HunterBodyBuilder.BuildSilently
        },
        new Piece
        {
            Name = "Tutorial prefabs",
            FixMenu = "delete their scene instances, then the prefabs (report only)",
            Problem = () =>
            {
                List<string> left = new List<string>();
                foreach (string name in new[] { "Moving_Platform", "Stairs", "Wall_Light_Left", "Wall_Light_Right" })
                {
                    if (AssetDatabase.LoadAssetAtPath<GameObject>($"Assets/Prefabs/{name}.prefab") != null) left.Add(name);
                }
                return left.Count == 0 ? null : "still in Assets/Prefabs: " + string.Join(", ", left);
            },
            Build = null
        },
    };

    [MenuItem("KEEP IT LIT/Check Project", priority = 0)]
    public static void Check()
    {
        if (!SceneReady("Check Project")) return;

        StringBuilder report = new StringBuilder();
        int problems = 0;
        foreach (Piece piece in Pieces())
        {
            string problem = piece.Problem();
            if (problem == null)
            {
                report.AppendLine($"OK    {piece.Name}");
            }
            else
            {
                problems++;
                report.AppendLine($"FIX   {piece.Name}: {problem}\n        -> KEEP IT LIT > {piece.FixMenu}");
            }
        }

        string header = problems == 0 ? "Everything is built and up to date.\n\n" : $"{problems} thing(s) need attention.\n\n";
        Debug.Log("KEEP IT LIT > Check Project\n" + header + report);
        if (!Application.isBatchMode) EditorUtility.DisplayDialog("Check Project", header + report, "OK");
    }

    public static void CheckFromCommandLine()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/GetStarted_Scene.unity", OpenSceneMode.Single);
        Check();
        EditorApplication.Exit(0);
    }

    [MenuItem("KEEP IT LIT/Build Missing Pieces", priority = 1)]
    public static void BuildMissing()
    {
        if (!SceneReady("Build Missing Pieces")) return;

        List<string> ran = new List<string>();
        List<string> manual = new List<string>();
        foreach (Piece piece in Pieces())
        {
            if (piece.Problem() == null) continue;
            if (piece.Build == null)
            {
                manual.Add(piece.Name);
                continue;
            }

            piece.Build();
            ran.Add(piece.Name);
        }

        if (ran.Count > 0) EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());

        string message = ran.Count == 0 ? "Nothing was missing." : "Built / fixed:\n  " + string.Join("\n  ", ran) + "\n\nSave the scene (Ctrl+S).";
        if (manual.Count > 0) message += "\n\nNeeds a hand (see Check Project):\n  " + string.Join("\n  ", manual);
        message += "\n\nRun Check Project to confirm - an incomplete piece that already exists is never rebuilt automatically.";
        EditorUtility.DisplayDialog("Build Missing Pieces", message, "OK");
    }

    // ---------------------------------------------------------------- helpers

    private static bool SceneReady(string title)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (scene.IsValid() && scene.isLoaded) return true;
        EditorUtility.DisplayDialog(title, "Open the game scene first.", "OK");
        return false;
    }

    /// <summary>Every object-reference slot on the kit that is empty. escapeHatch is optional (an empty slot is the old glowing slab); escapeGate (F111) is not.</summary>
    private static List<string> EmptySlots(InteractableKit kit)
    {
        List<string> empty = new List<string>();
        SerializedProperty it = new SerializedObject(kit).GetIterator();
        bool enterChildren = true;
        while (it.NextVisible(enterChildren))
        {
            enterChildren = false;
            if (it.propertyType != SerializedPropertyType.ObjectReference || it.name == "m_Script" || it.name == "escapeHatch") continue;
            if (it.objectReferenceValue == null) empty.Add(it.name);
        }
        return empty;
    }

    private static string PhraseBookProblem()
    {
        if (AssetDatabase.LoadAssetAtPath<StalkerPhraseBook>(StanPhraseBookBuilder.AssetPath) == null ||
            AssetDatabase.LoadAssetAtPath<HunterPhraseBook>(StanPhraseBookBuilder.HunterAssetPath) == null)
        {
            return "asset missing (in-memory defaults are used)";
        }

        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit != null && kit.Stalker != null && !HasBook(kit.Stalker)) return "Stan's template has no book assigned";

        AIFollower hunter = Object.FindAnyObjectByType<AIFollower>(FindObjectsInactive.Include);
        if (hunter != null)
        {
            HunterVoice voice = hunter.GetComponent<HunterVoice>();
            if (voice == null || !HasBook(voice)) return "the hunter has no HunterVoice / book";
        }
        return null;
    }

    private static bool HasBook(Object component)
    {
        SerializedProperty book = new SerializedObject(component).FindProperty("phraseBook");
        return book == null || book.objectReferenceValue != null;
    }
}
