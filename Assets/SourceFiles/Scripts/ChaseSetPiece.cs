using UnityEngine;

/// <summary>
/// F93 (S3, floor 4): the walk from the Morgue to the Surgery is a chase. The trigger is the player reaching the top of the basement stairs
/// on the ground floor: behind them, at the foot of the stairs, the pursuer (the same HubStalker as the floor-5 patrol, here relentless)
/// stands up from the floor (the Mixamo Standing Up clip), screams and hunts them, the escape track fades in under the music, and it only
/// stops when they cross into the Surgery (a trigger over the room). A catch is the usual hub catch (black, back at the arrival, no
/// GameOutcome) and re-arms the trigger. Everything resets when the hub is left.
/// </summary>
public class ChaseSetPiece : HubSetPiece
{
    [SerializeField] private HubStalker pursuer;
    [Tooltip("Where it rises: the foot of the basement stairs.")]
    [SerializeField] private Transform riseSpot;
    [Tooltip("Trigger box (world AABB used) at the top of the basement stairs.")]
    [SerializeField] private BoxCollider topZone;
    [Tooltip("Trigger box (world AABB used) over the Surgery.")]
    [SerializeField] private BoxCollider endZone;
    [SerializeField] private float riseSeconds = 3.2f;
    [Tooltip("How long the IT IS AWAKE. RUN TO THE SURGERY. line stays up when the chase starts (s).")]
    [SerializeField] private float runLineSeconds = 3f;

    private enum State { Armed, Chasing, Done }
    private State _state;
    private float _idleSince;

    public bool IsComplete => pursuer != null && riseSpot != null && topZone != null && endZone != null;

    protected override void OnBegin()
    {
        if (!IsComplete)
        {
            Debug.LogError("ChaseSetPiece: the pursuer, the rise spot or a trigger zone is missing. Run LIGHTS OUT > Build Missing Pieces. Floor 4 has no chase.", this);
            return;
        }
        _state = State.Armed;
        pursuer.ResetCatches();
    }

    protected override void OnEnd()
    {
        SetUrgency(0f);
        if (pursuer != null)
        {
            pursuer.relentless = false;
            pursuer.Stop();
            pursuer.gameObject.SetActive(false);
        }
        _state = State.Done;
    }

    private void SetUrgency(float v)
    {
        AsylumHub hub = Hub;
        if (hub != null && hub.Ambience != null) hub.Ambience.SetUrgency(v);
    }

    private void Update()
    {
        AsylumHub hub = Hub;
        if (!Running || !IsComplete || hub == null || hub.Player == null || hub.IsTransitioning || Time.timeScale <= 0f) return;
        Vector3 p = hub.Player.position;

        switch (_state)
        {
            case State.Armed:
                if (topZone.bounds.Contains(p)) Begin();
                break;

            case State.Chasing:
                if (endZone.bounds.Contains(p)) { End(); break; }
                // A hidden player breaks the chase for a while; once they are out and it has gone idle, it comes again.
                if (pursuer.CurrentMode == HubStalker.Mode.Idle)
                {
                    if (_idleSince <= 0f) _idleSince = Time.time;
                    bool hidden = hub.Stealth != null && hub.Stealth.Hidden;
                    if (!hidden && Time.time - _idleSince > 2f) { _idleSince = 0f; pursuer.ForceChase(); }
                }
                else _idleSince = 0f;
                break;
        }
    }

    private void Begin()
    {
        _state = State.Chasing;
        pursuer.relentless = true;
        riseSpot.gameObject.SetActive(true);
        if (!pursuer.Place(riseSpot.position))
        {
            Debug.LogError("ChaseSetPiece: the rise spot is not on the Hub navmesh. The chase is skipped.", this);
            _state = State.Done;
            return;
        }
        pursuer.transform.rotation = riseSpot.rotation;
        pursuer.StartRise(riseSeconds, Rearm);
        if (Hub != null && Hub.Ambience != null) Hub.Ambience.SetUrgentOverride(GameAudio.Library.chaseMusic.First()); // F97: the recorded chase loop (empty slot = the usual escape track)
        SetUrgency(1f);
        // World text: where the chase ends. Without it players ran to the floor-4 mission giver (Dr. Weiss, 6 m short of the end zone),
        // who refuses to talk while the pursuer is after them, and the chase seemed endless (10 Oct 2026).
        Hub.SubtitleLine(L10n.T("hub.chase.run"), runLineSeconds);
    }

    private void End()
    {
        _state = State.Done;
        SetUrgency(0f);
        pursuer.relentless = false;
        pursuer.Stop();
        GameAudio.Play2D(GameAudio.Library.chaseEnd, 0.7f, music: true); // F97: the last bars, fading out by themselves
        Hub.SubtitleLine(L10n.T("hub.chase.stops"), 2.6f);
        StartCoroutine(Fade());
    }

    private System.Collections.IEnumerator Fade()
    {
        yield return new WaitForSeconds(1.2f);
        if (pursuer != null) pursuer.gameObject.SetActive(false);
    }

    /// <summary>After a catch: the player woke at the arrival, the pursuer goes away, the trigger is armed again.</summary>
    private void Rearm()
    {
        SetUrgency(0f);
        pursuer.relentless = false;
        pursuer.Stop();
        pursuer.gameObject.SetActive(false);
        _state = State.Armed;
    }
}
