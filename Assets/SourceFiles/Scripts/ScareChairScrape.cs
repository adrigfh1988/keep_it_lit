using System.Collections;
using UnityEngine;

/// <summary>
/// F114 R5 (the visiting room): a chair near the wall. When the player is in the room, more than a metre from it and looking AWAY from it,
/// it slides back 0.35 m and turns 18 degrees in 0.35 s with a scrape - as if someone stood up. It stays moved for the visit; the next hub
/// entry puts it back. The chair is a hub prop (not static; its collider moves with it).
/// </summary>
public class ScareChairScrape : HubRoomScare
{
    [SerializeField] private Transform chair;
    [Tooltip("How far the chair slides, in the chair's own local space (its back direction).")]
    [SerializeField] private Vector3 slideLocal = new Vector3(0f, 0f, -0.35f);
    [SerializeField] private float turnDegrees = 18f;
    [SerializeField, Min(0.05f)] private float seconds = 0.35f;

    private Vector3 _restPos;
    private Quaternion _restRot;
    private bool _have;
    private static AudioClip s_scrape;

    public override bool IsComplete => base.IsComplete && chair != null && target != null;
    public Transform Chair => chair;
    public override Transform[] MovedParts => new[] { chair };

    protected override void Capture()
    {
        if (_have || chair == null) return;
        _restPos = chair.localPosition;
        _restRot = chair.localRotation;
        _have = true;
    }

    protected override void Restore()
    {
        if (!_have || chair == null) return;
        chair.localPosition = _restPos;
        chair.localRotation = _restRot;
    }

    protected override IEnumerator Play()
    {
        Vector3 toPos = _restPos + _restRot * slideLocal;
        Quaternion toRot = Quaternion.Euler(0f, turnDegrees, 0f) * _restRot;
        if (!GameAudio.PlayAt(GameAudio.Library.chairScrape, chair.position, 0.8f))
        {
            if (s_scrape == null) s_scrape = DoorAudio.BuildScrapeClip();
            GameAudio.ClipAtPoint(s_scrape, chair.position, 0.8f);
        }

        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / seconds);
            chair.localPosition = Vector3.Lerp(_restPos, toPos, k);
            chair.localRotation = Quaternion.Slerp(_restRot, toRot, k);
            yield return null;
        }
        chair.localPosition = toPos;
        chair.localRotation = toRot;
    }
}
