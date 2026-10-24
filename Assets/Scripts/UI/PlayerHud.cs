using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The resource HUD: torch battery, sprint stamina, and the interaction prompt lockers write into.
/// Built in Start (order -84) so it sits under the title screen, exactly like TensionDirector's counter.
/// Added to the MazeGenerator object by SetUpAtmosphere.
/// </summary>
[DefaultExecutionOrder(-84)]
public partial class PlayerHud : MonoBehaviour
{
    private static readonly Color CalmColor = new Color(0.86f, 0.86f, 0.9f);
    private static readonly Color AlarmedColor = new Color(0.9f, 0.15f, 0.1f);
    private static readonly Color StaminaColor = new Color(0.9f, 0.9f, 0.86f);
    private static readonly Color StaminaExhaustedColor = new Color(0.9f, 0.2f, 0.15f);
    private static readonly Vector2 BarSize = new Vector2(320f, 16f);

    private Flashlight _flashlight;
    private PlayerStamina _stamina;

    private GameObject _batteryHolder;
    private Image _batteryFill;
    private Image _batteryGhost;
    private float _batteryShown = 1f;
    private float _batteryVel;
    private TextMeshProUGUI _batteryLabel;

    private GameObject _staminaHolder;
    private CanvasGroup _staminaGroup;
    private Image _staminaFill;
    private Image _staminaGhost;
    private float _staminaShown = 1f;
    private float _staminaVel;
    private float _staminaAlpha;

    private TextMeshProUGUI _promptText;
    private TextMeshProUGUI _floorText;

    private static readonly Color SubtitleColor = new Color(0.78f, 0.72f, 0.72f);
    private TextMeshProUGUI _subtitleText;
    private Coroutine _subtitleRoutine;
    private readonly System.Collections.Generic.Queue<(string text, float seconds)> _subtitleQueue = new System.Collections.Generic.Queue<(string text, float seconds)>();

    private static readonly Color ShardColour = new Color(0.7f, 0.9f, 1f);
    private TextMeshProUGUI _shardText;
    private int _shardShown;
    private float _shardPunch;

    // Shop cosmetic (F35): everything that used to read CalmColor directly now reads this instead.
    private Color _tint = CalmColor;

    private bool _floorShownValid, _floorShownHub;
    private int _floorShownFloor;

    /// <summary>Shop cosmetic: recolours CalmColor's uses (battery label, item slots) for the rest of the campaign.</summary>
    public void SetTint(Color color)
    {
        _tint = color;
    }

    /// <summary>Called by MazeGenerator.SetUpAtmosphere. May be null (an incomplete InteractableKit skips throwables entirely).</summary>
    public void BindThrowController(ThrowController controller) => _throwController = controller;

    /// <summary>Called by ConsumableController right after a consumable is used: flashes the slot white and punches it.</summary>
    public void PulseSlot(int index)
    {
        if (index == 0) _slot1Punch = 1f;
        else if (index == 1) _slot2Punch = 1f;
        else if (index == 2) _planPunch = 1f;
    }

    /// <summary>Either may be null (no rig, or a scene with no player).</summary>
    public void Configure(Flashlight flashlight, PlayerStamina stamina)
    {
        _flashlight = flashlight;
        _stamina = stamina;
    }

    /// <summary>F120: the player's microphone, for the "you are loud" bar. May stay null.</summary>
    public void BindVoice(PlayerVoice voice) => _voice = voice;

    private void Start()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _batteryFill = CreateBar(canvas.transform, "BatteryBar", new Vector2(20f, 20f), BarSize,
            new Color(0f, 0f, 0f, 0.55f), _tint, out _batteryHolder, out _batteryGhost);

        _batteryLabel = RuntimeUi.CreateText(canvas.transform, "BatteryLabel", L10n.T("hud.torch"), 20f, _tint);
        _batteryLabel.alignment = TextAlignmentOptions.Left;
        RectTransform labelRect = _batteryLabel.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(0f, 0f);
        labelRect.pivot = new Vector2(0f, 0f);
        labelRect.anchoredPosition = new Vector2(20f, 34f);
        labelRect.sizeDelta = new Vector2(220f, 24f);
        RuntimeUi.AutoFit(_batteryLabel, 0.7f); // D14

        _staminaFill = CreateBar(canvas.transform, "StaminaBar", new Vector2(20f, 62f), BarSize,
            new Color(0f, 0f, 0f, 0.55f), StaminaColor, out _staminaHolder, out _staminaGhost);
        _staminaGroup = _staminaHolder.AddComponent<CanvasGroup>();
        _staminaGroup.alpha = 0f;

        // F120: the microphone's "you are loud" meter, above the stamina bar. Hidden unless PlayerVoice.Active.
        _voiceFill = CreateBar(canvas.transform, "VoiceBar", new Vector2(20f, 104f), BarSize,
            new Color(0f, 0f, 0f, 0.55f), StaminaColor, out _voiceHolder, out _voiceGhost);
        _voiceGroup = _voiceHolder.AddComponent<CanvasGroup>();
        _voiceGroup.alpha = 0f;
        _voiceGroup.blocksRaycasts = false;
        GameObject tick = new GameObject("VoiceThreshold");
        tick.transform.SetParent(_voiceHolder.transform, false);
        tick.layer = _voiceHolder.layer;
        _voiceTick = tick.AddComponent<Image>();
        _voiceTick.color = new Color(1f, 1f, 1f, 0.9f);
        _voiceTick.raycastTarget = false;
        _voiceLabel = RuntimeUi.CreateText(canvas.transform, "VoiceLabel", L10n.T("hud.voice"), 18f, _tint);
        _voiceLabel.alignment = TextAlignmentOptions.Left;
        RectTransform voiceRect = _voiceLabel.rectTransform;
        voiceRect.anchorMin = Vector2.zero;
        voiceRect.anchorMax = Vector2.zero;
        voiceRect.pivot = Vector2.zero;
        voiceRect.anchoredPosition = new Vector2(20f, 118f);
        voiceRect.sizeDelta = new Vector2(220f, 22f);
        RuntimeUi.AutoFit(_voiceLabel, 0.7f);
        _voiceLabel.gameObject.SetActive(false);

        // Top-left header above TensionDirector's vertical objectives card (which starts at y = -86 in Story, -56 in DESCENT).
        TextMeshProUGUI floorText = _floorText = RuntimeUi.CreateText(canvas.transform, "FloorLabel",
            GameFlow.IsDescent ? L10n.T("hud.descent.level", ("n", DescentRun.Depth)) : L10n.T("hud.floor", ("floor", GameFlow.CurrentFloor)), 24f, new Color(0.76f, 0.78f, 0.84f));
        RuntimeUi.AutoFit(floorText, 0.7f);
        floorText.alignment = TextAlignmentOptions.TopLeft;
        floorText.characterSpacing = 6f;
        RectTransform floorRect = floorText.rectTransform;
        floorRect.anchorMin = new Vector2(0f, 1f);
        floorRect.anchorMax = new Vector2(0f, 1f);
        floorRect.pivot = new Vector2(0f, 1f);
        floorRect.anchoredPosition = new Vector2(28f, -20f);
        floorRect.sizeDelta = new Vector2(320f, 30f);

        // Under the floor label, top-left. Hidden in safe rooms and in DESCENT mode (where shards are always 0).
        _shardText = RuntimeUi.CreateText(canvas.transform, "ShardCounter", L10n.T("hud.shards", ("currency", PlayerWallet.CurrencyName), ("shards", 0)), 22f, ShardColour);
        _shardText.alignment = TextAlignmentOptions.TopLeft;
        _shardText.characterSpacing = 6f;
        RectTransform shardRect = _shardText.rectTransform;
        shardRect.anchorMin = new Vector2(0f, 1f);
        shardRect.anchorMax = new Vector2(0f, 1f);
        shardRect.pivot = new Vector2(0f, 1f);
        shardRect.anchoredPosition = new Vector2(28f, -48f);
        shardRect.sizeDelta = new Vector2(320f, 28f);
        _shardShown = PlayerWallet.Shards;
        RuntimeUi.AutoFit(_shardText, 0.7f);
        _shardText.text = ShardLabel(_shardShown);
        if (GameFlow.IsDescent || GameFlow.IsInSafeRoom || GameFlow.IsInIntake)
        {
            _shardText.gameObject.SetActive(false);
        }

        _promptText = RuntimeUi.CreateText(canvas.transform, "InteractPrompt", "", 34f, Color.white);
        RectTransform promptRect = _promptText.rectTransform;
        promptRect.anchorMin = new Vector2(0.5f, 0f);
        promptRect.anchorMax = new Vector2(0.5f, 0f);
        promptRect.pivot = new Vector2(0.5f, 0f);
        promptRect.anchoredPosition = new Vector2(0f, 120f);
        promptRect.sizeDelta = new Vector2(600f, 50f);
        RuntimeUi.AutoFit(_promptText, 0.6f); // D14: longer in FR/DE
        _promptText.gameObject.SetActive(false);

        // Above the prompt (120) and clear of the escape timer, which is top-centre.
        _subtitleText = RuntimeUi.CreateText(canvas.transform, "DreadSubtitle", "", 30f, SubtitleColor);
        _subtitleText.fontStyle = FontStyles.Italic;
        _subtitleText.characterSpacing = 2f;
        // S9 (D14): up to two lines, growing upwards from the same baseline (bottom-aligned in a taller box).
        _subtitleText.alignment = TextAlignmentOptions.Bottom;
        _subtitleText.textWrappingMode = TextWrappingModes.Normal;
        RectTransform subtitleRect = _subtitleText.rectTransform;
        subtitleRect.anchorMin = new Vector2(0.5f, 0f);
        subtitleRect.anchorMax = new Vector2(0.5f, 0f);
        subtitleRect.pivot = new Vector2(0.5f, 0f);
        subtitleRect.anchoredPosition = new Vector2(0f, 196f);
        subtitleRect.sizeDelta = new Vector2(900f, 88f);
        RuntimeUi.AutoFit(_subtitleText, 0.8f);
        _subtitleText.gameObject.SetActive(false);

        // Styled item cards/pills at bottom-right with a boxed [Key] cap, color-coded accent bar, and bold ×Count badge.
        _slot2Card = BuildItemSlotCard(canvas.transform, "Slot2", CompassSlotColor, new Vector2(-24f, 20f));
        _slot2Text = _slot2Card.NameText;

        _slot1Card = BuildItemSlotCard(canvas.transform, "Slot1", BatterySlotColor, new Vector2(-24f, 64f));
        _slot1Text = _slot1Card.NameText;

        _throwCard = BuildItemSlotCard(canvas.transform, "ThrowSlot", ThrowColour, new Vector2(-24f, 108f));
        _throwText = _throwCard.NameText;
        _throwCard.Root.SetActive(false);

        _planCard = BuildItemSlotCard(canvas.transform, "PlanSlot", PlanSlotColor, new Vector2(-24f, 152f));
        _planText = _planCard.NameText;
        _planCard.Root.SetActive(false);

        _secondWindCard = BuildItemSlotCard(canvas.transform, "SecondWindLabel", SecondWindColor, new Vector2(-24f, 196f));
        _secondWindText = _secondWindCard.NameText;
        _secondWindCard.Root.SetActive(false);

        L10n.Changed += OnLanguageChanged;
        GameInput.DeviceChanged += RefreshPrompt;   // F119: the key label follows keyboard / gamepad / touch (the slot chips re-read it every frame)
    }

    private void OnDestroy()
    {
        L10n.Changed -= OnLanguageChanged;
        GameInput.DeviceChanged -= RefreshPrompt;
    }

    /// <summary>S9: drops the caches of composed labels so the next Update rewrites them in the new language.</summary>
    private void OnLanguageChanged()
    {
        _floorShownValid = false;
        _slotKey1 = _slotKey2 = _slotKeyThrow = _slotKeyPlan = _slotKeySecondWind = null;
        if (_shardText != null) _shardText.text = ShardLabel(_shardShown);
        _promptBuiltText = null;
    }

    private void Update()
    {
        // F86: the Intake is not a floor - hide the "FLOOR n" label (the TORCH bar stays, it is taught there).
        if (_floorText != null)
        {
            _floorText.gameObject.SetActive(!GameFlow.IsInIntake);
            // F92: the hospital between mazes is not a floor.
            bool hub = GameFlow.IsInHub;
            int floor = GameFlow.IsDescent ? DescentRun.Depth : GameFlow.CurrentFloor; // F107: a descent shows its true depth
            if (!_floorShownValid || _floorShownHub != hub || _floorShownFloor != floor)
            {
                _floorShownValid = true;
                _floorShownHub = hub;
                _floorShownFloor = floor;
                _floorText.text = hub ? L10n.T("hud.asylum") : GameFlow.IsDescent ? L10n.T("hud.descent.level", ("n", floor)) : L10n.T("hud.floor", ("floor", floor));
            }
        }
        UpdateBattery();
        UpdateStamina();
        UpdateVoice(); // F120
        UpdateShardCounter();
        UpdateItemSlots();
        RefreshPrompt(); // F76: keeps the prompt line's touch-mode visibility current every frame too
    }
}
