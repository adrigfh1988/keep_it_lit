using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// F119: keeps a gamepad able to drive an open menu. The open menu calls <see cref="Keep"/> EVERY frame (same "every frame" rule as
/// <c>PlayerLock.SetCursorFree</c>): while the last-used device is a gamepad it makes sure a usable button of that menu is the EventSystem's
/// selection, and tints the selected one so the focus is visible. With a mouse or keyboard it does nothing visible - the selection it made is
/// dropped when the device changes, so a stuck highlight never shows. <c>RuntimeUi.CreateButton</c> keeps <c>selectedColor == idle</c> on purpose
/// (a clicked button must not stay lit); the tint here is applied only while the pad is the device and undone when the selection moves.
/// New menus: call <c>MenuFocus.Keep(root, firstButton)</c> in Update while open and only while this menu is the top one (a menu opened over
/// another one must stop the lower menu from calling it). Do not use it where the same press is also read directly (ledger, dialogue, map).
/// </summary>
public static class MenuFocus
{
    private static GameObject _ours;           // the selection we made (dropped when the pad stops being the device)
    private static Selectable _tinted;
    private static Color _originalSelected;
    private static bool _wasPad;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _ours = null;
        _tinted = null;
        _wasPad = false;
    }

    public static void Keep(GameObject root, Selectable preferred)
    {
        EventSystem es = EventSystem.current;
        if (es == null || root == null) return;

        if (GameInput.LastDevice != GameInput.Device.Gamepad)
        {
            if (_wasPad)
            {
                _wasPad = false;
                Untint();
                if (_ours != null && es.currentSelectedGameObject == _ours) es.SetSelectedGameObject(null);
                _ours = null;
            }
            return;
        }
        _wasPad = true;

        GameObject current = es.currentSelectedGameObject;
        Selectable sel = current != null ? current.GetComponent<Selectable>() : null;
        bool valid = current != null && current.activeInHierarchy && sel != null && sel.IsInteractable() && current.transform.IsChildOf(root.transform);
        if (!valid)
        {
            Selectable pick = Usable(preferred) ? preferred : First(root);
            if (pick != null)
            {
                es.SetSelectedGameObject(pick.gameObject);
                _ours = pick.gameObject;
                sel = pick;
            }
            else sel = null;
        }
        Tint(sel);
    }

    /// <summary>Drops the pad highlight now (a menu closing).</summary>
    public static void Release()
    {
        Untint();
        _wasPad = false;
        EventSystem es = EventSystem.current;
        if (es != null && _ours != null && es.currentSelectedGameObject == _ours) es.SetSelectedGameObject(null);
        _ours = null;
    }

    private static bool Usable(Selectable s) => s != null && s.gameObject.activeInHierarchy && s.IsInteractable();

    private static Selectable First(GameObject root)
    {
        foreach (Selectable s in root.GetComponentsInChildren<Selectable>(false))
            if (Usable(s) && s.navigation.mode != Navigation.Mode.None) return s;
        return null;
    }

    private static void Tint(Selectable sel)
    {
        if (sel == _tinted) return;
        Untint();
        if (sel == null) return;
        _tinted = sel;
        ColorBlock c = sel.colors;
        _originalSelected = c.selectedColor;
        c.selectedColor = c.highlightedColor;
        sel.colors = c;
    }

    private static void Untint()
    {
        if (_tinted != null)
        {
            // Only the selected tint is ours to undo: anything else (a settings tab button re-coloured meanwhile) stays as its owner set it.
            ColorBlock c = _tinted.colors;
            c.selectedColor = _originalSelected;
            _tinted.colors = c;
        }
        _tinted = null;
    }
}
