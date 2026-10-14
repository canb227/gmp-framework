using Godot;

/// <summary>
/// A tool the player holds and uses, e.g. the magnet rod. Its <see cref="ItemInfo.inHandScene"/> has a
/// <see cref="HeldItem"/> root, which does the work.
/// </summary>
[GlobalClass]
public partial class EquipmentItem : PurchasableItem
{
}
