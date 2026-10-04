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
/// <see cref="GameWorld"/> attaches this script to the world it instantiates. Its properties are wrapped here
/// (through ClassDB with cached names, as in <see cref="GMPOBox3DBody"/>); its methods are extension methods in
/// <see cref="GMPOBox3DWorldApi"/>. See the Box3DWorld class docs for details.
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
    public Vector3 gravity { get => Native(Box3DNames.gravity).AsVector3(); set => Native(Box3DNames.gravity, value); }

    // Stepping
    /// <summary>Advance once per physics frame; turn off to call <see cref="GMPOBox3DWorldApi.Step"/> yourself.</summary>
    public bool autoStep { get => Native(Box3DNames.autoStep).AsBool(); set => Native(Box3DNames.autoStep, value); }
    /// <summary>Run the solver on a background thread while the engine renders, applied next frame.</summary>
    public bool asyncStep { get => Native(Box3DNames.asyncStep).AsBool(); set => Native(Box3DNames.asyncStep, value); }
    public int substepCount { get => Native(Box3DNames.substepCount).AsInt32(); set => Native(Box3DNames.substepCount, value); }
    public int workerCount { get => Native(Box3DNames.workerCount).AsInt32(); set => Native(Box3DNames.workerCount, value); }

    // Solver tuning
    public bool enableSleep { get => Native(Box3DNames.enableSleep).AsBool(); set => Native(Box3DNames.enableSleep, value); }
    public bool enableWarmStarting { get => Native(Box3DNames.enableWarmStarting).AsBool(); set => Native(Box3DNames.enableWarmStarting, value); }
    public bool continuousCollision { get => Native(Box3DNames.continuousCollision).AsBool(); set => Native(Box3DNames.continuousCollision, value); }
    /// <summary>Contact stiffness (Hz).</summary>
    public float contactHertz { get => Native(Box3DNames.contactHertz).AsSingle(); set => Native(Box3DNames.contactHertz, value); }
    public float contactDamping { get => Native(Box3DNames.contactDamping).AsSingle(); set => Native(Box3DNames.contactDamping, value); }
    /// <summary>Maximum overlap push-out speed (m/s).</summary>
    public float contactSpeed { get => Native(Box3DNames.contactSpeed).AsSingle(); set => Native(Box3DNames.contactSpeed, value); }
    public float contactRecycleDistance { get => Native(Box3DNames.contactRecycleDistance).AsSingle(); set => Native(Box3DNames.contactRecycleDistance, value); }
    /// <summary>Impact speed (m/s) below which restitution is ignored.</summary>
    public float restitutionThreshold { get => Native(Box3DNames.restitutionThreshold).AsSingle(); set => Native(Box3DNames.restitutionThreshold, value); }
    /// <summary>Closing speed (m/s) above which <see cref="ContactHit"/> fires.</summary>
    public float hitEventThreshold { get => Native(Box3DNames.hitEventThreshold).AsSingle(); set => Native(Box3DNames.hitEventThreshold, value); }
    /// <summary>Speed cap for dynamic bodies (m/s); 0 keeps Box3D's own default, not "no limit".</summary>
    public float maxLinearSpeed { get => Native(Box3DNames.maxLinearSpeed).AsSingle(); set => Native(Box3DNames.maxLinearSpeed, value); }
    /// <summary>The Box3DContactRules resource driving custom filter / pre-solve / friction / restitution, or null.</summary>
    public Resource contactRules { get => Native(Box3DNames.contactRules).As<Resource>(); set => Native(Box3DNames.contactRules, value); }

    // Capacity hints (pre-size the world's arrays)
    public int capacityStaticBodies { get => Native(Box3DNames.capacityStaticBodies).AsInt32(); set => Native(Box3DNames.capacityStaticBodies, value); }
    public int capacityStaticShapes { get => Native(Box3DNames.capacityStaticShapes).AsInt32(); set => Native(Box3DNames.capacityStaticShapes, value); }
    public int capacityDynamicBodies { get => Native(Box3DNames.capacityDynamicBodies).AsInt32(); set => Native(Box3DNames.capacityDynamicBodies, value); }
    public int capacityDynamicShapes { get => Native(Box3DNames.capacityDynamicShapes).AsInt32(); set => Native(Box3DNames.capacityDynamicShapes, value); }
    public int capacityContacts { get => Native(Box3DNames.capacityContacts).AsInt32(); set => Native(Box3DNames.capacityContacts, value); }

    // Debug drawing
    public bool debugDraw { get => Native(Box3DNames.debugDraw).AsBool(); set => Native(Box3DNames.debugDraw, value); }
    public bool debugDrawShapeBounds { get => Native(Box3DNames.debugDrawShapeBounds).AsBool(); set => Native(Box3DNames.debugDrawShapeBounds, value); }
    public bool debugDrawJoints { get => Native(Box3DNames.debugDrawJoints).AsBool(); set => Native(Box3DNames.debugDrawJoints, value); }
    public bool debugDrawJointExtras { get => Native(Box3DNames.debugDrawJointExtras).AsBool(); set => Native(Box3DNames.debugDrawJointExtras, value); }
    public bool debugDrawMass { get => Native(Box3DNames.debugDrawMass).AsBool(); set => Native(Box3DNames.debugDrawMass, value); }
    public bool debugDrawBodyNames { get => Native(Box3DNames.debugDrawBodyNames).AsBool(); set => Native(Box3DNames.debugDrawBodyNames, value); }
    public bool debugDrawContacts { get => Native(Box3DNames.debugDrawContacts).AsBool(); set => Native(Box3DNames.debugDrawContacts, value); }
    public bool debugDrawAnchorA { get => Native(Box3DNames.debugDrawAnchorA).AsBool(); set => Native(Box3DNames.debugDrawAnchorA, value); }
    public bool debugDrawContactNormals { get => Native(Box3DNames.debugDrawContactNormals).AsBool(); set => Native(Box3DNames.debugDrawContactNormals, value); }
    public bool debugDrawContactForces { get => Native(Box3DNames.debugDrawContactForces).AsBool(); set => Native(Box3DNames.debugDrawContactForces, value); }
    public bool debugDrawContactFeatures { get => Native(Box3DNames.debugDrawContactFeatures).AsBool(); set => Native(Box3DNames.debugDrawContactFeatures, value); }
    public bool debugDrawGraphColors { get => Native(Box3DNames.debugDrawGraphColors).AsBool(); set => Native(Box3DNames.debugDrawGraphColors, value); }
    public bool debugDrawIslands { get => Native(Box3DNames.debugDrawIslands).AsBool(); set => Native(Box3DNames.debugDrawIslands, value); }
    public bool debugDrawSleep { get => Native(Box3DNames.debugDrawSleep).AsBool(); set => Native(Box3DNames.debugDrawSleep, value); }
    /// <summary>Region the debug overlay draws at all; everything outside is culled.</summary>
    public Aabb debugDrawingBounds { get => Native(Box3DNames.debugDrawingBounds).AsAabb(); set => Native(Box3DNames.debugDrawingBounds, value); }
    public float debugForceScale { get => Native(Box3DNames.debugForceScale).AsSingle(); set => Native(Box3DNames.debugForceScale, value); }
    public float debugJointScale { get => Native(Box3DNames.debugJointScale).AsSingle(); set => Native(Box3DNames.debugJointScale, value); }
}
