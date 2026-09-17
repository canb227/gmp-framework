using Godot;
using System;


//claude wrote this so its kinda wack
public partial class InventoryUI : Control
{
    FactoryPlayer player;
    Inventory inventory;

    PanelContainer[] slotPanels = new PanelContainer[Inventory.TotalSlots];
    TextureRect[] slotIcons = new TextureRect[Inventory.TotalSlots];
    Label[] slotCounts = new Label[Inventory.TotalSlots];

    StyleBoxFlat normalStyle;
    StyleBoxFlat activeStyle;

    public override void _Ready()
    {
        player = GetParent<FactoryPlayer>();
        inventory = player.inventory;

        normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        normalStyle.SetCornerRadiusAll(4);

        activeStyle = new StyleBoxFlat();
        activeStyle.BgColor = new Color(0.3f, 0.3f, 0.1f, 0.9f);
        activeStyle.BorderColor = new Color(1f, 0.85f, 0.2f, 1f);
        activeStyle.SetBorderWidthAll(2);
        activeStyle.SetCornerRadiusAll(4);

        var hotbarSlots = GetNode("HotbarAnchor/HotbarCenter/HotbarSlots");
        for (int i = 0; i < Inventory.HotbarSlots; i++)
        {
            string slotName = $"Slot{i + 1}";
            var panel = hotbarSlots.GetNode<PanelContainer>(slotName);
            slotPanels[i] = panel;
            slotIcons[i] = panel.GetNode<TextureRect>("Icon");
            slotCounts[i] = panel.GetNode<Label>("StackCount");
        }

        var gridContainer = GetNode("InventoryScreen/InventoryCenter/InventoryPanel/InventoryMargin/InventoryVBox/InventoryGrid");
        for (int i = 0; i < Inventory.TotalSlots - Inventory.HotbarSlots; i++)
        {
            int slotIndex = i + Inventory.HotbarSlots;
            string slotName = $"InvSlot{i + 1}";
            var panel = gridContainer.GetNode<PanelContainer>(slotName);
            slotPanels[slotIndex] = panel;
            slotIcons[slotIndex] = panel.GetNode<TextureRect>("Icon");
            slotCounts[slotIndex] = panel.GetNode<Label>("StackCount");
        }

        inventory.InventoryChanged += RefreshAllSlots;
        RefreshAllSlots();
    }

    public override void _ExitTree()
    {
        if (inventory != null)
            inventory.InventoryChanged -= RefreshAllSlots;
    }

    void RefreshAllSlots()
    {
        for (int i = 0; i < Inventory.TotalSlots; i++)
        {
            var slot = inventory.GetSlot(i);
            if (slot.IsEmpty)
            {
                slotIcons[i].Texture = null;
                slotCounts[i].Text = "";
            }
            else
            {
                slotIcons[i].Texture = slot.Item.icon;
                slotCounts[i].Text = slot.Count > 1 ? slot.Count.ToString() : "";
            }

            if (i < Inventory.HotbarSlots)
            {
                slotPanels[i].AddThemeStyleboxOverride("panel",
                    i == inventory.ActiveHotbarSlot ? activeStyle : normalStyle);
            }
            else
            {
                slotPanels[i].AddThemeStyleboxOverride("panel", normalStyle);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (inventory.ActiveHotbarSlot != _lastHighlightedSlot)
        {
            RefreshAllSlots();
            _lastHighlightedSlot = inventory.ActiveHotbarSlot;
        }
    }
    int _lastHighlightedSlot = -1;

    // --- Drag and Drop ---

    public override Variant _GetDragData(Vector2 atPosition)
    {
        int slotIndex = GetSlotIndexAtPosition(atPosition);
        if (slotIndex < 0) return default;
        var slot = inventory.GetSlot(slotIndex);
        if (slot.IsEmpty) return default;

        var preview = new TextureRect();
        preview.Texture = slot.Item.icon;
        preview.CustomMinimumSize = new Vector2(48, 48);
        preview.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        preview.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        SetDragPreview(preview);

        return slotIndex;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Int) return false;
        int targetSlot = GetSlotIndexAtPosition(atPosition);
        return targetSlot >= 0;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Int) return;
        int sourceSlot = data.AsInt32();
        int targetSlot = GetSlotIndexAtPosition(atPosition);
        if (targetSlot < 0 || sourceSlot == targetSlot) return;

        var source = inventory.GetSlot(sourceSlot);
        var target = inventory.GetSlot(targetSlot);

        if (target.IsEmpty)
        {
            inventory.SwapSlots(sourceSlot, targetSlot);
        }
        else if (!source.IsEmpty && !target.IsEmpty && source.Item.itemID == target.Item.itemID)
        {
            inventory.MergeSlots(sourceSlot, targetSlot);
        }
        else
        {
            inventory.SwapSlots(sourceSlot, targetSlot);
        }

        player.UpdateEquippedItem();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationDragEnd)
        {
            if (!IsDragSuccessful())
            {
                var dragData = GetViewport().GuiGetDragData();
                if (dragData.VariantType == Variant.Type.Int && player.inventoryOpen)
                {
                    int sourceSlot = dragData.AsInt32();
                    DropEntireStack(sourceSlot);
                }
            }
        }
    }

    void DropEntireStack(int slotIndex)
    {
        var slot = inventory.GetSlot(slotIndex);
        if (slot.IsEmpty || slot.Item.droppedScene == null) return;

        var camera = player.GetNode<Camera3D>("Camera3D");
        Vector3 dropPos = camera.GlobalPosition + -camera.GlobalTransform.Basis.Z * 2f;
        Vector3 dropRot = player.GlobalRotation;

        for (int i = 0; i < slot.Count; i++)
        {
            Vector3 offset = new Vector3(
                (float)(Random.Shared.NextDouble() - 0.5) * 0.5f,
                0,
                (float)(Random.Shared.NextDouble() - 0.5) * 0.5f
            );
            GameWorld.SpawnScene(slot.Item.droppedScene.ResourcePath, dropPos + offset, dropRot);
        }

        inventory.ClearSlot(slotIndex);
        player.UpdateEquippedItem();
    }

    int GetSlotIndexAtPosition(Vector2 position)
    {
        for (int i = 0; i < Inventory.TotalSlots; i++)
        {
            if (slotPanels[i] == null) continue;
            Rect2 rect = slotPanels[i].GetGlobalRect();
            if (rect.HasPoint(position + GetGlobalRect().Position))
                return i;
        }
        return -1;
    }
}
