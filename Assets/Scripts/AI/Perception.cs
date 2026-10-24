using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Shared zero-allocation perception utilities for the Hunter, Stalker (Stan), and DESCENT entities.
/// Centralizes non-alloc line-of-sight raycasts, hierarchy filtering, flashlight cone illumination,
/// camera view-cone checks, and NavMesh path-distance evaluation.
/// </summary>
public static class Perception
{
    private static readonly RaycastHit[] Hits = new RaycastHit[24];
    private static NavMeshPath _sharedPath;

    /// <summary>True while the maze run is actively ticking and neither ended nor paused.</summary>
    public static bool IsRunLive => GameFlow.IsRunActive && !GameFlow.IsInSafeRoom && !GameOutcome.IsOver && Time.timeScale > 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _sharedPath = null;
    }

    /// <summary>True if <paramref name="t"/> is <paramref name="root"/> or a descendant of <paramref name="root"/>.</summary>
    public static bool IsPartOf(Transform t, Transform root)
    {
        if (t == null || root == null) return false;
        return t == root || t.IsChildOf(root);
    }

    /// <summary>
    /// True if no solid (non-trigger) collider lies between <paramref name="from"/> and <paramref name="to"/>,
    /// ignoring any hits belonging to <paramref name="selfRoot"/> or <paramref name="targetRoot"/>.
    /// </summary>
    public static bool HasClearLine(Vector3 from, Vector3 to, Transform selfRoot, Transform targetRoot = null, int layerMask = ~0)
    {
        return HasClearLine(from, to, selfRoot, targetRoot, null, layerMask);
    }

    /// <summary>
    /// Overload allowing a third hierarchy (<paramref name="extraIgnoreRoot"/>) to be ignored during the raycast.
    /// </summary>
    public static bool HasClearLine(Vector3 from, Vector3 to, Transform selfRoot, Transform targetRoot, Transform extraIgnoreRoot, int layerMask = ~0)
    {
        Vector3 ray = to - from;
        float distance = ray.magnitude;
        if (distance < 0.0001f) return true;

        int count = Physics.RaycastNonAlloc(from, ray / distance, Hits, distance, layerMask, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = Hits[i].transform;
            if (IsPartOf(hit, selfRoot) || IsPartOf(hit, targetRoot) || IsPartOf(hit, extraIgnoreRoot))
                continue;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Evaluates how strongly <paramref name="torch"/> illuminates <paramref name="worldPoint"/>:
    /// returns <c>0f</c> if the torch is null, unlit, out of range, outside the outer cone, or occluded;
    /// returns <c>(0..1)</c> in the penumbra between inner and outer cones; and returns <c>1f</c> inside the bright core.
    /// </summary>
    public static float EvaluateTorchIllumination(Flashlight torch, Vector3 worldPoint, Transform ignoreRoot, bool requireLos = true)
    {
        if (torch == null || !torch.IsBeamLit) return 0f;

        Vector3 origin = torch.BeamOrigin;
        Vector3 toPoint = worldPoint - origin;
        float distance = toPoint.magnitude;
        if (distance < 0.0001f) return 1f;
        if (distance > torch.Range) return 0f;

        float angle = Vector3.Angle(torch.BeamForward, toPoint);
        float outerHalf = torch.OuterAngle * 0.5f;
        if (angle >= outerHalf) return 0f;

        if (requireLos && !HasClearLine(origin, worldPoint, torch.transform.root, ignoreRoot))
            return 0f;

        float innerHalf = torch.InnerAngle * 0.5f;
        if (angle <= innerHalf) return 1f;

        float penumbraSpan = Mathf.Max(0.001f, outerHalf - innerHalf);
        return Mathf.Clamp01(1f - (angle - innerHalf) / penumbraSpan);
    }

    /// <summary>
    /// True if <paramref name="worldPoint"/> is inside the lit flashlight cone (with optional range/angle scaling and slack degrees)
    /// and unobstructed by walls.
    /// </summary>
    public static bool IsIlluminatedByTorch(Flashlight torch, Vector3 worldPoint, Transform ignoreRoot, float rangeScale = 1f, float angleScale = 1f, float slackDegrees = 4f)
    {
        if (torch == null || !torch.IsBeamLit) return false;

        Vector3 origin = torch.BeamOrigin;
        Vector3 toPoint = worldPoint - origin;
        float distance = toPoint.magnitude;
        if (distance < 0.0001f) return true;
        if (distance > torch.Range * rangeScale) return false;

        float halfAngle = (torch.OuterAngle * 0.5f) * angleScale + slackDegrees;
        if (Vector3.Angle(torch.BeamForward, toPoint) >= halfAngle) return false;

        return HasClearLine(origin, worldPoint, torch.transform.root, ignoreRoot);
    }

    /// <summary>
    /// True if <paramref name="worldPoint"/> is within <paramref name="maxDist"/> and <paramref name="halfAngleDeg"/> of <paramref name="cam"/>'s forward axis,
    /// with optional line-of-sight verification.
    /// </summary>
    public static bool IsInCameraCone(Camera cam, Vector3 worldPoint, float maxDist, float halfAngleDeg, Transform ignoreRoot, bool requireLos = true)
    {
        if (cam == null) return false;
        return IsInCameraCone(cam.transform, worldPoint, maxDist, halfAngleDeg, cam.transform.root, ignoreRoot, requireLos);
    }

    /// <summary>
    /// Transform-based overload of <see cref="IsInCameraCone(Camera, Vector3, float, float, Transform, bool)"/> for entities that cache the view camera Transform.
    /// </summary>
    public static bool IsInCameraCone(Transform camTransform, Vector3 worldPoint, float maxDist, float halfAngleDeg, Transform selfRoot, Transform ignoreRoot = null, bool requireLos = true)
    {
        if (camTransform == null) return false;

        Vector3 toPoint = worldPoint - camTransform.position;
        float distance = toPoint.magnitude;
        if (distance > maxDist) return false;
        if (distance >= 0.01f && Vector3.Angle(camTransform.forward, toPoint) > halfAngleDeg)
            return false;

        if (requireLos && !HasClearLine(camTransform.position, worldPoint, selfRoot, ignoreRoot))
            return false;

        return true;
    }

    /// <summary>
    /// Calculates the NavMesh path length between <paramref name="from"/> and <paramref name="to"/> using a shared <see cref="NavMeshPath"/>.
    /// Returns <c>true</c> only if both endpoints sample onto the NavMesh, the path is <see cref="NavMeshPathStatus.PathComplete"/>,
    /// and total corner length is <c>&lt;= maxLength</c>.
    /// </summary>
    public static bool TryGetNavPathLength(Vector3 from, Vector3 to, float maxLength, out float length, float sampleRadius = 3f)
    {
        length = 0f;
        if (maxLength <= 0.01f) return false;

        if (maxLength < float.MaxValue && Vector3.Distance(from, to) > maxLength)
            return false;

        if (!NavMesh.SamplePosition(from, out NavMeshHit fromHit, sampleRadius, NavMesh.AllAreas)) return false;
        if (!NavMesh.SamplePosition(to, out NavMeshHit toHit, sampleRadius, NavMesh.AllAreas)) return false;

        if (_sharedPath == null) _sharedPath = new NavMeshPath();
        if (!NavMesh.CalculatePath(fromHit.position, toHit.position, NavMesh.AllAreas, _sharedPath)) return false;
        if (_sharedPath.status != NavMeshPathStatus.PathComplete) return false;

        Vector3[] corners = _sharedPath.corners;
        for (int i = 1; i < corners.Length; i++)
        {
            length += Vector3.Distance(corners[i - 1], corners[i]);
        }

        return length <= maxLength;
    }
}
