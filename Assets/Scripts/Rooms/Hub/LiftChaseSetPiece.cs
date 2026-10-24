using System.Collections;
using UnityEngine;

/// <summary>
/// F113 (floor 2): the lift chase. While the jammed lift sits at the bottom, something comes down the basement stairs (heard, three steps
/// and a screech); once the player has forced the doors and been walked out, the pursuer (Life/Pursuer, the same HubStalker as floors 4/5)
/// stands on the stairs below the chained gate and runs them down - 4.7 m/s, under a sprint - and the only way on is the Boiler Room door.
/// The music is the lift's panic track (the hub's urgent clip = the maze's all-stars track), held from the jam to the walk-in. A catch
/// wakes the player at the lift exit (never upstairs: the lift is dead and the stairs are chained) and the pursuer comes again. Resets
/// completely when the hub is left.
/// </summary>
public class LiftChaseSetPiece : HubSetPiece
{
    [SerializeField] private LiftSetPiece lift;
    [SerializeField] private HubStalker pursuer;
    [Tooltip("Mid-flight on the basement stairs, below the chained gate, facing down (-Z).")]
    [SerializeField] private Transform stairSpawn;
    [Tooltip("Top to foot of the stairs: where the steps are heard while the player is in the lift.")]
    [SerializeField] private Transform[] stairSounds = new Transform[0];
    [Tooltip("The lift exit, facing west (toward the Boiler Room): where a caught player wakes.")]
    [SerializeField] private Transform wakePoint;
    [Tooltip("Run speed (m/s). Keep it below the player's sprint (5.3) so a sprint always escapes.")]
    [SerializeField, Range(3.5f, 5.2f)] private float chaseSpeed = 4.7f;
    [Tooltip("After a catch: seconds between the fade finishing and it coming down the stairs again.")]
    [SerializeField] private float rechaseDelay = 2.5f;
    [Tooltip("How long the IT IS ON THE STAIRS line stays up (s).")]
    [SerializeField] private float runLineSeconds = 3.5f;

    private enum State { Waiting, Chasing, Done }
    private State _state = State.Done;
    private Coroutine _routine;
    private bool _ownsMusic;
    private float _idleSince;
    private bool _subscribed;

    public bool IsComplete => lift != null && pursuer != null && stairSpawn != null && wakePoint != null && stairSounds != null && stairSounds.Length == 3 && lift.IsComplete;

    protected override void OnBegin()
    {
        if (!IsComplete)
        {
            Debug.LogError("LiftChaseSetPiece: the lift, the pursuer, the stair spawn, the wake point or the three stair sounds are missing. Run KEEP IT LIT > Build Missing Pieces. Floor 2 has no lift chase.", this);
            return;
        }
        _state = State.Waiting;
        pursuer.ResetCatches();
        Unsubscribe();
        lift.Landed += OnLanded;
        lift.WalkedOut += OnWalkedOut;
        _subscribed = true;
        lift.HoldPanicMusic = true;
    }

    protected override void OnEnd()
    {
        Unsubscribe();
        if (lift != null) lift.HoldPanicMusic = false;
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        if (_ownsMusic && Hub != null && Hub.Ambience != null) Hub.Ambience.SetUrgency(0f);
        _ownsMusic = false;
        if (pursuer != null)
        {
            pursuer.relentless = false;
            pursuer.chaseSpeedOverride = 0f;
            pursuer.catchLineKey = null;
            pursuer.ClearOnReset();
            pursuer.Stop();
            pursuer.gameObject.SetActive(false);
        }
        _state = State.Done;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Unsubscribe(); // OnEnd only runs while Running; the events must never outlive the piece
        if (lift != null) lift.HoldPanicMusic = false;
    }

    private void Unsubscribe()
    {
        if (!_subscribed || lift == null) { _subscribed = false; return; }
        lift.Landed -= OnLanded;
        lift.WalkedOut -= OnWalkedOut;
        _subscribed = false;
    }

    // ---------------------------------------------------------------- the ride and the walk-out

    private void OnLanded()
    {
        if (!Running || !IsComplete) return;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(StairSteps());
    }

    /// <summary>The lift has landed: three heavy steps down the stairs, then the servo screech at the foot. Then silence until they pry.</summary>
    private IEnumerator StairSteps()
    {
        yield return new WaitForSeconds(1.5f);
        for (int i = 0; i < 3; i++)
        {
            if (!Running) yield break;
            Vector3 at = stairSounds[i].position;
            DoorAudio.Note("lift chase: stair step " + (i + 1));
            if (!GameAudio.PlayAt(GameAudio.Library.timmyStep, at, 1f)) GameAudio.ClipAtPoint(StepClip(), at, 1f);
            if (Hub != null && Hub.Torch != null) Hub.Torch.Stutter(0.15f);
            if (i < 2) yield return new WaitForSeconds(0.85f);
        }
        yield return new WaitForSeconds(Mathf.Max(0f, 4.3f - (1.5f + 1.7f)));
        if (!Running) yield break;
        DoorAudio.Note("lift chase: servo screech");
        GameAudio.PlayAt(GameAudio.Library.timmySpot, stairSounds[2].position, 0.8f);
        _routine = null;
    }

    private static AudioClip _stepClip;
    private static AudioClip StepClip() => _stepClip != null ? _stepClip : (_stepClip = DoorAudio.BuildClunkClip(0.14f, 85f, 0.55f));

    private void OnWalkedOut()
    {
        if (!Running || !IsComplete) return;
        _ownsMusic = true; // the lift handed the track over (HoldPanicMusic)
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        BeginChase(true);
    }

    private void BeginChase(bool first)
    {
        pursuer.gameObject.SetActive(true);
        if (!pursuer.Place(stairSpawn.position))
        {
            Debug.LogError("LiftChaseSetPiece: the stair spawn is not on the Hub navmesh. The chase is skipped.", this);
            _state = State.Done;
            if (Hub != null && Hub.Ambience != null) Hub.Ambience.SetUrgency(0f, 3f);
            return;
        }
        pursuer.transform.rotation = stairSpawn.rotation;
        pursuer.relentless = true;
        pursuer.chaseSpeedOverride = chaseSpeed;
        pursuer.catchLineKey = "hub.liftchase.wake";
        pursuer.ForceChase(OnCaught);
        Hub.SubtitleLine(L10n.T(first ? "hub.liftchase.run" : "hub.liftchase.again"), runLineSeconds);
        if (Hub.Ambience != null) Hub.Ambience.SetUrgency(1f, 0.3f);
        _idleSince = 0f;
        _state = State.Chasing;
    }

    private void Update()
    {
        AsylumHub hub = Hub;
        if (!Running || !IsComplete || hub == null || hub.Player == null) return;
        // The Boiler Room door was used: the walk-in has begun (Leaving is set before IsTransitioning matters).
        if (_state == State.Chasing && hub.Leaving) { End(); return; }
        if (hub.IsTransitioning || Time.timeScale <= 0f) return;

        if (_state == State.Chasing)
        {
            // A hidden player breaks the chase for a while; once they are out and it has gone idle, it comes again.
            if (pursuer.CurrentMode == HubStalker.Mode.Idle)
            {
                if (_idleSince <= 0f) _idleSince = Time.time;
                bool hidden = hub.Stealth != null && hub.Stealth.Hidden;
                if (!hidden && Time.time - _idleSince > 2f) { _idleSince = 0f; pursuer.ForceChase(OnCaught); }
            }
            else _idleSince = 0f;
        }
    }

    /// <summary>The player has gone through the Boiler Room door: it stops where it stands and screams at the door it cannot pass. The music is left alone (OnLeaveHub fades it).</summary>
    private void End()
    {
        _state = State.Done;
        pursuer.Stop();
        GameAudio.PlayAt(GameAudio.Library.timmySpot, pursuer.transform.position + Vector3.up * 1.4f, 0.9f);
    }

    /// <summary>
    /// HubStalker calls this from ResetAfterCatch, inside the hub's CatchRoutine, after the hub teleported the player to the arrival (upstairs)
    /// and while the screen is still black: put them back at the lift exit, send the pursuer away and bring it down the stairs again.
    /// </summary>
    private void OnCaught()
    {
        if (Hub == null) return;
        Hub.TeleportPlayer(wakePoint.position, wakePoint.eulerAngles.y);
        pursuer.Stop();
        pursuer.gameObject.SetActive(false);
        _state = State.Waiting;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(Rechase());
    }

    private IEnumerator Rechase()
    {
        while (Running && Hub != null && Hub.IsTransitioning) yield return null; // the catch fade is still playing
        yield return new WaitForSeconds(rechaseDelay);
        _routine = null;
        if (!Running || Hub == null || Hub.Leaving) yield break;
        BeginChase(false);
    }
}
