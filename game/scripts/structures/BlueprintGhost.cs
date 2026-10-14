using Godot;
using System.Collections.Generic;

/// <summary>
/// The in-hand scene of every <see cref="BlueprintItem"/>. On the local player it shows a translucent
/// <see cref="PlacementProbe"/> of the blueprint's structure where it would be built (face-snapped, see
/// <see cref="BuildGrid.TryGetPlacementTarget"/>), tinted by whether the placement is allowed, which the
/// probe's sensors check against the simulation. While shown it reveals the grid around itself
/// and highlights the face it's snapped to (<see cref="BuildGrid.placementActive"/>, BuildGrid.Reveal.cs). Other players' copies stay empty. As a <see cref="HeldItem"/>
/// it handles its own input: "rotate" turns the preview, "alternate" switches a two-form blueprint (e.g. uphill/downhill
/// slope) to its other form, and primary builds it (host-arbitrated, see BuildGrid.Placement.cs). Deconstructing is an interact on a structure (FactoryPlayer.Interaction.cs).
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
    /// Off while the player holds "freePlace" (G): the preview moves a cell at a time instead of a module (2x2x2 cells,
    /// see <see cref="BuildGrid.TryGetPlacementTarget"/>), stays exactly where it's aimed (no snapping belt ends
    /// together) and placing doesn't turn the next one to follow its output.
    /// </summary>
    static bool smartPlacement => !Input.IsActionPressed(InputActions.freePlace);
    /// <summary>True when the probe has settled at <see cref="targetCell"/> and reports the placement allowed.</summary>
    public bool canPlace { get; private set; }

    PlacementProbe probe;

    /// <summary>The scene of the selected form (a belt's straight scene, while the preview may show one of its turns).</summary>
    PackedScene mainScene => blueprint.StructureScene(showingAlternate);
    /// <summary>
    /// Whether the structure has input ends (face tags, Structure.Ends.cs): its preview then snaps onto open output ends
    /// of placed structures. A <see cref="ConveyorBlueprint"/>'s also takes the shape the belt would be built in.
    /// </summary>
    bool snaps => Structure.SceneInputEnds(mainScene).Count > 0;
    ConveyorShape previewShape = ConveyorShape.Straight;
    /// <summary>
    /// The output end the preview last snapped to. Static, because each placement re-creates the ghost: the preview
    /// only turns to follow an end when it first snaps to a different one.
    /// </summary>
    static CellEnd snappedTo;
    /// <summary>True while a placement re-creates the ghost, which mustn't reset the direction like a real equip does.</summary>
    static bool placing;

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
        if (snaps && !placing)
        {
            // Equipping a belt (or anything else with inputs) points it the way the camera faces.
            Vector3 look = -player.camera.GlobalBasis.Z;
            quarterTurns = Mathf.PosMod(Mathf.RoundToInt(Mathf.Atan2(-look.X, -look.Z) / (Mathf.Pi / 2)), 4);
            snappedTo = null;
        }
        BuildPreview();
    }

    // (Re)builds the translucent probe and its arrow for the selected form.
    void BuildPreview()
    {
        probe?.Free();
        targetCell = null; // forces the new probe to be moved into place
        canPlace = false;
        PackedScene scene = mainScene;
        if (blueprint is ConveyorBlueprint conveyorBlueprint)
        {
            scene = conveyorBlueprint.SceneFor(previewShape);
        }
        probe = PlacementProbe.Create(scene, false);
        if (probe == null)
        {

            return;
        }

        foreach (Node n in probe.structure.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            // The structure's arrows keep their own colours.
            if (n.GetParent() is StructureArrow)
            {
                continue;
            }
            // Set through the engine rather than casting: a mesh can carry its own script (e.g. a Spinner),
            // which makes its managed type something other than MeshInstance3D.
            n.Set(GeometryInstance3D.PropertyName.MaterialOverride, material);
            n.Set(GeometryInstance3D.PropertyName.CastShadow, (int)GeometryInstance3D.ShadowCastingSetting.Off);
        }
        // Arrows hide themselves in game; the preview is where they're for.
        foreach (Node n in probe.structure.FindChildren("*", nameof(Node3D), true, false))
        {
            if (n is StructureArrow arrow)
            {
                arrow.Visible = true;
            }

        }
    }

    public override void _ExitTree()
    {
        probe?.Free();
        probe = null;
        if (isLocal)
        {
            BuildGrid.placementActive = false;
            BuildGrid.placementFree = false;
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
                placing = true;
                BuildGrid.RequestPlace(player, blueprint, cell, quarterTurns, showingAlternate);
                placing = false;
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
        BuildGrid.placementFree = !smartPlacement;

        Camera3D camera = player.camera;
        if (!BuildGrid.TryGetPlacementTarget(camera.GlobalPosition, -camera.GlobalTransform.Basis.Z, placeRange,
                probe.structure.cells.Keys, quarterTurns, smartPlacement, out Vector3I anchor, out _, out Vector3I faceNormal))
        {
            targetCell = null;
            canPlace = false;
            probe.structure.Visible = false;
            BuildGrid.placementFocus = null;
            BuildGrid.placementFace = null;
            return;
        }
        if (snaps && smartPlacement)
        {
            anchor = SnapToOutput(anchor);
        }
        if (blueprint is ConveyorBlueprint conveyorBlueprint)
        {
            // Preview the shape the belt will be built in.
            ConveyorShape wanted = Conveyor.ShapeAt(conveyorBlueprint, anchor, quarterTurns);
            if (wanted != previewShape)
            {
                previewShape = wanted;
                BuildPreview();
                if (probe == null)
                {
                    return;
                }
            }
        }
        BuildGrid.placementFocus = FootprintCentre(anchor);
        BuildGrid.placementHalfSize = FootprintHalfSize(anchor);
        BuildGrid.placementFloor = FootprintFloor(anchor);
        BuildGrid.placementFace = ContactFace(anchor, faceNormal);

        if (targetCell != anchor || probe.quarterTurns != quarterTurns || !probe.structure.Visible)
        {
            targetCell = anchor;
            probe.MoveTo(anchor, quarterTurns);
            probe.structure.Visible = true;

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

    // Aimed within a cell of a placed structure's open output end, the preview is pulled so that one of its input ends
    // takes that end's items, if one is the same size and shape. The first time it snaps to an end it turns to take
    // it head on; turned a quarter from there, a belt takes it on a turn's side input instead.
    Vector3I SnapToOutput(Vector3I anchor)
    {
        CellEnd feeder = Structure.FindOpenEnd(new List<Vector3I>(BuildGrid.FootprintCells(anchor, probe.structure.cells.Keys, quarterTurns)));
        if (feeder != snappedTo)
        {
            snappedTo = feeder;
            if (feeder != null)
            {
                TurnToTake(feeder);
            }
        }
        if (feeder == null)
        {
            return anchor;
        }
        var scenes = new List<PackedScene> { mainScene };
        if (blueprint is ConveyorBlueprint conveyorBlueprint)
        {
            scenes.Add(conveyorBlueprint.turnLeftScene);
            scenes.Add(conveyorBlueprint.turnRightScene);
        }
        foreach (PackedScene scene in scenes)
        {
            if (scene == null)
            {
                continue;
            }
            foreach (CellEnd input in Structure.SceneInputEnds(scene))
            {
                if (feeder.OffsetToFeed(input.Placed(anchor, quarterTurns)) is Vector3I offset)
                {
                    return anchor + offset;
                }
            }
        }
        return anchor;
    }

    // Turns the preview so one of the main scene's input ends can take the feeder's items; left alone if none can.
    void TurnToTake(CellEnd feeder)
    {
        for (int turns = 0; turns < 4; turns++)
        {
            foreach (CellEnd input in Structure.SceneInputEnds(mainScene))
            {
                if (feeder.OffsetToFeed(input.Placed(Vector3I.Zero, turns)) != null)
                {
                    quarterTurns = turns;
                    return;
                }
            }
        }
    }

    // World-space centre of the footprint's bounding box at anchor, where the grid reveal centres.
    Vector3 FootprintCentre(Vector3I anchor)
    {
        Vector3 min = Vector3.Inf, max = -Vector3.Inf;
        foreach (Vector3I cell in BuildGrid.FootprintCells(anchor, probe.structure.cells.Keys, quarterTurns))
        {
            Vector3 c = BuildGrid.CellToWorld(cell);
            min = min.Min(c);
            max = max.Max(c);
        }
        return (min + max) / 2f;
    }

    // Half the longest side of the footprint's bounding box at anchor, in metres; the grid reveal grows with it.
    float FootprintHalfSize(Vector3I anchor)
    {
        Vector3I min = new(int.MaxValue, int.MaxValue, int.MaxValue), max = new(int.MinValue, int.MinValue, int.MinValue);
        foreach (Vector3I cell in BuildGrid.FootprintCells(anchor, probe.structure.cells.Keys, quarterTurns))
        {
            min = min.Min(cell);
            max = max.Max(cell);
        }
        Vector3I size = max - min + Vector3I.One;
        return Mathf.Max(size.X, Mathf.Max(size.Y, size.Z)) * BuildGrid.CellSize / 2f;
    }

    // World height of the bottom of the footprint at anchor, where the grid reveal draws its floor brighter.
    float FootprintFloor(Vector3I anchor)
    {
        int bottom = int.MaxValue;
        foreach (Vector3I cell in BuildGrid.FootprintCells(anchor, probe.structure.cells.Keys, quarterTurns))
        {
            bottom = Mathf.Min(bottom, cell.Y);
        }
        return bottom * BuildGrid.CellSize;
    }

    // The footprint's rearmost layer along normal, as the min and max cells of its bounding box: its back side is
    // the face the structure is put against, which BuildGrid.Reveal.cs highlights.
    (Vector3I min, Vector3I max, Vector3I normal) ContactFace(Vector3I anchor, Vector3I normal)
    {
        int rear = int.MaxValue;
        foreach (Vector3I cell in BuildGrid.FootprintCells(anchor, probe.structure.cells.Keys, quarterTurns))
        {
            rear = Mathf.Min(rear, (cell.X * normal.X + cell.Y * normal.Y + cell.Z * normal.Z));
        }
        Vector3I min = new(int.MaxValue, int.MaxValue, int.MaxValue), max = new(int.MinValue, int.MinValue, int.MinValue);
        foreach (Vector3I cell in BuildGrid.FootprintCells(anchor, probe.structure.cells.Keys, quarterTurns))
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
