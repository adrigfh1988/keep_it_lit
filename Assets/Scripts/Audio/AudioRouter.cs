using UnityEngine;

/// <summary>
/// S2: sends every AudioSource that has no mixer group to the Effects group (music sources register themselves with GameSettings.RouteMusic and
/// are left alone). Every 0.5 s of unscaled time, plus once at Start - and again there for the mixer volumes, because AudioMixer.SetFloat in
/// Awake can be dropped before the mixer is live. Added by MazeGenerator.SetUpAtmosphere. One-shots spawned through GameAudio are routed from
/// their first sample; this catches the sources components create themselves (footsteps, doors, voices, ambience).
/// </summary>
public class AudioRouter : MonoBehaviour
{
    [SerializeField] private float sweepSeconds = 0.5f;
    private float _next;

    private void Start()
    {
        GameSettings.ApplyAudio();
        GameSettings.SweepToEffects();
        _next = Time.unscaledTime + sweepSeconds;
    }

    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + sweepSeconds;
        GameSettings.SweepToEffects();
    }
}
