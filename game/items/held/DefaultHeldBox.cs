using Godot;
using PolyType;
using System;

public partial class DefaultHeldBox : GMPONode3D
{
    [Export]
    public string labelName = null;

    [Export]
    public CompressedTexture2D icon = null;

    [Export]
    public string itemID = null;


    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
   
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }

    public void boxInit(string itemID)
    {
        this.itemID = itemID;
        FactoryItem item = FactoryItem.Fetch(itemID);
        this.labelName = item.displayName;
        this.icon = item.icon;
        GetNode<Label3D>("%L1").Text = labelName;
        GetNode<Label3D>("%L2").Text = labelName;
        GetNode<Label3D>("%L3").Text = labelName;
        GetNode<Label3D>("%L4").Text = labelName;

        StandardMaterial3D mat = new();
        mat.AlbedoTexture = icon;

        GetNode<MeshInstance3D>("%M1").SetSurfaceOverrideMaterial(0, mat);
        GetNode<MeshInstance3D>("%M2").SetSurfaceOverrideMaterial(0, mat);
        GetNode<MeshInstance3D>("%M3").SetSurfaceOverrideMaterial(0, mat);
        GetNode<MeshInstance3D>("%M4").SetSurfaceOverrideMaterial(0, mat);
    }
}

