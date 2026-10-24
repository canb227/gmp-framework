using Godot;
using PolyType;
using System;

/// <summary>One inventory slot: an item id and how many (empty when the id is null or the count 0).</summary>
[GenerateShape]
public partial struct InventorySlot
{
    public string itemID;
    public int Count;

    /// <summary>Nothing in it.</summary>
    public bool IsEmpty => itemID == null || Count <= 0;
}

[GenerateShapeFor<InventorySlot[]>]
public partial class Witness { }

/// <summary>
/// A player's items (<see cref="FactoryPlayer.inventory"/>): <see cref="TotalSlots"/> slots, the first
/// <see cref="HotbarSlots"/> of them the hotbar. Plain data on each peer; the player syncs its own changes.
/// Every method that changes it raises <see cref="InventoryChanged"/>; out-of-range slot indices are ignored.
/// The first slot always holds the player's gravity gun (<see cref="EnsureGun"/>): nothing moves, merges into or
/// empties it, and nothing else is added to it.
/// </summary>
public class Inventory
{
    public const int TotalSlots = 35;
    /// <summary>Slots 0 to HotbarSlots - 1 are the hotbar.</summary>
    public const int HotbarSlots = 10;

    public InventorySlot[] slots = new InventorySlot[TotalSlots];
    /// <summary>The slot that always holds the gravity gun, and its item id.</summary>
    public const int GunSlot = 0;
    public const string GunItemID = "gravity_gun";

    /// <summary>The hotbar slot whose item is in hand; -1 for none.</summary>
    public int ActiveHotbarSlot { get; set; } = 0;

    /// <summary>Something in <see cref="slots"/> changed.</summary>
    public event Action InventoryChanged;

    /// <summary>The slot at <paramref name="index"/>; an empty one if out of range.</summary>
    public InventorySlot GetSlot(int index)
    {
        if (index < 0 || index >= TotalSlots) return default;
        return slots[index];
    }

    /// <summary>True for the gravity gun's slot, which nothing may change.</summary>
    public static bool IsPinned(int index)
    {
        return index == GunSlot;
    }

    /// <summary>
    /// Puts the gravity gun in its slot. Anything else found there (an older save) moves to a free slot, or is lost if
    /// none is free. Doesn't raise <see cref="InventoryChanged"/>: call it before the inventory is synced or shown.
    /// </summary>
    public void EnsureGun()
    {
        InventorySlot old = slots[GunSlot];
        slots[GunSlot] = new InventorySlot { itemID = GunItemID, Count = 1 };
        if (old.IsEmpty || old.itemID == GunItemID) return;
        for (int i = 0; i < TotalSlots; i++)
        {
            if (slots[i].IsEmpty)
            {
                slots[i] = old;
                return;
            }
        }
    }

    /// <summary>Raises <see cref="InventoryChanged"/>, for code that changed <see cref="slots"/> directly.</summary>
    public void InventoryUpdated()
    {
        InventoryChanged?.Invoke();
    }

    /// <summary>Empties the slot.</summary>
    public void ClearSlot(int index)
    {
        if (index < 0 || index >= TotalSlots || IsPinned(index)) return;
        slots[index].itemID = null;
        slots[index].Count = 0;
        InventoryChanged?.Invoke();
    }

    /// <summary>Swaps the contents of two slots.</summary>
    public void SwapSlots(int a, int b)
    {
        if (a < 0 || a >= TotalSlots || b < 0 || b >= TotalSlots) return;
        if (a == b || IsPinned(a) || IsPinned(b)) return;
        (slots[a], slots[b]) = (slots[b], slots[a]);
        InventoryChanged?.Invoke();
    }

    /// <summary>
    /// Moves as much of <paramref name="source"/>'s stack onto <paramref name="target"/>'s as fits, if both hold the same
    /// item. Returns what is left in <paramref name="source"/> (0 if nothing was moved because they can't merge).
    /// </summary>
    public int MergeSlots(int source, int target)
    {
        if (source < 0 || source >= TotalSlots || target < 0 || target >= TotalSlots) return 0;
        if (slots[source].IsEmpty || slots[target].IsEmpty || IsPinned(source) || IsPinned(target)) return 0;
        if (slots[source].itemID != slots[target].itemID) return 0;

        int maxStack = ItemInfo.Fetch(slots[target].itemID).maxStackSize;
        int space = maxStack - slots[target].Count;
        int toMove = Math.Min(slots[source].Count, space);

        slots[target].Count += toMove;
        slots[source].Count -= toMove;
        if (slots[source].Count <= 0)
        {
            slots[source].itemID = null;
            slots[source].Count = 0;
        }
        InventoryChanged?.Invoke();
        return slots[source].Count;
    }

    /// <summary>Adds <paramref name="count"/> of an item, topping up its stacks first, then filling empty slots. Returns how many didn't fit (all of them for an item with no definition).</summary>
    public int AddItem(string itemID, int count = 1)
    {
        ItemInfo item = ItemInfo.Fetch(itemID);
        if (item == null || count <= 0) return count;
        int remaining = count;

        if (item.maxStackSize > 1)
        {
            for (int i = 0; i < TotalSlots && remaining > 0; i++)
            {
                if (!slots[i].IsEmpty && slots[i].itemID == item.itemID && !IsPinned(i))
                {
                    int space = item.maxStackSize - slots[i].Count;
                    int toAdd = Math.Min(remaining, space);
                    slots[i].Count += toAdd;
                    remaining -= toAdd;
                }
            }
        }

        for (int i = 0; i < TotalSlots && remaining > 0; i++)
        {
            if (slots[i].IsEmpty && !IsPinned(i))
            {
                int toAdd = Math.Min(remaining, item.maxStackSize);
                slots[i].itemID = itemID;
                slots[i].Count = toAdd;
                remaining -= toAdd;
            }
        }

        InventoryChanged?.Invoke();
        return remaining;
    }

    /// <summary>Takes up to <paramref name="count"/> from the slot; returns how many were taken.</summary>
    public int RemoveFromSlot(int index, int count = 1)
    {
        if (index < 0 || index >= TotalSlots || slots[index].IsEmpty || IsPinned(index)) return 0;
        int toRemove = Math.Min(count, slots[index].Count);
        slots[index].Count -= toRemove;
        if (slots[index].Count <= 0)
        {
            slots[index].itemID = null;
            slots[index].Count = 0;
        }
        InventoryChanged?.Invoke();
        return toRemove;
    }

    /// <summary>How many more of <paramref name="itemID"/> fit: space left in its stacks plus empty slots.</summary>
    public int RoomFor(string itemID)
    {
        ItemInfo item = ItemInfo.Fetch(itemID);
        if (item == null) return 0;
        int room = 0;
        for (int i = 0; i < TotalSlots; i++)
        {
            InventorySlot slot = slots[i];
            if (IsPinned(i))
                continue;
            if (slot.IsEmpty)
                room += item.maxStackSize;
            else if (slot.itemID == itemID)
                room += Math.Max(0, item.maxStackSize - slot.Count);
        }
        return room;
    }

    /// <summary>True if at least one more of <paramref name="itemID"/> fits (a free slot or a stack with space).</summary>
    public bool HasRoomFor(string itemID)
    {
        return RoomFor(itemID) > 0;
    }

    /// <summary>The item id in the active hotbar slot, or null if it's empty (or none is active).</summary>
    public string GetEquippedItem()
    {
        if (ActiveHotbarSlot == -1) return null;
        var slot = slots[ActiveHotbarSlot];
        return slot.IsEmpty ? null : slot.itemID;
    }
}
