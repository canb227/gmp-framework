using Godot;
using System.Collections.Generic;

/// <summary>
/// Builds the arrows <see cref="BlueprintGhost"/> floats over a structure's placement preview, as solid 3D arrows in
/// the structure's own frame, all worked out from its ports (<see cref="Structure.localPorts"/>):
/// <list type="bullet">
/// <item>one input and one output joined by a belt (a conveyor, a press), or on a structure that only carries items
/// (a chute): one blue arrow from the input to the output, along the belt or curving round;</item>
/// <item>anything else: green pointing in at each input, orange pointing out of each output;</item>
/// <item>and every structure gets its <see cref="Structure.arrows"/>, coloured by kind.</item>
/// </list>
/// Display only. The materials are shared, and built once.
/// </summary>
public static class FlowArrowMesh
{
    public static readonly Color FlowColor = new(0.25f, 0.55f, 1f, 0.75f);
    public static readonly Color InputColor = new(0.3f, 0.95f, 0.4f, 0.8f);
    public static readonly Color OutputColor = new(1f, 0.55f, 0.15f, 0.8f);

    const float ShaftRadius = 0.09f;
    const float HeadRadius = 0.25f;
    const float HeadLength = 0.45f;
    const int Sides = 12;
    /// <summary>How far a flow arrow stops short of where items get on and off.</summary>
    const float EndInset = 0.4f;
    /// <summary>Height of a flow arrow above the surface items ride on.</summary>
    public const float Hover = 0.55f;
    /// <summary>Length of a port arrow.</summary>
    const float PortArrowLength = 1.1f;
    /// <summary>How far a side port's arrow rides above the belt, where it isn't buried in it.</summary>
    const float PortArrowLift = 0.35f;
    const int CurveSteps = 16;

    static StandardMaterial3D flowMaterial, inputMaterial, outputMaterial;

    /// <summary>The preview's arrows for <paramref name="structure"/>, in its root's frame, or null if it has none.</summary>
    public static Node3D Create(Structure structure)
    {
        var root = new Node3D { Name = "FlowArrows" };
        IReadOnlyList<PortShape> ports = structure.localPorts;
        PortShape? input = null, output = null;
        int inputs = 0, outputs = 0;
        foreach (PortShape port in ports)
        {
            if (port.kind == PortKind.Input) { inputs++; input = port; }
            else { outputs++; output = port; }
        }
        if (inputs == 1 && outputs == 1 && FlowPath(structure, input.Value, output.Value) is List<Vector3> path)
        {
            Add(root, path, Material(FlowColor, ref flowMaterial));
        }
        else
        {
            foreach (PortShape port in ports)
            {
                Vector3 at = port.normal.Y != 0 ? port.point : port.point + Vector3.Up * PortArrowLift;
                Vector3 outside = at + (Vector3)port.normal * PortArrowLength;
                if (port.kind == PortKind.Input)
                    Add(root, [outside, at], Material(InputColor, ref inputMaterial));
                else
                    Add(root, [at, outside], Material(OutputColor, ref outputMaterial));
            }
        }
        if (structure.arrows != null)
        {
            foreach (StructureArrow arrow in structure.arrows)
            {
                if (arrow == null || arrow.start.IsEqualApprox(arrow.end)) continue;
                Add(root, [arrow.start, arrow.end], arrow.kind == PortKind.Input
                    ? Material(InputColor, ref inputMaterial) : Material(OutputColor, ref outputMaterial));
            }
        }
        if (root.GetChildCount() == 0)
        {
            root.Free();
            return null;
        }
        return root;
    }

    static void Add(Node3D root, List<Vector3> path, Material material)
    {
        root.AddChild(new MeshInstance3D
        {
            Name = "Arrow",
            Mesh = Tube(path),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    static StandardMaterial3D Material(Color color, ref StandardMaterial3D cached) => cached ??= new StandardMaterial3D
    {
        AlbedoColor = color,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        EmissionEnabled = true,
        Emission = color * 0.4f,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
    };

    /// <summary>
    /// The blue arrow's centreline from <paramref name="input"/> to <paramref name="output"/>, in the root's frame:
    /// along the belt that runs between them (so it follows turns and slopes), lifted off the surface items ride on;
    /// or, with no such belt, for a structure that only carries items (no port of it reads from a volume or spawns),
    /// a curve leaving the input the way items enter and reaching the output the way they leave. Stops
    /// <see cref="EndInset"/> short of both ends. Null for a machine without a belt between them (a smelter): its
    /// input and output are apart, so they're shown as such.
    /// </summary>
    static List<Vector3> FlowPath(Structure structure, PortShape input, PortShape output)
    {
        var path = new List<Vector3>();
        foreach (var (points, normal, _, _, _) in StructurePorts.Belts(structure))
        {
            if (points[0].IsEqualApprox(input.point) && points[^1].IsEqualApprox(output.point))
            {
                foreach (Vector3 p in points) path.Add(p + normal * Hover);
                return Trim(path, EndInset);
            }
        }
        foreach (StructurePort port in structure.ports)
        {
            if (port != null && (port.volume?.IsEmpty == false || port.spawn?.IsEmpty == false))
            {
                return null;
            }
        }
        // A quadratic curve whose control point is where the entry and exit lines meet (on the line, if they're one).
        Vector3 lift = Vector3.Up * Hover;
        Vector3 a = input.point + lift, b = output.point + lift;
        Vector3 inward = -(Vector3)input.normal;
        Vector3 control = a + inward * (b - a).Dot(inward);
        for (int i = 0; i <= CurveSteps; i++)
        {
            float t = i / (float)CurveSteps;
            path.Add(a.Lerp(control, t).Lerp(control.Lerp(b, t), t));
        }
        return Trim(path, EndInset);
    }

    // The path shortened by `inset` at each end (left whole if that would leave too little of it).
    static List<Vector3> Trim(List<Vector3> path, float inset)
    {
        float total = 0;
        for (int i = 1; i < path.Count; i++) total += path[i].DistanceTo(path[i - 1]);
        if (total < inset * 2 + HeadLength)
        {
            return path;
        }
        var result = new List<Vector3>();
        float walked = 0;
        for (int i = 1; i < path.Count; i++)
        {
            float seg = path[i].DistanceTo(path[i - 1]);
            float from = Mathf.Max(inset, walked), to = Mathf.Min(total - inset, walked + seg);
            if (seg > 0 && to > from)
            {
                if (result.Count == 0) result.Add(path[i - 1].Lerp(path[i], (from - walked) / seg));
                result.Add(path[i - 1].Lerp(path[i], (to - walked) / seg));
            }
            walked += seg;
        }
        return result;
    }

    /// <summary>
    /// A solid arrow along <paramref name="path"/>: a round shaft to <see cref="HeadLength"/> short of the end, then a
    /// cone to the tip at the last point (a shorter head on a path shorter than two heads).
    /// </summary>
    public static ArrayMesh Tube(List<Vector3> path)
    {
        float total = 0;
        for (int i = 1; i < path.Count; i++) total += path[i].DistanceTo(path[i - 1]);
        float headLength = Mathf.Min(HeadLength, total / 2);

        // The shaft's centreline: the path up to the neck.
        var shaft = new List<Vector3> { path[0] };
        float walked = 0, neckAt = total - headLength;
        for (int i = 1; i < path.Count; i++)
        {
            float seg = path[i].DistanceTo(path[i - 1]);
            if (walked + seg >= neckAt)
            {
                shaft.Add(path[i - 1].Lerp(path[i], (neckAt - walked) / seg));
                break;
            }
            walked += seg;
            shaft.Add(path[i]);
        }
        Vector3 neck = shaft[^1], tip = path[^1];

        // Ring frames carried along the shaft (parallel transport), so the tube doesn't twist.
        int n = shaft.Count;
        var tangents = new Vector3[n];
        for (int i = 0; i < n; i++)
        {
            Vector3 t = (i < n - 1 ? shaft[i + 1] - shaft[i] : Vector3.Zero) + (i > 0 ? shaft[i] - shaft[i - 1] : Vector3.Zero);
            tangents[i] = t.Normalized();
        }
        var axes = new Vector3[n];
        axes[0] = (Mathf.Abs(tangents[0].Y) < 0.9f ? Vector3.Up : Vector3.Right).Cross(tangents[0]).Normalized();
        for (int i = 1; i < n; i++)
        {
            axes[i] = (axes[i - 1] - tangents[i] * axes[i - 1].Dot(tangents[i])).Normalized();
        }
        Vector3 Radial(int i, int k)
        {
            float a = Mathf.Tau * k / Sides;
            return axes[i] * Mathf.Cos(a) + tangents[i].Cross(axes[i]) * Mathf.Sin(a);
        }

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        void Tri(Vector3 a, Vector3 na, Vector3 b, Vector3 nb, Vector3 c, Vector3 nc)
        {
            verts.Add(a); normals.Add(na);
            verts.Add(b); normals.Add(nb);
            verts.Add(c); normals.Add(nc);
        }

        for (int i = 0; i < n - 1; i++)
        {
            for (int k = 0; k < Sides; k++)
            {
                Vector3 r00 = Radial(i, k), r01 = Radial(i, k + 1), r10 = Radial(i + 1, k), r11 = Radial(i + 1, k + 1);
                Vector3 p00 = shaft[i] + r00 * ShaftRadius, p01 = shaft[i] + r01 * ShaftRadius;
                Vector3 p10 = shaft[i + 1] + r10 * ShaftRadius, p11 = shaft[i + 1] + r11 * ShaftRadius;
                Tri(p00, r00, p10, r10, p11, r11);
                Tri(p00, r00, p11, r11, p01, r01);
            }
        }
        Vector3 back = -tangents[0], forward = (tip - neck).Normalized();
        float slope = HeadRadius / Mathf.Max(headLength, 0.001f);
        for (int k = 0; k < Sides; k++)
        {
            Vector3 r0 = Radial(0, k), r1 = Radial(0, k + 1);
            Tri(shaft[0], back, shaft[0] + r1 * ShaftRadius, back, shaft[0] + r0 * ShaftRadius, back);         // tail cap
            Vector3 h0 = Radial(n - 1, k), h1 = Radial(n - 1, k + 1);
            Tri(neck + h0 * HeadRadius, -forward, neck + h1 * HeadRadius, -forward, neck, -forward);           // under the head
            Vector3 n0 = (h0 + forward * slope).Normalized(), n1 = (h1 + forward * slope).Normalized();        // cone
            Tri(neck + h0 * HeadRadius, n0, tip, (n0 + n1).Normalized(), neck + h1 * HeadRadius, n1);
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
