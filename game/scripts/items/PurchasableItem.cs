using Godot;

/// <summary>
/// An item the shop sells. Every definition of one under res://game/definitions is stocked at game start
/// (<see cref="Shop.StockAllItems"/>), and offered once it has a price and every currency in it has been discovered.
/// Bought ones go into the buyer's inventory.
/// </summary>
[GlobalClass]
public abstract partial class PurchasableItem : ItemInfo
{
    /// <summary>
    /// What one of these costs in the shop: any amount of any number of currencies. It is only offered once every
    /// currency here has been discovered, and never while this is empty.
    /// </summary>
    [Export]
    public Godot.Collections.Dictionary<Currency, int> cost = new();
}
