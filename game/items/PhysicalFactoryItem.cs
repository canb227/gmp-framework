using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class PhysicalFactoryItem : GMPOBox3DBody
{

    [Export]
    public string itemID;

    [Export]
    public bool canBePickTarget;

    [Export]
    public bool canBeGrabbed;

    [Export]
    public bool canBePickedUp;

    [Export]
    public bool canBeInteractedWith;
    [Export]
    public Godot.Collections.Array<ItemTags> tags;

    public override void _Ready()
    {
        base._Ready();
        GD.Print(GetType());
    }
}

