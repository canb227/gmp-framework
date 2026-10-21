using Godot;

/// <summary>How a <c>GMPBody</c> moves (its <c>body_type</c>).</summary>
public enum BodyTypeEnum
{
    Static = 0,
    Kinematic = 1,
    Dynamic = 2
}


/// <summary>
/// GMPObject base for physics bodies that are nodes: structures, tools, buttons and the like. Attach a script deriving
/// from this to a <c>GMPBody</c> node (our C++ body, native/src/physics_body.h; C# sees it as a Node3D), with
/// <c>GMPShape</c> nodes under it for its colliders.
/// <para>
/// A dynamic one is synced by the physics world like a loose item (simulated by its authority, followed everywhere
/// else; native/src/physics_world_sync.cpp), so it sends no state of its own. Static and kinematic ones don't move by
/// physics and aren't synced. GMPBody's methods are called by name (C# can't see the C++ class); each call costs about
/// a microsecond.
/// </para>
/// </summary>
public partial class GMPOBody : Node3D, GMPObject
{
    // Godot only reads [Export] on the node class, so each GMPO base repeats this block.
    [ExportGroup("Configuration")]
    [Export]
    public int priority { get; set; }
    [Export]
    public bool pauseable { get; set; } = true;

    [ExportGroup("Hover")]
    /// <summary>Name shown under the crosshair while a player looks at this body; empty shows none.</summary>
    [Export]
    public string hoverName { get; set; }
    /// <summary>Line shown below <see cref="hoverName"/>, e.g. "Press F to pick up!"; empty shows none.</summary>
    [Export]
    public string hoverText { get; set; }

    [ExportGroup("READONLY")]
    [Export]
    public ulong id { get; set; }
    [Export]
    public ulong authority { get; set; }
    [Export]
    public ulong owner { get; set; }
    [Export]
    public int priorityAccumulator { get; set; }

    /// <summary>Its authority simulates it; every other peer follows (the physics world does both).</summary>
    public virtual void AfterInit()
    {
        PhysicsWorld.SetBodyAuthority(id, authority);
    }

    public virtual void OnAuthorityChanged()
    {
        PhysicsWorld.SetBodyAuthority(id, authority);
    }

    public virtual void OnDespawned(DespawnReason reason)
    {
    }

    /// <summary>Its pose is synced by the physics world; a subclass with state of its own sends that.</summary>
    public virtual byte[] GenerateStateUpdate()
    {
        return null;
    }

    public virtual void ApplyStateUpdate(byte[] update)
    {
    }

    public virtual void UpdateSyncPriority()
    {
    }

    public virtual byte[] SaveState()
    {
        return null;
    }

    public virtual void LoadState(byte[] state)
    {
    }

    // ---- GMPBody ----
    // Its properties and methods, called by name with cached names (C# can't see the C++ class). A plain Get/Set would
    // first be matched against every property of this C# script. For anything else, the by-id calls in PhysicsWorld
    // work on node bodies too (velocities, forces, mass).

    static readonly StringName bodyTypeName = "body_type";
    static readonly StringName tagsName = "tags";
    static readonly StringName teleportName = "teleport";

    /// <summary>Static (never moves), kinematic (driven to its node each tick) or dynamic (simulated; its node follows it).</summary>
    public BodyTypeEnum bodyType
    {
        get => (BodyTypeEnum)ClassDB.ClassGetProperty(this, bodyTypeName).AsInt32();
        set => ClassDB.ClassSetProperty(this, bodyTypeName, (int)value);
    }

    /// <summary>Tags the whole body was given (bits: tag n is bit n, see SurfaceTags), on top of each GMPShape's own. 0: none of its own.</summary>
    public ulong tags
    {
        get => (ulong)ClassDB.ClassGetProperty(this, tagsName).AsInt64();
        set => ClassDB.ClassSetProperty(this, tagsName, value);
    }

    /// <summary>Moves the node and its body there at once.</summary>
    public void Teleport(Transform3D transform)
    {
        Call(teleportName, transform);
    }
}
