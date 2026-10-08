using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shop terminal UI (ShopUI.tscn): a catalogue of purchasable structure blueprints on the left; selecting one
/// shows its details on the right, with a purchase widget at the bottom (unit cost vs. how much of the cost item
/// the players hold, an order-quantity selector, and a Purchase button).
/// Laid out for a fixed 2560x1440 screen so it can later be rendered in a SubViewport on an in-world display.
/// A view over <see cref="Shop"/>, which holds the stock, prices and the purchase itself; every open shop rebuilds
/// when the stock changes. Opened as a screen by <see cref="UIManager.OpenShop"/>; Exit, Escape or interact close it.
/// </summary>
public partial class ShopUI : UIScreen
{
    [Export] public int maxOrder = 99;
    static readonly Color Accent = new(1f, 0.55f, 0.05f);
    static readonly Color Shortfall = new(0.95f, 0.27f, 0.2f);
    static readonly Color Dim = new(0.5f, 0.5f, 0.53f);

    private Inventory inventory;
    private readonly ButtonGroup rowGroup = new();
    private readonly List<(BlueprintItem item, Button row, Label costLabel)> rows = new();
    private BlueprintItem selected;
    private int quantity = 1;
    private VBoxContainer itemList;
    private Label listCount, emptyDetail, detailCategory, detailName, detailStats, detailDescription;
    private Label costName, costEach, costTotal, costHave, freeLabel, quantityLabel;
    private Control detailContent, costRow;
    private TextureRect detailIcon, costIcon;
    private Button minusButton, plusButton, maxButton, purchaseButton, closeButton;

    public override void _Ready()
    {
        itemList = GetNode<VBoxContainer>("%ItemList");
        listCount = GetNode<Label>("%ListCount");
        emptyDetail = GetNode<Label>("%EmptyDetail");
        detailContent = GetNode<Control>("%DetailContent");
        detailIcon = GetNode<TextureRect>("%DetailIcon");
        detailCategory = GetNode<Label>("%DetailCategory");
        detailName = GetNode<Label>("%DetailName");
        detailStats = GetNode<Label>("%DetailStats");
        detailDescription = GetNode<Label>("%DetailDescription");
        costRow = GetNode<Control>("%CostRow");
        costIcon = GetNode<TextureRect>("%CostIcon");
        costName = GetNode<Label>("%CostName");
        costEach = GetNode<Label>("%CostEach");
        costTotal = GetNode<Label>("%CostTotal");
        costHave = GetNode<Label>("%CostHave");
        freeLabel = GetNode<Label>("%FreeLabel");
        quantityLabel = GetNode<Label>("%Quantity");
        minusButton = GetNode<Button>("%MinusButton");
        plusButton = GetNode<Button>("%PlusButton");
        maxButton = GetNode<Button>("%MaxButton");
        purchaseButton = GetNode<Button>("%PurchaseButton");
        closeButton = GetNode<Button>("%CloseButton");

        minusButton.Pressed += () => SetQuantity(quantity - 1);
        plusButton.Pressed += () => SetQuantity(quantity + 1);
        maxButton.Pressed += () => SetQuantity(MaxAffordable());
        purchaseButton.Pressed += OnPurchasePressed;
        closeButton.Pressed += () => UIManager.CloseScreen(this);

        Shop.StockChanged += OnStockChanged;
        Shop.ResourcesChanged += OnResourcesChanged;
        BuildCatalogue();
    }

    public override void _ExitTree()
    {
        if (fittingToViewport)
        {
            GetViewport().SizeChanged -= Refit;
            fittingToViewport = false;
        }
        Shop.StockChanged -= OnStockChanged;
        Shop.ResourcesChanged -= OnResourcesChanged;
        SetInventory(null);
    }

    /// <summary>Interact closes the shop as well as Escape (the terminal is opened with interact).</summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.interact))
        {
            GetViewport().SetInputAsHandled();
            UIManager.CloseScreen(this);
        }
    }

    /// <summary>Points the shop at <paramref name="playerInventory"/>, which purchases go into.</summary>
    public void Open(Inventory playerInventory)
    {
        SetInventory(playerInventory);
    }

    public override void OnClosed()
    {
        SetInventory(null);
    }

    /// <summary>
    /// For use as a screen overlay: scales the fixed 2560x1440 layout to fit the window (letterboxed, centred) and
    /// keeps it fitted on resize. Not needed when the shop is rendered in its own 2560x1440 SubViewport.
    /// </summary>
    public void FitToViewport()
    {
        if (!fittingToViewport)
        {
            fittingToViewport = true;
            GetViewport().SizeChanged += Refit;
        }
        Refit();
    }

    private bool fittingToViewport;

    private void Refit()
    {
        Vector2 design = CustomMinimumSize;
        Vector2 window = GetViewport().GetVisibleRect().Size;
        float scale = Mathf.Min(window.X / design.X, window.Y / design.Y);
        SetAnchorsPreset(LayoutPreset.TopLeft);
        Size = design;
        Scale = new Vector2(scale, scale);
        Position = (window - design * scale) / 2f;
    }

    private void SetInventory(Inventory playerInventory)
    {
        if (inventory != null)
        {
            inventory.InventoryChanged -= RefreshCosts;
        }
        inventory = playerInventory;
        if (inventory != null)
        {
            inventory.InventoryChanged += RefreshCosts;
        }
        RefreshCosts();
    }

    // ---- catalogue -------------------------------------------------------------

    /// <summary>Rebuilds the list from <see cref="Shop.Stock"/>, keeping the selection if it's still on sale.</summary>
    private void BuildCatalogue()
    {
        foreach (Node child in itemList.GetChildren())
        {
            itemList.RemoveChild(child);
            child.QueueFree();
        }
        rows.Clear();

        // Grouped by definition folder, groups in order of first appearance, items in the order they were added.
        List<BlueprintItem> stock = Shop.Stock.Select(id => ItemInfo.Fetch(id) as BlueprintItem).Where(i => i != null).ToList();
        bool first = true;
        foreach (var group in stock.GroupBy(CategoryOf))
        {
            AddCategoryHeader(group.Key, first);
            first = false;
            foreach (BlueprintItem item in group)
            {
                AddRow(item);
            }
        }
        if (stock.Count == 0)
        {
            itemList.AddChild(new Label { Text = "NO STOCK AVAILABLE", ThemeTypeVariation = "ShopDim" });
        }
        listCount.Text = $"{rows.Count} ENTRIES";
        emptyDetail.Text = stock.Count == 0 ? "NO STOCK AVAILABLE" : "SELECT AN ENTRY FROM THE CATALOGUE";

        Select(stock.Contains(selected) ? selected : stock.FirstOrDefault());
    }

    private void OnStockChanged()
    {
        BuildCatalogue();
    }

    /// <summary>Turn-ins and purchases change what the player can afford.</summary>
    private void OnResourcesChanged(string itemID, int amount)
    {
        if (selected != null)
        {
            RefreshCosts();
        }
    }

    /// <summary>The blueprint's definition folder, e.g. "chutes" (top-level blueprints are "general").</summary>
    private static string CategoryOf(BlueprintItem item)
    {
        string folder = item.ResourcePath.GetBaseDir().TrimSuffix("/").GetFile();
        return folder == "blueprints" ? "general" : folder;
    }

    private void AddCategoryHeader(string category, bool first)
    {
        if (!first)
        {
            itemList.AddChild(new Control { CustomMinimumSize = new Vector2(0, 16) });
        }
        itemList.AddChild(new Label
        {
            Text = "// " + category.Replace('_', ' ').ToUpperInvariant(),
            ThemeTypeVariation = "ShopDim",
        });
    }

    private void AddRow(BlueprintItem item)
    {
        Button row = new()
        {
            ToggleMode = true,
            ButtonGroup = rowGroup,
            ThemeTypeVariation = "ShopRow",
            CustomMinimumSize = new Vector2(0, 104),
            FocusMode = FocusModeEnum.None,
        };
        row.Pressed += () => Select(item);

        // Buttons don't lay out children, so a full-rect margin container holds the row contents.
        MarginContainer margin = new() { MouseFilter = MouseFilterEnum.Ignore };
        margin.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 26);
        margin.AddThemeConstantOverride("margin_right", 20);
        HBoxContainer line = new() { MouseFilter = MouseFilterEnum.Ignore };
        line.AddThemeConstantOverride("separation", 24);

        line.AddChild(Icon(item.icon, 80));
        line.AddChild(new Label
        {
            Text = ShopName(item),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            VerticalAlignment = VerticalAlignment.Center,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
            MouseFilter = MouseFilterEnum.Ignore,
        });
        Label costLabel = new()
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Right,
            CustomMinimumSize = new Vector2(80, 0),
            MouseFilter = MouseFilterEnum.Ignore,
        };
        if (item.HasCost)
        {
            line.AddChild(Icon(ItemInfo.Fetch(item.costItemID)?.icon, 44));
        }
        line.AddChild(costLabel);

        margin.AddChild(line);
        row.AddChild(margin);
        itemList.AddChild(row);
        rows.Add((item, row, costLabel));
    }

    /// <summary>Display name without the "Blueprint: " prefix every entry here would share.</summary>
    private static string ShopName(BlueprintItem item)
    {
        string name = item.displayName ?? item.itemID;
        return name.StartsWith("Blueprint: ") ? name.Substring("Blueprint: ".Length) : name;
    }

    private static TextureRect Icon(Texture2D texture, int size) => new()
    {
        Texture = texture,
        CustomMinimumSize = new Vector2(size, size),
        ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
        StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        SizeFlagsVertical = SizeFlags.ShrinkCenter,
        MouseFilter = MouseFilterEnum.Ignore,
    };

    // ---- detail + purchase -----------------------------------------------------

    private void Select(BlueprintItem item)
    {
        if (item != selected)
        {
            quantity = 1;
        }
        selected = item;
        detailContent.Visible = item != null;
        emptyDetail.Visible = item == null;
        foreach (var (rowItem, row, _) in rows)
        {
            if (rowItem == item)
            {
                row.SetPressedNoSignal(true);
            }
        }
        if (item == null)
        {
            return;
        }

        detailIcon.Texture = item.icon;
        detailCategory.Text = $"BLUEPRINT  //  {CategoryOf(item).Replace('_', ' ').ToUpperInvariant()}";
        detailName.Text = ShopName(item);
        string alternate = item.alternateStructureScene != null ? "  //  2 FORMS" : "";
        detailStats.Text = $"ID {item.itemID}  //  STACKS TO {item.maxStackSize}{alternate}";
        detailDescription.Text = string.IsNullOrEmpty(item.description) ? "No specification on file." : item.description;

        RefreshCosts();
    }

    private int MaxAffordable() => selected == null ? 1 : Shop.MaxAffordable(selected, maxOrder);

    private void SetQuantity(int value)
    {
        quantity = Math.Clamp(value, 1, maxOrder);
        RefreshCosts();
    }

    /// <summary>Re-reads held counts into the catalogue cost column and the purchase widget.</summary>
    private void RefreshCosts()
    {
        if (detailContent == null)
        {
            return; // before _Ready
        }

        foreach (var (item, _, costLabel) in rows)
        {
            costLabel.Text = item.HasCost ? $"×{item.costAmount}" : "FREE";
            bool short1 = !Shop.CanAfford(item, 1);
            costLabel.AddThemeColorOverride("font_color", !item.HasCost ? Dim : short1 ? Shortfall : Accent);
        }

        if (selected == null)
        {
            return;
        }

        quantityLabel.Text = quantity.ToString();
        minusButton.Disabled = quantity <= 1;
        plusButton.Disabled = quantity >= maxOrder;

        costRow.Visible = selected.HasCost;
        freeLabel.Visible = !selected.HasCost;
        bool affordable = true;
        if (selected.HasCost)
        {
            ItemInfo costItem = ItemInfo.Fetch(selected.costItemID);
            int total = Shop.TotalCost(selected, quantity);
            int held = Shop.HeldCurrency(selected.costItemID);
            affordable = Shop.CanAfford(selected, quantity);

            costIcon.Texture = costItem?.icon;
            costName.Text = costItem?.displayName ?? selected.costItemID;
            costEach.Text = $"{selected.costAmount} PER UNIT";
            costTotal.Text = total.ToString();
            costHave.Text = held.ToString();
            costHave.AddThemeColorOverride("font_color", affordable ? Accent : Shortfall);
        }

        purchaseButton.Disabled = !affordable;
        purchaseButton.Text = affordable
            ? $"PURCHASE  ×{quantity}"
            : $"INSUFFICIENT {(ItemInfo.Fetch(selected.costItemID)?.displayName ?? selected.costItemID).ToUpperInvariant()}";
    }

    private void OnPurchasePressed()
    {
        if (selected != null)
        {
            // The host confirms the order; costs and the inventory refresh through their change events when it does.
            Shop.TryPurchase(inventory, selected.itemID, quantity);
        }
    }
}
