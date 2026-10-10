using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The two top entries of the LIGHTS OUT menu, so nobody has to remember which builder was run when.
///
/// LIGHTS OUT &gt; Check Project reports, piece by piece, whether everything the game clones or reads out of the
/// scene is there and up to date, naming the menu item that fixes each gap. It changes nothing.
///
/// LIGHTS OUT &gt; Build Missing Pieces runs only the builders whose piece is missing, in dependency order
/// (Interactables before Phrase Books, which assigns Stan's book to the Stalker template). Pieces that already
/// exist are never rebuilt - rebuilding one on purpose is LIGHTS OUT &gt; Build &gt; that piece.
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
            Problem = () =>
            {
                ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
                if (shop == null) return null; // reported by "Shop room"
                SerializedProperty hinge = new SerializedObject(shop).FindProperty("doorHinge");
                Transform pivot = hinge != null ? hinge.objectReferenceValue as Transform : null;
                if (pivot == null) return FindShopDoorLeaf(shop) != null ? "no doorHinge wired" : null;
                if (IsStaticAnywhere(pivot)) return "the door leaf is Static - static batching freezes it shut";
                return FindFusedDoorModel(shop) != null ? "the pack's fused Door model (frame + closed panel) hides the swinging leaf" : null;
            },
            Build = FixShopDoorHinge
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
            Name = "Asylum shop door (F94: the SHOP door between the hub and the shop)",
            FixMenu = "Build Missing Pieces (adds the SHOP door to the scene hub on a prop-free wall and applies it to the prefab)",
            Problem = AsylumHubBuilder.ShopDoorProblem,
            Build = AsylumHubBuilder.AddShopDoor
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
            Name = "Asylum wayfinding (F100: mission where-words, shop door room name)",
            FixMenu = "Build Missing Pieces (fills the missing words in the scene hub and applies them to the prefab)",
            Problem = AsylumHubBuilder.WayfindingProblem,
            Build = AsylumHubBuilder.WayfindingRepair
        },
        new Piece
        {
            Name = "Asylum wayfinding stars (F100: Patient 7's chalk stars on the way to the next key)",
            FixMenu = "Build Missing Pieces (adds the star pool and the SHOP door star to the scene hub and applies them to the prefab)",
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

    [MenuItem("LIGHTS OUT/Check Project", priority = 0)]
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
                report.AppendLine($"FIX   {piece.Name}: {problem}\n        -> LIGHTS OUT > {piece.FixMenu}");
            }
        }

        string header = problems == 0 ? "Everything is built and up to date.\n\n" : $"{problems} thing(s) need attention.\n\n";
        Debug.Log("LIGHTS OUT > Check Project\n" + header + report);
        EditorUtility.DisplayDialog("Check Project", header + report, "OK");
    }

    [MenuItem("LIGHTS OUT/Build Missing Pieces", priority = 1)]
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

    /// <summary>Every object-reference slot on the kit that is empty. escapeHatch is optional (an empty slot is the old glowing slab).</summary>
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

    /// <summary>The shop's closed door leaf (ShopRoom/Door/WoodDoor01...), whether or not it already sits under a hinge.</summary>
    private static Transform FindShopDoorLeaf(ShopRoom shop)
    {
        Transform door = shop.transform.Find("Door");
        if (door == null) return null;
        foreach (Transform t in door.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("WoodDoor01")) return t;
        }
        return null;
    }

    private static bool IsStaticAnywhere(Transform root)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (GameObjectUtility.GetStaticEditorFlags(t.gameObject) != 0) return true;
        }
        return false;
    }

    /// <summary>Gives an already-built shop room what Build &gt; Shop Room now makes: a hinge pivot on the leaf, wired into ShopRoom, nothing under it Static.</summary>
    private static void FixShopDoorHinge()
    {
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null) return;

        SerializedObject so = new SerializedObject(shop);
        SerializedProperty hingeProp = so.FindProperty("doorHinge");
        Transform hinge = hingeProp.objectReferenceValue as Transform;

        if (hinge == null)
        {
            Transform leaf = FindShopDoorLeaf(shop);
            if (leaf == null) return;

            hinge = leaf.parent != null && leaf.parent.name == "DoorHinge" ? leaf.parent : null;
            if (hinge == null)
            {
                Undo.RegisterFullObjectHierarchyUndo(leaf.parent.gameObject, "Hinge Shop Door");
                hinge = ShopRoom.CreateDoorHinge(shop.transform, leaf);
                Undo.RegisterCreatedObjectUndo(hinge.gameObject, "Hinge Shop Door");
            }

            hingeProp.objectReferenceValue = hinge;
            so.ApplyModifiedProperties();
        }

        foreach (Transform t in hinge.GetComponentsInChildren<Transform>(true)) Undo.RecordObject(t.gameObject, "Hinge Shop Door");
        ShopRoomBuilder.MakeMovable(hinge);

        GameObject fused = FindFusedDoorModel(shop);
        if (fused != null) Undo.DestroyObjectImmediate(fused);

        EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
    }

    /// <summary>
    /// The pack's Architecture/Door model under ShopRoom/Door: one mesh with the frame and a closed panel fused
    /// together. Older Build &gt; Shop Room runs placed it over the leaf, so the leaf swung open unseen behind it.
    /// </summary>
    private static GameObject FindFusedDoorModel(ShopRoom shop)
    {
        Transform group = shop.transform.Find("Door");
        if (group == null) return null;
        foreach (Transform child in group)
        {
            if (child.name == "Door" && child.GetComponent<Renderer>() != null) return child.gameObject;
        }
        return null;
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
