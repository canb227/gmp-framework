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
/// them oldest first. Each machine spawns the outputs of <see cref="currentRecipe"/> itself in
/// <see cref="Machine.FinishProcess"/>.
/// </summary>
public abstract partial class RecipeMachine : IOMachine
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
                foreach (PhysicalFactoryItem input in inputItems)
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
