using Godot;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// BuildGrid: belt lines. Every <see cref="ConveyorBelt"/> of a registered structure is swept into a ribbon of
/// triangles (its carrying surface and the return run under it), and belts whose ends meet are merged into one static
/// Box3D mesh body per connected line. Box3D marks the flat edges inside one mesh so they make no contacts, which is
/// what stops items catching at the joins: separate colliders meeting at a seam each present a face edge there, and
/// the solver can push an item sideways off it (a "ghost" collision).
/// <para>
/// Each triangle carries its belt's speed as a surface material's tangent velocity (world space, since the line body
/// sits at the origin), deduplicated per body. A body holds at most 255 materials, so a line that needs more is split
/// across bodies, which leaves a seam at the split.
/// </para>
/// <para>
/// Lines are rebuilt in <see cref="UpdateBeltLines"/> from <c>_PhysicsProcess</c> (Box3D calls belong there, see
/// <see cref="GameWorld"/>), only those touched by a belt being added, removed or retuned. Every peer builds its own
/// from its own grid, which holds the same structures, so the lines agree.
/// </para>
/// </summary>
public partial class BuildGrid
{
    /// <summary>Group of the belt line bodies, so hits on one can be told apart from level geometry.</summary>
    public const string BeltLineGroup = "belt_line";

    // Ends closer than this (on each axis) meet.
    private const float BeltEndTolerance = 0.01f;
    // Vertices closer than this are one vertex (also passed to Box3D as its weld tolerance).
    private const float BeltWeld = 0.001f;
    private const int MaxBeltMaterials = 255;

    private class BeltEntry
    {
        public ConveyorBelt belt;
        public Structure owner;
        public Vector3I startKey, endKey;
        public BeltLine line;
        public GeometryInstance3D visual;
        public float visualLength;
    }

    private class BeltLine
    {
        public readonly List<BeltEntry> members = new();
        public readonly List<Node3D> bodies = new();
    }

    private static readonly Dictionary<ConveyorBelt, BeltEntry> belts = new();
    // Found once at registration, so leaving the tree needn't search the structure's nodes again. Only structures
    // that have belts are listed.
    private static readonly Dictionary<Structure, List<BeltEntry>> beltsByStructure = new();
    private static readonly Dictionary<Vector3I, List<BeltEntry>> beltEnds = new();
    private static readonly HashSet<BeltEntry> dirtyBelts = new();
    private static readonly List<Node3D> retiredLineBodies = new();
    private static Node3D beltLineRoot;
    private static int beltLineCounter;

    /// <summary>
    /// Adds <paramref name="structure"/>'s belts to the lines (from <see cref="Structure.AfterInit"/>, after its ports,
    /// which settle a self-turning conveyor's form: only that form's belt joins).
    /// </summary>
    public static void RegisterBelts(Structure structure)
    {
        if (!beltsByStructure.ContainsKey(structure))
        {
            AddBelts(structure);
        }
    }

    /// <summary>Takes <paramref name="structure"/>'s belts out of their lines (from <see cref="Structure._ExitTree"/>).</summary>
    public static void UnregisterBelts(Structure structure) => RemoveBelts(structure);

    // Registers the structure's belts (only the current form's, for a conveyor) and returns them, or null if it has none.
    private static List<BeltEntry> AddBelts(Structure structure)
    {
        List<BeltEntry> entries = null;
        ConveyorBelt only = (structure as ConveyorStructure)?.activeBelt;
        foreach (Node n in structure.FindChildren("*", "", true, false))
        {
            if (n is not ConveyorBelt belt || belt.points.Length < 2 || (only != null && belt != only))
            {
                continue;
            }
            var entry = new BeltEntry
            {
                belt = belt,
                owner = structure,
                visual = belt.visual?.IsEmpty == false ? belt.GetNodeOrNull<GeometryInstance3D>(belt.visual) : null,
                visualLength = belt.VisualLength(),
            };
            Transform3D xf = belt.GlobalTransform;
            entry.startKey = EndKey(xf * belt.points[0]);
            entry.endKey = EndKey(xf * belt.points[^1]);
            belts[belt] = entry;
            AddEnd(entry.startKey, entry);
            AddEnd(entry.endKey, entry);
            dirtyBelts.Add(entry);
            (entries ??= new List<BeltEntry>()).Add(entry);
        }
        if (entries != null)
        {
            beltsByStructure[structure] = entries;
        }
        return entries;
    }

    // Unregisters the structure's belts, rebuilding the rest of their lines, and returns them, or null if it had none.
    private static List<BeltEntry> RemoveBelts(Structure structure)
    {
        if (!beltsByStructure.Remove(structure, out List<BeltEntry> entries))
        {
            return null;
        }
        foreach (BeltEntry entry in entries)
        {
            belts.Remove(entry.belt);
            RemoveEnd(entry.startKey, entry);
            RemoveEnd(entry.endKey, entry);
            dirtyBelts.Remove(entry);
            if (entry.line is BeltLine line)
            {
                // The rest of its line is rebuilt without it (possibly as two lines). RetireLine clears every
                // member's line, this one's included, so hold on to it here.
                RetireLine(line);
                foreach (BeltEntry other in line.members)
                {
                    if (other != entry) dirtyBelts.Add(other);
                }
            }
        }
        return entries;
    }

    /// <summary>Rebuilds the line holding <paramref name="belt"/>, after its speed, friction or the like changed.</summary>
    public static void RefreshBelt(ConveyorBelt belt)
    {
        if (belts.TryGetValue(belt, out BeltEntry entry))
        {
            dirtyBelts.Add(entry);
        }
    }

    /// <summary>True for a merged belt line body (a hit on one belongs to the structure in the hit cell, see <see cref="FindStructure(in RayHit)"/>).</summary>
    public static bool IsBeltLine(Node node) => node != null && node.IsInGroup(BeltLineGroup);

    private static Vector3I EndKey(Vector3 p)
    {
        return new Vector3I(Mathf.RoundToInt(p.X / BeltEndTolerance), Mathf.RoundToInt(p.Y / BeltEndTolerance), Mathf.RoundToInt(p.Z / BeltEndTolerance));
    }

    private static void AddEnd(Vector3I key, BeltEntry entry)
    {
        if (!beltEnds.TryGetValue(key, out var list))
        {
            beltEnds[key] = list = new List<BeltEntry>();
        }
        list.Add(entry);
    }

    private static void RemoveEnd(Vector3I key, BeltEntry entry)
    {
        if (beltEnds.TryGetValue(key, out var list))
        {
            list.Remove(entry);
            if (list.Count == 0) beltEnds.Remove(key);
        }
    }

    private static void RetireLine(BeltLine line)
    {
        retiredLineBodies.AddRange(line.bodies);
        line.bodies.Clear();
        foreach (BeltEntry member in line.members)
        {
            if (member.line == line) member.line = null;
        }
    }

    /// <summary>Drops every line (from <see cref="ResetSession"/>); the structures re-register as they spawn.</summary>
    private static void ResetBeltLines()
    {
        belts.Clear();
        beltsByStructure.Clear();
        beltEnds.Clear();
        ResetPorts();
        dirtyBelts.Clear();
        retiredLineBodies.Clear();
        if (GodotObject.IsInstanceValid(beltLineRoot))
        {
            beltLineRoot.QueueFree();
        }
        beltLineRoot = null;
    }

    /// <summary>Rebuilds the lines touched since the last call. Runs from <c>_PhysicsProcess</c>.</summary>
    private static void UpdateBeltLines()
    {
        if (dirtyBelts.Count == 0 && retiredLineBodies.Count == 0)
        {
            return;
        }

        // Every connected group holding a touched belt becomes one new line; the lines they came from are retired.
        var visited = new HashSet<BeltEntry>();
        var groups = new List<List<BeltEntry>>();
        foreach (BeltEntry seed in dirtyBelts.OrderBy(e => e.belt.GetInstanceId()))
        {
            if (!visited.Add(seed))
            {
                continue;
            }
            var group = new List<BeltEntry>();
            var queue = new Queue<BeltEntry>();
            queue.Enqueue(seed);
            while (queue.Count > 0)
            {
                BeltEntry e = queue.Dequeue();
                group.Add(e);
                if (e.line != null) RetireLine(e.line);
                foreach (Vector3I key in new[] { e.startKey, e.endKey })
                {
                    foreach (BeltEntry next in beltEnds[key])
                    {
                        if (visited.Add(next)) queue.Enqueue(next);
                    }
                }
            }
            groups.Add(group);
        }
        dirtyBelts.Clear();

        // Swap within the tick: free the old bodies and add the new ones before the next step.
        foreach (Node3D body in retiredLineBodies)
        {
            if (GodotObject.IsInstanceValid(body)) body.Free();
        }
        retiredLineBodies.Clear();
        foreach (List<BeltEntry> group in groups)
        {
            BuildBeltLine(group);
            PhaseBeltVisuals(group);
        }
    }

    // ---- building a line ----------------------------------------------------

    /// <summary>Triangles, vertices and materials of one line body under construction.</summary>
    private class BeltMeshBuilder
    {
        public readonly List<Vector3> vertices = new();
        public readonly List<int> indices = new();
        public readonly List<byte> triMaterials = new();
        public readonly Godot.Collections.Array<Godot.Collections.Dictionary> materials = new();
        readonly Dictionary<Vector3I, int> vertexIndex = new();
        readonly Dictionary<(float, float, Vector3I, long), int> materialIndex = new();

        public int Vertex(Vector3 p)
        {
            var key = new Vector3I(Mathf.RoundToInt(p.X / BeltWeld), Mathf.RoundToInt(p.Y / BeltWeld), Mathf.RoundToInt(p.Z / BeltWeld));
            if (!vertexIndex.TryGetValue(key, out int i))
            {
                vertexIndex[key] = i = vertices.Count;
                vertices.Add(p);
            }
            return i;
        }

        /// <summary>The material index for these values, or -1 if a new one would pass the limit.</summary>
        public int Material(float friction, float restitution, Vector3 tangent, long userMaterial)
        {
            var key = (friction, restitution, new Vector3I(Mathf.RoundToInt(tangent.X * 1000), Mathf.RoundToInt(tangent.Y * 1000), Mathf.RoundToInt(tangent.Z * 1000)), userMaterial);
            if (materialIndex.TryGetValue(key, out int i)) return i;
            if (materials.Count >= MaxBeltMaterials) return -1;
            materialIndex[key] = i = materials.Count;
            materials.Add(new Godot.Collections.Dictionary
            {
                ["friction"] = friction,
                ["restitution"] = restitution,
                ["rolling_resistance"] = 0f,
                ["tangent_velocity"] = tangent,
                ["user_material_id"] = userMaterial,
                ["custom_color"] = 0,
            });
            return i;
        }

        /// <summary>A quad facing <paramref name="facing"/> (Box3D collides on the side its winding faces).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 facing, int material)
        {
            Triangle(a, b, c, facing, material);
            Triangle(a, c, d, facing, material);
        }

        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 facing, int material)
        {
            if ((b - a).Cross(c - a).Dot(facing) < 0) (b, c) = (c, b);
            indices.Add(Vertex(a));
            indices.Add(Vertex(b));
            indices.Add(Vertex(c));
            triMaterials.Add((byte)material);
        }
    }

    private static void BuildBeltLine(List<BeltEntry> group)
    {
        // The group is in walk order (breadth first along the line), so if the materials run out the split falls
        // between neighbouring belts rather than scattering pieces across bodies.
        var line = new BeltLine();
        var builder = new BeltMeshBuilder();
        foreach (BeltEntry entry in group)
        {
            if (!AddBeltRibbon(builder, entry))
            {
                // Out of materials: finish this body and start the next with this belt.
                FinishBeltBody(line, builder);
                builder = new BeltMeshBuilder();
                AddBeltRibbon(builder, entry);
            }
            line.members.Add(entry);
            entry.line = line;
        }
        FinishBeltBody(line, builder);
    }

    private static readonly StringName BeltLengthParam = "belt_length";
    private static readonly StringName BeltOffsetParam = "belt_offset";
    private static readonly StringName BeltLoopLengthParam = "belt_loop_length";
    private static readonly StringName BeltJoinEntryParam = "belt_join_entry";
    private static readonly StringName BeltJoinExitParam = "belt_join_exit";

    /// <summary>
    /// Tells each belt's visible mesh how it sits in its line (ConveyorBeltLoop.gdshader), so the line is drawn as
    /// one continuous belt: where along the line it starts, so the pattern runs on unbroken from piece to piece;
    /// which of its ends join another belt, where the shader flattens the model's roller wrap; and, for a closed
    /// loop, the loop's length, which the pattern repeats over so the loop has no break. Positions are counted along
    /// the flow from the line's starts (belts no other belt feeds); each belt adds its visible length, a whole number
    /// of rib pitches, so the ribs stay lined up. Purely visual, so peers needn't agree on it.
    /// </summary>
    private static void PhaseBeltVisuals(List<BeltEntry> group)
    {
        var fedFrom = new Dictionary<Vector3I, List<BeltEntry>>();   // start key -> belts starting there
        var ends = new HashSet<Vector3I>();
        foreach (BeltEntry e in group)
        {
            if (!fedFrom.TryGetValue(e.startKey, out var list)) fedFrom[e.startKey] = list = new List<BeltEntry>();
            list.Add(e);
            ends.Add(e.endKey);
        }

        var offsets = new Dictionary<BeltEntry, float>();
        var queue = new Queue<BeltEntry>();
        void Walk(BeltEntry start)
        {
            offsets[start] = 0f;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                BeltEntry e = queue.Dequeue();
                if (!fedFrom.TryGetValue(e.endKey, out var next)) continue;
                foreach (BeltEntry n in next)
                {
                    if (offsets.TryAdd(n, offsets[e] + e.visualLength)) queue.Enqueue(n);
                }
            }
        }
        foreach (BeltEntry e in group)
        {
            if (!ends.Contains(e.startKey) && !offsets.ContainsKey(e)) Walk(e);   // nothing feeds it: a start
        }
        bool hasStart = offsets.Count > 0;
        foreach (BeltEntry e in group)
        {
            if (!offsets.ContainsKey(e)) Walk(e);                                 // left over: part of a loop
        }

        // A simple closed loop: nothing starts it and every belt hands on to exactly one other.
        float loopLength = 0f;
        if (!hasStart && group.All(e => fedFrom.TryGetValue(e.endKey, out var next) && next.Count == 1))
        {
            loopLength = group.Sum(e => e.visualLength);
        }

        foreach (BeltEntry e in group)
        {
            if (GodotObject.IsInstanceValid(e.visual))
            {
                e.visual.SetInstanceShaderParameter(BeltLengthParam, e.visualLength);
                e.visual.SetInstanceShaderParameter(BeltOffsetParam, offsets[e]);
                e.visual.SetInstanceShaderParameter(BeltLoopLengthParam, loopLength);
                e.visual.SetInstanceShaderParameter(BeltJoinEntryParam, beltEnds[e.startKey].Count > 1 ? 1f : 0f);
                e.visual.SetInstanceShaderParameter(BeltJoinExitParam, beltEnds[e.endKey].Count > 1 ? 1f : 0f);
            }
        }
    }

    /// <summary>Adds a belt's carrying surface and return run to <paramref name="builder"/>; false if its materials don't fit.</summary>
    private static bool AddBeltRibbon(BeltMeshBuilder builder, BeltEntry entry)
    {
        ConveyorBelt belt = entry.belt;
        Transform3D xf = belt.GlobalTransform;
        Vector3 up = (xf.Basis * belt.normal).Normalized();
        int n = belt.points.Length;
        var p = new Vector3[n];
        for (int i = 0; i < n; i++) p[i] = xf * belt.points[i];
        var dir = new Vector3[n - 1];
        for (int i = 0; i < n - 1; i++) dir[i] = (p[i + 1] - p[i]).Normalized();

        // Across the belt at each point: square to the path, mitred at the bends. The ends are squared to the grid
        // axis they run along, so neighbouring belts' end edges line up exactly and weld.
        var side = new Vector3[n];
        var normal = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 along = i == 0 ? (Vector3)DominantAxis(dir[0])
                : i == n - 1 ? (Vector3)DominantAxis(dir[n - 2])
                : (dir[i - 1] + dir[i]).Normalized();
            normal[i] = (up - along * up.Dot(along)).Normalized();
            side[i] = along.Cross(normal[i]).Normalized();
        }

        long tag = SurfaceTag(entry.owner);
        int[] top = new int[n - 1], ret = new int[n - 1];
        for (int i = 0; i < n - 1; i++)
        {
            float v = belt.SpeedAlong(dir[i], up);
            top[i] = builder.Material(belt.friction, belt.restitution, dir[i] * v, tag);
            ret[i] = belt.returnDepth > 0f ? builder.Material(belt.friction, belt.restitution, -dir[i] * v, tag) : 0;
            if (top[i] < 0 || ret[i] < 0) return false;
        }

        float hw = belt.width / 2f, rw = belt.returnWidth / 2f;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 segUp = (normal[i] + normal[i + 1]).Normalized();
            builder.Quad(p[i] - side[i] * hw, p[i] + side[i] * hw, p[i + 1] + side[i + 1] * hw, p[i + 1] - side[i + 1] * hw, segUp, top[i]);
            if (belt.returnDepth > 0f)
            {
                Vector3 q0 = p[i] - normal[i] * belt.returnDepth, q1 = p[i + 1] - normal[i + 1] * belt.returnDepth;
                builder.Quad(q0 - side[i] * rw, q0 + side[i] * rw, q1 + side[i + 1] * rw, q1 - side[i + 1] * rw, -segUp, ret[i]);
            }
        }
        return true;
    }

    // The structure's surface material (its first material tag), for whatever reads hit materials.
    private static long SurfaceTag(Structure owner)
    {
        if (owner.tags != null)
        {
            foreach (ItemTags t in owner.tags)
            {
                if (t >= ItemTags.METAL && t <= ItemTags.GLASS) return (long)t;
            }
        }
        return (long)ItemTags.RUBBER;
    }

    private static void FinishBeltBody(BeltLine line, BeltMeshBuilder builder)
    {
        if (builder.indices.Count == 0)
        {
            return;
        }
        if (!GodotObject.IsInstanceValid(beltLineRoot))
        {
            // Bodies must sit under the Box3D world to be simulated.
            beltLineRoot = new Node3D { Name = "BeltLines" };
            GameWorld.b3droot.AddChild(beltLineRoot);
        }
        // Everything is set before it enters the tree: each mesh property set on a live body rebuilds its shape.
        Node3D body = ClassDB.Instantiate("Box3DBody").As<Node3D>();
        body.Name = $"BeltLine{++beltLineCounter}";
        body.Set(Box3DNames.bodyType, (int)BodyTypeEnum.Static);
        body.Set(Box3DNames.shapeType, (int)ShapeTypeEnum.Mesh);
        body.Set(Box3DNames.debugVisualize, false);
        body.Set(Box3DNames.meshWeldTolerance, BeltWeld);
        body.Set(Box3DNames.surfaceMaterials, builder.materials);
        body.Set(Box3DNames.meshVertices, builder.vertices.ToArray());
        body.Set(Box3DNames.meshIndices, builder.indices.ToArray());
        body.Set(Box3DNames.meshMaterials, builder.triMaterials.ToArray());
        body.AddToGroup(BeltLineGroup);
        beltLineRoot.AddChild(body);
        line.bodies.Add(body);
    }
}
