using Godot;
using System.Collections.Generic;

/// <summary>
/// A grate of round bars for sieving: items smaller than <see cref="gap"/> fall between the bars, bigger ones ride
/// over them. Put it anywhere under a structure's GMPBody. Its bars lie in the node's XZ plane, centred on X, running
/// from the node's origin toward -Z for <see cref="length"/> metres, with their tops on the node's origin (Y = 0) so
/// the node's plane is the surface items sit on. Tilt the node to make a slope.
/// <para>
/// The bars (a capsule GMPShape each, drawn by one multimesh) are made from the settings below when the node is
/// ready, as children with no owner, so they are never saved into the scene (not internal children: a GMPBody only
/// looks through ordinary children for its shapes). A GMPBody collects its shapes when it is ready,
/// which is after its children are, so they collide like hand-placed shapes. In the editor they're remade on every
/// change, so what you see is what you get.
/// </para>
/// </summary>
[Tool]
[GlobalClass]
public partial class GrateBars : Node3D
{
    float _width = 1.88f;
    /// <summary>The width the bars are spread over (X). As many bars fit as leave openings of <see cref="gap"/>; what is left over is shared between the two edges.</summary>
    [Export(PropertyHint.Range, "0.1,10,0.01,or_greater,suffix:m")]
    public float width
    {
        get { return _width; }
        set
        {
            _width = value;
            Rebuild();
        }
    }

    float _length = 3f;
    /// <summary>How long each bar is, along -Z.</summary>
    [Export(PropertyHint.Range, "0.1,20,0.01,or_greater,suffix:m")]
    public float length
    {
        get { return _length; }
        set
        {
            _length = value;
            Rebuild();
        }
    }

    float _barThickness = 0.08f;
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
    /// <summary>The clear opening between neighbouring bars: what decides which items fall through.</summary>
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

    float _friction = 0.1f;
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

    float _pushSpeed = 0.5f;
    /// <summary>
    /// The bars' surface speed toward -Z (a tangent velocity, like a belt's): it nudges what rests on them along.
    /// 0: items move by the slope and their own speed alone.
    /// </summary>
    [Export(PropertyHint.Range, "0,4,0.05,or_greater,suffix:m/s")]
    public float pushSpeed
    {
        get { return _pushSpeed; }
        set
        {
            _pushSpeed = value;
            Rebuild();
        }
    }

    Material _barMaterial;
    /// <summary>What the bars are drawn with; none: plain steel.</summary>
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

    // GMPShape.shape value of a capsule (native/src/physics_shape.h).
    const int CapsuleShape = 2;

    // The bar shapes and their multimesh, made by Rebuild.
    readonly List<Node> made = new();

    public override void _Ready()
    {
        Rebuild();
    }

    // (Re)makes the bars as children with no owner, so they are never saved into the scene.
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

        // As many bars as fit with gap between each pair; the edges get the rest, which is less than a bar plus a gap.
        int count = Mathf.Max(1, Mathf.FloorToInt((width + gap) / (barThickness + gap)));
        float pitch = barThickness + gap;
        float firstX = -(count - 1) * pitch / 2f;
        float radius = barThickness / 2f;

        // A capsule's axis is its Y: turned a quarter about X, it runs along -Z, and its +Y tangent velocity pushes toward -Z.
        Basis alongBars = new Basis(Vector3.Right, -Mathf.Pi / 2f);

        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = new CapsuleMesh
            {
                Radius = radius,
                Height = length,
                RadialSegments = 12,
                Rings = 2,
            },
            InstanceCount = count,
        };

        for (int i = 0; i < count; i++)
        {
            var place = new Transform3D(alongBars, new Vector3(firstX + i * pitch, -radius, -length / 2f));
            multimesh.SetInstanceTransform(i, place);

            Node3D bar = ClassDB.Instantiate("GMPShape").As<Node3D>();
            bar.Name = $"Bar{i}";
            bar.Transform = place;
            bar.Set("shape", CapsuleShape);
            bar.Set("radius", radius);
            bar.Set("height", length);
            bar.Set("friction", friction);
            bar.Set("restitution", 0.05f);
            bar.Set("tangent_velocity", new Vector3(0, pushSpeed, 0));
            AddChild(bar);
            made.Add(bar);
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
        var bars = new MultiMeshInstance3D
        {
            Name = "BarMeshes",
            Multimesh = multimesh,
            MaterialOverride = material,
        };
        AddChild(bars);
        made.Add(bars);
    }
}
