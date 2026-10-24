using Godot;
using System.Collections.Generic;
using System.Runtime.InteropServices;

/// <summary>What a ray hit: a body (<see cref="id"/>) and its shape, or nothing.</summary>
public struct RayHit
{
    public bool hit;
    public Vector3 position;
    public Vector3 normal;
    /// <summary>0 to 1 along the ray.</summary>
    public float fraction;
    /// <summary>The body hit: a loose item's id, or a node body's (<see cref="PhysicsWorld.BodyNode"/>).</summary>
    public ulong id;
    /// <summary>The shape's number in its body.</summary>
    public int shape;
    /// <summary>The tags of the shape hit (its own plus its body's; see SurfaceTags in the game).</summary>
    public ulong tags;
}

/// <summary>
/// An impact (<see cref="HitStream"/>): a contact approaching at 2 m/s or more, the same on every peer. Side A is the
/// side that comes first in this list (hit_side_rank in native/src/physics_world_events.cpp), so a reader only has to
/// look at one side for what it cares about:
/// <list type="number">
/// <item>a player (PLAYER tag): its own landing or running into a wall, or something hitting it</item>
/// <item>a dropped tool (TOOL tag)</item>
/// <item>a loose item</item>
/// <item>a structure (STRUCTURE tag)</item>
/// <item>the level (LEVEL tag)</item>
/// <item>any other body</item>
/// <item>a side no peer can name (id 0, no tags)</item>
/// </list>
/// Two sides of the same rank (item on item) are A and B either way. Read straight from the bytes the C++ world packs,
/// so it must keep the fields and order of HitRecord (native/src/physics_world.h): 72 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct ContactHit
{
    /// <summary>The body on each side (a loose item's id, or a node body's); 0 for one no peer can name.</summary>
    public ulong idA;
    public ulong idB;
    /// <summary>The tags of the shape on each side (its own plus its body's); 0 for a side with id 0.</summary>
    public ulong tagsA;
    public ulong tagsB;
    /// <summary>The shape's number in its body (-1 for a character's capsule).</summary>
    public int shapeA;
    public int shapeB;
    public Vector3 position;
    /// <summary>From side A toward side B.</summary>
    public Vector3 normal;
    /// <summary>How fast the two sides were approaching (m/s).</summary>
    public float speed;
    /// <summary>
    /// Where this peer got it: <see cref="FromContact"/> (two bodies this peer simulates met), <see cref="FromMover"/>
    /// (a character this peer moves ran or landed into something) or <see cref="Heard"/> (another peer sent it).
    /// Game code that must count each hit once (damage) leaves out the heard ones: their sender counts them.
    /// </summary>
    public int source;

    public const int FromContact = 0;
    public const int FromMover = 1;
    public const int Heard = 2;
}

/// <summary>
/// Calls into our Box3D world, the GMPPhysicsWorld node (native/src/physics_world.h) that <see cref="GameWorld"/> makes
/// as <see cref="GameWorld.physics"/>. Every body in it, loose item or node, is known by an id that is the same on every
/// peer: an item's id, or the one a spawned scene's GMPBody gets (<see cref="NameBodies"/>). The C++ class isn't visible
/// to C#, so these call its methods by name; the names are cached so no call converts a string.
/// <para>
/// Each call into the world costs about a microsecond, so loops over many bodies belong in C++, not here.
/// </para>
/// </summary>
public static class PhysicsWorld
{
    /// <summary>The collision layer every loose item is on (GMPPhysicsWorld.item_layer).</summary>
    public const long ItemLayer = 1L << 1;

    static Node3D world => GameWorld.physics;

    static readonly StringName raycastName = "raycast";
    static readonly StringName overlapSphereName = "overlap_sphere";
    static readonly StringName overlapBoxName = "overlap_box";
    static readonly StringName overlapShapeName = "overlap_shape";
    static readonly StringName registerItemKindName = "register_item_kind";
    static readonly StringName spawnItemName = "spawn_item";
    static readonly StringName despawnItemName = "despawn_item";
    static readonly StringName clearItemsName = "clear_items";
    static readonly StringName getItemCountName = "get_item_count";
    static readonly StringName getItemsBelowName = "get_items_below";
    static readonly StringName kickRandomItemsName = "kick_random_items";
    static readonly StringName getBodyNodeName = "get_body_node";
    static readonly StringName getBodyTransformName = "get_body_transform";
    static readonly StringName getBodyLinearVelocityName = "get_body_linear_velocity";
    static readonly StringName getBodyAngularVelocityName = "get_body_angular_velocity";
    static readonly StringName getBodyMassName = "get_body_mass";
    static readonly StringName applyBodyForceName = "apply_body_force";
    static readonly StringName dampBodySpinName = "damp_body_spin";
    static readonly StringName teleportBodyName = "teleport_body";
    static readonly StringName setBodyTagsName = "set_body_tags";
    static readonly StringName setBodyAuthorityName = "set_body_authority";
    static readonly StringName holdBodyName = "hold_body";
    static readonly StringName showBodyNewestName = "show_body_newest";
    static readonly StringName getHitsName = "get_hits";
    static readonly StringName setViewersName = "set_viewers";
    static readonly StringName getStatsName = "get_stats";
    static readonly StringName getProfileName = "get_profile";
    static readonly StringName setSharedIdName = "set_shared_id";
    static readonly StringName getSharedIdName = "get_shared_id";
    static readonly StringName createDistanceJointName = "create_distance_joint";
    static readonly StringName setDistanceJointRangeName = "set_distance_joint_range";
    static readonly StringName createFilterJointName = "create_filter_joint";
    static readonly StringName destroyJointName = "destroy_joint";
    static readonly StringName createRevoluteJointName = "create_revolute_joint";
    static readonly StringName setRevoluteJointMotorName = "set_revolute_joint_motor";
    static readonly StringName getRevoluteJointAngleName = "get_revolute_joint_angle";

    static readonly Variant hitKey = "hit";
    static readonly Variant positionKey = "position";
    static readonly Variant normalKey = "normal";
    static readonly Variant fractionKey = "fraction";
    static readonly Variant idKey = "id";
    static readonly Variant shapeKey = "shape";
    static readonly Variant tagsKey = "tags";

    // ---- queries: body ids ----

    /// <summary>The closest thing between from and to. By default leaves out <see cref="GameWorld.QueryHiddenLayer"/>; sensors are never hit.</summary>
    public static RayHit Raycast(Vector3 from, Vector3 to, long mask = ~GameWorld.QueryHiddenLayer)
    {
        RayHit hit = new();
        using Godot.Collections.Dictionary result = world.Call(raycastName, from, to, mask).AsGodotDictionary();
        hit.hit = result[hitKey].AsBool();
        if (!hit.hit)
        {
            return hit;
        }
        hit.position = result[positionKey].AsVector3();
        hit.normal = result[normalKey].AsVector3();
        hit.fraction = result[fractionKey].AsSingle();
        hit.id = (ulong)result[idKey].AsInt64();
        hit.shape = result[shapeKey].AsInt32();
        hit.tags = (ulong)result[tagsKey].AsInt64();
        return hit;
    }

    /// <summary>The ids of the bodies overlapping the sphere. By default leaves out <see cref="GameWorld.QueryHiddenLayer"/>; sensors are never found.</summary>
    public static long[] OverlapSphere(Vector3 center, float radius, long mask = ~GameWorld.QueryHiddenLayer)
    {
        return world.Call(overlapSphereName, center, radius, mask).AsInt64Array();
    }

    /// <summary>The box of full size <paramref name="size"/> placed at <paramref name="transform"/>.</summary>
    public static long[] OverlapBox(Transform3D transform, Vector3 size, long mask = ~GameWorld.QueryHiddenLayer)
    {
        return world.Call(overlapBoxName, transform, size, mask).AsInt64Array();
    }

    /// <summary>
    /// What the GMPShape <paramref name="shape"/> would overlap at <paramref name="transform"/> (its own world transform),
    /// made <paramref name="margin"/> metres smaller on every side.
    /// </summary>
    public static long[] OverlapShape(Node3D shape, Transform3D transform, long mask = ~GameWorld.QueryHiddenLayer, float margin = 0)
    {
        return world.Call(overlapShapeName, shape, transform, mask, margin).AsInt64Array();
    }

    // ---- loose items ----

    /// <summary>
    /// Registers a kind of loose item (its colliders, meshes, tags, priority) and returns its index for
    /// <see cref="SpawnItem"/>, or -1 if the description can't be used. ItemWorld builds the description from an item's
    /// dropped scene, once per item id; see ItemWorld.KindOf for its keys.
    /// </summary>
    public static int RegisterItemKind(Godot.Collections.Dictionary description)
    {
        return world.Call(registerItemKindName, description).AsInt32();
    }

    /// <summary>Adds a loose item of <paramref name="kind"/> on this peer only, simulated by <paramref name="authority"/>. Use ItemWorld.Spawn, which keeps the game's record too.</summary>
    public static void SpawnItem(ulong id, int kind, ulong authority, Transform3D transform, Vector3 linearVelocity, Vector3 angularVelocity, bool awake = true)
    {
        world.Call(spawnItemName, id, kind, authority, transform, linearVelocity, angularVelocity, awake);
    }

    /// <summary>Removes a loose item on this peer only. Use ItemWorld.Despawn or RemoveLocal, which keep the game's record too.</summary>
    public static void DespawnItem(ulong id)
    {
        world.Call(despawnItemName, id);
    }

    /// <summary>Removes every loose item on this peer (GameWorld.ClearWorld).</summary>
    public static void ClearItems()
    {
        world.Call(clearItemsName);
    }

    /// <summary>How many loose items there are on this peer.</summary>
    public static int ItemCount()
    {
        return world.Call(getItemCountName).AsInt32();
    }

    /// <summary>Items this peer simulates that are below <paramref name="y"/>.</summary>
    public static long[] ItemsBelow(float y)
    {
        return world.Call(getItemsBelowName, y).AsInt64Array();
    }

    /// <summary>Testing: kicks this many random items this peer simulates upwards.</summary>
    public static void KickRandomItems(int count)
    {
        world.Call(kickRandomItemsName, count);
    }

    // ---- any body, by id ----

    /// <summary>The GMPBody node with this id, or null (a loose item, or nothing).</summary>
    public static Node3D BodyNode(ulong id)
    {
        return world.Call(getBodyNodeName, id).As<Node3D>();
    }

    /// <summary>Where the body is in the world now (identity if there is no body with this id).</summary>
    public static Transform3D BodyTransform(ulong id)
    {
        return world.Call(getBodyTransformName, id).AsTransform3D();
    }

    /// <summary>The body's position: the origin of <see cref="BodyTransform"/>.</summary>
    public static Vector3 BodyPosition(ulong id)
    {
        return BodyTransform(id).Origin;
    }

    /// <summary>The body's velocity (m/s).</summary>
    public static Vector3 BodyLinearVelocity(ulong id)
    {
        return world.Call(getBodyLinearVelocityName, id).AsVector3();
    }

    /// <summary>The body's spin (radians/s about each axis).</summary>
    public static Vector3 BodyAngularVelocity(ulong id)
    {
        return world.Call(getBodyAngularVelocityName, id).AsVector3();
    }

    /// <summary>The body's mass (kg), from its shapes' sizes and densities.</summary>
    public static float BodyMass(ulong id)
    {
        return world.Call(getBodyMassName, id).AsSingle();
    }

    /// <summary>Pushes the body through its centre for the next step (newtons), waking it. Only does anything where the body is simulated.</summary>
    public static void ApplyBodyForce(ulong id, Vector3 force)
    {
        world.Call(applyBodyForceName, id, force);
    }

    /// <summary>Takes <paramref name="fraction"/> (0 to 1) of the body's spin away.</summary>
    public static void DampBodySpin(ulong id, float fraction)
    {
        world.Call(dampBodySpinName, id, fraction);
    }

    /// <summary>Puts the body there at once, without sweeping it across.</summary>
    public static void TeleportBody(ulong id, Transform3D transform)
    {
        world.Call(teleportBodyName, id, transform);
    }

    /// <summary>Gives the whole body these tags (replacing what it was given before); its shapes keep their own.</summary>
    public static void SetBodyTags(ulong id, ulong tags)
    {
        world.Call(setBodyTagsName, id, tags);
    }

    /// <summary>
    /// Makes a dynamic body synced: simulated by <paramref name="peer"/> and followed by everyone else, like loose items
    /// (a node body that isn't dynamic has nothing to sync and is left as it is).
    /// </summary>
    public static void SetBodyAuthority(ulong id, ulong peer)
    {
        world.Call(setBodyAuthorityName, id, peer);
    }

    /// <summary>A gravity gun holds the body this tick: it is sent often while held and for a second after.</summary>
    public static void HoldBody(ulong id)
    {
        world.Call(holdBodyName, id);
    }

    /// <summary>Show this follower at the newest state heard this tick, rather than two sends behind.</summary>
    public static void ShowBodyNewest(ulong id)
    {
        world.Call(showBodyNewestName, id);
    }

    // ---- joints: a handle is a Box3D joint id packed into a long, 0 for none. Make them on every peer (a joint does
    // nothing while neither body is simulated here). Box3D destroys a body's joints with it. ----

    /// <summary>
    /// Holds a point on body A (<paramref name="anchorA"/>, in A's frame) and one on body B between
    /// <paramref name="minLength"/> and <paramref name="maxLength"/> apart, slack in between: a rope from 0 to its
    /// length, a rod when both are the same. The two bodies stop colliding with each other.
    /// </summary>
    public static long CreateDistanceJoint(ulong idA, Vector3 anchorA, ulong idB, Vector3 anchorB, float minLength, float maxLength)
    {
        return world.Call(createDistanceJointName, idA, anchorA, idB, anchorB, minLength, maxLength).AsInt64();
    }

    /// <summary>A new range for a distance joint (a rope reeled in or out); wakes its bodies.</summary>
    public static void SetDistanceJointRange(long joint, float minLength, float maxLength)
    {
        world.Call(setDistanceJointRangeName, joint, minLength, maxLength);
    }

    /// <summary>Only stops the two bodies colliding with each other.</summary>
    public static long CreateFilterJoint(ulong idA, ulong idB)
    {
        return world.Call(createFilterJointName, idA, idB).AsInt64();
    }

    /// <summary>
    /// A hinge: <paramref name="frameA"/> (in A's frame) and <paramref name="frameB"/> (in B's) are held together, turning
    /// only about their shared Z axis, between <paramref name="lowerAngle"/> and <paramref name="upperAngle"/> (radians,
    /// B relative to A; no limit unless lower &lt; upper). Its motor does nothing until <see cref="SetRevoluteJointMotor"/>.
    /// The two bodies stop colliding with each other.
    /// </summary>
    public static long CreateRevoluteJoint(ulong idA, Transform3D frameA, ulong idB, Transform3D frameB, float lowerAngle, float upperAngle)
    {
        return world.Call(createRevoluteJointName, idA, frameA, idB, frameB, lowerAngle, upperAngle).AsInt64();
    }

    /// <summary>Drives the hinge at <paramref name="speed"/> (radians/s) with at most <paramref name="maxTorque"/> (N m); 0 torque lets it swing free.</summary>
    public static void SetRevoluteJointMotor(long joint, float speed, float maxTorque)
    {
        world.Call(setRevoluteJointMotorName, joint, speed, maxTorque);
    }

    /// <summary>How far the hinge has turned (radians, B relative to A); 0 if it's gone.</summary>
    public static float RevoluteJointAngle(long joint)
    {
        return world.Call(getRevoluteJointAngleName, joint).AsSingle();
    }

    /// <summary>Does nothing if the joint is already gone.</summary>
    public static void DestroyJoint(long joint)
    {
        world.Call(destroyJointName, joint);
    }

    /// <summary>The id a GMPBody node's body is known by on every peer (given by <see cref="NameBodies"/>); 0 before that.</summary>
    public static ulong BodyIdOf(Node3D body)
    {
        return (ulong)body.Call(getSharedIdName).AsInt64();
    }

    /// <summary>
    /// Gives the GMPBodies of a spawned scene (or of one GMPObject in it) the ids every peer knows them by: the first,
    /// in tree order, gets <paramref name="id"/> and the rest the ids after it. GMPObjects under <paramref name="root"/>
    /// get their own.
    /// </summary>
    public static void NameBodies(Node root, ulong id)
    {
        ulong next = id;
        NameBodies(root, root, ref next);
    }

    static void NameBodies(Node node, Node root, ref ulong next)
    {
        if (node != root && node is GMPObject)
        {
            return;
        }
        if (node.IsClass("GMPBody"))
        {
            node.Call(setSharedIdName, next);
            next++;
        }
        foreach (Node child in node.GetChildren())
        {
            NameBodies(child, root, ref next);
        }
    }

    // ---- events ----

    /// <summary>
    /// Fills <paramref name="hits"/> with this tick's impacts at least <paramref name="minSpeed"/> (m/s) and within
    /// <paramref name="maxDistance"/> of <paramref name="listener"/>. Read through <see cref="HitStream"/>.
    /// </summary>
    public static void Hits(List<ContactHit> hits, Vector3 listener, float maxDistance, float minSpeed)
    {
        hits.Clear();
        if (System.Runtime.CompilerServices.Unsafe.SizeOf<ContactHit>() != 72)
        {
            GD.PushError("ContactHit no longer matches HitRecord (72 bytes) in native/src/physics_world.h");
            return;
        }
        byte[] bytes = world.Call(getHitsName, listener, maxDistance, minSpeed).AsByteArray();
        // The bytes are ContactHits back to back: viewed as such, no copying field by field.
        hits.AddRange(MemoryMarshal.Cast<byte, ContactHit>(bytes));
    }

    /// <summary>Every player's position and view, for the bodies' sync priorities.</summary>
    public static void SetViewers(Vector3[] positions, Vector3[] eyes, Vector3[] looks)
    {
        world.Call(setViewersName, positions, eyes, looks);
    }

    // ---- numbers, for the perf window and tests ----

    /// <summary>The world's counts and timings (items, awake bodies, step times, sync), for the perf window and tests.</summary>
    public static Godot.Collections.Dictionary Stats()
    {
        return world.Call(getStatsName).AsGodotDictionary();
    }

    /// <summary>Box3D's own timings of the last step (ms) and its counters.</summary>
    public static Godot.Collections.Dictionary Profile()
    {
        return world.Call(getProfileName).AsGodotDictionary();
    }
}
