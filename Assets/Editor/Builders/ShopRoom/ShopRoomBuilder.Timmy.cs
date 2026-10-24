using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The shop's salesman is "The Commissary Clerk" (<c>Assets/SourceFiles/Models/Shopkeeper/Shopkeeper_Model.fbx</c>, a 2.0 m
/// tall gaunt asylum patient in an institutional smock and commissary apron, falling back to a stripped Timmy robot only if
/// the FBX is missing), on ShopTimmy.controller: Idle (Starter Assets Stand--Idle), Talk (the Mixamo Talking clip, also used
/// by the hub NPCs) and Wave (Mixamo Waving.fbx, optional - copied from the shared Mixamo library when it is there; without
/// it a greeting is a short burst of talking). ShopkeeperAnimator plays them; ShopRoom and HubShop call it.
///
/// He is a hand-editable prefab, Assets/Prefabs/ShopSalesman.prefab: the first build generates it, after that every
/// build only places it (edit him in Prefab Mode; deleting the prefab is the reset). <see cref="SwapSalesmanToTimmy"/>
/// brings an existing room and the Asylum Hub Commissary there without rebuilding anything else.
/// </summary>
public static partial class ShopRoomBuilder
{
    private const string RobotPrefabPath = "Assets/Prefabs/PlayerRobot.prefab";
    public const string ShopkeeperModelPath = "Assets/SourceFiles/Models/Shopkeeper/Shopkeeper_Model.fbx";
    private const string ShopkeeperDiffusePath = "Assets/SourceFiles/Models/Shopkeeper/Shopkeeper_Atlas_Diffuse.png";
    private const string ShopkeeperNormalPath = "Assets/SourceFiles/Models/Shopkeeper/Shopkeeper_Atlas_Normal.png";
    private const string ShopkeeperMatFolder = "Assets/SourceFiles/Materials/Shop";
    private const string ShopkeeperMaterialPath = ShopkeeperMatFolder + "/Shopkeeper_Patient.mat";
    private const string ShopkeeperChildName = "Shopkeeper";

    /// <summary>The hand-editable salesman (Shopkeeper + blocker + ShopkeeperAnimator). Builders only place it once it exists.</summary>
    public const string SalesmanPrefabPath = "Assets/Prefabs/ShopSalesman.prefab";
    private const string ShopAnimDir = "Assets/SourceFiles/Animation/Shop";
    private const string ShopkeeperControllerPath = ShopAnimDir + "/ShopTimmy.controller";
    private const string IdleClipPath = "Assets/SourceFiles/StarterAssets/ThirdPersonController/Character/Animations/Stand--Idle.anim.fbx";
    private const string TalkingClipPath = "Assets/SourceFiles/Animation/Mixamo/Talking.fbx";
    private const string WavingClipPath = "Assets/SourceFiles/Animation/Mixamo/Waving.fbx";
    // The shared Mixamo library outside the project: copy from it when a clip is needed, never move.
    private const string WavingLibraryPath = "D:/Unity/essentials/Assets/Mixamo/Waving.fbx";

    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Shopkeeper (Commissary Clerk)", priority = 136)]
    public static void MenuSwapSalesman() => SwapSalesmanToTimmy();

    /// <summary>
    /// Non-null when the open scene's ShopRoom still has the old salesman, its salesman is not an instance of
    /// ShopSalesman.prefab, Shopkeeper_Model.fbx exists while the prefab still wears Timmy, or Wave is missing.
    /// </summary>
    public static string TimmySalesmanProblem()
    {
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null) return null; // reported by "Shop room"
        Transform salesman = shop.transform.Find("Salesman");
        if (salesman == null) return "the shop has no Salesman";

        SerializedObject so = new SerializedObject(shop);
        if (so.FindProperty("shopkeeper").objectReferenceValue == null) return "the salesman has no ShopkeeperAnimator wired";
        if (!IsSalesmanPrefabInstance(salesman)) return $"the salesman is not an instance of {SalesmanPrefabPath}";
        if (WaveAvailable() && !ControllerHasWave()) return "Waving.fbx is available but the shopkeeper controller has no Wave state";
        if (File.Exists(ShopkeeperModelPath) && salesman.Find(ShopkeeperChildName) == null)
            return $"the salesman is still the old Timmy robot instead of {ShopkeeperModelPath}";
        if (!File.Exists(ShopkeeperModelPath) && WearsPlainTimmy(salesman))
            return "the salesman still wears the plain Timmy material (no Shopkeeper brass skin)";
        if (so.FindProperty("headPivot").objectReferenceValue == null)
            return "the salesman's headPivot is not wired on ShopRoom";

        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub != null && hub.shop != null && hub.shop.headPivot == null)
            return "the Commissary salesman's headPivot is not wired on HubShop";
        return null;
    }

    /// <summary>
    /// Dialog-free migration. Upgrades ShopSalesman.prefab to the 2.0 m Commissary Clerk (Shopkeeper_Model.fbx) when present,
    /// wires headPivot + shopkeeper on both ShopRoom (and ShopRoom.prefab) and AsylumHub's HubShop (and AsylumHub.prefab).
    /// </summary>
    public static bool SwapSalesmanToTimmy()
    {
        Scene scene = SceneManager.GetActiveScene();
        ShopRoom shop = Object.FindAnyObjectByType<ShopRoom>(FindObjectsInactive.Include);
        if (shop == null) return false;
        Transform salesman = shop.transform.Find("Salesman");
        EnsureShopkeeperController();

        bool hasCustomModel = EnsureShopkeeperModel() != null;
        if (hasCustomModel)
        {
            UpgradeSalesmanPrefabToCustomModel();
        }

        SerializedObject so = new SerializedObject(shop);
        bool isWired = salesman != null && so.FindProperty("shopkeeper").objectReferenceValue != null;
        bool prefabExists = AssetDatabase.LoadAssetAtPath<GameObject>(SalesmanPrefabPath) != null;

        if (!isWired || !IsSalesmanPrefabInstance(salesman))
        {
            if (isWired && !prefabExists)
            {
                PrefabUtility.SaveAsPrefabAssetAndConnect(salesman.gameObject, SalesmanPrefabPath, InteractionMode.AutomatedAction);
            }
            else
            {
                if (salesman != null) Undo.DestroyObjectImmediate(salesman.gameObject);
                salesman = PlaceSalesman(shop.transform);
            }
        }

        if (!hasCustomModel) SkinSalesmanPrefab();

        Transform head = SalesmanParts(salesman, out ShopkeeperAnimator shopkeeper);
        so.FindProperty("headPivot").objectReferenceValue = head;
        so.FindProperty("shopkeeper").objectReferenceValue = shopkeeper;
        so.ApplyModifiedPropertiesWithoutUndo();

        if (PrefabUtility.IsPartOfPrefabInstance(shop.gameObject))
        {
            PrefabUtility.RecordPrefabInstancePropertyModifications(shop);
            string shopPrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(shop.gameObject);
            if (shopPrefabPath == PrefabPath)
            {
                PrefabUtility.ApplyObjectOverride(shop, PrefabPath, InteractionMode.AutomatedAction);
            }
        }

        // Also update the Commissary salesman in AsylumHub if present
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub != null && hub.shop != null)
        {
            Transform hubSalesman = hub.shop.transform.Find("Counter/Salesman");
            if (hubSalesman != null)
            {
                Transform hubHead = SalesmanParts(hubSalesman, out ShopkeeperAnimator hubKeeper);
                SerializedObject hubSo = new SerializedObject(hub.shop);
                hubSo.FindProperty("headPivot").objectReferenceValue = hubHead;
                hubSo.FindProperty("shopkeeper").objectReferenceValue = hubKeeper;
                hubSo.ApplyModifiedPropertiesWithoutUndo();
                if (PrefabUtility.IsPartOfPrefabInstance(hub.shop))
                {
                    PrefabUtility.RecordPrefabInstancePropertyModifications(hub.shop);
                    string hubPrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(hub.gameObject);
                    if (!string.IsNullOrEmpty(hubPrefabPath))
                    {
                        PrefabUtility.ApplyObjectOverride(hub.shop, hubPrefabPath, InteractionMode.AutomatedAction);
                    }
                }
            }
        }

        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log($"Shop salesman is {(hasCustomModel ? "The Commissary Clerk (Shopkeeper_Model.fbx)" : "Timmy")}, an instance of {SalesmanPrefabPath}. Save the scene (Ctrl+S) to keep it.", shop);
        return true;
    }

    private static GameObject EnsureShopkeeperModel()
    {
        if (!File.Exists(ShopkeeperModelPath)) return null;
        ModelImporter importer = AssetImporter.GetAtPath(ShopkeeperModelPath) as ModelImporter;
        if (importer == null) return null;
        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
            importer.importAnimation)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(ShopkeeperModelPath);
        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ShopkeeperModelPath))
        {
            if (o is Avatar a) avatar = a;
        }
        if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"ShopRoomBuilder: {ShopkeeperModelPath} did not import as a valid Humanoid avatar.");
            return null;
        }
        EnsureShopkeeperMaterial();
        return model;
    }

    private const string ShopkeeperModelDir = "Assets/SourceFiles/Models/Shopkeeper";

    private static Material EnsurePbrSubmeshMaterial(
        string matName, string matPath, string diffFile, string normFile,
        float smoothness, float metallic = 0f, string metalSmoothFile = null, string detailNormFile = null, float detailScale = 28f)
    {
        if (!string.IsNullOrEmpty(normFile))
        {
            string normPath = $"{ShopkeeperModelDir}/{normFile}";
            TextureImporter normImp = AssetImporter.GetAtPath(normPath) as TextureImporter;
            if (normImp != null && normImp.textureType != TextureImporterType.NormalMap)
            {
                normImp.textureType = TextureImporterType.NormalMap;
                normImp.SaveAndReimport();
            }
        }
        if (!string.IsNullOrEmpty(detailNormFile))
        {
            string detPath = $"{ShopkeeperModelDir}/{detailNormFile}";
            TextureImporter detImp = AssetImporter.GetAtPath(detPath) as TextureImporter;
            if (detImp != null && detImp.textureType != TextureImporterType.NormalMap)
            {
                detImp.textureType = TextureImporterType.NormalMap;
                detImp.SaveAndReimport();
            }
        }
        if (!string.IsNullOrEmpty(metalSmoothFile))
        {
            string msPath = $"{ShopkeeperModelDir}/{metalSmoothFile}";
            TextureImporter msImp = AssetImporter.GetAtPath(msPath) as TextureImporter;
            if (msImp != null && msImp.sRGBTexture)
            {
                msImp.sRGBTexture = false;
                msImp.SaveAndReimport();
            }
        }

        Texture2D diff = !string.IsNullOrEmpty(diffFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{ShopkeeperModelDir}/{diffFile}") : null;
        Texture2D nrm  = !string.IsNullOrEmpty(normFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{ShopkeeperModelDir}/{normFile}") : null;
        Texture2D ms   = !string.IsNullOrEmpty(metalSmoothFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{ShopkeeperModelDir}/{metalSmoothFile}") : null;
        Texture2D det  = !string.IsNullOrEmpty(detailNormFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{ShopkeeperModelDir}/{detailNormFile}") : null;

        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = matName };
            AssetDatabase.CreateAsset(mat, matPath);
        }

        if (diff != null)
        {
            mat.SetTexture("_BaseMap", diff);
            mat.SetTexture("_MainTex", diff);
        }
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_Metallic", metallic);
        if (nrm != null)
        {
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1.15f);
        }
        if (ms != null)
        {
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.SetFloat("_SmoothnessTextureChannel", 0f);
        }
        if (det != null)
        {
            mat.EnableKeyword("_DETAIL_MULX2");
            mat.SetTexture("_DetailNormalMap", det);
            mat.SetTextureScale("_DetailNormalMap", new Vector2(detailScale, detailScale));
            mat.SetFloat("_DetailNormalMapScale", 0.50f);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material EnsureTransparentGlassMaterial(string matName, string matPath, Color color, float smoothness)
    {
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = matName };
            AssetDatabase.CreateAsset(mat, matPath);
        }
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend", 0f);
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.SetColor("_BaseColor", color);
        mat.SetFloat("_Metallic", 0f);
        mat.SetFloat("_Smoothness", smoothness);
        mat.SetFloat("_ClearCoatMask", 1f);
        mat.SetFloat("_ClearCoatSmoothness", 0.98f);
        mat.EnableKeyword("_CLEARCOAT");
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static Material EnsureShopkeeperMaterial()
    {
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials"))
            AssetDatabase.CreateFolder("Assets/SourceFiles", "Materials");
        if (!AssetDatabase.IsValidFolder(ShopkeeperMatFolder))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials", "Shop");

        EnsurePbrSubmeshMaterial("Shopkeeper_Head",    $"{ShopkeeperMatFolder}/Shopkeeper_Head.mat",    "Shopkeeper_Head_Diffuse.png",   "Shopkeeper_Head_Normal.png",   1.0f,  0f, "Shopkeeper_Head_MetalSmooth.png", "Shopkeeper_SkinMicroPore_Normal.png", 28f);
        Material bodyMat = EnsurePbrSubmeshMaterial("Shopkeeper_Patient", ShopkeeperMaterialPath,       "Shopkeeper_Body_Diffuse.png",   "Shopkeeper_Body_Normal.png",   0.22f, 0f, null, "Shopkeeper_SkinMicroPore_Normal.png", 32f);
        EnsurePbrSubmeshMaterial("Shopkeeper_Apron",   $"{ShopkeeperMatFolder}/Shopkeeper_Apron.mat",   "Shopkeeper_Apron_Diffuse.png",  "Shopkeeper_Apron_Normal.png",  0.18f, 0f);
        EnsurePbrSubmeshMaterial("Shopkeeper_Items",   $"{ShopkeeperMatFolder}/Shopkeeper_Items.mat",   "Shopkeeper_Items_Diffuse.png",  null,                           0.68f, 0.72f);
        EnsurePbrSubmeshMaterial("Shopkeeper_Jacket",  $"{ShopkeeperMatFolder}/Shopkeeper_Jacket.mat",  "Shopkeeper_Jacket_Diffuse.png", "Shopkeeper_Jacket_Normal.png", 1.0f,  0f, "Shopkeeper_Jacket_MetalSmooth.png");
        EnsurePbrSubmeshMaterial("Shopkeeper_Pants",   $"{ShopkeeperMatFolder}/Shopkeeper_Pants.mat",   "Shopkeeper_Pants_Diffuse.png",  "Shopkeeper_Pants_Normal.png",  1.0f,  0f, "Shopkeeper_Pants_MetalSmooth.png");
        EnsurePbrSubmeshMaterial("Shopkeeper_Boots",   $"{ShopkeeperMatFolder}/Shopkeeper_Boots.mat",   "Shopkeeper_Boots_Diffuse.png",  "Shopkeeper_Boots_Normal.png",  1.0f,  0f, "Shopkeeper_Boots_MetalSmooth.png");

        EnsureTransparentGlassMaterial("Shopkeeper_Eyes", $"{ShopkeeperMatFolder}/Shopkeeper_Eyes.mat", new Color(0.95f, 0.98f, 1.0f, 0.08f), 0.985f);
        EnsureTransparentGlassMaterial("Shopkeeper_Glasses_Lens", $"{ShopkeeperMatFolder}/Shopkeeper_Glasses_Lens.mat", new Color(0.84f, 0.92f, 0.90f, 0.14f), 0.97f);

        string frameMatPath = $"{ShopkeeperMatFolder}/Shopkeeper_Glasses_Frame.mat";
        Material frameMat = AssetDatabase.LoadAssetAtPath<Material>(frameMatPath);
        if (frameMat == null)
        {
            frameMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Shopkeeper_Glasses_Frame" };
            AssetDatabase.CreateAsset(frameMat, frameMatPath);
        }
        frameMat.SetColor("_BaseColor", new Color(0.76f, 0.60f, 0.34f, 1.0f));
        frameMat.SetFloat("_Metallic", 0.92f);
        frameMat.SetFloat("_Smoothness", 0.78f);
        EditorUtility.SetDirty(frameMat);

        string shardsMatPath = $"{ShopkeeperMatFolder}/Shopkeeper_Shards.mat";
        Material shardsMat = AssetDatabase.LoadAssetAtPath<Material>(shardsMatPath);
        if (shardsMat == null)
        {
            shardsMat = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = "Shopkeeper_Shards" };
            AssetDatabase.CreateAsset(shardsMat, shardsMatPath);
        }
        shardsMat.SetColor("_BaseColor", new Color(1.0f, 0.58f, 0.08f, 1.0f));
        shardsMat.SetFloat("_Smoothness", 0.90f);
        shardsMat.SetFloat("_Metallic", 0.0f);
        shardsMat.EnableKeyword("_EMISSION");
        shardsMat.SetColor("_EmissionColor", new Color(2.8f, 1.35f, 0.15f, 1.0f));
        EditorUtility.SetDirty(shardsMat);

        AssetDatabase.SaveAssets();
        return bodyMat;
    }

    private static void UpgradeSalesmanPrefabToCustomModel()
    {
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SalesmanPrefabPath);
        if (existingPrefab == null) return;

        GameObject root = PrefabUtility.LoadPrefabContents(SalesmanPrefabPath);
        try
        {
            for (int i = root.transform.childCount - 1; i >= 0; i--)
            {
                Object.DestroyImmediate(root.transform.GetChild(i).gameObject);
            }
            CapsuleCollider blocker = root.GetComponent<CapsuleCollider>();
            if (blocker == null) blocker = root.AddComponent<CapsuleCollider>();
            blocker.center = new Vector3(0f, 1.0f, 0f);
            blocker.radius = 0.3f;
            blocker.height = 2.0f;

            BuildTimmy(root.transform, out _);
            PrefabUtility.SaveAsPrefabAsset(root, SalesmanPrefabPath);
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    /// <summary>
    /// An instance of ShopSalesman.prefab behind the counter. The first call generates the prefab (Shopkeeper, his blocker
    /// collider and ShopkeeperAnimator); after that the prefab is the source of truth and is only placed.
    /// </summary>
    private static Transform PlaceSalesman(Transform room)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SalesmanPrefabPath);
        Transform salesman;
        if (prefab != null)
        {
            salesman = ((GameObject)PrefabUtility.InstantiatePrefab(prefab, room)).transform;
            Undo.RegisterCreatedObjectUndo(salesman.gameObject, "Shop Salesman");
        }
        else
        {
            salesman = Group(room, "Salesman");
            CapsuleCollider blocker = salesman.gameObject.AddComponent<CapsuleCollider>();
            blocker.center = new Vector3(0f, 1.0f, 0f);
            blocker.radius = 0.3f;
            blocker.height = 2.0f;
            BuildTimmy(salesman, out _);
        }

        salesman.name = "Salesman";
        salesman.localPosition = new Vector3(0f, 0f, 5.1f);
        salesman.localRotation = Quaternion.Euler(0f, 180f, 0f);
        if (prefab == null) PrefabUtility.SaveAsPrefabAssetAndConnect(salesman.gameObject, SalesmanPrefabPath, InteractionMode.AutomatedAction);
        return salesman;
    }

    /// <summary>The ShopkeeperAnimator on a placed salesman, and the Head bone ShopRoom turns (null without a Humanoid body).</summary>
    private static Transform SalesmanParts(Transform salesman, out ShopkeeperAnimator shopkeeper)
    {
        shopkeeper = salesman.GetComponent<ShopkeeperAnimator>();
        Animator animator = salesman.GetComponentInChildren<Animator>();
        Transform head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) Debug.LogWarning("ShopRoomBuilder: the salesman has no Humanoid Head bone, so his head will not follow the player.", salesman);
        return head;
    }

    private const string PlainTimmyMaterialPath = "Assets/SourceFiles/TimmyRobot/Materials/TimmyRobot.mat";

    /// <summary>True while any of the salesman's renderers still uses the Starter Assets TimmyRobot material.</summary>
    private static bool WearsPlainTimmy(Transform salesman)
    {
        Material plain = AssetDatabase.LoadAssetAtPath<Material>(PlainTimmyMaterialPath);
        foreach (Renderer r in salesman.GetComponentsInChildren<Renderer>(true))
        {
            foreach (Material mat in r.sharedMaterials) if (mat != null && mat == plain) return true;
        }
        return false;
    }

    /// <summary>Swaps the plain TimmyRobot material for <paramref name="skin"/>; any other material (a hand change) is kept.</summary>
    private static void ReplaceTimmyMaterial(GameObject body, Material skin)
    {
        Material plain = AssetDatabase.LoadAssetAtPath<Material>(PlainTimmyMaterialPath);
        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            bool changed = false;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] != plain) continue;
                mats[i] = skin;
                changed = true;
            }
            if (changed) r.sharedMaterials = mats;
        }
    }

    /// <summary>Puts the Shopkeeper brass skin on ShopSalesman.prefab if it still wears the plain Timmy material. Edits the prefab asset.</summary>
    private static bool SkinSalesmanPrefab()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(SalesmanPrefabPath);
        try
        {
            if (!WearsPlainTimmy(root.transform)) return false;
            Material skin = HunterSkinBuilder.EnsureShopkeeperSkin();
            if (skin == null) return false;
            ReplaceTimmyMaterial(root, skin);
            PrefabUtility.SaveAsPrefabAsset(root, SalesmanPrefabPath);
            Debug.Log($"Shop salesman: the brass Shopkeeper skin is on {SalesmanPrefabPath}.");
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool IsSalesmanPrefabInstance(Transform salesman) =>
        PrefabUtility.IsPartOfPrefabInstance(salesman.gameObject) &&
        PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(salesman.gameObject) == SalesmanPrefabPath;

    /// <summary>Builds the salesman visual body under <paramref name="salesman"/>, idling, with a ShopkeeperAnimator on the group. Returns his Head bone.</summary>
    private static Transform BuildTimmy(Transform salesman, out ShopkeeperAnimator shopkeeper)
    {
        shopkeeper = null;
        GameObject customModel = EnsureShopkeeperModel();
        GameObject body;
        Animator animator;

        if (customModel != null)
        {
            body = (GameObject)PrefabUtility.InstantiatePrefab(customModel);
            PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            body.name = ShopkeeperChildName;
            body.tag = "Untagged";
            body.transform.SetParent(salesman, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;
            MakeMovable(body.transform);

            Material bodyMat   = EnsureShopkeeperMaterial();
            Material headMat   = AssetDatabase.LoadAssetAtPath<Material>($"{ShopkeeperMatFolder}/Shopkeeper_Head.mat");
            Material apronMat  = AssetDatabase.LoadAssetAtPath<Material>($"{ShopkeeperMatFolder}/Shopkeeper_Apron.mat");
            Material itemsMat  = AssetDatabase.LoadAssetAtPath<Material>($"{ShopkeeperMatFolder}/Shopkeeper_Items.mat");
            Material shardsMat = AssetDatabase.LoadAssetAtPath<Material>($"{ShopkeeperMatFolder}/Shopkeeper_Shards.mat");
            Material eyesMat   = AssetDatabase.LoadAssetAtPath<Material>($"{ShopkeeperMatFolder}/Shopkeeper_Eyes.mat");
            Material frameMat  = AssetDatabase.LoadAssetAtPath<Material>($"{ShopkeeperMatFolder}/Shopkeeper_Glasses_Frame.mat");
            Material lensMat   = AssetDatabase.LoadAssetAtPath<Material>($"{ShopkeeperMatFolder}/Shopkeeper_Glasses_Lens.mat");
            Material jacketMat = AssetDatabase.LoadAssetAtPath<Material>($"{ShopkeeperMatFolder}/Shopkeeper_Jacket.mat");
            Material pantsMat  = AssetDatabase.LoadAssetAtPath<Material>($"{ShopkeeperMatFolder}/Shopkeeper_Pants.mat");
            Material bootsMat  = AssetDatabase.LoadAssetAtPath<Material>($"{ShopkeeperMatFolder}/Shopkeeper_Boots.mat");

            foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
            {
                Material targetMat = bodyMat;
                string rName = r.gameObject.name;
                if (rName.Contains("Head") && headMat != null) targetMat = headMat;
                else if (rName.Contains("Apron") && apronMat != null) targetMat = apronMat;
                else if (rName.Contains("Items") && itemsMat != null) targetMat = itemsMat;
                else if (rName.Contains("Shards") && shardsMat != null) targetMat = shardsMat;
                else if (rName.Contains("Eyes") && eyesMat != null) targetMat = eyesMat;
                else if (rName.Contains("Glasses_Frame") && frameMat != null) targetMat = frameMat;
                else if (rName.Contains("Glasses_Lens") && lensMat != null) targetMat = lensMat;
                else if (rName.Contains("Jacket") && jacketMat != null) targetMat = jacketMat;
                else if (rName.Contains("Pants") && pantsMat != null) targetMat = pantsMat;
                else if (rName.Contains("Boots") && bootsMat != null) targetMat = bootsMat;

                if (targetMat != null)
                {
                    Material[] mats = r.sharedMaterials;
                    for (int i = 0; i < mats.Length; i++) mats[i] = targetMat;
                    r.sharedMaterials = mats;
                }
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
            }

            animator = body.GetComponentInChildren<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
            if (animator.avatar == null)
            {
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(ShopkeeperModelPath))
                {
                    if (o is Avatar a) animator.avatar = a;
                }
            }
        }
        else
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RobotPrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"ShopRoomBuilder: {RobotPrefabPath} is missing, so the shop has no salesman body.");
                return null;
            }

            body = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Undo.RegisterCreatedObjectUndo(body, "Shop Timmy");
            InteractableKitBuilder.StripRobotToVisuals(body);
            body.name = "Timmy";
            body.tag = "Untagged";
            body.transform.SetParent(salesman, false);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            body.transform.localScale = Vector3.one;
            MakeMovable(body.transform);
            Material skin = HunterSkinBuilder.EnsureShopkeeperSkin();
            if (skin != null) ReplaceTimmyMaterial(body, skin);

            animator = body.GetComponentInChildren<Animator>();
            if (animator == null) animator = body.AddComponent<Animator>();
        }

        animator.transform.localRotation = Quaternion.identity;
        animator.runtimeAnimatorController = EnsureShopkeeperController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        // Pose him now, so the room shows him idling in the Editor instead of in his bind pose.
        AnimationClip idle = FirstClip(IdleClipPath);
        if (idle != null)
        {
            idle.SampleAnimation(animator.gameObject, 0f);
            body.transform.localPosition = Vector3.zero;
            body.transform.localRotation = Quaternion.identity;
            animator.transform.localRotation = Quaternion.identity;
        }

        shopkeeper = salesman.GetComponent<ShopkeeperAnimator>();
        if (shopkeeper == null) shopkeeper = salesman.gameObject.AddComponent<ShopkeeperAnimator>();
        SerializedObject so = new SerializedObject(shopkeeper);
        so.FindProperty("animator").objectReferenceValue = animator;
        so.ApplyModifiedPropertiesWithoutUndo();

        Transform head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) Debug.LogWarning("ShopRoomBuilder: the salesman has no Humanoid Head bone, so his head will not follow the player.", body);
        return head;
    }

    /// <summary>ShopTimmy.controller with Idle (default), Talk and, once Waving.fbx exists, Wave (which returns to Idle). Adds missing states only.</summary>
    private static AnimatorController EnsureShopkeeperController()
    {
        EnsureWavingClip();

        if (!AssetDatabase.IsValidFolder(ShopAnimDir)) AssetDatabase.CreateFolder("Assets/SourceFiles/Animation", "Shop");
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ShopkeeperControllerPath);
        if (controller == null) controller = AnimatorController.CreateAnimatorControllerAtPath(ShopkeeperControllerPath);

        AnimatorStateMachine sm = controller.layers[0].stateMachine;
        AnimatorState idle = EnsureState(sm, "Idle", FirstClip(IdleClipPath));
        EnsureState(sm, "Talk", FirstClip(TalkingClipPath));
        sm.defaultState = idle;

        AnimationClip waving = FirstClip(WavingClipPath);
        if (waving != null)
        {
            AnimatorState wave = EnsureState(sm, "Wave", waving);
            if (!wave.transitions.Any(t => t.destinationState == idle))
            {
                AnimatorStateTransition back = wave.AddTransition(idle);
                back.hasExitTime = true;
                back.exitTime = 0.9f;
                back.duration = 0.25f;
                back.hasFixedDuration = true;
            }
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimatorState EnsureState(AnimatorStateMachine sm, string name, Motion motion)
    {
        AnimatorState state = sm.states.Select(s => s.state).FirstOrDefault(s => s.name == name);
        if (state == null) state = sm.AddState(name);
        if (motion != null) state.motion = motion;
        else Debug.LogWarning($"ShopRoomBuilder: no clip for Timmy's {name} state.");
        return state;
    }

    /// <summary>Copies Waving.fbx from the Mixamo library when the project lacks it, and imports it Humanoid, not looping, in place.</summary>
    private static void EnsureWavingClip()
    {
        if (!File.Exists(WavingClipPath))
        {
            if (!File.Exists(WavingLibraryPath)) return;
            File.Copy(WavingLibraryPath, WavingClipPath);
            AssetDatabase.ImportAsset(WavingClipPath, ImportAssetOptions.ForceUpdate);
        }

        ModelImporter importer = AssetImporter.GetAtPath(WavingClipPath) as ModelImporter;
        if (importer == null) return;
        bool dirty = false;
        if (importer.animationType != ModelImporterAnimationType.Human) { importer.animationType = ModelImporterAnimationType.Human; dirty = true; }
        if (importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel) { importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel; dirty = true; }
        ModelImporterClipAnimation[] clips = importer.clipAnimations;
        if (clips == null || clips.Length == 0) clips = importer.defaultClipAnimations;
        if (clips.Length > 0 && (clips[0].loopTime || clips[0].name != "Waving" || !clips[0].keepOriginalPositionXZ))
        {
            clips[0].name = "Waving";
            clips[0].loopTime = false;
            clips[0].keepOriginalOrientation = true;
            clips[0].keepOriginalPositionY = true;
            clips[0].keepOriginalPositionXZ = true;
            importer.clipAnimations = clips;
            dirty = true;
        }
        if (dirty) importer.SaveAndReimport();
    }

    private static bool WaveAvailable() => File.Exists(WavingClipPath) || File.Exists(WavingLibraryPath);

    private static bool ControllerHasWave()
    {
        AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ShopkeeperControllerPath);
        return controller != null && controller.layers[0].stateMachine.states.Any(s => s.state.name == "Wave" && s.state.motion != null);
    }

    private static AnimationClip FirstClip(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().FirstOrDefault(c => !c.name.StartsWith("__preview__"));
}
