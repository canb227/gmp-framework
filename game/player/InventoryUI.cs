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

    PanelContainer itemTooltip;
    Label tooltipName;
    Label tooltipDescription;
    int _hoveredSlot = -1;

    Control inventoryScreen;
    Label heldItemName;
    /// <summary>The held-item name currently shown above the hotbar.</summary>
    public string heldItemText => heldItemName.Text;

    /// <summary>True while the full inventory screen is shown. The mouse is released and hotbar/grab scrolling and click-to-capture are suppressed; other player input still works.</summary>
    public bool isOpen { get; private set; }

    public override void _Ready()
    {
        player = GetParent<FactoryPlayer>();
        inventory = player.inventory;
        inventoryScreen = GetNode<Control>("InventoryScreen");
        heldItemName = GetNode<Label>("%HeldItemName");

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

        itemTooltip = GetNode<PanelContainer>("%ItemTooltip");
        tooltipName = GetNode<Label>("%TooltipName");
        tooltipDescription = GetNode<Label>("%TooltipDescription");

        for (int i = 0; i < Inventory.TotalSlots; i++)
        {
            if (slotPanels[i] == null) continue;
            slotPanels[i].MouseFilter = MouseFilterEnum.Pass;
            foreach (Node child in slotPanels[i].GetChildren())
            {
                if (child is Control c)
                    c.MouseFilter = MouseFilterEnum.Ignore;
            }

            int slotIndex = i;
            slotPanels[i].MouseEntered += () => OnSlotMouseEntered(slotIndex);
            slotPanels[i].MouseExited += () => OnSlotMouseExited(slotIndex);
        }

        inventory.InventoryChanged += RefreshAllSlots;
        RefreshAllSlots();
    }

    void OnSlotMouseEntered(int slotIndex)
    {
        _hoveredSlot = slotIndex;
        UpdateTooltip();
    }

    void OnSlotMouseExited(int slotIndex)
    {
        if (_hoveredSlot == slotIndex)
        {
            _hoveredSlot = -1;
            itemTooltip.Hide();
        }
    }

    void UpdateTooltip()
    {
        if (_hoveredSlot < 0 || !isOpen)
        {
            itemTooltip.Hide();
            return;
        }

        var slot = inventory.GetSlot(_hoveredSlot);
        if (slot.IsEmpty)
        {
            itemTooltip.Hide();
            return;
        }

        tooltipName.Text = FactoryItem.Fetch(slot.itemID).displayName;
        tooltipDescription.Text = FactoryItem.Fetch(slot.itemID).description;
        itemTooltip.Show();
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
                slotIcons[i].Texture = FactoryItem.Fetch(slot.itemID).icon;
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

        // Name of the item in hand, shown above the hotbar.
        string equipped = inventory.GetEquippedItem();
        heldItemName.Text = equipped == null ? "" : FactoryItem.Fetch(equipped)?.displayName ?? equipped;
    }

    public override void _Process(double delta)
    {
        if (inventory.ActiveHotbarSlot != _lastHighlightedSlot)
        {
            RefreshAllSlots();
            _lastHighlightedSlot = inventory.ActiveHotbarSlot;
        }

        if (_hoveredSlot >= 0 && isOpen)
        {
            if (!itemTooltip.Visible)
            {
                UpdateTooltip();
            }

            Vector2 desiredPos = GetGlobalMousePosition() + new Vector2(16, 16);
            Vector2 viewportSize = GetViewportRect().Size;
            Vector2 tooltipSize = itemTooltip.Size;

            desiredPos.X = Mathf.Clamp(desiredPos.X, 0, Mathf.Max(0, viewportSize.X - tooltipSize.X));
            desiredPos.Y = Mathf.Clamp(desiredPos.Y, 0, Mathf.Max(0, viewportSize.Y - tooltipSize.Y));

            itemTooltip.Position = desiredPos;
        }
        else if (itemTooltip.Visible)
        {
            itemTooltip.Hide();
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
        preview.Texture = FactoryItem.Fetch(slot.itemID).icon;
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
        else if (!source.IsEmpty && !target.IsEmpty && source.itemID == target.itemID)
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
                if (dragData.VariantType == Variant.Type.Int && isOpen)
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
        if (slot.IsEmpty || FactoryItem.Fetch(slot.itemID).droppedScene == null) return;

        var camera = player.camera;
        Vector3 dropPos = camera.GlobalPosition + -camera.GlobalTransform.Basis.Z * 2f;
        Vector3 dropRot = player.GlobalRotation;

        for (int i = 0; i < slot.Count; i++)
        {
            Vector3 offset = new Vector3(
                (float)(Random.Shared.NextDouble() - 0.5) * 0.5f,
                0,
                (float)(Random.Shared.NextDouble() - 0.5) * 0.5f
            );
            GameWorld.SpawnScene(FactoryItem.Fetch(slot.itemID).droppedScene.ResourcePath, dropPos + offset, dropRot);
        }

        inventory.ClearSlot(slotIndex);
        player.UpdateEquippedItem();
    }

    public void Open()
    {
        isOpen = true;
        inventoryScreen.Show();
        MouseFilter = MouseFilterEnum.Stop;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void Close()
    {
        isOpen = false;
        inventoryScreen.Hide();
        MouseFilter = MouseFilterEnum.Ignore;
        Input.MouseMode = Input.MouseModeEnum.Captured;
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
