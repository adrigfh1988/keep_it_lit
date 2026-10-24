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
public partial class MainMenu : MonoBehaviour
{
    [Header("Identity")]
    [SerializeField] private string gameTitle = "KEEP IT LIT";
    // S9: the tagline now comes from ui.menu.tagline (the old serialized field is gone).

    [Header("Cover")]
    [Tooltip("Leave empty to use the cover generated in code. Drop a painted one in here when you have it.")]
    [SerializeField] private Sprite coverImage;
    [Tooltip("Kept modest on purpose: the cover is built pixel by pixel on the CPU, and it is a soft gradient that stretches without showing its resolution")]
    [SerializeField] private int generatedCoverWidth = 960;
    [Tooltip("Kept modest on purpose: the cover is built pixel by pixel on the CPU, and it is a soft gradient that stretches without showing its resolution")]
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
}
