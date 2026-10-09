using Godot;
using PolyType;
using System.Collections.Generic;
using System.Linq;

/// <summary>A player avatar and its inventory, matched to a peer again on load (by peer id, then by name).</summary>
[GenerateShape]
public partial record PlayerSave
{
    public ulong id;
    public ulong peerID;
    public string name;
    public Vector3 pos;
    public Vector3 rot;
    public InventorySlot[] slots;
    public int equippedSlot;
}

/// <summary>Everything a save file holds, and what a joiner or a resync is sent.</summary>
[GenerateShape]
public partial record SaveGame
{
    public int version;
    public GameInfo gameInfo;
    public WorldSave world;
    public List<string> shopStock = new();
    public Dictionary<string, int> shopResources = new();
    public List<string> completedQuests = new();
    /// <summary>Quests in progress: quest id to items turned in so far.</summary>
    public Dictionary<string, Dictionary<string, int>> questProgress = new();
    /// <summary>Avatars to spawn.</summary>
    public List<PlayerSave> players = new();
    /// <summary>Players from an earlier save who weren't here at the last load, kept so they can come back.</summary>
    public List<PlayerSave> absentPlayers = new();
}

/// <summary>
/// FactoryGame's save record around <see cref="GameWorld"/>'s world snapshot: players (saved here, not as world roots,
/// so they can be matched to whoever loads), the shop and quests. The freeze-and-restore protocol that moves it
/// between peers is in <c>GameWorld.Resync.cs</c>.
/// </summary>
public static class GameSave
{
    public const int Version = 1;

    /// <summary>Players the last restore couldn't match to anyone here; written back into the next save.</summary>
    static List<PlayerSave> absentPlayers = new();

    /// <summary>
    /// Host: the current game. <paramref name="forRestore"/> readies it for every peer to restore at once (a resync):
    /// departed peers' objects go to the host and their avatars are left out.
    /// </summary>
    public static byte[] Capture(bool forRestore)
    {
        SaveGame save = new()
        {
            version = Version,
            gameInfo = Lobby.gameInfo,
            world = GameWorld.SnapshotWorld(),
            shopStock = Shop.Stock.ToList(),
            shopResources = new(Shop.Resources),
            completedQuests = new(ProgressManager.completedQuests),
            questProgress = ProgressManager.currentQuests.ToDictionary(
                q => q.Key, q => q.Value.itemSubmissionProgress.ToDictionary(p => p.Key, p => p.Value)),
        };
        foreach (FactoryPlayer p in GameWorld.syncedObjs.Values.OfType<FactoryPlayer>())
        {
            save.players.Add(new PlayerSave
            {
                id = p.id,
                peerID = p.controllingPeerID,
                name = p.playerName,
                pos = p.Position,
                rot = p.Rotation,
                slots = (InventorySlot[])p.inventory.slots.Clone(),
                equippedSlot = p.inventory.ActiveHotbarSlot,
            });
        }
        save.absentPlayers = absentPlayers.Where(a => !save.players.Any(p => p.peerID == a.peerID)).ToList();
        if (forRestore)
        {
            Prepare(save);
        }
        return GMPObject.serializer.Serialize(save);
    }

    /// <summary>Host: a save file's contents, readied for every peer to restore. Null if it can't be used.</summary>
    public static byte[] PrepareLoaded(byte[] data)
    {
        SaveGame save = Read(data);
        if (save == null)
        {
            return null;
        }
        Prepare(save);
        return GMPObject.serializer.Serialize(save);
    }

    // For a restore on every peer: objects of peers who aren't here go to the host, and each saved player goes to the
    // peer with its id (Steam ids last between sessions) or else its name (LAN ids are new every launch).
    static void Prepare(SaveGame save)
    {
        GameWorld.RehomeAuthority(save.world);
        List<PlayerSave> all = save.players.Concat(save.absentPlayers).ToList();
        save.players = new();
        save.absentPlayers = new();
        HashSet<ulong> taken = new();
        foreach (PlayerSave p in all)
        {
            if (Lobby.members.ContainsKey(p.peerID) && taken.Add(p.peerID))
            {
                save.players.Add(p);
            }
        }
        foreach (PlayerSave p in all.Where(p => !save.players.Contains(p)).ToList())
        {
            PlayerInfo match = Lobby.members.Values.FirstOrDefault(m => !string.IsNullOrEmpty(p.name) && m.Name == p.name && !taken.Contains(m.PeerID));
            if (match != null && taken.Add(match.PeerID))
            {
                save.players.Add(p with { peerID = match.PeerID });
            }
            else
            {
                save.absentPlayers.Add(p);
            }
        }
    }

    /// <summary>Replaces this peer's game with the save in <paramref name="data"/>. A <paramref name="joining"/> peer also takes its game settings.</summary>
    public static void Apply(byte[] data, bool joining)
    {
        SaveGame save = Read(data);
        if (save == null)
        {
            return;
        }
        if (joining)
        {
            Lobby.gameInfo = save.gameInfo;
        }
        GameWorld.RestoreWorld(save.world);
        Shop.LoadSave(save.shopStock, save.shopResources);
        ProgressManager.LoadSave(save.completedQuests, save.questProgress);
        foreach (PlayerSave p in save.players)
        {
            SpawnPlayer(p);
        }
        absentPlayers = save.absentPlayers;
    }

    /// <summary>Spawns this peer's player if the restore didn't bring one back (a new joiner, or a new face at a load).</summary>
    public static void EnsureLocalPlayer()
    {
        if (!GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().Any(p => p.isLocal))
        {
            GameBootstrap.SpawnLocalPlayer();
        }
    }

    static SaveGame Read(byte[] data)
    {
        SaveGame save = GMPObject.serializer.Deserialize<SaveGame>(data);
        if (save.version != Version)
        {
            Logging.Error($"Save is version {save.version}, this build reads version {Version}", "GameSave");
            return null;
        }
        return save;
    }

    // Locally, like the rest of the restore: every restoring peer spawns the same avatars with the same ids.
    static void SpawnPlayer(PlayerSave p)
    {
        PlayerSync sync = new() { controllingPeerID = p.peerID, isHuman = true, pos = p.pos, rot = p.rot, equippedSlot = p.equippedSlot };
        GMPOInitData init = new(p.id, p.peerID, p.peerID, 0, true);
        if (GameWorld.SpawnLocal(GameBootstrap.PlayerScene, p.pos, p.rot, init, GMPObject.serializer.Serialize(sync)) is not FactoryPlayer player)
        {
            return;
        }
        if (string.IsNullOrEmpty(player.playerName))
        {
            player.SetPlayerName(p.name); // a player who has left: the lobby no longer knows the name
        }
        player.inventory.slots = p.slots ?? new InventorySlot[Inventory.TotalSlots];
        player.inventory.ActiveHotbarSlot = p.equippedSlot;
        player.inventory.InventoryUpdated();
        player.UpdateEquippedItem();
    }
}
