using Godot;

/// <summary>A push button: an <see cref="Activator"/> that plays its press animation on each accepted press.</summary>
public partial class BasicButton : GMPOActivator
{
    [Export] public AnimationPlayer animator;

    public override void OnActivated()
    {
        animator?.Play("button_press");
    }
}
