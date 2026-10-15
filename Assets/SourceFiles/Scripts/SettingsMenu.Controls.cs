using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The CONTROLS tab of the settings screen (F119 moved it out of SettingsMenu.cs): the sensitivity and invert rows, the touch rows and the
/// REMAP CONTROLS button that opens <see cref="RebindPanel"/>.
/// </summary>
public partial class SettingsMenu
{
    private void BuildControls()
    {
        AddSlider(S("look"), GameSettings.MinMouseSensitivity, GameSettings.MaxMouseSensitivity, GameSettings.MouseSensitivity, false,
            v => GameSettings.MouseSensitivity = v, v => v.ToString("0.00", L10n.Culture) + "x");
        AddStepper(S("invert_y"), OffOn(), GameSettings.InvertY ? 1 : 0, true, i => GameSettings.InvertY = i == 1);
        AddStepper(S("touch_controls"), new[] { S("opt.auto"), S("opt.on"), S("opt.off") }, (int)GameSettings.TouchControls, false, i => GameSettings.TouchControls = (TouchInput.Mode)i);
        AddSlider(S("touch_look"), TouchInput.MinLookSensitivity, TouchInput.MaxLookSensitivity, GameSettings.TouchLookSensitivity, false,
            v => GameSettings.TouchLookSensitivity = v, v => v.ToString("0.00", L10n.Culture) + "x");
        AddButtonRow(L10n.T("ui.settings.remap"), "ui.settings.remap", remap => RebindPanel.Show(remap)); // F119
    }

    /// <summary>A row that is one wide button (centered under the label column and the control column).</summary>
    private void AddButtonRow(string caption, string key, Action<Button> onClick)
    {
        float y = NextRowY();
        Button button = RuntimeUi.CreateButton(_rows, caption, Vector2.zero, new Vector2(760f, 60f), 32f, ButtonIdle, ButtonHover, Color.white);
        RuntimeUi.Place((RectTransform)button.transform, new Vector2(0.5f, 1f), new Vector2(0f, y), new Vector2(760f, 60f));
        RuntimeUi.BindButton(button, key);
        button.onClick.AddListener(() => onClick(button));
    }
}
