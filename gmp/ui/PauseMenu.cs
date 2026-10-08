using Godot;

/// <summary>
/// The in-game pause menu: a centred panel over a blurred view of the game. Opened by <see cref="UIManager"/> when
/// Escape is pressed in game with nothing else open; Escape closes it again. It doesn't stop the simulation: in
/// multiplayer everyone else keeps playing, and this player just stands still while it's open.
/// <para>
/// OPTIONS opens the options screen on top (the panel hides under it, the blur stays); QUIT leaves the session for
/// the main menu. The two UNUSED buttons are disabled placeholders for future entries.
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
        GetNode<Button>("%QuitButton").Pressed += UIManager.QuitToMainMenu;
    }

    public override void OnOpened() => GetNode<Button>("%OptionsButton").GrabFocus();

    public override void OnCovered() => center.Hide();

    public override void OnRevealed()
    {
        center.Show();
        GetNode<Button>("%OptionsButton").GrabFocus();
    }
}
