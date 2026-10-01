using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// One physics-testbed segment's controls and measurements:
/// <list type="bullet">
/// <item>Item tuning: Box3D properties applied to every item its <see cref="arm"/> spawns, plus the arm's rate.</item>
/// <item>Belt tuning: friction, restitution and speed of every conveyor in the segment (its sibling Structures).</item>
/// <item>Spawning follows <see cref="lever"/> (off until pulled; see <see cref="PhysicsTestbedMaster"/> for all at once).</item>
/// <item>Throughput: smelted, voided and fallen-off counts and source-to-smelter transit time, shown on the
/// <see cref="statsSign"/> above the segment and in both panels.</item>
/// </list>
/// Values are decided by the lobby host and applied on every peer: an edit asks the host, which broadcasts it. The arm
/// spawns on the host, which then broadcasts the new item's id so every peer tracks it and applies the item values
/// to its own copy (so they still hold if a player grabs it and becomes its authority). Stats are measured on the
/// host (it runs the arm, smelter and void) and broadcast once a second.
/// </summary>
public partial class PhysicsTuningStation : Node3D
{
    [Export] public ScrapArm arm;
    [Export] public BasicSmelter smelter;
    [Export] public ItemVoid itemVoid;
    [Export] public Lever lever;
    /// <summary>Sign above the segment showing its name and throughput.</summary>
    [Export] public Label3D statsSign;
    /// <summary>Item whose scene provides the item panel's defaults (the source's item).</summary>
    [Export] public string defaultsItemID = "scrap_ball";
    /// <summary>A tracked item whose centre drops below this height above the segment's floor has fallen off the line.</summary>
    [Export] public float fallenHeight = 0.3f;

    public enum Group { Items, Belts }

    /// <summary>
    /// One tunable. Item params read their default from the item's scene (<see cref="itemRead"/>) and are applied
    /// to each tracked item (<see cref="itemSet"/>); station params read and apply through the station.
    /// </summary>
    record Param(Group group, string label, float min, float max, float step, bool isToggle,
        Func<GMPOBox3DBody, float> itemRead = null, Action<GMPOBox3DBody, float> itemSet = null,
        Func<PhysicsTuningStation, float> read = null, Action<PhysicsTuningStation> apply = null);

    static readonly Param[] parameters =
    [
        new(Group.Items, "Density", 0.1f, 30f, 0.1f, false, b => ReadShaped(b, "density", b.density), (b, v) => { b.density = v; SetShapes(b, "density", v); }),
        new(Group.Items, "Friction", 0f, 2f, 0.01f, false, b => ReadShaped(b, "friction", b.friction), (b, v) => { b.friction = v; SetShapes(b, "friction", v); }),
        new(Group.Items, "Restitution", 0f, 1f, 0.01f, false, b => ReadShaped(b, "restitution", b.restitution), (b, v) => { b.restitution = v; SetShapes(b, "restitution", v); }),
        new(Group.Items, "Rolling resistance", 0f, 1f, 0.01f, false, b => ReadShaped(b, "rolling_resistance", b.rollingResistance),
            (b, v) => { b.rollingResistance = v; SetShapes(b, "rolling_resistance", v); }),
        new(Group.Items, "Linear damping", 0f, 5f, 0.01f, false, b => b.linearDamping, (b, v) => b.linearDamping = v),
        new(Group.Items, "Angular damping", 0f, 5f, 0.01f, false, b => b.angularDamping, (b, v) => b.angularDamping = v),
        new(Group.Items, "Gravity scale", 0f, 3f, 0.01f, false, b => b.gravityScale, (b, v) => b.gravityScale = v),
        new(Group.Items, "Sleep threshold (m/s)", 0f, 1f, 0.01f, false, b => b.sleepThreshold, (b, v) => b.sleepThreshold = v),
        new(Group.Items, "Can sleep", 0f, 1f, 1f, true, b => b.canSleep ? 1 : 0, (b, v) => b.canSleep = v > 0.5f),
        new(Group.Items, "Continuous (CCD)", 0f, 1f, 1f, true, b => b.continuous ? 1 : 0, (b, v) => b.continuous = v > 0.5f),
        new(Group.Items, "Spawn interval (s)", 0.05f, 5f, 0.05f, false,
            read: s => (float)(s.arm?.timePerOperation ?? 1.0), apply: s => { if (s.arm != null) s.arm.timePerOperation = s.Value("Spawn interval (s)"); }),

        new(Group.Belts, "Flat belt friction", 0f, 3f, 0.01f, false, read: s => s.authoredFlatFriction, apply: s => s.ApplyBelts()),
        new(Group.Belts, "Slope belt friction", 0f, 3f, 0.01f, false, read: s => s.authoredSlopeFriction, apply: s => s.ApplyBelts()),
        new(Group.Belts, "Belt restitution", 0f, 1f, 0.01f, false, read: s => s.authoredBeltRestitution, apply: s => s.ApplyBelts()),
        new(Group.Belts, "Belt speed multiplier", 0f, 3f, 0.05f, false, read: _ => 1f, apply: s => s.ApplyBelts()),
        new(Group.Belts, "Wall friction", 0f, 2f, 0.01f, false, read: s => s.authoredWallFriction, apply: s => s.ApplyBelts()),
    ];

    const double PruneInterval = 0.5;
    const double StatsInterval = 1.0;
    const int TransitWindow = 20;
    const double RateWindow = 60.0;

    /// <summary>Every station in the running level, for <see cref="PhysicsTestbedMaster"/>.</summary>
    public static readonly List<PhysicsTuningStation> all = new();

    static (PhysicsTuningStation station, Group group)? openPanel;

    float[] values;
    float[] defaults;
    readonly List<ulong> tracked = new();

    // Host-side measurements.
    readonly Dictionary<ulong, double> spawnTimes = new();
    readonly HashSet<ulong> fallenIds = new();
    readonly Queue<double> recentTransits = new();
    readonly Queue<double> smeltTimes = new();
    int spawnedTotal, smeltedTotal, fallenTotal;
    double lastTransit;
    double untilPrune, untilStats;

    // Last stats received from the host (what every peer shows).
    string statsText = "";

    // Belts: every belt/wall collider of the segment's conveyors with its authored values.
    record BeltShape(Node3D shape, bool slope, float friction, float restitution, Vector3 tangent);
    record MeshBelt(GMPOBox3DBody body, Godot.Collections.Dictionary[] materials);
    readonly List<BeltShape> beltShapes = new();
    readonly List<MeshBelt> meshBelts = new();
    readonly List<(Node3D shape, float friction)> wallShapes = new();
    float authoredFlatFriction = 0.8f, authoredSlopeFriction = 1.2f, authoredBeltRestitution, authoredWallFriction = 0.1f;

    readonly Dictionary<Group, CanvasLayer> _layers = new();
    readonly List<Label> _statsLabels = new();
    readonly Control[] _controls = new Control[parameters.Length];
    readonly Label[] _valueLabels = new Label[parameters.Length];

    public string title => GetParent()?.Name ?? Name;

    public override void _Ready()
    {
        all.Add(this);
        CollectBelts();
        defaults = ReadDefaults();
        // The defaults are the authored values, so nothing needs applying until the first edit.
        values = (float[])defaults.Clone();
        if (arm != null)
        {
            arm.ItemSpawned += OnArmSpawned;
            arm.running = lever?.pulled ?? false;
        }
        if (lever != null)
        {
            lever.displayName = $"{title} spawner";
            lever.Toggled += OnLeverToggled;
        }
        if (smelter != null)
        {
            smelter.Consumed += OnSmelterConsumed;
        }
        foreach (Group g in Enum.GetValues<Group>())
        {
            BuildPanel(g);
        }
        RefreshStatsText(0, 0, 0, 0, 0, 0, 0, 0);
    }

    public override void _ExitTree()
    {
        all.Remove(this);
        if (arm != null) arm.ItemSpawned -= OnArmSpawned;
        if (lever != null) lever.Toggled -= OnLeverToggled;
        if (smelter != null) smelter.Consumed -= OnSmelterConsumed;
        if (openPanel?.station == this)
        {
            ClosePanel();
        }
    }

    float Value(string label) => values[Array.FindIndex(parameters, p => p.label == label)];

    // ---- item shapes -----------------------------------------------------

    /// <summary>
    /// The Box3DCollisionShape children of <paramref name="body"/> (through plain nodes, not into nested bodies). Once a
    /// body has any, they replace its own shape and each carries its own density, friction, restitution and rolling
    /// resistance, so the item params set those on every shape as well as on the body.
    /// </summary>
    static IEnumerable<Node3D> ItemShapes(Node body)
    {
        foreach (Node child in body.GetChildren())
        {
            if (child.IsClass("Box3DBody"))
            {
                continue;
            }
            if (child is Node3D shape && shape.GetClass() == "Box3DCollisionShape")
            {
                yield return shape;
            }
            foreach (Node3D nested in ItemShapes(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>The value the item actually simulates with: its first child shape's, or the body's own without any.</summary>
    static float ReadShaped(GMPOBox3DBody body, string property, float own)
    {
        Node3D first = ItemShapes(body).FirstOrDefault();
        return first != null ? first.Get(property).AsSingle() : own;
    }

    static void SetShapes(GMPOBox3DBody body, string property, float value)
    {
        foreach (Node3D shape in ItemShapes(body))
        {
            shape.Set(property, value);
        }
    }

    // ---- measurement -----------------------------------------------------

    public override void _PhysicsProcess(double delta)
    {
        untilPrune -= delta;
        if (untilPrune <= 0)
        {
            untilPrune = PruneInterval;
            tracked.RemoveAll(id => !GameWorld.syncedObjs.ContainsKey(id));
            if (Lobby.isHost)
            {
                MeasureFallen();
            }
        }

        untilStats -= delta;
        if (untilStats <= 0 && Lobby.isHost)
        {
            untilStats = StatsInterval;
            double now = Now();
            while (smeltTimes.Count > 0 && now - smeltTimes.Peek() > RateWindow)
            {
                smeltTimes.Dequeue();
            }
            float avgTransit = recentTransits.Count > 0 ? (float)recentTransits.Average() : 0f;
            RPCManager.RPC(this, nameof(_Stats), [tracked.Count, spawnedTotal, smeltedTotal, smeltTimes.Count,
                itemVoid?.despawnedCount ?? 0, fallenTotal, avgTransit, (float)lastTransit]);
        }
    }

    void MeasureFallen()
    {
        float floorY = GlobalPosition.Y + fallenHeight;
        foreach (ulong id in tracked)
        {
            if (!fallenIds.Contains(id) && GameWorld.syncedObjs.TryGetValue(id, out GMPObject g)
                && g is Node3D n && n.GlobalPosition.Y < floorY)
            {
                fallenIds.Add(id);
                fallenTotal++;
            }
        }
        fallenIds.RemoveWhere(id => !GameWorld.syncedObjs.ContainsKey(id));
    }

    // Host: the smelter took an item in; if it came from this segment's arm, record its transit time.
    void OnSmelterConsumed(ulong id)
    {
        if (!spawnTimes.Remove(id, out double spawnedAt))
        {
            return;
        }
        double now = Now();
        smeltedTotal++;
        smeltTimes.Enqueue(now);
        lastTransit = now - spawnedAt;
        recentTransits.Enqueue(lastTransit);
        if (recentTransits.Count > TransitWindow)
        {
            recentTransits.Dequeue();
        }
    }

    [RPC(requireAuthority = true)]
    private void _Stats(int alive, int spawned, int smelted, int smeltedLastMinute, int voided, int fallen, float avgTransit, float lastTransitTime)
    {
        RefreshStatsText(alive, spawned, smelted, smeltedLastMinute, voided, fallen, avgTransit, lastTransitTime);
    }

    void RefreshStatsText(int alive, int spawned, int smelted, int smeltedLastMinute, int voided, int fallen, float avgTransit, float lastTransitTime)
    {
        string transit = smelted > 0 ? $"{avgTransit:0.0} s avg (last {lastTransitTime:0.0} s)" : "-";
        statsText = $"smelted {smelted}  ({smeltedLastMinute} in last 60 s)\n"
            + $"transit {transit}\n"
            + $"fell off {fallen}   voided {voided}\n"
            + $"alive {alive}   spawned {spawned}";
        if (statsSign != null)
        {
            string state = arm?.running == true ? "ON" : "off";
            statsSign.Text = $"{title}  [{state}]\n{statsText}";
        }
        foreach (Label l in _statsLabels)
        {
            l.Text = statsText;
        }
    }

    static double Now() => Time.GetTicksMsec() / 1000.0;

    // ---- defaults and belts ----------------------------------------------

    float[] ReadDefaults()
    {
        float[] d = new float[parameters.Length];
        GMPOBox3DBody template = ItemInfo.Fetch(defaultsItemID)?.droppedScene?.Instantiate() as GMPOBox3DBody;
        for (int i = 0; i < parameters.Length; i++)
        {
            Param p = parameters[i];
            if (p.itemRead != null && template != null)
            {
                d[i] = p.itemRead(template);
            }
            else if (p.read != null)
            {
                d[i] = p.read(this);
            }
        }
        template?.Free();
        return d;
    }

    /// <summary>Finds the belt and wall colliders of every conveyor beside this station and remembers their authored values.</summary>
    void CollectBelts()
    {
        bool flatSeen = false, slopeSeen = false, wallSeen = false;
        foreach (Node sibling in GetParent().GetChildren())
        {
            if (sibling is not Structure s || s.flowArrow == FlowArrow.None)
            {
                continue; // only conveyors carry a flow arrow
            }
            bool slope = s.cellOffsets.Count > 1;
            if (s.shapeType == ShapeTypeEnum.Mesh)
            {
                // Turns: the belt is the body's own mesh collider, one surface material per segment.
                meshBelts.Add(new MeshBelt(s, s.surfaceMaterials.Select(m => m.Duplicate()).ToArray()));
            }
            foreach (Node n in s.FindChildren("*", "", true, false))
            {
                if (n is not Node3D shape || shape.GetClass() != "Box3DCollisionShape")
                {
                    continue;
                }
                string name = shape.Name;
                float friction = shape.Get("friction").AsSingle();
                if (name.StartsWith("Belt"))
                {
                    float restitution = shape.Get("restitution").AsSingle();
                    beltShapes.Add(new BeltShape(shape, slope, friction, restitution, shape.Get("tangent_velocity").AsVector3()));
                    if (slope && !slopeSeen) { authoredSlopeFriction = friction; slopeSeen = true; }
                    if (!slope && !flatSeen) { authoredFlatFriction = friction; authoredBeltRestitution = restitution; flatSeen = true; }
                }
                else if (name.StartsWith("Wall") || name.StartsWith("Lip"))
                {
                    wallShapes.Add((shape, friction));
                    if (!wallSeen) { authoredWallFriction = friction; wallSeen = true; }
                }
            }
        }
    }

    /// <summary>Re-applies the belt values to every collected collider, from their authored values.</summary>
    void ApplyBelts()
    {
        if (values == null)
        {
            return;
        }
        float flat = Value("Flat belt friction"), slope = Value("Slope belt friction");
        float restitution = Value("Belt restitution"), speed = Value("Belt speed multiplier"), wall = Value("Wall friction");
        foreach (BeltShape b in beltShapes)
        {
            b.shape.Set("friction", b.slope ? slope : flat);
            b.shape.Set("restitution", restitution);
            b.shape.Set("tangent_velocity", b.tangent * speed);
        }
        foreach (MeshBelt m in meshBelts)
        {
            for (int i = 0; i < m.materials.Length; i++)
            {
                var mat = m.materials[i].Duplicate();
                mat["friction"] = flat;
                mat["restitution"] = restitution;
                mat["tangent_velocity"] = m.materials[i]["tangent_velocity"].AsVector3() * speed;
                m.body.SetMeshMaterial(i, mat);
            }
        }
        foreach ((Node3D shape, float _) in wallShapes)
        {
            shape.Set("friction", wall);
        }
    }

    // ---- networking ------------------------------------------------------

    /// <summary>Asks the host to set parameter <paramref name="index"/> for everyone.</summary>
    public void RequestSet(int index, float value)
    {
        RPCManager.RPCTo(Lobby.hostID, this, nameof(_RequestSet), [index, value]);
    }

    [RPC]
    private void _RequestSet(int index, float value)
    {
        if (index < 0 || index >= parameters.Length)
        {
            return;
        }
        Param p = parameters[index];
        RPCManager.RPC(this, nameof(_Set), [index, Mathf.Clamp(value, p.min, p.max)]);
    }

    [RPC(requireAuthority = true)]
    private void _Set(int index, float value)
    {
        values[index] = value;
        Param p = parameters[index];
        if (p.itemSet != null)
        {
            foreach (ulong id in tracked)
            {
                if (GameWorld.syncedObjs.TryGetValue(id, out GMPObject g) && g is GMPOBox3DBody body)
                {
                    p.itemSet(body, value);
                }
            }
        }
        p.apply?.Invoke(this);
        if (_controls[index] is HSlider slider)
        {
            slider.SetValueNoSignal(value);
        }
        else if (_controls[index] is CheckBox check)
        {
            check.SetPressedNoSignal(value > 0.5f);
        }
        if (_valueLabels[index] != null)
        {
            _valueLabels[index].Text = FormatValue(index, value);
        }
    }

    // Every peer: the lever decides whether the arm runs.
    void OnLeverToggled(bool pulled)
    {
        if (arm != null)
        {
            arm.running = pulled;
        }
        if (statsSign != null)
        {
            statsSign.Text = $"{title}  [{(pulled ? "ON" : "off")}]\n{statsText}";
        }
    }

    // Host: the arm just spawned an item.
    void OnArmSpawned(ulong id)
    {
        spawnTimes[id] = Now();
        spawnedTotal++;
        RPCManager.RPC(this, nameof(_Track), [id]);
    }

    [RPC(requireAuthority = true)]
    private void _Track(ulong id)
    {
        if (!GameWorld.syncedObjs.TryGetValue(id, out GMPObject g) || g is not GMPOBox3DBody body)
        {
            return;
        }
        tracked.Add(id);
        for (int i = 0; i < parameters.Length; i++)
        {
            parameters[i].itemSet?.Invoke(body, values[i]);
        }
    }

    public void RequestClear()
    {
        RPCManager.RPCTo(Lobby.hostID, this, nameof(_RequestClear), []);
    }

    // Host: despawns every item this segment spawned that is still around.
    [RPC]
    private void _RequestClear()
    {
        foreach (ulong id in tracked.ToArray())
        {
            if (GameWorld.syncedObjs.ContainsKey(id))
            {
                GameWorld.DespawnObject(id);
            }
        }
    }

    // ---- panels ----------------------------------------------------------

    public bool IsPanelOpen(Group group) => openPanel is { } o && o.station == this && o.group == group;

    public void TogglePanel(Group group)
    {
        if (IsPanelOpen(group))
        {
            ClosePanel();
            return;
        }
        ClosePanel();
        openPanel = (this, group);
        _layers[group].Show();
        Input.MouseMode = Input.MouseModeEnum.Visible;
    }

    public static void ClosePanel()
    {
        if (openPanel is not { } o)
        {
            return;
        }
        openPanel = null;
        o.station._layers[o.group].Hide();
        Input.MouseMode = Input.MouseModeEnum.Captured;
    }

    public override void _Input(InputEvent @event)
    {
        if (openPanel?.station == this && @event is InputEventKey key && key.Pressed && key.Keycode == Key.Escape)
        {
            ClosePanel();
            GetViewport().SetInputAsHandled();
        }
    }

    void BuildPanel(Group group)
    {
        var layer = new CanvasLayer { Layer = 10, Visible = false };
        AddChild(layer);
        _layers[group] = layer;

        // Full-screen catcher so clicks outside the panel don't reach the player (which would recapture the mouse).
        var blocker = new Control { MouseFilter = Control.MouseFilterEnum.Stop };
        blocker.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(blocker);

        var panel = new PanelContainer();
        panel.SetAnchorsPreset(Control.LayoutPreset.CenterRight);
        panel.GrowHorizontal = Control.GrowDirection.Begin;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.OffsetRight = -24;
        panel.CustomMinimumSize = new Vector2(460, 0);
        blocker.AddChild(panel);

        var margin = new MarginContainer();
        foreach (string side in new[] { "left", "right", "top", "bottom" })
        {
            margin.AddThemeConstantOverride($"margin_{side}", 12);
        }
        panel.AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        margin.AddChild(column);

        string heading = group == Group.Items ? "item tuning" : "belt tuning";
        column.AddChild(new Label { Text = $"{title} {heading}", HorizontalAlignment = HorizontalAlignment.Center });
        var stats = new Label { HorizontalAlignment = HorizontalAlignment.Center, Text = statsText };
        _statsLabels.Add(stats);
        column.AddChild(stats);
        column.AddChild(new HSeparator());

        var grid = new GridContainer { Columns = 3 };
        grid.AddThemeConstantOverride("h_separation", 10);
        column.AddChild(grid);

        for (int i = 0; i < parameters.Length; i++)
        {
            Param p = parameters[i];
            if (p.group != group)
            {
                continue;
            }
            int index = i;
            grid.AddChild(new Label { Text = p.label });
            Control control;
            if (p.isToggle)
            {
                var check = new CheckBox { ButtonPressed = values[i] > 0.5f };
                check.Toggled += on => RequestSet(index, on ? 1 : 0);
                control = check;
            }
            else
            {
                var slider = new HSlider
                {
                    MinValue = p.min, MaxValue = p.max, Step = p.step, Value = values[i],
                    CustomMinimumSize = new Vector2(180, 0), SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                };
                slider.ValueChanged += v =>
                {
                    _valueLabels[index].Text = FormatValue(index, (float)v);
                    RequestSet(index, (float)v);
                };
                control = slider;
            }
            grid.AddChild(control);
            _controls[i] = control;
            _valueLabels[i] = new Label { Text = FormatValue(i, values[i]), CustomMinimumSize = new Vector2(56, 0) };
            grid.AddChild(_valueLabels[i]);
        }

        column.AddChild(new HSeparator());
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        buttons.AddThemeConstantOverride("separation", 8);
        column.AddChild(buttons);
        AddButton(buttons, "Reset defaults", () =>
        {
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].group == group)
                {
                    RequestSet(i, defaults[i]);
                }
            }
        });
        AddButton(buttons, "Clear items", RequestClear);
        AddButton(buttons, "Close (Esc)", ClosePanel);
    }

    static void AddButton(Container parent, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        parent.AddChild(button);
    }

    static string FormatValue(int index, float value)
    {
        return parameters[index].isToggle ? (value > 0.5f ? "on" : "off") : value.ToString("0.###");
    }
}
