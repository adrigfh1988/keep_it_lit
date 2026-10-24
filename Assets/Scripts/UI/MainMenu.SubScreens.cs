using TMPro;
using UnityEngine;
using UnityEngine.UI;

public partial class MainMenu
{
    private static string RulesText() =>
        L10n.T("ui.rules.item1") + "\n\n" + L10n.T("ui.rules.item2") + "\n\n" + L10n.T("ui.rules.item3") + "\n\n" + L10n.T("ui.rules.item4");

    /// <summary>F119: the keyboard / mouse or gamepad sheet - every key and button is the live binding (a rebind or a pad press shows here).</summary>
    private static string ControlsSheet() => L10n.T("ui.controls.sheet",
        ("move", Bold(GameAction.Move)), ("sprint", Bold(GameAction.Sprint)), ("look", Bold(GameAction.Look)), ("torch", Bold(GameAction.Torch)),
        ("focus", Bold(GameAction.Focus)), ("interact", Bold(GameAction.Interact)), ("throw", Bold(GameAction.Throw)), ("item1", Bold(GameAction.Item1)),
        ("item2", Bold(GameAction.Item2)), ("map", Bold(GameAction.Map)), ("pause", Bold(GameAction.Pause)));

    private static string Bold(GameAction a) => "<b>" + InputGlyphs.Key(a) + "</b>";

    /// <summary>F76 T6: shown instead of the keyboard/mouse control line while touch is active or the
    /// platform is mobile (iPadOS Safari under-reports as desktop, so isMobilePlatform is checked too).</summary>
    private static string ControlsText()
    {
        string tip = "<size=30>" + L10n.T("ui.controls.tip") + "</size>";
        if (!TouchInput.Active && !Application.isMobilePlatform) return ControlsSheet() + "\n" + tip;
        return L10n.T("ui.controls.touch") + "\n" + tip;
    }

    // ---------------------------------------------------------------- credits

    /// <summary>The CREDITS button: opens the scrolling credits panel over the title screen (the data is <see cref="CreditsList"/>).</summary>
    private void ShowCredits()
    {
        TouchInput.Poll();
        if (_creditsScreen == null) BuildCreditsScreen(_titleScreen.transform.parent);
        _creditsScreen.transform.SetAsLastSibling();
        _creditsScroll.verticalNormalizedPosition = 1f;
        _titleScreen.SetActive(false);
        _creditsScreen.SetActive(true);
    }

    private void CloseCredits()
    {
        _creditsScreen.SetActive(false);
        _titleScreen.SetActive(true);
    }

    /// <summary>The credits panel: heading, the "made by" line, a scrollable list built from <see cref="CreditsList"/>, BACK and the version.</summary>
    private void BuildCreditsScreen(Transform canvas)
    {
        _creditsScreen = RuntimeUi.CreatePanel(canvas, "CreditsScreen", new Color(0.02f, 0.02f, 0.03f, 0.97f));
        Transform root = _creditsScreen.transform;

        TextMeshProUGUI heading = RuntimeUi.CreateText(root, "Heading", L10n.T("ui.credits.title"), 76f, accent);
        RuntimeUi.Bind(heading, "ui.credits.title", 0.6f);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 10f;
        RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -110f), new Vector2(1400f, 100f));

        TextMeshProUGUI madeBy = RuntimeUi.CreateText(root, "MadeBy", gameTitle + "  -  " + CreditsList.DeveloperName, 34f, new Color(0.88f, 0.88f, 0.9f));
        RuntimeUi.AutoFit(madeBy, 0.6f);
        RuntimeUi.Place(madeBy.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -190f), new Vector2(1400f, 50f));

        // Scroll view: a masked viewport (its transparent Image catches drags/taps for touch) over a vertical stack that sizes itself to its text.
        GameObject viewport = new GameObject("Viewport");
        viewport.transform.SetParent(root, false);
        viewport.layer = root.gameObject.layer;
        Image hit = viewport.AddComponent<Image>();
        hit.color = new Color(0f, 0f, 0f, 0f);
        viewport.AddComponent<RectMask2D>();
        RuntimeUi.Place(hit.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -45f), new Vector2(1400f, 690f));

        GameObject content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        content.layer = root.gameObject.layer;
        RectTransform contentRect = (RectTransform)content.transform;
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.anchoredPosition = Vector2.zero;
        contentRect.sizeDelta = Vector2.zero;
        VerticalLayoutGroup stack = content.AddComponent<VerticalLayoutGroup>();
        stack.spacing = 10f;
        stack.padding = new RectOffset(30, 30, 10, 30);
        stack.childAlignment = TextAnchor.UpperCenter;
        stack.childControlWidth = true;
        stack.childControlHeight = true;
        stack.childForceExpandWidth = true;
        stack.childForceExpandHeight = false;
        content.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        foreach (CreditsList.Section section in CreditsList.Sections)
        {
            TextMeshProUGUI head = RuntimeUi.CreateText(content.transform, "Heading", L10n.T(section.HeadingKey), 42f, accent);
            RuntimeUi.Bind(head, section.HeadingKey);
            head.fontStyle = FontStyles.Bold;
            head.characterSpacing = 6f;
            head.margin = new Vector4(0f, 22f, 0f, 4f);

            foreach (string line in section.Lines)
            {
                TextMeshProUGUI entry = RuntimeUi.CreateText(content.transform, "Line", line, 30f, new Color(0.82f, 0.82f, 0.86f));
                entry.textWrappingMode = TextWrappingModes.Normal;
            }
        }

        TextMeshProUGUI version = RuntimeUi.CreateText(content.transform, "Version", VersionLabel(), 26f, new Color(0.6f, 0.6f, 0.64f, 0.8f));
        version.margin = new Vector4(0f, 30f, 0f, 0f);

        _creditsScroll = viewport.AddComponent<ScrollRect>();
        _creditsScroll.viewport = hit.rectTransform;
        _creditsScroll.content = contentRect;
        _creditsScroll.horizontal = false;
        _creditsScroll.vertical = true;
        _creditsScroll.movementType = ScrollRect.MovementType.Clamped;
        _creditsScroll.scrollSensitivity = 45f;

        Button back = RuntimeUi.CreateButton(root, L10n.T("ui.common.back"),
            new Vector2(0f, 40f), new Vector2(420f, 86f), 40f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(back, "ui.common.back");
        back.onClick.AddListener(CloseCredits);
    }

    /// <summary>Escape / gamepad B / the touch pause button close the credits; arrow keys and the right stick scroll them.</summary>
    private void UpdateCredits()
    {
        // F119: MenuCancel = Esc / pad B / touch Pause; MenuScroll = up and down arrows / right stick Y.
        bool back = GameInput.Pressed(GameAction.MenuCancel);
        float scroll = GameInput.Axis(GameAction.MenuScroll);
        if (back) { CloseCredits(); return; }
        if (Mathf.Abs(scroll) > 0.05f)
        {
            // Unscaled: the title screen runs at timeScale 0. 900 px of content per second at full deflection.
            RectTransform content = _creditsScroll.content;
            float range = Mathf.Max(1f, content.rect.height - ((RectTransform)_creditsScroll.viewport).rect.height);
            _creditsScroll.verticalNormalizedPosition = Mathf.Clamp01(_creditsScroll.verticalNormalizedPosition + scroll * 900f * Time.unscaledDeltaTime / range);
        }
    }

    private void BuildRulesScreen(Transform canvas)
    {
        _rulesScreen = RuntimeUi.CreatePanel(canvas, "RulesScreen", new Color(0.02f, 0.02f, 0.03f, 0.97f));

        TextMeshProUGUI heading = RuntimeUi.CreateText(_rulesScreen.transform, "Heading", L10n.T("ui.rules.heading"), 76f, accent);
        RuntimeUi.Bind(heading, "ui.rules.heading", 0.6f);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 10f;
        RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -150f), new Vector2(1400f, 100f));

        string rules = RulesText();

        TextMeshProUGUI body = RuntimeUi.CreateText(_rulesScreen.transform, "Rules", rules, 36f, new Color(0.88f, 0.88f, 0.9f));
        _rulesBody = body;
        RuntimeUi.AutoFit(body, 0.7f);
        body.alignment = TextAlignmentOptions.Left;
        RuntimeUi.Place(body.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1100f, 380f));

        // Text is filled in by ShowRules() (touch-aware - see ControlsText), not here: TouchInput.Active
        // is not settled yet this early (Start runs before the first tap that could turn it on).
        _controlText = RuntimeUi.CreateText(_rulesScreen.transform, "Controls", "", 34f, new Color(0.62f, 0.62f, 0.66f));
        RuntimeUi.AutoFit(_controlText, 0.7f);
        RuntimeUi.Place(_controlText.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -210f), new Vector2(1400f, 170f));

        Button start = RuntimeUi.CreateButton(_rulesScreen.transform, L10n.T("ui.rules.start"),
            new Vector2(0f, 150f), new Vector2(420f, 90f), 44f, buttonIdle, buttonHover, Color.white);
        RuntimeUi.BindButton(start, "ui.rules.start");
        _rulesLabel = start.GetComponentInChildren<LocalizedLabel>();
        start.onClick.AddListener(() =>
        {
            if (_rulesStartsGame) StartGame();
            else
            {
                _rulesScreen.SetActive(false);
                _titleScreen.SetActive(true);
            }
        });
    }

    // ---------------------------------------------------------------- generated cover

    /// <summary>
    /// A cover built in code: the flashlight cone cutting up through fog, and two red eyes waiting
    /// above it. Stands in until a painted one is dropped into the coverImage slot.
    /// </summary>
    private Sprite GenerateCover()
    {
        int w = Mathf.Max(64, generatedCoverWidth);
        int h = Mathf.Max(64, generatedCoverHeight);
        float aspect = h / (float)w;

        _generatedCover = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "GeneratedCover" };
        Color[] pixels = new Color[w * h];

        Color fog = new Color(0.014f, 0.016f, 0.024f);
        Color beam = new Color(0.62f, 0.56f, 0.46f);

        for (int y = 0; y < h; y++)
        {
            float v = y / (float)(h - 1);

            for (int x = 0; x < w; x++)
            {
                float u = x / (float)(w - 1);
                Color c = fog;

                // Flashlight cone, apex just off the bottom edge, widening as it rises
                float alongCone = v + 0.05f;
                if (alongCone > 0f)
                {
                    float halfWidth = alongCone * 0.66f;
                    float across = 1f - Mathf.Clamp01(Mathf.Abs(u - 0.5f) / Mathf.Max(halfWidth, 0.0001f));
                    across *= across;
                    c += beam * (across * Mathf.Exp(-alongCone * 2.8f) * 1.15f);
                }

                c += EyeGlow(u, v, 0.466f, 0.585f, aspect);
                c += EyeGlow(u, v, 0.534f, 0.585f, aspect);

                // Vignette, so the title and buttons always have something dark to sit on
                float dx = (u - 0.5f) * 2f;
                float dy = (v - 0.5f) * 2f;
                float falloff = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) * 0.72f);
                c *= Mathf.Lerp(0.3f, 1f, falloff);

                pixels[y * w + x] = new Color(c.r, c.g, c.b, 1f);
            }
        }

        _generatedCover.SetPixels(pixels);
        _generatedCover.Apply();

        _generatedSprite = Sprite.Create(_generatedCover, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f));
        return _generatedSprite;
    }

    private static Color EyeGlow(float u, float v, float centreX, float centreY, float aspect)
    {
        float dx = u - centreX;
        float dy = (v - centreY) * aspect;
        float distance = Mathf.Sqrt(dx * dx + dy * dy);

        float halo = Mathf.Exp(-(distance / 0.055f) * (distance / 0.055f) * 2.2f) * 0.55f;
        float core = distance < 0.0075f ? 1f : 0f;

        float intensity = halo + core;
        return new Color(1f * intensity, 0.07f * intensity, 0.04f * intensity);
    }
}
