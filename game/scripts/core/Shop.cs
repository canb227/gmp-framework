using System;
using System.Collections.Generic;

/// <summary>
/// The shop's rules, shared by every shop terminal: what's on sale (<see cref="Stock"/>), what it costs and whether
/// the player can pay, and the purchase itself. <see cref="ShopUI"/> is only a view over this.
/// <para>
/// Prices are paid from <see cref="ProgressManager.extraItemStorage"/>, the items turned in to the void, not from
/// the player's inventory; bought blueprints go into the buyer's inventory.
/// </para>
/// </summary>
public static class Shop
{
    static readonly List<string> stock = new();

    /// <summary>Blueprint item ids on sale, in the order they were added. Starts empty; grows through quest rewards.</summary>
    public static IReadOnlyList<string> Stock => stock;

    /// <summary>Raised whenever <see cref="Stock"/> changes.</summary>
    public static event Action StockChanged;

    /// <summary>Raised after a purchase: (blueprint item id, quantity).</summary>
    public static event Action<string, int> Purchased;

    /// <summary>
    /// Puts <paramref name="itemID"/> on sale. It must be a <see cref="BlueprintItem"/>; returns false (and changes
    /// nothing) for unknown or non-blueprint ids and for ids already on sale.
    /// </summary>
    public static bool AddAvailableItem(string itemID)
    {
        if (stock.Contains(itemID))
        {
            return false;
        }
        if (ItemInfo.Fetch(itemID) is not BlueprintItem)
        {
            Logging.Log($"can't stock '{itemID}': not a blueprint item", "Shop");
            return false;
        }
        stock.Add(itemID);
        StockChanged?.Invoke();
        return true;
    }

    /// <summary>Takes everything off sale (e.g. when a new game starts; the list is static, so it outlives one).</summary>
    public static void ClearAvailableItems()
    {
        stock.Clear();
        StockChanged?.Invoke();
    }

    /// <summary>How much of the currency <paramref name="itemID"/> the players have to spend.</summary>
    public static int HeldCurrency(string itemID) =>
        string.IsNullOrEmpty(itemID) ? 0 : ProgressManager.extraItemStorage.GetValueOrDefault(itemID, 0);

    public static int TotalCost(BlueprintItem item, int quantity) => item.HasCost ? item.costAmount * quantity : 0;

    public static bool CanAfford(BlueprintItem item, int quantity) =>
        !item.HasCost || HeldCurrency(item.costItemID) >= TotalCost(item, quantity);

    /// <summary>The largest order of <paramref name="item"/> that can be paid for, at least 1 and at most <paramref name="maxOrder"/>.</summary>
    public static int MaxAffordable(BlueprintItem item, int maxOrder)
    {
        if (!item.HasCost)
        {
            return Math.Clamp(item.maxStackSize, 1, maxOrder);
        }
        return Math.Clamp(HeldCurrency(item.costItemID) / item.costAmount, 1, maxOrder);
    }

    /// <summary>
    /// Buys <paramref name="quantity"/> of <paramref name="itemID"/> into <paramref name="inventory"/>: takes the
    /// payment and adds the blueprints. Units that don't fit in the inventory are refunded. False (and nothing
    /// happens) if it isn't on sale, can't be afforded or nothing fits.
    /// </summary>
    public static bool TryPurchase(Inventory inventory, string itemID, int quantity)
    {
        if (inventory == null || quantity < 1 || !stock.Contains(itemID) || ItemInfo.Fetch(itemID) is not BlueprintItem item)
        {
            return false;
        }
        if (!CanAfford(item, quantity))
        {
            return false;
        }
        int leftover = inventory.AddItem(itemID, quantity);
        int bought = quantity - leftover;
        if (bought <= 0)
        {
            return false;
        }
        if (item.HasCost)
        {
            ProgressManager.extraItemStorage[item.costItemID] = HeldCurrency(item.costItemID) - TotalCost(item, bought);
        }
        Purchased?.Invoke(itemID, bought);
        return true;
    }
}
