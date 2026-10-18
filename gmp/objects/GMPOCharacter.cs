using Godot;

/// <summary>
/// GMPObject base for walking characters (the player). Attach a script deriving from this to a <c>GMPCharacter</c> node
/// (our C++ character, native/src/physics_character.h; C# sees it as a Node3D), whose origin is at the feet. The
/// character's authority moves it with <see cref="Move"/> each physics tick; every other peer places it from its states,
/// and its body follows the node, so it pushes items and blocks other characters everywhere.
/// </summary>
public partial class GMPOCharacter : Node3D, GMPObject
{
    // Godot only reads [Export] on the node class, so each GMPO base repeats this block.
    [ExportGroup("Configuration")]
    [Export]
    public int priority { get; set; }
    [Export]
    public bool pauseable { get; set; } = true;
    /// <summary>How quickly non-authority copies close the gap to replicated state (1/s); 0 snaps.</summary>
    [Export]
    public float syncLerpRate { get; set; } = 10f;

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

    public virtual void AfterInit()
    {
        if (authority == Lobby.selfPeerID && SyncHelpers.TryReadTransform(desiredState, out var s))
        {
            TeleportCharacter(new Transform3D(Basis.FromEuler(s.rot), s.pos));
        }
    }

    public virtual byte[] GenerateStateUpdate()
    {
        return SyncHelpers.WriteTransform(this);
    }

    public virtual void ApplyStateUpdate(byte[] update)
    {
        desiredState = update;
    }

    public virtual byte[] SaveState()
    {
        return null;
    }

    public virtual void LoadState(byte[] state)
    {
    }

    public override void _PhysicsProcess(double delta)
    {
        if (authority != Lobby.selfPeerID && SyncHelpers.TryReadTransform(desiredState, out var s))
        {
            SyncHelpers.LerpTransform(this, s, SyncHelpers.LerpWeight(syncLerpRate, delta));
        }
    }

    // ---- GMPCharacter ----
    // Called by name with cached names (C# can't see the C++ class); about a microsecond each.

    static readonly StringName moveName = "move";
    static readonly StringName teleportCharacterName = "teleport_character";
    static readonly StringName isOnFloorName = "is_on_floor";
    static readonly StringName getFloorNormalName = "get_floor_normal";
    static readonly StringName getFloorVelocityName = "get_floor_velocity";
    static readonly StringName canFitHeightName = "can_fit_height";
    static readonly StringName heightName = "height";

    /// <summary>
    /// Moves the character by <paramref name="velocity"/> over <paramref name="delta"/> seconds, sliding along what it
    /// meets, and returns its velocity afterwards. The legs hold it at standing height while it is on the floor, so
    /// give it no vertical velocity there (but a jump's).
    /// </summary>
    public Vector3 Move(Vector3 velocity, double delta)
    {
        return Call(moveName, velocity, (float)delta).AsVector3();
    }

    /// <summary>Puts the character there at once, without sweeping its body across (which would bowl things over).</summary>
    public void TeleportCharacter(Transform3D transform)
    {
        Call(teleportCharacterName, transform);
    }

    /// <summary>On walkable ground, as of the last <see cref="Move"/>.</summary>
    public bool IsOnFloor()
    {
        return Call(isOnFloorName).AsBool();
    }

    public Vector3 FloorNormal()
    {
        return Call(getFloorNormalName).AsVector3();
    }

    /// <summary>How fast the ground under it moves (a moving body, a belt's surface): standing still on it means moving with it.</summary>
    public Vector3 FloorVelocity()
    {
        return Call(getFloorVelocityName).AsVector3();
    }

    /// <summary>Whether the character would fit at <paramref name="height"/> where it stands (standing up from a crouch).</summary>
    public bool CanFitHeight(float height)
    {
        return Call(canFitHeightName, height).AsBool();
    }

    /// <summary>The capsule's full height from the feet.</summary>
    public float capsuleHeight
    {
        get => ClassDB.ClassGetProperty(this, heightName).AsSingle();
        set => ClassDB.ClassSetProperty(this, heightName, value);
    }
}
