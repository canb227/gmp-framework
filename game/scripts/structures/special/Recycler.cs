using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using PolyType;

[GenerateShape]
public partial record struct RecyclerSave
{
    public int points;
    public double untilBase;
    public int upgradeLevel;
}

/// <summary>
/// Spawns a free item at its first output every <see cref="baseInterval"/>. On top of that it eats items from its
/// valley (oldest first) for points, and each cycle spends a point on a bonus item at its second output.
/// </summary>
public partial class Recycler : IOMachine
{
    /// <summary>Item ids to spawn and their relative weights.</summary>
    [Export] public Godot.Collections.Dictionary<string, float> itemWeights = new() { { "scrap_ball", 1f } };

    /// <summary>Points each eaten item is worth, by item id; anything not listed is worth 1.</summary>
    [Export] public Godot.Collections.Dictionary<string, int> itemPoints = new();

    /// <summary>Seconds between the free items it always spawns.</summary>
    [Export] public double baseInterval = 1.0;

    int points;
    double untilBase;
    int upgradeLevel = 0;
    List<float> upgradeSpeedUp = new();
    List<Godot.Collections.Dictionary<Currency, int>> upgradeCosts = new();
    
    static string FormatCost(Godot.Collections.Dictionary<Currency, int> cost)
    {
        return string.Join(", ", cost.Select(c =>
            $"{c.Value:N0} {Regex.Replace(c.Key.ToString(), "(?<=[a-z])(?=[A-Z])", " ")}"));
    }


    public override void AfterInit()
    {
        upgradeCosts.Add(new Godot.Collections.Dictionary<Currency, int> { { Currency.MetalBar, 100 }}); //upgrade 1 cost
        upgradeCosts.Add(new Godot.Collections.Dictionary<Currency, int> {{ Currency.MetalBar, 400 }}); //upgrade 2 cost
        upgradeCosts.Add(new Godot.Collections.Dictionary<Currency, int> {{ Currency.MetalBar, 2000 }, {Currency.Circuits, 100}}); //upgrade 3 cost

        upgradeSpeedUp.Add(1.0f); //no upgrades
        upgradeSpeedUp.Add(4.0f); //upgrade 1
        upgradeSpeedUp.Add(8.0f); //upgrade 2
        upgradeSpeedUp.Add(16.0f); //upgrade 3

        if (upgradeLevel < upgradeCosts.Count)
            hoverText = "Feed it - get scrap\nPress F to Upgrade (Cost: " + FormatCost(upgradeCosts[upgradeLevel]) + ")";
        else
            hoverText = "Feed it - get scrap\n(Fully upgraded)";
        base.AfterInit();
    }

    public override void onInteract(ulong playerID)
    {
        bool canUpgrade = true;
        foreach(var cost in upgradeCosts[upgradeLevel])
        {
            if(Shop.Balance(cost.Key) < cost.Value)
            {
                canUpgrade = false;
            }
        }
        if(canUpgrade)
        {
            Shop.Spend(upgradeCosts[upgradeLevel], 1);
            upgradeLevel++;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (runsMachineLogic)
        {
            untilBase -= delta * processingSpeed * upgradeSpeedUp[upgradeLevel];
            if (untilBase <= 0)
            {
                untilBase += baseInterval;
                SpawnOne(0);
            }
        }
        base._PhysicsProcess(delta);
    }

    protected override bool TryStartProcess()
    {
        if (inputItems.Count == 0) return false;
        PhysicalFactoryItem ready = inputItems[0];
        if (itemPoints.TryGetValue(ready.itemID ?? "", out int p))
        {
            if(points < int.MaxValue-10000)
            {
                points += p;
            }
        }
        else
        {
            if(points < int.MaxValue-10000)
            {
                points += 1;
            }
        }
        ConsumeInputItem(ready);

        if (points <= 0) return false;
        points--;
        //animationPlayer.Play("process");
        processTimeRemaining = 1f;
        return true;
    }

    protected override void FinishProcess()
    {
        SpawnOne(1);
    }

    protected override byte[] SaveMachineState()
    {
        return GMPObject.serializer.Serialize(new RecyclerSave { points = points, untilBase = untilBase, upgradeLevel = upgradeLevel });
    }

    protected override void LoadMachineState(byte[] state)
    {
        RecyclerSave s = GMPObject.serializer.Deserialize<RecyclerSave>(state);
        points = s.points;
        untilBase = s.untilBase;
        upgradeLevel = s.upgradeLevel;
    }

    // Spawns one item at the output'th output (0: the regular stream, 1: the bonus items).
    void SpawnOne(int output)
    {
        string itemID = PickWeighted(itemWeights);
        if (itemID == null) return;

        Vector3 rotVal = new Vector3(
            (float)GD.RandRange(0, Mathf.Pi * 2),
            (float)GD.RandRange(0, Mathf.Pi * 2),
            (float)GD.RandRange(0, Mathf.Pi * 2)
        );
        ItemInfo.SpawnInWorld(itemID, OutputPosition(output), rotVal);
    }

    /// <summary>Picks a key at random in proportion to its weight; null if no key has a positive weight.</summary>
    public static string PickWeighted(Godot.Collections.Dictionary<string, float> itemWeights)
    {
        float total = 0;
        foreach (float w in itemWeights.Values) total += Mathf.Max(0f, w);
        if (total <= 0) return null;
        float roll = (float)System.Random.Shared.NextDouble() * total;
        string last = null;
        foreach (var (id, w) in itemWeights)
        {
            if (w <= 0) continue;
            last = id;
            roll -= w;
            if (roll < 0) return id;
        }
        return last; // rounding at the very top of the range
    }
}
