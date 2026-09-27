using Godot;

/// <summary>
/// Root script of a held item's in-hand scene (<see cref="ItemInfo.inHandScene"/>). The player instantiates it under the camera's item
/// holder on every peer when the tool is equipped, calls <see cref="Equip"/>, and frees it when the tool is
/// put away (override <c>_ExitTree</c> to clean up). Only the holder's own copy receives input: the player
/// offers each unhandled event to <see cref="HandleInput"/> and each hotbar scroll step to
/// <see cref="HandleScroll"/> before its own handling (grab, hotbar), while the inventory screen is closed.
/// Anything a tool does to the world must go through the usual networking (claims, RPCs, spawns).
/// </summary>
public partial class HeldItem : Node3D
{
    /// <summary>The item definition this tool was equipped from.</summary>
    public ItemInfo item { get; private set; }
    /// <summary>The player holding this tool (on every peer).</summary>
    public FactoryPlayer player { get; private set; }
    /// <summary>True on the holder's own peer, where input and local-only visuals apply.</summary>
    protected bool isLocal => player != null && player.isLocal;

    public virtual void Equip(ItemInfo item, FactoryPlayer player)
    {
        this.item = item;
        this.player = player;
    }

    /// <summary>Called for each unhandled input event on the holder's peer. Return true to consume it.</summary>
    public virtual bool HandleInput(InputEvent @event) => false;

    /// <summary>Called for each scroll step (+1 down, -1 up) on the holder's peer. Return true to consume it (no hotbar change).</summary>
    public virtual bool HandleScroll(int step) => false;
}
