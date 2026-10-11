using Godot;
using System.Collections.Generic;

/// <summary>
/// One port of a structure in its root's own frame: where items connect (<see cref="point"/>, on the face, facing
/// out along <see cref="normal"/>) and the rectangle of cell faces it covers (<see cref="centre"/>, spanning
/// <see cref="halfSize"/> along <see cref="across"/> and <see cref="up"/>).
/// </summary>
public readonly record struct PortShape(PortKind kind, Vector3 point, Vector3I normal, Vector3 centre, Vector3 across, Vector3 up, Vector2 halfSize, bool fromBelt);

/// <summary>
/// A structure's ports (<see cref="StructurePort"/>): those it declares, plus one at each end of each of its belts
/// (the start an input, the end an output; a 2 m belt spans two cells across). Works on the editor's placeholder
/// nodes as well as on live structures, reading only exported properties, so the editor gizmo
/// (addons/structure_snap) shows exactly what the game uses.
/// </summary>
public static class StructurePorts
{
    // Read through Get() so placeholders (the editor) work too.
    private static readonly StringName CellOffsetsProp = "cellOffsets";
    private static readonly StringName PlacementOffsetProp = "placementOffset";
    private static readonly StringName PortsProp = "ports";
    private static readonly StringName PointsProp = "points";
    private static readonly StringName WidthProp = "width";
    private static readonly StringName NormalProp = "normal";
    private static readonly StringName StartIsPortProp = "startIsPort";
    private static readonly StringName EndIsPortProp = "endIsPort";

    private const float Tolerance = 0.01f;

    public static Vector3I Normal(PortFace face) => face switch
    {
        PortFace.Front => new(0, 0, -1),
        PortFace.Back => new(0, 0, 1),
        PortFace.Left => new(-1, 0, 0),
        PortFace.Right => new(1, 0, 0),
        PortFace.Top => new(0, 1, 0),
        _ => new(0, -1, 0),
    };

    // The axes a port's size runs along on a face: across, then up (or along Z, on the top and bottom).
    private static (Vector3I across, Vector3I up) Axes(Vector3I normal) =>
        normal.Y != 0 ? (new(1, 0, 0), new(0, 0, 1))
        : normal.X != 0 ? (new(0, 0, 1), new(0, 1, 0))
        : (new(1, 0, 0), new(0, 1, 0));

    /// <summary>
    /// How far to turn the next structure placed after one with these ports so it carries on from its output, in
    /// quarter turns of positive yaw (the sense of <see cref="BuildGrid.RotateOffset"/>): the one that faces its front
    /// (-Z) the way its outputs face. Null if it has no level output, or outputs facing different ways.
    /// </summary>
    public static int? OutputQuarterTurns(IReadOnlyList<PortShape> ports)
    {
        int? turns = null;
        foreach (PortShape p in ports)
        {
            if (p.kind != PortKind.Output || p.normal.Y != 0) continue;
            int t = p.normal.Z < 0 ? 0 : p.normal.X < 0 ? 1 : p.normal.Z > 0 ? 2 : 3;   // front, left, back, right
            if (turns is int other && other != t) return null;
            turns = t;
        }
        return turns;
    }

    /// <summary>A footprint cell's centre relative to the structure's root.</summary>
    public static Vector3 CellCentre(Vector3I cell, Vector3 placementOffset) => (Vector3)cell * BuildGrid.CellSize - placementOffset;

    /// <summary>The shape of a declared port.</summary>
    public static PortShape FromDeclared(StructurePort port, Vector3 placementOffset)
    {
        Vector3I n = Normal(port.face);
        var (a, b) = Axes(n);
        Vector2I size = port.size.Max(Vector2I.One);
        float half = BuildGrid.CellSize / 2f;
        Vector3 first = CellCentre(port.cell, placementOffset);
        Vector3 centre = first + ((Vector3)a * (size.X - 1) + (Vector3)b * (size.Y - 1)) * half + (Vector3)n * half;
        // A side port connects at belt height in its lowest cell; a top or bottom one in the middle.
        Vector3 point = n.Y != 0 ? centre : centre with { Y = first.Y - half + Structure.BeltTopHeight };
        return new PortShape(port.kind, point, n, centre, a, b, new Vector2(size.X, size.Y) * half, false);
    }

    /// <summary>
    /// The port at one end of a belt: <paramref name="point"/>, the way items move there and the side they ride on
    /// (<see cref="ConveyorBelt.normal"/>), in the root's frame. It spans the belt's width across, and the cell the belt
    /// rides in (a belt runs <see cref="Structure.BeltTopHeight"/> off that cell's floor, wall or ceiling).
    /// </summary>
    public static PortShape FromBeltEnd(Vector3 point, Vector3 flow, Vector3 surfaceNormal, float width, bool output)
    {
        Vector3I n = BuildGrid.DominantAxis(output ? flow : -flow);
        Vector3I side = BuildGrid.DominantAxis(surfaceNormal);
        Vector3I across = BuildGrid.DominantAxis(((Vector3)n).Cross(side)).Abs();
        float half = BuildGrid.CellSize / 2f;
        int cells = Mathf.Max(1, Mathf.RoundToInt(width / BuildGrid.CellSize));
        Vector3 centre = point + (Vector3)side * (half - Structure.BeltTopHeight);
        return new PortShape(output ? PortKind.Output : PortKind.Input, point, n, centre, across, side.Abs(), new Vector2(cells * half, half), true);
    }

    /// <summary>
    /// Every port of <paramref name="structure"/> (a Structure, or its placeholder in the editor). Belts under a
    /// hidden node (a self-turning conveyor's unused forms) are left out unless <paramref name="includeHidden"/>,
    /// which gives every port any of its forms could have, each once.
    /// </summary>
    public static List<PortShape> Collect(Node3D structure, bool includeHidden = false)
    {
        var result = new List<PortShape>();
        Vector3 offset = structure.Get(PlacementOffsetProp).AsVector3();
        foreach (Variant v in structure.Get(PortsProp).AsGodotArray())
        {
            if (v.As<StructurePort>() is StructurePort port)
            {
                result.Add(FromDeclared(port, offset));
            }
        }
        foreach (var (points, normal, width, startIsPort, endIsPort) in Belts(structure, includeHidden))
        {
            if (startIsPort) AddOnce(result, FromBeltEnd(points[0], points[1] - points[0], normal, width, false));
            if (endIsPort) AddOnce(result, FromBeltEnd(points[^1], points[^1] - points[^2], normal, width, true));
        }
        return result;
    }

    // Forms of one structure share some ports (every form of a conveyor lets items out at the same place).
    private static void AddOnce(List<PortShape> ports, PortShape port)
    {
        foreach (PortShape p in ports)
        {
            if (p.kind == port.kind && p.normal == port.normal && p.point.IsEqualApprox(port.point) && p.halfSize.IsEqualApprox(port.halfSize))
            {
                return;
            }
        }
        ports.Add(port);
    }

    /// <summary>Each visible belt's path and surface normal in the structure's frame (and with <paramref name="includeHidden"/>, the hidden ones'), its width, and which of its ends are ports.</summary>
    public static IEnumerable<(Vector3[] points, Vector3 normal, float width, bool startIsPort, bool endIsPort)> Belts(Node3D structure, bool includeHidden = false)
    {
        foreach (Node n in structure.FindChildren("*", "", true, false))
        {
            Vector3[] local;
            Vector3 normal;
            float width;
            bool startIsPort, endIsPort;
            if (n is ConveyorBelt belt)
            {
                (local, normal, width, startIsPort, endIsPort) = (belt.points, belt.normal, belt.width, belt.startIsPort, belt.endIsPort);
            }
            else if (Engine.IsEditorHint() && n is Node3D placeholder && placeholder.Get(WidthProp).VariantType == Variant.Type.Float)
            {
                // In the editor a belt is a placeholder: known by its exported properties.
                local = placeholder.Get(PointsProp).AsVector3Array();
                normal = placeholder.Get(NormalProp).AsVector3();
                width = placeholder.Get(WidthProp).AsSingle();
                startIsPort = placeholder.Get(StartIsPortProp).AsBool();
                endIsPort = placeholder.Get(EndIsPortProp).AsBool();
            }
            else
            {
                continue;
            }
            if (local == null || local.Length < 2)
            {
                continue;
            }
            Node3D node = (Node3D)n;
            Transform3D rel = node.Transform;
            bool hidden = false;
            for (Node p = node.GetParent(); p != structure && p != null; p = p.GetParent())
            {
                if (p is Node3D p3)
                {
                    hidden |= !p3.Visible;
                    rel = p3.Transform * rel;
                }
            }
            if (hidden && !includeHidden)
            {
                continue;
            }
            var points = new Vector3[local.Length];
            for (int i = 0; i < local.Length; i++) points[i] = rel * local[i];
            yield return (points, (rel.Basis * normal).Normalized(), width, startIsPort, endIsPort);
        }
    }

    /// <summary>
    /// The boxes of a spawn volume (a sensor body: its box shapes, or its own box if it has none), each in the body's
    /// frame with its half size and its volume (for picking one at random). Empty for a plain node, which is a point.
    /// </summary>
    public static (Transform3D xf, Vector3 half, float weight)[] SpawnBoxes(Node3D spawn)
    {
        if (!spawn.IsClass("Box3DBody"))
        {
            return [];
        }
        var boxes = new List<(Transform3D, Vector3, float)>();
        foreach (Node child in spawn.GetChildren())
        {
            if (child is Node3D shape && shape.IsClass("Box3DCollisionShape") && shape.Get(Box3DNames.shapeType).AsInt32() == 0)
            {
                Vector3 half = shape.Get(Box3DNames.boxSize).AsVector3() / 2f;
                boxes.Add((shape.Transform, half, half.X * half.Y * half.Z));
            }
        }
        if (boxes.Count == 0)
        {
            Vector3 half = spawn.Get(Box3DNames.boxSize).AsVector3() / 2f;
            if (half.X * half.Y * half.Z > 0) boxes.Add((Transform3D.Identity, half, half.X * half.Y * half.Z));
        }
        return boxes.ToArray();
    }

    /// <summary>
    /// What's wrong with <paramref name="structure"/>'s ports, if anything: a declared port off its footprint or on a
    /// face inside it, or a belt that doesn't end on the outside face of its footprint (centred on whole cells) and
    /// so can't connect to anything; an input's volume that isn't a sensor body, or an output's spawn volume that
    /// isn't a sensor body of boxes; a link that doesn't lead to a node, or is on the wrong kind of port.
    /// </summary>
    public static List<string> Validate(Node3D structure)
    {
        var problems = new List<string>();
        var cells = new HashSet<Vector3I>();
        foreach (Vector3I c in structure.Get(CellOffsetsProp).AsGodotArray<Vector3I>()) cells.Add(c);
        Vector3 offset = structure.Get(PlacementOffsetProp).AsVector3();

        int index = 0;
        foreach (Variant v in structure.Get(PortsProp).AsGodotArray())
        {
            if (v.As<StructurePort>() is not StructurePort port)
            {
                problems.Add($"port {index} is empty");
                index++;
                continue;
            }
            string name = $"port {index} ({port.kind} {port.face})";
            bool hasVolume = port.volume?.IsEmpty == false, hasSpawn = port.spawn?.IsEmpty == false;
            if (port.kind == PortKind.Input && hasSpawn) problems.Add($"{name} is an input but has a spawn");
            if (port.kind == PortKind.Output && hasVolume) problems.Add($"{name} is an output but has a volume");
            if (hasVolume)
            {
                Node volume = structure.GetNodeOrNull(port.volume);
                if (volume == null) problems.Add($"{name}'s volume {port.volume} isn't a node");
                else if (!volume.IsClass("Box3DBody") || !volume.Get(Box3DNames.isSensor).AsBool()) problems.Add($"{name}'s volume {port.volume} isn't a sensor body");
            }
            if (hasSpawn)
            {
                Node spawn = structure.GetNodeOrNull(port.spawn);
                if (spawn is not Node3D spawn3) problems.Add($"{name}'s spawn {port.spawn} isn't a 3D node");
                else if (spawn3.IsClass("Box3DBody"))
                {
                    if (!spawn3.Get(Box3DNames.isSensor).AsBool()) problems.Add($"{name}'s spawn {port.spawn} is a body that isn't a sensor");
                    else if (SpawnBoxes(spawn3).Length == 0) problems.Add($"{name}'s spawn {port.spawn} has no boxes to spawn in");
                    foreach (Node child in spawn3.GetChildren())
                    {
                        if (child.IsClass("Box3DCollisionShape") && child.Get(Box3DNames.shapeType).AsInt32() != 0)
                            problems.Add($"{name}'s spawn {port.spawn} has a shape that isn't a box ({child.Name})");
                    }
                }
            }
            Vector3I n = Normal(port.face);
            var (a, b) = Axes(n);
            for (int i = 0; i < Mathf.Max(1, port.size.X); i++)
            {
                for (int j = 0; j < Mathf.Max(1, port.size.Y); j++)
                {
                    Vector3I c = port.cell + a * i + b * j;
                    if (!cells.Contains(c))
                        problems.Add($"port {index} ({port.kind} {port.face}) covers cell {c}, which isn't in the footprint");
                    else if (cells.Contains(c + n))
                        problems.Add($"port {index} ({port.kind} {port.face}) is on a face of cell {c} inside the footprint (cell {c + n} is part of it too)");
                }
            }
            index++;
        }

        foreach (PortShape p in Collect(structure))
        {
            if (!p.fromBelt)
            {
                continue;
            }
            // Grid coordinates: cell c spans [c, c + 1] on each axis.
            Vector3 g = (p.point + offset) / BuildGrid.CellSize + Vector3.One * 0.5f;
            Vector3 nf = p.normal;
            int axis = p.normal.X != 0 ? 0 : p.normal.Y != 0 ? 1 : 2;
            int acrossAxis = p.across.X != 0 ? 0 : p.across.Y != 0 ? 1 : 2;
            bool cellsEven = Mathf.RoundToInt(p.halfSize.X * 2 / BuildGrid.CellSize) % 2 == 0;
            float acrossFrac = Mathf.PosMod(g[acrossAxis], 1f);
            Vector3I inside = (Vector3I)(g - nf * 0.5f).Floor();
            string where = $"belt {(p.kind == PortKind.Input ? "start" : "end")} at {p.point}";
            if (Mathf.Abs(g[axis] - Mathf.Round(g[axis])) > Tolerance)
                problems.Add($"{where} isn't on a cell face");
            else if (cellsEven ? Mathf.Min(acrossFrac, 1 - acrossFrac) > Tolerance : Mathf.Abs(acrossFrac - 0.5f) > Tolerance)
                problems.Add($"{where} isn't centred on whole cells across the belt");
            else if (!cells.Contains(inside) || cells.Contains(inside + p.normal))
                problems.Add($"{where} isn't on the outside of the footprint");
        }
        return problems;
    }
}
