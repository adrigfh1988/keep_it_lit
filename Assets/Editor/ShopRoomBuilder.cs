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

    // ---------------------------------------------------------------- helpers

    private static Transform Group(Transform parent, string name)
    {
        Transform t = new GameObject(name).transform;
        t.SetParent(parent, false);
        return t;
    }

    private static GameObject Primitive(Transform parent, PrimitiveType type, string name, Vector3 localPosition, Vector3 scale, Material material, bool keepCollider = false)
    {
        GameObject go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;
        go.transform.localScale = scale;
        if (material != null) go.GetComponent<MeshRenderer>().sharedMaterial = material;

        if (!keepCollider)
        {
            Collider collider = go.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
        }
        return go;
    }

    private static void PointLight(Transform parent, string name, Vector3 localPosition, Color color, float range, float intensity)
    {
        GameObject go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = localPosition;

        Light light = go.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.range = range;
        light.intensity = intensity;
        // The flashlight is the only shadow caster in this game; a shadowed point light costs six atlas faces.
        light.shadows = LightShadows.None;
    }

    // ---------------------------------------------------------------- materials

    // The wall backdrop is the only primitive left. The old Shop_Floor/Ceiling/Counter/Door materials stay on disk, unused.
    private struct Materials
    {
        public Material Wall;
    }

    private static void EnsureFolder()
    {
        if (AssetDatabase.IsValidFolder(MaterialFolder)) return;
        AssetDatabase.CreateFolder("Assets/SourceFiles/Materials", "Shop");
    }

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
    {
        string path = $"{MaterialFolder}/{name}.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material material = new Material(shader) { name = name };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);
        // The stock 0.5 throws a specular flare straight back down the flashlight beam
        if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.15f);
        if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.15f);

        if (emission.HasValue)
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", emission.Value);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        AssetDatabase.CreateAsset(material, path);
        return material;
    }
}
