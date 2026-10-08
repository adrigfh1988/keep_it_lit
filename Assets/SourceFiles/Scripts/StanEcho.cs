using UnityEngine;

/// <summary>
/// F93 (E9, floor 4 only): an echo of Stan in the asylum. A clone of the Stalker template stripped to its visuals (the Animator frozen
/// on a pose) stands at the far end of the upper hall. Each time it has been out of the player's view for 3 s and the player is 10 m or
/// more away, it moves one point closer along AsylumHub/Life/EchoRoute/*. When the torch beam finds it within 6 m it is gone for good
/// (this visit) with a whisper. Harmless: no collider, no catch, no alert.
/// </summary>
public class StanEcho : HubSetPiece
{
    [SerializeField] private Transform body;
    [SerializeField] private Transform[] points = new Transform[0];
    [SerializeField] private float unseenSeconds = 3f;
    [SerializeField] private float minPlayerDistance = 10f;
    [SerializeField] private float torchRange = 6f;

    private int _index;
    private float _unseen;
    private bool _gone;
    private AudioClip _whisper;

    public bool IsComplete => body != null && points != null && points.Length >= 2;

    protected override void OnBegin()
    {
        if (!IsComplete)
        {
            Debug.LogError("StanEcho: the body or the echo route is missing. Run LIGHTS OUT > Build Missing Pieces.", this);
            return;
        }
        _index = 0;
        _unseen = 0f;
        _gone = false;
        Freeze();
        Place();
        body.gameObject.SetActive(true);
    }

    protected override void OnEnd()
    {
        if (body != null) body.gameObject.SetActive(false);
    }

    private void Freeze()
    {
        Animator animator = body.GetComponentInChildren<Animator>(true);
        if (animator != null) animator.speed = 0f;
    }

    private void Place()
    {
        body.position = points[_index].position;
        Transform look = points[Mathf.Min(_index + 1, points.Length - 1)];
        Vector3 d = look.position - body.position;
        d.y = 0f;
        if (_index == points.Length - 1 && Hub != null && Hub.Player != null) d = Hub.Player.position - body.position;
        d.y = 0f;
        if (d.sqrMagnitude > 0.01f) body.rotation = Quaternion.LookRotation(d);
    }

    private void Update()
    {
        if (!Running || _gone || !IsComplete) return;
        AsylumHub hub = Hub;
        if (hub == null || hub.Player == null || hub.PlayerCamera == null || hub.IsTransitioning || Time.timeScale <= 0f) return;

        Vector3 head = body.position + Vector3.up * 1.4f;
        Camera cam = hub.PlayerCamera;
        Vector3 vp = cam.WorldToViewportPoint(head);
        bool inFrustum = vp.z > 0f && vp.x > -0.05f && vp.x < 1.05f && vp.y > -0.05f && vp.y < 1.05f;
        bool visible = inFrustum && !Physics.Linecast(cam.transform.position, head, ~0, QueryTriggerInteraction.Ignore);

        float distance = Vector3.Distance(hub.Player.position, body.position);

        // The torch finds it: gone for good.
        Flashlight torch = hub.Torch;
        if (torch != null && torch.IsOn && distance <= torchRange && visible)
        {
            Vector3 to = head - cam.transform.position;
            if (Vector3.Angle(cam.transform.forward, to) <= torch.OuterAngle * 0.5f + 4f && distance <= torch.Range)
            {
                Vanish();
                return;
            }
        }

        if (visible) { _unseen = 0f; return; }
        _unseen += Time.deltaTime;
        if (_unseen >= unseenSeconds && distance >= minPlayerDistance && _index < points.Length - 1)
        {
            _index++;
            _unseen = 0f;
            Place();
        }
    }

    private void Vanish()
    {
        _gone = true;
        if (_whisper == null) _whisper = DoorAudio.BuildClunkClip(0.9f, 300f, 0.95f);
        AudioSource.PlayClipAtPoint(_whisper, body.position, 0.35f);
        body.gameObject.SetActive(false);
    }
}
