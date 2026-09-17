using Godot;
using Godot.Collections;
using PolyType;
using System;
using System.Reflection.Metadata;
using static Godot.OpenXRInterface;

public partial class FactoryPlayer : GMPOBox3DCharacter
{

    public int team;
    public bool isHuman;
    public ulong controllingPeerID;
    float gravityMagnitude = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
    Vector3 gravityDirection = ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3();
    Vector3 gravity;
    public Vector3 cachedVel;
    public const float Speed = 5.0f;
    public Vector3 JumpVector = new Vector3(0, 5, 0);
    const float MouseSensitivity = 0.002f;

    Vector3 Velocity = Vector3.Zero;

    [Export]
    Camera3D camera;

    [Export]
    Node3D head;

    [Export]
    Node3D body;

    Label3D playerNameLabel;

    bool inventoryOpen = false;

    [Export]
    public float pickRange = 10f;
    public Object pickTarget;
    public Control hud;
    public override void _Ready()
    {

        base._Ready();
        gravity = gravityDirection * gravityMagnitude;
        camera = GetNode<Camera3D>("Camera3D");
        hud = GetNode<Control>("PlayerHUD");
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (Lobby.selfPeerID != authority) return;

        if (@event is InputEventMouseButton mb && mb.Pressed && !inventoryOpen)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        if (@event is InputEventKey key && key.Pressed)
        {

            if (key.Keycode == Key.Escape)
            {
                if (hud.GetNode<Control>("InventoryScreen").Visible)
                {
                    hud.GetNode<Control>("InventoryScreen").Hide();
                    Input.MouseMode = Input.MouseModeEnum.Captured;
                    return;
                }
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }

        }



        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            RotateY(-motion.Relative.X * MouseSensitivity);
            camera.RotateX(-motion.Relative.Y * MouseSensitivity);
            camera.Rotation = new Vector3(
                Mathf.Clamp(camera.Rotation.X, Mathf.DegToRad(-89f), Mathf.DegToRad(89f)),
                camera.Rotation.Y,
                camera.Rotation.Z
            );
        }

        if (@event.IsActionPressed("interact"))
        {
            if (pickTarget != null && pickTarget is PhysicalFactoryItem item)
            {
                Logging.Log($"You just pressed interact on {item.Name}!", "Player");
                if (item.canBePickedUp)
                {
                    InventoryItem ii = ResourceLoader.Load<InventoryItem>(GameResources.Items[item.itemID].inventoryItemPath);
                    //GameWorld.Delete(pickTarget)
                }
            }
            else if(pickTarget != null && pickTarget is BasicButton button)
            {
                Logging.Log($"You just pressed interact on {button.Name}!", "Player");
                button.OnPressed();
            }
            
        }

        if (@event.IsActionPressed("inventory"))
        {
            if (hud.GetNode<Control>("InventoryScreen").Visible)
            {
                hud.GetNode<Control>("InventoryScreen").Hide();
                Input.MouseMode = Input.MouseModeEnum.Captured;
            }
            else
            {
                hud.GetNode<Control>("InventoryScreen").Show();
                Input.MouseMode = Input.MouseModeEnum.Visible;
            }


        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Lobby.selfPeerID != authority)
        {
            Call("move_and_slide", [Velocity, delta]);
            return;
        }

        Dictionary<string,Variant> ray = GameWorld.Raycast(camera.GlobalPosition, camera.GlobalPosition + -camera.GlobalTransform.Basis.Z * pickRange);
        if (ray["hit"].AsBool())
        {
            Node hit = (Node)ray["collider"].AsGodotObject();
            if (hit is PhysicalFactoryItem item)
            {
                pickTarget = item;
                hud.GetNode<Label>("%HoverInfoName").Show();
                hud.GetNode<Label>("%HoverInfoName").Text = item.displayName;
                if (item.canBePickedUp)
                {
                    hud.GetNode<Label>("%HoverInfoBelow").Show();
                    hud.GetNode<Label>("%HoverInfoBelow").Text = "Press F to pickup!";
                }
                else
                {
                    hud.GetNode<Label>("%HoverInfoBelow").Hide();
                }
            }
            else if(hit is BasicButton button)
            {
                pickTarget = button;
                hud.GetNode<Label>("%HoverInfoBelow").Show();
                hud.GetNode<Label>("%HoverInfoBelow").Text = "Press F to Activate.";
            }
            else
            {
                pickTarget = null;
                hud.GetNode<Label>("%HoverInfoName").Hide();
                hud.GetNode<Label>("%HoverInfoBelow").Hide();
            }
        }
        else
        {
            pickTarget = null;
            hud.GetNode<Label>("%HoverInfoName").Hide();
            hud.GetNode<Label>("%HoverInfoBelow").Hide();
        }





      

        if (!IsOnFloor())
        {
            Velocity += gravity * (float)delta;
        }
        else
        {
            if (Input.IsActionJustPressed("jump"))
            {
                Velocity = Velocity + JumpVector;
            }
        }


        Vector2 inputDir = Input.GetVector("left", "right", "forward", "backward");
        Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
        if (direction != Vector3.Zero)
        {
            Velocity.X = direction.X * Speed;
            Velocity.Z = direction.Z * Speed;
        }
        else
        {
            Velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
            Velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
        }
        cachedVel = Velocity;
        Velocity = Call("move_and_slide", [Velocity, delta]).AsVector3();


    }


    public override void AfterInit()
    {
        playerNameLabel = GetNode<Label3D>("label");
        playerNameLabel.Text = Lobby.members[controllingPeerID].Name;
        if (Lobby.selfPeerID == authority)
        {
            camera.Current = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            playerNameLabel.Hide();
            body.Hide();
            head.Hide();


        }
        else
        {
            camera.Current = false;
        }
    }




    public override byte[] GenerateStateUpdate()
    {
        PlayerSync msg = new PlayerSync
        {
            controllingPeerID = this.controllingPeerID,
            isHuman = this.isHuman,
            pos = this.Position,
            rot = this.Rotation,
            headRot = this.camera.Rotation,
            vel= this.cachedVel,
        };
        return GMPObject.serializer.Serialize(msg);
    }

    public override void ApplyStateUpdate(byte[] update)
    {
        if (update == null || update.Length == 0) return;
        PlayerSync msg = GMPObject.serializer.Deserialize<PlayerSync>(update);
        controllingPeerID = msg.controllingPeerID;
        isHuman = msg.isHuman;
        Position = msg.pos;
        Rotation = msg.rot;
        camera.Rotation = msg.headRot;
        this.Velocity = msg.vel;
    }




}

[GenerateShape]
public partial record struct PlayerSync
{
    public ulong controllingPeerID;
    public bool isHuman;
    public Vector3 pos;
    public Vector3 rot;
    public Vector3 headRot;
    public string[] hotbarItems;
    public string[] gridItems;
    public int equippedSlot;
    public Vector3 vel;
}