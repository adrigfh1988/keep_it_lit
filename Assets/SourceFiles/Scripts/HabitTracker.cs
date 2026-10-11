using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// F105 C6: counts what the player leans on during a floor - hiding in lockers, throwing, walking in the dark, sprinting - and
/// at floor clear (<see cref="CommitLearning"/>, called by GameOutcome.FloorCleared) teaches the hunter every habit over its
/// threshold through <see cref="HunterMemory"/>. A death or a retry commits nothing. It also announces the lessons learned on
/// the previous floor, a few seconds into this floor's run (once the floor intro banner has gone), on a red-barred objective card;
/// the pause menu lists everything it has learned so far (PauseMenu's lessons panel).
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
    [Tooltip("Seconds each learned-lesson card stays fully on screen (run time: a pause holds it)")]
    [SerializeField] private float announceSeconds = 5f;

    /// <summary>The lesson card's bar: red, a threat, not the amber of a goal.</summary>
    private static readonly Color LessonAccent = new Color(0.85f, 0.12f, 0.08f);

    private const float CardTop = 190f;

    private ObjectiveCard _card;

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
        if (_card != null) Destroy(_card.gameObject);
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

    /// <summary>
    /// Shows one card per lesson learned on an earlier floor, in order, then never again this floor. A card is the objective card
    /// with a red bar and the IT HAS LEARNED label: the lesson as the goal, the level bar as the counter, what it does now as the
    /// second line. The hold runs on scaled time, so a pause freezes the card rather than letting it fade out behind the menu.
    /// </summary>
    private void Announce()
    {
        _announced = true;
        if (!HunterMemory.HasPending) return;

        List<HunterMemory.Habit> lessons = HunterMemory.TakePendingAnnouncements();
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null)
        {
            // No UI at all: fall back to the old world text.
            if (_hud != null) foreach (HunterMemory.Habit habit in lessons) _hud.QueueSubtitle(HunterMemory.LessonText(habit), announceSeconds);
            return;
        }

        if (_card == null)
        {
            _card = ObjectiveCard.Create(canvas.transform, "HunterLessonCard", false, true, "ui.hunter.lesson.label", LessonAccent);
            _card.SetTop(CardTop); // clear of the three-row star / floor / shard counter top-left
        }
        StartCoroutine(ShowLessons(lessons));
    }

    private IEnumerator ShowLessons(List<HunterMemory.Habit> lessons)
    {
        TMP_FontAsset font = TMP_Settings.defaultFontAsset;
        foreach (HunterMemory.Habit habit in lessons)
        {
            if (GameOutcome.IsOver) break;

            _card.Set(HunterMemory.LevelBar(habit, font), HunterMemory.LessonText(habit), null, HunterMemory.EffectText(habit));
            GameAudio.Play2D(GameAudio.Library.whisperEcho, 0.6f);
            yield return _card.Fade(0f, 1f, 0.35f);

            float until = Time.time + announceSeconds;
            while (Time.time < until && !GameOutcome.IsOver) yield return null;

            yield return _card.Fade(1f, 0f, 0.35f);
            yield return new WaitForSeconds(0.3f);
        }
        _card.HideNow();
    }
}
