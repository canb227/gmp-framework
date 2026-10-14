using Godot;

/// <summary>
/// What a <see cref="Machine"/> turns into what: when all of <see cref="inputs"/> are in its input volumes it takes
/// them, works for <see cref="baseTime"/> divided by the machine's processingSpeed, then puts out <see cref="outputs"/>.
/// </summary>
[GlobalClass]
public partial class Recipe : Resource
{
    /// <summary>Items needed and how many of each.</summary>
    [Export] public Godot.Collections.Dictionary<ItemInfo, int> inputs = new();

    /// <summary>Items made and how many of each.</summary>
    [Export] public Godot.Collections.Dictionary<ItemInfo, int> outputs = new();

    /// <summary>Seconds one cycle takes at a processingSpeed of 1.</summary>
    [Export] public float baseTime = 1f;
}
