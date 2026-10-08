using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

/// <summary>
/// The full-screen options screen: GRAPHICS (display and rendering), INPUT (mouse and key bindings) and GAMEPLAY
/// tabs, built from <see cref="GameSettings"/>. Every change applies and saves immediately.
/// <para>
/// A <see cref="UIScreen"/> opened over the main menu or the pause menu (<see cref="UIManager.OpenOptions"/>); Back
/// closes it, returning to whichever it came from. Escape does the same as Back, except while a key binding is
/// being captured, when it cancels the capture.
/// </para>
/// </summary>
public partial class OptionsMenu : UIScreen
{
    VBoxContainer graphicsRows, inputRows, gameplayRows;
    OptionButton resolutionOption;

    /// <summary>The binding being captured (the next key or mouse button pressed goes to it), if any.</summary>
    (string action, int slot, Button button)? capturing;
    readonly Dictionary<(string action, int slot), Button> bindingButtons = new();

    static readonly Vector2I[] CommonResolutions =
    {
        new(1280, 720), new(1366, 768), new(1600, 900), new(1920, 1080), new(2560, 1080),
        new(2560, 1440), new(3440, 1440), new(3840, 2160),
    };
    static readonly int[] FpsCaps = { 0, 30, 60, 120, 144, 165, 240 };

    public override void _Ready()
    {
        graphicsRows = GetNode<VBoxContainer>("%GraphicsRows");
        inputRows = GetNode<VBoxContainer>("%InputRows");
        gameplayRows = GetNode<VBoxContainer>("%GameplayRows");
        GetNode<Button>("%BackButton").Pressed += Back;

        BuildGraphics();
        BuildInput();
        BuildGameplay();
        // After the rows exist: Fill also scales the dropdown popups it finds.
        HiResUI.Fill(this);
    }

    void Back()
    {
        CancelCapture();
        UIManager.CloseScreen(this);
    }

    public override void Cancel() => Back();

    // ---- GRAPHICS ----

    void BuildGraphics()
    {
        Section(graphicsRows, "DISPLAY");
        OptionButton windowMode = Choice(graphicsRows, "WINDOW MODE", ["Windowed", "Borderless", "Fullscreen"],
            (int)GameSettings.windowMode, i => { GameSettings.SetWindowMode((GameSettings.WindowModeSetting)i); RefreshResolution(); });

        List<Vector2I> resolutions = ResolutionChoices();
        resolutionOption = Choice(graphicsRows, "RESOLUTION", resolutions.Select(r => $"{r.X} x {r.Y}").ToArray(),
            resolutions.IndexOf(GameSettings.resolution), i => GameSettings.SetResolution(resolutions[i]));
        RefreshResolution();

        Toggle(graphicsRows, "VSYNC", GameSettings.vsync, GameSettings.SetVsync);
        Choice(graphicsRows, "FRAME RATE LIMIT", FpsCaps.Select(f => f == 0 ? "Unlimited" : $"{f} FPS").ToArray(),
            Math.Max(0, Array.IndexOf(FpsCaps, GameSettings.maxFps)), i => GameSettings.SetMaxFps(FpsCaps[i]));

        Section(graphicsRows, "RENDERING");
        Slider(graphicsRows, "RENDER SCALE", 50, 100, 5, GameSettings.renderScale * 100f, v => $"{v:0}%",
            v => GameSettings.SetRenderScale((float)(v / 100)));
        Choice(graphicsRows, "UPSCALING", ["Bilinear", "FSR 1", "FSR 2"], (int)GameSettings.upscaler,
            i => GameSettings.SetUpscaler((GameSettings.UpscalerSetting)i));
        Choice(graphicsRows, "ANTI-ALIASING", ["Off", "FXAA", "SMAA", "MSAA 2x", "MSAA 4x", "MSAA 8x", "TAA"],
            (int)GameSettings.antiAliasing, i => GameSettings.SetAntiAliasing((GameSettings.AntiAliasingSetting)i));
        Choice(graphicsRows, "SHADOW QUALITY", ["Low", "Medium", "High", "Ultra"], (int)GameSettings.shadowQuality,
            i => GameSettings.SetShadowQuality((GameSettings.ShadowQualitySetting)i));

        ResetButton(graphicsRows, "RESET GRAPHICS", () =>
        {
            GameSettings.ResetGraphics();
            Rebuild(graphicsRows, BuildGraphics);
        });
    }

    /// <summary>Common window sizes that fit the screen, plus the screen's own size and the current setting.</summary>
    static List<Vector2I> ResolutionChoices()
    {
        Vector2I screen = DisplayServer.ScreenGetSize();
        var choices = CommonResolutions.Where(r => r.X <= screen.X && r.Y <= screen.Y).ToList();
        foreach (Vector2I extra in new[] { screen, GameSettings.resolution })
        {
            if (extra.X > 0 && !choices.Contains(extra)) choices.Add(extra);
        }
        return choices.OrderBy(r => r.X * r.Y).ThenBy(r => r.X).ToList();
    }

    /// <summary>Resolution only applies to Windowed mode; the other modes use the monitor's.</summary>
    void RefreshResolution()
    {
        resolutionOption.Disabled = GameSettings.windowMode != GameSettings.WindowModeSetting.Windowed;
    }

    // ---- INPUT ----

    void BuildInput()
    {
        Section(inputRows, "MOUSE");
        Slider(inputRows, "LOOK SENSITIVITY", 0.1, 3, 0.05, GameSettings.mouseSensitivity, v => $"{v:0.00}x",
            v => GameSettings.SetMouseSensitivity((float)v));
        Toggle(inputRows, "INVERT LOOK Y", GameSettings.invertMouseY, GameSettings.SetInvertMouseY);

        Section(inputRows, "KEY BINDINGS");
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 16);
        header.AddChild(new Label { Text = "ACTION", ThemeTypeVariation = "HeaderLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        header.AddChild(new Label { Text = "PRIMARY", ThemeTypeVariation = "HeaderLabel", CustomMinimumSize = new Vector2(340, 0) });
        header.AddChild(new Label { Text = "SECONDARY", ThemeTypeVariation = "HeaderLabel", CustomMinimumSize = new Vector2(340, 0) });
        inputRows.AddChild(Indented(header));

        bindingButtons.Clear();
        foreach (string action in GameSettings.rebindableActions)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 16);
            row.AddChild(new Label { Text = ActionLabel(action), ThemeTypeVariation = "FieldLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center });
            for (int slot = 0; slot < 2; slot++)
            {
                int s = slot;
                var button = new Button { CustomMinimumSize = new Vector2(340, 72), ClipText = true };
                button.Pressed += () => BeginCapture(action, s, button);
                bindingButtons[(action, slot)] = button;
                row.AddChild(button);
            }
            inputRows.AddChild(FieldRow(row));
        }
        RefreshBindings();

        var hint = new Label { Text = "Click a binding, then press a key or mouse button.  Esc cancels, Delete clears.", ThemeTypeVariation = "DimLabel" };
        inputRows.AddChild(Indented(hint));
        ResetButton(inputRows, "RESET BINDINGS", () =>
        {
            CancelCapture();
            GameSettings.ResetBindings();
            RefreshBindings();
        });
    }

    /// <summary>"scrollUp" / "move_left" -> "SCROLL UP" / "MOVE LEFT".</summary>
    static string ActionLabel(string action) => Regex.Replace(action.Replace('_', ' '), "(?<=[a-z0-9])([A-Z])", " $1").ToUpperInvariant();

    void RefreshBindings()
    {
        foreach (((string action, int slot), Button button) in bindingButtons)
        {
            InputEvent[] events = GameSettings.GetBindings(action);
            button.Text = slot < events.Length ? GameSettings.Describe(events[slot]) : "—";
        }
    }

    void BeginCapture(string action, int slot, Button button)
    {
        CancelCapture();
        capturing = (action, slot, button);
        button.Text = "PRESS A KEY…";
    }

    void CancelCapture()
    {
        if (capturing == null) return;
        capturing = null;
        RefreshBindings();
    }

    public override void _Input(InputEvent @event)
    {
        if (capturing is not { } current) return;
        (string action, int slot, _) = current;
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            GetViewport().SetInputAsHandled();
            if (key.Keycode == Key.Escape)
            {
                CancelCapture();
                return;
            }
            capturing = null;
            bool clear = key.Keycode == Key.Delete || key.Keycode == Key.Backspace;
            GameSettings.SetBinding(action, slot, clear ? null : new InputEventKey { PhysicalKeycode = key.PhysicalKeycode });
            RefreshBindings();
        }
        else if (@event is InputEventMouseButton mouse && mouse.Pressed)
        {
            GetViewport().SetInputAsHandled();
            capturing = null;
            GameSettings.SetBinding(action, slot, new InputEventMouseButton { ButtonIndex = mouse.ButtonIndex });
            RefreshBindings();
        }
    }

    // ---- GAMEPLAY ----

    void BuildGameplay()
    {
        Section(gameplayRows, "GAMEPLAY");
        gameplayRows.AddChild(Indented(new Label { Text = "No gameplay settings yet.", ThemeTypeVariation = "DimLabel" }));
    }

    // ---- row builders ----

    static void Section(VBoxContainer rows, string title)
    {
        var label = new Label { Text = title, ThemeTypeVariation = "SectionLabel" };
        if (rows.GetChildCount() > 0)
        {
            // Space above every section but the first.
            rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });
        }
        rows.AddChild(label);
    }

    /// <summary>A labelled well (FieldRow) holding <paramref name="control"/> on the right.</summary>
    static void Row(VBoxContainer rows, string label, Control control)
    {
        // Same height for every kind of row (dropdowns are the tallest control).
        var box = new HBoxContainer { CustomMinimumSize = new Vector2(0, 60) };
        box.AddThemeConstantOverride("separation", 24);
        box.AddChild(new Label { Text = label, ThemeTypeVariation = "FieldLabel", SizeFlagsHorizontal = SizeFlags.ExpandFill, VerticalAlignment = VerticalAlignment.Center });
        box.AddChild(control);
        rows.AddChild(FieldRow(box));
    }

    static PanelContainer FieldRow(Control content)
    {
        var row = new PanelContainer { ThemeTypeVariation = "FieldRow" };
        row.AddChild(content);
        return row;
    }

    /// <summary>Lines up loose content with the text inside the FieldRow wells.</summary>
    static MarginContainer Indented(Control content)
    {
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 16);
        margin.AddThemeConstantOverride("margin_right", 16);
        margin.AddChild(content);
        return margin;
    }

    static OptionButton Choice(VBoxContainer rows, string label, string[] items, int selected, Action<int> changed)
    {
        var option = new OptionButton { CustomMinimumSize = new Vector2(520, 0) };
        foreach (string item in items) option.AddItem(item);
        if (items.Length > 0) option.Selected = Math.Clamp(selected, 0, items.Length - 1);
        option.ItemSelected += i => changed((int)i);
        Row(rows, label, option);
        return option;
    }

    static void Toggle(VBoxContainer rows, string label, bool value, Action<bool> changed)
    {
        var toggle = new CheckButton { ButtonPressed = value };
        toggle.Toggled += on => changed(on);
        Row(rows, label, toggle);
    }

    static void Slider(VBoxContainer rows, string label, double min, double max, double step, double value,
        Func<double, string> format, Action<double> changed)
    {
        var box = new HBoxContainer();
        box.AddThemeConstantOverride("separation", 20);
        var slider = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step, Value = value,
            CustomMinimumSize = new Vector2(400, 0), SizeFlagsVertical = SizeFlags.ShrinkCenter,
        };
        var readout = new Label { Text = format(value), CustomMinimumSize = new Vector2(100, 0), HorizontalAlignment = HorizontalAlignment.Right };
        slider.ValueChanged += v => readout.Text = format(v);
        // Apply when the drag ends (or on a click / key step), not on every intermediate value.
        slider.DragEnded += valueChanged => { if (valueChanged) changed(slider.Value); };
        slider.ValueChanged += v => { if (!Input.IsMouseButtonPressed(MouseButton.Left)) changed(v); };
        box.AddChild(slider);
        box.AddChild(readout);
        Row(rows, label, box);
    }

    static void ResetButton(VBoxContainer rows, string text, Action pressed)
    {
        rows.AddChild(new Control { CustomMinimumSize = new Vector2(0, 10) });
        var footer = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(320, 72) };
        button.Pressed += pressed;
        footer.AddChild(button);
        rows.AddChild(footer);
    }

    /// <summary>Rebuilds one tab's rows from the current settings (after a reset).</summary>
    void Rebuild(VBoxContainer rows, Action build)
    {
        foreach (Node child in rows.GetChildren())
        {
            rows.RemoveChild(child);
            child.QueueFree();
        }
        build();
        // New dropdowns' popups are separate windows: scale them like the rest of the screen (as HiResUI.Fill does).
        foreach (Node node in rows.FindChildren("*", "OptionButton", true, false))
        {
            ((OptionButton)node).GetPopup().ContentScaleFactor = Scale.X;
        }
    }
}
