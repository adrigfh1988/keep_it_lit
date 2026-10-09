using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// S2: a "&lt; value &gt;" picker for the settings menu (enums, resolutions, on/off). A Selectable so keyboard / gamepad navigation can land on it:
/// left / right step it, submit steps forward (wrapping) - which makes it a toggle for a two-option list. Built by RuntimeUi.CreateStepper.
/// </summary>
public class StepperControl : Selectable, IMoveHandler, ISubmitHandler
{
    private string[] _options = Array.Empty<string>();
    private int _index;
    private bool _wrap;
    private Action<int> _onChanged;
    private TextMeshProUGUI _label;

    public int Index => _index;

    public void Setup(TextMeshProUGUI label, Button left, Button right)
    {
        _label = label;
        left.onClick.AddListener(() => Step(-1));
        right.onClick.AddListener(() => Step(1));
    }

    public void Configure(string[] options, int index, bool wrap, Action<int> onChanged)
    {
        _options = options ?? Array.Empty<string>();
        _wrap = wrap;
        _onChanged = onChanged;
        _index = Mathf.Clamp(index, 0, Mathf.Max(0, _options.Length - 1));
        Refresh();
    }

    private void Refresh()
    {
        if (_label != null) _label.text = _options.Length > 0 ? _options[_index] : "-";
    }

    public void Step(int direction)
    {
        if (_options.Length == 0) return;
        int next = _index + direction;
        if (_wrap) next = (next % _options.Length + _options.Length) % _options.Length;
        else next = Mathf.Clamp(next, 0, _options.Length - 1);
        if (next == _index) return;
        _index = next;
        Refresh();
        _onChanged?.Invoke(_index);
    }

    public override void OnMove(AxisEventData eventData)
    {
        if (eventData.moveDir == MoveDirection.Left) { Step(-1); eventData.Use(); }
        else if (eventData.moveDir == MoveDirection.Right) { Step(1); eventData.Use(); }
        else base.OnMove(eventData);
    }

    public void OnSubmit(BaseEventData eventData)
    {
        if (_options.Length == 0) return;
        _index = (_index + 1) % _options.Length; // submit always wraps, so a two-option stepper is a toggle
        Refresh();
        _onChanged?.Invoke(_index);
    }
}
