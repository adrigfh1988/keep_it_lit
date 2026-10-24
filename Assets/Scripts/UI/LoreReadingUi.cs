using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// F74: the panel a LoreNote opens. Deliberately not a menu - it does not touch Time.timeScale and
/// never calls PlayerLock (see CLAUDE.md's cursor-fight gotcha), so the player can keep walking (and
/// even get caught) while reading. Closing goes through the same E-with-no-focus path Lantern uses
/// (decision: "coordinate with PlayerInteractor rather than adding a second E reader") - LoreNote sets
/// itself up so this class becomes PlayerInteractor.Fallback while open, and this class's own
/// IInteractable.Interact is what closes it. It also closes itself the moment the hunter starts a
/// chase, so a reading player is never blindsided by a scripted panel between them and what is chasing.
/// </summary>
public class LoreReadingUi : MonoBehaviour, IInteractable
{
    private GameObject _holder;
    private TextMeshProUGUI _text;
    private AIFollower _hunter;
    private PlayerInteractor _activeInteractor;
    private RectTransform _panelRect;

    /// <summary>True while the panel is showing.</summary>
    public bool IsOpen { get; private set; }

    /// <summary>Called once by MazeGenerator.SetUpAtmosphere. May be called again on a re-Configure - unsubscribes first so it never double-subscribes.</summary>
    public void Configure(AIFollower hunter)
    {
        if (_hunter != null) _hunter.ChaseStateChanged -= HandleChaseStateChanged;
        _hunter = hunter;
        if (_hunter != null) _hunter.ChaseStateChanged += HandleChaseStateChanged;
    }

    private void Start()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        _holder = RuntimeUi.CreatePanel(canvas.transform, "LoreReadingPanel", new Color(0.02f, 0.02f, 0.02f, 0.78f));
        RectTransform rect = _holder.GetComponent<RectTransform>();
        _panelRect = rect;
        RuntimeUi.Place(rect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 300f));

        _text = RuntimeUi.CreateText(_holder.transform, "NoteText", "", 28f, new Color(0.92f, 0.9f, 0.85f));
        _text.margin = new Vector4(36f, 28f, 36f, 28f);
        RuntimeUi.Stretch(_text.rectTransform);

        _holder.SetActive(false);
    }

    /// <summary>Called by LoreNote.Interact. `interactor` is handed straight through so this can register/clear itself as its Fallback.</summary>
    public void Show(string text, PlayerInteractor interactor)
    {
        if (_holder == null) return;

        // S9 (plan section 6): the panel size follows the MEASURED text, not a character count - a French or German note is longer than the
        // English one it replaces. F93: the patient case files are longer than a note - a taller panel and a slightly smaller type.
        _text.enableAutoSizing = false;
        _text.fontSize = 28f;
        _text.text = text;
        const float smallWidth = 760f, smallHeight = 300f;
        float needed = _text.GetPreferredValues(text ?? "", smallWidth - _text.margin.x - _text.margin.z, 0f).y + _text.margin.y + _text.margin.w;
        bool longText = needed > smallHeight;
        if (_panelRect != null) _panelRect.sizeDelta = longText ? new Vector2(860f, 480f) : new Vector2(smallWidth, smallHeight);
        _text.fontSize = longText ? 26f : 28f;
        if (longText)
        {
            // Still too long for the tall panel (a long file in a long language): shrink to fit rather than clip.
            _text.fontSizeMax = 26f;
            _text.fontSizeMin = 18f;
            _text.enableAutoSizing = true;
        }
        _holder.SetActive(true);
        IsOpen = true;

        _activeInteractor = interactor;
        if (_activeInteractor != null) _activeInteractor.Fallback = this;
    }

    public void Close()
    {
        if (!IsOpen) return;

        IsOpen = false;
        if (_holder != null) _holder.SetActive(false);

        if (_activeInteractor != null && ReferenceEquals(_activeInteractor.Fallback, this))
        {
            _activeInteractor.Fallback = null;
        }
        _activeInteractor = null;
    }

    private void HandleChaseStateChanged(bool chasing)
    {
        if (chasing) Close();
    }

    private void OnDestroy()
    {
        if (_hunter != null) _hunter.ChaseStateChanged -= HandleChaseStateChanged;
    }

    // ---------------------------------------------------------------- IInteractable (Fallback only - never Probed)

    string IInteractable.Prompt => L10n.T("hud.prompt.close");
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => IsOpen;
    void IInteractable.Interact(PlayerInteractor who) => Close();
    void IInteractable.OnFocus(bool focused) { }
}
