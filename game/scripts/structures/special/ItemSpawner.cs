using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Godot;
using PolyType;

[GenerateShape]
public partial record struct ItemSpawnerSave
{
    public double untilSpawn;
    public int upgradeLevel;
    public int nextStreamOutput;
    /// <summary>Whatever a subclass adds (<see cref="ItemSpawner.SaveSpawnerState"/>), or null.</summary>
    public byte[] extra;
}

/// <summary>
/// Spawns a free item every <see cref="spawnInterval"/> seconds, taking turns between its <see cref="streamOutputs"/>,
/// picked from <see cref="itemWeights"/>. Players can buy upgrades that speed it up (press F).
/// <para>
/// Before each spawn it checks its limits: how many of that item exist, how many items exist in all, and how many
/// items are near the output. While any limit is reached it holds, and spawns as soon as there's room again.
/// <see cref="TrySpawn"/> applies the same limits to anything a subclass spawns.
/// </para>
/// </summary>
public partial class ItemSpawner : IOMachine
{
    /// <summary>Item ids to spawn and their relative weights.</summary>
    [Export] public Godot.Collections.Dictionary<string, float> itemWeights = new();

    /// <summary>Seconds between the free items it spawns, before upgrades.</summary>
    [Export] public double spawnInterval = 1.0;

    /// <summary>The outputs (indices into <see cref="IOMachine.outputLocations"/>) the free items take turns between.</summary>
    [Export] public Godot.Collections.Array<int> streamOutputs = new() { 0 };

    /// <summary>Shown above the upgrade line when looked at (e.g. what to feed it).</summary>
    [Export] public string description = "";

    [ExportGroup("Upgrades")]
    /// <summary>What each upgrade costs, in order.</summary>
    [Export] public Godot.Collections.Array<Godot.Collections.Dictionary<Currency, int>> upgradeCosts = new();
    /// <summary>How many times faster it spawns after each upgrade, in order (one per entry in <see cref="upgradeCosts"/>).</summary>
    [Export] public Godot.Collections.Array<float> upgradeSpeedUps = new();

    [ExportGroup("Limits")]
    /// <summary>Holds while this many of the item it's about to spawn exist anywhere (0: no limit).</summary>
    [Export] public int maxOfItem = 0;
    /// <summary>Holds while this many items of any kind exist anywhere (0: no limit).</summary>
    [Export] public int maxTotalItems = 0;
    /// <summary>Holds while this many items are within <see cref="nearbyRadius"/> of the output (0: no limit).</summary>
    [Export] public int maxNearby = 0;
    /// <summary>Metres around the output that <see cref="maxNearby"/> counts.</summary>
    [Export] public float nearbyRadius = 3f;

    double untilSpawn;
    int upgradeLevel;
    int nextStreamOutput;

    /// <summary>Reused by <see cref="CountNearby"/>, which may see a body once per shape.</summary>
    static readonly HashSet<PhysicalFactoryItem> nearbyScratch = new();

    public override void AfterInit()
    {
        UpdateHoverText();
        base.AfterInit();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (runsMachineLogic && streamOutputs.Count > 0)
        {
            // Held at 0 while a limit is reached, so it spawns the moment there's room (and no more than one at once).
            if (untilSpawn > 0)
            {
                untilSpawn -= delta * processingSpeed * SpeedUp();
            }
            if (untilSpawn <= 0)
            {
                string itemID = PickWeighted(itemWeights);
                int output = streamOutputs[nextStreamOutput % streamOutputs.Count];
                if (TrySpawn(itemID, output))
                {
                    untilSpawn += spawnInterval;
                    nextStreamOutput = (nextStreamOutput + 1) % streamOutputs.Count;
                }
            }
        }
        base._PhysicsProcess(delta);
    }

    // A plain spawner takes nothing in; subclasses that do (the Recycler) override these.
    protected override bool TryStartProcess()
    {
        return false;
    }

    protected override void FinishProcess()
    {
    }

    // ---- spawning ----

    /// <summary>
    /// Authority only: spawns <paramref name="itemID"/> at the output'th output, at a random rotation, unless one of
    /// the limits is reached. Returns whether it spawned.
    /// </summary>
    protected bool TrySpawn(string itemID, int output)
    {
        if (itemID == null)
        {
            return false;
        }
        if (maxOfItem > 0 && PhysicalFactoryItem.CountOf(itemID) >= maxOfItem)
        {
            Hold($"{maxOfItem} {itemID} in the world");
            return false;
        }
        if (maxTotalItems > 0 && PhysicalFactoryItem.totalCount >= maxTotalItems)
        {
            Hold($"{maxTotalItems} items in the world");
            return false;
        }
        Vector3 position = OutputPosition(output);
        if (maxNearby > 0 && CountNearby(position) >= maxNearby)
        {
            Hold($"{maxNearby} items within {nearbyRadius} m of an output");
            return false;
        }
        holdingFor = null;

        Vector3 rotation = new Vector3(
            (float)GD.RandRange(0, Mathf.Pi * 2),
            (float)GD.RandRange(0, Mathf.Pi * 2),
            (float)GD.RandRange(0, Mathf.Pi * 2)
        );
        ItemInfo.SpawnInWorld(itemID, position, rotation);
        return true;
    }

    /// <summary>The limit it's holding for (null: not holding), so each hold is logged once, for tuning the limits.</summary>
    string holdingFor;

    void Hold(string limit)
    {
        if (holdingFor != limit)
        {
            holdingFor = limit;
            Logging.Log($"{Name} holding: {limit}", "ItemSpawner");
        }
    }

    /// <summary>How many items are within <see cref="nearbyRadius"/> of <paramref name="position"/>.</summary>
    int CountNearby(Vector3 position)
    {
        nearbyScratch.Clear();
        foreach (Node3D node in GameWorld.OverlapSphere(position, nearbyRadius, (int)~GameWorld.QueryHiddenLayer))
        {
            if (node is PhysicalFactoryItem item)
            {
                nearbyScratch.Add(item);
            }
        }
        return nearbyScratch.Count;
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

    // ---- upgrades ----

    /// <summary>How many times faster than <see cref="spawnInterval"/> it runs at its current upgrade level.</summary>
    float SpeedUp()
    {
        if (upgradeLevel == 0 || upgradeLevel > upgradeSpeedUps.Count)
        {
            return 1f;
        }
        return upgradeSpeedUps[upgradeLevel - 1];
    }

    // Spending is the host's to do (it owns the banks), so the host decides every upgrade.
    public override void onInteract(ulong playerID)
    {
        if (upgradeLevel >= upgradeCosts.Count)
        {
            return;
        }
        RPCManager.RPCTo(Lobby.hostID, this, nameof(RequestUpgrade), [upgradeLevel]);
    }

    /// <summary>Host: buys the upgrade after <paramref name="fromLevel"/>, if nobody bought it first and the banks can pay.</summary>
    [RPC]
    void RequestUpgrade(int fromLevel)
    {
        if (!Lobby.isHost || fromLevel != upgradeLevel || upgradeLevel >= upgradeCosts.Count)
        {
            return;
        }
        if (!Shop.CanAfford(upgradeCosts[upgradeLevel]))
        {
            return;
        }
        Shop.Spend(upgradeCosts[upgradeLevel]);
        RPCManager.RPC(this, nameof(SetUpgradeLevel), [upgradeLevel + 1]);
    }

    [RPC]
    void SetUpgradeLevel(int level)
    {
        if (RPCManager.sender != Lobby.hostID)
        {
            return;
        }
        upgradeLevel = level;
        UpdateHoverText();
    }

    void UpdateHoverText()
    {
        string upgradeLine;
        if (upgradeLevel < upgradeCosts.Count)
        {
            upgradeLine = "Press F to Upgrade (Cost: " + FormatCost(upgradeCosts[upgradeLevel]) + ")";
        }
        else if (upgradeCosts.Count > 0)
        {
            upgradeLine = "(Fully upgraded)";
        }
        else
        {
            upgradeLine = "";
        }

        if (string.IsNullOrEmpty(description))
        {
            hoverText = upgradeLine;
        }
        else
        {
            hoverText = description + "\n" + upgradeLine;
        }
    }

    static string FormatCost(Godot.Collections.Dictionary<Currency, int> cost)
    {
        return string.Join(", ", cost.Select(c =>
            $"{c.Value:N0} {Regex.Replace(c.Key.ToString(), "(?<=[a-z])(?=[A-Z])", " ")}"));
    }

    // ---- saving ----

    /// <summary>Extra state a subclass saves on top of the spawner's, or null.</summary>
    protected virtual byte[] SaveSpawnerState() { return null; }

    /// <summary>Reads back what <see cref="SaveSpawnerState"/> returned (never called with null). Must be silent.</summary>
    protected virtual void LoadSpawnerState(byte[] state) { }

    protected override byte[] SaveMachineState()
    {
        return GMPObject.serializer.Serialize(new ItemSpawnerSave
        {
            untilSpawn = untilSpawn,
            upgradeLevel = upgradeLevel,
            nextStreamOutput = nextStreamOutput,
            extra = SaveSpawnerState(),
        });
    }

    protected override void LoadMachineState(byte[] state)
    {
        ItemSpawnerSave s = GMPObject.serializer.Deserialize<ItemSpawnerSave>(state);
        untilSpawn = s.untilSpawn;
        upgradeLevel = s.upgradeLevel;
        nextStreamOutput = s.nextStreamOutput;
        if (s.extra != null) LoadSpawnerState(s.extra);
        UpdateHoverText();
    }
}
