using Godot;
using System.Collections.Generic;

/// <summary>Where on the HUD a text notification appears. Several at one anchor stack away from the screen edge.</summary>
public enum HudAnchor { TopLeft, TopCenter, TopRight, CenterLeft, Center, CenterRight, BottomLeft, BottomCenter, BottomRight }

/// <summary>
/// UIManager: notifications. Two kinds:
/// <list type="bullet">
/// <item><see cref="ShowText"/>: a line of text (optionally on a backing box) at a <see cref="HudAnchor"/>, which
/// fades away after a while (<see cref="HudToast"/>).</item>
/// <item><see cref="ShowMarker"/>: an icon with optional text pinned over an object in the world, drawn over
/// everything so it shows through walls and kept on screen at the edge when the object isn't
/// (<see cref="WorldMarker"/>). It can also outline the object through walls (<see cref="XRayHighlight"/>).</item>
/// </list>
/// Both are laid out in the 2560x1440 design space (<see cref="HiResUI"/>) and never take mouse input.
/// </summary>
public partial class UIManager
{
    const string ThemePath = "res://gmp/ui/theme/menu_theme.tres";
    /// <summary>Gap between anchored notifications and the screen edge, in design units.</summary>
    const float EdgeMargin = 48f;
    /// <summary>Bottom-centre notifications start above the hotbar and held-item name.</summary>
    const float HotbarClearance = 300f;

    static Control toastRoot, markerRoot;
    static readonly Dictionary<HudAnchor, VBoxContainer> anchors = new();

    void ReadyNotifications()
    {
        markerRoot = AddDesignRoot(AddLayer("WorldMarkers", MarkerLayer));
        toastRoot = AddDesignRoot(AddLayer("Notifications", NotificationLayer));
        foreach (HudAnchor anchor in System.Enum.GetValues<HudAnchor>())
        {
            anchors[anchor] = AddAnchorStack(anchor);
        }
    }

    /// <summary>A full-screen, click-through root in the design space.</summary>
    static Control AddDesignRoot(CanvasLayer layer)
    {
        Control root = new() { Name = "Root", MouseFilter = Control.MouseFilterEnum.Ignore };
        root.Theme = GD.Load<Theme>(ThemePath);
        layer.AddChild(root);
        HiResUI.Fill(root);
        return root;
    }

    static VBoxContainer AddAnchorStack(HudAnchor anchor)
    {
        int column = (int)anchor % 3, row = (int)anchor / 3;
        VBoxContainer stack = new()
        {
            Name = anchor.ToString(),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            // New notifications are added last: at the top anchors they go below the older ones, at the bottom
            // ones they go at the bottom and push the older ones up.
            Alignment = row == 2 ? BoxContainer.AlignmentMode.End : row == 1 ? BoxContainer.AlignmentMode.Center : BoxContainer.AlignmentMode.Begin,
        };
        stack.AddThemeConstantOverride("separation", 12);
        toastRoot.AddChild(stack);
        // Pinned to its anchor point; grows away from the screen edge as notifications are added.
        stack.AnchorLeft = stack.AnchorRight = column * 0.5f;
        stack.AnchorTop = stack.AnchorBottom = row * 0.5f;
        stack.GrowHorizontal = column == 0 ? Control.GrowDirection.End : column == 1 ? Control.GrowDirection.Both : Control.GrowDirection.Begin;
        stack.GrowVertical = row == 0 ? Control.GrowDirection.End : row == 1 ? Control.GrowDirection.Both : Control.GrowDirection.Begin;
        float x = column == 0 ? EdgeMargin : column == 2 ? -EdgeMargin : 0;
        float y = row == 0 ? EdgeMargin : row == 2 ? -(anchor == HudAnchor.BottomCenter ? HotbarClearance : EdgeMargin) : 0;
        stack.OffsetLeft = stack.OffsetRight = x;
        stack.OffsetTop = stack.OffsetBottom = y;
        return stack;
    }

    /// <summary>
    /// Shows <paramref name="text"/> at <paramref name="anchor"/> for <paramref name="duration"/> seconds, then fades it
    /// out. A duration of 0 or less keeps it up until <see cref="HudToast.Dismiss"/>. <paramref name="backing"/> puts
    /// it on a dark box; without one the text gets an outline to stay readable.
    /// </summary>
    public static HudToast ShowText(string text, HudAnchor anchor = HudAnchor.TopCenter, float duration = 3f,
        bool backing = true, Color? color = null)
    {
        HudToast toast = new(text, duration, backing, color ?? Colors.White);
        int column = (int)anchor % 3;
        toast.SizeFlagsHorizontal = column == 0 ? Control.SizeFlags.ShrinkBegin : column == 1 ? Control.SizeFlags.ShrinkCenter : Control.SizeFlags.ShrinkEnd;
        anchors[anchor].AddChild(toast);
        return toast;
    }

    /// <summary>
    /// Pins <paramref name="icon"/> (and <paramref name="text"/>, if any) over <paramref name="target"/>, offset by
    /// <paramref name="offset"/> (default 0.75 m up). Visible through walls; clamped to the screen edge when the
    /// target is off screen. Removed after <paramref name="duration"/> seconds (0 or less: when dismissed or the
    /// target is freed). <paramref name="highlight"/> also outlines the target through walls in
    /// <paramref name="color"/>.
    /// </summary>
    public static WorldMarker ShowMarker(Node3D target, Texture2D icon, string text = null, float duration = 0f,
        bool highlight = false, Color? color = null, Vector3? offset = null)
    {
        WorldMarker marker = new(target, icon, text, duration, color ?? WorldMarker.DefaultColor, offset ?? new Vector3(0, 0.75f, 0));
        markerRoot.AddChild(marker);
        if (highlight) marker.SetHighlighted(true);
        return marker;
    }

    /// <summary>Outlines <paramref name="target"/>'s meshes through walls until <see cref="ClearHighlight"/>.</summary>
    public static void Highlight(Node3D target, Color? color = null) => XRayHighlight.Apply(target, color ?? WorldMarker.DefaultColor);

    public static void ClearHighlight(Node3D target) => XRayHighlight.Remove(target);

    /// <summary>Removes every notification and marker at once (e.g. when leaving a game).</summary>
    public static void ClearNotifications()
    {
        foreach (VBoxContainer stack in anchors.Values)
        {
            foreach (Node toast in stack.GetChildren()) toast.QueueFree();
        }
        foreach (Node marker in markerRoot.GetChildren())
        {
            ((WorldMarker)marker).Dismiss(immediate: true);
        }
    }
}
