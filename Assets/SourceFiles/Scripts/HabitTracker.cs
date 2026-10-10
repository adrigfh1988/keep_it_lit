using UnityEngine;

/// <summary>
/// F105 C6: counts what the player leans on during a floor - hiding in lockers, throwing, walking in the dark, sprinting - and
/// at floor clear (<see cref="CommitLearning"/>, called by GameOutcome.FloorCleared) teaches the hunter every habit over its
/// threshold through <see cref="HunterMemory"/>. A death or a retry commits nothing. It also announces the lessons learned on
/// the previous floor as world text, a few seconds into this floor's run (once the floor intro banner has gone).
///
/// An instance component on the player, added by MazeGenerator.SetUpAtmosphere like ContractTracker, so a scene reload
/// recreates it with zeroed counts - exactly "per floor". Counts only while the run is active, so the Intake, the hub and the
/// shop never register; with <see cref="TerrorSettings.learningHunter"/> off it counts and announces nothing.
/// </summary>
public class HabitTracker : MonoBehaviour
{
    [Header("F105 C6 - thresholds (per floor)")]
    [Tooltip("Locker entries on one floor that teach it the lockers")]
    [SerializeField, Min(1)] private int lockerEntries = 5;
    [Tooltip("Throws on one floor that teach it the bottles")]
    [SerializeField, Min(1)] private int throwCount = 4;
    [Tooltip("Share of the floor's run time spent with the torch off and not hidden that teaches it the dark")]
    [SerializeField, Range(0f, 1f)] private float darkShare = 0.4f;
    [Tooltip("...and the dark seconds must also be at least this many")]
    [SerializeField, Min(0f)] private float darkMinSeconds = 90f;
    [Tooltip("Seconds of sprinting on one floor that teach it the run")]
    [SerializeField, Min(1f)] private float sprintSeconds = 120f;

    [Header("F105 C6 - announcement")]
    [Tooltip("Seconds after the run starts before a learned lesson is shown (the floor intro banner takes about 5.5 s)")]
    [SerializeField] private float announceDelay = 6f;
    [Tooltip("Seconds each learned-lesson line stays on screen")]
    [SerializeField] private float announceSeconds = 3f;

    private PlayerStealthState _stealth;
    private Flashlight _flashlight;
    private PlayerStamina _stamina;
    private ThrowController _throws;
    private PlayerHud _hud;
    private TerrorSettings _terror = new TerrorSettings();

    private int _lockerEntries;
    private int _throwsMade;
    private float _runSeconds;
    private float _darkSeconds;
    private float _sprintSeconds;
    private bool _wasHidden;
    private float _announceAt = -1f;
    private bool _announced;

    /// <summary>Called once by MazeGenerator.SetUpAtmosphere, after the player's other components exist. Any argument may be null.</summary>
    public void Configure(PlayerStealthState stealth, Flashlight flashlight, PlayerStamina stamina, ThrowController throws, PlayerHud hud, TerrorSettings terror)
    {
        if (_throws != null) _throws.Thrown -= HandleThrown;

        _stealth = stealth;
        _flashlight = flashlight;
        _stamina = stamina;
        _throws = throws;
        _hud = hud;
        _terror = terror ?? new TerrorSettings();

        if (_throws != null) _throws.Thrown += HandleThrown;
    }

    private void OnDestroy()
    {
        if (_throws != null) _throws.Thrown -= HandleThrown;
    }

    private bool Counting => _terror.learningHunter && GameFlow.IsRunActive && !GameOutcome.IsOver;

    private void HandleThrown()
    {
        if (Counting) _throwsMade++;
    }

    private void Update()
    {
        if (!_terror.learningHunter) return;

        if (GameFlow.IsRunActive && _announceAt < 0f) _announceAt = Time.time + announceDelay;
        if (!_announced && _announceAt >= 0f && Time.time >= _announceAt) Announce();

        if (!Counting) return;

        float dt = Time.deltaTime;
        _runSeconds += dt;

        bool hidden = _stealth != null && _stealth.Hidden;
        if (hidden && !_wasHidden) _lockerEntries++;
        _wasHidden = hidden;

        if (!hidden && _flashlight != null && !_flashlight.IsOn) _darkSeconds += dt;
        if (_stamina != null && _stamina.IsSprinting) _sprintSeconds += dt;
    }

    /// <summary>
    /// Floor cleared: teaches the hunter every habit whose threshold this floor's counts reached. Called once by
    /// GameOutcome.FloorCleared; a death never calls it. Does nothing with the learningHunter flag off.
    /// </summary>
    public void CommitLearning()
    {
        if (!_terror.learningHunter) return;

        TryLearn(HunterMemory.Habit.Lockers, _lockerEntries >= lockerEntries, $"{_lockerEntries} locker entries");
        TryLearn(HunterMemory.Habit.Throws, _throwsMade >= throwCount, $"{_throwsMade} throws");
        bool dark = _darkSeconds >= darkMinSeconds && _runSeconds > 0f && _darkSeconds / _runSeconds >= darkShare;
        TryLearn(HunterMemory.Habit.Dark, dark, $"{_darkSeconds:0} s in the dark of {_runSeconds:0} s");
        TryLearn(HunterMemory.Habit.Sprint, _sprintSeconds >= sprintSeconds, $"{_sprintSeconds:0} s sprinting");
    }

    private void TryLearn(HunterMemory.Habit habit, bool overThreshold, string evidence)
    {
        if (!overThreshold || !HunterMemory.Learn(habit)) return;

        _terror.Log($"Hunter learned {habit} (level {HunterMemory.Level(habit)}): {evidence}", this);
    }

    /// <summary>Shows one world-text line per lesson learned on an earlier floor, in order, then never again this floor.</summary>
    private void Announce()
    {
        _announced = true;
        if (_hud == null || !HunterMemory.HasPending) return;

        foreach (HunterMemory.Habit habit in HunterMemory.TakePendingAnnouncements())
        {
            _hud.QueueSubtitle(L10n.T(AnnouncementKey(habit)), announceSeconds);
        }
    }

    private static string AnnouncementKey(HunterMemory.Habit habit)
    {
        switch (habit)
        {
            case HunterMemory.Habit.Lockers: return "sub.hunter.learned_lockers";
            case HunterMemory.Habit.Throws: return "sub.hunter.learned_throws";
            case HunterMemory.Habit.Dark: return "sub.hunter.learned_dark";
            default: return "sub.hunter.learned_sprint";
        }
    }
}
