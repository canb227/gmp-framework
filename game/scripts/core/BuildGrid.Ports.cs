using Godot;
using System.Collections.Generic;

/// <summary>
/// A structure's port in world space: its connection <see cref="point"/>, the way it faces (<see cref="normal"/>)
/// and the rectangle it covers (<see cref="min"/> to <see cref="max"/>, flat along the normal).
/// </summary>
public readonly record struct WorldPort(Structure owner, PortKind kind, Vector3 point, Vector3I normal, Vector3 min, Vector3 max);

/// <summary>
/// BuildGrid: the ports of built structures (<see cref="StructurePorts"/>), indexed by the face plane they sit on.
/// An output connects to an input when they face each other on the same plane and the output's rectangle fits
/// inside the input's: belt to belt, a belt into a wider intake, a machine's mouth onto a belt, machine to machine.
/// Placement snapping (<see cref="AlignPorts"/>) and the self-turning conveyors (BuildGrid.BeltShapes.cs) go by these
/// connections. Like the belt lines, every peer keeps its own from the structures it holds.
/// </summary>
public partial class BuildGrid
{
    private const float PortTolerance = 0.01f;

    // Ports on each face plane, keyed by (axis, coordinate in hundredths): both sides of a face share a key.
    private static readonly Dictionary<(int axis, int plane), List<WorldPort>> portsByPlane = new();
    private static readonly Dictionary<Structure, WorldPort[]> portsByStructure = new();

    /// <summary>
    /// Adds <paramref name="structure"/>'s ports (from <see cref="Structure.AfterInit"/>, before its belts). Every
    /// port any of its forms could have is listed, so a self-turning conveyor offers all three inputs; one then takes
    /// the form its feeders call for, and conveyors this one's outputs feed are reshaped.
    /// </summary>
    public static void RegisterPorts(Structure structure)
    {
        if (portsByStructure.ContainsKey(structure))
        {
            return;
        }
        Transform3D pose = structure.GlobalTransform;
        IReadOnlyList<PortShape> local = structure.allLocalPorts;
        var ports = new WorldPort[local.Count];
        for (int i = 0; i < local.Count; i++)
        {
            ports[i] = ToWorld(structure, local[i], pose);
            if (!portsByPlane.TryGetValue(PlaneKey(ports[i]), out List<WorldPort> list))
            {
                portsByPlane[PlaneKey(ports[i])] = list = new List<WorldPort>();
            }
            list.Add(ports[i]);
        }
        portsByStructure[structure] = ports;
        if (structure is ConveyorStructure conveyor)
        {
            SetConveyorShape(conveyor, ResolveConveyorShape(conveyor, pose));
        }
        foreach (WorldPort port in ports)
        {
            if (port.kind == PortKind.Output) ReshapeConveyorsFedBy(port);
        }
    }

    /// <summary>Takes <paramref name="structure"/>'s ports out (from <see cref="Structure._ExitTree"/>), and reshapes the conveyors its outputs fed.</summary>
    public static void UnregisterPorts(Structure structure)
    {
        if (!portsByStructure.Remove(structure, out WorldPort[] ports))
        {
            return;
        }
        foreach (WorldPort port in ports)
        {
            if (portsByPlane.TryGetValue(PlaneKey(port), out List<WorldPort> list))
            {
                list.Remove(port);
                if (list.Count == 0) portsByPlane.Remove(PlaneKey(port));
            }
        }
        foreach (WorldPort port in ports)
        {
            if (port.kind == PortKind.Output) ReshapeConveyorsFedBy(port);
        }
    }

    /// <summary>
    /// Nudges a structure's <paramref name="anchor"/> by up to one cell on each axis so its ports
    /// (<paramref name="localPorts"/>, every form's, see <see cref="Structure.allLocalPorts"/>) connect to the ports of
    /// structures already built: its outputs into their inputs, their outputs into its inputs. Structures are wider
    /// than a cell, so aiming lands one a cell off its neighbour about half the time; this pulls it into line. Of the
    /// free placements, the one making the most connections wins, then the most exact ones (same rectangle, as belt
    /// to belt), then the one nearest the aimed anchor. Returns <paramref name="anchor"/> unchanged if none connects.
    /// Runs every frame while placing, so it allocates nothing.
    /// </summary>
    public static Vector3I AlignPorts(Structure structure, IReadOnlyList<PortShape> localPorts, Vector3I anchor, int quarterTurns)
    {
        if (localPorts.Count == 0 || portsByPlane.Count == 0)
        {
            return anchor;
        }
        Vector3I best = anchor;
        int bestMet = 0, bestExact = 0, bestDistance = int.MaxValue;
        for (int x = -1; x <= 1; x++)
        {
            for (int y = -1; y <= 1; y++)
            {
                for (int z = -1; z <= 1; z++)
                {
                    Vector3I candidate = anchor + new Vector3I(x, y, z);
                    int distance = x * x + y * y + z * z;
                    Transform3D pose = structure.PlacementTransform(candidate, quarterTurns);
                    int met = 0, exact = 0;
                    for (int i = 0; i < localPorts.Count; i++)
                    {
                        WorldPort mine = ToWorld(null, localPorts[i], pose);
                        if (!portsByPlane.TryGetValue(PlaneKey(mine), out List<WorldPort> others)) continue;
                        foreach (WorldPort other in others)
                        {
                            bool connects = mine.kind == PortKind.Output ? Connects(mine, other) : Connects(other, mine);
                            if (!connects) continue;
                            met++;
                            if (SameRectangle(mine, other)) exact++;
                        }
                    }
                    bool better = met > bestMet
                        || (met == bestMet && exact > bestExact)
                        || (met == bestMet && exact == bestExact && distance < bestDistance);
                    if (met > 0 && better && CanPlace(candidate, structure.cellOffsets, quarterTurns))
                    {
                        best = candidate;
                        bestMet = met;
                        bestExact = exact;
                        bestDistance = distance;
                    }
                }
            }
        }
        return best;
    }

    /// <summary>A port of <paramref name="owner"/> (null for a preview) at <paramref name="pose"/>, in world space.</summary>
    public static WorldPort ToWorld(Structure owner, in PortShape port, Transform3D pose)
    {
        Vector3 centre = pose * port.centre;
        Vector3 extent = (pose.Basis * port.across).Abs() * port.halfSize.X + (pose.Basis * port.up).Abs() * port.halfSize.Y;
        return new WorldPort(owner, port.kind, pose * port.point, DominantAxis(pose.Basis * (Vector3)port.normal), centre - extent, centre + extent);
    }

    /// <summary>True if <paramref name="output"/> hands items to <paramref name="input"/>: facing each other on one face plane, the output's rectangle inside the input's.</summary>
    public static bool Connects(in WorldPort output, in WorldPort input)
    {
        if (output.kind != PortKind.Output || input.kind != PortKind.Input || output.normal != -input.normal)
        {
            return false;
        }
        for (int axis = 0; axis < 3; axis++)
        {
            if (output.min[axis] < input.min[axis] - PortTolerance || output.max[axis] > input.max[axis] + PortTolerance)
            {
                return false;
            }
        }
        return true;
    }

    private static bool SameRectangle(in WorldPort a, in WorldPort b) =>
        a.min.DistanceSquaredTo(b.min) < PortTolerance * PortTolerance && a.max.DistanceSquaredTo(b.max) < PortTolerance * PortTolerance;

    private static (int, int) PlaneKey(in WorldPort port)
    {
        int axis = port.normal.X != 0 ? 0 : port.normal.Y != 0 ? 1 : 2;
        return (axis, Mathf.RoundToInt(port.point[axis] * 100f));
    }

    private static void ResetPorts()
    {
        portsByPlane.Clear();
        portsByStructure.Clear();
    }
}
