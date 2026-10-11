using Godot;
using PolyType;

/// <summary>What a save keeps of a <see cref="DefaultDroppedBox"/> beyond its transform.</summary>
[GenerateShape]
public partial record struct DroppedBoxSave
{
    public string itemID;
}

public partial class DefaultDroppedBox : PhysicalFactoryItem
{
    [Export]
    public string labelName = null;

    [Export]
    public CompressedTexture2D icon = null;

    public void boxInit(string itemID)
    {
        RPCManager.RPC(this, nameof(_boxInit), [itemID]);
    }
    [RPC]
    private void _boxInit(string itemID)
    {
        ApplyItem(itemID);
    }

    // A box placed by hand in a level (itemID set in the scene) labels itself; every peer loads the same value.
    public override void AfterInit()
    {
        base.AfterInit();
        if (labelName == null && !string.IsNullOrEmpty(itemID))
        {
            ApplyItem(itemID);
        }
    }

    // The item a box stands for is set by RPC after it spawns, so a save keeps it.
    public override byte[] SaveState()
    {
        return string.IsNullOrEmpty(itemID) ? null : GMPObject.serializer.Serialize(new DroppedBoxSave { itemID = itemID });
    }

    public override void LoadState(byte[] state)
    {
        ApplyItem(GMPObject.serializer.Deserialize<DroppedBoxSave>(state).itemID);
    }

    void ApplyItem(string itemID)
    {
        this.itemID = itemID;
        ItemInfo item = ItemInfo.Fetch(itemID);
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

