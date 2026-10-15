using Godot;
using System;
using System.Collections.Generic;

public enum BodyTypeEnum
{
    Static = 0,
    Kinematic = 1,
    Dynamic = 2
}

/// <summary>Box3DBody <c>shape_type</c>: the body's own collider (child Box3DCollisionShapes add more).</summary>
public enum ShapeTypeEnum
{
    Box = 0,
    Sphere = 1,
    Capsule = 2,
    Cylinder = 3,
    Cone = 4,
    Hull = 5,
    Mesh = 6,
    FitMesh = 7,
    HeightField = 8
}


/// <summary>
/// GMPObject base for Box3D physics bodies. Box3D is a GDExtension and isn't exposed to C#, so a script
/// deriving from this class is attached to a node of type <c>Box3DBody</c> in the editor (C# sees it as a
/// Node3D). The Box3DBody properties are wrapped below; its methods are extension methods in
/// <see cref="GMPOBox3DBodyApi"/>, kept off this class so its engine callbacks stay cheap.
/// </summary>
public partial class GMPOBox3DBody : Node3D, GMPObject
{
    // Godot only reads [Export] on the node class, so each GMPO base repeats this block.
    [ExportGroup("Configuration")]
    [Export]
    public int priority { get; set; }
    [Export]
    public bool pauseable { get; set; } = true;

    [ExportGroup("Hover")]
    /// <summary>Name shown under the crosshair while a player looks at this body; empty shows none.</summary>
    [Export]
    public string hoverName { get; set; }
    /// <summary>Line shown below <see cref="hoverName"/>, e.g. "Press F to pick up!"; empty shows none.</summary>
    [Export]
    public string hoverText { get; set; }

    [ExportGroup("READONLY")]
    [Export]
    public ulong id { get; set; }
    [Export]
    public ulong authority { get; set; }
    [Export]
    public ulong owner { get; set; }
    [Export]
    public int priorityAccumulator { get; set; }
    public byte[] desiredState { get; set; }

    /// <summary>The body type set in the scene; restored whenever this peer becomes the authority.</summary>
    public BodyTypeEnum authoredBodyType { get; private set; }

    public virtual void AfterInit()
    {
        authoredBodyType = this.GetBodyType();
        // A spawn-time state (a restored save, a late join) puts the body exactly where it was, on every peer.
        if (desiredState != null && desiredState.Length > 0)
        {
            BodySyncState s = BodySyncState.Unpack(desiredState);
            this.Teleport(s.pos, s.rot);
        }
        if (authority != Lobby.selfPeerID)
        {
            this.SetBodyType(BodyTypeEnum.Kinematic);
        }
        RefreshPhysicsProcess();
    }

    /// <summary>
    /// The authority simulates with the authored body type; everyone else follows as Kinematic. A new authority carries
    /// on at the velocity it was following the body at. A new follower holds still until the new authority's states
    /// arrive, then eases onto them.
    /// </summary>
    public virtual void OnAuthorityChanged()
    {
        desiredState = null;
        // Their ticks were counted by the old authority's clock.
        snapshots?.Clear();
        if (authority == Lobby.selfPeerID)
        {
            this.SetBodyType(authoredBodyType);
            if (authoredBodyType == BodyTypeEnum.Dynamic)
            {
                // A follower is only ever placed, so the body has no velocity of its own: give it the followed one.
                this.SetLinearVelocity(followVelocity);
                this.SetAngularVelocity(followAngularVelocity);
            }
        }
        else
        {
            this.SetBodyType(BodyTypeEnum.Kinematic);
            // A Kinematic body moves by its own velocity, on top of the follower placing it: clear it.
            this.SetLinearVelocity(Vector3.Zero);
            this.SetAngularVelocity(Vector3.Zero);
        }
        following = false;
        RefreshPhysicsProcess();
    }

    public virtual void OnDespawned(DespawnReason reason)
    {
    }

    // ---- physics processing ----
    // Godot calling into C# costs a few microseconds per node per tick, which adds up across thousands of items
    // (about 7 of a 7.4 ms tick at 2000 resting items, measured). So the base class only keeps physics processing on
    // while a non-authority copy is following. Subclasses that override _PhysicsProcess keep it on all the time.

    // ---- following ----
    // A non-authority copy plays the authority's states back from a short buffer instead of chasing the newest one:
    // it shows the authority's tick GameWorld.PlaybackTick, about GameWorld.InterpolationDelay ticks behind the newest
    // state heard, interpolating between the states either side of it. That smooths over packets arriving unevenly.
    // Past the newest state (packets late) it carries that one on at its velocity, briefly.

    /// <summary>One received state, by the authority's physics tick.</summary>
    struct Snapshot
    {
        public ulong tick;
        public Vector3 pos;
        public Quaternion rot;
        public Vector3 vel;
        /// <summary>World space (rad/s about each axis).</summary>
        public Vector3 angVel;
    }

    /// <summary>Received states, oldest first: the newest one at or before playback, then the ones after it.</summary>
    List<Snapshot> snapshots;

    /// <summary>States kept at most (half a second at 30 Hz).</summary>
    const int MaxSnapshots = 16;

    /// <summary>True while this non-authority copy is being placed from <see cref="snapshots"/>.</summary>
    bool following;

    /// <summary>The velocities at the last playback, for carrying on with should this peer become the authority.</summary>
    Vector3 followVelocity;
    Vector3 followAngularVelocity;

    /// <summary>
    /// How long a follower keeps carrying the newest state on at its velocity once playback has passed it. Long enough
    /// to cover late packets, short enough that a body whose updates stop doesn't drift off.
    /// </summary>
    const float MaxExtrapolation = 0.25f;

    /// <summary>
    /// Where the body is shown relative to the played-back pose: taken when following starts, from wherever the body
    /// was (e.g. simulated here as the authority before a hand-off), then faded out, so the body slides onto the
    /// authority's pose instead of jumping.
    /// </summary>
    Vector3 errorPos;
    Quaternion errorRot = Quaternion.Identity;
    /// <summary>Take <see cref="errorPos"/>/<see cref="errorRot"/> from the body's pose at the next playback.</summary>
    bool blendIn;

    /// <summary>Time constant (s) of the fade: most of the gap is gone in about three times this.</summary>
    const float ErrorBlendTime = 0.1f;

    /// <summary>
    /// Within these the fade counts as done: position in metres, rotation as 1 - |w| of the quaternion between the two
    /// poses (1e-6 is about 0.16°). Not an angle: acos near 1 is too noisy in float to compare against a small threshold.
    /// </summary>
    const float ArrivedDistance = 0.0005f, ArrivedRotation = 1e-6f;

    /// <summary>
    /// Ticks this follower is shown ahead of its authority's playback clock: 0, or up to
    /// <see cref="GameWorld.InterpolationDelay"/> (the newest state heard) while <see cref="ShowNewest"/> is called.
    /// Eased, so playback never jumps.
    /// </summary>
    double playbackLead;
    /// <summary>The physics frame <see cref="ShowNewest"/> was last called on.</summary>
    ulong showNewestFrame;
    /// <summary>
    /// How fast <see cref="playbackLead"/> eases, in ticks per second: the whole delay in about half a second, during
    /// which the body plays back a little fast or slow.
    /// </summary>
    static readonly double PlaybackLeadRate = GameWorld.InterpolationDelay * 2;

    static readonly System.Collections.Generic.Dictionary<Type, bool> customPhysicsProcess = new();

    /// <summary>Whether this node's class (or one between it and this base) overrides <c>_PhysicsProcess</c>.</summary>
    bool hasCustomPhysicsProcess
    {
        get
        {
            Type type = GetType();
            if (!customPhysicsProcess.TryGetValue(type, out bool custom))
            {
                var method = type.GetMethod(nameof(_PhysicsProcess), System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, [typeof(double)], null);
                custom = method.DeclaringType != typeof(GMPOBox3DBody);
                customPhysicsProcess[type] = custom;
            }
            return custom;
        }
    }

    /// <summary>Turns physics processing on only if this node has per-tick work (see above).</summary>
    protected void RefreshPhysicsProcess()
    {
        SetPhysicsProcess(following || hasCustomPhysicsProcess);
    }

    /// <summary>
    /// Called (via <see cref="GameWorld"/>) when Box3D puts this body to sleep in this peer's simulation, on any
    /// peer, so check <c>authority</c> if only the simulating peer should react. Box3D has no wake signal; poll
    /// <c>is_awake</c> to notice waking.
    /// </summary>
    public virtual void OnFellAsleep() { }

    /// <summary>Called once a body that asked with <see cref="GameWorld.WatchForWake"/> is awake again, or is no longer this peer's to simulate.</summary>
    public virtual void OnWoke() { }

    // ---- Box3DBody signals, as typed events (wired in _Ready) ----
    // Every peer simulates every body (followers as Kinematic), so these fire on all peers; check authority if only
    // the simulating peer should react.

    /// <summary>Sensor only (<see cref="isSensor"/>): a body started overlapping this one.</summary>
    public event Action<Node3D> SensorEntered;

    /// <summary>Sensor only (<see cref="isSensor"/>): a body stopped overlapping this one.</summary>
    public event Action<Node3D> SensorExited;

    /// <summary>Solid body with <see cref="contactMonitor"/> on: a body started touching this one.</summary>
    public event Action<Node3D> ContactStarted;

    /// <summary>Solid body with <see cref="contactMonitor"/> on: a body stopped touching this one.</summary>
    public event Action<Node3D> ContactEnded;

    public override void _Ready()
    {
        Connect(Box3DNames.areaEntered, Callable.From<Node3D>(OnAreaEntered));
        Connect(Box3DNames.areaExited, Callable.From<Node3D>(OnAreaExited));
        Connect(Box3DNames.bodyEntered, Callable.From<Node3D>(OnBodyEntered));
        Connect(Box3DNames.bodyExited, Callable.From<Node3D>(OnBodyExited));
    }

    void OnAreaEntered(Node3D body)
    {
        SensorEntered?.Invoke(body);
    }

    void OnAreaExited(Node3D body)
    {
        SensorExited?.Invoke(body);
    }

    void OnBodyEntered(Node3D body)
    {
        ContactStarted?.Invoke(body);
    }

    void OnBodyExited(Node3D body)
    {
        ContactEnded?.Invoke(body);
    }

    public virtual byte[] GenerateStateUpdate()
    {
        // A sleeping body is at rest; don't let followers drift on whatever velocity it went to sleep with.
        Vector3 velocity = Vector3.Zero;
        Vector3 angularVelocity = Vector3.Zero;
        if (this.IsAwake())
        {
            velocity = this.GetLinearVelocity();
            angularVelocity = this.GetAngularVelocity();
        }
        return new BodySyncState { pos = Position, rot = Rotation, vel = velocity, angVel = angularVelocity }.Pack();
    }

    public virtual void UpdateSyncPriority()
    {
    }

    public virtual byte[] SaveState()
    {
        return null;
    }

    public virtual void LoadState(byte[] state)
    {
    }

    public virtual void ApplyStateUpdate(byte[] update)
    {
        // Resting objects keep being re-sent unchanged; a copy that has already arrived there has nothing to do.
        bool unchanged = update != null && desiredState != null && update.AsSpan().SequenceEqual(desiredState);
        desiredState = update;
        // stateTick 0 is a spawn-time state, which AfterInit puts the body on.
        if (update == null || update.Length == 0 || GameWorld.stateTick == 0 || authority == Lobby.selfPeerID)
        {
            return;
        }
        if (unchanged && !following)
        {
            return;
        }
        if (snapshots == null)
        {
            snapshots = new List<Snapshot>();
        }
        if (snapshots.Count > 0 && snapshots[snapshots.Count - 1].tick >= GameWorld.stateTick)
        {
            return;
        }
        BodySyncState s = BodySyncState.Unpack(update);
        snapshots.Add(new Snapshot { tick = GameWorld.stateTick, pos = s.pos, rot = Quaternion.FromEuler(s.rot), vel = s.vel, angVel = s.angVel });
        if (snapshots.Count > MaxSnapshots)
        {
            snapshots.RemoveAt(0);
        }
        if (!following)
        {
            StartFollowing();
        }
    }

    void StartFollowing()
    {
        following = true;
        blendIn = true;
        RefreshPhysicsProcess();
    }

    /// <summary>
    /// Shows this follower at its authority's newest state instead of <see cref="GameWorld.InterpolationDelay"/> ticks
    /// behind, for as long as this keeps being called each tick: less lag, for more guessing ahead when packets are
    /// late. Eased in and out (see <see cref="playbackLead"/>). E.g. what the local player's magnet rod holds.
    /// </summary>
    public void ShowNewest()
    {
        showNewestFrame = Engine.GetPhysicsFrames();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!following || snapshots == null || snapshots.Count == 0)
        {
            // Nothing to do. Godot can switch processing back on behind AfterInit (it does so at _ready, which on a
            // client can come after Init), so switch it off again here.
            following = false;
            if (!hasCustomPhysicsProcess) SetPhysicsProcess(false);
            return;
        }

        double wantedLead = 0;
        // Asked for this tick or the last: bodies and whatever asks run in no fixed order within a tick.
        if (showNewestFrame + 1 >= Engine.GetPhysicsFrames())
        {
            wantedLead = GameWorld.InterpolationDelay;
        }
        playbackLead = Mathf.MoveToward(playbackLead, wantedLead, PlaybackLeadRate * delta);

        bool done = PlayBack(GameWorld.PlaybackTick(authority) + playbackLead, out Vector3 pos, out Quaternion rot);
        if (blendIn)
        {
            blendIn = false;
            errorPos = Position - pos;
            errorRot = Quaternion * rot.Inverse();
        }
        else
        {
            float keep = Mathf.Exp(-(float)delta / ErrorBlendTime);
            errorPos *= keep;
            errorRot = Quaternion.Identity.Slerp(errorRot, keep);
        }
        bool blended = errorPos.Length() < ArrivedDistance && 1f - Mathf.Abs(errorRot.W) < ArrivedRotation;
        if (blended)
        {
            errorPos = Vector3.Zero;
            errorRot = Quaternion.Identity;
        }
        // The poses are world space, set as the parent-relative Position and Quaternion: this assumes the body sits
        // under the world root (as every spawned object does), at the world origin.
        Position = pos + errorPos;
        Quaternion = (errorRot * rot).Normalized();
        if (done && blended)
        {
            following = false;
            RefreshPhysicsProcess();
        }
    }

    /// <summary>
    /// The authority's pose at <paramref name="playbackTick"/>, from <see cref="snapshots"/>; also sets
    /// <see cref="followVelocity"/>. True once there's nothing left to play: the newest state is at rest, or has been
    /// carried on for <see cref="MaxExtrapolation"/>.
    /// </summary>
    bool PlayBack(double playbackTick, out Vector3 pos, out Quaternion rot)
    {
        // Drop the states playback has moved past, keeping the newest one at or before it.
        while (snapshots.Count >= 2 && snapshots[1].tick <= playbackTick)
        {
            snapshots.RemoveAt(0);
        }
        Snapshot from = snapshots[0];
        if (playbackTick < from.tick)
        {
            // Every state is still ahead of playback (it has only just started): wait at the first.
            pos = from.pos;
            rot = from.rot;
            followVelocity = from.vel;
            followAngularVelocity = from.angVel;
            return false;
        }
        if (snapshots.Count >= 2)
        {
            Snapshot to = snapshots[1];
            float t = (float)((playbackTick - from.tick) / (to.tick - from.tick));
            pos = from.pos.Lerp(to.pos, t);
            rot = from.rot.Slerp(to.rot, t);
            followVelocity = from.vel.Lerp(to.vel, t);
            followAngularVelocity = from.angVel.Lerp(to.angVel, t);
            return false;
        }

        // Playback has passed the newest state: carry it on at its velocity, for a little while.
        float ahead = (float)((playbackTick - from.tick) / Engine.PhysicsTicksPerSecond);
        float carried = Mathf.Min(ahead, MaxExtrapolation);
        pos = from.pos + from.vel * carried;
        rot = from.rot;
        Vector3 spin = from.angVel * carried;
        float angle = spin.Length();
        // Below this the turn doesn't show, and the axis of so small a spin can't be normalized reliably in float
        // (Quaternion throws on an axis that isn't).
        if (angle > 1e-5f)
        {
            // Angular velocity is world space, so the turn goes on the left (before the body's own rotation).
            rot = new Quaternion(spin.Normalized(), angle) * from.rot;
        }
        bool atRest = from.vel == Vector3.Zero && from.angVel == Vector3.Zero;
        if (atRest || ahead >= MaxExtrapolation)
        {
            followVelocity = Vector3.Zero;
            followAngularVelocity = Vector3.Zero;
            return true;
        }
        followVelocity = from.vel;
        followAngularVelocity = from.angVel;
        return false;
    }

    // ---- Box3DBody properties ----
    // Typed wrappers over the extension's properties (C# can't see Box3DBody); see the Box3DBody class docs for
    // what each one does. They go through ClassDB straight to the native property with cached names: a plain
    // Get/Set would first be matched against every property of this C# script, and a string name would be
    // converted to a StringName on every call. The methods are extension methods in GMPOBox3DBodyApi.

    Variant Native(StringName name) => ClassDB.ClassGetProperty(this, name);
    void Native(StringName name, Variant value) => ClassDB.ClassSetProperty(this, name, value);

    /// <summary><see cref="heightFieldMaterials"/> value that punches a hole in a height field cell.</summary>
    public const int HeightFieldHole = 255;

    public BodyTypeEnum bodyType { get => (BodyTypeEnum)Native(Box3DNames.bodyType).AsInt32(); set => Native(Box3DNames.bodyType, (int)value); }
    public ShapeTypeEnum shapeType { get => (ShapeTypeEnum)Native(Box3DNames.shapeType).AsInt32(); set => Native(Box3DNames.shapeType, (int)value); }

    /// <summary>Full extents (not half) of the Box collider, in metres.</summary>
    public Vector3 boxSize { get => Native(Box3DNames.boxSize).AsVector3(); set => Native(Box3DNames.boxSize, value); }
    public float sphereRadius { get => Native(Box3DNames.sphereRadius).AsSingle(); set => Native(Box3DNames.sphereRadius, value); }
    /// <summary>Radius of the Capsule, Cylinder and Cone colliders.</summary>
    public float capsuleRadius { get => Native(Box3DNames.capsuleRadius).AsSingle(); set => Native(Box3DNames.capsuleRadius, value); }
    /// <summary>Total height of the Capsule, Cylinder and Cone colliders.</summary>
    public float capsuleHeight { get => Native(Box3DNames.capsuleHeight).AsSingle(); set => Native(Box3DNames.capsuleHeight, value); }
    public int cylinderSides { get => Native(Box3DNames.cylinderSides).AsInt32(); set => Native(Box3DNames.cylinderSides, value); }
    /// <summary>Source mesh for the Hull, Mesh and FitMesh colliders.</summary>
    public Mesh collisionMesh { get => Native(Box3DNames.collisionMesh).As<Mesh>(); set => Native(Box3DNames.collisionMesh, value); }
    /// <summary>Generates a MeshInstance3D child at runtime that mirrors the collider.</summary>
    public bool autoVisual { get => Native(Box3DNames.autoVisual).AsBool(); set => Native(Box3DNames.autoVisual, value); }

    public float density { get => Native(Box3DNames.density).AsSingle(); set => Native(Box3DNames.density, value); }
    public float friction { get => Native(Box3DNames.friction).AsSingle(); set => Native(Box3DNames.friction, value); }
    public float restitution { get => Native(Box3DNames.restitution).AsSingle(); set => Native(Box3DNames.restitution, value); }
    public float rollingResistance { get => Native(Box3DNames.rollingResistance).AsSingle(); set => Native(Box3DNames.rollingResistance, value); }
    /// <summary>Conveyor-belt drive of the body's own shape (m/s, in the shape's local space), projected onto the contact plane.</summary>
    public Vector3 tangentVelocity { get => Native(Box3DNames.tangentVelocity).AsVector3(); set => Native(Box3DNames.tangentVelocity, value); }
    /// <summary>Free 64-bit tag on the body's own shape; reported by queries and hit events, keyed by Box3DContactRules.</summary>
    public long userMaterialId { get => Native(Box3DNames.userMaterialId).AsInt64(); set => Native(Box3DNames.userMaterialId, value); }
    public float explosionScale { get => Native(Box3DNames.explosionScale).AsSingle(); set => Native(Box3DNames.explosionScale, value); }
    public bool invokeContactCreation { get => Native(Box3DNames.invokeContactCreation).AsBool(); set => Native(Box3DNames.invokeContactCreation, value); }
    public bool speculativeContact { get => Native(Box3DNames.speculativeContact).AsBool(); set => Native(Box3DNames.speculativeContact, value); }
    /// <summary>Bakes the Box3DCollisionShape children into one compound shape (see <see cref="GMPOBox3DBodyApi.GetCompoundInfo"/>).</summary>
    public bool bakedCompound { get => Native(Box3DNames.bakedCompound).AsBool(); set => Native(Box3DNames.bakedCompound, value); }

    public float linearDamping { get => Native(Box3DNames.linearDamping).AsSingle(); set => Native(Box3DNames.linearDamping, value); }
    public float angularDamping { get => Native(Box3DNames.angularDamping).AsSingle(); set => Native(Box3DNames.angularDamping, value); }
    public float gravityScale { get => Native(Box3DNames.gravityScale).AsSingle(); set => Native(Box3DNames.gravityScale, value); }
    public bool canSleep { get => Native(Box3DNames.canSleep).AsBool(); set => Native(Box3DNames.canSleep, value); }
    /// <summary>Speed (m/s) below which the body may fall asleep.</summary>
    public float sleepThreshold { get => Native(Box3DNames.sleepThreshold).AsSingle(); set => Native(Box3DNames.sleepThreshold, value); }
    /// <summary>When off the body is removed from the simulation entirely.</summary>
    public bool enabled { get => Native(Box3DNames.enabled).AsBool(); set => Native(Box3DNames.enabled, value); }

    /// <summary>Turn on to receive the body_entered / body_exited signals (<see cref="ContactStarted"/>).</summary>
    public bool contactMonitor { get => Native(Box3DNames.contactMonitor).AsBool(); set => Native(Box3DNames.contactMonitor, value); }
    /// <summary>Whether this body is visible to other bodies' sensors.</summary>
    public bool sensorEvents { get => Native(Box3DNames.sensorEvents).AsBool(); set => Native(Box3DNames.sensorEvents, value); }
    public bool hitEvents { get => Native(Box3DNames.hitEvents).AsBool(); set => Native(Box3DNames.hitEvents, value); }
    /// <summary>Makes the body a trigger volume (<see cref="SensorEntered"/> / <see cref="SensorExited"/>, <see cref="GMPOBox3DBodyApi.GetOverlappingBodies"/>).</summary>
    public bool isSensor { get => Native(Box3DNames.isSensor).AsBool(); set => Native(Box3DNames.isSensor, value); }
    public bool debugVisualize { get => Native(Box3DNames.debugVisualize).AsBool(); set => Native(Box3DNames.debugVisualize, value); }
    public bool continuous { get => Native(Box3DNames.continuous).AsBool(); set => Native(Box3DNames.continuous, value); }
    public bool contactRecycling { get => Native(Box3DNames.contactRecycling).AsBool(); set => Native(Box3DNames.contactRecycling, value); }
    /// <summary>When off the body still simulates but the node's transform is left alone.</summary>
    public bool syncNodeTransform { get => Native(Box3DNames.syncNodeTransform).AsBool(); set => Native(Box3DNames.syncNodeTransform, value); }
    public bool allowFastRotation { get => Native(Box3DNames.allowFastRotation).AsBool(); set => Native(Box3DNames.allowFastRotation, value); }

    // Layers 1-32 (and 33-64 in the High pair) as signed bit fields, like the native ints: a mask of -1 scans everything.
    public long collisionLayer { get => Native(Box3DNames.collisionLayer).AsInt64(); set => Native(Box3DNames.collisionLayer, value); }
    public long collisionMask { get => Native(Box3DNames.collisionMask).AsInt64(); set => Native(Box3DNames.collisionMask, value); }
    public long collisionLayerHigh { get => Native(Box3DNames.collisionLayerHigh).AsInt64(); set => Native(Box3DNames.collisionLayerHigh, value); }
    public long collisionMaskHigh { get => Native(Box3DNames.collisionMaskHigh).AsInt64(); set => Native(Box3DNames.collisionMaskHigh, value); }
    /// <summary>Box3D group index; when not 0 it overrides the layer and mask bits.</summary>
    public int collisionGroup { get => Native(Box3DNames.collisionGroup).AsInt32(); set => Native(Box3DNames.collisionGroup, value); }

    // Axis locks (world axes)
    public bool lockLinearX { get => Native(Box3DNames.lockLinearX).AsBool(); set => Native(Box3DNames.lockLinearX, value); }
    public bool lockLinearY { get => Native(Box3DNames.lockLinearY).AsBool(); set => Native(Box3DNames.lockLinearY, value); }
    public bool lockLinearZ { get => Native(Box3DNames.lockLinearZ).AsBool(); set => Native(Box3DNames.lockLinearZ, value); }
    public bool lockAngularX { get => Native(Box3DNames.lockAngularX).AsBool(); set => Native(Box3DNames.lockAngularX, value); }
    public bool lockAngularY { get => Native(Box3DNames.lockAngularY).AsBool(); set => Native(Box3DNames.lockAngularY, value); }
    public bool lockAngularZ { get => Native(Box3DNames.lockAngularZ).AsBool(); set => Native(Box3DNames.lockAngularZ, value); }

    // Mesh collider built from raw data (shapeType Mesh)
    public Vector3[] meshVertices { get => Native(Box3DNames.meshVertices).AsVector3Array(); set => Native(Box3DNames.meshVertices, value); }
    public int[] meshIndices { get => Native(Box3DNames.meshIndices).AsInt32Array(); set => Native(Box3DNames.meshIndices, value); }
    /// <summary>One <see cref="surfaceMaterials"/> index per triangle.</summary>
    public byte[] meshMaterials { get => Native(Box3DNames.meshMaterials).AsByteArray(); set => Native(Box3DNames.meshMaterials, value); }
    public float meshWeldTolerance { get => Native(Box3DNames.meshWeldTolerance).AsSingle(); set => Native(Box3DNames.meshWeldTolerance, value); }
    public bool meshMedianSplit { get => Native(Box3DNames.meshMedianSplit).AsBool(); set => Native(Box3DNames.meshMedianSplit, value); }

    // Height field collider (shapeType HeightField)
    /// <summary>Grid line counts along X and Z (not cell counts).</summary>
    public Vector2I heightFieldSize { get => Native(Box3DNames.heightFieldSize).AsVector2I(); set => Native(Box3DNames.heightFieldSize, value); }
    public Vector3 heightFieldScale { get => Native(Box3DNames.heightFieldScale).AsVector3(); set => Native(Box3DNames.heightFieldScale, value); }
    public float[] heightFieldHeights { get => Native(Box3DNames.heightFieldHeights).AsFloat32Array(); set => Native(Box3DNames.heightFieldHeights, value); }
    /// <summary>One <see cref="surfaceMaterials"/> index per cell; <see cref="HeightFieldHole"/> punches a hole.</summary>
    public byte[] heightFieldMaterials { get => Native(Box3DNames.heightFieldMaterials).AsByteArray(); set => Native(Box3DNames.heightFieldMaterials, value); }
    public Vector2 heightFieldWave { get => Native(Box3DNames.heightFieldWave).AsVector2(); set => Native(Box3DNames.heightFieldWave, value); }
    public Vector2 heightFieldHeightRange { get => Native(Box3DNames.heightFieldHeightRange).AsVector2(); set => Native(Box3DNames.heightFieldHeightRange, value); }
    public bool heightFieldHoles { get => Native(Box3DNames.heightFieldHoles).AsBool(); set => Native(Box3DNames.heightFieldHoles, value); }
    public bool heightFieldClockwise { get => Native(Box3DNames.heightFieldClockwise).AsBool(); set => Native(Box3DNames.heightFieldClockwise, value); }

    /// <summary>
    /// Material palette a Mesh or HeightField collider indexes per triangle / cell. Each entry may carry friction,
    /// restitution, rolling_resistance, tangent_velocity, user_material_id and custom_color.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> surfaceMaterials
    {
        get => Native(Box3DNames.surfaceMaterials).AsGodotArray<Godot.Collections.Dictionary>();
        set => Native(Box3DNames.surfaceMaterials, value);
    }
}
