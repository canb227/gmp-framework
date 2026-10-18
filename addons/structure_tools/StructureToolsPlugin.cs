#if TOOLS
using Godot;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Tools on the 3D editor's toolbar for making and placing <see cref="Structure"/>s:
/// <list type="bullet">
/// <item>View Grid draws the build grid's 1 m cells on a floor around the selection, and outlines the cells every
/// Structure in the open scene is made of (its <see cref="Structure.cells"/>), the scene's own root included when
/// editing a structure. Faces tagged as an input are filled green and as an output orange. The grid is the game's
/// exactly: drawn in the frame the game will put the scene's root in (<see cref="GameFrame"/>).</item>
/// <item>Modules marks the 2 m structure modules on that floor grid (<see cref="BuildGrid.ModuleSize"/>; modules
/// start on even cells): bold module lines and a chequerboard of modules, blue when the floor is on a module's
/// bottom (an even height) and orange when it's partway up one (odd), for lining a level up with the grid.</item>
/// <item>Blocked Cells fills the faces of the build grid cells the open scene's level geometry blocks, around the
/// selection, faint blue, the cells the game's build grid fill shows (BuildGrid.CellFill.cs): those a static, solid
/// GMPShape of a static GMPBody outside any GMPObject reaches more than <see cref="BuildGrid.LevelOverlapDepth"/> into. Cells a
/// <see cref="NoBuildVolume"/> touches are filled red. Worked out from the colliders' shapes when it's turned on and
/// after every edit, as the editor has no Box3D world to ask.</item>
/// <item>Grid Snap moves every selected Structure to the grid pose nearest to where a move or rotation ends
/// (<see cref="Structure.NearestGridTransform"/>), so hand-placed ones pass the runtime alignment check. The open
/// scene's root is never moved, so editing a structure's own scene is unaffected.</item>
/// <item>Fill Cells sets the selected Structures' cells (or the open structure scene's root's, with nothing
/// selected) to every cell their solid colliders touch: each collider's bounding box, read in the root's frame.
/// Cells that stay keep their tags. The box beside it is the overhang tolerance: a collider must reach further than
/// that into a cell to claim it. With Meshes on, every visible MeshInstance3D's bounding box claims cells the same way.</item>
/// <item>Edit Cells: click a structure's cell in the viewport to select it (a click that hits none selects the
/// anchor), then use the Structure Cells dock to put it in or out of the structure, tag its faces, and step to a
/// neighbouring cell. Shift-click adds a cell to the selection (or takes it out again), and the dock's changes and
/// steps then apply to every selected cell. Each change is written to the structure's cells straight away, as an
/// undoable step.</item>
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
    /// <summary>How far the module chequerboard sits above the floor grid, so it doesn't fight the floor under it.</summary>
    const float ModuleFillLift = 0.01f;
    /// <summary>How far Blocked Cells' faces sit out from their cell, so they don't fight level surfaces on a cell boundary.</summary>
    const float BlockedFaceLift = 0.01f;

    static readonly Vector3I[] Axes = [Vector3I.Right, Vector3I.Up, Vector3I.Back];
    static readonly Vector3[] BoxAxes = [Vector3.Right, Vector3.Up, Vector3.Back];
    static readonly string[] FaceNames = ["Front", "Back", "Left", "Right", "Up", "Down"];

    // Partial classes get one [ScriptPath] per file; the generator tags each with the script's path.
    static readonly HashSet<string> structureScriptPaths = StructureScriptPaths();
    static readonly HashSet<string> gmpObjectScriptPaths = GMPObjectScriptPaths();

    CheckButton viewGridToggle;
    CheckButton modulesToggle;
    CheckButton blockedToggle;
    CheckButton snapToggle;
    Button fillCellsButton;
    SpinBox toleranceBox;
    CheckButton fillMeshesToggle;
    CheckButton editCellsToggle;
    MeshInstance3D overlay;
    ImmediateMesh lines;
    StandardMaterial3D gridMaterial;
    StandardMaterial3D moduleLineMaterial;
    StandardMaterial3D moduleFillMaterial;
    StandardMaterial3D oddModuleLineMaterial;
    StandardMaterial3D oddModuleFillMaterial;
    StandardMaterial3D footprintMaterial;
    StandardMaterial3D inputMaterial;
    StandardMaterial3D outputMaterial;
    StandardMaterial3D selectedMaterial;
    StandardMaterial3D levelBlockedMaterial;
    StandardMaterial3D noBuildBlockedMaterial;

    // Blocked Cells. The cells are in the game's frame and numbered like a structure's (see CellLow), so one higher
    // in z than BuildGrid's. C# sets, as they're read for every face drawn; a script reload drops them (null) and
    // they're worked out again.
    MeshInstance3D blockedOverlay;
    HashSet<Vector3I> levelBlocked;
    HashSet<Vector3I> noBuildBlocked;
    /// <summary>The scene root <see cref="levelBlocked"/> was worked out for.</summary>
    Node blockedFor;
    /// <summary>The cell <see cref="blockedOverlay"/>'s mesh was built around; it's rebuilt when the selection leaves it.</summary>
    Vector3I blockedMeshCentre;
    bool blockedMeshStale = true;

    // The Structure Cells dock.
    EditorDock cellDock;
    Label cellLabel;
    CheckBox inStructureBox;
    // Godot arrays rather than C# ones: rebuilding the C# project reloads this script and keeps only fields Godot can
    // store, without running _EnterTree again.
    Godot.Collections.Array<CheckBox> inputBoxes = new();
    Godot.Collections.Array<CheckBox> outputBoxes = new();
    Godot.Collections.Array<Button> stepButtons = new();

    /// <summary>The structure Edit Cells works on (the selected one), or null.</summary>
    Node3D editTarget;
    /// <summary>The last clicked cell of <see cref="editTarget"/>, in its cells' frame; the dock shows its state. It needn't be one of its cells.</summary>
    Vector3I selectedCell;
    /// <summary>Every selected cell, <see cref="selectedCell"/> among them. The dock's changes apply to all of them.</summary>
    Godot.Collections.Array<Vector3I> selectedCells = new();

    public override void _EnterTree()
    {
        viewGridToggle = new CheckButton
        {
            Text = "View Grid",
            TooltipText = "Show the build grid's cells around the selection, and the cells each Structure is made of.",
        };
        modulesToggle = new CheckButton
        {
            Text = "Modules",
            TooltipText = "Mark the 2 m structure modules (which start on even cells) on the View Grid floor: bold lines and a chequerboard,\nblue when the floor is at an even height, orange when it's at an odd one (partway up a module).",
        };
        blockedToggle = new CheckButton
        {
            Text = "Blocked Cells",
            TooltipText = "Fill the faces of the build grid cells around the selection that level geometry blocks (blue)\nor a NoBuildVolume touches (red), as the game's build grid shows them.",
        };
        blockedToggle.Toggled += OnBlockedToggled;
        snapToggle = new CheckButton
        {
            Text = "Grid Snap",
            TooltipText = "When a move ends, put selected Structures on the build grid (nearest cell corner, quarter-turn yaw).",
            ButtonPressed = true,
        };
        fillCellsButton = new Button
        {
            Text = "Fill Cells",
            TooltipText = "Set the selected Structures' cells (or the open structure scene's) to every cell their solid colliders (and meshes, with Meshes on) touch.",
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
        fillMeshesToggle = new CheckButton
        {
            Text = "Meshes",
            TooltipText = "Fill Cells also counts the cells each visible mesh's bounding box covers, not just colliders.",
        };
        editCellsToggle = new CheckButton
        {
            Text = "Edit Cells",
            TooltipText = "Click a cell of the selected Structure to edit it in the Structure Cells dock.",
        };
        editCellsToggle.Toggled += OnEditCellsToggled;
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, viewGridToggle);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, modulesToggle);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, blockedToggle);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, snapToggle);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, fillCellsButton);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, toleranceBox);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, fillMeshesToggle);
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, editCellsToggle);
        // A move records its undo step when it ends (a gizmo drag released, a keyboard move confirmed, an inspector
        // edit), so snapping then leaves the drag itself free.
        GetUndoRedo().HistoryChanged += OnHistoryChanged;

        gridMaterial = OverlayMaterial(new Color(1f, 1f, 1f, 0.25f), false);
        moduleLineMaterial = OverlayMaterial(new Color(0.55f, 0.85f, 1f, 0.8f), false);
        moduleFillMaterial = OverlayMaterial(new Color(0.55f, 0.85f, 1f, 0.15f), false);
        oddModuleLineMaterial = OverlayMaterial(new Color(1f, 0.6f, 0.2f, 0.8f), false);
        oddModuleFillMaterial = OverlayMaterial(new Color(1f, 0.6f, 0.2f, 0.15f), false);
        // The rest draw through everything so a structure's cells show inside its model.
        footprintMaterial = OverlayMaterial(new Color(0.85f, 0.85f, 0.85f, 0.6f), true);
        inputMaterial = OverlayMaterial(new Color(StructureArrow.InputColor, 0.45f), true);
        outputMaterial = OverlayMaterial(new Color(StructureArrow.OutputColor, 0.45f), true);
        selectedMaterial = OverlayMaterial(new Color(1f, 0.95f, 0.2f, 1f), true);
        // The fill's colours (BuildGridFill.gdshader), a little stronger as nothing lights them here.
        levelBlockedMaterial = OverlayMaterial(new Color(0.66f, 0.86f, 1f, 0.2f), false);
        noBuildBlockedMaterial = OverlayMaterial(new Color(1f, 0.32f, 0.25f, 0.25f), false);
        lines = new ImmediateMesh();
        // Has no owner, so it's never saved into the scene it's drawn over.
        overlay = new MeshInstance3D
        {
            Name = "StructureToolsOverlay",
            Mesh = lines,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        blockedOverlay = new MeshInstance3D
        {
            Name = "StructureToolsBlockedCells",
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
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, modulesToggle);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, blockedToggle);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, snapToggle);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, fillCellsButton);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, toleranceBox);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, fillMeshesToggle);
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, editCellsToggle);
        RemoveDock(cellDock);
        viewGridToggle.QueueFree();
        modulesToggle.QueueFree();
        blockedToggle.QueueFree();
        snapToggle.QueueFree();
        fillCellsButton.QueueFree();
        toleranceBox.QueueFree();
        fillMeshesToggle.QueueFree();
        editCellsToggle.QueueFree();
        cellDock.QueueFree();
        overlay.QueueFree();
        blockedOverlay.QueueFree();
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
        // Any edit may have moved or changed level geometry: work the blocked cells out again when next drawn.
        levelBlocked = null;
    }

    // Turning it on works the cells out afresh, so it also picks up changes no edit records (a reimported mesh).
    void OnBlockedToggled(bool on)
    {
        levelBlocked = null;
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
        if (blockedToggle.ButtonPressed)
        {
            DrawBlockedCells();
        }
        else
        {
            blockedOverlay.Visible = false;
        }
    }

    void SnapSelected()
    {
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        Transform3D frame = GameFrame(root);
        bool moved = false;
        foreach (Node node in EditorInterface.Singleton.GetSelection().GetSelectedNodes())
        {
            if (node == root || !IsStructure(node) || node is not Node3D n)
            {
                continue;
            }
            // On the grid as the game will see it, in the root's frame.
            Transform3D current = frame.AffineInverse() * n.GlobalTransform;
            Transform3D snapped = Structure.NearestGridTransform(current, out _, out _);
            if (!current.IsEqualApprox(snapped))
            {
                // Set directly rather than as another undo step: undoing the move then returns to where it started.
                n.GlobalTransform = frame * snapped;
                moved = true;
            }
        }
        if (moved)
        {
            EditorInterface.Singleton.MarkSceneAsUnsaved();
        }
    }

    /// <summary>
    /// Where the game's world frame is in the editor: the open scene's root, unscaled. The game spawns a level (or any
    /// scene) with its root at the world origin, unturned, whatever position and rotation the root has here (it keeps
    /// its scale; GameWorld._SpawnInternal), so the build grid is drawn and snapped to in this frame and lines up with
    /// the game's exactly.
    /// </summary>
    static Transform3D GameFrame(Node root)
    {
        if (root is Node3D root3D)
        {
            Transform3D rootTransform = root3D.GlobalTransform;
            return new Transform3D(rootTransform.Basis.Orthonormalized(), rootTransform.Origin);
        }
        return Transform3D.Identity;
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

        // The floor grid, at the floor of the cell the selection stands in (a structure's root is on a cell floor),
        // reaching a whole number of modules out from the module it's in. Drawn in the game's frame (GameFrame).
        Transform3D frame = GameFrame(root);
        Vector3 centre = SelectionCentre(frame);
        float size = BuildGrid.CellSize;
        int cx = Mathf.FloorToInt(centre.X / BuildGrid.ModuleSize) * BuildGrid.ModuleCells;
        int cz = Mathf.FloorToInt(centre.Z / BuildGrid.ModuleSize) * BuildGrid.ModuleCells;
        int floorCell = Mathf.FloorToInt(centre.Y / size + 0.01f);
        float y = floorCell * size;
        bool showModules = modulesToggle.ButtonPressed;
        var grid = new List<Vector3>();
        var moduleLines = new List<Vector3>();
        for (int i = -GridRadius; i <= GridRadius; i++)
        {
            // cx and cz are even, so line i is on a module edge when i is.
            List<Vector3> output = grid;
            if (showModules && Mathf.PosMod(i, BuildGrid.ModuleCells) == 0)
            {
                output = moduleLines;
            }
            output.Add(frame * new Vector3((cx + i) * size, y, (cz - GridRadius) * size));
            output.Add(frame * new Vector3((cx + i) * size, y, (cz + GridRadius) * size));
            output.Add(frame * new Vector3((cx - GridRadius) * size, y, (cz + i) * size));
            output.Add(frame * new Vector3((cx + GridRadius) * size, y, (cz + i) * size));
        }
        AddSurface(Mesh.PrimitiveType.Lines, gridMaterial, grid);

        if (showModules)
        {
            // Every other module filled, like a chequerboard, so a cell's module (and whether it's an even or odd
            // cell in it) can be read at a glance. Orange when the floor is at an odd height, partway up a module.
            var checker = new List<Vector3>();
            float lifted = y + ModuleFillLift;
            for (int mx = -GridRadius; mx < GridRadius; mx += BuildGrid.ModuleCells)
            {
                for (int mz = -GridRadius; mz < GridRadius; mz += BuildGrid.ModuleCells)
                {
                    int x0 = cx + mx, z0 = cz + mz;
                    if (Mathf.PosMod(x0 / BuildGrid.ModuleCells + z0 / BuildGrid.ModuleCells, 2) != 0)
                    {
                        continue;
                    }
                    int x1 = x0 + BuildGrid.ModuleCells, z1 = z0 + BuildGrid.ModuleCells;
                    Vector3 c0 = frame * new Vector3(x0 * size, lifted, z0 * size);
                    Vector3 c1 = frame * new Vector3(x1 * size, lifted, z0 * size);
                    Vector3 c2 = frame * new Vector3(x1 * size, lifted, z1 * size);
                    Vector3 c3 = frame * new Vector3(x0 * size, lifted, z1 * size);
                    checker.AddRange([c0, c1, c2, c0, c2, c3]);
                }
            }
            if (Mathf.PosMod(floorCell, BuildGrid.ModuleCells) == 0)
            {
                AddSurface(Mesh.PrimitiveType.Lines, moduleLineMaterial, moduleLines);
                AddSurface(Mesh.PrimitiveType.Triangles, moduleFillMaterial, checker);
            }
            else
            {
                AddSurface(Mesh.PrimitiveType.Lines, oddModuleLineMaterial, moduleLines);
                AddSurface(Mesh.PrimitiveType.Triangles, oddModuleFillMaterial, checker);
            }
        }

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
                        AddFace(structure.GlobalTransform, cell.Key, face, 0f, inputFaces);
                    }
                    if ((cell.Value & Structure.OutputTag(face)) != 0)
                    {
                        AddFace(structure.GlobalTransform, cell.Key, face, 0f, outputFaces);
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
            foreach (Vector3I cell in selectedCells)
            {
                AddBox(editTarget.GlobalTransform, cell, selected);
            }
            AddSurface(Mesh.PrimitiveType.Lines, selectedMaterial, selected);
        }
    }

    /// <summary>Where the first selected Node3D is, in the game's frame; the origin with none selected.</summary>
    static Vector3 SelectionCentre(Transform3D frame)
    {
        foreach (Node node in EditorInterface.Singleton.GetSelection().GetSelectedNodes())
        {
            if (node is Node3D n)
            {
                return frame.AffineInverse() * n.GlobalPosition;
            }
        }
        return Vector3.Zero;
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

    /// <summary>
    /// A filled square on one face of a cell (in <see cref="Structure.FaceDirections"/> order), as two triangles,
    /// moved <paramref name="lift"/> out from the cell.
    /// </summary>
    static void AddFace(Transform3D transform, Vector3I cell, int face, float lift, List<Vector3> output)
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
        c0[a] += normal[a] * lift;
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

    // ---- Blocked Cells ----

    // Keeps the blocked cells worked out for the open scene and their faces' mesh built around the selection. The
    // mesh is in the game's frame and the overlay is put in that frame, so moving the scene's root needs no rebuild.
    void DrawBlockedCells()
    {
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root == null || root.GetParent() == null)
        {
            blockedOverlay.Visible = false;
            return;
        }
        if (blockedOverlay.GetParent() != root.GetParent())
        {
            blockedOverlay.GetParent()?.RemoveChild(blockedOverlay);
            root.GetParent().AddChild(blockedOverlay);
        }
        if (levelBlocked == null || noBuildBlocked == null || blockedFor != root)
        {
            FindBlockedCells(root);
        }

        Transform3D frame = GameFrame(root);
        Vector3 centre = SelectionCentre(frame) / BuildGrid.CellSize;
        // The cell the selection is in; a point at z lies in cell floor(z) + 1 (see AddCells).
        var centreCell = new Vector3I(Mathf.FloorToInt(centre.X), Mathf.FloorToInt(centre.Y), Mathf.FloorToInt(centre.Z) + 1);
        if (blockedMeshStale || centreCell != blockedMeshCentre)
        {
            BuildBlockedMesh(centreCell);
        }
        blockedOverlay.GlobalTransform = frame;
        blockedOverlay.Visible = true;
    }

    void FindBlockedCells(Node root)
    {
        levelBlocked = new HashSet<Vector3I>();
        noBuildBlocked = new HashSet<Vector3I>();
        blockedFor = root;
        blockedMeshStale = true;
        CollectBlockedCells(root, GameFrame(root).AffineInverse(), false);
    }

    // Adds the cells level geometry blocks (as PlacementProbe.IsStaticLevelCollider judges level geometry) and the
    // cells no-build volumes touch, at or under node. inObject: node is in a GMPObject (a structure, an item...),
    // whose colliders aren't level geometry.
    void CollectBlockedCells(Node node, Transform3D toFrame, bool inObject)
    {
        if (node is NoBuildVolume volume)
        {
            AddNoBuildCells(toFrame * volume.GlobalTransform * new Aabb(-volume.Size / 2, volume.Size), noBuildBlocked);
        }
        if (IsGMPObject(node))
        {
            inObject = true;
        }
        // Every solid GMPShape of a static body counts.
        bool isShape = IsSolidShape(node) && IsStaticSolidBody(BodyOf(node));
        if (!inObject && isShape && node is Node3D n)
        {
            // A mesh collider is a surface: Box3D tests a query box against its triangles, so it blocks the cells its
            // triangles pass through, not everything inside its bounds.
            bool isMesh = n.Get(shapeName).AsInt32() == MeshShape;
            if (isMesh && TryGetMeshTriangles(n, out Transform3D meshTransform, out Vector3[] triangles))
            {
                AddTriangleCells(toFrame * meshTransform, triangles, levelBlocked);
            }
            else if (TryGetShapeBounds(n, out Aabb local))
            {
                AddCells(toFrame * n.GlobalTransform, local, BuildGrid.LevelOverlapDepth, levelBlocked);
            }
        }
        foreach (Node child in node.GetChildren())
        {
            CollectBlockedCells(child, toFrame, inObject);
        }
    }

    static bool IsStaticSolidBody(Node body)
    {
        return IsSolidBody(body) && body.Get(bodyTypeName).AsInt32() == (int)BodyTypeEnum.Static;
    }

    // A mesh collider's triangles (a vertex list, three to a triangle) and the frame they're in: its mesh, else its
    // first MeshInstance3D child's mesh, as TryGetMeshBounds finds them.
    static bool TryGetMeshTriangles(Node3D node, out Transform3D transform, out Vector3[] triangles)
    {
        Mesh mesh = node.Get(meshName).As<Mesh>();
        if (mesh != null)
        {
            transform = node.GlobalTransform;
            triangles = mesh.GetFaces();
            return true;
        }
        foreach (Node child in node.GetChildren())
        {
            if (child is MeshInstance3D m && m.Mesh != null)
            {
                transform = m.GlobalTransform;
                triangles = m.Mesh.GetFaces();
                return true;
            }
        }
        transform = Transform3D.Identity;
        triangles = [];
        return false;
    }

    // Every cell a triangle passes through once the cell is pulled in by LevelOverlapDepth on every side: the box the
    // game's query asks Box3D about (BuildGrid.QueryLevelBlocked). Worked out in BuildGrid's numbering (cell n spans
    // n..n+1 on each axis) and added in a structure's (one higher in z).
    static void AddTriangleCells(Transform3D toFrame, Vector3[] triangles, HashSet<Vector3I> cells)
    {
        float size = BuildGrid.CellSize;
        float depth = BuildGrid.LevelOverlapDepth;
        float half = size / 2f - depth;
        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            Vector3 a = toFrame * triangles[i];
            Vector3 b = toFrame * triangles[i + 1];
            Vector3 c = toFrame * triangles[i + 2];
            Aabb bounds = new Aabb(a, Vector3.Zero).Expand(b).Expand(c);
            // Cells whose pulled-in box the triangle's bounds reach: n * size + depth <= high and (n + 1) * size - depth >= low.
            Vector3 low = bounds.Position;
            Vector3 high = bounds.End;
            int x0 = Mathf.CeilToInt((low.X + depth) / size - 1f);
            int x1 = Mathf.FloorToInt((high.X - depth) / size);
            int y0 = Mathf.CeilToInt((low.Y + depth) / size - 1f);
            int y1 = Mathf.FloorToInt((high.Y - depth) / size);
            int z0 = Mathf.CeilToInt((low.Z + depth) / size - 1f);
            int z1 = Mathf.FloorToInt((high.Z - depth) / size);
            for (int x = x0; x <= x1; x++)
            {
                for (int y = y0; y <= y1; y++)
                {
                    for (int z = z0; z <= z1; z++)
                    {
                        Vector3 cellCentre = new Vector3(x + 0.5f, y + 0.5f, z + 0.5f) * size;
                        if (TriangleTouchesBox(a, b, c, cellCentre, half))
                        {
                            cells.Add(new Vector3I(x, y, z + 1));
                        }
                    }
                }
            }
        }
    }

    // Whether a triangle touches an axis-aligned cube (centre and half its side). Separating axis test, as
    // BoxTouchesCell: the axes to try are the cube's three, the triangle's normal, and each cube axis crossed with
    // each triangle edge.
    static bool TriangleTouchesBox(Vector3 a, Vector3 b, Vector3 c, Vector3 centre, float half)
    {
        Vector3 p0 = a - centre;
        Vector3 p1 = b - centre;
        Vector3 p2 = c - centre;
        Vector3 e0 = p1 - p0;
        Vector3 e1 = p2 - p1;
        Vector3 e2 = p0 - p2;
        foreach (Vector3 axis in BoxAxes)
        {
            if (SeparatedOn(axis, p0, p1, p2, half)
                || SeparatedOn(axis.Cross(e0), p0, p1, p2, half)
                || SeparatedOn(axis.Cross(e1), p0, p1, p2, half)
                || SeparatedOn(axis.Cross(e2), p0, p1, p2, half))
            {
                return false;
            }
        }
        return !SeparatedOn(e0.Cross(e1), p0, p1, p2, half);
    }

    // Whether the triangle's shadow on axis misses the cube's (centred on the origin). A zero axis (an edge along
    // the cube's axis) separates nothing.
    static bool SeparatedOn(Vector3 axis, Vector3 p0, Vector3 p1, Vector3 p2, float half)
    {
        float d0 = axis.Dot(p0);
        float d1 = axis.Dot(p1);
        float d2 = axis.Dot(p2);
        float reach = half * (Mathf.Abs(axis.X) + Mathf.Abs(axis.Y) + Mathf.Abs(axis.Z));
        return Mathf.Min(d0, Mathf.Min(d1, d2)) > reach || Mathf.Max(d0, Mathf.Max(d1, d2)) < -reach;
    }

    // Every cell a no-build volume's box overlaps (touching doesn't count), as BuildGrid.FillBlockAt tests it. A
    // turned volume blocks the box around it, as in the game.
    static void AddNoBuildCells(Aabb box, HashSet<Vector3I> cells)
    {
        float size = BuildGrid.CellSize;
        Vector3 low = box.Position / size;
        Vector3 high = box.End / size;
        for (int x = Mathf.FloorToInt(low.X); x < high.X; x++)
        {
            for (int y = Mathf.FloorToInt(low.Y); y < high.Y; y++)
            {
                for (int z = Mathf.FloorToInt(low.Z); z < high.Z; z++)
                {
                    var cellBox = new Aabb(new Vector3(x, y, z) * size, Vector3.One * size);
                    if (box.Intersects(cellBox))
                    {
                        cells.Add(new Vector3I(x, y, z + 1));
                    }
                }
            }
        }
    }

    // The faces of blocked cells within GridRadius of centre that border an open cell. A cell both kinds block
    // shows as no-build, as in the game.
    void BuildBlockedMesh(Vector3I centre)
    {
        blockedMeshStale = false;
        blockedMeshCentre = centre;
        var levelFaces = new List<Vector3>();
        var noBuildFaces = new List<Vector3>();
        foreach (Vector3I cell in levelBlocked)
        {
            if (!noBuildBlocked.Contains(cell))
            {
                AddBlockedFaces(cell, centre, levelFaces);
            }
        }
        foreach (Vector3I cell in noBuildBlocked)
        {
            AddBlockedFaces(cell, centre, noBuildFaces);
        }

        var mesh = new ArrayMesh();
        AddMeshSurface(mesh, levelFaces, levelBlockedMaterial);
        AddMeshSurface(mesh, noBuildFaces, noBuildBlockedMaterial);
        blockedOverlay.Mesh = mesh;
    }

    void AddBlockedFaces(Vector3I cell, Vector3I centre, List<Vector3> output)
    {
        if ((cell - centre).LengthSquared() > GridRadius * GridRadius)
        {
            return;
        }
        for (int face = 0; face < 6; face++)
        {
            Vector3I next = cell + Structure.FaceDirections[face];
            if (!levelBlocked.Contains(next) && !noBuildBlocked.Contains(next))
            {
                AddFace(Transform3D.Identity, cell, face, BlockedFaceLift, output);
            }
        }
    }

    // An empty surface isn't allowed, so one is only added when it has something in it.
    static void AddMeshSurface(ArrayMesh mesh, List<Vector3> vertices, Material material)
    {
        if (vertices.Count == 0)
        {
            return;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices.ToArray();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, material);
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
        // A minimum width keeps the wrapped text from asking for one line per word while the dock still has no
        // width at editor start-up, which made the dock taller than the window and pushed the bottom panel off it.
        cellLabel = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(200, 0) };
        column.AddChild(cellLabel);

        inStructureBox = new CheckBox { Text = "In structure", TooltipText = "Whether this cell is one of the structure's cells. The last one can't be removed." };
        inStructureBox.Toggled += SetInStructure;
        column.AddChild(inStructureBox);

        // Faces down the side, inputs and outputs across.
        var faces = new GridContainer { Columns = 3 };
        faces.AddChild(new Label());
        faces.AddChild(new Label { Text = "In" });
        faces.AddChild(new Label { Text = "Out" });
        // The lambdas capture only this and an int, which Godot can carry over when it reloads the script.
        for (int face = 0; face < 6; face++)
        {
            int f = face;
            faces.AddChild(new Label { Text = FaceNames[face] });
            var input = new CheckBox { TooltipText = $"Items go in through this cell's {FaceNames[face].ToLower()} face." };
            input.Toggled += on => SetTag(Structure.InputTag(f), on);
            faces.AddChild(input);
            inputBoxes.Add(input);
            var output = new CheckBox { TooltipText = $"Items come out through this cell's {FaceNames[face].ToLower()} face." };
            output.Toggled += on => SetTag(Structure.OutputTag(f), on);
            faces.AddChild(output);
            outputBoxes.Add(output);
        }
        column.AddChild(faces);

        column.AddChild(new Label { Text = "Select neighbour" });
        var steps = new GridContainer { Columns = 2 };
        for (int face = 0; face < 6; face++)
        {
            int f = face;
            var button = new Button { Text = $"{FaceNames[face]} {Structure.FaceDirections[face]}" };
            button.Pressed += () => StepSelection(Structure.FaceDirections[f]);
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
            if (selectedCells.Count > 1)
            {
                cellLabel.Text = $"{editTarget.Name}: {selectedCells.Count} cells (showing {selectedCell})";
            }
            else
            {
                cellLabel.Text = $"{editTarget.Name}: cell {selectedCell}";
            }
        }
        else
        {
            cellLabel.Text = "Turn on Edit Cells (3D toolbar) and select a Structure, then click one of its cells.";
        }
        inStructureBox.SetPressedNoSignal(inStructure);
        // A structure needs at least one cell, so the selection can't take out every one it has.
        inStructureBox.Disabled = !editing || (inStructure && LeftAfterRemoving(cells) == 0);
        for (int face = 0; face < inputBoxes.Count && face < outputBoxes.Count; face++)
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
        if (!on && LeftAfterRemoving(cells) == 0)
        {
            return;
        }
        foreach (Vector3I cell in selectedCells)
        {
            if (on)
            {
                cells.TryAdd(cell, CellTags.None);
            }
            else
            {
                cells.Remove(cell);
            }
        }
        CommitCells(cells, "Set structure cells");
    }

    // How many of the structure's cells would be left if the selected ones were taken out.
    int LeftAfterRemoving(Godot.Collections.Dictionary<Vector3I, CellTags> cells)
    {
        int left = cells.Count;
        foreach (Vector3I cell in selectedCells)
        {
            if (cells.ContainsKey(cell))
            {
                left--;
            }
        }
        return left;
    }

    // Tagging a face of a cell that isn't in the structure adds it.
    void SetTag(CellTags tag, bool on)
    {
        if (editTarget == null)
        {
            return;
        }
        Godot.Collections.Dictionary<Vector3I, CellTags> cells = CellsOf(editTarget).Duplicate();
        foreach (Vector3I cell in selectedCells)
        {
            cells.TryGetValue(cell, out CellTags tags);
            if (on)
            {
                tags |= tag;
            }
            else
            {
                tags &= ~tag;
            }
            cells[cell] = tags;
        }
        CommitCells(cells, "Tag structure cell faces");
    }

    // Moves the whole selection one cell along direction.
    void StepSelection(Vector3I direction)
    {
        selectedCell += direction;
        for (int i = 0; i < selectedCells.Count; i++)
        {
            selectedCells[i] += direction;
        }
    }

    void SelectOnly(Vector3I cell)
    {
        selectedCell = cell;
        selectedCells.Clear();
        selectedCells.Add(cell);
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
        if (target != editTarget || selectedCells.Count == 0)
        {
            SelectOnly(Vector3I.Zero);
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
    // A shift-click adds that cell to the selection, or takes it out if it was already in; hitting none does nothing.
    public override int _Forward3DGuiInput(Camera3D viewportCamera, InputEvent @event)
    {
        if (editTarget == null || @event is not InputEventMouseButton click || click.ButtonIndex != MouseButton.Left || !click.Pressed)
        {
            return (int)AfterGuiInput.Pass;
        }
        Transform3D toLocal = editTarget.GlobalTransform.AffineInverse();
        Vector3 from = toLocal * viewportCamera.ProjectRayOrigin(click.Position);
        Vector3 direction = toLocal.Basis * viewportCamera.ProjectRayNormal(click.Position);
        Vector3I hit = Vector3I.Zero;
        bool hitAny = false;
        float nearest = float.MaxValue;
        foreach (Vector3I cell in CellsOf(editTarget).Keys)
        {
            var box = new Aabb(CellLow(cell), Vector3.One * BuildGrid.CellSize);
            // Of the cells the ray passes through, the one whose centre is nearest the camera.
            float distance = from.DistanceTo(box.GetCenter());
            if (distance < nearest && box.IntersectsSegment(from, from + direction * 10000f))
            {
                nearest = distance;
                hit = cell;
                hitAny = true;
            }
        }

        if (!click.ShiftPressed)
        {
            SelectOnly(hit);
        }
        else if (hitAny && !selectedCells.Contains(hit))
        {
            selectedCells.Add(hit);
            selectedCell = hit;
        }
        else if (hitAny && selectedCells.Count > 1)
        {
            // The dock then shows the most recently added cell still selected.
            selectedCells.Remove(hit);
            selectedCell = selectedCells[selectedCells.Count - 1];
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
            if (fillMeshesToggle.ButtonPressed)
            {
                CollectMeshCells(structure, structure.GlobalTransform.AffineInverse(), tolerance, found);
            }
            if (found.Count == 0)
            {
                GD.PushWarning($"Fill Cells: {structure.Name} has nothing to fill from; its cells are left as they are.");
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

    // Adds the cells touched by every solid collider (GMPShape) at or under node; sensors and the shapes of disabled
    // bodies don't count.
    static void CollectColliderCells(Node node, Transform3D toRoot, float tolerance, HashSet<Vector3I> cells)
    {
        bool isShape = IsSolidShape(node) && IsSolidBody(BodyOf(node));
        if (isShape && node is Node3D n && TryGetShapeBounds(n, out Aabb local))
        {
            AddCells(toRoot * n.GlobalTransform, local, tolerance, cells);
        }
        foreach (Node child in node.GetChildren())
        {
            CollectColliderCells(child, toRoot, tolerance, cells);
        }
    }

    // Adds the cells touched by every visible mesh at or under node, each by its mesh's bounding box.
    static void CollectMeshCells(Node node, Transform3D toRoot, float tolerance, HashSet<Vector3I> cells)
    {
        if (node is MeshInstance3D m && m.Mesh != null && m.IsVisibleInTree())
        {
            Aabb local = m.Mesh.GetAabb();
            if (local.Size.LengthSquared() > 0.000001f)
            {
                AddCells(toRoot * m.GlobalTransform, local, tolerance, cells);
            }
        }
        foreach (Node child in node.GetChildren())
        {
            CollectMeshCells(child, toRoot, tolerance, cells);
        }
    }

    static readonly StringName shapeName = "shape";
    static readonly StringName sizeName = "size";
    static readonly StringName radiusName = "radius";
    static readonly StringName heightName = "height";
    static readonly StringName meshName = "mesh";
    static readonly StringName isSensorName = "is_sensor";
    static readonly StringName enabledName = "enabled";
    static readonly StringName bodyTypeName = "body_type";

    // GMPShape.shape values (native/src/physics_shape.h).
    const int BoxShape = 0, SphereShape = 1, CapsuleShape = 2, CylinderShape = 3, HullShape = 4, MeshShape = 5;

    /// <summary>A GMPShape that isn't a sensor.</summary>
    static bool IsSolidShape(Node node)
    {
        return node.IsClass("GMPShape") && !node.Get(isSensorName).AsBool();
    }

    /// <summary>The GMPBody a shape belongs to: the nearest one above it, or null.</summary>
    static Node BodyOf(Node shape)
    {
        for (Node n = shape.GetParent(); n != null; n = n.GetParent())
        {
            if (n.IsClass("GMPBody"))
            {
                return n;
            }
        }
        return null;
    }

    static bool IsSolidBody(Node body)
    {
        return body != null && body.IsClass("GMPBody") && body.Get(enabledName).AsBool();
    }

    /// <summary>The bounding box of a GMPShape, in the shape's frame.</summary>
    static bool TryGetShapeBounds(Node3D node, out Aabb bounds)
    {
        bounds = new Aabb();
        int shape = node.Get(shapeName).AsInt32();
        if (shape == BoxShape)
        {
            Vector3 size = node.Get(sizeName).AsVector3();
            bounds = new Aabb(-size / 2, size);
        }
        else if (shape == SphereShape)
        {
            float r = node.Get(radiusName).AsSingle();
            bounds = new Aabb(new Vector3(-r, -r, -r), new Vector3(2 * r, 2 * r, 2 * r));
        }
        else if (shape == CapsuleShape || shape == CylinderShape)
        {
            float radius = node.Get(radiusName).AsSingle();
            float height = node.Get(heightName).AsSingle();
            bounds = new Aabb(new Vector3(-radius, -height / 2, -radius), new Vector3(2 * radius, height, 2 * radius));
        }
        else if (shape == HullShape || shape == MeshShape)
        {
            if (!TryGetMeshBounds(node, out bounds))
            {
                return false;
            }
        }
        else
        {
            return false;
        }
        return bounds.Size.LengthSquared() > 0.000001f;
    }

    // A mesh collider's source: its mesh, else its first MeshInstance3D child.
    static bool TryGetMeshBounds(Node3D node, out Aabb bounds)
    {
        bounds = new Aabb();
        Mesh mesh = node.Get(meshName).As<Mesh>();
        if (mesh != null)
        {
            bounds = mesh.GetAabb();
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
        return HasScriptIn(node, structureScriptPaths);
    }

    /// <summary>True if <paramref name="node"/> carries a script that is a <see cref="GMPObject"/> (placeholders included).</summary>
    static bool IsGMPObject(Node node)
    {
        return HasScriptIn(node, gmpObjectScriptPaths);
    }

    // Whether node's script, or one it derives from, is one of paths.
    static bool HasScriptIn(Node node, HashSet<string> paths)
    {
        for (Script s = node.GetScript().As<Script>(); s != null; s = s.GetBaseScript())
        {
            if (paths.Contains(s.ResourcePath))
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

    // Every script class that is a GMPObject (an interface, so each class implementing it is listed).
    static HashSet<string> GMPObjectScriptPaths()
    {
        var paths = new HashSet<string>();
        foreach (System.Type type in typeof(GMPObject).Assembly.GetTypes())
        {
            if (type.IsInterface || !typeof(GMPObject).IsAssignableFrom(type))
            {
                continue;
            }
            foreach (ScriptPathAttribute attribute in type.GetCustomAttributes<ScriptPathAttribute>(false))
            {
                paths.Add(attribute.Path);
            }
        }
        return paths;
    }
}
#endif
