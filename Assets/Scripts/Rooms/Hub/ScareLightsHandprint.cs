using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// F114 R3 (the upper bathroom): when the player looks at the middle mirror cabinet from within 3 m, every light in the room goes out and
/// the torch dies for 1.3 s with a low thump. When it all comes back, a bloody hand-print is on the mirror (inactive until then). Only
/// lights that were ON are switched off, and exactly those are switched back on (a lamp HubDecay killed stays dead). The room has no
/// lights of its own on some floors; then the torch blackout alone is the scare. Restore: lights as they were, print hidden.
/// </summary>
public class ScareLightsHandprint : HubRoomScare
{
    [Tooltip("The hand-print on the cabinet's left mirror (a child of the left leaf). Inactive until the lights return.")]
    [SerializeField] private GameObject handprint;
    [SerializeField, Min(0.2f)] private float darkSeconds = 1.3f;
    [SerializeField, Min(0f)] private float lightMargin = 0.5f;

    private readonly List<Light> _switchedOff = new List<Light>();

    public override bool IsComplete => base.IsComplete && target != null && handprint != null;
    public GameObject Handprint => handprint;

    protected override void Restore()
    {
        foreach (Light l in _switchedOff) if (l != null) l.enabled = true;
        _switchedOff.Clear();
        if (handprint != null) handprint.SetActive(false);
    }

    protected override IEnumerator Play()
    {
        AsylumHub hub = Hub;
        Bounds bounds = room.bounds;
        bounds.Expand(lightMargin * 2f);
        _switchedOff.Clear();
        foreach (Light l in hub.GetComponentsInChildren<Light>(false))
        {
            if (l == null || !l.enabled || !bounds.Contains(l.transform.position)) continue;
            l.enabled = false;
            _switchedOff.Add(l);
        }

        if (hub.Torch != null)
        {
            hub.Torch.Stutter(darkSeconds);
            hub.Torch.Blackout(darkSeconds);
        }
        Vector3 at = target != null ? target.position : transform.position;
        if (!GameAudio.PlayAt(GameAudio.Library.liftThud, at, 0.5f)) GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.3f, 55f, 0.8f), at, 0.5f);

        yield return new WaitForSeconds(darkSeconds);

        foreach (Light l in _switchedOff) if (l != null) l.enabled = true;
        _switchedOff.Clear();
        if (handprint != null) handprint.SetActive(true);
    }
}
