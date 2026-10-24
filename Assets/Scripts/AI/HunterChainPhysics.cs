using UnityEngine;

/// <summary>
/// Drives the Hunter's wrist shackle chains (<c>LeftWristChain</c>, <c>RightWristChain</c>) and broken waist chain
/// (<c>WaistChain</c>) in <see cref="LateUpdate"/> after the <see cref="Animator"/> evaluates its humanoid clips.
///
/// Humanoid clips (<c>Idle</c>, <c>Walk_N</c>, <c>Run_N</c>) rotate the arms ~80 degrees down from the T-pose without
/// animating custom child bones, which would otherwise leave wrist-parented chains sticking out horizontally.
/// In Edit mode this keeps every chain hanging straight down under gravity; in Play mode it runs a multi-segment
/// Verlet pendulum simulation with gravity, inertia, damping, and hand/hip collision repulsion so the chains hang
/// downward and whip naturally during patrols, sprints, and the rage foot-stomp tantrum.
/// </summary>
[ExecuteAlways]
[DefaultExecutionOrder(120)]
public class HunterChainPhysics : MonoBehaviour
{
    [SerializeField] private float gravity = 18f;
    [SerializeField] private float damping = 4.5f;
    [SerializeField] private float outwardBias = 2.2f;
    [SerializeField] private float maxSwingDegrees = 75f;
    [SerializeField] private float handRepulsionRadius = 0.068f;

    private class ChainSim
    {
        public Transform[] Bones;
        public Vector3[] LocalAxes;
        public float[] Lengths;
        public Vector3[] Pos;
        public Vector3[] PrevPos;
        public Transform Repulsor;
        public float RepulsorRadius;
        public float OutwardSign; // -1 for character Left (-X in Unity), +1 for Right (+X in Unity)
        public bool Initialized;
    }

    private ChainSim _leftWrist;
    private ChainSim _rightWrist;
    private ChainSim _waist;

    private void OnEnable()
    {
        DiscoverChains();
    }

    public void DiscoverChains()
    {
        Transform leftHand = FindDeep(transform, "LeftHand");
        Transform rightHand = FindDeep(transform, "RightHand");
        Transform hips = FindDeep(transform, "Hips");

        _leftWrist = BuildChain(new[] { "LeftWristChain", "LeftWristChain_1", "LeftWristChain_2" }, leftHand, handRepulsionRadius, -1f);
        _rightWrist = BuildChain(new[] { "RightWristChain", "RightWristChain_1", "RightWristChain_2" }, rightHand, handRepulsionRadius, 1f);
        _waist = BuildChain(new[] { "WaistChain", "WaistChain_1" }, hips, 0.05f, -0.6f);
    }

    private ChainSim BuildChain(string[] boneNames, Transform repulsor, float radius, float outwardSign)
    {
        int count = 0;
        Transform[] found = new Transform[boneNames.Length];
        for (int i = 0; i < boneNames.Length; i++)
        {
            Transform b = FindDeep(transform, boneNames[i]);
            if (b == null) break;
            found[count++] = b;
        }
        if (count == 0) return null;

        Transform[] bones = new Transform[count];
        Vector3[] axes = new Vector3[count];
        float[] lengths = new float[count];
        for (int i = 0; i < count; i++)
        {
            bones[i] = found[i];
        }
        for (int i = 0; i < count; i++)
        {
            if (i + 1 < count)
            {
                Vector3 localToChild = bones[i].InverseTransformPoint(bones[i + 1].position);
                axes[i] = localToChild.sqrMagnitude > 1e-6f ? localToChild.normalized : Vector3.up;
                lengths[i] = Mathf.Max(0.04f, Vector3.Distance(bones[i].position, bones[i + 1].position));
            }
            else
            {
                axes[i] = i > 0 ? axes[i - 1] : Vector3.up;
                lengths[i] = i > 0 ? lengths[i - 1] : 0.095f;
            }
        }

        return new ChainSim
        {
            Bones = bones,
            LocalAxes = axes,
            Lengths = lengths,
            Pos = new Vector3[count + 1],
            PrevPos = new Vector3[count + 1],
            Repulsor = repulsor,
            RepulsorRadius = radius,
            OutwardSign = outwardSign,
            Initialized = false
        };
    }

    /// <summary>Immediately aligns all chain bones downward under gravity (used by Editor builders right after Animator.Update(0f)).</summary>
    public void PoseAtRest()
    {
        DiscoverChains();
        StepChain(_leftWrist, 0f);
        StepChain(_rightWrist, 0f);
        StepChain(_waist, 0f);
    }

    private void LateUpdate()
    {
        if (_leftWrist == null && _rightWrist == null && _waist == null)
        {
            DiscoverChains();
        }

        float dt = Application.isPlaying ? Mathf.Clamp(Time.deltaTime, 0.001f, 0.05f) : 0f;
        StepChain(_leftWrist, dt);
        StepChain(_rightWrist, dt);
        StepChain(_waist, dt);
    }

    private void StepChain(ChainSim sim, float dt)
    {
        if (sim == null || sim.Bones == null || sim.Bones.Length == 0 || sim.Bones[0] == null) return;

        int n = sim.Bones.Length;
        Vector3 outward = transform.right * sim.OutwardSign;
        Vector3 rootRestDir = (Vector3.down + outward * 0.32f).normalized;
        Vector3 tailRestDir = (Vector3.down + outward * 0.06f).normalized;

        if (!Application.isPlaying || !sim.Initialized || dt <= 0.0001f)
        {
            sim.Pos[0] = sim.Bones[0].position;
            sim.PrevPos[0] = sim.Pos[0];
            for (int i = 0; i < n; i++)
            {
                Transform b = sim.Bones[i];
                Vector3 segDir = (i == 0 && n > 2) ? rootRestDir : tailRestDir;
                Vector3 curDir = b.TransformDirection(sim.LocalAxes[i]);
                if (curDir.sqrMagnitude > 1e-6f)
                {
                    b.rotation = Quaternion.FromToRotation(curDir, segDir) * b.rotation;
                }
                sim.Pos[i + 1] = b.position + segDir * sim.Lengths[i];
                sim.PrevPos[i + 1] = sim.Pos[i + 1];
            }
            sim.Initialized = Application.isPlaying;
            return;
        }
        Vector3 restDir = tailRestDir;

        // If the hunter teleported/warped, reset particle positions cleanly to avoid a giant velocity spike
        if ((sim.Bones[0].position - sim.Pos[0]).sqrMagnitude > 1.5f)
        {
            sim.Initialized = false;
            StepChain(sim, 0f);
            sim.Initialized = true;
            return;
        }

        sim.PrevPos[0] = sim.Pos[0];
        sim.Pos[0] = sim.Bones[0].position;

        float dampFactor = Mathf.Clamp01(1f - damping * dt);
        Vector3 accel = (Vector3.down * gravity + outward * outwardBias) * (dt * dt);

        for (int i = 1; i <= n; i++)
        {
            Vector3 vel = (sim.Pos[i] - sim.PrevPos[i]) * dampFactor;
            sim.PrevPos[i] = sim.Pos[i];
            sim.Pos[i] += vel + accel;
        }

        float maxRad = maxSwingDegrees * Mathf.Deg2Rad;
        for (int iter = 0; iter < 4; iter++)
        {
            sim.Pos[0] = sim.Bones[0].position;
            for (int i = 0; i < n; i++)
            {
                Vector3 delta = sim.Pos[i + 1] - sim.Pos[i];
                float dist = delta.magnitude;
                Vector3 dir = dist > 1e-5f ? delta / dist : restDir;

                // Clamp maximum swing angle relative to straight down so the chain never flips above the wrist
                float angleFromDown = Vector3.Angle(Vector3.down, dir);
                if (angleFromDown > maxSwingDegrees)
                {
                    dir = Vector3.RotateTowards(Vector3.down, dir, maxRad, 0f).normalized;
                }

                sim.Pos[i + 1] = sim.Pos[i] + dir * sim.Lengths[i];

                // Push links outside the hand/fist volume so they hang clear alongside the hand
                if (sim.Repulsor != null && sim.RepulsorRadius > 0.001f)
                {
                    Vector3 center = sim.Repulsor.position + Vector3.down * 0.06f;
                    Vector3 fromHand = sim.Pos[i + 1] - center;
                    float d = fromHand.magnitude;
                    if (d < sim.RepulsorRadius && d > 1e-5f)
                    {
                        sim.Pos[i + 1] = center + (fromHand / d) * sim.RepulsorRadius;
                    }
                }
            }
        }

        // Apply solved segment directions to the chain bones in hierarchy order
        for (int i = 0; i < n; i++)
        {
            Transform b = sim.Bones[i];
            Vector3 desiredDir = (sim.Pos[i + 1] - sim.Pos[i]).normalized;
            if (desiredDir.sqrMagnitude < 1e-6f) desiredDir = restDir;

            Vector3 curDir = b.TransformDirection(sim.LocalAxes[i]);
            if (curDir.sqrMagnitude > 1e-6f)
            {
                b.rotation = Quaternion.FromToRotation(curDir, desiredDir) * b.rotation;
            }
        }
    }

    private static Transform FindDeep(Transform root, string childName)
    {
        if (root.name == childName) return root;
        for (int i = 0; i < root.childCount; i++)
        {
            Transform found = FindDeep(root.GetChild(i), childName);
            if (found != null) return found;
        }
        return null;
    }
}
