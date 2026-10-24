using UnityEngine;

/// <summary>
/// F93 (S4, floor 5): the key run. The Director's door is locked ("THE DIRECTOR'S KEY IS IN RECORDS"); the key hangs on the records
/// board in OfficeA and the pursuer (HubStalker) patrols a loop of waypoints (upper hall, main stairs, ground hall, the records room
/// door) for the whole visit. Two hub lockers stand on the route. The key stays held after a catch. Everything resets when the hub is left.
/// </summary>
public class KeyRunSetPiece : HubSetPiece
{
    // S9 (D12): the display lines are string KEYS now (one per wing, floor 5 here); the English is the table text, these are only the
    // fallback while a key is missing. LockedLineKey is what WingDoor.Lock stores and resolves when E answers.
    public const string LockedLineKey = "hub.wing.5.locked_line";
    public const string OrderLineKey = "hub.wing.5.order";
    private const string LockedLineFallback = "LOCKED · THE DIRECTOR'S KEY IS IN RECORDS";
    private const string OrderLineFallback = "KEY NOT ISSUED · RECORDS ROOM";

    /// <summary>F100: the readmission slip's order and the arrival line while the Director's door is locked (translated now).</summary>
    public static string OrderText => L10n.Has(OrderLineKey) ? L10n.T(OrderLineKey) : OrderLineFallback;

    [SerializeField] private HubStalker pursuer;
    [Tooltip("Waypoints of the patrol loop (AsylumHub/Life/PatrolRoute/*).")]
    [SerializeField] private Transform[] route = new Transform[0];
    [SerializeField] private HubKey key;
    [Tooltip("Index into route where the pursuer starts (the farthest one from the hub arrival is picked at runtime when negative).")]
    [SerializeField] private int startIndex = -1;

    public bool KeyHeld { get; private set; }

    /// <summary>F121: raised at the end of TakeKey (the hub breach's detonator). An instance event: the subscriber unsubscribes in OnEnd and OnDisable.</summary>
    public event System.Action KeyTaken;

    public bool IsComplete => pursuer != null && route != null && route.Length >= 3 && key != null;

    protected override void OnBegin()
    {
        AsylumHub hub = Hub;
        if (hub == null || !IsComplete)
        {
            Debug.LogError("KeyRunSetPiece: the pursuer, the patrol route or the key is missing. Run KEEP IT LIT > Build Missing Pieces. The Director's door stays open.", this);
            return;
        }

        if (HubBreach.SkipKeyRun)
        {
            // F121: a Retry after a breach death. The key is already in the player's pocket, the Director's door open and Timmy is not on the route: the breach itself starts a few seconds after the arrival.
            KeyHeld = true;
            key.gameObject.SetActive(false);
            SetDoorLock(false);
            return;
        }

        KeyHeld = false;
        key.gameObject.SetActive(true);
        key.Bind(this);
        SetDoorLock(true);

        int start = startIndex;
        if (start < 0)
        {
            float best = -1f;
            for (int i = 0; i < route.Length; i++)
            {
                float d = (route[i].position - hub.ArrivalPosition).sqrMagnitude;
                if (d > best) { best = d; start = i; }
            }
        }
        pursuer.ResetCatches();
        if (pursuer.Place(route[start].position)) pursuer.StartPatrol(route, (start + 1) % route.Length);
        else SetDoorLock(false); // never strand the player behind a lock with nothing to patrol it
    }

    protected override void OnEnd()
    {
        SetDoorLock(false);
        if (key != null) key.gameObject.SetActive(false);
        if (pursuer != null)
        {
            pursuer.Stop();
            pursuer.gameObject.SetActive(false);
        }
        KeyHeld = false;
    }

    public void TakeKey()
    {
        KeyHeld = true;
        SetDoorLock(false);
        if (key != null) key.gameObject.SetActive(false);
        if (Hub != null) Hub.SubtitleLine(L10n.T("hub.keyrun.taken"), 3f);
        KeyTaken?.Invoke();
    }

    /// <summary>F121: the breach withdraws the patrolling Timmy under the blackout (the hunter takes over the hub).</summary>
    public void DismissPursuer()
    {
        if (pursuer == null) return;
        pursuer.Stop();
        pursuer.gameObject.SetActive(false);
    }

    /// <summary>S9: this piece owns the Director's door lock (WingDoor.Lock / Unlock by owner - no text compare).</summary>
    private void SetDoorLock(bool locked)
    {
        AsylumHub hub = Hub;
        if (hub == null || hub.wingDoors == null || hub.wingDoors.Length < floor || hub.wingDoors[floor - 1] == null) return;
        if (locked) hub.wingDoors[floor - 1].Lock(this, LockedLineKey, LockedLineFallback);
        else hub.wingDoors[floor - 1].Unlock(this);
    }
}
