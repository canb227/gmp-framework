using Godot;
using PolyType;
using System.Collections.Generic;









//i let claude oneshot this entire crane










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
/// trolley on a cable: the head is a dynamic body on a rope joint to the trolley, so it swings when the trolley starts
/// and stops, and knocks into things. Rails, bridge, trolley, head and bucket are plain boxes, made here when it enters the tree (and
/// remade in the editor on every change), as children with no owner so they are never saved into the scene.
/// <para>
/// The cockpit is a GMPBody placed by hand anywhere under the crane (however far from its box: a control room
/// elsewhere in the level works, with Editable Children on the crane's instance), its origin on the left-bottom-back
/// corner of its cells (<see cref="cockpitCells"/> of them). Only those cells are the crane's on the build grid. A
/// pilot counts as looking from the crane wherever their body is, for what the host sends them (<see cref="PilotView"/>). Interacting with the
/// cockpit pilots the crane: the camera orbits the head, WASD moves it along the crane's axes (whichever is nearest to
/// where the camera faces), jump and crouch raise and lower it (sprint makes both faster), primary opens the bucket and secondary shuts it, and
/// interact leaves.
/// </para>
/// <para>
/// The host runs it (it simulates every loose item, so only its bucket really moves anything). The pilot sends the
/// keys it holds to the host whenever they change; the host moves the crane and sends its <see cref="GantryCraneState"/>,
/// which the other peers play back on the host's playback clock, in step with the items. The swinging head and the
/// bucket halves are synced like loose items instead (the physics world's body sync, the host their authority): the
/// halves are dynamic too, on motor-driven hinges with a torque limit, so they squeeze what they shut on and the load
/// weighs on the head. While the bucket is shut the
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

    Color _color = new Color(0.9f, 0.65f, 0.1f);
    /// <summary>The paint on the bridge, trolley, head and every mesh of the cockpit (the rails and bucket are bare steel).</summary>
    [Export]
    public Color color
    {
        get { return _color; }
        set
        {
            _color = value;
            Rebuild();
        }
    }

    /// <summary>The cockpit body, placed by hand anywhere under the crane.</summary>
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
    /// <summary>Moving and lifting go this many times faster while sprint (Shift) is held.</summary>
    [Export] public float fastMultiplier = 2f;
    /// <summary>How far the bucket halves swing open.</summary>
    [Export] public float jawOpenDegrees = 75f;
    /// <summary>The most each bucket half's hinge motor turns with (N m): how hard the bucket squeezes, and how much it can lift open.</summary>
    [Export] public float jawTorque = 4000f;

    [ExportGroup("Swing")]
    /// <summary>
    /// How quickly the head's swinging dies away (1/s): it is pushed toward moving with the trolley, so it settles under
    /// it without dragging behind while the crane travels.
    /// </summary>
    [Export] public float swingDamping = 2f;
    /// <summary>How quickly the head stops spinning and wobbling about the rope (its angular damping, 1/s).</summary>
    [Export] public float spinDamping = 2f;

    [ExportGroup("Grip")]
    /// <summary>The bucket pulls what is in it only while it's shut further than this (jawOpen below it).</summary>
    [Export] public float gripBelowOpen = 0.15f;
    /// <summary>How hard it pulls, (m/s²) per metre an item is from the middle of the bucket.</summary>
    [Export] public float gripStrength = 30f;
    /// <summary>How quickly an item's motion relative to the bucket dies away (1/s), so it settles instead of bouncing.</summary>
    [Export] public float gripDamping = 4f;

    [ExportGroup("Pilot camera")]
    [Export] public float cameraDistance = 5f;
    [Export] public float cameraMinDistance = 2f;
    [Export] public float cameraMaxDistance = 14f;

    // ---- the parts' sizes (m) ----
    const float RailThickness = 0.3f;
    /// <summary>From the rails' tops down to the trolley's underside, where the cable starts.</summary>
    const float TrolleyDrop = 0.6f;
    const float HeadWidth = 1.1f;
    const float HeadHeight = 0.4f;
    const float BucketHalfWidth = 0.8f;
    const float BucketHalfLength = 0.65f;
    const float BucketDepth = 0.9f;
    const float PlateThickness = 0.06f;
    /// <summary>How fast a bucket half's motor turns toward where it should be (1/s of angle off).</summary>
    const float JawMotorGain = 8f;
    const float MinCable = 0.2f;

    /// <summary>Every peer: the pose shown now (on the host, the one being simulated).</summary>
    GantryCraneState state;
    /// <summary>A spawn-time state (from a save) was given, so it doesn't start at <see cref="StartState"/>.</summary>
    bool hasState;
    /// <summary>Host: how fast the trolley (x), cable (y) and bridge (z) are moving.</summary>
    Vector3 velocity;

    /// <summary>The player object piloting it (0: none), on every peer.</summary>
    public ulong pilotID { get; private set; }
    /// <summary>Host: what the pilot holds: move along X and Z (-1, 0, 1), lift (+1 up), jaw (+1 opens), fast (sprint).</summary>
    Vector2I pilotMove;
    int pilotLift;
    int pilotJaw;
    bool pilotFast;
    /// <summary>Host: where the middle of the bucket was last tick, for how fast it moves.</summary>
    Vector3 lastBucketMiddle;

    /// <summary>The local player, while it pilots this crane.</summary>
    FactoryPlayer localPilot;
    /// <summary>Pilot's peer: the keys last sent to the host, so only changes are sent.</summary>
    Vector2I sentMove;
    int sentLift;
    int sentJaw;
    bool sentFast;
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

    /// <summary>The rope from the trolley to the head (a distance joint), and the cable length it was last set to.</summary>
    long rope;
    float ropeLength;
    ulong headID;
    /// <summary>The bucket halves' hinges on the head (revolute joints).</summary>
    long leftHinge;
    long rightHinge;

    static readonly StringName getOverlappingName = "get_overlapping";
    static readonly StringName teleportName = "teleport";

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
        StandardMaterial3D paint = new StandardMaterial3D { AlbedoColor = color, Roughness = 0.6f };
        // The hand-placed cockpit is painted to match, whatever meshes it's made of.
        if (cockpit != null)
        {
            foreach (Node node in cockpit.FindChildren("*", "MeshInstance3D", true, false))
            {
                ((MeshInstance3D)node).MaterialOverride = paint;
            }
        }
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
        // Posed every frame from where the trolley and head are drawn (DrawCable).
        cable.TopLevel = true;
        cable.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        AddChild(cable);
        made.Add(cable);

        // Head: its node at the middle of its top, where the rope holds it. The bucket halves hinge on its underside.
        // Dynamic: it hangs and swings (its node follows its body).
        head = AddBody(this, "Head");
        head.Set("body_type", (int)BodyTypeEnum.Dynamic);
        head.Set("angular_damping", spinDamping);
        AddBox(head, "Block", new Vector3(HeadWidth, HeadHeight, HeadWidth), new Vector3(0, -HeadHeight / 2f, 0), paint);
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
            GantryCraneState start = StartState();
            ApplyPose(start);
            PoseJaws(start.jawOpen);
            head.Position = HangingPoint(start);
            DrawCable();
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
    /// One half of the clamshell bucket (side -1: left, +1: right), a dynamic body hinged on the head's underside about
    /// Z. Shut, it fills x 0..side*BucketHalfWidth below its hinge: an outer side plate, half the floor and two end plates.
    /// </summary>
    Node3D AddJaw(Node3D parent, string name, int side, Material material)
    {
        Node3D jaw = AddBody(parent, name);
        jaw.Set("body_type", (int)BodyTypeEnum.Dynamic);
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

    /// <summary>
    /// Poses the bridge and trolley from <paramref name="s"/>. Kinematic bodies follow their nodes, pushing what they meet.
    /// The head and bucket halves aren't posed: they hang on their joints (or, on a follower, are played back by the body sync).
    /// </summary>
    void ApplyPose(GantryCraneState s)
    {
        bridge.Position = new Vector3(0, 0, s.bridgeZ);
        trolley.Position = new Vector3(s.trolleyX, size.Y - TrolleyDrop, s.bridgeZ);
    }

    /// <summary>Turns the bucket halves' nodes open by <paramref name="jawOpen"/> (0 shut, 1 open): where they start.</summary>
    void PoseJaws(float jawOpen)
    {
        float jawAngle = Mathf.DegToRad(jawOpenDegrees) * jawOpen;
        leftJaw.Rotation = new Vector3(0, 0, -jawAngle);
        rightJaw.Rotation = new Vector3(0, 0, jawAngle);
    }

    /// <summary>Where the head hangs at rest, straight below the trolley at the cable's length (crane frame).</summary>
    Vector3 HangingPoint(GantryCraneState s)
    {
        return new Vector3(s.trolleyX, size.Y - TrolleyDrop - s.cableLength, s.bridgeZ);
    }

    /// <summary>Stretches the cable from the trolley's underside to the top of the head, as both are drawn.</summary>
    void DrawCable()
    {
        Vector3 top = trolley.GetGlobalTransformInterpolated().Origin;
        Vector3 bottom = head.GetGlobalTransformInterpolated().Origin;
        Vector3 along = bottom - top;
        float length = along.Length();
        if (length < 0.01f)
        {
            cable.Visible = false;
            return;
        }
        cable.Visible = true;
        Basis turn = new Basis(new Quaternion(Vector3.Up, along / length));
        cable.GlobalTransform = new Transform3D(turn * Basis.FromScale(new Vector3(1, length, 1)), (top + bottom) / 2f);
    }

    // ---- lifetime ----

    public override void AfterInit()
    {
        // Only the cockpit's cells are the crane's on the build grid, as offsets from the crane's anchor (in its frame).
        Vector3 cockpitCorner = ToLocal(cockpit.GlobalPosition);
        Vector3I corner = new Vector3I(Mathf.RoundToInt(cockpitCorner.X), Mathf.RoundToInt(cockpitCorner.Y), Mathf.RoundToInt(cockpitCorner.Z));
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
        PoseJaws(state.jawOpen);

        // Hang the head below the trolley on a rope, and the bucket halves on hinges under it: simulated by the
        // authority and followed everywhere else. The joints are made on every peer; they only act where the bodies are
        // simulated.
        head.Call(teleportName, new Transform3D(GlobalBasis, GlobalTransform * HangingPoint(state)));
        // The moving parts' bodies were made where their nodes first stood: put them where they start at once, or the
        // first step sweeps them there at hundreds of m/s, and the rope flings the head along with the trolley.
        bridge.Call(teleportName, bridge.GlobalTransform);
        trolley.Call(teleportName, trolley.GlobalTransform);
        leftJaw.Call(teleportName, leftJaw.GlobalTransform);
        rightJaw.Call(teleportName, rightJaw.GlobalTransform);
        headID = PhysicsWorld.BodyIdOf(head);
        ulong leftJawID = PhysicsWorld.BodyIdOf(leftJaw);
        ulong rightJawID = PhysicsWorld.BodyIdOf(rightJaw);
        PhysicsWorld.SetBodyAuthority(headID, authority);
        PhysicsWorld.SetBodyAuthority(leftJawID, authority);
        PhysicsWorld.SetBodyAuthority(rightJawID, authority);
        ropeLength = state.cableLength;
        rope = PhysicsWorld.CreateDistanceJoint(PhysicsWorld.BodyIdOf(trolley), Vector3.Zero, headID, Vector3.Zero, 0, ropeLength);
        // A hinge's angle is its half's turn about Z relative to the head, 0 shut: the left half opens to negative angles.
        Transform3D hinge = new Transform3D(Basis.Identity, new Vector3(0, -HeadHeight, 0));
        float open = Mathf.DegToRad(jawOpenDegrees);
        leftHinge = PhysicsWorld.CreateRevoluteJoint(headID, hinge, leftJawID, Transform3D.Identity, -open, 0);
        rightHinge = PhysicsWorld.CreateRevoluteJoint(headID, hinge, rightJawID, Transform3D.Identity, 0, open);
        // Shut, the two halves meet in the middle: they mustn't fight each other there.
        PhysicsWorld.CreateFilterJoint(leftJawID, rightJawID);
        lastBucketMiddle = gripSensor.GlobalPosition;
    }

    public override void _ExitTree()
    {
        if (Engine.IsEditorHint()) return;
        ClearPilot();
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
            if (state.cableLength != ropeLength)
            {
                ropeLength = state.cableLength;
                PhysicsWorld.SetDistanceJointRange(rope, 0, ropeLength);
            }
            DriveJaws();
            DampSwing();
        }
        else
        {
            PlayBack();
        }
        ApplyPose(state);
        // After posing, so the grip pulls toward where the bucket is this tick.
        if (authority == Lobby.selfPeerID)
        {
            Grip(dt);
        }
    }

    /// <summary>Host: moves the head toward what the pilot holds, within the crane's box.</summary>
    void Drive(float dt)
    {
        float speed = moveSpeed;
        float lift = liftSpeed;
        if (pilotFast)
        {
            speed *= fastMultiplier;
            lift *= fastMultiplier;
        }
        float step = moveAcceleration * dt;
        velocity.X = Mathf.MoveToward(velocity.X, pilotMove.X * speed, step);
        velocity.Z = Mathf.MoveToward(velocity.Z, pilotMove.Y * speed, step);
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
        state.cableLength = Mathf.Clamp(state.cableLength - pilotLift * lift * dt, MinCable, MaxCable);
        state.jawOpen = Mathf.Clamp(state.jawOpen + pilotJaw * jawSpeed * dt, 0, 1);
    }

    /// <summary>
    /// Host: turns each bucket half's hinge motor toward the opening the pilot has set, at most <see cref="jawTorque"/>,
    /// so a half that shuts on something stops there and squeezes it.
    /// </summary>
    void DriveJaws()
    {
        float target = Mathf.DegToRad(jawOpenDegrees) * state.jawOpen;
        float maxSpeed = 2 * jawSpeed * Mathf.DegToRad(jawOpenDegrees);
        float leftSpeed = Mathf.Clamp((-target - PhysicsWorld.RevoluteJointAngle(leftHinge)) * JawMotorGain, -maxSpeed, maxSpeed);
        float rightSpeed = Mathf.Clamp((target - PhysicsWorld.RevoluteJointAngle(rightHinge)) * JawMotorGain, -maxSpeed, maxSpeed);
        PhysicsWorld.SetRevoluteJointMotor(leftHinge, leftSpeed, jawTorque);
        PhysicsWorld.SetRevoluteJointMotor(rightHinge, rightSpeed, jawTorque);
    }

    /// <summary>
    /// Host: pushes the head toward moving sideways with the trolley, so a swing dies away under it (anti-sway) without
    /// the head lagging behind while the crane travels, as plain damping would make it. Sized for the whole hanging
    /// head with its bucket halves; the load is left to swing it.
    /// </summary>
    void DampSwing()
    {
        Vector3 trolleyVelocity = GlobalBasis * new Vector3(velocity.X, 0, velocity.Z);
        Vector3 relative = PhysicsWorld.BodyLinearVelocity(headID) - trolleyVelocity;
        relative.Y = 0;
        float mass = PhysicsWorld.BodyMass(headID) + PhysicsWorld.BodyMass(PhysicsWorld.BodyIdOf(leftJaw)) + PhysicsWorld.BodyMass(PhysicsWorld.BodyIdOf(rightJaw));
        PhysicsWorld.ApplyBodyForce(headID, -relative * swingDamping * mass);
    }

    /// <summary>
    /// Host: while the bucket is shut, pulls the loose items inside it toward its middle and damps how they move
    /// relative to it, so a grab settles and holds. Gravity is left in: the bucket's floor does the lifting, and the pull
    /// only keeps the load from rattling out, so an overfilled or swung bucket can still spill.
    /// </summary>
    void Grip(float dt)
    {
        Vector3 middle = gripSensor.GlobalPosition;
        Vector3 bucketVelocity = (middle - lastBucketMiddle) / dt;
        lastBucketMiddle = middle;
        if (state.jawOpen >= gripBelowOpen) return;
        foreach (long found in gripSensor.Call(getOverlappingName).AsInt64Array())
        {
            ulong item = (ulong)found;
            if (ItemWorld.Get(item) == null) continue;
            // Held items sync ahead of everything but the players.
            PhysicsWorld.HoldBody(item);
            Vector3 offset = middle - PhysicsWorld.BodyPosition(item);
            Vector3 relativeVelocity = PhysicsWorld.BodyLinearVelocity(item) - bucketVelocity;
            Vector3 acceleration = offset * gripStrength - relativeVelocity * gripDamping;
            PhysicsWorld.ApplyBodyForce(item, acceleration * PhysicsWorld.BodyMass(item));
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
        // Every peer knows who pilots what (for where they count as looking from); only the pilot's own peer steers.
        ClearPilot();
        pilotID = playerID;
        pilotMove = Vector2I.Zero;
        pilotLift = 0;
        pilotJaw = 0;
        pilotFast = false;
        if (!GameWorld.syncedObjs.TryGetValue(playerID, out GMPObject p) || p is not FactoryPlayer player)
        {
            return;
        }
        player.piloting = this;
        if (player.isLocal)
        {
            localPilot = player;
            // Nothing in hand while piloting, so the mouse buttons only work the bucket.
            player.inventory.ActiveHotbarSlot = -1;
            player.UpdateEquippedItem();
            UIManager.SetHoverInfo(null, null);
            cameraYaw = player.GlobalRotation.Y;
            sentMove = Vector2I.Zero;
            sentLift = 0;
            sentJaw = 0;
            sentFast = false;
        }
    }

    /// <summary>The player piloting it (if it's still here) stops, on this peer.</summary>
    void ClearPilot()
    {
        if (GameWorld.syncedObjs.TryGetValue(pilotID, out GMPObject p) && p is FactoryPlayer player && player.piloting == this)
        {
            player.piloting = null;
        }
        localPilot = null;
    }

    /// <summary>
    /// Where the pilot counts as looking from, on peers that don't have their camera (for which loose items are sent
    /// to them often, and which hits): above and behind the bucket, looking at it.
    /// </summary>
    public Transform3D PilotView()
    {
        Vector3 target = head.GlobalPosition + Vector3.Down * (HeadHeight + BucketDepth / 2f);
        Vector3 eye = target + GlobalBasis * new Vector3(0, cameraDistance / 2f, cameraDistance);
        return new Transform3D(Basis.LookingAt(target - eye), eye);
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
        bool fast = false;
        if (!UIManager.BlocksGameplayInput)
        {
            fast = Input.IsActionPressed(InputActions.sprint);
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
            fast = testFast;
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

        if (move != sentMove || lift != sentLift || jaw != sentJaw || fast != sentFast)
        {
            sentMove = move;
            sentLift = lift;
            sentJaw = jaw;
            sentFast = fast;
            RPCManager.RPCTo(authority, id, nameof(_PilotKeys), [move.X, move.Y, lift, jaw, fast]);
        }
    }

    // Runs on the crane's authority, from the pilot's peer.
    [RPC]
    void _PilotKeys(int moveX, int moveZ, int lift, int jaw, bool fast)
    {
        if (!GameWorld.syncedObjs.TryGetValue(pilotID, out GMPObject p) || p.authority != RPCManager.sender) return;
        pilotMove = new Vector2I(moveX, moveZ);
        pilotLift = lift;
        pilotJaw = jaw;
        pilotFast = fast;
    }

    /// <summary>Every frame: the cable, and on the pilot's peer the camera, orbiting the head as it's drawn (interpolated).</summary>
    public override void _Process(double delta)
    {
        if (Engine.IsEditorHint() || id == 0) return;
        DrawCable();
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
    public bool testFast;
    public float testDriveLeft;

    /// <summary>For the console: the pose shown on this peer, who pilots it and how many loose items are in the bucket.</summary>
    public string Describe()
    {
        int items = 0;
        foreach (long found in gripSensor.Call(getOverlappingName).AsInt64Array())
        {
            if (ItemWorld.Get((ulong)found) != null) items++;
        }
        // How far the rope leans from straight down, and how long it is now (shorter than the cable when slack).
        Vector3 rope = head.GlobalPosition - trolley.GlobalPosition;
        float swing = Mathf.RadToDeg(rope.AngleTo(Vector3.Down));
        // How far each bucket half stands open from the head, as this peer shows it (degrees).
        float left = Mathf.RadToDeg(leftJaw.GlobalBasis.Y.AngleTo(head.GlobalBasis.Y));
        float right = Mathf.RadToDeg(rightJaw.GlobalBasis.Y.AngleTo(head.GlobalBasis.Y));
        return $"crane: id={id} pilot={pilotID} bridgeZ={state.bridgeZ:0.00} trolleyX={state.trolleyX:0.00} cable={state.cableLength:0.00} jaw={state.jawOpen:0.00} jaws={left:0}/{right:0} head={head.GlobalPosition} swing={swing:0.0} rope={rope.Length():0.00} items={items}";
    }
}
