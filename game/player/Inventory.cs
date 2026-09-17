using System;

public struct InventorySlot
{
    public InventoryItem Item;
    public int Count;

    public bool IsEmpty => Item == null || Count <= 0;
}

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

    public void SetSlot(int index, InventoryItem item, int count)
    {
        if (index < 0 || index >= TotalSlots) return;
        slots[index].Item = item;
        slots[index].Count = count;
        InventoryChanged?.Invoke();
    }

    public void ClearSlot(int index)
    {
        if (index < 0 || index >= TotalSlots) return;
        slots[index].Item = null;
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
        if (slots[source].Item.itemID != slots[target].Item.itemID) return 0;

        int maxStack = slots[target].Item.maxStackSize;
        int space = maxStack - slots[target].Count;
        int toMove = Math.Min(slots[source].Count, space);

        slots[target].Count += toMove;
        slots[source].Count -= toMove;
        if (slots[source].Count <= 0)
        {
            slots[source].Item = null;
            slots[source].Count = 0;
        }
        InventoryChanged?.Invoke();
        return slots[source].Count;
    }

    public int AddItem(InventoryItem item, int count = 1)
    {
        if (item == null || count <= 0) return count;
        int remaining = count;

        if (item.maxStackSize > 1)
        {
            for (int i = 0; i < TotalSlots && remaining > 0; i++)
            {
                if (!slots[i].IsEmpty && slots[i].Item.itemID == item.itemID)
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
                slots[i].Item = item;
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
            slots[index].Item = null;
            slots[index].Count = 0;
        }
        InventoryChanged?.Invoke();
        return toRemove;
    }

    public InventoryItem GetEquippedItem()
    {
        var slot = slots[ActiveHotbarSlot];
        return slot.IsEmpty ? null : slot.Item;
    }
}
