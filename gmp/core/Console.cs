using Godot;
using ImGuiNET;
using Limbo.Console.Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public partial class Console : Node
{

    public override void _Ready()
    {
        LimboConsole.RegisterCommand(new Callable(this,"debugui"));
        LimboConsole.AddArgumentAutocompleteSource("debugui", 0, new Callable(this,"GetDebuguiOptions"));
        LimboConsole.RegisterCommand(new Callable(this, "save"));
        LimboConsole.RegisterCommand(new Callable(this, "resync"));
        LimboConsole.RegisterCommand(new Callable(this, "load"));

        // Keep debug windows (perf) drawing while the tree is paused, e.g. during world load.
        ProcessMode = ProcessModeEnum.Always;

        // "-- --debugui perf,graphics" opens those windows at startup.
        string[] args = OS.GetCmdlineUserArgs();
        int flag = Array.IndexOf(args, "--debugui");
        if (flag >= 0 && flag + 1 < args.Length)
        {
            foreach (string name in args[flag + 1].Split(','))
            {
                debugui(name);
            }
        }
    }

    public override void _Process(double delta)
    {
        PerfDebug.Process(delta);
        GraphicsDebug.Process(this, delta);
        ItemsDebug.Process(this, delta);
        QuestsDebug.Process();
    }

    // ---- saves (host only; see GameWorld.Resync.cs) ----

    // Names are files in this user's save folder; the pause menu and lobby browse for any file instead.
    static string SavePath(string name)
    {
        return GameWorld.SaveDir + (name.EndsWith(GameWorld.SaveExtension) ? name : name + GameWorld.SaveExtension);
    }

    public void save()
    {
        string path = GameWorld.SaveDir + GameWorld.DefaultSaveName();
        if (GameWorld.SaveToFile(path)) LimboConsole.Info($"Saved {path}");
        else LimboConsole.Error("Not saved: only the host can save, in a running game.");
    }

    public void resync()
    {
        if (!GameWorld.Resync()) LimboConsole.Error("Can't resync now: host only, in a running game, one at a time.");
    }

    public void load(string name)
    {
        if (!GameWorld.LoadFromFile(SavePath(name))) LimboConsole.Error($"Can't load '{name}': host only, in a running game, and the save must be readable.");
    }

    public void debugui(string which)
    {
        switch (which)
        {
            case "":
                LimboConsole.Error("Must provide name of debugui to toggle.");
                break;
            case "lobby":
                Lobby.displayLobbyDebugInfo = !Lobby.displayLobbyDebugInfo;
                break;
            case "syncedobjects":
                GameWorld.displaySyncedObjectDebugInfo = !GameWorld.displaySyncedObjectDebugInfo;
                break;
            case "grid":
                BuildGrid.Toggle();
                break;
            case "gridhighlight":
                BuildGrid.ToggleHighlight();
                break;
            case "player":
                FactoryPlayer.displayPlayerDebugInfo = !FactoryPlayer.displayPlayerDebugInfo;
                break;
            case "perf":
                PerfDebug.displayPerfDebugInfo = !PerfDebug.displayPerfDebugInfo;
                break;
            case "graphics":
                GraphicsDebug.displayGraphicsDebugInfo = !GraphicsDebug.displayGraphicsDebugInfo;
                break;
            case "items":
                ItemsDebug.displayItemsDebugInfo = !ItemsDebug.displayItemsDebugInfo;
                break;
            case "quests":
                QuestsDebug.displayQuestsDebugInfo = !QuestsDebug.displayQuestsDebugInfo;
                break;
            case "reset":
                foreach (string name in GetDebuguiOptions())
                {
                    ImGui.SetWindowPos("debugui "+name, new System.Numerics.Vector2(0, 0));
                    ImGui.SetWindowSize("debugui " + name, new System.Numerics.Vector2(150, 150));
                }
                break;

        }
    }

    public Godot.Collections.Array GetDebuguiOptions()
    {
        return new Godot.Collections.Array {
            "lobby",
            "syncedobjects",
            "grid",
            "gridhighlight",
            "player",
            "perf",
            "graphics",
            "items",
            "quests",
            "reset",
        };
    }
}

