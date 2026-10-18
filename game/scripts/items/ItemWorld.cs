using Godot;
using PolyType;
using System;
using System.Collections.Generic;

/// <summary>What a save keeps of one loose item.</summary>
[GenerateShape]
public partial record struct ItemSave
{
    public ulong id;
    public string itemID;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3 linearVelocity;
    public Vector3 angularVelocity;
}

/// <summary>
/// The loose items in the world (autoload). An item is data, not a node: its body, its sync and its drawing are in the
/// physics world (<see cref="PhysicsWorld"/>, native/src/physics_world.h), and the game keeps an <see cref="ItemRecord"/>
/// per item here, by the same id, on every peer.
/// <para>
/// The host simulates every loose item, so it is the authority of all of them and decides what happens to one (pickups,
/// tag reactions, the kill plane); spawns and despawns go to every peer by RPC. Things that are more than an item —
/// dropped tools, with their own scripts — stay nodes (<see cref="DroppedTool"/>, spawned by <see cref="ItemInfo.SpawnInWorld"/>).
/// </para>
/// </summary>
public partial class ItemWorld : Node
{
    public static ItemWorld instance { get; private set; }

    /// <summary>Every loose item on this peer, by id.</summary>
    public static readonly Dictionary<ulong, ItemRecord> items = new();

    /// <summary>On every peer, after an item has gone (picked up, consumed, despawned); its record is out of <see cref="items"/> already.</summary>
    public static event Action<ItemRecord, DespawnReason> ItemRemoved;

    /// <summary>How many loose items there are in the world, of any kind.</summary>
    public static int totalCount => items.Count;
    static readonly Dictionary<string, int> countByItem = new();

    /// <summary>The scene an item without one of its own shows as: a box with its icon on the sides.</summary>
    const string DefaultScene = "res://game/scenes/items/DefaultDroppedBox.tscn";

    /// <summary>The physics world's item kind for each item id, registered the first time one is spawned on this peer.</summary>
    static readonly Dictionary<string, int> kindByItem = new();

    /// <summary>How often (physics ticks) the host looks for items that fell out of the world.</summary>
    const int KillPlaneInterval = 30;
    ulong ticks;

    public override void _Ready()
    {
        instance = this;
        GameWorld.PhysicsStepped += OnPhysicsStepped;
        GameWorld.WorldCleared += OnWorldCleared;
    }

    /// <summary>How many of <paramref name="itemID"/> there are in the world.</summary>
    public static int CountOf(string itemID)
    {
        return countByItem.GetValueOrDefault(itemID, 0);
    }

    public static ItemRecord Get(ulong id)
    {
        return items.GetValueOrDefault(id);
    }

    // ---- spawning ----

    /// <summary>Spawns a loose <paramref name="itemID"/> for every peer; returns its id. Use <see cref="ItemInfo.SpawnInWorld"/>, which also handles tools.</summary>
    public static ulong Spawn(string itemID, Vector3 position, Vector3 rotation, Vector3 velocity = default)
    {
        ulong id = (ulong)Random.Shared.NextInt64();
        RPCManager.RPC(instance, nameof(_Spawn), [id, itemID, position, rotation, velocity]);
        return id;
    }

    [RPC]
    private void _Spawn(ulong id, string itemID, Vector3 position, Vector3 rotation, Vector3 velocity)
    {
        if (!SpawnLocal(id, itemID, new Transform3D(Basis.FromEuler(rotation), position), velocity, Vector3.Zero))
        {
            return;
        }
        // A small nudge for a fresh spawn (not for a restore, which leaves everything where it was saved).
        if (Lobby.isHost)
        {
            PhysicsWorld.ApplyItemForce(id, new Vector3(Random.Shared.Next(-5, 5), Random.Shared.Next(-5, 5), Random.Shared.Next(-5, 5)));
        }
    }

    /// <summary>Spawns an item on this peer only, with a known id (spawn RPCs and restores). False if it couldn't (logged).</summary>
    public static bool SpawnLocal(ulong id, string itemID, Transform3D transform, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        if (items.ContainsKey(id))
        {
            return false;
        }
        int kind = KindOf(itemID);
        if (kind < 0)
        {
            return false;
        }
        PhysicsWorld.SpawnItem(id, kind, Lobby.hostID, transform, linearVelocity, angularVelocity);
        ItemRecord record = new ItemRecord(id, itemID, ItemInfo.Fetch(itemID));
        items[id] = record;
        countByItem[itemID] = CountOf(itemID) + 1;
        TagsChanged(record);
        return true;
    }

    /// <summary>Removes an item for every peer.</summary>
    public static void Despawn(ulong id, DespawnReason reason = DespawnReason.Removed)
    {
        RPCManager.RPC(instance, nameof(_Despawn), [id, reason]);
    }

    [RPC]
    private void _Despawn(ulong id, DespawnReason reason)
    {
        RemoveLocal(id, reason);
    }

    /// <summary>Removes this peer's copy of an item without messaging anyone (for handlers that run on every peer).</summary>
    public static void RemoveLocal(ulong id, DespawnReason reason = DespawnReason.Removed)
    {
        if (!items.Remove(id, out ItemRecord record))
        {
            return;
        }
        countByItem[record.itemID] = CountOf(record.itemID) - 1;
        PhysicsWorld.DespawnItem(id);
        ItemRemoved?.Invoke(record, reason);
    }

    static void OnWorldCleared()
    {
        // The physics world has dropped its items already (GameWorld.ClearWorld).
        items.Clear();
        countByItem.Clear();
    }

    /// <summary>The physics world's kind for <paramref name="itemID"/>, registering it the first time; -1 if it can't be.</summary>
    static int KindOf(string itemID)
    {
        if (kindByItem.TryGetValue(itemID, out int kind))
        {
            return kind;
        }
        ItemInfo info = ItemInfo.Fetch(itemID);
        PackedScene scene = info?.droppedScene;
        bool defaultBox = scene == null;
        if (defaultBox)
        {
            scene = GD.Load<PackedScene>(DefaultScene);
        }
        Godot.Collections.Dictionary description = ItemKinds.Describe(scene, info);
        if (defaultBox && info?.icon != null)
        {
            // Its icon on the box's four sides (the M1-M4 planes).
            StandardMaterial3D iconMaterial = new();
            iconMaterial.AlbedoTexture = info.icon;
            foreach (Godot.Collections.Dictionary part in description["parts"].AsGodotArray<Godot.Collections.Dictionary>())
            {
                if (part["name"].AsString().StartsWith("M"))
                {
                    part["material_override"] = iconMaterial;
                }
            }
        }
        kind = PhysicsWorld.RegisterItemKind(description);
        if (kind < 0)
        {
            Logging.Error($"Can't make a physics kind for {itemID} ({scene.ResourcePath})", "ItemWorld");
        }
        kindByItem[itemID] = kind;
        return kind;
    }

    /// <summary>
    /// Only items with a watcher tag report their touches (TagInteractions): each report is work per new contact, and
    /// common materials touching in a pile would report every bounce. Call after changing an item's tags.
    /// </summary>
    public static void TagsChanged(ItemRecord record)
    {
        PhysicsWorld.SetItemReportsTouches(record.id, TagInteractions.IsWatcher(record.tags));
    }

    // ---- pickups: the host decides, so two players can't both get one ----

    /// <summary>The local <paramref name="player"/> pressed interact on the item.</summary>
    public static void Interact(ItemRecord record, FactoryPlayer player)
    {
        if (!record.canBePickedUp || !player.inventory.HasRoomFor(record.itemID))
        {
            return;
        }
        RPCManager.RPCTo(Lobby.hostID, instance, nameof(_RequestPickup), [record.id, player.id]);
    }

    [RPC]
    private void _RequestPickup(ulong itemId, ulong playerObjectId)
    {
        if (!Lobby.isHost || !items.TryGetValue(itemId, out ItemRecord record) || record.taken || !record.canBePickedUp)
        {
            return;
        }
        // The requester must actually control the player it names.
        if (!GameWorld.syncedObjs.TryGetValue(playerObjectId, out GMPObject p) || p.authority != RPCManager.sender)
        {
            return;
        }
        record.taken = true;
        RPCManager.RPC(instance, nameof(_ApplyPickup), [itemId, playerObjectId]);
    }

    [RPC]
    private void _ApplyPickup(ulong itemId, ulong playerObjectId)
    {
        if (RPCManager.sender != Lobby.hostID || !items.TryGetValue(itemId, out ItemRecord record))
        {
            return;
        }
        record.taken = true;
        if (GameWorld.syncedObjs.TryGetValue(playerObjectId, out GMPObject p) && p is FactoryPlayer player && player.isLocal)
        {
            player.ReceiveItem(record.itemID);
        }
        RemoveLocal(itemId, DespawnReason.PickedUp);
    }

    // ---- each physics tick, after the step ----

    void OnPhysicsStepped(double delta)
    {
        ticks++;
        SendViewers();
        if (!Lobby.isHost)
        {
            return;
        }
        // Reactions run on the host, which simulates every item; anything they change must be replicated.
        long[] touches = PhysicsWorld.ItemTouches();
        for (int i = 0; i + 1 < touches.Length; i += 2)
        {
            ItemRecord a = Get((ulong)touches[i]);
            ItemRecord b = Get((ulong)touches[i + 1]);
            if (a == null || b == null)
            {
                continue;
            }
            TagInteractions.OnTouch(a, b);
            TagInteractions.OnTouch(b, a);
        }
        if (ticks % KillPlaneInterval == 0)
        {
            foreach (long id in PhysicsWorld.ItemsBelow(GameWorld.KillPlaneY))
            {
                ItemRecord record = Get((ulong)id);
                // Where it is now (x and z tell where it fell out): an item that leaves the level in play points at a hole.
                Logging.Warn($"Despawned {record?.itemID} {id}: it fell below the kill plane (y {GameWorld.KillPlaneY}), now at {PhysicsWorld.ItemPosition((ulong)id)}", "ItemWorld");
                Despawn((ulong)id);
            }
        }
    }

    /// <summary>Every player's position and view, for the items' sync priorities (what someone is near or looking at is sent more often).</summary>
    static void SendViewers()
    {
        int count = FactoryPlayer.all.Count;
        Vector3[] positions = new Vector3[count];
        Vector3[] eyes = new Vector3[count];
        Vector3[] looks = new Vector3[count];
        for (int i = 0; i < count; i++)
        {
            FactoryPlayer player = FactoryPlayer.all[i];
            Transform3D view = player.camera.GlobalTransform;
            positions[i] = player.GlobalPosition;
            eyes[i] = view.Origin;
            looks[i] = -view.Basis.Z;
        }
        PhysicsWorld.SetViewers(positions, eyes, looks);
    }

    // ---- saves ----

    public static List<ItemSave> Capture()
    {
        List<ItemSave> saves = new();
        foreach (ItemRecord record in items.Values)
        {
            Transform3D transform = PhysicsWorld.ItemTransform(record.id);
            saves.Add(new ItemSave
            {
                id = record.id,
                itemID = record.itemID,
                position = transform.Origin,
                rotation = transform.Basis.GetRotationQuaternion(),
                linearVelocity = PhysicsWorld.ItemLinearVelocity(record.id),
                angularVelocity = PhysicsWorld.ItemAngularVelocity(record.id),
            });
        }
        return saves;
    }

    /// <summary>Spawns the saved items on this peer, after the world was cleared for a restore.</summary>
    public static void Restore(List<ItemSave> saves)
    {
        foreach (ItemSave save in saves)
        {
            SpawnLocal(save.id, save.itemID, new Transform3D(new Basis(save.rotation), save.position), save.linearVelocity, save.angularVelocity);
        }
    }
}
