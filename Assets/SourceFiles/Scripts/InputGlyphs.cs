using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F119: the label for an action in the player's current input device - the keyboard key, the gamepad button (Xbox / PlayStation / Nintendo
/// naming) or the on-screen touch word. Replaces <see cref="TouchInput.Key"/>. Labels follow <see cref="GameInput.LastDevice"/> and the live
/// (rebound) bindings; callers that show one on screen refresh when <see cref="GameInput.DeviceChanged"/> fires (the facade is the one event -
/// there is no second one here). Only words go through L10n; letter codes printed on the hardware (A, LB, L2, ZR ...) are constants.
/// ASCII only: the TMP fonts are not checked for the PlayStation shapes.
/// </summary>
public static class InputGlyphs
{
    private enum Family { Xbox, PlayStation, Nintendo }

    private static readonly Dictionary<int, string> Cache = new Dictionary<int, string>();
    private static int _cacheVersion = -1;
    private static string _cacheLang;
    private static bool _hooked;

    // L10n.Changed is cleared at every Play start, so the subscription is re-made on first use per session.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _hooked = false;
        _cacheVersion = -1;
        _cacheLang = null;
        Cache.Clear();
    }

    private static void OnLanguageChanged() => _cacheVersion = -1;

    /// <summary>The label in the current device. <paramref name="touchLabel"/> is what a touch screen shows (null = a default per action).</summary>
    public static string Key(GameAction a, string touchLabel = null)
    {
        switch (GameInput.LastDevice)
        {
            case GameInput.Device.Touch: return touchLabel ?? DefaultTouchLabel(a);
            case GameInput.Device.Gamepad: return PadLabel(a);
            default: return KeyboardLabel(a);
        }
    }

    public static string KeyboardLabel(GameAction a) => Cached((int)a * 8, () => ComputeKeyboard(a));

    public static string PadLabel(GameAction a)
    {
        Family f = CurrentFamily();
        return Cached((int)a * 8 + 1 + (int)f, () => ComputePad(a, f));
    }

    public static string DefaultTouchLabel(GameAction a)
    {
        switch (a)
        {
            case GameAction.Interact: return L10n.T("ui.key.use");
            case GameAction.Torch: return L10n.T("intake.key.torch");
            case GameAction.Focus: return L10n.T("intake.key.focus");
            case GameAction.MenuCancel: return L10n.T("ui.key.back");
            case GameAction.Look: return L10n.T("intake.key.right_thumb");
            case GameAction.Move: return L10n.T("intake.key.left_thumb");
            default: return L10n.T("ui.key.tap");
        }
    }

    private static string Cached(int key, System.Func<string> compute)
    {
        if (!_hooked)
        {
            _hooked = true;
            L10n.Changed += OnLanguageChanged;   // words (MOUSE, D-PAD LEFT ...) come from the tables: recompute after the language switched
        }
        string lang = L10n.Code;
        if (_cacheVersion != GameInput.Version || _cacheLang != lang)
        {
            Cache.Clear();
            _cacheVersion = GameInput.Version;
            _cacheLang = lang;
        }
        if (!Cache.TryGetValue(key, out string s))
        {
            s = compute();
            Cache[key] = s;
        }
        return s;
    }

    // ---------------------------------------------------------------- keyboard / mouse

    private static string ComputeKeyboard(GameAction a)
    {
        if (a == GameAction.Look) return L10n.T("ui.glyph.mouse");
        InputAction action = GameInput.Action(a);
        if (action == null) return L10n.T("ui.glyph.unbound");
        if (a == GameAction.Move) return MoveKeys(action);

        int index = FirstBinding(action, "KeyboardMouse");
        if (index < 0) return L10n.T("ui.glyph.unbound");
        return KeyName(action, index);
    }

    /// <summary>The up, left, down, right parts of the Move action's keyboard composite through the layout-aware display name (an AZERTY keyboard reads
    /// ZQSD); WASD when anything is missing or a part is not a single character.</summary>
    private static string MoveKeys(InputAction move)
    {
        string label = "WASD";
        try
        {
            string[] parts = new string[4];
            for (int i = 0; i < move.bindings.Count; i++)
            {
                InputBinding b = move.bindings[i];
                string path = b.effectivePath;
                if (!b.isPartOfComposite || path == null || !path.StartsWith("<Keyboard>")) continue;
                int slot = b.name == "up" ? 0 : b.name == "left" ? 1 : b.name == "down" ? 2 : b.name == "right" ? 3 : -1;
                if (slot < 0 || parts[slot] != null) continue;
                parts[slot] = KeyName(move, i);
            }
            if (parts[0] != null && parts[1] != null && parts[2] != null && parts[3] != null)
            {
                string joined = string.Concat(parts);
                if (joined.Length == 4) label = joined;
            }
        }
        catch (System.Exception) { /* keep WASD */ }
        return label;
    }

    private static string KeyName(InputAction action, int index)
    {
        string path = action.bindings[index].effectivePath ?? "";
        if (path.StartsWith("<Mouse>/") || path.StartsWith("<Pointer>/"))
        {
            switch (path.Substring(path.IndexOf('/') + 1))
            {
                case "leftButton": return L10n.T("ui.glyph.mouse_left");
                case "rightButton": return L10n.T("ui.glyph.mouse_right");
                case "middleButton": return L10n.T("ui.glyph.mouse_middle");
                default: return L10n.T("ui.glyph.mouse");
            }
        }

        string control = path.Substring(path.LastIndexOf('/') + 1);
        if (control.StartsWith("digit") && control.Length == 6) return control.Substring(5);
        if (control.StartsWith("numpad") && control.Length == 7 && char.IsDigit(control[6])) return control.Substring(6);
        switch (control)
        {
            case "escape": return "ESC";
            case "leftShift":
            case "rightShift": return "SHIFT";
            case "leftCtrl":
            case "rightCtrl": return "CTRL";
            case "leftAlt":
            case "rightAlt": return "ALT";
            case "space": return "SPACE";
            case "enter":
            case "numpadEnter": return "ENTER";
            case "tab": return "TAB";
            case "backspace": return "BKSP";
        }

        if (control.Length == 1 && char.IsDigit(control[0])) return control;   // <Keyboard>/1: never the layout's shifted character
        string shown = action.GetBindingDisplayString(index, InputBinding.DisplayStringOptions.DontIncludeInteractions);
        return string.IsNullOrEmpty(shown) ? L10n.T("ui.glyph.unbound") : shown.ToUpperInvariant();
    }

    // ---------------------------------------------------------------- gamepad

    private static Family CurrentFamily()
    {
        string layout = GameInput.PadLayout;
        if (layout == null) return Family.Xbox;
        // Layout strings only (no platform-specific types), so the Web build compiles. DualSense is based on DualShockGamepad.
        if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "DualShockGamepad")) return Family.PlayStation;
        if (InputSystem.IsFirstLayoutBasedOnSecond(layout, "SwitchProControllerHID")) return Family.Nintendo;
        return Family.Xbox;
    }

    private static string ComputePad(GameAction a, Family f)
    {
        InputAction action = GameInput.Action(a);
        if (action == null) return L10n.T("ui.glyph.unbound");
        int index = FirstBinding(action, "Gamepad");
        if (index < 0) return L10n.T("ui.glyph.unbound");
        string path = action.bindings[index].effectivePath ?? "";
        int slash = path.IndexOf('/');
        string control = slash >= 0 ? path.Substring(slash + 1) : path;
        return ControlLabel(control, f);
    }

    /// <summary>The label of a gamepad control path without its device prefix (<c>start</c>, <c>dpad/left</c>) in the connected pad's naming.</summary>
    public static string PadControlLabel(string control) => ControlLabel(control, CurrentFamily());

    /// <summary>The label of one binding of an action, by its group (RebindPanel cells).</summary>
    public static string BindingLabel(InputAction action, int index)
    {
        string path = action.bindings[index].effectivePath ?? "";
        if (path.StartsWith("<Gamepad>") || HasGroup(action.bindings[index], "Gamepad")) { int s = path.IndexOf('/'); return ControlLabel(s >= 0 ? path.Substring(s + 1) : path, CurrentFamily()); }
        return KeyName(action, index);
    }

    private static string ControlLabel(string control, Family f)
    {
        switch (control)
        {
            case "buttonSouth": return f == Family.PlayStation ? L10n.T("ui.glyph.ps.cross") : f == Family.Nintendo ? "B" : "A";
            case "buttonEast": return f == Family.PlayStation ? L10n.T("ui.glyph.ps.circle") : f == Family.Nintendo ? "A" : "B";
            case "buttonWest": return f == Family.PlayStation ? L10n.T("ui.glyph.ps.square") : f == Family.Nintendo ? "Y" : "X";
            case "buttonNorth": return f == Family.PlayStation ? L10n.T("ui.glyph.ps.triangle") : f == Family.Nintendo ? "X" : "Y";
            case "leftShoulder": return f == Family.PlayStation ? "L1" : f == Family.Nintendo ? "L" : "LB";
            case "rightShoulder": return f == Family.PlayStation ? "R1" : f == Family.Nintendo ? "R" : "RB";
            case "leftTrigger": return f == Family.PlayStation ? "L2" : f == Family.Nintendo ? "ZL" : "LT";
            case "rightTrigger": return f == Family.PlayStation ? "R2" : f == Family.Nintendo ? "ZR" : "RT";
            case "leftStickPress":
            case "leftStickButton": return f == Family.PlayStation ? "L3" : "LS";
            case "rightStickPress":
            case "rightStickButton": return f == Family.PlayStation ? "R3" : "RS";
            case "start": return f == Family.PlayStation ? L10n.T("ui.glyph.options") : f == Family.Nintendo ? "+" : L10n.T("ui.glyph.menu");
            case "select": return f == Family.PlayStation ? L10n.T("ui.glyph.create") : f == Family.Nintendo ? "-" : L10n.T("ui.glyph.view");
            case "dpad/up": return L10n.T("ui.glyph.dpad_up");
            case "dpad/down": return L10n.T("ui.glyph.dpad_down");
            case "dpad/left": return L10n.T("ui.glyph.dpad_left");
            case "dpad/right": return L10n.T("ui.glyph.dpad_right");
        }
        if (control.StartsWith("leftStick")) return L10n.T("ui.glyph.lstick");
        if (control.StartsWith("rightStick")) return L10n.T("ui.glyph.rstick");
        return control.Length == 0 ? L10n.T("ui.glyph.unbound") : control.ToUpperInvariant();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Index of the action's first non-composite-part binding (or composite part, for Move) in the binding group, or -1.</summary>
    public static int FirstBinding(InputAction action, string group)
    {
        for (int i = 0; i < action.bindings.Count; i++)
        {
            InputBinding b = action.bindings[i];
            if (b.isComposite) continue;
            if (!HasGroup(b, group)) continue;
            if (string.IsNullOrEmpty(b.effectivePath)) continue;
            return i;
        }
        return -1;
    }

    public static bool HasGroup(InputBinding b, string group)
    {
        if (string.IsNullOrEmpty(b.groups)) return false;
        foreach (string g in b.groups.Split(InputBinding.Separator))
            if (g == group) return true;
        return false;
    }
}
