using Godot;
using System;
using System.Linq;

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
        "blueprint_conveyor", "blueprint_conveyor_slope", "blueprint_conveyor_loader", "scrap_ball", "blueprint_assembler", "blueprint_smelter", "blueprint_grinder",
        "blueprint_conveyor_short",
    ];
    public const int StartingBlueprintCount = 99;

    /// <summary>
    /// Every peer stocks the shop. The host spawns the selected level for everyone, unless starting from a save
    /// (loaded once all peers are in).
    /// </summary>
    public static void Preload(GameInfo gameInfo)
    {
        Shop.StockAllItems();
        if (Lobby.isHost)
        {
            if (!string.IsNullOrEmpty(gameInfo.savePath))
            {
                if (GameWorld.ReadSaveFile(gameInfo.savePath) != null)
                {
                    return;
                }
                Logging.Error($"Can't read save {gameInfo.savePath}; starting the level instead", "GameBootstrap");
                gameInfo.savePath = null;
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

    /// <summary>
    /// Spawns this peer's player for everyone. A player coming back to this game (one that left, matched by peer id
    /// or name) picks up where it was with its inventory; anyone else gets the starter items from the game settings.
    /// </summary>
    public static void SpawnLocalPlayer()
    {
        string selfName = Lobby.members.TryGetValue(Lobby.selfPeerID, out PlayerInfo self) ? self.Name : "";
        PlayerSave returning = SaveManager.TakeAbsent(Lobby.selfPeerID, selfName);

        PlayerSync init = new PlayerSync();
        init.controllingPeerID = Lobby.selfPeerID;
        init.isHuman = true;
        Vector3 spawnPos = PickSpawnPosition();
        Vector3 spawnRot = default;
        if (returning != null)
        {
            spawnPos = returning.pos;
            spawnRot = returning.rot;
        }
        ulong pid = GameWorld.SpawnScene(PlayerScene, spawnPos, spawnRot, default, GMPObject.serializer.Serialize(init));
        FactoryPlayer player = GameWorld.syncedObjs[pid] as FactoryPlayer;

        if (returning != null)
        {
            player.inventory.slots = returning.slots ?? new InventorySlot[Inventory.TotalSlots];
            player.inventory.ActiveHotbarSlot = returning.equippedSlot;
            player.inventory.InventoryUpdated();
            player.UpdateEquippedItem();
            return;
        }
        switch (Lobby.gameInfo.starterItems)
        {
            case StarterItems.MagnetRod:
                player.inventory.AddItem("magnet_rod", 1);
                break;
            case StarterItems.All:
                player.inventory.AddItem("magnet_rod", 1);
                foreach (string blueprint in StartingBlueprints)
                {
                    player.inventory.AddItem(blueprint, StartingBlueprintCount);
                }
                break;
        }

        player.UpdateEquippedItem();
    }

    /// <summary>
    /// Every peer, when a peer leaves mid-game: its avatar is removed, and kept as an absent player so the next save
    /// holds it and the player can come back to it (<see cref="SpawnLocalPlayer"/>).
    /// </summary>
    public static void PeerLeft(ulong peer)
    {
        foreach (FactoryPlayer player in GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().ToList())
        {
            if (player.controllingPeerID == peer)
            {
                SaveManager.RememberAbsent(player);
                GameWorld.RemoveLocal(player.id);
            }
        }
    }

    /// <summary>Clears the game's state that outlives a session (it is static): the shop, quests and absent players.</summary>
    public static void ResetSession()
    {
        Shop.ResetSession();
        ProgressManager.ResetSession();
        SaveManager.ResetSession();
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
