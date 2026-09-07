using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class ExmapleGMPOSingletonReferencer : Node, GMPObject
{
    public int priority { get; set; }
    public bool pauseable { get; set; }
    public ulong id { get; set; }
    public ulong authority { get; set; }
    public ulong owner { get; set; }
    public int priorityAccumulator { get; set; }
    public byte[] desiredState { get; set; }

    public override void _Ready()
    {
        ExampleGMPOSingleton.ExampleSingletonOnlineEvent += ExampleGMPOSingleton_ExampleSingletonOnlineEvent;
    }

    private void ExampleGMPOSingleton_ExampleSingletonOnlineEvent(ulong id)
    {
        Logging.Log("My singleton buddy is ready!", "SingletonExample");
    }

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