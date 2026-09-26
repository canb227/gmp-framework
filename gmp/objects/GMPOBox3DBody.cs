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
/// Node3D) and reaches the Box3DBody API through <c>Call</c>/<c>Get</c>/<c>Set</c>.
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
        authoredBodyType = GetBodyType();
        if (authority == Lobby.selfPeerID)
        {
            if (SyncHelpers.TryReadTransform(desiredState, out var s))
            {
                Teleport(s.pos, s.rot);
            }

        }
        else
        {
            SetBodyType(BodyTypeEnum.Kinematic);
        }


    }

    /// <summary>The authority simulates with the authored body type; everyone else follows as Kinematic.</summary>
    public virtual void OnAuthorityChanged()
    {
        desiredState = null;
        SetBodyType(authority == Lobby.selfPeerID ? authoredBodyType : BodyTypeEnum.Kinematic);
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
        desiredState = update;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (authority != Lobby.selfPeerID)
        {
            if (SyncHelpers.TryReadTransform(desiredState, out var s))
            {
                SyncHelpers.LerpTransform(this, s, SyncHelpers.LerpWeight(syncLerpRate, delta));
            }
        }
    }

    // ---- Box3DBody properties ----
    // Typed wrappers over the extension's properties (C# can't see Box3DBody); see the Box3DBody class docs for
    // what each one does. Multi-word names differ from the native snake_case ones, so plain Get/Set reach the
    // native property. The single-word ones (density, friction, restitution, enabled, continuous) would share
    // their native name, and Godot routes Get/Set through a script's own C# properties first, so those go through
    // ClassDB to reach the native property instead of calling themselves.

    Variant Native(StringName name) => ClassDB.ClassGetProperty(this, name);
    void Native(StringName name, Variant value) => ClassDB.ClassSetProperty(this, name, value);

    /// <summary><see cref="heightFieldMaterials"/> value that punches a hole in a height field cell.</summary>
    public const int HeightFieldHole = 255;

    public BodyTypeEnum bodyType { get => GetBodyType(); set => SetBodyType(value); }
    public ShapeTypeEnum shapeType { get => (ShapeTypeEnum)Get("shape_type").AsInt32(); set => Set("shape_type", (int)value); }

    /// <summary>Full extents (not half) of the Box collider, in metres.</summary>
    public Vector3 boxSize { get => Get("box_size").AsVector3(); set => Set("box_size", value); }
    public float sphereRadius { get => Get("sphere_radius").AsSingle(); set => Set("sphere_radius", value); }
    /// <summary>Radius of the Capsule, Cylinder and Cone colliders.</summary>
    public float capsuleRadius { get => Get("capsule_radius").AsSingle(); set => Set("capsule_radius", value); }
    /// <summary>Total height of the Capsule, Cylinder and Cone colliders.</summary>
    public float capsuleHeight { get => Get("capsule_height").AsSingle(); set => Set("capsule_height", value); }
    public int cylinderSides { get => Get("cylinder_sides").AsInt32(); set => Set("cylinder_sides", value); }
    /// <summary>Source mesh for the Hull, Mesh and FitMesh colliders.</summary>
    public Mesh collisionMesh { get => Get("collision_mesh").As<Mesh>(); set => Set("collision_mesh", value); }
    /// <summary>Generates a MeshInstance3D child at runtime that mirrors the collider.</summary>
    public bool autoVisual { get => Get("auto_visual").AsBool(); set => Set("auto_visual", value); }

    public float density { get => Native("density").AsSingle(); set => Native("density", value); }
    public float friction { get => Native("friction").AsSingle(); set => Native("friction", value); }
    public float restitution { get => Native("restitution").AsSingle(); set => Native("restitution", value); }
    public float rollingResistance { get => Get("rolling_resistance").AsSingle(); set => Set("rolling_resistance", value); }
    /// <summary>Conveyor-belt drive of the body's own shape (m/s, in the shape's local space), projected onto the contact plane.</summary>
    public Vector3 tangentVelocity { get => Get("tangent_velocity").AsVector3(); set => Set("tangent_velocity", value); }
    /// <summary>Free 64-bit tag on the body's own shape; reported by queries and hit events, keyed by Box3DContactRules.</summary>
    public long userMaterialId { get => Get("user_material_id").AsInt64(); set => Set("user_material_id", value); }
    public float explosionScale { get => Get("explosion_scale").AsSingle(); set => Set("explosion_scale", value); }
    public bool invokeContactCreation { get => Get("invoke_contact_creation").AsBool(); set => Set("invoke_contact_creation", value); }
    public bool speculativeContact { get => Get("speculative_contact").AsBool(); set => Set("speculative_contact", value); }
    /// <summary>Bakes the Box3DCollisionShape children into one compound shape (see <see cref="GetCompoundInfo"/>).</summary>
    public bool bakedCompound { get => Get("baked_compound").AsBool(); set => Set("baked_compound", value); }

    public float linearDamping { get => Get("linear_damping").AsSingle(); set => Set("linear_damping", value); }
    public float angularDamping { get => Get("angular_damping").AsSingle(); set => Set("angular_damping", value); }
    public float gravityScale { get => Get("gravity_scale").AsSingle(); set => Set("gravity_scale", value); }
    public bool canSleep { get => Get("can_sleep").AsBool(); set => Set("can_sleep", value); }
    /// <summary>Speed (m/s) below which the body may fall asleep.</summary>
    public float sleepThreshold { get => Get("sleep_threshold").AsSingle(); set => Set("sleep_threshold", value); }
    /// <summary>When off the body is removed from the simulation entirely.</summary>
    public bool enabled { get => Native("enabled").AsBool(); set => Native("enabled", value); }

    /// <summary>Turn on to receive the body_entered / body_exited signals.</summary>
    public bool contactMonitor { get => Get("contact_monitor").AsBool(); set => Set("contact_monitor", value); }
    /// <summary>Whether this body is visible to other bodies' sensors.</summary>
    public bool sensorEvents { get => Get("sensor_events").AsBool(); set => Set("sensor_events", value); }
    public bool hitEvents { get => Get("hit_events").AsBool(); set => Set("hit_events", value); }
    /// <summary>Makes the body a trigger volume (area_entered / area_exited, <see cref="GetOverlappingBodies"/>).</summary>
    public bool isSensor { get => Get("is_sensor").AsBool(); set => Set("is_sensor", value); }
    public bool debugVisualize { get => Get("debug_visualize").AsBool(); set => Set("debug_visualize", value); }
    public bool continuous { get => Native("continuous").AsBool(); set => Native("continuous", value); }
    public bool contactRecycling { get => Get("contact_recycling").AsBool(); set => Set("contact_recycling", value); }
    /// <summary>When off the body still simulates but the node's transform is left alone.</summary>
    public bool syncNodeTransform { get => Get("sync_node_transform").AsBool(); set => Set("sync_node_transform", value); }
    public bool allowFastRotation { get => Get("allow_fast_rotation").AsBool(); set => Set("allow_fast_rotation", value); }

    // Layers 1-32 (and 33-64 in the High pair) as signed bit fields, like the native ints: a mask of -1 scans everything.
    public long collisionLayer { get => Get("collision_layer").AsInt64(); set => Set("collision_layer", value); }
    public long collisionMask { get => Get("collision_mask").AsInt64(); set => Set("collision_mask", value); }
    public long collisionLayerHigh { get => Get("collision_layer_high").AsInt64(); set => Set("collision_layer_high", value); }
    public long collisionMaskHigh { get => Get("collision_mask_high").AsInt64(); set => Set("collision_mask_high", value); }
    /// <summary>Box3D group index; when not 0 it overrides the layer and mask bits.</summary>
    public int collisionGroup { get => Get("collision_group").AsInt32(); set => Set("collision_group", value); }

    // Axis locks (world axes)
    public bool lockLinearX { get => Get("lock_linear_x").AsBool(); set => Set("lock_linear_x", value); }
    public bool lockLinearY { get => Get("lock_linear_y").AsBool(); set => Set("lock_linear_y", value); }
    public bool lockLinearZ { get => Get("lock_linear_z").AsBool(); set => Set("lock_linear_z", value); }
    public bool lockAngularX { get => Get("lock_angular_x").AsBool(); set => Set("lock_angular_x", value); }
    public bool lockAngularY { get => Get("lock_angular_y").AsBool(); set => Set("lock_angular_y", value); }
    public bool lockAngularZ { get => Get("lock_angular_z").AsBool(); set => Set("lock_angular_z", value); }

    // Mesh collider built from raw data (shapeType Mesh)
    public Vector3[] meshVertices { get => Get("mesh_vertices").AsVector3Array(); set => Set("mesh_vertices", value); }
    public int[] meshIndices { get => Get("mesh_indices").AsInt32Array(); set => Set("mesh_indices", value); }
    /// <summary>One <see cref="surfaceMaterials"/> index per triangle.</summary>
    public byte[] meshMaterials { get => Get("mesh_materials").AsByteArray(); set => Set("mesh_materials", value); }
    public float meshWeldTolerance { get => Get("mesh_weld_tolerance").AsSingle(); set => Set("mesh_weld_tolerance", value); }
    public bool meshMedianSplit { get => Get("mesh_median_split").AsBool(); set => Set("mesh_median_split", value); }

    // Height field collider (shapeType HeightField)
    /// <summary>Grid line counts along X and Z (not cell counts).</summary>
    public Vector2I heightFieldSize { get => Get("height_field_size").AsVector2I(); set => Set("height_field_size", value); }
    public Vector3 heightFieldScale { get => Get("height_field_scale").AsVector3(); set => Set("height_field_scale", value); }
    public float[] heightFieldHeights { get => Get("height_field_heights").AsFloat32Array(); set => Set("height_field_heights", value); }
    /// <summary>One <see cref="surfaceMaterials"/> index per cell; <see cref="HeightFieldHole"/> punches a hole.</summary>
    public byte[] heightFieldMaterials { get => Get("height_field_materials").AsByteArray(); set => Set("height_field_materials", value); }
    public Vector2 heightFieldWave { get => Get("height_field_wave").AsVector2(); set => Set("height_field_wave", value); }
    public Vector2 heightFieldHeightRange { get => Get("height_field_height_range").AsVector2(); set => Set("height_field_height_range", value); }
    public bool heightFieldHoles { get => Get("height_field_holes").AsBool(); set => Set("height_field_holes", value); }
    public bool heightFieldClockwise { get => Get("height_field_clockwise").AsBool(); set => Set("height_field_clockwise", value); }

    /// <summary>
    /// Material palette a Mesh or HeightField collider indexes per triangle / cell. Each entry may carry friction,
    /// restitution, rolling_resistance, tangent_velocity, user_material_id and custom_color.
    /// </summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> surfaceMaterials
    {
        get => Get("surface_materials").AsGodotArray<Godot.Collections.Dictionary>();
        set => Set("surface_materials", value);
    }

    // ---- Box3DBody methods ----
    // C# method names never shadow the native snake_case ones (Godot method names are case-sensitive).

    public BodyTypeEnum GetBodyType()
    {
        return (BodyTypeEnum)Get("body_type").AsInt32();
    }

    public void SetBodyType(BodyTypeEnum value)
    {
        Set("body_type", (int)value);
    }

    /// <summary>Moves the body without sweeping through the space between, clearing the implied motion.</summary>
    public void Teleport(Transform3D transform) => Call("teleport", transform);

    public void Teleport(Vector3 position, Vector3 rotation) => Teleport(new Transform3D(Basis.FromEuler(rotation), position));

    public void Teleport(Vector3 position) => Teleport(new Transform3D(Basis, position));

    /// <summary>Sets the velocity that carries the body to <paramref name="transform"/> after <paramref name="timeStep"/> seconds (kinematic sweeps).</summary>
    public void SetTargetTransform(Transform3D transform, float timeStep, bool wake = true) => Call("set_target_transform", transform, timeStep, wake);

    public void Enable() => enabled = true;

    public void Disable() => enabled = false;

    // Forces, impulses and velocity. Points are world-space positions.
    /// <summary>Force through the centre of mass (N); accumulates over the step, so reapply every physics frame.</summary>
    public void ApplyCentralForce(Vector3 force) => Call("apply_central_force", force);
    /// <summary>Instant momentum change through the centre of mass (N·s).</summary>
    public void ApplyCentralImpulse(Vector3 impulse) => Call("apply_central_impulse", impulse);
    public void ApplyForceAtPoint(Vector3 force, Vector3 point) => Call("apply_force_at_point", force, point);
    public void ApplyImpulseAtPoint(Vector3 impulse, Vector3 point) => Call("apply_impulse_at_point", impulse, point);
    /// <summary>Torque about the centre of mass (N·m); reapply every frame to sustain.</summary>
    public void ApplyTorque(Vector3 torque) => Call("apply_torque", torque);
    public void ApplyAngularImpulse(Vector3 impulse) => Call("apply_angular_impulse", impulse);
    /// <summary>Projected-area drag and lift against the body's velocity; <paramref name="wind"/> is the air velocity (m/s).</summary>
    public void ApplyWind(Vector3 wind, float drag, float lift, float maxSpeed) => Call("apply_wind", wind, drag, lift, maxSpeed);

    public Vector3 GetLinearVelocity() => Call("get_linear_velocity").AsVector3();
    public void SetLinearVelocity(Vector3 velocity) => Call("set_linear_velocity", velocity);
    public Vector3 GetAngularVelocity() => Call("get_angular_velocity").AsVector3();
    public void SetAngularVelocity(Vector3 velocity) => Call("set_angular_velocity", velocity);
    /// <summary>Velocity of a world-space point carried by the body (linear plus spin).</summary>
    public Vector3 GetPointVelocity(Vector3 worldPoint) => Call("get_point_velocity", worldPoint).AsVector3();
    public Vector3 GetLocalPointVelocity(Vector3 localPoint) => Call("get_local_point_velocity", localPoint).AsVector3();

    public bool IsAwake() => Call("is_awake").AsBool();
    public void SetAwake(bool awake) => Call("set_awake", awake);

    // Mass
    public float GetMass() => Call("get_mass").AsSingle();
    public float GetInverseMass() => Call("get_inverse_mass").AsSingle();
    /// <summary>World-space centre of mass.</summary>
    public Vector3 GetCenterOfMass() => Call("get_center_of_mass").AsVector3();
    public Vector3 GetLocalCenterOfMass() => Call("get_local_center_of_mass").AsVector3();
    public Basis GetInertiaTensor() => Call("get_inertia_tensor").AsBasis();
    public Basis GetInverseInertiaTensor() => Call("get_inverse_inertia_tensor").AsBasis();
    /// <summary>{ mass, center (local), inertia }.</summary>
    public Godot.Collections.Dictionary GetMassData() => Call("get_mass_data").AsGodotDictionary();
    /// <summary>Overrides the mass properties derived from density; dropped when shapes change.</summary>
    public void SetMassData(float mass, Vector3 localCenter, Basis inertia) => Call("set_mass_data", mass, localCenter, inertia);
    /// <summary>Recomputes mass from the shapes, discarding any <see cref="SetMassData"/> override.</summary>
    public void ApplyMassFromShapes() => Call("apply_mass_from_shapes");

    // Space conversion
    public Vector3 GetWorldPoint(Vector3 localPoint) => Call("get_world_point", localPoint).AsVector3();
    public Vector3 GetLocalPoint(Vector3 worldPoint) => Call("get_local_point", worldPoint).AsVector3();
    public Vector3 GetWorldVector(Vector3 localVector) => Call("get_world_vector", localVector).AsVector3();
    public Vector3 GetLocalVector(Vector3 worldVector) => Call("get_local_vector", worldVector).AsVector3();

    // Geometry and queries against this body alone
    public Aabb GetAabb() => Call("get_aabb").AsAabb();
    public Vector3 GetClosestPoint(Vector3 target) => Call("get_closest_point", target).AsVector3();
    /// <summary>Distance from <paramref name="target"/> to the body's surface; 0 inside.</summary>
    public float GetClosestDistance(Vector3 target) => Call("get_closest_distance", target).AsSingle();
    /// <summary>Ray against this body only; <paramref name="bodyTransform"/> defaults to identity.</summary>
    public Godot.Collections.Dictionary CastRay(Vector3 from, Vector3 to, Transform3D? bodyTransform = null, long collisionMask = -1, long collisionLayer = -1)
        => Call("cast_ray", from, to, bodyTransform ?? Transform3D.Identity, collisionMask, collisionLayer).AsGodotDictionary();
    /// <summary>Sweeps an axis-aligned box (full <paramref name="size"/>) against this body only.</summary>
    public Godot.Collections.Dictionary CastBox(Vector3 from, Vector3 to, Vector3 size, Transform3D? bodyTransform = null, long collisionMask = -1, long collisionLayer = -1)
        => Call("cast_box", from, to, size, bodyTransform ?? Transform3D.Identity, collisionMask, collisionLayer).AsGodotDictionary();
    /// <summary>Whether any of this body's shapes overlaps the axis-aligned box (full <paramref name="size"/>) at <paramref name="center"/>.</summary>
    public bool OverlapsBox(Vector3 center, Vector3 size, Transform3D? bodyTransform = null, long collisionMask = -1, long collisionLayer = -1)
        => Call("overlaps_box", center, size, bodyTransform ?? Transform3D.Identity, collisionMask, collisionLayer).AsBool();

    // Contacts and sensors
    /// <summary>Current contacts, one Dictionary each (collider, points, normal, ...).</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> GetContacts() => Call("get_contacts").AsGodotArray<Godot.Collections.Dictionary>();
    /// <summary>Bodies currently touching this one, no duplicates.</summary>
    public Godot.Collections.Array<Node3D> GetTouchingBodies() => Call("get_touching_bodies").AsGodotArray<Node3D>();
    /// <summary>Bodies inside this sensor (requires <see cref="isSensor"/>).</summary>
    public Godot.Collections.Array<Node3D> GetOverlappingBodies() => Call("get_overlapping_bodies").AsGodotArray<Node3D>();

    // Shapes, joints, world
    public int GetShapeCount() => Call("get_shape_count").AsInt32();
    public string[] GetShapeNames() => Call("get_shape_names").AsStringArray();
    /// <summary>The Box3DCollisionShape nodes behind the body's shapes (shorter than <see cref="GetShapeCount"/> when the body's own shape has none).</summary>
    public Godot.Collections.Array<Node3D> GetShapeNodes() => Call("get_shape_nodes").AsGodotArray<Node3D>();
    /// <summary>What <see cref="bakedCompound"/> built (sphere/capsule/hull/... counts).</summary>
    public Godot.Collections.Dictionary GetCompoundInfo() => Call("get_compound_info").AsGodotDictionary();
    public int GetMeshMaterialCount() => Call("get_mesh_material_count").AsInt32();
    public Godot.Collections.Dictionary GetMeshMaterial(int index) => Call("get_mesh_material", index).AsGodotDictionary();
    /// <summary>Replaces one surface material of the mesh / height field collider in place, without rebuilding it.</summary>
    public void SetMeshMaterial(int index, Godot.Collections.Dictionary material) => Call("set_mesh_material", index, material);
    public Vector3 GetHeightFieldExtent() => Call("get_height_field_extent").AsVector3();
    public int GetJointCount() => Call("get_joint_count").AsInt32();
    /// <summary>The Box3DJoint nodes attached to this body.</summary>
    public Godot.Collections.Array<Node> GetJoints() => Call("get_joints").AsGodotArray<Node>();
    /// <summary>The Box3DWorld simulating this body, or null.</summary>
    public GMPOBox3DWorld GetWorld() => Call("get_world").As<Node>() as GMPOBox3DWorld;
    public string GetBodyName() => Call("get_body_name").AsString();
    public void SetBodyName(string name) => Call("set_body_name", name);
}
