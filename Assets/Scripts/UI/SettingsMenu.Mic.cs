using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F120: the microphone rows of the AUDIO tab (desktop only - <see cref="GameSettings.MicAvailable"/>): an ON/OFF stepper, a device picker, the
/// threshold slider, a live level meter (a track, a fill that turns red above the threshold, a tick that follows the slider and the status text) and
/// the privacy note. PlayerVoice keeps the device open while this tab is showing, so the meter works on the title screen too.
/// </summary>
public partial class SettingsMenu
{
    private Image _micFill;
    private RectTransform _micTick;
    private TextMeshProUGUI _micStatus;
    private float _micMeterShown;
    private float _micMeterVelocity;

    private static readonly Color MeterTrack = new Color(0f, 0f, 0f, 0.6f);
    private static readonly Color MeterQuiet = new Color(0.85f, 0.85f, 0.8f);
    private static readonly Color MeterLoud = new Color(0.9f, 0.15f, 0.1f);

    private void BuildMicRows()
    {
        if (!GameSettings.MicAvailable) return;

        AddStepper(S("mic"), OffOn(), GameSettings.MicEnabled ? 1 : 0, true, i => GameSettings.MicEnabled = i == 1);

        string[] devices = MicInput.Devices;
        if (devices.Length > 1)
        {
            int index = Mathf.Max(0, System.Array.IndexOf(devices, GameSettings.MicDevice));
            AddStepper(S("mic_device"), devices, index, true, i =>
            {
                GameSettings.MicDevice = devices[i];
                MicInput.Restart();
            });
        }
        else
        {
            AddStaticValue(S("mic_device"), devices.Length == 1 ? devices[0] : S("mic_none"));
        }

        AddSlider(S("mic_threshold"), GameSettings.MinMicThreshold, GameSettings.MaxMicThreshold, GameSettings.MicThreshold, false,
            v =>
            {
                GameSettings.MicThreshold = v;
                PlaceMicTick();
            }, Percent);

        AddMeter(S("mic_level"));
        AddNote(S("mic_privacy"));
    }

    private void ClearMicRefs()
    {
        _micFill = null;
        _micTick = null;
        _micStatus = null;
    }

    /// <summary>A label with a plain value text instead of a control (a single device, or none).</summary>
    private void AddStaticValue(string label, string value)
    {
        float y = NextRowY();
        AddLabel(label, y);
        TextMeshProUGUI text = RuntimeUi.CreateText(_rows, label + " Value", value, 28f, Color.white);
        text.alignment = TextAlignmentOptions.Left;
        text.enableAutoSizing = true;
        text.fontSizeMax = 28f;
        text.fontSizeMin = 16f;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        RuntimeUi.Place(text.rectTransform, new Vector2(0.5f, 1f), new Vector2(205f, y), new Vector2(560f, 60f));
    }

    private void AddMeter(string label)
    {
        float y = NextRowY();
        AddLabel(label, y);

        GameObject track = new GameObject(label + " Meter");
        track.transform.SetParent(_rows, false);
        track.layer = _rows.gameObject.layer;
        Image trackImage = track.AddComponent<Image>();
        trackImage.color = MeterTrack;
        trackImage.raycastTarget = false;
        RuntimeUi.Place(trackImage.rectTransform, new Vector2(0.5f, 1f), new Vector2(205f, y), new Vector2(380f, 18f));

        GameObject fill = new GameObject("Fill");
        fill.transform.SetParent(track.transform, false);
        fill.layer = track.layer;
        _micFill = fill.AddComponent<Image>();
        _micFill.color = MeterQuiet;
        _micFill.raycastTarget = false;
        SetMeterFill(0f);

        GameObject tick = new GameObject("Threshold");
        tick.transform.SetParent(track.transform, false);
        tick.layer = track.layer;
        Image tickImage = tick.AddComponent<Image>();
        tickImage.color = Color.white;
        tickImage.raycastTarget = false;
        _micTick = tickImage.rectTransform;
        PlaceMicTick();

        _micStatus = RuntimeUi.CreateText(_rows, label + " Status", "", 22f, Dim);
        _micStatus.alignment = TextAlignmentOptions.Left;
        _micStatus.enableAutoSizing = true;
        _micStatus.fontSizeMax = 22f;
        _micStatus.fontSizeMin = 14f;
        _micStatus.textWrappingMode = TextWrappingModes.Normal;
        RuntimeUi.Place(_micStatus.rectTransform, new Vector2(0.5f, 1f), new Vector2(560f, y), new Vector2(260f, 60f));
        _micStatus.text = StatusText();
    }

    private void AddNote(string text)
    {
        float y = NextRowY();
        TextMeshProUGUI note = RuntimeUi.CreateText(_rows, "Mic Privacy Note", text, 24f, new Color(0.6f, 0.6f, 0.64f));
        note.alignment = TextAlignmentOptions.Center;
        note.textWrappingMode = TextWrappingModes.Normal;
        note.enableAutoSizing = true;
        note.fontSizeMax = 24f;
        note.fontSizeMin = 16f;
        RuntimeUi.Place(note.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1400f, 70f));
    }

    private void PlaceMicTick()
    {
        if (_micTick == null) return;
        float t = Mathf.InverseLerp(0f, 1f, GameSettings.MicThreshold);
        _micTick.anchorMin = new Vector2(t, 0f);
        _micTick.anchorMax = new Vector2(t, 1f);
        _micTick.pivot = new Vector2(0.5f, 0.5f);
        _micTick.sizeDelta = new Vector2(3f, 8f);
        _micTick.anchoredPosition = Vector2.zero;
    }

    private void SetMeterFill(float amount)
    {
        if (_micFill == null) return;
        RectTransform rect = _micFill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static string StatusText()
    {
        MicInput.Status status = GameSettings.MicEnabled ? MicInput.CurrentStatus : MicInput.Status.Off;
        switch (status)
        {
            case MicInput.Status.Listening: return S("mic_status.listening");
            case MicInput.Status.NoSignal: return S("mic_status.no_signal");
            case MicInput.Status.NoDevice:
            case MicInput.Status.Unsupported: return S("mic_status.no_device");
            default: return S("mic_status.off");
        }
    }

    /// <summary>Called from Update while the AUDIO tab is showing: the live level, red above the threshold, and the status text.</summary>
    private void RefreshMicMeter()
    {
        if (_micFill == null) return;

        bool on = GameSettings.MicEnabled && (MicInput.CurrentStatus == MicInput.Status.Listening || MicInput.CurrentStatus == MicInput.Status.NoSignal);
        float level = on ? MicInput.Level : 0f;
        _micMeterShown = Mathf.SmoothDamp(_micMeterShown, level, ref _micMeterVelocity, 0.04f, float.MaxValue, Time.unscaledDeltaTime);
        SetMeterFill(_micMeterShown);
        _micFill.color = level > GameSettings.MicThreshold ? MeterLoud : MeterQuiet;
        if (_micStatus != null)
        {
            string text = StatusText();
            if (_micStatus.text != text) _micStatus.text = text;
        }
    }
}
