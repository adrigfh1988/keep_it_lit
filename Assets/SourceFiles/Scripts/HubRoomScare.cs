using System.Collections;
using UnityEngine;

/// <summary>
/// F114: the shared base of the seven hub room scares (one in each room that holds a HubCache). Same contract as HubCreakScare, many times:
/// a one-shot per hub visit that fires only on the floors in <see cref="floorMask"/> (never in the finale), never while a hub pursuer is after
/// the player or a transition runs, only once the player has been inside <see cref="room"/> for <see cref="armDelay"/> seconds and the look
/// condition (<see cref="lookMode"/>, held <see cref="holdSeconds"/>) is met. AsylumHub.OnEnterHub re-arms it and puts everything back
/// (<see cref="Restore"/>); OnDisable (the hub content switching off for a maze) stops it and restores too. Nothing here hurts, costs
/// anything or makes noise for the hunters.
/// Each scare is its own subclass in its own file. Subclasses override <see cref="Play"/> (the scare), <see cref="Restore"/> and
/// optionally <see cref="Capture"/> (remember the resting pose, called before every Restore), <see cref="Triggered"/> (a different trigger)
/// and <see cref="Tick"/> (per-frame work that must run while the scare plays, e.g. a safety reopen).
/// Parts a scare moves must not be static (static batching freezes them) - the builder clears the flags.
/// </summary>
public abstract class HubRoomScare : MonoBehaviour
{
    public enum LookMode { Any, At, Away }

    [Tooltip("Short id used by the debug entry points: R1..R7.")]
    [SerializeField] protected string roomId = "";
    [Tooltip("Floors (1..5) it is armed on: bit n = floor n. The finale never.")]
    [SerializeField] protected int floorMask;
    [Tooltip("The room (the world AABB of this box is used, like ChaseSetPiece's zones). Trigger, no physics.")]
    [SerializeField] protected BoxCollider room;
    [Tooltip("What the player must be looking at (or away from, see lookMode). Optional.")]
    [SerializeField] protected Transform target;
    [SerializeField] protected LookMode lookMode = LookMode.Any;
    [Tooltip("At: dot of the camera forward with the direction to the target above this. Away: below -0.1.")]
    [SerializeField] protected float lookDot = 0.6f;
    [Tooltip("To the target, for At/Away: no further than this.")]
    [SerializeField] protected float maxDistance = 6f;
    [Tooltip("To the target, for At/Away: no nearer than this (0 = off).")]
    [SerializeField] protected float minDistance;
    [Tooltip("The look condition must hold this long (s).")]
    [SerializeField] protected float holdSeconds;
    [Tooltip("The player must have been in the room this long before it can fire (s).")]
    [SerializeField] protected float armDelay = 0.6f;

    private bool _fired;
    private float _inRoomSeconds;
    private float _heldSeconds;
    private Coroutine _routine;

    public string RoomId => roomId;
    public int FloorMask => floorMask;
    public BoxCollider Room => room;
    public Transform Target => target;

    /// <summary>True once it has played in this hub visit.</summary>
    public bool HasFired => _fired;

    /// <summary>True while the scare routine is running.</summary>
    public bool IsPlaying => _routine != null;

    /// <summary>The builder wired everything this scare needs. Subclasses add their own parts.</summary>
    public virtual bool IsComplete => room != null && floorMask != 0 && !string.IsNullOrEmpty(roomId);

    /// <summary>The transforms this scare moves (the builder and Check Project verify none of them is static).</summary>
    public virtual Transform[] MovedParts => System.Array.Empty<Transform>();

    protected static AsylumHub Hub => AsylumHub.Active;

    /// <summary>Armed on `floor` (1..5) and not in the finale.</summary>
    public bool ArmedOn(int floor) => !HubState.IsFinale && floor >= 1 && floor <= 5 && (floorMask & (1 << floor)) != 0;

    /// <summary>Armed on the floor whose hub visit this is.</summary>
    protected bool ArmedNow => ArmedOn(HubState.Floor);

    protected abstract IEnumerator Play();
    protected abstract void Restore();

    /// <summary>Remembers the resting pose of whatever the scare moves. Called by the base before the first Restore; must be idempotent.</summary>
    protected virtual void Capture() { }

    protected virtual void Tick(AsylumHub hub) { }

    protected virtual void OnEnable()
    {
        Capture();
        Rearm();
        AsylumHub.OnEnterHub += HandleEnter;
    }

    protected virtual void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        StopScare();
        Restore();
    }

    private void HandleEnter(int floor)
    {
        StopScare();
        Capture();
        Rearm();
    }

    private void Rearm()
    {
        _fired = false;
        _inRoomSeconds = 0f;
        _heldSeconds = 0f;
        Restore();
    }

    /// <summary>Stops the scare routine where it stands (Restore is the caller's job). The scare stays "fired" for this visit.</summary>
    protected void Abort() => StopScare();

    private void StopScare()
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
    }

    private void Update()
    {
        AsylumHub hub = Hub;
        if (hub == null || hub.Player == null || hub.PlayerCamera == null) return;
        Tick(hub);
        if (_fired || _routine != null) return;
        if (Time.timeScale <= 0f || hub.IsTransitioning || hub.Leaving) { _inRoomSeconds = 0f; _heldSeconds = 0f; return; }
        if (HubState.IsFinale || HubBreach.InProgress || !ArmedNow || room == null) return; // F121: the hunter is the scare
        if (hub.PursuerAfterPlayer()) return;

        if (!InRoom(hub)) { _inRoomSeconds = 0f; _heldSeconds = 0f; return; }
        _inRoomSeconds += Time.deltaTime;
        if (_inRoomSeconds < armDelay) return;
        if (!Triggered(hub, Time.deltaTime)) return;

        Fire();
    }

    /// <summary>The player (about hip height) is inside the room box.</summary>
    protected bool InRoom(AsylumHub hub) => room != null && room.bounds.Contains(hub.Player.position + Vector3.up * 0.9f);

    /// <summary>The look condition and its hold timer. Subclasses with a different trigger override this.</summary>
    protected virtual bool Triggered(AsylumHub hub, float dt)
    {
        bool ok = LookOk(hub);
        _heldSeconds = ok ? _heldSeconds + dt : 0f;
        return ok && _heldSeconds >= holdSeconds;
    }

    /// <summary>One frame of the look condition (no hold).</summary>
    protected bool LookOk(AsylumHub hub)
    {
        if (lookMode == LookMode.Any || target == null) return true;
        Transform cam = hub.PlayerCamera.transform;
        Vector3 to = target.position - cam.position;
        float distance = to.magnitude;
        if (distance > maxDistance || distance < minDistance) return false;
        float dot = Vector3.Dot(cam.forward, to / Mathf.Max(0.01f, distance));
        return lookMode == LookMode.At ? dot > lookDot : dot < -0.1f;
    }

    private void Fire()
    {
        _fired = true;
        _routine = StartCoroutine(Run());
    }

    private IEnumerator Run()
    {
        yield return Play();
        _routine = null;
    }

    [ContextMenu("Debug Fire")]
    private void DebugFireMenu() => DebugFire();

    /// <summary>Testing only: plays the scare now, skipping every guard except that the hub must be active. Returns a one-line report.</summary>
    public string DebugFire()
    {
        AsylumHub hub = Hub;
        if (hub == null || !isActiveAndEnabled || hub.Player == null || hub.PlayerCamera == null) return roomId + ": no active hub";
        if (_routine != null) return roomId + ": already playing";
        Capture();
        Fire();
        return roomId + ": fired";
    }

    /// <summary>Testing only (CLI): fires the scare of room `id` (R1..R7) in the active hub.</summary>
    public static string DebugFireIn(string id)
    {
        HubRoomScare scare = Find(id);
        return scare == null ? $"no active scare '{id}'" : scare.DebugFire();
    }

    /// <summary>The scare of room `id` in the active hub (active ones only), or null.</summary>
    public static HubRoomScare Find(string id)
    {
        AsylumHub hub = Hub;
        if (hub == null) return null;
        foreach (HubRoomScare scare in hub.GetComponentsInChildren<HubRoomScare>(false))
        {
            if (scare.roomId == id) return scare;
        }
        return null;
    }

    // ---------------------------------------------------------------- shared helpers

    private static AudioClip s_bang;

    /// <summary>The slam of a stall or cell door: the `stallSlam` slot, or a generated low bang while it is empty.</summary>
    protected static void Bang(Vector3 position, float volume)
    {
        if (GameAudio.PlayAt(GameAudio.Library.stallSlam, position, volume)) return;
        if (s_bang == null) s_bang = DoorAudio.BuildClunkClip(0.35f, 70f, 0.85f);
        GameAudio.ClipAtPoint(s_bang, position, volume);
    }

    /// <summary>Swings `hinge` about its parent's up axis: `yaw` degrees on top of `rest`.</summary>
    protected static Quaternion Swung(Quaternion rest, float yaw) => Quaternion.Euler(0f, yaw, 0f) * rest;
}
