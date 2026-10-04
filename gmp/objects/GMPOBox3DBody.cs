using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

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
    public bool pauseable { get; set; }
    /// <summary>How quickly non-authority copies close the gap to replicated state (1/s); 0 snaps.</summary>
    [Export]
    public float syncLerpRate { get; set; } = 10f;

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
        if (authority == Lobby.selfPeerID)
        {
            if (SyncHelpers.TryReadTransform(desiredState, out var s))
            {
                this.Teleport(s.pos, s.rot);
            }

        }
        else
        {
            this.SetBodyType(BodyTypeEnum.Kinematic);
        }
        RefreshPhysicsProcess();
    }

    /// <summary>The authority simulates with the authored body type; everyone else follows as Kinematic.</summary>
    public virtual void OnAuthorityChanged()
    {
        desiredState = null;
        following = false;
        this.SetBodyType(authority == Lobby.selfPeerID ? authoredBodyType : BodyTypeEnum.Kinematic);
        RefreshPhysicsProcess();
    }

    // ---- physics processing ----
    // Godot calling into C# costs a few microseconds per node per tick, which adds up across thousands of items
    // (about 7 of a 7.4 ms tick at 2000 resting items, measured). So the base class only keeps
    // physics processing on while a non-authority copy is still easing toward its last received state. Subclasses
    // that override _PhysicsProcess keep it on all the time.

    /// <summary>True while this non-authority copy hasn't yet reached <see cref="desiredState"/>.</summary>
    bool following;

    /// <summary>
    /// Within these a follower counts as arrived, snaps the rest of the way and stops easing: position in metres,
    /// rotation as 1 - |dot| of the two quaternions (1e-6 is about 0.16°). Not an angle: acos near 1 is too noisy
    /// in float to compare against a small threshold.
    /// </summary>
    const float ArrivedDistance = 0.0005f, ArrivedRotation = 1e-6f;

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

    public virtual byte[] GenerateStateUpdate()
    {
        return SyncHelpers.WriteTransform(this);
    }

    public virtual void ApplyStateUpdate(byte[] update)
    {
        // Resting objects keep being re-sent unchanged; a copy that has already arrived there has nothing to do.
        bool unchanged = update != null && desiredState != null && update.AsSpan().SequenceEqual(desiredState);
        desiredState = update;
        if (authority != Lobby.selfPeerID && !following && update != null && !unchanged)
        {
            following = true;
            RefreshPhysicsProcess();
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!following)
        {
            // Nothing to do. Godot can switch processing back on behind AfterInit (it does so at _ready, which on a
            // client can come after Init), so switch it off again here.
            if (!hasCustomPhysicsProcess) SetPhysicsProcess(false);
            return;
        }
        if (authority != Lobby.selfPeerID && SyncHelpers.TryReadTransform(desiredState, out var s))
        {
            SyncHelpers.LerpTransform(this, s, SyncHelpers.LerpWeight(syncLerpRate, delta));
            if (Position.DistanceTo(s.pos) > ArrivedDistance || 1f - Mathf.Abs(Quaternion.Dot(Quaternion.FromEuler(s.rot))) > ArrivedRotation)
            {
                return;
            }
            SyncHelpers.LerpTransform(this, s, 1f); // land exactly on it
        }
        following = false;
        RefreshPhysicsProcess();
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

    /// <summary>Turn on to receive the body_entered / body_exited signals.</summary>
    public bool contactMonitor { get => Native(Box3DNames.contactMonitor).AsBool(); set => Native(Box3DNames.contactMonitor, value); }
    /// <summary>Whether this body is visible to other bodies' sensors.</summary>
    public bool sensorEvents { get => Native(Box3DNames.sensorEvents).AsBool(); set => Native(Box3DNames.sensorEvents, value); }
    public bool hitEvents { get => Native(Box3DNames.hitEvents).AsBool(); set => Native(Box3DNames.hitEvents, value); }
    /// <summary>Makes the body a trigger volume (area_entered / area_exited, <see cref="GMPOBox3DBodyApi.GetOverlappingBodies"/>).</summary>
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
