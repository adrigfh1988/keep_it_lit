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

    // S9 (plan section 6): a story piece (a mission, the key run) locks the open door by OWNING the lock - the door no longer compares line
    // texts, so a translated line cannot break "who may unlock". The line is a string KEY resolved when E answers; the serialized English
    // text is the fallback while a key is missing. Cleared by its owner when the hub is left.
    [System.NonSerialized] public object lockOwner;
    [System.NonSerialized] private string _lockKey;
    [System.NonSerialized] private string _lockFallback;
    public bool StoryLocked => lockOwner != null;

    /// <summary>F93/S9: owner locks the open door. E answers with L10n.T(lineKey) (or fallbackText while the key is missing).</summary>
    public void Lock(object owner, string lineKey, string fallbackText)
    {
        lockOwner = owner;
        _lockKey = lineKey;
        _lockFallback = fallbackText;
    }

    /// <summary>Unlocks the door - only when owner is the one that locked it (never clobbers another piece's lock).</summary>
    public void Unlock(object owner)
    {
        if (lockOwner == null || !ReferenceEquals(lockOwner, owner)) return;
        lockOwner = null;
        _lockKey = null;
        _lockFallback = null;
    }

    /// <summary>The line E answers with while the door is story-locked (translated at the moment it is shown).</summary>
    public string LockedLine => !string.IsNullOrEmpty(_lockKey) && L10n.Has(_lockKey) ? L10n.T(_lockKey) : (_lockFallback ?? "");

    // ---------------------------------------------------------------- S9 (D10, D12): translated names and per-wing sentences

    private string Key(string leaf) => $"hub.wing.{floor}.{leaf}";
    private string Line(string leaf, string fallback, params (string name, object value)[] args) =>
        L10n.Has(Key(leaf)) ? L10n.T(Key(leaf), args) : fallback;

    /// <summary>Plaque title, translated by stable id (hub.wing.n.name); the serialized field is the fallback.</summary>
    public string WingName => Line("name", wingName);
    public string RoomName => Line("room", roomName);
    public string LevelName => Line("level", levelName);

    /// <summary>"GO TO THE WARD A DOOR · GROUND FLOOR" (floor 5: the office). A whole sentence per wing (D12: an article glued to a name).</summary>
    public string GoToLine => Line("go_to_door", floor == 5 ? $"GO TO THE {roomName} · {levelName}" : $"GO TO THE {wingName} DOOR · {levelName}");

    /// <summary>Arrival line: "FLOOR 1 · THE WARD A DOOR IS OPEN / LOCKED · GROUND FLOOR".</summary>
    public string ArrivalLine(bool locked) => locked
        ? Line("door_locked", $"FLOOR {floor} · THE {wingName} DOOR IS LOCKED · {levelName}", ("floor", floor))
        : Line("door_open", $"FLOOR {floor} · THE {wingName} DOOR IS OPEN · {levelName}", ("floor", floor));

    /// <summary>"THE WARD A DOOR IS UNLOCKED · GROUND FLOOR" (a mission has handed over the key).</summary>
    public string UnlockedLine => Line("door_unlocked", $"THE {wingName} DOOR IS UNLOCKED · {levelName}");

    /// <summary>The hint loop's plain location line: "WARD A · DAY ROOM · GROUND FLOOR".</summary>
    public string HintLine => L10n.Has("hub.wing.hint") ? L10n.T("hub.wing.hint", ("wing", WingName), ("room", RoomName), ("level", LevelName)) : $"{wingName} · {roomName} · {levelName}";

    /// <summary>The readmission slip's first order for the maze of this wing's floor ("REPORT TO BRUNO · KITCHEN"); fallback is built from the mission data.</summary>
    public string OrderLine(string fallback) => Line("order", fallback);

    private float _nudgeTimer;
    private AudioClip _padlockClip;
    private bool _paintedLocked;

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
                case DoorState.Open: return StoryLocked ? Line("locked", $"{wingName} · LOCKED") : Line("enter", $"ENTER {wingName}");
                case DoorState.Cleared: return Line("cleared", $"{wingName} · CLEARED");
                default: return Line("sealed", $"{wingName} · SEALED");
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
                    if (!GameAudio.PlayAt(GameAudio.Library.lockedRattle, transform.position, 0.7f)) GameAudio.ClipAtPoint(_padlockClip, transform.position, 0.7f); // F97
                    hud?.ShowSubtitle(LockedLine, 2.6f);
                    break;
                }
                if (AsylumHub.Active != null) AsylumHub.Active.EnterWing(this);
                break;

            case DoorState.Cleared:
                hud?.ShowSubtitle(Line("already_cleared", $"{wingName} · ALREADY CLEARED"), 1.8f);
                break;

            default:
                if (_nudgeTimer > 0f) break;
                _nudgeTimer = 2f;
                if (_padlockClip == null) _padlockClip = DoorAudio.BuildClunkClip(0.18f, 160f, 0.35f);
                if (!GameAudio.PlayAt(GameAudio.Library.lockedRattle, transform.position, 0.7f)) GameAudio.ClipAtPoint(_padlockClip, transform.position, 0.7f); // F97
                hud?.ShowSubtitle(Line("sealed_need_key", $"SEALED · YOU NEED THE {PreviousWingName()} KEY"), 2.2f);
                break;
        }
    }

    // ---------------------------------------------------------------- state

    private void OnEnable()
    {
        L10n.Changed += Refresh;
        Refresh();
    }

    private void OnDisable() => L10n.Changed -= Refresh;

    private void Update()
    {
        if (_nudgeTimer > 0f) _nudgeTimer -= Time.deltaTime;

        // F100: a mission or the key run locks and unlocks the door while the hub is up - repaint when that flips.
        if (StoryLocked != _paintedLocked) Refresh();

        if (beacon == null) return;
        // F100: only a door that can really be used pulses; a story-locked one stays dark (its plaque says LOCKED).
        if (State == DoorState.Open && !StoryLocked)
        {
            beacon.enabled = true;
            beacon.intensity = beaconIntensity * (0.75f + 0.25f * Mathf.Sin(Time.time * 2.4f));
        }
    }

    /// <summary>S9 (D14): a longer translation shrinks (down to 65 %) on one line instead of wrapping off the plate. Shared with ShopDoor.</summary>
    internal static void FitPlaque(TMP_Text t)
    {
        if (t == null) return;
        if (!t.enableAutoSizing)
        {
            float max = t.fontSize;
            t.enableAutoSizing = true;
            t.fontSizeMax = max;
            t.fontSizeMin = max * 0.65f;
        }
        t.textWrappingMode = TextWrappingModes.NoWrap;
    }

    /// <summary>Re-reads GameFlow and repaints the plaque and beacon. Called whenever the hub switches on.</summary>
    public void Refresh()
    {
        DoorState state = State;
        bool locked = state == DoorState.Open && StoryLocked;
        _paintedLocked = StoryLocked;
        Color color = locked ? SealedColor : state == DoorState.Open ? OpenColor : state == DoorState.Cleared ? ClearedColor : SealedColor;

        if (plaque != null)
        {
            string statusKey = locked ? "locked" : state == DoorState.Open ? "open" : state == DoorState.Cleared ? "cleared" : "sealed";
            string status = L10n.Has("hub.wing.status." + statusKey) ? L10n.T("hub.wing.status." + statusKey) : statusKey.ToUpperInvariant();
            plaque.text = L10n.Has("hub.wing.plaque")
                ? L10n.T("hub.wing.plaque", ("wing", WingName), ("room", RoomName), ("status", status))
                : $"{wingName}\n<size=60%>{roomName}  ·  {status}</size>";
            FitPlaque(plaque);
            plaque.color = color;
        }

        if (beacon != null)
        {
            beacon.color = color;
            beacon.enabled = state != DoorState.Sealed && !locked;
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
