using Godot;

// Entry scene. Routes to the shared debug lobby (in LAN or Steam mode), options, or
// quit. Also honours the command-line driving flags so the headless verification path
// still works: --steam (with or without --host/--join) opens the lobby in Steam mode,
// --host/--join/--lan opens it in LAN mode, and the lobby's own HandleCmdline then
// auto-hosts/joins from the same args.
public partial class MainMenu : Control
{
    private const string LobbyScene = "res://gmp/ui/lobby_debug.tscn";
    private const string OptionsScene = "res://gmp/ui/options_menu.tscn";

    public override void _Ready()
    {
        Button("StartSteamButton").Pressed += () => OpenLobby(LobbyMode.Steam);
        Button("StartLANButton").Pressed += () => OpenLobby(LobbyMode.Lan);
        Button("OptionsButton").Pressed += () => Open(OptionsScene);
        Button("QuitButton").Pressed += () => GetTree().Quit();

        CallDeferred(nameof(HandleCmdline));
    }

    private Button Button(string name) =>
        GetNode<Button>($"CenterContainer/VBoxContainer/ButtonList/{name}");

    private void Open(string path) => GetTree().ChangeSceneToFile(path);

    private void OpenLobby(LobbyMode mode)
    {
        LobbyDebug.NextMode = mode;
        Open(LobbyScene);
    }

    private void HandleCmdline()
    {
        bool steam = false, lan = false;
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == "--steam") steam = true;
            if (a == "--host" || a == "--join" || a == "--lan") lan = true;
        }
        if (steam) OpenLobby(LobbyMode.Steam);
        else if (lan) OpenLobby(LobbyMode.Lan);
    }
}
