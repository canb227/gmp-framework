using Godot;
using System;

/// <summary>
/// FactoryGame-specific session startup, called by GameWorld during the lobby → game transition.
/// Preload runs on every peer when the host starts the game; Init runs on every peer once all
/// peers have finished preloading.
/// </summary>
public static class GameBootstrap
{
    public const string PlayerScene = "res://game/player/FactoryPlayer.tscn";
    public static readonly string[] StartingBlueprints =
    [
        "blueprint_test_block", "blueprint_conveyor", "blueprint_conveyor_slope", "blueprint_conveyor_turn",
        "blueprint_item_void", "blueprint_grinder", "blueprint_item_spawner",
        "blueprint_test_block_2x1x1",
    ];
    public const int StartingBlueprintCount = 3;

    /// <summary>Host spawns the selected level for everyone.</summary>
    public static void Preload(GameInfo gameInfo)
    {
        if (Lobby.isHost)
        {
            string levelPath = GameResources.LevelsList[gameInfo.levelIdx].levelPath;
            GameWorld.SpawnScene(levelPath);
        }
    }

    /// <summary>Each peer spawns its own player and seeds its starting inventory.</summary>
    public static void Init()
    {
        PlayerSync init = new PlayerSync();
        init.controllingPeerID = Lobby.selfPeerID;
        init.isHuman = true;
        Vector3 spawnPos = PickSpawnPosition();
        ulong pid = GameWorld.SpawnScene(PlayerScene, spawnPos, default, default, GMPObject.serializer.Serialize(init));
        FactoryPlayer player = GameWorld.syncedObjs[pid] as FactoryPlayer;
        player.inventory.AddItem("magnet_rod", 1);
        foreach (string blueprint in StartingBlueprints)
        {
            player.inventory.AddItem(blueprint, StartingBlueprintCount);
        }
        player.inventory.AddItem("magnet_rod", 1);
        player.UpdateEquippedItem();
    }

    /// <summary>Group for spawn markers placed in level scenes (e.g. a Marker3D named PlayerStart).</summary>
    public const string PlayerSpawnGroup = "player_spawn";

    /// <summary>
    /// A spot near the level's <see cref="PlayerSpawnGroup"/> marker, jittered so peers don't spawn inside each
    /// other. Falls back to the old random spot near the origin for levels without a marker.
    /// </summary>
    static Vector3 PickSpawnPosition()
    {
        Vector3 jitter = new Vector3((float)(Random.Shared.NextDouble() * 3.0 - 1.5), 0f, (float)(Random.Shared.NextDouble() * 3.0 - 1.5));
        if (GameWorld.instance?.GetTree().GetFirstNodeInGroup(PlayerSpawnGroup) is Node3D marker)
        {
            return marker.GlobalPosition + jitter + Vector3.Up;
        }
        return new Vector3(Random.Shared.Next(5), Random.Shared.Next(2, 5), Random.Shared.Next(5));
    }
}
