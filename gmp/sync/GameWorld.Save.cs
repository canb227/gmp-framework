using Godot;
using PolyType;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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

/// <summary>Every saved root, in spawn order, and the state of each of their GMPObjects.</summary>
[GenerateShape]
public partial record WorldSave
{
    public List<RootSave> roots = new();
    /// <summary><see cref="GMPObject.GenerateStateUpdate"/> by id, handed back as the spawn initState (e.g. the transform).</summary>
    public Dictionary<ulong, byte[]> states = new();
    /// <summary><see cref="GMPObject.SaveState"/> by id, for the objects that returned any; read back by <see cref="GMPObject.LoadState"/>.</summary>
    public Dictionary<ulong, byte[]> custom = new();
}

/// <summary>
/// GameWorld: snapshotting the synced world into a <see cref="WorldSave"/> and rebuilding it from one, plus the save
/// files on disk. The game wraps the world in its own save record (players, shop, quests); see <see cref="GameSave"/>.
/// <para>
/// A snapshot walks <see cref="spawnedRoots"/>, so despawned objects are simply gone, and records each root as the
/// spawn call that rebuilds it with the same ids. This relies on GMPObjects never being reparented: a child is found
/// again by its path inside its root's scene. The snapshot warns about any synced object it can't place.
/// </para>
/// </summary>
public partial class GameWorld
{
    /// <summary>Roots in this group are left out of snapshots (the game saves its players itself).</summary>
    public const string UnsavedGroup = "unsaved";

    /// <summary>This user's save folder (created at first run by <see cref="Global"/>); steam id 0 without Steam.</summary>
    public static string SaveDir => "user://saves/" + Global.steamid + "/";
    public const string SaveExtension = ".sav";

    /// <summary>A new save's suggested file name: the time, so saves sort by age.</summary>
    public static string DefaultSaveName()
    {
        return "save_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + SaveExtension;
    }

    /// <summary>Records every live spawned root and the state of each GMPObject in them.</summary>
    public static WorldSave SnapshotWorld()
    {
        WorldSave save = new();
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
                parent = parent == b3droot ? null : parent.GetPath().ToString(),
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
                if (saved) SaveObject(save, g);
            }
            foreach (Node c in root.FindChildren("*"))
            {
                if (c is GMPObject gc && !c.IsQueuedForDeletion())
                {
                    r.childInit[root.GetPathTo(c)] = new GMPOInitData(gc.id, gc.authority, gc.owner, gc.priority, gc.pauseable);
                    covered.Add(gc.id);
                    if (saved) SaveObject(save, gc);
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
        return save;
    }

    static void SaveObject(WorldSave save, GMPObject gmpo)
    {
        save.states[gmpo.id] = gmpo.GenerateStateUpdate();
        byte[] custom = gmpo.SaveState();
        if (custom != null)
        {
            save.custom[gmpo.id] = custom;
        }
    }

    /// <summary>Replaces this peer's world with <paramref name="save"/>, spawning everything locally with the saved ids.</summary>
    public static void RestoreWorld(WorldSave save)
    {
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
    }

    /// <summary>Hands everything owned by a peer that is no longer in the lobby to the host.</summary>
    public static void RehomeAuthority(WorldSave save)
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
