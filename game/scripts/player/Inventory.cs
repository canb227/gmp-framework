using Godot;
using PolyType;
using System;

[GenerateShape]
public partial struct InventorySlot
{
    public string itemID;
    public int Count;

    public bool IsEmpty => itemID == null || Count <= 0;
}

[GenerateShapeFor<InventorySlot[]>]
public partial class Witness { }

public class Inventory
{
    public const int TotalSlots = 35;
    public const int HotbarSlots = 10;

    public InventorySlot[] slots = new InventorySlot[TotalSlots];
    public int ActiveHotbarSlot { get; set; } = 0;

    public event Action InventoryChanged;

    public InventorySlot GetSlot(int index)
    {
        if (index < 0 || index >= TotalSlots) return default;
        return slots[index];
    }

    public void InventoryUpdated()
    {
        InventoryChanged?.Invoke();
    }

    public void SetSlot(int index, string itemID, int count)
    {
        if (index < 0 || index >= TotalSlots) return;
        slots[index].itemID = itemID;
        slots[index].Count = count;
        InventoryChanged?.Invoke();
    }

    public void ClearSlot(int index)
    {
        if (index < 0 || index >= TotalSlots) return;
        slots[index].itemID = null;
        slots[index].Count = 0;
        InventoryChanged?.Invoke();
    }

    public void SwapSlots(int a, int b)
    {
        if (a < 0 || a >= TotalSlots || b < 0 || b >= TotalSlots) return;
        if (a == b) return;
        (slots[a], slots[b]) = (slots[b], slots[a]);
        InventoryChanged?.Invoke();
    }

    public int MergeSlots(int source, int target)
    {
        if (source < 0 || source >= TotalSlots || target < 0 || target >= TotalSlots) return 0;
        if (slots[source].IsEmpty || slots[target].IsEmpty) return 0;
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

    public int AddItem(string itemID, int count = 1)
    {
        ItemInfo item = ItemInfo.Fetch(itemID);
        GD.Print($"loaded {item.itemID} by searching for {itemID}", "Inventory");
        if (item == null || count <= 0) return count;
        int remaining = count;

        if (item.maxStackSize > 1)
        {
            for (int i = 0; i < TotalSlots && remaining > 0; i++)
            {
                if (!slots[i].IsEmpty && slots[i].itemID == item.itemID)
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
            if (slots[i].IsEmpty)
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

    public int RemoveFromSlot(int index, int count = 1)
    {
        if (index < 0 || index >= TotalSlots || slots[index].IsEmpty) return 0;
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

    /// <summary>True if at least one more of <paramref name="itemID"/> fits (a free slot or a stack with space).</summary>
    public bool HasRoomFor(string itemID)
    {
        ItemInfo item = ItemInfo.Fetch(itemID);
        if (item == null) return false;
        foreach (InventorySlot slot in slots)
        {
            if (slot.IsEmpty || (slot.itemID == itemID && slot.Count < item.maxStackSize))
                return true;
        }
        return false;
    }

    public string GetEquippedItem()
    {
        if (ActiveHotbarSlot == -1 ) return null;
        var slot = slots[ActiveHotbarSlot];
        return slot.IsEmpty ? null : slot.itemID;
    }
}
