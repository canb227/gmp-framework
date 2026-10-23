using Godot;
using System.Collections.Generic;

/// <summary>
/// The in-hand scene of every <see cref="BlueprintItem"/>. On the local player it shows a translucent
/// <see cref="PlacementProbe"/> of the blueprint's structure where it would be built (face-snapped, see
/// <see cref="BuildGrid.TryGetPlacementTarget"/>), tinted by whether the placement is allowed, which the
/// probe's sensors check against the simulation. While shown it reveals the grid around itself
/// and highlights the face it's snapped to (<see cref="BuildGrid.placementActive"/>, BuildGrid.Reveal.cs). Other players' copies stay empty. As a <see cref="HeldItem"/>
/// it handles its own input: "rotate" turns the preview, "alternate" switches a two-form blueprint (e.g. uphill/downhill
/// slope) to its other form, and releasing primary builds it (host-arbitrated, see BuildGrid.Placement.cs). Deconstructing is the <see cref="Wrench"/>'s.
/// <para>
/// A structure with input or output ends snaps to connect to placed structures' open ends whenever the crosshair's ray
/// passes near the connection (<see cref="SnapRange"/>), both ways round: taking an output's items or feeding an
/// input. That wins over the grid's module alignment and over where the ray lands, or doesn't (aiming off the end of
/// a belt into the air), but never over something the ray hits first. Connections that couldn't be built are passed over.
/// </para>
/// <para>
/// A belt blueprint (<see cref="ConveyorBlueprint"/>) is dragged, like Factorio: holding primary lays a straight line of
/// ghosts, end to end, from where it was pressed toward the crosshair, every belt facing away from the first, and releasing
/// builds the line. Aimed at a belt of its own blueprint, it targets that belt's cells, and its ghosts may cover such
/// belts exactly: they're replaced (turned to face the line's way) rather than blocking it.
/// </para>
/// </summary>
public partial class BlueprintGhost : HeldItem
{
    [Export] public float placeRange = 10f;
    [Export] public Color validColor = new(0.2f, 1f, 0.3f, 0.35f);
    [Export] public Color invalidColor = new(1f, 0.2f, 0.2f, 0.35f);
    /// <summary>
    /// When on, a dragged belt line with any blocked cell builds nothing; when off, its buildable cells are built and the
    /// blocked ones skipped. A belt the line replaces doesn't block it; running out of blueprints only cuts the line short.
    /// </summary>
    [Export] public bool refuseBlockedLines = false;
    /// <summary>
    /// How close (m) the crosshair must be to where the structure would connect to a placed structure's open end for
    /// the preview to snap there: where the ray lands, or the ray itself if it lands nowhere near, measured to the
    /// connected footprint (<see cref="FindConnection"/>). Aim further off and the preview goes where it's aimed.
    /// </summary>
    const float SnapRange = 0.5f;

    public BlueprintItem blueprint => item as BlueprintItem;
    /// <summary>The anchor cell the structure would be built at, or null when not looking at anything in range.</summary>
    public Vector3I? targetCell { get; private set; }
    /// <summary>
    /// Rotation for the next structure, in quarter turns of positive yaw (0-3). Static so it survives
    /// re-equipping; only the local player's ghost uses it.
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
    /// Whether the structure has input or output ends (face tags, Structure.Ends.cs): its preview then snaps to connect
    /// to open ends of placed structures. A <see cref="ConveyorBlueprint"/>'s also takes the shape the belt would be built in.
    /// </summary>
    bool connects => Structure.SceneInputEnds(mainScene).Count > 0 || Structure.SceneOutputEnds(mainScene).Count > 0;
    ConveyorShape previewShape = ConveyorShape.Straight;
    /// <summary>
    /// The placed structure's end the preview is snapped to, or null.
    /// </summary>
    static CellEnd snappedTo;
    /// <summary>True while the preview is snapped to connect to a placed structure's end (for ghost_info).</summary>
    public static bool connected => snappedTo != null;
    /// <summary>
    /// The end the player turned the snapped preview at with "rotate", or null: while the preview stays snapped to it,
    /// the preview keeps the pose they turned it to (<see cref="rotatedAnchor"/>, <see cref="rotatedTurns"/>) instead of
    /// the one snapping picks. Snapping to another end, or to none, forgets it.
    /// </summary>
    static CellEnd rotatedEnd;
    static Vector3I rotatedAnchor;
    static int rotatedTurns;
    /// <summary>How far apart (m) the crosshair's ray is sampled when measuring how close it passes to a connection.</summary>
    const float RaySampleStep = 0.1f;
    /// <summary>Further than this (m) past the ray's reach, a structure's root is too far for any of its ends to be in reach.</summary>
    const float StructureReach = 12f;
    /// <summary>
    /// The first stretch of the crosshair's ray (m) doesn't count toward connecting: a connection right beside the
    /// player would otherwise be near the ray whichever way they look.
    /// </summary>
    const float ConnectMinDistance = 1.5f;
    /// <summary>A connection whose footprint, grown by this (m, the player's radius), holds the player's feet or eyes is skipped.</summary>
    const float PlayerClearance = 0.35f;
    /// <summary>A surface whose normal points at least this far up is a floor (for <see cref="FindConnection"/>).</summary>
    const float FloorNormalY = 0.7f;

    /// <summary>
    /// A pose that connects the preview to a placed structure's open end, with the normal pointing from that structure
    /// into the preview, how close the crosshair's ray passes to the posed footprint and how far along the ray that is,
    /// and how far the crosshair is from the end itself (which picks between connections, see FindConnection).
    /// </summary>
    record Connection(Vector3I anchor, int turns, CellEnd end, Vector3I normal, float distance, float along, float aim);
    /// <summary>The connection to the end the preview is snapped to counts this much (m) nearer, so it doesn't flick off at the edge.</summary>
    const float ConnectStickiness = 0.5f;

    /// <summary>
    /// How fast (per second) the drawn preview eases toward its target pose: each frame it covers 1 - e^(-rate * delta)
    /// of the way, so about 63% in 1/rate seconds. The target is worked out each physics tick; the drawing is per frame.
    /// </summary>
    [Export] public float easeRate = 30f;
    /// <summary>How fast (per second) the tint eases between valid and invalid, the same way.</summary>
    [Export] public float tintRate = 25f;
    /// <summary>How long (s) the preview stays where it was while the crosshair finds nothing, before it hides.</summary>
    const float HideDelay = 0.15f;
    /// <summary>
    /// How long (s) the crosshair must keep asking for the target the preview just moved off before it moves back: aimed
    /// right on a boundary between two targets, it would otherwise flick between them every tick.
    /// </summary>
    const float BounceTime = 0.12f;

    // The target as of the last physics tick, beside targetCell: what it's turned, the face it's against and what it's
    // snapped to, so it can be kept when the crosshair bounces straight back to the previous one.
    int targetTurns;
    Vector3I targetNormal;
    CellEnd targetEnd;
    // The target the preview last moved off, and how long the crosshair has asked to go back to it.
    Vector3I? leftAnchor;
    int leftTurns;
    float backFor;
    // How long the crosshair has found nothing to place at.
    float sinceAimed;

    // What's drawn, eased each frame toward the target pose: the footprint's centre and the rotation, so a turn spins
    // about the middle. centreInRoot is that centre in the structure's own frame.
    bool shown;
    Vector3 shownCentre;
    Quaternion shownRotation = Quaternion.Identity;
    Vector3 targetCentre;
    Quaternion targetRotation = Quaternion.Identity;
    Vector3 centreInRoot;
    Color targetTint;

    readonly StandardMaterial3D material = new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
    };

    /// <summary>
    /// While primary is held with a belt blueprint: the min corner of the footprint's bounding box where the line starts.
    /// The first belt keeps those cells whichever way the line runs, so a module-aligned start stays module aligned.
    /// </summary>
    Vector3I? dragCorner;
    /// <summary>The line's ghosts, one per belt out from <see cref="dragCorner"/>, each tinted on its own.</summary>
    readonly List<(PlacementProbe probe, StandardMaterial3D material)> lineGhosts = new();
    /// <summary>Quarter turns every belt of the line faces.</summary>
    int lineTurns;
    /// <summary>The line's cells to build on release and whether each replaces a belt, worked out every physics tick.</summary>
    readonly List<(Vector3I anchor, bool replace)> linePlan = new();
    /// <summary>Whether any cell of the line can't be built (see <see cref="refuseBlockedLines"/>).</summary>
    bool lineBlocked;

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
        if (connects)
        {
            // Equipping a belt (or anything else with ends) points it the way the camera faces.
            Vector3 look = -player.camera.GlobalBasis.Z;
            quarterTurns = Mathf.PosMod(Mathf.RoundToInt(Mathf.Atan2(-look.X, -look.Z) / (Mathf.Pi / 2)), 4);
            snappedTo = null;
            rotatedEnd = null;
        }
        BuildPreview();
    }

    // (Re)builds the translucent probe and its arrow for the selected form. The new one is drawn where the old one was
    // (shownCentre), so a belt changing shape carries on easing rather than starting over.
    void BuildPreview()
    {
        probe?.Free();
        canPlace = false;
        PackedScene scene = mainScene;
        if (blueprint is ConveyorBlueprint conveyorBlueprint)
        {
            scene = conveyorBlueprint.SceneFor(previewShape);
        }
        probe = CreateGhost(scene, material);
    }

    // A translucent probe of scene drawn in ghostMaterial, with its arrows showing; hidden until moved into place.
    static PlacementProbe CreateGhost(PackedScene scene, StandardMaterial3D ghostMaterial)
    {
        PlacementProbe ghost = PlacementProbe.Create(scene, false);
        if (ghost == null)
        {
            return null;
        }
        // A ghost jumps (it appears, swaps shape, is created parked far below); interpolating those as motion between
        // physics ticks would streak it across the screen. The main preview eases itself each frame instead (_Process).
        ghost.structure.PhysicsInterpolationMode = PhysicsInterpolationModeEnum.Off;
        // Every kind of mesh: a grate's bars are a MultiMeshInstance3D.
        foreach (Node n in ghost.structure.FindChildren("*", nameof(GeometryInstance3D), true, false))
        {
            // The structure's arrows keep their own colours.
            if (n.GetParent() is StructureArrow)
            {
                continue;
            }
            // Set through the engine rather than casting: a mesh can carry its own script (e.g. a Spinner),
            // which makes its managed type something other than MeshInstance3D.
            n.Set(GeometryInstance3D.PropertyName.MaterialOverride, ghostMaterial);
            n.Set(GeometryInstance3D.PropertyName.CastShadow, (int)GeometryInstance3D.ShadowCastingSetting.Off);
        }
        // Arrows hide themselves in game; the preview is where they're for.
        foreach (Node n in ghost.structure.FindChildren("*", nameof(Node3D), true, false))
        {
            if (n is StructureArrow arrow)
            {
                arrow.Visible = true;
            }
        }
        return ghost;
    }

    public override void _ExitTree()
    {
        probe?.Free();
        probe = null;
        ClearLine();
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
            Rotate();
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
            // Builds on release; a belt is first dragged into a line from here.
            if (blueprint is ConveyorBlueprint && targetCell is Vector3I start)
            {
                dragCorner = FootprintMin(start, quarterTurns);
            }
            return true;
        }
        if (@event.IsActionReleased(InputActions.primary))
        {
            if (dragCorner != null)
            {
                if (linePlan.Count > 0 && !(refuseBlockedLines && lineBlocked))
                {
                    // The next belt carries on the line's way.
                    quarterTurns = lineTurns;
                    BuildGrid.RequestPlace(player, blueprint, linePlan, lineTurns);
                }
                ClearLine();
            }
            else if (blueprint is not ConveyorBlueprint && canPlace && targetCell is Vector3I cell)
            {
                BuildGrid.RequestPlace(player, blueprint, new() { (cell, false) }, quarterTurns, showingAlternate);
            }
            return true;
        }
        return false;
    }

    /// <summary>
    /// Turns the next structure a quarter clockwise seen from above (a quarter turn of negative yaw) from how the
    /// preview shows it, as "rotate" does: four presses come back round, like Factorio. A belt shows the shape it would
    /// be built in at each (straight, or a turn where it's fed from a side). While the preview is snapped to an end it
    /// turns in place, over the same cells, and keeps that pose instead of the one snapping picks until it snaps
    /// elsewhere (<see cref="rotatedEnd"/>).
    /// </summary>
    public void Rotate()
    {
        int turned = (quarterTurns + 3) % 4;
        if (snappedTo != null && smartPlacement && targetCell is Vector3I shownAnchor)
        {
            rotatedEnd = snappedTo;
            rotatedAnchor = FootprintMin(shownAnchor, quarterTurns) - FootprintMin(Vector3I.Zero, turned);
            rotatedTurns = turned;
        }
        quarterTurns = turned;
        // Asked for, so never taken for the crosshair bouncing back to the pose the preview last left.
        leftAnchor = null;
        backFor = 0;
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
        if (dragCorner is Vector3I corner)
        {
            UpdateLine(corner);
            return;
        }

        Camera3D camera = player.camera;
        Vector3 from = camera.GlobalPosition;
        Vector3 direction = -camera.GlobalTransform.Basis.Z;
        bool aimed = BuildGrid.TryGetPlacementTarget(from, direction, placeRange,
            probe.structure.cells.Keys, quarterTurns, smartPlacement, out Vector3I anchor, out _, out Vector3I faceNormal);
        RayHit ray = BuildGrid.RaycastPastItems(from, from + direction * placeRange);
        // Aimed at a belt of its own blueprint, a belt blueprint targets that belt's cells, to turn it or start a line on it.
        // Aimed at its front (its output end's cells) with nothing built past it, it continues the belt instead.
        Conveyor aimedBelt = null;
        CellEnd aimedOutput = null;
        if (aimed && blueprint is ConveyorBlueprint && BuildGrid.FindStructure(ray) is Conveyor belt && belt.blueprintItemID == blueprint.itemID)
        {
            aimedBelt = belt;
            // The belt's cell the ray hit: just inside the surface it hit.
            Vector3I hitCell = BuildGrid.WorldToCell(ray.position - ray.normal * 0.01f);
            if (smartPlacement && belt.outputEnds != null)
            {
                foreach (CellEnd end in belt.outputEnds)
                {
                    if (end.cells.Contains(hitCell) && Structure.IsOpen(end, true))
                    {
                        aimedOutput = end;
                        aimedBelt = null;
                    }
                }
            }
        }
        if (aimedBelt != null)
        {
            // The anchor that, at the preview's rotation, covers the same cells as the belt.
            anchor = FootprintMin(aimedBelt.anchor, aimedBelt.quarterTurns) - FootprintMin(Vector3I.Zero, quarterTurns);
            snappedTo = null;
        }
        else if (connects && smartPlacement)
        {
            // Connecting to a placed structure wins over where the ray lands, even when it lands nowhere (aimed off a
            // belt into the air), as long as the ray passes near the connection before it hits anything.
            float reach = placeRange;
            Vector3? floorHit = null;
            if (ray.hit)
            {
                reach = from.DistanceTo(ray.position);
                if (ray.normal.Y > FloorNormalY)
                {
                    floorHit = ray.position;
                }
            }
            Vector3? hitPoint = null;
            if (ray.hit)
            {
                hitPoint = ray.position;
            }
            Connection connection = FindConnection(from, direction, reach, hitPoint, floorHit, aimedOutput);
            snappedTo = connection?.end;
            if (connection != null)
            {
                anchor = connection.anchor;
                quarterTurns = connection.turns;
                faceNormal = connection.normal;
                aimed = true;
            }
        }
        if (!aimed)
        {
            // A tick or two of finding nothing (the ray slipping off an edge) leaves the preview where it was.
            sinceAimed += (float)delta;
            if (targetCell != null && sinceAimed < HideDelay)
            {
                snappedTo = targetEnd;
                return;
            }
            rotatedEnd = null;
            targetCell = null;
            canPlace = false;
            shown = false;
            probe.structure.Visible = false;
            BuildGrid.placementFocus = null;
            BuildGrid.placementFace = null;
            return;
        }
        sinceAimed = 0;
        if (targetCell is Vector3I current && (anchor != current || quarterTurns != targetTurns))
        {
            bool goingBack = leftAnchor == anchor && leftTurns == quarterTurns;
            if (goingBack)
            {
                backFor += (float)delta;
            }
            if (goingBack && backFor < BounceTime)
            {
                // Straight back to the target it just left: aimed on the boundary between them. Stay put until the
                // crosshair has asked for it long enough to mean it.
                anchor = current;
                quarterTurns = targetTurns;
                faceNormal = targetNormal;
                snappedTo = targetEnd;
            }
            else
            {
                leftAnchor = current;
                leftTurns = targetTurns;
                backFor = 0;
            }
        }
        else
        {
            backFor = 0;
        }
        // Left the end it was turned at: snapping picks the pose again, there and on coming back.
        if (snappedTo != rotatedEnd)
        {
            rotatedEnd = null;
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

        targetCell = anchor;
        targetTurns = quarterTurns;
        targetNormal = faceNormal;
        targetEnd = snappedTo;
        // FindConnection moves the probe about to test connections, so it's compared with the probe's own pose.
        if (probe.anchor != anchor || probe.quarterTurns != quarterTurns || !probe.structure.Visible)
        {
            probe.MoveTo(anchor, quarterTurns);
            probe.structure.Visible = true;
        }
        // Where _Process eases the drawn preview to.
        Transform3D pose = Structure.PlacementTransform(anchor, quarterTurns);
        targetCentre = FootprintBox(anchor, quarterTurns).GetCenter();
        targetRotation = pose.Basis.GetRotationQuaternion();
        centreInRoot = pose.AffineInverse() * targetCentre;
        // Until the probe settles its overlaps still describe the previous pose: keep the old tint, don't allow placing.
        if (!probe.settled)
        {
            canPlace = false;
            return;
        }
        canPlace = BuildGrid.IsPlacementAllowed(probe, aimedBelt != null, out _);
        // Replacing a belt with the same belt would change nothing (as in Factorio).
        if (aimedBelt != null && aimedBelt.quarterTurns == quarterTurns)
        {
            canPlace = false;
        }
        targetTint = invalidColor;
        if (canPlace)
        {
            targetTint = validColor;
        }
        BuildGrid.placementTint = targetTint;
        if (!shown)
        {
            // Just appeared: drawn straight at its target, rather than easing in from wherever it was last.
            shown = true;
            shownCentre = targetCentre;
            shownRotation = targetRotation;
            material.AlbedoColor = targetTint;
        }
    }

    // Draws the preview every frame, eased toward the target the last physics tick worked out.
    public override void _Process(double delta)
    {
        if (probe == null || !shown)
        {
            return;
        }
        float weight = 1f - Mathf.Exp(-easeRate * (float)delta);
        shownCentre = shownCentre.Lerp(targetCentre, weight);
        shownRotation = shownRotation.Slerp(targetRotation, weight);
        Basis basis = new Basis(shownRotation);
        probe.structure.GlobalTransform = new Transform3D(basis, shownCentre - basis * centreInRoot);
        material.AlbedoColor = material.AlbedoColor.Lerp(targetTint, 1f - Mathf.Exp(-tintRate * (float)delta));
    }

    // Lays the line of belt ghosts from the footprint at corner toward the crosshair, along whichever horizontal axis
    // that's further on, and works out which belts to build on release (linePlan). Belts follow one another a
    // footprint's depth apart (its size along the way it carries items), so they line up end to end.
    void UpdateLine(Vector3I corner)
    {
        ConveyorBlueprint conveyorBlueprint = (ConveyorBlueprint)blueprint;
        probe.structure.Visible = false;
        int minZ = int.MaxValue, maxZ = int.MinValue;
        foreach (Vector3I offset in probe.structure.cells.Keys)
        {
            minZ = Mathf.Min(minZ, offset.Z);
            maxZ = Mathf.Max(maxZ, offset.Z);
        }
        int depth = maxZ - minZ + 1;

        // The crosshair's cell: where it meets the floor of the line, or placeRange out if that's further or behind.
        Camera3D camera = player.camera;
        Vector3 from = camera.GlobalPosition;
        Vector3 direction = -camera.GlobalTransform.Basis.Z;
        float distance = placeRange;
        if (direction.Y != 0)
        {
            float toFloor = (corner.Y * BuildGrid.CellSize - from.Y) / direction.Y;
            if (toFloor > 0 && toFloor < placeRange)
            {
                distance = toFloor;
            }
        }
        Vector3I end = BuildGrid.WorldToCell(from + direction * distance);
        // How many footprints along each axis the crosshair is from the first one.
        int alongX = Mathf.FloorToInt((end.X - corner.X) / (float)depth);
        int alongZ = Mathf.FloorToInt((end.Z - corner.Z) / (float)depth);
        Vector3I step;
        int length;
        if (Mathf.Abs(alongX) >= Mathf.Abs(alongZ))
        {
            step = new Vector3I(Mathf.Sign(alongX), 0, 0);
            length = Mathf.Abs(alongX);
        }
        else
        {
            step = new Vector3I(0, 0, Mathf.Sign(alongZ));
            length = Mathf.Abs(alongZ);
        }
        // Belts face away from the start; a click without a drag keeps the preview's rotation.
        lineTurns = quarterTurns;
        if (length > 0)
        {
            lineTurns = Mathf.PosMod(Mathf.RoundToInt(Mathf.Atan2(-step.X, -step.Z) / (Mathf.Pi / 2)), 4);
        }

        int blueprintsLeft = 0;
        InventorySlot slot = player.inventory.GetSlot(player.inventory.ActiveHotbarSlot);
        if (slot.itemID == blueprint.itemID)
        {
            blueprintsLeft = slot.Count;
        }
        // The first belt's anchor that, turned the line's way, covers the cells at corner.
        Vector3I firstAnchor = corner - FootprintMin(Vector3I.Zero, lineTurns);
        linePlan.Clear();
        lineBlocked = false;
        for (int i = 0; i <= length; i++)
        {
            Vector3I anchor = firstAnchor + step * depth * i;
            // Only the first belt can be fed from a side; the rest are fed from behind by the one before.
            PackedScene scene = conveyorBlueprint.structureScene;
            if (i == 0)
            {
                scene = conveyorBlueprint.SceneFor(Conveyor.ShapeAt(conveyorBlueprint, anchor, lineTurns));
            }
            if (i == lineGhosts.Count)
            {
                StandardMaterial3D ghostMaterial = (StandardMaterial3D)material.Duplicate();
                lineGhosts.Add((CreateGhost(scene, ghostMaterial), ghostMaterial));
            }
            else if (lineGhosts[i].probe.structure.SceneFilePath != scene.ResourcePath)
            {
                lineGhosts[i].probe.Free();
                lineGhosts[i] = (CreateGhost(scene, lineGhosts[i].material), lineGhosts[i].material);
            }
            PlacementProbe ghost = lineGhosts[i].probe;
            if (ghost.anchor != anchor || ghost.quarterTurns != lineTurns || !ghost.structure.Visible)
            {
                ghost.MoveTo(anchor, lineTurns);
                ghost.structure.Visible = true;
            }

            Conveyor replaced = BuildGrid.ReplaceableBelt(blueprint.itemID, BuildGrid.FootprintCells(anchor, ghost.structure.cells.Keys, lineTurns));
            bool replace = replaced != null;
            bool buildable;
            if (replace && replaced.quarterTurns == lineTurns)
            {
                // Already the belt the line would build: left as it is. Clicked on alone, there's nothing to build.
                buildable = length > 0;
            }
            else
            {
                // Until the ghost settles its overlaps describe its previous cells: go by the grid alone, and let the host check the rest.
                if (ghost.settled)
                {
                    buildable = BuildGrid.IsPlacementAllowed(ghost, replace, out _);
                }
                else
                {
                    buildable = replace || BuildGrid.FootprintClear(anchor, ghost.structure.cells.Keys, lineTurns);
                }
                if (!buildable)
                {
                    lineBlocked = true;
                }
                else if (!replace && blueprintsLeft == 0)
                {
                    buildable = false;
                }
                else
                {
                    if (!replace)
                    {
                        blueprintsLeft--;
                    }
                    linePlan.Add((anchor, replace));
                }
            }
            if (buildable)
            {
                lineGhosts[i].material.AlbedoColor = validColor;
            }
            else
            {
                lineGhosts[i].material.AlbedoColor = invalidColor;
            }
        }
        while (lineGhosts.Count > length + 1)
        {
            lineGhosts[^1].probe.Free();
            lineGhosts.RemoveAt(lineGhosts.Count - 1);
        }
        BuildGrid.placementTint = validColor;
        if (refuseBlockedLines && lineBlocked)
        {
            // Nothing will be built.
            foreach (var lineGhost in lineGhosts)
            {
                lineGhost.material.AlbedoColor = invalidColor;
            }
            BuildGrid.placementTint = invalidColor;
        }

        // The grid reveal follows the last belt.
        BuildGrid.placementFocus = BuildGrid.CellToWorld(corner + step * depth * length);
        BuildGrid.placementHalfSize = depth * BuildGrid.CellSize / 2f;
        BuildGrid.placementFloor = corner.Y * BuildGrid.CellSize;
        BuildGrid.placementFace = null;
    }

    // The min corner of the footprint's bounding box at anchor, turned turns.
    Vector3I FootprintMin(Vector3I anchor, int turns)
    {
        Vector3I min = new(int.MaxValue, int.MaxValue, int.MaxValue);
        foreach (Vector3I cell in BuildGrid.FootprintCells(anchor, probe.structure.cells.Keys, turns))
        {
            min = min.Min(cell);
        }
        return min;
    }

    // Ends a drag: frees the line's ghosts and shows the ordinary preview again.
    void ClearLine()
    {
        foreach (var lineGhost in lineGhosts)
        {
            lineGhost.probe.Free();
        }
        lineGhosts.Clear();
        linePlan.Clear();
        dragCorner = null;
        targetCell = null;
    }

    // The best way to connect the preview to an open end of a placed structure near the crosshair's ray (from along
    // direction, up to reach), or null: one of its input ends taking an open output's items, or one of its output ends
    // feeding an open input. The ray must pass within SnapRange of the connected footprint. Where the ray lands on
    // a floor (floorHit), a footprint standing on that same floor must be within SnapRange of where it lands
    // instead: one the player could have aimed straight at on the floor they aimed at, but didn't, isn't wanted. Of
    // those, the end nearest where the ray lands (hitPoint), or nearest the ray if it lands nowhere, wins: between a
    // belt's output and a slope's input a few cells on, which one the preview joins switches halfway between them,
    // whatever kind of end each is. A pose the structure couldn't be built in (its cells taken or blocked, or its
    // colliders in the level, like a down slope sunk into the floor) is passed over, so the preview stays where it's aimed.
    // The ray landing right on an end's own cells (aimedEnd, the front of a belt) makes that end the nearest of all.
    Connection FindConnection(Vector3 from, Vector3 direction, float reach, Vector3? hitPoint, Vector3? floorHit, CellEnd aimedEnd)
    {
        var connections = new List<Connection>();
        foreach (GMPObject obj in GameWorld.syncedObjs.Values)
        {
            if (obj is not Structure structure || structure.outputEnds == null || structure.GlobalPosition.DistanceTo(from) > reach + StructureReach)
            {
                continue;
            }
            foreach (CellEnd end in structure.outputEnds)
            {
                AddConnection(connections, end, true, from, direction, reach, hitPoint, floorHit, aimedEnd);
            }
            foreach (CellEnd end in structure.inputEnds)
            {
                AddConnection(connections, end, false, from, direction, reach, hitPoint, floorHit, aimedEnd);
            }
        }
        // Best first, until one can be built.
        while (connections.Count > 0)
        {
            Connection best = connections[0];
            foreach (Connection c in connections)
            {
                if (c.aim < best.aim || (c.aim == best.aim && c.along < best.along))
                {
                    best = c;
                }
            }
            if (CanBuildAt(best.anchor, best.turns))
            {
                return best;
            }
            connections.Remove(best);
        }
        return null;
    }

    // Adds the pose connecting the preview to end (a placed structure's output if endIsOutput, else its input), if the
    // end is open, the preview has an end that fits it, and the ray passes within SnapRange of the posed footprint
    // (or lands that close to it, on the floor it stands on), or the ray lands on the end's own cells (aimedEnd; see FindConnection).
    void AddConnection(List<Connection> connections, CellEnd end, bool endIsOutput, Vector3 from, Vector3 direction, float reach,
        Vector3? hitPoint, Vector3? floorHit, CellEnd aimedEnd)
    {
        if (!Structure.IsOpen(end, endIsOutput))
        {
            return;
        }
        // The pose the player turned it to at this end, else the one snapping picks.
        Vector3I anchor = rotatedAnchor;
        int turns = rotatedTurns;
        if (end != rotatedEnd && !ConnectPose(end, endIsOutput, out anchor, out turns))
        {
            return;
        }
        Aabb box = FootprintBox(anchor, turns);
        Vector3 min = box.Position;
        Vector3 max = box.End;
        // Never around the player: standing where a connection would go and aiming past it means past it.
        Aabb around = box.Grow(PlayerClearance);
        if (around.HasPoint(player.GlobalPosition) || around.HasPoint(from))
        {
            return;
        }
        float distance;
        float along;
        if (floorHit is Vector3 hit && Mathf.Abs(min.Y - hit.Y) < BuildGrid.CellSize)
        {
            // Across the floor only: the floor sits a little below the footprint's bottom.
            Vector3 nearest = hit.Clamp(min, max);
            nearest.Y = hit.Y;
            distance = hit.DistanceTo(nearest);
            along = reach;
        }
        else
        {
            distance = RayToBox(from, direction, reach, min, max, out along);
        }
        if (end == snappedTo)
        {
            distance = Mathf.Max(0f, distance - ConnectStickiness);
        }
        if (end == aimedEnd)
        {
            distance = 0;
        }
        if (distance > SnapRange)
        {
            return;
        }
        // From the placed structure into the preview: along an output's items, against an input's.
        Vector3I normal = -end.direction;
        if (endIsOutput)
        {
            normal = end.direction;
        }
        // The middle of the end's face, where the preview would join it: the outer face of an output's cells, the
        // face an input's items come in through. Normal points out of that face either way.
        Vector3 face = Vector3.Zero;
        foreach (Vector3I cell in end.cells)
        {
            face += BuildGrid.CellToWorld(cell);
        }
        face = face / end.cells.Count + (Vector3)normal * (BuildGrid.CellSize / 2f);
        float aim;
        if (hitPoint is Vector3 landed)
        {
            aim = landed.DistanceTo(face);
        }
        else
        {
            aim = RayToBox(from, direction, reach, face, face, out _);
        }
        if (end == aimedEnd)
        {
            aim = 0;
        }
        connections.Add(new Connection(anchor, turns, end, normal, distance, along, aim));
    }

    // Where the preview is anchored, and turned, for one of its main scene's ends to connect to end head on (a placed
    // structure's output if endIsOutput, which the preview's inputs take, else its input, which the preview's outputs
    // feed): the first of the four turns from the preview's own that fits, so once snapped it stays as it is. Turning
    // a belt to take an output on a turn's side input is the player's, with "rotate" (see Rotate).
    bool ConnectPose(CellEnd end, bool endIsOutput, out Vector3I anchor, out int turns)
    {
        for (int i = 0; i < 4; i++)
        {
            turns = (quarterTurns + i) % 4;
            if (ConnectAnchor(end, endIsOutput, mainScene, turns) is Vector3I a)
            {
                anchor = a;
                return true;
            }
        }
        turns = quarterTurns;
        anchor = default;
        return false;
    }

    // The anchor at which one of scene's ends, turned turns, connects to end, or null if none fits it.
    static Vector3I? ConnectAnchor(CellEnd end, bool endIsOutput, PackedScene scene, int turns)
    {
        if (endIsOutput)
        {
            foreach (CellEnd input in Structure.SceneInputEnds(scene))
            {
                if (end.OffsetToFeed(input.Placed(Vector3I.Zero, turns)) is Vector3I offset)
                {
                    return offset;
                }
            }
            return null;
        }
        foreach (CellEnd output in Structure.SceneOutputEnds(scene))
        {
            // How far the input would have to move to take the output's items: the preview moves the other way instead.
            if (output.Placed(Vector3I.Zero, turns).OffsetToFeed(end) is Vector3I offset)
            {
                return -offset;
            }
        }
        return null;
    }

    // The world-space box around the footprint at anchor, turned turns.
    Aabb FootprintBox(Vector3I anchor, int turns)
    {
        Vector3I min = new(int.MaxValue, int.MaxValue, int.MaxValue), max = new(int.MinValue, int.MinValue, int.MinValue);
        foreach (Vector3I cell in BuildGrid.FootprintCells(anchor, probe.structure.cells.Keys, turns))
        {
            min = min.Min(cell);
            max = max.Max(cell);
        }
        return new Aabb((Vector3)min * BuildGrid.CellSize, (Vector3)(max - min + Vector3I.One) * BuildGrid.CellSize);
    }

    // Whether the structure could be built at anchor with turns: its cells free and clear, and its colliders out of
    // the level. Moves the probe there to ask.
    bool CanBuildAt(Vector3I anchor, int turns)
    {
        if (!BuildGrid.FootprintClear(anchor, probe.structure.cells.Keys, turns))
        {
            return false;
        }
        probe.MoveTo(anchor, turns);
        return probe.LevelOverlaps().Count == 0;
    }

    // How close the ray from `from` along direction (unit length), from ConnectMinDistance out to reach, passes to the
    // box min..max, and how far along the ray that closest point is. Sampled every RaySampleStep.
    static float RayToBox(Vector3 from, Vector3 direction, float reach, Vector3 min, Vector3 max, out float along)
    {
        float nearest = float.MaxValue;
        along = 0;
        for (float t = ConnectMinDistance; t <= reach; t += RaySampleStep)
        {
            Vector3 point = from + direction * t;
            float distance = point.DistanceTo(point.Clamp(min, max));
            if (distance < nearest)
            {
                nearest = distance;
                along = t;
            }
        }
        return nearest;
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
