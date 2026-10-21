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
/// the kill plane); spawns and despawns go to every peer by RPC. Things that are more than an item —
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
    /// <summary>What each item id's colliders have between them (<see cref="ItemRecord.colliderTags"/>).</summary>
    static readonly Dictionary<string, SurfaceTags> colliderTagsByItem = new();

    /// <summary>How often (physics ticks) the host looks for items that fell out of the world.</summary>
    const int KillPlaneInterval = 30;
    ulong ticks;

    public override void _Ready()
    {
        instance = this;
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
        if (insideImpactHook)
        {
            Logging.Error($"Spawn({itemID}) inside OnBreakingImpact, which runs on every peer: use SpawnLocal there", "ItemWorld");
            return 0;
        }
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
            PhysicsWorld.ApplyBodyForce(id, new Vector3(Random.Shared.Next(-5, 5), Random.Shared.Next(-5, 5), Random.Shared.Next(-5, 5)));
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
        // The physics world gives it its definition's tags itself (its kind's), the same as the record starts with.
        PhysicsWorld.SpawnItem(id, kind, Lobby.hostID, transform, linearVelocity, angularVelocity);
        ItemRecord record = new ItemRecord(id, itemID, ItemInfo.Fetch(itemID), colliderTagsByItem[itemID]);
        items[id] = record;
        countByItem[itemID] = CountOf(itemID) + 1;
        return true;
    }

    /// <summary>Removes an item for every peer.</summary>
    public static void Despawn(ulong id, DespawnReason reason = DespawnReason.Removed)
    {
        if (insideImpactHook)
        {
            Logging.Error($"Despawn({id}) inside OnBreakingImpact, which runs on every peer: use RemoveLocal there", "ItemWorld");
            return;
        }
        RPCManager.RPC(instance, nameof(_Despawn), [id, reason]);
    }

    [RPC]
    private void _Despawn(ulong id, DespawnReason reason)
    {
        RemoveLocal(id, reason);
    }

    /// <summary>
    /// Tells every peer an item was hit hard enough to break (host only): each runs its
    /// <see cref="ItemInfo.OnBreakingImpact"/> when its playback of the host reaches the impact. The seed gives every
    /// peer the same random numbers there.
    /// </summary>
    public static void Impact(ulong id, Vector3 hitPosition, float hitSpeed)
    {
        int seed = Random.Shared.Next();
        RPCManager.RPC(instance, nameof(_Impact), [id, hitPosition, hitSpeed, seed]);
    }

    [RPC(playbackDelayed = true)]
    private void _Impact(ulong id, Vector3 hitPosition, float hitSpeed, int seed)
    {
        ItemRecord record = Get(id);
        if (record == null || record.info == null)
        {
            return;
        }
        insideImpactHook = true;
        try
        {
            record.info.OnBreakingImpact(record, hitPosition, hitSpeed, new Random(seed));
        }
        finally
        {
            insideImpactHook = false;
        }
    }

    /// <summary>Set while <see cref="ItemInfo.OnBreakingImpact"/> runs, where Spawn and Despawn (RPCs) would happen once per peer.</summary>
    static bool insideImpactHook;

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

    /// <summary>From GameWorld.ClearWorld, after it has emptied the world.</summary>
    public static void OnWorldCleared()
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
        Godot.Collections.Dictionary description = DescribeKind(scene, info);
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
        colliderTagsByItem[itemID] = (SurfaceTags)description["collider_tags"].AsInt64();
        return kind;
    }

    /// <summary>Puts the item's <see cref="ItemRecord.ownTags"/> on its colliders in the physics world. Call after changing them.</summary>
    public static void TagsChanged(ItemRecord record)
    {
        PhysicsWorld.SetBodyTags(record.id, (ulong)record.ownTags);
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

    /// <summary>From GameWorld, each physics tick of a running game right after the step.</summary>
    public void OnPhysicsStepped(double delta)
    {
        ticks++;
        if (!Lobby.isHost)
        {
            return;
        }
        if (ticks % KillPlaneInterval == 0)
        {
            foreach (long id in PhysicsWorld.ItemsBelow(GameWorld.KillPlaneY))
            {
                ItemRecord record = Get((ulong)id);
                // Where it is now (x and z tell where it fell out): an item that leaves the level in play points at a hole.
                Logging.Warn($"Despawned {record?.itemID} {id}: it fell below the kill plane (y {GameWorld.KillPlaneY}), now at {PhysicsWorld.BodyPosition((ulong)id)}", "ItemWorld");
                Despawn((ulong)id);
            }
        }
        // Every hit this tick, however far from the camera (HitStream only keeps the ones near it): the host's own,
        // and the ones other peers sent (their players bumping items).
        PhysicsWorld.Hits(breakHits, Vector3.Zero, BreakHitRange, 0);
        impactedThisTick.Clear();
        foreach (ContactHit hit in breakHits)
        {
            CheckImpact(hit.idA, (SurfaceTags)hit.tagsA, hit);
            CheckImpact(hit.idB, (SurfaceTags)hit.tagsB, hit);
        }
    }

    /// <summary>Further than any level reaches (m): every hit counts for breaking.</summary>
    const float BreakHitRange = 100000;
    static readonly List<ContactHit> breakHits = new();
    /// <summary>Items that took an impact this tick: one hit can touch at several points, but counts once.</summary>
    static readonly HashSet<ulong> impactedThisTick = new();

    /// <summary>One side of a hit (host only): an impact if it is a breakable item hit hard enough.</summary>
    static void CheckImpact(ulong id, SurfaceTags tags, ContactHit hit)
    {
        // Most hits have no BREAKABLE side, so this cheap test goes first.
        if (!tags.HasFlag(SurfaceTags.BREAKABLE))
        {
            return;
        }
        // Not a loose item (level, structure, player), or one an impact this tick already removed.
        ItemRecord record = Get(id);
        if (record == null || record.info == null)
        {
            return;
        }
        if (hit.speed < record.info.breakSpeed)
        {
            return;
        }
        if (!impactedThisTick.Add(id))
        {
            return;
        }
        Impact(id, hit.position, hit.speed);
    }

    // ---- kinds: what the physics world needs to know about an item's scene ----

    /// <summary>GMPShape properties copied as they are.</summary>
    static readonly string[] ShapeProperties =
    {
        "shape", "size", "radius", "height", "sides", "max_hull_vertices", "density", "friction", "restitution", "rolling_resistance", "tags",
    };

    // GMPShape.shape values whose collider comes from its mesh. Items are dynamic, so a mesh shape becomes a hull too.
    const int HullShape = 4;
    const int MeshShape = 5;

    /// <summary>
    /// The description the physics world registers an item kind from (<c>GMPPhysicsWorld.register_item_kind</c>, see
    /// native/src/physics_world_bodies.cpp for the keys): a collider for every GMPShape in the item's scene, where it sits
    /// in the item, its tags, sync and shadow settings from <paramref name="info"/> (null for an item without one), and a
    /// part for every visible MeshInstance3D. Also sets "collider_tags" (not used by the physics world): what the item's
    /// colliders have between them, which are the item's tags when its definition gives it none.
    /// </summary>
    static Godot.Collections.Dictionary DescribeKind(PackedScene scene, ItemInfo info)
    {
        // Instanced outside the tree only to read it: no body is made.
        Node root = scene.Instantiate();
        Godot.Collections.Dictionary description = new();

        Godot.Collections.Array shapes = new();
        long colliderTags = 0;
        foreach (Node shape in root.FindChildren("*", "GMPShape", true, false))
        {
            Godot.Collections.Dictionary collider = new();
            foreach (string name in ShapeProperties)
            {
                collider[name] = shape.Get(name);
            }
            collider["transform"] = OffsetFromRoot((Node3D)shape, root);
            int shapeType = shape.Get("shape").AsInt32();
            if (shapeType == HullShape || shapeType == MeshShape)
            {
                Mesh mesh = shape.Get("mesh").As<Mesh>();
                collider["shape"] = HullShape;
                if (mesh != null)
                {
                    collider["hull_points"] = mesh.GetFaces();
                }
            }
            colliderTags |= shape.Get("tags").AsInt64();
            shapes.Add(collider);
        }
        if (shapes.Count == 0)
        {
            Logging.Error($"{scene.ResourcePath} has no GMPShape: the item can't be spawned", "ItemWorld");
        }
        description["shapes"] = shapes;
        description["collider_tags"] = colliderTags;

        if (info != null)
        {
            description["tags"] = (long)info.tags;
            description["cast_shadows"] = info.castsShadows;
            description["base_priority"] = info.basePriority;
        }

        Godot.Collections.Array parts = new();
        foreach (Node node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            MeshInstance3D meshInstance = (MeshInstance3D)node;
            if (meshInstance.Mesh == null || !meshInstance.Visible)
            {
                continue;
            }
            Godot.Collections.Dictionary part = new();
            // Not used by the physics world: lets the caller pick out parts (the default box's icon faces).
            part["name"] = meshInstance.Name;
            part["mesh"] = meshInstance.Mesh;
            part["transform"] = OffsetFromRoot(meshInstance, root);
            if (meshInstance.MaterialOverride != null)
            {
                part["material_override"] = meshInstance.MaterialOverride;
            }
            Godot.Collections.Array surfaceMaterials = new();
            for (int surface = 0; surface < meshInstance.Mesh.GetSurfaceCount(); surface++)
            {
                surfaceMaterials.Add(meshInstance.GetSurfaceOverrideMaterial(surface));
            }
            part["surface_materials"] = surfaceMaterials;
            parts.Add(part);
        }
        description["parts"] = parts;

        root.Free();
        return description;
    }

    /// <summary>Where <paramref name="node"/> sits relative to <paramref name="root"/> (not in the tree, so there is no GlobalTransform).</summary>
    static Transform3D OffsetFromRoot(Node3D node, Node root)
    {
        Transform3D offset = node.Transform;
        Node parent = node.GetParent();
        while (parent != null && parent != root)
        {
            if (parent is Node3D parent3D)
            {
                offset = parent3D.Transform * offset;
            }
            parent = parent.GetParent();
        }
        return offset;
    }

    // ---- saves ----

    public static List<ItemSave> Capture()
    {
        List<ItemSave> saves = new();
        foreach (ItemRecord record in items.Values)
        {
            Transform3D transform = PhysicsWorld.BodyTransform(record.id);
            saves.Add(new ItemSave
            {
                id = record.id,
                itemID = record.itemID,
                position = transform.Origin,
                rotation = transform.Basis.GetRotationQuaternion(),
                linearVelocity = PhysicsWorld.BodyLinearVelocity(record.id),
                angularVelocity = PhysicsWorld.BodyAngularVelocity(record.id),
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
