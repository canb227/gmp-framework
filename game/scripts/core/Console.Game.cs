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

    /// <summary>Host: the running count of loose items with one tag, a fresh count of the records to check it against, and every item meter.</summary>
    public void tag_count(string tagName)
    {
        if (!Enum.TryParse(tagName, true, out SurfaceTags tag))
        {
            LimboConsole.Error($"tag_count: no tag {tagName}.");
            return;
        }
        int counted = 0;
        foreach (ItemRecord item in ItemWorld.items.Values)
        {
            if ((item.tags & tag) != 0)
                counted++;
        }
        string line = $"tag_count: {tag}={ItemWorld.CountWithTag(tag)}/{counted}";
        foreach (GMPObject obj in GameWorld.syncedObjs.Values)
        {
            if (obj is ItemCountMeter meter)
                line += $" meter{meter.id}({meter.tag} {meter.count}/{meter.max} over={meter.over})";
        }
        LimboConsole.Info(line);
    }

    /// <summary>
    /// Host, for testing: spawns an item meter at x y z counting tagName up to max. Only the host's copy gets that tag
    /// and max (clients' show the scene's defaults); a real one is placed in a level.
    /// </summary>
    public void spawn_meter(string tagName, int max, float x, float y, float z)
    {
        if (!Lobby.isHost || !Enum.TryParse(tagName, true, out SurfaceTags tag))
        {
            LimboConsole.Error("spawn_meter: host only, and needs a tag name.");
            return;
        }
        ulong id = GameWorld.SpawnScene(GD.Load<PackedScene>("res://game/scenes/entities/ItemCountMeter.tscn"), new Vector3(x, y, z), Vector3.Zero);
        ItemCountMeter meter = (ItemCountMeter)GameWorld.syncedObjs[id];
        meter.tag = tag;
        meter.max = max;
        meter.OverMaxChanged += (on) => LimboConsole.Info($"spawn_meter: meter{id} over={on}");
        LimboConsole.Info($"spawn_meter: id={id} tag={tag} max={max}");
    }

    /// <summary>
    /// Builds a blueprint's structure with its anchor at cell x y z, turned quarterTurns, in its T form if alternate is 1,
    /// through the host's normal placement check. Free: takes no blueprint from the inventory.
    /// </summary>
    public void build(string blueprintItemID, int x, int y, int z, int quarterTurns, int alternate)
    {
        FactoryPlayer player = LocalPlayer();
        if (player == null || ItemInfo.Fetch(blueprintItemID) is not BlueprintItem)
        {
            LimboConsole.Error($"build: needs a running game and a blueprint id ({blueprintItemID}).");
            return;
        }
        RPCManager.RPCTo(Lobby.hostID, BuildGrid.instance, "_RequestPlace", [blueprintItemID, new Vector3I(x, y, z), quarterTurns, alternate == 1, false, player.id]);
        LimboConsole.Info($"build: requested {blueprintItemID} at ({x},{y},{z}) turns={quarterTurns} alternate={alternate}");
    }

    /// <summary>
    /// Host: spawns count items (loose items or tools) in a column above x y z, 0.5 m apart so they land one after another, each up to
    /// spread metres off to the side (else a column of balls stacks into a tower).
    /// </summary>
    public void drop_items(string itemID, int count, float x, float y, float z, float spread)
    {
        if (!Lobby.isHost)
        {
            LimboConsole.Error("drop_items: host only.");
            return;
        }
        for (int i = 0; i < count; i++)
        {
            float dx = (float)GD.RandRange(-spread, spread);
            float dz = (float)GD.RandRange(-spread, spread);
            ItemInfo.SpawnInWorld(itemID, new Vector3(x + dx, y + i * 0.5f, z + dz));
        }
        LimboConsole.Info($"drop_items: spawned={count}");
    }

    /// <summary>How many loose items are inside the world box from x1 y1 z1 to x2 y2 z2.</summary>
    public void count_items(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        Aabb box = new Aabb(new Vector3(x1, y1, z1), new Vector3(x2 - x1, y2 - y1, z2 - z1)).Abs();
        int inside = 0;
        string first = "";
        foreach (ulong id in ItemWorld.items.Keys)
        {
            Vector3 p = PhysicsWorld.BodyPosition(id);
            if (box.HasPoint(p))
            {
                inside++;
                if (inside <= 5)
                {
                    first += $" ({p.X:0.00},{p.Y:0.00},{p.Z:0.00})";
                }
            }
        }
        LimboConsole.Info($"count_items: inside={inside} total={ItemWorld.totalCount} first:{first}");
    }

    /// <summary>How many loose items of each kind are inside the world box from x1 y1 z1 to x2 y2 z2.</summary>
    public void count_kinds(float x1, float y1, float z1, float x2, float y2, float z2)
    {
        Aabb box = new Aabb(new Vector3(x1, y1, z1), new Vector3(x2 - x1, y2 - y1, z2 - z1)).Abs();
        var counts = new SortedDictionary<string, int>();
        foreach (ItemRecord record in ItemWorld.items.Values)
        {
            if (box.HasPoint(PhysicsWorld.BodyPosition(record.id)))
            {
                counts.TryGetValue(record.itemID, out int count);
                counts[record.itemID] = count + 1;
            }
        }
        string line = "";
        foreach (KeyValuePair<string, int> pair in counts)
        {
            line += $" {pair.Key}={pair.Value}";
        }
        LimboConsole.Info($"count_kinds:{line}");
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
        string bodies = "";
        foreach (long id in PhysicsWorld.OverlapBox(new Transform3D(Basis.Identity, BuildGrid.CellToWorld(cell)), size, ~GameWorld.QueryHiddenLayer & ~PhysicsWorld.ItemLayer))
        {
            Node3D body = PhysicsWorld.BodyNode((ulong)id);
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
            foreach (long id in PhysicsWorld.OverlapShape(shape, shape.GlobalTransform, ~GameWorld.QueryHiddenLayer & ~PhysicsWorld.ItemLayer, 0.1f))
            {
                if (PlacementProbe.IsStaticLevelCollider(PhysicsWorld.BodyNode((ulong)id)))
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
            $"sum={stats["position_sum"].AsDouble():0.00} |{bands}");
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

    // ---- hitstats: the physics world's impacts here, to compare host and client ----
    double hitStatsLeft;
    int hitStatsTicks;
    long hitCount, hardHitCount, itemOnItemHits, unnamedSides, taggedSides;
    long localCharacterHits, remoteCharacterHits, outOfOrder;
    double hitSpeedTotal, hitSpeedMax;
    readonly List<ContactHit> hits = new();
    const float HardHitSpeed = 3;

    /// <summary>
    /// Over the next n seconds, the impacts the physics world reports here (PhysicsWorld.Hits): per second, how many of
    /// them were at least 3 m/s, item on item vs item on anything else, average and top speed. Run it on the host and a
    /// client at once: followers collide too, so the numbers should be close.
    /// </summary>
    public void hitstats(float seconds)
    {
        hitStatsLeft = seconds;
        hitStatsTicks = 0;
        hitCount = 0;
        hardHitCount = 0;
        itemOnItemHits = 0;
        unnamedSides = 0;
        taggedSides = 0;
        localCharacterHits = 0;
        outOfOrder = 0;
        remoteCharacterHits = 0;
        hitSpeedTotal = 0;
        hitSpeedMax = 0;
        LimboConsole.Info($"hitstats: measuring for {seconds} s");
    }

    void HitStatsTick(double delta)
    {
        if (hitStatsLeft <= 0)
            return;
        // Every hit, anywhere, at any speed.
        PhysicsWorld.Hits(hits, Vector3.Zero, float.MaxValue, 0);
        foreach (ContactHit hit in hits)
        {
            hitCount++;
            hitSpeedTotal += hit.speed;
            hitSpeedMax = Math.Max(hitSpeedMax, hit.speed);
            if (hit.speed >= HardHitSpeed)
                hardHitCount++;
            if (ItemWorld.Get(hit.idA) != null && ItemWorld.Get(hit.idB) != null)
                itemOnItemHits++;
            // Sides no peer can name (a player's capsule, a body not spawned through GameWorld).
            if (hit.idA == 0)
                unnamedSides++;
            if (hit.idB == 0)
                unnamedSides++;
            if (hit.tagsA != 0)
                taggedSides++;
            if (hit.tagsB != 0)
                taggedSides++;
            // A player landing or running into something (side A), or hit by an item.
            SurfaceTags a = (SurfaceTags)hit.tagsA;
            SurfaceTags b = (SurfaceTags)hit.tagsB;
            // A player is always side A: this should stay 0.
            if ((b & SurfaceTags.PLAYER) != 0 && (a & SurfaceTags.PLAYER) == 0)
                outOfOrder++;
            GMPOCharacter character = null;
            if ((a & SurfaceTags.PLAYER) != 0)
                character = PhysicsWorld.BodyNode(hit.idA) as GMPOCharacter;
            if (character != null && character.authority == Lobby.selfPeerID)
                localCharacterHits++;
            else if (character != null)
                remoteCharacterHits++;
        }
        hitStatsTicks++;
        hitStatsLeft -= delta;
        if (hitStatsLeft > 0)
            return;
        double measured = (double)hitStatsTicks / Engine.PhysicsTicksPerSecond;
        double averageSpeed = 0;
        if (hitCount > 0)
            averageSpeed = hitSpeedTotal / hitCount;
        string role = "client";
        if (Lobby.isHost)
            role = "host";
        // Logged (not just the console) as it finishes after the command has replied.
        Logging.Log($"hitstats: role={role} seconds={measured:0.0} hits_per_s={hitCount / measured:0.0} hard_per_s={hardHitCount / measured:0.0} " +
            $"item_on_item_per_s={itemOnItemHits / measured:0.0} other_per_s={(hitCount - itemOnItemHits) / measured:0.0} " +
            $"unnamed_sides={unnamedSides} tagged_sides={taggedSides} avg_speed={averageSpeed:0.00} max_speed={hitSpeedMax:0.00}", "Test");
        Godot.Collections.Dictionary stats = PhysicsWorld.Stats();
        Logging.Log($"hitstats: role={role} hits_sent_total={stats["hits_sent"]} hits_heard_total={stats["hits_heard"]} " +
            $"local_character_hits={localCharacterHits} remote_character_hits={remoteCharacterHits} out_of_order={outOfOrder}", "Test");
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
        HitStatsTick(delta);
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
