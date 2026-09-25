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
    /// <summary>Set while the local player holds a blueprint; also shows the grid lines.</summary>
    public static bool placementActive = false;
    public static bool highlightLookedAtCell = false;
    public static Vector3I? highlightedCell = null;

    public const float CellSize = 2.0f;
    private const float DrawExtent = 200.0f;
    private const float HighlightRaycastRange = 20.0f;
    private const float HighlightEdgeNudge = 0.001f;

    private static readonly Dictionary<Vector3I, Structure> cells = new();

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
    }

    public override void _Process(double delta)
    {
        if (enabled || placementActive)
        {
            if (!_meshBuilt)
            {
                BuildGridMesh();
            }
            _meshInstance.Visible = true;
        }
        else
        {
            _meshInstance.Visible = false;
        }

        if (highlightLookedAtCell)
        {
            UpdateHighlightedCell();
        }
        else
        {
            _highlightMeshInstance.Visible = false;
            highlightedCell = null;
        }
    }

    public static void Toggle() => enabled = !enabled;
    public static void ToggleHighlight() => highlightLookedAtCell = !highlightLookedAtCell;

    private void UpdateHighlightedCell()
    {
        Camera3D camera = GetViewport().GetCamera3D();
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

    /// <summary>
    /// Raycasts along the camera's view and returns the empty-side cell of the surface it hits: the cell
    /// in front of whatever face you look at (on top of a floor, beside a wall or structure).
    /// </summary>
    public static bool TryGetLookedAtCell(Camera3D camera, float range, out Vector3I cell)
    {
        Vector3 from = camera.GlobalPosition;
        Vector3 to = from + -camera.GlobalTransform.Basis.Z * range;
        var ray = GameWorld.Raycast(from, to);
        if (!ray["hit"].AsBool())
        {
            cell = default;
            return false;
        }

        // Nudge off the surface along its normal before flooring, so a hit that lands exactly on a
        // cell boundary (e.g. a floor at a grid line) resolves to the cell above the surface rather
        // than the one buried inside it.
        cell = WorldToCell(ray["position"].AsVector3() + ray["normal"].AsVector3() * HighlightEdgeNudge);
        return true;
    }

    /// <summary>
    /// Where a structure with footprint <paramref name="offsets"/> (rotated by <paramref name="quarterTurns"/>)
    /// would be anchored when placed by looking along a ray. The ray picks a face:
    /// <list type="bullet">
    /// <item>If the ray enters a cell occupied by a structure before it hits any collider, it snaps to the face
    /// of that cell it crossed: whatever the structure's colliders look like (a thin conveyor, a slope), every
    /// occupied cell presents its full faces, and the target is the cell across that face.</item>
    /// <item>Otherwise the target is the cell on the open side of the surface the ray hit (its normal snapped to
    /// the nearest axis, so slopes work).</item>
    /// </list>
    /// The footprint then grows away from that face: it is shifted along the face normal so its rearmost
    /// cell sits in the target cell, e.g. a 2x1x1 put against a wall's side sticks out of the wall.
    /// </summary>
    public static bool TryGetPlacementTarget(Vector3 from, Vector3 direction, float range, IEnumerable<Vector3I> offsets, int quarterTurns, out Vector3I anchor)
    {
        anchor = default;
        direction = direction.Normalized();
        var ray = GameWorld.Raycast(from, from + direction * range);
        bool hit = ray["hit"].AsBool();
        float hitDistance = hit ? from.DistanceTo(ray["position"].AsVector3()) : range;

        Vector3I target, normal;
        if (TryFindOccupiedCellAlongRay(from, direction, hitDistance, out Vector3I entered, out normal))
        {
            // The ray reached a structure's cell before any collider: snap to the face of that cell it crossed.
            target = entered + normal;
        }
        else if (hit)
        {
            // Otherwise the cell on the open side of the surface that was hit.
            normal = DominantAxis(ray["normal"].AsVector3());
            target = WorldToCell(ray["position"].AsVector3() + (Vector3)normal * HighlightEdgeNudge);
        }
        else
        {
            return false;
        }

        int rearmost = int.MaxValue;
        foreach (Vector3I offset in offsets)
        {
            Vector3I o = RotateOffset(offset, quarterTurns);
            rearmost = Mathf.Min(rearmost, o.X * normal.X + o.Y * normal.Y + o.Z * normal.Z);
        }
        anchor = target - normal * rearmost;
        return true;
    }

    /// <summary>
    /// Walks the ray cell by cell (Amanatides-Woo voxel traversal) and returns the first cell occupied by a
    /// structure that it enters within <paramref name="maxDistance"/>, with the normal of the face it entered
    /// through (pointing back out of that cell). The cell the ray starts in is skipped, so standing inside or
    /// on a structure's cell doesn't hide its neighbours.
    /// </summary>
    static bool TryFindOccupiedCellAlongRay(Vector3 from, Vector3 direction, float maxDistance, out Vector3I cell, out Vector3I normal)
    {
        cell = WorldToCell(from);
        normal = default;
        Vector3I step = new(Math.Sign(direction.X), Math.Sign(direction.Y), Math.Sign(direction.Z));
        // Distance along the ray to the next cell boundary on each axis, and between boundaries.
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
            float boundary = (cell[axis] + (d > 0 ? 1 : 0)) * CellSize;
            next[axis] = (boundary - from[axis]) / d;
            delta[axis] = CellSize / Math.Abs(d);
        }

        while (true)
        {
            int axis = next.X < next.Y ? (next.X < next.Z ? 0 : 2) : (next.Y < next.Z ? 1 : 2);
            if (next[axis] > maxDistance)
            {
                return false;
            }
            cell[axis] += step[axis];
            next[axis] += delta[axis];
            if (GetStructureAt(cell) != null)
            {
                normal = default;
                normal[axis] = -step[axis];
                return true;
            }
        }
    }

    /// <summary>The unit grid axis closest to <paramref name="v"/>.</summary>
    public static Vector3I DominantAxis(Vector3 v)
    {
        Vector3 a = v.Abs();
        if (a.X >= a.Y && a.X >= a.Z) return new Vector3I(Math.Sign(v.X), 0, 0);
        if (a.Y >= a.Z) return new Vector3I(0, Math.Sign(v.Y), 0);
        return new Vector3I(0, 0, Math.Sign(v.Z));
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

    /// <summary>True if every cell of the footprint is free.</summary>
    public static bool CanPlace(Vector3I anchor, IEnumerable<Vector3I> offsets, int quarterTurns)
    {
        foreach (Vector3I cell in FootprintCells(anchor, offsets, quarterTurns))
        {
            if (cells.ContainsKey(cell))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Registers every cell of <paramref name="structure"/>. Fails (registering nothing) if any is taken.</summary>
    public static bool Occupy(Structure structure)
    {
        if (!CanPlace(structure.anchor, structure.cellOffsets, structure.quarterTurns))
        {
            return false;
        }
        foreach (Vector3I cell in FootprintCells(structure.anchor, structure.cellOffsets, structure.quarterTurns))
        {
            cells[cell] = structure;
        }
        return true;
    }

    /// <summary>Frees every cell registered to <paramref name="structure"/>.</summary>
    public static void Vacate(Structure structure)
    {
        foreach (Vector3I cell in FootprintCells(structure.anchor, structure.cellOffsets, structure.quarterTurns))
        {
            if (cells.TryGetValue(cell, out Structure existing) && existing == structure)
            {
                cells.Remove(cell);
            }
        }
    }

    /// <summary>The structure occupying <paramref name="cell"/>, or null.</summary>
    public static Structure GetStructureAt(Vector3I cell)
    {
        return cells.TryGetValue(cell, out Structure s) ? s : null;
    }

    /// <summary>Number of occupied cells (used by the headless multiplayer test).</summary>
    public static int occupiedCellCount => cells.Count;
}
