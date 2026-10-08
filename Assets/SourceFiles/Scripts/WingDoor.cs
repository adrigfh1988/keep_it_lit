using TMPro;
using UnityEngine;

/// <summary>
/// F92: one of the five wing doors in the asylum, each the way into a maze floor. State derives from
/// GameFlow.CurrentFloor only (decision D12): below it the wing is CLEARED, at it the door is OPEN (beacon pulsing, plaque
/// lit), above it SEALED (E gives a padlock clunk and a reminder, at most every 2 s). The "key" is narrative only.
/// The door leaf hangs under <see cref="leafHinge"/> (made with ShopRoom.CreateDoorHinge, never static) and is
/// purely the walk-in visual - the wall is behind it.
/// </summary>
public class WingDoor : MonoBehaviour, IInteractable
{
    public enum DoorState { Sealed, Open, Cleared }

    [Header("Identity")]
    [Range(1, 5)] public int floor = 1;
    [Tooltip("Plaque title, e.g. WARD A")]
    public string wingName = "WARD A";
    [Tooltip("Plaque second line, e.g. DAY ROOM")]
    public string roomName = "DAY ROOM";
    [Tooltip("Where it is, for the arrival subtitle, e.g. GROUND FLOOR")]
    public string levelName = "GROUND FLOOR";

    [Header("Parts")]
    public TMP_Text plaque;
    public Light beacon;
    public Transform leafHinge;
    [Tooltip("Second leaf of a double door (hinged on its far edge). Null for a single door.")]
    public Transform leafHingeB;
    [Tooltip("1.5 m in front of the door, facing away from it.")]
    public Transform arrivalPoint;

    [Header("Look")]
    public float beaconIntensity = 1.4f;

    private static readonly Color OpenColor = new Color(1f, 0.78f, 0.45f);
    private static readonly Color ClearedColor = new Color(0.55f, 0.85f, 0.6f);
    private static readonly Color SealedColor = new Color(0.85f, 0.2f, 0.15f);

    /// <summary>F93: a story piece (key run, chase) locks the open door by setting the line E answers with; null/empty = usable. Cleared when the hub is left.</summary>
    [System.NonSerialized] public string lockedLine;
    public bool StoryLocked => !string.IsNullOrEmpty(lockedLine);

    private float _nudgeTimer;
    private AudioClip _padlockClip;

    public bool IsComplete => floor >= 1 && floor <= 5 && plaque != null && beacon != null && leafHinge != null && arrivalPoint != null;

    public DoorState State
    {
        get
        {
            int current = GameFlow.CurrentFloor;
            return floor < current ? DoorState.Cleared : floor == current ? DoorState.Open : DoorState.Sealed;
        }
    }

    // ---------------------------------------------------------------- IInteractable

    public string Prompt
    {
        get
        {
            switch (State)
            {
                case DoorState.Open: return StoryLocked ? $"{wingName} · LOCKED" : $"ENTER {wingName}";
                case DoorState.Cleared: return $"{wingName} · CLEARED";
                default: return $"{wingName} · SEALED";
            }
        }
    }

    // Always focusable: a sealed or cleared door still answers E.
    public bool CanInteract => true;
    public float HoldSeconds => 0f;
    public void OnFocus(bool focused) { }

    public void Interact(PlayerInteractor who)
    {
        PlayerHud hud = Object.FindAnyObjectByType<PlayerHud>();
        switch (State)
        {
            case DoorState.Open:
                if (StoryLocked)
                {
                    if (_nudgeTimer > 0f) break;
                    _nudgeTimer = 2f;
                    if (_padlockClip == null) _padlockClip = DoorAudio.BuildClunkClip(0.18f, 160f, 0.35f);
                    AudioSource.PlayClipAtPoint(_padlockClip, transform.position, 0.7f);
                    hud?.ShowSubtitle(lockedLine, 2.6f);
                    break;
                }
                if (AsylumHub.Active != null) AsylumHub.Active.EnterWing(this);
                break;

            case DoorState.Cleared:
                hud?.ShowSubtitle($"{wingName} · ALREADY CLEARED", 1.8f);
                break;

            default:
                if (_nudgeTimer > 0f) break;
                _nudgeTimer = 2f;
                if (_padlockClip == null) _padlockClip = DoorAudio.BuildClunkClip(0.18f, 160f, 0.35f);
                AudioSource.PlayClipAtPoint(_padlockClip, transform.position, 0.7f);
                hud?.ShowSubtitle($"SEALED · YOU NEED THE {PreviousWingName()} KEY", 2.2f);
                break;
        }
    }

    // ---------------------------------------------------------------- state

    private void OnEnable() => Refresh();

    private void Update()
    {
        if (_nudgeTimer > 0f) _nudgeTimer -= Time.deltaTime;

        if (beacon == null) return;
        if (State == DoorState.Open)
        {
            beacon.enabled = true;
            beacon.intensity = beaconIntensity * (0.75f + 0.25f * Mathf.Sin(Time.time * 2.4f));
        }
    }

    /// <summary>Re-reads GameFlow and repaints the plaque and beacon. Called whenever the hub switches on.</summary>
    public void Refresh()
    {
        DoorState state = State;
        Color color = state == DoorState.Open ? OpenColor : state == DoorState.Cleared ? ClearedColor : SealedColor;

        if (plaque != null)
        {
            string status = state == DoorState.Open ? "OPEN" : state == DoorState.Cleared ? "CLEARED" : "SEALED";
            plaque.text = $"{wingName}\n<size=60%>{roomName}  ·  {status}</size>";
            plaque.color = color;
        }

        if (beacon != null)
        {
            beacon.color = color;
            beacon.enabled = state != DoorState.Sealed;
            beacon.intensity = state == DoorState.Open ? beaconIntensity : beaconIntensity * 0.35f;
        }
    }

    private string PreviousWingName()
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub != null && hub.wingDoors != null && floor >= 2 && floor - 2 < hub.wingDoors.Length && hub.wingDoors[floor - 2] != null)
        {
            return hub.wingDoors[floor - 2].wingName;
        }
        return "PREVIOUS WING";
    }
}
