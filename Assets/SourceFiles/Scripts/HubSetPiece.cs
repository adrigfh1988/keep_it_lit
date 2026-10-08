using UnityEngine;

/// <summary>
/// F93 (E8): base of the walk-between-wings events in the asylum hub (the lift, the flood, the chase, the key run). A set piece lives
/// under AsylumHub/Life, so it is only enabled while the player is in the hub; it begins when the hub is entered for its floor
/// (never in the finale) and ends - and must reset itself completely - when the hub is left, so death, Retry and a re-enter all start
/// it clean. It must never strand the player: anything it needs that is missing is a LogError and the piece simply stays off.
/// </summary>
public abstract class HubSetPiece : MonoBehaviour
{
    [Range(1, 5)] public int floor = 2;

    /// <summary>True between OnBegin and OnEnd.</summary>
    public bool Running { get; private set; }

    protected AsylumHub Hub => AsylumHub.Active;

    private void OnEnable()
    {
        AsylumHub.OnEnterHub += HandleEnter;
        AsylumHub.OnLeaveHub += HandleLeave;
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        AsylumHub.OnLeaveHub -= HandleLeave;
        Stop();
    }

    private void HandleEnter(int enteredFloor)
    {
        Stop();
        if (HubState.IsFinale || enteredFloor != floor || HubState.Floor != floor) return;
        Running = true;
        OnBegin();
    }

    private void HandleLeave() => Stop();

    private void Stop()
    {
        if (!Running) return;
        Running = false;
        OnEnd();
    }

    /// <summary>The hub was entered for this piece's floor.</summary>
    protected abstract void OnBegin();

    /// <summary>The hub is being left (or switched off): undo everything the piece changed.</summary>
    protected abstract void OnEnd();
}
