using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The escalation. Everything that should get worse as the maze empties out is driven from one place:
/// the vertical objectives card in the corner, how loud and how wrong the soundtrack is, and how fast the
/// thing in the dark is willing to move.
///
/// Execution order is deliberately ahead of MainMenu so the counter is created first and therefore
/// draws underneath the title screen rather than on top of it.
/// </summary>
[DefaultExecutionOrder(-85)]
public class TensionDirector : MonoBehaviour
{
    [Header("Counter")]
    // S9: the counter words come from hud.counter.* now (the old serialized "label" is gone).
    [SerializeField] private float fontSize = 22f;
    [SerializeField] private Color calmColor = new Color(0.90f, 0.91f, 0.94f);
    [Tooltip("The counter bleeds toward this as the last stars come in")]
    [SerializeField] private Color alarmedColor = new Color(0.9f, 0.15f, 0.1f);

    private static readonly Color CardBgColor = new Color(0.025f, 0.032f, 0.048f, 0.78f);
    private static readonly Color AmberAccent = new Color(1f, 0.72f, 0.42f, 0.95f);
    private static readonly Color CompleteGreen = new Color(0.25f, 1f, 0.68f, 1f);
    private static readonly Color CompleteFaceGreen = new Color(0.05f, 0.24f, 0.16f, 0.95f);
    private static readonly Color IncompleteBoxBorder = new Color(0.75f, 0.78f, 0.85f, 0.35f);
    private static readonly Color IncompleteFace = new Color(0.05f, 0.06f, 0.09f, 0.92f);
    private static readonly Color HeaderGrey = new Color(0.68f, 0.70f, 0.76f, 0.9f);

    /// <summary>Bottom Y (negative, from top-left anchor) of the objectives card so ContractTracker can dock cleanly underneath it.</summary>
    public static float CardBottomY { get; private set; } = -210f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => CardBottomY = -210f;

    [Header("F104 B1 - dread threat")]
    [Tooltip("The hunter's threat level is lifted to at least the DreadMeter times this")]
    [SerializeField] private float dreadThreatWeight = 0.8f;
    [Tooltip("Seconds between threat refreshes from the meter (s)")]
    [SerializeField] private float threatRefreshSeconds = 0.5f;

    [Header("Star noise")]
    [Tooltip("How far the sound of a star being taken carries at zero progress (m, path distance, before the floor's hearing scale)")]
    [SerializeField] private float starNoiseRadius = 14f;
    [Tooltip("...at full progress. 34 m is most of an 11x11 maze: by the last star the hunter always comes.")]
    [SerializeField] private float starNoiseRadiusAtPeak = 34f;

    private struct ObjectiveRow
    {
        public RectTransform Root;
        public Image BoxBorder;
        public Image BoxFace;
        public GameObject Dot;
        public GameObject Tick;
        public TextMeshProUGUI Label;
        public bool WasComplete;
        public float Punch;
    }

    private AIFollower _follower;
    private HorrorAudioDirector _audio;
    private PlayerHud _hud;
    private KeyRing _keys;
    private DreadMeter _dread;
    private float _nextThreatRefresh;

    private RectTransform _cardRect;
    private Image _accentBar;
    private TextMeshProUGUI _headerLabel;
    private ObjectiveRow _starsRow;
    private ObjectiveRow _fusesRow;
    private ObjectiveRow _keysRow;
    private ObjectiveRow _escapeRow;

    private int _collected;
    private int _total;
    private bool _hunting;

    public void Configure(AIFollower follower, HorrorAudioDirector audio)
    {
        _follower = follower;
        _audio = audio;
        Apply();
    }

    /// <summary>Wired separately: SetUpAtmosphere builds the HUD after this director's Configure call.</summary>
    public void BindHud(PlayerHud hud)
    {
        _hud = hud;
    }

    /// <summary>F77: the player's KeyRing, for the KEYS counter segment. Unsubscribes any previous ring first - CLAUDE.md's every-subscriber-unsubscribes rule.</summary>
    public void BindKeyRing(KeyRing ring)
    {
        if (_keys != null) _keys.Changed -= Apply;
        _keys = ring;
        if (_keys != null) _keys.Changed += Apply;
        Apply();
    }

    /// <summary>F104 B1: the DreadMeter that lifts the hunter's threat dial above plain star progress (never below it).</summary>
    public void BindDread(DreadMeter meter)
    {
        _dread = meter;
    }

    /// <summary>Threat level the hunter should run at: star progress, lifted by the meter when it is active.</summary>
    private float ThreatLevel => _dread != null && _dread.IsActive ? Mathf.Max(Progress, _dread.Smoothed * dreadThreatWeight) : Progress;

    /// <summary>Shop cosmetic (F35): recolours the calm end of the counter. The alarmed end is unchanged.</summary>
    public void SetCalmColor(Color color)
    {
        calmColor = color;
        Apply();
    }

    private void OnEnable()
    {
        GameManager.ProgressChanged += HandleProgress;
        GameManager.ObjectivesChanged += Apply;
        GameManager.AllStarsCollected += HandleHatchOpen;
        Pickup.OnCollectedAt += HandleStarTaken;
        L10n.Changed += Apply;
    }

    private void OnDisable()
    {
        GameManager.ProgressChanged -= HandleProgress;
        GameManager.ObjectivesChanged -= Apply;
        GameManager.AllStarsCollected -= HandleHatchOpen;
        Pickup.OnCollectedAt -= HandleStarTaken;
        L10n.Changed -= Apply;
        if (_keys != null) _keys.Changed -= Apply;
    }

    private void Start()
    {
        BuildCounter();
        Apply();
    }

    private void Update()
    {
        if (_cardRect != null)
        {
            bool visible = !GameFlow.IsInSafeRoom && !GameFlow.IsInIntake;
            if (_cardRect.gameObject.activeSelf != visible) _cardRect.gameObject.SetActive(visible);

            if (visible)
            {
                TickRowPunch(ref _starsRow);
                TickRowPunch(ref _fusesRow);
                TickRowPunch(ref _keysRow);
                TickRowPunch(ref _escapeRow);

                if (_hunting && _escapeRow.Root != null && _escapeRow.Root.gameObject.activeSelf)
                {
                    bool keysReady = _keys == null || _keys.Total <= 0 || _keys.Held >= _keys.Total;
                    float pulse = FlashSafety.On ? 0.85f : 0.65f + 0.35f * Mathf.Sin(Time.unscaledTime * 5.5f);
                    Color escColor = keysReady ? Color.Lerp(AmberAccent, CompleteGreen, 0.35f) : AmberAccent;
                    escColor.a = pulse;
                    _escapeRow.Label.color = escColor;
                }
            }
        }

        if (_dread != null && _follower != null && Time.time >= _nextThreatRefresh && GameFlow.IsRunActive && !GameOutcome.IsOver)
        {
            _nextThreatRefresh = Time.time + threatRefreshSeconds;
            _follower.SetThreatLevel(ThreatLevel, _hunting);
        }
    }

    private static void TickRowPunch(ref ObjectiveRow row)
    {
        if (row.Root == null || row.Punch <= 0f) return;
        row.Punch = Mathf.MoveTowards(row.Punch, 0f, Time.unscaledDeltaTime / 0.28f);
        float s = 1f + 0.16f * Mathf.Sin(row.Punch * Mathf.PI);
        row.Root.localScale = new Vector3(s, s, 1f);
    }

    private float Progress => _total > 0 ? _collected / (float)_total : 0f;

    private void HandleProgress(int collected, int total)
    {
        _collected = collected;
        _total = total;
        Apply();
    }

    private void HandleHatchOpen()
    {
        _hunting = true;
        Apply();

        if (_audio != null) _audio.BeginPanic();
    }

    /// <summary>
    /// Fired by Pickup before OnCoinCollected/ProgressChanged, so Progress here is "before this star" -
    /// the star you just took is heard at the current (lower) radius, the next one is louder.
    /// </summary>
    private void HandleStarTaken(Vector3 at)
    {
        if (_follower == null) return;

        float radius = Mathf.Lerp(starNoiseRadius, starNoiseRadiusAtPeak, Progress);
        bool heard = _follower.HearNoise(at, radius);
        // F103 A7: the "IT HEARD YOU" beat stamps LastHeardYouTime synchronously inside HearNoise, so it fired on
        // this very pickup when the stamp equals now - its world text wins, and this line would double it.
        if (heard && _hud != null && Progress >= 0.5f && _follower.LastHeardYouTime < Time.time)
        {
            _hud.ShowSubtitle(L10n.T("sub.star.heard"), 2.2f);
        }
    }

    private void Apply()
    {
        float progress = Progress;

        if (_cardRect != null)
        {
            if (_headerLabel != null) _headerLabel.text = L10n.T("ui.objective.label");

            bool starsDone = _total > 0 && _collected >= _total;
            UpdateRow(ref _starsRow, true, L10n.T("hud.counter.stars", ("n", _collected), ("total", _total)), starsDone);

            bool hasFuses = GameManager.TokensTotal > 0;
            bool fusesDone = hasFuses && GameManager.TokensDone >= GameManager.TokensTotal;
            UpdateRow(ref _fusesRow, hasFuses, hasFuses ? L10n.T("hud.counter.fuses", ("n", GameManager.TokensDone), ("total", GameManager.TokensTotal)) : "", fusesDone);

            bool hasKeys = _keys != null && _keys.Total > 0;
            bool keysDone = hasKeys && _keys.Held >= _keys.Total;
            UpdateRow(ref _keysRow, hasKeys, hasKeys ? L10n.T("hud.counter.keys", ("n", _keys.Held), ("total", _keys.Total)) : "", keysDone);

            bool showEscape = _hunting;
            bool escapeReady = !hasKeys || keysDone;
            string escapeText = showEscape
                ? (escapeReady ? "➔  " + L10n.T("hud.prompt.leave") : L10n.T("hud.keys_locked", ("h", _keys.Held), ("t", _keys.Total)))
                : "";
            UpdateRow(ref _escapeRow, showEscape, escapeText, false);

            LayoutRows();

            bool allCollected = starsDone && (!hasFuses || fusesDone) && (!hasKeys || keysDone);
            if (_accentBar != null)
            {
                _accentBar.color = allCollected ? CompleteGreen : Color.Lerp(AmberAccent, alarmedColor, progress * 0.5f);
            }
        }

        if (_audio != null) _audio.SetIntensity(progress);
        if (_follower != null)
        {
            _follower.SetStarCount(_total);
            _follower.SetThreatLevel(ThreatLevel, _hunting);
        }
    }

    private void UpdateRow(ref ObjectiveRow row, bool active, string text, bool complete)
    {
        if (row.Root == null) return;
        row.Root.gameObject.SetActive(active);
        if (!active) return;

        if (complete && !row.WasComplete)
        {
            row.Punch = 1f;
        }
        row.WasComplete = complete;

        row.Label.text = complete ? text + "  ✓" : text;
        row.Label.color = complete ? CompleteGreen : calmColor;
        row.Label.fontStyle = FontStyles.Bold;

        if (row.BoxBorder != null) row.BoxBorder.color = complete ? CompleteGreen : IncompleteBoxBorder;
        if (row.BoxFace != null) row.BoxFace.color = complete ? CompleteFaceGreen : IncompleteFace;
        if (row.Dot != null) row.Dot.SetActive(!complete);
        if (row.Tick != null) row.Tick.SetActive(complete);
    }

    private void LayoutRows()
    {
        if (_cardRect == null) return;

        // In DESCENT mode, the SHARDS row is hidden so the card sits higher up right under LEVEL -n.
        float topY = GameFlow.IsDescent ? -56f : -86f;
        _cardRect.anchoredPosition = new Vector2(26f, topY);

        float y = -36f;
        const float rowStep = 34f;

        if (_starsRow.Root != null && _starsRow.Root.gameObject.activeSelf)
        {
            _starsRow.Root.anchoredPosition = new Vector2(0f, y);
            y -= rowStep;
        }
        if (_fusesRow.Root != null && _fusesRow.Root.gameObject.activeSelf)
        {
            _fusesRow.Root.anchoredPosition = new Vector2(0f, y);
            y -= rowStep;
        }
        if (_keysRow.Root != null && _keysRow.Root.gameObject.activeSelf)
        {
            _keysRow.Root.anchoredPosition = new Vector2(0f, y);
            y -= rowStep;
        }
        if (_escapeRow.Root != null && _escapeRow.Root.gameObject.activeSelf)
        {
            _escapeRow.Root.anchoredPosition = new Vector2(0f, y);
            y -= rowStep;
        }

        float height = Mathf.Max(76f, -y + 10f);
        _cardRect.sizeDelta = new Vector2(316f, height);
        CardBottomY = topY - height;
    }

    private void BuildCounter()
    {
        Canvas canvas = RuntimeUi.ResolveCanvas();
        if (canvas == null) return;

        GameObject cardObj = RuntimeUi.CreatePanel(canvas.transform, "StarCounter", CardBgColor);
        cardObj.GetComponent<Image>().raycastTarget = false;
        _cardRect = cardObj.GetComponent<RectTransform>();
        _cardRect.anchorMin = new Vector2(0f, 1f);
        _cardRect.anchorMax = new Vector2(0f, 1f);
        _cardRect.pivot = new Vector2(0f, 1f);
        _cardRect.anchoredPosition = new Vector2(26f, -86f);
        _cardRect.sizeDelta = new Vector2(316f, 82f);

        GameObject barObj = RuntimeUi.CreatePanel(cardObj.transform, "AccentBar", AmberAccent);
        _accentBar = barObj.GetComponent<Image>();
        _accentBar.raycastTarget = false;
        RectTransform barRect = barObj.GetComponent<RectTransform>();
        barRect.anchorMin = new Vector2(0f, 0f);
        barRect.anchorMax = new Vector2(0f, 1f);
        barRect.pivot = new Vector2(0f, 0.5f);
        barRect.sizeDelta = new Vector2(5f, 0f);
        barRect.anchoredPosition = Vector2.zero;

        _headerLabel = RuntimeUi.CreateText(cardObj.transform, "Header", L10n.T("ui.objective.label"), 15f, HeaderGrey);
        _headerLabel.alignment = TextAlignmentOptions.TopLeft;
        _headerLabel.fontStyle = FontStyles.Bold;
        _headerLabel.characterSpacing = 5f;
        RectTransform headerRect = _headerLabel.rectTransform;
        headerRect.anchorMin = new Vector2(0f, 1f);
        headerRect.anchorMax = new Vector2(0f, 1f);
        headerRect.pivot = new Vector2(0f, 1f);
        headerRect.anchoredPosition = new Vector2(16f, -9f);
        headerRect.sizeDelta = new Vector2(280f, 22f);
        RuntimeUi.AutoFit(_headerLabel, 0.7f);

        _starsRow = CreateRow(cardObj.transform, "RowStars");
        _fusesRow = CreateRow(cardObj.transform, "RowFuses");
        _keysRow = CreateRow(cardObj.transform, "RowKeys");
        _escapeRow = CreateRow(cardObj.transform, "RowEscape");
    }

    private ObjectiveRow CreateRow(Transform parent, string name)
    {
        GameObject root = new GameObject(name, typeof(RectTransform));
        root.transform.SetParent(parent, false);
        root.layer = parent.gameObject.layer;
        RectTransform rect = (RectTransform)root.transform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.sizeDelta = new Vector2(316f, 32f);

        // Checkbox frame (22x22)
        GameObject boxObj = RuntimeUi.CreatePanel(root.transform, "Box", IncompleteBoxBorder);
        Image boxBorder = boxObj.GetComponent<Image>();
        boxBorder.raycastTarget = false;
        RectTransform boxRect = boxObj.GetComponent<RectTransform>();
        boxRect.anchorMin = new Vector2(0f, 0.5f);
        boxRect.anchorMax = new Vector2(0f, 0.5f);
        boxRect.pivot = new Vector2(0f, 0.5f);
        boxRect.anchoredPosition = new Vector2(16f, 0f);
        boxRect.sizeDelta = new Vector2(22f, 22f);

        GameObject faceObj = RuntimeUi.CreatePanel(boxObj.transform, "Face", IncompleteFace);
        Image boxFace = faceObj.GetComponent<Image>();
        boxFace.raycastTarget = false;
        RectTransform faceRect = faceObj.GetComponent<RectTransform>();
        faceRect.offsetMin = new Vector2(2f, 2f);
        faceRect.offsetMax = new Vector2(-2f, -2f);

        // Incomplete diamond dot
        GameObject dotObj = RuntimeUi.CreatePanel(boxObj.transform, "Dot", new Color(1f, 0.75f, 0.45f, 0.65f));
        dotObj.GetComponent<Image>().raycastTarget = false;
        RectTransform dotRect = dotObj.GetComponent<RectTransform>();
        dotRect.anchorMin = dotRect.anchorMax = new Vector2(0.5f, 0.5f);
        dotRect.pivot = new Vector2(0.5f, 0.5f);
        dotRect.anchoredPosition = Vector2.zero;
        dotRect.sizeDelta = new Vector2(5f, 5f);
        dotRect.localRotation = Quaternion.Euler(0f, 0f, 45f);

        // Vector green checkmark (two bars)
        GameObject tickObj = new GameObject("Tick", typeof(RectTransform));
        tickObj.transform.SetParent(boxObj.transform, false);
        tickObj.layer = boxObj.layer;
        RectTransform tickRect = (RectTransform)tickObj.transform;
        tickRect.anchorMin = tickRect.anchorMax = new Vector2(0.5f, 0.5f);
        tickRect.pivot = new Vector2(0.5f, 0.5f);
        tickRect.anchoredPosition = Vector2.zero;
        tickRect.sizeDelta = new Vector2(22f, 22f);
        MakeTickBar(tickObj.transform, new Vector2(-3.5f, -1.8f), new Vector2(3f, 8f), 45f);
        MakeTickBar(tickObj.transform, new Vector2(2.2f, 0.6f), new Vector2(3f, 14.5f), -45f);
        tickObj.SetActive(false);

        TextMeshProUGUI label = RuntimeUi.CreateText(root.transform, "Label", "", fontSize, calmColor);
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.fontStyle = FontStyles.Bold;
        label.characterSpacing = 2.5f;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        RectTransform labelRect = label.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(1f, 1f);
        labelRect.offsetMin = new Vector2(48f, 2f);
        labelRect.offsetMax = new Vector2(-12f, -2f);
        RuntimeUi.AutoFit(label, 0.65f);

        return new ObjectiveRow
        {
            Root = rect,
            BoxBorder = boxBorder,
            BoxFace = boxFace,
            Dot = dotObj,
            Tick = tickObj,
            Label = label
        };
    }

    private static void MakeTickBar(Transform parent, Vector2 position, Vector2 size, float zRotation)
    {
        GameObject bar = new GameObject("Bar", typeof(RectTransform), typeof(Image));
        bar.transform.SetParent(parent, false);
        bar.layer = parent.gameObject.layer;
        Image image = bar.GetComponent<Image>();
        image.color = CompleteGreen;
        image.raycastTarget = false;
        RectTransform rect = (RectTransform)bar.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
        rect.localRotation = Quaternion.Euler(0f, 0f, zRotation);
    }
}
