using System;
using UnityEngine;

/// <summary>
/// F92: the Karlheinz Asylum the player walks through between mazes. The room is a hand-editable prefab
/// (Assets/Prefabs/AsylumHub.prefab, first drafted by AsylumHubBuilder); this component only holds the parts the
/// game needs to find and switches the heavy content on and off.
///
/// Phases A-C: serialized references, IsComplete, content hiding (D10). The flow (entering from the Intake or a floor
/// start, walking into a wing door) is the later phase and lives here too once it exists.
/// </summary>
public class AsylumHub : MonoBehaviour
{
    /// <summary>The hub in the scene, or null. Scene-scoped: cleared in OnDestroy.</summary>
    public static AsylumHub Active { get; private set; }

    /// <summary>Raised when the player arrives in the hub, with the floor they are about to enter (hook for the later "the hub gets less safe" idea).</summary>
    public static event Action<int> OnEnterHub;

    [Header("Content (inactive except while the player is in the hub)")]
    [Tooltip("The pack building: architecture, props and its realtime lights.")]
    public Transform building;
    [Tooltip("Everything the builder adds on top of the building (props, signs, particles).")]
    public Transform dressing;
    [Tooltip("Lights the builder adds (wing-door beacons, entrance spill).")]
    public Transform lights;
    [Tooltip("The five wing doors.")]
    public Transform wings;

    [Header("Points (always active)")]
    public Transform points;
    public Transform mainEntrance;

    [Header("Wings")]
    [Tooltip("Index 0 = floor 1.")]
    public WingDoor[] wingDoors = new WingDoor[0];

    [Header("Atmosphere while the player is inside (D9)")]
    public Color hubAmbient = new Color(0.035f, 0.035f, 0.04f);
    public float hubFogDensity = 0.03f;
    public Color hubFogColor = new Color(0.01f, 0.012f, 0.02f);

    /// <summary>True while the building content is switched on.</summary>
    public bool ContentActive { get; private set; } = true;

    public bool IsComplete =>
        building != null && points != null && mainEntrance != null && wings != null && lights != null && dressing != null &&
        wingDoors != null && wingDoors.Length == 5 && AllWingsWired();

    private bool AllWingsWired()
    {
        foreach (WingDoor door in wingDoors)
        {
            if (door == null || !door.IsComplete) return false;
        }
        return true;
    }

    private void Awake()
    {
        Active = this;
        // The hub is saved with its content active so it can be edited; at runtime it only exists while the player is in it.
        if (Application.isPlaying) SetContentActive(false);
    }

    private void OnDestroy()
    {
        if (Active == this) Active = null;
    }

    /// <summary>Shows or hides the building, the dressing, the added lights and the wing doors.</summary>
    public void SetContentActive(bool active)
    {
        ContentActive = active;
        if (building != null) building.gameObject.SetActive(active);
        if (dressing != null) dressing.gameObject.SetActive(active);
        if (lights != null) lights.gameObject.SetActive(active);
        if (wings != null) wings.gameObject.SetActive(active);
    }

    /// <summary>
    /// The walk-in through an open wing door (Phase E: freeze, swing the leaf, fade, teleport to the maze, BeginRun).
    /// Not wired into the run yet - the hub is not entered by the game until then.
    /// </summary>
    public void EnterWing(WingDoor door)
    {
        Debug.Log($"AsylumHub: EnterWing({door.wingName}) - the walk into the maze is Phase E.", door);
    }

    /// <summary>For the flow code (later phase) and the later patch's listeners.</summary>
    protected static void RaiseEnterHub(int floor) => OnEnterHub?.Invoke(floor);
}
