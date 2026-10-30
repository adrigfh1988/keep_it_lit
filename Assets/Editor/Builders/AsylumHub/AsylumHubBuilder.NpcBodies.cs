using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The hub's people are Timmy robots: the six NPCs, the patients Hilde and Elias and the sheeted body in the morgue (the Adam Character
/// Pack they were built from was removed from the project for licence reasons, 11 Oct 2026). Each body is the Animator node of a stripped
/// PlayerRobot.prefab (<see cref="InteractableKitBuilder.InstantiateRobotNode"/>), in its own painted skin (HunterSkinBuilder.Npcs.cs).
/// Only the body changes: the holder, its HubNpc / HubPatient, anchors, colliders, voice and mission links stay. Build-missing-only, never
/// regenerates the hub. Tomas (Pete) and the finale Director (the Mixamo doctor) keep their human bodies.
/// </summary>
public static partial class AsylumHubBuilder
{
    /// <summary>The name of a robot NPC / patient body; the Problem checks key on it.</summary>
    public const string NpcRobotBodyName = "Body_Timmy";
    /// <summary>The name of Sister Agnes's custom 5-part 3D humanoid nurse body when <see cref="NurseAgnesModelPath"/> is present.</summary>
    public const string NurseAgnesBodyName = "Body_NurseAgnes";
    public const string NurseAgnesModelDir = "Assets/SourceFiles/Models/NurseAgnes";
    public const string NurseAgnesModelPath = NurseAgnesModelDir + "/NurseAgnes_Model.fbx";
    /// <summary>The name of Cook Bruno's custom Indie Horror 2-piece + props 3D humanoid body when <see cref="CookBrunoModelPath"/> is present.</summary>
    public const string CookBrunoBodyName = "Body_CookBruno";
    public const string CookBrunoModelDir = "Assets/SourceFiles/Models/CookBruno";
    public const string CookBrunoModelPath = CookBrunoModelDir + "/CookBruno_Model.fbx";
    /// <summary>The name of Elias Vogt's custom Indie Horror 2-piece + #5 morgue tag 3D humanoid body when <see cref="EliasVogtModelPath"/> is present.</summary>
    public const string EliasVogtBodyName = "Body_EliasVogt";
    public const string EliasVogtModelDir = "Assets/SourceFiles/Models/EliasVogt";
    public const string EliasVogtModelPath = EliasVogtModelDir + "/EliasVogt_Model.fbx";
    /// <summary>The name of Hilde Brandt's custom Indie Horror 2-piece + props 3D humanoid body when <see cref="HildeBrandtModelPath"/> is present.</summary>
    public const string HildeBrandtBodyName = "Body_HildeBrandt";
    public const string HildeBrandtModelDir = "Assets/SourceFiles/Models/HildeBrandt";
    public const string HildeBrandtModelPath = HildeBrandtModelDir + "/HildeBrandt_Model.fbx";
    /// <summary>The name of the custom 3D shrouded corpse in the morgue when <see cref="SheetedMorgueModelPath"/> is present.</summary>
    public const string SheetedMorgueBodyName = "Body_Sheeted_Morgue";
    public const string SheetedMorgueModelDir = "Assets/SourceFiles/Models/HorrorProps";
    public const string SheetedMorgueModelPath = SheetedMorgueModelDir + "/SheetedMorgueBody_HP.fbx";
    private const string NurseAgnesMatFolder = "Assets/SourceFiles/Materials/Asylum/Npcs";

    private static string ExpectedPatientBodyName(string patientName)
    {
        if (patientName == "Hilde" && System.IO.File.Exists(HildeBrandtModelPath))
            return HildeBrandtBodyName;
        if (patientName == "Elias" && System.IO.File.Exists(EliasVogtModelPath))
            return EliasVogtBodyName;
        return NpcRobotBodyName;
    }

    private static string ExpectedMorgueBodyName()
    {
        return System.IO.File.Exists(SheetedMorgueModelPath) ? SheetedMorgueBodyName : MorgueRobotName;
    }

    private static string ExpectedNpcBodyName(HubNpc npc)
    {
        if (npc != null && npc.who == HubNpc.Who.Agnes && System.IO.File.Exists(NurseAgnesModelPath))
            return NurseAgnesBodyName;
        if (npc != null && npc.who == HubNpc.Who.Bruno && System.IO.File.Exists(CookBrunoModelPath))
            return CookBrunoBodyName;
        return NpcRobotBodyName;
    }

    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Sister Agnes (Nurse)", priority = 137)]
    public static void MenuSwapSisterAgnes()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform npcs = hub != null && hub.life != null ? hub.life.Find(NpcsName) : null;
        if (npcs == null)
        {
            Debug.LogError("Sister Agnes: the hub has no Life/Npcs in the open scene.");
            return;
        }
        foreach (HubNpc npc in npcs.GetComponentsInChildren<HubNpc>(true))
        {
            if (npc.who != HubNpc.Who.Agnes) continue;
            string msg = SwapNpcBody(npc);
            if (Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include) != null)
            {
                InteractableKitBuilder.RebuildNightNurseTemplate();
            }
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
            ApplyHubToPrefab(hub);
            Debug.Log(msg + " Save the scene (Ctrl+S) to keep it.", npc);
            return;
        }
    }

    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Cook Bruno (Chef)", priority = 138)]
    public static void MenuSwapCookBruno()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform npcs = hub != null && hub.life != null ? hub.life.Find(NpcsName) : null;
        if (npcs == null)
        {
            Debug.LogError("Cook Bruno: the hub has no Life/Npcs in the open scene.");
            return;
        }
        foreach (HubNpc npc in npcs.GetComponentsInChildren<HubNpc>(true))
        {
            if (npc.who != HubNpc.Who.Bruno) continue;
            string msg = SwapNpcBody(npc);
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
            ApplyHubToPrefab(hub);
            Debug.Log(msg + " Save the scene (Ctrl+S) to keep it.", npc);
            return;
        }
    }

    /// <summary>What still has the wrong NPC body (null = all six match their expected body, or no hub / no NPCs yet - the F98 piece reports that).</summary>
    public static string NpcRobotProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform npcs = hub != null && hub.life != null ? hub.life.Find(NpcsName) : null;
        if (npcs == null) return null;
        List<string> missing = new List<string>();
        foreach (HubNpc npc in npcs.GetComponentsInChildren<HubNpc>(true))
        {
            string expected = ExpectedNpcBodyName(npc);
            if (npc.body == null || npc.body.name != expected) missing.Add(npc.name);
        }
        return missing.Count == 0 ? null : "NPC body needs update: " + string.Join(", ", missing);
    }

    /// <summary>ProjectSetup's fix: swap every outdated NPC body in the scene hub and apply it to the prefab. Marks the scene dirty.</summary>
    public static void NpcRobotRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        Transform npcs = hub != null && hub.life != null ? hub.life.Find(NpcsName) : null;
        if (npcs == null) { Debug.LogError("Asylum NPC bodies: the hub has no Life/Npcs (run Build Missing Pieces for the F98 NPCs first)."); return; }

        System.Text.StringBuilder report = new System.Text.StringBuilder();
        foreach (HubNpc npc in npcs.GetComponentsInChildren<HubNpc>(true))
        {
            string expected = ExpectedNpcBodyName(npc);
            if (npc.body != null && npc.body.name == expected) continue;
            report.AppendLine(SwapNpcBody(npc));
        }
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        ApplyHubToPrefab(hub);
        Debug.Log("Asylum NPC bodies:\n" + report.ToString().Trim());
    }

    /// <summary>
    /// Replaces the NPC's body with Sister Agnes's 5-part custom 3D humanoid nurse model (<see cref="NurseAgnesModelPath"/>),
    /// Cook Bruno's Indie Horror 2-piece + props 3D model (<see cref="CookBrunoModelPath"/>), or Timmy in its own skin otherwise.
    /// </summary>
    public static string SwapNpcBody(HubNpc npc)
    {
        if (npc.who == HubNpc.Who.Agnes && System.IO.File.Exists(NurseAgnesModelPath))
        {
            Transform oldAgnes = npc.body;
            Transform agnes = NurseAgnesBodyLike(npc.transform, oldAgnes);
            if (agnes != null)
            {
                npc.body = agnes;
                npc.controller = PeopleController();
                if (oldAgnes != null) Object.DestroyImmediate(oldAgnes.gameObject);
                EditorUtility.SetDirty(npc);
                return $"{npc.name}: body swapped to 5-part 3D NurseAgnes_Model.fbx (Body, Head, Hair, Hat, Apron + NurseAgnesPhysics)";
            }
        }

        if (npc.who == HubNpc.Who.Bruno && System.IO.File.Exists(CookBrunoModelPath))
        {
            Transform oldBruno = npc.body;
            Transform bruno = CookBrunoBodyLike(npc.transform, oldBruno);
            if (bruno != null)
            {
                npc.body = bruno;
                npc.controller = PeopleController();
                if (oldBruno != null) Object.DestroyImmediate(oldBruno.gameObject);
                EditorUtility.SetDirty(npc);
                return $"{npc.name}: body swapped to Indie Horror 3D CookBruno_Model.fbx (Head, Body, 3-part Cleaver, 2-part Ladle + CookBrunoPhysics)";
            }
        }

        Material skin = HunterSkinBuilder.EnsureNpcSkin(npc.who);
        if (skin == null) return $"{npc.name}: no skin was painted";

        Transform old = npc.body;
        Transform robot = RobotBodyLike(npc.transform, old, skin);
        if (robot == null) return $"{npc.name}: {PursuerRobotPrefab} is missing or has no Animator";

        npc.body = robot;
        npc.controller = PeopleController();
        if (old != null) Object.DestroyImmediate(old.gameObject);
        EditorUtility.SetDirty(npc);
        return $"{npc.name}: body swapped to Timmy in the {skin.name} skin";
    }

    private static GameObject EnsureNurseAgnesModel()
    {
        if (!System.IO.File.Exists(NurseAgnesModelPath)) return null;
        ModelImporter importer = AssetImporter.GetAtPath(NurseAgnesModelPath) as ModelImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(NurseAgnesModelPath, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(NurseAgnesModelPath) as ModelImporter;
        }
        if (importer == null) return null;
        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
            importer.importAnimation ||
            !importer.importBlendShapes)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importBlendShapes = true;
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(NurseAgnesModelPath);
        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(NurseAgnesModelPath))
        {
            if (o is Avatar a) avatar = a;
        }
        if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"AsylumHubBuilder: {NurseAgnesModelPath} did not import as a valid Humanoid avatar.");
            return null;
        }
        EnsureNurseAgnesMaterials();
        return model;
    }

    private static Material EnsureNursePbrMaterial(string matName, string matPath, string diffFile, string normFile, float smoothness, float metallic = 0f, bool doubleSided = false)
    {
        if (!string.IsNullOrEmpty(diffFile))
        {
            string diffPath = $"{NurseAgnesModelDir}/{diffFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath) == null && System.IO.File.Exists(diffPath))
                AssetDatabase.ImportAsset(diffPath, ImportAssetOptions.ForceUpdate);
        }
        if (!string.IsNullOrEmpty(normFile))
        {
            string normPath = $"{NurseAgnesModelDir}/{normFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(normPath) == null && System.IO.File.Exists(normPath))
                AssetDatabase.ImportAsset(normPath, ImportAssetOptions.ForceUpdate);
            TextureImporter normImp = AssetImporter.GetAtPath(normPath) as TextureImporter;
            if (normImp != null && normImp.textureType != TextureImporterType.NormalMap)
            {
                normImp.textureType = TextureImporterType.NormalMap;
                normImp.SaveAndReimport();
            }
        }

        Texture2D diff = !string.IsNullOrEmpty(diffFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{NurseAgnesModelDir}/{diffFile}") : null;
        Texture2D nrm  = !string.IsNullOrEmpty(normFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{NurseAgnesModelDir}/{normFile}") : null;

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
        mat.SetFloat("_Cull", doubleSided ? 0f : 2f);
        if (nrm != null)
        {
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1.0f);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void EnsureNurseAgnesMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials"))
            AssetDatabase.CreateFolder("Assets/SourceFiles", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials/Asylum"))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials", "Asylum");
        if (!AssetDatabase.IsValidFolder(NurseAgnesMatFolder))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials/Asylum", "Npcs");

        EnsureNursePbrMaterial("NurseAgnes_Body",  $"{NurseAgnesMatFolder}/NurseAgnes_Body.mat",  "NurseAgnes_Body_Diffuse.png",  "NurseAgnes_Body_Normal.png",  0.22f, 0f, false);
        EnsureNursePbrMaterial("NurseAgnes_Head",  $"{NurseAgnesMatFolder}/NurseAgnes_Head.mat",  "NurseAgnes_Head_Diffuse.png",  "NurseAgnes_Head_Normal.png",  0.28f, 0f, false);
        EnsureNursePbrMaterial("NurseAgnes_Eyes",  $"{NurseAgnesMatFolder}/NurseAgnes_Eyes.mat",  "NurseAgnes_Eyes_Diffuse.png",  "NurseAgnes_Eyes_Normal.png",  0.92f, 0f, false);
        EnsureNursePbrMaterial("NurseAgnes_Hair",  $"{NurseAgnesMatFolder}/NurseAgnes_Hair.mat",  "NurseAgnes_Hair_Diffuse.png",  "NurseAgnes_Hair_Normal.png",  0.20f, 0f, true);
        EnsureNursePbrMaterial("NurseAgnes_Hat",   $"{NurseAgnesMatFolder}/NurseAgnes_Hat.mat",   "NurseAgnes_Hat_Diffuse.png",   "NurseAgnes_Hat_Normal.png",   0.18f, 0f, true);
        EnsureNursePbrMaterial("NurseAgnes_Apron", $"{NurseAgnesMatFolder}/NurseAgnes_Apron.mat", "NurseAgnes_Apron_Diffuse.png", "NurseAgnes_Apron_Normal.png", 0.16f, 0f, true);
        AssetDatabase.SaveAssets();
    }

    private static Transform NurseAgnesBodyLike(Transform holder, Transform old)
    {
        GameObject customModel = EnsureNurseAgnesModel();
        if (customModel == null) return null;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(customModel, holder);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.name = NurseAgnesBodyName;
        body.tag = "Untagged";
        body.transform.SetPositionAndRotation(old != null ? old.position : holder.position, old != null ? old.rotation : holder.rotation);
        body.transform.localScale = Vector3.one * (old != null ? old.localScale.x : 1f);

        Material bodyMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Body.mat");
        Material headMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Head.mat");
        Material eyesMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Eyes.mat");
        Material hairMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Hair.mat");
        Material hatMat   = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Hat.mat");
        Material apronMat = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/NurseAgnes_Apron.mat");

        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material targetMat = bodyMat;
            string rName = r.gameObject.name;
            if (rName.Contains("Head") && headMat != null) targetMat = headMat;
            else if (rName.Contains("Eyes") && eyesMat != null) targetMat = eyesMat;
            else if (rName.Contains("Hair") && hairMat != null) targetMat = hairMat;
            else if (rName.Contains("Hat") && hatMat != null) targetMat = hatMat;
            else if (rName.Contains("Apron") && apronMat != null) targetMat = apronMat;

            if (targetMat != null)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = targetMat;
                r.sharedMaterials = mats;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        if (animator.avatar == null)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(NurseAgnesModelPath))
            {
                if (o is Avatar a) animator.avatar = a;
            }
        }
        animator.runtimeAnimatorController = PeopleController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (body.GetComponent<NurseAgnesPhysics>() == null)
        {
            body.AddComponent<NurseAgnesPhysics>();
        }

        body.SetActive(old == null || old.gameObject.activeSelf);
        return body.transform;
    }

    private static GameObject EnsureCookBrunoModel()
    {
        if (!System.IO.File.Exists(CookBrunoModelPath)) return null;
        ModelImporter importer = AssetImporter.GetAtPath(CookBrunoModelPath) as ModelImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(CookBrunoModelPath, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(CookBrunoModelPath) as ModelImporter;
        }
        if (importer == null) return null;
        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
            importer.importAnimation ||
            !importer.importBlendShapes)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importBlendShapes = true;
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(CookBrunoModelPath);
        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(CookBrunoModelPath))
        {
            if (o is Avatar a) avatar = a;
        }
        if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"AsylumHubBuilder: {CookBrunoModelPath} did not import as a valid Humanoid avatar.");
            return null;
        }
        EnsureCookBrunoMaterials();
        return model;
    }

    private static Material EnsureCookBrunoPbrMaterial(string matName, string matPath, string diffFile, string normFile, string msFile, float smoothness, float metallic = 0f, bool doubleSided = false)
    {
        if (!string.IsNullOrEmpty(diffFile))
        {
            string diffPath = $"{CookBrunoModelDir}/{diffFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath) == null && System.IO.File.Exists(diffPath))
                AssetDatabase.ImportAsset(diffPath, ImportAssetOptions.ForceUpdate);
        }
        if (!string.IsNullOrEmpty(normFile))
        {
            string normPath = $"{CookBrunoModelDir}/{normFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(normPath) == null && System.IO.File.Exists(normPath))
                AssetDatabase.ImportAsset(normPath, ImportAssetOptions.ForceUpdate);
            TextureImporter normImp = AssetImporter.GetAtPath(normPath) as TextureImporter;
            if (normImp != null && normImp.textureType != TextureImporterType.NormalMap)
            {
                normImp.textureType = TextureImporterType.NormalMap;
                normImp.SaveAndReimport();
            }
        }
        if (!string.IsNullOrEmpty(msFile))
        {
            string msPath = $"{CookBrunoModelDir}/{msFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(msPath) == null && System.IO.File.Exists(msPath))
                AssetDatabase.ImportAsset(msPath, ImportAssetOptions.ForceUpdate);
            TextureImporter msImp = AssetImporter.GetAtPath(msPath) as TextureImporter;
            if (msImp != null && msImp.sRGBTexture)
            {
                msImp.sRGBTexture = false;
                msImp.SaveAndReimport();
            }
        }

        Texture2D diff = !string.IsNullOrEmpty(diffFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{CookBrunoModelDir}/{diffFile}") : null;
        Texture2D nrm  = !string.IsNullOrEmpty(normFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{CookBrunoModelDir}/{normFile}") : null;
        Texture2D ms   = !string.IsNullOrEmpty(msFile)   ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{CookBrunoModelDir}/{msFile}")   : null;

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
        mat.SetFloat("_Cull", doubleSided ? 0f : 2f);
        if (ms != null && metallic < 0.05f)
        {
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.SetFloat("_Smoothness", 1.0f);
        }
        else
        {
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_MetallicGlossMap", null);
        }
        if (nrm != null)
        {
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1.0f);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    private static void EnsureCookBrunoMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials"))
            AssetDatabase.CreateFolder("Assets/SourceFiles", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials/Asylum"))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials", "Asylum");
        if (!AssetDatabase.IsValidFolder(NurseAgnesMatFolder))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials/Asylum", "Npcs");

        EnsureCookBrunoPbrMaterial("CookBruno_Head",           $"{NurseAgnesMatFolder}/CookBruno_Head.mat",           "CookBruno_Head_Diffuse.png",           "CookBruno_Head_Normal.png",           "CookBruno_Head_MetalSmooth.png",           0.26f, 0.0f,  false);
        EnsureCookBrunoPbrMaterial("CookBruno_Body",           $"{NurseAgnesMatFolder}/CookBruno_Body.mat",           "CookBruno_Body_Diffuse.png",           "CookBruno_Body_Normal.png",           "CookBruno_Body_MetalSmooth.png",           0.22f, 0.0f,  false);
        EnsureCookBrunoPbrMaterial("CookBruno_Cleaver_Blade",  $"{NurseAgnesMatFolder}/CookBruno_Cleaver_Blade.mat",  "CookBruno_CleaverBlade_Diffuse.png",   "CookBruno_CleaverBlade_Normal.png",   "CookBruno_CleaverBlade_MetalSmooth.png",   0.68f, 0.88f, false);
        EnsureCookBrunoPbrMaterial("CookBruno_Cleaver_Handle", $"{NurseAgnesMatFolder}/CookBruno_Cleaver_Handle.mat", "CookBruno_CleaverHandle_Diffuse.png",  "CookBruno_CleaverHandle_Normal.png",  "CookBruno_CleaverHandle_MetalSmooth.png",  0.32f, 0.0f,  false);
        EnsureCookBrunoPbrMaterial("CookBruno_Cleaver_Rivets", $"{NurseAgnesMatFolder}/CookBruno_Cleaver_Rivets.mat", "CookBruno_CleaverRivets_Diffuse.png",  "CookBruno_CleaverRivets_Normal.png",  "CookBruno_CleaverRivets_MetalSmooth.png",  0.72f, 0.90f, false);
        EnsureCookBrunoPbrMaterial("CookBruno_Ladle",          $"{NurseAgnesMatFolder}/CookBruno_Ladle.mat",          "CookBruno_Ladle_Diffuse.png",          "CookBruno_Ladle_Normal.png",          "CookBruno_Ladle_MetalSmooth.png",          0.58f, 0.85f, false);
        AssetDatabase.SaveAssets();
    }

    private static Transform CookBrunoBodyLike(Transform holder, Transform old)
    {
        GameObject customModel = EnsureCookBrunoModel();
        if (customModel == null) return null;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(customModel, holder);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.name = CookBrunoBodyName;
        body.tag = "Untagged";
        body.transform.SetPositionAndRotation(old != null ? old.position : holder.position, old != null ? old.rotation : holder.rotation);
        body.transform.localScale = Vector3.one * (old != null ? old.localScale.x : 1f);

        Material headMat   = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/CookBruno_Head.mat");
        Material bodyMat   = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/CookBruno_Body.mat");
        Material cBladeMat = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/CookBruno_Cleaver_Blade.mat");
        Material cHandMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/CookBruno_Cleaver_Handle.mat");
        Material cRivMat   = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/CookBruno_Cleaver_Rivets.mat");
        Material ladleMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/CookBruno_Ladle.mat");

        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material targetMat = bodyMat;
            string rName = r.gameObject.name;
            if (rName.Contains("Head") && headMat != null) targetMat = headMat;
            else if (rName.Contains("Cleaver_Blade") && cBladeMat != null) targetMat = cBladeMat;
            else if (rName.Contains("Cleaver_Handle") && cHandMat != null) targetMat = cHandMat;
            else if (rName.Contains("Cleaver_Rivets") && cRivMat != null) targetMat = cRivMat;
            else if (rName.Contains("Ladle") && ladleMat != null) targetMat = ladleMat;

            if (targetMat != null)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = targetMat;
                r.sharedMaterials = mats;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        if (animator.avatar == null)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(CookBrunoModelPath))
            {
                if (o is Avatar a) animator.avatar = a;
            }
        }
        animator.runtimeAnimatorController = PeopleController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (body.GetComponent<CookBrunoPhysics>() == null)
        {
            body.AddComponent<CookBrunoPhysics>();
        }

        body.SetActive(old == null || old.gameObject.activeSelf);
        return body.transform;
    }

    public static GameObject EnsureEliasVogtModel()
    {
        if (!System.IO.File.Exists(EliasVogtModelPath)) return null;
        ModelImporter importer = AssetImporter.GetAtPath(EliasVogtModelPath) as ModelImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(EliasVogtModelPath, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(EliasVogtModelPath) as ModelImporter;
        }
        if (importer == null) return null;
        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
            importer.importAnimation ||
            !importer.importBlendShapes)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importBlendShapes = true;
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(EliasVogtModelPath);
        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(EliasVogtModelPath))
        {
            if (o is Avatar a) avatar = a;
        }
        if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"AsylumHubBuilder: {EliasVogtModelPath} did not import as a valid Humanoid avatar.");
            return null;
        }
        EnsureEliasVogtMaterials();
        return model;
    }

    private static Material EnsureEliasVogtPbrMaterial(string matName, string matPath, string diffFile, string normFile, string msFile, string emisFile, float smoothness, float metallic = 0f, bool doubleSided = false)
    {
        if (!string.IsNullOrEmpty(diffFile))
        {
            string diffPath = $"{EliasVogtModelDir}/{diffFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath) == null && System.IO.File.Exists(diffPath))
                AssetDatabase.ImportAsset(diffPath, ImportAssetOptions.ForceUpdate);
        }
        if (!string.IsNullOrEmpty(normFile))
        {
            string normPath = $"{EliasVogtModelDir}/{normFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(normPath) == null && System.IO.File.Exists(normPath))
                AssetDatabase.ImportAsset(normPath, ImportAssetOptions.ForceUpdate);
            TextureImporter normImp = AssetImporter.GetAtPath(normPath) as TextureImporter;
            if (normImp != null && normImp.textureType != TextureImporterType.NormalMap)
            {
                normImp.textureType = TextureImporterType.NormalMap;
                normImp.SaveAndReimport();
            }
        }
        if (!string.IsNullOrEmpty(msFile))
        {
            string msPath = $"{EliasVogtModelDir}/{msFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(msPath) == null && System.IO.File.Exists(msPath))
                AssetDatabase.ImportAsset(msPath, ImportAssetOptions.ForceUpdate);
            TextureImporter msImp = AssetImporter.GetAtPath(msPath) as TextureImporter;
            if (msImp != null && msImp.sRGBTexture)
            {
                msImp.sRGBTexture = false;
                msImp.SaveAndReimport();
            }
        }
        if (!string.IsNullOrEmpty(emisFile))
        {
            string emisPath = $"{EliasVogtModelDir}/{emisFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(emisPath) == null && System.IO.File.Exists(emisPath))
                AssetDatabase.ImportAsset(emisPath, ImportAssetOptions.ForceUpdate);
        }

        Texture2D diff = !string.IsNullOrEmpty(diffFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{EliasVogtModelDir}/{diffFile}") : null;
        Texture2D nrm  = !string.IsNullOrEmpty(normFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{EliasVogtModelDir}/{normFile}") : null;
        Texture2D ms   = !string.IsNullOrEmpty(msFile)   ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{EliasVogtModelDir}/{msFile}")   : null;
        Texture2D emis = !string.IsNullOrEmpty(emisFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{EliasVogtModelDir}/{emisFile}") : null;

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
        mat.SetFloat("_Cull", doubleSided ? 0f : 2f);
        if (ms != null && metallic < 0.05f)
        {
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.SetFloat("_Smoothness", 1.0f);
        }
        else
        {
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_MetallicGlossMap", null);
        }
        if (nrm != null)
        {
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1.0f);
        }
        if (emis != null)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", emis);
            mat.SetColor("_EmissionColor", Color.white * 0.55f);
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", null);
            mat.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public static void EnsureEliasVogtMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials"))
            AssetDatabase.CreateFolder("Assets/SourceFiles", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials/Asylum"))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials", "Asylum");
        if (!AssetDatabase.IsValidFolder(NurseAgnesMatFolder))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials/Asylum", "Npcs");

        EnsureEliasVogtPbrMaterial("EliasVogt_Head",      $"{NurseAgnesMatFolder}/EliasVogt_Head.mat",      "EliasVogt_Head_Diffuse.png",      "EliasVogt_Head_Normal.png",      "EliasVogt_Head_MetalSmooth.png",      "EliasVogt_Head_Emission.png", 0.28f, 0.0f,  false);
        EnsureEliasVogtPbrMaterial("EliasVogt_Body",      $"{NurseAgnesMatFolder}/EliasVogt_Body.mat",      "EliasVogt_Body_Diffuse.png",      "EliasVogt_Body_Normal.png",      "EliasVogt_Body_MetalSmooth.png",      null,                          0.22f, 0.0f,  false);
        EnsureEliasVogtPbrMaterial("EliasVogt_Wristband", $"{NurseAgnesMatFolder}/EliasVogt_Wristband.mat", "EliasVogt_Wristband_Diffuse.png", "EliasVogt_Wristband_Normal.png", "EliasVogt_Wristband_MetalSmooth.png", null,                          0.32f, 0.0f,  false);
        EnsureEliasVogtPbrMaterial("EliasVogt_MorgueTag", $"{NurseAgnesMatFolder}/EliasVogt_MorgueTag.mat", "EliasVogt_MorgueTag_Diffuse.png", "EliasVogt_MorgueTag_Normal.png", "EliasVogt_MorgueTag_MetalSmooth.png", null,                          0.68f, 0.88f, false);
        AssetDatabase.SaveAssets();
    }

    private static Transform EliasVogtBodyLike(Transform holder, Transform old)
    {
        GameObject customModel = EnsureEliasVogtModel();
        if (customModel == null) return null;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(customModel, holder);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.name = EliasVogtBodyName;
        body.tag = "Untagged";
        body.transform.SetPositionAndRotation(old != null ? old.position : holder.position, old != null ? old.rotation : holder.rotation);
        body.transform.localScale = Vector3.one * (old != null ? old.localScale.x : 1f);

        Material headMat = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/EliasVogt_Head.mat");
        Material bodyMat = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/EliasVogt_Body.mat");
        Material wbMat   = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/EliasVogt_Wristband.mat");
        Material tagMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/EliasVogt_MorgueTag.mat");

        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material targetMat = bodyMat;
            string rName = r.gameObject.name;
            if (rName.Contains("Head") && headMat != null) targetMat = headMat;
            else if (rName.Contains("Wristband") && wbMat != null) targetMat = wbMat;
            else if (rName.Contains("MorgueTag") && tagMat != null) targetMat = tagMat;

            if (targetMat != null)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = targetMat;
                r.sharedMaterials = mats;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        if (animator.avatar == null)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(EliasVogtModelPath))
            {
                if (o is Avatar a) animator.avatar = a;
            }
        }
        animator.runtimeAnimatorController = PeopleController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        if (body.GetComponent<EliasPhysics>() == null)
        {
            body.AddComponent<EliasPhysics>();
        }

        body.SetActive(old == null || old.gameObject.activeSelf);
        return body.transform;
    }

    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Elias Vogt (Patient #5 & The Frost)", priority = 139)]
    public static void MenuSwapEliasVogt()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub != null && hub.life != null)
        {
            HubPatient eliasPatient = FindPatient(hub.life, "Elias");
            if (eliasPatient != null)
            {
                string msg = SwapPatientToRobot(eliasPatient);
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
                ApplyHubToPrefab(hub);
                Debug.Log(msg, eliasPatient);
            }
        }

        if (Object.FindAnyObjectByType<InteractableKit>(FindObjectsInactive.Include) != null)
        {
            InteractableKitBuilder.RebuildEliasTemplate();
        }
    }

    public static GameObject EnsureHildeBrandtModel()
    {
        if (!System.IO.File.Exists(HildeBrandtModelPath)) return null;
        ModelImporter importer = AssetImporter.GetAtPath(HildeBrandtModelPath) as ModelImporter;
        if (importer == null)
        {
            AssetDatabase.ImportAsset(HildeBrandtModelPath, ImportAssetOptions.ForceUpdate);
            importer = AssetImporter.GetAtPath(HildeBrandtModelPath) as ModelImporter;
        }
        if (importer == null) return null;
        if (importer.animationType != ModelImporterAnimationType.Human ||
            importer.avatarSetup != ModelImporterAvatarSetup.CreateFromThisModel ||
            importer.importAnimation ||
            !importer.importBlendShapes)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
            importer.importAnimation = false;
            importer.importBlendShapes = true;
            importer.SaveAndReimport();
        }
        GameObject model = AssetDatabase.LoadAssetAtPath<GameObject>(HildeBrandtModelPath);
        Avatar avatar = null;
        foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(HildeBrandtModelPath))
        {
            if (o is Avatar a) avatar = a;
        }
        if (model == null || avatar == null || !avatar.isValid || !avatar.isHuman)
        {
            Debug.LogError($"AsylumHubBuilder: {HildeBrandtModelPath} did not import as a valid Humanoid avatar.");
            return null;
        }
        EnsureHildeBrandtMaterials();
        return model;
    }

    private static Material EnsureDirPbrMaterial(string modelDir, string matName, string matPath, string diffFile, string normFile, string msFile, string emisFile, float smoothness, float metallic = 0f, bool doubleSided = false)
    {
        if (!string.IsNullOrEmpty(diffFile))
        {
            string diffPath = $"{modelDir}/{diffFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(diffPath) == null && System.IO.File.Exists(diffPath))
                AssetDatabase.ImportAsset(diffPath, ImportAssetOptions.ForceUpdate);
        }
        if (!string.IsNullOrEmpty(normFile))
        {
            string normPath = $"{modelDir}/{normFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(normPath) == null && System.IO.File.Exists(normPath))
                AssetDatabase.ImportAsset(normPath, ImportAssetOptions.ForceUpdate);
            TextureImporter normImp = AssetImporter.GetAtPath(normPath) as TextureImporter;
            if (normImp != null && normImp.textureType != TextureImporterType.NormalMap)
            {
                normImp.textureType = TextureImporterType.NormalMap;
                normImp.SaveAndReimport();
            }
        }
        if (!string.IsNullOrEmpty(msFile))
        {
            string msPath = $"{modelDir}/{msFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(msPath) == null && System.IO.File.Exists(msPath))
                AssetDatabase.ImportAsset(msPath, ImportAssetOptions.ForceUpdate);
            TextureImporter msImp = AssetImporter.GetAtPath(msPath) as TextureImporter;
            if (msImp != null && msImp.sRGBTexture)
            {
                msImp.sRGBTexture = false;
                msImp.SaveAndReimport();
            }
        }
        if (!string.IsNullOrEmpty(emisFile))
        {
            string emisPath = $"{modelDir}/{emisFile}";
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(emisPath) == null && System.IO.File.Exists(emisPath))
                AssetDatabase.ImportAsset(emisPath, ImportAssetOptions.ForceUpdate);
        }

        Texture2D diff = !string.IsNullOrEmpty(diffFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{modelDir}/{diffFile}") : null;
        Texture2D nrm  = !string.IsNullOrEmpty(normFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{modelDir}/{normFile}") : null;
        Texture2D ms   = !string.IsNullOrEmpty(msFile)   ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{modelDir}/{msFile}")   : null;
        Texture2D emis = !string.IsNullOrEmpty(emisFile) ? AssetDatabase.LoadAssetAtPath<Texture2D>($"{modelDir}/{emisFile}") : null;

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
        mat.SetFloat("_Cull", doubleSided ? 0f : 2f);
        if (ms != null && metallic < 0.05f)
        {
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_MetallicGlossMap", ms);
            mat.SetFloat("_Smoothness", 1.0f);
        }
        else
        {
            mat.DisableKeyword("_METALLICSPECGLOSSMAP");
            mat.SetTexture("_MetallicGlossMap", null);
        }
        if (nrm != null)
        {
            mat.EnableKeyword("_NORMALMAP");
            mat.SetTexture("_BumpMap", nrm);
            mat.SetFloat("_BumpScale", 1.0f);
        }
        if (emis != null)
        {
            mat.EnableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", emis);
            mat.SetColor("_EmissionColor", Color.white * 0.45f);
        }
        else
        {
            mat.DisableKeyword("_EMISSION");
            mat.SetTexture("_EmissionMap", null);
            mat.SetColor("_EmissionColor", Color.black);
        }
        EditorUtility.SetDirty(mat);
        return mat;
    }

    public static void EnsureHildeBrandtMaterials()
    {
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials"))
            AssetDatabase.CreateFolder("Assets/SourceFiles", "Materials");
        if (!AssetDatabase.IsValidFolder("Assets/SourceFiles/Materials/Asylum"))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials", "Asylum");
        if (!AssetDatabase.IsValidFolder(NurseAgnesMatFolder))
            AssetDatabase.CreateFolder("Assets/SourceFiles/Materials/Asylum", "Npcs");

        EnsureDirPbrMaterial(HildeBrandtModelDir, "HildeBrandt_Head",         $"{NurseAgnesMatFolder}/HildeBrandt_Head.mat",         "HildeBrandt_Head_Diffuse.png",         "HildeBrandt_Head_Normal.png",         "HildeBrandt_Head_MetalSmooth.png",         "HildeBrandt_Head_Emission.png", 0.26f, 0.0f,  false);
        EnsureDirPbrMaterial(HildeBrandtModelDir, "HildeBrandt_Body",         $"{NurseAgnesMatFolder}/HildeBrandt_Body.mat",         "HildeBrandt_Body_Diffuse.png",         "HildeBrandt_Body_Normal.png",         "HildeBrandt_Body_MetalSmooth.png",         null,                            0.20f, 0.0f,  false);
        EnsureDirPbrMaterial(HildeBrandtModelDir, "HildeBrandt_RestraintTag", $"{NurseAgnesMatFolder}/HildeBrandt_RestraintTag.mat", "HildeBrandt_RestraintTag_Diffuse.png", "HildeBrandt_RestraintTag_Normal.png", "HildeBrandt_RestraintTag_MetalSmooth.png", null,                            0.62f, 0.78f, false);
        EnsureDirPbrMaterial(HildeBrandtModelDir, "HildeBrandt_Chalk",        $"{NurseAgnesMatFolder}/HildeBrandt_Chalk.mat",        "HildeBrandt_Chalk_Diffuse.png",        "HildeBrandt_Chalk_Normal.png",        "HildeBrandt_Chalk_MetalSmooth.png",        null,                            0.08f, 0.0f,  false);
        AssetDatabase.SaveAssets();
    }

    private static Transform HildeBrandtBodyLike(Transform holder, Transform old)
    {
        GameObject customModel = EnsureHildeBrandtModel();
        if (customModel == null) return null;

        GameObject body = (GameObject)PrefabUtility.InstantiatePrefab(customModel, holder);
        PrefabUtility.UnpackPrefabInstance(body, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        body.name = HildeBrandtBodyName;
        body.tag = "Untagged";
        body.transform.SetPositionAndRotation(old != null ? old.position : holder.position, old != null ? old.rotation : holder.rotation);
        body.transform.localScale = Vector3.one * (old != null ? old.localScale.x : 1f);

        Material headMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/HildeBrandt_Head.mat");
        Material bodyMat  = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/HildeBrandt_Body.mat");
        Material tagMat   = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/HildeBrandt_RestraintTag.mat");
        Material chalkMat = AssetDatabase.LoadAssetAtPath<Material>($"{NurseAgnesMatFolder}/HildeBrandt_Chalk.mat");

        foreach (Renderer r in body.GetComponentsInChildren<Renderer>(true))
        {
            Material targetMat = bodyMat;
            string rName = r.gameObject.name;
            if (rName.Contains("Head") && headMat != null) targetMat = headMat;
            else if (rName.Contains("RestraintTag") && tagMat != null) targetMat = tagMat;
            else if (rName.Contains("Chalk") && chalkMat != null) targetMat = chalkMat;

            if (targetMat != null)
            {
                Material[] mats = r.sharedMaterials;
                for (int i = 0; i < mats.Length; i++) mats[i] = targetMat;
                r.sharedMaterials = mats;
            }
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (r is SkinnedMeshRenderer smr) smr.updateWhenOffscreen = true;
        }

        Animator animator = body.GetComponent<Animator>();
        if (animator == null) animator = body.AddComponent<Animator>();
        if (animator.avatar == null)
        {
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(HildeBrandtModelPath))
            {
                if (o is Avatar a) animator.avatar = a;
            }
        }
        animator.runtimeAnimatorController = PeopleController();
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

        HildePhysics hp = body.GetComponent<HildePhysics>();
        if (hp == null) hp = body.AddComponent<HildePhysics>();
        hp.BindRig();

        body.SetActive(old == null || old.gameObject.activeSelf);
        return body.transform;
    }

    [MenuItem("KEEP IT LIT/Build/Re-skin Templates/Hilde Brandt (Patient #3 & Morgue Body)", priority = 140)]
    public static void MenuSwapHildeAndMorgue()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null || hub.life == null)
        {
            Debug.LogError("Hilde Brandt & Morgue Body: no hub with a Life group in the open scene.");
            return;
        }
        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true);
        Physics.SyncTransforms();

        HubPatient hildePatient = FindPatient(hub.life, "Hilde");
        if (hildePatient != null)
        {
            string msg = SwapPatientToRobot(hildePatient);
            Debug.Log(msg, hildePatient);
        }

        Transform morgue = hub.life.Find(EliasMorgueName);
        if (morgue != null)
        {
            string morgueMsg = SwapMorgueBody(morgue);
            Debug.Log(morgueMsg, morgue);
        }

        if (!wasActive) hub.SetContentActive(false);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        ApplyHubToPrefab(hub);
    }

    /// <summary>
    /// Timmy in <paramref name="skin"/> under <paramref name="holder"/>, named <see cref="NpcRobotBodyName"/>, on the HubPeople controller,
    /// taking <paramref name="old"/>'s position, rotation, uniform scale and active state (the holder's own pose when there is no old body).
    /// </summary>
    private static Transform RobotBodyLike(Transform holder, Transform old, Material skin)
    {
        Transform robot = InteractableKitBuilder.InstantiateRobotNode(holder);
        if (robot == null) return null;
        robot.name = NpcRobotBodyName;
        robot.SetPositionAndRotation(old != null ? old.position : holder.position, old != null ? old.rotation : holder.rotation);
        robot.localScale = Vector3.one * (old != null ? old.localScale.x : 1f);
        StripPhysics(robot.gameObject);
        robot.GetComponent<Animator>().runtimeAnimatorController = PeopleController();
        foreach (Renderer r in robot.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++) mats[i] = skin;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        robot.gameObject.SetActive(old == null || old.gameObject.activeSelf);
        return robot;
    }

    // ---------------------------------------------------------------- patients and the morgue body

    private const string MorgueRobotName = "Body_Sheeted_Timmy";
    private static readonly string[] RobotPatients = { "Hilde", "Elias" };

    private static HubPatient FindPatient(Transform life, string who)
    {
        Transform patients = life != null ? life.Find(PatientsName) : null;
        Transform t = patients != null ? patients.Find(who) : null;
        return t != null ? t.GetComponent<HubPatient>() : null;
    }

    /// <summary>ProjectSetup: the patients Hilde and Elias and the morgue's sheeted body match their expected bodies (null = done, or no hub).</summary>
    public static string PatientRobotProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null || hub.life == null) return null;
        List<string> missing = new List<string>();
        foreach (string who in RobotPatients)
        {
            HubPatient p = FindPatient(hub.life, who);
            string expected = ExpectedPatientBodyName(who);
            if (p != null && (p.body == null || p.body.name != expected)) missing.Add(who);
        }
        Transform morgue = hub.life.Find(EliasMorgueName);
        if (morgue != null && morgue.Find(ExpectedMorgueBodyName()) == null) missing.Add("the morgue body");
        return missing.Count == 0 ? null : "patient body needs update: " + string.Join(", ", missing);
    }

    /// <summary>ProjectSetup's fix: swaps what <see cref="PatientRobotProblem"/> lists and applies the hub to its prefab. Marks the scene dirty.</summary>
    public static void PatientRobotRepair()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null || hub.life == null) { Debug.LogError("Asylum robot patients: no hub with a Life group in the scene."); return; }
        System.Text.StringBuilder report = new System.Text.StringBuilder();
        _building = hub.building;
        bool wasActive = hub.ContentActive;
        hub.SetContentActive(true); // renderer bounds (the gurney) need active objects
        Physics.SyncTransforms();
        foreach (string who in RobotPatients)
        {
            HubPatient p = FindPatient(hub.life, who);
            string expected = ExpectedPatientBodyName(who);
            if (p != null && (p.body == null || p.body.name != expected)) report.AppendLine(SwapPatientToRobot(p));
        }
        Transform morgue = hub.life.Find(EliasMorgueName);
        if (morgue != null && morgue.Find(ExpectedMorgueBodyName()) == null) report.AppendLine(SwapMorgueBody(morgue));
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(hub.gameObject.scene);
        if (!wasActive) hub.SetContentActive(false);
        ApplyHubToPrefab(hub);
        Debug.Log("Asylum robot patients:\n" + report.ToString().Trim());
    }

    /// <summary>A patient's body becomes Hilde's (<see cref="HildeBrandtModelPath"/>) or Elias's (<see cref="EliasVogtModelPath"/>) custom 3D model, or Timmy in that patient's skin, on the HubPeople controller (it has the Lie state), with the fitted capsule the human body had.</summary>
    public static string SwapPatientToRobot(HubPatient patient)
    {
        if (patient != null && patient.name == "Hilde" && System.IO.File.Exists(HildeBrandtModelPath))
        {
            Transform oldHilde = patient.body;
            Transform hildeBody = HildeBrandtBodyLike(patient.transform, oldHilde);
            if (hildeBody != null)
            {
                AddPatientCapsule(hildeBody.gameObject);
                patient.body = hildeBody;
                patient.controller = PeopleController();
                if (oldHilde != null) Object.DestroyImmediate(oldHilde.gameObject);
                EditorUtility.SetDirty(patient);
                patient.Refresh();
                return $"{patient.name}: body swapped to Indie Horror 3D HildeBrandt_Model.fbx (Head, Body, #3 RestraintTag & Chalk + HildePhysics)";
            }
        }

        if (patient != null && patient.name == "Elias" && System.IO.File.Exists(EliasVogtModelPath))
        {
            Transform oldElias = patient.body;
            Transform eliasBody = EliasVogtBodyLike(patient.transform, oldElias);
            if (eliasBody != null)
            {
                AddPatientCapsule(eliasBody.gameObject);
                patient.body = eliasBody;
                patient.controller = PeopleController();
                if (oldElias != null) Object.DestroyImmediate(oldElias.gameObject);
                EditorUtility.SetDirty(patient);
                patient.Refresh();
                return $"{patient.name}: body swapped to Indie Horror 3D EliasVogt_Model.fbx (Head, Body, Wristband & #5 MorgueTag + EliasPhysics)";
            }
        }

        Material skin = HunterSkinBuilder.EnsureNpcSkin(patient.name);
        if (skin == null) return $"{patient.name}: no skin was painted";
        Transform old = patient.body;
        Transform robot = RobotBodyLike(patient.transform, old, skin);
        if (robot == null) return $"{patient.name}: {PursuerRobotPrefab} is missing or has no Animator";
        AddPatientCapsule(robot.gameObject);

        patient.body = robot;
        patient.controller = PeopleController();
        if (old != null) Object.DestroyImmediate(old.gameObject);
        EditorUtility.SetDirty(patient);
        patient.Refresh();
        return $"{patient.name}: body swapped to Timmy in the {skin.name} skin";
    }

    private static void AddPatientCapsule(GameObject body)
    {
        CapsuleCollider capsule = body.AddComponent<CapsuleCollider>();
        capsule.center = new Vector3(0f, 0.9f, 0f);
        capsule.radius = 0.3f;
        capsule.height = 1.8f;
    }

    /// <summary>
    /// The sheeted body on the morgue gurney becomes the custom 3D <see cref="SheetedMorgueModelPath"/> corpse (with frostbitten feet and #5 tag)
    /// or Timmy under the sheet as fallback, centred on the gurney. The old body is deleted.
    /// </summary>
    private static string SwapMorgueBody(Transform morgue)
    {
        Transform old = morgue.Find("Body_Sheeted");
        if (old == null) old = morgue.Find(MorgueRobotName);
        if (old == null) old = morgue.Find(SheetedMorgueBodyName);
        Vector3 pos = old != null ? old.position : morgue.position;
        Quaternion rot = old != null ? old.rotation : morgue.rotation;
        if (old != null) Object.DestroyImmediate(old.gameObject);
        Transform body = BuildMorgueRobot(morgue, pos, rot);
        if (body == null) return "EliasMorgue: the morgue body could not be built";
        return $"EliasMorgue: the sheeted body is {body.name}";
    }

    private static Transform BuildMorgueRobot(Transform morgue, Vector3 position, Quaternion rotation)
    {
        bool morgueWasActive = morgue.gameObject.activeSelf;
        morgue.gameObject.SetActive(true); // the fit below needs live renderer bounds

        if (System.IO.File.Exists(SheetedMorgueModelPath))
        {
            ModelImporter morgueImp = AssetImporter.GetAtPath(SheetedMorgueModelPath) as ModelImporter;
            if (morgueImp == null)
            {
                AssetDatabase.ImportAsset(SheetedMorgueModelPath, ImportAssetOptions.ForceUpdate);
                morgueImp = AssetImporter.GetAtPath(SheetedMorgueModelPath) as ModelImporter;
            }
            if (morgueImp != null && (morgueImp.animationType != ModelImporterAnimationType.None || morgueImp.importAnimation))
            {
                morgueImp.animationType = ModelImporterAnimationType.None;
                morgueImp.importAnimation = false;
                morgueImp.SaveAndReimport();
            }

            if (!AssetDatabase.IsValidFolder(NurseAgnesMatFolder))
                EnsureHildeBrandtMaterials();
            Material morgueMat = EnsureDirPbrMaterial(
                SheetedMorgueModelDir,
                "SheetedMorgueBody_HP",
                $"{NurseAgnesMatFolder}/SheetedMorgueBody_HP.mat",
                "SheetedMorgueBody_HP_Diffuse.png",
                "SheetedMorgueBody_HP_Normal.png",
                "SheetedMorgueBody_HP_MetalSmooth.png",
                null,
                0.18f,
                0.0f,
                false);
            AssetDatabase.SaveAssets();

            GameObject morgueAsset = AssetDatabase.LoadAssetAtPath<GameObject>(SheetedMorgueModelPath);
            if (morgueAsset != null)
            {
                GameObject morgueGo = (GameObject)PrefabUtility.InstantiatePrefab(morgueAsset, morgue);
                PrefabUtility.UnpackPrefabInstance(morgueGo, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                morgueGo.name = SheetedMorgueBodyName;
                Renderer bed = _building != null ? NearestRenderer("MorgueWheelBed", new Vector3(126.4f, 1.6f, -52.6f), 2.0f) : null;
                Vector3 along = (bed == null || bed.bounds.size.z >= bed.bounds.size.x) ? Vector3.forward : Vector3.right;
                Quaternion lieFlatRot = Quaternion.LookRotation(-along, Vector3.down);
                morgueGo.transform.SetPositionAndRotation(position, lieFlatRot);
                morgueGo.transform.localScale = Vector3.one;
                StripPhysics(morgueGo);
                foreach (Renderer r in morgueGo.GetComponentsInChildren<Renderer>(true))
                {
                    if (morgueMat != null)
                    {
                        Material[] mats = new Material[r.sharedMaterials.Length];
                        for (int i = 0; i < mats.Length; i++) mats[i] = morgueMat;
                        r.sharedMaterials = mats;
                    }
                    r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                }
                FitOnGurney(morgueGo.transform);
                morgue.gameObject.SetActive(morgueWasActive);
                return morgueGo.transform;
            }
        }

        Transform robot = InteractableKitBuilder.InstantiateRobotNode(morgue);
        if (robot == null) { morgue.gameObject.SetActive(morgueWasActive); return null; }
        robot.name = MorgueRobotName;
        robot.SetPositionAndRotation(position, rotation);
        robot.localScale = Vector3.one;
        StripPhysics(robot.gameObject);
        robot.GetComponent<Animator>().runtimeAnimatorController = IdleController(); // arms at the sides under the sheet
        Material sheet = AssetDatabase.LoadAssetAtPath<Material>("Assets/SourceFiles/Materials/Asylum/Asylum_Sheet.mat");
        foreach (Renderer r in robot.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = sheet;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
        }
        FitOnGurney(robot);
        morgue.gameObject.SetActive(morgueWasActive);
        return robot;
    }

    /// <summary>Centres the lying body on the morgue gurney (its renderer bounds over the gurney's, resting on its top). No gurney = left as placed.</summary>
    private static void FitOnGurney(Transform robot)
    {
        if (_building == null) return;
        Renderer bed = NearestRenderer("MorgueWheelBed", new Vector3(126.4f, 1.6f, -52.6f), 2.0f);
        if (bed == null) return;
        Bounds bb = bed.bounds, rb = default;
        bool any = false;
        foreach (Renderer r in robot.GetComponentsInChildren<Renderer>(true)) { if (!any) { rb = r.bounds; any = true; } else rb.Encapsulate(r.bounds); }
        if (!any) return;
        robot.position += new Vector3(bb.center.x - rb.center.x, bb.max.y + 0.02f - rb.min.y, bb.center.z - rb.center.z);
    }
}
