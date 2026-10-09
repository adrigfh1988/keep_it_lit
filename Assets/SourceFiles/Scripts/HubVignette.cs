using UnityEngine;

/// <summary>
/// F101: a little story scene in the asylum hub whose look depends on how far the player has come. The component holds a list of poses
/// (a child object each, with the floors it is shown on); on every AsylumHub.OnEnterHub the poses whose range contains the floor are
/// switched on and the others off - several poses can be on at once (a "base" pose for floors 1-5 plus one that only exists from floor
/// 4). The finale counts as floor 5. It lives under AsylumHub/Life/Vignettes (HubDecay never touches Life) and changes nothing else:
/// no state, no save, no subscriptions beyond the hub's own enter event.
/// </summary>
public class HubVignette : MonoBehaviour
{
    [System.Serializable]
    public struct Pose
    {
        [Tooltip("First floor (1-5) this pose is shown on.")] public int minFloor;
        [Tooltip("Last floor (1-5) this pose is shown on.")] public int maxFloor;
        public GameObject root;
    }

    [SerializeField] private Pose[] poses = new Pose[0];

    public int PoseCount => poses != null ? poses.Length : 0;

    private void OnEnable()
    {
        AsylumHub.OnEnterHub += HandleEnter;
        Apply(CurrentFloor());
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
    }

    private void HandleEnter(int floor)
    {
        Apply(HubState.IsFinale ? 5 : Mathf.Clamp(floor, 1, 5));
    }

    private static int CurrentFloor() => HubState.IsFinale ? 5 : HubState.Floor;

    /// <summary>Switches the poses for `floor` (1..5) on and the rest off.</summary>
    public void Apply(int floor)
    {
        if (poses == null) return;
        foreach (Pose p in poses)
        {
            if (p.root == null) continue;
            bool on = floor >= p.minFloor && floor <= p.maxFloor;
            if (p.root.activeSelf != on) p.root.SetActive(on);
        }
    }

    /// <summary>Editor / builder: every pose on (used while the hub NavMesh is baked and the layout audited), or off again.</summary>
    public void SetAllActive(bool on)
    {
        if (poses == null) return;
        foreach (Pose p in poses) if (p.root != null) p.root.SetActive(on);
    }
}
