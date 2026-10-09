using Godot;

/// <summary>
/// The in-game pause menu: a centred panel over a blurred view of the game. Opened by <see cref="UIManager"/> when
/// Escape is pressed in game with nothing else open; Escape closes it again. It doesn't stop the simulation: in
/// multiplayer everyone else keeps playing, and this player just stands still while it's open.
/// <para>
/// OPTIONS opens the options screen on top (the panel hides under it, the blur stays); SAVE GAME (host only) opens
/// the save browser (<see cref="SaveFileDialog"/>) to write the game to a file; QUIT leaves the session for the main menu. The UNUSED button is a
/// disabled placeholder for a future entry.
/// </para>
/// </summary>
public partial class PauseMenu : UIScreen
{
    Control center;

    public override void _Ready()
    {
        HiResUI.Fill(this);
        center = GetNode<Control>("%Center");
        GetNode<Button>("%OptionsButton").Pressed += UIManager.OpenOptions;
        Button save = GetNode<Button>("%SaveButton");
        save.Disabled = !Lobby.isHost; // the host's world is the saved one
        save.Pressed += OnSavePressed;
        GetNode<Button>("%QuitButton").Pressed += UIManager.QuitToMainMenu;
    }

    void OnSavePressed()
    {
        SaveFileDialog.Open(this, true, path => UIManager.ShowText(GameWorld.SaveToFile(path) ? "Game saved" : "Save failed"));
    }

    public override void OnOpened() => GetNode<Button>("%OptionsButton").GrabFocus();

    public override void OnCovered() => center.Hide();

    public override void OnRevealed()
    {
        center.Show();
        GetNode<Button>("%OptionsButton").GrabFocus();
    }
}
