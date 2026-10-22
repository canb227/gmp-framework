using Godot;

/// <summary>
/// Turns its node about its own Y axis at <see cref="rpm"/> (negative turns the other way), from
/// <see cref="GameWorld.netTime"/>, so every peer's is at the same angle without sending anything (like
/// <see cref="TrommelDrum"/>). Put it on a kinematic GMPBody whose GMPShapes are the paddles: the body is driven to the
/// node each tick and shoves whatever it meets.
/// </summary>
public partial class Stirrer : Node3D
{
    [Export] public float rpm = 6f;

    Transform3D restTransform;

    public override void _Ready()
    {
        restTransform = Transform;
    }

    public override void _PhysicsProcess(double delta)
    {
        if (rpm == 0)
        {
            return;
        }
        double msPerTurn = 60000.0 / Mathf.Abs(rpm);
        double turn = (GameWorld.netTime % (ulong)msPerTurn) / msPerTurn;
        float angle = (float)(turn * Mathf.Tau) * Mathf.Sign(rpm);
        Transform = new Transform3D(restTransform.Basis * new Basis(Vector3.Up, angle), restTransform.Origin);
    }
}
