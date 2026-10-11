using Godot;

/// <summary>
/// An extra arrow drawn over a structure's placement preview, from <see cref="start"/> to <see cref="end"/> in the
/// structure's own frame, for flows its ports can't show (a catapult's throw, say). Display only: nothing reads it.
/// </summary>
[Tool]
[GlobalClass]
public partial class StructureArrow : Resource
{
    [Export] public Vector3 start;
    [Export] public Vector3 end;
    /// <summary>Colours the arrow as an input or an output.</summary>
    [Export] public PortKind kind;
}
