using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public partial class GMPOBox3DWorld : Node3D, GMPObject
{
    public int priority { get; set; }
    public bool pauseable { get; set; }
    public ulong id { get; set; }
    public ulong authority { get; set; }
    public ulong owner { get; set; }
    public int priorityAccumulator { get; set; }
    public byte[] desiredState { get; set; }

    public void AfterInit()
    {
        
    }

    public void ApplyStateUpdate(byte[] update)
    {
       
    }

    public byte[] GenerateStateUpdate()
    {
        return null;
    }
}

