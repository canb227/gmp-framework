using Godot;

/// <summary>
/// Turns its node steadily about a local axis: machine flywheels, shredder rollers, beacons. Purely cosmetic,
/// so every peer just runs its own (nothing is synced).
/// </summary>
public partial class Spinner : Node3D
{
    [Export] public Vector3 axis = Vector3.Right;
    /// <summary>Radians per second; negative turns the other way.</summary>
    [Export] public float speed = 1f;

    public override void _Process(double delta)
    {
        RotateObjectLocal(axis.Normalized(), speed * (float)delta);
    }
}
