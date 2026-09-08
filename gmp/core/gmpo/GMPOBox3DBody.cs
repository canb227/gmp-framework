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


public partial class GMPOBox3DBody : Node3D, GMPObject
{
    public int priority { get; set; }
    public bool pauseable { get; set; }
    public ulong id { get; set; }
    public ulong authority { get; set; }
    public ulong owner { get; set; }
    public int priorityAccumulator { get; set; }
    public byte[] desiredState { get; set; }



    public BodyTypeEnum GetBodyType()
    {
        return (BodyTypeEnum)Get("body_type").AsInt32();
    }

    public void SetBodyType(BodyTypeEnum value)
    {
        Set("body_type", (int)value);
    }

    public void AfterInit()
    {
        if (authority == Lobby.selfPeerID)
        {
            BasicSyncMessage desiredStateData = GMPObject.serializer.Deserialize<BasicSyncMessage>(desiredState);
           // GD.Print(desiredStateData.pos);
            //Position = desiredStateData.pos;
            Call("teleport", [new Transform3D(Basis.FromEuler(desiredStateData.rot), desiredStateData.pos)]);
        }
        else
        {
            // Set("enabled", false);
            // body_type = 1;
            // Set("body_type", 1);
            SetBodyType(BodyTypeEnum.Kinematic);
        }


    }

    public void ApplyStateUpdate(byte[] update)
    {
        desiredState = update;
        //BasicSyncMessage desiredStateData = GMPObject.serializer.Deserialize<BasicSyncMessage>(desiredState);

        //  GD.Print(desiredStateData.pos);


    }

    public byte[] GenerateStateUpdate()
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
                this.Position = this.Position.Lerp(desiredStateData.pos, (float)(.1f));
                this.Rotation = this.Rotation.Lerp(desiredStateData.rot, (float)(.1f));
            }
        }
        else
        {
          
        }
    }

    public void ApplyCentralForce(Vector3 force)
    {

        Call("apply_central_force", [force]);
    }
    public override void _Ready()
    {

        
    }
}

