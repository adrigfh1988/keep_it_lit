using UnityEngine;

/// <summary>
/// F93: Elias's wristband on the morgue table (floor 4 on). E shows one subtitle line. The object (AsylumHub/Life/EliasMorgue) is
/// only active from floor 4 - EliasMorgue itself is switched by HubPatient.RefreshMorgue.
/// </summary>
public class PatientWristband : MonoBehaviour, IInteractable
{
    public const string Line = "The wristband is cold. Drawer five is open.";

    string IInteractable.Prompt => "READ WRISTBAND · ELIAS VOGT · PATIENT 5";
    float IInteractable.HoldSeconds => 0f;
    bool IInteractable.CanInteract => true;

    void IInteractable.Interact(PlayerInteractor who)
    {
        AsylumHub hub = AsylumHub.Active;
        if (hub != null && hub.Hud != null) hub.Hud.ShowSubtitle(Line, 5f);
    }

    void IInteractable.OnFocus(bool focused) { }
}
