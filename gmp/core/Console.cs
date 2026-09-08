using Godot;
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
        }
    }
}

