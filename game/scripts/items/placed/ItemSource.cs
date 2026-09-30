using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public partial class ItemSource : Structure
{
    [Export]
    public Godot.Collections.Dictionary<string,float> itemWeights;

    [Export]
    public Godot.Collections.Dictionary<string, int> itemCounts;

    [Export]
    public Node3D spawnLocation;

    public void SpawnItem()
    {
        string item = ItemSpawner.PickWeighted(itemWeights);
        if (itemCounts[item] > 0 || itemCounts[item] == -1)
        {
            ConsumeOne(item);
            GameWorld.SpawnScene(ItemInfo.Fetch(item).droppedScene, spawnLocation.GlobalPosition);
        }
        else if (itemCounts[item] == 0)
        {
            //no mor
        }


    }

    public string GetItem()
    {
        //Logging.Log("attmpting get item", "ItemSource");
        string item = ItemSpawner.PickWeighted(itemWeights);
        if (itemCounts[item] > 0 || itemCounts[item] == -1)
        {
            ConsumeOne(item);
            return item;
        }
        else
        {
            //no mor
            return null;
        }
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