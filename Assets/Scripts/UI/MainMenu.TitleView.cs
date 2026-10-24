using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class MainMenu
{
    private static bool _versionLogged;
    private GameObject _confirmPanel;
    private Button _confirmBack;
    private int _savedFloor;
    private TextMeshProUGUI _confirmText;
    private System.Action _confirmAction;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetVersionLog() => _versionLogged = false;

    /// <summary>"v1.0.0": <c>Application.version</c> is PlayerSettings.bundleVersion in a build.</summary>
    public static string VersionLabel() => "v" + Application.version;

    /// <summary>F118 D8: SaveSystem.TryLoad set an unusable save aside - say so once, so a missing CONTINUE is not a mystery.</summary>
    private void ShowSaveProblemIfAny(Transform canvas)
    {
        SaveSystem.LoadProblem problem = SaveSystem.ConsumeProblem();
        if (problem == SaveSystem.LoadProblem.None) return;

        GameObject panel = RuntimeUi.CreatePanel(canvas, "SaveProblem", new Color(0.02f, 0.02f, 0.03f, 1f));
        panel.transform.SetAsLastSibling();

        TextMeshProUGUI heading = RuntimeUi.CreateText(panel.transform, "Heading", L10n.T("ui.menu.save_problem_heading"), 76f, accent);
        RuntimeUi.Bind(heading, "ui.menu.save_problem_heading", 0.6f);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 8f;
        RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1400f, 100f));

        string key = problem == SaveSystem.LoadProblem.TooNew ? "ui.menu.save_problem_body.too_new" : "ui.menu.save_problem_body.unreadable";
        TextMeshProUGUI body = RuntimeUi.CreateText(panel.transform, "Body", L10n.T(key), 34f, new Color(0.88f, 0.88f, 0.9f));
        RuntimeUi.Bind(body, key, 0.6f);
        body.textWrappingMode = TextWrappingModes.Normal;
        RuntimeUi.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -470f), new Vector2(1400f, 200f));

        Button ok = RuntimeUi.CreateButton(panel.transform, L10n.T("ui.common.continue"), new Vector2(0f, 330f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(ok, "ui.common.continue");
        ok.onClick.AddListener(() => Destroy(panel));
        if (UnityEngine.EventSystems.EventSystem.current != null) UnityEngine.EventSystems.EventSystem.current.SetSelectedGameObject(ok.gameObject);
    }

    private void BuildTitleScreen(Transform canvas)
    {
        _titleScreen = RuntimeUi.CreatePanel(canvas, "TitleScreen", Color.white);

        Image cover = _titleScreen.GetComponent<Image>();
        cover.sprite = coverImage != null ? coverImage : GenerateCover();
        cover.type = Image.Type.Simple;
        cover.color = Color.white;

        TextMeshProUGUI title = RuntimeUi.CreateText(_titleScreen.transform, "Title", gameTitle, 120f, Color.white);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 14f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -180f), new Vector2(1600f, 160f));

        TextMeshProUGUI sub = RuntimeUi.CreateText(_titleScreen.transform, "Tagline", L10n.T("ui.menu.tagline"), 38f, new Color(0.75f, 0.75f, 0.78f));
        RuntimeUi.Bind(sub, "ui.menu.tagline", 0.6f);
        RuntimeUi.Place(sub.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -290f), new Vector2(1200f, 60f));

        // F83: with a saved run a plain CONTINUE sits on top of the usual stack; without one the title screen is unchanged.
        if (SaveSystem.TryLoad(out SaveData save))
        {
            Button continueButton = RuntimeUi.CreateButton(_titleScreen.transform, L10n.T("ui.menu.continue"),
                new Vector2(0f, 560f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
            RuntimeUi.BindButton(continueButton, "ui.menu.continue");
            continueButton.onClick.AddListener(ContinueSavedRun);
            _savedFloor = save.floor;
        }

        Button start = RuntimeUi.CreateButton(_titleScreen.transform, L10n.T("ui.menu.start_game"),
            new Vector2(0f, 460f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(start, "ui.menu.start_game");
        start.onClick.AddListener(() => ConfirmThenStart(OpenNewCampaign)); // F122: the NEW ADMISSION screen first

        BuildDescentButton();

        // F86: the old rules screen, kept as a reference sheet now that START GAME opens the Intake.
        // F122: the row is split like CREDITS / EXIT - CONTROLS | RECORD (the Patient Record, with an amber dot when something new is unlocked).
        Button controls = RuntimeUi.CreateButton(_titleScreen.transform, L10n.T("ui.menu.controls"),
            new Vector2(-110f, 260f), new Vector2(200f, 86f), 32f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(controls, "ui.menu.controls");
        controls.onClick.AddListener(ShowControls);

        Button record = RuntimeUi.CreateButton(_titleScreen.transform, L10n.T("ui.menu.record"),
            new Vector2(110f, 260f), new Vector2(200f, 86f), 32f, buttonIdle, buttonHover, Color.white);
        record.name = "Record Button";
        RuntimeUi.BindButton(record, "ui.menu.record");
        record.onClick.AddListener(() => PatientRecordMenu.Show(PatientRecordMenu.Tab.Record, false));
        if (PatientRecord.HasNew)
        {
            GameObject dot = RuntimeUi.CreatePanel(record.transform, "NewDot", new Color(1f, 0.78f, 0.45f));
            dot.GetComponent<Image>().raycastTarget = false;
            RuntimeUi.Place((RectTransform)dot.transform, new Vector2(1f, 1f), new Vector2(-14f, -14f), new Vector2(16f, 16f));
        }

        // S2: the settings screen opens over the title screen and closes back onto it.
        Button settings = RuntimeUi.CreateButton(_titleScreen.transform, L10n.T("ui.menu.settings"),
            new Vector2(0f, 160f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(settings, "ui.menu.settings");
        settings.onClick.AddListener(SettingsMenu.Show);

        // The last row holds CREDITS and EXIT side by side (EXIT does not exist on the web, where CREDITS then takes the whole row).
        bool canQuit = GameFlow.CanQuit;
        Button credits = RuntimeUi.CreateButton(_titleScreen.transform, L10n.T("ui.menu.credits"),
            new Vector2(canQuit ? -110f : 0f, 60f), new Vector2(canQuit ? 200f : 420f, 86f), canQuit ? 32f : 40f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(credits, "ui.menu.credits");
        credits.onClick.AddListener(ShowCredits);

        Button exit = RuntimeUi.CreateButton(_titleScreen.transform, L10n.T("ui.menu.exit"),
            new Vector2(110f, 60f), new Vector2(200f, 86f), 32f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(exit, "ui.menu.exit");
        exit.onClick.AddListener(GameFlow.Quit);
        exit.gameObject.SetActive(canQuit);

        // Version: small and dim in the bottom-left corner (the touch controls only exist during a run, and nothing else on the title sits there).
        TextMeshProUGUI version = RuntimeUi.CreateText(_titleScreen.transform, "Version", VersionLabel(), 26f, new Color(0.6f, 0.6f, 0.64f, 0.7f));
        version.alignment = TextAlignmentOptions.BottomLeft;
        RectTransform vr = version.rectTransform;
        vr.anchorMin = vr.anchorMax = vr.pivot = Vector2.zero;
        vr.anchoredPosition = new Vector2(28f, 20f);
        vr.sizeDelta = new Vector2(400f, 40f);
    }

    // ---------------------------------------------------------------- F107: DESCENT

    /// <summary>
    /// F107 B2: the DESCENT button under START GAME, and beside it a small line: the deepest level reached once a record exists, or (while the
    /// button is dimmed) why it is dimmed. It is enabled once the Intake has been finished or skipped.
    /// </summary>
    private void BuildDescentButton()
    {
        bool unlocked = GameFlow.TutorialDone;
        Button descent = RuntimeUi.CreateButton(_titleScreen.transform, L10n.T("ui.menu.descent"),
            new Vector2(0f, 360f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, unlocked ? Color.white : new Color(1f, 1f, 1f, 0.35f));
        RuntimeUi.BindButton(descent, "ui.menu.descent");
        descent.interactable = unlocked;
        descent.onClick.AddListener(StartDescent);

        if (unlocked && !DescentRecords.HasRecord) return; // nothing to say yet

        TextMeshProUGUI note = RuntimeUi.CreateText(_titleScreen.transform, "DescentNote", "", 26f, new Color(0.75f, 0.75f, 0.78f));
        if (unlocked) RuntimeUi.Bind(note, "ui.menu.descent_best", 0.6f, ("n", DescentRecords.BestDepth));
        else RuntimeUi.Bind(note, "ui.menu.descent_locked", 0.6f);
        note.alignment = TextAlignmentOptions.Left;
        note.characterSpacing = 3f;
        RectTransform rect = note.rectTransform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = new Vector2(240f, 360f);
        rect.sizeDelta = new Vector2(520f, 40f);
    }

    /// <summary>
    /// DESCENT: a brand-new endless run from level -1, through the same reload a Retry uses (SkipMenuOnLoad -> StartGame). Never the Intake, the
    /// hub, the shop or the story save: it leaves them all alone.
    /// </summary>
    private void StartDescent()
    {
        if (!GameFlow.TutorialDone) return;

        RequestFullscreenOnTouch();
        DescentRun.Begin();
        GameFlow.IsDescent = true;
        GameFlow.CurrentFloor = 1;
        GameFlow.Restart(false, 0);
    }

    // ---------------------------------------------------------------- F83: saved run

    /// <summary>CONTINUE: restore the save and reload through the normal SkipMenuOnLoad path (StartGame, or the shop for a shop save).</summary>
    private void ContinueSavedRun()
    {
        GameFlow.IsDescent = false; // F107
        RequestFullscreenOnTouch();

        if (!SaveSystem.ResumeCampaign()) RefreshTitleScreen();
    }

    /// <summary>The reload throws the fullscreen gesture away, so ask for it now, on the tap itself (same as StartGame).</summary>
    private static void RequestFullscreenOnTouch()
    {
        TouchInput.Poll();
        if (TouchInput.Active)
        {
            try { Screen.fullScreen = true; } catch { /* best effort only */ }
        }
    }

    /// <summary>START GAME: with a saved run in the way, ask before throwing it away.</summary>
    private void ConfirmThenStart(System.Action begin)
    {
        if (!SaveSystem.HasSave)
        {
            begin();
            return;
        }

        if (_confirmPanel == null) BuildConfirmPanel();
        _confirmText.text = L10n.T("ui.menu.start_over_body", ("floor", _savedFloor));
        _confirmAction = begin;
        _confirmPanel.SetActive(true);
        _confirmPanel.transform.SetAsLastSibling();
    }

    private void BuildConfirmPanel()
    {
        _confirmPanel = RuntimeUi.CreatePanel(_titleScreen.transform, "ConfirmStartOver", new Color(0.02f, 0.02f, 0.03f, 0.97f));

        TextMeshProUGUI heading = RuntimeUi.CreateText(_confirmPanel.transform, "Heading", L10n.T("ui.menu.start_over_heading"), 76f, accent);
        RuntimeUi.Bind(heading, "ui.menu.start_over_heading", 0.6f);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 8f;
        RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -300f), new Vector2(1400f, 100f));

        _confirmText = RuntimeUi.CreateText(_confirmPanel.transform, "Body", "", 40f, new Color(0.88f, 0.88f, 0.9f));
        RuntimeUi.Place(_confirmText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -420f), new Vector2(1400f, 80f));

        Button startOver = RuntimeUi.CreateButton(_confirmPanel.transform, L10n.T("ui.menu.start_over"),
            new Vector2(0f, 400f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(startOver, "ui.menu.start_over");
        startOver.onClick.AddListener(() =>
        {
            SaveSystem.Delete();
            _confirmPanel.SetActive(false);
            System.Action action = _confirmAction;
            _confirmAction = null;
            action?.Invoke();
        });

        Button back = RuntimeUi.CreateButton(_confirmPanel.transform, L10n.T("ui.common.back"),
            new Vector2(0f, 290f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(back, "ui.common.back");
        back.onClick.AddListener(() => _confirmPanel.SetActive(false));
        _confirmBack = back;

        _confirmPanel.SetActive(false);
    }

    /// <summary>The save turned out unusable when CONTINUE was pressed (TryLoad already deleted it): rebuild the title screen without CONTINUE.</summary>
    private void RefreshTitleScreen()
    {
        bool wasActive = _titleScreen != null && _titleScreen.activeSelf;
        if (_titleScreen != null) Destroy(_titleScreen);
        _confirmPanel = null;

        Canvas canvas = RuntimeUi.ResolveCanvas();
        BuildTitleScreen(canvas.transform);
        _titleScreen.SetActive(wasActive);
        // Sits under the rules screen in sibling order, as it did when first built.
        if (_rulesScreen != null) _titleScreen.transform.SetSiblingIndex(_rulesScreen.transform.GetSiblingIndex());
    }
}
