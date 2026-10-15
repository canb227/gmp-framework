using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// A sensor copy of a structure scene, used to ask the Box3D simulation whether the structure's colliders
/// would overlap the level's static colliders at a candidate cell (<see cref="LevelOverlaps"/>). Placed structures,
/// items, players and every other synced object are ignored: structures keep out of each other through the grid's cells.
/// Every <c>Box3DBody</c> in the copy (the root and any child bodies) that isn't already a trigger sensor is turned into a sensor on <see cref="GameWorld.QueryHiddenLayer"/> so it affects nothing and world
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
            // A baked compound can't be a sensor (Box3D falls back to a runtime compound and warns), so the probe's
            // copy builds its compound at runtime.
            body.Set(Box3DNames.bakedCompound, false);
            body.Set(Box3DNames.isSensor, true);
            body.Set(Box3DNames.sensorEvents, true);
            body.Set(Box3DNames.collisionLayer, GameWorld.QueryHiddenLayer);
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
        if (node.IsClass("Box3DBody") && !node.Get(Box3DNames.isSensor).AsBool())
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
        Transform3D root = Structure.PlacementTransform(anchor, quarterTurns);
        root.Basis = root.Basis.Scaled(Vector3.One * SkinScale);
        foreach (var (body, relative) in bodies)
        {
            body.Call(Box3DNames.teleport, [root * relative]);
        }
        movedOnFrame = Engine.GetPhysicsFrames();
    }

    /// <summary>The level's static colliders the probe currently overlaps (only meaningful once <see cref="settled"/>).</summary>
    public List<Node> LevelOverlaps()
    {
        var hits = new List<Node>();
        foreach (var (body, _) in bodies)
        {
            foreach (Node other in body.Call(Box3DNames.getOverlappingBodies).AsGodotArray<Node>())
            {
                if (IsStaticLevelCollider(other))
                {
                    hits.Add(other);
                }
            }
        }
        return hits;
    }

    public void Free()
    {
        if (GodotObject.IsInstanceValid(structure))
        {
            structure.QueueFree();
        }
    }

    /// <summary>
    /// A solid, static Box3DBody that isn't part of a synced object (placed structures, items, players...) or another
    /// probe: the level geometry structures can't be built into. The build grid's cell fill uses it too (BuildGrid.CellFill.cs).
    /// </summary>
    public static bool IsStaticLevelCollider(Node hit)
    {
        if (!hit.IsClass("Box3DBody"))
        {
            return false;
        }
        if (hit.Get(Box3DNames.bodyType).AsInt32() != (int)BodyTypeEnum.Static || hit.Get(Box3DNames.isSensor).AsBool())
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
