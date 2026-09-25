using Godot;

/// <summary>
/// An item that builds one structure. While held, its <see cref="ItemInfo.inHandScene"/> should be
/// <c>BlueprintGhost.tscn</c>, which previews <see cref="structureScene"/> snapped to the build grid;
/// primary places it (consuming one blueprint). Leave <see cref="ItemInfo.droppedScene"/> empty to
/// drop as a <c>DefaultDroppedBox</c>.
/// </summary>
[GlobalClass]
public partial class BlueprintItem : ItemInfo
{
    /// <summary>The <see cref="Structure"/> scene this blueprint places.</summary>
    [Export]
    public PackedScene structureScene;
}
