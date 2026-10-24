using Godot;

/// <summary>
/// UIManager: the HUD. One <see cref="InventoryUI"/> (hotbar, held-item name, hover labels and the inventory
/// screen), created when the local player spawns (<see cref="BindHud"/>) and freed when it leaves, and with it the
/// <see cref="QuestTracker"/> (on its own layer above the screens, so menus don't hide it). Other players get no
/// HUD at all.
/// </summary>
public partial class UIManager
{
    const string HudScene = "res://game/scenes/ui/PlayerHUD.tscn";
    const string QuestTrackerScene = "res://game/scenes/ui/QuestTracker.tscn";

    static CanvasLayer hudLayer;
    static CanvasLayer questLayer;
    static QuestTracker questTracker;

    /// <summary>The local player's HUD, or null outside a game.</summary>
    public static InventoryUI Hud { get; private set; }

    /// <summary>True while the inventory screen is open (the mouse is free, but the player can still move).</summary>
    public static bool IsInventoryOpen => Hud != null && Hud.isOpen;

    void ReadyHud()
    {
        hudLayer = AddLayer("Hud", HudLayer);
        questLayer = AddLayer("Quests", QuestLayer);
    }

    /// <summary>Gives <paramref name="player"/> (the local player) the HUD and hands it the mouse.</summary>
    public static void BindHud(FactoryPlayer player)
    {
        UnbindHud();
        Hud = ResourceLoader.Load<PackedScene>(HudScene).Instantiate<InventoryUI>();
        Hud.player = player;
        hudLayer.AddChild(Hud);
        questTracker = ResourceLoader.Load<PackedScene>(QuestTrackerScene).Instantiate<QuestTracker>();
        questLayer.AddChild(questTracker);
        questTracker.SetUnderScreen(TopScreen != null);
        // Temporary diagnostic health bar, bottom left (to be replaced by a less intrusive effect).
        healthBar = new ProgressBar();
        healthBar.ShowPercentage = false;
        healthBar.AnchorTop = 1;
        healthBar.AnchorBottom = 1;
        healthBar.OffsetLeft = 24;
        healthBar.OffsetRight = 264;
        healthBar.OffsetTop = -44;
        healthBar.OffsetBottom = -24;
        healthBar.MouseFilter = Control.MouseFilterEnum.Ignore;
        healthBar.Modulate = new Color(1f, 0.35f, 0.35f);
        hudLayer.AddChild(healthBar);
        RefreshMouseMode();
    }

    static ProgressBar healthBar;

    /// <summary>The local player's health on the HUD's diagnostic bar.</summary>
    public static void SetHealth(float health, float maxHealth)
    {
        if (healthBar == null) return;
        healthBar.MaxValue = maxHealth;
        healthBar.Value = health;
    }

    /// <summary>Removes the HUD, if it belongs to <paramref name="player"/> (any player when null).</summary>
    public static void UnbindHud(FactoryPlayer player = null)
    {
        if (Hud == null || (player != null && Hud.player != player)) return;
        Hud.QueueFree();
        Hud = null;
        questTracker.QueueFree();
        questTracker = null;
        healthBar.QueueFree();
        healthBar = null;
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
