using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// Esc pauses. Added at runtime by MazeGenerator; builds nothing until the first pause, so its panel
/// is created above every other piece of HUD.
/// </summary>
public class PauseMenu : MonoBehaviour
{
    private static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    private static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);

    private Transform _player;
    private Flashlight _flashlight;
    private MainMenu _menu;
    private GameOutcome _outcome;
    private ShopMenu _shopMenu;
    private FloorMapUi _map;
    private int _seed;

    private GameObject _panel;
    private Button _restartButton;
    private TextMeshProUGUI _descentWarning; // F107
    private Button _skipButton;
    private Button _mapButton;
    // F105 C6: the top-right panel listing what the hunter has learned (HunterMemory), refreshed on every pause.
    private RectTransform _lessonsPanel;
    private TextMeshProUGUI _lessonsBody;

    private const float LessonsWidth = 560f;
    private static readonly Color LessonsRed = new Color(0.85f, 0.12f, 0.08f);
    private bool _paused;
    /// <summary>Last frame the cursor was locked and a pause was allowed. See the lock-loss check in Update.</summary>
    private bool _wasLockedAndPausable;

    /// <summary>True while the pause panel is up.</summary>
    public bool IsPaused => _paused;

    /// <summary>Flashlight, menu, shopMenu and map may be null: no first-person rig, no title screen, no shop, or no floor map (F106).</summary>
    public void Configure(Transform player, Flashlight flashlight, MainMenu menu, GameOutcome outcome, int seed, ShopMenu shopMenu, FloorMapUi map = null)
    {
        _player = player;
        _flashlight = flashlight;
        _menu = menu;
        _outcome = outcome;
        _seed = seed;
        _shopMenu = shopMenu;
        _map = map;
    }

    private void Update()
    {
        if (_paused)
        {
            // Held every frame: focus changes re-lock the cursor
            PlayerLock.SetCursorFree(true);
        }

        // In a browser, Esc belongs to the page: it releases the pointer lock and usually never reaches
        // the game. So a lock lost while the game was pausable counts as a pause request. Only a
        // Locked -> unlocked edge counts, and only if pausing was allowed on the frame before too, so a
        // menu that frees the cursor itself (shop, endings) never trips it.
        bool locked = Cursor.lockState == CursorLockMode.Locked;
        bool canPause = !_paused && CanPause();
        // F76: touch mode never holds a pointer lock in the first place (see PlayerLock.SetCursorFree),
        // so the very first tap on a touch-capable laptop would otherwise read as a lost lock and pause.
        bool lockLost = _wasLockedAndPausable && !locked && canPause && !TouchInput.Active;
        _wasLockedAndPausable = locked && canPause;
        if (lockLost)
        {
            Pause();
            return;
        }

        bool pressed = false;
#if ENABLE_INPUT_SYSTEM
        // Read directly, like Flashlight: the Player action map has no Pause action. P is the
        // browser-safe key; Esc still works everywhere else.
        if (Keyboard.current != null && (Keyboard.current.escapeKey.wasPressedThisFrame || Keyboard.current.pKey.wasPressedThisFrame)) pressed = true;
        if (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame) pressed = true;
#endif
        if (TouchInput.Pressed(TouchInput.TouchAction.Pause)) pressed = true;
        if (!pressed) return;

        // S2: Esc / B / Start while the settings screen is up (or closed it this very frame) belong to the settings screen, not to us.
        if (SettingsMenu.BlocksPause) return;

        if (_paused) Resume();
        else if (CanPause()) Pause();
    }

    /// <summary>F76 T7: lets TouchControls pause for the "ROTATE YOUR DEVICE" overlay through the same
    /// single-sourced Pause() rather than duplicating its rules. Returns false (a no-op) if something
    /// else already has it paused, or CanPause() says no - TouchControls only calls ExternalResume if
    /// this returned true, so it never resumes a pause it did not itself cause.</summary>
    public bool ExternalPause()
    {
        if (_paused || !CanPause()) return false;
        Pause();
        return true;
    }

    /// <summary>Counterpart to ExternalPause. A no-op if not currently paused.</summary>
    public void ExternalResume()
    {
        if (_paused) Resume();
    }

    private bool CanPause()
    {
        // The shop is a third phase (decision 5): not a run, but pausing still works there.
        if (!GameFlow.IsRunActive && !GameFlow.IsInShop && !GameFlow.IsInIntake && !GameFlow.IsInHub) return false;
        if (AsylumHub.Active != null && AsylumHub.Active.IsTransitioning) return false; // F92: not mid arrival or mid walk through a wing door
        if (_menu != null && _menu.IsOpen) return false;
        if (GameOutcome.IsOver) return false;
        if (_outcome != null && _outcome.IsEnding) return false;
        // The Esc that closes the shop panel must not also open the pause menu in the same frame -
        // Update order between the two components is not defined.
        if (_shopMenu != null && (_shopMenu.IsOpen || _shopMenu.ClosedThisFrame)) return false;
        // F98: same for the hospital's dialogue panel - Esc closes the conversation first.
        if (DialoguePanel.BlocksPause) return false;
        // F106: the same for the floor map - Esc closes it first.
        if (_map != null && (_map.IsOpen || _map.ClosedThisFrame)) return false;
        return true;
    }

    private void Pause()
    {
        _paused = true;
        Time.timeScale = 0f;
        // Covers every source: the heartbeat one-shots, the drone and the hunter's hum
        AudioListener.pause = true;

        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;

        if (_panel == null) BuildPanel();
        // Replaying a cleared floor from the shop would re-earn its payout (decision 4c).
        if (_restartButton != null) _restartButton.gameObject.SetActive(!GameFlow.IsInShop && !GameFlow.IsInIntake && !GameFlow.IsInHub && !GameFlow.IsDescent); // F92: no replaying a floor from the hospital either; F107: nor a descent level (a new maze each time)
        if (_descentWarning != null) _descentWarning.gameObject.SetActive(GameFlow.IsDescent);
        // F86: only the Intake offers a way out of itself.
        if (_skipButton != null) _skipButton.gameObject.SetActive(GameFlow.IsInIntake);
        // F106: the map button is for touch players (no M key) and anyone who forgot it; only once the loop corridor has handed the map over.
        if (_mapButton != null) _mapButton.gameObject.SetActive(_map != null && _map.Available);
        RefreshLessons();
        _panel.SetActive(true);
        _panel.transform.SetAsLastSibling();

        PlayerLock.SetCursorFree(true);
    }

    private void Resume()
    {
        _paused = false;
        if (_panel != null) _panel.SetActive(false);

        Time.timeScale = 1f;
        AudioListener.pause = false;

        PlayerLock.Freeze(_player, false);
        // The shop is lit; the torch stays off and disabled there for the whole phase.
        // F86: in the Intake the torch is only usable once the tutorial has handed it over (IntakeRoom.TorchAllowed).
        if (_flashlight != null)
        {
            _flashlight.InputEnabled = !GameFlow.IsInShop && (!GameFlow.IsInIntake || (IntakeRoom.Active != null && IntakeRoom.Active.TorchAllowed));
        }
        PlayerLock.SetCursorFree(false);
    }

    /// <summary>F86: leave the Intake straight into floor 1, the same way finishing it does.</summary>
    private void SkipTutorial()
    {
        IntakeRoom room = IntakeRoom.Active;
        Resume();
        if (room != null) room.Skip();
    }

    /// <summary>F106: the MAP button - back to the game, then the floor map over it.</summary>
    private void OpenMap()
    {
        Resume();
        if (_map != null) _map.Open();
    }

    private void OnEnable() => L10n.Changed += HandleLanguage;

    private void OnDisable() => L10n.Changed -= HandleLanguage;

    // SETTINGS opens over the pause menu, so the language can change while the lessons panel is up.
    private void HandleLanguage()
    {
        if (_paused) RefreshLessons();
    }

    /// <summary>
    /// F105 C6: one entry per habit the hunter has learned - its name, the level bar and what it does differently now. Hidden while
    /// it has learned nothing (always, with TerrorSettings.learningHunter off: then no level ever rises).
    /// </summary>
    private void RefreshLessons()
    {
        if (_lessonsPanel == null) return;
        bool any = HunterMemory.HasLearnedAny;
        _lessonsPanel.gameObject.SetActive(any);
        if (!any) return;

        string red = ColorUtility.ToHtmlStringRGB(LessonsRed);
        System.Text.StringBuilder text = new System.Text.StringBuilder();
        foreach (HunterMemory.Habit habit in System.Enum.GetValues(typeof(HunterMemory.Habit)))
        {
            if (HunterMemory.Level(habit) < 1) continue;
            if (text.Length > 0) text.Append("\n<size=12> </size>\n");
            text.Append("<b>").Append(HunterMemory.NameText(habit)).Append("</b>  <color=#").Append(red).Append('>')
                .Append(HunterMemory.LevelBar(habit, _lessonsBody.font)).Append("</color>\n");
            text.Append("<size=21><color=#C9B58A>").Append(HunterMemory.EffectText(habit)).Append("</color></size>");
        }

        _lessonsBody.text = text.ToString();
        float inner = LessonsWidth - 54f;
        float bodyHeight = Mathf.Ceil(_lessonsBody.GetPreferredValues(_lessonsBody.text, inner, 0f).y);
        _lessonsBody.rectTransform.sizeDelta = new Vector2(inner, bodyHeight);
        _lessonsPanel.sizeDelta = new Vector2(LessonsWidth, 58f + bodyHeight + 20f);
    }

    private void BuildLessonsPanel()
    {
        GameObject panel = RuntimeUi.CreatePanel(_panel.transform, "HunterLessons", new Color(0.02f, 0.02f, 0.03f, 0.8f));
        panel.GetComponent<Image>().raycastTarget = false;
        _lessonsPanel = panel.GetComponent<RectTransform>();
        _lessonsPanel.anchorMin = _lessonsPanel.anchorMax = new Vector2(1f, 1f);
        _lessonsPanel.pivot = new Vector2(1f, 1f);
        _lessonsPanel.anchoredPosition = new Vector2(-40f, -40f);
        _lessonsPanel.sizeDelta = new Vector2(LessonsWidth, 200f);

        // The same red bar as the lesson card that announced them.
        GameObject bar = RuntimeUi.CreatePanel(panel.transform, "RedBar", LessonsRed);
        bar.GetComponent<Image>().raycastTarget = false;
        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(0f, 1f);
        barRect.pivot = new Vector2(0f, 0.5f);
        barRect.sizeDelta = new Vector2(6f, 0f);
        barRect.anchoredPosition = Vector2.zero;

        TextMeshProUGUI title = RuntimeUi.CreateText(panel.transform, "Title", L10n.T("ui.pause.lessons_title"), 20f, new Color(0.75f, 0.75f, 0.78f));
        RuntimeUi.Bind(title, "ui.pause.lessons_title", 0.6f);
        title.alignment = TextAlignmentOptions.TopLeft;
        title.characterSpacing = 6f;
        title.textWrappingMode = TextWrappingModes.NoWrap;
        PlaceTopLeft(title.rectTransform, new Vector2(30f, -18f), new Vector2(LessonsWidth - 54f, 28f));

        _lessonsBody = RuntimeUi.CreateText(panel.transform, "Body", "", 28f, Color.white);
        _lessonsBody.alignment = TextAlignmentOptions.TopLeft;
        _lessonsBody.characterSpacing = 2f;
        _lessonsBody.textWrappingMode = TextWrappingModes.Normal;
        _lessonsBody.raycastTarget = false;
        PlaceTopLeft(_lessonsBody.rectTransform, new Vector2(30f, -58f), new Vector2(LessonsWidth - 54f, 100f));
        panel.SetActive(false);
    }

    private static void PlaceTopLeft(RectTransform rect, Vector2 offset, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
    }

    private void OnDestroy()
    {
        if (!_paused) return;

        Time.timeScale = 1f;
        AudioListener.pause = false;
    }

    private void BuildPanel()
    {
        RuntimeUi.EnsureEventSystem();
        Canvas canvas = RuntimeUi.ResolveCanvas();

        _panel = RuntimeUi.CreatePanel(canvas.transform, "PauseScreen", new Color(0f, 0f, 0f, 0.75f));

        TextMeshProUGUI title = RuntimeUi.CreateText(_panel.transform, "Title", L10n.T("ui.pause.title"), 84f, Color.white);
        RuntimeUi.Bind(title, "ui.pause.title", 0.6f);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -220f), new Vector2(1200f, 120f));

        Vector2 size = new Vector2(420f, 86f);

        Button resume = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.pause.resume"),
            new Vector2(0f, 520f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        RuntimeUi.BindButton(resume, "ui.pause.resume");
        resume.onClick.AddListener(Resume);

        // F106: MAP closes the pause menu and opens the floor map (which never pauses time). Hidden until the map is unlocked (see Pause).
        _mapButton = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.pause.map"),
            new Vector2(0f, 630f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        RuntimeUi.BindButton(_mapButton, "ui.pause.map");
        _mapButton.onClick.AddListener(OpenMap);
        _mapButton.gameObject.SetActive(false);

        _restartButton = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.pause.restart"),
            new Vector2(0f, 410f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        RuntimeUi.BindButton(_restartButton, "ui.pause.restart");

        // F107: leaving a descent loses it (there is no save), so the pause menu says so.
        _descentWarning = RuntimeUi.CreateText(_panel.transform, "DescentWarning", L10n.T("ui.pause.descent_warning"), 30f, new Color(0.95f, 0.7f, 0.3f));
        RuntimeUi.Bind(_descentWarning, "ui.pause.descent_warning", 0.6f);
        _descentWarning.characterSpacing = 4f;
        RuntimeUi.Place(_descentWarning.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 740f), new Vector2(1200f, 44f));
        _descentWarning.gameObject.SetActive(false);
        _restartButton.onClick.AddListener(() => GameFlow.Restart(true, _seed));

        // F86: SKIP TUTORIAL shares the RESTART slot - the two are never shown together (see Pause).
        _skipButton = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.pause.skip_tutorial"),
            new Vector2(0f, 410f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        RuntimeUi.BindButton(_skipButton, "ui.pause.skip_tutorial");
        _skipButton.onClick.AddListener(SkipTutorial);
        _skipButton.gameObject.SetActive(false);

        // S2: SETTINGS (the touch-controls toggle and look-sensitivity slider that used to sit here live in its CONTROLS tab now)
        Button settings = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.menu.settings"),
            new Vector2(0f, 300f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        RuntimeUi.BindButton(settings, "ui.menu.settings");
        settings.onClick.AddListener(SettingsMenu.Show);

        Button menu = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.pause.main_menu"),
            new Vector2(0f, 190f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        RuntimeUi.BindButton(menu, "ui.pause.main_menu");
        menu.onClick.AddListener(GameFlow.ReturnToMenu);

        Button quit = RuntimeUi.CreateButton(_panel.transform, L10n.T("ui.pause.quit"),
            new Vector2(0f, 80f), size, 40f, ButtonIdle, ButtonHover, Color.white);
        RuntimeUi.BindButton(quit, "ui.pause.quit");
        quit.onClick.AddListener(GameFlow.Quit);
        quit.gameObject.SetActive(GameFlow.CanQuit);

        BuildLessonsPanel();
    }
}
