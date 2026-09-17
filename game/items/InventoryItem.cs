using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GlobalClass]
public partial class InventoryItem : Resource
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

    public InventoryItem()
    {

    }

}