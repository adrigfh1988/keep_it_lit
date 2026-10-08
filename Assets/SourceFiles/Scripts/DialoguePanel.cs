using System;
using System.Collections.Generic;
using StarterAssets;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

/// <summary>
/// F98: the bottom-of-screen conversation panel the hospital's people talk through. One instance lives under AsylumHub/Life/Npcs (so
/// it only exists while the player is in the hub); HubNpc.Interact hands it a list of nodes and an onDone callback.
///
/// Behaviour: the speaker's name (in their colour) over the line, which types out at ~45 characters a second; E / click / the touch
/// INTERACT button / gamepad West skips the typing, then advances. A node with choices shows two buttons instead (keys 1 / 2, the touch
/// ITEM 1 / ITEM 2 buttons, the d-pad up / down, or a click). Esc closes it early (the conversation then counts as not finished: its
/// onDone is NOT called, so a half-heard offer does not start a mission). The player is frozen through PlayerLock for the whole
/// conversation, time keeps running (the hub is a safe place, not a pause), the cursor is freed only while choices are showing, and the
/// PlayerInteractor is switched off so the same E cannot also press whatever is under the crosshair. PauseMenu does not open on the Esc
/// that closes the panel (<see cref="BlocksPause"/>, the same trick as the shop panel's ClosedThisFrame). It closes by itself when
/// the hub is left, a transition starts or a hub pursuer starts after the player.
/// </summary>
public class DialoguePanel : MonoBehaviour
{
    /// <summary>The panel in the scene (null until the hub content has been switched on once). Scene-scoped: cleared in OnDestroy.</summary>
    public static DialoguePanel Current { get; private set; }

    private static int _closedFrame = -10;

    /// <summary>True while a conversation is open.</summary>
    public static bool IsOpen => Current != null && Current._open;

    /// <summary>PauseMenu.CanPause reads this: false pausing while the panel is open or on the very frame an Esc closed it.</summary>
    public static bool BlocksPause => IsOpen || _closedFrame == Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Current = null;
        _closedFrame = -10;
    }

    [Header("Typing")]
    [SerializeField] private float charactersPerSecond = 45f;

    // ---- state
    private bool _open;
    private HubNpc _npc;
    private Action _onDone;
    private readonly Queue<HubDialogue.Node> _queue = new Queue<HubDialogue.Node>();
    private HubDialogue.Node _node;
    private float _typed;
    private int _openFrame;
    private int _total;
    private int _count;

    // ---- ui
    private GameObject _root;
    private TextMeshProUGUI _name;
    private TextMeshProUGUI _line;
    private TextMeshProUGUI _hint;
    private Image _accent;
    private Button[] _choiceButtons;
    private TextMeshProUGUI[] _choiceLabels;
    private PlayerInteractor _interactor;

    private void Awake()
    {
        Current = this;
    }

    private void OnDestroy()
    {
        if (Current == this) Current = null;
        if (_root != null) Destroy(_root);
    }

    private void OnDisable()
    {
        // The hub content switched off (a wing door, the shop door): the conversation is over, nothing is called.
        if (_open) Close(false);
    }

    // ---------------------------------------------------------------- open / close

    /// <summary>Opens a conversation. False (nothing shown) when it cannot open right now - the caller shows its own refusal.</summary>
    public bool Begin(HubNpc npc, HubDialogue.Node[] nodes, Action onDone, int count = 0, int total = 0)
    {
        if (_open || nodes == null || nodes.Length == 0) return false;
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || hub.Player == null || hub.IsTransitioning || hub.Leaving) return false;
        if (Time.timeScale <= 0f) return false;
        if (!EnsureUi()) return false;

        _npc = npc;
        _onDone = onDone;
        _count = count;
        _total = total;
        _queue.Clear();
        foreach (HubDialogue.Node n in nodes) _queue.Enqueue(n);

        _open = true;
        _openFrame = Time.frameCount;
        _interactor = hub.Player.GetComponent<PlayerInteractor>();
        if (_interactor != null) _interactor.enabled = false;
        if (hub.Hud != null) { hub.Hud.SetPrompt(null); hub.Hud.SetFocusPrompt(null); }
        PlayerLock.Freeze(hub.Player, true);
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        if (_npc != null) _npc.BeginTalk();
        Advance();
        return true;
    }

    /// <summary>Closes the panel. `finished` = the player heard it to the end (runs onDone).</summary>
    public void Close(bool finished)
    {
        if (!_open) return;
        _open = false;
        _closedFrame = Time.frameCount;
        if (_root != null) _root.SetActive(false);
        _queue.Clear();
        _node = null;

        AsylumHub hub = AsylumHub.Active;
        if (_interactor != null) _interactor.enabled = true;
        _interactor = null;
        // A transition that closed us (wing door, catch) owns the freeze; otherwise hand the player back.
        if (hub != null && hub.Player != null && !hub.IsTransitioning && !hub.Leaving)
        {
            PlayerLock.Freeze(hub.Player, false);
            PlayerLock.SetCursorFree(false);
        }
        if (_npc != null) _npc.EndTalk();
        Action done = _onDone;
        _npc = null;
        _onDone = null;
        if (finished && done != null) done();
    }

    // ---------------------------------------------------------------- flow

    private void Advance()
    {
        if (_queue.Count == 0) { Close(true); return; }
        _node = _queue.Dequeue();
        _typed = 0f;
        string text = _node.text.Replace("{n}", _count.ToString()).Replace("{total}", _total.ToString());
        _line.text = text;
        _line.maxVisibleCharacters = 0;
        _line.ForceMeshUpdate();
        _name.text = _node.speaker;
        _name.color = HubDialogue.ColourOf(_node.speaker);
        _accent.color = new Color(_name.color.r, _name.color.g, _name.color.b, 0.55f);

        bool choices = _node.HasChoices;
        for (int i = 0; i < _choiceButtons.Length; i++)
        {
            bool show = choices && i < _node.choices.Length;
            _choiceButtons[i].gameObject.SetActive(show);
            if (show) _choiceLabels[i].text = $"{TouchInput.Key((i + 1).ToString(), (i + 1).ToString())}   {_node.choices[i].label}";
        }
        UpdateHint();
        if (_node.fx == "whisper") GameAudio.Play2D(GameAudio.Library.whisperEcho, 0.3f);
    }

    private void Choose(int index)
    {
        if (_node == null || !_node.HasChoices || index < 0 || index >= _node.choices.Length) return;
        HubDialogue.Node[] reply = _node.choices[index].reply;
        // The reply goes in front of what is left of the conversation.
        List<HubDialogue.Node> rest = new List<HubDialogue.Node>(_queue);
        _queue.Clear();
        if (reply != null) foreach (HubDialogue.Node n in reply) _queue.Enqueue(n);
        foreach (HubDialogue.Node n in rest) _queue.Enqueue(n);
        Advance();
    }

    private void UpdateHint()
    {
        if (_hint == null) return;
        bool revealed = _line.maxVisibleCharacters >= _line.textInfo.characterCount && _line.textInfo.characterCount > 0;
        if (_node != null && _node.HasChoices) _hint.text = revealed ? "ESC  LEAVE" : "";
        else _hint.text = $"{TouchInput.Key("E", "TAP")}  {(_queue.Count == 0 ? "END" : "CONTINUE")}    ·    {TouchInput.Key("ESC", "BACK")}  LEAVE";
    }

    // ---------------------------------------------------------------- update

    private void Update()
    {
        if (!_open) return;
        AsylumHub hub = AsylumHub.Active;
        if (hub == null || !hub.ContentActive || hub.Leaving || hub.IsTransitioning || hub.PursuerAfterPlayer())
        {
            Close(false);
            return;
        }
        if (Time.timeScale <= 0f) return;

        // The interactor is off, but the frame that opened the panel may have left its "E TALK" prompt up: keep the line clear.
        if (hub.Hud != null) { hub.Hud.SetFocusPrompt(null); hub.Hud.SetPrompt(null); }

        // The cursor is only free while there is something to click; every frame, never once (the cursor fight).
        bool choices = _node != null && _node.HasChoices;
        if (choices) PlayerLock.SetCursorFree(true);
        else PlayerLock.SetCursorFree(false);

        // The player faces whoever is speaking.
        ThirdPersonController controller = hub.Controller;
        if (controller != null && _npc != null && !_npc.IgnoresGaze) controller.LookAt(_npc.HeadPosition, 1f - Mathf.Exp(-5f * Time.deltaTime));

        // typing
        if (_node != null)
        {
            _typed += charactersPerSecond * Time.deltaTime;
            int visible = Mathf.Min(_line.textInfo.characterCount > 0 ? _line.textInfo.characterCount : _node.text.Length, Mathf.FloorToInt(_typed));
            if (_line.maxVisibleCharacters != visible)
            {
                _line.maxVisibleCharacters = visible;
                UpdateHint();
            }
        }

        if (Time.frameCount == _openFrame) return; // the E that opened it must not also advance it

        ReadInput(out bool confirm, out int pick, out bool leave);
        if (leave) { Close(false); return; }
        if (_node == null) return;

        bool fullyTyped = _line.textInfo.characterCount > 0 && _line.maxVisibleCharacters >= _line.textInfo.characterCount;
        if (choices)
        {
            if (fullyTyped && pick >= 0) Choose(pick);
            else if (!fullyTyped && (confirm || pick >= 0)) _typed = 9999f;
            return;
        }
        if (confirm)
        {
            if (!fullyTyped) _typed = 9999f;
            else Advance();
        }
    }

    private static void ReadInput(out bool confirm, out int pick, out bool leave)
    {
        confirm = false;
        pick = -1;
        leave = false;
#if ENABLE_INPUT_SYSTEM
        Keyboard kb = Keyboard.current;
        Gamepad pad = Gamepad.current;
        if (kb != null)
        {
            confirm |= kb.eKey.wasPressedThisFrame || kb.spaceKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame;
            if (kb.digit1Key.wasPressedThisFrame || kb.numpad1Key.wasPressedThisFrame) pick = 0;
            if (kb.digit2Key.wasPressedThisFrame || kb.numpad2Key.wasPressedThisFrame) pick = 1;
            leave |= kb.escapeKey.wasPressedThisFrame;
        }
        if (pad != null)
        {
            confirm |= pad.buttonWest.wasPressedThisFrame || pad.buttonSouth.wasPressedThisFrame;
            if (pad.dpad.up.wasPressedThisFrame) pick = 0;
            if (pad.dpad.down.wasPressedThisFrame) pick = 1;
            leave |= pad.buttonEast.wasPressedThisFrame;
        }
        Mouse mouse = Mouse.current;
        if (mouse != null) confirm |= mouse.leftButton.wasPressedThisFrame;
#endif
        // F76: touch. INTERACT advances, ITEM 1 / ITEM 2 pick, PAUSE leaves (the pause button must not open the pause menu here: BlocksPause).
        confirm |= TouchInput.Pressed(TouchInput.TouchAction.Interact);
        if (TouchInput.Pressed(TouchInput.TouchAction.Item1)) pick = 0;
        if (TouchInput.Pressed(TouchInput.TouchAction.Item2)) pick = 1;
        leave |= TouchInput.Pressed(TouchInput.TouchAction.Pause);
    }

    // ---------------------------------------------------------------- ui

    private static readonly Color PanelColour = new Color(0.025f, 0.025f, 0.03f, 0.9f);
    private static readonly Color ButtonIdle = new Color(0.12f, 0.115f, 0.11f, 0.95f);
    private static readonly Color ButtonHover = new Color(0.26f, 0.22f, 0.15f, 1f);
    private static readonly Color TextColour = new Color(0.92f, 0.9f, 0.85f);

    private bool EnsureUi()
    {
        if (_root != null) return true;
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return false;
        RuntimeUi.EnsureEventSystem();

        _root = RuntimeUi.CreatePanel(canvas.transform, "DialoguePanel", PanelColour);
        RectTransform rect = _root.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0f);
        rect.pivot = new Vector2(0.5f, 0f);
        rect.anchoredPosition = new Vector2(0f, 36f);
        rect.sizeDelta = new Vector2(1180f, 270f);
        _root.GetComponent<Image>().raycastTarget = false;

        _name = RuntimeUi.CreateText(_root.transform, "Speaker", "", 26f, TextColour);
        _name.alignment = TextAlignmentOptions.TopLeft;
        _name.fontStyle = FontStyles.Bold;
        _name.characterSpacing = 8f;
        AnchorTopLeft(_name.rectTransform, new Vector2(40f, -22f), new Vector2(900f, 36f));

        GameObject accentGo = RuntimeUi.CreatePanel(_root.transform, "Accent", Color.white);
        _accent = accentGo.GetComponent<Image>();
        _accent.raycastTarget = false;
        RectTransform ar = accentGo.GetComponent<RectTransform>();
        ar.anchorMin = new Vector2(0f, 1f);
        ar.anchorMax = new Vector2(1f, 1f);
        ar.pivot = new Vector2(0.5f, 1f);
        ar.offsetMin = new Vector2(40f, -62f);
        ar.offsetMax = new Vector2(-40f, -60f);

        _line = RuntimeUi.CreateText(_root.transform, "Line", "", 34f, TextColour);
        _line.alignment = TextAlignmentOptions.TopLeft;
        _line.textWrappingMode = TextWrappingModes.Normal;
        _line.overflowMode = TextOverflowModes.Overflow;
        RectTransform lr = _line.rectTransform;
        lr.anchorMin = new Vector2(0f, 0f);
        lr.anchorMax = new Vector2(1f, 1f);
        lr.offsetMin = new Vector2(40f, 74f);
        lr.offsetMax = new Vector2(-40f, -76f);

        _hint = RuntimeUi.CreateText(_root.transform, "Hint", "", 20f, new Color(0.6f, 0.6f, 0.6f));
        _hint.alignment = TextAlignmentOptions.BottomRight;
        RectTransform hr = _hint.rectTransform;
        hr.anchorMin = hr.anchorMax = new Vector2(1f, 0f);
        hr.pivot = new Vector2(1f, 0f);
        hr.anchoredPosition = new Vector2(-34f, 14f);
        hr.sizeDelta = new Vector2(760f, 30f);

        _choiceButtons = new Button[2];
        _choiceLabels = new TextMeshProUGUI[2];
        for (int i = 0; i < 2; i++)
        {
            int index = i;
            Button b = RuntimeUi.CreateButton(_root.transform, "Choice" + (i + 1), new Vector2(i == 0 ? -285f : 285f, 62f), new Vector2(540f, 58f), 26f, ButtonIdle, ButtonHover, TextColour);
            b.onClick.AddListener(() => { if (_open && _node != null && _node.HasChoices) Choose(index); });
            _choiceButtons[i] = b;
            _choiceLabels[i] = b.GetComponentInChildren<TextMeshProUGUI>();
            _choiceLabels[i].alignment = TextAlignmentOptions.Left;
            _choiceLabels[i].margin = new Vector4(22f, 0f, 14f, 0f);
            b.gameObject.SetActive(false);
        }

        _root.SetActive(false);
        return true;
    }

    private static void AnchorTopLeft(RectTransform rect, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }
}
