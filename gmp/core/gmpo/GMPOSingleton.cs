using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public abstract partial class GMPOSingleton : Node, GMPObject
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

    public abstract void AfterInit();

    public abstract void ApplyStateUpdate(byte[] update);

    public abstract byte[] GenerateStateUpdate();

    public void RPC(string method, object[] args)
    {
        RPCManager.RPC(this, method, args);
    }
}

