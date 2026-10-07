using Godot;
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


    public override void _PhysicsProcess(double delta)
    {
        if (currentlyProcessing)
        {
            //play the animation on both host and client
        }
        if (!runsMachineLogic)
        {
            return;
        }
        IEnumerable<PhysicalFactoryItem> itemsInInput = ItemsAtInputs();
        if (!currentlyProcessing && itemsInInput.Count()>0)
        {
            PhysicalFactoryItem selected = itemsInInput.ElementAt(Random.Shared.Next(itemsInInput.Count()));
            if (recipes.TryGetValue(selected.itemID, out string ouputItemID))
            {
                itemInSmelter = selected.itemID;
                currentlyProcessing = true;
                processTimeRemaining = processTime;
                GameWorld.DespawnObject(selected.id);
                Consumed?.Invoke(selected.id);
            }
            else
            {
                Logging.Log($"item {selected.itemID} has no receipe!", "Smelter");
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
    //public override void _PhysicsProcess(double delta)
    //{
    //    if (!runsMachineLogic || hopperTrigger == null) return;

    //    foreach (PhysicalFactoryItem item in ItemsInTrigger(hopperTrigger))
    //    {
    //        string input = item.itemID ?? "";
    //        GameWorld.DespawnObject(item.id);
    //        consumedCount++;
    //        string output = recipes.TryGetValue(input, out string o) ? o : item.itemID;
    //        queue.Enqueue((output, outputCounts.TryGetValue(input, out int n) ? Mathf.Max(1, n) : 1));
    //        if (queue.Count == 1)
    //        {
    //            untilNextOutput = processTime;
    //        }
    //    }

    //    if (queue.Count == 0) return;
    //    untilNextOutput -= delta;
    //    if (untilNextOutput <= 0)
    //    {
    //        (string output, int count) = queue.Dequeue();
    //        for (int i = 0; output != null && i < count; i++)
    //        {
    //            Vector3 side = new((i - (count - 1) / 2f) * outputSpacing, 0, 0);
    //            ItemInfo.SpawnInWorld(output, GlobalTransform * (outputPoint + side), GlobalRotation);
    //            producedCount++;
    //        }
    //        untilNextOutput = processTime;
    //    }
    //}