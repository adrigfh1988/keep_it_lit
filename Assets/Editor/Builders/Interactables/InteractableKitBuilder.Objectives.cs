using UnityEditor;
using UnityEngine;

public static partial class InteractableKitBuilder
{
    // Asset Store packs imported 4 Oct 2026 for the key and fuse box models ("Simple Low Poly Keys" by
    // 3Digitalis, "Sockets and Fuse Box Pack" by TrueBlue Studios). Both ship Built-in Standard materials,
    // so only their meshes/textures are used - UrpCopyOf rebuilds each material as URP Lit under
    // MaterialFolder. If a pack is missing the builder falls back to the original primitive model.
    private const string KeyModelPrefabPath = "Assets/Gabies_Assets/Keys/Prefabs/Simple_02.prefab";
    private const string FuseBoxModelPrefabPath = "Assets/Socket Pack/Prefabs/Fuse_Box_01.prefab";
    /// <summary>The pack key is life-size (13 cm); scaled up so it reads from down a dark corridor.</summary>
    private const float KeyModelScale = 1.6f;
    /// <summary>The pack fuse box is a 24 x 16 cm consumer unit; doubled to a 48 x 32 cm wall cabinet.</summary>
    private const float FuseBoxModelScale = 2f;
    /// <summary>The Maze kit's lever panel (Switch.prefab, already URP via the kit's PixPal shader) is the shutter button's model; 0.7 turns its 52 x 70 cm into a 36 x 49 cm wall panel.</summary>
    private const string KitSwitchPrefabPath = "Assets/Maze/Prefabs/Switch.prefab";
    private const string KitSwitchModelPath = "Assets/Maze/Models/Switch.fbx";
    private const float KitSwitchScale = 0.7f;

    private const string PackShard = "Items/Organic/Bulb01";
    /// <summary>Bulb01 is a 13 x 7 x 14 cm lumpy cluster; x2.4 gives a 31 x 17 x 34 cm crystal-sized lump.</summary>
    private const float PackShardScale = 2.4f;

    /// <summary>
    /// F90: the shard's model - the pack's lumpy Bulb01 cluster (nothing in the pack is a gem or a coin) centred on the root's origin, no
    /// collider. MazeGenerator.BuildShard clones the root's children under every shard and swaps in the glowing Maze_Shard material, so the
    /// glow and colour stay the runtime's; the pack texture only shows in the gallery. Null if the pack is not in the project.
    /// </summary>
    private static GameObject BuildShardModel(Transform parent, Vector3 localPosition)
    {
        if (!HasPackPrefab(PackShard)) return null;

        GameObject root = new GameObject("ShardModel");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        GameObject model = AlchemistPack.Spawn(root.transform, PackShard, PackShardScale);
        Bounds b = AlchemistPack.RenderBounds(model);
        model.transform.position += root.transform.position - b.center;
        return root;
    }

    /// <summary>
    /// F80: a double-leaf cellar trapdoor for MazeEscape.BuildHatch - two WoodDoor01 leaves lying flat,
    /// hinged on their outer edges (children 'LidL'/'LidR', opened about local Z), over a 'Glow' slab that
    /// MazeEscape gives its own emissive material at runtime. No colliders: the hatch is a position test.
    /// Null if the pack is not in the project.
    /// </summary>
    private static GameObject BuildEscapeHatch(Transform parent, Vector3 localPosition)
    {
        const string leaf = "Architecture/WoodDoor01";
        if (!HasPackPrefab(leaf)) return null;

        const float leafScale = 0.88f;   // 1.07 x 1.93 m door -> a 0.94 x 1.70 m leaf
        const float leafWidth = 0.94f;
        const float lift = 0.03f;         // leaves sit just proud of the glow slab

        Material glowMat = LoadOrCreateMat("Interactable_HatchGlow", new Color(0.25f, 1f, 0.7f), new Color(0.25f, 1f, 0.7f) * 3f, 0.1f);

        GameObject root = new GameObject("EscapeHatch");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        Primitive(root.transform, PrimitiveType.Cube, "Glow", new Vector3(0f, 0.01f, 0f), new Vector3(leafWidth * 2f + 0.24f, 0.02f, 1.94f), glowMat, keepCollider: false);

        // The shaft: a black inset over the glow, so the open hatch reads as a hole with a lit rim rather
        // than a glowing floor panel.
        Material pitMat = LoadOrCreateMat("Interactable_HatchPit", Color.black, null, 0f);
        Primitive(root.transform, PrimitiveType.Cube, "Pit", new Vector3(0f, 0.013f, 0f), new Vector3(leafWidth * 2f - 0.1f, 0.02f, 1.6f), pitMat, keepCollider: false);

        Transform lidL = new GameObject("LidL").transform;
        lidL.SetParent(root.transform, false);
        lidL.localPosition = new Vector3(-leafWidth, lift, 0f);
        AlchemistPack.Place(lidL, leaf, new Vector3(leafWidth * 0.5f, 0f, 0f), Quaternion.Euler(90f, 0f, 0f), leafScale);

        Transform lidR = new GameObject("LidR").transform;
        lidR.SetParent(root.transform, false);
        lidR.localPosition = new Vector3(leafWidth, lift, 0f);
        AlchemistPack.Place(lidR, leaf, new Vector3(-leafWidth * 0.5f, 0f, 0f), Quaternion.Euler(0f, 180f, 0f) * Quaternion.Euler(90f, 0f, 0f), leafScale);

        return root;
    }

    private static MazeDoor BuildShutterDoor(Transform parent, Vector3 localPosition)
    {
        Material metal = LoadOrCreateMat("Interactable_Shutter", new Color(0.24f, 0.25f, 0.27f), null, 0.4f);
        Material lightMat = LoadOrCreateMat("Interactable_StatusLight", new Color(0.9f, 0.12f, 0.08f), new Color(0.9f, 0.12f, 0.08f) * 2f, 0.1f);

        GameObject root = new GameObject("ShutterDoor");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        MazeDoor door = root.AddComponent<MazeDoor>();

        // Authored as a 1x1 unit panel at local (0,0,0) - MazeDoor.Configure scales X/Y to the real
        // opening and repositions it every frame while sliding (see MazeDoor.ApplyPanelOffset).
        GameObject panel = Primitive(root.transform, PrimitiveType.Cube, "Panel", Vector3.zero, new Vector3(1f, 1f, 0.1f), metal, keepCollider: false);

        GameObject statusLightGo = new GameObject("StatusLight");
        statusLightGo.transform.SetParent(root.transform, false);
        statusLightGo.transform.localPosition = new Vector3(0f, 2.6f, 0.08f);
        GameObject bulb = Primitive(statusLightGo.transform, PrimitiveType.Sphere, "Bulb", Vector3.zero, Vector3.one * 0.1f, lightMat, keepCollider: false);
        Light light = statusLightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = lightMat.HasProperty("_EmissionColor") ? lightMat.GetColor("_EmissionColor") : Color.red;
        light.range = 2.5f;
        light.intensity = 0.6f;
        light.shadows = LightShadows.None;

        SerializedObject so = new SerializedObject(door);
        so.FindProperty("panel").objectReferenceValue = panel.transform;
        so.FindProperty("statusLightRenderer").objectReferenceValue = bulb.GetComponent<Renderer>();
        so.FindProperty("statusLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return door;
    }

    private static WallButton BuildWallButton(Transform parent, Vector3 localPosition)
    {
        Material plateMat = LoadOrCreateMat("Interactable_Plate", new Color(0.16f, 0.17f, 0.19f), null, 0.35f);
        Material capMat = LoadOrCreateMat("Interactable_ButtonCap", new Color(0.75f, 0.08f, 0.06f), null, 0.55f);
        Material lightMat = LoadOrCreateMat("Interactable_StatusLight", new Color(0.9f, 0.12f, 0.08f), new Color(0.9f, 0.12f, 0.08f) * 2f, 0.1f);

        GameObject root;
        Vector3 statusPos;
        GameObject model = MountKitSwitch();
        if (model != null)
        {
            // Maze kit lever panel (4 Oct 2026, user's pick over a store pack): 52 x 70 x 20 cm at
            // scale 1 with its pivot at the bottom-back and the slot facing +Z, so at 0.7 it is placed
            // with its back on z = 0 and its height centred on the root (the generator mounts the root
            // at hand height, LookRotation(-dirVec)).
            root = new GameObject("WallButton");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * KitSwitchScale;
            model.transform.localPosition = new Vector3(0f, -0.35f * KitSwitchScale, -0.1f * KitSwitchScale);

            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0f, 0.1f * KitSwitchScale);
            collider.size = new Vector3(0.52f, 0.7f, 0.2f) * KitSwitchScale;

            statusPos = new Vector3(0f, 0.35f * KitSwitchScale + 0.03f, 0.1f * KitSwitchScale);
        }
        else
        {
            Debug.LogWarning($"InteractableKitBuilder: {KitSwitchPrefabPath} not found - building the primitive button instead.");
            // The root itself is the plate - a real, trigger-free collider for PlayerInteractor's spherecast.
            root = Primitive(parent, PrimitiveType.Cube, "WallButton", localPosition, new Vector3(0.3f, 0.4f, 0.08f), plateMat, keepCollider: true);
            Primitive(root.transform, PrimitiveType.Cylinder, "Button", new Vector3(0f, 0f, 0.09f),
                new Vector3(0.12f, 0.03f, 0.12f), capMat, keepCollider: false, localRotation: Quaternion.Euler(90f, 0f, 0f));
            statusPos = new Vector3(0f, 0.14f, 0.06f);
        }
        WallButton button = root.AddComponent<WallButton>();

        GameObject statusLightGo = new GameObject("StatusLight");
        statusLightGo.transform.SetParent(root.transform, false);
        statusLightGo.transform.localPosition = statusPos;
        GameObject bulb = Primitive(statusLightGo.transform, PrimitiveType.Sphere, "Bulb", Vector3.zero, Vector3.one * 0.04f, lightMat, keepCollider: false);
        Light light = statusLightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.red;
        light.range = 1f;
        light.intensity = 0.4f;
        light.shadows = LightShadows.None;

        SerializedObject so = new SerializedObject(button);
        so.FindProperty("statusLightRenderer").objectReferenceValue = bulb.GetComponent<Renderer>();
        so.FindProperty("statusLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return button;
    }

    private static FuseBox BuildFuseBox(Transform parent, Vector3 localPosition)
    {
        Material cabinetMat = LoadOrCreateMat("Interactable_FuseCabinet", new Color(0.22f, 0.23f, 0.25f), null, 0.35f);
        Material leverMat = LoadOrCreateMat("Interactable_FuseLever", new Color(0.12f, 0.12f, 0.13f), null, 0.5f);
        Material lightMat = LoadOrCreateMat("Interactable_StatusLight", new Color(0.9f, 0.12f, 0.08f), new Color(0.9f, 0.12f, 0.08f) * 2f, 0.1f);

        GameObject root;
        Vector3 leverPos;
        Vector3 statusPos;
        GameObject model = MountPackModel(FuseBoxModelPrefabPath, "FuseBoxModel");
        if (model != null)
        {
            // Pack cabinet (4 Oct 2026): its back sits on z = 0 and the door/fuses face +Z, which is the
            // template's "into the room" side, so no rotation. The glass door is hidden - its shader is
            // not URP and an open cabinet shows the fuse row, which reads as "something to operate".
            root = new GameObject("FuseBox");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * FuseBoxModelScale;
            Transform door = FindDeep(model.transform, "Fuse_Box_Door");
            if (door != null) door.gameObject.SetActive(false);

            // Solid collider for the interactor spherecast (decision D11), sized to the scaled cabinet.
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = new Vector3(0f, 0f, 0.1f);
            collider.size = new Vector3(0.48f, 0.32f, 0.22f);

            // Lever against the cabinet's right side; the indicator bulb sits on the lid, back from the edge.
            leverPos = new Vector3(0.25f, -0.03f, 0.1f);
            statusPos = new Vector3(-0.17f, 0.185f, 0.1f);
        }
        else
        {
            Debug.LogWarning($"InteractableKitBuilder: {FuseBoxModelPrefabPath} not found (Sockets and Fuse Box Pack not imported?) - building the primitive cabinet instead.");
            // The root is the cabinet body - trigger-free solid collider for the interactor ray (decision D11).
            root = Primitive(parent, PrimitiveType.Cube, "FuseBox", localPosition, new Vector3(0.5f, 0.6f, 0.16f), cabinetMat, keepCollider: true);
            leverPos = new Vector3(0.12f, -0.05f, 0.1f);
            statusPos = new Vector3(-0.15f, 0.2f, 0.1f);
        }
        FuseBox box = root.AddComponent<FuseBox>();

        Primitive(root.transform, PrimitiveType.Cylinder, "Lever", leverPos,
            new Vector3(0.03f, 0.1f, 0.03f), leverMat, keepCollider: false, localRotation: Quaternion.Euler(0f, 0f, 20f));

        GameObject statusLightGo = new GameObject("StatusLight");
        statusLightGo.transform.SetParent(root.transform, false);
        statusLightGo.transform.localPosition = statusPos;
        GameObject bulb = Primitive(statusLightGo.transform, PrimitiveType.Sphere, "Bulb", Vector3.zero, Vector3.one * 0.06f, lightMat, keepCollider: false);
        Light light = statusLightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = Color.red;
        light.range = 1.5f;
        light.intensity = 0.5f;
        light.shadows = LightShadows.None;

        SerializedObject so = new SerializedObject(box);
        so.FindProperty("indicatorRenderer").objectReferenceValue = bulb.GetComponent<Renderer>();
        so.FindProperty("indicatorLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return box;
    }

    // ---------------------------------------------------------------- F77 template (Key Hunt)

    /// <summary>
    /// A brass key on a dark iron wall hook (F77). The root is the hook plate - a solid collider for the
    /// interactor spherecast, the same decision D11 precedent as BuildBatteryCell/BuildFuseBox. The
    /// generator mounts it with the lore-note offset recipe and rotates it with LookRotation(-dirVec), so
    /// it is authored facing +Z here.
    /// </summary>
    private static KeyPickup BuildKeyPickup(Transform parent, Vector3 localPosition)
    {
        Material hookMat = LoadOrCreateMat("Interactable_KeyHook", new Color(0.16f, 0.16f, 0.17f), null, 0.45f);
        Material brassMat = LoadOrCreateMat("Interactable_KeyBrass", new Color(0.85f, 0.65f, 0.25f), new Color(0.85f, 0.65f, 0.25f) * 1.5f, 0.6f);

        // Unscaled root (a scaled primitive root would squash every child by its own scale) carrying the
        // solid collider for the interactor spherecast (decision D11); the hook plate is a child.
        GameObject root = new GameObject("KeyPickup");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        BoxCollider rootCollider = root.AddComponent<BoxCollider>();
        rootCollider.center = new Vector3(0f, -0.03f, 0.03f);
        rootCollider.size = new Vector3(0.18f, 0.34f, 0.09f);
        KeyPickup key = root.AddComponent<KeyPickup>();

        Primitive(root.transform, PrimitiveType.Cube, "Plate", new Vector3(0f, -0.03f, 0f), new Vector3(0.16f, 0.3f, 0.03f), hookMat, keepCollider: false);
        Primitive(root.transform, PrimitiveType.Cylinder, "Hook", new Vector3(0f, 0.06f, 0.03f),
            new Vector3(0.02f, 0.03f, 0.02f), hookMat, keepCollider: false, localRotation: Quaternion.Euler(90f, 0f, 0f));

        GameObject model = MountPackModel(KeyModelPrefabPath, "KeyModel");
        if (model != null)
        {
            // Pack key (4 Oct 2026): 13 cm tall, bow and keyring near the top, flat in Z. Hung so the ring
            // sits on the hook, just in front of the plate. A faint brass emission on the key body itself
            // so the metal catches the eye in the dark even before the glint light does.
            model.transform.SetParent(root.transform, false);
            model.transform.localScale = Vector3.one * KeyModelScale;
            model.transform.localPosition = new Vector3(0f, -0.035f, 0.045f);
            Renderer body = model.GetComponent<Renderer>();
            if (body != null && body.sharedMaterial != null)
            {
                // Kept very faint: at 0.35 the whole key blew out to white under the glint light.
                Material bodyMat = body.sharedMaterial;
                bodyMat.EnableKeyword("_EMISSION");
                bodyMat.SetColor("_EmissionColor", new Color(0.85f, 0.65f, 0.25f) * 0.06f);
                bodyMat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                EditorUtility.SetDirty(bodyMat);
            }
        }
        else
        {
            Debug.LogWarning($"InteractableKitBuilder: {KeyModelPrefabPath} not found (Simple Low Poly Keys not imported?) - building the primitive key instead.");
            Primitive(root.transform, PrimitiveType.Cylinder, "KeyBow", new Vector3(0f, 0.03f, 0.04f),
                new Vector3(0.07f, 0.008f, 0.07f), brassMat, keepCollider: false, localRotation: Quaternion.Euler(90f, 0f, 0f));
            Primitive(root.transform, PrimitiveType.Cube, "KeyShaft", new Vector3(0f, -0.045f, 0.04f),
                new Vector3(0.018f, 0.12f, 0.014f), brassMat, keepCollider: false);
            Primitive(root.transform, PrimitiveType.Cube, "KeyBit", new Vector3(0.015f, -0.09f, 0.04f),
                new Vector3(0.04f, 0.02f, 0.014f), brassMat, keepCollider: false);
        }

        GameObject glintGo = new GameObject("Glint");
        glintGo.transform.SetParent(root.transform, false);
        // Held well off the key: a point light a few centimetres from the metal saturates it to white.
        glintGo.transform.localPosition = new Vector3(0f, 0.05f, 0.32f);
        Light glint = glintGo.AddComponent<Light>();
        glint.type = LightType.Point;
        glint.color = new Color(1f, 0.85f, 0.55f);
        glint.range = 1.5f;
        glint.intensity = 0.22f;
        glint.shadows = LightShadows.None;

        SerializedObject so = new SerializedObject(key);
        so.FindProperty("glintLight").objectReferenceValue = glint;
        so.ApplyModifiedPropertiesWithoutUndo();

        return key;
    }

    // ---------------------------------------------------------------- Asset Store pack models (4 Oct 2026)

    /// <summary>
    /// Instantiates a pack prefab as a (linked) prefab instance named `name`, with every Built-in material
    /// on it swapped for a URP Lit copy from UrpCopyOf. Returns null if the prefab is not in the project,
    /// so each caller can fall back to its primitive model. Not parented here: the caller decides.
    /// </summary>
    private static GameObject MountPackModel(string prefabPath, string name)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null) return null;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.name = name;

        foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            Material[] materials = renderer.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < materials.Length; i++)
            {
                Material converted = UrpCopyOf(materials[i]);
                if (converted == materials[i]) continue;
                materials[i] = converted;
                changed = true;
            }
            if (changed) renderer.sharedMaterials = materials;
        }
        return instance;
    }

    /// <summary>
    /// The Maze kit's Switch panel, unpacked (so its own BoxCollider can go - the template root carries
    /// the interactor collider) and with its lever restored: the kit prefab's "Palanca" child has an
    /// empty MeshFilter, while Switch.fbx still contains the "Lever" mesh it was meant to show. Null if
    /// the kit is not in the project.
    /// </summary>
    private static GameObject MountKitSwitch()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(KitSwitchPrefabPath);
        if (prefab == null) return null;

        GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        instance.name = "SwitchModel";

        foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
        {
            Object.DestroyImmediate(collider);
        }

        UnityEngine.Mesh leverMesh = null;
        foreach (Object sub in AssetDatabase.LoadAllAssetsAtPath(KitSwitchModelPath))
        {
            if (sub is UnityEngine.Mesh mesh && mesh.name == "Lever") { leverMesh = mesh; break; }
        }
        foreach (MeshFilter filter in instance.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null && leverMesh != null) filter.sharedMesh = leverMesh;
        }
        return instance;
    }

    /// <summary>
    /// A URP Lit material carrying `source`'s base colour/texture, normal map and metallic map, saved as
    /// MaterialFolder/Pack_&lt;name&gt;.mat (loaded, never overwritten, if it already exists - same rule as
    /// LoadOrCreateMat). Materials already on a URP shader are returned as they are. Only the textures
    /// are reused; the packs' own Standard materials are left untouched.
    /// </summary>
    private static Material UrpCopyOf(Material source) => PackMaterials.UrpCopyOf(source); // moved to PackMaterials (F85)

    private static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
