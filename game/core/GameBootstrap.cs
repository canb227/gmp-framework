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
        Vector3 spawnPos = new Vector3(Random.Shared.Next(5), Random.Shared.Next(2, 5), Random.Shared.Next(5));
        ulong pid = GameWorld.SpawnScene(PlayerScene, spawnPos, default, default, GMPObject.serializer.Serialize(init));
        FactoryPlayer player = GameWorld.syncedObjs[pid] as FactoryPlayer;
        player.inventory.AddItem("test_1x1x1cubePLACEABLE", 1);
        player.UpdateEquippedItem();
    }
}
