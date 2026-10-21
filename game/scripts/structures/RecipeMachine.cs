using Godot;
using PolyType;

[GenerateShape]
public partial record struct RecipeMachineSave
{
    /// <summary>Index in <see cref="RecipeMachine.recipes"/> of the recipe being made, or -1.</summary>
    public int recipeIndex;
}

/// <summary>
/// A machine that makes <see cref="recipes"/>: when idle it starts the first one whose inputs are all there, taking
/// them oldest first, and when the cycle is done it spawns the recipe's outputs at its first output
/// (<see cref="IOMachine.OutputPosition"/>). Items no recipe wants stay in the input. Use it as is (the grinder), or
/// override <see cref="FinishProcess"/> to put the outputs out differently.
/// <para>
/// Known gap: an item grabbed or picked up at the moment it enters the input is decided separately by its own
/// authority, so in that race it could be both kept and used.
/// </para>
/// </summary>
public partial class RecipeMachine : IOMachine
{
    /// <summary>What this machine can make, in order of preference.</summary>
    [Export] public Godot.Collections.Array<Recipe> recipes = new();

    /// <summary>Every peer: the recipe being made, or last made (null before the first).</summary>
    public Recipe currentRecipe { get; private set; }

    protected override bool TryStartProcess()
    {
        foreach (Recipe recipe in recipes)
        {
            // Are all of the recipe's inputs here?
            bool hasInputs = true;
            foreach (var (item, count) in recipe.inputs)
            {
                int found = 0;
                foreach (ItemRecord input in inputItems)
                {
                    if (input.itemID == item.itemID) found++;
                }
                if (found < count) hasInputs = false;
            }
            if (!hasInputs) continue;

            // Take them, oldest first. Consuming removes the item from the list, so only step past items we keep.
            foreach (var (item, count) in recipe.inputs)
            {
                int left = count;
                int i = 0;
                while (i < inputItems.Count && left > 0)
                {
                    if (inputItems[i].itemID == item.itemID)
                    {
                        ConsumeInputItem(inputItems[i]);
                        left--;
                    }
                    else
                    {
                        i++;
                    }
                }
            }
            processTimeRemaining = recipe.baseTime;
            RPCManager.RPC(this, nameof(SetRecipe), [recipes.IndexOf(recipe)]);
            return true;
        }
        return false;
    }

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

    [RPC(requireAuthority = true)]
    protected void SetRecipe(int recipeIndex)
    {
        currentRecipe = null;
        if (recipeIndex >= 0) currentRecipe = recipes[recipeIndex];
    }


    // ---- saving ----

    protected override byte[] SaveMachineState()
    {
        return GMPObject.serializer.Serialize(new RecipeMachineSave { recipeIndex = recipes.IndexOf(currentRecipe)});
    }

    protected override void LoadMachineState(byte[] state)
    {
        RecipeMachineSave s = GMPObject.serializer.Deserialize<RecipeMachineSave>(state);
        currentRecipe = null;
        if (s.recipeIndex >= 0 && s.recipeIndex < recipes.Count) currentRecipe = recipes[s.recipeIndex];
    }
}
