using System;
using UnityEngine;

/// <summary>
/// F120: the hunter hears the player's microphone (opt-in, desktop only - <see cref="GameSettings.MicEnabled"/>, off by default). Sits on the
/// player (added by MazeGenerator.SetUpAtmosphere next to the HabitTracker). Every frame it tells <see cref="MicInput"/> whether a device is
/// wanted (during a run, or while Settings shows the AUDIO tab so the live meter works), polls the loudness, and turns a sustained loud sound
/// into a noise at the player's feet through the existing hearing path: <see cref="AIFollower.HearNoise(Vector3, float, NoiseSource)"/> with
/// <see cref="NoiseSource.Voice"/>, which also reaches every listening entity through <see cref="MazeNoise"/> (the Listener hunts sound). Inside a
/// locker the threshold is lower. Nothing is recorded, saved or sent; the audio is never played back.
/// </summary>
public class PlayerVoice : MonoBehaviour
{
    [Header("F120 - microphone")]
    [Tooltip("The threshold drops by this much while the player hides in a locker (holding still matters)")]
    [SerializeField] private float lockerThresholdDrop = 0.15f;
    [Tooltip("The level must stay above the threshold this long before it counts (filters keyboard clicks)")]
    [SerializeField] private float sustainSeconds = 0.12f;
    [Tooltip("At most one voice noise per this many seconds")]
    [SerializeField] private float cooldownSeconds = 0.6f;
    [Tooltip("Noise radius (m) at the threshold and at full volume")]
    [SerializeField] private float minRadius = 10f;
    [SerializeField] private float maxRadius = 34f;
    [Tooltip("Ignore the microphone for this long after a scripted ending / grab (the game's own sting coming back through the speakers)")]
    [SerializeField] private float afterEndingMuteSeconds = 0.8f;

    /// <summary>Raised for every voice noise (position, radius), for entities that want to hear the player (F121 / F108). Reset at SubsystemRegistration.</summary>
    public static event Action<Vector3, float> VoiceNoise;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => VoiceNoise = null;

    private Transform _player;
    private PlayerStealthState _stealth;
    private AIFollower _hunter;
    private GameOutcome _outcome;
    private TerrorSettings _terror = new TerrorSettings();

    private float _above;
    private float _nextAllowed;
    private float _muteUntil;

    /// <summary>Voice noises the hunter actually heard this floor (HabitTracker teaches the Voice habit from it). An instance field: a reload resets it.</summary>
    public int HeardThisFloor { get; private set; }

    /// <summary>The effective threshold right now (lower in a locker, lower with the learned Voice habit), for the HUD tick and the tests.</summary>
    public float Threshold { get; private set; } = GameSettings.DefaultMicThreshold;

    /// <summary>True while the level is above the threshold.</summary>
    public bool Loud { get; private set; }

    /// <summary>True while the microphone is being read for the game (setting on, a run in progress, a signal): the HUD shows its bar.</summary>
    public bool Active { get; private set; }

    public void Configure(Transform player, PlayerStealthState stealth, AIFollower hunter, GameOutcome outcome, TerrorSettings terror)
    {
        _player = player;
        _stealth = stealth;
        _hunter = hunter;
        _outcome = outcome;
        _terror = terror ?? new TerrorSettings();
    }

    private int Learned() => _terror.learningHunter ? HunterMemory.Level(HunterMemory.Habit.Voice) : 0;

    private void Update()
    {
        bool settingsMeter = SettingsMenu.Active != null && SettingsMenu.Active.IsOpen && SettingsMenu.Active.CurrentTab == SettingsMenu.Tab.Audio;
        bool inRun = GameFlow.IsRunActive && !GameOutcome.IsOver && !GameFlow.IsInSafeRoom;
        bool enabled = GameSettings.MicEnabled && MicInput.Supported;

        // The device stays open through a pause (no re-open hitch on resume) but emits nothing while the clock is stopped.
        MicInput.SetWanted(enabled && (inRun || settingsMeter));
        MicInput.Poll(Time.unscaledDeltaTime);

        bool hidden = _stealth != null && _stealth.Hidden;
        int learned = Learned();
        float threshold = GameSettings.MicThreshold - (hidden ? lockerThresholdDrop + 0.04f * learned : 0f);
        Threshold = Mathf.Max(0.08f, threshold);

        bool listening = MicInput.CurrentStatus == MicInput.Status.Listening || MicInput.DebugLevel >= 0f;
        Active = enabled && inRun && listening;
        Loud = Active && MicInput.Level > Threshold;

        if (_outcome != null && _outcome.IsEnding) _muteUntil = Time.time + afterEndingMuteSeconds;

        bool canEmit = Active && Time.timeScale > 0f && Time.time >= _muteUntil && (_outcome == null || !_outcome.IsEnding);
        if (!canEmit || !Loud)
        {
            _above = 0f;
            return;
        }

        _above += Time.deltaTime;
        if (_above < sustainSeconds || Time.time < _nextAllowed) return;

        _nextAllowed = Time.time + cooldownSeconds;
        float over = Mathf.InverseLerp(Threshold, 1f, MicInput.Level);
        float radius = Mathf.Lerp(minRadius, maxRadius, over) * (1f + 0.25f * learned);
        Vector3 at = _player != null ? _player.position : transform.position;

        bool heard;
        if (_hunter != null)
        {
            heard = _hunter.HearNoise(at, radius, NoiseSource.Voice);
        }
        else
        {
            MazeNoise.Emit(at, radius); // no hunter on this floor: the Listener still hears
            heard = false;
        }

        if (heard) HeardThisFloor++;
        VoiceNoise?.Invoke(at, radius);
        _terror.Log($"[voice] level {MicInput.Level:0.00} over threshold {Threshold:0.00}: noise radius {radius:0.0} m, hunter heard = {heard} ({HeardThisFloor} this floor)", this);
    }

    private void OnDestroy()
    {
        // A scene reload closes the device; the next scene reopens it when its run starts.
        MicInput.Shutdown();
    }
}
