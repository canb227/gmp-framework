using Godot;

/// <summary>
/// The blueprint of a belt that turns by itself (see <see cref="Conveyor"/>): <see cref="BlueprintItem.structureScene"/>
/// is the straight belt, and the turns are swapped in when a belt is fed from one side. All three give back this blueprint.
/// </summary>
[GlobalClass]
public partial class ConveyorBlueprint : BlueprintItem
{
    [Export]
    public PackedScene turnLeftScene;

    [Export]
    public PackedScene turnRightScene;

    public PackedScene SceneFor(ConveyorShape shape)
    {
        if (shape == ConveyorShape.TurnLeft && turnLeftScene != null)
        {
            return turnLeftScene;
        }
        if (shape == ConveyorShape.TurnRight && turnRightScene != null)
        {
            return turnRightScene;
        }
        return structureScene;
    }
}
