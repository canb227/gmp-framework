using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Spawns random objects from <see cref="spawnObjects"/> while <see cref="spawning"/> is on. The flag is
/// kept in sync on every peer (toggled by <see cref="BasicButton"/>), but only the host actually spawns,
/// so each object is created once for everyone.
/// </summary>
public partial class ObjectSpawner : Node3D
{
    [Export] public float spawnRate = 0.1f;
    [Export] public bool oneShot = false;
    [Export] public Godot.Collections.Array<PackedScene> spawnObjects;
    private List<PackedScene> spawnObjectsList;
    public bool spawning = false;

    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
        spawnObjectsList = spawnObjects.ToList();
    }

    private double deltaTotal = 0.0;
    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
        if (!spawning || !Lobby.isHost)
        {
            return;
        }
        deltaTotal += delta;
        if (deltaTotal > spawnRate)
        {
            SpawnRandomObject();
            if (oneShot)
            {
                RPCManager.RPC(this, nameof(_ApplySpawning), [false]);
            }
            deltaTotal = 0.0;
        }
    }

    [RPC(requireAuthority = true)]
    private void _ApplySpawning(bool on)
    {
        spawning = on;
    }


    private void SpawnRandomObject()
    {
        Vector3 spawnPosition = GlobalPosition;
        spawnPosition.X += Random.Shared.NextSingle()-0.5f;
        spawnPosition.Y += Random.Shared.NextSingle()-0.5f;
        spawnPosition.Z += Random.Shared.NextSingle()-0.5f;
        GameWorld.SpawnScene(spawnObjects.PickRandom().ResourcePath, spawnPosition, GlobalRotation);
    }
}
