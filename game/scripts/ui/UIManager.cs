using Godot;
using System.Collections.Generic;

/// <summary>
/// The one owner of the game's UI (autoload). Everything the player sees on screen goes through here, on its own
/// canvas layers so it survives scene changes:
/// <list type="bullet">
/// <item>Screens (this file): a stack of <see cref="UIScreen"/>s, the menus that take the mouse (main menu, options,
/// pause, shop). The top screen gets Escape; any open screen blocks gameplay input and frees the mouse.</item>
/// <item>The HUD (UIManager.Hud.cs): the local player's hotbar, hover labels and inventory screen, bound when the
/// local player spawns.</item>
/// <item>Notifications (UIManager.Notifications.cs): timed text popups anchored on the HUD, and world markers that
/// point at objects through walls.</item>
/// </list>
/// It also decides the mouse mode (<see cref="RefreshMouseMode"/>): captured only while playing with nothing open.
/// The developer console and the ImGui debug windows are not managed here.
/// <para>
/// The front end is screens too: the boot scene (<see cref="FrontEndScene"/>) is empty and the main menu is pushed
/// over it. The debug lobby is still a scene of its own, loaded by <see cref="OpenLobby"/>.
/// </para>
/// </summary>
public partial class UIManager : Node
{
    public const string FrontEndScene = "res://gmp/ui/front_end.tscn";
    const string LobbyScene = "res://gmp/ui/lobby_debug.tscn";
    const string MainMenuScene = "res://gmp/ui/main_menu.tscn";
    const string OptionsScene = "res://gmp/ui/options_menu.tscn";
    const string PauseMenuScene = "res://gmp/ui/pause_menu.tscn";
    const string ShopScene = "res://game/scenes/ui/shop/ShopUI.tscn";

    // Canvas layers, bottom to top. World markers sit under the HUD; notifications over everything.
    const int MarkerLayer = 4, HudLayer = 5, ScreenLayer = 10, NotificationLayer = 20;

    public static UIManager instance;

    static CanvasLayer screenLayer;
    static readonly List<UIScreen> screens = new();

    /// <summary>The top screen, or null when none is open.</summary>
    public static UIScreen TopScreen => screens.Count > 0 ? screens[^1] : null;

    /// <summary>True while an open screen keeps the player from moving or acting.</summary>
    public static bool BlocksGameplayInput
    {
        get
        {
            foreach (UIScreen screen in screens)
            {
                if (screen.BlocksGameplay) return true;
            }
            return false;
        }
    }

    /// <summary>True while the game should have the mouse: playing (a HUD is bound), no screen and no inventory open.</summary>
    public static bool GameplayHasMouse => Hud != null && screens.Count == 0 && !IsInventoryOpen;

    public override void _Ready()
    {
        instance = this;
        // Menus must keep working while the tree is paused.
        ProcessMode = ProcessModeEnum.Always;
        screenLayer = AddLayer("Screens", ScreenLayer);
        ReadyHud();
        ReadyNotifications();
        CallDeferred(nameof(ShowFrontEndIfBooting));
    }

    CanvasLayer AddLayer(string name, int layer)
    {
        CanvasLayer canvas = new() { Name = name, Layer = layer };
        AddChild(canvas);
        return canvas;
    }

    /// <summary>Started on the (empty) boot scene: put the main menu up.</summary>
    void ShowFrontEndIfBooting()
    {
        if (GetTree().CurrentScene?.SceneFilePath == FrontEndScene)
        {
            ShowMainMenu();
        }
    }

    // ---- screen stack ----------------------------------------------------------

    /// <summary>Instantiates <paramref name="scenePath"/> and pushes it as the new top screen.</summary>
    public static T OpenScreen<T>(string scenePath) where T : UIScreen
    {
        T screen = ResourceLoader.Load<PackedScene>(scenePath).Instantiate<T>();
        OpenScreen(screen);
        return screen;
    }

    /// <summary>Pushes <paramref name="screen"/> (not yet in the tree) as the new top screen.</summary>
    public static void OpenScreen(UIScreen screen)
    {
        if (screen.BlocksGameplay && IsInventoryOpen)
        {
            CloseInventory();
        }
        UIScreen covered = TopScreen;
        screens.Add(screen);
        // Later children draw on top and get GUI input first.
        screenLayer.AddChild(screen);
        covered?.OnCovered();
        screen.OnOpened();
        RefreshMouseMode();
    }

    /// <summary>Closes <paramref name="screen"/> and every screen above it.</summary>
    public static void CloseScreen(UIScreen screen)
    {
        int index = screens.IndexOf(screen);
        if (index < 0) return;
        while (screens.Count > index)
        {
            UIScreen top = screens[^1];
            screens.RemoveAt(screens.Count - 1);
            top.OnClosed();
            top.QueueFree();
        }
        TopScreen?.OnRevealed();
        RefreshMouseMode();
    }

    public static void CloseTopScreen()
    {
        if (TopScreen != null) CloseScreen(TopScreen);
    }

    public static void CloseAllScreens()
    {
        if (screens.Count > 0) CloseScreen(screens[0]);
    }

    /// <summary>True if a screen of type <typeparamref name="T"/> is anywhere on the stack.</summary>
    public static bool IsOpen<T>() where T : UIScreen
    {
        foreach (UIScreen screen in screens)
        {
            if (screen is T) return true;
        }
        return false;
    }

    // ---- named screens ---------------------------------------------------------

    public static void ShowMainMenu()
    {
        CloseAllScreens();
        OpenScreen<MainMenu>(MainMenuScene);
    }

    public static void OpenOptions() => OpenScreen<OptionsMenu>(OptionsScene);

    /// <summary>Opens the pause menu (in game only).</summary>
    public static void OpenPauseMenu()
    {
        if (Hud == null || IsOpen<PauseMenu>()) return;
        OpenScreen<PauseMenu>(PauseMenuScene);
    }

    /// <summary>Opens the shop against <paramref name="inventory"/> (the local player's).</summary>
    public static void OpenShop(Inventory inventory)
    {
        if (IsOpen<ShopUI>()) return;
        ShopUI shop = OpenScreen<ShopUI>(ShopScene);
        shop.FitToViewport();
        shop.Open(inventory);
    }

    /// <summary>Leaves the front end for the debug lobby scene in <paramref name="mode"/>.</summary>
    public static void OpenLobby(LobbyMode mode)
    {
        CloseAllScreens();
        LobbyDebug.NextMode = mode;
        instance.GetTree().ChangeSceneToFile(LobbyScene);
    }

    /// <summary>Leaves the session (tearing down the level and its players) and returns to the main menu.</summary>
    public static void QuitToMainMenu()
    {
        Lobby.LeaveLobby();
        CloseAllScreens();
        ClearNotifications();
        UnbindHud();
        instance.GetTree().ChangeSceneToFile(FrontEndScene);
        ShowMainMenu();
    }

    // ---- input + mouse ---------------------------------------------------------

    /// <summary>
    /// Escape and the inventory key. This autoload sits after the game world in the tree, so it sees unhandled input
    /// before the player does (unhandled input runs in reverse tree order) and can swallow it.
    /// </summary>
    public override void _UnhandledInput(InputEvent @event)
    {
        bool escape = @event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape;
        if (escape)
        {
            if (TopScreen != null) TopScreen.Cancel();
            else if (IsInventoryOpen) CloseInventory();
            else if (Hud != null) OpenPauseMenu();
            else return;
            GetViewport().SetInputAsHandled();
            return;
        }

        if (Hud != null && !BlocksGameplayInput && @event.IsActionPressed(InputActions.inventory))
        {
            ToggleInventory();
            GetViewport().SetInputAsHandled();
        }
    }

    /// <summary>Captures the mouse while <see cref="GameplayHasMouse"/>, frees it otherwise.</summary>
    public static void RefreshMouseMode()
    {
        Input.MouseMode = GameplayHasMouse ? Input.MouseModeEnum.Captured : Input.MouseModeEnum.Visible;
    }
}
