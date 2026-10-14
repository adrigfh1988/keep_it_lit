using System.Collections;
using UnityEngine;

/// <summary>
/// F114 R2 (the upper-floor toilets): the mirror cabinets over the sinks. When the player looks at the middle of the row from 2-6 m for
/// 0.8 s, every cabinet door creaks open together over 1.6 s, each to its own 60-110 degrees (seeded per door, so it is the same every
/// time). The leaves' pivots are already at their outer edges (the pack authored them that way) and a closed leaf has local yaw 0, so a
/// left-hand leaf opens to +yaw and a right-hand one to -yaw. Some leaves are authored ajar (-45 degrees); they open on from there.
/// Restore puts every leaf back to the pose it had when the room switched on.
/// </summary>
public class ScareCabinetsOpen : HubRoomScare
{
    [Tooltip("The cabinet leaves (MirrorShelf_DoorL / _DoorR). They must not be static.")]
    [SerializeField] private Transform[] leaves = new Transform[0];
    [Tooltip("Per leaf: +1 for a left-hand leaf (opens to +yaw), -1 for a right-hand one.")]
    [SerializeField] private float[] sides = new float[0];
    [SerializeField, Min(0.2f)] private float seconds = 1.6f;

    private Quaternion[] _rest;

    public override bool IsComplete => base.IsComplete && target != null && leaves != null && leaves.Length > 0 && sides != null && sides.Length == leaves.Length && System.Array.IndexOf(leaves, null) < 0;
    public Transform[] Leaves => leaves;
    public override Transform[] MovedParts => leaves;

    protected override void Capture()
    {
        if (_rest != null || leaves == null) return;
        _rest = new Quaternion[leaves.Length];
        for (int i = 0; i < leaves.Length; i++) if (leaves[i] != null) _rest[i] = leaves[i].localRotation;
    }

    protected override void Restore()
    {
        if (_rest == null) return;
        for (int i = 0; i < leaves.Length; i++) if (leaves[i] != null) leaves[i].localRotation = _rest[i];
    }

    protected override IEnumerator Play()
    {
        Vector3 at = target != null ? target.position : transform.position;
        if (!GameAudio.PlayAt(GameAudio.Library.exitDoorCreak, at, 0.6f)) GameAudio.ClipAtPoint(DoorAudio.BuildCreakClip(1.6f, 140f, 7), at, 0.6f);

        Quaternion[] to = new Quaternion[leaves.Length];
        for (int i = 0; i < leaves.Length; i++)
        {
            float degrees = 60f + 50f * (float)new System.Random(114 * 31 + i).NextDouble();
            to[i] = Quaternion.Euler(0f, sides[i] * degrees, 0f);
        }

        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            for (int i = 0; i < leaves.Length; i++) leaves[i].localRotation = Quaternion.Slerp(_rest[i], to[i], k);
            yield return null;
        }
        for (int i = 0; i < leaves.Length; i++) leaves[i].localRotation = to[i];
    }
}
