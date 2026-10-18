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

    private HubSupperSixthSeat _sixthSeat;
    private bool _endingStarted;
    private float _communionLookTimer;

    public bool CanInteractSixthSeat => Live && !_endingStarted && Hub != null && Hub.Hud != null && !Hub.PursuerAfterPlayer();

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
        EnsureSixthSeat();
    }

    private void EnsureSixthSeat()
    {
        if (_sixthSeat == null) _sixthSeat = GetComponentInChildren<HubSupperSixthSeat>(true);
        if (_sixthSeat == null)
        {
            Transform target = transform.Find("Chair_6") ?? transform.Find("Plate_6") ?? transform.Find("SixthSeat");
            if (target == null)
            {
                GameObject go = new GameObject("SixthSeat");
                go.transform.SetParent(transform, false);
                Vector3 pos = ResolveSixthPlacePosition();
                go.transform.position = pos;
                BoxCollider box = go.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.95f, 1.35f, 0.95f);
                target = go.transform;
            }
            else if (target.GetComponent<Collider>() == null)
            {
                BoxCollider box = target.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.95f, 1.35f, 0.95f);
            }
            _sixthSeat = target.GetComponent<HubSupperSixthSeat>();
            if (_sixthSeat == null) _sixthSeat = target.gameObject.AddComponent<HubSupperSixthSeat>();
        }
        _sixthSeat.Bind(this);
    }

    private Vector3 ResolveSixthPlacePosition()
    {
        float centerX = directorChair != null ? directorChair.transform.position.x
            : candle != null ? candle.transform.position.x
            : transform.position.x;
        if (seats != null && seats.Length >= 5 && seats[4] != null && seats[4].body != null)
        {
            Vector3 s5 = seats[4].body.position;
            float dx = Mathf.Abs(centerX - s5.x);
            if (dx < 0.2f) dx = 0.72f;
            return new Vector3(centerX + dx, s5.y + 0.65f, s5.z);
        }
        return transform.position + new Vector3(0.72f, 0.65f, -2.6f);
    }

    public void InteractSixthSeat()
    {
        if (!CanInteractSixthSeat) return;
        AsylumHub hub = Hub;
        if (hub == null) return;

        if (HubState.IsFinale)
        {
            _endingStarted = true;
            StartCoroutine(SitEndingCRoutine(hub));
            return;
        }

        Vector3 at = _sixthSeat != null ? _sixthSeat.transform.position : transform.position;
        GameAudio.PlayAt(GameAudio.Library.paper, at, 0.55f);
        GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.18f, 140f, 0.45f), at, 0.5f);
        _communionLookTimer = 2.4f;
        if (HubState.Floor >= 5 && hub.Torch != null) hub.Torch.Refill();
        if (hub.Hud != null) hub.Hud.ShowSubtitle(L10n.T("hub.supper.sit_refuse"), 3.8f);
    }

    private System.Collections.IEnumerator SitEndingCRoutine(AsylumHub hub)
    {
        if (hub.Reader != null) hub.Reader.Close();
        hub.SetScripted(true);
        if (hub.Controller != null) hub.Controller.MovementLocked = true;
        PlayerLock.Freeze(hub.Player, true);
        if (hub.Torch != null) { hub.Torch.InputEnabled = false; hub.Torch.ForceOff(); }
        if (hub.Hud != null)
        {
            hub.Hud.SetPrompt(null);
            hub.Hud.SetFocusPrompt(null);
            hub.Hud.ShowSubtitle(L10n.T("hub.supper.seated_line"), 3.8f);
        }

        Vector3 seatPos = ResolveSixthPlacePosition();
        Vector3 targetPlayerPos = new Vector3(seatPos.x + 0.12f, hub.Player != null ? hub.Player.position.y : seatPos.y - 0.65f, seatPos.z);
        Vector3 lookTarget = candle != null ? candle.transform.position + Vector3.up * 0.25f : new Vector3(seatPos.x - 1.2f, seatPos.y + 0.2f, seatPos.z);
        Vector3 startPos = hub.Player != null ? hub.Player.position : targetPlayerPos;

        GameAudio.ClipAtPoint(DoorAudio.BuildCreakClip(0.55f, 165f, 2), seatPos, 0.7f);
        _communionLookTimer = 6f;

        float elapsed = 0f;
        int knocksFired = 0;
        while (elapsed < 2.2f)
        {
            elapsed += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 1.3f));
            if (hub.Player != null) hub.MovePlayerTo(Vector3.Lerp(startPos, targetPlayerPos, k));
            if (hub.Controller != null) hub.Controller.LookAt(lookTarget, 1f - Mathf.Exp(-6f * Time.unscaledDeltaTime));

            if (knocksFired == 0 && elapsed >= 0.45f)
            {
                knocksFired = 1;
                GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.22f, 125f, 0.6f), seatPos, 0.65f);
            }
            else if (knocksFired == 1 && elapsed >= 0.90f)
            {
                knocksFired = 2;
                GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.22f, 120f, 0.65f), seatPos, 0.70f);
            }
            else if (knocksFired == 2 && elapsed >= 1.35f)
            {
                knocksFired = 3;
                GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.28f, 95f, 0.75f), seatPos, 0.80f);
            }
            yield return null;
        }

        yield return hub.BlackoutTo(1f, 1.0f);
        if (hub.Controller != null) hub.Controller.enabled = false;
        GameOutcome outcome = FindAnyObjectByType<GameOutcome>();
        if (outcome != null) outcome.CompleteFinale(GameOutcome.FinaleEnding.Seated);
    }

    private bool Live
    {
        get
        {
            AsylumHub hub = Hub;
            return GameFlow.IsInHub && hub != null && hub.ContentActive && (!hub.IsTransitioning || _endingStarted) && !hub.Leaving;
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
        if (_communionLookTimer > 0f) _communionLookTimer = Mathf.Max(0f, _communionLookTimer - Time.unscaledDeltaTime);
        bool follow = ((HubState.IsFinale && Inside(player)) || _communionLookTimer > 0f) && player != null;
        float dt = Time.deltaTime > 0f ? Time.deltaTime : Time.unscaledDeltaTime;

        foreach (Seat s in seats)
        {
            if (s == null || s.body == null || s.head == null || !s.body.gameObject.activeInHierarchy) continue;
            s.look = Mathf.MoveTowards(s.look, follow ? 1f : 0f, ( _endingStarted ? 1.4f : lookBlendSpeed) * dt);

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
