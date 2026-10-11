using Godot;

/// <summary>
/// Moves its node back and forth between its rest pose and rest + <see cref="offset"/> / <see cref="rotationDegrees"/>:
/// press rams stamping, a grabber arm idly scanning, pumps. Purely cosmetic, like <see cref="Spinner"/>, so every
/// peer just runs its own (nothing is synced).
/// </summary>
public partial class Oscillator : Node3D
{
    public enum Motion
    {
        /// <summary>Smooth there-and-back.</summary>
        Sine,
        /// <summary>Quick strike, short hold, slow return, then a pause at rest (presses, hammers).</summary>
        Stamp,
    }

    /// <summary>Translation from the rest position at the far end of the stroke (parent space).</summary>
    [Export] public Vector3 offset;
    /// <summary>Rotation added to the rest rotation at the far end of the stroke (degrees).</summary>
    [Export] public Vector3 rotationDegrees;
    /// <summary>Seconds for one full cycle.</summary>
    [Export] public float period = 2f;
    /// <summary>Fraction of a cycle to start at (0-1), so neighbouring parts can be out of step.</summary>
    [Export] public float phase;
    [Export] public Motion motion = Motion.Sine;

    Vector3 restPosition, restRotation;
    double time;

    public override void _Ready()
    {
        restPosition = Position;
        restRotation = RotationDegrees;
        time = phase * period;
    }

    public override void _Process(double delta)
    {
        if (period <= 0) return;
        time += delta;
        float u = (float)(time / period % 1.0);
        float k = motion == Motion.Sine ? 0.5f - 0.5f * Mathf.Cos(u * Mathf.Tau) : Stamp(u);
        Position = restPosition + offset * k;
        RotationDegrees = restRotation + rotationDegrees * k;
    }

    static float Stamp(float u)
    {
        if (u < 0.12f) return Mathf.Ease(u / 0.12f, 2.5f);             // strike
        if (u < 0.3f) return 1f;                                        // hold
        if (u < 0.7f) return 1f - Mathf.SmoothStep(0f, 1f, (u - 0.3f) / 0.4f); // return
        return 0f;                                                      // rest
    }
}
