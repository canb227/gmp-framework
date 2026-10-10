using Godot;

/// <summary>
/// UIManager: the HUD. One <see cref="InventoryUI"/> (hotbar, held-item name, hover labels and the inventory
/// screen), created when the local player spawns (<see cref="BindHud"/>) and freed when it leaves. Other players
/// get no HUD at all.
/// </summary>
public partial class UIManager
{
    const string HudScene = "res://game/scenes/ui/PlayerHUD.tscn";

    static CanvasLayer hudLayer;

    /// <summary>The local player's HUD, or null outside a game.</summary>
    public static InventoryUI Hud { get; private set; }

    /// <summary>True while the inventory screen is open (the mouse is free, but the player can still move).</summary>
    public static bool IsInventoryOpen => Hud != null && Hud.isOpen;

    void ReadyHud()
    {
        hudLayer = AddLayer("Hud", HudLayer);
    }

    /// <summary>Gives <paramref name="player"/> (the local player) the HUD and hands it the mouse.</summary>
    public static void BindHud(FactoryPlayer player)
    {
        UnbindHud();
        Hud = ResourceLoader.Load<PackedScene>(HudScene).Instantiate<InventoryUI>();
        Hud.player = player;
        hudLayer.AddChild(Hud);
        RefreshMouseMode();
    }

    /// <summary>Removes the HUD, if it belongs to <paramref name="player"/> (any player when null).</summary>
    public static void UnbindHud(FactoryPlayer player = null)
    {
        if (Hud == null || (player != null && Hud.player != player)) return;
        Hud.QueueFree();
        Hud = null;
        RefreshMouseMode();
    }

    public static void OpenInventory()
    {
        if (Hud == null || Hud.isOpen || BlocksGameplayInput) return;
        Hud.Open();
        RefreshMouseMode();
    }

    public static void CloseInventory()
    {
        if (!IsInventoryOpen) return;
        Hud.Close();
        RefreshMouseMode();
    }

    public static void ToggleInventory()
    {
        if (IsInventoryOpen) CloseInventory();
        else OpenInventory();
    }

    /// <summary>The crosshair hover labels: the target's name and the action prompt below it. Null hides a line.</summary>
    public static void SetHoverInfo(string name, string prompt) => Hud?.SetHoverInfo(name, prompt);
}
