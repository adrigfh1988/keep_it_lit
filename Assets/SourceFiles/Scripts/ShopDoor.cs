using TMPro;
using UnityEngine;

/// <summary>
/// F94: the SHOP door in the asylum hub - the way into the shop room between floors (the salesman moved into the hospital). It is
/// open once a floor has been cleared (GameFlow.CurrentFloor >= 2: before that the player has no shards and the board no offers) and
/// outside the finale; otherwise E answers with a padlock clunk and a line, at most every 2 s. Modelled on <see cref="WingDoor"/>:
/// the leaf hangs under <see cref="leafHinge"/> (never static) and is purely the walk-in visual. E while open calls
/// AsylumHub.EnterShop; the shop's own door walks the player back to <see cref="arrivalPoint"/>.
/// </summary>
public class ShopDoor : MonoBehaviour, IInteractable
{
    [Header("Parts")]
    public TMP_Text plaque;
    public Light beacon;
    public Transform leafHinge;
    [Tooltip("Second leaf of a double door (hinged on its far edge). Null for a single door.")]
    public Transform leafHingeB;
    [Tooltip("1.5 m in front of the door, facing away from it. Where the player comes back out of the shop.")]
    public Transform arrivalPoint;

    [Header("Look")]
    public float beaconIntensity = 1.4f;

    private static readonly Color OpenColor = new Color(1f, 0.78f, 0.45f);
    private static readonly Color ClosedColor = new Color(0.45f, 0.4f, 0.35f);

    private float _nudgeTimer;
    private AudioClip _padlockClip;

    public bool IsComplete => plaque != null && beacon != null && leafHinge != null && arrivalPoint != null;

    /// <summary>A floor has been cleared and the finale is not running.</summary>
    public bool IsOpen => GameFlow.CurrentFloor >= 2 && !HubState.IsFinale;

    // ---------------------------------------------------------------- IInteractable

    public string Prompt => IsOpen ? "ENTER SHOP" : "SHOP · CLOSED";

    // Always focusable: a closed door still answers E.
    public bool CanInteract => true;
    public float HoldSeconds => 0f;
    public void OnFocus(bool focused) { }

    public void Interact(PlayerInteractor who)
    {
        if (IsOpen)
        {
            if (AsylumHub.Active != null) AsylumHub.Active.EnterShop(this);
            return;
        }

        if (_nudgeTimer > 0f) return;
        _nudgeTimer = 2f;
        if (_padlockClip == null) _padlockClip = DoorAudio.BuildClunkClip(0.18f, 160f, 0.35f);
        AudioSource.PlayClipAtPoint(_padlockClip, transform.position, 0.7f);
        PlayerHud hud = Object.FindAnyObjectByType<PlayerHud>();
        if (hud != null) hud.ShowSubtitle("CLOSED · COME BACK WITH SHARDS", 2.2f);
    }

    // ---------------------------------------------------------------- state

    private void OnEnable() => Refresh();

    private void Update()
    {
        if (_nudgeTimer > 0f) _nudgeTimer -= Time.deltaTime;

        if (beacon != null && IsOpen)
        {
            beacon.enabled = true;
            beacon.intensity = beaconIntensity * (0.75f + 0.25f * Mathf.Sin(Time.time * 2.4f));
        }
    }

    /// <summary>Re-reads GameFlow and repaints the plaque and beacon. Called whenever the hub switches on.</summary>
    public void Refresh()
    {
        bool open = IsOpen;
        Color color = open ? OpenColor : ClosedColor;

        if (plaque != null)
        {
            plaque.text = $"SHOP\n<size=60%>THE SALESMAN  ·  {(open ? "OPEN" : "CLOSED")}</size>";
            plaque.color = color;
        }

        // One light only: off while closed (the hub's light budget has no room for a second one).
        if (beacon != null)
        {
            beacon.color = color;
            beacon.enabled = open;
            beacon.intensity = beaconIntensity;
        }
    }
}
