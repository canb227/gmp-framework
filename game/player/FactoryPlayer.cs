using Godot;
using ImGuiNET;
using PolyType;

/// <summary>
/// The networked player character. This file holds identity, movement, mouse look, input dispatch
/// and state sync; features live in partial-class files alongside it:
/// FactoryPlayer.Interaction.cs (looks-at target + interact), FactoryPlayer.Grab.cs (physics grab tool),
/// and FactoryPlayer.Equipment.cs (hotbar, in-hand item, dropping). A held <see cref="HeldItem"/> (magnet
/// rod, blueprint preview, ...) gets first refusal on input. The inventory screen is
/// <see cref="InventoryUI"/> (the PlayerHUD root).
/// </summary>
public partial class FactoryPlayer : GMPOBox3DCharacter
{
    public static bool displayPlayerDebugInfo = false;
    public double distanceSinceStepSound = 0;
    public double distancePerStepSound = 2;
    public int team;
    public bool isHuman;
    public ulong controllingPeerID;
    float gravityMagnitude = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
    Vector3 gravityDirection = ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3();
    Vector3 gravity;
    public Vector3 cachedVel;
    public const float Speed = 5.0f;
    /// <summary>Walking speed while "sprint" (left Shift) is held, m/s.</summary>
    [Export] public float sprintSpeed = 9.0f;
    public Vector3 JumpVector = new Vector3(0, 5, 0);
    const float MouseSensitivity = 0.002f;

    public Inventory inventory = new();

    Vector3 Velocity = Vector3.Zero;

    [Export] public Camera3D camera;
    [Export] Node3D head;
    [Export] Node3D body;
    [Export] Node3D itemHolder;

    Label3D playerNameLabel;
    public InventoryUI hud;

    /// <summary>True on the peer that controls this player (its authority).</summary>
    public bool isLocal => authority == Lobby.selfPeerID;

    public override void _Ready()
    {
        base._Ready();
        gravity = gravityDirection * gravityMagnitude;
        camera = GetNode<Camera3D>("Camera3D");
        itemHolder = camera.GetNode<Node3D>("ItemHolder");
        hud = GetNode<InventoryUI>("PlayerHUD");
        ReadyInteraction();
        ReadyGrab();
    }

    public override void AfterInit()
    {
        playerNameLabel = GetNode<Label3D>("label");
        playerNameLabel.Text = Lobby.members[controllingPeerID].Name;
        if (isLocal)
        {
            camera.Current = true;
            Input.MouseMode = Input.MouseModeEnum.Captured;
            playerNameLabel.Hide();
            body.Hide();
            head.Hide();
            SubscribeGrabClaims();
            // Replicate every local inventory change (pickups, drops, drag-and-drop, bootstrap seed).
            inventory.InventoryChanged += SyncInventory;
        }
        else
        {
            camera.Current = false;
            // Every player scene carries a HUD, and Controls draw on screen whatever their parent, so other
            // players' HUDs would cover this peer's own (whichever is last in the tree wins).
            // Nothing on it is seen or used for them, so it needn't refresh either.
            hud.Hide();
            hud.ProcessMode = ProcessModeEnum.Disabled;
        }
        UpdateEquippedItem();
    }

    public override void _ExitTree()
    {
        // Unconditional: on LeaveLobby selfPeerID is already cleared by the time this runs.
        UnsubscribeGrabClaims();
    }

    // ---- input ------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (!isLocal) return;

        if (HandleMouseAndMenuInput(@event)) return;
        HandleScrollWheel(@event);
        if (!hud.isOpen && currentInHandItem != null && currentInHandItem.HandleInput(@event)) return;
        HandleInteractionInput(@event);
        HandleEquipmentInput(@event);
        HandleGrabInput(@event);
    }


    /// <summary>Mouse capture, mouse look, Escape and the inventory key. Returns true if the event was consumed.</summary>
    bool HandleMouseAndMenuInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && !hud.isOpen)
        {
            Input.MouseMode = Input.MouseModeEnum.Captured;
        }

        if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            if (hud.isOpen)
            {
                hud.Close();
                return true;
            }
            Input.MouseMode = Input.MouseModeEnum.Visible;
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

        if (@event.IsActionPressed("inventory"))
        {
            if (hud.isOpen)
                hud.Close();
            else
                hud.Open();
        }
        return false;
    }

    /// <summary>The scroll wheel moves the grab point while grabbing, otherwise cycles the hotbar.</summary>
    void HandleScrollWheel(InputEvent @event)
    {
        if (hud.isOpen) return;

        int step = @event.IsActionPressed("scrollDown") ? +1 : @event.IsActionPressed("scrollUp") ? -1 : 0;
        if (step == 0) return;

        if (isGrabbing)
            MoveGrabPoint(step);
        else if (currentInHandItem == null || !currentInHandItem.HandleScroll(step))
            CycleHotbar(step);
    }

    // ---- movement ---------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (!isLocal)
        {
            // Remote copy: keep sliding along the last replicated velocity between state updates.
            Call("move_and_slide", [Velocity, delta]);
            return;
        }

        ApplyGrabForce(delta);
        UpdatePickTarget();
        ApplyMovement(delta);
        if (distanceSinceStepSound > distancePerStepSound)
        {
            AudioManager.playRandomSound(this.GetPath(), ["res://game/assets/audio/impacts/footstep_concrete_000.ogg", "res://game/assets/audio/impacts/footstep_concrete_001.ogg", "res://game/assets/audio/impacts/footstep_concrete_002.ogg", "res://game/assets/audio/impacts/footstep_concrete_003.ogg", "res://game/assets/audio/impacts/footstep_concrete_004.ogg"]);
            distanceSinceStepSound = 0;
        }
    }

    void ApplyMovement(double delta)
    {
        if (!IsOnFloor())
        {
            Velocity += gravity * (float)delta;
        }
        else if (Input.IsActionJustPressed("jump"))
        {
            Velocity = Velocity + JumpVector;
        }

        Vector2 inputDir = Input.GetVector("left", "right", "forward", "backward");
        Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
        if (direction != Vector3.Zero)
        {
            float speed = Input.IsActionPressed("sprint") ? sprintSpeed : Speed;
            Velocity.X = direction.X * speed;
            Velocity.Z = direction.Z * speed;
        }
        else
        {
            Velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
            Velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
        }
        cachedVel = Velocity;
        Velocity = Call("move_and_slide", [Velocity, delta]).AsVector3();
        distanceSinceStepSound += (Velocity.Length() * delta);
    }

    // ---- state sync -------------------------------------------------------

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
            equippedSlot = inventory.ActiveHotbarSlot,
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
        // Remote copies mirror the controlling peer's hotbar selection so the held item shows for everyone.
        if (!isLocal && msg.equippedSlot != inventory.ActiveHotbarSlot)
        {
            inventory.ActiveHotbarSlot = msg.equippedSlot;
            UpdateEquippedItem();
        }
    }

    // ---- debug ------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (displayPlayerDebugInfo && isLocal)
        {
            UpdateDebugUI();
        }
    }

    private void UpdateDebugUI()
    {
        var cell = BuildGrid.WorldToCell(GlobalPosition);

        ImGui.Begin("debugui player");
        ImGui.Text($"Peer ID: {controllingPeerID} | Team: {team} | Human: {isHuman}");
        ImGui.Text($"Position: {GlobalPosition} | Velocity: {cachedVel}");
        ImGui.Text($"Grid Cell: ({cell.X}, {cell.Y}, {cell.Z})");
        ImGui.Text($"Highlighted Cell: {(BuildGrid.highlightedCell is (int, int, int) hc ? $"({hc.X}, {hc.Y}, {hc.Z})" : "none")}");
        ImGui.Text($"On Floor: {IsOnFloor()}");
        ImGui.Text($"Active Hotbar Slot: {inventory.ActiveHotbarSlot}");
        ImGui.Text($"Pick Target: {pickTarget?.Name ?? "none"}");
        ImGui.Text($"Grab Target: {grabTarget?.Name ?? "none"}");
        ImGui.Text($"Inventory Open: {hud.isOpen}");
        ImGui.End();
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
    public int equippedSlot;
    public Vector3 vel;
}
