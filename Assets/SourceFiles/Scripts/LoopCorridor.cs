using System.Collections.Generic;
using StarterAssets;
using TMPro;
using UnityEngine;

/// <summary>
/// F105 C4 + F106: the looping corridor (P.T.), the runtime half. MazeGenerator.BuildLoopCorridor builds the geometry outside the maze (post-bake,
/// so the hunter's NavMesh never reaches it) and hands over <see cref="Setup"/>; SetUpAtmosphere then binds the player. The corridor is an L: a
/// vestibule behind the opening in the maze's outer wall, a first leg, a corner, a second leg whose end is built exactly like the first leg's
/// corner. Crossing the trigger in the second leg carries the player to the same place in the first, turned by the corner's angle, so nothing in
/// front of them changes at the cut.
///
/// It can be walked both ways (12 Oct 2026). Once the opening is walled up the vestibule reads, from the first leg, exactly as the corner reads
/// from the second (a far wall, a mouth on the same side), so walking back down the first leg past the return line carries the player to the same
/// place in the second leg, walking back - the same seamless cut in reverse. No blackout, no half turn: turning back is just walking the other way.
///
/// F106 makes it a puzzle: spot the anomaly. Lap 1 is normal and the opening behind the player is still real until they first reach the corner;
/// there it seals and the rule is shown. From then on each lap is normal or has exactly one <see cref="LoopAnomaly"/>, applied at the cut, in the
/// leg round the corner ahead of the way the lap is walked. Keep walking the way you were going on a normal lap, or turn round and walk the other
/// way on an anomalous one: right adds 1 to the count, wrong resets it and brings the <see cref="LoopVisitor"/> closer. Reaching the target count
/// throws the player out into the maze with the floor map and supplies; the Visitor reaching them is a real death (or a Second Wind, which also
/// throws them out, without the map). With too few anomalies the old fixed passes run instead (a lamp dies, blood and a flicker, a robot at the
/// corner, then out), with no seal and no Visitor.
///
/// Gameplay-inert otherwise: no hunter, star, key or switch is ever in it, and the NavMesh agent cannot enter. <see cref="UsedThisCampaign"/> is a
/// campaign-scoped static: set when a floor that had a loop is cleared (GameOutcome.FloorCleared), reset at SubsystemRegistration and in
/// GameFlow.ReturnToMenu, saved in SaveData. Setting it on the clear rather than on entry keeps a Retry of the same floor identical.
/// </summary>
public class LoopCorridor : MonoBehaviour
{
    /// <summary>Everything MazeGenerator.BuildLoopCorridor builds, plus where and when things happen.</summary>
    public struct Setup
    {
        /// <summary>Frame at the start of the first leg: origin on the floor where it leaves the vestibule, forward along the leg.</summary>
        public Transform legA;
        /// <summary>The same frame for the second leg (origin where it leaves the corner), turned by the corner's angle.</summary>
        public Transform legB;
        /// <summary>Distance along the second leg at which a player walking forward is carried back to the first (m).</summary>
        public float triggerDistance;
        /// <summary>Length of each leg including the cell at its end (m).</summary>
        public float legLength;
        /// <summary>Half the width the player can be at inside a leg (m).</summary>
        public float legHalfWidth;
        /// <summary>F106: along the first leg, where the corner cell begins: reaching it closes the trap (m).</summary>
        public float trapDistance;
        /// <summary>
        /// Along the first leg, the return line: a player walking back toward the vestibule who crosses it is carried to the same place in the
        /// second leg. As far from the vestibule's centre as the trigger is from the end cell's, so both cuts hide the same amount (m).
        /// </summary>
        public float returnDistance;
        /// <summary>F106: the player's distance along the whole path (first leg, then the second) at the centre of the corner cell (m).</summary>
        public float cornerPathDistance;
        /// <summary>The whole corridor, world space: being inside it ducks the sound and counts as "in the loop".</summary>
        public Bounds footprint;
        /// <summary>Where leaving puts the player: the maze's entrance cell centre, facing into the maze.</summary>
        public Vector3 exitPosition;
        public float exitYaw;
        /// <summary>The first leg's lamps in order (rear, front, then the front one's twin in the vestibule's stub). The second leg's are in <see cref="mirrorLamps"/> (rear, front).</summary>
        public WallLamp[] lamps;
        public WallLamp[] mirrorLamps;
        /// <summary>
        /// Inactive until an anomaly needs them: blood on the floor, a locker standing open against the wall, a handprint on it - in the second
        /// leg for a lap walked forward, and their twins at the same places in the first leg for a lap walked in reverse.
        /// </summary>
        public GameObject blood, locker, handprint;
        public GameObject bloodReverse, lockerReverse, handprintReverse;
        /// <summary>F106: the chalk numerals that show the count, one on each leg's wall. Inactive until the trap closes.</summary>
        public TMP_Text[] counters;
        public TMP_Text[] signVerdicts, signCounts; // the hanging light-box signs, one per leg and both faces so the cuts stay seamless (empty = none)
        /// <summary>Inactive until the opening is walled up.</summary>
        public GameObject sealWall;
        public Vector3 sealPoint;
        /// <summary>
        /// The robot's spot: the mouth of the corner ahead, and the way it faces (away from the player) - the first leg's end for a lap walked
        /// forward, the second leg's start for one walked in reverse. Its body comes from the template handed to <see cref="Bind"/>.
        /// </summary>
        public Vector3 figurePosition, figureForward;
        public Vector3 figurePositionReverse, figureForwardReverse;
        public TerrorSettings terror;
        public Vector2Int entranceCell;
        public Tuning tuning;
        public PuzzleTuning puzzle;
    }

    /// <summary>The tunables MazeGenerator keeps under its F105 C4 header.</summary>
    public struct Tuning
    {
        public float duckGain, duckSeconds, buzzVolume, buzzMaxDistance;
        public float figureDelay, figureVanishDistance, figureTestHeight, figureBeamRangeFraction, figureBeamAngleFraction;
        public Vector2 flickerGap;
        public float sealHideDistance;
    }

    /// <summary>The tunables MazeGenerator keeps under its F106 header.</summary>
    public struct PuzzleTuning
    {
        public int targetCount, minAnomalies;
        public ShopItem[] rewardItems;
        public bool rewardRefillsTorch;
        public float anomalyChance, ruleSeconds, chimeVolume, thudVolume;
        public Color redLamp;
        public float footstepsBehind, footstepsSeconds, footstepsStride, footstepsVolume, footstepsMaxDistance;
        public Vector2 footstepsPitch;
        public float visitorStartDistance, visitorCreepSpeed, visitorMistakeStep, visitorReliefStep, visitorCatchDistance;
        public float visitorChestHeight, visitorHeadHeight, visitorViewMargin;
        public float visitorStepRange, visitorStepStride, visitorStepVolume, visitorStepPitch;
    }

    private const float BuzzMinDistance = 1.5f;
    private const float SealLookDot = 0.3f;
    private const int ScriptedLaps = 4; // the fallback runs C4's four passes: the fourth crossing leaves
    private const string Separator = "  ·  "; // between the supplies in the reward line, as the DESCENT cache lines read

    private static LoopCorridor s_current;

    private Setup _setup;
    private Transform _player;
    private CharacterController _body;
    private ThirdPersonController _controller;
    private Transform _camera;
    private Flashlight _flashlight;
    private HorrorAudioDirector _audio;
    private MazeAmbience _ambience;
    private GameOutcome _outcome;
    private PlayerHud _hud;
    private FloorMapUi _map;
    private ThrowController _throws;
    private AudioSource[] _buzz;
    private WallLamp[] _lamps;
    private LoopVisitor _visitor;
    private readonly List<LoopAnomaly> _pool = new List<LoopAnomaly>();
    private LoopAnomaly _active;
    private LoopAnomaly _lastLap;

    private bool _inside;
    private bool _sealPending;
    private bool _trapped;
    private bool _scripted;
    private bool _offerShown;
    private bool _reverse; // the way the current lap is walked: false = first leg, corner, second leg; true = the other way
    private int _lap = 1;
    private int _count;
    private float _pathPosition;
    private float _previousTrigger = -1f;
    private float _previousReturn = -1f;

    /// <summary>True once a floor that had the loop was cleared: later floors of this campaign never roll one.</summary>
    public static bool UsedThisCampaign { get; private set; }

    /// <summary>Which lap the player is on, 1 (the first walk) upward.</summary>
    public int Lap => _lap;

    /// <summary>F106: how many right decisions in a row the player has made.</summary>
    public int Count => _count;

    /// <summary>True while the player is inside the corridor.</summary>
    public bool IsInside => _inside;

    /// <summary>F106: true from the moment the opening sealed behind the player; the puzzle is on.</summary>
    public bool IsTrapped => _trapped;

    /// <summary>True once the opening has been walled up.</summary>
    public bool IsSealed { get; private set; }

    /// <summary>True when the opening is waiting to be walled up (the hatch is open or the loop is done) but the player is inside or looking at it.</summary>
    public bool SealPending => _sealPending;

    /// <summary>For the anomalies and the Visitor: what MazeGenerator built and how it is tuned.</summary>
    internal Setup Config => _setup;
    internal Transform PlayerTransform => _player;
    internal Transform CameraTransform => _camera;
    internal Flashlight Torch => _flashlight;
    /// <summary>True while the current lap is walked back (second leg, corner, first leg): "behind the player" is then further along the path.</summary>
    internal bool IsReverse => _reverse;

    /// <summary>Called from GameOutcome.FloorCleared: the floor that was just cleared counts as the campaign's one loop, if it had one.</summary>
    public static void CommitFloor()
    {
        if (s_current == null) return;

        UsedThisCampaign = true;
        if (GameFlow.IsDescent) DescentRun.LastLoopDepth = DescentRun.Depth; // F107: a descent spaces loops by depth, not once per campaign
    }

    public static void ResetCampaign()
    {
        UsedThisCampaign = false;
    }

    public static void CaptureTo(SaveData data)
    {
        data.loopCorridorUsed = UsedThisCampaign;
    }

    public static void RestoreFrom(SaveData data)
    {
        UsedThisCampaign = data.loopCorridorUsed;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        ResetCampaign();
        s_current = null;
    }

    /// <summary>Hands over the built corridor. Called once, right after MazeGenerator has built it.</summary>
    public void Define(Setup setup)
    {
        _setup = setup;
        s_current = this;
        _lamps = new WallLamp[setup.lamps.Length + setup.mirrorLamps.Length];
        setup.lamps.CopyTo(_lamps, 0);
        setup.mirrorLamps.CopyTo(_lamps, setup.lamps.Length);
        _buzz = new AudioSource[_lamps.Length];
        for (int i = 0; i < _lamps.Length; i++) _buzz[i] = AddBuzz(_lamps[i]);
    }

    /// <summary>
    /// Hands over the player's components, the sound that is ducked inside, the InteractableKit's Stalker template (the robot at the corner), the
    /// hunter whose body the Visitor copies, the outcome that ends the run, the HUD, the floor map the puzzle unlocks and the throw controller
    /// the reward's bottles go to. Any may be null; that part is then skipped (no outcome or no hunter body: no Visitor; no template: no robot).
    /// </summary>
    public void Bind(Transform player, ThirdPersonController controller, Camera camera, Flashlight flashlight, HorrorAudioDirector audio, MazeAmbience ambience,
        Stalker figureTemplate, AIFollower hunter, GameOutcome outcome, PlayerHud hud, FloorMapUi map, ThrowController throws)
    {
        _throws = throws;
        _player = player;
        _body = player != null ? player.GetComponent<CharacterController>() : null;
        _controller = controller;
        _camera = camera != null ? camera.transform : null;
        _flashlight = flashlight;
        _audio = audio;
        _ambience = ambience;
        _outcome = outcome;
        _hud = hud;
        _map = map;

        BuildAnomalies(figureTemplate);
        if (!_scripted && outcome != null)
        {
            _visitor = LoopVisitor.TryCreate(this, hunter, camera);
            if (_visitor == null) _setup.terror.Log("Loop corridor: the hunter has no body to copy, so the puzzle runs without the Visitor", this);
        }
    }

    /// <summary>
    /// Collects the anomalies this floor can show. A piece the theme or the kit did not provide leaves its anomaly out (logged); with fewer than
    /// the minimum the puzzle falls back to C4's fixed passes.
    /// </summary>
    private void BuildAnomalies(Stalker figureTemplate)
    {
        _pool.Clear();
        // Each leg's rear lamp: round the corner from the landing point of a lap walked that way, so never seen to change.
        _pool.Add(new LampTintAnomaly(_setup.mirrorLamps[0], _setup.lamps[0], _setup.puzzle.redLamp));
        _pool.Add(new LampDeadAnomaly(_setup.mirrorLamps[0], _setup.lamps[0]));
        AddObjectAnomaly(_setup.blood, _setup.bloodReverse, "blood on the floor");
        AddObjectAnomaly(_setup.locker, _setup.lockerReverse, "a locker that was not there");
        AddObjectAnomaly(_setup.handprint, _setup.handprintReverse, "a handprint on the wall");
        _pool.Add(new FlickerAnomaly(_lamps, _setup.tuning.flickerGap));

        if (figureTemplate != null) _pool.Add(new FigureAnomaly(this, figureTemplate));
        else _setup.terror.Log("Loop corridor: the InteractableKit has no Stalker template, so no robot stands at the corner", this);

        AudioClip[] steps = _controller != null ? _controller.FootstepAudioClips : null;
        if (steps != null && steps.Length > 0) _pool.Add(new FootstepsAnomaly(this, steps));
        else _setup.terror.Log("Loop corridor: the player has no footstep clips, so nothing walks behind them", this);

        _scripted = _pool.Count < _setup.puzzle.minAnomalies;
        if (_scripted) _setup.terror.Log($"Loop corridor: only {_pool.Count} anomalies are available (need {_setup.puzzle.minAnomalies}), so the old fixed passes run", this);
    }

    private void AddObjectAnomaly(GameObject forward, GameObject reverse, string anomalyName)
    {
        if (forward != null && reverse != null) _pool.Add(new ShowObjectAnomaly(forward, reverse, anomalyName));
        else _setup.terror.Log($"Loop corridor: no {anomalyName} - the theme did not provide it", this);
    }

    private void OnEnable()
    {
        GameManager.AllStarsCollected += HandleAllStarsCollected;
    }

    private void OnDisable()
    {
        GameManager.AllStarsCollected -= HandleAllStarsCollected;
        if (_inside) SetInside(false);
    }

    private void OnDestroy()
    {
        if (s_current == this) s_current = null;
        if (_visitor != null) _visitor.Release();
    }

    private void HandleAllStarsCollected()
    {
        if (!IsSealed) _sealPending = true;
    }

    private void Update()
    {
        UpdateBuzz();
        if (_player == null || !GameFlow.IsRunActive || GameOutcome.IsOver || (_outcome != null && _outcome.IsEnding)) return;

        bool inside = _setup.footprint.Contains(new Vector3(_player.position.x, _setup.footprint.center.y, _player.position.z));
        if (inside != _inside) SetInside(inside);

        if (_inside)
        {
            TickCrossing();
            if (!enabled) return; // the crossing may have let the player out
            if (_active != null) _active.Tick();
            TickVisitor();
        }

        if (_sealPending && !_inside && !IsSealed) TrySeal();
    }

    /// <summary>The corridor's lamps hum with their light: a dead or blinking lamp falls silent with it.</summary>
    private void UpdateBuzz()
    {
        for (int i = 0; i < _buzz.Length; i++)
        {
            if (_buzz[i] != null && _lamps[i] != null) _buzz[i].volume = _setup.tuning.buzzVolume * _lamps[i].CurrentIntensity01;
        }
    }

    private AudioSource AddBuzz(WallLamp lamp)
    {
        AudioSource source = lamp.gameObject.AddComponent<AudioSource>();
        source.playOnAwake = false; // MazeGenerator.TakeOverSceneAmbience silences every looping play-on-awake source it finds
        source.clip = AsylumAmbience.BuzzClip;
        source.loop = true;
        source.spatialBlend = 1f;
        source.minDistance = BuzzMinDistance;
        source.maxDistance = _setup.tuning.buzzMaxDistance;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.volume = 0f;
        source.Play();
        return source;
    }

    private void SetInside(bool inside)
    {
        _inside = inside;
        _previousTrigger = -1f;
        _previousReturn = -1f;
        float gain = inside ? _setup.tuning.duckGain : 1f;
        if (_audio != null) _audio.Duck(gain, _setup.tuning.duckSeconds);
        if (_ambience != null) _ambience.SetDuck(gain, _setup.tuning.duckSeconds);

        if (inside) _setup.terror.Log($"Loop corridor at {_setup.entranceCell}, lap {_lap}", this);
        else if (_active != null) _active.Revert();
        if (inside && !_trapped && !_scripted) ShowOffer();
    }

    /// <summary>
    /// The loop is a side path: the first time the player steps in, before the corner commits them, they are told it is optional, that the
    /// corner closes the way, and what solving it gives. Walking back out before the corner costs nothing.
    /// </summary>
    private void ShowOffer()
    {
        if (_offerShown || _hud == null) return;

        _offerShown = true;
        _hud.ShowSubtitle(L10n.T("sub.loop.offer"), _setup.puzzle.ruleSeconds);
        _hud.QueueSubtitle(L10n.T("sub.loop.offer_reward", ("n", _setup.puzzle.targetCount)), _setup.puzzle.ruleSeconds);
    }

    // ---------------------------------------------------------------- crossings

    /// <summary>
    /// Where the player is on the two legs. Reaching the first leg's corner closes the trap; crossing the trigger plane in the second leg walking
    /// forward, or (once trapped) the return line in the first leg walking back toward the vestibule, ends a lap. Which of the two is "keep
    /// walking" and which is "turn back" depends on the way the lap was being walked.
    /// </summary>
    private void TickCrossing()
    {
        Vector3 onA = _setup.legA.InverseTransformPoint(_player.position);
        Vector3 onB = _setup.legB.InverseTransformPoint(_player.position);
        bool inA = onA.z >= 0f && onA.z < _setup.legLength && Mathf.Abs(onA.x) <= _setup.legHalfWidth;
        bool inB = onB.z >= 0f && onB.z < _setup.legLength && Mathf.Abs(onB.x) <= _setup.legHalfWidth;

        // The path runs along the first leg to the corner's centre and on along the second; past the centre the second leg's measure takes over.
        bool pastCorner = onB.z >= _setup.cornerPathDistance - _setup.legLength && onB.z < _setup.legLength && Mathf.Abs(onB.x) <= _setup.legHalfWidth;
        _pathPosition = pastCorner ? _setup.legLength + onB.z : Mathf.Max(onA.z, 0f);

        if (!_scripted && !_trapped && inA && onA.z >= _setup.trapDistance) Trap();

        bool crossed = inB && _previousTrigger >= 0f && _previousTrigger < _setup.triggerDistance && onB.z >= _setup.triggerDistance;
        _previousTrigger = inB && !crossed ? onB.z : -1f;

        bool returned = _trapped && inA && _previousReturn > _setup.returnDistance && onA.z <= _setup.returnDistance;
        _previousReturn = inA && !returned ? onA.z : -1f;

        if (crossed) Cross();
        else if (returned) Decide(true);
    }

    private void Cross()
    {
        if (_scripted)
        {
            CrossScripted();
            return;
        }

        if (!_trapped) Trap();
        Decide(false);
    }

    /// <summary>C4's fixed passes, for a floor that cannot show enough anomalies: each crossing carries the player on, the fourth lets them out.</summary>
    private void CrossScripted()
    {
        if (_lap >= ScriptedLaps)
        {
            LeaveToMaze();
            _sealPending = true;
            _setup.terror.Log($"Loop corridor at {_setup.entranceCell}, left after lap {_lap}", this);
            return;
        }

        NextLap(false);
    }

    /// <summary>The first time the player reaches the corner the opening behind them seals, the rule is shown and the Visitor starts its approach.</summary>
    private void Trap()
    {
        _trapped = true;
        _setup.sealWall.SetActive(true);
        IsSealed = true;
        _sealPending = false;
        if (_setup.counters != null)
        {
            foreach (TMP_Text counter in _setup.counters) if (counter != null) counter.gameObject.SetActive(true);
            ShowCount();
        }

        GameAudio.PlayAt(GameAudio.Library.farSlam, _setup.sealPoint, _setup.puzzle.thudVolume);
        if (_hud != null)
        {
            _hud.ShowSubtitle(L10n.T("sub.loop.rule"), _setup.puzzle.ruleSeconds);
            _hud.QueueSubtitle(L10n.T("sub.loop.reference"), _setup.puzzle.ruleSeconds); // this lap is always normal: the one to remember
        }
        if (_visitor != null) _visitor.Begin();
        _setup.terror.Log($"Loop corridor at {_setup.entranceCell}: the trap closed on lap {_lap}", this);
    }

    /// <summary>
    /// One decision, `backward` being the way the lap was ended (the return line, walking back) - turning back is ending it the other way from
    /// the way it was walked. On a lap with an anomaly turning back is right, on a normal lap keeping walking is. Right adds one to the count and
    /// may free the player; wrong resets the count and brings the Visitor closer.
    /// </summary>
    private void Decide(bool backward)
    {
        bool turnedBack = backward != _reverse;
        bool anomalous = _active != null;
        bool correct = turnedBack == anomalous;
        _count = correct ? _count + 1 : 0;
        _setup.terror.Log($"Loop corridor lap {_lap}: {(turnedBack ? "turned back" : "kept walking")} with {(anomalous ? _active.Name : "nothing wrong")} - {(correct ? "right" : "wrong")}, count {_count}/{_setup.puzzle.targetCount}", this);

        ShowCount();
        ShowVerdict(correct, turnedBack);
        if (correct) GameAudio.PlayAt(GameAudio.Library.shardChime, _player.position, _setup.puzzle.chimeVolume);
        else GameAudio.PlayAt(GameAudio.Library.farSlam, _player.position, _setup.puzzle.thudVolume);
        if (_visitor != null) _visitor.Decided(correct);

        if (_count >= _setup.puzzle.targetCount)
        {
            Free();
            return;
        }

        NextLap(backward);
    }

    /// <summary>
    /// The count is reached: the Visitor stops, the player gets the whole floor map and the supplies (<see cref="GrantSupplies"/>) and is let out
    /// into the maze. The opening stays walled.
    /// </summary>
    private void Free()
    {
        _setup.terror.Log($"Loop corridor at {_setup.entranceCell}: freed on lap {_lap}, the floor map is unlocked", this);
        if (_map != null)
        {
            _map.Unlock();
            _map.RevealAll(); // F110: the reward is the whole layout, no plan needed
        }

        string supplies = GrantSupplies();
        if (_hud != null)
        {
            _hud.ShowSubtitle(L10n.T("sub.loop.reward"), 3.5f);
            if (supplies.Length > 0) _hud.QueueSubtitle(L10n.T("hud.descent.found", ("item", supplies)), 4f);
        }
        LeaveToMaze();
        Retire();
    }

    /// <summary>
    /// The rest of the reward: a full torch and each of the tuned items the player has room for (a full hand skips that one, so a Retry cannot
    /// stack them past their caps). Returns the names of what was given, for the subtitle.
    /// </summary>
    private string GrantSupplies()
    {
        List<string> given = new List<string>();
        if (_setup.puzzle.rewardRefillsTorch && _flashlight != null)
        {
            _flashlight.Refill();
            given.Add(L10n.T("sub.loop.torch_full"));
        }

        if (_setup.puzzle.rewardItems == null) return string.Join(Separator, given);

        foreach (ShopItem item in _setup.puzzle.rewardItems)
        {
            switch (item)
            {
                case ShopItem.SpareBattery:
                case ShopItem.StarCompass:
                case ShopItem.SecondWind: // straight to the inventory: DescentLoot's Second Wind spacing is a DESCENT rule
                    if (!PlayerInventory.CanHoldMore(item)) continue;
                    PlayerInventory.Grant(item);
                    break;
                case ShopItem.FloorPlan:
                    continue; // the whole map is already revealed
                default:
                    if (!DescentLoot.Fits(item, _throws, _flashlight, DescentRun.Depth)) continue;
                    DescentLoot.Grant(item, _throws, _flashlight, DescentRun.Depth);
                    break;
            }

            given.Add(ShopCatalogue.Get(item)?.Name ?? item.ToString());
        }

        _setup.terror.Log($"Loop corridor reward: {string.Join(", ", given)}", this);
        return string.Join(Separator, given);
    }

    // ---------------------------------------------------------------- laps

    /// <summary>
    /// The cut that starts the next lap: the old anomaly goes, the new one (or none) is rolled, and the player is moved to the same place in the
    /// other leg - the first when the lap ended walking forward, the second when it ended walking back. `backward` becomes the way the new lap
    /// is walked, and its anomaly goes in the leg round the corner ahead.
    /// </summary>
    private void NextLap(bool backward)
    {
        if (_active != null) _active.Revert();
        _lap++;
        _active = _scripted ? ScriptedAnomaly() : RollAnomaly();
        _lastLap = _active;
        _reverse = backward;

        if (backward) ReverseCut();
        else LapCut();

        if (_active != null) _active.Apply(_reverse);
        _previousTrigger = -1f;
        _previousReturn = -1f;
        string distance = _visitor != null && _trapped ? $", the Visitor {_visitor.Distance:0.0} m behind" : "";
        _setup.terror.Log($"Loop corridor lap {_lap} ({(_reverse ? "walked back" : "walked forward")}): {(_active != null ? _active.Name : "nothing wrong")}{distance}", this);
    }

    /// <summary>With the anomaly chance, one from the pool that is not the one the lap before had.</summary>
    private LoopAnomaly RollAnomaly()
    {
        if (_pool.Count == 0 || Random.value >= _setup.puzzle.anomalyChance) return null;

        int last = _lastLap != null && _pool.Count > 1 ? _pool.IndexOf(_lastLap) : -1;
        int index = Random.Range(0, last >= 0 ? _pool.Count - 1 : _pool.Count);
        if (last >= 0 && index >= last) index++;
        return _pool[index];
    }

    private LoopAnomaly ScriptedAnomaly()
    {
        return _pool.Count > 0 ? _pool[(_lap - 2) % _pool.Count] : null;
    }

    /// <summary>The seamless cut, walking forward: the same place in the first leg, turned by the corner's angle.</summary>
    private void LapCut()
    {
        Vector3 local = _setup.legB.InverseTransformPoint(_player.position);
        float turn = _setup.legA.eulerAngles.y - _setup.legB.eulerAngles.y;
        MovePlayer(_setup.legA.TransformPoint(local), turn);
    }

    /// <summary>The same cut walking back: the same place in the second leg, turned the other way. The sealed vestibule ahead reads as the corner.</summary>
    private void ReverseCut()
    {
        Vector3 local = _setup.legA.InverseTransformPoint(_player.position);
        float turn = _setup.legB.eulerAngles.y - _setup.legA.eulerAngles.y;
        MovePlayer(_setup.legB.TransformPoint(local), turn);
    }


    /// <summary>Puts the player in the maze's entrance cell, facing into the maze.</summary>
    private void LeaveToMaze()
    {
        float cameraYaw = _camera != null ? _camera.eulerAngles.y : _player.eulerAngles.y;
        MovePlayer(_setup.exitPosition, Mathf.DeltaAngle(cameraYaw, _setup.exitYaw));
    }

    /// <summary>Moves the player and turns their view by `turn` degrees about the vertical, pitch kept. The CharacterController is off for the move, as in Locker.</summary>
    private void MovePlayer(Vector3 position, float turn)
    {
        if (_body != null) _body.enabled = false;
        _player.position = position;
        _player.rotation = Quaternion.Euler(0f, _player.eulerAngles.y + turn, 0f);
        if (_body != null) _body.enabled = true;

        if (_controller != null) _controller.AddYaw(turn);
    }

    /// <summary>The corridor is done with the player: nothing of it is left on, and it stops ticking (OnDisable gives the maze its sound back).</summary>
    private void Retire()
    {
        if (_active != null) _active.Revert();
        _active = null;
        if (_visitor != null) _visitor.Hide();
        foreach (AudioSource source in _buzz) source.Stop();
        enabled = false;
    }

    // ---------------------------------------------------------------- the hanging sign

    private static readonly Color RightColour = new Color(0.05f, 0.42f, 0.12f);
    private static readonly Color WrongColour = new Color(0.62f, 0.04f, 0.02f);

    /// <summary>
    /// Every decision is answered on the light-box signs hanging over both legs (the player faces one after every cut): CORRECT in green,
    /// or why it was wrong (NOTHING WAS DIFFERENT / YOU MISSED SOMETHING) in red; the count under it says the rest. Changed at the cut.
    /// </summary>
    private void ShowVerdict(bool correct, bool turnedBack)
    {
        if (_setup.signVerdicts == null) return;
        string verdict = correct ? L10n.T("world.loop.correct") : L10n.T(turnedBack ? "world.loop.wrong_turned" : "world.loop.wrong_missed");
        foreach (TMP_Text sign in _setup.signVerdicts)
        {
            if (sign == null) continue;
            sign.text = verdict;
            sign.color = correct ? RightColour : WrongColour;
        }
    }

    private void ShowCount()
    {
        string count = $"{_count} / {_setup.puzzle.targetCount}";
        if (_setup.counters != null) foreach (TMP_Text counter in _setup.counters) if (counter != null) counter.text = count;
        if (_setup.signCounts != null) foreach (TMP_Text sign in _setup.signCounts) if (sign != null) sign.text = count;
    }

    // ---------------------------------------------------------------- the Visitor

    private void TickVisitor()
    {
        if (_visitor == null || !_trapped) return;

        _visitor.Tick(_pathPosition, Time.deltaTime);
        if (_visitor.HasCaught) HandleCaught();
    }

    /// <summary>
    /// The Visitor has the player: a real death through GameOutcome, the camera easing onto its head. With a Second Wind the player is let go
    /// instead, under a blackout, and thrown out into the maze without the map.
    /// </summary>
    private void HandleCaught()
    {
        _setup.terror.Log($"Loop corridor lap {_lap}: the Visitor has the player (count {_count})", this);
        Transform view = _camera != null ? _camera : _player;
        Vector3 head = _visitor.PlaceBehind(_player, view);
        _outcome.CaughtByLoopVisitor(head, ThrowOut);
    }

    /// <summary>GameOutcome's Second Wind callback, run at full black.</summary>
    private void ThrowOut()
    {
        _setup.terror.Log("Loop corridor: a Second Wind let the player go, thrown out of the corridor with no reward", this);
        LeaveToMaze();
        Retire();
    }

    // ---------------------------------------------------------------- the opening

    /// <summary>The path point `distance` metres along the whole corridor: the first leg to the centre of the corner, then the second leg, on its axis.</summary>
    internal Vector3 PointOnPath(float distance)
    {
        return distance <= _setup.cornerPathDistance
            ? _setup.legA.TransformPoint(0f, 0f, distance)
            : _setup.legB.TransformPoint(0f, 0f, distance - _setup.legLength);
    }

    /// <summary>Walls the opening up once the player is neither inside nor looking at it from close by, so a wall never appears in front of their eyes.</summary>
    private void TrySeal()
    {
        if (_camera != null)
        {
            Vector3 toOpening = _setup.sealPoint - _camera.position;
            bool watching = toOpening.magnitude < _setup.tuning.sealHideDistance && Vector3.Dot(_camera.forward, toOpening.normalized) > SealLookDot;
            if (watching) return;
        }

        _setup.sealWall.SetActive(true);
        IsSealed = true;
        _sealPending = false;
        _setup.terror.Log($"Loop corridor at {_setup.entranceCell} sealed", this);
        Retire();
    }
}
