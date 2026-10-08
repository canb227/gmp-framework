using Godot;

/// <summary>
/// A text notification on the HUD (<see cref="UIManager.ShowText"/>): fades in, stays for its duration, fades out
/// and frees itself. Change <see cref="Text"/> to update it in place; <see cref="Dismiss"/> to take it down early.
/// </summary>
public partial class HudToast : PanelContainer
{
    const float FadeInTime = 0.15f, FadeOutTime = 0.5f;
    const int FontSize = 34;

    readonly Label label;
    readonly float duration;
    Tween tween;
    bool dismissing;

    public string Text
    {
        get => label.Text;
        set => label.Text = value;
    }

    public HudToast() { } // for Godot; build through UIManager.ShowText

    public HudToast(string text, float duration, bool backing, Color color)
    {
        this.duration = duration;
        MouseFilter = MouseFilterEnum.Ignore;
        label = new Label { Text = text, MouseFilter = MouseFilterEnum.Ignore };
        label.AddThemeFontSizeOverride("font_size", FontSize);
        label.AddThemeColorOverride("font_color", color);
        if (backing)
        {
            // Terminal-style well, as the inventory slots (InventoryUI).
            StyleBoxFlat box = new()
            {
                BgColor = new Color(0.035f, 0.035f, 0.04f, 0.85f),
                BorderColor = new Color(0.24f, 0.24f, 0.26f, 0.9f),
            };
            box.SetBorderWidthAll(2);
            box.ContentMarginLeft = box.ContentMarginRight = 28;
            box.ContentMarginTop = box.ContentMarginBottom = 14;
            AddThemeStyleboxOverride("panel", box);
        }
        else
        {
            AddThemeStyleboxOverride("panel", new StyleBoxEmpty());
            label.AddThemeConstantOverride("outline_size", 10);
            label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.85f));
        }
        AddChild(label);
    }

    public override void _Ready()
    {
        Modulate = new Color(1, 1, 1, 0);
        tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 1f, FadeInTime);
        if (duration > 0)
        {
            tween.TweenInterval(duration);
            tween.TweenProperty(this, "modulate:a", 0f, FadeOutTime);
            tween.TweenCallback(Callable.From(QueueFree));
        }
    }

    /// <summary>Fades the notification out now (or removes it at once when <paramref name="immediate"/>).</summary>
    public void Dismiss(bool immediate = false)
    {
        if (!IsInstanceValid(this) || IsQueuedForDeletion()) return;
        if (immediate || !IsInsideTree())
        {
            QueueFree();
            return;
        }
        if (dismissing) return;
        dismissing = true;
        tween?.Kill();
        tween = CreateTween();
        tween.TweenProperty(this, "modulate:a", 0f, FadeOutTime * Modulate.A);
        tween.TweenCallback(Callable.From(QueueFree));
    }
}
