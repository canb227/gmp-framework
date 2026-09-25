using Godot;
using Nerdbank.MessagePack;
using Nerdbank.MessagePack.Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GlobalClass]
public partial class GMPONode3D : Node3D, GMPObject
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

    public virtual void AfterInit()
    {

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

}
