using Godot;
using Nerdbank.MessagePack;
using Nerdbank.MessagePack.Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Godot.Node;

[GenerateShape]
public partial record struct GMPOInitData : IEquatable<GMPOInitData>
{
    public ulong id;
    public ulong authority;
    public ulong owner;
    public int priority;
    public bool pauseable;  

    public GMPOInitData(ulong id, ulong authority, ulong owner, int priority, bool pauseable)
    {
        this.id = id;
        this.authority = authority;
        this.owner = owner;
        this.priority = priority;
        this.pauseable = pauseable;
    }
}


[GenerateShape]
public partial interface GMPObject
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


    public static MessagePackSerializer serializer = new MessagePackSerializer().WithGodotConverters();

    public byte[] GenerateStateUpdate();
    public void ApplyStateUpdate(byte[] update);

    public void _Ready()
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

    public virtual void Init(GMPOInitData init, byte[] initState = null)
    {
        this.id = init.id;
        this.authority = init.authority;
        if (init.priority!=0)
        {
            this.priority = init.priority;
        }

        this.pauseable = init.pauseable;
        this.owner = init.owner;
        
        if (initState != null)
        {
            ApplyStateUpdate(initState);
        }
        AfterInit();
    }

    protected void AfterInit();

}

