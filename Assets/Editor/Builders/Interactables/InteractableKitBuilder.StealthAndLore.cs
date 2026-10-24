using UnityEditor;
using UnityEngine;

public static partial class InteractableKitBuilder
{
    private static BatteryCellPickup BuildBatteryCell(Transform parent, Vector3 localPosition)
    {
        Material bodyMat = LoadOrCreateMat("Interactable_BatteryBody", new Color(0.2f, 0.2f, 0.22f), null, 0.5f);
        Material bandMat = LoadOrCreateMat("Interactable_BatteryBand", new Color(0.25f, 0.95f, 0.4f), new Color(0.25f, 0.95f, 0.4f) * 2f, 0.2f);

        // The root is the cell body - trigger-free solid collider for the interactor ray (decision D11).
        GameObject root = CylinderRH(parent, "BatteryCell", localPosition, 0.07f, 0.16f, bodyMat, keepCollider: true);
        BatteryCellPickup pickup = root.AddComponent<BatteryCellPickup>();

        CylinderRH(root.transform, "Band", Vector3.zero, 0.075f, 0.04f, bandMat, keepCollider: false);

        return pickup;
    }

    // ---------------------------------------------------------------- F74 templates
    // ---------------------------------------------------------------- F79: Alchemist House re-skins

    /// <summary>A pack model for one bottle look-alike: prefab path under the pack's Prefabs/ folder and a scale multiplier (the flasks are small).</summary>
    private struct BottleVariant
    {
        public string Path;
        public float Scale;
        public BottleVariant(string path, float scale) { Path = path; Scale = scale; }
    }

    private const string PackBottle = "Items/Bottles/Bottle02";
    private const string PackCan = "Items/Dishes/ClutterTanker01";
    private const string PackLantern = "Items/Lights/Oillamp01_on";
    private const string PackNote = "Items/Books/Book12";
    private const float PackNoteScale = 1.5f;

    private static readonly BottleVariant[] BottleVariants =
    {
        new BottleVariant("Items/Bottles/Bottle01", 1f),
        new BottleVariant("Items/Bottles/Bottle03", 1f),
        new BottleVariant("Items/Bottles/Bottle04", 1f),
        new BottleVariant("Items/Bottles/Bottle02b", 1f),
        new BottleVariant("Items/Bottles/Flask04", 1.5f),
        new BottleVariant("Items/Bottles/Flask07", 1.5f),
    };

    private static bool HasPackPrefab(string path)
    {
        return AssetDatabase.LoadAssetAtPath<GameObject>(AlchemistPack.Prefabs + path + ".prefab") != null;
    }

    /// <summary>
    /// A throwable whose root origin is the pack model's centre (MazeGenerator.BuildThrowables lifts the
    /// root by a fixed height above the floor) and whose root collider is a primitive capsule fitted to the
    /// model - ThrowableItem.Awake reads GetComponent&lt;Collider&gt;() on the root and Launch adds a
    /// Rigidbody to it, so a non-convex mesh collider would not do. Null if the pack prefab is missing.
    /// </summary>
    private static ThrowableItem BuildPackThrowable(Transform parent, Vector3 localPosition, string rootName, string modelPath, float modelScale,
        ThrowableItem.Kind kind, float minRadius, float minHeight)
    {
        if (!HasPackPrefab(modelPath)) return null;

        GameObject root = new GameObject(rootName);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        GameObject model = AlchemistPack.Spawn(root.transform, modelPath, modelScale);
        Bounds b = AlchemistPack.RenderBounds(model);
        model.transform.position += root.transform.position - b.center;
        b = AlchemistPack.RenderBounds(model);

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.height = Mathf.Max(b.size.y, minHeight);
        capsule.radius = Mathf.Min(capsule.height * 0.5f, Mathf.Max(minRadius, Mathf.Min(b.size.x, b.size.z) * 0.5f));
        capsule.center = Vector3.zero;

        ThrowableItem item = root.AddComponent<ThrowableItem>();
        SerializedObject so = new SerializedObject(item);
        so.FindProperty("kind").enumValueIndex = (int)kind;
        so.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    private static ThrowableItem BuildBottleVariant(Transform parent, Vector3 localPosition, BottleVariant spec)
    {
        string modelName = spec.Path.Substring(spec.Path.LastIndexOf('/') + 1);
        ThrowableItem item = BuildPackThrowable(parent, localPosition, $"Bottle_{modelName}", spec.Path, spec.Scale, ThrowableItem.Kind.Bottle, 0.03f, 0.1f);
        if (item == null) Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{spec.Path}.prefab not found - skipping that bottle variant.");
        return item;
    }

    private static ThrowableItem BuildBottle(Transform parent, Vector3 localPosition)
    {
        ThrowableItem packed = BuildPackThrowable(parent, localPosition, "Bottle", PackBottle, 1f, ThrowableItem.Kind.Bottle, 0.03f, 0.1f);
        if (packed != null) return packed;

        Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{PackBottle}.prefab not found - building the primitive bottle instead.");
        return BuildBottlePrimitive(parent, localPosition);
    }

    private static ThrowableItem BuildCan(Transform parent, Vector3 localPosition)
    {
        ThrowableItem packed = BuildPackThrowable(parent, localPosition, "Can", PackCan, 1f, ThrowableItem.Kind.Can, 0.035f, 0.09f);
        if (packed != null) return packed;

        Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{PackCan}.prefab not found - building the primitive can instead.");
        return BuildCanPrimitive(parent, localPosition);
    }

    private static ThrowableItem BuildBottlePrimitive(Transform parent, Vector3 localPosition)
    {
        Material glassMat = LoadOrCreateMat("Interactable_BottleGlass", new Color(0.1f, 0.28f, 0.16f), new Color(0.05f, 0.1f, 0.06f), 0.7f);

        GameObject root = CylinderRH(parent, "Bottle", localPosition, 0.045f, 0.26f, glassMat, keepCollider: true);
        CylinderRH(root.transform, "Neck", new Vector3(0f, 0.16f, 0f), 0.018f, 0.1f, glassMat, keepCollider: false);
        ThrowableItem item = root.AddComponent<ThrowableItem>();

        SerializedObject so = new SerializedObject(item);
        so.FindProperty("kind").enumValueIndex = (int)ThrowableItem.Kind.Bottle;
        so.ApplyModifiedPropertiesWithoutUndo();

        return item;
    }

    private static ThrowableItem BuildCanPrimitive(Transform parent, Vector3 localPosition)
    {
        Material metalMat = LoadOrCreateMat("Interactable_CanMetal", new Color(0.72f, 0.74f, 0.76f), null, 0.75f);

        GameObject root = CylinderRH(parent, "Can", localPosition, 0.033f, 0.12f, metalMat, keepCollider: true);
        ThrowableItem item = root.AddComponent<ThrowableItem>();

        SerializedObject so = new SerializedObject(item);
        so.FindProperty("kind").enumValueIndex = (int)ThrowableItem.Kind.Can;
        so.ApplyModifiedPropertiesWithoutUndo();

        return item;
    }

    /// <summary>
    /// Shared by glassPatch/puddle: a flat 1.6 m decal quad ("Look", no collider - the NoisySurface trigger is the only footprint) lying on
    /// the floor. F90: the look is a transparent decal (broken-glass shards / a dark glossy pool, see NoisePatchLook), not a flat coloured square.
    /// </summary>
    private static NoisySurface BuildFloorPatch(Transform parent, Vector3 localPosition, string name, bool glass)
    {
        Material mat = LoadOrCreateMat($"Interactable_{name}", Color.white, null, 0.9f);
        if (glass) NoisePatchLook.ApplyGlass(mat); else NoisePatchLook.ApplyPuddle(mat);

        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        GameObject quad = Primitive(root.transform, PrimitiveType.Quad, "Look", Vector3.zero, new Vector3(NoisePatchLook.Size, NoisePatchLook.Size, 1f), mat, keepCollider: false,
            localRotation: Quaternion.Euler(90f, 0f, 0f));
        quad.transform.localPosition = Vector3.up * 0.005f;
        MeshRenderer renderer = quad.GetComponent<MeshRenderer>();
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

        return root.AddComponent<NoisySurface>();
    }

    private static Lantern BuildLantern(Transform parent, Vector3 localPosition)
    {
        if (!HasPackPrefab(PackLantern))
        {
            Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{PackLantern}.prefab not found - building the primitive lantern instead.");
            return BuildLanternPrimitive(parent, localPosition);
        }

        GameObject root = new GameObject("Lantern");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        // The pack lamp (its own light is stripped by AlchemistPack.Spawn - ours below keeps the old numbers),
        // centred over the root with its underside at -0.025 where the old primitive base's underside was.
        GameObject model = AlchemistPack.Spawn(root.transform, PackLantern);
        Bounds b = AlchemistPack.RenderBounds(model);
        model.transform.position += new Vector3(root.transform.position.x - b.center.x, root.transform.position.y - 0.025f - b.min.y, root.transform.position.z - b.center.z);
        b = AlchemistPack.RenderBounds(model);

        CapsuleCollider capsule = root.AddComponent<CapsuleCollider>();
        capsule.direction = 1;
        capsule.height = b.size.y;
        capsule.radius = Mathf.Min(b.size.y * 0.5f, Mathf.Max(b.size.x, b.size.z) * 0.5f);
        capsule.center = root.transform.InverseTransformPoint(b.center);

        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.position = new Vector3(b.center.x, b.min.y + b.size.y * 0.6f, b.center.z); // at the glass
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.45f);
        light.range = 6f;
        light.intensity = 1.1f;
        light.shadows = LightShadows.None;

        Lantern lantern = root.AddComponent<Lantern>();
        SerializedObject so = new SerializedObject(lantern);
        so.FindProperty("lanternLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return lantern;
    }

    private static Lantern BuildLanternPrimitive(Transform parent, Vector3 localPosition)
    {
        Material bodyMat = LoadOrCreateMat("Interactable_LanternBody", new Color(0.2f, 0.18f, 0.14f), null, 0.4f);
        Material glassMat = LoadOrCreateMat("Interactable_LanternGlass", new Color(0.95f, 0.75f, 0.4f), new Color(0.95f, 0.75f, 0.4f) * 2f, 0.3f);

        GameObject root = CylinderRH(parent, "Lantern", localPosition, 0.1f, 0.05f, bodyMat, keepCollider: true); // base
        CylinderRH(root.transform, "Glass", new Vector3(0f, 0.14f, 0f), 0.08f, 0.22f, glassMat, keepCollider: false);
        CylinderRH(root.transform, "Cap", new Vector3(0f, 0.27f, 0f), 0.1f, 0.04f, bodyMat, keepCollider: false);

        GameObject lightGo = new GameObject("Light");
        lightGo.transform.SetParent(root.transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 0.14f, 0f);
        Light light = lightGo.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.45f);
        light.range = 6f;
        light.intensity = 1.1f;
        light.shadows = LightShadows.None;

        Lantern lantern = root.AddComponent<Lantern>();
        SerializedObject so = new SerializedObject(lantern);
        so.FindProperty("lanternLight").objectReferenceValue = light;
        so.ApplyModifiedPropertiesWithoutUndo();

        return lantern;
    }

    private const string PackShelf = "Furniture/WoodShelf01";
    /// <summary>The pack shelf is a 1.8 m plank; x0.45 makes a 0.8 m bracket shelf, wide enough for the book and a glint.</summary>
    private const float PackShelfScale = 0.45f;
    private const float PackNoteLean = 10f;

    /// <summary>
    /// F90: an Alchemist book standing on a small hung pack shelf, leaning back against the wall - not a flat panel stuck to it. The root
    /// sits on the wall face with +Z into the corridor (MazeGenerator.BuildLoreNotes faces it with LookRotation(-dirVec), the Intake does
    /// the same), the shelf's back at z = -0.02 and its top 0.18 m under the root, the book (Book09 x1.5, cover to +Z) on it. One
    /// BoxCollider on the root around shelf + book is the click target (it replaces the old Edge quad's); a small warm point light
    /// pulsing through StarPulse keeps it findable in the dark. Falls back to the primitive note without the pack.
    /// </summary>
    private static LoreNote BuildLoreNote(Transform parent, Vector3 localPosition)
    {
        if (!HasPackPrefab(PackNote) || !HasPackPrefab(PackShelf))
        {
            Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{PackNote}.prefab or {PackShelf}.prefab not found - building the primitive note instead.");
            return BuildLoreNotePrimitive(parent, localPosition);
        }

        GameObject root = new GameObject("LoreNote");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        Vector3 origin = root.transform.position;

        // Shelf: long axis along X, centred on the root, back just off the wall plane, top 0.18 m below the root.
        GameObject shelf = AlchemistPack.Spawn(root.transform, PackShelf, PackShelfScale);
        Bounds sb = AlchemistPack.RenderBounds(shelf);
        if (sb.size.z > sb.size.x) shelf.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * shelf.transform.rotation;
        sb = AlchemistPack.RenderBounds(shelf);
        shelf.transform.position += new Vector3(origin.x - sb.center.x, origin.y - 0.18f - sb.max.y, origin.z - 0.02f - sb.min.z);
        sb = AlchemistPack.RenderBounds(shelf);

        // Book: upright with its thin axis on Z, leaning back, standing on the shelf's top against the wall side.
        GameObject book = AlchemistPack.Spawn(root.transform, PackNote, PackNoteScale);
        Bounds b = AlchemistPack.RenderBounds(book);
        if (b.size.x < b.size.z) book.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * book.transform.rotation;
        book.transform.rotation = Quaternion.Euler(0f, NoteBookYaw, 0f) * book.transform.rotation;
        book.transform.rotation = Quaternion.Euler(0f, 0f, NoteBookRoll) * book.transform.rotation;
        book.transform.rotation = Quaternion.Euler(-PackNoteLean, 0f, 0f) * book.transform.rotation;
        b = AlchemistPack.RenderBounds(book);
        book.transform.position += new Vector3(origin.x - b.center.x, sb.max.y + 0.003f - b.min.y, sb.min.z + 0.02f - b.min.z);
        b = AlchemistPack.RenderBounds(book);

        Bounds all = sb;
        all.Encapsulate(b);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(all.center);
        box.size = new Vector3(all.size.x + 0.1f, all.size.y + 0.1f, Mathf.Max(0.25f, all.size.z));

        GameObject glint = new GameObject("Glint");
        glint.transform.SetParent(root.transform, false);
        glint.transform.position = new Vector3(origin.x, b.center.y, origin.z + 0.5f);
        Light light = glint.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.82f, 0.5f);
        light.range = 1.8f;
        light.intensity = 0.3f;
        light.shadows = LightShadows.None;
        glint.AddComponent<StarPulse>(); // picks up the Light in Awake; +-30 % at 0.5 Hz like a star

        return root.AddComponent<LoreNote>();
    }

    /// <summary>Which way the book is turned about Y after its thin axis is put on Z: 0 or 180 (set from a visual check, so the cover - not the back - faces +Z).</summary>
    private const float NoteBookYaw = 180f;
    /// <summary>Roll about Z after the yaw: the pack model's title page reads upside down with its spine on the right as it comes out of the prefab.</summary>
    private const float NoteBookRoll = 180f;

    private const string MorgueNotebookPath = "Assets/Morgue Room PBR/Prefabs/props/notebook.prefab";
    private const float PatientFileScale = 1.25f;

    /// <summary>
    /// F93: a patient case file - the Morgue Room PBR notebook (a red-tinted copy of its material) standing upright and leaning back
    /// on the same small hung Alchemist shelf as the lore note, root on the wall face with +Z into the corridor, one BoxCollider on the
    /// root, and a red point light pulsing through StarPulse so it can be found in the dark. It is a LoreNote (ids 100+). Null if the
    /// Morgue pack or the Alchemist shelf is missing.
    /// </summary>
    private static LoreNote BuildPatientFile(Transform parent, Vector3 localPosition)
    {
        GameObject notebook = AssetDatabase.LoadAssetAtPath<GameObject>(MorgueNotebookPath);
        if (notebook == null || !HasPackPrefab(PackShelf))
        {
            Debug.LogWarning($"InteractableKitBuilder: {MorgueNotebookPath} or the Alchemist {PackShelf} not found - the patient file template was not built.");
            return null;
        }

        GameObject root = new GameObject("PatientFile");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        Vector3 origin = root.transform.position;

        GameObject shelf = AlchemistPack.Spawn(root.transform, PackShelf, PackShelfScale);
        Bounds sb = AlchemistPack.RenderBounds(shelf);
        if (sb.size.z > sb.size.x) shelf.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * shelf.transform.rotation;
        sb = AlchemistPack.RenderBounds(shelf);
        shelf.transform.position += new Vector3(origin.x - sb.center.x, origin.y - 0.18f - sb.max.y, origin.z - 0.02f - sb.min.z);
        sb = AlchemistPack.RenderBounds(shelf);

        GameObject book = (GameObject)PrefabUtility.InstantiatePrefab(notebook, root.transform);
        book.name = "Notebook";
        foreach (Collider c in book.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        book.transform.localScale *= PatientFileScale;
        // Flat (cover up) -> upright with the cover toward +Z, leaning back against the wall.
        // The prefab root carries a stray demo-scene yaw (326 degrees); the mesh itself lies flat, cover up, so it is dropped here.
        book.transform.rotation = Quaternion.Euler(-PackNoteLean, 0f, 0f) * Quaternion.Euler(90f, 0f, 0f);
        Material tint = PatientFileMaterial(book);
        if (tint != null)
        {
            foreach (Renderer r in book.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = tint;
                r.sharedMaterials = mats;
            }
        }
        Bounds b = AlchemistPack.RenderBounds(book);
        book.transform.position += new Vector3(origin.x - b.center.x, sb.max.y + 0.003f - b.min.y, sb.min.z + 0.02f - b.min.z);
        b = AlchemistPack.RenderBounds(book);

        Bounds all = sb;
        all.Encapsulate(b);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(all.center);
        box.size = new Vector3(all.size.x + 0.1f, all.size.y + 0.1f, Mathf.Max(0.25f, all.size.z));

        GameObject glint = new GameObject("Glint");
        glint.transform.SetParent(root.transform, false);
        glint.transform.position = new Vector3(origin.x, b.center.y, origin.z + 0.5f);
        Light light = glint.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.32f, 0.22f);
        light.range = 2.2f;
        light.intensity = 0.4f;
        light.shadows = LightShadows.None;
        glint.AddComponent<StarPulse>();

        return root.AddComponent<LoreNote>();
    }

    private const string PackTrunk = "Items/Misc/Trunk01";
    /// <summary>The cache's width along the wall (m): the pack trunk is scaled so its long side is this.</summary>
    private const float ItemCacheWidth = 0.8f;

    /// <summary>
    /// F107 C: the DESCENT item cache - the Alchemist trunk (long side along X, back against the wall plane at the root, sitting on the floor,
    /// opening toward +Z into the corridor), one BoxCollider on the root as the E target and a faint warm point light pulsing through
    /// StarPulse so it can be found in the dark. Null if the pack is missing.
    /// </summary>
    private static DescentItem BuildItemCache(Transform parent, Vector3 localPosition)
    {
        if (!HasPackPrefab(PackTrunk))
        {
            Debug.LogWarning($"InteractableKitBuilder: {AlchemistPack.Prefabs}{PackTrunk}.prefab not found - the item cache template was not built.");
            return null;
        }

        GameObject root = new GameObject("ItemCache");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        Vector3 origin = root.transform.position;

        GameObject trunk = AlchemistPack.Spawn(root.transform, PackTrunk);
        Bounds b = AlchemistPack.RenderBounds(trunk);
        if (b.size.z > b.size.x) trunk.transform.rotation = Quaternion.Euler(0f, 90f, 0f) * trunk.transform.rotation;
        b = AlchemistPack.RenderBounds(trunk);
        trunk.transform.localScale *= ItemCacheWidth / Mathf.Max(0.01f, b.size.x);
        b = AlchemistPack.RenderBounds(trunk);
        trunk.transform.position += new Vector3(origin.x - b.center.x, origin.y - b.min.y, origin.z - b.min.z);
        b = AlchemistPack.RenderBounds(trunk);

        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(b.center);
        box.size = new Vector3(b.size.x + 0.1f, b.size.y + 0.1f, b.size.z + 0.1f);

        GameObject glint = new GameObject("Glint");
        glint.transform.SetParent(root.transform, false);
        glint.transform.position = new Vector3(origin.x, b.max.y + 0.3f, origin.z + b.size.z + 0.3f);
        Light light = glint.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = new Color(1f, 0.78f, 0.45f);
        light.range = 2.2f;
        light.intensity = 0.3f;
        light.shadows = LightShadows.None;
        glint.AddComponent<StarPulse>();

        DescentItem item = root.AddComponent<DescentItem>();
        SerializedObject itemSo = new SerializedObject(item);
        itemSo.FindProperty("glintLight").objectReferenceValue = light;
        itemSo.ApplyModifiedPropertiesWithoutUndo();
        return item;
    }

    /// <summary>A red-tinted URP copy of the notebook's material, so a case file reads differently from a cream lore note.</summary>
    private static Material PatientFileMaterial(GameObject book)
    {
        string path = $"{MaterialFolder}/Interactable_PatientFileBook.mat";
        Material existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        Renderer r = book.GetComponentInChildren<Renderer>(true);
        if (r == null || r.sharedMaterial == null) return null;
        EnsureFolder();
        Material copy = new Material(r.sharedMaterial) { name = "Interactable_PatientFileBook" };
        if (copy.HasProperty("_BaseColor")) copy.SetColor("_BaseColor", new Color(1f, 0.38f, 0.32f));
        if (copy.HasProperty("_Smoothness")) copy.SetFloat("_Smoothness", Mathf.Min(0.25f, copy.GetFloat("_Smoothness")));
        AssetDatabase.CreateAsset(copy, path);
        return copy;
    }

    private static LoreNote BuildLoreNotePrimitive(Transform parent, Vector3 localPosition)
    {
        Material paperMat = LoadOrCreateMat("Interactable_NotePaper", new Color(0.78f, 0.74f, 0.62f), null, 0.1f);
        Material edgeMat = LoadOrCreateMat("Interactable_NoteEdge", new Color(0.9f, 0.85f, 0.5f), new Color(0.9f, 0.85f, 0.5f) * 1.2f, 0.1f);

        GameObject root = new GameObject("LoreNote");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;

        // The edge sits a hair behind the paper, so it reads as a faint emissive border - the interactor
        // ray hits a plain BoxCollider on this larger backing quad, easier to click than the paper itself.
        GameObject edge = Primitive(root.transform, PrimitiveType.Quad, "Edge", new Vector3(0f, 0f, 0.01f), new Vector3(0.42f, 0.58f, 1f), edgeMat, keepCollider: false);
        Primitive(root.transform, PrimitiveType.Quad, "Paper", Vector3.zero, new Vector3(0.38f, 0.54f, 1f), paperMat, keepCollider: false);
        BoxCollider box = edge.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 1f, 0.1f);

        return root.AddComponent<LoreNote>();
    }
}
