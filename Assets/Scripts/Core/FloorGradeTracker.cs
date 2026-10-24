using UnityEngine;

/// <summary>
/// F122 D8: counts what this attempt at a floor looked like (chases, Stan's touches, spare batteries spent, Second Winds consumed, the free grab,
/// shards found of those placed) and turns it into the floor's <see cref="Grade"/> when the floor is cleared. An instance component added by
/// MazeGenerator.SetUpAtmosphere like HabitTracker - a scene reload (Retry, NEXT FLOOR) recreates it, which is exactly "per attempt". Counts only while a
/// run is active, so the Intake, the hub and the shop never register. The pure arithmetic lives in <see cref="FloorGrade"/>.
/// </summary>
public class FloorGradeTracker : MonoBehaviour
{
    private AIFollower _hunter;
    private ConsumableController _items;
    private HunterGrab _grab;
    private MazeGenerator _maze;
    private bool _subscribed;

    private int _chases, _stanTouches, _batteries, _secondWinds, _shardsPlaced;

    public int Chases => _chases;
    public int StanTouches => _stanTouches;
    public int BatteriesUsed => _batteries;
    public int SecondWinds => _secondWinds;
    public int ShardsPlaced => _shardsPlaced;

    /// <summary>Called once by SetUpAtmosphere, after the maze (its shards) is built.</summary>
    public void Configure(MazeGenerator maze, AIFollower hunter, ConsumableController items, HunterGrab grab)
    {
        Unsubscribe();
        _maze = maze;
        _hunter = hunter;
        _items = items;
        _grab = grab;
        _chases = _stanTouches = _batteries = _secondWinds = 0;
        _shardsPlaced = FindObjectsByType<ShardPickup>(FindObjectsInactive.Exclude).Length;

        if (_hunter != null) _hunter.ChaseStateChanged += HandleChase;
        if (_items != null) _items.ItemUsed += HandleItem;
        Stalker.PlayerTouched += HandleStalkerTouch;
        _subscribed = true;
    }

    private void OnDestroy() => Unsubscribe();

    private void Unsubscribe()
    {
        if (!_subscribed) return;
        _subscribed = false;
        if (_hunter != null) _hunter.ChaseStateChanged -= HandleChase;
        if (_items != null) _items.ItemUsed -= HandleItem;
        Stalker.PlayerTouched -= HandleStalkerTouch;
    }

    private static bool Counting => GameFlow.IsRunActive && !GameOutcome.IsOver;

    private void HandleChase(bool chasing)
    {
        if (chasing && Counting) _chases++;
    }

    private void HandleItem(ShopItem item)
    {
        if (item == ShopItem.SpareBattery && Counting) _batteries++;
    }

    private void HandleStalkerTouch()
    {
        if (Counting) _stanTouches++;
    }

    /// <summary>GameOutcome calls this when a held Second Wind is consumed.</summary>
    public void NoteSecondWind()
    {
        if (Counting) _secondWinds++;
    }

    /// <summary>The grade for the floor being cleared now. <paramref name="countdownSeconds"/> is the escape countdown it started from.</summary>
    public Grade Compute(float runSeconds, float secondsLeft, float countdownSeconds)
    {
        return FloorGrade.For(Snapshot(runSeconds, secondsLeft, countdownSeconds));
    }

    public GradeInput Snapshot(float runSeconds, float secondsLeft, float countdownSeconds)
    {
        int floor = GameFlow.CurrentFloor;
        return new GradeInput
        {
            floor = floor,
            runSeconds = runSeconds,
            parSeconds = FloorGrade.ParSeconds(_maze != null ? _maze.Profile : null),
            secondsLeft = secondsLeft,
            countdownSeconds = countdownSeconds,
            chases = _chases,
            secondWinds = _secondWinds,
            grabs = _grab != null && _grab.Used ? 1 : 0,
            stanTouches = _stanTouches,
            batteriesUsed = _batteries,
            shardsFound = PlayerWallet.ShardsFoundThisAttempt,
            shardsPlaced = _shardsPlaced,
            deathsOnFloor = FloorGrade.DeathsOn(floor)
        };
    }
}
