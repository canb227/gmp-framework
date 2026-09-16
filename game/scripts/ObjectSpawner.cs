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

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        
    }
}
