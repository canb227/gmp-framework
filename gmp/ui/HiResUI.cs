using Godot;
using System;

/// <summary>
/// Screens are authored at 2560x1440 (sizes, fonts, margins). <see cref="Fill"/> makes a full-screen root
/// Control use that design space at any window size: it scales the root uniformly by window height / 1440 and
/// sizes it to cover the whole window, so anchored children (HUD corners, bottom bars) still reach the edges on
/// wider or narrower aspect ratios.
/// </summary>
public static class HiResUI
{
    public const float DesignHeight = 1440f;

    /// <summary>Scales <paramref name="root"/> to the design space now and on every window resize while it's in the tree.</summary>
    public static void Fill(Control root)
    {
        Viewport viewport = root.GetViewport();
        Action apply = () =>
        {
            Vector2 window = root.GetViewportRect().Size;
            float scale = window.Y / DesignHeight;
            root.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            root.Scale = new Vector2(scale, scale);
            root.Size = window / scale;
            root.Position = Vector2.Zero;
            // Dropdown popups are their own windows, outside the root's transform: scale them to match.
            foreach (Node node in root.FindChildren("*", "OptionButton", true, false))
            {
                ((OptionButton)node).GetPopup().ContentScaleFactor = scale;
            }
        };
        apply();
        viewport.SizeChanged += apply;
        root.TreeExiting += () => viewport.SizeChanged -= apply;
    }
}
