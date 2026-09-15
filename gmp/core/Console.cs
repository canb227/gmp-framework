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
            "reset",
        };
    }
}

