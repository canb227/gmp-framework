using Godot;
using System;

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

    /// <summary>
    /// Fired just before this void despawns an item, with the item's id (e.g. to count goal turn-ins).
    /// Authority only, like the despawn itself: other peers never see it, so sync any results yourself.
    /// </summary>
    public event Action<string> ItemDespawned;

    /// <summary>
    /// <see cref="ItemDespawned"/> for every void at once (the void, then the item id), so a goal tracker can
    /// subscribe once instead of finding each void. Authority only; static, so unsubscribe when done.
    /// </summary>
    public static event Action<ItemVoid, string> AnyItemDespawned;

    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic || trigger == null) return;
        foreach (PhysicalFactoryItem item in ItemsInTrigger(trigger))
        {
            // Raised before the despawn so handlers can still read the item.
            ItemDespawned?.Invoke(item.itemID);
            AnyItemDespawned?.Invoke(this, item.itemID);
            // Despawn runs synchronously here, so the item leaves syncedObjs and isn't seen again.
            GameWorld.DespawnObject(item.id);
            despawnedCount++;
        }
    }
}
