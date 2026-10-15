using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEngine;

/// <summary>
/// F123, the two InteractableKit templates of the wing nightmares (built by InteractableKitBuilder.Build, build-missing-only):
///  - morgueDrawerBank (floor 3): the pack's MorgueBox cabinet (2 x 2 trays) squeezed to fit a dead end, each tray + its front under a Drawer_i pivot,
///    a number tag on every front, one solid BoxCollider on the root, the player's own robot lying on its back (inactive) and Elias's toe tag (inactive);
///  - nightmareAmpoule (floor 4): the hub's Flask03 on its side, a pale blue tint with a faint blue emission, a trigger collider on the root.
/// Both are inactive in the gallery. A missing pack model logs an error and leaves the slot empty (MazeGenerator then logs and skips the feature).
/// </summary>
public static partial class InteractableKitBuilder
{
    private const string MorgueBoxPath = "Assets/Abandoned_Asylum/Prefabs/MorgueBox.prefab";
    private const string AmpoulePrefabPath = "Assets/BK_AlchemistHouse/Prefabs/Items/Bottles/Flask03.prefab";
    private const string AmpouleMaterialPath = "Assets/SourceFiles/Materials/Nightmare_Ampoule.mat";
    private const string PatientIdleControllerPath = "Assets/SourceFiles/Animation/Patients/Patient_Idle.controller";
    private const float BankHeight = 1.9f;
    private const float BankDepth = 1.4f;
    private const float BankSlide = 0.95f;
    private const float AmpouleHeight = 0.16f;

    private static Bounds RendererBounds(GameObject go)
    {
        Bounds b = default;
        bool any = false;
        foreach (Renderer r in go.GetComponentsInChildren<Renderer>(false))
        {
            if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds);
        }
        return b;
    }

    private static void StripNightmarePhysics(GameObject go)
    {
        foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Rigidbody rb in go.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(rb);
        foreach (Light l in go.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(l);
    }

    // ---------------------------------------------------------------- the drawer bank

    internal static MorgueDrawerBank BuildMorgueDrawerBank(Transform parent, Vector3 localPosition)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(MorgueBoxPath);
        if (prefab == null)
        {
            Debug.LogError($"InteractableKitBuilder: {MorgueBoxPath} not found - the morgue drawer bank template was not built.");
            return null;
        }

        GameObject root = new GameObject("MorgueDrawerBank");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        Vector3 origin = root.transform.position;

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        StripNightmarePhysics(model);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // the pack's fronts face -Z; the template's face +Z
        model.transform.localScale = Vector3.one;

        Bounds b0 = RendererBounds(model);
        float s = BankHeight / Mathf.Max(0.01f, b0.size.y);
        float sz = Mathf.Min(s, BankDepth / Mathf.Max(0.01f, b0.size.z));
        model.transform.localScale = new Vector3(s, s, sz);
        Bounds b = RendererBounds(model);
        // Origin at the back-bottom-centre: the back plane at z = 0, the floor at y = 0, centred on x.
        model.transform.position += new Vector3(origin.x - b.center.x, origin.y - b.min.y, origin.z - b.min.z);
        b = RendererBounds(model);
        Debug.Log($"InteractableKitBuilder: morgue drawer bank scaled x{s:0.00} (width {b.size.x:0.00} m, height {b.size.y:0.00} m, depth {b.size.z:0.00} m).");

        // Pair every front (MorgueBox_Door*) with its tray (MorgueBox_Bed*) by position.
        List<Transform> beds = new List<Transform>();
        List<Transform> doors = new List<Transform>();
        foreach (Transform t in model.GetComponentsInChildren<Transform>(true))
        {
            if (t.name.StartsWith("MorgueBox_Bed")) beds.Add(t);
            else if (t.name.StartsWith("MorgueBox_Door")) doors.Add(t);
        }
        doors.Sort((a, c) => (c.GetComponent<Renderer>().bounds.center.y * 10f + (-c.GetComponent<Renderer>().bounds.center.x)).CompareTo(a.GetComponent<Renderer>().bounds.center.y * 10f + (-a.GetComponent<Renderer>().bounds.center.x)));
        if (beds.Count < 4 || doors.Count < 4)
        {
            Debug.LogError($"InteractableKitBuilder: the MorgueBox prefab has {beds.Count} trays and {doors.Count} fronts (4 expected) - the morgue drawer bank template was not built.");
            Object.DestroyImmediate(root);
            return null;
        }

        Material cardMat = LoadOrCreateMat("Nightmare_TagCard", new Color(0.8f, 0.78f, 0.7f), null, 0.1f);
        TMP_FontAsset font = null;
        Material fontMat = null;
        if (WorldSignBuilder.FontsPresent())
        {
            font = WorldSignBuilder.FontAsset(WorldSignBuilder.SignFont.Barlow);
            fontMat = WorldSignBuilder.TextMaterial(WorldSignBuilder.SignFont.Barlow);
        }

        Transform[] pivots = new Transform[4];
        Transform[] hinges = new Transform[4];
        float[] swings = new float[4];
        TextMeshPro[] tags = new TextMeshPro[4];
        Transform[] anchors = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            Transform door = doors[i];
            Bounds db = door.GetComponent<Renderer>().bounds;
            Transform bed = null;
            float best = float.MaxValue;
            foreach (Transform candidate in beds)
            {
                Bounds bb = candidate.GetComponent<Renderer>().bounds;
                float d = Mathf.Abs(bb.center.x - db.center.x) + Mathf.Abs(bb.center.y - db.center.y) * 0.5f;
                if (d < best) { best = d; bed = candidate; }
            }
            beds.Remove(bed);
            Bounds bedBounds = bed.GetComponent<Renderer>().bounds;

            // The tray rolls out; the front is hinged at the cabinet's outer side and swings open first (as a morgue's does), so the tray is not hidden behind it.
            GameObject pivot = new GameObject("Drawer_" + i);
            pivot.transform.SetParent(root.transform, false);
            pivot.transform.position = new Vector3(db.center.x, db.center.y, db.max.z);
            bed.SetParent(pivot.transform, true);
            pivots[i] = pivot.transform;

            bool hingeAtPlusX = db.center.x > b.center.x;
            GameObject hinge = new GameObject("Hinge_" + i);
            hinge.transform.SetParent(root.transform, false);
            hinge.transform.position = new Vector3(hingeAtPlusX ? db.max.x : db.min.x, db.center.y, db.max.z);
            door.SetParent(hinge.transform, true);
            hinges[i] = hinge.transform;
            swings[i] = hingeAtPlusX ? 100f : -100f; // Ry(+100 deg) swings a front hinged on its +X edge out toward +Z; -100 for the -X edge

            // A paper card on the front with the tag's text on it.
            Vector3 face = new Vector3(db.center.x, db.center.y, db.max.z);
            GameObject card = Primitive(hinge.transform, PrimitiveType.Quad, "TagCard", Vector3.zero, new Vector3(0.5f, 0.14f, 1f), cardMat, keepCollider: false);
            card.transform.position = face + new Vector3(0f, 0.04f, 0.008f);
            card.transform.rotation = Quaternion.Euler(0f, 180f, 0f); // a quad is seen from -Z: turned to face +Z

            GameObject textGo = new GameObject("Tag");
            textGo.transform.SetParent(hinge.transform, false);
            textGo.transform.position = face + new Vector3(0f, 0.04f, 0.016f);
            textGo.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up); // text is seen from behind its forward
            TextMeshPro tmp = textGo.AddComponent<TextMeshPro>();
            if (font != null) tmp.font = font;
            if (font != null && fontMat != null) tmp.fontSharedMaterial = fontMat;
            tmp.text = (i + 1).ToString();
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = new Color(0.12f, 0.12f, 0.13f);
            tmp.textWrappingMode = TextWrappingModes.NoWrap;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 0.3f;
            tmp.fontSizeMax = 1.4f;
            tmp.rectTransform.sizeDelta = new Vector2(0.46f, 0.12f);
            tags[i] = tmp;

            GameObject anchor = new GameObject("Anchor");
            anchor.transform.SetParent(pivot.transform, false);
            anchor.transform.position = new Vector3(bedBounds.center.x, bedBounds.max.y, bedBounds.max.z - 0.55f);
            anchors[i] = anchor.transform;
        }

        // The cabinet as one solid thing for the interactor and the NavMesh bake.
        Bounds all = RendererBounds(root);
        BoxCollider box = root.AddComponent<BoxCollider>();
        box.center = root.transform.InverseTransformPoint(all.center);
        box.size = all.size;

        Transform yourBody = BuildBankBody(root.transform, origin);
        Transform toeTag = BuildToeTag(root.transform, font, fontMat, cardMat, out TextMeshPro toeText);

        MorgueDrawerBank bank = root.AddComponent<MorgueDrawerBank>();
        SerializedObject so = new SerializedObject(bank);
        SetArray(so.FindProperty("trays"), pivots);
        SetArray(so.FindProperty("doorHinges"), hinges);
        SerializedProperty swingProp = so.FindProperty("doorSwingDegrees");
        swingProp.arraySize = swings.Length;
        for (int i = 0; i < swings.Length; i++) swingProp.GetArrayElementAtIndex(i).floatValue = swings[i];
        SetArray(so.FindProperty("tags"), tags);
        SetArray(so.FindProperty("trayAnchors"), anchors);
        so.FindProperty("yourBody").objectReferenceValue = yourBody;
        so.FindProperty("toeTag").objectReferenceValue = toeTag;
        so.FindProperty("toeTagText").objectReferenceValue = toeText;
        so.FindProperty("slideLocal").vector3Value = new Vector3(0f, 0f, BankSlide);
        so.ApplyModifiedPropertiesWithoutUndo();

        // The pack's drawers are batching-static; a clone that has to roll a tray out must not be (static batching would weld it to the cabinet).
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        root.SetActive(false);
        return bank;
    }

    private static void SetArray(SerializedProperty array, Object[] items)
    {
        array.arraySize = items.Length;
        for (int i = 0; i < items.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = items[i];
    }

    /// <summary>The player's own robot (the stripped PlayerRobot, in its idle pose) lying on its back, head toward +Z, resting on the holder's origin. Inactive.</summary>
    private static Transform BuildBankBody(Transform root, Vector3 origin)
    {
        GameObject holder = new GameObject("YourBody");
        holder.transform.SetParent(root, false);
        holder.transform.position = origin;

        Transform robot = InstantiateRobotNode(holder.transform);
        if (robot == null)
        {
            Debug.LogWarning("InteractableKitBuilder: the PlayerRobot prefab is missing - drawer YOURS will show an empty tray.");
            Object.DestroyImmediate(holder);
            return null;
        }
        robot.name = "Robot";
        StripNightmarePhysics(robot.gameObject);
        Animator animator = robot.GetComponent<Animator>();
        RuntimeAnimatorController idle = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(PatientIdleControllerPath);
        if (animator != null && idle != null) animator.runtimeAnimatorController = idle;

        // Standing size (renderer bounds in the holder's frame), then lie it down: local Z (its face) to world up, local Y (its head) to +Z.
        Bounds standing = RendererBounds(holder);
        Vector3 c = standing.center - holder.transform.position;
        float k = Mathf.Min(1f, 1.0f / Mathf.Max(0.01f, standing.size.y));
        Quaternion lie = Quaternion.LookRotation(Vector3.up, Vector3.forward);
        robot.localScale = Vector3.one * k;
        robot.localRotation = lie;
        robot.localPosition = -(lie * (c * k)) + new Vector3(0f, k * standing.size.z * 0.5f + 0.01f, 0f);
        holder.SetActive(false);
        return holder.transform;
    }

    /// <summary>Elias Vogt's toe tag: a small card lying flat, text readable from the drawer's front. Inactive; MorgueDrawerBank sets its text.</summary>
    private static Transform BuildToeTag(Transform root, TMP_FontAsset font, Material fontMat, Material cardMat, out TextMeshPro text)
    {
        GameObject holder = new GameObject("ToeTag");
        holder.transform.SetParent(root, false);
        Primitive(holder.transform, PrimitiveType.Quad, "Card", new Vector3(0f, 0.008f, 0f), new Vector3(0.42f, 0.24f, 1f), cardMat, keepCollider: false, localRotation: Quaternion.Euler(90f, 0f, 0f));

        GameObject textGo = new GameObject("Text");
        textGo.transform.SetParent(holder.transform, false);
        textGo.transform.localPosition = new Vector3(0f, 0.014f, 0f);
        textGo.transform.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.back); // read from the front of the drawer, looking down
        text = textGo.AddComponent<TextMeshPro>();
        if (font != null) text.font = font;
        if (font != null && fontMat != null) text.fontSharedMaterial = fontMat;
        text.text = "E. VOGT";
        text.alignment = TextAlignmentOptions.Center;
        text.color = new Color(0.12f, 0.12f, 0.13f);
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.enableAutoSizing = true;
        text.fontSizeMin = 0.2f;
        text.fontSizeMax = 0.8f;
        text.lineSpacing = -10f;
        text.rectTransform.sizeDelta = new Vector2(0.38f, 0.21f);
        holder.SetActive(false);
        return holder.transform;
    }

    // ---------------------------------------------------------------- the ampoule

    internal static NightmareAmpoule BuildNightmareAmpoule(Transform parent, Vector3 localPosition)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AmpoulePrefabPath);
        if (prefab == null)
        {
            Debug.LogError($"InteractableKitBuilder: {AmpoulePrefabPath} not found - the ampoule template was not built.");
            return null;
        }

        GameObject root = new GameObject("NightmareAmpoule");
        root.transform.SetParent(parent, false);
        root.transform.localPosition = localPosition;
        Vector3 origin = root.transform.position;

        GameObject model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
        PrefabUtility.UnpackPrefabInstance(model, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        model.name = "Model";
        StripNightmarePhysics(model);
        model.transform.localPosition = Vector3.zero;
        model.transform.localRotation = Quaternion.Euler(0f, 0f, 90f); // lying on its side
        model.transform.localScale = Vector3.one;
        Bounds b = RendererBounds(model);
        float longSide = Mathf.Max(b.size.x, b.size.y, b.size.z);
        model.transform.localScale = Vector3.one * (AmpouleHeight / Mathf.Max(0.001f, longSide));
        b = RendererBounds(model);
        model.transform.position += new Vector3(origin.x - b.center.x, origin.y - b.min.y, origin.z - b.center.z);
        b = RendererBounds(model);

        Material tinted = AssetDatabase.LoadAssetAtPath<Material>(AmpouleMaterialPath);
        if (tinted == null)
        {
            Renderer first = model.GetComponentInChildren<Renderer>();
            Material source = first != null ? first.sharedMaterial : null;
            if (source != null)
            {
                tinted = new Material(source) { name = "Nightmare_Ampoule" };
                if (tinted.HasProperty("_BaseColor")) tinted.SetColor("_BaseColor", new Color(0.55f, 0.75f, 1f, tinted.GetColor("_BaseColor").a));
                tinted.EnableKeyword("_EMISSION");
                tinted.SetColor("_EmissionColor", new Color(0.12f, 0.4f, 1f) * 0.9f);
                tinted.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                AssetDatabase.CreateAsset(tinted, AmpouleMaterialPath);
            }
        }
        if (tinted != null)
        {
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>(true))
            {
                Material[] mats = new Material[r.sharedMaterials.Length];
                for (int i = 0; i < mats.Length; i++) mats[i] = tinted;
                r.sharedMaterials = mats;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        SphereCollider sphere = root.AddComponent<SphereCollider>();
        sphere.isTrigger = true;
        sphere.center = root.transform.InverseTransformPoint(b.center);
        sphere.radius = 0.3f;

        NightmareAmpoule ampoule = root.AddComponent<NightmareAmpoule>();
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true)) GameObjectUtility.SetStaticEditorFlags(t.gameObject, 0);
        root.SetActive(false);
        return ampoule;
    }
}
