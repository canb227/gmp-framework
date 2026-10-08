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
/// Multiplayer: stock and resources are shared by everyone and the lobby host owns them. Every change, from any
/// peer, is a request to the host (<see cref="RPCManager"/>), which applies it and broadcasts the result to every
/// peer, itself included; a peer's own copy only changes when that broadcast arrives. A purchase is checked
/// against the host's balance (so two players can't spend the same resources), paid there, and the blueprints
/// granted to the buyer. A peer that connects later gets the whole state from the host.
/// </para>
/// <para>
/// An autoload (for its RPC node path); the API is static.
/// </para>
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

    /// <summary>Raised on the buyer's peer when a purchase is delivered: (blueprint item id, quantity).</summary>
    public static event Action<string, int> Purchased;

    /// <summary>Stored resources by item id: what the players have turned in and not yet spent.</summary>
    public static IReadOnlyDictionary<string, int> Resources => resources;

    /// <summary>Raised whenever a stored resource changes: (item id, new amount).</summary>
    public static event Action<string, int> ResourcesChanged;

    public override void _Ready()
    {
        instance = this;
        ProcessMode = ProcessModeEnum.Always;
        Lobby.PeerConnectedEvent += OnPeerConnected;
    }

    public override void _ExitTree()
    {
        Lobby.PeerConnectedEvent -= OnPeerConnected;
    }

    static void RequestFromHost(string method, object[] args) => RPCManager.RPCTo(Lobby.hostID, instance, method, args);

    static void Broadcast(string method, object[] args) => RPCManager.RPC(instance, method, args);

    // ---- stock ----------------------------------------------------------------

    /// <summary>
    /// Puts <paramref name="itemID"/> on sale. It must be a <see cref="BlueprintItem"/>; returns false (and changes
    /// nothing) for unknown or non-blueprint ids and for ids already on sale. Otherwise the host adds it for everyone.
    /// </summary>
    public static bool AddAvailableItem(string itemID)
    {
        if (!CanStock(itemID))
        {
            return false;
        }
        RequestFromHost(nameof(_RequestAddStock), [itemID]);
        return true;
    }

    /// <summary>Takes everything off sale, for everyone (e.g. when a new game starts; the list is static, so it outlives one).</summary>
    public static void ClearAvailableItems()
    {
        RequestFromHost(nameof(_RequestClearStock), []);
    }

    static bool CanStock(string itemID)
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
        return true;
    }

    [RPC]
    private void _RequestAddStock(string itemID)
    {
        if (Lobby.isHost && CanStock(itemID))
        {
            Broadcast(nameof(_AddStock), [itemID]);
        }
    }

    [RPC]
    private void _RequestClearStock()
    {
        if (Lobby.isHost)
        {
            Broadcast(nameof(_SetStock), [Array.Empty<string>()]);
        }
    }

    [RPC(requireAuthority = true)]
    private void _AddStock(string itemID)
    {
        if (!stock.Contains(itemID))
        {
            stock.Add(itemID);
            StockChanged?.Invoke();
        }
    }

    [RPC(requireAuthority = true)]
    private void _SetStock(string[] itemIDs)
    {
        stock.Clear();
        stock.AddRange(itemIDs);
        StockChanged?.Invoke();
    }

    // ---- resources ------------------------------------------------------------

    /// <summary>How much of the currency <paramref name="itemID"/> the players have to spend.</summary>
    public static int HeldCurrency(string itemID) =>
        string.IsNullOrEmpty(itemID) ? 0 : resources.GetValueOrDefault(itemID, 0);

    /// <summary>Stores <paramref name="amount"/> more of <paramref name="itemID"/> for everyone (e.g. an item turned in to the void).</summary>
    public static void AddResource(string itemID, int amount)
    {
        if (string.IsNullOrEmpty(itemID) || amount <= 0)
        {
            return;
        }
        RequestFromHost(nameof(_RequestAddResource), [itemID, amount]);
    }

    /// <summary>Empties storage for everyone (e.g. when a new game starts; it is static, so it outlives one).</summary>
    public static void ClearResources()
    {
        RequestFromHost(nameof(_RequestClearResources), []);
    }

    [RPC]
    private void _RequestAddResource(string itemID, int amount)
    {
        if (Lobby.isHost && !string.IsNullOrEmpty(itemID) && amount > 0)
        {
            Broadcast(nameof(_SetResource), [itemID, HeldCurrency(itemID) + amount]);
        }
    }

    [RPC]
    private void _RequestClearResources()
    {
        if (Lobby.isHost)
        {
            Broadcast(nameof(_SetResources), [Array.Empty<string>(), Array.Empty<int>()]);
        }
    }

    [RPC(requireAuthority = true)]
    private void _SetResource(string itemID, int amount)
    {
        SetResourceLocal(itemID, amount);
    }

    [RPC(requireAuthority = true)]
    private void _SetResources(string[] itemIDs, int[] amounts)
    {
        List<string> dropped = resources.Keys.Except(itemIDs).ToList();
        foreach (string id in dropped)
        {
            SetResourceLocal(id, 0);
        }
        for (int i = 0; i < itemIDs.Length && i < amounts.Length; i++)
        {
            SetResourceLocal(itemIDs[i], amounts[i]);
        }
    }

    static void SetResourceLocal(string itemID, int amount)
    {
        if (amount <= 0)
            resources.Remove(itemID);
        else
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
    /// Orders <paramref name="quantity"/> of <paramref name="itemID"/> for the local player, whose inventory is
    /// <paramref name="inventory"/>. The order is cut down to what fits in it, then sent to the host, which takes the
    /// payment and grants the blueprints (<see cref="Purchased"/> is raised when they arrive). False (and nothing is
    /// sent) if it isn't on sale, can't be afforded as far as this peer knows, or nothing fits.
    /// </summary>
    public static bool TryPurchase(Inventory inventory, string itemID, int quantity)
    {
        if (inventory == null || quantity < 1 || !stock.Contains(itemID) || ItemInfo.Fetch(itemID) is not BlueprintItem item)
        {
            return false;
        }
        quantity = Math.Min(quantity, inventory.RoomFor(itemID));
        if (quantity < 1 || !CanAfford(item, quantity))
        {
            return false;
        }
        RequestFromHost(nameof(_RequestPurchase), [itemID, quantity]);
        return true;
    }

    // Host: the balance check that counts. First request to arrive wins the resources.
    [RPC]
    private void _RequestPurchase(string itemID, int quantity)
    {
        if (!Lobby.isHost || quantity < 1 || !stock.Contains(itemID) || ItemInfo.Fetch(itemID) is not BlueprintItem item)
        {
            return;
        }
        if (!CanAfford(item, quantity))
        {
            Logging.Log($"purchase of {quantity}x {itemID} by {RPCManager.sender} refused: can't afford it", "Shop");
            return;
        }
        if (item.HasCost)
        {
            Broadcast(nameof(_SetResource), [item.costItemID, HeldCurrency(item.costItemID) - TotalCost(item, quantity)]);
        }
        RPCManager.RPCTo(RPCManager.sender, instance, nameof(_GrantPurchase), [itemID, quantity]);
    }

    // Buyer: put the paid-for blueprints in the local player's inventory; refund whatever no longer fits.
    [RPC(requireAuthority = true)]
    private void _GrantPurchase(string itemID, int quantity)
    {
        FactoryPlayer player = GameWorld.syncedObjs.Values.OfType<FactoryPlayer>().FirstOrDefault(p => p.isLocal);
        int leftover = player == null ? quantity : player.inventory.AddItem(itemID, quantity);
        if (leftover > 0 && ItemInfo.Fetch(itemID) is BlueprintItem item && item.HasCost)
        {
            Logging.Warn($"bought {itemID} but {leftover} no longer fit; refunded", "Shop");
            AddResource(item.costItemID, TotalCost(item, leftover));
        }
        int bought = quantity - leftover;
        if (bought > 0)
        {
            player.UpdateEquippedItem();
            Purchased?.Invoke(itemID, bought);
        }
    }

    // ---- late joiners ---------------------------------------------------------

    void OnPeerConnected(ulong peerID)
    {
        if (!Lobby.isHost)
        {
            return;
        }
        RPCManager.RPCTo(peerID, instance, nameof(_SetStock), [stock.ToArray()]);
        RPCManager.RPCTo(peerID, instance, nameof(_SetResources), [resources.Keys.ToArray(), resources.Values.ToArray()]);
    }
}
