using System.Collections;
using UnityEngine;

/// <summary>
/// The asylum hub's objective card: the Intake's ObjectiveCard, driven by the floor's story state. Each frame it works out what the
/// player should do next on this floor and shows it big top-centre for a few seconds (on arrival and whenever it changes - a finished
/// step gets the tick first), then collapses it into a compact card top-left that stays while the objective stands.
///
/// Steps, floors 1-4 (HubMission): 1 / 4 talk to the giver, 2 / 4 fetch, 3 / 4 bring it back (the item count / CARRYING as the second
/// line), 4 / 4 go to the wing door. Floor 5 (KeyRunSetPiece): 1 / 2 find the Director's key, 2 / 2 go to the Director's door. A floor
/// without its mission or key run only says where the door is. Nothing in the finale, outside the hub, during a door walk or while the
/// hub's blackout is up. Added to the hub by AsylumHub.Configure; reads state only, never changes it.
/// </summary>
public class HubObjectiveCard : MonoBehaviour
{
    [Tooltip("How long the big card stays before it collapses into the compact one.")]
    [SerializeField] private float fullSeconds = 6f;

    private struct Objective
    {
        public bool Has;
        public int Floor, Step, Total;
        public string Goal, Sub;

        public bool SameGoal(Objective o) => Has && o.Has && Floor == o.Floor && Step == o.Step && Goal == o.Goal;
    }

    private ObjectiveCard _card;
    private Objective _shown;
    private bool _visible;      // the card (full or compact) is on screen or on its way
    private bool _busy;         // a show / advance routine is running
    private bool _expandNext = true;
    private Coroutine _run;

    // Looked up once per hub entry, not every frame.
    private int _cachedFloor = -1;
    private HubMission _mission;
    private KeyRunSetPiece _keyRun;

    private void OnEnable()
    {
        AsylumHub.OnEnterHub += HandleEnter;
    }

    private void OnDisable()
    {
        AsylumHub.OnEnterHub -= HandleEnter;
        Hide();
    }

    private void OnDestroy()
    {
        if (_card != null) Destroy(_card.gameObject);
    }

    private void HandleEnter(int floor)
    {
        _expandNext = true; // every arrival (from a maze, the Intake or the shop) shows the big card once
        _cachedFloor = -1;
    }

    private void Update()
    {
        AsylumHub hub = AsylumHub.Active;
        Objective now = CanShow(hub) ? Compute(hub) : default;
        if (!now.Has)
        {
            if (_visible || _busy) Hide();
            return;
        }

        if (_card == null)
        {
            Canvas canvas = RuntimeUi.ResolveCanvas();
            if (canvas == null) return;
            _card = ObjectiveCard.Create(canvas.transform, "HubObjectiveCard", true, true);
        }
        if (_busy) return; // the routine re-reads the state when it is done

        if (!_visible)
        {
            // Back after a blackout with the same objective: straight to the compact card. A new arrival or a new objective: big first.
            bool full = _expandNext || !now.SameGoal(_shown);
            _expandNext = false;
            _run = StartCoroutine(Appear(now, full));
            return;
        }

        if (!now.SameGoal(_shown))
        {
            bool done = now.Floor == _shown.Floor && now.Step > _shown.Step;
            _run = StartCoroutine(Advance(now, done));
            return;
        }

        if (now.Sub != _shown.Sub)
        {
            _card.Live(now.Goal, null, now.Sub);
            _shown = now;
        }
    }

    private static bool CanShow(AsylumHub hub) =>
        hub != null && GameFlow.IsInHub && !HubState.IsFinale && hub.ContentActive && !hub.IsTransitioning && !hub.Leaving && !hub.BlackoutVisible;

    private void Hide()
    {
        if (_run != null) StopCoroutine(_run);
        _run = null;
        _busy = false;
        _visible = false;
        if (_card != null) _card.HideNow();
    }

    // ---------------------------------------------------------------- routines

    private IEnumerator Appear(Objective o, bool full)
    {
        _busy = true;
        _visible = true;
        _shown = o;
        _card.HideNow();
        _card.Set(Counter(o), o.Goal, null, o.Sub);
        if (full)
        {
            yield return _card.Fade(0f, 1f, 0.3f);
            yield return HoldThenCollapse();
        }
        else
        {
            yield return _card.FadeCompact(0f, 1f, 0.3f);
        }
        _busy = false;
        _run = null;
    }

    /// <summary>The objective changed: a finished step is ticked on the big card first, then the new one comes in big.</summary>
    private IEnumerator Advance(Objective next, bool done)
    {
        _busy = true;
        if (done)
        {
            if (_card.FullAlpha < 0.99f) yield return _card.Expand(0.25f);
            yield return _card.Tick();
        }
        if (_card.CompactAlpha > 0f) yield return _card.FadeCompact(_card.CompactAlpha, 0f, 0.2f);
        if (_card.FullAlpha > 0f) yield return _card.Fade(_card.FullAlpha, 0f, 0.25f);
        yield return new WaitForSecondsRealtime(0.1f);

        _shown = next;
        _card.Set(Counter(next), next.Goal, null, next.Sub);
        yield return _card.Fade(0f, 1f, 0.3f);
        yield return HoldThenCollapse();
        _busy = false;
        _run = null;
    }

    /// <summary>Holds the big card for fullSeconds (cut short if the objective moves on), then collapses it into the compact one.</summary>
    private IEnumerator HoldThenCollapse()
    {
        float until = Time.unscaledTime + fullSeconds;
        while (Time.unscaledTime < until)
        {
            AsylumHub hub = AsylumHub.Active;
            Objective now = CanShow(hub) ? Compute(hub) : default;
            if (!now.SameGoal(_shown)) yield break; // Update takes over from the big card
            if (now.Sub != _shown.Sub) { _card.Live(now.Goal, null, now.Sub); _shown = now; }
            yield return null;
        }
        yield return _card.Collapse(0.45f);
    }

    private static string Counter(Objective o) => $"{o.Step} / {o.Total}";

    // ---------------------------------------------------------------- what to do next

    private Objective Compute(AsylumHub hub)
    {
        int floor = HubState.Floor;
        if (floor < 1 || hub.wingDoors == null || floor > hub.wingDoors.Length) return default;
        WingDoor door = hub.wingDoors[floor - 1];
        if (door == null) return default;

        if (floor != _cachedFloor)
        {
            _cachedFloor = floor;
            _mission = floor <= 4 ? HubMission.ForFloor(floor) : null;
            _keyRun = null;
            if (floor == 5)
            {
                foreach (KeyRunSetPiece k in hub.GetComponentsInChildren<KeyRunSetPiece>(true))
                {
                    if (k.floor == 5) { _keyRun = k; break; }
                }
            }
        }

        string doorGoal = floor == 5 ? $"GO TO THE {door.roomName} · {door.levelName}" : $"GO TO THE {door.wingName} DOOR · {door.levelName}";
        string doorSub = floor == 2 ? LiftSetPiece.ChainedLine : "";

        if (_mission != null)
        {
            switch (_mission.Current)
            {
                case HubMission.Stage.Offered: return Make(floor, 1, 4, _mission.CurrentObjectiveText, "");
                case HubMission.Stage.Active: return Make(floor, 2, 4, _mission.CurrentObjectiveText, _mission.CurrentProgressText);
                case HubMission.Stage.Fetched: return Make(floor, 3, 4, _mission.CurrentObjectiveText, _mission.CurrentProgressText);
                default: return Make(floor, 4, 4, doorGoal, doorSub);
            }
        }
        if (_keyRun != null && _keyRun.Running)
        {
            return _keyRun.KeyHeld ? Make(floor, 2, 2, doorGoal, "") : Make(floor, 1, 2, "FIND THE DIRECTOR'S KEY · RECORDS ROOM", "");
        }
        // No mission or key run on this floor (an incomplete hub): just the door, once it can be used.
        return door.StoryLocked ? default : Make(floor, 1, 1, doorGoal, doorSub);
    }

    private static Objective Make(int floor, int step, int total, string goal, string sub) =>
        string.IsNullOrEmpty(goal) ? default : new Objective { Has = true, Floor = floor, Step = step, Total = total, Goal = goal, Sub = sub ?? "" };
}
