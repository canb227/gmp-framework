using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


[GlobalClass]
public partial class ExampleGMPOSingleton : GMPOSingleton
{
    public static ExampleGMPOSingleton instance;

    public delegate void ExampleSingletonOnline(ulong id);
    public static event ExampleSingletonOnline ExampleSingletonOnlineEvent;

    public override void _Ready()
    {
        instance = this; //sometimes you want to treat this singleton like a static global but also like a node, this instance is a ref to the node (ExampleGMPOSingleton.instance is globally accessible)

    }

    public override void ApplyStateUpdate(byte[] update)
    {
        throw new NotImplementedException();
    }

    public override byte[] GenerateStateUpdate()
    {
        throw new NotImplementedException();
    }

    public override void AfterInit()
    {
        ExampleSingletonOnlineEvent?.Invoke(this.id);
    }
}

