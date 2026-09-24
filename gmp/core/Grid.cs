using Godot;
using System.Collections.Generic;

public partial class Grid : Node3D
{
    public static Grid instance;
    public static bool enabled = false;
    public static bool highlightLookedAtCell = false;
    public static (int, int, int)? highlightedCell = null;

    public const float CellSize = 2.0f;
    private const float DrawExtent = 200.0f;
    private const float HighlightRaycastRange = 20.0f;
    private const float HighlightEdgeNudge = 0.001f;

    public static Dictionary<(int, int, int), object> cells = new();

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
        if (enabled)
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
        if (camera == null)
        {
            _highlightMeshInstance.Visible = false;
            highlightedCell = null;
            return;
        }

        Vector3 from = camera.GlobalPosition;
        Vector3 to = from + -camera.GlobalTransform.Basis.Z * HighlightRaycastRange;
        var ray = GameWorld.Raycast(from, to);

        if (!ray["hit"].AsBool())
        {
            _highlightMeshInstance.Visible = false;
            highlightedCell = null;
            return;
        }

        Vector3 hitPosition = ray["position"].AsVector3();
        Vector3 hitNormal = ray["normal"].AsVector3();

        // Nudge off the surface along its normal before flooring, so a hit that
        // lands exactly on a cell boundary (e.g. a floor at a grid line) resolves
        // to the cell above the surface rather than the one buried inside it.
        var cell = WorldToCell(hitPosition + hitNormal * HighlightEdgeNudge);

        highlightedCell = cell;
        _highlightMeshInstance.Position = CellToWorld(cell);
        _highlightMeshInstance.Visible = true;
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

    public static (int, int, int) WorldToCell(Vector3 pos)
    {
        return (
            Mathf.FloorToInt(pos.X / CellSize),
            Mathf.FloorToInt(pos.Y / CellSize),
            Mathf.FloorToInt(pos.Z / CellSize)
        );
    }

    public static Vector3 CellToWorld((int, int, int) cell)
    {
        return new Vector3(
            (cell.Item1 + 0.5f) * CellSize,
            (cell.Item2 + 0.5f) * CellSize,
            (cell.Item3 + 0.5f) * CellSize
        );
    }

    public static bool Add(object obj, Vector3 worldPos)
    {
        var cell = WorldToCell(worldPos);
        if (cells.ContainsKey(cell))
        {
            return false;
        }
        cells[cell] = obj;
        return true;
    }

    public static bool Remove(object obj, Vector3 worldPos)
    {
        var cell = WorldToCell(worldPos);
        if (cells.TryGetValue(cell, out var existing) && Equals(existing, obj))
        {
            cells.Remove(cell);
            return true;
        }
        return false;
    }

    public static bool Move(object obj, Vector3 oldWorldPos, Vector3 newWorldPos)
    {
        var oldCell = WorldToCell(oldWorldPos);
        var newCell = WorldToCell(newWorldPos);

        if (oldCell == newCell)
        {
            return true;
        }

        if (!cells.TryGetValue(oldCell, out var existing) || !Equals(existing, obj))
        {
            return false;
        }

        if (cells.ContainsKey(newCell))
        {
            return false;
        }

        cells.Remove(oldCell);
        cells[newCell] = obj;
        return true;
    }

    public static object GetObjectAt(Vector3 worldPos)
    {
        var cell = WorldToCell(worldPos);
        return cells.TryGetValue(cell, out var obj) ? obj : null;
    }

    public static IEnumerable<object> GetNeighbors(Vector3 worldPos)
    {
        var (cx, cy, cz) = WorldToCell(worldPos);

        for (int dx = -1; dx <= 1; dx++)
        {
            for (int dy = -1; dy <= 1; dy++)
            {
                for (int dz = -1; dz <= 1; dz++)
                {
                    if (dx == 0 && dy == 0 && dz == 0)
                    {
                        continue;
                    }

                    if (cells.TryGetValue((cx + dx, cy + dy, cz + dz), out var obj))
                    {
                        yield return obj;
                    }
                }
            }
        }
    }
}
