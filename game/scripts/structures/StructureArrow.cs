using Godot;
using System.Collections.Generic;

/// <summary>
/// An arrow shown over a structure's placement preview (<see cref="BlueprintGhost"/>). Add it as a node anywhere
/// under a structure's scene and place it with the normal move and rotate gizmos: it starts at the node's position
/// heading along the node's -Z (the blue gizmo axis, reversed). A straight arrow runs <see cref="length"/> metres; a
/// turn bends a quarter circle toward +X and its head points exactly along +X, the tip <see cref="length"/> metres
/// across. Rotate the node to turn the other way (180 degrees about its Z) or to face any other direction. It draws itself in the editor, so what you see there is what the preview shows. In game
/// it stays hidden except in the preview. Display only: nothing reads it.
/// </summary>
[Tool]
[GlobalClass]
public partial class StructureArrow : Node3D
{
    public enum ArrowKind
    {
        /// <summary>Blue: which way the structure moves items.</summary>
        Direction,
        /// <summary>Green: where items go in.</summary>
        Input,
        /// <summary>Orange: where items come out.</summary>
        Output,
    }

    public enum ArrowShape
    {
        Straight,
        /// <summary>A quarter circle toward +X, then the head straight along +X: tip at (length, 0, -(length - head length)).</summary>
        Turn,
    }

    public static readonly Color DirectionColor = new(0.25f, 0.55f, 1f, 0.8f);
    public static readonly Color InputColor = new(0.3f, 0.95f, 0.4f, 0.8f);
    public static readonly Color OutputColor = new(1f, 0.55f, 0.15f, 0.8f);

    const float ShaftRadius = 0.06f;
    const float HeadRadius = 0.18f;
    const float HeadLength = 0.3f;
    const int TubeSides = 12;
    const int TurnSteps = 16;

    ArrowKind _kind = ArrowKind.Direction;
    [Export]
    public ArrowKind kind
    {
        get { return _kind; }
        set
        {
            _kind = value;
            Rebuild();
        }
    }

    ArrowShape _shape = ArrowShape.Straight;
    [Export]
    public ArrowShape shape
    {
        get { return _shape; }
        set
        {
            _shape = value;
            Rebuild();
        }
    }

    float _length = 1f;
    /// <summary>How far the head's tip reaches: along -Z for a straight arrow, along +X for a turn.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.05,or_greater")]
    public float length
    {
        get { return _length; }
        set
        {
            _length = value;
            Rebuild();
        }
    }

    MeshInstance3D shaft;
    MeshInstance3D head;

    public override void _Ready()
    {
        Rebuild();
        if (!Engine.IsEditorHint())
        {
            Visible = false;
        }
    }

    // (Re)makes the shaft and head as internal children, so they are never saved into the scene.
    void Rebuild()
    {
        if (!IsInsideTree())
        {
            return;
        }
        shaft?.Free();
        head?.Free();

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            AlbedoColor = KindColor(kind),
        };

        // The shaft's centre line, from the start to where the head begins.
        var path = new List<Vector3>();
        if (shape == ArrowShape.Straight)
        {
            float shaftLength = Mathf.Max(length - HeadLength, 0.01f);
            path.Add(Vector3.Zero);
            path.Add(new Vector3(0, 0, -shaftLength));
        }
        else
        {
            // A full quarter circle about (radius, 0, 0), so the shaft ends heading +X and the head carries on along +X.
            float radius = Mathf.Max(length - HeadLength, 0.05f);
            for (int i = 0; i <= TurnSteps; i++)
            {
                float angle = Mathf.Pi / 2f * i / TurnSteps;
                path.Add(new Vector3(radius * (1f - Mathf.Cos(angle)), 0, -radius * Mathf.Sin(angle)));
            }
        }

        shaft = new MeshInstance3D
        {
            Mesh = Tube(path),
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };

        // The head points along the shaft's last segment.
        Vector3 end = path[path.Count - 1];
        Vector3 forward = (end - path[path.Count - 2]).Normalized();
        Vector3 across = forward.Cross(Vector3.Up).Normalized();
        Basis alongForward = new Basis(across, forward, across.Cross(forward));
        head = new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = 0f,
                BottomRadius = HeadRadius,
                Height = HeadLength,
                RadialSegments = TubeSides,
                Rings = 1,
            },
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Transform = new Transform3D(alongForward, end + forward * (HeadLength / 2f)),
        };
        AddChild(shaft, false, InternalMode.Front);
        AddChild(head, false, InternalMode.Front);
    }

    // A round tube of ShaftRadius along path. Arrows lie flat, so each ring is drawn round its point using Up and
    // the sideways direction (path in the XZ plane).
    static ArrayMesh Tube(List<Vector3> path)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < path.Count; i++)
        {
            Vector3 forward;
            if (i == path.Count - 1)
            {
                forward = path[i] - path[i - 1];
            }
            else
            {
                forward = path[i + 1] - path[i];
            }
            Vector3 across = forward.Cross(Vector3.Up).Normalized();
            for (int j = 0; j < TubeSides; j++)
            {
                float angle = Mathf.Tau * j / TubeSides;
                st.AddVertex(path[i] + (Vector3.Up * Mathf.Cos(angle) + across * Mathf.Sin(angle)) * ShaftRadius);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
        {
            for (int j = 0; j < TubeSides; j++)
            {
                int a = i * TubeSides + j;
                int b = i * TubeSides + (j + 1) % TubeSides;
                int c = a + TubeSides;
                int d = b + TubeSides;
                st.AddIndex(a);
                st.AddIndex(c);
                st.AddIndex(b);
                st.AddIndex(b);
                st.AddIndex(c);
                st.AddIndex(d);
            }
        }
        return st.Commit();
    }

    public static Color KindColor(ArrowKind kind)
    {
        if (kind == ArrowKind.Input)
        {
            return InputColor;
        }
        if (kind == ArrowKind.Output)
        {
            return OutputColor;
        }
        return DirectionColor;
    }
}
