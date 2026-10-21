using Godot;
using ImGuiNET;
using Limbo.Console.Sharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


public partial class Console : Node
{

    public override void _Ready()
    {
        LimboConsole.RegisterCommand(new Callable(this,"debugui"));
        LimboConsole.AddArgumentAutocompleteSource("debugui", 0, new Callable(this,"GetDebuguiOptions"));
        LimboConsole.RegisterCommand(new Callable(this, "save"));
        LimboConsole.RegisterCommand(new Callable(this, "resync"));
        LimboConsole.RegisterCommand(new Callable(this, "load"));
        LimboConsole.RegisterCommand(new Callable(this, "send_budget"));
        LimboConsole.RegisterCommand(new Callable(this, "status"));
        LimboConsole.RegisterCommand(new Callable(this, "start_game"));
        LimboConsole.RegisterCommand(new Callable(this, "screenshot"));
        LimboConsole.RegisterCommand(new Callable(this, "perf"));
        LimboConsole.RegisterCommand(new Callable(this, "physprof"));
        LimboConsole.RegisterCommand(new Callable(this, "net_sim"));
        LimboConsole.RegisterCommand(new Callable(this, "net_blackout"));
        LimboConsole.RegisterCommand(new Callable(this, "net_flood"));
        LimboConsole.RegisterCommand(new Callable(this, "raycast"));
        // Game-specific commands, in game/scripts/core/Console.Game.cs.
        LimboConsole.RegisterCommand(new Callable(this, "players"));
        LimboConsole.RegisterCommand(new Callable(this, "tp"));
        LimboConsole.RegisterCommand(new Callable(this, "look_at"));
        LimboConsole.RegisterCommand(new Callable(this, "walk"));
        LimboConsole.RegisterCommand(new Callable(this, "jump"));
        LimboConsole.RegisterCommand(new Callable(this, "build_cell"));
        LimboConsole.RegisterCommand(new Callable(this, "build_probe"));
        LimboConsole.RegisterCommand(new Callable(this, "spawn_bodies"));
        LimboConsole.RegisterCommand(new Callable(this, "clear_bodies"));
        LimboConsole.RegisterCommand(new Callable(this, "bodies"));
        LimboConsole.RegisterCommand(new Callable(this, "test_arena"));
        LimboConsole.RegisterCommand(new Callable(this, "interp"));
        LimboConsole.RegisterCommand(new Callable(this, "lightshadows"));
        LimboConsole.RegisterCommand(new Callable(this, "hitstats"));
        LimboConsole.RegisterCommand(new Callable(this, "objects"));
        LimboConsole.RegisterCommand(new Callable(this, "spawn_scene"));
        LimboConsole.RegisterCommand(new Callable(this, "stir"));
        LimboConsole.RegisterCommand(new Callable(this, "repair_all"));
        LimboConsole.RegisterCommand(new Callable(this, "item_counts"));
        LimboConsole.RegisterCommand(new Callable(this, "build"));
        LimboConsole.RegisterCommand(new Callable(this, "drop_items"));
        LimboConsole.RegisterCommand(new Callable(this, "count_items"));

        // Keep debug windows (perf) drawing while the tree is paused, e.g. during world load.
        ProcessMode = ProcessModeEnum.Always;
        // Times each physics tick for the perf log's average; only unpaused ticks count.
        PhysicsTimer timerStart = new PhysicsTimer(true);
        PhysicsTimer timerEnd = new PhysicsTimer(false);
        timerStart.ProcessMode = ProcessModeEnum.Pausable;
        timerEnd.ProcessMode = ProcessModeEnum.Pausable;
        AddChild(timerStart);
        AddChild(timerEnd);

        // "-- --testctl <folder>" lets a test driver run console commands in this instance (see TestControl).
        string[] args = OS.GetCmdlineUserArgs();
        int testFlag = Array.IndexOf(args, "--testctl");
        if (testFlag >= 0 && testFlag + 1 < args.Length)
        {
            AddChild(new TestControl(args[testFlag + 1]));
        }

        // "-- --debugui perf,graphics" opens those windows at startup.
        int flag = Array.IndexOf(args, "--debugui");
        if (flag >= 0 && flag + 1 < args.Length)
        {
            foreach (string name in args[flag + 1].Split(','))
            {
                debugui(name);
            }
        }
    }

    public override void _Process(double delta)
    {
        PerfDebug.Process(delta);
        GraphicsDebug.Process(this, delta);
        ItemsDebug.Process(this, delta);
        QuestsDebug.Process();
    }

    // ---- saves (host only; see GameWorld.Resync.cs) ----

    // Names are files in this user's save folder; the pause menu and lobby browse for any file instead.
    static string SavePath(string name)
    {
        return GameWorld.SaveDir + (name.EndsWith(GameWorld.SaveExtension) ? name : name + GameWorld.SaveExtension);
    }

    public void save()
    {
        string path = GameWorld.SaveDir + GameWorld.DefaultSaveName();
        if (GameWorld.SaveToFile(path)) LimboConsole.Info($"Saved {path}");
        else LimboConsole.Error("Not saved: only the host can save, in a running game.");
    }

    public void resync()
    {
        if (!GameWorld.Resync()) LimboConsole.Error("Can't resync now: host only, in a running game, one at a time.");
    }

    public void load(string name)
    {
        if (!GameWorld.LoadFromFile(SavePath(name))) LimboConsole.Error($"Can't load '{name}': host only, in a running game, and the save must be readable.");
    }

    // ---- network ----

    /// <summary>
    /// Sets <see cref="GameWorld.SendBudget"/> in Mbps, for testing how much gets through. Steam's send rate is set
    /// from it when hosting or joining, so use it before then (on the main menu or lobby screen).
    /// </summary>
    public void send_budget(int mbps)
    {
        GameWorld.SendBudget = mbps * 1000 * 1000 / 8;
        LimboConsole.Info($"State send budget is now {mbps} Mbps. It applies to the next host or join.");
        Logging.Log($"send budget set to {mbps} Mbps", "Perf");
    }

    /// <summary>
    /// Simulated network conditions for LAN (ENetNetwork): lagMs one-way delay on everything this peer sends, plus or
    /// minus jitterMs, and lossPercent of unreliable packets dropped. Set it on every peer. "net_sim 0 0 0" turns it off.
    /// </summary>
    public void net_sim(int lagMs, int jitterMs, float lossPercent)
    {
        ENetNetwork.simLagMs = lagMs;
        ENetNetwork.simJitterMs = jitterMs;
        ENetNetwork.simLossPercent = lossPercent;
        LimboConsole.Info($"net_sim: lag {lagMs} ms ± {jitterMs} ms, loss {lossPercent}% (LAN only, on what this peer sends)");
    }

    /// <summary>
    /// LAN only: this peer stops servicing ENet for that many seconds (nothing sent, received or acknowledged), so its
    /// links time out as if the network went down. For testing link repair.
    /// </summary>
    public void net_blackout(float seconds)
    {
        ENetNetwork.Blackout(seconds);
        LimboConsole.Info($"net_blackout: ENet not serviced for {seconds} s");
    }

    /// <summary>Filler traffic in Mbps from net_flood; 0 is off.</summary>
    int floodMbps;
    static readonly byte[] floodPacket = new byte[1100];

    /// <summary>
    /// Sends this many Mbps of filler to every other peer, to load the transport without needing thousands of bodies:
    /// unreliable 1100-byte packets on a channel nothing listens to, all of each 30 Hz send's share at once, as state
    /// goes out. 0 stops.
    /// </summary>
    public void net_flood(int mbps)
    {
        floodMbps = mbps;
        LimboConsole.Info($"net_flood: {mbps} Mbps of filler to each peer");
    }

    /// <summary>Called each physics tick (Console.Game.cs): sends that tick's share of the flood, as state goes out every tick.</summary>
    void FloodTick()
    {
        if (floodMbps <= 0 || Lobby.MemberCount <= 1)
            return;
        int bytes = floodMbps * 1000 * 1000 / 8 / Engine.PhysicsTicksPerSecond;
        for (; bytes > 0; bytes -= floodPacket.Length)
        {
            Lobby.SendToAllExceptSelf(Channel.GAME_Flood, floodPacket, Network.k_nSteamNetworkingSend_Unreliable);
        }
    }

    /// <summary>Shadows on (1) or off (0) for every light in the scene, to measure what shadow passes cost.</summary>
    public void lightshadows(int on)
    {
        int count = 0;
        foreach (Node node in GetTree().Root.FindChildren("*", "Light3D", true, false))
        {
            if (node is Light3D light)
            {
                light.ShadowEnabled = on != 0;
                count++;
            }
        }
        LimboConsole.Info($"lightshadows: shadows {on != 0} on {count} lights");
    }

    /// <summary>Physics interpolation on (1) or off (0) for the whole game (project setting physics/common/physics_interpolation).</summary>
    public void interp(int on)
    {
        GetTree().PhysicsInterpolation = on != 0;
        LimboConsole.Info($"interp: physics interpolation {on != 0}");
    }

    // ---- testing ----

    /// <summary>A physics ray from one point to another: what it hits first (a body id, and its node if it has one) and where.</summary>
    public void raycast(float fromX, float fromY, float fromZ, float toX, float toY, float toZ)
    {
        RayHit hit = PhysicsWorld.Raycast(new Vector3(fromX, fromY, fromZ), new Vector3(toX, toY, toZ));
        string body = "none";
        Node3D node = PhysicsWorld.BodyNode(hit.id);
        if (node != null)
        {
            body = node.GetPath();
        }
        LimboConsole.Info($"raycast: hit={hit.hit} at={hit.position} normal={hit.normal} id={hit.id} shape={hit.shape} body={body} tags={hit.tags}");
    }

    /// <summary>Host: spawns the scene at path (res://...) at x y z for every peer, the host its authority.</summary>
    public void spawn_scene(string path, float x, float y, float z)
    {
        if (!Lobby.isHost)
        {
            LimboConsole.Error("spawn_scene: host only.");
            return;
        }
        ulong id = GameWorld.SpawnScene(path, new Vector3(x, y, z), default, new GMPOInitData { authority = Lobby.hostID });
        LimboConsole.Info($"spawn_scene: id={id}");
    }

    /// <summary>Every dynamic node body (tools and the like): its type, id, authority and where it is, to compare peers.</summary>
    public void objects()
    {
        foreach (GMPObject obj in GameWorld.syncedObjs.Values)
        {
            if (obj is GMPOBody body && body.bodyType == BodyTypeEnum.Dynamic)
            {
                LimboConsole.Info($"object: {body.GetType()} id={body.id} authority={body.authority} pos={body.GlobalPosition}");
            }
        }
    }

    /// <summary>Box3D's own step profile (ms, last step) and counters.</summary>
    public void physprof()
    {
        if (GameWorld.physics == null)
        {
            LimboConsole.Error("No physics world.");
            return;
        }
        string profile = "";
        foreach (var (key, value) in PhysicsWorld.Profile())
        {
            profile += $" {key}={value}";
        }
        LimboConsole.Info($"physprof:{profile}");
    }

    /// <summary>Logs the latest second's performance sample (the same line the log gets every 5 s in a game).</summary>
    public void perf()
    {
        PerfDebug.LogNow();
    }

    /// <summary>One line of where this instance is: lobby role, peers, whether the game is running, object count, fps.</summary>
    public void status()
    {
        string role = "none";
        if (Lobby.network != null)
        {
            if (Lobby.isHost) role = "host";
            else role = "client";
        }
        LimboConsole.Info($"status: role={role} peers={Lobby.MemberCount} ingame={GameWorld.started} paused={GetTree().Paused} " +
            $"objects={GameWorld.syncedObjs.Count} fps={Engine.GetFramesPerSecond()} level={Lobby.gameInfo.levelIdx}");
    }

    /// <summary>Host, in the lobby: starts GameResources.LevelsList[level] for everyone connected.</summary>
    public void start_game(int level)
    {
        if (!Lobby.isHost || GameWorld.started)
        {
            LimboConsole.Error("Only the host can start a game, from the lobby.");
            return;
        }
        if (level < 0 || level >= GameResources.LevelsList.Count)
        {
            LimboConsole.Error($"No level {level}; there are {GameResources.LevelsList.Count}.");
            return;
        }
        Lobby.gameInfo.levelIdx = level;
        Lobby.SendStartGame();
        LimboConsole.Info($"Starting {GameResources.LevelsList[level].levelName}");
    }

    /// <summary>
    /// Saves what this window shows as a PNG: into the test folder when run by a test driver, otherwise into
    /// user://screenshots. The name is optional.
    /// </summary>
    public void screenshot(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            name = "screenshot_" + DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string folder;
        if (TestControl.folder != null)
            folder = TestControl.folder;
        else
            folder = ProjectSettings.GlobalizePath("user://screenshots");
        System.IO.Directory.CreateDirectory(folder);
        string path = System.IO.Path.Combine(folder, name + ".png");
        Image image = GetViewport().GetTexture().GetImage();
        Error err = image.SavePng(path);
        if (err == Error.Ok) LimboConsole.Info($"screenshot: {path}");
        else LimboConsole.Error($"Screenshot not saved: {err}");
    }

    public void debugui(string which)
    {
        switch (which)
        {
            case "":
                LimboConsole.Error("Must provide name of debugui to toggle.");
                break;
            case "lobby":
                Lobby.displayLobbyDebugInfo = !Lobby.displayLobbyDebugInfo;
                break;
            case "syncedobjects":
                GameWorld.displaySyncedObjectDebugInfo = !GameWorld.displaySyncedObjectDebugInfo;
                break;
            case "grid":
                BuildGrid.Toggle();
                break;
            case "gridhighlight":
                BuildGrid.ToggleHighlight();
                break;
            case "player":
                FactoryPlayer.displayPlayerDebugInfo = !FactoryPlayer.displayPlayerDebugInfo;
                break;
            case "perf":
                PerfDebug.displayPerfDebugInfo = !PerfDebug.displayPerfDebugInfo;
                break;
            case "graphics":
                GraphicsDebug.displayGraphicsDebugInfo = !GraphicsDebug.displayGraphicsDebugInfo;
                break;
            case "items":
                ItemsDebug.displayItemsDebugInfo = !ItemsDebug.displayItemsDebugInfo;
                break;
            case "quests":
                QuestsDebug.displayQuestsDebugInfo = !QuestsDebug.displayQuestsDebugInfo;
                break;
            case "reset":
                foreach (string name in GetDebuguiOptions())
                {
                    ImGui.SetWindowPos("debugui "+name, new System.Numerics.Vector2(0, 0));
                    ImGui.SetWindowSize("debugui " + name, new System.Numerics.Vector2(150, 150));
                }
                break;

        }
    }

    public Godot.Collections.Array GetDebuguiOptions()
    {
        return new Godot.Collections.Array {
            "lobby",
            "syncedobjects",
            "grid",
            "gridhighlight",
            "player",
            "perf",
            "graphics",
            "items",
            "quests",
            "reset",
        };
    }
}

