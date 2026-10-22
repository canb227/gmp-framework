using Godot;

/// <summary>
/// The tool for taking structures apart: holding left click with it on a structure does what holding X does with
/// anything equipped. The player does the counting and asks the host (<see cref="FactoryPlayer.UpdateDeconstruct"/>);
/// this only takes left click, so nothing else uses it, and makes the player show the hint on structures.
/// </summary>
public partial class Wrench : HeldItem
{
    public override bool HandleInput(InputEvent @event)
    {
        return @event.IsActionPressed(InputActions.primary) || @event.IsActionReleased(InputActions.primary);
    }
}
