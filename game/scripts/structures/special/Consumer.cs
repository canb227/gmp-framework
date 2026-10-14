using System.Collections.Generic;

/// <summary>Eats one item from its inputs per cycle and turns it in for the quests (<see cref="ProgressManager.RequestDeposit"/>).</summary>
public partial class Consumer : RecipeMachine
{
    protected override bool TryStartProcess()
    {
        if (inputItems.Count == 0) return false;

        PhysicalFactoryItem item = inputItems[0];
        // Read what's needed before despawning, which may free the item.
        string itemID = item.itemID;
        Dictionary<Currency, int> value = new(item.currencyValue);
        ConsumeInputItem(item);
        ProgressManager.RequestDeposit(itemID, value);
        processTimeRemaining = 1f;
        return true;
    }

    protected override void FinishProcess()
    {
    }
}
