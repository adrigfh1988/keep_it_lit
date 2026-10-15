using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Title screen, rules screen, and the gate that holds the game frozen until the player presses Start.
///
/// The maze, the player and the hunter are all built by MazeGenerator before this runs - the world is
/// already standing behind the cover art. This just stops the clock until someone asks for it.
/// </summary>
[DefaultExecutionOrder(-80)]
public class MainMenu : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string gameTitle = "KEEP IT LIT";
    // S9: the tagline now comes from ui.menu.tagline (the old serialized field is gone).

    [Header("Cover")]
    [Tooltip("Leave empty to use the cover generated in code. Drop a painted one in here when you have it.")]
    [SerializeField] private Sprite coverImage;
    [Tooltip("Kept modest on purpose: the cover is built pixel by pixel on the CPU, and it is a soft gradient that stretches without showing its resolution")]
    [SerializeField] private int generatedCoverWidth = 960;
    [SerializeField] private int generatedCoverHeight = 540;

    [Header("Look")]
    [SerializeField] private Color accent = new Color(0.86f, 0.12f, 0.09f);
    [SerializeField] private Color buttonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    [SerializeField] private Color buttonHover = new Color(0.48f, 0.08f, 0.06f, 1f);

    private GameObject _titleScreen;
    private GameObject _rulesScreen;
    private GameObject _creditsScreen;
    private ScrollRect _creditsScroll;
    private TextMeshProUGUI _controlText;
    private int _sheetVersion = -1;
    private TextMeshProUGUI _rulesBody;
    private Texture2D _generatedCover;
    private Sprite _generatedSprite;
    private Transform _player;
    private bool _menuOpen = true;
    // F86: the rules screen starts the run only as START GAME's fallback (no Intake built); from CONTROLS it is just a back button.
    private bool _rulesStartsGame = true;
    private LocalizedLabel _rulesLabel;

    [Header("Music")]
    [Tooltip("Volume of the title music (Discovering Rooms)")]
    [SerializeField] private float musicVolume = 0.3f;
    [Tooltip("Seconds the title music takes to fade out once the run starts (real time - the clock is frozen behind the menu)")]
    [SerializeField] private float musicFadeSeconds = 1.2f;

    private AudioClip _musicClip;
    private AudioSource _music;
    private bool _musicFading;

    /// <summary>F107 B2: how long the torch stays off and locked at the start of a descent level (s, real time).</summary>
    private const float ColdOpenSeconds = 1.5f;

    /// <summary>True while the title or rules screen is up. The pause menu stays out of the way.</summary>
    public bool IsOpen => _menuOpen;

    public void Configure(Transform player, AudioClip music = null)
    {
        _musicClip = music;
        if (player == null) return;
        _player = player;

        // Freeze immediately as well as in Start. Mouse look is not scaled by deltaTime, so without
        // this the camera can be nudged on frame 0 before Start has run.
        PlayerLock.Freeze(_player, true);
    }

    private void Awake()
    {
        // Freeze everything. The maze is fully built by now, so this catches the world on frame one.
        Time.timeScale = 0f;
    }

    private void Start()
    {
        // Start rather than Awake: StarterAssetsInputs.Awake forces the cursor hidden, and the
        // scene's own components need to have finished initialising before the UI goes over them.
        if (!_versionLogged)
        {
            _versionLogged = true;
            Debug.Log($"KEEP IT LIT v{Application.version} (Unity {Application.unityVersion}, {Application.platform})");
        }

        RuntimeUi.EnsureEventSystem();

        Canvas canvas = RuntimeUi.ResolveCanvas();
        RuntimeUi.MakeCanvasResolutionIndependent(canvas);

        BuildTitleScreen(canvas.transform);
        BuildRulesScreen(canvas.transform);
        _rulesScreen.SetActive(false);

        PlayerLock.Freeze(_player, true);

        // F122: the new-campaign screen chose rules that change how floor 1 is built, so the scene was rebuilt; begin that campaign now (no title flash).
        if (GameFlow.BeginCampaignOnLoad)
        {
            GameFlow.BeginCampaignOnLoad = false;
            BeginNewCampaign();
            return;
        }

        // A retry goes straight into the game. Through StartGame rather than by hiding the screens:
        // it is what unfreezes the player, relocks the cursor and undoes the zeroed clock from Awake.
        if (GameFlow.SkipMenuOnLoad)
        {
            GameFlow.SkipMenuOnLoad = false;

            // F83: CONTINUE on a save taken in the shop. Same housekeeping as StartGame, but it is not a run:
            // IsRunActive stays false and the player lands in the shop.
            if (GameFlow.ResumeInShop)
            {
                GameFlow.ResumeInShop = false;
                CloseMenus();
                GameOutcome outcome = FindAnyObjectByType<GameOutcome>();
                if (outcome != null) outcome.EnterShopDirectly();
                else GameFlow.NextFloor();
                return;
            }

            // F92: the next floor (the shop door, or CONTINUE from a floor-start save) opens in the Asylum hub when the scene has a
            // usable one; Retry / Restart never set this flag. With no hub it is the plain StartGame it always was.
            bool viaHub = GameFlow.ArriveInHub;
            GameFlow.ArriveInHub = false;
            AsylumHub hub = AsylumHub.Active;
            if (viaHub && hub != null && hub.CanEnter)
            {
                CloseMenus();
                hub.EnterFromMenu();
                return;
            }

            StartGame();
            return;
        }

        PlayMenuMusic();

        // F118: title path only - the photosensitivity card first (until acknowledged), then the "your save was set aside" notice.
        if (!PhotosensitivityNotice.Acknowledged) PhotosensitivityNotice.Show(canvas.transform, () => ShowSaveProblemIfAny(canvas.transform));
        else ShowSaveProblemIfAny(canvas.transform);
    }

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

    /// <summary>The title music, only when the title screen is actually shown (a retry or CONTINUE skips it).</summary>
    private void PlayMenuMusic()
    {
        if (_musicClip == null) return;

        _music = gameObject.AddComponent<AudioSource>();
        _music.playOnAwake = false;
        _music.loop = true;
        _music.spatialBlend = 0f;
        _music.clip = _musicClip;
        GameSettings.RouteMusic(_music); // S2: the Music mixer group
        _music.volume = musicVolume;
        _music.Play();
    }

    private void OnDestroy()
    {
        // Leaving play mode with a zeroed clock would carry into the next session
        Time.timeScale = 1f;

        if (_generatedSprite != null) Destroy(_generatedSprite);
        if (_generatedCover != null) Destroy(_generatedCover);
    }

    private void Update()
    {
        if (_musicFading && _music != null)
        {
            // Unscaled: CloseMenus restores the clock, but the fade must not depend on it.
            // Clamped step: one long hitch (a loading frame) must not swallow the whole fade.
            float step = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _music.volume = Mathf.MoveTowards(_music.volume, 0f, musicVolume / Mathf.Max(0.01f, musicFadeSeconds) * step);
            if (_music.volume <= 0f)
            {
                _music.Stop();
                _musicFading = false;
            }
        }

        if (!_menuOpen) return;

        if (_creditsScreen != null && _creditsScreen.activeSelf) UpdateCredits();

        // F119: the controls sheet follows the device in use (keyboard keys, gamepad buttons) and any rebind.
        if (_rulesScreen != null && _rulesScreen.activeSelf && _controlText != null)
        {
            GameInput.Poll();
            if (_sheetVersion != GameInput.Version)
            {
                _sheetVersion = GameInput.Version;
                _controlText.text = ControlsText();
            }
        }

        // F119: pad B / Esc steps back out of the controls sheet and the start-over confirmation (the on-screen BACK buttons do the same).
        if (!SettingsMenu.BlocksPause && !NewCampaignScreen.BlocksBack && !PatientRecordMenu.BlocksBack && GameInput.Pressed(GameAction.MenuCancel))
        {
            if (_confirmPanel != null && _confirmPanel.activeSelf) _confirmPanel.SetActive(false);
            else if (_rulesScreen != null && _rulesScreen.activeSelf && !_rulesStartsGame)
            {
                _rulesScreen.SetActive(false);
                _titleScreen.SetActive(true);
            }
        }

        KeepPadFocus();

        // Held every frame rather than set once: StarterAssetsInputs re-locks the cursor on every
        // focus change, so a single assignment loses the moment the window is alt-tabbed.
        PlayerLock.SetCursorFree(true);
    }

    /// <summary>F119: with a gamepad the open screen always has a selected button (CONTINUE or START GAME on the title, BACK on the others). Left alone while Settings or
    /// an overlay (the photosensitivity card, the save notice) owns the selection.</summary>
    private void KeepPadFocus()
    {
        if (SettingsMenu.BlocksPause || NewCampaignScreen.IsOpen || PatientRecordMenu.BlocksBack) return; // F122: those screens keep their own focus
        UnityEngine.EventSystems.EventSystem es = UnityEngine.EventSystems.EventSystem.current;
        GameObject cur = es != null ? es.currentSelectedGameObject : null;
        if (cur != null && cur.activeInHierarchy && !OwnsScreen(cur.transform)) return;

        GameObject root = _creditsScreen != null && _creditsScreen.activeSelf ? _creditsScreen
            : _confirmPanel != null && _confirmPanel.activeSelf ? _confirmPanel
            : _rulesScreen != null && _rulesScreen.activeSelf ? _rulesScreen
            : _titleScreen;
        MenuFocus.Keep(root, root == _confirmPanel ? _confirmBack : null);
    }

    private bool OwnsScreen(Transform t) =>
        (_titleScreen != null && t.IsChildOf(_titleScreen.transform)) || (_rulesScreen != null && t.IsChildOf(_rulesScreen.transform)) ||
        (_creditsScreen != null && t.IsChildOf(_creditsScreen.transform));

    // ---------------------------------------------------------------- flow

    private void ShowRules(bool startsGame)
    {
        _rulesStartsGame = startsGame;
        _rulesLabel?.SetKey(startsGame ? "ui.rules.start" : "ui.common.back");

        // F76: TouchControls' own -83 Update has not necessarily run yet this frame (this is an
        // onClick callback), and the very first tap is exactly the case that needs Active to be
        // current right now, not next frame - Poll() is idempotent per frame either way.
        TouchInput.Poll();
        if (_controlText != null) _controlText.text = ControlsText();
        if (_rulesBody != null) _rulesBody.text = RulesText();

        _titleScreen.SetActive(false);
        _rulesScreen.SetActive(true);
    }

    private void StartGame()
    {
        CloseMenus();

        GameFlow.BeginRun();
        if (GameFlow.IsDescent) StartCoroutine(ColdOpenRoutine()); // F107 B2
    }

    /// <summary>F107 B2: every descent level opens in the dark - the torch is off and locked for a beat, so the first thing the player does is stand in it.</summary>
    private System.Collections.IEnumerator ColdOpenRoutine()
    {
        Flashlight torch = FindAnyObjectByType<Flashlight>();
        if (torch == null) yield break;

        torch.SetOn(false);
        torch.InputEnabled = false;
        yield return new WaitForSecondsRealtime(ColdOpenSeconds);

        // An ending inside the beat has already taken the torch away for good.
        if (torch != null && !GameOutcome.IsOver) torch.InputEnabled = true;
    }

    /// <summary>
    /// F86: START GAME. A new campaign opens in the Intake (the playable tutorial) instead of the rules screen; with no
    /// IntakeRoom built it falls back to the old rules screen, which then starts the run.
    /// </summary>
    private void BeginNewCampaign()
    {
        GameFlow.IsDescent = false; // F107: the story can never start with a descent flag left on
        IntakeRoom room = FindAnyObjectByType<IntakeRoom>();
        if (room == null || !room.IsComplete)
        {
            ShowRules(true);
            return;
        }

        // CloseMenus unfreezes the player and restores the clock; Begin (same frame) re-freezes for the wake-up beat.
        CloseMenus();
        room.Begin();
    }

    /// <summary>F122: START GAME. The NEW ADMISSION screen (preset, assists, kit, Oaths); BEGIN continues in <see cref="OnCampaignChosen"/>, BACK uncovers the title.</summary>
    private void OpenNewCampaign()
    {
        NewCampaignScreen.Show(OnCampaignChosen, null);
    }

    /// <summary>
    /// F122: the choice is made (CampaignRules.Begin has run). Floor 1 was already built at the title, so unless the choice leaves the build unchanged - always true
    /// for plain STANDARD - the scene is rebuilt once with the rules in place and the campaign begins on load (BeginCampaignOnLoad -> Start).
    /// </summary>
    private void OnCampaignChosen()
    {
        if (CampaignRules.Signature() == CampaignRules.BuiltSignature)
        {
            BeginNewCampaign();
            return;
        }

        RequestFullscreenOnTouch(); // the reload throws the touch gesture away, like CONTINUE
        GameFlow.BeginCampaignOnLoad = true;
        GameFlow.ReloadFresh();
    }

    /// <summary>The CONTROLS button: the old rules screen as a reference sheet. Its button goes back to the title.</summary>
    private void ShowControls() => ShowRules(false);

    /// <summary>The part of starting that both a run and a shop resume need: hide the screens, hand the player back, undo Awake's zeroed clock.</summary>
    private void CloseMenus()
    {
        TouchInput.Poll();
        _musicFading = _music != null && _music.isPlaying;

        // F76 T8: a fullscreen request needs a user gesture on the web, and this tap is one. iPhone
        // Safari does not support it at all - ignore failure either way, nothing here depends on it.
        if (TouchInput.Active)
        {
            try { Screen.fullScreen = true; } catch { /* best effort only */ }
        }

        _menuOpen = false;
        _titleScreen.SetActive(false);
        _rulesScreen.SetActive(false);

        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);
        Time.timeScale = 1f;
    }

    private static string RulesText() =>
        L10n.T("ui.rules.item1") + "\n\n" + L10n.T("ui.rules.item2") + "\n\n" + L10n.T("ui.rules.item3") + "\n\n" + L10n.T("ui.rules.item4");

    /// <summary>F119: the keyboard / mouse or gamepad sheet - every key and button is the live binding (a rebind or a pad press shows here).</summary>
    private static string ControlsSheet() => L10n.T("ui.controls.sheet",
        ("move", Bold(GameAction.Move)), ("sprint", Bold(GameAction.Sprint)), ("look", Bold(GameAction.Look)), ("torch", Bold(GameAction.Torch)),
        ("focus", Bold(GameAction.Focus)), ("interact", Bold(GameAction.Interact)), ("throw", Bold(GameAction.Throw)), ("item1", Bold(GameAction.Item1)),
        ("item2", Bold(GameAction.Item2)), ("map", Bold(GameAction.Map)), ("pause", Bold(GameAction.Pause)));

    private static string Bold(GameAction a) => "<b>" + InputGlyphs.Key(a) + "</b>";

    /// <summary>F76 T6: shown instead of the keyboard/mouse control line while touch is active or the
    /// platform is mobile (iPadOS Safari under-reports as desktop, so isMobilePlatform is checked too).</summary>
    private static string ControlsText()
    {
        string tip = "<size=30>" + L10n.T("ui.controls.tip") + "</size>";
        if (!TouchInput.Active && !Application.isMobilePlatform) return ControlsSheet() + "\n" + tip;
        return L10n.T("ui.controls.touch") + "\n" + tip;
    }

    // ---------------------------------------------------------------- screens

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

    /// <summary>"v1.0.0": <c>Application.version</c> is PlayerSettings.bundleVersion in a build.</summary>
    public static string VersionLabel() => "v" + Application.version;

    private static bool _versionLogged;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetVersionLog() => _versionLogged = false;

    // ---------------------------------------------------------------- credits

    /// <summary>The CREDITS button: opens the scrolling credits panel over the title screen (the data is <see cref="CreditsList"/>).</summary>
    private void ShowCredits()
    {
        TouchInput.Poll();
        if (_creditsScreen == null) BuildCreditsScreen(_titleScreen.transform.parent);
        _creditsScreen.transform.SetAsLastSibling();
        _creditsScroll.verticalNormalizedPosition = 1f;
        _titleScreen.SetActive(false);
        _creditsScreen.SetActive(true);
    }

    private void CloseCredits()
    {
        _creditsScreen.SetActive(false);
        _titleScreen.SetActive(true);
    }

    /// <summary>The credits panel: heading, the "made by" line, a scrollable list built from <see cref="CreditsList"/>, BACK and the version.</summary>
    private void BuildCreditsScreen(Transform canvas)
    {
        _creditsScreen = RuntimeUi.CreatePanel(canvas, "CreditsScreen", new Color(0.02f, 0.02f, 0.03f, 0.97f));
        Transform root = _creditsScreen.transform;

        TextMeshProUGUI heading = RuntimeUi.CreateText(root, "Heading", L10n.T("ui.credits.title"), 76f, accent);
        RuntimeUi.Bind(heading, "ui.credits.title", 0.6f);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 10f;
        RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1400f, 100f));

        TextMeshProUGUI madeBy = RuntimeUi.CreateText(root, "MadeBy", gameTitle + "  -  " + CreditsList.DeveloperName, 34f, new Color(0.88f, 0.88f, 0.9f));
        RuntimeUi.AutoFit(madeBy, 0.6f);
        RuntimeUi.Place(madeBy.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(1400f, 50f));

        // Scroll view: a masked viewport (its transparent Image catches drags/taps for touch) over a vertical stack that sizes itself to its text.
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(root, false);
        viewport.layer = root.gameObject.layer;
        Image hit = viewport.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        viewport.AddComponent<RectMask2D>();
        RuntimeUi.Place(hit.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -45f), new Vector2(1400f, 690f));

        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        content.layer = root.gameObject.layer;
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup stack = content.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 10f;
        stack.padding = new RectOffset(30, 30, 10, 30);
        stack.childAlignment = TextAnchor.UpperCenter;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (CreditsList.Section section in CreditsList.Sections)
        {
            TextMeshProUGUI head = RuntimeUi.CreateText(content.transform, "Heading", L10n.T(section.HeadingKey), 42f, accent);
            RuntimeUi.Bind(head, section.HeadingKey);
            head.fontStyle = FontStyles.Bold;
            head.characterSpacing = 6f;
            head.margin = new Vector4(0f, 22f, 0f, 4f);

            foreach (string line in section.Lines)
            {
                TextMeshProUGUI entry = RuntimeUi.CreateText(content.transform, "Line", line, 30f, new Color(0.82f, 0.82f, 0.86f));
                entry.textWrappingMode = TextWrappingModes.Normal;
            }
        }

        TextMeshProUGUI version = RuntimeUi.CreateText(content.transform, "Version", VersionLabel(), 26f, new Color(0.6f, 0.6f, 0.64f, 0.8f));
        version.margin = new Vector4(0f, 30f, 0f, 0f);

        _creditsScroll = viewport.AddComponent<ScrollRect>();
        _creditsScroll.viewport = hit.rectTransform;
        _creditsScroll.content = contentRect;
        _creditsScroll.horizontal = false;
        _creditsScroll.vertical = true;
        _creditsScroll.movementType = ScrollRect.MovementType.Clamped;
        _creditsScroll.scrollSensitivity = 45f;

        Button back = RuntimeUi.CreateButton(root, L10n.T("ui.common.back"),
            new Vector2(0f, 40f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(back, "ui.common.back");
        back.onClick.AddListener(CloseCredits);
    }

    /// <summary>Escape / gamepad B / the touch pause button close the credits; arrow keys and the right stick scroll them.</summary>
    private void UpdateCredits()
    {
        // F119: MenuCancel = Esc / pad B / touch Pause; MenuScroll = up and down arrows / right stick Y.
        bool back = GameInput.Pressed(GameAction.MenuCancel);
        float scroll = GameInput.Axis(GameAction.MenuScroll);
        if (back) { CloseCredits(); return; }
        if (Mathf.Abs(scroll) > 0.05f)
        {
            // Unscaled: the title screen runs at timeScale 0. 900 px of content per second at full deflection.
            RectTransform content = _creditsScroll.content;
            float range = Mathf.Max(1f, content.rect.height - ((RectTransform)_creditsScroll.viewport).rect.height);
            _creditsScroll.verticalNormalizedPosition = Mathf.Clamp01(_creditsScroll.verticalNormalizedPosition + scroll * 900f * Time.unscaledDeltaTime / range);
        }
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

    private GameObject _confirmPanel;
    private Button _confirmBack;
    private int _savedFloor;

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

    private TextMeshProUGUI _confirmText;
    private System.Action _confirmAction;

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

    private void BuildRulesScreen(Transform canvas)
    {
        _rulesScreen = RuntimeUi.CreatePanel(canvas, "RulesScreen", new Color(0.02f, 0.02f, 0.03f, 0.97f));

        TextMeshProUGUI heading = RuntimeUi.CreateText(_rulesScreen.transform, "Heading", L10n.T("ui.rules.heading"), 76f, accent);
        RuntimeUi.Bind(heading, "ui.rules.heading", 0.6f);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 10f;
        RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1400f, 100f));

        string rules = RulesText();

        TextMeshProUGUI body = RuntimeUi.CreateText(_rulesScreen.transform, "Rules", rules, 36f, new Color(0.88f, 0.88f, 0.9f));
        _rulesBody = body;
        RuntimeUi.AutoFit(body, 0.7f);
        body.alignment = TextAlignmentOptions.Left;
        RuntimeUi.Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1100f, 380f));

        // Text is filled in by ShowRules() (touch-aware - see ControlsText), not here: TouchInput.Active
        // is not settled yet this early (Start runs before the first tap that could turn it on).
        _controlText = RuntimeUi.CreateText(_rulesScreen.transform, "Controls", "", 34f, new Color(0.62f, 0.62f, 0.66f));
        RuntimeUi.AutoFit(_controlText, 0.7f);
        RuntimeUi.Place(_controlText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(1400f, 170f));

        Button start = RuntimeUi.CreateButton(_rulesScreen.transform, L10n.T("ui.rules.start"),
            new Vector2(0f, 150f), new Vector2(420f, 90f), 44f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(start, "ui.rules.start");
        _rulesLabel = start.GetComponentInChildren<LocalizedLabel>();
        start.onClick.AddListener(() =>
        {
            if (_rulesStartsGame) StartGame();
            else
            {
                _rulesScreen.SetActive(false);
                _titleScreen.SetActive(true);
            }
        });
    }

    // ---------------------------------------------------------------- generated cover

    /// <summary>
    /// A cover built in code: the flashlight cone cutting up through fog, and two red eyes waiting
    /// above it. Stands in until a painted one is dropped into the coverImage slot.
    /// </summary>
    private Sprite GenerateCover()
    {
        int w = Mathf.Max(64, generatedCoverWidth);
        int h = Mathf.Max(64, generatedCoverHeight);
        float aspect = h / (float)w;

        _generatedCover = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "GeneratedCover" };
        Color[] pixels = new Color[w * h];

        Color fog = new Color(0.014f, 0.016f, 0.024f);
        Color beam = new Color(0.62f, 0.56f, 0.46f);

        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);

            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                Color c = fog;

                // Flashlight cone, apex just off the bottom edge, widening as it rises
                float alongCone = v + 0.05f;
                if (alongCone > 0f)
                {
                    float halfWidth = alongCone * 0.66f;
                    float across = 1f - Mathf.Clamp01(Mathf.Abs(u - 0.5f) / Mathf.Max(halfWidth, 0.0001f));
                    across *= across;
                    c += beam * (across * Mathf.Exp(-alongCone * 2.8f) * 1.15f);
                }

                c += EyeGlow(u, v, 0.466f, 0.585f, aspect);
                c += EyeGlow(u, v, 0.534f, 0.585f, aspect);

                // Vignette, so the title and buttons always have something dark to sit on
                float dx = (u - 0.5f) * 2f;
                float dy = (v - 0.5f) * 2f;
                float falloff = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * 0.72f);
                c *= Mathf.Lerp(0.3f, 1f, falloff);

                pixels[y * w + x] = new Color(c.r, c.g, c.b, 1f);
            }
        }

        _generatedCover.SetPixels(pixels);
        _generatedCover.Apply();

        _generatedSprite = Sprite.Create(_generatedCover, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f));
        return _generatedSprite;
    }

    private static Color EyeGlow(float u, float v, float centreX, float centreY, float aspect)
    {
        float dx = u - centreX;
        float dy = (v - centreY) * aspect;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);

        float halo = Mathf.Exp(-(distance / 0.055f) * (distance / 0.055f) * 2.2f) * 0.55f;
        float core = distance < 0.0075f ? 1f : 0f;

        float intensity = halo + core;
        return new Color(1f * intensity, 0.07f * intensity, 0.04f * intensity);
    }
}
