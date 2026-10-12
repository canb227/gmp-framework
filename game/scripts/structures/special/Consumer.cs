using System.Collections.Generic;

/// <summary>Eats one item from its inputs per cycle and turns it in for the quests (<see cref="ProgressManager.RequestDeposit"/>).</summary>
public partial class Consumer : Machine
{
    protected override bool TryStartProcess()
    {
        List<PhysicalFactoryItem> items = ItemsInInputs();
        if (items.Count == 0) return false;

        PhysicalFactoryItem item = items[0];
        // Read what's needed before despawning, which may free the item.
        string itemID = item.itemID;
        Dictionary<Currency, int> value = new(item.currencyValue);
        GameWorld.DespawnObject(item.id, DespawnReason.Consumed);
        ProgressManager.RequestDeposit(itemID, value);
        return true;
    }
}
