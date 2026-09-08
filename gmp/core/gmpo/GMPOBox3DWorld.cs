using Godot;
using Godot.Collections;
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

    public Godot.Collections.Dictionary<string,Variant> Raycast(Vector3 from, Vector3 to)
    {
        return Call("raycast", [from, to]).AsGodotDictionary<string, Variant>();
    }

    public Godot.Collections.Array<Node> OverlapSphere(Vector3 center, float radius, int collisionMask = -1, int collisionLayer = -1)
    {
        return Call("overlap_sphere", [center, radius, collisionMask, collisionLayer]).AsGodotArray<Node>();
    }

}

