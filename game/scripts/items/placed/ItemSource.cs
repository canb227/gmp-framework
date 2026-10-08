using Godot;

/// <summary>A stock of items for a <see cref="ScrapArm"/> to take from. A count of -1 means unlimited.</summary>
public partial class ItemSource : Structure
{
    [Export]
    public Godot.Collections.Dictionary<string,float> itemWeights;

    [Export]
    public Godot.Collections.Dictionary<string, int> itemCounts;

    [Export]
    public Node3D spawnLocation;

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