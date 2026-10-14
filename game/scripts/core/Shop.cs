using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>What the shop takes as payment. Names and icons are in <see cref="Shop.Currencies"/>.</summary>
public enum Currency { Scrap, MetalBar }

/// <summary>How a <see cref="Currency"/> is shown in the UI.</summary>
public record CurrencyInfo(string name, Texture2D icon);

/// <summary>
/// The shop's rules, shared by every shop terminal: what's on sale (<see cref="Stock"/>), the players' currency banks
/// (<see cref="Balances"/>), what things cost and whether the players can pay, and the purchase itself.
/// <see cref="ShopUI"/> is only a view over this.
/// <para>
/// Items turned in to the void are deposited (<see cref="Deposit"/>): each is worth the currencies set on its
/// <see cref="PhysicalFactoryItem.currencyValue"/> (maybe none), and every deposit is counted by item id either way
/// (<see cref="Deposited"/>). All prices are paid from the currency banks; bought items go into the buyer's
/// inventory.
/// </para>
/// <para>
/// Every <see cref="PurchasableItem"/> definition is on sale from game start (<see cref="StockAllItems"/>). A currency
/// is discovered the first time any of it is gained, and stays discovered (a bank at 0 still shows). An item on sale
/// is only offered (<see cref="IsAvailable"/>) once it has a price and every currency in it has been discovered.
/// </para>
/// <para>
/// Multiplayer: the lobby host owns stock, banks and deposits. Only the host changes them, broadcasting each change to
/// every peer, itself included. Other players can only request purchases, which are paid for on the host (so two
/// players can't spend the same currency) and the items sent back to the buyer. A peer that joins a running game
/// gets the whole state with the world (<see cref="SaveManager"/>). Everything is cleared when the session ends.
/// </para>
/// <para>An autoload, so RPCs have a node to target; the API is static.</para>
/// </summary>
public partial class Shop : Node
{
    static Shop instance;

    /// <summary>Every currency's name and icon.</summary>
    public static readonly Dictionary<Currency, CurrencyInfo> Currencies = new()
    {
        [Currency.Scrap] = new("Scrap", GD.Load<Texture2D>("res://game/assets/icons/items/scrap_ball.png")),
        [Currency.MetalBar] = new("Metal Bar", GD.Load<Texture2D>("res://game/assets/icons/items/iron_ingot.png")),
    };

    static readonly List<string> stock = new();
    static readonly Dictionary<Currency, int> balances = new();
    static readonly Dictionary<string, int> deposited = new();

    /// <summary>Purchasable item ids on sale: every <see cref="PurchasableItem"/> definition, filled at game start.</summary>
    public static IReadOnlyList<string> Stock => stock;

    /// <summary>Raised whenever <see cref="Stock"/> changes.</summary>
    public static event Action StockChanged;

    /// <summary>The currency banks, by currency. Only discovered currencies have an entry.</summary>
    public static IReadOnlyDictionary<Currency, int> Balances => balances;

    /// <summary>Raised whenever a bank changes (including a currency being discovered).</summary>
    public static event Action BalancesChanged;

    /// <summary>Raised when a currency is discovered, which can make more of the stock available.</summary>
    public static event Action CurrencyDiscovered;

    /// <summary>How many of each item id have been turned in, currency value or not.</summary>
    public static IReadOnlyDictionary<string, int> Deposited => deposited;

    /// <summary>Raised on the buyer's peer when a purchase arrives: (item id, quantity).</summary>
    public static event Action<string, int> Purchased;

    public override void _Ready()
    {
        instance = this;
    }

    /// <summary>Empties stock, banks and deposits for the next game: they are static, so they would outlive this one.</summary>
    public static void ResetSession()
    {
        stock.Clear();
        balances.Clear();
        deposited.Clear();
        StockChanged?.Invoke();
        BalancesChanged?.Invoke();
    }

    // ---- stock ----------------------------------------------------------------

    const string DefinitionsRoot = "res://game/definitions";

    /// <summary>
    /// Puts every <see cref="PurchasableItem"/> definition under res://game/definitions on sale, replacing the stock.
    /// Each peer does this itself at game start: they all have the same definitions, so nothing is sent.
    /// </summary>
    public static void StockAllItems()
    {
        stock.Clear();
        StockItemsIn(DefinitionsRoot);
        StockChanged?.Invoke();
    }

    private static void StockItemsIn(string path)
    {
        using DirAccess dir = DirAccess.Open(path);
        if (dir == null)
        {
            return;
        }
        foreach (string sub in dir.GetDirectories())
        {
            StockItemsIn($"{path}/{sub}");
        }
        foreach (string file in dir.GetFiles())
        {
            // Exported builds list "x.tres.remap" instead of "x.tres".
            string name = file.TrimSuffix(".remap");
            if (!name.EndsWith(".tres")) continue;
            // Fetch also keeps the definition loaded (see ItemInfo).
            string itemID = name.TrimSuffix(".tres");
            if (ItemInfo.Fetch(itemID) is PurchasableItem)
            {
                stock.Add(itemID);
            }
        }
    }

    /// <summary>
    /// Host only: puts <paramref name="itemID"/> on sale for everyone. False (and nothing changes) for unknown or
    /// non-purchasable ids and for ids already on sale. Everything is stocked at game start, so this rarely adds anything.
    /// </summary>
    public static bool AddAvailableItem(string itemID)
    {
        if (stock.Contains(itemID)) return false;
        if (ItemInfo.Fetch(itemID) is not PurchasableItem)
        {
            Logging.Log($"can't stock '{itemID}': not a purchasable item", "Shop");
            return false;
        }
        RPCManager.RPC(instance, nameof(_AddStock), [itemID]);
        return true;
    }

    /// <summary>Host only: takes everything off sale for everyone.</summary>
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

    // ---- currencies -----------------------------------------------------------

    /// <summary>How much of <paramref name="currency"/> the players have to spend.</summary>
    public static int Balance(Currency currency) => balances.GetValueOrDefault(currency, 0);

    /// <summary>True once any of <paramref name="currency"/> has ever been gained.</summary>
    public static bool IsDiscovered(Currency currency) => balances.ContainsKey(currency);

    /// <summary>Host only: adds <paramref name="amount"/> of <paramref name="currency"/> to its bank for everyone (negative spends).</summary>
    public static void AddCurrency(Currency currency, int amount)
    {
        RPCManager.RPC(instance, nameof(_SetBalance), [(int)currency, Balance(currency) + amount]);
    }

    /// <summary>
    /// Host only: an item turned in to the void. Counts it in <see cref="Deposited"/> and adds what it's worth,
    /// <paramref name="value"/> (null or empty: nothing), to the banks.
    /// </summary>
    public static void Deposit(string itemID, IDictionary<Currency, int> value)
    {
        RPCManager.RPC(instance, nameof(_SetDeposited), [itemID, deposited.GetValueOrDefault(itemID, 0) + 1]);
        if (value == null) return;
        foreach (var (currency, amount) in value)
        {
            if (amount > 0)
            {
                AddCurrency(currency, amount);
            }
        }
    }

    // Any balance message discovers its currency.
    [RPC(requireAuthority = true)]
    private void _SetBalance(int currency, int amount)
    {
        bool discovered = !balances.ContainsKey((Currency)currency);
        balances[(Currency)currency] = amount;
        BalancesChanged?.Invoke();
        if (discovered)
        {
            CurrencyDiscovered?.Invoke();
        }
    }

    [RPC(requireAuthority = true)]
    private void _SetDeposited(string itemID, int count)
    {
        deposited[itemID] = count;
    }

    // ---- prices ---------------------------------------------------------------

    /// <summary>True when <paramref name="cost"/> takes anything.</summary>
    public static bool HasCost(Godot.Collections.Dictionary<Currency, int> cost) =>
        cost != null && cost.Values.Any(amount => amount > 0);

    /// <summary>True when <paramref name="cost"/> takes anything and every currency in it has been discovered (never, for a free price).</summary>
    public static bool IsAvailable(Godot.Collections.Dictionary<Currency, int> cost) =>
        HasCost(cost) && cost.All(c => c.Value <= 0 || IsDiscovered(c.Key));

    public static bool IsAvailable(PurchasableItem item) => IsAvailable(item.cost);

    /// <summary>True when the banks hold <paramref name="quantity"/> times <paramref name="cost"/>.</summary>
    public static bool CanAfford(Godot.Collections.Dictionary<Currency, int> cost, int quantity = 1) =>
        cost == null || cost.All(c => Balance(c.Key) >= c.Value * quantity);

    /// <summary>Host only: takes <paramref name="quantity"/> times <paramref name="cost"/> out of the banks. Check <see cref="CanAfford"/> first.</summary>
    public static void Spend(Godot.Collections.Dictionary<Currency, int> cost, int quantity = 1)
    {
        if (cost == null) return;
        foreach (var (currency, amount) in cost)
        {
            if (amount > 0)
            {
                AddCurrency(currency, -amount * quantity);
            }
        }
    }

    /// <summary>A price as text, e.g. "10 Scrap, 2 Metal Bar" ("Free" when it takes nothing).</summary>
    public static string CostText(Godot.Collections.Dictionary<Currency, int> cost, int quantity = 1)
    {
        if (!HasCost(cost)) return "Free";
        return string.Join(", ", cost.Where(c => c.Value > 0).Select(c => $"{c.Value * quantity} {Currencies[c.Key].name}"));
    }

    /// <summary>The largest order of <paramref name="item"/> that can be paid for, at least 1 and at most <paramref name="maxOrder"/>.</summary>
    public static int MaxAffordable(PurchasableItem item, int maxOrder)
    {
        if (!HasCost(item.cost))
        {
            return Math.Clamp(item.maxStackSize, 1, maxOrder);
        }
        int most = item.cost.Where(c => c.Value > 0).Min(c => Balance(c.Key) / c.Value);
        return Math.Clamp(most, 1, maxOrder);
    }

    // ---- purchasing -----------------------------------------------------------

    /// <summary>
    /// Orders <paramref name="quantity"/> of <paramref name="itemID"/> for the local player, cut down to what fits in
    /// <paramref name="inventory"/>. The host takes the payment and sends the items back (<see cref="Purchased"/>).
    /// False (and nothing is sent) if it isn't on sale or available, can't be afforded or nothing fits.
    /// </summary>
    public static bool TryPurchase(Inventory inventory, string itemID, int quantity)
    {
        if (inventory == null || !stock.Contains(itemID) || ItemInfo.Fetch(itemID) is not PurchasableItem item) return false;
        if (!IsAvailable(item)) return false;

        quantity = Math.Min(quantity, inventory.RoomFor(itemID));
        if (quantity < 1 || !CanAfford(item.cost, quantity)) return false;

        RPCManager.RPCTo(Lobby.hostID, instance, nameof(_RequestPurchase), [itemID, quantity]);
        return true;
    }

    // Host (only ever sent there): checks the order against its own banks (first request wins), takes the payment, sends the items.
    [RPC]
    private void _RequestPurchase(string itemID, int quantity)
    {
        if (quantity < 1 || !stock.Contains(itemID) || ItemInfo.Fetch(itemID) is not PurchasableItem item) return;
        if (!IsAvailable(item) || !CanAfford(item.cost, quantity))
        {
            Logging.Log($"purchase of {quantity}x {itemID} by {RPCManager.sender} refused: unavailable or can't afford it", "Shop");
            return;
        }
        Spend(item.cost, quantity);
        RPCManager.RPCTo(RPCManager.sender, this, nameof(_ReceivePurchase), [itemID, quantity]);
    }

    // Buyer: the items go into the local player's inventory (TryPurchase already cut the order to what fits).
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

    // ---- saves ------------------------------------------------------------------

    /// <summary>
    /// Replaces banks and deposits with a restored save's (see <see cref="SaveManager"/>). The stock is every
    /// purchasable item again, plus anything else the save had on sale. Banks are saved by currency name, so
    /// reordering <see cref="Currency"/> doesn't scramble them; unknown names are dropped. Runs on each restoring
    /// peer, all with the host's data, so they stay in agreement.
    /// </summary>
    public static void LoadSave(List<string> savedStock, Dictionary<string, int> savedBalances, Dictionary<string, int> savedDeposits)
    {
        StockAllItems();
        foreach (string itemID in savedStock)
        {
            if (!stock.Contains(itemID) && ItemInfo.Fetch(itemID) is PurchasableItem)
            {
                stock.Add(itemID);
            }
        }
        balances.Clear();
        foreach (var (name, amount) in savedBalances)
        {
            if (Enum.TryParse(name, out Currency currency))
            {
                balances[currency] = amount;
            }
        }
        deposited.Clear();
        foreach (var (itemID, count) in savedDeposits)
        {
            deposited[itemID] = count;
        }
        StockChanged?.Invoke();
        BalancesChanged?.Invoke();
        CurrencyDiscovered?.Invoke();
    }

    /// <summary>The banks keyed by currency name, for a save.</summary>
    public static Dictionary<string, int> SaveBalances() => balances.ToDictionary(b => b.Key.ToString(), b => b.Value);
}
