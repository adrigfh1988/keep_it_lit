using StarterAssets;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Turns the third-person starter rig into a first-person one.
///
/// Rather than reconfigure Cinemachine, this switches the brain off and parents the camera under
/// PlayerCameraRoot. ThirdPersonController.CameraRotation() already writes that transform's world
/// rotation from mouse look every LateUpdate, so a child camera inherits the view with no ordering
/// question at all — which is the whole reason for doing it this way.
/// </summary>
[DefaultExecutionOrder(-90)]
public class FirstPersonRig : MonoBehaviour
{
    [Header("Camera")]
    [Tooltip("Eye height above the player's feet. PlayerCameraRoot sits at 1.375.")]
    [SerializeField] private float eyeHeight = 1.6f;
    [Tooltip("Vertical FOV. 70 is about 104 horizontal at 16:9 - you take in a whole junction at once instead of a slice of one.")]
    [SerializeField] private float fieldOfView = 70f;
    [Tooltip("How far up and down the player can look. The starter rig clamps to -30/70, which in first person means you cannot see the floor.")]
    [SerializeField] private float pitchClamp = 80f;

    [Header("Body")]
    [SerializeField] private bool hidePlayerBody = true;

    [Header("Flashlight")]
    [SerializeField] private bool spawnFlashlight = true;

    private const float CameraRootHeight = 1.375f;

    public Camera PlayerCamera { get; private set; }
    public Flashlight Flashlight { get; private set; }
    public PlayerStealthState Stealth { get; private set; }

    private readonly System.Collections.Generic.List<Renderer> _hiddenRenderers = new System.Collections.Generic.List<Renderer>();

    /// <summary>
    /// F120: the death replay films the player's own robot, so the renderers HideBody switched off come back on. Only for the end screen, where the
    /// player camera is masked out and the world is behind an opaque blackout; every end-screen button reloads the scene, so nothing is restored.
    /// </summary>
    public void ShowBodyForReplay()
    {
        foreach (Renderer renderer in _hiddenRenderers)
        {
            if (renderer != null) renderer.enabled = true;
        }
    }

    private void Awake()
    {
        // The same prefab is instantiated again as the AI's body, where the Robot object is retagged
        // Untagged. This makes the component a no-op there whatever else happens.
        if (!CompareTag("Player"))
        {
            enabled = false;
            return;
        }

        Transform cameraRoot = transform.Find("PlayerCameraRoot");
        if (cameraRoot == null)
        {
            Debug.LogError("FirstPersonRig: no PlayerCameraRoot under the player.", this);
            return;
        }

        GameObject cameraObject = GameObject.FindGameObjectWithTag("MainCamera");
        if (cameraObject == null)
        {
            Debug.LogError("FirstPersonRig: no active MainCamera in the scene.", this);
            return;
        }

        // The Cinemachine camera lives beside the player, not under it. Switching it off first means
        // MazeGenerator's warp call finds nothing to do, which is the intent.
        if (transform.parent != null)
        {
            Transform followCamera = transform.parent.Find("PlayerFollowCamera");
            if (followCamera != null) followCamera.gameObject.SetActive(false);
        }

        // enabled rather than Destroy: reversible, and no deferred-destruction semantics to reason about.
        CinemachineBrain brain = cameraObject.GetComponent<CinemachineBrain>();
        if (brain != null) brain.enabled = false;

        cameraObject.transform.SetParent(cameraRoot, false);
        cameraObject.transform.localPosition = new Vector3(0f, eyeHeight - CameraRootHeight, 0f);
        cameraObject.transform.localRotation = Quaternion.identity;

        PlayerCamera = cameraObject.GetComponent<Camera>();
        _baseFov = GameSettings.FieldOfView;
        if (PlayerCamera != null)
        {
            PlayerCamera.fieldOfView = _baseFov; // S2: the setting owns the FOV now (its default, GameSettings.DefaultFov, mirrors the 70 serialized above)
        }

        MoveAudioListener(cameraObject);
        ConfigureController();

        if (hidePlayerBody) HideBody();

        Stealth = GetComponent<PlayerStealthState>();
        if (Stealth == null) Stealth = gameObject.AddComponent<PlayerStealthState>();

        if (spawnFlashlight)
        {
            Flashlight = cameraObject.AddComponent<Flashlight>();
            Flashlight.Bind(Stealth);
        }
    }

    private float _baseFov = 70f;

    private void OnEnable() => GameSettings.Changed += ApplyFieldOfView;
    private void OnDisable() => GameSettings.Changed -= ApplyFieldOfView;

    /// <summary>S2: the player camera's base FOV follows the Field of view setting. F126: LateUpdate offsets from _baseFov for focused beam and locker hiding.</summary>
    private void ApplyFieldOfView()
    {
        _baseFov = GameSettings.FieldOfView;
        if (PlayerCamera != null) PlayerCamera.fieldOfView = _baseFov;
    }

    private void LateUpdate()
    {
        if (PlayerCamera == null) return;
        _baseFov = GameSettings.FieldOfView;
        float focusOffset = Flashlight != null ? -8f * Flashlight.FocusBlend : 0f;
        float hideOffset = Stealth != null && Stealth.Hidden ? -5f : 0f;
        float targetFov = _baseFov + (focusOffset + hideOffset) * GameSettings.EffectsIntensity;
        PlayerCamera.fieldOfView = Mathf.Lerp(PlayerCamera.fieldOfView, targetFov, 1f - Mathf.Exp(-14f * Time.deltaTime));
    }

    private void ConfigureController()
    {
        ThirdPersonController controller = GetComponent<ThirdPersonController>();
        if (controller == null) return;

        controller.FirstPerson = true;
        controller.BottomClamp = -pitchClamp;
        controller.TopClamp = pitchClamp;
    }

    private void HideBody()
    {
        // Renderer.enabled is per-instance, and the AI is a separate instantiation of this prefab,
        // so switching these off cannot affect it. transform.root catches the nested robot FBX,
        // where the actual SkinnedMeshRenderers live.
        _hiddenRenderers.Clear();
        foreach (Renderer renderer in transform.root.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.enabled) _hiddenRenderers.Add(renderer); // F120: remembered so the death replay can show the body again
            renderer.enabled = false;
        }

        // The prefab ships CullUpdateTransforms, so with every renderer hidden the Animator would
        // stop writing transforms - and with it the footstep animation events. Animating one unseen
        // skeleton is cheap; losing the footsteps is not.
        Animator animator = GetComponent<Animator>();
        if (animator != null)
        {
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }

    private static void MoveAudioListener(GameObject cameraObject)
    {
        // The only live listener sits on the player root, which Move() yaws toward the direction of
        // travel - so looking around without moving pans the whole mix the wrong way. Everything
        // scary here is positional, so this matters more than it sounds.
        foreach (AudioListener listener in FindObjectsByType<AudioListener>(FindObjectsInactive.Include))
        {
            listener.enabled = false;
        }

        AudioListener cameraListener = cameraObject.GetComponent<AudioListener>();
        if (cameraListener == null) cameraListener = cameraObject.AddComponent<AudioListener>();
        cameraListener.enabled = true;
    }
}
