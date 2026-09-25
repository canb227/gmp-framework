using Godot;

/// <summary>
/// The in-hand scene of every <see cref="BlueprintItem"/>. On the local player it shows a translucent
/// <see cref="PlacementProbe"/> of the blueprint's structure where it would be built (face-snapped, see
/// <see cref="BuildGrid.TryGetPlacementTarget"/>), tinted by whether the placement is allowed, which the
/// probe's sensors check against the simulation. Structures with a <see cref="Structure.flowArrow"/> (conveyors)
/// also get a floating arrow showing which way they'll carry items. While shown it turns on the grid lines
/// (<see cref="BuildGrid.placementActive"/>). Other players' copies stay empty. As a <see cref="HeldItem"/>
/// it handles its own input: "rotate" turns the preview and primary builds it (host-arbitrated, see
/// BuildGrid.Placement.cs). Deconstructing is an interact on a structure (FactoryPlayer.Interaction.cs).
/// </summary>
public partial class BlueprintGhost : HeldItem
{
    [Export] public float placeRange = 10f;
    [Export] public Color validColor = new(0.2f, 1f, 0.3f, 0.35f);
    [Export] public Color invalidColor = new(1f, 0.2f, 0.2f, 0.35f);
    [Export] public Color arrowColor = new(1f, 1f, 1f, 0.55f);

    public BlueprintItem blueprint => item as BlueprintItem;
    /// <summary>The anchor cell the structure would be built at, or null when not looking at anything in range.</summary>
    public Vector3I? targetCell { get; private set; }
    /// <summary>
    /// Rotation for the next structure, in quarter turns of positive yaw (0-3). Static so it survives
    /// re-equipping (each placement re-creates the ghost); only the local player's ghost uses it.
    /// </summary>
    public static int quarterTurns { get; private set; }
    /// <summary>True when the probe has settled at <see cref="targetCell"/> and reports the placement allowed.</summary>
    public bool canPlace { get; private set; }

    PlacementProbe probe;
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
        probe = PlacementProbe.Create(blueprint.structureScene, false);
        if (probe == null)
        {
            return;
        }
        foreach (Node n in probe.structure.FindChildren("*", nameof(MeshInstance3D), true, false))
        {
            var mesh = (MeshInstance3D)n;
            mesh.MaterialOverride = material;
            mesh.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        }
        // Conveyors show which way they'll carry items; the arrow belongs to the preview only.
        if (FlowArrowMesh.Create(probe.structure, new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = arrowColor,
            }) is MeshInstance3D arrow)
        {
            probe.structure.AddChild(arrow);
        }
    }

    public override void _ExitTree()
    {
        probe?.Free();
        probe = null;
        if (isLocal)
        {
            BuildGrid.placementActive = false;
        }
    }

    public override bool HandleInput(InputEvent @event)
    {
        if (@event.IsActionPressed("rotate"))
        {
            quarterTurns = (quarterTurns + 1) % 4;
            return true;
        }
        if (@event.IsActionPressed("primary"))
        {
            if (canPlace && targetCell is Vector3I cell)
            {
                BuildGrid.RequestPlace(player, blueprint, cell, quarterTurns);
            }
            return true;
        }
        return false;
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
                probe.structure.cellOffsets, quarterTurns, out Vector3I anchor))
        {
            targetCell = null;
            canPlace = false;
            probe.structure.Visible = false;
            return;
        }

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
    }
}
