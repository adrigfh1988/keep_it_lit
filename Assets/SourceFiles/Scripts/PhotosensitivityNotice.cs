using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// F118: the first-launch photosensitivity warning. Shown once by MainMenu.Start over the title screen (title path only, never on a
/// Retry / CONTINUE reload) until the player presses CONTINUE; the card also offers the REDUCE FLASHING switch so it can be turned on
/// before anything flickers. Built from Buttons only, so Enter / gamepad South / touch all work through the EventSystem (no key reads).
/// </summary>
public static class PhotosensitivityNotice
{
    public const string AcknowledgedKey = "keepitlit.notice.photosensitivity";

    public static bool Acknowledged
    {
        get { try { return PlayerPrefs.GetInt(AcknowledgedKey, 0) == 1; } catch { return false; } }
        set { try { PlayerPrefs.SetInt(AcknowledgedKey, value ? 1 : 0); PlayerPrefs.Save(); } catch { /* best effort */ } }
    }

    /// <summary>Debug > Reset Photosensitivity Notice.</summary>
    public static void ResetAcknowledged()
    {
        try { PlayerPrefs.DeleteKey(AcknowledgedKey); PlayerPrefs.Save(); } catch { /* best effort */ }
    }

    /// <summary>Builds the card as the last child of <paramref name="canvas"/>; <paramref name="onDone"/> runs after CONTINUE.</summary>
    public static GameObject Show(Transform canvas, System.Action onDone)
    {
        Color accent = new Color(0.86f, 0.12f, 0.09f);
        Color idle = new Color(0.10f, 0.10f, 0.12f, 0.92f);
        Color hover = new Color(0.48f, 0.08f, 0.06f, 1f);

        GameObject panel = RuntimeUi.CreatePanel(canvas, "PhotosensitivityNotice", new Color(0.02f, 0.02f, 0.03f, 1f));
        panel.transform.SetAsLastSibling();

        TextMeshProUGUI heading = RuntimeUi.CreateText(panel.transform, "Heading", L10n.T("ui.notice.photosensitivity.heading"), 64f, accent);
        RuntimeUi.Bind(heading, "ui.notice.photosensitivity.heading", 0.6f);
        heading.fontStyle = FontStyles.Bold;
        heading.characterSpacing = 6f;
        RuntimeUi.Place(heading.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -170f), new Vector2(1500f, 100f));

        TextMeshProUGUI body = RuntimeUi.CreateText(panel.transform, "Body", L10n.T("ui.notice.photosensitivity.body"), 36f, new Color(0.88f, 0.88f, 0.9f));
        RuntimeUi.Bind(body, "ui.notice.photosensitivity.body", 0.6f);
        body.textWrappingMode = TextWrappingModes.Normal;
        RuntimeUi.Place(body.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -480f), new Vector2(1400f, 520f));

        Button toggle = RuntimeUi.CreateButton(panel.transform, "", new Vector2(0f, 330f), new Vector2(980f, 86f), 38f, idle, hover, Color.white);
        TextMeshProUGUI toggleLabel = toggle.GetComponentInChildren<TextMeshProUGUI>();
        RuntimeUi.AutoFit(toggleLabel, 0.65f);
        System.Action relabel = () => toggleLabel.text = L10n.T("ui.settings.reduce_flashing") + ": " + L10n.T(GameSettings.ReduceFlashing ? "ui.settings.opt.on" : "ui.settings.opt.off");
        relabel();
        toggle.onClick.AddListener(() => { GameSettings.ReduceFlashing = !GameSettings.ReduceFlashing; relabel(); });

        Button go = RuntimeUi.CreateButton(panel.transform, L10n.T("ui.common.continue"), new Vector2(0f, 220f), new Vector2(420f, 86f), 40f, idle, hover, Color.white);
        RuntimeUi.BindButton(go, "ui.common.continue");
        go.onClick.AddListener(() =>
        {
            Acknowledged = true;
            Object.Destroy(panel);
            onDone?.Invoke();
        });

        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(go.gameObject);
        return panel;
    }
}
