using Godot;

// TEMPORARY visual check for UIManager notifications; delete after use.
public partial class UITestScene : Node3D
{
    int frame;
    string outDir;
    string mode = "world";

    MeshInstance3D Box(Mesh mesh, Vector3 pos, Color color)
    {
        var mi = new MeshInstance3D { Mesh = mesh, Position = pos, MaterialOverride = new StandardMaterial3D { AlbedoColor = color } };
        AddChild(mi);
        return mi;
    }

    public override void _Ready()
    {
        // GameWorld pauses the tree outside a game.
        ProcessMode = ProcessModeEnum.Always;
        foreach (string a in OS.GetCmdlineUserArgs())
        {
            if (a.StartsWith("--out=")) outDir = a.Substring(6);
            if (a.StartsWith("--mode=")) mode = a.Substring(7);
        }
        AddChild(new WorldEnvironment { Environment = new Godot.Environment { BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color(0.35f, 0.4f, 0.45f), AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = Colors.White, AmbientLightEnergy = 0.6f } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-50, 30, 0) });
        var cam = new Camera3D { Position = new Vector3(0, 1.6f, 6) };
        AddChild(cam);
        cam.Current = true;
        Box(new PlaneMesh { Size = new Vector2(60, 60) }, Vector3.Zero, new Color(0.3f, 0.3f, 0.3f));
        Box(new BoxMesh { Size = new Vector3(4, 3, 0.2f) }, new Vector3(0, 1.5f, 2), new Color(0.6f, 0.55f, 0.5f));
        var hidden = Box(new BoxMesh(), new Vector3(0, 0.5f, 0), Colors.Red);
        hidden.RotationDegrees = new Vector3(0, 30, 0);
        var sphere = Box(new SphereMesh(), new Vector3(3f, 0.5f, 0), Colors.Blue);
        var left = Box(new BoxMesh(), new Vector3(-25, 0.5f, 0), Colors.Green);
        var behind = Box(new BoxMesh(), new Vector3(2, 0.5f, 20), Colors.Purple);

        if (mode == "frontend")
        {
            UIManager.ShowMainMenu();
            return;
        }
        var icon = GD.Load<Texture2D>("res://game/assets/icons/items/battery_cell.png");
        UIManager.ShowMarker(hidden, icon, "Behind the wall", highlight: true);
        UIManager.ShowMarker(sphere, icon, null, highlight: true, color: new Color(0.3f, 0.9f, 1f));
        UIManager.ShowMarker(left, icon, "Off to the left");
        UIManager.ShowMarker(behind, icon, "Behind you");
        Diag($"UITEST ready frame={Engine.GetProcessFrames()} paused={GetTree().Paused}");
        try {
        UIManager.ShowText("Backed text, top centre", HudAnchor.TopCenter, 0);
        UIManager.ShowText("A second one stacks below", HudAnchor.TopCenter, 0);
        UIManager.ShowText("No backing, bottom right", HudAnchor.BottomRight, 0, backing: false, color: Colors.Yellow);
        UIManager.ShowText("Centre left", HudAnchor.CenterLeft, 0);
        UIManager.ShowText("Gone soon", HudAnchor.TopRight, 0.5f);
        Diag("UITEST toasts added");
        } catch (System.Exception e) { Diag("UITEST toast exception " + e); }
    }

    static void Diag(string line) => System.IO.File.AppendAllText("C:/Users/steph/AppData/Local/Temp/claude/C--Users-steph-OneDrive-Documents-godot-projects-gmp-framework/254f81f8-a942-459e-b709-9a60b40c4226/scratchpad/diag.txt", line + System.Environment.NewLine);

    void Shot(string name)
    {
        try
        {
            Error err = GetViewport().GetTexture().GetImage().SavePng($"{outDir}/{name}.png");
            Diag($"UITEST shot {name} {err}");
        }
        catch (System.Exception e) { Diag($"UITEST shot {name} failed {e.Message}"); }
    }

    public override void _Process(double delta)
    {
        frame++;
        if (frame % 10 == 0) Diag($"UITEST frame {frame} paused={GetTree().Paused}");
        if (frame > 150) { GD.Print("UITEST timeout quit"); GetTree().Quit(); return; }
        if (mode == "frontend")
        {
            if (frame == 30) Shot("frontend_main");
            if (frame == 31) UIManager.OpenOptions();
            if (frame == 60) Shot("frontend_options");
            if (frame == 61) UIManager.TopScreen.Cancel();
            if (frame == 90) { Shot("frontend_back"); Diag($"UITEST top={UIManager.TopScreen?.Name} mouse={Input.MouseMode}"); GetTree().Quit(); }
            return;
        }
        if (frame == 30 && OS.GetCmdlineUserArgs().Length > 0 && System.Array.IndexOf(OS.GetCmdlineUserArgs(), "--diag") >= 0)
        {
            Node root = UIManager.instance.GetNode("Notifications/Root");
            Diag($"UITEST root size={((Control)root).Size} scale={((Control)root).Scale} vis={((Control)root).IsVisibleInTree()}");
            foreach (Node stack in root.GetChildren())
            {
                var c = (Control)stack;
                Diag($"UITEST stack {c.Name} pos={c.Position} size={c.Size} children={c.GetChildCount()}");
                foreach (Node t in c.GetChildren())
                {
                    var tc = (Control)t;
                    Diag($"UITEST   toast pos={tc.Position} size={tc.Size} mod={tc.Modulate} vis={tc.IsVisibleInTree()} text={((HudToast)tc).Text}");
                }
            }
            GetTree().Quit();
        }
        else if (frame == 30) Shot("world");
        if (frame == 32) GetTree().Quit();
    }
}
