using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class BasicButton : Node3D
{
    [Export] public string displayName;
    [Export] public AnimationPlayer animator;
    [Export] public Godot.Collections.Array<ObjectSpawner> targetObjectSpawner;
    private List<ObjectSpawner> targetSpawnerList;
    
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        targetSpawnerList = targetObjectSpawner.ToList();
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }

    public void OnPressed()
    {
        foreach(ObjectSpawner spawner in targetSpawnerList)
        {
            animator.Play("button_press");
            spawner.spawning = !spawner.spawning;
        }
    }
}
