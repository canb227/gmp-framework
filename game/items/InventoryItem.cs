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
    public MeshInstance3D inHandMesh;

    public InventoryItem()
    {

    }

    public void OnDrop()
    {
        if (droppedScenePath == null)
        {
            //make a generic item box and spawn it
        }
        else
        {
            //spawn the linked scene
        }
    }

    public void OnEquip()
    {

    }

    
}