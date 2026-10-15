using System.Collections;
using UnityEngine;

/// <summary>
/// F114 R4 (the staff kitchen): the old television is ON from the moment the hub is entered on its armed floors - a screen of static
/// (the noise texture slides around every 0.05 s) with a quiet 3D hiss. When the player comes within 2.2 m of it, or looks at it for 1.5 s
/// from anywhere in the room, it clicks OFF; 0.8 s later a whisper comes from behind the player. On unarmed floors the screen is dark and
/// the room silent. The screen is a quad the builder fitted to the set's front face. Restore: back on (if armed).
/// </summary>
public class ScareTelevision : HubRoomScare
{
    [Tooltip("The static quad on the television's screen face. Disabled while the set is off.")]
    [SerializeField] private Renderer screen;
    [Tooltip("The hiss: a looping 3D source on the screen object (min 1 m, max 9 m, volume 0.25).")]
    [SerializeField] private AudioSource hiss;
    [Tooltip("Within this distance of the set the player trips it at once.")]
    [SerializeField] private float nearDistance = 2.2f;
    [SerializeField] private float staticStep = 0.05f;

    private Material _material;
    private bool _on;
    private float _nextStep;
    private static AudioClip s_hiss, s_click;

    public override bool IsComplete => base.IsComplete && screen != null && hiss != null && target != null;
    public bool IsOn => _on;
    public Renderer Screen => screen;

    protected override void Capture()
    {
        if (_material != null || screen == null) return;
        _material = screen.material; // an instance: the UV offset must not leak into the shared asset
        if (hiss != null)
        {
            AudioClip clip = GameAudio.Library != null ? GameAudio.Library.tvStatic.First() : null;
            if (clip == null)
            {
                if (s_hiss == null) s_hiss = DoorAudio.BuildStaticHissClip();
                clip = s_hiss;
            }
            hiss.clip = clip;
            hiss.loop = true;
            hiss.playOnAwake = false;
            hiss.spatialBlend = 1f;
            hiss.minDistance = 1f;
            hiss.maxDistance = 9f;
            hiss.rolloffMode = AudioRolloffMode.Linear;
            hiss.volume = 0.25f;
            GameSettings.RouteEffect(hiss);
        }
    }

    protected override void Restore() => SetOn(ArmedNow);

    private void SetOn(bool on)
    {
        _on = on;
        if (screen != null) screen.enabled = on;
        if (hiss == null) return;
        if (on) { if (!hiss.isPlaying && hiss.isActiveAndEnabled) hiss.Play(); }
        else hiss.Stop();
    }

    protected override void Tick(AsylumHub hub)
    {
        if (!_on || _material == null || Time.timeScale <= 0f || Time.time < _nextStep) return;
        _nextStep = Time.time + (FlashSafety.On ? Mathf.Max(staticStep, 0.25f) : staticStep); // F118: the static jumps at most 4 times a second
        _material.mainTextureOffset = new Vector2(Random.value, Random.value);
    }

    protected override bool Triggered(AsylumHub hub, float dt)
    {
        if (target != null && Vector3.Distance(hub.PlayerCamera.transform.position, target.position) <= nearDistance) return true;
        return base.Triggered(hub, dt);
    }

    protected override IEnumerator Play()
    {
        Vector3 at = target != null ? target.position : transform.position;
        SetOn(false);
        if (s_click == null) s_click = DoorAudio.BuildClickClip();
        GameAudio.ClipAtPoint(s_click, at, 0.7f);

        yield return new WaitForSeconds(0.8f);

        AsylumHub hub = Hub;
        if (hub != null && hub.PlayerCamera != null)
        {
            Transform cam = hub.PlayerCamera.transform;
            GameAudio.PlayAt(GameAudio.Library.whisperEcho, cam.position - cam.forward * 1.2f, 0.7f);
        }
    }

    private void OnDestroy()
    {
        if (_material != null) Destroy(_material);
    }
}
