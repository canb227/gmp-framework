using Godot;
using System.Collections.Generic;

/// <summary>
/// Outlines an object so it shows through walls (a <see cref="WorldMarker"/> with highlight on). Uses the stencil
/// buffer (Godot 4.5+, Forward+/Mobile): each visible MeshInstance3D under the target gets a twin sharing its
/// mesh, rendered with XRayMask.gdshader (marks the silhouette, through walls) and
/// then XRayOutline.gdshader (a grown hull drawn only outside the marked silhouette). The target's own materials
/// are left alone; the twins are children named <see cref="TwinName"/>, freed by <see cref="Remove"/>.
/// <para>
/// Both passes render after all ordinary transparent geometry (<see cref="MaskPriority"/>), so the outline sits on
/// top of glass and fields too. Two highlighted objects that overlap on screen hide each other's outline where they
/// overlap (they share the stencil value). Static meshes only: skinned meshes would outline their rest pose.
/// </para>
/// </summary>
public static class XRayHighlight
{
    const string MaskShader = "res://game/assets/shaders/XRayMask.gdshader";
    const string OutlineShader = "res://game/assets/shaders/XRayOutline.gdshader";
    const string TwinName = "_XRayHighlight";
    // The mask must be drawn before any outline that reads it; transparent passes sort by priority first.
    const int MaskPriority = 100, OutlinePriority = 101;
    const float WidthPixels = 4f;

    static readonly Dictionary<Node3D, List<MeshInstance3D>> twins = new();

    /// <summary>Outlines <paramref name="target"/> in <paramref name="color"/> (re-applies with the new colour if already outlined).</summary>
    public static void Apply(Node3D target, Color color)
    {
        Remove(target);
        ShaderMaterial outline = new() { Shader = GD.Load<Shader>(OutlineShader), RenderPriority = OutlinePriority };
        outline.SetShaderParameter("color", color);
        outline.SetShaderParameter("width_px", WidthPixels);
        ShaderMaterial mask = new() { Shader = GD.Load<Shader>(MaskShader), RenderPriority = MaskPriority, NextPass = outline };

        List<MeshInstance3D> added = new();
        foreach (MeshInstance3D source in Meshes(target))
        {
            MeshInstance3D twin = new()
            {
                Name = TwinName,
                Mesh = source.Mesh,
                MaterialOverride = mask,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Layers = source.Layers,
            };
            source.AddChild(twin);
            added.Add(twin);
        }
        twins[target] = added;
    }

    /// <summary>Takes the outline off <paramref name="target"/>, if it has one.</summary>
    public static void Remove(Node3D target)
    {
        ForgetFreed();
        if (!twins.Remove(target, out List<MeshInstance3D> added)) return;
        foreach (MeshInstance3D twin in added)
        {
            if (GodotObject.IsInstanceValid(twin)) twin.QueueFree();
        }
    }

    /// <summary>Drops targets that have been freed (their twins went with them).</summary>
    static void ForgetFreed()
    {
        List<Node3D> freed = null;
        foreach (Node3D target in twins.Keys)
        {
            if (!GodotObject.IsInstanceValid(target)) (freed ??= new()).Add(target);
        }
        if (freed != null) foreach (Node3D target in freed) twins.Remove(target);
    }

    /// <summary>The visible meshes making up <paramref name="target"/>, itself included, not counting existing twins.</summary>
    static IEnumerable<MeshInstance3D> Meshes(Node3D target)
    {
        if (target is MeshInstance3D self && self.Mesh != null && self.IsVisibleInTree()) yield return self;
        foreach (Node node in target.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            if (node is MeshInstance3D mesh && mesh.Mesh != null && mesh.IsVisibleInTree() && mesh.Name != TwinName)
                yield return mesh;
        }
    }
}
