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

    /// <summary>Host: repairs every broken structure in the level (BrokenStructure.Replace).</summary>
    public void repair_all()
    {
        List<BrokenStructure> broken = new();
        foreach (GMPObject obj in GameWorld.syncedObjs.Values)
        {
            if (obj is BrokenStructure structure && structure.authority == Lobby.selfPeerID)
                broken.Add(structure);
        }
        foreach (BrokenStructure structure in broken)
        {
            structure.Replace();
        }
        LimboConsole.Info($"repair_all: repaired={broken.Count}");
    }

    /// <summary>The item counts spawners read for their limits: the total, then each item id's count.</summary>
    public void item_counts()
    {
        Dictionary<string, int> byItem = new();
        foreach (ItemRecord item in ItemWorld.items.Values)
        {
            byItem[item.itemID] = byItem.GetValueOrDefault(item.itemID, 0) + 1;
        }
        string line = $"item_counts: total={ItemWorld.totalCount} physics={PhysicsWorld.ItemCount()}";
        foreach (var (itemID, counted) in byItem)
        {
            // Running count, then a fresh count of the records to check it against.
            line += $" {itemID}={ItemWorld.CountOf(itemID)}/{counted}";
        }
        LimboConsole.Info(line);
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

    /// <summary>
    /// Testing: the local player steers by (x, y) — x right, y back, so 0 -1 is forward — for seconds, sprinting if
    /// sprint is 1. The reply comes when it's done: where it got to and its speed.
    /// </summary>
    public async void walk(float x, float y, float seconds, int sprint)
    {
        FactoryPlayer player = LocalPlayer();
        if (player == null)
        {
            LimboConsole.Error("walk: no local player.");
            return;
        }
        Vector3 start = player.GlobalPosition;
        player.testWalk = new Vector2(x, y);
        player.testWalkLeft = seconds;
        player.testSprint = sprint != 0;
        await ToSignal(GetTree().CreateTimer(seconds + 0.5f), SceneTreeTimer.SignalName.Timeout);
        Vector3 end = player.GlobalPosition;
        Logging.Log($"walk: moved {start.DistanceTo(end):0.00} m from {start} to {end}, speed now {player.cachedVel.Length():0.00} m/s, on floor {player.IsOnFloor()}", "Test");
    }

    /// <summary>Testing: the local player jumps; the log reports how high its feet got.</summary>
    public async void jump()
    {
        FactoryPlayer player = LocalPlayer();
        if (player == null)
        {
            LimboConsole.Error("jump: no local player.");
            return;
        }
        float startY = player.GlobalPosition.Y;
        player.testJump = true;
        await ToSignal(GetTree().CreateTimer(1.5f), SceneTreeTimer.SignalName.Timeout);
        Logging.Log($"jump: feet rose {player.testPeakY - startY:0.00} m, back on floor {player.IsOnFloor()} at y {player.GlobalPosition.Y:0.00}", "Test");
    }

    /// <summary>Testing the build grid: whether the level blocks cell (x, y, z), and what an overlap of that cell's box finds.</summary>
    public void build_cell(int x, int y, int z)
    {
        Vector3I cell = new Vector3I(x, y, z);
        Vector3 size = Vector3.One * (BuildGrid.CellSize - 2f * BuildGrid.LevelOverlapDepth);
        Overlap overlap = PhysicsWorld.OverlapBox(new Transform3D(Basis.Identity, BuildGrid.CellToWorld(cell)), size, ~GameWorld.QueryHiddenLayer & ~PhysicsWorld.ItemLayer);
        string bodies = "";
        foreach (Node3D body in overlap.bodies)
        {
            bodies += $" {body.Name}(static level {PlacementProbe.IsStaticLevelCollider(body)})";
        }
        LimboConsole.Info($"build_cell: {cell} centre {BuildGrid.CellToWorld(cell)} blocked={BuildGrid.IsLevelBlocked(cell)} overlaps:{bodies}");
    }

    /// <summary>Testing the build grid: whether blueprint can be placed anchored at cell (x, y, z), and what the level check overlaps.</summary>
    public void build_probe(string blueprintItemID, int x, int y, int z)
    {
        if (ItemInfo.Fetch(blueprintItemID) is not BlueprintItem blueprint)
        {
            LimboConsole.Error($"build_probe: {blueprintItemID} isn't a blueprint.");
            return;
        }
        PlacementProbe probe = PlacementProbe.Create(blueprint.StructureScene(false), false);
        probe.MoveTo(new Vector3I(x, y, z), 0);
        bool allowed = BuildGrid.IsPlacementAllowed(probe, false, out List<Node> overlaps);
        string hits = "";
        foreach (Node hit in overlaps)
        {
            hits += " " + hit.GetPath();
        }
        // Which of its shapes do it (the probe sits at the pose, so each shape's GlobalTransform is where it's tested).
        string blamed = "";
        foreach (Node node in probe.structure.FindChildren("*", "GMPShape", true, false))
        {
            Node3D shape = (Node3D)node;
            if (shape.Get("is_sensor").AsBool() || shape.Get("shape").AsInt32() == 5)
                continue;
            foreach (Node3D other in PhysicsWorld.OverlapShape(shape, shape.GlobalTransform, ~GameWorld.QueryHiddenLayer & ~PhysicsWorld.ItemLayer, 0.1f).bodies)
            {
                if (PlacementProbe.IsStaticLevelCollider(other))
                    blamed += $" {probe.structure.GetPathTo(shape)}(shape {shape.Get("shape")} at {shape.GlobalPosition} scale {shape.GlobalBasis.Scale} size {shape.Get("size")})";
            }
        }
        // And which of its cells the grid refuses, and why.
        string refused = "";
        foreach (Vector3I cell in BuildGrid.FootprintCells(probe.anchor, probe.structure.cells.Keys, 0))
        {
            if (BuildGrid.GetStructureAt(cell) != null)
                refused += $" {cell}:taken";
            else if (BuildGrid.IsNoBuild(cell))
                refused += $" {cell}:nobuild";
            else if (BuildGrid.IsLevelBlocked(cell))
                refused += $" {cell}:level";
        }
        LimboConsole.Info($"build_probe: {blueprintItemID} at ({x},{y},{z}) allowed={allowed} level overlaps:{hits} | by:{blamed} | cells refused:{refused}");
        probe.Free();
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

    // ---- loose items for performance tests (host only) ----
    // spawn_bodies drops scrap pieces in a stack of layers in front of the host's player (or in the test arena); more
    // calls add layers on top. stir keeps them awake with random kicks. Both run from _PhysicsProcess.

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

    /// <summary>Each item is kicked this many times a second on average (at random) while stirring; 0 is off.</summary>
    float stirRate;
    double stirKicksOwed;

    /// <summary>Host: spawns count scrap pieces in front of the local player (in layers of 20x20, more calls stack on top).</summary>
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
    /// The scrap pieces in this instance: how many, how many bodies are awake in the physics world (on the host: the
    /// simulated ones), the lowest and highest, a position sum to compare peers with, and a count per 2 m of height.
    /// </summary>
    public void bodies()
    {
        int count = 0;
        float lowest = float.MaxValue;
        float highest = float.MinValue;
        SortedDictionary<int, int> perBand = new();
        foreach (ItemRecord item in ItemWorld.items.Values)
        {
            if (item.itemID != BodyItem)
                continue;
            count++;
            float y = item.position.Y;
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
        Godot.Collections.Dictionary stats = PhysicsWorld.Stats();
        // The position sum (every item) compares two peers' copies: equal to a few mm once followers have caught up.
        LimboConsole.Info($"bodies: count={count} awake={stats["awake"]} followers={stats["followers"]} y={lowest:0.0}..{highest:0.0} " +
            $"sum={stats["position_sum"].AsDouble():0.00} reactions={TagInteractions.reactions} |{bands}");
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
            if (GameWorld.syncedObjs.TryGetValue(id, out GMPObject obj) && obj is GMPOBody arena)
                LimboConsole.Info($"test_arena: body at {arena.GlobalPosition}, type {arena.bodyType}");
            else
                LimboConsole.Error("test_arena: the arena didn't spawn");
        }
        Vector3 spot = ArenaPosition + new Vector3(0, 1.5f, -13);
        LimboConsole.Info($"test_arena: at {ArenaPosition}; stand at tp {spot.X} {spot.Y} {spot.Z}");
    }

    // ---- followstats: how followers are updated and moved here (a client) ----
    double followStatsLeft;
    readonly Dictionary<GMPOBody, (Vector3 last, float delta, float before)> followMotion = new();
    long frozenTicks, movingTicks;
    int followStatsTicks;
    double inViewStates, inViewBodyTicks, outViewStates, outViewBodyTicks;
    readonly Dictionary<GMPOBody, int> statesAtStart = new();

    /// <summary>
    /// Over the next n seconds, for every body following here: states received per second, in the local camera's view
    /// and out of it (the send priority's view bonus), and "frozen" ticks, where a moving body didn't move between two
    /// ticks where it did (the stutter of a body placed only every other tick).
    /// </summary>
    public void followstats(float seconds)
    {
        followStatsLeft = seconds;
        followMotion.Clear();
        statesAtStart.Clear();
        frozenTicks = 0;
        movingTicks = 0;
        followStatsTicks = 0;
        inViewStates = 0;
        inViewBodyTicks = 0;
        outViewStates = 0;
        outViewBodyTicks = 0;
        LimboConsole.Info($"followstats: measuring for {seconds} s");
    }

    void FollowStatsTick(double delta)
    {
        if (followStatsLeft <= 0)
            return;
        Camera3D camera = GetViewport().GetCamera3D();
        foreach (GMPOBody body in GMPOBody.Followers)
        {
            if (!IsInstanceValid(body))
                continue;
            if (!statesAtStart.ContainsKey(body))
                statesAtStart[body] = body.statesReceived;
            Vector3 now = body.Transform.Origin;
            if (followMotion.TryGetValue(body, out var motion))
            {
                float moved = now.DistanceTo(motion.last);
                // The tick before this one didn't move, but the ones either side did: it froze for a tick.
                if (motion.delta < 1e-6f && motion.before > 1e-4f && moved > 1e-4f)
                    frozenTicks++;
                if (moved > 1e-4f)
                    movingTicks++;
                followMotion[body] = (now, moved, motion.delta);
            }
            else
            {
                followMotion[body] = (now, 1, 1);
            }
            bool inView = camera != null && camera.IsPositionInFrustum(now);
            if (inView)
                inViewBodyTicks++;
            else
                outViewBodyTicks++;
        }
        followStatsTicks++;
        followStatsLeft -= delta;
        if (followStatsLeft > 0)
            return;
        // States received over the window, split by where each body was at the end (most stay put either side).
        foreach ((GMPOBody body, int start) in statesAtStart)
        {
            if (!IsInstanceValid(body))
                continue;
            int received = body.statesReceived - start;
            if (camera != null && camera.IsPositionInFrustum(body.Transform.Origin))
                inViewStates += received;
            else
                outViewStates += received;
        }
        double seconds = (double)followStatsTicks / Engine.PhysicsTicksPerSecond;
        double inViewBodies = inViewBodyTicks / followStatsTicks;
        double outViewBodies = outViewBodyTicks / followStatsTicks;
        double inViewHz = inViewBodies > 0 ? inViewStates / seconds / inViewBodies : 0;
        double outViewHz = outViewBodies > 0 ? outViewStates / seconds / outViewBodies : 0;
        double frozenPercent = movingTicks > 0 ? 100.0 * frozenTicks / movingTicks : 0;
        // Logged (not just the console) as it finishes after the command has replied.
        Logging.Log($"followstats: {seconds:0.0} s | in view {inViewBodies:0} bodies at {inViewHz:0.0} states/s each | out of view {outViewBodies:0} at {outViewHz:0.0} states/s | frozen ticks {frozenTicks} of {movingTicks} moving ({frozenPercent:0.00}%)", "Test");
    }

    /// <summary>Host: makes the first n scrap pieces MAGNETIC (instead of METAL, FERROUS), to test tag reactions.</summary>
    public void magnetize(int n)
    {
        if (!Lobby.isHost)
        {
            LimboConsole.Error("magnetize: only the host (it runs the reactions).");
            return;
        }
        int done = 0;
        foreach (ItemRecord item in ItemWorld.items.Values)
        {
            if (done >= n)
                break;
            if (item.itemID == BodyItem)
            {
                item.tags.Clear();
                item.tags.Add(ItemTags.MAGNETIC);
                ItemWorld.TagsChanged(item);
                done++;
            }
        }
        LimboConsole.Info($"magnetize: {done} bodies are MAGNETIC");
    }

    /// <summary>Host: despawns every scrap piece of the kind spawn_bodies makes.</summary>
    public void clear_bodies()
    {
        if (!Lobby.isHost)
        {
            LimboConsole.Error("clear_bodies: only the host.");
            return;
        }
        List<ulong> ids = new();
        foreach (ItemRecord item in ItemWorld.items.Values)
        {
            if (item.itemID == BodyItem)
                ids.Add(item.id);
        }
        foreach (ulong id in ids)
        {
            ItemWorld.Despawn(id);
        }
        bodiesPlaced = 0;
        bodiesToSpawn = 0;
        LimboConsole.Info($"clear_bodies: {ids.Count} despawned");
    }

    /// <summary>Host: kicks the items this many times a second each (on average), keeping them awake and moving; 0 stops.</summary>
    public void stir(float kicksPerSecond)
    {
        stirRate = kicksPerSecond;
        LimboConsole.Info($"stir: {kicksPerSecond} kicks per body per second");
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!GameWorld.started || GetTree().Paused)
            return;
        FloodTick();
        FollowStatsTick(delta);
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
        if (stirRate <= 0 || !Lobby.isHost)
            return;
        // Each tick kicks its share, picked at random, so every item is kicked stirRate times a second on average.
        stirKicksOwed += ItemWorld.totalCount * stirRate * delta;
        int kicks = (int)stirKicksOwed;
        stirKicksOwed -= kicks;
        PhysicsWorld.KickRandomItems(kicks);
    }
}
