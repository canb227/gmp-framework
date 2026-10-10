using Godot;
using PolyType;
using System.Collections.Generic;

/// <summary>What a save keeps of an <see cref="ItemSource"/>: how much of each item is left.</summary>
[GenerateShape]
public partial record struct ItemSourceSave
{
    public Dictionary<string, int> itemCounts;
}

/// <summary>A stock of items for a <see cref="ScrapArm"/> to take from. A count of -1 means unlimited.</summary>
public partial class ItemSource : Structure
{
    [Export]
    public Godot.Collections.Dictionary<string,float> itemWeights;

    [Export]
    public Godot.Collections.Dictionary<string, int> itemCounts;

    [Export]
    public Node3D spawnLocation;

    public override byte[] SaveState()
    {
        if (itemCounts == null) return null;
        Dictionary<string, int> counts = new();
        foreach (var (item, count) in itemCounts)
        {
            counts[item] = count;
        }
        return GMPObject.serializer.Serialize(new ItemSourceSave { itemCounts = counts });
    }

    public override void LoadState(byte[] state)
    {
        foreach (var (item, count) in GMPObject.serializer.Deserialize<ItemSourceSave>(state).itemCounts)
        {
            itemCounts[item] = count;
        }
    }

    public string GetItem()
    {
        string item = ItemSpawner.PickWeighted(itemWeights);
        if (itemCounts[item] == 0) return null;
        ConsumeOne(item);
        return item;
    }

    // A count of -1 means unlimited and is never decremented.
    void ConsumeOne(string item)
    {
        if (itemCounts[item] > 0)
        {
            itemCounts[item]--;
        }
    }
}