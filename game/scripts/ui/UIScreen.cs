using Godot;

/// <summary>
/// A screen on <see cref="UIManager"/>'s screen stack: a menu or overlay that takes the mouse (main menu, options,
/// pause, shop). Open and close screens through UIManager, never by adding or freeing them directly, so it can keep
/// the mouse mode, gameplay input blocking and Escape routing right.
/// <para>
/// Escape goes to the top screen's <see cref="Cancel"/>, which closes it by default. The hooks below are called by
/// UIManager as the stack changes; all are optional.
/// </para>
/// </summary>
public partial class UIScreen : Control
{
    /// <summary>True if the player can't move or act while this screen is open (the default).</summary>
    public virtual bool BlocksGameplay => true;

    /// <summary>Called once, after the screen has been added to the tree and pushed.</summary>
    public virtual void OnOpened() { }

    /// <summary>Called when another screen is pushed on top of this one.</summary>
    public virtual void OnCovered() { }

    /// <summary>Called when the screen above this one closes and this is the top screen again.</summary>
    public virtual void OnRevealed() { }

    /// <summary>Called just before the screen is removed from the stack and freed.</summary>
    public virtual void OnClosed() { }

    /// <summary>Escape / back while this is the top screen. Closes it unless overridden.</summary>
    public virtual void Cancel() => UIManager.CloseScreen(this);
}
