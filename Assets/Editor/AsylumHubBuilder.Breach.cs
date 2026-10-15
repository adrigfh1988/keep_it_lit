using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F121: the hub breach's authored content - <c>AsylumHub/Life/Breach</c> (a <see cref="HubBreachSetPiece"/>) and <c>Life/Breach/Hunter</c> (a
/// <see cref="HubStalker"/> wearing the hunter's body, inactive in the prefab). The first draft is written straight into the hand-edited hub prefab
/// through a <see cref="EditHubPrefab"/> round trip and touches nothing else: wing doors, arrival points, the records point and the lights are all found
/// at runtime through AsylumHub. The body follows <c>Build &gt; Hunter Body Look</c> (the Cyborg, or - when the look is Orderly or the Cyborg pack is
/// missing - the dulled Timmy the other hub pursuer wears); switching the look leaves it stale until Build Missing Pieces (Check Project says so).
/// Build-missing-only; a body that does not match the look is the one thing it replaces.
/// </summary>
public static partial class AsylumHubBuilder
{
    private const string BreachGroupName = "Breach";
    private const string BreachHunterName = "Hunter";
    private const string BreachCyborgBody = "Body_Cyborg";
    private const string BreachOrderlyBody = "Body_Orderly";
    private const string BreachController = "Assets/SourceFiles/StarterAssets/ThirdPersonController/Character/Animations/StarterAssetsThirdPerson.controller";

    /// <summary>The body the breach hunter should wear now: the Cyborg when that is the look and its model exists, else the Orderly (dulled Timmy).</summary>
    private static string BreachBodyWanted() =>
        HunterBodyBuilder.CurrentLook == HunterBodyBuilder.BodyLook.Cyborg && AssetDatabase.LoadAssetAtPath<GameObject>(HunterBodyBuilder.CyborgModelPath) != null
            ? BreachCyborgBody : BreachOrderlyBody;

    /// <summary>ProjectSetup piece: null when there is no hub (that piece reports it) or the breach is complete and wearing the right body.</summary>
    public static string BreachProblem()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) return null;
        HubBreachSetPiece piece = hub.life != null ? hub.life.GetComponentInChildren<HubBreachSetPiece>(true) : null;
        if (piece == null || !piece.IsComplete) return "missing: AsylumHub/Life/Breach (the breach set piece and its hunter)";
        Transform hunter = piece.transform.Find(BreachHunterName);
        string wanted = BreachBodyWanted();
        if (hunter == null || hunter.Find(wanted) == null)
            return $"the breach hunter does not wear the {wanted.Substring(5)} body that Build > Hunter Body Look selects (Build Missing Pieces replaces it)";
        return null;
    }

    /// <summary>ProjectSetup's build: adds Life/Breach and the hunter's body to AsylumHub.prefab (nothing else in it changes). The scene instance refreshes; the caller saves the scene.</summary>
    public static void BuildBreach()
    {
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null) { Debug.LogError("Asylum breach: there is no hub in the scene (Build Missing Pieces places it first)."); return; }
        if (hub.life == null || hub.life.GetComponentInChildren<KeyRunSetPiece>(true) == null) { Debug.LogError("Asylum breach: the hub has no Life/KeyRun (run KEEP IT LIT > Build Missing Pieces)."); return; }
        if (BreachProblem() == null) { Debug.Log("Asylum breach: Life/Breach is already there."); return; }

        StringBuilder report = new StringBuilder();
        EditHubPrefab(root =>
        {
            Transform life = root.transform.Find("Life");
            KeyRunSetPiece keyRun = life.GetComponentInChildren<KeyRunSetPiece>(true);
            Transform breach = life.Find(BreachGroupName);
            if (breach == null)
            {
                breach = Group(life, BreachGroupName);
                report.AppendLine("added Life/" + BreachGroupName);
            }
            HubBreachSetPiece piece = breach.GetComponent<HubBreachSetPiece>();
            if (piece == null) { piece = breach.gameObject.AddComponent<HubBreachSetPiece>(); report.AppendLine("added HubBreachSetPiece"); }
            piece.floor = HubBreach.Floor;

            Transform holder = breach.Find(BreachHunterName);
            if (holder == null)
            {
                holder = Group(breach, BreachHunterName);
                holder.gameObject.AddComponent<HubStalker>();
                NavMeshAgent agent = holder.gameObject.AddComponent<NavMeshAgent>();
                int type = HubNavMesh.FindAgentType();
                if (type != -1) agent.agentTypeID = type;
                agent.radius = 0.3f;
                agent.height = 1.8f; // HubStalker.EnsureAgent fixes 1.8 at runtime
                agent.enabled = false; // the prefab is saved with it off; HubStalker.Place enables it on the navmesh
                report.AppendLine("added Life/Breach/Hunter (HubStalker + disabled NavMeshAgent)");
            }

            string wanted = BreachBodyWanted();
            if (holder.Find(wanted) == null)
            {
                for (int i = holder.childCount - 1; i >= 0; i--) if (holder.GetChild(i).name.StartsWith("Body")) Object.DestroyImmediate(holder.GetChild(i).gameObject);
                report.AppendLine(wanted == BreachCyborgBody ? BuildBreachCyborg(holder) : BuildBreachOrderly(holder));
            }
            holder.gameObject.SetActive(false);

            SerializedObject so = new SerializedObject(piece);
            so.FindProperty("keyRun").objectReferenceValue = keyRun;
            so.FindProperty("hunter").objectReferenceValue = holder.GetComponent<HubStalker>();
            so.ApplyModifiedPropertiesWithoutUndo();
        });
        EditorUtility.SetDirty(hub);
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());
        Debug.Log("Asylum breach (F121):\n" + report + "Save the scene (Ctrl+S) - the prefab is already saved.");
    }

    private static string BuildBreachCyborg(Transform holder)
    {
        GameObject model = HunterBodyBuilder.EnsureCyborgModel();
        if (model == null) return BuildBreachOrderly(holder);
        bool holderWasActive = holder.gameObject.activeSelf;
        holder.gameObject.SetActive(true); // Animator.Rebind / Update need an active object
        GameObject body = HunterBodyBuilder.InstantiateCyborg(model, holder);
        body.name = BreachCyborgBody;
        body.transform.localPosition = Vector3.zero;
        body.transform.localRotation = Quaternion.identity;
        StripPhysics(body);
        Animator animator = body.GetComponent<Animator>();
        animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(BreachController);
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // the avatar's visual facing is the holder's forward, which is what HubStalker steers
        Vector3 visual = Vector3.ProjectOnPlane(animator.transform.forward, Vector3.up);
        float nodeYaw = Vector3.SignedAngle(Vector3.ProjectOnPlane(holder.forward, Vector3.up), visual, Vector3.up);
        body.transform.localRotation = Quaternion.Euler(0f, -nodeYaw, 0f);
        AddRedEyes(animator, CyborgEyeForward, CyborgEyeUp, CyborgEyeSpacing, CyborgEyeSize);
        holder.gameObject.SetActive(holderWasActive);

        SerializedObject so = new SerializedObject(holder.GetComponent<HubStalker>());
        so.FindProperty("animator").objectReferenceValue = animator;
        so.ApplyModifiedPropertiesWithoutUndo();
        return "Life/Breach/Hunter: the Cyborg body (Starter Assets locomotion controller, red eye beads on the head bone)";
    }

    private static string BuildBreachOrderly(Transform holder)
    {
        string result = SwapPursuerBody(holder, PursuerRobotPrefab);
        foreach (Transform child in holder)
        {
            if (child.name.StartsWith("Body")) { child.name = BreachOrderlyBody; break; }
        }
        return "Life/Breach/Hunter: the Orderly body (" + result + ")";
    }

    // Eye bead placement on the Cyborg's head bone (m): in front of the joint, above it, either side, bead size. Measured in a Play render.
    private const float CyborgEyeForward = 0.11f;
    private const float CyborgEyeUp = 0.085f;
    private const float CyborgEyeSpacing = 0.04f;
    private const float CyborgEyeSize = 0.035f;
}
