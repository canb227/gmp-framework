using Godot;
using ImGuiNET;
using PolyType;

/// <summary>
/// The networked player character. This file holds identity, movement, mouse look, input dispatch
/// and state sync; features live in partial-class files alongside it:
/// FactoryPlayer.Interaction.cs (looks-at target + interact), FactoryPlayer.Grab.cs (physics grab tool),
/// and FactoryPlayer.Equipment.cs (hotbar, in-hand item, dropping). A held <see cref="HeldItem"/> (magnet
/// rod, blueprint preview, ...) gets first refusal on input. The HUD and every menu belong to
/// <see cref="UIManager"/>, which binds the HUD to the local player when it spawns.
/// </summary>
public partial class FactoryPlayer : GMPOBox3DCharacter
{
    public static bool displayPlayerDebugInfo = false;
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
    /// <summary>
    /// Radians of look per screen pixel of mouse movement. Look reads unscaled screen pixels, so this is the same at
    /// every window size; it matches the old canvas-scaled 0.002 at 3440x1440 (stretched from the 1152x648 base).
    /// </summary>
    const float MouseSensitivity = 0.0009f;

    public Inventory inventory = new();

    Vector3 Velocity = Vector3.Zero;

    [Export] public Camera3D camera;
    [Export] Node3D head;
    [Export] Node3D body;
    [Export] Node3D itemHolder;

    Label3D playerNameLabel;

    /// <summary>Look pitch in radians; yaw is the body's own rotation.</summary>
    float lookPitch;
    /// <summary>The camera's offset from the body as authored in the scene (eye height).</summary>
    Vector3 cameraOffset;
    /// <summary>
    /// True for the local player, whose camera is top-level and out of physics interpolation, posed by
    /// <see cref="PoseCamera"/>. Interpolated, mouse look only showed up in full at the next 30 Hz physics tick.
    /// </summary>
    bool cameraPosed;

    /// <summary>True on the peer that controls this player (its authority).</summary>
    public bool isLocal => authority == Lobby.selfPeerID;

    public override void _Ready()
    {
        base._Ready();
        gravity = gravityDirection * gravityMagnitude;
        camera = GetNode<Camera3D>("Camera3D");
        cameraOffset = camera.Position;
        itemHolder = camera.GetNode<Node3D>("ItemHolder");
        grabNode = GetNode<Node3D>("%grabNode");
    }

    public override void AfterInit()
    {
        playerNameLabel = GetNode<Label3D>("label");
        playerNameLabel.Text = Lobby.members[controllingPeerID].Name;
        if (isLocal)
        {
            camera.Current = true;
            lookPitch = camera.Rotation.X;
            camera.TopLevel = true;
            camera.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
            cameraPosed = true;
            PoseCamera(false);
            UIManager.BindHud(this);
            playerNameLabel.Hide();
            body.GetNode<MeshInstance3D>("m").CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            head.GetNode<MeshInstance3D>("head").CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            GameWorld.ClaimGrantedEvent += OnClaimGranted;
            // Replicate every local inventory change (pickups, drops, drag-and-drop, bootstrap seed).
            inventory.InventoryChanged += SyncInventory;
        }
        else
        {
            camera.Current = false;
        }
        UpdateEquippedItem();
    }

    public override void _ExitTree()
    {
        // Unconditional: on LeaveLobby selfPeerID is already cleared by the time this runs.
        GameWorld.ClaimGrantedEvent -= OnClaimGranted;
        UIManager.UnbindHud(this);
    }

    // ---- input ------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        // Mouse motion arrives every frame as a fresh native event, so its C# wrapper is garbage right after this
        // call; dispose it now rather than leave it to the finalizer (each one lengthens the next GC pause).
        try { UnhandledInput(@event); }
        finally { if (@event is InputEventMouseMotion) @event.Dispose(); }
    }

    void UnhandledInput(InputEvent @event)
    {
        if (!isLocal) return;

        // Menus (pause, shop, ...) take every input while open; UIManager handles Escape and the inventory key.
        if (UIManager.BlocksGameplayInput) return;
        HandleMouseInput(@event);
        HandleScrollWheel(@event);
        if (!UIManager.IsInventoryOpen && heldItem != null && heldItem.HandleInput(@event)) return;
        HandleInteractionInput(@event);
        HandleEquipmentInput(@event);
        HandleGrabInput(@event);
    }

    /// <summary>Mouse look, and recapturing the mouse on a click (after the window lost it).</summary>
    void HandleMouseInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            UIManager.RefreshMouseMode();
        }

        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            // ScreenRelative, not Relative: Relative is scaled by the canvas stretch, so look speed changed with window size.
            float sensitivity = MouseSensitivity * GameSettings.mouseSensitivity;
            RotateY(-motion.ScreenRelative.X * sensitivity);
            lookPitch = Mathf.Clamp(lookPitch - motion.ScreenRelative.Y * sensitivity * (GameSettings.invertMouseY ? -1 : 1),
                Mathf.DegToRad(-89f), Mathf.DegToRad(89f));
            PoseCamera(false);
        }
    }

    /// <summary>The scroll wheel moves the grab point while grabbing, otherwise cycles the hotbar.</summary>
    void HandleScrollWheel(InputEvent @event)
    {
        if (UIManager.IsInventoryOpen) return;

        int step = @event.IsActionPressed(InputActions.scrollDown) ? +1 : @event.IsActionPressed(InputActions.scrollUp) ? -1 : 0;
        if (step == 0) return;

        if (isGrabbing)
            SetGrabDistance(grabDistance - step * grabScrollStep);
        else if (heldItem == null || !heldItem.HandleScroll(step))
            CycleHotbar(step);
    }

    // ---- movement ---------------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        if (!isLocal)
        {
            // Remote copy: keep sliding along the last replicated velocity between state updates.
            MoveAndSlide(Velocity, delta);
            return;
        }

        // Gameplay rays and the grab point use where the player actually is this tick, not where it's drawn.
        PoseCamera(false);
        ApplyGrabForce(delta);
        UpdatePickTarget();
        ApplyMovement(delta);
        PoseCamera(false);
    }

    void ApplyMovement(double delta)
    {
        if (!IsOnFloor())
        {
            Velocity += gravity * (float)delta;
        }
        else if (Input.IsActionJustPressed(InputActions.jump) && !UIManager.BlocksGameplayInput)
        {
            Velocity += JumpVector;
        }

        // No walking while a menu has the keyboard.
        Vector2 inputDir = UIManager.BlocksGameplayInput ? Vector2.Zero : Input.GetVector(InputActions.left, InputActions.right, InputActions.forward, InputActions.backward);
        Vector3 direction = (Transform.Basis * new Vector3(inputDir.X, 0, inputDir.Y)).Normalized();
        if (direction != Vector3.Zero)
        {
            float speed = Input.IsActionPressed(InputActions.sprint) ? sprintSpeed : Speed;
            Velocity.X = direction.X * speed;
            Velocity.Z = direction.Z * speed;
        }
        else
        {
            Velocity.X = Mathf.MoveToward(Velocity.X, 0, Speed);
            Velocity.Z = Mathf.MoveToward(Velocity.Z, 0, Speed);
        }
        cachedVel = Velocity;
        Velocity = MoveAndSlide(Velocity, delta);
    }

    /// <summary>
    /// Poses the local player's camera at eye height with the current look angles, applied at once rather than
    /// eased in over a physics tick. For drawing (<paramref name="interpolated"/>) it rides the body's interpolated
    /// position so walking stays smooth; otherwise it sits at the body's actual position, for gameplay rays.
    /// </summary>
    void PoseCamera(bool interpolated)
    {
        if (!cameraPosed) return;
        Vector3 origin = interpolated ? GetGlobalTransformInterpolated().Origin : GlobalPosition;
        Basis yaw = GlobalBasis;
        camera.GlobalTransform = new Transform3D(yaw * new Basis(Vector3.Right, lookPitch), origin + yaw * cameraOffset);
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
            // Remote copies keep their camera as a child of the body, so send the pitch alone, not the
            // top-level camera's world rotation.
            headRot = cameraPosed ? new Vector3(lookPitch, 0, 0) : this.camera.Rotation,
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
        PoseCamera(true);
        if (displayPlayerDebugInfo && isLocal)
        {
            UpdateDebugUI();
        }
    }

    private void UpdateDebugUI()
    {
        var cell = BuildGrid.WorldToCell(GlobalPosition);

        ImGui.Begin("debugui player", ref displayPlayerDebugInfo);
        ImGui.Text($"Peer ID: {controllingPeerID} | Team: {team} | Human: {isHuman}");
        ImGui.Text($"Position: {GlobalPosition} | Velocity: {cachedVel}");
        ImGui.Text($"Grid Cell: ({cell.X}, {cell.Y}, {cell.Z})");
        ImGui.Text($"Highlighted Cell: {(BuildGrid.highlightedCell is (int, int, int) hc ? $"({hc.X}, {hc.Y}, {hc.Z})" : "none")}");
        ImGui.Text($"On Floor: {IsOnFloor()}");
        ImGui.Text($"Active Hotbar Slot: {inventory.ActiveHotbarSlot}");
        ImGui.Text($"Pick Target: {pickTarget?.Name ?? "none"}");
        ImGui.Text($"Grab Target: {grabbedItem?.Name ?? "none"}");
        ImGui.Checkbox("Empty-hand grab", ref emptyHandGrabEnabled);
        ImGui.Text($"Inventory Open: {UIManager.IsInventoryOpen}");
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
