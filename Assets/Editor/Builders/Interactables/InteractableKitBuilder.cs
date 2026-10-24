using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// KEEP IT LIT &gt; Build &gt; Interactables. Generates the primitive-built templates every door/button/vault
/// pickup clones at runtime (F72 decision D11) - materials under Assets/SourceFiles/Materials/Interactables
/// (never overwritten if present) and a scene gallery (InteractableKit) holding one live instance of
/// each template, so an artist can inspect and retouch them exactly like FloorThemes. Nothing here runs
/// in a build; at Play time MazeGenerator.BuildDoors just finds what this made and clones it.
///
/// Build-missing-only by default (decision D11): an InteractableKit slot that is already assigned is
/// left completely alone, so a later slice (F73's fuse box, etc.) can add its own template without
/// touching this one. Re-running this menu item is always safe.
/// </summary>
public static partial class InteractableKitBuilder
{
    private const string MaterialFolder = "Assets/SourceFiles/Materials/Interactables";
    private const string GalleryName = "InteractableKit";
    private const string RobotPrefabPath = "Assets/Prefabs/PlayerRobot.prefab";

    // 30 m+ clear of every existing authored/generated spot (CLAUDE.md "The one thing to understand
    // first" + F70's dressing note): the largest maze footprint (floor 4, 12x8 cells at 5.5 m, so
    // origin (-70,4.98,-70) out to roughly x:[-70,-4] z:[-70,-26]), FloorThemes (-70,5,-6), ShopRoom
    // (-92,5,-45). +60 in Z clears all of them with margin to spare.
    private static readonly Vector3 GalleryOrigin = new Vector3(-70f, 5f, 60f);
    private const float PieceSpacing = 5f;

    [MenuItem("KEEP IT LIT/Build/Interactables", priority = 102)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Build Interactables", "Open the game scene first.", "OK");
            return;
        }

        EnsureFolder();

        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        GameObject root;
        if (kit != null)
        {
            root = kit.gameObject;
        }
        else
        {
            root = new GameObject(GalleryName);
            Undo.RegisterCreatedObjectUndo(root, "Build Interactables");
            root.transform.position = GalleryOrigin;
            kit = root.AddComponent<InteractableKit>();
        }

        SerializedObject so = new SerializedObject(kit);
        SerializedProperty shutterProp = so.FindProperty("shutterDoor");
        SerializedProperty buttonProp = so.FindProperty("wallButton");
        SerializedProperty cellProp = so.FindProperty("batteryCell");
        SerializedProperty fuseProp = so.FindProperty("fuseBox");
        SerializedProperty bottleProp = so.FindProperty("throwableBottle");
        SerializedProperty canProp = so.FindProperty("throwableCan");
        SerializedProperty variantsProp = so.FindProperty("bottleVariants");
        SerializedProperty glassProp = so.FindProperty("glassPatch");
        SerializedProperty puddleProp = so.FindProperty("puddle");
        SerializedProperty lanternProp = so.FindProperty("lantern");
        SerializedProperty noteProp = so.FindProperty("loreNote");
        SerializedProperty bellProp = so.FindProperty("callBell");
        SerializedProperty valveProp = so.FindProperty("steamValve");
        SerializedProperty candlesProp = so.FindProperty("candles");
        SerializedProperty terminalProp = so.FindProperty("terminal");
        SerializedProperty stalkerProp = so.FindProperty("stalker");
        SerializedProperty keyProp = so.FindProperty("keyPickup");

        // Continue the row after whatever is already there: build-missing-only used to restart at x = 0,
        // which stacked every later slice's template on top of the shutter door.
        float x = NextFreeX(root.transform);
        int built = 0;

        if (shutterProp.objectReferenceValue == null)
        {
            MazeDoor door = BuildShutterDoor(root.transform, new Vector3(x, 0f, 0f));
            shutterProp.objectReferenceValue = door;
            x += PieceSpacing;
            built++;
        }
        if (buttonProp.objectReferenceValue == null)
        {
            WallButton button = BuildWallButton(root.transform, new Vector3(x, 1f, 0f));
            buttonProp.objectReferenceValue = button;
            x += PieceSpacing;
            built++;
        }
        if (cellProp.objectReferenceValue == null)
        {
            BatteryCellPickup cell = BuildBatteryCell(root.transform, new Vector3(x, 0.4f, 0f));
            cellProp.objectReferenceValue = cell;
            x += PieceSpacing;
            built++;
        }
        // F73: build-missing-only (decision D11) - adds just this slot to an existing kit without
        // touching the F72 slots above.
        if (fuseProp.objectReferenceValue == null)
        {
            FuseBox box = BuildFuseBox(root.transform, new Vector3(x, 0.5f, 0f));
            fuseProp.objectReferenceValue = box;
            x += PieceSpacing;
            built++;
        }

        // F74: same build-missing-only rule - every slot below only touches an empty slot of its own.
        if (bottleProp.objectReferenceValue == null)
        {
            ThrowableItem bottle = BuildBottle(root.transform, new Vector3(x, 0.15f, 0f));
            bottleProp.objectReferenceValue = bottle;
            x += PieceSpacing;
            built++;
        }
        if (canProp.objectReferenceValue == null)
        {
            ThrowableItem can = BuildCan(root.transform, new Vector3(x, 0.08f, 0f));
            canProp.objectReferenceValue = can;
            x += PieceSpacing;
            built++;
        }
        if (glassProp.objectReferenceValue == null)
        {
            NoisySurface glass = BuildFloorPatch(root.transform, new Vector3(x, 0.01f, 0f), "GlassPatch", glass: true);
            glassProp.objectReferenceValue = glass;
            x += PieceSpacing;
            built++;
        }
        if (puddleProp.objectReferenceValue == null)
        {
            NoisySurface puddle = BuildFloorPatch(root.transform, new Vector3(x, 0.01f, 0f), "Puddle", glass: false);
            puddleProp.objectReferenceValue = puddle;
            x += PieceSpacing;
            built++;
        }
        if (lanternProp.objectReferenceValue == null)
        {
            Lantern lantern = BuildLantern(root.transform, new Vector3(x, 0.2f, 0f));
            lanternProp.objectReferenceValue = lantern;
            x += PieceSpacing;
            built++;
        }
        if (noteProp.objectReferenceValue == null)
        {
            LoreNote note = BuildLoreNote(root.transform, new Vector3(x, 1.2f, 0f));
            noteProp.objectReferenceValue = note;
            x += PieceSpacing;
            built++;
        }
        if (bellProp.objectReferenceValue == null)
        {
            ThemeInteractable bell = BuildCallBell(root.transform, new Vector3(x, 1.6f, 0f));
            bellProp.objectReferenceValue = bell;
            x += PieceSpacing;
            built++;
        }
        if (valveProp.objectReferenceValue == null)
        {
            ThemeInteractable valve = BuildSteamValve(root.transform, new Vector3(x, 1f, 0f));
            valveProp.objectReferenceValue = valve;
            x += PieceSpacing;
            built++;
        }
        if (candlesProp.objectReferenceValue == null)
        {
            ThemeInteractable candles = BuildCandles(root.transform, new Vector3(x, 0.5f, 0f));
            candlesProp.objectReferenceValue = candles;
            x += PieceSpacing;
            built++;
        }
        if (terminalProp.objectReferenceValue == null)
        {
            ThemeInteractable terminal = BuildTerminal(root.transform, new Vector3(x, 0.6f, 0f));
            terminalProp.objectReferenceValue = terminal;
            x += PieceSpacing;
            built++;
        }

        // F75: same build-missing-only rule.
        if (stalkerProp.objectReferenceValue == null)
        {
            Stalker stalker = BuildStalkerTemplate(root.transform, new Vector3(x, 0f, 0f));
            if (stalker != null)
            {
                stalkerProp.objectReferenceValue = stalker;
                x += PieceSpacing;
                built++;
            }
        }

        // F77: same build-missing-only rule, after the stalker block.
        if (keyProp.objectReferenceValue == null)
        {
            KeyPickup key = BuildKeyPickup(root.transform, new Vector3(x, 1.3f, 0f));
            keyProp.objectReferenceValue = key;
            x += PieceSpacing;
            built++;
        }

        // F79: bottle look-alikes (optional - an empty array is valid), same build-missing-only rule.
        if (variantsProp.arraySize == 0)
        {
            List<ThrowableItem> variants = new List<ThrowableItem>();
            foreach (BottleVariant spec in BottleVariants)
            {
                ThrowableItem variant = BuildBottleVariant(root.transform, new Vector3(x, 0.15f, 0f), spec);
                if (variant == null) continue;
                variants.Add(variant);
                x += PieceSpacing;
                built++;
            }
            variantsProp.arraySize = variants.Count;
            for (int i = 0; i < variants.Count; i++) variantsProp.GetArrayElementAtIndex(i).objectReferenceValue = variants[i];
        }

        // F80: the escape hatch's trapdoor, same build-missing-only rule. Optional - without the pack
        // the slot stays empty and MazeEscape keeps its glowing slab.
        SerializedProperty hatchProp = so.FindProperty("escapeHatch");
        if (hatchProp.objectReferenceValue == null)
        {
            GameObject hatch = BuildEscapeHatch(root.transform, new Vector3(x, 0f, 0f));
            if (hatch != null)
            {
                hatchProp.objectReferenceValue = hatch;
                x += PieceSpacing;
                built++;
            }
        }

        // F111: the exit gate (primitives and the kit's own materials, no pack, so never null), same build-missing-only rule. Not optional:
        // without it MazeGenerator.ChooseGates logs an error naming this menu item and the floor keeps its hatch.
        SerializedProperty gateProp = so.FindProperty("escapeGate");
        if (gateProp.objectReferenceValue == null)
        {
            gateProp.objectReferenceValue = BuildEscapeGate(root.transform, new Vector3(x, 0f, 0f));
            x += PieceSpacing;
            built++;
        }

        // F90: the currency shard's model (a pack item), same build-missing-only rule. Optional - without the pack the slot stays
        // empty and MazeGenerator.BuildShard keeps its cube.
        SerializedProperty shardProp = so.FindProperty("shardModel");
        if (shardProp.objectReferenceValue == null)
        {
            GameObject shardModel = BuildShardModel(root.transform, new Vector3(x, 0.5f, 0f));
            if (shardModel != null)
            {
                shardProp.objectReferenceValue = shardModel;
                x += PieceSpacing;
                built++;
            }
        }

        // Ground fog: the maze's fog-bank emitter (the hub fog's look, AsylumHubBuilder.ConfigureFogParticles), same build-missing-only
        // rule. Needs the Fog Particles pack's smoke sheet; without it the slot stays empty and the mazes have no fog.
        SerializedProperty fogProp = so.FindProperty("fogBank");
        if (fogProp.objectReferenceValue == null)
        {
            Material fogMat = AsylumHubBuilder.FogMaterial();
            if (fogMat != null)
            {
                GameObject fog = new GameObject("FogBank");
                fog.transform.SetParent(root.transform, false);
                fog.transform.localPosition = new Vector3(x, 0f, 0f);
                fogProp.objectReferenceValue = AsylumHubBuilder.ConfigureFogParticles(fog, 12, fogMat);
                x += PieceSpacing;
                built++;
            }
        }

        // F93: the patient case file, same build-missing-only rule. Needs the Morgue Room PBR notebook and the Alchemist shelf; without them
        // the slot stays empty and MazeGenerator.BuildPatientFile logs an error naming this menu item.
        SerializedProperty fileProp = so.FindProperty("patientFile");
        if (fileProp.objectReferenceValue == null)
        {
            LoreNote patientFile = BuildPatientFile(root.transform, new Vector3(x, 1.2f, 0f));
            if (patientFile != null)
            {
                fileProp.objectReferenceValue = patientFile;
                x += PieceSpacing;
                built++;
            }
        }

        // F107: the DESCENT item cache, same build-missing-only rule. Needs the Alchemist trunk; without it the slot stays empty and
        // MazeGenerator.BuildItemCaches logs an error naming this menu item (no primitive fallback, CLAUDE.md F89).
        SerializedProperty cacheProp = so.FindProperty("itemCache");
        if (cacheProp.objectReferenceValue == null)
        {
            DescentItem cache = BuildItemCache(root.transform, new Vector3(x, 0f, 0f));
            if (cache != null)
            {
                cacheProp.objectReferenceValue = cache;
                x += PieceSpacing;
                built++;
            }
        }

        // Night Nurse (Sister Agnes), same build-missing-only rule.
        SerializedProperty nightNurseProp = so.FindProperty("nightNurse");
        if (nightNurseProp != null && nightNurseProp.objectReferenceValue == null)
        {
            NightNurse nurse = BuildNightNurseTemplate(root.transform, new Vector3(x, 0f, 0f));
            if (nurse != null)
            {
                nightNurseProp.objectReferenceValue = nurse;
                x += PieceSpacing;
                built++;
            }
        }

        // Dark Bruno (The Butcher), same build-missing-only rule.
        SerializedProperty darkBrunoProp = so.FindProperty("darkBruno");
        if (darkBrunoProp != null && darkBrunoProp.objectReferenceValue == null)
        {
            DarkBruno butcher = BuildDarkBrunoTemplate(root.transform, new Vector3(x, 0f, 0f));
            if (butcher != null)
            {
                darkBrunoProp.objectReferenceValue = butcher;
                x += PieceSpacing;
                built++;
            }
        }

        // Elias ("The Frost" / Subject #5), same build-missing-only rule.
        SerializedProperty eliasProp = so.FindProperty("elias");
        if (eliasProp != null && eliasProp.objectReferenceValue == null)
        {
            Elias elias = BuildEliasTemplate(root.transform, new Vector3(x, 0f, 0f));
            if (elias != null)
            {
                eliasProp.objectReferenceValue = elias;
                x += PieceSpacing;
                built++;
            }
        }

        // F120: the dropped torch, same build-missing-only rule.
        SerializedProperty droppedTorchProp = so.FindProperty("droppedTorch");
        if (droppedTorchProp.objectReferenceValue == null)
        {
            droppedTorchProp.objectReferenceValue = BuildDroppedTorch(root.transform, new Vector3(x, 0.1f, 0f));
            x += PieceSpacing;
            built++;
        }

        // F123: the wing nightmares' two templates, same build-missing-only rule. A missing pack model logs an error and leaves the slot empty.
        SerializedProperty bankProp = so.FindProperty("morgueDrawerBank");
        if (bankProp.objectReferenceValue == null)
        {
            MorgueDrawerBank bank = BuildMorgueDrawerBank(root.transform, new Vector3(x, 0f, 0f));
            if (bank != null)
            {
                bankProp.objectReferenceValue = bank;
                x += PieceSpacing;
                built++;
            }
        }
        SerializedProperty ampouleProp = so.FindProperty("nightmareAmpoule");
        if (ampouleProp.objectReferenceValue == null)
        {
            NightmareAmpoule ampoule = BuildNightmareAmpoule(root.transform, new Vector3(x, 0f, 0f));
            if (ampoule != null)
            {
                ampouleProp.objectReferenceValue = ampoule;
                x += PieceSpacing;
                built++;
            }
        }
        so.ApplyModifiedPropertiesWithoutUndo();
        ThemeSurfaceUpgrade.Apply(); // F88/F90: the fixture materials (shutter, bell, valve) get their texture sets
        AssetDatabase.SaveAssets();

        Selection.activeGameObject = root;
        SceneView.lastActiveSceneView?.FrameSelected();
        EditorSceneManager.MarkSceneDirty(scene);

        if (built == 0)
        {
            Debug.Log("Interactables kit already complete - nothing built.", root);
        }
        else
        {
            Debug.Log($"Interactables kit: built {built} missing template(s) at {GalleryOrigin}. Save the scene (Ctrl+S) to keep them.", root);
        }
    }

    /// <summary>
    /// KEEP IT LIT &gt; Build &gt; Re-skin Templates &gt; Key, Fuse Box, Button. Build() is build-missing-only, so
    /// swapping a template's model (the Asset Store meshes and the kit lever, 4 Oct 2026) needs its slot
    /// emptied first: this destroys the current KeyPickup, FuseBox and WallButton gallery objects, clears
    /// the three slots and runs Build(), which re-creates just those at the end of the row. Save the
    /// scene afterwards.
    /// </summary>
    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Key, Fuse Box, Button", priority = 150)]
    public static void RebuildModelTemplates()
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit == null)
        {
            Build();
            return;
        }

        SerializedObject so = new SerializedObject(kit);
        foreach (string slot in new[] { "keyPickup", "fuseBox", "wallButton" })
        {
            SerializedProperty prop = so.FindProperty(slot);
            Component old = prop.objectReferenceValue as Component;
            prop.objectReferenceValue = null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        Build();
    }

    /// <summary>
    /// KEEP IT LIT &gt; Build &gt; Re-skin Templates &gt; Stalker (Stan). Empties the stalker slot on InteractableKit
    /// and runs Build() so the template is recreated from Assets/SourceFiles/Models/Stan/Stan_Model.fbx.
    /// </summary>
    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Stalker (Stan)", priority = 151)]
    public static void RebuildStalkerTemplate()
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit == null)
        {
            Build();
            return;
        }

        SerializedObject so = new SerializedObject(kit);
        SerializedProperty prop = so.FindProperty("stalker");
        Component old = prop.objectReferenceValue as Component;
        prop.objectReferenceValue = null;
        if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        so.ApplyModifiedPropertiesWithoutUndo();

        Build();
    }

    /// <summary>
    /// F90: brings an existing kit to the new floor-patch looks, the shelved lore note and the pack-model shard: the glassPatch, puddle,
    /// loreNote and shardModel slots are emptied (their gallery objects destroyed) and Build() re-creates just those. No dialogs, no menu
    /// item - RemainingPrimitivesCleanup drives it. Save the scene afterwards.
    /// </summary>
    public static void RebuildF90Templates()
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit == null) { Build(); return; }

        if (AlchemistMaterialUpgrade.NeedsConversion()) AlchemistMaterialUpgrade.Convert();

        SerializedObject so = new SerializedObject(kit);
        foreach (string slot in new[] { "glassPatch", "puddle", "loreNote" })
        {
            SerializedProperty prop = so.FindProperty(slot);
            Component old = prop.objectReferenceValue as Component;
            prop.objectReferenceValue = null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }
        SerializedProperty shard = so.FindProperty("shardModel");
        GameObject oldShard = shard.objectReferenceValue as GameObject;
        shard.objectReferenceValue = null;
        if (oldShard != null) Undo.DestroyObjectImmediate(oldShard);
        so.ApplyModifiedPropertiesWithoutUndo();

        Build();
    }

    /// <summary>
    /// KEEP IT LIT &gt; Build &gt; Re-skin Templates &gt; Bottle, Can, Lantern, Candles, Note. F79: re-skins five
    /// templates with Alchemist House pack models (and adds six bottle look-alikes). Same pattern as
    /// RebuildModelTemplates - the gallery objects in those slots (and every bottleVariants entry) are
    /// destroyed, the slots cleared and Build() re-creates just those at the end of the row. No dialogs.
    /// Save the scene afterwards.
    /// </summary>
    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Bottle, Can, Lantern, Candles, Note", priority = 151)]
    public static void RebuildAlchemistTemplates()
    {
        InteractableKit kit = Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include);
        if (kit == null)
        {
            Build();
            return;
        }

        if (AlchemistMaterialUpgrade.NeedsConversion()) AlchemistMaterialUpgrade.Convert();

        SerializedObject so = new SerializedObject(kit);
        foreach (string slot in new[] { "throwableBottle", "throwableCan", "lantern", "candles", "loreNote" })
        {
            SerializedProperty prop = so.FindProperty(slot);
            Component old = prop.objectReferenceValue as Component;
            prop.objectReferenceValue = null;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }

        SerializedProperty variants = so.FindProperty("bottleVariants");
        for (int i = 0; i < variants.arraySize; i++)
        {
            Component old = variants.GetArrayElementAtIndex(i).objectReferenceValue as Component;
            if (old != null) Undo.DestroyObjectImmediate(old.gameObject);
        }
        variants.arraySize = 0;
        so.ApplyModifiedPropertiesWithoutUndo();

        Build();
    }

    /// <summary>One slot past the right-most existing template, so a rebuilt or later-slice template never lands on an older one.</summary>
    private static float NextFreeX(Transform root)
    {
        float max = -PieceSpacing;
        for (int i = 0; i < root.childCount; i++)
        {
            max = Mathf.Max(max, root.GetChild(i).localPosition.x);
        }
        return max + PieceSpacing;
    }

    // ---------------------------------------------------------------- shared helpers (delegating to Shared/)

    private static void EnsureFolder() => EditorAssetUtil.EnsureFolder(MaterialFolder);

    private static GameObject Primitive(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 localScale,
        Material material, bool keepCollider, Quaternion? localRotation = null)
        => BuilderPrimitives.Primitive(parent, type, name, localPosition, localScale, material, keepCollider, localRotation);

    /// <summary>The built-in cylinder is 2 units tall with a 1-unit diameter at scale 1, so this converts a radius/height pair into that scale.</summary>
    private static GameObject CylinderRH(Transform parent, string name, Vector3 localPosition, float radius, float height, Material material, bool keepCollider)
        => BuilderPrimitives.CylinderRH(parent, name, localPosition, radius, height, material, keepCollider);

    /// <summary>Loaded if it already exists (hand edits survive a rebuild), otherwise created as a URP Lit material.</summary>
    private static Material LoadOrCreateMat(string name, Color color, Color? emission, float smoothness)
        => BuilderMaterials.LoadOrCreateMat(MaterialFolder, name, color, emission, smoothness);
}
