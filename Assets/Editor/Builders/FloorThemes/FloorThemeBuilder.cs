using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// KEEP IT LIT &gt; Build &gt; Floor Themes. Generates five floors' worth of themed set-dressing - materials
/// and prefabs under Assets/SourceFiles/Themes, and a scene gallery (FloorThemes) with one live prefab
/// instance of each piece per floor - so an artist can inspect and retouch every piece MazeGenerator
/// clones at Play time. Nothing here runs in a build; at Play time MazeGenerator.ResolveTheme just finds
/// what this made and clones the scene instances (see decision 1 in plannings/floor-themes-plan.md).
///
/// Rebuilding never silently loses work: materials and prefabs are loaded if they already exist
/// (LoadOrCreate, same pattern as ShopRoomBuilder), and the whole Themes folder is only ever deleted
/// when the artist explicitly picks "Replace everything".
/// </summary>
public static partial class FloorThemeBuilder
{
    private const string ThemesRoot = "Assets/SourceFiles/Themes";
    private const string GalleryName = "FloorThemes";
    private static readonly Vector3 GalleryOrigin = new Vector3(-70f, 5f, -6f);
    private const float RowSpacing = 12f;
    internal const float PieceSpacing = 6f;

    // Canonical authored sizes. WallPiece.ScaleFor divides by these; FloorTile/CeilingTile are scaled by
    // MazeGenerator using cellSize / TileSize. The gallery and SampleCell show every piece at scale 1.
    private const float WallHeight = 4f;
    private const float TileSize = 4.5f;

    // cellSize/2 - wallThickness/2 at the canonical 4.5 m cell / 0.5 m wall the pieces are authored for.
    private const float BackFaceDistance = 2.0f;

    /// <summary>
    /// How far a set piece's wall decal stands off the wall. MazeGenerator.BuildSetPieces puts a set piece's
    /// pivot ON the wall face with +Z into the cell, so a wall decal belongs just in front of z = 0. It used to
    /// be 1.98 (as if the wall were 2 m ahead), which hung every set-piece wall decal in mid-air - fixed 7 Oct 2026;
    /// SetPieceDecalFix moves the ones in prefabs built before that.
    /// </summary>
    internal const float WallDecalInset = 0.02f;

    private static Material _plinthMaterial;

    [MenuItem("KEEP IT LIT/Build/Floor Themes", priority = 100)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Build Floor Themes", "Open the game scene first.", "OK");
            return;
        }

        FloorThemeSet existingSet = UnityEngine.Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        if (existingSet != null)
        {
            if (!EditorUtility.DisplayDialog("Build Floor Themes",
                    "A FloorThemes gallery already exists in this scene. Replace it? Any changes you made to the gallery hierarchy itself (not the material/prefab assets) will be lost.",
                    "Replace", "Cancel"))
            {
                return;
            }
        }

        int choice = EditorUtility.DisplayDialogComplex("Build Floor Themes",
            "Keep the existing materials and prefabs under Assets/SourceFiles/Themes (any hand edits survive), or delete and rebuild everything?",
            "Keep existing assets (recommended)", "Cancel", "Replace everything");
        if (choice == 1) return; // Cancel

        BuildSilently(replaceAssets: choice == 2);
    }

    /// <summary>No dialogs: replaces any existing gallery, keeping the theme assets unless replaceAssets. Used by KEEP IT LIT &gt; Build Missing Pieces.</summary>
    public static void BuildSilently(bool replaceAssets = false)
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("Build Floor Themes: open the game scene first.");
            return;
        }

        FloorThemeSet existingSet = UnityEngine.Object.FindAnyObjectByType<FloorThemeSet>(FindObjectsInactive.Include);
        if (existingSet != null) Undo.DestroyObjectImmediate(existingSet.gameObject);
        if (replaceAssets && AssetDatabase.IsValidFolder(ThemesRoot)) AssetDatabase.DeleteAsset(ThemesRoot);
        EnsureFolder(ThemesRoot);
        // The cached plinth material, and every F70 dressing cache under Themes/Common, may point at an
        // asset the line above just deleted.
        _plinthMaterial = null;
        ResetDressingCaches();

        GameObject galleryRoot = new GameObject(GalleryName);
        Undo.RegisterCreatedObjectUndo(galleryRoot, "Build Floor Themes");
        galleryRoot.transform.position = GalleryOrigin;
        FloorThemeSet themeSet = galleryRoot.AddComponent<FloorThemeSet>();

        var builders = new Func<ThemeAssets>[] { BuildWard, BuildBoilerDeck, BuildCrypt, BuildLab, BuildHollow };
        var rowNames = new[] { "Floor1_Ward", "Floor2_BoilerDeck", "Floor3_Crypt", "Floor4_Lab", "Floor5_Hollow" };

        List<FloorTheme> builtThemes = new List<FloorTheme>();
        for (int i = 0; i < builders.Length; i++)
        {
            ThemeAssets assets = builders[i]();
            builtThemes.Add(BuildRow(galleryRoot.transform, rowNames[i], i, assets));
        }

        SerializedObject setSo = new SerializedObject(themeSet);
        SerializedProperty floorsProp = setSo.FindProperty("floors");
        floorsProp.arraySize = builtThemes.Count;
        for (int i = 0; i < builtThemes.Count; i++)
        {
            floorsProp.GetArrayElementAtIndex(i).objectReferenceValue = builtThemes[i];
        }
        setSo.ApplyModifiedPropertiesWithoutUndo();

        // The builders' ambient colours predate the F71 darkness pass; bring the fresh rows down to it.
        DarknessRelight.Relight();
        // The drawn blood decals are only placeholders when the Blood decal pack is in the project.
        BloodDecalUpgrade.Apply();
        SetPieceDecalFix.Apply();
        CorpseUpgrade.Apply();
        PackDressing.Apply(); // F85: any pack piece a hand-edited row or an older scene lacks
        PrimitiveDressingCleanup.Apply(); // F89: no untextured primitive dressing in an older or hand-edited row
        RemainingPrimitivesCleanup.Apply(); // F90: no untextured wall trims or plain pillars; kit patches / note / shard on their new looks
        ThemeSurfaceUpgrade.Apply(); // F88: real PBR surface textures on the structural theme materials

        AssetDatabase.SaveAssets();
        Selection.activeGameObject = galleryRoot;
        SceneView.lastActiveSceneView?.FrameSelected();
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("Floor themes built. Save the scene (Ctrl+S) to keep them.", galleryRoot);
    }

    // ---------------------------------------------------------------- per-theme data

    private class ThemeAssets
    {
        public string DisplayName;
        public Color Ambient;
        public Color FogColor;
        public float FogDensityScale = 1f;
        public Color LampColor;
        public float FaultyLampChance;
        public float WallPropChance;
        public float CeilingPropChance;
        public Material WallMaterial;
        public WallPiece Wall;
        public GameObject Pillar;
        public GameObject FloorTile;
        public GameObject CeilingTile;
        public WallLamp Lamp;
        public Locker Locker;
        public PropPiece[] Props;

        // F70.
        public float FloorPropChance;
        public float WallDecalChance;
        public float FloorDecalChance;
        public int MaxDecalsPerCell;
        public SetPiece[] SetPieces;
        public int SetPieceCount;
        public DustMotes Dust;
    }

    // ---------------------------------------------------------------- row assembly

    /// <summary>
    /// Builds one floor's gallery row: a showcase instance of every piece (each a real prefab instance,
    /// via PrefabUtility.InstantiatePrefab, so the artist can select and override them like any other
    /// scene object), a SampleCell mock-up, and the FloorTheme component whose fields point at the
    /// showcase instances - not the SampleCell copies, per decision 1 in the plan.
    /// </summary>
    private static FloorTheme BuildRow(Transform gallery, string rowName, int rowIndex, ThemeAssets assets)
    {
        GameObject rowGo = new GameObject(rowName);
        rowGo.transform.SetParent(gallery, false);
        rowGo.transform.localPosition = new Vector3(0f, 0f, rowIndex * RowSpacing);

        float x = 8f;
        WallPiece wallInstance = InstantiateShowcase(rowGo.transform, assets.Wall, ref x);
        GameObject pillarInstance = InstantiateShowcase(rowGo.transform, assets.Pillar, ref x);
        GameObject floorInstance = InstantiateShowcase(rowGo.transform, assets.FloorTile, ref x);
        GameObject ceilingInstance = InstantiateShowcase(rowGo.transform, assets.CeilingTile, ref x);
        WallLamp lampInstance = InstantiateShowcase(rowGo.transform, assets.Lamp, ref x);
        Locker lockerInstance = InstantiateShowcase(rowGo.transform, assets.Locker, ref x);

        List<PropPiece> propInstances = new List<PropPiece>();
        if (assets.Props != null)
        {
            foreach (PropPiece propPrefab in assets.Props)
            {
                propInstances.Add(InstantiateShowcase(rowGo.transform, propPrefab, ref x));
            }
        }

        // F70: set pieces need more room on their plinth than a single prop.
        List<SetPiece> setPieceInstances = new List<SetPiece>();
        if (assets.SetPieces != null)
        {
            foreach (SetPiece setPiecePrefab in assets.SetPieces)
            {
                // InstantiateShowcase already advances x by PieceSpacing; the extra half-step here
                // brings a set piece's total advance to PieceSpacing * 1.5, since it needs more room
                // than a single prop (plan section 3.5).
                setPieceInstances.Add(InstantiateShowcase(rowGo.transform, setPiecePrefab, ref x));
                x += PieceSpacing * 0.5f;
            }
        }

        DustMotes dustInstance = InstantiateShowcase(rowGo.transform, assets.Dust, ref x);

        BuildSampleCell(rowGo.transform, assets);

        FloorTheme theme = rowGo.AddComponent<FloorTheme>();
        SerializedObject so = new SerializedObject(theme);
        so.FindProperty("displayName").stringValue = assets.DisplayName;
        so.FindProperty("ambient").colorValue = assets.Ambient;
        so.FindProperty("fogColor").colorValue = assets.FogColor;
        so.FindProperty("fogDensityScale").floatValue = assets.FogDensityScale;
        so.FindProperty("lampColor").colorValue = assets.LampColor;
        so.FindProperty("faultyLampChance").floatValue = assets.FaultyLampChance;
        so.FindProperty("wall").objectReferenceValue = wallInstance;
        so.FindProperty("pillar").objectReferenceValue = pillarInstance;
        so.FindProperty("floorTile").objectReferenceValue = floorInstance;
        so.FindProperty("ceilingTile").objectReferenceValue = ceilingInstance;
        so.FindProperty("lamp").objectReferenceValue = lampInstance;
        so.FindProperty("locker").objectReferenceValue = lockerInstance;

        SerializedProperty propsProp = so.FindProperty("props");
        propsProp.arraySize = propInstances.Count;
        for (int i = 0; i < propInstances.Count; i++)
        {
            propsProp.GetArrayElementAtIndex(i).objectReferenceValue = propInstances[i];
        }

        so.FindProperty("wallPropChance").floatValue = assets.WallPropChance;
        so.FindProperty("ceilingPropChance").floatValue = assets.CeilingPropChance;
        so.FindProperty("wallMaterial").objectReferenceValue = assets.WallMaterial;

        // F70.
        so.FindProperty("floorPropChance").floatValue = assets.FloorPropChance;
        so.FindProperty("wallDecalChance").floatValue = assets.WallDecalChance;
        so.FindProperty("floorDecalChance").floatValue = assets.FloorDecalChance;
        so.FindProperty("maxDecalsPerCell").intValue = assets.MaxDecalsPerCell;

        SerializedProperty setPiecesProp = so.FindProperty("setPieces");
        setPiecesProp.arraySize = setPieceInstances.Count;
        for (int i = 0; i < setPieceInstances.Count; i++)
        {
            setPiecesProp.GetArrayElementAtIndex(i).objectReferenceValue = setPieceInstances[i];
        }
        so.FindProperty("setPieceCount").intValue = assets.SetPieceCount;
        so.FindProperty("dust").objectReferenceValue = dustInstance;

        so.ApplyModifiedPropertiesWithoutUndo();

        return theme;
    }

    /// <summary>Instantiates a prefab as a showcase piece in the row, on a small dark plinth so it reads clearly in the Scene view, and advances x by PieceSpacing.</summary>
    private static T InstantiateShowcase<T>(Transform row, T prefab, ref float x) where T : Component
    {
        if (prefab == null) return null;
        GameObject instance = InstantiateShowcaseGo(row, prefab.gameObject, ref x);
        return instance.GetComponent<T>();
    }

    /// <summary>GameObject-only overload for pieces with no distinguishing component of their own (Pillar, FloorTile, CeilingTile).</summary>
    private static GameObject InstantiateShowcase(Transform row, GameObject prefab, ref float x)
    {
        return prefab == null ? null : InstantiateShowcaseGo(row, prefab, ref x);
    }

    internal static GameObject InstantiateShowcaseGo(Transform row, GameObject prefab, ref float x)
    {
        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, row);
        instance.transform.localPosition = new Vector3(x, 0f, 0f);
        instance.name = prefab.name;

        GameObject plinth = GameObject.CreatePrimitive(PrimitiveType.Cube);
        plinth.name = "Plinth";
        plinth.transform.SetParent(row, false);
        plinth.transform.localPosition = new Vector3(x, -0.05f, 0f);
        plinth.transform.localScale = new Vector3(2.2f, 0.1f, 2.2f);
        UnityEngine.Object.DestroyImmediate(plinth.GetComponent<Collider>());
        plinth.GetComponent<MeshRenderer>().sharedMaterial = PlinthMaterial();

        x += PieceSpacing;
        return instance;
    }

    /// <summary>
    /// A real asset (Assets/SourceFiles/Themes/Materials/GalleryPlinth.mat), not an in-memory material:
    /// the plinths are scene objects, and a scene that references a material that exists only in memory
    /// loses it on reload and shows pink.
    /// </summary>
    private static Material PlinthMaterial()
    {
        if (_plinthMaterial != null) return _plinthMaterial;

        _plinthMaterial = LoadOrCreateMat(ThemesRoot, "GalleryPlinth", new Color(0.03f, 0.03f, 0.03f), null, null, Vector2.one, null);
        return _plinthMaterial;
    }

    /// <summary>A one-cell mock-up assembled from a second set of prefab instances (kept separate from the row's showcase instances, which is what FloorTheme actually points at) so the artist can see how the pieces read together as a corridor corner.</summary>
    private static void BuildSampleCell(Transform row, ThemeAssets assets)
    {
        Transform cell = Group(row, "SampleCell");
        cell.localPosition = Vector3.zero;

        const float half = TileSize * 0.5f;
        const float lampHeight = 2.6f;

        if (assets.FloorTile != null)
        {
            GameObject floor = (GameObject)PrefabUtility.InstantiatePrefab(assets.FloorTile, cell);
            floor.transform.localPosition = Vector3.zero;
            floor.name = "FloorTile";
        }

        if (assets.CeilingTile != null)
        {
            GameObject ceiling = (GameObject)PrefabUtility.InstantiatePrefab(assets.CeilingTile, cell);
            ceiling.transform.localPosition = new Vector3(0f, WallHeight, 0f);
            ceiling.name = "CeilingTile";
        }

        if (assets.Wall != null)
        {
            GameObject southWall = (GameObject)PrefabUtility.InstantiatePrefab(assets.Wall.gameObject, cell);
            southWall.transform.localPosition = new Vector3(0f, 0f, -half);
            southWall.transform.localRotation = Quaternion.identity;
            southWall.name = "SouthWall";

            GameObject westWall = (GameObject)PrefabUtility.InstantiatePrefab(assets.Wall.gameObject, cell);
            westWall.transform.localPosition = new Vector3(-half, 0f, 0f);
            westWall.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
            westWall.name = "WestWall";
        }

        if (assets.Pillar != null)
        {
            GameObject pillar = (GameObject)PrefabUtility.InstantiatePrefab(assets.Pillar, cell);
            pillar.transform.localPosition = new Vector3(-half, 0f, -half);
            pillar.transform.localScale = new Vector3(1f, WallHeight / 4f, 1f);
            pillar.name = "Pillar";
        }

        if (assets.Lamp != null)
        {
            GameObject lamp = (GameObject)PrefabUtility.InstantiatePrefab(assets.Lamp.gameObject, cell);
            Vector3 wallDir = Vector3.left; // on the west wall
            lamp.transform.localPosition = wallDir * (BackFaceDistance - 0.08f) + Vector3.up * lampHeight;
            lamp.transform.localRotation = Quaternion.LookRotation(-wallDir, Vector3.up);
            lamp.name = "Lamp";
        }

        if (assets.Locker != null)
        {
            GameObject locker = (GameObject)PrefabUtility.InstantiatePrefab(assets.Locker.gameObject, cell);
            Vector3 openDir = Vector3.forward; // on the south wall, door faces into the cell
            locker.transform.localPosition = -openDir * BackFaceDistance;
            locker.transform.localRotation = Quaternion.LookRotation(openDir, Vector3.up);
            locker.name = "Locker";
        }

        if (assets.Props != null)
        {
            foreach (PropPiece prop in assets.Props)
            {
                if (prop == null || prop.Mount != PropPiece.MountKind.Wall) continue;

                GameObject propInstance = (GameObject)PrefabUtility.InstantiatePrefab(prop.gameObject, cell);
                Vector3 dir = Vector3.left; // west wall, offset along +Z so it clears the lamp
                propInstance.transform.localPosition = dir * BackFaceDistance + Vector3.forward * 1.3f;
                propInstance.transform.localRotation = Quaternion.LookRotation(-dir);
                propInstance.name = "Prop";
                break; // one wall prop is enough for the mock-up
            }

            // F70: one floor prop (south wall) and one wall decal (west wall, above the wall prop) so the
            // mock-up also shows the new mount kinds reading together in a corner.
            foreach (PropPiece prop in assets.Props)
            {
                if (prop == null || prop.Mount != PropPiece.MountKind.Floor) continue;

                GameObject floorPropInstance = (GameObject)PrefabUtility.InstantiatePrefab(prop.gameObject, cell);
                Vector3 dir = Vector3.back; // south wall
                floorPropInstance.transform.localPosition = dir * BackFaceDistance + Vector3.right * 1.0f;
                floorPropInstance.transform.localRotation = Quaternion.LookRotation(-dir);
                floorPropInstance.name = "FloorProp";
                break;
            }

            foreach (PropPiece prop in assets.Props)
            {
                if (prop == null || prop.Mount != PropPiece.MountKind.WallDecal) continue;

                GameObject decalInstance = (GameObject)PrefabUtility.InstantiatePrefab(prop.gameObject, cell);
                Vector3 dir = Vector3.left; // west wall
                float y = Mathf.Clamp(1.6f, prop.DecalSizeRange.y * 0.5f + 0.3f, 2.2f - prop.DecalSizeRange.y * 0.5f);
                decalInstance.transform.localPosition = dir * (BackFaceDistance - 0.04f) + Vector3.forward * -1.0f + Vector3.up * y;
                decalInstance.transform.localRotation = Quaternion.LookRotation(-dir);
                decalInstance.transform.localScale = Vector3.one * prop.DecalSizeRange.y;
                decalInstance.name = "WallDecal";
                break;
            }
        }
    }
}
