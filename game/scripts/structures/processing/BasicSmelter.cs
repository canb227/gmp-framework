using Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A furnace: items dropped into its walled hopper (its input port's volume, <see cref="Structure.ItemsAtInputs"/>)
/// are smelted one at a time; after <see cref="processTime"/> each comes out as its <see cref="recipes"/> output at
/// its output port's spawn (<see cref="Structure.OutputPosition"/>), in front of its mouth. Items without a recipe
/// are left in the hopper. Runs on the smelter's authority, which despawns inputs and spawns outputs for everyone.
/// <para>
/// Known gap: an item grabbed or picked up at the moment it enters the hopper is decided separately by
/// its own authority, so in that race it could be both kept and ground.
/// </para>
/// </summary>
/// <summary>What a save keeps of a <see cref="BasicSmelter"/>: the item it was part way through (its input is already gone).</summary>
[GenerateShape]
public partial record struct SmelterSave
{
    public string itemInSmelter;
    public double processTimeRemaining;
}

public partial class BasicSmelter : Structure
{
    /// <summary>Seconds to grind one input item; queued items are ground one after another.</summary>
    [Export] public double processTime = 1.0;
    public double processTimeRemaining = 0;
    /// <summary>Input itemID → output itemID.</summary>
    [Export] public Godot.Collections.Dictionary<string, string> recipes = new();

    /// <summary>Raised with the id of each input item the smelter takes in (only on the peer running its machine logic).</summary>
    public event Action<ulong> Consumed;

    public bool currentlyProcessing = false;
    public string itemInSmelter;

    public override byte[] SaveState()
    {
        return currentlyProcessing ? GMPObject.serializer.Serialize(new SmelterSave { itemInSmelter = itemInSmelter, processTimeRemaining = processTimeRemaining }) : null;
    }

    public override void LoadState(byte[] state)
    {
        SmelterSave s = GMPObject.serializer.Deserialize<SmelterSave>(state);
        currentlyProcessing = true;
        itemInSmelter = s.itemInSmelter;
        processTimeRemaining = s.processTimeRemaining;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic) return;

        List<PhysicalFactoryItem> itemsInInput = ItemsAtInputs().ToList();
        if (!currentlyProcessing && itemsInInput.Count > 0)
        {
            PhysicalFactoryItem selected = itemsInInput[Random.Shared.Next(itemsInInput.Count)];
            if (recipes.ContainsKey(selected.itemID))
            {
                itemInSmelter = selected.itemID;
                currentlyProcessing = true;
                processTimeRemaining = processTime;
                GameWorld.DespawnObject(selected.id, DespawnReason.Consumed);
                Consumed?.Invoke(selected.id);
            }
            else
            {
                Logging.Log($"item {selected.itemID} has no recipe!", "Smelter");
            }
        }
        else if (currentlyProcessing)
        {
            processTimeRemaining -= delta;
            if (processTimeRemaining <= 0)
            {
                if (recipes.TryGetValue(itemInSmelter, out string outputItemID))
                {
                    currentlyProcessing = false;
                    GameWorld.SpawnScene(ItemInfo.Fetch(outputItemID).droppedScene, OutputPosition());
                }
            }
        }
    }
}
