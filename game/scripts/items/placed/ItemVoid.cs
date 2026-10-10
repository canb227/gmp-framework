using Godot;
using System;

/// <summary>
/// An open-topped bin that destroys every item that falls into it. Its input port's volume (a sensor child body,
/// <see cref="Structure.ItemsAtInputs"/>) fills the inside; the bin's authority despawns each synced
/// <see cref="PhysicalFactoryItem"/> it overlaps, for every peer, as <see cref="DespawnReason.Voided"/>. What that
/// means (a turn-in) is up to the item. Players and other objects are left alone.
/// </summary>
public partial class ItemVoid : Structure
{
    /// <summary>Items this void has despawned (authority only; used by the headless multiplayer test).</summary>
    public int despawnedCount { get; private set; }

    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic) return;
        foreach (PhysicalFactoryItem item in ItemsAtInputs())
        {
            // Despawn runs synchronously here, so the item leaves syncedObjs and isn't seen again.
            GameWorld.DespawnObject(item.id, DespawnReason.Voided);
            despawnedCount++;
        }
    }
}
