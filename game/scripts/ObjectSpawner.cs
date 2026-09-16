using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class ObjectSpawner : Node3D
{
    [Export] public Godot.Collections.Array<PackedScene> spawnObjects;
    private List<PackedScene> spawnObjectsList;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        spawnObjectsList = spawnObjects.ToList();
    }

    private double deltaTotal = 0.0;
    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        deltaTotal += delta;
        if(deltaTotal > 0.1)
        {
            Vector3 spawnPosition = GlobalPosition;
            spawnPosition.X += Random.Shared.NextSingle()-0.5f;
            spawnPosition.Y += Random.Shared.NextSingle()-0.5f;
            spawnPosition.Z += Random.Shared.NextSingle()-0.5f;
            GameWorld.SpawnScene(spawnObjects[0].ResourcePath, spawnPosition, GlobalRotation);
            deltaTotal = 0.0;
        }
    }
}
