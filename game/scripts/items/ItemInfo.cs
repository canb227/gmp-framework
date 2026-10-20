using Godot;
using PolyType;
using System;
using System.Collections.Generic;

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

    // ---- as a loose item in the world (see ItemWorld) ----

    [ExportGroup("In the world")]
    /// <summary>What the whole item is made of and does (see TagInteractions); none leaves it what its colliders have.</summary>
    [Export]
    public ItemTags tags;

    /// <summary>What it is worth when turned in to the void, by currency (empty: nothing, though the deposit still counts).</summary>
    [Export]
    public Godot.Collections.Dictionary<Currency, int> currencyValue = new();

    /// <summary>Shows its name under the crosshair.</summary>
    [Export]
    public bool canBePickTarget;

    /// <summary>A magnet rod can hold it.</summary>
    [Export]
    public bool canBeGrabbed;

    /// <summary>Interact (F) puts it in the inventory.</summary>
    [Export]
    public bool canBePickedUp;

    /// <summary>Sync priority before the awake, nearness and held bonuses; -1 never sends.</summary>
    [Export]
    public int basePriority = 1;

    /// <summary>
    /// Whether its meshes cast shadows. Off by default: with thousands of items, their shadow passes cost more render
    /// time than they're worth on small things.
    /// </summary>
    [Export]
    public bool castsShadows;

    /// <summary>
    /// Spawns <paramref name="itemID"/> in the world for every peer. A tool (whose droppedScene has a
    /// <see cref="DroppedTool"/> root) spawns as that node; anything else is a loose item (<see cref="ItemWorld"/>), drawn
    /// from its droppedScene, or as a box with its icon if it has none. Returns the new item's (or node's) id.
    /// <para>
    /// The host simulates all loose items and dropped tools, so it is the authority of every one, whoever spawns it.
    /// </para>
    /// </summary>
    public static ulong SpawnInWorld(string itemID, Vector3 position, Vector3 rotation = default, Vector3 velocity = default)
    {
        ItemInfo item = Fetch(itemID);
        if (item?.droppedScene != null && IsTool(item.droppedScene))
        {
            GMPOInitData init = new() { authority = Lobby.hostID };
            return GameWorld.SpawnScene(item.droppedScene, position, rotation, init);
        }
        return ItemWorld.Spawn(itemID, position, rotation, velocity);
    }

    /// <summary>Whether each dropped scene looked at so far has a <see cref="DroppedTool"/> root (a node), or is a loose item's.</summary>
    static readonly Dictionary<string, bool> toolScenes = new();

    static bool IsTool(PackedScene scene)
    {
        if (!toolScenes.TryGetValue(scene.ResourcePath, out bool tool))
        {
            Node root = scene.Instantiate();
            tool = root is DroppedTool;
            root.Free();
            toolScenes[scene.ResourcePath] = tool;
        }
        return tool;
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
            if (itemID.StartsWith("quest"))
            {
                return null;
            }
            string path = FindItemDefinition("res://game/definitions", itemID + ".tres");
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
