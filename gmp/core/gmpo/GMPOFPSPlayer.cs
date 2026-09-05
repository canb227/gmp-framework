using Godot;
using PolyType;
using System;

[GlobalClass]
public partial class GMPOFPSPlayer : GMPOCharacterBody3D, GMPObject
{

    public const float Speed = 5.0f;
    public const float JumpVelocity = 4.5f;

    public override void _PhysicsProcess(double delta)
    {
        if (Lobby.selfPeerID != authority)
        {
            base._PhysicsProcess(delta);
            MoveAndSlide();
            return;
        }

        Vector3 velocity = Velocity;

        // Add the gravity.
        if (!IsOnFloor())
        {
            velocity += GetGravity() * (float)delta;
        }

        // Handle Jump.
        if (Input.IsActionJustPressed("ui_accept") && IsOnFloor())
        {
            RPCManager.RPC(this, "Jump", []);
        }

        // Get the input direction and handle the movement/deceleration.
        // As good practice, you should replace UI actions with custom gameplay actions.
        Vector2 inputDir = Input.GetVector("ui_left", "ui_right", "ui_up", "ui_down");
        Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
        if (direction != Vector3.Zero)
        {
            velocity.X = direction.X * Speed;
            velocity.Z = direction.Z * Speed;
        }
        else
        {
            velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
            velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
        }

        Velocity = velocity;
        MoveAndSlide();
    }

    public void Jump()
    {
        Velocity = new Vector3(Velocity.X, JumpVelocity, Velocity.Z);
    }

    public override void AfterInit()
    {
        // Only the machine that controls this player drives the viewport. Demote
        // remote proxies explicitly: their camera still grabs the viewport when it
        // enters the tree, and whichever proxy spawns last would otherwise win.
        Camera3D cam = GetNode<Camera3D>("Camera3D");
        if (Lobby.selfPeerID == authority)
        {
            //Logging.Log($"Player {Lobby.selfPeerID} has linked camera to player {id}.", "GMPOPlayer");
            cam.Current = true;
        }
        else
        {
            cam.Current = false;
        }
    }
}
