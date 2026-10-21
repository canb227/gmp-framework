using Godot;

/// <summary>
/// GMPObject base for walking characters (the player). Attach a script deriving from this to a <c>GMPCharacter</c> node
/// (our C++ character, native/src/physics_character.h; C# sees it as a Node3D), whose origin is at the feet. The
/// character's authority moves it with <see cref="Move"/> each physics tick; every other peer places it from the states
/// the subclass sends (<see cref="GenerateStateUpdate"/>), and its body follows the node, so it pushes items and blocks
/// other characters everywhere.
/// </summary>
public partial class GMPOCharacter : Node3D, GMPObject
{
    // Godot only reads [Export] on the node class, so each GMPO base repeats this block.
    [ExportGroup("Configuration")]
    [Export]
    public int priority { get; set; }
    [Export]
    public bool pauseable { get; set; } = true;

    [ExportGroup("READONLY")]
    [Export]
    public ulong id { get; set; }
    [Export]
    public ulong authority { get; set; }
    [Export]
    public ulong owner { get; set; }
    [Export]
    public int priorityAccumulator { get; set; }

    /// <summary>Where the character looks from and toward (forward is -Z): the bodies it sees sync more often.</summary>
    public virtual Transform3D ViewTransform()
    {
        return GlobalTransform;
    }

    public virtual void AfterInit()
    {
    }

    /// <summary>Nothing by default: a subclass sends where its character is (and anything else) itself.</summary>
    public virtual byte[] GenerateStateUpdate()
    {
        return null;
    }

    public virtual void ApplyStateUpdate(byte[] update)
    {
    }

    public virtual byte[] SaveState()
    {
        return null;
    }

    public virtual void LoadState(byte[] state)
    {
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
    /// <para>What it lands on or runs into goes into the physics world's hits (<see cref="HitStream"/>), with the character as side A.</para>
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
