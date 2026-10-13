using Godot;
using PolyType;
using System;
using System.Collections.Generic;

[GenerateShape]
public partial record struct SmelterSave
{
    public string itemInSmelter;
}

/// <summary>Takes in one item with a recipe per cycle, picked at random from its inputs, and puts out the recipe's result.</summary>
public partial class BasicSmelter : Machine
{
    /// <summary>Input itemID → output itemID.</summary>
    public Dictionary<Dictionary<string, int>, Dictionary<string, int>> recipes = new();
    public Dictionary<string, int> content = new();


    /// <summary>Raised with the id of each input item the smelter takes in (only on the peer running its machine logic).</summary>
    public event Action<ulong> Smelted;

    /// <summary>The item being smelted (authority only).</summary>
    KeyValuePair<Dictionary<string, int>, Dictionary<string, int>> recipeInSmelter;

    protected override bool TryStartProcess() //JEFF -> STEPHEN TODO: This should probably only be called when a new item enters the input area not every tick
    {
        List<PhysicalFactoryItem> items = ItemsInInputs();
        if (items.Count == 0) return false;
        content.Clear();
        //get a full count of the items inside
        foreach(PhysicalFactoryItem item in items)
        {
            if(content.ContainsKey(item.itemID))
            {
                content[item.itemID]++;
            }
            else
            {
                content[item.itemID] = 1;
            }
        }

        //check for if we have a matching recipe based on the contents
        bool recipeMatch;
        foreach(var recipe in recipes)
        {
            recipeMatch = true;
            Dictionary<string, int> inputs = recipe.Key;
            foreach(var input in inputs)
            {
                if(content.ContainsKey(input.Key))
                {
                    if(content[input.Key] < input.Value)
                    {
                        recipeMatch = false;
                    }
                }
            }
            if(recipeMatch)
            {
                recipeInSmelter = recipe;
                break;
            }
        }

        //we should be assured that the full recipe contents are present so remove all of them, leaving leftovers
        content.Clear();
        content = recipeInSmelter.Key;
        foreach(PhysicalFactoryItem item in items)
        {
            if(content.ContainsKey(item.itemID) && content[item.itemID] > 0)
            {
                content[item.itemID]--;
                GameWorld.DespawnObject(item.id, DespawnReason.Consumed);
                Smelted?.Invoke(item.id);
            }
        }

        return true;
    }

    protected override void FinishProcess()
    {
        int outputIndex = 0;
        foreach(var outputItem in recipes.Values) //need to loop on sub items and get the counts to spawn items
        {
            ItemInfo.SpawnInWorld(outputItem.Keys, OutputPosition(outputIndex), GlobalRotation);
            outputIndex++;
        }

        recipeInSmelter = null;
    }

    protected override byte[] SaveMachineState()
    {
        if (itemInSmelter == null) return null; //need to store the progress and recipe, and stuff
        return GMPObject.serializer.Serialize(new SmelterSave { itemInSmelter = itemInSmelter });
    }

    protected override void LoadMachineState(byte[] state)
    {
        itemInSmelter = GMPObject.serializer.Deserialize<SmelterSave>(state).itemInSmelter;
    }
}
