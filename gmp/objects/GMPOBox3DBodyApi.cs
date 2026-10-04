using Godot;

/// <summary>
/// The Box3DBody methods, as extension methods on <see cref="GMPOBox3DBody"/> (call sites read the same as
/// instance methods). They live outside the node class on purpose: Godot dispatches every engine->C# call on a
/// node (callbacks, Call by name) through a generated chain that compares the name against each of the class's
/// methods in turn, so every method kept off the node class makes all of its callbacks cheaper.
/// </summary>
public static class GMPOBox3DBodyApi
{
    // ---- Box3DBody methods ----

    // C# method names never shadow the native snake_case ones (Godot method names are case-sensitive).

    public static BodyTypeEnum GetBodyType(this GMPOBox3DBody body)
    {
        return body.bodyType;
    }

    public static void SetBodyType(this GMPOBox3DBody body, BodyTypeEnum value)
    {
        body.bodyType = value;
    }

    /// <summary>Moves the body without sweeping through the space between, clearing the implied motion.</summary>
    public static void Teleport(this GMPOBox3DBody body, Transform3D transform) => body.Call(Box3DNames.teleport, transform);

    public static void Teleport(this GMPOBox3DBody body, Vector3 position, Vector3 rotation) => body.Teleport(new Transform3D(Basis.FromEuler(rotation), position));

    public static void Teleport(this GMPOBox3DBody body, Vector3 position) => body.Teleport(new Transform3D(body.Basis, position));

    /// <summary>Sets the velocity that carries the body to <paramref name="transform"/> after <paramref name="timeStep"/> seconds (kinematic sweeps).</summary>
    public static void SetTargetTransform(this GMPOBox3DBody body, Transform3D transform, float timeStep, bool wake = true) => body.Call(Box3DNames.setTargetTransform, transform, timeStep, wake);

    public static void Enable(this GMPOBox3DBody body) => body.enabled = true;

    public static void Disable(this GMPOBox3DBody body) => body.enabled = false;

    // Forces, impulses and velocity. Points are world-space positions.
    /// <summary>Force through the centre of mass (N); accumulates over the step, so reapply every physics frame.</summary>
    public static void ApplyCentralForce(this GMPOBox3DBody body, Vector3 force) => body.Call(Box3DNames.applyCentralForce, force);
    /// <summary>Instant momentum change through the centre of mass (N·s).</summary>
    public static void ApplyCentralImpulse(this GMPOBox3DBody body, Vector3 impulse) => body.Call(Box3DNames.applyCentralImpulse, impulse);
    public static void ApplyForceAtPoint(this GMPOBox3DBody body, Vector3 force, Vector3 point) => body.Call(Box3DNames.applyForceAtPoint, force, point);
    public static void ApplyImpulseAtPoint(this GMPOBox3DBody body, Vector3 impulse, Vector3 point) => body.Call(Box3DNames.applyImpulseAtPoint, impulse, point);
    /// <summary>Torque about the centre of mass (N·m); reapply every frame to sustain.</summary>
    public static void ApplyTorque(this GMPOBox3DBody body, Vector3 torque) => body.Call(Box3DNames.applyTorque, torque);
    public static void ApplyAngularImpulse(this GMPOBox3DBody body, Vector3 impulse) => body.Call(Box3DNames.applyAngularImpulse, impulse);
    /// <summary>Projected-area drag and lift against the body's velocity; <paramref name="wind"/> is the air velocity (m/s).</summary>
    public static void ApplyWind(this GMPOBox3DBody body, Vector3 wind, float drag, float lift, float maxSpeed) => body.Call(Box3DNames.applyWind, wind, drag, lift, maxSpeed);

    public static Vector3 GetLinearVelocity(this GMPOBox3DBody body) => body.Call(Box3DNames.getLinearVelocity).AsVector3();
    public static void SetLinearVelocity(this GMPOBox3DBody body, Vector3 velocity) => body.Call(Box3DNames.setLinearVelocity, velocity);
    public static Vector3 GetAngularVelocity(this GMPOBox3DBody body) => body.Call(Box3DNames.getAngularVelocity).AsVector3();
    public static void SetAngularVelocity(this GMPOBox3DBody body, Vector3 velocity) => body.Call(Box3DNames.setAngularVelocity, velocity);
    /// <summary>Velocity of a world-space point carried by the body (linear plus spin).</summary>
    public static Vector3 GetPointVelocity(this GMPOBox3DBody body, Vector3 worldPoint) => body.Call(Box3DNames.getPointVelocity, worldPoint).AsVector3();
    public static Vector3 GetLocalPointVelocity(this GMPOBox3DBody body, Vector3 localPoint) => body.Call(Box3DNames.getLocalPointVelocity, localPoint).AsVector3();

    public static bool IsAwake(this GMPOBox3DBody body) => body.Call(Box3DNames.isAwake).AsBool();
    public static void SetAwake(this GMPOBox3DBody body, bool awake) => body.Call(Box3DNames.setAwake, awake);

    // Mass
    public static float GetMass(this GMPOBox3DBody body) => body.Call(Box3DNames.getMass).AsSingle();
    public static float GetInverseMass(this GMPOBox3DBody body) => body.Call(Box3DNames.getInverseMass).AsSingle();
    /// <summary>World-space centre of mass.</summary>
    public static Vector3 GetCenterOfMass(this GMPOBox3DBody body) => body.Call(Box3DNames.getCenterOfMass).AsVector3();
    public static Vector3 GetLocalCenterOfMass(this GMPOBox3DBody body) => body.Call(Box3DNames.getLocalCenterOfMass).AsVector3();
    public static Basis GetInertiaTensor(this GMPOBox3DBody body) => body.Call(Box3DNames.getInertiaTensor).AsBasis();
    public static Basis GetInverseInertiaTensor(this GMPOBox3DBody body) => body.Call(Box3DNames.getInverseInertiaTensor).AsBasis();
    /// <summary>{ mass, center (local), inertia }.</summary>
    public static Godot.Collections.Dictionary GetMassData(this GMPOBox3DBody body) => body.Call(Box3DNames.getMassData).AsGodotDictionary();
    /// <summary>Overrides the mass properties derived from density; dropped when shapes change.</summary>
    public static void SetMassData(this GMPOBox3DBody body, float mass, Vector3 localCenter, Basis inertia) => body.Call(Box3DNames.setMassData, mass, localCenter, inertia);
    /// <summary>Recomputes mass from the shapes, discarding any <see cref="SetMassData"/> override.</summary>
    public static void ApplyMassFromShapes(this GMPOBox3DBody body) => body.Call(Box3DNames.applyMassFromShapes);

    // Space conversion
    public static Vector3 GetWorldPoint(this GMPOBox3DBody body, Vector3 localPoint) => body.Call(Box3DNames.getWorldPoint, localPoint).AsVector3();
    public static Vector3 GetLocalPoint(this GMPOBox3DBody body, Vector3 worldPoint) => body.Call(Box3DNames.getLocalPoint, worldPoint).AsVector3();
    public static Vector3 GetWorldVector(this GMPOBox3DBody body, Vector3 localVector) => body.Call(Box3DNames.getWorldVector, localVector).AsVector3();
    public static Vector3 GetLocalVector(this GMPOBox3DBody body, Vector3 worldVector) => body.Call(Box3DNames.getLocalVector, worldVector).AsVector3();

    // Geometry and queries against this body alone
    public static Aabb GetAabb(this GMPOBox3DBody body) => body.Call(Box3DNames.getAabb).AsAabb();
    public static Vector3 GetClosestPoint(this GMPOBox3DBody body, Vector3 target) => body.Call(Box3DNames.getClosestPoint, target).AsVector3();
    /// <summary>Distance from <paramref name="target"/> to the body's surface; 0 inside.</summary>
    public static float GetClosestDistance(this GMPOBox3DBody body, Vector3 target) => body.Call(Box3DNames.getClosestDistance, target).AsSingle();
    /// <summary>Ray against this body only; <paramref name="bodyTransform"/> defaults to identity.</summary>
    public static Godot.Collections.Dictionary CastRay(this GMPOBox3DBody body, Vector3 from, Vector3 to, Transform3D? bodyTransform = null, long collisionMask = -1, long collisionLayer = -1)
        => body.Call(Box3DNames.castRay, from, to, bodyTransform ?? Transform3D.Identity, collisionMask, collisionLayer).AsGodotDictionary();
    /// <summary>Sweeps an axis-aligned box (full <paramref name="size"/>) against this body only.</summary>
    public static Godot.Collections.Dictionary CastBox(this GMPOBox3DBody body, Vector3 from, Vector3 to, Vector3 size, Transform3D? bodyTransform = null, long collisionMask = -1, long collisionLayer = -1)
        => body.Call(Box3DNames.castBox, from, to, size, bodyTransform ?? Transform3D.Identity, collisionMask, collisionLayer).AsGodotDictionary();
    /// <summary>Whether any of this body's shapes overlaps the axis-aligned box (full <paramref name="size"/>) at <paramref name="center"/>.</summary>
    public static bool OverlapsBox(this GMPOBox3DBody body, Vector3 center, Vector3 size, Transform3D? bodyTransform = null, long collisionMask = -1, long collisionLayer = -1)
        => body.Call(Box3DNames.overlapsBox, center, size, bodyTransform ?? Transform3D.Identity, collisionMask, collisionLayer).AsBool();

    // Contacts and sensors
    /// <summary>Current contacts, one Dictionary each (collider, points, normal, ...).</summary>
    public static Godot.Collections.Array<Godot.Collections.Dictionary> GetContacts(this GMPOBox3DBody body) => body.Call(Box3DNames.getContacts).AsGodotArray<Godot.Collections.Dictionary>();
    /// <summary>Bodies currently touching this one, no duplicates.</summary>
    public static Godot.Collections.Array<Node3D> GetTouchingBodies(this GMPOBox3DBody body) => body.Call(Box3DNames.getTouchingBodies).AsGodotArray<Node3D>();
    /// <summary>Bodies inside this sensor (requires <see cref="GMPOBox3DBody.isSensor"/>).</summary>
    public static Godot.Collections.Array<Node3D> GetOverlappingBodies(this GMPOBox3DBody body) => body.Call(Box3DNames.getOverlappingBodies).AsGodotArray<Node3D>();

    // Shapes, joints, world
    public static int GetShapeCount(this GMPOBox3DBody body) => body.Call(Box3DNames.getShapeCount).AsInt32();
    public static string[] GetShapeNames(this GMPOBox3DBody body) => body.Call(Box3DNames.getShapeNames).AsStringArray();
    /// <summary>The Box3DCollisionShape nodes behind the body's shapes (shorter than <see cref="GetShapeCount"/> when the body's own shape has none).</summary>
    public static Godot.Collections.Array<Node3D> GetShapeNodes(this GMPOBox3DBody body) => body.Call(Box3DNames.getShapeNodes).AsGodotArray<Node3D>();
    /// <summary>What <see cref="GMPOBox3DBody.bakedCompound"/> built (sphere/capsule/hull/... counts).</summary>
    public static Godot.Collections.Dictionary GetCompoundInfo(this GMPOBox3DBody body) => body.Call(Box3DNames.getCompoundInfo).AsGodotDictionary();
    public static int GetMeshMaterialCount(this GMPOBox3DBody body) => body.Call(Box3DNames.getMeshMaterialCount).AsInt32();
    public static Godot.Collections.Dictionary GetMeshMaterial(this GMPOBox3DBody body, int index) => body.Call(Box3DNames.getMeshMaterial, index).AsGodotDictionary();
    /// <summary>Replaces one surface material of the mesh / height field collider in place, without rebuilding it.</summary>
    public static void SetMeshMaterial(this GMPOBox3DBody body, int index, Godot.Collections.Dictionary material) => body.Call(Box3DNames.setMeshMaterial, index, material);
    public static Vector3 GetHeightFieldExtent(this GMPOBox3DBody body) => body.Call(Box3DNames.getHeightFieldExtent).AsVector3();
    public static int GetJointCount(this GMPOBox3DBody body) => body.Call(Box3DNames.getJointCount).AsInt32();
    /// <summary>The Box3DJoint nodes attached to this body.</summary>
    public static Godot.Collections.Array<Node> GetJoints(this GMPOBox3DBody body) => body.Call(Box3DNames.getJoints).AsGodotArray<Node>();
    /// <summary>The Box3DWorld simulating this body, or null.</summary>
    public static GMPOBox3DWorld GetWorld(this GMPOBox3DBody body) => body.Call(Box3DNames.getWorld).As<Node>() as GMPOBox3DWorld;
    public static string GetBodyName(this GMPOBox3DBody body) => body.Call(Box3DNames.getBodyName).AsString();
    public static void SetBodyName(this GMPOBox3DBody body, string name) => body.Call(Box3DNames.setBodyName, name);
}
