using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Player-facing settings (graphics and input), saved to <see cref="FilePath"/>. <see cref="Global"/> loads and applies
/// them at startup; the options screen changes them through the setters here, which apply and save straight away.
/// Defaults are whatever the project settings and input map give at startup, so "reset" returns to those.
/// <para>
/// Launches with command-line user args (the dev/test flags such as --lan or --host) keep their own window: the
/// saved window mode and resolution are only applied to plain launches, so test instances aren't all made fullscreen.
/// Headless runs (the automated tests) never save, so they can't overwrite a player's settings.
/// </para>
/// </summary>
public static class GameSettings
{
    public const string FilePath = "user://config/settings.cfg";

    public enum WindowModeSetting { Windowed, Borderless, Fullscreen }
    public enum UpscalerSetting { Bilinear, Fsr1, Fsr2 }
    public enum AntiAliasingSetting { Off, Fxaa, Smaa, Msaa2x, Msaa4x, Msaa8x, Taa }
    public enum ShadowQualitySetting { Low, Medium, High, Ultra }

    // ---- graphics ----
    public static WindowModeSetting windowMode { get; private set; }
    /// <summary>Window size in Windowed mode (the borderless and fullscreen modes use the monitor's).</summary>
    public static Vector2I resolution { get; private set; }
    public static bool vsync { get; private set; }
    /// <summary>Frame-rate cap; 0 is uncapped.</summary>
    public static int maxFps { get; private set; }
    /// <summary>3D render resolution relative to the window, 0.5 to 1.</summary>
    public static float renderScale { get; private set; }
    public static UpscalerSetting upscaler { get; private set; }
    public static AntiAliasingSetting antiAliasing { get; private set; }
    public static ShadowQualitySetting shadowQuality { get; private set; }

    // ---- input ----
    /// <summary>Multiplier on the base mouse-look speed.</summary>
    public static float mouseSensitivity { get; private set; } = 1f;
    public static bool invertMouseY { get; private set; }

    /// <summary>Actions offered for rebinding: the project's own, in input-map order (engine ui_* and addon actions left out).</summary>
    public static IReadOnlyList<string> rebindableActions => rebindable;

    static readonly List<string> rebindable = new();
    static readonly Dictionary<string, InputEvent[]> defaultBindings = new();
    static Defaults defaults;
    static Window root;
    static bool applyWindow;
    static bool loaded;
    static bool headless => DisplayServer.GetName() == "headless";

    record Defaults(WindowModeSetting windowMode, Vector2I resolution, bool vsync, int maxFps, float renderScale,
        UpscalerSetting upscaler, AntiAliasingSetting antiAliasing, ShadowQualitySetting shadowQuality);

    /// <summary>Records the defaults, loads the saved settings over them and applies everything. Called once, at startup.</summary>
    public static void LoadAndApply(Window rootWindow)
    {
        root = rootWindow;
        applyWindow = OS.GetCmdlineUserArgs().Length == 0;
        CaptureDefaults();
        ResetGraphicsValues();
        var file = new ConfigFile();
        if (file.Load(FilePath) == Error.Ok)
        {
            windowMode = Enum<WindowModeSetting>(file, "graphics", "window_mode", windowMode);
            resolution = (Vector2I)file.GetValue("graphics", "resolution", resolution);
            vsync = (bool)file.GetValue("graphics", "vsync", vsync);
            maxFps = (int)file.GetValue("graphics", "max_fps", maxFps);
            renderScale = (float)file.GetValue("graphics", "render_scale", renderScale);
            upscaler = Enum<UpscalerSetting>(file, "graphics", "upscaler", upscaler);
            antiAliasing = Enum<AntiAliasingSetting>(file, "graphics", "anti_aliasing", antiAliasing);
            shadowQuality = Enum<ShadowQualitySetting>(file, "graphics", "shadow_quality", shadowQuality);
            mouseSensitivity = (float)file.GetValue("input", "mouse_sensitivity", mouseSensitivity);
            invertMouseY = (bool)file.GetValue("input", "invert_mouse_y", invertMouseY);
            if (file.HasSection("bindings"))
            {
                foreach (string action in file.GetSectionKeys("bindings"))
                {
                    if (!rebindable.Contains(action)) continue;
                    InputEvent[] events = ((string[])file.GetValue("bindings", action)).Select(DecodeEvent).Where(e => e != null).ToArray();
                    SetActionEvents(action, events);
                }
            }
        }
        loaded = true;
        ApplyGraphics();
    }

    static T Enum<T>(ConfigFile file, string section, string key, T fallback) where T : struct, System.Enum
        => System.Enum.TryParse((string)file.GetValue(section, key, fallback.ToString()), out T value) ? value : fallback;

    static void CaptureDefaults()
    {
        Viewport viewport = root;
        WindowModeSetting mode = DisplayServer.WindowGetMode() switch
        {
            DisplayServer.WindowMode.Fullscreen => WindowModeSetting.Borderless,
            DisplayServer.WindowMode.ExclusiveFullscreen => WindowModeSetting.Fullscreen,
            _ => WindowModeSetting.Windowed,
        };
        UpscalerSetting up = viewport.Scaling3DMode switch
        {
            Viewport.Scaling3DModeEnum.Fsr => UpscalerSetting.Fsr1,
            Viewport.Scaling3DModeEnum.Fsr2 => UpscalerSetting.Fsr2,
            _ => UpscalerSetting.Bilinear,
        };
        AntiAliasingSetting aa = viewport.UseTaa ? AntiAliasingSetting.Taa
            : viewport.Msaa3D == Viewport.Msaa.Msaa8X ? AntiAliasingSetting.Msaa8x
            : viewport.Msaa3D == Viewport.Msaa.Msaa4X ? AntiAliasingSetting.Msaa4x
            : viewport.Msaa3D == Viewport.Msaa.Msaa2X ? AntiAliasingSetting.Msaa2x
            : viewport.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Fxaa ? AntiAliasingSetting.Fxaa
            : viewport.ScreenSpaceAA == Viewport.ScreenSpaceAAEnum.Smaa ? AntiAliasingSetting.Smaa
            : AntiAliasingSetting.Off;
        defaults = new Defaults(mode, DisplayServer.WindowGetSize(), DisplayServer.WindowGetVsyncMode() != DisplayServer.VSyncMode.Disabled,
            Engine.MaxFps, Mathf.Clamp(viewport.Scaling3DScale, 0.5f, 1f), up, aa, ShadowQualitySetting.Medium);

        rebindable.Clear();
        defaultBindings.Clear();
        foreach (StringName action in InputMap.GetActions())
        {
            string name = action;
            if (name.StartsWith("ui_") || name.StartsWith("limbo_") || !ProjectSettings.HasSetting("input/" + name)) continue;
            rebindable.Add(name);
            defaultBindings[name] = InputMap.ActionGetEvents(action).Select(e => (InputEvent)e.Duplicate()).ToArray();
        }
    }

    static void ResetGraphicsValues()
    {
        windowMode = defaults.windowMode;
        resolution = defaults.resolution;
        vsync = defaults.vsync;
        maxFps = defaults.maxFps;
        renderScale = defaults.renderScale;
        upscaler = defaults.upscaler;
        antiAliasing = defaults.antiAliasing;
        shadowQuality = defaults.shadowQuality;
    }

    // ---- setters (apply and save) ----

    public static void SetWindowMode(WindowModeSetting value) { windowMode = value; ApplyWindow(force: true); Save(); }
    public static void SetResolution(Vector2I value) { resolution = value; ApplyWindow(force: true); Save(); }
    public static void SetVsync(bool value) { vsync = value; ApplyGraphics(); Save(); }
    public static void SetMaxFps(int value) { maxFps = value; ApplyGraphics(); Save(); }
    public static void SetRenderScale(float value) { renderScale = Mathf.Clamp(value, 0.5f, 1f); ApplyGraphics(); Save(); }
    public static void SetUpscaler(UpscalerSetting value) { upscaler = value; ApplyGraphics(); Save(); }
    public static void SetAntiAliasing(AntiAliasingSetting value) { antiAliasing = value; ApplyGraphics(); Save(); }
    public static void SetShadowQuality(ShadowQualitySetting value) { shadowQuality = value; ApplyGraphics(); Save(); }
    public static void SetMouseSensitivity(float value) { mouseSensitivity = value; Save(); }
    public static void SetInvertMouseY(bool value) { invertMouseY = value; Save(); }

    /// <summary>Puts every graphics setting back to its default.</summary>
    public static void ResetGraphics()
    {
        ResetGraphicsValues();
        ApplyWindow(force: true);
        ApplyGraphics();
        Save();
    }

    // ---- bindings ----

    /// <summary>The events bound to <paramref name="action"/>, in order (the options screen shows the first two).</summary>
    public static InputEvent[] GetBindings(string action) => InputMap.ActionGetEvents(action).ToArray();

    /// <summary>Sets binding <paramref name="slot"/> (0 = primary, 1 = secondary) of <paramref name="action"/>; null clears it.</summary>
    public static void SetBinding(string action, int slot, InputEvent inputEvent)
    {
        List<InputEvent> events = GetBindings(action).ToList();
        while (events.Count <= slot) events.Add(null);
        events[slot] = inputEvent;
        SetActionEvents(action, events.Where(e => e != null).ToArray());
        Save();
    }

    /// <summary>Puts every action back to its project bindings.</summary>
    public static void ResetBindings()
    {
        foreach (string action in rebindable)
        {
            SetActionEvents(action, defaultBindings[action]);
        }
        Save();
    }

    static void SetActionEvents(string action, IEnumerable<InputEvent> events)
    {
        InputMap.ActionEraseEvents(action);
        foreach (InputEvent e in events)
        {
            InputMap.ActionAddEvent(action, e);
        }
    }

    /// <summary>Short display name for a binding, such as "W", "Shift" or "Mouse 1".</summary>
    public static string Describe(InputEvent inputEvent) => inputEvent switch
    {
        null => "",
        // Physical keys are shown as the current keyboard layout labels them (the headless server has no layout).
        InputEventKey key => OS.GetKeycodeString(key.PhysicalKeycode == Key.None ? key.Keycode
            : headless ? key.PhysicalKeycode : DisplayServer.KeyboardGetKeycodeFromPhysical(key.PhysicalKeycode)),
        InputEventMouseButton mouse => mouse.ButtonIndex switch
        {
            MouseButton.Left => "Mouse 1",
            MouseButton.Right => "Mouse 2",
            MouseButton.Middle => "Mouse 3",
            MouseButton.WheelUp => "Wheel Up",
            MouseButton.WheelDown => "Wheel Down",
            MouseButton.Xbutton1 => "Mouse 4",
            MouseButton.Xbutton2 => "Mouse 5",
            _ => $"Mouse {(int)mouse.ButtonIndex}",
        },
        _ => inputEvent.AsText(),
    };

    // Saved as "key:<physical keycode>" or "mouse:<button index>".
    static string EncodeEvent(InputEvent inputEvent) => inputEvent switch
    {
        InputEventKey key => $"key:{(long)(key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode)}",
        InputEventMouseButton mouse => $"mouse:{(long)mouse.ButtonIndex}",
        _ => null,
    };

    static InputEvent DecodeEvent(string text)
    {
        string[] parts = text.Split(':');
        if (parts.Length != 2 || !long.TryParse(parts[1], out long code)) return null;
        return parts[0] switch
        {
            "key" => new InputEventKey { PhysicalKeycode = (Key)code },
            "mouse" => new InputEventMouseButton { ButtonIndex = (MouseButton)code },
            _ => null,
        };
    }

    // ---- applying ----

    static void ApplyWindow(bool force)
    {
        if ((!force && !applyWindow) || headless) return;
        switch (windowMode)
        {
            case WindowModeSetting.Windowed:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                // A saved size that can't be right (zero, or bigger than this monitor) falls back to the default.
                Vector2I screenSize = DisplayServer.ScreenGetSize();
                Vector2I size = resolution.X > 0 && resolution.Y > 0 && resolution.X <= screenSize.X && resolution.Y <= screenSize.Y
                    ? resolution : defaults.resolution;
                DisplayServer.WindowSetSize(size);
                DisplayServer.WindowSetPosition(DisplayServer.ScreenGetPosition() + (screenSize - size) / 2);
                break;
            case WindowModeSetting.Borderless:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
            case WindowModeSetting.Fullscreen:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.ExclusiveFullscreen);
                break;
        }
    }

    static void ApplyGraphics()
    {
        if (root == null) return;
        if (!loaded) return;
        ApplyWindow(force: false);
        if (!headless)
        {
            DisplayServer.WindowSetVsyncMode(vsync ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        }
        Engine.MaxFps = maxFps;

        Viewport viewport = root;
        viewport.Scaling3DScale = renderScale;
        viewport.Scaling3DMode = upscaler switch
        {
            UpscalerSetting.Fsr1 => Viewport.Scaling3DModeEnum.Fsr,
            UpscalerSetting.Fsr2 => Viewport.Scaling3DModeEnum.Fsr2,
            _ => Viewport.Scaling3DModeEnum.Bilinear,
        };
        viewport.ScreenSpaceAA = antiAliasing switch
        {
            AntiAliasingSetting.Fxaa => Viewport.ScreenSpaceAAEnum.Fxaa,
            AntiAliasingSetting.Smaa => Viewport.ScreenSpaceAAEnum.Smaa,
            _ => Viewport.ScreenSpaceAAEnum.Disabled,
        };
        viewport.Msaa3D = antiAliasing switch
        {
            AntiAliasingSetting.Msaa2x => Viewport.Msaa.Msaa2X,
            AntiAliasingSetting.Msaa4x => Viewport.Msaa.Msaa4X,
            AntiAliasingSetting.Msaa8x => Viewport.Msaa.Msaa8X,
            _ => Viewport.Msaa.Disabled,
        };
        viewport.UseTaa = antiAliasing == AntiAliasingSetting.Taa;

        (int directional, int positional, RenderingServer.ShadowQuality filter) = shadowQuality switch
        {
            ShadowQualitySetting.Low => (2048, 2048, RenderingServer.ShadowQuality.SoftVeryLow),
            ShadowQualitySetting.High => (4096, 4096, RenderingServer.ShadowQuality.SoftMedium),
            ShadowQualitySetting.Ultra => (8192, 8192, RenderingServer.ShadowQuality.SoftHigh),
            _ => (4096, 4096, RenderingServer.ShadowQuality.SoftLow), // Medium: Godot's defaults
        };
        RenderingServer.DirectionalShadowAtlasSetSize(directional, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(filter);
        RenderingServer.PositionalSoftShadowFilterSetQuality(filter);
        viewport.PositionalShadowAtlasSize = positional;
    }

    static void Save()
    {
        if (headless) return;
        var file = new ConfigFile();
        file.SetValue("graphics", "window_mode", windowMode.ToString());
        file.SetValue("graphics", "resolution", resolution);
        file.SetValue("graphics", "vsync", vsync);
        file.SetValue("graphics", "max_fps", maxFps);
        file.SetValue("graphics", "render_scale", renderScale);
        file.SetValue("graphics", "upscaler", upscaler.ToString());
        file.SetValue("graphics", "anti_aliasing", antiAliasing.ToString());
        file.SetValue("graphics", "shadow_quality", shadowQuality.ToString());
        file.SetValue("input", "mouse_sensitivity", mouseSensitivity);
        file.SetValue("input", "invert_mouse_y", invertMouseY);
        foreach (string action in rebindable)
        {
            file.SetValue("bindings", action, GetBindings(action).Select(EncodeEvent).Where(s => s != null).ToArray());
        }
        Error error = file.Save(FilePath);
        if (error != Error.Ok) Logging.Warn($"Couldn't save settings to {FilePath}: {error}", "GameSettings");
    }
}
