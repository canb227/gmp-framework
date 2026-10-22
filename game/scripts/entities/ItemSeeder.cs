using System.Collections.Generic;
using Godot;

/// <summary>
/// Fills a level with loose items when it starts: <see cref="itemsOnBelts"/> dropped onto the conveyors under
/// <see cref="beltsRoot"/> (a random cell of a random belt, just above its surface) and <see cref="itemsInArea"/>
/// scattered through a box <see cref="areaSize"/> centred on this node. Items are picked from <see cref="itemWeights"/>.
/// <para>
/// Host only, and not while a save is restored (the save brings back its own items). It spawns
/// <see cref="itemsPerTick"/> a tick so the start isn't one huge spike.
/// </para>
/// </summary>
public partial class ItemSeeder : Node3D
{
    /// <summary>Item ids to spawn and their relative weights.</summary>
    [Export] public Godot.Collections.Dictionary<string, float> itemWeights = new();
    /// <summary>The node whose child <see cref="Conveyor"/>s get <see cref="itemsOnBelts"/>.</summary>
    [Export] public Node3D beltsRoot;
    [Export] public int itemsOnBelts = 0;
    [Export] public int itemsInArea = 0;
    /// <summary>The box, centred on this node, that <see cref="itemsInArea"/> are scattered through.</summary>
    [Export] public Vector3 areaSize = new Vector3(4, 2, 4);
    [Export] public int itemsPerTick = 50;

    List<Conveyor> belts = new();
    int beltItemsLeft;
    int areaItemsLeft;

    public override void _Ready()
    {
        if (!Lobby.isHost || GameWorld.frozen)
        {
            SetPhysicsProcess(false);
            return;
        }
        beltItemsLeft = itemsOnBelts;
        areaItemsLeft = itemsInArea;
        if (beltsRoot != null)
        {
            foreach (Node child in beltsRoot.GetChildren())
            {
                if (child is Conveyor belt)
                {
                    belts.Add(belt);
                }
            }
        }
        if (belts.Count == 0)
        {
            beltItemsLeft = 0;
        }
    }

    public override void _PhysicsProcess(double delta)
    {
        // A cell a ray misses (a turn's outside corner) is just tried again, up to a few times the batch.
        int spawned = 0;
        int tries = 0;
        while (spawned < itemsPerTick && tries < itemsPerTick * 4 && beltItemsLeft > 0)
        {
            tries++;
            Conveyor belt = belts[System.Random.Shared.Next(belts.Count)];
            if (TryPointOnBelt(belt, out Vector3 point))
            {
                ItemInfo.SpawnInWorld(ItemSpawner.PickWeighted(itemWeights), point, RandomRotation());
                beltItemsLeft--;
                spawned++;
            }
        }
        while (spawned < itemsPerTick && areaItemsLeft > 0)
        {
            Vector3 offset = new Vector3(
                (System.Random.Shared.NextSingle() - 0.5f) * areaSize.X,
                (System.Random.Shared.NextSingle() - 0.5f) * areaSize.Y,
                (System.Random.Shared.NextSingle() - 0.5f) * areaSize.Z
            );
            ItemInfo.SpawnInWorld(ItemSpawner.PickWeighted(itemWeights), GlobalPosition + offset, RandomRotation());
            areaItemsLeft--;
            spawned++;
        }
        if (beltItemsLeft <= 0 && areaItemsLeft <= 0)
        {
            Logging.Log($"Seeded {itemsOnBelts} items on {belts.Count} belts and {itemsInArea} in the area", "ItemSeeder");
            SetPhysicsProcess(false);
        }
    }

    /// <summary>A point just above the belt's surface over one of its cells, found by a ray down through the cell.</summary>
    bool TryPointOnBelt(Conveyor belt, out Vector3 point)
    {
        List<Vector3I> cells = new List<Vector3I>(belt.cells.Keys);
        Vector3I cell = cells[System.Random.Shared.Next(cells.Count)];
        Vector3 jitter = new Vector3(System.Random.Shared.NextSingle() - 0.5f, 0, System.Random.Shared.NextSingle() - 0.5f) * 0.6f;
        Vector3 center = belt.GlobalTransform * (new Vector3(cell.X + 0.5f, cell.Y + 0.5f, cell.Z - 0.5f) + jitter);
        RayHit hit = PhysicsWorld.Raycast(center + Vector3.Up * 1.5f, center + Vector3.Down * 0.6f, ~(GameWorld.QueryHiddenLayer | PhysicsWorld.ItemLayer));
        point = hit.position + Vector3.Up * 0.3f;
        return hit.hit;
    }

    static Vector3 RandomRotation()
    {
        return new Vector3(System.Random.Shared.NextSingle(), System.Random.Shared.NextSingle(), System.Random.Shared.NextSingle()) * Mathf.Tau;
    }
}
