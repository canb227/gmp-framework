using Godot;

/// <summary>
/// A dev structure: deletes the loose items that land in it, oldest first, at most <see cref="itemsPerSecond"/> (0: all
/// of them as soon as they arrive). Unlike the Item Void it turns nothing in for the quests. Test levels use it as the
/// input end of a placeholder machine, or the end of a line.
/// </summary>
public partial class DevItemDeleter : IOMachine
{
    /// <summary>The most items it deletes a second; 0 deletes everything that arrives at once.</summary>
    [Export] public float itemsPerSecond = 0f;

    double deletesOwed;

    public override void AfterInit()
    {
        base.AfterInit();
        if (itemsPerSecond <= 0)
        {
            hoverText = "Deletes every item that lands in it";
        }
        else
        {
            hoverText = $"Deletes up to {itemsPerSecond:0.#} items a second";
        }
    }

    // Runs on its own clock rather than in cycles: a cycle tells every peer when it starts and stops, this sends nothing.
    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic)
        {
            return;
        }
        if (itemsPerSecond <= 0)
        {
            while (inputItems.Count > 0)
            {
                ConsumeInputItem(inputItems[0]);
            }
            return;
        }
        deletesOwed += itemsPerSecond * delta;
        while (deletesOwed >= 1 && inputItems.Count > 0)
        {
            ConsumeInputItem(inputItems[0]);
            deletesOwed -= 1;
        }
        // Time spent empty doesn't bank deletes for later.
        if (inputItems.Count == 0)
        {
            deletesOwed = Mathf.Min(deletesOwed, 1);
        }
    }

    protected override bool TryStartProcess()
    {
        return false;
    }

    protected override void FinishProcess()
    {
    }
}
