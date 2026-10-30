using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Shared locomotion, 0/1/2 gait hysteresis, MotionSpeed foot-lock scaling, Animator state crossfader,
/// and head-yaw tracking collaborator for humanoid pursuers (<see cref="DarkBruno"/>, <see cref="NightNurse"/>, and <see cref="AIFollower"/>).
/// </summary>
public sealed class EntityGaitAnimator
{
    public const float MinLocomotionPlayback = 0.4f;
    public const float DefaultStateFadeSeconds = 0.18f;

    public static readonly int AnimSpeed = Animator.StringToHash("Speed");
    public static readonly int AnimMotionSpeed = Animator.StringToHash("MotionSpeed");
    public static readonly int AnimGrounded = Animator.StringToHash("Grounded");
    public static readonly int AnimTurn = Animator.StringToHash("Turn");
    public static readonly int AnimPose = Animator.StringToHash("Pose");
    public static readonly int AnimReach = Animator.StringToHash("Reach");
    public static readonly int LocomotionHash = Animator.StringToHash("Locomotion");

    public Animator Animator { get; private set; }
    public bool HasSpeedParam { get; private set; }
    public bool HasMotionSpeedParam { get; private set; }
    public bool HasGroundedParam { get; private set; }
    public bool HasTurnParam { get; private set; }
    public bool HasPoseParam { get; private set; }
    public bool HasReachParam { get; private set; }
    public bool HasLocomotionState { get; private set; }

    public float AnimationBlend { get; private set; }
    public float Gait { get; private set; }
    public bool GaitRunning { get; private set; }
    public float TurnRaw { get; set; }
    public float TurnBlend { get; private set; }
    public float Playback { get; private set; } = 1f;
    public int CurrentStateHash { get; private set; }
    public float HeadYaw { get; private set; }

    /// <summary>Caches Animator parameter flags and initializes Grounded/MotionSpeed defaults.</summary>
    public void Bind(Animator animator)
    {
        Animator = animator;
        HasSpeedParam = false;
        HasMotionSpeedParam = false;
        HasGroundedParam = false;
        HasTurnParam = false;
        HasPoseParam = false;
        HasReachParam = false;
        HasLocomotionState = false;

        if (animator == null || animator.runtimeAnimatorController == null) return;

        foreach (AnimatorControllerParameter p in animator.parameters)
        {
            if (p.nameHash == AnimSpeed) HasSpeedParam = true;
            else if (p.nameHash == AnimMotionSpeed) HasMotionSpeedParam = true;
            else if (p.nameHash == AnimGrounded) HasGroundedParam = true;
            else if (p.nameHash == AnimTurn) HasTurnParam = true;
            else if (p.nameHash == AnimPose && p.type == AnimatorControllerParameterType.Int) HasPoseParam = true;
            else if (p.nameHash == AnimReach && p.type == AnimatorControllerParameterType.Trigger) HasReachParam = true;
        }

        HasLocomotionState = animator.HasState(0, LocomotionHash);
        if (HasGroundedParam) animator.SetBool(AnimGrounded, true);
        if (HasMotionSpeedParam) animator.SetFloat(AnimMotionSpeed, 1f);
    }

    /// <summary>Computes the walk-to-run transition speed for pursuers (<see cref="DarkBruno"/>, <see cref="NightNurse"/>).</summary>
    public static float ComputePursuerWalkToRunSpeed(float locomotionWalkSpeed, float maxWalkPlayback, float rushOrChargeSpeed)
    {
        float walkCap = Mathf.Max(0.25f, locomotionWalkSpeed) * Mathf.Max(1f, maxWalkPlayback);
        return Mathf.Clamp(walkCap, 1.5f, Mathf.Max(2f, rushOrChargeSpeed - 0.1f));
    }

    /// <summary>Computes the walk-to-run transition speed for <see cref="AIFollower"/>.</summary>
    public static float ComputeHunterWalkToRunSpeed(float locomotionWalkSpeed, float locomotionRunSpeed, float maxWalkPlayback, float maxLocomotionPlayback)
    {
        if (locomotionWalkSpeed < 0.05f || locomotionRunSpeed < 0.05f) return 1.5f;
        return Mathf.Min(
            locomotionWalkSpeed * Mathf.Min(maxWalkPlayback, maxLocomotionPlayback),
            Mathf.Sqrt(locomotionWalkSpeed * locomotionRunSpeed));
    }

    /// <summary>
    /// Steps 0/1/2 gait hysteresis, foot-locked MotionSpeed playback, and Turn blend for <see cref="DarkBruno"/> and <see cref="NightNurse"/>.
    /// Returns true if the Animator is active and ready for state switching.
    /// </summary>
    public bool StepPursuerGait(
        NavMeshAgent agent,
        bool isRushingOrCharging,
        float locomotionWalkSpeed,
        float locomotionRunSpeed,
        float maxWalkPlayback,
        float maxLocomotionPlayback,
        float rushOrChargeSpeed,
        float animationBlendRate,
        out bool moving,
        out float planarSpeed,
        out float walkSpeed,
        out float runSpeed,
        out float playback)
    {
        moving = false;
        planarSpeed = 0f;
        walkSpeed = Mathf.Max(0.25f, locomotionWalkSpeed);
        runSpeed = Mathf.Max(walkSpeed, locomotionRunSpeed);
        playback = 1f;

        if (Animator == null || !Animator.isActiveAndEnabled || Animator.runtimeAnimatorController == null || agent == null)
            return false;

        planarSpeed = agent.isStopped ? 0f : new Vector2(agent.velocity.x, agent.velocity.z).magnitude;
        AnimationBlend = Mathf.MoveTowards(AnimationBlend, planarSpeed, Time.deltaTime * animationBlendRate);
        if (AnimationBlend < 0.01f) AnimationBlend = 0f;

        float runSwitch = ComputePursuerWalkToRunSpeed(locomotionWalkSpeed, maxWalkPlayback, rushOrChargeSpeed);
        if (isRushingOrCharging && planarSpeed >= 0.15f) GaitRunning = true;
        else if (planarSpeed >= runSwitch + 0.15f) GaitRunning = true;
        else if (planarSpeed <= runSwitch - 0.25f) GaitRunning = false;

        moving = planarSpeed >= 0.08f || AnimationBlend >= 0.08f;
        float targetGait = !moving ? 0f : (GaitRunning ? 2f : 1f);
        Gait = Mathf.MoveTowards(Gait, targetGait, Time.deltaTime * 4f);

        float runWeight = Mathf.Clamp01(Gait - 1f);
        float clipSpeed = Mathf.Lerp(walkSpeed, runSpeed, runWeight);
        playback = moving
            ? Mathf.Clamp(planarSpeed / clipSpeed, MinLocomotionPlayback, maxLocomotionPlayback)
            : 1f;
        Playback = playback;

        if (HasSpeedParam) Animator.SetFloat(AnimSpeed, Gait);
        if (HasMotionSpeedParam) Animator.SetFloat(AnimMotionSpeed, playback);
        if (HasTurnParam)
        {
            TurnBlend = Mathf.Lerp(TurnBlend, TurnRaw, Time.deltaTime * animationBlendRate);
            Animator.SetFloat(AnimTurn, TurnBlend);
        }

        return true;
    }

    /// <summary>Plays the appropriate locomotion state during patrol/rotation/investigation/cooldown modes.</summary>
    public void PlayPatrolLocomotion(
        bool moving,
        float planarSpeed,
        float walkSpeed,
        float playback,
        float maxLocomotionPlayback,
        string locomotionState,
        string patrolState,
        string warnState,
        string rushFallbackState)
    {
        if (Animator == null) return;

        if (HasLocomotionState)
        {
            PlayState(locomotionState, patrolState, rushFallbackState, warnState, null, null);
            Animator.speed = HasMotionSpeedParam ? 1f : playback;
        }
        else if (moving)
        {
            PlayState(patrolState, patrolState, rushFallbackState, warnState, null, null);
            Animator.speed = HasMotionSpeedParam ? 1f : Mathf.Clamp(planarSpeed / walkSpeed, MinLocomotionPlayback, maxLocomotionPlayback);
        }
        else
        {
            PlayState(warnState, patrolState, rushFallbackState, warnState, null, null);
            Animator.speed = 1f;
        }
    }

    /// <summary>Plays the appropriate locomotion state during rush/charge modes.</summary>
    public void PlayChargeLocomotion(
        float planarSpeed,
        float runSpeed,
        float playback,
        float maxLocomotionPlayback,
        string locomotionState,
        string patrolState,
        string rushOrChargeState,
        string warnState)
    {
        if (Animator == null) return;

        if (HasLocomotionState)
        {
            PlayState(locomotionState, patrolState, rushOrChargeState, warnState, null, null);
            Animator.speed = HasMotionSpeedParam ? 1f : playback;
        }
        else
        {
            PlayState(rushOrChargeState, patrolState, rushOrChargeState, warnState, null, null);
            Animator.speed = HasMotionSpeedParam ? 1f : Mathf.Clamp(planarSpeed / runSpeed, MinLocomotionPlayback, maxLocomotionPlayback);
        }
    }

    /// <summary>Plays a non-locomotion action state (Warn, Scream/Roar, Pin) at a fixed playback speed.</summary>
    public void PlayActionState(
        string state,
        float speed,
        string patrolState,
        string rushOrChargeState,
        string warnState,
        string screamOrRoarState,
        string pinState,
        string chopState = null)
    {
        if (Animator == null) return;
        PlayState(state, patrolState, rushOrChargeState, warnState, screamOrRoarState, pinState, chopState);
        Animator.speed = speed;
        if (HasMotionSpeedParam) Animator.SetFloat(AnimMotionSpeed, speed);
    }

    /// <summary>Places the Animator in its idle/dormant state when !CanAct or dormant.</summary>
    public void PauseToIdle(string locomotionState, string patrolState, string rushOrChargeState, string warnState)
    {
        if (Animator != null) Animator.speed = 1f;
        if (HasSpeedParam) Animator.SetFloat(AnimSpeed, 0f);
        if (HasMotionSpeedParam) Animator.SetFloat(AnimMotionSpeed, 1f);
        PlayState(HasLocomotionState ? locomotionState : warnState, patrolState, rushOrChargeState, warnState, null, null);
    }

    /// <summary>Resolves a state hash with fallback names for HubPeople.controller / Pursuer.controller compatibility.</summary>
    public int ResolveStateHash(
        string state,
        string patrolState,
        string rushOrChargeState,
        string warnState,
        string screamOrRoarState,
        string pinState,
        string chopState = null)
    {
        if (Animator == null || string.IsNullOrEmpty(state)) return 0;
        int hash = Animator.StringToHash(state);
        if (Animator.HasState(0, hash)) return hash;

        string fallback = null;
        if (state == patrolState) fallback = "Walk";
        else if (state == rushOrChargeState) fallback = "Run";
        else if ((!string.IsNullOrEmpty(chopState) && state == chopState) || state == warnState) fallback = "Look";
        else if (state == screamOrRoarState) fallback = "Talking";
        else if (state == pinState) fallback = "Attack";

        if (!string.IsNullOrEmpty(fallback))
        {
            int fbHash = Animator.StringToHash(fallback);
            if (Animator.HasState(0, fbHash)) return fbHash;
            if (state == rushOrChargeState)
            {
                int walkHash = Animator.StringToHash("Walk");
                if (Animator.HasState(0, walkHash)) return walkHash;
            }
        }

        return 0;
    }

    /// <summary>Cross-fades to <paramref name="state"/> if not already playing or transitioning into it.</summary>
    public void PlayState(
        string state,
        string patrolState,
        string rushOrChargeState,
        string warnState,
        string screamOrRoarState,
        string pinState,
        string chopState = null,
        float fadeSeconds = DefaultStateFadeSeconds)
    {
        if (Animator == null || !Animator.isActiveAndEnabled || Animator.runtimeAnimatorController == null) return;
        int hash = ResolveStateHash(state, patrolState, rushOrChargeState, warnState, screamOrRoarState, pinState, chopState);
        if (hash == 0) return;

        AnimatorStateInfo info = Animator.GetCurrentAnimatorStateInfo(0);
        bool alreadyPlaying = (hash == CurrentStateHash) && (info.shortNameHash == hash || Animator.IsInTransition(0));
        if (alreadyPlaying) return;

        CurrentStateHash = hash;
        Animator.CrossFadeInFixedTime(hash, fadeSeconds);
    }

    /// <summary>Samples <paramref name="point"/> onto the NavMesh and sets the agent destination.</summary>
    public static void GoTo(NavMeshAgent agent, Vector3 point, float snapRadius, float repathInterval, ref float nextRepathAt)
    {
        nextRepathAt = Time.time + repathInterval;
        if (agent == null) return;
        if (NavMesh.SamplePosition(point, out NavMeshHit hit, snapRadius, NavMesh.AllAreas))
            agent.SetDestination(hit.position);
        else
            agent.SetDestination(point);
    }

    /// <summary>Ramps <paramref name="agent"/>'s speed toward <paramref name="targetSpeed"/> at <paramref name="speedRampRate"/>.</summary>
    public static void MoveSpeedTowards(NavMeshAgent agent, float targetSpeed, float speedRampRate)
    {
        if (agent == null) return;
        agent.speed = Mathf.MoveTowards(agent.speed, targetSpeed, speedRampRate * Time.deltaTime);
    }

    /// <summary>Returns the current planar movement or steering heading of <paramref name="agent"/>.</summary>
    public static Vector3 CurrentHeading(NavMeshAgent agent, Transform self)
    {
        if (agent == null) return Vector3.zero;
        Vector3 v = agent.velocity;
        v.y = 0f;
        if (v.sqrMagnitude > 0.04f) return v;

        if (agent.hasPath && self != null)
        {
            Vector3 steer = agent.steeringTarget - self.position;
            steer.y = 0f;
            if (steer.sqrMagnitude > 0.01f) return steer;
        }

        return Vector3.zero;
    }

    /// <summary>Rotates <paramref name="self"/> toward <paramref name="desiredDirection"/> and records normalized <see cref="TurnRaw"/>.</summary>
    public void UpdateRotation(
        Transform self,
        NavMeshAgent agent,
        Vector3 desiredDirection,
        bool isFastMode,
        float turnSpeedFast,
        float turnSpeedMoving,
        float turnSpeedStationary)
    {
        desiredDirection.y = 0f;
        if (self == null || desiredDirection.sqrMagnitude < 0.001f)
        {
            TurnRaw = 0f;
            return;
        }

        Vector3 forward = self.forward;
        forward.y = 0f;
        float signedAngle = Vector3.SignedAngle(forward, desiredDirection, Vector3.up);
        TurnRaw = Mathf.Clamp(signedAngle / 90f, -1f, 1f);

        float planarSpeed = agent != null ? new Vector2(agent.velocity.x, agent.velocity.z).magnitude : 0f;
        float rate = isFastMode ? turnSpeedFast
            : planarSpeed > 0.25f ? turnSpeedMoving
            : turnSpeedStationary;

        Quaternion targetRot = Quaternion.LookRotation(desiredDirection);
        self.rotation = Quaternion.RotateTowards(self.rotation, targetRot, rate * Time.deltaTime);
    }

    /// <summary>Aligns <paramref name="body"/> to the floor top height and applies smooth additive head-yaw tracking toward <paramref name="player"/>.</summary>
    public void UpdateBodyGroundAndHeadTrack(
        Transform entityTransform,
        Transform body,
        Transform head,
        MazeGenerator maze,
        Transform player,
        bool lookAtPlayer,
        bool fastTrack,
        float maxGroundOffset = 0.20f)
    {
        if (body == null || entityTransform == null) return;

        float floorOffset = maze != null
            ? Mathf.Clamp(maze.FloorTopY - entityTransform.position.y, -maxGroundOffset, maxGroundOffset)
            : 0f;
        body.localPosition = new Vector3(0f, floorOffset, 0f);

        if (head == null || player == null) return;

        float desiredYaw = 0f;
        if (lookAtPlayer)
        {
            Vector3 axis = body.up;
            Vector3 forward = Vector3.ProjectOnPlane(body.forward, axis);
            Vector3 toPlayer = Vector3.ProjectOnPlane(player.position - head.position, axis);
            if (forward.sqrMagnitude > 0.0001f && toPlayer.sqrMagnitude > 0.0001f)
            {
                float yaw = Vector3.SignedAngle(forward, toPlayer, axis);
                if (Mathf.Abs(yaw) <= 125f) desiredYaw = Mathf.Clamp(yaw, -75f, 75f);
            }
        }

        float rate = fastTrack ? 320f : 110f;
        HeadYaw = Mathf.MoveTowardsAngle(HeadYaw, desiredYaw, rate * Time.deltaTime);
        if (Mathf.Abs(HeadYaw) > 0.01f)
        {
            head.rotation = Quaternion.AngleAxis(HeadYaw, body.up) * head.rotation;
        }
    }

    /// <summary>Mirrors updated meshes/materials/avatar from the scene's <see cref="AsylumHub"/> NPC or Patient counterpart onto this maze instance.</summary>
    public static void SyncCosmeticsFromHub(Transform body, Animator animator, HubNpc.Who? who, string hubBodyName)
    {
        if (body == null) return;
        AsylumHub hub = Object.FindAnyObjectByType<AsylumHub>(FindObjectsInactive.Include);
        if (hub == null || hub.life == null) return;

        Transform hubBody = null;
        if (who.HasValue)
        {
            foreach (HubNpc npc in hub.life.GetComponentsInChildren<HubNpc>(true))
            {
                if (npc != null && npc.who == who.Value && npc.body != null && npc.body.name == hubBodyName)
                {
                    hubBody = npc.body;
                    break;
                }
            }
        }
        else
        {
            foreach (HubPatient patient in hub.life.GetComponentsInChildren<HubPatient>(true))
            {
                if (patient != null && patient.body != null && patient.body.name == hubBodyName)
                {
                    hubBody = patient.body;
                    break;
                }
            }
        }
        if (hubBody == null) return;

        Animator hubAnim = hubBody.GetComponent<Animator>();
        if (hubAnim != null && hubAnim.avatar != null && hubAnim.avatar.isValid && animator != null)
        {
            animator.avatar = hubAnim.avatar;
        }

        Renderer[] hubRenderers = hubBody.GetComponentsInChildren<Renderer>(true);
        Renderer[] myRenderers = body.GetComponentsInChildren<Renderer>(true);
        foreach (Renderer myR in myRenderers)
        {
            if (myR == null) continue;
            foreach (Renderer hubR in hubRenderers)
            {
                if (hubR != null && hubR.name == myR.name)
                {
                    myR.sharedMaterials = hubR.sharedMaterials;
                    if (myR is SkinnedMeshRenderer mySmr && hubR is SkinnedMeshRenderer hubSmr && hubSmr.sharedMesh != null)
                    {
                        if (mySmr.bones != null && hubSmr.bones != null && mySmr.bones.Length == hubSmr.bones.Length)
                        {
                            mySmr.sharedMesh = hubSmr.sharedMesh;
                        }
                    }
                    break;
                }
            }
        }
    }

    /// <summary>Recursively searches <paramref name="parent"/>'s hierarchy for a child with exact <paramref name="name"/>.</summary>
    public static Transform FindDeepChild(Transform parent, string name)
    {
        if (parent == null) return null;
        if (parent.name == name) return parent;
        for (int i = 0; i < parent.childCount; i++)
        {
            Transform found = FindDeepChild(parent.GetChild(i), name);
            if (found != null) return found;
        }
        return null;
    }
}
