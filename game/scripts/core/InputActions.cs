using Godot;

/// <summary>
/// The project's input actions as cached <see cref="StringName"/>s. Passing a string where Godot wants a StringName
/// creates a new one on every call, and each is a finalizable object the garbage collector has to process; input
/// handlers run for every input event (mouse motion arrives about once a frame), so they use these instead.
/// </summary>
public static class InputActions
{
    public static readonly StringName forward = "forward";
    public static readonly StringName left = "left";
    public static readonly StringName backward = "backward";
    public static readonly StringName right = "right";
    public static readonly StringName jump = "jump";
    public static readonly StringName sprint = "sprint";
    public static readonly StringName interact = "interact";
    public static readonly StringName primary = "primary";
    public static readonly StringName drop = "drop";
    public static readonly StringName inventory = "inventory";
    public static readonly StringName scrollDown = "scrollDown";
    public static readonly StringName scrollUp = "scrollUp";
    public static readonly StringName rotate = "rotate";
    public static readonly StringName alternate = "alternate";
    /// <summary>Held while placing to turn off smart placement (belt end snapping, turning to follow the last output).</summary>
    public static readonly StringName freePlace = "freePlace";

    /// <summary>The hotbar slot actions slot0 to slot9, by slot index.</summary>
    public static readonly StringName[] slots =
        ["slot0", "slot1", "slot2", "slot3", "slot4", "slot5", "slot6", "slot7", "slot8", "slot9"];
}
