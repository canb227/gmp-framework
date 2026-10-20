using Godot;

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
    public byte[] desiredState { get; set; }

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
        desiredState = update;
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
    // first be matched against every property of this C# script.

    static readonly StringName bodyTypeName = "body_type";
    static readonly StringName enabledName = "enabled";
    static readonly StringName tagsName = "tags";
    static readonly StringName getAllTagsName = "get_all_tags";
    static readonly StringName collisionLayerName = "collision_layer";
    static readonly StringName collisionMaskName = "collision_mask";
    static readonly StringName gravityScaleName = "gravity_scale";
    static readonly StringName teleportName = "teleport";
    static readonly StringName getLinearVelocityName = "get_linear_velocity";
    static readonly StringName setLinearVelocityName = "set_linear_velocity";
    static readonly StringName getAngularVelocityName = "get_angular_velocity";
    static readonly StringName setAngularVelocityName = "set_angular_velocity";
    static readonly StringName applyCentralImpulseName = "apply_central_impulse";
    static readonly StringName applyImpulseName = "apply_impulse";
    static readonly StringName applyCentralForceName = "apply_central_force";
    static readonly StringName applyAngularImpulseName = "apply_angular_impulse";
    static readonly StringName getMassName = "get_mass";
    static readonly StringName isAwakeName = "is_awake";
    static readonly StringName setAwakeName = "set_awake";

    public BodyTypeEnum bodyType
    {
        get => (BodyTypeEnum)ClassDB.ClassGetProperty(this, bodyTypeName).AsInt32();
        set => ClassDB.ClassSetProperty(this, bodyTypeName, (int)value);
    }

    /// <summary>Off: no body in the world at all.</summary>
    public bool enabled
    {
        get => ClassDB.ClassGetProperty(this, enabledName).AsBool();
        set => ClassDB.ClassSetProperty(this, enabledName, value);
    }

    /// <summary>Tags the whole body was given (bits: tag n is bit n), on top of each GMPShape's own. 0: none of its own.</summary>
    public ulong tags
    {
        get => (ulong)ClassDB.ClassGetProperty(this, tagsName).AsInt64();
        set => ClassDB.ClassSetProperty(this, tagsName, value);
    }

    /// <summary>The body's tags: the ones it was given, or what its shapes have between them if it was given none.</summary>
    public ulong allTags => (ulong)Call(getAllTagsName).AsInt64();

    public long collisionLayer
    {
        get => ClassDB.ClassGetProperty(this, collisionLayerName).AsInt64();
        set => ClassDB.ClassSetProperty(this, collisionLayerName, value);
    }

    public long collisionMask
    {
        get => ClassDB.ClassGetProperty(this, collisionMaskName).AsInt64();
        set => ClassDB.ClassSetProperty(this, collisionMaskName, value);
    }

    public float gravityScale
    {
        get => ClassDB.ClassGetProperty(this, gravityScaleName).AsSingle();
        set => ClassDB.ClassSetProperty(this, gravityScaleName, value);
    }

    /// <summary>Moves the node and its body there at once.</summary>
    public void Teleport(Transform3D transform)
    {
        Call(teleportName, transform);
    }

    public void Teleport(Vector3 position, Vector3 rotation)
    {
        Teleport(new Transform3D(Basis.FromEuler(rotation), position));
    }

    public void Teleport(Vector3 position)
    {
        Teleport(new Transform3D(Basis, position));
    }

    public Vector3 GetLinearVelocity()
    {
        return Call(getLinearVelocityName).AsVector3();
    }

    public void SetLinearVelocity(Vector3 velocity)
    {
        Call(setLinearVelocityName, velocity);
    }

    public Vector3 GetAngularVelocity()
    {
        return Call(getAngularVelocityName).AsVector3();
    }

    public void SetAngularVelocity(Vector3 velocity)
    {
        Call(setAngularVelocityName, velocity);
    }

    public void ApplyCentralImpulse(Vector3 impulse)
    {
        Call(applyCentralImpulseName, impulse);
    }

    public void ApplyImpulse(Vector3 impulse, Vector3 point)
    {
        Call(applyImpulseName, impulse, point);
    }

    public void ApplyCentralForce(Vector3 force)
    {
        Call(applyCentralForceName, force);
    }

    public void ApplyAngularImpulse(Vector3 impulse)
    {
        Call(applyAngularImpulseName, impulse);
    }

    public float GetMass()
    {
        return Call(getMassName).AsSingle();
    }

    public bool IsAwake()
    {
        return Call(isAwakeName).AsBool();
    }

    public void SetAwake(bool awake)
    {
        Call(setAwakeName, awake);
    }
}
