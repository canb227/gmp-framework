using Godot;
using System;

/// <summary>
/// Screens are authored at 2560x1440 (sizes, fonts, margins). <see cref="Fill"/> makes a full-screen root
/// Control use that design space at any window size: it scales the root uniformly by window height / 1440 and
/// sizes it to cover the whole window, so anchored children (HUD corners, bottom bars) still reach the edges on
/// wider or narrower aspect ratios.
/// <para>
/// The scale never drops below <see cref="MinPixelScale"/>: shrunk further, text gets too small to read (a 22 px
/// label is 11 px in a 1280x720 window at 0.5). Below 1080p a screen therefore gets a smaller design space instead,
/// as little as <see cref="MinDesignHeight"/> tall in a 720p window, so layouts must fit in that (and stay
/// anchored/contained rather than placed at fixed 1440p positions).
/// </para>
/// </summary>
public static class HiResUI
{
    public const float DesignHeight = 1440f;

    /// <summary>Fewest window pixels per design unit; reached at a 1080 px tall window.</summary>
    public const float MinPixelScale = 0.75f;

    /// <summary>Design-space height a screen gets in a 720p window (720 / <see cref="MinPixelScale"/>).</summary>
    public const float MinDesignHeight = 720f / MinPixelScale;

    /// <summary>Scales <paramref name="root"/> to the design space now and on every window resize while it's in the tree.</summary>
    public static void Fill(Control root)
    {
        Viewport viewport = root.GetViewport();
        Action apply = () =>
        {
            // The root lives in canvas units (the project stretches canvas_items from its base size), so work out
            // canvas units per design unit from the window's real pixel height.
            Vector2 canvas = root.GetViewportRect().Size;
            float windowPixels = Mathf.Max(root.GetWindow().Size.Y, 1);
            float pixelScale = Mathf.Max(windowPixels / DesignHeight, MinPixelScale);
            float scale = pixelScale * canvas.Y / windowPixels;
            root.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
            root.Scale = new Vector2(scale, scale);
            root.Size = canvas / scale;
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
