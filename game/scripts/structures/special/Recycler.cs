using Godot;
using PolyType;
using System;
using System.Collections.Generic;
using System.Linq;

[GenerateShape]
public partial record struct RecyclerSave
{
    public int points;
    public double untilBase;
    public double untilBonus;
}


public partial class Recycler : Structure, IOMachine
{
    /// <summary>Item ids to spawn and their relative weights.</summary>
    [Export] public Godot.Collections.Dictionary<string, float> itemWeights = new() { { "scrap_ball", 1f } };

    /// <summary>Points each eaten item is worth, by item id; anything not listed is worth 1.</summary>
    [Export] public Godot.Collections.Dictionary<string, int> itemPoints = new();

    [Export] public Godot.Collections.Array<Node3D> outputLocations = new();

    [Export] public Godot.Collections.Array<GMPOBox3DBody> inputVolumes = new();

    /// <summary>Seconds between the free items it always spawns.</summary>
    [Export] public double baseInterval = 1.0;

    /// <summary>Seconds between bonus items paid for with points.</summary>
    [Export] public double bonusInterval = 1.0;

    bool _isOperating;
    int points;
    double untilBase;
    double untilBonus;
    // When each item now in the valley was first seen there (authority only, not saved: a load restarts the dwell).
    Dictionary<ulong, ulong> firstSeen = new();



    public override void AfterInit()
    {
        hoverText = "Feed it - get scrap";
        base.AfterInit();

    }

    public override void onInteract(ulong playerID) { }

    public override byte[] SaveState()
    {
        return GMPObject.serializer.Serialize(new RecyclerSave { points = points, untilBase = untilBase, untilBonus = untilBonus });
    }

    public override void LoadState(byte[] state)
    {
        RecyclerSave s = GMPObject.serializer.Deserialize<RecyclerSave>(state);
        points = s.points;
        untilBase = s.untilBase;
        untilBonus = s.untilBonus;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (isOperating())
        {
            //DoLocalOperationEffects();
        }
        if (!runsMachineLogic) return;

        untilBase -= delta;
        if (untilBase <= 0)
        {
            untilBase += baseInterval;
            SpawnOne(0);
        }

        PhysicalFactoryItem ready = OldestReady();
        // The bonus clock waits at zero until there's a point to spend.
        untilBonus = Mathf.Max(0, untilBonus - delta);
        if (untilBonus > 0) return;
        if (points == 0 && ready != null)
        {
            points += itemPoints.TryGetValue(ready.itemID ?? "", out int p) ? p : 1;
            firstSeen.Remove(ready.id);
            GameWorld.DespawnObject(ready.id, DespawnReason.Consumed);
        }
        if (points > 0)
        {
            points--;
            SpawnOne(1);
            untilBonus = bonusInterval;
        }
    }


    PhysicalFactoryItem OldestReady()
    {
        ulong now = Time.GetTicksMsec();
        Dictionary<ulong, ulong> seen = new();
        PhysicalFactoryItem oldest = null;
        ulong oldestSince = ulong.MaxValue;
        foreach (PhysicalFactoryItem item in getBodiesInInputs())
        {
            if (GameWorld.heldBy.ContainsKey(item.id)) continue;
            ulong since = firstSeen.TryGetValue(item.id, out ulong t) ? t : now;
            seen[item.id] = since;
            if (since < oldestSince)
            {
                oldest = item;
                oldestSince = since;
            }
        }
        firstSeen = seen;
        return oldest;
    }

    // Spawns one item at the output'th output port (0: the regular stream, 1: the bonus items).
    void SpawnOne(int output)
    {
        string itemID = PickWeighted(itemWeights);
        if (itemID == null) return;
        ItemInfo.SpawnInWorld(itemID, OutputPosition(output), GlobalRotation);

    }

    private Vector3 OutputPosition(int output)
    {
        return Vector3.Zero;
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

    public bool isOperating()
    {
        return _isOperating;
    }

    public Godot.Collections.Array<PhysicalFactoryItem> getBodiesInInputs()
    {
        Godot.Collections.Array<PhysicalFactoryItem> items = new();
        foreach (var volume in inputVolumes)
        {
            items.AddRange(volume.GetOverlappingBodies().Where(b => b is PhysicalFactoryItem).Cast<PhysicalFactoryItem>().ToArray());
        }
        return items;
    }

    public Godot.Collections.Array<Node3D> getOutputLocations()
    {
        return outputLocations;
    }
}
