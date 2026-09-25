using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

public enum BodyTypeEnum
{
    Static = 0,
    Kinematic = 1,
    Dynamic = 2
}


/// <summary>
/// GMPObject base for Box3D physics bodies. Box3D is a GDExtension and isn't exposed to C#, so a script
/// deriving from this class is attached to a node of type <c>Box3DBody</c> in the editor (C# sees it as a
/// Node3D) and reaches the Box3DBody API through <c>Call</c>/<c>Get</c>/<c>Set</c>.
/// </summary>
public partial class GMPOBox3DBody : Node3D, GMPObject
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

    /// <summary>The body type set in the scene; restored whenever this peer becomes the authority.</summary>
    public BodyTypeEnum authoredBodyType { get; private set; }

    public virtual void AfterInit()
    {
        authoredBodyType = GetBodyType();
        if (authority == Lobby.selfPeerID)
        {
            if (SyncHelpers.TryReadTransform(desiredState, out var s))
            {
                Teleport(s.pos, s.rot);
            }

        }
        else
        {
            SetBodyType(BodyTypeEnum.Kinematic);
        }


    }

    /// <summary>The authority simulates with the authored body type; everyone else follows as Kinematic.</summary>
    public virtual void OnAuthorityChanged()
    {
        desiredState = null;
        SetBodyType(authority == Lobby.selfPeerID ? authoredBodyType : BodyTypeEnum.Kinematic);
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

    public BodyTypeEnum GetBodyType()
    {
        return (BodyTypeEnum)Get("body_type").AsInt32();
    }

    public void SetBodyType(BodyTypeEnum value)
    {
        Set("body_type", (int)value);
    }

    public void Teleport(Transform3D transform)
    {
        Call("teleport", [transform]);
    }

    public void Teleport(Vector3 position, Vector3 rotation)
    {
        Call("teleport", [new Transform3D(Basis.FromEuler(rotation), position)]);
    }

    public void Teleport(Vector3 position)
    {
        Call("teleport", [new Transform3D(Basis, position)]);
    }

    public void Enable()
    {
        Set("enabled", true);
    }

    public void Disable()
    {
        Set("enabled", false);
    }
    public void ApplyCentralForce(Vector3 force)
    {
        Call("apply_central_force", [force]);
    }
}
