using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

[GlobalClass]
public partial class InventoryItem : Resource
{
    public string itemID;
    public NodePath droppedScenePath;
    public string displayName;
    public string description;
    public CompressedTexture2D icon;
    public NodePath inHandScenePath;

    public InventoryItem()
    {

    }
    
}