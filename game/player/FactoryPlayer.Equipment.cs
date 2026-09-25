using Godot;
using System;

/// <summary>
/// FactoryPlayer: hotbar selection, the in-hand item visual, and dropping items.
/// Hotbar scrolling is routed here from <c>HandleScrollWheel</c>.
/// </summary>
public partial class FactoryPlayer
{
    const string DefaultHeldScene = "res://game/items/held/DefaultHeldBox.tscn";

    HeldItem currentInHandItem;

    void HandleEquipmentInput(InputEvent @event)
    {
        // Drop item (Q) — drop 1 from active hotbar slot
        if (@event.IsActionPressed("drop"))
            DropFromActiveSlot(1);

        SelectSlotFromNumberKeys(@event);
    }

    /// <summary>Replaces the in-hand visual with the currently equipped item's (or clears it). Runs on every peer.</summary>
    public void UpdateEquippedItem()
    {
        if (currentInHandItem != null)
        {
            currentInHandItem.QueueFree();
            currentInHandItem = null;
        }

        string equippedID = inventory.GetEquippedItem();
        if (equippedID == null) return;

        ItemInfo equipped = ItemInfo.Fetch(equippedID);
        if (equipped?.inHandScene != null)
        {
            currentInHandItem = equipped.inHandScene.Instantiate<HeldItem>();
        }
        else if (equipped != null)
        {
            currentInHandItem = ResourceLoader.Load<PackedScene>(DefaultHeldScene).Instantiate<HeldItem>();
            (currentInHandItem as DefaultHeldBox).boxInit(equipped.itemID);
        }
        itemHolder.AddChild(currentInHandItem);
        if (currentInHandItem is HeldItem tool)
        {
            tool.Equip(equipped, this);
        }
    }

    /// <summary>Spawns up to <paramref name="count"/> of the active hotbar item in front of the player.</summary>
    public void DropFromActiveSlot(int count)
    {
        var slot = inventory.GetSlot(inventory.ActiveHotbarSlot);
        if (slot.IsEmpty) return;

        int toDrop = Math.Min(count, slot.Count);
        Vector3 dropPos = camera.GlobalPosition + -camera.GlobalTransform.Basis.Z * 2f;
        Vector3 dropRot = GlobalRotation;

        for (int i = 0; i < toDrop; i++)
        {
            Vector3 offset = new Vector3(
                (float)(Random.Shared.NextDouble() - 0.5) * 0.5f,
                0,
                (float)(Random.Shared.NextDouble() - 0.5) * 0.5f
            );
            ItemInfo.SpawnInWorld(slot.itemID, dropPos + offset, dropRot);
        }

        inventory.RemoveFromSlot(inventory.ActiveHotbarSlot, toDrop);
        UpdateEquippedItem();
    }

    /// <summary>
    /// Sends this (local) player's inventory contents to every other peer. Runs automatically on every
    /// inventory change (subscribed in AfterInit); the active hotbar slot travels in <see cref="PlayerSync"/>.
    /// </summary>
    public void SyncInventory()
    {
        RPCManager.RPC(this, nameof(_SyncInventory), [inventory.slots]);
    }

    [RPC(requireAuthority = true)]
    private void _SyncInventory(InventorySlot[] slots)
    {
        if (!isLocal)
        {
            inventory.slots = slots;
            inventory.InventoryUpdated();
            UpdateEquippedItem();
        }
    }

    /// <summary>Moves the active hotbar slot by <paramref name="step"/> (+1 / -1), wrapping around.</summary>
    void CycleHotbar(int step)
    {
        if (inventory.ActiveHotbarSlot == -1 || inventory.ActiveHotbarSlot > Inventory.HotbarSlots)
        {
            inventory.ActiveHotbarSlot = 0;
        }
        inventory.ActiveHotbarSlot = (inventory.ActiveHotbarSlot + step + Inventory.HotbarSlots) % Inventory.HotbarSlots;
        UpdateEquippedItem();
    }

    void SelectSlotFromNumberKeys(InputEvent @event)
    {
        for (int i = 0; i <= 9; i++)
        {
            if (@event.IsActionPressed($"slot{i}"))
            {
                // Pressing the active slot's key again unequips.
                inventory.ActiveHotbarSlot = inventory.ActiveHotbarSlot == i ? -1 : i;
                UpdateEquippedItem();
                return;
            }
        }
    }
}
