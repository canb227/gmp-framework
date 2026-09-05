using Godot;
using System;
using System.Linq;

public partial class GmpoSphere : GMPORigidBody3D
{
    // Called when the node enters the scene tree for the first time.
    public override void _Ready()
    {
    }

    // Called every frame. 'delta' is the elapsed time since the previous frame.
    public override void _Process(double delta)
    {
    }

    public override void _Input(InputEvent @event)
    {
        if (authority == Lobby.selfPeerID)
        {
            if (@event is InputEventKey keyEvent && keyEvent.Pressed)
            {
                if (keyEvent.Keycode == Key.Enter)
                {
                    RPCManager.RPC(GetPath(), "ChangeColor", [ColorDict.NamedColors.ElementAt(Random.Shared.Next(0, ColorDict.NamedColors.Count)).Value]);
                }
            }
        }
    }

    public void ChangeColor(Color color)
    {
        GetNode<MeshInstance3D>("m").SetSurfaceOverrideMaterial(0, new StandardMaterial3D() { AlbedoColor = color });
    }
}
