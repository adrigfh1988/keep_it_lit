using UnityEngine;

/// <summary>
/// F118: the Reduce flashing setting as one place to ask. Every fast flicker, blink or strobe checks <see cref="On"/> and, when it is
/// set, draws a slow fade instead (at most <see cref="MaxHz"/> changes of direction per second). Only what is DRAWN changes -
/// gameplay values (WallLamp.CurrentIntensity01, IsDead, Flashlight.IsBeamLit...) stay exactly as they were, because stealth, the
/// Stalker and the phantoms read them. Read live every frame, so toggling the setting mid-run applies at once.
/// </summary>
public static class FlashSafety
{
    /// <summary>The ceiling for any visible brightness oscillation while Reduce flashing is on.</summary>
    public const float MaxHz = 3f;
    /// <summary>Shortest time a light may hold one state before flipping back: half a period at MaxHz.</summary>
    public const float MinStateSeconds = 1f / (2f * MaxHz);

    public static bool On => GameSettings.ReduceFlashing;

    /// <summary>Moves <paramref name="shown"/> toward <paramref name="target"/> at a full swing (0..1) per <paramref name="seconds"/>.</summary>
    public static float Smooth(float shown, float target, float dt, float seconds = 0.25f)
        => Mathf.MoveTowards(shown, target, dt / Mathf.Max(0.0001f, seconds));
}
