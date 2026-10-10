using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// F104 B7/B8: the one body every figure phantom wears (a shadow crossing the lit wall, a figure at the edge of
/// the view) - a clone of the hunter's own HunterBody child, stripped to its Animator and renderers, so a phantom
/// has exactly the hunter's silhouette and gait. Built once by PhantomDirector, lazily, and pooled: the director
/// shows it, poses it, then hides it again. Plain helper (not a MonoBehaviour), so it lives beside its only user.
///
/// It is gameplay-inert: no scripts, colliders, rigidbodies or audio, and the eyes and the chest stencil
/// are removed - a phantom must not be mistaken for the real thing. The one light a body may carry is the double's head spot (F105 C8). The cloned Animator never fires events (the
/// Starter clips would log "no receiver") and always animates, since a ShadowsOnly renderer may not count as visible.
/// </summary>
public class PhantomBody
{
    /// <summary>The hunter's visible body child, the same name HunterBodyBuilder.BodyChildName gives it (an editor const, so repeated here).</summary>
    private const string BodyChildName = "HunterBody";
    private const string HeadBoneName = "Head";

    private static readonly string[] StrippedChildNames = { "LeftEye_Glow", "RightEye_Glow", "ChestStencil" };
    private static readonly int AnimSpeed = Animator.StringToHash("Speed");
    private static readonly int AnimMotionSpeed = Animator.StringToHash("MotionSpeed");
    private static readonly int AnimGrounded = Animator.StringToHash("Grounded");
    private static readonly int AnimPose = Animator.StringToHash("Pose");

    private readonly Renderer[] _renderers;
    private readonly Material[][] _originalMaterials;
    private readonly Animator _animator;
    private readonly int[] _paramHashes;
    private Light _headSpot; // F105 C8: only the double's body has one

    /// <summary>The wrapper that is positioned and turned; the cloned body sits inside it at the hunter's own local offset.</summary>
    public GameObject Root { get; }

    /// <summary>F106: the body's first material, the source for a silhouette copy (see PhantomDirector.BuildSilhouetteMaterial). Null for a body with no renderer.</summary>
    public Material FirstMaterial => _originalMaterials.Length > 0 && _originalMaterials[0].Length > 0 ? _originalMaterials[0][0] : null;

    /// <summary>True while a phantom is using the body, so the other figure phantom does not borrow it mid-use.</summary>
    public bool InUse { get; private set; }

    private PhantomBody(GameObject root, Animator animator, Renderer[] renderers)
    {
        Root = root;
        _animator = animator;
        _renderers = renderers;
        _originalMaterials = new Material[renderers.Length][];
        for (int i = 0; i < renderers.Length; i++) _originalMaterials[i] = renderers[i].sharedMaterials;

        // Read once: Animator.parameters allocates a fresh array on every call.
        AnimatorControllerParameter[] parameters = animator != null ? animator.parameters : new AnimatorControllerParameter[0];
        _paramHashes = new int[parameters.Length];
        for (int i = 0; i < parameters.Length; i++) _paramHashes[i] = parameters[i].nameHash;
    }

    /// <summary>Clones and strips the hunter's HunterBody under `parent`. Returns null when the hunter has no such child.</summary>
    public static PhantomBody TryBuild(AIFollower follower, Transform parent)
    {
        return TryBuild(follower != null ? follower.transform.Find(BodyChildName) : null, parent);
    }

    /// <summary>
    /// F105 C8: clones and strips the Stalker template's body (the player's own robot, in the template's dark skin) under `parent` -
    /// the double's figure. Returns null when there is no template. Its glow eyes go with the rest (Strip), so the double has none.
    /// </summary>
    public static PhantomBody TryBuild(Stalker template, Transform parent)
    {
        return TryBuild(template != null ? template.transform : null, parent);
    }

    private static PhantomBody TryBuild(Transform source, Transform parent)
    {
        if (source == null) return null;

        GameObject root = new GameObject("Phantom_Body");
        root.SetActive(false);
        root.transform.SetParent(parent, false);

        // Instantiated under the inactive root, so none of the clone's scripts wake up before they are removed.
        GameObject clone = Object.Instantiate(source.gameObject, root.transform);
        clone.name = BodyChildName;
        clone.transform.localPosition = source.localPosition;
        clone.transform.localRotation = source.localRotation;
        clone.transform.localScale = source.localScale;
        Strip(clone);
        clone.SetActive(true); // a template sits inactive in its gallery; the root above is inactive too, so nothing wakes before Show

        Animator animator = clone.GetComponentInChildren<Animator>(true);
        if (animator != null)
        {
            animator.fireEvents = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.applyRootMotion = false;
        }

        return new PhantomBody(root, animator, clone.GetComponentsInChildren<Renderer>(true));
    }

    // DestroyImmediate on purpose: the clone is inactive, and a deferred Destroy would still let its scripts wake when the body is first shown.
    private static void Strip(GameObject clone)
    {
        foreach (MonoBehaviour behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true)) Object.DestroyImmediate(behaviour);
        foreach (Collider collider in clone.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(collider);
        foreach (Rigidbody body in clone.GetComponentsInChildren<Rigidbody>(true)) Object.DestroyImmediate(body);
        foreach (AudioSource audio in clone.GetComponentsInChildren<AudioSource>(true)) Object.DestroyImmediate(audio);
        foreach (Light light in clone.GetComponentsInChildren<Light>(true)) Object.DestroyImmediate(light);

        List<GameObject> doomed = new List<GameObject>();
        foreach (Transform child in clone.GetComponentsInChildren<Transform>(true))
        {
            if (System.Array.IndexOf(StrippedChildNames, child.name) >= 0) doomed.Add(child.gameObject);
        }

        foreach (GameObject go in doomed) Object.DestroyImmediate(go);
    }

    /// <summary>Puts the body at `position` facing `forward` (flat), switched on and marked in use. The Animator's parameters can only be written once it is active.</summary>
    public void Show(Vector3 position, Vector3 forward)
    {
        InUse = true;
        Root.transform.SetPositionAndRotation(position, forward.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(forward, Vector3.up) : Quaternion.identity);
        Root.SetActive(true);
        if (_animator == null) return;

        _animator.enabled = true;
        SetFloat(AnimSpeed, 0f);
        SetFloat(AnimMotionSpeed, 1f);
        if (HasParam(AnimGrounded)) _animator.SetBool(AnimGrounded, true);
    }

    /// <summary>Plays the walk gait (Speed 1, MotionSpeed 1) if the controller has those parameters.</summary>
    public void Walk()
    {
        SetFloat(AnimSpeed, 1f);
        SetFloat(AnimMotionSpeed, 1f);
    }

    /// <summary>Holds the body in `pose` (an AIFollower.Pose value) for good: the Animator is advanced half a second into it, then switched off so it stays frozen.</summary>
    public void Freeze(AIFollower.Pose pose)
    {
        if (_animator == null) return;

        if (HasParam(AnimPose)) _animator.SetInteger(AnimPose, (int)pose);
        _animator.Update(0.5f);
        _animator.enabled = false;
    }

    /// <summary>
    /// F105 C8: a small spot light on the head bone, switched on and off with the body (Strip removed every light the clone had, so this
    /// is the only one). Aim it with <see cref="AimSpot"/> once the body is shown. Idempotent: a second call keeps the first light.
    /// </summary>
    public void AddHeadSpot(float range, float intensity, float spotAngle, Color color)
    {
        if (_headSpot != null) return;

        Transform head = _animator != null && _animator.isHuman ? _animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) head = FindByName(Root.transform, HeadBoneName);

        GameObject holder = new GameObject("Double_Spot");
        holder.transform.SetParent(head != null ? head : Root.transform, false);
        _headSpot = holder.AddComponent<Light>();
        _headSpot.type = LightType.Spot;
        _headSpot.range = range;
        _headSpot.intensity = intensity;
        _headSpot.spotAngle = spotAngle;
        _headSpot.color = color;
        _headSpot.shadows = LightShadows.None;
    }

    /// <summary>F105 C8: points the head spot at a world position (the player). The body is frozen, so the aim holds until it is hidden.</summary>
    public void AimSpot(Vector3 worldPoint)
    {
        if (_headSpot == null) return;

        Vector3 direction = worldPoint - _headSpot.transform.position;
        if (direction.sqrMagnitude > 1e-4f) _headSpot.transform.rotation = Quaternion.LookRotation(direction);
    }

    private static Transform FindByName(Transform root, string childName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == childName) return child;
        }

        return null;
    }

    /// <summary>Every renderer casts the given shadow mode (ShadowsOnly for the shadow phantom, On for the visible figure).</summary>
    public void SetShadowMode(ShadowCastingMode mode)
    {
        foreach (Renderer renderer in _renderers) renderer.shadowCastingMode = mode;
    }

    /// <summary>Swaps every material slot for `material`, keeping each renderer's slot count.</summary>
    public void SetMaterial(Material material)
    {
        for (int i = 0; i < _renderers.Length; i++)
        {
            Material[] slots = new Material[_originalMaterials[i].Length];
            for (int s = 0; s < slots.Length; s++) slots[s] = material;
            _renderers[i].sharedMaterials = slots;
        }
    }

    /// <summary>Switches the body off and puts everything back for the next use: original materials, normal shadows, a running Animator.</summary>
    public void Hide()
    {
        for (int i = 0; i < _renderers.Length; i++) _renderers[i].sharedMaterials = _originalMaterials[i];
        SetShadowMode(ShadowCastingMode.On);
        if (_animator != null) _animator.enabled = true;
        Root.SetActive(false);
        InUse = false;
    }

    private bool HasParam(int hash)
    {
        return System.Array.IndexOf(_paramHashes, hash) >= 0;
    }

    private void SetFloat(int hash, float value)
    {
        if (_animator != null && HasParam(hash)) _animator.SetFloat(hash, value);
    }
}
