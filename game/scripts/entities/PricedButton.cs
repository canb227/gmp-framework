using Godot;
using System.Linq;

/// <summary>
/// A <see cref="BasicButton"/> whose presses cost <see cref="cost"/>, paid from the Shop's currency banks.
/// The host takes the payment when it accepts a press; a press the players can't pay for is refused.
/// </summary>
public partial class PricedButton : BasicButton
{
    /// <summary>What a press costs: any amount of any number of currencies.</summary>
    [Export] public Godot.Collections.Dictionary<Currency, int> cost = new();

    public override void _EnterTree()
    {
        Shop.BalancesChanged += UpdateHoverText;
    }

    public override void _ExitTree()
    {
        Shop.BalancesChanged -= UpdateHoverText;
    }

    protected override void UpdateHoverText()
    {
        base.UpdateHoverText();
        string have = string.Join(", ", cost.Keys.Select(c => $"{Shop.Balance(c)} {Shop.Currencies[c].name}"));
        hoverText += $" Costs {Shop.CostText(cost)} (have {have}).";
    }

    public override bool TryAccept(ulong presser)
    {
        if (!HasLiveTarget()) return false; // e.g. a broken structure already repaired: don't charge for nothing
        if (!Shop.CanAfford(cost)) return false;
        Shop.Spend(cost);
        return true;
    }

    bool HasLiveTarget()
    {
        foreach (Node target in targets)
        {
            if (IsInstanceValid(target) && !target.IsQueuedForDeletion()) return true;
        }
        return false;
    }
}
