using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// The build grid: cell/world conversion, which <see cref="Structure"/> occupies each cell, and the
/// debug line mesh / looked-at cell highlight. Occupancy is kept identical on every peer because each
/// peer registers structures as they spawn and despawn; placement and deconstruction are decided by
/// the host (BuildGrid.Placement.cs).
/// </summary>
public partial class BuildGrid : Node3D
{
    public static BuildGrid instance;
    /// <summary>Debug toggle for the grid lines (console: debugui grid).</summary>
    public static bool enabled = false;
    /// <summary>Set while the local player holds a blueprint; reveals the grid around the ghost (BuildGrid.Reveal.cs).</summary>
    public static bool placementActive = false;
    public static bool highlightLookedAtCell = false;
    public static Vector3I? highlightedCell = null;

    public const float CellSize = 1.0f;
    /// <summary>Size of one structure module: structures are built in 2 m blocks of cells. The grid draws bolder lines on it.</summary>
    public const float ModuleSize = 2.0f;
    /// <summary>Cells along each side of a module (<see cref="ModuleSize"/> / <see cref="CellSize"/>).</summary>
    public const int ModuleCells = 2;
    private const float DrawExtent = 200.0f;
    private const float HighlightRaycastRange = 20.0f;
    private const float HighlightEdgeNudge = 0.001f;

    private static readonly Dictionary<Vector3I, Structure> cells = new();
    /// <summary>The level's <see cref="NoBuildVolume"/>s in the tree; any cell one touches can't be built in.</summary>
    public static readonly List<NoBuildVolume> noBuildVolumes = new();

    private MeshInstance3D _meshInstance;
    private ImmediateMesh _immediateMesh;
    private bool _meshBuilt = false;

    private MeshInstance3D _highlightMeshInstance;

    public override void _Ready()
    {
        instance = this;

        _immediateMesh = new ImmediateMesh();

        var material = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1, 1, 1, 0.25f),
        };

        _meshInstance = new MeshInstance3D
        {
            Mesh = _immediateMesh,
            MaterialOverride = material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };

        AddChild(_meshInstance);

        var highlightMaterial = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(1f, 0.8f, 0.1f, 0.35f),
        };

        _highlightMeshInstance = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(CellSize, CellSize, CellSize) },
            MaterialOverride = highlightMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };

        AddChild(_highlightMeshInstance);
        CreateReveal();
        CreateFill();
    }

    public override void _Process(double delta)
    {
        UpdateReveal((float)delta);
        if (enabled && !_meshBuilt)
        {
            BuildGridMesh();
        }
        _meshInstance.Visible = enabled;
    }

    /// <summary>
    /// Runs from <c>_PhysicsProcess</c>, not <c>_Process</c>: a world raycast waits for any Box3D step running on
    /// the async step thread, which in a render frame would stall the frame.
    /// </summary>
    private void UpdateHighlight()
    {
        Camera3D camera = highlightLookedAtCell ? GetViewport().GetCamera3D() : null;
        if (camera != null && TryGetLookedAtCell(camera, HighlightRaycastRange, out Vector3I cell))
        {
            highlightedCell = cell;
            _highlightMeshInstance.Position = CellToWorld(cell);
            _highlightMeshInstance.Visible = true;
        }
        else
        {
            _highlightMeshInstance.Visible = false;
            highlightedCell = null;
        }
    }

    public static void Toggle() => enabled = !enabled;
    public static void ToggleHighlight() => highlightLookedAtCell = !highlightLookedAtCell;

    /// <summary>
    /// Raycasts along the camera's view and returns the empty-side cell of the surface it hits: the cell
    /// in front of whatever face you look at (on top of a floor, beside a wall or structure).
    /// </summary>
    public static bool TryGetLookedAtCell(Camera3D camera, float range, out Vector3I cell)
    {
        Vector3 from = camera.GlobalPosition;
        Vector3 to = from + -camera.GlobalTransform.Basis.Z * range;
        var ray = PhysicsWorld.Raycast(from, to);
        if (!ray.hit)
        {
            cell = default;
            return false;
        }

        // Nudge off the surface along its normal before flooring, so a hit that lands exactly on a
        // cell boundary (e.g. a floor at a grid line) resolves to the cell above the surface rather
        // than the one buried inside it.
        cell = WorldToCell(ray.position + ray.normal * HighlightEdgeNudge);
        return true;
    }

    /// <summary>
    /// Where a structure with footprint <paramref name="offsets"/> (rotated by <paramref name="quarterTurns"/>)
    /// would be anchored when placed by looking along a ray. The ray picks a face:
    /// <list type="bullet">
    /// <item>If the ray crosses cells occupied by a structure before it hits any collider beyond them, it snaps
    /// to a full face of those cells (of the modules they're in, with <paramref name="snapToModules"/>), whatever the
    /// structure's colliders look like (a thin conveyor, a slope):
    /// the face it entered through if the ray ends inside them, else the face it exits through (see
    /// <see cref="TryFindOccupiedCellAlongRay"/>). The target is the cell across that face.</item>
    /// <item>Otherwise the target is the cell on the open side of the surface the ray hit (its normal snapped to
    /// the nearest axis, so slopes work).</item>
    /// </list>
    /// The footprint then grows away from that face: it is shifted along the face normal so its rearmost
    /// layer sits in the target cell's layer, e.g. a 2x1x1 put against a wall's side sticks out of the wall. Across
    /// the face its anchor cell (the cell its root is the corner of) is the target cell, so the structure starts under
    /// the crosshair and turning it turns it about that cell. Height on a wall works out the same: the footprint
    /// stands on the target cell.
    /// <para>
    /// With <paramref name="snapToModules"/> the footprint's bounding box starts on a module corner (even cells on
    /// every axis), so structures line up with each other in whole modules instead of overlapping by a cell. Along the
    /// normal it moves away from the surface to get there; across the face the anchor's module
    /// (<see cref="AnchorModuleCentre"/>) is the target cell's, so the ghost only moves when the crosshair crosses a
    /// module line and turns about that module; and on a wall it stands on the module below.
    /// </para>
    /// <para>
    /// The target is the first cell out from the surface hit that the surface itself doesn't reach into (a floor whose
    /// top is partway up a cell, the cell above); if level geometry takes any of that cell (<see cref="IsLevelBlocked"/>,
    /// a floor cell at the foot of a wall), or with <paramref name="snapToModules"/> any cell of its module, the cell
    /// (module) the ray passes through just before it first enters one the level takes, still standing on the surface
    /// aimed at (its layer nearest that surface along the surface's normal), so the structure goes where it would on
    /// the surface and shows as blocked if it runs into the level.
    /// Whether the level is in the way of the structure is its colliders' to say (<see cref="PlacementProbe.LevelOverlaps"/>). Free building, a footprint that isn't free on the grid
    /// (<see cref="CanPlace"/>) moves a cell to the side to the nearest free spot,
    /// if there is one; with <paramref name="snapToModules"/> it stays where the aim puts it, shown as blocked, so the
    /// player sees why rather than the ghost jumping somewhere they didn't aim.
    /// </para>
    /// <paramref name="target"/> is the empty cell across the picked face and <paramref name="normal"/> points out of
    /// the surface into that cell.
    /// </summary>
    public static bool TryGetPlacementTarget(Vector3 from, Vector3 direction, float range, IEnumerable<Vector3I> offsets, int quarterTurns,
        bool snapToModules, out Vector3I anchor, out Vector3I target, out Vector3I normal)
    {
        anchor = default;
        direction = direction.Normalized();
        RayHit ray = RaycastPastItems(from, from + direction * range);
        bool hit = ray.hit;
        float hitDistance = hit ? from.DistanceTo(ray.position) : range;

        // A structure's cells crossed by the ray snap to a full face of them; otherwise the cell on the open side
        // of the surface that was hit. aim is where the ray meets the face.
        Vector3 aim;
        // Smart placement walks whole modules, so a structure's footprint counts as solid modules: a slope's low end,
        // one cell high, can't be aimed at through the top of its cells from beside it.
        int blockCells = 1;
        if (snapToModules)
        {
            blockCells = ModuleCells;
        }
        if (TryFindOccupiedCellAlongRay(from, direction, 2f, hitDistance, blockCells, out target, out normal))
        {
            int axis = normal.X != 0 ? 0 : normal.Y != 0 ? 1 : 2;
            float plane = (target[axis] + (normal[axis] > 0 ? 0 : 1)) * CellSize;
            aim = from + direction * ((plane - from[axis]) / direction[axis]);
        }
        else
        {
            if (!hit)
            {
                return false;
            }
            normal = DominantAxis(ray.normal);
            // The first cell out from the surface it doesn't reach into by more than LevelOverlapDepth: the cell just
            // across a surface on a cell boundary, the next one out from a surface partway through a cell. Judged by
            // the surface hit alone; other level geometry around it (a wall beside the floor) is the probe's to judge.
            target = WorldToCell(ray.position + (Vector3)normal * (CellSize - LevelOverlapDepth));
            aim = ray.position;
            // That cell (its module, with smart placement) taken by other level geometry (a floor cell at the foot of a
            // wall): the last cell (module) the ray passes through before it enters one the level takes. Only which
            // block changes: the structure still stands on the surface aimed at, from that block's layer nearest it
            // along the surface's normal, so it goes where it would on the surface, even if it then runs into the level.
            Vector3I targetBlock = new(
                target.X - Mathf.PosMod(target.X, blockCells),
                target.Y - Mathf.PosMod(target.Y, blockCells),
                target.Z - Mathf.PosMod(target.Z, blockCells));
            if (AnyLevelIn(targetBlock, blockCells)
                && TryFindLevelBlockedCellAlongRay(from, direction, hitDistance + blockCells * CellSize, blockCells, out Vector3I before))
            {
                target = before;
                for (int axis = 0; axis < 3; axis++)
                {
                    if (normal[axis] != 0)
                    {
                        int blockStart = before[axis] - Mathf.PosMod(before[axis], blockCells);
                        target[axis] = blockStart;
                        if (normal[axis] < 0)
                        {
                            target[axis] = blockStart + blockCells - 1;
                        }
                    }
                }
            }
        }

        Vector3I min = new(int.MaxValue, int.MaxValue, int.MaxValue), max = new(int.MinValue, int.MinValue, int.MinValue);
        foreach (Vector3I offset in offsets)
        {
            Vector3I o = RotateOffset(offset, quarterTurns);
            for (int axis = 0; axis < 3; axis++)
            {
                min[axis] = Math.Min(min[axis], o[axis]);
                max[axis] = Math.Max(max[axis], o[axis]);
            }
        }
        for (int axis = 0; axis < 3; axis++)
        {
            if (normal[axis] != 0)
            {
                // Along the normal: the rearmost layer in the target cell's.
                anchor[axis] = target[axis] - (normal[axis] > 0 ? min[axis] : max[axis]);
                if (snapToModules && Mathf.PosMod(anchor[axis] + min[axis], ModuleCells) != 0)
                {
                    // Off a module corner: step away from the surface onto the next one.
                    anchor[axis] += normal[axis];
                }
            }
            else if (axis == 1)
            {
                // Height on a wall: stand on the target cell.
                int start = target[axis];
                if (snapToModules)
                {
                    // Or on the module it's in.
                    start -= Mathf.PosMod(start, ModuleCells);
                }
                anchor[axis] = start - min[axis];
            }
            else if (snapToModules)
            {
                // Across the face: the anchor's module is the target cell's, with the footprint starting on a module corner.
                int moduleStart = target[axis] - Mathf.PosMod(target[axis], ModuleCells);
                anchor[axis] = moduleStart + Mathf.PosMod(-min[axis], ModuleCells);
            }
            else
            {
                // Or, free building, the anchor cell is the target cell.
                anchor[axis] = target[axis];
            }
        }

        if (!snapToModules && !CanPlace(anchor, offsets, quarterTurns))
        {
            // Free building, partly in the way of something (a wall beside the floor, a structure): of the spots a
            // cell to the side across the face, the clear one whose anchor cell is nearest the aim. Left where it is,
            // and shown as blocked, when none is clear.
            int faceAxis = normal.X != 0 ? 0 : normal.Y != 0 ? 1 : 2;
            int across1 = (faceAxis + 1) % 3, across2 = (faceAxis + 2) % 3;
            Vector3I best = anchor;
            float bestDistance = float.MaxValue;
            for (int i = -1; i <= 1; i++)
            {
                for (int j = -1; j <= 1; j++)
                {
                    if (i == 0 && j == 0)
                    {
                        continue;
                    }
                    Vector3I candidate = anchor;
                    candidate[across1] += i;
                    candidate[across2] += j;
                    float distance = CellToWorld(candidate).DistanceSquaredTo(aim);
                    if (distance < bestDistance && CanPlace(candidate, offsets, quarterTurns))
                    {
                        best = candidate;
                        bestDistance = distance;
                    }
                }
            }
            anchor = best;
        }
        return true;
    }

    /// <summary>
    /// Walks the ray block by block (Amanatides-Woo voxel traversal; blocks of <paramref name="blockCells"/> cells a
    /// side on the grid, a cell or a module) up to <paramref name="maxDistance"/> and, at the first run of blocks with
    /// structures in them that it enters, snaps to a full face of that run, so thin structures act like solid blocks.
    /// <paramref name="target"/> is the cell across that face where the ray crosses it and <paramref name="normal"/>
    /// points from the run into it:
    /// <list type="bullet">
    /// <item>If the ray ends inside the run, the face it entered through, like hitting a solid block.</item>
    /// <item>If it passes through and out the other side, the face it exits through. Aiming just past a structure
    /// (over a thin conveyor at the floor in front of it) then lands where the player looks, rather than on
    /// whichever face the ray happened to clip on the way in. Out through the bottom face it finds nothing (returns
    /// false), so the surface the ray hits beyond is used rather than the cell under the run.</item>
    /// </list>
    /// The cell the ray starts in is skipped, so standing inside or on a structure's cell doesn't hide its
    /// neighbours, and so is any occupied cell the ray enters closer than <paramref name="minDistance"/>, so
    /// structures right in front of the camera don't grab the target.
    /// </summary>
    static bool TryFindOccupiedCellAlongRay(Vector3 from, Vector3 direction, float minDistance, float maxDistance, int blockCells,
        out Vector3I target, out Vector3I normal)
    {
        float blockSize = blockCells * CellSize;
        Vector3I block = new(Mathf.FloorToInt(from.X / blockSize), Mathf.FloorToInt(from.Y / blockSize), Mathf.FloorToInt(from.Z / blockSize));
        target = default;
        normal = default;
        bool inRun = false;
        float runEntryDistance = 0;
        Vector3I runEntryNormal = default;
        Vector3I step = new(Math.Sign(direction.X), Math.Sign(direction.Y), Math.Sign(direction.Z));
        // Distance along the ray to the next block boundary on each axis, and between boundaries.
        Vector3 next = default, delta = default;
        for (int axis = 0; axis < 3; axis++)
        {
            float d = direction[axis];
            if (d == 0)
            {
                next[axis] = float.PositiveInfinity;
                delta[axis] = float.PositiveInfinity;
                continue;
            }
            float boundary = (block[axis] + (d > 0 ? 1 : 0)) * blockSize;
            next[axis] = (boundary - from[axis]) / d;
            delta[axis] = blockSize / Math.Abs(d);
        }

        while (true)
        {
            int axis = next.X < next.Y ? (next.X < next.Z ? 0 : 2) : (next.Y < next.Z ? 1 : 2);
            float entryDistance = next[axis];
            if (entryDistance > maxDistance)
            {
                return false;
            }
            block[axis] += step[axis];
            next[axis] += delta[axis];
            bool occupied = entryDistance >= minDistance && AnyStructureIn(block * blockCells, blockCells);

            if (inRun && !occupied)
            {
                // Passed through the run and out of it: the face it exits through. Out through the bottom, the ray is
                // aimed at the floor beyond (a belt in the way), not under the run: the surface it hits counts instead.
                if (axis == 1 && step.Y < 0)
                {
                    return false;
                }
                normal = default;
                normal[axis] = step[axis];
                // The cell across the face where the ray crosses it.
                target = WorldToCell(from + direction * entryDistance + (Vector3)normal * HighlightEdgeNudge);
                return true;
            }
            if (!inRun && occupied)
            {
                inRun = true;
                runEntryNormal = default;
                runEntryNormal[axis] = -step[axis];
                runEntryDistance = entryDistance;
            }
            float exitDistance = Mathf.Min(next.X, Mathf.Min(next.Y, next.Z));
            if (inRun && exitDistance >= maxDistance - HighlightEdgeNudge)
            {
                // The ray ends inside the run: the face it entered through, at the cell outside it where the ray crossed it.
                normal = runEntryNormal;
                target = WorldToCell(from + direction * runEntryDistance + (Vector3)normal * HighlightEdgeNudge);
                return true;
            }
        }
    }

    /// <summary>
    /// World-space centre of the module holding a structure's anchor cell (the cell its root is the corner of), which
    /// placement puts under the crosshair and turns about; or of the anchor cell itself without
    /// <paramref name="snapToModules"/> (free building, off module corners).
    /// </summary>
    public static Vector3 AnchorModuleCentre(Vector3I anchor, int quarterTurns, bool snapToModules)
    {
        if (!snapToModules)
        {
            return CellToWorld(anchor);
        }
        return ((Vector3)AnchorModuleMin(anchor, quarterTurns) + Vector3.One * (ModuleCells / 2f)) * CellSize;
    }

    /// <summary>
    /// The min cell of a structure's anchor module at <paramref name="anchor"/>, turned <paramref name="quarterTurns"/>:
    /// the module-sized block of its own frame that starts at its anchor cell and grows the way its footprint does
    /// (+X, +Y, -Z). On a module corner this is the module holding the anchor cell; a pose that isn't (connected to
    /// an end) has one too, which is what turning it keeps in place.
    /// </summary>
    public static Vector3I AnchorModuleMin(Vector3I anchor, int quarterTurns)
    {
        Vector3I min = new(int.MaxValue, anchor.Y, int.MaxValue);
        foreach (Vector3I corner in new Vector3I[] { new(0, 0, 0), new(ModuleCells - 1, 0, 0), new(0, 0, 1 - ModuleCells), new(ModuleCells - 1, 0, 1 - ModuleCells) })
        {
            Vector3I cell = anchor + RotateOffset(corner, quarterTurns);
            min.X = Math.Min(min.X, cell.X);
            min.Z = Math.Min(min.Z, cell.Z);
        }
        return min;
    }

    // Walks the ray block by block (blocks of blockCells cells a side, a cell or a module), as
    // TryFindOccupiedCellAlongRay does, up to maxDistance, to the first block with a cell level geometry blocks
    // (IsLevelBlocked): target is the cell just before the face the ray enters it through, where it crosses it. False
    // if it reaches none.
    static bool TryFindLevelBlockedCellAlongRay(Vector3 from, Vector3 direction, float maxDistance, int blockCells, out Vector3I target)
    {
        float blockSize = blockCells * CellSize;
        Vector3I block = new(Mathf.FloorToInt(from.X / blockSize), Mathf.FloorToInt(from.Y / blockSize), Mathf.FloorToInt(from.Z / blockSize));
        target = default;
        Vector3I step = new(Math.Sign(direction.X), Math.Sign(direction.Y), Math.Sign(direction.Z));
        Vector3 next = default, delta = default;
        for (int axis = 0; axis < 3; axis++)
        {
            float d = direction[axis];
            if (d == 0)
            {
                next[axis] = float.PositiveInfinity;
                delta[axis] = float.PositiveInfinity;
                continue;
            }
            float boundary = (block[axis] + (d > 0 ? 1 : 0)) * blockSize;
            next[axis] = (boundary - from[axis]) / d;
            delta[axis] = blockSize / Math.Abs(d);
        }

        while (true)
        {
            int axis = next.X < next.Y ? (next.X < next.Z ? 0 : 2) : (next.Y < next.Z ? 1 : 2);
            float entryDistance = next[axis];
            if (entryDistance > maxDistance)
            {
                return false;
            }
            block[axis] += step[axis];
            next[axis] += delta[axis];
            if (AnyLevelIn(block * blockCells, blockCells))
            {
                // Nudged back out of the face it enters through.
                Vector3 back = Vector3.Zero;
                back[axis] = -step[axis] * HighlightEdgeNudge;
                target = WorldToCell(from + direction * entryDistance + back);
                return true;
            }
        }
    }

    // Whether level geometry blocks any of the size x size x size cells from first (its min corner).
    static bool AnyLevelIn(Vector3I first, int size)
    {
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int z = 0; z < size; z++)
                {
                    if (IsLevelBlocked(first + new Vector3I(x, y, z)))
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    // Whether any of the size x size x size cells from first (its min corner) has a structure in it.
    static bool AnyStructureIn(Vector3I first, int size)
    {
        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                for (int z = 0; z < size; z++)
                {
                    if (GetStructureAt(first + new Vector3I(x, y, z)) != null)
                    {
                        return true;
                    }
                }
            }
        }
        return false;
    }

    /// <summary>The nearest hit along the ray that isn't a loose item: items aren't surfaces to build on or aim at.</summary>
    public static RayHit RaycastPastItems(Vector3 from, Vector3 to)
    {
        // Loose items are all on their own layer.
        return PhysicsWorld.Raycast(from, to, ~GameWorld.QueryHiddenLayer & ~PhysicsWorld.ItemLayer);
    }

    /// <summary>The unit grid axis closest to <paramref name="v"/>.</summary>
    public static Vector3I DominantAxis(Vector3 v)
    {
        Vector3 a = v.Abs();
        if (a.X >= a.Y && a.X >= a.Z) return new Vector3I(Math.Sign(v.X), 0, 0);
        if (a.Y >= a.Z) return new Vector3I(0, Math.Sign(v.Y), 0);
        return new Vector3I(0, 0, Math.Sign(v.Z));
    }

    /// <summary>
    /// The Structure a ray hit, or null. See <see cref="FindStructure(Node)"/>.
    /// </summary>
    public static Structure FindStructure(in RayHit hit)
    {
        if (!hit.hit)
        {
            return null;
        }
        return FindStructure(PhysicsWorld.BodyNode(hit.id));
    }

    /// <summary>The Structure a collider belongs to (it may be a child body), or null.</summary>
    public static Structure FindStructure(Node node)
    {
        for (Node n = node; n != null; n = n.GetParent())
        {
            if (n is Structure s)
            {
                return s;
            }
        }
        return null;
    }

    private void BuildGridMesh()
    {
        _immediateMesh.ClearSurfaces();
        _immediateMesh.SurfaceBegin(Mesh.PrimitiveType.Lines);

        float half = DrawExtent / 2f;
        int steps = Mathf.RoundToInt(DrawExtent / CellSize);

        for (int i = 0; i <= steps; i++)
        {
            float offset = -half + i * CellSize;

            // XZ plane
            _immediateMesh.SurfaceAddVertex(new Vector3(offset, 0, -half));
            _immediateMesh.SurfaceAddVertex(new Vector3(offset, 0, half));
            _immediateMesh.SurfaceAddVertex(new Vector3(-half, 0, offset));
            _immediateMesh.SurfaceAddVertex(new Vector3(half, 0, offset));

            // XY plane
            _immediateMesh.SurfaceAddVertex(new Vector3(offset, -half, 0));
            _immediateMesh.SurfaceAddVertex(new Vector3(offset, half, 0));
            _immediateMesh.SurfaceAddVertex(new Vector3(-half, offset, 0));
            _immediateMesh.SurfaceAddVertex(new Vector3(half, offset, 0));

            // YZ plane
            _immediateMesh.SurfaceAddVertex(new Vector3(0, offset, -half));
            _immediateMesh.SurfaceAddVertex(new Vector3(0, offset, half));
            _immediateMesh.SurfaceAddVertex(new Vector3(0, -half, offset));
            _immediateMesh.SurfaceAddVertex(new Vector3(0, half, offset));
        }

        _immediateMesh.SurfaceEnd();
        _meshBuilt = true;
    }

    public static Vector3I WorldToCell(Vector3 pos)
    {
        return new Vector3I(
            Mathf.FloorToInt(pos.X / CellSize),
            Mathf.FloorToInt(pos.Y / CellSize),
            Mathf.FloorToInt(pos.Z / CellSize)
        );
    }

    public static Vector3 CellToWorld(Vector3I cell)
    {
        return new Vector3(
            (cell.X + 0.5f) * CellSize,
            (cell.Y + 0.5f) * CellSize,
            (cell.Z + 0.5f) * CellSize
        );
    }

    // ---- occupancy --------------------------------------------------------

    /// <summary>
    /// Rotates a cell offset by <paramref name="quarterTurns"/> steps of +90° yaw (Godot's positive Y
    /// rotation, counter-clockwise seen from above), matching a node rotated by the same angle.
    /// </summary>
    public static Vector3I RotateOffset(Vector3I offset, int quarterTurns)
    {
        for (int i = 0; i < Mathf.PosMod(quarterTurns, 4); i++)
        {
            offset = new Vector3I(offset.Z, offset.Y, -offset.X);
        }
        return offset;
    }

    /// <summary>The rotation of a structure turned by <paramref name="quarterTurns"/> steps of +90° yaw.</summary>
    public static Basis QuarterTurnBasis(int quarterTurns)
    {
        return Basis.FromEuler(new Vector3(0, Mathf.PosMod(quarterTurns, 4) * Mathf.Pi / 2, 0));
    }

    /// <summary>The world cells covered by a footprint of <paramref name="offsets"/> anchored at <paramref name="anchor"/>.</summary>
    public static IEnumerable<Vector3I> FootprintCells(Vector3I anchor, IEnumerable<Vector3I> offsets, int quarterTurns)
    {
        foreach (Vector3I offset in offsets)
        {
            yield return anchor + RotateOffset(offset, quarterTurns);
        }
    }

    /// <summary>True if every cell of the footprint is free and outside every <see cref="NoBuildVolume"/>.</summary>
    public static bool CanPlace(Vector3I anchor, IEnumerable<Vector3I> offsets, int quarterTurns)
    {
        foreach (Vector3I cell in FootprintCells(anchor, offsets, quarterTurns))
        {
            if (cells.ContainsKey(cell) || IsNoBuild(cell))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>True if any <see cref="NoBuildVolume"/> overlaps <paramref name="cell"/>. Sharing only a face doesn't count.</summary>
    public static bool IsNoBuild(Vector3I cell)
    {
        var cellBox = new Aabb((Vector3)cell * CellSize, Vector3.One * CellSize);
        foreach (NoBuildVolume volume in noBuildVolumes)
        {
            if (volume.WorldBox.Intersects(cellBox))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Registers every cell of <paramref name="structure"/>. Fails (registering nothing) if any is taken.</summary>
    public static bool Occupy(Structure structure, out List<Vector3I> placedIn)
    {
        if (!CanPlace(structure.anchor, structure.cells.Keys, structure.quarterTurns))
        {
            placedIn = null;
            return false;
        }
        placedIn = new();
        foreach (Vector3I cell in FootprintCells(structure.anchor, structure.cells.Keys, structure.quarterTurns))
        {
            placedIn.Add(cell);
            cells[cell] = structure;
            MarkFillDirty(cell);
        }
        return true;
    }

    /// <summary>Frees every cell registered to <paramref name="structure"/>.</summary>
    public static void Vacate(Structure structure)
    {
        foreach (Vector3I cell in FootprintCells(structure.anchor, structure.cells.Keys, structure.quarterTurns))
        {
            if (cells.TryGetValue(cell, out Structure existing) && existing == structure)
            {
                cells.Remove(cell);
                MarkFillDirty(cell);
            }
        }
    }

    /// <summary>The structure occupying <paramref name="cell"/>, or null.</summary>
    public static Structure GetStructureAt(Vector3I cell)
    {
        return cells.TryGetValue(cell, out Structure s) ? s : null;
    }
}
