using Godot;
using System.Collections.Generic;

/// <summary>Which way a structure moves items, drawn as an arrow over its placement preview.</summary>
public enum FlowArrow
{
    None,
    /// <summary>Straight toward the front (local -Z), across the whole footprint; tilted to climb a slope toward the front.</summary>
    Straight,
    /// <summary>Enters from the back moving -Z and leaves through the left side (-X).</summary>
    TurnLeft,
    /// <summary>Enters from the back moving -Z and leaves through the right side (+X).</summary>
    TurnRight,
    // Appended, not inserted: scenes store these by number.
    /// <summary>Like <see cref="Straight"/>, but a slope descends toward the front (the downhill conveyor).</summary>
    StraightDown,
}

/// <summary>
/// Builds the flat arrow meshes for <see cref="FlowArrow"/>, in a structure's own frame (front is -Z), for
/// <see cref="BlueprintGhost"/> to float above the preview.
/// </summary>
public static class FlowArrowMesh
{
    const float ShaftWidth = 0.22f;
    const float HeadWidth = 0.6f;
    const float HeadLength = 0.5f;
    /// <summary>Height of the arrow above the belt surface.</summary>
    public const float Hover = 0.55f;

    /// <summary>
    /// The arrow for <paramref name="structure"/>, positioned relative to its root, or null for
    /// <see cref="FlowArrow.None"/>. It spans the footprint along the flow and floats <see cref="Hover"/> above the belt.
    /// </summary>
    public static MeshInstance3D Create(Structure structure, Material material)
    {
        if (structure.flowArrow == FlowArrow.None) return null;

        // Footprint bounds, in metres relative to the anchor cell's centre.
        Vector3 min = Vector3.Inf, max = -Vector3.Inf;
        foreach (Vector3I c in structure.cellOffsets)
        {
            min = min.Min((Vector3)c * BuildGrid.CellSize);
            max = max.Max((Vector3)c * BuildGrid.CellSize);
        }
        float half = BuildGrid.CellSize / 2;
        float beltY = min.Y - half + Structure.BeltTopHeight + Hover;

        List<Vector2> outline; // (x, z) points of the arrow's outline
        Transform3D placement;
        if (structure.flowArrow is FlowArrow.Straight or FlowArrow.StraightDown)
        {
            float back = max.Z + half * 0.6f, front = min.Z - half * 0.6f;
            outline = StraightArrow(back, front);
            // A footprint several cells tall is a slope climbing toward the front: tilt the arrow to match.
            float rise = max.Y - min.Y, run = max.Z - min.Z + BuildGrid.CellSize;
            Vector3 centre = new((min.X + max.X) / 2, beltY + rise / 2, (min.Z + max.Z) / 2);
            float pitch = Mathf.Atan2(rise, run) * (structure.flowArrow == FlowArrow.StraightDown ? -1 : 1);
            placement = new Transform3D(new Basis(Vector3.Right, pitch), centre);
            outline = outline.ConvertAll(p => new Vector2(p.X - centre.X, p.Y - centre.Z));
        }
        else
        {
            outline = TurnArrow(structure.flowArrow == FlowArrow.TurnRight ? 1 : -1);
            placement = new Transform3D(Basis.Identity, new Vector3(0, beltY, 0));
        }

        var mesh = new MeshInstance3D
        {
            Name = "FlowArrow",
            Mesh = Triangulate(outline),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        // The root sits at the anchor cell's centre plus placementOffset; undo that so the arrow is cell-relative.
        mesh.Transform = new Transform3D(Basis.Identity, -structure.placementOffset) * placement;
        return mesh;
    }

    // Tail at z = back, tip at z = front (front < back), centred on x = 0. Returned as a closed outline.
    static List<Vector2> StraightArrow(float back, float front)
    {
        float neck = front + HeadLength, w = ShaftWidth / 2, h = HeadWidth / 2;
        return [new(0, front), new(h, neck), new(w, neck), new(w, back), new(-w, back), new(-w, neck), new(-h, neck)];
    }

    // A quarter arc about the inner corner, from the back edge's centre to the side edge's centre, with a head
    // pointing out of the side. side = +1 exits +X (right turn), -1 exits -X.
    static List<Vector2> TurnArrow(int side)
    {
        const int Steps = 12;
        float r = BuildGrid.CellSize / 2, w = ShaftWidth / 2, h = HeadWidth / 2;
        Vector2 corner = new(side * r, r); // inner corner: back-right for a right turn, back-left for a left one
        // Angles about the corner in the (x, z) plane: the entry (0, r) and the exit (side * r, 0).
        float aStart = side > 0 ? Mathf.Pi : 0;
        float aTip = side > 0 ? Mathf.Pi * 1.5f : -Mathf.Pi / 2;
        float aNeck = aTip - side * HeadLength / r;
        Vector2 At(float a, float radius) => corner + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;

        var outline = new List<Vector2>();
        for (int i = 0; i <= Steps; i++) outline.Add(At(Mathf.Lerp(aStart, aNeck, i / (float)Steps), r + w));
        outline.Add(At(aNeck, r + h));
        outline.Add(At(aTip, r));
        outline.Add(At(aNeck, r - h));
        for (int i = Steps; i >= 0; i--) outline.Add(At(Mathf.Lerp(aStart, aNeck, i / (float)Steps), r - w));
        return outline;
    }

    // Arrow outlines aren't convex, so triangulate properly; drawn from both sides since it may be seen from below.
    static ArrayMesh Triangulate(List<Vector2> outline)
    {
        int[] idx = Geometry2D.TriangulatePolygon(outline.ToArray());
        var verts = new List<Vector3>();
        for (int i = 0; i < idx.Length; i += 3)
        {
            Vector2 a = outline[idx[i]], b = outline[idx[i + 1]], c = outline[idx[i + 2]];
            verts.AddRange([new(a.X, 0, a.Y), new(b.X, 0, b.Y), new(c.X, 0, c.Y)]);
            verts.AddRange([new(a.X, 0, a.Y), new(c.X, 0, c.Y), new(b.X, 0, b.Y)]);
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
