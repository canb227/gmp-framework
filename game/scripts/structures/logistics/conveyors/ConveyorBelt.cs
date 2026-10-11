using Godot;

/// <summary>
/// A moving belt that is part of a <see cref="Structure"/>: a conveyor piece, or the intake / output belt of a bigger
/// machine (a structure may hold several). It's described, not modelled: a centreline through the middle of the
/// carrying surface, in flow order, plus a width and speed. It has no collider of its own: the build grid sweeps
/// every registered belt into a ribbon of triangles and merges belts whose ends meet into one seamless mesh body per
/// line (BuildGrid.Belts.cs), so items cross from piece to piece without catching on the joins.
/// <para>
/// Some belts (the conveyor families) also expose their return run: the same path <see cref="returnDepth"/> under
/// the carrying surface (against <see cref="normal"/>), facing the other way and moving backward, for items riding
/// upside down. Most belts, such as a machine's intake, don't.
/// </para>
/// <para>
/// To join up with its neighbours, a belt should meet the cell face it crosses at the face's centre, square to it,
/// and with the same <see cref="width"/> as the belt across the face. Belts of a different width still join the same
/// body but leave a seam.
/// </para>
/// </summary>
public partial class ConveyorBelt : Node3D
{
    /// <summary>The middle of the carrying surface, from where items get on to where they leave, in this node's frame.</summary>
    [Export] public Vector3[] points = [];
    /// <summary>The side items ride on, in this node's frame; the surface tilts with the path (on slopes) around it.</summary>
    [Export] public Vector3 normal = Vector3.Up;
    [Export] public float width = 1.68f;
    /// <summary>Belt speed in m/s.</summary>
    [Export] public float speed = 2f;
    /// <summary>
    /// Speed on a 30° climb (toward <see cref="normal"/>); it blends in from <see cref="speed"/> with steepness, so
    /// inclines can push harder. Descents and level runs use <see cref="speed"/>. 0 means <see cref="speed"/>.
    /// </summary>
    [Export] public float climbSpeed;
    [Export] public float friction = 0.8f;
    [Export] public float restitution;
    /// <summary>How far under the carrying surface the exposed return run is; 0 (the default) for none.</summary>
    [Export] public float returnDepth;
    [Export] public float returnWidth = 1.624f;
    /// <summary>
    /// Whether the belt's start and end are ports (<see cref="StructurePorts"/>): where it meets a neighbour across a
    /// cell face. Off for an end that hands items on some other way, such as a loader's kicker or a launcher's lip.
    /// </summary>
    [Export] public bool startIsPort = true;
    [Export] public bool endIsPort = true;

    /// <summary>
    /// The belt's visible mesh, if it uses the scrolling belt shader (ConveyorBeltLoop.gdshader). Its belt line sets
    /// where along the line this belt sits, so the pattern runs on unbroken from belt to belt.
    /// </summary>
    [Export] public NodePath visual;
    /// <summary>
    /// The visible top run's length in belt texture metres (the model's whole number of rib pitches). 0 rounds the
    /// path length to the nearest <see cref="RibPitch"/>, which is what the models do.
    /// </summary>
    [Export] public float visualLength;

    /// <summary>Incline at which <see cref="climbSpeed"/> applies in full.</summary>
    public const float FullClimbDegrees = 30f;
    /// <summary>The belt texture's pattern repeat (rib_pitch in ConveyorBeltLoop.gdshader); every model's top run is a whole number of these.</summary>
    public const float RibPitch = 0.25f;

    /// <summary>The visible top run's length in belt texture metres (see <see cref="visualLength"/>).</summary>
    public float VisualLength()
    {
        if (visualLength > 0f)
        {
            return visualLength;
        }
        float length = 0f;
        for (int i = 1; i < points.Length; i++)
        {
            length += points[i].DistanceTo(points[i - 1]);
        }
        return Mathf.Max(1, Mathf.RoundToInt(length / RibPitch)) * RibPitch;
    }

    /// <summary>The belt speed along a segment heading <paramref name="direction"/> (unit, world space).</summary>
    public float SpeedAlong(Vector3 direction, Vector3 worldNormal)
    {
        if (climbSpeed <= 0f)
        {
            return speed;
        }
        float incline = Mathf.RadToDeg(Mathf.Asin(Mathf.Clamp(direction.Dot(worldNormal), -1f, 1f)));
        return speed + (climbSpeed - speed) * Mathf.Clamp(incline / FullClimbDegrees, 0f, 1f);
    }
}
