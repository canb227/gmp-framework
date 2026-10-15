using Godot;
using Nerdbank.MessagePack;
using PolyType;
using System.Collections.Generic;

/// <summary>What a magnet rod is doing.</summary>
public enum MagnetMode : byte { Off, Single, Area }

/// <summary>What the other peers' copies of a magnet rod need each tick (<see cref="MagnetRod.GenerateSyncState"/>).</summary>
/// <remarks>Sent as an array of values; see the note on <see cref="TransformSyncState"/> about [Key] numbers.</remarks>
[GenerateShape]
public partial record struct MagnetSync
{
    [Key(0)] public MagnetMode mode;
    /// <summary>Where the rod pulls to, and how fast that point moves.</summary>
    [Key(1)] public Vector3 target;
    [Key(2)] public Vector3 targetVelocity;
    /// <summary>The item a single grab holds; 0 in area mode, where every copy catches the items around the target itself.</summary>
    [Key(3)] public ulong itemId;
}

/// <summary>
/// The tool for moving things by hand. Hold left click on a loose item to lift it (<see cref="MagnetMode.Single"/>),
/// or hold right click to pull the nearest loose items into a floating ball (<see cref="MagnetMode.Area"/>). The
/// scroll wheel moves the hold point nearer or further.
/// <para>
/// Multiplayer: the rod's authority (the peer driving it) sends the mode, the hold point and (single) the item; it rides
/// along with its player's state. Every peer's copy of the rod holds the same items and pulls the ones its own peer
/// simulates, so pulls from several rods on one item add up: two players can lift what one can't. Items know nothing
/// of this; a held item is an item like any other.
/// </para>
/// <para>
/// Authority: the host simulates every item, held or not, so only its copy of each rod does any pulling; a client's own
/// held items trail its rod by about a round trip. To keep that small, a client shows what its rod holds at the host's
/// newest state rather than the usual interpolation delay behind (<see cref="GMPOBox3DBody.ShowNewest"/>), and the hold
/// point marker moves with the camera every frame.
/// </para>
/// <para>
/// The pull works in accelerations, then scales by the item's mass, so anything can be lifted: a position loop picks a
/// velocity that closes the gap (plus the hold point's own velocity, so held items keep up while you move), a velocity
/// loop turns that into an acceleration (never correcting more than a fraction of the difference per tick, which keeps
/// light items from oscillating), and gravity is cancelled in full. Heavy items get a lower pull speed and acceleration
/// (<see cref="Lightness"/>), so they feel slow and swing wide but always move a little. Spin is damped while held. In
/// area mode every item is pulled to the same point, and their collisions pack them into a ball.
/// </para>
/// </summary>
public partial class MagnetRod : HeldItem
{
    [ExportGroup("Hold distance")]
    [Export] public float holdDistance = 3f;
    [Export] public float minHoldDistance = 1f;
    [Export] public float maxHoldDistance = 8f;
    [Export] public float scrollStep = 0.5f;

    [ExportGroup("Pull")]
    /// <summary>How fast the gap to the hold point closes (1/s).</summary>
    [Export] public float positionGain = 8f;
    /// <summary>How fast an item's velocity follows the wanted velocity (1/s).</summary>
    [Export] public float velocityGain = 20f;
    /// <summary>Fastest an item is pulled toward the hold point (m/s), on top of following it.</summary>
    [Export] public float maxPullSpeed = 12f;
    /// <summary>
    /// Smoothing time (s) for the hold point's velocity. Mouse look and movement move the point between physics
    /// ticks, in uneven steps; unsmoothed, those steps would jolt held items.
    /// </summary>
    [Export] public float followSmoothing = 0.06f;
    /// <summary>
    /// Hardest a light item is accelerated (m/s²), apart from holding it up. Heavier items get less of this and a lower
    /// <see cref="maxPullSpeed"/>, by their <see cref="Lightness"/>.
    /// </summary>
    [Export] public float maxAcceleration = 150f;
    /// <summary>An item this heavy (kg) moves at half the speed and acceleration of a light one; twice as heavy, a third.</summary>
    [Export] public float heavyMass = 50f;
    /// <summary>However heavy an item is, it still gets at least this fraction of the speed and acceleration.</summary>
    [Export] public float minLightness = 0.05f;
    /// <summary>How fast a held item's spin is damped (1/s).</summary>
    [Export] public float spinDamping = 6f;

    [ExportGroup("Single")]
    /// <summary>A single grab lets go if the item stays further than this from the hold point...</summary>
    [Export] public float breakDistance = 2.5f;
    /// <summary>...for this long (s), e.g. when it's pinned behind a wall.</summary>
    [Export] public float breakTime = 0.5f;

    [ExportGroup("Area")]
    /// <summary>Items within this distance of the hold point are caught.</summary>
    [Export] public float captureRadius = 5f;
    [Export] public int maxItems = 30;
    /// <summary>Seconds between searches for more items.</summary>
    [Export] public double rescanInterval = 0.25;

    /// <summary>Shown only while the rod is pulling (the coil's glow); hidden otherwise.</summary>
    [Export] public Node3D activeIndicator;

    // On every copy: set by input on the holder's peer, by its synced state on the others.
    MagnetMode mode;
    Vector3 target;
    Vector3 targetVelocity;
    /// <summary>The items held; once caught, an item stays until the rod lets go.</summary>
    readonly List<PhysicalFactoryItem> caught = new();
    /// <summary>Area mode: seconds until the next search for items to catch (each copy searches for itself).</summary>
    double untilRescan;

    // The holder's peer only.
    Vector3 lastTarget;
    float strainedFor;
    MeshInstance3D ballMarker;

    static readonly Vector3 gravity = ProjectSettings.GetSetting("physics/3d/default_gravity_vector").AsVector3()
        * ProjectSettings.GetSetting("physics/3d/default_gravity").AsSingle();

    public override void Equip(ItemInfo item, FactoryPlayer player)
    {
        base.Equip(item, player);
        if (!isLocal) return;
        ballMarker = GetNode<MeshInstance3D>("BallMarker");
        ballMarker.TopLevel = true;
        ballMarker.Visible = false;
    }

    // ---- input (holder's peer) ----------------------------------------------------

    public override bool HandleInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.primary))
        {
            if (mode == MagnetMode.Off && player.pickTarget is PhysicalFactoryItem item && item.canBeGrabbed)
            {
                // Hold it where it is (distance-wise) rather than yanking it to a fixed point.
                Camera3D camera = player.camera;
                holdDistance = Mathf.Clamp((item.GlobalPosition - camera.GlobalPosition).Dot(-camera.GlobalTransform.Basis.Z), minHoldDistance, maxHoldDistance);
                StartHolding(MagnetMode.Single);
                caught.Add(item);
            }
            return true;
        }
        if (@event.IsActionPressed(InputActions.secondary))
        {
            if (mode == MagnetMode.Off)
            {
                StartHolding(MagnetMode.Area);
            }
            return true;
        }
        // Letting go is noticed in _PhysicsProcess, which also covers losing focus mid-hold.
        return @event.IsActionReleased(InputActions.primary) || @event.IsActionReleased(InputActions.secondary);
    }

    public override bool HandleScroll(int step)
    {
        if (mode == MagnetMode.Off) return false;
        holdDistance = Mathf.Clamp(holdDistance - step * scrollStep, minHoldDistance, maxHoldDistance);
        return true;
    }

    void StartHolding(MagnetMode newMode)
    {
        mode = newMode;
        target = HoldPoint();
        lastTarget = target;
        targetVelocity = Vector3.Zero;
        strainedFor = 0;
        untilRescan = 0;
    }

    Vector3 HoldPoint()
    {
        Camera3D camera = player.camera;
        return camera.GlobalPosition - camera.GlobalTransform.Basis.Z * holdDistance;
    }

    // ---- sync ---------------------------------------------------------------------

    public override byte[] GenerateSyncState()
    {
        MagnetSync sync = new() { mode = mode, target = target, targetVelocity = targetVelocity };
        if (mode == MagnetMode.Single && caught.Count > 0)
        {
            sync.itemId = caught[0].id;
        }
        return GMPObject.serializer.Serialize(sync);
    }

    public override void ApplySyncState(byte[] state)
    {
        if (state == null || state.Length == 0) return;
        MagnetSync sync = GMPObject.serializer.Deserialize<MagnetSync>(state);
        if (sync.mode != mode)
        {
            caught.Clear();
            mode = sync.mode;
            // Catch at once, as the holder's copy does (StartHolding): a quick tap may last only a few ticks here.
            untilRescan = 0;
        }
        target = sync.target;
        targetVelocity = sync.targetVelocity;
        if (mode == MagnetMode.Single)
        {
            PhysicalFactoryItem item = null;
            if (GameWorld.syncedObjs.TryGetValue(sync.itemId, out GMPObject gmpo))
            {
                item = gmpo as PhysicalFactoryItem;
            }
            if (caught.Count != 1 || caught[0] != item)
            {
                caught.Clear();
                if (item != null) caught.Add(item);
            }
        }
    }

    // ---- every tick, every copy ---------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        caught.RemoveAll(IsGone);
        if (isLocal)
        {
            UpdateHolder(delta);
        }
        if (activeIndicator != null) activeIndicator.Visible = mode != MagnetMode.Off;
        if (mode == MagnetMode.Off)
        {
            caught.Clear();
            return;
        }
        if (mode == MagnetMode.Area)
        {
            untilRescan -= delta;
            if (untilRescan <= 0)
            {
                untilRescan = rescanInterval;
                CatchNearbyItems();
            }
        }
        // Held items sync ahead of everything but the players (counts on the item's authority, the host).
        ulong heldUntil = Engine.GetPhysicsFrames() + PhysicalFactoryItem.HeldLingerFrames;
        foreach (PhysicalFactoryItem item in caught)
        {
            item.heldUntilFrame = heldUntil;
            if (isLocal)
            {
                // Show what this player holds as close to now as the host's states allow (nothing on the host).
                item.ShowNewest();
            }
            Pull(item, (float)delta);
        }
    }

    /// <summary>
    /// Holder's peer: the hold point marker, every frame rather than every physics tick, so it moves with the camera at
    /// once even though held items (simulated by the host) trail behind it.
    /// </summary>
    public override void _Process(double delta)
    {
        if (!isLocal) return;
        ballMarker.Visible = mode != MagnetMode.Off;
        if (mode == MagnetMode.Off) return;
        ballMarker.GlobalPosition = HoldPoint();
    }

    /// <summary>Holder's peer: lets go when the button is up, moves the hold point, and breaks a single grab that's stuck.</summary>
    void UpdateHolder(double delta)
    {
        StringName button = InputActions.secondary;
        if (mode == MagnetMode.Single) button = InputActions.primary;
        if (mode != MagnetMode.Off && !Input.IsActionPressed(button))
        {
            mode = MagnetMode.Off;
        }
        if (mode == MagnetMode.Single && caught.Count == 0)
        {
            mode = MagnetMode.Off; // the item is gone (picked up, eaten by a machine...)
        }
        if (mode == MagnetMode.Off) return;

        float dt = (float)delta;
        Vector3 point = HoldPoint();
        Vector3 rawVelocity = (point - lastTarget) / dt;
        lastTarget = point;
        targetVelocity = targetVelocity.Lerp(rawVelocity, 1f - Mathf.Exp(-dt / followSmoothing));
        target = point;

        if (mode == MagnetMode.Single)
        {
            strainedFor = caught[0].GlobalPosition.DistanceTo(target) > breakDistance ? strainedFor + dt : 0;
            // Heavy items trail far behind a quick turn: give them longer to catch up before letting go.
            if (strainedFor > breakTime / Lightness(caught[0].GetMass()))
            {
                mode = MagnetMode.Off;
            }
        }
    }

    /// <summary>Tops the catch up to <see cref="maxItems"/> with the nearest loose items around the hold point.</summary>
    void CatchNearbyItems()
    {
        if (caught.Count >= maxItems) return;
        List<PhysicalFactoryItem> inReach = new();
        foreach (Node n in GameWorld.OverlapSphere(target, captureRadius, (int)~GameWorld.QueryHiddenLayer))
        {
            if (n is PhysicalFactoryItem item && item.id != 0 && item.canBeGrabbed && item.authoredBodyType == BodyTypeEnum.Dynamic && !caught.Contains(item))
            {
                inReach.Add(item);
            }
        }
        inReach.Sort((a, b) => a.GlobalPosition.DistanceSquaredTo(target).CompareTo(b.GlobalPosition.DistanceSquaredTo(target)));
        foreach (PhysicalFactoryItem item in inReach)
        {
            if (caught.Count >= maxItems) return;
            caught.Add(item);
        }
    }

    static bool IsGone(PhysicalFactoryItem item)
    {
        return !IsInstanceValid(item) || item.IsQueuedForDeletion();
    }

    /// <summary>Drives <paramref name="item"/> toward the hold point, if this peer simulates it (otherwise the host's copy of this rod does).</summary>
    void Pull(PhysicalFactoryItem item, float dt)
    {
        if (item.authority != Lobby.selfPeerID) return;
        // A resting item sends about once a second, and the sleep poll can take a while to notice it woke: send it at
        // full rate from the first pull, or the other peers see the grab only after a long delay.
        if (item.asleep) item.OnWoke();

        float mass = item.GetMass();
        float lightness = Lightness(mass);

        float pullSpeed = maxPullSpeed * lightness;
        Vector3 pull = (target - item.GlobalPosition) * positionGain;
        if (pull.Length() > pullSpeed) pull = pull.Normalized() * pullSpeed;
        Vector3 wantedVelocity = targetVelocity + pull;

        // Correct at most 90% of the velocity difference per tick, whatever the gain: this keeps the loop stable.
        float response = Mathf.Min(velocityGain, 0.9f / dt);
        Vector3 accel = (wantedVelocity - item.GetLinearVelocity()) * response;
        float accelLimit = maxAcceleration * lightness;
        if (accel.Length() > accelLimit) accel = accel.Normalized() * accelLimit;
        // Gravity is always cancelled in full, so nothing is too heavy to lift. With several rods on one item each
        // cancels all of it; the velocity loop takes up the extra lift.
        item.ApplyCentralForce((accel - gravity) * mass);

        // Remove a fraction of its spin (an angular impulse of I * delta-omega).
        Vector3 spin = item.GetAngularVelocity();
        if (spin.LengthSquared() > 1e-6f)
        {
            item.ApplyAngularImpulse(item.GetInertiaTensor() * (-spin * Mathf.Min(1f, spinDamping * dt)));
        }
    }

    /// <summary>1 for a light item, falling toward <see cref="minLightness"/> as it gets heavier (0.5 at <see cref="heavyMass"/>).</summary>
    float Lightness(float mass)
    {
        return Mathf.Max(minLightness, 1f / (1f + mass / heavyMass));
    }
}
