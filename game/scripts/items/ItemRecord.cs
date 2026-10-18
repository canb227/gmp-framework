using Godot;
using System.Collections.Generic;

/// <summary>
/// One loose item in the world, as the game knows it: which item it is and what it can do. Its body, sync and drawing
/// are in the physics world, by the same <see cref="id"/> (<see cref="PhysicsWorld"/>). Kept by <see cref="ItemWorld"/>
/// on every peer.
/// </summary>
public class ItemRecord
{
    public readonly ulong id;
    public readonly string itemID;
    /// <summary>The item's definition, or null for an item without one (it shows as a labelled box).</summary>
    public readonly ItemInfo info;
    /// <summary>Its own copy of the definition's tags: a reaction can change them (call <see cref="ItemWorld.TagsChanged"/>).</summary>
    public readonly List<ItemTags> tags = new();
    /// <summary>Set once a pickup has been granted, so later simultaneous requests lose.</summary>
    public bool taken;

    public ItemRecord(ulong id, string itemID, ItemInfo info)
    {
        this.id = id;
        this.itemID = itemID;
        this.info = info;
        if (info != null)
        {
            tags.AddRange(info.tags);
        }
    }

    // An item without a definition (a test prop, or one only the inventory knows) can still be grabbed and picked up.

    public bool canBePickTarget => info == null || info.canBePickTarget;
    public bool canBeGrabbed => info == null || info.canBeGrabbed;
    public bool canBePickedUp => info == null || info.canBePickedUp;

    /// <summary>What it is worth when turned in to the void, by currency (empty: nothing).</summary>
    public Godot.Collections.Dictionary<Currency, int> currencyValue
    {
        get
        {
            if (info == null)
            {
                return new();
            }
            return info.currencyValue;
        }
    }

    /// <summary>Shown under the crosshair.</summary>
    public string hoverName
    {
        get
        {
            if (info != null && !string.IsNullOrEmpty(info.displayName))
            {
                return info.displayName;
            }
            return itemID;
        }
    }

    public string hoverText
    {
        get
        {
            if (canBePickedUp)
            {
                return "Press F to pick up!";
            }
            return "";
        }
    }

    /// <summary>Where it is now (asks the physics world).</summary>
    public Vector3 position => PhysicsWorld.ItemPosition(id);
}
