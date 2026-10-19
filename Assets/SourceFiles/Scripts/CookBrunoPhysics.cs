using UnityEngine;

/// <summary>
/// Secondary spring-damper physics (chef's toque <c>Hat_Pivot</c>, belt-hung soup ladle <c>Prop_Ladle_L</c>, right-hand cleaver
/// <c>Prop_Cleaver_R</c>) and dialogue mouth articulation (<c>Mouth_Open</c>, <c>Mouth_Speak_O</c>, <c>Mouth_Wide</c> BlendShapes on
/// <c>CookBruno_Head</c>) for Cook Bruno's Indie Horror 3D model (<c>Assets/SourceFiles/Models/CookBruno/CookBruno_Model.fbx</c>).
/// Runs in <see cref="LateUpdate"/> after <see cref="Animator"/> and <see cref="HubNpc"/> head-turn.
/// </summary>
[DefaultExecutionOrder(20)]
public class CookBrunoPhysics : MonoBehaviour
{
    [Header("Toque & Belt Ladle Secondary Physics")]
    public float hatStiffness = 130f;
    public float hatDamping = 10.5f;
    public float maxHatDegrees = 10f;

    public float ladleStiffness = 95f;
    public float ladleDamping = 7.5f;
    public float maxLadleDegrees = 14f;

    [Header("Speaking Mouth Articulation (BlendShapes + Jaw)")]
    public bool autoSpeakInDialogue = true;
    public bool isSpeaking;
    [Range(0f, 1f)] public float mouthOpen;
    [Range(0f, 1f)] public float mouthSpeakO;
    [Range(0f, 1f)] public float mouthWide;

    private Transform _headBone;
    private Transform _hipsBone;
    private Transform _hatPivot;
    private Transform _ladlePivot;
    private Transform _cleaverPivot;

    private Quaternion _hatBindRot;
    private Quaternion _ladleBindRot;
    private Quaternion _cleaverBindRot;

    private Vector2 _hatAngle;
    private Vector2 _hatVel;
    private Vector2 _ladleAngle;
    private Vector2 _ladleVel;

    private Vector3 _prevHeadPos;
    private Vector3 _prevHeadVel;
    private Quaternion _prevHeadRot;
    private Vector3 _prevHipsPos;

    private SkinnedMeshRenderer _headMesh;
    private int _bsMouthOpen = -1;
    private int _bsMouthSpeakO = -1;
    private int _bsMouthWide = -1;
    private float _smoothedOpen;
    private float _smoothedO;
    private float _smoothedWide;

    private HubNpc _parentNpc;
    private Animator _animator;
    private AudioSource _voiceSource;
    private bool _initialized;
    private float _seedPhase;

    private void Awake()
    {
        _seedPhase = Mathf.Abs(GetHashCode() % 997) * 0.013f;
        BindBones();
    }

    private void OnEnable()
    {
        if (!_initialized) BindBones();
        ResetPhysicsState();
    }

    public void BindBones()
    {
        _parentNpc = GetComponentInParent<HubNpc>();
        _animator = GetComponent<Animator>();
        _voiceSource = GetComponentInParent<AudioSource>();

        foreach (Transform t in GetComponentsInChildren<Transform>(true))
        {
            string n = t.name;
            if (n == "mixamorig:Head") _headBone = t;
            else if (n == "mixamorig:Hips") _hipsBone = t;
            else if (n == "Hat_Pivot") _hatPivot = t;
            else if (n == "Prop_Ladle_L") _ladlePivot = t;
            else if (n == "Prop_Cleaver_R") _cleaverPivot = t;
        }

        if (_hatPivot != null) _hatBindRot = _hatPivot.localRotation;
        if (_ladlePivot != null) _ladleBindRot = _ladlePivot.localRotation;
        if (_cleaverPivot != null) _cleaverBindRot = _cleaverPivot.localRotation;

        _headMesh = null;
        _bsMouthOpen = _bsMouthSpeakO = _bsMouthWide = -1;
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            if (smr.name == "CookBruno_Head" || smr.sharedMesh.blendShapeCount > 0)
            {
                _headMesh = smr;
                Mesh m = smr.sharedMesh;
                _bsMouthOpen = m.GetBlendShapeIndex("Mouth_Open");
                _bsMouthSpeakO = m.GetBlendShapeIndex("Mouth_Speak_O");
                _bsMouthWide = m.GetBlendShapeIndex("Mouth_Wide");
                break;
            }
        }

        _initialized = true;
        ResetPhysicsState();
    }

    private void ResetPhysicsState()
    {
        if (_headBone != null)
        {
            _prevHeadPos = _headBone.position;
            _prevHeadRot = _headBone.rotation;
            _prevHeadVel = Vector3.zero;
        }
        if (_hipsBone != null) _prevHipsPos = _hipsBone.position;
        _hatAngle = Vector2.zero;
        _hatVel = Vector2.zero;
        _ladleAngle = Vector2.zero;
        _ladleVel = Vector2.zero;
        _smoothedOpen = 0f;
        _smoothedO = 0f;
        _smoothedWide = 0f;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.0001f) return;
        dt = Mathf.Min(dt, 0.05f);
        float t = Time.time + _seedPhase;

        if (_headBone != null && _hatPivot != null)
        {
            Vector3 headPos = _headBone.position;
            Quaternion headRot = _headBone.rotation;
            Vector3 vel = (headPos - _prevHeadPos) / dt;
            Vector3 accel = (vel - _prevHeadVel) / dt;
            _prevHeadPos = headPos;
            _prevHeadVel = vel;

            Quaternion deltaRot = headRot * Quaternion.Inverse(_prevHeadRot);
            deltaRot.ToAngleAxis(out float angDeg, out Vector3 angAxis);
            if (angDeg > 180f) angDeg -= 360f;
            Vector3 localAngVel = _headBone.InverseTransformDirection(angAxis * (angDeg / dt));
            _prevHeadRot = headRot;

            Vector3 localAccel = _headBone.InverseTransformDirection(accel);
            float breathPitch = Mathf.Sin(t * 2.1f) * 0.7f;
            float breathRoll = Mathf.Cos(t * 1.7f) * 0.5f;

            Vector2 hatImpulse = new Vector2(
                Mathf.Clamp(-localAccel.z * 1.8f - localAngVel.x * 0.14f + breathPitch * 10f, -120f, 120f),
                Mathf.Clamp(-localAccel.x * 1.8f - localAngVel.z * 0.14f + breathRoll * 10f, -120f, 120f)
            );
            _hatVel += (hatImpulse - _hatAngle * hatStiffness - _hatVel * hatDamping) * dt;
            _hatAngle += _hatVel * dt;
            _hatAngle.x = Mathf.Clamp(_hatAngle.x, -maxHatDegrees, maxHatDegrees);
            _hatAngle.y = Mathf.Clamp(_hatAngle.y, -maxHatDegrees, maxHatDegrees);
            _hatPivot.localRotation = _hatBindRot * Quaternion.Euler(_hatAngle.x, 0f, _hatAngle.y);
        }

        if (_hipsBone != null && _ladlePivot != null)
        {
            Vector3 hipsVel = (_hipsBone.position - _prevHipsPos) / dt;
            _prevHipsPos = _hipsBone.position;
            Vector3 localHipsVel = _hipsBone.InverseTransformDirection(hipsVel);
            Vector2 ladleForce = new Vector2(
                Mathf.Clamp(-localHipsVel.z * 20f + Mathf.Sin(t * 2.4f) * 5f, -80f, 80f),
                Mathf.Clamp(-localHipsVel.x * 16f + Mathf.Cos(t * 1.9f) * 4f, -80f, 80f)
            );
            _ladleVel += (ladleForce - _ladleAngle * ladleStiffness - _ladleVel * ladleDamping) * dt;
            _ladleAngle += _ladleVel * dt;
            _ladleAngle.x = Mathf.Clamp(_ladleAngle.x, -maxLadleDegrees, maxLadleDegrees);
            _ladleAngle.y = Mathf.Clamp(_ladleAngle.y, -maxLadleDegrees, maxLadleDegrees);
            _ladlePivot.localRotation = _ladleBindRot * Quaternion.Euler(_ladleAngle.x, 0f, _ladleAngle.y);
        }

        if (_cleaverPivot != null)
        {
            float gripTremor = Mathf.Sin(t * 5.2f) * 1.1f + Mathf.Cos(t * 11.3f) * 0.45f;
            _cleaverPivot.localRotation = _cleaverBindRot * Quaternion.Euler(gripTremor, 0f, 0f);
        }

        ApplyMouthArticulation(t, dt);
    }

    private void ApplyMouthArticulation(float t, float dt)
    {
        bool dialogueTalking = false;
        if (autoSpeakInDialogue)
        {
            if (_parentNpc != null && _parentNpc.IsTalking) dialogueTalking = true;
            else if (_voiceSource != null && _voiceSource.isPlaying) dialogueTalking = true;
            else if (_animator != null && _animator.isActiveAndEnabled && _animator.runtimeAnimatorController != null)
            {
                AnimatorStateInfo st = _animator.GetCurrentAnimatorStateInfo(0);
                if (st.IsName("Talking") || st.IsName("Talk")) dialogueTalking = true;
            }
        }

        float targetOpen = Mathf.Clamp01(mouthOpen);
        float targetO = Mathf.Clamp01(mouthSpeakO);
        float targetWide = Mathf.Clamp01(mouthWide);

        if (isSpeaking || dialogueTalking)
        {
            float phraseGate = Mathf.Clamp01(0.65f + 0.55f * Mathf.Sin(t * 2.1f) + 0.35f * Mathf.Cos(t * 3.4f));
            float syllableA = Mathf.Sin(t * 12.4f);
            float syllableB = Mathf.Sin(t * 19.6f + 0.7f);
            float syllableEnv = Mathf.Clamp01((syllableA * 0.6f + syllableB * 0.4f) * 0.65f + 0.45f) * phraseGate;

            targetOpen = Mathf.Max(targetOpen, syllableEnv * 0.85f);
            targetO = Mathf.Max(targetO, Mathf.Clamp01(Mathf.Sin(t * 7.2f + 1.1f) * 0.55f + 0.15f) * syllableEnv * 0.55f);
            targetWide = Mathf.Max(targetWide, Mathf.Clamp01(Mathf.Cos(t * 5.0f) * 0.5f + 0.25f) * 0.35f);
        }

        float lerpRate = 1f - Mathf.Exp(-20f * dt);
        _smoothedOpen = Mathf.Lerp(_smoothedOpen, targetOpen, lerpRate);
        _smoothedO = Mathf.Lerp(_smoothedO, targetO, lerpRate);
        _smoothedWide = Mathf.Lerp(_smoothedWide, targetWide, lerpRate);

        if (_headMesh != null)
        {
            if (_bsMouthOpen >= 0) _headMesh.SetBlendShapeWeight(_bsMouthOpen, _smoothedOpen * 100f);
            if (_bsMouthSpeakO >= 0) _headMesh.SetBlendShapeWeight(_bsMouthSpeakO, _smoothedO * 100f);
            if (_bsMouthWide >= 0) _headMesh.SetBlendShapeWeight(_bsMouthWide, _smoothedWide * 100f);
        }
    }
}
