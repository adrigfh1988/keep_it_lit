using UnityEngine;

/// <summary>
/// F93 (E5): one of the three patients who live in the asylum hub (AsylumHub/Life/Patients/Name). Visual and voice only - no
/// navmesh movement: where it stands/lies and whether it is there at all is a pure function of HubState (floor + finale), applied by
/// <see cref="Refresh"/> whenever the hub switches on, on AsylumHub.OnEnterHub and whenever the floor or finale flag changes.
/// The body is a humanoid on an idle controller; everything alive about it is done in LateUpdate as offsets on top of that frame's
/// animated pose (the Stalker F81 pattern - the Animator keeps running and AlwaysAnimate, so offsets never compound):
///  - the head turns toward the player within 5 m (yaw only, up to 70 degrees);
///  - Hilde rocks (a gentle pitch about her feet);
///  - Tomas knocks on the pipes: his right arm comes up and taps, with a knock sound per tap (2 knocks, 3 on floors 4-5);
///  - Elias lies breathing in bed (floors 1-2), stands facing the wall at the foot of it (floor 3), and from floor 4 is gone: his
///    body lies under a sheet on the morgue table (morgueGroup), with the wristband, and drawer five stands open.
/// </summary>
public class HubPatient : MonoBehaviour
{
    public HubState.Patient patient;
    [Tooltip("The model root (the Animator is on it). Switched off while the patient is absent.")]
    public Transform body;

    [Header("Where the patient is (position + yaw)")]
    public Transform defaultAnchor;
    [Tooltip("Elias lying in bed, floors 1-2.")]
    public Transform bedAnchor;
    [Tooltip("Elias standing at the foot of the bed facing the wall, floor 3.")]
    public Transform standAnchor;
    [Tooltip("The finale: a memory standing along the route.")]
    public Transform finaleAnchor;

    [Header("Elias: the morgue from floor 4")]
    public Transform morgueGroup;
    [Tooltip("The parts of pack drawer five of the morgue drawer wall (the tray and its front); each is slid out by drawerOpenOffset (WORLD space) when Elias is dead.")]
    public Transform[] drawerParts = new Transform[0];
    public Vector3 drawerOpenOffset = new Vector3(0f, 0f, -1.25f);

    [Header("Mixamo animation (F93 G4+)")]
    [Tooltip("HubPeople.controller: states Look, Nervous, Lie, SitIdle, SitDisbelief, Talking, StandUp, StoopIdle. Assigned to the body Animator at Awake.")]
    public RuntimeAnimatorController controller;
    [Tooltip("Yaw added to the lying anchor so the Laying Idle clip's head end points where the anchor says (tuned by eye).")]
    public float lieYawOffset;
    [Tooltip("Metres added to the lying anchor height (the clip's hips sit above the root).")]
    public float lieHeight;

    [Header("Tuning")]
    public float headRange = 5f;
    public float headMaxYaw = 70f;
    public float rockDegrees = 2f;
    public float rockHertz = 0.45f;
    public float knockEverySeconds = 9f;
    public float knockVolume = 0.9f;

    private Animator _animator;
    private Transform _head;
    private Transform _rightArm;
    private AudioSource _audio;
    private AudioClip _knock;
    private Vector3[] _drawerClosed;

    private bool _present;
    private int _stateFloor = -1;
    private bool _stateFinale;
    private Pose _pose;
    private Vector3 _basePos;
    private Quaternion _baseRot;
    private float _headYaw;
    private float _clock;
    private int _lastPulse = -1;
    private Transform _anchorUsed;
    private int _seatFrames;
    private SkinnedMeshRenderer[] _skins;
    private PatientVoice _voice;
    private string _baseState = "Look";
    private string _playing;

    private enum Pose { Stand, Rock, Knock, Lie }

    public bool IsPresent => _present && body != null && body.gameObject.activeInHierarchy;
    public Vector3 HeadPosition => _head != null ? _head.position : (body != null ? body.position + Vector3.up * 1.6f : transform.position);

    private void Awake()
    {
        if (body != null)
        {
            _animator = body.GetComponent<Animator>();
            if (_animator != null)
            {
                if (controller != null) _animator.runtimeAnimatorController = controller;
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (_animator.isHuman)
                {
                    _head = _animator.GetBoneTransform(HumanBodyBones.Head);
                    _rightArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                }
            }
        }
        _voice = GetComponent<PatientVoice>();
        _drawerClosed = new Vector3[drawerParts != null ? drawerParts.Length : 0];
        for (int i = 0; i < _drawerClosed.Length; i++) _drawerClosed[i] = drawerParts[i] != null ? drawerParts[i].position : Vector3.zero;
        if (patient == HubState.Patient.Tomas)
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 2f;
            _audio.maxDistance = 14f; // clearly in the machine room, not through half the building
            _knock = DoorAudio.BuildPipeClankClip();
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
    }

    private void HandleEnter(int floor)
    {
        _stateFloor = -1;
        Refresh();
    }

    private void Update()
    {
        if (_present && _voice != null && _pose != Pose.Lie) PlayState(_voice.IsTalking ? "Talking" : _baseState, 0.3f);
        if (HubState.Floor != _stateFloor || HubState.IsFinale != _stateFinale) Refresh();
        if (_pose == Pose.Knock && _present) TickKnock();
    }

    /// <summary>Applies HubState: presence, placement and pose. Cheap and idempotent.</summary>
    public void Refresh()
    {
        int floor = HubState.Floor;
        bool finale = HubState.IsFinale;
        _stateFloor = floor;
        _stateFinale = finale;
        _present = HubState.PatientPresent(patient, floor, finale);

        bool dead = patient == HubState.Patient.Elias && HubState.EliasIsDead(floor, finale);
        if (morgueGroup != null) morgueGroup.gameObject.SetActive(dead);
        for (int i = 0; _drawerClosed != null && i < _drawerClosed.Length; i++)
        {
            if (drawerParts[i] != null) drawerParts[i].position = dead ? _drawerClosed[i] + drawerOpenOffset : _drawerClosed[i];
        }

        if (body != null) body.gameObject.SetActive(_present);
        if (!_present) return;

        Transform anchor = defaultAnchor;
        _pose = Pose.Stand;
        _baseState = "Look";
        if (finale)
        {
            anchor = finaleAnchor != null ? finaleAnchor : defaultAnchor;
            _baseState = patient == HubState.Patient.Hilde ? "Look" : patient == HubState.Patient.Tomas ? "Look" : "Nervous";
        }
        else if (patient == HubState.Patient.Hilde) { _pose = Pose.Rock; _baseState = "Nervous"; }
        else if (patient == HubState.Patient.Tomas) { _pose = Pose.Knock; _baseState = "StoopIdle"; }
        else if (patient == HubState.Patient.Elias)
        {
            if (floor <= 2) { anchor = bedAnchor != null ? bedAnchor : defaultAnchor; _pose = Pose.Lie; _baseState = "Lie"; }
            else { anchor = standAnchor != null ? standAnchor : defaultAnchor; _baseState = "Nervous"; }
        }
        if (anchor == null) return;

        _basePos = anchor.position;
        _baseRot = anchor.rotation;
        // Lying: the Mixamo Laying Idle clip already lies on its back; the offsets only turn and lift it onto the mattress.
        if (_pose == Pose.Lie) { _baseRot = anchor.rotation * Quaternion.Euler(0f, lieYawOffset, 0f); _basePos += Vector3.up * lieHeight; }
        _playing = null;
        PlayState(_baseState, 0f);
        _anchorUsed = anchor;
        _seatFrames = _pose == Pose.Lie ? 8 : 0;
        ApplyRoot(0f);
        _lastPulse = -1;
        _clock = 0f;
    }

    private void PlayState(string state, float fade)
    {
        if (_animator == null || !_animator.isActiveAndEnabled || _animator.runtimeAnimatorController == null || _playing == state) return;
        _playing = state;
        if (fade <= 0f) _animator.Play(state, 0, Random.value); else _animator.CrossFadeInFixedTime(state, fade);
    }

    private void ApplyRoot(float t)
    {
        if (body == null) return;
        Quaternion rot = _baseRot;
        Vector3 pos = _basePos;
        if (_pose == Pose.Rock) rot = _baseRot * Quaternion.Euler(Mathf.Sin(t * rockHertz * Mathf.PI * 2f) * rockDegrees, 0f, 0f);
        else if (_pose == Pose.Lie) pos += Vector3.up * (Mathf.Sin(t * 1.1f) * 0.006f);
        body.SetPositionAndRotation(pos, rot);
    }

    /// <summary>Testing only: jumps Tomas knock cycle to `seconds` (the arm is raised from about 0.6 s to 2.5 s into each 9 s cycle).</summary>
    public void DebugKnockAt(float seconds) { _clock = seconds; }

    // ---------------------------------------------------------------- knocking

    private int _forcedKnocks;
    private float _forcedUntil;
    private int Knocks => Time.time < _forcedUntil ? _forcedKnocks : HubState.Floor >= 4 ? 3 : 2;

    /// <summary>The flood set piece: Tomas starts a knock cycle now with `count` knocks (the next 9 s).</summary>
    public void KnockNow(int count)
    {
        _forcedKnocks = count;
        _forcedUntil = Time.time + 9f;
        _clock = 0.35f;
        _lastPulse = -1;
    }
    private const float KnockStart = 1.2f, KnockGap = 0.5f, KnockLen = 0.2f;

    private void TickKnock()
    {
        _clock += Time.deltaTime;
        float period = Mathf.Max(4f, knockEverySeconds);
        float cycle = _clock % period;
        int pulse = -1;
        for (int i = 0; i < Knocks; i++)
        {
            if (cycle >= KnockStart + i * KnockGap && cycle < KnockStart + i * KnockGap + KnockLen) pulse = i;
        }
        // one sound per pulse, once
        int key = pulse >= 0 ? (int)(_clock / period) * 10 + pulse : -1;
        if (pulse >= 0 && key != _lastPulse)
        {
            _lastPulse = key;
            if (_audio != null && _knock != null) { DoorAudio.Note("Tomas knock"); _audio.PlayOneShot(_knock, knockVolume); }
        }
    }

    private float ArmAngle()
    {
        if (_pose != Pose.Knock) return 0f;
        float cycle = _clock % Mathf.Max(4f, knockEverySeconds);
        float end = KnockStart + (Knocks - 1) * KnockGap + KnockLen;
        float raise = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(KnockStart - 0.6f, KnockStart - 0.1f, cycle)) *
                      (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(end, end + 0.5f, cycle)));
        float tap = 0f;
        for (int i = 0; i < Knocks; i++)
        {
            float local = cycle - (KnockStart + i * KnockGap);
            if (local >= 0f && local < KnockLen) tap = Mathf.Max(tap, Mathf.Sin(local / KnockLen * Mathf.PI));
        }
        return raise * (78f - tap * 14f);
    }

    /// <summary>
    /// The Laying Idle clip lies the body on its back wherever its root is: turn it so the head points along the anchor's back, and seat
    /// its renderer bounds on the mattress (centre over the mattress centre, underside on the surface). Runs for a few frames after a
    /// refresh, until the skinned bounds follow the animated pose.
    /// </summary>
    private void SeatLying()
    {
        if (_animator == null || !_animator.isHuman || _anchorUsed == null) return;
        if (_skins == null) { _skins = body.GetComponentsInChildren<SkinnedMeshRenderer>(true); foreach (var r in _skins) r.updateWhenOffscreen = true; }
        Transform hips = _animator.GetBoneTransform(HumanBodyBones.Hips);
        if (hips == null || _head == null) return;
        Vector3 along = Vector3.ProjectOnPlane(_head.position - hips.position, Vector3.up);
        Vector3 desired = -_anchorUsed.forward;
        if (along.sqrMagnitude > 0.0004f)
        {
            float delta = Vector3.SignedAngle(along, desired, Vector3.up);
            if (Mathf.Abs(delta) > 1f) { _baseRot = Quaternion.AngleAxis(delta, Vector3.up) * _baseRot; body.rotation = _baseRot; }
        }
        Bounds b = default; bool any = false;
        foreach (var r in _skins) { if (!r.enabled) continue; if (!any) { b = r.bounds; any = true; } else b.Encapsulate(r.bounds); }
        if (!any) return;
        Vector3 centre = _anchorUsed.position - _anchorUsed.forward * 0.95f;
        float surface = _anchorUsed.position.y - 0.13f + 0.03f + lieHeight;
        _basePos += new Vector3(centre.x - b.center.x, surface - b.min.y, centre.z - b.center.z);
    }

    // ---------------------------------------------------------------- LateUpdate: offsets on top of the animated pose

    private void LateUpdate()
    {
        if (!_present || body == null) return;
        ApplyRoot(Time.time);
        if (_seatFrames > 0) { SeatLying(); _seatFrames--; ApplyRoot(Time.time); }

        if (_rightArm != null && _pose == Pose.Knock)
        {
            float a = ArmAngle();
            if (a > 0.01f) _rightArm.rotation = Quaternion.AngleAxis(-a, body.right) * _rightArm.rotation;
        }

        if (_head != null)
        {
            float target = 0f;
            AsylumHub hub = AsylumHub.Active;
            Camera cam = hub != null ? hub.PlayerCamera : null;
            if (cam != null)
            {
                Vector3 axis = body.up;
                Vector3 forward = Vector3.ProjectOnPlane(body.forward, axis);
                Vector3 to = Vector3.ProjectOnPlane(cam.transform.position - _head.position, axis);
                if (to.magnitude < headRange && to.sqrMagnitude > 0.0001f && forward.sqrMagnitude > 0.0001f)
                {
                    float yaw = Vector3.SignedAngle(forward, to, axis);
                    if (Mathf.Abs(yaw) <= 110f) target = Mathf.Clamp(yaw, -headMaxYaw, headMaxYaw);
                }
            }
            _headYaw = Mathf.MoveTowardsAngle(_headYaw, target, 120f * Time.deltaTime);
            if (Mathf.Abs(_headYaw) > 0.01f) _head.rotation = Quaternion.AngleAxis(_headYaw, body.up) * _head.rotation;
        }
    }
}
