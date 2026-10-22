using Godot;

/// <summary>
/// The quiet control reminders in the HUD's lower right corner (PlayerHUD.tscn): one row per hint, the action's name
/// then its key in a small keycap, right-aligned so the keycaps line up along the screen edge. Small and dim on
/// purpose so it never draws the eye. Each hint's row is made once and <see cref="UpdateHints"/> shows or hides it.
/// </summary>
public partial class ControlHints : VBoxContainer
{
    const int FontSize = 17;
    static readonly Color ActionColor = new(0.75f, 0.75f, 0.78f, 0.5f);
    static readonly Color KeyColor = new(0.92f, 0.92f, 0.94f, 0.75f);

    FactoryPlayer player;
    StyleBoxFlat keycapStyle;

    Control freeBuildHint;
    Control deconstructHint;

    public override void _Ready()
    {
        player = GetParent<InventoryUI>().player;

        // A faint outlined keycap: barely filled, a hairline border, softly rounded.
        keycapStyle = new StyleBoxFlat();
        keycapStyle.BgColor = new Color(1f, 1f, 1f, 0.05f);
        keycapStyle.BorderColor = new Color(1f, 1f, 1f, 0.22f);
        keycapStyle.SetBorderWidthAll(1);
        keycapStyle.SetCornerRadiusAll(4);
        keycapStyle.ContentMarginLeft = 8;
        keycapStyle.ContentMarginRight = 8;
        keycapStyle.ContentMarginTop = 1;
        keycapStyle.ContentMarginBottom = 2;

        freeBuildHint = AddHint(InputActions.freePlace, "Free build");
        deconstructHint = AddHint(InputActions.deconstruct, "Deconstruct");
    }

    public override void _Process(double delta)
    {
        UpdateHints();
    }

    /// <summary>Picks which hints show right now. Rows that are hidden take no space, so the rest close up.</summary>
    void UpdateHints()
    {
        // TODO(human)
        freeBuildHint.Visible = true;
        deconstructHint.Visible = true;
    }

    /// <summary>
    /// Adds a row for <paramref name="action"/>: its label and the key it's bound to, read from the input map so the
    /// hint follows a rebinding (and shows the key as the player's keyboard layout labels it).
    /// </summary>
    Control AddHint(StringName action, string label)
    {
        HBoxContainer row = new() { Alignment = BoxContainer.AlignmentMode.End, MouseFilter = MouseFilterEnum.Ignore };
        row.AddThemeConstantOverride("separation", 12);

        Label actionLabel = new() { Text = label, Uppercase = true, VerticalAlignment = VerticalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        actionLabel.AddThemeFontSizeOverride("font_size", FontSize);
        actionLabel.AddThemeColorOverride("font_color", ActionColor);
        actionLabel.AddThemeConstantOverride("outline_size", 4);
        actionLabel.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 0.35f));
        row.AddChild(actionLabel);

        PanelContainer keycap = new() { CustomMinimumSize = new Vector2(30, 0), MouseFilter = MouseFilterEnum.Ignore };
        keycap.AddThemeStyleboxOverride("panel", keycapStyle);
        Label keyLabel = new() { Text = KeyName(action), HorizontalAlignment = HorizontalAlignment.Center, MouseFilter = MouseFilterEnum.Ignore };
        keyLabel.AddThemeFontSizeOverride("font_size", FontSize);
        keyLabel.AddThemeColorOverride("font_color", KeyColor);
        keycap.AddChild(keyLabel);
        row.AddChild(keycap);

        AddChild(row);
        return row;
    }

    /// <summary>The name of the first key bound to <paramref name="action"/>, or "?" if it has none.</summary>
    static string KeyName(StringName action)
    {
        foreach (InputEvent inputEvent in InputMap.ActionGetEvents(action))
        {
            if (inputEvent is not InputEventKey key) continue;
            Key keycode = key.Keycode;
            if (keycode == Key.None)
                keycode = DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode);
            return OS.GetKeycodeString(keycode);
        }
        return "?";
    }
}
