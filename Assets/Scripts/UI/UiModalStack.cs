using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Formal push/pop stack for modal overlays (<see cref="SettingsMenu"/>, <see cref="NewCampaignScreen"/>,
/// <see cref="PatientRecordMenu"/>, <see cref="RebindPanel"/>, <see cref="ShopMenu"/>, <see cref="FloorMapUi"/>,
/// <see cref="DialoguePanel"/>) so same-frame Esc/Cancel suppression and top-modal queries are centralized.
/// </summary>
public static class UiModalStack
{
    private static readonly List<object> Stack = new List<object>();
    private static int _lastPopFrame = -10;

    /// <summary>Number of currently open modal overlays.</summary>
    public static int Count => Stack.Count;

    /// <summary>Top-most modal overlay on the stack, or null when empty.</summary>
    public static object Top => Stack.Count > 0 ? Stack[Stack.Count - 1] : null;

    /// <summary>True when at least one modal overlay is open.</summary>
    public static bool AnyOpen => Stack.Count > 0;

    /// <summary>True on the exact frame the most recent modal was popped.</summary>
    public static bool ClosedThisFrame => _lastPopFrame == Time.frameCount;

    /// <summary>True while any modal is open or on the frame a modal closed (blocks Pause / Back propagation).</summary>
    public static bool BlocksCancelOrPause => AnyOpen || ClosedThisFrame;

    public static void Push(object modal)
    {
        if (modal == null) return;
        Stack.Remove(modal);
        Stack.Add(modal);
    }

    public static void Pop(object modal)
    {
        if (modal == null) return;
        if (Stack.Remove(modal))
        {
            _lastPopFrame = Time.frameCount;
        }
    }

    public static bool IsOpen(object modal) => modal != null && Stack.Contains(modal);

    public static bool IsTop(object modal) => modal != null && ReferenceEquals(Top, modal);

    public static void Clear()
    {
        Stack.Clear();
        _lastPopFrame = -10;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => Clear();
}
