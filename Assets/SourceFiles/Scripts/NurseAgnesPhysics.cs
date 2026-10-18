using UnityEngine;

/// <summary>
/// Secondary spring-damper physics and finger articulation driver for Sister Agnes's multi-part 3D model
/// (<c>Assets/SourceFiles/Models/NurseAgnes/NurseAgnes_Model.fbx</c>).
/// Runs in <see cref="LateUpdate"/> after the <see cref="Animator"/> and <see cref="HubNpc"/> head-turn so the
/// separate starched nurse cap (<c>Hat_Pivot</c>) reacts to head movement/acceleration and physically pushes and drives
/// the separate hair mesh bones (<c>Hair_Front_L/R</c>, <c>Hair_Side_L/R</c>, <c>Hair_Back_L/R</c>, <c>Hair_Bun</c>),
/// while also driving the rear apron bow tails (<c>Apron_Tail_L/R</c>) and subtle unhinged finger twitches.
/// </summary>
[DefaultExecutionOrder(20)]
public class NurseAgnesPhysics : MonoBehaviour
{
    [Header("Hat -> Hair Coupled Physics")]
    [Tooltip("Spring stiffness of the crooked starched nurse cap (Hat_Pivot).")]
    public float hatStiffness = 140f;
    [Tooltip("Damping of the nurse cap spring.")]
    public float hatDamping = 11f;
    [Tooltip("How strongly the nurse cap's angle and velocity push and move the underlying hair strands.")]
    public float hatToHairCoupling = 1.35f;
    [Tooltip("Spring stiffness of the loose hair locks and rear bun.")]
    public float hairStiffness = 95f;
    [Tooltip("Damping of the hair locks.")]
    public float hairDamping = 8.5f;
    [Tooltip("Maximum angular deflection (degrees) for hat and hair bones.")]
    public float maxAngleDegrees = 18f;

    [Header("Finger Articulation & Twitch")]
    [Tooltip("Subtle involuntary manic finger twitching on top of the current Animator pose.")]
    public bool idleFingerTwitch = true;
    [Tooltip("Overall 0..1 curl override for the left hand fingers (0 = Animator pose, 1 = full claw grip).")]
    [Range(0f, 1f)] public float leftHandCurl;
    [Tooltip("Overall 0..1 curl override for the right hand fingers (0 = Animator pose, 1 = full claw grip).")]
    [Range(0f, 1f)] public float rightHandCurl;

    [Header("Speaking Mouth Articulation (BlendShapes + Jaw Bone)")]
    [Tooltip("Automatically move her mouth when she is speaking in Hub dialogue or playing a voice AudioSource.")]
    public bool autoSpeakInDialogue = true;
    [Tooltip("Set true from code or Inspector to make her speak continuously with syllable cadence.")]
    public bool isSpeaking;
    [Tooltip("Direct 0..1 mouth open override (drives both the Mouth_Open BlendShape on NurseAgnes_Head and the mixamorig:Jaw bone).")]
    [Range(0f, 1f)] public float mouthOpen;
    [Tooltip("Direct 0..1 rounded 'O' vowel shape weight (Mouth_Speak_O BlendShape).")]
    [Range(0f, 1f)] public float mouthSpeakO;
    [Tooltip("Direct 0..1 extra-wide manic grin stretch (Mouth_Wide BlendShape).")]
    [Range(0f, 1f)] public float mouthWide;
    [Tooltip("Direct 0..1 lip closure weight (Mouth_Close BlendShape).")]
    [Range(0f, 1f)] public float mouthClose;
    [Tooltip("Maximum downward jaw bone rotation in degrees when mouthOpen = 1.")]
    public float maxJawOpenDegrees = 14f;

    private Transform _headBone;
    private Transform _jawBone;
    private Transform _hipsBone;
    private Transform _hatPivot;
    private Transform[] _hairBones;
    private Quaternion[] _hairBindRot;
    private Vector3[] _hairSideSign; // x = -1 (right) or +1 (left), z = -1 (front) or +1 (back)
    private Vector2[] _hairAngle;
    private Vector2[] _hairVel;

    private Transform[] _apronTails;
    private Quaternion[] _apronBindRot;
    private Vector2[] _apronAngle;
    private Vector2[] _apronVel;

    private Transform[] _leftFingerBones;
    private Transform[] _rightFingerBones;

    private SkinnedMeshRenderer _headMesh;
    private int _bsMouthOpen = -1;
    private int _bsMouthSpeakO = -1;
    private int _bsMouthWide = -1;
    private int _bsMouthClose = -1;
    private Quaternion _jawBindRot;
    private float _smoothedMouthOpen;
    private float _smoothedMouthO;
    private float _smoothedMouthWide;
    private float _smoothedMouthClose;

    private HubNpc _parentNpc;
    private Animator _animator;
    private AudioSource _voiceSource;

    private Quaternion _hatBindRot;
    private Vector2 _hatAngle;
    private Vector2 _hatVel;

    private Vector3 _prevHeadPos;
    private Vector3 _prevHeadVel;
    private Quaternion _prevHeadRot;
    private Vector3 _prevHipsPos;
    private bool _initialized;
    private float _seedPhase;

    public void SetFingerCurl(float leftCurl, float rightCurl)
    {
        leftHandCurl = Mathf.Clamp01(leftCurl);
        rightHandCurl = Mathf.Clamp01(rightCurl);
    }

    /// <summary>Starts or stops procedural speaking mouth articulation from code.</summary>
    public void SetSpeaking(bool speaking)
    {
        isSpeaking = speaking;
    }

    /// <summary>Directly sets Nurse Agnes's mouth opening and facial expression weights (0..1) from code.</summary>
    public void SetMouthOpen(float open01, float wide01 = 0f, float roundO01 = 0f, float close01 = 0f)
    {
        if (!_initialized || _headMesh == null) BindBones();
        mouthOpen = Mathf.Clamp01(open01);
        mouthWide = Mathf.Clamp01(wide01);
        mouthSpeakO = Mathf.Clamp01(roundO01);
        mouthClose = Mathf.Clamp01(close01);
        _smoothedMouthOpen = mouthOpen;
        _smoothedMouthWide = mouthWide;
        _smoothedMouthO = mouthSpeakO;
        _smoothedMouthClose = mouthClose;
        ApplyCurrentMouthWeights();
    }

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

        Transform[] all = GetComponentsInChildren<Transform>(true);
        System.Collections.Generic.List<Transform> hairList = new System.Collections.Generic.List<Transform>();
        System.Collections.Generic.List<Transform> apronList = new System.Collections.Generic.List<Transform>();
        System.Collections.Generic.List<Transform> lFingers = new System.Collections.Generic.List<Transform>();
        System.Collections.Generic.List<Transform> rFingers = new System.Collections.Generic.List<Transform>();

        foreach (Transform t in all)
        {
            string n = t.name;
            if (n == "mixamorig:Head") _headBone = t;
            else if (n == "Speech_Mandible" || n == "mixamorig:Jaw") _jawBone = t;
            else if (n == "mixamorig:Hips") _hipsBone = t;
            else if (n == "Hat_Pivot") _hatPivot = t;
            else if (n.StartsWith("Hair_")) hairList.Add(t);
            else if (n.StartsWith("Apron_Tail_")) apronList.Add(t);
            else if (n.StartsWith("mixamorig:LeftHand") && n.Length > "mixamorig:LeftHand".Length) lFingers.Add(t);
            else if (n.StartsWith("mixamorig:RightHand") && n.Length > "mixamorig:RightHand".Length) rFingers.Add(t);
        }

        if (_hatPivot != null) _hatBindRot = _hatPivot.localRotation;
        if (_jawBone != null) _jawBindRot = _jawBone.localRotation;

        _headMesh = null;
        _bsMouthOpen = _bsMouthSpeakO = _bsMouthWide = _bsMouthClose = -1;
        foreach (SkinnedMeshRenderer smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (smr.sharedMesh == null) continue;
            if (smr.name == "NurseAgnes_Head" || smr.sharedMesh.blendShapeCount > 0)
            {
                _headMesh = smr;
                Mesh m = smr.sharedMesh;
                _bsMouthOpen = m.GetBlendShapeIndex("Mouth_Open");
                _bsMouthSpeakO = m.GetBlendShapeIndex("Mouth_Speak_O");
                _bsMouthWide = m.GetBlendShapeIndex("Mouth_Wide");
                _bsMouthClose = m.GetBlendShapeIndex("Mouth_Close");
                break;
            }
        }

        _hairBones = hairList.ToArray();
        _hairBindRot = new Quaternion[_hairBones.Length];
        _hairSideSign = new Vector3[_hairBones.Length];
        _hairAngle = new Vector2[_hairBones.Length];
        _hairVel = new Vector2[_hairBones.Length];

        for (int i = 0; i < _hairBones.Length; i++)
        {
            Transform hb = _hairBones[i];
            _hairBindRot[i] = hb.localRotation;
            float sx = hb.name.EndsWith("_L") ? 1f : (hb.name.EndsWith("_R") ? -1f : 0f);
            float sz = hb.name.Contains("Front") ? -1f : (hb.name.Contains("Back") || hb.name.Contains("Bun") ? 1f : 0f);
            _hairSideSign[i] = new Vector3(sx, 0f, sz);
        }

        _apronTails = apronList.ToArray();
        _apronBindRot = new Quaternion[_apronTails.Length];
        _apronAngle = new Vector2[_apronTails.Length];
        _apronVel = new Vector2[_apronTails.Length];
        for (int i = 0; i < _apronTails.Length; i++)
        {
            _apronBindRot[i] = _apronTails[i].localRotation;
        }

        _leftFingerBones = lFingers.ToArray();
        _rightFingerBones = rFingers.ToArray();
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
        if (_hairAngle != null)
        {
            for (int i = 0; i < _hairAngle.Length; i++)
            {
                _hairAngle[i] = Vector2.zero;
                _hairVel[i] = Vector2.zero;
            }
        }
        if (_apronAngle != null)
        {
            for (int i = 0; i < _apronAngle.Length; i++)
            {
                _apronAngle[i] = Vector2.zero;
                _apronVel[i] = Vector2.zero;
            }
        }
        _smoothedMouthOpen = 0f;
        _smoothedMouthO = 0f;
        _smoothedMouthWide = 0f;
        _smoothedMouthClose = 0f;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.0001f) return;
        dt = Mathf.Min(dt, 0.05f);

        float t = Time.time + _seedPhase;

        if (_headBone != null)
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

            // Subtle nervous head/cap micro-tremor so the hat & hair stay alive even when standing at the Nurse Station
            float tremorPitch = Mathf.Sin(t * 4.3f) * 1.1f + Mathf.Cos(t * 9.7f) * 0.6f;
            float tremorRoll  = Mathf.Cos(t * 3.6f) * 1.3f + Mathf.Sin(t * 8.1f) * 0.7f;

            Vector2 hatImpulse = new Vector2(
                Mathf.Clamp(-localAccel.z * 2.2f - localAngVel.x * 0.18f + tremorPitch * 14f, -180f, 180f),
                Mathf.Clamp(-localAccel.x * 2.2f - localAngVel.z * 0.18f + tremorRoll * 14f,  -180f, 180f)
            );

            // 1. Update Hat_Pivot spring-damper
            _hatVel += (hatImpulse - _hatAngle * hatStiffness - _hatVel * hatDamping) * dt;
            _hatAngle += _hatVel * dt;
            _hatAngle.x = Mathf.Clamp(_hatAngle.x, -maxAngleDegrees, maxAngleDegrees);
            _hatAngle.y = Mathf.Clamp(_hatAngle.y, -maxAngleDegrees, maxAngleDegrees);

            if (_hatPivot != null)
            {
                _hatPivot.localRotation = _hatBindRot * Quaternion.Euler(_hatAngle.x, 0f, _hatAngle.y);
            }

            // 2. Update Hair bones — driven by BOTH head motion AND direct physical push from the Hat!
            if (_hairBones != null)
            {
                for (int i = 0; i < _hairBones.Length; i++)
                {
                    Transform hb = _hairBones[i];
                    if (hb == null) continue;

                    Vector3 sign = _hairSideSign[i];
                    // When the hat tilts down on one side (e.g., roll > 0 on left side), it compresses and pushes the hair locks outward!
                    float hatSideCompression = Mathf.Max(0f, _hatAngle.y * sign.x) * 0.65f;
                    float hatFrontBackPush   = _hatAngle.x * (0.85f + 0.25f * sign.z);

                    Vector2 hatDrive = new Vector2(
                        (hatFrontBackPush * hatStiffness + _hatVel.x * 2.4f) * hatToHairCoupling,
                        ((_hatAngle.y + hatSideCompression * sign.x) * hatStiffness + _hatVel.y * 2.4f) * hatToHairCoupling
                    );

                    float strandPhase = i * 1.15f;
                    Vector2 strandWave = new Vector2(
                        Mathf.Sin(t * 3.1f + strandPhase) * 8f,
                        Mathf.Cos(t * 2.7f + strandPhase) * 8f
                    );

                    Vector2 totalForce = hatDrive + strandWave - _hairAngle[i] * hairStiffness - _hairVel[i] * hairDamping;
                    _hairVel[i] += totalForce * dt;
                    _hairAngle[i] += _hairVel[i] * dt;
                    _hairAngle[i].x = Mathf.Clamp(_hairAngle[i].x, -maxAngleDegrees * 1.25f, maxAngleDegrees * 1.25f);
                    _hairAngle[i].y = Mathf.Clamp(_hairAngle[i].y, -maxAngleDegrees * 1.25f, maxAngleDegrees * 1.25f);

                    hb.localRotation = _hairBindRot[i] * Quaternion.Euler(_hairAngle[i].x, 0f, _hairAngle[i].y);
                }
            }
        }

        // 3. Update Apron Rear Bow Tails
        if (_hipsBone != null && _apronTails != null && _apronTails.Length > 0)
        {
            Vector3 hipsVel = (_hipsBone.position - _prevHipsPos) / dt;
            _prevHipsPos = _hipsBone.position;
            Vector3 localHipsVel = _hipsBone.InverseTransformDirection(hipsVel);

            for (int i = 0; i < _apronTails.Length; i++)
            {
                Transform at = _apronTails[i];
                if (at == null) continue;
                float side = i == 0 ? 1f : -1f;
                Vector2 force = new Vector2(
                    Mathf.Clamp(-localHipsVel.z * 25f + Mathf.Sin(t * 2.8f + i) * 6f, -90f, 90f),
                    Mathf.Clamp(-localHipsVel.x * 18f + side * Mathf.Abs(localHipsVel.z) * 8f, -90f, 90f)
                );
                _apronVel[i] += (force - _apronAngle[i] * 75f - _apronVel[i] * 7.5f) * dt;
                _apronAngle[i] += _apronVel[i] * dt;
                _apronAngle[i].x = Mathf.Clamp(_apronAngle[i].x, -16f, 16f);
                _apronAngle[i].y = Mathf.Clamp(_apronAngle[i].y, -14f, 14f);
                at.localRotation = _apronBindRot[i] * Quaternion.Euler(_apronAngle[i].x, 0f, _apronAngle[i].y);
            }
        }

        // 4. Finger Articulation (Curl override + eerie involuntary finger twitches)
        ApplyFingerArticulation(_leftFingerBones, leftHandCurl, t, 0f);
        ApplyFingerArticulation(_rightFingerBones, rightHandCurl, t, 2.1f);

        // 5. Speaking Mouth Articulation (BlendShapes + mixamorig:Jaw bone)
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
        float targetClose = Mathf.Clamp01(mouthClose);

        if (isSpeaking || dialogueTalking)
        {
            // Natural multi-syllable speech cadence with brief consonant closures and vowel roundings
            float phraseGate = Mathf.Clamp01(0.65f + 0.55f * Mathf.Sin(t * 2.3f) + 0.35f * Mathf.Cos(t * 3.7f));
            float syllableA = Mathf.Sin(t * 13.5f);
            float syllableB = Mathf.Sin(t * 21.2f + 0.8f);
            float syllableEnv = Mathf.Clamp01((syllableA * 0.6f + syllableB * 0.4f) * 0.65f + 0.45f) * phraseGate;

            float procOpen = syllableEnv * 0.88f;
            float procO = Mathf.Clamp01(Mathf.Sin(t * 7.8f + 1.1f) * 0.55f + 0.15f) * syllableEnv * 0.55f;
            float procWide = Mathf.Clamp01(Mathf.Cos(t * 5.4f) * 0.5f + 0.25f) * 0.40f;
            float procClose = (syllableEnv < 0.14f && phraseGate > 0.35f) ? 0.55f : 0f;

            targetOpen = Mathf.Max(targetOpen, procOpen);
            targetO = Mathf.Max(targetO, procO);
            targetWide = Mathf.Max(targetWide, procWide);
            targetClose = Mathf.Max(targetClose, procClose);
        }

        float lerpRate = 1f - Mathf.Exp(-22f * dt);
        _smoothedMouthOpen = Mathf.Lerp(_smoothedMouthOpen, targetOpen, lerpRate);
        _smoothedMouthO = Mathf.Lerp(_smoothedMouthO, targetO, lerpRate);
        _smoothedMouthWide = Mathf.Lerp(_smoothedMouthWide, targetWide, lerpRate);
        _smoothedMouthClose = Mathf.Lerp(_smoothedMouthClose, targetClose, lerpRate);

        ApplyCurrentMouthWeights();
    }

    private void ApplyCurrentMouthWeights()
    {
        bool hasBlendShapes = _headMesh != null && _bsMouthOpen >= 0;
        if (_headMesh != null)
        {
            if (_bsMouthOpen >= 0) _headMesh.SetBlendShapeWeight(_bsMouthOpen, _smoothedMouthOpen * 100f);
            if (_bsMouthSpeakO >= 0) _headMesh.SetBlendShapeWeight(_bsMouthSpeakO, _smoothedMouthO * 100f);
            if (_bsMouthWide >= 0) _headMesh.SetBlendShapeWeight(_bsMouthWide, _smoothedMouthWide * 100f);
            if (_bsMouthClose >= 0) _headMesh.SetBlendShapeWeight(_bsMouthClose, _smoothedMouthClose * 100f);
        }

        if (_jawBone != null && _jawBone.parent != null)
        {
            float jawDeg = (_smoothedMouthOpen + _smoothedMouthO * 0.35f - _smoothedMouthClose * 0.25f) * maxJawOpenDegrees;
            Vector3 worldRight = _headBone != null ? _headBone.right : transform.right;
            Vector3 parentLocalRight = _jawBone.parent.InverseTransformDirection(worldRight);
            // Rotating around +right in Unity's left-handed coordinate system pitches +forward (-Y in Blender) downward (-Y in Unity)!
            _jawBone.localRotation = Quaternion.AngleAxis(jawDeg, parentLocalRight) * _jawBindRot;
        }
    }

    private void ApplyFingerArticulation(Transform[] bones, float curlAmount, float t, float handOffset)
    {
        if (bones == null || bones.Length == 0) return;
        if (!idleFingerTwitch && curlAmount <= 0.001f) return;

        for (int i = 0; i < bones.Length; i++)
        {
            Transform fb = bones[i];
            if (fb == null) continue;
            float twitch = 0f;
            if (idleFingerTwitch)
            {
                float wave = Mathf.Sin(t * 3.8f + i * 1.37f + handOffset) + 0.5f * Mathf.Sin(t * 9.2f + i * 2.9f);
                twitch = Mathf.Max(0f, wave - 0.65f) * 9.5f;
            }
            float curlDeg = curlAmount * 28f + twitch;
            if (Mathf.Abs(curlDeg) > 0.05f)
            {
                fb.localRotation = fb.localRotation * Quaternion.Euler(0f, 0f, curlDeg);
            }
        }
    }
}
