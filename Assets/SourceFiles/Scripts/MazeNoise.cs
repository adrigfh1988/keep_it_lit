using UnityEngine;

/// <summary>
/// F107 D2: every loud thing in the maze, as one static event. AIFollower.HearNoise raises it before its own gates, so each existing noise
/// source (a thrown bottle, glass, a locker slam, the lift, an item cache, Stan's touch, a star being taken) reaches every listening entity
/// without new wiring. Only the Listener subscribes today. Scene-scoped: every subscriber unsubscribes in OnDisable/OnDestroy, and the
/// SubsystemRegistration reset is the backstop for a stale handler surviving with Reload Domain off.
/// </summary>
public static class MazeNoise
{
    /// <summary>A noise at a world position that carries `radius` metres.</summary>
    public static event System.Action<Vector3, float> Emitted;

    /// <summary>Raises <see cref="Emitted"/>.</summary>
    public static void Emit(Vector3 position, float radius) => Emitted?.Invoke(position, radius);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Emitted = null;
}
