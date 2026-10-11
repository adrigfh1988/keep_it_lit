using UnityEngine;

/// <summary>
/// F109 (D8): a hanging board swings a little on its chains: a gentle yaw/roll sine (about 1.5 degrees, 0.2-0.35 Hz, random phase) and a small
/// kick when the player walks past within 1.5 m. Rotates about <c>pivotLocal</c> (the chain anchor, in this object own space). Runs on scaled
/// time, so a paused game holds still. The object must not be static.
/// </summary>
[DisallowMultipleComponent]
public class SignSway : MonoBehaviour
{
    [SerializeField] private Vector3 pivotLocal = new Vector3(0f, 0.5f, 0f);
    [SerializeField] private float amplitude = 1.5f;
    [SerializeField] private Vector2 frequency = new Vector2(0.2f, 0.35f);
    [SerializeField] private float kickRadius = 1.5f;
    [SerializeField] private float kickDegrees = 3f;

    private Vector3 _basePos;
    private Quaternion _baseRot;
    private float _freq, _phase;
    private float _kick, _kickStart = -99f, _kickCooldown;
    private Camera _cam;
    private bool _near;

    public Vector3 PivotLocal { get => pivotLocal; set => pivotLocal = value; }

    private void OnEnable()
    {
        _basePos = transform.localPosition;
        _baseRot = transform.localRotation;
        _freq = Random.Range(frequency.x, frequency.y);
        _phase = Random.value * 6.2832f;
    }

    private void OnDisable()
    {
        transform.localPosition = _basePos;
        transform.localRotation = _baseRot;
    }

    private void Update()
    {
        float t = Time.time;
        if (_cam == null) _cam = Camera.main;
        if (_cam != null)
        {
            Vector3 anchor = transform.TransformPoint(pivotLocal);
            bool near = new Vector2(_cam.transform.position.x - anchor.x, _cam.transform.position.z - anchor.z).magnitude < kickRadius;
            if (near && !_near && t > _kickCooldown) { _kickStart = t; _kickCooldown = t + 4f; _kick = kickDegrees; }
            _near = near;
        }
        float swing = Mathf.Sin(t * Mathf.PI * 2f * _freq + _phase) * amplitude;
        float since = t - _kickStart;
        float kick = since < 8f ? _kick * Mathf.Exp(-since * 0.9f) * Mathf.Sin(since * 3.2f) : 0f;
        Quaternion d = Quaternion.Euler(0f, (swing + kick) * 0.6f, swing * 0.8f + kick);
        Quaternion rot = _baseRot * d;
        Vector3 pivotParent = _basePos + _baseRot * pivotLocal;
        transform.localRotation = rot;
        transform.localPosition = pivotParent - rot * pivotLocal;
    }
}
