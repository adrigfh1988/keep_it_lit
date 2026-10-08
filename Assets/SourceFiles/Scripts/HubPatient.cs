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

    [Header("Tuning")]
    public float headRange = 5f;
    public float headMaxYaw = 70f;
    public float rockDegrees = 4f;
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
                _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                if (_animator.isHuman)
                {
                    _head = _animator.GetBoneTransform(HumanBodyBones.Head);
                    _rightArm = _animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
                }
            }
        }
        _drawerClosed = new Vector3[drawerParts != null ? drawerParts.Length : 0];
        for (int i = 0; i < _drawerClosed.Length; i++) _drawerClosed[i] = drawerParts[i] != null ? drawerParts[i].position : Vector3.zero;
        if (patient == HubState.Patient.Tomas)
        {
            _audio = gameObject.AddComponent<AudioSource>();
            _audio.playOnAwake = false;
            _audio.spatialBlend = 1f;
            _audio.rolloffMode = AudioRolloffMode.Linear;
            _audio.minDistance = 2f;
            _audio.maxDistance = 26f;
            _knock = DoorAudio.BuildClunkClip(0.14f, 150f, 0.55f);
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
        if (finale)
        {
            anchor = finaleAnchor != null ? finaleAnchor : defaultAnchor;
        }
        else if (patient == HubState.Patient.Hilde) _pose = Pose.Rock;
        else if (patient == HubState.Patient.Tomas) _pose = Pose.Knock;
        else if (patient == HubState.Patient.Elias)
        {
            if (floor <= 2) { anchor = bedAnchor != null ? bedAnchor : defaultAnchor; _pose = Pose.Lie; }
            else anchor = standAnchor != null ? standAnchor : defaultAnchor;
        }
        if (anchor == null) return;

        _basePos = anchor.position;
        _baseRot = anchor.rotation;
        // Lying: the model's up becomes the bed's horizontal, face up, head toward the anchor's back.
        if (_pose == Pose.Lie) _baseRot = anchor.rotation * Quaternion.Euler(-90f, 0f, 0f);
        ApplyRoot(0f);
        _lastPulse = -1;
        _clock = 0f;
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

    private int Knocks => HubState.Floor >= 4 ? 3 : 2;
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
            if (_audio != null && _knock != null) _audio.PlayOneShot(_knock, knockVolume);
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

    // ---------------------------------------------------------------- LateUpdate: offsets on top of the animated pose

    private void LateUpdate()
    {
        if (!_present || body == null) return;
        ApplyRoot(Time.time);

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
