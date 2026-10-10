using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The objective card, shared by the Intake (IntakeRoom) and the asylum hub (HubObjectiveCard). A dark translucent card top-centre,
/// an amber bar on its left edge, a small grey OBJECTIVE label and step counter, the goal in caps, a row of key-cap chips (or, with
/// no keys, a warm second line such as an item count), and a tick drawn from two bars on completion. Optionally a compact version:
/// the same goal and second line on a small card top-left, which the full card collapses into once it has been read.
/// Built in code like all UI here; the owner decides when it shows (Set / Live, then the Fade / Tick / Collapse coroutines).
/// </summary>
public class ObjectiveCard : MonoBehaviour
{
    public static readonly Color Amber = new Color(1f, 0.72f, 0.42f);
    private static readonly Color Green = new Color(0.25f, 1f, 0.7f);
    private static readonly Color Grey = new Color(0.75f, 0.75f, 0.78f);
    private static readonly Color SubColour = new Color(0.79f, 0.71f, 0.54f);
    private static readonly Color CardColour = new Color(0.02f, 0.02f, 0.03f, 0.6f);

    private const float CardWidth = 780f;
    private const float CardHeight = 176f;
    private const float CardHeightNoRow = 116f;
    private const float CardTop = 36f;

    // The compact card sits under the HUD's floor label, where the hub's old objective line was.
    private const float CompactWidth = 540f;
    private const float CompactLeft = 38f;
    private const float CompactTop = 112f;

    private bool _shrinkWhenEmpty;

    private RectTransform _card;
    private CanvasGroup _cardGroup;
    private TextMeshProUGUI _goalText;
    private TextMeshProUGUI _counter;
    private TextMeshProUGUI _subText;
    private RectTransform _chipRow;
    private GameObject _tick;
    private string _goal = "";
    private string _keys = "";
    private string _sub = "";

    private RectTransform _compact;
    private CanvasGroup _compactGroup;
    private TextMeshProUGUI _compactGoal;
    private TextMeshProUGUI _compactSub;

    public float FullAlpha => _cardGroup != null ? _cardGroup.alpha : 0f;
    public float CompactAlpha => _compactGroup != null ? _compactGroup.alpha : 0f;

    /// <summary>
    /// Builds the card on a new full-screen child of <paramref name="parent"/> (so it draws where that child sits in the sibling
    /// order). compact = also build the small top-left card; shrinkWhenEmpty = a card with neither keys nor a second line drops its
    /// empty bottom row.
    /// </summary>
    public static ObjectiveCard Create(Transform parent, string name, bool compact, bool shrinkWhenEmpty)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        root.layer = parent.gameObject.layer;
        RuntimeUi.Stretch((RectTransform)root.transform);
        ObjectiveCard card = root.AddComponent<ObjectiveCard>();
        card._shrinkWhenEmpty = shrinkWhenEmpty;
        card.BuildFull();
        if (compact) card.BuildCompact();
        return card;
    }

    // ---------------------------------------------------------------- content

    /// <summary>A new goal: counter, text, keys and second line set instantly, the tick cleared (the card is invisible between goals).</summary>
    public void Set(string counter, string goal, string[] keys = null, string sub = null)
    {
        if (_card == null) return;
        _counter.text = counter ?? "";
        _tick.SetActive(false);
        _goal = null;
        _keys = null;
        _sub = null;
        Live(goal, keys, sub);
    }

    /// <summary>The goal, its keys or the second line can change mid-step: only rebuild what changed.</summary>
    public void Live(string goal, string[] keys = null, string sub = null)
    {
        if (_card == null) return;
        goal = goal ?? "";
        sub = sub ?? "";
        bool layout = false;
        if (goal != _goal)
        {
            _goal = goal;
            _goalText.text = goal;
            FitGoal(goal);
            if (_compactGoal != null) _compactGoal.text = goal;
            layout = true;
        }
        if (sub != _sub)
        {
            _sub = sub;
            layout = true;
        }

        string joined = string.Join("|", keys ?? new string[0]);
        if (joined != _keys)
        {
            _keys = joined;
            layout = true;
            for (int i = _chipRow.childCount - 1; i >= 0; i--) Destroy(_chipRow.GetChild(i).gameObject);
            float x = 0f;
            foreach (string key in keys ?? new string[0])
            {
                // S9 (plan section 6): the chip is as wide as its text measures (a translated key word such as RECHTSKLICK is longer than MOUSE).
                float width = BuildChip(key, x);
                x += width + 14f;
            }
        }

        if (layout) Layout();
    }

    private float _goalExtra;

    /// <summary>S9 (D14): a goal that would shrink below its minimum on one line (a longer translation) wraps onto two lines and the card grows.</summary>
    private void FitGoal(string goal)
    {
        _goalText.enableAutoSizing = false;
        _goalText.textWrappingMode = TextWrappingModes.NoWrap;
        _goalText.fontSize = 42f;
        float single = _goalText.GetPreferredValues(goal).x;
        bool two = single * (26f / 42f) > CardWidth - 130f;
        _goalExtra = two ? 28f : 0f;
        _goalText.textWrappingMode = two ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        _goalText.enableAutoSizing = true;
        _goalText.fontSizeMin = two ? 24f : 26f;
        _goalText.fontSizeMax = two ? 34f : 42f;
        _goalText.rectTransform.sizeDelta = new Vector2(CardWidth - 130f, 56f + _goalExtra);
    }

    /// <summary>The second line shows in the chip row's place when there are no keys; the card and the compact card size to their content.</summary>
    private void Layout()
    {
        bool hasKeys = !string.IsNullOrEmpty(_keys);
        bool hasSub = !string.IsNullOrEmpty(_sub);
        _subText.text = hasKeys ? "" : _sub;
        _subText.gameObject.SetActive(!hasKeys && hasSub);
        bool row = hasKeys || hasSub;
        _card.sizeDelta = new Vector2(CardWidth, (row || !_shrinkWhenEmpty ? CardHeight : CardHeightNoRow) + _goalExtra);
        _chipRow.anchoredPosition = new Vector2(30f, -112f - _goalExtra);
        _subText.rectTransform.anchoredPosition = new Vector2(30f, -112f - _goalExtra);

        if (_compact == null) return;
        _compactSub.text = _sub;
        _compactSub.gameObject.SetActive(hasSub);
        float inner = CompactWidth - 40f;
        float goalH = Mathf.Ceil(_compactGoal.GetPreferredValues(_goal, inner, 0f).y);
        _compactGoal.rectTransform.sizeDelta = new Vector2(inner, goalH);
        float height = 12f + goalH + 12f;
        if (hasSub)
        {
            float subH = Mathf.Ceil(_compactSub.GetPreferredValues(_sub, inner, 0f).y);
            _compactSub.rectTransform.anchoredPosition = new Vector2(24f, -(12f + goalH + 4f));
            _compactSub.rectTransform.sizeDelta = new Vector2(inner, subH);
            height += 4f + subH;
        }
        _compact.sizeDelta = new Vector2(CompactWidth, height);
    }

    // ---------------------------------------------------------------- animation

    /// <summary>Fades the full card (and slides it 20 px down into place on the way in).</summary>
    public IEnumerator Fade(float from, float to, float seconds)
    {
        if (_cardGroup == null) yield break;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            SetAlpha(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds))));
            yield return null;
        }
        SetAlpha(to);
    }

    public void SetAlpha(float alpha)
    {
        if (_cardGroup == null) return;
        _cardGroup.alpha = alpha;
        _card.anchoredPosition = new Vector2(0f, -CardTop + 20f * (1f - alpha));
    }

    /// <summary>Goal done: the tick shows for about 0.4 s.</summary>
    public IEnumerator Tick()
    {
        if (_tick != null) _tick.SetActive(true);
        yield return new WaitForSecondsRealtime(0.4f);
    }

    /// <summary>Fades the compact card (sliding in from the left on the way in).</summary>
    public IEnumerator FadeCompact(float from, float to, float seconds)
    {
        if (_compactGroup == null) yield break;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            SetCompactAlpha(Mathf.Lerp(from, to, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds))));
            yield return null;
        }
        SetCompactAlpha(to);
    }

    public void SetCompactAlpha(float alpha)
    {
        if (_compactGroup == null) return;
        _compactGroup.alpha = alpha;
        _compact.anchoredPosition = new Vector2(CompactLeft - 24f * (1f - alpha), -CompactTop);
    }

    /// <summary>The full card hands over to the compact one (both at once). Without a compact card it just fades out.</summary>
    public IEnumerator Collapse(float seconds)
    {
        float full = FullAlpha, small = CompactAlpha;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
            SetAlpha(Mathf.Lerp(full, 0f, k));
            if (_compactGroup != null) SetCompactAlpha(Mathf.Lerp(small, 1f, k));
            yield return null;
        }
        SetAlpha(0f);
        if (_compactGroup != null) SetCompactAlpha(1f);
    }

    /// <summary>The compact card hands back to the full one (both at once).</summary>
    public IEnumerator Expand(float seconds)
    {
        float full = FullAlpha, small = CompactAlpha;
        float t = 0f;
        while (t < seconds)
        {
            t += Time.unscaledDeltaTime;
            float k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / seconds));
            SetAlpha(Mathf.Lerp(full, 1f, k));
            SetCompactAlpha(Mathf.Lerp(small, 0f, k));
            yield return null;
        }
        SetAlpha(1f);
        SetCompactAlpha(0f);
    }

    /// <summary>Both cards off at once (a blackout, leaving).</summary>
    public void HideNow()
    {
        SetAlpha(0f);
        SetCompactAlpha(0f);
        if (_tick != null) _tick.SetActive(false);
    }

    // ---------------------------------------------------------------- building

    private static void PlaceTopLeft(RectTransform rect, Vector2 offset, Vector2 size)
    {
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;
    }

    private void BuildFull()
    {
        GameObject card = RuntimeUi.CreatePanel(transform, "ObjectiveCard", CardColour);
        card.GetComponent<Image>().raycastTarget = false;
        _card = card.GetComponent<RectTransform>();
        _card.anchorMin = _card.anchorMax = new Vector2(0.5f, 1f);
        _card.pivot = new Vector2(0.5f, 1f);
        _card.sizeDelta = new Vector2(CardWidth, CardHeight);
        _card.anchoredPosition = new Vector2(0f, -CardTop);
        _cardGroup = card.AddComponent<CanvasGroup>();
        _cardGroup.alpha = 0f;
        _cardGroup.blocksRaycasts = false;
        _cardGroup.interactable = false;

        AmberBar(card.transform);

        TextMeshProUGUI label = RuntimeUi.CreateText(card.transform, "Label", L10n.T("ui.objective.label"), 20f, Grey);
        label.alignment = TextAlignmentOptions.TopLeft;
        label.characterSpacing = 6f;
        PlaceTopLeft(label.rectTransform, new Vector2(30f, -14f), new Vector2(300f, 26f));
        RuntimeUi.Bind(label, "ui.objective.label", 0.6f);

        _counter = RuntimeUi.CreateText(card.transform, "Counter", "", 20f, Grey);
        _counter.alignment = TextAlignmentOptions.TopRight;
        _counter.characterSpacing = 4f;
        PlaceTopLeft(_counter.rectTransform, new Vector2(CardWidth - 30f - 160f, -14f), new Vector2(160f, 26f));

        _goalText = RuntimeUi.CreateText(card.transform, "Goal", "", 42f, Color.white);
        _goalText.alignment = TextAlignmentOptions.TopLeft;
        _goalText.fontStyle = FontStyles.Bold;
        _goalText.characterSpacing = 3f;
        _goalText.textWrappingMode = TextWrappingModes.NoWrap;
        // Long goals shrink rather than run under the tick.
        _goalText.enableAutoSizing = true;
        _goalText.fontSizeMin = 26f;
        _goalText.fontSizeMax = 42f;
        PlaceTopLeft(_goalText.rectTransform, new Vector2(30f, -46f), new Vector2(CardWidth - 130f, 56f));

        GameObject row = new GameObject("Chips", typeof(RectTransform));
        row.transform.SetParent(card.transform, false);
        row.layer = card.layer;
        _chipRow = (RectTransform)row.transform;
        PlaceTopLeft(_chipRow, new Vector2(30f, -112f), new Vector2(CardWidth - 60f, 46f));

        _subText = RuntimeUi.CreateText(card.transform, "Sub", "", 24f, SubColour);
        _subText.alignment = TextAlignmentOptions.MidlineLeft;
        _subText.characterSpacing = 3f;
        _subText.textWrappingMode = TextWrappingModes.NoWrap;
        _subText.enableAutoSizing = true; // a long hint shrinks rather than run off the card
        _subText.fontSizeMin = 16f;
        _subText.fontSizeMax = 24f;
        PlaceTopLeft(_subText.rectTransform, new Vector2(30f, -112f), new Vector2(CardWidth - 60f, 46f));
        _subText.gameObject.SetActive(false);

        // The tick: two bars forming a check mark, at the right of the goal.
        _tick = new GameObject("Tick", typeof(RectTransform));
        _tick.transform.SetParent(card.transform, false);
        _tick.layer = card.layer;
        RectTransform tickRect = (RectTransform)_tick.transform;
        tickRect.anchorMin = tickRect.anchorMax = new Vector2(1f, 1f);
        tickRect.pivot = new Vector2(0.5f, 0.5f);
        tickRect.anchoredPosition = new Vector2(-60f, -92f); // the full-height card's vertical centre, so it stays put when the card shrinks
        tickRect.sizeDelta = new Vector2(60f, 60f);
        MakeTickBar(_tick.transform, new Vector2(-12f, -6f), new Vector2(8f, 22f), 45f);
        MakeTickBar(_tick.transform, new Vector2(7f, 2f), new Vector2(8f, 44f), -45f);
        _tick.SetActive(false);
    }

    private void BuildCompact()
    {
        GameObject card = RuntimeUi.CreatePanel(transform, "ObjectiveCompact", CardColour);
        card.GetComponent<Image>().raycastTarget = false;
        _compact = card.GetComponent<RectTransform>();
        PlaceTopLeft(_compact, new Vector2(CompactLeft, -CompactTop), new Vector2(CompactWidth, 60f));
        _compactGroup = card.AddComponent<CanvasGroup>();
        _compactGroup.alpha = 0f;
        _compactGroup.blocksRaycasts = false;
        _compactGroup.interactable = false;

        AmberBar(card.transform);

        _compactGoal = RuntimeUi.CreateText(card.transform, "Goal", "", 24f, Color.white);
        _compactGoal.alignment = TextAlignmentOptions.TopLeft;
        _compactGoal.fontStyle = FontStyles.Bold;
        _compactGoal.characterSpacing = 2f;
        _compactGoal.textWrappingMode = TextWrappingModes.Normal;
        PlaceTopLeft(_compactGoal.rectTransform, new Vector2(24f, -12f), new Vector2(CompactWidth - 40f, 30f));

        _compactSub = RuntimeUi.CreateText(card.transform, "Sub", "", 20f, SubColour);
        _compactSub.alignment = TextAlignmentOptions.TopLeft;
        _compactSub.characterSpacing = 2f;
        _compactSub.textWrappingMode = TextWrappingModes.Normal;
        PlaceTopLeft(_compactSub.rectTransform, new Vector2(24f, -46f), new Vector2(CompactWidth - 40f, 26f));
        _compactSub.gameObject.SetActive(false);
    }

    /// <summary>The thin amber bar on a card's left edge.</summary>
    private static void AmberBar(Transform card)
    {
        GameObject bar = RuntimeUi.CreatePanel(card, "AmberBar", Amber);
        bar.GetComponent<Image>().raycastTarget = false;
        RectTransform barRect = bar.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(0f, 1f);
        barRect.pivot = new Vector2(0f, 0.5f);
        barRect.sizeDelta = new Vector2(6f, 0f);
        barRect.anchoredPosition = Vector2.zero;
    }

    private static void MakeTickBar(Transform parent, Vector2 position, Vector2 size, float zRotation)
    {
        GameObject bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(parent, false);
        bar.layer = parent.gameObject.layer;
        Image image = bar.GetComponent<Image>();
        image.color = Green;
        image.raycastTarget = false;
        RectTransform rect = (RectTransform)bar.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, zRotation);
    }

    /// <summary>Builds one key-cap chip at x and returns its width.</summary>
    private float BuildChip(string key, float x)
    {
        // A bordered box: an amber frame with a dark face inset by 2 px.
        GameObject frame = new GameObject("Chip_" + key, typeof(RectTransform), typeof(Image));
        frame.transform.SetParent(_chipRow, false);
        frame.layer = _chipRow.gameObject.layer;
        Image frameImage = frame.GetComponent<Image>();
        frameImage.color = new Color(Amber.r, Amber.g, Amber.b, 0.95f);
        frameImage.raycastTarget = false;
        RectTransform frameRect = (RectTransform)frame.transform;

        GameObject face = RuntimeUi.CreatePanel(frame.transform, "Face", new Color(0.07f, 0.07f, 0.08f, 1f));
        face.GetComponent<Image>().raycastTarget = false;
        RectTransform faceRect = face.GetComponent<RectTransform>();
        faceRect.offsetMin = new Vector2(2f, 2f);
        faceRect.offsetMax = new Vector2(-2f, -2f);

        TextMeshProUGUI text = RuntimeUi.CreateText(face.transform, "Key", key, 24f, Color.white);
        text.fontStyle = FontStyles.Bold;
        text.characterSpacing = 2f;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        RuntimeUi.Stretch(text.rectTransform);

        float width = Mathf.Max(56f, text.GetPreferredValues(key).x + 26f);
        PlaceTopLeft(frameRect, new Vector2(x, 0f), new Vector2(width, 44f));
        return width;
    }
}
