/// <summary>
/// F119: every verb the game reads from a device, by name. Each member must have an action of the SAME NAME in
/// <c>Assets/SourceFiles/Resources/Input/KeepItLitControls.inputactions</c> (Move, Look and Sprint live in the Player map, owned by PlayerInput;
/// everything else is in Gameplay or Menu). <see cref="GameInput"/> logs an error at init for a member without an action. Recipe for a new
/// action: docs/systems.md, "Input (F119)".
/// </summary>
public enum GameAction
{
    // Player map (PlayerInput -> StarterAssetsInputs moves the player; the facade can still read them)
    Move,
    Look,
    Sprint,

    // Gameplay map (rebindable except Pause)
    Torch,
    Focus,
    Interact,
    Throw,
    Item1,
    Item2,
    Map,
    Pause,

    // Menu map (fixed, never rebindable)
    MenuSubmit,
    MenuCancel,
    MenuTabPrev,
    MenuTabNext,
    MenuChoice1,
    MenuChoice2,
    MenuScroll,
    MenuClick,
    MenuTap,
}
