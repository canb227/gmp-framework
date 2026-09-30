using Godot;

/// <summary>
/// Physics-testbed performance readout (top-left): frame and physics timings, and how many synced items exist and
/// how many of those this peer simulates awake. Toggle with F3.
/// </summary>
public partial class PhysicsTestbedHud : CanvasLayer
{
    const double RefreshInterval = 0.25;

    Label _label;
    double _untilRefresh;
    double _worstFrameMs;

    public override void _Ready()
    {
        _label = new Label { Position = new Vector2(12, 12) };
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.F3)
        {
            Visible = !Visible;
        }
    }

    public override void _Process(double delta)
    {
        _worstFrameMs = Mathf.Max(_worstFrameMs, delta * 1000);
        _untilRefresh -= delta;
        if (!Visible || _untilRefresh > 0)
        {
            return;
        }
        _untilRefresh = RefreshInterval;

        int items = 0, simulated = 0, awake = 0;
        foreach (GMPObject g in GameWorld.syncedObjs.Values)
        {
            if (g is not PhysicalFactoryItem item)
            {
                continue;
            }
            items++;
            if (item.authority == Lobby.selfPeerID)
            {
                simulated++;
                if (!item.asleep)
                {
                    awake++;
                }
            }
        }

        _label.Text =
            $"FPS {Engine.GetFramesPerSecond():0}   worst frame {_worstFrameMs:0.0} ms\n" +
            $"process {Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000:0.00} ms   " +
            $"physics {Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000:0.00} ms\n" +
            $"items {items}   simulated here {simulated}   awake {awake}\n" +
            $"synced objects {GameWorld.syncedObjs.Count}   draw calls {Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame):0}\n" +
            "F3 hide";
        _worstFrameMs = 0;
    }
}
