using UnityEditor;
using UnityEngine;

/// <summary>
/// The hide step's silhouette is Timmy (a stripped PlayerRobot.prefab), blacked out, with the hunter's two red eye beads on its screen
/// face, on a looping zombie walk. It was a black Adam until 11 Oct 2026, when the Adam Character Pack was removed for licence reasons;
/// <see cref="SilhouetteProblem"/> / <see cref="SilhouetteRepair"/> swap an existing prefab's Adam for the robot in place (the
/// <c>IntakeRoom.silhouette</c> reference is the holder, so it survives). Never regenerates the room.
/// </summary>
public static partial class IntakeRoomBuilder
{
    private const string SilhouetteBodyName = "Body_Timmy";

    /// <summary>Timmy under the silhouette holder, every renderer in <paramref name="black"/>, red eyes, the walk controller. Null when the robot could not be built.</summary>
    private static Transform AddRobotSilhouette(Transform holder, Material black)
    {
        Transform robot = InteractableKitBuilder.InstantiateRobotNode(holder);
        if (robot == null) return null;
        robot.name = SilhouetteBodyName;
        foreach (Collider c in robot.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
        foreach (Renderer r in robot.GetComponentsInChildren<Renderer>(true))
        {
            Material[] mats = new Material[r.sharedMaterials.Length];
            for (int i = 0; i < mats.Length; i++) mats[i] = black;
            r.sharedMaterials = mats;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }
        Animator animator = robot.GetComponent<Animator>();
        animator.runtimeAnimatorController = AsylumHubBuilder.IntakeSilhouetteController();
        AsylumHubBuilder.AddRedEyes(animator, 0.205f, 0.2f, 0.06f, 0.045f); // on the TV-screen face, like the hub pursuer
        return robot;
    }

    /// <summary>ProjectSetup: the Intake prefab's silhouette is still the Adam body (null = it is the robot, or there is no prefab).</summary>
    public static string SilhouetteProblem()
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        Transform holder = prefab != null ? prefab.transform.Find("Silhouette") : null;
        if (holder == null) return null;
        return holder.Find(SilhouetteBodyName) != null ? null : "the hide-step silhouette is not the robot yet";
    }

    /// <summary>ProjectSetup's fix: edits the prefab asset - deletes every old body under Silhouette and adds the robot.</summary>
    public static void SilhouetteRepair()
    {
        GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            Transform holder = root.transform.Find("Silhouette");
            if (holder == null || holder.Find(SilhouetteBodyName) != null) return;
            Material black = AssetDatabase.LoadAssetAtPath<Material>(MaterialFolder + "/Intake_Silhouette.mat");
            for (int i = holder.childCount - 1; i >= 0; i--) Object.DestroyImmediate(holder.GetChild(i).gameObject);
            bool wasActive = holder.gameObject.activeSelf;
            holder.gameObject.SetActive(true); // Animator.Rebind (the eye placement) needs an active object
            Transform robot = AddRobotSilhouette(holder, black);
            holder.gameObject.SetActive(wasActive);
            if (robot == null) { Debug.LogError("Intake silhouette: the robot body could not be built."); return; }
            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("Intake silhouette: the hide-step silhouette is now Timmy (black, red eyes, zombie walk).");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }
}
