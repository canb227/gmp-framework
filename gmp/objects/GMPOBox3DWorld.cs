using Godot;
using System;

/// <summary>Box3DWorld <c>make_debug_color</c> material presets.</summary>
public enum DebugMaterialEnum
{
    Default = 0,
    Matte = 1,
    Soft = 2,
    Dead = 3,
    Glossy = 4,
    Metallic = 5
}

/// <summary>
/// Typed C# face of the Box3D physics world. Box3DWorld is a GDExtension class C# can't see, so
/// <see cref="GameWorld"/> attaches this script to the world it instantiates and everything here reaches the
/// native API through <c>Get</c>/<c>Set</c>/<c>Call</c>. See the Box3DWorld class docs for details.
/// <para>
/// Multi-word property names differ from the native snake_case ones, so plain Get/Set reach the native property.
/// <see cref="gravity"/> would share its native name, and Godot routes Get/Set through a script's own C#
/// properties first, so it goes through ClassDB instead of calling itself. Method names never clash (Godot
/// method names are case-sensitive).
/// </para>
/// </summary>
public partial class GMPOBox3DWorld : Node3D
{
    // ---- signals, as typed events (wired in _Ready) ----
    /// <summary>A body fell asleep (Box3D has no matching wake signal; poll is_awake).</summary>
    public event Action<Node3D> BodyFellAsleep;
    /// <summary>Contact began/ended between bodies with contact reporting; the Dictionary describes the contact.</summary>
    public event Action<Godot.Collections.Dictionary> ContactBegan;
    public event Action<Godot.Collections.Dictionary> ContactEnded;
    /// <summary>A fast impact above <see cref="hitEventThreshold"/> on a body with hit_events.</summary>
    public event Action<Godot.Collections.Dictionary> ContactHit;
    /// <summary>A joint's reaction exceeded its threshold: (joint, force, torque).</summary>
    public event Action<Node3D, Vector3, Vector3> JointThresholdExceeded;

    public override void _Ready()
    {
        Connect("body_fell_asleep", Callable.From<Node3D>(b => BodyFellAsleep?.Invoke(b)));
        Connect("contact_began", Callable.From<Godot.Collections.Dictionary>(c => ContactBegan?.Invoke(c)));
        Connect("contact_ended", Callable.From<Godot.Collections.Dictionary>(c => ContactEnded?.Invoke(c)));
        Connect("contact_hit", Callable.From<Godot.Collections.Dictionary>(h => ContactHit?.Invoke(h)));
        Connect("joint_threshold_exceeded", Callable.From<Node3D, Vector3, Vector3>((j, f, t) => JointThresholdExceeded?.Invoke(j, f, t)));
    }

    // ---- properties ----
    Variant Native(StringName name) => ClassDB.ClassGetProperty(this, name);
    void Native(StringName name, Variant value) => ClassDB.ClassSetProperty(this, name, value);

    /// <summary>World gravity (m/s²).</summary>
    public Vector3 gravity { get => Native("gravity").AsVector3(); set => Native("gravity", value); }

    // Stepping
    /// <summary>Advance once per physics frame; turn off to call <see cref="Step"/> yourself.</summary>
    public bool autoStep { get => Get("auto_step").AsBool(); set => Set("auto_step", value); }
    /// <summary>Run the solver on a background thread while the engine renders, applied next frame.</summary>
    public bool asyncStep { get => Get("async_step").AsBool(); set => Set("async_step", value); }
    public int substepCount { get => Get("substep_count").AsInt32(); set => Set("substep_count", value); }
    public int workerCount { get => Get("worker_count").AsInt32(); set => Set("worker_count", value); }

    // Solver tuning
    public bool enableSleep { get => Get("enable_sleep").AsBool(); set => Set("enable_sleep", value); }
    public bool enableWarmStarting { get => Get("enable_warm_starting").AsBool(); set => Set("enable_warm_starting", value); }
    public bool continuousCollision { get => Get("continuous_collision").AsBool(); set => Set("continuous_collision", value); }
    /// <summary>Contact stiffness (Hz).</summary>
    public float contactHertz { get => Get("contact_hertz").AsSingle(); set => Set("contact_hertz", value); }
    public float contactDamping { get => Get("contact_damping").AsSingle(); set => Set("contact_damping", value); }
    /// <summary>Maximum overlap push-out speed (m/s).</summary>
    public float contactSpeed { get => Get("contact_speed").AsSingle(); set => Set("contact_speed", value); }
    public float contactRecycleDistance { get => Get("contact_recycle_distance").AsSingle(); set => Set("contact_recycle_distance", value); }
    /// <summary>Impact speed (m/s) below which restitution is ignored.</summary>
    public float restitutionThreshold { get => Get("restitution_threshold").AsSingle(); set => Set("restitution_threshold", value); }
    /// <summary>Closing speed (m/s) above which <see cref="ContactHit"/> fires.</summary>
    public float hitEventThreshold { get => Get("hit_event_threshold").AsSingle(); set => Set("hit_event_threshold", value); }
    /// <summary>Speed cap for dynamic bodies (m/s); 0 keeps Box3D's own default, not "no limit".</summary>
    public float maxLinearSpeed { get => Get("max_linear_speed").AsSingle(); set => Set("max_linear_speed", value); }
    /// <summary>The Box3DContactRules resource driving custom filter / pre-solve / friction / restitution, or null.</summary>
    public Resource contactRules { get => Get("contact_rules").As<Resource>(); set => Set("contact_rules", value); }

    // Capacity hints (pre-size the world's arrays)
    public int capacityStaticBodies { get => Get("capacity_static_bodies").AsInt32(); set => Set("capacity_static_bodies", value); }
    public int capacityStaticShapes { get => Get("capacity_static_shapes").AsInt32(); set => Set("capacity_static_shapes", value); }
    public int capacityDynamicBodies { get => Get("capacity_dynamic_bodies").AsInt32(); set => Set("capacity_dynamic_bodies", value); }
    public int capacityDynamicShapes { get => Get("capacity_dynamic_shapes").AsInt32(); set => Set("capacity_dynamic_shapes", value); }
    public int capacityContacts { get => Get("capacity_contacts").AsInt32(); set => Set("capacity_contacts", value); }

    // Debug drawing
    public bool debugDraw { get => Get("debug_draw").AsBool(); set => Set("debug_draw", value); }
    public bool debugDrawShapeBounds { get => Get("debug_draw_shape_bounds").AsBool(); set => Set("debug_draw_shape_bounds", value); }
    public bool debugDrawJoints { get => Get("debug_draw_joints").AsBool(); set => Set("debug_draw_joints", value); }
    public bool debugDrawJointExtras { get => Get("debug_draw_joint_extras").AsBool(); set => Set("debug_draw_joint_extras", value); }
    public bool debugDrawMass { get => Get("debug_draw_mass").AsBool(); set => Set("debug_draw_mass", value); }
    public bool debugDrawBodyNames { get => Get("debug_draw_body_names").AsBool(); set => Set("debug_draw_body_names", value); }
    public bool debugDrawContacts { get => Get("debug_draw_contacts").AsBool(); set => Set("debug_draw_contacts", value); }
    public bool debugDrawAnchorA { get => Get("debug_draw_anchor_a").AsBool(); set => Set("debug_draw_anchor_a", value); }
    public bool debugDrawContactNormals { get => Get("debug_draw_contact_normals").AsBool(); set => Set("debug_draw_contact_normals", value); }
    public bool debugDrawContactForces { get => Get("debug_draw_contact_forces").AsBool(); set => Set("debug_draw_contact_forces", value); }
    public bool debugDrawContactFeatures { get => Get("debug_draw_contact_features").AsBool(); set => Set("debug_draw_contact_features", value); }
    public bool debugDrawGraphColors { get => Get("debug_draw_graph_colors").AsBool(); set => Set("debug_draw_graph_colors", value); }
    public bool debugDrawIslands { get => Get("debug_draw_islands").AsBool(); set => Set("debug_draw_islands", value); }
    public bool debugDrawSleep { get => Get("debug_draw_sleep").AsBool(); set => Set("debug_draw_sleep", value); }
    /// <summary>Region the debug overlay draws at all; everything outside is culled.</summary>
    public Aabb debugDrawingBounds { get => Get("debug_drawing_bounds").AsAabb(); set => Set("debug_drawing_bounds", value); }
    public float debugForceScale { get => Get("debug_force_scale").AsSingle(); set => Set("debug_force_scale", value); }
    public float debugJointScale { get => Get("debug_joint_scale").AsSingle(); set => Set("debug_joint_scale", value); }

    // ---- simulation ----
    /// <summary>Advances the simulation by <paramref name="delta"/> seconds (only when <see cref="autoStep"/> is off).</summary>
    public void Step(float delta) => Call("step", delta);
    /// <summary>Explosion impulse on every dynamic body within <paramref name="radius"/>, scaled by facing shape area.</summary>
    public void Explode(Vector3 center, float radius, float impulsePerArea, float falloff = 0f, long collisionMask = -1)
        => Call("explode", center, radius, impulsePerArea, falloff, collisionMask);

    // ---- queries (world space; bodies come back as Node3D, the GMPOBox3DBody script where one is attached) ----
    /// <summary>Closest hit; the Dictionary always has "hit".</summary>
    public Godot.Collections.Dictionary Raycast(Vector3 from, Vector3 to, long collisionMask = -1, long collisionLayer = -1)
        => Call("raycast", from, to, collisionMask, collisionLayer).AsGodotDictionary();
    /// <summary>Every hit along the ray, nearest first.</summary>
    public Godot.Collections.Array<Godot.Collections.Dictionary> RaycastAll(Vector3 from, Vector3 to, long collisionMask = -1, long collisionLayer = -1)
        => Call("raycast_all", from, to, collisionMask, collisionLayer).AsGodotArray<Godot.Collections.Dictionary>();
    /// <summary>Axis-aligned box (full <paramref name="size"/>) swept from <paramref name="from"/> to <paramref name="to"/>; first hit, as <see cref="Raycast"/>.</summary>
    public Godot.Collections.Dictionary ShapeCastBox(Vector3 from, Vector3 to, Vector3 size, long collisionMask = -1, long collisionLayer = -1)
        => Call("shape_cast_box", from, to, size, collisionMask, collisionLayer).AsGodotDictionary();
    /// <summary>A thick raycast.</summary>
    public Godot.Collections.Dictionary ShapeCastSphere(Vector3 from, Vector3 to, float radius, long collisionMask = -1, long collisionLayer = -1)
        => Call("shape_cast_sphere", from, to, radius, collisionMask, collisionLayer).AsGodotDictionary();
    public Godot.Collections.Dictionary ShapeCastCapsule(Vector3 pointA, Vector3 pointB, float radius, Vector3 motion, long collisionMask = -1, long collisionLayer = -1)
        => Call("shape_cast_capsule", pointA, pointB, radius, motion, collisionMask, collisionLayer).AsGodotDictionary();
    public Godot.Collections.Dictionary ShapeCastConvex(Vector3[] points, Vector3 motion, long collisionMask = -1, long collisionLayer = -1)
        => Call("shape_cast_convex", points, motion, collisionMask, collisionLayer).AsGodotDictionary();

    /// <summary>Bodies whose shapes overlap the box (full <paramref name="size"/>) at <paramref name="center"/>.</summary>
    public Godot.Collections.Array<Node3D> OverlapBox(Vector3 center, Vector3 size, long collisionMask = -1, long collisionLayer = -1)
        => Call("overlap_box", center, size, collisionMask, collisionLayer).AsGodotArray<Node3D>();
    public Godot.Collections.Array<Node3D> OverlapSphere(Vector3 center, float radius, long collisionMask = -1, long collisionLayer = -1)
        => Call("overlap_sphere", center, radius, collisionMask, collisionLayer).AsGodotArray<Node3D>();
    public Godot.Collections.Array<Node3D> OverlapCapsule(Vector3 pointA, Vector3 pointB, float radius, long collisionMask = -1, long collisionLayer = -1)
        => Call("overlap_capsule", pointA, pointB, radius, collisionMask, collisionLayer).AsGodotArray<Node3D>();
    public Godot.Collections.Array<Node3D> OverlapConvex(Vector3[] points, long collisionMask = -1, long collisionLayer = -1)
        => Call("overlap_convex", points, collisionMask, collisionLayer).AsGodotArray<Node3D>();
    /// <summary>Broad-phase only: bodies that potentially overlap <paramref name="aabb"/>.</summary>
    public Godot.Collections.Array<Node3D> OverlapAabb(Aabb aabb, long collisionMask = -1, long collisionLayer = -1)
        => Call("overlap_aabb", aabb, collisionMask, collisionLayer).AsGodotArray<Node3D>();
    /// <summary>Tree nodes / leaves the most recent query walked.</summary>
    public Godot.Collections.Dictionary GetLastQueryStats() => Call("get_last_query_stats").AsGodotDictionary();

    // ---- contacts ----
    /// <summary>Whether a kept contact handle still names a live contact.</summary>
    public bool IsContactValid(Vector3I contactId) => Call("is_contact_valid", contactId).AsBool();
    /// <summary>Everything Box3D knows about a contact; empty when the handle is stale.</summary>
    public Godot.Collections.Dictionary GetContactData(Vector3I contactId) => Call("get_contact_data", contactId).AsGodotDictionary();

    // ---- stats and diagnostics ----
    public int GetAwakeBodyCount() => Call("get_awake_body_count").AsInt32();
    /// <summary>Conservative world-space bounds of everything in the world.</summary>
    public Aabb GetBounds() => Call("get_bounds").AsAabb();
    public Godot.Collections.Dictionary GetCounters() => Call("get_counters").AsGodotDictionary();
    /// <summary>Per-phase solver timings (ms) for the last step.</summary>
    public Godot.Collections.Dictionary GetProfile() => Call("get_profile").AsGodotDictionary();
    /// <summary>Wall-clock ms of the last solver step.</summary>
    public float GetStepTimeMs() => Call("get_step_time_ms").AsSingle();
    /// <summary>What the solver is actually running with, read back from Box3D.</summary>
    public Godot.Collections.Dictionary GetLiveSettings() => Call("get_live_settings").AsGodotDictionary();
    public Godot.Collections.Dictionary GetMaxCapacity() => Call("get_max_capacity").AsGodotDictionary();
    public string DumpMemoryStats() => Call("dump_memory_stats").AsString();
    public int GetWorldCount() => Call("get_world_count").AsInt32();
    public int GetMaxWorldCount() => Call("get_max_world_count").AsInt32();
    public string GetBox3DVersion() => Call("get_box3d_version").AsString();
    public bool IsDoublePrecision() => Call("is_double_precision").AsBool();
    public float GetLengthUnitsPerMeter() => Call("get_length_units_per_meter").AsSingle();
    /// <summary>Process-wide length scale; refused once any world exists. Returns whether it took.</summary>
    public bool SetLengthUnitsPerMeter(float units) => Call("set_length_units_per_meter", units).AsBool();

    // ---- debug colours ----
    /// <summary>Packs a colour and material preset into a surface material's custom_color.</summary>
    public long MakeDebugColor(Color color, DebugMaterialEnum material = DebugMaterialEnum.Default) => Call("make_debug_color", color, (int)material).AsInt64();
    public Color GetGraphColor(int index) => Call("get_graph_color", index).AsColor();
    public int GetGraphColorCount() => Call("get_graph_color_count").AsInt32();

    // ---- recording (Box3DRecording resources) ----
    public bool StartRecording(GodotObject recording) => Call("start_recording", recording).AsBool();
    public bool StopRecording() => Call("stop_recording").AsBool();
    public bool IsRecording() => Call("is_recording").AsBool();
    /// <summary>The buffer being recorded into, or null.</summary>
    public GodotObject GetRecording() => Call("get_recording").AsGodotObject();
}
