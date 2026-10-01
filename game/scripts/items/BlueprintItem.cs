using Godot;

/// <summary>
/// An item that builds one structure. While held, its <see cref="ItemInfo.inHandScene"/> should be
/// <c>BlueprintGhost.tscn</c>, which previews <see cref="structureScene"/> snapped to the build grid;
/// primary places it (consuming one blueprint). Leave <see cref="ItemInfo.droppedScene"/> empty to
/// drop as a <c>DefaultDroppedBox</c>. A blueprint can have two forms (<see cref="alternateStructureScene"/>).
/// </summary>
[GlobalClass]
public partial class BlueprintItem : ItemInfo
{
    /// <summary>The <see cref="Structure"/> scene this blueprint places.</summary>
    [Export]
    public PackedScene structureScene;

    /// <summary>
    /// Optional second form of the structure (e.g. the mirrored turn, or the downhill slope), chosen with the
    /// "alternate" action while placing. It should share the footprint and give back this blueprint.
    /// </summary>
    [Export]
    public PackedScene alternateStructureScene;

    /// <summary>Item id paid to buy one of these in the shop (e.g. "scrap_ball"). Empty means free.</summary>
    [Export]
    public string costItemID = "";

    /// <summary>How many <see cref="costItemID"/> one of these costs (e.g. 2).</summary>
    [Export]
    public int costAmount = 0;

    /// <summary>True when buying this takes something (<see cref="costItemID"/> set and <see cref="costAmount"/> above 0).</summary>
    public bool HasCost => !string.IsNullOrEmpty(costItemID) && costAmount > 0;

    /// <summary>The scene for the chosen form; the main one when there is no alternate.</summary>
    public PackedScene StructureScene(bool alternate) => alternate && alternateStructureScene != null ? alternateStructureScene : structureScene;
}
