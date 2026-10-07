#if TOOLS
using Godot;
using System.Collections.Generic;

/// <summary>
/// Draws a <see cref="Structure"/>'s ports in the editor, exactly as the game computes them
/// (<see cref="StructurePorts"/>): each port's rectangle of cell faces, a cross where items connect, and an arrow
/// pointing in (green, inputs) or out (orange, outputs), with a line to the volume or spawn node a declared port
/// links to. Belt ends are drawn too, since they're ports as well, and the structure's extra display arrows
/// (<see cref="Structure.arrows"/>). Drawn on top, so ports inside hoppers or
/// behind walls stay visible. Registered by <see cref="StructureSnapPlugin"/>, which also redraws a structure when
/// its properties are edited.
/// </summary>
[Tool]
public partial class StructurePortGizmo : EditorNode3DGizmoPlugin
{
    const float ArrowLength = 1.1f;
    const float HeadLength = 0.3f;
    const float CrossSize = 0.15f;
    static readonly StringName ArrowsProp = "arrows";
    static readonly StringName PortsProp = "ports";
    static readonly StringName PlacementOffsetProp = "placementOffset";

    public StructurePortGizmo()
    {
        CreateMaterial("input", FlowArrowMesh.InputColor with { A = 1f }, false, true);
        CreateMaterial("output", FlowArrowMesh.OutputColor with { A = 1f }, false, true);
    }

    public override string _GetGizmoName() => "Structure Ports";

    public override bool _HasGizmo(Node3D forNode3D) => StructureSnapPlugin.IsStructure(forNode3D);

    public override void _Redraw(EditorNode3DGizmo gizmo)
    {
        gizmo.Clear();
        Node3D node = gizmo.GetNode3D();
        var input = new List<Vector3>();
        var output = new List<Vector3>();
        foreach (PortShape port in StructurePorts.Collect(node))
        {
            List<Vector3> lines = port.kind == PortKind.Input ? input : output;
            Vector3 a = port.across * port.halfSize.X, u = port.up * port.halfSize.Y, c = port.centre;
            Line(lines, c - a - u, c + a - u);
            Line(lines, c + a - u, c + a + u);
            Line(lines, c + a + u, c - a + u);
            Line(lines, c - a + u, c - a - u);
            Line(lines, port.point - port.across * CrossSize, port.point + port.across * CrossSize);
            Line(lines, port.point - port.up * CrossSize, port.point + port.up * CrossSize);
            Vector3 outside = port.point + (Vector3)port.normal * ArrowLength;
            if (port.kind == PortKind.Input)
                Arrow(lines, outside, port.point, port.across);
            else
                Arrow(lines, port.point, outside, port.across);
        }
        // A line from each declared port to the volume it reads items from or the spawn it puts them at.
        Vector3 offset = node.Get(PlacementOffsetProp).AsVector3();
        Transform3D toLocal = node.GlobalTransform.AffineInverse();
        foreach (Variant v in node.Get(PortsProp).AsGodotArray())
        {
            if (v.As<StructurePort>() is not StructurePort port) continue;
            NodePath link = port.kind == PortKind.Input ? port.volume : port.spawn;
            if (link?.IsEmpty != false || node.GetNodeOrNull<Node3D>(link) is not Node3D target) continue;
            Line(port.kind == PortKind.Input ? input : output, StructurePorts.FromDeclared(port, offset).point, toLocal * target.GlobalPosition);
        }
        foreach (Variant v in node.Get(ArrowsProp).AsGodotArray())
        {
            if (v.As<StructureArrow>() is StructureArrow arrow && !arrow.start.IsEqualApprox(arrow.end))
            {
                Vector3 dir = (arrow.end - arrow.start).Normalized();
                Vector3 side = Mathf.Abs(dir.Y) < 0.9f ? dir.Cross(Vector3.Up).Normalized() : Vector3.Right;
                Arrow(arrow.kind == PortKind.Input ? input : output, arrow.start, arrow.end, side);
            }
        }
        if (input.Count > 0) gizmo.AddLines(input.ToArray(), GetMaterial("input", gizmo));
        if (output.Count > 0) gizmo.AddLines(output.ToArray(), GetMaterial("output", gizmo));
    }

    static void Line(List<Vector3> lines, Vector3 a, Vector3 b)
    {
        lines.Add(a);
        lines.Add(b);
    }

    // A shaft from `from` to `to` with a head in the plane of `side`.
    static void Arrow(List<Vector3> lines, Vector3 from, Vector3 to, Vector3 side)
    {
        Vector3 dir = (to - from).Normalized();
        Line(lines, from, to);
        Line(lines, to, to - dir * HeadLength + side * HeadLength * 0.5f);
        Line(lines, to, to - dir * HeadLength - side * HeadLength * 0.5f);
    }
}
#endif
