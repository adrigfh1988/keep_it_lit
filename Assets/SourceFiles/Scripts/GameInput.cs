using System;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// F119: the one place gameplay and menu code reads a device. <c>GameInput.Pressed(GameAction.Torch)</c> is the action's keyboard/mouse
/// OR gamepad state OR'ed with the <see cref="TouchInput"/> twin (F76, same one-frame lag as ever). Never read
/// <c>Keyboard.current</c> / <c>Gamepad.current</c> / <c>Mouse.current</c> anywhere else - the allowed device sniffers are this file,
/// <c>TouchInput.Poll</c> (touch auto-detect) and <c>MobilePerformance</c> (<c>Touchscreen.current</c>).
///
/// It reads a private CLONE of the controls asset, not <c>PlayerInput.actions</c>: PlayerInput auto-switches control schemes and masks the
/// other device's bindings, whereas every reader before F119 OR'ed all devices unconditionally, and the clone (no mask, all maps enabled)
/// keeps exactly that. Movement itself is still driven by PlayerInput -> StarterAssetsInputs; rebinds are applied to both assets.
/// Recipe for a new action: docs/systems.md, "Input (F119)".
/// </summary>
public static class GameInput
{
    public enum Device { KeyboardMouse, Gamepad, Touch }

    private const string ResourcePath = "Input/KeepItLitControls";
    private const float MouseMoveThresholdSq = 4f;   // same threshold as TouchInput's auto-off

    private static InputActionAsset _source;          // the asset PlayerInput uses
    private static InputActionAsset _clone;           // what every read goes through
    private static InputAction[] _actions;
    private static bool _initFailed;
    private static int _pollFrame = -1;
    private static Device _physical = Device.KeyboardMouse;
    private static Device _effective = Device.KeyboardMouse;
    private static bool _gamepadUsed;

    /// <summary>Raised when the effective device changes (touch on/off included) or the bindings change (rebind, reset). Cleared on Play start;
    /// subscribers unsubscribe in OnDisable/OnDestroy. <see cref="Version"/> also moves, for callers that would rather poll a number.</summary>
    public static event Action DeviceChanged;

    /// <summary>Bumped every time <see cref="DeviceChanged"/> is raised.</summary>
    public static int Version { get; private set; }

    /// <summary>The device the player last used; Touch while the on-screen controls are up.</summary>
    public static Device LastDevice { get { Poll(); return _effective; } }

    /// <summary>A gamepad button, trigger, d-pad or stick was used this frame (TouchInput turns the on-screen controls off on it).</summary>
    public static bool GamepadUsedThisFrame { get { Poll(); return _gamepadUsed; } }

    /// <summary>The input layout name of the gamepad in use (Gamepad.current, else the first one connected), null with none - InputGlyphs picks the Xbox / PlayStation / Nintendo naming from it.</summary>
    public static string PadLayout
    {
        get
        {
            Gamepad pad = Gamepad.current;
            if (pad == null && Gamepad.all.Count > 0) pad = Gamepad.all[0];
            return pad != null ? pad.layout : null;
        }
    }

    /// <summary>The clone (rebinding, glyphs).</summary>
    public static InputActionAsset Asset { get { EnsureInit(); return _clone; } }

    // ---------------------------------------------------------------- lifecycle

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        // Disable the old clone first: with Reload Domain off an enabled stale clone would keep receiving input across Play sessions.
        if (_clone != null)
        {
            _clone.Disable();
            UnityEngine.Object.Destroy(_clone);
        }
        _clone = null;
        _actions = null;
        _initFailed = false;
        DeviceChanged = null;
        Version = 0;
        _pollFrame = -1;
        _physical = Device.KeyboardMouse;
        _effective = Device.KeyboardMouse;
        _gamepadUsed = false;
        if (_source != null) _source.RemoveAllBindingOverrides();
        _source = null;
    }

    private static void EnsureInit()
    {
        if (_actions != null || _initFailed) return;
        _source = Resources.Load<InputActionAsset>(ResourcePath);
        if (_source == null)
        {
            _initFailed = true;
            Debug.LogError("[GameInput] Resources/" + ResourcePath + ".inputactions is missing - no input will be read.");
            return;
        }

        _clone = InputActionAsset.FromJson(_source.ToJson());
        _clone.name = "KeepItLitControls (GameInput)";

        string[] names = Enum.GetNames(typeof(GameAction));
        _actions = new InputAction[names.Length];
        for (int i = 0; i < names.Length; i++)
        {
            GameAction a = (GameAction)i;
            bool inPlayer = a == GameAction.Move || a == GameAction.Look || a == GameAction.Sprint;
            InputAction found = _clone.FindAction(inPlayer ? "Player/" + names[i] : names[i], false);
            _actions[i] = found;
            if (found == null) Debug.LogError("[GameInput] no action for GameAction." + names[i] + " - add it to KeepItLitControls.inputactions");
        }

        ApplyOverridesInternal(GameSettings.InputOverrides);
        _clone.Enable();
    }

    /// <summary>Idempotent per frame: finds the last-used device and keeps <see cref="TouchInput.Active"/> up to date (TouchControls calls it
    /// every frame; every public read calls it too).</summary>
    public static void Poll()
    {
        int frame = Time.frameCount;
        if (frame == _pollFrame) return;
        _pollFrame = frame;
        EnsureInit();

        bool pad = false, kbMouse = false;
        Gamepad current = Gamepad.current;
        if (current != null) pad = PadUsed(current);
        else if (Gamepad.all.Count > 0) { for (int i = 0; i < Gamepad.all.Count && !pad; i++) pad = PadUsed(Gamepad.all[i]); }

        Keyboard kb = Keyboard.current;
        if (kb != null && kb.anyKey.wasPressedThisFrame) kbMouse = true;
        Mouse mouse = Mouse.current;
        if (mouse != null && (mouse.leftButton.wasPressedThisFrame || mouse.rightButton.wasPressedThisFrame || mouse.middleButton.wasPressedThisFrame ||
                              mouse.delta.ReadValue().sqrMagnitude > MouseMoveThresholdSq)) kbMouse = true;

        _gamepadUsed = pad;
        if (pad) _physical = Device.Gamepad;
        else if (kbMouse) _physical = Device.KeyboardMouse;

        TouchInput.Poll();   // re-enters Poll() harmlessly (frame already stamped) and settles Active before we read it

        Device effective = TouchInput.Active ? Device.Touch : _physical;
        if (effective != _effective)
        {
            _effective = effective;
            RaiseChanged();
        }
    }

    private static bool PadUsed(Gamepad g)
    {
        if (g.buttonSouth.wasPressedThisFrame || g.buttonEast.wasPressedThisFrame || g.buttonWest.wasPressedThisFrame || g.buttonNorth.wasPressedThisFrame) return true;
        if (g.leftShoulder.wasPressedThisFrame || g.rightShoulder.wasPressedThisFrame || g.leftStickButton.wasPressedThisFrame || g.rightStickButton.wasPressedThisFrame) return true;
        if (g.startButton.wasPressedThisFrame || g.selectButton.wasPressedThisFrame) return true;
        if (g.dpad.up.wasPressedThisFrame || g.dpad.down.wasPressedThisFrame || g.dpad.left.wasPressedThisFrame || g.dpad.right.wasPressedThisFrame) return true;
        if (g.leftTrigger.ReadValue() > 0.4f || g.rightTrigger.ReadValue() > 0.4f) return true;
        return g.leftStick.ReadValue().sqrMagnitude > 0.25f || g.rightStick.ReadValue().sqrMagnitude > 0.25f;
    }

    private static void RaiseChanged()
    {
        Version++;
        DeviceChanged?.Invoke();
    }

    // ---------------------------------------------------------------- reads

    private static InputAction Read(GameAction a)
    {
        EnsureInit();
        Poll();
        return _actions != null ? _actions[(int)a] : null;
    }

    public static bool Pressed(GameAction a)
    {
        InputAction act = Read(a);
        if (act != null && act.WasPressedThisFrame()) return true;
        TouchInput.TouchAction? twin = Twin(a);
        return twin.HasValue && TouchInput.Pressed(twin.Value);
    }

    public static bool Held(GameAction a)
    {
        InputAction act = Read(a);
        if (act != null && act.IsPressed()) return true;
        TouchInput.TouchAction? twin = Twin(a);
        return twin.HasValue && TouchInput.Held(twin.Value);
    }

    public static bool Released(GameAction a)
    {
        InputAction act = Read(a);
        if (act != null && act.WasReleasedThisFrame()) return true;
        TouchInput.TouchAction? twin = Twin(a);
        return twin.HasValue && TouchInput.Released(twin.Value);
    }

    /// <summary>The 1D value of a Value/Axis action (MenuScroll). 0 when unavailable.</summary>
    public static float Axis(GameAction a)
    {
        InputAction act = Read(a);
        return act != null ? act.ReadValue<float>() : 0f;
    }

    /// <summary>The raw action of the clone, or null.</summary>
    public static InputAction Action(GameAction a)
    {
        EnsureInit();
        return _actions != null ? _actions[(int)a] : null;
    }

    /// <summary>The on-screen button that stands in for an action (null = none; MenuSubmit's touch equivalent is Interact, OR'ed explicitly by the readers that need it).</summary>
    private static TouchInput.TouchAction? Twin(GameAction a)
    {
        switch (a)
        {
            case GameAction.Interact: return TouchInput.TouchAction.Interact;
            case GameAction.Torch: return TouchInput.TouchAction.Torch;
            case GameAction.Focus: return TouchInput.TouchAction.Focus;
            case GameAction.Throw: return TouchInput.TouchAction.Throw;
            case GameAction.Item1: return TouchInput.TouchAction.Item1;
            case GameAction.Item2: return TouchInput.TouchAction.Item2;
            case GameAction.Pause: return TouchInput.TouchAction.Pause;
            case GameAction.MenuCancel: return TouchInput.TouchAction.Pause;
            case GameAction.MenuChoice1: return TouchInput.TouchAction.Item1;
            case GameAction.MenuChoice2: return TouchInput.TouchAction.Item2;
            default: return null;
        }
    }

    // ---------------------------------------------------------------- overrides (rebinding)

    /// <summary>Loads binding overrides (the JSON <see cref="SaveOverrides"/> produced; "" = defaults) into the clone AND the PlayerInput source
    /// asset (so Move/Sprint follow), then raises <see cref="DeviceChanged"/> so every glyph refreshes.</summary>
    public static void ApplyOverrides(string json)
    {
        EnsureInit();
        ApplyOverridesInternal(json);
        RaiseChanged();
    }

    private static void ApplyOverridesInternal(string json)
    {
        if (_clone == null || _source == null) return;
        try
        {
            if (string.IsNullOrEmpty(json))
            {
                _clone.RemoveAllBindingOverrides();
                _source.RemoveAllBindingOverrides();
            }
            else
            {
                _clone.LoadBindingOverridesFromJson(json, true);
                _source.LoadBindingOverridesFromJson(json, true);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[GameInput] could not apply binding overrides, using defaults: " + e.Message);
            _clone.RemoveAllBindingOverrides();
            _source.RemoveAllBindingOverrides();
        }
    }

    /// <summary>The clone's overrides as JSON, "" when nothing differs from the defaults.</summary>
    public static string SaveOverrides()
    {
        EnsureInit();
        if (_clone == null) return "";
        bool any = false;
        foreach (InputActionMap map in _clone.actionMaps)
        {
            foreach (InputBinding b in map.bindings)
                if (!string.IsNullOrEmpty(b.overridePath) || !string.IsNullOrEmpty(b.overrideProcessors) || !string.IsNullOrEmpty(b.overrideInteractions)) { any = true; break; }
            if (any) break;
        }
        return any ? _clone.SaveBindingOverridesAsJson() : "";
    }
}
