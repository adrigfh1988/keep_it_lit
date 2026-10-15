using UnityEngine;

/// <summary>
/// F116: the Commissary's door while the shop is shut (floor 1 and the finale: nothing has been cleared yet, or nothing is left to sell). HubShop
/// swings the leaf shut and enables this object's collider, which both blocks the doorway and is what E finds: a padlock clunk and a line, at most
/// every 2 s. While the shop is open the collider is off and this answers nothing.
/// </summary>
public class HubShopDoor : MonoBehaviour, IInteractable
{
    [Tooltip("Solid collider across the doorway, enabled only while the door is shut. The pack's own leaf collider already closes the gap; this is the E target and a safety net.")]
    [SerializeField] private Collider blocker;

    private bool _closed;
    private float _nudgeTimer;
    private AudioClip _padlockClip;

    public string Prompt => L10n.T("hub.shopdoor.closed");
    public bool CanInteract => _closed && GameFlow.IsInHub;
    public float HoldSeconds => 0f;
    public void OnFocus(bool focused) { }

    /// <summary>HubShop.Refresh: true while the shop is shut.</summary>
    public void SetClosed(bool closed)
    {
        _closed = closed;
        if (blocker != null) blocker.enabled = closed;
    }

    private void Update()
    {
        if (_nudgeTimer > 0f) _nudgeTimer -= Time.deltaTime;
    }

    public void Interact(PlayerInteractor who)
    {
        if (!_closed || _nudgeTimer > 0f) return;
        _nudgeTimer = 2f;
        if (_padlockClip == null) _padlockClip = DoorAudio.BuildClunkClip(0.18f, 160f, 0.35f);
        if (!GameAudio.PlayAt(GameAudio.Library.lockedRattle, transform.position, 0.7f)) GameAudio.ClipAtPoint(_padlockClip, transform.position, 0.7f); // F97
        if (AsylumHub.Active != null && AsylumHub.Active.Hud != null) AsylumHub.Active.Hud.ShowSubtitle(L10n.T("hub.shopdoor.closed_line"), 2.2f);
    }

    private void OnDestroy()
    {
        if (_padlockClip != null) Destroy(_padlockClip);
    }
}
