using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// S2: the SETTINGS screen - four tabs (DISPLAY, GRAPHICS, AUDIO, CONTROLS) of slider / stepper rows over <see cref="GameSettings"/>, applied the
/// moment a control changes. One shared instance (added to the MazeGenerator object by SetUpAtmosphere, built lazily on first Open like the pause
/// and shop panels), opened by <see cref="Show"/> from the title screen and the pause menu and laid over whichever of them opened it, so closing it
/// just uncovers that menu again. Esc / gamepad B closes only this panel: <see cref="BlocksPause"/> keeps PauseMenu from also reacting to that press.
/// Mouse, touch, keyboard and gamepad all work (rows are Selectables; left / right change a selected stepper or slider; Q / E or LB / RB switch tabs).
/// </summary>
public partial class SettingsMenu : MonoBehaviour
{
    public enum Tab { General, Display, Graphics, Audio, Controls }

    private static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    private static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);
    private static readonly Color Accent = new Color(0.86f, 0.12f, 0.09f);
    private static readonly Color Dim = new Color(0.78f, 0.78f, 0.8f);

    private const float RowTop = -330f, RowStep = 78f;

    /// <summary>The scene's instance (null before SetUpAtmosphere and after the scene is torn down).</summary>
    public static SettingsMenu Active { get; private set; }

    /// <summary>True while the panel is up, and for the rest of the frame it closed in: the Esc that closes it must not also pause or unpause.</summary>
    public static bool BlocksPause => Active != null && (Active._open || Active._closedFrame == Time.frameCount);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = null;

    /// <summary>Opens the shared panel (creating it if the scene somehow has none). Called by the title screen and pause menu buttons.</summary>
    public static void Show()
    {
        if (Active == null)
        {
            GameObject holder = new GameObject("SettingsMenu");
            holder.AddComponent<SettingsMenu>();
        }
        Active.Open();
    }

    private GameObject _panel;
    private RectTransform _rows;
    private readonly List<Button> _tabButtons = new List<Button>();
    private readonly List<Tab> _tabs = new List<Tab>();
    private Tab _tab;
    private bool _open;
    private int _closedFrame = -1;
    private Selectable _first;
    private int _rowIndex;

    /// <summary>True while the panel is showing.</summary>
    public bool IsOpen => _open;
    public Tab CurrentTab => _tab;
    /// <summary>The built row container (test read-back: children are the rows of the current tab).</summary>
    public RectTransform Rows => _rows;
    public GameObject Panel => _panel;

    private bool _stale;

    private void Awake()
    {
        Active = this;
        L10n.Changed += OnLanguageChanged;
    }

    private void OnLanguageChanged() => _stale = true;

    private void OnDestroy()
    {
        L10n.Changed -= OnLanguageChanged;
        if (Active == this) Active = null;
    }

    // ---------------------------------------------------------------- open / close

    public void Open()
    {
        RuntimeUi.EnsureEventSystem();
        if (_panel != null && _stale) DestroyPanel();
        if (_panel == null) BuildPanel();
        _open = true;
        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();
        PlayerLock.SetCursorFree(true);
        ShowTab(_tabs.Contains(_tab) ? _tab : _tabs[0]);
    }

    public void Close()
    {
        if (!_open) return;
        _open = false;
        _closedFrame = Time.frameCount;
        if (_panel != null) _panel.SetActive(false);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void Update()
    {
        if (!_open) return;
        PlayerLock.SetCursorFree(true); // every frame: focus changes re-lock it
        // F119: REMAP CONTROLS sits over this panel and owns Esc / B / Start, the tab keys and the gamepad focus while it is up (and for the frame it ended in).
        if (RebindPanel.BlocksBack) return;
        if (_stale) Rebuild(); // the language changed: same panel, new text (done here, never inside the control that triggered it)

        if (_tab == Tab.Audio) RefreshMicMeter(); // F120: the live microphone level

        // F119: MenuCancel = Esc / pad B / touch Pause; MenuTabPrev / Next = Q, E / LB, RB.
        bool back = GameInput.Pressed(GameAction.MenuCancel);
        int tabStep = 0;
        if (GameInput.Pressed(GameAction.MenuTabPrev)) tabStep = -1;
        if (GameInput.Pressed(GameAction.MenuTabNext)) tabStep = 1;
        if (back) { Close(); return; }
        MenuFocus.Keep(_panel, _first);   // F119: a gamepad always has a selected row
        if (tabStep != 0)
        {
            int i = (_tabs.IndexOf(_tab) + tabStep + _tabs.Count) % _tabs.Count;
            ShowTab(_tabs[i]);
        }
    }

    // ---------------------------------------------------------------- panel

    private void BuildPanel()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        _panel = RuntimeUi.CreatePanel(canvas.transform, "SettingsScreen", new Color(0.02f, 0.02f, 0.03f, 1f));

        TextMeshProUGUI title = RuntimeUi.CreateText(_panel.transform, "Title", L10n.T("ui.settings.title"), 76f, Color.white);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -100f), new Vector2(1200f, 100f));

        _tabs.Add(Tab.General);
        if (GameSettings.DisplayAvailable) _tabs.Add(Tab.Display);
        _tabs.Add(Tab.Graphics);
        _tabs.Add(Tab.Audio);
        _tabs.Add(Tab.Controls);

        float tabWidth = 330f, gap = 14f;
        float x0 = -(tabWidth + gap) * (_tabs.Count - 1) * 0.5f;
        for (int i = 0; i < _tabs.Count; i++)
        {
            Tab tab = _tabs[i];
            Button b = RuntimeUi.CreateButton(_panel.transform, TabName(tab), Vector2.zero, new Vector2(tabWidth, 70f), 34f, ButtonIdle, ButtonHover, Color.white);
            RuntimeUi.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(x0 + i * (tabWidth + gap), -215f), new Vector2(tabWidth, 70f));
            b.onClick.AddListener(() => ShowTab(tab));
            _tabButtons.Add(b);
        }

        GameObject rows = new GameObject("Rows");
        rows.transform.SetParent(_panel.transform, false);
        rows.layer = _panel.layer;
        _rows = rows.AddComponent<RectTransform>();
        RuntimeUi.Stretch(_rows);

        Button reset = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.settings.reset"), new Vector2(-240f, 70f), new Vector2(440f, 76f), 32f, ButtonIdle, ButtonHover, Color.white);
        reset.onClick.AddListener(ResetCurrentTab);
        Button back = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.settings.back"), new Vector2(240f, 70f), new Vector2(440f, 76f), 36f, ButtonIdle, ButtonHover, Color.white);
        back.onClick.AddListener(Close);

        _panel.SetActive(false);
    }

    private void DestroyPanel()
    {
        if (_panel != null) Destroy(_panel);
        _panel = null;
        _rows = null;
        _tabs.Clear();
        _tabButtons.Clear();
        _stale = false;
    }

    private void Rebuild()
    {
        DestroyPanel();
        BuildPanel();
        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();
        ShowTab(_tabs.Contains(_tab) ? _tab : _tabs[0]);
    }

    private static string S(string suffix) => L10n.T("ui.settings." + suffix);
    private static string[] OffOn() => new[] { S("opt.off"), S("opt.on") };

    private static string TabName(Tab tab) => L10n.T("ui.settings.tab." + tab.ToString().ToLowerInvariant());

    private void ResetCurrentTab()
    {
        switch (_tab)
        {
            case Tab.Display: GameSettings.ResetDisplay(); break;
            case Tab.Graphics: GameSettings.ResetGraphics(); break;
            case Tab.Audio: GameSettings.ResetAudio(); break;
            case Tab.General: break; // the language is a machine choice, not a setting to reset
            default: GameSettings.ResetControls(); break;
        }
        ShowTab(_tab);
    }

    public void ShowTab(Tab tab)
    {
        _tab = tab;
        for (int i = 0; i < _tabButtons.Count; i++)
        {
            ColorBlock c = _tabButtons[i].colors;
            c.normalColor = _tabs[i] == tab ? Accent * 0.75f : ButtonIdle;
            c.selectedColor = c.normalColor;
            _tabButtons[i].colors = c;
        }

        for (int i = _rows.childCount - 1; i >= 0; i--)
        {
            Transform child = _rows.GetChild(i);
            child.gameObject.SetActive(false);
            Destroy(child.gameObject);
        }
        _first = null;
        ClearMicRefs(); // F120
        _rowIndex = 0;

        switch (tab)
        {
            case Tab.Display: BuildDisplay(); break;
            case Tab.Graphics: BuildGraphics(); break;
            case Tab.Audio: BuildAudio(); break;
            case Tab.General: BuildGeneral(); break;
            default: BuildControls(); break;
        }

        if (EventSystem.current != null && _first != null) EventSystem.current.SetSelectedGameObject(_first.gameObject);
    }

    // ---------------------------------------------------------------- tabs

    private void BuildGeneral()
    {
        int current = Mathf.Max(0, Array.IndexOf(L10n.Codes, L10n.Code));
        AddStepper(S("language"), L10n.NativeNames, current, true, i => L10n.SetLanguage(L10n.Codes[i]));
        AddStepper(S("reduce_flashing"), OffOn(), GameSettings.ReduceFlashing ? 1 : 0, true, i => GameSettings.ReduceFlashing = i == 1); // F118
    }

    private void BuildDisplay()
    {
        AddStepper(S("window_mode"), new[] { S("opt.fullscreen"), S("opt.exclusive"), S("opt.windowed") }, (int)GameSettings.WindowMode, false,
            i => GameSettings.WindowMode = (GameSettings.WindowModeKind)i);

        List<Vector2Int> list = GameSettings.Resolutions();
        if (list.Count > 0)
        {
            string[] names = new string[list.Count];
            for (int i = 0; i < names.Length; i++) names[i] = $"{list[i].x} x {list[i].y}";
            AddStepper(S("resolution"), names, GameSettings.ResolutionIndex(list), false, i => GameSettings.SetResolution(list[i]));
        }

        AddStepper(S("vsync"), OffOn(), GameSettings.VSync ? 1 : 0, true, i => GameSettings.VSync = i == 1);

        int cap = Array.IndexOf(GameSettings.FrameCaps, GameSettings.FrameCap);
        string[] capNames = new string[GameSettings.FrameCaps.Length];
        for (int i = 0; i < capNames.Length; i++) capNames[i] = GameSettings.FrameCaps[i] > 0 ? GameSettings.FrameCaps[i].ToString() : S("opt.unlimited");
        AddStepper(S("frame_cap"), capNames, Mathf.Max(0, cap), false, i => GameSettings.FrameCap = GameSettings.FrameCaps[i]);
    }

    private void BuildGraphics()
    {
        AddStepper(S("quality"), new[] { S("opt.low"), S("opt.medium"), S("opt.high") }, (int)GameSettings.Quality, false, i => GameSettings.Quality = (GameSettings.QualityTier)i);
        AddStepper(S("shadows"), new[] { S("opt.off"), S("opt.low"), S("opt.high") }, (int)GameSettings.Shadows, false, i => GameSettings.Shadows = (GameSettings.ShadowTier)i);
        if (!Application.isMobilePlatform)
        {
            AddStepper(S("ambient_occlusion"), OffOn(), GameSettings.AmbientOcclusion ? 1 : 0, true, i => GameSettings.AmbientOcclusion = i == 1);
        }
        AddStepper(S("fog"), new[] { S("opt.off"), S("opt.low"), S("opt.normal") }, (int)GameSettings.Fog, false, i => GameSettings.Fog = (GameSettings.FogTier)i);
        AddSlider(S("brightness"), GameSettings.MinBrightness, GameSettings.MaxBrightness, GameSettings.Brightness, false,
            v => GameSettings.Brightness = v, v => (v >= 0f ? "+" : "") + v.ToString("0.00", L10n.Culture) + " EV");
        AddSlider(S("fov"), GameSettings.MinFov, GameSettings.MaxFov, GameSettings.FieldOfView, true,
            v => GameSettings.FieldOfView = v, v => Mathf.RoundToInt(v).ToString());
        AddSlider(S("camera_shake"), 0f, 1f, GameSettings.CameraShake, false,
            v => GameSettings.CameraShake = v, v => Mathf.RoundToInt(v * 100f) + "%");
        AddSlider(S("effects_intensity"), 0f, 1f, GameSettings.EffectsIntensity, false,
            v => GameSettings.EffectsIntensity = v, v => Mathf.RoundToInt(v * 100f) + "%");
    }

    private void BuildAudio()
    {
        AddSlider(S("master_volume"), 0f, 1f, GameSettings.MasterVolume, false, v => GameSettings.MasterVolume = v, Percent);
        if (GameSettings.HasMixer) // Music / Effects need the mixer's groups
        {
            AddSlider(S("music_volume"), 0f, 1f, GameSettings.MusicVolume, false, v => GameSettings.MusicVolume = v, Percent);
            AddSlider(S("effects_volume"), 0f, 1f, GameSettings.EffectsVolume, false, v => GameSettings.EffectsVolume = v, Percent);
        }
        BuildMicRows(); // F120: desktop only
    }

    private static string Percent(float v) => Mathf.RoundToInt(v * 100f) + "%";

    // ---------------------------------------------------------------- rows

    private float NextRowY() => RowTop - _rowIndex++ * RowStep;

    private TextMeshProUGUI AddLabel(string text, float y)
    {
        TextMeshProUGUI label = RuntimeUi.CreateText(_rows, text + " Label", text, 34f, Dim);
        label.alignment = TextAlignmentOptions.Left;
        label.enableAutoSizing = true;
        label.fontSizeMax = 34f;
        label.fontSizeMin = 22f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        RuntimeUi.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(-330f, y), new Vector2(600f, 60f));
        return label;
    }

    private void AddStepper(string label, string[] options, int index, bool wrap, Action<int> onChanged)
    {
        float y = NextRowY();
        AddLabel(label, y);
        StepperControl stepper = RuntimeUi.CreateStepper(_rows, label + " Stepper", new Vector2(0.5f, 1f), new Vector2(330f, y), new Vector2(560f, 60f), 32f, ButtonIdle, ButtonHover);
        stepper.Configure(options, index, wrap, onChanged);
        if (_first == null) _first = stepper;
    }

    private void AddSlider(string label, float min, float max, float value, bool whole, Action<float> onChanged, Func<float, string> format)
    {
        float y = NextRowY();
        AddLabel(label, y);
        TextMeshProUGUI readout = RuntimeUi.CreateText(_rows, label + " Value", format(value), 30f, Color.white);
        readout.alignment = TextAlignmentOptions.Right;
        RuntimeUi.Place(readout.rectTransform, new Vector2(0.5f, 1f), new Vector2(500f, y), new Vector2(220f, 60f));
        readout.enableAutoSizing = true;
        readout.fontSizeMax = 30f;
        readout.fontSizeMin = 20f;
        readout.textWrappingMode = TextWrappingModes.NoWrap;
        Slider slider = RuntimeUi.CreateSlider(_rows, label + " Slider", new Vector2(0.5f, 1f), new Vector2(205f, y), new Vector2(380f, 60f),
            min, max, value, null, ButtonHover);
        slider.wholeNumbers = whole;
        slider.SetValueWithoutNotify(value);
        slider.onValueChanged.AddListener(v =>
        {
            readout.text = format(v);
            onChanged(v);
        });
        if (_first == null) _first = slider;
    }
}
