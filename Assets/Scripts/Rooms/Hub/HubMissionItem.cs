using UnityEngine;

/// <summary>
/// F98: a thing a floor's mission asks the player to fetch - the key ring, the tray, the music box, one of the three ampoules. A plain
/// prop under AsylumHub/Life/Missions/Floor n with a trigger collider, so the player interactor can find it. It is only interactable
/// (and only visible, HubMission switches the object) while its mission is Active; taking it just tells the mission. Not a Pickup,
/// not a KeyPickup and nothing the maze counts: it never leaves the hospital.
/// </summary>
public class HubMissionItem : MonoBehaviour, IInteractable
{
    [SerializeField] private HubMission mission;
    [Tooltip("Prompt after E, e.g. TAKE THE KEY RING.")]
    [SerializeField] private string prompt = "TAKE IT";
    [Tooltip("Subtitle on pickup. {n} / {total} = how many of the mission's items are taken.")]
    [SerializeField] private string takenLine = "";

    public bool Taken { get; private set; }

    /// <summary>The line said on pickup, with {n} / {total} still in it (HubMission fills them). S9 (D10): hub.mission.f.item.i.taken, else the serialized text.</summary>
    public string TakenLine
    {
        get
        {
            string k = Key("taken");
            return k != null && L10n.Has(k) ? L10n.T(k, ("n", mission != null ? mission.Collected : 0), ("total", mission != null && mission.items != null ? mission.items.Length : 0)) : takenLine;
        }
    }

    // hub.mission.<floor>.item.<index in the mission's items>.<leaf>
    private string Key(string leaf)
    {
        int index = mission != null ? mission.IndexOf(this) : -1;
        return index < 0 ? null : $"hub.mission.{mission.floor}.item.{index}.{leaf}";
    }

    public void Bind(HubMission owner) { mission = owner; }

    /// <summary>The mission counts this item as already in hand (super-easy mode, the debug completion).</summary>
    public void MarkTaken() { Taken = true; }

    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => mission != null && mission.IsItemAvailable(this);
    void IInteractable.OnFocus(bool focused) { }

    void IInteractable.Interact(PlayerInteractor by)
    {
        if (mission == null || Taken || !mission.IsItemAvailable(this)) return;
        Taken = true;
        mission.Take(this);
    }

    string IInteractable.Prompt
    {
        get
        {
            string k = Key("prompt");
            return k != null && L10n.Has(k) ? L10n.T(k) : prompt;
        }
    }
}
