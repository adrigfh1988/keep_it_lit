using UnityEngine;

/// <summary>
/// F112: the two sounds (and the dust) every walk-through door shares: the latch letting go, then cold air coming out of the opened doorway.
/// Recorded slots first (doorLatch, doorDraft), a generated clip while they are empty.
/// </summary>
public static class DoorPassageFx
{
    private static AudioClip _latchClip;
    private static AudioClip _draftClip;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _latchClip = null;
        _draftClip = null;
    }

    /// <summary>The latch letting go.</summary>
    public static void Latch(Vector3 at)
    {
        DoorAudio.Note("door latch");
        if (GameAudio.PlayAt(GameAudio.Library.doorLatch, at, 0.9f)) return;
        if (_latchClip == null) _latchClip = DoorAudio.BuildClunkClip(0.12f, 190f, 0.35f);
        GameAudio.ClipAtPoint(_latchClip, at, 0.9f);
    }

    /// <summary>Cold air out of the opened doorway: a soft whoosh and (when the door has one) a puff of dust.</summary>
    public static void Draft(Vector3 at, ParticleSystem dust)
    {
        DoorAudio.Note("door draft");
        if (!GameAudio.PlayAt(GameAudio.Library.doorDraft, at, 0.7f))
        {
            if (_draftClip == null) _draftClip = DoorAudio.BuildDraftClip();
            GameAudio.ClipAtPoint(_draftClip, at, 0.7f);
        }
        if (dust != null) dust.Play();
    }
}
