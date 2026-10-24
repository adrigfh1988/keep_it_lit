using System.Collections;
using UnityEngine;

/// <summary>
/// F114 R6 (the guard room): the best of the seven. The PC monitor shows a live feed of the corridor outside the guard room door, from a
/// camera the builder mounted high in the corridor. While the player is inside the guard room the camera renders by hand at 10 Hz into a
/// small RenderTexture (made at runtime, 256 x 160); outside it stops and the last frame stays. On the armed floors, when the player looks
/// at the screen from within 2.5 m for a second, a figure - the pursuer's body on the "CctvOnly" layer - and a cold fill light appear for
/// 2.5 s in the doorway right behind the player, visible ONLY on the monitor; a heavy step is heard at 1.2 s; at 2.5 s the screen flashes
/// static for 0.6 s, the figure and light go, and the live feed returns. Nothing is ever in the real doorway.
/// The "CctvOnly" layer must stay OUT of the player camera's culling mask: this removes it on every hub entry and every frame it is found set.
/// </summary>
public class ScareCctv : HubRoomScare
{
    /// <summary>The layer the figure lives on (added to ProjectSettings/TagManager.asset by the builder).</summary>
    public const string LayerName = "CctvOnly";

    [Tooltip("The quad on the monitor's screen face.")]
    [SerializeField] private Renderer screen;
    [Tooltip("The corridor camera. Disabled; rendered by hand into the RenderTexture.")]
    [SerializeField] private Camera cctvCamera;
    [Tooltip("The figure standing in the doorway on the corridor side. Inactive until the scare; every renderer on the CctvOnly layer.")]
    [SerializeField] private GameObject figure;
    [Tooltip("A cold light at the figure's head height. Disabled until the scare.")]
    [SerializeField] private Light fill;
    [Tooltip("The static shown for a moment at the end (the same noise texture the kitchen television uses).")]
    [SerializeField] private Texture staticTexture;
    [SerializeField] private int textureWidth = 256;
    [SerializeField] private int textureHeight = 160;
    [SerializeField, Min(1f)] private float framesPerSecond = 10f;

    private RenderTexture _texture;
    private Material _material;
    private float _nextFrame;
    private bool _staticPhase;
    private static AudioClip s_step;

    public override bool IsComplete => base.IsComplete && screen != null && cctvCamera != null && figure != null && fill != null && staticTexture != null && target != null;
    public Camera CctvCamera => cctvCamera;
    public GameObject Figure => figure;
    public Renderer Screen => screen;
    public RenderTexture Feed => _texture;

    /// <summary>The layer index of <see cref="LayerName"/>, or -1 if the project has no such layer.</summary>
    public static int Layer => LayerMask.NameToLayer(LayerName);

    protected override void Capture()
    {
        if (_texture != null || screen == null || cctvCamera == null) return;
        _texture = new RenderTexture(textureWidth, textureHeight, 16, RenderTextureFormat.Default) { name = "CctvFeed", antiAliasing = 1 };
        _texture.Create();
        _material = screen.material;
        _material.SetTexture("_BaseMap", _texture);
        cctvCamera.targetTexture = _texture;
        cctvCamera.enabled = false;
        int layer = Layer;
        if (layer >= 0 && fill != null) fill.cullingMask = 1 << layer;
    }

    protected override void Restore()
    {
        _staticPhase = false;
        if (figure != null) figure.SetActive(false);
        if (fill != null) fill.enabled = false;
        if (_material != null && _texture != null) _material.SetTexture("_BaseMap", _texture);
        HideFromPlayerCamera();
    }

    /// <summary>The main view must never show the figure: remove the layer from the player camera's mask.</summary>
    private void HideFromPlayerCamera()
    {
        AsylumHub hub = Hub;
        int layer = Layer;
        if (hub == null || hub.PlayerCamera == null || layer < 0) return;
        int bit = 1 << layer;
        if ((hub.PlayerCamera.cullingMask & bit) != 0) hub.PlayerCamera.cullingMask &= ~bit;
    }

    protected override void Tick(AsylumHub hub)
    {
        HideFromPlayerCamera();
        if (_texture == null || cctvCamera == null || _staticPhase || Time.timeScale <= 0f) return;
        if (!InRoom(hub) || Time.unscaledTime < _nextFrame) return;
        _nextFrame = Time.unscaledTime + 1f / framesPerSecond;
        cctvCamera.Render();
    }

    protected override IEnumerator Play()
    {
        figure.SetActive(true);
        fill.enabled = true;

        yield return new WaitForSeconds(1.2f);
        if (s_step == null) s_step = DoorAudio.BuildClunkClip(0.12f, 90f, 0.3f);
        if (!GameAudio.PlayAt(GameAudio.Library.timmyStep, figure.transform.position, 0.6f)) GameAudio.ClipAtPoint(s_step, figure.transform.position, 0.6f);

        yield return new WaitForSeconds(1.3f);
        _staticPhase = true;
        _material.SetTexture("_BaseMap", staticTexture);
        figure.SetActive(false);
        fill.enabled = false;

        yield return new WaitForSeconds(0.6f);
        _material.SetTexture("_BaseMap", _texture);
        _staticPhase = false;
    }

    private void OnDestroy()
    {
        if (cctvCamera != null) cctvCamera.targetTexture = null;
        if (_texture != null) { _texture.Release(); Destroy(_texture); }
        if (_material != null) Destroy(_material);
    }
}
