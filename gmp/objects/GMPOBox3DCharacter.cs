using Godot;

public partial class GMPOBox3DCharacter : Node3D, GMPObject
{
    // Godot only reads [Export] on the node class, so each GMPO base repeats this block.
    [ExportGroup("Configuration")]
    [Export]
    public int priority { get; set; }
    [Export]
    public bool pauseable { get; set; }
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

    public override void _EnterTree()
    {
        // Box3DCharacterBody's move_and_slide queries don't skip sensor shapes, so a character would walk
        // into (and be stopped by) query-hidden sensors such as placement previews. Never collide with them.
        Set(Box3DNames.collisionMask, Get(Box3DNames.collisionMask).AsInt64() & ~GameWorld.QueryHiddenLayer);
    }

    public virtual void AfterInit()
    {
        if (authority == Lobby.selfPeerID)
        {
            if (SyncHelpers.TryReadTransform(desiredState, out var s))
            {
                Call(Box3DNames.teleport, [new Transform3D(Basis.FromEuler(s.rot), s.pos)]);
            }
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

    public override void _PhysicsProcess(double delta)
    {
        if (authority != Lobby.selfPeerID)
        {
            if (SyncHelpers.TryReadTransform(desiredState, out var s))
            {
                SyncHelpers.LerpTransform(this, s, SyncHelpers.LerpWeight(syncLerpRate, delta));
            }
        }
    }

    public Vector3 MoveAndSlide(Vector3 velocity, double delta)
    {
        return Call(Box3DNames.moveAndSlide, [velocity, delta]).AsVector3();
    }

    public bool IsOnFloor()
    {
        return Call(Box3DNames.isOnFloor).AsBool();
    }
}
