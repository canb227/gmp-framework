#if TOOLS
using Godot;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// Editor snapping for hand-placed <see cref="Structure"/>s, so they pass the runtime grid-alignment check
/// (<see cref="Structure.isGridAligned"/>):
/// <list type="bullet">
/// <item>While the 3D toolbar's "Grid Snap" toggle is on, every selected Structure is held at the grid pose
/// nearest to where it is dragged or rotated (<see cref="Structure.NearestGridTransform"/>).</item>
/// <item>Project > Tools > Snap Structures To Build Grid snaps every Structure in the open scene (undoable).</item>
/// </list>
/// Structure isn't a [Tool] script, so in the editor its nodes carry a placeholder: they are found by script
/// inheritance and read <see cref="Structure.placementOffset"/> through Get(). The edited scene's root is never
/// moved, so editing a structure's own scene is unaffected.
/// </summary>
[Tool]
public partial class StructureSnapPlugin : EditorPlugin
{
    const string SnapAllMenuItem = "Snap Structures To Build Grid";

    // Partial classes get one [ScriptPath] per file; the generator tags each with the script's path.
    static readonly HashSet<string> structureScriptPaths = typeof(Structure).GetCustomAttributes<ScriptPathAttribute>(false).Select(a => a.Path).ToHashSet();

    CheckButton _toggle;

    public override void _EnterTree()
    {
        _toggle = new CheckButton
        {
            Text = "Grid Snap",
            TooltipText = "Hold selected Structures on BuildGrid cells (quarter-turn yaw).",
            ButtonPressed = true,
        };
        AddControlToContainer(CustomControlContainer.SpatialEditorMenu, _toggle);
        AddToolMenuItem(SnapAllMenuItem, Callable.From(SnapAllInScene));
    }

    public override void _ExitTree()
    {
        RemoveControlFromContainer(CustomControlContainer.SpatialEditorMenu, _toggle);
        _toggle.QueueFree();
        RemoveToolMenuItem(SnapAllMenuItem);
    }

    public override void _Process(double delta)
    {
        if (!_toggle.ButtonPressed)
        {
            return;
        }
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        bool moved = false;
        foreach (Node node in EditorInterface.Singleton.GetSelection().GetSelectedNodes())
        {
            if (node != root && IsStructure(node) && node is Node3D n && TryGetSnapped(n, out Transform3D snapped))
            {
                // Set directly: a gizmo drag records its own undo step from the node's transform on release.
                n.GlobalTransform = snapped;
                moved = true;
            }
        }
        if (moved)
        {
            EditorInterface.Singleton.MarkSceneAsUnsaved();
        }
    }

    void SnapAllInScene()
    {
        Node root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root == null)
        {
            return;
        }
        var targets = new List<(Node3D node, Transform3D from, Transform3D to)>();
        CollectMisaligned(root, root, targets);
        if (targets.Count == 0)
        {
            GD.Print("Structure Snap: every Structure is already on the build grid.");
            return;
        }

        EditorUndoRedoManager undo = GetUndoRedo();
        undo.CreateAction($"Snap {targets.Count} Structure(s) to build grid");
        foreach (var (node, from, to) in targets)
        {
            undo.AddDoProperty(node, Node3D.PropertyName.GlobalTransform, to);
            undo.AddUndoProperty(node, Node3D.PropertyName.GlobalTransform, from);
            GD.Print($"Structure Snap: {root.GetPathTo(node)} {from.Origin} -> {to.Origin}");
        }
        undo.CommitAction();
    }

    static void CollectMisaligned(Node node, Node root, List<(Node3D, Transform3D, Transform3D)> targets)
    {
        if (node != root && IsStructure(node) && node is Node3D n && TryGetSnapped(n, out Transform3D snapped))
        {
            targets.Add((n, n.GlobalTransform, snapped));
        }
        foreach (Node child in node.GetChildren())
        {
            CollectMisaligned(child, root, targets);
        }
    }

    /// <summary>True, with the grid pose, if <paramref name="n"/> isn't already at it.</summary>
    static bool TryGetSnapped(Node3D n, out Transform3D snapped)
    {
        Transform3D current = n.GlobalTransform;
        snapped = Structure.NearestGridTransform(current, n.Get(Structure.PropertyName.placementOffset).AsVector3(), out _, out _);
        return !current.IsEqualApprox(snapped);
    }

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
}
#endif
