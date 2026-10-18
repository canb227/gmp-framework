using Godot;

/// <summary>
/// Where a loose item starts in a level scene. Loose items aren't nodes, so a level marks them with these instead: when
/// the level is spawned, the host spawns the item here (<see cref="ItemInfo.SpawnInWorld"/>) and every peer drops the
/// marker.
/// </summary>
public partial class ItemPlacement : Node3D
{
    [Export]
    public ItemInfo item;

    public override void _Ready()
    {
        // Not while a save is restored: the save brings back the items as they were, this one included if it's left.
        if (Lobby.isHost && item != null && !GameWorld.frozen)
        {
            ItemInfo.SpawnInWorld(item.itemID, GlobalPosition, GlobalRotation);
        }
        QueueFree();
    }
}
