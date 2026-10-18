using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The resource HUD: torch battery, sprint stamina, and the interaction prompt lockers write into.
/// Built in Start (order -84) so it sits under the title screen, exactly like TensionDirector's counter.
/// Added to the MazeGenerator object by SetUpAtmosphere.
/// </summary>
[DefaultExecutionOrder(-84)]
public class PlayerHud : MonoBehaviour
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

    /// <summary>Shop cosmetic: recolours CalmColor's uses (battery label, item slots) for the rest of the campaign.</summary>
    public void SetTint(Color color)
    {
        _tint = color;
    }

    private static readonly Color SecondWindColor = new Color(0.25f, 1f, 0.7f);
    private static readonly Color BatterySlotColor = new Color(1.00f, 0.78f, 0.28f);
    private static readonly Color CompassSlotColor = new Color(0.38f, 0.88f, 1.00f);
    private static readonly Color PlanSlotColor = new Color(0.55f, 0.95f, 0.68f);
    // F74: throwable carry count, stacked above the two consumable slots.
    private static readonly Color ThrowColour = new Color(0.95f, 0.80f, 0.42f);

    private struct ItemSlotCard
    {
        public GameObject Root;
        public RectTransform Rect;
        public Image Background;
        public Image Accent;
        public Image KeyCapBg;
        public TextMeshProUGUI KeyText;
        public TextMeshProUGUI NameText;
        public Image CountBg;
        public TextMeshProUGUI CountText;
        public Color AccentColor;
    }

    private ItemSlotCard _slot1Card;
    private ItemSlotCard _slot2Card;
    private ItemSlotCard _throwCard;
    private ItemSlotCard _planCard;
    private ItemSlotCard _secondWindCard;

    private TextMeshProUGUI _slot1Text;
    private TextMeshProUGUI _slot2Text;
    private TextMeshProUGUI _secondWindText;
    private float _slot1Punch;
    private float _slot2Punch;
    // F110: the Floor Plan line (M, hold), stacked above the throw line.
    private TextMeshProUGUI _planText;
    private float _planPunch;
    private string _slotKeyPlan;
    private string _slotKeySecondWind;

    private TextMeshProUGUI _throwText;
    private ThrowController _throwController;

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

    // F72 decision D12: PlayerInteractor's focus prompt overrides Locker's own trigger prompt while set,
    // so the two never fight over the same line - see RefreshPrompt.
    // S9 (D15, plan section 6): a prompt is kept as PARTS - the physical key label ("E", or "USE" on touch), the translated text, an
    // optional hold percentage - so nothing (the touch INTERACT button) ever has to parse the composed "E  HIDE  53%" line again.
    // A prompt set by key (SetPromptKey) is translated when it is drawn, so it follows a language change without being set again.
    private struct PromptParts
    {
        public string Key;
        public bool HasAction;          // F119: the label comes from InputGlyphs (keyboard key, gamepad button or touch word), resolved when the line is drawn
        public GameAction Action;
        public string TouchLabel;
        public string Text;
        public string TextKey;
        public int Percent;

        public bool IsEmpty => string.IsNullOrEmpty(Text) && string.IsNullOrEmpty(TextKey);
        public string ResolvedText => !string.IsNullOrEmpty(TextKey) ? L10n.T(TextKey) : Text;
    }

    private PromptParts _triggerPrompt;
    private PromptParts _focusPrompt;
    private PromptParts _hintPrompt; // F106: a prompt that shows by itself for a few seconds, below any interaction prompt
    private Coroutine _hintRoutine;
    private string _promptBuiltKey, _promptBuiltText;
    private int _promptBuiltPercent = int.MinValue;
    private string _promptBuilt = "";

    /// <summary>Called by Locker on enter/exit/trigger. Null or empty hides the prompt (unless a focus prompt is showing).</summary>
    public void SetPrompt(string text)
    {
        _triggerPrompt = string.IsNullOrEmpty(text) ? default : new PromptParts { Text = text, Percent = -1 };
        RefreshPrompt();
    }

    /// <summary>S9: a trigger prompt drawn as "{keyLabel}  {L10n.T(textKey)}" and re-translated on a language change.</summary>
    public void SetPromptKey(string keyLabel, string textKey)
    {
        _triggerPrompt = string.IsNullOrEmpty(textKey) ? default : new PromptParts { Key = keyLabel, TextKey = textKey, Percent = -1 };
        RefreshPrompt();
    }

    /// <summary>F119: as SetPromptKey(string, string) but the key label follows the player's device (keyboard key / gamepad button / touch word).</summary>
    public void SetPromptKey(GameAction action, string textKey)
    {
        _triggerPrompt = string.IsNullOrEmpty(textKey) ? default : new PromptParts { HasAction = true, Action = action, TextKey = textKey, Percent = -1 };
        RefreshPrompt();
    }

    /// <summary>Called every frame by PlayerInteractor. Null or empty falls back to the trigger prompt.</summary>
    public void SetFocusPrompt(string text)
    {
        _focusPrompt = string.IsNullOrEmpty(text) ? default : new PromptParts { Text = text, Percent = -1 };
        RefreshPrompt();
    }

    /// <summary>S9: the focus prompt as parts - the key label, the (already translated) text and, while a hold is filling, its percentage (or -1).</summary>
    public void SetFocusPrompt(string keyLabel, string text, int percent = -1)
    {
        _focusPrompt = string.IsNullOrEmpty(text) ? default : new PromptParts { Key = keyLabel, Text = text, Percent = percent };
        RefreshPrompt();
    }

    /// <summary>F119: the focus prompt with a device-following key label.</summary>
    public void SetFocusPrompt(GameAction action, string text, int percent = -1)
    {
        _focusPrompt = string.IsNullOrEmpty(text) ? default : new PromptParts { HasAction = true, Action = action, Text = text, Percent = percent };
        RefreshPrompt();
    }

    // The prompt of something you can act on; the F106 hint only fills the line when there is none (and is never the INTERACT button's text).
    private PromptParts InteractionPrompt => !_focusPrompt.IsEmpty ? _focusPrompt : _triggerPrompt;
    private PromptParts ActivePrompt => !InteractionPrompt.IsEmpty ? InteractionPrompt : _hintPrompt;

    /// <summary>
    /// F106: "{keyLabel}  {L10n.T(textKey)}" for `seconds`, whenever nothing else holds the prompt line (an interactable's prompt wins and the hint
    /// reappears behind it). A second call replaces the first. Hidden on touch, like every prompt: the on-screen buttons say it.
    /// </summary>
    public void ShowHintPrompt(string keyLabel, string textKey, float seconds, int percent = -1)
    {
        if (_hintRoutine != null) StopCoroutine(_hintRoutine);
        _hintPrompt = new PromptParts { Key = keyLabel, TextKey = textKey, Percent = percent };
        RefreshPrompt();
        _hintRoutine = StartCoroutine(ClearHintAfter(seconds));
    }

    /// <summary>F119: ShowHintPrompt with a device-following key label; <paramref name="touchLabel"/> is what a touch screen shows.</summary>
    public void ShowHintPrompt(GameAction action, string touchLabel, string textKey, float seconds, int percent = -1)
    {
        if (_hintRoutine != null) StopCoroutine(_hintRoutine);
        _hintPrompt = new PromptParts { HasAction = true, Action = action, TouchLabel = touchLabel, TextKey = textKey, Percent = percent };
        RefreshPrompt();
        _hintRoutine = StartCoroutine(ClearHintAfter(seconds));
    }

    private IEnumerator ClearHintAfter(float seconds)
    {
        yield return new WaitForSecondsRealtime(seconds);
        _hintPrompt = default;
        _hintRoutine = null;
        RefreshPrompt();
    }

    /// <summary>The resolved prompt as one line (focus overrides trigger, same rule RefreshPrompt uses). Null/empty when nothing is shown.</summary>
    public string CurrentPrompt => FormatPrompt(ActivePrompt);

    /// <summary>F76/S9: just the words of the resolved prompt (no key label, no percentage) for TouchControls' INTERACT button. Null/empty when nothing is shown.</summary>
    public string CurrentPromptText => InteractionPrompt.IsEmpty ? null : InteractionPrompt.ResolvedText;

    private string FormatPrompt(PromptParts p)
    {
        if (p.IsEmpty) return null;
        string text = p.ResolvedText;
        string key = p.HasAction ? InputGlyphs.Key(p.Action, p.TouchLabel) : p.Key;
        if (text == _promptBuiltText && key == _promptBuiltKey && p.Percent == _promptBuiltPercent) return _promptBuilt;
        string line = string.IsNullOrEmpty(key) ? text : $"{key}  {text}";
        if (p.Percent >= 0) line += $"  {p.Percent}%";
        _promptBuiltText = text;
        _promptBuiltKey = key;
        _promptBuiltPercent = p.Percent;
        _promptBuilt = line;
        return line;
    }

    private void RefreshPrompt()
    {
        if (_promptText == null) return;
        // F76 T6: the INTERACT button shows this same text itself while touch is active, so the
        // keyboard-shaped prompt line ("E  HIDE") underneath the crosshair would just be a duplicate.
        if (TouchInput.Active)
        {
            _promptText.gameObject.SetActive(false);
            return;
        }
        string text = CurrentPrompt;
        bool show = !string.IsNullOrEmpty(text);
        _promptText.gameObject.SetActive(show);
        if (show) _promptText.text = text;
    }

    /// <summary>
    /// A dread cue, not narration: one line at a time, low on screen, fading in then out over
    /// unscaled time so it cannot freeze on screen through a pause. A new call replaces the current one.
    /// </summary>
    public void ShowSubtitle(string text, float seconds)
    {
        if (_subtitleText == null || string.IsNullOrEmpty(text)) return;
        if (_subtitleRoutine != null) StopCoroutine(_subtitleRoutine);
        _subtitleRoutine = StartCoroutine(SubtitleRoutine(text, Mathf.Max(0.2f, seconds, ReadingSeconds(text))));
    }

    /// <summary>F100: plays after the current line has finished (at once when none is showing). ShowSubtitle still replaces the line on screen; queued lines wait their turn behind it.</summary>
    public void QueueSubtitle(string text, float seconds)
    {
        if (_subtitleText == null || string.IsNullOrEmpty(text)) return;
        if (_subtitleRoutine == null) ShowSubtitle(text, seconds);
        else _subtitleQueue.Enqueue((text, Mathf.Max(seconds, ReadingSeconds(text))));
    }

    /// <summary>S9 (D14): a longer translation needs longer on screen - never less than 1.2 s plus 15 characters per second.</summary>
    public static float ReadingSeconds(string text) => string.IsNullOrEmpty(text) ? 0f : 1.2f + text.Length / 15f;

    private IEnumerator SubtitleRoutine(string text, float seconds)
    {
        const float fadeIn = 0.15f;
        const float fadeOut = 0.6f;
        float hold = Mathf.Max(0f, seconds - fadeIn - fadeOut);

        // F86: the maze's dread cues are italic; the Intake's explanations are plain teaching text.
        _subtitleText.text = text;
        _subtitleText.fontStyle = GameFlow.IsInIntake ? FontStyles.Normal : FontStyles.Italic;
        _subtitleText.gameObject.SetActive(true);

        float t = 0f;
        while (t < fadeIn)
        {
            t += Time.unscaledDeltaTime;
            SetSubtitleAlpha(Mathf.Clamp01(t / fadeIn));
            yield return null;
        }
        SetSubtitleAlpha(1f);

        t = 0f;
        while (t < hold)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }

        t = 0f;
        while (t < fadeOut)
        {
            t += Time.unscaledDeltaTime;
            SetSubtitleAlpha(1f - Mathf.Clamp01(t / fadeOut));
            yield return null;
        }

        SetSubtitleAlpha(0f);
        _subtitleText.gameObject.SetActive(false);
        _subtitleRoutine = null;
        if (_subtitleQueue.Count > 0)
        {
            (string next, float nextSeconds) = _subtitleQueue.Dequeue();
            ShowSubtitle(next, nextSeconds);
        }
    }

    private void SetSubtitleAlpha(float alpha)
    {
        Color c = SubtitleColor;
        c.a = alpha;
        _subtitleText.color = c;
    }

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

    private static ItemSlotCard BuildItemSlotCard(Transform parent, string name, Color accentColor, Vector2 anchoredPos)
    {
        ItemSlotCard card = new ItemSlotCard { AccentColor = accentColor };

        GameObject root = new GameObject(name);
        root.transform.SetParent(parent, false);
        RectTransform rect = root.AddComponent<RectTransform>();
        PlaceBottomRight(rect, anchoredPos, new Vector2(296f, 38f));

        Image bg = root.AddComponent<Image>();
        bg.color = new Color(0.03f, 0.04f, 0.055f, 0.82f);
        bg.raycastTarget = false;

        // Top highlight hairline
        GameObject topEdge = new GameObject("TopEdge");
        topEdge.transform.SetParent(root.transform, false);
        RectTransform topRect = topEdge.AddComponent<RectTransform>();
        topRect.anchorMin = new Vector2(0f, 1f);
        topRect.anchorMax = new Vector2(1f, 1f);
        topRect.pivot = new Vector2(0.5f, 1f);
        topRect.anchoredPosition = Vector2.zero;
        topRect.sizeDelta = new Vector2(0f, 1f);
        Image topImg = topEdge.AddComponent<Image>();
        topImg.color = new Color(1f, 1f, 1f, 0.08f);
        topImg.raycastTarget = false;

        // Left color-coded accent bar
        GameObject accentObj = new GameObject("Accent");
        accentObj.transform.SetParent(root.transform, false);
        RectTransform accentRect = accentObj.AddComponent<RectTransform>();
        accentRect.anchorMin = new Vector2(0f, 0f);
        accentRect.anchorMax = new Vector2(0f, 1f);
        accentRect.pivot = new Vector2(0f, 0.5f);
        accentRect.anchoredPosition = Vector2.zero;
        accentRect.sizeDelta = new Vector2(4f, 0f);
        Image accentImg = accentObj.AddComponent<Image>();
        accentImg.color = accentColor;
        accentImg.raycastTarget = false;

        // Boxed [Key] cap on the left
        GameObject keyCapObj = new GameObject("KeyCap");
        keyCapObj.transform.SetParent(root.transform, false);
        RectTransform keyCapRect = keyCapObj.AddComponent<RectTransform>();
        keyCapRect.anchorMin = new Vector2(0f, 0.5f);
        keyCapRect.anchorMax = new Vector2(0f, 0.5f);
        keyCapRect.pivot = new Vector2(0f, 0.5f);
        keyCapRect.anchoredPosition = new Vector2(12f, 0f);
        keyCapRect.sizeDelta = new Vector2(44f, 26f);
        Image keyCapBg = keyCapObj.AddComponent<Image>();
        keyCapBg.color = new Color(0.13f, 0.16f, 0.22f, 0.95f);
        keyCapBg.raycastTarget = false;

        TextMeshProUGUI keyText = RuntimeUi.CreateText(keyCapObj.transform, "Key", "", 15f, new Color(0.98f, 0.94f, 0.82f));
        keyText.alignment = TextAlignmentOptions.Center;
        keyText.characterSpacing = 1f;
        RectTransform keyTextRect = keyText.rectTransform;
        keyTextRect.anchorMin = Vector2.zero;
        keyTextRect.anchorMax = Vector2.one;
        keyTextRect.offsetMin = new Vector2(3f, 1f);
        keyTextRect.offsetMax = new Vector2(-3f, -1f);
        RuntimeUi.AutoFit(keyText, 0.55f);

        // Item name in the middle
        TextMeshProUGUI nameText = RuntimeUi.CreateText(root.transform, "Label", "", 19f, Color.white);
        nameText.alignment = TextAlignmentOptions.MidlineLeft;
        nameText.characterSpacing = 2.5f;
        RectTransform nameRect = nameText.rectTransform;
        nameRect.anchorMin = new Vector2(0f, 0f);
        nameRect.anchorMax = new Vector2(1f, 1f);
        nameRect.offsetMin = new Vector2(64f, 2f);
        nameRect.offsetMax = new Vector2(-60f, -2f);
        RuntimeUi.AutoFit(nameText, 0.58f);

        // Bold ×Count badge on the right
        GameObject countObj = new GameObject("CountBadge");
        countObj.transform.SetParent(root.transform, false);
        RectTransform countRect = countObj.AddComponent<RectTransform>();
        countRect.anchorMin = new Vector2(1f, 0.5f);
        countRect.anchorMax = new Vector2(1f, 0.5f);
        countRect.pivot = new Vector2(1f, 0.5f);
        countRect.anchoredPosition = new Vector2(-8f, 0f);
        countRect.sizeDelta = new Vector2(46f, 26f);
        Image countBg = countObj.AddComponent<Image>();
        countBg.color = new Color(accentColor.r * 0.22f, accentColor.g * 0.22f, accentColor.b * 0.22f, 0.85f);
        countBg.raycastTarget = false;

        TextMeshProUGUI countText = RuntimeUi.CreateText(countObj.transform, "Count", "×0", 19f, accentColor);
        countText.alignment = TextAlignmentOptions.Center;
        countText.characterSpacing = 1f;
        RectTransform countTextRect = countText.rectTransform;
        countTextRect.anchorMin = Vector2.zero;
        countTextRect.anchorMax = Vector2.one;
        countTextRect.offsetMin = new Vector2(2f, 1f);
        countTextRect.offsetMax = new Vector2(-2f, -1f);
        RuntimeUi.AutoFit(countText, 0.6f);

        card.Root = root;
        card.Rect = rect;
        card.Background = bg;
        card.Accent = accentImg;
        card.KeyCapBg = keyCapBg;
        card.KeyText = keyText;
        card.NameText = nameText;
        card.CountBg = countBg;
        card.CountText = countText;
        return card;
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

    private bool _floorShownValid, _floorShownHub;
    private int _floorShownFloor;
    private string _slotKey1, _slotKey2, _slotKeyThrow;

    private static string ShardLabel(int shards) => L10n.T("hud.shards", ("currency", PlayerWallet.CurrencyName), ("shards", shards));

    private static void PlaceBottomRight(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(1f, 0f);
        rect.anchorMax = new Vector2(1f, 0f);
        rect.pivot = new Vector2(1f, 0f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
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

    private void UpdateItemSlots()
    {
        if (_slot1Card.Root == null) return;

        // F86: the Intake hides the consumable slots like the shop does, but the THROW line stays - it is taught there.
        bool inShop = GameFlow.IsInSafeRoom;
        int secondWindCount = PlayerInventory.Count(ShopItem.SecondWind);
        bool secondWindHeld = secondWindCount > 0;

        if (GameFlow.IsInIntake)
        {
            _slot1Card.Root.SetActive(false);
            _slot2Card.Root.SetActive(false);
            if (_planCard.Root != null) _planCard.Root.SetActive(false);
            if (_secondWindCard.Root != null) _secondWindCard.Root.SetActive(false);
            if (_throwCard.Root != null)
            {
                int held = _throwController != null ? _throwController.Carried : 0;
                _throwCard.Root.SetActive(held > 0);
                if (held > 0)
                {
                    SetSlotCard(ref _throwCard, ref _slotKeyThrow, "hud.slot.throw", GameAction.Throw, held);
                    ApplySlotCard(ref _throwCard, true, 0f);
                    _throwCard.Rect.anchoredPosition = new Vector2(-24f, 20f);
                }
            }
            return;
        }

        if (inShop)
        {
            _slot1Card.Root.SetActive(false);
            _slot2Card.Root.SetActive(false);
            if (_throwCard.Root != null) _throwCard.Root.SetActive(false);
            if (_planCard.Root != null) _planCard.Root.SetActive(false);
            if (_secondWindCard.Root != null) _secondWindCard.Root.SetActive(false);
            return;
        }

        _slot1Punch = Mathf.MoveTowards(_slot1Punch, 0f, Time.unscaledDeltaTime / 0.25f);
        _slot2Punch = Mathf.MoveTowards(_slot2Punch, 0f, Time.unscaledDeltaTime / 0.25f);
        _planPunch = Mathf.MoveTowards(_planPunch, 0f, Time.unscaledDeltaTime / 0.25f);

        float stackY = 20f;
        const float stepY = 44f;

        // 1. Star Compass (Slot 2)
        int compassCount = PlayerInventory.Count(ShopItem.StarCompass);
        _slot2Card.Root.SetActive(true);
        _slot2Card.Rect.anchoredPosition = new Vector2(-24f, stackY);
        SetSlotCard(ref _slot2Card, ref _slotKey2, "hud.slot.compass", GameAction.Item2, compassCount);
        ApplySlotCard(ref _slot2Card, compassCount > 0, _slot2Punch);
        stackY += stepY;

        // 2. Spare Battery (Slot 1)
        int batteryCount = PlayerInventory.Count(ShopItem.SpareBattery);
        _slot1Card.Root.SetActive(true);
        _slot1Card.Rect.anchoredPosition = new Vector2(-24f, stackY);
        SetSlotCard(ref _slot1Card, ref _slotKey1, "hud.slot.battery", GameAction.Item1, batteryCount);
        ApplySlotCard(ref _slot1Card, batteryCount > 0, _slot1Punch);
        stackY += stepY;

        // 3. Throwables (Bottle / Can)
        if (_throwCard.Root != null)
        {
            int carried = _throwController != null ? _throwController.Carried : 0;
            bool showThrow = carried > 0 && !GameFlow.IsInShop && !GameFlow.IsInHub;
            _throwCard.Root.SetActive(showThrow);
            if (showThrow)
            {
                _throwCard.Rect.anchoredPosition = new Vector2(-24f, stackY);
                SetSlotCard(ref _throwCard, ref _slotKeyThrow, "hud.slot.throw", GameAction.Throw, carried);
                ApplySlotCard(ref _throwCard, true, 0f);
                stackY += stepY;
            }
        }

        // 4. Floor Plan
        if (_planCard.Root != null)
        {
            int plans = PlayerInventory.Count(ShopItem.FloorPlan);
            _planCard.Root.SetActive(plans > 0);
            if (plans > 0)
            {
                _planCard.Rect.anchoredPosition = new Vector2(-24f, stackY);
                SetSlotCard(ref _planCard, ref _slotKeyPlan, "hud.slot.floor_plan", GameAction.Map, plans);
                ApplySlotCard(ref _planCard, true, _planPunch);
                stackY += stepY;
            }
        }

        // 5. Second Wind
        if (_secondWindCard.Root != null)
        {
            _secondWindCard.Root.SetActive(secondWindHeld);
            if (secondWindHeld)
            {
                _secondWindCard.Rect.anchoredPosition = new Vector2(-24f, stackY);
                SetPassiveSlotCard(ref _secondWindCard, ref _slotKeySecondWind, "hud.second_wind", "✦", secondWindCount);
                ApplySlotCard(ref _secondWindCard, true, 0f);
            }
        }
    }

    /// <summary>S9: populates key cap, item name, and ×count badge only when the key mode, count or language changed.</summary>
    private static void SetSlotCard(ref ItemSlotCard card, ref string cache, string key, GameAction action, int count)
    {
        string chip = InputGlyphs.Key(action, L10n.T("ui.key.tap"));
        string id = key + "|" + chip + "|" + count;
        if (cache == id) return;
        cache = id;

        card.KeyText.text = chip;
        card.CountText.text = "×" + count;

        string raw = L10n.T(key, ("key", ""), ("count", count)).Trim();
        string suffix = "x" + count;
        if (raw.EndsWith(suffix, System.StringComparison.OrdinalIgnoreCase))
        {
            raw = raw.Substring(0, raw.Length - suffix.Length).TrimEnd();
        }
        card.NameText.text = raw;
    }

    private static void SetPassiveSlotCard(ref ItemSlotCard card, ref string cache, string key, string badgeGlyph, int count)
    {
        string id = key + "|" + badgeGlyph + "|" + count;
        if (cache == id) return;
        cache = id;

        card.KeyText.text = badgeGlyph;
        card.NameText.text = L10n.T(key);
        card.CountText.text = "×" + count;
    }

    private static void ApplySlotCard(ref ItemSlotCard card, bool has, float punch)
    {
        float scale = 1f + 0.18f * punch;
        card.Rect.localScale = new Vector3(scale, scale, 1f);

        Color accent = card.AccentColor;
        card.Background.color = Color.Lerp(
            has ? new Color(0.03f, 0.04f, 0.055f, 0.84f) : new Color(0.03f, 0.04f, 0.055f, 0.36f),
            new Color(0.22f, 0.25f, 0.32f, 0.92f),
            punch);

        card.Accent.color = Color.Lerp(
            has ? accent : new Color(accent.r * 0.45f, accent.g * 0.45f, accent.b * 0.45f, 0.35f),
            Color.white,
            punch);

        card.KeyCapBg.color = has
            ? new Color(0.13f, 0.16f, 0.22f, 0.95f)
            : new Color(0.09f, 0.10f, 0.14f, 0.42f);

        card.KeyText.color = Color.Lerp(
            has ? new Color(0.98f, 0.94f, 0.82f, 1f) : new Color(0.65f, 0.65f, 0.68f, 0.42f),
            Color.white,
            punch);

        card.NameText.color = Color.Lerp(
            has ? new Color(0.94f, 0.95f, 0.98f, 1f) : new Color(0.62f, 0.64f, 0.68f, 0.38f),
            Color.white,
            punch);

        card.CountBg.color = has
            ? new Color(accent.r * 0.24f, accent.g * 0.24f, accent.b * 0.24f, 0.88f)
            : new Color(0.08f, 0.09f, 0.12f, 0.35f);

        card.CountText.color = Color.Lerp(
            has ? accent : new Color(0.55f, 0.57f, 0.62f, 0.38f),
            Color.white,
            punch);
    }

    private void UpdateShardCounter()
    {
        if (_shardText == null) return;

        // Hide shards in safe rooms, Intake, and DESCENT mode (where shards are always 0 and unused).
        if (GameFlow.IsInSafeRoom || GameFlow.IsDescent || GameFlow.IsInIntake)
        {
            _shardText.gameObject.SetActive(false);
            return;
        }
        _shardText.gameObject.SetActive(true);

        int shards = PlayerWallet.Shards;
        if (shards != _shardShown)
        {
            _shardShown = shards;
            _shardText.text = ShardLabel(_shardShown);
            _shardPunch = 1f;
        }

        _shardPunch = Mathf.MoveTowards(_shardPunch, 0f, Time.unscaledDeltaTime / 0.25f);
        float scale = 1f + 0.25f * _shardPunch;
        _shardText.rectTransform.localScale = new Vector3(scale, scale, 1f);
    }

    private void UpdateBattery()
    {
        if (_flashlight == null || _batteryFill == null)
        {
            if (_batteryHolder != null) _batteryHolder.SetActive(false);
            if (_batteryLabel != null) _batteryLabel.gameObject.SetActive(false);
            return;
        }

        float charge = _flashlight.Charge;
        // Eased toward the real value so the bar visibly slides down instead of stepping. unscaled
        // time keeps the easing alive if the clock is frozen, and the real value does not move then.
        _batteryShown = Mathf.SmoothDamp(_batteryShown, charge, ref _batteryVel, 0.25f, float.MaxValue, Time.unscaledDeltaTime);
        SetFill(_batteryFill, _batteryShown);
        // The ghost lags behind on the way down, so a drain leaves a pale trail that shrinks after it
        SetFill(_batteryGhost, Mathf.Max(_batteryShown, Mathf.MoveTowards(GhostValue(_batteryGhost), charge, 0.05f * Time.unscaledDeltaTime)));

        // Calm above 30%, ramping to alarmed as it empties from there.
        float t = Mathf.Clamp01(charge / 0.3f);
        Color color = Color.Lerp(AlarmedColor, _tint, t);
        _batteryFill.color = color;

        if (_flashlight.IsDead)
        {
            _batteryLabel.text = L10n.T("hud.torch_dead");
            _batteryLabel.color = AlarmedColor;
        }
        else if (_flashlight.IsDropped)
        {
            // F120: the torch is on the floor - the bar keeps showing the charge, the label blinks at 2 Hz (steady with Reduce flashing)
            _batteryLabel.text = L10n.T("hud.torch_dropped");
            bool on = FlashSafety.On || ((int)(Time.unscaledTime * 4f)) % 2 == 0;
            _batteryLabel.color = on ? AlarmedColor : _tint;
        }
        else
        {
            _batteryLabel.text = L10n.T("hud.torch");
            bool blinking = charge <= 0.1f;
            if (blinking)
            {
                bool on = FlashSafety.On || ((int)(Time.time * 4f)) % 2 == 0; // F118: steady warning colour with Reduce flashing on
                _batteryLabel.color = on ? AlarmedColor : _tint;
            }
            else
            {
                _batteryLabel.color = color;
            }
        }
    }


    // ---- F120: the microphone meter -----------------------------------------------------------------------------------------
    private PlayerVoice _voice;
    private GameObject _voiceHolder;
    private Image _voiceFill;
    private Image _voiceGhost;
    private Image _voiceTick;
    private CanvasGroup _voiceGroup;
    private TextMeshProUGUI _voiceLabel;
    private float _voiceShown;
    private float _voiceVel;
    private float _voiceAlpha;

    /// <summary>F120: the player's microphone, for the "you are loud" bar. May stay null.</summary>
    public void BindVoice(PlayerVoice voice) => _voice = voice;

    private void UpdateVoice()
    {
        if (_voiceFill == null || _voiceGroup == null) return;

        bool active = _voice != null && _voice.Active;
        _voiceAlpha = Mathf.MoveTowards(_voiceAlpha, active ? 1f : 0f, Time.unscaledDeltaTime * 4f);
        _voiceGroup.alpha = _voiceAlpha;
        if (_voiceLabel != null) _voiceLabel.gameObject.SetActive(_voiceAlpha > 0.01f);
        if (!active) return;

        float level = MicInput.Level;
        _voiceShown = Mathf.SmoothDamp(_voiceShown, level, ref _voiceVel, 0.05f, float.MaxValue, Time.unscaledDeltaTime);
        SetFill(_voiceFill, _voiceShown);
        SetFill(_voiceGhost, 0f);

        float threshold = _voice.Threshold;
        if (_voiceTick != null)
        {
            RectTransform tr = _voiceTick.rectTransform;
            tr.anchorMin = new Vector2(Mathf.Clamp01(threshold), 0f);
            tr.anchorMax = new Vector2(Mathf.Clamp01(threshold), 1f);
            tr.pivot = new Vector2(0.5f, 0.5f);
            tr.sizeDelta = new Vector2(2f, 0f);
            tr.anchoredPosition = Vector2.zero;
        }

        bool loud = _voice.Loud;
        _voiceFill.color = loud ? AlarmedColor : StaminaColor;
        _voiceGroup.alpha = _voiceAlpha * (level < 0.6f * threshold && !loud ? 0.35f : 1f);
        if (_voiceLabel != null)
        {
            _voiceLabel.text = loud ? L10n.T("hud.voice_loud") : L10n.T("hud.voice");
            // 4 Hz blink; steady with Reduce flashing on (F118)
            bool on = !loud || FlashSafety.On || ((int)(Time.unscaledTime * 8f)) % 2 == 0;
            _voiceLabel.color = loud ? (on ? AlarmedColor : _tint) : _tint;
        }
    }
    private void UpdateStamina()
    {
        if (_stamina == null || _staminaFill == null)
        {
            if (_staminaHolder != null) _staminaHolder.SetActive(false);
            return;
        }

        float fraction = _stamina.Fraction;
        // Faster easing than the battery: sprinting and recovering are seconds-long, so the bar has
        // to visibly drain and refill in step with them.
        _staminaShown = Mathf.SmoothDamp(_staminaShown, fraction, ref _staminaVel, 0.12f, float.MaxValue, Time.unscaledDeltaTime);
        SetFill(_staminaFill, _staminaShown);
        SetFill(_staminaGhost, Mathf.Max(_staminaShown, Mathf.MoveTowards(GhostValue(_staminaGhost), fraction, 0.4f * Time.unscaledDeltaTime)));

        // Exhausted pulses red so the lockout is legible; a recovering bar tints toward white as it fills
        Color color = StaminaColor;
        if (_stamina.Exhausted || _stamina.Limping) // F120: a limp pulses like the lockout
        {
            float pulse = 0.65f + 0.35f * Mathf.Abs(Mathf.Sin(Time.unscaledTime * 6f));
            color = StaminaExhaustedColor * pulse;
            color.a = 1f;
        }
        _staminaFill.color = color;

        // Stays visible until it has genuinely finished refilling, then fades out
        bool full = fraction >= 0.999f && _staminaShown >= 0.99f && !_stamina.Limping;
        float targetAlpha = full ? 0f : 1f;
        _staminaAlpha = Mathf.MoveTowards(_staminaAlpha, targetAlpha, Time.unscaledDeltaTime * (full ? 1f : 6f));
        if (_staminaGroup != null) _staminaGroup.alpha = _staminaAlpha;
    }

    /// <summary>
    /// Sets how much of the track a fill covers by moving its right anchor. Image.Type.Filled cannot
    /// be used here: with no sprite assigned Unity ignores fillAmount and always draws a full rect,
    /// which is why the bars used to look like they never moved.
    /// </summary>
    private static void SetFill(Image fill, float amount)
    {
        if (fill == null) return;
        RectTransform rect = fill.rectTransform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = new Vector2(Mathf.Clamp01(amount), 1f);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static float GhostValue(Image ghost)
    {
        return ghost != null ? ghost.rectTransform.anchorMax.x : 0f;
    }

    /// <summary>Track + a fill and a pale trailing "ghost" fill, both sized by anchors.</summary>
    private static Image CreateBar(Transform parent, string name, Vector2 anchoredPosition, Vector2 size,
        Color trackColor, Color fillColor, out GameObject holder, out Image ghost)
    {
        holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;

        Image track = holder.AddComponent<Image>();
        track.color = trackColor;
        RectTransform trackRect = track.rectTransform;
        trackRect.anchorMin = new Vector2(0f, 0f);
        trackRect.anchorMax = new Vector2(0f, 0f);
        trackRect.pivot = new Vector2(0f, 0f);
        trackRect.anchoredPosition = anchoredPosition;
        trackRect.sizeDelta = size;

        // Ghost first so the real fill draws on top of it
        GameObject ghostObj = new GameObject(name + "_Ghost");
        ghostObj.transform.SetParent(holder.transform, false);
        ghostObj.layer = holder.layer;
        ghost = ghostObj.AddComponent<Image>();
        ghost.color = new Color(1f, 1f, 1f, 0.28f);
        ghost.raycastTarget = false;
        SetFill(ghost, 1f);

        GameObject fillObj = new GameObject(name + "_Fill");
        fillObj.transform.SetParent(holder.transform, false);
        fillObj.layer = holder.layer;

        Image fill = fillObj.AddComponent<Image>();
        fill.color = fillColor;
        fill.raycastTarget = false;
        SetFill(fill, 1f);

        return fill;
    }
}
