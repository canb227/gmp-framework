using Godot;

/// <summary>
/// The Box3DWorld methods, as extension methods on <see cref="GMPOBox3DWorld"/>; kept off the node class for the
/// same reason as <see cref="GMPOBox3DBodyApi"/>.
/// </summary>
public static class GMPOBox3DWorldApi
{
    // ---- simulation ----

    /// <summary>Advances the simulation by <paramref name="delta"/> seconds (only when <see cref="GMPOBox3DWorld.autoStep"/> is off).</summary>
    public static void Step(this GMPOBox3DWorld world, float delta) => world.Call(Box3DNames.step, delta);
    /// <summary>Explosion impulse on every dynamic body within <paramref name="radius"/>, scaled by facing shape area.</summary>
    public static void Explode(this GMPOBox3DWorld world, Vector3 center, float radius, float impulsePerArea, float falloff = 0f, long collisionMask = -1)
        => world.Call(Box3DNames.explode, center, radius, impulsePerArea, falloff, collisionMask);

    // ---- queries (world space; bodies come back as Node3D, the GMPOBox3DBody script where one is attached) ----
    /// <summary>Closest hit along the ray.</summary>
    public static RayHit Raycast(this GMPOBox3DWorld world, Vector3 from, Vector3 to, long collisionMask = -1, long collisionLayer = -1)
    {
        using Godot.Collections.Dictionary result = world.Call(Box3DNames.raycast, from, to, collisionMask, collisionLayer).AsGodotDictionary();
        return RayHit.From(result);
    }
    /// <summary>Every hit along the ray, nearest first.</summary>
    public static Godot.Collections.Array<Godot.Collections.Dictionary> RaycastAll(this GMPOBox3DWorld world, Vector3 from, Vector3 to, long collisionMask = -1, long collisionLayer = -1)
        => world.Call(Box3DNames.raycastAll, from, to, collisionMask, collisionLayer).AsGodotArray<Godot.Collections.Dictionary>();
    /// <summary>Axis-aligned box (full <paramref name="size"/>) swept from <paramref name="from"/> to <paramref name="to"/>; first hit, as <see cref="Raycast"/>.</summary>
    public static Godot.Collections.Dictionary ShapeCastBox(this GMPOBox3DWorld world, Vector3 from, Vector3 to, Vector3 size, long collisionMask = -1, long collisionLayer = -1)
        => world.Call(Box3DNames.shapeCastBox, from, to, size, collisionMask, collisionLayer).AsGodotDictionary();
    /// <summary>A thick raycast.</summary>
    public static Godot.Collections.Dictionary ShapeCastSphere(this GMPOBox3DWorld world, Vector3 from, Vector3 to, float radius, long collisionMask = -1, long collisionLayer = -1)
        => world.Call(Box3DNames.shapeCastSphere, from, to, radius, collisionMask, collisionLayer).AsGodotDictionary();
    public static Godot.Collections.Dictionary ShapeCastCapsule(this GMPOBox3DWorld world, Vector3 pointA, Vector3 pointB, float radius, Vector3 motion, long collisionMask = -1, long collisionLayer = -1)
        => world.Call(Box3DNames.shapeCastCapsule, pointA, pointB, radius, motion, collisionMask, collisionLayer).AsGodotDictionary();
    public static Godot.Collections.Dictionary ShapeCastConvex(this GMPOBox3DWorld world, Vector3[] points, Vector3 motion, long collisionMask = -1, long collisionLayer = -1)
        => world.Call(Box3DNames.shapeCastConvex, points, motion, collisionMask, collisionLayer).AsGodotDictionary();

    /// <summary>Bodies whose shapes overlap the box (full <paramref name="size"/>) at <paramref name="center"/>.</summary>
    public static Godot.Collections.Array<Node3D> OverlapBox(this GMPOBox3DWorld world, Vector3 center, Vector3 size, long collisionMask = -1, long collisionLayer = -1)
        => world.Call(Box3DNames.overlapBox, center, size, collisionMask, collisionLayer).AsGodotArray<Node3D>();
    public static Godot.Collections.Array<Node3D> OverlapSphere(this GMPOBox3DWorld world, Vector3 center, float radius, long collisionMask = -1, long collisionLayer = -1)
        => world.Call(Box3DNames.overlapSphere, center, radius, collisionMask, collisionLayer).AsGodotArray<Node3D>();
    public static Godot.Collections.Array<Node3D> OverlapCapsule(this GMPOBox3DWorld world, Vector3 pointA, Vector3 pointB, float radius, long collisionMask = -1, long collisionLayer = -1)
        => world.Call(Box3DNames.overlapCapsule, pointA, pointB, radius, collisionMask, collisionLayer).AsGodotArray<Node3D>();
    public static Godot.Collections.Array<Node3D> OverlapConvex(this GMPOBox3DWorld world, Vector3[] points, long collisionMask = -1, long collisionLayer = -1)
        => world.Call(Box3DNames.overlapConvex, points, collisionMask, collisionLayer).AsGodotArray<Node3D>();
    /// <summary>Broad-phase only: bodies that potentially overlap <paramref name="aabb"/>.</summary>
    public static Godot.Collections.Array<Node3D> OverlapAabb(this GMPOBox3DWorld world, Aabb aabb, long collisionMask = -1, long collisionLayer = -1)
        => world.Call(Box3DNames.overlapAabb, aabb, collisionMask, collisionLayer).AsGodotArray<Node3D>();
    /// <summary>Tree nodes / leaves the most recent query walked.</summary>
    public static Godot.Collections.Dictionary GetLastQueryStats(this GMPOBox3DWorld world) => world.Call(Box3DNames.getLastQueryStats).AsGodotDictionary();

    // ---- contacts ----
    /// <summary>Whether a kept contact handle still names a live contact.</summary>
    public static bool IsContactValid(this GMPOBox3DWorld world, Vector3I contactId) => world.Call(Box3DNames.isContactValid, contactId).AsBool();
    /// <summary>Everything Box3D knows about a contact; empty when the handle is stale.</summary>
    public static Godot.Collections.Dictionary GetContactData(this GMPOBox3DWorld world, Vector3I contactId) => world.Call(Box3DNames.getContactData, contactId).AsGodotDictionary();

    // ---- stats and diagnostics ----
    public static int GetAwakeBodyCount(this GMPOBox3DWorld world) => world.Call(Box3DNames.getAwakeBodyCount).AsInt32();
    /// <summary>Conservative world-space bounds of everything in the world.</summary>
    public static Aabb GetBounds(this GMPOBox3DWorld world) => world.Call(Box3DNames.getBounds).AsAabb();
    public static Godot.Collections.Dictionary GetCounters(this GMPOBox3DWorld world) => world.Call(Box3DNames.getCounters).AsGodotDictionary();
    /// <summary>Per-phase solver timings (ms) for the last step.</summary>
    public static Godot.Collections.Dictionary GetProfile(this GMPOBox3DWorld world) => world.Call(Box3DNames.getProfile).AsGodotDictionary();
    /// <summary>Wall-clock ms of the last solver step.</summary>
    public static float GetStepTimeMs(this GMPOBox3DWorld world) => world.Call(Box3DNames.getStepTimeMs).AsSingle();
    /// <summary>What the solver is actually running with, read back from Box3D.</summary>
    public static Godot.Collections.Dictionary GetLiveSettings(this GMPOBox3DWorld world) => world.Call(Box3DNames.getLiveSettings).AsGodotDictionary();
    public static Godot.Collections.Dictionary GetMaxCapacity(this GMPOBox3DWorld world) => world.Call(Box3DNames.getMaxCapacity).AsGodotDictionary();
    public static string DumpMemoryStats(this GMPOBox3DWorld world) => world.Call(Box3DNames.dumpMemoryStats).AsString();
    public static int GetWorldCount(this GMPOBox3DWorld world) => world.Call(Box3DNames.getWorldCount).AsInt32();
    public static int GetMaxWorldCount(this GMPOBox3DWorld world) => world.Call(Box3DNames.getMaxWorldCount).AsInt32();
    public static string GetBox3DVersion(this GMPOBox3DWorld world) => world.Call(Box3DNames.getBox3dVersion).AsString();
    public static bool IsDoublePrecision(this GMPOBox3DWorld world) => world.Call(Box3DNames.isDoublePrecision).AsBool();
    public static float GetLengthUnitsPerMeter(this GMPOBox3DWorld world) => world.Call(Box3DNames.getLengthUnitsPerMeter).AsSingle();
    /// <summary>Process-wide length scale; refused once any world exists. Returns whether it took.</summary>
    public static bool SetLengthUnitsPerMeter(this GMPOBox3DWorld world, float units) => world.Call(Box3DNames.setLengthUnitsPerMeter, units).AsBool();

    // ---- debug colours ----
    /// <summary>Packs a colour and material preset into a surface material's custom_color.</summary>
    public static long MakeDebugColor(this GMPOBox3DWorld world, Color color, DebugMaterialEnum material = DebugMaterialEnum.Default) => world.Call(Box3DNames.makeDebugColor, color, (int)material).AsInt64();
    public static Color GetGraphColor(this GMPOBox3DWorld world, int index) => world.Call(Box3DNames.getGraphColor, index).AsColor();
    public static int GetGraphColorCount(this GMPOBox3DWorld world) => world.Call(Box3DNames.getGraphColorCount).AsInt32();

    // ---- recording (Box3DRecording resources) ----
    public static bool StartRecording(this GMPOBox3DWorld world, GodotObject recording) => world.Call(Box3DNames.startRecording, recording).AsBool();
    public static bool StopRecording(this GMPOBox3DWorld world) => world.Call(Box3DNames.stopRecording).AsBool();
    public static bool IsRecording(this GMPOBox3DWorld world) => world.Call(Box3DNames.isRecording).AsBool();
    /// <summary>The buffer being recorded into, or null.</summary>
    public static GodotObject GetRecording(this GMPOBox3DWorld world) => world.Call(Box3DNames.getRecording).AsGodotObject();
}

/// <summary>
/// Keys of the Dictionaries the Box3D extension returns (query results, contact and hit events), made once.
/// Indexing with a C# string marshals a new Godot string on every read.
/// </summary>
public static class Box3DKeys
{
    public static readonly Variant hit = "hit";
    public static readonly Variant position = "position";
    public static readonly Variant normal = "normal";
    public static readonly Variant fraction = "fraction";
    public static readonly Variant collider = "collider";
    public static readonly Variant userMaterial = "user_material";
    public static readonly Variant point = "point";
}

/// <summary>A raycast result, read out of the extension's result Dictionary once (see <see cref="GMPOBox3DWorldApi.Raycast"/>).</summary>
public readonly record struct RayHit(bool hit, Vector3 position, Vector3 normal, float fraction, Node collider, long userMaterial)
{
    public static RayHit From(Godot.Collections.Dictionary result)
    {
        if (!result[Box3DKeys.hit].AsBool()) return default;
        // No collider when the shape's body has no node (shouldn't happen for scene bodies, but the key is optional).
        Node collider = result.TryGetValue(Box3DKeys.collider, out Variant c) ? c.As<Node>() : null;
        return new RayHit(true, result[Box3DKeys.position].AsVector3(), result[Box3DKeys.normal].AsVector3(),
            result[Box3DKeys.fraction].AsSingle(), collider, result[Box3DKeys.userMaterial].AsInt64());
    }
}
