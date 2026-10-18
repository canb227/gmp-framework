using Godot;

/// <summary>
/// Turns an item's scene into the description our physics world registers an item kind from
/// (<c>GMPPhysicsWorld.register_item_kind</c>, see native/src/physics_world_kinds.cpp for the keys): the collider from
/// the first GMPShape in the scene, its sync and shadow settings from the item's <see cref="ItemInfo"/>, and a part for
/// every visible MeshInstance3D.
/// </summary>
public static class ItemKinds
{
    /// <summary>GMPShape properties copied as they are.</summary>
    static readonly string[] ShapeProperties =
    {
        "shape", "size", "radius", "height", "sides", "density", "friction", "restitution", "rolling_resistance", "surface_type",
    };

    // GMPShape.shape values whose collider comes from its mesh. Items are dynamic, so a mesh shape becomes a hull too.
    const int HullShape = 4;
    const int MeshShape = 5;

    /// <param name="info">The item's definition, or null for an item without one.</param>
    public static Godot.Collections.Dictionary Describe(PackedScene scene, ItemInfo info)
    {
        // Instanced outside the tree only to read it: no body is made.
        Node root = scene.Instantiate();
        Godot.Collections.Dictionary description = new();

        Node shape = FindShape(root);
        if (shape == null)
        {
            Logging.Error($"{scene.ResourcePath} has no GMPShape: the item gets a 1 m box", "ItemKinds");
        }
        else
        {
            foreach (string name in ShapeProperties)
            {
                description[name] = shape.Get(name);
            }
            int shapeType = shape.Get("shape").AsInt32();
            if (shapeType == HullShape || shapeType == MeshShape)
            {
                Mesh mesh = shape.Get("mesh").As<Mesh>();
                description["shape"] = HullShape;
                if (mesh != null)
                {
                    description["hull_points"] = mesh.GetFaces();
                }
            }
        }

        if (info != null)
        {
            description["cast_shadows"] = info.castsShadows;
            description["base_priority"] = info.basePriority;
        }

        Godot.Collections.Array parts = new();
        foreach (Node node in root.FindChildren("*", "MeshInstance3D", true, false))
        {
            MeshInstance3D meshInstance = (MeshInstance3D)node;
            if (meshInstance.Mesh == null || !meshInstance.Visible)
            {
                continue;
            }
            Godot.Collections.Dictionary part = new();
            // Not used by the physics world: lets the caller pick out parts (the default box's icon faces).
            part["name"] = meshInstance.Name;
            part["mesh"] = meshInstance.Mesh;
            part["transform"] = OffsetFromRoot(meshInstance, root);
            if (meshInstance.MaterialOverride != null)
            {
                part["material_override"] = meshInstance.MaterialOverride;
            }
            Godot.Collections.Array surfaceMaterials = new();
            for (int surface = 0; surface < meshInstance.Mesh.GetSurfaceCount(); surface++)
            {
                surfaceMaterials.Add(meshInstance.GetSurfaceOverrideMaterial(surface));
            }
            part["surface_materials"] = surfaceMaterials;
            parts.Add(part);
        }
        description["parts"] = parts;

        root.Free();
        return description;
    }

    /// <summary>The first GMPShape in the scene (depth first), or null.</summary>
    static Node FindShape(Node node)
    {
        if (node.IsClass("GMPShape"))
        {
            return node;
        }
        foreach (Node child in node.GetChildren())
        {
            Node found = FindShape(child);
            if (found != null)
            {
                return found;
            }
        }
        return null;
    }

    /// <summary>Where <paramref name="node"/> sits relative to <paramref name="root"/> (not in the tree, so there is no GlobalTransform).</summary>
    static Transform3D OffsetFromRoot(Node3D node, Node root)
    {
        Transform3D offset = node.Transform;
        Node parent = node.GetParent();
        while (parent != null && parent != root)
        {
            if (parent is Node3D parent3D)
            {
                offset = parent3D.Transform * offset;
            }
            parent = parent.GetParent();
        }
        return offset;
    }
}
