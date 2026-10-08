using UnityEngine;

/// <summary>
/// F98: one of the six people who live in the asylum hub besides the three patients (AsylumHub/Life/Npcs/Name): Sister Agnes, Bruno,
/// Josef, Marta, Dr. Weiss and the Clerk. Like HubPatient it is visual + voice only - no NavMesh, nobody walks: where it stands, lies or
/// sits is baked into the prefab, and whether it is there at all is a pure function of HubState (floors 1-4, the Clerk also on floor 5
/// and in the finale). E (PlayerInteractor, so touch and gamepad work) opens the <see cref="DialoguePanel"/> with whatever
/// <see cref="HubMission.TryConversation"/> says; the panel's Talking clip plays on the body while it is open. Between talks the body
/// idles on the shared HubPeople animator (Look / Nervous / Lie / SitIdle / Typing) with its head turning slowly toward the player
/// within a few metres, applied in LateUpdate on top of that frame's animated pose (the HubPatient / Stalker pattern).
/// </summary>
public class HubNpc : MonoBehaviour, IInteractable
{
    public enum Who { Agnes = 0, Bruno = 1, Josef = 2, Marta = 3, Weiss = 4, Clerk = 5 }

    public Who who;
    [Tooltip("Speaker name as the panel and the prompt show it, e.g. SISTER AGNES.")]
    public string displayName = "";
    [Tooltip("The model root (the Animator is on it). Switched off while the NPC is absent.")]
    public Transform body;
    [Tooltip("Position + yaw of the body (a lying NPC: the foot end of the bed, yaw pointing along the bed like HubPatient's bed anchor).")]
    public Transform anchor;
    [Tooltip("HubPeople.controller. Assigned to the body Animator at Awake.")]
    public RuntimeAnimatorController controller;

    [Header("Animation")]
    [Tooltip("State on the HubPeople controller while idle: Look, Nervous, Lie, SitIdle, SitDisbelief, Typing, StoopIdle.")]
    public string baseState = "Look";
    [Tooltip("State while the panel is open. Empty = keep the base state (Josef lies on, the Clerk keeps typing).")]
    public string talkState = "Talking";
    public bool lying;

    [Header("Behaviour")]
    [Tooltip("The head follows the player within headRange. The Clerk never looks up.")]
    public bool turnsHead = true;
    [Tooltip("The player's view turns to this NPC's head while talking.")]
    public bool playerFacesMe = true;
    public float headRange = 4.5f;
    public float headMaxYaw = 60f;

    /// <summary>Conversations held so far this visit (the Clerk cycles his three).</summary>
    public int Talks { get; set; }

    private Animator _animator;
    private Transform _head;
    private bool _present;
    private bool _talking;
    private float _headYaw;
    private string _playing;
    private int _stateFloor = -1;
    private bool _stateFinale;
    private int _seatFrames;
    private SkinnedMeshRenderer[] _skins;
    private Vector3 _basePos;
    private Quaternion _baseRot;

    public bool IsPresent => _present && body != null && body.gameObject.activeInHierarchy;
    public bool IgnoresGaze => !playerFacesMe;
    public Vector3 HeadPosition => _head != null ? _head.position : (body != null ? body.position + Vector3.up * (lying ? 0.9f : 1.5f) : transform.position);
    public string Name => string.IsNullOrEmpty(displayName) ? who.ToString().ToUpperInvariant() : displayName;

    /// <summary>The floors this person is in the hospital: the staff and patients of floors 1-4; the Clerk always.</summary>
    public static bool PresentOn(Who who, int floor, bool finale)
    {
        if (who == Who.Clerk) return true;
        return !finale && floor <= 4;
    }

    private void Awake()
    {
        if (body != null)
        {
            _animator = body.GetComponent<Animator>();
            if (_animator != null)
            {
                if (controller != null) _animator.runtimeAnimatorController = controller;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (_animator.isHuman) _head = _animator.GetBoneTransform(HumanBodyBones.Head);
            }
        }
    }

    private void OnEnable()
    {
        AsylumHub.OnEnterHub += HandleEnter;
        _stateFloor = -1;
        Refresh();
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        _talking = false;
    }

    private void HandleEnter(int floor)
    {
        _stateFloor = -1;
        Talks = 0;
        Refresh();
    }

    /// <summary>Applies HubState: presence and the idle pose. Cheap and idempotent.</summary>
    public void Refresh()
    {
        int floor = HubState.Floor;
        bool finale = HubState.IsFinale;
        _stateFloor = floor;
        _stateFinale = finale;
        _present = PresentOn(who, floor, finale);
        if (body != null) body.gameObject.SetActive(_present);
        if (!_present) return;

        if (anchor != null)
        {
            _basePos = anchor.position;
            _baseRot = anchor.rotation;
            body.SetPositionAndRotation(_basePos, _baseRot);
        }
        _playing = null;
        PlayState(baseState, 0f);
        _seatFrames = lying ? 180 : 0;
        _seatWait = 0;
    }

    private int _seatWait;

    public void BeginTalk()
    {
        _talking = true;
    }

    public void EndTalk()
    {
        _talking = false;
    }

    private void Update()
    {
        if (HubState.Floor != _stateFloor || HubState.IsFinale != _stateFinale) Refresh();
        if (!_present) return;
        string wanted = _talking && !string.IsNullOrEmpty(talkState) ? talkState : baseState;
        PlayState(wanted, 0.3f);
    }

    private void PlayState(string state, float fade)
    {
        if (_animator == null || !_animator.isActiveAndEnabled || _animator.runtimeAnimatorController == null || _playing == state) return;
        _playing = state;
        if (fade <= 0f) _animator.Play(state, 0, Random.value); else _animator.CrossFadeInFixedTime(state, fade);
    }

    // ---------------------------------------------------------------- lying: seat the body on the mattress (as HubPatient does for Elias)

    /// <returns>-1 when the avatar or bounds are not ready yet, else how far (m) the body had to move.</returns>
    private float SeatLying()
    {
        if (_animator == null || !_animator.isHuman || anchor == null) return -1f;
        if (_skins == null || _skins.Length == 0) { _skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true); foreach (SkinnedMeshRenderer r in _skins) r.updateWhenOffscreen = true; }
        if (_head == null) _head = _animator.GetBoneTransform(HumanBodyBones.Head);
        Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null || _head == null) return -1f;
        Vector3 along = Vector3.ProjectOnPlane(_head.position - hips.position, Vector3.up);
        Vector3 desired = -anchor.forward;
        if (along.sqrMagnitude > 0.0004f)
        {
            float delta = Vector3.SignedAngle(along, desired, Vector3.up);
            if (Mathf.Abs(delta) > 1f) { _baseRot = Quaternion.AngleAxis(delta, Vector3.up) * _baseRot; body.rotation = _baseRot; }
        }
        Bounds b = default; bool any = false;
        foreach (SkinnedMeshRenderer r in _skins) { if (!r.enabled) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        if (!any) return -1f;
        Vector3 centre = anchor.position - anchor.forward * 0.95f;
        float surface = anchor.position.y - 0.13f + 0.03f;
        Vector3 shift = new Vector3(centre.x - b.center.x, surface - b.min.y, centre.z - b.center.z);
        _basePos += shift;
        return shift.magnitude;
    }

    private void LateUpdate()
    {
        if (!_present || body == null) return;
        if (lying)
        {
            body.SetPositionAndRotation(_basePos + Vector3.up * (Mathf.Sin(Time.time * 1.1f) * 0.006f), _baseRot);
            // The Animator needs a frame or three after the body switches on before the human avatar and the skinned bounds are
            // valid, so keep trying (a fixed handful of frames can all be spent before they are); once seated, a few more settle it.
            if (_seatFrames > 0)
            {
                _seatFrames--;
                if (_seatWait > 0) _seatWait--; // the skinned bounds lag a frame or two behind a move: measure again only after they caught up
                else
                {
                    float moved = SeatLying();
                    if (moved >= 0f)
                    {
                        _seatWait = 3;
                        if (moved < 0.02f) _seatFrames = 0;
                    }
                }
                body.SetPositionAndRotation(_basePos, _baseRot);
            }
        }

        if (_head == null || !turnsHead || lying) return;
        float target = 0f;
        AsylumHub hub = AsylumHub.Active;
        Camera cam = hub != null ? hub.PlayerCamera : null;
        if (cam != null)
        {
            Vector3 axis = body.up;
            Vector3 forward = Vector3.ProjectOnPlane(body.forward, axis);
            Vector3 to = Vector3.ProjectOnPlane(cam.transform.position - _head.position, axis);
            float range = _talking ? headRange * 1.6f : headRange;
            if (to.magnitude < range && to.sqrMagnitude > 0.0001f && forward.sqrMagnitude > 0.0001f)
            {
                float yaw = Vector3.SignedAngle(forward, to, axis);
                if (Mathf.Abs(yaw) <= 110f) target = Mathf.Clamp(yaw, -headMaxYaw, headMaxYaw);
            }
        }
        _headYaw = Mathf.MoveTowardsAngle(_headYaw, target, 90f * Time.deltaTime);
        if (Mathf.Abs(_headYaw) > 0.01f) _head.rotation = Quaternion.AngleAxis(_headYaw, body.up) * _head.rotation;
    }

    // ---------------------------------------------------------------- IInteractable

    string IInteractable.Prompt => $"TALK TO {Name}";
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => IsPresent && !DialoguePanel.IsOpen && DialoguePanel.Current != null;
    void IInteractable.OnFocus(bool focused) { }

    void IInteractable.Interact(PlayerInteractor by)
    {
        DialoguePanel panel = DialoguePanel.Current;
        AsylumHub hub = AsylumHub.Active;
        if (panel == null || hub == null || hub.IsTransitioning || hub.Leaving) return;
        if (hub.PursuerAfterPlayer())
        {
            hub.SubtitleLine("NOT NOW", 1.6f);
            return;
        }
        if (!HubMission.TryConversation(this, out HubDialogue.Node[] nodes, out System.Action done, out int count, out int total)) return;
        Talks++;
        panel.Begin(this, nodes, done, count, total);
    }
}
