using Godot;
using System;

/// <summary>
/// Level prop that spawns items, each picked at random from <see cref="itemWeights"/> in proportion to its
/// weight, at its position plus a little random jitter. <see cref="BasicButton"/> presses call
/// <see cref="SpawnOnce"/>; a <see cref="Lever"/> sets <see cref="spawning"/>, which spawns one item every
/// <see cref="spawnInterval"/> seconds while on. Controls change these on every peer (host-arbitrated), but
/// only the host actually spawns, so each item is created once for everyone.
/// </summary>
public partial class ObjectSpawner : Node3D
{
    /// <summary>Item ids to spawn and their relative weights.</summary>
    [Export] public Godot.Collections.Dictionary<string, float> itemWeights = new();
    /// <summary>Seconds between items while <see cref="spawning"/> is on.</summary>
    [Export] public double spawnInterval = 0.5;
    /// <summary>Items appear up to this far (m) from the spawner on each axis, so a stream doesn't stack up.</summary>
    [Export] public float spawnJitter = 0.5f;

    /// <summary>Whether items keep spawning (set on every peer by a lever).</summary>
    public bool spawning;
    /// <summary>Items this peer has spawned (host only; used by the headless multiplayer test).</summary>
    public int spawnedCount;

    double untilNextSpawn;

    public override void _Process(double delta)
    {
        if (!spawning || !Lobby.isHost)
        {
            untilNextSpawn = 0; // the first item comes straight away when switched on
            return;
        }
        untilNextSpawn -= delta;
        if (untilNextSpawn <= 0)
        {
            untilNextSpawn = spawnInterval;
            SpawnOnce();
        }
    }

    /// <summary>Spawns one item. Only the host spawns; elsewhere this does nothing.</summary>
    public void SpawnOnce()
    {
        if (!Lobby.isHost) return;
        string itemID = ItemSpawner.PickWeighted(itemWeights);
        if (itemID == null) return;
        Vector3 jitter = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle()) * 2 - Vector3.One;
        ItemInfo.SpawnInWorld(itemID, GlobalPosition + jitter * spawnJitter, GlobalRotation);
        spawnedCount++;
    }
}
