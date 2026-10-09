using Godot;

/// <summary>
/// A two-position <see cref="Activator"/> (a toggle): pulled is on, pushed is off.
/// <para>
/// Scene: the root is a static <c>Box3DBody</c> whose collider covers the lever (it's what the player aims at),
/// with <see cref="handle"/> a pivot node that tilts between <see cref="pushedAngle"/> and <see cref="pulledAngle"/>.
/// </para>
/// </summary>
public partial class Lever : GMPOActivator
{
    /// <summary>Pivot that tilts about its local X axis to show the state.</summary>
    [Export] public Node3D handle;
    /// <summary>Handle tilt (degrees about X) when pushed (off) and when pulled (on).</summary>
    [Export] public float pushedAngle = -35f;
    [Export] public float pulledAngle = 35f;

    Tween handleTween;

    public Lever()
    {
        toggle = true;
        hoverName = "Lever";
    }

    protected override void UpdateHoverText()
    {
        hoverText = active ? "Press F to push." : "Press F to pull.";
    }

    public override void _Ready()
    {
        base._Ready();
        if (handle != null) handle.RotationDegrees = new Vector3(pushedAngle, 0, 0);
    }

    public override void OnActivated()
    {
        if (handle == null) return;
        handleTween?.Kill();
        handleTween = CreateTween();
        handleTween.TweenProperty(handle, "rotation_degrees:x", active ? pulledAngle : pushedAngle, 0.25)
            .SetTrans(Tween.TransitionType.Back).SetEase(Tween.EaseType.Out);
    }
}
