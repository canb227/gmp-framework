using Godot;

/// <summary>
/// Spawns one of the items in <see cref="itemWeights"/> (picked at random, in proportion to its weight) every <see cref="interval"/> seconds,
/// forever, just in front of itself at belt height, so it lands on a conveyor placed there. Runs on the
/// spawner's authority, which spawns for everyone (and alone makes the random picks).
/// It doesn't check whether the output is clear, so a stopped line will pile up in front of it.
/// </summary>
public partial class ItemSpawner : Structure
{
    /// <summary>
    /// itemID → relative weight. Each spawn picks one item with probability weight / total weight, so
    /// {iron_ore: 3, copper_ore: 1} spawns iron three times as often. Zero or negative weights never spawn.
    /// </summary>
    [Export] public Godot.Collections.Dictionary<string, float> itemWeights = new() { { "test_1x1x1cube", 1f } };
    /// <summary>Seconds between spawns.</summary>
    [Export] public double interval = 1.0;
    /// <summary>Where items appear, relative to the spawner (in front of it, at belt height).</summary>
    [Export] public Vector3 outputPoint = new(0, -0.3f, -1.6f);

    /// <summary>Items spawned so far on this peer (used by the headless multiplayer test).</summary>
    public int spawnedCount { get; private set; }

    double untilNextSpawn;

    public override void _Ready()
    {
        untilNextSpawn = interval; // first item one interval after the spawner appears
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic || interval <= 0) return;
        untilNextSpawn -= delta;
        if (untilNextSpawn > 0) return;
        untilNextSpawn += interval;
        string itemID = PickWeighted(itemWeights);
        if (itemID == null) return;
        FactoryItem.SpawnInWorld(itemID, GlobalTransform * outputPoint, GlobalRotation);
        spawnedCount++;
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
