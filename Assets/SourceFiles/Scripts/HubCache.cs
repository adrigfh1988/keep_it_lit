using System.Collections;
using UnityEngine;

/// <summary>
/// F114: one searchable spot in an otherwise empty hub room (a mirror cabinet, a cistern, a fridge, a case, a drawer, a mattress). Hold E for
/// 1.2 s and it gives one small reward from <see cref="HubCacheLoot"/> and says what; a searched spot stays searched for the campaign
/// (<see cref="HubCaches"/>) and is not offered again. Its open pose - the cabinet doors, the fridge door, the drawer - is restored to
/// "open" on every later hub visit. Same shape as HubExamine: a hub IInteractable that refuses while a hub pursuer is after the player, a
/// transition runs or the finale stage is up, and makes no noise for the hunters (the hub pursuer only hears sprinting).
/// Also refused when something solid stands between the camera and the spot (the interactor's probe sees through walls and closed stall
/// doors; a find must not be searchable from behind one). Built by AsylumHubBuilder (Rooms3) under AsylumHub/Life/RoomFinds. The moved
/// parts must not be static (static batching freezes them).
/// </summary>
public class HubCache : MonoBehaviour, IInteractable
{
    public enum Sound { Rummage, RummageClink, MetalSlide, Paper }

    [Tooltip("0..6: the bit in HubCaches.SearchedMask. One per room.")]
    [SerializeField] private int cacheId;
    [SerializeField] private Sound sound = Sound.Rummage;
    [Tooltip("Moved when the spot is searched (cabinet doors, fridge door, drawer, case). Empty = nothing visibly opens.")]
    [SerializeField] private Transform[] openParts = new Transform[0];
    [Tooltip("Per part: how far it slides when open, in the part's own local space (added to its closed local position).")]
    [SerializeField] private Vector3[] openOffsets = new Vector3[0];
    [Tooltip("Per part: the extra local rotation when open (Euler, applied on top of its closed local rotation).")]
    [SerializeField] private Vector3[] openEulers = new Vector3[0];
    [SerializeField, Min(0.05f)] private float openSeconds = 0.35f;
    [SerializeField, Min(0.1f)] private float holdSeconds = 1.2f;
    [SerializeField, Min(0.5f)] private float subtitleSeconds = 3f;
    [Tooltip("The reach-in volume (a trigger on this object); the interactor's probe collects triggers.")]
    [SerializeField] private BoxCollider trigger;

    private Vector3[] _closedPos;
    private Quaternion[] _closedRot;
    private bool _busy;
    private Coroutine _routine;

    private static AudioClip s_rummage, s_clink, s_slide;

    public int CacheId => cacheId;
    public bool IsSearched => HubCaches.IsSearched(cacheId);
    public Transform[] OpenParts => openParts;

    /// <summary>The cache has a trigger and its per-part arrays line up.</summary>
    public bool IsComplete =>
        trigger != null && openParts != null && openOffsets != null && openEulers != null &&
        openOffsets.Length == openParts.Length && openEulers.Length == openParts.Length && System.Array.IndexOf(openParts, null) < 0;

    private static AsylumHub Hub => AsylumHub.Active;

    private void OnEnable()
    {
        Capture();
        Pose(IsSearched);
        AsylumHub.OnEnterHub += HandleEnter;
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        _busy = false;
        Pose(IsSearched);
    }

    private void HandleEnter(int floor)
    {
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        _busy = false;
        Pose(IsSearched);
    }

    private void Capture()
    {
        if (_closedPos != null || openParts == null) return;
        _closedPos = new Vector3[openParts.Length];
        _closedRot = new Quaternion[openParts.Length];
        for (int i = 0; i < openParts.Length; i++)
        {
            if (openParts[i] == null) continue;
            _closedPos[i] = openParts[i].localPosition;
            _closedRot[i] = openParts[i].localRotation;
        }
    }

    /// <summary>Puts every part at its closed (false) or fully open (true) pose.</summary>
    private void Pose(bool open)
    {
        if (_closedPos == null) return;
        for (int i = 0; i < openParts.Length; i++)
        {
            if (openParts[i] == null) continue;
            Apply(i, open ? 1f : 0f);
        }
    }

    private void Apply(int i, float t)
    {
        Transform part = openParts[i];
        Vector3 offset = i < openOffsets.Length ? openOffsets[i] : Vector3.zero;
        Vector3 euler = i < openEulers.Length ? openEulers[i] : Vector3.zero;
        part.localPosition = _closedPos[i] + _closedRot[i] * (offset * t);
        part.localRotation = _closedRot[i] * Quaternion.Euler(euler * t);
    }

    string IInteractable.Prompt => L10n.T(IsSearched ? "hub.cache.searched" : "hub.cache.search");

    float IInteractable.HoldSeconds => holdSeconds;

    bool IInteractable.CanInteract
    {
        get
        {
            if (IsSearched || _busy) return false;
            AsylumHub hub = Hub;
            if (hub == null || hub.Hud == null || hub.PlayerCamera == null || hub.IsTransitioning || hub.Leaving || HubState.IsFinale || hub.PursuerAfterPlayer()) return false;
            return InSight(hub);
        }
    }

    /// <summary>False when a solid collider (other than the spot's own parts) stands well in front of the reach-in volume.</summary>
    private bool InSight(AsylumHub hub)
    {
        Vector3 target = trigger != null ? trigger.bounds.center : transform.position;
        Vector3 from = hub.PlayerCamera.transform.position;
        Vector3 to = target - from;
        float distance = to.magnitude;
        if (distance < 0.6f) return true;
        if (!Physics.Raycast(from, to / distance, out RaycastHit hit, distance, ~0, QueryTriggerInteraction.Ignore)) return true;
        return hit.distance >= distance - 0.4f;
    }

    void IInteractable.Interact(PlayerInteractor who)
    {
        AsylumHub hub = Hub;
        if (hub == null || IsSearched || _busy) return;

        HubCaches.MarkSearched(cacheId);
        ThrowController throws = hub.Player != null ? hub.Player.GetComponent<ThrowController>() : null;
        HubCacheLoot.Reward reward = HubCacheLoot.Roll(HubState.Floor, throws);
        HubCacheLoot.Grant(reward, throws);
        if (hub.Hud != null) hub.Hud.ShowSubtitle(HubCacheLoot.Line(reward), subtitleSeconds);

        PlaySound();
        if (openParts != null && openParts.Length > 0)
        {
            _busy = true;
            if (_routine != null) StopCoroutine(_routine);
            _routine = StartCoroutine(OpenRoutine());
        }
    }

    void IInteractable.OnFocus(bool focused) { }

    private IEnumerator OpenRoutine()
    {
        for (float t = 0f; t < openSeconds; t += Time.deltaTime)
        {
            float k = Mathf.SmoothStep(0f, 1f, t / openSeconds);
            for (int i = 0; i < openParts.Length; i++) if (openParts[i] != null) Apply(i, k);
            yield return null;
        }
        Pose(true);
        _busy = false;
        _routine = null;
    }

    private void PlaySound()
    {
        Vector3 at = trigger != null ? trigger.bounds.center : transform.position;
        if (GameAudio.PlayAt(GameAudio.Library.cacheSearch, at, 0.8f, 1f, 12f))
        {
            if (sound == Sound.RummageClink) StartCoroutine(Clink(at));
            return;
        }

        switch (sound)
        {
            case Sound.MetalSlide:
                if (s_slide == null) s_slide = DoorAudio.BuildClunkClip(0.25f, 220f, 0.6f);
                GameAudio.ClipAtPoint(s_slide, at, 0.8f);
                break;
            case Sound.Paper:
                if (!GameAudio.PlayAt(GameAudio.Library.paper, at, 0.8f, 1f, 12f)) GameAudio.ClipAtPoint(Rummage(), at, 0.8f);
                break;
            default:
                GameAudio.ClipAtPoint(Rummage(), at, 0.8f);
                if (sound == Sound.RummageClink) StartCoroutine(Clink(at));
                break;
        }
    }

    private IEnumerator Clink(Vector3 at)
    {
        yield return new WaitForSeconds(0.35f);
        if (s_clink == null) s_clink = DoorAudio.BuildClunkClip(0.08f, 900f, 0.2f);
        GameAudio.ClipAtPoint(s_clink, at, 0.7f);
    }

    private static AudioClip Rummage()
    {
        if (s_rummage == null) s_rummage = DoorAudio.BuildRummageClip();
        return s_rummage;
    }

    /// <summary>Testing only (CLI): runs the search as if the hold had completed, skipping the sight and hub guards except that a hub must be active.</summary>
    public string DebugSearch()
    {
        AsylumHub hub = Hub;
        if (hub == null) return "no hub";
        if (IsSearched) return $"cache {cacheId} already searched";
        ((IInteractable)this).Interact(null);
        return $"cache {cacheId} searched";
    }
}
