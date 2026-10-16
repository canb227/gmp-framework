using Godot;
using Limbo.Console.Sharp;
using System;
using System.Collections.Generic;

/// <summary>FactoryGame's own console commands, mostly for testing. Registered in <see cref="Console"/>._Ready.</summary>
public partial class Console
{
    /// <summary>One line per player in this instance's world: name, authority, whether it's ours, position.</summary>
    public void players()
    {
        if (FactoryPlayer.all.Count == 0)
            LimboConsole.Info("players: none");
        foreach (FactoryPlayer player in FactoryPlayer.all)
        {
            Vector3 p = player.GlobalPosition;
            LimboConsole.Info($"player: name={player.playerName} authority={player.authority} local={player.isLocal} pos=({p.X:0.00},{p.Y:0.00},{p.Z:0.00})");
        }
    }

    /// <summary>Moves the local player to x y z.</summary>
    public void tp(float x, float y, float z)
    {
        FactoryPlayer player = LocalPlayer();
        if (player == null)
        {
            LimboConsole.Error("No local player to move (is the game running?).");
            return;
        }
        player.Teleport(new Vector3(x, y, z));
        LimboConsole.Info($"tp: {player.playerName} to ({x},{y},{z})");
    }

    /// <summary>Turns the local player to look at x y z.</summary>
    public void look_at(float x, float y, float z)
    {
        FactoryPlayer player = LocalPlayer();
        if (player == null)
        {
            LimboConsole.Error("No local player to turn (is the game running?).");
            return;
        }
        player.FaceTowards(new Vector3(x, y, z));
        LimboConsole.Info($"look_at: {player.playerName} faces ({x},{y},{z})");
    }

    static FactoryPlayer LocalPlayer()
    {
        foreach (FactoryPlayer player in FactoryPlayer.all)
        {
            if (player.isLocal)
                return player;
        }
        return null;
    }

    // ---- bodies for performance tests (host only) ----
    // spawn_bodies drops scrap pieces in a stack of layers in front of the host's player; more calls add layers on
    // top. stir keeps them awake with random kicks. Both run from _PhysicsProcess (Box3D calls belong there).

    const string BodyItem = "scrap_piece1";
    /// <summary>Bodies per layer side: a layer is BodiesPerSide x BodiesPerSide, BodySpacing apart.</summary>
    const int BodiesPerSide = 20;

    /// <summary>
    /// The test arena (test_arena): a 31 m floor with 3 m walls and no roof, high above the DevArea level so a big pile
    /// touches nothing else. Its floor's top is at ArenaPosition.Y + 0.5.
    /// </summary>
    const string ArenaScene = "res://dev/test_arena/test_arena.tscn";
    static readonly Vector3 ArenaPosition = new Vector3(100, 60, 15);
    bool arenaSpawned;
    const float BodySpacing = 0.6f;
    /// <summary>Spawned per physics tick, so a big batch is spread over a second or so instead of one long frame.</summary>
    const int BodiesPerTick = 50;

    int bodiesToSpawn;
    /// <summary>Bodies placed in the stack so far (sets the next one's layer); back to 0 on clear_bodies.</summary>
    int bodiesPlaced;
    Vector3 bodiesCorner;

    /// <summary>Each body is kicked this many times a second while stirring; 0 is off.</summary>
    float stirRate;
    readonly List<PhysicalFactoryItem> stirBodies = new();
    int stirCursor;
    double stirKicksOwed;

    /// <summary>Host: spawns count scrap pieces in front of the local player (in layers of 15x15, more calls stack on top).</summary>
    public void spawn_bodies(int count)
    {
        FactoryPlayer player = LocalPlayer();
        if (!Lobby.isHost || !GameWorld.started || player == null)
        {
            LimboConsole.Error("Only the host can spawn bodies, in a running game.");
            return;
        }
        if (bodiesPlaced == 0 && arenaSpawned)
        {
            float side = BodiesPerSide * BodySpacing;
            bodiesCorner = ArenaPosition + new Vector3(-side / 2, 1.5f, -side / 2);
        }
        else if (bodiesPlaced == 0)
        {
            Vector3 forward = -player.GlobalBasis.Z;
            forward.Y = 0;
            forward = forward.Normalized();
            float side = BodiesPerSide * BodySpacing;
            Vector3 center = player.GlobalPosition + forward * (side / 2 + 2);
            bodiesCorner = center - new Vector3(side / 2, 0, side / 2) + new Vector3(0, 1, 0);
        }
        bodiesToSpawn += count;
        LimboConsole.Info($"spawn_bodies: {count} more {BodyItem}, {bodiesPlaced + bodiesToSpawn} in all");
    }

    /// <summary>
    /// The spawned bodies in this instance: how many, how many awake (authority only), the lowest and highest, and a
    /// count per 2 m of height, to see where a pile ended up.
    /// </summary>
    public void bodies()
    {
        int count = 0;
        int awake = 0;
        double positionSum = 0;
        float lowest = float.MaxValue;
        float highest = float.MinValue;
        SortedDictionary<int, int> perBand = new();
        foreach (GMPObject obj in GameWorld.syncedObjs.Values)
        {
            if (obj is not PhysicalFactoryItem item || item.itemID != BodyItem)
                continue;
            count++;
            if (item.authority == Lobby.selfPeerID && !item.asleep)
                awake++;
            Vector3 position = item.GlobalPosition;
            positionSum += position.X + position.Y + position.Z;
            float y = position.Y;
            lowest = Math.Min(lowest, y);
            highest = Math.Max(highest, y);
            int band = Mathf.FloorToInt(y / 2) * 2;
            perBand.TryGetValue(band, out int inBand);
            perBand[band] = inBand + 1;
        }
        string bands = "";
        foreach ((int band, int inBand) in perBand)
        {
            bands += $" {band}m:{inBand}";
        }
        // The position sum compares two peers' copies: equal (to a few mm) once followers have placed every body.
        LimboConsole.Info($"bodies: count={count} awake={awake} y={lowest:0.0}..{highest:0.0} sum={positionSum:0.00} reactions={TagInteractions.reactions} |{bands}");
    }

    /// <summary>
    /// Host: spawns the test arena (once) for every peer; spawn_bodies then drops bodies in its middle. Each player
    /// moves itself there with tp; the reply gives a spot on the floor near the south wall.
    /// </summary>
    public void test_arena()
    {
        if (!Lobby.isHost || !GameWorld.started)
        {
            LimboConsole.Error("Only the host can make the arena, in a running game.");
            return;
        }
        if (!arenaSpawned)
        {
            ulong id = GameWorld.SpawnScene(ArenaScene, ArenaPosition);
            arenaSpawned = true;
            if (GameWorld.syncedObjs.TryGetValue(id, out GMPObject obj) && obj is GMPOBox3DBody arena)
                LimboConsole.Info($"test_arena: body at {arena.GlobalPosition}, type {arena.GetBodyType()}, bounds {arena.GetAabb()}");
            else
                LimboConsole.Error("test_arena: the arena didn't spawn");
        }
        Vector3 spot = ArenaPosition + new Vector3(0, 1.5f, -13);
        LimboConsole.Info($"test_arena: at {ArenaPosition}; stand at tp {spot.X} {spot.Y} {spot.Z}");
    }

    /// <summary>
    /// Turns Box3D's contact reporting (contact_monitor, which raises body_entered for TagInteractions) on (1) or off (0)
    /// for every spawned body in this instance, to measure what it costs.
    /// </summary>
    public void contacts(int on)
    {
        int count = 0;
        foreach (GMPObject obj in GameWorld.syncedObjs.Values)
        {
            if (obj is PhysicalFactoryItem item && item.itemID == BodyItem)
            {
                item.contactMonitor = on != 0;
                count++;
            }
        }
        LimboConsole.Info($"contacts: contact_monitor {on != 0} on {count} bodies");
    }

    /// <summary>
    /// Whether Box3D writes each spawned body's transform to its node every tick (sync_node_transform), on (1) or off
    /// (0), to measure what that costs. Off, the bodies still simulate but are drawn where they were.
    /// </summary>
    public void nodesync(int on)
    {
        int count = 0;
        foreach (GMPObject obj in GameWorld.syncedObjs.Values)
        {
            if (obj is PhysicalFactoryItem item && item.itemID == BodyItem)
            {
                item.syncNodeTransform = on != 0;
                count++;
            }
        }
        LimboConsole.Info($"nodesync: sync_node_transform {on != 0} on {count} bodies");
    }

    /// <summary>
    /// Times, per body, each engine call that building a body's state makes (and the whole build), over every spawned
    /// body this instance simulates: microseconds per call.
    /// </summary>
    public void bench_state()
    {
        List<PhysicalFactoryItem> items = new();
        foreach (GMPObject obj in GameWorld.syncedObjs.Values)
        {
            if (obj is PhysicalFactoryItem item && item.itemID == BodyItem && item.authority == Lobby.selfPeerID)
                items.Add(item);
        }
        if (items.Count == 0)
        {
            LimboConsole.Error("No spawned bodies simulated here.");
            return;
        }
        double usPerTick = 1000000.0 / System.Diagnostics.Stopwatch.Frequency / items.Count;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (PhysicalFactoryItem item in items) item.IsAwake();
        double awake = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (PhysicalFactoryItem item in items) item.GetLinearVelocity();
        double linear = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (PhysicalFactoryItem item in items) item.GetAngularVelocity();
        double angular = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (PhysicalFactoryItem item in items) _ = item.Position;
        double position = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (PhysicalFactoryItem item in items) _ = item.Rotation;
        double rotation = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (PhysicalFactoryItem item in items) _ = item.Transform;
        double transform = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (PhysicalFactoryItem item in items) _ = item.GlobalPosition;
        double globalPosition = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        Transform3D[] transforms = new Transform3D[items.Count];
        for (int i = 0; i < items.Count; i++) transforms[i] = items[i].Transform;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (Transform3D t in transforms) _ = t.Basis.Orthonormalized().GetEuler();
        double euler = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (Transform3D t in transforms) _ = new BodySyncState { pos = t.Origin, rot = t.Basis.GetEuler(), vel = Vector3.One, angVel = Vector3.One }.Pack();
        double pack = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        bool hasProperty = false;
        foreach (Godot.Collections.Dictionary property in ClassDB.ClassGetPropertyList("Box3DBody"))
        {
            if ((string)property["name"] == "linear_velocity") hasProperty = true;
        }
        StringName linearVelocityName = "linear_velocity";
        double viaProperty = -1;
        if (hasProperty)
        {
            start = System.Diagnostics.Stopwatch.GetTimestamp();
            foreach (PhysicalFactoryItem item in items) ClassDB.ClassGetProperty(item, linearVelocityName);
            viaProperty = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        }
        int stillAsleep = 0;
        foreach (PhysicalFactoryItem item in items) if (item.asleep) stillAsleep++;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (PhysicalFactoryItem item in items) item.GenerateStateUpdate();
        double build = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        foreach (PhysicalFactoryItem item in items) item.UpdateSyncPriority();
        double priority = (System.Diagnostics.Stopwatch.GetTimestamp() - start) * usPerTick;
        LimboConsole.Info($"bench_state: {items.Count} bodies, us per call: is_awake {awake:0.00}, linear velocity {linear:0.00}, angular velocity {angular:0.00}, Position {position:0.00}, Rotation {rotation:0.00}, Transform {transform:0.00}, GlobalPosition {globalPosition:0.00} | euler {euler:0.00}, Pack {pack:0.00}, linear_velocity property {hasProperty} {viaProperty:0.00} | GenerateStateUpdate {build:0.00} ({stillAsleep} flagged asleep), UpdateSyncPriority {priority:0.00}");
    }

    /// <summary>Makes the first n spawned bodies MAGNETIC (instead of METAL, FERROUS), to test tag reactions.</summary>
    public void magnetize(int n)
    {
        int done = 0;
        foreach (GMPObject obj in GameWorld.syncedObjs.Values)
        {
            if (done >= n)
                break;
            if (obj is PhysicalFactoryItem item && item.itemID == BodyItem)
            {
                item.tags = new Godot.Collections.Array<ItemTags> { ItemTags.MAGNETIC };
                item.RefreshTouchReporting();
                done++;
            }
        }
        LimboConsole.Info($"magnetize: {done} bodies are MAGNETIC");
    }

    /// <summary>Host: despawns every scrap piece of the kind spawn_bodies makes.</summary>
    public void clear_bodies()
    {
        List<ulong> ids = new();
        foreach (GMPObject obj in GameWorld.syncedObjs.Values)
        {
            if (obj is PhysicalFactoryItem item && item.itemID == BodyItem && item.authority == Lobby.selfPeerID)
                ids.Add(item.id);
        }
        foreach (ulong id in ids)
        {
            GameWorld.DespawnObject(id);
        }
        bodiesToSpawn = 0;
        bodiesPlaced = 0;
        stirBodies.Clear();
        LimboConsole.Info($"clear_bodies: despawned {ids.Count}");
    }

    /// <summary>Host: kicks every spawned body this many times a second, keeping them awake and moving; 0 stops.</summary>
    public void stir(float kicksPerSecond)
    {
        stirRate = kicksPerSecond;
        stirBodies.Clear();
        LimboConsole.Info($"stir: {kicksPerSecond} kicks per body per second");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!GameWorld.started || GetTree().Paused)
            return;
        FloodTick();
        SpawnPendingBodies();
        StirBodies(delta);
    }

    void SpawnPendingBodies()
    {
        int count = Math.Min(bodiesToSpawn, BodiesPerTick);
        for (int i = 0; i < count; i++)
        {
            int perLayer = BodiesPerSide * BodiesPerSide;
            int layer = bodiesPlaced / perLayer;
            int inLayer = bodiesPlaced % perLayer;
            float x = (inLayer % BodiesPerSide) * BodySpacing;
            float z = (inLayer / BodiesPerSide) * BodySpacing;
            Vector3 rotation = new Vector3(Random.Shared.NextSingle(), Random.Shared.NextSingle(), Random.Shared.NextSingle()) * Mathf.Tau;
            ItemInfo.SpawnInWorld(BodyItem, bodiesCorner + new Vector3(x, layer * BodySpacing, z), rotation);
            bodiesPlaced++;
        }
        bodiesToSpawn -= count;
    }

    void StirBodies(double delta)
    {
        if (stirRate <= 0)
            return;
        // The list is rebuilt whenever it has gone round once, so it picks up new bodies and drops despawned ones.
        if (stirCursor >= stirBodies.Count)
        {
            stirCursor = 0;
            stirBodies.Clear();
            foreach (GMPObject obj in GameWorld.syncedObjs.Values)
            {
                if (obj is PhysicalFactoryItem item && item.itemID == BodyItem && item.authority == Lobby.selfPeerID)
                    stirBodies.Add(item);
            }
        }
        // Each tick kicks its share of the bodies, so every one is kicked stirRate times a second.
        stirKicksOwed += stirBodies.Count * stirRate * delta;
        while (stirKicksOwed >= 1 && stirCursor < stirBodies.Count)
        {
            PhysicalFactoryItem body = stirBodies[stirCursor];
            stirCursor++;
            stirKicksOwed -= 1;
            if (!IsInstanceValid(body) || body.IsQueuedForDeletion())
                continue;
            Vector3 velocityChange = new Vector3(Random.Shared.NextSingle() * 3 - 1.5f, 2 + Random.Shared.NextSingle() * 1.5f, Random.Shared.NextSingle() * 3 - 1.5f);
            body.ApplyCentralImpulse(velocityChange * body.GetMass());
        }
        if (stirCursor >= stirBodies.Count)
            stirKicksOwed = 0;
    }
}
