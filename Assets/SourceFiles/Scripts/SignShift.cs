using UnityEngine;

/// <summary>
/// F109 (D8, hub only): a room plate that reads something else once the story is deep enough. From floor <c>minFloor</c> on, the first time during a
/// hub visit that the plate is out of the camera view and the player is at least <c>minDistance</c> away, its LocalizedText key becomes
/// <c>altKey</c> (DORMITORY becomes YOUR ROOM). It goes back to its own key every time the hub switches on, so it happens at most once per visit.
/// Instance state only: no save, no statics; a scene reload resets it.
/// </summary>
[RequireComponent(typeof(LocalizedText))]
[DisallowMultipleComponent]
public class SignShift : MonoBehaviour
{
    [SerializeField] private string altKey = "";
    [SerializeField] private int minFloor = 3;
    [SerializeField] private float minDistance = 8f;

    private LocalizedText _text;
    private string _ownKey;
    private bool _shifted;

    private void Awake()
    {
        _text = GetComponent<LocalizedText>();
        _ownKey = _text.Key;
    }

    private void OnEnable()
    {
        if (_text == null) _text = GetComponent<LocalizedText>();
        if (_ownKey == null) _ownKey = _text.Key;
        _shifted = false;
        if (_text.Key != _ownKey && !string.IsNullOrEmpty(_ownKey)) _text.Key = _ownKey;
    }

    private void Update()
    {
        if (_shifted || string.IsNullOrEmpty(altKey)) return;
        if (GameFlow.CurrentFloor < minFloor) return;
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.PlayerCamera == null) return;
        Camera cam = hub.PlayerCamera;
        Vector3 p = transform.position;
        if ((cam.transform.position - p).magnitude < minDistance) return;
        Vector3 vp = cam.WorldToViewportPoint(p);
        bool inView = vp.z > 0f && vp.x > -0.15f && vp.x < 1.15f && vp.y > -0.15f && vp.y < 1.15f;
        if (inView) return;
        _shifted = true;
        _text.Key = altKey;
    }
}
