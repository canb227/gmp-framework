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
public abstract partial class GMPOCharacterBody3D : CharacterBody3D, GMPObject
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

    public override void _Ready()
    {
       
    }

    public virtual void ApplyStateUpdate(byte[] update)
    {
        desiredState = update;
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
                this.Position = this.Position.Lerp(desiredStateData.pos, (float)(.7f));
                this.Rotation = this.Rotation.Lerp(desiredStateData.rot, (float)(.7f));
            }
        }
    }

    public abstract void AfterInit();
}

