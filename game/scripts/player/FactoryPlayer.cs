using Godot;
using ImGuiNET;
using System.Collections.Generic;
using Nerdbank.MessagePack;
using PolyType;

/// <summary>
/// The networked player character. This file holds identity, movement, mouse look, input dispatch
/// and state sync; features live in partial-class files alongside it:
/// FactoryPlayer.Interaction.cs (looks-at target + interact) and FactoryPlayer.Equipment.cs (hotbar, in-hand
/// item, dropping) and FactoryPlayer.Health.cs (damage from hits, regen, downed as a ball). A held <see cref="HeldItem"/> (magnet
/// rod, blueprint preview, ...) gets first refusal on input. The HUD and every menu belong to
/// <see cref="UIManager"/>, which binds the HUD to the local player when it spawns.
/// </summary>
public partial class FactoryPlayer : GMPOCharacter
{
    public static bool displayPlayerDebugInfo = false;
    /// <summary>Every player in the world, local and remote (e.g. for the items' sync priority by distance).</summary>
    public static readonly List<FactoryPlayer> all = new();
    /// <summary>The peer whose input drives this player (its authority, unless it has left).</summary>
    public ulong controllingPeerID;
    /// <summary>
    /// Which of the level's spawn points (<see cref="GameBootstrap.SpawnPoints"/>) is this player's: picked when they
    /// first join, kept in saves, and where <see cref="Respawn"/> sends them.
    /// </summary>
    public int spawnIndex;
    /// <summary>How quickly remote copies close the gap to where their controller says they are (1/s); 0 snaps.</summary>
    [Export] public float syncLerpRate = 10f;
    float gravityMagnitude = ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();
    Vector3 gravityDirection = ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3();
    Vector3 gravity;

    // ---- movement, after Source (Half-Life 2, Portal) ----
    // On the ground, friction slows the player (harder below stopSpeed, so stops are crisp) and acceleration only adds
    // speed toward where they steer, up to the wished speed: walking has a little weight, turning is instant. In the
    // air the wished speed is capped very low, so the player can't steer much directly, but turning the view while
    // strafing turns their momentum (air strafing). The legs (GMPCharacter) carry the player up steps and keep them on
    // slopes; standing on something moving (a belt, a body) carries them along.

    [ExportGroup("Movement")]
    [Export] public float walkSpeed = 4.5f;
    /// <summary>Speed while "sprint" (left Shift) is held, m/s.</summary>
    [Export] public float sprintSpeed = 7.0f;
    [Export] public float crouchSpeed = 2.2f;
    /// <summary>How fast speed builds toward the wished speed on the ground: this many times the wished speed per second.</summary>
    [Export] public float groundAcceleration = 10f;
    /// <summary>Ground friction (1/s): how fast speed bleeds off when not steering.</summary>
    [Export] public float friction = 6f;
    /// <summary>Below this speed (m/s) friction works as if at this speed, so a stop finishes quickly instead of creeping.</summary>
    [Export] public float stopSpeed = 1.5f;
    /// <summary>Air acceleration: this many times the (capped) wished speed per second.</summary>
    [Export] public float airAcceleration = 12f;
    /// <summary>The most the wished speed counts for in the air (m/s): how much steering can push or brake directly there.</summary>
    [Export] public float airSpeedCap = 2.0f;
    /// <summary>
    /// Air steering (1/s), like Quake's CPMA: while steering roughly the way it's already going, the player's motion turns
    /// toward where they steer this fast, keeping its speed. 0 is plain Source air.
    /// </summary>
    [Export] public float airControl = 6f;
    [Export] public float jumpHeight = 1.1f;
    /// <summary>A jump still works this long (s) after walking off an edge.</summary>
    [Export] public float coyoteTime = 0.12f;
    /// <summary>A jump pressed this long (s) before landing still happens on landing.</summary>
    [Export] public float jumpBufferTime = 0.12f;

    [ExportGroup("Crouch")]
    [Export] public float standHeight = 1.8f;
    [Export] public float crouchHeight = 1.15f;
    /// <summary>The eye drops by this much crouched.</summary>
    [Export] public float crouchEyeDrop = 0.65f;
    /// <summary>How fast the eye moves between standing and crouched heights (m/s).</summary>
    [Export] public float crouchEyeSpeed = 4f;

    [ExportGroup("Noclip")]
    /// <summary>Flying speed in noclip (m/s), and while "sprint" is held.</summary>
    [Export] public float noclipSpeed = 10f;
    [Export] public float noclipSprintSpeed = 30f;

    /// <summary>Velocity in the world (not relative to what it stands on), as of the last move.</summary>
    Vector3 velocity;
    /// <summary>Seconds since it was last on the floor, and since jump was last pressed (big: long ago).</summary>
    float sinceOnFloor = 10;
    float sinceJumpPressed = 10;
    bool crouching;
    /// <summary>How far the eye is below standing eye height now (eased toward crouchEyeDrop or 0).</summary>
    float eyeDrop;
    /// <summary>Velocity for the debug window.</summary>
    public Vector3 cachedVel => velocity;

    // ---- test input (console walk / jump, for the test harness, which can't press keys) ----
    /// <summary>Steering held for testWalkLeft more seconds (x right, y back, as Input.GetVector gives it).</summary>
    public Vector2 testWalk;
    public float testWalkLeft;
    public bool testSprint;
    public bool testJump;
    /// <summary>The highest the feet got since the last test jump, for the jump command's report.</summary>
    public float testPeakY;
    /// <summary>
    /// Radians of look per screen pixel of mouse movement. Look reads unscaled screen pixels, so this is the same at
    /// every window size; it matches the old canvas-scaled 0.002 at 3440x1440 (stretched from the 1152x648 base).
    /// </summary>
    public const float MouseSensitivity = 0.0009f;

    /// <summary>
    /// The crane this player pilots, or null; set on every peer. On the local player the body stands still, input goes
    /// to the crane and the crane poses the camera (<see cref="GantryCrane"/>); elsewhere it only moves where the player
    /// counts as looking from (<see cref="ViewTransform"/>).
    /// </summary>
    public GantryCrane piloting;

    public Inventory inventory = new();

    /// <summary>Remote copies: the latest replicated position and velocity, and how long ago it arrived (s).</summary>
    Vector3 remotePosition;
    Vector3 remoteVelocity;
    float remoteAge;
    bool heardRemote;


    [Export] public Camera3D camera;

    // The local pilot's camera is already at the crane; other peers don't have it, so ask the crane.
    public override Transform3D ViewTransform()
    {
        if (piloting != null && !isLocal)
        {
            return piloting.PilotView();
        }
        return camera.GlobalTransform;
    }
    [Export] Node3D head;
    [Export] Node3D body;
    [Export] Node3D itemHolder;

    Label3D playerNameLabel;

    /// <summary>Look pitch in radians; yaw is the body's own rotation.</summary>
    float lookPitch;
    /// <summary>The camera's offset from the body as authored in the scene (eye height).</summary>
    Vector3 cameraOffset;

    long currentSurface = 0;
    /// <summary>
    /// True for the local player, whose camera is top-level and out of physics interpolation, posed by
    /// <see cref="PoseCamera"/>. Interpolated, mouse look only showed up in full at the next 30 Hz physics tick.
    /// </summary>
    bool cameraPosed;

    /// <summary>True on the peer that controls this player (its authority).</summary>
    public bool isLocal => authority == Lobby.selfPeerID;

    /// <summary>The controlling player's lobby name, shown over the avatar; kept for saves after they leave.</summary>
    public string playerName { get; private set; } = "";

    public void SetPlayerName(string name)
    {
        playerName = name ?? "";
        playerNameLabel.Text = playerName;
    }

    public override void _Ready()
    {
        base._Ready();
        // Saved as the save's players, not as one of its world roots.
        AddToGroup(GameWorld.UnsavedGroup);
        all.Add(this);
        gravity = gravityDirection * gravityMagnitude;
        camera = GetNode<Camera3D>("Camera3D");
        cameraOffset = camera.Position;
        itemHolder = camera.GetNode<Node3D>("ItemHolder");
    }

    public override void AfterInit()
    {
        playerNameLabel = GetNode<Label3D>("label");
        SetPlayerName(Lobby.members.TryGetValue(controllingPeerID, out PlayerInfo info) ? info.Name : "");
        if (isLocal)
        {
            // Moves before GameWorld steps the world, so its body is where it is this tick, and what it lands on or
            // runs into is in this tick's hits.
            ProcessPhysicsPriority = -1;
            camera.Current = true;
            lookPitch = camera.Rotation.X;
            camera.TopLevel = true;
            camera.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
            cameraPosed = true;
            PoseCamera(false);
            // FMOD hears from the local player's camera (ears at the head, facing where they look).
            UIManager.BindHud(this);
            playerNameLabel.Hide();
            body.GetNode<MeshInstance3D>("m").CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            head.GetNode<MeshInstance3D>("head").CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly;
            // Replicate every local inventory change (pickups, drops, drag-and-drop, bootstrap seed).
            inventory.InventoryChanged += SyncInventory;
        }
        else
        {
            camera.Current = false;
        }
        UpdateEquippedItem();
        InitHealth();
    }

    public override void _ExitTree()
    {
        all.Remove(this);
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
        if (downed)
        {
            HandleDownedInput(@event);
            return;
        }
        if (piloting != null)
        {
            if (@event is InputEventMouseButton click && click.Pressed) UIManager.RefreshMouseMode();
            piloting.HandlePilotInput(@event);
            return;
        }
        HandleMouseInput(@event);
        HandleScrollWheel(@event);
        if (!UIManager.IsInventoryOpen && heldItem != null && heldItem.HandleInput(@event)) return;
        HandleInteractionInput(@event);
        HandleEquipmentInput(@event);
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

    /// <summary>
    /// The scroll wheel goes to the held item first (e.g. the magnet rod's hold distance), then the structure looked at
    /// (e.g. a catapult's strength), otherwise cycles the hotbar.
    /// </summary>
    void HandleScrollWheel(InputEvent @event)
    {
        if (UIManager.IsInventoryOpen) return;

        int step = @event.IsActionPressed(InputActions.scrollDown) ? +1 : @event.IsActionPressed(InputActions.scrollUp) ? -1 : 0;
        if (step == 0) return;

        if (heldItem != null && heldItem.HandleScroll(step)) return;
        if (pickTarget is Structure structure && structure.HandleScroll(step)) return;
        CycleHotbar(step);
    }

    // ---- movement ---------------------------------------------------------

    /// <summary>Turns the player and its camera to face a point (the console's look_at command).</summary>
    public void FaceTowards(Vector3 target)
    {
        Vector3 eye = GlobalPosition + GlobalBasis * cameraOffset;
        Vector3 direction = target - eye;
        Rotation = new Vector3(0, Mathf.Atan2(-direction.X, -direction.Z), 0);
        float flat = new Vector2(direction.X, direction.Z).Length();
        lookPitch = Mathf.Clamp(Mathf.Atan2(direction.Y, flat), Mathf.DegToRad(-89f), Mathf.DegToRad(89f));
        PoseCamera(false);
    }

    /// <summary>Puts the player at a spot at once, stopped (the console's tp command).</summary>
    public void Teleport(Vector3 position)
    {
        TeleportCharacter(new Transform3D(GlobalBasis, position));
        velocity = Vector3.Zero;
    }

    /// <summary>Turns noclip (flying through everything) on or off; the other peers follow through the player's state.</summary>
    public void SetNoclip(bool on)
    {
        noclip = on;
        velocity = Vector3.Zero;
        // Fly standing up.
        crouching = false;
        capsuleHeight = standHeight;
        eyeDrop = 0;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!isLocal)
        {
            FollowRemote(delta);
            return;
        }
        UpdateHealth((float)delta);
        // Fell out of the world (through a floor, off an edge): back to their spawn point. Noclip flies where it likes.
        if (!noclip && GlobalPosition.Y < GameWorld.KillPlaneY)
        {
            Logging.Log($"{playerName} fell below the kill plane at {GlobalPosition}, respawning", "FactoryPlayer");
            Respawn();
        }
        if (downed)
        {
            UpdateDowned((float)delta);
            return;
        }

        // The surface underfoot, for the footstep sounds (only the local player plays them).
        RayHit hit = PhysicsWorld.Raycast(new Vector3(GlobalPosition.X, GlobalPosition.Y+2.0f, GlobalPosition.Z), new Vector3(GlobalPosition.X, GlobalPosition.Y-4.0f, GlobalPosition.Z), ~(1L << 2));
        SurfaceTags tags = (SurfaceTags)hit.tags;

        if(tags.HasFlag(SurfaceTags.METAL))
        {
            currentSurface = 0; //metal
        }
        else if(tags.HasFlag(SurfaceTags.CONCRETE))
        {
            currentSurface = 13; //concrete
        }
        else if(tags.HasFlag(SurfaceTags.GLASS))
        {
            currentSurface = 9; //tile
        }
        else if(tags.HasFlag(SurfaceTags.PLASTIC))
        {
            currentSurface = 12; //wood panel
        }
        else if(tags.HasFlag(SurfaceTags.ROCK))
        {
            currentSurface = 13; //concrete
        }
        else if(tags.HasFlag(SurfaceTags.RUBBER))
        {
            currentSurface = 9; //tile
        }

        // Gameplay rays and held tools use where the player actually is this tick, not where it's drawn.
        PoseCamera(false);
        if (piloting == null)
        {
            UpdatePickTarget();
            UpdateDeconstruct((float)delta);
        }
        ApplyMovement((float)delta);
        PoseCamera(false);
    }

    /// <summary>Remote copy: eases toward where its controller says it is now (the latest state carried on at its velocity).</summary>
    void FollowRemote(double delta)
    {
        if (!heardRemote)
        {
            return;
        }
        remoteAge += (float)delta;
        // Carried on for a little while only, so a copy whose updates stop doesn't drift off.
        Vector3 predicted = remotePosition + remoteVelocity * Mathf.Min(remoteAge, 0.25f);
        // Frame-rate independent: the fraction of the gap closed this frame at syncLerpRate.
        float weight = 1f;
        if (syncLerpRate > 0)
        {
            weight = 1f - Mathf.Exp(-syncLerpRate * (float)delta);
        }
        Vector3 position = GlobalPosition.Lerp(predicted, weight);
        if (position.DistanceTo(predicted) > 5f)
        {
            // Far off (a teleport, a respawn): jump there rather than sweep through everything on the way.
            TeleportCharacter(new Transform3D(GlobalBasis, predicted));
            return;
        }
        GlobalPosition = position;
    }

    void ApplyMovement(float delta)
    {
        // Piloting a crane, the body stands still like with a menu open.
        bool menu = UIManager.BlocksGameplayInput || piloting != null;
        if (noclip)
        {
            Fly(menu, delta);
            return;
        }
        bool onFloor = IsOnFloor();
        sinceOnFloor += delta;
        sinceJumpPressed += delta;
        if (onFloor)
        {
            sinceOnFloor = 0;
        }
        if (!menu && Input.IsActionJustPressed(InputActions.jump))
        {
            sinceJumpPressed = 0;
        }
        if (testJump)
        {
            testJump = false;
            sinceJumpPressed = 0;
            testPeakY = GlobalPosition.Y;
        }
        testPeakY = Mathf.Max(testPeakY, GlobalPosition.Y);
        UpdateCrouch(!menu && Input.IsActionPressed(InputActions.crouch), onFloor, delta);

        // Where the player steers, flat, and how fast they'd like to go.
        Vector2 input = Vector2.Zero;
        if (!menu)
        {
            input = Input.GetVector(InputActions.left, InputActions.right, InputActions.forward, InputActions.backward);
        }
        bool sprinting = !menu && Input.IsActionPressed(InputActions.sprint);
        if (testWalkLeft > 0)
        {
            testWalkLeft -= delta;
            input = testWalk;
            sprinting = testSprint;
        }
        Vector3 wishDirection = GlobalBasis * new Vector3(input.X, 0, input.Y);
        wishDirection.Y = 0;
        float wishSpeed = wishDirection.Length();
        if (wishSpeed > 1)
        {
            wishSpeed = 1;
        }
        if (wishDirection != Vector3.Zero)
        {
            wishDirection = wishDirection.Normalized();
        }
        float maxSpeed = walkSpeed;
        if (crouching)
        {
            maxSpeed = crouchSpeed;
        }
        else if (sprinting)
        {
            maxSpeed = sprintSpeed;
        }
        wishSpeed *= maxSpeed;

        // Movement is worked out relative to what the player stands on, so standing still on a belt rides it.
        Vector3 ground = Vector3.Zero;
        if (onFloor)
        {
            ground = FloorVelocity();
        }
        Vector3 relative = velocity - ground;
        if (onFloor)
        {
            // The legs hold the height on the floor.
            relative.Y = 0;
            ApplyFriction(ref relative, delta);
            Accelerate(ref relative, wishDirection, wishSpeed, groundAcceleration, delta);
        }
        else
        {
            Accelerate(ref relative, wishDirection, Mathf.Min(wishSpeed, airSpeedCap), airAcceleration, delta);
            SteerInAir(ref relative, wishDirection, wishSpeed, delta);
            relative += gravity * delta;
        }

        bool canJump = onFloor || sinceOnFloor <= coyoteTime;
        if (sinceJumpPressed <= jumpBufferTime && canJump && relative.Y <= 0)
        {
            relative.Y = Mathf.Sqrt(2 * gravityMagnitude * jumpHeight);
            sinceJumpPressed = 10;
            sinceOnFloor = 10;
            AudioManager.Instance.PlayOneShot("event:/Footsteps/FS_Jump", 1.0f, GlobalPosition, new Dictionary<string, float>{["Surfaces"] = currentSurface});
        }

        velocity = Move(relative + ground, delta);
        Vector3 ground_velocity = new Vector3(velocity.X, 0, velocity.Z);
        if(sprinting && onFloor && ground_velocity.LengthSquared() > 3.0f)
        {
            AudioManager.Instance.PlayOneShot("event:/Footsteps/FS_Run", 1.0f, GlobalPosition, new Dictionary<string, float>{["Surfaces"] = currentSurface});
        }
        else if(ground_velocity.Length() > walkSpeed-1.0f && onFloor)
        {
            AudioManager.Instance.PlayOneShot("event:/Footsteps/FS_Walk", 1.0f, GlobalPosition, new Dictionary<string, float>{["Surfaces"] = currentSurface});
        }

    }

    /// <summary>
    /// Noclip: no gravity, friction or floor. Forward is where the camera looks (pitch included), jump rises, crouch
    /// sinks, sprint goes faster.
    /// </summary>
    void Fly(bool menu, float delta)
    {
        Vector2 input = Vector2.Zero;
        float rise = 0;
        bool sprinting = false;
        if (!menu)
        {
            input = Input.GetVector(InputActions.left, InputActions.right, InputActions.forward, InputActions.backward);
            if (Input.IsActionPressed(InputActions.jump))
            {
                rise += 1;
            }
            if (Input.IsActionPressed(InputActions.crouch))
            {
                rise -= 1;
            }
            sprinting = Input.IsActionPressed(InputActions.sprint);
        }
        if (testWalkLeft > 0)
        {
            testWalkLeft -= delta;
            input = testWalk;
            sprinting = testSprint;
        }
        Vector3 wishDirection = camera.GlobalBasis * new Vector3(input.X, 0, input.Y) + Vector3.Up * rise;
        if (wishDirection.LengthSquared() > 1)
        {
            wishDirection = wishDirection.Normalized();
        }
        float speed = noclipSpeed;
        if (sprinting)
        {
            speed = noclipSprintSpeed;
        }

        // Starts and stops at once, for placing yourself exactly.
        velocity = wishDirection * speed;
        velocity = Move(velocity, delta);
    }

    /// <summary>
    /// Turns the flat part of the motion toward where the player steers, keeping its speed: more the closer the steering
    /// is to the way it already goes (none from sideways or behind, where the air acceleration brakes and strafes).
    /// </summary>
    void SteerInAir(ref Vector3 v, Vector3 wishDirection, float wishSpeed, float delta)
    {
        if (airControl <= 0 || wishSpeed <= 0)
        {
            return;
        }
        Vector3 flat = new Vector3(v.X, 0, v.Z);
        float speed = flat.Length();
        if (speed < 0.01f)
        {
            return;
        }
        Vector3 direction = flat / speed;
        float along = direction.Dot(wishDirection);
        if (along <= 0)
        {
            return;
        }
        Vector3 turned = (direction + wishDirection * (airControl * along * along * delta)).Normalized();
        v.X = turned.X * speed;
        v.Z = turned.Z * speed;
    }

    /// <summary>Source's ground friction: speed falls by friction × max(speed, stopSpeed) per second.</summary>
    void ApplyFriction(ref Vector3 flatVelocity, float delta)
    {
        float speed = flatVelocity.Length();
        if (speed < 0.05f)
        {
            flatVelocity = Vector3.Zero;
            return;
        }
        float control = Mathf.Max(speed, stopSpeed);
        float newSpeed = Mathf.Max(0, speed - control * friction * delta);
        flatVelocity *= newSpeed / speed;
    }

    /// <summary>Source's acceleration: adds speed along direction only up to the wished speed, however fast the player already goes.</summary>
    static void Accelerate(ref Vector3 v, Vector3 direction, float wishSpeed, float acceleration, float delta)
    {
        if (wishSpeed <= 0)
        {
            return;
        }
        float currentSpeed = v.Dot(direction);
        float addSpeed = wishSpeed - currentSpeed;
        if (addSpeed <= 0)
        {
            return;
        }
        float accelerationSpeed = Mathf.Min(acceleration * wishSpeed * delta, addSpeed);
        v += direction * accelerationSpeed;
    }

    /// <summary>
    /// Crouches while held; stands back up once there's room. Crouching in the air pulls the legs up instead of
    /// lowering the head (the feet rise), so a crouch-jump clears higher ledges.
    /// </summary>
    void UpdateCrouch(bool held, bool onFloor, float delta)
    {
        if (held && !crouching)
        {
            crouching = true;
            capsuleHeight = crouchHeight;
            if (!onFloor)
            {
                float lift = standHeight - crouchHeight;
                TeleportCharacter(new Transform3D(GlobalBasis, GlobalPosition + Vector3.Up * lift));
                // The eye stays where it was.
                eyeDrop -= lift;
            }
        }
        else if (!held && crouching && CanFitHeight(standHeight))
        {
            crouching = false;
            capsuleHeight = standHeight;
        }
        float wanted = 0;
        if (crouching)
        {
            wanted = crouchEyeDrop;
        }
        eyeDrop = Mathf.MoveToward(eyeDrop, wanted, crouchEyeSpeed * delta);
    }

    /// <summary>
    /// Poses the local player's camera at eye height with the current look angles, applied at once rather than
    /// eased in over a physics tick. For drawing (<paramref name="interpolated"/>) it rides the body's interpolated
    /// position so walking stays smooth; otherwise it sits at the body's actual position, for gameplay rays.
    /// </summary>
    void PoseCamera(bool interpolated)
    {
        if (!cameraPosed || piloting != null || downed) return;
        Vector3 origin = interpolated ? GetGlobalTransformInterpolated().Origin : GlobalPosition;
        Basis yaw = GlobalBasis;
        Vector3 eye = cameraOffset - new Vector3(0, eyeDrop, 0);
        camera.GlobalTransform = new Transform3D(yaw * new Basis(Vector3.Right, lookPitch), origin + yaw * eye);
    }

    // ---- state sync -------------------------------------------------------

    public override byte[] GenerateStateUpdate()
    {
        PlayerSync msg = new PlayerSync
        {
            controllingPeerID = this.controllingPeerID,
            pos = this.Position,
            rot = this.Rotation,
            // Remote copies keep their camera as a child of the body, so send the pitch alone, not the
            // top-level camera's world rotation.
            headRot = cameraPosed ? new Vector3(lookPitch, 0, 0) : this.camera.Rotation,
            vel = velocity,
            height = capsuleHeight,
            noclip = noclip,
            spawnIndex = spawnIndex,
            equippedSlot = inventory.ActiveHotbarSlot,
            heldItemState = heldItem?.GenerateSyncState(),
        };
        return GMPObject.serializer.Serialize(msg);
    }

    public override void ApplyStateUpdate(byte[] update)
    {
        if (update == null || update.Length == 0) return;
        PlayerSync msg = GMPObject.serializer.Deserialize<PlayerSync>(update);
        controllingPeerID = msg.controllingPeerID;
        spawnIndex = msg.spawnIndex;
        if (isLocal)
        {
            // Only a spawn-time state reaches the local player: put it there.
            TeleportCharacter(new Transform3D(Basis.FromEuler(msg.rot), msg.pos));
        }
        else
        {
            Rotation = msg.rot;
            camera.Rotation = msg.headRot;
            remotePosition = msg.pos;
            remoteVelocity = msg.vel;
            remoteAge = 0;
            heardRemote = true;
            // Crouched copies are short for everyone, so items and players meet the right capsule.
            if (msg.height > 0 && !Mathf.IsEqualApprox(msg.height, capsuleHeight))
            {
                capsuleHeight = msg.height;
            }
            // A noclipping player's copies touch nothing either, so the host's copy doesn't shove items around.
            if (msg.noclip != noclip)
            {
                noclip = msg.noclip;
            }
        }
        // Remote copies mirror the controlling peer's hotbar selection so the held item shows for everyone.
        if (!isLocal && msg.equippedSlot != inventory.ActiveHotbarSlot)
        {
            inventory.ActiveHotbarSlot = msg.equippedSlot;
            UpdateEquippedItem();
        }
        // The held tool's own state (e.g. what the magnet rod is pulling), after the slot above so it reaches the right tool.
        if (!isLocal && heldItem != null)
        {
            heldItem.ApplySyncState(msg.heldItemState);
        }
    }

    // ---- debug ------------------------------------------------------------

    public override void _Process(double delta)
    {
        PoseCamera(true);
        PoseBallCamera();
        if (displayPlayerDebugInfo && isLocal)
        {
            UpdateDebugUI();
        }
    }

    private void UpdateDebugUI()
    {
        var cell = BuildGrid.WorldToCell(GlobalPosition);

        ImGui.Begin("debugui player", ref displayPlayerDebugInfo);
        ImGui.Text($"Peer ID: {controllingPeerID}");
        ImGui.Text($"Position: {GlobalPosition} | Velocity: {cachedVel}");
        ImGui.Text($"Grid Cell: ({cell.X}, {cell.Y}, {cell.Z})");
        ImGui.Text($"Highlighted Cell: {(BuildGrid.highlightedCell is (int, int, int) hc ? $"({hc.X}, {hc.Y}, {hc.Z})" : "none")}");
        ImGui.Text($"On Floor: {IsOnFloor()}");
        ImGui.Text($"Active Hotbar Slot: {inventory.ActiveHotbarSlot}");
        ImGui.Text($"Pick Target: {pickTarget?.Name ?? "none"}");
        ImGui.Text($"Inventory Open: {UIManager.IsInventoryOpen}");
        ImGui.End();
    }
}

/// <summary>Sent as an array of values; see the note on <see cref="WorldTickMessage"/> about [Key] numbers.</summary>
[GenerateShape]
public partial record struct PlayerSync
{
    [Key(0)] public ulong controllingPeerID;
    // Key 1 held isHuman once: don't reuse it.
    [Key(2)] public Vector3 pos;
    [Key(3)] public Vector3 rot;
    [Key(4)] public Vector3 headRot;
    [Key(5)] public int equippedSlot;
    [Key(6)] public Vector3 vel;
    /// <summary>The held tool's state for the other peers (<see cref="HeldItem.GenerateSyncState"/>), or null.</summary>
    [Key(7)] public byte[] heldItemState;
    /// <summary>The capsule's height (crouched or standing).</summary>
    [Key(8)] public float height;
    // Keys 9 and 10 held impacts once (they go through the physics world's hits now): don't reuse them.
    /// <summary>Flying through everything (<see cref="GMPOCharacter.noclip"/>).</summary>
    [Key(11)] public bool noclip;
    /// <summary>Which of the level's spawn points is this player's (<see cref="FactoryPlayer.spawnIndex"/>).</summary>
    [Key(12)] public int spawnIndex;
}
