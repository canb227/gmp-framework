using Godot;
using PolyType;
using System;
using System.Collections.Generic;

[GenerateShape]
public partial record struct SmelterSave
{
    public string itemInSmelter;
}

/// <summary>Takes in one item with a recipe per cycle, picked at random from its inputs, and puts out the recipe's result.</summary>
public partial class BasicSmelter : Machine
{
    /// <summary>Input itemID → output itemID.</summary>
    [Export] public Godot.Collections.Dictionary<string, string> recipes = new();

    /// <summary>Raised with the id of each input item the smelter takes in (only on the peer running its machine logic).</summary>
    public event Action<ulong> Smelted;

    /// <summary>The item being smelted (authority only).</summary>
    string itemInSmelter;

    protected override bool TryStartProcess()
    {
        List<PhysicalFactoryItem> items = ItemsInInputs();
        if (items.Count == 0) return false;

        PhysicalFactoryItem selected = items[Random.Shared.Next(items.Count)];
        if (!recipes.ContainsKey(selected.itemID ?? ""))
        {
            return false;
        }
        itemInSmelter = selected.itemID;
        GameWorld.DespawnObject(selected.id, DespawnReason.Consumed);
        Smelted?.Invoke(selected.id);
        return true;
    }

    protected override void FinishProcess()
    {
        if (recipes.TryGetValue(itemInSmelter, out string outputItemID))
        {
            ItemInfo.SpawnInWorld(outputItemID, OutputPosition(), GlobalRotation);
        }
        itemInSmelter = null;
    }

    protected override byte[] SaveMachineState()
    {
        if (itemInSmelter == null) return null;
        return GMPObject.serializer.Serialize(new SmelterSave { itemInSmelter = itemInSmelter });
    }

    protected override void LoadMachineState(byte[] state)
    {
        itemInSmelter = GMPObject.serializer.Deserialize<SmelterSave>(state).itemInSmelter;
    }
}
