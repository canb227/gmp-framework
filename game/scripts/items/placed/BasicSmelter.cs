using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// A two-cell-tall machine: items dropped into the hopper on top are consumed and ground one at a time; after
/// <see cref="processTime"/> each comes out transformed (<see cref="recipes"/>, <see cref="outputCounts"/> of
/// them side by side) just in front of the bottom cell, at belt height, so they land on a conveyor placed there.
/// Items without a recipe come out unchanged. Runs on the grinder's authority, which despawns inputs and
/// spawns outputs for everyone.
/// <para>
/// Known gap: an item grabbed or picked up at the moment it enters the hopper is decided separately by
/// its own authority, so in that race it could be both kept and ground.
/// </para>
/// </summary>
public partial class BasicSmelter : Structure
{
    /// <summary>Sensor child body filling the hopper.</summary>
    [Export] public Node3D hopperTrigger;
    /// <summary>Where outputs appear, relative to the grinder (in front of the bottom cell, at belt height).</summary>
    [Export] public Node3D outputSpawnPoint;
    /// <summary>Seconds to grind one input item; queued items are ground one after another.</summary>
    [Export] public double processTime = 1.0;
    public double processTimeRemaining = 0;
    /// <summary>Input itemID → output itemID.</summary>
    [Export] public Godot.Collections.Dictionary<string, string> recipes = new();

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
        IEnumerable<PhysicalFactoryItem> itemsInInput = ItemsInTrigger(hopperTrigger);
        if (!currentlyProcessing && itemsInInput.Count()>0)
        {
            PhysicalFactoryItem selected = itemsInInput.ElementAt(Random.Shared.Next(itemsInInput.Count()));
            if (recipes.TryGetValue(selected.itemID, out string ouputItemID))
            {
                itemInSmelter = selected.itemID;
                currentlyProcessing = true;
                processTimeRemaining = processTime;
                GameWorld.DespawnObject(selected.id);
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
                    GameWorld.SpawnScene(ItemInfo.Fetch(outputItemID).droppedScene,outputSpawnPoint.GlobalPosition);
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