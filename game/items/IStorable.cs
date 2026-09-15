using Godot;

public interface IStorable
{
    [Export]
    public CompressedTexture2D icon { get; set; }

    public ulong inventoryGMPOOwner {  get; set; }

    public void OnDrop()
    {

    }

}