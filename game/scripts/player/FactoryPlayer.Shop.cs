using Godot;

/// <summary>
/// FactoryPlayer: the shop screen. A <see cref="Store"/>'s interact opens <see cref="ShopUI"/> as a full-screen
/// overlay for this (local) player; interact or Escape closes it. While it's open the mouse is freed and all
/// other gameplay input (movement, look, interact, equipment, grab) is blocked.
/// </summary>
public partial class FactoryPlayer
{
    const string ShopScene = "res://game/scenes/ui/shop/ShopUI.tscn";

    ShopUI shopUI;

    public bool isShopOpen => shopUI != null && shopUI.Visible;

    /// <summary>Opens the shop overlay (created on first use). Local player only.</summary>
    public void OpenShop()
    {
        if (!isLocal || isShopOpen) return;
        if (shopUI == null)
        {
            // Own canvas layer so it draws over the HUD.
            CanvasLayer layer = new() { Layer = 10, Name = "ShopLayer" };
            AddChild(layer);
            shopUI = ResourceLoader.Load<PackedScene>(ShopScene).Instantiate<ShopUI>();
            layer.AddChild(shopUI);
            shopUI.FitToViewport();
            shopUI.CloseRequested += CloseShop;
        }
        if (hud.isOpen) hud.Close();
        shopUI.Open(inventory);
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void CloseShop()
    {
        if (!isShopOpen) return;
        shopUI.Close();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>While the shop is open: interact / Escape close it, and every other input is swallowed. True if open.</summary>
    bool HandleShopInput(InputEvent @event)
    {
        if (!isShopOpen) return false;
        bool escape = @event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape;
        if (escape || @event.IsActionPressed("interact"))
        {
            CloseShop();
            GetViewport().SetInputAsHandled();
        }
        return true;
    }
}
