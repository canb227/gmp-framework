using Godot;
using System;
using System.Collections.Generic;

/// <summary>What a candidate placement's colliders would overlap. <see cref="BuildGrid.placementBlockers"/> picks which of these block.</summary>
[Flags]
public enum PlacementBlockers
{
    None = 0,
    /// <summary>Anything not part of a GMPObject: level walls, floors and props.</summary>
    LevelGeometry = 1 << 0,
    /// <summary>PhysicalFactoryItems authored as dynamic bodies (dropped items).</summary>
    DynamicItems = 1 << 1,
    /// <summary>Structures, and PhysicalFactoryItems authored as static or kinematic.</summary>
    StaticObjects = 1 << 2,
    Players = 1 << 3,
    /// <summary>Any other synced object.</summary>
    OtherSynced = 1 << 4,
}

/// <summary>
/// A sensor copy of a structure scene, used to ask the Box3D simulation what the structure's colliders
/// would overlap at a candidate cell. Every <c>Box3DBody</c> in the copy (the root and any child bodies)
/// that isn't already a trigger sensor is turned into a sensor on <see cref="GameWorld.QueryHiddenLayer"/> so it affects nothing and world
/// raycasts pass through it. Used by the local <see cref="BlueprintGhost"/> (visible, as the preview) and
/// by the host to validate a place request (hidden).
/// <para>
/// Box3D only updates sensor overlaps when it steps, so after <see cref="MoveTo"/> the result is valid
/// once <see cref="settled"/>. Colliders are shrunk by <see cref="SkinScale"/> so that resting flush on a
/// floor or against a neighbour doesn't count as an overlap.
/// </para>
/// </summary>
public class PlacementProbe
{
    const string ProbeGroup = "placement_probe";
    /// <summary>Uniform shrink applied to the probe's colliders (about the structure root).</summary>
    const float SkinScale = 0.98f;
    /// <summary>Physics ticks after a move before overlaps reflect the new pose.</summary>
    const ulong SettleTicks = 2;

    public Structure structure { get; }
    public Vector3I anchor { get; private set; }
    public int quarterTurns { get; private set; }
    public bool settled => Engine.GetPhysicsFrames() >= movedOnFrame + SettleTicks;

    readonly List<(Node3D body, Transform3D relative)> bodies = new();
    ulong movedOnFrame;

    PlacementProbe(Structure structure)
    {
        this.structure = structure;
    }

    /// <summary>Instantiates <paramref name="scene"/> as a probe inside the physics world, parked until the first <see cref="MoveTo"/>.</summary>
    public static PlacementProbe Create(PackedScene scene, bool visible)
    {
        if (scene.Instantiate() is not Structure structure)
        {
            Logging.Error($"{scene.ResourcePath} has no Structure root; structures must be a Box3DBody with a Structure script", "PlacementProbe");
            return null;
        }
        var probe = new PlacementProbe(structure);
        structure.Name = "PlacementProbe_" + structure.Name;
        structure.AddToGroup(ProbeGroup);
        structure.Visible = visible;
        probe.CollectBodies(structure, Transform3D.Identity);
        foreach (var (body, _) in probe.bodies)
        {
            body.Set("is_sensor", true);
            body.Set("sensor_events", true);
            body.Set("collision_layer", GameWorld.QueryHiddenLayer);
        }
        // Box3D bodies take their pose on entering the tree; park it far away until MoveTo.
        structure.Position = new Vector3(0, -10000, 0);
        GameWorld.b3droot.AddChild(structure);
        probe.movedOnFrame = Engine.GetPhysicsFrames();
        return probe;
    }

    void CollectBodies(Node node, Transform3D relative)
    {
        // Bodies authored as sensors are machine triggers, not solid: they don't take part in placement.
        if (node.IsClass("Box3DBody") && !node.Get("is_sensor").AsBool())
        {
            bodies.Add(((Node3D)node, relative));
        }
        foreach (Node child in node.GetChildren())
        {
            if (child is Node3D c)
            {
                CollectBodies(c, relative * c.Transform);
            }
        }
    }

    /// <summary>Moves every body to the placement pose. Moving the node alone doesn't move a Box3D body, so each is teleported.</summary>
    public void MoveTo(Vector3I anchor, int quarterTurns)
    {
        this.anchor = anchor;
        this.quarterTurns = quarterTurns;
        Transform3D root = structure.PlacementTransform(anchor, quarterTurns);
        root.Basis = root.Basis.Scaled(Vector3.One * SkinScale);
        foreach (var (body, relative) in bodies)
        {
            body.Call("teleport", [root * relative]);
        }
        movedOnFrame = Engine.GetPhysicsFrames();
    }

    /// <summary>The categories of body the probe currently overlaps (only meaningful once <see cref="settled"/>).</summary>
    public PlacementBlockers Overlaps()
    {
        PlacementBlockers hits = PlacementBlockers.None;
        foreach (var (body, _) in bodies)
        {
            foreach (Node other in body.Call("get_overlapping_bodies").AsGodotArray<Node>())
            {
                hits |= Classify(other);
            }
        }
        return hits;
    }

    /// <summary>"path (category)" for every overlapped body in <paramref name="categories"/>, for logs.</summary>
    public string DescribeOverlaps(PlacementBlockers categories)
    {
        var parts = new List<string>();
        foreach (var (body, _) in bodies)
        {
            foreach (Node other in body.Call("get_overlapping_bodies").AsGodotArray<Node>())
            {
                PlacementBlockers c = Classify(other);
                if ((c & categories) != 0)
                {
                    parts.Add($"{other.GetPath()} ({c})");
                }
            }
        }
        return string.Join(", ", parts);
    }

    public void Free()
    {
        if (GodotObject.IsInstanceValid(structure))
        {
            structure.QueueFree();
        }
    }

    // Walk up to the nearest GMPObject to decide what was hit.
    static PlacementBlockers Classify(Node hit)
    {
        for (Node n = hit; n != null; n = n.GetParent())
        {
            if (n.IsInGroup(ProbeGroup))
            {
                return PlacementBlockers.None; // another probe (e.g. a pending host check)
            }
            switch (n)
            {
                case Structure:
                    return PlacementBlockers.StaticObjects;
                case PhysicalFactoryItem item:
                    return item.authoredBodyType == BodyTypeEnum.Dynamic ? PlacementBlockers.DynamicItems : PlacementBlockers.StaticObjects;
                case FactoryPlayer:
                    return PlacementBlockers.Players;
                case GMPObject:
                    return PlacementBlockers.OtherSynced;
            }
        }
        return PlacementBlockers.LevelGeometry;
    }
}
