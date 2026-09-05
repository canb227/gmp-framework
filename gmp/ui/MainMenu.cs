using Godot;

// Entry scene. Routes to the LAN or Steam lobby, options, or quit. Also honours the
// command-line driving flags so the headless verification path still works: --steam
// (with or without --host/--join) opens the Steam lobby, --host/--join/--lan opens the
// LAN lobby, and the lobby's own HandleCmdline then auto-hosts/joins from the same args.
public partial class MainMenu : Control
{
    private const string LanScene = "res://gmp/ui/lobby_lan_debug.tscn";
    private const string SteamScene = "res://gmp/ui/lobby_steam_debug.tscn";
    private const string OptionsScene = "res://gmp/ui/options_menu.tscn";

    public override void _Ready()
    {
        Button("StartSteamButton").Pressed += () => Open(SteamScene);
        Button("StartLANButton").Pressed += () => Open(LanScene);
        Button("OptionsButton").Pressed += () => Open(OptionsScene);
        Button("QuitButton").Pressed += () => GetTree().Quit();

        CallDeferred(nameof(HandleCmdline));
    }

    private Button Button(string name) =>
        GetNode<Button>($"CenterContainer/VBoxContainer/ButtonList/{name}");

    private void Open(string path) => GetTree().ChangeSceneToFile(path);

    private void HandleCmdline()
    {
        bool steam = false, lan = false;
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a == "--steam") steam = true;
            if (a == "--host" || a == "--join" || a == "--lan") lan = true;
        }
        if (steam) Open(SteamScene);
        else if (lan) Open(LanScene);
    }
}
