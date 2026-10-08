using UnityEngine;

/// <summary>
/// F93 (E10): a figure that only exists in the finale - Director Karlheinz standing by his desk, a memory. It plays Mixamo Look Around, goes
/// into Talking when the player comes close, and slowly turns to face them. It has no voice and no collision; it is present only while
/// HubState.IsFinale. (Hilde, Tomas and Elias are HubPatients; this is for the staff.)
/// </summary>
public class HubMemoryFigure : MonoBehaviour
{
    [SerializeField] private Transform body;
    [SerializeField] private RuntimeAnimatorController controller;
    [SerializeField] private float turnDegreesPerSecond = 65f;
    [SerializeField] private float noticeRange = 9f;
    [SerializeField] private float talkRange = 4f;

    private Animator _animator;
    private string _state;
    private bool _on;

    public bool IsComplete => body != null;

    private void Awake()
    {
        if (body == null) return;
        _animator = body.GetComponent<Animator>();
        if (_animator != null)
        {
            if (controller != null) _animator.runtimeAnimatorController = controller;
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            _animator.applyRootMotion = false;
        }
    }

    private void OnEnable()
    {
        AsylumHub.OnEnterHub += HandleEnter;
        Refresh();
    }

    private void OnDisable() { AsylumHub.OnEnterHub -= HandleEnter; }

    private void HandleEnter(int floor) => Refresh();

    private void Refresh()
    {
        _on = HubState.IsFinale;
        if (body != null) body.gameObject.SetActive(_on);
        _state = null;
        Play("Look");
    }

    private void Play(string state)
    {
        if (_animator == null || !_animator.isActiveAndEnabled || _animator.runtimeAnimatorController == null || _state == state) return;
        _state = state;
        _animator.CrossFadeInFixedTime(state, 0.35f);
    }

    private void Update()
    {
        if (!_on || body == null) return;
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.Player == null) return;
        Vector3 to = hub.Player.position - body.position;
        float dist = new Vector3(to.x, 0f, to.z).magnitude;
        Play(dist < talkRange ? "Talking" : "Look");
        if (dist < noticeRange && dist > 0.2f)
        {
            Quaternion want = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z), Vector3.up);
            body.rotation = Quaternion.RotateTowards(body.rotation, want, turnDegreesPerSecond * Time.deltaTime);
        }
    }
}
