using Godot;
using System.Collections.Generic;

/// <summary>
/// One input or output end of a structure: touching cells that share a face tag (<see cref="CellTags"/>), and the way
/// items travel through that face. Ends only drive placement, snapping and belt shapes; machines take in and hand out
/// items however they like.
/// </summary>
public class CellEnd
{
    public readonly List<Vector3I> cells = new();
    /// <summary>The way items travel through the end: out of an output, into an input.</summary>
    public Vector3I direction;

    /// <summary>This end on a structure built at <paramref name="anchor"/> with <paramref name="quarterTurns"/> (from its own frame).</summary>
    public CellEnd Placed(Vector3I anchor, int quarterTurns)
    {
        var placed = new CellEnd { direction = BuildGrid.RotateOffset(direction, quarterTurns) };
        foreach (Vector3I cell in cells)
        {
            placed.cells.Add(anchor + BuildGrid.RotateOffset(cell, quarterTurns));
        }
        return placed;
    }

    /// <summary>
    /// For an output end: how far <paramref name="input"/> must move to take this end's items, or null if it can't,
    /// because it travels another way or isn't the same size and shape as the cells this end delivers into.
    /// </summary>
    public Vector3I? OffsetToFeed(CellEnd input)
    {
        if (input.direction != direction || input.cells.Count != cells.Count)
        {
            return null;
        }
        // Both are the same shape if every input cell, moved by the offset between their lowest corners, is a target.
        Vector3I offset = Lowest(cells) + direction - Lowest(input.cells);
        foreach (Vector3I cell in input.cells)
        {
            if (!cells.Contains(cell + offset - direction))
            {
                return null;
            }
        }
        return offset;
    }

    static Vector3I Lowest(List<Vector3I> cells)
    {
        Vector3I min = cells[0];
        foreach (Vector3I c in cells)
        {
            min = new Vector3I(Mathf.Min(min.X, c.X), Mathf.Min(min.Y, c.Y), Mathf.Min(min.Z, c.Z));
        }
        return min;
    }
}

/// <summary>Structure: its input and output ends, read from the face tags on its <see cref="cells"/>.</summary>
public partial class Structure
{
    /// <summary>A placed structure's output ends, in world cells (set as it's placed). Null on previews and probes.</summary>
    public List<CellEnd> outputEnds { get; private set; }
    /// <summary>A placed structure's input ends, in world cells (set as it's placed). Null on previews and probes.</summary>
    public List<CellEnd> inputEnds { get; private set; }

    static readonly Dictionary<PackedScene, List<CellEnd>> sceneInputEnds = new();
    static readonly Dictionary<PackedScene, List<CellEnd>> sceneOutputEnds = new();

    public static CellTags InputTag(int face)
    {
        return (CellTags)(1 << face);
    }

    public static CellTags OutputTag(int face)
    {
        return (CellTags)(1 << (face + 6));
    }

    /// <summary>
    /// The input or output ends of <paramref name="cells"/> placed at <paramref name="anchor"/> with
    /// <paramref name="quarterTurns"/>: one per group of touching cells with the same face tag.
    /// </summary>
    public static List<CellEnd> FindEnds(Godot.Collections.Dictionary<Vector3I, CellTags> cells, bool outputs, Vector3I anchor, int quarterTurns)
    {
        var ends = new List<CellEnd>();
        for (int face = 0; face < 6; face++)
        {
            // Items come in through an input face, so they travel against its direction.
            CellTags tag = InputTag(face);
            Vector3I travel = -FaceDirections[face];
            if (outputs)
            {
                tag = OutputTag(face);
                travel = FaceDirections[face];
            }
            var tagged = new List<Vector3I>();
            foreach (KeyValuePair<Vector3I, CellTags> cell in cells)
            {
                if ((cell.Value & tag) != 0)
                {
                    tagged.Add(cell.Key);
                }
            }
            while (tagged.Count > 0)
            {
                // Grow a group from one tagged cell through its touching tagged neighbours (on the same side of SeparateEnd).
                var group = new List<Vector3I> { tagged[0] };
                tagged.RemoveAt(0);
                bool separate = (cells[group[0]] & CellTags.SeparateEnd) != 0;
                for (int i = 0; i < group.Count; i++)
                {
                    foreach (Vector3I step in FaceDirections)
                    {
                        Vector3I next = group[i] + step;
                        if (!tagged.Contains(next) || ((cells[next] & CellTags.SeparateEnd) != 0) != separate)
                        {
                            continue;
                        }
                        tagged.Remove(next);
                        group.Add(next);
                    }
                }
                var end = new CellEnd { direction = travel };
                end.cells.AddRange(group);
                ends.Add(end.Placed(anchor, quarterTurns));
            }
        }
        return ends;
    }

    /// <summary>The input ends of <paramref name="scene"/>'s structure, in its own frame. Read once per scene.</summary>
    public static List<CellEnd> SceneInputEnds(PackedScene scene)
    {
        return SceneEnds(scene, false, sceneInputEnds);
    }

    /// <summary>The output ends of <paramref name="scene"/>'s structure, in its own frame. Read once per scene.</summary>
    public static List<CellEnd> SceneOutputEnds(PackedScene scene)
    {
        return SceneEnds(scene, true, sceneOutputEnds);
    }

    static List<CellEnd> SceneEnds(PackedScene scene, bool outputs, Dictionary<PackedScene, List<CellEnd>> cache)
    {
        if (cache.TryGetValue(scene, out List<CellEnd> ends))
        {
            return ends;
        }
        ends = new List<CellEnd>();
        Node node = scene.Instantiate();
        if (node is Structure structure)
        {
            ends = FindEnds(structure.cells, outputs, Vector3I.Zero, 0);
        }
        node.Free();
        cache[scene] = ends;
        return ends;
    }

    /// <summary>The placed structure's output end that delivers exactly onto <paramref name="input"/> (world cells), or null.</summary>
    public static CellEnd FeederOf(CellEnd input)
    {
        if (BuildGrid.GetStructureAt(input.cells[0] - input.direction) is not Structure feeder || feeder.outputEnds == null)
        {
            return null;
        }
        foreach (CellEnd output in feeder.outputEnds)
        {
            if (output.OffsetToFeed(input) == Vector3I.Zero)
            {
                return output;
            }
        }
        return null;
    }

    /// <summary>
    /// For the placement preview: whether nothing is built where a placed structure's end connects, the cells an
    /// output (<paramref name="output"/>) delivers into or the cells an input takes items from.
    /// </summary>
    public static bool IsOpen(CellEnd end, bool output)
    {
        foreach (Vector3I cell in end.cells)
        {
            Vector3I across = cell - end.direction;
            if (output)
            {
                across = cell + end.direction;
            }
            if (BuildGrid.GetStructureAt(across) != null)
            {
                return false;
            }
        }
        return true;
    }

    // The belts this structure's outputs deliver into re-check their shape at the end of the frame.
    void CheckReceiversLater()
    {
        if (outputEnds == null)
        {
            return;
        }
        foreach (CellEnd end in outputEnds)
        {
            if (BuildGrid.GetStructureAt(end.cells[0] + end.direction) is Conveyor receiver)
            {
                Callable.From(receiver.CheckShape).CallDeferred();
            }
        }
    }
}
