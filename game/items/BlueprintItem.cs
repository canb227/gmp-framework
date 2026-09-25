using Godot;

/// <summary>
/// An item that builds one structure. While held, its <see cref="FactoryItem.inHandScene"/> should be
/// <c>BlueprintGhost.tscn</c>, which previews <see cref="structureScene"/> snapped to the build grid;
/// primary places it (consuming one blueprint). Leave <see cref="FactoryItem.droppedScene"/> empty to
/// drop as a <c>DefaultDroppedBox</c>.
/// </summary>
[GlobalClass]
public partial class BlueprintItem : ToolItem
{
    /// <summary>The <see cref="Structure"/> scene this blueprint places.</summary>
    [Export]
    public PackedScene structureScene;
}
