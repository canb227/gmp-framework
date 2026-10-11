using Godot;
using PolyType;
using System;

/// <summary>What a save keeps of an <see cref="ObjectSpawner"/>: that it was left spawning (saved only then).</summary>
[GenerateShape]
public partial record struct ObjectSpawnerSave
{
    public bool spawning;
}

/// <summary>
/// Level prop that spawns items, each picked at random from <see cref="itemWeights"/> in proportion to its
/// weight, at its position plus a little random jitter. As a <see cref="Triggerable"/> it spawns one item when
/// switched on, then one every <see cref="spawnInterval"/> seconds while it stays on: a push button's pulse gives
/// one item, a lever a stream. Activators switch it on every peer, but only its authority (the host, for a level
/// prop) actually spawns, so each item is created once for everyone.
/// </summary>
public partial class ObjectSpawner : GMPONode3D, Triggerable
{
    /// <summary>Item ids to spawn and their relative weights.</summary>
    [Export] public Godot.Collections.Dictionary<string, float> itemWeights = new();
    /// <summary>Seconds between items while <see cref="spawning"/> is on.</summary>
    [Export] public double spawnInterval = 0.5;
    /// <summary>Items appear up to this far (m) from the spawner on each axis, so a stream doesn't stack up.</summary>
    [Export] public float spawnJitter = 0.5f;

    /// <summary>Whether items keep spawning (set on every peer by its activators).</summary>
    public bool spawning;
    /// <summary>Items this peer has spawned (authority only; used by the headless multiplayer test).</summary>
    public int spawnedCount;

    double untilNextSpawn;

    public ObjectSpawner()
    {
        priority = -1; // never sends state updates
    }

    public override byte[] GenerateStateUpdate() => null;

    public override byte[] SaveState()
    {
        return spawning ? GMPObject.serializer.Serialize(new ObjectSpawnerSave { spawning = true }) : null;
    }

    public override void LoadState(byte[] state)
    {
        spawning = GMPObject.serializer.Deserialize<ObjectSpawnerSave>(state).spawning;
        untilNextSpawn = spawnInterval;
    }

    public void OnTrigger(bool active)
    {
        if (active && !spawning)
        {
            SpawnOnce();
            untilNextSpawn = spawnInterval;
        }
        spawning = active;
    }

    public override void _Process(double delta)
    {
        if (!spawning || authority != Lobby.selfPeerID) return;
        untilNextSpawn -= delta;
        if (untilNextSpawn <= 0)
        {
            untilNextSpawn = spawnInterval;
            SpawnOnce();
        }
    }

    /// <summary>Spawns one item. Only the authority spawns; elsewhere this does nothing.</summary>
    public void SpawnOnce()
    {
        if (authority != Lobby.selfPeerID) return;
        string itemID = ItemSpawner.PickWeighted(itemWeights);
        if (itemID == null) return;
        Vector3 jitter = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle()) * 2 - Vector3.One;
        ItemInfo.SpawnInWorld(itemID, GlobalPosition + jitter * spawnJitter, GlobalRotation);
        spawnedCount++;
    }
}
