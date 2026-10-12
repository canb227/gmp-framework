#if TOOLS
using Godot;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Two toggles on the 3D editor's toolbar for making and placing <see cref="Structure"/>s:
/// <list type="bullet">
/// <item>View Grid draws the build grid's 1 m cells on a floor around the selection, and outlines the cells every
/// Structure in the open scene claims (its cellOffsets), the scene's own root included when editing a structure.</item>
/// <item>Grid Snap moves every selected Structure to the grid pose nearest to where a move or rotation ends
/// (<see cref="Structure.NearestGridTransform"/>), so hand-placed ones pass the runtime alignment check. The open
/// scene's root is never moved, so editing a structure's own scene is unaffected.</item>
/// <item>Fill Cells sets the selected Structures' cellOffsets (or the open structure scene's root's, with nothing
/// selected) to every cell their solid colliders touch: each collider's bounding box, read in the root's frame.
/// The box beside it is the overhang tolerance: a collider must reach further than that into a cell to claim it.</item>
/// </list>
/// Structure isn't a [Tool] script, so in the editor its nodes carry a placeholder: they are found by script
/// inheritance and their cellOffsets are read through Get().
/// </summary>
[Tool]
public partial class StructureToolsPlugin : EditorPlugin
{
    /// <summary>How many cells the drawn floor grid reaches out from the selection in each direction.</summary>
    const int GridRadius = 12;

    static readonly Vector3I[] Axes = [Vector3I.Right, Vector3I.Up, Vector3I.Back];

    // Partial classes get one [ScriptPath] per file; the generator tags each with the script's path.
    static readonly HashSet<string> structureScriptPaths = StructureScriptPaths();

    CheckButton viewGridToggle;
    CheckButton snapToggle;
    Button fillCellsButton;
    SpinBox toleranceBox;
    MeshInstance3D overlay;
    ImmediateMesh lines;
    StandardMaterial3D gridMaterial;
    StandardMaterial3D footprintMaterial;

    public override void _EnterTree()
    {
        viewGridToggle = new CheckButton
        {
            Text = "View Grid",
            TooltipText = "Show the build grid's cells around the selection, and the cells each Structure claims.",
        };
        snapToggle = new CheckButton
        {
            Text = "Grid Snap",
            TooltipText = "When a move ends, put selected Structures on the build grid (nearest cell corner, quarter-turn yaw).",
            ButtonPressed = true,
        };
        fillCellsButton = new Button
        {
            Text = "Fill Cells",
            TooltipText = "Set the selected Structures' cellOffsets (or the open structure scene's) to every cell their solid colliders touch.",
        };
        fillCellsButton.Pressed += FillCells;
        toleranceBox = new SpinBox
        {
            MinValue = 0,
            MaxValue = 0.49,
            Step = 0.01,
            Value = 0.05,
            Suffix = "m",
            TooltipText = "Fill Cells overhang tolerance: a collider only claims a cell if it reaches further than this into it.",
        };
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, viewGridToggle);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, snapToggle);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, fillCellsButton);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, toleranceBox);
        // A move records its undo step when it ends (a gizmo drag released, a keyboard move confirmed, an inspector
        // edit), so snapping then leaves the drag itself free.
        GetUndoRedo().HistoryChanged += OnHistoryChanged;

        gridMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 1f, 1f, 0.25f),
        };
        // Drawn through everything so a structure's cells show inside its model.
        footprintMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            NoDepthTest = true,
            AlbedoColor = new Color(1f, 0.55f, 0.1f, 0.8f),
        };
        lines = new ImmediateMesh();
        // Has no owner, so it's never saved into the scene it's drawn over.
        overlay = new MeshInstance3D
        {
            Name = "StructureToolsOverlay",
            Mesh = lines,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
    }

    public override void _ExitTree()
    {
        GetUndoRedo().HistoryChanged -= OnHistoryChanged;
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, viewGridToggle);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, snapToggle);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, fillCellsButton);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, toleranceBox);
        viewGridToggle.QueueFree();
        snapToggle.QueueFree();
        fillCellsButton.QueueFree();
        toleranceBox.QueueFree();
        overlay.QueueFree();
    }

    void OnHistoryChanged()
    {
        if (snapToggle.ButtonPressed)
        {
            SnapSelected();
        }
    }

    public override void _Process(double delta)
    {
        if (viewGridToggle.ButtonPressed)
        {
            DrawOverlay();
        }
        else
        {
            overlay.Visible = false;
        }
    }

    void SnapSelected()
    {
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        bool moved = false;
        foreach (Node node in EditorInterface.Singleton.GetSelection().GetSelectedNodes())
        {
            if (node == root || !IsStructure(node) || node is not Node3D n)
            {
                continue;
            }
            Transform3D current = n.GlobalTransform;
            Transform3D snapped = Structure.NearestGridTransform(current, out _, out _);
            if (!current.IsEqualApprox(snapped))
            {
                // Set directly rather than as another undo step: undoing the move then returns to where it started.
                n.GlobalTransform = snapped;
                moved = true;
            }
        }
        if (moved)
        {
            EditorInterface.Singleton.MarkSceneAsUnsaved();
        }
    }

    void DrawOverlay()
    {
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root == null || root.GetParent() == null)
        {
            overlay.Visible = false;
            return;
        }
        // Lives beside the open scene, in the world the 3D viewports show.
        if (overlay.GetParent() != root.GetParent())
        {
            overlay.GetParent()?.RemoveChild(overlay);
            root.GetParent().AddChild(overlay);
        }
        overlay.Visible = true;
        lines.ClearSurfaces();

        // The floor grid, at the floor of the cell the selection stands in (a structure's root is on a cell floor).
        Vector3 centre = Vector3.Zero;
        foreach (Node node in EditorInterface.Singleton.GetSelection().GetSelectedNodes())
        {
            if (node is Node3D n)
            {
                centre = n.GlobalPosition;
                break;
            }
        }
        float size = BuildGrid.CellSize;
        int cx = Mathf.RoundToInt(centre.X / size);
        int cz = Mathf.RoundToInt(centre.Z / size);
        float y = Mathf.Floor(centre.Y / size + 0.01f) * size;
        lines.SurfaceBegin(Mesh.PrimitiveType.Lines, gridMaterial);
        for (int i = -GridRadius; i <= GridRadius; i++)
        {
            lines.SurfaceAddVertex(new Vector3((cx + i) * size, y, (cz - GridRadius) * size));
            lines.SurfaceAddVertex(new Vector3((cx + i) * size, y, (cz + GridRadius) * size));
            lines.SurfaceAddVertex(new Vector3((cx - GridRadius) * size, y, (cz + i) * size));
            lines.SurfaceAddVertex(new Vector3((cx + GridRadius) * size, y, (cz + i) * size));
        }
        lines.SurfaceEnd();

        var structures = new List<Node3D>();
        CollectStructures(root, structures);
        if (structures.Count == 0)
        {
            return;
        }
        lines.SurfaceBegin(Mesh.PrimitiveType.Lines, footprintMaterial);
        foreach (Node3D structure in structures)
        {
            AddFootprint(structure);
        }
        lines.SurfaceEnd();
    }

    /// <summary>Outlines the outside faces of a structure's cells, divided into cells.</summary>
    void AddFootprint(Node3D structure)
    {
        Variant value = structure.Get(Structure.PropertyName.cellOffsets);
        var cells = new HashSet<Vector3I>();
        if (value.VariantType == Variant.Type.Nil)
        {
            cells.Add(Vector3I.Zero);
        }
        else
        {
            foreach (Vector3I cell in value.AsGodotArray<Vector3I>())
            {
                cells.Add(cell);
            }
        }
        Transform3D transform = structure.GlobalTransform;
        float size = BuildGrid.CellSize;
        foreach (Vector3I cell in cells)
        {
            // Cell (x, y, z) spans x..x+1, y..y+1 and z-1..z from the root (see Structure).
            Vector3 low = new Vector3(cell.X, cell.Y, cell.Z - 1) * size;
            for (int a = 0; a < 3; a++)
            {
                for (int side = 0; side < 2; side++)
                {
                    Vector3I neighbour = cell + Axes[a] * (side * 2 - 1);
                    if (cells.Contains(neighbour))
                    {
                        continue;
                    }
                    // The face's four corners, going round, on the two axes other than a.
                    int u = (a + 1) % 3;
                    int v = (a + 2) % 3;
                    Vector3 c0 = FaceCorner(low, size, a, side, u, 0, v, 0);
                    Vector3 c1 = FaceCorner(low, size, a, side, u, 1, v, 0);
                    Vector3 c2 = FaceCorner(low, size, a, side, u, 1, v, 1);
                    Vector3 c3 = FaceCorner(low, size, a, side, u, 0, v, 1);
                    AddLine(transform * c0, transform * c1);
                    AddLine(transform * c1, transform * c2);
                    AddLine(transform * c2, transform * c3);
                    AddLine(transform * c3, transform * c0);
                }
            }
        }
    }

    static Vector3 FaceCorner(Vector3 low, float size, int a, int side, int u, int uStep, int v, int vStep)
    {
        Vector3 p = low;
        p[a] += side * size;
        p[u] += uStep * size;
        p[v] += vStep * size;
        return p;
    }

    void AddLine(Vector3 from, Vector3 to)
    {
        lines.SurfaceAddVertex(from);
        lines.SurfaceAddVertex(to);
    }

    static void CollectStructures(Node node, List<Node3D> structures)
    {
        if (node is Node3D n && IsStructure(node))
        {
            structures.Add(n);
        }
        foreach (Node child in node.GetChildren())
        {
            CollectStructures(child, structures);
        }
    }

    void FillCells()
    {
        // How far a collider may poke into a cell without claiming it, in metres.
        float tolerance = (float)toleranceBox.Value;
        var targets = new List<Node3D>();
        foreach (Node node in EditorInterface.Singleton.GetSelection().GetSelectedNodes())
        {
            if (node is Node3D n && IsStructure(node))
            {
                targets.Add(n);
            }
        }
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (targets.Count == 0 && root is Node3D r && IsStructure(root))
        {
            targets.Add(r);
        }
        if (targets.Count == 0)
        {
            GD.Print("Fill Cells: select a Structure, or open a structure's scene.");
            return;
        }

        EditorUndoRedoManager undo = GetUndoRedo();
        undo.CreateAction("Fill Structure cellOffsets");
        foreach (Node3D structure in targets)
        {
            var cells = new HashSet<Vector3I>();
            cells.Add(Vector3I.Zero); // the anchor cell always counts
            CollectColliderCells(structure, structure.GlobalTransform.AffineInverse(), tolerance, cells);

            // Stable order: bottom layer first, back to front, left to right.
            var sorted = new List<Vector3I>(cells);
            sorted.Sort(CompareCells);
            var offsets = new Godot.Collections.Array<Vector3I>();
            foreach (Vector3I cell in sorted)
            {
                offsets.Add(cell);
                if (cell.X < 0 || cell.Y < 0 || cell.Z > 0)
                {
                    GD.PushWarning($"Fill Cells: {structure.Name} has a collider in cell {cell}, outside the +X +Y -Z frame (see Structure).");
                }
            }
            undo.AddDoProperty(structure, Structure.PropertyName.cellOffsets, offsets);
            undo.AddUndoProperty(structure, Structure.PropertyName.cellOffsets, structure.Get(Structure.PropertyName.cellOffsets));
            GD.Print($"Fill Cells: {structure.Name} now claims {offsets.Count} cell(s).");
        }
        undo.CommitAction();
    }

    static int CompareCells(Vector3I a, Vector3I b)
    {
        if (a.Y != b.Y) return a.Y.CompareTo(b.Y);
        if (a.Z != b.Z) return b.Z.CompareTo(a.Z);
        return a.X.CompareTo(b.X);
    }

    // Adds the cells touched by every solid collider at or under node. A Box3DBody's own shape counts only when it
    // has no Box3DCollisionShape children (those replace it); sensors and disabled bodies don't count.
    static void CollectColliderCells(Node node, Transform3D toRoot, float tolerance, HashSet<Vector3I> cells)
    {
        bool isShape = node.IsClass("Box3DCollisionShape") && node.GetParent() != null && IsSolidBody(node.GetParent());
        bool isBodyShape = node.IsClass("Box3DBody") && IsSolidBody(node) && !HasShapeChildren(node);
        if ((isShape || isBodyShape) && node is Node3D n && TryGetShapeBounds(n, out Aabb local))
        {
            AddCells(toRoot * n.GlobalTransform, local, tolerance, cells);
        }
        foreach (Node child in node.GetChildren())
        {
            CollectColliderCells(child, toRoot, tolerance, cells);
        }
    }

    static bool IsSolidBody(Node body)
    {
        return body.IsClass("Box3DBody") && body.Get(Box3DNames.enabled).AsBool() && !body.Get(Box3DNames.isSensor).AsBool();
    }

    static bool HasShapeChildren(Node body)
    {
        foreach (Node child in body.GetChildren())
        {
            if (child.IsClass("Box3DCollisionShape"))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>The bounding box of a body's or collision shape's own shape, in that node's frame.</summary>
    static bool TryGetShapeBounds(Node3D node, out Aabb bounds)
    {
        bounds = new Aabb();
        switch ((ShapeTypeEnum)node.Get(Box3DNames.shapeType).AsInt32())
        {
            case ShapeTypeEnum.Box:
                Vector3 size = node.Get(Box3DNames.boxSize).AsVector3();
                bounds = new Aabb(-size / 2, size);
                break;
            case ShapeTypeEnum.Sphere:
                float r = node.Get(Box3DNames.sphereRadius).AsSingle();
                bounds = new Aabb(new Vector3(-r, -r, -r), new Vector3(2 * r, 2 * r, 2 * r));
                break;
            case ShapeTypeEnum.Capsule:
            case ShapeTypeEnum.Cylinder:
            case ShapeTypeEnum.Cone:
                float radius = node.Get(Box3DNames.capsuleRadius).AsSingle();
                float height = node.Get(Box3DNames.capsuleHeight).AsSingle();
                bounds = new Aabb(new Vector3(-radius, -height / 2, -radius), new Vector3(2 * radius, height, 2 * radius));
                break;
            case ShapeTypeEnum.Hull:
            case ShapeTypeEnum.Mesh:
            case ShapeTypeEnum.FitMesh:
                if (!TryGetMeshBounds(node, out bounds))
                {
                    return false;
                }
                break;
            default:
                return false;
        }
        return bounds.Size.LengthSquared() > 0.000001f;
    }

    // A mesh collider's source: its collision_mesh, else raw mesh_vertices, else its first MeshInstance3D child.
    static bool TryGetMeshBounds(Node3D node, out Aabb bounds)
    {
        bounds = new Aabb();
        Mesh mesh = node.Get(Box3DNames.collisionMesh).As<Mesh>();
        if (mesh != null)
        {
            bounds = mesh.GetAabb();
            return true;
        }
        Vector3[] vertices = node.Get(Box3DNames.meshVertices).AsVector3Array();
        if (vertices.Length > 0)
        {
            bounds = new Aabb(vertices[0], Vector3.Zero);
            foreach (Vector3 v in vertices)
            {
                bounds = bounds.Expand(v);
            }
            return true;
        }
        foreach (Node child in node.GetChildren())
        {
            if (child is MeshInstance3D m && m.Mesh != null)
            {
                bounds = m.Transform * m.Mesh.GetAabb();
                return true;
            }
        }
        return false;
    }

    // Every cell a shape's box reaches more than toleranceMetres into. The box is the shape's own bounds (local, in
    // the shape's frame) placed by shapeToRoot, so a tilted collider (a slope's wall) claims only the cells it
    // actually passes through, not everything inside its tilted outline. Cell (x, y, z) spans x..x+1, y..y+1 and
    // z-1..z (see Structure), so a point at z lies in cell floor(z) + 1.
    static void AddCells(Transform3D shapeToRoot, Aabb local, float toleranceMetres, HashSet<Vector3I> cells)
    {
        // Candidate cells: those the root-aligned bounds reach into, which holds every cell the box itself does.
        Aabb bounds = shapeToRoot * local;
        float size = BuildGrid.CellSize;
        Vector3 low = bounds.Position / size;
        Vector3 high = bounds.End / size;
        float tolerance = toleranceMetres / size;
        int x0 = CellIndex(low.X, high.X, tolerance, true);
        int x1 = CellIndex(low.X, high.X, tolerance, false);
        int y0 = CellIndex(low.Y, high.Y, tolerance, true);
        int y1 = CellIndex(low.Y, high.Y, tolerance, false);
        int z0 = CellIndex(low.Z, high.Z, tolerance, true) + 1;
        int z1 = CellIndex(low.Z, high.Z, tolerance, false) + 1;

        // The box as a centre and three half-axes in the root's frame, each pulled in by the tolerance, so it only
        // touches a cell if the full box reaches more than the tolerance into it. A side thinner than twice the
        // tolerance (a floor pad) shrinks to its middle.
        Vector3 centre = shapeToRoot * local.GetCenter();
        Vector3[] halfAxes = new Vector3[3];
        for (int i = 0; i < 3; i++)
        {
            Vector3 column = shapeToRoot.Basis[i];
            float half = column.Length() * local.Size[i] / 2f;
            halfAxes[i] = column.Normalized() * Mathf.Max(half - toleranceMetres, 0f);
        }

        for (int x = x0; x <= x1; x++)
        {
            for (int y = y0; y <= y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    Vector3 cellCentre = new Vector3(x + 0.5f, y + 0.5f, z - 0.5f) * size;
                    if (BoxTouchesCell(centre, halfAxes, cellCentre, size / 2f))
                    {
                        cells.Add(new Vector3I(x, y, z));
                    }
                }
            }
        }
    }

    // Whether a rotated box (centre and three half-axes) touches a cube (centre and half its side), both in the
    // same frame. Separating axis test: they're apart only if some axis has their shadows apart, and for two boxes
    // the axes to try are the cube's three, the box's three, and each cube axis crossed with each box axis.
    static bool BoxTouchesCell(Vector3 centre, Vector3[] halfAxes, Vector3 cellCentre, float cellHalf)
    {
        Vector3[] cellAxes = [Vector3.Right, Vector3.Up, Vector3.Back];
        var axes = new List<Vector3>();
        axes.AddRange(cellAxes);
        foreach (Vector3 halfAxis in halfAxes)
        {
            if (halfAxis.LengthSquared() > 0.0000001f)
            {
                axes.Add(halfAxis.Normalized());
            }
        }
        foreach (Vector3 cellAxis in cellAxes)
        {
            foreach (Vector3 halfAxis in halfAxes)
            {
                Vector3 cross = cellAxis.Cross(halfAxis);
                if (cross.LengthSquared() > 0.0000001f)
                {
                    axes.Add(cross.Normalized());
                }
            }
        }

        Vector3 between = centre - cellCentre;
        foreach (Vector3 axis in axes)
        {
            float boxReach = Mathf.Abs(halfAxes[0].Dot(axis)) + Mathf.Abs(halfAxes[1].Dot(axis)) + Mathf.Abs(halfAxes[2].Dot(axis));
            float cellReach = cellHalf * (Mathf.Abs(axis.X) + Mathf.Abs(axis.Y) + Mathf.Abs(axis.Z));
            if (Mathf.Abs(between.Dot(axis)) > boxReach + cellReach + 0.0001f)
            {
                return false;
            }
        }
        return true;
    }

    // The first (or last) whole-cell index a span low..high covers once trimmed by tolerance at each end; a span
    // thinner than that (a floor pad) uses its middle.
    static int CellIndex(float low, float high, float tolerance, bool first)
    {
        float a = low + tolerance;
        float b = high - tolerance;
        if (a > b)
        {
            a = (low + high) / 2;
            b = a;
        }
        if (first)
        {
            return Mathf.FloorToInt(a);
        }
        return Mathf.FloorToInt(b);
    }

    /// <summary>True if <paramref name="node"/> carries the Structure script or one derived from it (placeholders included).</summary>
    static bool IsStructure(Node node)
    {
        for (Script s = node.GetScript().As<Script>(); s != null; s = s.GetBaseScript())
        {
            if (structureScriptPaths.Contains(s.ResourcePath))
            {
                return true;
            }
        }
        return false;
    }

    static HashSet<string> StructureScriptPaths()
    {
        var paths = new HashSet<string>();
        foreach (ScriptPathAttribute attribute in typeof(Structure).GetCustomAttributes<ScriptPathAttribute>(false))
        {
            paths.Add(attribute.Path);
        }
        return paths;
    }
}
#endif
