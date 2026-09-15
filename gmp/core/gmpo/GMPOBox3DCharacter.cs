using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;



public partial class GMPOBox3DCharacter : Node3D, GMPObject
{
    [ExportGroup("Configuration")]
    [Export]
    public int priority { get; set; }
    [Export]
    public bool pauseable { get; set; }

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

    public Vector3 MoveAndSlide(Vector3 velocity, double delta)
    {
        return Call("move_and_slide", [velocity, delta]).AsVector3();
    }

    public bool IsOnFloor()
    {
        return Call("is_on_floor").AsBool();
    }


    public virtual void AfterInit()
    {
        if (authority == Lobby.selfPeerID)
        {
            if (desiredState != null && desiredState.Length > 0)
            {
                BasicSyncMessage desiredStateData = GMPObject.serializer.Deserialize<BasicSyncMessage>(desiredState);
                // GD.Print(desiredStateData.pos);
                //Position = desiredStateData.pos;
                Call("teleport", [new Transform3D(Basis.FromEuler(desiredStateData.rot), desiredStateData.pos)]);
            }

        }
        else
        {
            // Set("enabled", false);
            // body_type = 1;
            // Set("body_type", 1);

        }


    }

    public virtual void ApplyStateUpdate(byte[] update)
    {
        desiredState = update;
        //BasicSyncMessage desiredStateData = GMPObject.serializer.Deserialize<BasicSyncMessage>(desiredState);

        //  GD.Print(desiredStateData.pos);


    }

    public virtual byte[] GenerateStateUpdate()
    {
        BasicSyncMessage msg = new BasicSyncMessage
        {
            pos = this.Position,
            rot = this.Rotation
        };
        return GMPObject.serializer.Serialize(msg);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (authority != Lobby.selfPeerID)
        {
            if (desiredState != null && desiredState.Length > 0)
            {
                BasicSyncMessage desiredStateData = GMPObject.serializer.Deserialize<BasicSyncMessage>(desiredState);
                //Call("teleport", [new Transform3D(Basis.FromEuler(desiredStateData.rot), desiredStateData.pos)]);
                // GD.Print(desiredStateData.pos);
                this.Position = this.Position.Lerp(desiredStateData.pos, (float)(10*delta));
                this.Rotation = this.Rotation.Lerp(desiredStateData.rot, (float)(10*delta));
            }
        }
        else
        {
          
        }
    }


    public override void _Ready()
    {

        if (pauseable)
        {
            (this as Node).ProcessMode = ProcessModeEnum.Pausable;
        }
        else
        {
            (this as Node).ProcessMode = ProcessModeEnum.Always;
        }
    }
}

