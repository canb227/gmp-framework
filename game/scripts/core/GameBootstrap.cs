using Godot;
using System;
using System.Collections.Generic;
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
        "blueprint_conveyor_short", "blueprint_conveyor_splitter",
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
    /// or name) picks up where it was with its inventory and gravity gun upgrades; anyone else gets the starter items
    /// from the game settings. Everyone has a gravity gun in the first slot.
    /// </summary>
    public static void SpawnLocalPlayer()
    {
        string selfName = Lobby.members.TryGetValue(Lobby.selfPeerID, out PlayerInfo self) ? self.Name : "";
        PlayerSave returning = GameWorld.TakeAbsent(Lobby.selfPeerID, selfName);

        PlayerSync init = new PlayerSync();
        init.controllingPeerID = Lobby.selfPeerID;
        if (returning != null)
        {
            init.spawnIndex = returning.spawnIndex;
        }
        else
        {
            init.spawnIndex = PickSpawnIndex();
        }
        Transform3D spawn = SpawnTransform(init.spawnIndex);
        Vector3 spawnPos = spawn.Origin;
        Vector3 spawnRot = spawn.Basis.GetEuler();
        if (returning != null)
        {
            spawnPos = returning.pos;
            spawnRot = returning.rot;
        }
        init.pos = spawnPos;
        init.rot = spawnRot;
        ulong pid = GameWorld.SpawnScene(PlayerScene, spawnPos, spawnRot, default, GMPObject.serializer.Serialize(init));
        FactoryPlayer player = GameWorld.syncedObjs[pid] as FactoryPlayer;

        if (returning != null)
        {
            player.inventory.slots = returning.slots ?? new InventorySlot[Inventory.TotalSlots];
            player.inventory.EnsureGun();
            player.inventory.ActiveHotbarSlot = returning.equippedSlot;
            player.inventory.InventoryUpdated();
            player.UpdateEquippedItem();
            foreach (string upgradeID in returning.gunUpgrades ?? new())
            {
                player.AddGunUpgrade(upgradeID);
            }
            return;
        }
        player.inventory.EnsureGun();
        switch (Lobby.gameInfo.starterItems)
        {
            case StarterItems.Tools:
                player.inventory.AddItem("wrench", 1);
                break;
            case StarterItems.All:
                player.inventory.AddItem("wrench", 1);
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
                GameWorld.RememberAbsent(player);
                GameWorld.RemoveLocal(player.id);
            }
        }
    }

    /// <summary>Clears the game's state that outlives a session (it is static): the shop and quests.</summary>
    public static void ResetSession()
    {
        Shop.ResetSession();
        ProgressManager.ResetSession();
    }

    /// <summary>Group for spawn markers placed in level scenes (e.g. a Marker3D named PlayerStart).</summary>
    public const string PlayerSpawnGroup = "player_spawn";

    /// <summary>The level's spawn markers, in tree order: a player's <see cref="FactoryPlayer.spawnIndex"/> is an index into these.</summary>
    public static List<Node3D> SpawnPoints()
    {
        List<Node3D> points = new();
        if (GameWorld.instance == null)
        {
            return points;
        }
        foreach (Node node in GameWorld.instance.GetTree().GetNodesInGroup(PlayerSpawnGroup))
        {
            if (node is Node3D marker)
            {
                points.Add(marker);
            }
        }
        return points;
    }

    /// <summary>
    /// Where a player with this spawn index appears: beside its marker (jittered, as several players may share one),
    /// facing the way the marker faces. An index past the level's markers wraps around (a save from a level that had
    /// more); a level with no markers falls back to a random spot near the origin.
    /// </summary>
    public static Transform3D SpawnTransform(int spawnIndex)
    {
        List<Node3D> points = SpawnPoints();
        if (points.Count == 0)
        {
            return new Transform3D(Basis.Identity, new Vector3(Random.Shared.Next(5), Random.Shared.Next(2, 5), Random.Shared.Next(5)));
        }
        Node3D marker = points[spawnIndex % points.Count];
        Vector3 jitter = new Vector3((float)(Random.Shared.NextDouble() * 3.0 - 1.5), 0f, (float)(Random.Shared.NextDouble() * 3.0 - 1.5));
        Basis yaw = new Basis(Vector3.Up, marker.GlobalRotation.Y);
        return new Transform3D(yaw, marker.GlobalPosition + jitter + Vector3.Up);
    }

    /// <summary>
    /// A first-time player's spawn point: the one fewest players hold (lowest index on a tie), so points fill up in
    /// order and then double up. Players who left still hold theirs, so they get it back.
    /// <para>
    /// At the start of a session every peer picks at once, before it has seen anyone else's player, so each one
    /// also counts the peers ahead of it (by peer id) who haven't spawned yet: every peer works the same answer out
    /// for each of them, so nobody needs to ask the host.
    /// </para>
    /// </summary>
    static int PickSpawnIndex()
    {
        int pointCount = SpawnPoints().Count;
        if (pointCount == 0)
        {
            return 0;
        }
        // How many players hold each point already: those here and those who left.
        int[] uses = new int[pointCount];
        foreach (FactoryPlayer player in FactoryPlayer.all)
        {
            uses[player.spawnIndex % pointCount]++;
        }
        foreach (PlayerSave absent in GameWorld.absentPlayers)
        {
            uses[absent.spawnIndex % pointCount]++;
        }
        // The peers still to spawn, in an order every peer agrees on; this peer's place among them.
        List<ulong> waiting = new();
        foreach (ulong peer in Lobby.members.Keys)
        {
            bool hasPlayer = false;
            foreach (FactoryPlayer player in FactoryPlayer.all)
            {
                if (player.controllingPeerID == peer)
                {
                    hasPlayer = true;
                }
            }
            if (!hasPlayer)
            {
                waiting.Add(peer);
            }
        }
        waiting.Sort();
        int place = Mathf.Max(0, waiting.IndexOf(Lobby.selfPeerID));

        // The waiting peers take a point each in turn, the least held one; this peer takes the one it gets at its turn.
        int picked = 0;
        for (int turn = 0; turn <= place; turn++)
        {
            picked = 0;
            for (int i = 1; i < pointCount; i++)
            {
                if (uses[i] < uses[picked])
                {
                    picked = i;
                }
            }
            uses[picked]++;
        }
        return picked;
    }
}
