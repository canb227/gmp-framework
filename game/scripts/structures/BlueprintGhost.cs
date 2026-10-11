using Godot;
using System.Collections.Generic;

/// <summary>
/// The in-hand scene of every <see cref="BlueprintItem"/>. On the local player it shows a translucent
/// <see cref="PlacementProbe"/> of the blueprint's structure where it would be built (face-snapped, see
/// <see cref="BuildGrid.TryGetPlacementTarget"/>), tinted by whether the placement is allowed, which the
/// probe's sensors check against the simulation. Floating arrows show which way a belt carries items, or where a
/// machine takes them in and lets them out (<see cref="FlowArrowMesh"/>). While shown it reveals the grid around itself
/// and highlights the face it's snapped to (<see cref="BuildGrid.placementActive"/>, BuildGrid.Reveal.cs). Other players' copies stay empty. As a <see cref="HeldItem"/>
/// it handles its own input: "rotate" turns the preview, "alternate" switches a two-form blueprint (e.g. uphill/downhill
/// slope) to its other form, and primary builds it (host-arbitrated, see BuildGrid.Placement.cs). A floor conveyor
/// previews the form it would take where it's aimed (<see cref="ConveyorStructure"/>, see <see cref="PlacementProbe.MoveTo"/>). Deconstructing is an interact on a structure (FactoryPlayer.Interaction.cs).
/// </summary>
public partial class BlueprintGhost : HeldItem
{
    [Export] public float placeRange = 10f;
    [Export] public Color validColor = new(0.2f, 1f, 0.3f, 0.35f);
    [Export] public Color invalidColor = new(1f, 0.2f, 0.2f, 0.35f);

    public BlueprintItem blueprint => item as BlueprintItem;
    /// <summary>The anchor cell the structure would be built at, or null when not looking at anything in range.</summary>
    public Vector3I? targetCell { get; private set; }
    /// <summary>
    /// Rotation for the next structure, in quarter turns of positive yaw (0-3). Static so it survives
    /// re-equipping (each placement re-creates the ghost); only the local player's ghost uses it.
    /// </summary>
    public static int quarterTurns { get; private set; }
    /// <summary>
    /// Whether the next structure uses the blueprint's <see cref="BlueprintItem.alternateStructureScene"/>. Static like
    /// <see cref="quarterTurns"/>, so the chosen form survives re-equipping; ignored by blueprints with one form.
    /// </summary>
    public static bool useAlternate { get; private set; }
    /// <summary>True when the preview shows the alternate form (the blueprint has one and it's selected).</summary>
    public bool showingAlternate => useAlternate && blueprint?.alternateStructureScene != null;
    /// <summary>The structure copy the preview shows (used by the headless multiplayer test).</summary>
    public Structure previewStructure => probe?.structure;
    /// <summary>
    /// Off while the player holds "freePlace" (G): the preview stays exactly where it's aimed (no snapping belt ends
    /// together) and placing doesn't turn the next one to follow its output.
    /// </summary>
    static bool smartPlacement => !Input.IsActionPressed(InputActions.freePlace);
    /// <summary>True when the probe has settled at <see cref="targetCell"/> and reports the placement allowed.</summary>
    public bool canPlace { get; private set; }

    PlacementProbe probe;
    // The preview's ports in its own frame, for lining it up with built structures. A self-turning conveyor's
    // include every form's, so it also lines up with a belt that would feed its side.
    IReadOnlyList<PortShape> ports = [];
    readonly StandardMaterial3D material = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    /// <summary>Builds the preview (on the holder's peer only).</summary>
    public override void Equip(ItemInfo item, FactoryPlayer player)
    {
        base.Equip(item, player);
        if (!isLocal)
        {
            return;
        }
        if (blueprint?.structureScene == null)
        {
            Logging.Warn($"Blueprint {blueprint?.itemID} has no structure scene", "BlueprintGhost");
            return;
        }
        BuildPreview();
    }

    // (Re)builds the translucent probe and its arrow for the selected form.
    void BuildPreview()
    {
        probe?.Free();
        targetCell = null; // forces the new probe to be moved into place
        canPlace = false;
        probe = PlacementProbe.Create(blueprint.StructureScene(showingAlternate), false);
        if (probe == null)
        {
            ports = [];
            return;
        }
        ports = probe.structure.allLocalPorts;
        foreach (Node n in probe.structure.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            // Set through the engine rather than casting: a mesh can carry its own script (e.g. a Spinner),
            // which makes its managed type something other than MeshInstance3D.
            n.Set(GeometryInstance3D.PropertyName.MaterialOverride, material);
            n.Set(GeometryInstance3D.PropertyName.CastShadow, (int)GeometryInstance3D.ShadowCastingSetting.Off);
        }
        RefreshArrows();
    }

    // The preview's arrows (FlowArrowMesh): which way a belt carries items, or where a structure takes them in and
    // lets them out. Rebuilt when a self-turning conveyor's preview changes form, since its way in moves.
    Node3D arrows;
    ConveyorStructure.Shape? arrowsShape;

    void RefreshArrows()
    {
        // Freed already if the preview it hung on was replaced.
        if (GodotObject.IsInstanceValid(arrows)) arrows.QueueFree();
        arrows = FlowArrowMesh.Create(probe.structure);
        if (arrows != null)
        {
            probe.structure.AddChild(arrows);
        }
        arrowsShape = (probe.structure as ConveyorStructure)?.shape;
    }

    public override void _ExitTree()
    {
        probe?.Free();
        probe = null;
        if (isLocal)
        {
            BuildGrid.placementActive = false;
            BuildGrid.placementFocus = null;
            BuildGrid.placementFace = null;
        }
    }

    public override bool HandleInput(InputEvent @event)
    {
        if (@event.IsActionPressed(InputActions.rotate))
        {
            // Clockwise seen from above: a quarter turn of negative yaw.
            quarterTurns = (quarterTurns + 3) % 4;
            return true;
        }
        if (@event.IsActionPressed(InputActions.alternate))
        {
            if (blueprint?.alternateStructureScene != null)
            {
                SetAlternate(!useAlternate);
            }
            return true;
        }
        if (@event.IsActionPressed(InputActions.primary))
        {
            if (canPlace && targetCell is Vector3I cell)
            {
                // Read before requesting: using up the blueprint re-equips the slot, which can free this ghost's probe.
                int? outputTurns = StructurePorts.OutputQuarterTurns(probe.structure.localPorts);
                int placedTurns = quarterTurns;
                BuildGrid.RequestPlace(player, blueprint, cell, quarterTurns, showingAlternate);
                // Pre-rotate the next placement to carry on from this one's output, whatever blueprint comes next.
                if (outputTurns is int turns && smartPlacement)
                {
                    quarterTurns = (placedTurns + turns) % 4;
                }
            }
            return true;
        }
        return false;
    }

    /// <summary>Selects the main or alternate form and rebuilds the preview (public for the headless multiplayer test).</summary>
    public void SetAlternate(bool on)
    {
        if (useAlternate == on) return;
        useAlternate = on;
        if (probe != null) BuildPreview();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (probe == null)
        {
            return;
        }
        BuildGrid.placementActive = true;

        Camera3D camera = player.camera;
        if (!BuildGrid.TryGetPlacementTarget(camera.GlobalPosition, -camera.GlobalTransform.Basis.Z, placeRange,
                probe.structure.cellOffsets, quarterTurns, out Vector3I anchor, out _, out Vector3I faceNormal))
        {
            targetCell = null;
            canPlace = false;
            probe.structure.Visible = false;
            BuildGrid.placementFocus = null;
            BuildGrid.placementFace = null;
            return;
        }
        // A structure aimed a cell off a built one's port is pulled into line with it, so they connect (and belts merge).
        if (smartPlacement)
        {
            anchor = BuildGrid.AlignPorts(probe.structure, ports, anchor, quarterTurns);
        }
        BuildGrid.placementFocus = FootprintCentre(anchor);
        BuildGrid.placementFace = ContactFace(anchor, faceNormal);

        if (targetCell != anchor || probe.quarterTurns != quarterTurns || !probe.structure.Visible)
        {
            targetCell = anchor;
            probe.MoveTo(anchor, quarterTurns);
            probe.structure.Visible = true;
            if (probe.structure is ConveyorStructure conveyor && conveyor.shape != arrowsShape)
            {
                RefreshArrows();
            }
        }
        // Until the probe settles its overlaps still describe the previous pose: keep the old tint, don't allow placing.
        if (!probe.settled)
        {
            canPlace = false;
            return;
        }
        canPlace = BuildGrid.IsPlacementAllowed(probe, out _);
        material.AlbedoColor = canPlace ? validColor : invalidColor;
        BuildGrid.placementTint = material.AlbedoColor;
    }

    // World-space centre of the footprint's bounding box at anchor, where the grid reveal centres.
    Vector3 FootprintCentre(Vector3I anchor)
    {
        Vector3 min = Vector3.Inf, max = -Vector3.Inf;
        foreach (Vector3I cell in BuildGrid.FootprintCells(anchor, probe.structure.cellOffsets, quarterTurns))
        {
            Vector3 c = BuildGrid.CellToWorld(cell);
            min = min.Min(c);
            max = max.Max(c);
        }
        return (min + max) / 2f;
    }

    // The footprint's rearmost layer along normal, as the min and max cells of its bounding box: its back side is
    // the face the structure is put against, which BuildGrid.Reveal.cs highlights.
    (Vector3I min, Vector3I max, Vector3I normal) ContactFace(Vector3I anchor, Vector3I normal)
    {
        int rear = int.MaxValue;
        foreach (Vector3I cell in BuildGrid.FootprintCells(anchor, probe.structure.cellOffsets, quarterTurns))
        {
            rear = Mathf.Min(rear, (cell.X * normal.X + cell.Y * normal.Y + cell.Z * normal.Z));
        }
        Vector3I min = new(int.MaxValue, int.MaxValue, int.MaxValue), max = new(int.MinValue, int.MinValue, int.MinValue);
        foreach (Vector3I cell in BuildGrid.FootprintCells(anchor, probe.structure.cellOffsets, quarterTurns))
        {
            if ((cell.X * normal.X + cell.Y * normal.Y + cell.Z * normal.Z) != rear) continue;
            for (int axis = 0; axis < 3; axis++)
            {
                min[axis] = Mathf.Min(min[axis], cell[axis]);
                max[axis] = Mathf.Max(max[axis], cell[axis]);
            }
        }
        return (min, max, normal);
    }
}
