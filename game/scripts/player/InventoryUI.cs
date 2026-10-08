using Godot;
using System;

/// <summary>
/// The local player's HUD (PlayerHUD.tscn): hotbar, held-item name, crosshair hover labels, and the inventory
/// screen. Created and bound by <see cref="UIManager.BindHud"/>; open/close the inventory through UIManager too,
/// which owns the mouse mode.
/// </summary>
public partial class InventoryUI : Control
{
    /// <summary>The player this HUD shows. Set by UIManager before the HUD enters the tree.</summary>
    public FactoryPlayer player;
    Inventory inventory;
    Label hoverInfoName, hoverInfoBelow;

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
    Control inventoryCenter;
    Control inventoryPanel;
    GridContainer inventoryGrid;
    Label heldItemName;

    /// <summary>Inventory grid widths: the square layout, and a wide one (as wide as the hotbar) for short screens.</summary>
    const int SquareColumns = 5, WideColumns = 10;
    /// <summary>The held-item name currently shown above the hotbar.</summary>
    public string heldItemText => heldItemName.Text;

    /// <summary>True while the full inventory screen is shown. The mouse is released and hotbar/grab scrolling and click-to-capture are suppressed; other player input still works.</summary>
    public bool isOpen { get; private set; }

    public override void _Ready()
    {
        HiResUI.Fill(this);
        inventory = player.inventory;
        inventoryScreen = GetNode<Control>("InventoryScreen");
        heldItemName = GetNode<Label>("%HeldItemName");
        hoverInfoName = GetNode<Label>("%HoverInfoName");
        hoverInfoBelow = GetNode<Label>("%HoverInfoBelow");

        // Terminal-style slot wells (see gmp/ui/theme/menu_theme.tres); the active hotbar slot gets the orange accent.
        normalStyle = new StyleBoxFlat();
        normalStyle.BgColor = new Color(0.035f, 0.035f, 0.04f, 0.82f);
        normalStyle.BorderColor = new Color(0.24f, 0.24f, 0.26f, 0.9f);
        normalStyle.SetBorderWidthAll(2);
        normalStyle.SetContentMarginAll(12);

        activeStyle = new StyleBoxFlat();
        activeStyle.BgColor = new Color(0.16f, 0.1f, 0.03f, 0.9f);
        activeStyle.BorderColor = new Color(1f, 0.55f, 0.05f, 1f);
        activeStyle.SetBorderWidthAll(3);
        activeStyle.SetContentMarginAll(12);

        var hotbarSlots = GetNode("HotbarAnchor/HotbarCenter/HotbarSlots");
        for (int i = 0; i < Inventory.HotbarSlots; i++)
        {
            string slotName = $"Slot{i + 1}";
            var panel = hotbarSlots.GetNode<PanelContainer>(slotName);
            slotPanels[i] = panel;
            slotIcons[i] = panel.GetNode<TextureRect>("Icon");
            slotCounts[i] = panel.GetNode<Label>("StackCount");
        }

        inventoryCenter = GetNode<Control>("InventoryScreen/InventoryCenter");
        inventoryPanel = GetNode<Control>("InventoryScreen/InventoryCenter/InventoryPanel");
        inventoryGrid = GetNode<GridContainer>("InventoryScreen/InventoryCenter/InventoryPanel/InventoryMargin/InventoryVBox/InventoryGrid");
        for (int i = 0; i < Inventory.TotalSlots - Inventory.HotbarSlots; i++)
        {
            int slotIndex = i + Inventory.HotbarSlots;
            string slotName = $"InvSlot{i + 1}";
            var panel = inventoryGrid.GetNode<PanelContainer>(slotName);
            slotPanels[slotIndex] = panel;
            slotIcons[slotIndex] = panel.GetNode<TextureRect>("Icon");
            slotCounts[slotIndex] = panel.GetNode<Label>("StackCount");
        }

        itemTooltip = GetNode<PanelContainer>("%ItemTooltip");
        tooltipName = GetNode<Label>("%TooltipName");
        tooltipDescription = GetNode<Label>("%TooltipDescription");

        for (int i = 0; i < Inventory.TotalSlots; i++)
        {
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
        inventoryScreen.Resized += FitInventoryGrid;
        FitInventoryGrid();
    }

    /// <summary>
    /// Uses the square inventory grid when the panel fits above the hotbar, and otherwise lays the same slots out
    /// <see cref="WideColumns"/> wide (fewer rows), so it fits short windows (720p gets a 960-unit tall screen, see
    /// <see cref="HiResUI.MinPixelScale"/>).
    /// </summary>
    void FitInventoryGrid()
    {
        int slots = inventoryGrid.GetChildCount();
        float slotHeight = ((Control)inventoryGrid.GetChild(0)).GetCombinedMinimumSize().Y;
        float gap = inventoryGrid.GetThemeConstant("v_separation");
        float GridHeight(int columns)
        {
            int rows = (slots + columns - 1) / columns;
            return rows * slotHeight + (rows - 1) * gap;
        }
        // Everything in the panel apart from the grid: header, rule, padding.
        float chrome = inventoryPanel.GetCombinedMinimumSize().Y - GridHeight(inventoryGrid.Columns);
        // The centring area's anchored height (it grows to fit the panel, so its own size can't be used).
        float available = inventoryScreen.Size.Y + inventoryCenter.OffsetBottom - inventoryCenter.OffsetTop;
        inventoryGrid.Columns = chrome + GridHeight(SquareColumns) <= available ? SquareColumns : WideColumns;
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

        ItemInfo item = ItemInfo.Fetch(slot.itemID);
        tooltipName.Text = item.displayName;
        tooltipDescription.Text = item.description;
        itemTooltip.Show();
    }

    public override void _ExitTree()
    {
        inventory.InventoryChanged -= RefreshAllSlots;
    }

    /// <summary>The crosshair labels: the target's name and the prompt below it. Null hides a line.</summary>
    public void SetHoverInfo(string name, string prompt)
    {
        hoverInfoName.Visible = name != null;
        if (name != null) hoverInfoName.Text = name;
        hoverInfoBelow.Visible = prompt != null;
        if (prompt != null) hoverInfoBelow.Text = prompt;
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
                slotIcons[i].Texture = ItemInfo.Fetch(slot.itemID).icon;
                slotCounts[i].Text = slot.Count > 1 ? slot.Count.ToString() : "";
            }

            // The active slot is always a hotbar slot.
            slotPanels[i].AddThemeStyleboxOverride("panel", i == inventory.ActiveHotbarSlot ? activeStyle : normalStyle);
        }

        // Name of the item in hand, shown above the hotbar.
        string equipped = inventory.GetEquippedItem();
        heldItemName.Text = equipped == null ? "" : ItemInfo.Fetch(equipped)?.displayName ?? equipped;
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

            // In this (scaled, see HiResUI) control's own space.
            Vector2 desiredPos = GetLocalMousePosition() + new Vector2(28, 28);
            Vector2 viewportSize = Size;
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
        preview.Texture = ItemInfo.Fetch(slot.itemID).icon;
        preview.CustomMinimumSize = new Vector2(96, 96) * Scale;
        preview.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
        preview.StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered;
        SetDragPreview(preview);

        return slotIndex;
    }

    public override bool _CanDropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Int) return false;
        return GetSlotIndexAtPosition(atPosition) >= 0;
    }

    public override void _DropData(Vector2 atPosition, Variant data)
    {
        if (data.VariantType != Variant.Type.Int) return;
        int sourceSlot = data.AsInt32();
        int targetSlot = GetSlotIndexAtPosition(atPosition);
        if (targetSlot < 0 || sourceSlot == targetSlot) return;

        var source = inventory.GetSlot(sourceSlot);
        var target = inventory.GetSlot(targetSlot);

        if (!source.IsEmpty && !target.IsEmpty && source.itemID == target.itemID)
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
        // A slot dragged out of the inventory and released over nothing drops its whole stack.
        if (what != NotificationDragEnd || IsDragSuccessful()) return;
        var dragData = GetViewport().GuiGetDragData();
        if (dragData.VariantType == Variant.Type.Int && isOpen)
        {
            DropEntireStack(dragData.AsInt32());
        }
    }

    void DropEntireStack(int slotIndex)
    {
        var slot = inventory.GetSlot(slotIndex);
        if (slot.IsEmpty) return;
        PackedScene droppedScene = ItemInfo.Fetch(slot.itemID).droppedScene;
        if (droppedScene == null) return;

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
            GameWorld.SpawnScene(droppedScene.ResourcePath, dropPos + offset, dropRot);
        }

        inventory.ClearSlot(slotIndex);
        player.UpdateEquippedItem();
    }

    /// <summary>Shows the inventory screen. Call through <see cref="UIManager.OpenInventory"/>, which frees the mouse.</summary>
    public void Open()
    {
        isOpen = true;
        inventoryScreen.Show();
        MouseFilter = MouseFilterEnum.Stop;
    }

    /// <summary>Hides the inventory screen. Call through <see cref="UIManager.CloseInventory"/>, which recaptures the mouse.</summary>
    public void Close()
    {
        isOpen = false;
        inventoryScreen.Hide();
        MouseFilter = MouseFilterEnum.Ignore;
    }

    /// <summary>The slot under <paramref name="position"/> (in this control's local space), or -1.</summary>
    int GetSlotIndexAtPosition(Vector2 position)
    {
        // Compare in each slot's own space: the HUD is scaled (HiResUI), which global rects don't account for.
        Vector2 canvasPoint = GetGlobalTransform() * position;
        for (int i = 0; i < Inventory.TotalSlots; i++)
        {
            Vector2 local = slotPanels[i].GetGlobalTransform().AffineInverse() * canvasPoint;
            if (new Rect2(Vector2.Zero, slotPanels[i].Size).HasPoint(local))
                return i;
        }
        return -1;
    }
}
