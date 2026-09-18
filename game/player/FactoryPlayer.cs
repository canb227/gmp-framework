using Godot;
using Godot.Collections;
using PolyType;
using System;

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

    public Inventory inventory = new();

    Vector3 Velocity = Vector3.Zero;

    [Export] Camera3D camera;
    [Export] Node3D head;
    [Export] Node3D body;
    [Export] Node3D itemHolder;

    Label3D playerNameLabel;
    public bool inventoryOpen = false;

    [Export] public float pickRange = 5f;
    public Node3D pickTarget;
    public Control hud;

    Node3D currentInHandInstance;
    int previousHotbarSlot = -1;
    private bool isGrabbing;
    private PhysicalFactoryItem grabTarget;
    public Node3D grabNode;
    public float defaultGrabLocation;
    private float grabMinimumDistance = -1f;
    private float grabDistanceIncrement = .25f;
    private float grabMaximumDistance = -4f;

    private float grabForceBase = 25f;
    private float grabForceExponent = 1.2f;
    private float grabDamping = 10f;
    private float grabNearDamping = 5f;
    private float grabMaxForce = 400f;

    public override void _Ready()
    {
        base._Ready();
        gravity = gravityDirection * gravityMagnitude;
        camera = GetNode<Camera3D>("Camera3D");
        itemHolder = camera.GetNode<Node3D>("ItemHolder");
        hud = GetNode<Control>("PlayerHUD");
        grabNode = GetNode<Node3D>("%grabNode");
        defaultGrabLocation = grabNode.Position.Z;
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
                if (inventoryOpen)
                {
                    CloseInventory();
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

        // Item pickup
        if (@event.IsActionPressed("interact"))
        {
            if (pickTarget != null && pickTarget is PhysicalFactoryItem item)
            {
                Logging.Log($"You just pressed interact on {item.Name}!", "Player");
                if (item.canBePickedUp)
                {
                    InventoryItem ii = ResourceLoader.Load<InventoryItem>(GameResources.Items[item.itemID].inventoryItemPath);
                    int leftover = inventory.AddItem(ii, 1);
                    if (leftover == 0)
                    {
                        GameWorld.DespawnObject(item.id);
                    }
                }
            }
        }

        // Inventory toggle
        if (@event.IsActionPressed("inventory"))
        {
            if (inventoryOpen)
                CloseInventory();
            else
                OpenInventory();
        }




        // Scroll wheel hotbar cycling
        if (@event.IsActionPressed("scrollDown"))
        {
            if (!inventoryOpen && grabTarget == null)
            {
                if (inventory.ActiveHotbarSlot == -1 || inventory.ActiveHotbarSlot > Inventory.HotbarSlots)
                {
                    inventory.ActiveHotbarSlot = 0;
                }
                inventory.ActiveHotbarSlot = (inventory.ActiveHotbarSlot + 1) % Inventory.HotbarSlots;
                UpdateEquippedItem();
            }
            else if (!inventoryOpen && grabTarget != null)
            {

                    grabNode.Position = new Vector3(grabNode.Position.X, grabNode.Position.Y, Math.Min(grabNode.Position.Z + grabDistanceIncrement, grabMinimumDistance));

            }
        }
        if (@event.IsActionPressed("scrollUp"))
        {
            if (!inventoryOpen && grabTarget == null)
            {
                if (inventory.ActiveHotbarSlot == -1 || inventory.ActiveHotbarSlot > Inventory.HotbarSlots)
                {
                    inventory.ActiveHotbarSlot = 0;
                }
                inventory.ActiveHotbarSlot = (inventory.ActiveHotbarSlot - 1 + Inventory.HotbarSlots) % Inventory.HotbarSlots;
                UpdateEquippedItem();
            }
            else if (!inventoryOpen && grabTarget != null)
            {

                    grabNode.Position = new Vector3(grabNode.Position.X, grabNode.Position.Y, Math.Max(grabNode.Position.Z - grabDistanceIncrement, grabMaximumDistance));
                

            }
        }
    

        // Drop item (Q) — drop 1 from active hotbar slot
        if (@event.IsActionPressed("drop"))
        {
            DropFromActiveSlot(1);
        }

        if (@event.IsActionPressed("primary"))

        {
            if (inventory.ActiveHotbarSlot==-1 || inventory.slots[inventory.ActiveHotbarSlot].IsEmpty)
            {
                Logging.Log($"empty click: {inventory.ActiveHotbarSlot}","GameWorld");
                if (pickTarget != null && pickTarget is PhysicalFactoryItem item && item.canBeGrabbed)
                {
                    Logging.Log($"starting grab on {item.Name}", "Player");
                    grabTarget = item;
                    grabTarget.Set("gravity_scale", 0.1f);
                    grabTarget.Set("linear_damping", 5f);
                    grabTarget.Set("angular_damping", 4f);
                    GameWorld.Claim(item);
                }

            }
        }
        if (@event.IsActionReleased("primary"))
        {
            if (grabTarget!=null)
            {
                grabTarget.Set("gravity_scale", 1f);
                grabTarget.Set("linear_damping", 0f);
                grabTarget.Set("angular_damping", 0f);
                Logging.Log($"ending grab on {grabTarget.Name}", "Player");
                grabNode.Position = new Vector3(grabNode.Position.X, grabNode.Position.Y, defaultGrabLocation);
                grabTarget = null;
            }

        }
        else
        {
            // Hotbar slot selection via number keys
            for (int i = 0; i <= 9; i++)
            {
                if (@event.IsActionPressed($"slot{i}"))
                {
                    if (inventory.ActiveHotbarSlot == i)
                    {
                        inventory.ActiveHotbarSlot = -1;
                        UpdateEquippedItem();
                        break;
                    }
                    inventory.ActiveHotbarSlot = i;
                    UpdateEquippedItem();
                    break;
                }

            }
        }
    }

    void OpenInventory()
    {
        inventoryOpen = true;
        hud.GetNode<Control>("InventoryScreen").Show();
        hud.MouseFilter = Control.MouseFilterEnum.Stop;
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    void CloseInventory()
    {
        inventoryOpen = false;
        hud.GetNode<Control>("InventoryScreen").Hide();
        hud.MouseFilter = Control.MouseFilterEnum.Ignore;
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public void UpdateEquippedItem()
    {
        if (currentInHandInstance != null)
        {
            currentInHandInstance.QueueFree();
            currentInHandInstance = null;
        }

        InventoryItem equipped = inventory.GetEquippedItem();
        if (equipped?.inHandScene != null)
        {
            currentInHandInstance = equipped.inHandScene.Instantiate<Node3D>();
            itemHolder.AddChild(currentInHandInstance);
        }

        previousHotbarSlot = inventory.ActiveHotbarSlot;
    }

    public void DropFromActiveSlot(int count)
    {
        var slot = inventory.GetSlot(inventory.ActiveHotbarSlot);
        if (slot.IsEmpty) return;
        if (slot.Item.droppedScene == null) return;

        int toDrop = Math.Min(count, slot.Count);
        Vector3 dropPos = camera.GlobalPosition + -camera.GlobalTransform.Basis.Z * 2f;
        Vector3 dropRot = GlobalRotation;

        for (int i = 0; i < toDrop; i++)
        {
            Vector3 offset = new Vector3(
                (float)(Random.Shared.NextDouble() - 0.5) * 0.5f,
                0,
                (float)(Random.Shared.NextDouble() - 0.5) * 0.5f
            );
            GameWorld.SpawnScene(slot.Item.droppedScene.ResourcePath, dropPos + offset, dropRot);
        }

        inventory.RemoveFromSlot(inventory.ActiveHotbarSlot, toDrop);
        UpdateEquippedItem();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Lobby.selfPeerID != authority)
        {
            Call("move_and_slide", [Velocity, delta]);
            return;
        }

        if (grabTarget != null)
        {
            Vector3 displacement = grabNode.GlobalPosition - grabTarget.GlobalPosition;
            float distance = displacement.Length();

            if (distance > 0.001f)
            {
                Vector3 forceDir = displacement / distance;

                float forceMagnitude = Mathf.Min(
                    grabForceBase * (Mathf.Exp(grabForceExponent * distance) - 1f),
                    grabMaxForce
                );
                Vector3 attractionForce = forceDir * forceMagnitude;

                Vector3 velocity = (Vector3)grabTarget.Get("linear_velocity");
                float dampingScale = grabDamping * (1f + grabNearDamping / Mathf.Max(distance, 0.1f));
                Vector3 dampingForce = -velocity * dampingScale;

                grabTarget.ApplyCentralForce(attractionForce + dampingForce);
            }
        }
         
        Dictionary<string, Variant> ray = GameWorld.Raycast(camera.GlobalPosition, camera.GlobalPosition + -camera.GlobalTransform.Basis.Z * pickRange);
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
            else if (hit is BasicButton button)
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
            vel = this.cachedVel,
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
