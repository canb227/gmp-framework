using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;



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
        Set("collision_mask", Get("collision_mask").AsInt64() & ~GameWorld.QueryHiddenLayer);
    }

    public virtual void AfterInit()
    {
        if (authority == Lobby.selfPeerID)
        {
            if (SyncHelpers.TryReadTransform(desiredState, out var s))
            {
                Teleport(s.pos, s.rot);
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
        return Call("move_and_slide", [velocity, delta]).AsVector3();
    }

    public bool IsOnFloor()
    {
        return Call("is_on_floor").AsBool();
    }

    private void Teleport(Vector3 position, Vector3 rotation)
    {
        Call("teleport", [new Transform3D(Basis.FromEuler(rotation), position)]);
    }
}
