using Godot;
using PolyType;
using System;
using System.Collections.Generic;

/// <summary>
/// An item's definition: game/definitions/**/&lt;itemID&gt;.tres. Refer to items by this resource in the editor
/// (<c>[Export] ItemInfo</c>); the <see cref="itemID"/> string is for the wire, saves and inventories (<see cref="Fetch"/>).
/// Subclasses add what kinds of item do: <see cref="PurchasableItem"/>, <see cref="BlueprintItem"/>, <see cref="EquipmentItem"/>.
/// </summary>
[GlobalClass]
[GenerateShape]
public partial class ItemInfo : Resource
{
    /// <summary>Unique id, the same as the .tres file's name.</summary>
    [Export]
    public string itemID;

    /// <summary>
    /// How it looks and collides lying in the world: a GMPBody root with GMPShapes and meshes, no script (a loose item),
    /// or a <see cref="DroppedTool"/> root (a tool, which stays a node). None: a box with the <see cref="icon"/> on it.
    /// </summary>
    [Export]
    public PackedScene droppedScene;

    /// <summary>Shown in the inventory, the shop and under the crosshair.</summary>
    [Export]
    public string displayName;

    [Export]
    public string description;

    /// <summary>The inventory and shop picture.</summary>
    [Export]
    public CompressedTexture2D icon;

    /// <summary>Shown in the player's hand while equipped. Its root must be a <see cref="HeldItem"/> (a subclass does a tool's work). None: a box with the <see cref="icon"/> on it.</summary>
    [Export]
    public PackedScene inHandScene;

    /// <summary>How many fit in one inventory slot.</summary>
    [Export]
    public int maxStackSize = 1;

    // ---- as a loose item in the world (see ItemWorld) ----

    [ExportGroup("In the world")]
    /// <summary>The whole item's tags (see <see cref="SurfaceTags"/>); none leaves it what its colliders have.</summary>
    [Export]
    public SurfaceTags tags;

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
    /// With the BREAKABLE tag (the item's own or the collider that was hit): an impact at this speed or more (m/s, how
    /// fast the two sides approached) calls <see cref="OnBreakingImpact"/>.
    /// </summary>
    [Export]
    public float breakSpeed = 8;

    /// <summary>
    /// Runs on every peer when the host saw the item hit at <see cref="breakSpeed"/> or more: on the host at once, on
    /// the others when their playback of the host reaches the impact, so whatever it does happens as each peer sees the
    /// hit. What the item does is up to it: by default it breaks (is removed). Override it in a subclass, set as the
    /// script of the item's .tres, to crack, shed pieces, survive, and so on.
    /// <para>
    /// It already runs on every peer, so it must decide the same on every peer and change only this peer's copy:
    /// <list type="bullet">
    /// <item>Use the local calls (<see cref="ItemWorld.RemoveLocal"/>, <see cref="ItemWorld.SpawnLocal"/>), never the
    /// RPC ones (<see cref="ItemWorld.Spawn"/>, <see cref="ItemWorld.Despawn"/>), which would happen once per peer;
    /// those log an error and do nothing in here.</item>
    /// <item>Take every random number (shard ids, offsets) from <paramref name="rng"/>, which gives every peer the same
    /// ones, and draw from it the same way on every peer: one draw inside a <see cref="Lobby.isHost"/> branch puts the
    /// host's numbers out of step.</item>
    /// <item>Decide only from what every peer has: the item's record (keep anything it should remember between hits
    /// there), the hit and <paramref name="rng"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    public virtual void OnBreakingImpact(ItemRecord item, Vector3 hitPosition, float hitSpeed, Random rng)
    {
        ItemWorld.RemoveLocal(item.id, DespawnReason.Broken);
    }

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
            // Some props (e.g. spawner test items, the wrench) have no definition.
            if (path != null)
            {
                item = ResourceLoader.Load<ItemInfo>(path);
            }

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
