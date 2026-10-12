#if TOOLS
using Godot;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Tools on the 3D editor's toolbar for making and placing <see cref="Structure"/>s:
/// <list type="bullet">
/// <item>View Grid draws the build grid's 1 m cells on a floor around the selection, and outlines the cells every
/// Structure in the open scene is made of (its <see cref="Structure.cells"/>), the scene's own root included when
/// editing a structure. Faces tagged as an input are filled green and as an output orange.</item>
/// <item>Grid Snap moves every selected Structure to the grid pose nearest to where a move or rotation ends
/// (<see cref="Structure.NearestGridTransform"/>), so hand-placed ones pass the runtime alignment check. The open
/// scene's root is never moved, so editing a structure's own scene is unaffected.</item>
/// <item>Fill Cells sets the selected Structures' cells (or the open structure scene's root's, with nothing
/// selected) to every cell their solid colliders touch: each collider's bounding box, read in the root's frame.
/// Cells that stay keep their tags. The box beside it is the overhang tolerance: a collider must reach further than
/// that into a cell to claim it.</item>
/// <item>Edit Cells: click a structure's cell in the viewport to select it (a click that hits none selects the
/// anchor), then use the Structure Cells dock to put it in or out of the structure, tag its faces, and step to a
/// neighbouring cell. Each change is written to the structure's cells straight away, as an undoable step.</item>
/// </list>
/// Structure isn't a [Tool] script, so in the editor its nodes carry a placeholder: they are found by script
/// inheritance and their cells are read and written through Get() and Set().
/// </summary>
[Tool]
public partial class StructureToolsPlugin : EditorPlugin
{
    /// <summary>How many cells the drawn floor grid reaches out from the selection in each direction.</summary>
    const int GridRadius = 12;
    /// <summary>How far tagged faces and the selected cell's outline sit inside their cell, so they don't hide the footprint's outline.</summary>
    const float Inset = 0.06f;

    static readonly Vector3I[] Axes = [Vector3I.Right, Vector3I.Up, Vector3I.Back];
    static readonly string[] FaceNames = ["Front", "Back", "Left", "Right", "Up", "Down"];

    // Partial classes get one [ScriptPath] per file; the generator tags each with the script's path.
    static readonly HashSet<string> structureScriptPaths = StructureScriptPaths();

    CheckButton viewGridToggle;
    CheckButton snapToggle;
    Button fillCellsButton;
    SpinBox toleranceBox;
    CheckButton editCellsToggle;
    MeshInstance3D overlay;
    ImmediateMesh lines;
    StandardMaterial3D gridMaterial;
    StandardMaterial3D footprintMaterial;
    StandardMaterial3D inputMaterial;
    StandardMaterial3D outputMaterial;
    StandardMaterial3D selectedMaterial;

    // The Structure Cells dock.
    EditorDock cellDock;
    Label cellLabel;
    CheckBox inStructureBox;
    readonly CheckBox[] inputBoxes = new CheckBox[6];
    readonly CheckBox[] outputBoxes = new CheckBox[6];
    readonly List<Button> stepButtons = new();

    /// <summary>The structure Edit Cells works on (the selected one), or null.</summary>
    Node3D editTarget;
    /// <summary>The selected cell of <see cref="editTarget"/>, in its cells' frame. It needn't be one of its cells.</summary>
    Vector3I selectedCell;

    public override void _EnterTree()
    {
        viewGridToggle = new CheckButton
        {
            Text = "View Grid",
            TooltipText = "Show the build grid's cells around the selection, and the cells each Structure is made of.",
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
            TooltipText = "Set the selected Structures' cells (or the open structure scene's) to every cell their solid colliders touch.",
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
        editCellsToggle = new CheckButton
        {
            Text = "Edit Cells",
            TooltipText = "Click a cell of the selected Structure to edit it in the Structure Cells dock.",
        };
        editCellsToggle.Toggled += OnEditCellsToggled;
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, viewGridToggle);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, snapToggle);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, fillCellsButton);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, toleranceBox);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, editCellsToggle);
        // A move records its undo step when it ends (a gizmo drag released, a keyboard move confirmed, an inspector
        // edit), so snapping then leaves the drag itself free.
        GetUndoRedo().HistoryChanged += OnHistoryChanged;

        gridMaterial = OverlayMaterial(new Color(1f, 1f, 1f, 0.25f), false);
        // The rest draw through everything so a structure's cells show inside its model.
        footprintMaterial = OverlayMaterial(new Color(0.85f, 0.85f, 0.85f, 0.6f), true);
        inputMaterial = OverlayMaterial(new Color(StructureArrow.InputColor, 0.45f), true);
        outputMaterial = OverlayMaterial(new Color(StructureArrow.OutputColor, 0.45f), true);
        selectedMaterial = OverlayMaterial(new Color(1f, 0.95f, 0.2f, 1f), true);
        lines = new ImmediateMesh();
        // Has no owner, so it's never saved into the scene it's drawn over.
        overlay = new MeshInstance3D
        {
            Name = "StructureToolsOverlay",
            Mesh = lines,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };

        BuildCellDock();
        AddDock(cellDock);
    }

    public override void _ExitTree()
    {
        GetUndoRedo().HistoryChanged -= OnHistoryChanged;
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, viewGridToggle);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, snapToggle);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, fillCellsButton);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, toleranceBox);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, editCellsToggle);
        RemoveDock(cellDock);
        viewGridToggle.QueueFree();
        snapToggle.QueueFree();
        fillCellsButton.QueueFree();
        toleranceBox.QueueFree();
        editCellsToggle.QueueFree();
        cellDock.QueueFree();
        overlay.QueueFree();
    }

    static StandardMaterial3D OverlayMaterial(Color color, bool throughEverything)
    {
        return new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            NoDepthTest = throughEverything,
            AlbedoColor = color,
        };
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
        if (editTarget != null && !IsInstanceValid(editTarget))
        {
            editTarget = null;
        }
        RefreshCellDock();
        if (viewGridToggle.ButtonPressed || editTarget != null)
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

    // ---- drawing ----

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
        var grid = new List<Vector3>();
        for (int i = -GridRadius; i <= GridRadius; i++)
        {
            grid.Add(new Vector3((cx + i) * size, y, (cz - GridRadius) * size));
            grid.Add(new Vector3((cx + i) * size, y, (cz + GridRadius) * size));
            grid.Add(new Vector3((cx - GridRadius) * size, y, (cz + i) * size));
            grid.Add(new Vector3((cx + GridRadius) * size, y, (cz + i) * size));
        }
        AddSurface(Mesh.PrimitiveType.Lines, gridMaterial, grid);

        var structures = new List<Node3D>();
        CollectStructures(root, structures);
        var footprints = new List<Vector3>();
        var inputFaces = new List<Vector3>();
        var outputFaces = new List<Vector3>();
        foreach (Node3D structure in structures)
        {
            Godot.Collections.Dictionary<Vector3I, CellTags> cells = CellsOf(structure);
            AddFootprint(structure.GlobalTransform, cells, footprints);
            foreach (KeyValuePair<Vector3I, CellTags> cell in cells)
            {
                for (int face = 0; face < 6; face++)
                {
                    if ((cell.Value & Structure.InputTag(face)) != 0)
                    {
                        AddFace(structure.GlobalTransform, cell.Key, face, inputFaces);
                    }
                    if ((cell.Value & Structure.OutputTag(face)) != 0)
                    {
                        AddFace(structure.GlobalTransform, cell.Key, face, outputFaces);
                    }
                }
            }
        }
        AddSurface(Mesh.PrimitiveType.Lines, footprintMaterial, footprints);
        AddSurface(Mesh.PrimitiveType.Triangles, inputMaterial, inputFaces);
        AddSurface(Mesh.PrimitiveType.Triangles, outputMaterial, outputFaces);

        if (editTarget != null)
        {
            var selected = new List<Vector3>();
            AddBox(editTarget.GlobalTransform, selectedCell, selected);
            AddSurface(Mesh.PrimitiveType.Lines, selectedMaterial, selected);
        }
    }

    // An ImmediateMesh refuses an empty surface, so one is only added when it has something in it.
    void AddSurface(Mesh.PrimitiveType primitive, Material material, List<Vector3> vertices)
    {
        if (vertices.Count == 0)
        {
            return;
        }
        lines.SurfaceBegin(primitive, material);
        foreach (Vector3 v in vertices)
        {
            lines.SurfaceAddVertex(v);
        }
        lines.SurfaceEnd();
    }

    // Where cell (x, y, z) starts: it spans x..x+1, y..y+1 and z-1..z from the root (see Structure).
    static Vector3 CellLow(Vector3I cell)
    {
        return new Vector3(cell.X, cell.Y, cell.Z - 1) * BuildGrid.CellSize;
    }

    /// <summary>Outlines the outside faces of a structure's cells, divided into cells.</summary>
    static void AddFootprint(Transform3D transform, Godot.Collections.Dictionary<Vector3I, CellTags> cells, List<Vector3> output)
    {
        float size = BuildGrid.CellSize;
        foreach (Vector3I cell in cells.Keys)
        {
            Vector3 low = CellLow(cell);
            for (int a = 0; a < 3; a++)
            {
                for (int side = 0; side < 2; side++)
                {
                    if (cells.ContainsKey(cell + Axes[a] * (side * 2 - 1)))
                    {
                        continue;
                    }
                    // The face's four corners, going round, on the two axes other than a.
                    int u = (a + 1) % 3;
                    int v = (a + 2) % 3;
                    Vector3 c0 = low;
                    c0[a] += side * size;
                    Vector3 c1 = c0;
                    c1[u] += size;
                    Vector3 c2 = c1;
                    c2[v] += size;
                    Vector3 c3 = c0;
                    c3[v] += size;
                    output.Add(transform * c0);
                    output.Add(transform * c1);
                    output.Add(transform * c1);
                    output.Add(transform * c2);
                    output.Add(transform * c2);
                    output.Add(transform * c3);
                    output.Add(transform * c3);
                    output.Add(transform * c0);
                }
            }
        }
    }

    /// <summary>A filled square on one face of a cell (in <see cref="Structure.FaceDirections"/> order), as two triangles.</summary>
    static void AddFace(Transform3D transform, Vector3I cell, int face, List<Vector3> output)
    {
        float size = BuildGrid.CellSize;
        Vector3I normal = Structure.FaceDirections[face];
        int a = 0;
        if (normal.Y != 0) a = 1;
        if (normal.Z != 0) a = 2;
        int u = (a + 1) % 3;
        int v = (a + 2) % 3;
        Vector3 c0 = CellLow(cell);
        if (normal[a] > 0)
        {
            c0[a] += size;
        }
        c0[u] += Inset;
        c0[v] += Inset;
        Vector3 c1 = c0;
        c1[u] += size - 2 * Inset;
        Vector3 c2 = c1;
        c2[v] += size - 2 * Inset;
        Vector3 c3 = c0;
        c3[v] += size - 2 * Inset;
        output.Add(transform * c0);
        output.Add(transform * c1);
        output.Add(transform * c2);
        output.Add(transform * c0);
        output.Add(transform * c2);
        output.Add(transform * c3);
    }

    /// <summary>The twelve edges of a box a little inside a cell.</summary>
    static void AddBox(Transform3D transform, Vector3I cell, List<Vector3> output)
    {
        Vector3 low = CellLow(cell) + Vector3.One * Inset;
        Vector3 high = CellLow(cell) + Vector3.One * (BuildGrid.CellSize - Inset);
        // The eight corners: bit 0 picks high x, bit 1 high y, bit 2 high z. Edges join corners that differ in one bit.
        Vector3[] corners = new Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            Vector3 c = low;
            if ((i & 1) != 0) c.X = high.X;
            if ((i & 2) != 0) c.Y = high.Y;
            if ((i & 4) != 0) c.Z = high.Z;
            corners[i] = transform * c;
        }
        for (int i = 0; i < 8; i++)
        {
            for (int bit = 1; bit < 8; bit *= 2)
            {
                if ((i & bit) == 0)
                {
                    output.Add(corners[i]);
                    output.Add(corners[i | bit]);
                }
            }
        }
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

    // ---- Edit Cells ----

    /// <summary>A structure's cells as the placeholder holds them; just the anchor when it has never been set.</summary>
    static Godot.Collections.Dictionary<Vector3I, CellTags> CellsOf(Node structure)
    {
        Variant value = structure.Get(Structure.PropertyName.cells);
        if (value.VariantType != Variant.Type.Dictionary)
        {
            return new() { { Vector3I.Zero, CellTags.None } };
        }
        return value.AsGodotDictionary<Vector3I, CellTags>();
    }

    void BuildCellDock()
    {
        cellDock = new EditorDock { Title = "Structure Cells", DefaultSlot = EditorDock.DockSlot.RightUl };
        var column = new VBoxContainer();
        cellDock.AddChild(column);
        cellLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        column.AddChild(cellLabel);

        inStructureBox = new CheckBox { Text = "In structure", TooltipText = "Whether this cell is one of the structure's cells. The last one can't be removed." };
        inStructureBox.Toggled += SetInStructure;
        column.AddChild(inStructureBox);

        // Faces down the side, inputs and outputs across.
        var faces = new GridContainer { Columns = 3 };
        faces.AddChild(new Label());
        faces.AddChild(new Label { Text = "In" });
        faces.AddChild(new Label { Text = "Out" });
        for (int face = 0; face < 6; face++)
        {
            int f = face;
            faces.AddChild(new Label { Text = FaceNames[face] });
            inputBoxes[face] = new CheckBox { TooltipText = $"Items go in through this cell's {FaceNames[face].ToLower()} face." };
            inputBoxes[face].Toggled += on => SetTag(Structure.InputTag(f), on);
            faces.AddChild(inputBoxes[face]);
            outputBoxes[face] = new CheckBox { TooltipText = $"Items come out through this cell's {FaceNames[face].ToLower()} face." };
            outputBoxes[face].Toggled += on => SetTag(Structure.OutputTag(f), on);
            faces.AddChild(outputBoxes[face]);
        }
        column.AddChild(faces);

        column.AddChild(new Label { Text = "Select neighbour" });
        var steps = new GridContainer { Columns = 2 };
        for (int face = 0; face < 6; face++)
        {
            Vector3I direction = Structure.FaceDirections[face];
            var button = new Button { Text = $"{FaceNames[face]} {direction}" };
            button.Pressed += () => selectedCell += direction;
            steps.AddChild(button);
            stepButtons.Add(button);
        }
        column.AddChild(steps);
    }

    // Shows the selected cell's state, so undo and edits from the inspector show up too.
    void RefreshCellDock()
    {
        bool editing = editTarget != null;
        Godot.Collections.Dictionary<Vector3I, CellTags> cells = null;
        CellTags tags = CellTags.None;
        bool inStructure = false;
        if (editing)
        {
            cells = CellsOf(editTarget);
            inStructure = cells.TryGetValue(selectedCell, out tags);
            cellLabel.Text = $"{editTarget.Name}: cell {selectedCell}";
        }
        else
        {
            cellLabel.Text = "Turn on Edit Cells (3D toolbar) and select a Structure, then click one of its cells.";
        }
        inStructureBox.SetPressedNoSignal(inStructure);
        // A structure needs at least one cell.
        inStructureBox.Disabled = !editing || (inStructure && cells.Count == 1);
        for (int face = 0; face < 6; face++)
        {
            inputBoxes[face].SetPressedNoSignal((tags & Structure.InputTag(face)) != 0);
            outputBoxes[face].SetPressedNoSignal((tags & Structure.OutputTag(face)) != 0);
            inputBoxes[face].Disabled = !editing;
            outputBoxes[face].Disabled = !editing;
        }
        foreach (Button button in stepButtons)
        {
            button.Disabled = !editing;
        }
    }

    void SetInStructure(bool on)
    {
        if (editTarget == null)
        {
            return;
        }
        Godot.Collections.Dictionary<Vector3I, CellTags> cells = CellsOf(editTarget).Duplicate();
        if (!on && cells.Count == 1)
        {
            return;
        }
        if (on)
        {
            cells.TryAdd(selectedCell, CellTags.None);
        }
        else
        {
            cells.Remove(selectedCell);
        }
        CommitCells(cells, "Set structure cell");
    }

    // Tagging a face of a cell that isn't in the structure adds it.
    void SetTag(CellTags tag, bool on)
    {
        if (editTarget == null)
        {
            return;
        }
        Godot.Collections.Dictionary<Vector3I, CellTags> cells = CellsOf(editTarget).Duplicate();
        cells.TryGetValue(selectedCell, out CellTags tags);
        if (on)
        {
            tags |= tag;
        }
        else
        {
            tags &= ~tag;
        }
        cells[selectedCell] = tags;
        CommitCells(cells, "Tag structure cell face");
    }

    void CommitCells(Godot.Collections.Dictionary<Vector3I, CellTags> cells, string actionName)
    {
        EditorUndoRedoManager undo = GetUndoRedo();
        undo.CreateAction(actionName);
        undo.AddDoProperty(editTarget, Structure.PropertyName.cells, cells);
        undo.AddUndoProperty(editTarget, Structure.PropertyName.cells, editTarget.Get(Structure.PropertyName.cells));
        undo.CommitAction();
    }

    // The editor only asks which plugin handles a node when the selection changes, so re-select to start or stop.
    void OnEditCellsToggled(bool on)
    {
        EditorSelection selection = EditorInterface.Singleton.GetSelection();
        var nodes = new List<Node>(selection.GetSelectedNodes());
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (on && nodes.Count == 0 && root != null && IsStructure(root))
        {
            nodes.Add(root);
        }
        if (!on)
        {
            editTarget = null;
        }
        selection.Clear();
        foreach (Node node in nodes)
        {
            selection.AddNode(node);
        }
    }

    public override bool _Handles(GodotObject @object)
    {
        return editCellsToggle.ButtonPressed && @object is Node3D node && IsStructure(node);
    }

    public override void _Edit(GodotObject @object)
    {
        Node3D target = @object as Node3D;
        if (target != editTarget)
        {
            selectedCell = Vector3I.Zero;
        }
        editTarget = target;
    }

    public override void _MakeVisible(bool visible)
    {
        if (!visible)
        {
            editTarget = null;
        }
    }

    // A left click selects the nearest of the structure's cells under the cursor, or the anchor if it hits none.
    public override int _Forward3DGuiInput(Camera3D viewportCamera, InputEvent @event)
    {
        if (editTarget == null || @event is not InputEventMouseButton click || click.ButtonIndex != MouseButton.Left || !click.Pressed)
        {
            return (int)AfterGuiInput.Pass;
        }
        Transform3D toLocal = editTarget.GlobalTransform.AffineInverse();
        Vector3 from = toLocal * viewportCamera.ProjectRayOrigin(click.Position);
        Vector3 direction = toLocal.Basis * viewportCamera.ProjectRayNormal(click.Position);
        selectedCell = Vector3I.Zero;
        float nearest = float.MaxValue;
        foreach (Vector3I cell in CellsOf(editTarget).Keys)
        {
            var box = new Aabb(CellLow(cell), Vector3.One * BuildGrid.CellSize);
            // Of the cells the ray passes through, the one whose centre is nearest the camera.
            float distance = from.DistanceTo(box.GetCenter());
            if (distance < nearest && box.IntersectsSegment(from, from + direction * 10000f))
            {
                nearest = distance;
                selectedCell = cell;
            }
        }
        return (int)AfterGuiInput.Stop;
    }

    // ---- Fill Cells ----

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
        undo.CreateAction("Fill Structure cells");
        foreach (Node3D structure in targets)
        {
            var found = new HashSet<Vector3I>();
            CollectColliderCells(structure, structure.GlobalTransform.AffineInverse(), tolerance, found);
            if (found.Count == 0)
            {
                GD.PushWarning($"Fill Cells: {structure.Name} has no solid colliders; its cells are left as they are.");
                continue;
            }

            // Stable order: bottom layer first, back to front, left to right. Cells that stay keep their tags.
            var sorted = new List<Vector3I>(found);
            sorted.Sort(CompareCells);
            Godot.Collections.Dictionary<Vector3I, CellTags> old = CellsOf(structure);
            var cells = new Godot.Collections.Dictionary<Vector3I, CellTags>();
            foreach (Vector3I cell in sorted)
            {
                old.TryGetValue(cell, out CellTags tags);
                cells[cell] = tags;
                if (cell.X < 0 || cell.Y < 0 || cell.Z > 0)
                {
                    GD.PushWarning($"Fill Cells: {structure.Name} has a collider in cell {cell}, outside the +X +Y -Z frame (see Structure).");
                }
            }
            undo.AddDoProperty(structure, Structure.PropertyName.cells, cells);
            undo.AddUndoProperty(structure, Structure.PropertyName.cells, structure.Get(Structure.PropertyName.cells));
            GD.Print($"Fill Cells: {structure.Name} now has {cells.Count} cell(s).");
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
