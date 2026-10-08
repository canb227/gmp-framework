using Godot;
using System;

public partial class ScrapArm : Structure
{
    [Export]
    public double timePerOperation = 1;

    public double timer;

    [Export]
    public Node3D grabTarget;

    [Export]
    public Node3D grabLocation;

    public ItemSource linkedSource;

    [Export]
    public Node3D dropDestination;

    /// <summary>When off the arm stops taking items from its source (its timer is held).</summary>
    [Export]
    public bool running = true;

    /// <summary>Raised with the new object's id each time the arm spawns an item (only on the peer running its machine logic).</summary>
    public event Action<ulong> ItemSpawned;

    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic || !running || linkedSource == null) return;
        timer += delta;
        if (timer <= timePerOperation) return;
        timer = 0;
        // One GetItem per operation: each call consumes an item from the source.
        string itemID = linkedSource.GetItem();
        if (itemID == null) return;
        Logging.Log($"Spawning item {itemID}", "ScrapArm");
        ulong spawned = ItemInfo.SpawnInWorld(itemID, dropDestination.GlobalPosition);
        ItemSpawned?.Invoke(spawned);
    }

    public override void AfterInit()
    {
        base.AfterInit();
        // The arm faces -Z (its drop point is in the cell ahead); turn that the same way the grid turns footprints.
        Vector3I forward = BuildGrid.RotateOffset(new Vector3I(0, 0, -1), quarterTurns);
        Vector3I behind = occupiedCells[0] - forward;

        Structure backStructure = BuildGrid.GetStructureAt(behind);
        if (backStructure != null)
        {
            Logging.Log($"back cell {behind} contains a structure: {backStructure.Name}!", "ScrapArm");
            if (backStructure is ItemSource source)
            {
                linkedSource = source;
            }
        }
    }
}
