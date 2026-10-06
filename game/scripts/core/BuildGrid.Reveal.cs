using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// BuildGrid: the grid shown while a blueprint is held. The ghost (<see cref="BlueprintGhost"/>) publishes where it
/// is (<see cref="placementFocus"/>) and the face it's snapped to (<see cref="placementFace"/>); around the focus a
/// 3D lattice of grid lines is revealed, fading out with distance as if lit by the ghost, and the targeted face is
/// gently highlighted.
/// <para>
/// Nothing ever moves: the grid should look fixed in the world and only be revealed differently. So each place the
/// ghost has been gets its own reveal, a lattice centred there that fades in while the ghost is there and fades out
/// where it stands once the ghost moves on, and the face highlight crossfades the same way. Overlapping reveals
/// draw the same lines, so alpha blending unites them (coverage 1 - (1-a)(1-b)) without seams. See
/// BuildGridReveal.gdshader and BuildGridFace.gdshader.
/// </para>
/// </summary>
public partial class BuildGrid
{
    /// <summary>Centre of the ghost's footprint in world space, or null while it has no target. Set by the local ghost.</summary>
    public static Vector3? placementFocus;
    /// <summary>The face the ghost is snapped to: the empty cell across it and its normal (out of the surface), or null.</summary>
    public static (Vector3I cell, Vector3I normal)? placementFace;
    /// <summary>The ghost's tint (valid/invalid); the face highlight takes its hue. Set by the local ghost.</summary>
    public static Color placementTint = Colors.White;

    // Lattice half-extent in cells. The reveal radius must stay within RevealCells * CellSize so the lattice edge is
    // never visible: the lattice spans RevealCells cells either side of the focus' cell.
    private const int RevealCells = 4;
    private const float RevealRadius = 7.0f;
    // Enough layers for a quick sweep across cells to fade out gracefully; past that the faintest is recycled.
    private const int RevealLayers = 5;
    private const int FaceLayers = 3;
    private const float RevealFadeInTime = 0.18f;
    private const float RevealFadeOutTime = 0.35f;
    private const float FaceFadeInTime = 0.12f;
    private const float FaceFadeOutTime = 0.2f;
    // Lifts the face highlight off the surface it lies on.
    private const float FaceLift = 0.012f;
    private const float FaceTintRate = 14f;

    /// <summary>
    /// A set of identical visuals, each pinned to a key (a place), crossfading between them: the one for the current
    /// key fades in, every other fades out where it is.
    /// </summary>
    private class FadePool<TKey> where TKey : struct, IEquatable<TKey>
    {
        class Layer
        {
            public MeshInstance3D instance;
            public ShaderMaterial material;
            public TKey key;
            public float weight;
        }

        readonly List<Layer> layers = new();
        readonly float fadeIn, fadeOut;
        Layer current;

        public FadePool(Node parent, int count, Func<MeshInstance3D> create, string shaderPath, float fadeIn, float fadeOut)
        {
            this.fadeIn = fadeIn;
            this.fadeOut = fadeOut;
            var shader = GD.Load<Shader>(shaderPath);
            for (int i = 0; i < count; i++)
            {
                var layer = new Layer { instance = create(), material = new ShaderMaterial { Shader = shader } };
                layer.instance.MaterialOverride = layer.material;
                layer.instance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                layer.instance.TopLevel = true;
                layer.instance.Visible = false;
                parent.AddChild(layer.instance);
                layers.Add(layer);
            }
        }

        public void ForEachMaterial(Action<ShaderMaterial> action)
        {
            foreach (Layer layer in layers) action(layer.material);
        }

        /// <summary>The material of the layer fading in, or null; the others keep what they had as they fade out.</summary>
        public ShaderMaterial currentMaterial => current?.material;

        /// <summary>Fades in the layer at <paramref name="key"/> (or none, if null) and fades out the rest.</summary>
        public void Update(TKey? key, float delta, Action<MeshInstance3D, ShaderMaterial, TKey> place)
        {
            if (key is not TKey k)
            {
                current = null;
            }
            else if (current == null || !current.key.Equals(k))
            {
                // Back to a place still fading out: pick it up from there. Otherwise reuse the faintest layer.
                current = layers.Find(l => l.weight > 0f && l.key.Equals(k));
                if (current == null)
                {
                    current = layers[0];
                    foreach (Layer l in layers)
                    {
                        if (l.weight < current.weight) current = l;
                    }
                    current.key = k;
                    current.weight = 0f;
                    place(current.instance, current.material, k);
                }
            }

            foreach (Layer layer in layers)
            {
                layer.weight = layer == current
                    ? Mathf.Min(1f, layer.weight + delta / fadeIn)
                    : Mathf.Max(0f, layer.weight - delta / fadeOut);
                layer.instance.Visible = layer.weight > 0f;
                if (layer.instance.Visible)
                {
                    float w = layer.weight;
                    layer.material.SetShaderParameter("opacity", w * w * (3f - 2f * w));
                }
            }
        }
    }

    private FadePool<Vector3> _reveal;
    private FadePool<(Vector3I, Vector3I)> _face;

    private void CreateReveal()
    {
        ArrayMesh lattice = BuildRevealLattice();
        _reveal = new FadePool<Vector3>(this, RevealLayers,
            () => new MeshInstance3D { Mesh = lattice },
            "res://game/assets/shaders/BuildGridReveal.gdshader", RevealFadeInTime, RevealFadeOutTime);
        _reveal.ForEachMaterial(m =>
        {
            m.SetShaderParameter("cell_size", CellSize);
            m.SetShaderParameter("reveal_radius", RevealRadius);
        });

        var quad = new QuadMesh { Size = new Vector2(CellSize, CellSize) };
        _face = new FadePool<(Vector3I, Vector3I)>(this, FaceLayers,
            () => new MeshInstance3D { Mesh = quad },
            "res://game/assets/shaders/BuildGridFace.gdshader", FaceFadeInTime, FaceFadeOutTime);
    }

    private void UpdateReveal(float delta)
    {
        bool active = placementActive && placementFocus.HasValue;

        _reveal.Update(active ? placementFocus : null, delta, (instance, material, focus) =>
        {
            // The lattice sits on the world grid around the focus' cell; only the fade is centred on the focus.
            instance.GlobalPosition = (Vector3)WorldToCell(focus) * CellSize;
            material.SetShaderParameter("focus", focus);
        });

        bool newFace = false;
        _face.Update(active ? placementFace : null, delta, (instance, _, face) =>
        {
            var (cell, normal) = face;
            instance.GlobalTransform = new Transform3D(FaceBasis(normal),
                CellToWorld(cell) - (Vector3)normal * (CellSize / 2f - FaceLift));
            newFace = true;
        });
        // The ghost's hue at full strength (its alpha is for the ghost's body). A new face starts in the current tint;
        // on the same face a valid/invalid change blends over rather than flicking.
        Color tint = new(placementTint, 1f);
        _faceTint = newFace ? tint : _faceTint.Lerp(tint, 1f - Mathf.Exp(-FaceTintRate * delta));
        _face.currentMaterial?.SetShaderParameter("face_color", _faceTint);
    }

    private Color _faceTint = Colors.White;

    // A basis whose +Z (a QuadMesh's facing) is the face normal.
    private static Basis FaceBasis(Vector3I normal)
    {
        Vector3 z = normal;
        Vector3 up = Mathf.Abs(z.Y) > 0.5f ? Vector3.Forward : Vector3.Up;
        Vector3 x = up.Cross(z).Normalized();
        return new Basis(x, z.Cross(x), z);
    }

    /// <summary>
    /// The lattice of grid lines within <see cref="RevealCells"/> cells of the origin cell, in the format
    /// BuildGridReveal.gdshader expects: per cell-long segment a quad of four vertices on the line, the line's
    /// direction in NORMAL and the side in UV.y.
    /// </summary>
    private static ArrayMesh BuildRevealLattice()
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();

        int min = -RevealCells, max = RevealCells + 1;
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 dir = Vector3.Zero;
            dir[axis] = 1f;
            int a1 = (axis + 1) % 3, a2 = (axis + 2) % 3;
            for (int i = min; i <= max; i++)
            {
                for (int j = min; j <= max; j++)
                {
                    for (int k = min; k < max; k++)
                    {
                        Vector3 start = Vector3.Zero;
                        start[a1] = i * CellSize;
                        start[a2] = j * CellSize;
                        start[axis] = k * CellSize;
                        Vector3 end = start + dir * CellSize;

                        int baseIndex = vertices.Count;
                        vertices.AddRange([start, start, end, end]);
                        normals.AddRange([dir, dir, dir, dir]);
                        uvs.AddRange([new Vector2(0, -1), new Vector2(0, 1), new Vector2(1, -1), new Vector2(1, 1)]);
                        indices.AddRange([baseIndex, baseIndex + 1, baseIndex + 2, baseIndex + 2, baseIndex + 1, baseIndex + 3]);
                    }
                }
            }
        }

        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }
}
