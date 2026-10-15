using System.Collections;
using System.Collections.Generic;
using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.UI;

public enum LoseReason
{
    Caught,
    OutOfTime
}

/// <summary>
/// Owns how a run ends: the capture sequence, silencing the world, and the end screen with its retry
/// buttons. Added at runtime by MazeGenerator. Single-shot - the first ending wins, and a hatch drop
/// or a timeout that lands mid-capture is ignored.
/// </summary>
public partial class GameOutcome : MonoBehaviour
{
    private static readonly Color Green = new Color(0.25f, 1f, 0.7f);
    private static readonly Color Red = new Color(0.9f, 0.15f, 0.1f);
    private static readonly Color Grey = new Color(0.75f, 0.75f, 0.78f);
    private static readonly Color ButtonIdle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
    private static readonly Color ButtonHover = new Color(0.48f, 0.08f, 0.06f, 1f);

    // Seconds into the capture sequence at which the torch flips. Ends off.
    private static readonly float[] FlickerTimes = { 0.5f, 0.56f, 0.62f, 0.68f, 0.74f, 0.80f, 0.9f };

    [Tooltip("How long the hunter stands stunned after Second Wind lets go (F36).")]
    [SerializeField] private float secondWindStunSeconds = 6f;

    /// <summary>True once the run has ended. Static, so reset when the scene is torn down.</summary>
    public static bool IsOver { get; private set; }

    /// <summary>True while the capture sequence is playing.</summary>
    public bool IsEnding { get; private set; }

    private Transform _player;
    private AIFollower _follower;
    private HorrorAudioDirector _audio;
    private MazeEscape _escape;
    private Flashlight _flashlight;
    private int _seed;
    // F106: where CaptureSequence eases the camera when something other than the hunter caught the player (the loop corridor's Visitor).
    private Vector3 _captureLookAt;
    private bool _hasCaptureLookAt;

    // F32/F37: the room between floors. May all stay null (escapeSequence off, or BindShop never called).
    private ShopRoom _shop;
    private MazeGenerator _maze;
    private PlayerHud _hud;
    private PlayerStealthState _stealth;
    private ContractTracker _contracts;
    private HabitTracker _habits;
    private bool _showingLedger;
    private bool _continuePressed;

    // Kept only so ShowEndScreen can report the number after the fact.
    private Payout _floorPayout;
    private Payout _consolationPayout;

    private int _collected;
    private int _total;
    private float _runTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        IsOver = false;
    }

    /// <summary>Follower, escape and flashlight may each be null; every use is guarded.</summary>
    public void Configure(Transform player, AIFollower follower, HorrorAudioDirector audio,
        MazeEscape escape, Flashlight flashlight, int seed)
    {
        _player = player;
        _audio = audio;
        _escape = escape;
        _flashlight = flashlight;
        _seed = seed;

        if (_follower != null) _follower.PlayerCaught -= HandleCaught;
        _follower = follower;
        if (_follower != null) _follower.PlayerCaught += HandleCaught;
    }

    /// <summary>Wired after escape and outcome.Configure, once the shop and stealth references exist.</summary>
    public void BindShop(ShopRoom shop, MazeGenerator maze, PlayerHud hud, PlayerStealthState stealth)
    {
        _shop = shop;
        _maze = maze;
        _hud = hud;
        _stealth = stealth;
    }

    /// <summary>F87: the floor's contract checker. May stay null; every use is guarded.</summary>
    public void BindContracts(ContractTracker contracts) => _contracts = contracts;

    /// <summary>F105 C6: the tracker FloorCleared asks to teach the hunter what this floor's play showed it.</summary>
    public void BindHabits(HabitTracker habits) => _habits = habits;

    private void Awake()
    {
        IsOver = false;
    }

    private void OnEnable()
    {
        GameManager.ProgressChanged += HandleProgress;
        if (_follower != null) _follower.PlayerCaught += HandleCaught;
    }

    private void OnDisable()
    {
        GameManager.ProgressChanged -= HandleProgress;
        if (_follower != null) _follower.PlayerCaught -= HandleCaught;
    }

    private void OnDestroy()
    {
        IsOver = false;
    }

    private void Update()
    {
        // Held every frame, for the same reason as the title screen: focus changes re-lock the cursor
        if (IsOver || _showingLedger) PlayerLock.SetCursorFree(true);
        PollLedgerInput();
    }

    private void HandleProgress(int collected, int total)
    {
        _collected = collected;
        _total = total;
    }

    private void HandleCaught()
    {
        if (IsOver || IsEnding) return;

        // F36 Second Wind: a release, not an invulnerability. Consumed automatically, once.
        if (PlayerInventory.Count(ShopItem.SecondWind) > 0 && _follower != null && _maze != null)
        {
            PlayerInventory.TryConsume(ShopItem.SecondWind);
            StartCoroutine(SecondWindSequence(ReleaseHunter, "sub.second_wind.release"));
            return;
        }

        DescentRun.Cause = DescentCause.Hunter; // F107
        Lose(LoseReason.Caught);
    }

    /// <summary>
    /// F106: the loop corridor's Visitor has the player. The same two outcomes as the hunter's catch, through the same single-shot guards: with a
    /// Second Wind it is consumed and the player is let go under a blackout (`onSecondWind` runs at full black and moves them out; the hunter is
    /// not touched); otherwise it is a real death, the camera easing onto `lookAt` (the Visitor's head) instead of the hunter's eyes.
    /// </summary>
    public void CaughtByLoopVisitor(Vector3 lookAt, System.Action onSecondWind)
    {
        CaughtBy(lookAt, onSecondWind, DescentCause.Visitor, EntityKind.None, "sub.loop.second_wind");
    }

    /// <summary>
    /// F107 D2: a maze entity that kills (the Listener) has the player. The same two outcomes as the hunter's catch, through the same single-shot
    /// guards: a held Second Wind is consumed and `onSecondWind` runs at full black (the entity warps itself away); otherwise a real death with
    /// the camera easing onto `lookAt`, and the descent end screen names `kind` as the cause.
    /// </summary>
    public void CaughtByEntity(Vector3 lookAt, System.Action onSecondWind, EntityKind kind)
    {
        CaughtBy(lookAt, onSecondWind, DescentCause.Entity, kind, "sub.loop.second_wind");
    }

    /// <summary>The shared body of <see cref="CaughtByLoopVisitor"/> and <see cref="CaughtByEntity"/>.</summary>
    private void CaughtBy(Vector3 lookAt, System.Action onSecondWind, DescentCause cause, EntityKind kind, string secondWindLineKey)
    {
        if (IsOver || IsEnding) return;

        if (PlayerInventory.TryConsume(ShopItem.SecondWind))
        {
            StartCoroutine(SecondWindSequence(onSecondWind, secondWindLineKey));
            return;
        }

        _captureLookAt = lookAt;
        _hasCaptureLookAt = true;
        DescentRun.Cause = cause; // F107: read only by the descent end screen
        DescentRun.CauseEntity = kind;
        Lose(LoseReason.Caught);
    }

    /// <summary>The hunter's half of a Second Wind: warped to the far side of the maze and stunned. If it busted a locker, the player is still standing inside it with movement locked.</summary>
    private void ReleaseHunter()
    {
        _follower.Release(_maze.FarthestCellCenterFrom(_player.position), secondWindStunSeconds);

        // Locker.Leave() clears MovementLocked directly, so re-assert the freeze straight after -
        // the sequence is not done with the player yet.
        foreach (Locker locker in _maze.Lockers)
        {
            if (locker != null && locker.Occupied) locker.ForceLeave();
        }

        PlayerLock.Freeze(_player, true);
    }

    /// <summary>`letGo` runs at full black (the hunter's warp, or the loop's throw-out); `releaseLineKey` is the subtitle when the screen comes back.</summary>
    private IEnumerator SecondWindSequence(System.Action letGo, string releaseLineKey)
    {
        IsEnding = true; // pause, dread, phantoms, and Lose() all wait
        if (_escape != null) _escape.Frozen = true; // not Stop(): Stop is permanent
        bool torchWasOn = _flashlight != null && _flashlight.IsOn;

        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;
        if (_audio != null) _audio.PlayCaptureSting();

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = canvas != null
            ? RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>()
            : null;
        if (blackout != null) blackout.transform.SetAsLastSibling();

        // A hard cut, not the slow capture fade.
        if (blackout != null) yield return Fade(blackout, 0f, 1f, 0.25f);
        else yield return new WaitForSeconds(0.25f);

        yield return new WaitForSeconds(0.7f);

        letGo?.Invoke();

        if (_stealth != null) _stealth.LastExposed = false;
        if (_audio != null) _audio.ResumeBed();
        if (_flashlight != null)
        {
            _flashlight.SetOn(torchWasOn);
            _flashlight.Stutter(1.5f);
        }

        if (blackout != null)
        {
            yield return Fade(blackout, 1f, 0f, 0.6f);
            Destroy(blackout.gameObject);
        }
        else
        {
            yield return new WaitForSeconds(0.6f);
        }

        if (_escape != null) _escape.Frozen = false;
        PlayerLock.Freeze(_player, false);
        if (_flashlight != null) _flashlight.InputEnabled = true;
        IsEnding = false;

        if (_hud != null) _hud.ShowSubtitle(L10n.T(releaseLineKey), 3.2f);
    }

    // ---------------------------------------------------------------- endings

    /// <summary>The hatch was reached. Below the final floor this is not an ending: black, teleport, ledger, shop.</summary>
    public void FloorCleared()
    {
        if (IsOver || IsEnding) return;

        // F105 C6: the floor was cleared, so what the player leaned on this floor is learned (before the shop checkpoint / the finale).
        if (_habits != null) _habits.CommitLearning();
        LoopCorridor.CommitFloor(); // F105 C4: a loop on this floor counts as the campaign's one

        // F107 B2: a descent never wins, never visits the shop or the finale and never saves: always the hatch drop, a typed slip, the next level.
        if (GameFlow.IsDescent)
        {
            StartCoroutine(DescentClearSequence());
            return;
        }

        if (GameFlow.CurrentFloor >= FloorProfile.FinalFloor || (_shop == null && !AsylumHub.ShopInHub))
        {
            // F93 (E10): the last floor ends in the Director's office when the asylum can host it; anything missing = the old plain win.
            if (GameFlow.CurrentFloor >= FloorProfile.FinalFloor && AsylumHub.Active != null && AsylumHub.Active.CanFinale)
            {
                StartCoroutine(FinaleSequence());
                return;
            }
            Win();
            return;
        }

        StartCoroutine(FloorClearSequence());
    }

    private IEnumerator FloorClearSequence()
    {
        IsEnding = true; // blocks pause, dread, phantoms, and a second ending
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        float secondsLeft = _escape != null ? _escape.TimeLeft : 0f;

        EndTheHunt();
        if (_flashlight != null) { _flashlight.SetOn(false); _flashlight.InputEnabled = false; }
        PlayerLock.Freeze(_player, true);

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = null;
        if (canvas != null)
        {
            blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>();
            blackout.transform.SetAsLastSibling();
        }

        if (blackout != null) yield return Fade(blackout, 0f, 1f, 0.6f);
        else yield return new WaitForSeconds(0.6f); // the player is falling onto the platform meanwhile

        // F94: when the asylum can host the shop, the player goes to the hub instead (the Commissary is there); the ledger is over black.
        bool viaHub = AsylumHub.ShopInHub;
        if (!viaHub) _shop.ArrivePlayer(_player);

        Payout payout = PlayerWallet.ComputeFloorClear(GameFlow.CurrentFloor, _collected, secondsLeft);
        // F87: the taken contract is decided and paid in this same payout; then the shop's board gets its two offers for the next floor
        // (rolled before the checkpoint below, so a shop resume shows the same pair).
        if (_contracts != null) _contracts.AppendTo(payout);
        ContractState.RollOffers(_seed, GameFlow.CurrentFloor);
        PlayerWallet.Deposit(payout);
        // F83: shop-arrival checkpoint, with the payout banked. Resumes straight into the shop.
        // F94: not via the hub - NextFloor writes the floor-start checkpoint with the deposited wallet.
        if (!viaHub) SaveSystem.WriteCheckpoint(GameFlow.CurrentFloor, true);

        Transform ledger = blackout != null ? BuildLedger(blackout.transform, payout, viaHub) : null;
        _slipLedger = viaHub && ledger != null;
        _typing = _slipLedger; // set before the first frame of the coroutine so an early press cannot continue past the typing
        if (_slipLedger) StartCoroutine(TypeLedger());
        _showingLedger = true; // Update frees the cursor while this is true
        PlayerLock.SetCursorFree(true);

        _continuePressed = false;
        yield return new WaitUntil(() => _continuePressed);
        _showingLedger = false;
        _slipLedger = false;

        if (viaHub)
        {
            // The screen stays black; the reload lands in the hub at the cleared wing's arrival point (GameFlow.ArriveInHub).
            IsEnding = false;
            GameFlow.NextFloor();
            yield break;
        }

        GameFlow.IsInShop = true;
        IsEnding = false;
        _shop.Open();
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);

        if (blackout != null)
        {
            yield return Fade(blackout, 1f, 0f, 0.5f, ledger);
            Destroy(blackout.gameObject);
        }
    }

    /// <summary>
    /// F83: CONTINUE on a save taken in the shop. The tail of FloorClearSequence without the ledger or the
    /// payout (already banked in the save): silence the hunt, torch off, teleport in, open the room, fade up.
    /// Called by MainMenu.Start with GameFlow.IsRunActive still false. No shop in the scene = start the next floor.
    /// </summary>
    public void EnterShopDirectly()
    {
        if (_shop == null)
        {
            GameFlow.NextFloor();
            return;
        }

        StartCoroutine(EnterShopRoutine());
    }

    private IEnumerator EnterShopRoutine()
    {
        IsEnding = true;
        // The maze behind the shop was rebuilt from a fresh seed and just re-rolled this floor's pair; put the played one back.
        SaveSystem.ReapplyRolledObjectives();

        EndTheHunt();
        if (_flashlight != null) { _flashlight.SetOn(false); _flashlight.InputEnabled = false; }
        PlayerLock.Freeze(_player, true);

        // Created before the first yield, so the half-built maze is never seen.
        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = null;
        if (canvas != null)
        {
            blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", Color.black).GetComponent<Image>();
            blackout.transform.SetAsLastSibling();
        }

        _shop.ArrivePlayer(_player);
        GameFlow.IsInShop = true;
        IsEnding = false;
        _shop.Open();
        PlayerLock.Freeze(_player, false);
        PlayerLock.SetCursorFree(false);

        if (blackout != null)
        {
            yield return Fade(blackout, 1f, 0f, 0.5f);
            Destroy(blackout.gameObject);
        }
    }

    /// <summary>The floor-clear ledger, built onto the blackout panel. CONTINUE sets _continuePressed.</summary>
    /// <param name="slip">F100: the readmission slip (hub flow, floors 1-4): a heading, a slip-voice subtitle, the lines typing out and two typed orders.</param>
    private Transform BuildLedger(Transform parent, Payout payout, bool slip)
    {
        // No prior pause/menu may have built one yet (mainMenu = false skips the title screen's call).
        RuntimeUi.EnsureEventSystem();
        _ledgerTexts.Clear();

        GameObject ledger = RuntimeUi.CreatePanel(parent, "Ledger", Color.clear);
        Transform root = ledger.transform;
        int cleared = GameFlow.CurrentFloor;

        TextMeshProUGUI title = RuntimeUi.CreateText(root, "Title", payout.Title, 84f, Green);
        RuntimeUi.AutoFit(title, 0.6f);
        title.fontStyle = FontStyles.Bold;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, slip ? -150f : -200f), new Vector2(1600f, 120f));

        if (slip)
        {
            // The number stays redacted until the Director's file (105) has been read. U+2587 may be missing from the font: '#' then.
            string redacted = title.font != null && title.font.HasCharacter('▇', true, true) ? "▇" : "#";
            string patient = HubState.HasTruth ? "7" : redacted;
            TextMeshProUGUI heading = RuntimeUi.CreateText(root, "Heading", L10n.T("ui.ledger.slip_heading", ("admission", 14 + cleared), ("patient", patient)), 30f, Amber);
            RuntimeUi.AutoFit(heading, 0.7f);
            heading.characterSpacing = 4f;
            RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -232f), new Vector2(1400f, 44f));
            _ledgerTexts.Add(heading);
        }

        TextMeshProUGUI subtitle = RuntimeUi.CreateText(root, "Subtitle",
            slip ? L10n.T("ui.ledger.slip_subtitle") : L10n.T("ui.ledger.subtitle"), 34f, Grey);
        RuntimeUi.AutoFit(subtitle, 0.7f);
        RuntimeUi.Place(subtitle.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, slip ? -285f : -280f), new Vector2(1400f, 50f));
        if (slip) _ledgerTexts.Add(subtitle);

        float y = slip ? -355f : -370f;
        float step = slip && payout.Lines.Count > 4 ? 46f : slip ? 52f : 58f;
        foreach (PayoutLine line in payout.Lines)
        {
            bool informational = line.Amount == 0 && line.Kind == PayoutKind.ShardsFound;
            Color lineColor = informational ? new Color(Grey.r, Grey.g, Grey.b, 0.6f) : Grey;

            TextMeshProUGUI label = RuntimeUi.CreateText(root, "Label", line.Label, 36f, lineColor);
            RuntimeUi.AutoFit(label, 0.7f);
            label.alignment = TextAlignmentOptions.Left;
            RuntimeUi.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(-100f, y), new Vector2(900f, 50f));

            string amountText = informational ? L10n.T("ui.ledger.already_yours") : line.Amount == 0 && line.Kind == PayoutKind.Contract ? L10n.T("ui.ledger.no_pay") : $"+{line.Amount}";
            TextMeshProUGUI amount = RuntimeUi.CreateText(root, "Amount", amountText, 36f, lineColor);
            amount.alignment = TextAlignmentOptions.Right;
            RuntimeUi.Place(amount.rectTransform, new Vector2(0.5f, 1f), new Vector2(420f, y), new Vector2(300f, 50f));
            if (slip) { _ledgerTexts.Add(label); _ledgerTexts.Add(amount); }

            y -= step;
        }

        GameObject rule = RuntimeUi.CreatePanel(root, "Rule", new Color(Grey.r, Grey.g, Grey.b, 0.4f));
        RuntimeUi.Place(rule.GetComponent<Image>().rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y - 6f), new Vector2(900f, 2f));
        y -= 50f;

        TextMeshProUGUI payoutText = RuntimeUi.CreateText(root, "Payout", L10n.T("ui.ledger.payout", ("amount", payout.Total)), 44f, Green);
        RuntimeUi.Place(payoutText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(900f, 60f));
        y -= 60f;

        Color shardColor = new Color(0.7f, 0.9f, 1f);
        TextMeshProUGUI wallet = RuntimeUi.CreateText(root, "Wallet", L10n.T("ui.ledger.wallet", ("currency", PlayerWallet.CurrencyName), ("shards", PlayerWallet.Shards)), 44f, shardColor);
        RuntimeUi.Place(wallet.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(900f, 60f));

        if (slip)
        {
            _ledgerTexts.Add(payoutText);
            _ledgerTexts.Add(wallet);
            // Typed orders for the floor about to be played: who to report to (or the missing key), and the shop when it is worth the walk.
            y -= 66f;
            List<string> orders = new List<string>();
            string order = HubMission.OrderFor(cleared + 1);
            if (!string.IsNullOrEmpty(order)) orders.Add(order);
            if (AsylumHub.ShopWorthAVisit(cleared)) orders.Add(L10n.T("ui.ledger.shop_held", ("shards", PlayerWallet.Shards), ("currency", PlayerWallet.CurrencyName)));
            foreach (string text in orders)
            {
                TextMeshProUGUI o = RuntimeUi.CreateText(root, "Order", text, 34f, Amber);
                RuntimeUi.AutoFit(o, 0.7f);
                o.characterSpacing = 3f;
                RuntimeUi.Place(o.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(1500f, 46f));
                _ledgerTexts.Add(o);
                y -= 48f;
            }
        }

        Button continueButton = RuntimeUi.CreateButton(root, L10n.T("ui.common.continue"), new Vector2(0f, slip ? 100f : 160f), new Vector2(560f, 86f), 42f,
            new Color(0.08f, 0.32f, 0.24f, 0.95f), new Color(0.15f, 0.55f, 0.42f, 1f), Color.white);
        continueButton.onClick.AddListener(OnLedgerContinue);
        RuntimeUi.BindButton(continueButton, "ui.common.continue");

        return root;
    }

    // ---------------------------------------------------------------- the slip types out (F100)

    private static readonly Color Amber = new Color(1f, 0.78f, 0.45f);
    private readonly List<TextMeshProUGUI> _ledgerTexts = new List<TextMeshProUGUI>();
    private bool _slipLedger;
    private bool _typing;
    private bool _skipTyping;
    private AudioSource _clickSource;
    private AudioClip _clickClip;

    /// <summary>CONTINUE / E / tap: while the slip is still typing the first press completes it; the next one continues.</summary>
    private void OnLedgerContinue()
    {
        if (_typing) _skipTyping = true;
        else _continuePressed = true;
    }

    private void PollLedgerInput()
    {
        if (!_slipLedger || !_showingLedger) return;
        bool press = TouchInput.Pressed(TouchInput.TouchAction.Interact);
        if (Keyboard.current != null && (Keyboard.current.eKey.wasPressedThisFrame || Keyboard.current.enterKey.wasPressedThisFrame || Keyboard.current.spaceKey.wasPressedThisFrame)) press = true;
        if (Gamepad.current != null && Gamepad.current.buttonSouth.wasPressedThisFrame) press = true;
        if (press) { OnLedgerContinue(); return; }
        // A click or a tap anywhere finishes the typing (the CONTINUE button itself acts on release, so it never double-fires with this).
        bool tap = (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame) ||
                   (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame);
        if (tap && _typing) _skipTyping = true;
    }

    private IEnumerator TypeLedger()
    {
        _typing = true;
        _skipTyping = false;
        foreach (TextMeshProUGUI t in _ledgerTexts) { if (t != null) t.maxVisibleCharacters = 0; }
        const float charsPerSecond = 110f;
        foreach (TextMeshProUGUI t in _ledgerTexts)
        {
            if (t == null) continue;
            t.ForceMeshUpdate();
            int count = t.textInfo.characterCount;
            float shown = 0f;
            int clicked = 0;
            while (!_skipTyping && shown < count)
            {
                shown += charsPerSecond * Time.unscaledDeltaTime;
                int visible = Mathf.Min(count, (int)shown);
                t.maxVisibleCharacters = visible;
                if (visible / 3 > clicked) { clicked = visible / 3; Click(); }
                yield return null;
            }
            t.maxVisibleCharacters = 99999;
        }
        foreach (TextMeshProUGUI t in _ledgerTexts) { if (t != null) t.maxVisibleCharacters = 99999; }
        _typing = false;
    }

    /// <summary>One key tap of the slip: the library's typewriter slot, else a short generated click.</summary>
    private void Click()
    {
        if (_clickSource == null)
        {
            _clickSource = gameObject.AddComponent<AudioSource>();
            _clickSource.playOnAwake = false;
            _clickSource.spatialBlend = 0f;
            GameSettings.RouteEffect(_clickSource);
        }
        _clickSource.pitch = 1f + Random.Range(-0.12f, 0.12f);
        AudioSlot slot = GameAudio.Library.typewriter;
        if (slot.Has)
        {
            _clickSource.PlayOneShot(slot.Pick(), 0.35f * slot.volume);
            return;
        }
        if (_clickClip == null) _clickClip = DoorAudio.BuildClunkClip(0.03f, 1500f, 0.12f);
        _clickSource.PlayOneShot(_clickClip, 0.25f);
    }

    /// <summary>Fades a full-screen Image between two alpha values. If alsoFade is given, its CanvasGroup fades out alongside it.</summary>
    private static IEnumerator Fade(Image image, float from, float to, float seconds, Transform alsoFade = null)
    {
        CanvasGroup group = null;
        if (alsoFade != null)
        {
            group = alsoFade.GetComponent<CanvasGroup>();
            if (group == null) group = alsoFade.gameObject.AddComponent<CanvasGroup>();
        }

        float t = 0f;
        while (t < seconds)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / seconds);
            if (image != null) image.color = new Color(0f, 0f, 0f, Mathf.Lerp(from, to, k));
            if (group != null) group.alpha = 1f - k;
            yield return null;
        }

        if (image != null) image.color = new Color(0f, 0f, 0f, to);
        if (group != null) group.alpha = 0f;
    }

    /// <summary>The two ways out of the Director's office (F93).</summary>
    public enum FinaleEnding { Discharged, LightsOut }

    /// <summary>
    /// F93 (E10): floor 5 was cleared and the asylum can host the finale. Everything Win() does up to the end screen - the payout is read and
    /// banked now (time left included), the hunt silenced - but the run stays "ending" (pause, dread and a second ending are held off) and
    /// the player goes back to the Director's desk instead. The save is NOT deleted here; <see cref="CompleteFinale"/> does it when an
    /// ending actually completes.
    /// </summary>
    private IEnumerator FinaleSequence()
    {
        IsEnding = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        float secondsLeft = _escape != null ? _escape.TimeLeft : 0f;
        _floorPayout = PlayerWallet.ComputeFloorClear(GameFlow.CurrentFloor, _collected, secondsLeft);
        if (_contracts != null) _contracts.AppendTo(_floorPayout);
        PlayerWallet.Deposit(_floorPayout);

        EndTheHunt();
        if (_escape != null) _escape.Stop();
        if (_flashlight != null) { _flashlight.SetOn(false); _flashlight.InputEnabled = false; }
        PlayerLock.Freeze(_player, true);

        Canvas canvas = RuntimeUi.ResolveCanvas();
        Image blackout = null;
        if (canvas != null)
        {
            blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>();
            blackout.transform.SetAsLastSibling();
            yield return Fade(blackout, 0f, 1f, 0.6f);
        }
        else yield return new WaitForSeconds(0.6f);

        AsylumHub hub = AsylumHub.Active;
        if (hub != null && hub.CanFinale) hub.EnterFinale(); // makes its own black panel before its first yield
        else
        {
            // The hub vanished between the check and now: fall back to the plain win, never strand the player.
            if (blackout != null) Destroy(blackout.gameObject);
            IsEnding = false;
            Win();
            yield break;
        }
        if (blackout != null) Destroy(blackout.gameObject);
    }

    /// <summary>One of the two endings was reached in the Director's office. Single-shot; deletes the save and shows the ending over the campaign total.</summary>
    public void CompleteFinale(FinaleEnding ending)
    {
        if (IsOver) return;
        IsOver = true;
        IsEnding = false;
        SaveSystem.Delete();
        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;
        bool a = ending == FinaleEnding.Discharged;
        string endKey = a ? "lore.ending.discharged" : "lore.ending.lights_out"; // S9: the two ending texts live in the Lore table
        ShowEndScreen(true, LoseReason.Caught, false, L10n.T(endKey + ".title"), L10n.T(endKey + ".body"));
    }

    public void Win()
    {
        if (IsOver || IsEnding) return;

        // F107: a descent has no ending to win; anything that reaches here is a floor clear.
        if (GameFlow.IsDescent)
        {
            FloorCleared();
            return;
        }

        IsOver = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;

        float secondsLeft = _escape != null ? _escape.TimeLeft : 0f;
        _floorPayout = PlayerWallet.ComputeFloorClear(GameFlow.CurrentFloor, _collected, secondsLeft);
        if (_contracts != null) _contracts.AppendTo(_floorPayout); // F87: floor 5 pays its contract on the win screen
        PlayerWallet.Deposit(_floorPayout);
        // F83: the campaign is over. Guarded on the final floor so the no-shop fallback (a plain Win on floors 1-4) keeps its save.
        if (GameFlow.CurrentFloor >= FloorProfile.FinalFloor) SaveSystem.Delete();

        EndTheHunt();
        if (_escape != null) _escape.Stop();
        if (_flashlight != null) _flashlight.InputEnabled = false;
        PlayerLock.Freeze(_player, true);

        ShowEndScreen(true, LoseReason.Caught, false);
    }

    public void Lose(LoseReason reason)
    {
        if (IsOver || IsEnding) return;

        if (reason == LoseReason.Caught)
        {
            StartCoroutine(CaptureSequence());
            return;
        }

        IsOver = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        if (GameFlow.IsDescent) DescentRun.Cause = DescentCause.Timer; // F107

        BankConsolation();

        EndTheHunt();
        if (_escape != null) _escape.Stop();
        if (_flashlight != null) _flashlight.InputEnabled = false;
        PlayerLock.Freeze(_player, true);

        ShowEndScreen(false, reason, _flashlight != null && _flashlight.IsOn);
    }

    private IEnumerator CaptureSequence()
    {
        IsEnding = true;
        GameFlow.IsRunActive = false;
        _runTime = Time.time - GameFlow.RunStartTime;
        bool torchWasOn = _flashlight != null && _flashlight.IsOn;

        // The countdown freezes where it is, so it cannot produce a second ending mid-sequence
        if (_escape != null) _escape.Stop();
        PlayerLock.Freeze(_player, true);
        if (_flashlight != null) _flashlight.InputEnabled = false;
        if (_audio != null) _audio.PlayCaptureSting();

        ThirdPersonController controller = _player != null ? _player.GetComponent<ThirdPersonController>() : null;
        Image blackout = null;
        int flicker = 0;
        float elapsed = 0f;

        while (elapsed < 1.4f)
        {
            elapsed += Time.deltaTime;

            // Ease onto the hunter's eyes (or, for the loop's Visitor, its head); lands in about half a second
            if (controller != null && (_hasCaptureLookAt || _follower != null))
            {
                Vector3 target = _hasCaptureLookAt ? _captureLookAt : _follower.transform.position + Vector3.up * _follower.EyeHeight;
                controller.LookAt(target, 1f - Mathf.Pow(0.001f, Time.deltaTime));
            }

            while (flicker < FlickerTimes.Length && elapsed >= FlickerTimes[flicker])
            {
                // Even entries switch it off, odd ones back on; the last (index 6) leaves it off
                if (_flashlight != null) _flashlight.SetOn(flicker % 2 == 1);
                flicker++;
            }

            if (elapsed >= 0.9f)
            {
                if (blackout == null)
                {
                    // Created now rather than earlier so it is the topmost sibling
                    Canvas canvas = RuntimeUi.ResolveCanvas();
                    if (canvas != null)
                    {
                        blackout = RuntimeUi.CreatePanel(canvas.transform, "Blackout", new Color(0f, 0f, 0f, 0f)).GetComponent<Image>();
                    }
                }

                if (blackout != null)
                {
                    blackout.color = new Color(0f, 0f, 0f, Mathf.Clamp01((elapsed - 0.9f) / 0.5f));
                }
            }

            yield return null;
        }

        if (blackout != null) blackout.color = Color.black;

        EndTheHunt();
        IsOver = true;
        IsEnding = false;

        BankConsolation();

        yield return new WaitForSeconds(0.2f);

        ShowEndScreen(false, LoseReason.Caught, torchWasOn);
    }

    /// <summary>
    /// Once the run is over the hunter stops mattering, so silence it. Without this the heartbeat keeps
    /// pounding over the end screen and the hum keeps looping.
    /// </summary>
    private void EndTheHunt()
    {
        foreach (HorrorAudioDirector director in FindObjectsByType<HorrorAudioDirector>(FindObjectsInactive.Exclude))
        {
            // Silence first: disabling the component stops its Update but leaves its AudioSources running
            director.Silence();
            // Disabling then runs its OnDisable, which unsubscribes it from the chase event
            director.enabled = false;
        }

        foreach (AIPresence presence in FindObjectsByType<AIPresence>(FindObjectsInactive.Exclude))
        {
            presence.Silence();
        }

        foreach (AIFollower follower in FindObjectsByType<AIFollower>(FindObjectsInactive.Exclude))
        {
            follower.enabled = false;

            NavMeshAgent agent = follower.GetComponent<NavMeshAgent>();
            if (agent != null && agent.isOnNavMesh) agent.isStopped = true;
        }
    }

    // ---------------------------------------------------------------- end screen

    private void ShowEndScreen(bool won, LoseReason reason, bool torchWasOn, string titleOverride = null, string bodyOverride = null)
    {
        RuntimeUi.EnsureEventSystem();

        // F107 B2: a descent has its own end screen (the readmission slip); no shards are paid, no maze is retried.
        if (GameFlow.IsDescent && !won)
        {
            ShowDescentEndScreen();
            return;
        }

        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        Transform root = RuntimeUi.CreatePanel(canvas.transform, "EndScreen", new Color(0f, 0f, 0f, 0.85f)).transform;

        string title;
        string subtitle;
        Color titleColor = won ? Green : Red;

        if (won)
        {
            bool finalFloor = GameFlow.CurrentFloor >= FloorProfile.FinalFloor;
            title = finalFloor ? L10n.T("ui.end.won_final") : L10n.T("ui.end.floor_cleared", ("floor", GameFlow.CurrentFloor));
            subtitle = finalFloor ? L10n.T("ui.end.won_final_body") : L10n.T("ui.end.floor_cleared_body");
            if (titleOverride != null) title = titleOverride;
            if (bodyOverride != null) subtitle = bodyOverride;
        }
        else if (reason == LoseReason.Caught)
        {
            title = L10n.T("ui.end.caught");

            PlayerStealthState stealth = _player != null ? _player.GetComponent<PlayerStealthState>() : null;
            if (stealth != null && stealth.LastExposed)
            {
                subtitle = L10n.T("ui.end.caught_hidden");
            }
            else
            {
                subtitle = torchWasOn ? L10n.T("ui.end.caught_torch") : L10n.T("ui.end.caught_silent");
            }
        }
        else
        {
            title = L10n.T("ui.end.out_of_time");
            int metres = 0;
            if (_escape != null && _player != null)
            {
                Vector3 toHatch = _escape.HatchPosition - _player.position;
                toHatch.y = 0f;
                metres = Mathf.RoundToInt(toHatch.magnitude);
            }

            subtitle = L10n.T("ui.end.out_of_time_body", ("stars", _collected), ("metres", metres));
        }

        TextMeshProUGUI titleText = RuntimeUi.CreateText(root, "Title", title, 96f, titleColor);
        titleText.fontStyle = FontStyles.Bold;
        RuntimeUi.AutoFit(titleText, 0.6f);
        RuntimeUi.Place(titleText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -200f), new Vector2(1600f, 140f));

        bool longBody = bodyOverride != null;
        TextMeshProUGUI subText = RuntimeUi.CreateText(root, "Subtitle", subtitle, longBody ? 32f : 36f, Grey);
        if (longBody) subText.textWrappingMode = TextWrappingModes.Normal;
        RuntimeUi.AutoFit(subText, 0.7f);
        RuntimeUi.Place(subText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, longBody ? -385f : -300f), new Vector2(longBody ? 1300f : 1600f, longBody ? 200f : 60f));

        string stats = L10n.T("ui.end.stats", ("floor", GameFlow.CurrentFloor), ("collected", _collected), ("total", _total), ("time", MazeEscape.FormatTime(_runTime)), ("seed", _seed));
        TextMeshProUGUI statsText = RuntimeUi.CreateText(root, "Stats", stats, 32f, Grey);
        statsText.characterSpacing = 4f;
        RuntimeUi.AutoFit(statsText, 0.65f);
        RuntimeUi.Place(statsText.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, longBody ? -540f : -380f), new Vector2(1600f, 50f));

        // Second line: what this attempt paid into the campaign wallet.
        bool isFinalWin = won && GameFlow.CurrentFloor >= FloorProfile.FinalFloor;
        string stats2 = null;
        Color shardColor = new Color(0.7f, 0.9f, 1f);
        if (!won)
        {
            int amount = _consolationPayout != null ? _consolationPayout.Total : 0;
            stats2 = amount > 0
                ? L10n.T("ui.end.consolation", ("amount", amount), ("currency", PlayerWallet.CurrencyName), ("shards", PlayerWallet.Shards))
                : L10n.T("ui.end.no_consolation", ("currency", PlayerWallet.CurrencyName), ("shards", PlayerWallet.Shards));
        }
        else if (isFinalWin)
        {
            int amount = _floorPayout != null ? _floorPayout.Total : 0;
            stats2 = L10n.T("ui.end.final_payout", ("amount", amount), ("total", PlayerWallet.TotalEarned));
        }

        if (stats2 != null)
        {
            TextMeshProUGUI stats2Text = RuntimeUi.CreateText(root, "Stats2", stats2, 32f, shardColor);
            stats2Text.characterSpacing = 4f;
            RuntimeUi.AutoFit(stats2Text, 0.65f);
            RuntimeUi.Place(stats2Text.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, longBody ? -590f : -430f), new Vector2(1600f, 50f));
        }

        // Wider than the title screen's buttons: "TRY THIS MAZE AGAIN" does not fit in 420 at 40 pt
        Vector2 size = new Vector2(560f, 86f);

        // Winning below the final floor leads somewhere: the next, harder floor
        if (won && GameFlow.CurrentFloor < FloorProfile.FinalFloor)
        {
            Button next = RuntimeUi.CreateButton(root, L10n.T("ui.end.next_floor"),
                new Vector2(0f, 440f), size, 42f, new Color(0.08f, 0.32f, 0.24f, 0.95f), new Color(0.15f, 0.55f, 0.42f, 1f), Color.white);
            next.onClick.AddListener(GameFlow.NextFloor);
            RuntimeUi.BindButton(next, "ui.end.next_floor");
        }

        Button again = RuntimeUi.CreateButton(root, L10n.T(won ? "ui.end.replay" : "ui.end.retry"),
            new Vector2(0f, 330f), size, 38f, ButtonIdle, ButtonHover, Color.white);
        again.onClick.AddListener(() => GameFlow.Restart(true, _seed));
        RuntimeUi.BindButton(again, won ? "ui.end.replay" : "ui.end.retry");

        Button fresh = RuntimeUi.CreateButton(root, L10n.T("ui.end.new_maze"),
            new Vector2(0f, 220f), size, 38f, ButtonIdle, ButtonHover, Color.white);
        fresh.onClick.AddListener(() => GameFlow.Restart(false, _seed));
        RuntimeUi.BindButton(fresh, "ui.end.new_maze");

        Button menu = RuntimeUi.CreateButton(root, L10n.T("ui.end.main_menu"),
            new Vector2(0f, 110f), size, 38f, ButtonIdle, ButtonHover, Color.white);
        menu.onClick.AddListener(GameFlow.ReturnToMenu);
        RuntimeUi.BindButton(menu, "ui.end.main_menu");

        PlayerLock.SetCursorFree(true);
    }
}
