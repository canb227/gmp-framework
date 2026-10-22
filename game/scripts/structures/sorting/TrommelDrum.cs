using Godot;
using System.Collections.Generic;

/// <summary>
/// A trommel's turning drum: a cage of round bars that tumbles what is inside it, letting items smaller than
/// <see cref="gap"/> fall out between the bars while bigger ones work their way to the open far end. Put it on a
/// kinematic GMPBody under a structure. The drum's axis runs from the node's origin toward -Z for <see cref="length"/>
/// metres; tilt the node so the far end is lower and items travel along it.
/// <para>
/// The parts are made from the settings below when the node is ready, as children with no owner, so they are never
/// saved into the scene (like <see cref="GrateBars"/>): bars around the cage (a capsule GMPShape each), lifter fins along
/// the inside that carry items up and drop them, a lip round the back end that keeps them from falling out backward,
/// and a hoop at each end for looks. Each kind of part is drawn by one multimesh.
/// </para>
/// <para>
/// In game the node turns itself about its axis at <see cref="rpm"/>, from <see cref="GameWorld.netTime"/>, so every
/// peer's drum is at the same angle without sending anything. The drum's bottom moves toward -X: items are lifted up
/// the -X side and fall back toward the middle.
/// </para>
/// </summary>
[Tool]
[GlobalClass]
public partial class TrommelDrum : Node3D
{
    float _radius = 1.15f;
    /// <summary>From the axis to the bars' centres.</summary>
    [Export(PropertyHint.Range, "0.2,5,0.01,or_greater,suffix:m")]
    public float radius
    {
        get { return _radius; }
        set
        {
            _radius = value;
            Rebuild();
        }
    }

    float _length = 6f;
    [Export(PropertyHint.Range, "0.5,20,0.01,or_greater,suffix:m")]
    public float length
    {
        get { return _length; }
        set
        {
            _length = value;
            Rebuild();
        }
    }

    float _barThickness = 0.1f;
    /// <summary>Each bar's diameter.</summary>
    [Export(PropertyHint.Range, "0.01,1,0.005,or_greater,suffix:m")]
    public float barThickness
    {
        get { return _barThickness; }
        set
        {
            _barThickness = value;
            Rebuild();
        }
    }

    float _gap = 0.3f;
    /// <summary>The smallest clear opening between neighbouring bars. As many bars as leave at least this much go round the drum.</summary>
    [Export(PropertyHint.Range, "0.01,2,0.005,or_greater,suffix:m")]
    public float gap
    {
        get { return _gap; }
        set
        {
            _gap = value;
            Rebuild();
        }
    }

    int _lifters = 4;
    /// <summary>How many fins run along the inside, evenly spaced round it.</summary>
    [Export(PropertyHint.Range, "0,16,1")]
    public int lifters
    {
        get { return _lifters; }
        set
        {
            _lifters = value;
            Rebuild();
        }
    }

    float _lifterHeight = 0.15f;
    /// <summary>How far the fins stand in from the bars.</summary>
    [Export(PropertyHint.Range, "0.01,1,0.01,or_greater,suffix:m")]
    public float lifterHeight
    {
        get { return _lifterHeight; }
        set
        {
            _lifterHeight = value;
            Rebuild();
        }
    }

    float _lipDepth = 0.25f;
    /// <summary>How far the ring round the back end reaches in from the bars. 0: no lip.</summary>
    [Export(PropertyHint.Range, "0,1,0.01,or_greater,suffix:m")]
    public float lipDepth
    {
        get { return _lipDepth; }
        set
        {
            _lipDepth = value;
            Rebuild();
        }
    }

    float _friction = 0.5f;
    [Export(PropertyHint.Range, "0,2,0.01")]
    public float friction
    {
        get { return _friction; }
        set
        {
            _friction = value;
            Rebuild();
        }
    }

    /// <summary>Turns per minute. A drum pins items to its wall near 42.3 / sqrt(diameter) rpm; real trommels run at about a third of that.</summary>
    [Export(PropertyHint.Range, "0,60,0.1,or_greater")]
    public float rpm = 8f;

    Material _barMaterial;
    /// <summary>What the drum is drawn with; none: plain steel.</summary>
    [Export]
    public Material barMaterial
    {
        get { return _barMaterial; }
        set
        {
            _barMaterial = value;
            Rebuild();
        }
    }

    // GMPShape.shape values (native/src/physics_shape.h).
    const int BoxShape = 0;
    const int CapsuleShape = 2;
    const float LifterThickness = 0.06f;
    const float LipThickness = 0.06f;
    const float HoopThickness = 0.08f;

    // The parts made by Rebuild.
    readonly List<Node> made = new();

    // The node's transform as placed in the scene; it turns about this frame's Z.
    Transform3D restTransform;

    public override void _Ready()
    {
        restTransform = Transform;
        Rebuild();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Engine.IsEditorHint() || rpm <= 0)
        {
            return;
        }
        // Negative about +Z: the drum's bottom moves toward -X.
        double msPerTurn = 60000.0 / rpm;
        double turn = (GameWorld.netTime % (ulong)msPerTurn) / msPerTurn;
        float angle = -(float)(turn * Mathf.Tau);
        Transform = new Transform3D(restTransform.Basis * new Basis(Vector3.Back, angle), restTransform.Origin);
    }

    // (Re)makes the drum's parts as children with no owner, so they are never saved into the scene.
    void Rebuild()
    {
        if (!IsInsideTree())
        {
            return;
        }
        foreach (Node old in made)
        {
            RemoveChild(old);
            old.QueueFree();
        }
        made.Clear();

        // As many bars as leave at least gap (straight across) between neighbours.
        float barRadius = barThickness / 2f;
        float halfStep = Mathf.Asin(Mathf.Min(1f, (barThickness + gap) / (2f * radius)));
        int count = Mathf.Max(3, Mathf.FloorToInt(Mathf.Pi / halfStep));
        float step = Mathf.Tau / count;

        // A capsule's axis is its Y: turned a quarter about X, it runs along -Z.
        Basis alongAxis = new Basis(Vector3.Right, -Mathf.Pi / 2f);
        var bars = new List<Transform3D>();
        for (int i = 0; i < count; i++)
        {
            float angle = i * step;
            var place = new Transform3D(alongAxis, new Vector3(radius * Mathf.Cos(angle), radius * Mathf.Sin(angle), -length / 2f));
            bars.Add(place);

            Node3D bar = ClassDB.Instantiate("GMPShape").As<Node3D>();
            bar.Name = $"Bar{i}";
            bar.Transform = place;
            bar.Set("shape", CapsuleShape);
            bar.Set("radius", barRadius);
            bar.Set("height", length);
            bar.Set("friction", friction);
            bar.Set("restitution", 0.05f);
            AddChild(bar);
            made.Add(bar);
        }

        // Fins and lip segments stand on the inside of the bars. A box turned by (angle - 90 degrees) about Z has its
        // Y pointing out from the axis at angle and its X along the drum's round.
        float inside = radius - barRadius;
        var fins = new List<Transform3D>();
        for (int i = 0; i < lifters; i++)
        {
            // Halfway between two bars.
            float angle = (Mathf.FloorToInt((float)i * count / lifters) + 0.5f) * step;
            float middle = inside - lifterHeight / 2f;
            var place = new Transform3D(new Basis(Vector3.Back, angle - Mathf.Pi / 2f),
                new Vector3(middle * Mathf.Cos(angle), middle * Mathf.Sin(angle), -length / 2f));
            fins.Add(place);

            Node3D fin = ClassDB.Instantiate("GMPShape").As<Node3D>();
            fin.Name = $"Lifter{i}";
            fin.Transform = place;
            fin.Set("shape", BoxShape);
            fin.Set("size", new Vector3(LifterThickness, lifterHeight, length));
            fin.Set("friction", friction);
            AddChild(fin);
            made.Add(fin);
        }

        var lip = new List<Transform3D>();
        float lipWidth = 2f * inside * Mathf.Tan(step / 2f) + 0.02f;
        if (lipDepth > 0)
        {
            for (int i = 0; i < count; i++)
            {
                float angle = (i + 0.5f) * step;
                float middle = inside - lipDepth / 2f;
                var place = new Transform3D(new Basis(Vector3.Back, angle - Mathf.Pi / 2f),
                    new Vector3(middle * Mathf.Cos(angle), middle * Mathf.Sin(angle), -LipThickness / 2f));
                lip.Add(place);

                Node3D piece = ClassDB.Instantiate("GMPShape").As<Node3D>();
                piece.Name = $"Lip{i}";
                piece.Transform = place;
                piece.Set("shape", BoxShape);
                piece.Set("size", new Vector3(lipWidth, lipDepth, LipThickness));
                piece.Set("friction", friction);
                AddChild(piece);
                made.Add(piece);
            }
        }

        Material material = barMaterial;
        if (material == null)
        {
            material = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.55f, 0.56f, 0.58f),
                Metallic = 0.8f,
                Roughness = 0.35f,
            };
        }
        AddParts("BarMeshes", new CapsuleMesh { Radius = barRadius, Height = length, RadialSegments = 10, Rings = 2 }, bars, material);
        AddParts("LifterMeshes", new BoxMesh { Size = new Vector3(LifterThickness, lifterHeight, length) }, fins, material);
        AddParts("LipMeshes", new BoxMesh { Size = new Vector3(lipWidth, lipDepth, LipThickness) }, lip, material);

        // A hoop round each end, outside the bars. A torus lies in its XZ plane: turned a quarter about X, round the axis.
        var hoopMesh = new TorusMesh
        {
            InnerRadius = radius - barRadius,
            OuterRadius = radius + barRadius + HoopThickness,
            Rings = 48,
            RingSegments = 8,
        };
        Basis aroundAxis = new Basis(Vector3.Right, Mathf.Pi / 2f);
        var hoops = new List<Transform3D>
        {
            new Transform3D(aroundAxis, new Vector3(0, 0, -HoopThickness)),
            new Transform3D(aroundAxis, new Vector3(0, 0, -length + HoopThickness)),
        };
        AddParts("HoopMeshes", hoopMesh, hoops, material);
    }

    // One multimesh drawing mesh at each of places.
    void AddParts(string name, Mesh mesh, List<Transform3D> places, Material material)
    {
        if (places.Count == 0)
        {
            return;
        }
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = mesh,
            InstanceCount = places.Count,
        };
        for (int i = 0; i < places.Count; i++)
        {
            multimesh.SetInstanceTransform(i, places[i]);
        }
        var parts = new MultiMeshInstance3D
        {
            Name = name,
            Multimesh = multimesh,
            MaterialOverride = material,
        };
        AddChild(parts);
        made.Add(parts);
    }
}
