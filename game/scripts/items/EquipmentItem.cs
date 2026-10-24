using Godot;

/// <summary>
/// A tool the player holds and uses, e.g. the gravity gun. Its <see cref="ItemInfo.inHandScene"/> has a
/// <see cref="HeldItem"/> root, which does the work.
/// </summary>
[GlobalClass]
public partial class EquipmentItem : PurchasableItem
{
}
