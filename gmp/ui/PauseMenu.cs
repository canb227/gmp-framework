using Godot;
using System;

/// <summary>
/// The in-game pause menu: a centred panel over a blurred view of the game. The local player opens it with Escape
/// when no other UI is open (see FactoryPlayer.Pause). It doesn't stop the simulation: in multiplayer everyone else
/// keeps playing, and this player just stands still while it's open.
/// <para>
/// OPTIONS opens the full options screen on top (its Back returns here); QUIT leaves the session for the main menu.
/// The two UNUSED buttons are disabled placeholders for future entries. Escape closes the menu (or the options
/// screen, when that's open).
/// </para>
/// </summary>
public partial class PauseMenu : Control
{
    const string OptionsScene = "res://gmp/ui/options_menu.tscn";
    const string MainMenuScene = "res://gmp/ui/main_menu.tscn";

    /// <summary>Raised when the menu wants to close (Escape); the owner closes it and recaptures the mouse.</summary>
    public event Action CloseRequested;

    Control center;
    OptionsMenu options;

    public bool isOptionsOpen => options != null;

    public override void _Ready()
    {
        HiResUI.Fill(this);
        center = GetNode<Control>("%Center");
        GetNode<Button>("%OptionsButton").Pressed += OpenOptions;
        GetNode<Button>("%QuitButton").Pressed += Quit;
    }

    public void Open()
    {
        Show();
        center.Show();
        GetNode<Button>("%OptionsButton").GrabFocus();
    }

    public void Close()
    {
        CloseOptions();
        Hide();
    }

    void OpenOptions()
    {
        options = ResourceLoader.Load<PackedScene>(OptionsScene).Instantiate<OptionsMenu>();
        options.CloseRequested += CloseOptions;
        // A sibling, not a child: both screens scale themselves to the window (HiResUI), so nesting would scale twice.
        // Added after this menu, it draws on top and sees input (Escape) first.
        GetParent().AddChild(options);
        center.Hide();
    }

    void CloseOptions()
    {
        if (options == null) return;
        options.QueueFree();
        options = null;
        center.Show();
        GetNode<Button>("%OptionsButton").GrabFocus();
    }

    /// <summary>Leaves the session (this tears down the level and its players, this menu included) for the main menu.</summary>
    void Quit()
    {
        Input.MouseMode = Input.MouseModeEnum.Visible;
        Lobby.LeaveLobby();
        GetTree().ChangeSceneToFile(MainMenuScene);
    }

    // Children see unhandled input first, so the options screen's own Escape (Back) wins when it's open.
    public override void _UnhandledInput(InputEvent @event)
    {
        if (!Visible || options != null) return;
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Escape)
        {
            GetViewport().SetInputAsHandled();
            CloseRequested?.Invoke();
        }
    }
}
