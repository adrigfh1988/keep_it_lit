using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// F122 D14: the NEW ADMISSION screen START GAME opens - a difficulty preset (STORY / STANDARD / NIGHTMARE), the individual assists, the starting kit and torch the
/// Patient Record has unlocked, and (once an ending has been seen) the Oaths on a sub-panel. BEGIN hands the choice to <c>CampaignRules.Begin</c>; the caller
/// (MainMenu) decides whether floor 1 has to be rebuilt. Plain STANDARD with nothing else chosen is today's game: BEGIN there changes nothing.
/// Built lazily and rebuilt on a language change; every row is a Selectable (steppers are OFF/ON toggles, pattern P2), the gamepad is kept on a row by
/// <see cref="MenuFocus"/>, Esc / B steps back out. <see cref="BlocksBack"/> keeps the title screen from reacting to the same press.
/// Test names: NewCampaignScreen, Preset_Story|Standard|Nightmare, Assist_&lt;slug&gt;, Oath_&lt;slug&gt;, Kit, Torch, Oaths, Begin, Back.
/// </summary>
public class NewCampaignScreen : MonoBehaviour
{
    private static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    private static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);
    private static readonly Color Accent = new Color(0.86f, 0.12f, 0.09f);
    private static readonly Color Dim = new Color(0.78f, 0.78f, 0.8f);
    private static readonly Color Amber = new Color(1f, 0.78f, 0.45f);

    public static NewCampaignScreen Active { get; private set; }

    /// <summary>True while the screen (or its Oaths sub-panel) is up.</summary>
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
    private Selectable _first, _oathFirst;
    private readonly Button[] _presetButtons = new Button[3];
    private TextMeshProUGUI _presetDesc, _hint, _assistedNote, _oathHint, _oathSummary, _oathsRowSummary;
    private readonly List<StartingKit> _kitOptions = new List<StartingKit>();
    private readonly List<int> _torchOptions = new List<int>();
    private Button _oathsButton;

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
            bool oathsWereOpen = OathPanelOpen;
            DestroyPanels();
            BuildPanels();
            _panel.SetActive(true);
            _panel.transform.SetAsLastSibling();
            if (oathsWereOpen) OpenOaths();
        }

        if (GameInput.Pressed(GameAction.MenuCancel))
        {
            if (OathPanelOpen) CloseOaths();
            else Back();
            return;
        }

        if (OathPanelOpen) MenuFocus.Keep(_oathPanel, _oathFirst);
        else MenuFocus.Keep(_panel, _first);
    }

    private void Back()
    {
        Action cb = _onBack;
        Close();
        cb?.Invoke();
    }

    private void Begin()
    {
        int torch = _torchOptions.Contains(_torch) ? _torch : 0;
        CampaignRules.Begin(_preset, _assists, _oaths, _kit, torch);
        Action cb = _onBegin;
        Close();
        cb?.Invoke();
    }

    /// <summary>Test / tool hook: presses BEGIN.</summary>
    public void PressBegin() => Begin();

    /// <summary>Pre-selects what the last campaign used, falling back to STANDARD / none when something is no longer unlocked.</summary>
    private void LoadChoiceFromRecord()
    {
        PatientRecordData d = PatientRecord.Data;
        _preset = Enum.IsDefined(typeof(DifficultyPreset), d.lastPreset) ? (DifficultyPreset)d.lastPreset : DifficultyPreset.Standard;
        _assists = (Assist)d.lastAssists & OathCatalogue.AllAssists;
        _oaths = PatientRecord.IsUnlocked(Unlock.Oaths) ? (Oath)d.lastOaths & OathCatalogue.AllOaths : Oath.None;
        if (!OathCatalogue.IsAvailable(Oath.DoorsDontHold)) _oaths &= ~Oath.DoorsDontHold;
        _kit = Enum.IsDefined(typeof(StartingKit), d.lastKit) && PatientRecord.IsKitUnlocked((StartingKit)d.lastKit) ? (StartingKit)d.lastKit : StartingKit.StandardIssue;
        _torch = PatientRecord.IsTorchColourUnlocked(d.lastTorch) ? d.lastTorch : 0;
        if (!OathLegal) _oaths = Oath.None;
    }

    private bool OathLegal => CampaignRules.IsOathChoiceLegal(_preset, _assists);

    // ---------------------------------------------------------------- panels

    private void DestroyPanels()
    {
        if (_panel != null) Destroy(_panel);
        if (_oathPanel != null) Destroy(_oathPanel);
        _panel = _oathPanel = null;
        _first = _oathFirst = null;
        _kitOptions.Clear();
        _torchOptions.Clear();
    }

    private static string S(string suffix) => L10n.T("ui.newcampaign." + suffix);
    private static string[] OffOn() => new[] { L10n.T("ui.settings.opt.off"), L10n.T("ui.settings.opt.on") };

    private void BuildPanels()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        _panel = RuntimeUi.CreatePanel(canvas.transform, "NewCampaignScreen", new Color(0.02f, 0.02f, 0.03f, 1f));
        Transform root = _panel.transform;

        TextMeshProUGUI title = RuntimeUi.CreateText(root, "Title", S("title"), 68f, Color.white);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.AutoFit(title, 0.6f);
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1400f, 90f));

        BuildPresets(root);

        // left column: the assists
        TextMeshProUGUI assistHead = RuntimeUi.CreateText(root, "AssistsHeader", S("assists_header"), 30f, Amber);
        assistHead.alignment = TextAlignmentOptions.Left;
        assistHead.characterSpacing = 6f;
        RuntimeUi.Place(assistHead.rectTransform, new Vector2(0.5f, 1f), new Vector2(-480f, -330f), new Vector2(840f, 40f));
        float y = -392f;
        foreach (OathCatalogue.AssistDef def in OathCatalogue.Assists)
        {
            AddAssistRow(root, def, y);
            y -= 62f;
        }

        // right column: kit, torch, oaths
        bool anyRight = PatientRecord.IsUnlocked(Unlock.Oaths) || PatientRecord.IsKitUnlocked(StartingKit.Scavenger) || PatientRecord.IsKitUnlocked(StartingKit.Runner) || PatientRecord.IsKitUnlocked(StartingKit.Cartographer)
            || PatientRecord.IsTorchColourUnlocked(4) || PatientRecord.IsTorchColourUnlocked(5) || PatientRecord.IsTorchColourUnlocked(6);
        TextMeshProUGUI admissionHead = RuntimeUi.CreateText(root, "KitHeader", S("kit_header"), 30f, Amber);
        admissionHead.gameObject.SetActive(anyRight); // nothing unlocked yet: no empty column heading
        admissionHead.alignment = TextAlignmentOptions.Left;
        admissionHead.characterSpacing = 6f;
        RuntimeUi.Place(admissionHead.rectTransform, new Vector2(0.5f, 1f), new Vector2(480f, -330f), new Vector2(840f, 40f));
        float ry = -392f;
        BuildKitRow(root, ref ry);
        BuildTorchRow(root, ref ry);
        BuildOathsButton(root, ry);

        _hint = RuntimeUi.CreateText(root, "Hint", "", 27f, Dim);
        _hint.alignment = TextAlignmentOptions.TopLeft;
        _hint.textWrappingMode = TextWrappingModes.Normal;
        RuntimeUi.AutoFit(_hint, 0.7f);
        RuntimeUi.Place(_hint.rectTransform, new Vector2(0.5f, 1f), new Vector2(480f, -600f), new Vector2(840f, 190f));

        _assistedNote = RuntimeUi.CreateText(root, "AssistedNote", S("assisted_note"), 26f, Amber);
        _assistedNote.textWrappingMode = TextWrappingModes.NoWrap;
        RuntimeUi.AutoFit(_assistedNote, 0.6f);
        RuntimeUi.Place(_assistedNote.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -850f), new Vector2(1700f, 40f));

        Button begin = RuntimeUi.CreateButton(root, S("begin"), Vector2.zero, new Vector2(440f, 80f), 40f, ButtonIdle, ButtonHover, Color.white);
        begin.name = "Begin";
        RuntimeUi.Place((RectTransform)begin.transform, new Vector2(0.5f, 0f), new Vector2(240f, 70f), new Vector2(440f, 80f));
        begin.onClick.AddListener(Begin);
        RuntimeUi.BindButton(begin, "ui.newcampaign.begin");

        Button back = RuntimeUi.CreateButton(root, L10n.T("ui.common.back"), Vector2.zero, new Vector2(440f, 80f), 40f, ButtonIdle, ButtonHover, Color.white);
        back.name = "Back";
        RuntimeUi.Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(-240f, 70f), new Vector2(440f, 80f));
        back.onClick.AddListener(Back);
        RuntimeUi.BindButton(back, "ui.common.back");

        BuildOathPanel(canvas.transform);
        RefreshPresetLook();
        RefreshNotes();
    }

    private void BuildPresets(Transform root)
    {
        DifficultyPreset[] presets = { DifficultyPreset.Story, DifficultyPreset.Standard, DifficultyPreset.Nightmare };
        float[] xs = { -440f, 0f, 440f };
        for (int i = 0; i < 3; i++)
        {
            DifficultyPreset preset = presets[i];
            Button b = RuntimeUi.CreateButton(root, CampaignRules.PresetName(preset), Vector2.zero, new Vector2(400f, 76f), 36f, ButtonIdle, ButtonHover, Color.white);
            b.name = "Preset_" + preset;
            RuntimeUi.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(xs[i], -175f), new Vector2(400f, 76f));
            RuntimeUi.AutoFit(b.GetComponentInChildren<TMP_Text>(), 0.6f);
            b.onClick.AddListener(() => ChoosePreset(preset));
            SelectHint hint = b.gameObject.AddComponent<SelectHint>();
            hint.OnHint = () => ShowPresetDesc(preset);
            _presetButtons[i] = b;
            if (preset == _preset) _first = b;
        }
        if (_first == null) _first = _presetButtons[1];

        _presetDesc = RuntimeUi.CreateText(root, "PresetDesc", "", 28f, Dim);
        _presetDesc.textWrappingMode = TextWrappingModes.Normal;
        RuntimeUi.AutoFit(_presetDesc, 0.7f);
        RuntimeUi.Place(_presetDesc.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -250f), new Vector2(1500f, 70f));
    }

    private void ChoosePreset(DifficultyPreset preset)
    {
        _preset = preset;
        _assists = OathCatalogue.PresetAssists(preset); // a preset sets the assists to its defaults; editing them afterwards keeps the preset
        if (!OathLegal) _oaths = Oath.None;
        // Rebuild the rows so the steppers show the new values (the language-change rebuild does the same job).
        bool oaths = OathPanelOpen;
        DestroyPanels();
        BuildPanels();
        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();
        if (oaths) OpenOaths();
        Selectable pick = _presetButtons[(int)PresetOrder(preset)];
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

    private void RefreshNotes()
    {
        if (_assistedNote != null) _assistedNote.gameObject.SetActive(!OathLegal || WouldBeAssisted());
        if (_oathsButton != null)
        {
            _oathsButton.interactable = OathLegal;
            if (_oathsRowSummary != null) _oathsRowSummary.text = S("oaths_button") + ":  " + (OathLegal ? OathSummaryText() : S("oaths_need_standard"));
        }
    }

    private bool WouldBeAssisted() => _preset == DifficultyPreset.Story || (_assists & OathCatalogue.RecordAffectingAssists) != 0;

    // ---------------------------------------------------------------- rows

    private void AddLabel(Transform parent, string text, Vector2 position, float width)
    {
        TextMeshProUGUI label = RuntimeUi.CreateText(parent, text + " Label", text, 30f, Dim);
        label.alignment = TextAlignmentOptions.Left;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        RuntimeUi.AutoFit(label, 0.6f);
        RuntimeUi.Place(label.rectTransform, new Vector2(0.5f, 1f), position, new Vector2(width, 54f));
    }

    private StepperControl AddStepper(Transform parent, string name, Vector2 position, Vector2 size)
    {
        StepperControl stepper = RuntimeUi.CreateStepper(parent, name, new Vector2(0.5f, 1f), position, size, 30f, ButtonIdle, ButtonHover);
        stepper.name = name;
        TMP_Text valueLabel = stepper.transform.Find("Value") != null ? stepper.transform.Find("Value").GetComponent<TMP_Text>() : null;
        if (valueLabel != null) { valueLabel.textWrappingMode = TextWrappingModes.NoWrap; RuntimeUi.AutoFit(valueLabel, 0.6f); }
        return stepper;
    }

    private void AddAssistRow(Transform root, OathCatalogue.AssistDef def, float y)
    {
        string slug = def.Slug;
        AddLabel(root, L10n.T("ui.assist." + slug + ".name"), new Vector2(-660f, y), 520f);
        StepperControl stepper = AddStepper(root, "Assist_" + slug, new Vector2(-230f, y), new Vector2(330f, 54f));
        Assist id = def.Id;
        stepper.Configure(OffOn(), (_assists & id) != 0 ? 1 : 0, true, i =>
        {
            _assists = i == 1 ? _assists | id : _assists & ~id;
            if (!OathLegal) _oaths = Oath.None;
            RefreshNotes();
        });
        SelectHint hint = stepper.gameObject.AddComponent<SelectHint>();
        hint.OnHint = () => SetHint(L10n.T("ui.assist." + slug + ".desc"));
    }

    private void BuildKitRow(Transform root, ref float y)
    {
        foreach (StartingKit kit in (StartingKit[])Enum.GetValues(typeof(StartingKit)))
        {
            if (PatientRecord.IsKitUnlocked(kit)) _kitOptions.Add(kit);
        }
        if (_kitOptions.Count <= 1) return; // nothing unlocked beyond STANDARD ISSUE: no row

        AddLabel(root, S("kit"), new Vector2(260f, y), 300f);
        string[] names = new string[_kitOptions.Count];
        for (int i = 0; i < names.Length; i++) names[i] = L10n.T("ui.kit." + OathCatalogue.KitSlug(_kitOptions[i]) + ".name");
        StepperControl stepper = AddStepper(root, "Kit", new Vector2(650f, y), new Vector2(480f, 54f));
        stepper.Configure(names, Mathf.Max(0, _kitOptions.IndexOf(_kit)), true, i =>
        {
            _kit = _kitOptions[i];
            ShowKitHint();
        });
        SelectHint hint = stepper.gameObject.AddComponent<SelectHint>();
        hint.OnHint = ShowKitHint;
        y -= 62f;
    }

    private void ShowKitHint() => SetHint(L10n.T("ui.kit." + OathCatalogue.KitSlug(_kit) + ".desc"));

    private void BuildTorchRow(Transform root, ref float y)
    {
        _torchOptions.Add(0);
        for (int i = ShopCatalogue.ShopTorchSwatches + 1; i < ShopCatalogue.TorchColours.Length; i++)
        {
            if (PatientRecord.IsTorchColourUnlocked(i)) _torchOptions.Add(i);
        }
        if (_torchOptions.Count <= 1) return;

        AddLabel(root, S("torch"), new Vector2(260f, y), 300f);
        string[] names = new string[_torchOptions.Count];
        for (int i = 0; i < names.Length; i++) names[i] = ShopCatalogue.PaletteName(true, _torchOptions[i]);
        StepperControl stepper = AddStepper(root, "Torch", new Vector2(650f, y), new Vector2(480f, 54f));
        stepper.Configure(names, Mathf.Max(0, _torchOptions.IndexOf(_torch)), true, i => _torch = _torchOptions[i]);
        SelectHint hint = stepper.gameObject.AddComponent<SelectHint>();
        hint.OnHint = () => SetHint(S("torch_hint"));
        y -= 62f;
    }

    private void BuildOathsButton(Transform root, float y)
    {
        if (!PatientRecord.IsUnlocked(Unlock.Oaths)) return;

        _oathsButton = RuntimeUi.CreateButton(root, S("oaths_button"), Vector2.zero, new Vector2(840f, 54f), 30f, ButtonIdle, ButtonHover, Color.white);
        _oathsButton.name = "Oaths";
        RuntimeUi.Place((RectTransform)_oathsButton.transform, new Vector2(0.5f, 1f), new Vector2(480f, y), new Vector2(840f, 54f));
        TMP_Text caption = _oathsButton.GetComponentInChildren<TMP_Text>();
        caption.alignment = TextAlignmentOptions.Left;
        caption.margin = new Vector4(20f, 0f, 20f, 0f);
        _oathsRowSummary = caption as TextMeshProUGUI;
        RuntimeUi.AutoFit(caption, 0.6f);
        _oathsButton.onClick.AddListener(OpenOaths);
        SelectHint hint = _oathsButton.gameObject.AddComponent<SelectHint>();
        hint.OnHint = () => SetHint(S("oaths_hint"));
    }

    private void SetHint(string text)
    {
        if (_hint != null) _hint.text = text;
    }

    // ---------------------------------------------------------------- the Oaths sub-panel

    private void BuildOathPanel(Transform canvas)
    {
        if (!PatientRecord.IsUnlocked(Unlock.Oaths)) return;

        _oathPanel = RuntimeUi.CreatePanel(canvas, "OathsScreen", new Color(0.02f, 0.02f, 0.03f, 1f));
        Transform root = _oathPanel.transform;

        TextMeshProUGUI title = RuntimeUi.CreateText(root, "Title", S("oaths_title"), 68f, Color.white);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.AutoFit(title, 0.6f);
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1400f, 90f));

        float y = -190f;
        foreach (OathCatalogue.OathDef def in OathCatalogue.Oaths)
        {
            if (!OathCatalogue.IsAvailable(def.Id)) continue;
            string slug = def.Slug;
            Oath id = def.Id;
            AddLabel(root, L10n.T("ui.oath." + slug + ".name") + "   +" + def.Weight + "   x" + (1f + def.ShardBonus).ToString("0.00", L10n.Culture), new Vector2(-250f, y), 960f);
            StepperControl stepper = AddStepper(root, "Oath_" + slug, new Vector2(520f, y), new Vector2(330f, 54f));
            stepper.Configure(OffOn(), (_oaths & id) != 0 ? 1 : 0, true, i =>
            {
                _oaths = i == 1 ? _oaths | id : _oaths & ~id;
                RefreshOathSummary();
                RefreshNotes();
            });
            SelectHint hint = stepper.gameObject.AddComponent<SelectHint>();
            hint.OnHint = () => { if (_oathHint != null) _oathHint.text = L10n.T("ui.oath." + slug + ".desc"); };
            if (_oathFirst == null) _oathFirst = stepper;
            y -= 62f;
        }

        _oathHint = RuntimeUi.CreateText(root, "OathHint", "", 28f, Dim);
        _oathHint.textWrappingMode = TextWrappingModes.Normal;
        RuntimeUi.AutoFit(_oathHint, 0.7f);
        RuntimeUi.Place(_oathHint.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y - 20f), new Vector2(1500f, 80f));

        _oathSummary = RuntimeUi.CreateText(root, "OathSummary", "", 38f, Amber);
        _oathSummary.characterSpacing = 4f;
        RuntimeUi.AutoFit(_oathSummary, 0.6f);
        RuntimeUi.Place(_oathSummary.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y - 110f), new Vector2(1500f, 56f));

        Button done = RuntimeUi.CreateButton(root, S("oaths_done"), Vector2.zero, new Vector2(440f, 80f), 40f, ButtonIdle, ButtonHover, Color.white);
        done.name = "OathsDone";
        RuntimeUi.Place((RectTransform)done.transform, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(440f, 80f));
        done.onClick.AddListener(CloseOaths);
        RuntimeUi.BindButton(done, "ui.newcampaign.oaths_done");

        RefreshOathSummary();
        _oathPanel.SetActive(false);
    }

    private string OathSummaryText()
    {
        int rank = OathCatalogue.RankOf(_oaths) + (_preset == DifficultyPreset.Nightmare ? 2 : 0);
        float mult = WouldBeAssisted() ? 1f : 1f + OathCatalogue.ShardBonusOf(_oaths) + (_preset == DifficultyPreset.Nightmare ? 0.25f : 0f);
        return L10n.T("ui.newcampaign.oath_summary", ("rank", rank), ("mult", mult.ToString("0.00", L10n.Culture)));
    }

    private void RefreshOathSummary()
    {
        if (_oathSummary != null) _oathSummary.text = OathSummaryText();
    }

    private void OpenOaths()
    {
        if (_oathPanel == null || !OathLegal) return;
        RefreshOathSummary();
        _oathPanel.SetActive(true);
        _oathPanel.transform.SetAsLastSibling();
        if (EventSystem.current != null && _oathFirst != null) EventSystem.current.SetSelectedGameObject(_oathFirst.gameObject);
    }

    private void CloseOaths()
    {
        if (_oathPanel == null) return;
        _oathPanel.SetActive(false);
        RefreshNotes();
        if (EventSystem.current != null && _oathsButton != null) EventSystem.current.SetSelectedGameObject(_oathsButton.gameObject);
    }

    // ---- test hooks
    public void SetChoiceForTest(DifficultyPreset preset, Assist assists, Oath oaths)
    {
        _preset = preset;
        _assists = assists;
        _oaths = oaths;
    }
}
