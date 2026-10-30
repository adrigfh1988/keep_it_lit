using UnityEngine;

/// <summary>
/// Procedural secondary bone physics & somatic animation for Patient #3 · Hilde Brandt ("Forty-One Doors"):
///   - Spring-damper pendulum physics for the dangling restraint strap / `#3` metal admission tag (`Prop_RestraintTag_L`)
///     on her left leather wrist cuff.
///   - Subtle counting-whisper mandible articulation (`Speech_Mandible`) and facial BlendShapes (`Mouth_Open`,
///     `Mouth_Speak_O`, `Mouth_Wide`) driven by Hub speech bubbles (`PatientVoice.IsTalking`) or silent counting murmurs.
///   - Gentle chest breathing (`mixamorig:Spine1` / `mixamorig:Spine2`) and obsessive door-counting right index finger
///     tap (`mixamorig:RightHandIndex1` holding her piece of tally chalk).
/// Runs in `LateUpdate` at `[DefaultExecutionOrder(20)]` after `Animator` and `HubPatient.LateUpdate` (`order 0`).
/// </summary>
[DefaultExecutionOrder(20)]
[DisallowMultipleComponent]
public sealed class HildePhysics : MonoBehaviour
{
    [Header("Auto-Discovered Rig Bones")]
    [SerializeField] private Transform hipsBone;
    [SerializeField] private Transform spine1Bone;
    [SerializeField] private Transform spine2Bone;
    [SerializeField] private Transform neckBone;
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform mandibleBone;
    [SerializeField] private Transform restraintTagBone;
    [SerializeField] private Transform rightHandBone;
    [SerializeField] private Transform rightIndex1Bone;
    [SerializeField] private SkinnedMeshRenderer headMesh;

    [Header("Dangling Wrist Restraint / #3 Tag Spring-Damper")]
    [SerializeField] private float tagStiffness = 110f;
    [SerializeField] private float tagDamping = 7.2f;
    [SerializeField] private float tagLinearSensitivity = 45f;
    [SerializeField] private float tagMaxAngle = 34f;

    [Header("Breathing & Counting Twitch")]
    [SerializeField] private float breathRateHz = 0.34f;
    [SerializeField] private float countTapHz = 1.15f;

    private Vector3 _prevRootPos;
    private Vector3 _prevVel;
    private float _prevYaw;
    private bool _primed;

    private Vector2 _tagAngle;
    private Vector2 _tagVel;

    private float _phaseSeed;
    private float _speechPhase;
    private float _speechWeight;
    private PatientVoice _patientVoice;

    private int _bsMouthOpen = -1;
    private int _bsMouthO = -1;
    private int _bsMouthWide = -1;

    public void BindRig()
    {
        hipsBone = FindDeep(transform, "mixamorig:Hips");
        spine1Bone = FindDeep(transform, "mixamorig:Spine1");
        spine2Bone = FindDeep(transform, "mixamorig:Spine2");
        neckBone = FindDeep(transform, "mixamorig:Neck");
        headBone = FindDeep(transform, "mixamorig:Head");
        mandibleBone = FindDeep(transform, "Speech_Mandible");
        restraintTagBone = FindDeep(transform, "Prop_RestraintTag_L");
        rightHandBone = FindDeep(transform, "mixamorig:RightHand");
        rightIndex1Bone = FindDeep(transform, "mixamorig:RightHandIndex1");

        Transform headTr = FindDeep(transform, "HildeBrandt_Head");
        if (headTr != null) headMesh = headTr.GetComponent<SkinnedMeshRenderer>();
        CacheBlendShapeIndices();
    }

    private void CacheBlendShapeIndices()
    {
        _bsMouthOpen = -1;
        _bsMouthO = -1;
        _bsMouthWide = -1;
        if (headMesh == null || headMesh.sharedMesh == null) return;
        Mesh m = headMesh.sharedMesh;
        _bsMouthOpen = m.GetBlendShapeIndex("Mouth_Open");
        _bsMouthO = m.GetBlendShapeIndex("Mouth_Speak_O");
        _bsMouthWide = m.GetBlendShapeIndex("Mouth_Wide");
    }

    private void Awake()
    {
        if (hipsBone == null || headBone == null || restraintTagBone == null)
            BindRig();
        else
            CacheBlendShapeIndices();

        _patientVoice = GetComponentInParent<PatientVoice>();
        _phaseSeed = (GetHashCode() & 0xFF) * 0.17f;
    }

    private void OnEnable()
    {
        _primed = false;
        _tagAngle = Vector2.zero;
        _tagVel = Vector2.zero;
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (dt <= 0.0001f || dt > 0.2f) return;

        Vector3 rootPos = transform.position;
        float yaw = transform.eulerAngles.y;

        if (!_primed)
        {
            _prevRootPos = rootPos;
            _prevVel = Vector3.zero;
            _prevYaw = yaw;
            _primed = true;
            return;
        }

        Vector3 worldVel = (rootPos - _prevRootPos) / dt;
        Vector3 worldAcc = (worldVel - _prevVel) / dt;
        float yawVel = Mathf.DeltaAngle(_prevYaw, yaw) / dt;

        _prevRootPos = rootPos;
        _prevVel = worldVel;
        _prevYaw = yaw;

        Vector3 localVel = transform.InverseTransformDirection(worldVel);
        Vector3 localAcc = transform.InverseTransformDirection(worldAcc);
        float speed01 = Mathf.Clamp01(new Vector2(localVel.x, localVel.z).magnitude / 2.5f);
        float t = Time.time + _phaseSeed;

        // 1. Dangling wrist restraint strap / #3 tag spring-damper physics
        if (restraintTagBone != null)
        {
            float rockSwingX = Mathf.Sin(t * 2.8f) * 6.5f + Mathf.Sin(t * 5.8f) * speed01 * 12f;
            float rockSwingZ = Mathf.Cos(t * 1.9f) * 4.0f;

            Vector2 extForce = new Vector2(
                Mathf.Clamp(-localAcc.z * tagLinearSensitivity + rockSwingX, -160f, 160f),
                Mathf.Clamp(localAcc.x * tagLinearSensitivity - yawVel * 0.25f + rockSwingZ, -160f, 160f));

            Vector2 springAcc = extForce - _tagAngle * tagStiffness - _tagVel * tagDamping;
            _tagVel += springAcc * dt;
            _tagAngle += _tagVel * dt;
            _tagAngle = Vector2.ClampMagnitude(_tagAngle, tagMaxAngle);

            restraintTagBone.localRotation = restraintTagBone.localRotation * Quaternion.Euler(_tagAngle.x, 0f, _tagAngle.y);
        }

        // 2. Shallow nervous breathing & counting finger tap
        float breathWave = Mathf.Sin(t * breathRateHz * Mathf.PI * 2f);
        if (spine1Bone != null)
        {
            spine1Bone.localRotation = spine1Bone.localRotation * Quaternion.Euler(breathWave * 1.4f, 0f, 0f);
        }
        if (spine2Bone != null)
        {
            spine2Bone.localRotation = spine2Bone.localRotation * Quaternion.Euler(breathWave * 1.1f, 0f, 0f);
        }
        if (rightIndex1Bone != null)
        {
            // Obsessive rhythmic finger counting pulse ("one... two... forty-one...")
            float tapPulse = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(t * countTapHz * Mathf.PI * 2f)), 4f) * 14f;
            rightIndex1Bone.localRotation = rightIndex1Bone.localRotation * Quaternion.Euler(tapPulse, 0f, 0f);
        }

        // 3. Speaking / Silent Counting Whisper Mouth Articulation
        if (_patientVoice == null) _patientVoice = GetComponentInParent<PatientVoice>();
        bool talkingLoud = _patientVoice != null && _patientVoice.IsTalking;
        _speechWeight = Mathf.MoveTowards(_speechWeight, talkingLoud ? 1f : 0f, dt * 7.5f);

        // Even when not showing a speech bubble, Hilde silently mouths door counts every few seconds
        float silentCountWindow = Mathf.SmoothStep(0.35f, 0.65f, Mathf.Sin(t * 0.55f));
        float silentWhisper = Mathf.Max(0f, Mathf.Sin(t * 6.8f)) * 0.16f * silentCountWindow;

        float open01 = silentWhisper;
        float o01 = silentWhisper * 0.5f;
        float wide01 = 0f;

        if (_speechWeight > 0.001f)
        {
            _speechPhase += dt * 9.8f;
            float s1 = Mathf.Max(0f, Mathf.Sin(_speechPhase));
            float s2 = Mathf.Max(0f, Mathf.Sin(_speechPhase * 1.55f + 0.9f));
            float s3 = Mathf.Max(0f, Mathf.Sin(_speechPhase * 0.78f + 2.1f));
            open01 = Mathf.Clamp01((s1 * 0.60f + s2 * 0.40f) * _speechWeight + silentWhisper * 0.3f);
            o01 = Mathf.Clamp01(s3 * 0.72f * _speechWeight);
            wide01 = Mathf.Clamp01((1f - s3) * s2 * 0.50f * _speechWeight);
        }

        if (mandibleBone != null)
        {
            mandibleBone.localRotation = mandibleBone.localRotation * Quaternion.Euler(open01 * 9.5f, 0f, 0f);
        }

        if (headMesh != null)
        {
            if (_bsMouthOpen >= 0) headMesh.SetBlendShapeWeight(_bsMouthOpen, open01 * 85f);
            if (_bsMouthO >= 0) headMesh.SetBlendShapeWeight(_bsMouthO, o01 * 70f);
            if (_bsMouthWide >= 0) headMesh.SetBlendShapeWeight(_bsMouthWide, wide01 * 60f);
        }
    }

    private static Transform FindDeep(Transform root, string targetName)
    {
        if (root == null) return null;
        if (root.name == targetName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), targetName);
            if (found != null) return found;
        }
        return null;
    }
}
