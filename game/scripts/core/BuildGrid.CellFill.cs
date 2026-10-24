using Godot;
using System.Collections.Generic;

/// <summary>
/// BuildGrid: the cell fill shown with the grid while a blueprint is held. Every cell that can't be built in is
/// solid: a structure's cell, one a <see cref="NoBuildVolume"/> touches, or one the level's static geometry reaches
/// into (by more than <see cref="LevelOverlapDepth"/>, judged as placement judges it,
/// <see cref="PlacementProbe.IsStaticLevelCollider"/>). The faces of solid cells that border open cells are filled
/// with a faint colour per kind, lit by the ghost like the grid lines are (the same reveals, see BuildGrid.Reveal.cs):
/// they fade with distance from it and faces turned away from it are dimmer, so the floor, structures and walls
/// around the ghost read as shaded blocks. See BuildGridFill.gdshader.
/// <para>
/// The fill is built in chunks of <see cref="FillChunkCells"/>³ cells around the ghost, fixed in the world. Which cells
/// level geometry blocks is found with one physics overlap query per cell, a little each physics tick, and kept for the
/// session (the level doesn't move). Structures and no-build volumes are read from the grid whenever a chunk's mesh is
/// rebuilt, which placing or removing a structure triggers (<see cref="Occupy"/>, <see cref="Vacate"/>).
/// </para>
/// </summary>
public partial class BuildGrid
{
    /// <summary>What keeps a cell from being built in, as the fill colours it (its value is the shader's kind).</summary>
    enum CellBlock { None = -1, Level = 0, Structure = 1, NoBuild = 2 }

    const int FillChunkCells = 8;
    const int FillChunkVolume = FillChunkCells * FillChunkCells * FillChunkCells;
    /// <summary>How far (m) level geometry must reach into a cell to block it; less, like touching it, doesn't count.</summary>
    public const float LevelOverlapDepth = 0.05f;
    /// <summary>Time each physics tick may spend scanning level geometry and building fill meshes, in ms.</summary>
    const double FillWorkBudgetMs = 1.5;

    class FillChunk
    {
        public Vector3I coord;
        /// <summary>Whether level geometry blocks each cell, indexed by <see cref="FillIndex"/>.</summary>
        public readonly bool[] level = new bool[FillChunkVolume];
        /// <summary>Cells scanned for level geometry so far; -1 until the chunk as a whole has been checked.</summary>
        public int scanned = -1;
        /// <summary>True when the mesh is out of date with the cells.</summary>
        public bool dirty = true;
        public MeshInstance3D instance;
        /// <summary>Time (ms) until which the chunk stays shown: it is near a reveal, or one fading out there.</summary>
        public ulong shownUntil;
        public bool scanDone => scanned == FillChunkVolume;
    }

    static readonly Dictionary<Vector3I, FillChunk> fillChunks = new();
    static readonly List<FillChunk> shownFillChunks = new();
    static readonly List<FillChunk> nearFillChunks = new();
    static ShaderMaterial _fillMaterial;
    static readonly System.Diagnostics.Stopwatch fillWorkTimer = new();

    // Reused while building a chunk's mesh.
    static readonly List<Vector3> fillVertices = new();
    static readonly List<Vector3> fillNormals = new();
    static readonly List<Vector2> fillUVs = new();
    static readonly List<int> fillIndices = new();
    static readonly List<Aabb> fillNoBuildBoxes = new();

    static readonly Vector3I[] FaceDirections =
    [
        Vector3I.Right, Vector3I.Left, Vector3I.Up, Vector3I.Down, Vector3I.Back, Vector3I.Forward,
    ];

    private void CreateFill()
    {
        _fillMaterial = new ShaderMaterial
        {
            Shader = GD.Load<Shader>("res://game/assets/shaders/BuildGridFill.gdshader"),
            // Under the grid lines, which are drawn over it.
            RenderPriority = -1,
        };
    }

    /// <summary>
    /// Runs every physics tick (physics queries belong there): keeps the chunks around the ghost shown and scanned and
    /// their meshes current, within <see cref="FillWorkBudgetMs"/>, and hides chunks no reveal is near any more.
    /// </summary>
    private void UpdateFill()
    {
        ulong now = Time.GetTicksMsec();
        nearFillChunks.Clear();
        if (placementActive && placementFocus is Vector3 focus)
        {
            float radius = revealRadius;
            Vector3I lo = FillChunkOf(WorldToCell(focus - Vector3.One * radius));
            Vector3I hi = FillChunkOf(WorldToCell(focus + Vector3.One * radius));
            for (int x = lo.X; x <= hi.X; x++)
            {
                for (int y = lo.Y; y <= hi.Y; y++)
                {
                    for (int z = lo.Z; z <= hi.Z; z++)
                    {
                        Vector3I coord = new(x, y, z);
                        Vector3 min = (Vector3)(coord * FillChunkCells) * CellSize;
                        Vector3 closest = focus.Clamp(min, min + Vector3.One * (FillChunkCells * CellSize));
                        if (closest.DistanceTo(focus) > radius)
                        {
                            continue;
                        }
                        if (!fillChunks.TryGetValue(coord, out FillChunk chunk))
                        {
                            chunk = new FillChunk { coord = coord };
                            fillChunks[coord] = chunk;
                        }
                        nearFillChunks.Add(chunk);
                        // A reveal left behind here fades out over RevealFadeOutTime; the fill stays until it's gone.
                        chunk.shownUntil = now + (ulong)(RevealFadeOutTime * 1000f) + 50;
                        if (!shownFillChunks.Contains(chunk))
                        {
                            shownFillChunks.Add(chunk);
                            if (chunk.instance != null)
                            {
                                chunk.instance.Visible = true;
                            }
                        }
                    }
                }
            }
            DoFillWork(focus);
        }

        for (int i = shownFillChunks.Count - 1; i >= 0; i--)
        {
            FillChunk chunk = shownFillChunks[i];
            if (now > chunk.shownUntil)
            {
                if (chunk.instance != null)
                {
                    chunk.instance.Visible = false;
                }
                shownFillChunks.RemoveAt(i);
            }
        }
    }

    // Scans and builds the chunks near the ghost, nearest first, until the tick's budget is spent.
    void DoFillWork(Vector3 focus)
    {
        fillWorkTimer.Restart();
        while (fillWorkTimer.Elapsed.TotalMilliseconds < FillWorkBudgetMs)
        {
            FillChunk next = null;
            float nextDistance = float.MaxValue;
            foreach (FillChunk chunk in nearFillChunks)
            {
                if (chunk.scanDone && !chunk.dirty)
                {
                    continue;
                }
                Vector3 centre = ((Vector3)(chunk.coord * FillChunkCells) + Vector3.One * (FillChunkCells / 2f)) * CellSize;
                float distance = centre.DistanceSquaredTo(focus);
                if (distance < nextDistance)
                {
                    next = chunk;
                    nextDistance = distance;
                }
            }
            if (next == null)
            {
                return;
            }
            if (!next.scanDone)
            {
                ScanFillStep(next);
            }
            else
            {
                BuildFillMesh(next);
            }
        }
    }

    // One step of finding which of a chunk's cells level geometry blocks: first whether any level geometry is near
    // the chunk at all (most chunks are open air and need nothing more), then one cell at a time.
    static void ScanFillStep(FillChunk chunk)
    {
        Vector3 chunkMin = (Vector3)(chunk.coord * FillChunkCells) * CellSize;
        if (chunk.scanned < 0)
        {
            // A little past its sides, so a floor lying on its top face counts (QueryLevelBlocked).
            var box = new Aabb(chunkMin, Vector3.One * (FillChunkCells * CellSize)).Grow(2f * LevelOverlapDepth);
            long[] near = PhysicsWorld.OverlapBox(new Transform3D(Basis.Identity, box.GetCenter()), box.Size, ~GameWorld.QueryHiddenLayer & ~PhysicsWorld.ItemLayer);
            bool anyLevel = false;
            foreach (long id in near)
            {
                if (PlacementProbe.IsStaticLevelCollider(PhysicsWorld.BodyNode((ulong)id)))
                {
                    anyLevel = true;
                    break;
                }
            }
            chunk.scanned = anyLevel ? 0 : FillChunkVolume;
        }
        else
        {
            int i = chunk.scanned;
            Vector3I local = new(i % FillChunkCells, i / FillChunkCells % FillChunkCells, i / (FillChunkCells * FillChunkCells));
            chunk.level[i] = QueryLevelBlocked(chunk.coord * FillChunkCells + local);
            chunk.scanned++;
        }

        if (chunk.scanDone)
        {
            chunk.dirty = true;
            // Until now the neighbours took this chunk's cells for open: their faces against it may be wrong.
            foreach (Vector3I direction in FaceDirections)
            {
                if (fillChunks.TryGetValue(chunk.coord + direction, out FillChunk neighbour))
                {
                    neighbour.dirty = true;
                }
            }
        }
    }

    // Rebuilds a chunk's mesh: a quad on every face of its solid cells that borders an open cell.
    static void BuildFillMesh(FillChunk chunk)
    {
        chunk.dirty = false;
        fillVertices.Clear();
        fillNormals.Clear();
        fillUVs.Clear();
        fillIndices.Clear();

        // The no-build volumes near the chunk, read once rather than for every cell.
        Vector3I origin = chunk.coord * FillChunkCells;
        var around = new Aabb((Vector3)(origin - Vector3I.One) * CellSize, Vector3.One * ((FillChunkCells + 2) * CellSize));
        fillNoBuildBoxes.Clear();
        foreach (NoBuildVolume volume in noBuildVolumes)
        {
            Aabb box = volume.WorldBox;
            if (box.Intersects(around))
            {
                fillNoBuildBoxes.Add(box);
            }
        }

        for (int z = 0; z < FillChunkCells; z++)
        {
            for (int y = 0; y < FillChunkCells; y++)
            {
                for (int x = 0; x < FillChunkCells; x++)
                {
                    Vector3I cell = origin + new Vector3I(x, y, z);
                    CellBlock block = FillBlockAt(cell);
                    if (block == CellBlock.None)
                    {
                        continue;
                    }
                    foreach (Vector3I direction in FaceDirections)
                    {
                        if (FillBlockAt(cell + direction) == CellBlock.None)
                        {
                            AddFillFace(cell, direction, block);
                        }
                    }
                }
            }
        }

        if (fillIndices.Count == 0)
        {
            if (chunk.instance != null)
            {
                chunk.instance.Mesh = null;
            }
            return;
        }
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = fillVertices.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = fillNormals.ToArray();
        arrays[(int)Mesh.ArrayType.TexUV] = fillUVs.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = fillIndices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);

        if (chunk.instance == null)
        {
            chunk.instance = new MeshInstance3D
            {
                MaterialOverride = _fillMaterial,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                TopLevel = true,
                Visible = shownFillChunks.Contains(chunk),
            };
            instance.AddChild(chunk.instance);
        }
        chunk.instance.Mesh = mesh;
    }

    /// <summary>
    /// True if the level's static geometry reaches more than <see cref="LevelOverlapDepth"/> into
    /// <paramref name="cell"/>, as the fill shows it (placement ignores it: the structure's own colliders decide,
    /// <see cref="PlacementProbe.LevelOverlaps"/>). Read from the fill's scan where it has reached the
    /// cell, otherwise asked of the physics world (call from <c>_PhysicsProcess</c>).
    /// </summary>
    public static bool IsLevelBlocked(Vector3I cell)
    {
        if (fillChunks.TryGetValue(FillChunkOf(cell), out FillChunk chunk) && chunk.scanned > FillIndex(cell))
        {
            return chunk.level[FillIndex(cell)];
        }
        return QueryLevelBlocked(cell);
    }

    static bool QueryLevelBlocked(Vector3I cell)
    {
        Vector3 size = Vector3.One * (CellSize - 2f * LevelOverlapDepth);
        foreach (long id in PhysicsWorld.OverlapBox(new Transform3D(Basis.Identity, CellToWorld(cell)), size, ~GameWorld.QueryHiddenLayer & ~PhysicsWorld.ItemLayer))
        {
            if (PlacementProbe.IsStaticLevelCollider(PhysicsWorld.BodyNode((ulong)id)))
            {
                return true;
            }
        }
        // A level surface lying on one of the cell's faces (within LevelOverlapDepth) and facing out of it, so the
        // level is behind it, in the cell: a mesh collider is only a surface, with no inside for the overlap to find,
        // so a floor exactly on a cell line would otherwise leave the cell under it open.
        foreach (Vector3I direction in FaceDirections)
        {
            Vector3 face = CellToWorld(cell) + (Vector3)direction * (CellSize / 2f);
            if (LevelSurfaceFacing(face + (Vector3)direction * LevelOverlapDepth, face - (Vector3)direction * LevelOverlapDepth, direction))
            {
                return true;
            }
        }
        return false;
    }

    // Whether the ray from `from` to `to` meets a level collider's surface facing toward `facing` (within 60°), passing
    // through anything else (a structure standing there) on the way.
    static bool LevelSurfaceFacing(Vector3 from, Vector3 to, Vector3I facing)
    {
        Vector3 step = (to - from).Normalized() * 0.001f;
        Vector3 start = from;
        for (int i = 0; i < 4; i++)
        {
            RayHit hit = PhysicsWorld.Raycast(start, to, ~GameWorld.QueryHiddenLayer & ~PhysicsWorld.ItemLayer);
            if (!hit.hit)
            {
                return false;
            }
            if (PlacementProbe.IsStaticLevelCollider(PhysicsWorld.BodyNode(hit.id)))
            {
                return hit.normal.Dot(facing) > 0.5f;
            }
            start = hit.position + step;
        }
        return false;
    }

    // What keeps cell from being built in, for the fill. A structure counts over a no-build volume over level geometry.
    // Level geometry in a chunk not scanned yet counts as open; the chunk's neighbours are rebuilt once it is.
    static CellBlock FillBlockAt(Vector3I cell)
    {
        if (cells.ContainsKey(cell))
        {
            return CellBlock.Structure;
        }
        var cellBox = new Aabb((Vector3)cell * CellSize, Vector3.One * CellSize);
        foreach (Aabb box in fillNoBuildBoxes)
        {
            if (box.Intersects(cellBox))
            {
                return CellBlock.NoBuild;
            }
        }
        if (fillChunks.TryGetValue(FillChunkOf(cell), out FillChunk chunk) && chunk.scanDone && chunk.level[FillIndex(cell)])
        {
            return CellBlock.Level;
        }
        return CellBlock.None;
    }

    // A quad over the face of cell on the side of normal, facing out (Godot's front faces wind clockwise), its kind
    // in UV.x for the shader.
    static void AddFillFace(Vector3I cell, Vector3I normal, CellBlock block)
    {
        int axis = normal.X != 0 ? 0 : normal.Y != 0 ? 1 : 2;
        // The two axes across the face, ordered so that u x v points along the normal.
        Vector3 u = Vector3.Zero, v = Vector3.Zero;
        u[(axis + 1) % 3] = CellSize;
        v[(axis + 2) % 3] = CellSize;
        if (normal[axis] < 0)
        {
            (u, v) = (v, u);
        }
        Vector3 corner = (Vector3)cell * CellSize;
        if (normal[axis] > 0)
        {
            corner[axis] += CellSize;
        }

        int start = fillVertices.Count;
        fillVertices.Add(corner);
        fillVertices.Add(corner + v);
        fillVertices.Add(corner + u + v);
        fillVertices.Add(corner + u);
        var uv = new Vector2((int)block, 0);
        for (int i = 0; i < 4; i++)
        {
            fillNormals.Add(normal);
            fillUVs.Add(uv);
        }
        fillIndices.Add(start);
        fillIndices.Add(start + 1);
        fillIndices.Add(start + 2);
        fillIndices.Add(start);
        fillIndices.Add(start + 2);
        fillIndices.Add(start + 3);
    }

    /// <summary>Marks the fill around <paramref name="cell"/> out of date (its chunk, and the chunk across any face it shares with another).</summary>
    static void MarkFillDirty(Vector3I cell)
    {
        if (fillChunks.TryGetValue(FillChunkOf(cell), out FillChunk chunk))
        {
            chunk.dirty = true;
        }
        foreach (Vector3I direction in FaceDirections)
        {
            if (fillChunks.TryGetValue(FillChunkOf(cell + direction), out FillChunk neighbour))
            {
                neighbour.dirty = true;
            }
        }
    }

    /// <summary>Forgets the whole fill, since the next session's level is another. Called from <see cref="ResetSession"/>.</summary>
    static void ResetFill()
    {
        foreach (FillChunk chunk in fillChunks.Values)
        {
            chunk.instance?.QueueFree();
        }
        fillChunks.Clear();
        shownFillChunks.Clear();
        nearFillChunks.Clear();
    }

    static Vector3I FillChunkOf(Vector3I cell)
    {
        return new Vector3I(
            Mathf.FloorToInt(cell.X / (float)FillChunkCells),
            Mathf.FloorToInt(cell.Y / (float)FillChunkCells),
            Mathf.FloorToInt(cell.Z / (float)FillChunkCells));
    }

    static int FillIndex(Vector3I cell)
    {
        int x = Mathf.PosMod(cell.X, FillChunkCells), y = Mathf.PosMod(cell.Y, FillChunkCells), z = Mathf.PosMod(cell.Z, FillChunkCells);
        return x + FillChunkCells * (y + FillChunkCells * z);
    }
}
