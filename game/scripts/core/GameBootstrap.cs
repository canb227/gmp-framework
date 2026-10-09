using Godot;
using System;

/// <summary>
/// FactoryGame-specific session startup, called by GameWorld during the lobby → game transition.
/// Preload runs on every peer when the host starts the game; Init runs on every peer once all
/// peers have finished preloading.
/// </summary>
public static class GameBootstrap
{
    public const string PlayerScene = "res://game/scenes/player/FactoryPlayer.tscn";
    public static readonly string[] StartingBlueprints =
    [
        "blueprint_conveyor", "blueprint_conveyor_slope", "blueprint_conveyor_loader", "scrap_ball", 
    ];
    public const int StartingBlueprintCount = 99;

    /// <summary>Host spawns the selected level for everyone, unless starting from a save (loaded once all peers are in).</summary>
    public static void Preload(GameInfo gameInfo)
    {
        if (Lobby.isHost)
        {
            if (!string.IsNullOrEmpty(gameInfo.saveName))
            {
                if (GameWorld.ReadSaveFile(gameInfo.saveName) != null)
                {
                    return;
                }
                Logging.Error($"Can't read save {gameInfo.saveName}; starting the level instead", "GameBootstrap");
                gameInfo.saveName = null;
            }
            string levelPath = GameResources.LevelsList[gameInfo.levelIdx].levelPath;
            GameWorld.SpawnScene(levelPath);
        }
    }

    /// <summary>Each peer spawns its own player and seeds its starting inventory.</summary>
    public static void Init()
    {
        SpawnLocalPlayer();
    }

    /// <summary>Spawns this peer's player for everyone, with the starter items from the game settings.</summary>
    public static void SpawnLocalPlayer()
    {
        PlayerSync init = new PlayerSync();
        init.controllingPeerID = Lobby.selfPeerID;
        init.isHuman = true;
        Vector3 spawnPos = PickSpawnPosition();
        ulong pid = GameWorld.SpawnScene(PlayerScene, spawnPos, default, default, GMPObject.serializer.Serialize(init));
        FactoryPlayer player = GameWorld.syncedObjs[pid] as FactoryPlayer;

        FactoryPlayer.emptyHandGrabEnabled = Lobby.gameInfo.emptyHandGrab;
        switch (Lobby.gameInfo.starterItems)
        {
            case StarterItems.SingleMagnetRod:
                player.inventory.AddItem("magnet_rod_single", 1);
                break;
            case StarterItems.All:
                player.inventory.AddItem("magnet_rod", 1);
                player.inventory.AddItem("magnet_rod_single", 1);
                foreach (string blueprint in StartingBlueprints)
                {
                    player.inventory.AddItem(blueprint, StartingBlueprintCount);
                }
                break;
        }

        player.UpdateEquippedItem();
       // BlueprintCommands.Ensure(); // the other structure blueprints: "blueprints <set>" in the console
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
