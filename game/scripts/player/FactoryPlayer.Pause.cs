using Godot;

/// <summary>
/// FactoryPlayer: the pause menu (<see cref="PauseMenu"/>). Escape opens it when no other UI (inventory, shop) is
/// open; Escape again, or the menu itself, closes it. While it's open the mouse is freed and all gameplay input is
/// blocked, as with the shop. The game keeps running: the menu doesn't pause the (networked) simulation.
/// </summary>
public partial class FactoryPlayer
{
    const string PauseMenuScene = "res://gmp/ui/pause_menu.tscn";

    PauseMenu pauseMenu;

    public bool isPauseOpen => pauseMenu != null && pauseMenu.Visible;

    /// <summary>True while a menu has the keyboard and mouse, so the player shouldn't move or act.</summary>
    bool menuHasInput => isShopOpen || isPauseOpen;

    /// <summary>Opens the pause menu (created on first use). Local player only.</summary>
    public void OpenPauseMenu()
    {
        if (!isLocal || isPauseOpen) return;
        if (pauseMenu == null)
        {
            // Own canvas layer above the HUD and the shop.
            CanvasLayer layer = new() { Layer = 20, Name = "PauseLayer" };
            AddChild(layer);
            pauseMenu = ResourceLoader.Load<PackedScene>(PauseMenuScene).Instantiate<PauseMenu>();
            layer.AddChild(pauseMenu);
            pauseMenu.CloseRequested += ClosePauseMenu;
        }
        pauseMenu.Open();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public void ClosePauseMenu()
    {
        if (!isPauseOpen) return;
        pauseMenu.Close();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    /// <summary>
    /// Escape with nothing else open opens the pause menu; while it's open every gameplay input is swallowed (the
    /// menu handles its own Escape). True if the event was taken.
    /// </summary>
    bool HandlePauseInput(InputEvent @event)
    {
        if (isPauseOpen) return true;
        bool escape = @event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape;
        if (escape && !hud.isOpen && !isShopOpen)
        {
            OpenPauseMenu();
            GetViewport().SetInputAsHandled();
            return true;
        }
        return false;
    }
}
