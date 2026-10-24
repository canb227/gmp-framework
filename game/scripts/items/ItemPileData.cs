using Godot;

/// <summary>
/// A baked pile of loose items: what an <see cref="ItemDropper"/>'s Save button wrote (DroppersDebug), loaded on every
/// peer when its level starts. Each item is an index into <see cref="itemIDs"/> and a transform relative to the dropper,
/// so moving or turning the dropper in the editor moves the whole pile.
/// </summary>
[GlobalClass]
public partial class ItemPileData : Resource
{
    /// <summary>The item ids the pile uses, once each.</summary>
    [Export] public Godot.Collections.Array<string> itemIDs = new();
    /// <summary>For each item, its index in <see cref="itemIDs"/>.</summary>
    [Export] public int[] kinds = System.Array.Empty<int>();
    /// <summary>For each item, where it lies, relative to the dropper.</summary>
    [Export] public Godot.Collections.Array<Transform3D> transforms = new();
    /// <summary>A seed kept with the dropper's Keep seed button (-1: none); the dropper uses it when its own is -1.</summary>
    [Export] public long seed = -1;
}
