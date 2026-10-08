using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// The shop's rules, shared by every shop terminal: what's on sale (<see cref="Stock"/>), the players' stored
/// resources (<see cref="Resources"/>), what things cost and whether the player can pay, and the purchase itself.
/// <see cref="ShopUI"/> is only a view over this.
/// <para>
/// Resources are the items turned in to the void (<see cref="AddResource"/>), counted by item id. Any of them can be
/// a currency: prices are paid from these, not from the player's inventory; bought blueprints go into the buyer's
/// inventory.
/// </para>
/// <para>
/// Multiplayer: the lobby host owns stock and resources. Only the host changes them, broadcasting each change to
/// every peer, itself included. Other players can only request purchases, which are paid for on the host (so two
/// players can't spend the same resources) and the blueprints sent back to the buyer. A peer that connects later
/// is sent the whole state.
/// </para>
/// <para>An autoload, so RPCs have a node to target; the API is static.</para>
/// </summary>
public partial class Shop : Node
{
    static Shop instance;

    static readonly List<string> stock = new();
    static readonly Dictionary<string, int> resources = new();

    /// <summary>Blueprint item ids on sale, in the order they were added. Starts empty; grows through quest rewards.</summary>
    public static IReadOnlyList<string> Stock => stock;

    /// <summary>Raised whenever <see cref="Stock"/> changes.</summary>
    public static event Action StockChanged;

    /// <summary>Stored resources by item id: what the players have turned in and not yet spent.</summary>
    public static IReadOnlyDictionary<string, int> Resources => resources;

    /// <summary>Raised whenever a stored resource changes: (item id, new amount).</summary>
    public static event Action<string, int> ResourcesChanged;

    /// <summary>Raised on the buyer's peer when a purchase arrives: (blueprint item id, quantity).</summary>
    public static event Action<string, int> Purchased;

    public override void _Ready()
    {
        instance = this;
        Lobby.PeerConnectedEvent += SendStateTo;
    }

    public override void _ExitTree()
    {
        Lobby.PeerConnectedEvent -= SendStateTo;
    }

    // ---- stock ----------------------------------------------------------------

    /// <summary>
    /// Host only: puts <paramref name="itemID"/> on sale for everyone. False (and nothing changes) for unknown or
    /// non-blueprint ids and for ids already on sale.
    /// </summary>
    public static bool AddAvailableItem(string itemID)
    {
        if (stock.Contains(itemID)) return false;
        if (ItemInfo.Fetch(itemID) is not BlueprintItem)
        {
            Logging.Log($"can't stock '{itemID}': not a blueprint item", "Shop");
            return false;
        }
        RPCManager.RPC(instance, nameof(_AddStock), [itemID]);
        return true;
    }

    /// <summary>Host only: takes everything off sale for everyone (the list is static, so it outlives a game).</summary>
    public static void ClearAvailableItems()
    {
        RPCManager.RPC(instance, nameof(_ClearStock), []);
    }

    [RPC(requireAuthority = true)]
    private void _AddStock(string itemID)
    {
        stock.Add(itemID);
        StockChanged?.Invoke();
    }

    [RPC(requireAuthority = true)]
    private void _ClearStock()
    {
        stock.Clear();
        StockChanged?.Invoke();
    }

    // ---- resources ------------------------------------------------------------

    /// <summary>How much of the currency <paramref name="itemID"/> the players have to spend.</summary>
    public static int HeldCurrency(string itemID) =>
        string.IsNullOrEmpty(itemID) ? 0 : resources.GetValueOrDefault(itemID, 0);

    /// <summary>Host only: stores <paramref name="amount"/> more of <paramref name="itemID"/> for everyone (e.g. an item turned in to the void).</summary>
    public static void AddResource(string itemID, int amount)
    {
        RPCManager.RPC(instance, nameof(_SetResource), [itemID, HeldCurrency(itemID) + amount]);
    }

    [RPC(requireAuthority = true)]
    private void _SetResource(string itemID, int amount)
    {
        resources[itemID] = amount;
        ResourcesChanged?.Invoke(itemID, amount);
    }

    // ---- purchasing -----------------------------------------------------------

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
    /// Orders <paramref name="quantity"/> of <paramref name="itemID"/> for the local player, cut down to what fits in
    /// <paramref name="inventory"/>. The host takes the payment and sends the blueprints back (<see cref="Purchased"/>).
    /// False (and nothing is sent) if it isn't on sale, can't be afforded or nothing fits.
    /// </summary>
    public static bool TryPurchase(Inventory inventory, string itemID, int quantity)
    {
        if (inventory == null || !stock.Contains(itemID) || ItemInfo.Fetch(itemID) is not BlueprintItem item) return false;

        quantity = Math.Min(quantity, inventory.RoomFor(itemID));
        if (quantity < 1 || !CanAfford(item, quantity)) return false;

        RPCManager.RPCTo(Lobby.hostID, instance, nameof(_RequestPurchase), [itemID, quantity]);
        return true;
    }

    // Host: checks the order against its own balance (first request wins), takes the payment, sends the blueprints.
    [RPC]
    private void _RequestPurchase(string itemID, int quantity)
    {
        if (!Lobby.isHost || quantity < 1 || !stock.Contains(itemID) || ItemInfo.Fetch(itemID) is not BlueprintItem item) return;
        if (!CanAfford(item, quantity))
        {
            Logging.Log($"purchase of {quantity}x {itemID} by {RPCManager.sender} refused: can't afford it", "Shop");
            return;
        }
        if (item.HasCost)
        {
            RPCManager.RPC(this, nameof(_SetResource), [item.costItemID, HeldCurrency(item.costItemID) - TotalCost(item, quantity)]);
        }
        RPCManager.RPCTo(RPCManager.sender, this, nameof(_ReceivePurchase), [itemID, quantity]);
    }

    // Buyer: the blueprints go into the local player's inventory (TryPurchase already cut the order to what fits).
    [RPC(requireAuthority = true)]
    private void _ReceivePurchase(string itemID, int quantity)
    {
        FactoryPlayer player = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
        if (player == null) return;

        int leftover = player.inventory.AddItem(itemID, quantity);
        if (leftover > 0)
        {
            Logging.Warn($"bought {itemID} but {leftover} no longer fit; lost", "Shop");
        }
        int bought = quantity - leftover;
        if (bought > 0)
        {
            player.UpdateEquippedItem();
            Purchased?.Invoke(itemID, bought);
        }
    }

    // ---- late joiners ---------------------------------------------------------

    // Host: the same messages that built this state, replayed to the new peer.
    void SendStateTo(ulong peerID)
    {
        if (!Lobby.isHost) return;
        RPCManager.RPCTo(peerID, this, nameof(_ClearStock), []);
        foreach (string itemID in stock)
        {
            RPCManager.RPCTo(peerID, this, nameof(_AddStock), [itemID]);
        }
        foreach (var (itemID, amount) in resources)
        {
            RPCManager.RPCTo(peerID, this, nameof(_SetResource), [itemID, amount]);
        }
    }
}
