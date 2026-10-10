using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

/// <summary>
/// Small helpers for building UI in code. Everything in this project is assembled at runtime rather
/// than authored in the scene, so this keeps the font lookup and the widget boilerplate in one place.
/// </summary>
public static class RuntimeUi
{
    /// <summary>The project has exactly one font. Find it without needing a serialized reference.</summary>
    public static TMP_FontAsset ResolveFont()
    {
        if (TMP_Settings.defaultFontAsset != null) return TMP_Settings.defaultFontAsset;

        // Fall back to whatever the scene's own text uses. Include inactive: the win panel is hidden.
        foreach (TextMeshProUGUI text in Object.FindObjectsByType<TextMeshProUGUI>(FindObjectsInactive.Include))
        {
            if (text.font != null) return text.font;
        }

        return null;
    }

    /// <summary>
    /// Nothing in this scene is clickable without one. The project is Input System only, so this has
    /// to be InputSystemUIInputModule - the legacy StandaloneInputModule would silently do nothing.
    /// </summary>
    public static void EnsureEventSystem()
    {
        if (EventSystem.current != null) return;

        GameObject holder = new GameObject("EventSystem");
        holder.AddComponent<EventSystem>();

        InputSystemUIInputModule module = holder.AddComponent<InputSystemUIInputModule>();
        // Without actions the module reports no input at all; the defaults cover point and click.
        module.AssignDefaultActions();
    }

    /// <summary>The root canvas, creating one only if the scene somehow has none.</summary>
    public static Canvas ResolveCanvas()
    {
        foreach (Canvas canvas in Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude))
        {
            if (canvas.transform.parent == null) return canvas;
        }

        Canvas found = Object.FindAnyObjectByType<Canvas>();
        if (found != null) return found;

        GameObject holder = new GameObject("Canvas");
        Canvas created = holder.AddComponent<Canvas>();
        created.renderMode = RenderMode.ScreenSpaceOverlay;
        holder.AddComponent<CanvasScaler>();
        holder.AddComponent<GraphicRaycaster>();
        return created;
    }

    /// <summary>
    /// The scene's scaler is Constant Pixel Size, which pixel-locks every element to whatever
    /// resolution it happens to run at. Scale with screen size instead, against 1080p.
    /// </summary>
    public static void MakeCanvasResolutionIndependent(Canvas canvas)
    {
        CanvasScaler scaler = canvas.GetComponent<CanvasScaler>();
        if (scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();

        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
        scaler.matchWidthOrHeight = 0.5f;
    }

    public static GameObject CreatePanel(Transform parent, string name, Color color)
    {
        GameObject panel = new GameObject(name);
        panel.transform.SetParent(parent, false);
        panel.layer = parent.gameObject.layer;

        Image image = panel.AddComponent<Image>();
        image.color = color;
        Stretch(image.rectTransform);
        return panel;
    }

    public static TextMeshProUGUI CreateText(Transform parent, string name, string content, float fontSize, Color color)
    {
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;

        TextMeshProUGUI text = holder.AddComponent<TextMeshProUGUI>();
        text.font = ResolveFont();
        text.fontSize = fontSize;
        text.color = color;
        text.text = content;
        text.alignment = TextAlignmentOptions.Center;
        text.raycastTarget = false;
        return text;
    }

    /// <summary>
    /// S9: makes <paramref name="text"/> follow the language - sets it from <paramref name="key"/> now and again on every language change
    /// (see <see cref="LocalizedLabel"/>). Optionally shrinks to fit its box (D14, down to <paramref name="minFraction"/> of its size).
    /// </summary>
    public static LocalizedLabel Bind(TMP_Text text, string key, float autoFitMin = 0f, params (string name, object value)[] args)
    {
        if (text == null) return null;
        LocalizedLabel label = text.GetComponent<LocalizedLabel>();
        if (label == null) label = text.gameObject.AddComponent<LocalizedLabel>();
        label.Set(text, key, args != null && args.Length > 0 ? args : null);
        if (autoFitMin > 0f) AutoFit(text, autoFitMin);
        return label;
    }

    /// <summary>S9 (D14): a button whose caption follows the language and shrinks (to ~65 %) rather than overflowing in a longer language.</summary>
    public static Button BindButton(Button button, string key, float autoFitMin = 0.65f)
    {
        if (button != null) Bind(button.GetComponentInChildren<TMP_Text>(), key, autoFitMin);
        return button;
    }

    /// <summary>S9 (D14): enables auto-size with the current size as the maximum and <paramref name="minFraction"/> of it as the minimum.</summary>
    public static void AutoFit(TMP_Text text, float minFraction = 0.65f)
    {
        if (text == null) return;
        float max = text.fontSize;
        text.enableAutoSizing = true;
        text.fontSizeMax = max;
        text.fontSizeMin = Mathf.Max(8f, max * minFraction);
    }

    public static void Place(RectTransform rect, Vector2 anchor, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = anchor;
        rect.anchorMax = anchor;
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    public static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    /// <summary>
    /// The Image is left white and the whole look comes from the Button's colour tints, because
    /// Selectable multiplies its tint into the graphic - tinting a dark image just stays dark.
    /// </summary>
    public static Button CreateButton(Transform parent, string label, Vector2 anchoredPosition, Vector2 size,
        float fontSize, Color idle, Color hover, Color textColor)
    {
        GameObject holder = new GameObject(label + " Button");
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;

        Image image = holder.AddComponent<Image>();
        image.color = Color.white;
        Place(image.rectTransform, new Vector2(0.5f, 0f), anchoredPosition, size);

        Button button = holder.AddComponent<Button>();
        button.targetGraphic = image;

        ColorBlock colors = button.colors;
        colors.normalColor = idle;
        colors.highlightedColor = hover;
        colors.pressedColor = new Color(hover.r * 1.3f, hover.g * 1.3f, hover.b * 1.3f, 1f);
        colors.selectedColor = idle;
        colors.fadeDuration = 0.08f;
        button.colors = colors;

        TextMeshProUGUI text = CreateText(holder.transform, "Label", label, fontSize, textColor);
        Stretch(text.rectTransform);

        return button;
    }

    /// <summary>
    /// S2: a plain UGUI Slider (Background track, Fill Area/Fill, Handle Slide Area/Handle - the structure Unity's own prefab uses), generalised from the
    /// hand-built one the pause menu used to carry. The handle is tinted when selected so keyboard / gamepad focus is visible.
    /// </summary>
    public static Slider CreateSlider(Transform parent, string name, Vector2 anchor, Vector2 anchoredPosition, Vector2 size,
        float min, float max, float value, UnityEngine.Events.UnityAction<float> onChanged, Color fillColor)
    {
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;
        RectTransform holderRect = holder.AddComponent<RectTransform>();
        Place(holderRect, anchor, anchoredPosition, size);

        GameObject trackObj = new GameObject("Track");
        trackObj.transform.SetParent(holder.transform, false);
        trackObj.layer = holder.layer;
        Image track = trackObj.AddComponent<Image>();
        track.color = new Color(0f, 0f, 0f, 0.55f);
        Place(track.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(size.x, Mathf.Min(14f, size.y)));

        GameObject fillAreaObj = new GameObject("FillArea");
        fillAreaObj.transform.SetParent(trackObj.transform, false);
        fillAreaObj.layer = holder.layer;
        Stretch(fillAreaObj.AddComponent<RectTransform>());

        GameObject fillObj = new GameObject("Fill");
        fillObj.transform.SetParent(fillAreaObj.transform, false);
        fillObj.layer = holder.layer;
        Image fill = fillObj.AddComponent<Image>();
        fill.color = fillColor;
        Stretch(fill.rectTransform);

        GameObject handleAreaObj = new GameObject("HandleArea");
        handleAreaObj.transform.SetParent(trackObj.transform, false);
        handleAreaObj.layer = holder.layer;
        Stretch(handleAreaObj.AddComponent<RectTransform>());

        GameObject handleObj = new GameObject("Handle");
        handleObj.transform.SetParent(handleAreaObj.transform, false);
        handleObj.layer = holder.layer;
        Image handle = handleObj.AddComponent<Image>();
        handle.color = Color.white;
        Place(handle.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(26f, 34f));

        Slider slider = holder.AddComponent<Slider>();
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = min;
        slider.maxValue = max;
        slider.SetValueWithoutNotify(value);
        ColorBlock colors = slider.colors;
        colors.normalColor = Color.white;
        colors.highlightedColor = new Color(1f, 0.55f, 0.5f);
        colors.selectedColor = new Color(1f, 0.4f, 0.35f);
        colors.pressedColor = new Color(1f, 0.3f, 0.25f);
        slider.colors = colors;
        if (onChanged != null) slider.onValueChanged.AddListener(onChanged);
        return slider;
    }

    /// <summary>S2: a "&lt; value &gt;" picker (see StepperControl). Call Configure on the result with the option labels.</summary>
    public static StepperControl CreateStepper(Transform parent, string name, Vector2 anchor, Vector2 anchoredPosition, Vector2 size,
        float fontSize, Color idle, Color hover)
    {
        GameObject holder = new GameObject(name);
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;
        Image background = holder.AddComponent<Image>();
        background.color = Color.white;
        Place(background.rectTransform, anchor, anchoredPosition, size);

        StepperControl stepper = holder.AddComponent<StepperControl>();
        stepper.targetGraphic = background;
        ColorBlock colors = stepper.colors;
        colors.normalColor = idle;
        colors.highlightedColor = idle;
        colors.selectedColor = hover;
        colors.pressedColor = idle;
        colors.fadeDuration = 0.08f;
        stepper.colors = colors;

        float arrow = size.y;
        Button left = CreateArrow(holder.transform, "<", new Vector2(0f, 0.5f), new Vector2(arrow * 0.5f, 0f), new Vector2(arrow, size.y), fontSize, idle, hover);
        Button right = CreateArrow(holder.transform, ">", new Vector2(1f, 0.5f), new Vector2(-arrow * 0.5f, 0f), new Vector2(arrow, size.y), fontSize, idle, hover);
        TextMeshProUGUI label = CreateText(holder.transform, "Value", "", fontSize, Color.white);
        Stretch(label.rectTransform);
        label.rectTransform.offsetMin = new Vector2(arrow, 0f);
        label.rectTransform.offsetMax = new Vector2(-arrow, 0f);
        stepper.Setup(label, left, right);
        return stepper;
    }

    private static Button CreateArrow(Transform parent, string glyph, Vector2 anchor, Vector2 anchoredPosition, Vector2 size,
        float fontSize, Color idle, Color hover)
    {
        GameObject holder = new GameObject(glyph == "<" ? "Left" : "Right");
        holder.transform.SetParent(parent, false);
        holder.layer = parent.gameObject.layer;
        Image image = holder.AddComponent<Image>();
        image.color = Color.white;
        Place(image.rectTransform, anchor, anchoredPosition, size);
        Button button = holder.AddComponent<Button>();
        button.targetGraphic = image;
        button.navigation = new Navigation { mode = Navigation.Mode.None }; // focus lives on the stepper itself
        ColorBlock colors = button.colors;
        colors.normalColor = new Color(idle.r, idle.g, idle.b, 0f);
        colors.highlightedColor = hover;
        colors.pressedColor = new Color(hover.r * 1.3f, hover.g * 1.3f, hover.b * 1.3f, 1f);
        colors.selectedColor = colors.normalColor;
        colors.fadeDuration = 0.08f;
        button.colors = colors;
        TextMeshProUGUI text = CreateText(holder.transform, "Glyph", glyph, fontSize * 1.1f, Color.white);
        text.fontStyle = FontStyles.Bold;
        Stretch(text.rectTransform);
        return button;
    }
}
