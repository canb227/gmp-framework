using Godot;
using PolyType;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using FileAccess = Godot.FileAccess;

/// <summary>One spawned root: the arguments that rebuild it with <see cref="GameWorld.SpawnLocal"/>.</summary>
[GenerateShape]
public partial record RootSave
{
    public string scenePath;
    /// <summary>The node path it was spawned under; null for the physics root.</summary>
    public string parent;
    /// <summary>The root's node name (for a GMPObject root, its type and id), kept so saved parent paths still resolve.</summary>
    public string name;
    public Vector3 pos;
    public Vector3 rot;
    /// <summary>The root's own ids; id 0 for a root that isn't a GMPObject (e.g. the level).</summary>
    public GMPOInitData init;
    /// <summary>Its live GMPObject children by path relative to the root. A scene child missing here was despawned.</summary>
    public Dictionary<string, GMPOInitData> childInit = new();
}

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
    public int spawnIndex;
}

/// <summary>Everything a save file holds, and what a joiner or a resync is sent.</summary>
[GenerateShape]
public partial record SaveGame
{
    public int version;
    public GameInfo gameInfo;
    /// <summary>Every saved spawned root, in spawn order (players aren't: they're in <see cref="players"/>).</summary>
    public List<RootSave> roots = new();
    /// <summary><see cref="GMPObject.GenerateStateUpdate"/> by id, handed back as the spawn initState (e.g. a structure's cell).</summary>
    public Dictionary<ulong, byte[]> states = new();
    /// <summary><see cref="GMPObject.SaveState"/> by id, for the objects that returned any; read back by <see cref="GMPObject.LoadState"/>.</summary>
    public Dictionary<ulong, byte[]> custom = new();
    /// <summary>The loose items (they aren't nodes, see ItemWorld).</summary>
    public List<ItemSave> items = new();
    public List<string> shopStock = new();
    /// <summary>Currency banks by currency name.</summary>
    public Dictionary<string, int> shopBalances = new();
    /// <summary>Items turned in to the void, by item id.</summary>
    public Dictionary<string, int> shopDeposits = new();
    public List<string> completedQuests = new();
    /// <summary>Quests in progress: quest id to items turned in so far.</summary>
    public Dictionary<string, Dictionary<string, int>> questProgress = new();
    /// <summary>Quests in progress: quest id to currency earned so far, by currency name.</summary>
    public Dictionary<string, Dictionary<string, int>> questCurrencyProgress = new();
    /// <summary>Avatars to spawn.</summary>
    public List<PlayerSave> players = new();
    /// <summary>Players from an earlier save who weren't here at the last load, kept so they can come back.</summary>
    public List<PlayerSave> absentPlayers = new();
}

/// <summary>
/// GameWorld: the save record (<see cref="SaveGame"/>), capturing the game into one and rebuilding it from one, and the
/// save files on disk. The freeze-and-restore protocol that moves it between peers is in <c>GameWorld.Resync.cs</c>.
/// <para>
/// The world part walks <see cref="spawnedRoots"/>, so despawned objects are simply gone, and records each root as the
/// spawn call that rebuilds it with the same ids. This relies on GMPObjects never being reparented: a child is found
/// again by its path inside its root's scene. The capture warns about any synced object it can't place. Players are
/// saved on their own, not as roots, so they can be matched to whoever loads.
/// </para>
/// </summary>
public partial class GameWorld
{
    /// <summary>4: one flat record (the world's roots and states no longer nested in their own).</summary>
    public const int SaveVersion = 4;

    /// <summary>Roots in this group are left out of the world's roots (players, which are saved on their own).</summary>
    public const string UnsavedGroup = "unsaved";

    /// <summary>This user's save folder (created at first run by <see cref="Global"/>); steam id 0 without Steam.</summary>
    public static string SaveDir => "user://saves/" + Global.steamid + "/";
    public const string SaveExtension = ".sav";

    /// <summary>Players the last restore couldn't match to anyone here; written back into the next save.</summary>
    public static List<PlayerSave> absentPlayers = new();

    /// <summary>A new save's suggested file name: the time, so saves sort by age.</summary>
    public static string DefaultSaveName()
    {
        return "save_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + SaveExtension;
    }

    // ---- capturing ---------------------------------------------------------------

    /// <summary>
    /// Host: the current game, serialized. <paramref name="forRestore"/> readies it for every peer to restore at once
    /// (a resync): departed peers' objects go to the host and their avatars are left out.
    /// </summary>
    public static byte[] CaptureSave(bool forRestore)
    {
        SaveGame save = new()
        {
            version = SaveVersion,
            gameInfo = Lobby.gameInfo,
            items = ItemWorld.Capture(),
            shopStock = Shop.Stock.ToList(),
            shopBalances = Shop.SaveBalances(),
            shopDeposits = new(Shop.Deposited),
            completedQuests = new(ProgressManager.completedQuests),
            questProgress = ProgressManager.currentQuests.ToDictionary(
                q => q.Key, q => q.Value.itemSubmissionProgress.ToDictionary(p => p.Key, p => p.Value)),
            questCurrencyProgress = ProgressManager.currentQuests.ToDictionary(
                q => q.Key, q => q.Value.currencyProgress.ToDictionary(p => p.Key.ToString(), p => p.Value)),
        };
        CaptureRoots(save);
        foreach (FactoryPlayer p in syncedObjs.Values.OfType<FactoryPlayer>())
        {
            save.players.Add(SaveOf(p));
        }
        // Players who came back are in the game again (matched by name too: LAN ids change between launches).
        save.absentPlayers = absentPlayers.Where(a => !save.players.Any(p => p.peerID == a.peerID || (!string.IsNullOrEmpty(a.name) && p.name == a.name))).ToList();
        if (forRestore)
        {
            PrepareForRestore(save);
        }
        return GMPObject.serializer.Serialize(save);
    }

    /// <summary>Records every live spawned root and the state of each GMPObject in them.</summary>
    static void CaptureRoots(SaveGame save)
    {
        HashSet<ulong> covered = new();
        foreach (Node root in spawnedRoots)
        {
            if (!IsInstanceValid(root) || root.IsQueuedForDeletion() || !root.IsInsideTree())
            {
                continue;
            }
            bool saved = !root.IsInGroup(UnsavedGroup);
            Node parent = root.GetParent();
            RootSave r = new()
            {
                scenePath = root.SceneFilePath,
                parent = parent == physics ? null : parent.GetPath().ToString(),
                name = root.Name,
            };
            if (root is Node3D n3d)
            {
                r.pos = n3d.Position;
                r.rot = n3d.Rotation;
            }
            if (root is GMPObject g)
            {
                r.init = new GMPOInitData(g.id, g.authority, g.owner, g.priority, g.pauseable);
                covered.Add(g.id);
                if (saved) CaptureObject(save, g);
            }
            foreach (Node c in root.FindChildren("*"))
            {
                if (c is GMPObject gc && !c.IsQueuedForDeletion())
                {
                    r.childInit[root.GetPathTo(c)] = new GMPOInitData(gc.id, gc.authority, gc.owner, gc.priority, gc.pauseable);
                    covered.Add(gc.id);
                    if (saved) CaptureObject(save, gc);
                }
            }
            if (saved)
            {
                save.roots.Add(r);
            }
        }
        foreach ((ulong id, GMPObject gmpo) in syncedObjs)
        {
            if (!covered.Contains(id) && gmpo is Node n && !n.IsQueuedForDeletion())
            {
                Logging.Warn($"{n.GetPath()} ({id}) isn't where its spawned scene put it, so saves leave it out. GMPObjects must not be reparented.", "GameWorld");
            }
        }
    }

    static void CaptureObject(SaveGame save, GMPObject gmpo)
    {
        save.states[gmpo.id] = gmpo.GenerateStateUpdate();
        byte[] custom = gmpo.SaveState();
        if (custom != null)
        {
            save.custom[gmpo.id] = custom;
        }
    }

    static PlayerSave SaveOf(FactoryPlayer p)
    {
        return new PlayerSave
        {
            id = p.id,
            peerID = p.controllingPeerID,
            name = p.playerName,
            pos = p.Position,
            rot = p.Rotation,
            slots = (InventorySlot[])p.inventory.slots.Clone(),
            equippedSlot = p.inventory.ActiveHotbarSlot,
            spawnIndex = p.spawnIndex,
        };
    }

    // ---- players who left ----------------------------------------------------------

    /// <summary>Keeps a player who left as absent, so saves still hold it and it can come back (<see cref="TakeAbsent"/>).</summary>
    public static void RememberAbsent(FactoryPlayer player)
    {
        // Replaces an older entry of the same player (by name too: LAN ids change between launches).
        absentPlayers.RemoveAll(a => a.peerID == player.controllingPeerID || (!string.IsNullOrEmpty(a.name) && a.name == player.playerName));
        absentPlayers.Add(SaveOf(player));
    }

    /// <summary>The absent player matching this peer (by id, else by name), taken off the absent list; null if none.</summary>
    public static PlayerSave TakeAbsent(ulong peerID, string name)
    {
        PlayerSave match = absentPlayers.FirstOrDefault(a => a.peerID == peerID);
        if (match == null && !string.IsNullOrEmpty(name))
        {
            match = absentPlayers.FirstOrDefault(a => a.name == name);
        }
        if (match != null)
        {
            absentPlayers.Remove(match);
        }
        return match;
    }

    // ---- restoring -------------------------------------------------------------------

    /// <summary>Host: a save file's contents, readied for every peer to restore. Null if it can't be used.</summary>
    public static byte[] PrepareLoadedSave(byte[] data)
    {
        SaveGame save = ReadSave(data);
        if (save == null)
        {
            return null;
        }
        PrepareForRestore(save);
        return GMPObject.serializer.Serialize(save);
    }

    // For a restore on every peer: objects of peers who aren't here go to the host, and each saved player goes to the
    // peer with its id (Steam ids last between sessions) or else its name (LAN ids are new every launch).
    static void PrepareForRestore(SaveGame save)
    {
        foreach (RootSave r in save.roots)
        {
            if (r.init.id != 0 && !Lobby.members.ContainsKey(r.init.authority))
            {
                r.init.authority = Lobby.hostID;
            }
            foreach ((string path, GMPOInitData ci) in r.childInit)
            {
                if (!Lobby.members.ContainsKey(ci.authority))
                {
                    r.childInit[path] = ci with { authority = Lobby.hostID };
                }
            }
        }

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
    public static void ApplySave(byte[] data, bool joining)
    {
        SaveGame save = ReadSave(data);
        if (save == null)
        {
            return;
        }
        if (joining)
        {
            Lobby.gameInfo = save.gameInfo;
        }
        ClearWorld();
        foreach (RootSave r in save.roots)
        {
            if (!ResourceLoader.Exists(r.scenePath))
            {
                Logging.Warn($"Save names a scene that no longer exists, skipped: {r.scenePath}", "GameWorld");
                continue;
            }
            Node parent = r.parent == null ? null : instance.GetNodeOrNull(r.parent);
            if (r.parent != null && parent == null)
            {
                Logging.Warn($"Parent {r.parent} of saved {r.scenePath} wasn't restored, skipped", "GameWorld");
                continue;
            }
            Node node = SpawnLocal(r.scenePath, r.pos, r.rot, r.init, save.states.GetValueOrDefault(r.init.id), r.childInit, parent, save.states);
            if (node != null)
            {
                node.Name = r.name;
            }
        }
        // Once everything exists, so custom state can refer to other saved objects.
        foreach ((ulong id, byte[] state) in save.custom)
        {
            if (syncedObjs.TryGetValue(id, out GMPObject gmpo))
            {
                gmpo.LoadState(state);
            }
        }
        ItemWorld.Restore(save.items);
        Shop.LoadSave(save.shopStock, save.shopBalances, save.shopDeposits);
        ProgressManager.LoadSave(save.completedQuests, save.questProgress, save.questCurrencyProgress);
        foreach (PlayerSave p in save.players)
        {
            SpawnSavedPlayer(p);
        }
        absentPlayers = save.absentPlayers;
    }

    /// <summary>Spawns this peer's player if the restore didn't bring one back (a new joiner, or a new face at a load).</summary>
    public static void EnsureLocalPlayer()
    {
        if (!syncedObjs.Values.OfType<FactoryPlayer>().Any(p => p.isLocal))
        {
            GameBootstrap.SpawnLocalPlayer();
        }
    }

    static SaveGame ReadSave(byte[] data)
    {
        SaveGame save = GMPObject.serializer.Deserialize<SaveGame>(data);
        if (save.version != SaveVersion)
        {
            Logging.Error($"Save is version {save.version}, this build reads version {SaveVersion}", "GameWorld");
            return null;
        }
        return save;
    }

    // Locally, like the rest of the restore: every restoring peer spawns the same avatars with the same ids.
    static void SpawnSavedPlayer(PlayerSave p)
    {
        PlayerSync sync = new() { controllingPeerID = p.peerID, pos = p.pos, rot = p.rot, equippedSlot = p.equippedSlot, spawnIndex = p.spawnIndex };
        GMPOInitData init = new(p.id, p.peerID, p.peerID, 0, true);
        if (SpawnLocal(GameBootstrap.PlayerScene, p.pos, p.rot, init, GMPObject.serializer.Serialize(sync)) is not FactoryPlayer player)
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

    // ---- files ------------------------------------------------------------------

    /// <summary>Writes <paramref name="data"/> compressed to the file at <paramref name="path"/>; false (logged) if it couldn't.</summary>
    public static bool WriteSaveFile(string path, byte[] data)
    {
        DirAccess.MakeDirRecursiveAbsolute(path.GetBaseDir());
        // Write beside it and swap in, so a crash mid-write never leaves a half-written save.
        string temp = path + ".tmp";
        using (FileAccess file = FileAccess.Open(temp, FileAccess.ModeFlags.Write))
        {
            if (file == null)
            {
                Logging.Error($"Can't write {temp}: {FileAccess.GetOpenError()}", "GameWorld");
                return false;
            }
            file.StoreBuffer(Compress(data));
        }
        Error err = DirAccess.RenameAbsolute(temp, path);
        if (err != Error.Ok)
        {
            Logging.Error($"Can't move {temp} to {path}: {err}", "GameWorld");
            return false;
        }
        Logging.Log($"Saved {path}", "GameWorld");
        return true;
    }

    /// <summary>The uncompressed contents of the save at <paramref name="path"/>, or null (logged) if it can't be read.</summary>
    public static byte[] ReadSaveFile(string path)
    {
        if (!FileAccess.FileExists(path))
        {
            Logging.Error($"No save at {path}", "GameWorld");
            return null;
        }
        try
        {
            return Decompress(FileAccess.GetFileAsBytes(path));
        }
        catch (InvalidDataException e)
        {
            Logging.Error($"Save {path} is corrupt: {e.Message}", "GameWorld");
            return null;
        }
    }

    public static byte[] Compress(byte[] data)
    {
        using MemoryStream output = new();
        using (ZLibStream z = new(output, CompressionLevel.Fastest))
        {
            z.Write(data);
        }
        return output.ToArray();
    }

    public static byte[] Decompress(byte[] data)
    {
        using ZLibStream z = new(new MemoryStream(data), CompressionMode.Decompress);
        using MemoryStream output = new();
        z.CopyTo(output);
        return output.ToArray();
    }
}
