using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Shop terminal UI (ShopUI.tscn): a catalogue of purchasable structure blueprints on the left; selecting one
/// shows its details on the right, with a purchase widget at the bottom (per currency in its price: the cost vs. how
/// much the players' bank holds; an order-quantity selector, and a Purchase button). The header shows every
/// discovered currency's bank (<see cref="CurrencyBar"/>). Only stock whose currencies are all discovered is listed.
/// Laid out for a fixed 2560x1440 screen so it can later be rendered in a SubViewport on an in-world display.
/// A view over <see cref="Shop"/>, which holds the stock, prices and the purchase itself; every open shop rebuilds
/// when the stock changes or a currency is discovered. Opened as a screen by <see cref="UIManager.OpenShop"/>; Exit, Escape or interact close it.
/// </summary>
public partial class ShopUI : UIScreen
{
    [Export] public int maxOrder = 99;
    static readonly Color Accent = new(1f, 0.55f, 0.05f);
    static readonly Color Shortfall = new(0.95f, 0.27f, 0.2f);
    static readonly Color Dim = new(0.5f, 0.5f, 0.53f);

    private Inventory inventory;
    private readonly ButtonGroup rowGroup = new();
    private readonly List<(BlueprintItem item, Button row, HBoxContainer costBox)> rows = new();
    private BlueprintItem selected;
    private int quantity = 1;
    private VBoxContainer itemList;
    private Label listCount, emptyDetail, detailCategory, detailName, detailStats, detailDescription;
    private Label freeLabel, quantityLabel;
    private Control detailContent;
    private VBoxContainer costList;
    private TextureRect detailIcon;
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
        costList = GetNode<VBoxContainer>("%CostList");
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

        Shop.StockChanged += BuildCatalogue;
        Shop.CurrencyDiscovered += BuildCatalogue;
        Shop.BalancesChanged += RefreshCosts;
        BuildCatalogue();
    }

    public override void _ExitTree()
    {
        if (fittingToViewport)
        {
            GetViewport().SizeChanged -= Refit;
            fittingToViewport = false;
        }
        Shop.StockChanged -= BuildCatalogue;
        Shop.CurrencyDiscovered -= BuildCatalogue;
        Shop.BalancesChanged -= RefreshCosts;
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

    /// <summary>Points the shop at <paramref name="playerInventory"/>, which purchases go into (null: none).</summary>
    public void SetInventory(Inventory playerInventory)
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

    /// <summary>Rebuilds the list from the available <see cref="Shop.Stock"/>, keeping the selection if it's still listed.</summary>
    private void BuildCatalogue()
    {
        foreach (Node child in itemList.GetChildren())
        {
            itemList.RemoveChild(child);
            child.QueueFree();
        }
        rows.Clear();

        // Grouped by definition folder, groups in order of first appearance, items in the order they were added.
        List<BlueprintItem> stock = Shop.Stock.Select(id => ItemInfo.Fetch(id) as BlueprintItem).Where(i => i != null && Shop.IsAvailable(i)).ToList();
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
        // Filled by RefreshCosts: an icon and amount per currency in the price.
        HBoxContainer costBox = new() { MouseFilter = MouseFilterEnum.Ignore };
        costBox.AddThemeConstantOverride("separation", 12);
        line.AddChild(costBox);

        margin.AddChild(line);
        row.AddChild(margin);
        itemList.AddChild(row);
        rows.Add((item, row, costBox));
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

    /// <summary>Re-reads the banks into the catalogue cost column and the purchase widget.</summary>
    private void RefreshCosts()
    {
        foreach (var (item, _, costBox) in rows)
        {
            Clear(costBox);
            if (!Shop.HasCost(item.cost))
            {
                costBox.AddChild(CostLabel("FREE", Dim));
            }
            foreach (var (currency, amount) in item.cost)
            {
                if (amount <= 0) continue;
                costBox.AddChild(Icon(Shop.Currencies[currency].icon, 44));
                costBox.AddChild(CostLabel($"×{amount}", Shop.Balance(currency) >= amount ? Accent : Shortfall));
            }
        }

        if (selected == null)
        {
            return;
        }

        quantityLabel.Text = quantity.ToString();
        minusButton.Disabled = quantity <= 1;
        plusButton.Disabled = quantity >= maxOrder;

        freeLabel.Visible = !Shop.HasCost(selected.cost);
        Clear(costList);
        string shortOf = null; // the first currency the order is short of
        foreach (var (currency, amount) in selected.cost)
        {
            if (amount <= 0) continue;
            int total = amount * quantity;
            int held = Shop.Balance(currency);
            if (held < total)
            {
                shortOf ??= Shop.Currencies[currency].name;
            }
            costList.AddChild(CostRow(currency, amount, total, held));
        }

        bool affordable = shortOf == null;
        purchaseButton.Disabled = !affordable;
        purchaseButton.Text = affordable ? $"PURCHASE  ×{quantity}" : $"INSUFFICIENT {shortOf.ToUpperInvariant()}";
    }

    /// <summary>One currency of the selected price: icon and name, unit cost, the order's total, and the bank.</summary>
    private static Control CostRow(Currency currency, int each, int total, int held)
    {
        CurrencyInfo info = Shop.Currencies[currency];
        PanelContainer well = new() { ThemeTypeVariation = "ShopIconWell" };
        HBoxContainer line = new();
        line.AddThemeConstantOverride("separation", 24);
        well.AddChild(line);

        line.AddChild(Icon(info.icon, 80));
        VBoxContainer nameColumn = new() { SizeFlagsHorizontal = SizeFlags.ExpandFill, SizeFlagsVertical = SizeFlags.ShrinkCenter };
        nameColumn.AddThemeConstantOverride("separation", 0);
        nameColumn.AddChild(new Label { Text = info.name });
        nameColumn.AddChild(new Label { Text = $"{each} PER UNIT", ThemeTypeVariation = "ShopDim" });
        line.AddChild(nameColumn);
        line.AddChild(NumberColumn("REQUIRED", total, 220, null));
        line.AddChild(NumberColumn("IN BANK", held, 260, held >= total ? Accent : Shortfall));
        return well;
    }

    private static VBoxContainer NumberColumn(string header, int value, int width, Color? color)
    {
        VBoxContainer column = new() { CustomMinimumSize = new Vector2(width, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter };
        column.AddThemeConstantOverride("separation", 0);
        column.AddChild(new Label { Text = header, ThemeTypeVariation = "ShopDim", HorizontalAlignment = HorizontalAlignment.Right });
        Label number = new() { Text = value.ToString(), HorizontalAlignment = HorizontalAlignment.Right };
        number.AddThemeFontSizeOverride("font_size", 44);
        if (color != null)
        {
            number.AddThemeColorOverride("font_color", color.Value);
        }
        column.AddChild(number);
        return column;
    }

    private static Label CostLabel(string text, Color color)
    {
        Label label = new()
        {
            Text = text,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        label.AddThemeColorOverride("font_color", color);
        return label;
    }

    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren())
        {
            parent.RemoveChild(child);
            child.QueueFree();
        }
    }

    private void OnPurchasePressed()
    {
        // The host confirms the order; costs and the inventory refresh through their change events when it does.
        Shop.TryPurchase(inventory, selected.itemID, quantity);
    }
}
