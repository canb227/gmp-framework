using Godot;

/// <summary>What a ray hit: an item (<see cref="itemId"/>) or a body node (<see cref="body"/>), or nothing.</summary>
public struct RayHit
{
    public bool hit;
    public Vector3 position;
    public Vector3 normal;
    /// <summary>0 to 1 along the ray.</summary>
    public float fraction;
    /// <summary>The surface type of what was hit (GMPShape.surface_type, or the item's).</summary>
    public long surfaceType;
    /// <summary>The item hit, or 0.</summary>
    public ulong itemId;
    /// <summary>The GMPBody node hit, or null.</summary>
    public Node3D body;
}

/// <summary>What an overlap test found: item ids and GMPBody nodes.</summary>
public struct Overlap
{
    public long[] items;
    public Godot.Collections.Array<Node3D> bodies;
}

/// <summary>
/// Calls into our Box3D world, the GMPPhysicsWorld node (native/src/physics_world.h) that <see cref="GameWorld"/> makes
/// as <see cref="GameWorld.physics"/>. The C++ class isn't visible to C#, so these call its methods by name; the names
/// are cached so no call converts a string. Loose items are only data in there, known by their id.
/// <para>
/// Each call into the world costs about a microsecond, so loops over many items belong in C++, not here.
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
    static readonly StringName hasItemName = "has_item";
    static readonly StringName getItemTransformName = "get_item_transform";
    static readonly StringName getItemLinearVelocityName = "get_item_linear_velocity";
    static readonly StringName getItemAngularVelocityName = "get_item_angular_velocity";
    static readonly StringName getItemMassName = "get_item_mass";
    static readonly StringName isItemAsleepName = "is_item_asleep";
    static readonly StringName applyItemForceName = "apply_item_force";
    static readonly StringName applyItemImpulseName = "apply_item_impulse";
    static readonly StringName dampItemSpinName = "damp_item_spin";
    static readonly StringName teleportItemName = "teleport_item";
    static readonly StringName setItemReportsTouchesName = "set_item_reports_touches";
    static readonly StringName holdItemName = "hold_item";
    static readonly StringName showItemNewestName = "show_item_newest";
    static readonly StringName getItemsBelowName = "get_items_below";
    static readonly StringName getItemTouchesName = "get_item_touches";
    static readonly StringName getHitsName = "get_hits";
    static readonly StringName setViewersName = "set_viewers";
    static readonly StringName getStatsName = "get_stats";
    static readonly StringName getProfileName = "get_profile";
    static readonly StringName kickRandomItemsName = "kick_random_items";
    static readonly StringName getItemCountName = "get_item_count";

    static readonly Variant hitKey = "hit";
    static readonly Variant positionKey = "position";
    static readonly Variant normalKey = "normal";
    static readonly Variant fractionKey = "fraction";
    static readonly Variant surfaceTypeKey = "surface_type";
    static readonly Variant itemIdKey = "item_id";
    static readonly Variant bodyKey = "body";
    static readonly Variant itemsKey = "items";
    static readonly Variant bodiesKey = "bodies";

    // ---- queries ----

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
        hit.surfaceType = result[surfaceTypeKey].AsInt64();
        hit.itemId = (ulong)result[itemIdKey].AsInt64();
        hit.body = result[bodyKey].As<Node3D>();
        return hit;
    }

    public static Overlap OverlapSphere(Vector3 center, float radius, long mask = ~GameWorld.QueryHiddenLayer)
    {
        return ReadOverlap(world.Call(overlapSphereName, center, radius, mask).AsGodotDictionary());
    }

    /// <summary>The box of full size <paramref name="size"/> placed at <paramref name="transform"/>.</summary>
    public static Overlap OverlapBox(Transform3D transform, Vector3 size, long mask = ~GameWorld.QueryHiddenLayer)
    {
        return ReadOverlap(world.Call(overlapBoxName, transform, size, mask).AsGodotDictionary());
    }

    /// <summary>
    /// What the GMPShape <paramref name="shape"/> would overlap at <paramref name="transform"/> (its own world transform),
    /// made <paramref name="margin"/> metres smaller on every side.
    /// </summary>
    public static Overlap OverlapShape(Node3D shape, Transform3D transform, long mask = ~GameWorld.QueryHiddenLayer, float margin = 0)
    {
        return ReadOverlap(world.Call(overlapShapeName, shape, transform, mask, margin).AsGodotDictionary());
    }

    static Overlap ReadOverlap(Godot.Collections.Dictionary result)
    {
        Overlap overlap = new();
        overlap.items = result[itemsKey].AsInt64Array();
        overlap.bodies = result[bodiesKey].AsGodotArray<Node3D>();
        result.Dispose();
        return overlap;
    }

    // ---- items ----

    public static int RegisterItemKind(Godot.Collections.Dictionary description)
    {
        return world.Call(registerItemKindName, description).AsInt32();
    }

    public static void SpawnItem(ulong id, int kind, ulong authority, Transform3D transform, Vector3 linearVelocity, Vector3 angularVelocity)
    {
        world.Call(spawnItemName, id, kind, authority, transform, linearVelocity, angularVelocity);
    }

    public static void DespawnItem(ulong id)
    {
        world.Call(despawnItemName, id);
    }

    public static void ClearItems()
    {
        world.Call(clearItemsName);
    }

    public static int ItemCount()
    {
        return world.Call(getItemCountName).AsInt32();
    }

    public static bool HasItem(ulong id)
    {
        return world.Call(hasItemName, id).AsBool();
    }

    public static Transform3D ItemTransform(ulong id)
    {
        return world.Call(getItemTransformName, id).AsTransform3D();
    }

    public static Vector3 ItemPosition(ulong id)
    {
        return ItemTransform(id).Origin;
    }

    public static Vector3 ItemLinearVelocity(ulong id)
    {
        return world.Call(getItemLinearVelocityName, id).AsVector3();
    }

    public static Vector3 ItemAngularVelocity(ulong id)
    {
        return world.Call(getItemAngularVelocityName, id).AsVector3();
    }

    public static float ItemMass(ulong id)
    {
        return world.Call(getItemMassName, id).AsSingle();
    }

    /// <summary>Asleep in this peer's simulation (only means something where the item is simulated, the host).</summary>
    public static bool IsItemAsleep(ulong id)
    {
        return world.Call(isItemAsleepName, id).AsBool();
    }

    public static void ApplyItemForce(ulong id, Vector3 force)
    {
        world.Call(applyItemForceName, id, force);
    }

    public static void ApplyItemImpulse(ulong id, Vector3 impulse)
    {
        world.Call(applyItemImpulseName, id, impulse);
    }

    /// <summary>Takes <paramref name="fraction"/> (0 to 1) of the item's spin away.</summary>
    public static void DampItemSpin(ulong id, float fraction)
    {
        world.Call(dampItemSpinName, id, fraction);
    }

    public static void TeleportItem(ulong id, Transform3D transform)
    {
        world.Call(teleportItemName, id, transform);
    }

    /// <summary>Whether the item's new touches with other items are reported (<see cref="ItemTouches"/>).</summary>
    public static void SetItemReportsTouches(ulong id, bool reports)
    {
        world.Call(setItemReportsTouchesName, id, reports);
    }

    /// <summary>A magnet rod holds the item this tick: it is sent often while held and for a second after.</summary>
    public static void HoldItem(ulong id)
    {
        world.Call(holdItemName, id);
    }

    /// <summary>Show this follower at the newest state heard this tick, rather than two sends behind.</summary>
    public static void ShowItemNewest(ulong id)
    {
        world.Call(showItemNewestName, id);
    }

    /// <summary>Items this peer simulates that are below <paramref name="y"/>.</summary>
    public static long[] ItemsBelow(float y)
    {
        return world.Call(getItemsBelowName, y).AsInt64Array();
    }

    /// <summary>The last step's new touches between items where one reports its touches: ids in pairs.</summary>
    public static long[] ItemTouches()
    {
        return world.Call(getItemTouchesName).AsInt64Array();
    }

    /// <summary>
    /// The last step's impacts (at least 1 m/s), for impact sounds: "points" (Vector3[]) and "speeds" (float[]), one
    /// per hit, and "surfaces" and "items" (long[]), two per hit (the A and B side; an item id is 0 for a body node).
    /// Followers collide too while they move, so every peer gets the hits of what it shows.
    /// </summary>
    public static Godot.Collections.Dictionary Hits()
    {
        return world.Call(getHitsName).AsGodotDictionary();
    }

    /// <summary>Every player's position and view, for the items' sync priorities.</summary>
    public static void SetViewers(Vector3[] positions, Vector3[] eyes, Vector3[] looks)
    {
        world.Call(setViewersName, positions, eyes, looks);
    }

    /// <summary>Testing: kicks this many random items this peer simulates upwards.</summary>
    public static void KickRandomItems(int count)
    {
        world.Call(kickRandomItemsName, count);
    }

    // ---- numbers, for the perf window and tests ----

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
