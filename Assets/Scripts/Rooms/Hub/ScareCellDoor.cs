using System.Collections;
using UnityEngine;

/// <summary>
/// F114 R7 (the seclusion cell): the cell door stands open. When the player has walked into the cell, at least a leaf's width plus 0.4 m
/// from the hinge (so the swinging leaf can never hit them), the door slams shut in 0.25 s with a bang, a whisper comes from the padded
/// wall opposite, and the torch flutters. 3.5 s later it creaks open again over 2 s. The reopen is guaranteed: it also runs from
/// Restore (every hub entry and when the hub switches off) and immediately if a hub pursuer starts after the player or a catch or
/// transition begins while the door is shut - the player can never stay trapped.
/// </summary>
public class ScareCellDoor : HubRoomScare
{
    [Tooltip("The cell door's hinge (a DoorHinge). It is baked in the OPEN pose.")]
    [SerializeField] private Transform hinge;
    [Tooltip("The signed yaw (degrees) the hinge is turned from shut to the baked open pose.")]
    [SerializeField] private float openYaw = 95f;
    [Tooltip("The cell alone (not the whole seclusion room): the trigger box built from its walls.")]
    [SerializeField] private BoxCollider cell;
    [Tooltip("Width of the leaf (m).")]
    [SerializeField] private float leafWidth = 1.06f;
    [Tooltip("The padded wall opposite the door: where the whisper comes from.")]
    [SerializeField] private Transform whisperFrom;
    [SerializeField, Min(0.5f)] private float shutSeconds = 3.5f;
    [SerializeField, Min(0.2f)] private float reopenSeconds = 2f;

    private Quaternion _open, _closed;
    private bool _have;
    private bool _shut;

    public override bool IsComplete => base.IsComplete && hinge != null && cell != null && whisperFrom != null;
    public Transform Hinge => hinge;
    public override Transform[] MovedParts => new[] { hinge };
    public BoxCollider Cell => cell;

    /// <summary>True from the slam until the door is fully open again.</summary>
    public bool IsShut => _shut;

    protected override void Capture()
    {
        if (_have || hinge == null) return;
        _open = hinge.localRotation;
        _closed = Swung(_open, -openYaw);
        _have = true;
    }

    protected override void Restore()
    {
        _shut = false;
        if (_have && hinge != null) hinge.localRotation = _open;
    }

    protected override bool Triggered(AsylumHub hub, float dt)
    {
        if (cell == null || hinge == null) return false;
        Vector3 feet = hub.Player.position;
        if (!cell.bounds.Contains(feet + Vector3.up * 0.9f)) return false;
        Vector3 away = feet - hinge.position;
        away.y = 0f;
        return away.magnitude >= leafWidth + 0.4f;
    }

    // Safety: whatever starts mid-scare, the door must not stay shut on the player.
    protected override void Tick(AsylumHub hub)
    {
        if (!_shut) return;
        if (hub.PursuerAfterPlayer() || hub.IsTransitioning || hub.Leaving)
        {
            Abort();
            Restore();
        }
    }

    protected override IEnumerator Play()
    {
        AsylumHub hub = Hub;
        Quaternion from = hinge.localRotation;
        const float slam = 0.25f;
        _shut = true;
        for (float t = 0f; t < slam; t += Time.deltaTime)
        {
            float k = t / slam;
            hinge.localRotation = Quaternion.Slerp(from, _closed, k * k);
            yield return null;
        }
        hinge.localRotation = _closed;
        Bang(hinge.position + Vector3.up * 1f, 1f);
        Vector3 wall = whisperFrom != null ? whisperFrom.position : hinge.position;
        GameAudio.PlayAt(GameAudio.Library.whisperEcho, wall, 0.8f);
        if (hub != null && hub.Torch != null) hub.Torch.Stutter(0.8f);

        yield return new WaitForSeconds(shutSeconds);

        if (!GameAudio.PlayAt(GameAudio.Library.exitDoorCreak, hinge.position + Vector3.up, 0.5f))
            GameAudio.ClipAtPoint(DoorAudio.BuildCreakClip(2f, 150f, 3), hinge.position + Vector3.up, 0.5f);
        for (float t = 0f; t < reopenSeconds; t += Time.deltaTime)
        {
            hinge.localRotation = Quaternion.Slerp(_closed, _open, Mathf.SmoothStep(0f, 1f, t / reopenSeconds));
            yield return null;
        }
        hinge.localRotation = _open;
        _shut = false;
    }
}
