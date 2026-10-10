using Godot;

/// <summary>
/// A <see cref="BasicButton"/> whose presses cost <see cref="costAmount"/> of the Shop resource <see cref="costItemID"/>.
/// The host takes the payment when it accepts a press; a press the players can't pay for is refused.
/// </summary>
public partial class PricedButton : BasicButton
{
    /// <summary>Item id of the Shop resource the press is paid in.</summary>
    [Export] public string costItemID;
    [Export] public int costAmount = 1;

    public override void _EnterTree()
    {
        Shop.ResourcesChanged += OnResourcesChanged;
    }

    public override void _ExitTree()
    {
        Shop.ResourcesChanged -= OnResourcesChanged;
    }

    void OnResourcesChanged(string itemID, int amount) => UpdateHoverText();

    protected override void UpdateHoverText()
    {
        base.UpdateHoverText();
        string currency = ItemInfo.Fetch(costItemID)?.displayName ?? costItemID;
        hoverText += $" Costs {costAmount} {currency} (have {Shop.HeldCurrency(costItemID)}).";
    }

    public override bool TryAccept(ulong presser)
    {
        if (Shop.HeldCurrency(costItemID) < costAmount) return false;
        Shop.AddResource(costItemID, -costAmount);
        return true;
    }
}
