using System.Collections;
using UnityEngine;

/// <summary>
/// F93 (E10): the asylum's main doors, the way out in ending A (DISCHARGED). E at them: the player is frozen, the door swings open away
/// from the hall (a hinge made at runtime by ShopRoom.CreateDoorHinge on the pack's MainDoor leaf, which is never static), they are eased
/// up to the doorway looking through it while the screen goes a cold grey-white, and the ending completes. Only offered in the finale.
/// </summary>
public class HubExitDoor : MonoBehaviour, IInteractable
{
    [Tooltip("The pack's MainDoor mesh (AsylumHub/Building/.../MainDoor).")]
    [SerializeField] private Transform leaf;
    [SerializeField] private float openAngle = 100f;

    private HubFinale _owner;
    private Transform _hinge;

    public void Bind(HubFinale owner) { _owner = owner; }

    string IInteractable.Prompt => L10n.T("hub.exit.prompt");
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => HubState.IsFinale && _owner != null && !_owner.Finished;

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_owner != null) _owner.Discharge();
    }

    void IInteractable.OnFocus(bool focused) { }

    /// <summary>The walk-out. `done` runs at the end (GameOutcome.CompleteFinale).</summary>
    public IEnumerator WalkOut(System.Action done)
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.Player == null) { done?.Invoke(); yield break; }
        hub.SetScripted(true);
        if (hub.Controller != null) hub.Controller.MovementLocked = true;
        if (hub.Hud != null) { hub.Hud.SetPrompt(null); hub.Hud.SetFocusPrompt(null); }

        Transform player = hub.Player;
        Vector3 doorway = leaf != null ? leaf.position : transform.position;
        Vector3 into = leaf != null ? -transform.forward : Vector3.back; // out of the hall
        into.y = 0f;
        into = into.sqrMagnitude > 0.01f ? into.normalized : Vector3.back;
        Vector3 start = player.position;
        Vector3 threshold = new Vector3(doorway.x, start.y, doorway.z) - into * 0.9f;
        Vector3 stop = new Vector3(doorway.x, start.y, doorway.z) + into * 0.6f;
        Vector3 Look(Vector3 from) => from + into * 4f + Vector3.up * 1.4f;

        if (leaf != null && _hinge == null) _hinge = ShopRoom.CreateDoorHinge(hub.building, leaf);
        Quaternion closed = _hinge != null ? _hinge.localRotation : Quaternion.identity;
        float sign = _hinge != null ? SwingSign(_hinge, into) : 1f;
        Quaternion opened = closed * Quaternion.Euler(0f, sign * openAngle, 0f);
        if (!GameAudio.PlayAt(GameAudio.Library.exitDoorCreak, doorway, 1f)) GameAudio.ClipAtPoint(DoorAudio.BuildCreakClip(1.4f, 120f, 4), doorway, 1f); // F97

        float t = 0f;
        while (t < 1.4f)
        {
            t += Time.deltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 1.4f));
            if (_hinge != null) _hinge.localRotation = Quaternion.Slerp(closed, opened, k);
            hub.MovePlayerTo(Vector3.Lerp(start, threshold, k));
            if (hub.Controller != null) hub.Controller.LookAt(Look(player.position), 1f - Mathf.Exp(-8f * Time.deltaTime));
            yield return null;
        }

        // Out: the grey-white swallows the screen as they cross the threshold.
        StartCoroutine(hub.OverlayTo(new Color(0.78f, 0.82f, 0.86f), 1f, 2.2f));
        t = 0f;
        while (t < 2.2f)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / 2.2f);
            hub.MovePlayerTo(Vector3.Lerp(threshold, stop, 1f - (1f - k) * (1f - k)));
            if (hub.Controller != null) hub.Controller.LookAt(Look(player.position), 1f - Mathf.Exp(-8f * Time.deltaTime));
            yield return null;
        }
        yield return new WaitForSecondsRealtime(0.5f);
        done?.Invoke();
    }

    private static float SwingSign(Transform hinge, Vector3 into)
    {
        Renderer[] renderers = hinge.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return 1f;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);
        Vector3 offset = b.center - hinge.position;
        float plus = Vector3.Dot(Quaternion.AngleAxis(90f, hinge.up) * offset - offset, into);
        float minus = Vector3.Dot(Quaternion.AngleAxis(-90f, hinge.up) * offset - offset, into);
        return plus >= minus ? 1f : -1f;
    }
}
