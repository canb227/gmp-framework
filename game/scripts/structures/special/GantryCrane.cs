using Godot;
using PolyType;
using System.Collections.Generic;

/// <summary>Where a gantry crane's head is: everything its moving parts are posed from. Sent by its authority (the host).</summary>
[GenerateShape]
public partial record struct GantryCraneState
{
    /// <summary>The bridge's (and head's) Z, in the crane's frame.</summary>
    public float bridgeZ;
    /// <summary>The trolley's (and head's) X, in the crane's frame.</summary>
    public float trolleyX;
    /// <summary>How far the head hangs below the trolley.</summary>
    public float cableLength;
    /// <summary>0: the bucket is shut, 1: wide open.</summary>
    public float jawOpen;
}

/// <summary>
/// A gantry crane, placed by hand in a level (no blueprint; it can't be taken apart). It fills a box of
/// <see cref="size"/>: x 0..size.X, y 0..size.Y, z -size.Z..0 in its own frame, like any structure. Two rails run
/// along Z at the top; a bridge rides them, a trolley rides the bridge along X, and a clamshell bucket hangs from the
/// trolley on a cable. Rails, bridge, trolley, head and bucket are plain boxes, made here when it enters the tree (and
/// remade in the editor on every change), as children with no owner so they are never saved into the scene.
/// <para>
/// The cockpit is a GMPBody placed by hand under the crane, its origin on the left-bottom-back corner of its cells
/// (<see cref="cockpitCells"/> of them). Only those cells are the crane's on the build grid. Interacting with the
/// cockpit pilots the crane: the camera orbits the head, WASD moves it along the crane's axes (whichever is nearest to
/// where the camera faces), jump and crouch raise and lower it, primary opens the bucket and secondary shuts it, and
/// interact leaves.
/// </para>
/// <para>
/// The host runs it (it simulates every loose item, so only its bucket really moves anything). The pilot sends the
/// keys it holds to the host whenever they change; the host moves the crane and sends its <see cref="GantryCraneState"/>,
/// which the other peers play back on the host's playback clock, in step with the items. While the bucket is shut the
/// host pulls what is in it toward its middle, so a grab holds.
/// </para>
/// </summary>
[Tool]
public partial class GantryCrane : Structure
{
    Vector3 _size = new Vector3(8, 5, 8);
    /// <summary>The box the crane fills, from its root's corner: X wide, Y high (the rails' tops), Z long (toward -Z).</summary>
    [Export(PropertyHint.None, "suffix:m")]
    public Vector3 size
    {
        get { return _size; }
        set
        {
            _size = value;
            Rebuild();
        }
    }

    /// <summary>The cockpit body, placed by hand under the crane.</summary>
    [Export] public Node3D cockpit;
    /// <summary>How many build grid cells the cockpit fills (X, Y, and Z toward -Z), from its origin's corner.</summary>
    [Export] public Vector3I cockpitCells = new Vector3I(1, 2, 1);

    [ExportGroup("Driving")]
    /// <summary>Top speed of the bridge and trolley, m/s.</summary>
    [Export] public float moveSpeed = 3f;
    /// <summary>How fast the bridge and trolley speed up and slow down, m/s².</summary>
    [Export] public float moveAcceleration = 4f;
    /// <summary>How fast the cable reels in and out, m/s.</summary>
    [Export] public float liftSpeed = 1.5f;
    /// <summary>How fast the bucket opens and shuts: fractions of the way per second.</summary>
    [Export] public float jawSpeed = 1.2f;
    /// <summary>How far the bucket halves swing open.</summary>
    [Export] public float jawOpenDegrees = 75f;

    [ExportGroup("Grip")]
    /// <summary>The bucket pulls what is in it only while it's shut further than this (jawOpen below it).</summary>
    [Export] public float gripBelowOpen = 0.15f;
    /// <summary>How hard it pulls, (m/s²) per metre an item is from the middle of the bucket.</summary>
    [Export] public float gripStrength = 30f;

    [ExportGroup("Pilot camera")]
    [Export] public float cameraDistance = 5f;
    [Export] public float cameraMinDistance = 2f;
    [Export] public float cameraMaxDistance = 14f;

    // ---- the parts' sizes (m) ----
    const float RailThickness = 0.3f;
    /// <summary>From the rails' tops down to the trolley's underside, where the cable starts.</summary>
    const float TrolleyDrop = 0.6f;
    const float HeadHeight = 0.3f;
    const float BucketHalfWidth = 0.6f;
    const float BucketHalfLength = 0.5f;
    const float BucketDepth = 0.7f;
    const float PlateThickness = 0.05f;
    const float MinCable = 0.2f;

    /// <summary>Every peer: the pose shown now (on the host, the one being simulated).</summary>
    GantryCraneState state;
    /// <summary>A spawn-time state (from a save) was given, so it doesn't start at <see cref="StartState"/>.</summary>
    bool hasState;
    /// <summary>Host: how fast the trolley (x), cable (y) and bridge (z) are moving.</summary>
    Vector3 velocity;

    /// <summary>The player object piloting it (0: none), on every peer.</summary>
    public ulong pilotID { get; private set; }
    /// <summary>Host: what the pilot holds: move along X and Z (-1, 0, 1), lift (+1 up), jaw (+1 opens).</summary>
    Vector2I pilotMove;
    int pilotLift;
    int pilotJaw;

    /// <summary>The local player, while it pilots this crane.</summary>
    FactoryPlayer localPilot;
    /// <summary>Pilot's peer: the keys last sent to the host, so only changes are sent.</summary>
    Vector2I sentMove;
    int sentLift;
    int sentJaw;
    float cameraYaw;
    float cameraPitch = Mathf.DegToRad(-30f);

    /// <summary>Followers: received states and the host's tick each was sent on, oldest first.</summary>
    readonly List<(ulong tick, GantryCraneState state)> received = new();

    // The parts, made by Rebuild.
    readonly List<Node> made = new();
    Node3D bridge;
    Node3D trolley;
    Node3D head;
    Node3D leftJaw;
    Node3D rightJaw;
    Node3D gripSensor;
    MeshInstance3D cable;

    static readonly StringName getOverlappingName = "get_overlapping";

    // ---- building the parts ----

    // Before ready, so the root body (ready after its children) takes the rails as its own shapes.
    public override void _EnterTree()
    {
        if (made.Count == 0 || Engine.IsEditorHint())
        {
            Rebuild();
        }
    }

    // (Re)makes the rails and the moving parts as children with no owner, so they are never saved into the scene.
    void Rebuild()
    {
        if (!IsInsideTree())
        {
            return;
        }
        foreach (Node old in made)
        {
            RemoveChild(old);
            old.QueueFree();
        }
        made.Clear();

        StandardMaterial3D steel = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.46f, 0.48f), Metallic = 0.7f, Roughness = 0.4f };
        StandardMaterial3D paint = new StandardMaterial3D { AlbedoColor = new Color(0.9f, 0.65f, 0.1f), Roughness = 0.6f };
        StandardMaterial3D dark = new StandardMaterial3D { AlbedoColor = new Color(0.15f, 0.15f, 0.16f), Roughness = 0.7f };

        // Rails: shapes of the crane's own (static) body.
        float railY = size.Y - RailThickness / 2f;
        Vector3 railSize = new Vector3(RailThickness, RailThickness, size.Z);
        made.Add(AddBox(this, "RailLeft", railSize, new Vector3(RailThickness / 2f, railY, -size.Z / 2f), steel));
        made.Add(AddBox(this, "RailRight", railSize, new Vector3(size.X - RailThickness / 2f, railY, -size.Z / 2f), steel));

        // Bridge: spans between the rails; its node sits at the crane's origin, moved along Z.
        bridge = AddBody(this, "Bridge");
        AddBox(bridge, "Beam", new Vector3(size.X - 2 * RailThickness, RailThickness, 0.4f), new Vector3(size.X / 2f, railY, 0), paint);

        // Trolley: its node at the middle of its underside, where the cable starts.
        trolley = AddBody(this, "Trolley");
        AddBox(trolley, "Carriage", new Vector3(0.6f, TrolleyDrop - RailThickness, 0.6f), new Vector3(0, (TrolleyDrop - RailThickness) / 2f, 0), paint);

        // Cable: drawn only, from the trolley down to the head (scaled to its length).
        cable = new MeshInstance3D { Name = "Cable", Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 1f }, MaterialOverride = dark };
        AddChild(cable);
        made.Add(cable);

        // Head: its node at the middle of its top, where the cable meets it. The bucket halves hinge on its underside.
        head = AddBody(this, "Head");
        AddBox(head, "Block", new Vector3(0.8f, HeadHeight, 0.8f), new Vector3(0, -HeadHeight / 2f, 0), paint);
        leftJaw = AddJaw(head, "LeftJaw", -1, steel);
        rightJaw = AddJaw(head, "RightJaw", 1, steel);
        // What the shut bucket holds: a sensor of the head's, filling the bucket.
        gripSensor = ClassDB.Instantiate("GMPShape").As<Node3D>();
        gripSensor.Name = "GripSensor";
        gripSensor.Position = new Vector3(0, -HeadHeight - BucketDepth / 2f, 0);
        gripSensor.Set("size", new Vector3(2 * BucketHalfWidth - 0.1f, BucketDepth - 0.1f, 2 * BucketHalfLength - 0.1f));
        gripSensor.Set("is_sensor", true);
        head.AddChild(gripSensor);

        made.Add(bridge);
        made.Add(trolley);
        made.Add(head);

        if (Engine.IsEditorHint())
        {
            ApplyPose(StartState());
        }
    }

    /// <summary>A kinematic body (driven to its node each tick) under <paramref name="parent"/>.</summary>
    Node3D AddBody(Node3D parent, string name)
    {
        Node3D body = ClassDB.Instantiate("GMPBody").As<Node3D>();
        body.Name = name;
        body.Set("body_type", (int)BodyTypeEnum.Kinematic);
        body.Set("tags", (long)SurfaceTags.STRUCTURE);
        parent.AddChild(body);
        return body;
    }

    /// <summary>A box collider with a box mesh drawn on it, under <paramref name="parent"/>.</summary>
    Node3D AddBox(Node3D parent, string name, Vector3 boxSize, Vector3 position, Material material)
    {
        Node3D shape = ClassDB.Instantiate("GMPShape").As<Node3D>();
        shape.Name = name;
        shape.Position = position;
        shape.Set("size", boxSize);
        shape.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = boxSize }, MaterialOverride = material });
        parent.AddChild(shape);
        return shape;
    }

    /// <summary>
    /// One half of the clamshell bucket (side -1: left, +1: right), a kinematic body hinged on the head's underside.
    /// Shut, it fills x 0..side*BucketHalfWidth below its hinge: an outer side plate, half the floor and two end plates.
    /// </summary>
    Node3D AddJaw(Node3D parent, string name, int side, Material material)
    {
        Node3D jaw = AddBody(parent, name);
        jaw.Position = new Vector3(0, -HeadHeight, 0);
        float halfX = side * BucketHalfWidth / 2f;
        AddBox(jaw, "Side", new Vector3(PlateThickness, BucketDepth, 2 * BucketHalfLength),
            new Vector3(side * (BucketHalfWidth - PlateThickness / 2f), -BucketDepth / 2f, 0), material);
        AddBox(jaw, "Floor", new Vector3(BucketHalfWidth, PlateThickness, 2 * BucketHalfLength),
            new Vector3(halfX, -BucketDepth + PlateThickness / 2f, 0), material);
        AddBox(jaw, "Front", new Vector3(BucketHalfWidth, BucketDepth, PlateThickness),
            new Vector3(halfX, -BucketDepth / 2f, -BucketHalfLength + PlateThickness / 2f), material);
        AddBox(jaw, "Back", new Vector3(BucketHalfWidth, BucketDepth, PlateThickness),
            new Vector3(halfX, -BucketDepth / 2f, BucketHalfLength - PlateThickness / 2f), material);
        return jaw;
    }

    // ---- limits and posing ----

    float MinX => RailThickness + BucketHalfWidth;
    float MaxX => Mathf.Max(MinX, size.X - RailThickness - BucketHalfWidth);
    float MinZ => Mathf.Min(MaxZ, -size.Z + BucketHalfLength + 0.3f);
    float MaxZ => -BucketHalfLength - 0.3f;
    float MaxCable => Mathf.Max(MinCable, size.Y - TrolleyDrop - HeadHeight - BucketDepth);

    /// <summary>Where the head starts: over the middle, cable reeled in, bucket shut.</summary>
    GantryCraneState StartState()
    {
        return new GantryCraneState { bridgeZ = -size.Z / 2f, trolleyX = size.X / 2f, cableLength = MinCable, jawOpen = 0 };
    }

    /// <summary>Poses every moving part from <paramref name="s"/>. Kinematic bodies follow their nodes, pushing what they meet.</summary>
    void ApplyPose(GantryCraneState s)
    {
        float cableTop = size.Y - TrolleyDrop;
        bridge.Position = new Vector3(0, 0, s.bridgeZ);
        trolley.Position = new Vector3(s.trolleyX, cableTop, s.bridgeZ);
        head.Position = new Vector3(s.trolleyX, cableTop - s.cableLength, s.bridgeZ);
        float jawAngle = Mathf.DegToRad(jawOpenDegrees) * s.jawOpen;
        leftJaw.Rotation = new Vector3(0, 0, -jawAngle);
        rightJaw.Rotation = new Vector3(0, 0, jawAngle);
        cable.Position = new Vector3(s.trolleyX, cableTop - s.cableLength / 2f, s.bridgeZ);
        cable.Scale = new Vector3(1, Mathf.Max(0.01f, s.cableLength), 1);
    }

    // ---- lifetime ----

    public override void AfterInit()
    {
        // Only the cockpit's cells are the crane's on the build grid.
        Vector3I corner = new Vector3I(Mathf.RoundToInt(cockpit.Position.X), Mathf.RoundToInt(cockpit.Position.Y), Mathf.RoundToInt(cockpit.Position.Z));
        cells = new();
        for (int x = 0; x < cockpitCells.X; x++)
        {
            for (int y = 0; y < cockpitCells.Y; y++)
            {
                for (int z = 0; z < cockpitCells.Z; z++)
                {
                    cells[corner + new Vector3I(x, y, -z)] = CellTags.None;
                }
            }
        }
        deconstructable = false;
        if (string.IsNullOrEmpty(hoverText)) hoverText = "Press F to pilot";
        base.AfterInit();
        // Unlike other structures it sends its state: the head's pose.
        priority = 20;
        if (!hasState)
        {
            state = StartState();
        }
        ApplyPose(state);
    }

    public override void _ExitTree()
    {
        if (Engine.IsEditorHint()) return;
        if (localPilot != null)
        {
            localPilot.piloting = null;
            localPilot = null;
        }
        base._ExitTree();
    }

    // ---- state sync ----

    public override byte[] GenerateStateUpdate()
    {
        return GMPObject.serializer.Serialize(state);
    }

    // Also the spawn-time state from a save, on every peer (the host included), before AfterInit.
    public override void ApplyStateUpdate(byte[] update)
    {
        if (update == null || update.Length == 0) return;
        GantryCraneState s = GMPObject.serializer.Deserialize<GantryCraneState>(update);
        if (GameWorld.stateTick == 0)
        {
            // Spawn-time (a save): start there.
            state = s;
            hasState = true;
            return;
        }
        received.Add((GameWorld.stateTick, s));
    }

    /// <summary>Followers: the state at the host's playback tick, between the two received around it.</summary>
    void PlayBack()
    {
        if (received.Count == 0) return;
        double tick = GameWorld.PlaybackTick(authority);
        // Drop what playback has passed, keeping the one just before it to interpolate from.
        while (received.Count > 1 && received[1].tick <= tick)
        {
            received.RemoveAt(0);
        }
        GantryCraneState from = received[0].state;
        if (received.Count == 1 || tick <= received[0].tick)
        {
            state = from;
            return;
        }
        GantryCraneState to = received[1].state;
        float t = (float)((tick - received[0].tick) / (received[1].tick - received[0].tick));
        state = new GantryCraneState
        {
            bridgeZ = Mathf.Lerp(from.bridgeZ, to.bridgeZ, t),
            trolleyX = Mathf.Lerp(from.trolleyX, to.trolleyX, t),
            cableLength = Mathf.Lerp(from.cableLength, to.cableLength, t),
            jawOpen = Mathf.Lerp(from.jawOpen, to.jawOpen, t),
        };
    }

    // ---- every tick ----

    public override void _PhysicsProcess(double delta)
    {
        if (Engine.IsEditorHint() || id == 0) return;
        float dt = (float)delta;
        if (localPilot != null)
        {
            SendPilotKeys(dt);
        }
        if (authority == Lobby.selfPeerID)
        {
            if (pilotID != 0 && !GameWorld.syncedObjs.ContainsKey(pilotID))
            {
                // The pilot's peer left.
                RPCManager.RPC(id, nameof(_SetPilot), [0ul]);
            }
            Drive(dt);
            Grip(dt);
        }
        else
        {
            PlayBack();
        }
        ApplyPose(state);
    }

    /// <summary>Host: moves the head toward what the pilot holds, within the crane's box.</summary>
    void Drive(float dt)
    {
        float step = moveAcceleration * dt;
        velocity.X = Mathf.MoveToward(velocity.X, pilotMove.X * moveSpeed, step);
        velocity.Z = Mathf.MoveToward(velocity.Z, pilotMove.Y * moveSpeed, step);
        state.trolleyX += velocity.X * dt;
        state.bridgeZ += velocity.Z * dt;
        if (state.trolleyX < MinX || state.trolleyX > MaxX)
        {
            state.trolleyX = Mathf.Clamp(state.trolleyX, MinX, MaxX);
            velocity.X = 0;
        }
        if (state.bridgeZ < MinZ || state.bridgeZ > MaxZ)
        {
            state.bridgeZ = Mathf.Clamp(state.bridgeZ, MinZ, MaxZ);
            velocity.Z = 0;
        }
        // Lifting shortens the cable.
        state.cableLength = Mathf.Clamp(state.cableLength - pilotLift * liftSpeed * dt, MinCable, MaxCable);
        state.jawOpen = Mathf.Clamp(state.jawOpen + pilotJaw * jawSpeed * dt, 0, 1);
    }

    /// <summary>Host: while the bucket is shut, pulls the loose items inside it toward its middle so a grab holds.</summary>
    void Grip(float dt)
    {
        if (state.jawOpen >= gripBelowOpen) return;
        Vector3 middle = gripSensor.GlobalPosition;
        foreach (long found in gripSensor.Call(getOverlappingName).AsInt64Array())
        {
            ulong item = (ulong)found;
            if (ItemWorld.Get(item) == null) continue;
            // Held items sync ahead of everything but the players.
            PhysicsWorld.HoldBody(item);
            // TODO(human): pull the item toward `middle`.
        }
    }

    // ---- piloting ----

    /// <summary>Interact on the cockpit (not the rest of the crane) asks the host to let this player pilot.</summary>
    public override void onInteract(ulong playerID)
    {
        if (!GameWorld.syncedObjs.TryGetValue(playerID, out GMPObject p) || p is not FactoryPlayer player) return;
        Camera3D eye = player.camera;
        RayHit hit = PhysicsWorld.Raycast(eye.GlobalPosition, eye.GlobalPosition - eye.GlobalBasis.Z * player.pickRange);
        if (!hit.hit || PhysicsWorld.BodyNode(hit.id) != cockpit) return;
        RPCManager.RequestFromAuthority(this, nameof(_RequestPilot), [playerID]);
    }

    /// <summary>Pilot's peer: asks the host to let go of the crane.</summary>
    public void Leave()
    {
        RPCManager.RequestFromAuthority(this, nameof(_RequestLeave), []);
    }

    // Runs on the crane's authority: the first to ask gets it.
    [RPC]
    void _RequestPilot(ulong playerID)
    {
        if (authority != Lobby.selfPeerID || pilotID != 0) return;
        if (!GameWorld.syncedObjs.TryGetValue(playerID, out GMPObject p) || p.authority != RPCManager.sender) return;
        RPCManager.RPC(id, nameof(_SetPilot), [playerID]);
    }

    // Runs on the crane's authority.
    [RPC]
    void _RequestLeave()
    {
        if (authority != Lobby.selfPeerID) return;
        if (!GameWorld.syncedObjs.TryGetValue(pilotID, out GMPObject p) || p.authority != RPCManager.sender) return;
        RPCManager.RPC(id, nameof(_SetPilot), [0ul]);
    }

    [RPC(requireAuthority = true)]
    void _SetPilot(ulong playerID)
    {
        pilotID = playerID;
        pilotMove = Vector2I.Zero;
        pilotLift = 0;
        pilotJaw = 0;
        if (localPilot != null)
        {
            localPilot.piloting = null;
            localPilot = null;
        }
        if (GameWorld.syncedObjs.TryGetValue(playerID, out GMPObject p) && p is FactoryPlayer player && player.isLocal)
        {
            localPilot = player;
            player.piloting = this;
            // Nothing in hand while piloting, so the mouse buttons only work the bucket.
            player.inventory.ActiveHotbarSlot = -1;
            player.UpdateEquippedItem();
            UIManager.SetHoverInfo(null, null);
            cameraYaw = player.GlobalRotation.Y;
            sentMove = Vector2I.Zero;
            sentLift = 0;
            sentJaw = 0;
        }
    }

    /// <summary>Pilot's peer: mouse orbits the camera, the wheel zooms, interact leaves. Called by the pilot's FactoryPlayer.</summary>
    public void HandlePilotInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            float sensitivity = FactoryPlayer.MouseSensitivity * GameSettings.mouseSensitivity;
            cameraYaw -= motion.ScreenRelative.X * sensitivity;
            float invert = 1;
            if (GameSettings.invertMouseY) invert = -1;
            cameraPitch = Mathf.Clamp(cameraPitch - motion.ScreenRelative.Y * sensitivity * invert, Mathf.DegToRad(-85f), Mathf.DegToRad(30f));
        }
        if (@event.IsActionPressed(InputActions.scrollUp))
        {
            cameraDistance = Mathf.Max(cameraMinDistance, cameraDistance - 0.5f);
        }
        if (@event.IsActionPressed(InputActions.scrollDown))
        {
            cameraDistance = Mathf.Min(cameraMaxDistance, cameraDistance + 0.5f);
        }
        if (@event.IsActionPressed(InputActions.interact))
        {
            Leave();
        }
    }

    /// <summary>
    /// Pilot's peer: reads the held keys and sends them to the host when they change. WASD steers along the crane's
    /// axis nearest to where the camera faces (forward) and the one across it (right).
    /// </summary>
    void SendPilotKeys(float dt)
    {
        Vector2 input = Vector2.Zero;
        int lift = 0;
        int jaw = 0;
        if (!UIManager.BlocksGameplayInput)
        {
            input = Input.GetVector(InputActions.left, InputActions.right, InputActions.forward, InputActions.backward);
            if (Input.IsActionPressed(InputActions.jump)) lift += 1;
            if (Input.IsActionPressed(InputActions.crouch)) lift -= 1;
            if (Input.IsActionPressed(InputActions.primary)) jaw += 1;
            if (Input.IsActionPressed(InputActions.secondary)) jaw -= 1;
        }
        if (testDriveLeft > 0)
        {
            testDriveLeft -= dt;
            input = new Vector2(testDrive.X, testDrive.Y);
            lift = testLift;
            jaw = testJaw;
        }

        // The camera's flat facing in the crane's frame, snapped to the nearest axis.
        Vector3 facing = GlobalBasis.Inverse() * (Basis.FromEuler(new Vector3(0, cameraYaw, 0)) * Vector3.Forward);
        Vector2I forward;
        if (Mathf.Abs(facing.X) > Mathf.Abs(facing.Z))
        {
            forward = new Vector2I(Mathf.Sign(facing.X), 0);
        }
        else
        {
            forward = new Vector2I(0, Mathf.Sign(facing.Z));
        }
        Vector2I right = new Vector2I(-forward.Y, forward.X);
        Vector2I move = forward * -Mathf.RoundToInt(input.Y) + right * Mathf.RoundToInt(input.X);

        if (move != sentMove || lift != sentLift || jaw != sentJaw)
        {
            sentMove = move;
            sentLift = lift;
            sentJaw = jaw;
            RPCManager.RPCTo(authority, id, nameof(_PilotKeys), [move.X, move.Y, lift, jaw]);
        }
    }

    // Runs on the crane's authority, from the pilot's peer.
    [RPC]
    void _PilotKeys(int moveX, int moveZ, int lift, int jaw)
    {
        if (!GameWorld.syncedObjs.TryGetValue(pilotID, out GMPObject p) || p.authority != RPCManager.sender) return;
        pilotMove = new Vector2I(moveX, moveZ);
        pilotLift = lift;
        pilotJaw = jaw;
    }

    /// <summary>Pilot's peer: the camera orbits the head as it's drawn (interpolated), every frame.</summary>
    public override void _Process(double delta)
    {
        if (localPilot == null) return;
        Vector3 target = head.GetGlobalTransformInterpolated().Origin + Vector3.Down * (HeadHeight + BucketDepth / 2f);
        Basis look = Basis.FromEuler(new Vector3(cameraPitch, cameraYaw, 0));
        localPilot.camera.GlobalTransform = new Transform3D(look, target + look * new Vector3(0, 0, cameraDistance));
    }

    // ---- test driving (console crane_drive, for the test harness, which can't press keys) ----

    /// <summary>Keys held for testDriveLeft more seconds: steering x right, y back (as Input.GetVector gives it), lift, jaw.</summary>
    public Vector2I testDrive;
    public int testLift;
    public int testJaw;
    public float testDriveLeft;

    /// <summary>For the console: the pose shown on this peer and who pilots it.</summary>
    public string Describe()
    {
        return $"crane: id={id} pilot={pilotID} bridgeZ={state.bridgeZ:0.00} trolleyX={state.trolleyX:0.00} cable={state.cableLength:0.00} jaw={state.jawOpen:0.00} head={head.GlobalPosition}";
    }
}
