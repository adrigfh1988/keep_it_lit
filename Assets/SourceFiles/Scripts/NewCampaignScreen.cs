using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// F122 D14: the NEW ADMISSION screen START GAME opens - a difficulty preset (STORY / STANDARD / NIGHTMARE).
/// STORY enables all assists automatically; NIGHTMARE enables all Oaths + Nightmare modifiers automatically; STANDARD is the baseline game.
/// BEGIN hands the choice to <c>CampaignRules.Begin</c>; the caller (MainMenu) decides whether floor 1 has to be rebuilt.
/// Built lazily and rebuilt on a language change; the gamepad is kept on a button by <see cref="MenuFocus"/>, Esc / B steps back out.
/// <see cref="BlocksBack"/> keeps the title screen from reacting to the same press.
/// Test names: NewCampaignScreen, Preset_Story|Standard|Nightmare, Begin, Back.
/// </summary>
public class NewCampaignScreen : MonoBehaviour
{
    private static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    private static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);
    private static readonly Color Accent = new Color(0.86f, 0.12f, 0.09f);
    private static readonly Color Dim = new Color(0.78f, 0.78f, 0.8f);

    public static NewCampaignScreen Active { get; private set; }

    /// <summary>True while the screen is up.</summary>
    public static bool IsOpen => Active != null && Active._open;

    /// <summary>True while up and for the rest of the frame it closed in: the Esc / B that closes it must not also act on the title screen.</summary>
    public static bool BlocksBack => Active != null && (Active._open || Active._closedFrame == Time.frameCount);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = null;

    /// <summary>Opens the screen. <paramref name="onBegin"/> runs after CampaignRules.Begin; <paramref name="onBack"/> when the player backs out.</summary>
    public static void Show(Action onBegin, Action onBack)
    {
        if (Active == null)
        {
            GameObject holder = new GameObject("NewCampaignScreenHost");
            holder.AddComponent<NewCampaignScreen>();
        }
        Active.Open(onBegin, onBack);
    }

    // ---- the choice
    private DifficultyPreset _preset = DifficultyPreset.Standard;
    private Assist _assists = Assist.None;
    private Oath _oaths = Oath.None;
    private StartingKit _kit = StartingKit.StandardIssue;
    private int _torch;

    public DifficultyPreset Preset => _preset;
    public Assist Assists => _assists;
    public Oath Oaths => _oaths;
    public StartingKit Kit => _kit;
    public int Torch => _torch;

    // ---- the widgets
    private GameObject _panel;
    private GameObject _oathPanel;
    private bool _open;
    private int _closedFrame = -1;
    private bool _stale;
    private Action _onBegin, _onBack;
    private Selectable _first;
    private readonly Button[] _presetButtons = new Button[3];
    private TextMeshProUGUI _presetDesc;

    public GameObject Panel => _panel;
    public GameObject OathPanel => _oathPanel;
    public bool OathPanelOpen => _oathPanel != null && _oathPanel.activeSelf;

    private void Awake()
    {
        Active = this;
        L10n.Changed += OnLanguageChanged;
    }

    private void OnDestroy()
    {
        L10n.Changed -= OnLanguageChanged;
        if (Active == this) Active = null;
    }

    private void OnLanguageChanged() => _stale = true;

    // ---------------------------------------------------------------- open / close

    private void Open(Action onBegin, Action onBack)
    {
        _onBegin = onBegin;
        _onBack = onBack;
        RuntimeUi.EnsureEventSystem();
        LoadChoiceFromRecord();
        DestroyPanels();
        BuildPanels();
        _open = true;
        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();
        PlayerLock.SetCursorFree(true);
        if (EventSystem.current != null && _first != null) EventSystem.current.SetSelectedGameObject(_first.gameObject);
    }

    private void Close()
    {
        if (!_open) return;
        _open = false;
        _closedFrame = Time.frameCount;
        if (_panel != null) _panel.SetActive(false);
        if (_oathPanel != null) _oathPanel.SetActive(false);
        MenuFocus.Release();
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
    }

    private void Update()
    {
        if (!_open) return;
        PlayerLock.SetCursorFree(true); // every frame: focus changes re-lock it
        if (_stale)
        {
            _stale = false;
            DestroyPanels();
            BuildPanels();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
        }

        if (GameInput.Pressed(GameAction.MenuCancel))
        {
            Back();
            return;
        }

        MenuFocus.Keep(_panel, _first);
    }

    private void Back()
    {
        Action cb = _onBack;
        Close();
        cb?.Invoke();
    }

    private void Begin()
    {
        CampaignRules.Begin(_preset, _assists, _oaths, _kit, _torch);
        Action cb = _onBegin;
        Close();
        cb?.Invoke();
    }

    /// <summary>Test / tool hook: presses BEGIN.</summary>
    public void PressBegin() => Begin();

    /// <summary>Pre-selects the preset the last campaign used, falling back to STANDARD, and derives its assists and Oaths.</summary>
    private void LoadChoiceFromRecord()
    {
        PatientRecordData d = PatientRecord.Data;
        DifficultyPreset preset = Enum.IsDefined(typeof(DifficultyPreset), d.lastPreset) ? (DifficultyPreset)d.lastPreset : DifficultyPreset.Standard;
        ApplyPresetRules(preset);
    }

    private void ApplyPresetRules(DifficultyPreset preset)
    {
        _preset = preset;
        _assists = OathCatalogue.PresetAssists(preset);
        _oaths = OathCatalogue.PresetOaths(preset);
        _kit = StartingKit.StandardIssue;
        _torch = 0;
    }

    // ---------------------------------------------------------------- panels

    private void DestroyPanels()
    {
        if (_panel != null) Destroy(_panel);
        if (_oathPanel != null) Destroy(_oathPanel);
        _panel = _oathPanel = null;
        _first = null;
    }

    private static string S(string suffix) => L10n.T("ui.newcampaign." + suffix);

    private void BuildPanels()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        _panel = RuntimeUi.CreatePanel(canvas.transform, "NewCampaignScreen", new Color(0.02f, 0.02f, 0.03f, 1f));
        Transform root = _panel.transform;

        TextMeshProUGUI title = RuntimeUi.CreateText(root, "Title", S("title"), 76f, Color.white);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.AutoFit(title, 0.6f);
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(1400f, 100f));

        BuildPresets(root);

        Button begin = RuntimeUi.CreateButton(root, S("begin"), Vector2.zero, new Vector2(440f, 86f), 40f, ButtonIdle, ButtonHover, Color.white);
        begin.name = "Begin";
        RuntimeUi.Place((RectTransform)begin.transform, new Vector2(0.5f, 0f), new Vector2(240f, 140f), new Vector2(440f, 86f));
        begin.onClick.AddListener(Begin);
        RuntimeUi.BindButton(begin, "ui.newcampaign.begin");

        Button back = RuntimeUi.CreateButton(root, L10n.T("ui.common.back"), Vector2.zero, new Vector2(440f, 86f), 40f, ButtonIdle, ButtonHover, Color.white);
        back.name = "Back";
        RuntimeUi.Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(-240f, 140f), new Vector2(440f, 86f));
        back.onClick.AddListener(Back);
        RuntimeUi.BindButton(back, "ui.common.back");

        RefreshPresetLook();
    }

    private void BuildPresets(Transform root)
    {
        DifficultyPreset[] presets = { DifficultyPreset.Story, DifficultyPreset.Standard, DifficultyPreset.Nightmare };
        float[] xs = { -460f, 0f, 460f };
        for (int i = 0; i < 3; i++)
        {
            DifficultyPreset preset = presets[i];
            Button b = RuntimeUi.CreateButton(root, CampaignRules.PresetName(preset), Vector2.zero, new Vector2(420f, 92f), 38f, ButtonIdle, ButtonHover, Color.white);
            b.name = "Preset_" + preset;
            RuntimeUi.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(xs[i], -420f), new Vector2(420f, 92f));
            RuntimeUi.AutoFit(b.GetComponentInChildren<TMP_Text>(), 0.6f);
            b.onClick.AddListener(() => ChoosePreset(preset));
            SelectHint hint = b.gameObject.AddComponent<SelectHint>();
            hint.OnHint = () => ShowPresetDesc(preset);
            _presetButtons[i] = b;
            if (preset == _preset) _first = b;
        }
        if (_first == null) _first = _presetButtons[1];

        _presetDesc = RuntimeUi.CreateText(root, "PresetDesc", "", 32f, Dim);
        _presetDesc.textWrappingMode = TextWrappingModes.Normal;
        RuntimeUi.AutoFit(_presetDesc, 0.7f);
        RuntimeUi.Place(_presetDesc.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -560f), new Vector2(1400f, 110f));
    }

    private void ChoosePreset(DifficultyPreset preset)
    {
        ApplyPresetRules(preset);
        RefreshPresetLook();
        Selectable pick = _presetButtons[PresetOrder(preset)];
        if (EventSystem.current != null && pick != null) EventSystem.current.SetSelectedGameObject(pick.gameObject);
    }

    private static int PresetOrder(DifficultyPreset p) => p == DifficultyPreset.Story ? 0 : p == DifficultyPreset.Standard ? 1 : 2;

    private void ShowPresetDesc(DifficultyPreset preset)
    {
        if (_presetDesc != null) _presetDesc.text = L10n.T("ui.preset." + OathCatalogue.PresetSlug(preset) + ".desc");
    }

    private void RefreshPresetLook()
    {
        DifficultyPreset[] presets = { DifficultyPreset.Story, DifficultyPreset.Standard, DifficultyPreset.Nightmare };
        for (int i = 0; i < 3; i++)
        {
            if (_presetButtons[i] == null) continue;
            ColorBlock c = _presetButtons[i].colors;
            c.normalColor = presets[i] == _preset ? Accent * 0.8f : ButtonIdle;
            c.selectedColor = c.normalColor;
            _presetButtons[i].colors = c;
        }
        ShowPresetDesc(_preset);
    }

    // ---- test hooks
    public void SetChoiceForTest(DifficultyPreset preset, Assist assists, Oath oaths)
    {
        _preset = preset;
        _assists = assists;
        _oaths = oaths;
    }
}
