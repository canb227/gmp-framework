using Godot;

/// <summary>
/// Attach to any Node3D and assign a source MeshInstance3D.
/// Scatters CopyCount copies of the source mesh at random positions around it,
/// either as individual MeshInstance3D nodes or as a single MultiMeshInstance3D.
/// </summary>
public partial class DeleteMe : Node3D
{
    public enum ScatterMode
    {
        MeshInstances,
        MultiMesh
    }

    [Export] public MeshInstance3D Source { get; set; }
    [Export] public ScatterMode Mode { get; set; } = ScatterMode.MultiMesh;
    [Export] public int CopyCount { get; set; } = 100;
    [Export] public float MinRadius { get; set; } = 2f;
    [Export] public float MaxRadius { get; set; } = 10f;
    /// <summary>True = flat ring on the XZ plane. False = 3D spherical shell.</summary>
    [Export] public bool Flat { get; set; } = true;
    [Export] public bool RandomYRotation { get; set; } = true;
    [Export] public Vector2 RandomScaleRange { get; set; } = new Vector2(0.8f, 1.2f);
    /// <summary>0 = different every run, anything else = repeatable.</summary>
    [Export] public int RandomSeed { get; set; } = 0;
    [Export] public bool SpawnOnReady { get; set; } = true;

    private Node3D _container;
    private readonly RandomNumberGenerator _rng = new();

    public override void _Ready()
    {
        if (SpawnOnReady)
            Spawn();
    }

    /// <summary>(Re)builds the scatter. Call again at runtime after changing Mode or any setting.</summary>
    public void Spawn()
    {
        if (Source == null)
        {
            GD.PushError("MeshScatter: assign a Source MeshInstance3D.");
            return;
        }

        Clear();

        if (RandomSeed != 0)
            _rng.Seed = (ulong)RandomSeed;
        else
            _rng.Randomize();

        // Container is top-level and centered on the source, so all offsets are "around" it
        _container = new Node3D { Name = "ScatterContainer", TopLevel = true };
        AddChild(_container);
        _container.GlobalPosition = Source.GlobalPosition;

        if (Mode == ScatterMode.MeshInstances)
            SpawnMeshInstances();
        else
            SpawnMultiMesh();
    }

    public void Clear()
    {
        if (_container != null && IsInstanceValid(_container))
            _container.QueueFree();
        _container = null;
    }

    private void SpawnMeshInstances()
    {
        for (int i = 0; i < CopyCount; i++)
        {
            var copy = new MeshInstance3D
            {
                Mesh = Source.Mesh,
                MaterialOverride = Source.MaterialOverride
            };

            for (int s = 0; s < Source.GetSurfaceOverrideMaterialCount(); s++)
                copy.SetSurfaceOverrideMaterial(s, Source.GetSurfaceOverrideMaterial(s));

            _container.AddChild(copy);
            copy.Transform = RandomTransform();
        }
    }

    private void SpawnMultiMesh()
    {
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, // must be set BEFORE InstanceCount
            Mesh = Source.Mesh
        };
        mm.InstanceCount = CopyCount;

        for (int i = 0; i < CopyCount; i++)
            mm.SetInstanceTransform(i, RandomTransform());

        var mmi = new MultiMeshInstance3D
        {
            Multimesh = mm,
            // MultiMesh doesn't pick up the source's material automatically
            MaterialOverride = Source.MaterialOverride ?? Source.GetActiveMaterial(0)
        };
        _container.AddChild(mmi);
    }

    private Transform3D RandomTransform()
    {
        float scale = _rng.RandfRange(RandomScaleRange.X, RandomScaleRange.Y);
        Basis basis = Basis.Identity;

        if (RandomYRotation)
            basis = basis.Rotated(Vector3.Up, _rng.RandfRange(0f, Mathf.Tau));

        basis = basis.Scaled(Vector3.One * scale);
        return new Transform3D(basis, RandomOffset());
    }

    private Vector3 RandomOffset()
    {
        if (Flat)
        {
            // sqrt gives an even spread across the ring instead of clustering near the center
            float angle = _rng.RandfRange(0f, Mathf.Tau);
            float dist = Mathf.Sqrt(_rng.RandfRange(MinRadius * MinRadius, MaxRadius * MaxRadius));
            return new Vector3(Mathf.Cos(angle) * dist, 0f, Mathf.Sin(angle) * dist);
        }

        var dir = new Vector3(
            _rng.RandfRange(-1f, 1f),
            _rng.RandfRange(-1f, 1f),
            _rng.RandfRange(-1f, 1f));

        if (dir.LengthSquared() < 0.0001f)
            dir = Vector3.Up;

        float d = Mathf.Pow(_rng.RandfRange(Mathf.Pow(MinRadius, 3f), Mathf.Pow(MaxRadius, 3f)), 1f / 3f);
        return dir.Normalized() * d;
    }
}
