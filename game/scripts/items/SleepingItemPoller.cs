using Godot;
using System.Collections.Generic;

/// <summary>
/// Checks this peer's sleeping <see cref="PhysicalFactoryItem"/>s for waking, about every <see cref="WakeCheckInterval"/>
/// seconds each, spread evenly over the physics ticks. Box3D signals falling asleep but not waking, so waking has
/// to be polled. Doing it from one node instead of each item's own <c>_PhysicsProcess</c> means resting items cost
/// nothing per tick beyond their share of the polling. Created on first use as a child of <see cref="GameWorld.b3droot"/>.
/// </summary>
public partial class SleepingItemPoller : Node
{
    /// <summary>How often (s) each sleeping item is checked.</summary>
    const double WakeCheckInterval = 0.2;

    static SleepingItemPoller instance;

    /// <summary>The Box3D world this poller was made for (it doesn't outlive its world).</summary>
    Node world;
    readonly List<PhysicalFactoryItem> items = new();
    readonly Dictionary<PhysicalFactoryItem, int> slots = new();
    /// <summary>Where the round-robin left off, and the fraction of an item carried over between ticks.</summary>
    int next;
    double carry;

    /// <summary>Starts polling <paramref name="item"/> (it has just fallen asleep).</summary>
    public static void Add(PhysicalFactoryItem item)
    {
        if (GameWorld.b3droot == null) return;
        if (instance == null || !IsInstanceValid(instance) || instance.world != GameWorld.b3droot)
        {
            instance = new SleepingItemPoller { Name = "SleepingItemPoller", world = GameWorld.b3droot };
            // Items fall asleep from AfterInit, which can run while the level is still adding its children.
            GameWorld.b3droot.CallDeferred(Node.MethodName.AddChild, instance);
        }
        if (instance.slots.ContainsKey(item)) return;
        instance.slots[item] = instance.items.Count;
        instance.items.Add(item);
    }

    /// <summary>Stops polling <paramref name="item"/>. Safe to call for items that aren't being polled.</summary>
    public static void Remove(PhysicalFactoryItem item)
    {
        if (instance == null || !IsInstanceValid(instance) || !instance.slots.Remove(item, out int slot)) return;
        // Swap-remove: the last item takes the freed slot.
        int last = instance.items.Count - 1;
        if (slot != last)
        {
            instance.items[slot] = instance.items[last];
            instance.slots[instance.items[slot]] = slot;
        }
        instance.items.RemoveAt(last);
    }

    public override void _PhysicsProcess(double delta)
    {
        // Enough checks this tick that every item comes round once per interval.
        carry += items.Count * delta / WakeCheckInterval;
        int checks = Mathf.Min((int)carry, items.Count);
        carry -= checks;
        for (int n = 0; n < checks && items.Count > 0; n++)
        {
            if (next >= items.Count) next = 0;
            PhysicalFactoryItem item = items[next];
            // An item that woke removes itself, and the last item moves into its slot: check that one next.
            if (item.CheckStillAsleep()) next++;
        }
    }

    public override void _ExitTree()
    {
        if (instance == this) instance = null;
    }
}
