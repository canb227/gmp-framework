using Godot;
using PolyType;

[GenerateShape]
public partial record struct GrinderSave
{
    public string itemInGrinder;
}

/// <summary>
/// A machine that grinds what reaches its inputs, one item per cycle: each comes out transformed (<see cref="grindsInto"/>,
/// <see cref="outputCounts"/> of them side by side) at its output (<see cref="IOMachine.OutputPosition"/>), placed so
/// they land on a conveyor there. Items without a recipe come out unchanged.
/// <para>
/// Known gap: an item grabbed or picked up at the moment it enters the hopper is decided separately by
/// its own authority, so in that race it could be both kept and ground.
/// </para>
/// </summary>
public partial class Grinder : RecipeMachine
{
    protected override void FinishProcess()
    {
        if (currentRecipe == null) return; // a save from before recipes were saved
        foreach (var (item, count) in currentRecipe.outputs)
        {
            for (int i = 0; i < count; i++)
            {
                ItemInfo.SpawnInWorld(item.itemID, OutputPosition(), GlobalRotation);
            }
        }
    }
}
