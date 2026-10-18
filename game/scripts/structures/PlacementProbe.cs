using Godot;
using System.Collections.Generic;

/// <summary>
/// A copy of a structure scene used to ask the physics world whether the structure's colliders would overlap the
/// level's static colliders at a candidate cell (<see cref="LevelOverlaps"/>), and which loose items they'd shut in
/// (<see cref="ItemOverlaps"/>). Placed structures, items, players and every other synced object don't block it:
/// structures keep out of each other through the grid's cells, and the host lifts the items it overlaps out of the way
/// when it builds.
/// <para>
/// The copy's bodies are switched off (it affects nothing and nothing finds it); each of its solid GMPShapes is tested
/// with an overlap query where it would be, so a result is ready straight after <see cref="MoveTo"/>. Shapes are tested
/// <see cref="Margin"/> smaller on every side, so that resting flush on a floor or against a neighbour doesn't count as
/// an overlap (a collider has to reach that far into the level to block it). Used by
/// the local <see cref="BlueprintGhost"/> (visible, as the preview) and by the host to validate a place request (hidden).
/// </para>
/// </summary>
public class PlacementProbe
{
    const string ProbeGroup = "placement_probe";

    public Structure structure { get; }
    public Vector3I anchor { get; private set; }
    public int quarterTurns { get; private set; }
    /// <summary>Overlap queries answer at once, so the probe is always ready (kept for the callers that wait for it).</summary>
    public bool settled => true;

    /// <summary>The structure's solid shapes and where each sits relative to its root.</summary>
    readonly List<(Node3D shape, Transform3D relative)> shapes = new();
    /// <summary>Where the shapes are tested: the placement pose.</summary>
    Transform3D testPose;

    static readonly StringName enabledName = "enabled";
    static readonly StringName isSensorName = "is_sensor";
    static readonly StringName shapeName = "shape";
    /// <summary>GMPShape.shape of a mesh shape.</summary>
    const int MeshShape = 5;
    /// <summary>
    /// The probe's shapes are tested this much smaller on every side (m): a model that dips a few centimetres into the
    /// floor, or sits flush against a wall, still places.
    /// </summary>
    const float Margin = 0.1f;
    static readonly StringName bodyTypeName = "body_type";

    PlacementProbe(Structure structure)
    {
        this.structure = structure;
    }

    /// <summary>Instantiates <paramref name="scene"/> as a probe, parked until the first <see cref="MoveTo"/>.</summary>
    public static PlacementProbe Create(PackedScene scene, bool visible)
    {
        if (scene.Instantiate() is not Structure structure)
        {
            Logging.Error($"{scene.ResourcePath} has no Structure root; structures must be a GMPBody with a Structure script", "PlacementProbe");
            return null;
        }
        PlacementProbe probe = new PlacementProbe(structure);
        structure.Name = "PlacementProbe_" + structure.Name;
        structure.AddToGroup(ProbeGroup);
        structure.Visible = visible;
        probe.CollectShapes(structure, Transform3D.Identity);
        // No bodies at all: the probe must not push anything or be found by queries.
        probe.DisableBodies(structure);
        structure.Position = new Vector3(0, -10000, 0);
        GameWorld.physics.AddChild(structure);
        return probe;
    }

    void CollectShapes(Node node, Transform3D relative)
    {
        // Sensor shapes are machine triggers, not solid: they don't take part in placement. Nor do mesh shapes: a mesh
        // is a surface, which an overlap test can only approximate by the box around it, and that blocks far too much
        // (a chimney's box reaching the ceiling). The grid's cells keep structures out of the level anyway.
        if (node.IsClass("GMPShape") && !node.Get(isSensorName).AsBool() && node.Get(shapeName).AsInt32() != MeshShape)
        {
            shapes.Add(((Node3D)node, relative));
        }
        foreach (Node child in node.GetChildren())
        {
            if (child is Node3D c)
            {
                CollectShapes(c, relative * c.Transform);
            }
        }
    }

    void DisableBodies(Node node)
    {
        if (node.IsClass("GMPBody"))
        {
            node.Set(enabledName, false);
        }
        foreach (Node child in node.GetChildren())
        {
            DisableBodies(child);
        }
    }

    /// <summary>Moves the probe (and its preview) to the placement pose.</summary>
    public void MoveTo(Vector3I anchor, int quarterTurns)
    {
        this.anchor = anchor;
        this.quarterTurns = quarterTurns;
        Transform3D root = Structure.PlacementTransform(anchor, quarterTurns);
        structure.GlobalTransform = root;
        testPose = root;
    }

    /// <summary>The level's static colliders the probe overlaps where it is.</summary>
    public List<Node> LevelOverlaps()
    {
        List<Node> hits = new();
        foreach ((Node3D shape, Transform3D relative) in shapes)
        {
            foreach (Node3D other in PhysicsWorld.OverlapShape(shape, testPose * relative, ~GameWorld.QueryHiddenLayer & ~PhysicsWorld.ItemLayer, Margin).bodies)
            {
                if (IsStaticLevelCollider(other) && !hits.Contains(other))
                {
                    hits.Add(other);
                }
            }
        }
        return hits;
    }

    /// <summary>The loose items the probe overlaps where it is: they'd be stuck inside the structure.</summary>
    public HashSet<ulong> ItemOverlaps()
    {
        HashSet<ulong> items = new();
        foreach ((Node3D shape, Transform3D relative) in shapes)
        {
            foreach (long item in PhysicsWorld.OverlapShape(shape, testPose * relative, PhysicsWorld.ItemLayer).items)
            {
                items.Add((ulong)item);
            }
        }
        return items;
    }

    public void Free()
    {
        if (GodotObject.IsInstanceValid(structure))
        {
            structure.QueueFree();
        }
    }

    /// <summary>
    /// A static GMPBody that isn't part of a synced object (placed structures, players...) or a probe: the level geometry
    /// structures can't be built into. The build grid's cell fill uses it too (BuildGrid.CellFill.cs).
    /// </summary>
    public static bool IsStaticLevelCollider(Node hit)
    {
        if (hit == null || !hit.IsClass("GMPBody"))
        {
            return false;
        }
        if (hit.Get(bodyTypeName).AsInt32() != (int)BodyTypeEnum.Static)
        {
            return false;
        }
        for (Node n = hit; n != null; n = n.GetParent())
        {
            if (n is GMPObject || n.IsInGroup(ProbeGroup))
            {
                return false;
            }
        }
        return true;
    }
}
