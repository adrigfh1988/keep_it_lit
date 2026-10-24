using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// F119 D8: REMAP CONTROLS - the screen behind Settings > CONTROLS that rebinds the keyboard / mouse and gamepad bindings of the verbs in
/// <see cref="Rows"/> with <c>PerformInteractiveRebinding</c>. Rebinds go to the <see cref="GameInput"/> clone; every change is saved as
/// <see cref="GameSettings.InputOverrides"/> and re-applied through <see cref="GameInput.ApplyOverrides"/> (which also feeds the PlayerInput source
/// asset, so Move and Sprint follow, and refreshes every glyph). Same lazy shared-instance pattern as <see cref="SettingsMenu"/>: built by
/// <see cref="Show"/>, laid over the settings panel, closed by Back / Esc / pad B. The press that starts a rebind opens it one frame late (so the
/// click or A that chose the cell is never captured), UI navigation is suspended while listening (the pad A you bind must not also Submit the cell),
/// and <see cref="BlocksBack"/> keeps Settings from also reading the Esc that cancels it (the same class of bug as <c>SettingsMenu.BlocksPause</c>).
/// Adding a rebindable verb is one line in <see cref="Rows"/> plus an <c>ui.rebind.action.*</c> key (recipe: docs/systems.md, "Input (F119)").
/// </summary>
public class RebindPanel : MonoBehaviour
{
    /// <summary>One rebindable verb. <c>Part</c> is null, or "up"/"down"/"left"/"right" for a part of Move's keyboard composite (gamepad column fixed: the stick).</summary>
    private struct Row
    {
        public GameAction Action;
        public string LabelKey;
        public string Part;

        public Row(GameAction action, string labelKey, string part = null) { Action = action; LabelKey = labelKey; Part = part; }
    }

    private static readonly Row[] Rows =
    {
        new Row(GameAction.Move, "ui.rebind.action.move_up", "up"),
        new Row(GameAction.Move, "ui.rebind.action.move_down", "down"),
        new Row(GameAction.Move, "ui.rebind.action.move_left", "left"),
        new Row(GameAction.Move, "ui.rebind.action.move_right", "right"),
        new Row(GameAction.Sprint, "ui.rebind.action.sprint"),
        new Row(GameAction.Torch, "ui.rebind.action.torch"),
        new Row(GameAction.Focus, "ui.rebind.action.focus"),
        new Row(GameAction.Interact, "ui.rebind.action.interact"),
        new Row(GameAction.Throw, "ui.rebind.action.throw"),
        new Row(GameAction.Item1, "ui.rebind.action.item1"),
        new Row(GameAction.Item2, "ui.rebind.action.item2"),
        new Row(GameAction.Map, "ui.rebind.action.map"),
    };

    private const float RowHeight = 55f, CellHeight = 48f, CellWidth = 360f, LabelWidth = 600f;
    private const float KeyX = 120f, PadX = 520f, LabelX = -400f;

    private static readonly Color ButtonIdle = UiTheme.ButtonIdle;
    private static readonly Color ButtonHover = UiTheme.ButtonHover;
    private static readonly Color Accent = UiTheme.Accent;
    private static readonly Color Dim = UiTheme.Dim;

    public static RebindPanel Active { get; private set; }

    /// <summary>True while the panel is up, while a rebind is listening, and for the rest of the frame either ended in: the Esc / B / Start that cancels or closes
    /// it must not also close Settings.</summary>
    public static bool BlocksBack => Active != null && (Active._open || Active._listening || Active._endedFrame == Time.frameCount || Active._closedFrame == Time.frameCount);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Active = null;

    /// <summary>Opens the shared panel; <paramref name="returnTo"/> is selected again when it closes (the REMAP row's button).</summary>
    public static void Show(Selectable returnTo = null)
    {
        if (Active == null)
        {
            GameObject holder = new GameObject("RebindPanel");
            holder.AddComponent<RebindPanel>();
        }
        Active.Open(returnTo);
    }

    private GameObject _root;
    private RectTransform _content;
    private ScrollRect _scroll;
    private GameObject _overlay;
    private TextMeshProUGUI _overlayText;
    private TextMeshProUGUI _notice;
    private float _noticeUntil;
    private readonly List<Button> _keyCells = new List<Button>();
    private readonly List<Button> _padCells = new List<Button>();
    private Selectable _returnTo;
    private bool _open, _listening, _stale;
    private int _endedFrame = -1, _closedFrame = -1;
    private int _navRestoreFrame = -1;
    private int _glyphVersion = -1;
    private GameObject _lastSelected;
    private InputActionRebindingExtensions.RebindingOperation _op;
    private InputActionMap _opMap;
    private bool _navWasSending = true;

    public bool IsOpen => _open;
    public bool IsListening => _listening;
    /// <summary>Test read-back: the cells of row <paramref name="i"/>.</summary>
    public Button KeyCell(int i) => i >= 0 && i < _keyCells.Count ? _keyCells[i] : null;
    public Button PadCell(int i) => i >= 0 && i < _padCells.Count ? _padCells[i] : null;
    public int RowCount => Rows.Length;

    private void Awake()
    {
        Active = this;
        L10n.Changed += OnLanguageChanged;
    }

    private void OnLanguageChanged() => _stale = true;

    private void OnDestroy()
    {
        L10n.Changed -= OnLanguageChanged;
        AbortRebind();
        UiModalStack.Pop(this);
        if (Active == this) Active = null;
    }

    // ---------------------------------------------------------------- open / close

    private void Open(Selectable returnTo)
    {
        RuntimeUi.EnsureEventSystem();
        _returnTo = returnTo;
        if (_root != null && _stale) DestroyPanel();
        if (_root == null) Build();
        _open = true;
        UiModalStack.Push(this);
        _root.SetActive(true);
        _root.transform.SetAsLastSibling();
        _overlay.SetActive(false);
        PlayerLock.SetCursorFree(true);
        RefreshCells();
        _glyphVersion = GameInput.Version;
        if (EventSystem.current != null && _keyCells.Count > 0) EventSystem.current.SetSelectedGameObject(_keyCells[0].gameObject);
    }

    public void Close()
    {
        if (!_open || _listening) return;
        _open = false;
        _closedFrame = Time.frameCount;
        UiModalStack.Pop(this);
        if (_root != null) _root.SetActive(false);
        EventSystem es = EventSystem.current;
        if (es != null) es.SetSelectedGameObject(_returnTo != null && _returnTo.gameObject.activeInHierarchy ? _returnTo.gameObject : null);
    }

    private void DestroyPanel()
    {
        if (_root != null) Destroy(_root);
        _root = null;
        _content = null;
        _scroll = null;
        _overlay = null;
        _overlayText = null;
        _notice = null;
        _keyCells.Clear();
        _padCells.Clear();
        _stale = false;
    }

    private void Update()
    {
        // Navigation comes back the frame AFTER a rebind ended, so the A that was just bound does not also Submit a cell.
        if (_navRestoreFrame >= 0 && Time.frameCount >= _navRestoreFrame)
        {
            _navRestoreFrame = -1;
            if (EventSystem.current != null) EventSystem.current.sendNavigationEvents = _navWasSending;
        }

        if (!_open) return;
        PlayerLock.SetCursorFree(true); // every frame: focus changes re-lock it
        if (_stale && !_listening)
        {
            DestroyPanel();
            Open(_returnTo);
            return;
        }

        if (_notice != null && _notice.gameObject.activeSelf && Time.unscaledTime > _noticeUntil) _notice.gameObject.SetActive(false);

        if (_glyphVersion != GameInput.Version)
        {
            _glyphVersion = GameInput.Version;
            RefreshCells();
        }

        if (_listening) return;

        if (_endedFrame != Time.frameCount && GameInput.Pressed(GameAction.MenuCancel))
        {
            Close();
            return;
        }

        MenuFocus.Keep(_root, _keyCells.Count > 0 ? _keyCells[0] : null);
        KeepSelectionVisible();
    }

    // ---------------------------------------------------------------- build

    private void Build()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        _root = RuntimeUi.CreatePanel(canvas.transform, "RebindScreen", new Color(0.02f, 0.02f, 0.03f, 1f));

        TextMeshProUGUI title = RuntimeUi.CreateText(_root.transform, "Title", L10n.T("ui.rebind.title"), 76f, Color.white);
        title.fontStyle = FontStyles.Bold;
        title.characterSpacing = 10f;
        RuntimeUi.Place(title.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -90f), new Vector2(1200f, 100f));

        AddHeader("ui.rebind.col.keyboard", KeyX);
        AddHeader("ui.rebind.col.gamepad", PadX);

        // The list: a clipped viewport over a tall content rectangle.
        GameObject viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(_root.transform, false);
        viewport.layer = _root.layer;
        RectTransform vr = (RectTransform)viewport.transform;
        RuntimeUi.Place(vr, new Vector2(0.5f, 1f), new Vector2(0f, -(250f + 330f)), new Vector2(1600f, 660f));
        Image hit = viewport.AddComponent<Image>();   // raycast catcher so the wheel scrolls anywhere over the list
        hit.color = new Color(0f, 0f, 0f, 0f);
        viewport.AddComponent<RectMask2D>();

        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        content.layer = _root.layer;
        _content = (RectTransform)content.transform;
        _content.anchorMin = _content.anchorMax = new Vector2(0.5f, 1f);
        _content.pivot = new Vector2(0.5f, 1f);
        _content.anchoredPosition = Vector2.zero;
        _content.sizeDelta = new Vector2(1500f, Rows.Length * RowHeight);

        _scroll = viewport.AddComponent<ScrollRect>();
        _scroll.viewport = vr;
        _scroll.content = _content;
        _scroll.horizontal = false;
        _scroll.vertical = true;
        _scroll.movementType = ScrollRect.MovementType.Clamped;
        _scroll.scrollSensitivity = 45f;

        for (int i = 0; i < Rows.Length; i++)
        {
            Row row = Rows[i];
            float y = -(i * RowHeight + RowHeight * 0.5f);

            TextMeshProUGUI label = RuntimeUi.CreateText(_content, row.LabelKey + " Label", L10n.T(row.LabelKey), 32f, Dim);
            label.alignment = TextAlignmentOptions.Left;
            label.textWrappingMode = TextWrappingModes.NoWrap;
            RuntimeUi.AutoFit(label, 0.65f);
            RuntimeUi.Place(label.rectTransform, new Vector2(0.5f, 1f), new Vector2(LabelX, y), new Vector2(LabelWidth, CellHeight));

            Button key = MakeCell(row.LabelKey + " Key", KeyX, y);
            int rowIndex = i;
            key.onClick.AddListener(() => StartCoroutine(BeginRebind(rowIndex, false)));
            _keyCells.Add(key);

            Button pad = MakeCell(row.LabelKey + " Pad", PadX, y);
            if (row.Part != null) pad.interactable = false;   // Move's gamepad binding is the left stick, fixed
            else pad.onClick.AddListener(() => StartCoroutine(BeginRebind(rowIndex, true)));
            _padCells.Add(pad);
        }

        Button reset = RuntimeUi.CreateButton(_root.transform, L10n.T("ui.rebind.reset_all"), Vector2.zero, new Vector2(440f, 76f), 32f, ButtonIdle, ButtonHover, Color.white);
        RuntimeUi.Place((RectTransform)reset.transform, new Vector2(0.5f, 0f), new Vector2(-240f, 70f), new Vector2(440f, 76f));
        reset.onClick.AddListener(ResetAll);
        RuntimeUi.AutoFit(reset.GetComponentInChildren<TMP_Text>(), 0.65f);
        Button back = RuntimeUi.CreateButton(_root.transform, L10n.T("ui.settings.back"), Vector2.zero, new Vector2(440f, 76f), 36f, ButtonIdle, ButtonHover, Color.white);
        RuntimeUi.Place((RectTransform)back.transform, new Vector2(0.5f, 0f), new Vector2(240f, 70f), new Vector2(440f, 76f));
        back.onClick.AddListener(Close);

        _notice = RuntimeUi.CreateText(_root.transform, "Notice", "", 32f, new Color(0.95f, 0.75f, 0.3f));
        RuntimeUi.Place(_notice.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 140f), new Vector2(1500f, 50f));
        _notice.gameObject.SetActive(false);

        // Listening overlay: swallows every click while a rebind waits for its key.
        _overlay = RuntimeUi.CreatePanel(_root.transform, "Listening", new Color(0f, 0f, 0f, 0.82f));
        _overlay.GetComponent<Image>().raycastTarget = true;
        _overlayText = RuntimeUi.CreateText(_overlay.transform, "Text", "", 44f, Color.white);
        RuntimeUi.AutoFit(_overlayText, 0.6f);
        RuntimeUi.Place(_overlayText.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1600f, 120f));
        _overlay.SetActive(false);

        _root.SetActive(false);
    }

    private void AddHeader(string key, float x)
    {
        TextMeshProUGUI h = RuntimeUi.CreateText(_root.transform, key, L10n.T(key), 30f, Accent);
        h.fontStyle = FontStyles.Bold;
        h.characterSpacing = 4f;
        RuntimeUi.AutoFit(h, 0.6f);
        RuntimeUi.Place(h.rectTransform, new Vector2(0.5f, 1f), new Vector2(x, -205f), new Vector2(CellWidth, 44f));
    }

    private Button MakeCell(string name, float x, float y)
    {
        Button b = RuntimeUi.CreateButton(_content, "--", Vector2.zero, new Vector2(CellWidth, CellHeight), 30f, ButtonIdle, ButtonHover, Color.white);
        b.gameObject.name = name;
        RuntimeUi.Place((RectTransform)b.transform, new Vector2(0.5f, 1f), new Vector2(x, y), new Vector2(CellWidth, CellHeight));
        RuntimeUi.AutoFit(b.GetComponentInChildren<TMP_Text>(), 0.6f);
        ColorBlock colors = b.colors;
        colors.disabledColor = new Color(0.10f, 0.10f, 0.12f, 0.55f);   // the fixed cells (Move's stick) read as dim, not as a light button
        b.colors = colors;
        return b;
    }

    /// <summary>Re-reads every cell's text from the live bindings (after a rebind, a reset, or the device / language changing).</summary>
    private void RefreshCells()
    {
        for (int i = 0; i < Rows.Length; i++)
        {
            Row row = Rows[i];
            InputAction action = GameInput.Action(row.Action);
            string key = L10n.T("ui.glyph.unbound");
            string pad = L10n.T("ui.glyph.unbound");
            if (action != null)
            {
                int k = KeyboardIndex(action, row);
                if (k >= 0) key = InputGlyphs.BindingLabel(action, k);
                if (row.Part != null) pad = L10n.T("ui.glyph.lstick");
                else
                {
                    int p = InputGlyphs.FirstBinding(action, "Gamepad");
                    if (p >= 0) pad = InputGlyphs.BindingLabel(action, p);
                }
            }
            SetCell(_keyCells[i], key);
            SetCell(_padCells[i], pad);
        }
    }

    private static void SetCell(Button b, string text)
    {
        TMP_Text t = b != null ? b.GetComponentInChildren<TMP_Text>() : null;
        if (t != null) t.text = text;
    }

    /// <summary>The binding the keyboard cell edits: the first KeyboardMouse binding, or for Move the named part of its first composite.</summary>
    private static int KeyboardIndex(InputAction action, Row row)
    {
        if (row.Part == null) return InputGlyphs.FirstBinding(action, "KeyboardMouse");
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding b = action.bindings[i];
            if (b.isPartOfComposite && b.name == row.Part && InputGlyphs.HasGroup(b, "KeyboardMouse")) return i;
        }
        return -1;
    }

    // ---------------------------------------------------------------- rebinding

    private IEnumerator BeginRebind(int rowIndex, bool pad)
    {
        if (_listening || !_open) yield break;
        _listening = true;
        EventSystem es = EventSystem.current;
        if (es != null)
        {
            _navWasSending = es.sendNavigationEvents;
            es.sendNavigationEvents = false;    // the pad A being bound must not also Submit a cell
        }
        _navRestoreFrame = -1;
        _overlay.SetActive(true);
        _overlay.transform.SetAsLastSibling();
        _overlayText.text = L10n.T("ui.rebind.listening", ("cancel", pad ? InputGlyphs.PadControlLabel("start") : "ESC"));

        // One frame late: the click / A that chose this cell is over before anything listens.
        yield return null;

        Row row = Rows[rowIndex];
        InputAction action = GameInput.Action(row.Action);
        int index = action != null ? (pad ? InputGlyphs.FirstBinding(action, "Gamepad") : KeyboardIndex(action, row)) : -1;
        if (index < 0)
        {
            FinishListening();
            yield break;
        }

        _opMap = action.actionMap;
        _opMap.Disable();   // an enabled action cannot be rebound
        string oldPath = action.bindings[index].effectivePath;

        InputActionRebindingExtensions.RebindingOperation op = action.PerformInteractiveRebinding(index)
            .WithExpectedControlType("Button")
            .WithMagnitudeHavingToBeGreaterThan(0.5f)
            .OnMatchWaitForAnother(0.1f);
        if (pad)
        {
            op = op.WithControlsHavingToMatchPath("<Gamepad>")
                   .WithControlsExcluding("<Gamepad>/leftStick").WithControlsExcluding("<Gamepad>/rightStick")
                   .WithCancelingThrough("<Gamepad>/start");
        }
        else
        {
            op = op.WithControlsHavingToMatchPath("<Keyboard>").WithControlsHavingToMatchPath("<Mouse>")
                   .WithControlsExcluding("<Mouse>/position").WithControlsExcluding("<Mouse>/delta").WithControlsExcluding("<Mouse>/scroll")
                   .WithControlsExcluding("<Pointer>/position").WithControlsExcluding("<Pointer>/delta")
                   .WithControlsExcluding("<Keyboard>/anyKey").WithControlsExcluding("<Keyboard>/p")    // P is Pause, fixed
                   .WithCancelingThrough("<Keyboard>/escape");
        }

        op.OnComplete(o =>
        {
            DisposeOp();
            FinishListening();
            AfterRebind(rowIndex, pad, index, oldPath);
        });
        op.OnCancel(o =>
        {
            DisposeOp();
            FinishListening();
        });
        _op = op;
        op.Start();
    }

    private void DisposeOp()
    {
        if (_op != null) { _op.Dispose(); _op = null; }
        if (_opMap != null) { _opMap.Enable(); _opMap = null; }
    }

    private void AbortRebind()
    {
        if (_op != null) { _op.Cancel(); }
        DisposeOp();
    }

    private void FinishListening()
    {
        _listening = false;
        _endedFrame = Time.frameCount;
        _navRestoreFrame = Time.frameCount + 1;
        if (_overlay != null) _overlay.SetActive(false);
        if (_opMap != null) { _opMap.Enable(); _opMap = null; }
    }

    /// <summary>A key or button was chosen: swap it with whatever else held it, save, and push the result into every asset.</summary>
    private void AfterRebind(int rowIndex, bool pad, int index, string oldPath)
    {
        Row row = Rows[rowIndex];
        InputAction action = GameInput.Action(row.Action);
        string newPath = action.bindings[index].effectivePath;

        string swapped = null;
        if (!string.IsNullOrEmpty(newPath) && newPath != oldPath)
        {
            for (int i = 0; i < Rows.Length; i++)
            {
                Row other = Rows[i];
                if (i == rowIndex) continue;
                if (pad && other.Part != null) continue;
                InputAction oa = GameInput.Action(other.Action);
                if (oa == null) continue;
                int oi = pad ? InputGlyphs.FirstBinding(oa, "Gamepad") : KeyboardIndex(oa, other);
                if (oi < 0 || oa.bindings[oi].effectivePath != newPath) continue;
                InputActionRebindingExtensions.ApplyBindingOverride(oa, oi, oldPath);
                swapped = L10n.T(other.LabelKey);
            }
        }

        Commit();
        if (swapped != null) ShowNotice(L10n.T("ui.rebind.swapped", ("action", swapped)));
    }

    /// <summary>Saves the clone's overrides and re-applies them everywhere (the PlayerInput source asset too); every glyph refreshes through the event.</summary>
    private void Commit()
    {
        string json = GameInput.SaveOverrides();
        GameSettings.InputOverrides = json;
        GameInput.ApplyOverrides(json);
        RefreshCells();
        _glyphVersion = GameInput.Version;
    }

    private void ResetAll()
    {
        if (_listening) return;
        GameSettings.InputOverrides = "";
        GameInput.ApplyOverrides("");
        RefreshCells();
        _glyphVersion = GameInput.Version;
    }

    private void ShowNotice(string text)
    {
        if (_notice == null) return;
        _notice.text = text;
        _notice.gameObject.SetActive(true);
        _noticeUntil = Time.unscaledTime + 2.5f;
    }

    // ---------------------------------------------------------------- list scrolling

    /// <summary>A pad moves the selection down the list; the ScrollRect does not follow by itself.</summary>
    private void KeepSelectionVisible()
    {
        EventSystem es = EventSystem.current;
        GameObject sel = es != null ? es.currentSelectedGameObject : null;
        if (sel == null || sel == _lastSelected || _content == null || !sel.transform.IsChildOf(_content)) { _lastSelected = sel; return; }
        _lastSelected = sel;

        RectTransform item = (RectTransform)sel.transform;
        float top = -item.anchoredPosition.y - item.rect.height * 0.5f;   // distance of the cell's top edge from the content top
        float bottom = top + item.rect.height;
        float view = ((RectTransform)_scroll.viewport).rect.height;
        float range = _content.rect.height - view;
        if (range <= 0f) return;
        float offset = (1f - _scroll.verticalNormalizedPosition) * range;
        if (top < offset) offset = top - 6f;
        else if (bottom > offset + view) offset = bottom - view + 6f;
        _scroll.verticalNormalizedPosition = 1f - Mathf.Clamp01(offset / range);
    }
}
