using Godot;
using System;
using System.Collections.Generic;

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

    Dictionary<string, Node> multiMesh = new();

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }

    public override void _PhysicsProcess(double delta)
    {
        if (runsMachineLogic && running && linkedSource!=null)
        {
            timer += delta;
            if (timer>timePerOperation)
            {
                timer = 0;
                // One GetItem per operation: each call consumes an item from the source.
                string itemID = linkedSource.GetItem();

                if (itemID != null)
                {
                    if (multiMesh.ContainsKey(itemID))
                    {

                    }
                    else
                    {
                        multiMesh[itemID] = ClassDB.Instantiate("Box3DMultiMeshRenderer").As<Node3D>();
                        multiMesh[itemID].Name = $"MultiMesh_{itemID}";
                        GameWorld.b3droot.AddChild(multiMesh[itemID]);

                    }
                    //Logging.Log($"guh-1 {multiMesh[itemID].GetPath()}", "GameWorld");
                    ItemSpawned?.Invoke(ItemInfo.SpawnInWorld(itemID, dropDestination.GlobalPosition, parentPath: multiMesh[itemID].GetPath()));
                }
            }
        }
    }

    public override void AfterInit()
    {
        base.AfterInit();
        //Logging.Log($"scrap arm occupies cells: {occupiedCells}", "ScrapArm");
        Vector3I loc = occupiedCells[0];

        // The arm faces -Z (its drop point is in the cell ahead); turn that the same way the grid turns footprints.
        Vector3I forward = BuildGrid.RotateOffset(new Vector3I(0, 0, -1), quarterTurns);
        Vector3I front = loc + forward;
        Vector3I behind = loc - forward;


        Structure frontStructure = BuildGrid.GetStructureAt(front);
        Structure backStructure = BuildGrid.GetStructureAt(behind);

        if (frontStructure != null)
        {
         //   Logging.Log($"front cell {front} contains a structure: {frontStructure.Name}!", "ScrapArm");
            

        }
        if (backStructure != null)
        {
          //  Logging.Log($"back cell {behind} contains a structure: {backStructure.Name}!", "ScrapArm");
            if (backStructure is ItemSource source)
            {
                linkedSource = source;
            }
        }
    }
}
