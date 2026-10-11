using Godot;

/// <summary>Which face of a footprint cell a <see cref="StructurePort"/> sits on, in the structure's own (unrotated) frame.</summary>
public enum PortFace
{
    /// <summary>-Z, the way the structure faces.</summary>
    Front,
    /// <summary>+Z.</summary>
    Back,
    /// <summary>-X.</summary>
    Left,
    /// <summary>+X.</summary>
    Right,
    /// <summary>+Y.</summary>
    Top,
    /// <summary>-Y.</summary>
    Bottom,
}

/// <summary>Whether items go in or come out through a port.</summary>
public enum PortKind { Input, Output }

/// <summary>
/// A place where items enter or leave a structure: a rectangle of whole cells on one face of its footprint (see
/// <see cref="StructurePorts"/>). Ports are what snapping, belt shaping and the I/O arrows go by. Everything here is
/// a cell, a face or a count of cells, so a port can't be placed off the grid: its exact point is worked out from them
/// (for a side face, the middle of the rectangle's width at belt height in its lowest cell; for the top or bottom, the
/// rectangle's centre), which is where a conveyor's belt meets that face.
/// <para>
/// Belts declare their own ports (their start is an input, their end an output); openings that aren't belts, such as a
/// hopper, need one of these, and so does a belt end that the machine reads items from or puts them on (declared at
/// the same place, it stands in for the belt's own port and carries the <see cref="volume"/> or <see cref="spawn"/>).
/// </para>
/// </summary>
[Tool]
[GlobalClass]
public partial class StructurePort : Resource
{
    [Export] public PortKind kind;
    /// <summary>The rectangle's first footprint cell (a <see cref="Structure.cellOffsets"/> entry); it extends from here toward + along the face.</summary>
    [Export] public Vector3I cell;
    [Export] public PortFace face;
    /// <summary>
    /// The rectangle's size in cells. On a side face: across (along X for Front/Back, Z for Left/Right), then up.
    /// On the top or bottom: along X, then along Z.
    /// </summary>
    [Export] public Vector2I size = Vector2I.One;
    /// <summary>
    /// For an input: the sensor body (a child of the structure, path from its root) whose overlap is what the machine
    /// takes in (<see cref="Structure.ItemsAtInputs"/>). Any shape and size, anywhere on the structure (a whole hopper,
    /// or reaching out to grab items early): it has nothing to do with where the port is for snapping.
    /// </summary>
    [Export] public NodePath volume;
    /// <summary>
    /// For an output: where the machine puts what it makes (<see cref="Structure.OutputPosition"/>). A plain node
    /// (a Marker3D) is a point; a sensor body is a volume, and each item lands at a random point in one of its boxes.
    /// </summary>
    [Export] public NodePath spawn;
}
