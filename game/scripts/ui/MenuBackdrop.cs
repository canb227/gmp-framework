using Godot;

/// <summary>
/// Live scenery behind the main menu (in front_end.tscn). It runs a world on this machine alone (a lobby hosted over a
/// <see cref="LocalNetwork"/>, <see cref="GameWorld.StartLocal"/>) and spawns one of <see cref="scenes"/> into it,
/// moving to the next every <see cref="secondsPerScene"/>. A backdrop scene is a small level made for the menu:
/// structures, droppers and lights, no store or quests, and one Camera3D, posed where the view should rest; the mouse
/// shifts and tilts it a little from there. Leaving the front end (hosting, joining) ends the world.
/// </summary>
public partial class MenuBackdrop : Node
{
    /// <summary>The backdrop scenes, shown in order (or at random, <see cref="shuffle"/>).</summary>
    [Export] public Godot.Collections.Array<PackedScene> scenes = new();
    [Export] public double secondsPerScene = 30;
    [Export] public bool shuffle = false;

    /// <summary>How far the camera moves sideways and up at the edge of the screen, in metres.</summary>
    [Export] public float maxShift = 0.3f;
    /// <summary>How far the camera turns at the edge of the screen, in degrees.</summary>
    [Export] public float maxTiltDegrees = 3f;
    /// <summary>How quickly the camera follows the mouse (higher is snappier).</summary>
    [Export] public float followSpeed = 3f;
    /// <summary>Impact sounds play at this share of their in-game volume behind the menu.</summary>
    [Export] public float hitVolume = 0.25f;

    /// <summary>A peer id for the local world; nothing else ever sees it.</summary>
    const ulong LocalPeerID = 1;

    int sceneIndex = -1;
    double sceneTime;
    Camera3D camera;
    Transform3D cameraRest;
    Vector2 smoothedMouse;

    public override void _Ready()
    {
        if (scenes.Count == 0)
        {
            return;
        }
        Lobby.StartHost(new LocalNetwork(LocalPeerID), LocalPeerID, "Menu");
        AudioManager.hitVolumeScale = hitVolume;
        GameWorld.StartLocal();
        ShowNextScene();
    }

    public override void _ExitTree()
    {
        // Only if this backdrop made the session: a scene change away from a running game must not end that game.
        if (Lobby.network is LocalNetwork)
        {
            Lobby.LeaveLobby();
            AudioManager.hitVolumeScale = 1f;
        }
    }

    public override void _Process(double delta)
    {
        if (scenes.Count == 0)
        {
            return;
        }
        sceneTime += delta;
        if (scenes.Count > 1 && sceneTime >= secondsPerScene)
        {
            ShowNextScene();
        }
        if (camera == null || !IsInstanceValid(camera))
        {
            return;
        }
        // The mouse from the middle of the screen: -1 at the left and top edges, +1 at the right and bottom.
        Vector2 size = GetViewport().GetVisibleRect().Size;
        Vector2 mouse = GetViewport().GetMousePosition() / size * 2 - Vector2.One;
        mouse = mouse.Clamp(-Vector2.One, Vector2.One);
        smoothedMouse = smoothedMouse.Lerp(mouse, 1 - Mathf.Exp(-followSpeed * (float)delta));
        camera.GlobalTransform = SwayedTransform(smoothedMouse);
    }

    /// <summary>
    /// The camera's pose for the mouse at <paramref name="mouse"/> (-1..1 on each axis, 0 in the middle of the screen),
    /// made from its resting pose <see cref="cameraRest"/>, moved up to <see cref="maxShift"/> and turned up to
    /// <see cref="maxTiltDegrees"/>.
    /// </summary>
    Transform3D SwayedTransform(Vector2 mouse)
    {
        // Screen y grows downward; up on screen should move and look up.
        float right = mouse.X;
        float up = -mouse.Y;
        // Moves toward the mouse along the camera's own right and up, so it works however the camera is aimed.
        Vector3 origin = cameraRest.Origin + cameraRest.Basis.X.Normalized() * right * maxShift + cameraRest.Basis.Y.Normalized() * up * maxShift;
        // And looks a little toward it: turned about world up (the horizon stays level), then nodded about its own right.
        float tilt = Mathf.DegToRad(maxTiltDegrees);
        Basis basis = cameraRest.Basis.Rotated(Vector3.Up, -right * tilt);
        basis = basis.Rotated(basis.X.Normalized(), up * tilt);
        return new Transform3D(basis, origin);
    }

    void ShowNextScene()
    {
        if (shuffle && scenes.Count > 1)
        {
            // Any scene but the one showing now.
            int next = GD.RandRange(0, scenes.Count - 2);
            if (next >= sceneIndex)
            {
                next++;
            }
            sceneIndex = next;
        }
        else
        {
            sceneIndex = (sceneIndex + 1) % scenes.Count;
        }
        sceneTime = 0;
        GameWorld.ClearWorld();
        GameWorld.SpawnScene(scenes[sceneIndex]);

        camera = null;
        foreach (Node node in GameWorld.physics.FindChildren("*", "Camera3D", true, false))
        {
            camera = (Camera3D)node;
            break;
        }
        if (camera == null)
        {
            Logging.Warn($"Backdrop {scenes[sceneIndex].ResourcePath} has no Camera3D", "MenuBackdrop");
            return;
        }
        camera.MakeCurrent();
        cameraRest = camera.GlobalTransform;
    }
}
