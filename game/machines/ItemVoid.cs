using Godot;

/// <summary>
/// An open-topped bin that destroys every item that falls into it. Its trigger (a sensor child body
/// named by <see cref="trigger"/>) fills the inside; the bin's authority despawns each synced
/// <see cref="PhysicalFactoryItem"/> the trigger overlaps, for every peer. Players and other objects are
/// left alone.
/// </summary>
public partial class ItemVoid : Structure
{
    [Export] public Node3D trigger;

    /// <summary>Items this void has despawned (authority only; used by the headless multiplayer test).</summary>
    public int despawnedCount { get; private set; }

    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic || trigger == null) return;
        foreach (PhysicalFactoryItem item in ItemsInTrigger(trigger))
        {
            // Despawn runs synchronously here, so the item leaves syncedObjs and isn't seen again.
            GameWorld.DespawnObject(item.id);
            despawnedCount++;
        }
    }
}
