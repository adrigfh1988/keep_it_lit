using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// KEEP IT LIT > Build > Shop Room (regenerate). Generates the first draft of the room between floors and saves it as
/// the hand-editable prefab Assets/Prefabs/ShopRoom.prefab (ShopRoomBuilder.Prefab.cs); from then on the prefab is
/// the source of truth, BuildSilently only places it, and the menu item is a confirmed reset. Nothing here runs in a
/// build; at Play time the ShopRoom component just drives what this made.
///
/// The room is an alchemist's shop dressed from the Assets/BK_AlchemistHouse pack (its materials are
/// converted to URP by AlchemistMaterialUpgrade). The shell, lights, door, counter and salesman live in
/// this file; the placement helpers and the furniture passes are in ShopRoomBuilder.Dressing.cs.
///
/// Spec: plannings/shop-room-alchemist-plan.md.
/// </summary>
public static partial class ShopRoomBuilder
{
    private const string RoomName = "ShopRoom";
    private const string MaterialFolder = "Assets/SourceFiles/Materials/Shop";

    // Where the room sits, west of the 11x11 maze (which spans x -70..-20 and z -70..-20). Floor top of
    // the maze is y = 5.0. It must stay above y = -5 or RespawnPlayer bounces the player out of it.
    private static readonly Vector3 RoomPosition = new Vector3(-92f, 5f, -45f);

    private const float HalfX = 4.5f;        // inner half-width  (3 modules x 3 m)
    private const float HalfZ = 6f;          // inner half-depth  (4 modules x 3 m)
    private const float RoomHeight = 3f;
    private const float Module = 3f;         // 2.5 m pack module x ArchScale
    private const float ArchScale = 1.2f;

    private static readonly Color HatchGreen = new Color(0.25f, 1f, 0.7f);
    private static readonly Color Amber = new Color(1f, 0.72f, 0.42f);

    /// <summary>
    /// Generates the room from scratch and saves it over the prefab - a reset, not the way to change the room. To change
    /// it, open Assets/Prefabs/ShopRoom.prefab and edit it by hand.
    /// </summary>
    [MenuItem("KEEP IT LIT/Build/Shop Room (regenerate)", priority = 104)]
    public static void Build()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            EditorUtility.DisplayDialog("Build Shop Room", "Open the game scene first.", "OK");
            return;
        }

        bool hasPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        if ((hasPrefab || Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include) != null) &&
            !EditorUtility.DisplayDialog("Regenerate Shop Room",
                $"This throws away the current shop and regenerates it from code, overwriting {PrefabPath}.\n\n" +
                "Every change you made to it by hand will be lost. To change the shop, edit the prefab instead.",
                "Regenerate", "Cancel"))
        {
            return;
        }

        if (AlchemistMaterialUpgrade.NeedsConversion())
        {
            if (!EditorUtility.DisplayDialog("Build Shop Room",
                    "The Alchemist House materials are still on Built-in shaders and would render magenta. Convert them to URP now? " +
                    "This edits the materials under Assets/BK_AlchemistHouse in place.",
                    "Convert", "Cancel"))
            {
                return;
            }
        }

        Generate();
    }

    /// <summary>
    /// No dialogs, never overwrites hand work: when the prefab exists and the room is missing, it places the prefab;
    /// it only generates from code when there is no prefab yet. Used by ProjectSetup and for scripted rebuilds.
    /// </summary>
    public static void BuildSilently()
    {
        Scene scene = SceneManager.GetActiveScene();
        if (!scene.IsValid() || !scene.isLoaded)
        {
            Debug.LogError("Build Shop Room: open the game scene first.");
            return;
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null)
        {
            if (Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include) == null)
            {
                InstantiatePrefab();
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return;
        }

        Generate();
    }

    /// <summary>Generates the room from code (replacing any existing one, converting the pack materials if needed) and saves it as the prefab.</summary>
    private static void Generate()
    {
        Scene scene = SceneManager.GetActiveScene();
        ShopRoom existing = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (existing != null) Undo.DestroyObjectImmediate(existing.gameObject);

        // A previous build that died half-way leaves the root behind without its component.
        GameObject stale;
        while ((stale = GameObject.Find(RoomName)) != null) Undo.DestroyObjectImmediate(stale);

        if (AlchemistMaterialUpgrade.NeedsConversion()) AlchemistMaterialUpgrade.Convert();

        BuildCore(scene);

        ShopRoom built = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (built != null) WorldSignBuilder.RestyleShopRoot(built.gameObject, out int _, out int _); // F109: a regenerate keeps the restyled signs
        if (built != null) SaveAsPrefab(built.gameObject);
    }

    private static void BuildCore(Scene scene)
    {
        EnsureFolder();
        Materials mats = LoadMaterials();
        UsedPrefabs.Clear();
        KeptLights.Clear();

        GameObject root = new GameObject(RoomName);
        Undo.RegisterCreatedObjectUndo(root, "Build Shop Room");
        root.transform.position = RoomPosition;
        _room = root.transform;

        BuildShell(root.transform);
        BuildBackdrop(root.transform, mats.Wall);
        BuildColliders(root.transform);
        Transform doorHinge = BuildDoor(root.transform);
        GameObject[] counterTables = BuildCounter(root.transform);
        Transform head = BuildSalesman(root.transform, out ShopkeeperAnimator shopkeeper);
        DressCounter(Group(root.transform, "CounterTop"), counterTables);
        BuildBackWall(root.transform);
        BuildLabSide(root.transform);
        BuildLibrarySide(root.transform);
        BuildCorners(root.transform);
        BuildCarpets(root.transform);
        BuildLights(root.transform);

        TextMeshPro counterSign = BuildSign(root.transform, "ShopSign", new Vector3(0f, 2.45f, 3.6f), Quaternion.identity, "SHARDS ACCEPTED", Amber, 8f, 3.5f);
        TextMeshPro doorSign = BuildSign(root.transform, "DoorSign", new Vector3(0f, 2.68f, -5.92f), Quaternion.Euler(0f, 180f, 0f), "FLOOR 2", HatchGreen, 6f, 3f);

        Transform points = new GameObject("Points").transform;
        points.SetParent(root.transform, false);

        Transform arrival = new GameObject("Arrival").transform;
        arrival.SetParent(points, false);
        arrival.localPosition = new Vector3(0f, 0f, -1.5f);
        arrival.localRotation = Quaternion.identity; // facing +Z, toward the counter

        Collider counterZone = BuildZone(points, "CounterZone", new Vector3(0f, 1.25f, 2.4f), new Vector3(4.5f, 2.5f, 2.2f));
        Collider doorZone = BuildZone(points, "DoorZone", new Vector3(0f, 1.25f, -4.9f), new Vector3(2.6f, 2.5f, 2.0f));

        Collider contractZone = BuildContractBoard(root.transform); // F87

        ShopRoom room = root.AddComponent<ShopRoom>();
        SerializedObject so = new SerializedObject(room);
        so.FindProperty("arrivalPoint").objectReferenceValue = arrival;
        so.FindProperty("counterZone").objectReferenceValue = counterZone;
        so.FindProperty("doorZone").objectReferenceValue = doorZone;
        so.FindProperty("headPivot").objectReferenceValue = head;
        so.FindProperty("shopkeeper").objectReferenceValue = shopkeeper;
        so.FindProperty("counterSign").objectReferenceValue = counterSign;
        so.FindProperty("doorSign").objectReferenceValue = doorSign;
        so.FindProperty("doorHinge").objectReferenceValue = doorHinge;
        so.FindProperty("contractZone").objectReferenceValue = contractZone;
        so.ApplyModifiedPropertiesWithoutUndo();

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log($"Shop room built ({UsedPrefabs.Count} pack prefabs). Save the scene (Ctrl+S) to keep it.\n{Audit(root.transform)}", root);
    }

    // ---------------------------------------------------------------- pieces

    /// <summary>The salesman stands behind the counter facing -Z (the room). Returns the head pivot the component turns.</summary>
    /// <remarks>
    /// The salesman is the hand-editable prefab Assets/Prefabs/ShopSalesman.prefab (Timmy, the player's own robot, on a
    /// small controller of his own: idle, Talk while the menu is open, Wave on arrival; see ShopRoomBuilder.Timmy.cs).
    /// The first build generates the prefab; every build after that only places it. The head pivot is his Humanoid
    /// Head bone: ShopRoom turns it in LateUpdate, on top of whatever the clip did that frame.
    /// </remarks>
    private static Transform BuildSalesman(Transform root, out ShopkeeperAnimator shopkeeper)
    {
        Transform salesman = PlaceSalesman(root);
        return SalesmanParts(salesman, out shopkeeper);
    }

    private static TextMeshPro BuildSign(Transform root, string name, Vector3 localPosition, Quaternion localRotation, string text, Color color, float spacing, float fontSize)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(root, false);
        go.transform.localPosition = localPosition;
        go.transform.localRotation = localRotation;

        TextMeshPro tmp = go.AddComponent<TextMeshPro>();
        if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
        tmp.text = text;
        tmp.fontSize = fontSize;
        tmp.color = color;
        tmp.characterSpacing = spacing;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.rectTransform.sizeDelta = new Vector2(10f, 2f);
        return tmp;
    }

    private static Collider BuildZone(Transform parent, string name, Vector3 localCenter, Vector3 size)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localCenter;

        BoxCollider box = go.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = size;
        return box;
    }

    // ---------------------------------------------------------------- door hinge check & repair

    public static string DoorHingeProblem()
    {
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null) return null; // reported by "Shop room"
        SerializedProperty hinge = new SerializedObject(shop).FindProperty("doorHinge");
        Transform pivot = hinge != null ? hinge.objectReferenceValue as Transform : null;
        if (pivot == null) return FindShopDoorLeaf(shop) != null ? "no doorHinge wired" : null;
        if (IsStaticAnywhere(pivot)) return "the door leaf is Static - static batching freezes it shut";
        return FindFusedDoorModel(shop) != null ? "the pack's fused Door model (frame + closed panel) hides the swinging leaf" : null;
    }

    /// <summary>Gives an already-built shop room what Build &gt; Shop Room now makes: a hinge pivot on the leaf, wired into ShopRoom, nothing under it Static.</summary>
    public static void FixShopDoorHinge()
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
        MakeMovable(hinge);

        GameObject fused = FindFusedDoorModel(shop);
        if (fused != null) Undo.DestroyObjectImmediate(fused);

        EditorSceneManager.MarkSceneDirty(shop.gameObject.scene);
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

    // ---------------------------------------------------------------- helpers (delegating to Shared/)

    private static Transform Group(Transform parent, string name) => BuilderPrimitives.Group(parent, name);

    private static GameObject Primitive(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider = false)
        => BuilderPrimitives.Primitive(parent, type, name, localPosition, scale, material, keepCollider);

    private static void PointLight(Transform parent, string name, Vector3 localPosition, Color color, float range, float intensity)
        => BuilderPrimitives.PointLight(parent, name, localPosition, color, range, intensity);

    // ---------------------------------------------------------------- materials

    // The wall backdrop is the only primitive left. The old Shop_Floor/Ceiling/Counter/Door materials stay on disk, unused.
    private struct Materials
    {
        public Material Wall;
    }

    private static void EnsureFolder() => EditorAssetUtil.EnsureFolder(MaterialFolder);

    private static Materials LoadMaterials()
    {
        Materials m = new Materials
        {
            Wall = LoadOrCreate("Shop_Wall", new Color(0.22f, 0.22f, 0.25f), null),
        };
        AssetDatabase.SaveAssets();
        return m;
    }

    private static Material LoadOrCreate(string name, Color color, Color? emission)
        => BuilderMaterials.LoadOrCreateMat(MaterialFolder, name, color, emission, 0.15f);
}
