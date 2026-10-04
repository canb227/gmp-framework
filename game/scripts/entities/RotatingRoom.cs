using Godot;

/// <summary>
/// Puzzle-room concept: turns a whole room about an axis in steps (turn, hold, turn...), carrying everything
/// built inside it, so floors become walls and ceilings and only magnetic belts keep their loads.
/// <para>
/// Scene: the root is a <b>kinematic</b> <c>Box3DBody</c> (body_type 1) whose child Box3DCollisionShapes are the
/// room's walls and the colliders of the structures inside; meshes inside are plain children, so they turn with
/// it. The room is driven by angular velocity (not by setting its transform) so Box3D carries players and items
/// touching it. The velocity follows a smoothstep profile, plus a correction toward the target angle so it
/// never drifts. Set <see cref="driveTransform"/> to rotate the node directly instead (no contact carrying).
/// </para>
/// <para>Cosmetic proof of concept: every peer runs its own clock (nothing is synced).</para>
/// </summary>
public partial class RotatingRoom : Node3D
{
    /// <summary>Rotation axis in the room's local space.</summary>
    [Export] public Vector3 axis = Vector3.Right;
    /// <summary>Degrees per turn (negative turns the other way).</summary>
    [Export] public float stepDegrees = 90f;
    /// <summary>Seconds each turn takes, and the pause between turns.</summary>
    [Export] public float turnSeconds = 4f;
    [Export] public float holdSeconds = 5f;
    [Export] public bool running = true;
    /// <summary>Spin steadily at <see cref="degreesPerSecond"/> instead of stepping (turntables).</summary>
    [Export] public bool continuous;
    [Export] public float degreesPerSecond = 30f;
    /// <summary>Rotate the node's transform directly rather than through the body's angular velocity.</summary>
    [Export] public bool driveTransform;

    /// <summary>How hard (1/s) the velocity pulls the room back onto its target angle.</summary>
    const float Correction = 4f;

    Basis restBasis;
    Quaternion restRotation;
    Vector3 localAxis;
    double time;

    public override void _Ready()
    {
        restBasis = Basis;
        restRotation = GlobalBasis.GetRotationQuaternion();
        localAxis = axis.Normalized();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (running) time += delta;
        if (continuous)
        {
            float spinTarget = (float)(time * Mathf.DegToRad(degreesPerSecond) % Mathf.Tau);
            Drive(spinTarget, running ? Mathf.DegToRad(degreesPerSecond) : 0f);
            return;
        }
        double cycle = turnSeconds + holdSeconds;
        long n = (long)(time / cycle);
        float u = (float)(time - n * cycle);
        float step = Mathf.DegToRad(stepDegrees);
        bool turning = u < turnSeconds;
        float t = turning ? u / turnSeconds : 1f;
        float target = (n + t * t * (3f - 2f * t)) * step;               // smoothstep between stops

        Drive(target, turning && running ? step * 6f * t * (1f - t) / turnSeconds : 0f);   // rate: d(smoothstep)/dt
    }

    /// <summary>Moves the room toward <paramref name="target"/> (radians) turning at <paramref name="rate"/> (rad/s).</summary>
    void Drive(float target, float rate)
    {
        if (driveTransform)
        {
            Basis = restBasis * new Basis(localAxis, target);
            return;
        }
        float error = Mathf.Wrap(target - CurrentAngle(), -Mathf.Pi, Mathf.Pi);
        Vector3 worldAxis = (GlobalBasis * localAxis).Normalized();
        Call(Box3DNames.setAngularVelocity, worldAxis * (rate + error * Correction));
    }

    /// <summary>The room's current turn about its axis, relative to where it started (radians, -pi..pi).</summary>
    float CurrentAngle()
    {
        Quaternion rel = restRotation.Inverse() * GlobalBasis.GetRotationQuaternion();
        return 2f * Mathf.Atan2(new Vector3(rel.X, rel.Y, rel.Z).Dot(localAxis), rel.W);
    }
}
