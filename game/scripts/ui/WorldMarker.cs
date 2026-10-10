using Godot;

/// <summary>
/// An icon (and optional text) pinned over an object in the world (<see cref="UIManager.ShowMarker"/>). It's a 2D
/// control on its own canvas layer, so it shows through walls. When the target is off screen or behind the camera
/// it sticks to the screen edge in the target's direction, with an arrow beside the icon pointing at it. Freed
/// after its duration, on <see cref="Dismiss"/>, or when the target leaves the tree.
/// <para>
/// It's repositioned every frame, so that work is kept small: everything that only changes with the text or the
/// tree (the parent root, the size, the target's lifetime) is tracked by signals rather than queried per frame.
/// </para>
/// </summary>
public partial class WorldMarker : VBoxContainer
{
    public static readonly Color DefaultColor = new(1f, 0.55f, 0.05f); // the UI's orange accent
    const float IconSize = 64f;
    const int FontSize = 30;
    /// <summary>How far from the screen edge an off-screen marker stays, in design units.</summary>
    const float EdgeInset = 56f;
    /// <summary>Off-screen markers are drawn smaller and dimmer.</summary>
    const float OffscreenScale = 0.75f, OffscreenAlpha = 0.7f;
    const float FadeTime = 0.25f;
    /// <summary>Off-screen arrow: length from base to tip, and gap between the icon's edge and the arrow, in design units.</summary>
    const float ArrowLength = 22f, ArrowGap = 10f;

    /// <summary>The object pointed at.</summary>
    public Node3D target { get; private set; }
    /// <summary>Where on the target to point, from its origin in world space.</summary>
    public Vector3 offset;

    readonly Label label;
    readonly Color color;
    /// <summary>Shown while off screen; points along +X before rotation.</summary>
    readonly Node2D arrow;
    readonly bool hasIcon;
    /// <summary>The design-space root this marker is laid out in (its parent).</summary>
    Control root;
    float remaining;
    /// <summary>Fade in/out, 0..1; multiplied with the off-screen dimming into Modulate.</summary>
    float fade;
    bool dismissing, highlighted;

    public string Text
    {
        get => label.Text;
        set
        {
            label.Text = value ?? "";
            label.Visible = !string.IsNullOrEmpty(value);
        }
    }

    public WorldMarker() { } // for Godot; build through UIManager.ShowMarker

    public WorldMarker(Node3D target, Texture2D icon, string text, float duration, Color color, Vector3 offset)
    {
        this.target = target;
        this.offset = offset;
        this.color = color;
        remaining = duration;
        MouseFilter = MouseFilterEnum.Ignore;
        Alignment = AlignmentMode.Begin;
        AddThemeConstantOverride("separation", 4);
        hasIcon = icon != null;
        if (hasIcon)
        {
            AddChild(new TextureRect
            {
                Texture = icon,
                CustomMinimumSize = new Vector2(IconSize, IconSize),
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                SizeFlagsHorizontal = SizeFlags.ShrinkCenter,
                MouseFilter = MouseFilterEnum.Ignore,
            });
        }
        label = new Label { HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", color);
        label.AddThemeConstantOverride("outline_size", 10);
        label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        AddChild(label);
        Text = text;

        // A Node2D isn't laid out by the container, so it can sit wherever _Process puts it. The dark, larger
        // triangle behind keeps it readable on any background.
        arrow = new Node2D { Visible = false };
        arrow.AddChild(Triangle(ArrowLength + 6f, new Color(0, 0, 0, 0.85f), -3f));
        arrow.AddChild(Triangle(ArrowLength, color, 0f));
        AddChild(arrow);
    }

    /// <summary>A triangle pointing along +X, its base at <paramref name="baseX"/>.</summary>
    static Polygon2D Triangle(float length, Color fill, float baseX) => new()
    {
        Color = fill,
        Polygon = new[] { new Vector2(baseX, -length * 0.6f), new Vector2(baseX + length, 0), new Vector2(baseX, length * 0.6f) },
    };

    public override void _Ready()
    {
        root = GetParent<Control>();
        if (!IsInstanceValid(target) || !target.IsInsideTree())
        {
            Dismiss(immediate: true);
            return;
        }
        target.TreeExiting += OnTargetExiting;
        // Sized to its contents here and whenever they change (text), not every frame.
        MinimumSizeChanged += FitToContents;
        FitToContents();
        Visible = false; // until the first _Process has placed it
        CreateTween().TweenMethod(Callable.From<float>(v => fade = v), 0f, 1f, FadeTime);
    }

    public override void _ExitTree()
    {
        if (IsInstanceValid(target)) target.TreeExiting -= OnTargetExiting;
    }

    void FitToContents() => Size = GetCombinedMinimumSize();

    void OnTargetExiting() => Dismiss(immediate: true);

    /// <summary>Turns the through-walls outline on the target on or off.</summary>
    public void SetHighlighted(bool on)
    {
        if (on == highlighted || !IsInstanceValid(target)) return;
        highlighted = on;
        if (on) XRayHighlight.Apply(target, color);
        else XRayHighlight.Remove(target);
    }

    /// <summary>Fades the marker out now (or removes it at once when <paramref name="immediate"/>) and drops its highlight.</summary>
    public void Dismiss(bool immediate = false)
    {
        if (!IsInstanceValid(this) || IsQueuedForDeletion()) return;
        SetHighlighted(false);
        if (immediate || !IsInsideTree())
        {
            // Freed at the end of the frame; the target may already be gone, so don't position again before that.
            SetProcess(false);
            QueueFree();
            return;
        }
        if (dismissing) return;
        dismissing = true;
        Tween tween = CreateTween();
        tween.TweenMethod(Callable.From<float>(v => fade = v), fade, 0f, FadeTime);
        tween.TweenCallback(Callable.From(QueueFree));
    }

    public override void _Process(double delta)
    {
        if (remaining > 0 && !dismissing)
        {
            remaining -= (float)delta;
            if (remaining <= 0) Dismiss();
        }

        Camera3D camera = GetViewport().GetCamera3D();
        if (camera == null)
        {
            Visible = false;
            return;
        }
        Visible = true;

        Vector3 world = target.GlobalPosition + offset;
        bool behind = camera.IsPositionBehind(world);
        // Canvas position (unproject works in the viewport's visible rect, as Controls do), then the root's design space.
        Vector2 point = root.GetGlobalTransform().AffineInverse() * camera.UnprojectPosition(world);

        Vector2 center = root.Size / 2f;
        Vector2 half = center - new Vector2(EdgeInset, EdgeInset);
        Vector2 fromCenter = point - center;
        // Behind the camera the projection comes out mirrored through the centre.
        if (behind) fromCenter = fromCenter.LengthSquared() > 1f ? -fromCenter : new Vector2(0, half.Y);
        bool offscreen = behind || Mathf.Abs(fromCenter.X) > half.X || Mathf.Abs(fromCenter.Y) > half.Y;
        if (offscreen)
        {
            // Onto the inset screen rect along the line from the centre: pulled in when past it, pushed out to it
            // when behind the camera (where the mirrored point can be anywhere, even near the centre).
            float t = Mathf.Min(half.X / Mathf.Max(Mathf.Abs(fromCenter.X), 0.001f), half.Y / Mathf.Max(Mathf.Abs(fromCenter.Y), 0.001f));
            point = center + fromCenter * t;
        }

        float scale = offscreen ? OffscreenScale : 1f;
        Scale = new Vector2(scale, scale);
        Modulate = new Color(1, 1, 1, fade * (offscreen ? OffscreenAlpha : 1f));
        // The icon's centre (the top of the stack) sits on the point; text hangs below it.
        Vector2 size = Size;
        float anchorY = hasIcon ? IconSize / 2f : size.Y / 2f;
        Vector2 position = point - new Vector2(size.X / 2f, anchorY) * scale;
        // Keep the whole marker (text included) on screen.
        Position = position.Clamp(Vector2.Zero, (root.Size - size * scale).Max(Vector2.Zero));

        arrow.Visible = offscreen;
        if (offscreen)
        {
            // Beside the icon (or the text), on the side facing the target, in this control's unscaled space.
            Vector2 direction = fromCenter.LengthSquared() > 0.001f ? fromCenter.Normalized() : Vector2.Down;
            float radius = (hasIcon ? IconSize / 2f : size.Y / 2f) + ArrowGap;
            arrow.Position = new Vector2(size.X / 2f, anchorY) + direction * radius;
            arrow.Rotation = direction.Angle();
        }
    }
}
