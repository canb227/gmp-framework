using Godot;

// The front end's first screen, put up by UIManager over the empty boot scene. Routes to the
// shared debug lobby (in LAN or Steam mode), options, or quit. Also honours the command-line
// driving flags so the headless verification path still works: --steam (with or without
// --host/--join) opens the lobby in Steam mode, --host/--join/--lan opens it in LAN mode, and
// the lobby's own HandleCmdline then auto-hosts/joins from the same args.
public partial class MainMenu : UIScreen
{

    public override void _Ready()
    {
        HiResUI.Fill(this);
        GetNode<Button>("%StartSteamButton").Pressed += () => UIManager.OpenLobby(LobbyMode.Steam);
        GetNode<Button>("%StartLANButton").Pressed += () => UIManager.OpenLobby(LobbyMode.Lan);
        GetNode<Button>("%OptionsButton").Pressed += UIManager.OpenOptions;
        GetNode<Button>("%QuitButton").Pressed += () => GetTree().Quit();

        CallDeferred(nameof(HandleCmdline));
    }

    // The bottom of the stack: Escape has nowhere to go back to.
    public override void Cancel() { }

    // The options screen is opaque; no need to draw (and run the overlay shader for) this under it.
    public override void OnCovered() => Hide();

    public override void OnRevealed() => Show();

    /// <summary>The command-line flags are acted on once per run, so leaving a flag-launched lobby or game returns here.</summary>
    private static bool cmdlineHandled;

    private void HandleCmdline()
    {
        if (cmdlineHandled) return;
        cmdlineHandled = true;
        bool steam = false, lan = false;
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == "--steam") steam = true;
            if (a == "--host" || a == "--join" || a == "--lan") lan = true;
        }
        if (steam) UIManager.OpenLobby(LobbyMode.Steam);
        else if (lan) UIManager.OpenLobby(LobbyMode.Lan);
    }
}
