        // Every seated body stays AlwaysAnimate (set by InstantiateRobotNode) on purpose: this adds the nod and the turn on top of the pose the Animator wrote THIS frame, so a culled Animator (CullCompletely) would leave last frame's rotated head in place and the offset would compound.
using TMPro;
using UnityEngine;

/// <summary>
/// F117: THE LAST SUPPER. The asylum's dining room holds one long table laid for the lost: five patients' places (one per maze floor, seat n
/// belongs to floor n's case file), a sixth place that is always empty, and the Director's high-backed chair at the head. Seat n is filled by a
/// silent seated patient once floor n has been cleared (the hub shows floors 1..HubState.Floor-1 seated; the finale fills every seat). Each
/// place has a name card that reads the case file's label once that file has been read (<see cref="LoreArchive.HasRead"/>); E on a named card
/// gives one unsettling line (<see cref="HubSupperCard"/>). In the finale the Director's chair stays empty with one lit candle and every head
/// turns to follow the player while they are in the room; at every other time the heads stay bowed - stillness is the scare.
///
/// Lives on AsylumHub/Supper (a hand-editable part of the hub prefab; AsylumHub switches it with the other content and calls <see cref="Refresh"/>
/// at black on every arrival). The builder made the table, chairs, bodies and cards; this component only switches bodies and cards and, in
/// LateUpdate (after the Animator has written the sitting pose), bows or turns each head.
/// </summary>
public class HubSupper : MonoBehaviour
{
    /// <summary>One place at the table. Plain data (not a component): the seated body, its head bone, the name card.</summary>
    [System.Serializable]
    public class Seat
    {
        [Tooltip("The seated robot's body (its Animator node). Switched on when the seat is filled.")]
        public Transform body;
        [Tooltip("Its Humanoid head bone. Bowed in LateUpdate; in the finale it turns to the player.")]
        public Transform head;
        [Tooltip("The name card's text.")]
        public TMP_Text card;
        [HideInInspector] public Animator animator;
        [HideInInspector] public float look;   // 0 = bowed, 1 = looking at the player
        [HideInInspector] public float yaw;    // current head turn, degrees from the body's facing
    }

    [Header("The table")]
    [Tooltip("Seats 1..5 = floors 1..5. The sixth place (always empty) has no entry.")]
    public Seat[] seats = new Seat[0];
    [Tooltip("The Director's high-backed chair at the head of the table. Always there, always empty.")]
    public GameObject directorChair;
    [Tooltip("The candle in front of the Director's chair: lit in the finale only.")]
    public GameObject candle;
    [Tooltip("The candle's light (off outside the finale). HubDecay never blinks it.")]
    public Light candleLight;
    [Tooltip("Trigger volume over the dining room. The player counts as inside when XZ is within it and the feet are within its Y range.")]
    public BoxCollider roomZone;
    [Tooltip("The dining room's one hub light, hung low over the table (the pack's own light, moved there by the builder). HubDecay never puts it out: the table stays lit until the finale.")]
    public Light tableLight;

    [Header("Heads")]
    [Tooltip("How far a bowed head nods forward, degrees.")]
    public float bowPitch = 24f;
    public float turnSpeed = 45f;
    public float lookBlendSpeed = 0.7f;
    public float yawLimit = 80f;

    private static AsylumHub Hub => AsylumHub.Active;

    /// <summary>True once the builder has made the seats and the zone.</summary>
    public bool IsComplete => seats != null && seats.Length == 5 && roomZone != null && System.Array.IndexOf(seats, null) < 0 && AllSeatsBuilt();

    private bool AllSeatsBuilt()
    {
        foreach (Seat s in seats) if (s.body == null || s.head == null || s.card == null) return false;
        return true;
    }

    /// <summary>Seat n (1..5) holds a patient: its floor has been cleared (the hub is entered for a later floor), or the finale is on.</summary>
    public static bool IsFilled(int seat) => HubState.IsFinale || HubState.Floor > seat;

    /// <summary>Seat n's case file has been read: its card carries the name.</summary>
    public static bool IsNamed(int seat) => LoreArchive.HasRead(PatientFiles.IdForFloor(seat));

    /// <summary>The name card of seat n is readable: filled or not, the card names the file once it has been read.</summary>
    public bool CardNamed(int seat) => seat >= 1 && seat <= seats.Length && IsNamed(seat);

    private void OnEnable() => L10n.Changed += Refresh;

    private void OnDisable() => L10n.Changed -= Refresh;

    /// <summary>Puts the room in the state of the current floor. Called by AsylumHub.EnterRoutine at black, and on a language change.</summary>
    public void Refresh()
    {
        if (seats == null) return;
        for (int i = 0; i < seats.Length; i++)
        {
            Seat s = seats[i];
            if (s == null) continue;
            bool filled = IsFilled(i + 1);
            if (s.body != null && s.body.gameObject.activeSelf != filled) s.body.gameObject.SetActive(filled);
            if (s.animator == null && s.body != null) s.animator = s.body.GetComponent<Animator>();
            if (!filled) { s.look = 0f; s.yaw = 0f; }
            if (s.card != null) s.card.text = IsNamed(i + 1) ? PatientFiles.Label(PatientFiles.IdForFloor(i + 1)).Replace(" · ", "\n") : ""; // "P3 · HILDE BRANDT" over two lines
        }
        bool lit = HubState.IsFinale;
        if (candleLight != null) candleLight.enabled = lit;
        if (candle != null)
        {
            // the candle always stands in front of the Director's chair; it is lit in the finale only
            Transform litT = candle.transform.Find("Lit"), unlit = candle.transform.Find("Unlit");
            if (litT != null) litT.gameObject.SetActive(lit);
            if (unlit != null) unlit.gameObject.SetActive(!lit);
        }
    }

    private bool Live
    {
        get
        {
            AsylumHub hub = Hub;
            return GameFlow.IsInHub && hub != null && hub.ContentActive && !hub.IsTransitioning && !hub.Leaving;
        }
    }

    /// <summary>XZ inside the room zone and the feet within its Y band (the room above is a different room).</summary>
    public bool Inside(Transform player)
    {
        if (roomZone == null || player == null) return false;
        Bounds b = roomZone.bounds;
        Vector3 p = player.position;
        return p.x >= b.min.x && p.x <= b.max.x && p.z >= b.min.z && p.z <= b.max.z && p.y >= b.min.y && p.y <= b.max.y;
    }

    /// <summary>LateUpdate, so the nod and the turn land after the Animator has written the sitting pose for this frame.</summary>
    private void LateUpdate()
    {
        if (seats == null || !Live) return;
        AsylumHub hub = Hub;
        Transform player = hub != null ? hub.Player : null;
        bool follow = HubState.IsFinale && player != null && Inside(player);
        float dt = Time.deltaTime;

        foreach (Seat s in seats)
        {
            if (s == null || s.body == null || s.head == null || !s.body.gameObject.activeInHierarchy) continue;
            s.look = Mathf.MoveTowards(s.look, follow ? 1f : 0f, lookBlendSpeed * dt);

            float wantYaw = 0f;
            if (follow)
            {
                Vector3 to = player.position - s.head.position;
                to.y = 0f;
                if (to.sqrMagnitude > 0.0001f)
                {
                    float wanted = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
                    wantYaw = Mathf.Clamp(Mathf.DeltaAngle(s.body.eulerAngles.y, wanted), -yawLimit, yawLimit);
                }
            }
            s.yaw = Mathf.MoveTowards(s.yaw, wantYaw, turnSpeed * dt);

            float pitch = Mathf.Lerp(bowPitch, -2f, s.look);
            Quaternion pose = s.head.rotation;
            s.head.rotation = Quaternion.AngleAxis(s.yaw * s.look, s.body.up) * Quaternion.AngleAxis(pitch, s.body.right) * pose;
        }
    }
}
