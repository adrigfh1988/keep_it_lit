using System.Collections;
using UnityEngine;

/// <summary>
/// F114 R1 (the men's toilets on the ground floor): one stall door stands open 70 degrees. When the player is in the room, more than a
/// couple of metres from it and looking AWAY from it, it slams shut in 0.12 s (ease-in, no ease-out) with a bang and a short torch flutter.
/// It stays shut for the rest of the visit; the next hub entry opens it again. The hinge is made by the builder and is never static.
/// </summary>
public class ScareStallSlam : HubRoomScare
{
    [Tooltip("The stall door's hinge (a DoorHinge the builder inserted). It is baked in the OPEN pose.")]
    [SerializeField] private Transform hinge;
    [Tooltip("The signed yaw (degrees) the hinge is turned from shut to the baked open pose.")]
    [SerializeField] private float openYaw = 70f;

    private Quaternion _open, _closed;
    private bool _have;

    public override bool IsComplete => base.IsComplete && hinge != null && target != null;
    public Transform Hinge => hinge;
    public override Transform[] MovedParts => new[] { hinge };

    protected override void Capture()
    {
        if (_have || hinge == null) return;
        _open = hinge.localRotation;
        _closed = Swung(_open, -openYaw);
        _have = true;
    }

    protected override void Restore()
    {
        if (_have && hinge != null) hinge.localRotation = _open;
    }

    protected override IEnumerator Play()
    {
        AsylumHub hub = Hub;
        Quaternion from = hinge.localRotation;
        const float seconds = 0.12f;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = t / seconds;
            hinge.localRotation = Quaternion.Slerp(from, _closed, k * k); // ease-in only: it hits the frame at full speed
            yield return null;
        }
        hinge.localRotation = _closed;
        Bang(target != null ? target.position : hinge.position, 0.85f);
        if (hub != null && hub.Torch != null) hub.Torch.Stutter(0.25f);
    }
}
