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
        string path = "res://game/items/definitions/" + itemID + ".tres";
        // Some props (e.g. spawner test items) have no definition; don't spam load errors every frame.
        return ResourceLoader.Exists(path) ? ResourceLoader.Load<FactoryItem>(path) : null;
    }

}