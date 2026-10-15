using UnityEngine;

/// <summary>
/// F123 floor 4 (Dr. Weiss): a clear-glass, blue-capped ampoule lying on the floor where no lamp reaches (InteractableKit.nightmareAmpoule, cloned by
/// MazeGenerator.BuildAmpoules). E takes it and takes the dose: <see cref="WingNightmareDirector.TakeAmpoule"/> numbs the fear for a while.
/// Optional, never required. Deliberately not a Pickup (the star count) and not a DescentItem; a trigger collider only, so it never touches the NavMesh.
/// </summary>
public class NightmareAmpoule : MonoBehaviour, IInteractable
{
    private WingNightmareDirector _director;
    private bool _taken;

    public bool IsTaken => _taken;

    /// <summary>Late-bound by MazeGenerator.SetUpAtmosphere once the director exists.</summary>
    public void Bind(WingNightmareDirector director) => _director = director;

    string IInteractable.Prompt => L10n.T("hud.prompt.ampoule");
    bool IInteractable.CanInteract => !_taken && _director != null && GameFlow.IsRunActive && !GameOutcome.IsOver;
    float IInteractable.HoldSeconds => 0f;
    void IInteractable.OnFocus(bool focused) { }

    void IInteractable.Interact(PlayerInteractor who)
    {
        if (_taken) return;
        _taken = true;
        foreach (Renderer r in GetComponentsInChildren<Renderer>(true)) r.enabled = false;
        foreach (Collider c in GetComponentsInChildren<Collider>(true)) c.enabled = false;
        _director?.TakeAmpoule(this);
    }
}
