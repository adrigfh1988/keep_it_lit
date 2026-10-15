using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// F121 (spec <c>plannings/f121-hub-breach-plan.md</c>): the sanctuary is broken once. On the floor-5 visit, the moment the player takes the
/// Director's key from the records board the power dies, the Clerk and the patient are gone when the torch comes back, the Commissary slams
/// shut and THE hunter - the Cyborg (or the Orderly robot), on the floor-5 numbers of <see cref="FloorProfile"/> and with what
/// <see cref="HunterMemory"/> has taught it - comes up out of the Surgery wing door and hunts the player across the dark asylum to the Director's
/// door. The hunter is a second <see cref="HubStalker"/> (the hub's own brain, hub NavMesh), not the maze's AIFollower: the maze run stays
/// inactive. A catch is real: one free grab per attempt (the F120 shove through <c>GameOutcome.TryGrabInHub</c>), then a held Second Wind, then
/// <c>GameOutcome.CaughtInHub</c> and the CAUGHT screen, whose Retry replays the breach (the key is already held). Walking into the Director's
/// door while it hunts survives it: <see cref="HubState.BreachDone"/> is set and saved. Everything it changes is put back in OnEnd.
/// Lives under AsylumHub/Life/Breach (built by AsylumHubBuilder.Breach).
/// </summary>
public class HubBreachSetPiece : HubSetPiece
{
    public enum State { Idle, Armed, Waiting, Hunting, Done }

    [SerializeField] private KeyRunSetPiece keyRun;
    [Tooltip("AsylumHub/Life/Breach/Hunter: a HubStalker wearing the hunter's body, inactive in the prefab.")]
    [SerializeField] private HubStalker hunter;

    [Header("Timing")]
    [Tooltip("Retry after a breach death: seconds after the arrival fade before the power dies.")]
    [SerializeField] private float retryDetonateSeconds = 4f;
    [Tooltip("Earliest the hunter comes up (seconds after the power cut), once the player is far enough from the Surgery door.")]
    [SerializeField] private float emergeDelay = 6f;
    [SerializeField] private float emergeMinDistance = 10f;
    [Tooltip("It comes up at this time whatever the player does.")]
    [SerializeField] private float emergeMaxWait = 14f;
    [SerializeField] private float huntRetargetSeconds = 4f;
    [Tooltip("Hunt error: it aims at where you roughly are; the error shrinks from the first to the second value over 90 s.")]
    [SerializeField] private Vector2 huntError = new Vector2(9f, 3f);
    [SerializeField] private float secondWindHoldSeconds = 6f;
    [SerializeField] private float lockerWaitSeconds = 6f;

    [Header("Lights")]
    [SerializeField] private int emergencyCount = 7;
    [SerializeField] private Color emergencyColor = new Color(0.85f, 0.07f, 0.05f);
    [SerializeField] private float emergencyScale = 0.45f;

    private struct SavedLight { public Light light; public Color color; public float intensity, target; public bool enabled; }

    private State _state = State.Idle;
    private bool _retry;
    private float _armedClock, _waitClock, _huntTime, _nextRetarget, _holdUntil, _catchBlockedUntil, _nextMoan, _nextBeat;
    private int _bangs;
    private bool _subscribed, _survived, _grabUsed, _firstSpot, _lockerLine, _shopPending, _emerged;
    private HubStalker.Mode _prevMode = HubStalker.Mode.Idle;
    private Coroutine _routine;
    private GameOutcome _outcome;
    private HunterGrab _grabber;
    private HubDecay _decay;
    private DoorPassage _door4;
    private WingDoor _wing4, _wing5;
    private FloorProfile _profile;
    private readonly List<SavedLight> _emergency = new List<SavedLight>();
    private readonly List<(Light light, bool enabled)> _extraLights = new List<(Light, bool)>();
    private float _emergencyFade;
    private AudioSource _music;
    private float _musicLevel;
    private int _agentType = int.MinValue;

    // The hunter's serialized senses, put back in OnEnd.
    private bool _defaultsSaved;
    private float _dViewRange, _dFocused, _dAngle, _dHear, _dLose, _dSearch, _dStare, _dEye;
    private bool _dRelentless;

    // Locker walk-up (HunterMemory Lockers level >= 1).
    private Locker _lockerSpot;
    private bool _lockerWaiting;
    private float _lockerSince;
    private bool _lockerPeeked;

    public State Current => _state;
    public bool IsComplete => keyRun != null && hunter != null;

    // ---------------------------------------------------------------- lifecycle

    private void Awake()
    {
        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.volume = 0f;
        GameSettings.RouteMusic(_music); // S2: music is music
    }

    protected override void OnBegin()
    {
        _state = State.Idle;
        _survived = false;
        if (!IsComplete)
        {
            Debug.LogError("HubBreachSetPiece: the key run or the breach hunter is missing. Run KEEP IT LIT > Build Missing Pieces (adds Life/Breach). The key run plays as before.", this);
            return;
        }
        if (!HubBreach.ShouldRun(floor)) return;
        _outcome = FindAnyObjectByType<GameOutcome>();
        _grabber = FindAnyObjectByType<HunterGrab>();
        AsylumHub hub = Hub;
        if (_outcome == null || hub == null || hub.wingDoors == null || hub.wingDoors.Length < 5 || hub.wingDoors[3] == null || hub.wingDoors[4] == null)
        {
            Debug.LogError("HubBreachSetPiece: no GameOutcome or no wing 4 / 5 door - the breach is skipped.", this);
            return;
        }
        _wing4 = hub.wingDoors[3];
        _wing5 = hub.wingDoors[4];
        _door4 = _wing4.GetComponent<DoorPassage>();
        _decay = hub.life != null ? hub.life.GetComponentInChildren<HubDecay>(true) : null;
        SaveDefaults();

        _retry = HubBreach.SkipKeyRun;
        _armedClock = 0f;
        _grabUsed = false;
        _state = State.Armed;
        if (!_retry)
        {
            keyRun.KeyTaken -= OnKeyTaken;
            keyRun.KeyTaken += OnKeyTaken;
            _subscribed = true;
        }
    }

    protected override void OnEnd()
    {
        Unsubscribe();
        bool survivedNow = (_state == State.Waiting || _state == State.Hunting) && Hub != null && Hub.Leaving;
        if (survivedNow) MarkSurvived();
        else if (_state == State.Armed && Hub != null && Hub.Leaving) HubBreach.Retrying = false; // walked out before it began: not a retry any more
        if (_routine != null) { StopCoroutine(_routine); _routine = null; }
        StopAllCoroutines();
        Restore();
        _state = State.Idle;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        Unsubscribe(); // OnEnd only runs while Running; the event must never outlive the piece
        if (_music != null) { _music.volume = 0f; _music.Stop(); }
    }

    private void Unsubscribe()
    {
        if (_subscribed && keyRun != null) keyRun.KeyTaken -= OnKeyTaken;
        _subscribed = false;
    }

    private void OnKeyTaken()
    {
        if (_state == State.Armed) Detonate();
    }

    // ---------------------------------------------------------------- the power cut

    private void Detonate()
    {
        if (_state != State.Armed || !Running) return;
        AsylumHub hub = Hub;
        if (hub == null) return;
        _state = State.Waiting;
        _waitClock = 0f;
        _bangs = 0;
        _emerged = false;
        HubBreach.InProgress = true;
        if (_routine != null) StopCoroutine(_routine);
        _routine = StartCoroutine(DetonateRoutine());
    }

    private IEnumerator DetonateRoutine()
    {
        AsylumHub hub = Hub;
        Flashlight torch = hub.Torch;
        Transform player = hub.Player;

        if (!GameAudio.Play2D(GameAudio.Library.powerDown, 1f)) GameAudio.ClipAtPoint(DoorAudio.BuildClunkClip(0.9f, 45f, 0.9f), player.position, 1f);
        if (hub.Ambience != null) hub.Ambience.Mute(); // the machinery dies with the power: silence is the first thing that is different
        if (DialoguePanel.Current != null) DialoguePanel.Current.Close(false);
        keyRun.DismissPursuer();

        if (_decay != null) _decay.SetBlackout(true, null);
        DimExtraLights(hub);
        RefreshPeople(hub);
        if (hub.shop != null)
        {
            if (hub.shop.Inside(player)) _shopPending = true; // never swing the door leaf onto the player
            else hub.shop.Refresh();
        }

        // the three nearest lamps pop, the torch dies for a second
        bool wasOn = torch != null && torch.IsOn;
        if (torch != null) { torch.InputEnabled = false; torch.SetOn(false); }
        List<Light> popped = new List<Light>();
        float[] popAt = { 0f, 0.15f, 0.35f };
        float t = 0f;
        for (int i = 0; i < popAt.Length; i++)
        {
            while (t < popAt[i]) { t += Time.deltaTime; yield return null; }
            Light l = _decay != null ? _decay.NearestSlotLight(player.position, 18f, popped) : null;
            if (l == null) continue;
            popped.Add(l);
            GameAudio.PlayAt(GameAudio.Library.bulbPop, l.transform.position, 1f, 1f, 30f);
        }
        while (t < 1.1f) { t += Time.deltaTime; yield return null; }
        if (torch != null)
        {
            if (wasOn && !torch.IsDropped) torch.SetOn(true);
            torch.Stutter(1.2f);
            torch.InputEnabled = true;
        }
        yield return new WaitForSeconds(0.1f);
        hub.SubtitleLine(L10n.T("hub.breach.dark"), 3.5f);

        // red emergency lights along the way to the Director's door fade in
        PickEmergencyLights(hub);
        _emergencyFade = 0f;
        while (_emergencyFade < 1f)
        {
            _emergencyFade = Mathf.Min(1f, _emergencyFade + Time.deltaTime / 1.5f);
            ApplyEmergency(_emergencyFade);
            yield return null;
        }
        _routine = null;
    }

    private void DimExtraLights(AsylumHub hub)
    {
        _extraLights.Clear();
        // the lights HubDecay exempts: the kitchen tray, the classroom board and the dining table (the Commissary closes its own)
        if (hub.meal != null && hub.meal.light != null) AddExtra(hub.meal.light);
        if (hub.lesson != null && hub.lesson.light != null) AddExtra(hub.lesson.light);
        if (hub.supper != null && hub.supper.tableLight != null) AddExtra(hub.supper.tableLight);
    }

    private void AddExtra(Light l)
    {
        _extraLights.Add((l, l.enabled));
        l.enabled = false;
    }

    private void PickEmergencyLights(AsylumHub hub)
    {
        _emergency.Clear();
        if (_decay == null || _wing5 == null || _wing5.arrivalPoint == null) return;
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = HubAgentType(), areaMask = NavMesh.AllAreas };
        NavMeshPath path = new NavMeshPath();
        List<Vector3> points = new List<Vector3>();
        if (NavMesh.SamplePosition(hub.Player.position, out NavMeshHit from, 3f, filter) && NavMesh.CalculatePath(from.position, _wing5.arrivalPoint.position, filter, path) && path.corners.Length > 1)
        {
            float carry = 0f;
            points.Add(path.corners[0]);
            for (int i = 1; i < path.corners.Length; i++)
            {
                Vector3 a = path.corners[i - 1], b = path.corners[i];
                float len = Vector3.Distance(a, b);
                float d = 6f - carry;
                while (d < len)
                {
                    points.Add(Vector3.Lerp(a, b, d / len));
                    d += 6f;
                }
                carry = (carry + len) % 6f;
            }
            points.Add(path.corners[path.corners.Length - 1]);
        }
        HashSet<Light> taken = new HashSet<Light>();
        foreach (Vector3 p in points)
        {
            if (_emergency.Count >= emergencyCount) break;
            Light l = _decay.NearestSlotLight(p + Vector3.up * 2f, 5.5f, taken);
            if (l == null) continue;
            taken.Add(l);
            float baseIntensity = l.intensity > 0.2f ? l.intensity : 1.5f;
            _emergency.Add(new SavedLight { light = l, color = l.color, intensity = l.intensity, target = baseIntensity * emergencyScale, enabled = l.enabled });
        }
        foreach (SavedLight s in _emergency)
        {
            _decay.EmergencyOn(s.light, emergencyColor * 1.5f);
            s.light.color = emergencyColor;
            s.light.intensity = 0f;
        }
        Debug.Log($"HubBreach: {_emergency.Count} emergency lights along the way to the Director's door (of {(_decay != null ? _decay.LightCount : 0)} decay lights).");
    }

    private void ApplyEmergency(float k)
    {
        foreach (SavedLight s in _emergency) if (s.light != null) s.light.intensity = s.target * k;
    }

    private void RefreshPeople(AsylumHub hub)
    {
        foreach (HubNpc npc in hub.GetComponentsInChildren<HubNpc>(true)) if (npc != null) npc.Refresh();
        foreach (HubPatient patient in hub.GetComponentsInChildren<HubPatient>(true)) if (patient != null) patient.Refresh();
    }

    // ---------------------------------------------------------------- update

    private void Update()
    {
        if (!Running || _state == State.Idle) return;
        AsylumHub hub = Hub;
        if (hub == null || hub.Player == null) return;

        // The Director's door was used: the walk-in has begun (Leaving is set in the same frame as IsTransitioning, so it is tested first).
        if ((_state == State.Waiting || _state == State.Hunting) && hub.Leaving) { Survive(); return; }
        if (_state == State.Done) return;

        bool held = hub.IsTransitioning || Time.timeScale <= 0f;
        if (_shopPending && hub.shop != null && !hub.shop.Inside(hub.Player)) { _shopPending = false; hub.shop.Refresh(); }
        if (held) return;

        switch (_state)
        {
            case State.Armed:
                if (_retry)
                {
                    _armedClock += Time.deltaTime;
                    if (_armedClock >= retryDetonateSeconds) Detonate();
                }
                break;
            case State.Waiting:
                WaitTick(hub);
                break;
            case State.Hunting:
                HuntTick(hub);
                break;
        }
    }

    private void WaitTick(AsylumHub hub)
    {
        _waitClock += Time.deltaTime;
        Vector3 door = DoorwayPoint();
        if (_bangs == 0 && _waitClock >= 2.5f) Bang(door, false);
        else if (_bangs == 1 && _waitClock >= 5f) Bang(door, true);

        if (_waitClock < emergeDelay || _routine != null && _waitClock < emergeDelay + 1f) return;
        Vector3 flat = hub.Player.position - _wing4.arrivalPoint.position;
        flat.y = 0f;
        if (flat.magnitude >= emergeMinDistance || _waitClock >= emergeMaxWait) Emerge(hub);
    }

    private Vector3 DoorwayPoint()
    {
        if (_door4 != null && _door4.IsComplete) return _door4.Doorway(_wing4.arrivalPoint.position.y) + Vector3.up * 1.2f;
        return _wing4.transform.position + Vector3.up * 1.2f;
    }

    private static AudioClip _bangClip;

    private void Bang(Vector3 at, bool last)
    {
        _bangs++;
        if (!GameAudio.PlayAt(GameAudio.Library.liftBang, at, 1f, 1f, 60f))
        {
            if (_bangClip == null) _bangClip = DoorAudio.BuildClunkClip(0.7f, 55f, 0.9f);
            GameAudio.ClipAtPoint(_bangClip, at, 1f);
        }
        if (Hub != null && Hub.Torch != null) Hub.Torch.Stutter(0.2f);
        if (last && Hub != null) Hub.SubtitleLine(L10n.T("hub.breach.followed"), 3f);
    }

    private void Emerge(AsylumHub hub)
    {
        if (_emerged) return;
        _emerged = true;
        ApplyHunterSetup();
        hunter.gameObject.SetActive(true);
        if (!hunter.Place(_wing4.arrivalPoint.position))
        {
            Debug.LogError("HubBreachSetPiece: wing 4's arrival point is not on the Hub navmesh - the breach ends without the hunter.", this);
            hunter.gameObject.SetActive(false);
            // never a dark hub with nothing in it: put the lights back and call it survived
            _state = State.Done;
            Restore();
            MarkSurvived();
            return;
        }
        hunter.transform.rotation = Quaternion.Euler(0f, _wing4.arrivalPoint.eulerAngles.y, 0f);
        if (_door4 != null && _door4.IsComplete)
        {
            Vector3 doorway = DoorwayPoint();
            DoorPassageFx.Latch(doorway);
            DoorPassageFx.Draft(doorway, _door4.draft);
            StartCoroutine(SwingDoor());
        }
        GameAudio.PlayAt(GameAudio.Library.hunterMoan.Has ? GameAudio.Library.hunterMoan : GameAudio.Library.orderlyPatrol, hunter.transform.position + Vector3.up * 1.6f, 1f, 1f, 40f);
        _state = State.Hunting;
        _catchBlockedUntil = Time.time + 3f; // a Retry puts the player on the arrival point the hunter comes up at: a moment to move
        _huntTime = 0f;
        _nextRetarget = Time.time;
        _holdUntil = 0f;
        _nextMoan = Time.time + Random.Range(7f, 13f);
        _nextBeat = Time.time;
        _firstSpot = true;
        _lockerLine = true;
        _prevMode = HubStalker.Mode.Idle;
        AudioClip chase = GameAudio.Library.chaseMusic.First();
        if (_music != null && chase != null)
        {
            _music.clip = chase;
            _musicLevel = 0f;
            _music.volume = 0f;
            _music.Play();
        }
    }

    private IEnumerator SwingDoor()
    {
        float t = 0f;
        while (t < 0.5f)
        {
            t += Time.deltaTime;
            if (_door4 != null) _door4.SetOpen(Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / 0.5f)));
            yield return null;
        }
    }

    // ---------------------------------------------------------------- the hunter

    private void SaveDefaults()
    {
        if (_defaultsSaved || hunter == null) return;
        _defaultsSaved = true;
        _dViewRange = hunter.viewRange;
        _dFocused = hunter.focusedViewRange;
        _dAngle = hunter.viewAngle;
        _dHear = hunter.hearRange;
        _dLose = hunter.loseSightSeconds;
        _dSearch = hunter.searchSeconds;
        _dStare = hunter.stareSeconds;
        _dRelentless = hunter.relentless;
        _dEye = hunter.eyeHeight;
    }

    /// <summary>THE hunter's floor-5 numbers, sharpened by what it has learned. Everything here is undone in <see cref="Restore"/>.</summary>
    private void ApplyHunterSetup()
    {
        _profile = FloorProfile.For(HubBreach.Floor);
        int sprint = HunterMemory.Level(HunterMemory.Habit.Sprint);
        int dark = HunterMemory.Level(HunterMemory.Habit.Dark);
        hunter.viewRange = Mathf.Min(16f, _profile.ViewDistance);
        hunter.focusedViewRange = hunter.viewRange + 6f;
        hunter.viewAngle = _profile.ViewAngle;
        hunter.darkViewRange = hunter.viewRange * (0.45f + 0.15f * dark);
        hunter.hearRange = 8f * (1f + 0.3f * sprint);
        hunter.loseSightSeconds = _profile.LoseSightTime;
        hunter.searchSeconds = 6f;
        hunter.stareSeconds = 0.45f;
        hunter.relentless = false;
        hunter.chaseSpeedOverride = Mathf.Min(4.2f, _profile.RunSpeed + 0.2f * sprint);
        hunter.eyeHeight = 1.8f;
        hunter.spotSlot = GameAudio.Library.hunterAlert;
        hunter.stepLayerSlot = GameAudio.Library.hunterScrape;
        hunter.catchHandler = OnHunterCatch;
        hunter.ResetCatches();
    }

    private void ResetHunter()
    {
        if (hunter == null) return;
        hunter.catchHandler = null;
        hunter.darkViewRange = 0f;
        hunter.spotSlot = null;
        hunter.stepLayerSlot = null;
        hunter.chaseSpeedOverride = 0f;
        hunter.catchLineKey = null;
        if (_defaultsSaved)
        {
            hunter.viewRange = _dViewRange;
            hunter.focusedViewRange = _dFocused;
            hunter.viewAngle = _dAngle;
            hunter.hearRange = _dHear;
            hunter.loseSightSeconds = _dLose;
            hunter.searchSeconds = _dSearch;
            hunter.stareSeconds = _dStare;
            hunter.relentless = _dRelentless;
            hunter.eyeHeight = _dEye;
        }
        hunter.ClearOnReset();
        hunter.Stop();
        hunter.gameObject.SetActive(false);
    }

    private void HuntTick(AsylumHub hub)
    {
        float dt = Time.deltaTime;
        _huntTime += dt;
        HubStalker.Mode mode = hunter.CurrentMode;
        bool chasing = mode == HubStalker.Mode.Spotted || mode == HubStalker.Mode.Chase;
        Vector3 head = hunter.transform.position + Vector3.up * 1.6f;
        float dist = Vector3.Distance(hunter.transform.position, hub.Player.position);

        // the first time it sees you
        if (chasing && (_prevMode != HubStalker.Mode.Spotted && _prevMode != HubStalker.Mode.Chase) && _firstSpot)
        {
            _firstSpot = false;
            if (!GameAudio.Play2D(GameAudio.Library.spottedSting, 0.9f)) { /* empty slot: silent */ }
            hub.SubtitleLine(L10n.T("hub.breach.run"), 3f);
            HunterMemory.Habit top = HunterMemory.Habit.Lockers;
            int best = 0;
            foreach (HunterMemory.Habit h in new[] { HunterMemory.Habit.Lockers, HunterMemory.Habit.Dark, HunterMemory.Habit.Sprint })
            {
                int level = HunterMemory.Level(h);
                if (level > best) { best = level; top = h; }
            }
            if (best >= 1) hub.QueueSubtitleLine(HunterMemory.LessonText(top), 3f);
        }
        _prevMode = mode;

        LockerTick(hub, mode);

        // retarget: it walks to where you roughly are; the error closes in over 90 s
        if (Time.time >= _holdUntil && Time.time >= _nextRetarget && !_lockerWaiting && (mode == HubStalker.Mode.Idle || hunter.IsHunting))
        {
            _nextRetarget = Time.time + huntRetargetSeconds;
            Retarget(hub, Mathf.Lerp(huntError.x, huntError.y, Mathf.Clamp01(_huntTime / 90f)));
        }

        // moans while it hunts
        if (mode == HubStalker.Mode.Patrol && Time.time >= _nextMoan)
        {
            _nextMoan = Time.time + Random.Range(7f, 13f);
            if (!GameAudio.PlayAt(GameAudio.Library.hunterMoan, head, 0.9f, 1f, 26f)) GameAudio.PlayAt(GameAudio.Library.orderlyPatrol, head, 0.9f, 1f, 26f);
        }

        // the heartbeat quickens as it closes in
        if (dist < 20f && Time.time >= _nextBeat)
        {
            float closeness = 1f - Mathf.Clamp01((dist - 4f) / 16f);
            _nextBeat = Time.time + Mathf.Lerp(1.2f, 0.45f, closeness);
            GameAudio.Play2D(GameAudio.Library.heartbeat, 0.7f);
        }

        // the chase track only while it chases
        if (_music != null)
        {
            _musicLevel = Mathf.MoveTowards(_musicLevel, chasing ? 0.8f : 0f, Time.unscaledDeltaTime);
            _music.volume = Mathf.Clamp01(_musicLevel * GameAudio.Library.chaseMusic.volume);
        }
    }

    private void Retarget(AsylumHub hub, float error)
    {
        Vector2 offset = Random.insideUnitCircle * error;
        Vector3 target = hub.Player.position + new Vector3(offset.x, 0f, offset.y);
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = HubAgentType(), areaMask = NavMesh.AllAreas };
        if (NavMesh.SamplePosition(target, out NavMeshHit hit, 3f, filter)) hunter.Hunt(hit.position, _profile.WalkSpeed);
        else if (NavMesh.SamplePosition(hub.Player.position, out hit, 3f, filter)) hunter.Hunt(hit.position, _profile.WalkSpeed);
    }

    /// <summary>A hunter that has learned the lockers watched you get in: it walks to the door and waits, then moves on. It never opens a locker to kill (F104 B3).</summary>
    private void LockerTick(AsylumHub hub, HubStalker.Mode mode)
    {
        int level = HunterMemory.Level(HunterMemory.Habit.Lockers);
        bool hidden = hub.Stealth != null && hub.Stealth.Hidden;
        if (!hidden)
        {
            if (_lockerSpot != null || _lockerWaiting) { _lockerSpot = null; _lockerWaiting = false; _nextRetarget = Time.time; } // out again: hunt on
            return;
        }
        if (level < 1) return;
        if (_lockerSpot == null && (mode == HubStalker.Mode.Chase || mode == HubStalker.Mode.Spotted))
        {
            _lockerSpot = hub.Stealth.CurrentHidingSpot as Locker;
            if (_lockerSpot == null) return;
            _lockerWaiting = true;
            _lockerPeeked = false;
            _lockerSince = 0f;
            hunter.Hunt(_lockerSpot.FrontPosition, _profile.RunSpeed, true);
            if (_lockerLine) { _lockerLine = false; hub.SubtitleLine(L10n.T("hub.breach.locker"), 3f); }
            return;
        }
        if (_lockerSpot == null || !_lockerWaiting) return;
        Vector3 to = _lockerSpot.FrontPosition - hunter.transform.position;
        to.y = 0f;
        if (to.magnitude > 1.4f) return;
        if (_lockerSince <= 0f)
        {
            _lockerSince = Time.time;
            hunter.Stop();
            GameAudio.PlayAt(GameAudio.Library.hunterMoan, hunter.transform.position + Vector3.up * 1.6f, 0.9f, 1f, 26f);
        }
        Vector3 face = _lockerSpot.transform.position - hunter.transform.position;
        face.y = 0f;
        if (face.sqrMagnitude > 0.01f) hunter.transform.rotation = Quaternion.RotateTowards(hunter.transform.rotation, Quaternion.LookRotation(face), 240f * Time.deltaTime);
        if (level >= 2 && !_lockerPeeked && Time.time - _lockerSince > 2f)
        {
            _lockerPeeked = true;
            _lockerSpot.Peek(2.5f); // the door creaks ajar: you are still hidden, but only just
        }
        if (Time.time - _lockerSince > lockerWaitSeconds)
        {
            _lockerWaiting = false;
            _lockerSpot = null;
            _holdUntil = 0f;
            _nextRetarget = Time.time;
            // it gives up this locker: wander off to a point well away from it
            Vector2 away = Random.insideUnitCircle.normalized * Random.Range(10f, 15f);
            NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = HubAgentType(), areaMask = NavMesh.AllAreas };
            if (NavMesh.SamplePosition(hunter.transform.position + new Vector3(away.x, 0f, away.y), out NavMeshHit hit, 4f, filter)) hunter.Hunt(hit.position, _profile.WalkSpeed);
        }
    }

    // ---------------------------------------------------------------- the catch

    /// <summary>HubStalker.TryCatch asks this instead of the soft hub catch. True = taken. One free grab per attempt, then the real thing.</summary>
    private bool OnHunterCatch(Vector3 head)
    {
        AsylumHub hub = Hub;
        if (hub == null || _outcome == null || _state != State.Hunting) return false;
        if (Time.time < _catchBlockedUntil || GameOutcome.IsOver || _outcome.IsEnding) return false;

        // a player cornered in a locker is pulled out first
        ForceLeaveLockers(hub);
        _lockerSpot = null;
        _lockerWaiting = false;

        if (!_grabUsed)
        {
            bool started = _outcome.TryGrabInHub(hunter.transform, 1.8f, OnGrabRelease, () => { if (Hub != null) Hub.SetScripted(false); });
            if (started)
            {
                _grabUsed = true;
                _catchBlockedUntil = Time.time + 4f;
                hub.SetScripted(true);
                GameAudio.Play2D(GameAudio.Library.caught, 0.8f);
                return true;
            }
        }

        // the real thing: Second Wind if held, else the CAUGHT screen
        hub.SetScripted(true);
        hunter.Stop();
        if (_music != null) _music.volume = 0f;
        _outcome.CaughtInHub(head, OnSecondWind);
        return true;
    }

    private void OnGrabRelease(Vector3 hunterPos, Vector3 back, Vector3 landing)
    {
        // it lets go, steps back and stands for the stun; then it searches around the torch
        hunter.Place(hunterPos + back * 0.8f);
        hunter.Stop();
        float stun = _grabber != null ? _grabber.StunSeconds : 3f;
        _holdUntil = Time.time + stun;
        _nextRetarget = _holdUntil;
        StartCoroutine(HuntTorchAfter(stun, landing));
    }

    private IEnumerator HuntTorchAfter(float seconds, Vector3 torch)
    {
        yield return new WaitForSeconds(seconds);
        if (!Running || hunter == null || _state != State.Hunting || _profile == null) yield break;
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = HubAgentType(), areaMask = NavMesh.AllAreas };
        if (NavMesh.SamplePosition(torch, out NavMeshHit hit, 3f, filter)) hunter.Hunt(hit.position, _profile.WalkSpeed);
    }

    /// <summary>Runs at full black inside SecondWindSequence: the hunter is moved to the farthest wing door (never wing 5's) and held.</summary>
    private void OnSecondWind()
    {
        AsylumHub hub = Hub;
        if (hub == null) return;
        ForceLeaveLockers(hub);
        NavMeshQueryFilter filter = new NavMeshQueryFilter { agentTypeID = HubAgentType(), areaMask = NavMesh.AllAreas };
        Vector3 best = _wing4.arrivalPoint.position;
        float bestLen = -1f;
        NavMeshPath path = new NavMeshPath();
        for (int i = 0; i < 4 && i < hub.wingDoors.Length; i++)
        {
            WingDoor w = hub.wingDoors[i];
            if (w == null || w.arrivalPoint == null) continue;
            float len = Vector3.Distance(w.arrivalPoint.position, hub.Player.position);
            if (NavMesh.SamplePosition(hub.Player.position, out NavMeshHit a, 3f, filter) && NavMesh.SamplePosition(w.arrivalPoint.position, out NavMeshHit b, 3f, filter)
                && NavMesh.CalculatePath(a.position, b.position, filter, path) && path.status == NavMeshPathStatus.PathComplete)
            {
                len = 0f;
                for (int c = 1; c < path.corners.Length; c++) len += Vector3.Distance(path.corners[c - 1], path.corners[c]);
            }
            if (len > bestLen) { bestLen = len; best = w.arrivalPoint.position; }
        }
        hunter.Place(best);
        hunter.Stop();
        hub.SetScripted(false);
        _catchBlockedUntil = Time.time + 6f;
        _holdUntil = Time.time + secondWindHoldSeconds;
        _nextRetarget = _holdUntil;
    }

    private static void ForceLeaveLockers(AsylumHub hub)
    {
        if (hub.life == null) return;
        foreach (Locker locker in hub.life.GetComponentsInChildren<Locker>(true)) if (locker != null && locker.Occupied) locker.ForceLeave();
    }

    // ---------------------------------------------------------------- survived

    private void Survive()
    {
        if (_state == State.Done) return;
        _state = State.Done;
        MarkSurvived();
        if (hunter != null)
        {
            hunter.Stop();
            GameAudio.PlayAt(GameAudio.Library.hunterAlert, hunter.transform.position + Vector3.up * 1.6f, 0.9f, 1f, 40f); // it loses you at the door
        }
    }

    /// <summary>Idempotent: the breach has been lived through - never again this campaign, and the save says so.</summary>
    private void MarkSurvived()
    {
        if (_survived) return;
        _survived = true;
        HubState.BreachDone = true;
        HubBreach.Retrying = false;
        SaveSystem.WriteCheckpoint(GameFlow.CurrentFloor, false);
    }

    // ---------------------------------------------------------------- restore

    /// <summary>Puts back everything the breach touched, in the order the finale's later snapshot needs: lights, then people and shop.</summary>
    private void Restore()
    {
        foreach (SavedLight s in _emergency)
        {
            if (s.light == null) continue;
            s.light.color = s.color;
            s.light.intensity = s.intensity;
        }
        _emergency.Clear();
        if (_decay != null) _decay.SetBlackout(false, null);
        foreach ((Light light, bool enabled) in _extraLights) if (light != null) light.enabled = enabled;
        _extraLights.Clear();

        HubBreach.InProgress = false;
        AsylumHub hub = Hub;
        if (hub != null)
        {
            RefreshPeople(hub);
            if (hub.shop != null) hub.shop.Refresh();
            if (hub.Torch != null) hub.Torch.InputEnabled = true;
        }
        _shopPending = false;
        if (_door4 != null) _door4.ResetClosed();
        if (_music != null) { _music.volume = 0f; _music.Stop(); }
        if (_grabber != null) _grabber.EndForTransition();
        ResetHunter();
        _lockerSpot = null;
        _lockerWaiting = false;
        _emerged = false;
    }

    private int HubAgentType()
    {
        if (_agentType != int.MinValue) return _agentType;
        _agentType = -1;
        for (int i = 0; i < NavMesh.GetSettingsCount(); i++)
        {
            NavMeshBuildSettings s = NavMesh.GetSettingsByIndex(i);
            if (NavMesh.GetSettingsNameFromID(s.agentTypeID) == "Hub") _agentType = s.agentTypeID;
        }
        return _agentType;
    }

    // ---------------------------------------------------------------- debug (testing only; never reachable from the player's UI)

    private static HubBreachSetPiece Find() => FindAnyObjectByType<HubBreachSetPiece>(FindObjectsInactive.Include);

    /// <summary>Detonates an armed breach now, ignoring the key.</summary>
    public static string DebugDetonate()
    {
        HubBreachSetPiece b = Find();
        if (b == null) return "no HubBreachSetPiece";
        if (b._state != State.Armed) return "not armed: " + b._state;
        b.Detonate();
        return "detonated";
    }

    public static string DebugState()
    {
        HubBreachSetPiece b = Find();
        if (b == null) return "no HubBreachSetPiece";
        AsylumHub hub = AsylumHub.Active;
        string dist = hub != null && hub.Player != null && b.hunter != null && b.hunter.gameObject.activeInHierarchy ? Vector3.Distance(hub.Player.position, b.hunter.transform.position).ToString("F1") : "-";
        return $"state={b._state} running={b.Running} inProgress={HubBreach.InProgress} hunter={(b.hunter != null && b.hunter.gameObject.activeInHierarchy ? b.hunter.CurrentMode.ToString() : "off")} dist={dist} grabUsed={b._grabUsed} " +
               $"emergency={b._emergency.Count} blackout={(b._decay != null && b._decay.InBlackout)} done={HubState.BreachDone} retrying={HubBreach.Retrying} mode={HubBreach.Mode}";
    }

    /// <summary>Calls the catch handler as if the hunter had touched the player.</summary>
    public static string DebugCatch()
    {
        HubBreachSetPiece b = Find();
        if (b == null || b.hunter == null) return "no breach";
        b._catchBlockedUntil = 0f;
        return "catch handler -> " + b.OnHunterCatch(b.hunter.transform.position + Vector3.up * 1.8f);
    }
}
