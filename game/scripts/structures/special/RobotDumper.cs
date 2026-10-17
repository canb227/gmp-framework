using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using PolyType;

[GenerateShape]
public partial record struct RobotDumperSave
{
    public int points;
    public double untilBase;
    public int upgradeLevel;
}

/// <summary>
/// Spawns a free item at its first output every <see cref="baseInterval"/>. On top of that it eats items from its
/// valley (oldest first) for points, and each cycle spends a point on a bonus item at its second output.
/// </summary>
public partial class RobotDumper : IOMachine
{
    /// <summary>Item ids to spawn and their relative weights.</summary>
    [Export] public Godot.Collections.Dictionary<string, float> itemWeights = new() { { "robot_parts", 1f } };


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
        upgradeCosts.Add(new Godot.Collections.Dictionary<Currency, int> { { Currency.Circuits, 100 }}); //upgrade 1 cost
        upgradeCosts.Add(new Godot.Collections.Dictionary<Currency, int> {{ Currency.Circuits, 400 }}); //upgrade 2 cost
        upgradeCosts.Add(new Godot.Collections.Dictionary<Currency, int> {{ Currency.Circuits, 2000 }, {Currency.MetalBar, 8000}}); //upgrade 3 cost

        upgradeSpeedUp.Add(1.0f); //no upgrades
        upgradeSpeedUp.Add(4.0f); //upgrade 1
        upgradeSpeedUp.Add(8.0f); //upgrade 2
        upgradeSpeedUp.Add(16.0f); //upgrade 3

        if (upgradeLevel < upgradeCosts.Count)
            hoverText = "Press F to Upgrade (Cost: " + FormatCost(upgradeCosts[upgradeLevel]) + ")";
        else
            hoverText = "(Fully upgraded)";
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

    int index = 0;
    public override void _PhysicsProcess(double delta)
    {
        if (runsMachineLogic)
        {
            untilBase -= delta * processingSpeed * upgradeSpeedUp[upgradeLevel];
            if (untilBase <= 0)
            {
                untilBase += baseInterval;
                SpawnOne(index);
                index++;
                if(index > 2)
                {
                    index = 0;
                }
            }
        }
        base._PhysicsProcess(delta);
    }

    protected override bool TryStartProcess()
    {
        return false;
    }

    protected override void FinishProcess()
    {
        
    }

    protected override byte[] SaveMachineState()
    {
        return GMPObject.serializer.Serialize(new RobotDumperSave { points = points, untilBase = untilBase, upgradeLevel = upgradeLevel });
    }

    protected override void LoadMachineState(byte[] state)
    {
        RobotDumperSave s = GMPObject.serializer.Deserialize<RobotDumperSave>(state);
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
