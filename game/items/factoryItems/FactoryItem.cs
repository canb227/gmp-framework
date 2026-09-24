using Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GlobalClass]
[GenerateShape]
public partial class FactoryItem : Resource
{
    [Export]
    public string itemID;

    [Export]
    public PackedScene droppedScene;

    [Export]
    public string displayName;

    [Export]
    public string description;

    [Export]
    public CompressedTexture2D icon;

    [Export]
    public PackedScene inHandScene;

    [Export]
    public int maxStackSize = 1;



    public FactoryItem()
    {

    }


    public static FactoryItem Fetch (string itemID)
    {
        if (itemID == null)
            return null;
        return ResourceLoader.Load<FactoryItem>("res://game/items/factoryItems/"+itemID+".tres");
    }

}