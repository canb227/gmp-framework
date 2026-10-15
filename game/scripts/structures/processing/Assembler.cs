/// <summary>Assembles what reaches its inputs by its <see cref="RecipeMachine.recipes"/>.</summary>
public partial class Assembler : RecipeMachine
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
