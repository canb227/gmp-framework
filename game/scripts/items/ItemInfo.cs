using Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GlobalClass]
[GenerateShape]
public partial class ItemInfo : Resource
{
    [Export]
    public string itemID;

    [Export]
    public PackedScene droppedScene;

    [Export]
    public string displayName;

    [Export]
    public string description;

    [Export]
    public CompressedTexture2D icon;

    [Export]
    public PackedScene inHandScene;

    [Export]
    public int maxStackSize = 1;



    public ItemInfo()
    {

    }


    const string DefaultDroppedScene = "res://game/scenes/items/world/DefaultDroppedBox.tscn";

    /// <summary>
    /// Spawns <paramref name="itemID"/> in the world for every peer: its <see cref="droppedScene"/>, or a labelled
    /// <c>DefaultDroppedBox</c> if it has none. Returns the new object's id.
    /// </summary>
    public static ulong SpawnInWorld(string itemID, Vector3 position, Vector3 rotation = default, NodePath parentPath = default)
    {
        //Logging.Log($"guh0 {parentPath}", "GameWorld");
        ItemInfo item = Fetch(itemID);
        if (item?.droppedScene != null)
        {
            return GameWorld.SpawnScene(item.droppedScene, position, rotation, parent: parentPath);
        }
        ulong id = GameWorld.SpawnScene(DefaultDroppedScene, position, rotation, parent: parentPath);
        (GameWorld.syncedObjs[id] as DefaultDroppedBox).boxInit(itemID);
        return id;
    }

    // Every definition ever fetched, held strongly (null for ids without one). Definitions are script-backed
    // RefCounted resources: if nothing holds one, the C# GC finalizer can dispose it on its own thread while
    // a later Fetch is reloading it from Godot's cache, which crashes (hotbar scrolling fetched fast enough
    // to hit this). Caching also makes Fetch cheap, which matters as it runs per slot and per frame.
    static readonly Dictionary<string, ItemInfo> definitions = new();

    /// <summary>The definition for <paramref name="itemID"/>, or null if it has none. Main thread only.</summary>
    public static ItemInfo Fetch(string itemID)
    {
        if (itemID == null)
            return null;

        if (!definitions.TryGetValue(itemID, out ItemInfo item))
        {
            string path = FindItemDefinition(
                "res://game/definitions",
                itemID + ".tres"
            );

            // Some props (e.g. spawner test items) have no definition.
            item = ResourceLoader.Load<ItemInfo>(path);

            definitions[itemID] = item;
        }

        return item;
    }

    private static string FindItemDefinition(string directoryPath, string fileName)
    {
        using var dir = DirAccess.Open(directoryPath);
        if (dir == null)
            return null;

        dir.ListDirBegin();

        while (true)
        {
            string name = dir.GetNext();

            if (string.IsNullOrEmpty(name))
                break;

            if (name.StartsWith("."))
                continue;

            string fullPath = $"{directoryPath}/{name}";

            if (dir.CurrentIsDir())
            {
                string found = FindItemDefinition(fullPath, fileName);
                if (found != null)
                    return found;
            }
            else if (name.Equals(fileName, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath;
            }
        }

        return null;
    }

}
