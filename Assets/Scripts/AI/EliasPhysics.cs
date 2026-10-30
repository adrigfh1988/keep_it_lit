using UnityEngine;

/// <summary>
/// Procedural secondary bone physics & cold-storage somatic animation for Elias Vogt ("The Frost" / Patient #5):
///   - Spring-damper pendulum physics for the dangling `#5` metal morgue tag (`Prop_ToeTag_L`) on his left wrist.
///   - Cold-storage shivering micro-tremor on head/neck/jaw and frostbitten finger curl.
///   - Procedural right-arm overhead reach toward `WallLamp`s during `HarvestLamp` and arm-shielding recoil
///     during focused-flashlight stagger (`Stunned`), plus lunge reach during `ColdSurge` / `FrostShove` / `Capture`.
///   - Speaking/screaming mouth articulation (`Mouth_Open`, `Mouth_Speak_O`, `Mouth_Wide` BlendShapes +
///     `Speech_Mandible` bone) driven either by Hub dialogue (`PatientVoice` / `EntityVoiceAndMusic`) or DESCENT screams.
/// Runs in `LateUpdate` at `[DefaultExecutionOrder(20)]` after `Animator` and `Elias.LateUpdate` (`order 0`).
/// </summary>
[DefaultExecutionOrder(20)]
[DisallowMultipleComponent]
public sealed class EliasPhysics : MonoBehaviour
{
    [Header("Auto-Discovered Rig Bones")]
    [SerializeField] private Transform hipsBone;
    [SerializeField] private Transform spine1Bone;
    [SerializeField] private Transform spine2Bone;
    [SerializeField] private Transform neckBone;
    [SerializeField] private Transform headBone;
    [SerializeField] private Transform mandibleBone;
    [SerializeField] private Transform toeTagBone;
    [SerializeField] private Transform leftShoulderBone;
    [SerializeField] private Transform leftArmBone;
    [SerializeField] private Transform leftForeArmBone;
    [SerializeField] private Transform leftHandBone;
    [SerializeField] private Transform leftIndex1Bone;
    [SerializeField] private Transform rightShoulderBone;
    [SerializeField] private Transform rightArmBone;
    [SerializeField] private Transform rightForeArmBone;
    [SerializeField] private Transform rightHandBone;
    [SerializeField] private Transform rightIndex1Bone;
    [SerializeField] private SkinnedMeshRenderer headMesh;

    [Header("Dangling #5 Morgue Tag Spring-Damper")]
    [SerializeField] private float tagStiffness = 115f;
    [SerializeField] private float tagDamping = 7.5f;
    [SerializeField] private float tagLinearSensitivity = 48f;
    [SerializeField] private float tagMaxAngle = 36f;

    [Header("Cold Shiver & Breathing")]
    [SerializeField] private float breathRateHz = 0.28f;
    [SerializeField] private float shiverIntensity = 1.0f;

    private Vector3 _prevRootPos;
    private Vector3 _prevVel;
    private float _prevYaw;
    private bool _primed;

    // 2D angular spring state (pitch X, roll Z) for the dangling #5 morgue tag
    private Vector2 _tagAngle;
    private Vector2 _tagVel;

    private float _phaseSeed;
    private float _speechPhase;
    private float _speechWeight;
    private bool _externalSpeaking;

    // Smoothed procedural pose weights driven by Elias.cs in DESCENT (zero in Asylum Hub)
    private float _harvestReachWeight;
    private Vector3 _harvestLampWorldPos;
    private bool _hasHarvestLampPos;
    private float _recoilShieldWeight;
    private float _surgeWeight;
    private float _shoveWeight;

    private int _bsMouthOpen = -1;
    private int _bsMouthO = -1;
    private int _bsMouthWide = -1;

    private AudioSource _voiceSource;
    private float[] _voiceSamples;

    public void BindRig()
    {
        hipsBone = FindDeep(transform, "mixamorig:Hips");
        spine1Bone = FindDeep(transform, "mixamorig:Spine1");
        spine2Bone = FindDeep(transform, "mixamorig:Spine2");
        neckBone = FindDeep(transform, "mixamorig:Neck");
        headBone = FindDeep(transform, "mixamorig:Head");
        mandibleBone = FindDeep(transform, "Speech_Mandible");
        toeTagBone = FindDeep(transform, "Prop_ToeTag_L");

        leftShoulderBone = FindDeep(transform, "mixamorig:LeftShoulder");
        leftArmBone = FindDeep(transform, "mixamorig:LeftArm");
        leftForeArmBone = FindDeep(transform, "mixamorig:LeftForeArm");
        leftHandBone = FindDeep(transform, "mixamorig:LeftHand");
        leftIndex1Bone = FindDeep(transform, "mixamorig:LeftHandIndex1");

        rightShoulderBone = FindDeep(transform, "mixamorig:RightShoulder");
        rightArmBone = FindDeep(transform, "mixamorig:RightArm");
        rightForeArmBone = FindDeep(transform, "mixamorig:RightForeArm");
        rightHandBone = FindDeep(transform, "mixamorig:RightHand");
        rightIndex1Bone = FindDeep(transform, "mixamorig:RightHandIndex1");

        Transform headTr = FindDeep(transform, "EliasVogt_Head");
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

    public void SetSpeaking(bool speaking)
    {
        _externalSpeaking = speaking;
    }

    public void SetPoseState(
        bool harvesting,
        Vector3 harvestTargetPos,
        bool hasHarvestTarget,
        bool recoiling,
        bool surging,
        bool shoving)
    {
        float dt = Mathf.Max(0.001f, Time.deltaTime);
        _harvestReachWeight = Mathf.MoveTowards(_harvestReachWeight, harvesting ? 1f : 0f, dt * 4.5f);
        _hasHarvestLampPos = hasHarvestTarget;
        if (hasHarvestTarget) _harvestLampWorldPos = harvestTargetPos;

        _recoilShieldWeight = Mathf.MoveTowards(_recoilShieldWeight, recoiling ? 1f : 0f, dt * 7.0f);
        _surgeWeight = Mathf.MoveTowards(_surgeWeight, surging ? 1f : 0f, dt * 5.5f);
        _shoveWeight = Mathf.MoveTowards(_shoveWeight, shoving ? 1f : 0f, dt * 8.0f);
    }

    private void Awake()
    {
        if (hipsBone == null || headBone == null || toeTagBone == null)
            BindRig();
        else
            CacheBlendShapeIndices();

        _phaseSeed = (GetHashCode() & 0xFF) * 0.19f;
    }

    private void OnEnable()
    {
        _primed = false;
        _tagAngle = Vector2.zero;
        _tagVel = Vector2.zero;
        _harvestReachWeight = 0f;
        _recoilShieldWeight = 0f;
        _surgeWeight = 0f;
        _shoveWeight = 0f;
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
        float speed01 = Mathf.Clamp01(new Vector2(localVel.x, localVel.z).magnitude / 3.2f);
        float t = Time.time + _phaseSeed;

        // 1. Dangling #5 Metal Morgue Tag spring-damper physics on `Prop_ToeTag_L`
        if (toeTagBone != null)
        {
            // Wrist walk swing + inertia (no high-frequency buzz)
            float walkSwingX = Mathf.Sin(t * 6.4f) * speed01 * 14f;
            float walkSwingZ = Mathf.Cos(t * 3.2f) * speed01 * 8f;

            Vector2 extForce = new Vector2(
                Mathf.Clamp(-localAcc.z * tagLinearSensitivity + walkSwingX, -180f, 180f),
                Mathf.Clamp(localAcc.x * tagLinearSensitivity - yawVel * 0.25f + walkSwingZ, -180f, 180f));

            Vector2 springAcc = extForce - _tagAngle * tagStiffness - _tagVel * tagDamping;
            _tagVel += springAcc * dt;
            _tagAngle += _tagVel * dt;
            _tagAngle = Vector2.ClampMagnitude(_tagAngle, tagMaxAngle);

            toeTagBone.localRotation = toeTagBone.localRotation * Quaternion.Euler(_tagAngle.x, 0f, _tagAngle.y);
        }

        // 2. Cold-storage chest breathing & smooth posture pitch on Spine/Neck/Head
        //    (No high-frequency Perlin noise on Spine/Neck — spine noise acts as a lever arm that makes the hands tremble!)
        float breathWave = Mathf.Sin(t * breathRateHz * Mathf.PI * 2f);
        float stressShiver = shiverIntensity * Mathf.Clamp01(_surgeWeight * 0.7f + _recoilShieldWeight * 0.8f);
        float headTremorX = (Mathf.PerlinNoise(t * 11.0f, 1.3f) - 0.5f) * 2.0f * stressShiver;
        float headTremorY = (Mathf.PerlinNoise(2.7f, t * 12.5f) - 0.5f) * 2.4f * stressShiver;

        if (spine1Bone != null)
        {
            float chestPitch = breathWave * 1.3f - _surgeWeight * 5.0f + _recoilShieldWeight * 6.0f;
            spine1Bone.localRotation = spine1Bone.localRotation * Quaternion.Euler(chestPitch, 0f, 0f);
        }

        if (spine2Bone != null)
        {
            float upperPitch = breathWave * 1.0f - _harvestReachWeight * 4.5f - _surgeWeight * 4.0f;
            spine2Bone.localRotation = spine2Bone.localRotation * Quaternion.Euler(upperPitch, 0f, 0f);
        }

        if (neckBone != null)
        {
            float neckPitch = -_harvestReachWeight * 3.5f + _recoilShieldWeight * 4.0f;
            neckBone.localRotation = neckBone.localRotation * Quaternion.Euler(neckPitch, 0f, 0f);
        }

        if (headBone != null)
        {
            float headTilt = Mathf.Sin(t * 0.85f) * 2.5f * (1f - speed01 * 0.5f);
            float headPitch = -_harvestReachWeight * 7f + _recoilShieldWeight * 9f - _surgeWeight * 6f;
            float headYaw = _recoilShieldWeight * 10f * Mathf.Sin(t * 2.2f);
            headBone.localRotation = headBone.localRotation * Quaternion.Euler(headPitch + headTremorX, headYaw + headTremorY, headTilt);
        }

        // 3. Gentle Upper-Arm Lamp Aim Refinement during Harvest
        //    The Mixamo Humanoid clips (`Reach_Overhead`, `Nervous_Look_Around`, `Zombie_Scream`, `Frost_Shove`)
        //    drive the arms and hands cleanly; we only apply a gentle, damped upper-arm aim nudge when stopped
        //    at a lamp so we never fight the Animator or twist the forearm/wrist bones.
        if (rightArmBone != null && rightForeArmBone != null && _harvestReachWeight > 0.001f && _hasHarvestLampPos)
        {
            Vector3 toLamp = _harvestLampWorldPos - rightArmBone.position;
            if (toLamp.sqrMagnitude > 0.04f)
            {
                Vector3 targetDir = toLamp.normalized;
                Vector3 curUpper = (rightForeArmBone.position - rightArmBone.position).normalized;
                if (curUpper.sqrMagnitude > 0.001f && Vector3.Dot(curUpper, targetDir) > -0.5f)
                {
                    Quaternion upperDelta = Quaternion.Slerp(
                        Quaternion.identity,
                        Quaternion.FromToRotation(curUpper, targetDir),
                        _harvestReachWeight * 0.25f);
                    rightArmBone.rotation = upperDelta * rightArmBone.rotation;
                }
            }
        }

        // 4. Speaking / Screaming Mouth BlendShapes & Mandible Bone
        bool speakingNow = _externalSpeaking || _surgeWeight > 0.15f || IsVoiceAudioPlaying(out _);
        _speechWeight = Mathf.MoveTowards(_speechWeight, speakingNow ? 1f : 0f, dt * 8f);

        // Even when silent, Elias has a slight hypothermic jaw chatter
        float jawChatter = Mathf.Abs(Mathf.Sin(t * 23f)) * 0.08f * shiverIntensity;
        float open01 = jawChatter;
        float o01 = 0f;
        float wide01 = 0f;

        if (_speechWeight > 0.001f)
        {
            _speechPhase += dt * 10.5f;
            if (_surgeWeight > 0.25f)
            {
                // Convulsive cold surge / scream: wide open mouth
                open01 = Mathf.Max(open01, 0.85f * _surgeWeight);
                o01 = 0.55f * _surgeWeight;
                wide01 = 0.45f * _surgeWeight;
            }
            else
            {
                float s1 = Mathf.Max(0f, Mathf.Sin(_speechPhase));
                float s2 = Mathf.Max(0f, Mathf.Sin(_speechPhase * 1.65f + 1.1f));
                float s3 = Mathf.Max(0f, Mathf.Sin(_speechPhase * 0.72f + 2.4f));
                open01 = Mathf.Clamp01((s1 * 0.62f + s2 * 0.38f) * _speechWeight + jawChatter);
                o01 = Mathf.Clamp01(s3 * 0.75f * _speechWeight);
                wide01 = Mathf.Clamp01((1f - s3) * s2 * 0.55f * _speechWeight);
            }
        }

        if (mandibleBone != null)
        {
            mandibleBone.localRotation = mandibleBone.localRotation * Quaternion.Euler(open01 * 10.5f, 0f, 0f);
        }

        if (headMesh != null)
        {
            if (_bsMouthOpen >= 0) headMesh.SetBlendShapeWeight(_bsMouthOpen, open01 * 88f);
            if (_bsMouthO >= 0) headMesh.SetBlendShapeWeight(_bsMouthO, o01 * 72f);
            if (_bsMouthWide >= 0) headMesh.SetBlendShapeWeight(_bsMouthWide, wide01 * 64f);
        }
    }

    private bool IsVoiceAudioPlaying(out float rms)
    {
        rms = 0f;
        if (_voiceSource == null)
            _voiceSource = GetComponentInChildren<AudioSource>();

        if (_voiceSource == null || !_voiceSource.isPlaying)
            return false;

        if (_voiceSamples == null || _voiceSamples.Length != 64)
            _voiceSamples = new float[64];

        _voiceSource.GetOutputData(_voiceSamples, 0);
        float sum = 0f;
        for (int i = 0; i < _voiceSamples.Length; i++)
            sum += _voiceSamples[i] * _voiceSamples[i];
        rms = Mathf.Sqrt(sum / _voiceSamples.Length);
        return rms > 0.004f;
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
