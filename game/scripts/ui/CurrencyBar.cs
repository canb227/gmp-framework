using Godot;
using System;

/// <summary>
/// A row of every discovered currency: its icon, name and how much the players hold (<see cref="Shop.Balances"/>),
/// in <see cref="Currency"/> order. Empty until the first currency is discovered. Used in the shop header and the
/// HUD (hide the HUD's CurrencyBar node to turn it off there).
/// </summary>
public partial class CurrencyBar : HBoxContainer
{
    [Export] public int iconSize = 48;
    [Export] public int fontSize = 30;
    /// <summary>Text outline, for reading over the world (0: none).</summary>
    [Export] public int outlineSize = 0;

    public override void _EnterTree()
    {
        Shop.BalancesChanged += Rebuild;
        Rebuild();
    }

    public override void _ExitTree()
    {
        Shop.BalancesChanged -= Rebuild;
    }

    private void Rebuild()
    {
        foreach (Node child in GetChildren())
        {
            RemoveChild(child);
            child.QueueFree();
        }
        foreach (Currency currency in Enum.GetValues<Currency>())
        {
            if (!Shop.IsDiscovered(currency)) continue;
            CurrencyInfo info = Shop.Currencies[currency];

            HBoxContainer entry = new() { MouseFilter = MouseFilterEnum.Ignore };
            entry.AddThemeConstantOverride("separation", 10);
            entry.AddChild(new TextureRect
            {
                Texture = info.icon,
                CustomMinimumSize = new Vector2(iconSize, iconSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsVertical = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            });
            Label label = new()
            {
                Text = $"{info.name.ToUpperInvariant()}  {Shop.Balance(currency)}",
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = MouseFilterEnum.Ignore,
            };
            label.AddThemeFontSizeOverride("font_size", fontSize);
            if (outlineSize > 0)
            {
                label.AddThemeConstantOverride("outline_size", outlineSize);
                label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
            }
            entry.AddChild(label);
            AddChild(entry);
        }
    }
}
