using Godot;
using PolyType;
using System.Collections.Generic;

[GenerateShape]
public partial record struct GrinderSave
{
    public string itemInGrinder;
}

/// <summary>
/// A machine that grinds what reaches its inputs, one item per cycle: each comes out transformed (<see cref="recipes"/>,
/// <see cref="outputCounts"/> of them side by side) at its output (<see cref="Machine.OutputPosition"/>), placed so
/// they land on a conveyor there. Items without a recipe come out unchanged.
/// <para>
/// Known gap: an item grabbed or picked up at the moment it enters the hopper is decided separately by
/// its own authority, so in that race it could be both kept and ground.
/// </para>
/// </summary>
public partial class Grinder : Machine
{
    /// <summary>Input itemID → output itemID.</summary>
    [Export] public Godot.Collections.Dictionary<string, string> recipes = new();
    /// <summary>Input itemID → how many outputs it makes (default 1).</summary>
    [Export] public Godot.Collections.Dictionary<string, int> outputCounts = new();
    /// <summary>Sideways spacing between outputs of the same item (m).</summary>
    [Export] public float outputSpacing = 0.6f;

    /// <summary>The item being ground (authority only).</summary>
    string itemInGrinder;

    protected override bool TryStartProcess()
    {
        List<PhysicalFactoryItem> items = ItemsInInputs();
        if (items.Count == 0) return false;

        PhysicalFactoryItem item = items[0];
        itemInGrinder = item.itemID ?? "";
        GameWorld.DespawnObject(item.id, DespawnReason.Consumed);
        return true;
    }

    protected override void FinishProcess()
    {
        string output = itemInGrinder;
        if (recipes.TryGetValue(itemInGrinder, out string recipeOutput))
        {
            output = recipeOutput;
        }
        int count = 1;
        if (outputCounts.TryGetValue(itemInGrinder, out int n))
        {
            count = Mathf.Max(1, n);
        }

        ItemInfo.SpawnInWorld(output, OutputPosition(), GlobalRotation);

        itemInGrinder = null;
    }

    protected override byte[] SaveMachineState()
    {
        if (itemInGrinder == null) return null;
        return GMPObject.serializer.Serialize(new GrinderSave { itemInGrinder = itemInGrinder });
    }

    protected override void LoadMachineState(byte[] state)
    {
        itemInGrinder = GMPObject.serializer.Deserialize<GrinderSave>(state).itemInGrinder;
    }
}
