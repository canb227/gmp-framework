using Godot;

public partial class DefaultHeldBox : HeldItem
{
    [Export]
    public string labelName = null;

    [Export]
    public CompressedTexture2D icon = null;

    [Export]
    public string itemID = null;

    public void boxInit(string itemID)
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

