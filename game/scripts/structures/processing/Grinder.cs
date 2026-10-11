using Godot;
using System.Collections.Generic;

/// <summary>
/// A machine that grinds what reaches its inputs: items in its input ports' volumes (<see cref="Structure.ItemsAtInputs"/>,
/// the grinder's hopper, or a press's stretch of belt) are consumed and ground one at a time; after
/// <see cref="processTime"/> each comes out transformed (<see cref="recipes"/>, <see cref="outputCounts"/> of them
/// side by side) at its output port's spawn (<see cref="Structure.OutputPosition"/>), placed so they land on a
/// conveyor there. Items without a recipe come out unchanged. Runs on the grinder's authority, which despawns inputs
/// and spawns outputs for everyone.
/// <para>
/// Known gap: an item grabbed or picked up at the moment it enters the hopper is decided separately by
/// its own authority, so in that race it could be both kept and ground.
/// </para>
/// </summary>
public partial class Grinder : Structure
{
    /// <summary>Seconds to grind one input item; queued items are ground one after another.</summary>
    [Export] public double processTime = 1.0;
    /// <summary>Input itemID → output itemID.</summary>
    [Export] public Godot.Collections.Dictionary<string, string> recipes = new();
    /// <summary>Input itemID → how many outputs it makes (default 1).</summary>
    [Export] public Godot.Collections.Dictionary<string, int> outputCounts = new();
    /// <summary>Sideways spacing between outputs of the same item (m).</summary>
    [Export] public float outputSpacing = 0.6f;

    /// <summary>Items consumed / produced so far (authority only; used by the headless multiplayer test).</summary>
    public int consumedCount { get; private set; }
    public int producedCount { get; private set; }

    readonly Queue<(string output, int count)> queue = new(); // authority only
    double untilNextOutput;

    public override void _PhysicsProcess(double delta)
    {
        if (!runsMachineLogic) return;

        foreach (PhysicalFactoryItem item in ItemsAtInputs())
        {
            string input = item.itemID ?? "";
            GameWorld.DespawnObject(item.id, DespawnReason.Consumed);
            consumedCount++;
            string output = recipes.TryGetValue(input, out string o) ? o : item.itemID;
            queue.Enqueue((output, outputCounts.TryGetValue(input, out int n) ? Mathf.Max(1, n) : 1));
            if (queue.Count == 1)
            {
                untilNextOutput = processTime;
            }
        }

        if (queue.Count == 0) return;
        untilNextOutput -= delta;
        if (untilNextOutput <= 0)
        {
            (string output, int count) = queue.Dequeue();
            for (int i = 0; output != null && i < count; i++)
            {
                Vector3 side = GlobalBasis.X * ((i - (count - 1) / 2f) * outputSpacing);
                ItemInfo.SpawnInWorld(output, OutputPosition() + side, GlobalRotation);
                producedCount++;
            }
            untilNextOutput = processTime;
        }
    }
}
