using Godot;

/// <summary>
/// The gravity gun's mode wheel, on the holder's screen while Q is held: one wedge per <see cref="GravityGun.modes"/>,
/// clockwise from the top. Moving the mouse picks a wedge (the view holds still meanwhile); letting go of Q selects it.
/// Locked modes show greyed out and can't be picked.
/// </summary>
public partial class GravityGunRadial : Control
{
    public GravityGun gun;
    public bool isOpen { get; private set; }

    const float Radius = 150f;
    const float InnerRadius = 45f;
    /// <summary>The mouse must move this far (px) from the middle to pick a wedge.</summary>
    const float Deadzone = 30f;

    /// <summary>The mouse's movement since the wheel opened, capped at the wheel's edge.</summary>
    Vector2 pointer;
    int hovered = -1;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        MouseFilter = MouseFilterEnum.Ignore;
        Visible = false;
    }

    public void Open()
    {
        isOpen = true;
        pointer = Vector2.Zero;
        hovered = -1;
        Visible = true;
        QueueRedraw();
    }

    /// <summary>Hides the wheel; returns the wedge picked, or -1 for none.</summary>
    public int Close()
    {
        isOpen = false;
        Visible = false;
        return hovered;
    }

    public void Move(Vector2 screenRelative)
    {
        pointer += screenRelative;
        if (pointer.Length() > Radius)
        {
            pointer = pointer.Normalized() * Radius;
        }
        hovered = -1;
        if (pointer.Length() >= Deadzone)
        {
            int count = GravityGun.modes.Length;
            float wedge = Mathf.Tau / count;
            // Clockwise from straight up.
            float angle = Mathf.PosMod(Mathf.Atan2(pointer.X, -pointer.Y) + wedge / 2, Mathf.Tau);
            hovered = (int)(angle / wedge) % count;
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        // The middle of the screen, in canvas units (the window may be stretched).
        Vector2 center = GetViewportRect().Size / 2;
        int count = GravityGun.modes.Length;
        float wedge = Mathf.Tau / count;
        Font font = ThemeDB.FallbackFont;
        for (int i = 0; i < count; i++)
        {
            GravityGunMode mode = GravityGun.modes[i];
            bool unlocked = mode.IsUnlocked(gun.player);
            // Godot's angles start at +X and turn clockwise on screen; ours start at the top.
            float start = -Mathf.Pi / 2 + wedge * i - wedge / 2;
            Color fill = new Color(0.1f, 0.1f, 0.12f, 0.75f);
            if (i == hovered && unlocked)
            {
                fill = new Color(mode.color.R * 0.5f, mode.color.G * 0.5f, mode.color.B * 0.5f, 0.85f);
            }
            DrawWedge(center, start, start + wedge, fill);

            float middle = start + wedge / 2;
            Vector2 labelAt = center + new Vector2(Mathf.Cos(middle), Mathf.Sin(middle)) * (InnerRadius + Radius) / 2;
            Color text = Colors.White;
            if (i == gun.modeIndex)
            {
                text = mode.color.Clamp();
            }
            if (!unlocked)
            {
                text = new Color(0.45f, 0.45f, 0.45f);
            }
            Vector2 size = font.GetStringSize(mode.name, HorizontalAlignment.Left, -1, 20);
            DrawString(font, labelAt - new Vector2(size.X / 2, -size.Y / 4), mode.name, HorizontalAlignment.Left, -1, 20, text);
        }
        DrawCircle(center + pointer, 5f, Colors.White);
    }

    /// <summary>A ring segment between <see cref="InnerRadius"/> and <see cref="Radius"/>, from angle a to b, with a thin gap at each side.</summary>
    void DrawWedge(Vector2 center, float a, float b, Color color)
    {
        const int Steps = 24;
        float gap = 0.02f;
        Vector2[] points = new Vector2[(Steps + 1) * 2];
        for (int s = 0; s <= Steps; s++)
        {
            float angle = Mathf.Lerp(a + gap, b - gap, (float)s / Steps);
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            points[s] = center + direction * Radius;
            points[points.Length - 1 - s] = center + direction * InnerRadius;
        }
        DrawColoredPolygon(points, color);
    }
}
