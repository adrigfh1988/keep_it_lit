using UnityEngine;

/// <summary>
/// F111: the lever beside an exit gate (on the template's LeverBox; its LeverFocus child carries the collider PlayerInteractor's probe hits).
/// An IProgressInteractable: holding E fills the gate's Progress, letting go leaves it where it was. It stays focusable while it cannot be used so
/// the prompt can say why - NO POWER before the escape opens, LOCKED with the missing keys on a Key Hunt floor.
/// </summary>
public class GateLever : MonoBehaviour, IProgressInteractable
{
    private ExitGate _gate;

    public void Bind(ExitGate gate) => _gate = gate;

    float IProgressInteractable.Progress01 => _gate != null ? _gate.Progress01 : 0f;

    string IInteractable.Prompt
    {
        get
        {
            if (_gate == null) return null;
            switch (_gate.State)
            {
                case ExitGate.GateState.Dormant: return L10n.T("hud.gate.no_power");
                case ExitGate.GateState.Powered:
                    if (_gate.IsLocked && _gate.Keys != null) return L10n.T("hud.gate.locked", ("h", _gate.Keys.Held), ("t", _gate.Keys.Total));
                    return L10n.T("hud.gate.pull");
                default: return null; // opening or open: nothing left to pull
            }
        }
    }

    bool IInteractable.CanInteract => _gate != null && _gate.CanPull;

    /// <summary>Unused: PlayerInteractor drives an IProgressInteractable through HoldTick.</summary>
    float IInteractable.HoldSeconds => 0f;

    void IInteractable.Interact(PlayerInteractor who) { }

    void IProgressInteractable.HoldTick(float dt)
    {
        if (_gate != null) _gate.Pull(dt);
    }

    void IInteractable.OnFocus(bool focused) { }
}
