using System.Collections;
using UnityEngine;

/// <summary>
/// F101: the empty wheelchair behind the statue's bay (floor 4 and later). The first time per hub visit the player comes within
/// `triggerRange` and looks its way, it creaks round `degrees` over `seconds` with the hub's door-creak sound, and stops. Nothing else:
/// no damage, no noise for the hunters, no subtitle. It never fires while a hub pursuer is after the player or a transition runs;
/// each hub entry puts the chair back and re-arms it. The chair must not be static (it moves). Put this on the pose object that is
/// only enabled from floor 4 (see HubVignette), so it does not exist on earlier floors.
/// </summary>
public class HubCreakScare : MonoBehaviour
{
    [SerializeField] private Transform chair;
    [SerializeField] private float triggerRange = 4f;
    [Tooltip("Dot of the camera's forward with the direction to the chair above which the player counts as looking at it.")]
    [SerializeField] private float lookDot = 0.55f;
    [SerializeField] private float degrees = 30f;
    [SerializeField] private float seconds = 0.8f;

    private Quaternion _start;
    private bool _haveStart;
    private bool _fired;
    private Coroutine _routine;

    private void OnEnable()
    {
        if (chair != null)
        {
            if (!_haveStart) { _start = chair.localRotation; _haveStart = true; }
            chair.localRotation = _start;
        }
        _fired = false;
        AsylumHub.OnEnterHub += HandleEnter;
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        if (chair != null && _haveStart) chair.localRotation = _start;
    }

    private void HandleEnter(int floor)
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        if (chair != null && _haveStart) chair.localRotation = _start;
        _fired = false;
    }

    private void Update()
    {
        if (_fired || chair == null) return;
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.Player == null || hub.PlayerCamera == null || hub.IsTransitioning || hub.Leaving) return;
        if (HubState.IsFinale || HubState.Floor < 4) return;
        if (hub.PursuerAfterPlayer()) return;

        Vector3 to = chair.position + Vector3.up * 0.6f - hub.PlayerCamera.transform.position;
        float distance = to.magnitude;
        if (distance > triggerRange) return;
        if (Vector3.Dot(hub.PlayerCamera.transform.forward, to / Mathf.Max(0.01f, distance)) < lookDot) return;

        _fired = true;
        _routine = StartCoroutine(Creak());
    }

    private IEnumerator Creak()
    {
        if (!GameAudio.PlayAt(GameAudio.Library.exitDoorCreak, chair.position, 0.8f)) GameAudio.PlayAt(GameAudio.Library.whisperEcho, chair.position, 0.5f);
        Quaternion from = chair.localRotation;
        Quaternion to = from * Quaternion.Euler(0f, degrees, 0f);
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            chair.localRotation = Quaternion.Slerp(from, to, Mathf.SmoothStep(0f, 1f, t / seconds));
            yield return null;
        }
        chair.localRotation = to;
        _routine = null;
    }
}
