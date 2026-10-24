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

    [Header("Butcher Cleaver Chopping Articulation (Dark Bruno)")]
    public bool isChopping;
    [Range(0f, 1f)] public float chopPhase;
    [Range(0.5f, 2f)] public float chopIntensity = 1f;

    private Transform _headBone;
    private Transform _hipsBone;
    private Transform _spineBone;
    private Transform _rightArmBone;
    private Transform _rightForeArmBone;
    private Transform _rightHandBone;
    private Transform _leftArmBone;
    private Transform _leftForeArmBone;
    private Transform _hatPivot;
    private Transform _ladlePivot;
    private Transform _cleaverPivot;

    private Quaternion _hatBindRot;
    private Quaternion _ladleBindRot;
    private Quaternion _cleaverBindRot;
    private Quaternion _rightHandBindRot;
    private float _smoothedChop;

    // Sampled humanoid hammer-grip arm poses from Rush/Idle so the right forearm and gloved hand stay in one natural chopping plane
    private static readonly Quaternion RushArmLocal = Quaternion.Euler(328.1f, 307.7f, 35.2f);
    private static readonly Quaternion RushForeLocal = Quaternion.Euler(303.6f, 137.3f, 182.5f);
    private static readonly Quaternion IdleForeLocal = Quaternion.Euler(324.9f, 347.8f, 348.3f);
    // Flat normal of CookBruno_Cleaver_Blade in Prop_Cleaver_R local space (rotates the blade forward/down in its own cutting plane)
    private static readonly Vector3 BladeFlatNormalLocal = new Vector3(0.7880f, -0.4800f, -0.3860f);

    public void SetSpeaking(bool speaking) => isSpeaking = speaking;

    public void SetMouthOpen(float open, float speakO = 0f, float wide = 0f)
    {
        mouthOpen = Mathf.Clamp01(open);
        mouthSpeakO = Mathf.Clamp01(speakO);
        mouthWide = Mathf.Clamp01(wide);
    }

    public void SetChopping(bool chopping, float normalizedPhase = 0f, float intensity = 1f)
    {
        isChopping = chopping;
        chopPhase = Mathf.Repeat(normalizedPhase, 1f);
        chopIntensity = Mathf.Clamp(intensity, 0.5f, 2f);
        if (chopping && intensity > 1.1f) _smoothedChop = 1f;
    }

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
            else if (n == "mixamorig:Spine2" || (n == "mixamorig:Spine1" && _spineBone == null)) _spineBone = t;
            else if (n == "mixamorig:RightArm") _rightArmBone = t;
            else if (n == "mixamorig:RightForeArm") _rightForeArmBone = t;
            else if (n == "mixamorig:RightHand") _rightHandBone = t;
            else if (n == "mixamorig:LeftArm") _leftArmBone = t;
            else if (n == "mixamorig:LeftForeArm") _leftForeArmBone = t;
            else if (n == "Hat_Pivot") _hatPivot = t;
            else if (n == "Prop_Ladle_L") _ladlePivot = t;
            else if (n == "Prop_Cleaver_R") _cleaverPivot = t;
        }

        if (_hatPivot != null) _hatBindRot = _hatPivot.localRotation;
        if (_ladlePivot != null) _ladleBindRot = _ladlePivot.localRotation;
        if (_cleaverPivot != null) _cleaverBindRot = _cleaverPivot.localRotation;
        if (_rightHandBone != null) _rightHandBindRot = _rightHandBone.localRotation;

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

        // Lock RightHand wrist to its bind pose relative to RightForeArm: CookBruno_Body's right glove vertices are weighted
        // 45% to RightForeArm and 51% to RightHand, whereas CookBruno_Cleaver_* is weighted 100% to Prop_Cleaver_R (child of RightHand).
        // Keeping RightHand locked to _rightHandBindRot makes the forearm, glove, and cleaver move as one rigid unit in all animations.
        if (_rightHandBone != null)
        {
            _rightHandBone.localRotation = _rightHandBindRot;
        }

        Vector3 bladeAxis = BladeFlatNormalLocal.normalized;
        _smoothedChop = Mathf.MoveTowards(_smoothedChop, isChopping ? 1f : 0f, dt * 8f);
        if (_smoothedChop > 0.001f)
        {
            // Asymmetric butcher hack curve: slow wind-up (0..0.58), violent downward strike (0.58..0.74), brief embed hold (0.74..1.0)
            float p = chopPhase;
            float stroke;
            if (p < 0.58f)
            {
                float u = p / 0.58f;
                stroke = -Mathf.Sin(u * Mathf.PI * 0.5f); // -1 = raised high above shoulder/head
            }
            else if (p < 0.74f)
            {
                float u = (p - 0.58f) / 0.16f;
                stroke = Mathf.Lerp(-1f, 1f, u * u); // +1 = slammed down onto the butcher block
            }
            else
            {
                float u = (p - 0.74f) / 0.26f;
                stroke = Mathf.Lerp(1f, 0f, u);
            }

            float w = _smoothedChop;
            float strikeFrac = (stroke + 1f) * 0.5f; // 0 = wind-up (raised high), 1 = downward strike onto block
            Vector3 charRight = transform.right;
            Vector3 charUp = transform.up;

            if (_spineBone != null)
            {
                float spinePitch = Mathf.Lerp(-6f, 16f, strikeFrac) * w;
                float spineYaw = Mathf.Lerp(6f, -10f, strikeFrac) * w;
                _spineBone.rotation = Quaternion.AngleAxis(spinePitch, charRight)
                                      * Quaternion.AngleAxis(spineYaw, charUp)
                                      * _spineBone.rotation;
            }

            // Upper arm starts from the natural hammer-grip plane (RushArmLocal) and swings down/inward onto the block
            if (_rightArmBone != null)
            {
                _rightArmBone.localRotation = Quaternion.Slerp(_rightArmBone.localRotation, RushArmLocal, w);
                float armWorldPitch = Mathf.Lerp(-26f, 12f, strikeFrac) * w;
                float armWorldYaw = Mathf.Lerp(-10f, -22f, strikeFrac) * w;
                _rightArmBone.rotation = Quaternion.AngleAxis(armWorldPitch, charRight)
                                         * Quaternion.AngleAxis(armWorldYaw, charUp)
                                         * _rightArmBone.rotation;
            }

            // Forearm unbends from the raised hammer-grip pose (RushForeLocal) toward IdleForeLocal at impact
            if (_rightForeArmBone != null)
            {
                Quaternion targetFore = Quaternion.Slerp(RushForeLocal, IdleForeLocal, strikeFrac * 0.62f);
                _rightForeArmBone.localRotation = Quaternion.Slerp(_rightForeArmBone.localRotation, targetFore, w);
            }

            // During aggressive roar/execution (chopIntensity > 1.1), left gloved hand reaches forward to grab/hold the player
            float grabWeight = w * Mathf.Clamp01((chopIntensity - 1f) * 2.2f);
            if (grabWeight > 0.01f)
            {
                if (_leftArmBone != null)
                {
                    _leftArmBone.rotation = Quaternion.AngleAxis(Mathf.Lerp(-24f, -16f, strikeFrac) * grabWeight, charRight)
                                            * Quaternion.AngleAxis(-8f * grabWeight, charUp)
                                            * _leftArmBone.rotation;
                }
                if (_leftForeArmBone != null)
                {
                    _leftForeArmBone.rotation = Quaternion.AngleAxis(Mathf.Lerp(-38f, -26f, strikeFrac) * grabWeight, charRight)
                                                * _leftForeArmBone.rotation;
                }
                if (_headBone != null)
                {
                    _headBone.rotation = Quaternion.AngleAxis(Mathf.Lerp(6f, -10f, strikeFrac) * grabWeight, charRight)
                                         * _headBone.rotation;
                }
            }

            if (_cleaverPivot != null)
            {
                float gripTremor = Mathf.Sin(t * 5.2f) * 0.8f + Mathf.Cos(t * 11.3f) * 0.35f;
                float inPlaneAngle = Mathf.Lerp(18f, Mathf.Lerp(28f, 68f, strikeFrac), w) + gripTremor;
                _cleaverPivot.localRotation = _cleaverBindRot * Quaternion.AngleAxis(inPlaneAngle, bladeAxis);
            }
        }
        else if (_cleaverPivot != null)
        {
            float gripTremor = Mathf.Sin(t * 5.2f) * 0.6f + Mathf.Cos(t * 11.3f) * 0.25f;
            _cleaverPivot.localRotation = _cleaverBindRot * Quaternion.AngleAxis(18f + gripTremor, bladeAxis);
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
            float roarBoost = chopIntensity > 1.1f ? 2.1f : 1.0f;
            if (_bsMouthOpen >= 0) _headMesh.SetBlendShapeWeight(_bsMouthOpen, _smoothedOpen * 100f * roarBoost);
            if (_bsMouthSpeakO >= 0) _headMesh.SetBlendShapeWeight(_bsMouthSpeakO, _smoothedO * 100f * roarBoost);
            if (_bsMouthWide >= 0) _headMesh.SetBlendShapeWeight(_bsMouthWide, _smoothedWide * 100f * roarBoost);
        }
    }
}
