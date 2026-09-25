using Godot;

/// <summary>
/// An item that does something while held. Its <see cref="FactoryItem.inHandScene"/> must have a
/// <see cref="HeldTool"/> root, which gets first refusal on the holder's input and runs its own per-frame
/// logic. Examples: the magnet rod, and every <see cref="BlueprintItem"/> (its placement preview is a tool).
/// </summary>
[GlobalClass]
public partial class ToolItem : FactoryItem
{
}
